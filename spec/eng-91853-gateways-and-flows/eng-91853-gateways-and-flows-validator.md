# ENG-91853 — The R1–R17 validator: what to add, what to fix, what not to implement

Two validators must agree, and the ticket names both:

| Where | What it is | File |
|---|---|---|
| **Client pre-flight** | clio's `ProcessGraphValidator`, exposed as MCP `validate-process-graph`. Validates a **planned** graph before anything is built. | `clio/Command/ProcessModel/ProcessGraphValidator.cs` |
| **Server build guard** | `ProcessGraphBuilder.ValidateStructure`. The hard guarantee that holds when an agent skips the pre-flight. | `packages/CrtProcessBuilder/Files/src/cs/Graph/ProcessGraphBuilder.cs` |

The contract between them is explicit in the server's own remarks: *"the server must not build a graph
clio's validator calls invalid"* — the server mirrors the **error**-severity rules, the client keeps the
advisory ones. Every change below states which side it lands on.

Rule numbering comes from
[`spec/ai-business-process-generation/ai-bp-connection-rules.md`](../ai-business-process-generation/ai-bp-connection-rules.md).

> **State 2026-09-05.** `ProcessGraphValidator.cs` is **unchanged since ENG-90883** — ENG-95891 touched
> the tool *descriptions* but no rule. Everything in this document is still open work.

---

## 1. Rule-by-rule status

| Rule | Spec says | Implemented? | Verdict |
|---|---|---|---|
| R1 start arity | no incoming, exactly 1 outgoing | ✔ error | keep |
| R2 end arity | no outgoing, ≥1 incoming | ✔ error | keep |
| R3 one start | exactly one start event | ✔ error | keep |
| R6 gateway arity | diverging 1-in/≥2-out; converging ≥2-in/1-out | ✘ | **do not implement — §3** |
| R7 exclusive needs default | diverging XOR *requires* a default | ✔ **warning** | keep as warning, **improve the message** — §2.3 |
| R9 inclusive needs default | as R7 | ✔ warning (same code) | keep (ENG-95889 owns the gateway) |
| R10 event-based targets | each outgoing → an intermediate catch event | ✔ error | keep (ENG-95889) |
| R11 parallel/event-based flows | plain sequence only | ✔ error | keep — **0 corpus violations** |
| R12 implicit parallel split | >1 outgoing *sequence* flow from a non-gateway | ✔ warning | keep; it becomes the main "use a parallel gateway" hint |
| R13 conditional origin | only from a gateway or an activity | ✔ error | keep, **document the divergence** — §2.4 |
| R14 default needs a sibling conditional | error | ✔ error | **FIX — over-fires on 45 shipped gateways** — §2.1 |
| R15 reachability | orphan / cannot-reach-end | ✔ error | keep |
| R15 self-loop | *"self-loops … are invalid"* | ✘ **missing** | **ADD** — §2.2 |
| R17 addData chaining | advisory | ✔ warning | keep, unrelated |
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
`ProcessGraphValidator.cs:169-173`

**Why it is wrong.** An exclusive/inclusive gateway's *allowed* outgoing flow kinds are **conditional and
default only** — `AddGatewaysAllowedOutgoingSequenceFlows` never adds `SequenceFlowUId`
(`ProcessSchemaElementManager.cs:431-434`) — and the designer client forces conditional on anything drawn
from an or-gateway (`connection-utils.ts:72`). So a **converging** or-gateway, whose single outgoing flow
is by definition unconditional, can only be modelled as a **default flow with no conditional sibling**.

**Measured counter-examples: exactly 45** — 40 exclusive + 5 inclusive gateways with one outgoing flow,
that flow being a default. Verified by a dedicated recount over every `BK4` array in the corpus.
Named examples: `BulkFileManagement/DeleteFilesInTable`, `CaseService/RunSendEmailToCaseGroup`,
`CrtCaseCopilot/Copilot_GetCaseExternalMessages`, `BpmGDPR/BpmProcess6`.

Academy's wording (*"a default flow is used when there is at least one conditional flow outgoing from the
same process element"*) does not contemplate the converging gateway the designer itself produces.

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
`create-business-process`/`modify-business-process` **and** by `validate-process-graph`. At run time a
self-looping task re-executes on every completion.

The designer refuses to draw one — `canConnectionCreate` requires `source !== target`
(`process-diagram-rules.ts:120-134`) — but **tolerates re-saving an existing one**, and 3 shipped flows
are self-loops, one in production content (`OpportunityBank/OppManagementNeedAnalysisFinance`,
`ConditionalFlow2`; the other two are the platform's own `ProcessTests/ReRunningProcessElementCase3`).

So the rule is **refuse on author, tolerate on read** — the designer's own posture:

- **Client validator: error**, rule id `R15`, for any edge with `source == target`. Safe because
  `validate-process-graph` only sees a *planned* graph.
- **Server: reject** in `ValidateStructure` (build) and in `AddFlowOperation` (modify), so the guarantee
  holds when the pre-flight is skipped.
- **`describe` must not refuse** a process containing one, and no read path may fail on it.

The layout engine already skips self-loops when building adjacency (`ProcessLayoutEngine.cs:47-53`), so
nothing else changes on the read side.

### 2.3 Improve the R7/R9 message — name the actual failure

Current text: *"Diverging gateway 'X' should have a default flow so the process never dead-ends."*

The real consequence is specific and findable: `FlowConditionalGateway.OnVisited` throws
`MismatchItemsCountException` (`ProcessEngine.Exception.MatchCondition.ByCount`) when no condition matched
and no default branch exists (`FlowConditionalGateway.cs:119-123`). Nothing earlier objects — the
platform's `ProcessInterpretationValidator` has no branch-coverage rule.

Proposed: *"Diverging gateway 'X' has no default flow: if no condition matches at run time the process
instance fails with MismatchItemsCountException. Add a default flow, or confirm the conditions cover
every case."*

Stays a **warning**: 65 shipped exclusive gateways deliberately have two conditional flows and no default.

### 2.4 Keep R13, and record that it is stricter than the platform

R13 restricts a conditional flow's source to a **gateway or an activity**. The platform is more
permissive: `AddEventStandardAllowedOutgoingSequenceFlows` grants `ConditionalFlowUId` to every start and
intermediate event (`ProcessSchemaElementManager.cs:436-440`, `:480-513`), and the corpus contains 4 such
flows (2 from a start event, 2 from an intermediate catch signal) out of 1 406.

Keep it as an error — deliberate conservatism for AI-authored graphs, and ENG-95891's whole two-step
recipe (build plain, then `setFlowCondition`) depends on the *activity* source being permitted. But say
so in the guidance, because an agent that reads back an existing process and re-validates it will
otherwise get an error on a valid platform process.

`RoleOf` already classifies `SubProcess`, `WebService`, `ScriptTask` and `FormulaTask` as `Activity`
(`Schema.cs:1140-1141`), covering 112 of the 1 406 shipped conditional flows — no change needed.

### 2.5 ADD "a conditional flow must carry a condition" — and extend the edge argument

A conditional flow with no condition and no activity-result is **not** an error the platform reports: it
silently becomes the literal `"true"` (`ProcessSchemaConditionalFlow.cs:216-219`) — a branch that looks
conditional and always fires. 7 shipped flows are in that state.

ENG-95891 closed this for the routes that exist today (the build path refuses `flows[].condition`
outright; `setFlowCondition` refuses an empty condition). **The new hole is the `kind` path** this ticket
opens.

`validate-process-graph`'s edge argument is `{source, target, flow-kind}`
(`ValidateProcessGraphTool.cs:130-133`), so the client validator **cannot see** conditions. Two halves:

- **Server (mandatory):** `BuildGraph` refuses `kind: conditional` with no `condition`, unless the flow
  already carries an activity-result dialect. This is the load-bearing guard.
- **Client (recommended, one optional field):** add `condition` (optional string) to
  `ProcessGraphEdgeArg` and emit an **error** for `flow-kind: conditional` with no condition. Purely
  additive to the MCP contract.

### 2.6 ADD the promised parallel-join deadlock warning

`ai-bp-connection-rules.md` lists *"parallel converge that can deadlock"* among the intended warnings; it
was never implemented. A `FlowParallelGateway` join proceeds only when **every** incoming branch has
delivered a token (`FlowParallelGateway.cs:53-90`) — if only one of two branches can ever run, the
instance hangs in *Running* with no exception and no log line.

Minimal no-false-positive form: **warn** when a parallel gateway with ≥2 incoming flows has incoming
branches that trace back to a common **exclusive** (or inclusive) split. Warning only.

### 2.7 ADD "at most one default flow per source" — error

Corpus: **0** sources carry two default flows, in 9 762 flows. The designer keeps the invariant by
**demotion**: promoting a flow to default converts the previous default back to the required kind
(`process-replace-menu-provider.ts:63-67`, `:114-121`).

- **Client:** error when a source has >1 outgoing flow of kind `default`.
- **Server:** refuse on write. Do **not** replicate the designer's silent demotion — an API that quietly
  changed a flow the caller did not name is the failure mode this package refuses everywhere else
  (ENG-95891 made the same call when it refused to clear an author's result selection rather than doing
  it on the caller's behalf). Refuse and name both flows.

### 2.8 ADD "a diverging or-gateway must not use plain sequence flows" — error, arity-scoped

The mirror of R11. From an or-gateway the designer offers only conditional and default
(`ProcessSchemaElementManager.cs:431-434`; `getFlowExcludeTypes` removes plain `connection`,
`process-replace-menu-provider.ts:97-101`).

Arity scope matters: 14 shipped exclusive gateways *do* carry a single plain sequence flow — all with
exactly **one** outgoing flow, i.e. legacy converging gateways from an older designer.

- **error** when the gateway has **>1** outgoing flow and any is plain `sequence`;
- **no finding** when it has exactly one outgoing flow (legacy-tolerated on read);
- and the **builder normalises**: asked for a single unconditional continuation out of an or-gateway, it
  emits a **default** flow — matching the 40 shipped merges and the designer's only option.

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

A gateway that both merges incoming branches and re-splits them is normal, useful BPMN, and 60+ shipped
processes use it. R6 describes the two *canonical* roles, not a constraint.

**Recommendation: do not implement R6, and record the reason in `ai-bp-connection-rules.md`** so the next
person does not "complete the rule set" and break real processes.

---

## 4. Client/server parity after this ticket

The server's `ValidateStructure` must mirror every new **error**. Its current remark — *"the build path
materializes only start/signal-start/end/user-task nodes and sequence flows, so reachability cannot
false-positive on designer-only constructs (gateways, …)"* — is falsified by this ticket and must be
rewritten in the same edit
([traps T-13](eng-91853-gateways-and-flows-traps.md#t-13--the-build-path-structural-guard-carries-a-remark-this-ticket-falsifies)).

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

Reachability with a **retry loop** must keep passing on both sides: 15 % of gateway containers contain a
back-edge, and both implementations use plain forward/backward BFS, which handles cycles correctly. Add a
fixture anyway — it is the case most likely to be broken by a well-meaning refactor.

---

## 5. Closing the validate-vs-build fork (review follow-up #6)

ENG-95891 already narrowed the fork once. The current tool text reads:

> *"…while create-business-process / modify-business-process build only
> startEvent/signalStart/endEvent/userTask/sendEmail elements. Flows start plain, and modify turns one
> into a conditional branch with setFlowCondition — so a conditional branch IS buildable even though a
> gateway ELEMENT is not."*
> `ValidateProcessGraphTool.cs:50`

After this ticket the buildable slice becomes:

```text
elements:  startEvent, signalStart, endEvent, userTask (+ readData / performTask / changeData /
           sendEmail / preconfiguredPage aliases), exclusiveGateway, parallelGateway
flows:     sequence, conditional (declaratively, with a condition), default
```

Still **not** buildable, so the fork narrows rather than closes: `inclusiveGateway` and
`eventBasedGateway` (ENG-95889), timer/message starts, intermediate events, sub-processes, formula and
script tasks, and the **activity-result** condition dialect (its own follow-up).

Required edits, all in the same change:

1. `ValidateProcessGraphTool` `[Description]` — restate the buildable slice; the "conditional branch is
   buildable even though a gateway element is not" clause becomes obsolete and must be rewritten, not
   left standing.
2. `CreateBusinessProcessTool` / `ModifyBusinessProcessTool` `[Description]` — the two gateway `type`
   tokens, `flows[].kind`, `flows[].condition` (which the create tool currently tells the agent to avoid),
   and the gateway/flow rules an agent must obey (one default per source; or-gateway outgoings are
   conditional/default; **flow order is evaluation order**).
3. **`DescribeProcessPrompt`** — introduce `condition` **and** `branchesOnActivityResult` together. This
   was reverted to master by ENG-95891 on the project owner's scope call precisely so that this ticket
   ships both at once (clio `09898af82`); shipping only one repeats the mistake that revert undid.
4. `ProcessElementFactory`'s rejection message — the supported-token list is derived from the registry and
   updates itself; **the trailing hand-written sentence naming gateways as unbuildable must be updated.**
5. `ProcessGraphBuilder.BuildGraph`'s two `NotSupportedException`s — the flow-kind refusal and the
   `condition` refusal, both of which point at the two-step recipe that this ticket supersedes for the
   declarative path.
6. `ProcessFlowDescriptor.Kind` / `.Condition` XML docs.
7. `spec/backend-designer/backend-designer-manual-qa.md` — TC-C-05 records the fork and TC-D-01 asserts
   that the builder **rejects** a gateway. Both invert.
8. `guidance name=process-modeling` (and the flow half of `process-formulas`) — a pull request in the
   **clio-knowledge** repository, plus a `libraryVersion` + `sequence` bump and a re-pin of
   `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`.
9. `spec/ai-business-process-generation/ai-bp-connection-rules.md` — R14's arity scope, the R15 self-loop
   rule, the three new rules, and the R6 non-decision from §3.
