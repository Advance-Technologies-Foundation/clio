using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// Validates a planned Creatio business-process graph against the BPMN connection rules (R1–R17),
/// so an AI agent can catch invalid connections before driving the Process Designer. The graph
/// itself is validated in-memory, but the tool first resolves the requested environment and
/// queries its installed packages to enforce that the <c>CrtProcessBuilder</c> package is present.
/// </summary>
[McpServerToolType]
public sealed class ValidateProcessGraphTool {
	internal const string ToolName = "validate-process-graph";

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
	[Description("Validates a planned Creatio business-process graph (nodes by data-id, e.g. startEvent/readDataUserTask/exclusiveGateway/endEvent; edges by flow-kind sequence|conditional|default - an omitted flow-kind is a plain sequence flow, an UNKNOWN one is refused rather than treated as plain) against the BPMN connection rules R1-R17. The graph is validated in-memory, but the tool requires the 'CrtProcessBuilder' package to be installed on the target environment (install it with install-process-builder) (named by environment-name). Returns structured findings (error/warning + ruleId). Call this BEFORE driving the designer. IMPORTANT: a passing graph is NOT necessarily buildable — the rules cover the full BPMN catalog (gateways, conditional/default flows, timers, sub-processes), while create-business-process / modify-business-process build only startEvent/signalStart/endEvent/userTask/sendEmail elements. Flows start plain, and modify turns one into a conditional branch with setFlowCondition - so a conditional branch IS buildable even though a gateway ELEMENT is not; check the buildable slice in get-guidance name=process-modeling before promising a build; get-guidance name=process-formulas for an `expression` mapping source or a conditional-flow condition.")]
	public ValidateProcessGraphResponse Validate([Required] ValidateProcessGraphArgs args) {
		try {
			IRequiredPackageChecker checker = _commandResolver.Resolve<IRequiredPackageChecker>(
				new EnvironmentOptions { Environment = args.EnvironmentName });
			checker.EnsureRequirements(args);


			List<ProcessGraphNode> nodes = (args.Nodes ?? [])
										   .Select(n => new ProcessGraphNode(n.Name, n.Type))
										   .ToList();
			List<ProcessGraphEdge> edges = (args.Edges ?? [])
										   .Select(e =>
											   new ProcessGraphEdge(e.Source, e.Target, ParseFlowKind(e.FlowKind)))
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
	[property: Description("The flows: [{source, target, flow-kind}] where flow-kind is sequence | conditional | default.")]
	List<ProcessGraphEdgeArg> Edges = null
	);

/// <summary>One node argument.</summary>
public sealed record ProcessGraphNodeArg(
	[property: JsonPropertyName("name")] string Name = null,
	[property: JsonPropertyName("type")] string Type = null);

/// <summary>One edge argument.</summary>
public sealed record ProcessGraphEdgeArg(
	[property: JsonPropertyName("source")] string Source = null,
	[property: JsonPropertyName("target")] string Target = null,
	[property: JsonPropertyName("flow-kind")] string FlowKind = null);

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
	/// <c>bool</c> emitted <c>"has-errors": false</c> there, and this tool's description advertises the field
	/// while the shipped prompt tells the agent to resolve every error finding - so a graph that was never
	/// looked at read as a graph with nothing wrong. Absent is the honest answer; branch on <c>success</c> first.
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
