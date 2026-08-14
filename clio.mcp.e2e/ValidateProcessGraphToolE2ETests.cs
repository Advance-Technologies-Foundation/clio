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
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 5 (ai-business-process-generation) end-to-end coverage for <c>validate-process-graph</c>.
/// NOT in CI — run manually. The tool is feature-toggled (<c>process-designer</c>) and, since
/// the env-scoping fix, requires the <c>CrtProcessBuilder</c> package on the named environment, so
/// it is no longer hermetic: the advertisement and refusal cases run without a Creatio instance, but
/// the happy-path graph validation requires a reachable sandbox environment with the package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ValidateProcessGraphTool.ToolName)]
[NonParallelizable]
[Category(ProcessDesignerE2EGate.CategoryName)]
public sealed class ValidateProcessGraphToolE2ETests {

	private const string ToolName = ValidateProcessGraphTool.ToolName;
	private const string FeatureKey = "process-designer";

	[Test]
	[Description("Starts the real clio MCP server and verifies validate-process-graph is discoverable via the get-tool-contract compact index (requires the feature toggle to be enabled).")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph is discoverable on the lazy surface of the clio MCP server")]
	public async Task ValidateProcessGraph_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		// ArrangeAsync already Assert.Ignores when the process-designer feature is disabled, so this test
		// only runs against a server that registered the gated tool. On the lazy surface even an ENABLED
		// gated tool is never resident in tools/list — discoverability is asserted through the union of
		// tools/list and the get-tool-contract compact index.
		await using ArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "the validate-process-graph tool must be discoverable via the get-tool-contract compact index when the process-designer feature is enabled");
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
	[Description("Over the real MCP path against a reachable environment with CrtProcessBuilder, a valid Start -> Read data -> End graph validates with zero error findings.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph reports a valid graph as having no errors")]
	public async Task ValidateProcessGraph_Should_ReportNoErrors_WhenGraphIsValid() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
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
		response.Success.Should().BeTrue(because: "validating a well-formed graph on an environment with CrtProcessBuilder succeeds");
		response.HasErrors.Should().BeFalse(because: "Start -> Read data -> End violates no connection rule");
	}

	[Test]
	[Description("Over the real MCP path, a sendEmail node classifies as a user task rather than an unknown type: ManagerMap.ResolveDataId maps the 'sendEmail' build token, so the graph validates with no UNKNOWN finding. Purely client-side classification — it needs no sendEmail support in the deployed CrtProcessBuilder package.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph classifies a sendEmail node as a known type")]
	public async Task ValidateProcessGraph_Should_ClassifySendEmail_AsKnownType() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = environmentName,
			["nodes"] = new[] {
				Node("s", "startEvent"), Node("m", "sendEmail"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s", "m", "sequence"), Edge("m", "e", "sequence")
			}
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "validating a graph with a sendEmail node returns a structured payload");
		response.Success.Should().BeTrue(because: "the graph is well formed");
		(response.Findings ?? new List<ValidateProcessGraphFinding>())
			.Where(finding => finding.RuleId == "UNKNOWN")
			.Should().BeEmpty(
				because: "'sendEmail' is a known build type, so it must not be reported as an unrecognized element "
					+ "type the way it was before the token was mapped");
		response.HasErrors.Should().BeFalse(
			because: "Start -> Send email -> End violates no connection rule once the node type is recognized");
	}

	[Test]
	[Description("Over the real MCP path against a reachable environment with CrtProcessBuilder, a start event with an incoming flow surfaces an R1 error finding.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph surfaces an R1 error for a start with an incoming flow")]
	public async Task ValidateProcessGraph_Should_SurfaceR1Error_WhenStartHasIncomingFlow() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
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

	// Ignores on BOTH conditions that make these tests meaningless: no environment configured, and a configured
	// environment that cannot be reached. Checking only the former made an unreachable stand FAIL the fixture
	// instead of skipping it, which is how every other Sandbox fixture here behaves and what the tier's
	// Skipped-not-Failed contract expects — an absent stand is not a product defect, and reporting it as one
	// buries real failures in the same run.
	private static async Task<string> ResolveEnvironmentOrIgnoreAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		// Resolve the clio binary the same way ArrangeAsync does. Without this the reachability probe
		// spawns whatever the raw settings point at, which fails in about three seconds instead of
		// pinging - and a probe that cannot run reads as "environment unreachable", turning a healthy
		// stand into three silent skips. A gate that fails open like that is worse than no gate.
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore($"Configure McpE2E:Sandbox:EnvironmentName (with CrtProcessBuilder installed) to run {ToolName} graph-validation E2E tests.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"{ToolName} graph-validation E2E requires a reachable configured sandbox environment. "
				+ $"'{environmentName}' was not reachable.");
		}

		return environmentName!;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private static Dictionary<string, object?> Node(string name, string type) =>
		new() { ["name"] = name, ["type"] = type };

	private static Dictionary<string, object?> Edge(string source, string target, string flowKind) =>
		new() { ["source"] = source, ["target"] = target, ["flow-kind"] = flowKind };

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext arrangeContext, Dictionary<string, object?> graphArgs) {
		// Gated tools are never resident in tools/list on the lazy surface, so the availability canary
		// checks the reachable-name union (tools/list + get-tool-contract compact index) instead.
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		if (!toolNames.Contains(ToolName)) {
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
		ProcessDesignerE2EGate.SkipIfFeatureDisabled(settings);
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
