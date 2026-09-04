# Story 18: Two writers can reach one child transport, and nothing proves that is safe

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 4 (the relay owns the write side) — **must land before Stage 7**, since sticky workers are what make
the overlap ordinary rather than rare
**Status**: ready-for-dev
**Size**: S

## As a
caller whose request shares one worker with a sampling answer or a second call

## I want
the relay's writes to the child to be serialised, by the SDK or by the relay

## So that
two messages cannot interleave into one framed stream and hand the worker a line it cannot parse

## Design
- **The relay writes to the child from four places, and one of them is deliberately off the read loop.**
  In `clio/Command/McpServer/Relay/WorkerMcpRelay.cs`: `RequestAsync` (`:285-288`, the caller's thread),
  `HandshakeAsync` (`:428`, `:448-451`), `AnswerChildRequestAsync` (`:556-559`) and `RespondWithErrorAsync`
  (`:575-579`). The last two run inside `Task.Run(() => AnswerChildRequestAsync(request))` (`:469`) — off the
  loop on purpose, so a slow client cannot stall notification forwarding — which means a sampling answer is
  written by a thread that has nothing to do with the caller's.
- **The read side is single-consumer and says so; the write side has no equivalent statement.** The relay's
  own documentation is explicit that `ITransport.MessageReader` is a `ChannelReader` and therefore
  single-consumer (`IWorkerMcpRelay.cs:168-172`, asserted by
  `OpenAsync_ShouldDrainTheChildMessageReaderExactlyOnce_WhenTheSessionIsOpened`). Nothing anywhere records
  whether `ITransport.SendMessageAsync` may be called concurrently. The relay's transport is the SDK's
  `StreamClientSessionTransport` (reached through `StreamClientTransport`,
  `WorkerChildTransportOwner.cs:79-80`), and whether that type serialises writes internally is
  **unverified** — it was assumed, not measured, and the SDK's own client never had two writers because it
  owns the whole client leg.
- **What breaks if it does not serialise.** The framing is newline-delimited JSON. Two interleaved writes
  produce one corrupt line, so the worker either fails to parse (and the parent sees a worker that answers
  nothing, i.e. the wedge one process down) or drops a request that will never be answered. This is a
  silent-in-the-wrong-way failure: it looks like a sick environment, not like a client bug.
- **Reachability, stated honestly.** Today the relay has no production consumer, and a per-call worker's
  traffic does not overlap by construction: one `tools/call` is sent, then the caller awaits, and the only
  other write is a sampling answer that occurs during that await. The overlap becomes ordinary at Stage 7 —
  a second `tools/call` issued on a sticky worker while a sampling answer for the previous one is being
  written — and story 14 adds a fifth writer (`notifications/cancelled`) on the canceller's path. So this is
  cheap now and load-bearing later, which is why it blocks Stage 7 rather than depending on it.
- **Verify first, then branch.** Read the shipped 2.2.0 `StreamClientSessionTransport` (the same way the
  transport-owner note verified by reflection that the stdio transport derives from it) and settle the
  question:
  - if it serialises internally, pin that with an interleaving test so a future SDK bump that removes the
    guarantee fails here rather than in the field;
  - if it does not, add a send gate in `WorkerRelaySession` — an async mutex around the write, never a `lock`
    across an `await` — and pin the same test.
  Either way the record stops being "assumed safe".

## Acceptance Criteria
- [ ] AC-01 — Whether the SDK transport serialises writes is established by reading the shipped assembly and
      written into the relay's own remarks, with the SDK version it was checked against.
- [ ] AC-02 — Two writers racing on one session (a sampling answer while a request is sent) produce two whole,
      parseable messages on the child's stdin, in a test that fails if either is truncated or interleaved.
- [ ] AC-03 — If a gate is added, it is async (no `lock` held across `await`) and it does not serialise the
      READ path — the notification order guarantee comes from the single reader and must not be re-derived
      from the write gate.
- [ ] AC-04 — The hot path cost is unchanged for the common case of one writer.

## Tests
Unit TC-U-405 (`WorkerMcpRelayTests`, `Module=McpServer`): a fake child transport that records complete
messages and blocks mid-write; a sampling answer and a `tools/call` issued concurrently must both arrive whole.

## Notes
This is the one finding of the six whose current status is "unknown", not "wrong". If the SDK already
serialises, the whole story collapses into one test plus one sentence of documentation — and that is the cheap
outcome worth paying for before sticky workers make the race routine.
