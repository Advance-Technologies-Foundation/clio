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
- **The relay owns the child's transport read loop** (rule 12) — and this is CHEAP, not a fork. Re-measured on
  SDK 2.2.0 (ADR §3.1): `IClientTransport.ConnectAsync` → `ITransport.MessageReader` (a
  `ChannelReader<JsonRpcMessage>`) gives the messages in pipe order through the PUBLIC api. Keep
  `StdioClientTransport` for spawn, framing and serialization; skip only `McpClient.CreateAsync`, which is what
  installs the reordering dispatch layer. ~120 lines, proven 30/30 on 2.2.0. Do NOT hand-roll JSON-RPC.
- **Sampling is deprecated in 2.2.0** (MCP9005 / SEP-2577) — it still works, but record OQ-6 and do not build
  new dependencies on it. Measured on SDK 1.4.1: forwarding through `McpClientHandlers.NotificationHandlers` reordered `0..5` into `[5,4,2,3,0,1]`, and a single-consumer FIFO in the parent did **not** fix it (`[2,0,1,3,4]` on retry). The two runs disagreeing is the evidence that the SDK dispatches notification callbacks **concurrently** — a FIFO fed by racing producers preserves their disorder faithfully, so no buffering depth helps. Owning the read loop works because it takes messages off the wire serially, before SDK dispatch is involved (ADR §3.2).
- Notifications forwarded **raw**. ClioRing reads `_meta.clioStageEvent`, the exact progress token, and buffers by `(runId, sequence)`; deserialising and rebuilding breaks it.
- **Two dispatch seams, and routing must cover both** (ADR §9). The relay's target is the MATCHED path — `filters.AddCallToolFilter(McpToolErrorFilter.HandleCallToolErrors)` (`BindingsModule.cs:1160`) — NOT `McpDurableCallToolHandler`, which is a no-op whenever `MatchedPrimitive` is set. But the unmatched path (`WithCallToolHandler`, `BindingsModule.cs:165`) is how a deprecated alias arrives, so routing there too is required or an alias executes in-process while its canonical runs in a worker. Inside each path, name resolution runs before routing.
- **Router sits after the destructive-confirmation seams** (rule 9), or unmatched writes bypass host gating.
- Cancellation propagates parent → child.

## Acceptance Criteria
- [ ] AC-01 — **Sampling actually executes**: a marker planted in the client's sampling answer appears in the tool result; `update-page` produces a real review, not `Skipped=true` (TC-E-401).
- [ ] AC-02 — `_meta.clioStageEvent` and `progressToken` byte/schema identical to the committed contract fixture (TC-E-402).
- [ ] AC-03 — Monotonic sequence delivery under concurrency (TC-E-403).
- [ ] AC-04 — Cancellation propagates; the child stops issuing backend requests (TC-E-404).
- [ ] AC-05 — Structural: notifications are not forwarded through the SDK's client notification handlers (TC-U-401).
- [ ] AC-06 — Router runs after destructive confirmation; an unmatched write cannot bypass gating, and a refused confirmation prevents dispatch entirely (TC-U-403).
- [ ] AC-07 — Both seams route and agree: a tool reached by canonical name and by deprecated alias resolves to the same execution location (TC-U-402).
- [ ] AC-08 — `ClioRing.Tests` green; unknown-field tolerance and ordered replay preserved (TC-C-401).

## Tests
E2E TC-E-401…404; unit TC-U-401, TC-U-402, TC-U-403; ClioRing contract TC-C-401. **Full unit suite required** — `BindingsModule.cs` is touched to register the relay filter on both dispatch seams.

## Notes
AC-01 is the story's real risk: it fails **silently** if got wrong.
