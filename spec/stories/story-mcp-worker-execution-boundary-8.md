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
- [ ] AC-03 — `dotnet publish clio-ring/ClioRing.Desktop -r win-x64 -p:PublishAot=true` green; no new IL2026/IL3050 (TC-C-801).
- [ ] AC-04 — No agent, test, probe, watcher, retry or startup path performs a real deploy/uninstall without an explicit user gesture and a disposable target.
- [ ] AC-05 — **Lost child:** a child killed mid-deploy, and one silent past the stage-event timeout, each produce an explicit indeterminate error naming the last stage reached — never a success, never an automatic retry (TC-E-802).
- [ ] AC-06 — **Post-terminal grace:** a child that emits its terminal stage then hangs is killed after the grace window and the tool result is the terminal stage, not an error (TC-E-803).

## Tests
E2E TC-E-801, TC-E-802, TC-E-803; ClioRing TC-C-801. **Full unit suite required** — the protocol touches the supervisor and relay under `clio/Common/**`.
