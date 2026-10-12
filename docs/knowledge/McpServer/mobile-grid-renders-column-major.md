---
description: the mobile runtime draws a crt.GridContainer as independent column stacks weighted by `<n>fr` — no spans, no row alignment across columns — so a converted tablet grid takes its column count and weights from one web row, and a list wider than its slot merges the columns it covers
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileConversionServiceTests.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileRealPageRegressionTests.cs
ticket: ENG-94835
date: 2026-10-09
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
the converter's job; if the runtime ever honours spans, revisit the slot merging below. That is
why `BuildTabletGrid` builds the tablet grid from ONE web row (the one with the most surviving
children) and turns that row's `colSpan`s into weights. A child elsewhere that spans further than its
slot cannot be widened. For a field that is accepted: the field gets one slot. For a list
(`crt.List`, `crt.FileList`) it is not, because a half-width list is the defect ENG-94835 reported,
so the slots it covers merge into one column carrying their weights (all of them when it spans the
whole grid, which stacks the grid like the phone). Only a list that is a DIRECT child of the grid counts:
a list wrapped in a FlexContainer or an ExpansionPanel still gets one slot. That is a known limit, left
in place because none of the converted pages measured for ENG-94835 has that shape.

**What breaks if you ignore it** —
- Emit equal `1fr` columns from the web column count, as the converter did before ENG-94835, and a
  full-width web list renders at 1/N width beside empty columns.
- Trust the registry text and drop the weights, and a 4:1 web form renders 1:1, squeezing its fields
  into overflow.
- A `crt.Toggle` is a fixed 44×26 dp switch, so a column narrower than that squeezes it into a circle
  or a sliver: that is a too-narrow column, not a Checkbox→Toggle mapping or widget defect.
- Merge the columns under every wide child, not just under a list, and every form with a full-width
  Notes field loses its side-by-side tablet layout.
- Expect fields and their checkboxes in neighbouring columns to line up row by row, and they will
  not: that is the runtime, not the converter.
