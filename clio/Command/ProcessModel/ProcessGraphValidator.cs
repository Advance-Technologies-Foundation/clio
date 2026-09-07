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
	// The parameters are annotated NULLABLE deliberately, and it is the fix for a real defect rather than a
	// concession to an analyser: `{"elements":[null]}` deserializes to a list CONTAINING null, so the old
	// non-null signature was a lie about what this method accepts, and a static analyser reading it called
	// the guards unnecessary. Removing them on that advice was measured: NullReferenceException on both the
	// element and the flow path, i.e. a validator that throws instead of reporting, out of the one method
	// whose contract is that it never does. Pinned by
	// Validate_ShouldReportAndKeepGoing_WhenAnElementEntryIsNull and ...WhenAFlowEntryIsNull, which did not
	// exist before - the existing null-NAME test covers a different input, which is why the deletion looked
	// safe.
	private static (IReadOnlyList<ProcessGraphNode>, IReadOnlyList<ProcessGraphEdge>) NameTheNameless(
			IReadOnlyList<ProcessGraphNode?> nodes, IReadOnlyList<ProcessGraphEdge?> edges,
			List<ProcessGraphFinding> findings) {
		bool everyNodeNamed = nodes.All(node => node is not null && !string.IsNullOrWhiteSpace(node.Name));
		bool everyEdgeConnected = edges.All(edge =>
			edge is not null && !string.IsNullOrWhiteSpace(edge.Source) && !string.IsNullOrWhiteSpace(edge.Target));
		if (everyNodeNamed && everyEdgeConnected) {
			// The ORIGINAL lists, not copies: this is the shape almost every call has, and it is the reason
			// the null handling below costs nothing on it.
			return (nodes, edges);
		}
		return (NameUnnamedElements(nodes, findings), NameBlankEndpoints(edges));
	}

	// A null ENTRY is dropped rather than reported: there is no element to report about, and inventing a
	// finding for one would describe the caller's serializer rather than their graph. A null NAME on a real
	// element IS reported, because that element exists and cannot be referenced.
	private static List<ProcessGraphNode> NameUnnamedElements(IReadOnlyList<ProcessGraphNode?> nodes,
			List<ProcessGraphFinding> findings) {
		List<ProcessGraphNode> named = [];
		int unnamed = 0;
		foreach (ProcessGraphNode? node in nodes) {
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
		return named;
	}

	// A blank endpoint is left as a name no element PLAUSIBLY has, so the missing-node rule reports it in
	// the ordinary way rather than this method inventing a second vocabulary for the same mistake. Not a
	// name no element CAN have, which is what this comment used to claim: a node literally named
	// "(missing source)" makes the placeholder resolve, and the caller is then told about that node's flow
	// arity instead of about the endpoint they forgot. Pre-existing - the single `(missing)` literal had it
	// identically - and left alone because the alternative is a reserved-name check on every node to buy a
	// better message for a caller who named an element after this file's placeholder.
	private static List<ProcessGraphEdge> NameBlankEndpoints(IReadOnlyList<ProcessGraphEdge?> edges) {
		// TWO placeholders, not one, and the difference is a fabricated finding. `{"edges":[{}]}` is
		// reachable - no field on the wire type is required - and with a single literal both endpoints
		// became the same name, so CheckSelfLoops reported "Flow connects '(missing)' to itself. To repeat
		// an element, route the flow back through a gateway..." on an edge that connects nothing at all.
		// Remediation about repeating an element, for a caller who forgot both endpoints.
		const string missingSource = "(missing source)";
		const string missingTarget = "(missing target)";
		List<ProcessGraphEdge> connected = [];
		foreach (ProcessGraphEdge? edge in edges) {
			if (edge is null) {
				continue;
			}
			bool blankSource = string.IsNullOrWhiteSpace(edge.Source);
			bool blankTarget = string.IsNullOrWhiteSpace(edge.Target);
			if (!blankSource && !blankTarget) {
				connected.Add(edge);
				continue;
			}
			connected.Add(edge with {
				Source = blankSource ? missingSource : edge.Source,
				Target = blankTarget ? missingTarget : edge.Target
			});
		}
		return connected;
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
	// unconditional one when no condition matched. Nothing else reports it: R12 counts only flows whose kind
	// is SEQUENCE and needs more than one, so it fires on `[conditional, sequence, sequence]` - as a warning
	// whose text describes an all-plain split, saying nothing about the decision - and on
	// `[conditional, default, sequence]` it does not fire at all. That second shape is the one raised in
	// review as a false positive, and it is the shape where R18 is the ONLY finding between the author and a
	// silent double start.
	//
	// UNCONDITIONAL here means "not conditional", so an explicitly declared `default` counts. That is
	// deliberate and it is what the runtime does - GetIsDefSequenceFlow matches every non-conditional flow,
	// default and plain alike - but the corpus sentence below was originally written about a second flow
	// drawn PLAIN, which is a narrower predicate than the code's. Re-measured over 1711 schemas under both
	// readings: >=1 conditional with >=2 non-conditional siblings is ZERO, >=2 plain siblings is ZERO, and
	// the reviewer's `[conditional, default, sequence]` shape specifically is ZERO. So the severity does not
	// depend on which reading you take, and it was the sentence that did not match the code.
	//
	// An ERROR rather than a warning, unlike R7/R9/R13/R14: those were demoted because the shipped corpus
	// contains the shape they rejected. This one it does not. Of 1711 schemas, 736 sources carry a conditional
	// flow beside ONE unconditional flow — 310 of them not gateways — and zero carry two, because
	// connection-utils.ts turns the second connection into a conditional rather than drawing it plain.
	// CrtProcessBuilder refuses to build it as of 1.4.0.64, so a warning here would promise a build that
	// fails. Off a GATEWAY the shape cannot arise at all: the first unconditional flow becomes the
	// default and a second is refused outright, which is why this rule reads outs[] rather than the
	// element's kind.
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
			+ "whichever branch the condition chose. Marking one of them 'default' does not settle which: "
			+ "the runtime never reads that marker (GetIsDefSequenceFlow matches every non-conditional flow, "
			+ "default and plain alike) and drops one by declaration order. Give the extra flow a condition, "
			+ "or remove it.", node.Name));
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

	// R13 — a conditional flow may originate only from a gateway or an activity; a BLANK condition is an
	// error, an OMITTED one a warning.
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
			// literal "true", producing a branch that looks conditional and always fires.
			//
			// The corpus census, because two probes got this wrong in opposite directions and the number
			// decides the severity. Over 1711 schemas, 1367 shipped ProcessSchemaConditionalFlow:
			//
			//     CI3 a real expression                    1023
			//     CI3 absent or the string "null"           344   <- 3 absent, 341 the literal "null"
			//     CI3 an EMPTY string                          0
			//
			// A probe that tested only for a missing key returned 3 and could not have returned 344. But 344
			// is not "carries no condition" either, and that is the half a probe stopping at CI3 cannot see:
			// GV2 (ProcessSchemaConditionalFlow.ProcessActivitiesSelectedResultsPropertyName) holds the
			// ACTIVITY-RESULT set, and ConditionalSequenceFlow.CheckCondition dispatches on
			// ResultParameterName - it never evaluates an expression. Splitting the 344 by GV2 entry count:
			//
			//     GV2 has entries (activity-result branch)   337   condition stored as a result set
			//     GV2 empty too (nothing decides it)           7   RemoveSequenceFlowsTestProcess,
			//                                                      UsrNonValidSubProcess, RND30540... - test
			//                                                      schemas, one named NonValid
			//
			// The split is exact: every conditional flow carries a formula OR a result set, never both and
			// never neither, except those 7. So the demotion rule - a shape the corpus contains in bulk is
			// not an error - does not reach this rule at all, and the warning stands on 7 rather than 344.
			//
			// What the 337 DO cost: this tool's edge carries `condition` and nothing else, so an
			// activity-result flow read back by describe-then-validate arrives here indistinguishable from a
			// bare one and warns. The finding is still true for it - clio cannot build an activity-result
			// condition either - but the REMEDIATION would destroy the branch, so the message names the case.
			//
			// BLANK is an error; OMITTED is a WARNING, and the split is deliberate rather than tidy.
			//
			// Omitted used to be SILENT, and that was a documented contract ("omitted is silent" in
			// McpCapabilityMap) with a test behind it: omitting an optional field is not the same as
			// supplying an empty one. What that missed is that `EnsureConditionMatchesKind` REFUSES a
			// conditional flow with no condition, so silence recreated the validate-says-clean /
			// build-refuses fork this ticket exists to close - and three surfaces added by this same ticket
			// call the shape dangerous.
			//
			// The rule this ticket used elsewhere - "error iff the builder refuses" - does NOT settle this
			// split, and it is worth being exact because it looks as though it does. FlowKindRules
			// EnsureConditionMatchesKind tests `!string.IsNullOrWhiteSpace(condition)`, so the builder
			// refuses BLANK and OMITTED alike; by that rule both would be errors. What separates them is
			// whether the shape has a legitimate reading BEFORE any predicate exists. Omission does: this
			// tool checks a PLAN, its own description says a passing graph is not necessarily buildable, and
			// `condition` is optional on the wire precisely so a caller can check a graph's SHAPE first - an
			// error there is a false block on the tool's primary use, while a warning tells the caller what
			// the build will do and blocks nothing. Whitespace does not: nobody types "   " while deferring
			// predicates, so it is a value the caller believes in, and it is the one of the two that ALSO
			// has a consequence past the build - reached through the designer or a direct save, the platform
			// substitutes the literal `true`, and the branch always fires with nothing to show it.
			bool blankCondition = edge.Condition is { } supplied && supplied.Trim().Length == 0;
			if (blankCondition) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R13",
					$"Conditional flow '{edge.Source}' -> '{edge.Target}' has an empty condition. The BUILD "
					+ "path refuses it, and reached any other way the platform stores it as the literal "
					+ "'true' - a branch that always fires. Give it a condition, or pass 'true' explicitly "
					+ "if a branch that always fires is what you mean.", edge.Source, edge));
			} else if (edge.Condition is null) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R13",
					$"Conditional flow '{edge.Source}' -> '{edge.Target}' carries no condition. That is fine "
					+ "for checking a graph's shape, but the BUILD path refuses it - give it a condition "
					+ "before you build, or make the flow 'sequence'. Unless this graph was READ BACK and the "
					+ "flow branches on the preceding activity's RESULT: 337 shipped flows do, their condition "
					+ "is a result set rather than text, and neither fix above applies - describe-business-"
					+ "process reports branchesOnActivityResult for those.", edge.Source, edge));
			}
		}
	}

	// R15 — a flow from an element to itself. The designer refuses to DRAW one (canConnectionCreate requires
	// source !== target) while tolerating the three that exist in the shipped corpus on re-save, which is the
	// posture mirrored here: refuse on author, tolerate on read. This tool only ever sees a PLANNED graph, so
	// the read half does not apply to it. At run time a self-looping task re-executes on every completion, and
	// nothing on the diagram shows it, because the layout engine skips self-loops when building adjacency.
	//
	// No null-source guard, and that is a deletion rather than an omission - but the fact that makes it dead
	// is no longer the one this comment first named. It said "CheckMissingNodeFlows runs first and its
	// ContainsKey(null) throws", and that throw is precisely what NameBlankEndpoints was added to eliminate,
	// so the justification outlived its own mechanism inside this same file. The live fact: NameTheNameless
	// runs before EVERY rule and replaces a blank or null endpoint with "(missing source)"/"(missing
	// target)", so nothing downstream of it can see a null. That fact is OURS - it lives in this file and
	// would change in our own diff - which is the case where an unreachable guard is dead code rather than
	// insurance. See docs/knowledge/Tests/reachability-not-corpus-absence-decides-whether-a-guard-stays.md.
	// The tripwire is therefore NameTheNameless running FIRST, not the order of these two checks: move the
	// naming pass after the rules and this guard comes back in the same commit.
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
		// Type only on BOTH arms, and one fact covers both: DivergesIntoTwoBranches needs two branches
		// leaving the element by DIFFERENT edges, so an element with a single outgoing edge can never satisfy
		// it - every per-branch set is the same singleton and they always overlap. This rule HAD such a
		// filter on the gateway arm until a mutation showed it could not fail, and a `Count > 1` on the
		// conditional arm measured equivalent to no filter. Both are the fast path no test can distinguish
		// from the check it guards, which is the shape of code that rots.
		// An ELEMENT that branches on a condition belongs in this set too, and leaving it out was the rule's
		// blind spot: the platform synthesizes an exclusive gateway for any element with a conditional
		// outgoing flow, and that synthesized gateway chooses exactly as a declared one does. So
		// `A -conditional-> B`, `A -conditional-> C`, both into a parallel join, hangs in Running forever and
		// raised nothing, because A is a userTask and the set held only declared gateways. The edge-level
		// test below is unchanged - what widened is WHICH sources it is applied to.
		HashSet<string> choosingElements = nodes
			.Where(n => TypeOf(n) is EventType.ExclusiveGateway or EventType.InclusiveGateway
				|| outgoing[n.Name].Any(o => o.FlowKind == ProcessFlowKind.Conditional))
			.Select(n => n.Name)
			.ToHashSet();
		foreach (string joinName in nodes.Where(n => TypeOf(n) == EventType.ParallelGateway).Select(n => n.Name)) {
			List<ProcessGraphEdge> ins = incoming[joinName];
			if (ins.Count < 2 || choosingElements.Count == 0) {
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
			// going xor -conditional-> A -> and. The direct branch then projected to the empty set at the
			// gateway, the emptiness check dropped it, no pair formed and the commonest hand-authored
			// deadlock of all raised nothing - while the join can never fire whichever way the gateway goes.
			// Grouped by SOURCE once per join. It used to be a flat set per branch, re-scanned in full for
			// every candidate element (`perBranch.Select(edges => edges.Where(edge => edge.Source ==
			// gateway).ToHashSet())`), allocating a fresh HashSet per branch per candidate - so the work
			// grew as joins x candidates x branches x edges. Widening the candidate set to every element
			// with a conditional flow, which is what made R8 see the synthesized gateway, multiplied the
			// term that was already the largest. A dictionary lookup per candidate replaces the re-scan.
			//
			// Grouped by hand rather than with GroupBy().ToDictionary(g => g.ToHashSet()), which allocates
			// THREE structures per branch - LINQ's internal Lookup, with a Grouping plus backing array per
			// distinct source, then the dictionary, then the per-group HashSet - and passes over the walked
			// edges twice. The re-scan this replaces was per CANDIDATE while the grouping is per JOIN, so on
			// a graph with many joins and one or two candidates each the allocation is the larger of the two
			// costs and the rewrite would have been a net loss. The manual loop keeps the dictionary lookup
			// and drops the intermediate layers.
			//
			// Keying on edge.Source is safe for the same reason the self-loop guard above is absent, and it
			// is worth naming because a null key here would throw out of the one method whose contract is
			// that it never throws: NameTheNameless has already replaced every blank endpoint.
			List<Dictionary<string, HashSet<ProcessGraphEdge>>> perBranch = ins
				.Select(seed => GroupWalkedEdgesBySource(seed, incoming))
				.ToList();
			string split = choosingElements.FirstOrDefault(gateway => DivergesIntoTwoBranches(gateway, perBranch));
			if (split != null) {
				findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Warning, "R8",
					$"Parallel join '{joinName}' waits for every incoming branch, but two of them leave "
					+ $"'{split}' by different flows, and that element takes only one of them. If that is the "
					+ "shape you meant, the instance will hang in Running with no error - use an exclusive "
					+ "gateway to merge instead.",
					joinName));
			}
		}
	}

	// One branch's backward walk, grouped by the SOURCE element of each walked edge. Its own method rather
	// than two nested loops inside CheckParallelJoinDeadlock: the grouping pushed that method's cognitive
	// complexity to 17 against a limit of 15 (S3776), and the nesting was the whole of the increase.
	//
	// Hand-rolled rather than GroupBy().ToDictionary(g => g.ToHashSet()), which allocates THREE structures
	// per branch - LINQ's internal Lookup, with a Grouping plus backing array per distinct source, then the
	// dictionary, then the per-group HashSet - and passes over the walked edges twice. This runs once per
	// join where the projection it replaced ran once per join PER CANDIDATE, so on a graph with many joins
	// and one or two candidates each the allocation would otherwise be the larger of the two costs.
	//
	// Keying on edge.Source is safe for the same reason CheckSelfLoops carries no null guard, and it is
	// worth naming because a null key would throw out of the one method whose contract is that it never
	// throws: NameTheNameless has already replaced every blank endpoint.
	private static Dictionary<string, HashSet<ProcessGraphEdge>> GroupWalkedEdgesBySource(
			ProcessGraphEdge seed, IReadOnlyDictionary<string, List<ProcessGraphEdge>> incoming) {
		Dictionary<string, HashSet<ProcessGraphEdge>> bySource = [];
		foreach (ProcessGraphEdge walked in TraverseBackwardEdges(seed, incoming)) {
			if (!bySource.TryGetValue(walked.Source, out HashSet<ProcessGraphEdge> group)) {
				bySource[walked.Source] = group = [];
			}
			group.Add(walked);
		}
		return bySource;
	}

	// True when two of the join's branches trace back through DISJOINT outgoing flows of the same gateway -
	// so the gateway picks one of them and the other never delivers its token. Branches that reach the
	// gateway through the same flow (or through all of them, which is what a fully merged choice upstream
	// looks like) are not in conflict and must not warn.
	private static bool DivergesIntoTwoBranches(string gateway,
			List<Dictionary<string, HashSet<ProcessGraphEdge>>> perBranch) {
		List<HashSet<ProcessGraphEdge>> atGateway = [];
		foreach (Dictionary<string, HashSet<ProcessGraphEdge>> branch in perBranch) {
			// A branch that never passes through this element must not be compared, or a retry loop reads as
			// two disjoint branches - and TryGetValue alone is now the whole of that test. The flat version
			// needed `Count > 0` because its `.Where(...)` could yield an empty set; a group here exists only
			// because an edge was put in it, so carrying the count check across made it unreachable. It was
			// carried across, WITH a comment calling it load-bearing, and the mutation that removes it
			// stayed green - which is how the comment was found to be wrong rather than merely stale.
			if (branch.TryGetValue(gateway, out HashSet<ProcessGraphEdge> walked)) {
				atGateway.Add(walked);
			}
		}
		for (int left = 0; left < atGateway.Count; left++) {
			for (int right = left + 1; right < atGateway.Count; right++) {
				if (!atGateway[left].Overlaps(atGateway[right])) {
					return true;
				}
			}
		}
		return false;
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
		visited.UnionWith(queue);
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
		visited.UnionWith(queue);
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
