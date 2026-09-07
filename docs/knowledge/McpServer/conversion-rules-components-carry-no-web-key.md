---
description: WebToMobilePageConversionRules.components entries carry no `web` key, so FindRule never matches against the shipped rules and the equivalence-rule branch is reachable only from hand-built test rules
applies-to:
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePageConversionRulesModels.cs
ticket: ENG-95827
date: 2026-09-07
---

**What is true** — all four `components` entries in the shipped
`WebToMobilePageConversionRules.json` are filter/template groups (`filters` + `viewConfigTemplates`,
one also `path`). **None carries a `web` key.** `ComponentEquivalenceRule.Web` defaults to `[]`, and
`FindRule` matches on that key alone, so against the bundled rules it **always returns `null`**.

Two things follow. The equivalence-rule branch — `rule.Category`, `rule.Mobile`, `rule.Note`,
`BuildPrimaryWebMergeNote` — is exercised **only by hand-built test rules**, never in production;
`primaryWebMerge`, `note` and the `AlternativeAvailable`-from-a-rule path therefore never appear on a
real response. But `FindRule` itself is **not** dead code: it has a second call site in
`ResolveConvertedMobileType`, where `rule.Mobile` is the last fallback for a leaf's target type. So a
published rules file carrying a `web` key would change **which types convert**, not just what the
advisory sections say. Do not refactor `FindRule` away on the strength of the first call site.

The way a grid actually becomes a `crt.List` is a third lookup, touching neither: `ResolveTemplateTargetType`
reads the first `viewConfigTemplates[].value.type` of the entry whose `filters`/`path` match, via
`RuleAppliesTo`.

**Why it is this way** — the `web`/`mobile` pair was the original v1 matrix shape. The rules file
then moved to filter/template groups, which express placement and a full value skeleton rather than
a type pair, and the pair-based reader was left in place because the tests kept it green.

**What breaks if you ignore it** — you will read `BuildComponentSuggestions` as rules-driven and
conclude a wrong suggestion is a data problem you can fix by authoring rules. It is not: with no
`web` key, nothing you write in `components` reaches the classification. This cost real conversions
— the advisory sections were classified from bare registry membership and disagreed with the diff
about the reference page's five largest elements, which starved `mobileContracts` of the `crt.List`
contract and blocked `update-page --dry-run` with eight unresolved-binding errors.

Since ENG-95827 the classification is derived from the finished element map and a matching rule may
only **add** mobile types the diff emits no operation for (a `crt.List`'s `crt.ListItem` lives inside
`itemLayout`) plus advisory text. So publishing a rules file that finally carries a `web` key can no
longer relabel a conversion that already happened — but it still will not make the pair the source
of truth, and a test that asserts a category through a hand-built `Web` rule is testing a path
production does not take.
