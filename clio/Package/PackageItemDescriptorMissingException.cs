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
	// message puts its (shortened) path on a later line that carries no [ERR] prefix (issue #1749).
	private static string BuildMessage(IReadOnlyCollection<PackageItemFolderWithoutDescriptor> folders) {
		string subject = folders.Count == 1
			? "This package folder has no descriptor.json"
			: $"These {folders.Count} package folders have no descriptor.json";
		string list = string.Join("; ", folders.Select(folder => $"{folder.FolderPath} {DescribeFiles(folder.Files)}"));
		return $"{subject}: {list}. Creatio rejects the whole installation when a folder under Schemas/ or Data/ "
			+ "has no descriptor.json (\"Invalid descriptor\"), so clio stopped before building the package archive. "
			+ "Such a folder is usually left behind when a schema or data binding is deleted: git removes the tracked "
			+ "files and keeps the ignored Localization or Resources files. Delete the folder, or restore its "
			+ "descriptor.json if the element is still needed.";
	}

	private static string DescribeFiles(IReadOnlyList<string> files) {
		string listed = string.Join(", ", files.Take(ListedFileCount));
		return files.Count switch {
			1 => $"(1 file: {listed})",
			<= ListedFileCount => $"({files.Count} files: {listed})",
			_ => $"({files.Count} files, e.g. {listed})"
		};
	}

	#endregion

}
