using System;
using System.Collections.Generic;
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
	/// (<c>packages/CrtProcessBuilder</c> at commit <c>2971f76</c>, branch
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
		"D234B196C63746D2EBB210BA0F3E8466AD542EE0236B4DBEC9831D3B62883D3E";

	/// <summary>
	/// The <c>ModifiedOnUtc</c> the shipped descriptor carries.
	/// </summary>
	/// <remarks>
	/// Pinned because it is what makes a version bump take effect. Creatio treats this field — not
	/// <c>PackageVersion</c> — as "this descriptor changed", and rewrites the package's <c>SysPackage</c> row
	/// only when it differs (<c>PackageStorageComposer.ApplySourcePackageChanges</c> →
	/// <c>IsPackageDescriptorChanged</c> → <c>PackageDBStorage.SavePackageDescriptor</c>'s guard). So the date
	/// decides WHETHER the row is updated and the version decides WHAT lands there; a version moved without
	/// the date installs cleanly and leaves the recorded version — the <c>[RequiresPackage]</c> floor — behind.
	/// <para>
	/// That state is unreachable through <c>clio set-pkg-version</c>, which writes both fields. It is
	/// reachable by hand-editing <c>descriptor.json</c>, so this pin sits beside the version and the SHA-256:
	/// a rebundle touches all three, and a hand edit that skipped the date fails here rather than on a
	/// customer's environment.
	/// </para>
	/// <para>
	/// The value must end in <c>000</c>. <c>PackageDescriptor.ConvertToModifiedOnUtc</c> truncates to whole
	/// seconds, so a stamp carrying milliseconds proves the descriptor was NOT written by the supported
	/// command — the previous pin ended in <c>431</c>, which is how the hand edit was eventually noticed.
	/// </para>
	/// </remarks>
	private const string ExpectedDescriptorModifiedOnUtc = "/Date(1786013657000)/";

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

	/// <summary>
	/// The connection-type half of the gate, as the shipped guard expresses it. Pinned separately because the
	/// operation literal alone leaves half the promise unchecked — see the authorization-gate test.
	/// </summary>
	private const string ConnectionTypeCheck = "ConnectionType != UserType.General";

	/// <summary>
	/// The friend-assembly attribute in its fully-qualified csproj form. Matching this rather than the bare
	/// word keeps the csproj's own explanatory comment about <c>InternalsVisibleTo</c> out of the match.
	/// </summary>
	private const string VisibilityAttributeMarker =
		"AssemblyAttribute Include=\"System.Runtime.CompilerServices.InternalsVisibleTo\"";

	/// <summary>
	/// The MSBuild property that must condition every friend-assembly group. Defined outside the package
	/// directory, so only a local build sets it.
	/// </summary>
	private const string VisibilityConditionProperty = "CrtProcessBuilderIncludeTestVisibility";

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
		// Pins the PAIRING — every friend-assembly attribute sits inside an ItemGroup conditioned on the
		// local-only property — rather than the absence of one hand-written Label. The label is cosmetic free
		// text in ANOTHER repository: renaming it, or swapping Condition/Label order, would have made the old
		// probe pass an archive with unconditioned visibility (and red-fail a correct one). Matching the
		// fully-qualified attribute form keeps the csproj's own explanatory comment, which says the word
		// "InternalsVisibleTo", from reading as a violation.
		string[] itemGroups = archive.Split("<ItemGroup");
		List<string> unconditionedGroups = [];
		foreach (string itemGroup in itemGroups) {
			if (!itemGroup.Contains(VisibilityAttributeMarker, StringComparison.Ordinal)) {
				continue;
			}
			int openingTagEnd = itemGroup.IndexOf('>');
			string openingTag = openingTagEnd < 0 ? itemGroup : itemGroup[..openingTagEnd];
			if (!openingTag.Contains(VisibilityConditionProperty, StringComparison.Ordinal)) {
				unconditionedGroups.Add(openingTag);
			}
		}

		// Assert
		archive.Should().Contain(VisibilityAttributeMarker,
			because: "the attributes must survive for LOCAL builds (the package's own tests reach internals), so "
				+ "the fix is a condition on a property defined outside the package directory — not deleting "
				+ "them. Losing them entirely breaks the package repository's test project, so this asserts the "
				+ "pairing exists before the next assertion checks that it holds");
		unconditionedGroups.Should().BeEmpty(
			because: "an UNCONDITIONED visibility group ships the friend assemblies into the assembly the "
				+ "TARGET compiles. Established on a stand rather than assumed: the platform rewrites the "
				+ "package csproj on install but does NOT drop these entries, and the DLL the stand produced "
				+ "contained both friend names. Creatio compiles configuration packages into separate UNSIGNED "
				+ "assemblies, where InternalsVisibleTo matches on simple name, and 'DynamicProxyGenAssembly2' "
				+ "has no dot — i.e. it is a usable Creatio package name");
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
		archive.Should().Contain(ConnectionTypeCheck,
			because: "the operation literal alone pins only HALF of what every clio surface promises — the help "
				+ "text, the command docs and the MCP contract all state a General (non-portal) user is "
				+ "required too, and that half is exactly what makes this gate stricter than CanManageSolution. "
				+ "A rebundle that lost the connection-type check would install, pass every other pin here, and "
				+ "let a portal user holding CanManageProcessDesign write a process carrying a script task");
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
		archive.Should().Contain($"\"ModifiedOnUtc\": \"{ExpectedDescriptorModifiedOnUtc}\"",
			because: "this is the value that makes the version bump take effect - Creatio rewrites the "
				+ "SysPackage row only when it changes. If this assertion fails together with the version one, "
				+ "the bump was done properly (clio set-pkg-version writes both); if only the version "
				+ "assertion fails, descriptor.json was hand-edited and the [RequiresPackage] floor would "
				+ "refuse every environment that already carries the package");
		archive.Should().Contain($"\"PackageVersion\": \"{BundledPackages.ProcessBuilderVersion}\"",
			because: "the descriptor is the ONLY place this package's version lives, and this is the single link "
				+ "between it and clio: BundledPackages.ProcessBuilderVersion is what clio info reports and the "
				+ "floor the [RequiresPackage] gates enforce against the version the environment recorded from "
				+ "this very descriptor. A drift either refuses a correct installation or accepts a stale one, "
				+ "and nothing else in the product compares the two");
		ExpectedDescriptorModifiedOnUtc.Should().EndWith("000)/",
			because: "it is the one provenance oracle available here. PackageDescriptor.ConvertToModifiedOnUtc "
				+ "truncates to whole seconds, so milliseconds in the stamp prove the descriptor was written by "
				+ "something other than the supported command — this archive shipped for a while with a stamp "
				+ "ending in 431, i.e. hand-edited, while every doc told the next person to use "
				+ "'clio set-pkg-version'. A comment saying so would not have caught the next one");
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
	[Description("The bundled archive must carry NO compiled assembly of its own, because a shipped DLL survives a failed target-side compile and then serves stale code that the install command's own outcome check would accept as success.")]
	public void BundledArchive_ShouldNotCarryACompiledAssembly() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act & Assert
		// Case-INSENSITIVE on purpose: the platform resolves '<packageName>.dll' case-insensitively on Windows
		// hosts, so an ordinal ban would miss 'crtprocessbuilder.dll' on the one host family where the mistake
		// still loads and serves stale code.
		archive.Should().NotContainEquivalentOf($"{BundledPackages.ProcessBuilderPackageName}.dll",
			because: "a shipped assembly turns a FAILED target-side compile into a silent one. Installing "
				+ "materialises Files/Bin into the deployed package folder (that is how cliogate's prebuilt "
				+ "assembly gets loaded at all), and the server's regenerated csproj outputs to that same path "
				+ "— so a successful build overwrites ours and it was merely dead weight, measured at +13 s. A "
				+ "failed build overwrites nothing: our DLL stays, the platform resolves the package assembly "
				+ "by name as '<packageName>.dll', and ListUserTasks answers from it — so the outcome check "
				+ "passes for an environment that never compiled the shipped sources. That check already "
				+ "cannot tell WHICH build answered; shipping an assembly would give it a stale one to answer "
				+ "from. Note this cannot be a blanket '.dll' ban: the csproj legitimately names ~60 "
				+ "Terrasoft.* and third-party assemblies in HintPath references, so only the package's OWN "
				+ "assembly name is forbidden");
		archive.Should().NotContainEquivalentOf($"{BundledPackages.ProcessBuilderPackageName}.pdb",
			because: "symbols travel with a leaked build output and are the same accident by a different name; "
				+ "the bundling runbook passes --skip-pdb, and this asserts it actually happened");
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
