# Story 4: sync-pages — per-page conflict surfacing, per-page force, meta.json refresh

**Feature**: detect-external-schema-changes
**FR coverage**: FR-03, FR-05 (per-page force), FR-08 (per-page guard), FR-09, FR-13
**PRD**: [prd-detect-external-schema-changes.md](../prd/prd-detect-external-schema-changes.md)
**ADR**: [adr-detect-external-schema-changes.md](../adr/adr-detect-external-schema-changes.md)
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-detect-external-schema-changes-3

---

## As a

QA engineer (and LLM agent running batch syncs)

## I want

`sync-pages` to run the same pre-save conflict check per page — reporting per-page conflicts without aborting the batch, honoring per-page `force`, and rewriting a full fresh `meta.json` on the verify path

## So that

conflict detection behaves identically across `update-page` and `sync-pages` (one contract), and the existing stale-baseline gap in the verify path is closed

---

## Acceptance Criteria

- [ ] **AC-01** — Given a batch with one stale page (baseline checksum differs on server) and one fresh page, when `sync-pages` runs, then the stale page's result carries `conflict=true` + `conflict-details` (kebab-case), the fresh page is saved successfully, and the batch does NOT abort (FR-03, PRD AC-07).
- [ ] **AC-02** — Given a stale page with per-page `force=true` in its `PageSyncPageInput`, when the batch runs, then that page is saved (deliberate overwrite) (FR-05).
- [ ] **AC-03** — Given a page with no `meta.json` or a legacy one without `baseline`, when the batch runs, then no check is performed for that page and behavior is identical to the pre-feature flow (FR-07, PRD AC-05).
- [ ] **AC-04** — Given a page baseline captured against a different environment than `args.EnvironmentName`, when the batch runs, then the check is silently skipped for that page (FR-08, PRD AC-06).
- [ ] **AC-05** — Given verify mode (`verify=true`), when a page is verified, then a FULL fresh `meta.json` (typed `PageMetaFileModel` incl. baseline) is written next to the verified `body.js` (FR-13).
- [ ] **AC-06** — Given non-verify mode and a successful save with baseline in play, when post-save `NewChecksum` is available, then the page's local baseline is refreshed via `RefreshExistingBaseline`; when `NewChecksum` is null, then `DeleteBaseline` runs for that page (FR-09).

## Implementation Notes

From ADR validation finding 4 and the files-to-modify table:

- `clio/Command/McpServer/Tools/PageSyncTool.cs`:
  - Add per-page `force` (bool?) to `PageSyncPageInput`.
  - In `SyncSinglePage`: per-page `PageBaselineStore.TryReadBaseline` + env guard. **Important nuance**: `SyncSinglePage` builds `PageUpdateOptions` WITHOUT `Environment` (line 453) — compare baseline identity against `args.EnvironmentName` at the MCP layer, NOT against options.
  - Populate `ExpectedChecksum`/`ExpectedSchemaUId`/`ExpectedSchemaAbsent`/`Force` on the per-page `PageUpdateOptions`; the check itself runs in the Story 3 chokepoint (`PageUpdateCommand.TryCheckForExternalModification`) — do NOT reimplement it here (FR-11).
  - Conflict surfaces as per-page `conflict`/`conflict-details` on `PageSyncPageResult` (kebab-case envelope, Story 1 models); other pages continue.
  - Verify path (lines 470–496) currently rewrites only `body.js` — extend to write a full fresh typed `meta.json` beside it.
  - Update tool `[Description]` for per-page force + conflict semantics.

Key file: `clio/Command/McpServer/Tools/PageSyncTool.cs`
Pattern to follow: `PageUpdateTool` baseline plumbing from Story 3

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (substitutes + MockFileSystem) | Per-page conflict does not abort batch (AC-07 of PRD); per-page force saves; env-mismatch skip; verify=true rewrites full meta.json; verify=false refreshes baseline / deletes on null `NewChecksum` | `clio.tests/Command/McpServer/PageSyncToolTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004); no new `CLIO*` warnings
- [ ] MCP arg names kebab-case (`force` on page input items); no new CLI verbs/flags
- [ ] Conflict logic NOT duplicated — single chokepoint in `PageUpdateCommand` (FR-11)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`; AAA + `because` + `[Description]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
