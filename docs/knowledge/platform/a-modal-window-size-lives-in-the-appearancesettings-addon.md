---
description: a Freedom UI modal window's size is modalSettings.size in the page's AppearanceSettings add-on; GetSchema useFullHierarchy=true merges every package of the environment, false only what the package sees; the runtime winner is the top layer by package dependencies
applies-to:
  - clio/Command/PageModalSize/
  - clio/Command/PageSchemaMetadataHelper.cs
ticket: ENG-105681
date: 2026-11-10
---

**What is true** — the size of a Freedom UI modal window is `{"modalSettings":{"size":N}}` in the page's
`AppearanceSettings` add-on (a `SysSchema` row `<Page>AppearanceSettings`, `AddonSchemaManager`, attached through
`ClientUnitSchemaManager`). The Page Designer offers S/M/L/XL and stores 1/2/3/4; the platform enum also has
`None = 0`, `Custom = 5`, `ExtraSmall = 6`, which the designer never offers. A page with no size opens Small when it
inherits `BaseMiniPageTemplate` and ExtraLarge otherwise. `layoutConfig.width: 420` of `Main` comes from
`BaseMiniPageTemplate` and does not set the window. Measured on 10.2.1027 (SE_M_SE, MSSQL) and, with several
layers in parallel and stacked packages, on 10.2.760 (SE_M_SE, PostgreSQL, 2026-11-10):

- `AddonSchemaDesignerService.GetSchema` with `useFullHierarchy: true` merges the layers of **every** package of
  the environment, whichever package is requested: from a package that does not depend on the package holding the
  top layer it still returns that layer's size (10.2.760). With `useFullHierarchy: false` it merges the requested
  package and its dependencies only — the size as that package sees it. The 1027 note "false returns the same
  merged metadata" was taken with a single layer, where both answers coincide.
- Its `package` is always the **requested** package. Its `uId` is the persisted row's UId when the package has
  its own layer, otherwise a fresh random GUID; `parentSchemaUId` is the **base** layer (the page package's
  layer) even when a layer in between sets the size, so it does not name the source of an inherited size.
- `AppearanceSettingsService.svc/GetAppearanceSettings {schemaName}` returns the size as the window opens and
  reflects a `SaveSchema` immediately. Its winner is the top layer of the package hierarchy: a layer whose package
  depends on another layer's package wins over it even when the lower one was saved later. Customer layers all
  parent to the same base UId (`ExtendParent: true`), so `SysSchema.ParentId` says nothing about the order — the
  package dependencies (`SysPackageDependency`) do. Two parallel top layers were told apart by their own views
  (`useFullHierarchy: false`); which one the runtime takes when both hold different sizes was not measured.
- A save into a package below an existing top layer stores the value but answers `success: false`
  (`AutoAddPackageDependenciesValidateException`, "cyclic dependency", `validationErrors[].package` = the top
  layer's package): the platform tries to make the saving package depend on the top one.
- Vendor pages outside the `MiniPage` naming carry the add-on too (`AccountOmniFolderPage`,
  `BaseLookupPageTemplate`, even `Leads_FormPage` with `modalSettings: null`).

**Why it is this way** — the add-on designer prepares an editable add-on for the requested package the same
way the page designer prepares a replacing page, so the payload describes the layer to be written, not the one
read. Only the merged value and the parent link say where the current value lives.

**What breaks if you ignore it** — reporting `schema.package` as the source names the caller's own package for a
vendor value; trusting `uId` without the row lookup names a layer that does not exist; reading with
`useFullHierarchy: true` makes a layer the package cannot see look like its own size, so an override is never
detected and a write copies the other package's value into the caller's layer; naming `parentSchemaUId` as the
source names the base layer for a size a layer in between sets. Reusing
`PageModalSizeService` for a page that does not inherit `BaseMiniPageTemplate` (a lookup page) reports a wrong
default: its "no size" default is Small only because of that gate.

On that stand the Page Designer's size panel showed M after reopening, even for a size the designer itself had
just saved (stored `size: 3`, `GetSchemaMetadata` returning it 10/10). M is the panel's form default, so it is
what "metadata not loaded" looks like there — do not treat it as proof that a write failed; check the runtime
service instead.
