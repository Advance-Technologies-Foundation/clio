using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolBaselineTests
{
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";
	private const string MetaPath = "/ws/.clio-pages/Test_FormPage/meta.json";

	private const string ValidBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;
	private MockFileSystem _fileSystem;
	private PageUpdateTool _tool;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		PageUpdateCommand command = new(_applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		_fileSystem = new MockFileSystem();
		_tool = new PageUpdateTool(
			command, logger, commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(_fileSystem));
	}

	private void StubChecksumByUId(params string[] responses) {
		System.Collections.Generic.Queue<string> queue = new(responses);
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => queue.Count > 0 ? queue.Dequeue() : """{"success": false}""");
	}

	private static string ChecksumRow(string checksum) =>
		$$"""{"success": true, "rows": [{"Checksum": "{{checksum}}", "ModifiedOn": "2026-06-12T09:00:00"}]}""";

	private void AddMetaWithBaseline(string environmentName, string checksum) {
		_fileSystem.AddFile(MetaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-06-12T10:00:00Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Baseline = new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = environmentName,
				EditableSchemaExists = true,
				EditableSchemaUId = SchemaUId,
				Checksum = checksum,
				ModifiedOn = "raw",
				CapturedAt = "2026-06-12T10:00:00Z"
			}
		})));
	}

	private static PageUpdateRunArgs CreateArgs(bool? force = null) =>
		new(SchemaName, ValidBody, null, null, "sandbox", null, null, null,
			SkipSampling: true, OutputDirectory: "/ws", Force: force);

	[Test]
	[Description("update-page must read the meta.json baseline and surface a conflict when the server checksum differs from the stored baseline.")]
	public void UpdatePage_ShouldPassExpectedChecksum_WhenMetaJsonBaselineExists() {
		// Arrange
		AddMetaWithBaseline("sandbox", "baseline-checksum");
		StubChecksumByUId(ChecksumRow("server-checksum"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(), null).Result;

		// Assert
		response.Success.Should().BeFalse(because: "the stored baseline differs from the server checksum");
		response.Conflict.Should().BeTrue(because: "the on-disk baseline must arm the external-modification check automatically");
		response.ConflictDetails.ExpectedChecksum.Should().Be("baseline-checksum",
			because: "the expected checksum must come from meta.json without the caller passing it explicitly");
	}

	[Test]
	[Description("update-page must skip the conflict check when the baseline was captured against a different environment.")]
	public void UpdatePage_ShouldSkipCheck_WhenBaselineEnvironmentDiffers() {
		// Arrange
		AddMetaWithBaseline("production", "baseline-checksum");
		StubChecksumByUId(ChecksumRow("server-checksum"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(), null).Result;

		// Assert
		response.Success.Should().BeTrue(because: "a baseline from another environment is not evidence of an external modification here");
		response.Conflict.Should().BeFalse(because: "the env-identity guard must disarm the check");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("baseline-checksum",
			because: "a foreign-environment baseline must be left untouched by this save");
	}

	[Test]
	[Description("update-page must refresh the meta.json baseline with the post-save checksum after a successful save.")]
	public void UpdatePage_ShouldRefreshBaseline_WhenSaveSucceeds() {
		// Arrange
		AddMetaWithBaseline("sandbox", "baseline-checksum");
		StubChecksumByUId(ChecksumRow("baseline-checksum"), ChecksumRow("fresh-after-save"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(), null).Result;

		// Assert
		response.Success.Should().BeTrue(because: "a matching baseline allows the save to proceed");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Should().NotBeNull(because: "a successful save with fresh metadata must keep the baseline armed");
		meta.Baseline.Checksum.Should().Be("fresh-after-save",
			because: "consecutive updates in the same session must compare against the post-save checksum, not the original");
		meta.FetchedAt.Should().Be("2026-06-12T10:00:00Z", because: "the refresh must not touch the get-page snapshot fields");
	}

	[Test]
	[Description("update-page must drop the meta.json baseline when the post-save checksum query fails, so the next write skips the check instead of false-conflicting.")]
	public void UpdatePage_ShouldDeleteBaseline_WhenPostSaveChecksumUnavailable() {
		// Arrange
		AddMetaWithBaseline("sandbox", "baseline-checksum");
		StubChecksumByUId(ChecksumRow("baseline-checksum"), """{"success": false}""");

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(), null).Result;

		// Assert
		response.Success.Should().BeTrue(because: "a failed post-save metadata query must not fail the already-successful save");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Should().BeNull(because: "a stale baseline must be removed when fresh metadata could not be obtained (fail toward no-check)");
	}

	[Test]
	[Description("update-page must run unchanged when no meta.json exists at all (regression-safe default).")]
	public void UpdatePage_ShouldSkipCheck_WhenMetaJsonMissing() {
		// Arrange — no meta.json on the mock file system.
		StubChecksumByUId();

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(), null).Result;

		// Assert
		response.Success.Should().BeTrue(because: "the legacy flow without a baseline must be unaffected");
		response.Conflict.Should().BeFalse(because: "no baseline means nothing to conflict with");
		_fileSystem.FileExists(MetaPath).Should().BeFalse(because: "update-page must never create .clio-pages trees on its own");
	}

	[Test]
	[Description("update-page with force=true must overwrite despite a stale baseline and refresh it afterwards.")]
	public void UpdatePage_ShouldOverwriteAndRefreshBaseline_WhenForceTrue() {
		// Arrange
		AddMetaWithBaseline("sandbox", "baseline-checksum");
		StubChecksumByUId(ChecksumRow("fresh-after-save"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(force: true), null).Result;

		// Assert
		response.Success.Should().BeTrue(because: "force=true deliberately bypasses the conflict check");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("fresh-after-save",
			because: "after a forced overwrite the baseline must track the new server state");
	}
}
