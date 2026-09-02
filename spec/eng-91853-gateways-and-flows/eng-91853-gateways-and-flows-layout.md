# ENG-91853 — The auto-layout engine: does it need rework, and what exactly

**Short answer: yes, but narrowly.** Three defects, all in one pure class with no I/O
(`packages/CrtProcessBuilder/Files/src/cs/Layout/ProcessLayoutEngine.cs`, ~100 lines), ~120 lines of
change and ~0.5 day including tests. Two of the three defects hit the ticket's *own* stated basic case;
the third already affects 14 % of real gateway processes.

What does **not** need rework: the X axis. Longest-path column layering is correct, produces the
designer's left-to-right reading order, and needs no change for gateways.

---

## 1. What the engine does today

`ProcessLayoutEngine.Apply(ProcessSchema)` — pure geometry, no `UserConnection`, unit-testable in
isolation (`ProcessLayoutEngine.cs:37-99`):

1. `nodes` = every flow element that is **not** a `ProcessSchemaSequenceFlow` (so gateways are already
   included — they are `ProcessSchemaFlowNode`s).
2. Build `outgoing` + `inDegree`, **skipping self-loops** and edges whose endpoints are not nodes.
3. **Longest-path layering (Kahn)**: seed the queue with in-degree-0 nodes; each node sits one column
   right of its deepest predecessor. If the queue starts empty, enqueue *all* nodes.
4. Post-pass: every start event is forced to column `0`; every end/terminate event to
   `maxNonEndColumn + 1`.
5. Position: `X = StartX + column * StepX`; within a column, nodes are staggered vertically around
   `CenterY` by `VerticalStep`, and `Position.Y = centerY - Size.Height / 2`.

Constants (`ProcessDesignConstants.Layout`): `StartX = 60`, `StepX = 180`, `CenterY = 200`,
`VerticalStep = 90`, `EventSizePx = 31`, `TaskWidthPx = 69`, `TaskHeightPx = 55`. **No gateway size.**

Two design facts worth keeping:

- `Position` is the shape's **top-left**, and the engine already centres shapes of different heights on
  one axis (`centerY - Size.Height / 2`). The designer does the same: in the §6 capture a 31-px start
  event sits at `Y = 184` and a 55-px script task at `Y = 172` — both centred on `Y ≈ 199.5`.
- The layout is a **pure function of the graph**, so re-running it on an unchanged graph is a no-op.
  That property is load-bearing (see [§6](#6-the-relayout-on-every-modify-question)) and must survive
  the rework.

---

## 2. Traced behaviour on branching graphs

Each trace below is the algorithm above executed by hand. `col` = column after step 4.

### A. Split with merge, **equal** branch lengths — the ticket's stated basic case ✔ works

```text
S → G ; G → A ; G → B ; A → M ; B → M ; M → E
col:  S=0  G=1  A=2  B=2  M=3  E=4
col 2 = {A, B} → Y = 155 / 245 ;  all other columns single → Y = 200
```
Two branches get their own lanes, the merge and the end return to the centre row, nothing overlaps.
**No change needed for this case.**

### B. Split with merge, **unequal** branch lengths ✘ overlap

```text
S → G ; G → A ; A → C ; C → M ; G → M ; M → E
col:  S=0  G=1  A=2  C=3  M=4  E=5
every column holds exactly ONE node → every node at Y = 200
```
The short branch `G → M` is drawn straight through `A` and `C`. **Still one split and one merge — so
this is inside the ticket's scope, and it is broken.** Unequal branch lengths are the norm, not the
exception: "if the check fails, skip the two follow-up steps" is the archetypal exclusive gateway.

### C. Split **without** merge — 48 % of real gateway processes ✘ overlap when lengths differ

```text
S → G ; G → A ; A → E1 ; G → E2
col:  S=0  G=1  A=2  E1=3  E2=3
col 2 = {A} → Y = 200 ;  col 3 = {E1, E2} → Y = 155 / 245
```
`A` sits on the centre row while its own end event is pushed to a lane — and the `G → E2` flow crosses
`A`. The `1 split, 0 merge` shape is **176 of 368** gateway-bearing schemas
([capture §4](eng-91853-gateways-and-flows-serialization-capture.md#4-process-level-shapes--what-the-layout-has-to-handle));
the ticket's stated basic case (`1 split, 1 merge`) is **35 of 368**. A lane model built only for
split→merge covers the wrong 10 %.

*Equal-length* split-without-merge (`G → A → E1`, `G → B → E2`) does work, by the same mechanism as
case A.

### D. Retry loop ✘ collapse — 14 % of real gateway processes

Real process, `BulkFileManagement/DeleteFilesInTable`
([capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim)):

```text
Start → EG2 ; EG2 →(default) Script → Formula → EG1 ; EG1 →(conditional) EG2 ; EG1 →(default) Terminate
```

```text
inDegree: Start=0, EG2=2, Script=1, Formula=1, EG1=1, Terminate=1
queue=[Start] → EG2: col=1, remaining 2→1 → NOT enqueued
queue empty. Traversal ends after one step.
col: Start=0, EG2=1, Script=0, Formula=0, EG1=0, Terminate=2   (Terminate = maxNonEnd+1)
col 0 = {Start, Script, Formula, EG1} → four shapes stacked at X = 60
```
**Four of six elements in one column.** The `if (queue.Count == 0)` fallback does not help: the queue is
non-empty at the start, it just drains early. `53 of 368` gateway-bearing schemas contain a back-edge,
and the designer's own answer to a loop is to keep everything on one row and route the back-edge below
via `CI10` polyline points — which the toolkit cannot do (see
[traps T-2](eng-91853-gateways-and-flows-traps.md#t-2--visualtype-defaults-to-polyline-and-the-toolkit-writes-no-polyline)).

### E. Three-way split with merge ✔ works

```text
col 2 = {A, B, C} → Y = 110 / 200 / 290 ; merge alone in col 3 → Y = 200 = mean ✔
```

---

## 3. The three defects, stated precisely

| # | Defect | Root cause | Impact |
|---|---|---|---|
| **L1** | A branch's Y is not stable along its length | Y is staggered **per column**, not per branch. A column with one node always lands on the centre row, whatever branch that node belongs to. | Cases B and C: flows drawn through elements. Hits the ticket's own basic case. |
| **L2** | A back-edge collapses the layout | Kahn layering seeded from in-degree 0; a loop target never reaches 0, the queue drains, unreached nodes keep `column = 0`. | Case D. 14 % of gateway processes. |
| **L3** | Merge alignment is accidental | The merge lands on the centre row only because it happens to be alone in its column. Put anything else in that column and it is staggered away from its split. | The ticket's *"merge point aligned with its split"* is not actually guaranteed. |

Two smaller gaps that belong in the same change:

- **L4** — no gateway `Size`. `Layout` has no gateway constant; a handler without `DefaultSize` yields
  `(0,0)` and mis-centres by ~27 px ([traps T-11](eng-91853-gateways-and-flows-traps.md#t-11--a-gateway-with-no-size)).
  Corpus value: **55 × 55**.
- **L5** — `VerticalStep = 90` vs a measured designer branch separation of **129 px** (median over 351
  canonical gateways). 90 px still clears a 55-px gateway, so this is polish, not correctness — but it
  is free to fix while the code is open.

---

## 4. The proposed algorithm

Keep phases 1–3 (adjacency, columns, start/end pinning) and replace the per-column Y stagger with a
**lane model**.

```text
Phase 1  Adjacency from schema.FlowElements, preserving each source's flow DECLARATION ORDER.
         (Order is load-bearing: it is also the runtime's branch precedence — traps T-7.)

Phase 2  Back-edge classification. DFS from every start event, then from any still-unvisited node.
         An edge to a node currently on the DFS stack is a BACK-EDGE. Exclude back-edges from
         phases 3 and 4; they still get drawn, they just do not constrain geometry.   [fixes L2]

Phase 3  Columns. Longest-path layering over the remaining DAG, seeded from start events plus any
         DAG in-degree-0 node (so an unreachable fragment still lays out rather than piling up).
         Keep the existing post-pass: start → 0, end/terminate → maxNonEndColumn + 1.

Phase 4  Lanes. Walk the DAG in column order and assign an integer lane per node:
           • start event                     → lane 0
           • single predecessor              → inherit the predecessor's lane
           • a split (source has >1 outgoing) → the FIRST-declared branch keeps the parent's lane;
                                                each subsequent branch takes the next free lane
                                                DOWNWARD (+1, +2, …) in the columns it spans
           • several predecessors (a merge)   → round(mean(predecessor lanes)), then the nearest
                                                free lane in that column                       [fixes L3]
         A lane, once taken by a branch, is reserved for that branch across the columns it spans,
         so a node cannot drift back to the centre row.                                        [fixes L1]

Phase 5  Y. y(lane) = CenterY + lane * BranchStep. Any residual (column, lane) collision — possible
         in graphs outside the basic case — is pushed to the next free lane in that column, which
         degrades gracefully instead of overlapping.
         Position = (StartX + column * StepX,  y - Size.Height / 2).
```

New constants: `Layout.GatewaySizePx = 55`, `Layout.BranchStep = 130`. `VerticalStep = 90` stays, used
only by the phase-5 collision fallback.

### Why branches go **downward** rather than being centred on the parent

A centred fan (lanes `−1, +1` for two branches, `−1, 0, +1` for three) is prettier, but it moves the
*existing* branches whenever a branch is added. `ProcessModifyHandler` re-runs the layout on **every**
modify and saves (`ProcessModifyHandler.cs`, the `layoutEngine.Apply` → `SaveEdited` sequence), so with
a centred fan a single `addFlow` reshuffles the whole diagram and a human reviewing the process sees
noise instead of a change. Downward assignment keeps every previously placed branch where it was.

It also makes the invisible visible: **top-to-bottom lane order equals runtime evaluation order**
(phase 1 preserves declaration order; the runtime takes the first `true` in array order —
[platform-reference §5.2](eng-91853-gateways-and-flows-platform-reference.md#52-evaluation-order-is-array-order-and-is-not-encoded-anywhere)).
A human can then read branch precedence off the diagram, which is otherwise impossible: there is no
priority field anywhere in the metadata.

The corpus supports the shape but not a specific geometry: `dy = 0` is by far the most common single
branch offset (379 of 974 measured branch targets), i.e. **one branch keeps the parent's row** — and the
remaining offsets are hand-dragged noise in both directions
([capture §5](eng-91853-gateways-and-flows-serialization-capture.md#5-branch-geometry--there-is-no-canonical-designer-layout-to-copy)).
There is nothing canonical to imitate, so pick the property that serves incremental editing.

### Verifying the algorithm against the traces

| Case | Result under the proposed algorithm |
|---|---|
| A — split/merge, equal | `A` lane 0, `B` lane 1, `M` = round(mean(0,1)) = 0 or 1 → aligned with the split. ✔ |
| B — split/merge, unequal | `A` lane 0 → `C` inherits lane 0; `M` (short branch) lane 1 across its columns; `M` = round(mean(0,1)). No crossing of occupied cells. ✔ |
| C — split, no merge, unequal | `A` lane 0 → `E1` lane 0; `E2` lane 1. The `G → E2` flow no longer crosses `A`. ✔ |
| D — retry loop | Back-edge `EG1 → EG2` excluded ⇒ the DAG is a straight chain ⇒ `Start=0, EG2=1, Script=2, Formula=3, EG1=4, Terminate=5`, all on lane 0 — exactly the single row the designer produced. ✔ |
| E — three-way | lanes 0/1/2; merge = round(mean(0,1,2)) = 1. ✔ |

---

## 5. What stays out

- **Nested branching, several merge points, asymmetric multi-level branches, long chains** — ENG-95890
  by the ticket's own split. The phase-5 collision fallback is what keeps those *readable-ish* instead
  of overlapping, and it is the honest boundary: this ticket guarantees no-overlap for one split level,
  and degrades gracefully beyond it.
- **Polyline routing.** The toolkit will not compute `CI10` points. Setting
  `VisualType = AutoPolyline` (traps T-2) hands routing to the designer, which is both correct and free.
- **Lane sets / swimlanes.** `PlaceNewElement` already honours a named lane
  (`ProcessGraphBuilder.cs:118-124`); the layout engine is lane-set-agnostic and stays so.
- **X-axis polish.** All nodes in a column share a left edge, so shapes of different widths have
  uneven centre spacing. The designer's own spacing is uneven too (measured centre gaps in the §6
  capture: 108, 126, 140, 113, 161 px). Not worth touching.

---

## 6. The relayout-on-every-modify question

Worth flagging even though the recommendation is to change nothing.

`ProcessModifyHandler` applies operations, then calls `layoutEngine.Apply(schema)` over the **whole**
schema, then saves. So any `modify-business-process` call **overwrites the position of every element**,
including ones a human arranged by hand in the designer. Today that is nearly harmless: toolkit-built
processes are a single row and the layout is idempotent. With branches it becomes a real question,
because the corpus shows humans *do* arrange gateway diagrams by hand (median branch separation 129 px
with wide spread, loop-backs routed below the row).

Options considered:

| Option | Verdict |
|---|---|
| Keep always-relayout | **Recommended for this ticket.** Simple, deterministic, and idempotent on an unchanged graph. |
| Position only newly added elements | Better for humans, but needs a "which elements are new" concept the modify path does not have, and leaves a mixed diagram where new elements ignore the human's arrangement. Scope creep. |
| Skip layout when every element already has a non-default position | Cheap heuristic, but silently different behaviour on two similar processes — the worst kind of surprise. |

Decision: keep always-relayout; make the new algorithm **deterministic and stable** (a change adds
lanes, it does not move existing ones); and record the trade-off in the guidance article so an agent
knows that editing a hand-arranged process will re-flatten it. If it becomes a complaint, it is a
follow-up ticket with a real design, not a heuristic bolted on here.

---

## 7. Testing

`ProcessLayoutEngineTests` already exercises the engine directly against an in-memory `ProcessSchema`
with no composition root, and has the helpers this work needs (`AddNode`, `Connect`, `ColumnX`) —
`tests/CrtProcessBuilder/ProcessLayoutEngineTests.cs:20-50`. Add a `ConnectConditional` /
`ConnectDefault` pair and the cases above:

| Test | Asserts |
|---|---|
| `Apply_SplitAndMergeEqualBranches_PlacesBranchesOnSeparateLanes` | distinct Y per branch; merge Y between them |
| `Apply_SplitAndMergeUnequalBranches_KeepsEachBranchOnItsLane` | **L1** — the long branch's second node keeps its lane |
| `Apply_SplitWithoutMerge_KeepsEachBranchOnItsLane` | **L1** — the 48 % shape |
| `Apply_BackEdge_LaysOutTheAcyclicChainLeftToRight` | **L2** — the `DeleteFilesInTable` topology gives 6 distinct columns |
| `Apply_MergeWithSiblingInSameColumn_AlignsMergeWithItsSplit` | **L3** |
| `Apply_Gateway_UsesGatewaySize` | **L4** — 55×55, and Y centring matches a 31-px event on the same lane |
| `Apply_ThreeWaySplit_AssignsLanesInFlowDeclarationOrder` | **T-7** — first-declared branch on the top lane |
| `Apply_UnchangedGraph_IsIdempotent` | two consecutive `Apply` calls give identical positions |
| `Apply_AddingABranch_DoesNotMoveExistingBranches` | the stability property that justifies downward lanes |

Every assertion carries a `because:`, every test an `[Description]`, per the repository test policy. The
engine takes no `UserConnection`, so none of these needs a mock.
