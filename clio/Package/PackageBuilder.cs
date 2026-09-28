namespace Clio.Package
{
	using System;
	using System.Net.Http;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;
	using Clio.CreatioModel;

	#region Interface: IPackageBuilder

	public interface IPackageBuilder
	{

		#region Methods: Public

		/// <summary>
		/// Builds the packages incrementally, one request per package.
		/// </summary>
		/// <param name="packagesNames">Names of the packages to build.</param>
		/// <exception cref="PackageCompilationException">Creatio reported that a package build failed.</exception>
		void Build(IEnumerable<string> packagesNames);

		/// <summary>
		/// Rebuilds the packages, returning as soon as the environment's compilation activity pauses.
		/// </summary>
		/// <param name="packagesNames">Names of the packages to rebuild.</param>
		/// <exception cref="PackageCompilationException">Creatio reported that a package build failed.</exception>
		void Rebuild(IEnumerable<string> packagesNames);

		/// <summary>
		/// Rebuilds the packages and, when <paramref name="waitOptions"/> is supplied, blocks until the environment
		/// has finished building each one instead of returning when the build was accepted.
		/// </summary>
		/// <param name="packagesNames">Names of the packages to rebuild.</param>
		/// <param name="waitOptions">
		/// How long to wait for each build to finish; <see langword="null"/> behaves like
		/// <see cref="Rebuild(IEnumerable{string})"/>.
		/// </param>
		/// <remarks>
		/// A failure answer ends the build at once - it carries Creatio's own verdict and compiler diagnostics.
		/// A success answer does not: the build has finished only once compilation history has recorded at
		/// least one row for it and then stayed quiet long enough that no further project is plausibly still
		/// building. When the environment never answers, the history decides the same way. A build that
		/// leaves no history row within the timeout fails with <see cref="TimeoutException"/>.
		/// </remarks>
		/// <exception cref="PackageCompilationException">Creatio reported that a package build failed.</exception>
		/// <exception cref="TimeoutException">A build did not finish within <see cref="PackageCompilationWaitOptions.Timeout"/>.</exception>
		void Rebuild(IEnumerable<string> packagesNames, PackageCompilationWaitOptions waitOptions);

		#endregion

	}

	#endregion

	#region Class: PackageBuilder

	public class PackageBuilder : IPackageBuilder
	{

		#region Constants: Private

		private const int CompilationSettleSeconds = 5;
		private const int CompilationTimeoutMinutes = 10;

		#endregion

		#region Constants: Internal

		/// <summary>
		/// How long compilation history must stay quiet before a WAITED build whose request ended without an
		/// answer is considered finished.
		/// </summary>
		/// <remarks>
		/// The default path settles after <see cref="CompilationSettleSeconds"/>, which is shorter than the
		/// measured gaps between two projects of one build (about 31-33 s on a live stand, see
		/// <see cref="CompilationSettleTracker.BaseQuietWindowSeconds"/>). That is what lets the default path
		/// return while a later package is still compiling, and why a waited build uses the tracker's window
		/// instead - scaled up once a slower project than the window has been seen.
		/// </remarks>
		internal static readonly TimeSpan WaitSettleWindow =
			TimeSpan.FromSeconds(CompilationSettleTracker.BaseQuietWindowSeconds);

		/// <summary>
		/// How long compilation history must stay quiet before a WAITED build whose request is still open is
		/// considered finished anyway.
		/// </summary>
		/// <remarks>
		/// While the request is open its answer is the better evidence, because it carries the verdict. This
		/// only ends the wait when an intermediary holds the connection open and the answer never arrives;
		/// it is the same five minutes <c>compile-configuration</c> uses for that case.
		/// </remarks>
		internal static readonly TimeSpan WaitQuietFallback = TimeSpan.FromMinutes(5);

		/// <summary>How often the wait loop re-reads the request and the observed history.</summary>
		internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;
		private readonly ICompilationHistoryPoller _compilationHistoryPoller;
		private readonly TimeProvider _timeProvider;
		private readonly ICancellableDelay _delay;

		#endregion

		#region Constructors: Public

		public PackageBuilder(EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger, TimeProvider timeProvider, ICancellableDelay delay,
			ICompilationHistoryPoller compilationHistoryPoller = null) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			logger.CheckArgumentNull(nameof(logger));
			timeProvider.CheckArgumentNull(nameof(timeProvider));
			delay.CheckArgumentNull(nameof(delay));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
			_compilationHistoryPoller = compilationHistoryPoller;
			// The clock and the wait are seams, not Thread.Sleep and DateTime.UtcNow, so a test can run a
			// ten-minute waited build in simulated time instead of spending real seconds racing a poll thread.
			_timeProvider = timeProvider;
			_delay = delay;
		}

		#endregion

		#region Methods: Private

		private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

		private void Pause() => _delay.WaitOrCancelled(PollInterval, CancellationToken.None);

		private static string CreateRequestData(string packageName) => "{ \"packageName\":\"" + packageName + "\" }";

		private IOwnedApplicationClient CreateClient() => _applicationClientFactory.CreateOwnedClient(_environmentSettings);

		private string GetSafePackageName(string packageName) =>
			packageName
				.Replace(" ", string.Empty)
				.Replace(",", "\",\"");

		private void Compilation(IEnumerable<string> packagesNames, bool force,
			PackageCompilationWaitOptions waitOptions = null) {
			string compilationName = force ? "rebuild" : "build";
			string fullBuildPackageUrl = _serviceUrlBuilder.Build(
				force
				? ServiceUrlBuilder.KnownRoute.RebuildPackage
				: ServiceUrlBuilder.KnownRoute.BuildPackage);

			foreach (string packageName in packagesNames) {
				string safePackageName = GetSafePackageName(packageName);
				_logger.WriteLine($"Start {compilationName} packages ({safePackageName}).");
				string requestData = CreateRequestData(safePackageName);

				if (_compilationHistoryPoller is not null) {
					CompileWithPolling(fullBuildPackageUrl, requestData, safePackageName, waitOptions, suggestWait: force);
				} else {
					using IOwnedApplicationClient applicationClient = CreateClient();
					string responseBody = applicationClient.ExecutePostRequest(fullBuildPackageUrl, requestData);
					ThrowIfUnrecognizedBody(safePackageName, responseBody);
					ApplyVerdict(new BuildVerdictEvidence(safePackageName,
						PackageBuildResultParser.TryParseResponse(responseBody), History: null,
						CompletionInferred: false, Waited: waitOptions is not null, SuggestWait: force));
				}

				_logger.WriteLine($"End {compilationName} packages ({safePackageName}).");
			}
		}

		/// <summary>
		/// Decides from compilation history alone whether the build has finished.
		/// </summary>
		/// <param name="observed">What the poll thread has observed so far.</param>
		/// <param name="waited">Whether the caller asked to wait for the build to finish.</param>
		/// <param name="requestEnded">Whether the build request has ended without an answer.</param>
		/// <returns><see langword="true"/> when the history has been quiet for the applicable window.</returns>
		/// <remarks>
		/// Nothing is concluded before the first row: an empty history is a build that has not started
		/// writing yet, not one that finished.
		/// </remarks>
		private bool HasSettled(CompilationProgressSnapshot observed, bool waited, bool requestEnded) {
			if (!observed.LastActivityAt.HasValue) {
				return false;
			}
			TimeSpan quiet = UtcNow() - observed.LastActivityAt.Value;
			return quiet >= RequiredQuietWindow(observed, waited, requestEnded);
		}

		/// <summary>
		/// How long compilation history has to stay quiet before the build counts as finished.
		/// </summary>
		/// <param name="observed">What the poll thread has observed so far.</param>
		/// <param name="waited">Whether the caller asked to wait for the build to finish.</param>
		/// <param name="requestEnded">Whether the build request has ended.</param>
		/// <remarks>
		/// The window is deliberately NOT shortened to fit the remaining <c>--wait-timeout</c>: concluding on a
		/// shorter quiet spell than the slowest project suggests is the premature success of issue #1632. A
		/// build that finishes too close to the deadline times out instead, and the timeout says so.
		/// </remarks>
		private static TimeSpan RequiredQuietWindow(CompilationProgressSnapshot observed, bool waited,
			bool requestEnded) {
			if (!waited) {
				return TimeSpan.FromSeconds(CompilationSettleSeconds);
			}
			// An error row settles on the short window even while the request is open: a compile error stops
			// the build (the same rule CompilationSettleTracker applies), and on a loaded stand the failure
			// answer was measured arriving minutes after the row that already carried the diagnostics.
			if (!requestEnded && !observed.HasErrors) {
				return WaitQuietFallback;
			}
			TimeSpan scaled = TimeSpan.FromSeconds(
				observed.SlowestDurationSeconds * CompilationSettleTracker.DurationScaleFactor);
			return scaled > WaitSettleWindow ? scaled : WaitSettleWindow;
		}

		/// <summary>
		/// Concludes a build whose request has been answered, unless it is a waited build that must keep
		/// being observed.
		/// </summary>
		/// <param name="answered">The answer and how the build was requested.</param>
		/// <param name="progress">What the poll thread has observed so far.</param>
		/// <param name="endMonitoring">Stops the poll thread and releases the request.</param>
		/// <returns><see langword="true"/> when the build was concluded (successfully, or by throwing).</returns>
		/// <remarks>
		/// A waited build does NOT stop at a success answer: a host that compiles in the background can
		/// answer "accepted" while projects are still being built (issue #1632), so it is concluded only once
		/// neither the answer nor the history is more recent than the wait window. A failure answer is final -
		/// Creatio does not report a build as failed and then keep building it.
		/// </remarks>
		private bool TryConcludeOnAnswer(AnsweredBuild answered, CompilationProgress progress, Action endMonitoring) {
			if (PackageBuildResultParser.IsUnrecognizedBody(answered.Body)) {
				endMonitoring();
				ThrowIfUnrecognizedBody(answered.PackageName, answered.Body);
			}
			PackageBuildResult response = PackageBuildResultParser.TryParseResponse(answered.Body);
			CompilationProgressSnapshot observed = progress.Snapshot();
			bool final = !answered.Waited || response is { Success: false }
				|| HasSettledSince(observed, answered.AnsweredAt);
			if (!final) {
				return false;
			}
			endMonitoring();
			ApplyVerdict(new BuildVerdictEvidence(answered.PackageName, response, progress.Snapshot(),
				CompletionInferred: false, Waited: answered.Waited, SuggestWait: answered.SuggestWait));
			return true;
		}

		/// <summary>
		/// Decides whether a waited build that has answered "success" has also stopped building.
		/// </summary>
		/// <param name="observed">What the poll thread has observed so far.</param>
		/// <param name="responseAt">When the success answer was first seen.</param>
		/// <returns>
		/// <see langword="true"/> when at least one history row has been observed and neither the answer nor
		/// any history row is more recent than the wait window.
		/// </returns>
		/// <remarks>
		/// The answer alone is never completion evidence: a .NET 8 host answers "success" on acceptance and
		/// writes its first history row 60-120 s later (issue #1632). Without a row the build keeps being
		/// observed and, if none ever arrives, ends in a timeout rather than an inferred success.
		/// </remarks>
		private bool HasSettledSince(CompilationProgressSnapshot observed, DateTime responseAt) {
			if (observed.LastActivityAt is not { } activity) {
				return false;
			}
			DateTime lastEvidence = activity > responseAt ? activity : responseAt;
			return HasSettled(observed with { LastActivityAt = lastEvidence }, waited: true, requestEnded: true);
		}

		/// <summary>
		/// Turns what is known about a finished build into success, or a <see cref="PackageCompilationException"/>.
		/// </summary>
		/// <param name="evidence">The response verdict (if any) and the history observations (if any).</param>
		/// <remarks>
		/// The response verdict and the history both have to be clean: under-claiming success is the safe
		/// direction when either reports an error, the same rule <c>compile-configuration</c> applies.
		/// When the environment gave no verdict the history decides alone, and the user is told so, because
		/// a success concluded that way is weaker evidence than Creatio's own answer.
		/// </remarks>
		private void ApplyVerdict(BuildVerdictEvidence evidence) {
			CompilationProgressSnapshot history = evidence.History;
			bool historyFailed = history?.HasErrors == true;
			PackageBuildResult response = evidence.Response;
			if (response is null) {
				WarnVerdictMissing(evidence, historyFailed);
				if (historyFailed) {
					Fail(evidence.PackageName, null, history.Diagnostics, null, history.ErrorDetails);
				}
				return;
			}
			if (response.Success && !historyFailed) {
				return;
			}
			List<PackageBuildDiagnostic> responseErrors = response.Errors.ToList();
			IEnumerable<PackageBuildDiagnostic> diagnostics = responseErrors.Count > 0
				? responseErrors
				: history?.Diagnostics ?? [];
			Fail(evidence.PackageName, response.BuildResult, diagnostics, response.ErrorMessage, history?.ErrorDetails);
		}

		/// <summary>
		/// Tells the user that the outcome is not Creatio's own verdict, and why.
		/// </summary>
		private void WarnVerdictMissing(BuildVerdictEvidence evidence, bool historyFailed) {
			if (!evidence.CompletionInferred) {
				_logger.WriteWarning(
					$"The environment did not report a build result for '{evidence.PackageName}' (empty or "
					+ "unrecognized response); the outcome is taken from the compilation history only.");
				return;
			}
			if (evidence.Waited) {
				_logger.WriteWarning(
					$"The environment never answered the build request for '{evidence.PackageName}'; completion "
					+ "was inferred from its compilation history having stopped.");
				return;
			}
			if (!historyFailed && evidence.SuggestWait) {
				_logger.WriteWarning(
					$"The environment has not reported the build result for '{evidence.PackageName}' yet. Completion "
					+ $"was inferred from {CompilationSettleSeconds} s without new compilation history, so a later "
					+ "project may still be building and a compile error in it would not be seen here. Run "
					+ "`clio compile-package --wait` to block until the build finishes.");
			}
		}

		/// <summary>
		/// Writes the diagnostics, then throws the failure summary.
		/// </summary>
		private void Fail(string packageName, int? buildResult, IEnumerable<PackageBuildDiagnostic> diagnostics,
			string errorMessage, string rawHistoryErrors) {
			List<PackageBuildDiagnostic> errors = diagnostics.Where(diagnostic => !diagnostic.IsWarning).ToList();
			foreach (PackageBuildDiagnostic diagnostic in errors) {
				_logger.WriteError(diagnostic.ToString());
			}
			if (errors.Count == 0 && !string.IsNullOrWhiteSpace(rawHistoryErrors)) {
				// The history payload was not in the expected shape; show it as it came rather than lose it.
				_logger.WriteError(rawHistoryErrors);
			}
			string resultPart = buildResult is { } value ? $" (build result {value})" : string.Empty;
			string messagePart = string.IsNullOrWhiteSpace(errorMessage)
				? string.Empty
				: $" Creatio reported: {errorMessage.Trim().TrimEnd('.')}.";
			throw new PackageCompilationException(
				$"Package compilation failed for '{packageName}'{resultPart}.{messagePart} The new code was not "
				+ "loaded: the environment keeps running the previous build until the errors are fixed and the "
				+ "package is compiled again.");
		}

		/// <summary>
		/// Reports one compilation-history row, so the output names the projects that were built meanwhile.
		/// </summary>
		private void ReportHistoryRow(CompilationHistory record) {
			bool failed = CompilationProgress.IsErrorRow(record);
			// Worded as what the history shows, not as "this build rebuilt X": another trigger (an OData
			// rebuild, a second client) can write a row inside the same window.
			_logger.WriteInfo(
				$"Compilation history: {record.ProjectName} built in {record.DurationInSeconds} s, "
				+ (failed ? "failed" : "succeeded"));
		}

		/// <summary>
		/// Ends the monitoring of a response-less compile: stops the poll thread and cancels, then
		/// OBSERVES, the pending HTTP request.
		/// </summary>
		/// <remarks>
		/// Every exit from the wait loop has to do all three, in this order - leaving the cancelled request
		/// unobserved raises an unhandled task exception later, and disposing the token source while the poll
		/// thread still holds its token throws ObjectDisposedException. Extracted so no exit path can carry
		/// only part of the sequence.
		/// </remarks>
		private static void StopMonitoring(CancellationTokenSource cts, Thread pollThread, Task httpTask) {
			cts.Cancel();
			JoinPollThread(pollThread);
			ObserveCancelledRequest(httpTask, cts);
		}

		/// <summary>
		/// Joins the poll thread under the same bound <see cref="CompilationActivityWatcher.StopJoinTimeout"/>
		/// puts on its own poll thread.
		/// </summary>
		/// <remarks>
		/// An UNBOUNDED join is the #1422 hang: <c>PollOnce</c> is a synchronous ATF.Repository call with no
		/// timeout and no cancellation token, so a stand that accepts the connection and never answers pins
		/// build-package past its own 10-minute timeout indefinitely. Cancelling the token does not help - it
		/// is only read BETWEEN rounds. Issue #1376 extends the poll thread's failing lifetime from about
		/// 10 s to about 93 s, which widens the window this can be observed in, so the bound
		/// CompilationActivityWatcher already carries is ported across rather than walked past (PR #1477
		/// review). A thread left behind is a background thread reading a cancelled token; it cannot keep the
		/// process alive, and the compile itself is unaffected either way - the server keeps compiling.
		/// </remarks>
		/// <param name="pollThread">The history-poll thread to join.</param>
		private static void JoinPollThread(Thread pollThread) =>
			pollThread.Join(CompilationActivityWatcher.StopJoinTimeout);

		/// <summary>
		/// Reads the compilation-history baseline, degrading to <c>null</c> when the read fails.
		/// </summary>
		/// <remarks>
		/// ClassifyingDataProvider turns a failed OData round into an exception instead of an empty list, so an
		/// unguarded read here would abort the build before the compilation request is ever sent - one transient
		/// OData failure would fail a compile that would otherwise have succeeded. The baseline only sharpens
		/// which history rows count as new (Poll falls back to DateTime.MinValue without it), so a failed read is
		/// reported as a warning and the compilation goes ahead.
		/// </remarks>
		private CompilationHistory TryGetBaseline() {
			try {
				return _compilationHistoryPoller.GetBaseline();
			} catch (Exception exception) {
				//GetReadableMessageException, not .Message: a DataProviderFailureException's Message is the
				//AGENT rendering and carries the [untrusted-source-text begin] … [end] fence, which on a
				//terminal has no reader and makes an ordinary platform failure read as clio malfunctioning.
				//The extension picks the carrier's ConsoleMessage instead (PR #1374 review).
				_logger.WriteWarning(
					$"Could not read the compilation history baseline: {exception.GetReadableMessageException()}");
				return null;
			}
		}

		// In Creatio 8.3.3+, RebuildPackage no longer sends back an HTTP response —
		// the server compiles in the background and drops the connection. Use a cancellable
		// asynchronous request while the CompilationHistoryPoller detects completion via OData.
		// Where the response DOES arrive it carries Creatio's own verdict (success, buildResult and the
		// compiler diagnostics), so it is read rather than discarded: discarding it is how a C# compile
		// error used to end in "Done" and exit code 0 (issue #1633).
		private void CompileWithPolling(string url, string requestData, string packageName,
			PackageCompilationWaitOptions waitOptions, bool suggestWait) {
			CompilationHistory baseline = TryGetBaseline();
			DateTime baselineCreatedOn = baseline?.CreatedOn ?? DateTime.MinValue;

			bool waited = waitOptions is not null;
			DateTime? responseAt = null;
			TimeSpan budget = waitOptions?.Timeout ?? TimeSpan.FromMinutes(CompilationTimeoutMinutes);
			DateTime startedAt = UtcNow();
			DateTime timeoutAt = startedAt.Add(budget);
			CompilationProgress progress = new(ReportHistoryRow, UtcNow);

			using CancellationTokenSource cts = new();
			Task<string> httpTask = SendCompilationRequestAsync(cts.Token);
			// Published through a ONE-ELEMENT HOLDER with Volatile.Write/Read, not a plain captured local: the
			// write happens on the poll thread and the read on the main thread's spin loop below, so without an
			// explicit barrier there is no happens-before edge and the JIT may hoist the read out of the loop.
			Exception[] pollFaultBox = new Exception[1];
			Thread pollThread = StartPollThread(baselineCreatedOn, cts, progress, pollFaultBox);

			while (UtcNow() < timeoutAt) {
				ThrowIfPollFaulted(pollFaultBox, EndMonitoring);

				if (httpTask.Status == TaskStatus.RanToCompletion) {
					responseAt ??= UtcNow();
					if (TryConcludeOnAnswer(new AnsweredBuild(packageName, httpTask.Result, responseAt.Value, waited,
							suggestWait), progress, EndMonitoring)) {
						return;
					}
					Pause();
					continue;
				}

				if (httpTask.IsCompleted && !waited) {
					// The request failed without an answer. Without --wait that is reported as it always was.
					cts.Cancel();
					JoinPollThread(pollThread);
					httpTask.GetAwaiter().GetResult();
					return;
				}

				//ONE snapshot per iteration, taken under the same lock Observe writes under: the fields are
				//written on the poll thread and read here, so an unsynchronized read could be hoisted out of
				//the loop (a settle the poll thread saw is never observed, and the compile reports a timeout
				//instead) or mix a fresh HasErrors with stale diagnostics.
				CompilationProgressSnapshot observed = progress.Snapshot();
				if (HasSettled(observed, waited, requestEnded: httpTask.IsCompleted)) {
					EndMonitoring();
					ApplyVerdict(new BuildVerdictEvidence(packageName, Response: null, observed,
						CompletionInferred: true, Waited: waited, SuggestWait: suggestWait));
					return;
				}

				Pause();
			}

			// Read BEFORE EndMonitoring: cancelling a still-open request can fault it too, and that fault is
			// ours, not the environment's.
			Exception requestFault = httpTask.IsFaulted ? httpTask.Exception?.GetBaseException() : null;
			bool requestEnded = responseAt.HasValue || httpTask.IsCompleted;
			EndMonitoring();
			CompilationProgressSnapshot atDeadline = progress.Snapshot();
			FailOnTimeout(packageName, atDeadline, new TimeoutContext(waited, budget,
				AnsweredWithoutHistory: responseAt.HasValue && !atDeadline.LastActivityAt.HasValue, requestFault,
				DescribeQuietShortfall(atDeadline, new QuietShortfallContext(waited, startedAt, responseAt,
					RequestEnded: requestEnded))));

			void EndMonitoring() => EndCompileMonitoring(cts, pollThread, httpTask);

			async Task<string> SendCompilationRequestAsync(CancellationToken cancellationToken) {
				using IOwnedApplicationClient client = CreateClient();
				using HttpResponseMessage response = await client.ExecutePostRequestAsync(
					url, requestData, Timeout.Infinite, cancellationToken: cancellationToken).ConfigureAwait(false);
				return response.Content is null
					? null
					: await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Ends the monitoring of one compile, whatever state its request is in.
		/// </summary>
		/// <remarks>
		/// A waited build keeps observing after its request has ended without an answer, so that fault is
		/// no longer news: it is observed here instead of being rethrown by StopMonitoring.
		/// </remarks>
		private static void EndCompileMonitoring(CancellationTokenSource cts, Thread pollThread, Task httpTask) {
			if (httpTask.IsFaulted) {
				cts.Cancel();
				JoinPollThread(pollThread);
				_ = httpTask.Exception;
				return;
			}
			StopMonitoring(cts, pollThread, httpTask);
		}

		/// <summary>
		/// Reports a fault the poll thread gave up with, on the MAIN thread, so it is not silently lost while
		/// the loop runs to its full timeout with nothing watching the compile.
		/// </summary>
		/// <remarks>
		/// The fault is CHAINED, not interpolated. Interpolating its message made DescribeOuterContext treat
		/// this wrapper's own text as redundant (outer.Message contains the carrier's) and drop it, so the line
		/// lost every mention of the compile - the wrappers' context was destroyed by the very interpolation
		/// meant to carry it. Chained, EVERY link prints: this wrapper names the operation and the poller's own
		/// wrapper below it carries the give-up window and the failed-round count. That middle link survives
		/// only because DescribeChainAboveCarrier walks the whole chain - before issue #1376 the renderer kept
		/// just the outermost message and this three-link shape lost its diagnosis silently.
		/// </remarks>
		private static void ThrowIfPollFaulted(Exception[] pollFaultBox, Action endMonitoring) {
			Exception pollFault = Volatile.Read(ref pollFaultBox[0]);
			if (pollFault is null) {
				return;
			}
			endMonitoring();
			throw new InvalidOperationException("Package compilation could not be monitored", pollFault);
		}

		/// <summary>
		/// Ends a compile that ran out of budget: as a failure when the history already showed a compile
		/// error, otherwise as a timeout.
		/// </summary>
		/// <param name="packageName">The package being built.</param>
		/// <param name="observed">What the poll thread had observed when the budget ran out.</param>
		/// <param name="context">How the build was requested and how its request ended.</param>
		/// <remarks>
		/// A waited build settles on an error row only after the quiet window, so a row written shortly
		/// before the deadline used to be dropped and the failed build reported as "may still be running".
		/// An error row is final (Creatio does not keep building after one), so its diagnostics win.
		/// </remarks>
		private void FailOnTimeout(string packageName, CompilationProgressSnapshot observed, TimeoutContext context) {
			if (observed.HasErrors) {
				Fail(packageName, null, observed.Diagnostics, null, observed.ErrorDetails);
			}
			throw CreateTimeout(packageName, context);
		}

		/// <summary>
		/// Builds the exception for a compile that did not finish within its budget.
		/// </summary>
		/// <param name="packageName">The package being built.</param>
		/// <param name="context">How the build was requested and how its request ended.</param>
		/// <remarks>
		/// A request fault a waited build kept observing past is chained, not dropped: an authentication or
		/// connection failure is then still visible under the timeout instead of being lost.
		/// </remarks>
		private static TimeoutException CreateTimeout(string packageName, TimeoutContext context) {
			if (!context.Waited) {
				return new TimeoutException(
					$"Package compilation did not complete within {CompilationTimeoutMinutes} minutes.",
					context.RequestFault);
			}
			string reason = context.AnsweredWithoutHistory
				? $"The environment accepted the build of '{packageName}' but wrote no compilation history within "
					+ $"{context.Budget.TotalSeconds:0} s, so there is no evidence it finished."
				: $"Package compilation of '{packageName}' did not finish within {context.Budget.TotalSeconds:0} s.";
			string advice = context.QuietShortfall
				?? " The build may still be running on the environment; check `clio last-compilation-log` before "
				+ "compiling again.";
			return new TimeoutException(reason + advice, context.RequestFault);
		}

		/// <summary>
		/// Explains a waited timeout in which the history showed only clean rows but had not been quiet long
		/// enough yet, so the user knows the build most likely succeeded and which budget would have let
		/// clio say so.
		/// </summary>
		/// <param name="observed">What the poll thread had observed when the budget ran out.</param>
		/// <param name="context">How the build was requested and how its request had ended.</param>
		/// <returns>The explanation, or <see langword="null"/> when it does not apply.</returns>
		/// <remarks>
		/// The exit code stays a failure: the missing quiet spell is exactly the evidence that is not there.
		/// Only the wording changes, so the user raises <c>--wait-timeout</c> instead of recompiling with the
		/// same budget and timing out again.
		/// </remarks>
		private static string DescribeQuietShortfall(CompilationProgressSnapshot observed,
			QuietShortfallContext context) {
			if (!context.Waited || observed.HasErrors || observed.LastActivityAt is not { } lastRowAt) {
				return null;
			}
			DateTime lastEvidence = context.ResponseAt is { } answeredAt && answeredAt > lastRowAt
				? answeredAt
				: lastRowAt;
			TimeSpan window = RequiredQuietWindow(observed, waited: true, context.RequestEnded);
			double neededSeconds = (lastEvidence - context.StartedAt + window).TotalSeconds;
			int suggested = (int)Math.Ceiling(neededSeconds / 60) * 60;
			string retry = suggested <= PackageCompilationWaitOptions.MaxTimeoutSeconds
				? $"Re-run with `--wait-timeout {suggested}` or more"
				: $"Even the largest `--wait-timeout` ({PackageCompilationWaitOptions.MaxTimeoutSeconds}) would not cover it; check "
					+ "`clio last-compilation-log`";
			return $" No compile error was reported: the last compilation-history row ({observed.LastProjectName}) "
				+ $"arrived {(lastRowAt - context.StartedAt).TotalSeconds:0} s after the build started, and a build "
				+ $"counts as finished only after {window.TotalSeconds:0} s without a new row, so it has most likely "
				+ $"succeeded. {retry} to let clio confirm it.";
		}

		/// <summary>
		/// Rejects a build answer that is not a JSON verdict, such as an HTML login or proxy error page.
		/// </summary>
		/// <param name="packageName">The package being built.</param>
		/// <param name="responseBody">The raw response body.</param>
		/// <exception cref="InvalidOperationException">The body is present but is not a JSON object.</exception>
		/// <remarks>The body itself is not echoed: a login page can carry tokens or internal URLs.</remarks>
		private static void ThrowIfUnrecognizedBody(string packageName, string responseBody) {
			if (PackageBuildResultParser.IsUnrecognizedBody(responseBody)) {
				throw new InvalidOperationException(
					$"The environment answered the build request for '{packageName}' with a response that is not a "
					+ "build result (not JSON; possibly a login or proxy error page), so the build was not "
					+ "confirmed. Check the environment's URL and credentials.");
			}
		}

		/// <summary>
		/// Starts the dedicated poll thread, capturing any fault it gives up with instead of letting it
		/// escape.
		/// </summary>
		/// <remarks>
		/// The poll fault is CAPTURED, never allowed to escape the thread. An unhandled exception on a
		/// dedicated thread terminates the whole process, so a failed OData round would have killed clio
		/// mid-compile and skipped every cleanup in the wait loop (cts.Cancel / Join /
		/// ObserveCancelledRequest). Poll itself already tolerates individual failed rounds; this catches
		/// the case where it gives up, and hands the fault to the wait loop to report on the main thread.
		/// An unobserved fault is strictly worse than no guard at all - the poll thread has exited, nothing
		/// is watching the compilation history, and the loop runs to the full CompilationTimeoutMinutes
		/// with an open HTTP request before reporting a timeout instead of the real fault.
		/// <para>
		/// The thread is a BACKGROUND thread. A history read goes through Creatio.Client, whose response read
		/// has no timeout and does not observe the cancellation token, so a read the environment never
		/// answers blocks the thread indefinitely. JoinPollThread gives up after a bounded wait and the
		/// command reports its verdict, but a foreground thread would still keep the process alive: measured
		/// on a 10.2.254 stand, <c>compile-package --wait --wait-timeout 5</c> printed its timeout after 5 s
		/// and the process was still running 15 minutes later.
		/// </para>
		/// </remarks>
		private Thread StartPollThread(DateTime baselineCreatedOn, CancellationTokenSource cts,
			CompilationProgress progress, Exception[] pollFaultBox) {
			Thread pollThread = new(() => {
				try {
					_compilationHistoryPoller.Poll(baselineCreatedOn, cts.Token, progress.Observe);
				} catch (Exception exception) {
					Volatile.Write(ref pollFaultBox[0], exception);
				}
			}) {
				IsBackground = true
			};
			pollThread.Start();
			return pollThread;
		}

		/// <summary>
		/// What the poll thread has observed so far, read by the wait loop on the main thread: when the
		/// last record arrived, and whether any of them reported a compilation error.
		/// </summary>
		private sealed class CompilationProgress {

			/// <summary>An empty <c>ErrorsWarnings</c> array, which is not an error report.</summary>
			private const string EmptyErrorsWarnings = "[]";

			/// <summary>
			/// Guards every field below. A lock rather than <c>Volatile</c> per field, for two reasons:
			/// <c>DateTime?</c> is a 16-byte struct, so its write is not atomic and volatile could not make
			/// it so; and the wait loop wants a CONSISTENT view of all of them at once, which per-field
			/// barriers cannot give it.
			/// </summary>
			private readonly object _gate = new();

			private DateTime? _lastActivityAt;

			private bool _hasErrors;

			private string _errorDetails;

			private int _slowestDurationSeconds;

			private string _lastProjectName;

			private readonly List<PackageBuildDiagnostic> _diagnostics = [];

			private readonly Action<CompilationHistory> _onRecord;

			private readonly Func<DateTime> _utcNow;

			/// <param name="onRecord">Invoked for every observed record, outside the lock.</param>
			/// <param name="utcNow">The clock a record's arrival is stamped with.</param>
			public CompilationProgress(Action<CompilationHistory> onRecord, Func<DateTime> utcNow) {
				_onRecord = onRecord;
				_utcNow = utcNow;
			}

			/// <summary>
			/// Whether a history row reports a failed build: a failed result with a non-empty payload.
			/// </summary>
			internal static bool IsErrorRow(CompilationHistory record) =>
				!record.Result && !string.IsNullOrEmpty(record.ErrorsWarnings)
				&& record.ErrorsWarnings != EmptyErrorsWarnings;

			public void Observe(CompilationHistory record) {
				lock (_gate) {
					_lastActivityAt = _utcNow();
					_lastProjectName = record.ProjectName;
					_slowestDurationSeconds = Math.Max(_slowestDurationSeconds, record.DurationInSeconds);
					if (IsErrorRow(record)) {
						_hasErrors = true;
						_errorDetails = record.ErrorsWarnings;
						_diagnostics.AddRange(PackageBuildResultParser.ParseHistoryDiagnostics(record.ErrorsWarnings));
					}
				}
				_onRecord?.Invoke(record);
			}

			/// <summary>One consistent view of what the poll thread has observed so far.</summary>
			public CompilationProgressSnapshot Snapshot() {
				lock (_gate) {
					return new CompilationProgressSnapshot(_lastActivityAt, _hasErrors, _errorDetails,
						_slowestDurationSeconds, _lastProjectName, [.. _diagnostics]);
				}
			}

		}

		/// <summary>
		/// An immutable view of <see cref="CompilationProgress"/> taken under its lock, so the wait loop
		/// never mixes a fresh error flag with stale error text.
		/// </summary>
		private sealed record CompilationProgressSnapshot(
			DateTime? LastActivityAt, bool HasErrors, string ErrorDetails, int SlowestDurationSeconds,
			string LastProjectName, IReadOnlyList<PackageBuildDiagnostic> Diagnostics);

		/// <summary>
		/// A package build whose request has been answered.
		/// </summary>
		/// <param name="PackageName">The package being built.</param>
		/// <param name="Body">The raw answer body.</param>
		/// <param name="AnsweredAt">When the answer was first seen.</param>
		/// <param name="Waited">Whether the caller asked to wait for the build to finish.</param>
		/// <param name="SuggestWait">Whether an early success may point the user at <c>--wait</c>.</param>
		private sealed record AnsweredBuild(string PackageName, string Body, DateTime AnsweredAt,
			bool Waited, bool SuggestWait);

		/// <summary>
		/// How a compile that ran out of budget was requested and how its request ended.
		/// </summary>
		/// <param name="Waited">Whether the caller asked to wait for the build to finish.</param>
		/// <param name="Budget">The time the build was given.</param>
		/// <param name="AnsweredWithoutHistory">
		/// Whether the environment answered but wrote no compilation history, so the user does not read the
		/// timeout as a hung request.
		/// </param>
		/// <param name="RequestFault">The fault the request ended with, when it ended with one.</param>
		/// <param name="QuietShortfall">
		/// Why a waited build with only clean rows did not count as finished yet; <see langword="null"/> otherwise.
		/// </param>
		private sealed record TimeoutContext(bool Waited, TimeSpan Budget, bool AnsweredWithoutHistory,
			Exception RequestFault, string QuietShortfall);

		/// <summary>
		/// The timing a waited timeout is explained from.
		/// </summary>
		/// <param name="Waited">Whether the caller asked to wait for the build to finish.</param>
		/// <param name="StartedAt">When the build request was sent.</param>
		/// <param name="ResponseAt">When the answer was first seen; <see langword="null"/> when none arrived.</param>
		/// <param name="RequestEnded">Whether the build request had ended.</param>
		private sealed record QuietShortfallContext(bool Waited, DateTime StartedAt, DateTime? ResponseAt,
			bool RequestEnded);

		/// <summary>
		/// Everything a verdict on one finished package build is made from.
		/// </summary>
		/// <param name="PackageName">The package that was built.</param>
		/// <param name="Response">Creatio's verdict from the build response; <see langword="null"/> when it gave none.</param>
		/// <param name="History">What compilation history showed; <see langword="null"/> when it was not watched.</param>
		/// <param name="CompletionInferred">Whether completion was concluded from history going quiet.</param>
		/// <param name="Waited">Whether the caller asked to wait for the build to finish.</param>
		/// <param name="SuggestWait">
		/// Whether an early, inferred success should point the user at <c>compile-package --wait</c>; only the
		/// rebuild that command drives can act on that advice.
		/// </param>
		private sealed record BuildVerdictEvidence(string PackageName, PackageBuildResult Response,
			CompilationProgressSnapshot History, bool CompletionInferred, bool Waited, bool SuggestWait);

		private static void ObserveCancelledRequest(Task request, CancellationTokenSource cancellation) {
			try {
				request.GetAwaiter().GetResult();
			} catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
				// Expected when compilation history settles before the 8.3.3+ HTTP response arrives.
			} catch (HttpRequestException) when (cancellation.IsCancellationRequested) {
				// The server may close the connection while the settled request is being cancelled.
			}
		}

		#endregion

		#region Methods: Public

		public void Build(IEnumerable<string> packagesNames) => Compilation(packagesNames, false);

		public void Rebuild(IEnumerable<string> packagesNames) => Compilation(packagesNames, true);

		public void Rebuild(IEnumerable<string> packagesNames, PackageCompilationWaitOptions waitOptions) =>
			Compilation(packagesNames, true, waitOptions);

		#endregion

	}

	#endregion
}
