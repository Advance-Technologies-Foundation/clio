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
	/// (<c>packages/CrtProcessBuilder</c> at commit <c>53cb6be</c>, branch
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
	/// <para>
	/// Verify the reference rather than copying the previous one: this docstring named <c>e01a0ec</c> for a
	/// while, which cannot produce these bytes (its descriptor carried <c>/Date(1786026213000)/</c> against
	/// the shipped <c>/Date(1786075660000)/</c>), so anyone following it to reproduce the archive would have
	/// got a different hash. The <c>ModifiedOnUtc</c> pinned below is the cheapest way to check: it must
	/// match the descriptor at the commit named above.
	/// </para>
	/// </remarks>
	private const string ExpectedArchiveSha256 =
		"93B527B9626E0D6D63F90189A2DDA5B1D8097FEEA64F0B14D5968EEDCDA05748";

	/// <summary>
	/// The <c>PackageVersion</c> the shipped descriptor carries.
	/// </summary>
	/// <remarks>
	/// A TEST-side pin, not a reintroduction of the deleted <c>BundledPackages.ProcessBuilderVersion</c>: no
	/// production code reads it, nothing compares against it at runtime, and the version clio ships is still
	/// read out of the archive. What it exists for is the same thing the SHA-256 pin exists for — a `.gz`
	/// renders in a diff as a changed byte count, so without this line a reviewer cannot see whether the
	/// version moved. It sits beside the SHA because a rebundle edits both.
	/// <para>
	/// It cannot catch a deliberately FROZEN version — that is
	/// <c>rebundle-process-builder.ps1</c>'s must-increase guard, which refuses before touching anything.
	/// This makes the freeze visible; the script makes it impossible through the supported path.
	/// </para>
	/// </remarks>
	private const string ExpectedArchiveVersion = "1.0.0.0";

	/// <summary>
	/// The <c>ModifiedOnUtc</c> the shipped descriptor carries.
	/// </summary>
	/// <remarks>
	/// Pinned because it is what makes a version bump take effect. Creatio treats this field — not
	/// <c>PackageVersion</c> — as "this descriptor changed", and rewrites the package's <c>SysPackage</c> row
	/// only when it differs (<c>PackageStorageComposer.ApplySourcePackageChanges</c> →
	/// <c>IsPackageDescriptorChanged</c> → <c>PackageDBStorage.SavePackageDescriptor</c>'s guard). So the date
	/// decides WHETHER the row is updated and the version decides WHAT lands there; a version moved without
	/// the date installs cleanly and leaves the OLD version recorded — and that recorded version is exactly
	/// what the convergence rule compares, so the environment keeps being told it is behind after a correct
	/// upgrade.
	/// <para>
	/// That state is unreachable through <c>clio set-pkg-version</c>, which writes both fields. It is
	/// reachable by hand-editing <c>descriptor.json</c>, so this pin sits beside the SHA-256: a rebundle
	/// touches both, and a hand edit that skipped the date fails here rather than on a customer's
	/// environment.
	/// </para>
	/// <para>
	/// The value must end in <c>000</c>. <c>PackageDescriptor.ConvertToModifiedOnUtc</c> truncates to whole
	/// seconds, so a stamp carrying milliseconds proves the descriptor was NOT written by the supported
	/// command — the previous pin ended in <c>431</c>, which is how the hand edit was eventually noticed.
	/// </para>
	/// </remarks>
	private const string ExpectedDescriptorModifiedOnUtc = "/Date(1786075660000)/";

	/// <summary>
	/// The <c>ModifiedOnUtc</c> the shipped COMPILE-MARKER SCHEMA descriptor carries.
	/// </summary>
	/// <remarks>
	/// Pinned separately from the package descriptor because <c>clio set-pkg-version</c> does NOT touch schema
	/// descriptors — <c>docs/agent-instructions/bundled-packages.md</c> step 2b says so and tells the
	/// rebundler to check them by hand. This is the one field in the archive with a DEMONSTRATED production
	/// defect: the marker shipped for a day carrying LOCAL time in a UTC-labelled field (05:42:51Z for a file
	/// written at 08:46Z, i.e. exactly the +03:00 offset), because <c>ClearMilliseconds</c> dropped
	/// <c>DateTimeKind</c> and <c>ToUniversalTime</c> then treated the value as local. The producing bug is
	/// fixed, but nothing re-stamps a descriptor written before the fix — so the field with the worst record
	/// was the only one with no guard.
	/// </remarks>
	private const string ExpectedSchemaDescriptorModifiedOnUtc = "/Date(1785919371000)/";

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
	/// Exact number of <c>[OperationContract]</c> methods the shipped service may expose.
	/// </summary>
	/// <remarks>
	/// An EXACT count, unlike the gate floor above, and that is the point: a floor cannot notice a NEW
	/// operation added without a gate — the count simply rises and still clears the floor. This package is a
	/// privileged Creatio service whose one ungated operation (<see cref="UngatedOperations"/>) is a deliberate,
	/// argued exception, so a second one must not be able to arrive unnoticed. Raise this together with the
	/// allowlist, in the same commit, or not at all.
	/// </remarks>
	private const int ExpectedOperationContractCount = 5;

	/// <summary>
	/// The operations allowed to ship WITHOUT the authorization gate.
	/// </summary>
	/// <remarks>
	/// <c>Ping</c> only, and its exemption is argued at its declaration: it answers a question about the
	/// INSTALLATION rather than about process design, so gating it would fail the check for exactly the
	/// operator who has just installed the package successfully. Anything else appearing here needs the same
	/// standard of argument.
	/// </remarks>
	private static readonly string[] UngatedOperations = ["Ping"];

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
	/// Lists the archive's entry paths through the production reader.
	/// </summary>
	/// <remarks>
	/// The ONLY way to assert anything about a path. Entry names are stored UTF-16LE, so they are invisible
	/// to <see cref="ReadBundledArchiveAsText"/> — measured, a text scan finds zero hits for
	/// <c>SafeText.cs</c> while the archive really carries it. Any assertion phrased against a path must come
	/// through here or it is vacuous.
	/// </remarks>
	private static IReadOnlyList<string> ReadBundledArchiveEntryNames() {
		IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		ICompressionUtilities compressionUtilities =
			new CompressionUtilities(fileSystem, new ZipFileWrapper());
		return compressionUtilities.ListGZipEntryNames(BundledArchivePath);
	}

	/// <summary>
	/// Decompresses the archive and returns it as text. The stream is clio's own package format and
	/// carries binary members, so only file CONTENT that happens to be UTF-8 (the JSON descriptors, the C#
	/// sources, the csproj) is searchable here.
	/// </summary>
	/// <remarks>
	/// Entry PATHS are NOT searchable and never were: the container stores them UTF-16LE. Use
	/// <see cref="ReadBundledArchiveEntryNames"/> for anything about a path, and treat a hit found here as
	/// evidence about a file's contents only.
	/// </remarks>
	private static string ReadBundledArchiveAsText() {
		using FileStream compressed = File.OpenRead(BundledArchivePath);
		using GZipStream decompressor = new(compressed, CompressionMode.Decompress);
		using MemoryStream buffer = new();
		decompressor.CopyTo(buffer);
		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>
	/// Reads the shipped version the way production does — through the real
	/// <see cref="BundledPackageCatalog"/> over the real archive in this test project's build output.
	/// </summary>
	/// <remarks>
	/// Real collaborators on purpose: the point is the bytes on disk and the production container walk, not
	/// the wiring. Substituting the reader here would make every assertion built on this a statement about
	/// the substitute.
	/// </remarks>
	private static PackageVersion ReadBundledVersionThroughTheCatalog() {
		IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		workingDirectoriesProvider.ExecutingDirectory.Returns(AppContext.BaseDirectory);
		IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		IBundledPackageCatalog catalog = new BundledPackageCatalog(
			workingDirectoriesProvider, fileSystem, new CompressionUtilities(fileSystem, new ZipFileWrapper()));
		bool read = catalog.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out string diagnosis);
		read.Should().BeTrue(
			because: "clio info and the convergence rule both go through this reader, so a distribution whose "
				+ $"version it cannot read breaks both silently. Diagnosis was '{diagnosis}'");
		return version;
	}

	/// <summary>
	/// Collects every <see cref="RequiresPackageAttribute"/> declared against the bundled package on a type,
	/// the same way <c>RequiredPackageChecker.CollectTriggeredRequirements</c> does.
	/// </summary>
	private static IEnumerable<RequiresPackageAttribute> GetProcessBuilderRequirements(Type type) {
		IEnumerable<RequiresPackageAttribute> onClass =
			(RequiresPackageAttribute[])type.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true);
		IEnumerable<RequiresPackageAttribute> onProperties = type.GetProperties()
			.SelectMany(property => (RequiresPackageAttribute[])
				property.GetCustomAttributes(typeof(RequiresPackageAttribute), inherit: true));
		return onClass.Concat(onProperties)
			.Where(requirement => string.Equals(requirement.Name,
				BundledPackages.ProcessBuilderPackageName, StringComparison.OrdinalIgnoreCase));
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
				+ "than a gated one would, so this is the only place the property is checked");
	}

	[Test]
	[Description("The shipped service must expose exactly the expected number of operations, so a new ungated endpoint cannot arrive unnoticed behind the gate-count floor.")]
	public void BundledArchive_ShouldExposeNoUnexpectedOperation() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act
		int operations = CountOccurrences(archive, "[OperationContract]");

		// Assert
		operations.Should().Be(ExpectedOperationContractCount,
			because: "the gate assertion above is a FLOOR, and a floor is structurally blind to the case that "
				+ "matters most here: an operation added WITHOUT a gate raises the operation count while leaving "
				+ "the gate count untouched, so every other pin in this fixture passes and clio ships a "
				+ "privileged Creatio service with an unauthorized endpoint. Pinning the total is what makes "
				+ "that arrival fail a test. If this number is legitimately higher, raise it together with "
				+ $"UngatedOperations in the same commit — currently exempt: {string.Join(", ", UngatedOperations)}");
		foreach (string ungated in UngatedOperations) {
			archive.Should().Contain($"{ungated}()",
				because: $"'{ungated}' is allowlisted as ungated, so it must actually BE in the shipped sources — "
					+ "an allowlist naming an operation that no longer exists silently widens what the count "
					+ "above tolerates");
		}
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
				+ "CopyToOutputDirectory=Always plus PackAsTool is what puts the file under tools/<tfm>/any/ in the nupkg - there is no None/Pack pair, and the csproj's Pack=\"false\" exists to SUPPRESS extra copies rather than to place any. Break the copy and every install answers 'this clio installation does not carry the package archive' with this test still green, because it reads the build output and not the package");
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
			because: "this is the value that makes a version bump take effect at all - Creatio rewrites the "
				+ "SysPackage row only when this field changes, never because PackageVersion did. So a rebundle "
				+ "that moved the version but not the date installs cleanly and leaves the environment recording "
				+ "the OLD version, which is what the convergence rule then compares. Pinning it means such a "
				+ "rebundle fails here rather than on a customer's environment");
		// Pinned as a VALUE, not against a production constant - there is no longer one to compare with, and
		// that is the point of the current design. What this buys is the reviewability the SHA pin buys for
		// the bytes: a rebundle edits this line, so the version movement appears in the diff as readable text
		// instead of only as a changed hash. It cannot catch a DELIBERATELY frozen version (the rebundle
		// script's must-increase guard is what does that) - it makes the freeze visible to a reviewer.
		//
		// Read through the catalog rather than by scanning the archive text: `"PackageVersion"` also appears
		// on every entry of the descriptor's DependsOn array, so a textual match would silently start
		// comparing a dependency's version the day this package gains one.
		ReadBundledVersionThroughTheCatalog().ToString().Should().Be(ExpectedArchiveVersion,
			because: "the version in the shipped descriptor is what clio info reports and what the convergence "
				+ "rule compares against every environment; pinning it here is what puts a version change on a "
				+ "reviewable line rather than leaving it invisible inside the archive's byte count");
		ExpectedArchiveVersion.Should().MatchRegex("^[0-9]+(\\.[0-9]+){3}$",
			because: "four parts, because a shorter version yields Revision = -1 through System.Version and "
				+ "would compare below any four-part requirement, making a gate unsatisfiable by a successful "
				+ "install");
		archive.Should().Contain($"\"ModifiedOnUtc\": \"{ExpectedSchemaDescriptorModifiedOnUtc}\"",
			because: "set-pkg-version stamps the PACKAGE descriptor only, so the schema descriptor's timestamp "
				+ "is hand-maintained — and it is the one field here that has actually shipped wrong, carrying "
				+ "local time in a UTC-labelled field for a day. Pinning it turns the runbook's manual step 2b "
				+ "into a failing test");
		ExpectedSchemaDescriptorModifiedOnUtc.Should().EndWith("000)/",
			because: "the same provenance oracle applies to the schema descriptor: ConvertToModifiedOnUtc "
				+ "truncates to whole seconds, so a value with milliseconds was written by something other than "
				+ "clio — which is exactly how the +03:00 shift got in");
		ExpectedDescriptorModifiedOnUtc.Should().EndWith("000)/",
			because: "it is the one provenance oracle available here. PackageDescriptor.ConvertToModifiedOnUtc "
				+ "truncates to whole seconds, so milliseconds in the stamp prove the descriptor was written by "
				+ "something other than the supported command — this archive shipped for a while with a stamp "
				+ "ending in 431, i.e. hand-edited, while every doc told the next person to use "
				+ "'clio set-pkg-version'. A comment saying so would not have caught the next one");
	}

	[Test]
	[Description("The production catalog must read the shipped archive with its real container format and byte-order mark, because every unit test of it substitutes the reader and would stay green if the real walk were broken.")]
	public void BundledPackageCatalog_ShouldReadTheVersionOutOfTheRealArchive() {
		// Arrange & Act
		PackageVersion version = ReadBundledVersionThroughTheCatalog();

		// Assert
		version.ToString().Should().Be(ExpectedArchiveVersion,
			because: "the production reader must return the version actually written in the shipped "
				+ "descriptor - every unit test of the catalog substitutes the container walk, so this is the "
				+ "only place the real format, the real byte-order mark and the real entry ordering are "
				+ "exercised together");
	}

	[Test]
	[Description("Every literal version declared in a [RequiresPackage] against the bundled package must be satisfiable by the archive clio itself ships, or clio demands of an environment something it cannot supply and its own remediation cannot help.")]
	public void BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement() {
		// Arrange
		PackageVersion bundledVersion = ReadBundledVersionThroughTheCatalog();

		// Act
		// Mirrors RequiredPackageChecker.CollectTriggeredRequirements exactly — class-level with
		// inherit: true, AND every property. Anything less is worse than no guard: a property-level
		// [RequiresPackage(…, "9.9.9.9")] on a bool option is a SUPPORTED declaration form that the checker
		// enforces at runtime, so a class-only scan would stay green while shipping a gate no install can
		// satisfy. RequiresPackageAttribute.IsDefinedOn walks both levels for the same reason.
		List<RequiresPackageAttribute> declared = [];
		foreach (Type type in typeof(BundledPackages).Assembly.GetTypes()) {
			declared.AddRange(GetProcessBuilderRequirements(type));
		}
		List<RequiresPackageAttribute> versioned =
			declared.FindAll(r => !string.IsNullOrEmpty(r.Version));

		// Assert
		// The scan has to be falsifiable before its verdict means anything. Every way it could break —
		// wrong assembly, wrong attribute type, the inherit flag, the property-level miss above — yields an
		// EMPTY list and a green test, which is indistinguishable from "no literals declared". Asserting the
		// five presence-only declarations are visible is what tells the two apart.
		declared.Should().HaveCountGreaterThanOrEqualTo(5,
			because: "the five process-designer gates must be visible to this scan; if it finds fewer, the "
				+ "reflection is broken and the version loop below is silently inspecting nothing");
		// The loop is vacuous today — every declaration is presence-only — and that is the intended state.
		// It asserts an invariant that must hold WHEN a literal appears, and the commit that adds the first
		// one is exactly when it must already be in place. It replaces the old pin (descriptor version == a
		// constant), which needed hand-synchronising on every rebundle and asserted a coincidence, not a rule.
		foreach (string declaredVersion in versioned.ConvertAll(r => r.Version)) {
			PackageVersion.TryParseVersion(declaredVersion, out PackageVersion required).Should().BeTrue(
				because: $"RequiredPackageChecker parses '{declaredVersion}' through System.Version, so an "
					+ "unparseable literal makes every gated command throw instead of gating");
			required.Version.Revision.Should().BeGreaterThanOrEqualTo(0,
				because: $"'{declaredVersion}' must carry all four parts to match the archive descriptor: a "
					+ "three-part literal yields Revision = -1 and compares below any four-part installed "
					+ "version, making the gate unsatisfiable by a successful install");
			// The operator, not a FluentAssertions comparison: PackageVersion implements the non-generic
			// IComparable only, and this is the exact comparison the convergence rule and the gate perform.
			(bundledVersion >= required).Should().BeTrue(
				because: $"a command requires {declaredVersion} but this clio ships {bundledVersion}, so the "
					+ "gate would refuse an environment and then hand it an installer that cannot satisfy the "
					+ "refusal - clio must never demand what it does not carry");
		}
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
		// Entry NAMES, not a text scan of the decompressed bytes. Paths are stored UTF-16LE, so an ASCII probe
		// over the container can never match one: measured, `SafeText.cs` is a real entry and a text scan finds
		// it zero times. The previous phrasing of this test fired only incidentally — a leaked assembly happens
		// to carry its own module name in its #Strings heap — and the .pdb half could not fire at all, because
		// a portable PDB does not embed its filename. Both now read the actual inventory.
		IReadOnlyList<string> entries = ReadBundledArchiveEntryNames();

		// Act & Assert
		// Case-INSENSITIVE on purpose: the platform resolves '<packageName>.dll' case-insensitively on Windows
		// hosts, so an ordinal ban would miss 'crtprocessbuilder.dll' on the one host family where the mistake
		// still loads and serves stale code.
		entries.Should().NotContain(
			entry => entry.EndsWith($"{BundledPackages.ProcessBuilderPackageName}.dll",
				StringComparison.OrdinalIgnoreCase),
			because: "a shipped assembly turns a FAILED target-side compile into a silent one. Installing "
				+ "materialises Files/Bin into the deployed package folder (that is how cliogate's prebuilt "
				+ "assembly gets loaded at all), and the server's regenerated csproj outputs to that same path "
				+ "— so a successful build overwrites ours and it was merely dead weight, measured at +13 s. A "
				+ "failed build overwrites nothing: our DLL stays, the platform resolves the package assembly "
				+ "by name as '<packageName>.dll', and Ping answers from it — so the outcome check passes for an "
				+ "environment that never compiled the shipped sources. That check already cannot tell WHICH "
				+ "build answered; shipping an assembly would hand it a stale one to answer from, and would do "
				+ "so on a FIRST install, which is the one case the check does decide today. Note this cannot be a blanket '.dll' ban: the csproj legitimately names ~60 "
				+ "Terrasoft.* and third-party assemblies in HintPath references, so only the package's OWN "
				+ "assembly name is forbidden");
		entries.Should().NotContain(
			entry => entry.EndsWith($"{BundledPackages.ProcessBuilderPackageName}.pdb",
				StringComparison.OrdinalIgnoreCase),
			because: "symbols travel with a leaked build output and are the same accident by a different name; "
				+ "neither runbook passes --skip-pdb - both delete Files/Bin instead, which is what removes the pdb along with the assembly - so this assertion is the check on THAT step, not on a flag");
		entries.Should().NotContain(
			entry => entry.StartsWith("Files/Bin/", StringComparison.OrdinalIgnoreCase),
			because: "Files/Bin is the csproj's unconditional OutputPath and clioignore does not filter it, so "
				+ "the accident this test guards is 'the build output directory was not deleted' - banning the "
				+ "directory catches it whatever the leaked files happen to be called");
	}

	[Test]
	[Description("The archive must carry exactly the two Files/Libs compile references and nothing else with a .dll extension, because a rebundle that dropped them passes every other pin here and then fails the target's configuration build.")]
	public void BundledArchive_ShouldCarryExactlyTheTwoCompileReferences() {
		// Arrange
		// This inventory check existed only in rebundle-process-builder.ps1 step 5 until now, i.e. it protected
		// only the operator who ran the script. It could not be written as a test before, because it is phrased
		// entirely against entry PATHS and no text probe can see one.
		IReadOnlyList<string> entries = ReadBundledArchiveEntryNames();

		// Act
		List<string> dlls = entries
			.Where(entry => entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
			.ToList();

		// Assert
		dlls.Should().BeEquivalentTo(["Files/Libs/ErrorOr.dll", "Files/Libs/ATF.Repository.dll"],
			because: "ErrorOr and ATF.Repository are real compile references absent from the platform core, so "
				+ "dropping either ships source the target cannot build; and any OTHER dll is a leaked build "
				+ "output, which would survive a failed compile and answer from stale code");
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
			because: "the REST entry point clio's five KnownRoute entries call must be among the shipped "
				+ "sources");
		archive.Should().Contain("PingResponse Ping()",
			because: "the ENTIRE install verdict now rests on this one operation — IsPackageOperational asks it "
				+ "and fails unless it answers — and an archive built from any commit before the rename "
				+ "satisfies every other pin in this fixture. Without this line the route 404s and clio reports "
				+ "'the environment did not compile the package' about environments that compiled it perfectly");
		archive.Should().Contain("[DataMember(Name = \"success\")]",
			because: "clio parses the answer as PingResult.success, hand-mirrored across two repositories with "
				+ "nothing linking the two. Renaming this DataMember, or dropping it so the member serialises "
				+ "as 'Success', makes the verifier return false for a healthy install — and no test on either "
				+ "side would fail");
		archive.Should().Contain("BodyStyle = WebMessageBodyStyle.Wrapped",
			because: "the wrapper name clio looks for (PingResult) is a FUNCTION of this setting; flipping it to "
				+ "Bare removes the envelope and the verdict inverts silently");
	}

	#endregion

}
