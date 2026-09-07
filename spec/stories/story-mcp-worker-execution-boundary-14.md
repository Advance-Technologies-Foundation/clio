# Story 14: A cancelled `tools/call` never tells the worker

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 7 (deferred out of Stage 4a on purpose — the correct shape depends on the sticky session model Stage 7 introduces)
**Status**: ready-for-dev
**Size**: M

## As a
caller who cancels a tool call that is running in a sticky worker

## I want
the relay to tell the worker its call was abandoned

## So that
a worker that outlives the response stops working on an answer nobody will ever read

## Design
- **What the relay does today**, read out of `clio/Command/McpServer/Relay/WorkerMcpRelay.cs`, not inferred:
  `RequestAsync` registers the caller's token before it sends (`:279-283`), and the callback does
  `session.TakePending(key)?.TrySetCanceled(token)` — it removes the LOCAL pending slot and releases the
  awaiter. Nothing at all is written on the child leg. When the worker eventually answers, the read loop's
  `JsonRpcResponse` branch (`:471-473`) calls `TakePending(...)` on a key that is no longer in the
  dictionary, gets `null`, and drops the answer silently. The worker is never told and keeps executing.
- **Who this actually bites is narrow, and that is why it is a Stage 7 story and not a Stage 4 defect.** A
  per-call worker is reclaimed by the supervisor — the budget is measured from spawn and the lease kills the
  process (`clio/Common/McpWorker/IWorkerProcessSupervisor.cs:100-117`) — so an abandoned call dies with the
  process it was running in. Every `Lifetime = sticky` tool is the opposite case: the worker is deliberately
  kept, so it goes on running the abandoned tool, goes on holding its Creatio-side session, and is then handed
  the NEXT call on the same transport while the old one is still in flight (which is also why story 18 must
  land before sticky ships).
- **The mechanism is one line; the contract around it is not.** MCP defines `notifications/cancelled`
  (`requestId`, optional `reason`), and emitting it is a single `ITransport.SendMessageAsync` on the child
  leg. What Stage 4a deliberately did NOT guess:
  - whether a worker that was told to cancel is returned to the sticky pool or retired — a worker that
    ignores the notification is still busy, and reusing it hands the next caller a worker with a running tool;
  - what a cancelled STARTER means for the family that has no operation registry (rule 5:
    `install-process-builder`, `create-app-section`, restart-by-credentials have no terminal status to reap
    on, hence Stage 7's private completion signal);
  - the interaction with the terminal-stage protocol (ADR §3.3): a cancelled deploy must still produce an
    explicit *indeterminate* outcome naming the last stage reached, never a silent success and never an
    automatic retry.
- **Do not send it from inside the cancellation callback.** That callback runs synchronously on whichever
  thread cancelled the token, under the registration; a write to a transport that is closing would surface
  there as an exception in an unrelated place. Emit it from the request path (the `catch` in `RequestAsync`
  already runs on cancellation, `:291-294`), on a token that is not the one that just fired.
- Late-answer handling must not regress: after cancellation the response for that id still has to be
  discarded without faulting the session, exactly as `:471-473` does now.

## Acceptance Criteria
- [ ] AC-01 — Cancelling a `tools/call` writes `notifications/cancelled` on the child leg carrying the SAME
      request id the relay used for that call, and the caller's await still completes as cancelled.
- [ ] AC-02 — A late response for a cancelled id is still discarded; it must not fault the session or the next
      caller's pending slot.
- [ ] AC-03 — The reuse decision for a cancelled sticky worker is explicit and written down (returned to the
      pool only after a bounded liveness confirmation — story 15 — or retired outright), not left to whichever
      code path reaches the worker first.
- [ ] AC-04 — Per-call workers behave exactly as they do today; the supervisor kill remains their bound and no
      extra round trip is added to the hot path.
- [ ] AC-05 — TC-E-404's claim ("the child stops issuing backend requests") is asserted for a STICKY worker on
      backend request counters — the per-call case is satisfied by the kill and proves nothing about this one.

## Tests
Unit TC-U-704 (`clio.tests/Command/McpServer/WorkerMcpRelayTests.cs`, `Module=McpServer`): cancelling a
pending call emits `notifications/cancelled` with the matching id; a late response for that id is dropped
without faulting. E2E TC-E-702: cancel a call on a sticky worker and assert on the stub's `/counters` that the
worker stops issuing backend requests — timings prove nothing here.

## Notes
Story 4's AC-04 / TC-E-404 is satisfied today only in the per-call sense, by the supervisor kill. This story is
the sticky half of it, and the test plan's TC-E-404 row now says so rather than leaving the two records to
disagree quietly.
