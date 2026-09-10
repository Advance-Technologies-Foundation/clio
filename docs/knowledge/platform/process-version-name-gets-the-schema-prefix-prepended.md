---
description: the client composes a new process version's name as parentSchemaName + packageName with non-word characters stripped + version number, and PREPENDS the environment schema-name prefix when the parent does not start with it - so a version of an unprefixed root is named UsrInvoiceVisaProcessInvoice1 and no validation refuses it
applies-to:
  - clio/Command/ProcessModel/IProcessVersionLibReader.cs
  - clio/Command/ProcessModel/ProcessLibResolver.cs
ticket: ENG-94374
date: 2026-09-03
---

**What is true** — read from `Terrasoft.BaseProcessSchemaManager.prototype.setNewSchemaVersionName` in a
live client (Creatio core 10.1.448.0, 2026-09-03):

```js
r = r.replace(/\W/g, "");                     // r is the PACKAGE name
e.name = t.parentSchemaName + r + e.version;
if (this.schemaNamePrefix && !startsWith(t.parentSchemaName, this.schemaNamePrefix)) {
    e.name = this.schemaNamePrefix + e.name;  // prefix PREPENDED to the whole name
}
```

Three consequences, none of them visible from a version's name alone:

- A version of an **unprefixed** root gets the environment prefix in front. On a stand whose
  `SchemaNamePrefix` is `Usr`, a version of the stock `InvoiceVisaProcess` is named
  `UsrInvoiceVisaProcessInvoice1` — the name no longer begins with the root's name, so a
  "does it start with the root's name" test fails on exactly the processes most likely to be versioned.
- The package segment has every non-word character **stripped**, so the segment is not the package name
  as displayed.
- Nothing validates or refuses. There is no prefix rejection on this path — the name simply changes
  shape.

The number in that name is `maxVersionInPackage + 1`, scoped to the pair **(family root, package)**:
`getNewSchemaVersion` calls `GetSchemaVersionInfo` with `{parentSchemaUId, packageUId}`. Measured on
`ProcessSchemaManagerService.svc` (POST with the CSRF header; a GET answers 405 and a plain POST 403):
`InvoiceVisaProcess` with its own package returns 1, the same root with a different package returns 0.
So a version created in another package starts at 1 again, and two members of one family can carry the
same number in different packages.

**Why it is this way** — the name is assembled client-side, in the same composer that builds the version
(no server API creates one), and the prefix rule exists so that a version authored in a custom package
satisfies the environment's naming policy even when the product schema it descends from does not.

**What breaks if you ignore it** — anything that treats a version's Name as parseable. A resolver that
strips a trailing number to find the root, a regex that asserts `^<rootName>`, or a check that a version
name "contains the package name" all fail on real rows: the prefix may be in front, the package segment
may be spelled differently than the package, and the number is only unique within a package. This is the
mechanism behind the broader rule that versionhood cannot be inferred from a schema name — see the
`process-versions` guidance article, V4 and V7. Resolve through `VwProcessLib.VersionParentUId` instead;
it is `COALESCE(parent.UId, own.UId)` and needs no parsing.
