using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Common;

public class PackageUtilities : IPackageUtilities
{
    private static readonly IEnumerable<string> PackageElementNames =
        new[] { "Assemblies", "Bin", "Data", "Files", "Resources", "Schemas", "SqlScripts" };

    private readonly IFileSystem _fileSystem;

    public PackageUtilities(IFileSystem fileSystem)
    {
        fileSystem.CheckArgumentNull(nameof(fileSystem));
        _fileSystem = fileSystem;
    }

    public void CopyPackageElements(string sourcePath, string destinationPath, bool overwrite)
    {
        sourcePath.CheckArgumentNullOrWhiteSpace(nameof(sourcePath));
        destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
        string packageContentPath = GetPackageContentFolderPath(sourcePath);
        _fileSystem.CreateOrOverwriteExistsDirectoryIfNeeded(destinationPath, overwrite);
        foreach (string packageElementName in PackageElementNames)
        {
            CopyPackageElement(packageContentPath, destinationPath, packageElementName);
        }

        _fileSystem.CopyFile(
            Path.Combine(packageContentPath, "descriptor.json"),
            Path.Combine(destinationPath, "descriptor.json"), overwrite);
    }

    public string GetPackageContentFolderPath(string repositoryPackageFolderPath)
    {
        string repositoryPackageFolderBranchesPath = Path.Combine(repositoryPackageFolderPath, "branches");
        if (_fileSystem.ExistsDirectory(repositoryPackageFolderBranchesPath))
        {
            DirectoryInfo[] directories = new DirectoryInfo(repositoryPackageFolderBranchesPath).GetDirectories();
            if (directories.Count() == 1)
            {
                return directories[0].FullName;
            }

            throw new NotSupportedException($"Unsupported package folder structure." +
                                            $"Expected structure contains one package version in folder '{repositoryPackageFolderBranchesPath}'.");
        }

        return repositoryPackageFolderPath;
    }

    public string GetPackageContentFolderPath(string repositoryFolderPath, string packageName)
    {
        string fullPackagePath = Path.Combine(repositoryFolderPath, packageName);
        return GetPackageContentFolderPath(fullPackagePath);
    }

    private void CopyPackageElement(string sourcePath, string destinationPath, string name)
    {
        string fromAssembliesPath = Path.Combine(sourcePath, name);
        if (_fileSystem.ExistsDirectory(fromAssembliesPath))
        {
            string toAssembliesPath = Path.Combine(destinationPath, name);
            _fileSystem.CopyDirectory(fromAssembliesPath, toAssembliesPath, true);
        }
    }

    public static string BuildPackageDescriptorPath(string packagePath) =>
        Path.Combine(packagePath, CreatioPackage.DescriptorName);
}
