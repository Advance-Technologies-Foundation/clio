# Story 5: Docs, MCP guidance artifacts, and E2E coverage

**Feature**: detect-external-schema-changes
**FR coverage**: FR-14; E2E verification of A-01/OQ-02 and PRD AC-01/AC-02/AC-07/AC-11
**PRD**: [prd-detect-external-schema-changes.md](../prd/prd-detect-external-schema-changes.md)
**ADR**: [adr-detect-external-schema-changes.md](../adr/adr-detect-external-schema-changes.md)
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: story-detect-external-schema-changes-3, story-detect-external-schema-changes-4

---

## As a

LLM agent (MCP client) and clio CLI user

## I want

the docs, MCP tool descriptions, page-flow guidance resource, and prompts to explain the baseline lifecycle, conflict contract, and force semantics — plus E2E tests reproducing the ticket scenario

## So that

I can recover from a conflict (re-fetch, rebase, retry; force only after user confirmation) without guessing, and the team has end-to-end proof that designer saves bump `SysSchema.Checksum`

---

## Acceptance Criteria

- [ ] **AC-01** — Given the docs set, when reviewed, then `clio/help/en/update-page.txt` and `get-page.txt` describe `--expected-checksum`/`--force` and conflict semantics; `clio/docs/commands/update-page.md`, `sync-pages.md`, `get-page.md` cover baseline lifecycle, conflict contract, and force semantics; `clio/Commands.md` lists the new `update-page` options (FR-14).
- [ ] **AC-02** — Given the MCP surface, when reviewed, then `PageUpdateTool`/`PageSyncTool`/`PageGetTool` `[Description]` texts state the conflict contract and "force only after explicit user confirmation"; `PageModificationGuidanceResource` has a new section: conflict → reload via get-page → rebase → retry → force after confirmation; `clio/Command/McpServer/Prompts/` page-flow texts and `docs/McpCapabilityMap.md` are reviewed and updated or explicitly marked "reviewed, no update required".
- [ ] **AC-03** — Given a live stand, when the E2E ticket scenario runs (get-page → out-of-band schema change in designer → update-page), then the write is blocked with `conflict=true` / `reason=checksum-mismatch` — this is the FIRST scenario to run because it verifies A-01/OQ-02 (designer saves bump `SysSchema.Checksum`).
- [ ] **AC-04** — Given the same conflict state, when `update-page` runs with `force=true`, then the overwrite succeeds (PRD AC-02).
- [ ] **AC-05** — Given a flow with no external modifications (get-page → update-page → update-page), when it runs E2E, then every write succeeds with no conflict (regression, PRD AC-11).
- [ ] **AC-06** — Given a `sync-pages` batch with one stale and one fresh page, when it runs E2E, then the stale page reports a per-page conflict and the fresh page is saved (PRD AC-07).

## Implementation Notes

From the ADR "Docs / MCP artifacts" section:

- Docs: `clio/help/en/update-page.txt`, `clio/help/en/get-page.txt`, `clio/docs/commands/update-page.md`, `sync-pages.md`, `get-page.md`, `clio/Commands.md` (and `Wiki/WikiAnchors.txt` if applicable). Use the `document-command` skill.
- MCP: tool `[Description]` texts (may have landed partially in Stories 3/4 — verify completeness here), `clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs` new conflict-recovery section, prompts review, `docs/McpCapabilityMap.md`. Use the `create-mcp-tool` / `test-mcp-tool` skills as appropriate.
- E2E: new scenarios in `clio.mcp.e2e/` covering AC-03..AC-06.
- **CI caveat**: `clio.mcp.e2e` is NOT in CI — manual execution against a live stand only. If no stand is available, flag the E2E scenarios as unverified in the PR (do not silently skip).
- Recovery wording must match the single error-text constant from Story 3 (no divergent guidance).

Key file: `clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs`, `clio.mcp.e2e/`
Pattern to follow: existing page-flow guidance sections; existing e2e scenario structure in `clio.mcp.e2e/`

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| E2E `[Category("E2E")]` | Ticket scenario (conflict), force overwrite, no-baseline regression, batch stale+fresh — manual only, not in CI | `clio.mcp.e2e/` |
| Unit `[Category("Unit")]` | Guidance resource content assertions if the existing resource fixture pattern requires them | `clio.tests/Command/McpServer/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]` for any unit tests.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004); no new `CLIO*` warnings
- [ ] All documented flags shown in kebab-case
- [ ] Any new unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`; E2E tests use `[Category("E2E")]`
- [ ] Docs and MCP artifacts either updated or explicitly marked "reviewed, no update required" in the PR description
- [ ] E2E run status (verified on stand / flagged unverified) recorded in the PR description; OQ-02/A-01 outcome recorded
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes (incl. OQ-02/A-01 verification outcome):
