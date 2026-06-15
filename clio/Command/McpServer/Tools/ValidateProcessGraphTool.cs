using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Validates a planned Creatio business-process graph against the BPMN connection rules (R1–R17)
/// in-memory, so an AI agent can catch invalid connections before driving the Process Designer.
/// Pure analysis: no environment, no I/O.
/// </summary>
[McpServerToolType]
public sealed class ValidateProcessGraphTool {
	internal const string ToolName = "validate-process-graph";

	private readonly IProcessGraphValidator _validator;

	/// <summary>Initializes the tool with the graph validator resolved from DI.</summary>
	/// <param name="validator">The connection-rule validator.</param>
	public ValidateProcessGraphTool(IProcessGraphValidator validator) {
		_validator = validator;
	}

	/// <summary>
	/// Validates the supplied node/edge graph and returns the structured findings.
	/// </summary>
	/// <param name="args">The planned graph (nodes by <c>data-id</c>, edges by flow kind).</param>
	/// <returns>The validation response (success flag, has-errors, findings).</returns>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Validates a planned Creatio business-process graph (nodes by data-id, e.g. startEvent/readDataUserTask/exclusiveGateway/endEvent; edges by flow-kind sequence|conditional|default) against the BPMN connection rules R1-R17 in-memory. Returns structured findings (error/warning + ruleId). Call this BEFORE driving the designer. No environment, no I/O.")]
	public ValidateProcessGraphResponse Validate([Required] ValidateProcessGraphArgs args) {
		try {
			List<ProcessGraphNode> nodes = (args.Nodes ?? [])
				.Select(n => new ProcessGraphNode(n.Id, n.Type))
				.ToList();
			List<ProcessGraphEdge> edges = (args.Edges ?? [])
				.Select(e => new ProcessGraphEdge(e.Source, e.Target, ParseFlowKind(e.FlowKind)))
				.ToList();

			ProcessGraphValidationResult result = _validator.Validate(new ProcessGraph(nodes, edges));

			return new ValidateProcessGraphResponse {
				Success = true,
				HasErrors = result.HasErrors,
				Findings = result.Findings.Select(f => new ValidateProcessGraphFinding {
					Severity = f.Severity == ProcessGraphSeverity.Error ? "error" : "warning",
					RuleId = f.RuleId,
					Message = f.Message,
					NodeId = f.NodeId,
					Source = f.Edge?.Source,
					Target = f.Edge?.Target
				}).ToList()
			};
		} catch (Exception ex) {
			return new ValidateProcessGraphResponse {
				Success = false,
				Error = $"validate-process-graph failed: {ex.Message}. Expected args: " +
					"{\"nodes\":[{\"id\":\"s\",\"type\":\"startEvent\"}],\"edges\":[{\"source\":\"s\",\"target\":\"r\",\"flow-kind\":\"sequence\"}]}."
			};
		}
	}

	private static ProcessFlowKind ParseFlowKind(string flowKind) => flowKind?.Trim().ToLowerInvariant() switch {
		"conditional" => ProcessFlowKind.Conditional,
		"default" => ProcessFlowKind.Default,
		_ => ProcessFlowKind.Sequence
	};
}

/// <summary>Request arguments for <c>validate-process-graph</c>.</summary>
public sealed record ValidateProcessGraphArgs(
	[property: JsonPropertyName("nodes")]
	[property: Description("The element nodes: [{id, type}] where type is the catalog data-id (e.g. startEvent, readDataUserTask, exclusiveGateway, endEvent).")]
	List<ProcessGraphNodeArg> Nodes = null,
	[property: JsonPropertyName("edges")]
	[property: Description("The flows: [{source, target, flow-kind}] where flow-kind is sequence | conditional | default.")]
	List<ProcessGraphEdgeArg> Edges = null);

/// <summary>One node argument.</summary>
public sealed record ProcessGraphNodeArg(
	[property: JsonPropertyName("id")] string Id = null,
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

	[JsonPropertyName("has-errors")]
	public bool HasErrors { get; init; }

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

	[JsonPropertyName("node-id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NodeId { get; init; }

	[JsonPropertyName("source")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Source { get; init; }

	[JsonPropertyName("target")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Target { get; init; }
}
