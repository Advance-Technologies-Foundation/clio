using Clio.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Command
{
	internal class RfsEnvironment
	{
		public static IEnumerable<DirectoryInfo> ReadCreatioPackages(string pkgPath) {
			return new DirectoryInfo(pkgPath).GetDirectories();
		}

		public static IEnumerable<string> ReadCreatioWorkspacePakageNames(string repositoryPath) {
			var directories = ReadCreatioWorkspacePakages(repositoryPath);
			return directories.Select(directory => directory.Name);
		}

		public static IEnumerable<DirectoryInfo> ReadCreatioWorkspacePakages(string repositoryPath) {
			var workspacePackagesPath = Path.Combine(repositoryPath, "packages");
			if (Directory.Exists(workspacePackagesPath)) {
				return ReadCreatioPackages(workspacePackagesPath);
			}
			return ReadCreatioPackages(repositoryPath);
		}

		internal static void Link2Repo(string environmentPackagePath, string repositoryPath) {
			var environmentPackageFolders = ReadCreatioPackages(environmentPackagePath).ToList();
			var repositoryPackageFolders = ReadCreatioWorkspacePakages(repositoryPath);
			for (int i = 0; i < environmentPackageFolders.Count(); i++) {
				DirectoryInfo environmentPackageFolder = environmentPackageFolders[i];
				var environmentPackageName = environmentPackageFolder.Name;
				Console.WriteLine($"Processing package '{environmentPackageName}' {i + 1} of {environmentPackageFolders.Count()}.");
				var repositoryPackageFolder = repositoryPackageFolders.FirstOrDefault(s => s.Name == environmentPackageName);
				if (repositoryPackageFolder != null) {
					Console.WriteLine($"Package '{environmentPackageName}' found in repository.");
					environmentPackageFolder.Delete(true);
					string repositoryPackageFolderPath = repositoryPackageFolder.FullName;
					string packageContentFolderPath = PackageUtilities.GetPackageContentFolderPath(repositoryPackageFolderPath);
					FileSystem.CreateLink(packageContentFolderPath, repositoryPackageFolderPath);
				} else {
					Console.WriteLine($"Package '{environmentPackageName}' not found in repository.");
				}
			}
		}

		internal static void Link4Repo(string environmentPackagePath, string repositoryPath, string packages) {
			if (string.IsNullOrEmpty(packages)) {
				throw new Exception("At least one package must be specified or use '*' to include all packages. " +
					"Multiple packages can be separeted by comma.");
			}
			IEnumerable<string> packageNames = null;
			if (packages == "*") {
				packageNames = ReadCreatioWorkspacePakageNames(repositoryPath);
			} else {
				packageNames = packages.Split(',').Select(s => s.Trim());
			}
			var environmentPackageFolders = ReadCreatioPackages(environmentPackagePath).ToList();
			var repositoryPackageFolders = ReadCreatioWorkspacePakages(repositoryPath);
			var repositoryPackageNames = repositoryPackageFolders.Select(s => s.Name);
			var missingPackages = new List<string>();
			foreach (string packageName in packageNames) {
				if (!repositoryPackageNames.Contains(packageName)) {
					missingPackages.Add(packageName);
				}
			}
			if (missingPackages.Any()) {
				throw new Exception($"Packages {string.Join(", ", missingPackages)} not found in repository: {repositoryPath}.");
			}
			foreach (var packageName in packageNames) {
				var environmentPackageDirectory = environmentPackageFolders.FirstOrDefault(s => s.Name == packageName);
				var environmentPackageDirectoryPath = string.Empty;
				if (environmentPackageDirectory != null) {
					environmentPackageDirectoryPath = environmentPackageDirectory.FullName;
					environmentPackageDirectory.Delete(true);
				} else {
					environmentPackageDirectoryPath = Path.Combine(environmentPackagePath, packageName);
				}
				var repositoryPackageFolder = repositoryPackageFolders.FirstOrDefault(s => s.Name == packageName);
				string repositoryPackageContentFolderPath =
					PackageUtilities.GetPackageContentFolderPath(repositoryPackageFolder.FullName);
				FileSystem.CreateLink(environmentPackageDirectoryPath, repositoryPackageContentFolderPath);
			}

		}
	}
}
