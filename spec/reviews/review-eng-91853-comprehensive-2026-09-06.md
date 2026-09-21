# ENG-91853 — comprehensive pre-merge review gate

Run 2026-09-06 over the **complete diff** of all three repositories, for the three open pull requests.
Nine incremental review rounds preceded this; none of them was a single sweep end to end, and this is
that sweep. Five lenses — four adversarial agents plus the reviewer's own build-and-mutation lens.

| Repository | Base → HEAD | PR |
|---|---|---|
| package | `7e93995` → `ed5f25d` (`main`) | GHE #45 |
| clio | `a9deb32bc` → `f17e8ba4a` (`master`) | #1398 |
| guidance | `84e2609` → `25e3790` (`master`) | #135 |

## Verdict

**Do not merge yet. One finding is serious enough on its own: the toolkit can build a process whose
runtime silently takes an extra branch, and nothing on any surface says so.** Seven further High
findings follow, six of them contradictions between surfaces that an agent reads *instead of* the code.

Everything the ticket set out to deliver works and is proven on a stand. The failures below are at the
edges the nine rounds did not sweep together: one runtime shape nobody modelled, and a set of texts
that drifted apart while each was individually correct when written.

## Evidence base — what was actually executed

| Check | Result |
|---|---|
| package suite `-c dev-nf` | **1244 pass, 0 fail** |
| clio targeted suite `-c Release`, four modules | **10136 pass, 0 fail, 18 skipped** (6 m 44 s) |
| mutations executed by the reviewer this pass | **8** — 6 confirmed pinned, **2 confirmed unfalsifiable** |
| platform claims re-read at the relocated tree | `FlowConditionalGateway`, `FlowSchema`, `FlowVisitor`, `ReadDataUserTask`, `MetaItemCollection` |
| one lens claim **refuted by measurement** | see *Refuted* |

---

## HIGH

### H1 — a decision plus a stray branch: two unconditional siblings beside a conditional

**Owner: `CrtProcessBuilder`.** Verified by the reviewer against platform source, not taken on report.

`FlowKindRules.EnsureAtMostOneDefault` counts only flows whose **kind token** is `default`. The runtime's
notion of the fallback is different: any flow that is not a `ConditionalSequenceFlow`, and it removes
exactly **one** of them.

```csharp
// FlowConditionalGateway.Accept
if (GetIsDefSequenceFlow(sequenceFlow)) { ResultSequenceFlows.AddIfNotExists(sequenceFlow); continue; }
...
if (result) {                                   // a conditional matched
    ResultSequenceFlows.Add(sequenceFlow);
    if (ConditionEvalStrategy == ConditionEvalStrategy.Exclusive) { RemoveDefSequenceFlow(ResultSequenceFlows); … }

// RemoveDefSequenceFlow — Find + ONE Remove
SequenceFlow defSequenceFlow = sequenceFlowList.Find(GetIsDefSequenceFlow);
if (defSequenceFlow != null) { sequenceFlowList.Remove(defSequenceFlow); }

// GetIsDefSequenceFlow — plain AND default both match
return defSequenceFlow.BpmnElementName != BpmnElementVocabulary.ConditionalSequenceFlowName;
```

`OnVisited` then returns **every** flow still in `ResultSequenceFlows`.

**Failure.** One `create-business-process` call, source an ordinary user task:

```json
"flows":[{"source":"Approve","target":"A"},
         {"source":"Approve","target":"B"},
         {"source":"Approve","target":"C","kind":"conditional","condition":"[#Amount#] > 100"}]
```

Nothing refuses it: the source is not a gateway, so `EnsureKindSuitsANonDecidingGateway` and
`NormaliseForADecidingGateway` return early, and `EnsureAtMostOneDefault` sees no `default`. At run time
the platform synthesizes the exclusive gateway, takes the matched conditional, removes **one**
unconditional flow and starts the other alongside it. `describe` reads back
`sequence / sequence / conditional`; the response carries no notice.

**Why it is High and not Blocker.** `validate-process-graph` does emit R12 on this shape — but only on
the plan check, only as an advisory warning, and its text ("implicit parallel split — confirm intent")
describes the all-plain shape, not "your decision will also take a second branch". An agent that builds
without validating gets nothing.

**Corpus.** 736 shipped sources carry ≥1 conditional plus ≥1 unconditional sibling; **zero** carry two
unconditional ones — the designer coerces the second into a conditional (`connection-utils.ts`). So the
toolkit can author a shape the designer cannot, which is exactly the standard `FlowKindRules` sets for
itself: *"Every rule here mirrors one the DESIGNER enforces on its own canvas."*

**Secondary, same root.** The comment justifying the narrow rule is inverted — *"the platform … picks by
collection order, so the second would be dead metadata that reads like a live branch."* It is not dead
metadata; it is a branch that runs. The same sentence is duplicated in `ProcessGraphValidator.cs`.

### H2 — validate says warning, build says fatal: an undocumented fork in both directions

**Owners: `CrtProcessBuilder` docblock + clio tool text.** Found independently by three lenses; the
reviewer verified both halves.

`ProcessGraphBuilder.ValidateStructure`'s docblock states the contract:

> This now mirrors clio's client validator error-severity rules **R1/R2/R3/R15** … the server must not
> build a graph clio's validator calls invalid. Only the advisory rules (R7/R9/R12/R17 warnings and the
> gateway advice) stay client-side.

clio's actual Error-severity set, read out of the file: **R1, R2, R3, R10, R11, R13, R14, R15.**

- **Server builds what clio calls invalid.** `Task1 --default--> A` plus `Task1 --sequence--> B` on a
  user task builds green; `describe` → `validate` returns **Error R14**. Same for a conditional flow off
  a start event (**Error R13**), which 4 shipped flows use.
- **Server refuses what clio calls advisory.** A diverging exclusive gateway with one conditional and
  one plain flow: `validate-process-graph` returns **one R7 warning, `has-errors: false`**;
  `create-business-process` throws and creates nothing. It is order-dependent, which is worse —
  `[plain, conditional]` builds and normalises silently, `[conditional, plain]` aborts, and the
  guidance's own precedence advice tells the author to declare the conditional arm first.

### H3 — the guidance's only element-output example is a branch that can never be true

**Owner: guidance.** Verified by the reviewer against the platform.

`branch-conditions.md:36` — `"condition": "[#Read.ResultCount#] > 0"`.

`ReadDataUserTask.CrtProcessDesigner.cs:128` returns early for anything but `ProcessReadDataResultType.Function`,
and `ResultCount` is assigned only at `:135`, inside that Function arm and only when
`FunctionType == Count`. clio can build `mode:"first"` only, which is Entity mode. `ResultCount` is a
declared parameter, so the name resolves, the condition is stored, and it is **always false** — the
"record found" branch never runs, the fallback always does, and `describe` reports the condition intact.

Every other surface already uses the right parameter: `data-elements.md`, `CreateBusinessProcessTool`,
and this PR's own knowledge record all say `ResultEntity`.

### H4 — the documented clear-condition route does not exist off a gateway, and its only test asserts the impossible

**Owners: clio prompt + guidance + the e2e.** Verified by the reviewer.

`ModifyBusinessProcessPrompt` and `branch-conditions.md:97-98` both state that the clear-condition
operation is `setFlow` with `kind: "sequence"`. Off a **deciding gateway** that route is refused: with a
conditional sibling present, `NormaliseForADecidingGateway` throws; with no sibling it normalises to
`default`. `sequence` never survives. Neither surface says so — and a gateway is every branch this
ticket added.

The e2e that would have caught it asserts the impossible. `ModifyBusinessProcessToolE2ETests`
builds `Decide` as an `exclusiveGateway` with `EndA` default and `EndB` conditional, then asserts
`toA.Kind == "sequence"` after `setFlow …EndA kind=sequence` — which throws on operation 2, aborting the
whole call, so the earlier `toB` assertions fail too. This is not merely an unrun test: it is a test
whose expected outcome is provably unreachable against the archive committed in this PR.

### H5 — the validator spec contradicts itself inside one commit

**Owner: `spec/ai-business-process-generation/ai-bp-connection-rules.md`.** Verified.

Line 36, added in this diff: *"Both are **warnings**, and measurement is why."*
Line 106, also in this diff, under the **errors** bullet: *"a plain sequence flow out of a DIVERGING
or-gateway (R7/R9 …)"*.

The code emits `Warning`. The next implementer who trusts the authoritative spec restores `Error` and
re-rejects the seven shipped or-gateways this PR exists to stop rejecting.

### H6 — the create tool's own description says both "by name" and "never by name"

**Owner: clio.** Verified.

- `:91` — *"…NAME here: `[#Amount#]` for a process parameter and `[#ElementName.ParameterName#]` for an
  element's…"*
- `:127` — *"(Shared with modify-business-process. **A conditional branch IS built here, through
  flows[].kind and flows[].condition** above…)"* — explicitly pulls conditions into the paragraph's scope
- `:134` — *"…a parameter is referenced by its UId meta-path, **never by name**."*

A condition is a formula. The categorical statement comes last, and `formulas.md` got the carve-out that
the tool description — the surface always in context — did not. Scope `:134` to `mappings[].expression`.

### H7 — the guidance's headline bullet is still the trap the same article fixes 25 lines earlier

**Owner: guidance.** Verified: one unqualified occurrence at `branch-conditions.md:90`.

*"Give every branching element a plain sibling"* is a hard build refusal on a gateway element. The
article fixes exactly this at `:74` for the if/else paragraph and leaves the bullet standing. PR #135's
body claims the trap was removed; it was removed from one paragraph.

### H8 — the validator throws on input its own contract and its MCP schema declare legal

**Owner: clio.** Reported by a lens; the reasoning is corroborated by the code's own comments.

`IProcessGraphValidator` promises *"Never throws on malformed input"*. `ProcessGraphNodeArg.Name` and
`ProcessGraphEdgeArg.Source/Target` have no `[Required]` and default to `null`. A null node name reaches
`ToDictionary` and throws `ArgumentNullException`; the tool's catch-all returns
*"Value cannot be null. (Parameter 'key')"* — no findings for any node, and a message naming nothing in
the caller's graph. The validator elsewhere *depends* on this throw to justify deleting a null guard,
which records the contract as false in the same file.

---

## MEDIUM

**M1 · layout · the negative-lane bound is unfalsifiable — MUTATION EXECUTED, suite green.**
Deleting `above >= 0` from `FindFreeLane` leaves 1244/0. It is reachable: three roots in column 0 drive
`above` to −1 → lane −1 → Y = 55; five roots reach Y = −75, off-canvas.

**M2 · layout · the case-B decision is pinned only on the shape that is not the common one — MUTATION EXECUTED, suite green.**
Changing `sourceColumn + 1 <= lastSpanColumn` to `<` leaves 1244/0. The only case-B test uses a
**two**-element long arm, so its span survives the edit; the **one**-element arm — which the layout spec
calls "the norm" — silently reverts to the mean rule, leaving the corridor empty and the connector
drawn through the other branch. That is the owner decision quietly undone.

**M3 · clio · R8 is blind to the split the product's own guidance recommends.** `orGateways` is built
from gateway *nodes*, so the rule cannot see the exclusive gateway the platform synthesizes at an
activity with sibling conditional flows. `s→A`, `A -cond-> B`, `A -cond-> C`, `B→and`, `C→and` hangs in
Running forever and returns zero findings. 329 shipped elements are non-gateway sources with more than
one conditional/default outgoing, and `branch-conditions.md` says *"NO GATEWAY IS NEEDED"*.

**M4 · clio · three flow/condition combinations validate clean and the builder refuses.**
`kind:"conditional"` with `condition` omitted; `kind:"sequence"` with a condition; `kind:"default"` with
a condition. All three return `has-errors: false` and all three abort the build. The second is reachable
straight from `describe`, which deliberately reports condition text on non-conditional flows.

**M5 · package · the three-segment condition body gets the error this class exists to remove.**
`[#Read1.ResultEntity.Amount#]` passes through untouched and the platform's pre-save gate aborts the
whole build with *"Formula value error: Expression expected (at index 0)"*, naming neither the flow nor
the token. The class's own docblock measures this shape at **242 of 487** element-output conditions —
the majority — and the head already resolves through the same lookup, so a named refusal is available.

**M6 · package · the 2 048-character condition bound is defeated ~18× by the expansion added in the same PR.**
`AddFlow` bounds the caller's text explicitly because *"running the converters is what a pathological
length costs"*; `ResolveOnBuild` then rewrites it afterwards and never re-checks. A parameter meta-path
is 93 characters wrapped, an element output 142, so 2 KB of `[#a.b#]` tokens becomes ~36 KB per flow
against a 1 000-flow request cap. Not a proven hang — the expanded text is well-formed — but the bound
is one line from being restored on the resolved string.

**M7 · package · `setFlow` reports success, writes nothing, says nothing, where `addFlow` raises a notice.**
On a gateway whose only outgoing flow is its default, `setFlow kind=sequence` normalises to `default`,
hits the no-op return before `NoticeIfNormalised`, and answers `success: true`. `addFlow` in the same
state raises the notice. The reorder correctly removed a message that lied about a write; it did not
replace it with the true statement.

**M8 · docs · the layout specification describes the rule the code no longer implements.**
`layout-addendum.md` §2 still says case B *"is NOT fixed … and cannot be fixed by placement alone"* and
*"needs a decision"*; `layout.md` §4 still gives the mean rule with no skip arm and row B still asserts
`round(mean(0,1))`; `README.md` still indexes it as an open decision. All of these files are **added** by
PR #1398, so this is what ships. Per AGENTS.md a fact that stopped being true is deleted, not left.

**M9 · docs · the de-duplication rationale describes an impossible failure.** `BuildAdjacency`'s
comment says a duplicated pair *"decrements a node's counter past zero … and the layering silently
degrades"*. It cannot: the counter is incremented twice and decremented twice. The de-dup **is**
load-bearing — for `AssignBranchLanes`, where a duplicate consumes a lane — and that is pinned. Only the
stated reason is false, in four places including the diary.

**M10 · docs · `McpCapabilityMap.md:751`** still describes the tool as checking *"default without a
sibling conditional"* — the unscoped R14 phrasing this PR exists to fix — eight lines below the bullet
the PR corrected.

**M11 · spec · two stale statements the sibling files had corrected**: `ai-bp-connection-rules.md`
still calls the validate-vs-build fork "a known divergence" where gateways are unbuildable (the QA spec
was corrected in the same commit), and still carries the retracted *"7 shipped flows"* empty-condition
count that `ProcessGraphValidator` retracts to three in this same PR.

---

## LOW

- `ValidateProcessGraphTool` says it validates *"against the BPMN connection rules R1–R17"* with no
  enforced-subset qualifier, while two other surfaces name the subset. R6 is deliberately unenforced.
- `ValidateProcessGraphPrompt` tells the agent *advisory warnings are optional to address* — now
  covering R8 (instance hangs forever) and R7/R9-no-default (instance suspended).
- R13 is the only ERROR that still rejects shipped, running content (4 shipped flows); every sibling
  rule got a "why it is a warning" note in this PR and R13 got none.
- R17 compares element types with case-sensitive literals in a validator whose type resolution is
  deliberately case-insensitive, so it never fires for the lowercase build/describe tokens.
- `ResolveDataId` promises a round trip that three shipped build tokens (`readdata`, `changedata`,
  `preconfiguredpage`) fail, resolving to `Unknown`.
- `setFlowCondition` is documented as an **alias** of `setFlow` in two places. It is not: the two differ
  in both directions.
- `ProcessDesignerRequiresPackageAttributeTests` asserts `1.4.0.60` while its `because:` prose describes
  `.58`; `ModifyBusinessProcessCommand`'s floor rationale is copy-pasted from the create path and its
  central argument is a build-path fact.
- Three package guards no mutation can redden: `_notices?.Add` (the `?.` can only swallow a notice), the
  empty-body early return, and `segments.Length == 2` — the last one silently truncates
  `[#Read.ResultEntity.Name#]` under a `>= 2` mutation.
- Stray `//` at the end of two comment sentences; `CheckParallelJoinDeadlock`'s `outgoing` parameter is
  unused; `DivergesIntoTwoBranches` mixes filtered and unfiltered index spaces (correct by accident);
  `Layout.VerticalStep` is referenced by nothing; a BOM appeared on one file and vanished from another.
- Test-file gaps: `Apply_NestedSplits_DoNotOverlap` asserts unique `Point`s, which does not imply
  non-overlap across two shape sizes; every X assertion recomputes the engine's own formula, so
  `StartX`/`StepX` are pinned by no literal; two stale rationales name a mechanism the code no longer uses.

---

## Refuted by measurement

A lens reported that the two `Count > 0` filters in `DivergesIntoTwoBranches` are unfalsifiable and that
"the whole suite stays green". **Executed: RED.**
`Validate_ShouldNotWarnR8_ForAParallelSectionInsideARetryLoop` fails, exactly as it did when the same
mutation was run in the first gate. The lens analysed the fixture instead of running it. The guards are
pinned.

## Verified clean — stated so it is not re-reviewed

- **The bundled archive, sixth check.** All four pins match the bytes committed: `1.4.0.63`,
  `/Date(1788690350000)/`, producing commit `4aed165`, SHA `0B23D96A…` computed. The restamp **is**
  committed (`ed5f25d`, clean tree). No `[RequiresPackage]` literal exceeds the bundled version.
- **Six mutations on the newest code all RED**: `IsTheDefault`, the no-op short-circuit,
  `hasPlainFallback`, R12's role filter, the case-B skipping override, and the R8 `Count > 0` filters.
- **Both of the first gate's High findings are closed**: R7/R9 are now `Warning`, and R14 carries the
  `plainSiblingLeadsToAGateway` exemption mirroring `GetOutgoingsDefFlows`. Post-fix R14 error rate over
  the whole corpus is **0**.
- **Determinism and state in the layout engine**: no dictionary or set enumeration reaches a coordinate;
  no per-call collection is hoisted into a field.
- **Every corpus figure in the new comments and records reproduces**: 45, 65, 14, 7, 1, 736, 329.
- **Platform facts in the new knowledge records verified** at the relocated tree.
- **Ordering of `ResolveOnBuild`** is provably right: nothing after it creates a name-addressable
  parameter.
- **The bare-name refusal is sound beyond the corpus** — every macro family in the platform's converters
  is dotted, so a bare identifier cannot be a platform macro.
- **Knowledge base, DI, analyzer, shipped templates and the curated-knowledge fixture** all conform.

## What to do before merging

H1 needs a rule and a decision — refusing a second unconditional sibling beside a conditional is the
designer's own behaviour and the cheapest fix. H2 needs the docblock corrected and the fork stated on
the tool surface. H3, H5, H6, H7 are text edits. H4 needs the two texts scoped and the e2e assertion
corrected — it cannot pass as written. M1 and M2 each need one test.
