using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the update-page MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(PageUpdateTool.ToolName)]
[NonParallelizable]
public sealed class PageUpdateToolE2ETests : McpContractFixtureBase {
	private const string ToolName = PageUpdateTool.ToolName;
	private const string MinimalMarkerPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("Exposes update-page via the get-tool-contract compact index so callers can discover it directly on the lazy tool surface.")]
	[AllureTag(ToolName)]
	[AllureName("update-page tool is discoverable on the lazy surface")]
	[AllureDescription("Verifies that update-page is discoverable via the get-tool-contract compact index of the MCP server.")]
	public async Task PageUpdateTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list, so MCP callers can discover the single-page save tool");
	}

	[Test]
	[Description("update-page fails fast at the JavaScript-syntax gate before any remote call when the body contains an `await X = Y` (the actual production incident body), and the structured response carries the {line, column, message} per the AC.")]
	[AllureTag(ToolName)]
	[AllureName("update-page fails fast on JavaScript syntax error before any remote call")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page with the incident body (`await request.$context.X = Y`), and verifies that the response carries success=false, a 'JavaScript syntax error at line N, column M' message, and the 'NOT sent to Creatio' assurance — the deterministic floor of the validator must surface through the real MCP transport before any remote call is even attempted.")]
	public async Task PageUpdateTool_Should_FailFast_When_Body_Has_JavaScript_Syntax_Error() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		// `await` cannot be an assignment target; no environment-name because the
		// syntax gate must short-circuit before any environment resolution.
		string nonValidBody =
			"define(\"Bad_FormPage\", [], function() {\n" +
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

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrSyntaxIncident_FormPage",
					["body"] = nonValidBody,
					["dry-run"] = true
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the syntax failure is a structured tool response, not an MCP transport error");
		response.Success.Should().BeFalse(
			because: "the incident body must be rejected end-to-end via the real MCP transport — the unit test alone is not enough per AGENTS.md MCP e2e rule");
		response.Error.Should().Contain("JavaScript syntax error",
			because: "the agent-facing error must name the actual class of problem (parser rejection) so the caller does not chase a phantom environment / marker / sampling failure");
		response.Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the broken body did not reach the server without inspecting logs, even when the failure surfaces through the MCP wire");
	}

	[Test]
	[Description("update-page fails fast on the up-front append/full-config guard (ENG-93090): mode=append with a full-config body (SCHEMA_VIEW_MODEL_CONFIG) is rejected with an actionable hint before any remote call, so the agent does not burn a fetch+merge round-trip discovering the incompatibility server-side.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects append of a full-config body up-front before any remote call")]
	[AllureDescription("Starts the real clio MCP server and invokes update-page in mode=append with a full-config web body (the non-diff SCHEMA_VIEW_MODEL_CONFIG / SCHEMA_MODEL_CONFIG markers). Verifies the structured response carries success=false and the corrective 'Append merge cannot use this body … replace' hint end-to-end via the real MCP transport, with no environment-name supplied so the guard must short-circuit before any environment resolution or merge attempt.")]
	public async Task PageUpdateTool_Should_FailFast_When_Append_Body_Is_FullConfigForm() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		// Full-config (static) FormPage body: carries SCHEMA_VIEW_MODEL_CONFIG / SCHEMA_MODEL_CONFIG
		// (no *_DIFF). Append cannot merge this shape; the up-front guard must reject it offline.
		// No environment-name — the guard must short-circuit before any environment resolution.
		string fullConfigBody =
			"define(\"FullConfig_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
			"modelConfig: /**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrAppendFullConfig_FormPage",
					["body"] = fullConfigBody,
					["mode"] = "append"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the append/full-config rejection is a structured tool response, not an MCP transport error");
		response.Success.Should().BeFalse(
			because: "append cannot merge a full-config body; the up-front guard must reject it end-to-end via the real MCP transport per AGENTS.md MCP e2e rule");
		response.Error.Should().Contain("Append merge cannot use this body",
			because: "the agent-facing error must name the append/full-config problem so the caller does not chase a phantom environment / syntax failure");
		response.Error.Should().Contain("replace",
			because: "the corrective hint must route the caller to replace mode, the working alternative for a full-config body");
	}

	[Test]
	[Description("update-page fails fast at the AST lint gate when a custom converter uses the reserved `crt.*` prefix — the lint rule `converter-crt-prefix-reserved` is unique to the AST pass (the regex layer treats `crt.*` as a valid vendor prefix), so this body is what proves the lint pass surfaces through the real MCP transport.")]
	[AllureTag(ToolName)]
	[AllureName("update-page fails fast on converter-crt-prefix-reserved lint error before any remote call")]
	[AllureDescription("Starts the real clio MCP server and submits a body whose `converters` section registers a custom converter under the reserved `crt.*` namespace. The existing regex validators accept the body (their shape checks explicitly skip `crt.*` keys), so verifying the structured response carries `Page body lint failed` proves the AST lint pass adds detection beyond the regex layer end-to-end via the real MCP wire.")]
	public async Task PageUpdateTool_Should_FailFast_When_Custom_Converter_Uses_Crt_Prefix() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		// Body uses the reserved `crt.*` namespace for a custom converter
		// — only the AST lint pass catches this; the regex layer treats
		// `crt.*` as a valid vendor prefix and the converter shape checks
		// explicitly skip `crt.*` keys (SchemaValidationService.cs:1899-1901).
		string crtPrefixConverterBody =
			"define(\"Bad_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{ \"crt.MyConverter\": function(v) { return v; } }/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrLintCrtConverter_FormPage",
					["body"] = crtPrefixConverterBody,
					["dry-run"] = true
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the lint failure is a structured tool response, not an MCP transport error");
		response.Success.Should().BeFalse(
			because: "the reserved `crt.*` namespace is for Creatio built-in converters; the lint gate must catch the custom-name usage end-to-end via the real MCP transport per AGENTS.md MCP rule");
		response.Error.Should().Contain("Page body lint failed",
			because: "the canonical lint error prefix is the contract surface the agent keys on to distinguish lint rejection from syntax / sampling rejection");
		response.Error.Should().Contain("converter-crt-prefix-reserved",
			because: "the rule id must be visible in the wire response so the agent can map the failure back to the guidance doc that describes the anti-pattern");
		response.Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the body did not reach the server without inspecting logs, mirroring the syntax-gate tail");
	}

	[Test]
	[Description("Reports readable failures when update-page is called with an invalid environment name.")]
	[AllureTag(ToolName)]
	[AllureName("update-page reports invalid environment failures")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page with an unknown environment name in dry-run mode, and verifies that the failure remains human-readable.")]
	public async Task PageUpdateTool_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-update-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMissing_FormPage",
					["body"] = MinimalMarkerPageBody,
					["dry-run"] = true,
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured update-page failures should stay inside the tool response envelope");
		response.Success.Should().BeFalse(
			because: "update-page should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Rejects malformed resources JSON through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects malformed resources JSON")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with malformed resources JSON against any reachable environment, and verifies that the tool returns a structured validation error.")]
	public async Task PageUpdateTool_Should_Reject_Invalid_Resources_Json() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrValidationOnly_FormPage",
					["body"] = MinimalMarkerPageBody,
					["dry-run"] = true,
					["environment-name"] = environmentName,
					["resources"] = "{\"UsrTitle\":"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "malformed resources payloads should be surfaced as structured validation failures");
		response.Success.Should().BeFalse(
			because: "update-page should reject malformed resources JSON before save or dry-run validation continues");
		response.Error.Should().Be("resources must be a valid JSON object string",
			because: "the failure should explain how the resources payload must be formatted");
	}

	[Test]
	[Description("Rejects a run-process button that omits processName through update-page before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects run-process button without processName")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a crt.RunBusinessProcessRequest button missing processName, and verifies a structured validation failure that names the button and processName.")]
	public async Task PageUpdateTool_Should_Reject_RunProcess_Button_Without_ProcessName() {
		// Arrange
		string invalidEnvironmentName = $"missing-runproc-env-{Guid.NewGuid():N}";
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
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrRunProcessValidation_FormPage",
					["body"] = runProcessBody,
					["dry-run"] = true,
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a missing processName should be surfaced as a structured validation failure");
		response.Success.Should().BeFalse(
			because: "update-page must reject a run-process button without processName before any remote call");
		response.Error.Should().Contain("processName",
			because: "the failure must point at the missing processName");
		response.Error.Should().Contain("RunBpButton",
			because: "the failure should name the offending button");
	}

	[Test]
	[Description("Rejects field bindings to undeclared attributes through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects undeclared field bindings in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a field control bound to an undeclared attribute, and verifies that the tool returns a structured validation error.")]
	public async Task PageUpdateTool_Should_Reject_Undeclared_Field_Bindings_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string proxyBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatusField\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"values\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrProxyBinding_FormPage",
					["body"] = proxyBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "semantic page-validation failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "update-page should fail fast when a field control points to an undeclared attribute");
		response.Error.Should().Contain("invalid form field bindings")
			.And.Contain("UsrStatusField")
			.And.Contain("undeclared attribute",
				because: "the failure should explain that the control binding is missing from viewModelConfig");
	}

	[Test]
	[Description("Rejects stale control bindings through update-page dry-run when handlers populate a different declared attribute.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects divergent handler-driven control bindings in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with handlers writing one declared attribute while the control stays bound to another declared attribute for the same field, and verifies that the tool returns a structured validation error.")]
	public async Task PageUpdateTool_Should_Reject_HandlerDriven_Divergent_Bindings_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string handlerDrivenBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"label\":\"$Resources.Strings.UsrName\",\"control\":\"$UsrNameField\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}},\"UsrNameField\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"}}}}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { const result = await next?.handle(request); await request.$context.set(\"UsrName\", \"Primary currency\"); return result; } }]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrHandlerDrivenBinding_FormPage",
					["body"] = handlerDrivenBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "semantic page-validation failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "update-page should fail fast when handlers and controls diverge onto different declared attributes for the same field");
		response.Error.Should().Contain("invalid form field bindings")
			.And.Contain("$UsrNameField")
			.And.Contain("$UsrName")
			.And.Contain("$context.set",
				because: "the failure should explain that the handler and the control must use the same declared attribute");
	}

	[Test]
	[Description("Rejects an insert of a new field control whose binding attribute is not declared in viewModelConfigDiff and whose label resource is neither registered in 'resources' nor auto-provided.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects inserted field without matching binding or resource in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a viewConfigDiff insert that omits the matching viewModelConfigDiff attribute declaration and any resources payload, and verifies that the tool returns a structured validation error naming the field, the missing attribute, and the section that needs the edit.")]
	public async Task PageUpdateTool_Should_Reject_Inserted_Field_Without_Binding_Or_Resource_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string bareInsertBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
			"{\"operation\":\"insert\",\"name\":\"UsrCompleted\",\"values\":{\"type\":\"crt.Checkbox\"," +
			"\"label\":\"$Resources.Strings.PDS_UsrCompleted\",\"control\":\"$PDS_UsrCompleted\"}}" +
			"]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrInsertedField_FormPage",
					["body"] = bareInsertBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the inserted-field self-consistency check should surface as a structured update-page response, not a protocol-level error");
		response.Success.Should().BeFalse(
			because: "update-page must reject inserts that would render with no data source and a blank label");
		response.Error.Should().Contain("inserted field controls")
			.And.Contain("UsrCompleted")
			.And.Contain("PDS_UsrCompleted")
			.And.Contain("viewModelConfigDiff",
				because: "the failure must name the offending field, the missing attribute, and the section that needs the edit so the agent can fix the payload in one pass");
	}

	[Test]
	[Description("Rejects a body that sets a user-visible text property (placeholder) to an inline string literal instead of a localizable-string binding, before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects inline placeholder literal in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a viewConfigDiff insert whose placeholder is a hardcoded string, and verifies that the tool returns a structured validation error naming the node, the placeholder property, and the page-schema-resources guide.")]
	public async Task PageUpdateTool_Should_Reject_Inline_Placeholder_Literal_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string inlinePlaceholderBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
			"{\"operation\":\"insert\",\"name\":\"EmailField\",\"values\":{\"type\":\"crt.Input\"," +
			"\"control\":\"$Email\",\"placeholder\":\"name@firm.com\"}}" +
			"]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrInlinePlaceholder_FormPage",
					["body"] = inlinePlaceholderBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the localizable-text check should surface as a structured update-page response, not a protocol-level error");
		response.Success.Should().BeFalse(
			because: "update-page must reject a hardcoded placeholder that cannot be translated");
		response.Error.Should().Contain("EmailField")
			.And.Contain("placeholder")
			.And.Contain("page-schema-resources",
				because: "the failure must name the node, the offending property, and point to the localization guide so the agent can fix the payload in one pass");
	}

	[Test]
	[Description("Rejects invalid handler section shape through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects non-array handlers in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with SCHEMA_HANDLERS authored as an object instead of an array, and verifies that the tool returns a structured validation error.")]
	public async Task PageUpdateTool_Should_Reject_NonArray_Handlers_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidHandlersBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { await next?.handle(request); } }/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrInvalidHandlers_FormPage",
					["body"] = invalidHandlersBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "handler validation failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "update-page should reject schemas where SCHEMA_HANDLERS is no longer an array literal");
		response.Error.Should().Contain("SCHEMA_HANDLERS")
			.And.Contain("array literal",
				because: "the failure should explain that the handlers section must stay an array");
	}

	[Test]
	[Description("Rejects a custom validator that uses 'validate' alias instead of the canonical 'validator' factory key through update-page dry-run.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects validator with 'validate' key alias in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with SCHEMA_VALIDATORS using the misleading 'validate' key alias, and verifies that the tool returns a structured factory-shape validation error pointing at page-schema-validators guidance.")]
	public async Task PageUpdateTool_Should_Reject_Validator_With_Validate_Key_Alias_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string bodyWithValidateAlias = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{ \"usr.PhoneFormatValidator\": { params: [{ \"name\": \"message\" }], async: false, " +
			"validate: function(value, config) { return null; } } }/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrValidatorAlias_FormPage",
					["body"] = bodyWithValidateAlias,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "validator factory-shape failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "the runtime ignores any key other than 'validator', so update-page must reject the schema before save");
		response.Error.Should().Contain("usr.PhoneFormatValidator")
			.And.Contain("'validate'")
			.And.Contain("'validator'")
			.And.Contain("page-schema-validators",
				because: "the error must name the offending validator and the wrong key, and direct the agent at the validator guidance");
	}

	[Test]
	[Description("Rejects a custom converter declared as object literal instead of a callable function value through update-page dry-run.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects converter with object literal value in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with SCHEMA_CONVERTERS containing an object literal in place of a function value, and verifies that the tool returns a structured function-shape validation error pointing at page-schema-converters guidance.")]
	public async Task PageUpdateTool_Should_Reject_Converter_With_Object_Literal_Value_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string bodyWithObjectConverter = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{ \"usr.WrongShape\": { transform: \"upper\" } }/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrConverterShape_FormPage",
					["body"] = bodyWithObjectConverter,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "converter function-shape failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "an object-literal converter silently fails to apply at the binding site and must be rejected before save");
		response.Error.Should().Contain("usr.WrongShape")
			.And.Contain("not callable")
			.And.Contain("page-schema-converters",
				because: "the error must name the offending converter and direct the agent at the converter guidance");
	}

	[Test]
	[Description("Rejects request.viewModel handler APIs through update-page dry-run and returns a handler-guidance recovery hint.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects request.viewModel handler APIs in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with SCHEMA_HANDLERS that use request.viewModel accessors, and verifies that the tool returns a structured validation error with a handler-guidance recovery hint.")]
	public async Task PageUpdateTool_Should_Reject_Request_ViewModel_Handler_Apis_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidHandlerApiBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelAttributeChangeRequest\", handler: async (request, next) => { const current = await request.viewModel.get(\"UsrParkingRequired\"); await request.viewModel.set(\"UsrVehicleNumber\", current ? \"A-01\" : null); return next?.handle(request); } }]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrInvalidHandlerApi_FormPage",
					["body"] = invalidHandlerApiBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "handler API validation failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "update-page should reject handlers that invent request.viewModel accessors");
		response.Error.Should().Contain("request.viewModel")
			.And.Contain("page-schema-handlers")
			.And.Contain("canonical clio handler examples")
			.And.Contain("request.value")
			.And.Contain("request.$context",
				because: "the failure should redirect callers to the clio handler guidance and canonical handler patterns");
	}

	[Test]
	[Description("Rejects a SCHEMA_CONVERTERS entry whose key is missing the required dot separator through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects converter key without dot in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a SCHEMA_CONVERTERS entry whose key has no dot separator, and verifies that the tool returns a structured validation error naming the key and the VendorPrefix requirement.")]
	public async Task PageUpdateTool_Should_Reject_Converter_Key_Without_Dot_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string bodyWithBadConverter = MinimalMarkerPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"UsrBadConverter\": function(value) { return value; } }/**SCHEMA_CONVERTERS*/");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrBadConverter_FormPage",
					["body"] = bodyWithBadConverter,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "converter key validation failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "a converter key without a dot causes a Creatio runtime error and must be rejected before save");
		response.Error.Should().Contain("UsrBadConverter")
			.And.Contain("VendorPrefix",
				because: "the failure should name the offending key and reference the VendorPrefix.Name format requirement");
	}

	[Test]
	[Description("Rejects a SCHEMA_HANDLERS entry whose request value is missing the required dot separator through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects handler request value without dot in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a SCHEMA_HANDLERS array entry whose request value has no dot separator, and verifies that the tool returns a structured validation error naming the value and the VendorPrefix requirement.")]
	public async Task PageUpdateTool_Should_Reject_Handler_Request_Without_Dot_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string bodyWithBadHandler = MinimalMarkerPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"/**SCHEMA_HANDLERS*/[{ request: \"BadHandlerRequest\", " +
			"handler: async (request, next) => { await next?.handle(request); } }]/**SCHEMA_HANDLERS*/");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrBadHandler_FormPage",
					["body"] = bodyWithBadHandler,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "handler request validation failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "a handler request value without a dot causes a Creatio runtime error and must be rejected before save");
		response.Error.Should().Contain("BadHandlerRequest")
			.And.Contain("VendorPrefix")
			.And.Contain("page-schema-handlers",
				because: "the failure should name the offending request value and direct the agent at the handler guidance");
	}

	[Test]
	[Description("Rejects a SCHEMA_VALIDATORS entry whose key is missing the required dot separator through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects validator key without dot in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a SCHEMA_VALIDATORS entry whose key has no dot separator, and verifies that the tool returns a structured validation error naming the key and the VendorPrefix requirement.")]
	public async Task PageUpdateTool_Should_Reject_Validator_Key_Without_Dot_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string bodyWithBadValidator = MinimalMarkerPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"BadValidator\": { params: [] } }/**SCHEMA_VALIDATORS*/");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrBadValidator_FormPage",
					["body"] = bodyWithBadValidator,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "validator key validation failures should be surfaced as structured update-page responses");
		response.Success.Should().BeFalse(
			because: "a validator key without a dot causes a Creatio runtime error and must be rejected before save");
		response.Error.Should().Contain("BadValidator")
			.And.Contain("VendorPrefix")
			.And.Contain("page-schema-validators",
				because: "the failure should name the offending key and direct the agent at the validator guidance");
	}

	[Test]
	[Description("Accepts optional-properties JSON array and verify flag through update-page dry-run without rejecting them as invalid parameters.")]
	[AllureTag(ToolName)]
	[AllureName("update-page accepts optional-properties and verify in dry-run")]
	[AllureDescription("Verifies the new ENG-88190 parameters (optional-properties merge payload and verify flag) flow through the dry-run path without triggering parameter-validation errors.")]
	public async Task PageUpdateTool_Should_Accept_OptionalProperties_And_Verify_In_DryRun() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrOptionalProps_FormPage",
					["body"] = MinimalMarkerPageBody,
					["dry-run"] = true,
					["environment-name"] = environmentName,
					["optional-properties"] = "[{\"key\":\"layout\",\"value\":\"sidebar\"}]",
					["verify"] = true
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the new optional-properties and verify parameters must flow through the tool without triggering envelope-level errors");
		response.Error.Should().NotMatch(
			"*optional-properties*invalid*",
			because: "a valid JSON array for optional-properties must not be rejected at the parameter-validation stage");
		response.Error.Should().NotContain("verify",
			because: "verify is a tool-layer read-back flag and must not surface as a validation error");
	}

	[Test]
	[Description("Rejects malformed optional-properties JSON through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects malformed optional-properties JSON")]
	[AllureDescription("Verifies that optional-properties validation rejects payloads that are not valid JSON arrays with a readable structured error.")]
	public async Task PageUpdateTool_Should_Reject_Invalid_OptionalProperties_Json() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrBadOptionalProps_FormPage",
					["body"] = MinimalMarkerPageBody,
					["dry-run"] = true,
					["environment-name"] = environmentName,
					["optional-properties"] = "{not-an-array}"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "malformed optional-properties payloads should be surfaced as structured validation failures");
		response.Success.Should().BeFalse(
			because: "update-page should reject malformed optional-properties JSON before save or dry-run validation continues");
		response.Error.Should().MatchRegex("(?i)optional-properties",
			because: "the error message must identify the offending parameter");
	}

	[Test]
	[Description("ENG-91317 ticket scenario: get-page captures a checksum baseline, an out-of-band save changes the schema, update-page detects the conflict (verifying SysSchema.Checksum is bumped on save — risk A-01), recovery via get-page + retry succeeds, and force=true overwrites deliberately.")]
	[AllureTag(ToolName)]
	[AllureName("update-page detects out-of-band schema modification via checksum baseline and recovers")]
	[AllureDescription("Uses the real clio MCP server against the seeded page ClioMcp_BlankPageToSave: (1) get-page anchored at a temp directory stores the baseline; (2) a second update-page anchored at a DIFFERENT temp directory simulates the out-of-band modification (no baseline there, so no conflict check and no refresh of the first baseline); (3) update-page anchored at the first directory must fail with conflict:true / checksum-mismatch — this is the live proof that Creatio bumps SysSchema.Checksum on SaveSchema; (4) re-running get-page refreshes the baseline and the retry succeeds, restoring the original body (built-in cleanup).")]
	public async Task PageUpdateTool_Should_Detect_OutOfBand_Modification_And_Recover_Via_GetPage() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive update-page conflict-detection test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		const string savePage = "ClioMcp_BlankPageToSave";
		string sessionDir = Directory.CreateTempSubdirectory("clio-e2e-conflict-session-").FullName;
		string outOfBandDir = Directory.CreateTempSubdirectory("clio-e2e-conflict-oob-").FullName;
		try {
			// Act 1: get-page anchored at sessionDir — captures the checksum baseline.
			PageGetResponse getResponse = await GetPageAsync(arrangeContext, savePage, environmentName, sessionDir);
			getResponse.Success.Should().BeTrue(
				because: $"get-page must succeed for the seeded page '{savePage}'. Error: {getResponse.Error}");
			string originalBody = await File.ReadAllTextAsync(getResponse.Files.BodyFile);
			string metaJson = await File.ReadAllTextAsync(getResponse.Files.MetaFile);
			metaJson.Should().Contain("\"baseline\"",
				because: "get-page must persist the conflict-detection baseline into meta.json (story 2 contract, live)");

			// Act 2: out-of-band modification — update-page anchored at a DIFFERENT directory
			// (no baseline there → no conflict check, and the session baseline stays untouched).
			string outOfBandBody = originalBody.Replace(
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrE2EOobContainer\",\"values\":{\"type\":\"crt.FlexContainer\",\"direction\":\"row\",\"items\":[]},\"parentName\":\"Main\",\"propertyName\":\"items\",\"index\":0}]/**SCHEMA_VIEW_CONFIG_DIFF*/");
			PageUpdateResponse outOfBandResponse = await UpdatePageAsync(
				arrangeContext, savePage, outOfBandBody, environmentName, outOfBandDir);
			outOfBandResponse.Success.Should().BeTrue(
				because: $"the simulated out-of-band save must succeed to set up the conflict. Error: {outOfBandResponse.Error}");

			// Act 3: update-page with the stale session baseline — must surface the conflict.
			PageUpdateResponse conflictResponse = await UpdatePageAsync(
				arrangeContext, savePage, originalBody, environmentName, sessionDir);

			// Assert: structured conflict, proving Creatio bumped SysSchema.Checksum on save (A-01).
			conflictResponse.Success.Should().BeFalse(
				because: "the schema changed outside the session, so the stale-baseline write must be blocked");
			conflictResponse.Conflict.Should().BeTrue(
				because: "the response must carry the machine-readable conflict marker through the real MCP transport");
			conflictResponse.ConflictDetails.Should().NotBeNull(
				because: "the conflict must explain itself with structured details");
			conflictResponse.ConflictDetails.Reason.Should().Be("checksum-mismatch",
				because: "the out-of-band SaveSchema must have bumped SysSchema.Checksum — the load-bearing assumption (A-01) of the whole feature");
			conflictResponse.Error.Should().Contain("Re-run get-page",
				because: "the error must guide the agent toward the reload-and-rebase recovery");

			// Act 4: recovery — re-run get-page (fresh baseline), retry the save (restores the
			// original blank body, doubling as cleanup of the out-of-band container).
			PageGetResponse refreshedGet = await GetPageAsync(arrangeContext, savePage, environmentName, sessionDir);
			refreshedGet.Success.Should().BeTrue(
				because: $"the recovery get-page must succeed. Error: {refreshedGet.Error}");
			PageUpdateResponse retryResponse = await UpdatePageAsync(
				arrangeContext, savePage, originalBody, environmentName, sessionDir);
			retryResponse.Success.Should().BeTrue(
				because: $"after reloading the baseline the retry must succeed and restore the seed body. Error: {retryResponse.Error}");
			retryResponse.Conflict.Should().BeFalse(
				because: "the refreshed baseline matches the server state, so no conflict remains");
		} finally {
			TryDeleteDirectory(sessionDir);
			TryDeleteDirectory(outOfBandDir);
		}
	}

	[Test]
	[Description("A successful update-page save pushes a Designer Presence save event that a second session can receive for the page sender.")]
	[AllureTag(ToolName)]
	[AllureName("update-page publishes Designer Presence save event")]
	[AllureDescription("Starts the real MCP server, connects a second authenticated session to the page Designer Presence sender, performs a real update-page save against the seeded page ClioMcp_BlankPageToSave, and verifies receipt of a Designer Presence save event.")]
	public async Task PageUpdateTool_Should_Publish_DesignerPresence_Save_Event() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping live Designer Presence save-event E2E.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		const string savePage = "ClioMcp_BlankPageToSave";
		string outputDirectory = Directory.CreateTempSubdirectory("clio-e2e-presence-").FullName;
		await using var listener = await DesignerPresenceListener.StartAsync(environmentName, savePage);
		try {
			PageGetResponse getResponse = await GetPageAsync(arrangeContext, savePage, environmentName, outputDirectory);
			getResponse.Success.Should().BeTrue(
				because: $"get-page must succeed for the seeded page '{savePage}' before the save-event probe. Error: {getResponse.Error}");
			string originalBody = await File.ReadAllTextAsync(getResponse.Files.BodyFile);

			// Act
			PageUpdateResponse saveResponse = await UpdatePageAsync(
				arrangeContext,
				savePage,
				originalBody,
				environmentName,
				outputDirectory);
			DesignerPresenceServerEvent receivedEvent = await listener.WaitForSaveEventAsync(TimeSpan.FromSeconds(30));

			// Assert
			saveResponse.Success.Should().BeTrue(
				because: $"the seeded page save must succeed before Designer Presence can rebroadcast it. Error: {saveResponse.Error}");
			receivedEvent.SchemaName.Should().Be(savePage,
				because: "the save event should be scoped to the same page sender that the listener joined");
			receivedEvent.SchemaType.Should().Be("page",
				because: "update-page publishes page designer presence only in this iteration");
			receivedEvent.Users.Should().Contain(user => string.Equals(user.Mode, "save", StringComparison.OrdinalIgnoreCase),
				because: "the aggregated presence event should contain a collaborator in save mode after update-page succeeds");
		} finally {
			TryDeleteDirectory(outputDirectory);
		}
	}

	[Test]
	[Description("ENG-91317: update-page with force=true overwrites an out-of-band modification deliberately, and a no-baseline flow stays unaffected (regression guard).")]
	[AllureTag(ToolName)]
	[AllureName("update-page force=true overwrites out-of-band changes; no-baseline flow unaffected")]
	[AllureDescription("Against the seeded page ClioMcp_BlankPageToSave: stores a baseline via get-page, makes an out-of-band save from a different anchor, then saves the original body with force=true from the session anchor — the overwrite must succeed (restoring the seed body). A final save without any baseline directory proves the legacy no-baseline flow is unaffected.")]
	public async Task PageUpdateTool_Should_Overwrite_With_Force_And_Keep_NoBaseline_Flow_Unaffected() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive update-page force-overwrite test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		const string savePage = "ClioMcp_BlankPageToSave";
		string sessionDir = Directory.CreateTempSubdirectory("clio-e2e-force-session-").FullName;
		string outOfBandDir = Directory.CreateTempSubdirectory("clio-e2e-force-oob-").FullName;
		try {
			PageGetResponse getResponse = await GetPageAsync(arrangeContext, savePage, environmentName, sessionDir);
			getResponse.Success.Should().BeTrue(
				because: $"get-page must succeed for the seeded page '{savePage}'. Error: {getResponse.Error}");
			string originalBody = await File.ReadAllTextAsync(getResponse.Files.BodyFile);
			string outOfBandBody = originalBody.Replace(
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrE2EForceContainer\",\"values\":{\"type\":\"crt.FlexContainer\",\"direction\":\"row\",\"items\":[]},\"parentName\":\"Main\",\"propertyName\":\"items\",\"index\":0}]/**SCHEMA_VIEW_CONFIG_DIFF*/");
			PageUpdateResponse outOfBandResponse = await UpdatePageAsync(
				arrangeContext, savePage, outOfBandBody, environmentName, outOfBandDir);
			outOfBandResponse.Success.Should().BeTrue(
				because: $"the simulated out-of-band save must succeed to set up the overwrite. Error: {outOfBandResponse.Error}");

			// Act 1: force=true from the stale session anchor — deliberate overwrite (also restores the seed body).
			PageUpdateResponse forceResponse = await UpdatePageAsync(
				arrangeContext, savePage, originalBody, environmentName, sessionDir, force: true);

			// Assert
			forceResponse.Success.Should().BeTrue(
				because: $"force=true must bypass the conflict check after explicit user confirmation. Error: {forceResponse.Error}");
			forceResponse.Conflict.Should().BeFalse(
				because: "a forced overwrite reports no conflict");

			// Act 2: regression guard — a save with no baseline anywhere must behave exactly as before the feature.
			string noBaselineDir = Directory.CreateTempSubdirectory("clio-e2e-nobaseline-").FullName;
			try {
				PageUpdateResponse noBaselineResponse = await UpdatePageAsync(
					arrangeContext, savePage, originalBody, environmentName, noBaselineDir);
				noBaselineResponse.Success.Should().BeTrue(
					because: $"the legacy no-baseline flow must stay unaffected (AC-11). Error: {noBaselineResponse.Error}");
				noBaselineResponse.Conflict.Should().BeFalse(
					because: "without a baseline there is nothing to conflict with");
			} finally {
				TryDeleteDirectory(noBaselineDir);
			}
		} finally {
			TryDeleteDirectory(sessionDir);
			TryDeleteDirectory(outOfBandDir);
		}
	}

	private static async Task<PageGetResponse> GetPageAsync(
		ArrangeContext arrangeContext, string schemaName, string environmentName, string outputDirectory) {
		CallToolResult result = await arrangeContext.Session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = environmentName,
					["output-directory"] = outputDirectory
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		result.IsError.Should().NotBeTrue(
			because: "get-page must return a structured payload, not an MCP transport error");
		return EntitySchemaStructuredResultParser.Extract<PageGetResponse>(result);
	}

	private static async Task<PageUpdateResponse> UpdatePageAsync(
		ArrangeContext arrangeContext,
		string schemaName,
		string body,
		string environmentName,
		string outputDirectory,
		bool? force = null) {
		Dictionary<string, object?> args = new() {
			["schema-name"] = schemaName,
			["body"] = body,
			["environment-name"] = environmentName,
			["output-directory"] = outputDirectory,
			["skip-sampling"] = true
		};
		if (force == true) {
			args["force"] = true;
		}
		CallToolResult result = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		result.IsError.Should().NotBeTrue(
			because: "update-page must return a structured payload, not an MCP transport error");
		return EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(result);
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch {
			// best-effort temp cleanup; never fail the test on it.
		}
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"update-page MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	[Test]
	[Description("Rejects a mobile JSON body that contains a 'validators' section without making any remote call.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects mobile body with 'validators' key")]
	[AllureDescription("Verifies that update-page returns a structured validation error for a mobile body containing the 'validators' key, without reaching Creatio.")]
	public async Task PageUpdateTool_Should_Reject_Mobile_Body_With_Validators() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string mobileBodyWithValidators = """
			{
			  "viewConfigDiff": [],
			  "validators": {}
			}
			""";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMobile_FormPage",
					["body"] = mobileBodyWithValidators
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "mobile validation failures should be surfaced as structured tool results, not protocol errors");
		response.Success.Should().BeFalse(
			because: "mobile pages do not support the 'validators' key — update-page must reject the body");
		response.Error.Should().Contain("validators",
			because: "the error should identify the disallowed 'validators' key");
	}

	[Test]
	[Description("Accepts a valid mobile JSON body (plain JSON starting with '{') and skips AMD marker validation.")]
	[AllureTag(ToolName)]
	[AllureName("update-page accepts a valid mobile JSON body without AMD markers")]
	[AllureDescription("Verifies that update-page passes a well-formed mobile body through mobile validation only (no AMD marker checks) before attempting the save.")]
	public async Task PageUpdateTool_Should_Accept_Valid_Mobile_Body_Without_AMD_Markers() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string mobileBody = """
			{
			  "viewConfigDiff": [],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMobile_FormPage",
					["body"] = mobileBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid mobile body should produce a structured result even when dry-run fails because the schema doesn't exist");
		response.Error.Should().NotContain("SCHEMA_",
			because: "AMD marker errors must not appear when the body is a mobile JSON object");
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private sealed class DesignerPresenceListener : IAsyncDisposable {
		private readonly IApplicationClient _applicationClient;
		private readonly IServiceProvider _provider;
		private readonly CancellationTokenSource _listenCancellationTokenSource;
		private readonly Task _listenTask;
		private readonly TaskCompletionSource<bool> _connectedTcs =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<DesignerPresenceServerEvent> _saveEventTcs =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly string _senderName;
		private readonly EventHandler<System.Net.WebSockets.WebSocketState> _connectionHandler;
		private readonly EventHandler<Creatio.Client.Dto.WsMessage> _messageHandler;

		private DesignerPresenceListener(
			IServiceProvider provider,
			IApplicationClient applicationClient,
			string senderName,
			CancellationTokenSource listenCancellationTokenSource) {
			_provider = provider;
			_applicationClient = applicationClient;
			_senderName = senderName;
			_listenCancellationTokenSource = listenCancellationTokenSource;
			_connectionHandler = (_, state) => {
				if (state == System.Net.WebSockets.WebSocketState.Open) {
					_connectedTcs.TrySetResult(true);
				}
			};
			_messageHandler = (_, message) => OnMessageReceived(message);
			_applicationClient.ConnectionStateChanged += _connectionHandler;
			_applicationClient.MessageReceived += _messageHandler;
			_listenTask = Task.Run(() => _applicationClient.Listen(_listenCancellationTokenSource.Token));
		}

		public static async Task<DesignerPresenceListener> StartAsync(string environmentName, string schemaName) {
			EnvironmentSettings environmentSettings = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
			IServiceProvider provider = new BindingsModule().Register(environmentSettings);
			IApplicationClient applicationClient = provider.GetRequiredService<IApplicationClient>();
			var listenCancellationTokenSource = new CancellationTokenSource();
			string senderName = $"DesignerPresence_page_{schemaName.ToLowerInvariant()}";
			var listener = new DesignerPresenceListener(
				provider,
				applicationClient,
				senderName,
				listenCancellationTokenSource);
			await listener.WaitUntilConnectedAsync(TimeSpan.FromSeconds(30));
			await listener.SendViewJoinAsync(schemaName);
			return listener;
		}

		public async Task<DesignerPresenceServerEvent> WaitForSaveEventAsync(TimeSpan timeout) {
			using CancellationTokenSource timeoutTokenSource = new(timeout);
			using CancellationTokenRegistration _ = timeoutTokenSource.Token.Register(() =>
					   _saveEventTcs.TrySetCanceled(timeoutTokenSource.Token));
			{
				return await _saveEventTcs.Task.ConfigureAwait(false);
			}
		}

		private async Task WaitUntilConnectedAsync(TimeSpan timeout) {
			using CancellationTokenSource timeoutTokenSource = new(timeout);
			using CancellationTokenRegistration _ = timeoutTokenSource.Token.Register(() =>
					   _connectedTcs.TrySetCanceled(timeoutTokenSource.Token));
			{
				await _connectedTcs.Task.ConfigureAwait(false);
			}
		}

		private void OnMessageReceived(Creatio.Client.Dto.WsMessage? message) {
			if (message is null) {
				return;
			}
			if (!string.Equals(message.Header?.Sender, _senderName, StringComparison.Ordinal)) {
				return;
			}
			if (string.IsNullOrWhiteSpace(message.Body)) {
				return;
			}
			try {
				DesignerPresenceServerEvent? payload = JsonSerializer.Deserialize<DesignerPresenceServerEvent>(message.Body);
				if (payload is null) {
					return;
				}
				if (payload.Users.Any(user => string.Equals(user.Mode, "save", StringComparison.OrdinalIgnoreCase))) {
					_saveEventTcs.TrySetResult(payload);
				}
			} catch (JsonException) {
				// Ignore unrelated or malformed channel messages; the listener is scoped by sender.
			}
		}

		private async Task SendViewJoinAsync(string schemaName) {
			IBrowserSessionService browserSessionService = _provider.GetRequiredService<IBrowserSessionService>();
			IServiceUrlBuilder serviceUrlBuilder = _provider.GetRequiredService<IServiceUrlBuilder>();
			IReadOnlyList<IMessageChannelPublisher> publishers = _provider.GetServices<IMessageChannelPublisher>().ToArray();
			string sessionPath = await browserSessionService
				.GetSessionPathAsync(_provider.GetRequiredService<EnvironmentSettings>(), forceRefresh: false, ct: CancellationToken.None)
				.ConfigureAwait(false);
			string storageStateJson = await File.ReadAllTextAsync(sessionPath).ConfigureAwait(false);
			IReadOnlyList<BrowserCookie> cookies = StorageStateJson.ParseCookies(storageStateJson);
			(JsonDocument applicationInfo, JsonDocument userInfo) = ReadPresenceContext(serviceUrlBuilder);
			string? clientConnectionClassName = applicationInfo.RootElement
				.GetProperty("applicationInfo")
				.GetProperty("clientConnectionClassName")
				.GetString();
			string rawServiceUrl = applicationInfo.RootElement
				.GetProperty("applicationInfo")
				.GetProperty("serviceUrl")
				.GetString()!;
			Uri serviceUrl = ResolvePresenceServiceUrl(_provider.GetRequiredService<EnvironmentSettings>().Uri, rawServiceUrl, clientConnectionClassName);
			IMessageChannelPublisher publisher = publishers.Single(p => p.ClientConnectionClassName == clientConnectionClassName);
			string sessionId = userInfo.RootElement.GetProperty("userInfo").GetProperty("sessionId").GetString()!;
			string body = JsonSerializer.Serialize(new {
				mode = "view",
				schemaType = "page",
				schemaName = schemaName,
				schemaCaption = schemaName,
				user = new {
					sessionId,
					id = userInfo.RootElement.GetProperty("userInfo").GetProperty("id").GetString(),
					name = userInfo.RootElement.GetProperty("userInfo").GetProperty("contactName").GetString(),
					contactId = userInfo.RootElement.GetProperty("userInfo").GetProperty("contactId").GetString(),
					contactName = userInfo.RootElement.GetProperty("userInfo").GetProperty("contactName").GetString(),
					photoId = userInfo.RootElement.GetProperty("userInfo").GetProperty("photoId").GetString(),
					email = userInfo.RootElement.GetProperty("userInfo").GetProperty("email").GetString()
				},
				sessionId
			});
			await publisher.PublishAsync(new MessageChannelPublishRequest(
				serviceUrl,
				cookies,
				MessageChannelEnvelope.Create("DesignerPresence", "ServerMsg", body))).ConfigureAwait(false);
		}

		private (JsonDocument ApplicationInfo, JsonDocument UserInfo) ReadPresenceContext(IServiceUrlBuilder serviceUrlBuilder) {
			string applicationInfoJson = _applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build(CreatioServicePaths.GetApplicationInfo),
				"{}");
			string userInfoJson = _applicationClient.ExecutePostRequest(
				serviceUrlBuilder.Build(CreatioServicePaths.GetCurrentUserInfo),
				"{}");
			return (JsonDocument.Parse(applicationInfoJson), JsonDocument.Parse(userInfoJson));
		}

		private static Uri ResolvePresenceServiceUrl(string environmentUri, string rawServiceUrl, string? transportClassName) {
			Uri resolved = Uri.TryCreate(rawServiceUrl, UriKind.Absolute, out Uri? absolute)
				? absolute
				: new Uri(new Uri(environmentUri.TrimEnd('/') + "/", UriKind.Absolute), rawServiceUrl);
			if (string.Equals(transportClassName, WebSocketMessageChannelPublisher.TransportClassName, StringComparison.Ordinal)
				&& (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps)) {
				var builder = new UriBuilder(resolved) {
					Scheme = resolved.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
				};
				if (resolved.IsDefaultPort) {
					builder.Port = -1;
				}
				return builder.Uri;
			}
			return resolved;
		}

		public async ValueTask DisposeAsync() {
			_applicationClient.ConnectionStateChanged -= _connectionHandler;
			_applicationClient.MessageReceived -= _messageHandler;
			_listenCancellationTokenSource.Cancel();
			try {
				await _listenTask.ConfigureAwait(false);
			} catch (OperationCanceledException) {
				// Expected when the listener is disposed.
			} catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException)) {
				// Expected when the listener is disposed.
			}
			_listenCancellationTokenSource.Dispose();
			if (_provider is IDisposable disposable) {
				disposable.Dispose();
			}
		}
	}

	private sealed class DesignerPresenceServerEvent {
		[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
		public string SchemaName { get; set; } = string.Empty;

		[System.Text.Json.Serialization.JsonPropertyName("schemaType")]
		public string SchemaType { get; set; } = string.Empty;

		[System.Text.Json.Serialization.JsonPropertyName("schemaCaption")]
		public string SchemaCaption { get; set; } = string.Empty;

		[System.Text.Json.Serialization.JsonPropertyName("users")]
		public List<DesignerPresenceServerUser> Users { get; set; } = [];
	}

	private sealed class DesignerPresenceServerUser {
		[System.Text.Json.Serialization.JsonPropertyName("mode")]
		public string Mode { get; set; } = string.Empty;
	}
}
