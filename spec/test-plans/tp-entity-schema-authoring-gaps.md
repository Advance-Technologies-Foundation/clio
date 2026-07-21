# Test Plan: Close Entity-Schema Authoring Gaps — Primary-Display Column, Inherited-Column Caption Override, Color Type

**Feature**: entity-schema-authoring-gaps
**Jira**: [ENG-93040](https://creatio.atlassian.net/browse/ENG-93040) (epic ENG-85256)
**Stories**:
[story-1 (Color type)](../stories/story-entity-schema-authoring-gaps-1.md),
[story-2 (set-entity-schema-properties)](../stories/story-entity-schema-authoring-gaps-2.md),
[story-3 (inherited caption override)](../stories/story-entity-schema-authoring-gaps-3.md),
[story-4 (MCP guidance/prompt/routing + E2E)](../stories/story-entity-schema-authoring-gaps-4.md)
**PRD**: [prd-entity-schema-authoring-gaps.md](../prd/prd-entity-schema-authoring-gaps.md)
**ADR**: [adr-entity-schema-authoring-gaps.md](../adr/adr-entity-schema-authoring-gaps.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-07-09

---

## Scope

### In scope

- **Capability 1 — Primary-display column** via the new `set-entity-schema-properties` command/tool: resolve
  own-or-inherited target by name → `uId`, set the nested `primaryDisplayColumn` object (modern contract, NOT
  legacy flat `primaryDisplayColumnUId`), and verify by readback (`get-entity-schema-properties`
  `primary-display-column-name`). Readback verification converts the A-01 legacy-version silent-no-op into a
  clear error.
- **Capability 2 — Inherited-column caption/description override** on a replacing/child schema:
  caption-only (and description-only) modify allowed and applied in place on the `InheritedColumns` entry with
  `uId`/`name`/`type` unchanged; parent caption untouched; name/type/flag mutations of an inherited column
  still rejected with a clear error. Includes the `VerifyColumnMutation` fix (accept `Columns` OR
  `InheritedColumns`; effective-culture caption match with en-US fallback).
- **Capability 3 — Color type (dataValueType 18)**: `create-entity-schema` / `modify-entity-schema-column`
  accept the named `Color` token only (raw `18` stays internal); `["color"] = 18` in
  `SupportedDataValueTypes`; `18 => "Color"` friendly readback; Color is NOT text-like — multiline / accent /
  format-validation / mask are not enabled/accepted.
- The MCP surface for all three capabilities (tool arg mapping, descriptions, guidance/prompt/routing text) at
  the unit level, plus the consolidated `clio.mcp.e2e` suite (manual).
- Regression guarding of the existing entity-schema create/modify/update unit + E2E suites, the shared-pipeline
  extraction (Story 2), and the `VerifyColumnMutations` change.

### Out of scope (with reason)

- Broader schema-level property editing beyond primary-display (schema caption, description, parent
  reassignment) — PRD Non-goals; only the extensible bag shape is validated (FR-11).
- Mutating **name, type, or flags** of an inherited column — deliberately rejected; only the rejection path is
  tested, not any success path.
- Color hex-string value/format validation beyond platform enforcement — PRD Non-goals.
- Downstream `creatio-ai-app-development-toolkit` skill-doc updates (OQ-02) — tracked separately.
- Adding a `[FeatureToggle]` for these surfaces — ADR ships them **enabled**; no toggle-gating tests required.
- Legacy flat `primaryDisplayColumnUId` transport support — not implemented; the plan only asserts the readback
  guard fires when a target version no-ops (A-01), not a legacy-write path.

---

## Risk Assessment

| ID | Risk | Likelihood | Impact | Mitigation (test) |
|----|------|-----------|--------|-------------------|
| R-01 (A-01) | A target Creatio version expects the legacy flat `primaryDisplayColumnUId` and silently no-ops the modern nested `primaryDisplayColumn` object → primary-display never set, no error | Med | High | TC-U-05 asserts readback mismatch (`reloadedSchema.PrimaryDisplayColumn?.Name != <C>`) raises a clear error; TC-E2E-01 exercises the full round-trip live |
| R-02 (A-02/OQ-03) | Inherited-verify culture mismatch — reloaded caption compared in the wrong culture falsely fails (or falsely passes) an override | Med | Med | TC-U-10 asserts effective-culture caption match with en-US fallback in `VerifyColumnMutation`; TC-U-08 asserts caption persisted under the resolved culture |
| R-03 (A-03) | Color misclassified as text-like → multiline/accent/format-validated/masked wrongly enabled, malformed column | Med | High | TC-U-13 (`IsTextLikeDataValueType(18)` false), TC-U-15 (Color build rejects/omits text-only options); ADR alt G explicitly rejected — do NOT add 18 to `TextDataValueTypes` |
| R-04 | Shared-pipeline extraction (Story 2) changes existing `ModifyColumns` save/publish/verify behavior (publish-once, OData-rebuild ordering, warning-on-rebuild-fault) | Med | High | Regression: existing `ModifyColumns_PublishesAndRebuildsOnce_ForMultiOperationBatch`, `ModifyColumn_PublishesAndRequestsODataRebuild_AfterSaving`, `ModifyColumn_SucceedsWithWarning_WhenODataRebuildRequestFails`, `ModifyColumn_Throws_WhenPublishFails` must stay green unchanged |
| R-05 | `VerifyColumnMutation` change (accept `InheritedColumns`) breaks own-column verification | Med | High | TC-U-11 asserts own-column Modify verification still passes via `Columns`; existing own-column modify tests stay green |
| R-06 | Story-3 guard relaxation silently loosens rejection of non-caption inherited mutations (name/type/flags leak through) | Low | High | TC-U-07 parameterized over every mutating field in AC-04; existing inherited-rejection test rewritten (see Regression Guard note) |
| R-07 | New verb not wired (CommandOption / Program dispatch / DI) → command unreachable; or CLIO005 flags a dead registration | Low | Med | TC-U-16 (command delegates to manager, resolved from container); build-time CLIO001/CLIO005 gates; `BaseCommandTests` fixture resolves SUT from DI |
| R-08 | MCP tool arg mapping drifts from options (args → options mismatch) or Color/inherited notes missing from descriptions | Med | Med | TC-U-17..TC-U-19 mapping + description assertions; TC-E2E-* exercise the live MCP path |
| R-09 | Batch `update-entity-schema` mixing a caption override with a disallowed inherited change partially applies before failing | Low | High | TC-U-09 asserts the whole batch is rejected (no partial save) when one op is a disallowed inherited mutation |

---

## Unit Tests (`clio.tests/`)

All unit tests: `[Category("Unit")]` (never `UnitTests`), AAA structure, a `because` on every assertion, a
`[Description]` on every test, NUnit 4 + FluentAssertions + NSubstitute. Command tests derive from
`BaseCommandTests<SetEntitySchemaPropertiesOptions>` (register doubles in `AdditionalRegistrations`, resolve SUT
from the container, `ClearReceivedCalls` in teardown).
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"`.

### Capability 1 — Primary-display column (Story 2)

File: `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs`

**TC-U-01** — `SetSchemaProperties_ShouldSetOwnColumnAsPrimaryDisplay_WhenColumnExistsInColumns`
Happy path, own column. Arrange a loaded schema with own column `Name`; act `SetSchemaProperties` with
`PrimaryDisplayColumn = "Name"`; assert the saved schema's `PrimaryDisplayColumn.Name == "Name"` and it is
matched by the column's `uId` object (not a flat UId field). (AC-01)

**TC-U-02** — `SetSchemaProperties_ShouldSetInheritedColumnAsPrimaryDisplay_WhenColumnExistsOnlyInInheritedColumns`
Arrange own columns + an inherited column `Owner`; act with `PrimaryDisplayColumn = "owner"` (case-insensitive);
assert resolution falls through `Columns` then `InheritedColumns` and the inherited column is set as primary
display. (AC-02)

**TC-U-03** — `SetSchemaProperties_ShouldResolveColumnCaseInsensitively_WhenNameCasingDiffers`
Edge: mixed-case name matches. Assert match succeeds regardless of casing. (AC-02)

**TC-U-04** — `SetSchemaProperties_ShouldThrowNotFound_WhenColumnMissingFromSchema`
Negative: `PrimaryDisplayColumn = "Ghost"` absent from both collections; assert
`EntitySchemaDesignerException` with message `Column 'Ghost' was not found in schema '<S>'.` and no save
occurs. (AC-ERR)

**TC-U-05** — `SetSchemaProperties_ShouldThrowReadbackError_WhenReloadedPrimaryDisplayDoesNotMatch`
Negative (R-01/A-01): mock the reload so `reloadedSchema.PrimaryDisplayColumn?.Name` differs from the requested
column; assert a clear error is raised (silent no-op converted to failure). (AC-ERR)

**TC-U-06** — `SetSchemaProperties_ShouldThrowNoProperty_WhenNoSettablePropertySupplied`
Negative: options with all schema-level properties null/empty; assert
`EntitySchemaDesignerException("No schema property to set.")`, no LoadSchema-save side effects. (AC-ERR)

File: `clio.tests/Command/SetEntitySchemaPropertiesCommandTests.cs` (`BaseCommandTests<SetEntitySchemaPropertiesOptions>`)

**TC-U-16** — `Execute_ShouldDelegateToSetSchemaProperties_WhenOptionsValid`
Resolve the command from the container; act `Execute` with a valid options bag; assert
`IRemoteEntitySchemaColumnManager.Received(1).SetSchemaProperties(options)` and a zero return code. Verifies
DI wiring and delegation (R-07). Also assert required `--package` / `--schema-name` binding and that
`--primary-display-column` is optional (FR-11 extensibility). (AC-01, FR-11)

### Capability 2 — Inherited caption/description override (Story 3)

File: `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs`

**TC-U-07** — `ModifyColumn_ShouldOverrideCaptionInPlaceOnInheritedColumn_WhenModifyIsCaptionOnly`
Happy path. Arrange a child schema with inherited column `Symptoms`; act a caption-only modify
(new `TitleLocalizations`, no mutating fields); assert the override is applied in place on the
`InheritedColumns` entry, `isInherited:true`, `uId`/`name`/`type` unchanged, the column is NOT moved into
`Columns`, and the new caption is persisted (keyed `<Schema>.Columns.Symptoms.Caption`). (AC-03)

**TC-U-08** — `ModifyColumn_ShouldOverrideDescriptionOnly_WhenDescriptionOnlyOnInheritedColumn`
Description-only variant of the caption-only path; assert it is accepted and applied in place. (AC-03/FR-03)

**TC-U-09** — `ModifyColumn_ShouldRejectInheritedMutation_WhenAnyNonCaptionFieldPresent` (parameterized)
Negative (R-06, counter-metric). Parameterize a case per AC-04 field: `NewName`, `Type`,
`ReferenceSchemaName`, `Required`, `Indexed`, `Cloneable`, `TrackChanges`, default-value*, `MultilineText`,
`LocalizableText`, `AccentInsensitive`, `Masked`, `FormatValidated`, `UseSeconds`, `SimpleLookup`, `Cascade`,
`DoNotControlIntegrity`. Each asserts `EntitySchemaDesignerException` with message
`Column '<C>' is inherited; only its caption and description can be overridden. Its name, type, and flags are read-only.`
and no save. (AC-04, AC-ERR)

**TC-U-10** — `ModifyColumn_ShouldRejectMixedMutation_WhenCaptionCombinedWithDisallowedField`
Edge: caption set AND a mutating field set together; assert the whole modify is rejected (caption presence does
not whitelist the operation). (AC-04)

File: `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` — verify-path

**TC-U-11** — `VerifyColumnMutation_ShouldAcceptInheritedOverride_WhenColumnFoundInInheritedColumns`
Verify fix: a `Modify` where the reloaded column lives in `InheritedColumns` (not `Columns`) passes
verification, and the reloaded inherited caption is asserted equal to the requested value in the effective
culture. (Verify-fix AC, R-02)

**TC-U-12** — `VerifyColumnMutation_ShouldFallBackToEnUs_WhenEffectiveCultureCaptionAbsent`
Edge (R-02): effective culture missing from the reloaded caption array → en-US fallback used for the match.

**TC-U-13** — `VerifyColumnMutation_ShouldStillVerifyOwnColumn_WhenColumnInColumns`
Regression (R-05): own-column `Modify` verification continues to succeed via `Columns` after the change.

File: `clio.tests/Command/UpdateEntitySchemaCommand.BatchExecution.Tests.cs`

**TC-U-14** — `Execute_ShouldRejectEntireBatch_WhenBatchMixesCaptionOverrideWithDisallowedInheritedChange`
Negative (R-09). Arrange a batch: op1 = valid inherited caption override, op2 = disallowed inherited
name/type/flag change; assert the batch fails with the AC-04 message and no schema is saved (no partial apply,
consistent with the existing publish-once-per-batch contract). (AC-04, AC-ERR)

### Capability 3 — Color type (Story 1)

File: `clio.tests/Command/EntitySchemaDesignerSupportTests.cs`

**TC-U-20** — `TryResolveDataValueType_ShouldResolveColorTo18_WhenTokenIsColor`
`Color` (name-keyed, case-insensitive) resolves to `18`. (AC-05)

**TC-U-21** — `TryResolveDataValueType_ShouldNotResolveRawNumeric18_WhenTokenIsNumericString`
Negative (OQ-04): raw `"18"` is NOT publicly resolvable; only the named token is accepted. (AC-ERR)

**TC-U-22** — `GetFriendlyTypeName_ShouldReturnColor_WhenDataValueTypeIs18`
`GetFriendlyTypeName(18) == "Color"` (named readback, not raw `18`). (AC-06)

**TC-U-23** — `IsTextLikeDataValueType_ShouldReturnFalse_WhenDataValueTypeIs18`
R-03: Color is not text-like (18 absent from `TextDataValueTypes`). (AC-07)

**TC-U-24** — `TryResolveDataValueType_ShouldReject_WhenTokenIsUnsupported`
Negative: an unknown token (e.g. `"Rainbow"`) fails resolution and drives the unsupported-type error. (AC-ERR)

File: `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs`

**TC-U-25** — `ModifyColumn_ShouldCreateDataValueType18Column_WhenTypeIsColor`
Color add: assert the saved column carries `dataValueType 18`. (AC-05)

**TC-U-26** — `ModifyColumn_ShouldNotEnableTextOnlyOptions_WhenColumnTypeIsColor` (parameterized over
`MultilineText`, `AccentInsensitive`, `FormatValidated`, `Masked`)
Negative (R-03): building/modifying a Color column does not enable/accept text-only options — they are
rejected or absent on the saved column. (AC-07)

### MCP surface — unit mapping & descriptions (Stories 1–4)

File: `clio.tests/Command/McpServer/EntitySchemaToolTests.cs`

**TC-U-17** — `SetEntitySchemaPropertiesTool_ShouldMapArgsToOptions_WhenInvoked`
New tool: `SetEntitySchemaPropertiesArgs` (package, schema-name, primary-display-column, env) map onto
`SetEntitySchemaPropertiesOptions`; destructive flag aligned with contract. (AC-MCP)

**TC-U-18** — `EntitySchemaTool_ShouldListColorInTypeDescription_ForCreateAndModifyArgs`
Create/modify column arg `type` `[Description]` includes `Color`. (AC-DOC/Story 1)

**TC-U-19** — `EntitySchemaTool_ShouldMentionInheritedCaptionOverride_InModifyAndUpdateDescription`
Modify/update column tool `[Description]` notes inherited caption-override support. (AC-DOC/Story 3)

File: `clio.tests/Command/McpServer/*` (guidance/routing resource tests, if resource text is asserted there)

**TC-U-27** — `AppModelingGuidance_ShouldDescribeAllThreeCapabilities_WhenResourceRead`
Guidance text states: set primary-display via `set-entity-schema-properties`; inherited caption/description
override allowed while name/type/flags stay read-only; Color is a supported type. Also assert guide content is
NOT duplicated into `McpServerInstructions.cs`. (AC-DOC/Story 4)

---

## Integration Tests (`clio.tests/`)

No new `[Category("Integration")]` tests are required for this feature. All server interaction is over
`IApplicationClient` and is exercised at the unit level with NSubstitute (save/publish/verify pipeline) and at
the E2E level against a live stand. There is no local file-system / DB / IIS / K8s surface introduced by these
three capabilities. If the shared-pipeline extraction later introduces a file-touching helper, add an
`[Category("Integration")]` case then.

---

## E2E Tests (`clio.mcp.e2e/`)

> **CI status: NOT in CI yet — run manually** against a live Creatio 8 stand (core 10.1.185, where the
> contracts were verified). Record the manual run in the PR/change summary. Unit mapping tests are necessary
> but insufficient for MCP work (repo MCP policy). **Add each E2E scenario below to the PR checklist as a
> manual gate.** Consolidated in Story 4; tag per the repo E2E category/traits.

File: `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` (+ `Support/Results/EntitySchemaEnvelope.cs` if a new result
field is needed)

**TC-E2E-01** — set-primary-display round-trip (own column)
- Tool: `set-entity-schema-properties`
- Input: `{ "package": "<P>", "schema-name": "<S>", "primary-display-column": "<ownCol>" }`
- Expected: save succeeds; `get-entity-schema-properties` reports `primary-display-column-name == <ownCol>`.
- (AC-01)

**TC-E2E-02** — set-primary-display round-trip (inherited column)
- Input: `--primary-display-column <inheritedCol>`; expected readback confirms the inherited column as primary
  display. (AC-02)

**TC-E2E-03** — inherited caption override + parent unchanged
- Tool: `modify-entity-schema-column` / `update-entity-schema` on a replacing/child schema (Case→Tickets
  rebrand: Symptoms→Description, Solution→Resolution Notes, ClosureDate→Closed Date/Time).
- Expected: child readback shows the new caption; reading the **parent** schema shows its caption unchanged.
  (AC-03)

**TC-E2E-04** — Color create + named readback
- Tool: `create-entity-schema` / `modify-entity-schema-column` with `type: "Color"`.
- Expected: dataValueType-18 column created; `get-entity-schema-properties` reports the named `Color` token
  (not raw `18`). (AC-05, AC-06)

**TC-E2E-05** — negatives (all in one scenario group)
- Inherited non-caption mutation (name/type/flag) → rejected with the AC-04 message, non-zero exit.
- Missing `--primary-display-column` target → `Column '<C>' was not found in schema '<S>'.`, non-zero exit.
- Unsupported type token → unsupported-type error, non-zero exit.
- Color text-only options (multiline/accent/format-validated/masked) → rejected/absent.
- (AC-04, AC-07, AC-ERR)

---

## Regression Guard

Tests that MUST behave correctly after this feature ships. Note the distinction between **stay-green
unchanged** and **must be rewritten** (behavior intentionally changed by Story 3).

| Test file | Test name | Status | Why at risk |
|-----------|-----------|--------|------------|
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | `ModifyColumn_Throws_WhenColumnIsInherited` (asserts `"*inherited and read-only*"`) | **MUST BE REWRITTEN** | Story 3 intentionally changes this behavior. The old blanket-reject message is replaced. Rewrite/split into TC-U-07 (caption allowed) + TC-U-09 (non-caption rejected with the new AC-04 message). Do NOT leave it asserting the old v1 message. |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | `ModifyColumns_PublishesAndRebuildsOnce_ForMultiOperationBatch` | Stay green | R-04: shared-pipeline extraction (Story 2) must not change publish-once-per-batch |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | `ModifyColumn_PublishesAndRequestsODataRebuild_AfterSaving` | Stay green | R-04: save → publish → OData-rebuild ordering preserved through extraction |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | `ModifyColumn_SucceedsWithWarning_WhenODataRebuildRequestFails` | Stay green | R-04: warning-on-rebuild-fault behavior preserved |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | `ModifyColumn_Throws_WhenPublishFails` | Stay green | R-04: publish-failure path preserved |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | `ModifyColumn_AddsOwnColumn_AndSetsPrimaryDisplayColumn`, `ModifyColumn_RemovesOwnColumn_AndReassignsReferences` | Stay green | Counter-metric G1: existing default primary-display behavior unchanged by the new setter |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | `ModifyColumn_UpdatesOwnColumn_AndPreservesUnspecifiedFields`, all `ModifyColumn_*Caption*` / `*Localizations*` tests | Stay green | R-05: `VerifyColumnMutation` change (accept `InheritedColumns`) must not break own-column verification or caption normalization |
| `clio.tests/Command/EntitySchemaDesignerSupportTests.cs` | `TryResolveDataValueType_*`, `GetFriendlyTypeName_*`, `IsImageLookupDataValueType_*` | Stay green | R-03: adding `["color"]=18` / `18 => "Color"` must not disturb existing type-registry resolution or friendly names; 18 must NOT land in `TextDataValueTypes`/`BinaryLikeDataValueTypes`/`RuntimeDataValueTypeUIdMap` |
| `clio.tests/Command/UpdateEntitySchemaCommand.BatchExecution.Tests.cs` | `Execute_Should_Save_And_Materialize_Only_Once_Per_Batch`, `Execute_Should_PreserveEffectiveTitle_*` | Stay green | R-04/R-09: batch save-once + localization behavior preserved after extraction and the inherited-guard change |
| `clio.tests/Command/ModifyEntitySchemaColumnCommandTests.cs` | all | Stay green | Command validation unchanged (inherited handling lives in the manager); only `--type` HelpText widened |
| `clio.tests/Command/CreateEntitySchemaCommandTests.cs`, `UpdateEntitySchemaCommandTests.cs` | all | Stay green | Counter-metric G1: create/update without the new parameter unchanged |
| `clio.tests/Command/McpServer/EntitySchemaToolTests.cs`, `CreateEntitySchemaToolTests.cs`, `UpdateEntitySchemaToolTests.cs`, `EntitySchemaLocalizationContractTests.cs` | all | Stay green | R-08: existing tool arg mappings/descriptions unaffected by the new tool + widened descriptions |
| `clio.tests/Command/GetEntitySchemaPropertiesCommandTests.cs`, `GetEntitySchemaColumnPropertiesCommandTests.cs` | all | Stay green | Readback used by AC-01/AC-06 must keep reporting `primary-display-column-name` and column types |
| `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` (pre-existing scenarios) | all | Stay green (manual) | Existing E2E create/modify/update round-trips must not regress; run manually alongside TC-E2E-01..05 |

### Smart-regression module mapping (per AGENTS.md)

Changed source paths → module traits:

- `clio/Command/EntitySchemaDesigner/*`, `clio/Command/SetEntitySchemaPropertiesCommand.cs`,
  `clio/Command/ModifyEntitySchemaColumnCommand.cs`, `clio/Command/CreateEntitySchemaCommand.cs` → **Module=Command**
- `clio/Command/McpServer/**` (tool, prompt, guidance, routing resources) → **Module=McpServer**
- `clio/Program.cs` + `clio/BindingsModule.cs` (Story 2 wiring) → **DI composition root → full unit suite trigger**

Targeted commands:

```shell
# Stories 1–3 (behavior + command + tool mapping)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build

# Story 4 (MCP guidance/prompt/routing only)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build

# Full unit suite — REQUIRED for Story 2 (touches Program.cs + BindingsModule.cs — DI composition root)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"
```

Story 2 edits `Program.cs` and `BindingsModule.cs`, which are full-suite triggers under the smart-regression
policy — run the full `Category=Unit` suite for that story, not just the targeted modules.

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit | ~28 (TC-U-01..27, several parameterized) | 1 rewritten (`ModifyColumn_Throws_WhenColumnIsInherited`) | Split across Command + McpServer modules |
| Integration | 0 | 0 | No local I/O surface introduced |
| E2E | 5 scenario groups (TC-E2E-01..05) | pre-existing E2E stay green | Manual only — NOT in CI |

Per-story unit distribution: Story 1 ≈ TC-U-20..26, TC-U-18 (Color); Story 2 ≈ TC-U-01..06, TC-U-16, TC-U-17
(primary-display + command + tool); Story 3 ≈ TC-U-07..14, TC-U-19 (inherited caption + verify + batch);
Story 4 ≈ TC-U-27 + all TC-E2E-*.

---

## Traceability Matrix

| PRD AC | ADR test-strategy row | Test case IDs | Story |
|--------|----------------------|---------------|-------|
| AC-01 (set primary-display, readback) | Unit: SetSchemaProperties own resolution / E2E round-trip | TC-U-01, TC-U-16, TC-E2E-01 | 2 |
| AC-02 (inherited target as primary-display) | Unit: inherited resolution / E2E | TC-U-02, TC-U-03, TC-E2E-02 | 2 |
| AC-03 (inherited caption override, parent unchanged) | Unit: inherited caption allow / E2E override+parent | TC-U-07, TC-U-08, TC-E2E-03 | 3 |
| AC-04 (inherited name/type/flags rejected) | Unit: inherited caption reject | TC-U-09, TC-U-10, TC-U-14, TC-E2E-05 | 3 |
| AC-05 (Color create → dataValueType 18) | Unit: Color add/modify | TC-U-20, TC-U-25, TC-E2E-04 | 1 |
| AC-06 (readback reports named `Color`) | Unit: friendly name `Color` | TC-U-22, TC-E2E-04 | 1 |
| AC-07 (Color not text-like) | Unit: Color rejects masked/multiline/accent/format-validated; not text-like | TC-U-23, TC-U-26, TC-E2E-05 | 1 |
| AC-ERR (missing column / unsupported type / disallowed inherited mutation / readback mismatch) | Unit negatives + E2E negatives | TC-U-04, TC-U-05, TC-U-06, TC-U-09, TC-U-21, TC-U-24, TC-E2E-05 | 1, 2, 3 |
| AC-MCP (tool exposed, aligned args/descriptions, E2E) | Unit: tool arg mapping + E2E | TC-U-17, TC-U-18, TC-U-19, TC-E2E-01..05 | 2, 3, 4 |
| AC-DOC (guidance/routing/type-list updated) | Unit: guidance/description text | TC-U-18, TC-U-19, TC-U-27 | 1, 3, 4 |
| FR-11 (extensible property bag) | Unit: optional primary-display, required package/schema | TC-U-16 | 2 |
| Verify-fix AC (Modify accepts `Columns` OR `InheritedColumns`, culture match) | Unit: inherited verify + en-US fallback + own-column still verified | TC-U-11, TC-U-12, TC-U-13 | 3 |
| Counter-metric G1 (create/update default primary-display unchanged) | Regression | Regression Guard (create/update/own-column suites) | 2 |
| Counter-metric G2 (inherited non-caption immutability preserved) | Regression | TC-U-09, rewritten `ModifyColumn_Throws_WhenColumnIsInherited` | 3 |
| Counter-metric G3 (Color never exposes text-only options) | Regression | TC-U-23, TC-U-26 | 1 |

Risk coverage: R-01→TC-U-05/TC-E2E-01; R-02→TC-U-11/TC-U-12; R-03→TC-U-23/TC-U-26; R-04→ModifyColumns
publish/rebuild regression set; R-05→TC-U-13; R-06→TC-U-09 + rewritten legacy test; R-07→TC-U-16; R-08→TC-U-17..19;
R-09→TC-U-14.

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] Command tests use `BaseCommandTests<SetEntitySchemaPropertiesOptions>` (SUT resolved from container,
      doubles in `AdditionalRegistrations`, `ClearReceivedCalls` in teardown)
- [ ] Every test has AAA structure, a `because` on every assertion, and a `[Description]` attribute
- [ ] Test naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`
- [ ] No Integration tests required (documented rationale above); revisit if a file-touching helper is added
- [ ] All TC-E2E-* implemented in `clio.mcp.e2e/`, tagged per E2E traits, and each added to the PR checklist as
      a **manual** gate (E2E not in CI); manual run recorded in the PR/change summary
- [ ] Regression guard: `ModifyColumn_Throws_WhenColumnIsInherited` **rewritten** (not left on the old v1
      message); all other listed suites stay green
- [ ] Story 2 ran the **full** `Category=Unit` suite (Program.cs + BindingsModule.cs are DI-root triggers)
- [ ] Color asserted NOT text-like (masked/multiline/accent/format-validated rejected/absent) — AC-07
- [ ] Filter command(s) used recorded in each story PR description (smart-regression policy)
- [ ] PR includes new/changed test files in the changed-files list
