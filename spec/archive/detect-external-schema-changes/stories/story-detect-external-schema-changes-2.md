# Story 2: Baseline capture — get-page checksum fetch, typed meta.json, PageBaselineStore

**Feature**: detect-external-schema-changes
**FR coverage**: FR-01, FR-07, FR-08 (store-side guard), FR-10
**PRD**: [prd-detect-external-schema-changes.md](../prd/prd-detect-external-schema-changes.md)
**ADR**: [adr-detect-external-schema-changes.md](../adr/adr-detect-external-schema-changes.md)
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: story-detect-external-schema-changes-1

---

## As a

user of an AI no-code agent

## I want

`get-page` to capture a baseline (editable-schema UId, `SysSchema.Checksum`, opaque `ModifiedOn`, environment identity, `editableSchemaExists`) into `.clio-pages/{schema}/meta.json`, with a `PageBaselineStore` that can read/refresh/delete it

## So that

later writes can detect that I edited the page in the designer, instead of working blindly from a stale snapshot

---

## Acceptance Criteria

- [ ] **AC-01** — Given `get-page` resolves an editable schema, when the checksum query succeeds, then `meta.json` contains a `baseline` block with `schemaName`, env identity (`environmentName` OR `environmentUri`, the other null), `editableSchemaExists=true`, `editableSchemaUId`, `checksum`, raw `modifiedOn`, ISO-8601 UTC `capturedAt` (FR-01).
- [ ] **AC-02** — Given `get-page` would create a replacing schema (`willCreateReplacing`), when meta is written, then `baseline.editableSchemaExists=false` and `editableSchemaUId`/`checksum` are null.
- [ ] **AC-03** — Given the checksum query throws, when `get-page` completes, then the page is still returned successfully and `meta.json` is written WITHOUT a `baseline` block (FR-10 / PRD AC-09).
- [ ] **AC-04** — Given a legacy `meta.json` without `baseline`, a missing file, or corrupt JSON, when `PageBaselineStore.TryReadBaseline` runs, then it returns `false` (FR-07).
- [ ] **AC-05** — Given `body-file` resides inside a `.clio-pages/{schema}/` directory, when `TryReadBaseline` runs, then the sibling `meta.json` wins over anchor-resolved discovery.
- [ ] **AC-06** — Given baseline env identity and call env identity, when `MatchesEnvironment` runs, then: name/name ordinal-ignore-case match → true; uri/uri normalized (trailing-slash-insensitive, ignore-case) match → true; cross-mode, both-null, or mismatch → false (FR-08).
- [ ] **AC-07** — Given an existing `meta.json`, when `RefreshExistingBaseline` runs, then only the `baseline` block is rewritten and `fetchedAt`/`page` are preserved; given no `meta.json`, then it no-ops and never creates `.clio-pages` directories.
- [ ] **AC-08** — Given an existing `meta.json` with a baseline, when `DeleteBaseline` runs, then the `baseline` block is removed, `fetchedAt`/`page` are kept, and I/O errors are swallowed (best-effort).

## Implementation Notes

From ADR validation findings 1, 7, 8 and the contract block:

- New file `clio/Command/McpServer/Tools/PageBaselineStore.cs` — `internal static`, `IFileSystem`-parameterised, NO DI registration (deliberate parity with `PageOutputDirectoryResolver.ResolveAnchor`). Signatures exactly per ADR:
  `TryReadBaseline(fs, anchorCwd, homeDir, homeFallbackAnchor, outputDirectory, bodyFile, schemaName, out PageBaselineInfo)`, `MatchesEnvironment(baseline, environmentName, uri)`, `RefreshExistingBaseline(fs, metaFilePath, savedSchemaUId, newChecksum, newModifiedOn)`, `DeleteBaseline(fs, metaFilePath)`.
- `clio/Command/PageGetOptions.cs` `TryGetPage` (~line 147, after `editableSchema` lookup + `willCreateReplacing`): best-effort `QuerySysSchemaRowByUId(editableSchema.UId, ("Checksum","Checksum"), ("ModifiedOn","ModifiedOn"))` inside its OWN local try/catch (the method's catch-all at line 187 is not enough — checksum failure must not fail get-page). Surface result in the `editable` block on `PageGetResponse` (Story 1).
- `clio/Command/McpServer/Tools/PageGetTool.cs` `WriteFilesAndCompact` (lines 66–108): replace the anonymous `{fetchedAt, page}` object with typed `PageMetaFileModel` (legacy names stable via `[JsonPropertyName]`). Baseline data from the `editable` block; env identity from `args.EnvironmentName`/`args.Uri`. Checksum absent → meta written without baseline.
- Note: `PageGetTool` deletes the whole schema directory per fetch (lines 76–79) — the refresh path must tolerate a missing directory (covered by AC-07).
- `ModifiedOn` is opaque — store raw, never parse/compare (PRD A-04).

Key file: `clio/Command/McpServer/Tools/PageBaselineStore.cs`, `clio/Command/PageGetOptions.cs`, `clio/Command/McpServer/Tools/PageGetTool.cs`
Pattern to follow: `PageOutputDirectoryResolver` (static + `IFileSystem` params)

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` (MockFileSystem) | `PageBaselineStore`: read baseline; legacy/missing/corrupt meta → false; sibling meta via `body-file`; env guard name/name, uri/uri, cross-mode, both-null; refresh preserves `fetchedAt`/`page`; refresh no-ops without meta.json; `DeleteBaseline` keeps legacy fields | `clio.tests/Command/McpServer/PageBaselineStoreTests.cs` (new) |
| Unit `[Category("Unit")]` | `PageGetCommand`: checksum query failure → success + nulls in `editable` block | `clio.tests/Command/` existing PageGet fixture |
| Unit `[Category("Unit")]` | `PageGetTool`: meta.json carries baseline; `editableSchemaExists=false` when `willCreateReplacing`; checksum failure → meta without baseline + get-page succeeds | `clio.tests/Command/McpServer/PageToolsTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004); no new `CLIO*` warnings
- [ ] No new CLI flags in this story; `meta.json` extension is additive (legacy files stay valid)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`; AAA + `because` + `[Description]`
- [ ] `meta.json` keeps exact legacy property names `fetchedAt`/`page` (explicit `[JsonPropertyName]`)
- [ ] `RefreshExistingBaseline`/`DeleteBaseline` never create `.clio-pages` directories
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
