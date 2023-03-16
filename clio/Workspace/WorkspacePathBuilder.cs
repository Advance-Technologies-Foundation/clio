namespace Clio.Workspace
{
	using System;
	using System.IO;
	using Clio.Common;

	#region Class: WorkspacePathBuilder

	public class WorkspacePathBuilder : IWorkspacePathBuilder
	{

		#region Constants: Private

		private const string PackagesFolderName = "packages";
		private const string ProjectsFolderName = "projects";
		private const string ClioDirectoryName = ".clio";
		private const string WorkspaceSettingsJson = "workspaceSettings.json";
		private const string WorkspaceEnvironmentSettingsJson = "workspaceEnvironmentSettings.json";
		private const string SolutionName = "CreatioPackages.sln";
		private const string NugetFolderName = ".nuget";
		private const string SolutionFolderName = ".solution";
		private const string TasksFolderName = "tasks";
		private const string ApplicationFolderName = ".application";
		private const string NetCoreFolderName = "net-core";
		private const string NetFrameworkFolderName = "net-framework";
		private const string CoreBinFolderName = "core-bin";
		private const string LibFolderName = "Lib";
		private const string ConfigurationBinFolderName = "bin";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public WorkspacePathBuilder(EnvironmentSettings environmentSettings, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_environmentSettings = environmentSettings;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_rootPathLazy = new Lazy<string>(GetRootPath);

		}

		#endregion


		#region Properties: Private

		private string RootApplicationFolderPath  => Path.Combine(RootPath, ApplicationFolderName);

		#endregion


		#region Properties: Public

		private readonly Lazy<string> _rootPathLazy;
		public string RootPath => _rootPathLazy.Value;
		public bool IsWorkspace => _fileSystem.ExistsFile(WorkspaceSettingsPath);
		public string ClioDirectoryPath => Path.Combine(RootPath, ClioDirectoryName);

		public string WorkspaceSettingsPath => Path.Combine(ClioDirectoryPath, WorkspaceSettingsJson);
		public string WorkspaceEnvironmentSettingsPath =>
			Path.Combine(ClioDirectoryPath, WorkspaceEnvironmentSettingsJson);

		public string PackagesFolderPath => Path.Combine(RootPath, PackagesFolderName);
		public string ProjectsFolderPath => Path.Combine(RootPath, ProjectsFolderName);

		public string SolutionFolderPath => Path.Combine(RootPath, SolutionFolderName);
		public string SolutionPath => Path.Combine(SolutionFolderPath, SolutionName);

		public string NugetFolderPath => Path.Combine(RootPath, NugetFolderName);
		public string TasksFolderPath => Path.Combine(RootPath, TasksFolderName);
		public string ApplicationFolderPath => _environmentSettings.IsNetCore 
			? Path.Combine(RootApplicationFolderPath, NetCoreFolderName)
			: Path.Combine(RootApplicationFolderPath, NetFrameworkFolderName);
		public string CoreBinFolderPath => Path.Combine(ApplicationFolderPath, CoreBinFolderName);
		public string LibFolderPath => Path.Combine(ApplicationFolderPath, LibFolderName);
		public string ConfigurationBinFolderPath => Path.Combine(ApplicationFolderPath, ConfigurationBinFolderName);

		#endregion

		#region Methods: Private

		private string GetRootPath() {
			string currentDirectory = _workingDirectoriesProvider.CurrentDirectory;
			DirectoryInfo directoryInfo = new DirectoryInfo(currentDirectory);
			while (true) {
				string presumablyClioDirectoryPath = BuildClioDirectoryPath(directoryInfo.FullName);
				if (_fileSystem.ExistsDirectory(presumablyClioDirectoryPath)) {
					return directoryInfo.FullName;
				}
				if (directoryInfo.Parent == null) {
					return currentDirectory;
				}
				directoryInfo = directoryInfo.Parent;
			}
		}

		private string BuildClioDirectoryPath(string rootPath) => Path.Combine(rootPath, ClioDirectoryName);

		#endregion

		#region Methods: Public

		public string BuildPackagePath(string packageName) => Path.Combine(PackagesFolderPath, packageName);

		public string BuildFrameworkCreatioSdkPath(Version nugetVersion) =>
			Path.Combine(NugetFolderPath, "creatiosdk", nugetVersion.ToString(), "lib",
				"net40");

		public string BuildCoreCreatioSdkPath(Version nugetVersion) =>
			Path.Combine(NugetFolderPath, "creatiosdk", nugetVersion.ToString(), "lib",
				"netstandard2.0");

		public string BuildRelativePathRegardingPackageProjectPath(string destinationPath) {
			string templatePackageProjectRelativeDirectoryPath =
				Path.Combine(PackagesFolderPath, "PackageName", "Files");
			return Path.GetRelativePath(templatePackageProjectRelativeDirectoryPath, destinationPath);
		}

		#endregion

	}

	#endregion

}