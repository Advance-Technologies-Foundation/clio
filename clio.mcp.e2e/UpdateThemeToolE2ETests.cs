using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// End-to-end coverage for the update-theme MCP tool. Actually overwriting a theme requires a live Creatio
/// environment with branding licensing and the CanManageThemes operation, so the hermetic CI-safe assertions
/// are that the real clio MCP server advertises update-theme and binds its args wrapper to a structured
/// validation error; the live behavior is exercised manually (mirrors the list-themes flow).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("update-theme")]
[NonParallelizable]
public sealed class UpdateThemeToolE2ETests {
	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme tool is advertised by the MCP server")]
	[Description("Starts the real clio MCP server and verifies update-theme is advertised.")]
	public async Task UpdateTheme_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(UpdateThemeTool.ToolName,
			because: "the MCP server should advertise the update-theme tool for the no-code server flow");
	}

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme binds the args wrapper and returns a structured validation failure")]
	[Description("Calls update-theme through the real clio MCP server with an empty args object and verifies the structured exit-code-1 error names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task UpdateTheme_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			UpdateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope response = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		response.ExitCode.Should().Be(1,
			because: "a missing environment name is an expected, caller-actionable validation error");
		response.Output.Should().Contain(message =>
			message.Value != null && message.Value.Contains("environment-name is required"),
			because: "the failure must name the exact kebab-case field the caller has to add");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
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
