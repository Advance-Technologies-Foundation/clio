using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 7 (ai-business-process-generation) end-to-end coverage for <c>process-add-element</c>.
/// NOT in CI — run manually. The advertised-tool test is hermetic; the live build-and-readback test is
/// gated on a reachable forms-auth environment (krestov-test) with a local Chromium.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ProcessAddElementTool.ToolName)]
[NonParallelizable]
public sealed class ProcessAddElementToolE2ETests {

	private const string ToolName = ProcessAddElementTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies process-add-element is advertised (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("process-add-element is advertised by the clio MCP server")]
	public async Task ProcessAddElement_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the process-add-element tool must be discoverable on the real clio MCP server");
	}

	[Test]
	[Description("Live: builds Start -> Read data(Contact) -> End and reads it back via describe-process by caption.")]
	[AllureTag(ToolName)]
	[AllureName("process-add-element builds and saves a Read data process, readable back by caption")]
	public async Task ProcessAddElement_Should_BuildAndSave_ThenBeReadableBack() {
		// Arrange — requires a reachable forms-auth env + a local Chromium.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string caption = $"clio-e2e-pae-{Guid.NewGuid():N}".Substring(0, 24);

		// Act — build.
		CallToolResult buildResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["element-type"] = "read-data",
			["read-object"] = "Contact",
			["process-caption"] = caption
		});

		// Assert — the build call returned a structured envelope reporting success with the caption.
		buildResult.IsError.Should().NotBeTrue(because: "the build call should return a structured envelope");
		string buildJson = JsonSerializer.Serialize(buildResult);
		buildJson.Should().Contain(caption, because: "the saved process carries the deterministic caption we supplied");

		// Act — read it back via describe-process by the same caption.
		CallToolResult describeResult = await CallToolAsync(context, "describe-process", new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-caption"] = caption
		});

		// Assert — the read-back returns the structured element graph.
		describeResult.IsError.Should().NotBeTrue(because: "the just-saved process must be readable back");
		JsonSerializer.Serialize(describeResult).Should().Contain("elements",
			because: "describe-process returns the element graph of the process we just built");
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: $"the {toolName} tool must be advertised before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment && string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (forms-auth, with Chromium available) to run process-add-element MCP E2E.");
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
