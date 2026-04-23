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
	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
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
					["body"] = ValidPageBody,
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
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrValidationOnly_FormPage",
					["body"] = ValidPageBody,
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
