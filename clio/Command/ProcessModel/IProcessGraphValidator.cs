using System.Collections.Generic;

namespace Clio.Command.ProcessModel;

/// <summary>
/// One node in a planned process graph.
/// </summary>
/// <param name="Id">Caller-assigned node identifier (unique within the graph).</param>
/// <param name="Type">The Process Designer element <c>data-id</c> (e.g. <c>startEvent</c>, <c>readDataUserTask</c>, <c>exclusiveGateway</c>).</param>
public sealed record ProcessGraphNode(string Id, string Type);

/// <summary>
/// The kind of a sequence connection between two nodes.
/// </summary>
public enum ProcessFlowKind {
	/// <summary>An unconditional sequence flow.</summary>
	Sequence,
	/// <summary>A conditional flow (activates when its condition is true).</summary>
	Conditional,
	/// <summary>A default flow (the fallback when no sibling conditional activates).</summary>
	Default
}

/// <summary>
/// One directed connection between two nodes in a planned process graph.
/// </summary>
/// <param name="Source">The source node id.</param>
/// <param name="Target">The target node id.</param>
/// <param name="FlowKind">The flow kind.</param>
public sealed record ProcessGraphEdge(string Source, string Target, ProcessFlowKind FlowKind);

/// <summary>
/// A planned process graph: the nodes and the flows between them.
/// </summary>
/// <param name="Nodes">The element nodes.</param>
/// <param name="Edges">The flows.</param>
public sealed record ProcessGraph(IReadOnlyList<ProcessGraphNode> Nodes, IReadOnlyList<ProcessGraphEdge> Edges);

/// <summary>
/// Severity of a validation finding.
/// </summary>
public enum ProcessGraphSeverity {
	/// <summary>A rule violation that the live designer would reject; must be fixed before building.</summary>
	Error,
	/// <summary>An advisory issue; the graph is still buildable but the intent should be confirmed.</summary>
	Warning
}

/// <summary>
/// A single validation finding against one of the connection rules (R1–R17).
/// </summary>
/// <param name="Severity">Whether the finding blocks building or is advisory.</param>
/// <param name="RuleId">The rule identifier (e.g. <c>R1</c>, <c>R14</c>, or <c>UNKNOWN</c> for an unrecognized element type).</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="NodeId">The offending node id, when the finding is about a node.</param>
/// <param name="Edge">The offending edge, when the finding is about a flow.</param>
public sealed record ProcessGraphFinding(
	ProcessGraphSeverity Severity,
	string RuleId,
	string Message,
	string NodeId = null,
	ProcessGraphEdge Edge = null);

/// <summary>
/// The outcome of validating a process graph.
/// </summary>
/// <param name="HasErrors"><see langword="true"/> when at least one <see cref="ProcessGraphSeverity.Error"/> finding exists.</param>
/// <param name="Findings">All findings (errors and warnings), in evaluation order.</param>
public sealed record ProcessGraphValidationResult(bool HasErrors, IReadOnlyList<ProcessGraphFinding> Findings);

/// <summary>
/// Validates a planned process graph against the Creatio BPMN connection rules (R1–R17) in-memory,
/// so an AI agent gets deterministic pre-build feedback before driving the live Process Designer.
/// </summary>
/// <remarks>
/// Node types are classified through <see cref="ManagerMap.ResolveDataId"/> / <see cref="ManagerMap.ResolveRole"/>
/// — the single source of truth — rather than a re-derived taxonomy. The live designer remains the final
/// authority (it flags invalid connections with <c>.djs-validate-outline</c>); this validator is a fast pre-check.
/// The full rule definitions live in <c>spec/ai-business-process-generation/ai-bp-connection-rules.md</c>.
/// </remarks>
public interface IProcessGraphValidator {
	/// <summary>
	/// Validates the supplied graph and returns the findings. Never throws on malformed input
	/// (missing-node edges and unrecognized element types are surfaced as findings).
	/// </summary>
	/// <param name="graph">The planned graph to validate.</param>
	/// <returns>The validation result.</returns>
	ProcessGraphValidationResult Validate(ProcessGraph graph);
}
