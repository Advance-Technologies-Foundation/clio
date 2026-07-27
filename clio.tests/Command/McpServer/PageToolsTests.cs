using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class PageToolsTests
{

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
			"sandbox", null, null, null,
			OptionalProperties: """[{"key":"DashboardsEntitySchemaName","value":"Contact"}]""");

		string json = System.Text.Json.JsonSerializer.Serialize(args);

		json.Should().Contain("\"schema-name\":\"UsrDemo_BlankPage\"");
		json.Should().Contain("\"template\":\"BlankPageTemplate\"");
		json.Should().Contain("\"package-name\":\"Custom\"");
		json.Should().Contain("\"entity-schema-name\":\"UsrDemoEntity\"");
		json.Should().Contain("\"environment-name\":\"sandbox\"");
		json.Should().Contain("\"optional-properties\"");
		json.Should().NotContain("\"schemaName\"");
		json.Should().NotContain("\"packageName\"");
		json.Should().NotContain("\"optionalProperties\":");
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
	[Description("list-page-templates rejects an invalid schema-type BEFORE resolving the environment (ENG-91825 validation-ordering invariant), so a bad schema-type is reported as a schema-type error instead of being masked by an environment-resolution failure")]
	public void ListPageTemplates_ShouldRejectInvalidSchemaTypeBeforeResolvingEnvironment_WhenSchemaTypeIsUnknown() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageTemplatesListCommand command = new(Substitute.For<ISchemaTemplateCatalog>(), ConsoleLogger.Instance);
		PageTemplatesListTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act — invalid schema-type paired with an environment that would also fail to resolve.
		PageTemplateListResponse response = tool.ListPageTemplates(
			new PageTemplatesListArgs("not-a-schema-type", "does-not-exist", null, null, null));

		// Assert
		response.Success.Should().BeFalse(because: "an unknown schema-type is a pure-input failure");
		response.Error.Should().Contain("Unknown schema-type",
			because: "the schema-type error must surface instead of an environment-resolution error");
		commandResolver.DidNotReceive().Resolve<PageTemplatesListCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("list-page-templates resolves the environment for a valid schema-type, proving the schema-type gate blocks only invalid input and does not short-circuit the normal resolution path")]
	public void ListPageTemplates_ShouldResolveEnvironment_WhenSchemaTypeIsValid() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageTemplatesListCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("environment 'does-not-exist' is not registered"));
		PageTemplatesListCommand command = new(Substitute.For<ISchemaTemplateCatalog>(), ConsoleLogger.Instance);
		PageTemplatesListTool tool = new(command, ConsoleLogger.Instance, commandResolver);

		// Act — valid schema-type, so the tool proceeds past the gate into environment resolution.
		PageTemplateListResponse response = tool.ListPageTemplates(
			new PageTemplatesListArgs("web", "does-not-exist", null, null, null));

		// Assert
		response.Success.Should().BeFalse(because: "the environment cannot be resolved");
		commandResolver.Received(1).Resolve<PageTemplatesListCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("TryParseSchemaType maps every recognized web/mobile alias to its schema type, case- and whitespace-insensitively, so the MCP tool and the command accept the same documented filter values.")]
	[TestCase("web", PageSchemaType.Web)]
	[TestCase("freedomuipage", PageSchemaType.Web)]
	[TestCase("page", PageSchemaType.Web)]
	[TestCase("9", PageSchemaType.Web)]
	[TestCase("  WEB  ", PageSchemaType.Web)]
	[TestCase("mobile", PageSchemaType.Mobile)]
	[TestCase("mobilepage", PageSchemaType.Mobile)]
	[TestCase("10", PageSchemaType.Mobile)]
	[TestCase("MoBiLe", PageSchemaType.Mobile)]
	public void TryParseSchemaType_ShouldParseRecognizedAlias_WhenValueIsKnown(string value, PageSchemaType expected) {
		// Act
		bool parsed = PageTemplatesListCommand.TryParseSchemaType(value, out PageSchemaType schemaType, out string error);

		// Assert
		parsed.Should().BeTrue(because: "every documented schema-type alias must resolve to a schema type");
		schemaType.Should().Be(expected, because: "the alias must map to its canonical schema type regardless of case or surrounding whitespace");
		error.Should().BeNull(because: "a recognized alias produces no parse error");
	}

	[Test]
	[Description("TryParseSchemaType is total over its input: null, blank, or unknown values return a clean false with an actionable message instead of throwing, because the method is public and may be called without the IsNullOrWhiteSpace pre-guard.")]
	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("desktop")]
	public void TryParseSchemaType_ShouldReturnFalseWithMessage_WhenValueIsNullBlankOrUnknown(string value) {
		// Act
		bool parsed = PageTemplatesListCommand.TryParseSchemaType(value, out PageSchemaType schemaType, out string error);

		// Assert
		parsed.Should().BeFalse(because: "null, blank, or unrecognized input is not a valid schema-type");
		schemaType.Should().Be(default(PageSchemaType), because: "an unparsed value must leave the out parameter at its default instead of a partial result");
		error.Should().Contain("Unknown schema-type", because: "the caller needs an actionable message naming the accepted values");
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
		response.Error.Should().Contain("'app-code' -> 'code'",
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
		prompt.Should().Contain("Before inserting ANY new component",
			because: "get-page prompt guidance must require verifying a component type exists before inserting it, so the agent does not author an invented crt.* type that saves successfully but renders broken");
		prompt.Should().Contain("ask the user whether to use one of the existing components or build a custom one",
			because: "get-page prompt guidance must instruct the agent to ask the user when no existing component matches, instead of fabricating a type (ENG-90939)");
		prompt.Should().Contain(GuidanceGetTool.ToolName,
			because: "get-page prompt guidance should route guide lookups through the dedicated guidance tool");
		prompt.Should().Contain("`existing-app-maintenance`",
			because: "get-page prompt guidance should point callers to the MCP-owned existing-app maintenance guide name");
		prompt.Should().Contain("`page-modification`",
			because: "get-page prompt guidance should route page-body edits through the general page modification guide first");
		prompt.Should().Contain("pre-edit checklist",
			because: "get-page prompt guidance should use page-modification as the router for specialized page-authoring guides");
		prompt.Should().Contain("`page-schema-handlers`",
			because: "get-page prompt guidance should point handler edits to the dedicated clio-owned handler guide name");
		prompt.Should().Contain($"you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-handlers` before proposing or applying changes",
			because: "handler guidance should be mandatory before authorship so callers use the canonical handler contract");
		prompt.Should().Contain("must not author handler changes until that guidance has been read",
			because: "handler guidance should be framed as a hard workflow prerequisite rather than an optional recommendation");
		prompt.Should().Contain("`page-schema-validators`",
			because: "get-page prompt guidance should point validator edits to the dedicated clio-owned validator guide name");
		prompt.Should().Contain($"you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-validators` before proposing or applying changes",
			because: "validator guidance should be mandatory before authorship so callers do not drift into handler syntax");
		prompt.Should().Contain("must not author validator changes until that guidance has been read",
			because: "validator guidance should be framed as a hard workflow prerequisite rather than an optional recommendation");
		prompt.Should().Contain("never use handler signatures like `handler(request, next)`",
			because: "validator guidance should explicitly block handler contract leakage into SCHEMA_VALIDATORS");
		prompt.Should().Contain("If the requirement is field-value validation such as max/min/length/range/regex, including when the threshold comes from a system setting or other async SDK read, treat it as `validators` work and read `page-schema-validators`, not `page-schema-handlers`.",
			because: "page guidance should stop callers from choosing handlers for syssetting-backed length validation rules");
		prompt.Should().Contain($"`{ToolContractGetTool.ToolName}`",
			because: "get-page prompt guidance should bootstrap page workflows from the authoritative MCP contract before the first page tool call");
		prompt.Should().Contain($"`{PageSyncTool.ToolName}`",
			because: "page guidance should advertise sync-pages as the canonical page write path");
		prompt.Should().Contain("`validate`",
			because: "page guidance should surface the canonical validation semantics for sync-pages");
		prompt.Should().Contain("`verify`",
			because: "page guidance should surface the optional read-back semantics for sync-pages");
		prompt.Should().Contain("`resources`",
			because: "page guidance should surface the resources parameter so callers know it exists before authoring localizable strings");
		prompt.Should().Contain($"you must call `{GuidanceGetTool.ToolName}` with `name` set to `page-schema-resources`",
			because: "resources guidance should be mandatory before authorship so callers cannot register DS-bound caption keys that the platform already auto-provides");
		prompt.Should().Contain("NOT sufficient justification",
			because: "page guidance should block the common shortcut of passing resources just because the body contains a localizable-string reference");
		prompt.Should().Contain("declared view-model attribute from `viewModelConfig` / `viewModelConfigDiff`",
			because: "page guidance should steer standard fields toward declared view-model attributes instead of naming conventions");
		prompt.Should().Contain("If validator or handler logic moves to a different declared attribute for the same field, rebind the control to that same attribute.",
			because: "page guidance should call out the rebind rule that keeps control, validators, and handlers on the same attribute");
		prompt.Should().Contain("If the control is inherited from a parent schema and there is no local",
			because: "page guidance should explain that inherited controls need a local merge when rebinding is required");
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
		prompt.Should().NotContain("Use `sync-pages` only when you need to save multiple pages in one workflow.",
			because: "sync-pages should no longer be presented as a multi-page-only path");
		prompt.Should().NotContain("`schemaName`",
			because: "get-page prompt guidance should no longer advertise removed camelCase request fields");
		prompt.Should().NotContain("`environmentName`",
			because: "get-page prompt guidance should no longer advertise removed camelCase request fields");
	}

	[Test]
	[Description("get-page tool description routes callers to the page-modification guide (whose pre-edit checklist owns the specialized handler/validator/lookup-routing rules) before they edit the body.")]
	public void PageGetTool_Description_Should_Contain_Validator_Binding_Location_Guidance() {
		// Arrange
		var method = typeof(PageGetTool).GetMethod(nameof(PageGetTool.GetPage))!;
		var descAttr = method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Act
		string description = descAttr.Description;

		// Assert
		description.Should().Contain("get-guidance `page-modification`",
			because: "get-page description should route callers to the general page modification guide before body edits (the detailed handler/validator/lookup routing now lives in that guide's pre-edit checklist, not inline)");
		description.Should().Contain("pre-edit checklist",
			because: "get-page description should defer specialized guide selection to the page-modification pre-edit checklist instead of duplicating it inline");
		description.Should().Contain("mobile-page-modification",
			because: "get-page description must still route mobile pages to the mobile-specific guide");
		description.Should().NotContain("page-schema-resources",
			because: "get-page should point at the general page-modification router instead of a localizable-string leaf guide");
		description.Should().NotContain("crt.InitRequest",
			because: "the verbose lookup-vs-handler routing prose is moved into the page-modification guide GATE table — it must not be re-inlined on get-page");
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
		description.Should().Contain("SCHEMA_HANDLERS",
			because: "sync-pages description should surface the handler section name as part of body authoring rules");
		description.Should().Contain("call get-guidance with name `page-schema-handlers`",
			because: "sync-pages description should route callers to the dedicated handler guide through get-guidance");
		description.Should().Contain("SCHEMA_VALIDATORS",
			because: "sync-pages description should surface the validator section name as part of body authoring rules");
		description.Should().Contain("call get-guidance with name `page-schema-validators`",
			because: "sync-pages description should route callers to the dedicated validator guide through get-guidance");
		description.Should().Contain("call get-guidance with name `page-modification`",
			because: "sync-pages description should route broad page edits through the general page modification guide");
		description.Should().NotContain("page-schema-resources",
			because: "sync-pages should avoid surfacing localizable-string leaf guidance directly in the broad tool description");
	}

	[Test]
	[Description("update-page tool description routes page-body authoring to the page-modification guide instead of duplicating section rules inline, while keeping its load-bearing conflict-detection and Designer Presence behaviour.")]
	public void PageUpdateTool_Description_Should_Contain_Validator_Section_Authoring_Rules() {
		// Arrange
		var method = typeof(PageUpdateTool).GetMethod(nameof(PageUpdateTool.UpdatePage))!;
		var descAttr = method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Act
		string description = descAttr.Description;

		// Assert
		description.Should().Contain("get-guidance `page-modification`",
			because: "update-page description should route page-body edits through the general page modification guide (the per-section handler/validator/converter routing now lives in that guide's pre-edit checklist, not inline)");
		description.Should().Contain("pre-edit checklist",
			because: "update-page description should defer specialized guide selection to the page-modification pre-edit checklist instead of duplicating it inline");
		description.Should().Contain("Designer Presence",
			because: "update-page should disclose the best-effort live designer notification behaviour in its MCP description");
		description.Should().Contain("CONFLICT DETECTION",
			because: "update-page must keep the checksum-baseline conflict-detection contract in its description \u2014 it is behaviour, not duplicated guidance");
		description.Should().Contain("force=true",
			because: "update-page must keep the explicit conflict-override instruction so a model does not blindly retry the same body");
		description.Should().Contain("INSERTED-FIELD CONTRACT",
			because: "update-page must keep the inserted-field contract summary, which is the authoritative write-time contract reused across tools");
		description.Should().Contain("get-process-signature",
			because: "update-page should route run-process button parameter-code resolution through the get-process-signature probe");
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
			because: "get-page description must route callers to the handler and validator guides through get-guidance");
		syncDesc.Should().Contain("get-guidance",
			because: "sync-pages description must route callers to the handler and validator guides through get-guidance");
		updateDesc.Should().Contain("get-guidance",
			because: "update-page description must route callers to the handler and validator guides instead of duplicating page-body rules inline");
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
		PageGetTool tool = new(command, logger, commandResolver, new PageFileWriter(mockFs));

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
		// The data query returns zero rows (count 0 < limit 50), so the page is provably complete and
		// the supplementary count round-trip is skipped — only the single data query carries the filter.
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
		// app lookup + GetApplicationPackages + the single page data query = 3 calls. The page returns
		// one row (1 < limit 50), so it is provably complete and the supplementary count query is skipped.
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
	[Description("TryListPages reports total and truncated=true when the full match count exceeds the returned page")]
	public void TryListPages_WhenMoreMatchesThanLimit_ReportsTotalAndTruncated() {
		// Arrange — the data query returns a capped page; the follow-up count query reports the full total
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" },
				new JObject { ["Name"] = "Page2", ["UId"] = "uid-2", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		var countResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["RecordCount"] = 3232 } }
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString(), countResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 2 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because the listing query itself succeeded");
		response.Count.Should().Be(2, "because the capped page returned two rows");
		response.Total.Should().Be(3232, "because the count query reports the full number of matching pages");
		response.Truncated.Should().BeTrue(
			"because total exceeds count, so a caller must be able to detect the result is incomplete");
	}

	[Test]
	[Description("TryListPages reports truncated=false when the full match count equals the returned count")]
	public void TryListPages_WhenAllMatchesReturned_ReportsNotTruncated() {
		// Arrange
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		var countResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["RecordCount"] = 1 } }
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString(), countResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because the listing query succeeded");
		response.Count.Should().Be(1, "because a single page matched");
		response.Total.Should().Be(1, "because the count query confirms only one page matches");
		response.Truncated.Should().BeFalse(
			"because total equals count, so the result is complete");
	}

	[Test]
	[Description("TryListPages issues the supplementary count query and reports truncated=true only when the page is capped (count == effectiveLimit) and the count succeeds")]
	public void TryListPages_WhenPageIsCappedAndCountSucceeds_ReportsTotalAndTruncated() {
		// Arrange — Limit equals the row count, so the page is provably capped and the count query runs
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" },
				new JObject { ["Name"] = "Page2", ["UId"] = "uid-2", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		var countResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["RecordCount"] = 42 } }
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString(), countResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 2 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because the capped listing query itself succeeded");
		response.Total.Should().Be(42, "because the count query reports the full number of matching pages");
		response.Truncated.Should().BeTrue(
			"because the page was capped and the count proves more pages match than were returned");
		applicationClient.Received(2).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryListPages skips the supplementary count round-trip when the page is provably complete (count < effectiveLimit) and reports truncated=false")]
	public void TryListPages_WhenPageIsNotCapped_SkipsCountQueryAndReportsNotTruncated() {
		// Arrange — only one row returned for a limit of 50, so the result is provably complete
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because the listing query succeeded");
		response.Total.Should().Be(1, "because a short page is complete, so total equals the returned count");
		response.Truncated.Should().BeFalse("because a page shorter than the limit cannot be truncated");
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryListPages reports truncated=true when the page is capped but the supplementary count query returns success:false, because completeness is unprovable on a capped page")]
	public void TryListPages_WhenPageIsCappedAndCountReturnsUnsuccessful_ReportsTruncated() {
		// Arrange — capped page, count query responds success:false
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		var countResponse = new JObject { ["success"] = false };
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString(), countResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 1 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because the data query succeeded even though the count query failed");
		response.Total.Should().Be(1, "because a failed count falls back to the returned count");
		response.Truncated.Should().BeTrue(
			"because a capped page whose count cannot be confirmed must be reported as possibly incomplete");
	}

	[Test]
	[Description("TryListPages reports truncated=true when the page is capped but the supplementary count query returns malformed JSON, because completeness is unprovable on a capped page")]
	public void TryListPages_WhenPageIsCappedAndCountReturnsMalformedJson_ReportsTruncated() {
		// Arrange — capped page, count query responds with unparseable JSON (JsonException branch)
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString(), "{ this is not valid json ");
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 1 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because the malformed count body must never fail the whole listing");
		response.Total.Should().Be(1, "because an unparseable count falls back to the returned count");
		response.Truncated.Should().BeTrue(
			"because a capped page whose count could not be parsed must be reported as possibly incomplete");
	}

	[Test]
	[Description("TryListPages rejects a negative limit instead of disabling the cap and returning every page")]
	public void TryListPages_WhenLimitNegative_ReturnsFailureWithoutQuerying() {
		// Arrange
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = -5 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeFalse("because a negative limit is invalid and must not run an unbounded query");
		response.Success.Should().BeFalse("because the negative limit is rejected before any request");
		response.Error.Should().Contain("limit must be zero or greater",
			because: "the failure should explain why the negative limit was rejected");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("TryListPages treats limit=0 as 'use the default' and sends the default rowCount to the server")]
	public void TryListPages_WhenLimitZero_UsesDefaultRowCount() {
		// Arrange
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject { ["success"] = true, ["rows"] = new JArray() };
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 0 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because limit=0 is a valid request that falls back to the default cap");
		response.Success.Should().BeTrue("because the default-capped query succeeded");
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rowCount\":50")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryListPages honors a small valid limit and sends it as the server rowCount")]
	public void TryListPages_WhenSmallValidLimit_SendsThatRowCount() {
		// Arrange
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg", ["ParentSchemaName"] = "BasePage" }
			}
		};
		var countResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["RecordCount"] = 1 } }
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataResponse.ToString(), countResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 5 };

		// Act
		bool result = command.TryListPages(options, out PageListResponse response);

		// Assert
		result.Should().BeTrue("because a small valid limit is honored");
		response.Count.Should().Be(1, "because one row was returned");
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rowCount\":5")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
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
		PageUpdateCommand updateCommand = new(updateApplicationClient, updateServiceUrlBuilder, updateLogger, Substitute.For<IPageBaselineGuard>(), CreateHierarchyClientFor("roundtrip-page-uid", "roundtrip-pkg-uid"));

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
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), CreateHierarchyClientFor("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
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
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), CreateHierarchyClientFor("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
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
	[Description("TryUpdatePage rejects a plain JSON body for a web schema even though the body shape looks like a mobile body.")]
	public void TryUpdatePage_WhenWebSchemaReceivesPlainJsonBody_ReturnsMarkerValidationError() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray {
					new JObject { ["UId"] = "web-schema-uid" }
				}
			}.ToString());
		hierarchyClient.GetDesignPackageUId("web-schema-uid").Returns("web-pkg-uid");
		hierarchyClient.GetParentSchemas("web-schema-uid", "web-pkg-uid").Returns([
			new PageDesignerHierarchySchema {
				UId = "web-schema-uid",
				Name = "UsrWeb_FormPage",
				PackageUId = "web-pkg-uid",
				PackageName = "UsrWebPkg",
				SchemaVersion = 1,
				SchemaType = 9
			}
		]);
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);
		PageUpdateOptions options = new() {
			SchemaName = "UsrWeb_FormPage",
			Body = """
				{
				  "viewConfigDiff": [],
				  "viewModelConfigDiff": [],
				  "modelConfigDiff": []
				}
				""",
			DryRun = true
		};

		// Act
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "hierarchy schema type, not body shape, must select the web validation path");
		response.Error.Should().Contain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "a web schema still requires AMD marker pairs even if the body starts with a JSON object");
	}

	[Test]
	[Description("TryUpdatePage registers explicit resources for a mobile JSON body before saving the schema.")]
	public void TryUpdatePage_WhenMobileBodyHasExplicitResources_RegistersResourcesOnSave() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		string savedPayload = null;
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse(
				"UsrMobile_FormPage",
				"mobile-schema-uid",
				"mobile-package-uid",
				"UsrMobilePackage",
				"BaseMobilePageTemplate").ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject {
					["body"] = "{}",
					["localizableStrings"] = new JArray()
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Do<string>(body => savedPayload = body),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("ResetScriptCache")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		string mobileBody = """
			{
			  "viewConfigDiff": [
			    {
			      "operation": "insert",
			      "name": "UsrMobileTitle",
			      "values": {
			        "type": "crt.Label",
			        "caption": "$Resources.Strings.UsrMobileTitle"
			      }
			    }
			  ],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";
		PageUpdateOptions options = new() {
			SchemaName = "UsrMobile_FormPage",
			Body = mobileBody,
			Resources = "{\"UsrMobileTitle\":\"Mobile title\"}",
			DryRun = false
		};

		// Act
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(
			because: "mobile JSON bodies with explicit resources should save successfully");
		response.ResourcesRegistered.Should().Be(1,
			because: "the explicit resource referenced by the mobile body should be registered");
		response.RegisteredResourceKeys.Should().Equal(["UsrMobileTitle"],
			because: "the response should report the resource key registered during save");
		savedPayload.Should().Contain("\"name\":\"UsrMobileTitle\"",
			because: "the saved schema payload should include the new localizable string");
		savedPayload.Should().Contain("\"value\":\"Mobile title\"",
			because: "the explicit resource value should be preserved in localizableStrings");
	}

	[Test]
	[Description("TryUpdatePage surfaces an advisory warning (without blocking) when a replace body downgrades an own-body insert to a merge")]
	public void TryUpdatePage_WhenInsertDowngradedToMerge_ReturnsWarningAndSaves() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse(
				"UsrDowngrade_FormPage",
				"downgrade-schema-uid",
				"downgrade-package-uid",
				"UsrDowngradePackage",
				"BasePage").ToString());
		// The schema currently stored on the server inserts UsrName in its own body.
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject {
					["body"] = CreatePageBody("""[{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } }]"""),
					["localizableStrings"] = new JArray()
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("ResetScriptCache")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		// The incoming replace body changes that same UsrName from insert to merge.
		PageUpdateOptions options = new() {
			SchemaName = "UsrDowngrade_FormPage",
			Body = CreatePageBody("""[{ "operation": "merge", "name": "UsrName", "values": { "label": "X" } }]"""),
			DryRun = false
		};

		// Act
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(
			because: "the downgrade is advisory only and must not block the save");
		response.Warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("merge"),
			because: "demoting an own-body insert to a merge orphans the component and must surface as a warning");
	}

	[Test]
	[Description("PageUpdateTool merges the command's downgrade warning with body-only validation warnings instead of overwriting either (locks MergeWarnings).")]
	public async System.Threading.Tasks.Task UpdatePage_WhenDowngradeAndAwaitWarningsBothApply_MergesBothIntoResponse() {
		// Arrange — the stored schema inserts UsrName (so the incoming merge is a downgrade), and the
		// incoming body also reads $context without await (a body-only validation warning).
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse(
				"UsrMerge_FormPage", "merge-schema-uid", "merge-package-uid", "UsrMergePackage", "BasePage").ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject {
					["body"] = CreatePageBody("""[{ "operation": "insert", "name": "UsrName", "values": { "type": "crt.Input" } }]"""),
					["localizableStrings"] = new JArray()
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("ResetScriptCache")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(string.Empty);
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody(
			viewConfigDiff: """[{ "operation": "merge", "name": "UsrName", "values": { "label": "$Resources.Strings.UsrName" } }]""",
			handlers: """[{ request: "crt.HandleViewModelInitRequest", handler: async (request, next) => { const x = $context["UsrMode"]; return next?.handle(request); } }]""");
		PageUpdateArgs args = new("UsrMerge_FormPage", body, null, false, "dev", null, null, null, SkipSampling: true);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "both findings are advisory, so the save must still succeed");
		response.Warnings.Should().Contain(w => w.Contains("UsrName") && w.Contains("merge"),
			because: "the command-side insert->merge downgrade warning must be preserved");
		response.Warnings.Should().Contain(w => w.Contains("UsrMode") && w.Contains("await"),
			because: "the body-only un-awaited $context warning must coexist rather than being overwritten");
	}

	[Test]
	[Description("validate-page surfaces the un-awaited $context warning at the tool level without marking the body invalid (locks PageValidateTool ContextAwait wiring).")]
	public async System.Threading.Tasks.Task ValidatePage_WhenUnAwaitedContextRead_ReturnsWarningAndStaysValid() {
		// Arrange
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageValidateTool tool = new(mobileCatalog, webCatalog);
		string body = CreatePageBody(
			handlers: """[{ request: "crt.HandleViewModelInitRequest", handler: async (request, next) => { const x = $context["UsrMode"]; return next?.handle(request); } }]""");
		PageValidateArgs args = new(body);

		// Act
		PageValidateResponse response = await tool.ValidatePage(args);

		// Assert
		response.Valid.Should().BeTrue(
			because: "an un-awaited $context read is advisory and must not invalidate the body");
		response.Validation.Warnings.Should().Contain(w => w.Contains("UsrMode") && w.Contains("await"),
			because: "validate-page must surface the ValidateContextAccessAwait warning through its tool-level wiring");
	}

	[Test]
	[Description("TryUpdatePage rejects a mobile JSON body that contains a 'validators' section.")]
	[Category("Unit")]
	public void TryUpdatePage_WhenMobileBodyHasValidators_ReturnsValidationError() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		SetupSchemaMetadata(applicationClient, serviceUrlBuilder, "UsrMobile_FormPage");
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrMobile_FormPage",
			Body = """
				{
				  "viewConfigDiff": [],
				  "validators": {}
				}
				""",
			DryRun = true
		};

		// Act
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "mobile pages do not support validators \u2014 the command must reject the body");
		response.Error.Should().Contain("validators",
			because: "the error should identify the disallowed 'validators' key");
	}

	[Test]
	[Description("TryUpdatePage rejects a mobile JSON body that contains a 'handlers' section.")]
	[Category("Unit")]
	public void TryUpdatePage_WhenMobileBodyHasHandlers_ReturnsValidationError() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		SetupSchemaMetadata(applicationClient, serviceUrlBuilder, "UsrMobile_FormPage");
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrMobile_FormPage",
			Body = """
				{
				  "viewConfigDiff": [],
				  "handlers": []
				}
				""",
			DryRun = true
		};

		// Act
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "mobile pages do not support handlers \u2014 the command must reject the body");
		response.Error.Should().Contain("handlers",
			because: "the error should identify the disallowed 'handlers' key");
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
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), Substitute.For<IPageDesignerHierarchyClient>());
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
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), Substitute.For<IPageDesignerHierarchyClient>());
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
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), Substitute.For<IPageDesignerHierarchyClient>());
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
	[Description("TryUpdatePage dry-run rejects field inserts whose binding attribute is not declared in the body — even when other unrelated attributes are declared. An inserted control with an undeclared binding attribute has no data source after save, so update-page must reject the payload at validation time.")]
	public void TryUpdatePage_WhenInsertedFieldBindingHasNoMatchingViewModelDeclaration_ReturnsValidationError() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		SetupSchemaMetadata(applicationClient, serviceUrlBuilder, "UsrProxyBinding_FormPage");
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrProxyBinding_FormPage",
			Body = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatusField\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"values\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });",
			DryRun = true
		};

		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeFalse(
			because: "update-page must require the body to declare any attribute referenced by an inserted control");
		response.Success.Should().BeFalse(
			because: "the validation failure must be surfaced in the response");
		response.Error.Should().Contain("UsrStatusField",
			because: "the diagnostic should name the undeclared binding attribute the agent must add");
		response.Error.Should().Contain("viewModelConfigDiff",
			because: "the diagnostic should point at the section that needs the missing declaration");
	}

	[Test]
	[Description("TryUpdatePage dry-run accepts merge operations against parent-provided controls — only insert operations are required to declare their binding attributes locally.")]
	public void TryUpdatePage_WhenMergeOperationTargetsParentProvidedAttribute_Succeeds() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		SetupSchemaMetadata(applicationClient, serviceUrlBuilder, "UsrMergeBinding_FormPage");
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrMergeBinding_FormPage",
			Body = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatusField\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });",
			DryRun = true
		};

		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeTrue(
			because: "merge operations target existing controls whose attribute and resource may legitimately come from a parent schema");
		response.Success.Should().BeTrue(
			because: "the new strict check applies only to insert operations");
	}

	[Test]
	[Description("TryUpdatePage dry-run rejects controls bound to a handler-updated attribute at a different name than the declared binding.")]
	public void TryUpdatePage_WhenHandlerDrivenFieldStaysOnDifferentDeclaredAttribute_ReturnsValidationError() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		SetupSchemaMetadata(applicationClient, serviceUrlBuilder, "UsrHandlerDrivenBinding_FormPage");
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrHandlerDrivenBinding_FormPage",
			Body = CreatePageBody(
				viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","label":"$Resources.Strings.UsrName","control":"$UsrNameField"}}]""",
				viewModelConfig: """{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"}},"UsrNameField":{"modelConfig":{"path":"PDS.UsrName"}}}}""",
				handlers: """[{ request: "crt.HandleViewModelInitRequest", handler: async (request, next) => { const result = await next?.handle(request); await request.$context.set("UsrName", "Primary currency"); return result; } }]"""),
			DryRun = true
		};

		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeFalse(
			because: "update-page should fail fast when the control binds to a different declared attribute than the handler updates");
		response.Success.Should().BeFalse(
			because: "the validation failure should be surfaced in the response envelope");
		response.Error.Should().Contain("invalid form field bindings")
			.And.Contain("$UsrNameField")
			.And.Contain("$UsrName")
			.And.Contain("$context.set",
				because: "the response should guide toward the correct declared attribute written by the handler");
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
	}

	[Test]
	[Description("TryUpdatePage dry-run rejects validators declared directly on a viewConfigDiff control.")]
	public void TryUpdatePage_WhenValidatorsAreDeclaredOnViewConfigDiffControl_ReturnsValidationError() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		SetupSchemaMetadata(applicationClient, serviceUrlBuilder, "UsrValidatorPlacement_FormPage");
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		PageUpdateOptions options = new() {
			SchemaName = "UsrValidatorPlacement_FormPage",
			Body = CreatePageBody(
				viewConfigDiff: """[{"operation":"insert","name":"UsrCode","values":{"type":"crt.Input","control":"$UsrCode","validators":[{"id":"usr.MaxLengthFromSysSettingValidator","params":{"settingCode":"MaxProcessLoopCount","message":"Too long"}}]}}]""",
				viewModelConfig: """{"attributes":{"UsrCode":{"modelConfig":{"path":"PDS.UsrCode"}}}}""",
				validators: """{"usr.MaxLengthFromSysSettingValidator":{"validator":function(config){return async function(control){return null;};},"params":[{"name":"settingCode"},{"name":"message"}],"async":true}}"""),
			DryRun = true
		};

		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeFalse(
			because: "update-page should fail fast when validators are declared on the UI element instead of the bound attribute");
		response.Success.Should().BeFalse(
			because: "the validation failure should be surfaced in the response envelope");
		response.Error.Should().Contain("invalid validator bindings")
			.And.Contain("viewConfigDiff")
			.And.Contain("viewModelConfig/viewModelConfigDiff",
				because: "the response should explain the correct validator binding location");
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects a page body where a JSON marker section contains malformed JSON — when the markers themselves are intact the marker/content validator is preferred over the generic ENG-89796 syntax message, so the error names the specific SCHEMA_* section the caller must fix")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_Rejects_Body_With_Malformed_Json_Marker() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithBadJson = CreatePageBody(viewConfigDiff: "[{ bad json }]");
		PageUpdateArgs args = new("UsrTest_FormPage", bodyWithBadJson, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "update-page must reject page bodies where JSON marker sections contain malformed JSON");
		response.Error.Should().Contain("Invalid JSON in SCHEMA_VIEW_CONFIG_DIFF",
			because: "when the SCHEMA_* markers are intact the marker/content validator is preferred over the generic syntax message, so the error names the exact section with the malformed JSON");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "validation must fail before any remote call is made to Creatio");
	}

	[Test]
	[Description("ENG-89796: update-page fails fast on the canonical incident body (`await request.$context.X = Y`) — pins the dedicated syntax gate symmetrically with the equivalent test on sync-pages")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_ShouldFailFast_WhenBodyHasJavaScriptSyntaxError() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		// The actual broken body from the ENG-89796 production incident: `await X = Y`
		// — `await` is an expression and cannot be an assignment target.
		string incidentBody = "define(\"Bad_FormPage\", [], function() {\n" +
			"    return {\n" +
			"        handlers: [{\n" +
			"            request: 'crt.HandleViewModelInitRequest',\n" +
			"            handler: async function(request, next) {\n" +
			"                await request.$context.FieldX = \"value\";\n" +
			"                return next?.handle(request);\n" +
			"            }\n" +
			"        }]\n" +
			"    };\n" +
			"});";
		PageUpdateArgs args = new("UsrTest_FormPage", incidentBody, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "the exact incident body that triggered ENG-89796 must NEVER pass — letting it through is a regression to the pre-fix behaviour update-page silently writing a broken page");
		response.Error.Should().Contain("JavaScript syntax error",
			because: "the failure message must name the actual class of problem so the operator does not chase a phantom marker / sampling issue when the parser rejected the body");
		response.Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the broken body did not reach the server (and therefore did not corrupt a saved page) without having to inspect logs");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the syntax gate must short-circuit BEFORE any remote call — the entire point of the validator is to keep broken bodies off the wire");
	}

	// A body in the legacy marker-without-key shape (`{ /**MARKER*/[]/**MARKER*/, ... }`) — an object
	// literal whose entries have no keys. Acornima rejects it with "Unexpected token ']'" at column 139,
	// the exact JS-syntax failure the ENG-90640 e2e contracts feed update-page. Marker INTEGRITY still
	// passes (all required marker pairs present), so the offline content/argument validators can run on
	// the syntax-failure path.
	private const string SyntaxBrokenMarkerBody =
		"define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static PageUpdateTool BuildSyntaxFailureTool(out IApplicationClient applicationClient) {
		applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		return new PageUpdateTool(
			command, logger, commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(new MockFileSystem()));
	}

	[Test]
	[Description("ENG-90640: a body that fails the JS syntax gate but carries malformed optional-properties surfaces the specific optional-properties argument error over the generic JavaScript-syntax message.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_PrefersOptionalPropertiesError_WhenBodyAlsoHasSyntaxError() {
		// Arrange
		PageUpdateTool tool = BuildSyntaxFailureTool(out IApplicationClient applicationClient);
		PageUpdateArgs args = new(
			"UsrBadOptionalProps_FormPage", SyntaxBrokenMarkerBody, null, DryRun: true,
			"local", null, null, null, OptionalProperties: "{not-an-array}");

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "a malformed optional-properties payload must reject the call");
		response.Error.Should().MatchRegex("(?i)optional-properties",
			because: "the specific optional-properties argument error must win over the generic JavaScript syntax error so the operator fixes the right thing");
		response.Error.Should().NotContain("JavaScript syntax error",
			because: "the actionable argument error must shadow the generic parser message on the syntax-failure path");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "argument validation is offline and must precede any remote call");
	}

	[Test]
	[Description("ENG-90640: a body that fails the JS syntax gate but carries malformed resources JSON surfaces the canonical resources argument error over the generic JavaScript-syntax message.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_PrefersResourcesError_WhenBodyAlsoHasSyntaxError() {
		// Arrange
		PageUpdateTool tool = BuildSyntaxFailureTool(out IApplicationClient applicationClient);
		PageUpdateArgs args = new(
			"UsrValidationOnly_FormPage", SyntaxBrokenMarkerBody, Resources: "{\"UsrTitle\":", DryRun: true,
			"local", null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "a malformed resources payload must reject the call");
		response.Error.Should().Be("resources must be a valid JSON object string",
			because: "the canonical resources argument error must win over the generic JavaScript syntax error");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "argument validation is offline and must precede any remote call");
	}

	[Test]
	[Description("ENG-90640: a run-process button missing processName inside a body that also fails the JS syntax gate surfaces the structural processName error (offline) over the generic JavaScript-syntax message, even though marker integrity fails.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_PrefersRunProcessStructureError_WhenBodyAlsoHasSyntaxError() {
		// Arrange
		PageUpdateTool tool = BuildSyntaxFailureTool(out IApplicationClient applicationClient);
		// Same shape as the e2e contract body: a run-process button with processRunType but no
		// processName, wrapped in the legacy marker-without-key object literal (fails Acornima).
		string runProcessBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function() { return { "
			+ "/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"RunBpButton\",\"values\":{"
			+ "\"type\":\"crt.Button\",\"clicked\":{\"request\":\"crt.RunBusinessProcessRequest\","
			+ "\"params\":{\"processRunType\":\"RegardlessOfThePage\"}}},\"parentName\":\"MainHeaderTop\","
			+ "\"propertyName\":\"items\",\"index\":0}]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
			+ "/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
			+ "/**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, "
			+ "/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
			+ "/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
			+ "/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageUpdateArgs args = new(
			"UsrRunProcessValidation_FormPage", runProcessBody, null, DryRun: true,
			"local", null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "a run-process button without processName must reject the call before any remote call");
		response.Error.Should().Contain("processName",
			because: "the structural run-process error names the missing processName and must win over the generic syntax error");
		response.Error.Should().Contain("RunBpButton",
			because: "the structural run-process error names the offending button");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the structural run-process check is offline regex and must precede any remote signature call");
	}

	[Test]
	[Description("ENG-89796/ENG-90640: a genuine JS-only syntax error with clean argument payloads and no run-process structural problem still surfaces the generic JavaScript-syntax message — the actionable-error preference does not eat the fail-fast wording.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_PreservesSyntaxError_WhenNoMoreSpecificOfflineErrorExists() {
		// Arrange
		PageUpdateTool tool = BuildSyntaxFailureTool(out IApplicationClient applicationClient);
		// Clean payloads, no run-process button, no environment supplied — nothing more specific than
		// the JS syntax error can be detected offline.
		PageUpdateArgs args = new(
			"UsrPlain_FormPage", SyntaxBrokenMarkerBody, null, DryRun: true,
			null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "a body that cannot parse as JavaScript must never be persisted");
		response.Error.Should().Contain("JavaScript syntax error",
			because: "with clean payloads and no offline-detectable problem the generic ENG-89796 syntax wording must be preserved");
		response.Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the broken body did not reach the server");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the syntax gate must short-circuit before any remote call");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects schemas where validator params use $Resources.Strings.X binding syntax before saving to Creatio.")]
	public void PageUpdateTool_UpdatePage_Rejects_Schema_With_Resources_Strings_In_Validator_Params() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody(
			viewModelConfig: """{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"},"validators":{"UpperCase":{"type":"usr.UpperCase","params":{"message":"$Resources.Strings.UsrUpperCaseValidator_Message"}}}}}}""",
			validators: """{"usr.UpperCase":{"validator":function(config){return function(control){return null;}},"params":[{"name":"message"}],"async":false}}""");
		PageUpdateArgs args = new("UsrTest_FormPage", body, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

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
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(System.Text.Json.JsonSerializer.Serialize(new { success = true }));
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody(
			viewModelConfig: """{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"},"validators":{"UpperCase":{"type":"usr.UpperCase","params":{"message":"#ResourceString(UsrUpperCaseValidator_Message)#"}}}}}}""",
			validators: """{"usr.UpperCase":{"validator":function(config){return function(control){return null;}},"params":[{"name":"message"}],"async":false}}""");
		PageUpdateArgs args = new("UsrTest_FormPage", body, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Error.Should().NotContain("Validation failed",
			because: "#ResourceString(KeyName)# is the correct format and must not be rejected by the validator param check");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects handlers when SCHEMA_HANDLERS stops being an array literal.")]
	public void PageUpdateTool_UpdatePage_Rejects_NonArray_Handlers_Section() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody(
			handlers: """{ request: "crt.HandleViewModelInitRequest", handler: async (request, next) => { await next?.handle(request); } }""");
		PageUpdateArgs args = new("UsrHandlerShape_FormPage", body, null, true, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "update-page must reject schemas where SCHEMA_HANDLERS is no longer an array literal");
		response.Error.Should().Contain("Validation failed")
			.And.Contain("SCHEMA_HANDLERS")
			.And.Contain("array literal",
				because: "the error should explain that the handlers section must stay an array");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "handler-shape validation must fail before any remote call is made");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects handler entries that omit the request property.")]
	public void PageUpdateTool_UpdatePage_Rejects_Handler_Entry_Without_Request() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody(
			handlers: """[{ handler: async (request, next) => { await next?.handle(request); } }]""");
		PageUpdateArgs args = new("UsrHandlerShape_FormPage", body, null, true, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "update-page must reject handler entries that do not declare the handled request type");
		response.Error.Should().Contain("Validation failed")
			.And.Contain("'request'",
				because: "the error should identify the missing request property");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "handler-shape validation must fail before any remote call is made");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects crt.MaxLength bindings that use max instead of maxLength in params.")]
	public void PageUpdateTool_UpdatePage_Rejects_BuiltIn_MaxLength_With_Wrong_Param_Name() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrName","values":{"type":"crt.Input","control":"$UsrName"}}]""",
			viewModelConfig: """{"attributes":{"UsrName":{"modelConfig":{"path":"PDS.UsrName"},"validators":{"NameMaxLength":{"type":"crt.MaxLength","params":{"max":4}}}}}}""");
		PageUpdateArgs args = new("UsrTest_FormPage", body, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

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
	[Description("PageUpdateTool.UpdatePage rejects validators declared directly on a viewConfigDiff control before saving.")]
	public void PageUpdateTool_UpdatePage_Rejects_Validators_Declared_On_ViewConfigDiff_Control() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrCode","values":{"type":"crt.Input","control":"$UsrCode","validators":[{"id":"usr.MaxLengthFromSysSettingValidator","params":{"settingCode":"MaxProcessLoopCount","message":"Too long"}}]}}]""",
			viewModelConfig: """{"attributes":{"UsrCode":{"modelConfig":{"path":"PDS.UsrCode"}}}}""",
			validators: """{"usr.MaxLengthFromSysSettingValidator":{"validator":function(config){return async function(control){return null;};},"params":[{"name":"settingCode"},{"name":"message"}],"async":true}}""");
		PageUpdateArgs args = new("UsrTest_FormPage", body, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "validators in viewConfigDiff are ignored by runtime and must be rejected before save");
		response.Error.Should().Contain("viewConfigDiff")
			.And.Contain("viewModelConfig/viewModelConfigDiff")
			.And.Contain("UsrCode",
				because: "the validation error should explain where validator bindings belong");
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

	[Test]
	[Description("sync-pages rejects handlers when SCHEMA_HANDLERS stops being an array literal before any save attempt.")]
	public void PageSyncTool_SyncPages_Rejects_NonArray_Handlers_Section() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand updateCommand = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		MockFileSystem fileSystem = new();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, fileSystem, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(fileSystem));
		PageSyncArgs args = new(
			"local",
			[
				new PageSyncPageInput(
					"UsrHandlerShape_FormPage",
					CreatePageBody(
						handlers: """{ request: "crt.HandleViewModelInitRequest", handler: async (request, next) => { await next?.handle(request); } }"""))
			],
			true,
			false);

		// Act
		PageSyncResponse response = tool.SyncPages(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "sync-pages must reject schemas where SCHEMA_HANDLERS is no longer an array literal");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the invalid handlers section should fail client-side validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "sync-pages should include validation details for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "handler-shape validation contributes to content-ok for page bodies");
		response.Pages[0].Error.Should().Contain("SCHEMA_HANDLERS")
			.And.Contain("array literal",
				because: "the error should explain that the handlers section must stay an array");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "client-side handler validation must fail before any remote save call is made");
	}

	[Test]
	[Description("sync-pages rejects request.viewModel handler APIs and tells callers to read handler guidance before any save attempt.")]
	public void PageSyncTool_SyncPages_Rejects_Request_ViewModel_Handler_Api_With_Recovery_Hint() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand updateCommand = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		MockFileSystem fileSystem = new();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, fileSystem, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(fileSystem));
		PageSyncArgs args = new(
			"local",
			[
				new PageSyncPageInput(
					"UsrInvalidHandlerApi_FormPage",
					CreatePageBody(
						handlers: """[{ request: "crt.HandleViewModelAttributeChangeRequest", handler: async (request, next) => { const current = await request.viewModel.get("UsrParkingRequired"); await request.viewModel.set("UsrVehicleNumber", current ? "A-01" : null); return next?.handle(request); } }]"""))
			],
			true,
			false);

		// Act
		PageSyncResponse response = tool.SyncPages(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "sync-pages must reject invented request.viewModel handler APIs");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the invalid handler API should fail client-side validation");
		response.Pages[0].Error.Should().Contain("request.viewModel")
			.And.Contain("page-schema-handlers")
			.And.Contain("canonical clio handler examples")
			.And.Contain("request.value")
			.And.Contain("request.$context",
				because: "the error should reject the invented API and redirect the caller to the clio guidance and canonical handler patterns");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "client-side handler API validation must fail before any remote save call is made");
	}

	[Test]
	[Description("sync-pages rejects validators declared directly on a viewConfigDiff control before any save attempt.")]
	public void PageSyncTool_SyncPages_Rejects_Validators_Declared_On_ViewConfigDiff_Control() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand updateCommand = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		MockFileSystem fileSystem = new();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, fileSystem, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(fileSystem));
		PageSyncArgs args = new(
			"local",
			[
				new PageSyncPageInput(
					"UsrValidatorPlacement_FormPage",
					CreatePageBody(
						viewConfigDiff: """[{"operation":"insert","name":"UsrCode","values":{"type":"crt.Input","control":"$UsrCode","validators":[{"id":"usr.MaxLengthFromSysSettingValidator","params":{"settingCode":"MaxProcessLoopCount","message":"Too long"}}]}}]""",
						viewModelConfig: """{"attributes":{"UsrCode":{"modelConfig":{"path":"PDS.UsrCode"}}}}""",
						validators: """{"usr.MaxLengthFromSysSettingValidator":{"validator":function(config){return async function(control){return null;};},"params":[{"name":"settingCode"},{"name":"message"}],"async":true}}"""))
			],
			true,
			false);

		// Act
		PageSyncResponse response = tool.SyncPages(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "sync-pages must reject validators declared on the UI element before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the invalid validator placement should fail client-side validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "sync-pages should include validation details for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "validator-placement validation contributes to content-ok for page bodies");
		response.Pages[0].Error.Should().Contain("viewConfigDiff")
			.And.Contain("viewModelConfig/viewModelConfigDiff")
			.And.Contain("UsrCode",
				because: "the error should explain that validators belong on the bound view-model attribute");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "client-side validator-placement validation must fail before any remote save call is made");
	}

	[Test]
	[Description("sync-pages reports a handler-shape validation failure only once when SCHEMA_HANDLERS is invalid.")]
	public void PageSyncTool_SyncPages_Does_Not_Duplicate_Handler_Validation_Error() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand updateCommand = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		MockFileSystem fileSystem = new();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, fileSystem, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(fileSystem));
		PageSyncArgs args = new(
			"local",
			[
				new PageSyncPageInput(
					"UsrHandlerShape_FormPage",
					CreatePageBody(
						handlers: """{ request: "crt.HandleViewModelInitRequest", handler: async (request, next) => { await next?.handle(request); } }"""))
			],
			true,
			false);

		// Act
		PageSyncResponse response = tool.SyncPages(args, null).Result;

		// Assert
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "sync-pages should return validation details for the rejected page");
		response.Pages[0].Validation!.Errors!.Count(error =>
			error.Contains("SCHEMA_HANDLERS", StringComparison.Ordinal) &&
			error.Contains("array literal", StringComparison.Ordinal)).Should().Be(1,
			because: "the handler-shape error should be reported once even though marker-content validation already includes handler validation");
	}


	[Test]
	[Description("get-page persists a conflict-detection baseline (UId + checksum + environment identity) into meta.json when the editable schema exists")]
	public void PageGetTool_ShouldWriteBaselineIntoMetaJson_WhenEditableSchemaExists() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": [{"Checksum": "chk-001", "ModifiedOn": "2026-06-12T09:00:00"}]}""");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
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
		PageGetTool tool = new(command, logger, commandResolver, new PageFileWriter(mockFs));

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", "sandbox", null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "the baseline capture must not affect a successful get-page");
		string metaContent = mockFs.GetFile(response.Files.MetaFile).TextContents;
		PageMetaFileModel meta = System.Text.Json.JsonSerializer.Deserialize<PageMetaFileModel>(metaContent);
		meta.Baseline.Should().NotBeNull(because: "an existing editable schema must produce a conflict-detection baseline");
		meta.Baseline.EditableSchemaExists.Should().BeTrue(because: "the editable schema was found in the design package");
		meta.Baseline.EditableSchemaUId.Should().Be("uid-1", because: "the baseline must pin the editable schema identity");
		meta.Baseline.Checksum.Should().Be("chk-001", because: "the baseline must capture the SysSchema checksum at fetch time");
		meta.Baseline.EnvironmentName.Should().Be("sandbox", because: "the baseline must record which environment it was captured against");
	}

	[Test]
	[Description("get-page records editableSchemaExists=false in the baseline when no replacing schema exists yet (willCreateReplacing)")]
	public void PageGetTool_ShouldWriteAbsentBaseline_WhenWillCreateReplacing() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("byPackage")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": []}""");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => !body.Contains("byPackage")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse("UsrMcp_FormPage", "uid-1", "pkg-1", "UsrMcp", "BasePage").ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns("design-pkg");
		hierarchyClient.GetParentSchemas("uid-1", "design-pkg")
			.Returns([new PageDesignerHierarchySchema {
				UId = "uid-1", Name = "UsrMcp_FormPage",
				PackageUId = "pkg-1", PackageName = "UsrMcp",
				SchemaVersion = 1, Body = CreatePageBody()
			}]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(command);
		MockFileSystem mockFs = new();
		PageGetTool tool = new(command, logger, commandResolver, new PageFileWriter(mockFs));

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", "sandbox", null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "the baseline capture must not affect a successful get-page");
		PageMetaFileModel meta = System.Text.Json.JsonSerializer.Deserialize<PageMetaFileModel>(
			mockFs.GetFile(response.Files.MetaFile).TextContents);
		meta.Baseline.Should().NotBeNull(because: "absence of the editable schema is itself baseline information");
		meta.Baseline.EditableSchemaExists.Should().BeFalse(
			because: "no replacing schema exists yet, so an externally created one must be detectable later");
		meta.Baseline.Checksum.Should().BeNull(because: "a non-existent schema has no checksum");
	}

	[Test]
	[Description("get-page degrades to a meta.json without baseline when the checksum query fails, and still succeeds")]
	public void PageGetTool_ShouldOmitBaseline_WhenChecksumQueryFails() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false}""");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
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
		PageGetTool tool = new(command, logger, commandResolver, new PageFileWriter(mockFs));

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", "sandbox", null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "a failed checksum capture must never fail get-page (best-effort, FR-10)");
		PageMetaFileModel meta = System.Text.Json.JsonSerializer.Deserialize<PageMetaFileModel>(
			mockFs.GetFile(response.Files.MetaFile).TextContents);
		meta.Baseline.Should().BeNull(because: "without a checksum the consumer must skip the conflict check rather than compare garbage");
		meta.FetchedAt.Should().NotBeNullOrEmpty(because: "the legacy meta.json contract must stay intact");
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
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			CreatePassthroughPageFileWriter());
	}

	private static IPageFileWriter CreatePassthroughPageFileWriter() {
		IPageFileWriter writer = Substitute.For<IPageFileWriter>();
		writer.WritePageFiles(
				Arg.Any<PageGetResponse>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => callInfo.Arg<PageGetResponse>());
		return writer;
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
		return (new PageGetTool(command, logger, commandResolver, new PageFileWriter(mockFs)), mockFs);
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

	private static void SetupSchemaMetadata(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		string schemaName) {
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateMetadataResponse(
				schemaName,
				"test-schema-uid",
				"test-package-uid",
				"UsrTestPackage",
				"BasePage").ToString());
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
		PageGetTool tool = new(command, logger, commandResolver, new PageFileWriter(mockFs));

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
		PageGetTool tool = new(command, logger, commandResolver, new PageFileWriter(mockFs));

		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		response.Files.BodyFile.Should().Contain(".clio-pages",
			because: "files must be written under .clio-pages directory");
		response.Files.BodyFile.Should().Contain("UsrMcp_FormPage",
			because: "files must be grouped under the schema name subdirectory");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page returns a plain JSON editable fallback body for a mobile page when no replacing schema exists in the design package.")]
	public void TryGetPage_WhenMobilePageHasNoEditableSchema_ReturnsPlainJsonFallbackBody() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		int selectCallIndex = 0;
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => {
				selectCallIndex++;
				return selectCallIndex switch {
					1 => CreateMetadataResponse(
						"UsrMobile_FormPage",
						"mobile-schema-uid",
						"runtime-package-uid",
						"RuntimePkg",
						"BaseMobilePageTemplate").ToString(),
					2 => new JObject {
						["success"] = true,
						["rows"] = new JArray {
							new JObject { ["Name"] = "DesignPkg" }
						}
					}.ToString(),
					_ => new JObject {
						["success"] = true,
						["rows"] = new JArray()
					}.ToString()
				};
			});
		const string runtimeHierarchyBody = """
			{
			  "viewConfigDiff": [{"operation":"insert","name":"Marker","values":{"type":"crt.Label"}}],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("mobile-schema-uid").Returns("design-package-uid");
		hierarchyClient.GetParentSchemas("mobile-schema-uid", "design-package-uid")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "mobile-schema-uid",
					Name = "UsrMobile_FormPage",
					PackageUId = "runtime-package-uid",
					PackageName = "RuntimePkg",
					SchemaVersion = 1,
					SchemaType = 10,
					Body = runtimeHierarchyBody
				}
			]);
		PageGetCommand command = CreatePageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient);

		// Act
		bool result = command.TryGetPage(
			new PageGetOptions { SchemaName = "UsrMobile_FormPage" },
			out PageGetResponse response);

		// Assert
		result.Should().BeTrue(
			because: "mobile page metadata and hierarchy are valid");
		response.Page.SchemaType.Should().Be("mobile",
			because: "the schema type from the designer hierarchy should be surfaced to callers");
		response.Raw.Body.TrimStart().Should().StartWith("{",
			because: "mobile fallback editable bodies must be plain JSON, not AMD define modules");
		response.Raw.Body.Should().NotContain("define(",
			because: "AMD wrappers are invalid for mobile page bodies");
		response.Raw.Body.Should().NotContain("\"Marker\"",
			because: "when no replacing schema exists in the design package, get-page must return the empty mobile fallback, not the runtime-package hierarchy body");
		response.Raw.Body.Should().Be(
			"{\n\t\"viewConfigDiff\": [],\n\t\"viewModelConfigDiff\": [],\n\t\"modelConfigDiff\": []\n}",
			because: "BuildEmptyBody must return the canonical empty mobile JSON skeleton with the three required top-level arrays and no AMD/handlers/validators/converters sections");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page writes proxy bindings unchanged because body.js is the editable source body.")]
	public void PageGetTool_WhenBodyHasProxyBindings_WritesBodyUnchanged() {
		// Arrange
		string proxyBody = CreatePageBody(
			viewConfigDiff: """[{"operation":"insert","name":"UsrStatus","values":{"type":"crt.ComboBox","label":"$Resources.Strings.PDS_UsrStatus","control":"$UsrStatus"}}]""",
			viewModelConfigDiff: """[{"operation":"merge","values":{"UsrStatus":{"modelConfig":{"path":"PDS.UsrStatus"}}}}]""");
		(PageGetTool tool, MockFileSystem mockFs) = CreatePageGetToolWithBody(proxyBody);

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "get-page should succeed even when the source body has proxy view-model attribute bindings");
		string writtenBody = mockFs.File.ReadAllText(response.Files.BodyFile);
		writtenBody.Should().Be(proxyBody,
			because: "get-page should not silently rewrite editable body.js content");
		writtenBody.Should().Contain("\"$UsrStatus\"",
			because: "callers must make any binding repairs explicitly in the page body");
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
	[Description("get-page wipes the schema directory before writing so stale files from prior fetches or AI-authored drafts do not leak into the fresh response")]
	public void PageGetTool_WhenSchemaDirHasStaleFiles_WipesBeforeWriting() {
		(PageGetTool tool, MockFileSystem mockFs) = CreatePageGetToolWithBody(CreatePageBody());
		string schemaDir = System.IO.Path.Combine(mockFs.Directory.GetCurrentDirectory(), ".clio-pages", "UsrMcp_FormPage");
		mockFs.Directory.CreateDirectory(schemaDir);
		string stalePath = System.IO.Path.Combine(schemaDir, "body.new.js");
		mockFs.File.WriteAllText(stalePath, "stale draft content");
		string oldBodyPath = System.IO.Path.Combine(schemaDir, "body.js");
		mockFs.File.WriteAllText(oldBodyPath, "previous session body");

		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		response.Success.Should().BeTrue();
		mockFs.File.Exists(stalePath).Should().BeFalse(
			because: "stale AI-authored files in the schema dir must not survive a fresh get-page call");
		mockFs.File.ReadAllText(oldBodyPath).Should().NotBe("previous session body",
			because: "body.js must be overwritten with the fresh fetched body");
		response.Files.FetchedAt.Should().NotBeNullOrEmpty(
			because: "get-page must report the ISO-8601 fetch timestamp so callers can detect cache staleness");
	}

	[Test]
	[Category("Unit")]
	[Description("get-page writes a .gitignore entry under .clio-pages so the working tree stays clean by default")]
	public void PageGetTool_WhenWriting_AddsGitIgnoreEntry() {
		(PageGetTool tool, MockFileSystem mockFs) = CreatePageGetToolWithBody(CreatePageBody());

		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		response.Success.Should().BeTrue();
		string gitignorePath = System.IO.Path.Combine(mockFs.Directory.GetCurrentDirectory(), ".clio-pages", ".gitignore");
		mockFs.File.Exists(gitignorePath).Should().BeTrue(
			because: "the .clio-pages directory is ephemeral MCP state and must be git-ignored by default");
		string content = mockFs.File.ReadAllText(gitignorePath);
		content.Should().Contain("*",
			because: "the gitignore must exclude all cached page files");
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
		failingFs.Path.GetFullPath(Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0));
		failingFs.Directory.GetCurrentDirectory().Returns("/workspace");
		// Configure the workspace-root walk-up so it terminates: a bare IDirectoryInfo substitute
		// returns a non-null recursive substitute for .Parent, so PageOutputDirectoryResolver
		// would loop forever and exhaust memory. A single directory whose Parent is null models a
		// filesystem root and lets the resolver fall through to the current directory.
		System.IO.Abstractions.IDirectoryInfo workspaceDir = Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
		workspaceDir.FullName.Returns("/workspace");
		workspaceDir.Parent.Returns((System.IO.Abstractions.IDirectoryInfo)null);
		failingFs.DirectoryInfo.New(Arg.Any<string>()).Returns(workspaceDir);
		failingFs.Directory.When(d => d.CreateDirectory(Arg.Any<string>()))
			.Do(_ => throw new System.UnauthorizedAccessException("Access denied"));
		PageGetTool tool = new(command, logger, commandResolver, new PageFileWriter(failingFs));

		PageGetResponse response = tool.GetPage(new PageGetArgs("UsrMcp_FormPage", null, null, null, null));

		response.Success.Should().BeFalse(because: "directory creation failure should produce a failed response");
		response.Error.Should().Contain("Failed to prepare output directory",
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
				if (callIndex == 3) { lastSavePayload = ci.ArgAt<string>(1); return saveResponse.ToString(); }
				return new JObject { ["success"] = true }.ToString();
			});

		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);
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
				["localizableStrings"] = new JArray {
					new JObject {
						["name"] = "DefaultPageTitle",
						["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "Page title" } }
					},
					new JObject {
						["name"] = "SaveButton",
						["values"] = new JArray { new JObject { ["cultureName"] = "en-US", ["value"] = "Save" } }
					}
				},
				["package"] = new JObject { ["uId"] = originalPackageUId, ["name"] = "CrtCustomer360App" },
				["parent"] = new JObject { ["uId"] = "b7b898d0-8c77-4953-c097-23fa6800da02", ["name"] = "ListPageV3Template" },
				["isReadOnly"] = true,
				["optionalProperties"] = new JArray()
			}
		};
		var saveResponse = new JObject { ["success"] = true };
		var noRowsResponse = new JObject { ["success"] = true, ["rows"] = new JArray() };
		string lastSavePayload = null;
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				if (callIndex == 1) return metadataResponse.ToString();
				if (callIndex == 2) return noRowsResponse.ToString();
				if (callIndex == 3) return getSchemaResponse.ToString();
				if (callIndex == 4) { lastSavePayload = ci.ArgAt<string>(1); return saveResponse.ToString(); }
				return new JObject { ["success"] = true }.ToString();
			});

		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);
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
		savedDto["localizableStrings"].Should().NotBeNull(
			because: "SaveSchema deletes schema-level strings omitted from the DTO, so new replacing schemas must carry template resources");
		savedDto["localizableStrings"].Children<JObject>().Select(item => item["name"].ToString())
			.Should().BeEquivalentTo(["DefaultPageTitle", "SaveButton"],
				because: "the designer sends inherited template localizable strings on first save");
		savedDto["body"].ToString().Should().Be(validBody,
			because: "the body passed to update-page must be written into the new replacing schema DTO");
	}

	[Test]
	[Description("TryUpdatePage materializes the parent schema caption onto a newly created replacing schema so its runtime title is not lost")]
	public void TryUpdatePage_WhenCreatingReplacing_PreservesParentCaption() {
		// Arrange
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
		var parentCaption = new JArray {
			new JObject { ["cultureName"] = "en-US", ["value"] = "Accounts list" },
			new JObject { ["cultureName"] = "uk-UA", ["value"] = "Список контрагентів" }
		};
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
				["caption"] = parentCaption.DeepClone(),
				["localizableStrings"] = new JArray(),
				["package"] = new JObject { ["uId"] = originalPackageUId, ["name"] = "CrtCustomer360App" },
				["parent"] = new JObject { ["uId"] = "b7b898d0-8c77-4953-c097-23fa6800da02", ["name"] = "ListPageV3Template" },
				["isReadOnly"] = true,
				["optionalProperties"] = new JArray()
			}
		};
		var saveResponse = new JObject { ["success"] = true };
		var noRowsResponse = new JObject { ["success"] = true, ["rows"] = new JArray() };
		string lastSavePayload = null;
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				if (callIndex == 1) return metadataResponse.ToString();
				if (callIndex == 2) return noRowsResponse.ToString();
				if (callIndex == 3) return getSchemaResponse.ToString();
				if (callIndex == 4) { lastSavePayload = ci.ArgAt<string>(1); return saveResponse.ToString(); }
				return new JObject { ["success"] = true }.ToString();
			});

		// Act
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);
		bool ok = command.TryUpdatePage(new PageUpdateOptions { SchemaName = "Accounts_ListPage", Body = validBody, DryRun = false }, out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: "the create-replacing save should succeed; error: " + response.Error);
		var savedDto = JObject.Parse(lastSavePayload);
		savedDto["extendParent"].Value<bool>().Should().BeTrue(
			because: "a create-replacing DTO must extend its parent");
		savedDto["caption"].Should().NotBeNull(
			because: "the replacing schema must carry a caption so its runtime title (e.g. a dashboard tab) is not lost");
		JToken.DeepEquals(savedDto["caption"], parentCaption).Should().BeTrue(
			because: "the parent schema caption must be materialized verbatim onto the replacing schema, mirroring the platform designer and CreateDesignSchema");
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

		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
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

	[Test]
	[Description("BuildSaveErrorMessage appends an actionable hint for the 'Object vs Array' server error that typically happens when resending the full raw.body")]
	public void PageUpdateCommand_Should_Append_Hint_For_Object_Vs_Array_Error() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		const string originalUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
		const string designPkg = "11111111-2222-3333-4444-555555555555";
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(ci => "http://test" + ci.ArgAt<string>(0));
		hierarchyClient.GetDesignPackageUId(originalUId).Returns(designPkg);
		hierarchyClient.GetParentSchemas(originalUId, designPkg).Returns(new List<PageDesignerHierarchySchema> {
			new() { UId = originalUId, Name = "Test_FormPage", PackageUId = designPkg, PackageName = "DesignPkg" }
		});
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["UId"] = originalUId } }
		};
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject { ["uId"] = originalUId, ["name"] = "Test_FormPage", ["body"] = "x", ["package"] = new JObject { ["uId"] = designPkg } }
		};
		var saveError = new JObject {
			["success"] = false,
			["errorInfo"] = new JObject {
				["message"] = "The requested operation requires an element of type 'Object', but the target element has type 'Array'."
			}
		};
		int callIndex = 0;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				return callIndex switch {
					1 => metadataResponse.ToString(),
					2 => getSchemaResponse.ToString(),
					_ => saveError.ToString()
				};
			});
		string validBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);

		bool ok = command.TryUpdatePage(new PageUpdateOptions { SchemaName = "Test_FormPage", Body = validBody, DryRun = false }, out PageUpdateResponse response);

		ok.Should().BeFalse();
		response.Error.Should().Contain("Object", "original server error must be preserved");
		response.Error.Should().Contain("hint:", "hint annotation must be appended");
		response.Error.Should().Contain("re-sending the full get-page raw.body",
			"hint must explain the likely cause");
		response.Error.Should().Contain("page-modification",
			"hint must point back to the canonical guide resource");
	}

	[Test]
	[Description("PageGetCommand.BuildOwnBodySummary exposes operation counts so AI can detect when raw.body is not safe to resend")]
	public void PageGetCommand_Should_Populate_OwnBodySummary_On_Response() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		var bodyParser = Substitute.For<IPageSchemaBodyParser>();
		var bundleBuilder = Substitute.For<IPageBundleBuilder>();
		const string schemaUId = "c787571c-c8ca-4c9b-b05b-7ccbe0271a76";
		const string packageUId = "51a0fc55-ce4f-a533-0340-b70a0c04b905";
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(ci => "http://test" + ci.ArgAt<string>(0));
		hierarchyClient.GetDesignPackageUId(schemaUId).Returns(packageUId);
		var hierarchySchema = new PageDesignerHierarchySchema {
			UId = schemaUId,
			Name = "Leads_ListPage",
			PackageUId = packageUId,
			PackageName = "CrtLead",
			Body = new string('x', 14000)
		};
		hierarchyClient.GetParentSchemas(schemaUId, packageUId).Returns(new List<PageDesignerHierarchySchema> { hierarchySchema });
		bodyParser.Parse(Arg.Any<string>()).Returns(new PageParsedSchemaBody {
			ViewConfigDiff = new Newtonsoft.Json.Linq.JArray { new Newtonsoft.Json.Linq.JObject(), new Newtonsoft.Json.Linq.JObject(), new Newtonsoft.Json.Linq.JObject() },
			ViewModelConfigDiff = new Newtonsoft.Json.Linq.JArray { new Newtonsoft.Json.Linq.JObject() },
			ModelConfigDiff = new Newtonsoft.Json.Linq.JArray(),
			Handlers = "[{request:'a',handler:()=>{}},{request:'b',handler:()=>{}}]"
		});
		bundleBuilder.Build(Arg.Any<IReadOnlyList<PageSchemaBundlePart>>()).Returns(new PageBundleInfo());
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["UId"] = schemaUId, ["Name"] = "Leads_ListPage", ["PackageName"] = "CrtLead", ["PackageUId"] = packageUId, ["ParentSchemaName"] = "ListPageV3Template" } }
		};
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		var command = new PageGetCommand(applicationClient, serviceUrlBuilder, logger, hierarchyClient, bodyParser, bundleBuilder, CreatePassthroughPageFileWriter());

		bool ok = command.TryGetPage(new PageGetOptions { SchemaName = "Leads_ListPage" }, out PageGetResponse response);

		ok.Should().BeTrue();
		response.Page.OwnBodySummary.Should().NotBeNull(
			because: "every successful get-page response must include the own body summary so AI can decide whether raw.body is safe to resend");
		response.Page.OwnBodySummary.BodyLength.Should().Be(14000);
		response.Page.OwnBodySummary.ViewConfigDiffOperations.Should().Be(3,
			because: "viewConfigDiff operation count must match the parsed JArray length");
		response.Page.OwnBodySummary.ViewModelConfigDiffOperations.Should().Be(1);
		response.Page.OwnBodySummary.ModelConfigDiffOperations.Should().Be(0);
		response.Page.OwnBodySummary.HandlerEntries.Should().Be(2,
			because: "handler entries are counted by top-level object literals in the handlers marker block");
	}

	[Test]
	[Description("PageBodyMerger.Merge concatenates viewConfigDiff entries, dedupes by name (incoming wins), and preserves other sections")]
	public void PageBodyMerger_Should_Merge_ViewConfigDiff_And_Handlers() {
		string currentBody = "define(\"P\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"RefreshButton\",\"values\":{\"size\":\"large\"}},{\"operation\":\"insert\",\"name\":\"Existing\",\"values\":{\"type\":\"crt.Input\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[{request:\"crt.KeepMeRequest\",handler:()=>null}]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		string incomingBody = "define(\"P\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"TestButton\",\"values\":{\"type\":\"crt.Button\",\"caption\":\"Test\"},\"parentName\":\"ActionButtonsContainer\"},{\"operation\":\"merge\",\"name\":\"RefreshButton\",\"values\":{\"size\":\"small\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[{request:\"usr.TestRequest\",handler:async(r)=>{alert(\"hi\");return r.next?.handle(r);}}]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("\"name\": \"Existing\"", because: "existing entries without collisions are preserved");
		merged.Should().Contain("\"name\": \"TestButton\"", because: "new entries are appended");
		merged.Should().Contain("\"size\": \"small\"", because: "incoming wins when names collide (RefreshButton gets size:small)");
		merged.Should().NotContain("\"size\": \"large\"", because: "the colliding entry is superseded");
		merged.Should().Contain("crt.KeepMeRequest", because: "existing handlers without request collision are preserved");
		merged.Should().Contain("usr.TestRequest", because: "new handlers are appended");
	}

	[Test]
	[Description("update-page append mode permits bodies that omit sections — missing markers are not treated as validation failures because the current schema body supplies those sections")]
	public void TryUpdatePage_AppendMode_Should_Allow_Body_With_Missing_Markers() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		var hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		const string originalUId = "86416224-550a-4087-87d9-d4ebc9aa69c8";
		const string designPkg = "520a3697-4d73-c598-38d4-a7501f8c8e9b";
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(ci => "http://test" + ci.ArgAt<string>(0));
		hierarchyClient.GetDesignPackageUId(originalUId).Returns(designPkg);
		hierarchyClient.GetParentSchemas(originalUId, designPkg).Returns(new List<PageDesignerHierarchySchema> {
			new() { UId = originalUId, Name = "Opportunities_ListPage", PackageUId = designPkg, PackageName = "CrtWaterfallPipelineInLeadOppMgmt" }
		});
		string currentBody = "define(\"Opportunities_ListPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"Existing\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray { new JObject { ["UId"] = originalUId } }
		};
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject { ["uId"] = originalUId, ["name"] = "Opportunities_ListPage", ["body"] = currentBody, ["package"] = new JObject { ["uId"] = designPkg } }
		};
		var saveResponse = new JObject { ["success"] = true };
		int callIndex = 0;
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				return callIndex switch {
					1 => metadataResponse.ToString(),
					2 => getSchemaResponse.ToString(),
					_ => saveResponse.ToString()
				};
			});
		string incomingFragment = "/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"TestButton\",\"values\":{\"type\":\"crt.Button\",\"caption\":\"Test\"},\"parentName\":\"ActionButtonsContainer\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/ /**SCHEMA_HANDLERS*/[{request:\"usr.TestRequest\",handler:()=>alert(\"Test\")}]/**SCHEMA_HANDLERS*/";
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);

		bool ok = command.TryUpdatePage(new PageUpdateOptions {
			SchemaName = "Opportunities_ListPage",
			Body = incomingFragment,
			Mode = "append",
			DryRun = false
		}, out PageUpdateResponse response);

		ok.Should().BeTrue(because: "append mode should accept bodies with only the sections that change");
		response.Error.Should().BeNull();
		response.Success.Should().BeTrue();
	}

	[Test]
	[Description("PageBodyMerger handlers dedupe: when incoming declares the same request as current, incoming wins")]
	public void PageBodyMerger_Should_Dedupe_Handlers_By_Request() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[{request:\"usr.X\",handler:()=>{return 'old';}}]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/ /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[{request:\"usr.X\",handler:()=>{return 'new';}}]/**SCHEMA_HANDLERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("'new'", because: "incoming handler wins when request string matches");
		merged.Should().NotContain("'old'", because: "old handler with the same request is dropped");
	}

	[Test]
	[Description("PageBodyMerger converters merge: new converter keys from incoming are appended to existing converters")]
	public void PageBodyMerger_Should_Merge_Converters_By_Key() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.ToUpperCase\":function(value){return value.toUpperCase();}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.ToCurrency\":function(value){return '$'+value;}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.ToUpperCase", because: "existing converter without key collision must be preserved");
		merged.Should().Contain("usr.ToCurrency", because: "new converter from incoming must be added");
		merged.Should().Contain("value.toUpperCase()", because: "existing converter body must remain intact");
		merged.Should().Contain("'$'+value", because: "incoming converter body must be present in the result");
	}

	[Test]
	[Description("PageBodyMerger converters dedupe: when incoming declares the same key as current, incoming wins")]
	public void PageBodyMerger_Should_Dedupe_Converters_By_Key() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.ToUpperCase\":function(value){return value.toUpperCase();},\"usr.Keep\":function(v){return v;}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.ToUpperCase\":function(value){return value.toUpperCase().trim();}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("value.toUpperCase().trim()", because: "incoming converter wins when key collides");
		merged.Should().NotContain("value.toUpperCase();}", because: "old converter with the same key must be dropped");
		merged.Should().Contain("usr.Keep", because: "non-colliding existing converter must be preserved");
	}

	[Test]
	[Description("PageBodyMerger converters: empty current converters are replaced entirely by incoming")]
	public void PageBodyMerger_Should_Replace_Empty_Converters_With_Incoming() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.ToUpperCase\":function(value){return value?.toUpperCase()??'';}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.ToUpperCase", because: "incoming converter should appear when current is empty");
		merged.Should().Contain("value?.toUpperCase()??''", because: "incoming converter body should be preserved verbatim");
	}

	[Test]
	[Description("PageBodyMerger converters: incoming body with no SCHEMA_CONVERTERS marker leaves existing converters intact")]
	public void PageBodyMerger_Should_Preserve_Converters_When_Incoming_Has_No_Converters_Section() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.ToUpperCase\":function(value){return value.toUpperCase();}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		// Incoming body intentionally omits the SCHEMA_CONVERTERS marker entirely.
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.ToUpperCase", because: "existing converter must survive when incoming body has no SCHEMA_CONVERTERS section");
		merged.Should().Contain("value.toUpperCase()", because: "existing converter body must remain verbatim when incoming omits the section");
	}

	[Test]
	[Description("PageBodyMerger converters: incoming empty {} leaves existing converters intact")]
	public void PageBodyMerger_Should_Preserve_Converters_When_Incoming_Converters_Are_Empty() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.FormatDate\":function(value){return new Date(value).toLocaleDateString();}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.FormatDate", because: "existing converter must survive when incoming carries an empty converter section");
		merged.Should().Contain("toLocaleDateString()", because: "existing converter body must remain verbatim when incoming is empty");
	}

	[Test]
	[Description("PageBodyMerger converters: async arrow function with await, template literal, and regex survives the merge unchanged")]
	public void PageBodyMerger_Should_Preserve_Async_Arrow_Converter_Body() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.FormatPhoneNumber\": async (value) => {" +
			"  if (!value) return \"\";" +
			"  const svc = new sdk.SysSettingsService();" +
			"  const setting = await svc.getByCode(\"UsrEnablePhoneFormatting\");" +
			"  if (!Boolean(setting?.value)) return value;" +
			"  const digits = String(value).replace(/\\D/g, \"\");" +
			"  if (digits.length !== 11) return value;" +
			"  return `+${digits.slice(0, 1)} (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7, 9)}-${digits.slice(9, 11)}`;" +
			"}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.FormatPhoneNumber",
			because: "async converter key must survive the merge");
		merged.Should().Contain("await svc.getByCode",
			because: "await expression inside the converter value must be preserved verbatim");
		merged.Should().Contain("digits.slice(1, 4)",
			because: "template literal interpolation content must not be truncated by the depth-tracking parser");
		merged.Should().Contain("replace(/\\D/g",
			because: "regex literal inside the converter body must be preserved verbatim");
	}

	[Test]
	[Description("PageBodyMerger converters: converter value with multiple nested brace pairs is kept intact after merge")]
	public void PageBodyMerger_Should_Preserve_Converter_With_Nested_Braces() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{" +
			"\"usr.FormatScore\": function(value) {" +
			"  if (!value) { return \"\"; }" +
			"  if (value >= 90) { return \"Excellent\"; }" +
			"  return \"Poor\";" +
			"}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.NewConv\": function(v){return v;}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.FormatScore",
			because: "existing converter with nested braces must survive the merge");
		merged.Should().Contain("return \"Excellent\"",
			because: "inner brace content must not be truncated by the depth-tracking entry parser");
		merged.Should().Contain("usr.NewConv",
			because: "new incoming converter must be appended");
	}

	[Test]
	[Description("PageBodyMerger converters: curly braces and commas inside string literals do not confuse the entry splitter")]
	public void PageBodyMerger_Should_Not_Split_On_Structural_Chars_Inside_Strings() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{" +
			"\"usr.A\": function(v){ return v + \" {hello, world}\"; }," +
			"\"usr.B\": function(v){ return \"{x,y}\"; }" +
			"}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.C\": function(v){return v;}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.A",
			because: "converter whose value contains string literals with braces and commas must survive as a complete entry");
		merged.Should().Contain("{hello, world}",
			because: "string content with structural characters must be preserved verbatim");
		merged.Should().Contain("usr.B",
			because: "second existing converter following a string-containing entry must also be preserved");
		merged.Should().Contain("{x,y}",
			because: "string content of second converter must be preserved verbatim");
		merged.Should().Contain("usr.C",
			because: "new incoming converter must be appended");
	}

	[Test]
	[Description("PageBodyMerger converters: single-quoted converter keys are parsed and merged correctly")]
	public void PageBodyMerger_Should_Handle_Single_Quoted_Converter_Keys() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{'usr.OldConverter': function(v){return v;}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{'usr.NewConverter': function(v){return v + '!';}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.OldConverter",
			because: "existing single-quoted key converter must survive merge");
		merged.Should().Contain("usr.NewConverter",
			because: "incoming single-quoted key converter must be added");
	}

	[Test]
	[Description("PageBodyMerger converters: single-quoted key incoming overwrites same-named single-quoted key in current")]
	public void PageBodyMerger_Should_Overwrite_Single_Quoted_Key_On_Name_Clash() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{'usr.Format': function(v){return 'old';}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{'usr.Format': function(v){return 'new';}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("'usr.Format': function(v){return 'new';}",
			because: "incoming converter entry must replace the current one with the same key");
		merged.Should().NotContain("return 'old'",
			because: "overwritten converter body must not appear in the merged result");
	}

	[Test]
	[Description("PageBodyMerger converters: double-quoted current key and single-quoted incoming key with the same name deduplicate correctly")]
	public void PageBodyMerger_Should_Deduplicate_Cross_Quote_Key_Clash() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.Format\": function(v){return 'old';}}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{'usr.Format': function(v){return 'new';}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("'usr.Format': function(v){return 'new';}",
			because: "incoming single-quoted entry must win over the existing double-quoted entry with the same key");
		merged.Should().NotContain("return 'old'",
			because: "the double-quoted current entry must be removed — it refers to the same logical key");
	}

	[Test]
	[Description("PageBodyMerger converters: second entry is not absorbed when first entry value contains nested brackets")]
	public void PageBodyMerger_Should_Not_Absorb_Next_Entry_After_Nested_Brackets() {
		// Regression guard: balanced nested brackets in a value must not prevent the
		// top-level comma from splitting the next entry.
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{" +
			"\"usr.A\": function(v){return {x: v};}," +
			"\"usr.B\": function(v){return v;}" +
			"}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.C\": function(v){return v;}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.A",
			because: "first entry with a nested-object value must be preserved");
		merged.Should().Contain("usr.B",
			because: "second entry must not be absorbed into the first entry's value when the preceding value contains nested brackets");
		merged.Should().Contain("usr.C",
			because: "incoming entry must be appended");
	}

	[Test]
	[Description("PageBodyMerger converters: quoted entry following an unquoted ES6 method-shorthand entry is not corrupted")]
	public void PageBodyMerger_Should_Not_Corrupt_Quoted_Entry_After_Unquoted_Key() {
		// Regression guard: an unquoted key whose value contains a string literal (e.g. return "x")
		// must not cause the parser to misidentify that string as the next key. Without the fix,
		// ParseConverterEntries would treat "fallback" as a key and corrupt the subsequent "usr.A" entry.
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{" +
			"toDisplayValue(v) { return \"fallback\"; }," +
			"\"usr.A\": function(v){return v;}" +
			"}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{\"usr.B\": function(v){return v;}}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("usr.A",
			because: "the quoted entry after the unquoted key must survive merge without corruption");
		merged.Should().Contain("usr.B",
			because: "the incoming quoted entry must be appended");
	}

	[Test]
	[Description("PageBodyMerger converters: when both current and incoming converter sections are empty the result is also empty")]
	public void PageBodyMerger_Should_Return_Empty_Converters_When_Both_Are_Empty() {
		string currentBody = "/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/ /**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/ " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			because: "merging two empty converter sections must produce an empty converter section");
	}

	[Test]
	[Description("ParseSamplingResponse parses a valid JSON response with ok=true")]
	public void ParseSamplingResponse_Should_Parse_Ok_Response() {
		string text = "{\"ok\":true,\"issues\":[],\"warnings\":[]}";

		PageSamplingReview result = PageBodySamplingService.ParseSamplingResponse(text);

		result.Ok.Should().BeTrue(because: "ok=true in the response");
		result.Skipped.Should().BeFalse(because: "valid response was parsed successfully");
		result.Issues.Should().BeNull(because: "empty issues array becomes null");
		result.Warnings.Should().BeNull(because: "empty warnings array becomes null");
	}

	[Test]
	[Description("ParseSamplingResponse parses a response with ok=false and issues")]
	public void ParseSamplingResponse_Should_Parse_Issues() {
		string text = "{\"ok\":false,\"issues\":[\"handler 'usr.Missing' not found\"],\"warnings\":[\"minor concern\"]}";

		PageSamplingReview result = PageBodySamplingService.ParseSamplingResponse(text);

		result.Ok.Should().BeFalse(because: "ok=false in the response");
		result.Issues.Should().ContainSingle(because: "one issue was reported")
			.Which.Should().Contain("usr.Missing");
		result.Warnings.Should().ContainSingle(because: "one warning was reported");
	}

	[Test]
	[Description("ParseSamplingResponse strips markdown code fences from the response")]
	public void ParseSamplingResponse_Should_Strip_Markdown_Fences() {
		string text = "```json\n{\"ok\":true,\"issues\":[],\"warnings\":[]}\n```";

		PageSamplingReview result = PageBodySamplingService.ParseSamplingResponse(text);

		result.Ok.Should().BeTrue(because: "markdown fences should be stripped before parsing");
		result.Skipped.Should().BeFalse(because: "valid response was parsed after stripping fences");
	}

	[Test]
	[Description("ParseSamplingResponse returns Skipped=true for unparseable text")]
	public void ParseSamplingResponse_Should_Skip_On_Invalid_Text() {
		string text = "I cannot review this page.";

		PageSamplingReview result = PageBodySamplingService.ParseSamplingResponse(text);

		result.Skipped.Should().BeTrue(because: "non-JSON text should result in a skipped review");
	}

	[Test]
	[Description("MobileSystemPrompt is distinct from SystemPrompt and validates mobile-specific type-mismatch heuristic")]
	public void MobileSystemPrompt_Should_Be_Mobile_Specific() {
		PageBodySamplingService.MobileSystemPrompt.Should().NotBe(PageBodySamplingService.SystemPrompt,
			because: "mobile and web prompts must be different");
		PageBodySamplingService.MobileSystemPrompt.Should().Contain("mobile",
			because: "the mobile prompt should mention mobile pages");
		PageBodySamplingService.MobileSystemPrompt.Should().NotContain("SCHEMA_HANDLERS",
			because: "mobile pages do not have SCHEMA_HANDLERS markers");
		PageBodySamplingService.MobileSystemPrompt.Should().NotContain("SCHEMA_CONVERTERS",
			because: "mobile pages do not have SCHEMA_CONVERTERS markers");
		PageBodySamplingService.MobileSystemPrompt.Should().Contain("viewModelConfig",
			because: "the prompt must mention both viewModelConfigDiff and viewModelConfig variants");
		PageBodySamplingService.MobileSystemPrompt.Should().Contain("modelConfig",
			because: "the prompt must mention both modelConfigDiff and modelConfig variants");
		PageBodySamplingService.MobileSystemPrompt.Should().Contain("Type mismatch",
			because: "the prompt must include the type-mismatch heuristic");
		PageBodySamplingService.MobileSystemPrompt.Should().Contain("crt.DateTimePicker",
			because: "the prompt must give a concrete example of type-mismatch");
		PageBodySamplingService.MobileSystemPrompt.Should().NotContain("operation",
			because: "viewConfigDiff structure is checked deterministically, not by sampling");
	}

	[Test]
	[Description("Web SystemPrompt validates handler request references, non-crt converter declarations, and type-mismatch heuristic")]
	public void SystemPrompt_Should_Validate_Handler_And_Converter_References() {
		PageBodySamplingService.SystemPrompt.Should().Contain("request",
			because: "web prompt must mention that handlers are matched by their request field");
		PageBodySamplingService.SystemPrompt.Should().Contain("SCHEMA_HANDLERS",
			because: "web prompt must reference SCHEMA_HANDLERS for handler cross-reference");
		PageBodySamplingService.SystemPrompt.Should().Contain("crt.",
			because: "web prompt must explain that crt.* converters are built-in and must not be declared");
		PageBodySamplingService.SystemPrompt.Should().Contain("SCHEMA_VALIDATORS",
			because: "web prompt must reference SCHEMA_VALIDATORS for validator type resolution — " +
				"structural validator checks (params, resource format) are deterministic, but type resolution " +
				"is ambiguous (page-local vs remote module) and belongs in sampling");
		PageBodySamplingService.SystemPrompt.Should().NotContain("view binds to viewModel only",
			because: "MVVM binding is handled deterministically, not by sampling");
		PageBodySamplingService.SystemPrompt.Should().Contain("Type mismatch",
			because: "web prompt must include the type-mismatch heuristic for control-to-attribute checks");
	}

	[Test]
	[Description("PageBodyMerger mobile: merges viewConfigDiff by name, viewModelConfigDiff and modelConfigDiff by append")]
	public void PageBodyMerger_Should_Merge_Mobile_Bodies() {
		string currentBody = "{\"viewConfigDiff\":[{\"operation\":\"merge\",\"name\":\"Existing\",\"values\":{\"size\":\"large\"}}],\"viewModelConfigDiff\":[{\"operation\":\"insert\",\"name\":\"VM1\"}],\"modelConfigDiff\":[{\"operation\":\"insert\",\"name\":\"M1\"}]}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"NewButton\",\"values\":{\"type\":\"crt.Button\"}},{\"operation\":\"merge\",\"name\":\"Existing\",\"values\":{\"size\":\"small\"}}],\"viewModelConfigDiff\":[{\"operation\":\"insert\",\"name\":\"VM2\"}],\"modelConfigDiff\":[{\"operation\":\"insert\",\"name\":\"M2\"}]}";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		JObject result = JObject.Parse(merged);
		JArray viewConfigDiff = (JArray)result["viewConfigDiff"];
		viewConfigDiff.Count.Should().Be(2, because: "existing + new entry, collision replaced");
		viewConfigDiff.Any(t => t["name"]?.ToString() == "NewButton").Should().BeTrue(because: "new entry is appended");
		viewConfigDiff.Any(t => t["values"]?["size"]?.ToString() == "small").Should().BeTrue(because: "incoming wins on name collision");
		viewConfigDiff.Any(t => t["values"]?["size"]?.ToString() == "large").Should().BeFalse(because: "old entry with same name is replaced");

		JArray viewModelConfigDiff = (JArray)result["viewModelConfigDiff"];
		viewModelConfigDiff.Count.Should().Be(2, because: "viewModelConfigDiff uses append, both items kept");

		JArray modelConfigDiff = (JArray)result["modelConfigDiff"];
		modelConfigDiff.Count.Should().Be(2, because: "modelConfigDiff uses append, both items kept");
	}

	[Test]
	[Description("PageBodyMerger mobile: incoming body with empty arrays leaves current arrays unchanged")]
	public void PageBodyMerger_Mobile_Should_Preserve_Current_When_Incoming_Is_Empty() {
		string currentBody = "{\"viewConfigDiff\":[{\"operation\":\"merge\",\"name\":\"A\"}],\"viewModelConfigDiff\":[{\"operation\":\"insert\",\"name\":\"VM1\"}],\"modelConfigDiff\":[]}";
		string incomingBody = "{\"viewConfigDiff\":[],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		JObject result = JObject.Parse(merged);
		((JArray)result["viewConfigDiff"]).Count.Should().Be(1, because: "current entry must survive when incoming viewConfigDiff is empty");
		((JArray)result["viewModelConfigDiff"]).Count.Should().Be(1, because: "current entry must survive when incoming viewModelConfigDiff is empty");
		((JArray)result["modelConfigDiff"]).Count.Should().Be(0, because: "both sides are empty so result is empty");
	}

	[Test]
	[Description("PageBodyMerger mobile: incoming body missing array keys still produces a valid result")]
	public void PageBodyMerger_Mobile_Should_Handle_Missing_Keys_In_Incoming() {
		string currentBody = "{\"viewConfigDiff\":[{\"operation\":\"merge\",\"name\":\"A\"}],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"B\"}]}";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		JObject result = JObject.Parse(merged);
		((JArray)result["viewConfigDiff"]).Count.Should().Be(2, because: "A (existing) and B (incoming) should both be present");
		((JArray)result["viewModelConfigDiff"]).Count.Should().Be(0, because: "incoming has no viewModelConfigDiff, current is empty");
	}

	[Test]
	[Description("PageBodyMerger mobile: current body missing array keys gets them from incoming")]
	public void PageBodyMerger_Mobile_Should_Handle_Missing_Keys_In_Current() {
		string currentBody = "{}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"A\"}],\"viewModelConfigDiff\":[{\"operation\":\"insert\",\"name\":\"VM1\"}]}";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		JObject result = JObject.Parse(merged);
		((JArray)result["viewConfigDiff"]).Count.Should().Be(1, because: "incoming entry should appear when current has no viewConfigDiff key");
		((JArray)result["viewModelConfigDiff"]).Count.Should().Be(1, because: "incoming entry should appear when current has no viewModelConfigDiff key");
	}

	[Test]
	[Description("PageBodyMerger mobile: invalid incoming JSON throws InvalidOperationException")]
	public void PageBodyMerger_Mobile_Should_Throw_On_Invalid_Incoming_Json() {
		string currentBody = "{\"viewConfigDiff\":[]}";
		string incomingBody = "{not valid json";

		Action act = () => PageBodyMerger.Merge(currentBody, incomingBody);

		act.Should().Throw<InvalidOperationException>(because: "invalid incoming JSON must be rejected with a descriptive error");
	}

	[Test]
	[Description("PageBodyMerger mobile: preserves extra top-level properties that are not merge targets")]
	public void PageBodyMerger_Mobile_Should_Preserve_Extra_Properties() {
		string currentBody = "{\"viewConfigDiff\":[],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[],\"customProp\":\"keep\"}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"A\"}]}";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		JObject result = JObject.Parse(merged);
		result["customProp"]?.ToString().Should().Be("keep", because: "extra properties in the current body must not be discarded");
	}

	[Test]
	[Description("PageBodyMerger mobile: full-config 'viewModelConfig' form on the current body is rejected by append merge")]
	public void PageBodyMerger_Mobile_Should_Throw_When_Current_Uses_Full_ViewModelConfig_Form() {
		string currentBody = "{\"viewModelConfig\":{\"attributes\":{}},\"viewConfigDiff\":[]}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"A\"}]}";

		Action act = () => PageBodyMerger.Merge(currentBody, incomingBody);

		act.Should().Throw<InvalidOperationException>(
				because: "append merge does not support the full 'viewModelConfig' form and must fail loudly instead of silently producing a mixed body")
			.WithMessage("*viewModelConfig*");
	}

	[Test]
	[Description("PageBodyMerger mobile: full-config 'modelConfig' form on the current body is rejected by append merge")]
	public void PageBodyMerger_Mobile_Should_Throw_When_Current_Uses_Full_ModelConfig_Form() {
		string currentBody = "{\"modelConfig\":{\"path\":\"x\"},\"viewConfigDiff\":[]}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"A\"}]}";

		Action act = () => PageBodyMerger.Merge(currentBody, incomingBody);

		act.Should().Throw<InvalidOperationException>(
				because: "append merge does not support the full 'modelConfig' form and must fail loudly instead of silently producing a mixed body")
			.WithMessage("*modelConfig*");
	}

	[Test]
	[Description("PageBodyMerger mobile: merged body is indented JSON, not a single minified line")]
	public void PageBodyMerger_Mobile_Should_Return_Indented_Json() {
		string currentBody = "{\"viewConfigDiff\":[{\"operation\":\"merge\",\"name\":\"Existing\",\"values\":{\"size\":\"large\"}}],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"NewButton\",\"values\":{\"type\":\"crt.Button\"}}],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("\n",
			because: "the merged mobile body must be indented JSON so that subsequent get-page writes produce a readable body.js");
		merged.Should().NotBe(merged.ReplaceLineEndings("").Replace(" ", ""),
			because: "a minified single-line output would make the saved body.js unreadable in source control");
		JObject.Parse(merged)["viewConfigDiff"].Should().NotBeNull(
			because: "the output must still be valid JSON regardless of formatting");
	}

	[Test]
	[Description("PageBodyMerger web: JSON sections inside marker pairs are written as indented JSON, not minified single lines")]
	public void PageBodyMerger_Web_Should_Write_Indented_Json_Inside_Marker_Sections() {
		string currentBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"NewButton\",\"values\":{\"type\":\"crt.Button\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"VM1\"}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"M1\"}]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";

		string merged = PageBodyMerger.Merge(currentBody, incomingBody);

		merged.Should().Contain("/**SCHEMA_VIEW_CONFIG_DIFF*/",
			because: "the marker envelope must be preserved verbatim");
		merged.Should().NotContain("[{\"operation\":\"insert\",\"name\":\"NewButton\"",
			because: "the merged JSON section must be indented, not written as a single minified line");
		merged.Should().Contain("\"name\": \"NewButton\"",
			because: "Newtonsoft's Indented formatting inserts a space after the colon — its presence proves the section was not serialized with Formatting.None");
		merged.Should().Contain("\"name\": \"VM1\"",
			because: "viewModelConfigDiff content inside its marker pair must also be indented");
		merged.Should().Contain("\"name\": \"M1\"",
			because: "modelConfigDiff content inside its marker pair must also be indented");
	}

	[Test]
	[Description("PageBodyMerger web: full-form 'SCHEMA_VIEW_MODEL_CONFIG' marker on the current body is rejected by append merge")]
	public void PageBodyMerger_Web_Should_Throw_When_Current_Uses_Full_ViewModelConfig_Marker() {
		string currentBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/ " +
			"/**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"A\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";

		Action act = () => PageBodyMerger.Merge(currentBody, incomingBody);

		act.Should().Throw<InvalidOperationException>(
				because: "append merge against a form-page body (full SCHEMA_VIEW_MODEL_CONFIG marker) would silently drop the incoming diff and must fail loudly instead")
			.WithMessage("*SCHEMA_VIEW_MODEL_CONFIG*");
	}

	[Test]
	[Description("PageBodyMerger web: full-form 'SCHEMA_MODEL_CONFIG' marker on the current body is rejected by append merge")]
	public void PageBodyMerger_Web_Should_Throw_When_Current_Uses_Full_ModelConfig_Marker() {
		string currentBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"A\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";

		Action act = () => PageBodyMerger.Merge(currentBody, incomingBody);

		act.Should().Throw<InvalidOperationException>(
				because: "append merge against a body with the full SCHEMA_MODEL_CONFIG marker would silently drop the incoming SCHEMA_MODEL_CONFIG_DIFF and must fail loudly instead")
			.WithMessage("*SCHEMA_MODEL_CONFIG*");
	}

	[Test]
	[Description("UsesUnsupportedFullConfigForm: a web body with the full SCHEMA_VIEW_MODEL_CONFIG marker is flagged with the web message (ENG-93090)")]
	public void UsesUnsupportedFullConfigForm_ShouldReturnTrueWithWebMessage_WhenWebBodyUsesFullViewModelConfig() {
		// Arrange
		string body = "define(\"UsrX_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
			"modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/ }; });";

		// Act
		bool result = PageBodyMerger.UsesUnsupportedFullConfigForm(body, out string message);

		// Assert
		result.Should().BeTrue(because: "a web body carrying the full SCHEMA_VIEW_MODEL_CONFIG marker cannot be append-merged");
		message.Should().Contain("SCHEMA_VIEW_MODEL_CONFIG", because: "the web message must name the unsupported marker so the caller can act");
		message.Should().Contain("replace", because: "the message must point the caller at the working alternative (replace mode)");
	}

	[Test]
	[Description("UsesUnsupportedFullConfigForm: a web body with only the *_DIFF markers is not flagged (ENG-93090)")]
	public void UsesUnsupportedFullConfigForm_ShouldReturnFalse_WhenWebBodyIsDiffFormOnly() {
		// Arrange
		string body = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/";

		// Act
		bool result = PageBodyMerger.UsesUnsupportedFullConfigForm(body, out string message);

		// Assert
		result.Should().BeFalse(because: "a diff-form body is exactly what append merge supports and must never be flagged");
		message.Should().BeNull(because: "no message is produced when the body is acceptable");
	}

	[Test]
	[Description("UsesUnsupportedFullConfigForm: a mobile body with the full viewModelConfig object is flagged with the mobile message (ENG-93090)")]
	public void UsesUnsupportedFullConfigForm_ShouldReturnTrueWithMobileMessage_WhenMobileBodyUsesFullViewModelConfig() {
		// Arrange
		string body = "{\"viewModelConfig\":{\"attributes\":{}},\"viewConfigDiff\":[]}";

		// Act
		bool result = PageBodyMerger.UsesUnsupportedFullConfigForm(body, out string message);

		// Assert
		result.Should().BeTrue(because: "a mobile body carrying the full viewModelConfig object cannot be append-merged");
		message.Should().Contain("viewModelConfig", because: "the mobile message must name the unsupported key so the caller can act");
		message.Should().Contain("replace", because: "the message must point the caller at the working alternative (replace mode)");
	}

	[Test]
	[Description("UsesUnsupportedFullConfigForm: a mobile body with only *Diff arrays is not flagged (ENG-93090)")]
	public void UsesUnsupportedFullConfigForm_ShouldReturnFalse_WhenMobileBodyIsDiffFormOnly() {
		// Arrange
		string body = "{\"viewConfigDiff\":[],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}";

		// Act
		bool result = PageBodyMerger.UsesUnsupportedFullConfigForm(body, out string message);

		// Assert
		result.Should().BeFalse(because: "a diff-form mobile body is supported by append merge and must not be flagged");
		message.Should().BeNull(because: "no message is produced when the body is acceptable");
	}

	[Test]
	[Description("UsesUnsupportedFullConfigForm: null/blank and unparseable-mobile bodies fail open so the merge surfaces the precise error (ENG-93090)")]
	public void UsesUnsupportedFullConfigForm_ShouldFailOpen_WhenBodyIsBlankOrUnparseable() {
		// Act
		bool blank = PageBodyMerger.UsesUnsupportedFullConfigForm("   ", out string blankMessage);
		bool badJson = PageBodyMerger.UsesUnsupportedFullConfigForm("{ not valid json", out string badJsonMessage);

		// Assert
		blank.Should().BeFalse(because: "a blank body is not our concern here — the empty-body check owns that error");
		blankMessage.Should().BeNull(because: "no message is produced when the guard fails open");
		badJson.Should().BeFalse(because: "an unparseable mobile body must fail open so the merge/JSON validators surface the precise parse error");
		badJsonMessage.Should().BeNull(because: "no message is produced when the guard fails open");
	}

	[Test]
	[Description("update-page append: a full-config incoming body is rejected up-front with an actionable hint and no server round-trip (ENG-93090)")]
	public async System.Threading.Tasks.Task UpdatePage_ShouldRejectAppendUpFront_WhenIncomingBodyIsFullConfigForm() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog,
			Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string fullConfigBody = "define(\"UsrX_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
			"modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageUpdateArgs args = new("UsrX_FormPage", fullConfigBody, null, false, "dev", null, null, null, SkipSampling: true, Mode: "append");

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeFalse(because: "append cannot merge a full-config body and must fail rather than silently drop the change");
		response.Error.Should().StartWith(PageUpdateTool.AppendFullConfigRejectionPrefix, because: "the up-front guard message must begin with the shared rejection-prefix constant, not a duplicated string literal (ENG-93090 RC-5)");
		response.Error.Should().Contain("replace", because: "the hint must route the caller to replace mode as the corrective action");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	private static IPageDesignerHierarchyClient CreateHierarchyClientFor(string schemaUId, string packageUId = "test-pkg-uid") {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(schemaUId).Returns(packageUId);
		hierarchyClient.GetParentSchemas(schemaUId, packageUId).Returns([
			new PageDesignerHierarchySchema { UId = schemaUId, PackageUId = packageUId }
		]);
		return hierarchyClient;
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage accepts a valid mobile JSON body (plain JSON starting with '{'), skips AMD validation, AND reaches the save path so the schema lookup is attempted — proves the mobile bypass is not silently short-circuited at any upstream gate (a regression that swallowed the mobile body before TryUpdatePage would now fail the SelectQuery assertion below, where the old NOT-CONTAIN form would still pass on a null Error).")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_Accepts_Valid_Mobile_Json_Body() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(System.Text.Json.JsonSerializer.Serialize(new { success = true }));
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string mobileBody = """
			{
			  "viewConfigDiff": [],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";
		PageUpdateArgs args = new("UsrMobile_FormPage", mobileBody, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert — the mobile body must reach the save path. The mock here
		// intentionally does NOT wire the full SelectQuery / SaveSchema chain
		// (that's exercised by other tests), so the response surfaces a
		// downstream "Schema 'UsrMobile_FormPage' not found" once schema
		// lookup runs. Asserting on that downstream error string is the
		// strongest available "no upstream short-circuit happened" signal in
		// this fixture: any regression that swallowed the mobile body before
		// TryUpdatePage would surface a different error (or none at all) and
		// the test would fail. The old NOT-CONTAIN form passed on a null
		// Error too, which is exactly why the reviewer flagged it as
		// structurally unable to fail.
		response.Error.Should().Contain("not found",
			because: "TryUpdatePage must reach the SelectQuery step and surface the lookup failure from the deliberately-thin mock — proves the mobile body got past every upstream validation gate");
		response.Error.Should().NotContain("AMD",
			because: "mobile JSON bodies must NOT trigger AMD marker validation");
		response.Error.Should().NotContain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "AMD marker validation errors must NOT appear for mobile bodies");
		response.Error.Should().NotContain("Mobile page validation failed",
			because: "the body is well-formed mobile JSON; the mobile validator must accept it");
	}

	[Test]
	[Description("AC4 positive: a valid web body that passes the deterministic syntax + lint pre-pass MUST invoke the LLM sampling service via the injected IPageBodySamplingService seam. The downstream save path is exercised by other tests — this one focuses only on the sampling-call observability.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_Should_Invoke_Sampling_For_Valid_Body() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		IPageBodySamplingService samplingService = Substitute.For<IPageBodySamplingService>();
		samplingService
			.TrySamplingReviewAsync(Arg.Any<ModelContextProtocol.Server.McpServer>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
			.Returns((PageSamplingReview)null);
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, samplingService, new PageBaselineGuard(new MockFileSystem()));
		string body = CreatePageBody();
		PageUpdateArgs args = new("UsrValid_FormPage", body, "{\"caption\":\"Hello\"}", null, null, null, null, null);

		// Act
		_ = tool.UpdatePage(args, null).Result;

		// Assert — focus on AC4 only. The body's deterministic gates passed,
		// so sampling MUST be invoked with the exact schemaName / body /
		// resources triple the caller submitted. Whether the downstream
		// TryUpdatePage call eventually persists or fails is exercised by
		// other PageUpdateTool save-path tests in this file.
		samplingService.Received(1).TrySamplingReviewAsync(
			Arg.Any<ModelContextProtocol.Server.McpServer>(),
			Arg.Is<string>(n => n == "UsrValid_FormPage"),
			Arg.Is<string>(b => b == body),
			Arg.Is<string>(r => r == "{\"caption\":\"Hello\"}"),
			Arg.Any<System.Threading.CancellationToken>());
	}

	[Test]
	[Description("AC4 negative: when the body fails the deterministic syntax gate, sampling is NOT invoked — proves the gate short-circuits BEFORE LLM tokens are spent on a doomed body.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_Should_NotInvoke_Sampling_When_Syntax_Fails() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		IPageBodySamplingService samplingService = Substitute.For<IPageBodySamplingService>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, samplingService, new PageBaselineGuard(new MockFileSystem()));
		PageUpdateArgs args = new("UsrBad_FormPage", "define('BadPage', {})}", null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "the syntax gate must reject the body");
		samplingService.DidNotReceive().TrySamplingReviewAsync(
			Arg.Any<ModelContextProtocol.Server.McpServer>(),
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<System.Threading.CancellationToken>());
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage rejects the call when neither 'body' nor 'body-file' is provided.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_Rejects_When_Body_And_BodyFile_Both_Missing() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageUpdateArgs args = new("UsrTest_FormPage", null, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(because: "the tool must fail fast when no body content is supplied");
		response.Error.Should().Contain("body-file",
			because: "the error must explicitly mention both 'body' and 'body-file' so the caller can pick either input");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "no save attempt may be made when there is no body to send");
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage loads body from BodyFile and runs validation against the resolved content (catches malformed JSON markers loaded from disk).")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_BodyFile_Triggers_Validation_On_Resolved_Content() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithBadJson = CreatePageBody(viewConfigDiff: "[{ bad json }]");
		string tempFile = Path.Combine(Path.GetTempPath(), $"clio-bodyfile-{Path.GetRandomFileName()}.js");
		File.WriteAllText(tempFile, bodyWithBadJson);
		try {
			PageUpdateArgs args = new("UsrTest_FormPage", null, null, null, null, null, null, null, BodyFile: tempFile);

			// Act
			PageUpdateResponse response = tool.UpdatePage(args, null).Result;

			// Assert
			response.Success.Should().BeFalse(
				because: "validation must run against the body loaded from BodyFile, not be skipped because inline body is empty");
			response.Error.Should().Contain("Invalid JSON in SCHEMA_VIEW_CONFIG_DIFF",
				because: "malformed JSON inside an otherwise-intact SCHEMA_* marker is now reported against the specific marker (the marker/content validator is preferred over the generic ENG-89796 syntax message when marker integrity holds) so the caller knows exactly which section to fix");
			applicationClient.ReceivedCalls().Should().BeEmpty(
				because: "no save attempt may be made when validation fails");
		}
		finally {
			if (File.Exists(tempFile)) {
				File.Delete(tempFile);
			}
		}
	}

	[Test]
	[Description("PageUpdateTool.UpdatePage returns a descriptive error when BodyFile points to a missing file.")]
	[Category("Unit")]
	public void PageUpdateTool_UpdatePage_Rejects_When_BodyFile_Missing() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageUpdateTool tool = new(command, logger, commandResolver, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string missingPath = Path.Combine(Path.GetTempPath(), $"clio-missing-{Path.GetRandomFileName()}.js");
		PageUpdateArgs args = new("UsrTest_FormPage", null, null, null, null, null, null, null, BodyFile: missingPath);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args, null).Result;

		// Assert
		response.Success.Should().BeFalse(because: "a missing BodyFile must produce a load failure before any save attempt");
		response.Error.Should().Contain(missingPath, because: "the error must identify the missing file path so the caller can fix the input");
		applicationClient.ReceivedCalls().Should().BeEmpty(because: "no save attempt may be made when the body cannot be loaded");
	}

	[Test]
	[Description("PageBodyMerger: a full-config INCOMING body (against a diff-form current body) is rejected by the shared Merge path so the CLI verb cannot silently drop its full-config content (ENG-93090 RC-1)")]
	[Category("Unit")]
	public void PageBodyMerger_Should_Throw_When_Incoming_Body_Uses_Full_Config_Form() {
		// Arrange — current body is diff-form (a merge would otherwise proceed); the INCOMING fragment
		// carries a real viewConfigDiff insert AND the full-config SCHEMA_VIEW_MODEL_CONFIG marker.
		string currentBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/ " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/";
		string incomingBody = "/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"A\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/ " +
			"/**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/";

		// Act
		Action act = () => PageBodyMerger.Merge(currentBody, incomingBody);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the shared merge path must reject a full-config incoming body on BOTH the CLI and MCP surfaces instead of silently dropping its full-config content (ENG-93090 RC-1)")
			.WithMessage("*SCHEMA_VIEW_MODEL_CONFIG*");
	}

	[Test]
	[Description("UsesUnsupportedFullConfigForm: a mobile full-config key present as a non-object (array) value is still flagged so it cannot slip past detection and be silently dropped (ENG-93090 RC-8)")]
	[Category("Unit")]
	public void UsesUnsupportedFullConfigForm_ShouldReturnTrue_WhenMobileFullConfigKeyIsNonObject() {
		// Arrange — viewModelConfig is present but as an array rather than an object.
		string body = "{\"viewModelConfig\":[],\"viewConfigDiff\":[]}";

		// Act
		bool result = PageBodyMerger.UsesUnsupportedFullConfigForm(body, out string message);

		// Assert
		result.Should().BeTrue(because: "a present-but-non-object full-config key must be flagged, not slip past the JObject-only check and get dropped by the merge (ENG-93090 RC-8)");
		message.Should().Contain("viewModelConfig", because: "the mobile message must name the unsupported key so the caller can act");
	}

	[Test]
	[Description("PageBodyMerger mobile: a CURRENT-body full-config key present as a non-object is now rejected via the shared predicate, closing the mixed-form gap on the current body too (ENG-93090 RC-9)")]
	[Category("Unit")]
	public void PageBodyMerger_Mobile_Should_Throw_When_Current_FullConfig_Key_Is_NonObject() {
		// Arrange — the CURRENT server body carries `viewModelConfig` as an array (present, non-object);
		// the incoming fragment is clean diff form.
		string currentBody = "{\"viewModelConfig\":[],\"viewConfigDiff\":[]}";
		string incomingBody = "{\"viewConfigDiff\":[{\"operation\":\"insert\",\"name\":\"A\"}]}";

		// Act
		Action act = () => PageBodyMerger.Merge(currentBody, incomingBody);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the shared current-body predicate must reject a present-but-non-object full-config key instead of merging into a mixed full-config/*Diff body (ENG-93090 RC-9)")
			.WithMessage("*viewModelConfig*");
	}

	[Test]
	[Description("update-page replace: a full-config body is NOT tripped by the append guard — replace saves it verbatim (ENG-93090 RC-6)")]
	[Category("Unit")]
	public async System.Threading.Tasks.Task UpdatePage_ShouldNotRejectFullConfigBody_WhenModeIsReplace() {
		// Arrange
		PageUpdateTool tool = BuildAppendGuardTool();
		PageUpdateArgs args = new("UsrX_FormPage", FullConfigWebBody, null, false, "dev", null, null, null, SkipSampling: true, Mode: "replace");

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		(response.Error ?? string.Empty).Should().NotStartWith(PageUpdateTool.AppendFullConfigRejectionPrefix,
			because: "replace mode saves a full-config body verbatim and must never trip the append/full-config guard (ENG-93090 RC-6)");
	}

	[Test]
	[Description("update-page default mode (null resolves to replace): a full-config body is NOT tripped by the append guard (ENG-93090 RC-6)")]
	[Category("Unit")]
	public async System.Threading.Tasks.Task UpdatePage_ShouldNotRejectFullConfigBody_WhenModeIsDefault() {
		// Arrange
		PageUpdateTool tool = BuildAppendGuardTool();
		PageUpdateArgs args = new("UsrX_FormPage", FullConfigWebBody, null, false, "dev", null, null, null, SkipSampling: true, Mode: null);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		(response.Error ?? string.Empty).Should().NotStartWith(PageUpdateTool.AppendFullConfigRejectionPrefix,
			because: "the default (replace) path must not trip the append/full-config guard so a regression of the Mode gate cannot break the common path (ENG-93090 RC-6)");
	}

	[Test]
	[Description("update-page append: the full-config guard matches mode case-insensitively ('Append') (ENG-93090 RC-7)")]
	[Category("Unit")]
	public async System.Threading.Tasks.Task UpdatePage_ShouldRejectAppendUpFront_WhenModeCasingIsMixed() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		PageUpdateTool tool = BuildAppendGuardTool(applicationClient);
		PageUpdateArgs args = new("UsrX_FormPage", FullConfigWebBody, null, false, "dev", null, null, null, SkipSampling: true, Mode: "Append");

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeFalse(because: "the Mode gate is OrdinalIgnoreCase, so 'Append' must be guarded exactly like 'append' (ENG-93090 RC-7)");
		response.Error.Should().StartWith(PageUpdateTool.AppendFullConfigRejectionPrefix, because: "the mixed-case append must produce the same up-front rejection");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("update-page append: a full-config body loaded from --body-file is rejected up-front (proves load-then-guard ordering) (ENG-93090 RC-7)")]
	[Category("Unit")]
	public void UpdatePage_ShouldRejectAppend_WhenFullConfigLoadedFromBodyFile() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		PageUpdateTool tool = BuildAppendGuardTool(applicationClient);
		string tempFile = Path.Combine(Path.GetTempPath(), $"clio-fullconfig-{Path.GetRandomFileName()}.js");
		File.WriteAllText(tempFile, FullConfigWebBody);
		try {
			PageUpdateArgs args = new("UsrX_FormPage", null, null, false, "dev", null, null, null, SkipSampling: true, Mode: "append", BodyFile: tempFile);

			// Act
			PageUpdateResponse response = tool.UpdatePage(args, null).Result;

			// Assert
			response.Success.Should().BeFalse(because: "a full-config body loaded from disk must be rejected once resolved, before any save (ENG-93090 RC-7)");
			response.Error.Should().StartWith(PageUpdateTool.AppendFullConfigRejectionPrefix, because: "the guard runs after the body is loaded from --body-file, so the body-file path is guarded identically to inline --body");
			applicationClient.ReceivedCalls().Should().BeEmpty(because: "no save attempt may be made once the append/full-config guard rejects the body");
		}
		finally {
			if (File.Exists(tempFile)) {
				File.Delete(tempFile);
			}
		}
	}

	[Test]
	[Description("update-page append: a body that is BOTH full-config form AND syntactically invalid yields the append/full-config prefix — the guard runs before the JS-syntax gate, locking the RC-12 ordering (ENG-93090 RC-16)")]
	[Category("Unit")]
	public async System.Threading.Tasks.Task UpdatePage_ShouldPreferAppendRejection_OverSyntaxError_WhenBodyIsFullConfigAndMalformed() {
		// Arrange — the full-config SCHEMA_VIEW_MODEL_CONFIG marker pair is present (regex-detected by the
		// guard), but the surrounding JavaScript is deliberately broken so the syntax gate would also fire.
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		PageUpdateTool tool = BuildAppendGuardTool(applicationClient);
		string fullConfigButMalformed =
			"define(\"UsrX_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/, @@@ not valid javascript (((";
		PageUpdateArgs args = new("UsrX_FormPage", fullConfigButMalformed, null, false, "dev", null, null, null, SkipSampling: true, Mode: "append");

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeFalse(because: "a full-config append body must be rejected");
		response.Error.Should().StartWith(PageUpdateTool.AppendFullConfigRejectionPrefix,
			because: "the append/full-config guard runs before the JS-syntax gate, so the actionable form-mismatch hint wins over a generic syntax error and a future reorder would fail this test (ENG-93090 RC-12/RC-16)");
		applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	// Shared full-config web body for the append-guard tests (ENG-93090): valid AMD JS carrying the
	// non-diff SCHEMA_VIEW_MODEL_CONFIG / SCHEMA_MODEL_CONFIG markers that append merge cannot process.
	private const string FullConfigWebBody =
		"define(\"UsrX_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
		"modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	// Builds a PageUpdateTool wired with substitutes for the append-guard tests. The guard runs before
	// any command resolution or network call, so the substitutes only need to exist, not behave.
	private static PageUpdateTool BuildAppendGuardTool(IApplicationClient applicationClient = null) {
		applicationClient ??= Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		return new PageUpdateTool(command, logger, commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(), Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
	}

}
