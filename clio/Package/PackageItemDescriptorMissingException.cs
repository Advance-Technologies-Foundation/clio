using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Package;

/// <summary>
/// Raised before a package archive is built when a folder under <c>Schemas/</c> or <c>Data/</c> has no
/// <c>descriptor.json</c>, which Creatio would reject with an "Invalid descriptor" error naming only the folder's
/// last path segment.
/// </summary>
/// <remarks>
/// The archive is refused instead of built without the folder. Dropping it would install the package without that
/// schema or data binding, and an installation can remove package elements its archive no longer contains (see
/// issue #1612), so a folder whose <c>descriptor.json</c> was lost by accident could delete the element from the
/// environment.
/// </remarks>
public sealed class PackageItemDescriptorMissingException : Exception {

	#region Constants: Private

	private const int ListedFileCount = 3;
	private const int ListedFolderCount = 20;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="PackageItemDescriptorMissingException"/> class.
	/// </summary>
	/// <param name="folders">The offending folders, each with its full local path.</param>
	public PackageItemDescriptorMissingException(IEnumerable<PackageItemFolderWithoutDescriptor> folders)
		: this(folders?.ToList() ?? throw new ArgumentNullException(nameof(folders))) { }

	#endregion

	#region Constructors: Private

	private PackageItemDescriptorMissingException(List<PackageItemFolderWithoutDescriptor> folders)
		: base(BuildMessage(folders)) {
		Folders = folders;
	}

	#endregion

	#region Properties: Public

	/// <summary>
	/// Gets the folders that have no <c>descriptor.json</c>, each with its full local path.
	/// </summary>
	public IReadOnlyList<PackageItemFolderWithoutDescriptor> Folders { get; }

	#endregion

	#region Methods: Private

	// One line, folder paths first: a reader that keeps only the [ERR] line still gets them. The platform's own
	// message puts its (shortened) path on a later line that carries no [ERR] prefix (issue #1749). The list is
	// capped so a large branch switch does not turn one log entry into hundreds of paths; Folders keeps them all.
	private static string BuildMessage(IReadOnlyCollection<PackageItemFolderWithoutDescriptor> folders) {
		string subject = folders.Count == 1
			? "This package folder has no descriptor.json"
			: $"These {folders.Count} package folders have no descriptor.json";
		string list = string.Join("; ", folders.Take(ListedFolderCount).Select(DescribeFolder));
		string more = folders.Count > ListedFolderCount
			? $"; and {folders.Count - ListedFolderCount} more"
			: string.Empty;
		return $"{subject}: {list}{more}. Creatio rejects the whole installation when a folder under Schemas/ or "
			+ "Data/ has no descriptor.json (\"Invalid descriptor\"), so clio stopped before building the package "
			+ "archive. A folder that holds only Localization files is what git leaves behind when a data binding is "
			+ "deleted: delete it. A folder that still holds other files is an element that lost its descriptor.json: "
			+ "restore the descriptor, for example from git, because deleting the folder can remove the element from "
			+ "the environment on the next install.";
	}

	// Each folder carries its own verdict, so a reader does not have to apply the rule above to the file list.
	private static string DescribeFolder(PackageItemFolderWithoutDescriptor folder) {
		return $"{folder.FolderPath} ({DescribeFiles(folder.Files)}; {DescribeAdvice(folder)})";
	}

	private static string DescribeAdvice(PackageItemFolderWithoutDescriptor folder) {
		if (folder.HoldsOnlyOperatingSystemFiles) {
			return "only operating system files, delete the folder";
		}
		if (folder.HoldsOnlyLocalization) {
			return "only Localization files, delete the folder";
		}
		return "element files, restore descriptor.json";
	}

	private static string DescribeFiles(IReadOnlyList<string> files) {
		string listed = string.Join(", ", files.Take(ListedFileCount));
		return files.Count switch {
			1 => $"1 file: {listed}",
			<= ListedFileCount => $"{files.Count} files: {listed}",
			_ => $"{files.Count} files, e.g. {listed}"
		};
	}

	#endregion

}
