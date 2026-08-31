---
description: a mobile crt.GridContainer positions children by layoutConfig only — items order does nothing AND an unpositioned child is not auto-placed into a free cell, so both the sibling and the anchor need explicit rows
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideTool.cs
ticket: ENG-96114
date: 2026-08-28
---

**What is true** — a mobile `crt.GridContainer` positions its children by `layoutConfig` and by nothing
else. Two consequences, and the second one cost two rounds to learn:

1. The `items` order does not position anything. A template child that pins `row: 1` keeps row 1
   however early an inserted sibling's `index` is.
2. A child with **no** `layoutConfig` is **not** auto-placed into the free cell the way CSS grid
   auto-placement would. Measured on a real converted page (`UsrOrders_MobileFormPage`,
   env `seeenu_15934775`): with the merged tree confirmed as
   `MainContainer.items = [ProgressBarContainer (no layoutConfig), Tabs (row 2)]` — i.e. the row
   genuinely freed — the progress bar still rendered below the tab strip.

So placing content above an anchor takes BOTH halves: an explicit row on the sibling AND the anchor
moved down. And a `layoutConfig` must be COMPLETE: write `row`, `column`, `colSpan` and `rowSpan`
every time. The runtime renders fine without the spans, but the Freedom UI Mobile **designer** fails
to open a page whose `layoutConfig` omits them — a partial placement is not a smaller placement, it is
a page nobody can edit. `NormalizePlacements` enforces that once, last, over the whole element map:
the converter authors placements from several places (the positional pass, the anchor clone, the
adaptive pass, the tab-area stacking) and it also carries the WEB page's own `layoutConfig` verbatim,
which may legitimately declare only `row`/`column`. Fixing the writers one by one leaves that carry
path — and every future writer — behind.

**Why it is this way** — the web side gives no hint: there the same wrapper lives in a
`crt.FlexContainer`, where DOM order *is* visual order, so no child carries a `layoutConfig` for
`BuildMobileValues` to copy. Web flex → mobile grid is the whole trap, and it is invisible in either
tree read on its own. `PlacePositionalGroups` takes the ANCHOR's own template placement as the origin —
its row is the first row the group occupies, and its shape (flat, or per-breakpoint `layoutConfig.adaptive`)
decides the shape written onto the siblings, since the runtime resolves from `adaptive` when it is
present. Nothing is assumed: the placement is read from the live mobile template
(`CollectLayoutConfigByName`), so no container name, component type or row number lives in code. An
anchor whose template declares no row is left alone with its group — that parent positions by item
order, where the index arithmetic already is the whole placement.

**What breaks if you ignore it** — the misplacement is completely silent. The body passes
`validate-page`, passes `update-page --dry-run`, saves, and reads back byte-identical to what was sent;
`elementMap` looks right because the index is right; and `get-page` confirms the merged tree is exactly
what was intended. Only the Creatio Mobile app shows it. Do not treat a correct merged tree as proof
that placement works — that is precisely the inference that failed here.
