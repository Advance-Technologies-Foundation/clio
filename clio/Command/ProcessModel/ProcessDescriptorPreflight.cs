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
	/// The warning lines, in validator order and at most <c>20</c> of them plus a count of the rest; empty when
	/// there is nothing to report or when the descriptor is a shape the server refuses before looking at a flow
	/// (unreadable, duplicate names, an element type it cannot build). A single "skipped" line when the graph is
	/// too large to check cheaply or the check itself failed - the build goes ahead either way.
	/// </returns>
	IReadOnlyList<string> CheckCreateDescriptor(JsonObject descriptor);
}

/// <inheritdoc cref="IProcessDescriptorPreflight" />
public sealed class ProcessDescriptorPreflight(IProcessGraphValidator validator) : IProcessDescriptorPreflight {

	/// <summary>Opens every pre-flight warning line, so a caller can tell it from a server warning.</summary>
	internal const string WarningPrefix = "Pre-flight";

	/// <summary>
	/// Above this many elements or flows the pre-flight is skipped. R8 walks backwards from every parallel join
	/// once per incoming branch and compares the branches pairwise per choosing element, so a crafted graph can
	/// make it super-linear - and it runs BEFORE the POST, inside the worker's budget, where a stall would block
	/// the build it promises never to block. Real processes are far below it.
	/// </summary>
	internal const int MaxElements = 500;

	/// <inheritdoc cref="MaxElements" />
	internal const int MaxFlows = 1000;

	/// <summary>At most this many lines are written; the rest are counted in one closing line.</summary>
	internal const int MaxLines = 20;

	/// <summary>A line is cut at this length: element names are echoed from the descriptor without a bound.</summary>
	internal const int MaxLineLength = 600;

	/// <inheritdoc />
	public IReadOnlyList<string> CheckCreateDescriptor(JsonObject descriptor) {
		try {
			if (descriptor?["elements"] is JsonArray { Count: > MaxElements }
				|| descriptor?["flows"] is JsonArray { Count: > MaxFlows }) {
				return [$"{WarningPrefix} skipped: the descriptor has more than {MaxElements} elements or "
					+ $"{MaxFlows} flows, so clio did not run its connection rules over it. The build was not "
					+ "affected; run validate-process-graph on the parts you want checked."];
			}
			ProcessGraph graph = TryMapGraph(descriptor);
			return graph is null ? [] : Render(validator.Validate(graph).Findings);
		} catch (Exception exception) {
			// A bare catch, deliberately and only here: this is the one call on the create path whose failure
			// must NEVER change the outcome, and "the validator never throws" is a property of code this class
			// does not own - it has broken once already (the null-name ArgumentNullException recorded in
			// ProcessGraphValidator.NameTheNameless). A throw here would abort the build before the POST, which
			// is exactly what an advisory check promises not to do. Reported, not swallowed: silence would read
			// as "checked, nothing found".
			return [$"{WarningPrefix} skipped: clio's connection-rule check failed ({exception.GetType().Name}: "
				+ $"{Bounded(exception.Message)}). The build was not affected."];
		}
	}

	private static List<string> Render(IReadOnlyList<ProcessGraphFinding> findings) {
		List<string> lines = findings
			.Where(finding => finding.Severity == ProcessGraphSeverity.Warning && !finding.ReportedByBuild)
			.Select(finding => Bounded($"{WarningPrefix} {finding.RuleId} (advisory: a connection rule the "
				+ $"server does not check; it does not block the build): {finding.Message}"))
			.ToList();
		if (lines.Count <= MaxLines) {
			return lines;
		}
		int hidden = lines.Count - MaxLines;
		return [.. lines.Take(MaxLines),
			$"{WarningPrefix}: {hidden} more advisory finding(s) not shown - run validate-process-graph for the full list."];
	}

	// Control characters become spaces - a name carrying a newline must not start a line that reads as clio's
	// own - and the line is cut, because a descriptor may carry an element name of any length.
	private static string Bounded(string line) {
		string flat = new(line.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
		return flat.Length <= MaxLineLength ? flat : flat[..(MaxLineLength - 1)] + "…";
	}

	// Null when the descriptor is not a graph this can describe FAITHFULLY. The rule is "read it the way the
	// server reads it, or not at all": validating a re-interpreted graph - an unknown flow kind taken as plain,
	// a non-string condition taken as absent - would answer about a process the caller did not ask for, in the
	// reassuring direction, and the server refuses every one of these shapes with its own message anyway.
	// Two more shapes the server refuses BEFORE it looks at a single flow get the same answer: two names that
	// differ only in case (ProcessGraphBuilder refuses them as duplicates, OrdinalIgnoreCase, where the
	// validator's ordinal grouping would not), and an element type the build cannot place (unknown or
	// unbuildable). Flow advice about a graph that cannot be built is noise in front of the refusal.
	private static ProcessGraph TryMapGraph(JsonObject descriptor) {
		if (descriptor?["elements"] is not JsonArray elements || elements.Count == 0) {
			return null;
		}
		List<ProcessGraphNode> nodes = [];
		// Names are matched the way ProcessGraphBuilder matches them - trimmed and case-INSENSITIVELY - and
		// every flow endpoint is rewritten to the declared spelling. The validator keys on exact names, so
		// without this a flow the server connects ('start' onto an element named 'Start') vanished from the
		// graph, and a gateway whose default flow was spelled that way read as having none.
		Dictionary<string, string> declaredNames = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonNode entry in elements) {
			if (entry is not JsonObject element
				|| !TryReadString(element, "name", out string name)
				|| !TryReadString(element, "type", out string type)
				|| !TryReadString(element, "userTaskName", out string userTaskName)) {
				return null;
			}
			name = name?.Trim();
			if (!string.IsNullOrEmpty(name) && !declaredNames.TryAdd(name, name)) {
				return null;
			}
			string nodeType = NodeType(type, userTaskName);
			if (!ManagerMap.IsBuildable(ManagerMap.ResolveDataId(nodeType))) {
				return null;
			}
			nodes.Add(new ProcessGraphNode(name, nodeType));
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

	// A user task names its schema in userTaskName, and THAT is the element the rules care about:
	// {"type":"userTask","userTaskName":"AddDataUserTask"} is an Add data element for R17 exactly as
	// {"type":"addData"} is. The server reads userTaskName for every token its generic user-task handler owns,
	// so the swap applies whenever the token is a user task at all - but only when the NAME is one the
	// validator classifies too. A custom schema ("UsrScoreLead") resolves to Unknown, and swapping it in would
	// turn a working activity into an unrecognised node, with a false "neither a gateway nor an activity" R13
	// and a missed R17 behind it; there the token already says what the rules need - it is a user task.
	private static string NodeType(string type, string userTaskName) =>
		!string.IsNullOrWhiteSpace(userTaskName)
			&& ManagerMap.ResolveDataId(type) == ManagerMap.EventType.UserTask
			&& ManagerMap.ResolveDataId(userTaskName) == ManagerMap.EventType.UserTask
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
