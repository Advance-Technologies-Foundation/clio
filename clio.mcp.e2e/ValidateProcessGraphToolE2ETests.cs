using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 5 (ai-business-process-generation) end-to-end coverage for <c>validate-process-graph</c>.
/// NOT in CI — run manually. The tool is feature-toggled (<c>process-designer</c>) and, since
/// the env-scoping fix, requires the <c>clioprocessbuilder</c> package on the named environment, so
/// it is no longer hermetic: the advertisement and refusal cases run without a Creatio instance, but
/// the happy-path graph validation requires a reachable sandbox environment with the package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ValidateProcessGraphTool.ToolName)]
[NonParallelizable]
public sealed class ValidateProcessGraphToolE2ETests {

	private const string ToolName = ValidateProcessGraphTool.ToolName;
	private const string FeatureKey = "process-designer";

	[Test]
	[Description("Starts the real clio MCP server and verifies validate-process-graph is advertised (requires the feature toggle to be enabled).")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph is advertised by the clio MCP server")]
	public async Task ValidateProcessGraph_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		if (!tools.Select(tool => tool.Name).Contains(ToolName)) {
			Assert.Ignore($"{ToolName} is feature-toggled off. Enable it (clio experimental --name {FeatureKey} --enable) to run this E2E.");
		}
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the validate-process-graph tool must be discoverable on the real clio MCP server when the feature is enabled");
	}

	[Test]
	[Description("Over the real MCP path, an unknown environment name makes validate-process-graph refuse with success=false (env-scoping is enforced end to end).")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph refuses an unknown environment")]
	public async Task ValidateProcessGraph_Should_Refuse_WhenEnvironmentIsUnknown() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string unknownEnvironment = $"missing-process-graph-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> graph = new() {
			["environment-name"] = unknownEnvironment,
			["nodes"] = new[] { Node("s", "startEvent"), Node("e", "endEvent") },
			["edges"] = new[] { Edge("s", "e", "sequence") }
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "an unknown environment cannot be resolved, so the graph must not be validated");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(unknownEnvironment)}|environment.*not.*found|not found|bootstrap)",
			because: "the refusal must explain that the requested environment could not be resolved");
	}

	[Test]
	[Description("Over the real MCP path against a reachable environment with clioprocessbuilder, a valid Start -> Read data -> End graph validates with zero error findings.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph reports a valid graph as having no errors")]
	public async Task ValidateProcessGraph_Should_ReportNoErrors_WhenGraphIsValid() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = ResolveEnvironmentOrIgnore();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = environmentName,
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
		callResult.IsError.Should().NotBeTrue(because: "a validation call against a valid graph should return a structured payload");
		response.Success.Should().BeTrue(because: "validating a well-formed graph on an environment with clioprocessbuilder succeeds");
		response.HasErrors.Should().BeFalse(because: "Start -> Read data -> End violates no connection rule");
	}

	[Test]
	[Description("Over the real MCP path against a reachable environment with clioprocessbuilder, a start event with an incoming flow surfaces an R1 error finding.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph surfaces an R1 error for a start with an incoming flow")]
	public async Task ValidateProcessGraph_Should_SurfaceR1Error_WhenStartHasIncomingFlow() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = ResolveEnvironmentOrIgnore();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = environmentName,
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
		response.Success.Should().BeTrue(because: "the package is present, so the graph is validated and findings are returned");
		response.HasErrors.Should().BeTrue(because: "a start event with an incoming flow violates R1");
		response.Findings.Should().Contain(f => f.RuleId == "R1" && f.Severity == "error",
			because: "the R1 violation must be reported in the response findings");
	}

	private static string ResolveEnvironmentOrIgnore() {
		McpE2ESettings settings = TestConfiguration.Load();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore($"Configure McpE2E:Sandbox:EnvironmentName (with clioprocessbuilder installed) to run {ToolName} graph-validation E2E tests.");
		}
		return environmentName!;
	}

	private static Dictionary<string, object?> Node(string name, string type) =>
		new() { ["name"] = name, ["type"] = type };

	private static Dictionary<string, object?> Edge(string source, string target, string flowKind) =>
		new() { ["source"] = source, ["target"] = target, ["flow-kind"] = flowKind };

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext arrangeContext, Dictionary<string, object?> graphArgs) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		if (!tools.Select(tool => tool.Name).Contains(ToolName)) {
			Assert.Ignore($"{ToolName} is feature-toggled off. Enable it (clio experimental --name {FeatureKey} --enable) to run this E2E.");
		}
		return await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = graphArgs },
			arrangeContext.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		FeatureE2EGate.SkipIfFeatureDisabled(settings, "process-designer");
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
