using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Clio.Workspaces;

namespace Clio.Common;

/// <summary>
///     Build .props file for the main csproj file of a package.
/// </summary>
public interface IPropsBuilder
{
    /// <summary>
    ///     Builds props files for the project and copies all dlls from
    ///     nuget/bin folder to the package Libs folder.
    /// </summary>
    /// <param name="packageName">Package name to convert.</param>
    /// <remarks>
    ///     It add Libs folder with the following structure : <br />
    ///     📦Files                                <br />
    ///     ┣ 📂Libs                               <br />
    ///     ┃ ┣ 📂net472                           <br />
    ///     ┃ ┗ 📂netstandard                      <br />
    ///     ┣ 📜PKG_NAME-net472.nuget.props        <br />
    ///     ┣ 📜PKG_NAME-netstandard.nuget.props.   <br />
    /// </remarks>
    void Build(string packageName);
}

public class PropsBuilder(IFileSystem fileSystem, ILogger logger, IWorkspacePathBuilder workspacePathBuilder)
    : IPropsBuilder
{
    private const string IncludeTag = "Include";
    private const string ProjExtension = ".csproj";
    private const string PropsExtension = ".props";
    private const string ReferenceTag = "Reference";
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ILogger _logger = logger;
    private readonly IWorkspacePathBuilder _workspacePathBuilder = workspacePathBuilder;

    public void Build(string packageName)
    {
        BuildNet472Props(packageName);
        BuildNetStdProps(packageName);
    }

    private void BuildNet472Props(string packageName)
    {
        string net472BinDir = GetPathTo(ItemType.Net472BinDir, packageName);
        IEnumerable<string> net472Files = _fileSystem
            .GetFiles(net472BinDir, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(packageName + ".dll"));

        string net472PropsContent = Process(net472Files, packageName, Moniker.Net472);
        string net472PropsFilePath = GetPathTo(ItemType.Net472PropsFilePath, packageName);

        _logger.WriteLine("Saving props file to " + net472PropsFilePath);
        _fileSystem.WriteAllTextToFile(net472PropsFilePath, net472PropsContent);
    }

    private void BuildNetStdProps(string packageName)
    {
        string netStdBinDir = GetPathTo(ItemType.NetStdBinDir, packageName);
        IEnumerable<string> netStdFiles = _fileSystem
            .GetFiles(netStdBinDir, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(packageName + ".dll"));

        string netStdPropsContent = Process(netStdFiles, packageName, Moniker.Netstandard);
        string netStdPropsFilePath = GetPathTo(ItemType.NetStdPropsFilePath, packageName);

        _logger.WriteLine("Saving props file to " + netStdPropsFilePath);
        _fileSystem.WriteAllTextToFile(netStdPropsFilePath, netStdPropsContent);
    }

    private string GetPathTo(ItemType itemType, string packageName)
    {
        return itemType switch
        {
            ItemType.NugetFolder => FilePathGetter(_workspacePathBuilder.NugetFolderPath),
            ItemType.PackageFolder => FilePathGetter(_workspacePathBuilder.PackagesFolderPath),
            ItemType.Net472BinDir => FolderPathGetter(_workspacePathBuilder.NugetFolderPath, Moniker.Net472),
            ItemType.NetStdBinDir => FolderPathGetter(_workspacePathBuilder.NugetFolderPath, Moniker.Netstandard),
            ItemType.Net472PropsFilePath => PackageFolderPathGetter(
                _workspacePathBuilder.PackagesFolderPath,
                Moniker.Net472),
            ItemType.NetStdPropsFilePath => PackageFolderPathGetter(
                _workspacePathBuilder.PackagesFolderPath,
                Moniker.Netstandard),
            ItemType.Net472PackageLibsPath => PackageLibsPath(_workspacePathBuilder.PackagesFolderPath, Moniker.Net472),
            ItemType.NetStdPackageLibsPath => PackageLibsPath(
                _workspacePathBuilder.PackagesFolderPath,
                Moniker.Netstandard),
            _ => throw new ArgumentOutOfRangeException(nameof(itemType), itemType, null)
        };

        string PackageLibsPath(string path, Moniker moniker)
        {
            return Path.Combine(path, packageName, "Files", "Libs", moniker.ToString());
        }

        string FilePathGetter(string path)
        {
            return Path.Combine(path, packageName, packageName + ProjExtension);
        }

        string PackageFolderPathGetter(string path, Moniker moniker)
        {
            return Path.Combine(path, packageName, "Files", packageName + "-" + moniker + ".nuget" + PropsExtension);
        }

        string FolderPathGetter(string path, Moniker moniker)
        {
            return Path.Combine(path, packageName, "bin", moniker.ToString());
        }
    }

    private string Process(IEnumerable<string> dlls, string packageName, Moniker moniker)
    {
        IEnumerable<string> enumerableDlls = dlls as string[] ?? dlls.ToArray();
        if (!enumerableDlls.Any())
        {
            return string.Empty;
        }

        string csprojPath = _workspacePathBuilder.BuildPackageProjectPath(packageName);
        string xmlContent = _fileSystem.ReadAllText(csprojPath);
        XDocument csproj = XDocument.Parse(xmlContent);

        string tplFileName = moniker == Moniker.Net472 ? "propItem-net472.xml.tpl" : "propItem-netstandard.xml.tpl";
        string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", tplFileName);
        string templateContent = _fileSystem.ReadAllText(templatePath);

        StringBuilder sb = new StringBuilder()
            .AppendLine("<!-- THIS FILE IS AUTO GENERATED USE CLIO CLI FOR HELP-->")
            .AppendLine("<Project>");

        string destinationFolder = moniker switch
        {
            Moniker.Net472 => GetPathTo(ItemType.Net472PackageLibsPath, packageName),
            Moniker.Netstandard => GetPathTo(ItemType.NetStdPackageLibsPath, packageName),
            _ => throw new ArgumentOutOfRangeException(nameof(moniker), moniker, null)
        };
        _fileSystem.CreateOrOverwriteExistsDirectoryIfNeeded(destinationFolder, true);
        foreach (string dll in enumerableDlls)
        {
            string dllName = Path.GetFileNameWithoutExtension(dll);
            bool isReferenced = csproj
                .Descendants(ReferenceTag)
                .Any(e => e.Attribute(IncludeTag)?.Value == dllName);

            if (isReferenced)
            {
                // When dll is referenced in csproj file
                // we don't need to add it to props file
                continue;
            }

            string propXml = templateContent.Replace("#dll-name-here#", dllName);
            sb.Append(Environment.NewLine).AppendLine(propXml);

            string binFolder = moniker switch
            {
                Moniker.Net472 => GetPathTo(ItemType.Net472BinDir, packageName),
                Moniker.Netstandard => GetPathTo(ItemType.NetStdBinDir, packageName),
                _ => throw new ArgumentOutOfRangeException(nameof(moniker), moniker, null)
            };
            string fullDllPath = Path.Combine(binFolder, dll);
            _fileSystem.CopyFiles(new[] { fullDllPath }, destinationFolder, true);
        }

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

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
        Net472,
        Netstandard
    }
}
