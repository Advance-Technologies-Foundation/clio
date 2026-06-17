using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageBaselineGuardTests {

	private const string SchemaName = "Usr_FormPage";
	private const string SchemaUId = "11111111-2222-3333-4444-555555555555";
	private const string OutputDirectory = "/ws";

	private MockFileSystem _fileSystem;
	private PageBaselineGuard _guard;
	// Built through the same GetFullPath + Combine normalization the guard uses, so path comparisons
	// stay OS-agnostic (the Windows CI adds a drive prefix and uses backslashes; macOS/Linux do not).
	private string _metaPath;

	[SetUp]
	public void SetUp() {
		_fileSystem = new MockFileSystem();
		_guard = new PageBaselineGuard(_fileSystem);
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
		(string metaFilePath, bool armed) = _guard.TryArm(options, OutputDirectory);

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
		(_, bool armed) = _guard.TryArm(options, OutputDirectory);

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
		(_, bool armed) = _guard.TryArm(options, OutputDirectory);

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
		(_, bool armed) = _guard.TryArm(options, OutputDirectory);

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
		(string metaFilePath, bool armed) = _guard.TryArm(options, OutputDirectory);

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
		(_, bool armed) = _guard.TryArm(options, OutputDirectory);

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
		(string metaFilePath, bool armed) = _guard.TryArm(options, OutputDirectory);
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
}
