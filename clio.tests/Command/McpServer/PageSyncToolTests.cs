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
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
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
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
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
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
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
	[Description("Client-side validation rejects page body with missing markers")]
	public async Task SyncPages_Should_Reject_Invalid_Page_Body_When_Validation_Enabled() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrBad_FormPage", "define('BadPage', {})}")],
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
	[Description("Skips client-side validation when validate is false")]
	public async Task SyncPages_Should_Skip_Validation_When_Disabled() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
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
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
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
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
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
	[Description("Client-side validation rejects proxy standard field bindings before save")]
	public async Task SyncPages_Should_Reject_Proxy_Field_Bindings_When_Validation_Is_Enabled() {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
		string bodyWithProxyBinding = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"values\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithProxyBinding)],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeFalse(
			because: "sync-pages should block the known broken proxy field pattern before save");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail semantic validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "sync-pages should return validation details for blocked field bindings");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "semantic field validation contributes to the content-ok decision");
		response.Pages[0].Error.Should().Contain("$UsrStatus")
			.And.Contain("$PDS_UsrStatus",
				because: "the failure should explain both the rejected proxy binding and the expected datasource binding");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation surfaces warnings for explicit custom field caption resources")]
	public async Task SyncPages_Should_Surface_FieldCaptionWarnings_When_ExplicitResources_Are_Provided() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
		string bodyWithExplicitFieldCaption = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"#ResourceString(UsrStatus_caption)#\",\"control\":\"$PDS_UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
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
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
		string bodyWithResource = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{ values: { caption: \"#ResourceString(UsrTitle)#\" } }]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
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
		PageSyncTool tool = new(commandResolver, mockFs);
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
		PageSyncTool tool = new(commandResolver, new MockFileSystem());
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

	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static IPageDesignerHierarchyClient CreateHierarchyClientFor(string schemaUId, string packageUId = "test-pkg-uid") {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(schemaUId).Returns(packageUId);
		hierarchyClient.GetParentSchemas(schemaUId, packageUId).Returns([
			new PageDesignerHierarchySchema { UId = schemaUId, PackageUId = packageUId }
		]);
		return hierarchyClient;
	}

	private static PageUpdateCommand CreateSuccessfulPageUpdateCommand() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray { new JObject { ["UId"] = "test-uid" } }
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
						["ParentSchemaName"] = "BaseModulePage"
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
}
