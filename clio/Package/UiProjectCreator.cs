using System;

namespace Clio.Package
{
	using System.IO;
	using System.Text.RegularExpressions;
	using Clio.Common;
	using Clio.Workspace;

	#region Interface: IUiProjectCreator

	public interface IUiProjectCreator
	{

		#region Methods: Public

		void Create(string projectName, string packageName, string vendorPrefix);

		#endregion

	}

	#endregion

	#region Class: UiProjectCreator

	public class UiProjectCreator : IUiProjectCreator
	{

		#region Constants: Private

		private const string packagesDirectoryName = "packages";
		private const string projectsDirectoryName = "projects";
		private const string angularFileName = "angular.json";
		private const string packageFileName = "package.json";
		private const string webpackConfigFileName = "webpack.config.js";

		#endregion

		#region Fields: Private

		private readonly IApplicationPackageListProvider _applicationPackageListProvider;
		private readonly IPackageCreator _packageCreator;
		private readonly IWorkspacePathBuilder _workspacePathBuilder;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem; 

		#endregion

		#region Constructors: Public

		public UiProjectCreator(IApplicationPackageListProvider applicationPackageListProvider, 
				IPackageCreator packageCreator, IWorkspacePathBuilder workspacePathBuilder,
				ITemplateProvider templateProvider, IWorkingDirectoriesProvider workingDirectoriesProvider,
				IFileSystem fileSystem) {
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			packageCreator.CheckArgumentNull(nameof(packageCreator));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_applicationPackageListProvider = applicationPackageListProvider;
			_packageCreator = packageCreator;
			_workspacePathBuilder = workspacePathBuilder;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Properties: Private

		private bool IsWorkspace => _fileSystem.ExistsFile(_workspacePathBuilder.WorkspaceSettingsPath);
		private string PackagesPath => IsWorkspace
				? _workspacePathBuilder.PackagesFolderPath
				: Path.Combine(_workingDirectoriesProvider.CurrentDirectory, packagesDirectoryName);
		private string ProjectsPath => IsWorkspace
				? _workspacePathBuilder.ProjectsFolderPath
				: Path.Combine(_workingDirectoriesProvider.CurrentDirectory, projectsDirectoryName);

		#endregion

		#region Methods: Private

		private void UpdateTemplateInfo(string projectPath, string fileName, string projectName, string packageName,
				string vendorPrefix) {
			string filePath = Path.Combine(projectPath, fileName);
			string tplContent = _fileSystem.ReadAllText(filePath);
			tplContent = tplContent.Replace("<%vendorPrefix%>", vendorPrefix);
			tplContent = tplContent.Replace("<%projectName%>", projectName);
			tplContent = tplContent.Replace("<%distPath%>",
				$"{Path.Combine("../", "packages/", packageName + "/", "Files/", "src/","js/", projectName)}");
			_fileSystem.WriteAllTextToFile(filePath, tplContent);
		}

		private void CreatePackage(string packageName) {
			_packageCreator.Create(PackagesPath, packageName);
		}

		private void CreateProject(string projectName, string packageName, string vendorPrefix) {
			_fileSystem.CreateDirectoryIfNotExists(ProjectsPath);
			var projectPath = Path.Combine(ProjectsPath, projectName);
			_templateProvider.CopyTemplateFolder("ui-project", projectPath);
			UpdateTemplateInfo(projectPath, angularFileName, projectName, packageName, vendorPrefix);
			UpdateTemplateInfo(projectPath, packageFileName, projectName, packageName, vendorPrefix);
			UpdateTemplateInfo(projectPath, webpackConfigFileName, projectName, packageName, vendorPrefix);
		}

		private void CheckCorrectProjectName(string projectName) {
			var namePattern = new Regex("^([0-9a-z_]+)$");
			if (namePattern.IsMatch(projectName)) {
				return;
			}
			throw new ArgumentException("Not correct project name. Use only 'snake_case' format");
		}

		#endregion

		#region Methods: Public

		public void Create(string projectName, string packageName, string vendorPrefix) {
			CheckCorrectProjectName(projectName);
			CreatePackage(packageName);
			CreateProject(projectName, packageName, vendorPrefix);
		}

		#endregion

	}

	#endregion

}