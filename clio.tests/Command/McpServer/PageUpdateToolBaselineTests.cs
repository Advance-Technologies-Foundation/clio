using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
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
	private IComponentInfoCatalog _webComponentCatalog;
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
		_webComponentCatalog = Substitute.For<IComponentInfoCatalog>();
		// The target environment resolves to platform version 8.3.4, so chart-widget validation is scoped
		// to that version rather than 'latest'. Chart validation itself is fail-open here (the substitute
		// catalog returns no state), so this affects only the requested version, not the other assertions.
		// ISettingsRepository is retained only as a dependency-presence gate on ResolvePlatformVersionAsync
		// (Story 11, ENG-93347): the actual settings lookup now goes through IToolCommandResolver so a
		// header-aware passthrough call routes to the header tenant instead of a silent active-env probe.
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		IOwnedPlatformVersionResolver resolver = Substitute.For<IOwnedPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(new PlatformVersionResolution("8.3.4", VersionResolutionSource.Environment));
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		resolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		_tool = new PageUpdateTool(
			command, logger, commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(),
			_webComponentCatalog,
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(_fileSystem),
			resolverFactory, settingsRepository);
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

	private void AddMetaWithBaseline(string environmentName, string checksum,
		bool editableExists = true, string editableSchemaUId = SchemaUId) {
		_fileSystem.AddFile(MetaPath, new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-06-12T10:00:00Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Baseline = new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = environmentName,
				EditableSchemaExists = editableExists,
				EditableSchemaUId = editableSchemaUId,
				Checksum = checksum,
				ModifiedOn = "raw",
				CapturedAt = "2026-06-12T10:00:00Z"
			}
		})));
	}

	private static PageUpdateArgs CreateArgs(bool? force = null, string checksum = null) =>
		new(SchemaName, ValidBody, null, null, "sandbox", null, null, null,
			SkipSampling: true, OutputDirectory: "/ws", Force: force, Checksum: checksum);

	[Test]
	[Description("update-page scopes the registry-driven chart-widget validation to the platform version resolved from the target environment.")]
	public async System.Threading.Tasks.Task UpdatePage_ShouldScopeChartValidationToResolvedEnvironmentVersion() {
		// Arrange
		PageUpdateArgs args = new(SchemaName, ValidBody, null, null, "sandbox", null, null, null,
			SkipSampling: true, OutputDirectory: "/ws");

		// Act
		await _tool.UpdatePage(args, null);

		// Assert
		string requestedVersion = (string)_webComponentCatalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync))
			.GetArguments()[0];
		requestedVersion.Should().Be("8.3.4",
			because: "update-page must scope its save-time chart-widget validation to the version resolved from the target environment");
	}

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

	[Test]
	[Description("update-page must NOT false-reject an insert whose label resource is supplied via the 'resources' parameter: the pre-resolution field-binding validators run with the parsed explicitResources, matching the resource-aware post-resolution path.")]
	public void UpdatePage_ShouldSucceed_WhenInsertLabelResourceProvidedViaResourcesParameter() {
		// Arrange
		const string bodyWithResourceBoundInsert =
			"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrContactPhone\"," +
			"\"values\":{\"type\":\"crt.PhoneInput\",\"label\":\"$Resources.Strings.PDS_UsrContactPhone\",\"control\":\"$PDS_UsrContactPhone\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"path\":[]," +
			"\"values\":{\"attributes\":{\"PDS_UsrContactPhone\":{\"modelConfig\":{\"path\":\"PDS.UsrContactPhone\"}}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageUpdateArgs args = new(SchemaName, bodyWithResourceBoundInsert,
			"{\"PDS_UsrContactPhone\":\"Contact phone\"}", null, "sandbox", null, null, null,
			SkipSampling: true, OutputDirectory: "/ws");

		// Act
		PageUpdateResponse response = _tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeTrue(
			because: "the label resource key is registered through the 'resources' parameter, so the pre-resolution field-binding gate must accept it instead of failing with 'invalid form field bindings'");
		response.Error.Should().BeNull(
			because: "a valid resource-backed insert must produce no validation error at the MCP layer");
	}

	[Test]
	[Description("A caller-pinned checksum that matches the server must let the save through even when the on-disk baseline is stale, and the baseline must move forward afterwards (AC-1 of issue #1320).")]
	public void UpdatePage_ShouldSaveAndMoveTheBaselineForward_WhenTheCallerChecksumMatchesButTheDiskBaselineIsStale() {
		// Arrange
		AddMetaWithBaseline("sandbox", "stale-on-disk-checksum");
		StubChecksumByUId(ChecksumRow("server-checksum"), ChecksumRow("fresh-after-save"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(checksum: "server-checksum"), null).Result;

		// Assert
		response.Success.Should().BeTrue(
			because: "the pin the caller fetched matches the server, so the stale on-disk baseline must not veto the save");
		response.Conflict.Should().BeFalse(because: "nothing was modified externally");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(MetaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("fresh-after-save",
			because: "the matching baseline must still be refreshed, otherwise the next unpinned save false-conflicts");
	}

	[Test]
	[Description("A caller-pinned checksum that is stale must still be refused, with SaveSchema never reached (the true positive AC-1 had to preserve).")]
	public void UpdatePage_ShouldRefuse_WhenTheCallerChecksumIsStale() {
		// Arrange
		AddMetaWithBaseline("sandbox", "stale-on-disk-checksum");
		StubChecksumByUId(ChecksumRow("server-checksum"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(checksum: "checksum-the-caller-read-earlier"), null).Result;

		// Assert
		response.Success.Should().BeFalse(because: "the page moved on the server since the caller read it");
		response.Conflict.Should().BeTrue(because: "a stale pin is exactly what the guard exists to catch");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.ChecksumMismatch,
			because: "the refusal must name the checksum comparison, not schema identity");
		response.ConflictDetails.ExpectedChecksum.Should().Be("checksum-the-caller-read-earlier",
			because: "the pin wins the comparison, so it is the value the refusal reports back");
	}

	[Test]
	[Description("A pinned checksum must not be vetoed by a stale schema-absent marker on disk: arming it produced a false schema-created-externally on a save whose pin matched the server.")]
	public void UpdatePage_ShouldSave_WhenTheDiskBaselineSaysSchemaAbsentButTheCallerPinnedAMatchingChecksum() {
		// Arrange
		AddMetaWithBaseline("sandbox", "stale-on-disk-checksum", editableExists: false);
		StubChecksumByUId(ChecksumRow("server-checksum"), ChecksumRow("fresh-after-save"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(checksum: "server-checksum"), null).Result;

		// Assert
		response.Success.Should().BeTrue(
			because: "a pinned checksum asserts the schema existed, so a stale 'editableSchemaExists: false' must not refuse the save");
		response.Conflict.Should().BeFalse(
			because: "no external modification happened - the absent marker came from a differently-anchored earlier read");
	}

	[Test]
	[Description("With no meta.json at all, a matching caller-pinned checksum must save cleanly - the common MCP path where get-page wrote no baseline.")]
	public void UpdatePage_ShouldSave_WhenTheCallerChecksumMatchesAndNoBaselineExists() {
		// Arrange — no meta.json on the mock file system.
		StubChecksumByUId(ChecksumRow("server-checksum"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(checksum: "server-checksum"), null).Result;

		// Assert
		response.Success.Should().BeTrue(because: "the pin is the only baseline, and it matches");
		response.Conflict.Should().BeFalse(because: "nothing was modified externally");
		_fileSystem.FileExists(MetaPath).Should().BeFalse(because: "update-page must never create .clio-pages trees on its own");
	}

	[Test]
	[Description("A caller-pinned checksum surrounded by whitespace must be trimmed, so it matches the server value instead of producing a false checksum-mismatch.")]
	public void UpdatePage_ShouldTrimTheCallerChecksum_WhenItArrivesPadded() {
		// Arrange
		StubChecksumByUId(ChecksumRow("server-checksum"));

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(checksum: "  server-checksum  "), null).Result;

		// Assert
		response.Success.Should().BeTrue(
			because: "the arming predicate is whitespace-tolerant while the comparison is strictly Ordinal, so an untrimmed pin would arm the check and then fail it");
		response.Conflict.Should().BeFalse(because: "the padded value denotes the very checksum the server reports");
	}
}
