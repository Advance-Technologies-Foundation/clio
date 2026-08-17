# Story 9: Interprocess file gates

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 9
**Status**: ready-for-dev
**Size**: M

## As a
system now running eight clio processes at once

## I want
the shared files on disk to be gated

## So that
separate address spaces do not create a data race where a monitor used to hide one

## Design
- **Separate address spaces do not isolate files** (rule 8). `.clio-pages/{schema}/meta.json` is read-modify-write **with swallowed I/O failures** (`PageBaselineGuard.cs`, `PageFileWriter.cs`): the loser of an interleaved write is lost with no error at all.
- **Correction (2026-08-17), and it changes the urgency rather than the wording.** The earlier claim that
  "today `CwdLock` accidentally serialises this" is **false at all three call sites**. `CwdLock` guards the
  process current directory, and at every site it covers only the anchor-path computation and is released
  before any file touch: `PageBaselineGuard` holds it across `ResolveMetaFilePath` only (the baseline read
  and the whole of `RefreshOrDrop` are outside it), `PageFileWriter` holds it across `ResolveAnchor` only
  (the recursive delete and all three writes are outside it), and `PageSyncTool.WriteVerifiedBodyFile` does
  the same. What actually serialises these paths today is narrower and incidental: the **per-tenant
  execution monitor** (same tenant key only) plus the fact that there is **one process**. So the race is
  already reachable on shipped clio — two tenant keys writing one schema, or a CLI `clio update-page`
  running beside an MCP server in the same workspace — and this story is a **present bug fix that stands on
  its own**, not a precondition sequenced against the worker cohort.
- The ordering constraint is therefore stated by MECHANISM, not by deletion: the serialisation that exists
  is per-tenant and single-process, so it disappears the moment a `.clio-pages` writer runs in a **child
  process**, with or without Stage 10's `CwdLock` removal. **Story 6 AC-06 still encodes the cohort side**:
  no cohort tool writes `.clio-pages` until this gate exists. Whoever later "just removes `CwdLock` at
  Stage 10" must understand they are not removing this guard — the guard was never there.
- Browser-session cache: **explicitly documented last-write-wins, plus an atomic write** — no lock. The
  cache key hashes login/password/clientId, so two writers that agree on the key agree on the credentials
  and their sessions are interchangeable; there is no lost update to arbitrate. The real hazard is a torn
  read by Playwright loading `storageState`, which the atomic replacement removes.
- `appsettings.json`: **already safe, no implementation work** — cross-process lock, atomic replace,
  read-share on read, optimistic concurrency check and symlink refusal all predate this story
  (`ConfigurationOptions.cs`). AC-04 is a regression test that pins them.
- DbHub needs nothing — already cross-process safe (`.clio.lock`, `FileShare.None`). The gate added here is
  modelled on it deliberately: exclusion via an exclusive OS handle, never via the presence of a lock file,
  because the ADR bounds a worker by **killing** it and the OS releases handles on death. A presence-based
  lock would strand a schema permanently after the first budget kill.

## Acceptance Criteria
- [ ] AC-01 — Two concurrent workers writing the same `meta.json` produce a consistent file; neither write is
  silently lost (TC-E-901). **Implemented and covered, but NOT yet evidenced:** TC-E-901 needs a reachable
  Creatio stand and is skipped without one, so it has never executed. It closes only on a green
  `Team_Atf_ClioMcpE2eTests` run — GitHub CI does not run `clio.mcp.e2e`.
- [x] AC-02 — I/O failures in the baseline/meta path **surface as a response warning** instead of being
  swallowed (TC-U-901). **Amended:** "surface" cannot mean "throw". `PageBaselineStore` and
  `PageBaselineGuard` both document that a failed refresh must never fail a save that already succeeded, so
  throwing would fail `update-page` AFTER the server write landed — a successful write reported as a
  failure, strictly worse than the silent loss. The diagnostic travels on the existing warning channel
  (`response.Warnings` / `AppendCommandWarnings`). The discrimination is part of the AC: a **missing**
  meta.json stays silent (the legitimate "no baseline captured" state), while a corrupt read warns that
  conflict detection is disarmed and a failed write warns that the refresh was lost.
- [x] AC-03 — Browser-session cache behaves per its documented policy under concurrency (TC-E-902).
- [x] AC-04 — `appsettings.json` concurrent read during a `reg-web-app` write yields a whole, valid catalog.
- [x] AC-05 — **The gate is never held across a Creatio round trip.** Each acquisition wraps one disk
  touch: the baseline read, the refresh/delete read-modify-write, get-page's prepare-and-write, and
  sync-pages' verified body + fresh-meta writes. Wrapping `TryArm` .. `RefreshOrDrop` would hold a
  cross-process lock across the network save, rebuilding the head-of-line stall this ADR exists to delete —
  in a place no monitor can bound — and a budget kill mid-span would strand the lock. Cross-call
  consistency stays the checksum CAS's job.
- [x] AC-06 — Every `meta.json` write is **atomic** (temp file + replace), so a reader outside the gate — an
  older clio, a foreign tool — cannot observe a truncated prefix even with no contention at all.

## Tests
E2E TC-E-901, TC-E-902 and the AC-04 regression (`clio.mcp.e2e/ClioPagesConcurrencyE2ETests.cs`); unit
TC-U-901 (`clio.tests/Command/McpServer/PageBaselineStoreTests.cs`) plus the gate's own coverage
(`clio.tests/Command/McpServer/InterprocessFileGateTests.cs`) and the per-disk-touch / not-across-the-
network assertions in `PageBaselineGuardTests` and `PageFileWriterTests`.

## Implementation notes
- The **fourth** `meta.json` read-modify-write is `PageSyncTool.WriteFreshMetaAfterVerify`, which the ADR,
  the cross-call-state inventory and the original story text all omit. A gate installed only in
  `PageBaselineGuard` and `PageFileWriter` would leave it racing, so it is gated as one acquisition
  spanning its read, merge and write.
- The sentinel is `{anchor}/.clio-pages/.locks/{schema}.lock` — deliberately OUTSIDE the per-schema
  directory, because `PageFileWriter` deletes that subtree recursively on every get-page. A sentinel inside
  it would be unlinked from under its holder on Unix and would make the delete fail against the open
  exclusive handle on Windows, turning a working get-page into an error.
- `IInterprocessFileGate` takes `IFileSystem` by constructor injection, diverging from the three static
  in-repo lock helpers. That divergence is the reason it is unit-testable at all — DbHub's equivalent gate
  is not.
