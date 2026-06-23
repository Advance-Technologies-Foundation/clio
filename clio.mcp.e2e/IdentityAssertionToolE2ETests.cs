using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the identity-assertion MCP tools (Identity Service V3 token-exchange flow).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("identity-assertion")]
[NonParallelizable]
public sealed class IdentityAssertionToolE2ETests {

	private static readonly string[] ExpectedToolNames = [
		GetIdentityAssertionTool.ToolName,
		GetIdentityPublicJwkTool.ToolName,
		RegenerateIdentitySigningKeyTool.ToolName,
		CheckAuthCodeFlowTool.ToolName
	];

	[Test]
	[AllureTag("identity-assertion")]
	[AllureName("Identity-assertion MCP tools are advertised by the real server")]
	[AllureDescription("Starts the real clio MCP server and verifies all four identity-assertion tools are advertised.")]
	[Description("Starts the real clio MCP server and verifies all four identity-assertion tools are advertised.")]
	public async Task IdentityTools_Should_Be_Advertised_By_McpServer() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using IdentityArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		IEnumerable<string> advertised = tools.Select(tool => tool.Name);
		advertised.Should().Contain(ExpectedToolNames,
			because: "all four identity-assertion tools must be advertised by the MCP server");
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
		await using IdentityArrangeContext arrangeContext = await ArrangeAsync(settings);

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

	private static async Task<IdentityArrangeContext> ArrangeAsync(McpE2ESettings settings) {
		return await AllureApi.Step("Arrange identity-assertion MCP session", async () => {
			CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
			McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			return new IdentityArrangeContext(session, cancellationTokenSource);
		});
	}

	private static async Task<CommandExecutionEnvelope> ActCheckAuthCodeFlowAsync(
		IdentityArrangeContext arrangeContext, string environmentName) {
		return await AllureApi.Step("Act by invoking check-auth-code-flow through MCP", async () => {
			IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
			tools.Select(tool => tool.Name).Should().Contain(CheckAuthCodeFlowTool.ToolName,
				because: "the check-auth-code-flow MCP tool must be advertised before the end-to-end call can be executed");

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

	private sealed record IdentityArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

}
