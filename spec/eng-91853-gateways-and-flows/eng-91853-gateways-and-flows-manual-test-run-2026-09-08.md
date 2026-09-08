# ENG-91853 — manual test run, 2026-09-08 (TC-16, TC-17)

Scope: the two cases added to the suite today, run for the first time. TC-18 was written but not run.
TC-01…TC-15 are running separately (see the manifest for that run's id).

## Run identity

| | |
|---|---|
| Executor | blind `claude -p` session `c8140d14-0c66-4ae3-8e3e-9b48e196268f`, clio MCP only (`--strict-mcp-config`) |
| clio | branch build `clio/bin/Release/net8.0/clio.dll`, `feature/ENG-91853-gateways-and-flows` @ `77ad67223` |
| Guidance library | pinned `af57c415` — 1.13.99, the ENG-91853 knowledge branch, `knowledge-allow-unsequenced` on |
| CrtProcessBuilder | 1.6.0.3 on the stand |
| Stand | `Creatio` — `<dev-stand>:40001`, core 10.1.37.0, .NET Framework |
| Processes left in place | `BPTest R6 TC16` (`UsrBpTest_R6Tc16`), `BPTest R6 TC17` (`UsrBpTest_R6Tc17`), package `Custom` |

Every claim below was re-verified by me against the stand after the run, independently of the
executor's write-up: schema read-back through `describe-business-process`, runtime through
`SysProcessLog` / `SysProcessElementLog`. Where the executor's account and my measurement agree I say
so; the two things it did not report are findings F1 and F3.

## TC-16 — three routes converge at one place — PASS

**Stored.** `UsrBpTest_R6Tc16`, schema `7c450da7-721a-4b4a-8bdb-07441be38d37`. One diverging
`ProcessSchemaExclusiveGateway` ("Route request by amount") with three outgoing flows —
`>1000` conditional, `>100` conditional, one `default` — and one converging
`ProcessSchemaExclusiveGateway` ("Amount routes joined") with **3 incoming `sequence` flows and
exactly 1 outgoing flow**. The join is a distinct element, not an implied merge on a shared step.
Conditions are stored against the process parameter, in the platform's own reference form:
`[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{f1fcb88b-…}]#] > 1000`.

**Runtime.** Three instances, one per route. Element log, in order, with the join counted:

| Amount | `SysProcessLog` instance | Elements logged |
|---|---|---|
| 5000 | `38d2ed41` 09:52:37 | Route request by amount → Handle high-value request → **Amount routes joined** → Request completed |
| 500 | `0f5532a8` 09:54:45 | Route → Handle medium-value request → **Amount routes joined** → Request completed |
| 50 | `80244ea3` 09:55:12 | Route → Handle standard request → **Amount routes joined** → Request completed |

Four entries per instance, the join appearing **exactly once** in each — XOR-join semantics, confirmed
at runtime and not merely inferred from the shape. No instance waited on the two routes it did not take.

## TC-17 — rework returns to the same deciding place — PASS on the structural requirement

**Stored.** `UsrBpTest_R6Tc17`, schema `b74a6cc1-1e5e-487c-bf18-6b4516649ee3`. The examining gateway
has **2 incoming** (start, and the rework return off the Approval element) and **2 outgoing**
(`>1000` conditional to the approval, `default` to fulfilment). There is exactly one examining
element; the rework flow targets it directly. The Approved exit is a conditional flow on the
approval's own result parameter (`== Guid.Parse("e79facb3-…")`), and the rework exit is the plain
sibling — the else branch off an activity, which is the shape the suite's Group 7 covers.

**Runtime.** One instance, `98396187` 09:59:49. Element log, in order:

```
09:59:49  Examine request by amount      (pass 1)
09:59:49  Send request for approval      (pass 1)
10:00:15  Examine request by amount      (pass 2)   <- rework returned to the SAME element
10:00:15  Send request for approval      (pass 2)
10:00:36  Fulfil the request
10:00:50  Request fulfilled
```

The examining element ran twice, in the right order, and the second pass came from the rework flow —
the structural requirement of the case, fully met.

**What the case asked for and could not be done.** The case's runtime script asks for the amount to be
*corrected below the threshold during rework* so that pass 2 decides differently. `AmountParameter` is
the process's own `In` parameter, bound once at start; there is no supported way — through clio or
otherwise — to change a running instance's own parameter, and the instance state is an opaque
serialized blob (`SysProcessData.PropertiesData`). The only buildable route to a human-edited number
is an edit page bound to an entity column, and an entity column read back that way is not referenceable
in a flow condition through clio's declarative tools (ENG-91844). The executor reported this correctly
and did the next best thing: it changed the *approval verdict* instead, which exercised the other exit
of the approval but not the gateway's condition. So pass 2 necessarily re-took the same branch. See F2.

## Findings

### F1 — the pinned three-way merge lane, measured on a stand for the first time

Measured, both processes, `position` as stored:

| TC-16 element | position | | TC-17 element | position |
|---|---|---|---|---|
| Request received (start) | `60;185` | | Request received | `60;185` |
| **Route request by amount** (fork) | **`240;173`** | | **Examine request by amount** (fork) | **`240;173`** |
| Handle high-value | `420;173` | | Send request for approval | `420;173` |
| Handle medium-value | `420;303` | | Fulfil the request | `600;303` |
| Handle standard | `420;433` | | Request fulfilled (end) | `780;315` |
| **Amount routes joined** (join) | **`600;303`** | | | |
| Request completed (end) | `780;315` | | | |

The x grid is a clean 180 step. On the y axis the **diverging** gateway sits in the lane of its
**first** branch (`173`, the top of a fan spanning `173`–`433`) while the **converging** gateway sits
in the lane of the **middle** inbound branch (`303`). The 12 px offsets on the events (`185`, `315`
against `173`, `303`) are the deliberate centre correction for the smaller event glyph.

**This is the pinned specification, not a defect.** I first wrote it up as a fork/join inconsistency;
that was wrong, and the review session corrected it. The layout addendum's merge-lane table pins both
outcomes: case A (equal branches) has `mean(0,1) = 0.5` floor to lane **0**, "aligned with the split";
case E (three-way) has `mean(0,1,2) = 1`, lane **1**. Branches are assigned **downward** from the
parent lane and a merge takes the **floored mean** of its inbound lanes — so a two-way merge lands back
on its split's lane and a three-way one does not. `ProcessLayoutEngine.PreferredLane`'s docblock says
it in as many words: its equal-branch case still lands on the split's lane, its three-way case still
lands on the middle branch. The asymmetry is arithmetic, and both rules were chosen deliberately.

What this run therefore contributes is the thing the addendum did not have: case E was pinned by
**arithmetic** and had never been observed on a stand. `240;173` / `420;173,303,433` / `600;303` is
case E to the pixel — the first measured confirmation that the implementation matches the pinned lane
on a real three-way fan, which is exactly why these two cases were asked for. TC-17 confirms the
two-way half of the same table (fork `173`, targets `173` and `303`).

**Carried forward as an owner decision, not a fix.** A three-way fan still *reads* asymmetrically —
the fork looks pinned to the top branch, the join looks centred. That is a legitimate criticism of the
specified behaviour, but changing it changes a pinned spec case and moves every three-way merge in
every process the tooling has already built. It belongs to whoever owns the layout specification, and
filed as a defect it would be closed as working-as-specified. clio holds no layout constants
(`clio/Command/ProcessModel/` has none), so nothing on the clio side is involved either way.

### F2 — TC-17's runtime script asks for something no tool can do

The "corrected amount during rework" leg is not executable on this platform with these tools (see
above). As written, the case cannot be completed by any executor, which makes it a case that will
report the same limitation every run instead of testing the loop. The suite has been amended: the
second pass is now decided by the corrected **verdict**, with the amount limitation recorded as a
known gap rather than a step. The underlying product gap (no way to correct a value mid-instance,
and an entity column not referenceable in a condition — ENG-91844) is a real finding and stays on
record here.

### F3 — neither case's plan was checked before it was built, and the shape they cover is exactly R14's carve-out

The executor built both processes without running `validate-process-graph`, so the run proved the
build path and the runtime but not the plan-check path — on the two shapes whose rules are the newest.
I ran both afterwards against the branch build (`clio-run` → `validate-process-graph`, driven over
stdio so the branch binary answers rather than the released one):

- TC-16 shape (3-way fork, 3-in/1-out XOR join, conditions supplied): `has-errors: false`, **no findings**.
- TC-17 shape (2-in/2-out gateway with the rework loop, conditions supplied): `has-errors: false`, **no findings**.

So the R14 carve-out for a converging gateway's lone `default` exit holds on the exact shape TC-16
builds, and the loop-back shape produces no false positive from the arity or reachability rules. Both
cases now carry a Stored-level line requiring the plan check, so the next run measures this instead of me.

That carve-out is not a detail: unscoped, R14 called **45 shipped gateways invalid** — 40 exclusive and
5 inclusive, `BulkFileManagement/DeleteFilesInTable` and `CaseService/RunSendEmailToCaseGroup` among
them. Until now it was argued from the shipped corpus; this is the first time it has been measured on a
process the tooling itself built.

## For the next runner — read the version before believing the answer

**The MCP server behind an interactive session is the RELEASED clio; the executor ran the branch
build.** Anything read through this session's own clio MCP — a tool contract, a description, a rule set —
describes the released binary, not the branch under test. It cost me a false finding today (see below),
and this is the third time on this ticket that released-versus-branch has produced a confident wrong
answer about our own code: the floor hypothesis, the 1.4.x version numbers no release carries, and now
a tool contract. The probe is fine each time; it points at the wrong artifact. Drive the branch binary
directly over stdio when the answer has to be about the branch, and record which binary answered.

## Checked and dismissed — not findings

- **The join's single exit is stored as `kind: default`, not `sequence`.** By construction: the
  designer's allowed-outgoing list for an or-gateway is conditional + default with no plain sequence,
  which `ProcessGraphValidator.cs:310-311` already states and R14 already carves out. The executor
  read this correctly.
- **The `validate-process-graph` contract I first fetched showed no `condition` field on an edge and
  claimed only "plain sequence flows" are buildable.** That contract came from the **released** clio
  behind this session's MCP. The branch's `ValidateProcessGraphArgs` does carry
  `ProcessGraphEdgeArg.Condition`, and its description documents blank vs omitted; the branch
  description also names R1–R18 and the gateways as buildable. Released-vs-branch, not a defect.
- **My own first validation run reported R2 and R15 errors on the TC-17 shape.** I had omitted the
  `Fulfil → End` edge. The rules were right and I was wrong; the corrected graph is clean.
