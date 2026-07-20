# Story 1: Verify requested columns before resuming a same-package create-lookup

**Feature**: sync-schemas-verify-resume
**FR coverage**: FR-01, FR-02, FR-04, FR-07, FR-08
**PRD**: [prd-sync-schemas-verify-resume.md](../prd/prd-sync-schemas-verify-resume.md)
**ADR**: [adr-sync-schemas-verify-resume.md](../adr/adr-sync-schemas-verify-resume.md)
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

developer (AI coding agent) using the `sync-schemas` MCP tool

## I want

the same-target-package create-lookup resume branch to read the existing schema's actual columns and only resume when the requested columns are already present (or none were requested)

## So that

an interrupted create legitimately resumes, while the different-package guard and column-intent are respected instead of blindly forcing success

---

## Acceptance Criteria

- [ ] **AC-01** — Given a create-lookup op with EMPTY `op.Columns` and a same-target-package collision, when `ExecuteCreateSchema` runs, then it skips re-create via the FR-01 fast-path (no extra read round-trip), completes lookup registration, and returns `success:true`, `status:"completed"`.
- [ ] **AC-02** — Given a create-lookup op with NON-EMPTY `op.Columns` where every requested column already exists (case-insensitive) on the target-package schema, when the op runs, then `VerifyRequestedColumns` returns `Verified`, the op skips re-create, completes registration, and returns `success:true`, `status:"completed"`.
- [ ] **AC-02b (FR-08)** — Given an existing schema that contains the requested columns plus extra unrelated columns, when verification runs, then the subset check treats the op as `Verified` (extra existing columns are allowed; comparison is presence-by-name, case-insensitive).
- [ ] **AC-05-guard** — Given a create-lookup op whose collision resolves to a DIFFERENT package than `args.PackageName`, when the op runs, then the same-package guard (`string.Equals(collision.ExistingPackageName, args.PackageName, OrdinalIgnoreCase)`) is false, `VerifyRequestedColumns` is NOT called, create is left as a genuine failure (`Success == false`), and registration is not invoked. (Behavior; the dedicated regression test is Story 3 / FR-09.)
- [ ] **AC-ERR** — Given a create-lookup that fails for a non-collision reason (no same-name schema found at all), when the op runs, then it returns `success:false` with the honest error and non-zero op outcome, unchanged from current behavior (the resume sub-branch is never entered because `collision is null`).

## Implementation Notes

Key file: `clio/Command/McpServer/Tools/SchemaSyncTool.cs` — rework the same-package resume sub-branch inside `ExecuteCreateSchema` (currently L326–342 / L330–342). The single collision probe (`FindEntitySchemaCommand` via `commandResolver`) is already run once; **reuse** it — do NOT add a second probe.

Add the private helper and enum exactly as specified in the ADR "Key interfaces / contracts":

```csharp
private enum ColumnVerification { Verified, Missing, Unverified }

private (ColumnVerification Outcome, IReadOnlyList<string> Missing, Exception? ProbeFault)
    VerifyRequestedColumns(SchemaSyncOperation op, SchemaSyncArgs args)
```

- Requested column identity: `op.Columns` → `CreateEntitySchemaColumnArgs.ResolveName()`, filter blank names.
- Read path (single read-only round-trip, NOT wrapped in `RunAttempts` — one probe like `TryGetCollisionInfo`): resolve `GetEntitySchemaPropertiesCommand` through the same `commandResolver` and call `.GetSchemaProperties(readOptions)` with `GetEntitySchemaPropertiesOptions { Environment = args.EnvironmentName, SchemaName = op.SchemaName }`. Reads the merged/effective schema (no `--package` filter): lookup columns are own-to-target-package, so name-presence in the merged view is equivalent (ADR).
- Compare with `new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase)` (FR-08 case-insensitive subset). All requested present → `Verified`; ≥1 absent → `Missing` (with the missing list). This story wires the `Verified` outcome; `Missing`/`Unverified` routing is Story 2.

Resume sub-branch guard (ADR "Resume sub-branch shape"):
```csharp
if (collision is not null
    && string.Equals(collision.ExistingPackageName, args.PackageName, StringComparison.OrdinalIgnoreCase)) {
    bool hasColumns = op.Columns?.Any() == true;
    ColumnVerification outcome = ColumnVerification.Verified; // empty-columns → resume as today (FR-01)
    if (hasColumns) { (outcome, missing, probeFault) = VerifyRequestedColumns(op, args); }
    // Verified → execution = new OperationExecution(0, null, [info note], createExecution.Attempts); schemaApplied = true;
}
```
- FR-07: the `string.Equals(...ExistingPackageName..., OrdinalIgnoreCase)` guard MUST stay intact — a different-package collision never enters the sub-branch, so create stays failed and registration is not invoked.
- Verified/empty-columns branch replaces the failed create's `execution` with a fresh `OperationExecution(0, null, [InfoMessage("... already exists ... skipping re-create and completing lookup registration.")], createExecution.Attempts)` and sets `schemaApplied = true` (drops the failed-create Error lines — foreshadows FR-06, fully asserted in Story 2).

Pattern to follow: the existing collision-probe usage of `commandResolver.Resolve<FindEntitySchemaCommand>(...)`; mirror it for `GetEntitySchemaPropertiesCommand`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | AC-01 empty-columns resume (no read performed); AC-02 all-present resume returns `success:true`; AC-08 extra-existing-columns still `Verified`; AC-ERR non-collision failure unchanged | `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` |
| Integration `[Category("Integration")]` | None — behavior is exercised through the resolver seam with substitutes (ADR) | — |
| E2E `[Category("E2E")]` | Optional manual scenario; MCP E2E is NOT in CI — manual only | `clio.mcp.e2e/SchemaSyncToolE2ETests.cs` |

Test seam (ADR): resolve a real `GetEntitySchemaPropertiesCommand(stubManager, logger)` where `stubManager = Substitute.For<IRemoteEntitySchemaColumnManager>()`; `stubManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>()).Returns(info)`. Mirrors the existing `FakeFindEntitySchemaCommand` pattern — no production `virtual` needed.

Test naming: `MethodName_ShouldBehavior_WhenCondition` — e.g. `ExecuteCreateSchema_ShouldResumeWithoutReading_WhenColumnsEmptyAndSamePackageCollision`, `ExecuteCreateSchema_ShouldResume_WhenAllRequestedColumnsPresent`.

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [ ] No new CLI flags introduced (internal MCP behavior only)
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Existing `SchemaSync_CreateLookup_Should_Complete_Registration_When_Schema_Already_Exists_In_Target_Package` (empty-columns) still passes via the FR-01 fast-path
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
