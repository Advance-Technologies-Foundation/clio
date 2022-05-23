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

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly IWorkspaceRestorer _workspaceRestorer;
		private readonly IWorkspaceInstaller _workspaceInstaller;
		private readonly IPackageDownloader _packageDownloader;
		private readonly ICreatioSdk _creatioSdk;
		private readonly IJsonConverter _jsonConverter;
		private readonly IFileSystem _fileSystem;
		private readonly IApplicationPackageListProvider _applicationPackageListProvider;

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IWorkspacePathBuilder workspacePathBuilder,
				IWorkspaceRestorer workspaceRestorer, 
				IWorkspaceInstaller workspaceInstaller, IPackageDownloader packageDownloader, 
				ICreatioSdk creatioSdk, IJsonConverter jsonConverter, 
				IFileSystem fileSystem, IApplicationPackageListProvider applicationPackageListProvider) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
			workspaceRestorer.CheckArgumentNull(nameof(workspaceRestorer));
			workspaceInstaller.CheckArgumentNull(nameof(workspaceInstaller));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			creatioSdk.CheckArgumentNull(nameof(creatioSdk));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			_environmentSettings = environmentSettings;
			_workspacePathBuilder = workspacePathBuilder;
			_workspaceRestorer = workspaceRestorer;
			_workspaceInstaller = workspaceInstaller;
			_packageDownloader = packageDownloader;
			_creatioSdk = creatioSdk;
			_jsonConverter = jsonConverter;
			_fileSystem = fileSystem;
			_applicationPackageListProvider = applicationPackageListProvider;
			_workspaceSettings = new Lazy<WorkspaceSettings>(ReadWorkspaceSettings);
		}

		#endregion

		#region Properties: Private

		private string WorkspaceSettingsPath => _workspacePathBuilder.WorkspaceSettingsPath;
		private string PackagesPath => _workspacePathBuilder.PackagesDirectoryPath;
		private string ClioDirectoryPath => _workspacePathBuilder.ClioDirectoryPath;

		#endregion

		#region Properties: Public

		private readonly Lazy<WorkspaceSettings> _workspaceSettings;
		public WorkspaceSettings WorkspaceSettings => _workspaceSettings.Value;

		#endregion

		#region Methods: Private


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
			if (_fileSystem.DirectoryExists(ClioDirectoryPath)) {
				return;
			}
			_fileSystem.CreateDirectory(ClioDirectoryPath);
		}

		private void CreateWorkspaceSettingsFile(bool isAddingPackageNames = false) {
			if (_fileSystem.FileExists(WorkspaceSettingsPath)) {
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
			_workspaceRestorer.Restore(creatioSdkVersion);
		}

		public void Install(string creatioPackagesZipName = null) =>
			_workspaceInstaller.Install(WorkspaceSettings.Packages, creatioPackagesZipName);

		#endregion

	}

	#endregion

}