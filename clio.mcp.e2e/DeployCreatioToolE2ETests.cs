using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature("deploy-creatio")]
public sealed class DeployCreatioToolE2ETests
{
	private const string ToolName = InstallerCommandTool.DeployCreatioToolName;
	private const string ScheduledMaintenanceMessage =
		"Infrastructure temporarily unavailable due to scheduled maintenance. Please try again later.";

	[Test]
	[Description("Starts the real clio MCP server, discovers deploy-creatio, and verifies the tool metadata advertises destructive behavior plus the required preflight guidance.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy creatio advertises preflight guidance and destructive metadata")]
	[AllureDescription("Uses the real clio MCP server tool discovery payload to verify that deploy-creatio is destructive and instructs agents to run assert-infrastructure and show-passing-infrastructure first.")]
	public async Task DeployCreatio_Should_Advertise_Preflight_Guidance()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		McpClientTool tool = tools.Single(tool => tool.Name == ToolName);

		// Assert
		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for client-side safety policies");
		tool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "deploy-creatio mutates infrastructure and should be marked destructive");
		tool.Description.Should().Contain("assert-infrastructure",
			because: "the deploy-creatio description should tell the agent to review full infrastructure first");
		tool.Description.Should().Contain("show-passing-infrastructure",
			because: "the deploy-creatio description should tell the agent to fetch deployable recommendations second");
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement argsSchema = inputSchema.GetProperty("properties").GetProperty("args");
		argsSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
			["siteName", "zipFile", "sitePort", "dbServerName", "redisServerName"],
			because: "the real MCP server should only advertise the five approved deploy-creatio arguments inside the args wrapper");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes deploy-creatio with an invalid archive path, and verifies that the tool reaches the real command path instead of returning the removed scheduled-maintenance stub.")]
	[AllureTag(ToolName)]
	[AllureName("Deploy creatio reaches the real command path")]
	[AllureDescription("Uses the real clio MCP server to call deploy-creatio with an invalid zip path and verifies that the result is a real command failure rather than the removed scheduled-maintenance response.")]
	public async Task DeployCreatio_Should_Not_Return_Scheduled_Maintenance_Response()
	{
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string missingZipFile = Path.Combine(Path.GetTempPath(), $"missing-creatio-{Guid.NewGuid():N}.zip");

		// Act
		var callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?>
			{
				["siteName"] = $"e2e-{Guid.NewGuid():N}",
				["zipFile"] = missingZipFile,
				["sitePort"] = 5001
			},
			cancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => message.Value ?? string.Empty));

		// Assert
		(callResult.IsError == true || execution.ExitCode != 0).Should().BeTrue(
			because: "an invalid zip path should produce a real deployment failure");
		combinedOutput.Should().NotContain(ScheduledMaintenanceMessage,
			because: "the scheduled-maintenance stub has been removed and the MCP tool should now reach the real command path");
		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "the real command path should produce human-readable diagnostics for an invalid archive path");
	}
}
