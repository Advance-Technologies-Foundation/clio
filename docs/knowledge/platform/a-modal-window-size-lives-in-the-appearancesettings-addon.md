---
description: a Freedom UI modal window's size is modalSettings.size in the page's AppearanceSettings add-on, not the page body; GetSchema returns it merged and names the requested package, so the source layer comes from uId/parentSchemaUId
applies-to:
  - clio/Command/PageModalSize/
  - clio/Command/PageSchemaMetadataHelper.cs
ticket: ENG-105681
date: 2026-11-06
---

**What is true** — the size of a Freedom UI modal window is `{"modalSettings":{"size":N}}` in the page's
`AppearanceSettings` add-on (a `SysSchema` row `<Page>AppearanceSettings`, `AddonSchemaManager`, attached through
`ClientUnitSchemaManager`). The Page Designer offers S/M/L/XL and stores 1/2/3/4; the platform enum also has
`None = 0`, `Custom = 5`, `ExtraSmall = 6`, which the designer never offers. A page with no size opens Small when it
inherits `BaseMiniPageTemplate` and ExtraLarge otherwise. `layoutConfig.width: 420` of `Main` comes from
`BaseMiniPageTemplate` and does not set the window. Measured on 10.2.1027 (SE_M_SE, MSSQL):

- `AddonSchemaDesignerService.GetSchema` (`useFullHierarchy: true`) returns the metadata **merged** across the
  layers visible from the requested package, and its `package` is always the **requested** package, not the
  layer the value came from.
- Its `uId` is a fresh random GUID on every call while the package has no layer of its own; it becomes the
  persisted row's UId once `SaveSchema` stores it. `parentSchemaUId` is the topmost existing layer. So the
  source layer is the `SysSchema` row with `UId = uId`, else the one with `UId = parentSchemaUId`.
- `useFullHierarchy: false` does not isolate one layer: it returns the same merged metadata. No call reads the
  requested package's layer alone, so an own layer is reported as the source even in the rare case where that
  layer was saved without a size. A save (designer or clio) writes the merged metadata, size included.
- `AppearanceSettingsService.svc/GetAppearanceSettings {schemaName}` returns the size as the window opens
  (every layer merged) and reflects a `SaveSchema` immediately.
- Vendor pages outside the `MiniPage` naming carry the add-on too (`AccountOmniFolderPage`,
  `BaseLookupPageTemplate`, even `Leads_FormPage` with `modalSettings: null`).

**Why it is this way** — the add-on designer prepares an editable add-on for the requested package the same
way the page designer prepares a replacing page, so the payload describes the layer to be written, not the one
read. Only the merged value and the parent link say where the current value lives.

**What breaks if you ignore it** — reporting `schema.package` as the source names the caller's own package for a
vendor value; trusting `uId` without the row lookup names a layer that does not exist. Reusing
`PageModalSizeService` for a page that does not inherit `BaseMiniPageTemplate` (a lookup page) reports a wrong
default: its "no size" default is Small only because of that gate.

On that stand the Page Designer's size panel showed M after reopening, even for a size the designer itself had
just saved (stored `size: 3`, `GetSchemaMetadata` returning it 10/10). M is the panel's form default, so it is
what "metadata not loaded" looks like there — do not treat it as proof that a write failed; check the runtime
service instead.
