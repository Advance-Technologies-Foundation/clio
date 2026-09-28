# Test Plan: Page localization (ENG-90576)

**Feature**: page-localization
**Jira**: [ENG-90576](https://creatio.atlassian.net/browse/ENG-90576)
**Spec**: [spec-page-localization.md](../prd/spec-page-localization.md)
**ADR**: [adr-page-localization.md](../adr/adr-page-localization.md) (facts F1-F9 and raw evidence)
**Stories**: [1](../stories/story-page-localization-1.md) · [2](../stories/story-page-localization-2.md) ·
[3](../stories/story-page-localization-3.md)
**Status**: Draft
**Created**: 2026-09-26

---

## Risks and what covers them

| Risk | Consequence | Covered by |
|---|---|---|
| A page-owned key is saved with only the target culture | `en-US` and every other culture of that key are deleted (F3) | TC-U-01, TC-U-02, TC-E2E-01, TC-E2E-03 |
| A culture absent from `SysCulture` is written | `success:true`, nothing stored (F5) | TC-U-08, TC-U-12, TC-E2E-04, TC-U-30..34 |
| Inactive culture refused | Jira scenario (translate, then activate) blocked | TC-U-09, TC-M-01 |
| Re-run re-saves the schema | Checksum moves, the agent's baseline goes stale for nothing | TC-U-04, TC-E2E-02 |
| Baseline not refreshed after a save | Next `update-page` blocked as "modified outside this session" (F9) | TC-U-14, TC-M-02 |
| A replacing schema is created | A schema the caller did not ask for appears in another package | TC-U-13 |
| `update-page` / `sync-pages` behaviour changes through the shared helper | Regressions in page authoring | TC-U-16 + existing suites unchanged |
| "Present" read as "translated" | Page caption holds English in every culture after `create-page` (F8) | TC-U-14 (`sameAsDefault`), TC-E2E-05 |

## Regression scope

- `clio.tests/Command/ResourceStringHelperTests.cs` (incl. the existing es-ES preservation case) — unchanged
  assertions.
- `clio.tests/Command/McpServer/PageUpdateToolTests.cs`, page sync tests — unchanged.
- `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs`, `EntitySchemaDesignerSupportTests.cs`,
  `SetEntitySchemaPropertiesTitleTests.cs`, `UpdateEntitySchemaCommandTests.cs` — assertions unchanged (story 3 AC-4).
- `ToolContractGetToolTests`, `ToolContractPayloadBudgetTests`, `WorkspaceTemplateGuidanceDriftTests`,
  `McpFeatureToggleFilterTests` tools/list budget, `McpE2eSelectionCoverageTests`.
- Full unit suite (`--filter "TestCategory=Unit"`) because `BindingsModule.cs` / `Program.cs` / `clio/Common/`
  change.

## Unit test cases (`[Category("Unit")]`, AAA, `because`, `[Description]`)

Naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`. Command tests inherit
`BaseCommandTests<LocalizePageOptions>` and resolve the command from DI.

### Story 1 — `LocalizePageCommandTests`

| ID | Test | Arrange | Assert |
|---|---|---|---|
| TC-U-01 | `Execute_ShouldWriteTargetCultureAndKeepOtherCultures_WhenKeyIsOwnedByPage` | GetSchema returns own key with en-US + de-DE; resources `{key: es}` | SaveSchema body: the entry has en-US and de-DE unchanged, es-ES = value, same `uId` / `parentSchemaUId` |
| TC-U-02 | `Execute_ShouldKeepParentMarkerAndAllCultures_WhenKeyIsInherited` | inherited entry, 28 values, `parentSchemaUId` = ancestor | 28 values sent, only es-ES differs, marker unchanged |
| TC-U-03 | `Execute_ShouldAddCultureValue_WhenEntryHasNoValueInThatCulture` | own entry with en-US only | es-ES object appended |
| TC-U-04 | `Execute_ShouldNotSave_WhenEverySuppliedValueIsUnchanged` | stored es-ES equals supplied | no SaveSchema call, `saved:false`, key in `unchanged`, `success:true` |
| TC-U-05 | `Execute_ShouldOnlyReportCoverage_WhenNoResourcesAndNoCaption` | no resources, no caption | no SaveSchema, coverage populated |
| TC-U-06 | `Execute_ShouldFailWithoutSaving_WhenKeyIsUnknown` | resources contains a key not in the list | `success:false`, error lists unknown + available keys, no SaveSchema |
| TC-U-07 | `Execute_ShouldPointToEntityColumnCaption_WhenUnknownKeyIsDataSourceBound` | body has `$Resources.Strings.UsrName` bound to a DS attribute, key absent from list | error names `title-localizations` on the entity tools |
| TC-U-08 | `Execute_ShouldFailBeforeReadingSchema_WhenCultureIsAbsent` | catalog has no `fi-FI` | `success:false`, message contains "Languages section" and the available list, GetSchema not called |
| TC-U-09 | `Execute_ShouldSaveAndWarn_WhenCultureIsInactive` | catalog `es-ES Active=false` | saved, `cultureActive:false`, inactive warning present |
| TC-U-10 | `Execute_ShouldCanonicalizeCultureName_WhenCaseDiffers` | `--culture es-es`, catalog `es-ES` | value written under `es-ES`, response `culture:"es-ES"` |
| TC-U-11 | `Execute_ShouldWriteCaptionInTargetCultureOnly_WhenCaptionSupplied` | caption array with 28 cultures | only es-ES caption changed |
| TC-U-12 | `Execute_ShouldFail_WhenReadbackDoesNotContainWrittenValue` | readback GetSchema lacks es-ES | `success:false` naming the key |
| TC-U-13 | `Execute_ShouldFailWithoutCreatingSchema_WhenNoEditableSchemaInDesignPackage` | head not in design package, no same-name schema there | `success:false`, SaveSchema not called |
| TC-U-14 | `Execute_ShouldReportMissingAndSameAsDefault_WhenComputingCoverage` | keys: translated, missing, same-as-en-US; baseline meta exists | `missing` / `sameAsDefault` / `captionSameAsDefault` exact; baseline refresh called once after save |

### Story 1 — helpers

| ID | Test | Assert |
|---|---|---|
| TC-U-15 | `SetCultureValue_ShouldReplaceOnlyThatCulture_WhenEntryHasSeveralCultures` (ResourceStringHelperTests) | other culture objects untouched, order kept |
| TC-U-16 | existing `CleanAndMerge` en-US cases re-run unchanged; add `CleanAndMerge_ShouldStillWriteDefaultCulture_WhenRoutedThroughCultureSetter` | en-US behaviour identical |
| TC-U-17 | `GetCultures_ShouldReturnNameAndActive_WhenSelectQuerySucceeds` (CreatioCultureCatalogTests) | rows parsed |
| TC-U-18 | `Find_ShouldReportAbsentAndInactive_WhenCultureMissingOrInactive` | absent → not found; inactive → found, `Active=false`; case-insensitive match returns canonical name |
| TC-U-19 | `GetCultures_ShouldThrowWithServerMessage_WhenSelectQueryFails` | failure surfaces, never an empty list |
| TC-U-20 | route tests for the new `KnownRoute` entries | `.NET Framework` → `0/…`, `.NET Core` → no prefix |

### Story 2 — `LocalizePageToolTests`

| ID | Test | Assert |
|---|---|---|
| TC-U-21 | `LocalizePage_ShouldMapArgumentsToOptions_WhenCalled` | every field incl. `environment-name` |
| TC-U-22 | `LocalizePage_ShouldRejectLegacyAliases_WhenCamelCaseNamesSupplied` | error lists valid names |
| TC-U-23 | `LocalizePage_ShouldResolveCommandForEnvironment_WhenEnvironmentNameGiven` | `IToolCommandResolver` called with the env |
| TC-U-24 | `ToolSchema_ShouldRequireOnlySchemaNameAndCulture_WhenListed` | SDK-emitted `required` = [`schema-name`, `culture`] |
| TC-U-25 | `GetToolContract_ShouldDescribeLocalizePage_WhenRequested` (ToolContractGetToolTests) | fields, required flags, output shape |

### Story 3 — entity writes

| ID | Test | Assert |
|---|---|---|
| TC-U-30 | `Execute_ShouldFailBeforeSave_WhenTitleLocalizationCultureIsAbsent` (set-entity-schema-properties) | no SaveSchema, Languages message |
| TC-U-31 | `Execute_ShouldSaveAndWarn_WhenTitleLocalizationCultureIsInactive` | saved, warning logged |
| TC-U-32 | `ModifyColumn_ShouldFailBeforeSave_WhenColumnTitleCultureIsAbsent` | no SaveSchema |
| TC-U-33 | `Execute_ShouldFailBeforeAnySave_WhenOneBatchOperationHasAbsentCulture` (update-entity-schema) | zero saves |
| TC-U-34 | `CreateSchema_ShouldFailBeforeSave_WhenColumnMapCultureIsAbsent` | no SaveSchema |
| TC-U-35 | `Execute_ShouldFail_WhenCultureCatalogReadFails` | no SaveSchema, error surfaced |

## E2E cases (`clio.mcp.e2e/LocalizePageToolE2ETests.cs`, `McpE2E.Sandbox`)

Arrange (once per fixture): create a throwaway page from `PageWithTabsFreedomTemplate` in the test package, register
`UsrE2eLabel_caption` = "E2E label" with `update-page`; capture `get-page` resources and the schema checksum.

| ID | Act | Assert |
|---|---|---|
| TC-E2E-01 | `localize-page` es-ES `{UsrE2eLabel_caption: "Etiqueta E2E"}`, caption "Página E2E" | `success`, `written` = key, `caption:"written"`; `get-page` shows es-ES value; en-US value byte-identical to arrange |
| TC-E2E-02 | repeat TC-E2E-01 | `saved:false`, key in `unchanged`, checksum unchanged |
| TC-E2E-03 | `localize-page` de-DE `{UsrE2eLabel_caption: "E2E-Beschriftung"}` | es-ES from TC-E2E-01 still present; en-US unchanged |
| TC-E2E-04 | `localize-page` fi-FI (not a Creatio language) | `success:false`, message contains "Languages section"; checksum unchanged |
| TC-E2E-05 | report-only for es-ES before TC-E2E-01 (order the cases) | `coverage.missing` contains the key, `captionSameAsDefault:true`, checksum unchanged |

## Manual cases (live stand `eng90576`)

| ID | Steps | Expected |
|---|---|---|
| TC-M-01 | On the lab page: translate every custom key and the caption into es-ES with `localize-page` while es-ES is inactive; activate es-ES in System Designer → Languages; set Supervisor's language to Spanish; sign in again; open the page. Leave one custom key untranslated. | Spanish captions shown; the untranslated key shows the English text; switching back to English shows unchanged English captions. |
| TC-M-02 | Jira scenario through the CLI: `get-page` → `localize-page` es-ES → `update-page` (no `--force`) with a body change | `update-page` succeeds (baseline refreshed by `localize-page`), es-ES values survive. |
| TC-M-03 | Story 3: `update-entity-schema` modify with `{en-US, fi-FI}` then `{en-US, es-ES}` | first fails with the Languages message; second saves with the inactive warning; `de-DE` still present afterwards. |

## Exit criteria

All TC-U green in the targeted and full unit suites; TC-E2E-01..05 green on a live stand (TeamCity run is advisory);
TC-M-01..03 recorded in the PR with the stand name and date.
