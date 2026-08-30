using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Clio.Workspaces;

namespace Clio.Common;

/// <summary>
/// Describes which props files the <see cref="IPropsBuilder"/> produced.
/// </summary>
/// <param name="Net472PropsCreated">True when the net472 props file was written.</param>
/// <param name="NetStandardPropsCreated">True when the netstandard props file was written.</param>
/// <param name="MaterializedAssemblies">
/// Names, without extension, of the assemblies actually copied into the package Libs folder.
/// A nuget package that contributed nothing here is still needed as a PackageReference.
/// </param>
public readonly record struct PropsBuildResult(bool Net472PropsCreated, bool NetStandardPropsCreated,
	IReadOnlyCollection<string> MaterializedAssemblies)
{

	/// <summary>
	/// Names, without extension, of the assemblies actually copied into the package Libs folder.
	/// Never null, so default(PropsBuildResult) stays enumerable.
	/// </summary>
	public IReadOnlyCollection<string> MaterializedAssemblies { get; init; } =
		MaterializedAssemblies ?? Array.Empty<string>();

	/// <summary>
	/// True when at least one props file was written.
	/// </summary>
	public bool HasAnyProps => Net472PropsCreated || NetStandardPropsCreated;

	/// <summary>
	/// True when the given nuget package name matches an assembly that was materialized.
	/// </summary>
	/// <param name="nugetPackageName">Nuget package name as written in the csproj.</param>
	/// <remarks>
	/// A heuristic: a package id is not always its assembly name (NUnit ships
	/// nunit.framework.dll), so an assembly whose name starts with the package id counts as
	/// a match. It errs in both directions - a package whose assembly is named differently
	/// keeps its reference and simply restores as before, while a package named Foo is treated
	/// as materialized when an unrelated Foo.Something.dll appears. The accurate mapping lives
	/// in the nuget project's project.assets.json and is not read here.
	/// </remarks>
	public bool IsMaterialized(string nugetPackageName){
		if (MaterializedAssemblies is null || string.IsNullOrWhiteSpace(nugetPackageName)) {
			return false;
		}
		return MaterializedAssemblies.Any(assembly =>
			assembly.Equals(nugetPackageName, StringComparison.OrdinalIgnoreCase)
			|| assembly.StartsWith(nugetPackageName + ".", StringComparison.OrdinalIgnoreCase));
	}

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

	private const string ConditionTag = "Condition";
	private const string IncludeTag = "Include";
	private const string Net472TargetFramework = "net472";
	private const string NetStandardTargetFramework = "netstandard2.0";
	private const string ProjExtension = ".csproj";
	private const string PropsExtension = ".props";
	private const string ReferenceTag = "Reference";

	#endregion

	#region Fields: Private

	//Matches a whole condition that is a single $(TargetFramework) comparison, in either
	//operand order: '$(TargetFramework)' == 'net472' and 'net472' != '$(TargetFramework)'
	private static readonly Regex SimpleTargetFrameworkConditionRegex = new(
		@"^\s*(?:'\$\(TargetFramework\)'\s*(?<operator>==|!=)\s*'(?<framework>[^']*)'"
		+ @"|'(?<framework2>[^']*)'\s*(?<operator2>==|!=)\s*'\$\(TargetFramework\)')\s*$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	//Any mention of the property, used to tell "no opinion" from "an expression we cannot evaluate"
	private static readonly Regex TargetFrameworkMentionRegex = new(
		@"\$\(TargetFramework[^)]*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

	private bool BuildProps(string packageName, Moniker moniker, ICollection<string> materializedAssemblies){
		ItemType binDirItem = moniker == Moniker.net472 ? ItemType.Net472BinDir : ItemType.NetStdBinDir;
		ItemType propsFileItem = moniker == Moniker.net472
			? ItemType.Net472PropsFilePath
			: ItemType.NetStdPropsFilePath;
		string binDir = GetPathTo(binDirItem, packageName);
		IEnumerable<string> dlls = GetDependencyDlls(binDir, packageName);

		string propsContent = Process(dlls, packageName, moniker, materializedAssemblies);
		string propsFilePath = GetPathTo(propsFileItem, packageName);
		return SavePropsFile(propsFilePath, propsContent, moniker);
	}

	/// <summary>
	/// Returns the dlls the package depends on, excluding the package assembly itself.
	/// Compares file names, so a dependency whose name merely ends with the package
	/// name (Contoso.MyPkg.dll for package MyPkg) is kept.
	/// </summary>
	private IEnumerable<string> GetDependencyDlls(string binDir, string packageName){
		if (!_fileSystem.ExistsDirectory(binDir)) {
			//The nuget project failed to build for this moniker, or was never built.
			//GetFiles would throw DirectoryNotFoundException and hide that.
			_logger.WriteWarning($"Directory {binDir} does not exist, no dependencies to reference");
			return Array.Empty<string>();
		}
		return _fileSystem
			.GetFiles(binDir, "*.dll", SearchOption.TopDirectoryOnly)
			.Where(f => !string.Equals(Path.GetFileNameWithoutExtension(f), packageName,
				StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Writes the props file, or reports that there is nothing to write.
	/// An empty file must never be written: the csproj imports it, and MSBuild
	/// fails the whole project with "Root element is missing".
	/// </summary>
	private bool SavePropsFile(string propsFilePath, string propsContent, Moniker moniker){
		if (string.IsNullOrWhiteSpace(propsContent)) {
			_logger.WriteWarning($"No {moniker} dependencies found, skipping {propsFilePath}");
			//A props file left over from an earlier run no longer describes anything,
			//and an empty one from a clio version before this fix breaks every build.
			_fileSystem.DeleteFileIfExists(propsFilePath);
			return false;
		}
		_logger.WriteLine("Saving props file to " + propsFilePath);
		_fileSystem.WriteAllTextToFile(propsFilePath, propsContent);
		return true;
	}
	/// <summary>
	/// Decides whether a csproj element applies to the target framework being built.
	/// </summary>
	/// <param name="element">Element to inspect, usually a Reference.</param>
	/// <param name="targetFramework">Target framework being built: net472 or netstandard2.0.</param>
	/// <remarks>
	/// A Reference restricted to one target framework - directly, or through an enclosing
	/// Choose/When - says nothing about the others. Treating it as "already referenced"
	/// everywhere drops the dependency from the props file of every other target framework,
	/// and the package then fails to compile for them.
	/// Only a condition that is a single $(TargetFramework) comparison is interpreted. A
	/// condition that mentions the property inside something more complex - And, Or, a negation,
	/// a property function - is treated as NOT applying, so the dll is written into the props
	/// file. That direction is deliberate: a duplicate reference is an MSBuild warning, while a
	/// missing one is a compile error. A condition that does not mention $(TargetFramework) at
	/// all is assumed to apply, as before.
	/// </remarks>
	private static bool AppliesToTargetFramework(XElement element, string targetFramework){
		for (XElement current = element; current is not null; current = current.Parent) {
			string condition = current.Attribute(ConditionTag)?.Value;
			if (string.IsNullOrWhiteSpace(condition)) {
				continue;
			}
			Match match = SimpleTargetFrameworkConditionRegex.Match(condition);
			if (!match.Success) {
				if (TargetFrameworkMentionRegex.IsMatch(condition)) {
					//The condition depends on the target framework in a way clio cannot evaluate
					return false;
				}
				continue;
			}
			string comparisonOperator = match.Groups["operator"].Success
				? match.Groups["operator"].Value
				: match.Groups["operator2"].Value;
			string framework = match.Groups["framework"].Success
				? match.Groups["framework"].Value
				: match.Groups["framework2"].Value;
			bool negated = comparisonOperator == "!=";
			bool matchesFramework = string.Equals(framework, targetFramework, StringComparison.OrdinalIgnoreCase);
			if (matchesFramework == negated) {
				return false;
			}
		}
		return true;
	}

	private string GetPathTo(ItemType itemType, string packageName){
		return itemType switch {
			ItemType.NugetFolder => FilePathGetter(_workspacePathBuilder.NugetFolderPath),
			ItemType.PackageFolder => FilePathGetter(_workspacePathBuilder.PackagesFolderPath),
			ItemType.Net472BinDir => FolderPathGetter(_workspacePathBuilder.NugetFolderPath, Moniker.net472),
			ItemType.NetStdBinDir => FolderPathGetter(_workspacePathBuilder.NugetFolderPath, Moniker.netstandard),
			ItemType.Net472PropsFilePath =>
				_workspacePathBuilder.BuildPackagePropsPath(packageName, Moniker.net472.ToString()),
			ItemType.NetStdPropsFilePath =>
				_workspacePathBuilder.BuildPackagePropsPath(packageName, Moniker.netstandard.ToString()),
			ItemType.Net472PackageLibsPath => PackageLibsPath(_workspacePathBuilder.PackagesFolderPath, Moniker.net472),
			ItemType.NetStdPackageLibsPath => PackageLibsPath(_workspacePathBuilder.PackagesFolderPath,
				Moniker.netstandard),
			var _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, null)
		};

		string PackageLibsPath(string path, Moniker moniker) =>
			Path.Combine(path, packageName, "Files", "Libs", moniker.ToString());

		string FilePathGetter(string path) => Path.Combine(path, packageName, packageName + ProjExtension);

		string FolderPathGetter(string path, Moniker moniker) =>
			Path.Combine(path, packageName, "bin", moniker.ToString());
	}

	private string Process(IEnumerable<string> dlls, string packageName, Moniker moniker,
		ICollection<string> materializedAssemblies){
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
		int referencedDllCount = 0;

		string destinationFolder = moniker switch {
			Moniker.net472 => GetPathTo(ItemType.Net472PackageLibsPath, packageName),
			Moniker.netstandard => GetPathTo(ItemType.NetStdPackageLibsPath, packageName),
			var _ => throw new ArgumentOutOfRangeException(nameof(moniker), moniker, null)
		};
		_fileSystem.CreateOrOverwriteExistsDirectoryIfNeeded(destinationFolder, true);
		foreach (string dll in enumerableDlls) {
			string dllName = Path.GetFileNameWithoutExtension(dll);
			string targetFramework = moniker == Moniker.net472 ? Net472TargetFramework : NetStandardTargetFramework;
			bool isReferenced = csproj
				.Descendants(ReferenceTag)
				.Where(e => AppliesToTargetFramework(e, targetFramework))
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
			materializedAssemblies.Add(dllName);
			referencedDllCount++;
		}
		sb.AppendLine("</Project>");
		//A props file that references nothing is legal MSBuild but pointless, and importing it
		//only hides that no dependency was materialized for this moniker.
		return referencedDllCount == 0 ? string.Empty : sb.ToString();
	}

	#endregion

	#region Methods: Public

	public PropsBuildResult Build(string packageName){
		HashSet<string> materializedAssemblies = new(StringComparer.OrdinalIgnoreCase);
		bool net472PropsCreated = BuildProps(packageName, Moniker.net472, materializedAssemblies);
		bool netStandardPropsCreated = BuildProps(packageName, Moniker.netstandard, materializedAssemblies);
		return new PropsBuildResult(net472PropsCreated, netStandardPropsCreated, materializedAssemblies);
	}

	#endregion

}