---
description: no component in the runtime-derived mobile catalog declares dataSourceName, so the converter's property prune removes it — deliberately overruling the older in-code claim that crt.Feed requires dataSourceName + entitySchemaName
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePropertyPrune.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobilePageConversionRulesModels.cs
ticket: ENG-96589
date: 2026-09-17
---

**What is true** — `dataSourceName` is declared by **zero** components in the runtime-derived
`MobileComponentRegistry` (neither `inputs` nor `outputs`, on any of the 66), and it is not in
`references.baseInputs`. The converter's prune therefore removes it from every converted element wherever
pruning is enabled, and reports it in `prunedProperties`.

This deliberately overrules two older statements that are still easy to find:

- `ExcludedSourceProps`' remarks used to say *"some components require it (e.g. `crt.Feed` needs
  `dataSourceName` + `entitySchemaName`)"*.
- ENG-96589's own ticket text names `crt.Feed.dataSourceName` as a must-NOT-drop regression case.

Both predate the runtime-derived catalog. The ticket's example was taken from
`MobileComponentRegistry.live-snapshot.json` as it stood at 35 curated entries, where `crt.Feed` did
declare `dataSourceName`. The catalog generated from `flutter_creatio` declares
`autoLoad, controller, controllerType, entitySchemaName, modelType, primaryColumnValue` — and no
`dataSourceName`.

**Why it is this way** — once the catalog is introspected from the runtime, "the runtime does not name
this key" is evidence, not absence of evidence. Keeping an exemption for `dataSourceName` would have meant
preferring a comment written against the old catalog over the new one's direct testimony, and an exemption
list that starts accepting entries on that basis has no principled end.

`entitySchemaName` — the other half of the old claim — **is** declared and survives. That asymmetry is the
point: the ENG-91859 fear was that pruning would take the whole Feed contract with it, and it does not.

**What breaks if you ignore it** — two opposite ways:

- Re-adding `dataSourceName` to an exemption list "because the comment said so" reintroduces a web-only key
  into every converted list/feed page, which is the leak ENG-96589 exists to stop.
- If a device test ever shows the mobile runtime DOES read `dataSourceName`, a converted `crt.Feed` renders
  empty and nothing errors — `validate-page` and `update-page --dry-run` both pass, exactly like the
  `crt.GridContainer.rows` defect. In that case the fix is a producer-side catalog gap (the key must be
  declared), not a clio exemption, and
  `WebToMobilePropertyPruneTests.Analyze_Feed_ShouldPruneDataSourceName_AndKeepWhatTheRegistryDeclares` is
  the test that must be inverted, together with the two comments above.
