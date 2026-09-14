using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.CreatioModel;
using CommandLine;

namespace Clio.Command;

#region Class: CompileConfigurationOptions

[Verb("compile-configuration", Aliases = ["cc","compile-remote"], HelpText = "Compile configuration")]
public class CompileConfigurationOptions : RemoteCommandOptions
{

	[Option("all", Required = false, HelpText = "Compile configuration all", Default = false)]
	public bool All {
		get; set;
	}
	// No DefaultTimeout override. RemoteCommandOptions.GetTimeOut already declares 60 minutes for
	// `compile-configuration`, and this class used to override it with Timeout.Infinite - which also
	// silently discarded any --timeout the caller passed. An unbounded wait is what let a build whose
	// response never arrives hold its slot for the life of the process (issue #1422); the bound is a
	// backstop now that completion no longer depends on that response at all.

}

#endregion

#region Interface: CompileConfigurationCommand

public interface ICompileConfigurationCommand {
	int Execute(CompileConfigurationOptions options);

}

#endregion

#region Class: CompileConfigurationCommand

public class CompileConfigurationCommand : RemoteCommand<CompileConfigurationOptions>, ICompileConfigurationCommand {
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ICompilationHistoryPoller _compilationHistoryPoller;
	private readonly ILogger _logger;
	private readonly IInteractiveConsole _interactiveConsole;
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly ICompilationActivityWatcher _activityWatcher;
	private readonly IEnvironmentReloadWatcher _reloadWatcher;
	private readonly ICompilationCompletionDecider _completionDecider;
	private readonly ICompilationResultReader _compilationResultReader;

	// Set once, when the reload is first seen, so the user is told what the pause in the output is.
	private bool _reloadReported;

	/// <summary>
	/// Why the compile request failed, kept so the transport-failure message can name the actual cause
	/// (401, DNS, TLS) instead of printing a generic checklist. <see langword="null"/> until one is seen.
	/// </summary>
	private Exception _requestFailure;

	private const string OdataProjName = "Terrasoft.Configuration.ODataEntities.csproj";
	private const string DevProjName = "Terrasoft.Configuration.Dev.csproj";

	/// <summary>
	/// Heavy-operation warning shown on the interactive CLI before a site compilation (ENG-93157). Paired
	/// with the <c>[Y/N]</c> prompt so the user can proceed now or postpone.
	/// </summary>
	internal const string SiteCompilationWarning =
		"WARNING: Compilation is a heavy operation. It recompiles the site configuration and forces a " +
		"runtime reload that may disrupt every user currently connected to this environment.";

	private bool _compileAll;

	private bool _isSuccess = false;

	#region Constructors: Public

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "The command composes its required collaborators (application client, environment "
			+ "settings, URL builder, history poller, logger, interactive console, client factory, activity "
			+ "watcher, reload watcher, completion decider, result reader) via constructor injection; "
			+ "bundling them into a parameter object would hide the injected contract without changing "
			+ "behaviour, which is how the other multi-collaborator commands in this assembly are handled.")]
	public CompileConfigurationCommand(IApplicationClient applicationClient,
		EnvironmentSettings settings, IServiceUrlBuilder serviceUrlBuilder,
		ICompilationHistoryPoller compilationHistoryPoller, ILogger logger,
		IInteractiveConsole interactiveConsole, IApplicationClientFactory applicationClientFactory,
		ICompilationActivityWatcher activityWatcher, IEnvironmentReloadWatcher reloadWatcher,
		ICompilationCompletionDecider completionDecider, ICompilationResultReader compilationResultReader)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_compilationHistoryPoller = compilationHistoryPoller;
		_logger = logger;
		_interactiveConsole = interactiveConsole;
		_applicationClientFactory = applicationClientFactory;
		_activityWatcher = activityWatcher;
		_reloadWatcher = reloadWatcher;
		_completionDecider = completionDecider;
		_compilationResultReader = compilationResultReader;
		// The class had two loggers: the injected one used by Execute, and the base Logger property that
		// defaults to ConsoleLogger.Instance and is what ProceedResponse writes its errors through. In
		// production DI they resolve to the same singleton, so nobody noticed - but it meant half the
		// command's output was unreachable from a test that substituted ILogger. One logger now.
		Logger = logger;
	}

	#endregion

	protected override string ServicePath => _compileAll
		? _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileAll)
		: _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Compile);

	public override int Execute(CompileConfigurationOptions options) {
		if (!_interactiveConsole.ConfirmHeavyOperation(options.IsSilent, SiteCompilationWarning, _logger, BuildPostponeHint(options))) {
			// The user chose to postpone: nothing is compiled. Return the distinct DeclinedExitCode (not 0)
			// so in-process callers (push-package --force-compilation) and shell chains can tell it apart
			// from a successful compile. Only reachable on an interactive, non-silent terminal.
			return InteractiveConsoleExtensions.DeclinedExitCode;
		}
		CompilationHistory baseline = TryGetBaseline();
		_compileAll = options.All;
		_reloadReported = false;
		_requestFailure = null;
		Stopwatch sw = new();
		sw.Start();
		_logger.WriteLine("=================================================================================");
		_logger.WriteInfo($"At: {DateTime.Now:HH:mm:ss} Starting compilation...");
		_logger.WriteLine();

		CompilationCompletionKind completion = RunObservedCompilation(options, baseline, out string responseBody);
		sw.Stop();
		ApplyVerdict(completion, responseBody, options);

		if (CommandSuccess) {
			_logger.WriteLine();
			_logger.WriteInfo($"Compilation finished in {TimeOnly.FromTimeSpan(sw.Elapsed):HH:mm:ss}");
			_logger.WriteLine("=================================================================================");
		}
		return _isSuccess ? 0 : 1;
	}

	#region Constants: Internal

	/// <summary>
	/// How long a build may stay silent at the start before silence is read as "nothing was ever built".
	/// </summary>
	/// <remarks>
	/// The gap between the compile request and the first <c>CompilationHistory</c> row was measured at
	/// ~18 s on a warm stand; a cold or loaded one is slower. The grace only gates the TRANSPORT-FAILURE
	/// verdict, so being generous here costs nothing when the build is real and avoids calling a slow
	/// start a broken environment.
	/// </remarks>
	internal static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(90);

	/// <summary>Cadence at which the completion rule re-examines what has been observed.</summary>
	internal static readonly TimeSpan DecisionInterval = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The bound used when the caller supplied no usable timeout - the same 60 minutes
	/// <see cref="RemoteCommandOptions"/> declares for this verb.
	/// </summary>
	internal static readonly int DeclaredTimeoutMs = (int)TimeSpan.FromMinutes(60).TotalMilliseconds;

	/// <summary>
	/// How long compilation activity must have been stopped before quiet ALONE ends the wait, when the
	/// runtime reload that normally ends a build was never observed.
	/// </summary>
	/// <remarks>
	/// <b>It has to outlast the reload, and that is why it is not the settle tracker's 45 seconds.</b>
	/// That window is calibrated for the gaps BETWEEN projects, which is a different question.
	/// Measured on a live stand: the reload lands about two minutes after the last compilation-history
	/// row, so a 45-second window ends the wait BEFORE the application has reloaded - and the verdict
	/// endpoint carries no timestamp, so what gets read at that moment may still be the previous build's
	/// result. Five minutes keeps the reload the normal path and leaves this one for the case it exists
	/// for: an intermediary holding the request open so nothing else ever terminates (issue #1422).
	/// <para>
	/// The window is not the only precondition. Quiet only concludes the build while the environment is
	/// still answering - a runtime that stopped answering mid-build and never came back writes the same
	/// evidence, and waiting it out to the timeout is the correct report there.
	/// </para>
	/// </remarks>
	internal static readonly TimeSpan QuietFallback = TimeSpan.FromMinutes(5);

	#endregion

	#region Properties: Internal

	/// <summary>
	/// Test seam overriding <see cref="StartupGrace"/>; <see langword="null"/> in production.
	/// </summary>
	/// <remarks>
	/// Unit tests drive the transport-failure branch with substitutes whose request never produces a
	/// response, so without this every such test would sit out the real 90-second grace.
	/// </remarks>
	internal TimeSpan? StartupGraceOverride { get; set; }

	/// <summary>
	/// Test seam overriding <see cref="DecisionInterval"/>; <see langword="null"/> in production.
	/// </summary>
	internal TimeSpan? DecisionIntervalOverride { get; set; }

	/// <summary>
	/// Test seam overriding <see cref="QuietFallback"/>; <see langword="null"/> in production.
	/// </summary>
	internal TimeSpan? QuietFallbackOverride { get; set; }

	#endregion

	#region Methods: Private

	/// <summary>
	/// Sends the compile request and watches the environment until the completion rule decides the build
	/// has ended, the request bound elapses, or nothing was ever built.
	/// </summary>
	/// <param name="options">The command options, whose <c>TimeOut</c> bounds the whole operation.</param>
	/// <param name="baseline">The newest compilation-history row before the build was requested.</param>
	/// <param name="responseBody">The compile response body when one arrived; otherwise <see langword="null"/>.</param>
	/// <returns>How the build ended.</returns>
	/// <remarks>
	/// The request is sent ONCE. <see cref="RemoteCommandOptions.MaxAttempts"/> defaults to 3 and the old
	/// synchronous path passed it through, so a build whose connection the runtime reload had just reset
	/// was re-sent - measured on a live stand as three full rebuilds, three runtime reloads and about nine
	/// minutes for one `clio cc --all`, after which IIS rapid-fail protection stopped the application pool.
	/// Under observed completion a retry is not merely wasteful but wrong: the watcher would see the rows
	/// start over and wait out every repeat.
	/// </remarks>
	private CompilationCompletionKind RunObservedCompilation(CompileConfigurationOptions options,
		CompilationHistory baseline, out string responseBody) {
		responseBody = null;
		DateTime startedUtc = DateTime.UtcNow;

		using CancellationTokenSource requestCancellation = new();
		Task<string> requestTask = SendCompileRequestAsync(options, requestCancellation.Token);
		try {
			_activityWatcher.Start(baseline?.CreatedOn ?? DateTime.MinValue, LogRecord);
			// Availability is watched SEPARATELY from build activity, and on a different endpoint, because
			// the reload is invisible on the compilation-history channel - see IEnvironmentReloadWatcher.
			// Both starts are INSIDE the try: if the second one throws, the finally still stops the first,
			// cancels the request and observes its fault.
			_reloadWatcher.Start();
			// A non-positive timeout is NOT "wait forever". Timeout.Infinite (-1) was this verb's documented
			// default until this change, so scripts plausibly still pass it - and honouring it literally
			// would restore the unbounded wait that issue #1422 is about.
			DateTime deadline = startedUtc.AddMilliseconds(EffectiveTimeoutMs(options));
			while (true) {
				CompilationCompletionKind completion = Decide(requestTask, startedUtc);
				if (completion != CompilationCompletionKind.KeepWaiting) {
					if (completion == CompilationCompletionKind.ResponseReceived) {
						responseBody = requestTask.Result;
					} else if (completion == CompilationCompletionKind.TransportFailure) {
						// Keep WHY the request failed. TransportFailure is only decided once the request has
						// ended, so the fault is available here - and it is the whole diagnosis (401, DNS,
						// TLS). Without it the user gets a generic checklist for a cause the command knows.
						_requestFailure = requestTask.Exception?.GetBaseException();
					}
					return completion;
				}
				if (DateTime.UtcNow >= deadline) {
					return CompilationCompletionKind.KeepWaiting;
				}
				Thread.Sleep(DecisionIntervalOverride ?? DecisionInterval);
			}
		} finally {
			// Order matters: cancel the request first so a still-open connection stops being held, then stop
			// the watcher, which joins its poll thread. Cancelling a request nobody is waiting for is what
			// keeps an abandoned socket from outliving the command.
			requestCancellation.Cancel();
			_activityWatcher.Stop();
			_reloadWatcher.Stop();
			ObserveAbandonedRequest(requestTask);
		}
	}

	/// <summary>Samples the current evidence and asks the completion rule what it means.</summary>
	private CompilationCompletionKind Decide(Task<string> requestTask, DateTime startedUtc) {
		CompilationActivitySnapshot activity = _activityWatcher.Snapshot;
		EnvironmentReloadSnapshot availability = _reloadWatcher.Snapshot;
		ReportReloadOnce(availability);
		return _completionDecider.Decide(new CompilationCompletionState(
			ResponseReceived: requestTask.Status == TaskStatus.RanToCompletion,
			RequestEnded: requestTask.IsCompleted,
			NewRecordCount: activity.NewRecordCount,
			// Availability comes from the reload watcher, not the history poller: the reload is invisible
			// on the history channel, and "the environment is answering" has to mean the endpoint the
			// verdict is about to be read from.
			ReloadObserved: availability.ReloadObserved,
			EnvironmentReachable: availability.Reachable,
			EnvironmentEverReachable: availability.EverReachable,
			LastActivityAtUtc: activity.LastActivityAtUtc,
			StartedUtc: startedUtc,
			NowUtc: DateTime.UtcNow,
			StartupGrace: StartupGraceOverride ?? StartupGrace,
			QuietFallback: QuietFallbackOverride ?? QuietFallback));
	}

	/// <summary>
	/// Tells the user, once, that the runtime reload that ends a build has been seen.
	/// </summary>
	/// <param name="availability">The current availability snapshot.</param>
	private void ReportReloadOnce(EnvironmentReloadSnapshot availability) {
		if (_reloadReported || !availability.ReloadObserved) {
			return;
		}
		_reloadReported = true;
		_logger.WriteInfo(
			$"At: {DateTime.Now:HH:mm:ss} The environment reloaded and is answering again - a configuration "
			+ "build ends by reloading the application. Reading the compilation result.");
	}

	/// <summary>
	/// Sends the configuration-build request on its own owned client, once, with cancellation.
	/// </summary>
	private async Task<string> SendCompileRequestAsync(CompileConfigurationOptions options,
		CancellationToken cancellationToken) {
		using IOwnedApplicationClient client = _applicationClientFactory.CreateOwnedClient(EnvironmentSettings);
		using HttpResponseMessage response = await client
			.ExecutePostRequestAsync(ServiceUri, "{}", EffectiveTimeoutMs(options), maxAttempts: 1,
				delaySec: 1, cancellationToken)
			.ConfigureAwait(false);
		return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>The bound actually applied, falling back to the declared one for a non-positive value.</summary>
	private static int EffectiveTimeoutMs(CompileConfigurationOptions options) =>
		options.TimeOut > 0 ? options.TimeOut : DeclaredTimeoutMs;

	/// <summary>
	/// Observes the fault of a request nobody is waiting for any more.
	/// </summary>
	/// <remarks>
	/// The expected outcome on every successful build is a fault: the runtime reload resets the connection
	/// before it answers. Leaving that unobserved would surface later as an UnobservedTaskException in an
	/// unrelated part of the process. The fault is still REPORTED on the transport-failure path - see
	/// <see cref="_requestFailure"/>; this method only makes sure the abandoned ones are observed.
	/// </remarks>
	private static void ObserveAbandonedRequest(Task<string> requestTask) =>
		_ = requestTask.ContinueWith(static task => _ = task.Exception,
			CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

	/// <summary>
	/// Turns the completion kind into the command's success state and user-visible outcome.
	/// </summary>
	/// <param name="completion">How the build ended.</param>
	/// <param name="responseBody">The compile response body, when one arrived.</param>
	/// <param name="options">The command options.</param>
	/// <remarks>
	/// <c>CommandSuccess</c> is set on EVERY path. It defaults to <see langword="true"/> and used to be
	/// cleared only inside <c>ProceedResponse</c>, which a build with no response never reaches - so a
	/// failed run printed its error and then "Compilation finished" immediately after it.
	/// </remarks>
	private void ApplyVerdict(CompilationCompletionKind completion, string responseBody,
		CompileConfigurationOptions options) {
		switch (completion) {
			case CompilationCompletionKind.ResponseReceived:
				ProceedResponse(responseBody, options);
				return;
			case CompilationCompletionKind.ConfirmedByReload:
				ApplyEnvironmentVerdict(inferred: false);
				return;
			case CompilationCompletionKind.InferredFromQuiet:
				ApplyEnvironmentVerdict(inferred: true);
				return;
			case CompilationCompletionKind.TransportFailure:
				CommandSuccess = _isSuccess = false;
				Logger.WriteError(
					"The compilation request failed and the environment never started building: no compilation "
					+ "history was written after the request was sent.");
				if (_requestFailure is not null) {
					Logger.WriteError($"Request error: {_requestFailure.Message}");
				}
				Logger.WriteError($"Endpoint: {ServiceUri}");
				Logger.WriteError("Check the environment URI, the IsNetCore flag, and the credentials.");
				return;
			default:
				CommandSuccess = _isSuccess = false;
				Logger.WriteError(
					"Timed out waiting for the compilation to finish. The build may still be running on the "
					+ "environment; check `clio last-compilation-log` before starting another one.");
				return;
		}
	}

	/// <summary>
	/// Reads the verdict from the environment after the build has ended.
	/// </summary>
	/// <param name="inferred">
	/// Whether completion was inferred from activity stopping rather than confirmed by the runtime reload.
	/// </param>
	private void ApplyEnvironmentVerdict(bool inferred) {
		CompilationActivitySnapshot activity = _activityWatcher.Snapshot;
		CreatioCompilationLogResponse result = _compilationResultReader.TryRead();
		if (inferred) {
			Logger.WriteWarning(
				"The compilation request never returned and no runtime reload was observed. Completion was "
				+ "inferred from the environment's compilation activity having stopped.");
		}
		if (result is null && inferred) {
			// The build was never confirmed to have ENDED - the reload was not seen, completion was inferred
			// from activity stopping - and now its verdict cannot be read either. Two unknowns stacked is not
			// evidence of success: a runtime that stopped answering mid-build presents exactly this. Report a
			// failure so nothing downstream treats an unverified build as a delivered one.
			CommandSuccess = _isSuccess = false;
			Logger.WriteError(
				"Could not read the compilation result from the environment, and the build's completion was "
				+ "only inferred from its activity having stopped. The outcome is unknown; check "
				+ "`clio last-compilation-log` before relying on this environment.");
			return;
		}
		if (result is null) {
			// Completion here was CONFIRMED by the runtime reload, so the build demonstrably ran to its end
			// and only the verdict lookup failed. Reporting a clio error would replace something known with
			// something unknown, so the observed diagnostics decide instead.
			CommandSuccess = _isSuccess = !activity.HasErrors;
			Logger.WriteWarning(
				"Could not read the compilation result from the environment; reporting the outcome from the "
				+ "compilation history instead.");
			return;
		}
		CommandSuccess = _isSuccess = result.success && !activity.HasErrors;
		if (_isSuccess) {
			return;
		}
		Logger.WriteError($"Compilation failed on the environment. Build result: {result.buildResult}.");
		foreach (CreatioCompilationError diagnostic in result.errors ?? []) {
			if (diagnostic.warning) {
				continue;
			}
			Logger.WriteError(
				$"({diagnostic.errorNumber}) in {diagnostic.fileName} at ({diagnostic.line},{diagnostic.column}): "
				+ diagnostic.errorText);
		}
	}

	#endregion

	/// <summary>
	/// Builds the "how to run it later" hint shown when the user postpones the compilation, echoing the
	/// exact <c>clio cc</c> invocation (with environment and <c>--all</c>) that reproduces the request.
	/// </summary>
	private static string BuildPostponeHint(CompileConfigurationOptions options) {
		string environmentPart = string.IsNullOrWhiteSpace(options.Environment)
			? string.Empty
			: $" -e {options.Environment}";
		string allPart = options.All ? " --all" : string.Empty;
		return $"Compilation postponed. Nothing was compiled. Run it later with: clio cc{environmentPart}{allPart}";
	}

	/// <summary>
	/// Reads the compilation-history baseline, degrading to <c>null</c> when the read fails.
	/// </summary>
	/// <remarks>
	/// ClassifyingDataProvider turns a failed OData round into an exception instead of an empty list, so an
	/// unguarded read here would abort the compile before the compile request is ever sent - a single
	/// transient failure would be strictly worse than before that decorator existed. A missing baseline only
	/// costs precision in the progress lines (Poll falls back to DateTime.MinValue), and monitoring is not the
	/// point of the command, so the failure is reported as a warning and the compile goes ahead.
	/// Mirrors WatchCompilationCommand.TryGetBaseline, except that command cannot continue without a
	/// baseline and therefore aborts, while this one can.
	/// </remarks>
	private CompilationHistory TryGetBaseline() {
		try {
			return _compilationHistoryPoller.GetBaseline();
		} catch (Exception exception) {
			//GetReadableMessageException, not .Message: a DataProviderFailureException's Message is the AGENT
			//rendering and carries the [untrusted-source-text begin] … [end] fence, which on a terminal has no
			//reader and makes an ordinary platform failure read as clio malfunctioning. The extension picks
			//the carrier's ConsoleMessage instead (PR #1374 review).
			_logger.WriteWarning(
				$"Could not read the compilation history baseline: {exception.GetReadableMessageException()}");
			return null;
		}
	}

	private void LogRecord(CompilationHistory record) {
		string decoratedDuration = record.DurationInSeconds switch {
			>= 10 => ConsoleLogger.WrapRed(record.DurationInSeconds),
			>= 5 => ConsoleLogger.WrapYellow(record.DurationInSeconds),
			var _ => record.DurationInSeconds.ToString("N0", CultureInfo.InvariantCulture)
		};
		List<string> specialProj = [OdataProjName, DevProjName];
		string decoratedProjectName = record.ProjectName switch {
			{ } y when specialProj.Contains(y) => ConsoleLogger.WrapBlue(y) + ConsoleLogger.WrapGreen(" <============"),
			var _ => record.ProjectName
		};
		if (string.Equals(record.ErrorsWarnings, "[]", StringComparison.OrdinalIgnoreCase)) {
			_logger.WriteInfo($"At: {record.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName}");
		} else {
			_logger.WriteWarning($"At: {record.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName} with: {ParseErrors(record.ErrorsWarnings)}");
		}
	}

	private static readonly JsonSerializerOptions JsonSerializerOptions = new()
		{ PropertyNameCaseInsensitive = true };
	private static readonly Func<string, string> ParseErrors = (json) => {
		try {
			List<CompError> errors = JsonSerializer.Deserialize<List<CompError>>(json, JsonSerializerOptions);
			StringBuilder sb = new();
			int errorNumber = 1;
			foreach (string message in errors.Select(error => error switch {
				var _ when string.IsNullOrWhiteSpace(error.FileName) && error.IsWarning => $"({ConsoleLogger.WrapYellow(error.ErrorNumber)}): {error.ErrorText}",
				var _ when string.IsNullOrWhiteSpace(error.FileName) && !error.IsWarning => $"({ConsoleLogger.WrapRed(error.ErrorNumber)}): {error.ErrorText}",
				var _ when !string.IsNullOrWhiteSpace(error.FileName) && error.IsWarning => $"({ConsoleLogger.WrapYellow(error.ErrorNumber)}) in {ConsoleLogger.WrapYellow(error.FileName)} at ({error.Line},{error.Column}): {error.ErrorText}",
				var _ when !string.IsNullOrWhiteSpace(error.FileName) && !error.IsWarning => $"({ConsoleLogger.WrapRed(error.ErrorNumber)}) in {ConsoleLogger.WrapYellow(error.FileName)} at ({error.Line},{error.Column}) : {error.ErrorText}",
				var _ => json //We should never be here, this is to make compiler happy
			})) {
				sb.AppendLine().Append('\t').Append($"{errorNumber++} of {errors.Count} ").Append(message);
			}
			return sb.ToString();
		}
		// Could not parse errors, return original json
		catch {
			return json;
		}
	};

	protected override void ProceedResponse(string response, CompileConfigurationOptions options) {
		base.ProceedResponse(response, options);
		try {
			if (string.IsNullOrWhiteSpace(response)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Empty response received from server during compilation.");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				return;
			}

			string trimmed = response.TrimStart();
			if (trimmed.StartsWith("<", StringComparison.Ordinal)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Server returned non-JSON response during compilation (looks like HTML).");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				Logger.WriteError("Full response:");
				Logger.WriteLine(trimmed);
				Logger.WriteError("Check environment URI, IsNetCore flag, and credentials (a login/404 page is often returned as HTML).");
				return;
			}

			CreatioResponse model = JsonSerializer.Deserialize<CreatioResponse>(response);
			CommandSuccess = _isSuccess = model.Success;
			if (!model.Success) {
				Logger.WriteError($"{model.ErrorInfo.ErrorCode}: {model.ErrorInfo.Message}");
			}
		}
		catch (Exception e) {
			CommandSuccess = _isSuccess = false;
			Logger.WriteError(e.Message);
			Logger.WriteError($"Endpoint: {ServiceUri}");
			if (!string.IsNullOrWhiteSpace(response)) {
				Logger.WriteError("Full response:");
				Logger.WriteLine(response);
			}
		}
	}
}

#endregion


public class CreatioResponse
{
	[JsonPropertyName("errorInfo")]
	public ErrorInfo ErrorInfo { get; set; }

	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonPropertyName("buildResult")]
	public int BuildResult { get; set; }

	[JsonPropertyName("errors")]
	public object Errors { get; set; }

	[JsonPropertyName("message")]
	public object Message { get; set; }
}

public class ErrorInfo
{
	[JsonPropertyName("errorCode")]
	public string ErrorCode { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }

	[JsonPropertyName("stackTrace")]
	public object StackTrace { get; set; }
}

public class CompError
{
	[JsonPropertyName("Line")]
	public int Line { get; set; }

	[JsonPropertyName("Column")]
	public int Column { get; set; }

	[JsonPropertyName("ErrorNumber")]
	public string ErrorNumber { get; set; }

	[JsonPropertyName("ErrorText")]
	public string ErrorText { get; set; }

	[JsonPropertyName("IsWarning")]
	public bool IsWarning { get; set; }

	[JsonPropertyName("FileName")]
	public string FileName { get; set; }
}
