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
/// End-to-end coverage for the check-theming-access MCP tool. Actually probing rights and licenses requires a
/// live Creatio environment, so the hermetic CI-safe assertions are that the real clio MCP server advertises
/// check-theming-access and binds its args wrapper to a structured validation error; the live behavior is
/// exercised manually (mirrors the list-themes flow).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("check-theming-access")]
[NonParallelizable]
public sealed class CheckThemingAccessToolE2ETests {
	[Test]
	[AllureTag(CheckThemingAccessTool.ToolName)]
	[AllureName("check-theming-access tool is advertised by the MCP server")]
	[Description("Starts the real clio MCP server and verifies check-theming-access is advertised.")]
	public async Task CheckThemingAccess_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(CheckThemingAccessTool.ToolName,
			because: "the MCP server should advertise the check-theming-access tool for the theming precheck");
	}

	[Test]
	[AllureTag(CheckThemingAccessTool.ToolName)]
	[AllureName("check-theming-access binds the args wrapper and returns a structured validation failure")]
	[Description("Calls check-theming-access through the real clio MCP server with an empty args object and verifies the structured kebab-case validation error names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task CheckThemingAccess_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CheckThemingAccessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		ThemingAccessResult result = EntitySchemaStructuredResultParser.Extract<ThemingAccessResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "an access check without an environment name is invalid");
		result.Error.Should().Contain("environment-name",
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
