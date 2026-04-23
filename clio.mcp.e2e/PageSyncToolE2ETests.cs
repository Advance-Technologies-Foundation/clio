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
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the sync-pages composite MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("sync-pages")]
[NonParallelizable]
public sealed class PageSyncToolE2ETests {

	private const string ToolName = PageSyncTool.ToolName;
	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("Advertises sync-pages MCP tool in the server tool list so callers can discover and invoke it.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages tool is advertised by the MCP server")]
	[AllureDescription("Verifies that sync-pages appears in the MCP server tool manifest.")]
	public async Task PageSyncTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(t => t.Name);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "sync-pages must be advertised so MCP clients can discover the composite tool");
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
		serializedCallResult.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|error occurred invoking)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Rejects an invalid page body through the real MCP server before any remote save is attempted.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages rejects invalid body during client-side validation")]
	[AllureDescription("Uses any reachable environment, sends an invalid page body through sync-pages, and verifies that validation fails without requiring a real page save.")]
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
							["body"] = "define('BadPage', {})}"
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
			because: "client-side validation should reject malformed page bodies");
		response.Pages.Should().ContainSingle(
			because: "one page was submitted for validation");
		response.Pages[0].Success.Should().BeFalse(
			because: "the malformed body should fail validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should be returned when validation is enabled");
		response.Pages[0].Validation!.MarkersOk.Should().BeFalse(
			because: "the malformed body is missing required schema markers");
		response.Pages[0].Error.Should().Contain("validation failed",
			because: "the response should explain that client-side validation blocked the save");
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
	[Description("Deferred positive coverage for sync-pages save-and-verify when the E2E environment has a known editable page.")]
	[AllureTag(ToolName)]
	[AllureName("sync-pages saves and verifies a real page with structured read-back")]
	[AllureDescription("Placeholder for a future seeded-data E2E that saves and verifies a known editable page through sync-pages.")]
	public void PageSyncTool_Should_Save_And_Verify_Real_Page_With_NoOp_Body() {
		Assert.Ignore("TODO: add predefined editable page data to the E2E environment, then restore this positive sync-pages save-and-verify scenario.");
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

	private static async Task<ArrangeContext> ArrangeAsync() {
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
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
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

	private sealed record ArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : System.IAsyncDisposable {

		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}
}
