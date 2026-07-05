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
/// End-to-end coverage for the create-theme MCP tool. Actually creating a theme requires a live Creatio
/// environment with branding licensing and the CanManageThemes operation, so the hermetic CI-safe assertions
/// are that the real clio MCP server advertises create-theme and rejects a camelCase alias with a structured
/// rename hint; the live behavior is exercised manually (mirrors the list-themes flow).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("create-theme")]
[NonParallelizable]
public sealed class CreateThemeToolE2ETests {
	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme tool is advertised by the MCP server")]
	[Description("Starts the real clio MCP server and verifies create-theme is advertised.")]
	public async Task CreateTheme_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(CreateThemeTool.ToolName,
			because: "the MCP server should advertise the create-theme tool for the no-code server flow");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme rejects a camelCase alias with a structured rename hint over the wire")]
	[Description("Calls create-theme through the real clio MCP server with a camelCase environmentName field and verifies the structured rename hint — proving the args wrapper binds and unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	public async Task CreateTheme_Should_Return_RenameHint_When_CamelCase_Alias_Is_Passed_Over_The_Wire() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environmentName"] = "docker_fix2"
				}
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
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
