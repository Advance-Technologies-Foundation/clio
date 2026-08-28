---
description: DataBindingDbService reads bound rows from the binding itself (FetchBoundRows / GetBoundSchemaData), never from the entity table, so read-data-binding-db and remove-data-binding-row-db still work after the live record is gone
applies-to:
  - clio/Command/DataBindingDbCommand.cs
ticket: ENG-88474
date: 2026-08-19
---

**What is true** — `DataBindingDbService.RemoveRow` resolves the binding, calls
`FetchBoundRows(bindingUId)` and checks the requested key against THAT list before it calls
`DeleteEntityRow` (clio/Command/DataBindingDbCommand.cs:649-657). `FetchBoundRows` goes to
`GetBoundSchemaData`, which returns the data stored on the binding. Neither the membership check nor
`read-data-binding-db` consults the entity table at any point.

**Why it is this way** — a package data binding is a stored projection, not a view over live rows;
that is the whole reason `read-data-binding-db` exists. The command reports what the package will
ship, which has to be readable independently of whether the row currently exists on this environment.

**What breaks if you ignore it** — reviewers reason from the table and conclude that a recipe which
deletes the live record first (for example `odata-delete`, then
`remove-data-binding-row-db`) must fail on the second step, and rewrite a working recipe. The
opposite is true, and it is what makes the orphan case recoverable: a binding row whose live record
was deleted by other means is still listed and still removable. Do not "fix" a step order here from
code reading alone.
