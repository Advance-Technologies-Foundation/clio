# ENG-91853 — manual test run 2, 2026-09-06

## Verdict

**Stored level only; design time and runtime not verified.** A `--mode agent` run establishes what the
toolkit wrote, read back independently, and nothing more.

Within that limit this is a strong result. The executor worked the **whole 15-case suite itself**, and
the headline finding of run 1 — D1, that a build-path condition could not name its parameter — is
**closed and demonstrated on a stand**: a clean-room agent wrote `[#AmountParameter#]` in a single
`create-business-process` call and the stored metadata carries the expanded UId meta-path.

## Run header

| | |
|---|---|
| Issue | ENG-91853 |
| Stand | `Creatio` → `http://d_krestov_n.tscrm.com:40001` (.NET Framework, MSSQL, `IsDemoMode`) |
| Mode / isolation | `agent` / `isolated` (no API key; hooks and user `CLAUDE.md` still load) |
| clio | `73dbec829` |
| Package | **1.4.0.61**, installed by this run and verified (stand was on 1.4.0.59) |
| Knowledge | `1.13.94`, revision `c1a9e69fb3d9e881401453ba22a541aa88071d88` |
| Prompt | 15 cases @ `a556e4098` |
| Run id | `331dc693-6848-4e52-8918-085492fd500f` |

**Gates.** Identity: revision == the pinned commit. Positive control: `routing` 9 309,
`process-branch-conditions` 12 268, `process-modeling` 27 276, `process-formulas` 24 877 chars, all
`success: true`. Content: the build-path name rule (`ON THE BUILD PATH, WRITE THE NAME`) is present in
the served generation and returns **0** matches on `master`.

## The prompt fix worked — measured, not asserted

| | run 1 | run 2 |
|---|---|---|
| executor's own turns | 9 | **126** |
| delegation (`Agent`) calls | 2 | **0** |
| cases reached | 6 of 11 | **15 of 15 attempted** |
| errored tool results | 9 | **2** |

Run 1 died because the executor handed the suite to two background sub-agents and ended its session.
The revised preamble — work the cases yourself, in order, writing each up as you finish — removed the
*mechanism*, not the symptom. That is the cleaner result of the two.

## D1 — closed, with the evidence that distinguishes it

The stored form alone cannot settle this: a name that expanded and a meta-path written by hand both end
up identical in `CI3`. What settles it is what was **sent**.

Sent, on the build path, in one call:

```
flow: CheckRequestAmount -> EndApprovalPathTaken   kind=conditional
     CONDITION AS SENT: '[#AmountParameter#] > 100'
```

Stored, read out of the pulled package's raw `metadata.json`:

```
CI3 VERBATIM: [#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{874f9328-4a59-4668-8350-3c8f01207035}]#] > 250
```

(The `250` is TC-06, which later moved the threshold on the same process.) So `ConditionParameterNames`
ran, matched the bare name, and expanded it against the parameter the same call created. The two-step
route run 1 was forced into never appeared.

**The guidance's split by call also held.** The agent used a NAME on `create-business-process` and the
UId meta-path on the later `setFlowCondition` — which is exactly what `branch-conditions.md` now tells
it to do, and it is the first evidence that the split is followed rather than merely written.

## Results per case

Every process below was read back by the reviewer from the package pulled off the stand, not from the
executor's account.

| Case | Artifact | Stored | Design time | Runtime |
|---|---|---|---|---|
| TC-01 | `UsrPurchaseRequest_Route` | **PASS** — XOR, conditional + default, condition expanded | not verified | not verified |
| TC-02 | `UsrPurchaseRequest_Escalate` | **PASS** — XOR, 2 conditionals + default | not verified | not verified |
| TC-03 | `UsrPurchaseRequest_CheckNoFallback` | **PASS** — 2 conditionals, no default, adversarial shape exact | not verified | not verified |
| TC-04 | `UsrRequest_ConfirmAfterChecks` | **PASS** — AND split + AND join, 2 branches | not verified | not verified |
| TC-05 | plan check only | **PASS** — deadlock plan checked, not built | n/a | n/a |
| TC-06 | `UsrPurchaseRequest_Route` | **PASS** — threshold moved 100 → 250 in place | not verified | not verified |
| TC-07 | `UsrPurchaseRequest_Classify` | **PASS** — stored order is conditional / **default** / conditional: the default sits in the middle, so the role swap preserved positions | not verified | not verified |
| TC-08 | `UsrRecordBatch_Process` | **PASS** — XOR with a real back edge | not verified | not verified |
| TC-09 | — | see note | not verified | n/a |
| TC-10 | plan check only | **PASS** — merge shape not called invalid | n/a | n/a |
| TC-11 | *refused, no schema* | **PASS** — see below | n/a | n/a |
| TC-12 | `UsrPurchaseRequest_Fulfil` | **PASS** — **no gateway element**; conditional + default straight off an activity | not verified | not verified |
| TC-13 | `UsrRequest_DecideNextStep` | **PASS** — two plain flows off one step; R12 warned | n/a | not verified |
| TC-14 | `UsrRequest_ConfirmAfterThreeChecks` | **PASS** — AND split + join, **three** branches | not verified | not verified |
| TC-15 | `UsrPurchaseRequest_NotifyAfterRoute` | **PASS** — three routes converge on one shared step that appears **once** | not verified | not verified |

TC-09 is a design-time-only case by construction; there is no stored assertion for it, and the browser
leg owns it.

### TC-11 — the scope line, and the parity it demands

The issue's scope addition asks for a self-loop to be refused **on both sides**. Both refused, and they
agree word for word on the remedy:

Build — `exit-code: 1`, no schema on the stand:
> A flow cannot connect 'RepeatStep' to itself. To repeat an element, route the flow back through a
> gateway that decides whether to repeat it.

Plan check — `severity: error`, `rule-id: R15`:
> Flow connects 'RepeatStep' to itself. To repeat an element, route the flow back through a gateway
> that decides whether to repeat it.

The transcript names `UsrRepeatStep_SelfLoop` as a create argument, which is ambiguous evidence on its
own — a refused attempt and a built process look the same there. The stand settles it: the schema does
not exist.

### TC-13 — the silent split is not silent, but only on one path

Two plain flows off one step build successfully, which is correct — the shape is legal. The question the
case asks is whether anything warns. It does:

> `warning` `R12` — Element 'DecideNextStep' has multiple outgoing sequence flows (implicit parallel
> split) — confirm intent.

**But only at plan-check time.** The `create-business-process` response carries `exit-code: 0` and no
notice. An agent that builds without validating first gets nothing, and the 60 shipped sources in this
shape say the mistake is real. Not a defect in this change — R12 predates it — but worth knowing that
the safety net hangs on a step the agent may skip.

## Defects

**None new at the stored level.** Two errored tool results in 125 calls, both the same known shape:
`No such tool available: mcp__clio__odata-read` and `…create-business-process`, each recovered through
`clio-run`. That is D4 from run 1 — a long-tail tool that looks resident — already spawned as its own
task, and the recovery cost here was two calls rather than the detour it caused last time.

## What this run does NOT establish

Stored level only. Every `not verified` above stays that way until the browser leg opens these processes
in the designer and runs them. In particular: that the default branch renders as the fallback, that the
three-way join waits for all three at run time, and that the expanded condition actually evaluates —
storage proves the text is right, not that the platform agrees with it.

## Continuing

Eleven processes are left on the stand deliberately as the browser leg's input.

```bash
/bp-test-run ENG-91853 --mode browser --env Creatio
```

That run appends design-time and runtime verdicts to **this** file.

---

# Browser leg — 2026-09-06, design time and runtime

Run against the same stand (`Creatio`, `http://d_krestov_n.tscrm.com:40001`), package **1.4.0.61**,
against the eleven processes the agent leg left in `Custom`. Verdicts below are read from the stand —
the process log and the designer — not from any tool's own account of success.

**Manifest gap, worth fixing in the skill before the next run:** the agent manifest records each
process's case, name and package but **not its UId**, which `references/environment.md` says it should.
The browser leg needs the UId to open a designer directly, so it has to re-derive all ten from
`VwProcessLib` first. Cheap once, and pure waste every time.

## Runtime

| Case | Input | Elements the process log shows | Verdict |
|---|---|---|---|
| **TC-01 / TC-06** | Amount 300 | `Check request amount` (XOR) then **`Approval path taken`** | **PASS** |
| | Amount 200 | `Check request amount` (XOR) then **`Fast-track path taken`** | **PASS** |
| **TC-02** | 5000 | `Check escalation tier` then **`Director path taken`** | **PASS** |
| | 500 | then **`Manager path taken`** | **PASS** |
| | 50 | then **`Fallback path taken`** | **PASS** |
| **TC-03** | 1 | `Check amount, no fallback` only; instance status **Error**, no end date | **PASS** (adversarial) |
| **TC-04** | none | `Split into checks`, both checks, `Join after checks`, `Confirm the request` | **PASS** |
| **TC-07** | 5000 | `Classify request amount` then **`Director path taken`** | **PASS** |
| | 50 | then **`Manager path taken`** (the default, now in the middle) | **PASS** |
| **TC-12** | none | `Register the request amount` (user task) then exactly one path, **no decision shape logged** | **PASS** |
| **TC-14** | none | three checks, `Join after three checks`, `Confirm the request` | **PASS** |
| **TC-15** | 5000 / 500 / 50 | one route each, then `Send the notification` **once**, then `Request handled` | **PASS** |
| **TC-13** | none | `Decide the next step` then **`Path A taken`** and nothing else | **see finding 2** |

### The result this ticket most needed

`UsrPurchaseRequest_Route` carries the condition the build path expanded from a NAME. At run time:

```
Amount = 300 -> Check request amount (ProcessSchemaExclusiveGateway) -> Approval path taken
Amount = 200 -> Check request amount (ProcessSchemaExclusiveGateway) -> Fast-track path taken
```

So the expanded meta-path condition is a **live predicate**, not merely correct text, and the gateway
takes ONE branch rather than both. That is `ConditionParameterNames` working end to end, and the first
evidence 1.4.0.60/.61 has had outside unit tests.

### TC-04 and TC-14 — the join really waits

Millisecond timings from `SysProcessElementLog`, which is what makes this checkable rather than
plausible:

```
TC-04   Read requester contact   10:45:54.521 -> .542
        Read requester company   10:45:54.566 -> .580
        Join after checks        start .586      (after the later finish, .580)
        Confirm the request      start .609

TC-14   Read requester contact            10:46:07.531 -> .541
        Read requester company            10:46:07.549 -> .557
        Read requester contact a 2nd time 10:46:07.566 -> .575
        Join after three checks           start .582   (after the last finish, .575)
        Confirm the request               start .586
```

### TC-03 — the message a person actually sees

Quoted from the process-log card's **Show error**, not from any API:

> None of the conditions were met after the element "Check amount, no fallback".
> The business process execution has been suspended and cannot continue.
>
> Possible causes
> - the conditional element in your business process does not have a `default` outgoing flow.
> - all outgoing flows of the branching element have conditions that evaluated to `false`.
>
> Recommended action
> - Always consider configuring a `default` outgoing flow for conditional elements to prevent process
>   suspension when no conditions are met.

This is the wording R7/R9's warning was re-worded to quote, and it matches. The instance is left in
**Error** with no end date: it does not silently complete and it does not silently take a path.

## Design time

**TC-01 `Purchase request routing`** — one exclusive gateway (`Check request amount`, the orange rhomb
with the XOR cross), exactly two paths leaving it, both end events captioned as the case asks, one
`Amount` parameter, no stray element and no empty path. **PASS**, with the observation below.

**TC-08 `Record batch processing`** — **FAIL**, see finding 1. The four steps are placed correctly:
four distinct columns, left to right in the order they happen, no shape overlapping another and
nothing collapsed into one column — which is the back-edge classification doing its job, and is what
the case's own regression clause asks about. But the repeat path is not *visible* as a path going back,
which is the requirement the case leads with.

### Observation — the default marker sits on a shared connector stem

Not filed, because it is the same routing behaviour as finding 1 and the case for TC-01 does not name
it. Recorded because the case does ask whether a person can tell.

TC-01's fallback flow carries the BPMN default marker (the short diagonal slash) and it is on the
correct flow — clicking the fast-track connector selects the polyline the slash sits on. But both
outgoing connectors leave the gateway along the **same horizontal segment** before the fast-track one
turns down, so the slash is drawn over pixels the two share. A reader can see that *a* default exists
and cannot tell *which* path it belongs to without clicking.

## Finding 1 — a back edge is drawn on top of the forward flow (CrtProcessBuilder, layout)

> **DISPOSITION, owner's decision 2026-09-06: DEFERRED to a separate autolayout task**
> (`task_85915b4e`). The defect stands and the FAIL below is not withdrawn — what the owner
> decided is WHERE it is fixed, not whether it exists. It does not block this ticket's pull
> requests: connector routing was never in ENG-91853's scope, and the placement half of the
> same case passes.

**Observed**, TC-08, `Record batch processing`, between `Process a portion of records` (user task) and
`Check remaining records` (exclusive gateway): a **single straight horizontal line carrying an arrowhead
at BOTH ends**. A double-headed arrow is not BPMN, so the picture alone already says something is wrong;
what it does not say is whether that is one malformed connector or two superimposed ones.

**Proven, not inferred.** `ProcessDesignService/DescribeProcess` returns four flows for this schema,
and TWO of them join those same two elements in opposite directions:

```
ProcessPortion         -> CheckRemainingRecords   kind = sequence      (forward)
CheckRemainingRecords  -> ProcessPortion          kind = conditional   (the repeat path)
CheckRemainingRecords  -> EndBatchProcessed       kind = default
BatchProcessingRequestedStart -> ProcessPortion   kind = sequence
```

So it is two distinct flows rendered on identical geometry. **And the loss is worse than direction:**
the return path is a CONDITIONAL flow — it carries the repeat condition — drawn indistinguishably from
a plain forward sequence flow. A reader loses the direction, the fact that the return exists as its own
flow, and the fact that it is taken on a condition. (The default marker visible beside the gateway is
correct and belongs to the third flow, the one to the end event.)

**Why it is a defect and not the routing the case excuses.** TC-08 pre-excuses one thing — *"a path that
skips ahead over other steps may have its connector line drawn across those steps"* — and names two
regressions, shape overlap and column collapse. This is neither. It is a third outcome: the repeat path
is not distinguishable from the forward path at all, so the case's leading requirement, *"the repeat
path is visible as a path going back to the earlier step, **not merely implied**"*, is not met. A
double-headed arrow is not BPMN; a business analyst opening this cannot follow the loop, which is the
stated purpose of the case.

**Mechanism.** The layout engine sets element `Position` and leaves every connector to the platform's
AutoPolyline router. The two elements are sequential neighbours on the same lane — which is exactly
what the rest of the case requires — so the shortest path for both flows is the same straight segment,
and the router has no reason to bend either. Placement cannot fix this without breaking "steps run left
to right in the order they happen".

**What could fix it**, carried into `task_85915b4e` with this evidence: the engine already knows about
`PolylinePointPositions` (`ProcessGraphBuilder.CarryOperatorState` copies it on a re-kind), and a back
edge is exactly the case where an explicit route — down out of the source, back along a free lane, up
into the target — is worth writing rather than delegating. `VisualType` is NOT the lever:
`ProcessSchemaSequenceFlow.WriteMetaData` passes the literal, so the field never reaches metadata (see
`docs/knowledge/platform/sequence-flow-visualtype-is-written-as-a-literal.md`).

**Reproduction:** open `UsrRecordBatch_Process` in the process designer and look between the task and
the gateway. No run needed; it is design time only.

## Finding 2 — TC-13 took ONE path, not two (clio, R12 wording)

**Observed.** `UsrRequest_DecideNextStep` has two plain sequence flows leaving `Decide the next step`, a
user task. The completed instance logged exactly two elements:

```
05:34:43.360  Decide the next step   ProcessSchemaUserTask         Completed
05:34:54.763  Path A taken           ProcessSchemaTerminateEvent   Completed
```

Instance status **Completed**. Path B never ran.

**Why it matters.** Three surfaces assert the opposite, unconditionally:

- clio's **R12**: *"multiple outgoing sequence flows (implicit parallel split) — confirm intent"*;
- `FlowKindRules`' docblock: *"with all of them plain there is no gateway and EVERY outgoing flow is
  taken. That is a parallel split, silently."*;
- the spawned task `task_5483c080`, which proposes warning on the build path using that same claim.

On this element the platform did not fan out. The likely reason is that the source is a **user task**,
whose outgoing flows are selected by the activity RESULT — the `branchesOnActivityResult` mechanism this
ticket already knows about — rather than all being taken. If that is right, the claim is true for some
source kinds and false for a user task, and all three texts state it without qualification.

**Not yet established:** what the second flow's target was, and whether the task's completion result is
what selected Path A. One completed instance is enough to falsify "EVERY outgoing flow is taken" as an
unconditional claim; it is not enough to state the replacement rule. That measurement belongs to
whoever picks up `task_5483c080`, whose premise must be corrected before it is implemented — a warning
that tells an author their process will fan out, when it will not, is worse than no warning.

## Side effects left on the stand

Five instances are parked on a user task and stay `Running` until a person completes them:

| Process | Instance | Waiting on |
|---|---|---|
| `UsrRequest_ConfirmAfterChecks` | `0b6b9785…` | `Confirm the request` |
| `UsrRequest_ConfirmAfterThreeChecks` | `77e3b7a4…` | `Confirm the request` |
| `UsrRequest_DecideNextStep` | `3f960f50…` | `Decide the next step` |
| `UsrPurchaseRequest_Fulfil` | `1368ed6c…` | `Register the request amount` |
| `UsrPurchaseRequest_Fulfil` | `bcba17e2…` | `Register the request amount` |

They are this run's own artifacts and are safe to cancel. `Parallel check confirmation` (`00:32:57`,
waiting on `Confirm checks completed`) predates both legs and belongs to an earlier session.

## Verdict

**Runtime is verified and passes for every case that declares it.** The feature works on a stand: a
build-path condition written as a name expands and then evaluates, exclusive gateways choose one branch,
default branches are taken when nothing matches, parallel joins wait for every arm, and three routes
converge on a shared step that runs once.

**Design time passes for TC-01 and FAILS for TC-08**, on the one requirement TC-08 leads with: a back
edge drawn on top of the forward flow is not a visible return path. Placement is correct; the connector
is not, and the connector is currently left entirely to the platform's router. That FAIL is **deferred
to a separate autolayout task by the owner**, not withdrawn, and it does not block this ticket — routing
was never in its scope.

Two findings, one on each side of this ticket's boundary. Finding 1 is the layout engine's to answer and
now has its own task. Finding 2 is a claim in clio's own rule text that a run contradicts, and it is the
one a reader of this report should carry forward: **R12 says every outgoing flow is taken, and on a user
task one was.**
