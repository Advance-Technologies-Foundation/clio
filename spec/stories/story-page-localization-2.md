# Story 2: `localize-page` MCP tool — contract, unit tests, E2E

**Feature**: page-localization
**Jira**: [ENG-90576](https://creatio.atlassian.net/browse/ENG-90576)
**Spec**: [spec-page-localization.md](../prd/spec-page-localization.md) — CAP-01..CAP-05 on the MCP surface
**ADR**: [adr-page-localization.md](../adr/adr-page-localization.md) — D1, D7, D10, "Guidance changes required"
**Test plan**: [tp-page-localization.md](../test-plans/tp-page-localization.md)
**Status**: review
**Size**: M
**Depends on**: story 1 (`LocalizePageCommand`, `LocalizePageOptions`, response DTO)
**Blocks**: nothing (the clio-knowledge guidance PR should reference this tool's final contract)

---

## As a

coding agent working through clio MCP

## I want

a `localize-page` tool that takes a page, a culture and a key→translation map

## So that

I can translate a page I built into additional languages and see what is still untranslated, without re-sending
the page body.

---

## Acceptance criteria

- **AC-1** `clio/Command/McpServer/Tools/LocalizePageTool.cs`: `[McpServerToolType]`, `ToolName = "localize-page"`,
  `[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]`
  (Destructive: it overwrites an existing target-culture value; Idempotent: the same call twice leaves the same
  state — ADR D2 step 4). `[McpToolExecution]` like `PageUpdateTool` (Worker, PerCall).
- **AC-2** Arguments record: `schema-name` (required), `culture` (required), `resources` (optional JSON object
  `string → string`), `caption` (optional), `environment-name` (preferred) + `uri`/`login`/`password` fallback, with
  credential-passthrough rules identical to other environment-aware tools. Optional fields are declared
  `string? X = null` / `Dictionary<string,string>? X = null` so the SDK does not emit them in `required`.
  Legacy aliases (`schemaName`, `environmentName`) are rejected with `McpToolArgumentSupport.BuildLegacyAliasError`
  like `GetUserCultureTool`.
- **AC-3** Environment-aware execution: the command is resolved per environment through `IToolCommandResolver`
  (the `BaseTool` environment-aware pattern), never the startup-injected instance.
- **AC-4** The tool returns the story-1 response unchanged (same field names) so CLI and MCP agree.
- **AC-5** `[Description]` states: translate captions of ONE page into ONE culture; never changes `en-US` or other
  cultures; report-only when `resources` and `caption` are omitted; unknown keys fail; absent culture fails with the
  Languages-section message; inactive culture is written with a warning; DS-bound field labels are entity column
  captions (use entity `title-localizations`). Trigger line: `Read get-guidance name=page-schema-resources before
  translating a page.`
- **AC-6** Long-tail: NOT added to `McpCoreToolProfile.CoreToolTypes`. Reachable through `clio-run` /
  `get-tool-contract`; appears in the compact index.
- **AC-7** `ToolContractGetTool`: `[LocalizePageTool.ToolName] = BuildLocalizePage()` with every field, its type,
  required flag and the output shape; add the tool to whichever tool-name list `PageUpdateTool.ToolName` is in at
  `ToolContractGetTool.cs:~911` if that list governs contract availability. `ToolContractPayloadBudgetTests` stays
  green.
- **AC-8** `McpToolDescriptions.PageResourcesAdditive` gains one sentence: `For other cultures use localize-page.`
  (long-tail constant; does not affect the resident `tools/list` byte budget — confirm with
  `McpFeatureToggleFilterTests.RegisterEnabledPrimitives_ShouldKeepToolsSerializedSizeWithinBudget_WhenCalled`).
- **AC-9** Unit tests `clio.tests/Command/McpServer/LocalizePageToolTests.cs`: argument mapping, legacy-alias
  rejection, environment resolution, response pass-through, schema `required` = `schema-name`, `culture` only.
- **AC-10** E2E `clio.mcp.e2e/LocalizePageToolE2ETests.cs`, `[Category("McpE2E.Sandbox")]`,
  `[AllureFeature(LocalizePageTool.ToolName)]`, fixture name not a prefix of another fixture. Referenced by
  `LocalizePageTool.ToolName` so `mcp-e2e-selection.json` selects it; update the manifest if the coverage guard
  (`clio.tests/McpE2eSelectionCoverageTests.cs`) requires an entry. Arrange: create a throwaway page (as
  `PageCreateToolE2ETests` does) and register one custom key with `update-page`. Cases TC-E2E-01..05.
- **AC-11** Template drift: `clio/tpl/**` does not have to name the tool; if it is named anywhere, it must be
  resident-or-bridged — `WorkspaceTemplateGuidanceDriftTests` green.
- **AC-12** ClioRing gate: search `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`,
  `clio-ring/ClioRing.Desktop/actions.json` for `localize-page` and for the `update-page` description; expected
  result "ClioRing compatibility reviewed, no Ring-consumed contract changed" with the inspected paths in the PR.
- **AC-13** Telemetry vocabulary unchanged (no event names added) — state it in the PR.

---

## Files to touch

| File | Change |
|---|---|
| `clio/Command/McpServer/Tools/LocalizePageTool.cs` (new) | AC-1..AC-5. |
| `clio/Command/McpServer/Tools/ToolContractGetTool.cs` | AC-7. |
| `clio/Command/McpServer/Tools/McpToolDescriptions.cs` | AC-8. |
| `clio/BindingsModule.cs` (if MCP tools need explicit registration) | Tool registration via `McpFeatureToggleFilter.RegisterEnabledPrimitives` path — do not use `WithToolsFromAssembly`. |
| `clio.tests/Command/McpServer/LocalizePageToolTests.cs` (new) | AC-9. |
| `clio.tests/Command/McpServer/ToolContractGetToolTests.cs` | Contract presence and fields. |
| `clio.mcp.e2e/LocalizePageToolE2ETests.cs` (new), `clio.mcp.e2e/TestSelection/mcp-e2e-selection.json` | AC-10. |

## Test cases (details in the test plan)

- TC-U-21 argument record maps to `LocalizePageOptions` (all fields, env name).
- TC-U-22 legacy aliases rejected with the valid-names list.
- TC-U-23 command resolved through `IToolCommandResolver` for the given environment.
- TC-U-24 emitted input schema `required` = [`schema-name`, `culture`] (use the SDK schema, not STJ).
- TC-U-25 contract entry exists with the documented fields and output.
- TC-E2E-01 translate own key + caption into es-ES; en-US unchanged; `get-page` shows es-ES.
- TC-E2E-02 same call again → `saved:false`, all `unchanged`.
- TC-E2E-03 de-DE after es-ES → es-ES still present.
- TC-E2E-04 `fi-FI` → failure with the Languages-section message, schema checksum unchanged.
- TC-E2E-05 report-only → coverage lists the untranslated key, checksum unchanged.

## Validation

- `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&Module=McpServer"`; plus the full unit suite
  if `BindingsModule.cs` changed.
- E2E: run the new fixture against stand `eng90576` locally (skill `clio-mcp-e2e-live-stand`) and on TeamCity
  (skill `run-clio-mcp-e2e`); CI E2E is advisory, the unit mirror is the gate.
- "docs reviewed" / "MCP reviewed" statements in the PR.

## Definition of done

- [x] AC-1..AC-13 met; TC-U-21..25 and TC-E2E-01..05 implemented; unit green; E2E green on a live stand
  (`eng90576`, 2026-09-26, local run; TeamCity run still to do).
- [x] clio-knowledge guidance PR opened with the ADR's six changes and linked from the clio PR (clio-knowledge#238).
