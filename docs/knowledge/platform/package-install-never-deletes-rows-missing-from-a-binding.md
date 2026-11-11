---
description: package install inserts and updates the rows of a data binding but never deletes a row that is missing from it, so set-object-rights --remove-row takes the row out of the SysEntitySchemaOperationRight_<code> binding while every site that already installed the package keeps it; rights rows also reference SysAdminUnit by id
applies-to:
  - clio/Command/ObjectRights/ObjectRightsPackageBinder.cs
  - clio/Command/ObjectRights/SetObjectRightsCommand.cs
ticket: ENG-105290
date: 2026-11-08
---

**What is true** — read from the Creatio core source (`creatio-core`, October 2026), not reproduced on a stand:
`PackageInstallUtilities.InstallPackageSchemaData` applies a binding through `SaveData`, which finds each bound row
by its key columns (`IsKey`, else the primary column) and inserts or updates it. Nothing in that path deletes a
target row that the binding does not carry: a row bound by an earlier version of the package and dropped from the
binding later stays on every site that installed the earlier version. The only data deletion is package
UNINSTALL (`PackageDataHistoryUtilities.UninstallPackageData`, rows recorded in `SysInstalledPackageData`). On an
existing row, `Install` (install type 0) copies only the columns marked `IsForceUpdate`. That is why the
`SysEntitySchemaOperationRight` binding force-updates every column but its `Id` key: `SubjectSchemaUId`,
`SysAdminUnit`, `CanRead`, `CanAppend`, `CanEdit`, `CanDelete` and `Position`. When
`Feature-ActualizeSchemaRightPositions` is on, the installer renumbers duplicate positions of an object's rights rows
after install (`SchemaRightPositionsActualizer`) and expires the operation-rights cache.

Each rights row references its role by `SysAdminUnit` id. A role created on the source site has a different id on
every other site unless the role itself is shipped. All employees and All external users are seeded with the same
id everywhere.

**Why it is this way** — a data binding is an upsert snapshot, not a mirror: the installer has no notion of "rows
this package used to ship". The Object permissions designer deletes a row from the database of the site it runs on
and nothing more, and `set-object-rights --remove-row` does the same plus a re-save of the binding (decided with
the operator on ENG-105290: delete the row, update the binding — no install-time delete script).

**What breaks if you ignore it** — a test or a guide that expects a reinstall to take a removed row off a site that
already has it fails, and the obvious "fix" — an install SQL script that deletes the object's rows missing from the
binding — would also delete rights the customer added on that site by hand. The removed row is still gone from a
clean install. A row whose role is missing on the target installs a rights row that points at no role. The
`set-object-rights` result names every role other than All employees and All external users for that reason.
