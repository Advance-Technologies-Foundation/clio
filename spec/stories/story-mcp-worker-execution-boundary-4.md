# Story 4: Transparent full-duplex relay

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 4
**Status**: ready-for-dev
**Size**: XL

## As a
client whose tool call now runs in another process

## I want
the parent to relay requests, responses, sampling and notifications transparently

## So that
nothing about the MCP contract changes — including the parts that fail silently when they break

## Design
- Parent relays `tools/call` and the response verbatim. **Full-duplex, not call/response forwarding** (rule 1): `update-page` / `sync-pages` call `server.SampleAsync` (`PageBodySamplingService.cs:130`), and a child whose client is the parent degrades semantic review to `Skipped=true` with no error at all.
- **The relay owns the child's transport read loop** (rule 12). Measured on SDK 1.4.1: forwarding through `McpClientHandlers.NotificationHandlers` reordered `0..5` into `[5,4,2,3,0,1]`, and a single-consumer FIFO in the parent did **not** fix it (`[2,0,1,3,4]` on retry) — the reordering is at or before the SDK's handler dispatch. Owning the read loop makes forwarding inherit the pipe's order.
- Notifications forwarded **raw**. ClioRing reads `_meta.clioStageEvent`, the exact progress token, and buffers by `(runId, sequence)`; deserialising and rebuilding breaks it.
- **Composes with `adr-mcp-durable-invocation.md`** — both need `WithCallToolHandler`. Order is fixed: durable **name resolution first** (which canonical tool a name means, via `McpToolCompatibilityCatalog`), then routing (where that tool executes). Routing first would key on an alias and miss. Extend the existing handler; do not register a competing one.
- **Router sits after the destructive-confirmation seams** (rule 9), or unmatched writes bypass host gating.
- Cancellation propagates parent → child.

## Acceptance Criteria
- [ ] AC-01 — **Sampling actually executes**: a marker planted in the client's sampling answer appears in the tool result; `update-page` produces a real review, not `Skipped=true` (TC-E-401).
- [ ] AC-02 — `_meta.clioStageEvent` and `progressToken` byte/schema identical to the committed contract fixture (TC-E-402).
- [ ] AC-03 — Monotonic sequence delivery under concurrency (TC-E-403).
- [ ] AC-04 — Cancellation propagates; the child stops issuing backend requests (TC-E-404).
- [ ] AC-05 — Structural: notifications are not forwarded through the SDK's client notification handlers (TC-U-401).
- [ ] AC-06 — Router runs after destructive confirmation; an unmatched write cannot bypass gating.
- [ ] AC-07 — `ClioRing.Tests` green; unknown-field tolerance and ordered replay preserved (TC-C-401).

## Tests
E2E TC-E-401…404; unit TC-U-401; ClioRing contract TC-C-401.

## Notes
AC-01 is the story's real risk: it fails **silently** if got wrong.
