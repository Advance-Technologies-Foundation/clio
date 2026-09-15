---
description: crt.ListItem.body is typed as ONE element in Dart but every page schema writes an ARRAY that list_item_preprocessor.dart wraps before deserialisation - a contract generated from the runtime therefore describes a shape no page writes, and an agent authoring from it produces a single body element instead of the row's fields
applies-to:
  - clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs
  - clio/Command/McpServer/Data/WebToMobilePageConversionRules.json
ticket: ENG-91859
date: 2026-09-07
---

**What is true** — a mobile `crt.ListItem` row writes its `body` as an ARRAY of field entries
(`[{"value": "$PDS_Stage"}, …]`), which is what the rules file's grid → list template emits and what
every OOTB mobile list page contains. The Flutter class types the field as one element
(`final BaseComponentConfig? body`); `lib/modules/list/domain/list_item_preprocessor.dart` turns the
array into that element via `ListItemBodyComponentConfig.fromItemsConfig` BEFORE deserialisation, and
that helper returns null for anything that is not a List, so the object form stays legal too.

**Why it is this way** — a contract generated from the runtime describes the shape the runtime ends
up HOLDING. A preprocessor that rewrites a value before deserialisation breaks that equivalence: the
page-schema shape and the config shape differ, and only the config shape is visible to static
analysis. This is the same blind spot as a preprocessor-consumed property, one level in — the
property exists on both sides, but with different shapes.

**What breaks if you ignore it** — an agent authoring a row from the contract writes one body element
and the row renders with a single field instead of the grid's columns. Nothing errors: the page
validates, saves and opens.

Truncation is the plausible-sounding version of this, and the history is worth keeping straight
because it decides whether the array declaration is load-bearing. It IS. Before the named-type
resolver landed, `{"type": "ViewElementConfig"}` read as *indeterminate*, so
`CoerceToDeclaredShape` left the array alone and a wrong declaration cost only the agent's reading.
Since that resolver (`a-named-type-must-resolve-to-a-container-shape.md`) the same descriptor
resolves to **Object**, and the object branch keeps an array's FIRST element and drops the rest. So
declaring `body` as an array is now what stops a seven-field row from arriving with one, and the
thing that decides it is a producer-side declaration in another repository.

Two clio-side guards were added for that (ENG-91859), and neither one can *prevent* the reversion:

- `CoerceToDeclaredShape` now records every Object-coercion that discarded array elements and the
  guide reports them as a `CONTENT WAS DISCARDED BY A REGISTRY SHAPE COERCION` constraint naming
  `type.property` and how many entries were lost. So the truncation reaches the caller instead of
  shipping as a page that validates, saves and opens with most of the row missing.
- `Mobile_Registry_Snapshot_Should_Declare_ListItem_Body_As_An_Array` pins the declaration in the
  fixture. It is **vacuous today**: the pinned mobile fixture is the pre-cutover 35-component pin
  and contains no `crt.ListItem` at all, so its no-type branch only asserts that the fixture is
  still that pin. It starts checking the declaration on the first fixture refresh after the
  producer publishes.

The mobile generator publishes the array form deliberately
(`preprocessorComponentProperties` with `overridesExtracted: true` in the **mobile-app** repository's
`tools/mcp_registry_generator/mcp-registry.yaml`). Do not "correct" it back to the Dart field type,
and check for the same trap on any other property a preprocessor rewrites.
