using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 8 (ai-business-process-generation) end-to-end coverage for <c>describe-process</c>.
/// NOT in CI — run manually. The advertised-tool test is hermetic; the read test is gated on a
/// reachable environment with a known process (configured caption).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(DescribeProcessTool.ToolName)]
[NonParallelizable]
public sealed class DescribeProcessToolE2ETests {

	private const string ToolName = DescribeProcessTool.ToolName;

	// A process expected to exist on the configured sandbox (the Read-data PoC process). Adjust per env.
	private const string KnownProcessCaption = "AI PoC Read Contact";

	[Test]
	[Description("Starts the real clio MCP server and verifies describe-process is advertised (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("describe-process is advertised by the clio MCP server")]
	public async Task DescribeProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the describe-process tool must be discoverable on the real clio MCP server");
	}

	[Test]
	[Description("Over the real MCP path, describe-process returns a structured graph for a known process.")]
	[AllureTag(ToolName)]
	[AllureName("describe-process returns a structured graph for a known process")]
	public async Task DescribeProcess_Should_ReturnStructuredGraph_ForKnownProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-caption"] = KnownProcessCaption
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a structured envelope should be returned, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("elements",
			because: "describe-process returns the structured element graph (run against an environment that has the process)");
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the describe-process tool must be advertised before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string environmentName = settings.Sandbox.EnvironmentName ?? string.Empty;
		if (requireReachableEnvironment && string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the known process) to run describe-process MCP E2E.");
		}
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
