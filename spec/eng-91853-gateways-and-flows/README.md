# ENG-91853 — Exclusive/parallel gateways, conditional/default flows, basic Y auto-layout

Analysis and implementation plan for
[ENG-91853](https://creatio.atlassian.net/browse/ENG-91853) (Task · component *bpms tools* · Major ·
status **HOME WORK** · reporter Yan Lypnytskyi · assignee Dmitro Krestov · estimate ~2.5 d).

**Task 15** of [Task list: Add business process generation via AI instructions](https://creatio.atlassian.net/wiki/spaces/TER/pages/4758143001);
parent research ENG-90883. Scope was narrowed by splitting out **ENG-95889** (inclusive OR +
event-based gateways) and **ENG-95890** (branch-aware layout for complex processes).
**Blocked by [ENG-95891](https://creatio.atlassian.net/browse/ENG-95891)** (formula authoring), which
ships first — see [Relationship to ENG-95891](#relationship-to-eng-95891).

Seven documents, written to be attached to the ticket. Read them in this order.

| # | Document | What it settles |
|---|---|---|
| 1 | [serialization-capture](eng-91853-gateways-and-flows-serialization-capture.md) | The ticket's *"capture gateway + conditional/default-flow serialization from a designer-built example before implementing"* — mined from the whole 7.8.0 corpus, not one example |
| 2 | [platform-reference](eng-91853-gateways-and-flows-platform-reference.md) | What a gateway/flow **is** on the server, in the designer client, and at run time — with `file:line` |
| 3 | [traps](eng-91853-gateways-and-flows-traps.md) | T-1…T-16. Ten fail **silently**; three exist in shipped code **today** |
| 4 | [layout](eng-91853-gateways-and-flows-layout.md) | The auto-layout engine: what it does on a branch today (traced), what must change, and the redesign |
| 5 | [validator](eng-91853-gateways-and-flows-validator.md) | R1–R17 reconciliation: what to add, what to **fix** (R14 false-positives on real content), what **not** to implement (R6), how the validate-vs-build fork closes |
| 6 | [plan](eng-91853-gateways-and-flows-plan.md) | Decisions D1–D12, work packages S0–S9, estimate, scope in/out, Definition of Done |
| 7 | [test-plan](eng-91853-gateways-and-flows-test-plan.md) | Harness, mocking recipes, the full case matrix |

**Sources.** Platform `C:/Projects/Creatio/TSBpm/Src/Lib`; corpus `C:/Projects/PackageStore`
(Creatio 7.8.0, 1 099 packages, 19 718 `metadata.json`, **1 664** parse as process schemas); designer
client `C:/Projects/creatio-ui` and `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0`;
package under change `C:/Projects/workspace/ProcessBuilder`; clio surface `C:/Projects/clio`.
Every number below is measured; the mining is reproducible from
[serialization-capture §8](eng-91853-gateways-and-flows-serialization-capture.md#8-reproducing-the-mining).

---

## The seven findings that change the shape of the work

### 1. Three serialization fields are wrong in the code we already ship — and gateways make them visible

Not "would be wrong once we add gateways". Wrong **now**, on every process the toolkit has ever built.

| Field | Designer-built content | `ProcessGraphBuilder.AddSequenceFlow` today |
|---|---|---|
| `BL7` `ManagerItemUId` | set on **9 144 / 9 144** flows | never set ⇒ `Guid.Empty` ⇒ **omitted from the metadata** |
| `CI6` `VisualType` | `1` = `AutoPolyline` on **9 144 / 9 144** flows | never set ⇒ `0` = `Polyline` ⇒ omitted |
| `CI5` `StrokeColor` | `FF939598` on 9 144 / 9 144 | class default is the same value — **already correct** |

`BL7` is how the designer resolves the flow's *manager item*, which carries its image and its
allowed-flow rules (`ProcessSchemaElementManager.cs:456-471`, `:725-727`). `VisualType = Polyline`
makes the designer route the flow through the stored `CI10` polyline points — which the toolkit never
writes — instead of auto-routing. Both are invisible while every element sits on one row and every
arrow is a short straight segment. Both become visible the moment a branch leaves that row.

This is inside the ticket's own deliverable ("server serialization (verified vs captures)") and it is
the cheapest high-value item in the task. See [traps T-1, T-2](eng-91853-gateways-and-flows-traps.md).

### 2. A default flow is not a class — and a conditional flow is not a `FlowType`

Measured across 9 144 flows, with **zero** exceptions:

```text
plain sequence   ProcessSchemaSequenceFlow      BL7=0d8351f6…  CI4 absent (0)   CI3="null"   7051
conditional      ProcessSchemaConditionalFlow   BL7=dac675d4…  CI4=2            CI3=text     1365
default          ProcessSchemaSequenceFlow      BL7=573ed909…  CI4=1            CI3="null"    727
```

The default branch is the **plain** class with `FlowType = Default` plus the *default-flow* manager
item. The conditional branch needs the **`ProcessSchemaConditionalFlow` class**, whose constructor sets
`FlowType` for you but **does not set `ManagerItemUId`**.

Setting `FlowType = Conditional` on a plain `ProcessSchemaSequenceFlow` is not a shortcut — it makes the
platform's own design-time helper `ProcessSchemaFlowNode.GetOutgoingsConditionalFlowsInternal` execute
`(ProcessSchemaConditionalFlow)sequenceFlow` and throw `InvalidCastException`
(`ProcessSchemaFlowNode.cs:125-137`).

`ProcessSchemaExclusiveGateway.DefaultUId` (`BX1`) exists in code and occurs **0 times** in 1 099
packages. Do not model the default branch as a gateway property. The package's existing
`FlowKinds { sequence, conditional, default }` already matches the platform.

### 3. Run time and design time disagree about what "the else branch" is — deliberately

- **Run time** ignores `FlowType` entirely: `FlowConditionalGateway.GetIsDefSequenceFlow` returns true
  for *any* outgoing flow whose `BpmnElementName != "CSF"` (`FlowConditionalGateway.cs:80-89`).
- **Design time** reads `FlowType`: `GetOutgoingsDefFlows` / `GetOutgoingsConditionalFlowsInternal`
  select on `ProcessSchemaEditSequenceFlowType` (`ProcessSchemaFlowNode.cs:107-137`).

Both halves must be written consistently, and each fails differently: a wrong `FlowType` breaks the
designer *silently*; a wrong class throws at run time or on a properties-page open.

Two runtime facts follow, neither documented on Academy:

- **Evaluation order is array order.** `FlowSchema.FindSequenceFlowsBySourceUId` is a plain `Where`
  over the insertion-ordered collection (`FlowSchema.cs:747-749`); under
  `ConditionEvalStrategy.Exclusive` the **first `true` wins** and the default is dropped
  (`FlowConditionalGateway.cs:165-176`). **The order in which the toolkit inserts flows silently
  decides branch precedence.**
- **A split with no default and no matching condition throws.**
  `FlowConditionalGateway.OnVisited` raises `MismatchItemsCountException` when
  `ResultSequenceFlows.Count == 0` (`FlowConditionalGateway.cs:119-123`). That is the concrete failure
  R7 warns about — say so in the warning text.

### 4. A gateway is optional for a conditional flow — and 40 % of real ones have none

Conditional-flow source element, measured over all 1 365:

| Source | n |
|---|---|
| `ProcessSchemaExclusiveGateway` | 707 |
| **`ProcessSchemaUserTask`** | **485** |
| `ProcessSchemaScriptTask` | 50 |
| `ProcessSchemaInclusiveGateway` | 50 |
| `ProcessSchemaSubProcess` | 33 |
| `ProcessSchemaFormulaTask` | 23 |
| `ProcessSchemaWebService` | 6 |
| `ProcessSchemaStartEvent` | 2 |
| `ProcessSchemaIntermediateCatchSignalEvent` | 2 |
| unresolved source | 7 |

`FlowSchemaGenerator.FillSequenceFlows` synthesizes a `FlowExclusiveGateway` when a source group
contains a conditional flow and the source is not already an exclusive/inclusive gateway
(`FlowSchemaGenerator.cs:144-166`). This is what let **ENG-95891** ship the condition half without
waiting for this ticket.

### 5. There is a second, documented condition dialect this ticket must not silently mangle

`ProcessSchemaConditionalFlow.ProcessActivitiesSelectedResults` (`GV2`) branches on an **activity
result** — the *Activity results* preset Academy documents as the first of the two ways to set a
condition. Measured, and **mutually exclusive** with the expression in every shipped flow:

| Dialect | n |
|---|---|
| `CI3` expression only | 1 021 |
| `GV2` activity result only (exactly one entry) | 337 |
| neither (runtime substitutes `"true"`) | 7 |
| **both** | **0** |

The code explains the zero: `CreateSequenceFlowElement` uses `GV2` when it is non-empty and **ignores
`ConditionExpression`** (`ProcessSchemaConditionalFlow.cs:216-231`), so writing both would silently
discard the expression. Recommendation: the **write path supports the expression only** (D6);
**describe must read `GV2` back** so a legacy process is not misreported as "conditional with no
condition"; the result dialect gets its own follow-up ticket.

### 6. clio's R14 flags real, shipped, production processes as invalid

`ProcessGraphValidator.CheckDefaultFlowRules` raises an **error** when a default flow has no sibling
conditional flow (`ProcessGraphValidator.cs:172-177`). Measured counter-examples: **45** shipped
gateways (40 exclusive + 5 inclusive) whose *only* outgoing flow is a default flow — because the
designer **cannot draw a plain sequence flow out of an or-gateway at all**
(`ProcessSchemaElementManager.cs:431-434` allows only conditional + default; `connection-utils.ts:72`
forces conditional), so a converging or-gateway's single continuation is a default flow by
construction. One of the 45 is `BulkFileManagement/DeleteFilesInTable`, quoted verbatim in
[serialization-capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim).

R14 must be scoped to sources with **more than one** outgoing flow. Symmetrically, **R6 must not be
implemented as an error**: 60+ shipped gateways both diverge and converge (42 exclusive gateways are
2-in/2-out). See [validator](eng-91853-gateways-and-flows-validator.md).

### 7. The auto-layout engine needs real work — and the ticket's "basic case" is the wrong 10 %

Traced on the real process above, the current `ProcessLayoutEngine` puts **four of six elements in one
column**, because a back-edge starves its Kahn queue and every unreached node stays at column 0.
**53 of 368** gateway-bearing schemas (14 %) contain a back-edge, so this is not a corner case.

And the shape the ticket names — *"one gateway splitting into branches and merging back"* — is only
**35 of 368** (10 %). The most common real shape by far is **1 split, no merge** (**176 of 368**, 48 %):
branches that never rejoin, each ending in its own end event. A lane model that only handles
split→merge misses the majority; both are cheap together and neither is cheap alone.

Also measured: designer branch lanes sit ~**129 px** apart (median), against the engine's
`VerticalStep = 90`; and gateways are **55×55**, a size no handler sets today.
Full analysis and the proposed algorithm: [layout](eng-91853-gateways-and-flows-layout.md).

---

## Relationship to ENG-95891

ENG-95891 ships first and delivers the **condition expression** — the validator seam
(`IScriptSession.Validate`), the `setFlowCondition` modify operation, and a
`ProcessSchemaConditionalFlow` construction path — for a conditional flow hanging off an **activity**
(no gateway). ENG-91853 therefore does **not** re-implement any of that. It adds:

- the two **gateway element kinds** (a new `IProcessElementHandler`);
- **flow-kind support on the build path** (`flows[].kind` + `flows[].condition`, today rejected by
  `ProcessGraphBuilder.BuildGraph`) and the **`default`** marker;
- the gateway/flow **structural rules**, on both the server build path and clio's R1–R17 validator;
- **branch-aware Y layout**;
- **describe** read-back of gateways, flow kind, condition and the `GV2` dialect;
- the three **serialization fixes** in finding 1.

If ENG-95891 slips, S1–S3 and S6–S9 of this plan stay independent; only the `condition` write path (S4)
depends on it. See [plan §7](eng-91853-gateways-and-flows-plan.md#7-sequencing-and-the-eng-95891-dependency).
