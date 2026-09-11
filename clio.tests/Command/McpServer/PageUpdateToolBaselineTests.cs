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
		new(SchemaName, ValidBody, SkipSampling: true, OutputDirectory: "/ws", Force: force, Checksum: checksum)
			{ EnvironmentName = "sandbox" };

	[Test]
	[Description("update-page scopes the registry-driven chart-widget validation to the platform version resolved from the target environment.")]
	public async System.Threading.Tasks.Task UpdatePage_ShouldScopeChartValidationToResolvedEnvironmentVersion() {
		// Arrange
		PageUpdateArgs args = new(SchemaName, ValidBody, SkipSampling: true, OutputDirectory: "/ws") { EnvironmentName = "sandbox" };

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
		PageUpdateArgs args = new(SchemaName, bodyWithResourceBoundInsert, "{\"PDS_UsrContactPhone\":\"Contact phone\"}", SkipSampling: true, OutputDirectory: "/ws") { EnvironmentName = "sandbox" };

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

	// ---------------------------------------------------------------------------------------------
	// AC-2 on the MCP surface (PR #1356 review). The tool's pre-execution gate runs BEFORE the
	// command: UpdatePage -> TryCreatePreExecutionFailureAsync -> ValidateBody returns earlyFailure
	// and PageUpdateCommand never executes. So the label-resource rescue has to work in THIS
	// provider for issue #1320 to be fixed over MCP, and nothing drove it through the tool.
	// ---------------------------------------------------------------------------------------------

	private const string PersistedResourceKey = "CaseSLA_label";

	/// <summary>
	/// Body that inserts a field whose label points at <see cref="PersistedResourceKey"/> while binding to a
	/// DIFFERENT attribute name, so the platform cannot auto-provide the caption: the validator rejects it
	/// unless the key is found among the keys already persisted on the schema.
	/// </summary>
	private static string PersistedResourceBody() =>
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { "
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"CaseSLA\","
		+ "\"values\":{\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.CaseSLA_label\",\"control\":\"$PDS_CaseSLA\"}}]"
		+ "/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"path\":[],"
		+ "\"values\":{\"attributes\":{\"PDS_CaseSLA\":{\"modelConfig\":{\"path\":\"PDS.UsrSLA\"}}}}}]"
		+ "/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
		+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	/// <summary>Re-stubs GetSchema (both client overloads) with the given persisted localizable-string keys.</summary>
	private void StubSchemaWithPersistedKeys(params string[] persistedKeys) {
		string entries = string.Join(",", System.Array.ConvertAll(persistedKeys,
			key => "{\"name\": \"" + key + "\", \"value\": \"stored caption\"}"));
		string payload = "{\"success\": true, \"schema\": {\"uId\": \"" + SchemaUId + "\", \"name\": \"" + SchemaName
			+ "\", \"body\": \"old body\", \"localizableStrings\": [" + entries + "]}}";
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(payload);
		_applicationClient.ExecutePostRequest(
			GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(payload);
	}

	private int GetSchemaCallCount() => _applicationClient.ReceivedCalls().Count(call =>
		call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest) &&
		call.GetArguments().Length > 0 &&
		call.GetArguments()[0] as string == GetSchemaUrl);

	private static PageUpdateArgs CreateArgs(string body) =>
		new(SchemaName, body, SkipSampling: true, OutputDirectory: "/ws")
			{ EnvironmentName = "sandbox" };

	[Test]
	[Description("AC-2 through the MCP tool, which is the surface issue #1320 was reported against: a label resource that is NOT repeated in `resources` but IS already persisted on the schema passes the tool's PRE-EXECUTION gate and the save is issued. Deleting the provider argument the gate is given, or breaking command resolution inside it, restores the reported bug - and until now did so with a fully green suite.")]
	public void UpdatePage_ShouldSave_WhenTheLabelResourceIsAlreadyPersistedOnTheSchema() {
		// Arrange
		StubSchemaWithPersistedKeys(PersistedResourceKey);

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(PersistedResourceBody()), null).Result;

		// Assert
		response.Success.Should().BeTrue(
			because: "the key is stored on the schema and resolves at runtime, so the pre-execution gate must not reject the save");
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	/// <summary>
	/// Makes the persisted-key read fail CLEANLY: resolution succeeds, then GetSchema answers
	/// <c>success:false</c> with the designer service's own message rather than throwing.
	/// </summary>
	private void StubSchemaReadRefusal(string message) {
		string payload = "{\"success\": false, \"errorInfo\": {\"message\": \"" + message + "\"}}";
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>()).Returns(payload);
		_applicationClient.ExecutePostRequest(
			GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(payload);
	}

	[Test]
	[Description("The reason a persisted-key read failed must reach an MCP caller, not only the log: update-page answers with a typed PageUpdateResponse that has no log member and ExecuteWithCleanLog discards the capture buffer, so a 401 or an inaccessible schema used to surface as nothing but the validator's own 'neither auto-provided nor registered' - the misleading cause issue #1320 opened with (PR #1356 round-5 review).")]
	public void UpdatePage_ShouldReportWhyTheRescueFailed_WhenTheSchemaReadIsRefused() {
		// Arrange
		const string refusal = "Access denied to schema";
		StubSchemaReadRefusal(refusal);

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(PersistedResourceBody()), null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "an unreadable schema must leave the stricter label-resource verdict standing");
		response.Warnings.Should().NotBeNull(
			because: "the warning channel is the only member of the typed response that can carry the reason");
		response.Warnings.Should().Contain(warning =>
				warning.Contains("Persisted resource keys could not be read") && warning.Contains(refusal),
			because: "the caller has to see that the rescue was skipped and why, not just that the key is unregistered");
	}

	[Test]
	[Description("Non-vacuity twin for the test above: a save whose persisted-key read SUCCEEDS must carry no such warning, so the assertion above cannot pass on a response that warns unconditionally.")]
	public void UpdatePage_ShouldNotReportARescueFailure_WhenTheSchemaReadSucceeds() {
		// Arrange
		StubSchemaWithPersistedKeys(PersistedResourceKey);

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(PersistedResourceBody()), null).Result;

		// Assert
		response.Success.Should().BeTrue(because: "the key is persisted, so the rescue resolves it");
		(response.Warnings ?? []).Should().NotContain(warning =>
				warning.Contains("Persisted resource keys could not be read"),
			because: "a read that produced keys did not fail and must not warn");
	}

	[Test]
	[Description("Non-vacuity twin for the test above: the SAME body with an empty localizableStrings array is refused by the tool gate and never reaches SaveSchema. A rescue that always reported the key, or never consulted the schema, would pass the positive test and fail here.")]
	public void UpdatePage_ShouldRefuse_WhenTheLabelResourceIsPersistedNowhere() {
		// Arrange
		StubSchemaWithPersistedKeys();

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(PersistedResourceBody()), null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "with the key stored nowhere the stricter verdict must stand rather than letting an unresolvable caption through");
		response.Error.Should().Contain(PersistedResourceKey,
			because: "the original diagnostic must survive the failed rescue and name the unresolved key");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The memoization documented on PersistedResourceKeysRead, asserted at the surface where it matters: the rescue costs exactly ONE extra GetSchema over a clean save even though the tool gate AND the command gate both read the persisted keys - the tool-gate read is reused by the command through the shared options instance.")]
	public void UpdatePage_ShouldPayExactlyOneExtraGetSchema_WhenTheRescueRunsThroughTheTool() {
		// Arrange
		StubSchemaWithPersistedKeys(PersistedResourceKey);
		PageUpdateResponse cleanResponse = _tool.UpdatePage(CreateArgs(ValidBody), null).Result;
		int cleanSaveGetSchemaCalls = GetSchemaCallCount();
		_applicationClient.ClearReceivedCalls();

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(PersistedResourceBody()), null).Result;
		int rescuedSaveGetSchemaCalls = GetSchemaCallCount();

		// Assert
		cleanResponse.Success.Should().BeTrue(
			because: "the control save must succeed for its call count to be a valid baseline");
		cleanSaveGetSchemaCalls.Should().BeGreaterThan(0,
			because: "a zero baseline would make the comparison below vacuous");
		response.Success.Should().BeTrue(because: "the rescued save must still go through");
		rescuedSaveGetSchemaCalls.Should().Be(cleanSaveGetSchemaCalls + 1,
			because: "one read per gate, or one per key, would multiply the cost of every later save of a page with stored resources");
	}

	/// <summary>
	/// The same body as <see cref="PersistedResourceBody"/> with the closing `}); ` removed: the markers stay
	/// present and paired, so the offline content chain runs, but the JS no longer parses.
	/// </summary>
	private static string PersistedResourceBodyWithBrokenSyntax() =>
		PersistedResourceBody().Replace("}; });", "};");

	[Test]
	[Description("ResolveSyntaxFailure passes offlineOnly: true, so the fail-fast path for an unparsable body must make NO Creatio call at all - not even the persisted-key read. Dropping the flag would reintroduce remote I/O on exactly the path that promises none, including against an unreachable environment, and nothing asserted it (PR #1356 review).")]
	public void UpdatePage_ShouldMakeNoRemoteCall_WhenTheBodyDoesNotParse() {
		// Arrange
		StubSchemaWithPersistedKeys(PersistedResourceKey);

		// Act
		PageUpdateResponse response =
			_tool.UpdatePage(CreateArgs(PersistedResourceBodyWithBrokenSyntax()), null).Result;

		// Assert
		response.Success.Should().BeFalse(because: "a body that does not parse cannot be saved");
		_applicationClient.DidNotReceive().ExecutePostRequest(GetSchemaUrl, Arg.Any<string>());
		_applicationClient.DidNotReceive().ExecutePostRequest(
			GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The ExpectedChecksum echo on the SchemaCreatedExternally branch, asserted like the ChecksumMismatch one already is: without it 'modified outside this session' is the caller's only clue and the refusal is undiagnosable (PR #1356 review).")]
	public void UpdatePage_ShouldEchoTheExpectedChecksum_WhenTheSchemaWasCreatedExternally() {
		// Arrange - the baseline records that no editable schema existed, and the caller pinned nothing,
		// so the absent marker is armed; the server, however, does resolve a schema.
		AddMetaWithBaseline("sandbox", "baseline-checksum", editableExists: false, editableSchemaUId: null);

		// Act
		PageUpdateResponse response = _tool.UpdatePage(CreateArgs(ValidBody), null).Result;

		// Assert
		response.Success.Should().BeFalse(because: "a schema that appeared since the baseline was captured is an external change");
		response.Conflict.Should().BeTrue();
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.SchemaCreatedExternally,
			because: "the absent marker is what this refusal is formed from");
		response.ConflictDetails.ExpectedChecksum.Should().Be("baseline-checksum",
			because: "the echo tells the caller which baseline produced the verdict, exactly as the ChecksumMismatch branch does");
	}
}
