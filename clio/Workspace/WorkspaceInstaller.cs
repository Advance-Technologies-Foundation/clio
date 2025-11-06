using System;
using Clio.Utilities;

namespace Clio.Workspaces
{
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using Clio.Common;
	using Clio.Package;
	using Terrasoft.Core;

	#region Interface: IWorkspaceInstaller

	public interface IWorkspaceInstaller
	{

		#region Methods: Public

		void Install(IEnumerable<string> packages, string creatioPackagesZipName = null, bool useApplicationInstaller = false);

		void Publish(IList<string> packages, string zipFileName, string destionationFolderPath, bool ovverideFile);

		string PublishToFolder(string workspaceFolderPath, string zipFileName, string destinationFolderPath, bool overwrite);

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
		private readonly IApplicationInstaller _applicationInstaller;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IPackageBuilder _packageBuilder;
		private readonly IStandalonePackageFileManager _standalonePackageFileManager;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly IOSPlatformChecker _osPlatformChecker;
		private readonly ILogger _logger;
		private readonly Lazy<IApplicationClient> _applicationClientLazy;

		#endregion

		#region Constructors: Public

		public WorkspaceInstaller(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
			IApplicationClientFactory applicationClientFactory, IPackageInstaller packageInstaller,
			IPackageArchiver packageArchiver, IPackageBuilder packageBuilder,
			IStandalonePackageFileManager standalonePackageFileManager, IServiceUrlBuilder serviceUrlBuilder,
			IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem,
			IOSPlatformChecker osPlatformChecker, ILogger logger, IApplicationInstaller applicationInstaller = null){
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
			// applicationInstaller может быть null
			_environmentSettings = environmentSettings;
			_workspacePathBuilder = workspacePathBuilder;
			_applicationClientFactory = applicationClientFactory;
			_packageInstaller = packageInstaller;
			_applicationInstaller = applicationInstaller;
			_packageArchiver = packageArchiver;
			_packageBuilder = packageBuilder;
			_standalonePackageFileManager = standalonePackageFileManager;
			_serviceUrlBuilder = serviceUrlBuilder;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_osPlatformChecker = osPlatformChecker;
			_logger = logger;
			_applicationClientLazy = new Lazy<IApplicationClient>(CreateClient);
		}

		#endregion

		#region Properties: Private

		private IApplicationClient ApplicationClient => _applicationClientLazy.Value;
		
		private string ResetSchemaChangeStateServiceUrl => _serviceUrlBuilder.Build(ResetSchemaChangeStateServicePath);

		#endregion

		#region Methods: Private

		private IApplicationClient CreateClient() => _applicationClientFactory.CreateClient(_environmentSettings);

		private void ResetSchemaChangeStateServiceUrlByPackage(string packageName) =>
			ApplicationClient.ExecutePostRequest(ResetSchemaChangeStateServiceUrl,
				"{\"packageName\":\"" + packageName + "\"}");

		private void PackPackage(string packageName, string rootPackedPackagePath){
			string packagePath = Path.Combine(_workspacePathBuilder.PackagesFolderPath, packageName);
			string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
			_packageArchiver.Pack(packagePath, packedPackagePath, true, true);
		}

		private string CreateRootPackedPackageDirectory(string creatioPackagesZipName, string tempDirectory){
			string rootPackedPackagePath = Path.Combine(tempDirectory, creatioPackagesZipName);
			_fileSystem.CreateDirectory(rootPackedPackagePath);
			return rootPackedPackagePath;
		}

		private string ZipPackages(string creatioPackagesZipName, string tempDirectory, string rootPackedPackagePath){
			string applicationZip = Path.Combine(tempDirectory, $"{creatioPackagesZipName}.zip");
			_packageArchiver.ZipPackages(rootPackedPackagePath,
				applicationZip, true);
			return applicationZip;
		}

		private void InstallApplication(string applicationZip, bool useApplicationInstaller = false){
			if (useApplicationInstaller && _applicationInstaller != null) {
				_logger.WriteInfo($"Installing workspace packages using ApplicationInstaller...");
				_applicationInstaller.Install(applicationZip, _environmentSettings);
				_logger.WriteInfo("Installation completed successfully.");
			} else {
				_packageInstaller.Install(applicationZip, _environmentSettings);
			}
		}

		private void BuildStandalonePackagesIfNeeded(){
			if (_osPlatformChecker.IsWindowsEnvironment || _environmentSettings.IsNetCore) {
				return;
			}
			IEnumerable<string> standalonePackagesNames = _standalonePackageFileManager
				.FindStandalonePackagesNames(_workspacePathBuilder.PackagesFolderPath);
			_packageBuilder.Build(standalonePackagesNames);
		}

		#endregion

		#region Methods: Public

		public void Install(IEnumerable<string> packages, string creatioPackagesZipName = null, bool useApplicationInstaller = false){
			creatioPackagesZipName ??= CreatioPackagesZipName;
			
			if (useApplicationInstaller && _applicationInstaller == null) {
				_logger.WriteWarning("ApplicationInstaller is not available. Falling back to PackageInstaller.");
				useApplicationInstaller = false;
			}
			
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath =
					CreateRootPackedPackageDirectory(creatioPackagesZipName, tempDirectory);
				foreach (string packageName in packages) {
					PackPackage(packageName, rootPackedPackagePath);
					ResetSchemaChangeStateServiceUrlByPackage(packageName);
				}
				var applicationZip = ZipPackages(creatioPackagesZipName, tempDirectory, rootPackedPackagePath);
				InstallApplication(applicationZip, useApplicationInstaller);
				BuildStandalonePackagesIfNeeded();
			});
		}

		public void Publish(IList<string> packages, string zipFileName, string destionationFolderPath,
			bool overrideFile = false){
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath =
					CreateRootPackedPackageDirectory(zipFileName, tempDirectory);
				foreach (string packageName in packages) {
					PackPackage(packageName, rootPackedPackagePath);
					ResetSchemaChangeStateServiceUrlByPackage(packageName);
				}
				var applicationZip = ZipPackages(zipFileName, tempDirectory, rootPackedPackagePath);
				_fileSystem.CopyFile(applicationZip, Path.Combine(destionationFolderPath, zipFileName), overrideFile);
			});
		}

		public string PublishToFolder(string workspaceFolderPath, string zipFileName, string destinationFolderPath,
			bool overwrite){
			_workspacePathBuilder.RootPath = workspaceFolderPath;
			string resultApplicationFilePath = string.Empty;
			var packages = Directory.GetDirectories(_workspacePathBuilder.PackagesFolderPath)
				.Select(p => new DirectoryInfo(p).Name);
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath =
					CreateRootPackedPackageDirectory(zipFileName, tempDirectory);
				foreach (string packageName in packages) {
					PackPackage(packageName, rootPackedPackagePath);
					//ResetSchemaChangeStateServiceUrl(packageName);
				}
				var applicationZip = ZipPackages(zipFileName, tempDirectory, rootPackedPackagePath);
				var filename = Path.GetFileName(applicationZip);
				resultApplicationFilePath = Path.Combine(destinationFolderPath, filename);
				_fileSystem.CreateDirectoryIfNotExists(destinationFolderPath);
				_fileSystem.CopyFile(applicationZip, resultApplicationFilePath, overwrite);
			});
			return resultApplicationFilePath;
		}
		


		#endregion

	}

	#endregion
}
