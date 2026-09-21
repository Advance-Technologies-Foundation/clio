---
description: a crt.MenuItem carried verbatim inside a button's values never reaches ProcessEventBindings, so before ENG-96178 its unsupported request was neither converted, flagged nor dropped - and which of the two traversals runs is decided by the published registry, not by the page
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.DeadActions.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-96178
date: 2026-09-21
---

**What is true** — a button's `menuItems` reach the element map in one of two shapes, and only one of them
passes through `ProcessEventBindings`:

- **entry graph** — every member resolves to a mobile type, `IsChildElementArray` accepts the slot, and
  `RecurseChildArrays` walks each menu item into its own element-map entry, where the walk's own gates see it;
- **verbatim carry** — a member does NOT resolve, so the slot is refused and `BuildMobileValues` copies the
  whole array into the owner's `values` untouched. Those nodes are never walked. Their requests are never
  converted, never flagged, never dropped.

The second is the NORMAL shape for `crt.MenuItem`, because the published mobile registry does not declare that
type (architecture doc §13). So on a real page the request on a menu item was invisible on every channel: the
six OOTB settings buttons of `Leads_FormPage` shipped ten dead export/import actions, and no field of the
response mentioned any of them. `RemoveDeadActions` exists because no guard placed in the walk can see this —
`ExcludedComponentsPass` split itself into the same two phases for the same reason.

**Why it is this way** — `IsChildElementArray` requires every member to resolve because it must be able to
RE-EMIT what it descends into. Walking a slot it cannot re-emit would strip it and leave, say, a dropdown with
an empty menu — so refusing is the conservative answer for LAYOUT. It is the wrong answer for ACTIONS, which
is the asymmetry: the same refusal that protects the menu from being lost also hides what is inside it.

**What breaks if you ignore it** — two directions, both quiet.

Writing the menu-item rule only into the walk (widening the leaf drop's type gate and stopping there) passes a
hand-built unit test and changes NOTHING on any real page, because a hand-built bundle usually declares
`crt.MenuItem` in its mobile-type set while production does not. The suite goes green over a fix that does not
fix the reported defect.

The gap is not only about REPORTING a dead request, either. Every rule the entry graph applies has to be
restated for the carried shape, because the two share no code path: "the owner is dead once its menu is
gone" has an entry-graph form (an element-map candidate) and a carried form (a post-order test inside the
values walk), and shipping only the first leaves a nested carried submenu on the page, empty.

Letting the two shapes report DIFFERENTLY is the mirror failure. Whether `crt.MenuItem` resolves is a fact
about the published registry — it can change when the producer ships a catalog, with no clio release and no
page edit — so any difference between the paths surfaces later as a response that changed under a caller for a
reason nothing in their page explains. This is why the pass mints no `droppedRequests` record even though it
could: the walk does not on the entry-graph path, so it must not on the carried one.
`Analyze_ShouldReportDeadActionsIdentically_InBothTraversalShapes_OnTheRealLeadsFormPageShape` pins the two
against each other on the real page; a per-shape test would not have caught it.
