using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExperimentalTool.ToolName)]
[NonParallelizable]
public sealed class ExperimentalToolE2ETests : McpContractFixtureBase {
	private const string ToolName = ExperimentalTool.ToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("experimental lists feature flags with a success exit code through the real MCP server")]
	[Description("Starts the real clio MCP server, invokes experimental with no arguments, and verifies the list path returns a success envelope.")]
	public async Task Tool_ShouldListFeatureFlags_WhenNoArgumentsSupplied() {
		// Arrange
		await using var context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
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
		await using var context = Arrange();
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

}
