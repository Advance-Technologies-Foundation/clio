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
	/// Every top-level entry the bundled archive may contain.
	/// </summary>
	/// <remarks>
	/// Adding to this list is a decision about what runs on a customer's environment, so make it deliberately
	/// and say why. <c>SqlScripts</c> and <c>Data</c> are the two that must never appear — Creatio executes
	/// them at install time and the install passes no <c>PackageInstallOptions</c>, so the platform's own
	/// defaults apply — but the list is an allowlist rather than a ban on those two names, because a denylist
	/// would need extending for every install-time mechanism the platform grows and would be silently
	/// incomplete until somebody noticed.
	/// </remarks>
	private static readonly string[] AllowedTopLevelEntries = [
		"descriptor.json", "Files", "Schemas", "Resources"
	];

	/// <summary>
	/// SHA-256 of the committed archive. Produced by <c>rebundle-process-builder.ps1</c> at
	/// <see cref="ExpectedArchiveVersion"/> from
	/// the <c>ProcessBuilder</c> repository (<c>packages/CrtProcessBuilder</c>, branch
	/// <c>feature/ENG-95891-formula-expressions</c>), at the commit recorded mechanically in
	/// <see cref="ExpectedProducingCommit"/> — the script captures <c>git rev-parse HEAD</c> and refuses to cut
	/// from a tree with uncommitted changes, so this reference is no longer a sentence anyone has to keep true
	/// by hand. Many numbers below the current one are burned rather than reused — some because two branches drew
	/// from one monotonic sequence on the same day, most because a cut was superseded before it left the machine
	/// and abandoned rather than re-pointed. Two branches collide unless the number is claimed BEFORE it is cut,
	/// so a gap is always cheaper than an ambiguous number, and a version identifying two different byte sets is
	/// worse than either.
	/// <para>Which numbers are burned is deliberately NOT recorded, here or anywhere: two files have carried such a
	/// list, both went stale, and this file's went stale four times. To pick the next number, go UP from this
	/// constant — the highest THIS branch has cut — and then check the candidate is unused in BOTH histories, this
	/// file's and the package repository's <c>descriptor.json</c>, because a cut can exist there without ever
	/// reaching a clio commit. Do not take one above the global maximum across all branches: another branch sitting
	/// higher does not make its number yours to continue, and adopting it produces a version that looks newer than
	/// work it does not contain. See docs/agent-instructions/bundled-packages.md for the commands.</para>
	/// <para>What this cut carries, over the 1.3.1.1 performer/lookup delivery it replaces: server-side
	/// VALIDATION of formula expressions — an <c>expression</c> mapping source and a conditional-flow condition
	/// are now parsed, their parameter references resolved against the process, and their result type checked
	/// against the declared target, instead of being stored unchecked. The MINOR digit moved at 1.4.0.0 because
	/// that is a new capability; every PATCH digit over it fixes something a review or a manual case found, and
	/// each is raised so a stand still carrying an earlier one is DETECTABLY behind — same-version re-cuts make
	/// equal version numbers mean nothing, which the convergence check cannot see through.</para>
	/// <para>
	/// The cut ran with the package's own gate tests passing rather than under <c>-SkipTests</c>, and the
	/// script verified the archive inventory it produced. The byte-for-byte comparison of every archive entry
	/// against the commit's CHECKOUT rendering was NOT re-run here, and the clean-tree refusal does NOT cover
	/// it: a clean TREE and a clean CHECKOUT are different states. `git add` normalises to LF in the INDEX while
	/// the working tree keeps what was written, so LF files can be committed, leave the tree clean, pass the
	/// gate, and still be packed as LF where a checkout of that same commit renders CRLF. The gate closes the
	/// DIRTY-TREE failure and that one only — see the paragraph above for why it does not close the
	/// commit-is-behind one either.
	/// It earned its keep on the 1.3.1.1 cut — freshly written files carried LF
	/// where a checkout on a <c>core.autocrlf=true</c> host produces CRLF, an archive corresponding to no commit
	/// at all — so re-run the line-ending audit whenever the archive is cut from a tree with just-written files,
	/// and do not read its absence here as a clean result.
	/// </para>
	/// Provenance rules live in <c>docs/agent-instructions/bundled-packages.md</c>.
	/// </summary>
	/// <remarks>
	/// This is the change-detection pin, not a security control: it cannot tell a good archive from a bad
	/// one. What it does is make replacing the archive impossible to do QUIETLY — a <c>.gz</c> swap otherwise
	/// shows up in review as nothing but a changed byte count, and passes every other check here, because the
	/// rest are substring probes that a tampered archive can satisfy while adding whatever it likes. Update
	/// this constant in the SAME commit that replaces the archive, and say in the message which commit of the
	/// producing repository the bytes came from.
	/// <para>
	/// Verify the reference rather than copying the previous one, and verify it in BOTH directions — this
	/// paragraph has now been wrong each way round. It first named <c>e01a0ec</c>, whose descriptor carried
	/// <c>/Date(1786026213000)/</c> and so cannot produce these bytes. It was then corrected to a commit whose
	/// descriptor still said <c>/Date(1786075660000)/</c> while the archive shipped
	/// <c>/Date(1786345127000)/</c>: that time the COMMIT was behind, because the restamp a rebundle performs
	/// happens in the package checkout and was left uncommitted there. Both failures look identical to whoever
	/// follows the reference — a different hash — so the check is the same: the <c>ModifiedOnUtc</c> pinned
	/// below CANNOT match the descriptor at the commit named above, and expecting it to was the error in this
	/// paragraph. The script captures <c>rev-parse HEAD</c> at step 0b and performs the restamp at step 2, so
	/// the pin names the commit BEFORE the version moved — by design, and unavoidably, because the script does
	/// not commit. Today's pins show it plainly: <see cref="ExpectedProducingCommit"/> resolves to a descriptor
	/// reading a version one restamp behind the archive beside it.
	/// <para>So reproducing the bytes is not a checkout, and not two steps either: check out the pin, re-run
	/// <c>set-pkg-version</c> with the pinned version, then hand-set <c>ModifiedOnUtc</c> to
	/// <see cref="ExpectedDescriptorModifiedOnUtc"/>, then pack. The third step is not optional —
	/// <c>SetPackageVersionCommand</c> writes <c>DateTime.Now</c> and takes no timestamp argument, so re-running
	/// it stamps the present and the descriptor bytes differ every time. That pin exists for exactly this. Even
	/// then the hash matches only on a host that renders the same line endings and the same path separator, which
	/// is why the line-ending note above is not a footnote.</para>
	/// <para>What the pin establishes is which SOURCES the
	/// archive was built from — which is the question that actually matters, since the descriptor is the one
	/// file the rebundle rewrites and the one whose expected content is pinned separately. Committing the
	/// restamp afterwards is still part of every rebundle, but not for the reason first written here: the pin
	/// names the PRE-restamp commit, which is on the branch either way. The real reasons are that the next cut's
	/// clean-tree gate refuses a dirty tree, and that the pinned <c>ModifiedOnUtc</c> would otherwise exist in no
	/// commit at all.</para>
	/// </para>
	/// <para>
	/// It has since been wrong a THIRD way, which no amount of checking the date would have caught: the bytes
	/// did not correspond to ANY commit. Nine sources differed from a real checkout of the referenced commit by
	/// LINE ENDINGS alone — the archive carried LF where a checkout produces CRLF — because it was cut from
	/// freshly written files before they had round-tripped through git, and this host normalises on checkout
	/// (<c>core.autocrlf=true</c>). Identical content, different bytes, unreproducible hash. So the reference
	/// is only verifiable if the archive is packed from a CLEAN checkout: pack from a tree carrying
	/// just-written files and the pin records bytes nobody can reproduce, which leaves this constant detecting
	/// change while establishing nothing about provenance. That entry-by-entry audit was run for the 1.3.1.1
	/// cut and is what caught the line-ending case; it was NOT re-run for the archive pinned below.
	/// Two statements about the same bytes, one reassuring and one not, is exactly the shape this file exists
	/// to prevent, so read the summary above as authoritative on what was and was not checked for THIS cut.
	/// </para>
	/// </remarks>
	private const string ExpectedArchiveSha256 =
		"DA653F2DE9B98CD8A1F797EA21AE84486B483874BCA8AEB99E38B2F272A1206E";

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
	/// This makes the freeze visible; the script makes it hard to do by accident.
	/// <para>
	/// The must-increase rule starts at the FIRST RELEASE. Before the package has ever shipped in a released
	/// clio there is no CUSTOMER environment carrying it, so a same-version re-cut (only <c>ModifiedOnUtc</c>
	/// moves) is permitted — but its real boundary is narrower than "unreleased": the moment a cut leaves the
	/// working copy (pushed, installed on a shared stand, handed to a reviewer), equal version numbers stop
	/// meaning equal bytes and the convergence check cannot tell the copies apart — this branch re-cut 1.3.0.5
	/// three times and then had to raise twice precisely to make stale review-time copies detectable. So:
	/// same-version only while the previous cut never left your machine; otherwise raise. The script still
	/// refuses a same-version cut; the pre-release path is deliberately the manual one, so the guard keeps no
	/// hole for the steady state.
	/// </para>
	/// </para>
	/// </remarks>
	private const string ExpectedArchiveVersion = "1.4.0.64";

	/// <summary>
	/// The commit of the PRODUCING repository the archive was cut from, written by
	/// <c>rebundle-process-builder.ps1</c> from <c>git rev-parse HEAD</c> rather than typed here.
	/// <para>This does not PROVE the bytes came from that commit — nothing in this repository can, because the
	/// producing checkout is not present. What it removes is the failure mode that has actually happened three
	/// times, all recorded on <see cref="ExpectedArchiveSha256"/>: a hand-written reference that was stale,
	/// behind, or named no commit at all. The script now also refuses to cut from a dirty tree, so "an archive
	/// corresponding to no commit" is unreachable rather than merely documented. Anyone with a checkout can
	/// verify the rest with one `git checkout`.</para>
	/// </summary>
	private const string ExpectedProducingCommit = "3352e0f94e72282ce6a2c93cbd2a9abd2b3038f7";

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
	private const string ExpectedDescriptorModifiedOnUtc = "/Date(1788698008000)/";

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
	/// EXACT number of call sites that must invoke <see cref="AuthorizationGateMethodName"/> in the shipped
	/// sources.
	/// </summary>
	/// <remarks>
	/// Three, NOT four, and the difference is worth writing down because it is the first thing anyone
	/// recomputing this number gets wrong: it is not one gate per gated operation. Of the
	/// <see cref="ExpectedOperationContractCount"/> operations, one is ungated
	/// (<see cref="UngatedOperations"/>) and the remaining four are gated at three places, because
	/// <c>ProcessDesigner.Execute</c> is a SHARED boundary for the two read operations — it applies the guard
	/// once and both <c>ListUserTasks</c> and <c>DescribeProcess</c> pass through it. Build and modify do not
	/// use it (they own their own rollback and session-release error handling), so they gate in their own
	/// handlers. Hence: 2 write handlers + 1 shared read boundary.
	/// <para>
	/// EXACT rather than a floor, but do NOT read more into that than it gives. A floor equal to the current
	/// count already catches a dropped gate — remove one of the three and two remain — so exactness adds only
	/// the detection of an ADDED call site, which is never a regression. Its real value is that the number is
	/// now stated with its arithmetic: a reader who assumes one gate per operation concludes a floor of 3 sits
	/// two below the truth and that a dropped gate would pass it, which is how this pin came to be reported as
	/// broken when it was not.
	/// <para>
	/// What NEITHER form catches is a gate MOVED rather than removed — the total survives by definition. Do
	/// NOT try to close that here. It needs each gate bound to the operation it protects, and a text scan over
	/// an archive cannot do it honestly: proximity matching would accept a call that never executes and would
	/// break on reformatting. The property is already asserted where the code lives and can be substituted, by
	/// four tests in the ProcessBuilder repository that make <c>IProcessDesignGuard</c> deny and require the
	/// operation to fail without doing its work — <c>BuildProcess_ShouldFailAndNotMutate_WhenGuardDenies</c>,
	/// <c>ModifyProcess_ShouldFailAndNotMutate_WhenGuardDenies</c>,
	/// <c>ListUserTasks_ShouldRefuseAndNotQueryCatalog_WhenGuardDenies</c> and
	/// <c>DescribeProcess_ShouldRefuseAndNotQueryDescriber_WhenGuardDenies</c> — plus
	/// <c>ProcessDesignGuardTests</c> for the gate itself. Those are strictly stronger than any byte scan:
	/// they prove the guard is on the execution path, not merely present in the text.
	/// </para>
	/// <para>
	/// What is left for THIS file is the coarse failure the other repository's CI cannot see: an archive
	/// bundled from a pre-gate state as a whole, which is what the literals and counts here catch. The one
	/// path that reaches it is <c>rebundle-process-builder.ps1 -SkipTests</c>. Adding a gated operation must
	/// raise this count in the same commit as <see cref="ExpectedOperationContractCount"/> — both are security
	/// counts, and neither should be able to drift on its own.
	/// </para>
	/// </remarks>
	private const int ExpectedAuthorizationGateCallSites = 3;

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

	/// <summary>
	/// Counts occurrences of <paramref name="value"/> that are actually CODE — i.e. not preceded on their own
	/// line by a <c>//</c> comment marker.
	/// </summary>
	/// <remarks>
	/// A plain substring count over archive text cannot tell a live call from a commented-out one, and for the
	/// authorization gate that gap is the whole guard: comment out all three
	/// <c>_guard.EnsureCanManageProcessDesign()</c> calls and the count stays at three, the operation count is
	/// unchanged, and both gate literals still match — because the guard CLASS is untouched — so an archive
	/// with zero live gates passes every pin in this fixture. Line-level rather than token-level on purpose:
	/// this is a text scan over sources it cannot parse, so it recognises the one form that actually occurs
	/// (<c>// _guard.…</c>) and does not pretend to understand block comments or strings.
	/// </remarks>
	private static int CountUncommentedOccurrences(string text, string value) {
		int count = 0;
		for (int index = text.IndexOf(value, StringComparison.Ordinal);
			index >= 0;
			index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) {
			int lineStart = text.LastIndexOfAny(['\n', '\r'], index) + 1;
			string beforeOnLine = text.Substring(lineStart, index - lineStart);
			if (!beforeOnLine.Contains("//", StringComparison.Ordinal)) {
				count++;
			}
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
		int callSites = CountUncommentedOccurrences(archive, $"_guard.{AuthorizationGateMethodName}()");

		// Assert
		archive.Should().Contain("\"CanManageProcessDesign\"",
			because: "the shipped service writes process schemas, and a Creatio process can carry a script "
				+ "task — i.e. server-side C#. The gate is the CanManageProcessDesign operation plus a "
				+ "General (non-portal) user, deliberately stricter than cliogate's CanManageSolution, which "
				+ "omits the connection-type check (see the backend-process-designer ADR, section "
				+ "'Security gate')");
		archive.Should().Contain(ConnectionTypeCheck,
			because: "the operation literal alone pins only HALF of what every clio surface promises — the help "
				+ "text, the command docs and the MCP contract all state a General (non-portal) user is "
				+ "required too, and that half is exactly what makes this gate stricter than CanManageSolution. "
				+ "A rebundle that lost the connection-type check would install, pass every other pin here, and "
				+ "let a portal user holding CanManageProcessDesign write a process carrying a script task");
		callSites.Should().Be(ExpectedAuthorizationGateCallSites,
			because: "the gate sits BELOW the service boundary, in the domain handlers, so it is these call "
				+ "sites and not a per-[WebInvoke] attribute that authorize a request. Two write handlers plus "
				+ "the one shared read boundary in ProcessDesigner.Execute cover all four gated operations — "
				+ "see ExpectedAuthorizationGateCallSites for why that is three and not four. An archive "
				+ "rebuilt from a pre-gate prototype installs fine and answers the install command's own probe "
				+ "BETTER than a gated one would, so this is the only place the property is checked");
	}

	[Test]
	[Description("The shipped service must expose exactly the expected number of operations, so a new ungated endpoint cannot arrive unnoticed behind the gate-count assertion.")]
	public void BundledArchive_ShouldExposeNoUnexpectedOperation() {
		// Arrange
		string archive = ReadBundledArchiveAsText();

		// Act
		int operations = CountOccurrences(archive, "[OperationContract]");

		// Assert
		operations.Should().Be(ExpectedOperationContractCount,
			because: "the gate assertion above counts CALL SITES, which is structurally blind to the case that "
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
			because: "four parts and nothing else. Shorter yields Revision = -1 through System.Version and would "
				+ "compare below any four-part requirement, making a gate unsatisfiable by a successful install. "
				+ "A pre-release SUFFIX is excluded by the same pattern, and that is now load-bearing rather "
				+ "than tidiness: InstallProcessBuilderCommand compares four-part numbers alone, which is sound "
				+ "only while no suffix can reach the shipped side — one that did would make a rollback "
				+ "undetectable, so the command refuses such a distribution outright and this line is what stops "
				+ "one being committed in the first place");
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
		// The scan has to be falsifiable before its verdict means anything. Most ways it could break — wrong
		// assembly, wrong attribute type, the inherit flag — yield an EMPTY list and a green test, which is
		// indistinguishable from "no literals declared", and asserting the five presence-only declarations
		// are visible tells those two apart. It does NOT cover the property-level arm: all five declarations
		// in the assembly are class-level today, so this count survives deleting the GetProperties() walk
		// entirely. That arm is falsified by GetProcessBuilderRequirements_ShouldSeeAPropertyLevelDeclaration
		// instead, on a fixture-local type, which is the only way to test it before the first real
		// property-level literal ships — the very moment it has to already work.
		declared.Should().HaveCountGreaterThanOrEqualTo(5,
			because: "the five process-designer gates must be visible to this scan; if it finds fewer, the "
				+ "reflection is broken and the version loop below is silently inspecting nothing");
		// The loop EXECUTES today: two of the five carry a version literal (create/modify, 1.4.0.60). It was
		// vacuous when written, deliberately — the invariant had to be in place before the first literal
		// appeared, because the commit that adds one is exactly when it must already work. It replaces the old
		// pin (descriptor version == a constant), which needed hand-synchronising on every rebundle and
		// asserted a coincidence, not a rule.
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

	/// <summary>
	/// Reads a repository file by walking up from the test assembly to the directory that holds the solution.
	/// The fixture already resolves the bundled archive from the build output; a repository DOC is not copied
	/// there, so it is read from the checkout instead. Fails loudly rather than skipping: a pin that silently
	/// stops reading its surface is the failure this test exists to prevent.
	/// </summary>
	private static string ReadRepositoryText(string relativePath) {
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, relativePath))) {
			directory = directory.Parent;
		}
		directory.Should().NotBeNull(
			because: $"'{relativePath}' must be reachable from the test directory - without it this pin would "
				+ "silently stop covering that surface, which is exactly how it drifted in the first place");
		return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
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
			because: "symbols travel with a leaked build output and are the same accident by a different name. "
				+ "BOTH runbooks pass --skip-pdb, and both also delete Files/Bin, which is what actually removes "
				+ "the pdb along with the assembly - so this assertion checks the OUTCOME rather than either "
				+ "step. The earlier version of this sentence claimed neither runbook passed the flag, which was "
				+ "false for the script and would have told the next reader not to look for it while the manual "
				+ "path silently produced different bytes for the same archive");
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
	[Description("The archive may contain NOTHING that executes on install beyond the sources the target compiles. This is the only pin that constrains what else is in there: every other check in this fixture is a substring probe over the decompressed text or a descriptor field, and the DLL inventory above covers .dll entries alone. Creatio applies its own defaults for SQL scripts and bound data because the install passes no PackageInstallOptions, so a SqlScripts/ or Data/ folder arriving in this archive would run arbitrary SQL and write bound rows — role membership, granted operations, system settings — on every customer environment that installs it. The whole-archive SHA-256 does not compensate: the rebundle script refreshes it from the archive it just produced, so the clio-side diff for such an addition is one hash line, one date line and a binary blob.")]
	public void BundledArchive_ShouldContainNothingThatExecutesOnInstall() {
		// Arrange
		IReadOnlyList<string> entries = ReadBundledArchiveEntryNames();

		// Act
		List<string> unexpectedTopLevel = entries
			.Select(entry => entry.Split('/')[0])
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(top => !AllowedTopLevelEntries.Contains(top, StringComparer.OrdinalIgnoreCase))
			.ToList();
		List<string> schemaFolders = entries
			.Where(entry => entry.StartsWith("Schemas/", StringComparison.OrdinalIgnoreCase))
			.Select(entry => entry.Split('/')[1])
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		// Assert
		// Every allowlisted name must actually BE in the archive, for the same reason the ungated-operation
		// allowlist is presence-checked: a name left behind after the thing it named stopped shipping silently
		// widens what this test tolerates, and nothing else would notice.
		List<string> presentTopLevel = entries
			.Select(entry => entry.Split('/')[0])
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		foreach (string allowed in AllowedTopLevelEntries) {
			presentTopLevel.Should().Contain(
				present => string.Equals(present, allowed, StringComparison.OrdinalIgnoreCase),
				because: $"'{allowed}' is on the allowlist, so it must still be something the archive ships — an "
					+ "entry left on the list after it stopped shipping widens the check below without saying so");
		}
		// An ALLOWLIST, not a ban on the two folder names that motivated it. A denylist would have to be
		// extended for every install-time mechanism Creatio grows, and would be silently wrong until someone
		// noticed the new one — which is the same shape of failure this whole fixture exists to prevent.
		unexpectedTopLevel.Should().BeEmpty(
			because: "a source-only package needs exactly the descriptor, the sources and their resources. "
				+ $"Anything else is either inert (then say so and add it to {nameof(AllowedTopLevelEntries)} "
				+ "deliberately) or it executes on the customer's environment at install time, which is a "
				+ "decision nobody should be able to make by dropping a folder into the producing repository");
		schemaFolders.Should().BeEquivalentTo([CompileMarkerSchemaName],
			because: "the compile marker is the only schema this package ships, and it is empty on purpose. A "
				+ "second schema is not inert: a ProcessSchema can carry a script task, and a client schema "
				+ "reaches the UI — both would install and run under the package's own name, below the "
				+ "CanManageProcessDesign gate that protects everything the service itself does");
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

	[Test]
	[Description("The collector must see a PROPERTY-level declaration. Every CrtProcessBuilder gate in the assembly happens to be class-level today, so the requirement scan's own self-check (five declarations visible) is satisfied without the property walk ever running — deleting that walk leaves the suite green. This fixture-local type is the red test the walk did not have, and it stays meaningful after the first real property-level literal ships.")]
	public void GetProcessBuilderRequirements_ShouldSeeAPropertyLevelDeclaration() {
		// Arrange & Act
		List<RequiresPackageAttribute> declared =
			[.. GetProcessBuilderRequirements(typeof(PropertyLevelRequirementFixture))];

		// Assert
		declared.Should().HaveCount(1,
			because: "[RequiresPackage] is declared for AttributeTargets.Property as well as Class, and the "
				+ "runtime checker enforces both, so a scan that walks only classes reports a gate it cannot see");
		declared[0].Version.Should().Be("9.9.9.9",
			because: "the version literal is the whole point of collecting it — the satisfiability check "
				+ "downstream compares it against what the archive carries");
	}

	[Test]
	[Description("Symmetry for the class-level arm: the collector passes inherit: true, so a requirement declared on a base class must be seen through a derived type. RequiredPackageChecker resolves the requirement from the concrete options type it is handed, which is routinely a subclass.")]
	public void GetProcessBuilderRequirements_ShouldSeeAClassLevelDeclarationThroughADerivedType() {
		// Arrange & Act
		List<RequiresPackageAttribute> declared =
			[.. GetProcessBuilderRequirements(typeof(DerivedFromClassLevelRequirementFixture))];

		// Assert
		declared.Should().HaveCount(1,
			because: "the attribute is Inherited = true and the collector asks for inherited attributes, so a "
				+ "derived options type carries its base's gate");
	}

	[Test]
	[Description("The collector must filter by package name. Without the filter it would fold cliogate's own declarations — which are numerous, versioned, and satisfied by a completely different archive — into a check that compares them against the CrtProcessBuilder archive, and demand of it versions it was never meant to carry.")]
	public void GetProcessBuilderRequirements_ShouldIgnoreADeclarationForAnotherPackage() {
		// Arrange & Act
		List<RequiresPackageAttribute> declared =
			[.. GetProcessBuilderRequirements(typeof(OtherPackageRequirementFixture))];

		// Assert
		declared.Should().BeEmpty(
			because: "only requirements naming the bundled package may reach the satisfiability check; a "
				+ "cliogate floor is satisfied by a different archive entirely");
	}

	[Test]
	[Description("No 'CrtProcessBuilder <version>' literal the process-designer tool descriptions and the modify prompt hand out to agents may name a version NEWER than the archive clio bundles. Each literal is a capability floor — the version a route or a check first shipped in — so it is expected to lag, sometimes by several releases; what it may never do is promise a version this distribution cannot install, because the remedy those same sentences hand the agent ('update the package') would then be unperformable. This pin previously demanded EQUALITY, and equality is what walked the bare-Guid lookup route's honest 1.3.1.1 through 1.4.0.0, .1, .2 and .3 — four consecutive rebundle commits, not one of them a change in the fact, while McpCapabilityMap.md went on saying 1.3.1.1 for the same route.")]
	public void ToolContractVersionLiterals_ShouldNotExceedTheBundledArchiveVersion() {
		// Arrange — the shipped agent-facing texts that name the package version
		var surfaces = new Dictionary<string, string> {
			["create-business-process description"] =
				GetToolDescription(typeof(Clio.Command.McpServer.Tools.ProcessDesigner.CreateBusinessProcessTool)),
			["modify-business-process description"] =
				GetToolDescription(typeof(Clio.Command.McpServer.Tools.ProcessDesigner.ModifyBusinessProcessTool)),
			["modify-business-process prompt"] =
				Clio.Command.McpServer.Prompts.ProcessDesigner.ModifyBusinessProcessPrompt.PromptByProcess(
					"env-placeholder", "process-placeholder")
		};
		var literalPattern = new System.Text.RegularExpressions.Regex(@"CrtProcessBuilder (\d+\.\d+\.\d+\.\d+)");

		var anyVersionPattern = new System.Text.RegularExpressions.Regex(@"\d+\.\d+\.\d+\.\d+");

		// Act & Assert
		foreach (KeyValuePair<string, string> surface in surfaces) {
			System.Text.RegularExpressions.MatchCollection matches = literalPattern.Matches(surface.Value);
			matches.Should().NotBeEmpty(
				because: $"the {surface.Key} documents the version the lookup/performer route ships from — if the "
					+ "sentence was removed on purpose, remove the surface from this test in the same commit");
			foreach (System.Text.RegularExpressions.Match match in matches) {
				AssertInstallableFromThisDistribution(match.Groups[1].Value, surface.Key);
			}
			// The wide net behind the shaped one: ANY four-part version on these surfaces is the package
			// version (nothing else four-part belongs in them), so a mention that drifts into a different
			// shape — 'CrtProcessBuilder >= X', 'pre-X', a bare number — cannot hide beside a matching literal.
			foreach (System.Text.RegularExpressions.Match match in anyVersionPattern.Matches(surface.Value)) {
				AssertInstallableFromThisDistribution(match.Value, surface.Key);
			}
		}
	}

	[Test]
	[Description("The ENFORCED floor sentence must equal the literal the command actually gates on. These surfaces carry two kinds of version sentence and only one may lag: a CAPABILITY floor ('the route ships from CrtProcessBuilder 1.3.1.1') records when something first shipped and freezes there, while 'this clio requires X' states what [RequiresPackage] refuses below. The sibling pin was relaxed from equality to <= for the capability floors, which was right, and that relaxation left this sentence checked by nothing: rewriting it to 1.2.0.1 keeps the whole suite green while telling every agent to update to a version the gate does not enforce.")]
	public void EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral() {
		// Arrange — the enforced floor as the ATTRIBUTE states it, by reflection rather than retyped here.
		var surfaces = new Dictionary<string, (string Text, Type OptionsType)> {
			["create-business-process description"] = (
				GetToolDescription(typeof(Clio.Command.McpServer.Tools.ProcessDesigner.CreateBusinessProcessTool)),
				typeof(Clio.Command.CreateBusinessProcessOptions)),
			["modify-business-process description"] = (
				GetToolDescription(typeof(Clio.Command.McpServer.Tools.ProcessDesigner.ModifyBusinessProcessTool)),
				typeof(Clio.Command.ModifyBusinessProcessOptions)),
			// The capability map states the same sentence to the same audience and is NOT reflected over, so it
			// drifted to 1.4.0.37 against an enforced .44 and shipped green - found in review, not here. It is a
			// repository file, so the pin reads it: a prose surface that promises a gate belongs to the gate's
			// test, not to a script nothing runs. Keyed on modify's options type because the sentence in it
			// describes the modify path's mapped expression.
			["McpCapabilityMap.md"] = (
				ReadRepositoryText(Path.Combine("docs", "McpCapabilityMap.md")),
				typeof(Clio.Command.ModifyBusinessProcessOptions))
		};
		var sentencePattern = new System.Text.RegularExpressions.Regex(
			@"this clio requires (\d+\.\d+\.\d+\.\d+)");

		// Act & Assert
		foreach (KeyValuePair<string, (string Text, Type OptionsType)> surface in surfaces) {
			string enforced = surface.Value.OptionsType
				.GetCustomAttributes(typeof(Clio.Common.RequiresPackageAttribute), inherit: false)
				.Cast<Clio.Common.RequiresPackageAttribute>()
				.Select(attribute => attribute.Version)
				.FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));
			enforced.Should().NotBeNullOrWhiteSpace(
				because: $"the {surface.Key} names an enforced floor, so its options type must carry a versioned "
					+ "[RequiresPackage] - otherwise the sentence promises a gate that does not exist");
			System.Text.RegularExpressions.MatchCollection matches = sentencePattern.Matches(surface.Value.Text);
			matches.Should().NotBeEmpty(
				because: $"the {surface.Key} is expected to state the enforced floor; if that sentence was "
					+ "removed on purpose, remove the surface from this test in the same commit");
			foreach (System.Text.RegularExpressions.Match match in matches) {
				match.Groups[1].Value.Should().Be(enforced,
					because: $"the {surface.Key} tells the agent this clio requires {match.Groups[1].Value} while "
						+ $"[RequiresPackage] refuses below {enforced} - reading the wrong one, a caller either "
						+ "updates to a version that changes nothing or skips an update it needs");
			}
		}
	}

	[Test]
	[Description("The floor version is not credited with the change that justifies it. The sibling test pins WHICH version the floor sentence names; this one pins the REASON beside it, which nothing checked. The collapse - the package no longer validating a formula a second time - shipped in 1.4.0.41; .42 corrected the message that replaced the package's own reference pre-check; .44 is merely the first archive carrying both AND the ENG-96325 lookup-constant contract. Crediting the floor with the collapse describes an environment on .41 to .43 as still carrying a package wording and a reference pre-check that are already gone there, so a caller reads a refusal that does not match what they were promised and cannot tell which half is wrong.")]
	public void FloorSentences_ShouldNotCreditTheEnforcedFloorWithTheCollapse() {
		// Arrange
		const string collapse = "stopped validating formulas";
		const int precedingWindow = 60;
		var surfaces = new Dictionary<string, (string Text, Type OptionsType)> {
			["create-business-process description"] = (
				GetToolDescription(typeof(Clio.Command.McpServer.Tools.ProcessDesigner.CreateBusinessProcessTool)),
				typeof(Clio.Command.CreateBusinessProcessOptions)),
			["modify-business-process description"] = (
				GetToolDescription(typeof(Clio.Command.McpServer.Tools.ProcessDesigner.ModifyBusinessProcessTool)),
				typeof(Clio.Command.ModifyBusinessProcessOptions)),
			["McpCapabilityMap.md"] = (
				ReadRepositoryText(Path.Combine("docs", "McpCapabilityMap.md")),
				typeof(Clio.Command.ModifyBusinessProcessOptions))
		};

		// Act & Assert
		foreach (KeyValuePair<string, (string Text, Type OptionsType)> surface in surfaces) {
			string enforced = surface.Value.OptionsType
				.GetCustomAttributes(typeof(Clio.Common.RequiresPackageAttribute), inherit: false)
				.Cast<Clio.Common.RequiresPackageAttribute>()
				.Select(attribute => attribute.Version)
				.FirstOrDefault(version => !string.IsNullOrWhiteSpace(version));
			int at = surface.Value.Text.IndexOf(collapse, StringComparison.Ordinal);
			at.Should().BeGreaterThanOrEqualTo(0,
				because: $"the {surface.Key} explains its floor by what the package stopped doing, and a surface "
					+ "that dropped the explanation would pass the assertion below by carrying nothing - if it was "
					+ "removed on purpose, remove the surface from this test in the same commit");
			int from = Math.Max(0, at - precedingWindow);
			surface.Value.Text.Substring(from, at - from).Should().NotContain(enforced,
				because: $"the {surface.Key} would be naming {enforced} as the version where the package stopped "
					+ "validating formulas itself, and that was 1.4.0.41 - so an environment on .41 to .43 gets "
					+ "described with a package wording and a reference pre-check it no longer has");
		}
	}

	/// <summary>
	/// A version literal shipped on an agent-facing surface must be satisfiable by the archive this distribution
	/// carries. It may LAG it — a capability floor records when something first shipped and freezes there — but it
	/// may not exceed it: the surfaces carrying these literals also tell the agent to update the package when an
	/// environment is behind, and a number no clio-installable archive reaches turns that instruction into a dead
	/// end.
	/// </summary>
	private static void AssertInstallableFromThisDistribution(string literal, string surfaceKey) {
		Version named = Version.Parse(literal);
		var bundled = Version.Parse(ExpectedArchiveVersion);
		(named <= bundled).Should().BeTrue(
			because: $"the {surfaceKey} hands an agent '{literal}' as the version to be on and tells it to update "
				+ $"the package to get there, but this clio bundles {bundled} — so no archive it can install "
				+ "would ever satisfy the claim");
	}

	[Test]
	[Description("The producing-commit pin is a full commit id. It is written by the rebundle script from git rev-parse HEAD, so a value that is not 40 hex characters means it was hand-edited - which is the failure this pin exists to replace, and the one the prose above records happening three times.")]
	public void ExpectedProducingCommit_ShouldBeAFullCommitId() {
		// Arrange & Act & Assert
		ExpectedProducingCommit.Should().MatchRegex("^[0-9a-f]{40}$",
			because: "a short id, a branch name or a placeholder cannot be checked out by a reviewer, which is the "
				+ "only thing this pin is for");
	}

	/// <summary>The [Description] text of the type's MCP tool method (the agent-facing contract).</summary>
	private static string GetToolDescription(Type toolType) {
		System.Reflection.MethodInfo method = toolType
			.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
			.Single(candidate => candidate
				.GetCustomAttributes(true)
				.Any(attribute => attribute.GetType().Name == "McpServerToolAttribute"));
		var description = (System.ComponentModel.DescriptionAttribute)method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), true)
			.Single();
		return description.Description;
	}

	#endregion

	#region Nested types: Fixtures for the requirement collector

	// Deliberately local to this fixture rather than reusing a production options type: these exist to
	// exercise the COLLECTOR's two arms independently of which declaration form the product happens to use
	// today, so they keep working when that changes.
	private sealed class PropertyLevelRequirementFixture {

		[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "9.9.9.9")]
		public bool GatedOption { get; set; }

	}

	[RequiresPackage(BundledPackages.ProcessBuilderPackageName)]
	private class ClassLevelRequirementFixture { }

	private sealed class DerivedFromClassLevelRequirementFixture : ClassLevelRequirementFixture { }

	private sealed class OtherPackageRequirementFixture {

		[RequiresPackage("cliogate", "2.0.0.42")]
		public bool GatedOption { get; set; }

	}

	#endregion

}
