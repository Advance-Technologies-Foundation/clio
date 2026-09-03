# Story 12: Number, name and persist the new version

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-15
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: ready-for-dev
**Size**: M

---

## As a

no-code builder

## I want

the new version numbered and named the way the platform does it, and saved once

## So that

numbering never collides and the response tells me what was actually persisted

---

## Acceptance Criteria

- [ ] **AC-01** — Given a root with no versions, when the operation runs, then the number comes from `GetMaxProcessVersionInPackage(conn, rootItem.Id, packageUId) + 1`; run twice, the numbers are 1 and 2
- [ ] **AC-02** — Given the clone before the save, when its name is set, then it is `rootItem.Name + Regex.Replace(packageName, @"\W", "") + version` and the rename precedes the platform's name validation
- [ ] **AC-03** — Given a name that already exists, when the operation runs, then it refuses before the save with a message naming the collision
- [ ] **AC-04** — Given a successful save, when the response is built, then `versionSchemaUId`, `versionName`, `version` and `isActiveVersion` come from a post-save re-read through metadata, not from the in-memory instance
- [ ] **AC-05** — Given another writer took the number, when the post-save re-read runs, then the operation returns `success:false` naming the number that was taken
- [ ] **AC-06** — Given the saved version reports `IsInterpretable` false, when the response is built, then `warnings` says it cannot execute until the configuration is compiled
- [ ] **AC-ERR** — Given the save fails — whether by throwing **or** by returning `false` without throwing — when the operation returns, then it reports `success:false` and the draft is rolled back

## Implementation Notes

**`rootItem.Id` is the `SysSchema.Id`, not the UId** — passing the UId compiles, runs and silently returns 0 (ADR choice 14).
Save through `SaveSchema(item, …)` (`SchemaManager.cs:4841-4848`); do NOT open a design session for the clone (ADR choice 18) — `DesignSchema` on a UId absent from `SysSchema` dereferences a null design item and `SaveSchema(Guid, …)` throws `ItemNotFoundException`.
Rename before saving: the clone starts life carrying the source's `Name` (`MetaItem.cs:108`), and both `CheckIsValidSchemaName` (`InternalSaveSchema:2701`) and `SchemaDuplicationDetector.CheckDuplicateSchemaNameExists` (`SchemaManager.cs:941-945`, case-insensitive, throws `InvalidNameException`) gate it.
`SaveSchema` can return **false without committing and without throwing** when source generation fails (`:1666-1685`) — handle that branch as the in-place path does (`ProcessModifyHandler.cs:90-97`).
Post-save re-read (ADR choice 19): both production copy paths re-read through metadata (`ProcessSchemaManager.cs:560-596`, `GetCopiedSchemaInstance:271-275` forcing `ForceUseInstanceFromMetaData = true`). It doubles as the collision check.
Fix the rollback guard while here: `ProcessSchemaRepository.cs:82` guards with `FindItemByUId` over `Items`, while the save path registers with `Add(item)` and is found through `FindItemByRealUId` over `AllItems` (`Manager.cs:181-187`), so a rolled-back clone can fall between the two.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | allocation arguments, rename-before-validation, collision refusal, post-save re-read as the response source, re-read collision verdict, both save-failure branches, interpretability warning | `tests/CrtProcessBuilder/ProcessVersionSaveHandlerTests.cs` |
| Integration `[Category("Integration")]` | two consecutive saves numbering 1 then 2, and a rejected edit leaving the family unchanged | dedicated sandbox, `[Explicit]`, sequential |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Both save-failure branches covered: the throw and the non-throwing `false`
- [ ] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [ ] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [ ] PR description references this story file
- [ ] `docs/process-builder-architecture.md` and `.puml` updated together
- [ ] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`)

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
