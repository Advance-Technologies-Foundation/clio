using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class PageBaselineStoreTests {

	private const string SchemaName = "UsrCase_FormPage";
	private const string MetaPath = "/ws/.clio-pages/UsrCase_FormPage/meta.json";

	private static string MetaJsonWithBaseline(
		string environmentName = "local",
		string environmentUri = null,
		string checksum = "abc",
		string editableSchemaUId = "11111111-2222-3333-4444-555555555555") =>
		JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-06-12T10:00:00Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Baseline = new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = environmentName,
				EnvironmentUri = environmentUri,
				EditableSchemaExists = true,
				EditableSchemaUId = editableSchemaUId,
				Checksum = checksum,
				ModifiedOn = "raw-modified-on",
				CapturedAt = "2026-06-12T10:00:00Z"
			}
		});

	[Test]
	[Description("TryReadBaseline must return the baseline block when meta.json contains one.")]
	public void TryReadBaseline_ShouldReturnBaseline_WhenMetaJsonContainsBaseline() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline()));

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, MetaPath);

		// Assert
		baseline.Should().NotBeNull(because: "the meta.json on disk carries a baseline block");
		baseline.Checksum.Should().Be("abc", because: "the persisted checksum must round-trip unchanged");
		baseline.EditableSchemaExists.Should().BeTrue(because: "the persisted existence flag must round-trip unchanged");
	}

	[Test]
	[Description("TryReadBaseline must return null for a legacy meta.json without a baseline block so the conflict check is skipped.")]
	public void TryReadBaseline_ShouldReturnNull_WhenMetaJsonIsLegacyFormat() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData("""{"fetchedAt":"2026-06-12T10:00:00Z","page":{"schemaName":"UsrCase_FormPage"}}"""));

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, MetaPath);

		// Assert
		baseline.Should().BeNull(because: "legacy meta.json files predate the baseline contract and must skip the check");
	}

	[Test]
	[Description("TryReadBaseline must return null when meta.json is missing or unparseable instead of throwing.")]
	public void TryReadBaseline_ShouldReturnNull_WhenMetaJsonMissingOrCorrupt() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile("/ws/.clio-pages/Other/meta.json", new MockFileData("not-json{{{"));

		// Act
		PageBaselineInfo missing = PageBaselineStore.TryReadBaseline(fs, MetaPath);
		PageBaselineInfo corrupt = PageBaselineStore.TryReadBaseline(fs, "/ws/.clio-pages/Other/meta.json");

		// Assert
		missing.Should().BeNull(because: "a missing meta.json means no baseline was ever captured");
		corrupt.Should().BeNull(because: "an unparseable meta.json must fail toward no-check, never throw");
	}

	[Test]
	[Description("ResolveMetaFilePath must prefer the sibling meta.json when body-file resides inside .clio-pages/{schema}/.")]
	public void ResolveMetaFilePath_ShouldUseBodyFileSibling_WhenBodyFileInsideClioPages() {
		// Arrange
		MockFileSystem fs = new();
		string bodyFile = "/custom/anchor/.clio-pages/UsrCase_FormPage/body.js";

		// Act
		string metaPath = PageBaselineStore.ResolveMetaFilePath(
			fs, "/elsewhere", "/home/user", "/home/user/.clio", null, bodyFile, SchemaName);

		// Assert
		metaPath.Should().Be("/custom/anchor/.clio-pages/UsrCase_FormPage/meta.json",
			because: "a body-file inside .clio-pages pins the baseline to its sibling meta.json regardless of the anchor");
	}

	[Test]
	[Description("ResolveMetaFilePath must fall back to anchor resolution when body-file is outside .clio-pages.")]
	public void ResolveMetaFilePath_ShouldUseAnchor_WhenBodyFileOutsideClioPages() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile("/ws/.clio/workspaceSettings.json", new MockFileData("{}"));
		fs.AddDirectory("/ws/src");

		// Act
		string metaPath = PageBaselineStore.ResolveMetaFilePath(
			fs, "/ws/src", "/home/user", "/home/user/.clio", null, "/tmp/body.js", SchemaName);

		// Assert
		metaPath.Should().Be("/ws/.clio-pages/UsrCase_FormPage/meta.json",
			because: "a body-file outside .clio-pages must not override the workspace-root anchor resolution");
	}

	[Test]
	[Description("MatchesEnvironment must match registered environment names ordinally ignoring case.")]
	public void MatchesEnvironment_ShouldMatch_WhenEnvironmentNamesEqualIgnoreCase() {
		// Arrange
		PageBaselineInfo baseline = new() { EnvironmentName = "Local" };

		// Act
		bool matches = PageBaselineStore.MatchesEnvironment(baseline, "local", null);

		// Assert
		matches.Should().BeTrue(because: "environment names identify the same registration regardless of casing");
	}

	[Test]
	[Description("MatchesEnvironment must match direct URIs normalized for trailing slash and case.")]
	public void MatchesEnvironment_ShouldMatch_WhenUrisDifferOnlyByTrailingSlash() {
		// Arrange
		PageBaselineInfo baseline = new() { EnvironmentUri = "https://Site.creatio.com/" };

		// Act
		bool matches = PageBaselineStore.MatchesEnvironment(baseline, null, "https://site.creatio.com");

		// Assert
		matches.Should().BeTrue(because: "a trailing slash or casing difference does not change the target environment");
	}

	[Test]
	[Description("MatchesEnvironment must NOT match cross-mode combinations (name vs uri) or differing identities so the check is skipped.")]
	public void MatchesEnvironment_ShouldNotMatch_WhenIdentityModesDifferOrMismatch() {
		// Arrange
		PageBaselineInfo namedBaseline = new() { EnvironmentName = "local" };
		PageBaselineInfo uriBaseline = new() { EnvironmentUri = "https://a.creatio.com" };

		// Act
		bool crossMode = PageBaselineStore.MatchesEnvironment(namedBaseline, null, "https://a.creatio.com");
		bool nameMismatch = PageBaselineStore.MatchesEnvironment(namedBaseline, "prod", null);
		bool uriMismatch = PageBaselineStore.MatchesEnvironment(uriBaseline, null, "https://b.creatio.com");
		bool nullBaseline = PageBaselineStore.MatchesEnvironment(null, "local", null);

		// Assert
		crossMode.Should().BeFalse(because: "a name-captured baseline cannot be proven to target the same host as a raw uri");
		nameMismatch.Should().BeFalse(because: "different environment names are different targets");
		uriMismatch.Should().BeFalse(because: "different hosts are different targets");
		nullBaseline.Should().BeFalse(because: "no baseline means there is nothing to match");
	}

	[Test]
	[Description("RefreshExistingBaseline must rewrite only the baseline block, preserving fetchedAt and page.")]
	public void RefreshExistingBaseline_ShouldPreserveFetchedAtAndPage_WhenRefreshing() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline(checksum: "old")));

		// Act
		PageBaselineStore.RefreshExistingBaseline(
			fs, MetaPath, SchemaName, "local", null,
			"99999999-8888-7777-6666-555555555555", "new-checksum", "new-modified", "2026-06-12T11:00:00Z");

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fs.GetFile(MetaPath).TextContents);
		meta.FetchedAt.Should().Be("2026-06-12T10:00:00Z", because: "fetchedAt belongs to the get-page snapshot and must survive the refresh");
		meta.Page.SchemaName.Should().Be(SchemaName, because: "page metadata belongs to the get-page snapshot and must survive the refresh");
		meta.Baseline.Checksum.Should().Be("new-checksum", because: "the refresh must persist the post-save checksum");
		meta.Baseline.EditableSchemaUId.Should().Be("99999999-8888-7777-6666-555555555555",
			because: "the refresh must persist the schema UId the save actually wrote to");
		meta.Baseline.EditableSchemaExists.Should().BeTrue(because: "a successful save guarantees the editable schema now exists");
		meta.Baseline.CapturedAt.Should().Be("2026-06-12T11:00:00Z", because: "the refresh timestamp must reflect the save moment");
	}

	[Test]
	[Description("RefreshExistingBaseline must no-op when meta.json does not exist — the store never creates .clio-pages trees.")]
	public void RefreshExistingBaseline_ShouldNoOp_WhenMetaJsonMissing() {
		// Arrange
		MockFileSystem fs = new();

		// Act
		PageBaselineStore.RefreshExistingBaseline(
			fs, MetaPath, SchemaName, "local", null, "uid", "checksum", "modified", "2026-06-12T11:00:00Z");

		// Assert
		fs.FileExists(MetaPath).Should().BeFalse(because: "the refresh path must never materialize .clio-pages directories");
	}

	[Test]
	[Description("DeleteBaseline must remove the baseline block while keeping fetchedAt and page intact.")]
	public void DeleteBaseline_ShouldRemoveBaselineOnly_WhenMetaJsonHasBaseline() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline()));

		// Act
		PageBaselineStore.DeleteBaseline(fs, MetaPath);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fs.GetFile(MetaPath).TextContents);
		meta.Baseline.Should().BeNull(because: "a stale baseline must be removed so the next write skips the check instead of false-conflicting");
		meta.FetchedAt.Should().Be("2026-06-12T10:00:00Z", because: "legacy fields must survive baseline removal");
		meta.Page.SchemaName.Should().Be(SchemaName, because: "legacy fields must survive baseline removal");
	}
}
