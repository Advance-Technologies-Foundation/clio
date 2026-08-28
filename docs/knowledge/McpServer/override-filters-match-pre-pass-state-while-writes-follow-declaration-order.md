---
description: componentPropertyOverrides rules are selected only by filters (the type is one constraint, shared with the components group); filters match the element as it entered the pass, but the matching rules write in rules-file declaration order
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePageConversionRulesModels.cs
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
ticket: ENG-95684
date: 2026-08-27
---

**What is true** — a `componentPropertyOverrides` rule is selected ONLY by its `filters`; the rule has
no `type` field. The component type is one filter constraint among others
(`"filters": [{ "type": "crt.GridContainer" }]`), evaluated by the same code and the same rule as
`ComponentEquivalenceRule.Filters`. Several rules may therefore target one type, and every rule that
matches is applied. The two orders involved are deliberately different:

- **Matching** is snapshot-based. Every rule is evaluated against the element's mobile values as they
  ENTERED the pass, before any rule writes. Declaration order cannot change which rules match.
- **Writing** follows the rules file's array order. Two matching rules that write the same key resolve
  last-declared-wins per key, and a later rule sees what an earlier one wrote.

This replaces the earlier invariant, where the rule carried its own `type`, `byType` was a
`Dictionary<string, Rule>`, and a duplicate `type` silently LAST-WINS — a second rule for a type used to
make the first one vanish entirely.

A filter entry that declares nothing — no type, no value — matches NOTHING. A rule with NO filters at
all is the opposite: it matches every insert of EVERY type, mirroring how the components group reads an
unfiltered entry. That is almost never intended, so `LoadBundled_OverridesCarryDataOnly` requires every
bundled rule to declare at least one filter and every filter to name a type. Neither guard reaches a
rules file served from the CDN.

`ElementFilterRule` is SHARED between the two groups and both sides run one match rule (entries OR-ed,
each AND-ing every constraint it declares, deep equality on values). They differ only in what they read:
the components group matches the SOURCE web node (Newtonsoft `JObject`), the override group matches the
element's TARGET mobile values (STJ `JsonObject`). One comparer serves both — the Newtonsoft token is
adapted through `ToJsonNode` lazily, so a type-only filter never pays for the round-trip. Sharing brings
a casing rule with it: `type` is compared case-INSENSITIVELY (the components group always did), while a
value constraint is compared case-sensitively — one is an identifier, the other is data.

**Why it is this way** — a lazily evaluated filter would let an unrelated rule enable or disable a
narrowed one by touching the property it filters on (a rule stamping `borderRadius: large` would stop
a rule filtered on `borderRadius: medium` from ever matching). The outcome would then depend on the
order the file happens to list rules in, which is exactly the thing a rules file is supposed to make
explicit rather than emergent. Writing, by contrast, is a genuine authoring decision — the file orders
it, the same way `mergeNestedObjects` stays per-rule.

**What breaks if you ignore it** — author a narrowed rule expecting it to see the accumulated state
(e.g. "promote whatever the spacing rule just produced") and it silently never fires, because the
filter was decided earlier; the element ships un-normalized and the `normalizations` section simply
omits it, so nothing looks wrong. Omit `filters` meaning "this rule has no extra conditions" and the
rule stamps onto every component type on the page. And do not reach for a `type` field on the rule —
there is none; a rule that targets a type outright and one narrowed to a subset of it are the same
construct, which is the whole point of the shared shape.
