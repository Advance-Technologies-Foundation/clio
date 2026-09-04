namespace Clio.Package
{
	using System;
	using System.Net.Http;
	using System.Threading;
	using System.Threading.Tasks;
	using System.Collections.Generic;
	using Clio.Common;
	using Clio.CreatioModel;

	#region Interface: IPackageBuilder

	public interface IPackageBuilder
	{

		#region Methods: Public

		void Build(IEnumerable<string> packagesNames);

		void Rebuild(IEnumerable<string> packagesNames);

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

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;
		private readonly ICompilationHistoryPoller _compilationHistoryPoller;

		#endregion

		#region Constructors: Public

		public PackageBuilder(EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger, ICompilationHistoryPoller compilationHistoryPoller = null) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
			_compilationHistoryPoller = compilationHistoryPoller;
		}

		#endregion

		#region Methods: Private

		private static string CreateRequestData(string packageName) => "{ \"packageName\":\"" + packageName + "\" }";

		private IOwnedApplicationClient CreateClient() => _applicationClientFactory.CreateOwnedClient(_environmentSettings);

		private string GetSafePackageName(string packageName) =>
			packageName
				.Replace(" ", string.Empty)
				.Replace(",", "\",\"");

		private void Compilation(IEnumerable<string> packagesNames, bool force) {
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
					CompileWithPolling(fullBuildPackageUrl, requestData);
				} else {
					using IOwnedApplicationClient applicationClient = CreateClient();
					applicationClient.ExecutePostRequest(fullBuildPackageUrl, requestData);
				}

				_logger.WriteLine($"End {compilationName} packages ({safePackageName}).");
			}
		}

		/// <summary>
		/// <see langword="true"/> when no compilation-history activity has been seen for
		/// <see cref="CompilationSettleSeconds"/>, which is how a response-less 8.3.3+ compile signals
		/// that it finished.
		/// </summary>
		private static bool HasSettled(DateTime? lastActivityAt)
			=> lastActivityAt.HasValue
				&& (DateTime.UtcNow - lastActivityAt.Value).TotalSeconds >= CompilationSettleSeconds;

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
			pollThread.Join();
			ObserveCancelledRequest(httpTask, cts);
		}

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
				_logger.WriteWarning($"Could not read the compilation history baseline: {exception.Message}");
				return null;
			}
		}

		// In Creatio 8.3.3+, RebuildPackage no longer sends back an HTTP response —
		// the server compiles in the background and drops the connection. Use a cancellable
		// asynchronous request while the CompilationHistoryPoller detects completion via OData.
		private void CompileWithPolling(string url, string requestData) {
			CompilationHistory baseline = TryGetBaseline();
			DateTime baselineCreatedOn = baseline?.CreatedOn ?? DateTime.MinValue;

			DateTime timeoutAt = DateTime.UtcNow.AddMinutes(CompilationTimeoutMinutes);
			CompilationProgress progress = new();

			using CancellationTokenSource cts = new();
			Task httpTask = SendCompilationRequestAsync(cts.Token);
			// Published through a ONE-ELEMENT HOLDER with Volatile.Write/Read, not a plain captured local: the
			// write happens on the poll thread and the read on the main thread's spin loop below, so without an
			// explicit barrier there is no happens-before edge and the JIT may hoist the read out of the loop.
			Exception[] pollFaultBox = new Exception[1];
			Thread pollThread = StartPollThread(baselineCreatedOn, cts, progress, pollFaultBox);

			while (DateTime.UtcNow < timeoutAt) {
				//Observed on the MAIN thread, so the fault is reported rather than silently ending the
				//poll and letting the loop run to its full timeout with nothing watching the compile.
				Exception pollFault = Volatile.Read(ref pollFaultBox[0]);
				if (pollFault is not null) {
					StopMonitoring(cts, pollThread, httpTask);
					throw new InvalidOperationException(
						$"Package compilation could not be monitored: {pollFault.Message}", pollFault);
				}

				if (httpTask.IsCompleted) {
					cts.Cancel();
					pollThread.Join();
					httpTask.GetAwaiter().GetResult();
					return;
				}

				if (HasSettled(progress.LastActivityAt)) {
					StopMonitoring(cts, pollThread, httpTask);
					if (progress.HasErrors) {
						throw new Exception($"Package compilation failed: {progress.ErrorDetails}");
					}
					return;
				}

				Thread.Sleep(500);
			}

			StopMonitoring(cts, pollThread, httpTask);
			throw new TimeoutException($"Package compilation did not complete within {CompilationTimeoutMinutes} minutes.");

			async Task SendCompilationRequestAsync(CancellationToken cancellationToken) {
				using IOwnedApplicationClient client = CreateClient();
				using HttpResponseMessage _ = await client.ExecutePostRequestAsync(
					url, requestData, Timeout.Infinite, cancellationToken: cancellationToken).ConfigureAwait(false);
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
		/// </remarks>
		private Thread StartPollThread(DateTime baselineCreatedOn, CancellationTokenSource cts,
			CompilationProgress progress, Exception[] pollFaultBox) {
			Thread pollThread = new(() => {
				try {
					_compilationHistoryPoller.Poll(baselineCreatedOn, cts.Token, progress.Observe);
				} catch (Exception exception) {
					Volatile.Write(ref pollFaultBox[0], exception);
				}
			});
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

			public DateTime? LastActivityAt { get; private set; }

			public bool HasErrors { get; private set; }

			public string ErrorDetails { get; private set; }

			public void Observe(CompilationHistory record) {
				LastActivityAt = DateTime.UtcNow;
				if (record.Result || string.IsNullOrEmpty(record.ErrorsWarnings)
						|| record.ErrorsWarnings == EmptyErrorsWarnings) {
					return;
				}
				HasErrors = true;
				ErrorDetails = record.ErrorsWarnings;
			}

		}

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

		#endregion

	}

	#endregion
}
