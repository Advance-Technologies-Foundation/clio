---
description: componentPropertyOverrides filters are matched against the element as it entered the pass, but the matching rules then write in rules-file declaration order — so a rule can never disable a later one, yet two rules writing the same key still resolve last-declared-wins
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePageConversionRulesModels.cs
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
ticket: ENG-95684
date: 2026-08-26
---

**What is true** — a mobile type may carry SEVERAL `componentPropertyOverrides` rules, and the pass
applies every one whose `filters` match. The two orders involved are deliberately different:

- **Matching** is snapshot-based. Every rule of the type is evaluated against the element's mobile
  values as they ENTERED the pass, before any rule writes. Declaration order cannot change which
  rules match.
- **Writing** follows the rules file's array order. Two matching rules that write the same key
  resolve last-declared-wins per key, and a later rule sees what an earlier one wrote.

This replaces the earlier invariant, where `byType` was a `Dictionary<string, Rule>` and a duplicate
`type` silently LAST-WINS — a second rule for a type used to make the first one vanish entirely.
An empty filter bag matches NOTHING (not everything), and `type` inside a bag is never meaningful:
the rule is already selected by its own `type` before any filter is read.

**Why it is this way** — a lazily evaluated filter would let an unrelated rule enable or disable a
narrowed one by touching the property it filters on (a rule stamping `borderRadius: large` would stop
a rule filtered on `borderRadius: medium` from ever matching). The outcome would then depend on the
order the file happens to list rules in, which is exactly the thing a rules file is supposed to make
explicit rather than emergent. Writing, by contrast, is a genuine authoring decision — the file
orders it, the same way `mergeNestedObjects` stays per-rule.

**What breaks if you ignore it** — author a narrowed rule expecting it to see the accumulated state
(e.g. "promote whatever the spacing rule just produced") and it silently never fires, because the
filter was decided earlier; the element ships un-normalized and the `normalizations` section simply
omits it, so nothing looks wrong. In the other direction, assume the old one-rule-per-type shadowing
still holds and a second rule you added as a replacement will instead STACK on the first, applying
both sets of values. `WebToMobilePageConversionRulesCatalogTests.LoadBundled_OverridesCarryDataOnly`
still guards the bundled file against a duplicate UNFILTERED rule and against a `type` key inside a
filter bag; neither guard reaches a rules file served from the CDN.
