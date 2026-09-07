# Story 15: The liveness probe never returns for the sickest kind of worker

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 7 (the stage that gets the probe's first production caller — sticky reuse)
**Status**: ready-for-dev
**Size**: S

## As a
supervisor deciding whether a sticky worker may be handed the next call

## I want
`ProbeLivenessAsync` to answer within a bound

## So that
the question "is this worker still alive?" cannot itself hang on the worker it is asking about

## Design
- **The contract and the implementation disagree.** `WorkerRelaySession.ProbeLivenessAsync`
  (`clio/Command/McpServer/Relay/WorkerMcpRelay.cs:240-252`) documents its return as *"`true` when the worker
  answered; `false` when it failed or closed"*, and it is built as `ListToolsAsync` wrapped in a `catch` that
  turns any failure into `false`. But `ListToolsAsync` → `RequestAsync` only completes when the worker answers
  (`:471-473`), when the worker's pipe closes (`FailAllPending`, `:480-482`), or when the caller's own token
  fires (`:279-283`). There is no timer anywhere in that path.
- **So the one failure mode the probe exists to catch is the one it cannot report.** A worker whose pipe is
  open and which answers nothing — the wedge this whole boundary exists to remove, now reproduced one process
  down — never closes its stdout and never sends a response, so the probe waits forever unless the caller
  brought its own token. "Sick worker" and "healthy but slow worker" are indistinguishable to a call that
  never returns, and the caller that was meant to decide the kill is the one that is stuck.
- **There is no production caller yet**, verified: the only reference outside the type itself is
  `clio.tests/Command/McpServer/WorkerMcpRelayTests.cs:405`, and the relay namespace is deliberately excluded
  from DI auto-registration (`clio/BindingsModule.cs:1381-1386`). Its natural first caller is Stage 7 — a
  sticky worker being reconsidered for reuse (TC-E-701's `compile-status` polls reach the same worker) — which
  is why the stage is 7 and not 2. Stage 2's supervisor exists and never probes.
- **Two exits, and they must stay distinguishable.** A caller-supplied token that fires is a CANCELLATION and
  must keep throwing `OperationCanceledException` — the current `catch (OperationCanceledException) { throw; }`
  is deliberate, because a cancelled probe has learned nothing about the worker. The new internal bound must
  produce `false` ("it did not answer in time"), which is a verdict, not a cancellation. Collapsing the two
  makes a shutdown look like a dead worker.
- The bound belongs to the probe, with a default it carries itself and an explicit override for the caller —
  a probe whose bound comes only from the caller is the same defect written one level up. Keep it small
  relative to the spawn cost the ADR measured (§2.4: p50 2.76 s spawn + `initialize` on Windows); the point of
  probing at all is that reusing a live worker beats spawning a new one, so a probe that costs more than a
  spawn has no reason to exist.

## Acceptance Criteria
- [ ] AC-01 — A worker whose pipe stays open and which never answers makes `ProbeLivenessAsync` return `false`
      within its own bound, with no caller-supplied token involved.
- [ ] AC-02 — A caller token that fires still throws `OperationCanceledException`; a cancelled probe never
      reports `false`.
- [ ] AC-03 — A worker that answers `tools/list` still returns `true`, and the probe still never uses `ping`
      (not served on protocol revision `2026-07-28`, ADR §3.1b).
- [ ] AC-04 — The internal bound is a default on the relay's own options with a per-call override, and its
      value is justified against the measured spawn cost rather than picked.
- [ ] AC-05 — The pending slot for a timed-out probe is removed, so a late `tools/list` answer cannot resolve
      a request nobody is waiting on.

## Tests
Unit TC-U-705 (`WorkerMcpRelayTests`, `Module=McpServer`), against the fake child transport: a transport that
accepts the request and never answers returns `false` inside the bound; a fired caller token throws; a healthy
worker still returns `true`. No timing-sensitive sleeps — drive the bound through the options record, the way
`ReadLoopShutdownGrace` is already driven in the disposal tests.
