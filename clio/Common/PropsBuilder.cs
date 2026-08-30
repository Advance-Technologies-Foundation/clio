using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Clio.Workspaces;

namespace Clio.Common;

/// <summary>
/// Describes which props files the <see cref="IPropsBuilder"/> produced.
/// </summary>
/// <param name="Net472PropsCreated">True when the net472 props file was written.</param>
/// <param name="NetStandardPropsCreated">True when the netstandard props file was written.</param>
public readonly record struct PropsBuildResult(bool Net472PropsCreated, bool NetStandardPropsCreated)
{

	/// <summary>
	/// True when at least one props file was written.
	/// </summary>
	public bool HasAnyProps => Net472PropsCreated || NetStandardPropsCreated;

}

/// <summary>
/// Build .props file for the main csproj file of a package 
/// </summary>
public interface IPropsBuilder
{

	#region Methods: Public

	/// <summary>
	/// Builds props files for the project and copies all dlls from
	/// nuget/bin folder to the package Libs folder
	/// </summary>
	/// <param name="packageName">Package name to convert</param>
	/// <returns>
	/// Which target monikers actually received a usable props file. A moniker without
	/// referenced dlls produces no props file, and its caller must not import one.
	/// </returns>
	/// <remarks>
	/// It add Libs folder with the following structure : <br/>
	/// 📦Files                                <br/>
	/// ┣ 📂Libs                               <br/>
	/// ┃ ┣ 📂net472                           <br/>
	/// ┃ ┗ 📂netstandard                      <br/>
	/// ┣ 📜PKG_NAME-net472.nuget.props        <br/>
	/// ┣ 📜PKG_NAME-netstandard.nuget.props   <br/>
	/// </remarks>
	PropsBuildResult Build(string packageName);

	#endregion

}

public class PropsBuilder : IPropsBuilder
{

	#region Enum: Private

	private enum ItemType
	{

		NugetFolder,
		PackageFolder,
		Net472BinDir,
		NetStdBinDir,
		NetStdPropsFilePath,
		Net472PropsFilePath,
		Net472PackageLibsPath,
		NetStdPackageLibsPath

	}

	private enum Moniker
	{

		net472,
		netstandard

	}

	#endregion

	#region Constants: Private

	private const string IncludeTag = "Include";
	private const string ProjExtension = ".csproj";
	private const string PropsExtension = ".props";
	private const string ReferenceTag = "Reference";

	#endregion

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;

	#endregion

	#region Constructors: Public

	public PropsBuilder(IFileSystem fileSystem, ILogger logger, IWorkspacePathBuilder workspacePathBuilder){
		_fileSystem = fileSystem;
		_logger = logger;
		_workspacePathBuilder = workspacePathBuilder;
	}

	#endregion

	#region Methods: Private

	private bool BuildNet472Props(string packageName){
		string net472BinDir = GetPathTo(ItemType.Net472BinDir, packageName);
		IEnumerable<string> net472Files = GetDependencyDlls(net472BinDir, packageName);

		string net472PropsContent = Process(net472Files, packageName, Moniker.net472);
		string net472PropsFilePath = GetPathTo(ItemType.Net472PropsFilePath, packageName);
		return SavePropsFile(net472PropsFilePath, net472PropsContent, Moniker.net472);
	}
	private bool BuildNetStdProps(string packageName){
		string netStdBinDir = GetPathTo(ItemType.NetStdBinDir, packageName);
		IEnumerable<string> netStdFiles = GetDependencyDlls(netStdBinDir, packageName);

		string netStdPropsContent = Process(netStdFiles, packageName, Moniker.netstandard);
		string netStdPropsFilePath = GetPathTo(ItemType.NetStdPropsFilePath, packageName);
		return SavePropsFile(netStdPropsFilePath, netStdPropsContent, Moniker.netstandard);
	}

	/// <summary>
	/// Returns the dlls the package depends on, excluding the package assembly itself.
	/// Compares file names, so a dependency whose name merely ends with the package
	/// name (Contoso.MyPkg.dll for package MyPkg) is kept.
	/// </summary>
	private IEnumerable<string> GetDependencyDlls(string binDir, string packageName) =>
		_fileSystem
			.GetFiles(binDir, "*.dll", SearchOption.TopDirectoryOnly)
			.Where(f => !string.Equals(Path.GetFileNameWithoutExtension(f), packageName,
				StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Writes the props file, or reports that there is nothing to write.
	/// An empty file must never be written: the csproj imports it, and MSBuild
	/// fails the whole project with "Root element is missing".
	/// </summary>
	private bool SavePropsFile(string propsFilePath, string propsContent, Moniker moniker){
		if (string.IsNullOrWhiteSpace(propsContent)) {
			_logger.WriteWarning($"No {moniker} dependencies found, skipping {propsFilePath}");
			return false;
		}
		_logger.WriteLine("Saving props file to " + propsFilePath);
		_fileSystem.WriteAllTextToFile(propsFilePath, propsContent);
		return true;
	}
	private string GetPathTo(ItemType itemType, string packageName){
		return itemType switch {
			ItemType.NugetFolder => FilePathGetter(_workspacePathBuilder.NugetFolderPath),
			ItemType.PackageFolder => FilePathGetter(_workspacePathBuilder.PackagesFolderPath),
			ItemType.Net472BinDir => FolderPathGetter(_workspacePathBuilder.NugetFolderPath, Moniker.net472),
			ItemType.NetStdBinDir => FolderPathGetter(_workspacePathBuilder.NugetFolderPath, Moniker.netstandard),
			ItemType.Net472PropsFilePath => PackageFolderPathGetter(_workspacePathBuilder.PackagesFolderPath,
				Moniker.net472),
			ItemType.NetStdPropsFilePath => PackageFolderPathGetter(_workspacePathBuilder.PackagesFolderPath,
				Moniker.netstandard),
			ItemType.Net472PackageLibsPath => PackageLibsPath(_workspacePathBuilder.PackagesFolderPath, Moniker.net472),
			ItemType.NetStdPackageLibsPath => PackageLibsPath(_workspacePathBuilder.PackagesFolderPath,
				Moniker.netstandard),
			var _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, null)
		};

		string PackageLibsPath(string path, Moniker moniker) =>
			Path.Combine(path, packageName, "Files", "Libs", moniker.ToString());

		string FilePathGetter(string path) => Path.Combine(path, packageName, packageName + ProjExtension);

		string PackageFolderPathGetter(string path, Moniker moniker) =>
			Path.Combine(path, packageName, "Files", packageName + "-" + moniker + ".nuget" + PropsExtension);

		string FolderPathGetter(string path, Moniker moniker) =>
			Path.Combine(path, packageName, "bin", moniker.ToString());
	}

	private string Process(IEnumerable<string> dlls, string packageName, Moniker moniker){
		IEnumerable<string> enumerableDlls = dlls as string[] ?? dlls.ToArray();
		if (!enumerableDlls.Any()) {
			return string.Empty;
		}

		string csprojPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
		string xmlContent = _fileSystem.ReadAllText(csprojPath);
		XDocument csproj = XDocument.Parse(xmlContent);
		
		string tplFileName = moniker == Moniker.net472 ? "propItem-net472.xml.tpl" : "propItem-netstandard.xml.tpl";
		string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", tplFileName);
		string templateContent = _fileSystem.ReadAllText(templatePath);

		StringBuilder sb = new StringBuilder()
			.AppendLine("<!-- THIS FILE IS AUTO GENERATED USE CLIO CLI FOR HELP-->")
			.AppendLine("<Project>");

		string destinationFolder = moniker switch {
			Moniker.net472 => GetPathTo(ItemType.Net472PackageLibsPath, packageName),
			Moniker.netstandard => GetPathTo(ItemType.NetStdPackageLibsPath, packageName),
			var _ => throw new ArgumentOutOfRangeException(nameof(moniker), moniker, null)
		};
		_fileSystem.CreateOrOverwriteExistsDirectoryIfNeeded(destinationFolder, true);
		foreach (string dll in enumerableDlls) {
			string dllName = Path.GetFileNameWithoutExtension(dll);
			bool isReferenced = csproj
				.Descendants(ReferenceTag)
				.Any(e => e.Attribute(IncludeTag)?.Value == dllName);

			if (isReferenced) {
				//When dll is referenced in csproj file
				//we don't need to add it to props file
				continue;
			}
			string propXml = templateContent.Replace("#dll-name-here#", dllName);
			sb.Append(Environment.NewLine).AppendLine(propXml);

			string binFolder = moniker switch {
				Moniker.net472 => GetPathTo(ItemType.Net472BinDir, packageName),
				Moniker.netstandard => GetPathTo(ItemType.NetStdBinDir, packageName),
				var _ => throw new ArgumentOutOfRangeException(nameof(moniker), moniker, null)
			};
			string fullDllPath = Path.Combine(binFolder, dll);
			_fileSystem.CopyFiles(new[] {fullDllPath}, destinationFolder, true);
		}
		sb.AppendLine("</Project>");
		return sb.ToString();
	}

	#endregion

	#region Methods: Public

	public PropsBuildResult Build(string packageName){
		bool net472PropsCreated = BuildNet472Props(packageName);
		bool netStandardPropsCreated = BuildNetStdProps(packageName);
		return new PropsBuildResult(net472PropsCreated, netStandardPropsCreated);
	}

	#endregion

}