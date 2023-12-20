using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Clio.Project.NuGet;
using Clio.Workspace;

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

	private void UpdateCsProjFile(string packageName, IEnumerable<XElement> xElements){
		bool needsBackUp = false;
		
		//comment out PackageReference from the main csproj file
		foreach (XElement element in xElements) {
			needsBackUp = true;
			XComment comment = new(element.ToString());
			element.ReplaceWith(comment);
		}
		
		//Look in csproj for the following line
		//<Import Condition="'$(TargetFramework)' == 'net472'" Project="MrktApolloApp-net472.nuget.props" />
		IEnumerable<XElement> importElementsNet472 = _csproj.Descendants("Import")
			.Where(e=> 
				e.Attribute("Project")?.Value == $"{packageName}-net472.nuget.props"
				&& e.Attribute("Condition")?.Value == "'$(TargetFramework)' == 'net472'");
		
		if(!importElementsNet472.Any()){
			XElement importElementNet472 = new("Import");
			importElementNet472.SetAttributeValue("Condition", "'$(TargetFramework)' == 'net472'");
			importElementNet472.SetAttributeValue("Project", $"{packageName}-net472.nuget.props");
			
			//This will not be null, since csproj MUST have Project element
			_csproj.Element("Project")!.Add(importElementNet472); 
			needsBackUp = true;
		}else {
			_logger.WriteWarning($"Could not add {packageName}-net472.nuget.props import to the {_csprojPath} file. Import already exists");
		}
		
		//Look in csproj for the following line
		//<Import Condition="'$(TargetFramework)' == 'netstandard2.0'" Project="MrktApolloApp-netstandard.nuget.props" />
		IEnumerable<XElement> importElementsNetStandard = _csproj.Descendants("Import")
			.Where(e=> 
				e.Attribute("Project")?.Value == $"{packageName}-netstandard.nuget.props"
				&& e.Attribute("Condition")?.Value == "'$(TargetFramework)' == 'netstandard2.0'");

		if(!importElementsNetStandard.Any()) {
			XElement importElementNetStandard = new("Import");
			importElementNetStandard.SetAttributeValue("Condition", "'$(TargetFramework)' == 'netstandard2.0'");
			importElementNetStandard.SetAttributeValue("Project", $"{packageName}-netstandard.nuget.props");
			
			//This will not be null, since csproj MUST have Project element
			_csproj.Element("Project")!.Add(importElementNetStandard);
			needsBackUp = true;
		}else {
			_logger.WriteWarning($"Could not add {packageName}-netstandard.nuget.props import to the {_csprojPath} file. Import already exists");
		}

		if (!needsBackUp) {
			return;
		}
		
		_logger.WriteInfo($"Creating csproj backup file {_csprojPath}.bak");
		_fileSystem.CopyFile(_csprojPath, $"{_csprojPath}.bak", true);
		_csproj.Save(_csprojPath);
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
			return 1;
		}

		CreateNugetProjectIfNotExists(packageName);
		AddNugetReferences(packageName, xElements);
		BuildNugetProject(packageName);
		_propsBuilder.Build(packageName);
		UpdateCsProjFile(packageName, xElements);
		return 0;
	}

	#endregion

}