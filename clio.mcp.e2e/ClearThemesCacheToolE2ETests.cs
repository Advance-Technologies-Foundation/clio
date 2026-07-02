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
/// End-to-end coverage for the clear-themes-cache MCP tool. Actually clearing the theme cache requires a
/// live Creatio environment with branding licensing, so the hermetic CI-safe assertion is that the real
/// clio MCP server advertises both connection-mode tool names; the live behavior is exercised manually
/// (mirrors the destructive clear-redis sandbox flow).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("clear-themes-cache")]
[NonParallelizable]
public sealed class ClearThemesCacheToolE2ETests {
	[Test]
	[AllureTag(ClearThemesCacheTool.ClearThemesCacheByEnvironmentName)]
	[AllureName("clear-themes-cache tools are advertised by the MCP server")]
	[Description("Starts the real clio MCP server and verifies both clear-themes-cache connection-mode tools are advertised.")]
	public async Task ClearThemesCache_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(ClearThemesCacheTool.ClearThemesCacheByEnvironmentName,
			because: "the MCP server should advertise the environment-name clear-themes-cache tool for theme activation");
		toolNames.Should().Contain(ClearThemesCacheTool.ClearThemesCacheByCredentialsToolName,
			because: "the MCP server should advertise the credentials clear-themes-cache tool for unregistered environments");
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
