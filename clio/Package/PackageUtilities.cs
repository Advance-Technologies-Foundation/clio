using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Common
{

	#region Class: PackageUtilities

	public class PackageUtilities : IPackageUtilities
	{

		#region Fields: Private

		private readonly IFileSystem _fileSystem;
		private static readonly IEnumerable<string> PackageElementNames = new [] {
			"Assemblies",
			"Bin",
			"Data",
			"Files",
			"Resources",
			"Schemas",
			"SqlScripts"
		};

		#endregion

		#region Constructors: Public

		public PackageUtilities(IFileSystem fileSystem) {
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private void CopyPackageElement(string sourcePath, string destinationPath, string name) {
			string fromAssembliesPath = Path.Combine(sourcePath, name);
			if (Directory.Exists(fromAssembliesPath)) {
				string toAssembliesPath = Path.Combine(destinationPath, name);
				_fileSystem.CopyDirectory(fromAssembliesPath, toAssembliesPath, true);
			}
		}

		#endregion

		#region Methods: Public

		public void CopyPackageElements(string sourcePath, string destinationPath, bool overwrite) {
			sourcePath.CheckArgumentNullOrWhiteSpace(nameof(sourcePath));
			destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
			_fileSystem.CreateOrOverwriteExistsDirectoryIfNeeded(destinationPath, overwrite);
			foreach (string packageElementName in PackageElementNames) {
				CopyPackageElement(sourcePath, destinationPath, packageElementName);
			}
			File.Copy(Path.Combine(sourcePath, "descriptor.json"), 
				Path.Combine(destinationPath, "descriptor.json"));
		}

		public static string GetPackageContentFolderPath(string repositoryPackageFolderPath) {
			string repositoryPackageFolderBranchesPath = Path.Combine(repositoryPackageFolderPath, "branches");
			if (Directory.Exists(repositoryPackageFolderBranchesPath)) {
				DirectoryInfo[] directories = new DirectoryInfo(repositoryPackageFolderBranchesPath).GetDirectories();
				if (directories.Count() == 1) {
					return directories[0].FullName;
				} else {
					throw new NotSupportedException($"Unsupported package folder structure." +
						$"Expected structure contains one package version in folder '{repositoryPackageFolderBranchesPath}'.");
				}
			}
			return repositoryPackageFolderPath;
		}

		public static string GetPackageContentFolderPath(string repositoryFolderPath, string packageName) {
			string fullPackagePath = Path.Combine(repositoryFolderPath, packageName);
			return GetPackageContentFolderPath(fullPackagePath);
		}

		public static string BuildPackageDescriptorPath(string packagePath) =>
			Path.Combine(packagePath, CreatioPackage.DescriptorName);

		#endregion

	}

	#endregion

}