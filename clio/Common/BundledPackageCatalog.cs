using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Project.NuGet;

namespace Clio.Common;

/// <summary>
/// Answers what the running clio distribution actually carries for a bundled Creatio package.
/// </summary>
/// <remarks>
/// This is the ONLY place the shipped version of a bundled package is read, and it reads it from the
/// archive's own descriptor rather than from a constant. The reason is recorded in
/// <c>spec/adr/adr-bundled-package-version-source-of-truth.md</c> and is worth restating because it caused
/// a live divergence: the archive is a CONTENT file copied to the build output, not compiled in, so
/// <c>clio compress -d …</c> replaces it without recompiling anything. A constant in the assembly therefore
/// describes the archive that existed when the assembly was built, which need not be the archive that will
/// be installed — during this feature's development three build outputs held three different archives while
/// one constant claimed to describe them all.
/// <para>
/// It resolves from <see cref="IWorkingDirectoriesProvider.ExecutingDirectory"/> on purpose: that is the
/// path the install command resolves from, so this reports the bytes that will actually be shipped rather
/// than the ones in the repository.
/// </para>
/// </remarks>
public interface IBundledPackageCatalog {

	/// <summary>
	/// Determines whether the named package ships inside the clio distribution.
	/// </summary>
	/// <param name="packageName">Package name (case-insensitive).</param>
	/// <returns><c>true</c> when clio carries an archive for this package.</returns>
	/// <remarks>
	/// This is the predicate that decides whether a package is subject to the convergence rule — a package
	/// clio does not ship cannot be converged to anything, and nothing has to declare that anywhere.
	/// </remarks>
	bool IsBundled(string packageName);

	/// <summary>
	/// Retrieves the absolute path of the bundled archive for the named package.
	/// </summary>
	/// <param name="packageName">Package name (case-insensitive).</param>
	/// <returns>The absolute path the archive is expected at, whether or not the file exists.</returns>
	/// <exception cref="ArgumentException">
	/// Thrown when the package does not ship inside clio. Asking for the archive of an unbundled package is a
	/// programming error, not a runtime condition — call <see cref="IsBundled"/> first.
	/// </exception>
	string GetArchivePath(string packageName);

	/// <summary>
	/// Reads the version recorded in the bundled archive's descriptor.
	/// </summary>
	/// <param name="packageName">Package name (case-insensitive).</param>
	/// <param name="version">The version the distribution carries, when readable.</param>
	/// <param name="diagnosis">
	/// A user-facing explanation of why the version could not be read, when it could not. Never a bare
	/// exception message: a distribution that cannot describe itself is broken, and the reader needs to be
	/// told that rather than shown a stack.
	/// </param>
	/// <returns><c>true</c> when the version was read.</returns>
	/// <exception cref="ArgumentException">
	/// Thrown when the package does not ship inside clio — see <see cref="GetArchivePath"/>.
	/// </exception>
	bool TryGetVersion(string packageName, out PackageVersion version, out string diagnosis);

}

/// <inheritdoc cref="IBundledPackageCatalog"/>
public class BundledPackageCatalog : IBundledPackageCatalog {

	#region Constants: Private

	// Path of the descriptor inside a Creatio package archive, relative to the package root.
	private const string DescriptorEntryPath = "descriptor.json";

	#endregion

	#region Fields: Private

	// Package name -> the archive's location relative to the executing directory, as
	// (containing folder, file name). Only packages clio actually ships belong here.
	//
	// cliogate is deliberately absent. It ships an archive too, but its version story is a separate
	// mechanism (a prebuilt assembly, version.txt, and Program.CheckApiVersion) that this catalog does not
	// model; folding it in is noted as out of scope in the ADR so the asymmetry stays deliberate.
	private static readonly IReadOnlyDictionary<string, (string Folder, string FileName)> BundledArchives =
		new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase) {
			[BundledPackages.ProcessBuilderPackageName] =
				(BundledPackages.ProcessBuilderPackageName, BundledPackages.ProcessBuilderArchiveFileName)
		};

	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _fileSystem;
	private readonly ICompressionUtilities _compressionUtilities;

	// Successful reads only. In normal operation the archive cannot change under a running process, so a
	// version once read stays true; a FAILURE is not cached, because a distribution being repaired
	// mid-process (or an archive still being written when first asked) must not stay broken for the life of
	// an MCP server. Concurrent because the MCP server serves tool calls in parallel.
	//
	// The one way to break the assumption is to rebundle while a long-lived `clio mcp` is running: the
	// rebuild replaces the archive in the same output directory the server resolves from, and that server
	// keeps serving the version it read first. The rebundle runbook says to restart it for this reason.
	private readonly ConcurrentDictionary<string, PackageVersion> _versionCache =
		new(StringComparer.OrdinalIgnoreCase);

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="BundledPackageCatalog"/> class.
	/// </summary>
	/// <param name="workingDirectoriesProvider">Provider used to locate the executing directory.</param>
	/// <param name="fileSystem">File system used to check the archive is present.</param>
	/// <param name="compressionUtilities">Reader used to pull the descriptor out of the archive.</param>
	public BundledPackageCatalog(
		IWorkingDirectoriesProvider workingDirectoriesProvider,
		IFileSystem fileSystem,
		ICompressionUtilities compressionUtilities) {
		workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		compressionUtilities.CheckArgumentNull(nameof(compressionUtilities));
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
		_compressionUtilities = compressionUtilities;
	}

	#endregion

	#region Methods: Private

	// System.Text.Json rejects a UTF-8 BOM outright, and clio's own packer preserves whatever the source
	// file had - the shipped descriptor does carry one.
	private static ReadOnlyMemory<byte> StripBom(byte[] content) =>
		content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF
			? content.AsMemory(3)
			: content.AsMemory();

	private static bool TryReadVersionFromDescriptor(byte[] descriptor, out PackageVersion version) {
		version = null;
		using JsonDocument document = JsonDocument.Parse(StripBom(descriptor));
		// Every ValueKind guard is load-bearing: TryGetProperty THROWS InvalidOperationException on a
		// non-object rather than returning false, and that is not a JsonException, so it would escape this
		// class entirely — turning a merely wrong-shaped descriptor (`[…]`, `{"Descriptor": null}`) into an
		// unhandled throw out of `clio info` and, worse, into a hard refusal of every gated command through
		// the convergence path. Which is the exact opposite of what an unreadable archive is supposed to do.
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty("Descriptor", out JsonElement descriptorElement)
			|| descriptorElement.ValueKind != JsonValueKind.Object
			|| !descriptorElement.TryGetProperty("PackageVersion", out JsonElement versionElement)
			|| versionElement.ValueKind != JsonValueKind.String) {
			return false;
		}
		return PackageVersion.TryParseVersion(versionElement.GetString(), out version);
	}

	// Exception text reaches a single-line log sink and an MCP execution log, so collapse any newlines the
	// underlying exception carried rather than letting one message become several lines.
	private static string Flatten(string message) =>
		message?.Replace("\r", " ").Replace("\n", " ");

	#endregion

	#region Methods: Public

	public bool IsBundled(string packageName) =>
		!string.IsNullOrWhiteSpace(packageName) && BundledArchives.ContainsKey(packageName);

	public string GetArchivePath(string packageName) {
		if (string.IsNullOrWhiteSpace(packageName)
			|| !BundledArchives.TryGetValue(packageName, out (string Folder, string FileName) archive)) {
			throw new ArgumentException(
				$"Package '{packageName}' does not ship inside the clio distribution.", nameof(packageName));
		}
		return Path.Combine(
			_workingDirectoriesProvider.ExecutingDirectory, archive.Folder, archive.FileName);
	}

	public bool TryGetVersion(string packageName, out PackageVersion version, out string diagnosis) {
		string archivePath = GetArchivePath(packageName);
		diagnosis = null;
		if (_versionCache.TryGetValue(packageName, out version)) {
			return true;
		}
		if (!_fileSystem.ExistsFile(archivePath)) {
			diagnosis =
				$"This clio installation does not carry the bundled {packageName} archive — it was expected at "
				+ $"'{archivePath}'. Reinstall or update clio itself.";
			return false;
		}
		byte[] descriptor;
		bool found;
		try {
			found = _compressionUtilities.TryReadFileFromGZip(archivePath, DescriptorEntryPath, out descriptor);
		} catch (Exception e) {
			// Anything the reader can throw — a truncated gzip member, an unreadable file — means the same
			// thing to the caller, so it is reported as one condition with the cause appended rather than as
			// a stack. The archive being unreadable is a broken distribution, never a user error.
			diagnosis =
				$"The bundled {packageName} archive at '{archivePath}' could not be read ({Flatten(e.Message)}). "
				+ "Reinstall or update clio itself.";
			return false;
		}
		if (!found) {
			// Distinct from the catch above on purpose: the reader throws for a CORRUPT archive and answers
			// false only for a cleanly-read archive that genuinely has no such entry. Collapsing the two would
			// tell an operator their archive lacks a descriptor when in fact it is truncated.
			diagnosis =
				$"The bundled {packageName} archive at '{archivePath}' contains no {DescriptorEntryPath}, so "
				+ "the version it carries cannot be determined. Reinstall or update clio itself.";
			return false;
		}
		try {
			if (!TryReadVersionFromDescriptor(descriptor, out version)) {
				diagnosis =
					$"The bundled {packageName} archive at '{archivePath}' has a {DescriptorEntryPath} without a "
					+ "readable Descriptor.PackageVersion. Reinstall or update clio itself.";
				return false;
			}
		} catch (JsonException e) {
			diagnosis =
				$"The bundled {packageName} archive at '{archivePath}' has a malformed {DescriptorEntryPath} "
				+ $"({Flatten(e.Message)}). Reinstall or update clio itself.";
			return false;
		}
		_versionCache[packageName] = version;
		return true;
	}

	#endregion

}
