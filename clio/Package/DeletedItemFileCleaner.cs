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
/// The zero value is deliberately "not checked": an unstubbed test substitute returns the enum's zero value,
/// and it must not look like a completed cleanup.
/// </remarks>
public enum DeletedItemFileCleanupStatus {

	/// <summary>The file system mode of the environment could not be read, so nothing was removed.</summary>
	FileSystemModeUnknown = 0,

	/// <summary>The environment is not in file system mode; the package files are not the source of truth.</summary>
	FileSystemModeOff = 1,

	/// <summary>The environment is in file system mode, but clio could not reach or identify the item's folders.</summary>
	NotCleaned = 2,

	/// <summary>The environment is in file system mode and clio removed the item's folders it found.</summary>
	Cleaned = 3
}

/// <summary>
/// Describes a workspace item that was just deleted from an environment and whose files may remain in its package.
/// </summary>
/// <param name="EnvironmentName">Registered clio environment name; <c>null</c> when the call used a bare URI.</param>
/// <param name="EnvironmentPathOverride">Site root folder supplied by the caller; overrides the registered one.</param>
/// <param name="PackageName">Package the item belonged to.</param>
/// <param name="ItemName">Item name as WorkspaceExplorerService reported it.</param>
/// <param name="ItemType">Item type as WorkspaceExplorerService reported it (platform <c>WorkspaceItemType</c>).</param>
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
/// <param name="Problem">Why nothing was removed; <c>null</c> for Cleaned and FileSystemModeOff.</param>
public sealed record DeletedItemFileCleanupResult(
	DeletedItemFileCleanupStatus Status,
	string PackageFolderPath,
	IReadOnlyList<string> ExpectedFolders,
	IReadOnlyList<string> RemovedFolders,
	IReadOnlyList<string> RemainingFolders,
	string Problem);

/// <summary>
/// Removes the files of a deleted workspace item from its package folder when the environment is in file
/// system mode. The platform's <c>WorkspaceExplorerService.svc/Delete</c> deletes database rows only, so in file
/// system mode the item's folders stay in <c>Pkg/&lt;package&gt;</c> and the next <c>pkg-to-db</c> registers the
/// item again.
/// </summary>
public interface IDeletedItemFileCleaner {

	/// <summary>
	/// Checks the environment's file system mode and, when it is on, removes the item's folders from the
	/// package folder on this machine.
	/// </summary>
	/// <param name="request">The deleted item.</param>
	/// <returns>What was removed, what remains, or why nothing was attempted. Failures are reported, not thrown.</returns>
	DeletedItemFileCleanupResult Clean(DeletedItemFileCleanupRequest request);
}

/// <inheritdoc cref="IDeletedItemFileCleaner"/>
public sealed class DeletedItemFileCleaner : IDeletedItemFileCleaner {

	private const string FileSystemModeOn = "on";

	private readonly IFsmModeStatusService _fsmModeStatusService;
	private readonly IEnvironmentPackageFolderResolver _packageFolderResolver;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="DeletedItemFileCleaner"/> class.
	/// </summary>
	/// <param name="fsmModeStatusService">Reads whether the environment is in file system mode.</param>
	/// <param name="packageFolderResolver">Locates the package folder on this machine.</param>
	/// <param name="fileSystem">File system the folders are removed from.</param>
	public DeletedItemFileCleaner(IFsmModeStatusService fsmModeStatusService,
		IEnvironmentPackageFolderResolver packageFolderResolver, IFileSystem fileSystem) {
		_fsmModeStatusService = fsmModeStatusService ?? throw new ArgumentNullException(nameof(fsmModeStatusService));
		_packageFolderResolver = packageFolderResolver ?? throw new ArgumentNullException(nameof(packageFolderResolver));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	/// <inheritdoc />
	public DeletedItemFileCleanupResult Clean(DeletedItemFileCleanupRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		IReadOnlyList<PackageItemFolders.Rule> rules = PackageItemFolders.GetRules(request.ItemType, request.ItemName);
		IReadOnlyList<string> expected = rules.Select(rule => rule.Describe()).ToList();
		if (string.IsNullOrWhiteSpace(request.EnvironmentName)) {
			return NotAttempted(DeletedItemFileCleanupStatus.FileSystemModeUnknown, expected,
				"the connection is not a registered clio environment, so its file system mode could not be read");
		}
		FsmModeStatusResult fsmStatus;
		try {
			fsmStatus = _fsmModeStatusService.GetStatus(request.EnvironmentName);
		}
		catch (Exception exception) {
			// Any probe failure (transport, authentication, an unexpected payload) means the same thing here:
			// the mode is unknown, so nothing is removed and the caller is told which folders to check.
			return NotAttempted(DeletedItemFileCleanupStatus.FileSystemModeUnknown, expected, exception.Message);
		}
		if (!string.Equals(fsmStatus?.Mode, FileSystemModeOn, StringComparison.OrdinalIgnoreCase)) {
			return NotAttempted(DeletedItemFileCleanupStatus.FileSystemModeOff, expected, null);
		}
		if (rules.Count == 0) {
			return NotAttempted(DeletedItemFileCleanupStatus.NotCleaned, expected,
				$"clio does not know where items of type {request.ItemType} named '{request.ItemName}' are stored");
		}
		EnvironmentPackageFolderResolution folder = _packageFolderResolver.Resolve(request.EnvironmentName,
			request.PackageName, request.EnvironmentPathOverride);
		if (!folder.IsResolved) {
			return NotAttempted(DeletedItemFileCleanupStatus.NotCleaned, expected, folder.Problem);
		}
		return RemoveFolders(folder.PackageFolderPath, rules, expected);
	}

	private static DeletedItemFileCleanupResult NotAttempted(DeletedItemFileCleanupStatus status,
		IReadOnlyList<string> expected, string problem) =>
		new(status, null, expected, [], [], problem);

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
			foreach (string folderPath in _fileSystem.GetDirectories(areaPath)) {
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

	private string TryRemove(string folderPath) {
		try {
			if (_fileSystem.GetDirectoryInfo(folderPath).LinkTarget is not null) {
				return "it is a symbolic link; clio does not remove link targets";
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

/// <summary>
/// Package-relative folders where Creatio keeps a workspace item in file system mode. Mirrors the platform's
/// own per-item layout (<c>PackageFileStorage</c>): <c>Schemas/&lt;name&gt;</c> plus
/// <c>Resources/&lt;name&gt;.&lt;manager&gt;</c> for a schema, <c>Data/&lt;name&gt;</c> for a data binding,
/// <c>SqlScripts/&lt;name&gt;</c> and <c>Assemblies/&lt;name&gt;</c>.
/// </summary>
internal static class PackageItemFolders {

	// Platform WorkspaceItemType values, as WorkspaceExplorerService.svc/GetWorkspaceItems reports them.
	internal const int SqlScriptType = 0;
	internal const int SchemaDataType = 1;
	internal const int AssemblyType = 2;
	internal const int EntitySchemaType = 3;
	private const int LastSchemaType = 12;
	private const int LocalizationSchemaType = 13;

	private const string SchemasArea = "Schemas";
	private const string ResourcesArea = "Resources";
	private const string DataArea = "Data";
	private const string SqlScriptsArea = "SqlScripts";
	private const string AssembliesArea = "Assemblies";

	/// <summary>Whether a name can be used as one folder name without leaving its parent folder.</summary>
	/// <param name="name">Candidate folder name.</param>
	/// <returns><c>true</c> when the name is a single, non-relative folder name.</returns>
	internal static bool IsSafeFolderName(string name) =>
		!string.IsNullOrWhiteSpace(name)
		&& name == name.Trim()
		&& name != "."
		&& name != ".."
		&& name.IndexOfAny(['/', '\\', ':']) < 0
		&& name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

	/// <summary>Returns the folder rules for an item; empty for an unknown type or an unsafe name.</summary>
	/// <param name="itemType">Platform <c>WorkspaceItemType</c> value.</param>
	/// <param name="itemName">Item name.</param>
	/// <returns>The rules, in the order the folders are reported.</returns>
	internal static IReadOnlyList<Rule> GetRules(int itemType, string itemName) {
		if (!IsSafeFolderName(itemName)) {
			return [];
		}
		return itemType switch {
			SqlScriptType => [new Rule(SqlScriptsArea, StripExtension(itemName, ".sql"), false)],
			SchemaDataType => [new Rule(DataArea, itemName, false)],
			AssemblyType => [new Rule(AssembliesArea, StripExtension(itemName, ".dll"), false)],
			>= EntitySchemaType and <= LastSchemaType => [
				new Rule(SchemasArea, itemName, false),
				new Rule(ResourcesArea, itemName, true)
			],
			LocalizationSchemaType => [new Rule(ResourcesArea, itemName, true)],
			_ => []
		};
	}

	private static string StripExtension(string name, string extension) =>
		name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
			? name[..^extension.Length]
			: name;

	/// <summary>One folder (or family of resource folders) under a package area.</summary>
	/// <param name="Area">Top-level package folder, for example <c>Schemas</c>.</param>
	/// <param name="FolderName">Folder name of the item inside the area.</param>
	/// <param name="IncludesManagerSuffix">
	/// Whether <c>&lt;name&gt;.&lt;manager&gt;</c> folders match too (schema resources), as well as a bare
	/// <c>&lt;name&gt;</c> folder written by older platform versions.
	/// </param>
	internal sealed record Rule(string Area, string FolderName, bool IncludesManagerSuffix) {

		/// <summary>Package-relative description used when the folders cannot be listed.</summary>
		/// <returns>For example <c>Schemas/UsrFoo/</c> or <c>Resources/UsrFoo.*/</c>.</returns>
		public string Describe() => IncludesManagerSuffix ? $"{Area}/{FolderName}.*/" : $"{Area}/{FolderName}/";

		/// <summary>Whether a child folder of <see cref="Area"/> belongs to the item.</summary>
		/// <param name="folderName">Child folder name.</param>
		/// <returns><c>true</c> when the folder holds the item's files.</returns>
		public bool Matches(string folderName) {
			if (string.Equals(folderName, FolderName, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
			if (!IncludesManagerSuffix || folderName.Length <= FolderName.Length + 1
				|| !folderName.StartsWith(FolderName + ".", StringComparison.OrdinalIgnoreCase)) {
				return false;
			}
			// Schema names never contain a dot, so the suffix is a single manager name such as Entity or ClientUnit.
			return folderName.IndexOf('.', FolderName.Length + 1) < 0;
		}
	}
}
