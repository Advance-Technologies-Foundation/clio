using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end contract tests for the long-tail <c>last-compilation-log</c> MCP tool.
/// </summary>
[TestFixture]
[Category("E2E")]
[AllureNUnit]
[AllureFeature(LastCompilationLogTool.ToolName)]
[NonParallelizable]
public sealed class LastCompilationLogToolE2ETests : McpContractFixtureBase {

	private const string ToolName = LastCompilationLogTool.ToolName;

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("last-compilation-log is discoverable but not resident, so clients invoke it through clio-run.")]
	[AllureTag(ToolName)]
	[AllureName("Last compilation log is a long-tail clio-run tool")]
	[AllureDescription("Verifies that last-compilation-log is absent from the resident tools/list surface but remains discoverable through get-tool-contract for clio-run callers.")]
	public async Task LastCompilationLog_ShouldBeDiscoverableButNotResident(){
		// Arrange
		await using var context = Arrange();

		// Act
		bool advertised = await context.Session.IsToolAdvertisedAsync(
			ToolName, context.CancellationTokenSource.Token);
		IReadOnlyCollection<string> reachable = await context.Session.ListReachableToolNamesAsync(
			context.CancellationTokenSource.Token);
		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } }
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contract = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(
			contractResult);

		// Assert
		advertised.Should().BeFalse(
			because: "last-compilation-log is intentionally excluded from the resident tools/list profile");
		reachable.Should().Contain(ToolName,
			because: "get-tool-contract must expose the long-tail tool so clio-run callers can discover it");
		contract.Success.Should().BeTrue(because: "the long-tail tool must expose a complete derived contract");
		ToolContractDefinition definition = contract.Tools!.Single(tool => tool.Name == ToolName);
		definition.InputSchema.Required.Should().NotContain("environment-name",
			because: "HTTP credential passthrough supplies the target and rejects a mixed explicit environment");
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[Description("last-compilation-log returns a structured failure through clio-run for an unknown environment.")]
	[AllureTag(ToolName)]
	[AllureName("Last compilation log reports invalid environments through clio-run")]
	[AllureDescription("Invokes last-compilation-log through clio-run with an unknown environment and verifies a structured, actionable failure without fabricated diagnostics.")]
	public async Task LastCompilationLog_ShouldReturnStructuredFailure_WhenEnvironmentIsUnknown(){
		// Arrange
		await using var context = Arrange();
		string environmentName = $"missing-last-compilation-log-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await InvokeAsync(
			context.Session, context.CancellationTokenSource.Token, environmentName);
		LastCompilationLogResponse response = ExtractResponse(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "expected environment resolution failures should use the tool's structured response envelope");
		response.Success.Should().BeFalse(because: "an unknown environment prevents result retrieval");
		response.Error.Should().NotBeNullOrWhiteSpace(
			because: "the failure must tell the caller why retrieval did not run");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}|environment.*not.*found|not found)",
			because: "the failure should identify the requested environment or explain that it is not registered");
		response.Diagnostics.Should().BeEmpty(
			because: "there are no compiler diagnostics when the target cannot be resolved");
	}

	[Test]
	[Category("McpE2E.Sandbox")]
	[Description("last-compilation-log returns Creatio's persisted result as structured data through clio-run.")]
	[AllureTag(ToolName)]
	[AllureName("Last compilation log returns a structured sandbox result through clio-run")]
	[AllureDescription("Invokes last-compilation-log through clio-run against the configured sandbox and verifies the typed persisted compilation-result contract.")]
	public async Task LastCompilationLog_ShouldReturnStructuredResult_WhenEnvironmentIsReachable(){
		// Arrange
		McpE2ESettings settings = await AllureApi.Step("Arrange sandbox MCP settings", () => {
			McpE2ESettings configuredSettings = TestConfiguration.Load();
			configuredSettings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			return Task.FromResult(configuredSettings);
		});
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		string environmentName = await AllureApi.Step("Resolve the configured reachable sandbox environment", () =>
			ResolveReachableEnvironmentOrIgnoreAsync(settings));
		await using McpServerSession session = await AllureApi.Step("Start the sandbox MCP server session", () =>
			McpServerSession.StartAsync(settings, cts.Token));

		// Act
		CallToolResult callResult = await AllureApi.Step("Invoke last-compilation-log through clio-run", () =>
			InvokeAsync(session, cts.Token, environmentName));
		LastCompilationLogResponse response = ExtractResponse(callResult);
		string serializedResult = JsonSerializer.Serialize(callResult.StructuredContent);

		// Assert
		AllureApi.Step("Assert the MCP transport call succeeds", () =>
			callResult.IsError.Should().NotBeTrue(
				because: "reading the configured sandbox compilation result should not fail at the MCP transport layer"));
		AllureApi.Step("Assert Creatio's persisted result was retrieved", () =>
			response.Success.Should().BeTrue(
				because: $"the configured sandbox should expose its persisted compilation result. Error: {response.Error}"));
		AllureApi.Step("Assert the numeric build result is preserved", () =>
			response.BuildResult.Should().NotBeNull(
				because: "the typed MCP response must preserve Creatio's numeric build result"));
		AllureApi.Step("Assert the diagnostics collection is always present", () =>
			response.Diagnostics.Should().NotBeNull(
				because: "the typed MCP response must always carry a diagnostics collection, including when it is empty"));
		AllureApi.Step("Assert every diagnostic retains its severity", () =>
			response.Diagnostics.Should().OnlyContain(
				diagnostic => diagnostic.Severity == "error" || diagnostic.Severity == "warning",
				because: "every compiler diagnostic must retain its error or warning classification"));
		AllureApi.Step("Assert compilation outcome is serialized explicitly", () =>
			serializedResult.Should().Contain("compilation-succeeded",
				because: "the clio-run response must serialize compilation outcome explicitly even when it is false"));
		AllureApi.Step("Assert build result is serialized explicitly", () =>
			serializedResult.Should().Contain("build-result",
				because: "the clio-run response must serialize Creatio's build-result field"));
	}

	private static LastCompilationLogResponse ExtractResponse(CallToolResult callResult) {
		return EntitySchemaStructuredResultParser.Extract<LastCompilationLogResponse>(callResult);
	}

	private static Task<CallToolResult> InvokeAsync(
		McpServerSession session, CancellationToken cancellationToken, string environmentName) {
		return session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = ToolName,
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
	}

	private static async Task<string> ResolveReachableEnvironmentOrIgnoreAsync(McpE2ESettings settings) {
		string? configured = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configured) && await CanReachEnvironmentAsync(settings, configured)) {
			return configured;
		}
		Assert.Ignore(
			$"last-compilation-log sandbox E2E requires the configured McpE2E:Sandbox:EnvironmentName to be present and reachable. Configured value: '{configured}'.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings, ["ping-app", "-e", environmentName], cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}
}
