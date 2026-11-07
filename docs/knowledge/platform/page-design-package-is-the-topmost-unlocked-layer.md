---
description: GetDesignPackageUId sends a Freedom UI page save to the topmost UNLOCKED replacing layer of the page, silently, even when another unlocked package also holds a layer; get-page-hierarchy sees every layer
applies-to:
  - clio/Command/GetPageHierarchyCommand.cs
  - clio/Command/PageUpdateOptions.cs
ticket: ENG-105680
date: 2026-11-07
---

**What is true** — measured on 10.1.834 (stand `sae_m_seeenu_16284236_1119`, page
`UsrEng105680Orders_FormPage` with replacing schemas in the app package, `Custom` and a second user package
`UsrEng105680Proj`):

- `ClientUnitSchemaDesignerService.GetDesignPackageUId` — the automatic target of `get-page` / `update-page` —
  returned the topmost unlocked layer of the chain: `Custom` once `Custom` held a layer, then `UsrEng105680Proj`
  once that package held one above it, and `Custom` again after `UsrEng105680Proj` was locked
  (`SysPackage.InstallType` 1). Nothing in the response says that another unlocked layer exists.
- `GetParentSchemas(schemaUId, designPackageUId)`, which `get-page-hierarchy` returns, listed ALL three user
  layers, including the ones above and beside the design package, and the merged bundle carried the changes of
  every layer. A replacing schema "outside the design package" is therefore visible in the chain; it is only the
  save target that hides it.
- A package installed with `push-pkg` refuses page saves ("created by third-party publisher or installed from the
  file archive") until `unlock-package`; the lock is `InstallType`, not the maintainer.

**Why it is this way** — the platform designer edits one layer, the highest one the user may change; the
choice among several editable layers is the platform's, not the caller's.

**What breaks if you ignore it** — an agent that only reads `designPackageName` from `get-page` believes the
page is customized in one package, while another unlocked package also changes it. The change lands in one of
them and the user does not learn that the page's customization is split. ENG-105680 adds `packageLocked` to
every `get-page-hierarchy` entry so the guidance can detect the case and ask the user; reading the chain without
it cannot tell an editable layer from a vendor one.
