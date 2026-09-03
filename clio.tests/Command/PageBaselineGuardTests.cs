using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

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
	[Description("TryArm_ShouldKeepTheCallerChecksumButStillArmSchemaIdentity_WhenTheCallerPinnedAChecksum — pinning a checksum says nothing about schema identity, so dropping the baseline's schema UId and absent marker would silently disable the schema-uid-mismatch and schema-created-externally conflicts on the pinned path (issue #1320).")]
	public void TryArm_ShouldKeepTheCallerChecksumButStillArmSchemaIdentity_WhenTheCallerPinnedAChecksum() {
		// Arrange
		AddMetaWithBaseline("dev", "on-disk-checksum");
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
		options.ExpectedSchemaUId.Should().Be(SchemaUId,
			"because the schema-identity half of the baseline must stay armed so a schema-uid mismatch is still detected");
		options.ExpectedSchemaAbsent.Should().BeFalse(
			"because the baseline recorded an existing editable schema");
		warning.Should().BeNull(
			"because a readable, matching baseline is the normal path and must not report the check as disarmed");
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
