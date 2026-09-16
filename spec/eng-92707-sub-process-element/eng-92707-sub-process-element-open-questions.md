# ENG-92707 — Sub-process element: open questions and verification status

Two lists. §A needs a decision from the reporter or the owner before implementation starts. §B records
the state of the evidence after the research run was resumed to completion on 2026-09-13 — what the
second pass corrected, what it promoted to a Blocker, and what still has to be measured rather than read.

---

## A. Decisions the ticket does not make

### Q1 — Is the ticket's task numbering corrected, and where?

The description footers of ENG-92705/06/07 and the whole Roadmap page (4794843138) carry a uniform −1
offset for every task ≥ 16, against the live Confluence index (4758143001) and this repo's mirror.
Sub-process is **Task 19**; the re-sync precedent is **Task 17**. *Recommendation: fix the three ticket
footers and the Roadmap page; this plan cites the corrected numbers regardless.*

### Q2 — Does AC2's "via Task 6" get restated? — **DECIDED 2026-09-14: yes**

Task 6 is ENG-91844, **To Do**, but the capability AC2 depends on shipped through two other tickets:
ENG-92127 (element↔process mapping) and **ENG-95891 (formulas)**. Four mapping sources exist today —
`value`, `processParameter`, `sourceElement` + `sourceElementParameter`, and `expression` (a formula,
validated server-side from CrtProcessBuilder 1.4.0.0; this clio requires 1.4.0.3). None of the sources
ENG-91844 still owes — `entityColumn`, `sysSetting`, `sysVariable` — is needed to feed a sub-process
input.

Measured the same day: `describe-business-process` already returns a sub-process element's synced
parameters with `direction`, `isResult`, `source` and `value`, so the *read outputs back* half of AC2
needs no work at all.

**Owner's decision: AC2 is restated against what exists. ENG-92707 does not wait on ENG-91844.** The
missing specific sources stay a known limitation of ENG-91844.

### Q3 — Does the re-sync get its own sub-task? — **DECIDED 2026-09-14: no split**

ENG-92705 spawned [ENG-95461](https://creatio.atlassian.net/browse/ENG-95461) *"Re-sync element
parameters after the pre-configured page changes"* (Sub-task, Closed 2026-09-08) for exactly this half,
and that is where the semantics were settled.

**Owner's decision: ENG-92707 is NOT split.** Unlike the page element, our re-sync is not a separate
algorithm — the platform performs the diff and our part is the same setter call plus a snapshot around
it (D1, D2). Two tickets would edit one applier.

**Done: a `relates to` link from ENG-92707 to ENG-95461 was created** (link id 564829), so its shipped
decisions are discoverable from this ticket. ENG-92707 now links to both ENG-92705 (the parent Story)
and ENG-95461 (the sub-task that actually settled the re-sync rules).

The conventions inherited from ENG-95461, and now binding here:

* re-sync rides `setElement`; no dedicated operation shipped;
* `describe-process` reports an `inSync` flag and must **not** mutate the process;
* a retarget that would strand a reference is **refused by name**, not silently dropped;
* a renamed parameter "behaves per the agreed rule, and the outcome is reported rather than silent"
  — see Q4, which the platform settles for us.

Its description also names two neighbours our ticket does not: **ENG-92728** (parameter-usage check on
deletion) and **ENG-92729** (parameter data-type change — the same subject as our TC-04 retype case).

Consequence for the estimate: the whole of it sits on ENG-92707 — 2.5–3 days of effort over 3–4
calendar days, re-costed in [plan §7](eng-92707-sub-process-element-plan.md).

### Q4 — The rule for a **renamed** callee parameter — **DECIDED 2026-09-14**

ENG-95461's AC required a rule and required the outcome to be reported, but never stated the rule. For
a sub-process the platform states it for us: pairing runs through the `ProcessSchemaMapping` row, not
through the name (platform-reference §3.2), so a rename leaves the row intact — the element parameter
keeps its **UId** and its **value**, and only its name and caption are refreshed.

**Decision: adopt the platform's behaviour verbatim.** Report the rename as drift in the sync report,
and write it into the guidance — an agent that mapped a value by the old name needs to know the mapping
still resolves, because the link was never the name.

One caveat from §3.3: the adoption branch in `FillNewSchemaParameters` creates the row but does not call
`SynchronizeParameter`, so the *name and caption* are only corrected on the next run. That next run is
free (every design-time read re-syncs), but a test that asserts the new name immediately after one
explicit sync will fail for the wrong reason.

### Q5 — How does an agent discover **which** process to call? — **DECIDED 2026-09-14: it already can**

There is no dedicated `list-processes` tool, and an earlier draft of this document concluded from that
that "the selection half of AC1 depends on a human naming the process". **That was wrong**, and the
measurement that corrects it is below.

**The discovery route exists today and needs no new code.** Measured on the stand 2026-09-14:

```jsonc
// clio-run -> execute-esq
{"environment-name": "<env>",
 "query": {"rootSchemaName": "VwProcessLib", "operationType": 0, "allColumns": false,
           "columns": {"items": {
              "Id":              {"expression": {"expressionType": 0, "columnPath": "Id"}},
              "Name":            {"expression": {"expressionType": 0, "columnPath": "Name"}},
              "Caption":         {"expression": {"expressionType": 0, "columnPath": "Caption"}},
              "Enabled":         {"expression": {"expressionType": 0, "columnPath": "Enabled"}},
              "IsActiveVersion": {"expression": {"expressionType": 0, "columnPath": "IsActiveVersion"}},
              "HasStartEvent":   {"expression": {"expressionType": 0, "columnPath": "HasStartEvent"}}}},
           "rowCount": 10, "isPageable": true, "rowsOffset": 0}}
```

`VwProcessLib` is "Process library (view)" — a DB view whose primary display column is `Caption`, which
is exactly what the classic designer's own candidate list reads, with the same
`IsActiveVersion = true` / optional `Enabled` filters. `HasStartEvent` is a column on the view, so an
agent can even pre-screen for R16 before selecting.

An agent that does not know the view name finds it the same way: `find-entity-schema` /
`get-entity-schema-properties` over "process library".

**The trap worth writing down: `odata-read` does NOT work for this view.** Measured the same day — it
returns `success:false, "The operation was canceled"`. Someone who tries OData first will conclude
discovery is impossible, which is how this document got it wrong in the first place. The guidance must
name **ESQ** specifically.

**Decision: out of scope for ENG-92707, and not a gap.** No new tool is owed. What IS owed is a
paragraph of guidance naming the route (and the OData dead end), plus D11 accepting a caption as well
as a name so the human's natural phrasing resolves. A dedicated `list-processes` tool stays a
nice-to-have for ergonomics — its other consumers are `run-process`, the two versioning tools and
`describe-business-process`, which all assume the name is known — and belongs in its own ticket if
anyone wants it.

### Q6 — Is R16 enforced, warned, or left to guidance? — **DECIDED 2026-09-14: enforced**

**Measured: 269 of 269** resolvable callees in the 7.8.0 corpus contain a Simple start event; **zero
violations** (2 of the 271 callee UIds are not in the package store at all). The repo's own rule — a new
graph rule is measured against the shipped corpus before it is enforced — is satisfied, and the corpus
permits enforcement. Contrast the gateway rules, which had 45 + 7 + 65 shipped violators and therefore
stayed advisory.

**Decision: enforce.** A hard refusal in the sub-process applier, and an Error in
`ProcessGraphValidator`. The R-catalog preamble in `process-activity-connections`, which currently lists
R16 among "semantic or not yet enforced", moves with it — see plan D8 and D12.

Residual nuance, stated rather than hidden: the measurement proves every callee *contains* a Simple
start, not that none of them *also* carries another start kind. Either way the rule never fires a false
positive on shipped content.

### Q7 — Does the sub-process stay on the high-risk list? — **DECIDED 2026-09-14: yes, rewritten**

`process-modeling` currently keeps the sub-process on its "do not remove or rewire" list under
*"constructs the builder cannot create … sub-process"*. After this ticket the first half of that
sentence stops being true, so the line has to be touched either way.

**Owner's decision: the element stays on the list, and the line is rewritten to separate two actions.**

* **Creating** a new sub-process element — allowed, and the guidance should say so plainly.
* **Rewiring an existing one** — retargeting the callee, deleting the element, editing its parameters —
  stays high-risk.

Conditional flows set the precedent by staying on the list after they became buildable, on the reasoning
that "I can create it" and "I can safely rewrite someone else's" are different claims. For a sub-process
that reasoning is not an argument but a measurement, and the rewritten line should cite it:

* any sync on an already multi-instance element **flattens** it — 61 of 416 shipped elements (T-25);
* a retarget with live dependents sets `IsValid = false` quietly, and the process then refuses to
  **start** (T-8);
* a retarget can strand a mapping row on an `IsDynamic` parameter (T-27);
* `ProcessGraphValidator` has **no parameter or mapping rule at all**, so none of the above is caught by
  automation;
* `ProcessGraphBuilder.RemoveElement` prunes mappings by substring-matching the element UId in
  `TargetMetaPath` only, and does not prune a row where the element is the *source*.

Writing "do not touch" without those reasons produces guidance an agent works around. Writing the
reasons makes the boundary self-explaining.

### Q8 — Ship order, and does the block need a `[RequiresPackage]` floor? — **DECIDED 2026-09-14: neither**

The pattern from ENG-92705/ENG-95461: package first, then clio with a version floor. The repo's own
rule for a floor is "a capability whose absence the caller cannot be **told** about". Whether a
`subProcess` block sent to an older package is discarded with `success: true` or refused loudly is
**now measured** (§B.3 probe 3): an unknown **type** token is refused **loudly** — `exit-code 1`,
nothing written — while an unknown **block** on a known type is discarded silently with
`exit-code 0`.

**Decision: no `[RequiresPackage]` floor and no `SubProcessBlockExpectation`**, because the
`subProcess` block is accepted only alongside `type:"subProcess"` (plan **D2a**). An older package then
refuses the whole call by name and the caller IS told, which is exactly the repo's test for whether a
floor is owed. The precedent points the same way: commit 79270adbf reverted a floor raise as "a lockout
for everyone in exchange for a block most descriptors never use".

The decision is conditional on D2a holding. If the contract ever tolerates the block on another element
type — the way `ProcessElementFactory` deliberately tolerates `approval` on `type:"userTask"` — the
silent-discard path reopens and the guard goes back on the bill.

Ship order is unchanged from the ENG-92705 / ENG-95461 pattern: the package side first, then clio.

### Q9 — Does the plan expose "Execution method"? — **DECIDED 2026-09-14: no**

Academy documents the field as unavailable unless a collection is mapped, and the same reference page
contradicts itself twelve paragraphs apart about whether Parallel mode actually runs instances
simultaneously. With multi-instance out of scope (D9) the field has no meaning here.

**Decision: do not expose it, and do not encode any parallelism promise in a tool description.**

### Q10 — Which caption does the contract echo for the selection field?

Academy's element reference says **"Which process to run?"**; the tutorial and the collections article
use "Process" and the question-mark-less form. The contract field is `processName` / `processUId`
either way; this only affects tool description prose.

---

## B. Verification status — complete

The research run behind this analysis lost six of fifteen verifiers and its completeness critic to a
session limit on 2026-09-09. It was **resumed on 2026-09-13 and completed 31/31, zero errors**, so §B no
longer lists unverified claims — it lists what that second pass *changed*, and what it says still has to
be measured rather than read.

### B.1 — Six corrections the second pass forced

| Was | Is | Where |
|---|---|---|
| `ExpectedOperationContractCount = 5`, gates = 3 | **7** and **5** | `clio.tests/Common/BundledProcessBuilderPackageTests.cs:343`, `:331` |
| Archive floor 1.6.0.9 / 1.6.0.12 | **1.6.1.9**, commit `ee5188ef404dfae299a373f1d67adfa9bb13df3b` | same file, `:207`, `:219` |
| Four "not buildable" surfaces | **Five** — `ValidateProcessGraphTool.cs:50` was missed | clio |
| Three process-designer MCP tools | **Eight**, with **two** `[RequiresPackage]` floors (1.6.0.3 and 1.6.1.0) | `Tools/ProcessDesigner/` |
| Guidance pin in step | Pin **1.14.9**, local `clio-knowledge` checkout **1.14.4** — stale | `curated-knowledge-names.json:3-4` vs `bundle-source.json:6` |
| Multi-instance was a scope note | A **Blocker** — see B.2 | `ProcessSchemaActivity.cs:373-395` |

### B.2 — The Blocker the first pass missed

`SynchronizeParametersInternal` calls `Parameters.Clear()` **before** the inner `SynchronizeParameters`
where `GetCanSynchronizeParameters()` lives. So the self-reference and empty-UId guards do not protect
an element that is already multi-instance: any path reaching the setter or the interface method
rebuilds it as two collections plus three counters, discarding the callee's parameters and every mapped
value. 61 of 416 shipped elements (14.7 %) are in that state. Plan D9 and trap T-25 carry the
consequence.

### B.3 — Nine things to measure before code — **five now measured**

Measured 2026-09-14. Results first; the original list keeps its wording below.

| # | Measurement | Result |
|---|---|---|
| 2 | D5 stamping rule, against the corpus | **CONFIRMED.** 416 elements / 1 650 element parameters: `A3 == CK4` (the callee) on 1 321. Of the 329 mismatches, **293 sit on multi-instance elements**, whose collection and counter parameters are caller-created and legitimately differ — so on the single-instance path the rule holds **1 321 / 1 357 = 97.3 %**, and the whole 36-parameter residue is one element in one package (`LeadFinance / QualificationSubProcess`). Of the 629 parameters carrying a value, **581 (92.4 %) stamp `L8.GS5` with the CALLER**, 47 with the callee (the known dead mappings), 1 neither. |
| 3 | Q8 — older package: loud refusal or silent discard? | **BOTH, and the difference decides the design.** Unknown *type* token → loud refusal, `exit-code 1`, nothing written. Unknown *block* on a known type → **silent discard**, `exit-code 0`, describe shows a healthy element, no warning. See plan **D2a**: bind the block to the type token and neither a floor nor a `BlockExpectation` is owed. |
| 4 | R16 corpus count | **269 of 269** resolvable callees contain a Simple start event; **zero violations** (2 of 271 callee UIds are not in the package store). D8 moves from *warn* to *enforce*. |
| 5 | Build token round trip | `ManagerMap.ResolveDataId` carries `"callactivity"` and **no** `"subprocess"` arm; the file's own comment at `Schema.cs:1125-1136` records this exact trap for seven earlier tokens. D4 stands: add the arm and a `[TestCase]`. |
| 6 | Runtime name-binding, including the negative case | **CONFIRMED IN SOURCE, both directions.** Inbound (`InitParameterValues`) and outbound (`WriteParametersToInterpretedOwner`) both call the same `ProcessInstanceParametersDataWriter.CopyCurrentValue`, which keys on `reader.CurrentName` and resolves it through `FindScalarParameterByName`. An unmatched name returns `false` from `TryGetProcessParameterPath`, the write is skipped, and there is **no else branch, no throw and no log**. `Direction` is never consulted. See [platform-reference §6](eng-92707-sub-process-element-platform-reference.md) for the quoted chain. This was the load-bearing justification for AC3 and it is no longer single-sourced. |
| 7 | `get-process-signature` response shape | **Sufficient.** `ProcessSignatureParameter` carries `caption`, `clrType`, `dataValueTypeId`, **`direction`**, `isLookup`, `referenceSchemaUId`. It does **not** carry `isResult`. D6 and D11 hold. |

Still open (1, 8, 9): the flattening and both NRE paths need the package test harness built; the E2E
fixture needs a callee on a stand.

**A safe pair now exists on the stand** (`Custom`): `UsrTc92707CopyCaller`
(`61640769-f56a-5573-8f26-412ca5d44cec`, 14 elements, `SubProcess3` with 7 mapped parameters) calling
`UsrTc92707CopyCallee` (`ca97ad92-9bf9-54c0-8da1-7299b4a8f2d9`, 24 elements). Both are copies of
`CrtTouchPoint/OptionsForSearchingAndCreatingContact` and `CrtWebForm/SearchingAndCreatingContact`,
re-UId'd into `Custom` and re-pointed at each other, so they can be modified without touching installed
content. Re-pointing took **39** rewrites, not one: the callee's UId also stamps every synced element
parameter's `A3`/`A4` and every mapping row's `GT4`.

That pair is still **not** side-effect-free: the callee carries two `AddData` and two `ChangeData`
elements, so a runtime leg writes Contact records. Since probe 6 is now settled in source, a run buys
observable confirmation rather than the mechanism itself.

**A shipped caller can supply that, and 38 of the 73 corpus packages that contain a sub-process element
are installed on the stand.** It is already earning its keep read-only (see the capture, §5a). It does
**not** close probe 6 as-is, for one reason: the negative case needs a callee parameter RENAMED, and
renaming a parameter on a shipped callee mutates installed content that other callers share — 63 of the
271 callees are called from more than one caller schema. The same objection blocks using a shipped
multi-instance caller to demonstrate T-25: demonstrating it means letting a save flatten a real process.

The cheap, safe route for both is one designer action: copy a small shipped caller into `Custom`
(`StartProcessWebhookToEntityInEngagementTools` and `AddContactsToAccount` are the smallest
multi-instance ones; `OptionsForSearchingAndCreatingContact` is a clean 9-parameter single-instance
one), or hand-build a minimal pair. Everything after that — run, rename, re-run, read `SysProcessLog`
— is CLI-only.

The critic's original list, condensed. Five are folded into plan step S1; the rest are named here.

1. **T-25 / T-1 / T-26** — reproduce the flattening and both NRE paths as package tests first. *(S1)*
2. **D5's stamping rule** — assert against the corpus that a synced parameter's `A3` equals the
   element's `CK4` while a mapped value's `L8.GS5` equals the caller. *(S1)*
3. **Q8** — does an older CrtProcessBuilder refuse a `subProcess` block loudly, or discard it with
   `success: true`? Decides whether `SubProcessBlockExpectation` is mandatory. *(S1)*
4. **R16 corpus count** — do shipped callees actually all begin with a Simple start event? *(S1, D8)*
5. **The build token round trip** — decide it in `ProcessDesignConstants` first, then confirm
   `validate-process-graph` and `describe-business-process` agree on it. *(S1, D4)*
6. **Runtime name-binding** — the negative case. Create a callee with `Alpha` (In) and `Beta` (Out),
   rename one underneath a caller, run it, and confirm the mismatch is skipped silently rather than
   thrown. This is the whole business justification for AC3 and V8 currently tests only the positive
   case.
7. **`get-process-signature`'s response shape** — does it return `direction`, `isResult`,
   `referenceSchemaUId` and captions? D6 and D11 assume it can serve as the discovery step; nobody has
   opened `GetProcessSignatureCommand.cs`.
8. **T-27** — does a retarget really strand a mapping row on an `IsDynamic` parameter, and does
   `UpdateParameters` then dereference its `Source`?
9. **The E2E callee** — `clio.mcp.e2e` has no non-self-seeding process on a stand with declared In
   **and** Out parameters. Every sub-process E2E case needs one; it has to be arranged.

### B.4 — Still single-sourced

* **The derived corpus figures.** The base counts were reproduced independently in both passes
  (262 files, 416–420 elements, 61 multi-instance, 73 packages, one element with no `CK4`). **Not**
  reproduced: the 340 same-package vs 72 cross-package split, the six business motives, the 48 dead
  mappings and the 203 `"null"`-literal values.
* **Academy's Parallel-mode contradiction is unresolvable from documentation** — the product's own
  reference page contradicts itself twelve paragraphs apart. Do not encode a parallelism promise in any
  tool description. D9 moots it for v1.

### B.5 — Process debt, unchanged

The designer serialization capture (AC4) has **not** been taken; no `docs/knowledge/` records are
written; and there is no PRD, ADR, story or `spec/sprint-status.yaml` row. This folder is what those
would be built from, not a substitute for them — and until it is committed it has already been reported
as non-existent once *inside this very research effort*.
