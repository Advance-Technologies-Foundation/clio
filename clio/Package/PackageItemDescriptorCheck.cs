using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Package;

/// <summary>
/// Element folder of a package archive that Creatio cannot load because it has no <c>descriptor.json</c>.
/// </summary>
/// <param name="FolderPath">
/// Folder path. <see cref="PackageItemDescriptorCheck"/> returns it relative to the package root with <c>/</c>
/// separators (<c>Data/Lookup_Status</c>); <see cref="PackageItemDescriptorMissingException"/> carries the full
/// local path, so the message points at the folder to fix.
/// </param>
/// <param name="Files">Paths, relative to the folder, of the files the folder would put into the archive.</param>
public sealed record PackageItemFolderWithoutDescriptor(string FolderPath, IReadOnlyList<string> Files) {

	private const string LocalizationFolderPrefix = "Localization/";

	// Files the operating system drops into any folder a user opened; git ignores them, so they survive a deleted
	// binding next to its Localization files and must not turn a leftover into a "damaged element".
	private static readonly string[] OperatingSystemFileNames = [".DS_Store", "Thumbs.db", "desktop.ini"];

	/// <summary>
	/// Gets whether every file of the folder lies under <c>Localization/</c>, apart from operating system metadata
	/// such as <c>.DS_Store</c>: the shape a deleted data binding leaves behind when git removes its tracked files
	/// and keeps the ignored per-culture files. Any other file means the element itself is still there and only its
	/// <c>descriptor.json</c> is missing.
	/// </summary>
	public bool HoldsOnlyLocalization {
		get {
			List<string> elementFiles = Files.Where(file => !IsOperatingSystemFile(file)).ToList();
			return elementFiles.Count > 0
				&& elementFiles.All(file => file.StartsWith(LocalizationFolderPrefix, StringComparison.OrdinalIgnoreCase));
		}
	}

	/// <summary>
	/// Gets whether the folder holds nothing but operating system metadata such as <c>.DS_Store</c>: a leftover of
	/// an element whose own files are gone, so there is no descriptor to restore and the folder can be deleted.
	/// </summary>
	public bool HoldsOnlyOperatingSystemFiles => Files.All(IsOperatingSystemFile);

	private static bool IsOperatingSystemFile(string file) {
		string fileName = file[(file.LastIndexOf('/') + 1)..];
		return OperatingSystemFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
	}

}

/// <summary>
/// Finds the element folders of a package archive that Creatio rejects: a folder directly under
/// <c>Schemas/</c> or <c>Data/</c> that puts files into the archive but has no <c>descriptor.json</c>.
/// </summary>
/// <remarks>
/// Creatio loads an installed archive with <c>PackageFileStorage</c>, which reads <c>descriptor.json</c> from
/// every directory under <c>Schemas/</c>, <c>Data/</c>, <c>Assemblies/</c> and <c>SqlScripts/</c>. One directory
/// without it fails the whole installation with "Invalid descriptor", and the message names only the last segment
/// of the folder path. The check works on the list of files that is about to be packed, not on the folder on
/// disk: an empty directory, or one whose files <c>.clioignore</c> excludes, never reaches the archive, so the
/// platform never sees it either.
/// <para>
/// Only <c>Schemas/</c> and <c>Data/</c> are checked on purpose. <c>Assemblies/</c> and <c>SqlScripts/</c> are
/// left out until it is shown that every real package layout keeps a descriptor there, so the check never refuses
/// a package that installs today; <c>Files/</c> and <c>Resources/</c> are not read with descriptors at all.
/// </para>
/// </remarks>
internal static class PackageItemDescriptorCheck {

	#region Constants: Private

	private static readonly string[] CheckedFolderNames = ["Schemas", "Data"];
	private static readonly char[] PathSeparators = ['/', '\\'];

	#endregion

	#region Methods: Public

	/// <summary>
	/// Returns every element folder under <c>Schemas/</c> or <c>Data/</c> that contributes files but no
	/// <c>descriptor.json</c>.
	/// </summary>
	/// <param name="packageRelativeFilePaths">
	/// Paths of the files that go into the archive, relative to the package root; either separator is accepted.
	/// </param>
	/// <returns>The offending folders ordered by path; an empty list when the package is loadable.</returns>
	public static IReadOnlyList<PackageItemFolderWithoutDescriptor> FindFoldersWithoutDescriptor(
		IEnumerable<string> packageRelativeFilePaths) {
		ArgumentNullException.ThrowIfNull(packageRelativeFilePaths);
		Dictionary<string, SortedSet<string>> filesByFolder = new(StringComparer.Ordinal);
		HashSet<string> foldersWithDescriptor = new(StringComparer.Ordinal);
		foreach (string filePath in packageRelativeFilePaths) {
			string[] segments = (filePath ?? string.Empty).Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
			// A file directly under Schemas/ or Data/ (the template's placeholder.txt) is not an element folder;
			// the platform enumerates directories only.
			if (segments.Length < 3 || !CheckedFolderNames.Contains(segments[0], StringComparer.Ordinal)) {
				continue;
			}
			string folder = $"{segments[0]}/{segments[1]}";
			if (!filesByFolder.TryGetValue(folder, out SortedSet<string> files)) {
				files = new SortedSet<string>(StringComparer.Ordinal);
				filesByFolder.Add(folder, files);
			}
			// A set: the clioignore filter can hand the same file in twice, and the message must not double it.
			files.Add(string.Join('/', segments, 2, segments.Length - 2));
			// Case-insensitive on purpose: a check that runs before the install must never refuse an archive the
			// platform can load, and a Windows host reads Descriptor.json as descriptor.json.
			if (segments.Length == 3
				&& string.Equals(segments[2], CreatioPackage.DescriptorName, StringComparison.OrdinalIgnoreCase)) {
				foldersWithDescriptor.Add(folder);
			}
		}
		return filesByFolder
			.Where(pair => !foldersWithDescriptor.Contains(pair.Key))
			.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => new PackageItemFolderWithoutDescriptor(pair.Key, pair.Value.ToList()))
			.ToList();
	}

	#endregion

}
