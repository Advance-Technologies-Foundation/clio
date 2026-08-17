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
- [x] AC-05 — **OQ-1 CLOSED 2026-08-17** on Windows Server 2022 (ADR §2.4): spawn + `initialize` p50 **2.763 s**
      (n=8, 4x the macOS figure), and Job Object kill-on-close gives full subtree containment **only** with
      `CREATE_SUSPENDED` → assign → `ResumeThread`. Assign-after-start leaks exactly one grandchild, reproduced.
      Consequence for this story: `Process.Start` cannot express `CREATE_SUSPENDED`, so the Windows path must
      P/Invoke `CreateProcess` or use `PROC_THREAD_ATTRIBUTE_JOB_LIST`. Implement to that, not to "start then assign".
- [x] AC-06 — **OQ-2 CLOSED 2026-08-17** (ADR §2.4): wall time grows linearly past core count, so the cap is
      `Environment.ProcessorCount`-derived, not a constant. Memory is not the constraint (1 GB at width 16 of 16 GB).
- [ ] AC-07 — **The budget clock starts at SPAWN, never at admission.** At width 16 a healthy call waited 16.9 s
      just to reach `initialize`; a 12 s budget measured from enqueue would have killed it. Killing healthy calls
      for being queued is a failure mode this fix would otherwise invent. Needs a test.

## Tests
Unit: TC-U-201, TC-U-202. E2E (`clio.mcp.e2e`): TC-E-201 (Unix), TC-E-203 (Windows), TC-E-202. Measured: TC-M-201, TC-M-202.
**Full unit suite required** — DI composition root is touched.

## Notes
AC-05 and AC-06 are gates, not paperwork: everything measured so far was on macOS.
