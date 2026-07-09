using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the sync-pages composite MCP tool.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("sync-pages")]
[NonParallelizable]
public sealed class PageSyncToolE2ETests : McpContractFixtureBase {

	private const string ToolName = PageSyncTool.ToolName;
	private const string SavePage = "ClioMcp_BlankPageToSave";
	// The returned object must carry the real schema-section property keys
	// (viewConfigDiff, viewModelConfigDiff, ...). Without them the body is invalid
	// JavaScript — `return { [] , {} , ... }` parses `[]` as an empty computed
	// property key and throws "Unexpected token ']'", so the syntax stage rejects
	// the body before any env/content check can run. The markers wrap the VALUES,
	// the keys live outside them (same shape update-page expects).
	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("Exposes sync-pages via the get-tool-contract compact index so callers can discover and invoke it on the lazy tool surface.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages tool is discoverable on the lazy surface")]
	[AllureDescription("Verifies that sync-pages is discoverable via the get-tool-contract compact index of the MCP server.")]
	public async Task PageSyncTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list, so MCP clients can discover the composite tool");
	}

	[Test]
	[Description("Reports readable failures when sync-pages is called with an invalid environment name.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages reports invalid environment failures")]
	[AllureDescription("Starts the real clio MCP server, invokes sync-pages with an unknown environment name, and verifies that the failure stays human-readable.")]
	public async Task PageSyncTool_Should_Report_Invalid_Environment_Failure() {
		await using ArrangeContext context = await ArrangeAsync();
		string invalidEnvironmentName = $"missing-sync-pages-env-{Guid.NewGuid():N}";

		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = "UsrMissing_FormPage",
							["body"] = ValidPageBody
						}
					}
				}
			},
			context.CancellationTokenSource.Token);
		bool structuredFailure = TryExtractFailure(callResult, out PageSyncResponse? response)
			&& response is not null
			&& !response.Success;
		string serializedCallResult = JsonSerializer.Serialize(new {
			callResult.IsError,
			callResult.StructuredContent,
			callResult.Content
		});

		(callResult.IsError == true || structuredFailure).Should().BeTrue(
			because: "sync-pages should fail when the requested environment does not exist");
		// sync-pages is a hidden long-tail tool routed through the clio-run executor, so an
		// invocation-layer failure may also surface as the wrapped "Error: tool '<name>' failed:" text.
		serializedCallResult.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|error occurred invoking|tool '{Regex.Escape(ToolName)}' failed)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Rejects a page body with missing schema markers through the real MCP server before any remote save is attempted. Body is syntactically valid JavaScript so the syntax gate passes — the markers validator catches the missing SCHEMA_* envelope.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects body with missing markers during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends a syntactically valid page body whose SCHEMA_* markers are missing through sync-pages, and verifies that the markers validator (not the upstream syntax gate) fails the call without requiring a real page save.")]
	public async Task PageSyncTool_Should_Reject_Invalid_Page_Body_When_Validation_Is_Enabled() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrValidationOnly_{Guid.NewGuid():N}",
							["body"] = "define('BadPage', [], function() { return {}; });"
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "validation failures should be reported as structured tool results");
		response.Success.Should().BeFalse(
			because: "client-side validation should reject page bodies with missing schema markers");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the body without markers should fail validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned when validation is enabled");
		response.Pages[0].Validation!.MarkersOk.Should().BeFalse(
			because: "the body is missing the required SCHEMA_* marker envelope");
		response.Pages[0].Error.Should().Contain("validation failed",
			because: "the response should explain that client-side validation blocked the save");
	}

	[Test]
	[Description("Rejects a marker-valid page body that sets a user-visible text property (placeholder) to an inline string literal instead of a localizable-string binding, before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects inline placeholder literal during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends a marker-valid page body whose inserted crt.Input carries a hardcoded placeholder through sync-pages with validation enabled, and verifies that the localizable-text check fails the call without requiring a real page save.")]
	public async Task PageSyncTool_Should_Reject_Inline_Placeholder_Literal_When_Validation_Is_Enabled() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string inlinePlaceholderBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[" +
			"{\"operation\":\"insert\",\"name\":\"EmailField\",\"values\":{\"type\":\"crt.Input\"," +
			"\"control\":\"$Email\",\"placeholder\":\"name@firm.com\"}}" +
			"]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		await using ArrangeContext context = await ArrangeAsync();
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrInlinePlaceholder_{Guid.NewGuid():N}",
							["body"] = inlinePlaceholderBody
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "validation failures should be reported as structured tool results");
		response.Success.Should().BeFalse(
			because: "client-side validation should reject a hardcoded placeholder that cannot be translated");
		response.Pages.Should().ContainSingle(because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(because: "the inline placeholder literal must fail validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned when validation is enabled");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "the localizable-text rule is a content-level validator");
		response.Pages[0].Validation.Errors!.Should().Contain(
			e => e.Contains("EmailField") && e.Contains("placeholder") && e.Contains("page-schema-resources"),
			because: "the diagnostic must name the node, the offending property, and point to the localization guide");
	}

	[Test]
	[Description("sync-pages fails fast at the JavaScript-syntax gate BEFORE sampling and BEFORE any remote save when a body contains an `await X = Y` assignment shape (the actual production incident). Verifies the gate runs end-to-end through the real MCP transport per the AC.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages fails fast on JavaScript syntax error before sampling")]
	[AllureDescription("Starts the real clio MCP server, sends a single-page sync-pages call with the incident body (`await request.$context.X = Y`), and verifies that the per-page result carries the JavaScript-syntax-error message and the 'NOT sent to Creatio' assurance — without sampling tokens spent and without any remote save attempted.")]
	public async Task PageSyncTool_Should_FailFast_When_Body_Has_JavaScript_Syntax_Error() {
		await using ArrangeContext context = await ArrangeAsync();
		// `await` cannot be an assignment target; no environment-name because the
		// syntax gate must short-circuit before environment resolution.
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

		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrSyntaxIncident_{Guid.NewGuid():N}",
							["body"] = nonValidBody
						}
					},
					["skip-sampling"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "the syntax failure is a structured tool response, not an MCP transport error");
		response.Success.Should().BeFalse(
			because: "the incident body must be rejected end-to-end via the real MCP transport — the unit test alone is not enough per AGENTS.md MCP e2e rule");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted");
		response.Pages[0].Success.Should().BeFalse(
			because: "the per-page result must mirror the overall failure");
		response.Pages[0].Error.Should().Contain("JavaScript syntax error",
			because: "the agent-facing error must name the actual class of problem (parser rejection) so the caller does not chase a phantom environment / marker / sampling failure");
		response.Pages[0].Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the broken body did not reach the server without inspecting logs, even when the failure surfaces through the MCP wire");
	}

	[Test]
	[Description("sync-pages fails fast at the AST lint gate when a custom converter uses the reserved `crt.*` prefix — the lint rule `converter-crt-prefix-reserved` is unique to the AST pass (the regex layer treats `crt.*` as a valid vendor prefix), so this body is what proves the lint pass surfaces through the real MCP transport, no sampling and no remote save attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages fails fast on converter-crt-prefix-reserved lint error")]
	[AllureDescription("Starts the real clio MCP server and submits a body whose `converters` section registers a custom converter under the reserved `crt.*` namespace. The existing regex validators accept the body (their shape checks explicitly skip `crt.*` keys); verifying the per-page response carries `Page body lint failed` confirms the AST lint pass surfaces end-to-end through the real MCP wire.")]
	public async Task PageSyncTool_Should_FailFast_When_Custom_Converter_Uses_Crt_Prefix() {
		await using ArrangeContext context = await ArrangeAsync();
		// Body uses the reserved `crt.*` namespace for a custom converter
		// — only the AST lint pass catches this; the regex layer treats
		// `crt.*` as a valid vendor prefix.
		string crtPrefixConverterBody =
			"define(\"Bad_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{ \"crt.MyConverter\": function(v) { return v; } }/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrLintCrtConverter_{Guid.NewGuid():N}",
							["body"] = crtPrefixConverterBody
						}
					},
					["skip-sampling"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "the lint failure is a structured tool response, not an MCP transport error");
		response.Success.Should().BeFalse(
			because: "the reserved `crt.*` namespace is for Creatio built-in converters; lint must catch the custom-name usage end-to-end via the real MCP transport per AGENTS.md MCP rule");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted");
		response.Pages[0].Success.Should().BeFalse(
			because: "the per-page result must mirror the overall failure");
		response.Pages[0].Error.Should().Contain("Page body lint failed",
			because: "the canonical lint error prefix is the contract surface the agent keys on to distinguish lint rejection from syntax / sampling rejection");
		response.Pages[0].Error.Should().Contain("converter-crt-prefix-reserved",
			because: "the rule id must be visible in the wire response so the agent can map the failure back to the guidance doc that describes the anti-pattern");
		response.Pages[0].Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the body did not reach the server without inspecting logs, mirroring the syntax-gate tail");
	}

	[Test]
	[Description("Keeps JavaScript handlers out of JSON content validation failures.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages ignores handler JavaScript during content validation")]
	[AllureDescription("Uses any reachable environment, sends a page body with JavaScript handlers plus a malformed JSON-backed marker, and verifies that validation reports the real JSON marker instead of the handler block.")]
	public async Task PageSyncTool_Should_Not_Report_Handler_Marker_As_Invalid_Json() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithHandlerAndBrokenJson = ValidPageBody
			.Replace(
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"name\":\"DataTable\"},,]/**SCHEMA_VIEW_CONFIG_DIFF*/")
			.Replace(
				"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
				"/**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { await next?.handle(request); } }]/**SCHEMA_HANDLERS*/");
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrHandlerValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithHandlerAndBrokenJson
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "validation failures should stay in the structured response");
		response.Success.Should().BeFalse(
			because: "the malformed JSON-backed marker should still fail validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "sync-pages should return validation details for the rejected body");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "viewConfigDiff contains invalid JSON-like content");
		response.Pages[0].Error.Should().Contain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "the malformed JSON-backed marker should be identified in the validation error");
		response.Pages[0].Error.Should().NotContain("SCHEMA_HANDLERS",
			because: "handler blocks may contain JavaScript and should not be parsed as JSON");
	}

	[Test]
	[Description("Keeps JavaScript converters and validators out of JSON content validation failures.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages ignores converter and validator JavaScript during content validation")]
	[AllureDescription("Uses a reachable sandbox environment, sends a page body with function-based converters and validators plus a malformed JSON-backed marker, and verifies that validation reports the real JSON marker instead of SCHEMA_CONVERTERS or SCHEMA_VALIDATORS.")]
	public async Task PageSyncTool_Should_Not_Report_Converter_Or_Validator_Markers_As_Invalid_Json() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run sync-pages validation E2E.");
		}
		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"sync-pages validation E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithConverterValidatorAndBrokenJson = ValidPageBody
			.Replace(
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"name\":\"DataTable\"},,]/**SCHEMA_VIEW_CONFIG_DIFF*/")
			.Replace(
				"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
				"/**SCHEMA_CONVERTERS*/{ \"usr.ToUpperCase\": function(value) { return value?.toUpperCase() ?? \"\"; } }/**SCHEMA_CONVERTERS*/")
			.Replace(
				"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
				"/**SCHEMA_VALIDATORS*/{ \"usr.ValidateFieldValue\": { \"validator\": function(config) { return function(control) { return control.value !== config.invalidName ? null : { \"usr.ValidateFieldValue\": { message: config.message } }; }; }, \"params\": [{ \"name\": \"invalidName\" }, { \"name\": \"message\" }], \"async\": false } }/**SCHEMA_VALIDATORS*/");
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrConverterValidatorValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithConverterValidatorAndBrokenJson
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "validation failures should stay in the structured response");
		response.Success.Should().BeFalse(
			because: "the malformed JSON-backed marker should still fail validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "sync-pages should return validation details for the rejected body");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "viewConfigDiff contains invalid JSON-like content");
		response.Pages[0].Error.Should().Contain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "the malformed JSON-backed marker should be identified in the validation error");
		response.Pages[0].Error.Should().NotContain("SCHEMA_CONVERTERS",
			because: "converter blocks may contain JavaScript functions and should not be parsed as JSON");
		response.Pages[0].Error.Should().NotContain("SCHEMA_VALIDATORS",
			because: "validator blocks may contain JavaScript functions and should not be parsed as JSON");
	}

	[Test]
	[Description("Rejects proxy standard field bindings through the real MCP server before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects proxy field bindings during semantic validation")]
	[AllureDescription("Uses any reachable environment, sends a page body with a standard field bound through a proxy Usr attribute, and verifies that semantic validation blocks the save with a structured response.")]
	public async Task PageSyncTool_Should_Reject_Proxy_Field_Bindings_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithProxyBinding = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"values\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrProxyValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithProxyBinding
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "semantic validation failures should stay in the structured tool response");
		response.Success.Should().BeFalse(
			because: "the broken proxy binding should be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail semantic validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "semantic field validation contributes to the content-ok decision");
		response.Pages[0].Error.Should().Contain("$UsrStatus")
			.And.Contain("$PDS_UsrStatus",
				because: "the failure should explain both the rejected proxy binding and the expected datasource binding");
	}

	[Test]
	[Description("Rejects non-array handlers through the real MCP server before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects non-array handlers during semantic validation")]
	[AllureDescription("Uses any reachable environment, sends a page body with SCHEMA_HANDLERS authored as an object instead of an array, and verifies that client-side validation blocks the save with a structured response.")]
	public async Task PageSyncTool_Should_Reject_NonArray_Handlers_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithNonArrayHandlers = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_HANDLERS*/{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { await next?.handle(request); } }/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrInvalidHandlers_{Guid.NewGuid():N}",
							["body"] = bodyWithNonArrayHandlers
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "handler validation failures should stay in the structured tool response");
		response.Success.Should().BeFalse(
			because: "sync-pages should reject schemas where SCHEMA_HANDLERS is no longer an array literal");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail handler-shape validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "handler-shape validation contributes to the content-ok decision");
		response.Pages[0].Error.Should().Contain("SCHEMA_HANDLERS")
			.And.Contain("array literal",
				because: "the failure should explain that the handlers section must stay an array");
	}

	[Test]
	[Description("Reads the seeded Freedom UI page ClioMcp_BlankPageToSave via get-page, sends the unchanged body back through sync-pages with validate=true, and verifies the no-op save succeeds.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages saves and verifies a real page with structured read-back")]
	[AllureDescription("Uses the real clio MCP server to call get-page for the seeded page ClioMcp_BlankPageToSave in AutoTestClioMcp, then sends the unchanged body back through sync-pages with validate=true, and verifies the save succeeds without validation drift so the seed page's body remains stable across read-write cycles.")]
	public async Task PageSyncTool_Should_Save_And_Verify_Real_Page_With_NoOp_Body() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive sync-pages save-and-verify test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using ArrangeContext context = await ArrangeAsync();

		// Act 1: get-page reads the seeded page's body to disk
		CallToolResult getResult = await context.Session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = SavePage,
					["environment-name"] = environmentName
				}
			},
			context.CancellationTokenSource.Token);
		PageGetResponse getResponse = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getResult);

		getResult.IsError.Should().NotBeTrue(
			because: $"get-page should return a structured payload for the seeded page '{SavePage}' before the no-op save can run");
		getResponse.Success.Should().BeTrue(
			because: $"get-page must succeed for the seeded page '{SavePage}'. Error: {getResponse.Error}");
		getResponse.Files.Should().NotBeNull(
			because: "successful get-page calls must return the materialized file paths");
		getResponse.Files.BodyFile.Should().NotBeNullOrWhiteSpace(
			because: "sync-pages needs a body-file path to read the raw body from disk");
		string body = await File.ReadAllTextAsync(getResponse.Files.BodyFile);
		body.Should().NotBeNullOrWhiteSpace(
			because: "the no-op save is only meaningful if get-page returns a non-empty body");

		// Act 2: sync-pages saves the unchanged body back with validation
		CallToolResult syncResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = SavePage,
							["body"] = body
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse syncResponse = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(syncResult);

		// Assert sync-pages save-and-verify succeeded
		syncResult.IsError.Should().NotBeTrue(
			because: "the no-op save should produce a structured sync-pages response instead of a transport-level error");
		syncResponse.Success.Should().BeTrue(
			because: $"sync-pages must accept and persist the body get-page produced for '{SavePage}'. Per-page error: {syncResponse.Pages.FirstOrDefault()?.Error}");
		syncResponse.Pages.Should().ContainSingle(
			because: "one page was submitted for sync");
		syncResponse.Pages[0].Success.Should().BeTrue(
			because: $"per-page sync-pages result must succeed for '{SavePage}'. Error: {syncResponse.Pages[0].Error}");
		syncResponse.Pages[0].Error.Should().BeNullOrWhiteSpace(
			because: "a successful no-op save should not include an error payload");
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
			$"sync-pages MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	[Test]
	[Description("ENG-91317: sync-pages surfaces a per-page conflict for a stale-baseline page after an out-of-band modification, and the per-page force flag overwrites it deliberately (restoring the seed body).")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages detects out-of-band schema modification per page and honors per-page force")]
	[AllureDescription("Against the seeded page ClioMcp_BlankPageToSave: (1) get-page anchored at a temp directory stores the checksum baseline; (2) an update-page anchored at a DIFFERENT temp directory simulates the out-of-band modification; (3) sync-pages from the first anchor must fail that page with conflict:true / conflict-details (checksum-mismatch); (4) the same batch entry resubmitted with force:true must overwrite, restoring the original blank body (built-in cleanup).")]
	public async Task PageSyncTool_Should_Surface_PerPage_Conflict_And_Honor_PerPage_Force() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive sync-pages conflict-detection test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using ArrangeContext context = await ArrangeAsync();
		string sessionDir = Directory.CreateTempSubdirectory("clio-e2e-sync-conflict-").FullName;
		string outOfBandDir = Directory.CreateTempSubdirectory("clio-e2e-sync-oob-").FullName;
		try {
			// Act 1: get-page anchored at sessionDir — captures the baseline.
			CallToolResult getResult = await context.Session.CallToolAsync(
				PageGetTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = SavePage,
						["environment-name"] = environmentName,
						["output-directory"] = sessionDir
					}
				},
				context.CancellationTokenSource.Token);
			PageGetResponse getResponse = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(getResult);
			getResponse.Success.Should().BeTrue(
				because: $"get-page must succeed for the seeded page '{SavePage}'. Error: {getResponse.Error}");
			string originalBody = await File.ReadAllTextAsync(getResponse.Files.BodyFile);

			// Act 2: out-of-band modification via update-page anchored elsewhere (no baseline there).
			string outOfBandBody = originalBody.Replace(
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/",
				"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrE2ESyncOobContainer\",\"values\":{\"type\":\"crt.FlexContainer\",\"direction\":\"row\",\"items\":[]},\"parentName\":\"Main\",\"propertyName\":\"items\",\"index\":0}]/**SCHEMA_VIEW_CONFIG_DIFF*/");
			CallToolResult outOfBandResult = await context.Session.CallToolAsync(
				PageUpdateTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = SavePage,
						["body"] = outOfBandBody,
						["environment-name"] = environmentName,
						["output-directory"] = outOfBandDir,
						["skip-sampling"] = true
					}
				},
				context.CancellationTokenSource.Token);
			PageUpdateResponse outOfBandResponse = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(outOfBandResult);
			outOfBandResponse.Success.Should().BeTrue(
				because: $"the simulated out-of-band save must succeed to set up the conflict. Error: {outOfBandResponse.Error}");

			// Act 3: sync-pages from the stale session anchor — the page must fail with a conflict.
			CallToolResult conflictResult = await context.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["pages"] = new[] {
							new Dictionary<string, object?> {
								["schema-name"] = SavePage,
								["body"] = originalBody
							}
						},
						["validate"] = true,
						["skip-sampling"] = true,
						["output-directory"] = sessionDir
					}
				},
				context.CancellationTokenSource.Token);
			PageSyncResponse conflictResponse = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(conflictResult);

			// Assert per-page conflict
			conflictResponse.Success.Should().BeFalse(
				because: "the stale-baseline page must fail the batch");
			conflictResponse.Pages.Should().ContainSingle(
				because: "one page was submitted for sync");
			conflictResponse.Pages[0].Conflict.Should().BeTrue(
				because: "the per-page result must carry the conflict marker through the real MCP transport");
			conflictResponse.Pages[0].ConflictDetails.Should().NotBeNull(
				because: "the per-page conflict must explain itself with structured details");
			conflictResponse.Pages[0].ConflictDetails.Reason.Should().Be("checksum-mismatch",
				because: "the out-of-band SaveSchema bumped SysSchema.Checksum (risk A-01) and the baseline went stale");

			// Act 4: per-page force=true overwrites deliberately, restoring the seed body (cleanup).
			CallToolResult forceResult = await context.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["pages"] = new[] {
							new Dictionary<string, object?> {
								["schema-name"] = SavePage,
								["body"] = originalBody,
								["force"] = true
							}
						},
						["validate"] = true,
						["skip-sampling"] = true,
						["output-directory"] = sessionDir
					}
				},
				context.CancellationTokenSource.Token);
			PageSyncResponse forceResponse = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(forceResult);

			// Assert force overwrite
			forceResponse.Success.Should().BeTrue(
				because: $"per-page force=true must bypass the conflict check after user confirmation. Per-page error: {forceResponse.Pages.FirstOrDefault()?.Error}");
			forceResponse.Pages[0].Conflict.Should().BeFalse(
				because: "a forced overwrite reports no conflict");
		} finally {
			TryDeleteDirectory(sessionDir);
			TryDeleteDirectory(outOfBandDir);
		}
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

	[Test]
	[Description("Rejects a SCHEMA_CONVERTERS entry whose key is missing the required dot separator before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects converter key without dot during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends a page body with a converter key that has no dot, and verifies that client-side validation blocks the save with a structured response naming the key and the VendorPrefix requirement.")]
	public async Task PageSyncTool_Should_Reject_Converter_Key_Without_Dot_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithBadConverter = ValidPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"UsrBadConverter\": function(value) { return value; } }/**SCHEMA_CONVERTERS*/");
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrConverterKeyValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithBadConverter
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "converter key validation failures should be reported as structured tool results, not protocol errors");
		response.Success.Should().BeFalse(
			because: "a converter key without a dot causes a Creatio runtime error and must be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail client-side converter key validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "converter key format failure is a content-level error");
		response.Pages[0].Error.Should().Contain("UsrBadConverter")
			.And.Contain("VendorPrefix",
				because: "the error must name the offending key and reference the VendorPrefix.Name format requirement");
	}

	[Test]
	[Description("Rejects a SCHEMA_VALIDATORS entry that uses 'validate' alias instead of the canonical 'validator' factory key before any remote save.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects validator with 'validate' key alias during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends a page body where SCHEMA_VALIDATORS uses the misleading 'validate' key alias, and verifies that client-side validation blocks the save with a factory-shape error.")]
	public async Task PageSyncTool_Should_Reject_Validator_With_Validate_Key_Alias_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithValidateAlias = ValidPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"usr.PhoneFormatValidator\": { params: [{ \"name\": \"message\" }], async: false, " +
			"validate: function(value, config) { return null; } } }/**SCHEMA_VALIDATORS*/");
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrValidatorFactoryShape_{Guid.NewGuid():N}",
							["body"] = bodyWithValidateAlias
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "validator factory-shape failures should be reported as structured tool results, not protocol errors");
		response.Success.Should().BeFalse(
			because: "the runtime ignores any key other than 'validator', so the validator never executes and must be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail client-side validator factory-shape validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "validator factory shape failure is a content-level error");
		response.Pages[0].Error.Should().Contain("usr.PhoneFormatValidator")
			.And.Contain("'validate'")
			.And.Contain("'validator'")
			.And.Contain("page-schema-validators",
				because: "the error must name the offending validator and the wrong key, and direct the agent at the validator guidance");
	}

	[Test]
	[Description("Rejects a SCHEMA_CONVERTERS entry whose value is an object literal instead of a callable function expression before any remote save.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects converter with object literal value during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends a page body where a SCHEMA_CONVERTERS entry has an object literal in place of a function value, and verifies that client-side validation blocks the save with a function-shape error.")]
	public async Task PageSyncTool_Should_Reject_Converter_With_Object_Literal_Value_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithObjectConverter = ValidPageBody.Replace(
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
			"/**SCHEMA_CONVERTERS*/{ \"usr.WrongShape\": { transform: \"upper\" } }/**SCHEMA_CONVERTERS*/");
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrConverterFunctionShape_{Guid.NewGuid():N}",
							["body"] = bodyWithObjectConverter
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "converter function-shape failures should be reported as structured tool results, not protocol errors");
		response.Success.Should().BeFalse(
			because: "an object-literal converter silently fails to apply at the binding site and must be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail client-side converter function-shape validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "converter function shape failure is a content-level error");
		response.Pages[0].Error.Should().Contain("usr.WrongShape")
			.And.Contain("not callable")
			.And.Contain("page-schema-converters",
				because: "the error must name the offending converter and direct the agent at the converter guidance");
	}

	[Test]
	[Description("Rejects a SCHEMA_HANDLERS entry whose request value is missing the required dot separator before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects handler request value without dot during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends a page body with a SCHEMA_HANDLERS array entry whose request value has no dot, and verifies that client-side validation blocks the save with a structured response naming the value and the VendorPrefix requirement.")]
	public async Task PageSyncTool_Should_Reject_Handler_Request_Without_Dot_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithBadHandler = ValidPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"/**SCHEMA_HANDLERS*/[{ request: \"BadHandlerRequest\", " +
			"handler: async (request, next) => { await next?.handle(request); } }]/**SCHEMA_HANDLERS*/");
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrHandlerRequestValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithBadHandler
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "handler request validation failures should be reported as structured tool results, not protocol errors");
		response.Success.Should().BeFalse(
			because: "a handler request value without a dot causes a Creatio runtime error and must be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail client-side handler request validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "handler request format failure is a content-level error");
		response.Pages[0].Error.Should().Contain("BadHandlerRequest")
			.And.Contain("VendorPrefix")
			.And.Contain("page-schema-handlers",
				because: "the error must name the offending request value and direct the agent at the handler guidance");
	}

	[Test]
	[Description("Rejects a SCHEMA_VALIDATORS entry whose key is missing the required dot separator before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects validator key without dot during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends a page body with a validator key that has no dot, and verifies that client-side validation blocks the save with a structured response naming the key and the VendorPrefix requirement.")]
	public async Task PageSyncTool_Should_Reject_Validator_Key_Without_Dot_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithBadValidator = ValidPageBody.Replace(
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
			"/**SCHEMA_VALIDATORS*/{ \"BadValidator\": { params: [] } }/**SCHEMA_VALIDATORS*/");
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrValidatorKeyValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithBadValidator
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "validator key validation failures should be reported as structured tool results, not protocol errors");
		response.Success.Should().BeFalse(
			because: "a validator key without a dot causes a Creatio runtime error and must be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail client-side validator key validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "validator key format failure is a content-level error");
		response.Pages[0].Error.Should().Contain("BadValidator")
			.And.Contain("VendorPrefix")
			.And.Contain("page-schema-validators",
				because: "the error must name the offending key and direct the agent at the validator guidance");
	}

	[Test]
	[Description("Rejects obvious custom max-length validators through the real MCP server before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects obvious custom max-length validators during semantic validation")]
	[AllureDescription("Uses a reachable sandbox environment, sends a page body with a custom usr.NameMaxLength validator, and verifies that semantic validation blocks the save and recommends crt.MaxLength.")]
	public async Task PageSyncTool_Should_Reject_Obvious_Custom_MaxLength_Validators_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run sync-pages semantic validation E2E.");
		}
		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"sync-pages semantic validation E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithCustomMaxLengthValidator = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG*/{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NameMaxLength\":{\"type\":\"usr.NameMaxLength\",\"params\":{\"message\":\"#ResourceString(UsrNameMaxLength_Message)#\"}}}}}}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
			"/**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{\"usr.NameMaxLength\":{\"validator\":function(config){return function(control){if (control.value && control.value.length >= 5) { return {\"usr.NameMaxLength\": { message: config.message }}; } return null;};},\"params\":[{\"name\":\"message\"}],\"async\":false}}/**SCHEMA_VALIDATORS*/ }; });";
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrCustomMaxLengthValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithCustomMaxLengthValidator
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "semantic validation failures should stay in the structured tool response");
		response.Success.Should().BeFalse(
			because: "obvious custom max-length validators should be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail semantic validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "standard-validator enforcement contributes to the content-ok decision");
		response.Pages[0].Error.Should().Contain("usr.NameMaxLength")
			.And.Contain("crt.MaxLength",
				because: "the failure should identify the rejected custom validator and the built-in replacement");
	}

	[Test]
	[Description("Rejects built-in crt.MaxLength bindings that use max instead of maxLength before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects invalid built-in validator param names during semantic validation")]
	[AllureDescription("Uses a reachable sandbox environment, sends a page body with crt.MaxLength configured through params.max, and verifies that semantic validation blocks the save and requires maxLength.")]
	public async Task PageSyncTool_Should_Reject_BuiltIn_MaxLength_With_Wrong_Param_Name_Before_Save() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run sync-pages semantic validation E2E.");
		}
		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"sync-pages semantic validation E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext context = await ArrangeAsync();
		string bodyWithWrongBuiltInParam = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\",\"control\":\"$UsrName\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG*/{\"attributes\":{\"UsrName\":{\"modelConfig\":{\"path\":\"PDS.UsrName\"},\"validators\":{\"NameMaxLength\":{\"type\":\"crt.MaxLength\",\"params\":{\"max\":4}}}}}}/**SCHEMA_VIEW_MODEL_CONFIG*/, " +
			"/**SCHEMA_MODEL_CONFIG*/{}/**SCHEMA_MODEL_CONFIG*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = $"UsrBuiltInMaxLengthValidation_{Guid.NewGuid():N}",
							["body"] = bodyWithWrongBuiltInParam
						}
					},
					["validate"] = true
				}
			},
			context.CancellationTokenSource.Token);
		PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "semantic validation failures should stay in the structured tool response");
		response.Success.Should().BeFalse(
			because: "crt.MaxLength with params.max should be rejected before save");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail semantic validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned for the rejected page");
		response.Pages[0].Validation!.ContentOk.Should().BeFalse(
			because: "validator-param contract enforcement contributes to the content-ok decision");
		response.Pages[0].Error.Should().Contain("crt.MaxLength")
			.And.Contain("max")
			.And.Contain("maxLength",
				because: "the failure should identify both the wrong param and the required param name");
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-sync-pages-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		CancellationTokenSource cancellationTokenSource = new(System.TimeSpan.FromMinutes(5));
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			cancellationToken: cancellationTokenSource.Token);
		McpServerSession session = Session;
		return new ArrangeContext(rootDirectory, workspacePath, session, cancellationTokenSource);
	}

	private static bool TryExtractFailure(CallToolResult callResult, out PageSyncResponse? response) {
		try {
			response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(callResult);
			return true;
		}
		catch (InvalidOperationException) {
			response = null;
			return false;
		}
	}

	[Test]
	[Description("Rejects a mobile JSON body containing a 'converters' section when validate=true is requested.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects mobile body with 'converters' key when validate=true")]
	[AllureDescription("Verifies that sync-pages returns a per-page validation failure for a mobile body containing the 'converters' key when validate mode is enabled.")]
	public async Task PageSyncTool_Should_Reject_Mobile_Body_With_Converters_When_Validate_Is_True() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string mobileBodyWithConverters = """
			{
			  "viewConfigDiff": [],
			  "converters": {}
			}
			""";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "dev",
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = "UsrMobile_FormPage",
							["body"] = mobileBodyWithConverters
						}
					},
					["validate"] = true,
					["skip-sampling"] = true
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "sync-pages mobile validation failures should be surfaced as structured tool results");

		if (TryExtractFailure(callResult, out PageSyncResponse? response) && response is not null) {
			response.Pages.Should().ContainSingle(
				because: "one page was submitted");
			PageSyncPageResult page = response.Pages[0];
			page.Validation!.ContentOk.Should().BeFalse(
				because: "a mobile body containing 'converters' must fail mobile content validation");
		}
	}

	[Test]
	[Description("Accepts a valid mobile JSON body through sync-pages without AMD marker checks.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages accepts a valid mobile JSON body")]
	[AllureDescription("Verifies that sync-pages does not trigger AMD marker validation for plain-JSON mobile bodies — only mobile-specific validation runs.")]
	public async Task PageSyncTool_Should_Accept_Valid_Mobile_Body_Without_AMD_Marker_Errors() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string mobileBody = """
			{
			  "viewConfigDiff": [],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "dev",
					["pages"] = new[] {
						new Dictionary<string, object?> {
							["schema-name"] = "UsrMobile_FormPage",
							["body"] = mobileBody
						}
					},
					["validate"] = true,
					["skip-sampling"] = true
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid mobile body must not raise a protocol-level error");

		if (TryExtractFailure(callResult, out PageSyncResponse? response) && response is not null) {
			foreach (PageSyncPageResult page in response.Pages) {
				if (page.Validation is not null) {
					page.Validation.Errors.Should().NotContain(e => e.Contains("SCHEMA_"),
						because: "AMD marker errors must not appear when the body is a mobile JSON object");
				}
			}
		}
	}

	private new sealed record ArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : System.IAsyncDisposable {

		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
			return ValueTask.CompletedTask;
		}
	}
}
