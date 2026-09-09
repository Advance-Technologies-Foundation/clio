# Story 1: Read version facts from the process-library view

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01, FR-05, FR-06, FR-17, FR-18, FR-19
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: in-progress
**Size**: M

---

## As a

developer

## I want

a service that reads a process's version facts and its version family from the platform's own process-library view

## So that

the describe path can report which version it read without a server change

---

## Acceptance Criteria

- [x] **AC-01** — Given a schema with no versions, when the reader runs, then it returns version 0, active true, a one-member family flagged as the root, and no warning
- [x] **AC-02** — Given a two-member family, when the reader runs against the root, then it returns active false plus the active member's name and schema UId
- [x] **AC-03** — Given the view row is absent or the DataService read fails, when the reader runs, then it returns facts carrying a warning and no version values, and does not throw
- [x] **AC-04** — Given a family of 80 members, when the reader runs, then 50 are returned and `FamilyTruncated` is true
- [x] **AC-05** — Given the view returns NULL for `Version` or `IsActiveVersion`, when the reader runs, then those facts are null rather than 0 / false
- [x] **AC-06** — Given any successful read, when the facts are returned, then the active-version provenance is `process-library-view`
- [x] **AC-ERR** — Given a `schemaUId` that is not a Guid, when the reader runs, then it returns facts with a warning and no version values, without throwing

## Implementation Notes

Key file: `clio/Command/ProcessModel/IProcessVersionLibReader.cs` (interface + impl in one file, as `IProcessDescriber.cs` does). Contract shape: ADR "Key interfaces / contracts".
Model change is the FIRST edit: `clio/CreatioModel/VwProcessLib.cs` — `Version` → `int?` (`:76-77`), `IsActiveVersion` → `bool?` (`:91-92`), `ParentId` → `Guid?` (`:34-35`). **Do NOT touch `VersionParentUId`** (`:100-101`): it is `COALESCE(PS.UId, SS.UId)` in the view and never NULL.
**Project the columns — resolved by removing them.** The model declared `MetaData byte[]` and `MetaDataModifiedOn`; ATF selects every declared property, so those made each process-library query carry the serialized schema per row. No caller reads them (verified across `clio`, `clio.tests`, `clio.mcp.e2e`), so they are deleted from the model instead of introducing a parallel narrow type — which would have been this repository's first duplicated `[Schema]` and first class-name/schema-name mismatch. This also narrows the two existing queries (`IProcessDescriber.cs:99`, `ProcessModelGenerator.cs:85-88`).
Two reads: the row by `UId`, then the family by `VersionParentUId` (a root's value equals its own `UId`, so the family query includes the root).
`OrderBy`/`Take` have precedent in `Common/CompilationHistoryPoller.cs:34-40`.
Registration: none. `RegisterAssemblyInterfaceTypes` (`BindingsModule.cs:233`) auto-registers `Clio`-namespace interfaces — confirm the skip-list does not catch this one (ADR choice 12).
Do NOT surface `IsMaxVersion` (ADR choice 7).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | selection, family ordering, root marking, cap flag, null facts, warning, provenance | `clio.tests/Command/ProcessModel/ProcessVersionLibReaderTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [x] `[Property("Module","ProcessModel")]` on the fixture — not `Module="Command"`
- [x] Rows fed through `ATF.Repository.Mock`'s items mock — check a current call site (`ApplyEnvironmentManifestCommandTests.cs:62,71`) for the exact API; a bare `Substitute.For<IDataProvider>()` cannot return rows
- [x] If ATF cannot materialise a SQL NULL into the nullable properties, the null-facts case is proven at the mapping layer and that limitation is recorded
- [x] Code compiles without Roslyn analyzer warnings
- [x] All new tool names and flags are kebab-case
- [x] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] No `catch (Exception)` added to clio code paths
- [x] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [x] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [x] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-09-02
- Implementation completed: 2026-09-02 (record backfilled 2026-09-03 — it was left as the template when the story was committed, which the pre-PR review caught)
- Tests passing: `dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)"` → green at every step since. 9 tests in `ProcessVersionLibReaderTests` cover AC-01..AC-06 and AC-ERR.
- Notes:
  - `MetaData` / `MetaDataModifiedOn` were REMOVED from `VwProcessLib` instead of introducing the parallel narrow type the ADR first specified. ATF selects every declared property, so those two made every process-library query carry the serialized schema per row — including a family read of up to 50 members. No caller had ever read them, so removing them is both smaller and wider: two pre-existing queries got cheaper. The ADR records the change of mechanism.
  - `Version`, `IsActiveVersion` and `ParentId` became nullable because the view genuinely returns NULL: the first two come from subqueries against `VwProcessSchemaInfo`, which INNER JOINs `SysPackage`. `VersionParentUId` stayed non-nullable on purpose — it is `COALESCE(parent.UId, own.UId)` — and story 3 later made that the family key the caption resolver gates on.
  - The failure surface moved to `ProcessLibRead.Guarded` on 2026-09-03: the pre-PR review showed the local ladder listed `TimeoutException` while the HttpClient transport raises `TaskCanceledException`, so the timeout coverage was nominal. One list now serves this reader and the describe caption path.
  - Also on 2026-09-03, `BuildFacts` stopped being able to publish an absent value with no warning — the one combination the contract forbids. A NULL column, a family with no flagged active member and an empty family read now each name the fact they could not establish.
  - Limitation, unchanged: the NULL mapping is proven through the ATF items mock. Materialising a real SQL NULL needs a family whose package does not resolve, which the local stand does not have.
  - The empty-family branch in `BuildFacts` is defensive and NOT unit-tested: `DataProviderMock` keys its canned rows on the schema name, so one query cannot return a row while the next returns none. 
