# Test Plan: sync-schemas — convergent "ensure" semantics

## Autonomous Mode Summary

Generated in autonomous mode (`--auto`) — no checkpoint pauses. Covers PRD/ADR/Stories 1–6 for
`sync-schemas-ensure-semantics` (ENG-93807). **33 unit test cases** (2 of them — TC-U-32 confirm-green, TC-U-33 gated on #910) (TC-U-*), **0 integration**
(justified — no local FS/DB path), **5 E2E test cases** (TC-E-*, manual, NOT in CI). Regression
surface (modified existing tests) is larger than the new-test surface: the Story-1 constructor
change to `SchemaSyncTool` forces every existing `SchemaSyncToolTests` case to be recompiled with a
mocked `ISchemaConvergenceService`. Residual risks: SM-03c p50 wall-time is a manual perf budget
(not automatable); all TC-E are manual gates (E2E not in CI).

**Feature**: sync-schemas convergent ("ensure") semantics — MCP-only tool, no CLI verb
**Stories**: [1](../stories/story-sync-schemas-ensure-1.md) · [2](../stories/story-sync-schemas-ensure-2.md) · [3](../stories/story-sync-schemas-ensure-3.md) · [4](../stories/story-sync-schemas-ensure-4.md) · [5](../stories/story-sync-schemas-ensure-5.md) · [6](../stories/story-sync-schemas-ensure-6.md)
**PRD**: [prd-sync-schemas-ensure-semantics.md](../prd/prd-sync-schemas-ensure-semantics.md)
**ADR**: [adr-sync-schemas-ensure-semantics.md](../adr/adr-sync-schemas-ensure-semantics.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-07-20
**Jira**: ENG-93807 (sub-task of ENG-93367; follow-up to PR #910 / ENG-93374)

---

## Scope

### In scope

- Unit coverage for the new `ISchemaConvergenceService` classifier (absent / in-package-subset /
  in-package-identical / cross-package-collision / column-type-conflict).
- Unit coverage for `SchemaSyncTool.ExecuteCreateSchema` and `ExecuteUpdateEntity` routed through
  convergence: all four `outcome` values for `create-lookup` (`created` / `reconciled` /
  `already-satisfied` / `collision`) and the two reachable outcomes for `update-entity`
  (`reconciled` / `already-satisfied`).
- The three residual-hole regressions (masked collision, replay-as-failure, no-`Name`-seed PK-conflict on replay) each
  proven with a discriminating assertion, not a behavior re-assertion.
- The consolidated ambiguous-failure re-run class (AC-03 / SM-01c / SM-02c counter-metric).
- The read-budget counter-metric in the round-trip formulation (OQ-04 / OI-01), NOT the literal
  AC-09 one-state-read wording.
- Additive wire-shape guard (`outcome` omitted `WhenWritingNull`).
- Story-4 unit-provable slice: guidance no-catch-up negative assertion, tool `[Description]`
  re-run-safety text, `WorkspaceTemplateGuidanceDriftTests` staying green.
- Heuristic-shrink: pre-emptive classification replaces reactive `TryGetCollisionInfo` (removed in
  Story 1 with its last caller). Story 6 owns only reconciling #910's resume-plan special-cases and
  preserving the #910 resume-plan result shape.
- E2E surface (`clio.mcp.e2e`, real `mcp-server`) for absent-create, existing-reconcile, replay
  idempotency, cross-package collision, and the updated `BuildSchemaSync` contract text.

### Out of scope (with reason)

- **Full-reconcile / delete-unlisted `update-entity`** — OQ-02 permanently excluded (data-loss risk,
  A-04). No TC exercises column deletion of unlisted columns; instead TC-U-18 asserts unlisted
  columns never enter the delta.
- **seed-data upsert-by-key / `uniqueness-key` arg** — OQ-01 deferred (Option E). No code path, no TC
  beyond the `Name`-bearing replay skip and the documented-non-convergence (no-`Name` PK-conflict) contract assertion.
- **`create-entity` dedicated `ensure-entity` scope** — FR-10 is "Could"; covered only where it falls
  out of the shared convergent create path (TC-U-07/08 exercise the shared path; no separate
  `create-entity` matrix).
- **Integration tier** — none. ADR §Test strategy: `sync-schemas` has no local FS/DB path; state-reads
  are mocked at the unit tier. Cross-process / real-Creatio behavior lives in the E2E tier (manual).
- **SM-03c p50 wall-time perf budget** — a manual/observational counter-metric, not an automatable
  assertion. Named as a residual risk, not a TC.
- **AC-09 literal "≤1 extra state-read per operation"** — superseded by OI-01 (self-contradictory on
  the reconcile path). No TC asserts it; TC-U-27/28/29 assert the round-trip formulation instead.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **Constructor break**: Story 1 adds `ISchemaConvergenceService` ctor dep → every existing `SchemaSyncToolTests` case (`new(commandResolver, ConsoleLogger.Instance)`) fails to compile | High | High | Modified-test sweep (see Regression Guard): supply a default fake convergence service classifying to `Create`/`Reconcile` so legacy routing/alias tests still exercise the write path |
| **Masked collision re-introduced** (residual hole a) — reactive probe leaks back in | Med | High | TC-U-11 / TC-U-27 assert `CreateEntitySchemaCommand.Execute` `Received(0)` on cross-package pre-existing schema (pre-emptive, not reactive) |
| **Replay reported as failure** (residual hole b) | Med | High | TC-U-15 / TC-U-25 assert `UpdateEntitySchemaCommand.Execute` `Received(0)` + `outcome: already-satisfied` |
| **No-`Name` seed rows non-convergent** (residual hole c — PK-conflict on replay, not silent dup) | Med | Med | TC-U-22 / TC-U-26 assert `SkippedRows` populated + `CreatedRows` empty on `Name`-bearing replay; no-`Name` non-convergence = doc/contract assertion (TC-U-23 / TC-U-30, Story 4) |
| **Wire-shape break** — `outcome` serialized when null breaks #910 consumers / ClioRing tolerance | Low | High | TC-U-13 asserts `WhenWritingNull` omission |
| **update-entity over-classification** — a per-column type conflict wrongly emitted as `outcome: collision` | Med | Med | TC-U-20 asserts modify-conflict → `success:false` + `Error:` (NOT `collision`); collision gate is package-match only (Story 1) |
| **Heuristic surface grows instead of shrinks** (SM-01c/SM-02c) — removing the reactive probe breaks the re-run class | Med | Med | Ambiguous-failure re-run class (TC-U-24..27) must stay green after `TryGetCollisionInfo` removal (owned by Story 1); Story 6 keeps it green while reconciling #910 resume special-cases |
| **Guidance still tells agents to hand-compose a catch-up batch** (SM-04c) | Med | Med | TC-U-30 negative assertion over the four guidance resources; `WorkspaceTemplateGuidanceDriftTests` green (TC-U-32) |
| **MCP E2E not in CI** | High | Med | All TC-E flagged manual-run; add to PR checklist (Story 5) |
| **#910 not yet merged** (A-06) — resume-plan baseline absent | Med | Med | Story 6 is gated on #910 merge; TC-U-33 (resume-plan shape) authored but gated — do not land the #910 reconciliation before baseline present |

---

## Regression scope — existing tests in `SchemaSyncToolTests.cs`

`clio.tests/Command/McpServer/SchemaSyncToolTests.cs` (~40 cases) is the primary regression surface.
The Story-1 constructor change breaks compilation of **all** of them; each must be updated. Two
buckets:

**Modified — semantics change, assertions must be rewritten:**

| Existing test | Why it changes |
|---------------|----------------|
| `SchemaSync_CreateLookup_Should_Route_Through_CreateEntitySchemaCommand` | Now routes via convergence; assert `outcome: created` and create-on-absent |
| `SchemaSync_CreateLookup_Should_Include_CollisionInfo_When_Schema_Exists_In_Different_Package` | Reactive probe → pre-emptive classification; `CreateEntitySchemaCommand.Execute` `Received(0)` |
| `SchemaSync_CreateLookup_Should_Not_Include_CollisionInfo_When_Schema_Not_Found` | Absent path now `outcome: created`, still no collision-info |
| `SchemaSync_UpdateEntity_Should_Route_Through_UpdateEntitySchemaCommand` | Now per-column reconcile against read state, not unconditional |
| `SchemaSync_CreateLookup_Should_Fail_When_Lookup_Registration_Fails` | `EnsureLookupRegistration` now runs on both created + already-exists paths (FR-02) |

**Modified — recompile only (inject a default fake convergence classifying to `Create`/`Reconcile`):**
all `SchemaSync_UpdateEntity_Coercion_*`, `SchemaSync_UpdateOperations_Add_*`, alias-mapping,
`SchemaSync_SeedRows_*`, `SchemaSync_Should_Stop_On_First_Failure`,
`SchemaSync_Should_Include_Detailed_Command_Error_When_Present`,
`SchemaSync_Should_Execute_Multiple_Operations_In_Order`,
`SchemaSync_Should_Assign_Messages_To_The_Correct_Operation`.

**Guard-green — must stay passing unchanged** (recompile only, no assertion change):
`SchemaSyncTool_Should_Advertise_Stable_Tool_Name`, `..._Should_Advertise_Safety_Metadata`,
`..._ShouldRouteToVirtualEntitiesGuidance_...`, `SchemaSync_Unknown_OperationType_Should_Return_Error`,
`SchemaSync_Should_Reject_Legacy_Operation_Field_Name`, the seed-row validation guards
(`..._Should_Fail_Before_Command_Resolution_...`).

**Test-double note.** Existing tests use `FakeCreateEntitySchemaCommand` +
`Substitute.For<IToolCommandResolver>()` + `Substitute.For<ILookupRegistrationService>()`. New reads
require `Substitute.For<ISchemaConvergenceService>()` returning a `SchemaConvergencePlan`, or (at the
service unit tier) mocked `FindEntitySchemaCommand.FindSchemas` → `EntitySchemaSearchResult` and
`GetEntitySchemaPropertiesCommand.GetSchemaProperties` → `EntitySchemaPropertiesInfo`.

---

## Fixture conventions (honor the existing shape)

`SchemaSyncTool` is an **MCP tool, not a `Command<TOptions>`** — do NOT force `BaseCommandTests` here.
Follow the existing `SchemaSyncToolTests` shape:

- `[TestFixture]` + `[Property("Module", "McpServer")]` on the class.
- `[Test]` + `[Category("Unit")]` + `[Description("...")]` on every method (never `[Category("UnitTests")]`).
- Direct construction: `new SchemaSyncTool(commandResolver, ConsoleLogger.Instance, convergenceService)`.
- NUnit 4 + FluentAssertions (every assertion carries a `because:`) + NSubstitute.
- AAA structure explicit. Cross-OS (no OS-specific paths).
- Naming: strict `MethodName_ShouldExpectedBehavior_WhenCondition` for **new** cases (do not copy the
  legacy `SchemaSync_CreateLookup_Should_...` snake pattern).

---

## Unit Tests (`clio.tests/`)

### Group A — `SchemaConvergenceServiceTests.cs` (new file) — classifier — Story 1 / U1

#### TC-U-01: `Classify_ShouldReturnCreateOutcome_WhenSchemaIsAbsent`
- **File**: `clio.tests/Command/McpServer/SchemaConvergenceServiceTests.cs`
- **Maps**: FR-01, AC-01, U1
- **Arrange**: `FindEntitySchemaCommand.FindSchemas` returns an empty result set.
- **Act**: `Classify(target)`.
- **Assert**: `plan.Outcome == Create` (because an absent schema must be created);
  `ColumnsToAdd`/`ColumnsToModify` empty; `Error` null.

#### TC-U-02: `Classify_ShouldReturnReconcileWithColumnsToAdd_WhenSchemaExistsInTargetPackageWithSubset`
- **Maps**: FR-01, AC-02, OQ-05
- **Arrange**: `FindSchemas` → one `EntitySchemaSearchResult` with `PackageName == target package`;
  `GetSchemaProperties` → a subset of the requested columns.
- **Assert**: `Outcome == Reconcile`; `ColumnsToAdd` equals exactly the missing columns (because only
  the delta is applied); no recreate implied.

#### TC-U-03: `Classify_ShouldReturnAlreadySatisfied_WhenSchemaExistsInTargetPackageWithAllColumns`
- **Maps**: FR-01, AC-02 (already-satisfied branch)
- **Assert**: `Outcome == AlreadySatisfied`; `ColumnsToAdd` empty (because nothing is missing).

#### TC-U-04: `Classify_ShouldReturnCollision_WhenSchemaExistsInDifferentPackage`
- **Maps**: FR-03, AC-04, OQ-05 (package-match gate)
- **Arrange**: `FindSchemas` → `EntitySchemaSearchResult` with `PackageName != target package`.
- **Assert**: `Outcome == Collision`; `CollisionPackageName` = the owning package; `Error` is a
  user-friendly `Error: {message}` string (because a cross-package name clash is a durable collision).

#### TC-U-04b: `Classify_ShouldReturnCollision_WhenSameNameSchemaInTargetPackageHasIncompatibleParent`
- **Maps**: FR-03, AC-04, OQ-05 (same-package parent/kind gate — the H2 branch)
- **Arrange**: `FindSchemas` → `EntitySchemaSearchResult` in the TARGET package whose
  `ParentSchemaName` is incompatible with the requested lookup (e.g. a `BaseEntity`-derived entity
  when a `BaseLookup` is requested).
- **Assert**: `Outcome == Collision` (NOT `Reconcile`); `Error` names the parent/kind mismatch;
  no lookup columns are planned and no `EnsureLookupRegistration` is planned (because a same-name,
  wrong-kind schema in the target package must fail explicitly, not be reconciled into a lookup).
- **Note**: the name/package/parent comparison uses `OrdinalIgnoreCase`. Document the known
  `ManagerName == "EntitySchemaManager"` blind spot (a same-name schema under a different manager is
  invisible to this gate) — out of scope for this TC, tracked as an ADR-documented limitation.

#### TC-U-05: `Classify_ShouldSurfaceColumnAsModify_WhenColumnTypeDiffersInTargetPackage`
- **Maps**: OQ-05 (column-shape is per-column, NOT a schema collision)
- **Arrange**: same-package schema; one requested column present with a different type.
- **Assert**: `Outcome == Reconcile` (NOT `Collision`); the differing column appears in
  `ColumnsToModify`, not treated as a whole-schema collision (because column-shape differences are
  per-column modify-conflicts by OQ-05).

#### TC-U-06: `Classify_ShouldReadSchemaExactlyOnce_WhenSchemaIsAbsent`
- **Maps**: FR-06, AC-BUDGET (create-only read count)
- **Assert**: `FindEntitySchemaCommand.FindSchemas` `Received(1)`; `GetSchemaProperties`
  `Received(0)` (because the create-only path needs one existence read and no column read).

#### TC-U-07: `Classify_ShouldReadSchemaTwice_WhenSchemaExistsInTargetPackage`
- **Maps**: FR-06, AC-BUDGET (reconcile read count, OQ-04/OI-01)
- **Assert**: `FindSchemas` `Received(1)` **and** `GetSchemaProperties` `Received(1)` (because the
  reconcile path legitimately does 2 server-side reads; the honest budget is 2, not 1 — OI-01).

### Group B — `SchemaSyncToolTests.cs` — `ExecuteCreateSchema` wiring — Story 1 / U1

#### TC-U-08: `ExecuteCreateSchema_ShouldReturnCreatedOutcomeAndEnsureRegistration_WhenSchemaAbsent`
- **Maps**: FR-01, FR-02, AC-01
- **Arrange**: convergence fake → `Create`.
- **Assert**: `CreateEntitySchemaCommand.Execute` `Received(1)`; `EnsureLookupRegistration`
  `Received(1)`; result `success: true`, `outcome == "created"`.

#### TC-U-09: `ExecuteCreateSchema_ShouldAddOnlyMissingColumnsWithoutRecreate_WhenSchemaExistsWithSubset`
- **Maps**: FR-01, AC-02, H1 (add-to-existing write path)
- **Arrange**: convergence fake → `Reconcile(ColumnsToAdd=[X])`.
- **Assert**: `CreateEntitySchemaCommand.Execute` `Received(0)` (no recreate — it is create-only, AC-02);
  the `UpdateEntitySchemaCommand` add-column operation is invoked for **exactly** the missing column(s)
  `[X]` and no others (the additive column-add mechanism, shared with `update-entity`); result
  `outcome == "reconciled"`, `success: true`.

#### TC-U-10: `ExecuteCreateSchema_ShouldEnsureLookupRegistrationOnAlreadyExistsPath_WhenRegistrationMissing`
- **Maps**: FR-02, AC-FR02 (the "moved out of exitCode==0 branch" guarantee)
- **Arrange**: convergence fake → `AlreadySatisfied` (schema exists, no columns missing).
- **Assert**: `EnsureLookupRegistration` `Received(1)` (because registration must be reconciled
  unconditionally, not gated on the freshly-created branch); `outcome == "already-satisfied"`.

#### TC-U-11: `ExecuteCreateSchema_ShouldFailWithCollisionAndNotCallCreate_WhenSchemaInDifferentPackage`
- **Maps**: FR-03, AC-04, AC-ERR — **residual hole (a): masked collision**
```csharp
[Test]
[Category("Unit")]
[Description("Cross-package pre-existing schema is surfaced pre-emptively as a collision without ever calling create.")]
public async Task ExecuteCreateSchema_ShouldFailWithCollisionAndNotCallCreate_WhenSchemaInDifferentPackage() {
    // Arrange
    var fakeCreate = new FakeCreateEntitySchemaCommand();
    var convergence = Substitute.For<ISchemaConvergenceService>();
    convergence.Classify(Arg.Any<SchemaConvergenceTarget>())
        .Returns(new SchemaConvergencePlan(SchemaConvergenceOutcome.Collision, [], [],
            CollisionPackageName: "OtherPkg", Error: "Error: schema 'UsrTodoStatus' already exists in package 'OtherPkg'"));
    var resolver = Substitute.For<IToolCommandResolver>();
    resolver.Resolve<CreateEntitySchemaCommand>(Arg.Any<CreateEntitySchemaOptions>()).Returns(fakeCreate);
    SchemaSyncTool tool = new(resolver, ConsoleLogger.Instance, convergence);
    SchemaSyncArgs args = new("dev", "UsrPkg",
        [new SchemaSyncOperation("create-lookup", "UsrTodoStatus")]);

    // Act
    SchemaSyncResponse response = await tool.SchemaSync(args);

    // Assert
    response.Results[0].Success.Should().BeFalse(
        because: "a cross-package name collision is a durable failure, not a success");
    response.Results[0].Outcome.Should().Be("collision",
        because: "the collision outcome discriminator must be surfaced to callers");
    response.Results[0].CollisionInfo.Should().NotBeNull(
        because: "the owning package must be machine-readable");
    response.Results[0].Error.Should().StartWith("Error:",
        because: "errors must be user-friendly Error: {message} strings");
    fakeCreate.CapturedOptions.Should().BeNull(
        because: "the collision must be detected pre-emptively — create is never attempted (no masked collision)");
}
```

#### TC-U-12: `SchemaSync_ShouldStopOnFirstFailure_WhenCreateCollisionDetected`
- **Maps**: AC-ERR (stop-on-first-failure preserved)
- **Arrange**: two ops; first classifies to `Collision`.
- **Assert**: second op never executes (its command `Received(0)`); response `success: false`.

#### TC-U-13: `SchemaSyncOperationResult_ShouldOmitOutcomeField_WhenOutcomeIsNull`
- **Maps**: FR-09, wire-shape additivity (ClioRing tolerance)
- **Act**: serialize a result with `Outcome == null` via `System.Text.Json`.
- **Assert**: the serialized JSON does not contain `"outcome"` (because the field is
  `JsonIgnoreCondition.WhenWritingNull` — existing wire shape preserved).

### Group C — `SchemaSyncToolTests.cs` — `ExecuteUpdateEntity` per-column reconcile — Story 2 / U2

> `update-entity` reaches only `reconciled` and `already-satisfied`; a per-column incompatibility is
> `success:false` + `Error` (modify-conflict), NOT `outcome:collision`, and there is no `created`.

#### TC-U-14: `ExecuteUpdateEntity_ShouldAddColumn_WhenRequestedColumnAbsent`
- **Maps**: FR-04, AC-FR04
- **Assert**: add path issued for the absent column; `outcome == "reconciled"`.

#### TC-U-15: `ExecuteUpdateEntity_ShouldModifyColumn_WhenRequestedColumnPresentButDifferent`
- **Maps**: FR-04, AC-FR04
- **Assert**: modify path issued for the differing column; `outcome == "reconciled"`.

#### TC-U-16: `ExecuteUpdateEntity_ShouldReturnAlreadySatisfiedAndNotCallUpdate_WhenColumnsIdentical`
- **Maps**: FR-05, AC-05 — **residual hole (b): replay-as-failure**
- **Arrange**: convergence fake → `AlreadySatisfied` (all requested columns present + identical).
- **Assert**: `UpdateEntitySchemaCommand.Execute` `Received(0)` (because an already-applied change on
  replay must not re-issue and must not be reported as a failure); `success: true`,
  `outcome == "already-satisfied"`.

#### TC-U-17: `ExecuteUpdateEntity_ShouldTreatRemoveAsSuccess_WhenColumnAlreadyAbsent`
- **Maps**: FR-04, AC-06
- **Assert**: `success: true` with no remove mutation issued (because `remove` means "ensure absent"
  and the column is already absent).

#### TC-U-18: `ExecuteUpdateEntity_ShouldIssueRemove_WhenRequestedRemoveColumnPresent`
- **Maps**: FR-04
- **Assert**: remove mutation issued for the present column; `outcome == "reconciled"`.

#### TC-U-19: `ExecuteUpdateEntity_ShouldLeaveUnlistedColumnsOutOfDelta_WhenReconciling`
- **Maps**: FR-04, AC-07 (no full-reconcile deletion)
- **Arrange**: schema has columns `[A, B]`; request names only `A`.
- **Assert**: the emitted mutation set never references `B` (because unlisted columns are untouched;
  no delete-unlisted — A-04/OQ-02).

#### TC-U-20: `ExecuteUpdateEntity_ShouldEmitExactlyComputedDelta_WhenColumnStatesMixed`
- **Maps**: FR-04, AC-FR04
- **Arrange**: one absent (→add), one different (→modify), one identical (→no-op).
- **Assert**: exactly two mutations emitted (add + modify), the identical column produces none
  (because the emitted set equals exactly the computed delta).

#### TC-U-21: `ExecuteUpdateEntity_ShouldFailWithModifyConflictNotCollision_WhenColumnTypeIncompatible`
- **Maps**: FR-04, AC-ERR (modify-conflict vs collision distinction)
- **Assert**: `success: false`, `Error:` present, `outcome != "collision"` (because a per-column
  incompatibility is a modify-conflict, not a whole-schema collision — that gate is package-match only).

### Group D — seed-data contract — Story 3 / U3

#### TC-U-22: `ExecuteSeedData_ShouldReportSkippedRowsAndNoDuplicates_WhenReplayedWithNameBearingRows`
- **Maps**: FR-08, AC-08 — **residual hole (c): seed replay (Name-dedup side)**
- **Arrange**: `DataBindingDbCommand` (`CreateBinding`→`ProcessRows`) against a schema that HAS a `Name`
  column, replaying rows that carry a `Name` already present in the target binding.
- **Assert**: `SkippedRows` populated with the already-present-by-`Name` rows; `CreatedRows` empty
  (because the seed path dedups by `Name` — a `Name`-bearing already-present row is skipped, no
  duplicate mutation). This asserts the REAL `Name`-dedup behavior, NOT a mocked "skip by `Id`" fiction.

#### TC-U-23: `SeedDataContract_ShouldDocumentNoNameRowsAsNonConvergent_WhenSchemaKeyedByName`
- **Maps**: FR-08, AC-08 (no-`Name` non-convergence = **documented contract**)
- **Note**: A row without a `Name` (or a schema without a `Name` column) falls to the `InsertEntityRow`
  branch with its explicit `Id` and causes a **primary-key conflict on replay** — it is NOT deduped.
  This is the documented, deliberately-limited contract (OQ-01 deferred), not a defect. Assert the
  *contract text* ("a row is replay-safe only when the target schema has a `Name` column AND the row
  carries a `Name`; rows without a `Name` are non-convergent — a stable-`Id`, no-`Name` row PK-conflicts
  on replay"), verified against the guidance/contract string (see TC-U-30 scope). No mutation-dedup
  behavior is asserted for no-`Name` rows because none exists.

### Group E — ambiguous-failure re-run class (consolidated) — Story 5 / U5 — SM-01c/SM-02c counter

> This class staying green **is** the SM-01c/SM-02c counter-metric. It must remain green after Story 6
> removes `TryGetCollisionInfo` and the redundant #910 resume special-cases.

#### TC-U-24: `ReRun_ShouldReturnAlreadySatisfied_WhenCreateLookupMutationAlreadyApplied`
- **Maps**: AC-03 (thesis), FR-05, SM-01/SM-02
- **Assert**: identical `create-lookup` re-run → `success: true`, `outcome == "already-satisfied"`,
  `CreateEntitySchemaCommand.Execute` `Received(0)` (no duplicate mutation).

#### TC-U-25: `ReRun_ShouldReturnAlreadySatisfied_WhenUpdateEntityMutationAlreadyApplied`
- **Maps**: AC-03, FR-05
- **Assert**: identical `update-entity` re-run → `success: true`, `outcome == "already-satisfied"`,
  `UpdateEntitySchemaCommand.Execute` `Received(0)`.

#### TC-U-26: `ReRun_ShouldSkipRowsNotDuplicate_WhenSeedDataReplayedWithNameBearingRows`
- **Maps**: AC-03, AC-08
- **Assert**: identical `seed-data` re-run with `Name`-bearing rows (target schema has a `Name` column)
  → `SkippedRows` populated, `CreatedRows` empty (no duplicate rows, via the `Name`-dedup path). Rows
  without a `Name` are out of scope for this convergence assertion (documented non-convergent — TC-U-23).

#### TC-U-27: `ReRun_ShouldSurfaceCollisionNotMaskSuccess_WhenCrossPackageSchemaPreexists`
- **Maps**: AC-03, AC-04 (the masked-collision hole must stay closed on replay)
- **Assert**: `success: false`, `outcome == "collision"`, `CreateEntitySchemaCommand.Execute`
  `Received(0)` (because pre-emptive classification never masks a durable collision as success).

### Group F — read-budget counter-metric — Story 5 / U5 — round-trip formulation (OQ-04/OI-01)

> **Tier honesty**: a unit test proves server-side read COUNT + absence of a verify read-back. "No
> added MCP round-trip" is by construction (reads are DataService calls inside the single batch call)
> and is observed at the E2E tier (TC-E-*), not unit-provable. Do NOT assert the literal AC-09
> "≤1 extra state-read per operation" — OI-01 flags it as self-contradictory for the reconcile path.

#### TC-U-28: `ExecuteCreateSchema_ShouldPerformOneServerSideReadAndNoVerifyReadBack_OnCreateOnlyPath`
- **Maps**: FR-06, AC-BUDGET
- **Assert**: exactly 1 existence read (`FindEntitySchemaCommand`); no column read; no post-write
  read-back after create (because the create-only path is 1 read/op and correctness comes from
  idempotent re-run, not a second read).

#### TC-U-29: `ExecuteCreateSchema_ShouldPerformTwoServerSideReadsAndNoVerifyReadBack_OnReconcilePath`
- **Maps**: FR-06, AC-BUDGET, OQ-04/OI-01
- **Assert**: exactly 2 reads (`FindEntitySchemaCommand` + `GetEntitySchemaPropertiesCommand`); no
  post-write verify read-back (because the reconcile path legitimately does 2 server-side reads).

### Group G — contract / guidance unit-provable slice — Story 4 / U4

#### TC-U-30: `GuidanceResources_ShouldNotInstructHandComposedCatchUpBatch_ForConvergentOps`
- **File**: extend `clio.tests/Command/McpServer/McpGuidanceResourceTests.cs` (or a new focused fixture)
- **Maps**: FR-07, SM-04c, AC-GUIDANCE
- **Arrange**: read the four sync-schemas-touching guidance resources
  (`AppModelingGuidanceResource`, `ExistingAppMaintenanceGuidanceResource`,
  `DataBindingsGuidanceResource`, `AgentExecutionGuidanceResource`).
- **Assert**: none contains hand-composed catch-up-batch language for the convergent ops; each states
  "re-submit the identical batch" is the safe recovery path (negative + positive assertion). Also
  assert the `Name`-keyed seed contract phrase is present (backs TC-U-23).

#### TC-U-31: `SchemaSyncToolDescription_ShouldStateReRunSafety_WhenInspected`
- **Maps**: AC-DESC
- **Assert**: the `SchemaSyncTool.SchemaSync` `[Description]` states a completed batch is safe to
  re-submit (no hand-composed catch-up batch needed) — reflection over the attribute, mirroring the
  existing `SchemaSyncTool_ShouldRouteToVirtualEntitiesGuidance_...` pattern.

#### TC-U-32: `WorkspaceTemplateGuidanceDrift_ShouldStayGreen_WhenNoOperationRenamed`
- **File**: existing `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs`
- **Maps**: AC-TPL (OQ-03 → no rename)
- **Assert**: existing drift test still passes (no `create-lookup`/`update-entity` rename → no
  `McpToolCompatibilityCatalog` churn). Reference-only: confirm green, no new assertion.

### Group H — #910 integration / heuristic-shrink — Story 6 / U6 (gated on #910 merge, A-06)

#### TC-U-33: `SchemaSync_ShouldPreserveResumePlanResultShape_WhenRebasedOn910`
- **Maps**: FR-09, AC-SHAPE
- **Gate**: authored but landed only after #910 merges (A-06).
- **Assert**: the resume-plan / partial-result output fields introduced by #910 are still present and
  populated (existing consumers not broken); the additive `outcome` field coexists with them.

---

## Integration Tests (`clio.tests/`)

**None — justified.** Per ADR §Test strategy: `sync-schemas` has no local file-system or DB path; all
state-reads are mocked at the unit tier. Cross-process and real-Creatio behavior is covered at the
E2E tier (manual). Do not manufacture integration TCs to fill the template.

---

## E2E Tests (`clio.mcp.e2e/`) — real `clio mcp-server`, MCP protocol — Story 5 / U5

> **⚠️ CI status**: `clio.mcp.e2e` is **NOT in CI** — every case below is a manual gate. Add to the
> PR checklist. Files already exist from PR #910 (`SchemaSyncToolE2ETests.cs`,
> `ToolContractGetToolE2ETests.cs`) — extend them; follow the existing harness (Sandbox vs
> NoEnvironment fixture split). `[Category("E2E")]` on every method.

#### TC-E-01: `SchemaSync_ShouldCreateAndSucceed_WhenSchemaAbsent`
- **Tool**: `sync-schemas`
- **Input**: `create-lookup` for a schema that does not exist.
- **Expected**: `success: true`, `outcome: created`, Lookups registration ensured.
- **File**: `clio.mcp.e2e/SchemaSyncToolE2ETests.cs`

#### TC-E-02: `SchemaSync_ShouldAddOnlyMissingColumns_WhenSchemaExistsInTargetPackage`
- **Tool**: `sync-schemas`
- **Input**: `create-lookup` for a pre-existing in-package schema missing one column.
- **Expected**: only the missing column added; `outcome: reconciled`; no recreate.

#### TC-E-03: `SchemaSync_ShouldReturnAlreadySatisfiedOnReplay_WhenBatchAlreadyApplied` (AC-03 thesis)
- **Tool**: `sync-schemas`
- **Input**: run a batch, then re-run the identical batch.
- **Expected output**: second run `success: true`, `outcome: already-satisfied`, zero new mutations.

#### TC-E-04: `SchemaSync_ShouldFailWithCollision_WhenSchemaInDifferentPackage`
- **Tool**: `sync-schemas`
- **Input**: `create-lookup` whose name collides with a schema in a different package.
- **Expected output**: `success: false`, `outcome: collision`, `collision-info` naming the owning package.

#### TC-E-05: `ToolContract_ShouldDescribeConvergentSemantics_WhenBuildSchemaSyncRead` (AC-CONTRACT/AC-E2E)
- **File**: `clio.mcp.e2e/ToolContractGetToolE2ETests.cs`
- **Expected**: `BuildSchemaSync` contract text describes the convergent superset, the four `outcome`
  values, the collision failure shape, and the `Name`-keyed seed-data contract.

---

## Regression Guard

Tests that MUST pass after this feature ships:

| Test file | Test name | Why at risk |
|-----------|-----------|------------|
| `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` | ambiguous-failure re-run class (TC-U-24..27) | staying green IS the SM-01c/SM-02c counter — guards Story 6 heuristic removal |
| `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` | `SchemaSync_Should_Stop_On_First_Failure` | stop-on-first-failure contract must survive convergence |
| `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` | seed-row validation guards (`..._Should_Fail_Before_Command_Resolution_...`) | seed path is untouched but shares the batch loop + ctor |
| `clio.tests/Command/McpServer/SchemaSyncToolTests.cs` | alias/coercion tests (`..._Coercion_*`, `..._UpdateOperations_Add_*`) | share the `update-entity` write path now gated behind convergence |
| `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` | resident-or-bridged oracle | no rename expected → must stay green (AC-TPL) |
| `clio.tests/Command/McpServer/McpToolCompatibilityCatalogTests.cs` | catalog integrity | OQ-03 no-rename → no new catalog entry should appear |
| `clio.tests/Command/McpServer/ToolContractGetToolTests.cs` | `BuildSchemaSync` unit assertions (if any) | contract text changes in Story 4 |
| `clio.tests/Command/McpServer/DataBindingDbToolTests.cs` | seed/data-binding behavior | shares `DataBindingDbCommand` (`CreateBinding`→`ProcessRows`) skip-by-`Name` behavior asserted in TC-U-22 |

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit | 33 (TC-U-01..33; TC-U-32 confirm-green, TC-U-33 gated on #910) | ~40 (all existing `SchemaSyncToolTests` recompiled; 5 semantics-rewritten) | Modified surface > new surface |
| Integration | 0 | 0 | None — justified (no local FS/DB path) |
| E2E | 5 (TC-E-01..05) | extend 2 existing e2e files | Manual only — NOT in CI |

Per-story mapping: Story 1 → TC-U-01..13, 28..29; Story 2 → TC-U-14..21; Story 3 → TC-U-22..23;
Story 4 → TC-U-30..32; Story 5 → TC-U-24..29 + TC-E-01..05; Story 6 → TC-U-33 + re-run class stays green.

---

## FR / AC → TC traceability

| PRD FR / AC | Story | TC |
|-------------|-------|-----|
| FR-01, AC-01 | 1 | TC-U-01, TC-U-08, TC-E-01 |
| FR-01, AC-02 | 1 | TC-U-02, TC-U-03, TC-U-09, TC-E-02 |
| FR-02, AC-FR02 | 1 | TC-U-08, TC-U-10 |
| FR-03, AC-04 | 1 | TC-U-04, TC-U-04b, TC-U-11, TC-U-27, TC-E-04 |
| FR-04, AC-05 | 2 | TC-U-16 |
| FR-04, AC-06 | 2 | TC-U-17 |
| FR-04, AC-07 | 2 | TC-U-19 |
| FR-04, AC-FR04 | 2 | TC-U-14, TC-U-15, TC-U-18, TC-U-20 |
| FR-05, AC-03 (thesis) | 5 | TC-U-24, TC-U-25, TC-U-26, TC-U-27, TC-E-03 |
| FR-06, AC-09→OI-01 budget | 1/5 | TC-U-06, TC-U-07, TC-U-28, TC-U-29 |
| FR-07, SM-04, SM-04c | 4 | TC-U-30, TC-U-31, TC-U-32, TC-E-05 |
| FR-08, AC-08 (OQ-01) | 3 | TC-U-22, TC-U-23, TC-U-26 |
| FR-09, AC-SHAPE/AC-SHRINK | 6 | TC-U-13, TC-U-33, re-run class green |
| OQ-05 (collision identity) | 1/2 | TC-U-04, TC-U-04b, TC-U-05, TC-U-21 |
| AC-ERR (stop-on-first-failure) | 1/2 | TC-U-11, TC-U-12, TC-U-21 |

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] Integration tier explicitly recorded as "none — justified" (no local FS/DB path)
- [ ] Regression guard tests green (esp. the ambiguous-failure re-run class after Story 6 removal)
- [ ] All existing `SchemaSyncToolTests` cases recompiled against the new ctor; 5 semantics-changed
      cases rewritten (see Regression scope)
- [ ] MCP E2E tests (TC-E-01..05) implemented and flagged NOT-in-CI; manual run recorded in the PR
- [ ] Read-budget TCs assert the round-trip formulation (1 read create-only / 2 reads reconcile, no
      verify read-back) — NOT the literal AC-09 one-state-read wording (OI-01)
- [ ] `outcome` wire-shape additivity asserted (`WhenWritingNull` omission — TC-U-13)
- [ ] Test naming follows `MethodName_ShouldBehavior_WhenCondition`; every assertion carries `because:`;
      every method has `[Description]`; fixtures carry `[Property("Module","McpServer")]`
- [ ] Cross-OS: no OS-specific paths in any TC
- [ ] PR includes the test files in the changed files list

## Residual coverage risks

- **SM-03c (p50 batch wall-time)** — a perf budget, not automatable; must be measured manually against
  the pre-change tool. Uncovered by automated TCs by design.
- **"No added MCP round-trip"** — by construction (server-side DataService reads inside the single
  batch call); unit tests prove read-count + no-verify-readback only. End-to-end round-trip absence is
  observed at the (manual) E2E tier, not CI-enforced.
- **All TC-E** — manual gates; `clio.mcp.e2e` is not in CI, so E2E regressions are caught only on a
  manual live-stand run. **Compensating control:** AC-03 (idempotent-replay thesis) and AC-09
  (round-trip budget) are verifiable ONLY at the E2E tier, so per Story 5's DoD the manual E2E run
  (`SchemaSyncToolE2ETests`) is a **HARD, RECORDED merge gate** — the PR MUST NOT merge without the
  recorded manual E2E result attached. This is the deliberate compensating control for the absence of
  CI E2E; it is not merely "flagged".
- **Story 6 (#910 integration)** — gated on PR #910 / ENG-93374 merging first (A-06); TC-U-33 and the
  heuristic-removal validation cannot run until the resume-plan baseline is present.
- **SM-04c negative assertion (TC-U-30)** — guards only the four named guidance resources; new guidance
  surfaces added later would not be covered unless the assertion is extended.
