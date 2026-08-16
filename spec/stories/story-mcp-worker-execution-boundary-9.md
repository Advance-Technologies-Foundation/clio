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
- Today `CwdLock` accidentally serialises this. **Ordering constraint (cross-call state §5): the file gate must land before `CwdLock` is removed at Stage 10** — removing it first converts a correct guard into an invisible race.
- Browser-session cache: file lock, or an explicitly documented last-write-wins.
- `appsettings.json`: read-share on read, atomic replace on write.
- DbHub needs nothing — already cross-process safe (`.clio.lock`, `FileShare.None`).

## Acceptance Criteria
- [ ] AC-01 — Two concurrent workers writing the same `meta.json` produce a consistent file; neither write is silently lost (TC-E-901).
- [ ] AC-02 — I/O failures in the baseline/meta path **surface** instead of being swallowed (TC-U-901).
- [ ] AC-03 — Browser-session cache behaves per its documented policy under concurrency (TC-E-902).
- [ ] AC-04 — `appsettings.json` concurrent read during a `reg-web-app` write yields a whole, valid catalog.

## Tests
E2E TC-E-901, TC-E-902; unit TC-U-901.
