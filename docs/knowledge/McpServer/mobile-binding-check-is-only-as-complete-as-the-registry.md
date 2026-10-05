---
description: the mobile binding check reads only the properties the mobile registry declares for an element's type, so a key a mobile preprocessor reads raw but the registry omits is never checked - and the converter prune strips the same key from converted pages
applies-to:
  - clio/Command/SchemaValidationService.cs
  - clio/Command/McpServer/Tools/MobilePageValidation.cs
  - clio/Command/McpServer/Tools/MobileComponentRegistry/MobileRegistryDeclarations.cs
ticket: ENG-101924
date: 2026-10-05
---

**What is true** — `ValidateMobileFieldBindings` checks a `$Attr` binding only when it sits in a
top-level property `DeclaredPropertyIndex` declares for the element's `type` (own inputs plus
`references.baseInputs`). The index fails open — every property is checked — for a disabled index,
a type with no registry inputs, and a `merge` without a `type`.

The registry is generated from the Dart config classes. Some mobile preprocessors read keys straight
from the raw view config that no config class carries, so the registry does not list them. A binding
in such a key is skipped by this check, and `PruneUndeclaredProperties` removes the key from a
converted page.

Known at the time of writing:
- `control` on a type whose entry does not declare it — `properties_attribute_preprocessor.dart`
  copies `control` to `bindTo` on any named element.
- legacy aliases kept unpublished on purpose: `crt.Button` `text` / `onPressed`,
  `crt.FolderTreeActions.FolderEntityName`, `crt.ListItem.action`.
- non-binding keys the converter strips: `drilldownConfig` / `cacheConfig` on chart and indicator
  widgets, `crt.Gallery.specificPageRecordId`, `crt.TabPanel` `scrollable` / `selectedTab`.

**Why it is this way** — the Mobile Designer writes keys the mobile runtime never reads (for
example `filters: "$<Tile>_Items"` on every `crt.TimelineTile`, with no attribute behind it).
Checking every property rejected those Designer-saved pages; the registry is the only machine-readable
statement of what the runtime reads.

**What breaks if you ignore it** — a new preprocessor that reads an undeclared key gets no
validation for its bindings, and converted pages silently lose that key. Publish the key from
mobile-app (a config field, or `@crtComponentRegistryOverrides`) rather than special-casing it in
clio.
