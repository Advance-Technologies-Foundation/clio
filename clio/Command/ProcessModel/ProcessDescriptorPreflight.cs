using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Runs the connection-rule validator over a <c>create-business-process</c> descriptor before it is posted and
/// returns the advisory findings the server does not report on its own.
/// </summary>
/// <remarks>
/// ADVISORY, never a gate, and on the create path only. The server is the gate: its create-path structure check
/// (clio's R1/R2/R3/R15), <c>FlowKindRules</c> on both paths and the platform's own process validation refuse a
/// bad graph before anything is saved, so a clio refusal here would only move the same refusal one round trip
/// earlier - and block the builds where the two rule sets disagree in the server's favour. What the pre-flight
/// adds is the part the server is silent about: R8 (a parallel join behind a choice, which hangs in Running with
/// no error), R7/R9 without a default branch, R13 off an event, and R17.
/// <para>Nothing the server already says is repeated. Every ERROR is excluded, because an error is by the
/// validator's own rule a shape the build refuses, and the server's message for it is the authoritative one;
/// a warning the build also reports carries <see cref="ProcessGraphFinding.ReportedByBuild"/>. A refused build
/// therefore shows the server's refusal and the silent-risk warnings, not two phrasings of one problem.</para>
/// <para>Deliberately NOT wired into <c>modify-business-process</c> or
/// <c>modify-business-process-as-new-version</c>: those take operations, not a graph, and judging the whole
/// process they edit would pin every pre-existing violation on the caller - 37 processes shipped in 7.8.0 carry
/// a start event the whole-graph rules reject. See
/// <c>docs/knowledge/ProcessModel/start-event-arity-is-enforced-on-create-not-modify.md</c>.</para>
/// </remarks>
public interface IProcessDescriptorPreflight {

	/// <summary>
	/// Maps the descriptor's <c>elements[]</c> and <c>flows[]</c> to a graph, validates it, and returns one
	/// warning line per advisory finding. Never throws and never refuses.
	/// </summary>
	/// <param name="descriptor">The parsed create descriptor, exactly as it will be posted.</param>
	/// <returns>
	/// The warning lines, in validator order; empty when there is nothing to report or when the descriptor
	/// cannot be read as a graph - a shape the server refuses with its own message.
	/// </returns>
	IReadOnlyList<string> CheckCreateDescriptor(JsonObject descriptor);
}

/// <inheritdoc cref="IProcessDescriptorPreflight" />
public sealed class ProcessDescriptorPreflight(IProcessGraphValidator validator) : IProcessDescriptorPreflight {

	/// <summary>Opens every pre-flight warning line, so a caller can tell it from a server warning.</summary>
	internal const string WarningPrefix = "Pre-flight";

	/// <inheritdoc />
	public IReadOnlyList<string> CheckCreateDescriptor(JsonObject descriptor) {
		ProcessGraph graph = TryMapGraph(descriptor);
		if (graph is null) {
			return [];
		}
		return validator.Validate(graph).Findings
			.Where(finding => finding.Severity == ProcessGraphSeverity.Warning && !finding.ReportedByBuild)
			.Select(finding => $"{WarningPrefix} {finding.RuleId} (advisory: a connection rule the server does "
				+ $"not check; it does not block the build): {finding.Message}")
			.ToList();
	}

	// Null when the descriptor is not a graph this can describe FAITHFULLY. The rule is "read it the way the
	// server reads it, or not at all": validating a re-interpreted graph - an unknown flow kind taken as plain,
	// a non-string condition taken as absent - would answer about a process the caller did not ask for, in the
	// reassuring direction, and the server refuses every one of these shapes with its own message anyway.
	private static ProcessGraph TryMapGraph(JsonObject descriptor) {
		if (descriptor?["elements"] is not JsonArray elements || elements.Count == 0) {
			return null;
		}
		List<ProcessGraphNode> nodes = [];
		// Names are matched the way ProcessGraphBuilder matches them - trimmed and case-INSENSITIVELY, first
		// declaration wins - and every flow endpoint is rewritten to the declared spelling. The validator keys
		// on exact names, so without this a flow the server connects ('start' onto an element named 'Start')
		// vanished from the graph, and a gateway whose default flow was spelled that way read as having none.
		Dictionary<string, string> declaredNames = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonNode entry in elements) {
			if (entry is not JsonObject element
				|| !TryReadString(element, "name", out string name)
				|| !TryReadString(element, "type", out string type)
				|| !TryReadString(element, "userTaskName", out string userTaskName)) {
				return null;
			}
			name = name?.Trim();
			if (!string.IsNullOrEmpty(name)) {
				declaredNames.TryAdd(name, name);
			}
			nodes.Add(new ProcessGraphNode(name, NodeType(type, userTaskName)));
		}
		List<ProcessGraphEdge> edges = [];
		JsonNode flowsNode = descriptor["flows"];
		if (flowsNode is null) {
			return new ProcessGraph(nodes, edges);
		}
		if (flowsNode is not JsonArray flows) {
			return null;
		}
		foreach (JsonNode entry in flows) {
			ProcessGraphEdge edge = TryMapEdge(entry, declaredNames);
			if (edge is null) {
				return null;
			}
			edges.Add(edge);
		}
		return new ProcessGraph(nodes, edges);
	}

	private static ProcessGraphEdge TryMapEdge(JsonNode entry, IReadOnlyDictionary<string, string> declaredNames) {
		if (entry is not JsonObject flow
			|| !TryReadString(flow, "source", out string source)
			|| !TryReadString(flow, "target", out string target)
			|| !TryReadString(flow, "kind", out string kindToken)
			|| !TryReadString(flow, "condition", out string condition)
			|| !TryReadStrings(flow, "results", out List<string> results)
			|| !TryParseKind(kindToken, out ProcessFlowKind kind)) {
			return null;
		}
		return new ProcessGraphEdge(Declared(source, declaredNames), Declared(target, declaredNames), kind,
			condition, results);
	}

	// An endpoint that names no element is passed through as written: the validator turns it into R15, an
	// error, which this pre-flight does not show because the server refuses the flow itself.
	private static string Declared(string endpoint, IReadOnlyDictionary<string, string> declaredNames) {
		string trimmed = endpoint?.Trim();
		return trimmed is not null && declaredNames.TryGetValue(trimmed, out string declared) ? declared : trimmed;
	}

	// A generic user task names its schema in userTaskName, and THAT is the element the rules care about:
	// {"type":"userTask","userTaskName":"AddDataUserTask"} is an Add data element for R17 exactly as
	// {"type":"addData"} is. Every other token - dedicated user-task tokens, gateways, events - is already one
	// ManagerMap.ResolveDataId accepts, and it is passed through untouched so the validator classifies it the
	// same way validate-process-graph does.
	private static string NodeType(string type, string userTaskName) =>
		string.Equals(type?.Trim(), "userTask", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(userTaskName)
			? userTaskName.Trim()
			: type;

	// The server's own parse: trimmed, case-insensitive, an omitted kind is a plain sequence flow
	// (FlowKindRules.ParseKind). An UNKNOWN kind is not mapped to anything - see TryMapGraph.
	private static bool TryParseKind(string token, out ProcessFlowKind kind) {
		switch (token?.Trim().ToLowerInvariant()) {
			case null:
			case "":
			case "sequence":
				kind = ProcessFlowKind.Sequence;
				return true;
			case "conditional":
				kind = ProcessFlowKind.Conditional;
				return true;
			case "default":
				kind = ProcessFlowKind.Default;
				return true;
			default:
				kind = default;
				return false;
		}
	}

	// False only for a member that is PRESENT and not a string; an absent or JSON-null member reads as null.
	private static bool TryReadString(JsonObject owner, string key, out string value) {
		value = null;
		JsonNode node = owner[key];
		if (node is null) {
			return true;
		}
		return node is JsonValue scalar && scalar.GetValueKind() == JsonValueKind.String
			&& scalar.TryGetValue(out value);
	}

	private static bool TryReadStrings(JsonObject owner, string key, out List<string> values) {
		values = null;
		JsonNode node = owner[key];
		if (node is null) {
			return true;
		}
		if (node is not JsonArray array) {
			return false;
		}
		values = [];
		foreach (JsonNode item in array) {
			if (item is not JsonValue scalar || scalar.GetValueKind() != JsonValueKind.String
				|| !scalar.TryGetValue(out string text)) {
				return false;
			}
			values.Add(text);
		}
		return true;
	}
}
