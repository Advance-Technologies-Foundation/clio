# ENG-95891 — manual testing on a stand: two runs, what they measured, what they found

Consolidated report for the formula-expressions manual testing, 2026-09-01. Companion documents: the
two run reports, their manifests, the verification document written by the feature session, and the
prompt both runs executed.

## What this was

An AI agent was given a business-language prompt in an isolated session — no memory, no repository
access, no sight of the Jira issue — with one toolset: the clio MCP surface and the guidance library
that ships with it. It had to build six processes on a real stand from business requirements alone.

The subject under test is not the agent. It is three things: **CrtProcessBuilder** (what serializes,
renders and executes), **clio** (tool contracts, validation, error text), and **the clio knowledge
library** (the guidance that steers an agent through the other two). The prompt is the instrument.

Both runs covered the **stored** level only: what the toolkit wrote and reads back. Design time and
runtime remain unmeasured — no run has opened a process in the designer or started one.

## The two runs at a glance

| | R1 | R2 |
|---|---|---|
| Guidance generation | 1.13.54 at 950a998 | **1.13.65** at eb69cf7 |
| Articles under guides/processes/ | 3, monolith at 102 KB | **10**, process-modeling at 20 KB, new formulas.md |
| Parameter-reference form taught | **no** | **yes** |
| Total tool calls | 77 | **44** |
| Assistant turns | 160 | **82** |
| create-business-process | 10 | **6** — one per process |
| modify-business-process | 33 | **10** |
| describe-business-process | 13 | 11 |
| get-guidance | 3 | 6 |
| Filesystem calls (Bash / Read / Grep) | 10 | **0** |
| Failed calls | 25 | 6 |
| Distinct refused expressions | **18** | **2** |
| Against baseline (~26 calls) | 2.75x | **1.7x** |
| Cases passing at stored level | 6/6 | 6/6 |

Stand for both: krestov-test, core 10.0.731.0, CrtProcessBuilder 1.4.0.18. clio built from
feature/ENG-95891-formula-expressions at 77a8ff1ba — confirmed for R1. If R2 ran after that commit its
own head should be recorded separately rather than shared with R1: the branch has moved since, and a
manifest whose point is reproducibility cannot round two runs to one commit.

## R1 — sound observations, two wrong diagnoses

R1 reported three defects. The observations were all real; two of the attributions were not.

It pinned the guidance checkout HEAD, which sat on a **detached commit older than the work under
test**. The commit that teaches the parameter-reference form, 4feb042, is not an ancestor of that
revision, and the article split had not reached it either. So R1 measured a library nobody ships and
reported the result as a live defect of the library.

The two gates it ran both passed: the revision matched what was on disk, and guidance served real
content. Neither asks the question that mattered — does the pinned generation contain the change under
test?

**A stale generation does not fail loudly. It fabricates guidance defects.** The rubric then works
perfectly against the wrong library: every signal fires, every finding looks real, and the fix goes to
an article that was already corrected.

R1 also cast doubt on the task own TC-11 note, which states that the short reference form works. That
doubt was unfounded: the verification tested the short form with a write, and R2 stored it directly.
Both forms round-trip. The long meta-path is what the **server** writes when a structural mapping is
converted, which is why an agent with no guidance finds that one first; the short form is what an
author writes.

## Miss by miss — where the calls actually went

### R1: 21 of 25 failures were one search on one process

All on UsrPrice_ComputeTotal (TC-A1), calls 19 to 46. Sixteen distinct ways to name a parameter, each
refused:

| Attempted | Server answer |
|---|---|
| Math.Ceiling([#Price#]) | Expression expected (at index 13) — does not parse |
| Math.Ceiling([Price]) | same |
| Math.Ceiling(PriceParameter) | references PriceParameter, which does not exist |
| Math.Ceiling(Price) | references Price |
| PriceParameter, TotalParameter | do not exist |
| Get("PriceParameter") | references Get |
| d3b65326-0952-… (raw uid) | references d3b65326 |
| uid with hyphens stripped | does not exist |
| price (lower case) | references price |
| Parameters.PriceParameter | references Parameters |
| [#Price#] alone | Expression expected (at index 0) |
| CurrentUser, SysVariable.CurrentUser | do not exist — probing whether any macro resolves |
| X, Parameter1 | do not exist |

Two calls outside that sweep: a mapping onto ProbeTask.Duration — the agent built a scratch element to
see what a *structural* mapping looks like, which is how it finally recovered the token — and an
attempt to build a formulaTask element, answered with "Element type formulaTask is not supported yet".

The remaining four failures are not failures: three are the cases themselves (a fractional value into
an Integer, a missing parameter, a namespace-qualified call) and one is a ToolSearch miss.

Worth noting where misses did **not** happen: A2 and A3 ran almost clean, because the token was
already known. The entire 2.75x overshoot is the price of one discovery.

### R2: six failures, none of them wasted

| # | Call | Assessment |
|---|---|---|
| 9, 10, 13 | ToolSearch returns "No matching deferred tools found" | the D2 gap — three attempts, not one |
| 34 | 3 / 2 into an Integer, cannot be used as Int32 | TC-B1, the designed refusal |
| 39 | TotalParameter not found | TC-B2, the designed refusal |
| 43 | System.Math.Abs(-1), references System | TC-B3, the designed refusal |

**Two** refused expressions in total, both put there by the prompt on purpose. Against eighteen in R1.

Zero wasted domain calls is a stronger result than "44 versus 77": the guidance did not shorten the
search, it removed it. The agent wrote the short reference form on the first attempt.

## D2 — the one finding that survives, correctly diagnosed

R1 report claimed the executor had already loaded the process tools through ToolSearch and then still
routed everything through the generic executor. That is false, and it makes an architectural fact look
like an agent choosing a slow path.

Measured:

1. ToolSearch asking for create-business-process, describe-business-process, modify-business-process
   and validate-process-graph returned **No matching deferred tools found**.
2. The whole process-designer surface is absent from the resident profile: CreateBusinessProcess,
   ModifyBusinessProcess and DescribeProcess appear **zero** times in McpCoreToolProfile.cs, and
   neither CoreToolTypes nor AlwaysOnLazyToolTypes carries a process tool.

clio-run was the only route. The agent found the fast path does not exist and took the one that does.

What remains as measurable cost:

- **Discovery overhead: 7 of 44 calls (~16%)** — five ToolSearch (three failing) plus two
  get-tool-contract, all before any work begins.
- **A wasted round trip with an unhelpful answer.** "No matching deferred tools found" is true and
  useless: the tool exists, it is simply not deferred-loadable, and nothing points at clio-run.
- **Read-only operations are classified destructive.** Both clio-run and clio-run-destructive are
  Destructive=true (ClioRunTool.cs:173), so describe-business-process — a pure read — reaches the host
  as destructive and needs confirmation. R2 did 11 read-backs; in a permission-gated host that is 11
  prompts. The read-back is exactly what guidance tells an agent to do to verify its own work, and it
  is the operation the classification penalises hardest.

Two independent decisions, neither taken here: whether the process surface stays long-tail (if so, the
routing guidance should say so, which removes the failed searches), and whether a read-only long-tail
dispatch should be non-destructive (the information exists at dispatch time — the result meta already
reports destructive false for the tool it dispatched).

## What the runs established, and what they did not

**Established at the stored level, verified by independent read-back rather than the executor own
account:** rounding over a parameter reference; the three aggregates Max / Avg / Mod; the three date
parts Day / Month / DayOfWeek without a Get prefix, fitting Integer targets; and three refusals that
each name the target, quote the expression as written, and leave the parameter untouched.

**Not established by any run:** design time and runtime. Nothing has been opened in the designer and
nothing executed. A green stored-level result is not a working feature.

## What this produced in the harness

Three changes to the manual-test skills, each caused by a failure in these runs:

1. **A content gate.** Assert that the commit introducing the guidance under test is an ancestor of the
   pinned revision, and compare the article inventory. Its absence produced R1 two false diagnoses.
2. **An attribution guard in the rubric.** No finding may be attributed to the knowledge library until
   the served generation is identified, and three positions are kept apart: closed in the served
   generation, closed in the main branch, closed only on an unmerged branch.
3. **A note that a probe which can only fail one way is not evidence** — carried over from the
   verification, where a read-only shell variable made a correct note look wrong.

Also recorded: --strict-mcp-config restricts MCP servers, not built-in tools. Both executors had Bash,
Read and Grep and could have read the clio repository or the guidance checkout, which would have
invalidated the measurement. Verified after the fact that neither did — R1 filesystem calls were
grepping the guidance article the harness had spilled to disk, and R2 made none.

## State to clean up

Machine, in test configuration **as of 2026-09-01**. Re-read `info-knowledge` before acting on this
list — it has already moved once. At the time R1 was measured the active source served 1.13.54 at
`950a998` with the 102 KB monolith; it now serves 1.13.65 at `eb69cf7` with ten articles, i.e. the R2
setup is still installed. A teardown list is a claim about the present and ages faster than the rest of
a report:

- guidance pinned to tmp/eng95891-kb-1165 in clio-knowledge, a temporary ref pushed only so the git
  transport could reach 1.13.65 — deletable once testing ends;
- the local-dev flag knowledge-allow-unsequenced is **enabled**; while it is on, the content-integrity
  check is weakened for this source on every clio run on this machine;
- appsettings.json.bpskills-backup holds the released configuration.

Teardown, three steps: disable the flag, restore the backup, reinstall the released library. The third
matters because setup deleted the release generation to get past the sequence guard.

Stand: six R2 processes (BPTest ENG95891 R2 A1 to B3) in package Custom, unrun — the input for the
design-time and runtime pass. R1's schemas were deleted before R2 and verified gone — recorded here as
seven, while the R1 manifest lists six cases and names no seventh. `ProbeTask` was an ELEMENT inside one
of the six, not a schema of its own, so it does not account for the difference. Left as a discrepancy
rather than rounded to six: the schemas are gone, so neither number can be re-measured, and guessing
which is right would put an unverifiable count in the one document a reader will trust for it.
