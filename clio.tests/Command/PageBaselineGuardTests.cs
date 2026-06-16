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
	private const string MetaPath = "/ws/.clio-pages/Usr_FormPage/meta.json";

	private MockFileSystem _fileSystem;
	private PageBaselineGuard _guard;

	[SetUp]
	public void SetUp() {
		_fileSystem = new MockFileSystem();
		_guard = new PageBaselineGuard(_fileSystem);
	}

	private void AddMetaWithBaseline(string environmentName, string checksum, bool editableExists = true) {
		_fileSystem.AddFile(MetaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
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
		_fileSystem.AddFile(MetaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
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
		metaFilePath.Should().Be(MetaPath, because: "the guard must resolve the meta.json under the supplied output anchor");
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
	[Description("TryArm must honor an explicit --expected-checksum and leave it untouched instead of overwriting it from disk.")]
	public void TryArm_ShouldRespectExplicitChecksum_WhenAlreadySet() {
		// Arrange
		AddMetaWithBaseline("dev", "disk-checksum");
		PageUpdateOptions options = CreateOptions("dev");
		options.ExpectedChecksum = "manual-checksum";

		// Act
		(_, bool armed) = _guard.TryArm(options, OutputDirectory);

		// Assert
		armed.Should().BeFalse(because: "a caller-pinned checksum wins so the post-save refresh leaves the on-disk baseline alone");
		options.ExpectedChecksum.Should().Be("manual-checksum",
			because: "the explicit CLI --expected-checksum must not be overwritten by the on-disk baseline");
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
		_guard.RefreshOrDrop(MetaPath, options, response);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(MetaPath).TextContents);
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
		_guard.RefreshOrDrop(MetaPath, options, response);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Should().BeNull(
			because: "a stale baseline must be removed when fresh metadata could not be obtained (fail toward no-check)");
	}
}
