using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Guards the process-builder package archive that ships inside the clio distribution.
/// </summary>
/// <remarks>
/// These are consistency checks over a committed binary artifact, not unit tests of a class. They exist
/// because every failure mode they cover is otherwise SILENT: the package installs, the name-based
/// <c>[RequiresPackage]</c> gate reports it present, and only a <c>/rest/ProcessDesignService/*</c> call
/// reveals that nothing works — on a customer's environment, long after release.
/// <para>
/// They also carry the only REVIEWABILITY this artifact has. Unlike <c>cliogate</c>, whose C# lives in
/// this repository and whose archive is regenerable from it, this archive is hand-produced from a
/// different repository — so a changed <c>.gz</c> renders in a diff as nothing but a byte count. The
/// SHA-256 pin below turns any change to it into a deliberate, visible test edit, and the guard-gate
/// check pins the one property that decides whether the privileged service it installs is safe.
/// </para>
/// <para>
/// Kept in the <c>Unit</c> lane despite reading from disk (which <c>project-context.md</c> assigns to
/// <c>Integration</c>). Deliberate: the pre-commit gate is <c>Category=Unit&amp;Module=X</c>, and a guard
/// that only runs in the integration lane would not guard the commit that breaks it. The I/O is a file
/// this test project's own build output always carries, not an external dependency.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class BundledProcessBuilderPackageTests {

	#region Constants: Private

	/// <summary>
	/// The package's stable identity. Creatio matches a package by UId, so this value must NEVER change:
	/// a new UId makes the platform treat the archive as a different package, which would leave the old
	/// one installed and load two assemblies declaring <c>ProcessDesignService</c> — and
	/// <c>CustomServicesParser</c> silently keeps whichever it enumerates first.
	/// </summary>
	private const string ExpectedPackageUId = "f100e6d2-3cd0-a1d8-fbc0-41fce76a538d";

	/// <summary>
	/// The Source Code schema whose sole purpose is to put the package into the target environment's
	/// configuration build. See the schema's own remarks in the process-builder repository.
	/// </summary>
	private const string CompileMarkerSchemaName = "CrtProcessBuilderCompileMarker";

	/// <summary>
	/// SHA-256 of the committed archive. Produced by hand from the <c>ProcessBuilder</c> repository
	/// (<c>packages/CrtProcessBuilder</c> at commit <c>58dc0ea</c>, branch
	/// <c>feature/ENG-94385-rename-crt-process-builder</c>) following that repository's
	/// <c>docs/bundling-into-clio.md</c>; there is no build step in the release path that could regenerate it
	/// here.
	/// </summary>
	/// <remarks>
	/// This is the change-detection pin, not a security control: it cannot tell a good archive from a bad
	/// one. What it does is make replacing the archive impossible to do QUIETLY — a <c>.gz</c> swap otherwise
	/// shows up in review as nothing but a changed byte count, and passes every other check here, because the
	/// rest are substring probes that a tampered archive can satisfy while adding whatever it likes. Update
	/// this constant in the SAME commit that replaces the archive, and say in the message which commit of the
	/// producing repository the bytes came from.
	/// </remarks>
	private const string ExpectedArchiveSha256 =
		"8BA3B0A3FDEBB81AE6707AED0B6F7AE2B3F7FFEDAED69FB8E238E7BB201D645F";

	/// <summary>
	/// The authorization gate inside the shipped package. See
	/// <c>BundledArchive_ShouldCarryTheAuthorizationGateOnTheServiceHandlers</c> for why clio pins it.
	/// </summary>
	private const string AuthorizationGateMethodName = "EnsureCanManageProcessDesign";

	/// <summary>
	/// Number of handler call sites that must invoke <see cref="AuthorizationGateMethodName"/>. A FLOOR, not
	/// an exact count: adding a handler must not fail this test, but removing a gate from an existing one
	/// must.
	/// </summary>
	private const int MinimumAuthorizationGateCallSites = 3;

	#endregion

	#region Properties: Private

	private static string BundledArchivePath => Path.Combine(
		AppContext.BaseDirectory,
		BundledPackages.ProcessBuilderPackageName,
		BundledPackages.ProcessBuilderArchiveFileName);

	#endregion

	#region Methods: Private

	/// <summary>
	/// Decompresses the archive and returns it as text. The stream is clio's own package format and
	/// carries binary members, so only ASCII/UTF-8 content (the JSON descriptors) is meaningfully
	/// searchable here — which is all these checks need.
	/// </summary>
	private static string ReadBundledArchiveAsText() {
		using FileStream compressed = File.OpenRead(BundledArchivePath);
		using GZipStream decompressor = new(compressed, CompressionMode.Decompress);
		using MemoryStream buffer = new();
		decompressor.CopyTo(buffer);
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>
	/// Counts non-overlapping occurrences of <paramref name="value"/> in <paramref name="text"/>.
	/// </summary>
	private static int CountOccurrences(string text, string value) {
		int count = 0;
		for (int index = text.IndexOf(value, StringComparison.Ordinal);
			index >= 0;
			index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) {
			count++;
		}
		return count;
	}

	#endregion

	#region Methods: Public

	[Test]
	[Description("The shipped project must gate its InternalsVisibleTo attributes on a property that only local builds define, because the platform does not strip them and every customer environment would otherwise compile this package with friend assemblies.")]
	public void BundledArchive_ShouldNotGrantFriendAccessOnTheCustomerBuild() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act
		int unconditionalVisibilityGroups = CountOccurrences(archive,
			"<ItemGroup Label=\"Add visibility for test project\">");

		// Assert
		unconditionalVisibilityGroups.Should().Be(0,
			because: "an UNCONDITIONED visibility group ships the friend assemblies into the assembly the "
				+ "TARGET compiles. Established on a stand rather than assumed: the platform rewrites the "
				+ "package csproj on install but does NOT drop these entries, and the DLL the stand produced "
				+ "contained both friend names. Creatio compiles configuration packages into separate UNSIGNED "
				+ "assemblies, where InternalsVisibleTo matches on simple name, and 'DynamicProxyGenAssembly2' "
				+ "has no dot — i.e. it is a usable Creatio package name");
		archive.Should().Contain("CrtProcessBuilderIncludeTestVisibility",
			because: "the attributes must survive for LOCAL builds (the package's own tests reach internals), "
				+ "so the fix is a condition on a property defined outside the package directory — not "
				+ "deleting them. Losing the property reference would mean they were deleted instead, which "
				+ "breaks the package repository's test project");
	}

	[Test]
	[Description("The bundled archive's SHA-256 must equal the pinned value, so replacing the committed binary cannot pass review as an opaque byte-count change.")]
	public void BundledArchive_ShouldMatchThePinnedHash() {
		// Arrange
		using FileStream archive = File.OpenRead(BundledArchivePath);

		// Act
		string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive));

		// Assert
		actual.Should().Be(ExpectedArchiveSha256,
			because: "the archive is hand-produced from another repository and ships executable source into "
				+ "customers' Creatio instances, so a change to it must be a deliberate, reviewable edit "
				+ "rather than a diff line reading 'Bin N -> M bytes'. If this failed because you replaced "
				+ "the archive on purpose, update ExpectedArchiveSha256 in the same commit and record where "
				+ "the new bytes came from");
	}

	[Test]
	[Description("The bundled archive's sources must carry the CanManageProcessDesign authorization gate on the service handlers, since nothing else in clio can verify the privileged service it installs is gated.")]
	public void BundledArchive_ShouldCarryTheAuthorizationGateOnTheServiceHandlers() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act
		int callSites = CountOccurrences(archive, $"_guard.{AuthorizationGateMethodName}()");

		// Assert
		archive.Should().Contain("\"CanManageProcessDesign\"",
			because: "the shipped service writes process schemas, and a Creatio process can carry a script "
				+ "task — i.e. server-side C#. The gate is the CanManageProcessDesign operation plus a "
				+ "General (non-portal) user, deliberately stricter than cliogate's CanManageSolution, which "
				+ "omits the connection-type check (adr-ENG-90883 section 'Security gate')");
		callSites.Should().BeGreaterThanOrEqualTo(MinimumAuthorizationGateCallSites,
			because: "the gate sits BELOW the service boundary, in the domain handlers, so it is these call "
				+ "sites and not a per-[WebInvoke] attribute that authorize a request. An archive rebuilt "
				+ "from a pre-gate prototype installs fine and answers the install command's own probe BETTER "
				+ "than a gated one would, so this is the only place the property is checked. It is a floor, "
				+ "not a proof: it cannot catch a NEW handler added without a gate");
	}

	[Test]
	[Description("The bundled process-builder archive must be present in the build output, at the path the install command resolves.")]
	public void BundledArchive_ShouldExistInBuildOutput_AtThePathTheInstallCommandResolves() {
		// Arrange & Act
		bool exists = File.Exists(BundledArchivePath);

		// Assert
		exists.Should().BeTrue(
			because: $"install-process-builder resolves the archive as "
				+ $"<ExecutingDirectory>/{BundledPackages.ProcessBuilderPackageName}/"
				+ $"{BundledPackages.ProcessBuilderArchiveFileName}; if the committed file goes missing, "
				+ $"nothing fails until a user runs the command against a real environment. Looked in "
				+ $"'{BundledArchivePath}'. NOTE the limit of this check: it reads the BUILD OUTPUT, so it "
				+ $"cannot see a packaging regression. The csproj entry carries Pack=\"false\" and a separate "
				+ $"None/Pack pair puts the file under tools/<tfm>/any/ in the nupkg; break that and every "
				+ $"test here still passes while the shipped global tool cannot find the archive. Covering it "
				+ $"needs a check over the produced .nupkg or publish output");
	}

	[Test]
	[Description("The descriptor inside the bundled archive must match the identity clio advertises, so the [RequiresPackage] gate cannot look for a package the archive does not install.")]
	public void BundledArchive_ShouldCarryADescriptorMatchingBundledPackages() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act & Assert
		archive.Should().Contain($"\"Name\": \"{BundledPackages.ProcessBuilderPackageName}\"",
			because: "clio gates the process-designer commands on this exact name and the install command "
				+ "ships this exact archive; a mismatch installs successfully and then reports the package "
				+ "missing forever, because nothing in clio compares the two");
		archive.Should().Contain($"\"UId\": \"{ExpectedPackageUId}\"",
			because: "Creatio identifies a package by UId, so a changed UId would install a SECOND package "
				+ "instead of upgrading this one");
		archive.Should().Contain($"\"PackageVersion\": \"{BundledPackages.ProcessBuilderVersion}\"",
			because: "the constant and the descriptor must agree, since clio info reports the constant as the "
				+ "version it ships. Neither is a gate floor - the [RequiresPackage] gates are presence-only "
				+ "because Creatio does not rewrite a package's SysPackage row on re-install, so a floor could "
				+ "never be satisfied by an upgraded environment");
	}

	[Test]
	[Description("The bundled archive must contain the compile-marker Source Code schema, without which the target environment never compiles the package.")]
	public void BundledArchive_ShouldContainTheCompileMarkerSchema() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act & Assert
		archive.Should().Contain($"\"Name\": \"{CompileMarkerSchemaName}\"",
			because: "the package ships as source only, so this schema is what puts it into the target's "
				+ "configuration build; without it the package installs, never compiles, produces no "
				+ "assembly, and every ProcessDesignService call fails while the gate still reports the "
				+ "package as present");
		archive.Should().Contain("\"ManagerName\": \"SourceCodeSchemaManager\"",
			because: "only a Source Code schema drags the package into the configuration build; an entity "
				+ "or client schema would not");
	}

	[Test]
	[Description("The bundled archive must carry the package sources, since the target environment compiles them.")]
	public void BundledArchive_ShouldCarryThePackageSources() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act & Assert
		archive.Should().Contain($"namespace {BundledPackages.ProcessBuilderPackageName}",
			because: "there is no compiled assembly in the archive, so the sources ARE the payload; an "
				+ "archive without them would compile to nothing");
		archive.Should().Contain("class ProcessDesignService",
			because: "the REST entry point clio's four KnownRoute entries call must be among the shipped "
				+ "sources");
	}

	#endregion

}
