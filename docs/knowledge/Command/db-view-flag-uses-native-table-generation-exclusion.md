---
description: IsDBView must be saved before native DB-structure publication; Creatio skips table generation but does not provision the SQL view
applies-to:
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaCreator.cs
  - clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
ticket: "#1618"
date: 2026-09-17
---

**What is true** — Creatio's designer maps `isDBView` to `EntitySchema.IsDBView`
(metadata `D10`). `DbStructureInstaller.CanGanerateDbActions` excludes DB views
and virtual entities. Verified on disposable Creatio 10.1.585/PostgreSQL: creating
a DB-view entity saves the flag and creates no physical table; ordinary creation does.

**Why it is this way** — The entity metadata describes the view, while a separately
deployed SQL script owns its definition. Clio can retain the native save/publish pipeline.

**What breaks if you ignore it** — Creating an ordinary entity first materializes a
table under the intended view name. Setting the flag later does not convert that table
to a SQL view. `IsVirtual` is independent and is not a substitute for `IsDBView`.
