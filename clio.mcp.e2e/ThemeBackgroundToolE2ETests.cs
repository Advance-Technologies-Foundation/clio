using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
[AllureFeature("theme-background")]
[NonParallelizable]
public sealed class ThemeBackgroundToolE2ETests : McpContractFixtureBase {

	private async Task<CommandExecutionEnvelope> CallAsync(string toolName, Dictionary<string, object?> args) {
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		CallToolResult callResult = await context.Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		return McpCommandExecutionParser.Extract(callResult);
	}

	[Test]
	[AllureTag(RemoveBackgroundTool.ToolName)]
	[Description("Calls remove-background without a theme through the real MCP server and verifies the structured failure names theme before any environment work.")]
	public async Task RemoveBackground_Should_Require_Theme() {
		CommandExecutionEnvelope response = await CallAsync(RemoveBackgroundTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = "docker_fix2" });

		response.Output.Should().Contain(message => message.Value != null && message.Value.Contains("theme is required"),
			because: "the removal has no target without a theme");
	}

	[Test]
	[AllureTag(SetBackgroundColorTool.ToolName)]
	[Description("Calls set-background-color without a theme through the real MCP server and verifies the structured failure names theme before any environment work.")]
	public async Task SetBackgroundColor_Should_Require_Theme() {
		CommandExecutionEnvelope response = await CallAsync(SetBackgroundColorTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = "docker_fix2", ["color"] = "#0A1B2C" });

		response.Output.Should().Contain(message => message.Value != null && message.Value.Contains("theme is required"),
			because: "a theme color has no target without a theme");
	}
}
