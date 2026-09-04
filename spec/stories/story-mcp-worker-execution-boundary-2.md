# Story 2: Process supervisor: containment, cap, cleanup

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 2
**Status**: in-progress
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
- [x] AC-01 — Cap admits N, queues N+1, drops nothing (TC-U-201).
- [x] AC-02 — Stale-worker cleanup is identity-checked; a reused PID owned by a stranger is not killed (TC-U-202).
- [x] AC-03 — **R-8a (Unix): SIGKILL the parent while a worker has a descendant of its own: both disappear** on Linux and macOS (TC-E-201).
- [ ] AC-03b — **R-8b (Windows): the same containment via Job Object kill-on-close** (TC-E-203). Split from AC-03 because the verification differs and Windows is unmeasured (OQ-1) — one cross-platform criterion would be satisfiable by a Unix-only test and then read as green everywhere. Any delivery before this passes is explicitly scoped to R-8a.
- [x] AC-04 — Budget expiry kills the worker and its descendants; the parent answers with a bounded error (TC-E-202).
- [x] AC-05 — **OQ-1 CLOSED 2026-08-17** on Windows Server 2022 (ADR §2.4): spawn + `initialize` p50 **2.763 s**
      (n=8, 4x the macOS figure), and Job Object kill-on-close gives full subtree containment **only** with
      `CREATE_SUSPENDED` → assign → `ResumeThread`. Assign-after-start leaks exactly one grandchild, reproduced.
      Consequence for this story: `Process.Start` cannot express `CREATE_SUSPENDED`, so the Windows path must
      P/Invoke `CreateProcess` or use `PROC_THREAD_ATTRIBUTE_JOB_LIST`. Implement to that, not to "start then assign".
- [x] AC-06 — **OQ-2 CLOSED 2026-08-17** (ADR §2.4): wall time grows linearly past core count, so the cap is
      `Environment.ProcessorCount`-derived, not a constant. Memory is not the constraint (1 GB at width 16 of 16 GB).
- [x] AC-07 — **The budget clock starts at SPAWN, never at admission.** At width 16 a healthy call waited 16.9 s
      just to reach `initialize`; a 12 s budget measured from enqueue would have killed it. Killing healthy calls
      for being queued is a failure mode this fix would otherwise invent. Needs a test. **(TC-U-204.)**
- [x] AC-08 — **The Unix kill target is never derived from the worker's current process group.** Measured on a
      development host: `Process.Start` calls neither `setsid` nor `setpgid`, so a spawned child inherits the
      LAUNCHING shell's group — an orphaned .NET descendant had process group 17401 while the launching shell was
      itself process 17401. `getpgid(worker)` therefore names the parent clio, the agent host and the user's
      interactive shell, and a group kill derived from it would take all three out on an ordinary budget expiry.
      A group kill is issued only once the worker is a group LEADER (`getpgid(pid) == pid`), which is exactly what
      `setpgid(0, 0)` in the worker establishes and no inherited group can imitate; leadership is the promotion
      proof, and merely differing from the parent's group is not. Before promotion the kill falls back to the
      best-effort tree kill, which suffices there because a worker that has not answered `initialize` has spawned
      nothing. No `tools/call` may be relayed before that barrier. **(TC-U-203.)**
- [x] AC-09 — **Parent-death signalling is SIGTERM plus a handler, never SIGKILL.** AC-03 requires that BOTH the
      worker and its descendant disappear, and a `SIGKILL`ed worker cannot pass anything on to its own children.
      The worker must therefore receive a signal it can handle and respond by killing its own group. This is why
      AC-03's wording constrains the mechanism and not only the outcome.

## Tests
Unit: TC-U-201, TC-U-202. E2E (`clio.mcp.e2e`): TC-E-201 (Unix), TC-E-203 (Windows), TC-E-202. Measured: TC-M-201, TC-M-202.
**Full unit suite required** — DI composition root is touched.

Delivered coverage (Stage 2): TC-U-201 + TC-U-201b (cap, queueing, caller-only cancellation), TC-U-202 + TC-U-202b
(identity gate, both directions), TC-U-203 (group-leadership barrier), TC-U-204 (spawn-anchored budget), TC-U-205
(Windows command-line quoting — the one part of the Windows path verifiable on any host), TC-U-206 (a live foreign
parent's workers are left alone), TC-U-207 (muxer-versus-apphost launch shape), TC-U-208 (core-count cap);
TC-E-201, TC-E-202 (green on macOS), TC-E-203 (Windows, skipped elsewhere with an explicit platform reason).

## Notes
AC-05 and AC-06 are gates, not paperwork: everything measured so far was on macOS.

**Delivery scope: R-8a only.** AC-03 / TC-E-201 and AC-04 / TC-E-202 are green on macOS, so Unix containment is
delivered and verified. AC-03b / TC-E-203 is implemented to the sequence ADR §2.4 measured green
(`CREATE_SUSPENDED` → `AssignProcessToJobObject` → `ResumeThread`, via `CreateProcessW`, because `Process.Start`
cannot express a suspended creation) but **cannot be executed on a macOS host at all**; it closes on a Windows
run of `Team_Atf_ClioMcpE2eTests`. Its test declares that requirement and skips with an explicit reason rather
than passing silently, because one cross-platform criterion satisfied by a Unix-only run reads as green
everywhere — the exact failure the threat model warns about.

**`depends_on: 1` in `spec/sprint-status.yaml` is not a compile-order dependency.** The supervisor consumes no
execution metadata: it resolves a launch descriptor, spawns, contains, caps, bounds and reaps processes, and makes
no routing decision. The edge is rollout order (nothing routes to a worker until Stage 6), and Stage 2 was
implemented in parallel with Stage 1. The tracker was left untouched.
