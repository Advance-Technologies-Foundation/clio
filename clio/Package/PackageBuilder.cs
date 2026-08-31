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
			Thread pollThread = new(() => {
				_compilationHistoryPoller.Poll(baselineCreatedOn, cts.Token, record => {
					lastActivityAt = DateTime.UtcNow;
					if (!record.Result && !string.IsNullOrEmpty(record.ErrorsWarnings) && record.ErrorsWarnings != "[]") {
						hasErrors = true;
						errorDetails = record.ErrorsWarnings;
					}
				});
			});
			pollThread.Start();

			while (DateTime.UtcNow < timeoutAt) {
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
