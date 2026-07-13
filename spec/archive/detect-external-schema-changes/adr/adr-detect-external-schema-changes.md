# ADR: Detect External Schema Changes and Reload Before Applying Updates

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-detect-external-schema-changes.md](../prd/prd-detect-external-schema-changes.md)
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317)
**Created**: 2026-06-12
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

When a Freedom UI page is modified outside the agent session (e.g. a component is deleted in the Creatio designer), `update-page` / `sync-pages` keep writing bodies built from the agent's stale local snapshot and silently revert the user's manual edits — replace mode overwrites the server body wholesale, and append mode's `PageBodyMerger` re-introduces deleted components. Creatio maintains a change signal (`SysSchema.Checksum`, precedent reader: `SaveSettingsToManifestCommand`), but no page tool reads it: `PageGetTool.WriteFilesAndCompact` writes only an anonymous `{fetchedAt, page}` object to `.clio-pages/{schema}/meta.json`, so there is no baseline to compare against. A design decision is needed on where the conflict check lives, how the baseline is captured/discovered/refreshed, and what the conflict contract looks like for the LLM agent above clio. The technical design was approved up front (plan referenced in ENG-91317 delivery notes); this ADR formalizes it and pins it to the verified code.

## Decision

`get-page` captures a typed `baseline` block (editable-schema UId, `SysSchema.Checksum`, opaque `ModifiedOn`, environment identity, `editableSchemaExists`) into `meta.json`; the conflict check is implemented once, in `PageUpdateCommand.TryUpdatePage` (single chokepoint for the MCP `update-page` and `sync-pages` tools and the CLI verb), driven by new `--expected-checksum` / `--force` options plus MCP-internal expected-UId/absent fields, and returns a structured `Conflict`/`ConflictDetails` response with agent-guiding recovery text; a new static `PageBaselineStore` in the MCP layer owns baseline discovery, refresh, env-identity guard, and fail-toward-no-check semantics.

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Check in each MCP tool (`PageUpdateTool`, `PageSyncTool`) separately | No command-layer changes | Two divergent implementations; CLI verb gets no protection; QA must verify N contracts (violates FR-11) | Rejected: duplicated logic, no CLI coverage |
| B: Check in `PageUpdateCommand` after `TryResolveContext`; baseline I/O in MCP-layer `PageBaselineStore` | One implementation for MCP tools + CLI; command stays filesystem-free (CLI users pass `--expected-checksum` explicitly); reuses `EditableSchemaContext` (`EditableSchemaUId`, `IsCreateReplacing`) already resolved on the write path | Command API grows MCP-internal options without `[Option]` | **Chosen** |
| C: Server-side optimistic locking (pass expected checksum to `SaveSchema`) | Closes the TOCTOU window | Requires platform/cliogate changes on all supported Creatio versions; out of clio's control | Rejected: out of scope; TOCTOU window accepted (PRD A-02) |
| D: Compare `ModifiedOn` instead of / in addition to `Checksum` | No extra column semantics | DataService date format is version-unstable → false conflicts (PRD A-04) | Rejected: `ModifiedOn` carried as opaque informational data only |
| E: Baseline the whole hierarchy (parents included) | Detects parent-schema edits | Agent writes only the own body; parent changes would be false positives (PRD A-03) | Rejected: editable (own) schema only |

## Code Validation Findings (verified against the worktree)

All mechanics in the approved design were confirmed against the code; precise integration points and the discrepancies/nuances the implementer must respect:

1. `clio/Command/McpServer/Tools/PageGetTool.cs` `WriteFilesAndCompact` (lines 66–108) writes `meta.json` as an **anonymous object** `{fetchedAt, page}` via `System.Text.Json` — must be replaced by a typed `PageMetaFileModel` with explicit `[JsonPropertyName]` to keep `fetchedAt`/`page` names stable for legacy readers. It also **deletes the whole schema directory** on every fetch (lines 76–79), so the post-save baseline refresh in the update path must tolerate a missing directory and must NOT create `.clio-pages` when absent.
2. `clio/Command/PageUpdateOptions.cs` `PageUpdateCommand.TryUpdatePage` (lines 116–140): the dry-run short-circuit (`if (options.DryRun) … return true`, line 127) sits **after** `TryResolveContext` (line 124) — the conflict check slots between them, satisfying FR-12 (dry-run reports conflicts) with no flow restructuring. `EditableSchemaContext` already carries `EditableSchemaUId` and `IsCreateReplacing` — exactly the inputs the check needs.
3. `clio/Command/PageSchemaMetadataHelper.cs` has `QuerySysSchemaRow` filtered by `Name + ManagerName` only (lines 190–235); a UId-filtered variant (`QuerySysSchemaRowByUId`) does not exist and must be added (dataValueType 0/Guid for the `UId` filter, mirroring `BuildEqFilter("SysPackage.UId", 0, …)` usage at line 101).
4. `clio/Command/McpServer/Tools/PageSyncTool.cs` `SyncSinglePage` verify path (lines 470–496) rewrites **only `body.js`**, never `meta.json` — confirmed stale-baseline gap; FR-13 fixes it by writing a full fresh `meta.json` beside the verified body. Note `SyncSinglePage` builds `PageUpdateOptions` **without** `Environment` (line 453); the env-identity guard must therefore compare against `args.EnvironmentName` at the MCP layer, not against options.
5. `clio/CreatioModel/SysSchema.cs` exposes `Checksum` (string) + `ModifiedOn`; `SaveSettingsToManifestCommand` (lines 149, 172) is the in-repo precedent for treating `Checksum` as the change signal.
6. **JSON naming discrepancy to encode in the contract**: `PageUpdateResponse` (`clio/Command/PageModels.cs` line 627) serializes camelCase (`schemaName`, `bodyLength`, …) while `PageSyncPageResult` (PageSyncTool.cs line 786) serializes kebab-case (`schema-name`, `body-length`, …). New fields must follow each envelope's existing convention: `conflict`/`conflictDetails`/`newChecksum`/`newModifiedOn`/`savedSchemaUId` on `PageUpdateResponse`; `conflict`/`conflict-details` on `PageSyncPageResult`.
7. `clio/Command/PageGetOptions.cs` `PageGetCommand.TryGetPage` editable resolution confirmed at lines 138–147 (`editableSchema` lookup + `willCreateReplacing`) — the natural hook for the best-effort checksum query (FR-10: wrapped so failure degrades to "no baseline", never fails get-page; the whole method already has a catch-all at line 187, so the checksum query needs its own local try/catch).
8. `PageOutputDirectoryResolver.ResolveAnchor` (static, `IFileSystem`-parameterised, no DI) is the established pattern `PageBaselineStore` mirrors; `PageUpdateTool` currently does **not** inject `IFileSystem` (constructor at PageUpdateTool.cs lines 17–23) — adding it changes the constructor signature, which breaks instantiation in `clio.tests/Command/McpServer/PageToolsTests.cs` (expected; update those tests).
9. `PageUpdateArgs` already has `output-directory`? **No** — unlike `PageGetArgs`/`PageSyncArgs`, `PageUpdateArgs` lacks `output-directory`; it must be added for baseline discovery (PRD A-06), alongside `force`.

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/McpServer/Tools/PageBaselineStore.cs` | `internal static` baseline store: `TryReadBaseline`, `WriteBaseline`, `RefreshExistingBaseline`, `DeleteBaseline`, env-identity guard. Static + `IFileSystem` parameters (mirrors `PageOutputDirectoryResolver`), no DI registration |
| `clio.tests/Command/McpServer/PageBaselineStoreTests.cs` | Unit tests (MockFileSystem) for discovery, legacy meta, env guard, refresh, delete-on-refresh-failure |
| `clio/help/en/update-page.txt` update + `clio/docs/commands/update-page.md` etc. | See docs section below |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/PageSchemaMetadataHelper.cs` | Add `QuerySysSchemaRowByUId(IApplicationClient, IServiceUrlBuilder, string schemaUId, params (string alias, string path)[] columns)` — same SelectQuery shape as `QuerySysSchemaRow` but filtered on `UId` (dataValueType 0) + `ManagerName = ClientUnitSchemaManager`; returns `(JToken row, string error)`; row absent → `(null, "Schema '<uid>' not found")` |
| `clio/Command/PageModels.cs` | Add `PageConflictDetails`, `PageMetaFileModel`, `PageBaselineInfo` records; extend `PageUpdateResponse` (`Conflict`, `ConflictDetails`, `NewChecksum`, `NewModifiedOn`, `SavedSchemaUId` — camelCase JSON, null-suppressed), `PageGetResponse` (optional `editable` block with checksum info), `PageSyncPageResult` (`conflict`, `conflict-details` — kebab JSON, null-suppressed) |
| `clio/Command/PageGetOptions.cs` | In `TryGetPage` (after editable resolution, ~line 147): best-effort `QuerySysSchemaRowByUId(editableSchema.UId, ("Checksum","Checksum"), ("ModifiedOn","ModifiedOn"))` in a local try/catch; surface result in the response `editable` block; failure → nulls (FR-10) |
| `clio/Command/PageUpdateOptions.cs` | New `[Option("expected-checksum")]` `string ExpectedChecksum`, `[Option("force")]` `bool Force`; MCP-internal (no `[Option]`) `string ExpectedSchemaUId`, `bool ExpectedSchemaAbsent`. New `TryCheckForExternalModification` invoked in `TryUpdatePage` immediately after `TryResolveContext` (before the `DryRun` branch). Post-save (after `TrySaveSchema`, non-dry-run, only when a baseline option or `Force` was supplied): best-effort `QuerySysSchemaRowByUId` for fresh `Checksum`/`ModifiedOn` → `NewChecksum`/`NewModifiedOn`/`SavedSchemaUId`; on query failure leave them null (MCP layer then deletes the baseline) |
| `clio/Command/McpServer/Tools/PageGetTool.cs` | `WriteFilesAndCompact` writes typed `PageMetaFileModel` incl. `baseline` (env identity from `args.EnvironmentName`/`args.Uri`); baseline data taken from the new `editable` block on `PageGetResponse`; checksum-absent → meta written **without** baseline block (FR-10/AC-09) |
| `clio/Command/McpServer/Tools/PageUpdateTool.cs` | Inject `IFileSystem`; new args `force` (bool?) and `output-directory` (string?) on `PageUpdateArgs`; before execute: `PageBaselineStore.TryReadBaseline` (anchor resolution; sibling `meta.json` when `body-file` lives inside `.clio-pages/{schema}/`) + env guard → populate `ExpectedChecksum`/`ExpectedSchemaUId`/`ExpectedSchemaAbsent`/`Force`; after success: `RefreshExistingBaseline` from `NewChecksum`/`NewModifiedOn`/`SavedSchemaUId`, or `DeleteBaseline` when post-save metadata is null; update `[Description]` with conflict + force semantics |
| `clio/Command/McpServer/Tools/PageSyncTool.cs` | Per-page `force` (bool?) on `PageSyncPageInput`; baseline discovery in `SyncSinglePage` (per page, env guard vs `args.EnvironmentName`); conflict surfaces as per-page `conflict`/`conflict-details` without aborting the batch (FR-03/AC-07); verify path writes a **full fresh `meta.json`** next to the verified `body.js` (FR-13); non-verify path refreshes baseline from `NewChecksum` (or deletes on null) |
| `clio.tests/Command/McpServer/PageToolsTests.cs` | `PageUpdateTool` constructor gains `IFileSystem` — update instantiations |
| `clio.tests/...` (existing Page command/tool fixtures) | New unit tests per Test strategy below |
| Docs + MCP resources/prompts | See dedicated sections below |

### Key interfaces / contracts

```csharp
// clio/Command/PageModels.cs — conflict reason codes (string constants; serialized verbatim)
public static class PageConflictReasons {
    public const string ChecksumMismatch        = "checksum-mismatch";
    public const string SchemaCreatedExternally = "schema-created-externally";
    public const string SchemaDeletedExternally = "schema-deleted-externally";
    public const string SchemaUIdMismatch       = "schema-uid-mismatch";
}

public sealed class PageConflictDetails {
    [JsonPropertyName("reason")]            public string Reason { get; init; }            // one of PageConflictReasons
    [JsonPropertyName("expectedChecksum")]  public string ExpectedChecksum { get; init; }
    [JsonPropertyName("actualChecksum")]    public string ActualChecksum { get; init; }
    [JsonPropertyName("expectedSchemaUId")] public string ExpectedSchemaUId { get; init; }
    [JsonPropertyName("actualSchemaUId")]   public string ActualSchemaUId { get; init; }
    [JsonPropertyName("modifiedOn")]        public string ModifiedOn { get; init; }        // opaque, informational only
}

// On PageUpdateResponse (camelCase envelope), all null/false-suppressed:
public bool Conflict { get; set; }                       // "conflict"
public PageConflictDetails ConflictDetails { get; set; } // "conflictDetails"
public string NewChecksum { get; set; }                  // "newChecksum"  — post-save, best-effort
public string NewModifiedOn { get; set; }                // "newModifiedOn"
public string SavedSchemaUId { get; set; }               // "savedSchemaUId"

// On PageSyncPageResult (kebab-case envelope):
//   "conflict": bool, "conflict-details": PageConflictDetails

// meta.json file model (System.Text.Json, explicit names — keeps legacy "fetchedAt"/"page")
public sealed class PageMetaFileModel {
    [JsonPropertyName("fetchedAt")] public string FetchedAt { get; init; }
    [JsonPropertyName("page")]      public PageMetadataInfo Page { get; init; }
    [JsonPropertyName("baseline")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageBaselineInfo Baseline { get; init; }      // absent on legacy files and on checksum-capture failure
}

public sealed class PageBaselineInfo {
    [JsonPropertyName("schemaName")]           public string SchemaName { get; init; }
    [JsonPropertyName("environmentName")]      public string EnvironmentName { get; init; }   // null when call used direct uri
    [JsonPropertyName("environmentUri")]       public string EnvironmentUri { get; init; }    // null when call used environment-name
    [JsonPropertyName("editableSchemaExists")] public bool EditableSchemaExists { get; init; }
    [JsonPropertyName("editableSchemaUId")]    public string EditableSchemaUId { get; init; } // null when not exists
    [JsonPropertyName("checksum")]             public string Checksum { get; init; }          // null when not exists
    [JsonPropertyName("modifiedOn")]           public string ModifiedOn { get; init; }        // raw/opaque
    [JsonPropertyName("capturedAt")]           public string CapturedAt { get; init; }        // ISO-8601 UTC
}

// clio/Command/McpServer/Tools/PageBaselineStore.cs — static, IFileSystem-parameterised, no DI
internal static class PageBaselineStore {
    // Discovery: PageOutputDirectoryResolver.ResolveAnchor(...) + ".clio-pages/{schema}/meta.json";
    // when bodyFile is non-null and resides inside a ".clio-pages/{schema}/" directory, the sibling
    // meta.json wins. Missing file / legacy meta (no "baseline") / unparseable JSON → returns false.
    internal static bool TryReadBaseline(IFileSystem fs, string anchorCwd, string homeDir,
        string homeFallbackAnchor, string outputDirectory, string bodyFile,
        string schemaName, out PageBaselineInfo baseline);

    // Env-identity guard (FR-08): when the baseline carries environmentName and the write call has an
    // environment-name → ordinal-ignore-case name comparison; when the baseline carries environmentUri
    // and the call has a uri → normalized (trailing-slash-insensitive, ignore-case) URI comparison;
    // any other combination (cross-mode, both null, mismatch) → NOT a match → check skipped.
    internal static bool MatchesEnvironment(PageBaselineInfo baseline, string environmentName, string uri);

    // Refresh existing baseline after a successful save: rewrites only the baseline block in the
    // existing meta.json (preserving fetchedAt/page); no-ops when meta.json does not exist —
    // never creates .clio-pages directories.
    internal static void RefreshExistingBaseline(IFileSystem fs, string metaFilePath,
        string savedSchemaUId, string newChecksum, string newModifiedOn);

    // FR-09 failure branch: removes the baseline block (keeps fetchedAt/page) so the next write
    // fails toward "no check", never toward a false conflict. Best-effort; swallows I/O errors.
    internal static void DeleteBaseline(IFileSystem fs, string metaFilePath);
}

// clio/Command/PageSchemaMetadataHelper.cs — new query (UId filter, dataValueType 0 = Guid)
internal static (JToken row, string error) QuerySysSchemaRowByUId(
    IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
    string schemaUId, params (string alias, string path)[] columns);

// clio/Command/PageUpdateOptions.cs — chokepoint check, called in TryUpdatePage right after
// TryResolveContext(...) and BEFORE the DryRun short-circuit (FR-12):
private bool TryCheckForExternalModification(
    PageUpdateOptions options, EditableSchemaContext context, out PageUpdateResponse response);
```

**Conflict-check decision table** (inside `TryCheckForExternalModification`; first match wins):

| # | Condition | Outcome |
|---|-----------|---------|
| 1 | `options.Force` | skip check, proceed |
| 2 | no baseline options set (`ExpectedChecksum`, `ExpectedSchemaUId`, `ExpectedSchemaAbsent` all empty/false) | skip check, proceed (FR-07) |
| 3 | `ExpectedSchemaAbsent && !context.IsCreateReplacing` | conflict `schema-created-externally` (AC-03) |
| 4 | `ExpectedSchemaAbsent && context.IsCreateReplacing` | no conflict (still absent), proceed |
| 5 | `ExpectedChecksum` set && `context.IsCreateReplacing` | conflict `schema-deleted-externally` (AC-04) |
| 6 | `ExpectedSchemaUId` set && ≠ `context.EditableSchemaUId` (ordinal-ignore-case) | conflict `schema-uid-mismatch` |
| 7 | `ExpectedChecksum` set: `QuerySysSchemaRowByUId(context.EditableSchemaUId, Checksum, ModifiedOn)` → row absent | conflict `schema-deleted-externally` |
| 8 | row present && server `Checksum` ≠ `ExpectedChecksum` | conflict `checksum-mismatch` (AC-01) |
| 9 | otherwise | proceed |

Conflict response shape: `Success=false`, `Conflict=true`, populated `ConflictDetails`, and `Error` text (single user-facing constant): *"Schema '<name>' was modified outside this session (external modification detected). Do NOT retry with the same body. Re-run get-page for this schema, re-apply your change on top of the fresh body, then retry. Use force=true ONLY after the user explicitly confirms overwriting the external changes."* CLI exit code is non-zero via the existing `Execute` → `TryUpdatePage` false path (AC-ERR).

**Post-save refresh (FR-09 / AC-08):** runs in `PageUpdateCommand` after `TrySaveSchema` succeeds (non-dry-run) and only when any baseline option or `Force` was supplied — keeps the no-baseline path at zero extra queries (SM-03 counter). Best-effort `QuerySysSchemaRowByUId`; success → `NewChecksum`/`NewModifiedOn`/`SavedSchemaUId` on the response; failure → fields stay null and the save still reports success. The MCP layer interprets *baseline-was-in-play + Success + null NewChecksum* as the signal to `DeleteBaseline`. OQ-01 (does `SaveSchema`/`GetSchema` already return the checksum?) stays open for the implementer — if it does, the post-save query is replaced by reading the save response; the SysSchema query is the committed safe fallback.

### CLI flag specification

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `--expected-checksum` | string | No | Baseline `SysSchema.Checksum` of the editable schema; when set, the save is blocked with a conflict error if the server-side checksum differs |
| `--force` | bool | No (default false) | Skip the external-modification check and deliberately overwrite |

MCP args (kebab-case JSON property names, matching existing tool conventions):

| Tool | New arg | Notes |
|------|---------|-------|
| `update-page` | `force` (bool?) | Skip conflict check |
| `update-page` | `output-directory` (string?) | Anchors `.clio-pages` baseline discovery (same semantics as on `get-page`/`sync-pages`) |
| `sync-pages` | per-page `force` (bool?) on `PageSyncPageInput` | Per-page overwrite |

MCP-internal options on `PageUpdateOptions` (no `[Option]` attribute, set only by tools): `ExpectedSchemaUId`, `ExpectedSchemaAbsent`.

All flags are kebab-case — CLIO001 enforced.

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit | NSubstitute on `IApplicationClient.ExecutePostRequest` (per-URL stubs) | `PageUpdateCommand`: conflict on checksum mismatch / absent-but-exists / deleted / UId mismatch; save proceeds with `force`; skip without baseline options; dry-run reports conflict; `NewChecksum` populated after save; post-save query failure leaves nulls + save succeeds | `clio.tests/Command/PageUpdateCommandTests.cs` (existing fixture family) |
| Unit | NSubstitute | `QuerySysSchemaRowByUId`: UId filter shape (dataValueType 0), not-found error | `clio.tests/Command/PageSchemaMetadataHelperTests.cs` |
| Unit | MockFileSystem | `PageBaselineStore`: read baseline; legacy/missing/corrupt meta → false; sibling meta via `body-file` inside `.clio-pages`; env guard (name/name, uri/uri, cross-mode); refresh preserves `fetchedAt`/`page`; refresh no-ops without meta.json; `DeleteBaseline` keeps legacy fields | `clio.tests/Command/McpServer/PageBaselineStoreTests.cs` |
| Unit | MockFileSystem + substitutes | `PageUpdateTool`: baseline → options mapping; skip on env mismatch; refresh after save; delete on null `NewChecksum`; constructor change ripples into `PageToolsTests.cs` | `clio.tests/Command/McpServer/PageToolsTests.cs` |
| Unit | substitutes | `PageSyncTool`: per-page conflict does not abort batch (AC-07); per-page force; verify=true rewrites full meta.json; verify=false refreshes baseline | `clio.tests/Command/McpServer/PageSyncToolTests.cs` |
| Unit | substitutes | `PageGetTool`: meta.json carries baseline; `editableSchemaExists=false` when `willCreateReplacing`; checksum query failure → meta without baseline + get-page succeeds (AC-09) | `clio.tests/Command/McpServer/PageToolsTests.cs` |
| E2E | clio.mcp.e2e | Ticket scenario: get-page → out-of-band schema change → update-page = conflict; force=true overwrites; no-baseline regression (AC-11); sync-pages batch with stale + fresh pages. **Run the first scenario early — it verifies A-01 (designer saves bump `SysSchema.Checksum`)** | `clio.mcp.e2e/` |

All new tests: `[Category("Unit")]`, AAA structure, `because` on every assertion, `[Description]` on every test, naming `MethodName_ShouldExpectedBehavior_WhenCondition`. Targeted run before commit: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`.

**Note:** MCP E2E tests (`clio.mcp.e2e`) are currently NOT in CI — manual execution against a live stand only; if no stand is available, flag the E2E scenarios as unverified in the PR.

### Docs / MCP artifacts (mandatory per repo MCP + docs policy)

- `clio/help/en/update-page.txt`, `clio/help/en/get-page.txt` — new options, conflict semantics
- `clio/docs/commands/update-page.md`, `sync-pages.md`, `get-page.md` — baseline lifecycle, conflict contract, force semantics
- `clio/Commands.md` — option index for `update-page`
- `clio/Command/McpServer/Tools/PageUpdateTool.cs` / `PageSyncTool.cs` / `PageGetTool.cs` `[Description]` texts — conflict contract + "force only after explicit user confirmation"
- `clio/Command/McpServer/Resources/PageModificationGuidanceResource.cs` — new section: conflict → reload via get-page → rebase → retry; force after confirmation
- Review `clio/Command/McpServer/Prompts/` page-flow texts; `docs/McpCapabilityMap.md`

## Consequences

- **Positive**: external designer edits can no longer be silently destroyed (Goal 2); one conflict implementation serves three write surfaces (FR-11); the LLM agent gets a machine-readable, self-explanatory recovery contract (Goal 3); the no-conflict path costs at most one extra SysSchema query per write and zero when no baseline exists (SM-03 counter); the existing sync-pages stale-baseline gap is closed (FR-13).
- **Trade-offs**: TOCTOU window between check and `SaveSchema` remains last-write-wins (accepted, A-02); detection depends on Creatio bumping `Checksum` on designer saves (A-01 — verified by the first E2E scenario before completing stories); parent-schema changes are invisible by design (A-03); `PageUpdateTool` constructor change touches existing test fixtures; `PageUpdateCommand` grows MCP-internal option fields.
- **Breaking change**: No — all CLI options, MCP args, response fields, and the meta.json `baseline` block are additive and optional; legacy meta.json files remain valid and skip the check (FR-07). No RELEASE.md migration entry required beyond the feature note.

## Pre-implementation Checklist

- [ ] All new CLI options are kebab-case (`--expected-checksum`, `--force`) — CLIO001
- [ ] No MediatR — logic stays in `PageUpdateCommand` (`Command<TOptions>`) and static MCP helpers; no new DI registrations needed (`PageBaselineStore` is static by deliberate parity with `PageOutputDirectoryResolver`)
- [ ] All Creatio HTTP via `IApplicationClient` (`QuerySysSchemaRowByUId` reuses the existing SelectQuery plumbing)
- [ ] Conflict check inserted after `TryResolveContext`, before the `DryRun` short-circuit (FR-12)
- [ ] Error message constants are user-friendly and agent-guiding (no stack traces)
- [ ] Existing tests affected: `clio.tests/Command/McpServer/PageToolsTests.cs` (PageUpdateTool ctor), PageUpdateCommand/PageGetTool/PageSyncTool fixtures
- [ ] MCP surface updated: tool descriptions, `PageModificationGuidanceResource`, prompts review, `docs/McpCapabilityMap.md`, `clio.mcp.e2e` coverage
- [ ] Docs updated: `help/en/*.txt`, `docs/commands/*.md`, `Commands.md`
- [ ] OQ-01 resolved during implementation (SaveSchema response checksum availability); OQ-02 (A-01 checksum semantics) verified by the first E2E scenario
- [ ] `meta.json` keeps exact legacy property names `fetchedAt`/`page` via explicit `[JsonPropertyName]`

---

## Addendum 2026-06-16 — CLI parity (ENG-91317 reopened)

**Trigger**: ENG-91317 was reopened. Reporter note: *"Claude works correct, Codex continue rewrite changes."* Codex reads a page through the **MCP** `get-page` tool (which writes the `.clio-pages/{schema}/meta.json` baseline) but saves through the **CLI** `clio update-page --body-file …\.clio-pages\{schema}\body.js` — without `--expected-checksum`. The conflict gate in `PageUpdateCommand.TryUpdatePage` therefore short-circuited (`if (!hasChecksum && !ExpectedSchemaAbsent) return true;`) and the external edit was silently overwritten.

**Decision reversed**: Option B assumed *"the command stays filesystem-free (CLI users pass `--expected-checksum` explicitly)"*. That assumption fails for AI-agent CLI flows, which never pass the flag. The CLI is no longer filesystem-free for page writes:

- Baseline orchestration (`TryArm` before save, `RefreshOrDrop` after) is extracted from `PageUpdateTool`/`PageSyncTool` into a shared DI service **`IPageBaselineGuard`** (`clio/Command/PageBaselineGuard.cs`), consumed by `PageUpdateCommand.Execute` (CLI) **and** all three MCP tools. An explicit `--expected-checksum` still wins over the on-disk baseline.
- `get-page` file output (`body.js`/`bundle.json`/`meta.json` + baseline) is extracted from `PageGetTool` into a shared DI service **`IPageFileWriter`** (`clio/Command/PageFileWriter.cs`), consumed by `PageGetCommand.Execute` (CLI, new kebab-case `--output-directory` option) **and** the MCP `get-page` tool. The CLI verb now persists the same `.clio-pages` layout the MCP tool does, so a pure-CLI `get-page → update-page` flow is protected too.
- `PageBaselineStore` and `PageOutputDirectoryResolver` moved from namespace `Clio.Command.McpServer.Tools` to `Clio.Command` (shared, no longer MCP-specific). Both new services are registered in `BindingsModule`.

**Scope notes**: `sync-pages` has no CLI verb (MCP-only) — unaffected beyond the shared-service refactor. The MCP external contract is unchanged (behavior-preserving refactor), so existing `clio.mcp.e2e` page coverage still applies; CLI-path coverage is added as unit tests (`PageBaselineGuardTests`, `PageFileWriterTests`, `PageUpdateCommandBaselineTests`, `PageGetCommandFileWriterTests`). Tracked as `story-detect-external-schema-changes-6`.
