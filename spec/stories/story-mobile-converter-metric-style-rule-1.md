# Story 1: Stamp the mobile-standard style on converted metrics

**Feature**: mobile-converter-metric-style-rule
**FR coverage**: CAP-01, CAP-02, CAP-03, CAP-04, CAP-05
**SPEC**: [spec-mobile-converter-metric-style-rule.md](../prd/spec-mobile-converter-metric-style-rule.md)
**Ticket**: ENG-94230
**Status**: review
**Size**: S
**Depends on**: ENG-91228 branch (componentPropertyOverrides pass)

## As a
developer converting a web page with a metric to mobile

## I want
the converted `crt.IndicatorWidget` to arrive with extra-small text and a hidden border already set

## So that
I do not repeat the same two Mobile Designer fixes on every conversion, and the result follows the
mobile design standard without the agent being told to do it in prose

## Design
Rewritten after two review rounds on PR #1010; this section describes what shipped, not the first cut.

- `WebToMobileAnalysisService.ApplyComponentPropertyOverrides` stamps every standard the rules file
  declares. An object rule value MERGES into the element's own object when the rule sets
  `mergeNestedObjects`; every other shape replaces, which is what the pre-existing spacing rules rely on.
- The merge never OVERWRITES a value that is present but is not an object (a whole-value binding), at any
  depth; such a branch is recorded in the report's `skipped[]`. An ABSENT branch IS created — a real
  metric carries `layout` with a colour and icon but no `border`, so refusing would make the standard
  unreachable. Leaves are written only when the value actually differs.
- `WebToMobilePageConversionRulesModels`: the rule carries `reportGroup` (a FREE-FORM key), its own
  caller-facing `reportNote` / `reportConstraint` / `reportNextStep`, and `note` for the rationale.
- `Data/WebToMobilePageConversionRules.json`: the `crt.IndicatorWidget` rule declares
  `config.text.fontSizeMode = "extra-small"` and `config.layout.border.hidden = true`, merging, in group
  `metricStyle`. The spacing rules keep the default group and replace semantics, and now carry their own
  prose. `config.theme` is deliberately untouched (see SPEC non-goals).
- `MobilePageConversionGuideModels`: one shared `NormalizationInfo` / `NormalizationEntry` /
  `NormalizationSkip`, surfaced as `normalizations: { "<group>": ... }`. `spacingNormalization` survives
  as a back-compat alias of the `spacing` section, shape unchanged.
- Groups are declared in RULES-FILE order, so the emitted key spelling, the prose a group carries when
  several rules feed it, and the section order never depend on page content.
- `FreedomToMobileConversionGuidanceResource` and the tool `[Description]` describe the MECHANISM and
  route the caller to the report; neither names a component, a property or a value, because the rules
  file is resolved at runtime and would drift from them.

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
- `clio.mcp.e2e` — no ENG-94230-specific case. ENG-94188 (#927) moved every guidance article out of this
  repository into clio-knowledge, so the article wording this story changed is no longer ours to assert
  here; and the tool is non-resident, so its `[Description]` is not observable over `tools/list`. The
  article wording is handed off in
  [guidance-wording-handoff.md](../mobile-converter-metric-style-rule/guidance-wording-handoff.md) and
  needs its own clio-knowledge PR. The existing e2e cases (discoverability, failure envelope, feature-flag
  gating) still cover the tool and are unaffected by this change.

Validated with: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`
