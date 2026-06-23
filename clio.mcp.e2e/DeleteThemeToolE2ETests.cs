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
using FluentAssertions;
using ModelContextProtocol.Client;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the delete-theme MCP tool. Actually deleting a theme requires a live Creatio
/// environment with branding licensing and the CanManageThemes operation, so the hermetic CI-safe assertion
/// is that the real clio MCP server advertises both connection-mode tool names; the live behavior is
/// exercised manually (mirrors the clear-themes-cache flow).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("delete-theme")]
[NonParallelizable]
public sealed class DeleteThemeToolE2ETests {
	[Test]
	[AllureTag(DeleteThemeTool.DeleteThemeByEnvironmentName)]
	[AllureName("delete-theme tools are advertised by the MCP server")]
	[Description("Starts the real clio MCP server and verifies both delete-theme connection-mode tools are advertised.")]
	public async Task DeleteTheme_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(DeleteThemeTool.DeleteThemeByEnvironmentName,
			because: "the MCP server should advertise the environment-name delete-theme tool for the no-code server flow");
		toolNames.Should().Contain(DeleteThemeTool.DeleteThemeByCredentialsToolName,
			because: "the MCP server should advertise the credentials delete-theme tool for unregistered environments");
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
