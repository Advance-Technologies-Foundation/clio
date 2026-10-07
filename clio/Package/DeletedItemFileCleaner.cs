using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Package;

/// <summary>
/// What <see cref="IDeletedItemFileCleaner"/> did with the files of a deleted workspace item.
/// </summary>
/// <remarks>
/// The zero value is deliberately "nothing was removed", so a default value never reads as a cleanup.
/// </remarks>
public enum DeletedItemFileCleanupStatus {

	/// <summary>clio could not reach or identify the item's folders, so nothing was removed.</summary>
	NotCleaned = 0,

	/// <summary>
	/// clio went through the package folder. Folders it found but could not remove are in
	/// <see cref="DeletedItemFileCleanupResult.RemainingFolders"/>, which callers must check.
	/// </summary>
	Cleaned = 1
}

/// <summary>
/// Describes a workspace item that was just deleted from an environment and whose files may remain in its package.
/// </summary>
/// <param name="EnvironmentName">
/// Registered clio environment name whose <c>EnvironmentPath</c> locates the site folder; <c>null</c> when the call
/// passed <c>--uri</c>, so only <paramref name="EnvironmentPathOverride"/> can locate it.
/// </param>
/// <param name="EnvironmentPathOverride">Site root folder supplied by the caller; overrides the registered one.</param>
/// <param name="PackageName">Package the item belonged to.</param>
/// <param name="ItemName">Item name as WorkspaceExplorerService reported it.</param>
/// <param name="ItemType">Item type as WorkspaceExplorerService reported it (<c>WorkspaceExplorerItemType</c>).</param>
public sealed record DeletedItemFileCleanupRequest(
	string EnvironmentName,
	string EnvironmentPathOverride,
	string PackageName,
	string ItemName,
	int ItemType);

/// <summary>
/// Result of removing the files of a deleted workspace item from its package folder.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="PackageFolderPath">Package folder that was cleaned; <c>null</c> unless <see cref="Status"/> is Cleaned.</param>
/// <param name="ExpectedFolders">Package-relative folders where the platform keeps this item (patterns use <c>*</c>).</param>
/// <param name="RemovedFolders">Package-relative folders that were removed.</param>
/// <param name="RemainingFolders">Package-relative folders that were found but could not be removed, with the reason.</param>
/// <param name="Problem">Why nothing was removed; <c>null</c> when <see cref="Status"/> is Cleaned.</param>
public sealed record DeletedItemFileCleanupResult(
	DeletedItemFileCleanupStatus Status,
	string PackageFolderPath,
	IReadOnlyList<string> ExpectedFolders,
	IReadOnlyList<string> RemovedFolders,
	IReadOnlyList<string> RemainingFolders,
	string Problem);

/// <summary>
/// Removes the files of a deleted workspace item from its package folder on this machine. The platform's
/// <c>WorkspaceExplorerService.svc/Delete</c> deletes database rows only, so in file system mode the item's folders
/// stay in <c>Pkg/&lt;package&gt;</c> and the next <c>pkg-to-db</c> registers the item again.
/// </summary>
/// <remarks>
/// Call it only for an environment that reports file design mode as on: outside file system mode the site does not
/// read the package folder, and its files are not the caller's to remove.
/// </remarks>
public interface IDeletedItemFileCleaner {

	/// <summary>
	/// Removes the item's folders from the package folder on this machine.
	/// </summary>
	/// <param name="request">The deleted item.</param>
	/// <returns>
	/// What was removed, what remains, or why nothing was attempted; never <c>null</c>. File system failures are
	/// reported in the result, not thrown.
	/// </returns>
	DeletedItemFileCleanupResult Clean(DeletedItemFileCleanupRequest request);
}

/// <inheritdoc cref="IDeletedItemFileCleaner"/>
public sealed class DeletedItemFileCleaner : IDeletedItemFileCleaner {

	private const string LinkNotFollowed = "it is a symbolic link; clio does not follow links below the package folder";

	private readonly IEnvironmentPackageFolderResolver _packageFolderResolver;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="DeletedItemFileCleaner"/> class.
	/// </summary>
	/// <param name="packageFolderResolver">Locates the package folder on this machine.</param>
	/// <param name="fileSystem">File system the folders are removed from.</param>
	public DeletedItemFileCleaner(IEnvironmentPackageFolderResolver packageFolderResolver, IFileSystem fileSystem) {
		_packageFolderResolver = packageFolderResolver ?? throw new ArgumentNullException(nameof(packageFolderResolver));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	/// <inheritdoc />
	public DeletedItemFileCleanupResult Clean(DeletedItemFileCleanupRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		IReadOnlyList<PackageItemFolders.Rule> rules = PackageItemFolders.GetRules(request.ItemType, request.ItemName);
		IReadOnlyList<string> expected = rules.Select(rule => rule.Describe()).ToList();
		if (!PackageItemFolders.IsSafeFolderName(request.ItemName)) {
			return NotAttempted(expected,
				$"item name '{request.ItemName}' is not a valid folder name");
		}
		if (rules.Count == 0) {
			return NotAttempted(expected,
				$"clio does not know where items of type {request.ItemType} are stored");
		}
		EnvironmentPackageFolderResolution folder = _packageFolderResolver.Resolve(request.EnvironmentName,
			request.PackageName, request.EnvironmentPathOverride);
		if (!folder.IsResolved) {
			return NotAttempted(expected, folder.Problem);
		}
		if (request.ItemType == PackageItemFolders.LocalizationSchemaType
			&& HoldsSchemaFolder(folder.PackageFolderPath, request.ItemName)) {
			// A localization item shares Resources/<name>.*/ with a schema of the same name in this package;
			// removing that folder would strip the remaining schema's captions.
			return new DeletedItemFileCleanupResult(DeletedItemFileCleanupStatus.Cleaned, folder.PackageFolderPath,
				expected, [],
				[$"{expected[0]} (the package also holds schema '{request.ItemName}', which uses these resources)"],
				null);
		}
		return RemoveFolders(folder.PackageFolderPath, rules, expected);
	}

	private bool HoldsSchemaFolder(string packageFolderPath, string itemName) {
		string schemasPath = _fileSystem.Combine(packageFolderPath, PackageItemFolders.SchemasArea);
		if (!_fileSystem.ExistsDirectory(schemasPath)) {
			return false;
		}
		if (!TryListFolders(schemasPath, out string[] folderPaths, out _)) {
			// Unknown is treated as present: leaving a resource folder behind is reported, removing a live one is not.
			return true;
		}
		return folderPaths.Any(path => string.Equals(Path.GetFileName(path), itemName, StringComparison.OrdinalIgnoreCase));
	}

	private static DeletedItemFileCleanupResult NotAttempted(IReadOnlyList<string> expected, string problem) =>
		new(DeletedItemFileCleanupStatus.NotCleaned, null, expected, [], [], problem);

	private DeletedItemFileCleanupResult RemoveFolders(string packageFolderPath,
		IReadOnlyList<PackageItemFolders.Rule> rules, IReadOnlyList<string> expected) {
		List<string> removed = [];
		List<string> remaining = [];
		foreach (PackageItemFolders.Rule rule in rules) {
			string areaPath = _fileSystem.Combine(packageFolderPath, rule.Area);
			if (!_fileSystem.ExistsDirectory(areaPath)) {
				continue;
			}
			// Enumerate instead of combining the item name into a path: the match is case-insensitive like
			// Creatio names, and only real children of the area folder can ever be removed.
			if (!TryListFolders(areaPath, out string[] folderPaths, out string listingFailure)) {
				remaining.Add($"{rule.Describe()} ({listingFailure})");
				continue;
			}
			foreach (string folderPath in folderPaths) {
				string folderName = Path.GetFileName(folderPath);
				if (!rule.Matches(folderName)) {
					continue;
				}
				string relativePath = $"{rule.Area}/{folderName}/";
				string failure = TryRemove(folderPath);
				if (failure is null) {
					removed.Add(relativePath);
				} else {
					remaining.Add($"{relativePath} ({failure})");
				}
			}
		}
		return new DeletedItemFileCleanupResult(DeletedItemFileCleanupStatus.Cleaned, packageFolderPath, expected,
			removed, remaining, null);
	}

	private bool TryListFolders(string areaPath, out string[] folderPaths, out string failure) {
		try {
			// The package folder itself may be a link (link-from-repository); anything below it is not followed,
			// so a removal can never land outside the package.
			if (_fileSystem.GetDirectoryInfo(areaPath).LinkTarget is not null) {
				folderPaths = [];
				failure = LinkNotFollowed;
				return false;
			}
			folderPaths = _fileSystem.GetDirectories(areaPath);
			failure = null;
			return true;
		}
		catch (IOException exception) {
			failure = exception.Message;
		}
		catch (UnauthorizedAccessException exception) {
			failure = exception.Message;
		}
		folderPaths = [];
		return false;
	}

	private string TryRemove(string folderPath) {
		try {
			if (_fileSystem.GetDirectoryInfo(folderPath).LinkTarget is not null) {
				return LinkNotFollowed;
			}
			_fileSystem.DeleteDirectory(folderPath, true);
			return null;
		}
		catch (IOException exception) {
			return exception.Message;
		}
		catch (UnauthorizedAccessException exception) {
			return exception.Message;
		}
	}
}
