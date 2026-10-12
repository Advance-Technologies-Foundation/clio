using System;
using System.Collections.Generic;
using System.IO;

namespace Clio.Package;

/// <summary>
/// Package-relative folders where Creatio keeps a workspace item in file system mode. Mirrors the platform's
/// own per-item layout (<c>PackageFileStorage</c>): <c>Schemas/&lt;name&gt;</c> plus
/// <c>Resources/&lt;name&gt;.&lt;manager&gt;</c> for a schema, <c>Data/&lt;name&gt;</c> for a data binding,
/// <c>SqlScripts/&lt;name&gt;</c> and <c>Assemblies/&lt;name&gt;</c>.
/// </summary>
internal static class PackageItemFolders {

	// WorkspaceExplorerItemType values, as WorkspaceExplorerService.svc/GetWorkspaceItems reports them.
	internal const int SqlScriptType = 0;
	internal const int SchemaDataType = 1;
	internal const int AssemblyType = 2;
	internal const int EntitySchemaType = 3;
	private const int LastSchemaType = 12;
	internal const int LocalizationSchemaType = 13;

	internal const string SchemasArea = "Schemas";
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
	/// <param name="itemType"><c>WorkspaceExplorerItemType</c> value.</param>
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
