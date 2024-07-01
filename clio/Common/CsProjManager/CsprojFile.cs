using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Xml;
using Clio.Workspaces;

namespace Clio.Common.CsProjManager;

/// <summary>
///     Interface for managing operations related to a .csproj file.
/// </summary>
public interface ICsprojFile
{

	#region Methods: Public

	/// <summary>
	///     Initializes the .csproj file using a package name.
	/// </summary>
	/// <param name="packageName">The name of the package.</param>
	/// <returns>An initialized .csproj file.</returns>
	IInitializedCsprojFile Initialize(string packageName);

	/// <summary>
	///     Initializes the .csproj file using a file info object.
	/// </summary>
	/// <param name="fileInfo">The file info object.</param>
	/// <returns>An initialized .csproj file.</returns>
	IInitializedCsprojFile Initialize(IFileInfo fileInfo);

	#endregion

}

public interface IInitializedCsprojFile
{

	#region Properties: Public

	public string CsProjFileContent { get; }

	#endregion

	#region Methods: Public

	/// <summary>
	///     Retrieves the package references from the .csproj file.
	/// </summary>
	/// <returns>
	///     An IEnumerable of Reference objects, each representing a package reference in the .csproj file.
	///     Each Reference object contains the package name, version, hint path, specific version flag, and private flag.
	/// </returns>
	/// <remarks>
	///     This method loads the content of the .csproj file into an XmlDocument.
	///     It then finds the first 'ItemGroup' element with a 'Label' attribute of 'Package References'.
	///     This is where the package references are defined in the .csproj file.
	///     If the 'ItemGroup' element is found, it selects all 'Reference' child elements that have an 'Include' attribute.
	///     Each 'Reference' element represents a package reference.
	///     For each 'Reference' element, it creates a new Reference object with the package name,
	///     version, hint path, specific version flag, and private flag.
	/// </remarks>
	public IEnumerable<Reference> GetPackageReferences();

	#endregion

}

public class CsprojFile : ICsprojFile, IInitializedCsprojFile
{
	
	#region Fields: Private

	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public CsprojFile(IWorkspacePathBuilder workspacePathBuilder, IFileSystem fileSystem){
		_workspacePathBuilder = workspacePathBuilder;
		_fileSystem = fileSystem;
	}

	#endregion

	#region Properties: Public

	public string CsProjFileContent { get; private set; }

	#endregion

	#region Methods: Public

	public IEnumerable<Reference> GetPackageReferences(){
		if (string.IsNullOrEmpty(CsProjFileContent)) {
			return Array.Empty<Reference>();
		}

		XmlDocument doc = new();
		doc.LoadXml(CsProjFileContent);
		XmlNode rootNode = doc.DocumentElement;

		if (rootNode is null || !rootNode.ChildNodes.OfType<XmlElement>().Any()) {
			return Array.Empty<Reference>();
		}
		return rootNode.ChildNodes.OfType<XmlElement>()
			.FirstOrDefault(x =>
				x.Name == "ItemGroup" && x.Attributes.Count > 0 && x.Attributes[0].Name == "Label" &&
				x.Attributes[0].Value == "Package References")!
			.ChildNodes.OfType<XmlElement>().Where(x => x.Name == "Reference")
			.Where(i => i.Attributes.OfType<XmlAttribute>()
				.Any(item => item.Name == "Include" && item.Value != "Terrasoft.Configuration"))
			.Select(i => {
				string packageName = i.Attributes["Include"]!.Value;
				string version = i.ChildNodes.OfType<XmlElement>().FirstOrDefault(x => x.Name == "Version")?.InnerText;
				string hintPath = i.ChildNodes.OfType<XmlElement>().FirstOrDefault(x => x.Name == "HintPath")
					?.InnerText;
				string specificVersion = i.ChildNodes.OfType<XmlElement>()
					.FirstOrDefault(x => x.Name == "SpecificVersion")?.InnerText;
				string @private = i.ChildNodes.OfType<XmlElement>().FirstOrDefault(x => x.Name == "Private")?.InnerText;
				Version.TryParse(version, out Version ver);
				Reference r = new Reference(packageName, ver, hintPath ?? string.Empty,
					bool.Parse(specificVersion ?? string.Empty), bool.Parse(@private ?? string.Empty));
				return r;
			});
	}

	public IInitializedCsprojFile Initialize(string packageName){
		string csprojFilePath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
		bool existsFile = _fileSystem.ExistsFile(csprojFilePath);
		if (existsFile) {
			CsProjFileContent = _fileSystem.ReadAllText(csprojFilePath);
		}
		return this;
	}

	public IInitializedCsprojFile Initialize(IFileInfo fileInfo){
		if (fileInfo.Exists) {
			CsProjFileContent = _fileSystem.ReadAllText(fileInfo.FullName);
		}
		return this;
	}

	#endregion

}

public record Reference(string PackageName, Version? Version, string HintPath, bool SpecificVersion, bool Private);