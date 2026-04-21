using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class PageToolsTests {

	[Test]
	[Description("Verifies that PageListTool has the correct MCP tool name")]
	public void PageListTool_HasCorrectName() {
		PageListTool.ToolName.Should().Be("list-pages", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("Verifies that PageGetTool has the correct MCP tool name")]
	public void PageGetTool_HasCorrectName() {
		PageGetTool.ToolName.Should().Be("get-page", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("Verifies that PageUpdateTool has the correct MCP tool name")]
	public void PageUpdateTool_HasCorrectName() {
		PageUpdateTool.ToolName.Should().Be("update-page", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("Verifies that PageCreateTool has the correct MCP tool name")]
	public void PageCreateTool_HasCorrectName() {
		PageCreateTool.ToolName.Should().Be("create-page", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("Verifies that PageTemplatesListTool has the correct MCP tool name")]
	public void PageTemplatesListTool_HasCorrectName() {
		PageTemplatesListTool.ToolName.Should().Be("list-page-templates", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("Serializes create-page MCP request arguments using kebab-case field names")]
	public void PageCreateToolArgs_Should_Serialize_Using_Kebab_Case_Field_Names() {
		PageCreateArgs args = new(
			"UsrDemo_BlankPage", "BlankPageTemplate", "Custom",
			"Demo page", "Demo description", "UsrDemoEntity",
			"sandbox", null, null, null);

		string json = System.Text.Json.JsonSerializer.Serialize(args);

		json.Should().Contain("\"schema-name\":\"UsrDemo_BlankPage\"");
		json.Should().Contain("\"template\":\"BlankPageTemplate\"");
		json.Should().Contain("\"package-name\":\"Custom\"");
		json.Should().Contain("\"entity-schema-name\":\"UsrDemoEntity\"");
		json.Should().Contain("\"environment-name\":\"sandbox\"");
		json.Should().NotContain("\"schemaName\"");
		json.Should().NotContain("\"packageName\"");
		json.Should().NotContain("\"dry-run\"");
		json.Should().NotContain("\"dryRun\"");
	}

	[Test]
	[Description("Serializes list-page-templates MCP request arguments using kebab-case field names")]
	public void PageTemplatesListArgs_Should_Serialize_Using_Kebab_Case_Field_Names() {
		PageTemplatesListArgs args = new("web", "sandbox", null, null, null);

		string json = System.Text.Json.JsonSerializer.Serialize(args);

		json.Should().Contain("\"schema-type\":\"web\"");
		json.Should().Contain("\"environment-name\":\"sandbox\"");
		json.Should().NotContain("\"schemaType\"");
	}

	[Test]
	[Description("Serializes page MCP request arguments using kebab-case field names")]
	public void PageToolArgs_Should_Serialize_Using_Kebab_Case_Field_Names() {
		// Arrange
		PageGetArgs getArgs = new("UsrTodo_FormPage", "sandbox", "https://sandbox", "Supervisor", "Supervisor");
		PageListArgs listArgs = new("UsrTodo", null, "FormPage", 25, "sandbox", "https://sandbox", "Supervisor", "Supervisor");
		PageListArgs listArgsByApp = new(null, "UsrTodo", "FormPage", 25, "sandbox", "https://sandbox", "Supervisor", "Supervisor");
		PageUpdateArgs updateArgs = new("UsrTodo_FormPage", "define(...)", "{\"UsrTitle\":\"Title\"}", true, "sandbox", "https://sandbox", "Supervisor", "Supervisor");

		// Act
		string getJson = System.Text.Json.JsonSerializer.Serialize(getArgs);
		string listJson = System.Text.Json.JsonSerializer.Serialize(listArgs);
		string listByAppJson = System.Text.Json.JsonSerializer.Serialize(listArgsByApp);
		string updateJson = System.Text.Json.JsonSerializer.Serialize(updateArgs);

		// Assert
		getJson.Should().Contain("\"schema-name\":\"UsrTodo_FormPage\"",
			because: "get-page should expose the normalized schema-name request field");
		getJson.Should().Contain("\"environment-name\":\"sandbox\"",
			because: "get-page should expose the normalized environment-name request field");
		getJson.Should().NotContain("\"schemaName\"",
			because: "get-page should no longer serialize the removed camelCase request field");
		getJson.Should().NotContain("\"environmentName\"",
			because: "get-page should no longer serialize the removed camelCase request field");
		listJson.Should().Contain("\"package-name\":\"UsrTodo\"",
			because: "list-pages should expose the normalized package-name request field");
		listJson.Should().Contain("\"search-pattern\":\"FormPage\"",
			because: "list-pages should expose the normalized search-pattern request field");
		listJson.Should().Contain("\"environment-name\":\"sandbox\"",
			because: "list-pages should expose the normalized environment-name request field");
		listJson.Should().NotContain("\"packageName\"",
			because: "list-pages should no longer serialize the removed camelCase request field");
		listJson.Should().NotContain("\"searchPattern\"",
			because: "list-pages should no longer serialize the removed camelCase request field");
		listByAppJson.Should().Contain("\"code\":\"UsrTodo\"",
			because: "list-pages should expose the normalized code request field when app discovery is used");
		updateJson.Should().Contain("\"schema-name\":\"UsrTodo_FormPage\"",
			because: "update-page should expose the normalized schema-name request field");
		updateJson.Should().Contain("\"dry-run\":true",
			because: "update-page should expose the normalized dry-run request field");
		updateJson.Should().Contain("\"resources\":\"{\\u0022UsrTitle\\u0022:\\u0022Title\\u0022}\"",
			because: "update-page should include the optional resources payload when it is provided");
		updateJson.Should().Contain("\"environment-name\":\"sandbox\"",
			because: "update-page should expose the normalized environment-name request field");
		updateJson.Should().NotContain("\"schemaName\"",
			because: "update-page should no longer serialize the removed camelCase request field");
		updateJson.Should().NotContain("\"dryRun\"",
			because: "update-page should no longer serialize the removed camelCase request field");
		updateJson.Should().NotContain("\"environmentName\"",
			because: "update-page should no longer serialize the removed camelCase request field");
	}

	[Test]
	[Description("Rejects legacy list-pages aliases so callers do not silently fall back to an unscoped query.")]
	public void PageListTool_Should_Reject_Legacy_AppCode_Alias() {
		PageListCommand command = Substitute.For<PageListCommand>(
			Substitute.For<IApplicationClient>(),
			Substitute.For<IServiceUrlBuilder>(),
			Substitute.For<ILogger>());
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		PageListTool tool = new(command, logger, resolver);
		PageListArgs args = System.Text.Json.JsonSerializer.Deserialize<PageListArgs>("{\"app-code\":\"UsrTodoApp\"}")!;

		PageListResponse response = tool.ListPages(args);

		response.Success.Should().BeFalse(
			because: "legacy aliases should be rejected before list-pages runs an unscoped discovery query");
		response.Error.Should().Be("Use 'code' instead of 'app-code'.",
			because: "the MCP tool should direct callers to the canonical selector field");
	}

	[Test]
	[Description("Prompt guidance for page MCP tools references kebab-case request arguments and the optional resources payload.")]
	public void PagePrompt_Should_Mention_Kebab_Case_Arguments_And_Resources() {
		// Arrange

		// Act
		string prompt = PagePrompt.GetPage("UsrTodo_FormPage", "sandbox");

		// Assert
		prompt.Should().Contain("`schema-name`",
			because: "get-page prompt guidance should match the current MCP argument contract");
		prompt.Should().Contain("`code`",
			because: "page guidance should mention code as a valid discovery selector for list-pages");
		prompt.Should().Contain("`environment-name`",
			because: "get-page prompt guidance should match the current MCP argument contract");
		prompt.Should().Contain("call `reg-web-app` first",
			because: "page guidance should prefer registering the environment instead of normalizing direct URL credentials into the workflow");
		prompt.Should().Contain("emergency recovery flow",
			because: "page guidance should keep direct connection args in a fallback-only role");
		prompt.Should().Contain($"`{ComponentInfoTool.ToolName}`",
			because: "get-page prompt guidance should direct callers to get-component-info for unfamiliar Freedom UI types");
		prompt.Should().Contain(GuidanceGetTool.ToolName,
			because: "get-page prompt guidance should route guide lookups through the dedicated guidance tool");
		prompt.Should().Contain("`existing-app-maintenance`",
			because: "get-page prompt guidance should point callers to the MCP-owned existing-app maintenance guide name");
		prompt.Should().Contain("`page-schema-validators`",
			because: "get-page prompt guidance should point validator edits to the dedicated clio-owned validator guide name");
		prompt.Should().Contain($"you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-validators` before proposing or applying changes",
			because: "validator guidance should be mandatory before authorship so callers do not drift into handler syntax");
		prompt.Should().Contain("must not author validator changes until that guidance has been read",
			because: "validator guidance should be framed as a hard workflow prerequisite rather than an optional recommendation");
		prompt.Should().Contain("never use handler signatures like `handler(request, next)`",
			because: "validator guidance should explicitly block handler contract leakage into SCHEMA_VALIDATORS");
		prompt.Should().Contain($"`{ToolContractGetTool.ToolName}`",
			because: "get-page prompt guidance should bootstrap page workflows from the authoritative MCP contract before the first page tool call");
		prompt.Should().Contain($"`{PageSyncTool.ToolName}`",
			because: "page guidance should advertise sync-pages as the canonical page write path");
		prompt.Should().Contain("`validate`",
			because: "page guidance should surface the canonical validation semantics for sync-pages");
		prompt.Should().Contain("`verify`",
			because: "page guidance should surface the optional read-back semantics for sync-pages");
		prompt.Should().Contain("`resources`",
			because: "page guidance should tell callers how to preserve ResourceString macros during sync-pages");
		prompt.Should().Contain("valid JSON object string",
			because: "get-page prompt guidance should clarify that malformed resource payloads are rejected");
		prompt.Should().Contain("$PDS_*",
			because: "page guidance should steer standard fields toward direct datasource-backed bindings");
		prompt.Should().Contain("$UsrStatus -> PDS.UsrStatus",
			because: "page guidance should call out the proxy binding pattern that update-page now rejects");
		prompt.Should().Contain("Usr*_label",
			because: "page guidance should reserve custom Usr label resources for standalone UI only");
		prompt.Should().Contain("`list-pages -> get-page -> sync-pages -> get-page`",
			because: "page guidance should describe the canonical maintenance sequence for page edits");
		prompt.Should().Contain("single-page dry-run or legacy save workflows",
			because: "page guidance should keep update-page in a fallback-only role");
		prompt.Should().Contain("body.js",
			because: "page guidance should explicitly call out body.js as the editable JavaScript source");
		prompt.Should().Contain("Do not send bundle data back to page tools",
			because: "page guidance should explicitly reject submitting bundle content to write tools");
		prompt.Should().Contain("do not send a nested object payload",
			because: "page guidance should explicitly reject non-string resources payloads");
		prompt.Should().NotContain("Use `sync-pages` only when you need to save multiple pages in one workflow.",
			because: "sync-pages should no longer be presented as a multi-page-only path");
		prompt.Should().NotContain("`schemaName`",
			because: "get-page prompt guidance should no longer advertise removed camelCase request fields");
		prompt.Should().NotContain("`environmentName`",
			because: "get-page prompt guidance should no longer advertise removed camelCase request fields");
	}

	[Test]
	[Description("get-page tool description routes callers to the canonical validator guide so they read it before authoring validators.")]
	public void PageGetTool_Description_Should_Contain_Validator_Binding_Location_Guidance() {
		// Arrange
		var method = typeof(PageGetTool).GetMethod(nameof(PageGetTool.GetPage))!;
		var descAttr = method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Act
		string description = descAttr.Description;

		// Assert
		description.Should().Contain("SCHEMA_VALIDATORS",
			because: "get-page description should surface the section name so callers know which guide to read before editing");
		description.Should().Contain("call get-guidance with name `page-schema-validators`",
			because: "get-page description should route callers to the dedicated validator guide through get-guidance");
	}

	[Test]
	[Description("sync-pages tool description routes callers to the canonical validator guide so they read it before authoring validators.")]
	public void PageSyncTool_Description_Should_Contain_Validator_Section_Authoring_Rules() {
		// Arrange
		var method = typeof(PageSyncTool).GetMethod(nameof(PageSyncTool.SyncPages))!;
		var descAttr = method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Act
		string description = descAttr.Description;

		// Assert
		description.Should().Contain("SCHEMA_VALIDATORS",
			because: "sync-pages description should surface the validator section name as part of body authoring rules");
		description.Should().Contain("call get-guidance with name `page-schema-validators`",
			because: "sync-pages description should route callers to the dedicated validator guide through get-guidance");
	}

	[Test]
	[Description("update-page tool description routes validator authoring to the dedicated guidance resource instead of duplicating validator rules inline.")]
	public void PageUpdateTool_Description_Should_Contain_Validator_Section_Authoring_Rules() {
		// Arrange
		var method = typeof(PageUpdateTool).GetMethod(nameof(PageUpdateTool.UpdatePage))!;
		var descAttr = method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Act
		string description = descAttr.Description;

		// Assert
		description.Should().Contain("SCHEMA_VALIDATORS",
			because: "update-page description should surface the validator section name as part of body authoring rules");
		description.Should().Contain("call get-guidance with name `page-schema-validators` first",
			because: "update-page description should make validator guidance a mandatory precondition before validator authoring");
	}

	[Test]
	[Description("get-page, sync-pages, and update-page tool descriptions all link to the validator guide so validator-specific rules live in one canonical location.")]
	public void PageTools_Descriptions_Should_Forbid_PDS_Control_Binding_For_Validators() {
		// Arrange
		var getDesc = typeof(PageGetTool).GetMethod(nameof(PageGetTool.GetPage))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;
		var syncDesc = typeof(PageSyncTool).GetMethod(nameof(PageSyncTool.SyncPages))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;
		var updateDesc = typeof(PageUpdateTool).GetMethod(nameof(PageUpdateTool.UpdatePage))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;

		// Act & Assert
		getDesc.Should().Contain("get-guidance",
			because: "get-page description must route callers to the validator guide through get-guidance");
		syncDesc.Should().Contain("get-guidance",
			because: "sync-pages description must route callers to the validator guide through get-guidance");
		updateDesc.Should().Contain("get-guidance",
			because: "update-page description must route callers to the validator guide instead of duplicating PDS binding rules inline");
	}

	[Test]
	[Description("get-page, sync-pages, and update-page tool descriptions enforce #ResourceString(KeyName)# for validator message params and forbid $Resources.Strings.KeyName.")]
	public void PageTools_Descriptions_Should_Enforce_ResourceString_Format_For_Validator_Messages() {
		// Arrange
		var getDesc = typeof(PageGetTool).GetMethod(nameof(PageGetTool.GetPage))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;
		var syncDesc = typeof(PageSyncTool).GetMethod(nameof(PageSyncTool.SyncPages))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;
		var updateDesc = typeof(PageUpdateTool).GetMethod(nameof(PageUpdateTool.UpdatePage))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;

		// Act & Assert
		// all tools route callers to the guide which contains the ResourceString rules
		getDesc.Should().Contain("get-guidance",
			because: "get-page description must route callers to the validator guide which documents the #ResourceString(KeyName)# requirement");
		syncDesc.Should().Contain("get-guidance",
			because: "sync-pages description must route callers to the validator guide which documents the #ResourceString(KeyName)# requirement");
		updateDesc.Should().Contain("get-guidance",
			because: "update-page description must route callers to the validator guide which documents the #ResourceString(KeyName)# requirement");
	}

	[Test]
	[Description("Serializes update-page resource registration metadata in the command response.")]
	public void PageUpdateResponse_Should_Serialize_Resource_Registration_Metadata() {
		// Arrange
		PageUpdateResponse response = new() {
			Success = true,
			SchemaName = "UsrTodo_FormPage",
			BodyLength = 123,
			DryRun = false,
			ResourcesRegistered = 2,
			RegisteredResourceKeys = ["UsrTitle", "UsrDetails"]
		};

		// Act
		string serializedResponse = System.Text.Json.JsonSerializer.Serialize(response);

		// Assert
		serializedResponse.Should().Contain("\"resourcesRegistered\":2",
			because: "update-page should surface the number of registered child-schema resources");
		serializedResponse.Should().Contain("\"registeredResourceKeys\":[\"UsrTitle\",\"UsrDetails\"]",
			because: "update-page should surface the concrete resource keys that were registered");
	}

	[Test]
	[Description("PageGetTool returns the nested MCP response contract with page, bundle, raw, and packageUId")]
	public void PageGetTool_WhenCalled_ReturnsNestedResponseContract() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(CreateMetadataResponse(
				"UsrMcp_FormPage",
				"tool-page-uid",
				"tool-package-uid",
				"UsrMcp",
				"BasePage").ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("tool-page-uid").Returns("tool-package-uid");
		hierarchyClient.GetParentSchemas("tool-page-uid", "tool-package-uid")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "tool-page-uid",
					Name = "UsrMcp_FormPage",
					PackageUId = "tool-package-uid",
					PackageName = "UsrMcp",
					SchemaVersion = 1,
					Body = CreatePageBody("""
						[
						  {
						    operation: 'insert',
						    name: 'MainContainer',
						    values: {
						      type: 'crt.FlexContainer'
						    }
						  }
						]
						""")
				}
			]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>())
			.Returns(command);
		MockFileSystem mockFs = new();
		PageGetTool tool = new(command, logger, commandResolver, mockFs);

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		// Assert
		response.Success.Should().BeTrue(
			because: "the MCP tool should surface the successful get-page command result");
		response.Page.PackageUId.Should().Be("tool-package-uid",
			because: "the nested page metadata should include packageUId for MCP callers");
		response.Bundle.Should().BeNull(
			because: "bundle should be omitted from the response when files are written to disk");
		response.Raw.Should().BeNull(
			because: "raw should be omitted from the response when files are written to disk");
		response.Files.Should().NotBeNull(
			because: "the MCP tool should return file paths when output is written to disk");
		string serializedResponse = System.Text.Json.JsonSerializer.Serialize(response);
		JObject serializedObject = JObject.Parse(serializedResponse);
		serializedResponse.Should().Contain("\"page\"",
			because: "the serialized MCP response should include the page block");
		serializedResponse.Should().NotContain("\"bundle\"",
			because: "the serialized MCP response should omit bundle when files are written");
		serializedResponse.Should().NotContain("\"raw\"",
			because: "the serialized MCP response should omit raw when files are written");
		serializedResponse.Should().Contain("\"files\"",
			because: "the serialized MCP response should include file paths");
		serializedObject["schemaName"].Should().BeNull(
			because: "the old flat response contract should no longer emit schemaName at the root");
	}

	[Test]
	[Description("TryListPages returns success with pages when DataService returns valid rows")]
	public void TryListPages_WhenDataServiceReturnsRows_ReturnsSuccessWithPages() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/DataService/json/SyncReply/SelectQuery");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "TestPage1", ["UId"] = "uid-1", ["PackageName"] = "TestPkg", ["ParentSchemaName"] = "PageWithTabsFreedomTemplate" },
				new JObject { ["Name"] = "TestPage2", ["UId"] = "uid-2", ["PackageName"] = "TestPkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeTrue("because the DataService returned a successful response");
		response.Success.Should().BeTrue("because the query succeeded");
		response.Count.Should().Be(2, "because two rows were returned from the DataService");
		response.Pages.Should().HaveCount(2, "because each row maps to a page item");
		response.Pages[0].SchemaName.Should().Be("TestPage1", "because the first row has Name=TestPage1");
		response.Pages[0].ParentSchemaName.Should().Be("PageWithTabsFreedomTemplate",
			"because list-pages should now preserve direct parent schema context for target selection");
	}

	[Test]
	[Description("TryListPages projects the direct parent schema name into the select query and response payload")]
	public void TryListPages_Should_Project_Parent_Schema_Context() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "TestPage1", ["UId"] = "uid-1", ["PackageName"] = "TestPkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };

		bool result = command.TryListPages(options, out PageListResponse response);

		result.Should().BeTrue("because the DataService returned a successful response");
		response.Pages[0].ParentSchemaName.Should().Be("BasePage",
			"because the response payload should keep the parent schema context returned by the query");
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("[SysSchema:Id:Parent].Name") && body.Contains("ParentSchemaName")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryListPages returns failure when DataService returns unsuccessful response")]
	public void TryListPages_WhenDataServiceFails_ReturnsFailure() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject { ["success"] = false };
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeFalse("because the DataService returned success=false");
		response.Success.Should().BeFalse("because the query failed");
		response.Error.Should().Be("Query failed", "because a non-success DataService response maps to this error");
	}

	[Test]
	[Description("TryListPages filters by package name when PackageName is provided")]
	public void TryListPages_WhenPackageNameProvided_IncludesPackageFilter() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { PackageName = "MyPackage", Limit = 50 };
		command.TryListPages(options, out PageListResponse response);
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("SysPackage.Name") && body.Contains("MyPackage")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryListPages resolves the primary package from app-code before querying pages")]
	public void TryListPages_WhenAppCodeProvided_ResolvesPrimaryPackage_And_ReturnsPages() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		serviceUrlBuilder.Build("ServiceModel/ApplicationPackagesService.svc/GetApplicationPackages").Returns("http://test/packages");
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(
				new JObject {
					["success"] = true,
					["rows"] = new JArray {
						new JObject { ["Id"] = "app-uid" }
					}
				}.ToString(),
				new JObject {
					["success"] = true,
					["packages"] = new JArray {
						new JObject {
							["name"] = "UsrTodo",
							["isApplicationPrimaryPackage"] = true
						}
					}
				}.ToString(),
				new JObject {
					["success"] = true,
					["rows"] = new JArray {
						new JObject { ["Name"] = "UsrTodo_FormPage", ["UId"] = "page-uid", ["PackageName"] = "UsrTodo", ["ParentSchemaName"] = "BasePage" }
					}
				}.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { AppCode = "UsrTodoApp", Limit = 50 };

		bool result = command.TryListPages(options, out PageListResponse response);

		result.Should().BeTrue("because the app-code selector should resolve the primary package and then list its pages");
		response.Success.Should().BeTrue("because the page query succeeded after package resolution");
		response.Pages.Should().ContainSingle("because one page row was returned");
		response.Pages[0].SchemaName.Should().Be("UsrTodo_FormPage");
		applicationClient.Received(3).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("SysPackage.Name") && body.Contains("UsrTodo")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryListPages rejects the invalid selector combination of package-name and app-code")]
	public void TryListPages_WhenPackageNameAndAppCodeProvided_ReturnsFailure() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { PackageName = "UsrTodo", AppCode = "UsrTodoApp", Limit = 50 };

		bool result = command.TryListPages(options, out PageListResponse response);

		result.Should().BeFalse("because list-pages should reject ambiguous selectors");
		response.Success.Should().BeFalse("because the invalid selector combination should not execute");
		response.Error.Should().Contain("either package-name or app-code",
			because: "the failure should explain the mutually exclusive selector rule");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("TryListPages returns empty list when DataService returns no rows")]
	public void TryListPages_WhenNoRows_ReturnsEmptyList() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeTrue("because the query itself succeeded even with zero results");
		response.Success.Should().BeTrue("because empty result set is still a successful query");
		response.Count.Should().Be(0, "because no rows were returned");
		response.Pages.Should().BeEmpty("because there are no pages to list");
	}

	[Test]
	[Description("TryListPages catches exceptions and returns error response")]
	public void TryListPages_WhenExceptionThrown_ReturnsErrorResponse() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(x => throw new System.Exception("Connection refused"));
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeFalse("because an exception occurred during execution");
		response.Success.Should().BeFalse("because the operation failed with an exception");
		response.Error.Should().Be("Connection refused", "because the exception message should be propagated");
	}

	[Test]
	[Description("Execute delegates to TryListPages and logs result")]
	public void Execute_WhenSuccessful_ReturnsZeroAndLogsResponse() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg1" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		int exitCode = command.Execute(options);
		exitCode.Should().Be(0, "because TryListPages succeeded");
		logger.Received(1).WriteInfo(Arg.Is<string>(s => s.Contains("Page1")));
	}

	[Test]
	[Description("TryGetPage uses the GetParentSchemas designer endpoint without duplicating the /0 prefix")]
	public void TryGetPage_UsesGetParentSchemasDesignerEndpoint() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetParentSchemas")
			.Returns("http://test/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetParentSchemas");
		JObject metadataResponse = CreateMetadataResponse(
			"TestPage_FormPage",
			"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
			"pkg-uid",
			"TestPkg",
			"PageWithTabsFreedomTemplate");
		JObject hierarchyResponse = CreateHierarchyResponse(
			new JObject {
				["uId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
				["name"] = "TestPage_FormPage",
				["package"] = new JObject {
					["uId"] = "pkg-uid",
					["name"] = "TestPkg"
				},
				["schemaVersion"] = 1,
				["body"] = CreatePageBody("""
					[
					  {
					    operation: 'insert',
					    name: 'NameField',
					    parentName: 'MainContainer',
					    path: ['items'],
					    values: {
					      type: 'crt.Input'
					    }
					  }
					]
					""")
			},
			new JObject {
				["uId"] = "base-uid",
				["name"] = "PageWithTabsFreedomTemplate",
				["package"] = new JObject {
					["uId"] = "base-pkg-uid",
					["name"] = "CrtBase"
				},
				["schemaVersion"] = 1,
				["body"] = CreatePageBody("""
					[
					  {
					    operation: 'insert',
					    name: 'MainContainer',
					    values: {
					      type: 'crt.FlexContainer'
					    }
					  }
					]
					""")
			});
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(_ => ++callIndex == 1 ? metadataResponse.ToString() : hierarchyResponse.ToString());
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger);
		PageGetOptions options = new() { SchemaName = "TestPage_FormPage" };

		// Act
		bool result = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		result.Should().BeTrue(
			because: "the metadata query and hierarchy read both succeeded");
		response.Success.Should().BeTrue(
			because: "the page read should return a success envelope");
		response.Bundle.Name.Should().Be("TestPage_FormPage",
			because: "the designer hierarchy should be interpreted in current-page-first order");
		serviceUrlBuilder.Received(1).Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetParentSchemas");
		serviceUrlBuilder.DidNotReceive().Build(Arg.Is<string>(path => path.Contains("/0/ServiceModel")));
	}

	[Test]
	[Description("TryGetPage returns the new nested bundle envelope when a page is found")]
	public void TryGetPage_WhenSchemaExists_ReturnsBundleEnvelope() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => $"http://test{callInfo.Arg<string>()}");
		JObject metadataResponse = CreateMetadataResponse(
			"UsrApp_FormPage",
			"11111111-2222-3333-4444-555555555555",
			"99999999-2222-3333-4444-555555555555",
			"UsrApp",
			"PageWithTabsFreedomTemplate");
		string parentBody = CreatePageBody("""
			[
			  {
			    operation: 'insert',
			    name: 'MainContainer',
			    values: {
			      type: 'crt.FlexContainer',
			      items: []
			    }
			  }
			]
			""",
			viewModelConfig: """
			{
			  values: {
			    ParentValue: {
			      _id: 'ParentValue',
			      type: 'crt.StringAttribute'
			    }
			  }
			}
			""",
			modelConfig: """
			{
			  dataSources: {
			    BaseDS: {
			      type: 'crt.EntityDataSource'
			    }
			  }
			}
			""");
		string expectedBody = CreatePageBody("""
			[
			  {
			    operation: 'insert',
			    name: 'NameField',
			    parentName: 'MainContainer',
			    path: ['items'],
			    values: {
			      type: 'crt.Input'
			    }
			  }
			]
			""",
			viewModelConfigDiff: """
			[
			  {
			    operation: 'insert',
			    path: ['values'],
			    propertyName: 'ChildValue',
			    values: {
			      _id: 'ChildValue',
			      type: 'crt.StringAttribute'
			    }
			  }
			]
			""",
			handlers: "[{ request: 'crt.HandleViewModelInitRequest' }]");
		JObject hierarchyResponse = CreateHierarchyResponse(
			new JObject {
				["uId"] = "11111111-2222-3333-4444-555555555555",
				["name"] = "UsrApp_FormPage",
				["package"] = new JObject {
					["uId"] = "99999999-2222-3333-4444-555555555555",
					["name"] = "UsrApp"
				},
				["schemaVersion"] = 1,
				["body"] = expectedBody,
				["localizableStrings"] = new JArray {
					new JObject {
						["name"] = "Title",
						["values"] = new JArray {
							new JObject {
								["cultureName"] = "en-US",
								["value"] = "Child title"
							}
						}
					}
				},
				["parameters"] = new JArray {
					new JObject {
						["uId"] = "param-child-uid",
						["name"] = "AccountId",
						["caption"] = new JObject {
							["en-US"] = "Account"
						},
						["type"] = 11,
						["required"] = false,
						["parentSchemaUId"] = "11111111-2222-3333-4444-555555555555",
						["lookup"] = "account-schema-uid",
						["schema"] = "Account"
					}
				},
				["optionalProperties"] = new JArray {
					new JObject {
						["key"] = "layout",
						["value"] = "child"
					}
				}
			},
			new JObject {
				["uId"] = "base-page-uid",
				["name"] = "PageWithTabsFreedomTemplate",
				["package"] = new JObject {
					["uId"] = "base-package-uid",
					["name"] = "CrtBase"
				},
				["schemaVersion"] = 1,
				["body"] = parentBody,
				["localizableStrings"] = new JArray {
					new JObject {
						["name"] = "Title",
						["values"] = new JArray {
							new JObject {
								["cultureName"] = "en-US",
								["value"] = "Base title"
							}
						}
					}
				},
				["parameters"] = new JArray {
					new JObject {
						["uId"] = "param-base-uid",
						["name"] = "ParentId",
						["caption"] = new JObject {
							["en-US"] = "Parent"
						},
						["type"] = 10,
						["required"] = true,
						["parentSchemaUId"] = "base-page-uid",
						["lookup"] = "contact-schema-uid",
						["schema"] = "Contact"
					}
				},
				["optionalProperties"] = new JArray {
					new JObject {
						["key"] = "layout",
						["value"] = "base"
					}
				}
			});
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(_ => ++callIndex == 1 ? metadataResponse.ToString() : hierarchyResponse.ToString());
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger);
		PageGetOptions options = new() { SchemaName = "UsrApp_FormPage" };

		// Act
		bool result = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		result.Should().BeTrue(
			because: "the page metadata, hierarchy, and body parsing all succeeded");
		response.Success.Should().BeTrue(
			because: "the returned envelope should indicate success");
		response.Page.SchemaName.Should().Be("UsrApp_FormPage",
			because: "the nested page metadata should include the schema name");
		response.Page.SchemaUId.Should().Be("11111111-2222-3333-4444-555555555555",
			because: "the nested page metadata should include the schema identifier");
		response.Page.PackageName.Should().Be("UsrApp",
			because: "the page metadata should include the owning package name");
		response.Page.PackageUId.Should().Be("99999999-2222-3333-4444-555555555555",
			because: "the page metadata should include the owning package identifier");
		response.Page.ParentSchemaName.Should().Be("PageWithTabsFreedomTemplate",
			because: "the page metadata should preserve the direct parent schema name");
		response.Bundle.Name.Should().Be("UsrApp_FormPage",
			because: "the bundle should be keyed to the current schema");
		response.Bundle.ViewConfig.Should().HaveCount(1,
			because: "the child view config should be merged into the inherited container hierarchy");
		response.Bundle.ViewConfig[0]!["items"]!.AsArray().Should().ContainSingle(
			because: "the inherited container should receive the inserted child component");
		response.Bundle.ViewModelConfig["values"]!["ParentValue"]!.Should().NotBeNull(
			because: "the bundle should preserve inherited view-model config entries");
		response.Bundle.ViewModelConfig["values"]!["ChildValue"]!["_id"]!.ToString().Should().Be("ChildValue",
			because: "the child diff should be applied to the view-model config");
		response.Bundle.ModelConfig["dataSources"]!["BaseDS"]!.Should().NotBeNull(
			because: "the bundle should preserve merged model config");
		response.Bundle.Resources.Strings["Title"]!["en-US"]!.ToString().Should().Be("Child title",
			because: "child resource values should override parent values for the same key and culture");
		response.Bundle.Parameters.Should().ContainSingle(parameter => parameter.Name == "AccountId" && parameter.IsOwnParameter,
			because: "own parameters should be marked on the merged bundle output");
		JToken? mergedOptionalProperty = response.Bundle.OptionalProperties
			.Select(node => node is null ? null : JToken.Parse(node.ToJsonString()))
			.SingleOrDefault(token => token?["key"]?.ToString() == "layout");
		mergedOptionalProperty.Should().NotBeNull(
			because: "the merged bundle should keep the overridden optional property");
		mergedOptionalProperty!["value"]!.ToString().Should().Be("child",
			because: "child optional properties should override duplicated parent keys");
		response.Bundle.Handlers.Should().Be("[{ request: 'crt.HandleViewModelInitRequest' }]",
			because: "non-JSON sections should come from the current schema part");
		response.Raw.Body.Should().Be(expectedBody,
			because: "raw.body should keep the current schema body for update-page round-trips");
		string serializedResponse = System.Text.Json.JsonSerializer.Serialize(response);
		serializedResponse.Should().Contain("\"page\"",
			because: "the MCP-facing response should serialize the nested page block");
		serializedResponse.Should().Contain("\"bundle\"",
			because: "the MCP-facing response should serialize the nested bundle block");
		serializedResponse.Should().Contain("\"raw\"",
			because: "the MCP-facing response should serialize the raw payload block");
		serializedResponse.Should().Contain("\"packageUId\":\"99999999-2222-3333-4444-555555555555\"",
			because: "the MCP-facing response should keep the package identifier stable");
	}

	[Test]
	[Description("TryGetPage returns error when schema metadata is not found in SysSchema")]
	public void TryGetPage_WhenSchemaNotFound_ReturnsError() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns("http://test/url");
		JObject metadataResponse = new() {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger);
		PageGetOptions options = new() { SchemaName = "NonExistentPage" };

		// Act
		bool result = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		result.Should().BeFalse(
			because: "the page cannot be read when SysSchema does not contain the requested item");
		response.Success.Should().BeFalse(
			because: "the envelope should report the failed page lookup");
		response.Error.Should().Contain("NonExistentPage").And.Contain("not found",
			because: "the failure should explain which schema could not be resolved");
	}

	[Test]
	[Description("TryGetPage returns a readable failure when the hierarchy client reports an invalid response")]
	public void TryGetPage_WhenHierarchyReadFails_ReturnsReadableError() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/DataService/json/SyncReply/SelectQuery");
		JObject metadataResponse = CreateMetadataResponse(
			"UsrBroken_FormPage",
			"broken-page-uid",
			"broken-package-uid",
			"UsrBroken",
			"PageWithTabsFreedomTemplate");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("broken-page-uid").Returns("broken-package-uid");
		hierarchyClient.GetParentSchemas("broken-page-uid", "broken-package-uid")
			.Returns(_ => throw new System.InvalidOperationException("Failed to load page schema hierarchy"));
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		PageGetOptions options = new() { SchemaName = "UsrBroken_FormPage" };

		// Act
		bool result = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		result.Should().BeFalse(
			because: "the read should fail when the designer hierarchy cannot be loaded");
		response.Success.Should().BeFalse(
			because: "the returned envelope should flag the failed hierarchy read");
		response.Error.Should().Be("Failed to load page schema hierarchy",
			because: "the hierarchy failure should stay readable in the command response");
	}

	[Test]
	[Description("TryGetPage returns a readable failure when a schema body contains malformed JSON5 markers")]
	public void TryGetPage_WhenBodySectionIsMalformed_ReturnsReadableError() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/DataService/json/SyncReply/SelectQuery");
		JObject metadataResponse = CreateMetadataResponse(
			"UsrMalformed_FormPage",
			"malformed-page-uid",
			"malformed-package-uid",
			"UsrMalformed",
			"PageWithTabsFreedomTemplate");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("malformed-page-uid").Returns("malformed-package-uid");
		hierarchyClient.GetParentSchemas("malformed-page-uid", "malformed-package-uid")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "malformed-page-uid",
					Name = "UsrMalformed_FormPage",
					PackageUId = "malformed-package-uid",
					PackageName = "UsrMalformed",
					SchemaVersion = 1,
					Body = CreatePageBody("[{ operation: 'insert', ]")
				}
			]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		PageGetOptions options = new() { SchemaName = "UsrMalformed_FormPage" };

		// Act
		bool result = command.TryGetPage(options, out PageGetResponse response);

		// Assert
		result.Should().BeFalse(
			because: "the malformed body section cannot be parsed into a bundle");
		response.Success.Should().BeFalse(
			because: "the returned envelope should surface the parsing failure");
		response.Error.Should().Contain("Failed to parse schema section 'SCHEMA_VIEW_CONFIG_DIFF'",
			because: "the parsing error should explain which marker section is invalid");
	}

	[Test]
	[Description("The raw body returned by TryGetPage can be passed unchanged to update-page dry-run")]
	public void TryGetPage_RawBody_CanBePassed_To_PageUpdate_DryRun() {
		// Arrange
		IApplicationClient getApplicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder getServiceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger getLogger = Substitute.For<ILogger>();
		getServiceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => $"http://test{callInfo.Arg<string>()}");
		JObject metadataResponse = CreateMetadataResponse(
			"UsrRoundTrip_FormPage",
			"roundtrip-page-uid",
			"roundtrip-package-uid",
			"UsrRoundTrip",
			"PageWithTabsFreedomTemplate");
		string rawBody = CreatePageBody();
		JObject hierarchyResponse = CreateHierarchyResponse(
			new JObject {
				["uId"] = "roundtrip-page-uid",
				["name"] = "UsrRoundTrip_FormPage",
				["package"] = new JObject {
					["uId"] = "roundtrip-package-uid",
					["name"] = "UsrRoundTrip"
				},
				["schemaVersion"] = 1,
				["body"] = rawBody
			});
		int getCallIndex = 0;
		getApplicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(_ => ++getCallIndex == 1 ? metadataResponse.ToString() : hierarchyResponse.ToString());
		PageGetCommand getCommand = CreatePageGetCommand(getApplicationClient, getServiceUrlBuilder, getLogger);
		PageGetOptions getOptions = new() { SchemaName = "UsrRoundTrip_FormPage" };

		IApplicationClient updateApplicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder updateServiceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger updateLogger = Substitute.For<ILogger>();
		updateServiceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		updateApplicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray {
					new JObject {
						["UId"] = "roundtrip-page-uid"
					}
				}
			}.ToString());
		PageUpdateCommand updateCommand = new(updateApplicationClient, updateServiceUrlBuilder, updateLogger, CreateHierarchyClientFor("roundtrip-page-uid", "roundtrip-pkg-uid"));

		// Act
		bool getResult = getCommand.TryGetPage(getOptions, out PageGetResponse getResponse);
		bool updateResult = updateCommand.TryUpdatePage(
			new PageUpdateOptions {
				SchemaName = "UsrRoundTrip_FormPage",
				Body = getResponse.Raw.Body,
				DryRun = true
			},
			out PageUpdateResponse updateResponse);

		// Assert
		getResult.Should().BeTrue(
			because: "the page must be readable before its raw body can be reused");
		updateResult.Should().BeTrue(
			because: "update-page should still accept the raw body returned by get-page");
		updateResponse.Success.Should().BeTrue(
			because: "the dry-run should validate the raw body without saving");
		updateResponse.DryRun.Should().BeTrue(
			because: "the regression should stay non-destructive");
		updateResponse.BodyLength.Should().Be(rawBody.Length,
			because: "update-page should receive the exact raw body emitted by get-page");
	}

	[Test]
	[Description("TryUpdatePage calls ServiceUrlBuilder without /0/ prefix for both GetSchema and SaveSchema")]
	public void TryUpdatePage_UsesCorrectDesignerServiceUrls_WithoutDoubleZeroPrefix() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema")
			.Returns("http://test/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema")
			.Returns("http://test/0/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["UId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }
			}
		};
		string validBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject {
				["body"] = "old body",
				["name"] = "Test_FormPage"
			}
		};
		var saveResponse = new JObject { ["success"] = true };
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				if (callIndex == 1) return metadataResponse.ToString();
				if (callIndex == 2) return getSchemaResponse.ToString();
				return saveResponse.ToString();
			});
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, CreateHierarchyClientFor("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
		var options = new PageUpdateOptions {
			SchemaName = "Test_FormPage",
			Body = validBody,
			DryRun = false
		};
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);
		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.BodyLength.Should().Be(validBody.Length);
		serviceUrlBuilder.Received(1).Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.Received(1).Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
		serviceUrlBuilder.DidNotReceive().Build(Arg.Is<string>(s => s.Contains("/0/ServiceModel")));
	}

	[Test]
	[Description("TryUpdatePage with dryRun skips GetSchema and SaveSchema calls")]
	public void TryUpdatePage_WhenDryRun_SkipsDesignerServiceCalls() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["UId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		string validBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, CreateHierarchyClientFor("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
		var options = new PageUpdateOptions {
			SchemaName = "Test_FormPage",
			Body = validBody,
			DryRun = true
		};
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);
		result.Should().BeTrue();
		response.DryRun.Should().BeTrue();
		response.BodyLength.Should().Be(validBody.Length);
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
	}

	[Test]
	[Description("TryUpdatePage returns error when schema not found")]
	public void TryUpdatePage_WhenSchemaNotFound_ReturnsError() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns("http://test/url");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		string validBody = "define(\"X\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageDesignerHierarchyClient>());
		var options = new PageUpdateOptions {
			SchemaName = "MissingPage",
			Body = validBody,
			DryRun = false
		};
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);
		result.Should().BeFalse(
			because: "a missing schema should fail before the command attempts to save it");
		response.Error.Should().Contain("MissingPage").And.Contain("not found",
			because: "the failure should identify the missing schema name");
	}

	[Test]
	[Description("TryUpdatePage rejects empty body payloads with a raw.body hint before any remote calls are made.")]
	public void TryUpdatePage_WhenBodyIsEmpty_ReturnsRawBodyHint() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageDesignerHierarchyClient>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrEmptyBody_FormPage",
			Body = string.Empty,
			DryRun = true
		};

		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeFalse(
			because: "an empty page body should fail before the command attempts remote validation or save");
		response.Success.Should().BeFalse(
			because: "the validation failure should be surfaced in the response envelope");
		response.Error.Should().Contain("get-page raw.body",
			because: "the error should teach callers which page payload shape is required");
		serviceUrlBuilder.ReceivedCalls().Should().BeEmpty(
			because: "validation should fail before the command builds any service URLs");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "validation should fail before the command sends any remote requests");
	}

	[Test]
	[Description("TryUpdatePage rejects malformed resources JSON before any remote calls are made.")]
	public void TryUpdatePage_WhenResourcesJsonIsInvalid_ReturnsValidationError() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageDesignerHierarchyClient>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrInvalidResources_FormPage",
			Body = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });",
			Resources = "{\"UsrTitle\":",
			DryRun = true
		};
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);
		result.Should().BeFalse(
			because: "malformed resource payloads should be rejected instead of being ignored");
		response.Success.Should().BeFalse(
			because: "the command should surface the validation failure");
		response.Error.Should().Be("resources must be a valid JSON object string",
			because: "the validation error should explain how the resources payload must be formatted");
		serviceUrlBuilder.ReceivedCalls().Should().BeEmpty(
			because: "validation should fail before the command builds any service URLs");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "validation should fail before the command sends any remote requests");
	}

	[Test]
	[Description("TryUpdatePage dry-run rejects proxy field bindings before any remote calls are made.")]
	public void TryUpdatePage_WhenFieldBindingUsesRejectedProxy_ReturnsValidationError() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageDesignerHierarchyClient>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrProxyBinding_FormPage",
			Body = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"values\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });",
			DryRun = true
		};

		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeFalse(
			because: "update-page should fail fast on standard fields that proxy a direct PDS path through a custom attribute");
		response.Success.Should().BeFalse(
			because: "the validation failure should be surfaced in the response envelope");
		response.Error.Should().Contain("invalid form field bindings")
			.And.Contain("$UsrStatus")
			.And.Contain("$PDS_UsrStatus",
				because: "the response should explain both the rejected proxy binding and the expected datasource binding");
		serviceUrlBuilder.ReceivedCalls().Should().BeEmpty(
			because: "validation should fail before the command builds any service URLs");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "validation should fail before the command sends any remote requests");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects schemas where validator params use $Resources.Strings.X binding syntax before saving to Creatio.")]
	public void PageUpdateTool_UpdatePage_Rejects_Schema_With_Resources_Strings_In_Validator_Params() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger);
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		PageUpdateTool tool = new(command, logger, commandResolver);
		string body = CreatePageBody(
			viewModelConfig: """{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"},"validators":{"UpperCase":{"type":"usr.UpperCase","params":{"message":"$Resources.Strings.UsrUpperCaseValidator_Message"}}}}}}""",
			validators: """{"usr.UpperCase":{"validator":function(config){return function(control){return null;}},"params":[{"name":"message"}],"async":false}}""");
		PageUpdateArgs args = new("UsrTest_FormPage", body, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "update-page must reject schemas where validator params use $Resources.Strings.X — this syntax is not evaluated in validator params");
		response.Error.Should().Contain("Validation failed",
			because: "the error message must clearly communicate that client-side validation blocked the save");
		response.Error.Should().Contain("#ResourceString(",
			because: "the error message must suggest the correct #ResourceString(KeyName)# format");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "validation must fail before any remote call is made to Creatio");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage accepts a schema that correctly uses #ResourceString(KeyName)# in validator params.")]
	public void PageUpdateTool_UpdatePage_Accepts_Schema_With_Correct_ResourceString_In_Validator_Params() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger);
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(System.Text.Json.JsonSerializer.Serialize(new { success = true }));
		PageUpdateTool tool = new(command, logger, commandResolver);
		string body = CreatePageBody(
			viewModelConfig: """{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"},"validators":{"UpperCase":{"type":"usr.UpperCase","params":{"message":"#ResourceString(UsrUpperCaseValidator_Message)#"}}}}}}""",
			validators: """{"usr.UpperCase":{"validator":function(config){return function(control){return null;}},"params":[{"name":"message"}],"async":false}}""");
		PageUpdateArgs args = new("UsrTest_FormPage", body, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args);

		// Assert
		response.Error.Should().NotContain("Validation failed",
			because: "#ResourceString(KeyName)# is the correct format and must not be rejected by the validator param check");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects crt.MaxLength bindings that use max instead of maxLength in params.")]
	public void PageUpdateTool_UpdatePage_Rejects_BuiltIn_MaxLength_With_Wrong_Param_Name() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger);
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		PageUpdateTool tool = new(command, logger, commandResolver);
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","control":"$UsrName"}}]""",
			viewModelConfig: """{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"},"validators":{"NameMaxLength":{"type":"crt.MaxLength","params":{"max":4}}}}}}""");
		PageUpdateArgs args = new("UsrTest_FormPage", body, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "crt.MaxLength expects maxLength in params, so max must be rejected before save");
		response.Error.Should().Contain("crt.MaxLength")
			.And.Contain("max")
			.And.Contain("maxLength",
				because: "the validation error should identify the wrong param and the required one");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the validation failure must happen before any remote save call is made");
	}

	[Test]
	[Description("PageUpdateTool description routes callers to the validator guide which covers both viewModelConfig and viewModelConfigDiff for validator bindings.")]
	public void PageUpdateTool_Description_Supports_Static_And_Diff_ViewModel_Config() {
		// Arrange
		System.ComponentModel.DescriptionAttribute? attribute = typeof(PageUpdateTool)
			.GetMethod(nameof(PageUpdateTool.UpdatePage))?
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.SingleOrDefault();

		// Act
		string? description = attribute?.Description;

		// Assert
		description.Should().Contain("get-guidance",
			because: "the tool contract should delegate static-vs-diff binding details to the canonical validator guidance");
	}

	[Test]
	[Description("PageSyncTool description routes callers to the validator guide which covers both viewModelConfig and viewModelConfigDiff binding variants.")]
	public void PageSyncTool_Description_Supports_Static_And_Diff_ViewModel_Config() {
		// Arrange
		System.ComponentModel.DescriptionAttribute? attribute = typeof(PageSyncTool)
			.GetMethod(nameof(PageSyncTool.SyncPages))?
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.SingleOrDefault();

		// Act
		string? description = attribute?.Description;

		// Assert
		description.Should().Contain("get-guidance",
			because: "sync-pages description must route callers to the validator guide which covers both static and diff-based viewModelConfig binding");
	}


	private static PageGetCommand CreatePageGetCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger,
		IPageDesignerHierarchyClient hierarchyClient = null) {
		return new PageGetCommand(
			applicationClient,
			serviceUrlBuilder,
			logger,
			hierarchyClient ?? new PageDesignerHierarchyClient(applicationClient, serviceUrlBuilder),
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()));
	}

	private static (PageGetTool tool, MockFileSystem mockFs) CreatePageGetToolWithBody(string body) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse("UsrMcp_FormPage", "uid-1", "pkg-1", "UsrMcp", "BasePage").ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas("uid-1", "pkg-1")
			.Returns([new PageDesignerHierarchySchema {
				UId = "uid-1", Name = "UsrMcp_FormPage",
				PackageUId = "pkg-1", PackageName = "UsrMcp",
				SchemaVersion = 1, Body = body
			}]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(command);
		MockFileSystem mockFs = new();
		return (new PageGetTool(command, logger, commandResolver, mockFs), mockFs);
	}

	private static JObject CreateMetadataResponse(
		string schemaName,
		string schemaUId,
		string packageUId,
		string packageName,
		string parentSchemaName) {
		return new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject {
					["Name"] = schemaName,
					["UId"] = schemaUId,
					["PackageName"] = packageName,
					["PackageUId"] = packageUId,
					["ParentSchemaName"] = parentSchemaName
				}
			}
		};
	}

	private static JObject CreateHierarchyResponse(params JObject[] values) {
		return new JObject {
			["success"] = true,
			["values"] = new JArray(values)
		};
	}

	private static string CreatePageBody(
		string viewConfigDiff = "[]",
		string viewModelConfig = "{}",
		string viewModelConfigDiff = "[]",
		string modelConfig = "{}",
		string modelConfigDiff = "[]",
		string deps = "[]",
		string args = "()",
		string handlers = "[]",
		string converters = "{}",
		string validators = "{}") {
		return $$"""
			define("TestPage", /**SCHEMA_DEPS*/{{deps}}/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/{{args}}/**SCHEMA_ARGS*/ {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiff}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{{viewModelConfig}}/**SCHEMA_VIEW_MODEL_CONFIG*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{{viewModelConfigDiff}}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfig: /**SCHEMA_MODEL_CONFIG*/{{modelConfig}}/**SCHEMA_MODEL_CONFIG*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{{modelConfigDiff}}/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/{{handlers}}/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{{converters}}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{{validators}}/**SCHEMA_VALIDATORS*/
				};
			});
			""";
	}

	[Test]
	[Category("Unit")]
	[Description("get-page writes body.js, bundle.json, meta.json and returns file paths instead of inline data")]
	public void PageGetTool_WhenCalled_WritesThreeFilesAndReturnsPaths() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse("UsrMcp_FormPage", "uid-1", "pkg-1", "UsrMcp", "BasePage").ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas("uid-1", "pkg-1")
			.Returns([new PageDesignerHierarchySchema {
				UId = "uid-1", Name = "UsrMcp_FormPage",
				PackageUId = "pkg-1", PackageName = "UsrMcp",
				SchemaVersion = 1, Body = CreatePageBody()
			}]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(command);
		MockFileSystem mockFs = new();
		PageGetTool tool = new(command, logger, commandResolver, mockFs);

		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		response.Success.Should().BeTrue(because: "page read and file write should both succeed");
		response.Bundle.Should().BeNull(because: "bundle must be omitted from MCP response when written to disk");
		response.Raw.Should().BeNull(because: "raw must be omitted from MCP response when written to disk");
		response.Files.Should().NotBeNull(because: "response should include file paths");
		response.Files.BodyFile.Should().EndWith("body.js", because: "body file must have .js extension");
		response.Files.BundleFile.Should().EndWith("bundle.json", because: "bundle file must have .json extension");
		response.Files.MetaFile.Should().EndWith("meta.json", because: "meta file must have .json extension");
		mockFs.AllFiles.Should().Contain(response.Files.BodyFile, because: "body.js must be written to disk");
		mockFs.AllFiles.Should().Contain(response.Files.BundleFile, because: "bundle.json must be written to disk");
		mockFs.AllFiles.Should().Contain(response.Files.MetaFile, because: "meta.json must be written to disk");
		mockFs.File.ReadAllText(response.Files.BodyFile).Should().NotBeNullOrWhiteSpace(
			because: "body.js must contain the raw JS body for update-page round-trips");
		string json = System.Text.Json.JsonSerializer.Serialize(response);
		json.Should().NotContain("\"bundle\":", because: "bundle must be absent from serialized MCP response");
		json.Should().NotContain("\"raw\":", because: "raw must be absent from serialized MCP response");
		json.Should().Contain("\"files\"", because: "file paths block must appear in serialized response");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page places files under .clio-pages/{schema-name}/ subdirectory")]
	public void PageGetTool_WhenCalled_FilesAreUnderDotClioPagesSubdirectory() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse("UsrMcp_FormPage", "uid-1", "pkg-1", "UsrMcp", "BasePage").ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas("uid-1", "pkg-1")
			.Returns([new PageDesignerHierarchySchema {
				UId = "uid-1", Name = "UsrMcp_FormPage",
				PackageUId = "pkg-1", PackageName = "UsrMcp",
				SchemaVersion = 1, Body = CreatePageBody()
			}]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(command);
		MockFileSystem mockFs = new();
		PageGetTool tool = new(command, logger, commandResolver, mockFs);

		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		response.Files.BodyFile.Should().Contain(".clio-pages",
			because: "files must be written under .clio-pages directory");
		response.Files.BodyFile.Should().Contain("UsrMcp_FormPage",
			because: "files must be grouped under the schema name subdirectory");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page normalizes proxy bindings before writing body.js so the file passes sync-pages validation.")]
	public void PageGetTool_WhenBodyHasProxyBindings_WritesNormalizedBodyFile() {
		// Arrange
		string proxyBody = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","label":"$Resources.Strings.PDS_UsrStatus","control":"$UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");
		(PageGetTool tool, MockFileSystem mockFs) = CreatePageGetToolWithBody(proxyBody);

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "get-page should succeed even when the source body has proxy bindings");
		string writtenBody = mockFs.File.ReadAllText(response.Files.BodyFile);
		writtenBody.Should().Contain("$PDS_UsrStatus",
			because: "get-page must normalize proxy binding $UsrStatus to $PDS_UsrStatus before writing body.js");
		writtenBody.Should().NotContain("\"$UsrStatus\"",
			because: "the proxy binding must not remain in the written body.js");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page leaves body.js unchanged when it already uses canonical $PDS_* bindings.")]
	public void PageGetTool_WhenBodyHasNoProxyBindings_WritesBodyUnchanged() {
		// Arrange
		string canonicalBody = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"PDS_UsrStatus","values":{"type":"crt.ComboBox","control":"$PDS_UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"PDS_UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");
		(PageGetTool tool, MockFileSystem mockFs) = CreatePageGetToolWithBody(canonicalBody);

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "get-page should succeed for a body with canonical bindings");
		string writtenBody = mockFs.File.ReadAllText(response.Files.BodyFile);
		writtenBody.Should().Contain("$PDS_UsrStatus",
			because: "canonical binding must be preserved unchanged in body.js");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page returns error response when directory creation fails")]
	public void PageGetTool_WhenDirectoryCreationFails_ReturnsError() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse("UsrMcp_FormPage", "uid-1", "pkg-1", "UsrMcp", "BasePage").ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("pkg-1");
		hierarchyClient.GetParentSchemas("uid-1", "pkg-1")
			.Returns([new PageDesignerHierarchySchema {
				UId = "uid-1", Name = "UsrMcp_FormPage",
				PackageUId = "pkg-1", PackageName = "UsrMcp",
				SchemaVersion = 1, Body = CreatePageBody()
			}]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(command);
		System.IO.Abstractions.IFileSystem failingFs = Substitute.For<System.IO.Abstractions.IFileSystem>();
		failingFs.Path.Combine(Arg.Any<string>(), Arg.Any<string>())
			.Returns(ci => System.IO.Path.Combine(ci.ArgAt<string>(0), ci.ArgAt<string>(1)));
		failingFs.Path.Combine(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(ci => System.IO.Path.Combine(ci.ArgAt<string>(0), ci.ArgAt<string>(1), ci.ArgAt<string>(2)));
		failingFs.Directory.GetCurrentDirectory().Returns("/workspace");
		failingFs.Directory.When(d => d.CreateDirectory(Arg.Any<string>()))
			.Do(_ => throw new System.UnauthorizedAccessException("Access denied"));
		PageGetTool tool = new(command, logger, commandResolver, failingFs);

		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		response.Success.Should().BeFalse(because: "directory creation failure should produce a failed response");
		response.Error.Should().Contain("Failed to create output directory",
			because: "the error must explain what failed");
		response.Files.Should().BeNull(because: "no files block should be returned on failure");
	}

	[Test]
	[Category("Unit")]
	[Description("PageGetResponse with Files set serializes without bundle and raw fields")]
	public void PageGetResponse_WithFiles_SerializesOmittingBundleAndRaw() {
		PageGetResponse response = new() {
			Success = true,
			Page = new PageMetadataInfo {
				SchemaName = "UsrFoo", SchemaUId = "uid-1",
				PackageName = "UsrFoo", PackageUId = "pkg-1", ParentSchemaName = "BasePage"
			},
			Files = new PageGetFilesInfo {
				BodyFile = "/out/UsrFoo/body.js",
				BundleFile = "/out/UsrFoo/bundle.json",
				MetaFile = "/out/UsrFoo/meta.json"
			}
		};

		string json = System.Text.Json.JsonSerializer.Serialize(response);

		json.Should().NotContain("\"bundle\"", because: "bundle must be absent when null with WhenWritingNull");
		json.Should().NotContain("\"raw\"", because: "raw must be absent when null with WhenWritingNull");
		json.Should().Contain("\"files\"", because: "files block must appear in the serialized response");
		json.Should().Contain("\"bodyFile\":\"/out/UsrFoo/body.js\"", because: "body file path must be serialized");
		json.Should().Contain("\"bundleFile\":\"/out/UsrFoo/bundle.json\"", because: "bundle file path must be serialized");
		json.Should().Contain("\"metaFile\":\"/out/UsrFoo/meta.json\"", because: "meta file path must be serialized");
	}

	[Test]
	[Description("TryUpdatePage uses hierarchy[0] as editable schema when a replacing schema already lives in the design package")]
	public void TryUpdatePage_WhenReplacingExistsInDesignPackage_UpdatesThatReplacing() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		const string originalUId = "f537fd79-9bdc-43ea-a9ce-b068c29d0b22";
		const string replacingUId = "088384f8-5379-4f4c-b71a-3e86d5117909";
		const string designPackageUId = "082ea278-3ea9-4cca-96da-d5bb999b141e";
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(ci => "http://test" + ci.ArgAt<string>(0));
		hierarchyClient.GetDesignPackageUId(originalUId).Returns(designPackageUId);
		hierarchyClient.GetParentSchemas(originalUId, designPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			new() { UId = replacingUId, Name = "Accounts_ListPage", PackageUId = designPackageUId, PackageName = "CrtCustomer360App_pcsejrm" },
			new() { UId = originalUId, Name = "Accounts_ListPage", PackageUId = "2ecba2bd-b810-47a5-a1b1-08c888529d6c", PackageName = "CrtCustomer360App" }
		});
		string validBody = "define(\"Accounts_ListPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["UId"] = originalUId } }
		};
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject {
				["uId"] = replacingUId,
				["name"] = "Accounts_ListPage",
				["body"] = "old diff",
				["package"] = new JObject { ["uId"] = designPackageUId },
				["parent"] = new JObject { ["uId"] = originalUId }
			}
		};
		var saveResponse = new JObject { ["success"] = true };
		string lastSavePayload = null;
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				if (callIndex == 1) return metadataResponse.ToString();
				if (callIndex == 2) return getSchemaResponse.ToString();
				lastSavePayload = ci.ArgAt<string>(1);
				return saveResponse.ToString();
			});

		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		bool ok = command.TryUpdatePage(new PageUpdateOptions { SchemaName = "Accounts_ListPage", Body = validBody, DryRun = false }, out PageUpdateResponse response);

		ok.Should().BeTrue(because: "expected success; error: " + response.Error);
		response.Success.Should().BeTrue();
		hierarchyClient.Received(1).GetDesignPackageUId(originalUId);
		hierarchyClient.Received(1).GetParentSchemas(originalUId, designPackageUId);
		var savedDto = JObject.Parse(lastSavePayload);
		savedDto["uId"].ToString().Should().Be(replacingUId,
			because: "update path must target the existing replacing schema in design package");
	}

	[Test]
	[Description("TryUpdatePage creates a new replacing schema in design package when hierarchy[0] is not in design package (virtual package materialization)")]
	public void TryUpdatePage_WhenNoReplacingInDesignPackage_BuildsNewReplacingDto() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		const string originalUId = "f537fd79-9bdc-43ea-a9ce-b068c29d0b22";
		const string originalPackageUId = "2ecba2bd-b810-47a5-a1b1-08c888529d6c";
		const string virtualDesignPackageUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(ci => "http://test" + ci.ArgAt<string>(0));
		hierarchyClient.GetDesignPackageUId(originalUId).Returns(virtualDesignPackageUId);
		hierarchyClient.GetParentSchemas(originalUId, virtualDesignPackageUId).Returns(new List<PageDesignerHierarchySchema> {
			new() { UId = originalUId, Name = "Accounts_ListPage", PackageUId = originalPackageUId, PackageName = "CrtCustomer360App" }
		});
		string validBody = "define(\"Accounts_ListPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["UId"] = originalUId } }
		};
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject {
				["uId"] = originalUId,
				["name"] = "Accounts_ListPage",
				["schemaType"] = 9,
				["schemaVersion"] = 1,
				["body"] = "original body",
				["package"] = new JObject { ["uId"] = originalPackageUId, ["name"] = "CrtCustomer360App" },
				["parent"] = new JObject { ["uId"] = "b7b898d0-8c77-4953-c097-23fa6800da02", ["name"] = "ListPageV3Template" },
				["isReadOnly"] = true,
				["optionalProperties"] = new JArray()
			}
		};
		var saveResponse = new JObject { ["success"] = true };
		string lastSavePayload = null;
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				if (callIndex == 1) return metadataResponse.ToString();
				if (callIndex == 2) return getSchemaResponse.ToString();
				lastSavePayload = ci.ArgAt<string>(1);
				return saveResponse.ToString();
			});

		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		bool ok = command.TryUpdatePage(new PageUpdateOptions { SchemaName = "Accounts_ListPage", Body = validBody, DryRun = false }, out PageUpdateResponse response);

		ok.Should().BeTrue();
		response.Success.Should().BeTrue();
		var savedDto = JObject.Parse(lastSavePayload);
		savedDto["uId"].ToString().Should().NotBe(originalUId,
			because: "create path must issue a fresh uId so backend creates a new replacing schema");
		System.Guid.TryParse(savedDto["uId"].ToString(), out _).Should().BeTrue(
			because: "new uId must be a valid GUID generated by the client");
		savedDto["name"].ToString().Should().Be("Accounts_ListPage",
			because: "replacing schema keeps the original name so the hierarchy link matches");
		savedDto["package"]["uId"].ToString().Should().Be(virtualDesignPackageUId,
			because: "package must be the design package — backend materializes it if virtual");
		savedDto["parent"]["uId"].ToString().Should().Be(originalUId,
			because: "parent must reference the original schema so replacing inherits from it");
		savedDto["extendParent"].Value<bool>().Should().BeTrue(
			because: "replacing schemas must extend their parent for diff-based body merge");
		savedDto["body"].ToString().Should().Be(validBody,
			because: "the body passed to update-page must be written into the new replacing schema DTO");
	}

	[Test]
	[Description("TryUpdatePage without hierarchy client falls back to legacy direct-update flow (backward compatibility)")]
	public void TryUpdatePage_WhenHierarchyClientOmitted_FallsBackToLegacyFlow() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(ci => "http://test" + ci.ArgAt<string>(0));
		const string uid = "aaaaaaaa-bbbb-cccc-dddd-111111111111";
		string validBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var metadataResponse = new JObject { ["success"] = true, ["rows"] = new JArray { new JObject { ["UId"] = uid } } };
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject { ["uId"] = uid, ["name"] = "Test_FormPage", ["body"] = "old" }
		};
		var saveResponse = new JObject { ["success"] = true };
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				return callIndex switch {
					1 => metadataResponse.ToString(),
					2 => getSchemaResponse.ToString(),
					_ => saveResponse.ToString()
				};
			});

		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger);
		bool ok = command.TryUpdatePage(new PageUpdateOptions { SchemaName = "Test_FormPage", Body = validBody, DryRun = false }, out PageUpdateResponse response);

		ok.Should().BeTrue();
		response.Success.Should().BeTrue();
	}

	[Test]
	[Description("PageBundleBuilder.ExtractContainers flattens viewConfig into a list that AI callers can use as parentName source")]
	public void PageBundleBuilder_Containers_Flattens_ViewConfig_Tree() {
		var parser = Substitute.For<IPageSchemaBodyParser>();
		parser.Parse(Arg.Any<string>()).Returns(new PageParsedSchemaBody {
			ViewConfigDiff = new Newtonsoft.Json.Linq.JArray {
				new Newtonsoft.Json.Linq.JObject {
					["operation"] = "insert",
					["name"] = "RootContainer",
					["values"] = new Newtonsoft.Json.Linq.JObject {
						["type"] = "crt.FlexContainer",
						["items"] = new Newtonsoft.Json.Linq.JArray {
							new Newtonsoft.Json.Linq.JObject {
								["name"] = "NestedContainer",
								["type"] = "crt.Grid",
								["items"] = new Newtonsoft.Json.Linq.JArray {
									new Newtonsoft.Json.Linq.JObject {
										["name"] = "LeafButton",
										["type"] = "crt.Button"
									}
								}
							}
						}
					}
				}
			}
		});
		var jsonDiff = Substitute.For<IPageJsonDiffApplier>();
		jsonDiff.ApplyDiff(Arg.Any<Newtonsoft.Json.Linq.JArray>(), Arg.Any<IReadOnlyList<Newtonsoft.Json.Linq.JArray>>(), Arg.Any<IReadOnlyList<PageJsonDiffApplyOptions>>())
			.Returns(ci => {
				var array = new Newtonsoft.Json.Linq.JArray {
					new Newtonsoft.Json.Linq.JObject {
						["name"] = "RootContainer",
						["type"] = "crt.FlexContainer",
						["items"] = new Newtonsoft.Json.Linq.JArray {
							new Newtonsoft.Json.Linq.JObject {
								["name"] = "NestedContainer",
								["type"] = "crt.Grid",
								["items"] = new Newtonsoft.Json.Linq.JArray {
									new Newtonsoft.Json.Linq.JObject {
										["name"] = "LeafButton",
										["type"] = "crt.Button"
									}
								}
							}
						}
					}
				};
				return array;
			});
		var pathDiff = Substitute.For<IPageJsonPathDiffApplier>();
		pathDiff.Apply(Arg.Any<Newtonsoft.Json.Linq.JObject>(), Arg.Any<Newtonsoft.Json.Linq.JArray>())
			.Returns(ci => new Newtonsoft.Json.Linq.JObject());
		var builder = new PageBundleBuilder(jsonDiff, pathDiff);
		var parts = new List<PageSchemaBundlePart> {
			new(
				new PageDesignerHierarchySchema { UId = "u", Name = "TestPage", PackageUId = "p", Body = "x" },
				parser.Parse("x"))
		};

		PageBundleInfo bundle = builder.Build(parts);

		bundle.Containers.Should().HaveCount(2,
			because: "extractor must collect both the root container and the nested container (leaf button has no items array so is skipped)");
		bundle.Containers[0].Name.Should().Be("RootContainer");
		bundle.Containers[0].Type.Should().Be("crt.FlexContainer");
		bundle.Containers[0].ChildCount.Should().Be(1,
			because: "RootContainer holds one nested container");
		bundle.Containers[0].Path.Should().Be("RootContainer");
		bundle.Containers[1].Name.Should().Be("NestedContainer");
		bundle.Containers[1].ChildCount.Should().Be(1,
			because: "NestedContainer holds one leaf button (buttons are counted as children even though they don't appear as containers themselves)");
		bundle.Containers[1].Path.Should().Be("RootContainer/NestedContainer",
			because: "path must expose the ancestry chain so AI can disambiguate when same name appears in multiple branches");
	}
}
