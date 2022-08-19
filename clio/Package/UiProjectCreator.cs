namespace Clio.Package
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text.RegularExpressions;
	using Clio.Common;
	using Clio.Workspace;

	#region Interface: IUiProjectCreator

	public interface IUiProjectCreator
	{

		#region Methods: Public

		void Create(string projectName, string packageName, string vendorPrefix, bool isEmpty,
			Func<string, bool> enableDownloadPackage);

		#endregion

	}

	#endregion

	#region Class: UiProjectCreator

	public class UiProjectCreator : IUiProjectCreator
	{

		#region Constants: Private

		private const string packagesDirectoryName = "packages";
		private const string projectsDirectoryName = "projects";

		#endregion

		#region Fields: Private

		private static string[] _templateExtensions = new[] {
			".json", ".js", ".ts", ".conf", ".config", ".scss", ".css"
		};
		private readonly EnvironmentSettings _environmentSettings;
		private readonly IWorkspace _workspace;
		private readonly IApplicationPackageListProvider _applicationPackageListProvider;
		private readonly IPackageCreator _packageCreator;
		private readonly IPackageDownloader _packageDownloader;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public UiProjectCreator(EnvironmentSettings environmentSettings, IWorkspace workspace,
				IApplicationPackageListProvider applicationPackageListProvider, IPackageCreator packageCreator,
				IPackageDownloader packageDownloader, IWorkspacePathBuilder workspacePathBuilder,
				ITemplateProvider templateProvider, IWorkingDirectoriesProvider workingDirectoriesProvider,
				IFileSystem fileSystem) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspace.CheckArgumentNull(nameof(workspace));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			packageCreator.CheckArgumentNull(nameof(packageCreator));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_environmentSettings = environmentSettings;
			_workspace = workspace;
			_applicationPackageListProvider = applicationPackageListProvider;
			_packageCreator = packageCreator;
			_packageDownloader = packageDownloader;
			_workspacePathBuilder = workspacePathBuilder;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Properties: Private

		private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;
		private string PackagesPath => IsWorkspace
				? _workspacePathBuilder.PackagesFolderPath
				: Path.Combine(_workingDirectoriesProvider.CurrentDirectory, packagesDirectoryName);
		private string ProjectsPath => IsWorkspace
				? _workspacePathBuilder.ProjectsFolderPath
				: Path.Combine(_workingDirectoriesProvider.CurrentDirectory, projectsDirectoryName);

		#endregion

		#region Methods: Private

		private void UpdateTemplateInfo(string projectPath, string projectName, string packageName,
				string vendorPrefix) {
			IEnumerable<string> filesPaths = _fileSystem
				.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
				.Where(f => _templateExtensions.Any(e => f.ToLower().EndsWith(e)));
			foreach (string filePath in filesPaths) {
				string tplContent = _fileSystem.ReadAllText(filePath);
				tplContent = tplContent.Replace("<%vendorPrefix%>", vendorPrefix);
				tplContent = tplContent.Replace("<%projectName%>", projectName);
				tplContent = tplContent.Replace("<%distPath%>",
					$"{Path.Combine("../../", "packages/", packageName + "/", "Files/", "src/","js/", projectName)}");
				_fileSystem.WriteAllTextToFile(filePath, tplContent);
			}
		}

		private void CreatePackage(string packageName) {
			_packageCreator.Create(PackagesPath, packageName);
		}

		private void CreateProject(string projectName, string packageName, string vendorPrefix, bool isEmpty) {
			_fileSystem.CreateDirectoryIfNotExists(ProjectsPath);
			var projectPath = Path.Combine(ProjectsPath, projectName);
			string templateFolderName = isEmpty ? "ui-project-Empty" : "ui-project";
			_templateProvider.CopyTemplateFolder(templateFolderName, projectPath);
			UpdateTemplateInfo(projectPath, projectName, packageName, vendorPrefix);
		}

		private void CheckCorrectProjectName(string projectName) {
			var namePattern = new Regex("^([0-9a-z_]+)$");
			if (namePattern.IsMatch(projectName)) {
				return;
			}
			throw new ArgumentException("Not correct project name. Use only 'snake_case' format");
		}

		private PackageInfo FindExistingPackage(string packageName) {
			try {
				IEnumerable<PackageInfo> packages = _applicationPackageListProvider.GetPackages();
				var package = packages.FirstOrDefault(p =>
					p.Descriptor.Name.Equals(packageName, StringComparison.InvariantCultureIgnoreCase));
				return package;
			} catch (Exception e) {
				return null;
			}
		}

		#endregion

		#region Methods: Public

		public void Create(string projectName, string packageName, string vendorPrefix, bool isEmpty,
				Func<string, bool> enableDownloadPackage) {
			CheckCorrectProjectName(projectName);
			var package = FindExistingPackage(packageName);
			if (package != null && enableDownloadPackage(packageName)) {
				_packageDownloader.DownloadPackage(packageName, _environmentSettings,
					_workspacePathBuilder.PackagesFolderPath);
				_workspace.AddPackageIfNeeded(packageName);
			} else {
				CreatePackage(packageName);
			}
			CreateProject(projectName, packageName, vendorPrefix, isEmpty);
		}

		#endregion

	}

	#endregion

}