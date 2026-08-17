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
- Today `CwdLock` accidentally serialises this — but only *within one process*. **Ordering constraint (cross-call state §5): the gate must land before any `.clio-pages` writer joins the worker cohort**, which is Stage 6 (`get-page`), not Stage 10. A child escapes `CwdLock` by being a different process; it does not wait for the deletion. **Story 6 AC-06 encodes this**: no cohort tool writes `.clio-pages` until this gate exists, so either the gate ships with the first cohort or `get-page` leaves it. Stage 10's `CwdLock` removal is a second, later checkpoint on the same gate.
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
