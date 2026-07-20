# Story 6 — CLI parity for conflict detection

**Feature**: detect-external-schema-changes
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317) (reopened 2026-06-16)
**Status**: in-progress
**Depends on**: story-detect-external-schema-changes-3
**FR**: FR-02, FR-09, FR-11

## Context

ENG-91317 shipped conflict detection but was reopened: *"Claude works correct, Codex
continue rewrite changes."* The ADR (Option B) deliberately left baseline I/O in the MCP
layer and assumed CLI users pass `--expected-checksum` by hand. AI agents driving the
**CLI** (`clio update-page --body-file …\.clio-pages\{schema}\body.js`) never pass it, so
`PageUpdateCommand.TryUpdatePage` skipped the check and silently overwrote external edits —
even though the MCP `get-page` had already written the baseline to
`.clio-pages/{schema}/meta.json`.

## Goal

Give the CLI verbs the same automatic, on-disk conflict protection the MCP tools have,
without duplicating logic.

## Acceptance Criteria

- [x] CLI `update-page` auto-discovers the `.clio-pages/{schema}/meta.json` baseline (same
  environment) and blocks the save with a structured conflict on checksum mismatch — no
  `--expected-checksum` required. An explicit `--expected-checksum` still wins.
- [x] CLI `update-page` refreshes / drops the on-disk baseline after a successful save, so
  consecutive CLI updates do not false-conflict.
- [x] CLI `get-page` persists `body.js` / `bundle.json` / `meta.json` (incl. baseline) into
  `.clio-pages/{schema}/`, with a new kebab-case `--output-directory` option.
- [x] Baseline arm/refresh and get-page file output are shared DI services
  (`IPageBaselineGuard`, `IPageFileWriter`) consumed by the CLI verbs **and** the three MCP
  tools — no duplicated logic; MCP behavior unchanged.
- [x] No on-disk baseline → no extra SysSchema query (regression-safe for plain CLI users).

## Definition of Done

- [x] `IPageBaselineGuard` + `IPageFileWriter` created, registered in `BindingsModule`.
- [x] `PageBaselineStore` / `PageOutputDirectoryResolver` moved to `Clio.Command` namespace.
- [x] `PageUpdateTool` / `PageSyncTool` / `PageGetTool` refactored onto the shared services.
- [x] Unit tests: `PageBaselineGuardTests`, `PageFileWriterTests`,
  `PageUpdateCommandBaselineTests`, `PageGetCommandFileWriterTests`; existing MCP fixtures updated.
- [x] Docs updated: `help/en/update-page.txt` + `get-page.txt`,
  `docs/commands/update-page.md` + `get-page.md`.
- [x] ADR addendum recorded (Option B's filesystem-free-CLI assumption reversed).
- [x] Targeted + full unit suite green (full Unit: 4026 passed / 0 failed).
- [x] Live verification on a dev stand (saetest0619 / ts1-core-dev04:88/sae_m_seeenu_15597755_0619): CLI `get-page` wrote `.clio-pages/{schema}/meta.json` with the baseline; CLI `update-page` returned `conflict: true` (`checksum-mismatch`) on a stale baseline and blocked the save; `--force` overwrote and refreshed the on-disk baseline.
