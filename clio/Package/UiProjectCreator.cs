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

		/// <summary>
		/// Creates a Freedom UI project and creates or reuses its local Creatio host package.
		/// </summary>
		/// <param name="projectName">Snake-case Angular project name.</param>
		/// <param name="packageName">Creatio package that receives the compiled bundle.</param>
		/// <param name="vendorPrefix">Lowercase Creatio vendor prefix.</param>
		/// <param name="isEmpty">Whether to use the empty UI project template.</param>
		/// <param name="creatioVersion">Optional Creatio version used to select a compatible template.</param>
		/// <param name="enableDownloadPackage">
		/// Callback that decides whether an environment package should be downloaded when no local package exists.
		/// </param>
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
		private const string ExistingProjectMessage =
			"UI project path '{0}' already exists. Choose a different project name or remove the existing project explicitly.";
		private const string InvalidExistingPackageMessage =
			"Directory '{0}' exists but is not a valid Creatio package: {1}.";
		private const string MissingDescriptorReason = "'{0}' is missing";
		private const string MalformedDescriptorReason = "'{0}' is malformed";
		private const string MissingPackageDescriptorReason = "'{0}' does not contain a package descriptor";
		private const string DescriptorNameMismatchReason = "descriptor name '{0}' does not match '{1}'";
		private const string EmptyDescriptorUIdReason = "descriptor UId is empty";
		private const string PackagePathIsFileReason = "the package path is a file";
		private const string PackageDirectoryCaseMismatchReason =
			"package directory name '{0}' does not match the requested casing '{1}'";
		private const string LinkedDescriptorReason = "linked package descriptors are not supported";
		private const string OversizedDescriptorReason = "package descriptor exceeds the {0}-byte size limit";
		private const string StagingCleanupFailureDataKey = "UiProjectStagingCleanupFailure";
		private const long MaxPackageDescriptorBytes = 1024 * 1024;

		#endregion

		#region Fields: Private

		private static string[] _templateExtensions = new[] {
			".json", ".js", ".ts", ".conf", ".config", ".scss", ".css"
		};
		private static readonly JsonSerializerOptions _descriptorJsonOptions = new() {
			PropertyNameCaseInsensitive = true
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

		private void CheckProjectDoesNotExist(string projectName) {
			string projectPath = Path.Combine(ProjectsPath, projectName);
			if (_fileSystem.ExistsDirectory(projectPath) || _fileSystem.ExistsFile(projectPath)) {
				throw new InvalidOperationException(string.Format(
					CultureInfo.InvariantCulture, ExistingProjectMessage, projectPath));
			}
		}

		private void CreatePackage(string packageName) {
			_packageCreator.Create(PackagesPath, packageName);
		}

		private bool ReuseLocalPackageIfValid(string packageName) {
			string packagePath = Path.Combine(PackagesPath, packageName);
			if (_fileSystem.ExistsFile(packagePath)) {
				throw InvalidExistingPackage(packagePath, PackagePathIsFileReason);
			}
			if (_fileSystem.ExistsDirectory(PackagesPath)) {
				string[] packageDirectories = _fileSystem.GetDirectories(PackagesPath);
				bool hasExactMatch = packageDirectories.Any(path =>
					string.Equals(Path.GetFileName(path), packageName, StringComparison.Ordinal));
				if (!hasExactMatch) {
					string casingMismatchPath = packageDirectories.FirstOrDefault(path =>
						string.Equals(Path.GetFileName(path), packageName, StringComparison.OrdinalIgnoreCase));
					if (casingMismatchPath is not null) {
						throw InvalidExistingPackage(casingMismatchPath, string.Format(CultureInfo.InvariantCulture,
							PackageDirectoryCaseMismatchReason, Path.GetFileName(casingMismatchPath), packageName));
					}
				}
			}
			if (!_fileSystem.ExistsDirectory(packagePath)) {
				return false;
			}
			string descriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
			if (!_fileSystem.ExistsFile(descriptorPath)) {
				throw InvalidExistingPackage(packagePath, string.Format(
					CultureInfo.InvariantCulture, MissingDescriptorReason, CreatioPackage.DescriptorName));
			}
			if ((_fileSystem.GetFilesInfos(descriptorPath).Attributes & FileAttributes.ReparsePoint) != 0) {
				throw InvalidExistingPackage(packagePath, LinkedDescriptorReason);
			}
			if (_fileSystem.GetFileSize(descriptorPath) > MaxPackageDescriptorBytes) {
				throw InvalidExistingPackage(packagePath, string.Format(CultureInfo.InvariantCulture,
					OversizedDescriptorReason, MaxPackageDescriptorBytes));
			}

			PackageDescriptorDto descriptor;
			try {
				descriptor = ReadPackageDescriptor(descriptorPath, packagePath);
			} catch (JsonException exception) {
				throw InvalidExistingPackage(packagePath, string.Format(
					CultureInfo.InvariantCulture, MalformedDescriptorReason, CreatioPackage.DescriptorName), exception);
			}

			if (descriptor?.Descriptor is null) {
				throw InvalidExistingPackage(packagePath, string.Format(
					CultureInfo.InvariantCulture, MissingPackageDescriptorReason, CreatioPackage.DescriptorName));
			}
			if (!string.Equals(descriptor.Descriptor.Name, packageName, StringComparison.Ordinal)) {
				throw InvalidExistingPackage(packagePath, string.Format(
					CultureInfo.InvariantCulture, DescriptorNameMismatchReason, descriptor.Descriptor.Name, packageName));
			}
			if (descriptor.Descriptor.UId == Guid.Empty) {
				throw InvalidExistingPackage(packagePath, EmptyDescriptorUIdReason);
			}

			return true;
		}

		private PackageDescriptorDto ReadPackageDescriptor(string descriptorPath, string packagePath) {
			using Stream descriptorStream = _fileSystem.OpenReadStream(descriptorPath);
			using MemoryStream descriptorBytes = new();
			byte[] buffer = new byte[81920];
			int bytesRead;
			while ((bytesRead = descriptorStream.Read(buffer, 0, buffer.Length)) > 0) {
				if (descriptorBytes.Length + bytesRead > MaxPackageDescriptorBytes) {
					throw InvalidExistingPackage(packagePath, string.Format(CultureInfo.InvariantCulture,
						OversizedDescriptorReason, MaxPackageDescriptorBytes));
				}
				descriptorBytes.Write(buffer, 0, bytesRead);
			}
			byte[] content = descriptorBytes.ToArray();
			ReadOnlySpan<byte> json = content;
			if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF) {
				json = json[3..];
			}
			return JsonSerializer.Deserialize<PackageDescriptorDto>(json, _descriptorJsonOptions);
		}

		private static InvalidOperationException InvalidExistingPackage(string packagePath, string reason,
			Exception innerException = null) =>
			new(string.Format(CultureInfo.InvariantCulture, InvalidExistingPackageMessage, packagePath, reason),
				innerException);

		private void CreateProject(string projectName, string packageName, string vendorPrefix, bool isEmpty,
			string creatioVersion) {
			_fileSystem.CreateDirectoryIfNotExists(ProjectsPath);
			string projectPath = Path.Combine(ProjectsPath, projectName);
			string stagingPath = Path.Combine(ProjectsPath, $".{projectName}.{Guid.NewGuid():N}.tmp");
			string templateFolderName = isEmpty ? "ui-project-Empty" : "ui-project";
			try {
				if(string.IsNullOrWhiteSpace(creatioVersion)) {
					_templateProvider.CopyTemplateFolder(templateFolderName, stagingPath);
				}else {
					_templateProvider.CopyTemplateFolder(templateFolderName, stagingPath, creatioVersion, "ui");
				}
				UpdateTemplateInfo(stagingPath, projectName, packageName, vendorPrefix);
				_fileSystem.GetDirectoryInfo(stagingPath).MoveTo(projectPath);
			} catch (Exception exception) {
				try {
					if (_fileSystem.ExistsDirectory(stagingPath)) {
						_fileSystem.DeleteDirectory(stagingPath, true);
					}
				} catch (Exception cleanupException) {
					exception.Data[StagingCleanupFailureDataKey] = cleanupException.Message;
				}
				throw;
			}
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
			// Keep forward slashes in BuildOutputFolder: they are POSIX-native and MSBuild normalizes
			// them on Windows. A hard-coded backslash would be treated as a literal character on
			// macOS/Linux and break the bundle path.
			string content = _templateProvider.GetTemplate(esprojTemplateName)
				.Replace("<%projectName%>", projectName)
				.Replace("<%distPath%>", BuildDistPath(packageName, projectName));
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
			CheckProjectDoesNotExist(projectName);
			if (ReuseLocalPackageIfValid(packageName)) {
				_workspace.AddPackageIfNeeded(packageName);
			} else {
				var package = FindExistingPackage(packageName);
				if (package != null && enableDownloadPackage(packageName)) {
					_packageDownloader.DownloadPackage(packageName, _environmentSettings,
						_workspacePathBuilder.PackagesFolderPath);
					_workspace.AddPackageIfNeeded(packageName);
				} else {
					CreatePackage(packageName);
				}
			}
			CreateProject(projectName, packageName, vendorPrefix, isEmpty, creatioVersion);
			IntegrateEsprojIntoSolution(projectName, packageName);
		}

		#endregion

	}

	#endregion
}
