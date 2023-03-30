using System;
using Clio.Utilities;

namespace Clio.Workspace
{
	using System.Collections.Generic;
	using System.IO;
	using Clio.Common;
	using Clio.Package;

	#region Interface: IWorkspaceInstaller

	public interface IWorkspaceInstaller
	{

		#region Methods: Public
		void Install(IEnumerable<string> packages, string creatioPackagesZipName = null);

		#endregion

	}

	#endregion

	#region Class: WorkspaceInstaller

	public class WorkspaceInstaller : IWorkspaceInstaller
	{

		#region Constants: Private

		private const string CreatioPackagesZipName = "CreatioPackages";
		private const string ResetSchemaChangeStateServicePath = @"/rest/CreatioApiGateway/ResetSchemaChangeState";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IPackageBuilder _packageBuilder;
		private readonly IStandalonePackageFileManager _standalonePackageFileManager;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly IOSPlatformChecker _osPlatformChecker;
		private readonly Lazy<IApplicationClient> _applicationClientLazy;
		private readonly string _resetSchemaChangeStateServiceUrl;

		#endregion

		#region Constructors: Public

		public WorkspaceInstaller(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
				IApplicationClientFactory applicationClientFactory, IPackageInstaller packageInstaller, 
				IPackageArchiver packageArchiver, IPackageBuilder packageBuilder,
				IStandalonePackageFileManager standalonePackageFileManager, IServiceUrlBuilder serviceUrlBuilder, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem,
				IOSPlatformChecker osPlatformChecker) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			packageBuilder.CheckArgumentNull(nameof(packageBuilder));
			standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			osPlatformChecker.CheckArgumentNull(nameof(osPlatformChecker));
			_environmentSettings = environmentSettings;
			_workspacePathBuilder = workspacePathBuilder;
			_applicationClientFactory = applicationClientFactory;
			_packageInstaller = packageInstaller;
			_packageArchiver = packageArchiver;
			_packageBuilder = packageBuilder;
			_standalonePackageFileManager = standalonePackageFileManager;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_osPlatformChecker = osPlatformChecker;
			_applicationClientLazy = new Lazy<IApplicationClient>(CreateClient);
			_resetSchemaChangeStateServiceUrl = serviceUrlBuilder.Build(ResetSchemaChangeStateServicePath);

		}

		#endregion

		#region Properties: Private

		private IApplicationClient ApplicationClient => _applicationClientLazy.Value;

		#endregion

		#region Methods: Private

		private IApplicationClient CreateClient() =>
			_applicationClientFactory.CreateClient(_environmentSettings);

		private void ResetSchemaChangeStateServiceUrl(string packageName) =>
			ApplicationClient.ExecutePostRequest(_resetSchemaChangeStateServiceUrl,
				"{\"packageName\":\"" + packageName + "\"}");

		private void PackPackage(string packageName, string rootPackedPackagePath) {
			string packagePath = Path.Combine(_workspacePathBuilder.PackagesFolderPath, packageName);
			string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
			_packageArchiver.Pack(packagePath, packedPackagePath, true, true);
		}

		private string CreateRootPackedPackageDirectory(string creatioPackagesZipName, string tempDirectory) {
			string rootPackedPackagePath = Path.Combine(tempDirectory, creatioPackagesZipName);
			_fileSystem.CreateDirectory(rootPackedPackagePath);
			return rootPackedPackagePath;
		}

		private string ZipPackages(string creatioPackagesZipName, string tempDirectory, string rootPackedPackagePath) {
			string applicationZip = Path.Combine(tempDirectory, $"{creatioPackagesZipName}.zip");
			_packageArchiver.ZipPackages(rootPackedPackagePath,
				applicationZip, true);
			return applicationZip;
		}


		private void InstallApplication(string applicationZip) {
			_packageInstaller.Install(applicationZip, _environmentSettings);
		}

		private void BuildStandalonePackagesIfNeeded() {
			if (_osPlatformChecker.IsWindowsEnvironment || _environmentSettings.IsNetCore) {
				return;
			}
			IEnumerable<string> standalonePackagesNames = _standalonePackageFileManager
				.FindStandalonePackagesNames(_workspacePathBuilder.PackagesFolderPath);
			_packageBuilder.Build(standalonePackagesNames);
		}

		#endregion

		#region Methods: Public

		public void Install(IEnumerable<string> packages, string creatioPackagesZipName = null) {
			creatioPackagesZipName ??= CreatioPackagesZipName;
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath = 
					CreateRootPackedPackageDirectory(creatioPackagesZipName, tempDirectory);
				foreach (string packageName in packages) {
					PackPackage(packageName, rootPackedPackagePath);
					ResetSchemaChangeStateServiceUrl(packageName);
				}
				var applicationZip = ZipPackages(creatioPackagesZipName, tempDirectory, rootPackedPackagePath);
				InstallApplication(applicationZip);
				BuildStandalonePackagesIfNeeded();
			});
		}

		#endregion

	}

	#endregion

}