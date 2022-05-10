using Clio.UserEnvironment;

namespace Clio.Workspace
{
	using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Clio.Common;
	using Clio.Package;
	using Clio.Project.NuGet;

	#region Class: Workspace

	public class Workspace : IWorkspace
	{

		
		#region Constants: Private

		private const string CreatioPackagesZipName = "CreatioPackages";
		private const string ResetSchemaChangeStateServicePath = @"/rest/CreatioApiGateway/ResetSchemaChangeState";

		#endregion

	
		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IWorkspaceRestorer _workspaceRestorer;
		private readonly IPackageDownloader _packageDownloader;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IPackageArchiver _packageArchiver;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ICreatioSdk _creatioSdk;
		private readonly IJsonConverter _jsonConverter;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly string _rootPath;
		private readonly IApplicationPackageListProvider _applicationPackageListProvider;

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
				IApplicationClientFactory applicationClientFactory, IWorkspaceRestorer workspaceRestorer, 
				IPackageDownloader packageDownloader, IPackageInstaller packageInstaller, 
				IPackageArchiver packageArchiver, IServiceUrlBuilder serviceUrlBuilder, ICreatioSdk creatioSdk, 
				IJsonConverter jsonConverter, IWorkingDirectoriesProvider workingDirectoriesProvider, 
				IFileSystem fileSystem, IApplicationPackageListProvider applicationPackageListProvider) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			workspaceRestorer.CheckArgumentNull(nameof(workspaceRestorer));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			creatioSdk.CheckArgumentNull(nameof(creatioSdk));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			_environmentSettings = environmentSettings;
			_workspacePathBuilder = workspacePathBuilder;
			_applicationClientFactory = applicationClientFactory;
			_workspaceRestorer = workspaceRestorer;
			_packageDownloader = packageDownloader;
			_packageInstaller = packageInstaller;
			_packageArchiver = packageArchiver;
			_serviceUrlBuilder = serviceUrlBuilder;
			_creatioSdk = creatioSdk;
			_jsonConverter = jsonConverter;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_rootPath = GetRootPath();
			_workspaceSettings = new Lazy<WorkspaceSettings>(ReadWorkspaceSettings);
			_applicationPackageListProvider = applicationPackageListProvider;
		}

		#endregion

		#region Properties: Private

		private string WorkspaceSettingsPath => _workspacePathBuilder.BuildWorkspaceSettingsPath(_rootPath);
		private string PackagesPath => _workspacePathBuilder.BuildPackagesDirectoryPath(_rootPath);
		private string ClioDirectoryPath => _workspacePathBuilder.BuildClioDirectoryPath(_rootPath);

		#endregion

		#region Properties: Public

		private readonly Lazy<WorkspaceSettings> _workspaceSettings;
		public WorkspaceSettings WorkspaceSettings => _workspaceSettings.Value;

		#endregion

		#region Methods: Private

		private string GetRootPath() {
			string currentDirectory = _workingDirectoriesProvider.CurrentDirectory;
			DirectoryInfo directoryInfo = new DirectoryInfo(currentDirectory);
			while (true) {
				string presumablyClioDirectoryPath = 
					_workspacePathBuilder.BuildClioDirectoryPath(directoryInfo.FullName); 
				if (Directory.Exists(presumablyClioDirectoryPath)) {
					return directoryInfo.FullName;
				}
				if (directoryInfo.Parent == null) {
					return currentDirectory;
				}
				directoryInfo = directoryInfo.Parent;
			}
		}

		private WorkspaceSettings ReadWorkspaceSettings() =>
			_jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath);

		private WorkspaceSettings CreateDefaultWorkspaceSettings(string[] packages) {
			Version lv = _creatioSdk.LastVersion;
			WorkspaceSettings workspaceSettings = new WorkspaceSettings {
				ApplicationVersion = new Version(lv.Major, lv.Minor, lv.Build),
				Packages = packages
			};
			return workspaceSettings;
		}

		private void CreateClioDirectory() {
			if (Directory.Exists(ClioDirectoryPath)) {
				return;
			}
			Directory.CreateDirectory(ClioDirectoryPath);
		}

		private void CreateWorkspaceSettingsFile(bool isAddingPackageNames = false) {
			if (File.Exists(WorkspaceSettingsPath)) {
				return;
			}
			string[] packages = new string[] { };
			if (isAddingPackageNames) {
				IEnumerable<PackageInfo> packagesInfo =
					_applicationPackageListProvider.GetPackages("{\"isCustomer\": \"true\"}");
				packages = packagesInfo.Select(s => s.Descriptor.Name).ToArray();
			}
			WorkspaceSettings defaultWorkspaceSettings = CreateDefaultWorkspaceSettings(packages);
			_jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceSettingsPath);
		}

		#endregion

		#region Methods: Public

		public void Create(bool isAddingPackageNames = false) {
			CreateClioDirectory();
			CreateWorkspaceSettingsFile(isAddingPackageNames);
		}

		public void Restore() {
			Version creatioSdkVersion = _creatioSdk.FindSdkVersion(WorkspaceSettings.ApplicationVersion);
			_packageDownloader.DownloadPackages(WorkspaceSettings.Packages, _environmentSettings, PackagesPath);
			_workspaceRestorer.Restore(_rootPath, creatioSdkVersion);
		}

		public void Install(string creatioPackagesZipName = null) {
			creatioPackagesZipName ??= CreatioPackagesZipName;
			IApplicationClient applicationClient = _applicationClientFactory.CreateClient(_environmentSettings);
			applicationClient.Login();
			string resetSchemaChangeStateServiceUrl = 
				_serviceUrlBuilder.Build(ResetSchemaChangeStateServicePath);
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string rootPackedPackagePath = Path.Combine(tempDirectory, creatioPackagesZipName);
				Directory.CreateDirectory(rootPackedPackagePath);
				foreach (string packageName in WorkspaceSettings.Packages) {
					string packagePath = Path.Combine(PackagesPath, packageName);
					string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
					_packageArchiver.Pack(packagePath, packedPackagePath, true, true);
					applicationClient.ExecutePostRequest(resetSchemaChangeStateServiceUrl,
						"{\"packageName\":\"" + packageName + "\"}");
				}
				string applicationZip = Path.Combine(tempDirectory, $"{creatioPackagesZipName}.zip");
				_packageArchiver.ZipPackages(rootPackedPackagePath, 
					applicationZip, true);
				_packageInstaller.Install(applicationZip, _environmentSettings);
			});
		}

		#endregion

	}

	#endregion

}