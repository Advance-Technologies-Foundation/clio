# Story 17: Nobody reads the worker's standard error

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 2 — the SUPERVISOR owns that stream, not the relay; must land before Stage 6 routes the first cohort
**Status**: ready-for-dev
**Size**: S

## As a
operator, or an agent reading a failed tool result

## I want
the worker's standard error captured and attached to the failure

## So that
a worker that dies during startup says why, instead of producing one sentence about a closed transport

## Design
- **The stream exists and is redirected; nothing consumes it.** `WorkerProcessSupervisor` starts the child
  with `RedirectStandardError = true` (`clio/Common/McpWorker/WorkerProcessSupervisor.cs:339`), hands the
  stream through the containment handle (`:360`, `:502`, `:520`, `:560`) and exposes it on the lease as
  `Stream StandardError` (`clio/Common/McpWorker/IWorkerProcessSupervisor.cs:126`). A search of `clio/` finds
  no reader: the only other `StandardError` uses are `AppUpdater`, `SkillRepositoryResolver` and
  `ProcessExecutor`, none of which touches a worker.
- **Ownership matters here and is easy to get wrong.** The relay never sees this stream by design:
  `IWorkerChildTransportOwner.ConnectAsync` takes stdin and stdout only
  (`clio/Command/McpServer/Relay/WorkerChildTransportOwner.cs:51`), because the transport must not own the
  process. So this drain belongs to the supervisor / lease, and a fix written in the relay would be in the
  wrong place.
- **What the operator gets today.** A worker that fails before or during `initialize` — a bad executable path,
  a missing runtime, an unhandled startup exception, a `--worker` argument the build does not know — writes
  its diagnosis to stderr, then exits. The relay's read loop sees the pipe close and every pending caller
  is faulted with `"The worker closed its transport before answering."`
  (`clio/Command/McpServer/Relay/WorkerMcpRelay.cs:480-482`). That sentence is true and useless: it is the
  same message for every startup failure there is.
- **The second consequence is worse than missing logs, and it is why this cannot wait for "better logging
  later".** A redirected pipe has a finite OS buffer. A child that writes more to stderr than the buffer holds
  BLOCKS on the write — the documented .NET `Process` deadlock when a redirected stream is never drained — and
  a blocked child stops emitting stage events. For a sticky or deploy worker that lands in ADR §3.3's
  stage-event silence timer, so the parent declares a lost child and reports an *indeterminate* outcome, when
  the real cause was an undrained pipe on the parent's own side. The diagnosis would point at the environment.
- **Redaction is not optional on this path.** The test plan already assumes this drain exists: TC-U-505
  requires that a known secret marker appears nowhere in parent output, error envelopes **or worker-stderr
  passthrough** (R-7). Route the captured text through the same `SensitiveErrorTextRedactor` the relay already
  uses for child→parent error text (`WorkerMcpRelay.cs:566-567`).
- Keep it bounded: a fixed-size tail (last N KB) is enough to explain a startup failure and cannot turn a
  chatty worker into a memory problem. Drain continuously — capturing only after the process exits is exactly
  the case that deadlocks.

## Acceptance Criteria
- [ ] AC-01 — The worker's standard error is drained continuously from the moment the process starts, so a
      chatty worker can never block on a full pipe.
- [ ] AC-02 — A worker that fails during startup produces a failure that carries its stderr tail, not only
      "the worker closed its transport before answering".
- [ ] AC-03 — The captured text is redacted with the same rule as every other MCP error text (TC-U-505's marker
      must not appear).
- [ ] AC-04 — The capture is bounded (documented tail size) and the bound is stated where the text is surfaced,
      so a truncated diagnosis is not mistaken for the whole one.
- [ ] AC-05 — A healthy worker's stderr — which is where a well-behaved MCP server logs — does not become tool
      output, and does not pollute the parent's own stdout, which is the parent's MCP transport.

## Tests
Unit TC-U-203 (`Module=McpServer`, supervisor-level): a fake worker that writes more than one pipe buffer to
stderr and then exits is drained without blocking, and its tail reaches the failure; the redaction marker from
TC-U-505 does not survive into the surfaced text.

## Notes
AC-05 is the one to get wrong by accident: on the parent side stdout IS the MCP transport, so anything routed
there instead of to the log corrupts the protocol stream for the real client.
