---
description: create-page --entity-schema-name binds a data source whose shape depends on the template (new primary PDS, the template's own primary, or the list template's non-primary PDS); without it the page has no primary data source and every bound component is empty; a page created without an entity is still stored with an EMPTY body
applies-to:
  - clio/Command/PageEntityDataSourceSeeder.cs
  - clio/Command/PageCreateOptions.cs
  - clio/Command/PageUpdateOptions.cs
ticket: GH-1839
date: 2026-10-17
---

**What is true** — Freedom UI record templates (`PageWithTabsFreedomTemplate` and its siblings,
`BaseMiniPageTemplate`, `BlankPageTemplate`) declare no primary data source. A stock record page such as
`Accounts_FormPage` declares `primaryDataSourceName: "PDS"` and a page-scoped `PDS` in its own body, and
the designer adds the same when a data source is set on a page (`SetEntityDataSourceCommand` →
`SchemaService.createDataSource` makes the first page-scoped source primary). `ListPageV3Template` is
the exception: it declares `PDS` itself, as a `viewElement` list source, and stock list pages
(`Accounts_ListPage`) have NO `primaryDataSourceName`. Checked on a 10.2.545 stand with `get-page`.
`PageEntityDataSourceSeeder` reads the template's merged `modelConfig` and picks one of three shapes:
bind the template's named primary, bind the list `PDS` without making it primary, or add a primary `PDS`.
A template with no `dataSources` object gets the last shape as one root `merge` (see
`a-config-merge-into-a-missing-path-is-skipped.md`). Mobile pages are not seeded, and a template that
cannot be read binds nothing (a blind guess could make the list `PDS` primary); both say so in `note`.

A page `create-page` makes WITHOUT an entity is still saved with `body: ""`. `get-page` shows a
synthesized empty scaffold for it, so the empty stored body is invisible from the read side.

**Why it is this way** — the template macros `#DataSourceEntityName()#` / `#PrimaryDataSourceName()#`
resolve from `modelConfig.primaryDataSourceName` at runtime, so the entity reaching only `dependsOn`
(the behaviour before GH-1839) bound nothing.

**What breaks if you ignore it** — always adding a primary `PDS` turns a list page's `viewElement`
`PDS` into a page-scoped primary source, which changes how the list loads. Assuming the stored body is
the scaffold `get-page` shows: before GH-1839 `update-page --mode append` skipped the merge on an empty
stored body and saved the fragment as the whole body, so a `modelConfigDiff`-only fragment replaced the
page with one line and no `define(...)` wrapper while reporting `success`.
