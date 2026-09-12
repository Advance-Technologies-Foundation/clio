---
description: the web-to-mobile converter decides several things by asking "does this type exist in the mobile registry", so ENG-91859 growing that catalog by roughly a factor of two silently flips those decisions from the safe branch to the confident one - a class of regression, not one bug
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
ticket: ENG-91859
date: 2026-09-03
---

**What is true** — several converter decisions are written as "is this web type present in the
mobile registry", and they were authored while the published mobile catalog held 46 types extracted
from the *web* monorepo. ENG-91859 replaces that producer with one that scans the Flutter runtime
and publishes substantially more (95 components / 64 requests as the generator stood on 2026-09-09 —
a moving number until the producer publishes, so treat it as a magnitude, not a pin). Every such test
that was reliably FALSE becomes TRUE, and the converter moves from its cautious branch to its
confident one for dozens of types at once.

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

**Follow-up (2026-09-07)** — resolving the twin's mobile type from the template probe fixed the
wrong payload but left the list with no row at all: the rules file HAS the grid → list
`crt.ListItem` template, and it was applied only on the INSERT path, while a list page's mobile
template already provides the `List`, so the grid converts by merge-by-name.

The row now arrives, but NOT on the twin. A structural twin carries nothing of its own: the
structure the rules declare for it is a separate named element the template already provides in a
single-object slot, so it is emitted as its OWN merge entry addressed by that element's name (read
from the probe's `SlotElementsByOwner`, never assumed). Putting it in the parent's merge values
instead is a silent no-op — `crt.List` is not a container, `itemLayout` is an input, and the differ
discards a merge property whose slot already holds a named element, which every OOTB mobile list
template fills. See `a-row-belongs-to-the-template-element-not-the-parent-merge.md`.

**What breaks if you ignore it** — the failure is silent in both directions. A wrong merge writes
properties the target component does not declare, and the mobile runtime ignores them rather than
erroring, so the page saves, validates and opens; only the missing UI shows it. And the converter's
own tests do not catch it, because they construct a small mobile type set by hand: the existing
`Analyze_TemplateComponentTwin_IsKeptAndMergedByName_NoHardcodedTransform` never put
`crt.DataGrid` in the mobile registry, so it passed before and after the regression. When you touch
a decision that reads `ctx.MobileTypes`, add a case whose mobile type set contains the web type -
otherwise the test proves the opposite of what it reads as proving.
