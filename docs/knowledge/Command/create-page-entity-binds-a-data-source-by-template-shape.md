---
description: create-page --entity-schema-name binds a data source whose shape depends on the template (new primary PDS, the template's own primary, or the list template's non-primary PDS); without it the page has no primary data source and every bound component is empty
applies-to:
  - clio/Command/PageEntityDataSourceSeeder.cs
  - clio/Command/PageCreateOptions.cs
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
cannot be read, or whose hierarchy comes back empty, binds nothing (a blind guess could make the list `PDS` primary); both say so in `note`.

**Why it is this way** — the template macros `#DataSourceEntityName()#` / `#PrimaryDataSourceName()#`
resolve from `modelConfig.primaryDataSourceName` at runtime, so an entity that reaches only `dependsOn`
binds nothing.

**What breaks if you ignore it** — always adding a primary `PDS` turns a list page's `viewElement`
`PDS` into a page-scoped primary source, which changes how the list loads.
