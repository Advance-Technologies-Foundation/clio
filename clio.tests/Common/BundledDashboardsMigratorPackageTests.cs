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
/// Guards the bundled <c>CrtDashboardsMigratorApp</c> archive: provenance pins that
/// <c>rebundle-bundled-package.ps1</c> rewrites from the archive it produced, plus the inventory the archive must
/// have. The reasoning behind the pin mechanism lives on <see cref="BundledProcessBuilderPackageTests"/> and in
/// <c>docs/agent-instructions/bundled-packages.md</c>; it is not repeated here.
/// </summary>
/// <remarks>
/// Unlike the process builder this package ships PREBUILT, like cliogate: the archive is the SDLC (Jenkins)
/// build of the release, carrying the package assembly compiled for both .NET Framework and .NET, so the target
/// installs it without compiling the package. Consequences for the pins: exactly two own assemblies are
/// expected rather than none; provenance is the SHA-256 of the SDLC build zip the archive was cut from rather
/// than a git commit; <c>Data/</c> is allowed because its bound rows register the migration page and its
/// permission and run nothing; and there is no compile-marker schema.
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

	/// <summary>The package's own assembly, once per runtime the platform can load it on.</summary>
	private static readonly string[] ExpectedAssemblies = [
		"Files/Bin/CrtDashboardsMigratorApp.dll",
		"Files/Bin/netstandard/CrtDashboardsMigratorApp.dll"
	];

	/// <summary>
	/// SHA-256 of the committed archive, written by <c>rebundle-bundled-package.ps1</c> from the archive it
	/// produced out of the SDLC build zip pinned in <see cref="ExpectedSourceBuildSha256"/>.
	/// </summary>
	private const string ExpectedArchiveSha256 =
		"0000000000000000000000000000000000000000000000000000000000000000";

	/// <summary>Version in the shipped descriptor; a test-side pin, no runtime consumer (see the ADR).</summary>
	private const string ExpectedArchiveVersion = "0.0.0.0";

	/// <summary>
	/// SHA-256 of the SDLC build zip the archive was cut from. The build's full version equals
	/// <see cref="ExpectedArchiveVersion"/>; the commit is recorded on the build's page in the SDLC app.
	/// </summary>
	private const string ExpectedSourceBuildSha256 =
		"0000000000000000000000000000000000000000000000000000000000000000";

	/// <summary>The descriptor stamp that makes the version bump take effect on the target (fact 2).</summary>
	private const string ExpectedDescriptorModifiedOnUtc = "/Date(0)/";

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
			because: "the archive is produced from another team's build and ships compiled code into "
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
	[Description("The descriptor inside the archive must carry the identity clio advertises, the pinned stamp, a plain four-part version readable through the production catalog, and no install script.")]
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
			because: "the package applies its column rights from an app-start listener instead; an install script "
				+ "would run before the target compiles a source package, and this archive must stay installable "
				+ "either way");
	}

	[Test]
	[Description("The archive carries the package's own assembly for both runtimes and no other binary, so the target loads it without compiling the package and no leaked build output rides along.")]
	public void BundledArchive_ShouldCarryExactlyTheTwoPackageAssemblies() {
		// Arrange
		IReadOnlyList<string> entries = ReadBundledArchiveEntryNames();

		// Act
		List<string> binaries = entries
			.Where(entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
				|| entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
			.ToList();

		// Assert
		binaries.Should().BeEquivalentTo(ExpectedAssemblies,
			because: "the platform resolves the package assembly as <packageName>.dll under Files/Bin (net472) or "
				+ "Files/Bin/netstandard (.NET); a missing one makes that runtime compile the package after all, an "
				+ "extra one is leaked payload, and a pdb means --skip-pdb was dropped");
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
			because: "the install command's verdict rests on this schema's Ping answering; without it the "
				+ "package installs and clio reports it as not serving");
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
	[Description("The source-build pin is a full SHA-256, so a reviewer can verify the SDLC zip the archive was cut from.")]
	public void ExpectedSourceBuildSha256_ShouldBeAFullHash() {
		// Arrange, Act & Assert
		ExpectedSourceBuildSha256.Should().MatchRegex("^[0-9A-F]{64}$",
			because: "a placeholder or abbreviated hash cannot be checked against the build on the share");
	}

	#endregion

}
