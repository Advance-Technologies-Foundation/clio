# ENG-95891 — blind branching run, 2026-09-03

The branch use site, blind, at the shipped generation. Its behavioural half had not been measured since
2026-08-30 at CrtProcessBuilder 1.4.0.3 — the save-gate probe covers condition REFUSALS at the current
archive, not what a branch does. This run covers what it does, and one case produced a finding worth
acting on.

## Setup — three gates

| | |
|---|---|
| Stand | `krestov-test`, core 10.0.731.0 |
| Package | CrtProcessBuilder **1.4.0.52** |
| clio | `feature/ENG-95891-formula-expressions` at `ab20c4deb`; no `.cs` differs from branch HEAD `c82b6ae92`, so no rebuild |
| Guidance | **1.13.88**, git transport, revision `4ad1a2f2` |
| Gate 1 | revision matches the branch pin |
| Gate 3 | `branch-conditions.md` present — the article under test, split out at 1.13.87 and never blind-run |
| Gate 2 | `process-branch-conditions` served whole |
| Executor session | `c2925e41-711d-4276-87ef-19284d0b4083` |

**Gate 2 earned its place a third time.** After the previous teardown the released 1.13.77 was active
with sequence `1013077000`; the git-derived `1013088` is numerically lower, so activation was refused
while `info-knowledge` reported `Installed: yes, Valid: yes, Library version 1.13.88`. Only
`get-guidance` showed the truth. A full cache delete fixed it. That divergence — status commands
reporting a generation that is not the one being served — has now appeared three times and is worth its
own defect.

## Verdicts

Every runtime claim below was re-established by this session from `SysProcessElementLog`, not taken
from the executor's account.

### TC-D1 — the order takes the right path — **PASS**

Stored: one conditional flow off the start event carrying `[#[Parameter:{a44c3991-…}]#] > 100` to
`EndOrderApproved`, and a **plain** sibling to `EndOrderNotApproved`. No gateway element in the graph.
That is the if/else shape the guidance describes, reached without being told it.

Runtime: `Amount=500` → instance `21d30a0c` logged exactly `Order approved`; `Amount=10` → `086683c9`
logged exactly `Order not approved`. Both finished unattended.

### TC-D2 — which rule wins when two are true — **PASS**

Stored: two conditional flows, `>100` first and `>1000` second. **Nothing records priority.** The
executor said so plainly rather than inventing a field.

Runtime: `Amount=5000`, where both are true → `4885a14a` logged `Order approved` — the `>100` branch,
because it was stored first. The executor stated the rule in the form a builder needs it: precedence is
add order, not specificity, and building the two in the other order reverses the outcome.

### TC-D3 — a path with no condition at all — **FINDING, see below**

### TC-D4 — a decision after a human step — **PASS**

Stored: `Order registered → Review the order`, then a conditional `>100` to approved and a plain
sibling to not-approved.

Runtime: started at `Amount=500`, task left open. `c4e01ee9` logged one row — `Review the order`, type
`ProcessSchemaUserTask`, `CompleteDate` at the null sentinel. Neither end event logged. Parked, as
required.

**Regression check — the load-bearing half:** read back after the run, both flows are unchanged, the
condition text intact. Verified twice, by the executor and again here. No regression.

Design time, from the browser: the diagram draws `Order registered → Review the order` with two lines
splitting straight to the two end events, and **no gateway diamond**. The synthesized gateway never
appears as a node, exactly as documented.

### TC-D5 — the same moment, told twice — **PASS on the interval; the clock question settled**

Stored: no date constant and no formula — `Duration = 2`, `DurationPeriod = 2` (days). The platform
computes the deadline when the task starts.

Runtime: Activity `cb9ef1d3`, `StartDate 2026-09-04T06:37:06.3001142Z`,
`DueDate 2026-09-06T06:37:06.3001142Z` — exactly two days.

The executor refused to invent the browser-rendered value and explained why the interval is invariant
under any timezone. That was the right answer, and it left the open question to this session.

## D3 — removing the last condition makes an outcome unreachable, silently

**Severity: high. Owner: ENG-91853, not ENG-95891 — see *Where the task boundary falls* below.**

The business asked for the approved path to stop carrying a condition. There is no clear-condition
operation, so the executor removed the flow and added a plain one — and the request was **not refused**.

Read back, independently, after the change:

```
1. OrderRegisteredStart -> EndOrderNotApproved   kind=sequence   cond=None
2. OrderRegisteredStart -> EndOrderApproved      kind=sequence   cond=None
```

Both plain, and the remove-and-add pushed `EndOrderApproved` to the **end** of the array.

What that produces at runtime, measured three times — twice by the executor, once independently by
this session:

| Instance | Amount | Element executed |
|---|---|---|
| `9b1cc4f8` | 10 | `Order not approved` |
| `ad346e45` | 500 | `Order not approved` |
| `d71a5442` | 500 | `Order not approved` — this session's own run |

`Amount` no longer influences the outcome at all, and **`Order approved` is unreachable for every
value**. With all flows plain the platform synthesizes no gateway, every outgoing flow is taken, and
the first-stored terminate event ends the instance before the second is reached.

The business asked for *always approved* and got *never approved*. Nothing refused it, nothing warned,
and `describe-business-process` shows `kind: "sequence"` on both flows — which reads exactly like "the
condition was cleared, as asked".

**The guidance already warns about this, and the agent had read it.** `process-branch-conditions` says
not to leave a branching element with only plain flows, and not to reach for remove-and-add, and to set
the condition to `true` instead. The agent quoted that article and still took the other route, because
the business asked for *no condition* and setting one to `true` is not that. So the warning is
positioned as advice about a mechanism, at a point where the reader is deciding something else.

Two independent mitigations, neither of which this run chooses between:

- **the flow model (ENG-91853)**: this is the fallback case that task exists to build. A default flow —
  "taken when no condition matches" — is exactly the concept whose absence produces this outcome, and it
  is listed there as not done. Until it exists, an edit that leaves a branching element with no
  conditional flow could be refused or warned about; clio already refuses a blank condition for the same
  class of reason (the platform stores it as literal `true`), so the precedent exists and the check is
  cheap.
- **guidance**: move the rule from "how to change a condition" to "what happens when the last one goes",
  which is the question a reader has at that moment. That is available today and independent of the
  task above.

## Where the task boundary falls, and what this run may and may not claim

**ENG-91853 — exclusive and parallel gateways, conditional and default flows, branch-aware Y layout —
is `To Do` and states plainly: "Today only sequence flows exist; no gateways, no conditional / default
flows."** Its flow list marks *sequence flow (done)*, *conditional flow (not done)*, *default flow (not
done)*. It also records the dependency in the other direction: a conditional flow carries a condition
expression, and that expression is ENG-95891.

So the split is narrower than "branching belongs to ENG-95891":

| Behaviour | Owner | State today |
|---|---|---|
| the condition TEXT — vocabulary, references, validation, refusals | ENG-95891 | shipped, and this run exercised it |
| setting a condition on an existing flow (`setFlowCondition`) | ENG-95891 surface | shipped and working — D1, D2 and D4 all rely on it |
| gateway ELEMENTS as buildable nodes | ENG-91853 | not built; the platform synthesizes one instead |
| the DEFAULT-flow concept — a declared fallback when nothing matches | ENG-91853 | **not built** |
| branch-aware Y auto-layout | ENG-91853 / ENG-95890 | not built |

That places **D3 on ENG-91853's side of the line, not ENG-95891's.** What it measures is the behaviour
of a branching element with no conditional flow left — the fallback case, in a model that has no
declared fallback yet. It is not a regression in shipped work and should not be filed against the
formula PR.

It remains worth having, and worth carrying INTO that task: the finding is the concrete cost of the
missing concept, measured rather than argued, with a reproduction. A task that has to decide whether a
default flow is worth building now has a case where its absence turns "always approve" into "never
approve" silently.

**What this run may still claim without qualification:** D1, D2 and D4 exercise conditional flows set
through the shipped surface, and their verdicts stand. **What it may not claim:** that branching as a
whole is verified. Gateway elements and default flows were never in this build to test, and the parts of
the older case set (`TC-01`…`TC-10`) that assume them are testing functionality that does not exist yet.

## Other findings

### A structural rule the build path enforces and the modify path does not (Medium)

`create-business-process` refused a start event with two outgoing flows — *"must have a single outgoing
flow"* — twice. The executor then built one flow and added the second through
`modify-business-process`, which applied it without complaint. The same graph is therefore reachable by
two routes with different validation, and the stricter one is the one a builder meets first.

### The long-tail discovery gap is worse on a longer scenario (Medium)

**12 `ToolSearch` calls, 4 of them returning `No matching deferred tools found`** — against one attempt
in the previous run. The process surface is absent from the resident profile and nothing points at
`clio-run`, so a longer session pays the toll repeatedly rather than once.

### `run-process` argument envelope, two more instances (Low, known class)

Two refusals of the form `argument 'args' for MCP tool 'run-process' must be an object` before the
working shape `{environment-name, process-name, parameters}` was found. Same class as the `odata-read`
friction already reported to the MCP parameter-contract work.

## The stand clock is twelve hours ahead — an environment fact, not a defect

This retires an observation carried unexplained through two earlier runs.

An Activity created minutes before the check carries `StartDate 2026-09-04T06:37:06Z`, while real UTC on
this machine at that moment was `2026-09-03T18:42`. Exactly **+12 hours**, and the stamps are labelled
`Z`.

Consequences worth knowing before anyone writes a case around a date here:

- any case asserting an **absolute** date or time on this stand will be wrong by twelve hours, and the
  product is not at fault;
- any case asserting an **interval** is unaffected — both stamps come from the same clock — which is
  why TC-D5 passes cleanly;
- the earlier "the same activity reads twelve hours apart through different paths" note was this, seen
  from one side.

## Efficiency

77 calls, 144 assistant turns, five processes, nine process runs. A well-guided baseline is about 34,
so **2.3x**; 10 of the 77 were refusals.

Branching is inherently more expensive to verify than storage: one run shows one path, so D1 and D2
alone need five runs to establish what they claim. That is not overhead to remove.

Where no calls were wasted: the condition vocabulary, the reference syntax, and the if/else shape. The
agent built a plain sibling as the else branch without being told that is how it works — which is the
one thing `branch-conditions.md` most needed to convey, and it conveyed it.

## State

Five processes on `krestov-test` in `Custom` (`BPTest ENG95891 R4 D1`…`D5`). D4 and D5 are parked with
their tasks open. D3 is the reproduction of the finding above and should be kept until that finding is
closed.

Machine in test configuration: guidance pinned to `4ad1a2f2` (1.13.88) as a git source,
`knowledge-allow-unsequenced` enabled, `appsettings.json.bpskills-backup` holding the released
configuration.
