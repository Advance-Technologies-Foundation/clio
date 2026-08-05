# Story 1: Stamp the mobile-standard style on converted metrics

**Feature**: mobile-converter-metric-style-rule
**FR coverage**: CAP-01, CAP-02, CAP-03, CAP-04, CAP-05
**SPEC**: [spec-mobile-converter-metric-style-rule.md](../prd/spec-mobile-converter-metric-style-rule.md)
**Ticket**: ENG-94230
**Status**: in-progress
**Size**: S
**Depends on**: ENG-91228 branch (insertValueOverrides pass)

## As a
developer converting a web page with a metric to mobile

## I want
the converted `crt.IndicatorWidget` to arrive with extra-small text and a hidden border already set

## So that
I do not repeat the same two Mobile Designer fixes on every conversion, and the result follows the
mobile design standard without the agent being told to do it in prose

## Design
- `WebToMobileAnalysisService.ApplyInsertValueOverrides`: replace the shallow
  `values[pair.Key] = JsonNode.Parse(...)` assignment with a recursive object merge. Two JSON objects
  merge key-by-key; every other combination keeps replace semantics. The `name`/`type` identity guard
  stays ahead of the merge.
- `WebToMobilePageConversionRulesModels`: add the optional report-grouping field to
  `InsertValueOverrideRule` so a rule declares which normalization section it feeds.
- `Data/WebToMobilePageConversionRules.json`: add the `crt.IndicatorWidget` override —
  `config.text.fontSizeMode = "extra-small"`, `config.layout.border.hidden = true` — with a `note`
  explaining that the web font size and border are ignored, not translated. Existing Grid/Flex rules
  declare the spacing grouping explicitly so their reporting is unchanged.
- `MobilePageConversionGuideModels`: add the metric normalization section to the guide response
  alongside `SpacingNormalization`, with XML docs matching the existing style.
- `FreedomToMobileConversionGuidanceResource`: document the stamped metric style and add the
  "do not restore the web font size / border" constraint, mirroring the spacing wording.
- Non-goal guard: do not set `config.theme` (see SPEC non-goals).

## Acceptance Criteria
- [ ] AC-01 — A converted metric insert carries `mobileValues.config.text.fontSizeMode == "extra-small"`.
- [ ] AC-02 — The same insert carries `mobileValues.config.layout.border.hidden == true`.
- [ ] AC-03 — Sibling `config` subtrees produced by conversion (notably `config.data.providing`) survive
  the stamp unchanged.
- [ ] AC-04 — `spacingNormalization` still lists only Grid/Flex containers, with its summary text
  unchanged; metric entries appear in their own section.
- [ ] AC-05 — The guide carries a constraint forbidding restoration of the web font size / border.
- [ ] AC-06 — An empty/absent override group remains a no-op; `name`/`type` remain non-overridable.
- [ ] AC-07 — MCP surface review outcome stated in the change summary (guide response gained a section).
- [ ] AC-08 — Docs review outcome stated in the change summary (no CLI verb changed).
- [ ] AC-09 — No new `CLIO*` analyzer warnings; SonarCloud reports no new issues.

## Tests
- `clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileConversionServiceTests.cs` —
  stamping, deep-merge preservation, identity guard, empty-group no-op, spacing section unaffected.
- `clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobilePageConversionRulesCatalogTests.cs` —
  the new rule parses, carries both values and its report grouping.
- `clio.mcp.e2e/MobilePageConversionGuideToolE2ETests.cs` — the live guide response carries the metric
  normalization section and its constraint.

Validated with: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`
