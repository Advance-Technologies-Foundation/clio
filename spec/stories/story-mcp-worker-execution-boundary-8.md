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
- `BudgetPolicy = terminal-stage` for this family.
- Gated by ClioRing contract tests **and** the Windows x64 NativeAOT publish. Contract changes can alter source-generated DTO/serialization paths, so a JIT-only pass is not evidence.

## Acceptance Criteria
- [ ] AC-01 — Bounded by terminal stage; a mid-deploy budget expiry never leaves a half-installed environment (TC-E-801).
- [ ] AC-02 — ClioRing contract suite green; byte/schema parity on the committed stage-event fixture; unknown-field tolerance and ordered replay preserved.
- [ ] AC-03 — `dotnet publish clio-ring/ClioRing.Desktop -r win-x64 -p:PublishAot=true` green; no new IL2026/IL3050 (TC-C-801).
- [ ] AC-04 — No agent, test, probe, watcher, retry or startup path performs a real deploy/uninstall without an explicit user gesture and a disposable target.

## Tests
E2E TC-E-801; ClioRing TC-C-801.
