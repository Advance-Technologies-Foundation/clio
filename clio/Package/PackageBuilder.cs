namespace Clio.Package
{
	using System.Collections.Generic;
	using Clio.Common;

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

		private static string BuildPackageUrl = @"/ServiceModel/WorkspaceExplorerService.svc/BuildPackage";
		private static string RebuildPackageUrl = @"/ServiceModel/WorkspaceExplorerService.svc/RebuildPackage";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public PackageBuilder(EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			logger.CheckArgumentNull(nameof(logger));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
		}
		#endregion

		#region Methods: Private

		private static string CreateRequestData(string packageName) =>
			"{ \"packageName\":\"" + packageName + "\" }";

		private IApplicationClient CreateClient() =>
			_applicationClientFactory.CreateClient(_environmentSettings);

		private string GetSafePackageName(string packageName) => packageName
			.Replace(" ", string.Empty)
			.Replace(",", "\",\"");

		private void Compilation(IEnumerable<string> packagesNames, bool force) {
			IApplicationClient applicationClient = CreateClient();
			string compilationUrl = force ? RebuildPackageUrl : BuildPackageUrl;
			string compilationName = force ? "rebuild" : "build";
			string fullBuildPackageUrl = _serviceUrlBuilder.Build(compilationUrl);
			foreach (string packageName in packagesNames) {
				string safePackageName = GetSafePackageName(packageName);
				_logger.WriteLine($"Start {compilationName} packages ({safePackageName}).");
				var requestData = CreateRequestData(safePackageName);
				applicationClient.ExecutePostRequest(fullBuildPackageUrl, requestData);
				_logger.WriteLine($"End {compilationName} packages ({safePackageName}).");
			}
		}

		#endregion

		#region Methods: Public

		public void Build(IEnumerable<string> packagesNames) =>
			Compilation(packagesNames, false);

		public void Rebuild(IEnumerable<string> packagesNames) =>
			Compilation(packagesNames, true);

		#endregion

	}

	#endregion

}