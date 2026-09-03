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

		// In Creatio 8.3.3+, RebuildPackage no longer sends back an HTTP response —
		// the server compiles in the background and drops the connection. Use a cancellable
		// asynchronous request while the CompilationHistoryPoller detects completion via OData.
		private void CompileWithPolling(string url, string requestData) {
			CompilationHistory baseline = _compilationHistoryPoller.GetBaseline();
			DateTime baselineCreatedOn = baseline?.CreatedOn ?? DateTime.MinValue;

			DateTime timeoutAt = DateTime.UtcNow.AddMinutes(CompilationTimeoutMinutes);
			DateTime? lastActivityAt = null;
			bool hasErrors = false;
			string errorDetails = null;

			using CancellationTokenSource cts = new();
			Task httpTask = SendCompilationRequestAsync(cts.Token);
			// The poll fault is CAPTURED, never allowed to escape this lambda. An unhandled exception on a
			// dedicated thread terminates the whole process, so a failed OData round would have killed
			// clio mid-compile and skipped every cleanup below (cts.Cancel / Join / ObserveCancelledRequest).
			// Poll itself already tolerates individual failed rounds; this catches the case where it gives
			// up, and hands the fault to the loop below to report on the main thread.
			// Published through a ONE-ELEMENT HOLDER with Volatile.Write/Read, not a plain captured local: the
			// write happens on the poll thread and the read on the main thread's spin loop below, so without an
			// explicit barrier there is no happens-before edge and the JIT may hoist the read out of the loop.
			// An unobserved fault is strictly worse than no guard at all - the poll thread has exited, nothing is
			// watching the compilation history, and the loop runs to the full CompilationTimeoutMinutes with an
			// open HTTP request before reporting a timeout instead of the real fault.
			Exception[] pollFaultBox = new Exception[1];
			Thread pollThread = new(() => {
				try {
					_compilationHistoryPoller.Poll(baselineCreatedOn, cts.Token, record => {
						lastActivityAt = DateTime.UtcNow;
						if (!record.Result && !string.IsNullOrEmpty(record.ErrorsWarnings) && record.ErrorsWarnings != "[]") {
							hasErrors = true;
							errorDetails = record.ErrorsWarnings;
						}
					});
				} catch (Exception exception) {
					Volatile.Write(ref pollFaultBox[0], exception);
				}
			});
			pollThread.Start();

			while (DateTime.UtcNow < timeoutAt) {
				//Observed on the MAIN thread, so the fault is reported rather than silently ending the
				//poll and letting the loop run to its full timeout with nothing watching the compile.
				Exception pollFault = Volatile.Read(ref pollFaultBox[0]);
				if (pollFault is not null) {
					cts.Cancel();
					pollThread.Join();
					ObserveCancelledRequest(httpTask, cts);
					throw new InvalidOperationException(
						$"Package compilation could not be monitored: {pollFault.Message}", pollFault);
				}

				if (httpTask.IsCompleted) {
					cts.Cancel();
					pollThread.Join();
					httpTask.GetAwaiter().GetResult();
					return;
				}

				if (lastActivityAt.HasValue &&
					(DateTime.UtcNow - lastActivityAt.Value).TotalSeconds >= CompilationSettleSeconds) {
					cts.Cancel();
					pollThread.Join();
					ObserveCancelledRequest(httpTask, cts);
					if (hasErrors) {
						throw new Exception($"Package compilation failed: {errorDetails}");
					}
					return;
				}

				Thread.Sleep(500);
			}

			cts.Cancel();
			pollThread.Join();
			ObserveCancelledRequest(httpTask, cts);
			throw new TimeoutException($"Package compilation did not complete within {CompilationTimeoutMinutes} minutes.");

			async Task SendCompilationRequestAsync(CancellationToken cancellationToken) {
				using IOwnedApplicationClient client = CreateClient();
				using HttpResponseMessage _ = await client.ExecutePostRequestAsync(
					url, requestData, Timeout.Infinite, cancellationToken: cancellationToken).ConfigureAwait(false);
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
