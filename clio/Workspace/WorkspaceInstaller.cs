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

		/// <summary>
		/// Packs the workspace packages and installs them into the environment.
		/// </summary>
		/// <param name="packages">Workspace package names to install.</param>
		/// <param name="creatioPackagesZipName">Name of the zip that carries the packages; a default when <c>null</c>.</param>
		/// <param name="useApplicationInstaller">Whether the application installer is used instead of the package installer.</param>
		/// <param name="createBackup">Whether the environment backs the packages up before installing.</param>
		/// <exception cref="PackageItemDescriptorMissingException">
		/// A package holds a <c>Schemas/</c> or <c>Data/</c> folder without <c>descriptor.json</c>. Every package is
		/// packed before the first call to the environment, so nothing was reset or installed, and the exception
		/// names the folders of all packages.
		/// </exception>
		void Install(IEnumerable<string> packages, string creatioPackagesZipName = null,
			bool useApplicationInstaller = false, bool createBackup = true);

		/// <summary>
		/// Packs the workspace packages into a zip and copies it to a folder.
		/// </summary>
		/// <param name="packages">Workspace package names to publish.</param>
		/// <param name="zipFileName">Name of the resulting zip.</param>
		/// <param name="destionationFolderPath">Folder that receives the zip.</param>
		/// <param name="ovverideFile">Whether an existing zip is replaced.</param>
		/// <exception cref="PackageItemDescriptorMissingException">
		/// A package holds a <c>Schemas/</c> or <c>Data/</c> folder without <c>descriptor.json</c>; see
		/// <see cref="Install"/>.
		/// </exception>
		void Publish(IList<string> packages, string zipFileName, string destionationFolderPath, bool ovverideFile);

		//string PublishToFolder(string workspaceFolderPath, string zipFileName, string destinationFolderPath, bool overwrite);

		/// <summary>
		/// Packs the workspace packages into a zip in a folder, without contacting the environment.
		/// </summary>
		/// <param name="packages">Workspace package names to publish.</param>
		/// <param name="zipFileName">Name of the resulting zip.</param>
		/// <param name="destinationFolderPath">Folder that receives the zip; created when missing.</param>
		/// <param name="overwrite">Whether an existing zip is replaced.</param>
		/// <returns>Full path of the written zip.</returns>
		/// <exception cref="PackageItemDescriptorMissingException">
		/// A package holds a <c>Schemas/</c> or <c>Data/</c> folder without <c>descriptor.json</c>; see
		/// <see cref="Install"/>.
		/// </exception>
		string PublishToFolder(IEnumerable<string> packages, string zipFileName, string destinationFolderPath,
			bool overwrite);

		#endregion

	}

	#endregion

	#region Class: WorkspaceInstaller

	public class WorkspaceInstaller : IWorkspaceInstaller, IDisposable
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
		private readonly IWorkspacePackageFilter _workspacePackageFilter;
		private readonly Lazy<IOwnedApplicationClient> _applicationClientLazy;

		#endregion

		#region Constructors: Public

		public WorkspaceInstaller(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
			IApplicationClientFactory applicationClientFactory, IPackageInstaller packageInstaller,
			IPackageArchiver packageArchiver, IPackageBuilder packageBuilder,
			IStandalonePackageFileManager standalonePackageFileManager, IServiceUrlBuilder serviceUrlBuilder,
			IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem,
			IOSPlatformChecker osPlatformChecker, ILogger logger, 
			IWorkspacePackageFilter workspacePackageFilter, IApplicationInstaller applicationInstaller = null){
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
			// applicationInstaller can be null
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
			_workspacePackageFilter = workspacePackageFilter;
			_applicationClientLazy = new Lazy<IOwnedApplicationClient>(CreateClient);
		}

		#endregion

		#region Properties: Private

		private IApplicationClient ApplicationClient => _applicationClientLazy.Value;
		
		private string ResetSchemaChangeStateServiceUrl => _serviceUrlBuilder.Build(ResetSchemaChangeStateServicePath);

		#endregion

		#region Methods: Private

		private IOwnedApplicationClient CreateClient() => _applicationClientFactory.CreateOwnedClient(_environmentSettings);

		private void ResetSchemaChangeStateServiceUrlByPackage(string packageName) =>
			ApplicationClient.ExecutePostRequest(ResetSchemaChangeStateServiceUrl,
				"{\"packageName\":\"" + packageName + "\"}");

		private void PackPackage(string packageName, string rootPackedPackagePath){
			string packagePath = Path.Combine(_workspacePathBuilder.PackagesFolderPath, packageName);
			string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
			_packageArchiver.Pack(packagePath, packedPackagePath, true, true);
		}

		/// <summary>
		/// Packs every workspace package before anything is sent to the environment.
		/// </summary>
		/// <param name="packageNames">Workspace packages to pack.</param>
		/// <param name="rootPackedPackagePath">Folder that receives one <c>.gz</c> archive per package.</param>
		/// <exception cref="PackageItemDescriptorMissingException">
		/// One or more packages hold a <c>Schemas/</c> or <c>Data/</c> folder without <c>descriptor.json</c>. The
		/// remaining packages are still checked, so a single run names the folders of every package (issue #1749).
		/// </exception>
		private void PackPackages(IEnumerable<string> packageNames, string rootPackedPackagePath){
			List<PackageItemFolderWithoutDescriptor> foldersWithoutDescriptor = [];
			foreach (string packageName in packageNames) {
				try {
					PackPackage(packageName, rootPackedPackagePath);
				} catch (PackageItemDescriptorMissingException exception) {
					foldersWithoutDescriptor.AddRange(exception.Folders);
				}
			}
			if (foldersWithoutDescriptor.Count > 0) {
				throw new PackageItemDescriptorMissingException(foldersWithoutDescriptor);
			}
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

		private void InstallApplication(string applicationZip, bool useApplicationInstaller = false,
			bool createBackup = true){
			if (useApplicationInstaller && _applicationInstaller != null) {
				_logger.WriteInfo($"Installing workspace packages using ApplicationInstaller...");
				_applicationInstaller.Install(applicationZip, _environmentSettings, createBackup: createBackup);
				_logger.WriteInfo("Installation completed successfully.");
			} else {
				_logger.WriteInfo($"Installing workspace packages using PackageInstaller...");
				_packageInstaller.Install(applicationZip, _environmentSettings, createBackup: createBackup);
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

		public void Install(IEnumerable<string> packages, string creatioPackagesZipName = null,
			bool useApplicationInstaller = false, bool createBackup = true){
			creatioPackagesZipName ??= CreatioPackagesZipName;
			
			if (useApplicationInstaller && _applicationInstaller == null) {
				_logger.WriteWarning("ApplicationInstaller is not available. Falling back to PackageInstaller.");
				useApplicationInstaller = false;
			}
			
			List<string> packageNames = packages.ToList();
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath =
					CreateRootPackedPackageDirectory(creatioPackagesZipName, tempDirectory);
				// Pack everything first: a package the platform would reject must stop the push before the first
				// call that changes the environment.
				PackPackages(packageNames, rootPackedPackagePath);
				foreach (string packageName in packageNames) {
					ResetSchemaChangeStateServiceUrlByPackage(packageName);
				}
				var applicationZip = ZipPackages(creatioPackagesZipName, tempDirectory, rootPackedPackagePath);
				InstallApplication(applicationZip, useApplicationInstaller, createBackup);
				BuildStandalonePackagesIfNeeded();
			});
		}

		public void Publish(IList<string> packages, string zipFileName, string destionationFolderPath, bool overrideFile = false){
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string rootPackedPackagePath = CreateRootPackedPackageDirectory(zipFileName, tempDirectory);
				PackPackages(packages, rootPackedPackagePath);
				foreach (string packageName in packages) {
					ResetSchemaChangeStateServiceUrlByPackage(packageName);
				}
				var applicationZip = ZipPackages(zipFileName, tempDirectory, rootPackedPackagePath);
				_fileSystem.CopyFile(applicationZip, Path.Combine(destionationFolderPath, zipFileName), overrideFile);
			});
		}

		
		public string PublishToFolder(IEnumerable<string> packages, string zipFileName, string destinationFolderPath,
			bool overwrite){
			string resultApplicationFilePath = string.Empty;
			
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				var rootPackedPackagePath =
					CreateRootPackedPackageDirectory(zipFileName, tempDirectory);
				PackPackages(packages, rootPackedPackagePath);
				var applicationZip = ZipPackages(zipFileName, tempDirectory, rootPackedPackagePath);
				var filename = Path.GetFileName(applicationZip);
				resultApplicationFilePath = Path.Combine(destinationFolderPath, filename);
				_fileSystem.CreateDirectoryIfNotExists(destinationFolderPath);
				_fileSystem.CopyFile(applicationZip, resultApplicationFilePath, overwrite);
			});
			return resultApplicationFilePath;
		}

		/// <summary>Releases the factory-owned Creatio transport when it was created.</summary>
		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>Releases the factory-owned Creatio transport when disposal was requested.</summary>
		/// <param name="disposing">Whether managed resources should be released.</param>
		protected virtual void Dispose(bool disposing) {
			if (!disposing) {
				return;
			}
			if (_applicationClientLazy.IsValueCreated) {
				_applicationClientLazy.Value.Dispose();
			}
		}
		
		// [Obsolete("This method does not account for included and filtered packages. Use PublishToFolder with package list instead.")]
		// public string PublishToFolder(string workspaceFolderPath, string zipFileName, string destinationFolderPath,
		// 	bool overwrite){
		// 	_workspacePathBuilder.RootPath = workspaceFolderPath;
		// 	string resultApplicationFilePath = string.Empty;
		// 	
		// 	IEnumerable<string> packages = Directory.GetDirectories(_workspacePathBuilder.PackagesFolderPath)
		// 											.Select(p => new DirectoryInfo(p).Name).ToList();
		// 	
		// 	
		// 	_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
		// 		var rootPackedPackagePath =
		// 			CreateRootPackedPackageDirectory(zipFileName, tempDirectory);
		// 		foreach (string packageName in packages) {
		// 			PackPackage(packageName, rootPackedPackagePath);
		// 			//ResetSchemaChangeStateServiceUrl(packageName);
		// 		}
		// 		var applicationZip = ZipPackages(zipFileName, tempDirectory, rootPackedPackagePath);
		// 		var filename = Path.GetFileName(applicationZip);
		// 		resultApplicationFilePath = Path.Combine(destinationFolderPath, filename);
		// 		_fileSystem.CreateDirectoryIfNotExists(destinationFolderPath);
		// 		_fileSystem.CopyFile(applicationZip, resultApplicationFilePath, overwrite);
		// 	});
		// 	return resultApplicationFilePath;
		// }
		


		#endregion

	}

	#endregion
}
