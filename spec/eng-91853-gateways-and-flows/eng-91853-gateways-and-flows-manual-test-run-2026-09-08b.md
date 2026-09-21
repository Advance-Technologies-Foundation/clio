# ENG-91853 — manual test run, 2026-09-08b (TC-01…TC-15)

The remaining fifteen cases, run after TC-16/TC-17, on the same stand and the same versions.
Run `2ebac4d9-2bae-48df-aea2-304dbe76c581`, `BPTest R7 TC01`…`TC15` in package `Custom`.
Identity: branch clio `77ad67223` (binary built 06:57), CrtProcessBuilder **1.6.0.3**, knowledge
`af57c415` / 1.13.99, stand `Creatio` — `<dev-stand>:40001`.

The executor reports all fifteen as PASS. Twelve processes were built; TC-05 and TC-10 are plan checks
that build nothing, and TC-11 was refused by design. I re-verified the load-bearing claims from the
stand afterwards — schema read-back through the **branch** binary over stdio, runtime through
`SysProcessLog` / `SysProcessElementLog`. Every claim I checked held. Two things the run got wrong are
G1 and G3 below, and neither is a case the executor failed — one is a defect it walked past, the other
is a defect in the case I wrote.

## What I verified independently

| Claim | Evidence |
|---|---|
| TC-01/TC-06 threshold edited in place, 100 → 250 | `UsrBPTestR7_TC01` flow reads `…[Parameter:{c9449a2e…}]#] > 250`; one gateway, conditional + `default`; instances at 10:15:02 and 10:15:06 |
| TC-03 refuses nothing at build, fails at run | instance `7be4a89d` status **Error**, `ErrorDescription` verbatim: *"None of the conditions were met after the element "Route by amount"."* plus causes and the recommendation to add a default flow |
| TC-07 role swap, flow order preserved | `SendToManager` now `default`, `RouteToFallback` now conditional, array order still director → manager → fallback |
| TC-13 both paths ran | instance `042df7f7`, element log: `Decide next step` 10:20:38.339 → `Go down path A` 10:20:38.351 → `Go down path B` 10:20:38.711 |
| TC-15 merge is one element, 3 in / 1 out | `MergeRoutesGateway` at `600;303` with three inbound `sequence` flows and one `default` out to `NotifyRequester` |
| TC-15 the shared step fires exactly once per run | proven after unblocking (below): each of the three instances logs `Route by amount` → its one route → **`Merge routes` once** → **`Notify requester` once** |
| Instance statuses | `ed2ae277…` Running (17 instances, all ending on a Perform task), `f942c08d…` Error (TC-03), `815c9586…` Completed (the two TC-16/17 processes) |

Layout is case E again in TC-15 — fork `240;173`, targets `420;173/303/433`, join `600;303` — the
pinned three-way merge lane, now measured a second time on an independently built process.

**The clean room held on the write side too.** The executor recorded two notes to memory; they landed in
its own session-scoped store (`…-bp-test-R7-…/memory/`), not in the project's. Nothing it learned
reached this project's memory, which is what the isolation is for.

## G1 — the modify path does not enforce the invariants the build path enforces

**Corrected after the first write-up of this report, which had it wrong.** I originally reported that
`create-business-process` accepted a start event with two outgoing flows. It never saw that shape.

What the transcript shows the executor actually did to `UsrBPTestR7_TC12`:

1. **Created** a legal process — `Start → ReadRequestAmount (performTask) → conditional(>100) →
   SendForApproval`, `default → RouteToFulfilment`. That is the shape the case asks for, branching off
   the step, and it built cleanly at 07:18:37.
2. **Ran it twice** (07:18:50, 07:18:53). Both instances logged only `Read request amount` and stopped
   there — the human step blocked, so which branch would be taken was not observable.
3. **Modified it** to get past the block: `removeElement ReadRequestAmount`, then two `addFlow`
   operations hanging the conditional and the default off the **start event**. Then re-ran, and the
   routing became observable (07:19:45, 07:19:49).

So the contested shape was produced by `modify-business-process`. Measured now, on a stand confirmed at
1.6.0.3, the create path **refuses** exactly this graph:

```
create-business-process → exit-code 1
"The process graph is structurally invalid: start event 'Start' must have a single outgoing flow.
 A process must have exactly one start event and an end event, and every element must lie on a path
 from a start to an end."
```

`ProcessGraphBuilder.ValidateStructure` (package source, `Graph/ProcessGraphBuilder.cs:959-961`) raises
it on `outgoing[start.UId].Count > 1` and throws on any error. The guard is old — it predates the
`clioprocessbuilder` → `CrtProcessBuilder` rename — so no version of the package in play lacks it.

**The defect, restated.** One shape, four answers:

| path | answer |
|---|---|
| `create-business-process` | **refuses** — structurally invalid |
| `modify-business-process` | **accepts** — this is how TC-12 reached its current state |
| `validate-process-graph` | **R1 error** (+ R13 warning: the designer cannot draw a conditional flow off an event) |
| runtime | **works** — routed correctly in four instances, and `SaveNewApiKey` ships with this shape |

The build/modify disagreement is the Group 6 invariant failing inside one package: the same mistake must
be refused wherever it is expressed. TC-11 exercises both doors for the self-loop and both refuse, so
*some* invariants are shared and this one is not. Whether the fix is to enforce the invariant on modify
or to demote it everywhere is a severity question the corpus bears on — 34 shipped start events have
more than one outgoing flow, one of them (`SaveNewApiKey`) in exactly this conditional+default shape —
and it is being decided in the implementation session, not here.

**The fix has three answers, not two, and the code says which axis matters.** I first put it as
enforce-on-modify or demote-everywhere. The review session pointed out that this collapses the real
axis, and the mechanism is in the same file I was reading:

- `ValidateStructure` has exactly **one** caller — `ProcessGraphBuilder.cs:98`, inside the create path —
  and the scoping is deliberate, not accidental. `:96-97` says it "is still create-path only … and why
  it must not run over an arbitrary existing process", and `:926-927` says the per-flow authoring rules
  live in `FlowKindRules` "so they also cover the modify path this guard must not run on".
- It judges the **whole graph**. Running it on modify would therefore refuse an unrelated edit because
  of a pre-existing defect somewhere else in the process — by the reviewer's census that is 37 shipped
  processes (34 with more than one outgoing flow on a start, 3 with none) becoming un-modifiable for
  *any* edit.

So the three answers are: move the whole-graph guard onto modify (correct and unusable), add a
**per-operation authoring rule** where `FlowKindRules` already sits, refusing the specific `addFlow`
that would give a start event its second outgoing flow (narrow, and the shape the code is built for),
or demote the invariant everywhere including create (all four answers then agree with the runtime and
the corpus). Whole-graph guard versus per-operation rule is the axis; the two-way framing hid it.

**Why TC-11 refuses through both doors, mechanically.** Both handlers call the platform's own
validation — `_schemaValidator.EnsureValidForSave(schema)` at `ProcessBuildHandler.cs:114` and
`ProcessModifyHandler.cs:119` — and that is what catches a self-loop either way. `ProcessModifyHandler`
injects `IProcessGraphBuilder` but calls only `GetPrimaryLane` from it (`:99`). So the modify path runs
the platform's validation and skips the package's own graph pre-check: narrower than create, not absent.

**No arity divergence at the other end either.** `ValidateStructure`'s arity clause is `> 1` and does
miss a start event with **zero** outgoing flows, but the reachability clause catches that anyway:
`reachableFromStart` is `{start}` alone, so every other element — the end event included — trips
"element '…' is not reachable from a start event" and create refuses it through a different message.
The finding therefore lives entirely on the modify path.

**Version hypothesis, checked and refuted.** The implementation session reasonably suspected the stand
had been at 1.4.0.70 when this case ran, since an earlier run today raised the package mid-flight.
`SysPackage` says `CrtProcessBuilder` 1.6.0.3 with `ModifiedOn` **2026-09-07T23:21:40Z**, and the R7
transcript contains **zero** calls to `install-process-builder` or `push-pkg` — the run was forbidden
from installing and did not. The stand was at 1.6.0.3 for the whole run.

## G2 — the silent parallel split is real, and only a voluntary plan check catches it

TC-13 built one step with two plain outgoing flows and no rule. The build emitted `Info` messages only
— no warning. At runtime **both** paths executed in a single instance (evidence above). The plan check
does surface it: **R12, warning** — "Element 'DecidingStep' has multiple outgoing sequence flows
(implicit parallel split) — confirm intent."

R12 is a warning by deliberate corpus decision and should not become a refusal. But the build path
stays completely silent on it, so an author who does not voluntarily run a plan check gets a process
that quietly does two things at once. `create-business-process` already has a channel for exactly this
— its own description promises that "a SUCCESSFUL build can still report caveats" as `Warning`
entries. Surfacing R12 there would close the gap without refusing anything.

Owner: the gateways/flows work.

## G3 — TC-12 was run on the wrong shape, and the case let it happen

Group 7 exists for branching that belongs to **the step**, which the group preamble calls roughly half
of all branching in shipped content. The case says the choice must "sit on the step that makes it", and
then describes "the step, and two paths leaving it". The executor attached both flows to the **start
event** instead, satisfied every literal expectation the case lists — no decision element, one
identifiable fallback, correct routing — and reported PASS. The shape the group exists for went
untested.

That is a defect in the case, not in the run — but the cause was not carelessness. The executor built
the case's shape correctly **first**, and abandoned it only because both runs stalled on the Perform
task and it could not see which branch was taken (G1 above has the sequence). The harness limitation
drove the deviation, and the shape it improvised is the one that exposed G1.

Both halves are now fixed: the case names the element the flows must leave and says the start event is
not it, and the blocking step turns out to be removable from outside the run (below). TC-12 should be
rerun on the corrected case; Group 7's headline shape is still untested and I am not counting it.

## The human-step block is not a harness limitation after all

Any `performTask` element creates an **unassigned** Activity and the instance blocks there until a
human completes it, which is why seventeen of the twenty instances were left `Running`. The executor
read this as a hard boundary — it tried `odata-update` and was refused by its own permission
classifier — and I wrote it up as a harness limitation. That was wrong: the block is removable from
outside the run.

**The recipe.** Complete the open Activity by setting its status to a *finish* status, and the platform
resumes the instance:

```
odata-read  Activity   filters: Status/Finish eq false, order-by CreatedOn desc   → find the open one
odata-update Activity  id=<activity>  data={"StatusId":"4bdbb88f-58e6-df11-971b-001d60e938c6"}  confirm=true
```

`4bdbb88f-58e6-df11-971b-001d60e938c6` is `ActivityStatus` "Completed" (`Finish: true`); "Canceled"
(`201cfba8…`) is the other finish status. Measured on this run: the three TC-15 activities were
completed at 07:33, and each instance then logged `Merge routes` and `Notify requester` within two
milliseconds of each other — 11 minutes after the route step, which is the gap between the run ending
and my unblocking it. TC-01's two post-edit instances likewise went to **Completed** once their
activity was closed, which retires the executor's "contradicts prompt: stays Running" note: nothing
contradicted the case, the instance was simply waiting on its human step.

TC-15's own instance stays `Running` after this, because `Notify requester` is itself a Perform task
and opens the next Activity — expected, and irrelevant to the case, which needs the shared step to
*appear* once.

**The browser route needs a session I cannot create.** The user's suggestion — close the Activity from
the process log in the UI — works for a person, and it is the better route when the point is to see
what an analyst sees. From a headless run it is not available: the stand redirected to
`Login/NuiLogin.aspx`, and typing a password into a login form is not something this harness does.
The API recipe above is the equivalent that needs no session.

So a runtime expectation behind a human step **is** observable in agent mode, provided the harness is
allowed the `Activity` write. That is a prompt-contract question rather than a platform one: the
executor was right to report the refusal instead of working around it, and the run's own instructions
should say whether completing activities is in scope for it.
