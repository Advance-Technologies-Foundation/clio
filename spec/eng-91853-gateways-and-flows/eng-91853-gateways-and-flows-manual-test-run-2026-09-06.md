# ENG-91853 — manual test run, 2026-09-06

## Verdict

**Stored level only; design time and runtime not verified.** This was a `--mode agent` run. It
establishes what the toolkit *wrote*, read back independently, and nothing more. Six of eleven cases
were reached and all six pass at the stored level. Five were never attempted.

The feature itself came out well: a blind agent with no repository, no memory and no sight of the
issue built an exclusive gateway with a conditional and a default branch, an overlapping pair of rules
in the right precedence order, a parallel split with a join, and swapped a rule-driven branch with the
fallback in place — all from business sentences. **One functional defect matters more than any of
that**, and it is in the half of the ticket that was supposed to remove a two-step route: see D1.

## Run header

| | |
|---|---|
| Issue | ENG-91853 |
| Stand | `Creatio` → `http://d_krestov_n.tscrm.com:40001` (.NET Framework, MSSQL, `IsDemoMode: true`) |
| Mode | `agent` (no manifest existed, so `browser` could not run) |
| Isolation | `isolated` — no `ANTHROPIC_API_KEY`, no `apiKeyHelper`. Hooks, user `CLAUDE.md`, skills and plugins still loaded. **Efficiency numbers here are not comparable with a `bare` run.** |
| clio commit | `97017ec32` |
| Knowledge | library `1.13.94`, sequence `1013094`, revision `6ea736c47f00f14539ae101989cf1a63409b408e` |
| Package version | `1.4.0.59`, installed by this run (stand was on `1.4.0.58`) |
| Prompt | `…-manual-test-prompt.md` @ `eb5cfddd4` |
| Run id | `14dd2c73-2741-45f6-9bab-14a9608c13a3` |

**Positive control (phase 1, gate 2):** `routing` 9 309 chars, `process-branch-conditions` 10 571,
`process-modeling` 27 276, `process-activity-connections` 24 496 — all `success: true`. The library
served real content; no guidance finding below is an artefact of an empty library.

**Content gate (gate 3):** the pinned revision's `branch-conditions.md` carries the gateway rules
(`ELEMENTS are buildable`, the three placement rules, `parallelGateway` starts every branch) and
`process-modeling.md` declares both gateways `BUILDABLE`. The same probe against `master` returns **0**
occurrences. The generation measured is the one under test.

## Baseline — the minimum a well-guided agent would use

Stated before the numbers, so a count means something.

| Case | Minimum sequence | Calls |
|---|---|---|
| TC-01 | routing → branch guide → build → describe → run ×2 → read log | 7 |
| TC-02 | build → describe → run ×3 → read log | 6 |
| TC-03 | build → run → read log | 4 |
| TC-04 | build → describe → run → read log | 5 |
| TC-06 | modify → describe → run ×2 → read log | 5 |
| TC-07 | modify → describe → run ×2 → read log | 5 |
| **Total for the six reached** | | **32** |

Observed: **146** tool calls, of which 82 were nested command dispatches. Roughly 4.5× the baseline.
Most of the excess has one cause, D1; the rest is in the efficiency table.

## Results per case

| Case | Stored | Design time | Runtime | Evidence |
|---|---|---|---|---|
| TC-01 | **PASS** | not verified | executor-reported only | `UsrAmount_Route`: exclusive gateway, `default` → fast track, `conditional` → approval. Read back independently |
| TC-02 | **PASS** | not verified | executor-reported only | `UsrAmount_Escalate` was built with `> 1000` → director **before** `> 100` → manager, plus a default — the overlapping pair in the correct precedence order |
| TC-03 | **PASS** | not verified | **observed** in the transcript | `UsrAmount_RouteStrict`: `> 1000` then `> 100`, no default — the adversarial shape exactly |
| TC-04 | **PASS** | not verified | executor-reported only | `UsrParallelCheck_Confirm`: parallel split → two reads → parallel join → confirm; every outgoing flow plain |
| TC-06 | **PASS** | not verified | executor-reported only | `setFlowCondition` moved the threshold `100 → 250` in place; the flow kept its position among its siblings |
| TC-07 | **PASS** | not verified | executor-reported only | two `setFlow` calls swapped the roles in place: the rule-driven arm became the fallback and the fallback took a rule. Lanes unchanged (185 / 315 / 445) |
| TC-05 | not reached | not reached | not reached | executor ended before the second sub-agent got here |
| TC-08 | not reached | not reached | not reached | as above |
| TC-09 | not reached | not reached | not reached | as above |
| TC-10 | not reached | not reached | not reached | as above |
| TC-11 | not reached | not reached | not reached | as above |

**Runtime is marked `executor-reported only` deliberately.** The executor did call `run-process` nine
times and did read process logs, but this mode does not verify runtime, and its own account is evidence
of what it believed. The one exception is TC-03, whose runtime message appears verbatim in a tool
result and is quoted under D2.

### What the validator returned, unprompted

Four plan checks ran. All four results are worth keeping:

| Shape checked | Result |
|---|---|
| gateway + conditional + default (TC-01) | no findings |
| gateway + `> 1000` + `> 100` + default (TC-02) | no findings |
| gateway + two conditionals, **no default** (TC-03) | **R7 warning**, correctly |
| parallel split → two branches → parallel join (TC-04) | **no findings** — no R8 false positive on a legitimate parallel section |

The last row is the useful regression evidence: the deadlock rule stayed quiet on the shape it must
stay quiet on.

## Defects

### D1 — a condition that references a process parameter cannot be declared on the build path

**Owner: `CrtProcessBuilder` + clio (the contract text).** This is the finding of the run.

**Observed.** Every one of the four processes was created with its branches **plain or default only**,
and every condition was added afterwards through `modify-business-process`. Not once did the executor
use `flows[].condition` on the build path — the capability this ticket exists to add.

**Why.** A condition must reference a parameter by its UId meta-path
(`[#[Parameter:{3ebebf0f-…}]#] > 1000`). That UId does not exist until the process has been created. A
name-based reference is refused: `[#Amount#]` fails the pre-save gate with *"Formula value error:
Expression expected (at index 0)"*. So for any condition that mentions a process parameter — which is
essentially all of them — the build path cannot carry it, and the two-step route is the only one
available.

**How much of the product this covers — measured, not estimated.** Every `metadata.json` in the 7.8.0
corpus (11 844 files) was classified by what its conditional flows reference:

| What the condition references | n | share | buildable on the create path? |
|---|---:|---:|---|
| an element's output — `[Element:{uid}].[Parameter:{uid}]` | 487 | 45.9% | **no** |
| a process parameter — `[Parameter:{uid}]` | 445 | 41.9% | **no** |
| a literal or a call into the schema's own generated methods | 92 | 8.7% | almost never |
| `[#SysSettings.X<Type>#]` | 37 | 3.5% | **yes** |
| *(excluded: 344 flows with an empty expression — the runtime substitutes `true`)* | | | |

**932 of 1 061 shipped conditions — 87.8% — carry a UId that does not exist before the process is
created.** The only family that is writable on the build path today is `SysSettings`, at 3.5%, because
it is addressed by name and needs no UId. The 92 "literal" ones are mostly calls like
`IsRemindingNeeded()` / `GetNextMassMailing()` into the schema's own generated code, which a create-path
caller cannot produce either, plus a few rows from `CrtBase/BaseEditPage`, which is not a business
process at all.

Named, checkable examples of conditions this ticket's own create path cannot express:

- **`BulkFileManagement/DeleteFilesInTable`** — `[#…[Parameter:{17726c7b…}]#] == [#…#]`. This is the
  retry-loop topology the layout half of this very ticket is argued on. The ticket's showcase process
  cannot be built by the tool the ticket ships.
- **`BulkFileManagement/ScheduleFileCleanup`** — three gateways, all on process parameters.
- **`BpmGDPR/BpmProcess5`** — several gateways on one element's output.
- **`AzManagerIntegration/EnvironmentsActualizationFromAzManagerJobPusherProcess`** — `…!=true`.
- For contrast, what *would* build today: **`CrtBase/ExpireLicenseNotificationProcess`** —
  `[#SysSettings.ExpireLicenseNotificationTerm<Int32>#] > 0`.

A fix must cover **both** UId-bearing forms: process parameters (41.9%) and element outputs (45.9%).
Neither alone reaches half the corpus.

**Why it matters.** `CreateBusinessProcessTool`'s own description instructs the opposite:

> Declare the branch here rather than building the flow plain and setting its condition afterwards:
> the two-step route saves the process once with a flow that does not yet branch.

The tool asks for something the platform makes impossible, and the window it warns about — a process
saved with a flow that does not yet branch — is exactly what every run will produce.

**Minimal reproduction.**

1. `create-business-process` with a process parameter `Amount` (Integer, In) and
   `flows: [{source: "Gw", target: "A", kind: "conditional", condition: "[#Amount#] > 100"}]`.
2. Observe: `Process validation failed: … Formula value error: Expression expected (at index 0)`.
3. There is no form of the condition that both references `Amount` and is writable before the process
   exists.

**The mechanism already exists in the package.** This is not a platform limitation; it is an
inconsistency between two neighbouring surfaces. `ProcessMappingService` resolves a mapping source
**by name** and expands it into the meta-path itself:

```csharp
/// Resolves a process-level parameter by name …
private static ProcessSchemaParameter ResolveProcessParameter(ProcessSchema schema, string name)
…
sourceValue.Value = string.Format(MetaPathFormat, processParameter.GetMetaPath());
```

Mappings take `processParameter` by name on the same schema object, in the same build call. Conditions
take raw text and store it verbatim (`ProcessGraphBuilder.cs:226`). `ProcessParameterDescriptor` has no
`uid` field either, so the caller cannot pre-generate one and close the gap from outside.

**Recommended fix.** Accept `[#ParameterName#]` inside `flows[].condition` on the build path and expand
each name to its meta-path in a pass over `flows[]` after the parameters are created, refusing by name
when one does not resolve. Reasons for that shape specifically: it reuses `ResolveProcessParameter` /
`GetMetaPath` rather than inventing a syntax; and it is the **discoverable** form — this session's
author and the blind executor both wrote `[#Amount#]` independently, unprompted, on first contact.

**Stopgap, worth shipping either way.** The create tool's description currently instructs the caller to
do the impossible. Until resolution lands it should say that a condition referencing a process
parameter belongs to the modify step. Note the two are coupled: if resolution lands, that sentence
becomes wrong again — so they are one decision, not two independent edits.

### D2 — the no-default warning names an exception the operator never sees

**Owner: clio.** Already raised in the review gate; this run confirms it end to end, in one session.

`validate-process-graph` on TC-03's shape returned:

> Diverging gateway 'CheckAmount' has no default flow: if no condition matches at run time the process
> instance fails with **MismatchItemsCountException**. Add a default flow, or confirm the conditions
> cover every case.

Running that same process produced, in `SysProcessLog`:

> None of the conditions were met after the element "Check amount". The business process execution has
> been suspended and cannot continue. Possible causes — The conditional element in your business
> process does not have a default outgoing flow. — All outgoing flows of the branching element have
> conditions that evaluated to false.

**Reproduction:** build a gateway with two false conditions and no default, validate it, run it,
compare the two texts. The exception name appears nowhere the operator can see. Fix: name the
observable symptom in the warning.

### D3 — `odata-read` rejects the argument shapes an agent naturally writes

**Owner: clio (tool description).** Five failed calls, four of one kind:

- `invalid-parameter-type: argument 'select' … must be an array` ×4
- `invalid-parameter-type: argument 'order-by' … must be a string` ×1

**Reproduction:** call `odata-read` with `select` as a comma-separated string. Fix: say the types in
the description, or accept the string form and split it.

### D4 — a command reachable only through the dispatcher looks like a resident tool

**Owner: clio (tool profile / routing).** The executor called `mcp__clio__list-user-tasks` and got
*"No such tool available"*, then reached the same thing through `clio-run list-user-tasks`, which
worked. **Reproduction:** as written. Fix: either make it resident or name it in the routing table so
the dispatcher is the obvious route.

### Not a defect, and worth recording as a pass

`setFlowCondition` on a default branch was refused with:

> …default branch, which runs when no condition matched, so a condition cannot be ADDED to it. To
> convert it into a conditional branch instead, use `setFlow` with kind `conditional` and the
> condition.

The executor followed that sentence and succeeded on the next call. This is the dead end the review
gate flagged and the branch fixed, demonstrated working by an agent that had never seen the code.

## Efficiency findings

| # | Signal | Evidence | Cost | Owner | Proposed fix |
|---|---|---|---|---|---|
| 1 | Success reached by a path the guidance does not describe | every create used plain/default flows, every condition arrived by a later modify | the two-step route on all four processes | `CrtProcessBuilder` contract | D1 — either resolve name references server-side or document the split |
| 2 | `get-tool-contract` fetched after a failure | 8 contract fetches beside 19 `ToolSearch` calls | ~27 discovery calls before productive work | tool descriptions + routing article | Name the process-designer tools in the routing table so discovery is one hop |
| 3 | Retry after a validation error a prior read would have prevented | the five `odata-read` type errors | 5 wasted calls | `odata-read` description | D3 |
| 4 | `clio-run` used where a resident tool was assumed to exist | `list-user-tasks` | 1 wasted call + a detour | tool profile | D4 |

**Not counted as findings**, per the rubric: the `setFlowCondition` → `setFlow` retry (the guidance
prescribed it and it worked), and the raw turn count on its own.

**No parallel write burst occurred.** Writes were sequential; the stand stayed healthy throughout and
answered `list-packages` normally afterwards.

## Invalidated by the prompt

Two, and both are narrow. Neither hides a product defect.

1. **TC-07 was applied to TC-02's process**, which is legitimate — TC-07's precondition is *"a process
   with two rule-driven paths and one fallback"*, and TC-02's was one. But it overwrites TC-02's end
   state, so TC-02 is no longer inspectable in place afterwards. This run only recovered TC-02's
   verdict by replaying the executor's operations out of the transcript. **Fix:** give TC-07 its own
   process, or require TC-02 to be read back before any later case touches it.
2. **The prompt does not bound how the suite is executed.** The executor split the eleven cases across
   two background sub-agents and then ended its own session while the second was still working — in
   print mode nothing collects a background agent afterwards. Five cases were lost this way, and the
   final TC-01..TC-11 report the prompt asks for was never produced. **Fix:** state that the cases are
   to be executed directly and in order, and that the report is written as they complete rather than at
   the end.

Neither is a reason to move a product defect out of the sections above.

## Environment notes worth carrying into the skill

- The package checkout for this feature is `C:/Projects/workspace/ProcessBuilder`, not
  `C:/Projects/cli-process-builder` as `references/environment.md` states — the latter sits on an
  unrelated branch.
- clio here targets **net10.0**. `clio/bin/Release/net8.0` still ships `1.4.0.58` and would have
  installed the wrong package while every later observation looked valid.
- Two executor actions were refused by the harness classifier (*"Blocked by classifier"*). That is a
  run-environment artefact, not a product behaviour, and it degrades a clean-room measurement.
- **The skill's scripted teardown cannot restore the machine when the run installed a higher
  generation than the pre-run pin.** Restoring the settings and reinstalling was *rejected*: `Git
  knowledge source 'creatio-curated' rejected sequence 1013088; the previously validated sequence is
  1013094. The previous revision 6ea736c4… was restored.` The rollback guard is monotonic, so the plain
  restore is a no-op that silently leaves the **test** library active — the exact leftover the teardown
  section exists to prevent. The working sequence is `delete-knowledge --force` **then** install, the
  same trick the setup already uses for the same reason. Worth adding to
  `references/environment.md`.

## Teardown

Recorded in the teardown section appended below after phase 7.

## Continuing this run

The four processes are left on the stand deliberately — they are the input to the browser half.

```bash
/bp-test-run ENG-91853 --mode browser --env Creatio
```

That run appends design-time and runtime verdicts to **this** file, so all three levels for a case end
up together. Until it has run, every `not verified` above stays `not verified`.
