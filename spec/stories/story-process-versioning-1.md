# Story 1: Read version facts from the process-library view

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-01, FR-05, FR-06, FR-17, FR-18, FR-19
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
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

- [ ] **AC-01** — Given a schema with no versions, when the reader runs, then it returns version 0, active true, a one-member family flagged as the root, and no warning
- [ ] **AC-02** — Given a two-member family, when the reader runs against the root, then it returns active false plus the active member's name and schema UId
- [ ] **AC-03** — Given the view row is absent or the DataService read fails, when the reader runs, then it returns facts carrying a warning and no version values, and does not throw
- [ ] **AC-04** — Given a family of 80 members, when the reader runs, then 50 are returned and `FamilyTruncated` is true
- [ ] **AC-05** — Given the view returns NULL for `Version` or `IsActiveVersion`, when the reader runs, then those facts are null rather than 0 / false
- [ ] **AC-06** — Given any successful read, when the facts are returned, then the active-version provenance is `process-library-view`
- [ ] **AC-ERR** — Given a `schemaUId` that is not a Guid, when the reader runs, then it returns facts with a warning and no version values, without throwing

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

- [ ] `[Property("Module","ProcessModel")]` on the fixture — not `Module="Command"`
- [ ] Rows fed through `ATF.Repository.Mock`'s items mock — check a current call site (`ApplyEnvironmentManifestCommandTests.cs:62,71`) for the exact API; a bare `Substitute.For<IDataProvider>()` cannot return rows
- [ ] If ATF cannot materialise a SQL NULL into the nullable properties, the null-facts case is proven at the mapping layer and that limitation is recorded
- [ ] Code compiles without Roslyn analyzer warnings
- [ ] All new tool names and flags are kebab-case
- [ ] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No `catch (Exception)` added to clio code paths
- [ ] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [ ] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [ ] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
