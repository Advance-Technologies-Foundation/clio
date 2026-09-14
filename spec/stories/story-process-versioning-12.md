# Story 12: Number, name and persist the new version

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-15
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: done
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

- [x] **AC-01** — Given a root with no versions, when the operation runs, then the number comes from `GetMaxProcessVersionInPackage(conn, rootItem.Id, packageUId) + 1`; run twice, the numbers are 1 and 2
- [x] **AC-02** — Given the clone before the save, when its name is set, then it is `rootItem.Name + Regex.Replace(packageName, @"\W", "") + version` and the rename precedes the platform's name validation
- [x] **AC-03** — Given a name that already exists, when the operation runs, then it refuses before the save with a message naming the collision
- [x] **AC-04** — Given a successful save, when the response is built, then `versionSchemaUId`, `versionName`, `version` and `isActiveVersion` come from a post-save re-read through metadata, not from the in-memory instance
- [x] **AC-05** — Given another writer took the number, when the post-save re-read runs, then the operation returns `success:false` naming the number that was taken
- [x] **AC-06** — Given the saved version reports `IsInterpretable` false, when the response is built, then `warnings` says it cannot execute until the configuration is compiled
- [x] **AC-ERR** — Given the save fails — whether by throwing **or** by returning `false` without throwing — when the operation returns, then it reports `success:false` and the draft is rolled back

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

- [x] Both save-failure branches covered: the throw and the non-throwing `false`
- [x] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [x] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [x] PR description references this story file
- [x] `docs/process-builder-architecture.md` and `.puml` updated together
- [x] ClioRing MCP compatibility verdict recorded — the anchor `AGENTS.md:241-299` does not resolve in this repository. Verdict: no MCP surface changes, nothing for ClioRing to be compatible with until stories 16-17.

## Dev Agent Record

- Implementation started: 2026-09-04
- Implementation completed: 2026-09-04
- Tests passing: `dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf` → **957 passed, 0 failed** (13 new). Build clean, 0 warnings. The story-9 placeholder refusal and the two tests that pinned it are gone — the operation now saves.
- Notes:
  - **The allocator takes `rootItem.Id`.** Handed a UId it compiles, runs and silently answers 0, which is
    how two versions end up claiming one number. The count is scoped to (root, PACKAGE), so a version
    created in another package restarts at 1, and a root's own stamped number is not part of it.
  - **A name collision is refused BEFORE the save.** The platform validates the name inside
    `InternalSaveSchema` and its duplication detector throws `InvalidNameException`, which a caller cannot
    act on; `ProcessExists(name)` runs first so the refusal names the collision and the package.
  - **The rename touches the item AND the instance.** `SaveSchema(item, \u2026)` reads the package from
    `designItem.Instance.PackageUId` while the manager indexes the item by its own `Name`; setting one and
    not the other saves into the wrong package or under the wrong name.
  - **`ForceUseInstanceFromMetaData` is unreachable, so the re-read goes through the interface member.**
    That flag — which both production copy paths set — is `internal` on `ProcessSchemaManagerItem`. The
    facts therefore come from `item.FindPropertyValue(\u2026)` on a freshly found item, exactly as the ADR's
    contract note prescribes. It is still a genuine post-save read and still doubles as the collision
    check: the family is re-read, and another member on our number in our package makes the call
    `success:false` naming it.
  - **`SaveSchema` can answer `false` without throwing**, committing nothing, when source generation
    fails. Handled as its own branch with a rollback and an explicit message.
  - **The rollback guard could NOT be fixed as the Implementation Notes describe.**
    `FindItemByRealUId` — the lookup the SAVE path registers through — is inaccessible from a package, so
    a `FindItemByUId` guard covers only designed items and a saved-then-rolled-back clone falls between
    the two. `Rollback` now removes UNCONDITIONALLY; the existing try/catch already absorbs "there was
    nothing to remove", which is the only thing the guard bought. `ProcessSchemaRepositoryTests` was
    re-pinned to that contract and the old test name went with it.
  - **A story-9 test caught a real defect here:** the first version of the catch called
    `Rollback(cloneItem)` unconditionally, so an unauthorized caller produced a repository call — turning
    the refusal into an oracle for whether a process exists. Now guarded by `cloneItem != null && !saved`.
  - **VERIFIED ON A STAND — the numbers 1 and 2 are observed, not derived.** Run against `creatio_2`
    (`C:\Projects\creatio_2`, port 40002, core 10.2.20.0, classic DB mode), a second local stand the
    owner pointed at. The branch build installed through `push-workspace` and
    `ProcessDesignService/Ping` answered `{"PingResult":{"success":true}}`.
    - First call, empty edit list: `success:true, version:1, versionName:"UsrVerifyStory12Custom1",
      isActiveVersion:false, versionRootSchemaUId:<root>, appliedOperations:0, warnings:null`.
    - Second call, same root: `version:2, versionName:"UsrVerifyStory12Custom2"`. **AC-01 observed.**
    - A rejected descriptor (`op:"addWidget"`): `success:false`, every version member null/0, the message
      naming the unsupported operation. `SysSchema` then held exactly THREE rows for the family, so
      nothing was persisted — **FR-16 observed rather than argued.**
    - In the database, both versions' `SysSchema.ParentId` equals the ROOT's `SysSchema.Id`, not its UId
      — the translation story 8 measured, now confirmed from the write side.
    - Through the READ half, `describe-business-process` on the root reports `version:0,
      isActiveVersion:true` and a `versions[]` of three: root active at 0, both created versions inactive
      at 1 and 2, all in one package. **The root stayed active through both creations**, so creating a
      version changed nothing about what the environment executes. Stories 1-5 and 9-12 agree end to end.
    - `warnings` was null on both saves, so the saved versions came back `IsInterpretable = true` and the
      AC-06 warning path stayed correctly silent.
    - Trap for any caller: the WCF body must be WRAPPED by the parameter name (`{"request": {…}}`). A bare
      descriptor answers `Value cannot be null. Parameter name: request`, which reads like a null-check bug.
    - Permanent residue on `creatio_2`: `UsrVerifyStory12` plus `UsrVerifyStory12Custom1` and `…Custom2`
      in package `Custom`. Versions are undeletable by design, which is why this ran on the second stand.
  - **The diff against a DESIGNER-created version is DONE, and the two are indistinguishable.** Run on
    `creatio_2` through the classic designer (served on the 10.2 line, entered from the process card).
    Observed first-hand: the SAVE split button offers `Save new version (Ctrl+Alt+N)` /
    `Save current version (Ctrl+Alt+S)`, and the former creates the version and then ASKS in a separate
    prompt — *Set the current version of the process "Story 12 verification" actual?* YES / NO. Answered
    NO, so the comparison is apples to apples. The designer's version came out as `…Custom3` — it
    CONTINUED the numbering, an independent confirmation that this build's allocator call and the
    platform's own agree on (root, package).
    - `SysSchemaProperty`: identical sets of nine properties and identical values except the number —
      `CreatedInVersion 10.2.20.0`, `IsActiveVersion False`, `IsCreatedInSvg True`,
      `IsInterpretable True`, `IsTracing False`, `StudioFreeProcessUrl` empty, `Tag Business Process`,
      `UseForceCompile False`, `Version 1 / 2 / 3`.
    - `SysSchema` row: identical `ParentId` (the ROOT's Id), `ExtendParent False`, same `SysPackageId`,
      `ManagerName`, `Caption`, `IsChanged True`, `IsLocked True`, `DenyExtending False`.
    - Two read traps worth keeping: `SysSchema` has no `CreatedInVersion` COLUMN (it exists only as a
      `SysSchemaProperty` row), and OData cannot `$filter` `SysSchemaProperty` by `SysSchemaId` even
      though the column projects fine. Property reads go through SQL.
