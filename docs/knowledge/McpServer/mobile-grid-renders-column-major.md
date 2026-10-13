---
description: the mobile runtime draws a crt.GridContainer as independent column stacks weighted by `<n>fr` — no spans, no row alignment across columns — so a converted tablet grid is cut into bands of rows that share one column structure, each band with several columns becoming its own synthesized grid
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileConversionServiceTests.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileRealPageRegressionTests.cs
ticket: ENG-94835
date: 2026-10-13
---

**What is true** — the Creatio Mobile runtime (`mobile-app`, `grid_container_component.dart`) renders a
`crt.GridContainer` as a `Row` of independent `Column`s. Each entry of `columns` becomes one column:
`"<int>fr"` is a `Flexible` with that flex, a bare number a fixed width, anything else (`minmax(...)`,
`"1.5fr"`) flex 1. A child's `column` picks its stack and its `row` only orders it inside that stack:
row N of column 1 and row N of column 2 get no shared top or height, and no span is read at all.

A mobile registry published before the ENG-94835 mobile-app change says "only the count is used (ignore
fr values)". That text came from a stale doc comment in `grid_container_component_config.dart`; the code
has honoured the weights all along.

The Mobile Designer (creatio-ui) keeps column strings as they are on open and save, as long as the
container and every child carry all three breakpoints. It rewrites a breakpoint to N × `"1fr"` only
when a user edits that breakpoint's column count or flips "Use adaptive layout". Its canvas is a real
CSS grid, rows aligned, so its preview does NOT match the device.

**Why it is this way** — with no spans, the only lever for width is the container's columns. Adding
`colSpan` support to the mobile runtime is not planned (mobile team decision, 2026-10-12), so this is
the converter's job; if the runtime ever honours spans, revisit the bands below. A child can only be
widened by giving it a grid of its own columns, so `SplitIntoBands` cuts the web grid into runs of
consecutive rows in which no child covers another child's start column (rows a `rowSpan` covers stay
together, because the runtime's independent column stacks already draw a spanned child beside a stack).
One band keeps one weighted grid. Several bands make the container one column: a band with one start
column keeps its children there at full width, a band with several becomes a new `crt.GridContainer`
`<container>_TabletBand<N>` weighted by its own spans. On the Keimfarben and VTQ corpora (1 151 grids)
94 % of grids come out unchanged; the rest are mostly paired fields above a full-width list or Details
field, which the old one-grid rule left at half width.

Banding is skipped — the grid falls back to the one-reference-row grid in `BuildTabletGrid` — when the
container is not an inserted `crt.GridContainer` (a template grid may own children the converter never
sees, which a one-column container would misplace), a child is a template twin (a merge cannot be
re-parented), two children of one row overlap, a `rowSpan`-covered row breaks its band,
or a band has more than 12 start columns. In that fallback only a list (`crt.List`,
`crt.FileList`) wider than its slot merges the slots it covers; a wide field just takes its slot.

**What breaks if you ignore it** —
- Emit equal `1fr` columns from the web column count, as the converter did before ENG-94835, and a
  full-width web list renders at 1/N width beside empty columns.
- Trust the registry text and drop the weights, and a 4:1 web form renders 1:1, squeezing its fields
  into overflow.
- A `crt.Toggle` is a fixed 44×26 dp switch, so a column narrower than that squeezes it into a circle
  or a sliver: that is a too-narrow column, not a Checkbox→Toggle mapping or widget defect.
- Widen a full-width child by merging the grid's columns instead of banding, and every form with a
  full-width Notes field or list loses its side-by-side tablet layout above it.
- Expect a band grid to exist on the web page: it is synthesized, has no web counterpart, and a later
  re-conversion recreates it under the same name.
- Expect fields and their checkboxes in neighbouring columns to line up row by row, and they will
  not: that is the runtime, not the converter.
