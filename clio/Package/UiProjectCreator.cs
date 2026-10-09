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
		/// <param name="creatioVersion">
		/// Optional Creatio version used to select the template. An omitted version, or one at or above the
		/// lowest version the current templates target, selects the current template; an older version
		/// selects the closest legacy template under <c>tpl/ui/&lt;version&gt;</c>. The selected template and
		/// the <c>@creatio-devkit/common</c> range it declares are reported through the logger.
		/// </param>
		/// <param name="enableDownloadPackage">
		/// Callback that decides whether an environment package should be downloaded when no local package exists.
		/// </param>
		/// <exception cref="ArgumentException">
		/// <paramref name="projectName"/> is not snake_case, or <paramref name="creatioVersion"/> is not a valid
		/// version or is older than every shipped template. Both are detected before anything is written.
		/// </exception>
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
		private const string FullTemplateFolderName = "ui-project";
		private const string EmptyTemplateFolderName = "ui-project-Empty";

		/// <summary>Template group that holds the legacy per-version snapshots (<c>tpl/ui/&lt;version&gt;</c>).</summary>
		private const string LegacyTemplateGroup = "ui";
		private const string PackageJsonFileName = "package.json";
		private const string DevkitPackageName = "@creatio-devkit/common";
		private const string InvalidCreatioVersionMessage =
			"Creatio version '{0}' is not a valid version. Use major.minor[.build[.revision]], for example 10.0.0.";
		private const string UnsupportedCreatioVersionMessage =
			"Creatio version '{0}' is not supported: the oldest UI project template targets Creatio {1}.";
		private const string CurrentTemplateReportMessage =
			"UI project template: {0} (current template, targets Creatio {1} and later); requested Creatio version: {2}; {3}: {4}.";
		private const string LegacyTemplateReportMessage =
			"UI project template: {0}/{1}/{2} (legacy template for Creatio {1}); requested Creatio version: {3}; {4}: {5}.";
		private const string NotSpecifiedValue = "not specified";
		private const string UnknownValue = "unknown";

		#endregion

		#region Fields: Private

		private static string[] _templateExtensions = new[] {
			".json", ".js", ".ts", ".conf", ".config", ".scss", ".css"
		};
		private static readonly JsonSerializerOptions _descriptorJsonOptions = new() {
			PropertyNameCaseInsensitive = true
		};
		private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
		private static readonly Regex ProjectNamePattern = new("^([0-9a-z_]+)$", RegexOptions.Compiled, RegexTimeout);

		/// <summary>
		/// Lowest Creatio version the current templates (<c>tpl/ui-project</c>, <c>tpl/ui-project-Empty</c>)
		/// target. They implement the lazy remote-entry contract (<c>RemoteEntryDefinition</c>) introduced in
		/// <c>@creatio-devkit/common</c> 0.834.0, which ships with Creatio 8.3.4. The snapshots under
		/// <c>tpl/ui/&lt;version&gt;</c> serve only Creatio versions below this one, so a newer requested
		/// version (10.x included) must never fall back to them.
		/// </summary>
		private static readonly Version CurrentTemplateMinimumCreatioVersion = new(8, 3, 4);

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
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public UiProjectCreator(EnvironmentSettings environmentSettings, IWorkspace workspace,
			IApplicationPackageListProvider applicationPackageListProvider, IPackageCreator packageCreator,
			IPackageDownloader packageDownloader, IWorkspacePathBuilder workspacePathBuilder,
			ITemplateProvider templateProvider, IWorkingDirectoriesProvider workingDirectoriesProvider,
			IFileSystem fileSystem, ISolutionCreator solutionCreator, ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			workspace.CheckArgumentNull(nameof(workspace));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			packageCreator.CheckArgumentNull(nameof(packageCreator));
			packageDownloader.CheckArgumentNull(nameof(packageDownloader));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			solutionCreator.CheckArgumentNull(nameof(solutionCreator));
			logger.CheckArgumentNull(nameof(logger));
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
			_logger = logger;
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

		/// <summary>
		/// Resolves the legacy template snapshot for <paramref name="creatioVersion"/>, or <see langword="null"/>
		/// when the current template applies (no version, or a version at or above
		/// <see cref="CurrentTemplateMinimumCreatioVersion"/>).
		/// </summary>
		private Version ResolveLegacyTemplateVersion(string creatioVersion) {
			if (string.IsNullOrWhiteSpace(creatioVersion)) {
				return null;
			}
			if (!Version.TryParse(creatioVersion.Trim(), out Version requestedVersion)) {
				// No paramName: the message is printed as-is by the CLI, where the option is --version.
				throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
					InvalidCreatioVersionMessage, creatioVersion));
			}
			if (requestedVersion >= CurrentTemplateMinimumCreatioVersion) {
				return null;
			}
			List<Version> legacyVersions = _templateProvider.GetTemplateDirectories(LegacyTemplateGroup)
				.Select(Path.GetFileName)
				.Select(name => Version.TryParse(name, out Version version) ? version : null)
				.Where(version => version is not null)
				.ToList();
			Version compatibleVersion = legacyVersions.Where(version => version <= requestedVersion).Max();
			if (compatibleVersion is null) {
				Version oldestVersion = legacyVersions.Count > 0 ? legacyVersions.Min() : CurrentTemplateMinimumCreatioVersion;
				throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
					UnsupportedCreatioVersionMessage, creatioVersion, oldestVersion));
			}
			return compatibleVersion;
		}

		/// <summary>
		/// Reports which template produced the project and the <c>@creatio-devkit/common</c> range it declares,
		/// so a caller that asked for a specific Creatio version sees the mapping instead of only "Done".
		/// </summary>
		private void ReportSelectedTemplate(string projectName, string templateFolderName, string creatioVersion,
			Version legacyTemplateVersion) {
			string requestedVersion = string.IsNullOrWhiteSpace(creatioVersion) ? NotSpecifiedValue : creatioVersion.Trim();
			string devkitRange = ReadDeclaredDevkitRange(Path.Combine(ProjectsPath, projectName, PackageJsonFileName));
			string message = legacyTemplateVersion is null
				? string.Format(CultureInfo.InvariantCulture, CurrentTemplateReportMessage, templateFolderName,
					CurrentTemplateMinimumCreatioVersion, requestedVersion, DevkitPackageName, devkitRange)
				: string.Format(CultureInfo.InvariantCulture, LegacyTemplateReportMessage, LegacyTemplateGroup,
					legacyTemplateVersion, templateFolderName, requestedVersion, DevkitPackageName, devkitRange);
			_logger.WriteInfo(message);
		}

		/// <summary>
		/// Best-effort read of the devkit range for the report only: the project is already in place, so a
		/// failure here must never fail the command.
		/// </summary>
		private string ReadDeclaredDevkitRange(string packageJsonPath) {
			try {
				if (!_fileSystem.ExistsFile(packageJsonPath)) {
					return UnknownValue;
				}
				JsonNode devkitNode = JsonNode.Parse(_fileSystem.ReadAllText(packageJsonPath))?["dependencies"]?[DevkitPackageName];
				return devkitNode is JsonValue devkitValue && devkitValue.TryGetValue(out string range)
					&& !string.IsNullOrWhiteSpace(range)
						? range
						: UnknownValue;
			} catch (JsonException) {
				return UnknownValue;
			} catch (InvalidOperationException) {
				// The JSON root or "dependencies" is not an object.
				return UnknownValue;
			} catch (IOException) {
				return UnknownValue;
			} catch (UnauthorizedAccessException) {
				return UnknownValue;
			}
		}

		private string CreateProject(string projectName, string packageName, string vendorPrefix, bool isEmpty,
			Version legacyTemplateVersion) {
			_fileSystem.CreateDirectoryIfNotExists(ProjectsPath);
			string projectPath = Path.Combine(ProjectsPath, projectName);
			string stagingPath = Path.Combine(ProjectsPath, $".{projectName}.{Guid.NewGuid():N}.tmp");
			string templateFolderName = isEmpty ? EmptyTemplateFolderName : FullTemplateFolderName;
			try {
				if (legacyTemplateVersion is null) {
					_templateProvider.CopyTemplateFolder(templateFolderName, stagingPath);
				} else {
					_templateProvider.CopyTemplateFolder(templateFolderName, stagingPath,
						legacyTemplateVersion.ToString(), LegacyTemplateGroup);
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
			return templateFolderName;
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
			if (ProjectNamePattern.IsMatch(projectName)) {
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
			// Resolve the template before any package is created or downloaded, so an invalid or
			// unsupported version fails without leaving a half-scaffolded workspace behind.
			Version legacyTemplateVersion = ResolveLegacyTemplateVersion(creatioVersion);
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
			string templateFolderName = CreateProject(projectName, packageName, vendorPrefix, isEmpty, legacyTemplateVersion);
			IntegrateEsprojIntoSolution(projectName, packageName);
			ReportSelectedTemplate(projectName, templateFolderName, creatioVersion, legacyTemplateVersion);
		}

		#endregion

	}

	#endregion
}
