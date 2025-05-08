using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;
using FileSystem = System.IO.Abstractions.FileSystem;

namespace Clio.Command;

internal class RfsEnvironment
{
    public static IEnumerable<DirectoryInfo> ReadCreatioPackages(string pkgPath) =>
        new DirectoryInfo(pkgPath).GetDirectories();

    public static IEnumerable<string> ReadCreatioWorkspacePakageNames(string repositoryPath)
    {
        IEnumerable<DirectoryInfo> directories = ReadCreatioWorkspacePakages(repositoryPath);
        return directories.Select(directory => directory.Name);
    }

    public static IEnumerable<DirectoryInfo> ReadCreatioWorkspacePakages(string repositoryPath)
    {
        string workspacePackagesPath = Path.Combine(repositoryPath, "packages");
        if (Directory.Exists(workspacePackagesPath))
        {
            return ReadCreatioPackages(workspacePackagesPath);
        }

        return ReadCreatioPackages(repositoryPath);
    }

    internal static void Link2Repo(string environmentPackagePath, string repositoryPath)
    {
        List<DirectoryInfo> environmentPackageFolders = ReadCreatioPackages(environmentPackagePath).ToList();
        IEnumerable<DirectoryInfo> repositoryPackageFolders = ReadCreatioWorkspacePakages(repositoryPath);
        for (int i = 0; i < environmentPackageFolders.Count(); i++)
        {
            DirectoryInfo environmentPackageFolder = environmentPackageFolders[i];
            string environmentPackageName = environmentPackageFolder.Name;
            Console.WriteLine(
                $"Processing package '{environmentPackageName}' {i + 1} of {environmentPackageFolders.Count()}.");
            DirectoryInfo? repositoryPackageFolder =
                repositoryPackageFolders.FirstOrDefault(s => s.Name == environmentPackageName);
            if (repositoryPackageFolder != null)
            {
                Console.WriteLine($"Package '{environmentPackageName}' found in repository.");
                environmentPackageFolder.Delete(true);
                string repositoryPackageFolderPath = repositoryPackageFolder.FullName;
                PackageUtilities packageUtilities = new(new Common.FileSystem(new FileSystem()));
                string packageContentFolderPath =
                    packageUtilities.GetPackageContentFolderPath(repositoryPackageFolderPath);
                Common.FileSystem.CreateLink(packageContentFolderPath, repositoryPackageFolderPath);
            }
            else
            {
                Console.WriteLine($"Package '{environmentPackageName}' not found in repository.");
            }
        }
    }

    internal static void Link4Repo(string environmentPackagePath, string repositoryPath, string packages)
    {
        if (string.IsNullOrEmpty(packages))
        {
            throw new Exception("At least one package must be specified or use '*' to include all packages. " +
                                "Multiple packages can be separeted by comma.");
        }

        IEnumerable<string> packageNames = null;
        if (packages == "*")
        {
            packageNames = ReadCreatioWorkspacePakageNames(repositoryPath);
        }
        else
        {
            packageNames = packages.Split(',').Select(s => s.Trim());
        }

        List<DirectoryInfo> environmentPackageFolders = ReadCreatioPackages(environmentPackagePath).ToList();
        IEnumerable<DirectoryInfo> repositoryPackageFolders = ReadCreatioWorkspacePakages(repositoryPath);
        IEnumerable<string> repositoryPackageNames = repositoryPackageFolders.Select(s => s.Name);
        List<string> missingPackages = [];
        foreach (string packageName in packageNames)
        {
            if (!repositoryPackageNames.Contains(packageName))
            {
                missingPackages.Add(packageName);
            }
        }

        if (missingPackages.Any())
        {
            throw new Exception(
                $"Packages {string.Join(", ", missingPackages)} not found in repository: {repositoryPath}.");
        }

        foreach (string packageName in packageNames)
        {
            DirectoryInfo? environmentPackageDirectory =
                environmentPackageFolders.FirstOrDefault(s => s.Name == packageName);
            string environmentPackageDirectoryPath = string.Empty;
            if (environmentPackageDirectory != null)
            {
                environmentPackageDirectoryPath = environmentPackageDirectory.FullName;
                environmentPackageDirectory.Delete(true);
            }
            else
            {
                environmentPackageDirectoryPath = Path.Combine(environmentPackagePath, packageName);
            }

            DirectoryInfo? repositoryPackageFolder =
                repositoryPackageFolders.FirstOrDefault(s => s.Name == packageName);
            PackageUtilities packageUtilities = new(new Common.FileSystem(new FileSystem()));
            string repositoryPackageContentFolderPath =
                packageUtilities.GetPackageContentFolderPath(repositoryPackageFolder.FullName);
            Common.FileSystem.CreateLink(environmentPackageDirectoryPath, repositoryPackageContentFolderPath);
        }
    }
}
