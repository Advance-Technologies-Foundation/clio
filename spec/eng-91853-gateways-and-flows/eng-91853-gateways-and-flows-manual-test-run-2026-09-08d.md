# ENG-91853 — manual test run, 2026-09-08d (TC-18, on the substrate where its invariant lives)

Third attempt at TC-18, and the first that tested what the case exists to test. Run
`e433b91a-534d-4c9b-8e16-d3b96b4ae277`, same stand and versions: branch clio `77ad67223`,
CrtProcessBuilder **1.6.0.3**, knowledge `af57c415` / 1.13.99, `Creatio` —
`d_krestov_n.tscrm.com:40001`.

## The result: the two ways disagree, and the accepted one silently fans out

| attempt | door | outcome |
|---|---|---|
| build a decision point already carrying the withdrawal (`[plain, plain]` off an ordinary step) | `create-business-process` | **accepted** |
| take the rule off an existing decision point (`setFlow` → `kind: sequence` on the last conditional) | `modify-business-process` | **refused**, correctly |
| the same withdrawal on a drawn gateway element (remark) | `modify-business-process` | refused by a **different** objection |

Verified from the stand through the branch binary, after the run:

```
UsrBPTestR9Tc18Build_Threshold      ProcessRequest  usertask
  ProcessRequest -> MarkHighBranch    sequence
  ProcessRequest -> MarkOtherBranch   sequence      <- two plain flows, not one ruled

UsrBPTestR9Tc18Modify_Threshold     ProcessRequest  usertask
  ProcessRequest -> EndHighAmount     conditional   …[Parameter:{…}]#] > 1000   <- intact after the refusal
  ProcessRequest -> EndOtherAmount    sequence
```

(The executor's write-up calls `ProcessRequest` a `readData` step; the read-back says `usertask`.
Immaterial to the finding — both are non-gateway sources — but the measured type is `usertask`.)

**The refusal, verbatim, and it is the right objection:**

> The flow from 'ProcessRequest' to 'EndHighAmount' is the last conditional branch leaving
> 'ProcessRequest', which has other outgoing flows. Making it unconditional stops the platform
> synthesizing the exclusive gateway, after which EVERY outgoing flow is taken rather than one.
> To make this branch always taken, set its condition to 'true' and leave the kind alone.

That is `WouldDropTheLastBranch`. It names the element, states the consequence, and gives a remedy that
works. Nothing was saved: the conditional flow is still there.

**And the runtime consequence is now measured, not predicted.** `SysProcessElementLog` for the two runs
of the accepted process:

| instance | shape at the time | elements logged |
|---|---|---|
| `9cd41719` 08:12:31 | flows going straight to two terminate ends | `Process request` → `High amount handled` |
| `b54d0abf` 08:14:11 | a marker step inserted on each branch | `Process request` → **`Mark high branch reached`** → **`Mark other branch reached`** → `High amount handled` |

Both instances Completed, both started with Amount = 5000. The second one settles it: **both branches
ran in a single execution.** The first shows why the markers were needed — with the branches going
straight to terminate events, whichever end fires first ends the instance, so the log shows one ending
and looks entirely normal. That is exactly the damage the case describes: reading the process back
shows two ordinary `sequence` flows, indistinguishable from "the rule was withdrawn, as asked".

## G4 — the asymmetry is this change's own, even though the accepting behaviour is not

`WouldDropTheLastBranch` refuses the withdrawal through `setFlow`. The build path has no equivalent: it
accepts the same end state directly. `EnsureADefaultHasSomethingToFallBackFrom` cannot cover it — that
guard fires only when the requested kind is `default`, and here both flows are declared plain.

Graded by the merge base rather than by what it sits next to (`5781c6aa5`):

- `FlowKindRules.cs` is **absent** at base, so `WouldDropTheLastBranch` — the refusal — is **new in this
  PR**.
- The build path's acceptance of two plain flows off an activity is **pre-existing**: no such guard
  existed before, and the guard that was added does not fire on this shape.

So the *acceptance* is old and the *refusal* is new: this change guarded one door and not the other.

**But the asymmetry is not the defect, and a symmetric fix would be worse than the asymmetry.** My
first filing named "a matching build-path guard" as one of the cheap options. That option is harmful
and should not be taken. The code already argues for the asymmetry, at `FlowKindRules.cs:158-160`,
about the neighbouring shape: the re-kind "**DESTROYS a branch that exists**, turning one decision into
a parallel split with nothing in the metadata saying so, whereas building a lone default is a caller
declaring a shape that may simply not be finished yet". That reasoning carries over here without
modification. Building two unruled paths off an ordinary step **is the legitimate way to express a
parallel split** — it is TC-13's own subject, it builds by design, and R12 is a **Warning** rather than
an error precisely because the shape is common and usually intended. Withdrawing the last condition
from `[conditional, default]` is a user destroying a decision they already had. One end state, two
acts, different information about intent.

Guard the build door symmetrically and every implicit parallel split becomes an error — refusing
legitimate processes to close an asymmetry the design chose on purpose. Same failure mode as moving
`ValidateStructure` onto the modify path, one shape over.

**What this run legitimately surfaces is the build side's silence, which is G2.** R12 warns on the plan
check and says nothing in the build response, so a caller who skips `validate-process-graph` builds a
fan-out and is told nothing at all. The right fix is a **warning in the build response**, not a
refusal — `create-business-process` already promises in its own description that a successful build can
carry `Warning` entries. That one change covers G2 and this shape together.

So G4 is **folded into G2 as its strongest evidence** rather than standing as a separate defect. What it
adds is the concealment: instance `9cd41719` has terminate ends, one ending in the log, two ordinary
`sequence` flows in the schema, and nothing anywhere recording that two branches ran. G2 previously
argued the fan-out from a two-path log; this run shows the fan-out **and** how invisible it is.

**And TC-18's own expectation is wrong for this leg, which is mine to fix.** The case demanded a refusal
"both ways" on the Group 6 premise that one mistake must be caught wherever it is expressed. That
premise assumes both doors receive the same *act*. At build time there is no withdrawal — there is
nothing yet to withdraw — so leg 1 asks the tooling to refuse a legitimate parallel split. The case has
been revised: leg 1 now expects the build to **say what it did**, not to refuse, and the refusal
expectation is scoped to the withdrawal on an existing decision.

## Prediction, registered and refuted

The review session predicted before the run that TC-18 would find the two doors **agreeing**, on the
grounds that the invariant is enforced by two mirror guards — `EnsureADefaultHasSomethingToFallBackFrom`
on the build side and `WouldDropTheLastBranch` on the modify side — both per-flow and both reachable
from the create and modify paths. The run refutes it. The two guards are not mirrors for this input:
the build-side one is keyed on `kind == Default` and the withdrawal expressed as `[plain, plain]` never
reaches it. Registering the prediction is what made the refutation cheap to state.

The gateway remark came out as predicted, and confirms the case had to move off the drawn element:

> 'CheckAmountGateway' is a gateway that chooses between its branches … This gateway already has its
> 'default' branch, so this flow needs a condition - or re-kind the existing default first.

## TC-18's verdict: run, and it earned its keep — but not as a pass or a fail

The withdrawal leg passed: refused, atomically, with a message that names the element, states the
consequence and gives a remedy that works. The build leg was accepted, and after the analysis above
that is **correct behaviour against a wrong expectation** — the case was asking for a refusal the design
deliberately does not give. So TC-18 does not report a product defect; it produced the best evidence in
the suite for one that was already open (G2), and it exposed a premise error in its own text.

That is worth stating plainly rather than filing as a pass: a case that runs, reveals nothing wrong with
the product, and corrects itself is a useful case, and calling it a pass would hide both halves.

## Stand housekeeping

- A throwaway probe Contact remains: `188e2d82-e223-4c7e-9614-4289be0db116`, "BPTest R9 TC18 Probe
  Contact". `odata-delete` returned IIS **405 Method Not Allowed** — the DELETE verb is blocked at the
  web-server level on this stand, which is a stand configuration fact, not a clio or process defect.
  The executor flagged it rather than working around it. Needs manual cleanup.
- Three processes left in place: `UsrBPTestR9Tc18Build_Threshold`,
  `UsrBPTestR9Tc18Modify_Threshold`, `UsrBPTestR9Tc18Gateway_Threshold`. `BPTest R8 TC18` untouched.
- The `autoupdate` cross-version defect recurred during this run: the branch-built clio rewrote
  `autoupdate` as an object and the released clio behind the interactive session's MCP then refused
  every environment-bearing call with "clio settings bootstrap is broken". Repaired by writing the
  scalar form back, with a timestamped backup. Fifth recurrence; still no owner.
