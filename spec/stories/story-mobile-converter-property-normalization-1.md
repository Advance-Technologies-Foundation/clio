# Story 1: Stamp and report the property standards the conversion rules declare

**Feature**: mobile-converter-property-normalization
**FR coverage**: CAP-01 … CAP-07
**SPEC**: [spec-mobile-converter-property-normalization.md](../prd/spec-mobile-converter-property-normalization.md)
**Ticket**: ENG-94230
**Status**: review
**Size**: S
**Depends on**: ENG-91228 branch (componentPropertyOverrides pass)

## As a
developer converting a web page to mobile

## I want
every element the converter inserts to arrive with the mobile design standards the conversion RULES
declare already applied, and each standard reported on its own

## So that
nobody repeats the same manual fixes in the Mobile Designer after every conversion, and adding the next
standard is a rules-file entry rather than a clio release

> ENG-94230 asked for one standard — a metric with extra-small text and a hidden border. It is delivered
> as the FIRST consumer of this mechanism, declared entirely in the rules file, alongside the container
> spacing ENG-93153 introduced. Nothing about `crt.IndicatorWidget` is encoded in code.

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

Mechanism — the contract:

- [x] AC-01 — A rule stamps its declared values on every inserted element of its type; an absent or empty
  rule group is a no-op.
- [x] AC-02 — A merging rule targeting a nested leaf leaves the untargeted sibling subtrees of the same
  parent byte-identical.
- [x] AC-03 — A branch the element carries as a NON-object is never overwritten, and the refusal is
  reported in `skipped[]` with its path and a reason. An ABSENT branch is created.
- [x] AC-04 — Each declared `reportGroup` gets its own `normalizations` section; an unrecognized group
  gets its own key and never inherits another standard's note, constraint or next step.
- [x] AC-05 — `reportNote` / `reportConstraint` / `reportNextStep` from the rule appear in the section,
  in `constraints[]` and in `nextSteps[]`; rules-authored lines are attributable and length-bounded.
- [x] AC-06 — `normalized[].properties` lists only the paths actually written; a leaf already at the
  standard is not reported.
- [x] AC-07 — Adding a further standard needs no code change — no enum member, model type, prose string,
  tool `[Description]` edit or guidance-article edit.
- [x] AC-08 — `spacingNormalization` keeps its shape and content as a back-compat alias of the `spacing`
  section; `name`/`type` remain non-overridable.

ENG-94230's own standard, delivered by the above and declared only in the rules file:

- [x] AC-09 — A converted `crt.IndicatorWidget` carries `config.text.fontSizeMode == "extra-small"`.
- [x] AC-10 — The same insert carries `config.layout.border.hidden == true`.
- [x] AC-11 — `config.data.providing` and the other converted `config` subtrees survive the stamp.
- [x] AC-12 — `config.theme` is untouched (SPEC non-goal).

Process:

- [x] AC-13 — MCP surface review outcome stated in the change summary (guide response gained
  `normalizations`; the guidance article moved to clio-knowledge#39).
- [x] AC-14 — Docs review outcome stated in the change summary (no CLI verb or options class changed).
- [x] AC-15 — No new `CLIO*` analyzer warnings; SonarCloud reports no new issues.

## Tests
- `clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileConversionServiceTests.cs` —
  stamping, deep-merge preservation, identity guard, empty-group no-op, spacing section unaffected.
- `clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobilePageConversionRulesCatalogTests.cs` —
  the new rule parses, carries both values and its report grouping.
- `clio.mcp.e2e` — no ENG-94230-specific case. ENG-94188 (#927) moved every guidance article out of this
  repository into clio-knowledge, so the article wording this story changed is no longer ours to assert
  here; and the tool is non-resident, so its `[Description]` is not observable over `tools/list`. The
  wording is published instead as
  [clio-knowledge#39](https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/39)
  (generation 16), which must not be released before this branch merges. The existing e2e cases
  (discoverability, failure envelope, feature-flag gating) still cover the tool and are unaffected by
  this change.

Validated with: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`
