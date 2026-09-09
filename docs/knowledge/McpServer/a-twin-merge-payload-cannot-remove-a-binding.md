---
description: ProcessOneEventBinding is shared by two writers with OPPOSITE omission semantics - on the insert path a missing key removes the property, on the same-component twin path the values object is a delta MERGE payload where a missing key means "keep the mobile template's own value"
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-94839
date: 2026-09-09
---

**What is true** — `ProcessOneEventBinding` writes into a `values` object whose meaning depends entirely on
which caller supplied it. From the insert builder, `values` becomes the element's whole `mobileValues`, so
omitting a key removes that property. From `BuildDeltaTwinMergeValues` (a same-component twin, e.g.
`AttachmentList` → `AttachmentFileList`), `values` is a **delta merge payload**: an omitted key means "keep
the mobile template element's own value". So on the twin path a removal is not something the writer can
perform, and if the mobile template declares the same event the control keeps firing the template's
request. That is why the removal decision takes a `canRemoveBinding` parameter rather than being derived
from the target kind alone.

**Why it is this way** — the ENG-94839 report is recorded where the binding is WRITTEN, so that the report
is what the conversion did rather than a reconciliation against the finished element map (which needed
three suppression rules). Moving it there is right, but it put one decision inside a function two writers
share, and only one of them can honour it.

**What breaks if you ignore it** — add any other body edit to this function and the twin path reports it
without performing it. The concrete failure that already happened: a twin whose page-changed binding named
a dead target was reported `bindingRemoved: true` with a `droppedRequests` entry, and the guide told the
agent the control "renders and does nothing" for a merge whose payload never carried the binding. Worse,
when the stripped binding was the twin's ONLY delta, `BuildDeltaTwinMergeValues` returns null and the entry
degrades to an advisory merge with no `mobileValues` at all — while the report still points at
`elementMap[].mobileValues`. Pinned by `Analyze_TwinMergeTargetMissing_ReportsWithoutClaimingARemoval`.
