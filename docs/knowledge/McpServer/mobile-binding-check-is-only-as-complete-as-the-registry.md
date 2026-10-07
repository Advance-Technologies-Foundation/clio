---
description: the mobile binding check reads only the properties the mobile registry declares for an element's type, so a key a mobile preprocessor reads raw but the registry omits is never checked - and the converter prune strips the same key from converted pages
applies-to:
  - clio/Command/SchemaValidationService.cs
  - clio/Command/McpServer/Tools/MobilePageValidation.cs
  - clio/Command/McpServer/Tools/MobileComponentRegistry/MobileRegistryDeclarations.cs
ticket: ENG-101924
date: 2026-10-07
---

**What is true** — `ValidateMobileFieldBindings` checks a `$Attr` binding only in a top-level property
the registry declares for the element's `type` (`inputs`, `outputs`, legacy `properties`, plus
`references.baseInputs`). It checks every property for a disabled index, a type with no registry
entry, and a `merge` without a `type`. A key a preprocessor reads raw but no Dart config class carries
is missing from the registry, so its binding is not checked and the converter prune drops it.

Known gaps: `control` on types that do not declare it (`properties_attribute_preprocessor.dart` copies
it to `bindTo` on any named element); `crt.IndicatorWidget.sectionBindingColumnRecordId` until
mobile-app#813 is published; legacy aliases kept unpublished on purpose (`crt.Button` `text` /
`onPressed`, `crt.ListItem.action`, `crt.FolderTreeActions.FolderEntityName`).

**Why it is this way** — the Mobile Designer writes keys the runtime never reads, such as
`filters: "$<Tile>_Items"` on `crt.TimelineTile` and `selectionState` on `crt.List`, with no attribute
behind them. The registry is the only machine-readable statement of what the runtime reads.

**What breaks if you ignore it** — bindings in a newly read raw key go unvalidated and converted pages
lose the key. Publish it from mobile-app (a config field or `@crtComponentRegistryOverrides`) instead of
special-casing it in clio.
