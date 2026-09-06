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
		(nodes, edges) = NameTheNameless(nodes, edges, findings);

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

		CheckConditionalFlows(edges, nodeByName, findings);
		CheckSelfLoops(edges, findings);
		CheckParallelJoinDeadlock(nodes, incoming, outgoing, findings);
		CheckReachability(nodes, startNodes, outgoing, incoming, findings);

		bool hasErrors = findings.Any(f => f.Severity == ProcessGraphSeverity.Error);
		return new ProcessGraphValidationResult(hasErrors, findings);
	}

	// A name is a dictionary KEY here, so a null one threw ArgumentNullException straight out of the first
	// ToDictionary and the caller got "Value cannot be null. (Parameter 'key')" and NOT ONE finding for any
	// node in the graph - against this method's documented contract of never throwing on malformed input.
	// The MCP schema marks no field required, so an agent reaches this by omitting one. Naming the nameless
	// keeps every other rule running over the rest of the graph, which is the point: the caller wants the
	// findings for the nodes they did name.
	private static (IReadOnlyList<ProcessGraphNode>, IReadOnlyList<ProcessGraphEdge>) NameTheNameless(
			IReadOnlyList<ProcessGraphNode> nodes, IReadOnlyList<ProcessGraphEdge> edges,
			List<ProcessGraphFinding> findings) {
		const string missing = "(missing)";
		if (nodes.All(node => !string.IsNullOrWhiteSpace(node?.Name))
			&& edges.All(edge => !string.IsNullOrWhiteSpace(edge?.Source) && !string.IsNullOrWhiteSpace(edge?.Target))) {
			return (nodes, edges);
		}
		List<ProcessGraphNode> named = [];
		int unnamed = 0;
		foreach (ProcessGraphNode node in nodes) {
			if (node is null) {
				continue;
			}
			if (!string.IsNullOrWhiteSpace(node.Name)) {
				named.Add(node);
				continue;
			}
			string placeholder = $"(unnamed element {++unnamed})";
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "UNNAMED",
				$"An element has no name ({placeholder}). Every element needs a name: flows reference their "
				+ "endpoints by name, so an unnamed element cannot be connected to anything.", placeholder));
			named.Add(node with { Name = placeholder });
		}
		// A blank endpoint is left as a name no element can have, so the missing-node rule reports it in the
		// ordinary way rather than this method inventing a second vocabulary for the same mistake.
		List<ProcessGraphEdge> connected = edges.Where(edge => edge is not null).Select(edge =>
			string.IsNullOrWhiteSpace(edge.Source) || string.IsNullOrWhiteSpace(edge.Target)
				? edge with {
					Source = string.IsNullOrWhiteSpace(edge.Source) ? missing : edge.Source,
					Target = string.IsNullOrWhiteSpace(edge.Target) ? missing : edge.Target
				}
				: edge).ToList();
		return (named, connected);
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
		CheckDefaultFlowRules(node, eventType, outs, nodeByName, findings);

		// R12 (warning) — multiple outgoing sequence flows from a non-gateway = implicit parallel split.
		if (role != Role.Gateway && outs.Count(o => o.FlowKind == ProcessFlowKind.Sequence) > 1) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R12",
				$"Element '{node.Name}' has multiple outgoing sequence flows (implicit parallel split) — confirm intent.", node.Name));
		}

		CheckStrayBranchBesideACondition(node, outs, findings);
	}

	// R18 (error) — a conditional branch beside TWO flows that have none. The platform synthesizes a gateway
	// for any element that branches, and that gateway's fallback is not the `default` marker: it matches every
	// flow that is not CONDITIONAL and removes exactly ONE of them, then runs the rest. So the second
	// unconditional flow always starts — beside the branch the condition chose, and beside the other
	// unconditional one when no condition matched. R12 does fire on this shape, but as a warning whose text
	// describes an all-plain split, so it says nothing about the decision.
	//
	// An ERROR rather than a warning, unlike R7/R9/R13/R14: those were demoted because the shipped corpus
	// contains the shape they rejected. This one it does not. Of 1711 schemas, 736 sources carry a conditional
	// flow beside an unconditional one — 310 of them not gateways — and ZERO carry two unconditional ones,
	// because connection-utils.ts turns the second connection into a conditional rather than drawing it plain.
	// CrtProcessBuilder refuses to build it as of 1.4.0.64, so a warning here would promise a build that fails.
	private static void CheckStrayBranchBesideACondition(ProcessGraphNode node, List<ProcessGraphEdge> outs,
			List<ProcessGraphFinding> findings) {
		if (!outs.Any(o => o.FlowKind == ProcessFlowKind.Conditional)) {
			return;
		}
		int unconditional = outs.Count(o => o.FlowKind != ProcessFlowKind.Conditional);
		if (unconditional < 2) {
			return;
		}
		findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R18",
			$"Element '{node.Name}' branches on a condition while carrying {unconditional} flows that have "
			+ "none. Only one of those is the fallback — the platform starts the other one as well, beside "
			+ "whichever branch the condition chose. Give it a condition, or remove it.", node.Name));
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
			List<ProcessGraphEdge> outs, IReadOnlyDictionary<string, ProcessGraphNode> nodeByName,
			List<ProcessGraphFinding> findings) {
		List<ProcessGraphEdge> defaults = outs.Where(o => o.FlowKind == ProcessFlowKind.Default).ToList();
		bool hasDefault = defaults.Count > 0;
		bool hasConditional = outs.Any(o => o.FlowKind == ProcessFlowKind.Conditional);

		// R14 — a default flow needs a sibling conditional only where the source actually BRANCHES.
		// Scoped by ARITY, not by element kind, and that scope is the fix rather than a refinement: a
		// CONVERGING or-gateway's single outgoing flow is a default flow by construction, because the
		// designer's allowed-outgoing list for an or-gateway is conditional + default with no plain sequence
		// flow at all. Unscoped, this rule called 45 shipped gateways invalid - 40 exclusive and 5 inclusive,
		// among them BulkFileManagement/DeleteFilesInTable and CaseService/RunSendEmailToCaseGroup. Academy's
		// wording ("a default flow is used when there is at least one conditional flow outgoing from the same
		// process element") simply does not contemplate the shape the designer itself produces.
		// EXEMPT when a plain sibling leads into a GATEWAY. That is not an unexpressed decision, it is the
		// decision living one element further on, and the platform says so explicitly:
		// ProcessSchemaFlowNode.GetOutgoingsDefFlows, with no conditional flow present, recurses into a
		// sequence flow whose target is a gateway and collects THAT gateway's default flows. Without this the
		// arity fix went 45 shipped gateways to 1, not to 0 - CrtLeadOppMgmtApp/LeadDistribution's
		// ReadDataUserTask1 is the one, and it runs.
		bool plainSiblingLeadsToAGateway = outs.Any(edge => edge.FlowKind == ProcessFlowKind.Sequence
			&& nodeByName.TryGetValue(edge.Target, out ProcessGraphNode target)
			&& RoleOf(target) == Role.Gateway);
		if (hasDefault && !hasConditional && outs.Count > 1 && defaults.Count == 1
				&& !plainSiblingLeadsToAGateway) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R14",
				$"Default flow from '{node.Name}' requires at least one sibling conditional flow.", node.Name));
		}

		// R14 — at most ONE default flow per source. The default is "the branch taken when nothing matched",
		// so two make that undecidable; the platform does not refuse it and picks by collection order, which
		// leaves the second one dead metadata that reads like a live branch. Zero sources in the shipped
		// corpus carry two, and the designer keeps the invariant by DEMOTING the previous default when a new
		// one is promoted - a silent edit this validator reports instead of imitating.
		if (defaults.Count > 1) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R14",
				$"Element '{node.Name}' has {defaults.Count} default flows; only one branch can be the one "
				+ "taken when no condition matched.", node.Name));
		}

		if (eventType is not (EventType.ExclusiveGateway or EventType.InclusiveGateway) || outs.Count <= 1) {
			return;
		}
		string ruleId = eventType == EventType.ExclusiveGateway ? "R7" : "R9";

		// R7 / R9 (warning) — a DIVERGING or-gateway's outgoing flows should each say how they are chosen.
		// The mirror of R11, and a WARNING rather than an error for the same reason R14 is arity-scoped and R6
		// is not implemented at all: it describes shipped, running content. Seven or-gateways in the shipped
		// 7.8.0 corpus are diverging and carry a plain sequence flow - Compensation/BonusVisaBaseSubProcess,
		// Compensation/BonusVisaBaseSubProcessCompensation1, CrtOpportunityManagement/Presentation780,
		// LeadFinance/LeadManagementFinance, OldGoogleIntegration/SynchronizeWithGoogleModuleProcess,
		// OpportunityBank/Presentation780Finance and PRMBase/CreateOrUpdatePartnerParamHistory - and they run,
		// because FlowConditionalGateway.GetIsDefSequenceFlow treats ANY outgoing that is not a conditional
		// flow as the default branch. Calling that invalid would repeat the mistake R14 was arity-scoped to
		// undo - a rule that rejects real, shipped, running processes - in a brand new rule, and it is
		// reachable by the ordinary describe-then-validate route rather than only by hand-written input.
		if (outs.Any(o => o.FlowKind == ProcessFlowKind.Sequence)) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, ruleId,
				$"Diverging gateway '{node.Name}' has a plain sequence flow. At run time it is taken as the "
				+ "default branch; say so explicitly with kind 'default', or give it a condition, so the "
				+ "diagram states which branch is the fallback.", node.Name));
		}

		// R7 / R9 (warning) — a diverging or-gateway should have a default flow. Stays a WARNING because 65
		// shipped exclusive gateways deliberately have two conditional flows and no default.
		//
		// The message names what the OPERATOR sees. It used to name MismatchItemsCountException, read out of
		// FlowConditionalGateway.OnVisited - true about the code, and not what anyone finds. A manual run
		// measured both halves in ONE pass: validate-process-graph promised the exception, and the resulting
		// SysProcessLog entry read "None of the conditions were met after the element ... The business process
		// execution has been suspended". Whether the exception is thrown behind that is not the point; it is
		// not the text the reader can search for, and the recorded outcome is SUSPENDED rather than failed.
		//
		// SCOPED OFF a gateway that carries a plain sequence flow, because on that shape the message would be
		// FALSE. FlowConditionalGateway treats any outgoing that is not a conditional flow as the default
		// branch, so nothing stops and the log says nothing - the instance takes the plain flow. Seven shipped
		// diverging or-gateways are in exactly that shape and every one of them is reachable here through the
		// ordinary describe-then-validate route, so this would have promised a run-time failure that cannot
		// happen, seven times over, on the platform's own content. That shape already has its own warning
		// above, which says the useful thing instead: mark it 'default' so the diagram states the fallback.
		bool hasPlainFallback = outs.Any(edge => edge.FlowKind == ProcessFlowKind.Sequence);
		if (!hasDefault && !hasPlainFallback) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, ruleId,
				$"Diverging gateway '{node.Name}' has no default flow: if no condition matches at run time the "
				+ "instance stops there and the process log reads \"None of the conditions were met after the "
				+ $"element '{node.Name}'\". Add a default flow, or confirm the conditions cover every case.",
				node.Name));
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

	// R13 — a conditional flow may originate only from a gateway or an activity, and must carry a condition.
	private static void CheckConditionalFlows(IReadOnlyList<ProcessGraphEdge> edges,
			IReadOnlyDictionary<string, ProcessGraphNode> nodeByName, List<ProcessGraphFinding> findings) {
		foreach (ProcessGraphEdge edge in edges.Where(e => e.FlowKind == ProcessFlowKind.Conditional)) {
			// A WARNING, not an error, and the corpus is why. Measured over 1711 shipped schemas, four
			// conditional flows leave an event: two a start event (CrtBase
			// PushNotificationAboutAppUpdateAvailableProcess, CrtCustomer360AI SaveNewApiKey) and two an
			// intermediate catch signal event. They ship and they run. The designer does not offer the
			// connection, which is why this stays a finding at all - but an ERROR told an agent that the
			// platform's own content is invalid, and CrtProcessBuilder builds it without complaint, so the
			// error also promised a refusal that never comes.
			if (nodeByName.TryGetValue(edge.Source, out ProcessGraphNode source)
					&& RoleOf(source) is not (Role.Gateway or Role.Activity)) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R13",
					$"Conditional flow leaves '{edge.Source}', which is neither a gateway nor an activity. "
					+ "The designer cannot draw that connection, though four shipped flows have it and run.",
					edge.Source, edge));
			}

			// A conditional flow with no condition is NOT an error the platform reports: it substitutes the
			// literal "true", producing a branch that looks conditional and always fires. Re-measured: THREE
			// shipped conditional flows are in that state, and they omit the CI3 key entirely - zero store an
			// empty string. The "7" this comment used to claim was wrong, which matters because the number is
			// the argument for the rule being a warning about a real shape rather than a hypothetical.
			//
			// A NULL condition is the field being omitted on THIS edge and raises nothing: the field is
			// optional, and a caller describing a graph's shape rather than its predicates must not be
			// flooded with findings about a value they never claimed to supply. Only a supplied-but-blank
			// one is the mistake.
			if (edge.Condition is { } condition && condition.Trim().Length == 0) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R13",
					$"Conditional flow '{edge.Source}' -> '{edge.Target}' has an empty condition, which the "
					+ "platform stores as the literal 'true' - a branch that always fires.", edge.Source, edge));
			}
		}
	}

	// R15 — a flow from an element to itself. The designer refuses to DRAW one (canConnectionCreate requires
	// source !== target) while tolerating the three that exist in the shipped corpus on re-save, which is the
	// posture mirrored here: refuse on author, tolerate on read. This tool only ever sees a PLANNED graph, so
	// the read half does not apply to it. At run time a self-looping task re-executes on every completion, and
	// nothing on the diagram shows it, because the layout engine skips self-loops when building adjacency.
	//
	// No null-source guard, and that is a deletion rather than an omission: CheckMissingNodeFlows runs first
	// and its ContainsKey(null) throws, so an edge with a null source cannot reach this loop. That fact is
	// OURS - it lives in this file and would change in our own diff - which is the case where an unreachable
	// guard is dead code rather than insurance. See
	// docs/knowledge/Tests/reachability-not-corpus-absence-decides-whether-a-guard-stays.md. If the ordering
	// in Validate ever changes, this guard comes back in the same commit.
	private static void CheckSelfLoops(IReadOnlyList<ProcessGraphEdge> edges, List<ProcessGraphFinding> findings) {
		foreach (ProcessGraphEdge edge in edges
				.Where(e => string.Equals(e.Source, e.Target, System.StringComparison.Ordinal))) {
			findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R15",
				$"Flow connects '{edge.Source}' to itself. To repeat an element, route the flow back through a "
				+ "gateway that decides whether to repeat it.", edge.Source, edge));
		}
	}

	// Parallel-join deadlock (warning) — a parallel gateway proceeds only when EVERY incoming branch has
	// delivered a token. If two of its incoming branches trace back to a common EXCLUSIVE or inclusive split,
	// only one of them can ever run, and the instance hangs in Running with no exception and no log line -
	// the failure mode with no diagnostic at all, which is why it is worth a warning even though it cannot be
	// proven from the graph alone.
	//
	// Deliberately the minimal no-false-positive form: it fires only on a COMMON or-gateway ancestor of two
	// distinct incoming branches. An inclusive gateway can legitimately activate several branches at once, so
	// this over-warns there; it is a warning, and the alternative - tracing which branches an inclusive
	// gateway's conditions can co-activate - is not decidable from a planned graph.
	private static void CheckParallelJoinDeadlock(IReadOnlyList<ProcessGraphNode> nodes,
			IReadOnlyDictionary<string, List<ProcessGraphEdge>> incoming,
			IReadOnlyDictionary<string, List<ProcessGraphEdge>> outgoing, List<ProcessGraphFinding> findings) {
		// Type only. A converging or-gateway needs no arity filter here and had one until a mutation showed
		// it could not fail: with ONE outgoing flow, every branch that gets behind the gateway came through
		// that same flow, so the divergence test below always finds them overlapping. The filter was a fast
		// path no test could distinguish from the check it guarded, which is the shape of code that rots.
		HashSet<string> orGateways = nodes
			.Where(n => TypeOf(n) is EventType.ExclusiveGateway or EventType.InclusiveGateway)
			.Select(n => n.Name)
			.ToHashSet();
		foreach (ProcessGraphNode node in nodes.Where(n => TypeOf(n) == EventType.ParallelGateway)) {
			List<ProcessGraphEdge> ins = incoming[node.Name];
			if (ins.Count < 2 || orGateways.Count == 0) {
				continue;
			}
			// EDGES, not nodes, and that is the whole rule. Sharing an or-gateway ANCESTOR proves nothing:
			// for a genuine AND fork the two backward walks are identical from the fork upward, so they
			// contain every earlier or-gateway in the process and a node-level intersection warns on almost
			// any parallel section that has a choice somewhere behind it - including a plain retry loop,
			// because the walk goes round the back-edge. What deadlocks is two branches leaving one
			// or-gateway BY DIFFERENT EDGES, so that is what is compared.
			// Seeded with the INBOUND EDGE, not just with its source. Walking from the source alone drops
			// the one edge that is guaranteed to be on the branch, and that is precisely the edge that
			// matters when the or-gateway feeds the join DIRECTLY: xor -default-> and, with the other arm
			// going xor -conditional-> A -> and. The direct branch then projects to the empty set at the
			// gateway, the Count > 0 filter discards it, no pair forms and the commonest hand-authored
			// deadlock of all raised nothing - while the join can never fire whichever way the gateway goes.
			List<HashSet<ProcessGraphEdge>> perBranch = ins
				.Select(edge => TraverseBackwardEdges(edge, incoming))
				.ToList();
			string split = orGateways.FirstOrDefault(gateway => DivergesIntoTwoBranches(gateway, perBranch));
			if (split != null) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R8",
					$"Parallel join '{node.Name}' waits for every incoming branch, but two of them leave the "
					+ $"gateway '{split}' by different flows, and that gateway takes only one. If that is the "
					+ "shape you meant, the instance will hang in Running with no error - use an exclusive "
					+ "gateway to merge instead.",
					node.Name));
			}
		}
	}

	// True when two of the join's branches trace back through DISJOINT outgoing flows of the same gateway -
	// so the gateway picks one of them and the other never delivers its token. Branches that reach the
	// gateway through the same flow (or through all of them, which is what a fully merged choice upstream
	// looks like) are not in conflict and must not warn.
	private static bool DivergesIntoTwoBranches(string gateway, List<HashSet<ProcessGraphEdge>> perBranch) {
		List<HashSet<ProcessGraphEdge>> atGateway = perBranch
			.Select(edges => edges.Where(edge => edge.Source == gateway).ToHashSet())
			.ToList();
		return atGateway.Where(left => left.Count > 0)
			.SelectMany((left, index) => atGateway.Skip(index + 1).Where(right => right.Count > 0)
				.Select(right => !left.Overlaps(right)))
			.Any(disjoint => disjoint);
	}

	// Backward BFS collecting the EDGES walked, not the nodes reached. Terminates on a cycle for the same
	// reason TraverseBackward does - a node is enqueued once - so a retry loop's back-edge is followed once.
	private static HashSet<ProcessGraphEdge> TraverseBackwardEdges(ProcessGraphEdge seed,
			IReadOnlyDictionary<string, List<ProcessGraphEdge>> incoming) {
		HashSet<ProcessGraphEdge> walked = [seed];
		HashSet<string> visited = [seed.Source];
		Queue<string> queue = new([seed.Source]);
		while (queue.Count > 0) {
			string current = queue.Dequeue();
			if (!incoming.TryGetValue(current, out List<ProcessGraphEdge> ins)) {
				continue;
			}
			foreach (ProcessGraphEdge edge in ins) {
				walked.Add(edge);
				if (visited.Add(edge.Source)) {
					queue.Enqueue(edge.Source);
				}
			}
		}
		return walked;
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
