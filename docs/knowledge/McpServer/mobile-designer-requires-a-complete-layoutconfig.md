---
description: the Freedom UI Mobile designer refuses to open a page whose layoutConfig omits colSpan or rowSpan, and the runtime honours neither — every emitted placement carries all four keys and both spans are always 1, at every adaptive breakpoint
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileConversionServiceTests.cs
ticket: ENG-96114
date: 2026-09-28
---

**What is true** — a `layoutConfig` the converter emits must carry all four keys: `row`, `column`,
`colSpan`, `rowSpan`. The mobile RUNTIME renders fine with only `row`/`column` — which is why they
were the only two written at first — but the Freedom UI Mobile **designer** then fails to open the
page. A partial placement is not a smaller placement; it is a page nobody can edit.

The two spans exist for the designer alone, and **their only valid value is 1** (ENG-96589). The
runtime gives each item one cell and honours neither, so `colSpan: 2` is not a wider item but a
placement disagreeing with what renders; width at a breakpoint is the CONTAINER's column count.
Hence the asymmetry in `FillPlacementKeys`: a POSITION is the caller's own answer and is filled only
when absent, while both SPANS are written as 1 whatever they held and whoever declared them — a web
page, a mobile template's own anchor placement, or a value pasted into the tree. `WebPlacement` reads
no web span at all, so the guide's `adaptiveLayout` index, built from its own object rather than the
one pasted into values, cannot outlive the correction either.

The boundary is COMPLETE-or-ABSENT, and it was measured, not reasoned: the designer opens a page whose
`crt.GridContainer` child carries **no** `layoutConfig` at all (the synthesized tab Area has always been
that shape), and refuses one whose `layoutConfig` is partial. So the converter completes a placement
that exists and never invents one — filling every element would position things nobody asked to
position and change layouts that are correct today.

This is a sibling of `grid-containers-position-by-layoutconfig-not-item-order.md`: that record says
WHEN a grid child needs a placement, this one says what a placement must contain once it has one.

**Why it is this way** — `NormalizePlacements` enforces both halves once, over the whole element map,
rather than at each writer. The converter authors a placement from several places — the positional
pass's `SiblingSlot`, the anchor clone in `ShiftRows`, the per-breakpoint adaptive pass, the tab-area
stacking — and `BuildMobileValues` copies the WEB page's own `layoutConfig` wholesale, spans and all.
That copy is why the pass must be central: a child of a single-column web grid is touched by no
placement pass, so a web placement reaches the page as authored — missing `colSpan`, or claiming
`colSpan: 2`. The pass walks the WHOLE value tree, so a placement nested inside a carried value (a
list row's `itemLayout`, a menu item) is answered too — and the property prune cannot reach that one,
because the key is nested. It runs after `ApplyComponentPropertyOverrides` on purpose: that pass
stamps rule-declared properties onto inserted elements, so a rules file that ever declares a
`layoutConfig` would otherwise write a partial one after normalization had already run.

**What breaks if you ignore it** — a MISSING key and a WRONG value fail differently, and both are
silent in the conversion. Drop a key and the body still validates, saves and reads back
byte-identical while the designer refuses to open the page. Keep a web span and everything opens, and
the page simply renders one cell wide while its own placement claims otherwise. It is not a rare
shape: on one real converted form page ten elements carried a web span into `medium`/`large` — a
container claiming two columns, a list claiming thirteen rows — and every one of them rendered a
single cell. Do not "simplify" a placement by dropping the spans the runtime ignores, and do not
"preserve" a web span because it looks like information: both are the reasoning that produced this
record.
