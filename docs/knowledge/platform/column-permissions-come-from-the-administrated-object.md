---
description: column (field-level) permissions are the administratedByColumns switch plus entitySchemaColumnsRights in the same GetAdministratedObject answer as object permissions - per column columnUId + columnCaption + rights[{position, rightLevel 0 denied/1 read/2 edit, sysAdminUnit}], no column code and no row id; the server does not store a column whose rows are all edit, and a save answers success for rows it dropped
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ObjectRights/ColumnRightsModel.cs
  - clio/Command/ObjectRights/GetColumnRightsCommand.cs
  - clio/Command/ObjectRights/SetColumnRightsCommand.cs
  - clio/Common/ObjectRights/ColumnRightsPlanner.cs
date: 2026-11-17
---

**What is true** — `RightManagementService.svc/GetAdministratedObject` returns the column layer next to the
operation and record layers: `administratedByColumns` (the "Use column permissions" switch) and
`entitySchemaColumnsRights`, a list of `{columnUId, columnCaption, rights: [{position, rightLevel, sysAdminUnit:
{id, name}}]}`. `rightLevel` is the `Value` of the `SysEntitySchemaColRightLevel` lookup: 0 Access denied, 1 Reading,
2 Editing. Storage is the view `VwSysEntitySchemaColumnRight` (`SubjectSchemaUId`, `SubjectColumnUId`,
`SysAdminUnitId`, `RightLevelId`, `Position`). Measured on Creatio (stand brightwater, object `UsrHoliday`,
2026-11-17) through `SaveAdministratedObject` and read-back:

- a row read back has no `id`, and the answer carries the column's UId and caption but not its code;
- a column whose rows are all `rightLevel` 2 (Editing, the default) is dropped by the server; Editing next to a deny
  row is kept as sent, positions included;
- with the switch on and no rows the server stores no rows itself;
- `entitySchemaColumnsRights: null` leaves the stored column rows alone, `[]` clears every column, and a list
  REPLACES every column's rows (sending one column dropped the others);
- row fields that are not these (`subjectColumnUId`, `column{...}`, `canRead`) are ignored, and the save still
  answers `success: true` while storing nothing.

Which column row wins for a user in several roles (the precedence of `position`) is not measured, so
`get-column-rights` lists rows in stored position order (0 first) and claims nothing more about them.

**Why it is this way** — it is the platform's own payload for the System Designer "Column permissions" tab; nothing
in it names the column by code, and the save has no per-column endpoint.

**What breaks if you ignore it** — a reader that prints only the UId leaves the agent unable to name the column, so
`get-column-rights` resolves the code from the merged entity schema. A reader that expects every restricted column
to be listed reports a column made all-edit as missing data; it is not stored by design. A writer that sends a partial
list wipes every other column's rows, and one that trusts `success: true` reports rows the server dropped: only a
read-back proves a column-rights save.
