# Story 1: Foundation — UId-filtered SysSchema query + conflict/baseline models

**Feature**: detect-external-schema-changes
**FR coverage**: FR-04 (contract shapes), FR-06 (reason codes), foundation for FR-01/FR-02/FR-09
**PRD**: [prd-detect-external-schema-changes.md](../prd/prd-detect-external-schema-changes.md)
**ADR**: [adr-detect-external-schema-changes.md](../adr/adr-detect-external-schema-changes.md)
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: —

---

## As a

developer implementing the external-modification guard

## I want

a UId-filtered SysSchema metadata query (`QuerySysSchemaRowByUId`) and the typed conflict/baseline/response models in `PageModels.cs`

## So that

all later stories (baseline capture, conflict gate, sync-pages) build on one shared query and one shared contract instead of duplicating shapes

---

## Acceptance Criteria

- [ ] **AC-01** — Given a schema UId and column list, when `QuerySysSchemaRowByUId` runs, then it issues a SelectQuery filtered on `UId` (dataValueType 0/Guid) AND `ManagerName = ClientUnitSchemaManager` via `IApplicationClient.ExecutePostRequest` and returns `(row, null)` for an existing row.
- [ ] **AC-02** — Given the server returns no matching row, when `QuerySysSchemaRowByUId` runs, then it returns `(null, "Schema '<uid>' not found")`.
- [ ] **AC-03** — Given a `PageUpdateResponse` with `Conflict=true` and populated `ConflictDetails`, when it is serialized, then JSON uses camelCase names (`conflict`, `conflictDetails`, `newChecksum`, `newModifiedOn`, `savedSchemaUId`) and false/null values are suppressed (no contract change for pre-feature responses).
- [ ] **AC-04** — Given a `PageSyncPageResult` with conflict fields set, when it is serialized, then JSON uses kebab-case names (`conflict`, `conflict-details`) per that envelope's existing convention.
- [ ] **AC-05** — Given a `PageMetaFileModel` without a `Baseline`, when it is serialized, then output contains exactly the legacy property names `fetchedAt` and `page` and NO `baseline` property (byte-compatible legacy shape).
- [ ] **AC-06** — Given `PageConflictReasons`, when referenced, then the four constants serialize verbatim as `checksum-mismatch`, `schema-created-externally`, `schema-deleted-externally`, `schema-uid-mismatch` (FR-06).
- [ ] **AC-ERR** — Given the metadata query throws or the response is unparseable, when `QuerySysSchemaRowByUId` runs, then it returns `(null, <error text>)` and never throws to the caller.

## Implementation Notes

From ADR "Files to create/modify" + "Key interfaces":

- `clio/Command/PageSchemaMetadataHelper.cs` — add:
  ```csharp
  internal static (JToken row, string error) QuerySysSchemaRowByUId(
      IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
      string schemaUId, params (string alias, string path)[] columns);
  ```
  Same SelectQuery shape as the existing `QuerySysSchemaRow` (lines 190–235), but filtered on `UId` with dataValueType 0 — mirror the `BuildEqFilter("SysPackage.UId", 0, …)` usage at line 101. All HTTP via `IApplicationClient` only.
- `clio/Command/PageModels.cs` — add `PageConflictReasons` (static string constants), `PageConflictDetails`, `PageMetaFileModel`, `PageBaselineInfo` exactly per the ADR contract block (explicit `[JsonPropertyName]`, `PageMetaFileModel.Baseline` with `JsonIgnoreCondition.WhenWritingNull`).
- Extend `PageUpdateResponse` (line ~627, camelCase, null/false-suppressed): `Conflict`, `ConflictDetails`, `NewChecksum`, `NewModifiedOn`, `SavedSchemaUId`.
- Extend `PageGetResponse`: optional `editable` block carrying checksum/modifiedOn info (consumed by Story 2).
- Extend `PageSyncPageResult` (PageSyncTool.cs line ~786 convention): `conflict`, `conflict-details` kebab-case.
- DTO/value carriers may use `new`; no DI registration needed. XML doc comments on new public members.

Key file: `clio/Command/PageSchemaMetadataHelper.cs`, `clio/Command/PageModels.cs`
Pattern to follow: existing `QuerySysSchemaRow` SelectQuery plumbing; serialization conventions of each response envelope.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `QuerySysSchemaRowByUId`: UId filter shape (dataValueType 0), ManagerName filter, not-found error, error-on-exception | `clio.tests/Command/PageSchemaMetadataHelperTests.cs` |
| Unit `[Category("Unit")]` | Serialization round-trips: camelCase/kebab-case names, null/false suppression, legacy `fetchedAt`/`page` names preserved | `clio.tests/Command/PageModelsTests.cs` (or existing models fixture) |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` on every assertion + `[Description]` on every test. NSubstitute per-URL stubs on `IApplicationClient.ExecutePostRequest`.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command"`

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004); no new `CLIO*` warnings in modified files
- [ ] No new CLI flags in this story (models/query only)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`; AAA + `because` + `[Description]`
- [ ] All Creatio HTTP via `IApplicationClient` (no raw `HttpClient`)
- [ ] XML doc comments on new public types/members
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
