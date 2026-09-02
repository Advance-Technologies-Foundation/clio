# ENG-91853 — The R1–R17 validator: what to add, what to fix, what not to implement

Two validators must agree, and the ticket names both:

| Where | What it is | File |
|---|---|---|
| **Client pre-flight** | clio's `ProcessGraphValidator`, exposed as MCP `validate-process-graph`. Validates a **planned** graph before anything is built. | `clio/Command/ProcessModel/ProcessGraphValidator.cs` |
| **Server build guard** | `ProcessGraphBuilder.ValidateStructure`. The hard guarantee that holds even when an agent skips the pre-flight. | `packages/CrtProcessBuilder/Files/src/cs/Graph/ProcessGraphBuilder.cs:213-283` |

The existing contract between them is explicit in the server's own remarks: *"the server must not build
a graph clio's validator calls invalid"* — the server mirrors the **error**-severity rules, the client
keeps the advisory ones. Every change below states which side it lands on.

The rule numbering comes from
[`spec/ai-business-process-generation/ai-bp-connection-rules.md`](../ai-business-process-generation/ai-bp-connection-rules.md).

---

## 1. Rule-by-rule status

| Rule | Spec says | Implemented? | Verdict |
|---|---|---|---|
| R1 start arity | no incoming, exactly 1 outgoing | ✔ error (`:104-117`) | keep |
| R2 end arity | no outgoing, ≥1 incoming | ✔ error (`:118-128`) | keep |
| R3 one start | exactly one start event | ✔ error (`:95-102`) | keep |
| R6 gateway arity | diverging 1-in/≥2-out; converging ≥2-in/1-out | ✘ | **do not implement — see §3** |
| R7 exclusive needs default | diverging XOR *requires* a default | ✔ **warning** (`:178-184`) | keep as warning, **improve the message** — §2.3 |
| R9 inclusive needs default | as R7 | ✔ warning (same code) | keep (ENG-95889 owns the gateway itself) |
| R10 event-based targets | each outgoing → an intermediate catch event | ✔ error (`:146-162`) | keep (ENG-95889) |
| R11 parallel/event-based flows | plain sequence only | ✔ error (`:134-140`) | keep — **0 corpus violations** |
| R12 implicit parallel split | >1 outgoing *sequence* flow from a non-gateway | ✔ warning (`:141-145`) | keep; it becomes the main "use a parallel gateway" hint |
| R13 conditional origin | only from a gateway or an activity | ✔ error (`:196-209`) | keep, **document the divergence** — §2.4 |
| R14 default needs a sibling conditional | error | ✔ error (`:170-177`) | **FIX — over-fires on 45 shipped gateways** — §2.1 |
| R15 reachability | orphan / cannot-reach-end | ✔ error (`:212-232`) | keep |
| R15 self-loop | *"self-loops … are invalid"* | ✘ **missing** | **ADD** — §2.2 |
| R17 addData chaining | advisory | ✔ warning (`:186-194`) | keep, unrelated |
| — | *"parallel converge that can deadlock"* (promised in the spec) | ✘ | **ADD as warning** — §2.6 |
| — | a conditional flow must carry a condition | ✘ (no condition in the arg shape) | **ADD** — §2.5 |
| — | at most one default flow per source | ✘ | **ADD as error** — §2.7 |
| — | a diverging or-gateway must not use plain sequence flows | ✘ | **ADD as error** — §2.8 |

---

## 2. The changes

### 2.1 FIX R14 — it currently rejects a shape only the designer can produce

```csharp
if (hasDefault && !hasConditional) {
    findings.Add(new ProcessGraphFinding(ProcessGraphSeverity.Error, "R14",
        $"Default flow from '{node.Name}' requires at least one sibling conditional flow.", node.Name));
}
```
`ProcessGraphValidator.cs:170-177`

**Why it is wrong.** An exclusive/inclusive gateway's *allowed* outgoing flow kinds are **conditional
and default only** — `AddGatewaysAllowedOutgoingSequenceFlows` never adds `SequenceFlowUId`
(`ProcessSchemaElementManager.cs:431-434`), and the designer client forces conditional on anything drawn
from an or-gateway (`connection-utils.ts:72`). So a **converging** or-gateway, whose single outgoing flow
is by definition unconditional, can only be modelled as a **default flow with no conditional sibling**.

**Measured counter-examples: 45** shipped gateways — 40 exclusive + 5 inclusive with exactly one
outgoing flow, that flow being a default. One is `BulkFileManagement/DeleteFilesInTable`, quoted verbatim
in [capture §6](eng-91853-gateways-and-flows-serialization-capture.md#6-a-designer-built-process-verbatim).
Academy's wording (*"a default flow is used when there is at least one conditional flow outgoing from the
same process element"*) simply does not contemplate the converging gateway the designer itself produces.

**Fix.** Scope the rule to a **diverging** source:

```csharp
// R14 — a default flow needs a sibling conditional only where the source actually BRANCHES.
// A converging or-gateway's single outgoing flow is a default flow by construction: the designer's
// allowed-outgoing list for an or-gateway is conditional + default only (no plain sequence flow),
// so 45 shipped gateways are in exactly this shape. Scoped by arity, not by element kind.
if (hasDefault && !hasConditional && outs.Count > 1) { … }
```

Regression test: a single default flow out of an exclusive gateway with two incoming flows must produce
**no** finding.

### 2.2 ADD the R15 self-loop rule (the ticket's explicit scope addition)

The spec's R15 says self-loops are invalid; the code never checks it. Today a `T → T` flow is accepted by
`create-business-process`/`modify-business-process` (it builds and saves) **and** by
`validate-process-graph` (no finding). At run time a self-looping task re-executes on every completion.

The designer refuses to draw one — `canConnectionCreate` requires `source !== target`
(`process-diagram-rules.ts:120-134`) — but **tolerates re-saving an existing one**, and 3 shipped flows
are self-loops, one of them in production content (`OpportunityBank /
OppManagementNeedAnalysisFinance`, `ConditionalFlow2`; the other two are the platform's own
`ProcessTests/ReRunningProcessElementCase3`).

So the rule is **refuse on author, tolerate on read** — exactly the designer's own posture:

- **Client validator: error**, rule id `R15`, for any edge with `source == target`. Safe because
  `validate-process-graph` only ever sees a *planned* graph.
- **Server `ValidateStructure`: reject** on the build path, and reject in `AddFlowOperation` on the
  modify path, so the guarantee holds when the pre-flight is skipped.
- **`describe` must not refuse** a process that contains one, and no read path may fail on it.

Note the layout engine already skips self-loops when building adjacency
(`ProcessLayoutEngine.cs:47-53`), so nothing else needs to change for the read side.

### 2.3 Improve the R7/R9 message — name the actual failure

Current text: *"Diverging gateway 'X' should have a default flow so the process never dead-ends."*

The real consequence is specific and findable:
`FlowConditionalGateway.OnVisited` throws `MismatchItemsCountException`
(`ProcessEngine.Exception.MatchCondition.ByCount`) when no condition matched and no default branch
exists (`FlowConditionalGateway.cs:119-123`). Nothing earlier objects — the platform's own
`ProcessInterpretationValidator` has no branch-coverage rule.

Proposed text: *"Diverging gateway 'X' has no default flow: if no condition matches at run time the
process instance fails with MismatchItemsCountException. Add a default flow, or confirm the conditions
cover every case."*

Stays a **warning**: 65 shipped exclusive gateways deliberately have two conditional flows and no
default.

### 2.4 Keep R13, and record that it is stricter than the platform

R13 restricts a conditional flow's source to a **gateway or an activity**. The platform is more
permissive: `AddEventStandardAllowedOutgoingSequenceFlows` grants `ConditionalFlowUId` to every start and
intermediate event (`ProcessSchemaElementManager.cs:436-440`, `:480-513`), and the corpus contains 4 such
flows (2 from a start event, 2 from an intermediate catch signal) out of 1 365.

Keep the rule as an error. It is deliberate conservatism for AI-authored graphs, and ENG-95891 already
depends on the *activity* source being permitted. But say so in the guidance article, because an agent
that reads back an existing process and re-validates it will otherwise get an error on a valid platform
process.

`RoleOf` already classifies `SubProcess`, `WebService`, `ScriptTask` and `FormulaTask` as `Activity`
(`Schema.cs:1140-1141`), which covers 112 of the 1 365 shipped conditional flows — no change needed.

### 2.5 ADD "a conditional flow must carry a condition" — and extend the edge argument

A conditional flow with no condition and no activity-result is **not** an error the platform reports: it
silently becomes the literal `"true"` (`ProcessSchemaConditionalFlow.cs:216-219`) — a branch that looks
conditional and always fires. 7 shipped flows are in that state
([traps T-6](eng-91853-gateways-and-flows-traps.md#t-6--a-conditional-flow-with-no-condition-is-an-unconditional-branch-that-looks-conditional)).

`validate-process-graph`'s edge argument is `{source, target, flow-kind}` today
(`ValidateProcessGraphTool.cs:130-133`), so the client validator **cannot see** conditions. Two halves:

- **Server (mandatory):** `BuildGraph` / `AddFlowOperation` refuse a `kind: conditional` flow whose
  `condition` is empty, unless the flow already carries an activity-result dialect. This is the
  load-bearing guard.
- **Client (recommended, one optional field):** add `condition` (optional string) to
  `ProcessGraphEdgeArg` and emit an **error** for `flow-kind: conditional` with no condition. This keeps
  the pre-flight the authority it is documented to be, at the cost of one nullable property. Purely
  additive to the MCP contract — an existing caller that omits it gets the finding only when it also
  omits the condition, which is the case worth flagging.

### 2.6 ADD the promised parallel-join deadlock warning

`ai-bp-connection-rules.md` lists *"parallel converge that can deadlock"* among the intended warnings; it
was never implemented. A `FlowParallelGateway` join holds a token set and proceeds only when **every**
incoming branch has delivered (`FlowParallelGateway.cs:53-90`) — if only one of two branches can ever
run, the instance hangs in *Running* with no exception and no log line
([traps T-16](eng-91853-gateways-and-flows-traps.md#t-16--a-parallel-join-that-can-never-complete-hangs-the-process-instance)).

Minimal no-false-positive form: **warn** when a parallel gateway with ≥2 incoming flows has incoming
branches that trace back to a common **exclusive** (or inclusive) split. Warning only — a false positive
must never block a build.

### 2.7 ADD "at most one default flow per source" — error

Corpus: **0** sources carry two default flows, in 9 144 flows. The designer keeps the invariant by
**demotion**, not refusal: promoting a flow to default converts the previous default back to the required
kind (`process-replace-menu-provider.ts:63-67`, `:114-121`).

- **Client:** error, when a source has >1 outgoing flow of kind `default`.
- **Server:** refuse on write. Do **not** replicate the designer's silent demotion — an API that quietly
  changed a flow the caller did not name would be the exact failure mode this project keeps avoiding.
  Refuse and name both flows.

### 2.8 ADD "a diverging or-gateway must not use plain sequence flows" — error, arity-scoped

The mirror of R11. From an exclusive/inclusive gateway the designer offers only conditional and default
(`ProcessSchemaElementManager.cs:431-434`; `getFlowExcludeTypes` removes plain `connection`,
`process-replace-menu-provider.ts:97-101`).

Arity scope matters, because 14 shipped exclusive gateways *do* carry a single plain sequence flow —
all of them with exactly **one** outgoing flow, i.e. legacy converging gateways from an older designer:

- **error** when the gateway has **>1** outgoing flow and any of them is plain `sequence`;
- **no finding** when it has exactly one outgoing flow (legacy-tolerated on read);
- and the **builder normalises**: asked for a single unconditional continuation out of an or-gateway, it
  emits a **default** flow — matching the 40 shipped merges and the designer's own only option.

---

## 3. R6: deliberately not implemented

The spec states *"R6 Diverging gateway: 1 incoming, ≥2 outgoing. Converging gateway: ≥2 incoming, 1
outgoing."* Implementing that as an error would reject a large slice of shipped content:

| Shape | n |
|---|---|
| exclusive, **2 in / 2 out** | 42 |
| exclusive, 3 in / 2 out | 7 |
| exclusive, 4 in / 3 out | 3 |
| exclusive, 2 in / 3 out | 3 |
| exclusive, 8 in / 6 out | 2 |
| exclusive, 1 in / 1 out | 11 |
| parallel, 1 in / 1 out | 3 |
| inclusive, 2 in / 2 out | 2 |

A gateway that both merges incoming branches and re-splits them is normal, useful BPMN — and 60+ shipped
processes use it. R6 describes the two *canonical* roles, not a constraint.

**Recommendation: do not implement R6, and record the reason in
`ai-bp-connection-rules.md`** so the next person does not "complete the rule set" and break real
processes. If any check is wanted here, it is at most a warning for the degenerate `1 in / 1 out`
gateway (14 shipped instances) — and even that is questionable.

---

## 4. Client/server parity after this ticket

The server's `ValidateStructure` must mirror every new **error** so the documented invariant holds. Its
current remark — *"the build path materializes only start/signal-start/end/user-task nodes and sequence
flows, so reachability cannot false-positive on designer-only constructs (gateways, …)"*
(`ProcessGraphBuilder.cs:82-90`) — is falsified by this ticket and must be rewritten in the same edit
([traps T-13](eng-91853-gateways-and-flows-traps.md#t-13--the-build-path-structural-guard-carries-a-remark-that-this-ticket-falsifies)).

| Rule | Client | Server build path |
|---|---|---|
| R1 / R2 / R3 / R15 reachability | error | already mirrored |
| **R15 self-loop** | **add** error | **add** rejection (build **and** `addFlow`) |
| **R14 (arity-scoped)** | **fix** | mirror |
| **one default per source** | **add** error | **add** rejection |
| **diverging or-gateway: no plain sequence** | **add** error (>1 outgoing) | **add** normalisation + rejection |
| **conditional flow needs a condition** | **add** error (needs the new edge field) | **add** rejection — mandatory half |
| R11 parallel/event-based sequence-only | error | **add** rejection |
| R7 / R9 / R12 / R17, parallel-join deadlock | warning | stays client-side |

Reachability with a **retry loop** must keep passing on both sides: 14 % of gateway processes contain a
back-edge, and both implementations use plain forward/backward BFS, which handles cycles correctly. Add a
fixture for it anyway — it is the case most likely to be broken by a well-meaning refactor.

---

## 5. Closing the validate-vs-build fork (review follow-up #6)

The fork is documented in the tool description itself:

> *"IMPORTANT: a passing graph is NOT necessarily buildable — the rules cover the full BPMN catalog
> (gateways, conditional/default flows, timers, sub-processes), while create-business-process /
> modify-business-process build only startEvent/signalStart/endEvent/userTask/sendEmail elements joined
> by plain sequence flows."*
> `ValidateProcessGraphTool.cs:44`

After this ticket the buildable slice becomes:

```text
elements:  startEvent, signalStart, endEvent, userTask (+ readData / performTask / sendEmail aliases),
           exclusiveGateway, parallelGateway
flows:     sequence, conditional (expression condition), default
```

Still **not** buildable, so the fork narrows rather than closes: `inclusiveGateway` and
`eventBasedGateway` (ENG-95889), timer/message starts, intermediate events, sub-processes, formula and
script tasks, and the **activity-result** condition dialect (its own follow-up).

Required edits, all in the same change:

1. `ValidateProcessGraphTool` `[Description]` — restate the buildable slice; keep the "not necessarily
   buildable" warning, with the shorter remaining list.
2. `CreateBusinessProcessTool` / `ModifyBusinessProcessTool` `[Description]` — add the two gateway
   `type` tokens, `flows[].kind`, `flows[].condition`, and the gateway/flow rules an agent must obey
   (one default per source; or-gateway outgoings are conditional/default; **flow order is evaluation
   order**).
3. `ProcessElementFactory`'s rejection message — it currently ends *"Gateways, conditional/default
   flows, timer/message starts, intermediate events and sub-processes are not buildable yet"*
   (`ProcessElementFactory.cs:48-55`). The supported-token list is derived from the registry and stays
   correct automatically; **the trailing sentence is a hand-written literal and must be updated.**
4. `ProcessGraphBuilder.BuildGraph`'s `NotSupportedException` for a non-sequence flow kind
   (`ProcessGraphBuilder.cs:70-79`) — replace with real support; its message names conditional/default
   flows as unimplemented.
5. `ProcessFlowDescriptor.Kind` / `.Condition` XML docs — both say *"Reserved for the upcoming
   conditional-branching support … not consumed yet"* (`ProcessDescriptorContracts.cs`).
6. `spec/backend-designer/backend-designer-manual-qa.md` — TC-C-05 records the fork and TC-D-01 asserts
   that the builder **rejects** a gateway. Both invert.
7. `guidance name=process-modeling` — a pull request in the **clio-knowledge** repository (not here),
   plus a `libraryVersion` + `sequence` bump and a re-pin of
   `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`.
8. `spec/ai-business-process-generation/ai-bp-connection-rules.md` — R14's arity scope, the R15
   self-loop rule, the three new rules, and the R6 non-decision from §3.
