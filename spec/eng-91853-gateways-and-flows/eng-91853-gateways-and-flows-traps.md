# ENG-91853 — Traps

Every entry is something that costs a day if missed. They are ordered by damage. The **Silent** column
is the one that matters: a silent trap means the call succeeds, the schema saves, the process compiles
or interprets, and the behaviour or the diagram is wrong.

| # | Trap | Silent? | Already broken today? |
|---|---|---|---|
| T-1 | `ManagerItemUId` never set on a flow | yes | **yes** |
| T-2 | `VisualType` left at `Polyline` | yes | **yes** |
| T-3 | `FlowType = Conditional` on the plain flow class | no — throws | no |
| T-4 | `ProcessSchemaConditionalFlow` left at `FlowType = Sequence` | yes | n/a (new code) |
| T-5 | Writing `GV2` **and** `CI3` | yes | n/a |
| T-6 | Conditional flow with neither condition nor result | yes | n/a |
| T-7 | Flow insertion order silently sets branch precedence | yes | n/a |
| T-8 | Removing a flow leaves stale `Outgoings` / `Incomings` | yes | **yes** (harmless today) |
| T-9 | `RemoveFlow` silently picks one of several matches | yes | **yes** (unreachable today) |
| T-10 | Reusing the `SequenceFlow_` name prefix for all kinds | yes | n/a |
| T-11 | Gateway with no `Size` | yes | n/a |
| T-12 | Diverging split with no default branch | no — throws at run time | n/a |
| T-13 | `ValidateStructure`'s "gateways cannot appear here" remark goes stale | yes | n/a |
| T-14 | R14 rejects a converging or-gateway | no — false error | **yes** |
| T-15 | Layout collapses on a back-edge | yes (visual) | **yes** |
| T-16 | Parallel join that can never complete | yes — hangs | n/a |

---

## T-1 — `ManagerItemUId` is never set on a flow, and the writer then omits it

**What happens.** `ProcessGraphBuilder.AddSequenceFlow` constructs the flow and sets `UId`, `Name`,
`SourceRefUId`, `TargetRefUId` — and nothing else (`ProcessGraphBuilder.cs:150-158`). Neither
`ProcessSchemaSequenceFlow.Initialize()` nor `ProcessSchemaConditionalFlow.Initialize()` assigns
`ManagerItemUId` (`ProcessSchemaSequenceFlow.cs:284-287`, `ProcessSchemaConditionalFlow.cs:682-684`).
`ProcessSchemaBaseElement.WriteMetaData` then writes it with the **default-skipping** overload
(`ProcessSchemaBaseElement.cs:414`), so `Guid.Empty` is not written as an empty GUID — the `BL7` key is
**absent from the metadata entirely**.

**Evidence it is wrong.** `BL7` is present on **9 144 / 9 144** flows in 1 099 shipped packages, and its
value is exactly the manager item for the kind: `0d8351f6…` sequence, `dac675d4…` conditional,
`573ed909…` default.

**Why nothing has caught it.** The run time resolves a flow's kind from the CLR type
(`FlowConditionalGateway.cs:80-83`), never from `BL7`. The platform's own unit tests set the *wrong*
item on a conditional flow (`BaseProcessTestCase.cs:358-368`) and pass. The blast radius is the
**designer** only: `BL7` is how the client resolves the manager item that carries the flow's image and
its allowed-flow rules (`ProcessSchemaElementManager.cs:456-471`, `:725-727`).

**What breaks.** A default flow renders as an ordinary solid arrow instead of the dashed slash-marked
default-flow glyph, and a conditional flow loses its diamond marker — so a human opening a
toolkit-built branching process cannot see which branch is the else-branch. Invisible today because
every flow the toolkit builds is a plain sequence flow on a single row.

**Fix.** Set `ManagerItemUId` per kind when the flow is constructed, from constants
(`SequenceFlowUId` / `ConditionalFlowUId` / `DefFlowUId`). Assert it in a round-trip test.

---

## T-2 — `VisualType` defaults to `Polyline`, and the toolkit writes no polyline

**What happens.** `ProcessSchemaSequenceFlowVisualType = { Polyline = 0, AutoPolyline = 1, Curve = 2 }`
(`ProcessEnum.cs:135-140`). The class default is `Polyline`; the default-skipping writer omits `0`; the
toolkit never sets it. Every designer flow in the corpus carries `CI6 = 1` — **9 144 / 9 144**.

**What breaks.** `Polyline` routes the connector through the stored `CI10` `PolylinePointPositions`
collection, which the toolkit never populates. `AutoPolyline` routes itself. On one straight row the two
are indistinguishable; as soon as a branch leaves the row — which is the entire point of this ticket —
a `Polyline` flow with no points draws a straight segment across whatever is in the way, while the
designer's own flows bend around obstacles. The verbatim capture shows the designer using `CI10` to
route a loop-back **below** the row (`CI10: {Item0: "553;315", Item1: "174;315"}`,
[capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim)).

**Fix.** `VisualType = ProcessSchemaSequenceFlowVisualType.AutoPolyline` on every flow the package
creates. One line; do **not** try to compute polyline points.

`StrokeColor` needs no change: the field initialiser is already `FF939598`
(`ProcessSchemaSequenceFlow.cs:207`) and the write default is `Color.Empty`, so it is emitted correctly.

---

## T-3 — `FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow` throws

The tempting shortcut — reuse the existing `AddSequenceFlow` and just set `FlowType` — makes the
platform's own design-time helper execute an **unguarded cast**:

```csharp
if (sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Conditional) {
    var conditionalFlow = (ProcessSchemaConditionalFlow)sequenceFlow;   // InvalidCastException
```
`ProcessSchemaFlowNode.cs:125-131`

The save succeeds. The exception surfaces later, when a designer properties page walks the node's
outgoing flows — i.e. to a human, not to the API caller.

**Fix.** A conditional flow is always `new ProcessSchemaConditionalFlow(schema)`. `FlowType` is then set
for you by the constructor chain (`ProcessSchemaConditionalFlow.cs:640-643`); never set it by hand.

---

## T-4 — The mirror image: a `ProcessSchemaConditionalFlow` whose `FlowType` is `Sequence`

Reachable if the flow is constructed through the parameterless constructor, cloned, or read from
metadata written by something that omitted `CI4`. Both design-time helpers select on `FlowType`
(`ProcessSchemaFlowNode.cs:107-137`), so such a flow becomes **invisible** to the designer's branch
structure while the run time still treats it as conditional (it selects on `BpmnElementName`). The
designer will show a split it cannot edit and will happily add a second default flow to it.

**Fix.** Assert the pair in the same round-trip test as T-1: `class == ProcessSchemaConditionalFlow`
**and** `FlowType == Conditional` **and** `ManagerItemUId == ConditionalFlowUId`. All three, together,
or the artifact is not designer-faithful.

---

## T-5 — Writing an activity-result and an expression on the same flow discards the expression

```csharp
if (activitiesSelectedResults.Count == 0) {
    conditionalSequenceFlow.ExpressionText = ConditionExpression.IsNotNullOrEmpty() ? ConditionExpression : "true";
    return conditionalSequenceFlow;
}
…                                     // ConditionExpression is never read again
```
`ProcessSchemaConditionalFlow.cs:214-231`

`GV2` wins; `CI3` becomes dead text that `describe` will faithfully report and the run time will never
evaluate. Corpus confirms the platform's own tooling never produces both: **0 of 1 365**.

**Fix.** Treat the two dialects as mutually exclusive in the contract. A `setFlowCondition` /
`flows[].condition` write onto a flow that already carries a non-empty `GV2` must be **refused**, naming
the activity-result branching, not silently applied. `describe` reports which dialect a flow uses.

---

## T-6 — A conditional flow with no condition is an unconditional branch that looks conditional

Same code path: with `GV2` empty and `ConditionExpression` empty, `ExpressionText` becomes the literal
`"true"`. The diagram shows a conditional flow; the run time always takes it. 7 shipped flows are in
this state, so it is a real shape, not a hypothetical.

**Fix.** The build/modify path must **refuse** a `kind: conditional` flow with no `condition`. Nothing
in the platform will refuse it, and `validate-process-graph` cannot see conditions at all (it validates
topology only) — so this guard has to live in the server builder.

---

## T-7 — The order the toolkit inserts flows in silently decides which branch wins

`FlowSchema.FindSequenceFlowsBySourceUId` is a plain `Where` over an insertion-ordered collection
(`FlowSchema.cs:747-749`); `FlowConditionalGateway.Accept` iterates that order and, under
`ConditionEvalStrategy.Exclusive`, **returns on the first `true`** (`FlowConditionalGateway.cs:165-176`).
There is no index, priority or position field on a flow, and Academy documents evaluation order nowhere.

**What breaks.** Two overlapping conditions (`Amount > 100` and `Amount > 1000`) resolve differently
depending only on descriptor array order — with no diagnostic, no metadata difference a human can
inspect, and no way for the AI to discover the rule.

**Fix.** Three parts, all cheap: (a) preserve `flows[]` declaration order when materialising flows —
never re-sort; (b) say it in the tool `[Description]` and in the `process-modeling` guidance article;
(c) make the Y-layout put the **first-declared** branch on the top lane, so visual order equals
evaluation order and the invisible rule becomes visible
([layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm)).

---

## T-8 — Removing a flow leaves it in its endpoints' `Outgoings` / `Incomings`

`Outgoings` and `Incomings` are populated by the `SourceRefUId` / `TargetRefUId` **setters**
(`ProcessSchemaSequenceFlow.cs:128-152`, `:180-204`). Removal does not undo it:
`ProcessSchemaFlowElementCollection.RemoveItem` only nulls `ParentMetaSchema`
(`ProcessSchemaFlowElement.cs:304-309`). So after `ProcessGraphBuilder.RemoveFlow` /
`RemoveElement` — both of which call `schema.FlowElements.Remove(...)`
(`ProcessGraphBuilder.cs:161-193`) — the source node still lists the removed flow among its outgoing
flows for the rest of the modify session.

**Harmless today** because nothing in the package reads those collections: the graph builder, the layout
engine and the describer all walk `schema.FlowElements` themselves. **Not harmless once gateways
exist**: `ProcessSchemaFlowNode.GetOutgoingsDefFlows` / `GetOutgoingsConditionalFlows` and
`ProcessSchemaConditionalFlow.GetSourceRefProcessActivities` all walk `Outgoings` / `Incomings`
(`ProcessSchemaFlowNode.cs:107-137`, `ProcessSchemaConditionalFlow.cs:686-698`), and any new gateway
rule ("at most one default per source", "or-gateway outgoings must be conditional or default") is
naturally written against them.

**Fix.** In `RemoveFlow`, clear the endpoints before removing the element:

```csharp
flow.SourceRefUId = Guid.Empty;   // the setter removes it from the old source's Outgoings
flow.TargetRefUId = Guid.Empty;   // …and from the old target's Incomings
schema.FlowElements.Remove(flow);
```

The setters do exactly that and then early-return on `Guid.Empty`
(`ProcessSchemaSequenceFlow.cs:138-152`). Then keep the package rule: **derive adjacency from
`schema.FlowElements`, never from `Outgoings`/`Incomings`** — and state it in a code comment, because
the platform's own helpers do the opposite.

---

## T-9 — `RemoveFlow` silently removes an arbitrary flow when several match

```csharp
ProcessSchemaSequenceFlow flow = schema.FlowElements.OfType<ProcessSchemaSequenceFlow>()
    .FirstOrDefault(f => f.SourceRefUId == sourceUId && f.TargetRefUId == targetUId);
```
`ProcessGraphBuilder.cs:161-169`

Unreachable today (one flow per pair, by construction). With gateways it becomes reachable in principle:
two conditional flows from the same gateway to the same target with different conditions is a legal
shape, and `FirstOrDefault` would delete whichever came first.

**Measured relief:** of **9 144** shipped flows there are **9 144** distinct `(source, target)` pairs —
**0** duplicates. So `(source, target)` remains a sufficient address and **this ticket does not need
flow-identity addressing**.

**Fix.** Keep the address; replace `FirstOrDefault` with a count check that **throws naming both flows**
when more than one matches. Cheap, and it converts a silent wrong-deletion into a clear refusal.
`OfType<ProcessSchemaSequenceFlow>()` already covers conditional flows through inheritance — no change
needed there.

---

## T-10 — Reusing one name prefix for all three flow kinds

The package has a single constant, `SchemaDefaults.SequenceFlowNamePrefix = "SequenceFlow_"`
(`ProcessDesignConstants.cs`), and `AddSequenceFlow` names every flow
`$"SequenceFlow_{source}_{target}"`. The designer's own prefixes come from
`DesignModeClass(DefNamePrefix = …)` and the corpus bears them out: `ConditionalSequenceFlow<N>` (1 017)
/ `ConditionalFlow<N>` (275) for conditional flows, `DefaultSequenceFlow<N>` (548) for default flows.

**Why it matters beyond cosmetics.** The flow name is what appears in process logs, in the designer's
element list, and in any human diff of two metadata files. A default flow called
`SequenceFlow_Gateway1_Terminate1` reads as a plain flow to everyone who is not looking at `CI4`.

**Fix.** Add `ConditionalFlowNamePrefix` and `DefaultFlowNamePrefix` siblings; do not overload the
existing constant.

---

## T-11 — A gateway with no `Size`

`ProcessElementFactory.Create` assigns `element.Size = handler.DefaultSize`
(`ProcessElementFactory.cs:48-55`), so a new gateway handler that forgets `DefaultSize` yields
`Size = (0, 0)`. Two consequences: the layout engine centres by `node.Size.Height / 2`
(`ProcessLayoutEngine.cs:92-96`), so the gateway sits ~27 px off its lane; and the designer renders a
zero-size rhomb.

**Fix.** `Layout.GatewaySizePx = 55` (measured: `"55;55"` on every gateway kind in the corpus that
carries `BN2`) and `DefaultSize => new Size(55, 55)` on the handler.

---

## T-12 — A diverging split with no default branch throws at run time

```csharp
if (ResultSequenceFlows.Count == 0) {
    throw new MismatchItemsCountException(new LocalizableString("Terrasoft.Core",
        "ProcessEngine.Exception.MatchCondition.ByCount"));
}
```
`FlowConditionalGateway.cs:119-123`

Not silent — but it fires **only at run time, only on the input that fails every condition**, and
nothing earlier objects: `ProcessInterpretationValidator` has no rule about branch coverage
(`ProcessInterpretationValidator.cs:264-275`), so `EnsureValidForSave` passes.

65 shipped exclusive gateways are in this shape (2 conditional, no default), so it must **not** become
a build-time error — it is legitimate when the conditions provably cover every case.

**Fix.** Keep it a **warning** (R7), and put the concrete consequence in the message: *"if no condition
matches at run time the process fails with MismatchItemsCountException"*. That is far more actionable
than the current *"should have a default flow so the process never dead-ends"*.

---

## T-13 — The build-path structural guard carries a remark that this ticket falsifies

`ProcessGraphBuilder.ValidateStructure` is documented as create-path-only, with this justification:

> *"Scope is safe here: the build path materializes only start/signal-start/end/user-task nodes and
> sequence flows, so reachability cannot false-positive on designer-only constructs (gateways,
> annotations, boundary events) — unlike an arbitrary existing process on the modify path."*
> `ProcessGraphBuilder.cs:82-90`

Once gateways are buildable that premise is gone, and the guard silently starts judging graphs it was
explicitly reasoned about *not* judging. Its reachability check is still correct for a gateway graph —
but its "start event must have a single outgoing flow" and "every element must reach an end" clauses now
interact with branches, and a **retry loop** (14 % of gateway processes) must keep passing.

**Fix.** Extend `ValidateStructure` with the gateway/flow rules **and** update the remark in the same
edit. Add a fixture for each of: split-without-merge, split-with-merge, and a retry loop.

---

## T-14 — clio's R14 rejects a shape only the designer can produce

`ProcessGraphValidator.CheckDefaultFlowRules` raises an **error** when a default flow has no sibling
conditional flow (`ProcessGraphValidator.cs:172-177`). But an or-gateway's allowed outgoing kinds are
**conditional and default only** (`ProcessSchemaElementManager.cs:431-434`), and the client forces the
same (`connection-utils.ts:72`) — so a **converging** exclusive gateway's single continuation is a
default flow with no conditional sibling, by construction.

**45 shipped gateways** are in exactly that shape (40 exclusive + 5 inclusive), including
`BulkFileManagement/DeleteFilesInTable`. R14 calls all 45 invalid.

**Fix.** Scope R14 to sources with **more than one** outgoing flow. Details and the other five R-rule
corrections: [validator](eng-91853-gateways-and-flows-validator.md).

---

## T-15 — One back-edge collapses the whole diagram

`ProcessLayoutEngine` layers with Kahn's algorithm seeded from in-degree-0 nodes. A back-edge means the
target of the loop never reaches in-degree 0, the queue drains early, and **every node the traversal did
not reach keeps `column = 0`** (`ProcessLayoutEngine.cs:57-79`).

Traced on the real `BulkFileManagement/DeleteFilesInTable`: four of six elements land in column 0,
stacked vertically at `X = 60`. **53 of 368** gateway-bearing schemas (14 %) contain a back-edge, and a
retry loop is one of the most natural things to build with an exclusive gateway.

**Fix.** [layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm) — seed from start
events, classify back-edges with a DFS and exclude them from layering.

---

## T-16 — A parallel join that can never complete hangs the process instance

`FlowParallelGateway` is a token join: it holds a `HashSet<Guid>` of arrived tokens and proceeds only
when every incoming branch has delivered one (`FlowParallelGateway.cs:53-90`). If an incoming branch is
unreachable, or if an exclusive split upstream means only one of two branches ever runs, the join waits
**forever** — no exception, no log entry, an instance stuck in *Running*.

`ai-bp-connection-rules.md` already lists *"parallel converge that can deadlock"* as an intended
warning; it is **not implemented**.

**Fix (in scope, cheap).** A warning when a parallel gateway with ≥2 incoming flows has an ancestor
exclusive/inclusive split such that not all of its incoming branches can be active together. The
minimal, no-false-positive version: warn when a parallel **join**'s incoming branches trace back to a
common **exclusive** split. Implement it as a warning only — a false positive must never block a build.

---

## Two non-traps, recorded so nobody spends a day on them

**`StrokeColor` is already correct.** `FF939598` on 9 144 / 9 144 shipped flows equals the C# field
initialiser (`ProcessSchemaSequenceFlow.cs:207`), and the write default is `Color.Empty`, so it is
emitted today. Do not add code for it.

**`HH2`, `CG1` and `GV3` need no writes.** `BranchingDecisions` (`HH2`) is written unconditionally as an
empty collection by `ProcessSchemaDecisionalGateway.WriteMetaData` (`:196-199`); `IncomingBranchNames`
(`CG1`) is written as an empty object with a standing platform `TODO`
(`ProcessSchemaParallelGateway.cs:100-106`); `MatchBranchingDecisions` (`GV3`) is empty on 1 365 / 1 365.
The corpus and the writers agree: leave all three alone.
