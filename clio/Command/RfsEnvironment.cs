using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Provides repository-to-environment package linking operations.
/// </summary>
public class RfsEnvironment {
	private sealed record PackageIdentity(string Name, Guid UId);

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

	private static void CopyPhysicalDirectory(string sourcePath, string destinationPath) {
		Directory.CreateDirectory(destinationPath);
		foreach (FileSystemInfo entry in new DirectoryInfo(sourcePath).EnumerateFileSystemInfos()) {
			RejectReparsePoint(entry);
			string destinationEntryPath = Path.Combine(destinationPath, entry.Name);
			if (entry is DirectoryInfo directory) {
				CopyPhysicalDirectory(directory.FullName, destinationEntryPath);
			}
			else if (entry is FileInfo file) {
				File.Copy(file.FullName, destinationEntryPath, overwrite: false);
			}
		}
	}

	private static bool IsSamePath(string firstPath, string secondPath) {
		StringComparison comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		return string.Equals(
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
			Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
			comparison);
	}

	private static bool IsPathInside(string rootPath, string path) {
		string relativePath = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(path));
		return !Path.IsPathRooted(relativePath)
			&& relativePath != ".."
			&& !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
			&& !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
	}

	private static PackageIdentity ReadPackageIdentity(string packagePath) {
		string descriptorPath = Path.Combine(packagePath, "descriptor.json");
		FileInfo descriptor = new(descriptorPath);
		if (!descriptor.Exists) {
			throw new InvalidOperationException($"Package descriptor is missing: {descriptorPath}.");
		}
		RejectReparsePoint(descriptor);

		using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(descriptorPath));
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty("Descriptor", out JsonElement descriptorElement)
			|| descriptorElement.ValueKind != JsonValueKind.Object
			|| !descriptorElement.TryGetProperty("Name", out JsonElement nameElement)
			|| nameElement.ValueKind != JsonValueKind.String
			|| string.IsNullOrWhiteSpace(nameElement.GetString())
			|| !descriptorElement.TryGetProperty("UId", out JsonElement uidElement)
			|| uidElement.ValueKind != JsonValueKind.String
			|| !Guid.TryParse(uidElement.GetString(), out Guid uid)
			|| uid == Guid.Empty) {
			throw new InvalidOperationException(
				$"Package descriptor must contain non-empty Descriptor.Name and Descriptor.UId: {descriptorPath}.");
		}

		return new PackageIdentity(nameElement.GetString()!, uid);
	}

	private static void RejectReparsePoint(FileSystemInfo entry) {
		if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) {
			throw new InvalidOperationException(
				$"Package runtime output must not contain symbolic links or reparse points: {entry.FullName}.");
		}
	}

	private void ValidateRepositoryContentPath(string packageRootPath, string packageContentPath) {
		if (!IsPathInside(packageRootPath, packageContentPath)
			|| _fileSystem.HasLinkWithin(packageRootPath, packageContentPath)) {
			throw new InvalidOperationException(
				$"Repository package content must stay inside its physical package root: {packageContentPath}.");
		}
	}

	private static void ValidatePackageIdentity(
		string packageName,
		string environmentPackagePath,
		string repositoryPackagePath) {
		PackageIdentity environmentIdentity = ReadPackageIdentity(environmentPackagePath);
		PackageIdentity repositoryIdentity = ReadPackageIdentity(repositoryPackagePath);
		if (!string.Equals(environmentIdentity.Name, packageName, StringComparison.Ordinal)
			|| !string.Equals(repositoryIdentity.Name, packageName, StringComparison.Ordinal)
			|| environmentIdentity != repositoryIdentity) {
			throw new InvalidOperationException(
				$"Package '{packageName}' descriptor identity does not match between the environment and repository.");
		}
	}

	private static bool IsExistingLinkToRepository(DirectoryInfo environmentPackage, string repositoryPackagePath) {
		if ((environmentPackage.Attributes & FileAttributes.ReparsePoint) == 0) {
			return false;
		}

		FileSystemInfo resolved = environmentPackage.ResolveLinkTarget(returnFinalTarget: true)
			?? throw new InvalidOperationException(
				$"Package link target cannot be resolved: {environmentPackage.FullName}.");
		if (!IsSamePath(resolved.FullName, repositoryPackagePath)) {
			throw new InvalidOperationException(
				$"Package '{environmentPackage.Name}' is linked to a different repository location.");
		}

		return true;
	}

	private static void PreserveRuntimeOutput(string environmentPackagePath, string repositoryPackagePath) {
		string sourceFilesPath = Path.Combine(environmentPackagePath, "Files");
		string sourceBinPath = Path.Combine(sourceFilesPath, "Bin");
		if (!Directory.Exists(sourceBinPath)) {
			return;
		}

		RejectReparsePoint(new DirectoryInfo(sourceFilesPath));
		RejectReparsePoint(new DirectoryInfo(sourceBinPath));

		string destinationFilesPath = Path.Combine(repositoryPackagePath, "Files");
		string destinationBinPath = Path.Combine(destinationFilesPath, "Bin");
		if (Directory.Exists(destinationBinPath)) {
			RejectReparsePoint(new DirectoryInfo(destinationFilesPath));
			RejectReparsePoint(new DirectoryInfo(destinationBinPath));
			return;
		}
		if (File.Exists(destinationBinPath)) {
			throw new InvalidOperationException(
				$"Repository package runtime output path is not a directory: {destinationBinPath}.");
		}

		if (Directory.Exists(destinationFilesPath)) {
			RejectReparsePoint(new DirectoryInfo(destinationFilesPath));
		}
		else if (File.Exists(destinationFilesPath)) {
			throw new InvalidOperationException(
				$"Repository package Files path is not a directory: {destinationFilesPath}.");
		}
		else {
			Directory.CreateDirectory(destinationFilesPath);
		}

		string stagingPath = Path.Combine(destinationFilesPath, $".clio-bin-{Guid.NewGuid():N}");
		try {
			CopyPhysicalDirectory(sourceBinPath, stagingPath);
			if (Directory.Exists(destinationBinPath) || File.Exists(destinationBinPath)) {
				throw new IOException($"Repository package runtime output appeared while it was being preserved: {destinationBinPath}.");
			}
			Directory.Move(stagingPath, destinationBinPath);
		}
		catch {
			if (Directory.Exists(stagingPath)) {
				Directory.Delete(stagingPath, recursive: true);
			}
			throw;
		}
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
			RejectReparsePoint(repositoryPackageFolder);
			ValidateRepositoryContentPath(
				repositoryPackageFolder.FullName,
				repositoryPackageContentFolderPath);
			if (environmentPackageDirectory != null) {
				if (IsExistingLinkToRepository(environmentPackageDirectory, repositoryPackageContentFolderPath)) {
					continue;
				}
				RejectReparsePoint(environmentPackageDirectory);
				ValidatePackageIdentity(
					packageName,
					environmentPackageDirectory.FullName,
					repositoryPackageContentFolderPath);
				PreserveRuntimeOutput(
					environmentPackageDirectory.FullName,
					repositoryPackageContentFolderPath);
				environmentPackageDirectory.Delete(true);
			}
			_fileSystem.CreateDirectorySymLink(environmentPackageDirectoryPath, repositoryPackageContentFolderPath);
		}
	}

	#endregion
}
