# Story 2: Process supervisor: containment, cap, cleanup

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 2
**Status**: ready-for-dev
**Size**: L

## As a
parent process spawning workers on behalf of callers

## I want
a supervisor that bounds concurrency, contains descendants, and cleans up after a crash

## So that
a killed worker takes its children with it and a dead parent leaves nothing behind

## Design
- Concurrency cap with queueing (never drop a call), plus resource accounting.
- **Containment, not EOF** (rule 6): Windows **Job Object with kill-on-close**; Unix **process-group containment plus parent-death signalling**. Closing a pipe is not containment — a child that ignores EOF survives it.
- **Identity-checked** stale-worker cleanup at parent startup. PIDs are reused; killing a stranger's process is its own defect.
- The prototype **leaked one orphan** when the parent was killed mid-operation. That is the case TC-E-201 exists for; it is measured behaviour, not a hypothetical.

## Acceptance Criteria
- [ ] AC-01 — Cap admits N, queues N+1, drops nothing (TC-U-201).
- [ ] AC-02 — Stale-worker cleanup is identity-checked; a reused PID owned by a stranger is not killed (TC-U-202).
- [ ] AC-03 — **R-8a (Unix): SIGKILL the parent while a worker has a descendant of its own: both disappear** on Linux and macOS (TC-E-201).
- [ ] AC-03b — **R-8b (Windows): the same containment via Job Object kill-on-close** (TC-E-203). Split from AC-03 because the verification differs and Windows is unmeasured (OQ-1) — one cross-platform criterion would be satisfiable by a Unix-only test and then read as green everywhere. Any delivery before this passes is explicitly scoped to R-8a.
- [ ] AC-04 — Budget expiry kills the worker and its descendants; the parent answers with a bounded error (TC-E-202).
- [ ] AC-05 — **OQ-1 closed**: Windows child spawn cost and Job Object containment measured (TC-M-201). Until this number exists, no cohort ships on Windows.
- [ ] AC-06 — **OQ-2 closed**: memory/CPU ceiling for concurrent children measured; the supported maximum is a number, not "8 was fine on a laptop" (TC-M-202).

## Tests
Unit: TC-U-201, TC-U-202. E2E (`clio.mcp.e2e`): TC-E-201 (Unix), TC-E-203 (Windows), TC-E-202. Measured: TC-M-201, TC-M-202.
**Full unit suite required** — DI composition root is touched.

## Notes
AC-05 and AC-06 are gates, not paperwork: everything measured so far was on macOS.
