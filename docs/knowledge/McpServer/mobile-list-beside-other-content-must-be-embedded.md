---
description: the mobile runtime treats a crt.List without scrollable false as infinite-height and passes that up to every ancestor, so a converted list that shares any container on its way to the tab gets only the height its siblings leave; the converter embeds such a list (scrollable false)
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio.tests/Command/McpServer/Tools/MobilePageConverter/WebToMobileConversionServiceTests.cs
ticket: ENG-103470
date: 2026-10-15
---

**What is true** — in `mobile-app`, `ListTreeElement` marks a `crt.List` infinite-height unless
`scrollable` is `false`, and the config reads a missing `scrollable` as `true`. Every container passes an
infinite child up (`BaseContainerTreeElement.getIsInfiniteHeightByChildren`), so the tab around it stops
scrolling. A `crt.GridContainer` hands its child over directly only when it has one column and one item;
otherwise it stacks the children in `Column(mainAxisSize: min)` with the list in a `Flexible`, and the list
gets only what its siblings leave. `crt.ExpansionPanel` and a scrollable `crt.FlexContainer` stop the
propagation; `crt.FileList` is finite unless `scrollable` is `true`.

`EmbedListsSharingTheirScreen` therefore sets `scrollable: false` on an inserted `crt.List` unless every
container from the list up to its `crt.TabContainer` (or the page root) has exactly one child. Children the
mobile template already owns count as siblings. An explicit `scrollable` is left alone.

**Why it is this way** — the converter's `crt.DataGrid → crt.List` template sets no `scrollable`, and the
runtime decides layout from the whole subtree, not from the list's own parent: a list that is the only child
of a grid placed next to other content is still squeezed one level up. An embedded list renders the loaded
page plus a "Show more" button, which is also what the Mobile Designer toolbox inserts on a form.

**What breaks if you ignore it** —
- Leave `scrollable` out, and a converted detail list next to fields shows a blank slate that overflows
  ("bottom overflowed by N pixels") or rows squeezed into a small window, and the tab does not scroll.
- Check only the list's own parent, and a list nested alone in an inner grid beside other content is still
  squeezed.
- Set `scrollable: false` on a list that is the only content of its tab, and it loses its own scrolling and
  pull-to-refresh for nothing.
