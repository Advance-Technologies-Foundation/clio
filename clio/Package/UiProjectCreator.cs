using System.Globalization;
using System.Text;

namespace Clio.Package
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text.Json;
	using System.Text.Json.Nodes;
	using System.Text.RegularExpressions;
	using Clio.Common;
	using Clio.Workspace;
	using Clio.Workspaces;

	#region Interface: IUiProjectCreator

	public interface IUiProjectCreator
	{

		#region Methods: Public

		void Create(string projectName, string packageName, string vendorPrefix, bool isEmpty, string creatioVersion,
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

		/// <summary>Name of the MSBuild project SDK that wraps the npm/Angular build.</summary>
		private const string JavaScriptSdkName = "Microsoft.VisualStudio.JavaScript.Sdk";

		/// <summary>
		/// Pinned JavaScript SDK version written to the repo-root <c>global.json</c>. MSBuild project
		/// SDKs do not support floating/latest versions, so this must be an exact version.
		/// </summary>
		private const string JavaScriptSdkVersion = "1.0.5581896";

		private const string globalJsonFileName = "global.json";
		private const string esprojTemplateName = "esproj";

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
		private readonly ISolutionCreator _solutionCreator;

		#endregion

		#region Constructors: Public

		public UiProjectCreator(EnvironmentSettings environmentSettings, IWorkspace workspace,
			IApplicationPackageListProvider applicationPackageListProvider, IPackageCreator packageCreator,
			IPackageDownloader packageDownloader, IWorkspacePathBuilder workspacePathBuilder,
			ITemplateProvider templateProvider, IWorkingDirectoriesProvider workingDirectoriesProvider,
			IFileSystem fileSystem, ISolutionCreator solutionCreator) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspace.CheckArgumentNull(nameof(workspace));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			packageCreator.CheckArgumentNull(nameof(packageCreator));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			_environmentSettings = environmentSettings;
			_workspace = workspace;
			_applicationPackageListProvider = applicationPackageListProvider;
			_packageCreator = packageCreator;
			_packageDownloader = packageDownloader;
			_workspacePathBuilder = workspacePathBuilder;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_solutionCreator = solutionCreator;
		}

		#endregion

		#region Properties: Private

		private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

		private string PackagesPath =>
			IsWorkspace
				? _workspacePathBuilder.PackagesFolderPath
				: Path.Combine(_workingDirectoriesProvider.CurrentDirectory, packagesDirectoryName);

		private string ProjectsPath =>
			IsWorkspace
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
				string tplContent = _fileSystem.ReadAllText(filePath );
				tplContent = tplContent.Replace("<%vendorPrefix%>", vendorPrefix, true, CultureInfo.InvariantCulture);
				tplContent = tplContent.Replace("<%projectName%>", projectName,true, CultureInfo.InvariantCulture);
				tplContent = tplContent.Replace("<%distPath%>",
					BuildDistPath(packageName, projectName), true, CultureInfo.InvariantCulture);
				_fileSystem.WriteAllTextToFile(filePath, tplContent);
			}
		}

		/// <summary>
		/// Bundle output folder (the <c>angular.json</c> <c>outputPath</c>), relative to the Angular
		/// project directory and using forward slashes — e.g.
		/// <c>../../packages/UsrRssReader/Files/src/js/rss_reader</c>.
		/// </summary>
		private static string BuildDistPath(string packageName, string projectName) =>
			Path.Combine("../../", "packages/", packageName + "/", "Files/", "src/", "js/", projectName);
		private void CreatePackage(string packageName) {
			_packageCreator.Create(PackagesPath, packageName);
		}

		private void CreateProject(string projectName, string packageName, string vendorPrefix, bool isEmpty,
			string creatioVersion) {
			_fileSystem.CreateDirectoryIfNotExists(ProjectsPath);
			var projectPath = Path.Combine(ProjectsPath, projectName);
			string templateFolderName = isEmpty ? "ui-project-Empty" : "ui-project";
			if(string.IsNullOrWhiteSpace(creatioVersion)) {
				_templateProvider.CopyTemplateFolder(templateFolderName, projectPath);
			}else {
				_templateProvider.CopyTemplateFolder(templateFolderName, projectPath, creatioVersion, "ui");
			}
			UpdateTemplateInfo(projectPath, projectName, packageName, vendorPrefix);
		}

		/// <summary>
		/// Wires the generated Angular project into the .NET solution so that
		/// <c>dotnet build MainSolution.slnx</c> also runs the npm build. Performs three coordinated
		/// edits: writes an <c>.esproj</c> wrapper next to <c>package.json</c>, pins the JavaScript SDK
		/// version in the repo-root <c>global.json</c>, and adds the <c>.esproj</c> to
		/// <c>MainSolution.slnx</c> with a forced <c>&lt;Build /&gt;</c> element.
		/// No-op outside a workspace, where there is no main solution to integrate with.
		/// </summary>
		private void IntegrateEsprojIntoSolution(string projectName, string packageName) {
			if (!IsWorkspace) {
				return;
			}
			CreateEsprojFile(projectName, packageName);
			EnsureJavaScriptSdkPinnedInGlobalJson();
			AddEsprojToMainSolution(projectName);
		}

		private void CreateEsprojFile(string projectName, string packageName) {
			string esprojPath = Path.Combine(ProjectsPath, projectName, $"{projectName}.esproj");
			// BuildOutputFolder is combined with $(MSBuildProjectDirectory); use OS separators so the
			// path reads naturally on the host (MSBuild normalizes either way).
			string buildOutputFolder = BuildDistPath(packageName, projectName)
				.Replace('/', Path.DirectorySeparatorChar);
			string content = _templateProvider.GetTemplate(esprojTemplateName)
				.Replace("<%projectName%>", projectName)
				.Replace("<%distPath%>", buildOutputFolder);
			_fileSystem.WriteAllTextToFile(esprojPath, content);
		}

		/// <summary>
		/// Ensures the repo-root <c>global.json</c> pins the JavaScript SDK version. Merges into an
		/// existing file (preserving the <c>sdk</c> node and any other content) rather than overwriting.
		/// </summary>
		private void EnsureJavaScriptSdkPinnedInGlobalJson() {
			string globalJsonPath = Path.Combine(_workspacePathBuilder.RootPath, globalJsonFileName);
			JsonObject root = _fileSystem.ExistsFile(globalJsonPath)
				? JsonNode.Parse(_fileSystem.ReadAllText(globalJsonPath)) as JsonObject ?? new JsonObject()
				: new JsonObject();
			if (root["msbuild-sdks"] is not JsonObject msbuildSdks) {
				msbuildSdks = new JsonObject();
				root["msbuild-sdks"] = msbuildSdks;
			}
			msbuildSdks[JavaScriptSdkName] = JavaScriptSdkVersion;
			string serialized = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
			_fileSystem.WriteAllTextToFile(globalJsonPath, serialized);
		}

		private void AddEsprojToMainSolution(string projectName) {
			string esprojPath = Path.Combine(ProjectsPath, projectName, $"{projectName}.esproj");
			string relativeEsprojPath =
				Path.GetRelativePath(_workspacePathBuilder.MainSolutionFolderPath, esprojPath);
			SolutionProject esprojSolutionProject = new(projectName, relativeEsprojPath) { ForceBuild = true };
			_solutionCreator.AddProjectToSolution(_workspacePathBuilder.MainSolutionPath, [esprojSolutionProject]);
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
			} catch (Exception) {
				return null;
			}
		}

		#endregion

		#region Methods: Public

		public void Create(string projectName, string packageName, string vendorPrefix, bool isEmpty,
			string creatioVersion, Func<string, bool> enableDownloadPackage) {
			CheckCorrectProjectName(projectName);
			var package = FindExistingPackage(packageName);
			if (package != null && enableDownloadPackage(packageName)) {
				_packageDownloader.DownloadPackage(packageName, _environmentSettings,
					_workspacePathBuilder.PackagesFolderPath);
				_workspace.AddPackageIfNeeded(packageName);
			} else {
				CreatePackage(packageName);
			}
			CreateProject(projectName, packageName, vendorPrefix, isEmpty, creatioVersion);
			IntegrateEsprojIntoSolution(projectName, packageName);
		}

		#endregion

	}

	#endregion
}