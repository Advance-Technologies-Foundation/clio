# ENG-91853 — Traps

Every entry costs a day if missed. Ordered by damage. **Silent** means: the call succeeds, the schema
saves, the process interprets, and the behaviour or the diagram is wrong.

Revised 2026-09-05 — four traps were closed by ENG-95891 and are kept here (struck through in the
Status column) because the *reason* they were traps is still the reason the remaining work must follow
the same shape.

| # | Trap | Silent? | Status |
|---|---|---|---|
| T-1 | `ManagerItemUId` not set on a flow | yes | **partly closed** — sequence + conditional done; **default missing** |
| T-2 | `VisualType` left at `Polyline` | yes | **closed** (ENG-95891) |
| T-3 | `FlowType = Conditional` on the plain flow class | no — throws | open (new code) |
| T-4 | `ProcessSchemaConditionalFlow` left at `FlowType = Sequence` | yes | open (new code) |
| T-5 | Writing `GV2` **and** `CI3` | yes | **closed** (ENG-95891 refuses) |
| T-6 | Conditional flow with neither condition nor result | yes | partly — build path refuses; the `kind` path is new |
| T-7 | Flow insertion order silently sets branch precedence | yes | **open** — nothing enforces or documents it for `flows[]` |
| T-8 | Removing a flow leaves stale `Outgoings` / `Incomings` | yes | **open** in `RemoveFlow` |
| T-9 | Ambiguous `(source, target)` match acted on arbitrarily | yes | **closed** (`FindTheFlowBetween`) |
| T-10 | Reusing the `SequenceFlow_` name prefix for all kinds | yes | open |
| T-11 | Gateway with no `Size` | yes | open |
| T-12 | Diverging split with no default branch | no — throws at run time | open |
| T-13 | `ValidateStructure`'s "gateways cannot appear here" remark goes stale | yes | open |
| T-14 | R14 rejects a converging or-gateway | no — false error | **open** |
| T-15 | Layout collapses on a back-edge | yes (visual) | **open** |
| T-16 | Parallel join that can never complete | yes — hangs | open |
| T-17 | A re-kind that regenerates the flow or moves it in `FlowElements` | yes | open — the pattern exists, follow it |

---

## T-1 — `ManagerItemUId`: two kinds fixed, the default flow still missing

**The rule.** `BL7` is how the designer resolves a flow's *manager item*, which carries its image and its
allowed-flow rules (`ProcessSchemaElementManager.cs:456-471`, `:725-727`). Neither flow class assigns one
(`ProcessSchemaSequenceFlow.cs:284-287`, `ProcessSchemaConditionalFlow.cs:682-684`), and
`WriteMetaData` uses the default-skipping overload (`ProcessSchemaBaseElement.cs:414`) — so an unset
value means the key is **absent**, not empty. It is present on **9 762 / 9 762** shipped flows.

**Closed half.** ENG-95891 added `ProcessDesignConstants.FlowManagerItems.Sequence` / `.Conditional` and
stamps both write paths.

**Open half.** The default-flow item `573ed909-e069-4161-b193-ae8dd9437c68` is recorded in that
docblock as a measured fact but has **no constant**, on the explicit reasoning that the package cannot
build a default flow so the constant would be dead. This ticket makes it live: add
`FlowManagerItems.Default` and stamp it.

**Why nothing catches it.** The run time resolves a flow's kind from the CLR type
(`FlowConditionalGateway.cs:80-83`), never from `BL7`. The platform's own unit tests set the *wrong* item
on a conditional flow (`BaseProcessTestCase.cs:358-368`) and pass. The blast radius is designer-side
only, and silent.

---

## T-2 — `VisualType` (closed)

`ProcessSchemaSequenceFlowVisualType = { Polyline = 0, AutoPolyline = 1, Curve = 2 }`
(`ProcessEnum.cs:135-140`). The class default is `Polyline`, which routes through the stored `CI10`
polyline points — a collection the toolkit never writes. Every designer flow is `AutoPolyline`:
**9 762 / 9 762**.

Fixed by ENG-95891 on both write paths, with the corpus measurement recorded in the code. **Keep it set
on the new default-flow path**, and assert it in the per-kind round-trip test — this is exactly the field
whose absence is invisible on a single row and visible the moment a branch leaves it.

`StrokeColor` needs no code: the field initialiser is already `FF939598`
(`ProcessSchemaSequenceFlow.cs:207`) and the write default is `Color.Empty`.

---

## T-3 — `FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow` throws

The tempting shortcut — reuse `AddSequenceFlow` and just set the enum — makes the platform's own
design-time helper execute an **unguarded cast**:

```csharp
if (sequenceFlow.FlowType == ProcessSchemaEditSequenceFlowType.Conditional) {
    var conditionalFlow = (ProcessSchemaConditionalFlow)sequenceFlow;   // InvalidCastException
```
`ProcessSchemaFlowNode.cs:125-131`

The save succeeds; the exception surfaces later, to a human opening a properties page. ENG-95891's
`SetFlowCondition` documents the same conclusion from the generator side: such a flow *"SERIALIZES as
conditional and has its condition DROPPED during flow-schema generation"*.

**Rule:** a conditional flow is always `new ProcessSchemaConditionalFlow(schema)`; never set `FlowType`
by hand. The **default** flow is the opposite case — there the enum *is* the marker, on the plain class.

---

## T-4 — The mirror image: a `ProcessSchemaConditionalFlow` whose `FlowType` is `Sequence`

Reachable via the parameterless constructor, a clone, or metadata written without `CI4`. Both
design-time helpers select on `FlowType` (`ProcessSchemaFlowNode.cs:107-137`), so such a flow becomes
**invisible** to the designer's branch structure while the run time still treats it as conditional. The
designer then shows a split it cannot edit and will happily add a second default flow to it.

**Assert the whole quadruple per kind** — class, `FlowType`, `ManagerItemUId`, `VisualType` — in one
round-trip test. All four together, or the artifact is not designer-faithful.

---

## T-5 — Writing an activity result and an expression on the same flow (closed)

`CreateSequenceFlowElement` reads `ProcessActivitiesSelectedResults` first and only falls back to
`ConditionExpression` when it is empty (`ProcessSchemaConditionalFlow.cs:214-231`), so `CI3` becomes dead
text. Corpus: **0 of 1 406** flows carry both.

ENG-95891 refuses the write, naming the result branching, and reports `branchesOnActivityResult` on
describe. **Keep both behaviours when the `flows[].kind` path arrives**: a build-time
`kind: conditional` + `condition` on an element that already branches on results must be refused the same
way, not silently applied.

---

## T-6 — A conditional flow with no condition is an unconditional branch that looks conditional

With `GV2` empty and `ConditionExpression` empty, `ExpressionText` becomes the literal `"true"`. The
diagram shows a conditional flow; the run time always takes it. 7 shipped flows are in that state.

ENG-95891's build path refuses a `condition` outright (it cannot apply one), and `setFlowCondition`
refuses an empty condition. **The new hole is the `kind` path**: `flows[].kind = conditional` with no
`condition` must be refused at build, because nothing downstream will.

---

## T-7 — The order the toolkit inserts flows in silently decides which branch wins

`FlowSchemaGenerator.Generate` iterates `Schema.FlowElements` (`:396`) and groups by source in encounter
order; `FillSequenceFlows` (`:145-160`) adds them unsorted; `FlowSchema.FindSequenceFlowsBySourceUId`
(`FlowSchema.cs:747`) is a plain `Where`; `FlowConditionalGateway.Accept` iterates that and, under
`ConditionEvalStrategy.Exclusive`, **returns on the first `true`** (`:165-176`). No index, priority or
position field exists on a flow, and Academy documents evaluation order nowhere.

**`Outgoings` is not in this chain** (it appears zero times in `FlowSchemaGenerator.cs`) — the
`FlowElements` index is what matters. ENG-95891 verified this and relies on it.

**What breaks.** Two overlapping conditions (`Amount > 100`, `Amount > 1000`) resolve differently purely
by `flows[]` order, with no diagnostic and no metadata a human can inspect.

**Fix, three cheap parts:** (a) preserve `flows[]` declaration order when materialising — never re-sort;
(b) say it in the tool `[Description]` and the guidance article; (c) put the **first-declared** branch on
the top layout lane so visual order equals evaluation order
([layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm)).

---

## T-8 — Removing a flow leaves it in its endpoints' `Outgoings` / `Incomings`

`Outgoings` / `Incomings` are populated by the `SourceRefUId` / `TargetRefUId` **setters**
(`ProcessSchemaSequenceFlow.cs:128-152`, `:180-204`). Removal does not undo it:
`ProcessSchemaFlowElementCollection.RemoveItem` only nulls `ParentMetaSchema`
(`ProcessSchemaFlowElement.cs:304-309`). `ProcessGraphBuilder.RemoveFlow` still does exactly
`schema.FlowElements.Remove(flow)`.

**Harmless today** because nothing in the package reads those collections. **Not harmless once gateways
exist**: `GetOutgoingsDefFlows` / `GetOutgoingsConditionalFlows` and
`ProcessSchemaConditionalFlow.GetSourceRefProcessActivities` all walk them
(`ProcessSchemaFlowNode.cs:107-137`, `ProcessSchemaConditionalFlow.cs:686-698`), and any new gateway rule
("at most one default per source", "or-gateway outgoings must be conditional or default") is naturally
written against them.

ENG-95891 already hit the sharp edge from the other side and documented it: the collection is **keyed**,
so attaching a replacement carrying the same `UId` throws `ItemAlreadyExistException` from inside the
platform. Its `SetFlowCondition` therefore detaches first:

```csharp
flow.SourceRefUId = Guid.Empty;   // the setter removes it from the old source's Outgoings
flow.TargetRefUId = Guid.Empty;   // …and from the old target's Incomings
```

**Fix.** Do the same in `RemoveFlow` before `schema.FlowElements.Remove(flow)`, and keep the package
rule — **derive adjacency from `schema.FlowElements`, never from `Outgoings`/`Incomings`** — in a code
comment, because the platform's own helpers do the opposite.

---

## T-9 — Ambiguous `(source, target)` match (closed)

`FindTheFlowBetween` now refuses when more than one flow connects the pair, and `AddSequenceFlow` refuses
to create a second one, on the reasoning that the pair is *"the only handle a caller has"*
(`ProcessGraphBuilder.cs:155-215`). Corpus support: of **9 762** flows there are 9 762 distinct pairs.

**Consequence for this ticket:** no flow-identity addressing is needed, and every new flow operation must
route through `FindTheFlowBetween` rather than re-introducing a `FirstOrDefault`.

---

## T-10 — One name prefix for three flow kinds

`SchemaDefaults.SequenceFlowNamePrefix = "SequenceFlow_"` names every flow
`SequenceFlow_<source>_<target>`. The designer's own prefixes come from
`DesignModeClass(DefNamePrefix = …)`, and the corpus bears them out: `ConditionalSequenceFlow<N>` (1 017)
/ `ConditionalFlow<N>` (275), `DefaultSequenceFlow<N>` (548).

Beyond cosmetics: the flow name appears in process logs, in the designer's element list, and in any
human diff of two metadata files. A default flow called `SequenceFlow_Gateway1_Terminate1` reads as a
plain flow to everyone not looking at `CI4`.

**Fix.** Add `ConditionalFlowNamePrefix` and `DefaultFlowNamePrefix`; do not overload the existing
constant. Note `SetFlowCondition` deliberately **keeps** the original name on a re-kind (renaming an
existing flow would break nothing but would surprise), so the prefix applies to newly created flows only.

---

## T-11 — A gateway with no `Size`

`ProcessElementFactory.Create` assigns `element.Size = handler.DefaultSize`
(`ProcessElementFactory.cs:48-55`), so a handler that forgets `DefaultSize` yields `Size = (0, 0)`. The
layout engine centres by `node.Size.Height / 2` (`ProcessLayoutEngine.cs:92-96`), so the gateway sits
~27 px off its lane, and the designer renders a zero-size rhomb.

**Fix.** `Layout.GatewaySizePx = 55` (measured on every gateway kind that carries `BN2`) and
`DefaultSize => new Size(55, 55)` on both handlers.

---

## T-12 — A diverging split with no default branch throws at run time

```csharp
if (ResultSequenceFlows.Count == 0) {
    throw new MismatchItemsCountException(new LocalizableString("Terrasoft.Core",
        "ProcessEngine.Exception.MatchCondition.ByCount"));
}
```
`FlowConditionalGateway.cs:119-123`

Not silent — but it fires only at run time, only on the input that fails every condition, and nothing
earlier objects: `ProcessInterpretationValidator` has no branch-coverage rule, so `EnsureValidForSave`
passes.

65 shipped exclusive gateways are in this shape (2 conditional, no default), so it must **not** become a
build-time error.

**Fix.** Keep it a warning (R7), with the concrete consequence in the message: *"if no condition matches
at run time the process fails with MismatchItemsCountException"*.

---

## T-13 — The build-path structural guard carries a remark this ticket falsifies

`ProcessGraphBuilder.ValidateStructure` is documented as create-path-only, justified by:

> *"Scope is safe here: the build path materializes only start/signal-start/end/user-task nodes and
> sequence flows, so reachability cannot false-positive on designer-only constructs (gateways,
> annotations, boundary events)."*

Once gateways are buildable that premise is gone, and the guard silently starts judging graphs it was
explicitly reasoned about *not* judging. Its reachability check is still correct for a gateway graph, but
its "start event must have a single outgoing flow" and "every element must reach an end" clauses now
interact with branches, and a **retry loop** (15 % of gateway containers) must keep passing.

**Fix.** Extend `ValidateStructure` with the gateway/flow rules **and** rewrite the remark in the same
edit. Add a fixture for each of: split-without-merge, split-with-merge, retry loop.

---

## T-14 — clio's R14 rejects a shape only the designer can produce

`ProcessGraphValidator.CheckDefaultFlowRules` raises an **error** when a default flow has no sibling
conditional flow (`ProcessGraphValidator.cs:169-173`). But an or-gateway's allowed outgoing kinds are
**conditional and default only** (`ProcessSchemaElementManager.cs:431-434`), and the client forces the
same (`connection-utils.ts:72`) — so a **converging** or-gateway's single continuation is a default flow
with no conditional sibling, by construction.

**Recounted on the broad corpus: exactly 45** shipped gateways (40 exclusive + 5 inclusive). Examples:
`BulkFileManagement/DeleteFilesInTable`, `CaseService/RunSendEmailToCaseGroup`,
`CrtCaseCopilot/Copilot_GetCaseExternalMessages`, `BpmGDPR/BpmProcess6`.

**Fix.** Scope R14 to sources with **more than one** outgoing flow — see
[validator §2.1](eng-91853-gateways-and-flows-validator.md#21-fix-r14--it-currently-rejects-a-shape-only-the-designer-can-produce).

---

## T-15 — One back-edge collapses the whole diagram

`ProcessLayoutEngine` layers with Kahn's algorithm seeded from in-degree-0 nodes. A back-edge means the
loop target never reaches in-degree 0, the queue drains early, and **every node the traversal did not
reach keeps `column = 0`** (`ProcessLayoutEngine.cs:57-79`).

Traced on the real `BulkFileManagement/DeleteFilesInTable`: four of six elements land in column 0,
stacked at `X = 60`. **54 of 368** gateway-bearing containers contain a back-edge, and a retry loop is
one of the most natural things to build with an exclusive gateway.

**Fix.** [layout §4](eng-91853-gateways-and-flows-layout.md#4-the-proposed-algorithm).

---

## T-16 — A parallel join that can never complete hangs the instance

`FlowParallelGateway` is a token join: it proceeds only when every incoming branch has delivered a token
(`FlowParallelGateway.cs:53-90`). If only one of two branches can ever run, the instance waits
**forever** — no exception, no log line, stuck in *Running*.

`ai-bp-connection-rules.md` already lists *"parallel converge that can deadlock"* among the intended
warnings; it is not implemented.

**Fix (cheap).** Warn when a parallel join's incoming branches trace back to a common **exclusive**
split. Warning only — a false positive must never block a build.

---

## T-17 — A re-kind that regenerates the flow, or moves it in `FlowElements`

Turning a plain flow into a **default** flow is the same operation shape ENG-95891 already solved for
conditional, and it is far more delicate than it looks. `SetFlowCondition` is the reference: it captures
`schema.FlowElements.IndexOf(flow)` and re-inserts at that index, **carries the `UId` over** rather than
regenerating it, restores `CreatedInSchemaUId` (removal zeroes it and insertion back-fills it only when
empty), deliberately does **not** restore `ModifiedInSchemaUId`, **clones** the caption (the setter is
`LocalizableString.Merge`, which returns its argument *by reference*, so a plain assignment would leave
two flows sharing one instance), and carries every persisted operator field — stroke colour, curve
centre, both local point positions, both anchor positions (`CI11`/`CI12`, which the platform's own copy
constructor drops), container, index, position, size, drag group, background mode.

Three ways to get this wrong, all silent:

- **regenerate the `UId`** → anything addressing the flow by UId stops resolving;
- **append instead of re-inserting at the index** → the branch moves to last in evaluation order (T-7);
- **copy what the platform's copy constructor copies** → loses `CI11`/`CI12`, which are persisted.

**Rule for this ticket: reuse `SetFlowCondition`'s clone helper rather than writing a second one.** If
the default re-kind needs the same field list, extract it; do not fork it.

---

## Two non-traps, recorded so nobody spends a day on them

**`StrokeColor` is already correct** — `FF939598` on 9 762 / 9 762 equals the C# field initialiser. No
code needed.

**`HH2`, `CG1` and `GV3` need no writes.** `BranchingDecisions` (`HH2`) is written unconditionally as an
empty collection (`ProcessSchemaDecisionalGateway.cs:196-199`); `IncomingBranchNames` (`CG1`) is written
as an empty object with a standing platform `TODO` (`ProcessSchemaParallelGateway.cs:100-106`);
`MatchBranchingDecisions` (`GV3`) is empty on 1 406 / 1 406 and — measured by ENG-95891 — **read by
nothing**. A guard for `GV3` was written and then removed on that evidence; do not add it back without
re-tracing the runtime dispatch.
