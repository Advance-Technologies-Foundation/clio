using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;
using IPath = System.IO.Abstractions.IPath;

namespace Clio.Tests.Command;

/// <summary>
/// Passthrough stand-in for the interprocess gate: runs the guarded work immediately and records which
/// sentinel it was asked to hold, and for how long. Recording the ENTRY is how a test proves the disk
/// touch is gated; recording the EXIT is how it proves the gate is not still held while clio talks to
/// Creatio — a lock held across a network round trip would serialise unrelated callers across processes,
/// which is the stall the worker execution boundary exists to remove.
/// </summary>
internal sealed class RecordingFileGate : IInterprocessFileGate {

	private readonly List<string> _entered = [];

	internal IReadOnlyList<string> EnteredLockPaths => _entered;

	internal int Depth { get; private set; }

	internal int MaxDepth { get; private set; }

	internal bool IsHeld => Depth > 0;

	public T Enter<T>(string lockFilePath, Func<T> action) {
		_entered.Add(lockFilePath);
		Depth++;
		MaxDepth = Math.Max(MaxDepth, Depth);
		try {
			return action();
		} finally {
			Depth--;
		}
	}

	public void Enter(string lockFilePath, Action action) =>
		Enter(lockFilePath, () => {
			action();
			return true;
		});
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageBaselineGuardTests {

	private const string SchemaName = "Usr_FormPage";
	private const string SchemaUId = "11111111-2222-3333-4444-555555555555";
	private const string OutputDirectory = "/ws";

	private MockFileSystem _fileSystem;
	private RecordingFileGate _fileGate;
	private PageBaselineGuard _guard;
	// Built through the same GetFullPath + Combine normalization the guard uses, so path comparisons
	// stay OS-agnostic (the Windows CI adds a drive prefix and uses backslashes; macOS/Linux do not).
	private string _metaPath;

	[SetUp]
	public void SetUp() {
		_fileSystem = new MockFileSystem();
		_fileGate = new RecordingFileGate();
		_guard = new PageBaselineGuard(_fileSystem, _fileGate);
		_metaPath = _fileSystem.Path.Combine(
			_fileSystem.Path.GetFullPath(OutputDirectory), ".clio-pages", SchemaName, "meta.json");
	}

	private void AddMetaWithBaseline(string environmentName, string checksum, bool editableExists = true) {
		_fileSystem.AddFile(_metaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-06-16T10:00:00Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Baseline = new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = environmentName,
				EditableSchemaExists = editableExists,
				EditableSchemaUId = editableExists ? SchemaUId : null,
				Checksum = checksum,
				ModifiedOn = "raw",
				CapturedAt = "2026-06-16T10:00:00Z"
			}
		})));
	}

	private void AddLegacyMetaWithoutBaseline() =>
		_fileSystem.AddFile(_metaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-06-16T10:00:00Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName }
		})));

	/// <summary>
	/// A file system whose path resolution refuses every input, which is the only way to reach the guard's
	/// discovery catch block: MockFileSystem resolves even malformed anchors instead of throwing.
	/// </summary>
	private static IFileSystem BuildFileSystemThatCannotResolvePaths() {
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IPath path = Substitute.For<IPath>();
		path.GetFullPath(Arg.Any<string>()).Returns(_ => throw new ArgumentException("the anchor is not of a legal form"));
		fileSystem.Path.Returns(path);
		return fileSystem;
	}

	private static PageUpdateOptions CreateOptions(string environment = "dev") =>
		new() { SchemaName = SchemaName, Body = "body", Environment = environment };

	[Test]
	[Description("TryArm must populate the expected-checksum/UId/absent options from a matching on-disk baseline and report armed.")]
	public void TryArm_ShouldPopulateExpectedFields_WhenBaselineMatchesEnvironment() {
		// Arrange
		AddMetaWithBaseline("dev", "checksum-1");
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		(string metaFilePath, bool armed, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeTrue(because: "a baseline captured against the same environment must arm the check");
		_fileSystem.Path.GetFullPath(metaFilePath).Should().Be(_fileSystem.Path.GetFullPath(_metaPath),
			because: "the guard must resolve the meta.json under the supplied output anchor");
		options.ExpectedChecksum.Should().Be("checksum-1", because: "the baseline checksum must drive the conflict comparison");
		options.ExpectedSchemaUId.Should().Be(SchemaUId, because: "the editable schema UId is part of the baseline identity");
		options.ExpectedSchemaAbsent.Should().BeFalse(because: "the baseline recorded an existing editable schema");
	}

	[Test]
	[Description("TryArm must NOT arm when the baseline was captured against a different environment.")]
	public void TryArm_ShouldNotArm_WhenBaselineEnvironmentDiffers() {
		// Arrange
		AddMetaWithBaseline("production", "checksum-1");
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		(_, bool armed, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "a baseline from another environment is not evidence of an external modification");
		options.ExpectedChecksum.Should().BeNull(because: "a foreign-environment baseline must not arm the check");
	}

	[Test]
	[Description("TryArm must NOT arm when no meta.json exists for the schema.")]
	public void TryArm_ShouldNotArm_WhenMetaMissing() {
		// Arrange — no meta.json added.
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		(_, bool armed, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "a missing baseline must fail toward no check");
		options.ExpectedChecksum.Should().BeNull(because: "there is no baseline to arm from");
	}

	[Test]
	[Description("TryArm must NOT arm when the meta.json is legacy (carries no baseline block).")]
	public void TryArm_ShouldNotArm_WhenLegacyMetaHasNoBaseline() {
		// Arrange
		AddLegacyMetaWithoutBaseline();
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		(_, bool armed, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "a legacy meta.json without a baseline block must skip the check");
	}

	[Test]
	[Description("TryArm must keep an explicit --expected-checksum untouched yet report armed so a matching on-disk baseline is refreshed after the save.")]
	public void TryArm_ShouldArmRefreshButKeepExplicitChecksum_WhenBaselineMatchesEnvironment() {
		// Arrange
		AddMetaWithBaseline("dev", "disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "manual-checksum";

		// Act
		(string metaFilePath, bool armed, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeTrue(
			because: "the matching on-disk baseline must move forward after the save even when the checksum was pinned, else the next unpinned save would raise a false conflict");
		_fileSystem.Path.GetFullPath(metaFilePath).Should().Be(_fileSystem.Path.GetFullPath(_metaPath),
			because: "the meta.json must be resolved so RefreshOrDrop can rewrite it");
		options.ExpectedChecksum.Should().Be("manual-checksum",
			because: "the explicit CLI --expected-checksum wins the comparison and must not be overwritten by the on-disk baseline");
	}

	[Test]
	[Description("TryArm must normalize a padded caller-pinned checksum so the strict Ordinal comparison downstream cannot fail on whitespace alone.")]
	public void TryArm_ShouldTrimTheCallerPinnedChecksum_WhenItArrivesPadded() {
		// Arrange — the shape a CLI caller produces when the value is piped from a file or from a shell
		// substitution that keeps the trailing newline. TryArm's own arming predicate is whitespace-
		// tolerant (IsNullOrWhiteSpace), but TryCheckForExternalModification compares with
		// StringComparison.Ordinal, so an untrimmed pin arms the guard and then reports a ChecksumMismatch
		// that never happened. The MCP mapper trims its argument; --expected-checksum is bound verbatim by
		// CommandLineParser, so the normalization has to live here, at the shared choke point.
		AddMetaWithBaseline("dev", "disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "  manual-checksum\n";

		// Act
		(_, bool armed, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		options.ExpectedChecksum.Should().Be("manual-checksum",
			because: "the pin must reach the Ordinal comparison already normalized, or padding alone produces a false conflict");
		armed.Should().BeTrue(
			because: "trimming must not change whether the matching on-disk baseline is refreshed after the save");
	}

	[Test]
	[Description("TryArm must treat a whitespace-only caller-pinned checksum as not supplied rather than arming the guard with nothing to compare.")]
	public void TryArm_ShouldTreatAWhitespaceOnlyCallerChecksumAsNotSupplied() {
		// Arrange
		AddMetaWithBaseline("dev", "disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "   ";

		// Act
		_guard.TryArm(options, OutputDirectory);

		// Assert
		options.ExpectedChecksum.Should().Be("disk-checksum",
			because: "whitespace-only is equivalent to no pin, so the on-disk baseline must supply the comparison value instead of an unusable blank");
	}

	[Test]
	[Description("TryArm must NOT arm when --expected-checksum is pinned but no matching on-disk baseline exists, so nothing is refreshed.")]
	public void TryArm_ShouldNotArm_WhenExplicitChecksumSetAndNoBaseline() {
		// Arrange — no meta.json on disk.
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "manual-checksum";

		// Act
		(_, bool armed, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "with no on-disk baseline there is nothing to move forward");
		options.ExpectedChecksum.Should().Be("manual-checksum",
			because: "the explicit CLI --expected-checksum must remain the comparison value");
	}

	[Test]
	[Description("An explicit --expected-checksum save must still move the on-disk baseline forward to the post-save checksum so the next unpinned save does not raise a false conflict.")]
	public void TryArmThenRefreshOrDrop_ShouldMoveBaselineForward_WhenExplicitChecksumPinned() {
		// Arrange
		AddMetaWithBaseline("dev", "pre-save-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "pre-save-checksum";

		// Act — arm with the pinned checksum, then refresh as PageUpdateCommand.Execute does after a save.
		(string metaFilePath, bool armed, _) = _guard.TryArm(options, OutputDirectory);
		armed.Should().BeTrue(because: "a matching on-disk baseline must arm the post-save refresh on the explicit-checksum path");
		_guard.RefreshOrDrop(metaFilePath, options, new PageUpdateResponse {
			Success = true,
			SavedSchemaUId = SchemaUId,
			NewChecksum = "post-save-checksum",
			NewModifiedOn = "fresh-modified"
		});

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(_metaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("post-save-checksum",
			because: "after an explicit-checksum save the on-disk baseline must point at the new checksum, not the overwritten one");
	}

	[Test]
	[Description("RefreshOrDrop must rewrite the baseline checksum with the post-save value while preserving the get-page snapshot fields.")]
	public void RefreshOrDrop_ShouldRefreshChecksum_WhenNewChecksumPresent() {
		// Arrange
		AddMetaWithBaseline("dev", "old-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		PageUpdateResponse response = new() {
			Success = true,
			SavedSchemaUId = SchemaUId,
			NewChecksum = "fresh-checksum",
			NewModifiedOn = "fresh-modified"
		};

		// Act
		_guard.RefreshOrDrop(_metaPath, options, response);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(_metaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("fresh-checksum",
			because: "consecutive CLI updates must compare against the post-save checksum, not the original");
		meta.Baseline.EnvironmentName.Should().Be("dev", because: "the environment identity must be recorded for the env-guard");
		meta.FetchedAt.Should().Be("2026-06-16T10:00:00Z", because: "the refresh must not touch the get-page snapshot fields");
	}

	[Test]
	[Description("RefreshOrDrop must delete the baseline when the post-save checksum is unavailable, so the next write skips the check.")]
	public void RefreshOrDrop_ShouldDeleteBaseline_WhenNewChecksumBlank() {
		// Arrange
		AddMetaWithBaseline("dev", "old-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		PageUpdateResponse response = new() { Success = true, SavedSchemaUId = SchemaUId, NewChecksum = null };

		// Act
		_guard.RefreshOrDrop(_metaPath, options, response);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(_metaPath).TextContents);
		meta.Baseline.Should().BeNull(
			because: "a stale baseline must be removed when fresh metadata could not be obtained (fail toward no-check)");
	}

	[Test]
	[Description("TryArm_ShouldKeepTheCallerChecksumAndNotArmSchemaIdentityFromDisk_WhenTheCallerPinnedAChecksum — the checksum comparison runs against the RESOLVED target schema, so a matching pin already proves the caller read that schema; a disk-derived schema UId is the weaker witness and must not veto a pin the server corroborates with schema-uid-mismatch (PR #1356 gate-3 review of issue #1320).")]
	public void TryArm_ShouldKeepTheCallerChecksumAndNotArmSchemaIdentityFromDisk_WhenTheCallerPinnedAChecksum() {
		// Arrange
		AddMetaWithBaseline("dev", "caller-pinned-checksum");
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "caller-pinned-checksum";

		// Act
		(string metaFilePath, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeTrue(
			"because the matching on-disk baseline must still be refreshed after the save");
		metaFilePath.Should().Be(_metaPath,
			"because the guard must report the baseline it resolved");
		options.ExpectedChecksum.Should().Be("caller-pinned-checksum",
			"because the caller-supplied checksum is the authoritative conflict baseline and must not be overwritten from disk");
		options.ExpectedSchemaUId.Should().BeNull(
			"because the pin is compared against the resolved target schema's own checksum, which already proves the caller read that schema - a stale on-disk UId must not turn a server-corroborated pin into a schema-uid-mismatch");
		options.ExpectedSchemaAbsent.Should().BeFalse(
			"because the baseline recorded an existing editable schema");
		warning.Should().BeNull(
			"because a readable, matching baseline whose checksum AGREES with the pin is the normal path and must not report anything");
	}

	[Test]
	[Description("TryArm_ShouldCarryTheBaselineAsConditional_WhenTargetPackageUIdIsSupplied — a selector is not proof of a redirect. The .clio-pages baseline is keyed by schema name and get-page has no redirect option, so it cannot describe a DIFFERENT target; but naming the package that already owns the schema resolves to the very schema the baseline describes, and dropping it there let a stale body overwrite a concurrent writer with success: true (PR #1356 review). Nothing is armed here, because the target is resolved after the guard runs — the baseline travels as a conditional one instead.")]
	public void TryArm_ShouldCarryTheBaselineAsConditional_WhenTargetPackageUIdIsSupplied() {
		// Arrange
		AddMetaWithBaseline("dev", "disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.TargetPackageUId = "99999999-8888-7777-6666-555555555555";

		// Act
		(string metaFilePath, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(
			because: "reporting armed unconditionally would let RefreshOrDrop write a redirected schema's UId and checksum into the baseline keyed by the schema NAME; the refresh is decided later, only if the resolved target matched");
		options.ExpectedChecksum.Should().BeNull(because: "nothing may be armed before the target is resolved");
		options.ExpectedSchemaUId.Should().BeNull(because: "nothing may be armed before the target is resolved");
		options.ExpectedSchemaAbsent.Should().BeFalse(because: "nothing may be armed before the target is resolved");
		options.ConditionalBaselineSchemaUId.Should().Be(SchemaUId,
			because: "the baseline still applies if the selector resolves to the schema it was captured for, and only the caller of the resolved target can tell");
		options.ConditionalBaselineChecksum.Should().Be("disk-checksum",
			because: "the checksum is what protects the unpinned caller against a concurrent writer");
		options.ConditionalBaselineSchemaAbsent.Should().BeFalse(because: "the baseline recorded an existing editable schema");
		_fileSystem.Path.GetFullPath(metaFilePath).Should().Be(_fileSystem.Path.GetFullPath(_metaPath),
			because: "the refresh that follows a promoted baseline needs the path the baseline was read from");
		warning.Should().NotBeNull(because: "a save whose check depends on the resolved target must say so");
		warning.Should().Contain("target-package-uid",
			because: "the trace has to name the option that made the baseline conditional");
	}

	[Test]
	[Description("A --target-schema-uid redirect KEEPS a caller-supplied checksum. TryResolveContext sets EditableSchemaUId from that option with IsCreateReplacing false, so the comparison runs against exactly the schema the caller pinned — clearing it turned external-modification detection off on a destructive write and still reported success: true / conflict: false, which is the failure this guard exists to prevent.")]
	public void TryArm_ShouldKeepThePinAndWarn_WhenTheWriteIsRedirectedByTargetSchemaUId() {
		// Arrange
		AddMetaWithBaseline("dev", "disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "caller-pinned-checksum";
		options.TargetSchemaUId = "99999999-8888-7777-6666-444444444444";

		// Act
		(string metaFilePath, bool refreshBaseline, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		options.ExpectedChecksum.Should().Be("caller-pinned-checksum",
			because: "--target-schema-uid names the write target outright, so the pin describes exactly the schema being written and must still govern the save");
		refreshBaseline.Should().BeFalse(
			because: "the schema-name-keyed baseline describes the auto-resolved schema, so it must not be moved forward from a redirected write");
		metaFilePath.Should().Be(_metaPath,
			because: "the path is reported so a refresh can still run if the resolved target turns out to BE the baseline's schema; whether it runs is decided after resolution, not here (issue #1538)");
		options.ConditionalBaselineSchemaUId.Should().Be(SchemaUId,
			because: "the recorded identity is what that later decision compares the resolved target against");
		options.ExpectedSchemaUId.Should().BeNull(because: "the disk identity describes the auto-resolved schema");
		options.ExpectedSchemaAbsent.Should().BeFalse(because: "a stale on-disk absence marker cannot veto a redirected write");
		_fileGate.EnteredLockPaths.Should().HaveCount(1,
			because: "the baseline is now read under its lock even for a pinned selector save - its checksum still arms nothing, but its schema identity is the only way to tell a same-target save from a genuine redirect afterwards (issue #1538); a SECOND acquisition would mean the read was split and could interleave with another writer");
		warning.Should().NotBeNull(
			because: "a pinned selector save must leave a trace explaining which baseline decision was taken");
		warning.Should().Contain("refreshed afterwards",
			because: "with a readable baseline the trace must say it is consulted only for the refresh decision, never as a second conflict check (issue #1538)");
		warning.Should().Contain("still compared",
			because: "the caller must keep being told the pin itself remains an active guard against the resolved target - widening the read must not cost that statement");
		warning.Should().NotContain("was ignored",
			because: "the pin is NOT ignored on this path any more - saying so would describe the very fail-open this change removed");
	}

	[Test]
	[Description("The redirect must clear the schema-absent marker even when the baseline actually recorded an absent editable schema. Asserting BeFalse on a baseline built with editableSchemaExists: true holds on the normal path too, so it cannot fail on the mutation it claims to pin.")]
	public void TryArm_ShouldClearTheSchemaAbsentMarker_WhenTheWriteIsRedirectedAndTheBaselineRecordedNoEditableSchema() {
		// Arrange
		AddMetaWithBaseline("dev", "disk-checksum", editableExists: false);
		PageUpdateOptions options = CreateOptions("dev");
		options.TargetSchemaUId = "99999999-8888-7777-6666-444444444444";

		// Act
		(_, bool refreshBaseline, _) = _guard.TryArm(options, OutputDirectory);

		// Assert
		options.ExpectedSchemaAbsent.Should().BeFalse(
			because: "the recorded absence describes the auto-resolved schema, so carrying it into a redirected write would refuse the save as schema-created-externally on a schema the baseline never described");
		refreshBaseline.Should().BeFalse(
			because: "a redirected write must not move the schema-name-keyed baseline forward");
	}

	[Test]
	[Description("A --target-package-uid redirect keeps a caller-supplied checksum: the resolved target is checked after hierarchy resolution, so an existing same-package target remains protected and a pin from another target fails safe with a checksum conflict. With a readable on-disk baseline the guard ALSO carries that baseline's schema identity, so a successful same-target save can refresh it (issue #1538).")]
	public void TryArm_ShouldKeepThePinAndCarryTheBaselineIdentity_WhenTheWriteIsRedirectedByTargetPackageUIdOnly() {
		// Arrange
		AddMetaWithBaseline("dev", "disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "caller-pinned-checksum";
		options.TargetPackageUId = "11111111-2222-3333-4444-555555555555";

		// Act
		(string metaFilePath, bool refreshBaseline, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		options.ExpectedChecksum.Should().Be("caller-pinned-checksum",
			because: "the resolved target is the only authoritative comparison surface, so dropping the pin would allow a same-package target to overwrite a concurrent edit");
		options.ConditionalBaselineSchemaUId.Should().Be(SchemaUId,
			because: "the command can only decide whether this write landed on the baseline's own page if the guard hands the recorded identity over");
		options.ConditionalBaselineChecksum.Should().BeNull(
			because: "a pinned save must not gain a second, disk-derived conflict witness - the pin alone governs the check");
		metaFilePath.Should().Be(_metaPath,
			because: "a refresh decided after resolution still needs the file to write, and returning null here is exactly what left the baseline stale");
		refreshBaseline.Should().BeFalse(
			because: "the target is resolved after the guard runs, so an unconditional refresh would stamp a redirected schema into the schema-name-keyed baseline");
		warning.Should().Contain("refreshed afterwards",
			because: "the trace must say the disk baseline is consulted only for the refresh decision, never as a second guard");
	}

	[Test]
	[Description("End-to-end for issue #1538: TryArm -> the command's target match -> RefreshOrDrop. A PINNED save whose selector resolves to the schema the baseline describes must leave meta.json holding the POST-SAVE checksum, or the caller's next unpinned save conflicts with its own previous save. Both halves can be correct in isolation while the path and the flag drift apart, which is why this is asserted through the file, not through the flag.")]
	public void TryArmThenRefreshOrDrop_ShouldMoveBaselineForward_WhenAPinnedSelectorSaveResolvesToTheBaselineSchema() {
		// Arrange
		AddMetaWithBaseline("dev", "pre-save-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "pre-save-checksum";
		options.TargetPackageUId = "99999999-8888-7777-6666-555555555555";

		// Act — arm, then stand in for the command's PromoteConditionalBaselineWhenTargetMatches deciding
		// that the resolved target IS the schema the carried identity names, then refresh as the writers do.
		(string metaFilePath, bool refreshBaseline, _) = _guard.TryArm(options, OutputDirectory);
		refreshBaseline.Should().BeFalse(because: "the guard cannot decide this before the target is resolved");
		options.ConditionalBaselineSchemaUId.Should().Be(SchemaUId,
			because: "the identity the command compares the resolved target against has to survive the guard");
		_guard.RefreshOrDrop(metaFilePath, options, new PageUpdateResponse {
			Success = true,
			SavedSchemaUId = SchemaUId,
			NewChecksum = "post-save-checksum",
			NewModifiedOn = "fresh-modified"
		});

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(_metaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("post-save-checksum",
			because: "this is the symptom issue #1538 reported: the baseline kept the superseded checksum and the next unpinned save was refused against the caller's own write");
		meta.Baseline.EditableSchemaUId.Should().Be(SchemaUId,
			because: "the refreshed baseline must still describe the schema it was keyed to, not a redirected one");
	}

	[Test]
	[Description("The writer-level twin of the test above (issue #1538 AC-2): after this change metaFilePath is no longer null on the pinned selector path, so the boolean match is the ONLY thing standing between a genuine redirect and a corrupted schema-name-keyed baseline. A resolved target that is NOT the baseline's schema must leave meta.json byte-identical.")]
	public void TryArmThenNoRefresh_ShouldLeaveTheBaselineUntouched_WhenAPinnedSelectorSaveResolvesElsewhere() {
		// Arrange
		AddMetaWithBaseline("dev", "pre-save-checksum");
		string before = _fileSystem.GetFile(_metaPath).TextContents;
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "pre-save-checksum";
		options.TargetPackageUId = "99999999-8888-7777-6666-555555555555";

		// Act — arm, then stand in for the command resolving to a DIFFERENT schema, which leaves
		// ConditionalBaselineApplied false and so never calls RefreshOrDrop.
		(_, bool refreshBaseline, _) = _guard.TryArm(options, OutputDirectory);
		options.ConditionalBaselineApplied.Should().BeFalse(
			because: "nothing has matched the carried identity, so no writer may refresh from this save");

		// Assert
		refreshBaseline.Should().BeFalse(because: "an unconditional refresh here is exactly the redirect corruption the guard exists to prevent");
		_fileSystem.GetFile(_metaPath).TextContents.Should().Be(before,
			because: "a redirected write must not move the baseline of the automatically resolved schema by even one field");
	}

	[Test]
	[Description("A pinned selector save must surface the corrupt-meta.json read warning the non-selector path already accumulates. Widening the read to the pinned path widened the set of callers who would otherwise get silence about a meta.json that exists but cannot be deserialized.")]
	public void TryArm_ShouldSurfaceTheReadWarning_WhenAPinnedSelectorSaveHitsUnreadableMeta() {
		// Arrange
		_fileSystem.AddFile(_metaPath, new MockFileData("{ this is not json"));
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "caller-pinned-checksum";
		options.TargetPackageUId = "99999999-8888-7777-6666-555555555555";

		// Act
		(_, _, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		warning.Should().NotBeNull(
			because: "a meta.json that exists but cannot be read is a fact about the caller's workspace, not an internal detail");
		warning.Should().Contain("meta.json",
			because: "the trace has to name the file the caller has to look at");
	}

	[Test]
	[Description("The pinned redirect must keep saying the pin is uncorroborated when NO on-disk baseline is readable - the non-vacuity twin of the test above, so widening the read does not quietly change the trace for the case it does not cover.")]
	public void TryArm_ShouldReportThePinUncorroborated_WhenRedirectedAndNoBaselineIsReadable() {
		// Arrange
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "caller-pinned-checksum";
		options.TargetPackageUId = "11111111-2222-3333-4444-555555555555";

		// Act
		(string metaFilePath, bool refreshBaseline, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		options.ExpectedChecksum.Should().Be("caller-pinned-checksum",
			because: "an absent baseline changes nothing about the caller's own pin");
		options.ConditionalBaselineSchemaUId.Should().BeNull(
			because: "there is no recorded identity to hand over, so no refresh may be decided from one");
		metaFilePath.Should().BeNull(because: "there is no baseline file to refresh");
		refreshBaseline.Should().BeFalse(because: "nothing governs a redirected write, so nothing may be moved forward after it");
		warning.Should().Contain("still compared",
			because: "the trace must make clear that the explicit pin remains an active guard even though no disk baseline corroborates it");
	}

	[Test]
	[Description("TryArm warns when the pinned checksum disagrees with the recorded baseline — the conflict-response bypass (copy actualChecksum, resubmit the same body) is otherwise byte-identical on the wire to a legitimate up-to-date save, and RefreshOrDrop then erases the only local record that the pin ever diverged (PR #1356 gate-3 review).")]
	public void TryArm_ShouldWarn_WhenThePinnedChecksumDiffersFromTheRecordedBaseline() {
		// Arrange
		AddMetaWithBaseline("dev", "on-disk-checksum");
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "caller-pinned-checksum";

		// Act
		(_, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeTrue(
			"because the divergence is reported, not enforced — the pin still wins the comparison");
		options.ExpectedChecksum.Should().Be("caller-pinned-checksum",
			"because a warning must not change which checksum is authoritative");
		warning.Should().NotBeNull(
			"because a caller that took the conflict-response bypass and one that made a legitimate save must not be indistinguishable");
		warning.Should().Contain("differs from the baseline",
			"because the trace has to name what diverged");
	}

	[Test]
	[Description("TryArm_ShouldWarn_WhenThePinIsUncorroboratedAndNoBaselineExists — the pin GOVERNS the save whatever TryArm reports (TryCheckForExternalModification gates on ExpectedChecksum alone and never consults armed), so a pinned overwrite reached the server with no trace at all whenever no local baseline was found for the anchor (PR #1356 gate-3 review).")]
	public void TryArm_ShouldWarn_WhenThePinIsUncorroboratedAndNoBaselineExists() {
		// Arrange — no meta.json on disk, exactly as a fresh workspace or a different cwd produces.
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "caller-pinned-checksum";

		// Act
		(_, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "with no on-disk baseline there is still nothing to move forward");
		options.ExpectedChecksum.Should().Be("caller-pinned-checksum",
			because: "the trace must not change which checksum is authoritative");
		warning.Should().NotBeNull(
			because: "a pinned overwrite that could not be corroborated locally must not be byte-identical to a clean save");
		warning.Should().Contain("could not be corroborated",
			because: "the trace has to say that the pin came from the caller and nothing local backs it");
	}

	[Test]
	[Description("TryArm_ShouldWarn_WhenThePinIsUncorroboratedAndTheBaselineEnvironmentDiffers — an update-page invoked with an explicit --uri/--login, or against another environment name, cannot satisfy MatchesEnvironment, which is the second documented-normal way the pinned path used to leave no trace.")]
	public void TryArm_ShouldWarn_WhenThePinIsUncorroboratedAndTheBaselineEnvironmentDiffers() {
		// Arrange
		AddMetaWithBaseline("other-env", "on-disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "caller-pinned-checksum";

		// Act
		(_, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "a baseline captured elsewhere must not be moved forward by this save");
		warning.Should().NotBeNull(
			because: "the pin still governs the comparison, so the save must carry a machine-readable trace");
		warning.Should().Contain("could not be corroborated",
			because: "the trace has to state that no local baseline backs the caller's pin");
	}

	[Test]
	[Description("TryArm_ShouldWarnAboutBothTheCorruptBaselineAndTheUncorroboratedPin_WhenTheMetaCannotBeRead — the two traces describe different facts and co-occur: TryReadBaseline sets its warning only when meta.json EXISTS but cannot be read, and on that path it also returns a null baseline, so a single `??=` slot dropped the pinned-save trace in exactly the case where the pin is least trustworthy (PR #1356 gate-3 re-review).")]
	public void TryArm_ShouldWarnAboutBothTheCorruptBaselineAndTheUncorroboratedPin_WhenTheMetaCannotBeRead() {
		// Arrange — a meta.json that exists but does not deserialize, plus a caller pin.
		_fileSystem.AddFile(_metaPath, new MockFileData("not-json{{{"));
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "caller-pinned-checksum";

		// Act
		(_, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "an unparseable baseline must fail toward no-check, never block the write");
		warning.Should().NotBeNull();
		warning.Should().Contain("could not be corroborated",
			because: "the pin still governs the comparison here, so losing this trace hides an uncorroborated overwrite behind an unrelated parse error");
		warning.Should().Contain("baseline",
			because: "the corrupt-baseline fact must survive alongside the pin trace rather than be replaced by it");
	}

	[Test]
	[Description("TryArm_ShouldNotClaimDetectionIsDisarmed_WhenTheAnchorCannotBeResolvedOnAPinnedSave — TryCheckForExternalModification gates on ExpectedChecksum alone and never consults Armed, so on a pinned save detection IS running; telling the caller it is off is not merely unhelpful but affirmatively false (PR #1356 gate-3 re-review).")]
	public void TryArm_ShouldNotClaimDetectionIsDisarmed_WhenTheAnchorCannotBeResolvedOnAPinnedSave() {
		// Arrange - a file system whose path resolution throws, which is the only way into the guard's
		// catch block; MockFileSystem resolves even malformed anchors rather than refusing them.
		PageBaselineGuard guard = new(BuildFileSystemThatCannotResolvePaths(), _fileGate);
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "caller-pinned-checksum";

		// Act
		(_, bool armed, string warning) = guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "nothing local was discovered, so nothing can be moved forward");
		warning.Should().NotBeNull();
		warning.Should().NotContain("DISARMED",
			because: "the pin governs the comparison on this path, so the save IS checked - against a baseline nothing local backs");
		warning.Should().Contain("could not be corroborated",
			because: "that is the accurate statement about a pin no local baseline can confirm");
	}

	[Test]
	[Description("Non-vacuity twin: with no pin there is genuinely nothing driving the comparison, so the unresolvable anchor must keep saying DISARMED - suppressing the wording unconditionally would trade one false statement for another.")]
	public void TryArm_ShouldStillSayDisarmed_WhenTheAnchorCannotBeResolvedAndNothingWasPinned() {
		// Arrange
		PageBaselineGuard guard = new(BuildFileSystemThatCannotResolvePaths(), _fileGate);
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		(_, bool armed, string warning) = guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse();
		warning.Should().Contain("DISARMED",
			because: "an unpinned save really does reach the server with no external-modification check at all");
	}

	[Test]
	[Description("TryArm_ShouldNotWarnAboutCorroboration_WhenNoChecksumWasPinned — the trace is about a CALLER pin; an ordinary unpinned save with no baseline is not an overwrite of anything and must stay quiet.")]
	public void TryArm_ShouldNotWarnAboutCorroboration_WhenNoChecksumWasPinned() {
		// Arrange — no meta.json on disk and no pin.
		PageUpdateOptions options = CreateOptions();

		// Act
		(_, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "there is no baseline to arm from");
		(warning ?? string.Empty).Should().NotContain("could not be corroborated",
			because: "warning on every first save of a page would make the trace worthless noise");
	}

	// ---------------------------------------------------------------------------------------------
	// ENG-95262 H-1: every meta.json touch runs under the schema's interprocess sentinel, and the
	// sentinel is released before clio talks to Creatio.
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Description("TryArm must read the baseline under the schema's interprocess gate, keyed on a sentinel that sits outside the get-page-deleted schema directory.")]
	public void TryArm_ShouldEnterTheSchemaGate_WhenReadingTheBaseline() {
		// Arrange
		AddMetaWithBaseline("dev", "checksum-1");
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		_guard.TryArm(options, OutputDirectory);

		// Assert
		_fileGate.EnteredLockPaths.Should().HaveCount(1,
			because: "the baseline read is one disk touch and must be gated exactly once — a second acquisition would mean the read was split and could interleave");
		string lockPath = _fileGate.EnteredLockPaths[0];
		_fileSystem.Path.GetFileName(lockPath).Should().Be($"{SchemaName}.lock",
			because: "the sentinel is per schema so unrelated pages never wait on each other");
		_fileSystem.Path.GetFullPath(lockPath).Should().NotStartWith(
			_fileSystem.Path.GetFullPath(_fileSystem.Path.GetDirectoryName(_metaPath)),
			because: "get-page deletes .clio-pages/{schema}/ recursively, so a sentinel inside it would be destroyed under its holder");
	}

	[Test]
	[Description("RefreshOrDrop must perform its whole read-merge-write inside ONE gate acquisition, so a concurrent writer cannot slip between the read and the write and lose its own update.")]
	public void RefreshOrDrop_ShouldHoldTheGateAcrossTheWholeReadModifyWrite_WhenRefreshing() {
		// Arrange
		AddMetaWithBaseline("dev", "old-checksum");
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		_guard.RefreshOrDrop(_metaPath, options, new PageUpdateResponse {
			Success = true, SavedSchemaUId = SchemaUId, NewChecksum = "fresh", NewModifiedOn = "m"
		});

		// Assert
		_fileGate.EnteredLockPaths.Should().HaveCount(1,
			because: "the read, the merge and the write are one indivisible unit; two acquisitions would reopen the lost-update window between them");
		_fileGate.EnteredLockPaths.Distinct().Should().HaveCount(1,
			because: "the whole sequence must be guarded by the same per-schema sentinel");
	}

	[Test]
	[Description("The gate must be released before the caller reaches Creatio: holding it across a network round trip would serialise unrelated callers across processes, which is the stall this design removes.")]
	public void TryArmAndRefreshOrDrop_ShouldNotHoldTheGate_WhenTheCreatioRoundTripRuns() {
		// Arrange
		AddMetaWithBaseline("dev", "checksum-1");
		PageUpdateOptions options = CreateOptions("dev");

		// Act — the exact sequence every caller runs: arm, then the save (simulated here), then refresh.
		(string metaFilePath, bool armed, _) = _guard.TryArm(options, OutputDirectory);
		bool heldDuringSave = _fileGate.IsHeld;
		_guard.RefreshOrDrop(metaFilePath, options, new PageUpdateResponse {
			Success = true, SavedSchemaUId = SchemaUId, NewChecksum = "fresh", NewModifiedOn = "m"
		});

		// Assert
		armed.Should().BeTrue(because: "the matching baseline must arm the check for this scenario to be the real one");
		heldDuringSave.Should().BeFalse(
			because: "between TryArm and RefreshOrDrop the caller performs the Creatio save; a cross-process lock held across that would rebuild the head-of-line stall in a place no monitor can bound, and a budget kill mid-round-trip would strand it");
		_fileGate.IsHeld.Should().BeFalse(because: "the gate must be released once the refresh returns");
		_fileGate.MaxDepth.Should().Be(1,
			because: "no acquisition should ever nest more than one level deep on this path");
	}

	[Test]
	[Description("TryArm must report a warning that conflict detection is disarmed when an existing meta.json cannot be parsed, instead of silently proceeding without a check.")]
	public void TryArm_ShouldReturnDisarmedWarning_WhenMetaIsCorrupt() {
		// Arrange
		_fileSystem.AddFile(_metaPath, new MockFileData("not-json{{{"));
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		(_, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "an unparseable baseline must fail toward no-check, never block the write");
		warning.Should().NotBeNull(
			because: "proceeding without external-modification detection is a fact the caller needs; the old code made it indistinguishable from having no baseline at all");
	}

	[Test]
	[Description("TryArm must stay silent when no baseline exists — the ordinary state of a page that was never fetched.")]
	public void TryArm_ShouldNotReturnWarning_WhenMetaMissing() {
		// Arrange — no meta.json.
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		(_, bool armed, string warning) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "there is no baseline to arm from");
		warning.Should().BeNull(
			because: "warning on every un-fetched page would make the channel noise and train callers to ignore it");
	}

	[Test]
	[Description("TryArm must NOT materialise a .clio-pages tree (not even the gate's .locks directory) when the page has no baseline, so an update-page run outside a page workspace leaves no litter behind.")]
	public void TryArm_ShouldNotCreateAnyClioPagesDirectory_WhenMetaMissing() {
		// Arrange — no meta.json, and nothing under the anchor at all.
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		_guard.TryArm(options, OutputDirectory);

		// Assert
		_fileGate.EnteredLockPaths.Should().BeEmpty(
			because: "taking the gate would create its .locks directory; a lookup for a baseline that was never captured must not write anything");
		_fileSystem.AllDirectories.Should().NotContain(directory => directory.Contains(".clio-pages", StringComparison.Ordinal),
			because: "the store promises never to create .clio-pages on the read/write path, and a stray directory would show up in the user's git status");
	}

	[Test]
	[Description("RefreshOrDrop must return null when the refresh landed, so the caller adds no warning to a clean save.")]
	public void RefreshOrDrop_ShouldReturnNull_WhenRefreshSucceeds() {
		// Arrange
		AddMetaWithBaseline("dev", "old-checksum");
		PageUpdateOptions options = CreateOptions("dev");

		// Act
		string warning = _guard.RefreshOrDrop(_metaPath, options, new PageUpdateResponse {
			Success = true, SavedSchemaUId = SchemaUId, NewChecksum = "fresh", NewModifiedOn = "m"
		});

		// Assert
		warning.Should().BeNull(because: "a successful refresh must not decorate a clean response with a warning");
	}
}
