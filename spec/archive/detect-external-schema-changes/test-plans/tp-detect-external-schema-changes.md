# Test Plan: Detect External Schema Changes and Reload Before Applying Updates

**Feature**: detect-external-schema-changes
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317)
**Stories**: [story-1](../stories/story-detect-external-schema-changes-1.md), [story-2](../stories/story-detect-external-schema-changes-2.md), [story-3](../stories/story-detect-external-schema-changes-3.md), [story-4](../stories/story-detect-external-schema-changes-4.md), [story-5](../stories/story-detect-external-schema-changes-5.md)
**PRD**: [prd-detect-external-schema-changes.md](../prd/prd-detect-external-schema-changes.md)
**ADR**: [adr-detect-external-schema-changes.md](../adr/adr-detect-external-schema-changes.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-06-12

---

## Scope

### In scope

- Conflict-gate decision table in `PageUpdateCommand.TryCheckForExternalModification` (all 9 rows of the ADR table; all four reason codes of FR-06).
- Baseline capture in `get-page` (command + tool) and the typed `meta.json` (`PageMetaFileModel`/`PageBaselineInfo`).
- `PageBaselineStore` lifecycle: discovery, env-identity guard, refresh, delete, fail-toward-no-check semantics.
- `QuerySysSchemaRowByUId` query shape and error contract.
- `PageUpdateTool` baseline → options plumbing; `PageSyncTool` per-page conflict / per-page force / verify-path meta refresh.
- Response-contract serialization (camelCase on `PageUpdateResponse`, kebab-case on `PageSyncPageResult`, legacy `fetchedAt`/`page` byte-compatibility).
- CLI surface: `--expected-checksum` / `--force` on `update-page` (AC-ERR exit code).
- E2E reproduction of the ticket scenario incl. verification of A-01/OQ-02 (designer save bumps `SysSchema.Checksum`).
- Regression: pre-feature flows without a baseline must be byte-identical in behavior (legacy meta.json, missing meta.json, env mismatch, no `.clio-pages`).

### Out of scope

- `update-client-unit-schema` guard — deferred to a separate ticket (PRD non-goal).
- TOCTOU elimination / server-side locking — accepted limitation (A-02), only documented behavior is asserted.
- Parent-schema change detection — out by design (A-03).
- `ModifiedOn` comparison — opaque carry-through only (A-04); tests must assert it is NEVER compared.
- Performance benchmarking — only the "zero extra queries without baseline" counter (SM-03) is asserted via `Received` call counts.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **A-01/OQ-02 wrong**: Creatio does NOT bump `SysSchema.Checksum` on designer save → the whole feature detects nothing | Med | High | TC-E-01 is the FIRST test to run, before the feature is polished (Story 5 AC-03). Gate: do not close stories 3–5 until TC-E-01 result is recorded |
| **TOCTOU window**: external save lands between checksum check and `SaveSchema` → silent lost update despite the guard | Med | Med | Out of scope to fix (A-02); TC-E-07 documents the window as known-last-write-wins; no test asserts atomicity |
| **False-positive conflict after own save**: post-save baseline refresh missing/stale → the agent's own next `update-page` reports a bogus conflict | Med | High | TC-U-22/TC-U-23 (refresh after save), TC-E-05 regression loop (get-page → update → update must never conflict, PRD AC-11) |
| **Stale baseline after failed post-save query**: `NewChecksum` null but baseline kept → false conflict on next write | Med | High | FR-09 fail-toward-no-check: TC-U-15 (nulls + success), TC-U-24 (`DeleteBaseline` on null `NewChecksum`), TC-U-35 (delete keeps legacy fields) |
| **Regression in `PageToolsTests.cs` (119 tests)**: `PageUpdateTool` ctor gains `IFileSystem`; `PageGetTool` meta.json becomes typed | High | High | Expected breakage per ADR finding 8 — update instantiations; TC-U-04 asserts legacy `fetchedAt`/`page` JSON names byte-stable |
| **Regression in `PageSyncToolTests.cs` (25 tests)**: `SyncSinglePage` gains baseline plumbing + verify-path meta write | Med | High | Run `Module=McpServer` suite per story; TC-U-29 asserts no-baseline batch behaves pre-feature |
| **Envelope-convention mix-up**: camelCase fields on the kebab-case `PageSyncPageResult` (or vice versa) breaks MCP clients | Med | Med | TC-U-05/TC-U-06 serialization contract tests per envelope (ADR finding 6) |
| **Env-guard mis-match**: `SyncSinglePage` builds options without `Environment` — guard against options instead of `args.EnvironmentName` skips/fires wrongly | Med | High | TC-U-31 explicitly pins the guard to `args.EnvironmentName` (ADR finding 4) |
| **Baseline refresh creates `.clio-pages`**: `PageGetTool` deletes the schema dir per fetch; refresh after save must not resurrect it | Med | Low | TC-U-34 (refresh no-ops without meta.json, never creates dirs) |
| **MCP E2E not in CI** | High | Med | All TC-E-* manual-only; PR checklist gate; "verified on stand / flagged unverified" recorded in PR description |
| **CLI flag naming (CLIO001)** | Low | High | Build-time analyzer; AC on kebab-case `--expected-checksum`/`--force`; TC-U-16 covers the CLI conflict path |

---

## Unit Tests (`clio.tests/`)

All unit tests: `[Category("Unit")]`, NUnit 4 + FluentAssertions + NSubstitute, AAA + `because` on every assertion + `[Description]` on every test, naming `MethodName_ShouldBehavior_WhenCondition`. Creatio HTTP mocked via per-URL NSubstitute stubs on `IApplicationClient.ExecutePostRequest`; filesystem via `MockFileSystem`.

### Story 1 — query + models (`Module=Command`)

**File**: `clio.tests/Command/PageSchemaMetadataHelperTests.cs` (new)

#### TC-U-01: UId-filtered query shape

```csharp
[Test]
[Category("Unit")]
[Description("Verifies QuerySysSchemaRowByUId issues a SelectQuery filtered on UId (dataValueType 0/Guid) and ManagerName=ClientUnitSchemaManager")]
public void QuerySysSchemaRowByUId_ShouldFilterByUIdAndManagerName_WhenCalled()
{
    // Arrange
    var client = Substitute.For<IApplicationClient>();
    string capturedBody = null;
    client.ExecutePostRequest(Arg.Is<string>(u => u.Contains("SelectQuery")), Arg.Do<string>(b => capturedBody = b))
          .Returns(ValidSingleRowSelectResponse);

    // Act
    var (row, error) = PageSchemaMetadataHelper.QuerySysSchemaRowByUId(
        client, _serviceUrlBuilder, SchemaUId, ("Checksum", "Checksum"), ("ModifiedOn", "ModifiedOn"));

    // Assert
    row.Should().NotBeNull(because: "an existing row must be returned");
    error.Should().BeNull(because: "success path carries no error");
    capturedBody.Should().Contain("\"dataValueType\":0", because: "UId filter must use Guid dataValueType 0 per ADR");
    capturedBody.Should().Contain("ClientUnitSchemaManager", because: "only client-unit schemas are relevant");
}
```

#### TC-U-02: not-found contract
`QuerySysSchemaRowByUId_ShouldReturnNotFoundError_WhenServerReturnsNoRow` — `(null, "Schema '<uid>' not found")` (Story 1 AC-02).

#### TC-U-03: never-throw contract
`QuerySysSchemaRowByUId_ShouldReturnErrorTuple_WhenClientThrowsOrResponseUnparseable` — exception and garbage-JSON cases both return `(null, errorText)`, never propagate (Story 1 AC-ERR).

**File**: `clio.tests/Command/PageModelsTests.cs` (new)

#### TC-U-04: legacy meta.json byte-compatibility
`Serialize_ShouldEmitOnlyFetchedAtAndPage_WhenPageMetaFileModelHasNoBaseline` — exact property names `fetchedAt`/`page`, NO `baseline` key (Story 1 AC-05, FR-07 reader compatibility).

#### TC-U-05: camelCase envelope contract
`Serialize_ShouldUseCamelCaseAndSuppressDefaults_WhenPageUpdateResponseCarriesConflict` — `conflict`, `conflictDetails`, `newChecksum`, `newModifiedOn`, `savedSchemaUId`; AND a pre-feature response (all new fields default) serializes with none of the new keys present (Story 1 AC-03).

#### TC-U-06: kebab-case envelope contract
`Serialize_ShouldUseKebabCaseNames_WhenPageSyncPageResultCarriesConflict` — `conflict`, `conflict-details` (Story 1 AC-04, ADR finding 6).

#### TC-U-07: reason-code constants
`PageConflictReasons_ShouldSerializeVerbatimKebabValues_WhenUsedInConflictDetails` — the four FR-06 strings exactly.

### Story 2 — baseline capture (`Module=Command` + `Module=McpServer`)

**File**: `clio.tests/Command/McpServer/PageBaselineStoreTests.cs` (new, MockFileSystem)

#### TC-U-08: happy-path read
`TryReadBaseline_ShouldReturnBaseline_WhenMetaJsonContainsBaselineBlock` — anchor-resolved `.clio-pages/{schema}/meta.json`.

#### TC-U-09: fail-toward-no-check trio
`TryReadBaseline_ShouldReturnFalse_WhenMetaMissingOrLegacyOrCorrupt` — three cases (missing file, legacy `{fetchedAt,page}` only, unparseable JSON) each return `false` (Story 2 AC-04, FR-07).

#### TC-U-10: sibling-meta precedence
`TryReadBaseline_ShouldPreferSiblingMeta_WhenBodyFileResidesInsideClioPagesSchemaDir` (Story 2 AC-05, A-06).

#### TC-U-11: env guard — name match
`MatchesEnvironment_ShouldReturnTrue_WhenEnvironmentNamesMatchIgnoringCase`.

#### TC-U-12: env guard — uri match + normalization
`MatchesEnvironment_ShouldReturnTrue_WhenUrisMatchIgnoringCaseAndTrailingSlash`.

#### TC-U-13: env guard — skip combinations
`MatchesEnvironment_ShouldReturnFalse_WhenCrossModeOrBothNullOrMismatch` — name vs uri (cross-mode), both-null, name mismatch, uri mismatch (Story 2 AC-06, FR-08).

**File**: existing PageGet fixture in `clio.tests/Command/` (extend)

#### TC-U-14: get-page degrades, never fails
`TryGetPage_ShouldSucceedWithNullEditableChecksum_WhenChecksumQueryThrows` — page returned, `editable` block nulls (Story 2 AC-03 / PRD AC-09, FR-10).

**File**: `clio.tests/Command/McpServer/PageToolsTests.cs` (extend)

#### TC-U-15: meta carries baseline
`WriteFilesAndCompact_ShouldWriteBaselineBlock_WhenChecksumQuerySucceeds` — `schemaName`, env identity (one of name/uri, the other null), `editableSchemaExists=true`, `editableSchemaUId`, `checksum`, raw `modifiedOn`, ISO-8601 UTC `capturedAt` (FR-01).

#### TC-U-16: absent editable schema baseline
`WriteFilesAndCompact_ShouldWriteEditableSchemaExistsFalse_WhenWillCreateReplacing` — `editableSchemaUId`/`checksum` null (Story 2 AC-02; feeds AC-03 `schema-created-externally`).

#### TC-U-17: checksum failure → meta without baseline
`WriteFilesAndCompact_ShouldOmitBaselineBlock_WhenChecksumUnavailable` — and get-page result still successful (PRD AC-09).

### Story 3 — conflict gate + update tool (`Module=Command` + `Module=McpServer`)

**File**: `clio.tests/Command/PageUpdateCommandTests.cs` (new fixture in the existing Page command family; prefer `BaseCommandTests<PageUpdateOptions>`)

#### TC-U-18: checksum mismatch blocks write (core scenario)

```csharp
[Test]
[Category("Unit")]
[Description("Verifies the write is blocked with a checksum-mismatch conflict when the server checksum differs from --expected-checksum")]
public void TryUpdatePage_ShouldBlockWithChecksumMismatchConflict_WhenServerChecksumDiffers()
{
    // Arrange: context resolves an existing editable schema; SysSchema row stub returns Checksum="B"
    var options = ValidOptions(expectedChecksum: "A");

    // Act
    bool result = _sut.TryUpdatePage(options, out PageUpdateResponse response);

    // Assert
    result.Should().BeFalse(because: "a conflicting write must fail (non-zero CLI exit, AC-ERR)");
    response.Conflict.Should().BeTrue(because: "the response must be machine-readably marked as a conflict");
    response.ConflictDetails.Reason.Should().Be("checksum-mismatch", because: "FR-06 reason code for AC-01");
    response.ConflictDetails.ExpectedChecksum.Should().Be("A", because: "agent needs the baseline value to reason about the conflict");
    response.ConflictDetails.ActualChecksum.Should().Be("B", because: "agent needs the server value");
    response.Error.Should().Contain("Re-run get-page", because: "agent-guiding recovery text is part of the contract (Goal 3)");
    _client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(u => u.Contains("SaveSchema")), Arg.Any<string>());
}
```

#### TC-U-19: force overwrites
`TryUpdatePage_ShouldSaveSchema_WhenForceTrueDespiteChecksumMismatch` — saved, `NewChecksum` populated, no conflict (PRD AC-02; decision-table row 1).

#### TC-U-20: schema-created-externally
`TryUpdatePage_ShouldBlockWithSchemaCreatedExternally_WhenBaselineSaysAbsentButSchemaExists` — `ExpectedSchemaAbsent=true && !IsCreateReplacing` (PRD AC-03; row 3); companion branch: still-absent (`IsCreateReplacing`) proceeds (row 4).

#### TC-U-21: schema-deleted-externally (two paths)
`TryUpdatePage_ShouldBlockWithSchemaDeletedExternally_WhenEditableSchemaNoLongerExists` — both row 5 (`IsCreateReplacing` with checksum set) and row 7 (metadata row absent) (PRD AC-04).

#### TC-U-22: schema-uid-mismatch
`TryUpdatePage_ShouldBlockWithSchemaUIdMismatch_WhenExpectedUIdDiffersFromContext` — ordinal-ignore-case comparison: differing-case-only UIds do NOT conflict (row 6).

#### TC-U-23: no baseline → zero extra queries (SM-03 counter)
`TryUpdatePage_ShouldSkipCheckAndIssueNoMetadataQuery_WhenNoBaselineOptionsSet` — assert `Received` count on `ExecutePostRequest` for SelectQuery URLs equals the pre-feature count exactly (FR-07, PRD AC-05/AC-11).

#### TC-U-24: dry-run reports conflict
`TryUpdatePage_ShouldReportConflictWithoutWriting_WhenDryRunAgainstStaleBaseline` — check runs before the DryRun short-circuit (FR-12, PRD AC-10).

#### TC-U-25: post-save refresh success
`TryUpdatePage_ShouldPopulateNewChecksumAndModifiedOn_WhenPostSaveQuerySucceeds` — also asserts the post-save query fires ONLY when a baseline option or `Force` was supplied (FR-09 + SM-03 counter).

#### TC-U-26: post-save refresh failure → fail toward no-check
`TryUpdatePage_ShouldReportSuccessWithNullNewChecksum_WhenPostSaveQueryFails` — save success preserved, `NewChecksum`/`NewModifiedOn`/`SavedSchemaUId` null (PRD AC-08 failure branch).

**File**: `clio.tests/Command/McpServer/PageToolsTests.cs` (extend; ctor instantiations updated for `IFileSystem`)

#### TC-U-27: baseline → options mapping
`ExecuteAsync_ShouldPopulateExpectedOptionsFromBaseline_WhenMetaJsonHasBaselineAndEnvMatches` — `ExpectedChecksum`/`ExpectedSchemaUId`/`ExpectedSchemaAbsent`/`Force` mapped onto `PageUpdateOptions`.

#### TC-U-28: env mismatch skips check
`ExecuteAsync_ShouldNotPopulateExpectedOptions_WhenBaselineEnvironmentDiffers` (FR-08, PRD AC-06).

#### TC-U-29: refresh after save
`ExecuteAsync_ShouldRefreshBaseline_WhenSaveSucceedsWithNewChecksum` — `RefreshExistingBaseline` semantics observable via MockFileSystem: baseline block updated, `fetchedAt`/`page` preserved.

#### TC-U-30: delete baseline on null NewChecksum
`ExecuteAsync_ShouldDeleteBaselineBlock_WhenSaveSucceedsButNewChecksumIsNull` — stale-baseline guard (FR-09; risk "stale baseline after failed post-save query").

### Story 2/3 store lifecycle (continued in `PageBaselineStoreTests.cs`)

#### TC-U-31: refresh preserves legacy fields
`RefreshExistingBaseline_ShouldRewriteOnlyBaselineBlock_WhenMetaJsonExists` — `fetchedAt`/`page` byte-preserved (Story 2 AC-07).

#### TC-U-32: refresh never creates directories
`RefreshExistingBaseline_ShouldNoOp_WhenMetaJsonDoesNotExist` — no `.clio-pages` dir created (PageGetTool deletes the dir per fetch — ADR finding 1).

#### TC-U-33: delete keeps legacy fields and swallows I/O errors
`DeleteBaseline_ShouldRemoveBaselineKeepFetchedAtAndPage_WhenMetaJsonExists` + locked-file/I/O-error case swallowed (Story 2 AC-08).

### Story 4 — sync-pages (`Module=McpServer`)

**File**: `clio.tests/Command/McpServer/PageSyncToolTests.cs` (extend)

#### TC-U-34: per-page conflict does not abort batch
`SyncPages_ShouldReportPerPageConflictAndContinueBatch_WhenOnePageIsStale` — stale page: `conflict=true` + kebab `conflict-details`; fresh page saved; batch result complete (FR-03, PRD AC-07).

#### TC-U-35: per-page force
`SyncPages_ShouldSaveStalePage_WhenPerPageForceTrue` (FR-05).

#### TC-U-36: no-baseline page is pre-feature identical
`SyncSinglePage_ShouldPerformNoCheck_WhenMetaJsonMissingOrLegacy` — zero extra SelectQuery calls for that page (PRD AC-05).

#### TC-U-37: env guard uses args.EnvironmentName
`SyncSinglePage_ShouldSkipCheck_WhenBaselineEnvironmentDiffersFromArgsEnvironmentName` — explicitly pins the comparison to `args.EnvironmentName`, NOT `PageUpdateOptions.Environment` (built without it — ADR finding 4).

#### TC-U-38: verify path writes full fresh meta.json
`SyncSinglePage_ShouldWriteFullTypedMetaJsonBesideBodyJs_WhenVerifyTrue` — typed `PageMetaFileModel` incl. fresh baseline (FR-13, closes the stale-baseline gap).

#### TC-U-39: non-verify refresh / delete
`SyncSinglePage_ShouldRefreshBaselineOrDeleteOnNullNewChecksum_WhenVerifyFalse` (FR-09 per page).

### Story 5 — guidance content (`Module=McpServer`)

#### TC-U-40: guidance resource conflict section
`PageModificationGuidanceResource_ShouldContainConflictRecoverySection_WhenRead` — reload via get-page → rebase → retry → force-after-confirmation wording matches the Story 3 error-text constant (only if the existing resource fixture pattern asserts content; otherwise fold into doc review).

---

## Integration Tests (`clio.tests/`)

No new Integration-tier tests required: all I/O is virtualized via `MockFileSystem` and all HTTP via `IApplicationClient` substitutes; there is no DB/IIS/K8s surface in this feature. Real-process and real-Creatio verification is covered by the E2E tier below.

---

## E2E Tests (`clio.mcp.e2e/`)

**⚠️ CI status: clio.mcp.e2e is NOT in CI — manual execution against a live stand only.** Record run status (verified on stand / flagged unverified) in the PR description; do not silently skip (Story 5 DoD).

### TC-E-01: A-01/OQ-02 verification — designer save bumps SysSchema.Checksum ⚠️ RUN FIRST

- **Why first**: the entire feature depends on assumption A-01. If a designer/`SaveSchema` save does NOT change `SysSchema.Checksum`, detection silently misses every external edit and stories 3–5 must be re-designed. This scenario is the gate before the rest of the feature is polished (PRD OQ-02, ADR trade-offs).
- **Tools**: `get-page` → out-of-band schema save (designer save or direct `SaveSchema` call via the test harness) → `get-page` again (or raw SysSchema query)
- **Input**: existing Freedom UI page with an editable schema
- **Expected**: `SysSchema.Checksum` value AFTER the out-of-band save differs from the baseline captured BEFORE it
- **Manual gate**: result (incl. Creatio version tested) recorded in the Story 5 Dev Agent Record and PR description

### TC-E-02: Ticket scenario — external change blocks update-page

- **Tools**: `get-page` → out-of-band schema change → `update-page` (no force)
- **Expected output**: write blocked; `conflict=true`, `conflictDetails.reason="checksum-mismatch"`, expected/actual checksums, recovery text present; server body unchanged (PRD AC-01, SM-02)

### TC-E-03: Force overwrite

- **Tools**: same conflict state → `update-page` with `force=true`
- **Expected output**: save succeeds, response carries fresh `newChecksum`; server body now reflects the agent's write (PRD AC-02)

### TC-E-04: Conflict recovery loop

- **Tools**: conflict from TC-E-02 → `get-page` (re-fetch) → `update-page` with rebased body
- **Expected output**: second write succeeds with no conflict — proves the baseline-refresh-on-get-page path closes the loop the guidance text describes (Goal 3)

### TC-E-05: No-baseline / no-external-change regression loop

- **Tools**: `get-page` → `update-page` → `update-page` (no external modification)
- **Expected output**: every write succeeds, `conflict` absent from every response, behavior identical to pre-feature flow — guards both the false-positive risk (own-save-then-write must not conflict, i.e. baseline refresh after save works end-to-end) and SM-01 counter (PRD AC-11)

### TC-E-06: sync-pages batch — stale + fresh

- **Tool**: `sync-pages` with two pages, one externally modified after its `get-page`
- **Expected output**: stale page result: `conflict=true` + `conflict-details` (kebab-case); fresh page saved; batch completes without abort (PRD AC-07); repeat with per-page `force=true` on the stale page → it saves

### TC-E-07: TOCTOU window — documented limitation (exploratory, not a pass/fail gate)

- **Scenario**: external save injected BETWEEN the conflict check and `SaveSchema` (timing-dependent; best-effort)
- **Expected**: last-write-wins inside the window — this is the accepted A-02 behavior; the test exists to document, not to enforce. Any future flakiness here must NOT block release.

### TC-E-08: Legacy meta.json regression

- **Setup**: hand-written pre-feature `meta.json` (`{fetchedAt, page}` only) in `.clio-pages/{schema}/`
- **Tools**: `update-page` and `sync-pages` against it
- **Expected output**: no check performed, writes succeed exactly as pre-feature; meta.json not corrupted (PRD AC-05, FR-07)

---

## Regression Guard

Tests that MUST pass after this feature ships:

| Test file | Scope at risk | Why at risk |
|-----------|---------------|------------|
| `clio.tests/Command/McpServer/PageToolsTests.cs` (119 tests) | ALL — entire fixture | `PageUpdateTool` constructor gains `IFileSystem` (instantiation break, expected per ADR finding 8); `PageGetTool.WriteFilesAndCompact` replaces the anonymous meta object with typed `PageMetaFileModel` — any test asserting meta.json content must still see `fetchedAt`/`page` |
| `clio.tests/Command/McpServer/PageSyncToolTests.cs` (25 tests) | ALL — entire fixture | `SyncSinglePage` gains baseline discovery, per-page `force` on `PageSyncPageInput`, verify-path meta write; result envelope gains kebab fields |
| `clio.tests/Command/McpServer/PageOutputDirectoryResolverTests.cs` | Anchor resolution | `PageBaselineStore` reuses `ResolveAnchor` — behavior must stay byte-identical |
| `clio.tests/Command/McpServer/ToolContractGetToolTests.cs` | Tool contract snapshots | `update-page` gains `force`/`output-directory` args, `sync-pages` page input gains `force` — contract-shape assertions may need updating (additive only) |
| `clio.tests/Command/PageUpdateBodyLoaderTests.cs` | Body loading | Shares the `update-page` execution path; must be untouched by the conflict gate (gate sits after `TryResolveContext`, before body save) |
| `clio.tests/Command/McpServer/ComponentRegistryCacheStoreTests.cs` | None expected | Same static-store pattern family; sanity only |

Mandatory pre-commit filter per story:
`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`

Behavioral regression invariants (asserted by TC-U-23, TC-U-36, TC-E-05, TC-E-08):
1. No baseline options → zero extra SysSchema queries, identical responses (no new JSON keys emitted thanks to null/false suppression).
2. Legacy/missing `meta.json`, env mismatch, absent `.clio-pages` → check silently skipped on `update-page`, `sync-pages`, and the CLI verb.
3. `get-page` never fails due to checksum capture (FR-10).

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit | 40 (TC-U-01…40) | ~10–15 in `PageToolsTests.cs` (ctor `IFileSystem` ripple) + contract snapshots in `ToolContractGetToolTests.cs` | New files: `PageSchemaMetadataHelperTests.cs`, `PageModelsTests.cs`, `PageBaselineStoreTests.cs`, `PageUpdateCommandTests.cs` |
| Integration | 0 | 0 | No real-I/O surface; MockFileSystem + IApplicationClient substitutes |
| E2E | 8 (TC-E-01…08) | 0 | Manual only — NOT in CI; TC-E-01 runs FIRST (A-01 gate); TC-E-07 exploratory |

Traceability: every PRD AC (AC-01…AC-11, AC-ERR) maps to at least one TC — AC-01→TC-U-18/TC-E-02; AC-02→TC-U-19/TC-E-03; AC-03→TC-U-20; AC-04→TC-U-21; AC-05→TC-U-23/TC-U-36/TC-E-08; AC-06→TC-U-28/TC-U-37; AC-07→TC-U-34/TC-E-06; AC-08→TC-U-25/TC-U-26/TC-U-30; AC-09→TC-U-14/TC-U-17; AC-10→TC-U-24; AC-11→TC-E-05; AC-ERR→TC-U-18 (non-zero exit assertion).

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] No Integration tier needed — confirmed in scope review (re-open if any real-I/O surface appears during implementation)
- [ ] All assertions carry `because`; all tests carry `[Description]`; AAA structure throughout
- [ ] Test naming follows `MethodName_ShouldBehavior_WhenCondition`
- [ ] Regression guard fixtures (`PageToolsTests`, `PageSyncToolTests`, `PageOutputDirectoryResolverTests`, `ToolContractGetToolTests`, `PageUpdateBodyLoaderTests`) green after each story
- [ ] TC-E-01 (A-01/OQ-02 checksum-bump verification) executed FIRST and its outcome recorded before stories 3–5 close
- [ ] All TC-E-* documented in `clio.mcp.e2e/` with `[Category("E2E")]`; run status (verified on stand / unverified) in PR description — manual gate, NOT in CI
- [ ] SM-03 counter validated: TC-U-23/TC-U-36 assert zero extra queries on the no-baseline path
- [ ] PR includes new and modified test files in the changed-files list; targeted filter command quoted in the PR description
