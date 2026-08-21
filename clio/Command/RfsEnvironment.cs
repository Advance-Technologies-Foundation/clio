using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Provides repository-to-environment package linking operations.
/// </summary>
public class RfsEnvironment {
	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly IPackageUtilities _packageUtilities;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="RfsEnvironment"/> class.
	/// </summary>
	/// <param name="fileSystem">Filesystem abstraction used for link creation.</param>
	/// <param name="packageUtilities">Package utility service used to resolve package content paths.</param>
	/// <param name="logger">Logger used for progress output.</param>
	public RfsEnvironment(IFileSystem fileSystem, IPackageUtilities packageUtilities, ILogger logger) {
		_fileSystem = fileSystem;
		_packageUtilities = packageUtilities;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private static DirectoryInfo[] ReadCreatioPackages(string pkgPath) {
		return new DirectoryInfo(pkgPath).GetDirectories();
	}

	private static IEnumerable<string> ReadCreatioWorkspacePackageNames(string repositoryPath) {
		DirectoryInfo[] directories = ReadCreatioWorkspacePackages(repositoryPath);
		return directories.Select(directory => directory.Name);
	}

	private static DirectoryInfo[] ReadCreatioWorkspacePackages(string repositoryPath) {
		string workspacePackagesPath = Path.Combine(repositoryPath, "packages");
		return ReadCreatioPackages(Directory.Exists(workspacePackagesPath)
			? workspacePackagesPath
			: repositoryPath);
	}

	#endregion

	#region Methods: Protected

	internal void Link2Repo(string environmentPackagePath, string repositoryPath) {
		List<DirectoryInfo> environmentPackageFolders = ReadCreatioPackages(environmentPackagePath).ToList();
		IEnumerable<DirectoryInfo> repositoryPackageFolders = ReadCreatioWorkspacePackages(repositoryPath);
		for (int i = 0; i < environmentPackageFolders.Count; i++) {
			DirectoryInfo environmentPackageFolder = environmentPackageFolders[i];
			string environmentPackageName = environmentPackageFolder.Name;
				_logger.WriteLine(
					$"Processing package '{environmentPackageName}' {i + 1} of {environmentPackageFolders.Count}.");
			DirectoryInfo repositoryPackageFolder
				= repositoryPackageFolders.FirstOrDefault(s => s.Name == environmentPackageName);
			if (repositoryPackageFolder != null) {
					_logger.WriteLine($"Package '{environmentPackageName}' found in repository.");
				environmentPackageFolder.Delete(true);
				string repositoryPackageFolderPath = repositoryPackageFolder.FullName;
				string packageContentFolderPath
					= _packageUtilities.GetPackageContentFolderPath(repositoryPackageFolderPath);
				_fileSystem.CreateDirectorySymLink(packageContentFolderPath, repositoryPackageFolderPath);
			}
			else {
					_logger.WriteLine($"Package '{environmentPackageName}' not found in repository.");
			}
		}
	}

	internal void Link4Repo(string environmentPackagePath, string repositoryPath, string packages) {
		if (string.IsNullOrEmpty(packages)) {
			throw new Exception("At least one package must be specified or use '*' to include all packages. " +
								"Multiple packages can be separated by comma.");
		}

		IEnumerable<string> packageNames = packages == "*"
			? ReadCreatioWorkspacePackageNames(repositoryPath)
			: packages.Split(',').Select(s => s.Trim());

		List<DirectoryInfo> environmentPackageFolders = ReadCreatioPackages(environmentPackagePath).ToList();
		DirectoryInfo[] repositoryPackageFolders = ReadCreatioWorkspacePackages(repositoryPath);
		IEnumerable<string> repositoryPackageNames = repositoryPackageFolders.Select(s => s.Name);
		List<string> missingPackages = [];
		foreach (string packageName in packageNames) {
			if (!repositoryPackageNames.Contains(packageName)) {
				missingPackages.Add(packageName);
			}
		}

		if (missingPackages.Any()) {
			throw new Exception(
				$"Packages {string.Join(", ", missingPackages)} not found in repository: {repositoryPath}.");
		}

		foreach (string packageName in packageNames) {
			DirectoryInfo environmentPackageDirectory
				= environmentPackageFolders.FirstOrDefault(s => s.Name == packageName);
			string environmentPackageDirectoryPath = string.Empty;
			if (environmentPackageDirectory != null) {
				environmentPackageDirectoryPath = environmentPackageDirectory.FullName;
				environmentPackageDirectory.Delete(true);
			}
			else {
				environmentPackageDirectoryPath = Path.Combine(environmentPackagePath, packageName);
			}

			DirectoryInfo repositoryPackageFolder = repositoryPackageFolders.FirstOrDefault(s => s.Name == packageName);
			if (repositoryPackageFolder == null) {
				// Unreachable in practice: the missingPackages check above already guarantees every
				// packageName is present in repositoryPackageFolders. Guarding explicitly turns a future
				// invariant break into a clear diagnostic instead of a raw NullReferenceException.
				throw new InvalidOperationException(
					$"Package '{packageName}' was validated as present but its repository folder could not be located.");
			}
			string repositoryPackageContentFolderPath =
				_packageUtilities.GetPackageContentFolderPath(repositoryPackageFolder.FullName);
			_fileSystem.CreateDirectorySymLink(environmentPackageDirectoryPath, repositoryPackageContentFolderPath);
		}
	}

	#endregion
}
