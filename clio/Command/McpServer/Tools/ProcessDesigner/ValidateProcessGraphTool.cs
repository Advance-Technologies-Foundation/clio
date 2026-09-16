using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// Validates a planned Creatio business-process graph against the BPMN connection rules (R1–R20),
/// so an AI agent can catch invalid connections before driving the Process Designer. The graph
/// itself is validated in-memory, but the tool first resolves the requested environment and
/// queries its installed packages to enforce that the <c>CrtProcessBuilder</c> package is present.
/// </summary>
[McpServerToolType]
public sealed class ValidateProcessGraphTool {
	internal const string ToolName = "validate-process-graph";

	/// <summary>The canonical field list echoed back when an unknown argument key is refused (ENG-98566).</summary>
	internal const string ValidArgsHint = "Valid: environment-name, nodes, edges.";

	/// <summary>
	/// Refusal for a call that names no graph. Says WHY there is nothing to validate and points at the tool
	/// that does read a process, because the argument an agent actually reached for was <c>process-name</c>.
	/// </summary>
	internal const string NoGraphSuppliedError =
		"No graph was supplied: 'nodes' is absent or empty, so there is nothing to validate. This is a missing "
		+ "argument, not a finding about a process - validate-process-graph checks a graph you DESCRIBE inline "
		+ "(nodes:[{name,type}], edges:[{source,target,flow-kind}]); it never reads a process out of the "
		+ "environment. To inspect an existing process use describe-business-process.";

	private readonly IProcessGraphValidator _validator;
	private readonly IToolCommandResolver _commandResolver;

	/// <summary>Initializes the tool with the graph validator and the environment-aware command resolver.</summary>
	/// <param name="validator">The connection-rule validator.</param>
	/// <param name="commandResolver">Resolves environment-scoped services (e.g. the package checker) for the requested environment.</param>
	public ValidateProcessGraphTool(IProcessGraphValidator validator, IToolCommandResolver commandResolver) {
		_validator = validator;
		_commandResolver = commandResolver;
	}

	/// <summary>
	/// Validates the supplied node/edge graph and returns the structured findings.
	/// </summary>
	/// <param name="args">The planned graph (nodes by <c>data-id</c>, edges by flow kind).</param>
	/// <returns>The validation response (success flag, has-errors, findings).</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	// Worker despite the graph itself being validated in memory: the method first resolves the requested
	// environment and queries its installed packages through IRequiredPackageChecker, so it CAN block on
	// Creatio before any local work starts.
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	// The FIRST sentence is what the get-tool-contract compact index shows as this tool's one-line
	// purpose, so it is written to BE that line: self-contained and under the 120-character cap, rather
	// than the opening of a longer explanation that the cap then cuts mid-word. See
	// docs/knowledge/McpServer/first-sentence-of-a-description-becomes-the-compact-index-purpose.md
	[Description("Checks a planned Creatio business-process graph against the BPMN connection rules before you build it. "
		+ "Nodes are given by data-id (e.g. startEvent/readDataUserTask/exclusiveGateway/endEvent; edges by flow-kind sequence|conditional|default - an omitted flow-kind is a plain sequence flow, an UNKNOWN one is refused rather than treated as plain; and results[], the activity-result CAPTIONS deciding a conditional branch when its predicate is a selection rather than text - pass it and R13 stops asking for a condition that branch neither needs nor can use) and are checked against rules R1-R20 (R18: a conditional flow may have at most ONE outgoing sibling that carries no condition - the platform drops one of them and runs the other beside the branch the condition chose). The graph is validated in-memory, but the tool requires the 'CrtProcessBuilder' package to be installed on the target environment (install it with install-process-builder) (named by environment-name). Returns structured findings (error/warning + ruleId). nodes is REQUIRED - a call supplying none is REFUSED, not validated as an empty graph, and this tool never reads a process from the environment (use describe-business-process for that). Call this BEFORE driving the designer. IMPORTANT: a passing graph is NOT necessarily buildable — the rules cover the full BPMN catalog (gateways, conditional/default flows, timers, sub-processes), while create-business-process / modify-business-process build only startEvent/signalStart/endEvent/userTask/sendEmail/approval/changeAccessRights elements plus exclusiveGateway and parallelGateway, and all three flow kinds declaratively (flows[].kind with flows[].condition). Still NOT buildable: inclusiveGateway, eventBasedGateway, timer/message starts, intermediate events, sub-processes, and formula and script tasks - so the fork narrows rather than closes. The activity-result dialect IS buildable now (flows[].results / setFlowResults), and a formula on such a connector is REFUSED by the build, so do not plan one; check the buildable slice in get-guidance name=process-modeling before promising a build; get-guidance name=process-formulas for an `expression` mapping source or a conditional-flow condition.")]
	public ValidateProcessGraphResponse Validate([Required] ValidateProcessGraphArgs args) {
		try {
			// ENG-98566. An unknown key inside the WRAPPED payload ({"args":{...}}) never reaches the
			// flat-argument classifier - McpToolErrorFilter leaves an already-wrapped call untouched - so the
			// serializer drops it at bind time and this method would answer about the graph it was NOT given.
			// The overflow bag plus this check is the remedy docs/knowledge/McpServer/
			// mcp-arg-records-swallow-unbound-fields.md prescribes; the bag alone is the failure mode.
			// Checked BEFORE the package requirement so a caller mistake is answered without touching Creatio.
			string argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
				args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArgsHint);
			if (!string.IsNullOrWhiteSpace(argumentError)) {
				return new ValidateProcessGraphResponse { Success = false, Error = argumentError };
			}

			// An empty node set is a MISSING ARGUMENT, not a graph that fails R3. Running the rules over it
			// returned "Process has no start event." - a real rule id and a plausible message about a process
			// the tool never read, which is worse than silence because it reads as authoritative.
			if (args.Nodes is null || args.Nodes.Count == 0) {
				return new ValidateProcessGraphResponse { Success = false, Error = NoGraphSuppliedError };
			}

			IRequiredPackageChecker checker = _commandResolver.Resolve<IRequiredPackageChecker>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
			checker.EnsureRequirements(args);


			List<ProcessGraphNode> nodes = (args.Nodes ?? [])
										   .Select(n => new ProcessGraphNode(n.Name, n.Type))
										   .ToList();
			List<ProcessGraphEdge> edges = (args.Edges ?? [])
										   .Select(e =>
											   new ProcessGraphEdge(e.Source, e.Target, ParseFlowKind(e.FlowKind), e.Condition,
												   e.Results))
										   .ToList();

			ProcessGraphValidationResult result = _validator.Validate(new ProcessGraph(nodes, edges));

			return new ValidateProcessGraphResponse {
				Success = true,
				HasErrors = result.HasErrors,
				Findings = result.Findings.Select(f => new ValidateProcessGraphFinding {
					Severity = f.Severity == ProcessGraphSeverity.Error ? "error" : "warning",
					RuleId = f.RuleId,
					Message = f.Message,
					NodeName = f.NodeName,
					Source = f.Edge?.Source,
					Target = f.Edge?.Target
				}).ToList()
			};
		}
		catch (PackageRequirementException ex) {
			return new ValidateProcessGraphResponse {
				Success = false,
				Error = ex.Message
			};
		}
		catch (InvalidOperationException ex) {
			return new ValidateProcessGraphResponse {
				Success = false,
				Error = ex.Message
			};
		}
		catch (Exception ex) {
			return new ValidateProcessGraphResponse {
				Success = false,
				Error = $"validate-process-graph failed: {ex.Message}. Expected args: " +
					"{\"nodes\":[{\"name\":\"s\",\"type\":\"startEvent\"}],\"edges\":[{\"source\":\"s\",\"target\":\"r\",\"flow-kind\":\"sequence\"}]}."
			};
		}
	}

	/// <summary>
	/// Parses an edge's <c>flow-kind</c>, refusing a value that is not one of the three.
	/// <para>An omitted kind is a plain sequence flow - that is the documented default and the common case.
	/// An unknown one is an ERROR rather than a plain flow: this tool exists to catch a mistake before the
	/// designer is driven, and silently reclassifying <c>"conditionnal"</c> as a plain flow makes exactly the
	/// rules that care about the difference (R7 exclusive-diverge, R13, R14) answer about a different graph -
	/// in the reassuring direction, since a plain flow violates fewer rules than a conditional one.</para>
	/// </summary>
	private static ProcessFlowKind ParseFlowKind(string flowKind) {
		string kind = flowKind?.Trim().ToLowerInvariant();
		switch (kind) {
			case null:
			case "":
			case "sequence":
				return ProcessFlowKind.Sequence;
			case "conditional":
				return ProcessFlowKind.Conditional;
			case "default":
				return ProcessFlowKind.Default;
			default:
				throw new InvalidOperationException(
					$"Unknown 'flow-kind' value '{flowKind}'. Use 'sequence' (or omit it), 'conditional' or "
					+ "'default'. It is refused rather than treated as a plain flow, because the rules that "
					+ "care about the difference would then answer about a graph you did not describe.");
		}
	}
}

/// <summary>Request arguments for <c>validate-process-graph</c>.</summary>
[RequiresPackage(BundledPackages.ProcessBuilderPackageName,
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed record ValidateProcessGraphArgs(
	[property:JsonPropertyName("environment-name")]
	[property:Description("Creatio environment name")]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("nodes")]
	[property: Description("The element nodes: [{name, type}] where name is the element handle (the schema element Name/string code) and type is the catalog data-id (e.g. startEvent, readDataUserTask, exclusiveGateway, endEvent).")]
	List<ProcessGraphNodeArg> Nodes = null,

	[property: JsonPropertyName("edges")]
	[property: Description("The flows: [{source, target, flow-kind, condition}] where flow-kind is sequence | "
		+ "conditional | default. 'condition' is optional and only meaningful on a conditional flow, and all "
		+ "three states are checked: a BLANK one is an R13 error (reached outside the build path the platform "
		+ "stores it as the literal 'true' - a branch that always fires), an OMITTED one an R13 warning (the "
		+ "build path refuses a conditional flow with no condition, so a shape-only check is fine but the "
		+ "build will not happen). Flow ORDER is branch precedence: sibling conditions are evaluated in the "
		+ "order given here and the first true one wins.")]
	List<ProcessGraphEdgeArg> Edges = null
	) {

	/// <summary>
	/// Captures top-level keys the SDK could not bind to a declared argument - most often
	/// <c>process-name</c>, which an agent carries over from <c>describe-business-process</c>. Inspected by
	/// <see cref="ValidateProcessGraphTool.Validate"/>; a bag that is never read is the defect, not the fix.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

/// <summary>One node argument.</summary>
public sealed record ProcessGraphNodeArg(
	[property: JsonPropertyName("name")] string Name = null,
	[property: JsonPropertyName("type")] string Type = null);

/// <summary>One edge argument.</summary>
public sealed record ProcessGraphEdgeArg(
	[property: JsonPropertyName("source")] string Source = null,
	[property: JsonPropertyName("target")] string Target = null,
	[property: JsonPropertyName("flow-kind")] string FlowKind = null,
	[property: JsonPropertyName("condition")] string Condition = null,
	[property: JsonPropertyName("results")] string[] Results = null);

/// <summary>Response from the <c>validate-process-graph</c> MCP tool.</summary>
public sealed class ValidateProcessGraphResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>
	/// Whether the graph violates a rule. NULL - and omitted - when the graph was never validated, which is
	/// every failure path: a missing package, an unknown <c>flow-kind</c>, an unexpected fault. A non-nullable
	/// <c>bool</c> emitted <c>"has-errors": false</c> there - so a graph that was never looked at read as a
	/// graph with nothing wrong. Absent is the honest answer; branch on <c>success</c> first.
	/// <para>An earlier version of this note added that the tool description advertises the field and the
	/// prompt tells the agent to resolve every error finding. Neither is so: <c>has-errors</c> appears in no
	/// <c>[Description]</c> and in no prompt. The argument above does not need them and is left standing on
	/// its own; putting the field into the description is a contract change, not a comment fix.</para>
	/// </summary>
	[JsonPropertyName("has-errors")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? HasErrors { get; init; }

	[JsonPropertyName("findings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<ValidateProcessGraphFinding> Findings { get; init; }
}

/// <summary>One finding in the validation response.</summary>
public sealed class ValidateProcessGraphFinding {
	[JsonPropertyName("severity")]
	public string Severity { get; init; }

	[JsonPropertyName("rule-id")]
	public string RuleId { get; init; }

	[JsonPropertyName("message")]
	public string Message { get; init; }

	[JsonPropertyName("node-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NodeName { get; init; }

	[JsonPropertyName("source")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Source { get; init; }

	[JsonPropertyName("target")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Target { get; init; }
}
