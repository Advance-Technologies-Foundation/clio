---
description: the web-to-mobile converter decides several things by asking "does this type exist in the mobile registry", so ENG-91859 growing that catalog from 46 to 87 types silently flips those decisions from the safe branch to the confident one - a class of regression, not one bug
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
ticket: ENG-91859
date: 2026-09-03
---

**What is true** — several converter decisions are written as "is this web type present in the
mobile registry", and they were authored while the published mobile catalog held 46 types extracted
from the *web* monorepo. ENG-91859 replaces that producer with one that scans the Flutter runtime
and publishes 87. Every such test that was reliably FALSE becomes TRUE, and the converter moves
from its cautious branch to its confident one for ~41 types at once.

Measured on a real page before the fix: `crt.DataGrid` went from `Unsupported` (advisory merge, no
payload) to `DirectMapping`, and the `DataTable -> List` twin carried DataGrid-shaped values
(`columns`, `features`, `bulkActions`, `selectionState`, `_designOptions`, `_selectionOptions`)
onto an element that is a `crt.List` and declares none of them. The converted list rendered with no
row. Nothing failed, nothing warned.

**Why it is this way** — presence in the registry was a serviceable proxy for "the mobile side
supports this" while the catalog was small and web-derived. It answers a different question from the
one the converter needs: whether the component exists SOMEWHERE on mobile, not whether it is what
sits at the target position. The mobile template probe (`mobileTemplateTypesByName`) is the
authority for the latter and must be preferred wherever it is available.

**What breaks if you ignore it** — the failure is silent in both directions. A wrong merge writes
properties the target component does not declare, and the mobile runtime ignores them rather than
erroring, so the page saves, validates and opens; only the missing UI shows it. And the converter's
own tests do not catch it, because they construct a small mobile type set by hand: the existing
`Analyze_TemplateComponentTwin_IsKeptAndMergedByName_NoHardcodedTransform` never put
`crt.DataGrid` in the mobile registry, so it passed before and after the regression. When you touch
a decision that reads `ctx.MobileTypes`, add a case whose mobile type set contains the web type -
otherwise the test proves the opposite of what it reads as proving.
