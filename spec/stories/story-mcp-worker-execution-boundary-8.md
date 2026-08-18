# Story 8: Long synchronous / streaming commands (deploy, uninstall)

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 8
**Status**: ready-for-dev
**Size**: L

## As a
ClioRing user watching a deployment

## I want
deploy and uninstall bounded by their terminal stage rather than by a stopwatch

## So that
a budget expiry never leaves a half-installed environment

## Design
- **Process lifetime ≠ response budget** (rule 4). `deploy-creatio` and `uninstall-creatio` are synchronous, destructive and progress-streaming, and ClioRing waits for the authoritative terminal stage. A generic 45–60 s kill could leave the environment half-installed — the one place where killing the process is the wrong tool.
- `BudgetPolicy = terminal-stage` for this family — and `Location = worker`, `OperationFamily = deploy` for both tools, which is what the Stage 1 invariant TC-U-108 enforces.
- **The signalling protocol is ADR §3.3 and it is binding**, because without it `terminal-stage` is a label and TC-E-801 can only prove one implementation passes. In short: the signal rides the **existing stage-event stream** (`_meta.clioStageEvent`, terminal `status` on the root `runId`) — no second IPC path; the parent's bound is a **stage-event silence timer** (default 300 s), not an operation timer, so a healthy long deploy is never truncated; a **30 s post-terminal exit grace** covers a child that hangs after its terminal stage; and a child that exits or goes silent without a terminal stage produces an explicit **indeterminate** error naming the last stage reached, with **no automatic retry** — retry-on-ambiguity turns one half-installed environment into two.
- Gated by ClioRing contract tests **and** the Windows x64 NativeAOT publish. Contract changes can alter source-generated DTO/serialization paths, so a JIT-only pass is not evidence.

## Acceptance Criteria
- [ ] AC-01 — Bounded by terminal stage; a mid-deploy budget expiry never leaves a half-installed environment (TC-E-801).
- [ ] AC-02 — ClioRing contract suite green; byte/schema parity on the committed stage-event fixture; unknown-field tolerance and ordered replay preserved.
- [x] AC-03 — **DONE 2026-08-17** (ADR §2.4): publish exits 0 in 53.3 s, zero IL2026/IL3050/IL2104/IL3053,
      and the output is a real native image — `clio-ring.exe` 30.8 MB with no managed `clio-ring.dll` beside it.
      Re-run this whenever the contract changes; it is cheap and it is the only check that sees source-generated
      serialization paths. Note the host requirement: it needs the MSVC toolchain, which ts1-core-dev04 cannot
      install (ESET TLS interception breaks the VS catalog download).
- [ ] AC-04 — No agent, test, probe, watcher, retry or startup path performs a real deploy/uninstall without an explicit user gesture and a disposable target.
- [ ] AC-05 — **Lost child:** a child killed mid-deploy, and one silent past the stage-event timeout, each produce an explicit indeterminate error naming the last stage reached — never a success, never an automatic retry (TC-E-802).
- [ ] AC-06 — **Post-terminal grace:** a child that emits its terminal stage then hangs is killed after the grace window and the tool result is the terminal stage, not an error (TC-E-803).

## Tests
E2E TC-E-801, TC-E-802, TC-E-803; ClioRing TC-C-801. **Full unit suite required** — the protocol touches the supervisor and relay under `clio/Common/**`.

## Recon findings (2026-08-18) — read before implementing

A read-only pass over the shipped code established the following. Two of them are perimeter conditions
the ADR did not state, and one is an outright error in the ADR that would send an implementer down a
path that can never match.

### The tool set is exactly two

`deploy-creatio` (`clio/Command/McpServer/Tools/InstallerCommandTool.cs`, attribute at `:43-49`) and
`uninstall-creatio` (`clio/Command/McpServer/Tools/UninstallCreatioTool.cs`, attribute at `:32-38`) are
the only tools declaring `BudgetPolicy = TerminalStage`, and the cross-field invariant at
`McpToolExecutionMetadata.cs:284-294` (`Deploy ⇒ Worker + TerminalStage`) pins them there.

Fifteen tools declare `RequiresClientRequests = Progress` and two declare `Sampling`, but the other
thirteen carry a parent-kill budget and belong to stages 6 and 7. They matter to stage 8 only as the
reason the relay must stay full-duplex. Derive that list from the declared metadata, not from
`McpProgressHeartbeat` call sites: `stop-creatio` and `start-creatio` emit progress by calling
`SendNotificationAsync` directly (`StopTool.cs:85`, `StartTool.cs:50`), so a call-site derivation misses
them, and `list-apps` declares `None` with the reason in the comment at `ApplicationTool.cs:38` (it takes
no server parameter).

### Neither tool runs in a worker today

`McpWorkerCohort.cs:63-71` ships seven names and neither of these is among them, so
`McpExecutionRouter.Decide` returns `InProcessOutsideCohort` (`McpExecutionRouter.cs:125-127`). The
declaration says `Worker`; the cohort says the machinery is not built. Adding the two names is step one —
and it must not happen before the protocol below exists, because the dispatcher currently kills at the
ordinary budget unconditionally (`McpWorkerCallDispatcher.cs:182-185`).

Both are also non-resident, so the live caller reaches them through `clio-run`
(`clio-ring/ClioRing/ViewModels/UninstallFormViewModel.cs:239`, `InstallFormViewModel.cs:264-265`). The
live dispatch site is therefore `ClioRunTool.cs:181-194`; the matched and unmatched sites must behave
identically for a raw-name call.

### Correction to ADR §3.3 — the terminal vocabulary in the ADR does not exist

§3.3 described a terminal stage as a stage event whose `status` is `Completed` / `Failed` / `Cancelled`.
The shipped contract has no such tokens. `ClioStageEventContract.cs` declares
`EventTypes = { "manifest", "stage", "run-completed" }` (`:32-42`) and
`RunOutcomes = { "success", "failure", "success-with-warnings" }` (`:74-84`); `ClioStageDetail.Status`
is `running`/`done`/`failed`/`skipped`/`warning` (`:54-71`) and is per-stage, never terminal.

Terminal detection is: a progress notification whose `_meta.clioStageEvent.eventType` is
`"run-completed"` and whose `runId` is the run's root `runId`; the outcome is `runCompleted.outcome`.
An implementer coding the ADR's original wording gets a condition that never fires, so every deploy
times out on silence and reports indeterminate — a defect that reads as an environment problem.

**There is no `cancelled` outcome at all.** A cancelled deploy therefore emits no terminal event and
must resolve through the indeterminate path, not a terminal one.

### Perimeter condition: a caller who did not ask for progress produces no events

`StageEventProgressForwarder.cs:57-63` returns a no-op subscription when the progress token is null. A
client calling `deploy-creatio` without one emits zero stage events, so a silence-bounded protocol would
declare that healthy deploy a lost child. A terminal-stage route with no caller token needs a synthetic
token injected on the child leg, through the seam that already clones params
(`McpWorkerCallDispatcher.cs:323-354`). Whether synthetic-token progress is forwarded upward or
suppressed is a deliberate sub-decision — suppression is the one exception to rule 1 and must be
documented as such if chosen.

### The indeterminate result shape is constrained by the consumer, not free

ClioRing classifies a no-terminal result itself, reading the payload rather than trusting `IsError`
alone (`InstallFormViewModel.cs:290-340`, `DescribeUnstreamedFailure`). The indeterminate result must
set `IsError = true` **and** structured content with `success: false` plus a non-empty `error` — the
shape `BudgetExpiredResult` and `RelayFailureResult` already produce. Anything else lands in Ring's
"outcome genuinely unknown" branch, which for a possibly half-installed environment is the wrong
message. Do not reuse `BudgetExpiredErrorClass`: its shipped guidance says the call is safe to retry.

### What must be asserted on counters, and the one counter that does not exist here

`spec/test-plans/tp-mcp-worker-execution-boundary.md` makes counter assertions load-bearing, but
deploy and uninstall are local-only commands with no `IApplicationClient` — deploy *creates* the
instance (`StageEventProgressForwarder.cs:22-28`). **There is no Creatio backend counter to sample.**
The substitutes are: spawn count exactly 1 (this is the discriminator for "never an automatic retry" —
a retry loop is invisible to any timing or result assertion), kill count exactly 1 and its ordinal
position relative to composing the result, and the fixture child's own emitted-event log stopping after
the kill. State this in the pull request, because a reviewer applying the counter rule mechanically will
ask for a backend delta that cannot exist for these two tools.

### The supervisor needs no changes

The post-terminal exit grace can be implemented entirely from the dispatcher using `lease.HasExited`
(`IWorkerProcessSupervisor.cs:141`) and `KillContained` (`:231`). Do not repurpose
`WaitWithinBudgetAsync` (`:226`) — its `BudgetExpired` semantics are the generic-kill model stage 8
exists to replace. This materially reduces how much stage 8 collides with stage 7.

### Sequencing

Stage 8 must not run in a parallel wave with stories 14/15/16/18: its read-loop tap and story 18's send
changes occupy the same region of `WorkerMcpRelay.RunReadLoopAsync`, and case (c) above *is* the
composition of story 14's cancellation contract with this protocol — building it first means guessing.

### The NativeAOT gate must be re-run

`dotnet publish clio-ring/ClioRing.Desktop -c Release -r win-x64 --self-contained -p:PublishAot=true`
passed on 2026-08-17 (ADR §2.4). It must be re-run whenever the contract changes, and it cannot run on
ts1-core-dev04: an ESET TLS filter re-signs HTTPS with an untrusted certificate authority and the Visual
Studio installer fails to download its catalogue. Check the published output has no managed
`clio-ring.dll` beside the executable — a bare exit code does not distinguish real ahead-of-time
compilation from an executable wrapping intermediate language.
