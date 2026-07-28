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
/// End-to-end tests for the identity-assertion MCP tools (Identity Service V3 token-exchange flow).
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("identity-assertion")]
[NonParallelizable]
public sealed class IdentityAssertionToolE2ETests : McpContractFixtureBase {

	private static readonly string[] ExpectedToolNames = [
		GetIdentityAssertionTool.ToolName,
		GetIdentityPublicJwkTool.ToolName,
		RegenerateIdentitySigningKeyTool.ToolName,
		CheckAuthCodeFlowTool.ToolName
	];

	[Test]
	[AllureTag("identity-assertion")]
	[AllureName("Identity-assertion MCP tools are discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies all four identity-assertion tools are discoverable via the get-tool-contract compact index.")]
	[Description("Starts the real clio MCP server and verifies all four identity-assertion tools are discoverable via the get-tool-contract compact index.")]
	public async Task IdentityTools_Should_Be_Advertised_By_McpServer() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		IReadOnlyCollection<string> reachableToolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		reachableToolNames.Should().Contain(ExpectedToolNames,
			because: "all four identity-assertion MCP tools must be discoverable on the lazy surface (get-tool-contract compact index) even though they are not resident in tools/list");
	}

	[Test]
	[AllureTag(nameof(CheckAuthCodeFlowTool))]
	[AllureName("check-auth-code-flow returns a structured boolean from the sandbox")]
	[AllureDescription("Invokes check-auth-code-flow against the configured sandbox environment and verifies a structured boolean is returned. This endpoint does not depend on the EnableIdentityAssertionIssuer feature.")]
	[Description("Invokes check-auth-code-flow against the configured sandbox environment and verifies a structured boolean is returned.")]
	public async Task CheckAuthCodeFlow_Should_Return_StructuredBoolean_FromSandbox() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		CommandExecutionEnvelope execution = await ActCheckAuthCodeFlowAsync(
			arrangeContext, settings.Sandbox.EnvironmentName!);

		// Assert
		execution.ExitCode.Should().Be(0,
			because: "canUseAuthorizationCodeFlow is readable on any reachable environment and does not require the identity-assertion feature");
		execution.Output.Should().NotBeNull(
			because: "the tool should forward the command log so agents can read the true/false result");
		execution.Output!.Select(message => message.Value)
			.Should().Contain(value => value != null && (value.Contains("true") || value.Contains("false")),
				because: "the command prints the authorization-code-flow flag as a plain boolean");
	}

	private static async Task<CommandExecutionEnvelope> ActCheckAuthCodeFlowAsync(
		ArrangeContext arrangeContext, string environmentName) {
		return await AllureApi.Step("Act by invoking check-auth-code-flow through MCP", async () => {
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(CheckAuthCodeFlowTool.ToolName,
				because: "the check-auth-code-flow MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

			CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
				CheckAuthCodeFlowTool.ToolName,
				new Dictionary<string, object?> {
					["environmentName"] = environmentName,
					["format"] = "text"
				},
				arrangeContext.CancellationTokenSource.Token);

			return McpCommandExecutionParser.Extract(callResult);
		});
	}

}
