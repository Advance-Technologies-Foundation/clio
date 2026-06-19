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

[TestFixture]
[AllureNUnit]
[AllureFeature(ExperimentalTool.ToolName)]
[NonParallelizable]
public sealed class ExperimentalToolE2ETests {
	private const string ToolName = ExperimentalTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("experimental lists feature flags with a success exit code through the real MCP server")]
	[Description("Starts the real clio MCP server, invokes experimental with no arguments, and verifies the list path returns a success envelope.")]
	public async Task Tool_ShouldListFeatureFlags_WhenNoArgumentsSupplied() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the experimental MCP tool must be advertised by the real server");
		callResult.IsError.Should().NotBeTrue(
			because: "listing feature flags is a read operation that should return a normal MCP envelope");
		execution.ExitCode.Should().Be(0,
			because: "listing feature flags always succeeds");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("experimental enables and then disables a feature flag through the real MCP server")]
	[Description("Starts the real clio MCP server, enables a feature key and then disables it, verifying both toggles return a success envelope.")]
	public async Task Tool_ShouldToggleFeatureFlag_WhenNameAndEnableSupplied() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		const string featureKey = "e2e-experimental-feature";

		// Act
		CallToolResult enableResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["name"] = featureKey, ["enable"] = true },
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope enableExecution = McpCommandExecutionParser.Extract(enableResult);

		CallToolResult disableResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["name"] = featureKey, ["disable"] = true },
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope disableExecution = McpCommandExecutionParser.Extract(disableResult);

		// Assert
		enableResult.IsError.Should().NotBeTrue(
			because: "enabling a feature flag should return a normal MCP envelope");
		enableExecution.ExitCode.Should().Be(0,
			because: "a valid enable toggle should succeed");
		disableResult.IsError.Should().NotBeTrue(
			because: "disabling a feature flag should return a normal MCP envelope");
		disableExecution.ExitCode.Should().Be(0,
			because: "a valid disable toggle should succeed");
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
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
