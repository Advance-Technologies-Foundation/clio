namespace Clio.Workspace
{
	using System;
	using System.IO;
	using Clio.Common;
	using Clio.Package;
	using Clio.Project.NuGet;

	#region Class: Workspace

	public class Workspace : IWorkspace
	{

		#region Constants: Private

		private const string PackagesFolderName = "packages";
		private const string ClioDirectoryName = ".clio";
		private const string WorkspaceSettingsJson = "workspaceSettings.json";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspaceRestorer _workspaceRestorer;
		private readonly IPackageDownloader _packageDownloader;
		private readonly IPackageInstaller _packageInstaller;
		private readonly IPackageArchiver _packageArchiver;
		private readonly ICreatioSDK _creatioSDK;
		private readonly IFileSystem _fileSystem;
		private readonly IJsonConverter _jsonConverter;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly string _rootPath;

		#endregion

		#region Constructors: Public

		public Workspace(EnvironmentSettings environmentSettings, IWorkspaceRestorer workspaceRestorer,
				IPackageDownloader packageDownloader, IPackageInstaller packageInstaller, 
				IPackageArchiver packageArchiver, ICreatioSDK creatioSDK, IJsonConverter jsonConverter, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspaceRestorer.CheckArgumentNull(nameof(workspaceRestorer));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			packageInstaller.CheckArgumentNull(nameof(packageInstaller));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			creatioSDK.CheckArgumentNull(nameof(creatioSDK));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_environmentSettings = environmentSettings;
			_workspaceRestorer = workspaceRestorer;
			_packageDownloader = packageDownloader;
			_packageInstaller = packageInstaller;
			_packageArchiver = packageArchiver;
			_creatioSDK = creatioSDK;
			_jsonConverter = jsonConverter;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_rootPath = _workingDirectoriesProvider.CurrentDirectory;
			_workspaceSettings = new Lazy<WorkspaceSettings>(ReadWorkspaceSettings);
		}

		#endregion

		#region Properties: Private

		private string WorkspaceSettingsPath => Path.Combine(_rootPath, ClioDirectoryName, WorkspaceSettingsJson);
		private string PackagesPath => Path.Combine(WorkspaceSettings.RootPath, PackagesFolderName);

		#endregion

		#region Properties: Public

		private readonly Lazy<WorkspaceSettings> _workspaceSettings;
		public WorkspaceSettings WorkspaceSettings => _workspaceSettings.Value;

		#endregion

		#region Methods: Private

		private WorkspaceSettings ReadWorkspaceSettings() {
			var workspaceSettings = _jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath);
			if (workspaceSettings != null) {
				workspaceSettings.RootPath = _rootPath;
			}
			return workspaceSettings;
		}
		

		private WorkspaceSettings CreateDefaultWorkspaceSettings() {
			WorkspaceSettings workspaceSettings = new WorkspaceSettings() {
				Name = "",
				ApplicationVersion = new Version(0, 0, 0, 0)
			};
			workspaceSettings.Environments.Add("", new WorkspaceEnvironment());
			return workspaceSettings;
		}

		private void CreateClioDirectory() {
			string clioDirectoryPath = Path.Combine(_rootPath, ClioDirectoryName);
			if (Directory.Exists(clioDirectoryPath)) {
				return;
			}
			Directory.CreateDirectory(clioDirectoryPath);
		}

		private void CreateWorkspaceSettingsFile() {
			if (File.Exists(WorkspaceSettingsPath)) {
				return;
			}
			WorkspaceSettings defaultWorkspaceSettings = CreateDefaultWorkspaceSettings();
			_jsonConverter.SerializeObjectToFile(defaultWorkspaceSettings, WorkspaceSettingsPath);
		}

		#endregion

		#region Methods: Public

		public void Create() {
			CreateClioDirectory();
			CreateWorkspaceSettingsFile();
		}

		public void Restore(string workspaceEnvironmentName) {
			Version creatioSdkVersion = _creatioSDK.FindSdkVersion(WorkspaceSettings.ApplicationVersion);
			_packageDownloader.DownloadPackages(WorkspaceSettings.Packages, PackagesPath);
			_workspaceRestorer.Restore(creatioSdkVersion);
		}

		public void Install(string workspaceEnvironmentName) {
			WorkspaceSettings workspaceSettings = WorkspaceSettings;
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string rootPackedPackagePath = Path.Combine(tempDirectory, workspaceSettings.Name);
				Directory.CreateDirectory(rootPackedPackagePath);
				foreach (string packageName in workspaceSettings.Packages) {
					string packagePath = Path.Combine(workspaceSettings.RootPath, PackagesFolderName, packageName);
					string packedPackagePath = Path.Combine(rootPackedPackagePath, $"{packageName}.gz");
					_packageArchiver.Pack(packagePath, packedPackagePath, true, true);
				}
				string applicationZip = Path.Combine(tempDirectory, $"{workspaceSettings.Name}.zip");
				_packageArchiver.ZipPackages(rootPackedPackagePath, 
					applicationZip, true);
				_packageInstaller.Install(applicationZip);
			});

		}

		#endregion

	}

	#endregion

}