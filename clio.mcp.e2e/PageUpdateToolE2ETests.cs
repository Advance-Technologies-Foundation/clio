using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the update-page MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(PageUpdateTool.ToolName)]
[NonParallelizable]
public sealed class PageUpdateToolE2ETests {
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
	[Description("Advertises update-page MCP tool in the server tool list so callers can discover it directly instead of relying on get-page tests.")]
	[AllureTag(ToolName)]
	[AllureName("update-page tool is advertised by the MCP server")]
	[AllureDescription("Verifies that update-page appears in the MCP server tool manifest.")]
	public async Task PageUpdateTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "update-page must be advertised so MCP callers can discover the single-page save tool directly");
	}

	[Test]
	[Description("update-page fails fast at the JavaScript-syntax gate before any remote call when the body contains an `await X = Y` (the actual production incident body), and the structured response carries the {line, column, message} per the AC.")]
	[AllureTag(ToolName)]
	[AllureName("update-page fails fast on JavaScript syntax error before any remote call")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page with the incident body (`await request.$context.X = Y`), and verifies that the response carries success=false, a 'JavaScript syntax error at line N, column M' message, and the 'NOT sent to Creatio' assurance — the deterministic floor of the validator must surface through the real MCP transport before any remote call is even attempted.")]
	public async Task PageUpdateTool_Should_FailFast_When_Body_Has_JavaScript_Syntax_Error() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
	[Description("update-page fails fast at the AST lint gate when a custom converter uses the reserved `crt.*` prefix — the lint rule `converter-crt-prefix-reserved` is unique to the AST pass (the regex layer treats `crt.*` as a valid vendor prefix), so this body is what proves the lint pass surfaces through the real MCP transport.")]
	[AllureTag(ToolName)]
	[AllureName("update-page fails fast on converter-crt-prefix-reserved lint error before any remote call")]
	[AllureDescription("Starts the real clio MCP server and submits a body whose `converters` section registers a custom converter under the reserved `crt.*` namespace. The existing regex validators accept the body (their shape checks explicitly skip `crt.*` keys), so verifying the structured response carries `Page body lint failed` proves the AST lint pass adds detection beyond the regex layer end-to-end via the real MCP wire.")]
	public async Task PageUpdateTool_Should_FailFast_When_Custom_Converter_Uses_Crt_Prefix() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
	[Description("Surfaces the un-awaited $context read as an advisory warning through update-page dry-run, without failing the call.")]
	[AllureTag(ToolName)]
	[AllureName("update-page surfaces un-awaited $context warning in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with a handler that reads $context without await, and verifies the tool returns a successful structured response carrying the advisory await warning through the real MCP transport.")]
	public async Task PageUpdateTool_Should_Surface_UnAwaitedContextWarning_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string unAwaitedContextBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { const mode = $context[\"UsrMode\"]; return next?.handle(request); } }]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrContextAwaitWarning_FormPage",
					["body"] = unAwaitedContextBody,
					["dry-run"] = true,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an advisory warning must stay inside the structured response, not raise a protocol-level error");
		response.Success.Should().BeTrue(
			because: "an un-awaited $context read is advisory; dry-run validation should still succeed");
		response.Warnings.Should().NotBeNull(
			because: "the response must carry the advisory warning list");
		response.Warnings.Should().Contain(w => w.Contains("UsrMode") && w.Contains("await"),
			because: "update-page must surface the ValidateContextAccessAwait warning through the real MCP transport");
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

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
	[Description("Rejects a run-process button that omits processName through update-page before any remote calls are attempted (ENG-91168).")]
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
	[Description("Rejects invalid handler section shape through update-page dry-run before any remote calls are attempted.")]
	[AllureTag(ToolName)]
	[AllureName("update-page rejects non-array handlers in dry-run mode")]
	[AllureDescription("Starts the real clio MCP server, invokes update-page in dry-run mode with SCHEMA_HANDLERS authored as an object instead of an array, and verifies that the tool returns a structured validation error.")]
	public async Task PageUpdateTool_Should_Reject_NonArray_Handlers_In_DryRun_Mode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

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

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
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

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
