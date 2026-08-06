# SPEC: Mobile Page Converter — rules-declared property normalization

**Created**: 2026-08-05
**Size estimate**: S (1-2 stories)
**Recommended next**: /bmad-spec is sufficient — proceed to story creation
**Ticket**: ENG-94230 (epic ENG-93494 "Converting Web page to Mobile page")

> **Scope note.** ENG-94230 asked for one thing — a metric style — but the delivered capability is the
> MECHANISM: the converter stamps whatever mobile design standards the conversion rules declare, and
> reports each one. The metric is simply the first standard to use it, alongside the container spacing
> that ENG-93153 introduced. This SPEC is deliberately written at the mechanism level; anything specific
> to `crt.IndicatorWidget` appears only as that first standard's data, never as the contract.

---

## Why

A converted element inherits the web page's values for properties the mobile design standard fixes, so
someone repeats the same manual fixes in the Mobile Designer after every conversion. ENG-93153 solved
this once for container spacing by stamping gap Medium in the converter. ENG-94230 needs the same for a
metric (`crt.IndicatorWidget`: extra-small text, hidden border) — and the second instance is the signal
that the answer is a mechanism, not another special case. The rules file is resolved at runtime
(env var → cache → CDN → bundled), so a standard must be expressible as DATA, including its
caller-facing wording; otherwise every new standard costs a clio release.

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | A rule declares a property standard and the converter stamps it on every element it inserts of that type | A rule for a type yields, on each inserted element of it, `mobileValues` carrying the declared values |
| CAP-02 | A standard can target a NESTED property without destroying the siblings the converter already produced | With a rule targeting a nested leaf, the untargeted subtrees of the same parent are byte-identical to their pre-stamp values |
| CAP-03 | A value the element carries but cannot be merged into is never overwritten, and the caller is told | An element carrying a non-object where the rule needs to merge keeps it verbatim and appears in the report's `skipped[]` with its path and a reason |
| CAP-04 | Each standard reports through its OWN section, and a section this build has never heard of is not folded into another's | The guide exposes one `normalizations` section per declared `reportGroup`; an unrecognized group gets its own key and carries only its own rule's wording |
| CAP-05 | The caller-facing wording of a standard travels with the standard, not with the binary | A rule's `reportNote` / `reportConstraint` / `reportNextStep` appear in its section, in `constraints[]` and in `nextSteps[]`; text authored by the rules is attributable as such |
| CAP-06 | Adding a further standard requires no code change | A rules-file entry alone produces a new section, its constraint and its next step |
| CAP-07 | ENG-94230's own standard is delivered by that mechanism | A converted `crt.IndicatorWidget` carries `config.text.fontSizeMode == "extra-small"` and `config.layout.border.hidden == true`, declared entirely in the rules file |

## Constraints

- **C1**: The pre-existing pass assigned rule values shallowly. A nested standard needs merge semantics,
  but merging must be OPT-IN per rule: the existing spacing rules have object-valued `gap`, and a global
  merge would silently let unnamed web gap keys survive, breaking their documented promise that the web
  value is discarded rather than translated.
- **C2**: Must not re-shape the existing `spacingNormalization` guide section. It is part of the unmerged
  ENG-91228 / ENG-93153 contract this branch builds on. It survives as a back-compat alias of the
  `spacing` section, unchanged in shape and content.
- **C3**: Element identity (`name`, `type`) stays non-overridable regardless of what the rules file says.
- **C4**: The mechanism is switched by DATA end to end — values AND reporting. An absent or empty rule
  group is a no-op, and adding a standard must not require an enum member, a model type, a prose string,
  a tool `[Description]` edit or a guidance-article edit.
  **MET.** Not met in the first cut, where sections were a closed enum and each section's prose was a
  literal; corrected under review (PR #1010). Two deliberate exceptions, both documented in code: the
  `spacingNormalization` alias per C2, and built-in wording for the `spacing` group alone as a fallback,
  so a rules file predating `reportNote` does not silently lose guidance it always had.
- **C5**: A standard's values are the mobile registry's literals, never a ticket's prose. For ENG-94230:
  `fontSizeMode` accepts `extra-small|small|medium|large|extra-large` (so "XS" is `"extra-small"`), and
  hide-border is `layout.border.hidden` (`WidgetBorderConfig`) — there is no top-level `size`/`hideBorder`.
- **C6**: Rules-supplied text reaches `constraints[]` and `nextSteps[]`, the arrays a caller treats as
  clio's own hard rules, from a file resolved at runtime. It must therefore be attributable and bounded.

## Non-goals

- Will NOT set `config.theme` for the metric standard. ENG-94230 names two settings; the default
  `without-fill` theme already yields the "Plain white" look together with a hidden border. Recorded as
  an assumption, not a change.
- Will NOT add a component-equivalence rule for the metric. It already converts as `DirectMapping`;
  a rule would reclassify it and change the suggestion output for no benefit.
- Will NOT touch merge twins, drops, or relocate hints — the pass only ever sees `insert` operations.
- Will NOT synthesize an element a source page does not contain.
- Will NOT let a rule create an object branch that the element carries as a non-object, and will NOT
  claim a leaf was normalized when its value already matched the standard.

## Success Signal

A standard declared entirely in the conversion rules — component type, property paths, values, merge or
replace, report group and caller-facing wording — appears in the guide as its own `normalizations`
section listing the exact paths written per element and anything it had to skip, contributes its own
constraint and next step, and requires no change to clio's code. ENG-94230's metric standard is
delivered that way and nothing else.

---

## Companion Notes

**Why merge rather than replace.** Both of the metric's target properties live under `config`, so a
shallow assign would have replaced the whole object and destroyed `config.data.providing` — a widget
without `config.data` renders nothing. Merging is per-rule opt-in (see C1).

**Create versus overwrite.** A merging rule CREATES an absent branch — a real converted metric carries
`layout` with a colour and icon but no `border`, so refusing would make the standard unreachable on
every real page — but never overwrites a branch the element carries as a non-object, since that is
typically a whole-value binding. The refusal is reported rather than silent.

**Registry provenance.** The metric's property names and allowed values were read from
`get-component-info component-type=crt.IndicatorWidget schema-type=mobile`, which resolved `latest`
with `resolvedFrom: environment-superset` (the target stand's exact version is not published on the
CDN). `fontSizeMode` and `layout.border.hidden` are long-standing fields, so the superset warning does
not put them at risk.

**Guidance lives elsewhere now.** ENG-94188 (clio#927) moved every guidance article out of clio into
[clio-knowledge](https://github.com/Advance-Technologies-Foundation/clio-knowledge). The article wording
for this mechanism is published as
[clio-knowledge#39](https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/39)
(generation 16), which must not be released before this branch merges.

**Open question (non-blocking).** "Plain white style" in ENG-94230 is read as describing the result of
hiding the border, not a third setting. If the reporter (Dmytro Snetenchuk) means a named designer
preset that also pins `config.theme`, no capability changes — it is one more key in the same rule.
