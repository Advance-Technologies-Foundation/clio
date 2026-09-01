---
description: the excludedComponents pass records its verbatim-carry (Phase B) removals in the removed-web-name set even though nothing reads them today - the attribute-consumer walk descends items only, and a Phase B node is never under items, so the symmetry is insurance against that walk changing
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/ExcludedComponentsPass.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-95081
date: 2026-08-26
---

**What is true** — `ExcludedComponentsPass.RemoveExcludedComponents` returns the web names of everything
it removed, and `BuildMobileViewModelConfig` subtracts that set from the drop entries it scans so an
attribute referenced only by an excluded element is KEPT (removal is layout cleanup, not attribute
cleanup). Both phases feed the set — but only Phase A's contribution can currently change any output.

A Phase B (verbatim-carry) name is unreachable by the pruning logic, and the reason spans two files:

- `WalkConsumers` (in `WebToMobileAnalysisService`) attributes every `$Attr` reference to a NAMED node,
  and it descends `items` **only**. `ExtractConsumedAttributes` removes `items` from the node it
  serialises but keeps every other property — so a `$Attr` sitting anywhere inside a node's `tools`,
  `menuItems` or any custom slot is credited to that node, not to the component that actually carries it.
- A Phase B node is by construction NOT under `items`: `items` children are always walked into their own
  element-map entries (`WalkElements`), which is Phase A's shape. Only non-`items` slots reach
  `RecurseChildArrays`/`IsChildElementArray` and can be left verbatim.

Put together: a Phase B node never appears as a consumer under its own name, so `users.All(dropped.Contains)`
never sees it, so its attributes survive on the host's account whether or not the pass reports the name.

**Why it is this way** — the symmetry is kept anyway. The two phases are one rule applied to two shapes the
mobile component registry chooses between, not two features; letting them report through different channels
means the same rules-file entry can behave differently on two pages for reasons the rule author cannot see.
The cost is one set insert per removal.

**What breaks if you ignore it** — two directions, both quiet:

- **Deleting the Phase B recording** as dead code passes every test today
  (`RemoveExcludedComponents_ShouldReportBothPhases_ThroughTheWebNameSet` is the only thing that fails, and
  it asserts the contract at the pass boundary, not an observable conversion difference). It becomes a real
  defect the moment `WalkConsumers` learns to descend `tools`/`menuItems` — which is the natural fix for the
  mis-attribution described above. The symptom then is an attribute pruned out of `mobileViewModelConfig`,
  i.e. a converted page whose `visible`/access binding silently resolves to nothing.
- **Writing an Analyze-level test to "prove" the Phase B attribute path** produces a test that passes with
  the recording removed. It is not testing what it looks like it is testing. Pin this contract at the pass
  boundary instead.
