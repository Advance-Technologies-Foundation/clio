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
/// NOT in CI — run manually. Since the env-scoping fix the tool requires the <c>CrtProcessBuilder</c>
/// package on the named environment, so it is not hermetic: the advertisement and refusal cases run
/// without a Creatio instance, but the happy-path graph validation requires a reachable sandbox
/// environment with the package. The tool itself ships gate-free since go-live (ENG-96132).
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ValidateProcessGraphTool.ToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ValidateProcessGraphToolE2ETests {

	private const string ToolName = ValidateProcessGraphTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies validate-process-graph is discoverable via the get-tool-contract compact index.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph is discoverable on the lazy surface of the clio MCP server")]
	public async Task ValidateProcessGraph_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		// The tool is long-tail: never resident in tools/list — discoverability is asserted through the
		// union of tools/list and the get-tool-contract compact index.
		await using ArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "validate-process-graph ships gate-free since go-live (ENG-96132), so it must be "
				+ "discoverable via the get-tool-contract compact index on default settings");
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
	[Description("Over the real MCP path, the build tokens create-business-process accepts for the data and access-rights elements classify as user tasks rather than unknown types: ManagerMap.ResolveDataId maps 'readData', 'changeData' and 'changeAccessRights', so the graph validates with no UNKNOWN finding. These spellings do not end in 'usertask', so before ENG-92717 each produced a hard validator error on a graph that builds fine. Purely client-side classification \u2014 it needs no Change access rights support in the deployed CrtProcessBuilder package.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph classifies the data and access-rights build tokens as known types")]
	[TestCase("readData")]
	[TestCase("changeData")]
	[TestCase("changeAccessRights")]
	public async Task ValidateProcessGraph_Should_ClassifyBuildTokens_AsKnownTypes(string elementType) {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = environmentName,
			["nodes"] = new[] {
				Node("s", "startEvent"), Node("m", elementType), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s", "m", "sequence"), Edge("m", "e", "sequence")
			}
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: $"validating a graph with a {elementType} node returns a structured payload");
		response.Success.Should().BeTrue(because: "the graph is well formed");
		(response.Findings ?? new List<ValidateProcessGraphFinding>())
			.Where(finding => finding.RuleId == "UNKNOWN")
			.Should().BeEmpty(
				because: $"'{elementType}' is a known build type advertised by create-business-process, so it must "
					+ "not be reported as an unrecognized element type the way it was before the token was mapped");
		response.HasErrors.Should().BeFalse(
			because: $"Start -> {elementType} -> End violates no connection rule once the node type is recognized");
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

	[Test]

	[Description("Over the real MCP path, SEVERAL SIGNAL starts validate clean while a SECOND SIMPLE start is an R3 error (ENG-98559). The cap is per KIND: a process may react to as many triggers as it has signals - the shape PublishDraftToArticle ships with - but only one simple start, which is the manual launch. Both halves travel in one case because the rule is the pair, and a build that relaxed the count for every kind would pass a test that only checked the signal half.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph accepts several signal starts and reports a second simple start")]
	public async Task ValidateProcessGraph_Should_AcceptSeveralSignalStarts_AndReportASecondSimpleStart() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
		Dictionary<string, object?> severalSignalStarts = new() {
			["environment-name"] = environmentName,
			["nodes"] = new[] {
				Node("added", "signalStart"), Node("changed", "signalStart"),
				Node("r", "readDataUserTask"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("added", "r", "sequence"), Edge("changed", "r", "sequence"), Edge("r", "e", "sequence")
			}
		};
		Dictionary<string, object?> twoSimpleStarts = new() {
			["environment-name"] = environmentName,
			["nodes"] = new[] {
				Node("s1", "startEvent"), Node("s2", "startEvent"),
				Node("r", "readDataUserTask"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s1", "r", "sequence"), Edge("s2", "r", "sequence"), Edge("r", "e", "sequence")
			}
		};

		// Act
		CallToolResult signalResult = await CallToolAsync(arrangeContext, severalSignalStarts);
		ValidateProcessGraphResponse signalResponse =
			EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(signalResult);
		CallToolResult simpleResult = await CallToolAsync(arrangeContext, twoSimpleStarts);
		ValidateProcessGraphResponse simpleResponse =
			EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(simpleResult);

		// Assert
		signalResponse.Success.Should().BeTrue(because: "the package is present, so the graph is validated");
		signalResponse.Findings.Should().NotContain(f => f.RuleId == "R3",
			because: "one signal per trigger the process reacts to is the shape the platform itself ships (ENG-98559)");
		signalResponse.HasErrors.Should().BeFalse(
			because: "each start has a single outgoing flow and every node lies on a start-to-end path, so nothing else is wrong with the graph either");
		simpleResponse.Findings.Should().Contain(f => f.RuleId == "R3" && f.Severity == "error",
			because: "two simple starts are two manual launches of one process with nothing to choose between them");
	}

	[Test]
	[Description("Over the real MCP path, an unknown flow-kind is REFUSED by the tool rather than validated as a plain flow. The refusal travels back through the MCP envelope, which is the only place a caller sees it: a silent coercion would return success:true with findings about a graph the caller never described, and nothing in the envelope would say so.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph refuses an unknown flow-kind")]
	public async Task ValidateProcessGraph_Should_RefuseCall_WhenFlowKindIsUnknown() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = environmentName,
			["nodes"] = new[] {
				Node("s", "startEvent"), Node("a", "activityUserTask"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s", "a", "sequence"), Edge("a", "e", "exclusive")
			}
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "'exclusive' is not one of the three flow kinds, and coercing it to a plain flow would "
				+ "answer about a graph the caller did not send");
		response.Error.Should().Contain("exclusive",
			because: "the rejected value has to reach the caller through the MCP envelope to be actionable");
	}

	[Test]
	[Description("Over the real MCP path, an edge's condition BINDS from the wire and reaches the rules. The unit tests construct ProcessGraphEdgeArg positionally in C#, so none of them exercises the JSON binder at all, and the binder skips a member it cannot map in silence - rename or mistype the property and every condition arrives null, with the whole suite green and the tool quietly answering about a graph without conditions. A blank condition is the discriminating value: it is the one condition R13 reports as an ERROR, and its message is unique to it - an omitted condition is reported too since ENG-91853, but as a warning whose text names the build refusal instead. So the error exists if and only if the blank string itself crossed the wire; a dropped key would produce the warning, not this.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph binds an edge condition from the wire")]
	public async Task ValidateProcessGraph_Should_BindEdgeCondition_FromTheWire() {
		// Arrange: two conditional branches off one gateway - one with a blank condition, one with a real
		// one. R13 must name the first and only the first.
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = environmentName,
			["nodes"] = new[] {
				Node("s", "startEvent"), Node("g", "exclusiveGateway"), Node("blank", "activityUserTask"),
				Node("real", "activityUserTask"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s", "g", "sequence"),
				Edge("g", "blank", "conditional", "   "),
				Edge("g", "real", "conditional", "1 > 0"),
				Edge("blank", "e", "sequence"), Edge("real", "e", "sequence")
			}
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(
			because: "the package is present, so the graph is validated and findings are returned");
		response.Findings.Should().Contain(
			f => f.RuleId == "R13" && f.Severity == "error" && f.Message.Contains("empty condition")
				&& f.Message.Contains("'g' -> 'blank'"),
			because: "the platform stores a blank condition as the literal 'true' - a branch that always "
				+ "fires - and the rule can only see that if the blank string itself crossed the wire; a "
				+ "dropped key arrives as null, which R13 reports as a WARNING about the build refusal and never with this text");
		response.Findings.Should().NotContain(
			f => f.RuleId == "R13" && f.Message.Contains("'g' -> 'real'"),
			because: "the sibling carries a real condition, so the VALUE has to survive the crossing and not "
				+ "just the key - a binder that mapped every condition to the empty string would report both");
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

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) =>
		await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName);

	[Test]
	[Description("Over the real MCP path, an edge's RESULTS bind from the wire and silence R13. Same argument as the condition case beside it and the same blind spot: the unit tests construct ProcessGraphEdgeArg positionally in C#, so none of them touches the JSON binder, and the binder skips a member it cannot map in silence. Here the silence is worse than a missing value - a dropped 'results' key arrives null, R13 fires exactly as it did before the field existed, and its warning reads like ordinary advice while telling the caller to do one of two things that DESTROY the branch they planned. The sibling edge in the same graph carries neither predicate and must still be reported, so this cannot pass by R13 having stopped firing altogether.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph binds an edge result selection from the wire")]
	public async Task ValidateProcessGraph_Should_BindEdgeResults_FromTheWire() {
		// Arrange: two conditional branches off one activity - one decided by a result SELECTION, one
		// carrying no predicate at all. R13 must name the second and only the second.
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = await ResolveEnvironmentOrIgnoreAsync();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = environmentName,
			["nodes"] = new[] {
				Node("s", "startEvent"), Node("approve", "activityUserTask"),
				Node("selected", "activityUserTask"), Node("bare", "activityUserTask"), Node("e", "endEvent")
			},
			["edges"] = new[] {
				Edge("s", "approve", "sequence"),
				EdgeWithResults("approve", "selected", "Positive"),
				Edge("approve", "bare", "conditional"),
				Edge("selected", "e", "sequence"), Edge("bare", "e", "sequence")
			}
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeTrue(
			because: "the package is present, so the graph is validated and findings are returned");
		response.Findings.Should().NotContain(
			f => f.RuleId == "R13" && f.Message.Contains("'approve' -> 'selected'"),
			because: "a result selection IS the predicate, so that branch is finished - and the rule can only "
				+ "know it if the array itself crossed the wire; a dropped key arrives null and R13 would "
				+ "warn, sending the caller to a condition the designer will not render or to deleting the branch");
		response.Findings.Should().Contain(
			f => f.RuleId == "R13" && f.Severity == "warning" && f.Message.Contains("'approve' -> 'bare'"),
			because: "the sibling carries neither predicate and still has to be reported - without this the "
				+ "test would pass on a build where R13 had stopped firing at all");
	}

	[Test]
	[Description("ENG-98566: over the real MCP path, an unrecognized argument is named back instead of being dropped. The call goes through CallToolAsync, which sends the WRAPPED {\"args\":{...}} shape - the one McpToolErrorFilter passes through untouched - so this is the exact path on which the key was lost. Needs no Creatio: the guard runs before the environment is resolved.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph names an unrecognized argument instead of dropping it")]
	public async Task ValidateProcessGraph_Should_Name_AnUnrecognizedArgument() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		Dictionary<string, object?> graph = new() {
			["environment-name"] = $"missing-process-graph-env-{Guid.NewGuid():N}",
			// The measured mistake: the key an agent carries over from describe-business-process.
			["process-name"] = "UsrOrder_Handle"
		};

		// Act
		CallToolResult callResult = await CallToolAsync(arrangeContext, graph);
		ValidateProcessGraphResponse response = EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "an argument the tool cannot bind is a caller mistake, not a validated graph");
		response.Error.Should().Contain("process-name",
			because: "naming the offending key is the remedy - the caller cannot otherwise see the drop, and "
				+ "the binder loses it silently on this wrapped path");
		(response.Findings ?? new List<ValidateProcessGraphFinding>()).Should().BeEmpty(
			because: "the tool validated nothing, so a finding here would be a statement about a process it "
				+ "never read - the R3 fabrication this ticket exists for");
	}

	[Test]
	[Description("ENG-98566: over the real MCP path, a call supplying no graph says so instead of answering R3 'Process has no start event.'. The regression this pins is specific: the refusal must not be reachable-but-identical to the unknown-argument one, because the measured defect was two different calls returning a byte-identical response.")]
	[AllureTag(ToolName)]
	[AllureName("validate-process-graph states that no graph was supplied")]
	public async Task ValidateProcessGraph_Should_State_ThatNoGraphWasSupplied() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync();
		string environmentName = $"missing-process-graph-env-{Guid.NewGuid():N}";
		Dictionary<string, object?> noGraph = new() { ["environment-name"] = environmentName };
		Dictionary<string, object?> unknownArgument = new() {
			["environment-name"] = environmentName,
			["process-name"] = "UsrOrder_Handle"
		};

		// Act
		ValidateProcessGraphResponse noGraphResponse = EntitySchemaStructuredResultParser
			.Extract<ValidateProcessGraphResponse>(await CallToolAsync(arrangeContext, noGraph));
		ValidateProcessGraphResponse unknownArgumentResponse = EntitySchemaStructuredResultParser
			.Extract<ValidateProcessGraphResponse>(await CallToolAsync(arrangeContext, unknownArgument));

		// Assert
		noGraphResponse.Success.Should().BeFalse(because: "there is no graph to validate, so nothing succeeded");
		noGraphResponse.Error.Should().Contain("No graph was supplied",
			because: "the caller has to learn that the INPUT was missing, not that their process is broken");
		(noGraphResponse.Findings ?? new List<ValidateProcessGraphFinding>())
			.Should().NotContain(finding => finding.RuleId == "R3",
			because: "R3 over an empty node set is the false statement, and it is the worst one to fabricate - "
				+ "'no start event' reads as a structural defect in the caller's own process");
		noGraphResponse.Error.Should().NotBe(unknownArgumentResponse.Error,
			because: "the measured symptom was that supplying a bad argument and supplying nothing at all "
				+ "produced byte-identical answers; two different mistakes must now read differently");
	}

	private static Dictionary<string, object?> Node(string name, string type) =>
		new() { ["name"] = name, ["type"] = type };

	private static Dictionary<string, object?> Edge(string source, string target, string flowKind) =>
		new() { ["source"] = source, ["target"] = target, ["flow-kind"] = flowKind };

	// The four-argument form exists to put "condition" ON THE WIRE. Every other edge in this fixture is
	// built without it, so nothing here would notice the key being dropped by the binder.
	private static Dictionary<string, object?> Edge(string source, string target, string flowKind,
			string condition) =>
		new() {
			["source"] = source, ["target"] = target, ["flow-kind"] = flowKind, ["condition"] = condition
		};

	// Puts "results" ON THE WIRE. No other edge in this fixture carries it, so nothing else here would
	// notice the binder dropping the key - and a dropped key is INVISIBLE in the reassuring direction:
	// results arrives null, R13 fires exactly as it did before the field existed, and the warning reads
	// like ordinary advice rather than like a defect.
	private static Dictionary<string, object?> EdgeWithResults(string source, string target,
			params string[] results) =>
		new() {
			["source"] = source, ["target"] = target, ["flow-kind"] = "conditional", ["results"] = results
		};

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext arrangeContext, Dictionary<string, object?> graphArgs) {
		// Long-tail tools are never resident in tools/list on the lazy surface, so the availability canary
		// checks the reachable-name union (tools/list + get-tool-contract compact index) instead.
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: $"{ToolName} ships gate-free since go-live (ENG-96132), so a server that hides it is a "
				+ "regression rather than a configuration to skip over");
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
