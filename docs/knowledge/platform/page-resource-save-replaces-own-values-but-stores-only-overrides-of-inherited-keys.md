---
description: Page SaveSchema deletes a culture omitted from an OWN localizableStrings entry, but for an INHERITED entry stores only the cultures that differ from the parent; entity column captions instead keep omitted cultures
applies-to:
  - clio/Command/ResourceStringHelper.cs
  - clio/Command/LocalizePageCommand.cs
  - clio/Command/PageUpdateOptions.cs
  - clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
  - spec/adr/adr-page-localization.md
ticket: ENG-90576
date: 2026-09-26
---

**What is true** — measured on Creatio 10.2.254 (stand `eng90576`, Redis cleared before every read, values checked in
`SysLocalizableValue`):

1. `GetSchema` with `useFullHierarchy:false` returns the page's own keys (`parentSchemaUId` = the page UId) and SOME
   inherited keys (`parentSchemaUId` = the declaring ancestor) with the effective value in every culture — not all of
   them: on the lab page it returned 14 keys while the merged `GetParentSchemas` hierarchy (what `get-page` shows) has
   16; the two missing keys are referenced only by an ancestor's replacing schema in another package
   (`BasePageFreedomTemplate` in `OperatorSingleWindow`). The complete key set comes from `GetParentSchemas`.
2. OWN key: `SaveSchema` replaces the stored list. An entry holding `en-US`, `es-ES`, `de-DE` saved with
   `values: [fr-FR]` leaves exactly one row, `fr-FR`; the other three are deleted.
3. INHERITED key: an entry saved with `values: [es-ES]` only — with or without the parent marker, even as a fresh
   entry with a new `uId` — stores one `es-ES` row in the child; `en-US` and the other cultures keep resolving from
   the parent. Sending all 28 inherited values with one changed also stores only the changed culture. Ancestor
   schemas are not modified.
4. ENTITY column caption (`EntitySchemaDesignerService.svc/SaveSchema`): a culture omitted from the payload is KEPT.
   `{en-US, fr-FR}` over a stored `{en-US, es-ES, de-DE}` gives all four.

**Why it is this way** — the page designer persists the difference between the child entry and its parent for
inherited keys, and the full list for own keys; the entity designer merges per culture.

**What breaks if you ignore it** — treating `GetSchema`'s list as the page's full key set rejects real, visible keys as unknown and under-counts coverage (point 1); writing one culture into an own page key without re-sending the others deletes
the key's English caption (point 2); the page then renders the key name. Copying the inherited `en-US` into the child
(point 3) is unnecessary and freezes a later parent change. Changing clio's column-caption "replace" code to "merge"
(point 4) fixes nothing observable. `ResourceStringHelper.CleanAndMerge` is correct because it deep-clones every
stored culture before changing `en-US`.
