# SPEC: Mobile Page Converter — recommended style rule for Metric

**Created**: 2026-08-05
**Size estimate**: S (1-2 stories)
**Recommended next**: /bmad-spec is sufficient — proceed to story creation
**Ticket**: ENG-94230 (epic ENG-93494 "Converting Web page to Mobile page")

---

## Why

A web metric widget converts to mobile today as a `DirectMapping` — `crt.IndicatorWidget` exists in
the mobile registry, so `BuildComponentSuggestions` carries it over with the web `config` verbatim.
The web defaults it inherits (`text.fontSizeMode: "medium"`, no `layout.border`) do not follow the
mobile design standard, so every converted metric needs the same two manual fixes in the Mobile
Designer. The converter should stamp the mobile-standard style itself, the same way ENG-93153 made it
stamp container spacing.

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | Every inserted mobile metric carries the mobile-standard extra-small text size | Converting a web page with a metric yields an `elementMap` insert whose `mobileValues.config.text.fontSizeMode == "extra-small"` |
| CAP-02 | Every inserted mobile metric carries a hidden widget border | The same insert carries `mobileValues.config.layout.border.hidden == true` |
| CAP-03 | Stamping a nested value preserves the sibling values the converter already produced | After stamping, `mobileValues.config.data.providing` from the source page is byte-identical to its pre-stamp value |
| CAP-04 | The guide reports metric normalization separately from spacing normalization | The guide response carries the metric entries under their own section; `spacingNormalization` still lists only Grid/Flex containers and its summary text is unchanged |
| CAP-05 | The caller is told not to undo the stamped style | The guide carries a constraint forbidding restoration of the web font size / border, mirroring the existing spacing constraint |

## Constraints

- **C1**: `ApplyInsertValueOverrides` currently assigns rule values shallowly
  (`values[pair.Key] = JsonNode.Parse(...)`). Both target values are nested under `config`, so a
  shallow assign would replace the whole `config` object and destroy `config.data.providing` — the
  component treats an absent `config.data` as invalid and renders nothing. Nested stamping must merge,
  not replace.
- **C2**: Must not rename or re-shape the existing `spacingNormalization` guide section. It is part of
  the unmerged ENG-91228 / ENG-93153 contract this branch builds on; changing it would collide with
  that work and silently alter a shipped guide shape.
- **C3**: Element identity (`name`, `type`) stays non-overridable regardless of what the rules file
  says — the existing guard must survive the change.
- **C4**: The mechanism stays switched by DATA. An absent or empty rule group remains a no-op, and
  adding a further component's style must not require another code change.
  **MET after review (PR #1010).** It was not met in the first cut — the report sections were a closed
  enum and each section's prose was a string literal, so a further standard needed a new enum member, a
  new guide model pair, three prose strings, a tool `[Description]` edit and a guidance-article edit.
  Now `reportGroup` is a free-form key, the guide exposes one `normalizations` section per key with a
  single shared entry type, and each section's note / constraint / nextStep is carried by the rule that
  declared it. Adding a further component's style is a rules-file entry and nothing else. Two deliberate
  exceptions, both documented in code: `spacingNormalization` survives as a back-compat alias of the
  `spacing` section, and that one group keeps built-in wording as a fallback so a rules file predating
  `reportNote` does not silently lose the guidance it always had.
- **C5**: Values must be the registry's literals, not the ticket's prose: `fontSizeMode` accepts
  `extra-small|small|medium|large|extra-large` (so "XS" is `"extra-small"`), and hide-border is
  `layout.border.hidden` (`WidgetBorderConfig`), not a top-level `hideBorder`.

## Non-goals

- Will NOT set `config.theme`. The ticket names two settings; the default `without-fill` theme already
  yields the "Plain white" look together with a hidden border. Recorded as an assumption, not a change.
- Will NOT add a component-equivalence rule for the metric. It already converts as `DirectMapping`;
  adding a rule would reclassify it and change the suggestion output for no benefit.
- Will NOT touch merge twins, drops, or relocate hints — the override pass only ever sees `insert`
  operations and must keep it that way.
- Will NOT convert metrics that the source page does not contain. No synthesis.

## Success Signal

Running the conversion guide over a web page containing a metric returns an `elementMap` insert for
`crt.IndicatorWidget` whose `mobileValues.config` carries `text.fontSizeMode == "extra-small"` and
`layout.border.hidden == true` while retaining every other converted `config` subtree, and the guide
reports the change in its own normalization section without altering `spacingNormalization`.

---

## Companion Notes

**Chosen design.** Teach `ApplyInsertValueOverrides` a recursive object merge: when both the existing
value and the rule value are JSON objects, merge key-by-key instead of replacing; any other
combination keeps today's replace semantics (a scalar, array, or type mismatch still wins outright).
This keeps one mechanism for all insert-value stamping, which is the direction ENG-93153 established.

**Report routing.** To satisfy C2 without letting the report lie, each override rule declares which
report grouping it feeds. Grid/Flex keep the existing spacing grouping and its summary; the metric
rule declares its own. This is additive to the rules schema and to the guide response, so no existing
consumer shape changes.

**Open question (non-blocking).** "Plain white style" in the ticket is read as a description of the
result of hiding the border, not a third setting. If the reporter (Dmytro Snetenchuk) means a named
designer preset that also pins `config.theme`, CAP-01..CAP-05 are unaffected and a follow-up adds one
key to the same rule. The assumption is stated in the PR description.

**Registry provenance.** Property names and allowed values were read from
`get-component-info component-type=crt.IndicatorWidget schema-type=mobile`, which resolved
`latest` with `resolvedFrom: environment-superset` (the target stand's exact version is not published
on the CDN). `fontSizeMode` and `layout.border.hidden` are long-standing fields, not newly shipped
ones, so the superset warning does not put them at risk.
