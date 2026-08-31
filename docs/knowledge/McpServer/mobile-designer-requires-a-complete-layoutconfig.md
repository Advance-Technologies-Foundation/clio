---
description: the Freedom UI Mobile designer refuses to open a page whose layoutConfig omits colSpan or rowSpan, even though the runtime renders fine without them — every emitted placement must carry all four keys
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-96114
date: 2026-08-31
---

**What is true** — a `layoutConfig` the converter emits must carry all four keys: `row`, `column`,
`colSpan`, `rowSpan`. The mobile RUNTIME renders fine with only `row`/`column` — which is why they
were the only two written at first — but the Freedom UI Mobile **designer** then fails to open the
page. A partial placement is not a smaller placement; it is a page nobody can edit.

The boundary is COMPLETE-or-ABSENT, and it was measured, not reasoned: the designer opens a page whose
`crt.GridContainer` child carries **no** `layoutConfig` at all (the synthesized tab Area has always been
that shape), and refuses one whose `layoutConfig` is partial. So the converter completes a placement
that exists and never invents one — filling every element would position things nobody asked to
position and change layouts that are correct today.

The two surfaces disagree, and only one of them is exercised by anything in the pipeline. This is a
sibling of `grid-containers-position-by-layoutconfig-not-item-order.md`: that record says WHEN a grid
child needs a placement, this one says what a placement must contain once it has one.

**Why it is this way** — `NormalizePlacements` enforces it once, over the whole element map, rather
than at each writer. The converter authors a placement from several places — the positional pass's
`SiblingSlot`, the anchor clone in `ShiftRows`, the per-breakpoint adaptive pass, the tab-area
stacking — and it also carries the WEB page's own `layoutConfig` **verbatim**, which may legitimately
declare only `row`/`column`. A child of a single-column web grid is touched by none of the placement
passes and keeps that carried object exactly as authored, so fixing the writers one at a time leaves
that path — and every future writer — behind. The pass runs after
`ApplyComponentPropertyOverrides` on purpose: that pass stamps rule-declared properties onto inserted
elements, so a rules file that ever declares a `layoutConfig` would otherwise write a partial one
after normalization had already run.

**What breaks if you ignore it** — the same silence as its sibling record, one surface over. The body
validates, saves, and reads back byte-identical; the conversion reports nothing; the app renders the
page. The failure appears only when somebody opens the converted page in the designer to edit it, and
nothing in the conversion output hints at the cause. Do not "simplify" a placement by dropping the
spans the runtime ignores — that is exactly the reasoning that produced this.
