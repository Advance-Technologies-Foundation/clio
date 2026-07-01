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
/// End-to-end coverage for the update-theme MCP tool. Actually overwriting a theme requires a live Creatio
/// environment with branding licensing and the CanManageThemes operation, so the hermetic CI-safe assertion
/// is that the real clio MCP server advertises both connection-mode tool names; the live behavior is
/// exercised manually (mirrors the list-themes flow).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("update-theme")]
[NonParallelizable]
public sealed class UpdateThemeToolE2ETests {
	[Test]
	[AllureTag(UpdateThemeTool.UpdateThemeByEnvironmentName)]
	[AllureName("update-theme tools are advertised by the MCP server")]
	[Description("Starts the real clio MCP server and verifies both update-theme connection-mode tools are advertised.")]
	public async Task UpdateTheme_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		FeatureE2EGate.SkipIfFeatureDisabled(settings, "theming");
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(UpdateThemeTool.UpdateThemeByEnvironmentName,
			because: "the MCP server should advertise the environment-name update-theme tool for the no-code server flow");
		toolNames.Should().Contain(UpdateThemeTool.UpdateThemeByCredentialsToolName,
			because: "the MCP server should advertise the credentials update-theme tool for unregistered environments");
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
