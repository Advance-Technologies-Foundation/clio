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
/// Story 5 (ai-business-process-generation) end-to-end coverage for <c>validate-process-graph</c>.
/// NOT in CI — run manually. Hermetic: the tool is pure in-memory, so no Creatio environment is required.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ValidateProcessGraphTool.ToolName)]
[NonParallelizable]
public sealed class ValidateProcessGraphToolE2ETests {

	private const string ToolName = ValidateProcessGraphTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies validate-process-graph is advertised (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph is advertised by the clio MCP server")]
	public async Task ValidateProcessGraph_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the validate-process-graph tool must be discoverable on the real clio MCP server");
	}

	[Test]
	[Description("Over the real MCP path, a valid Start -> Read data -> End graph validates with zero error findings.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph reports a valid graph as having no errors")]
	public async Task ValidateProcessGraph_Should_ReportNoErrors_WhenGraphIsValid() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		Dictionary<string, object?> graph = new() {
			["nodes"] = new[] {
				Node("s", "startEvent"), Node("r", "readDataUserTask"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s", "r", "sequence"), Edge("r", "e", "sequence")
			}
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a hermetic validation call should return a structured payload");
		response.Success.Should().BeTrue(because: "validating a well-formed graph succeeds");
		response.HasErrors.Should().BeFalse(because: "Start -> Read data -> End violates no connection rule");
	}

	[Test]
	[Description("Over the real MCP path, a start event with an incoming flow surfaces an R1 error finding.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph surfaces an R1 error for a start with an incoming flow")]
	public async Task ValidateProcessGraph_Should_SurfaceR1Error_WhenStartHasIncomingFlow() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		Dictionary<string, object?> graph = new() {
			["nodes"] = new[] {
				Node("s", "startEvent"), Node("a", "activityUserTask"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s", "a", "sequence"), Edge("a", "e", "sequence"), Edge("a", "s", "sequence")
			}
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.HasErrors.Should().BeTrue(because: "a start event with an incoming flow violates R1");
		response.Findings.Should().Contain(f => f.RuleId == "R1" && f.Severity == "error",
			because: "the R1 violation must be reported in the response findings");
	}

	private static Dictionary<string, object?> Node(string id, string type) =>
		new() { ["id"] = id, ["type"] = type };

	private static Dictionary<string, object?> Edge(string source, string target, string flowKind) =>
		new() { ["source"] = source, ["target"] = target, ["flow-kind"] = flowKind };

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext arrangeContext, Dictionary<string, object?> graphArgs) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the validate-process-graph tool must be advertised before the end-to-end call can be executed");
		return await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = graphArgs },
			arrangeContext.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
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
