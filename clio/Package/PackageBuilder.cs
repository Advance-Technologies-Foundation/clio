namespace Clio.Package
{
	using System;
	using System.Threading;
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

		private IApplicationClient CreateClient() => _applicationClientFactory.CreateClient(_environmentSettings);

		private string GetSafePackageName(string packageName) =>
			packageName
				.Replace(" ", string.Empty)
				.Replace(",", "\",\"");

		private void Compilation(IEnumerable<string> packagesNames, bool force) {
			IApplicationClient applicationClient = CreateClient();
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
					CompileWithPolling(applicationClient, fullBuildPackageUrl, requestData);
				} else {
					applicationClient.ExecutePostRequest(fullBuildPackageUrl, requestData);
				}

				_logger.WriteLine($"End {compilationName} packages ({safePackageName}).");
			}
		}

		// In Creatio 8.3.3+, RebuildPackage no longer sends back an HTTP response —
		// the server compiles in the background and drops the connection. We fire the
		// request in a daemon thread and use the same CompilationHistoryPoller pattern
		// as CompileConfigurationCommand to detect completion via OData.
		private void CompileWithPolling(IApplicationClient client, string url, string requestData) {
			CompilationHistory baseline = _compilationHistoryPoller.GetBaseline();
			DateTime baselineCreatedOn = baseline?.CreatedOn ?? DateTime.MinValue;

			Exception httpException = null;
			bool httpDone = false;

			Thread httpThread = new(() => {
				try {
					client.ExecutePostRequest(url, requestData);
				} catch (Exception ex) {
					httpException = ex;
				} finally {
					httpDone = true;
				}
			}) { IsBackground = true };
			httpThread.Start();

			DateTime timeoutAt = DateTime.UtcNow.AddMinutes(CompilationTimeoutMinutes);
			DateTime? lastActivityAt = null;
			bool hasErrors = false;
			string errorDetails = null;

			using CancellationTokenSource cts = new();
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
				if (httpDone) {
					cts.Cancel();
					pollThread.Join();
					if (httpException is not null) {
						throw httpException;
					}
					return;
				}

				if (lastActivityAt.HasValue &&
					(DateTime.UtcNow - lastActivityAt.Value).TotalSeconds >= CompilationSettleSeconds) {
					cts.Cancel();
					pollThread.Join();
					if (hasErrors) {
						throw new Exception($"Package compilation failed: {errorDetails}");
					}
					return;
				}

				Thread.Sleep(500);
			}

			cts.Cancel();
			pollThread.Join();
			throw new TimeoutException($"Package compilation did not complete within {CompilationTimeoutMinutes} minutes.");
		}

		#endregion

		#region Methods: Public

		public void Build(IEnumerable<string> packagesNames) => Compilation(packagesNames, false);

		public void Rebuild(IEnumerable<string> packagesNames) => Compilation(packagesNames, true);

		#endregion

	}

	#endregion
}
