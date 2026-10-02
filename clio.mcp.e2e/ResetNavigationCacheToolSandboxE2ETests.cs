using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end proof against a reachable sandbox that reset-navigation-cache clears clio's session cache and
/// returns the in-tab call for a stale browser tab. The tool changes no data, so any reachable sandbox fits.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ResetNavigationCacheTool.ToolName)]
[NonParallelizable]
public sealed class ResetNavigationCacheToolSandboxE2ETests : McpContractFixtureBase {

	private const string ToolName = ResetNavigationCacheTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("reset-navigation-cache returns success and the browser-session note")]
	[Description("Calls reset-navigation-cache against the configured sandbox and verifies success:true with a next-step naming the environment's ConfigurationDataService/GetData URL.")]
	public async Task ResetNavigationCache_Should_Succeed_And_Return_NextStep_For_Reachable_Environment() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string environmentName = await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(
			settings,
			"reset-navigation-cache MCP E2E requires a reachable configured sandbox environment. Configure "
			+ "McpE2E:Sandbox:EnvironmentName; configured sandbox environment "
			+ $"'{settings.Sandbox.EnvironmentName}' was absent or not reachable.");
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await Session.CallToolAsync(ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = environmentName }
			},
			cancellationTokenSource.Token);
		ResetNavigationCacheResponse result =
			EntitySchemaStructuredResultParser.Extract<ResetNavigationCacheResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "reset-navigation-cache returns a structured payload, not an MCP-level error");
		result.Success.Should().BeTrue(because: result.Error ?? "the server confirms the reset of clio's session");
		result.NextStep.Should().Contain("ConfigurationDataService/GetData",
			because: "the caller needs the in-tab call, because clio's reset never reaches a browser tab");
	}
}
