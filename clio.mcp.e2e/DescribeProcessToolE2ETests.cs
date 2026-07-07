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
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 8 (ai-business-process-generation) end-to-end coverage for <c>describe-business-process</c>.
/// NOT in CI — run manually. The advertised-tool test is hermetic; the read test is gated on a
/// reachable environment with a known process (configured caption).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(DescribeProcessTool.ToolName)]
[NonParallelizable]
public sealed class DescribeProcessToolE2ETests {

	private const string ToolName = DescribeProcessTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies describe-business-process is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process is discoverable on the lazy surface")]
	public async Task DescribeProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Over the real MCP path, describe-business-process returns a structured graph for a known process.")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process returns a structured graph for a known process")]
	public async Task DescribeProcess_Should_ReturnStructuredGraph_ForKnownProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = context.ProcessCode
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a structured envelope should be returned, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("elements",
			because: "describe-business-process returns the structured element graph, not raw metadata");
		callResultJson.Should().Contain("buildType",
			because: "each element carries its round-trippable buildType token — proof of real structured typing, not an echo");
		callResultJson.Should().Contain("flows",
			because: "the structured graph includes the sequence flows between elements");
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the describe-business-process tool must be discoverable via the get-tool-contract compact index before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ProcessDesignerE2EGate.SkipIfFeatureDisabled(settings);
		string? environmentName = settings.Sandbox.EnvironmentName;
		string? processCode = settings.Sandbox.ProcessCode;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName) || string.IsNullOrWhiteSpace(processCode)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName and McpE2E:Sandbox:ProcessCode (a process that exists on the stand) to run describe-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore($"describe-business-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
			}
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName, processCode);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName,
		string? ProcessCode) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
