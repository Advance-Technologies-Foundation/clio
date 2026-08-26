---
description: InitializeContainerChildSlots must run after RemoveEmptyContainers and after BuildTabAreaLayers - RemoveEmptyContainers reads the items slot's ABSENCE as its emptiness signal, so declaring the slot earlier silently disables the whole removal pass
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
ticket: ENG-95573
date: 2026-08-25
---

**What is true** — the web-to-mobile converter declares a container's child-collection slots
(`items`, and any other slot a surviving child targets: `tools`, `menuItems`) in one pass,
`InitializeContainerChildSlots`, and that pass is ordered LAST among the element-map-mutating passes
for two independent reasons that are invisible at its own call site:

1. `RemoveEmptyContainers` decides "is this container empty?" by the **absence** of the `items` slot
   (`IsEmptyRemovalCandidate`). Declare the slot before it runs and every container looks occupied,
   so the removal pass silently becomes a no-op.
2. `BuildTabAreaLayers` is the only other pass that ADDS insert entries which other inserts then
   target as parent (the synthesized tab-body grid and Area card). Run the slot pass before it and
   those two layers ship with no declared slot — `SynthesizedLayerEntry` no longer compensates
   inline, on purpose, so one pass covers converted and synthesized containers alike.

**Why it is this way** — `BuildMobileValues` deliberately never carries a child array as a value
(children are emitted as their own element-map entries), and the Creatio differ resolves an insert's
target collection generically as `itemInfo.Item[propertyName]`, throwing
`NotContainerItemInsertException` for ANY slot it cannot find there — not just `items`. So the slot
has to be declared somewhere, and the only place that can see every parent-slot pair at once is a
single pass over the finished element map. Reusing the slot's absence as the emptiness signal was
cheaper than tracking occupancy separately, which is what couples the two passes.

**What breaks if you ignore it** — both failures are silent. Moving the pass earlier keeps every
empty converted container on the page (no error, no warning: the guide just carries containers the
designer will render empty), and the only signal is
`Analyze_ShouldCascadeBothLevelsToDrop_WhenItemsSlotPassRunsAfterRemoval` in
`WebToMobileConversionServiceTests`. Moving it before `BuildTabAreaLayers` produces a guide whose
synthesized tab layers are refused by the differ at apply time with
`Item "MainTabContainer_x" is not a container for other items` — the exact production error this
ticket fixed, reintroduced one level up.
