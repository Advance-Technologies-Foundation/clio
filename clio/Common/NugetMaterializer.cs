using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
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

	#endregion

	#region Fields: Private

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
	private void AddNugetReferences(string packageName, IEnumerable<XElement> xElements){
		IEnumerable<NugetPackage> refs = GetNugetReferences(xElements);
		foreach (NugetPackage nugetPackage in refs) {
			_processExecutor.Execute(
				"dotnet",
				$"add package {nugetPackage.Name} -v {nugetPackage.Version}",
				true,
				Path.Combine(_workspacePathBuilder.RootPath, ".nuget", packageName)
			);
		}
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
	private void BuildNugetProject(string packageName){
		_processExecutor.Execute(
			"dotnet",
			$"build {packageName}.csproj -c Release --no-incremental",
			true,
			Path.Combine(_workspacePathBuilder.RootPath, ".nuget", packageName)
		);
		//TODO: Should we Delete obj folder or leave it alone?
	}

	private void CreateNugetProjectIfNotExists(string packageName){
		string nugetProjectFolderPath = Path.Combine(_workspacePathBuilder.RootPath, ".nuget", packageName);
		_fileSystem.CreateDirectoryIfNotExists(nugetProjectFolderPath);

		string nugetCsprojPath = Path.Combine(nugetProjectFolderPath, $"{packageName}.csproj");
		bool projExists = _fileSystem.ExistsFile(nugetCsprojPath);

		if (projExists) {
			return;
		}

		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string templatePath = Path.Combine(baseDir, "tpl", "NugetProject.csproj.tpl");
		string templateContent = _fileSystem.ReadAllText(templatePath);
		_fileSystem.WriteAllTextToFile(nugetCsprojPath, templateContent);
	}

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
		
		SaveCsProjFile();
	}

	/// <summary>
	/// Backs up the csproj and writes the current document over it.
	/// Goes through IFileSystem so the written content is part of the testable contract.
	/// </summary>
	private void SaveCsProjFile(){
		string backupPath = $"{_csprojPath}.bak";
		//Only the FIRST save may write the backup. A later save - the stale-import repair, above all -
		//would otherwise copy the already-converted csproj over it and destroy the only pre-conversion
		//copy, which is what the caller is told to recover from.
		if (_fileSystem.ExistsFile(backupPath)) {
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
		
		SaveCsProjFile();
	}

	/// <summary>
	/// Removes every Import element of the given props file from the csproj.
	/// </summary>
	/// <returns>True when the csproj was modified.</returns>
	private bool RemovePropsImport(string propsFileName){
		List<XElement> staleImports = _csproj.Descendants("Import")
			.Where(e => e.Attribute("Project")?.Value == propsFileName)
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
			.Any(e => e.Attribute("Project")?.Value == propsFileName
				&& e.Attribute("Condition")?.Value == condition);
		
		if (importExists) {
			_logger.WriteInfo($"{propsFileName} import already exists in the {_csprojPath} file, skipping");
			return false;
		}
		
		XElement importElement = new("Import");
		importElement.SetAttributeValue("Condition", condition);
		importElement.SetAttributeValue("Project", propsFileName);
		
		//This will not be null, since csproj MUST have Project element
		_csproj.Element("Project")!.Add(importElement);
		return true;
	}

	#endregion

	#region Methods: Public

	public int Materialize(string packageName){
		_csprojPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
		string xmlContent = GetXmlContent(_csprojPath);
		IEnumerable<XElement> elements = FindNugetReferences(xmlContent);
		IEnumerable<XElement> xElements = elements as XElement[] ?? elements.ToArray();
		if (!xElements.Any()) {
			_logger.WriteWarning($"Could not find any {Tag} references in the {_csprojPath} file");
			RepairUnusablePropsImports(packageName);
			return 1;
		}

		CreateNugetProjectIfNotExists(packageName);
		AddNugetReferences(packageName, xElements);
		BuildNugetProject(packageName);
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