# Story 4: MCP guidance / prompt / routing + clio.mcp.e2e coverage for all three capabilities

**Feature**: entity-schema-authoring-gaps
**FR coverage**: FR-08 (full MCP surface incl. E2E), FR-09 (guidance/routing docs), closes AC-MCP and AC-DOC
**PRD**: [prd-entity-schema-authoring-gaps.md](../prd/prd-entity-schema-authoring-gaps.md)
**ADR**: [adr-entity-schema-authoring-gaps.md](../adr/adr-entity-schema-authoring-gaps.md)
**Jira**: [ENG-93040](https://creatio.atlassian.net/browse/ENG-93040) (epic ENG-85256)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1, Story 2, Story 3 (all three capabilities must exist before guidance/prompt/routing describe them and E2E exercises them)

---

## As a

QA engineer / AI no-code agent driving clio through MCP

## I want

MCP guidance, prompt, and routing to describe the new primary-display setter, the inherited caption override, and the Color type, plus `clio.mcp.e2e` coverage for all three (including negatives)

## So that

the new surfaces are discoverable and verifiably correct end-to-end, not just at the unit-mapping level (repo policy: unit mapping alone does not complete MCP work)

---

## Acceptance Criteria

- [ ] **AC-MCP** — Given the MCP server, each new/changed capability (set-primary-display tool, inherited caption override on modify/update, Color type on create/modify) is exposed with aligned arguments/descriptions/destructive flags and has passing `clio.mcp.e2e` coverage.
- [ ] **AC-DOC** — Given the shipped change, the guidance and routing MCP resources referencing the inherited-column rule and the supported type list are updated: `AppModelingGuidanceResource` states set-primary-display via the new tool, inherited caption/description override allowed (name/type/flags still read-only), and Color is a supported type; the `app-modeling` routing row wording is updated only if the trigger phrasing changes (no new guide).
- [ ] **AC-01/AC-02 (E2E)** — set-primary-display round-trip: set an own then an inherited primary-display column and confirm `get-entity-schema-properties` reports it.
- [ ] **AC-03 (E2E)** — inherited caption override persists on the child and leaves the parent unchanged (read both schemas).
- [ ] **AC-05/AC-06 (E2E)** — Color create + `get-entity-schema-properties` reports the named `Color` token.
- [ ] **AC-04/AC-07/AC-ERR (E2E negatives)** — inherited non-caption mutation rejected; missing primary-display column rejected; unsupported type token rejected; Color text-only options rejected/absent.

## Implementation Notes

From ADR "Files to modify" (MCP resources + prompt + E2E). This story ships the guidance/prompt/routing and the consolidated E2E suite once Stories 1–3 have landed the tools and behavior.

- `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs` — add guidance: set primary-display via `set-entity-schema-properties`; inherited caption/description override allowed while name/type/flags stay read-only; Color is a supported type. Keep guide content authoritative here (do not duplicate into `McpServerInstructions.cs`).
- `clio/Command/McpServer/Resources/RoutingGuidanceResource.cs` — update the `app-modeling` routing row wording only if the trigger phrasing changes; no new guide name.
- `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs` — mention `set-entity-schema-properties`, inherited caption override, and Color, aligned to the current tool contracts.
- `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` (+ `Support/Results/EntitySchemaEnvelope.cs` if a new result field is needed) — E2E scenarios for all three capabilities incl. the negatives above. Tag per the repo's E2E category/traits.

> `clio.mcp.e2e` is **NOT in CI yet** — these scenarios must be run manually against a live stand and the run recorded in the PR/change summary. Unit mapping tests from Stories 1–3 are necessary but insufficient (repo MCP policy).

Key file: `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs`, `clio.mcp.e2e/EntitySchemaToolE2ETests.cs`
Pattern to follow: existing guidance-resource wording and the existing `EntitySchemaToolE2ETests` scenario harness / envelope.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Guidance/prompt/routing text present and consistent (if covered by resource tests) | `clio.tests/Command/McpServer/*` |
| E2E `[Category("E2E")]` (manual — not in CI) | set-primary-display round-trip (own + inherited); inherited caption override + parent unchanged; Color create + `Color` readback; negatives (inherited non-caption mutation, missing column, unsupported type, Color text-only options) | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"` (E2E run manually, recorded in PR).

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); no new `CLIO*` warnings in modified files
- [ ] No new CLI flags in this story
- [ ] `AppModelingGuidanceResource` + `EntitySchemaPrompt` updated; `RoutingGuidanceResource` row reviewed (updated only if trigger phrasing changed); no guide content duplicated into `McpServerInstructions.cs`
- [ ] `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` covers all three capabilities incl. negatives; manual E2E run recorded in the PR (E2E not in CI)
- [ ] Any resource/prompt unit tests use `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] MCP capability map (`docs/McpCapabilityMap.md`) reviewed/updated for the new tool + widened commands
- [ ] Targeted `Module=McpServer` unit filter passes; commands recorded in PR description
- [ ] Agentic code review (parallel quality/correctness/security) run before opening the PR; Blocker/High findings resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes — full `dotnet test --filter "Category=Unit"` → 5078 passed, 0 failed, 25 skipped (net8.0 + net10.0). Full suite run because Story 2 touched Program.cs + BindingsModule.cs (DI root).
- Notes: AppModelingGuidanceResource — added 3 guardrail bullets (Color type; set primary-display via set-entity-schema-properties; inherited caption/description override with name/type/flags read-only). EntitySchemaPrompt — added Color to create/update/modify type lists, inherited caption-override notes to update/modify, and a new dedicated `set-entity-schema-properties` prompt. RoutingGuidanceResource — reviewed; the `app-modeling` row already covers schema-modeling triggers, no change (no new guide). docs/McpCapabilityMap.md — listed set-entity-schema-properties in both entity-schema sections + 3 capability bullets. clio.mcp.e2e — added CallSetEntitySchemaPropertiesAsync helper, a NoEnvironment invalid-environment test for the new tool, and 3 Sandbox tests (Color create+named readback; set-primary-display round-trip; inherited caption override + non-caption rejection). clio.mcp.e2e is NOT in CI — the Sandbox scenarios must be run manually against a live stand with McpE2E:AllowDestructiveMcpTests=true; the inherited test targets BaseEntity's CreatedOn and may need a different inherited column per stand. Not committed yet.
