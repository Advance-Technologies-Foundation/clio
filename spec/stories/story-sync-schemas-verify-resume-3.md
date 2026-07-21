# Story 3: Different-package regression test pinning the collision guard

**Feature**: sync-schemas-verify-resume
**FR coverage**: FR-09
**PRD**: [prd-sync-schemas-verify-resume.md](../prd/prd-sync-schemas-verify-resume.md)
**ADR**: [adr-sync-schemas-verify-resume.md](../adr/adr-sync-schemas-verify-resume.md)
**Status**: ready-for-dev
**Size**: S (< 2h)

---

## As a

QA engineer protecting the `sync-schemas` create-lookup behavior

## I want

a dedicated negative regression unit test that pins the different-package collision guard

## So that

a future refactor cannot silently start skipping create (or invoking registration) for a foreign-package collision

---

## Acceptance Criteria

- [ ] **AC-05** — Given a create-lookup op whose collision resolves to a DIFFERENT package than `args.PackageName`, when the op runs, then create is invoked EXACTLY ONCE, `Success == false`, and the registration service `DidNotReceive().EnsureLookupRegistration(...)`.
- [ ] **AC-05-no-verify** — Given the same different-package collision, when the op runs, then `VerifyRequestedColumns` is NOT reached (the same-package guard short-circuits), i.e. no `GetEntitySchemaPropertiesCommand`/`GetSchemaProperties` read is issued for that op.

## Implementation Notes

Key file: `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` — this is a test-only story; no production change (the guard itself ships in Story 1 / FR-07).

Per the ADR "FR-09 note": the existing `SchemaSync_CreateLookup_Should_Include_CollisionInfo_When_Schema_Exists_In_Different_Package` test only asserts collision-info is populated — it does NOT pin the guard behavior. Add a NEW dedicated regression test asserting:
- the create/execute path is invoked exactly once (`Received(1)` on the create seam),
- `result.Success == false`,
- the registration service `DidNotReceive().EnsureLookupRegistration(...)`.

Set up the collision so `collision.ExistingPackageName != args.PackageName`, so the `string.Equals(collision.ExistingPackageName, args.PackageName, OrdinalIgnoreCase)` guard is false and the resume sub-branch is never entered. Optionally assert the column-read seam (`stubManager.DidNotReceive().GetSchemaProperties(...)`) to prove no verification round-trip occurs for a foreign-package collision (AC-05-no-verify).

Pattern to follow: the existing `SchemaSync_CreateLookup_Should_Include_CollisionInfo_When_Schema_Exists_In_Different_Package` test's fixture/arrange; reuse the same substitutes (`FakeFindEntitySchemaCommand`, registration service substitute, create seam).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | AC-05 different-package collision → create invoked once, `Success == false`, `DidNotReceive().EnsureLookupRegistration(...)`; AC-05-no-verify no `GetSchemaProperties` read | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |
| Integration `[Category("Integration")]` | None | — |
| E2E `[Category("E2E")]` | None | — |

Test naming: `MethodName_ShouldBehavior_WhenCondition` — e.g. `ExecuteCreateSchema_ShouldNotSkipCreateOrRegister_WhenCollisionInDifferentPackage`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] Unit test added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Test is a distinct regression from the pre-existing collision-info-population test
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
