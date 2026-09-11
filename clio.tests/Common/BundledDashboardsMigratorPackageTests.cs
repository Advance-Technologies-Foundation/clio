using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Clio.Common;
using Clio.Project.NuGet;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Guards the bundled <c>CrtDashboardsMigratorApp</c> archive the same way
/// <see cref="BundledProcessBuilderPackageTests"/> guards the process builder: provenance pins that
/// <c>rebundle-bundled-package.ps1</c> rewrites from the archive it produced, plus the inventory a
/// source-only package must have. The reasoning behind each pin lives on the process-builder fixture and in
/// <c>docs/agent-instructions/bundled-packages.md</c>; it is not repeated here.
/// </summary>
/// <remarks>
/// Two deliberate differences from the process-builder fixture. <c>Data/</c> IS allowed: the package's bound
/// data (SysAdminOperation, SysModule, SysDetail, SysImage rows) is what registers the migration page and its
/// permission, and none of it executes. And there is no compile-marker schema and no <c>Files/Libs</c>: the
/// package's own Source Code schemas put it into the target's configuration build, and it references nothing
/// outside the platform core.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class BundledDashboardsMigratorPackageTests {

	#region Constants: Private

	/// <summary>The package's stable identity. Never change it — see fact 1 in the runbook.</summary>
	private const string ExpectedPackageUId = "a1576367-2b6f-4060-a4e6-0dffc16fc579";

	/// <summary>The Source Code schema whose ungated Ping the install command probes.</summary>
	private const string PingSchemaName = "DashboardsMigratorService";

	private static readonly string[] AllowedTopLevelEntries = [
		"descriptor.json", "Files", "Schemas", "Resources", "Data"
	];

	/// <summary>
	/// SHA-256 of the committed archive, written by <c>rebundle-bundled-package.ps1</c> from the archive it
	/// produced out of the <c>crt-dashboards-migrator-app</c> repository at <see cref="ExpectedProducingCommit"/>.
	/// </summary>
	private const string ExpectedArchiveSha256 =
		"38BC11C3AF3899E05D1BA91857DB6B709768A87945D5A747F446E062D14250A0";

	/// <summary>Version in the shipped descriptor; a test-side pin, no runtime consumer (see the ADR).</summary>
	private const string ExpectedArchiveVersion = "1.1.4.2";

	/// <summary>HEAD of the package repository when the bytes were cut, before the restamp.</summary>
	private const string ExpectedProducingCommit = "563fd9e46d6bebe905535bc429d44d8bc121e749";

	/// <summary>The descriptor stamp that makes the version bump take effect on the target (fact 2).</summary>
	private const string ExpectedDescriptorModifiedOnUtc = "/Date(1789112495000)/";

	#endregion

	#region Properties: Private

	private static string BundledArchivePath => Path.Combine(
		AppContext.BaseDirectory,
		BundledPackages.DashboardsMigratorPackageName,
		BundledPackages.DashboardsMigratorArchiveFileName);

	#endregion

	#region Methods: Private

	private static IReadOnlyList<string> ReadBundledArchiveEntryNames() {
		IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		ICompressionUtilities compressionUtilities = new CompressionUtilities(fileSystem, new ZipFileWrapper());
		return compressionUtilities.ListGZipEntryNames(BundledArchivePath);
	}

	private static string ReadBundledArchiveAsText() {
		using FileStream compressed = File.OpenRead(BundledArchivePath);
		using GZipStream decompressor = new(compressed, CompressionMode.Decompress);
		using MemoryStream buffer = new();
		decompressor.CopyTo(buffer);
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	private static PackageVersion ReadBundledVersionThroughTheCatalog() {
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.ExecutingDirectory.Returns(AppContext.BaseDirectory);
		IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		IBundledPackageCatalog catalog = new BundledPackageCatalog(
			workingDirectoriesProvider, fileSystem, new CompressionUtilities(fileSystem, new ZipFileWrapper()));
		bool read = catalog.TryGetVersion(
			BundledPackages.DashboardsMigratorPackageName, out PackageVersion version, out string diagnosis);
		read.Should().BeTrue(
			because: $"clio info and the install command both read the version through this catalog. Diagnosis was '{diagnosis}'");
		return version;
	}

	#endregion

	#region Methods: Public

	[Test]
	[Description("The bundled archive's SHA-256 must equal the pinned value, so replacing the committed binary cannot pass review as an opaque byte-count change.")]
	public void BundledArchive_ShouldMatchThePinnedHash() {
		// Arrange
		using FileStream archive = File.OpenRead(BundledArchivePath);

		// Act
		string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive));

		// Assert
		actual.Should().Be(ExpectedArchiveSha256,
			because: "the archive is produced from another repository and ships executable source into "
				+ "customers' Creatio instances; a change to it must be a reviewable line, not a byte count");
	}

	[Test]
	[Description("The archive must be present in the build output at the path install-dashboards-migrator resolves.")]
	public void BundledArchive_ShouldExistInBuildOutput_AtThePathTheInstallCommandResolves() {
		// Arrange & Act
		bool exists = File.Exists(BundledArchivePath);

		// Assert
		exists.Should().BeTrue(
			because: $"the install command resolves <ExecutingDirectory>/{BundledPackages.DashboardsMigratorPackageName}/"
				+ $"{BundledPackages.DashboardsMigratorArchiveFileName}; looked in '{BundledArchivePath}'");
	}

	[Test]
	[Description("The descriptor inside the archive must carry the identity clio advertises, the pinned stamp, and a plain four-part version readable through the production catalog.")]
	public void BundledArchive_ShouldCarryADescriptorMatchingBundledPackages() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act & Assert
		archive.Should().Contain($"\"Name\": \"{BundledPackages.DashboardsMigratorPackageName}\"",
			because: "the install command ships this archive under this name and the verifier maps the name to the Ping route");
		archive.Should().Contain($"\"UId\": \"{ExpectedPackageUId}\"",
			because: "Creatio identifies a package by UId; a changed UId installs a SECOND package instead of upgrading");
		archive.Should().Contain($"\"ModifiedOnUtc\": \"{ExpectedDescriptorModifiedOnUtc}\"",
			because: "Creatio rewrites the SysPackage row only when this field changes, never because PackageVersion did");
		ExpectedDescriptorModifiedOnUtc.Should().EndWith("000)/",
			because: "whole seconds are the provenance oracle: milliseconds mean the descriptor was written by hand with a wrong tool");
		ReadBundledVersionThroughTheCatalog().ToString().Should().Be(ExpectedArchiveVersion,
			because: "the shipped version is what clio info reports and what the downgrade check compares; pinning puts a version move on a reviewable line");
		ExpectedArchiveVersion.Should().MatchRegex("^[0-9]+(\\.[0-9]+){3}$",
			because: "four parts and no suffix: the install command refuses a suffixed distribution outright");
		archive.Should().NotContain("\"InstallScripts\"",
			because: "install scripts run BEFORE the target compiles a source-only package, so a script living in the "
				+ "package's own assembly fails every first install (measured: 'Path to assembly "
				+ "crtdashboardsmigratorapp.dll not found'); the package applies its rights at app start instead");
	}

	[Test]
	[Description("The archive carries NO .dll at all: the package has no compile references outside the platform core, so any dll is a leaked build output that would answer Ping from stale code after a failed target-side build.")]
	public void BundledArchive_ShouldCarryNoAssembly() {
		// Arrange
		IReadOnlyList<string> entries = ReadBundledArchiveEntryNames();

		// Act
		List<string> binaries = entries
			.Where(entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
				|| entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
				|| entry.StartsWith("Files/Bin/", StringComparison.OrdinalIgnoreCase))
			.ToList();

		// Assert
		binaries.Should().BeEmpty(
			because: "clio compress copies Files/ wholesale and clioignore does not filter Files/Bin, so the only "
				+ "thing keeping this archive source-only is the delete step before packing — this pin is what "
				+ "notices when it was skipped");
	}

	[Test]
	[Description("The archive contains only the allowlisted top-level entries — Data/ included, SqlScripts/ not — and ships the Ping schema the install verdict rests on.")]
	public void BundledArchive_ShouldContainOnlyTheAllowedTopLevelEntries_AndThePingSchema() {
		// Arrange
		IReadOnlyList<string> entries = ReadBundledArchiveEntryNames();

		// Act
		List<string> presentTopLevel = entries
			.Select(entry => entry.Split('/')[0])
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		List<string> schemaFolders = entries
			.Where(entry => entry.StartsWith("Schemas/", StringComparison.OrdinalIgnoreCase))
			.Select(entry => entry.Split('/')[1])
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		// Assert
		presentTopLevel.Should().BeEquivalentTo(AllowedTopLevelEntries,
			because: "SqlScripts/ would execute on the target at install time, and every allowlisted name must "
				+ "still be shipped or the list has silently widened. Data/ is allowed for this package: its bound "
				+ "rows register the migration page and its permission and run nothing");
		schemaFolders.Should().Contain(PingSchemaName,
			because: "the install command's whole verdict rests on this schema's Ping answering; without it the "
				+ "package installs and clio reports 'the environment did not compile the package' about an "
				+ "environment that compiled everything else");
		entries.Should().NotContain(entry => entry.StartsWith("Autogenerated/", StringComparison.OrdinalIgnoreCase),
			because: "clio compress copies an allowlist of package folders that excludes Autogenerated/; the "
				+ "target regenerates those stubs itself");
	}

	[Test]
	[Description("The shipped Ping source keeps the envelope clio parses: a Wrapped body so the root is PingResult, and a DataMember named success.")]
	public void BundledArchive_ShouldCarryThePingSourceInTheEnvelopeClioParses() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act & Assert
		archive.Should().Contain($"class {PingSchemaName}",
			because: "the route is /rest/<ClassName>/Ping, so the class name IS the route the verifier probes");
		archive.Should().Contain("PingResponse Ping()",
			because: "the operation name is the other half of the route");
		archive.Should().Contain("BodyStyle = WebMessageBodyStyle.Wrapped",
			because: "the wrapper name clio looks for (PingResult) is a function of this setting; Bare removes the envelope");
		archive.Should().Contain("[DataMember(Name = \"success\")]",
			because: "clio parses PingResult.success; a renamed member makes a healthy install read as failed");
	}

	[Test]
	[Description("The producing-commit pin is a full 40-hex commit id, so a reviewer can `git show` it in the package repository.")]
	public void ExpectedProducingCommit_ShouldBeAFullCommitId() {
		// Arrange, Act & Assert
		ExpectedProducingCommit.Should().MatchRegex("^[0-9a-f]{40}$",
			because: "an abbreviated or placeholder id cannot be resolved unambiguously later");
	}

	#endregion

}
