# Story 6: Ring ClioStageEvent mirror + raw _meta notification adapter in ClioRing.Ipc

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio\clio-ring` (branch `spike/ring-clio-ipc`)
**FR coverage**: FR-08 (consume typed events over MCP progress `_meta`), FR-12 (unknown-field tolerance, sequence de-dup / out-of-order drop)
**AC coverage**: AC-11 (unknown field tolerated; duplicate/out-of-order sequence ignored)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D1 fact 6, D2, D5)
**Status**: review
**Size**: L (full day)
**Depends on**: story-ring-guided-deploy-1 (needs clio's committed JSON fixture + contract shape)
**Blocks**: story-ring-guided-deploy-7, story-ring-guided-deploy-10
**Dependency note**: The mirror + contract test can be built against clio story 1's committed JSON fixture. Live end-to-end verification requires clio story 4 (tool forwarding) to have landed; unit/contract coverage does not.

---

## As a

Ring developer wiring the structured progress path

## I want

a mirrored `ClioStageEvent` record and a `CallToolAsync(…, IProgress<ClioStageEvent>)` overload that registers a raw `RegisterNotificationHandler("notifications/progress", …)`, reads `params._meta.clioStageEvent`, and applies FR-12 tolerance

## So that

the Ring consumes genuinely typed stage events (not the SDK's `_meta`-dropping `IProgress<ProgressNotificationValue>` callback) and raises typed events the pipeline VM can trust

---

## Acceptance Criteria

- [ ] **AC-01** — Given clio's committed JSON fixture (from story 1) copied byte-identically into this repo, when the Ring's contract test deserializes it into the mirrored `ClioStageEvent`, then all fields map correctly and a re-serialize round-trip matches the fixture bytes (cross-repo contract anchor).
- [ ] **AC-02** — Given a `notifications/progress` notification with a populated `_meta.clioStageEvent`, when the raw handler processes it, then it deserializes the envelope and raises a typed `ClioStageEvent` (NOT via the `IProgress<ProgressNotificationValue>` overload, which drops `_meta` — ADR fact 6).
- [ ] **AC-03** — Given an event with an unknown extra field, when consumed, then it is tolerated with no throw (FR-12 / AC-11).
- [ ] **AC-04** — Given a duplicate `sequence` or an out-of-order `sequence` for a given `runId`, when consumed, then the duplicate/out-of-order event is ignored (no double-raise, no crash) — de-dup + drop per `runId` (AC-11).
- [ ] **AC-05** — Given concurrent tool calls, when notifications arrive, then events are correlated strictly by `progressToken`→`runId`; events for an unknown/foreign run are ignored.
- [ ] **AC-06** — Given `schemaVersion` mismatch between the received envelope and the mirror, when consumed, then the mismatch is detectable (version gate exposed) so the Ring can degrade gracefully rather than misparse.
- [ ] **AC-07** — Given the existing read-only workflows, when this overload is added, then the pre-existing `IProgress<string>` overload of `CallToolAsync` is unchanged and still works.
- [ ] **AC-ERR** — Given `_meta` is absent or `clioStageEvent` is malformed, when the handler runs, then it is skipped safely (no throw, no fabricated event).

## Implementation Notes

From ADR D5 + "clio-ring files to create/modify":

- `ClioRing.Ipc/ClioStageEvent.cs` (new) — mirrored envelope record (D2). Commit the JSON fixture copied byte-identically from clio story 1; assert it in a contract test.
- `ClioRing.Ipc/ClioIpcClient.cs` (modify) — add `CallToolAsync(…, IProgress<ClioStageEvent>)` (or an event-based) overload. It registers `RegisterNotificationHandler("notifications/progress", (JsonRpcNotification n, ct) => …)` for the call duration, reads `n.Params._meta.clioStageEvent`, correlates by `progressToken`, applies unknown-field tolerance + `sequence` de-dup/order tolerance per `runId`, raises typed events. Keep the existing `IProgress<string>` overload.
- This project stays non-AOT (JIT-only) — the reflection-heavy MCP SDK + `_meta` deserialize are isolated here per ADR D8/predecessor AOT isolation.

Key files: `ClioRing.Ipc/ClioStageEvent.cs`, `ClioRing.Ipc/ClioIpcClient.cs`
Pattern to follow: the existing `ProgressAdapter : IProgress<ProgressNotificationValue>` (ADR fact 3) is what we are bypassing; use `RegisterNotificationHandler` (present in ModelContextProtocol 1.4.0) instead.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit (`ClioRing.Tests`) | fixture deserialize + byte round-trip vs clio's fixture; `_meta` extraction; unknown-field tolerance; duplicate/out-of-order `sequence` drop per `runId`; `progressToken`→`runId` correlation; malformed `_meta` skipped | `ClioRing.Tests/ClioStageEventAdapterTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]` (repo test-style policy).

## Definition of Done

- [ ] Mirrored `ClioStageEvent` + JSON fixture committed byte-identically to clio story 1's fixture; contract test green
- [ ] Raw `RegisterNotificationHandler` reads `_meta.clioStageEvent`; does NOT use the `_meta`-dropping `IProgress<ProgressNotificationValue>` path
- [ ] Unknown-field tolerance + `sequence` de-dup/out-of-order drop + `progressToken`→`runId` correlation implemented
- [ ] Existing `IProgress<string>` overload unchanged
- [ ] Project stays JIT-only / non-AOT (SDK + `_meta` deserialize isolated in `ClioRing.Ipc`)
- [ ] Unit tests green; AAA + `because` + `[Description]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
