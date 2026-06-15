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

		// A node may appear once; tolerate duplicate ids by keeping the first.
		Dictionary<string, ProcessGraphNode> nodeById = new();
		foreach (ProcessGraphNode node in nodes) {
			nodeById.TryAdd(node.Id, node);
		}

		EventType TypeOf(ProcessGraphNode n) => ManagerMap.ResolveDataId(n.Type);
		Role RoleOf(ProcessGraphNode n) => ManagerMap.ResolveRole(TypeOf(n));

		// AC-08 — unrecognized element types are surfaced, never crash the validator.
		foreach (ProcessGraphNode node in nodes) {
			if (ManagerMap.ResolveDataId(node.Type) == EventType.Unknown) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "UNKNOWN",
					$"Element '{node.Id}' has an unrecognized type '{node.Type}'.", node.Id));
			}
		}

		// R2 (missing-node) — a flow must reference existing nodes on both ends.
		foreach (ProcessGraphEdge edge in edges) {
			if (!nodeById.ContainsKey(edge.Source) || !nodeById.ContainsKey(edge.Target)) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R2",
					$"Flow references a missing node (source '{edge.Source}', target '{edge.Target}').", null, edge));
			}
		}

		// Adjacency over edges with valid endpoints only.
		Dictionary<string, List<ProcessGraphEdge>> outgoing = nodes.ToDictionary(n => n.Id, _ => new List<ProcessGraphEdge>());
		Dictionary<string, List<ProcessGraphEdge>> incoming = nodes.ToDictionary(n => n.Id, _ => new List<ProcessGraphEdge>());
		foreach (ProcessGraphEdge edge in edges) {
			if (nodeById.ContainsKey(edge.Source) && nodeById.ContainsKey(edge.Target)) {
				outgoing[edge.Source].Add(edge);
				incoming[edge.Target].Add(edge);
			}
		}

		// R3 — exactly one start event.
		List<ProcessGraphNode> startNodes = nodes.Where(n => RoleOf(n) == Role.Start).ToList();
		if (startNodes.Count == 0) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R3", "Process has no start event."));
		} else {
			foreach (ProcessGraphNode extraStart in startNodes.Skip(1)) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R3",
					$"Process has more than one start event ('{extraStart.Id}').", extraStart.Id));
			}
		}

		foreach (ProcessGraphNode node in nodes) {
			Role role = RoleOf(node);
			EventType eventType = TypeOf(node);
			List<ProcessGraphEdge> outs = outgoing[node.Id];
			List<ProcessGraphEdge> ins = incoming[node.Id];

			// R1 — start: no incoming, exactly one outgoing.
			if (role == Role.Start) {
				if (ins.Count > 0) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R1",
						$"Start event '{node.Id}' must not have an incoming flow.", node.Id));
				}
				if (outs.Count != 1) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R1",
						$"Start event '{node.Id}' must have exactly one outgoing flow.", node.Id));
				}
			}

			// R2 — end: no outgoing, at least one incoming.
			if (role == Role.End) {
				if (outs.Count > 0) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R2",
						$"End event '{node.Id}' must not have an outgoing flow.", node.Id));
				}
				if (ins.Count == 0) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R2",
						$"End event '{node.Id}' must have at least one incoming flow.", node.Id));
				}
			}

			// R11 — parallel / event-based gateways carry sequence flows only.
			if (eventType is EventType.ParallelGateway or EventType.EventBasedGateway
				&& outs.Any(o => o.FlowKind != ProcessFlowKind.Sequence)) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R11",
					$"Gateway '{node.Id}' (parallel/event-based) must use plain sequence flows only.", node.Id));
			}

			// R10 — event-based gateway: each outgoing must lead directly to an intermediate catch event.
			if (eventType == EventType.EventBasedGateway) {
				foreach (ProcessGraphEdge edge in outs) {
					if (nodeById.TryGetValue(edge.Target, out ProcessGraphNode target) && RoleOf(target) != Role.Intermediate) {
						findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R10",
							$"Event-based gateway '{node.Id}' outgoing must lead to an intermediate catch event; '{edge.Target}' is not.",
							node.Id, edge));
					}
				}
			}

			bool hasDefault = outs.Any(o => o.FlowKind == ProcessFlowKind.Default);
			bool hasConditional = outs.Any(o => o.FlowKind == ProcessFlowKind.Conditional);

			// R14 — a default flow is legal only with at least one sibling conditional flow.
			if (hasDefault && !hasConditional) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R14",
					$"Default flow from '{node.Id}' requires at least one sibling conditional flow.", node.Id));
			}

			// R7 / R9 (warning) — diverging exclusive/inclusive gateway should have a default flow.
			if (eventType is EventType.ExclusiveGateway or EventType.InclusiveGateway && outs.Count > 1 && !hasDefault) {
				string ruleId = eventType == EventType.ExclusiveGateway ? "R7" : "R9";
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, ruleId,
					$"Diverging gateway '{node.Id}' should have a default flow so the process never dead-ends.", node.Id));
			}

			// R12 (warning) — multiple outgoing sequence flows from a non-gateway = implicit parallel split.
			if (role != Role.Gateway && outs.Count(o => o.FlowKind == ProcessFlowKind.Sequence) > 1) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R12",
					$"Element '{node.Id}' has multiple outgoing sequence flows (implicit parallel split) — confirm intent.", node.Id));
			}

			// R17 (warning) — Add data returns only an Id; chain a Read data before consuming other fields.
			if (node.Type == "addDataUserTask") {
				foreach (ProcessGraphEdge edge in outs) {
					if (nodeById.TryGetValue(edge.Target, out ProcessGraphNode target)
						&& RoleOf(target) == Role.Activity && target.Type != "readDataUserTask") {
						findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R17",
							$"Add data '{node.Id}' outputs only the new Id; chain a Read data before '{edge.Target}' consumes other fields.",
							edge.Target, edge));
					}
				}
			}
		}

		// R13 — a conditional flow may originate only from a gateway or an activity.
		foreach (ProcessGraphEdge edge in edges) {
			if (edge.FlowKind == ProcessFlowKind.Conditional && nodeById.TryGetValue(edge.Source, out ProcessGraphNode source)) {
				Role sourceRole = RoleOf(source);
				if (sourceRole is not (Role.Gateway or Role.Activity)) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R13",
						$"Conditional flow may originate only from a gateway or an activity (source '{edge.Source}').",
						edge.Source, edge));
				}
			}
		}

		// R15 — reachability: every node must be reachable from a start and able to reach an end.
		if (startNodes.Count > 0 && nodes.Count > 0) {
			HashSet<string> reachableFromStart = TraverseForward(startNodes.Select(n => n.Id), outgoing);
			List<string> endIds = nodes.Where(n => RoleOf(n) == Role.End).Select(n => n.Id).ToList();
			HashSet<string> canReachEnd = TraverseBackward(endIds, incoming);
			foreach (ProcessGraphNode node in nodes) {
				Role role = RoleOf(node);
				if (role != Role.Start && !reachableFromStart.Contains(node.Id)) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R15",
						$"Element '{node.Id}' is not reachable from the start event.", node.Id));
				} else if (role != Role.End && !canReachEnd.Contains(node.Id)) {
					findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R15",
						$"Element '{node.Id}' cannot reach an end event.", node.Id));
				}
			}
		}

		bool hasErrors = findings.Any(f => f.Severity == ProcessGraphSeverity.Error);
		return new ProcessGraphValidationResult(hasErrors, findings);
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
			foreach (ProcessGraphEdge edge in outs) {
				if (visited.Add(edge.Target)) {
					queue.Enqueue(edge.Target);
				}
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
			foreach (ProcessGraphEdge edge in ins) {
				if (visited.Add(edge.Source)) {
					queue.Enqueue(edge.Source);
				}
			}
		}
		return visited;
	}
}
