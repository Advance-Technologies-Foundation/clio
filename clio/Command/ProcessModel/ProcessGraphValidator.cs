using System.Collections.Generic;
using System.Linq;
using Role = Clio.Command.ProcessModel.ManagerMap.ProcessElementRole;
using EventType = Clio.Command.ProcessModel.ManagerMap.EventType;

namespace Clio.Command.ProcessModel;

/// <inheritdoc cref="IProcessGraphValidator" />
public sealed class ProcessGraphValidator : IProcessGraphValidator {

	/// <inheritdoc />
	public ProcessGraphValidationResult Validate(ProcessGraph graph) {
		List<ProcessGraphFinding> findings = [];
		IReadOnlyList<ProcessGraphNode> nodes = graph?.Nodes ?? [];
		IReadOnlyList<ProcessGraphEdge> edges = graph?.Edges ?? [];

		// Group elements by name once. First occurrence wins for the lookup used downstream; any name that
		// appears more than once is an error — the server doesn't guard duplicates on the build/modify
		// path, where two same-name nodes break name-based flow/describe round-tripping.
		List<IGrouping<string, ProcessGraphNode>> nodeGroups = nodes.GroupBy(node => node.Name).ToList();
		Dictionary<string, ProcessGraphNode> nodeByName = nodeGroups.ToDictionary(group => group.Key, group => group.First());
		findings.AddRange(nodeGroups
			.Where(group => group.Count() > 1)
			.Select(group => new ProcessGraphFinding(ProcessGraphSeverity.Error, "DUP",
				$"Duplicate element name '{group.Key}'. Element names must be unique within a process.", group.Key)));

		CheckUnknownTypes(nodes, findings);
		CheckMissingNodeFlows(edges, nodeByName, findings);

		(Dictionary<string, List<ProcessGraphEdge>> outgoing, Dictionary<string, List<ProcessGraphEdge>> incoming) =
			BuildAdjacency(edges, nodeByName);

		List<ProcessGraphNode> startNodes = nodes.Where(n => RoleOf(n) == Role.Start).ToList();
		CheckStartCount(startNodes, findings);

		foreach (ProcessGraphNode node in nodes) {
			Role role = RoleOf(node);
			EventType eventType = TypeOf(node);
			List<ProcessGraphEdge> outs = outgoing[node.Name];
			List<ProcessGraphEdge> ins = incoming[node.Name];
			CheckStartEndArity(node, role, outs, ins, findings);
			CheckGatewayAndFlowRules(node, eventType, role, outs, nodeByName, findings);
			CheckAddDataChaining(node, outs, nodeByName, findings);
		}

		CheckConditionalFlowOrigins(edges, nodeByName, findings);
		CheckReachability(nodes, startNodes, outgoing, incoming, findings);

		bool hasErrors = findings.Any(f => f.Severity == ProcessGraphSeverity.Error);
		return new ProcessGraphValidationResult(hasErrors, findings);
	}

	private static EventType TypeOf(ProcessGraphNode node) => ManagerMap.ResolveDataId(node.Type);

	private static Role RoleOf(ProcessGraphNode node) => ManagerMap.ResolveRole(TypeOf(node));

	// AC-08 — unrecognized element types are surfaced, never crash the validator.
	private static void CheckUnknownTypes(IReadOnlyList<ProcessGraphNode> nodes, List<ProcessGraphFinding> findings) {
		foreach (ProcessGraphNode node in nodes.Where(n => ManagerMap.ResolveDataId(n.Type) == EventType.Unknown)) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "UNKNOWN",
				$"Element '{node.Name}' has an unrecognized type '{node.Type}'.", node.Name));
		}
	}

	// R15 (missing-node) — every flow needs a valid source and target node (guidance R15, not the R2 end-arity rule).
	private static void CheckMissingNodeFlows(IReadOnlyList<ProcessGraphEdge> edges,
			IReadOnlyDictionary<string, ProcessGraphNode> nodeByName, List<ProcessGraphFinding> findings) {
		foreach (ProcessGraphEdge edge in edges
				.Where(e => !nodeByName.ContainsKey(e.Source) || !nodeByName.ContainsKey(e.Target))) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R15",
				$"Flow references a missing node (source '{edge.Source}', target '{edge.Target}').", null, edge));
		}
	}

	// Adjacency over edges with valid endpoints only. Seeded from the de-duplicated node set (nodeByName) rather than
	// the raw node list, so a graph with duplicate names yields the DUP finding instead of throwing here.
	private static (Dictionary<string, List<ProcessGraphEdge>> Outgoing, Dictionary<string, List<ProcessGraphEdge>> Incoming)
			BuildAdjacency(IReadOnlyList<ProcessGraphEdge> edges,
			IReadOnlyDictionary<string, ProcessGraphNode> nodeByName) {
		Dictionary<string, List<ProcessGraphEdge>> outgoing = nodeByName.Keys.ToDictionary(name => name, _ => new List<ProcessGraphEdge>());
		Dictionary<string, List<ProcessGraphEdge>> incoming = nodeByName.Keys.ToDictionary(name => name, _ => new List<ProcessGraphEdge>());
		foreach (ProcessGraphEdge edge in edges
				.Where(e => nodeByName.ContainsKey(e.Source) && nodeByName.ContainsKey(e.Target))) {
			outgoing[edge.Source].Add(edge);
			incoming[edge.Target].Add(edge);
		}
		return (outgoing, incoming);
	}

	// R3 — exactly one start event.
	private static void CheckStartCount(IReadOnlyList<ProcessGraphNode> startNodes, List<ProcessGraphFinding> findings) {
		if (startNodes.Count == 0) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R3", "Process has no start event."));
			return;
		}
		findings.AddRange(startNodes.Skip(1).Select(extraStart => new ProcessGraphFinding(
			ProcessGraphSeverity.Error, "R3",
			$"Process has more than one start event ('{extraStart.Name}').", extraStart.Name)));
	}

	// R1 — start: no incoming, exactly one outgoing. R2 — end: no outgoing, at least one incoming.
	private static void CheckStartEndArity(ProcessGraphNode node, Role role,
			List<ProcessGraphEdge> outs, List<ProcessGraphEdge> ins, List<ProcessGraphFinding> findings) {
		if (role == Role.Start) {
			if (ins.Count > 0) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R1",
					$"Start event '{node.Name}' must not have an incoming flow.", node.Name));
			}
			if (outs.Count != 1) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R1",
					$"Start event '{node.Name}' must have exactly one outgoing flow.", node.Name));
			}
		}
		if (role == Role.End) {
			if (outs.Count > 0) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R2",
					$"End event '{node.Name}' must not have an outgoing flow.", node.Name));
			}
			if (ins.Count == 0) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R2",
					$"End event '{node.Name}' must have at least one incoming flow.", node.Name));
			}
		}
	}

	// Gateway and flow-kind rules for a single node: R11, R10, R14, R7/R9, R12.
	private static void CheckGatewayAndFlowRules(ProcessGraphNode node, EventType eventType, Role role,
			List<ProcessGraphEdge> outs, IReadOnlyDictionary<string, ProcessGraphNode> nodeByName,
			List<ProcessGraphFinding> findings) {
		// R11 — parallel / event-based gateways carry sequence flows only.
		if (eventType is EventType.ParallelGateway or EventType.EventBasedGateway
			&& outs.Any(o => o.FlowKind != ProcessFlowKind.Sequence)) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R11",
				$"Gateway '{node.Name}' (parallel/event-based) must use plain sequence flows only.", node.Name));
		}

		CheckEventBasedGatewayTargets(node, eventType, outs, nodeByName, findings);
		CheckDefaultFlowRules(node, eventType, outs, findings);

		// R12 (warning) — multiple outgoing sequence flows from a non-gateway = implicit parallel split.
		if (role != Role.Gateway && outs.Count(o => o.FlowKind == ProcessFlowKind.Sequence) > 1) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R12",
				$"Element '{node.Name}' has multiple outgoing sequence flows (implicit parallel split) — confirm intent.", node.Name));
		}
	}

	// R10 — event-based gateway: each outgoing must lead directly to an intermediate catch event.
	private static void CheckEventBasedGatewayTargets(ProcessGraphNode node, EventType eventType,
			List<ProcessGraphEdge> outs, IReadOnlyDictionary<string, ProcessGraphNode> nodeByName,
			List<ProcessGraphFinding> findings) {
		if (eventType != EventType.EventBasedGateway) {
			return;
		}
		foreach (ProcessGraphEdge edge in outs) {
			if (nodeByName.TryGetValue(edge.Target, out ProcessGraphNode target) && RoleOf(target) != Role.Intermediate) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R10",
					$"Event-based gateway '{node.Name}' outgoing must lead to an intermediate catch event; '{edge.Target}' is not.",
					node.Name, edge));
			}
		}
	}

	// R14 — a default flow needs a sibling conditional flow. R7/R9 — a diverging gateway should have a default flow.
	private static void CheckDefaultFlowRules(ProcessGraphNode node, EventType eventType,
			List<ProcessGraphEdge> outs, List<ProcessGraphFinding> findings) {
		bool hasDefault = outs.Any(o => o.FlowKind == ProcessFlowKind.Default);
		bool hasConditional = outs.Any(o => o.FlowKind == ProcessFlowKind.Conditional);

		// R14 — a default flow is legal only with at least one sibling conditional flow.
		if (hasDefault && !hasConditional) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R14",
				$"Default flow from '{node.Name}' requires at least one sibling conditional flow.", node.Name));
		}

		// R7 / R9 (warning) — diverging exclusive/inclusive gateway should have a default flow.
		if (eventType is EventType.ExclusiveGateway or EventType.InclusiveGateway && outs.Count > 1 && !hasDefault) {
			string ruleId = eventType == EventType.ExclusiveGateway ? "R7" : "R9";
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, ruleId,
				$"Diverging gateway '{node.Name}' should have a default flow so the process never dead-ends.", node.Name));
		}
	}

	// R17 (warning) — Add data returns only an Id; chain a Read data before consuming other fields.
	private static void CheckAddDataChaining(ProcessGraphNode node, List<ProcessGraphEdge> outs,
			IReadOnlyDictionary<string, ProcessGraphNode> nodeByName, List<ProcessGraphFinding> findings) {
		if (node.Type != "addDataUserTask") {
			return;
		}
		foreach (ProcessGraphEdge edge in outs) {
			if (nodeByName.TryGetValue(edge.Target, out ProcessGraphNode target)
				&& RoleOf(target) == Role.Activity && target.Type != "readDataUserTask") {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R17",
					$"Add data '{node.Name}' outputs only the new Id; chain a Read data before '{edge.Target}' consumes other fields.",
					edge.Target, edge));
			}
		}
	}

	// R13 — a conditional flow may originate only from a gateway or an activity.
	private static void CheckConditionalFlowOrigins(IReadOnlyList<ProcessGraphEdge> edges,
			IReadOnlyDictionary<string, ProcessGraphNode> nodeByName, List<ProcessGraphFinding> findings) {
		foreach (ProcessGraphEdge edge in edges) {
			if (edge.FlowKind == ProcessFlowKind.Conditional && nodeByName.TryGetValue(edge.Source, out ProcessGraphNode source)) {
				Role sourceRole = RoleOf(source);
				if (sourceRole is not (Role.Gateway or Role.Activity)) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R13",
						$"Conditional flow may originate only from a gateway or an activity (source '{edge.Source}').",
						edge.Source, edge));
				}
			}
		}
	}

	// R15 — reachability: every node must be reachable from a start and able to reach an end.
	private static void CheckReachability(IReadOnlyList<ProcessGraphNode> nodes, IReadOnlyList<ProcessGraphNode> startNodes,
			IReadOnlyDictionary<string, List<ProcessGraphEdge>> outgoing,
			IReadOnlyDictionary<string, List<ProcessGraphEdge>> incoming, List<ProcessGraphFinding> findings) {
		if (startNodes.Count == 0 || nodes.Count == 0) {
			return;
		}
		HashSet<string> reachableFromStart = TraverseForward(startNodes.Select(n => n.Name), outgoing);
		List<string> endNames = nodes.Where(n => RoleOf(n) == Role.End).Select(n => n.Name).ToList();
		HashSet<string> canReachEnd = TraverseBackward(endNames, incoming);
		foreach (ProcessGraphNode node in nodes) {
			Role role = RoleOf(node);
			if (role != Role.Start && !reachableFromStart.Contains(node.Name)) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R15",
					$"Element '{node.Name}' is not reachable from the start event.", node.Name));
			} else if (role != Role.End && !canReachEnd.Contains(node.Name)) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R15",
					$"Element '{node.Name}' cannot reach an end event.", node.Name));
			}
		}
	}

	// Forward BFS from the seed ids following outgoing edge targets.
	private static HashSet<string> TraverseForward(IEnumerable<string> seeds, IReadOnlyDictionary<string, List<ProcessGraphEdge>> outgoing) {
		HashSet<string> visited = [];
		Queue<string> queue = new(seeds);
		foreach (string seed in queue) {
			visited.Add(seed);
		}
		while (queue.Count > 0) {
			string current = queue.Dequeue();
			if (!outgoing.TryGetValue(current, out List<ProcessGraphEdge> outs)) {
				continue;
			}
			foreach (string target in outs.Select(edge => edge.Target).Where(visited.Add)) {
				queue.Enqueue(target);
			}
		}
		return visited;
	}

	// Backward BFS from the seed ids following incoming edge sources.
	private static HashSet<string> TraverseBackward(IEnumerable<string> seeds, IReadOnlyDictionary<string, List<ProcessGraphEdge>> incoming) {
		HashSet<string> visited = [];
		Queue<string> queue = new(seeds);
		foreach (string seed in queue) {
			visited.Add(seed);
		}
		while (queue.Count > 0) {
			string current = queue.Dequeue();
			if (!incoming.TryGetValue(current, out List<ProcessGraphEdge> ins)) {
				continue;
			}
			foreach (string source in ins.Select(edge => edge.Source).Where(visited.Add)) {
				queue.Enqueue(source);
			}
		}
		return visited;
	}
}
