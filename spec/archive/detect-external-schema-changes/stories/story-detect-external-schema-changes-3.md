# Story 3: Conflict gate — PageUpdateCommand chokepoint + PageUpdateTool plumbing

**Feature**: detect-external-schema-changes
**FR coverage**: FR-02, FR-04, FR-05, FR-06, FR-08 (tool-side guard), FR-09, FR-11, FR-12
**PRD**: [prd-detect-external-schema-changes.md](../prd/prd-detect-external-schema-changes.md)
**ADR**: [adr-detect-external-schema-changes.md](../adr/adr-detect-external-schema-changes.md)
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: story-detect-external-schema-changes-1, story-detect-external-schema-changes-2

---

## As a

LLM agent (MCP client) and clio CLI user

## I want

`update-page` to block the write with a structured conflict when the editable schema changed externally since my baseline, with `force` to deliberately overwrite, and the baseline refreshed after a successful save

## So that

I never silently revert the user's designer edits, and I get machine-readable recovery instructions when a conflict occurs

---

## Acceptance Criteria

- [ ] **AC-01** — Given `ExpectedChecksum` set and the server checksum differs, when `update-page` runs without force, then the schema is NOT saved and the response has `Success=false`, `Conflict=true`, `ConflictDetails.Reason="checksum-mismatch"` with expected/actual checksums and the agent-guiding error text (PRD AC-01).
- [ ] **AC-02** — Given the same conflict state, when `update-page` runs with `Force=true`, then the schema IS saved and the response carries fresh `NewChecksum` (PRD AC-02).
- [ ] **AC-03** — Given `ExpectedSchemaAbsent=true` and the editable schema now exists (`!IsCreateReplacing`), when the write runs, then it is blocked with `Reason="schema-created-externally"` (PRD AC-03); given the schema is still absent (`IsCreateReplacing`), then the write proceeds.
- [ ] **AC-04** — Given `ExpectedChecksum` set and the editable schema no longer exists (`IsCreateReplacing` or metadata row absent), when the write runs, then it is blocked with `Reason="schema-deleted-externally"` (PRD AC-04).
- [ ] **AC-05** — Given `ExpectedSchemaUId` set and it differs from `context.EditableSchemaUId` (ordinal-ignore-case), when the write runs, then it is blocked with `Reason="schema-uid-mismatch"`.
- [ ] **AC-06** — Given no baseline options set (no `ExpectedChecksum`/`ExpectedSchemaUId`/`ExpectedSchemaAbsent`), when the write runs, then NO check and NO extra metadata query is performed — behavior identical to pre-feature flow (FR-07, PRD AC-05/AC-11, SM-03 counter).
- [ ] **AC-07** — Given dry-run mode with a stale baseline, when `update-page` runs, then the conflict is reported and nothing is written (FR-12, PRD AC-10).
- [ ] **AC-08** — Given a successful non-dry-run save with baseline options in play, when the post-save metadata query succeeds, then `NewChecksum`/`NewModifiedOn`/`SavedSchemaUId` are populated and the MCP tool refreshes the local baseline; when it fails, then the fields stay null, the save still reports success, and the MCP tool deletes the baseline block (FR-09, PRD AC-08).
- [ ] **AC-09** — Given a baseline captured against environment A, when the MCP `update-page` call targets environment B, then `PageUpdateTool` does not populate the expected-* options and no check runs (FR-08, PRD AC-06).
- [ ] **AC-ERR** — Given `--expected-checksum` via CLI and the server checksum differs, when the verb runs, then clio prints an error identifying the external-modification conflict (expected vs actual checksum) and exits non-zero (PRD AC-ERR).

## Acceptance Criteria — options surface

- [ ] **AC-10** — `PageUpdateOptions` gains `[Option("expected-checksum")] string ExpectedChecksum` and `[Option("force")] bool Force` (kebab-case, CLIO001); `ExpectedSchemaUId` / `ExpectedSchemaAbsent` are MCP-internal properties WITHOUT `[Option]`.
- [ ] **AC-11** — `PageUpdateArgs` (MCP) gains optional `force` (bool?) and `output-directory` (string?); `PageUpdateTool` `[Description]` documents the conflict contract and "force only after explicit user confirmation".

## Implementation Notes

From ADR validation findings 2, 8, 9 and the decision table:

- `clio/Command/PageUpdateOptions.cs`:
  - New options per AC-10.
  - New `private bool TryCheckForExternalModification(PageUpdateOptions options, EditableSchemaContext context, out PageUpdateResponse response)` invoked in `TryUpdatePage` immediately AFTER `TryResolveContext` (line 124) and BEFORE the `DryRun` short-circuit (line 127). `EditableSchemaContext` already carries `EditableSchemaUId` + `IsCreateReplacing`.
  - Implement the ADR 9-row decision table verbatim (Force → skip; no baseline options → skip; absent-but-exists → created-externally; absent-and-still-absent → proceed; checksum+IsCreateReplacing → deleted-externally; UId mismatch; metadata row absent → deleted-externally; checksum mismatch; otherwise proceed). Metadata via `QuerySysSchemaRowByUId(context.EditableSchemaUId, ("Checksum","Checksum"), ("ModifiedOn","ModifiedOn"))`.
  - Conflict error text — single user-facing constant (exact wording in ADR): "Schema '<name>' was modified outside this session (external modification detected). Do NOT retry with the same body. Re-run get-page … force=true ONLY after the user explicitly confirms…". CLI non-zero exit via existing `Execute` → `TryUpdatePage` false path.
  - Post-save refresh: after `TrySaveSchema` succeeds, non-dry-run, ONLY when a baseline option or `Force` was supplied: best-effort `QuerySysSchemaRowByUId` → `NewChecksum`/`NewModifiedOn`/`SavedSchemaUId`; failure → nulls. Resolve OQ-01 first: if `SaveSchema`/`GetSchema` response already carries the checksum, read it instead; SysSchema query is the committed fallback.
- `clio/Command/McpServer/Tools/PageUpdateTool.cs`:
  - Inject `IFileSystem` (constructor lines 17–23 change → update instantiations in `clio.tests/Command/McpServer/PageToolsTests.cs`).
  - Before execute: `PageBaselineStore.TryReadBaseline` (anchor resolution; sibling meta when `body-file` is inside `.clio-pages/{schema}/`) + `MatchesEnvironment` guard → populate `ExpectedChecksum`/`ExpectedSchemaUId`/`ExpectedSchemaAbsent`/`Force`.
  - After success: `RefreshExistingBaseline` from `NewChecksum`/`NewModifiedOn`/`SavedSchemaUId`; baseline-in-play + Success + null `NewChecksum` → `DeleteBaseline`.

Key file: `clio/Command/PageUpdateOptions.cs`, `clio/Command/McpServer/Tools/PageUpdateTool.cs`
Pattern to follow: existing `TryUpdatePage` flow; environment-aware MCP tool execution per `BaseTool` conventions

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (NSubstitute per-URL stubs on `IApplicationClient.ExecutePostRequest`) | `PageUpdateCommand`: conflict on checksum mismatch / absent-but-exists / deleted / UId mismatch; proceeds with force; skip without baseline options (zero extra queries); dry-run reports conflict; `NewChecksum` populated after save; post-save query failure → nulls + success | `clio.tests/Command/PageUpdateCommandTests.cs` (existing fixture family) |
| Unit `[Category("Unit")]` (MockFileSystem + substitutes) | `PageUpdateTool`: baseline → options mapping; skip on env mismatch; refresh after save; delete on null `NewChecksum`; ctor change in `PageToolsTests.cs` | `clio.tests/Command/McpServer/PageToolsTests.cs` |
| E2E `[Category("E2E")]` (deferred to Story 5) | Ticket scenario coverage | `clio.mcp.e2e/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004); no new `CLIO*` warnings
- [ ] All new CLI flags are kebab-case (`--expected-checksum`, `--force`)
- [ ] No MediatR; logic in `PageUpdateCommand` (`Command<TOptions>`); all HTTP via `IApplicationClient`
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`; AAA + `because` + `[Description]`
- [ ] Existing `PageToolsTests.cs` instantiations updated for the `IFileSystem` ctor change
- [ ] OQ-01 resolution noted in the Dev Agent Record
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes (incl. OQ-01 outcome):
