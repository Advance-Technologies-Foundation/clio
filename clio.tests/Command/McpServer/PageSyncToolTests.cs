using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class PageSyncToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for sync-pages")]
	public void PageSyncTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = PageSyncTool.ToolName;

		// Assert
		toolName.Should().Be("sync-pages",
			because: "the sync-pages MCP tool identifier must remain stable for callers");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks sync-pages as destructive and not read-only")]
	public void PageSyncTool_Should_Advertise_Safety_Metadata() {
		// Arrange
		var method = typeof(PageSyncTool).GetMethod(nameof(PageSyncTool.SyncPages))!;
		var attribute = method
			.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)
			.Cast<ModelContextProtocol.Server.McpServerToolAttribute>()
			.Single();

		// Act
		bool readOnly = attribute.ReadOnly;
		bool destructive = attribute.Destructive;

		// Assert
		readOnly.Should().BeFalse(
			because: "sync-pages mutates remote page schemas and should not be marked read-only");
		destructive.Should().BeTrue(
			because: "sync-pages modifies remote page schemas and should warn clients");
	}

	[Test]
	[Category("Unit")]
	[Description("Successfully updates a single page when Creatio responds with success")]
	public async Task SyncPages_Should_Succeed_For_Valid_Page() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "the page update should succeed when Creatio returns success");
		response.Pages.Should().HaveCount(1,
			because: "one page was requested");
		response.Pages[0].SchemaName.Should().Be("UsrTodo_FormPage",
			because: "the result should reference the requested page");
		response.Pages[0].BodyLength.Should().BeGreaterThan(0,
			because: "the saved page should report a non-zero body length");
	}

	[Test]
	[Category("Unit")]
	[Description("Updates multiple pages in a single call")]
	public async Task SyncPages_Should_Process_Multiple_Pages() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody),
				new PageSyncPageInput("UsrTodo_ListPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "both pages should be updated successfully");
		response.Pages.Should().HaveCount(2,
			because: "two pages were requested");
		response.Pages[0].SchemaName.Should().Be("UsrTodo_FormPage",
			because: "pages should be processed in order");
		response.Pages[1].SchemaName.Should().Be("UsrTodo_ListPage",
			because: "the second page should also be processed");
	}

	[Test]
	[Category("Unit")]
	[Description("Continues processing remaining pages when one page fails")]
	public async Task SyncPages_Should_Continue_On_Failure() {
		// Arrange
		PageUpdateCommand updateCommand = CreatePageUpdateCommandWithFailureForSchema("UsrBroken_FormPage");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrBroken_FormPage", ValidPageBody),
				new PageSyncPageInput("UsrWorking_ListPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "one page failed so overall success should be false");
		response.Pages.Should().HaveCount(2,
			because: "both pages should be processed even when the first fails");
		response.Pages[0].Success.Should().BeFalse(
			because: "the first page should report failure");
		response.Pages[1].Success.Should().BeTrue(
			because: "the second page should succeed independently");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation rejects a page body whose schema markers are missing — the syntax check passes (the body parses as JS) so the test exercises the markers validator on its own, not the upstream Acornima gate")]
	public async Task SyncPages_Should_Reject_Invalid_Page_Body_When_Validation_Enabled() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		// Body parses as valid JavaScript so the upstream PageBodySyntaxValidator
		// gate (ENG-89796) passes; the markers validator then catches the missing
		// SCHEMA_* envelope and reports the failure.
		string bodyWithMissingMarkers = "define('BadPage', [], function() { return {}; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrBad_FormPage", bodyWithMissingMarkers)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "client-side validation should reject a body with missing markers");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation results should be included when validation is enabled");
		response.Pages[0].Validation!.MarkersOk.Should().BeFalse(
			because: "the body is missing required schema markers");
		response.Pages[0].Error.Should().Contain("validation failed",
			because: "the error should describe the validation failure");
	}

	[Test]
	[Category("Unit")]
	[Description("A body with a JavaScript syntax error fails fast BEFORE the markers/sampling chain AND no remote save call is made — proves the deterministic gate short-circuits before TryUpdatePage by asserting ReceivedCalls on the IApplicationClient substitute is empty")]
	public async Task SyncPages_Should_FailFast_WhenBodyHasJavaScriptSyntaxError() {
		// Arrange — wire a real PageUpdateCommand so the IApplicationClient
		// substitute behind it can confirm no remote save call was made.
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		// `define('BadPage', {})}` has a stray closing brace at the end → SyntaxError.
		// The PageBodySyntaxValidator must surface this before the markers validator
		// runs and no SaveSchema request should ever leave the process.
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrBad_FormPage", "define('BadPage', {})}")],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "a syntactically broken body must NEVER be persisted — this is the deterministic floor of the syntax gate");
		response.Pages[0].Success.Should().BeFalse(
			because: "the per-page result must mirror the overall failure");
		response.Pages[0].Error.Should().Contain("JavaScript syntax error",
			because: "the failure message must name the actual class of problem so the operator does not chase a phantom marker/sampling issue");
		response.Pages[0].Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the broken body did not reach the server (and therefore did not corrupt a saved page) without having to read the code");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the syntax-gate short-circuit must run BEFORE PageUpdateCommand.TryUpdatePage — no SaveSchema (or any other) request must have been issued for this batch");
	}

	[Test]
	[Category("Unit")]
	[Description("Batch with two pages sharing the same schema-name: a broken first body must not corrupt the second body's pre-pass results — the gates are keyed by input index, not by SchemaName, so last-write-wins on a Dictionary cannot blow away the AST/findings of the first page.")]
	public async Task SyncPages_Should_Not_CrossContaminate_When_Batch_Contains_Duplicate_SchemaName() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrPage_FormPage", "define('Dup', {})}"),
				new PageSyncPageInput("UsrPage_FormPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "both inputs must produce an independent per-page result, even when they share a schema-name");
		response.Pages[0].Success.Should().BeFalse(
			because: "the first entry's body is syntactically broken and must surface its own deterministic failure regardless of what the duplicate-named sibling looks like");
		response.Pages[0].Error.Should().Contain("JavaScript syntax error",
			because: "index-keyed pre-pass guarantees the first entry's diagnosis is not overwritten by the second entry's AST or vice versa");
		response.Pages[1].Success.Should().BeTrue(
			because: "the second entry's valid body must still proceed to save, independent of the first entry's failure");
		int saveSchemaCalls = applicationClient.ReceivedCalls()
			.Count(c => c.GetArguments().FirstOrDefault() is string url && url.Contains("SaveSchema"));
		saveSchemaCalls.Should().Be(1,
			because: "exactly one SaveSchema round-trip must happen — for the valid second entry only");
	}

	[Test]
	[Category("Unit")]
	[Description("Mixed batch: a syntactically broken page is rejected and a valid page is saved in the same call — exactly one save round-trip happens for the valid page, not one per page or none at all. Pins per-page fail-fast semantics that no other test currently covers.")]
	public async Task SyncPages_Should_Save_Only_Valid_Page_When_Batch_Contains_One_Broken_And_One_Valid() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrBad_FormPage", "define('BadPage', {})}"),
				new PageSyncPageInput("UsrGood_FormPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "both inputs must produce a per-page result, with order preserved");
		response.Pages[0].Success.Should().BeFalse(
			because: "the broken page must fail fast on the syntax gate");
		response.Pages[0].Error.Should().Contain("JavaScript syntax error",
			because: "the broken page's failure must name the deterministic problem class");
		response.Pages[1].Success.Should().BeTrue(
			because: "fail-fast is per-page; one broken sibling must not block the valid one from being saved");
		int saveSchemaCalls = applicationClient.ReceivedCalls()
			.Count(c => c.GetArguments().FirstOrDefault() is string url && url.Contains("SaveSchema"));
		saveSchemaCalls.Should().Be(1,
			because: "exactly one SaveSchema round-trip must happen — for the valid page only; the broken page must not be sent");
	}

	[Test]
	[Category("Unit")]
	[Description("Skips client-side validation when validate is false")]
	public async Task SyncPages_Should_Skip_Validation_When_Disabled() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrPage", ValidPageBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "validation is disabled so the page should be updated directly");
		response.Pages[0].Validation.Should().BeNull(
			because: "no validation results should be present when validation is skipped");
	}

	[Test]
	[Category("Unit")]
	[Description("Allows JavaScript handlers when validation is enabled")]
	public async Task SyncPages_Should_Accept_JavaScript_Handler_Content_When_Validation_Is_Enabled() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string bodyWithHandler = ValidPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"/**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { await next?.handle(request); } }]/**SCHEMA_HANDLERS*/");
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithHandler)],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeTrue(
			because: "handler markers may contain JavaScript and should not fail content validation");
		response.Pages[0].Success.Should().BeTrue(
			because: "the page body remains valid when handlers contain executable JavaScript");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should still be returned when validation is enabled");
		response.Pages[0].Validation!.ContentOk.Should().BeTrue(
			because: "content validation should ignore handler JavaScript blocks");
	}

	[Test]
	[Category("Unit")]
	[Description("Allows JavaScript converters and validators when validation is enabled")]
	public void SyncPages_Should_Accept_JavaScript_Converters_And_Validators_When_Validation_Is_Enabled() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string bodyWithConverterAndValidator = ValidPageBody
			.Replace(
				"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
				"/**SCHEMA_CONVERTERS*/{ \"usr.ToUpperCase\": function(value) { return value?.toUpperCase() ?? \"\"; } }/**SCHEMA_CONVERTERS*/")
			.Replace(
				"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
				"/**SCHEMA_VALIDATORS*/{ \"usr.ValidateFieldValue\": { \"validator\": function(config) { return function(control) { return control.value !== config.invalidName ? null : { \"usr.ValidateFieldValue\": { message: config.message } }; }; }, \"params\": [{ \"name\": \"invalidName\" }, { \"name\": \"message\" }], \"async\": false } }/**SCHEMA_VALIDATORS*/");
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithConverterAndValidator)],
			Validate: true);

		PageSyncResponse response = tool.SyncPages(args, null).Result;

		response.Success.Should().BeTrue(
			because: "sync-pages should not reject function-based converter and validator sections as JSON errors");
		response.Pages[0].Success.Should().BeTrue(
			because: "the page body remains valid when converter and validator markers contain JavaScript functions");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should still be present when validation is enabled");
		response.Pages[0].Validation!.ContentOk.Should().BeTrue(
			because: "content validation should treat converters and validators as JavaScript object sections");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation rejects a field insert whose binding attribute is not declared in viewModelConfigDiff and whose label resource is neither registered in 'resources' nor auto-provided by a DS-bound attribute. Without rejection, the saved control would have no data source and a blank caption.")]
	public async Task SyncPages_Should_Reject_InsertedFields_Without_Matching_ViewModelOrResource() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string bodyWithUndeclaredBindings = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$PDS_UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithUndeclaredBindings)],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeFalse(
			because: "sync-pages must surface insert operations that would render fields with no data source and a blank caption");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation accepts a merge operation that targets a parent-provided field control without local viewModelConfigDiff declarations — the inserted-field contract applies only to operation:\"insert\", not to operation:\"merge\".")]
	public async Task SyncPages_Should_Accept_MergeAgainstParentProvidedControl() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string bodyWithParentMerge = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$PDS_UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithParentMerge)],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeTrue(
			because: "merge operations target existing parent-provided controls whose binding attribute and resource may legitimately live in the parent schema, so the inserted-field contract does NOT apply");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation surfaces warnings for explicit custom field caption resources when the field uses a declared view-model attribute")]
	public async Task SyncPages_Should_Surface_FieldCaptionWarnings_When_ExplicitResources_Are_Provided() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string bodyWithExplicitFieldCaption = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"#ResourceString(UsrStatus_caption)#\",\"control\":\"$UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"path\":[],\"values\":{\"attributes\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithExplicitFieldCaption, "{\"UsrStatus_caption\":\"Status\"}")],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeTrue(
			because: "explicit resources keep the custom field caption pattern non-blocking");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should still be returned on successful guarded saves");
		response.Pages[0].Validation!.Warnings.Should().ContainSingle(warning => warning.Contains("UsrStatus_caption"),
			because: "sync-pages should surface the softer caption guidance as a warning");
	}

	[Test]
	[Category("Unit")]
	[Description("Passes page resources through sync-pages and returns the registered resource count from update-page.")]
	public async Task SyncPages_Should_Surface_Registered_Resources() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray { new JObject { ["UId"] = "resource-page-uid" } }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject {
					["body"] = "original",
					["localizableStrings"] = new JArray()
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		PageUpdateCommand updateCommand = new(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), CreateHierarchyClientFor("resource-page-uid"));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string bodyWithResource = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{ values: { caption: \"#ResourceString(UsrTitle)#\" } }]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithResource, "{\"UsrTitle\":\"Title\"}")],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "sync-pages should forward resources into update-page and keep the successful response");
		response.Pages[0].ResourcesRegistered.Should().Be(1,
			because: "the sync-pages response should preserve the number of resources registered by update-page");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes sync-pages request and response resource fields using the documented MCP names.")]
	public void PageSync_Should_Serialize_Resource_Fields() {
		// Arrange
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody, "{\"UsrTitle\":\"Title\"}")],
			Validate: true,
			Verify: false);
		PageSyncResponse response = new() {
			Success = true,
			Pages = [
				new PageSyncPageResult {
					SchemaName = "UsrTodo_FormPage",
					Success = true,
					ResourcesRegistered = 1,
					Page = new PageMetadataInfo {
						SchemaName = "UsrTodo_FormPage",
						SchemaUId = "test-uid",
						PackageName = "UsrPkg",
						PackageUId = "test-package-uid",
						ParentSchemaName = "BaseModulePage"
					},
					VerifiedBodyFile = "/workspace/.clio-pages/UsrTodo_FormPage/body.js"
				}
			]
		};

		// Act
		string serializedArgs = System.Text.Json.JsonSerializer.Serialize(args);
		string serializedResponse = System.Text.Json.JsonSerializer.Serialize(response);

		// Assert
		serializedArgs.Should().Contain("\"resources\":\"{\\u0022UsrTitle\\u0022:\\u0022Title\\u0022}\"",
			because: "sync-pages should include the optional page resources payload when it is provided");
		serializedResponse.Should().Contain("\"resources-registered\":1",
			because: "sync-pages should serialize the registered-resource count using the documented MCP field name");
		serializedResponse.Should().Contain("\"page\":{",
			because: "sync-pages should serialize read-back page metadata when it is present");
		serializedResponse.Should().Contain("\"verified-body-file\":",
			because: "sync-pages should serialize the verified body file path using the documented MCP field name");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies page content after save when verify is true")]
	public async Task SyncPages_Should_Verify_After_Save_When_Enabled() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		PageGetCommand getCommand = CreateSuccessfulPageGetCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>())
			.Returns(getCommand);
		MockFileSystem mockFs = new();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, mockFs, mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			Verify: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "both save and verification should succeed");
		response.Pages[0].Success.Should().BeTrue(
			because: "the page was saved and verified successfully");
		response.Pages[0].Page.Should().NotBeNull(
			because: "verify=true should surface page metadata from the read-back response");
		response.Pages[0].Page!.SchemaName.Should().Be("UsrTodo_FormPage",
			because: "the verified page metadata should identify the saved schema");
		response.Pages[0].Page.SchemaUId.Should().Be("test-uid",
			because: "the read-back page metadata should preserve schema identity");
		response.Pages[0].Page.PackageName.Should().Be("UsrPkg",
			because: "the read-back page metadata should preserve package ownership");
		response.Pages[0].Page.ParentSchemaName.Should().Be("BaseModulePage",
			because: "the read-back page metadata should preserve the parent schema");
		response.Pages[0].VerifiedBodyFile.Should().NotBeNullOrWhiteSpace(
			because: "verify=true should write verified body to disk and return the file path");
		response.Pages[0].VerifiedBodyFile.Should().EndWith("body.js",
			because: "the verified body file must be named body.js");
		mockFs.File.ReadAllText(response.Pages[0].VerifiedBodyFile).Should().Be(ValidPageBody,
			because: "the file written to disk must contain the verified body from the read-back get-page");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports error when verification fails after successful save")]
	public async Task SyncPages_Should_Report_Error_When_Verification_Fails() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		PageGetCommand getCommand = CreateFailingPageGetCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>())
			.Returns(getCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			Verify: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "verification failure should result in overall failure");
		response.Pages[0].Error.Should().Contain("verification failed",
			because: "the error should indicate that verification failed after save");
	}

	[Test]
	[Category("Unit")]
	[Description("Propagates the command's insert->merge downgrade warning onto the per-page sync result (locks AppendCommandWarnings).")]
	public async Task SyncPages_Should_Surface_InsertDowngradeWarning_PerPage() {
		// Arrange — the stored schema inserts UsrName; the incoming body downgrades it to a merge.
		PageUpdateCommand updateCommand = CreatePageUpdateCommandWithInsertPriorBody();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", MergeUsrNameBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(
			because: "the downgrade is advisory and must not fail the per-page save");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "AppendCommandWarnings must attach the command warning even when client-side validation is skipped");
		response.Pages[0].Validation!.Warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("merge"),
			because: "the per-page result must propagate the command's insert->merge downgrade warning");
	}

	// ENG-89796: every page-body fixture must be syntactically valid JavaScript.
	// Earlier fixtures dropped the object-literal property keys (`viewConfigDiff:`,
	// `handlers:`, …) before each SCHEMA_* marker pair because the brace-counter
	// "syntax" check let them through. Acornima parses these fixtures for real
	// now, so the keys are required.
	private const string InsertUsrNamePriorBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private const string MergeUsrNameBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrName\",\"values\":{\"label\":\"X\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static PageUpdateCommand CreatePageUpdateCommandWithInsertPriorBody() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray { new JObject { ["UId"] = "test-uid", ["SchemaType"] = 9 } }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject { ["body"] = InsertUsrNamePriorBody }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), CreateHierarchyClientFor("test-uid"));
	}

	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static IPageDesignerHierarchyClient CreateHierarchyClientFor(string schemaUId, string packageUId = "test-pkg-uid") {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(schemaUId).Returns(packageUId);
		hierarchyClient.GetParentSchemas(schemaUId, packageUId).Returns([
			new PageDesignerHierarchySchema { UId = schemaUId, PackageUId = packageUId }
		]);
		return hierarchyClient;
	}

	private static PageUpdateCommand CreateSuccessfulPageUpdateCommand(int schemaType = 9) =>
		CreateSuccessfulPageUpdateCommandWithClient(out _, schemaType);

	private static PageUpdateCommand CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient, int schemaType = 9) {
		applicationClient = Substitute.For<IApplicationClient>();
		return ConfigureSuccessfulPageUpdateCommand(applicationClient, schemaType);
	}

	private static PageUpdateCommand ConfigureSuccessfulPageUpdateCommand(IApplicationClient applicationClient, int schemaType) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray {
					new JObject {
						["UId"] = "test-uid",
						["SchemaType"] = schemaType
					}
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject { ["body"] = "original" }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), CreateHierarchyClientFor("test-uid"));
	}

	private static PageUpdateCommand CreatePageUpdateCommandWithFailureForSchema(string failSchemaName) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => {
				string body = callInfo.ArgAt<string>(1);
				bool isFailing = body.Contains(failSchemaName);
				return new JObject {
					["success"] = !isFailing,
					["rows"] = isFailing
						? new JArray()
						: new JArray { new JObject { ["UId"] = "test-uid" } }
				}.ToString();
			});
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject { ["body"] = "original" }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), CreateHierarchyClientFor("test-uid"));
	}

	private static PageGetCommand CreateSuccessfulPageGetCommand() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray {
					new JObject {
						["Name"] = "UsrTodo_FormPage",
						["UId"] = "test-uid",
						["PackageUId"] = "test-package-uid",
						["PackageName"] = "UsrPkg",
						["ParentSchemaName"] = "BaseModulePage",
						["SchemaType"] = 9
					}
				}
			}.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("test-uid").Returns("test-package-uid");
		hierarchyClient.GetParentSchemas("test-uid", "test-package-uid")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "test-uid",
					Name = "UsrTodo_FormPage",
					PackageUId = "test-package-uid",
					PackageName = "UsrPkg",
					SchemaVersion = 1,
					Body = ValidPageBody
				}
			]);
		return new PageGetCommand(
			applicationClient,
			serviceUrlBuilder,
			Substitute.For<ILogger>(),
			hierarchyClient,
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()));
	}

	private static PageGetCommand CreateFailingPageGetCommand() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = false }.ToString());
		return new PageGetCommand(
			applicationClient,
			serviceUrlBuilder,
			Substitute.For<ILogger>(),
			Substitute.For<IPageDesignerHierarchyClient>(),
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()));
	}

	[Test]
	[Category("Unit")]
	[Description("SyncPages succeeds for a mobile page body (plain JSON) and skips AMD marker validation.")]
	public async Task SyncPages_Should_Succeed_For_Valid_Mobile_Json_Body() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand(schemaType: 10);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string mobileBody = """
			{
			  "viewConfigDiff": [],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrMobile_FormPage", mobileBody)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Error.Should().NotContain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "AMD marker validation errors must not appear for mobile JSON bodies");
		response.Pages[0].Error.Should().NotContain("Mobile page validation failed",
			because: "a valid mobile body should not produce mobile validation errors");
	}

	[Test]
	[Category("Unit")]
	[Description("SyncPages with validate:true rejects a mobile body that contains 'converters' section.")]
	public async Task SyncPages_Should_Reject_Mobile_Body_With_Converters_When_Validate_Is_True() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog);
		string mobileBodyWithConverters = """
			{
			  "viewConfigDiff": [],
			  "converters": {}
			}
			""";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrMobile_FormPage", mobileBodyWithConverters)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeFalse(
			because: "a mobile body containing 'converters' must be rejected during validation");
		response.Pages[0].Error.Should().Contain("converters",
			because: "the error should describe the disallowed key that caused validation to fail");
	}
}
