# ENG-91853 — Exclusive/parallel gateways, conditional/default flows, basic Y auto-layout

Analysis and implementation plan for
[ENG-91853](https://creatio.atlassian.net/browse/ENG-91853) (Task · component *bpms tools* · Major ·
status **HOME WORK** · reporter Yan Lypnytskyi · assignee Dmitro Krestov · estimate ~2.5 d).

**Task 15** of [Task list: Add business process generation via AI instructions](https://creatio.atlassian.net/wiki/spaces/TER/pages/4758143001);
parent research ENG-90883. Scope was narrowed by splitting out **ENG-95889** (inclusive OR + event-based
gateways) and **ENG-95890** (branch-aware layout for complex processes).

> **Revised 2026-09-05.** First written 2026-08-27, before ENG-95891 shipped. ENG-95891 is now
> **merged in all three repositories**, and it closed four of the sixteen traps and delivered the whole
> condition half. The corpus figures were also recounted on the wider scope ENG-95891 used, so the two
> spec folders agree. [§ State of play](#state-of-play-2026-09-05) is the delta; every document below is
> current as of the revision.

| # | Document | What it settles |
|---|---|---|
| 1 | [serialization-capture](eng-91853-gateways-and-flows-serialization-capture.md) | The ticket's *"capture gateway + conditional/default-flow serialization from a designer-built example before implementing"* — mined from the whole 7.8.0 corpus, not one example |
| 2 | [platform-reference](eng-91853-gateways-and-flows-platform-reference.md) | What a gateway/flow **is** on the server, in the designer client, and at run time — with `file:line` |
| 3 | [traps](eng-91853-gateways-and-flows-traps.md) | T-1…T-17, four now closed by ENG-95891; nine of the rest fail **silently** |
| 4 | [layout](eng-91853-gateways-and-flows-layout.md) | The auto-layout engine: what it does on a branch today (traced), what must change, and the redesign |
| 5 | [validator](eng-91853-gateways-and-flows-validator.md) | R1–R17 reconciliation: what to add, what to **fix** (R14 false-positives on real content), what **not** to implement (R6) |
| 6 | [plan](eng-91853-gateways-and-flows-plan.md) | Decisions D1–D13, work packages S1–S8, estimate, branch/PR/session strategy, Definition of Done |
| 7 | [test-plan](eng-91853-gateways-and-flows-test-plan.md) | Harness, mocking recipes, the case matrix |
| 8 | [layout-addendum](eng-91853-gateways-and-flows-layout-addendum.md) | **Written after implementing S4.** What §4 got wrong, measured: the midpoint tie-break it left unstated breaks its own row A, and its case B is marked ✔ but is not fixable by placement at all. One open owner decision. |
| 9 | [handoff](eng-91853-gateways-and-flows-handoff.md) | **Written when the code was done.** What the verification session needs: the three diff ranges, the three pieces nobody but the author has reviewed, what is already settled by measurement, what the three prior review rounds found, and the two build traps (`dev-nf` not `dev-n8`; `-c Release` not Debug). |

**Sources.** Platform `C:/Projects/Creatio/TSBpm/Src/Lib`; corpus `C:/Projects/PackageStore`
(Creatio 7.8.0, 1 099 packages, 19 718 `metadata.json`, **1 795** flow-element containers); designer
client `C:/Projects/creatio-ui` and `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0`;
package under change `C:/Projects/workspace/ProcessBuilder`; clio surface `C:/Projects/clio`.

---

## State of play (2026-09-05)

ENG-95891 is **merged in all three repositories** — crt-process-builder PR #42 (into `main`), clio
PR #1340 (into `master`), clio-knowledge PR #122 (into `master`). Bundled archive on clio `master`:
**CrtProcessBuilder 1.4.0.57**; `[RequiresPackage]` floors on create/modify: **1.4.0.44**.

So this ticket branches from each repository's default branch with no stacking and no rebase step —
note that crt-process-builder's default branch is **`main`**, not `master`
([plan D13](eng-91853-gateways-and-flows-plan.md#d13--branch-pr-and-session-strategy)).

### What it already delivered that this ticket had planned

| Was planned here | Now shipped by ENG-95891 |
|---|---|
| Set `ManagerItemUId` on every flow (trap T-1) | `FlowManagerItems.Sequence` / `.Conditional`, both write paths |
| Set `VisualType = AutoPolyline` (trap T-2) | done, with the corpus measurement in the code |
| Refuse an ambiguous `(source, target)` flow match (trap T-9) | `FindTheFlowBetween` refuses; `AddSequenceFlow` also refuses creating a duplicate pair |
| Refuse writing a condition onto an activity-result flow (trap T-5) | `SetFlowCondition` refuses, naming the result branching |
| `describe` flow kind from the **CLR type**, not `FlowType` (D10) | `MapFlowKind` does exactly that |
| `describe` reports the condition | `DescribeProcessFlow.Condition` + `BranchesOnActivityResult` |
| An in-place re-kind that preserves UId **and** array position (D5) | `SetFlowCondition` — the reference implementation for the `default` re-kind this ticket needs |
| A condition validator | **Deleted** by ADR `adr-collapse-formula-validation-onto-platform-rule.md`: the platform's own pre-save gate refuses a bad condition, measured on a stand. This ticket must **not** add one back. |

### What ENG-95891 explicitly handed to this ticket

Its own sprint note is unambiguous: *"The declarative build path (`flows[].kind` / `.condition`), the
default marker, gateways and branch-aware layout are ENG-91853's."* Two items are handed over by name:

- **`DescribeProcessPrompt`** was reverted to master on the project owner's scope call, so that
  ENG-91853 introduces `condition` **and** `branchesOnActivityResult` together — *"half of the contract
  is worse here than none"* (clio `09898af82`).
- **`FlowManagerItems.Default`** was deliberately left out as a would-be dead constant, because the
  package cannot build a default flow. Adding it is this ticket's job.

### What is untouched and still fully open

`ProcessLayoutEngine.cs` — last modified 2026-08-10. `ProcessGraphValidator.cs` — last touched by
ENG-90883. `Layout` has no gateway size. `BuildGraph` still refuses any flow kind but `sequence`, and
refuses `flows[].condition` outright.

---

## The seven findings that shape the work

### 1. Two of the three serialization defects are fixed; the third never existed and one new one appeared

The 2026-08-27 version of this document opened on three fields the toolkit wrote wrongly. Current state:

| Field | Designer-built content | Package today |
|---|---|---|
| `BL7` `ManagerItemUId` | set on **9 762 / 9 762** flows | **fixed** — `FlowManagerItems.Sequence` / `.Conditional` |
| `CI6` `VisualType` | `1` `AutoPolyline` on **9 762 / 9 762** | **fixed** |
| `CI5` `StrokeColor` | `FF939598` on 9 762 / 9 762 | already correct — the class field initialiser |
| `BL7` for a **default** flow | `573ed909-…` on **756 / 756** | **missing** — no constant, because no code can build one yet |

So finding 1 is now small and precise: **add `FlowManagerItems.Default` and use it**, and keep the
`(class, FlowType, ManagerItemUId, VisualType)` quadruple asserted per kind
([traps T-1](eng-91853-gateways-and-flows-traps.md)).

### 2. A default flow is not a class — and a conditional flow is not a `FlowType`

Measured across 9 762 flows, with **zero** exceptions:

```text
plain sequence   ProcessSchemaSequenceFlow      BL7=0d8351f6…  CI4 absent (0)   CI3="null"   7599
conditional      ProcessSchemaConditionalFlow   BL7=dac675d4…  CI4=2            CI3=text     1406
default          ProcessSchemaSequenceFlow      BL7=573ed909…  CI4=1            CI3="null"    756
```

The default branch is the **plain** class with `FlowType = Default` plus the *default-flow* manager
item. Setting `FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow` is not a shortcut — it
makes `ProcessSchemaFlowNode.GetOutgoingsConditionalFlowsInternal` execute
`(ProcessSchemaConditionalFlow)sequenceFlow` and throw `InvalidCastException`
(`ProcessSchemaFlowNode.cs:125-137`). `ProcessSchemaExclusiveGateway.DefaultUId` (`BX1`) occurs **0
times** in 1 099 packages: do not model the default branch as a gateway property.

### 3. Run time and design time disagree about what "the else branch" is — deliberately

- **Run time** ignores `FlowType`: `FlowConditionalGateway.GetIsDefSequenceFlow` returns true for *any*
  outgoing flow whose `BpmnElementName != "CSF"` (`FlowConditionalGateway.cs:80-83`).
- **Design time** reads `FlowType` (`ProcessSchemaFlowNode.cs:107-137`).

Two consequences, neither documented on Academy:

- **Evaluation order is array order.** First `true` wins under `ConditionEvalStrategy.Exclusive`
  (`FlowSchema.cs:747`, `FlowConditionalGateway.cs:165-176`), and nothing encodes precedence — so the
  order the toolkit inserts flows in silently decides which branch runs.
- **A split with no default and no matching condition throws** `MismatchItemsCountException`
  (`FlowConditionalGateway.cs:119-123`). That is the concrete failure R7 warns about; say so in the text.

### 4. A gateway is optional for a conditional flow — 40 % of real ones have none

Conditional-flow source element, over all 1 406:

| Source | n |
|---|---|
| `ProcessSchemaExclusiveGateway` | 709 |
| **`ProcessSchemaUserTask`** | **485** |
| `ProcessSchemaScriptTask` | 50 |
| `ProcessSchemaInclusiveGateway` | 50 |
| `ProcessSchemaSubProcess` | 33 |
| `ProcessSchemaFormulaTask` | 23 |
| `ProcessSchemaWebService` | 6 |
| `ProcessSchemaStartEvent` | 2 |
| `ProcessSchemaIntermediateCatchSignalEvent` | 2 |
| unresolved source (embedded event processes) | 45 |

`FlowSchemaGenerator.FillSequenceFlows` synthesizes a `FlowExclusiveGateway` when a source group has a
conditional flow and the source is not already an or-gateway (`FlowSchemaGenerator.cs:144-166`). This is
what let ENG-95891 ship the condition half without a gateway element.

### 5. The second condition dialect is handled on the write side — the read side is this ticket's

`ProcessActivitiesSelectedResults` (`GV2`) branches on an **activity result** — Academy's *preset
condition* route. Measured, mutually exclusive with the expression:

| Dialect | n |
|---|---|
| `CI3` expression only | 1 061 |
| `GV2` activity result only (exactly one entry) | 337 |
| neither (runtime substitutes `"true"`) | 7 |
| **both** | **0** |

ENG-95891 already refuses a condition write onto such a flow and reports `branchesOnActivityResult` on
describe. What remains here is surfacing it on the **prompt** (handed over by name) and deciding the
write side — recommendation unchanged: **own follow-up ticket** (D6).

### 6. clio's R14 flags real, shipped, production processes as invalid

`ProcessGraphValidator.CheckDefaultFlowRules` raises an **error** when a default flow has no sibling
conditional flow (`ProcessGraphValidator.cs:169-173`, unchanged). Recounted on the broad corpus:
**exactly 45** shipped gateways (40 exclusive + 5 inclusive) whose *only* outgoing flow is a default
flow — because the designer **cannot draw a plain sequence flow out of an or-gateway at all**
(`ProcessSchemaElementManager.cs:431-434`; `connection-utils.ts:72`). Examples:
`BulkFileManagement/DeleteFilesInTable`, `CaseService/RunSendEmailToCaseGroup`,
`CrtCaseCopilot/Copilot_GetCaseExternalMessages`, `BpmGDPR/BpmProcess6`.

R14 must be scoped to sources with **more than one** outgoing flow. Symmetrically **R6 must not be
implemented**: 42 exclusive gateways are 2-in/2-out.

### 7. The layout engine needs real work — and the ticket's "basic case" is the wrong 10 %

Traced on a real process, the current `ProcessLayoutEngine` puts **four of six elements in one column**,
because a back-edge starves its Kahn queue. **54 of 368** gateway-bearing containers (15 %) contain a
back-edge.

The shape the ticket names — *"one gateway splitting into branches and merging back"* — is **35 of 368**
(10 %). The most common real shape is **1 split, no merge** (**176 of 368**, 48 %). A lane model built
only for split→merge misses the majority; both are cheap together and neither is cheap alone.

Also measured: designer branch lanes sit ~**129 px** apart (median) against `VerticalStep = 90`, and
gateways are **55×55**, a size no handler sets. Full analysis:
[layout](eng-91853-gateways-and-flows-layout.md).
