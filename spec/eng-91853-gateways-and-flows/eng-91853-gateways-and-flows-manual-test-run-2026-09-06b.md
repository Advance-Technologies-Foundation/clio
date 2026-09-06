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
