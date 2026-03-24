using System;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class PageSyncToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for page-sync")]
	public void PageSyncTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = PageSyncTool.ToolName;

		// Assert
		toolName.Should().Be("page-sync",
			because: "the page-sync MCP tool identifier must remain stable for callers");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks page-sync as destructive and not read-only")]
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
			because: "page-sync mutates remote page schemas and should not be marked read-only");
		destructive.Should().BeTrue(
			because: "page-sync modifies remote page schemas and should warn clients");
	}

	[Test]
	[Category("Unit")]
	[Description("Successfully updates a single page when Creatio responds with success")]
	public void SyncPages_Should_Succeed_For_Valid_Page() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		PageSyncTool tool = new(commandResolver);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

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
	public void SyncPages_Should_Process_Multiple_Pages() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		PageSyncTool tool = new(commandResolver);
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody),
				new PageSyncPageInput("UsrTodo_ListPage", ValidPageBody)
			],
			Validate: false);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

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
	public void SyncPages_Should_Continue_On_Failure() {
		// Arrange
		PageUpdateCommand updateCommand = CreatePageUpdateCommandWithFailureForSchema("UsrBroken_FormPage");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		PageSyncTool tool = new(commandResolver);
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrBroken_FormPage", ValidPageBody),
				new PageSyncPageInput("UsrWorking_ListPage", ValidPageBody)
			],
			Validate: false);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

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
	public void SyncPages_Should_Reject_Invalid_Page_Body_When_Validation_Enabled() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageSyncTool tool = new(commandResolver);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrBad_FormPage", "define('BadPage', {})}")],
			Validate: true);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

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
	public void SyncPages_Should_Skip_Validation_When_Disabled() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		PageSyncTool tool = new(commandResolver);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrPage", ValidPageBody)],
			Validate: false);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "validation is disabled so the page should be updated directly");
		response.Pages[0].Validation.Should().BeNull(
			because: "no validation results should be present when validation is skipped");
	}

	[Test]
	[Category("Unit")]
	[Description("Passes page resources through page-sync and returns the registered resource count from page-update.")]
	public void SyncPages_Should_Surface_Registered_Resources() {
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
		PageUpdateCommand updateCommand = new(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		PageSyncTool tool = new(commandResolver);
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
			Validate: false);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "page-sync should forward resources into page-update and keep the successful response");
		response.Pages[0].ResourcesRegistered.Should().Be(1,
			because: "the page-sync response should preserve the number of resources registered by page-update");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes page-sync request and response resource fields using the documented MCP names.")]
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
					ResourcesRegistered = 1
				}
			]
		};

		// Act
		string serializedArgs = System.Text.Json.JsonSerializer.Serialize(args);
		string serializedResponse = System.Text.Json.JsonSerializer.Serialize(response);

		// Assert
		serializedArgs.Should().Contain("\"resources\":\"{\\u0022UsrTitle\\u0022:\\u0022Title\\u0022}\"",
			because: "page-sync should include the optional page resources payload when it is provided");
		serializedResponse.Should().Contain("\"resources-registered\":1",
			because: "page-sync should serialize the registered-resource count using the documented MCP field name");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies page content after save when verify is true")]
	public void SyncPages_Should_Verify_After_Save_When_Enabled() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		PageGetCommand getCommand = CreateSuccessfulPageGetCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>())
			.Returns(getCommand);
		PageSyncTool tool = new(commandResolver);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			Verify: true);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

		// Assert
		response.Success.Should().BeTrue(
			because: "both save and verification should succeed");
		response.Pages[0].Success.Should().BeTrue(
			because: "the page was saved and verified successfully");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports error when verification fails after successful save")]
	public void SyncPages_Should_Report_Error_When_Verification_Fails() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		PageGetCommand getCommand = CreateFailingPageGetCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>())
			.Returns(getCommand);
		PageSyncTool tool = new(commandResolver);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			Verify: true);

		// Act
		PageSyncResponse response = tool.SyncPages(args);

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
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>());
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
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>());
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
