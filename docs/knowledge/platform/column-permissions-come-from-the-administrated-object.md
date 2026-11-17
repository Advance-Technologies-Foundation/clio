---
description: column (field-level) permissions - the switch is administratedByColumns in GetAdministratedObject, but the rows must be read from storage (VwSysEntitySchemaColumnRight) because that read filters; a save with a column list gives every column it does not name the default row (All employees, edit), null leaves the rows alone, and only a storage read-back proves a save
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ObjectRights/ColumnRightsModel.cs
  - clio/Common/ObjectRights/ColumnRightsStorage.cs
  - clio/Common/ObjectRights/ColumnRightsPlanner.cs
  - clio/Command/ObjectRights/GetColumnRightsCommand.cs
  - clio/Command/ObjectRights/SetColumnRightsCommand.cs
date: 2026-11-17
---

**What is true** — measured on Creatio (stand brightwater, object `UsrHoliday`, 2026-11-17) against STORAGE:

- **Storage** is the view `VwSysEntitySchemaColumnRight`: `Id`, `SubjectSchemaUId`, `SubjectColumnUId`,
  `SysAdminUnit` (→ `VwSysAdminUnit`), `RightLevel` (→ the `SysEntitySchemaColRightLevel` lookup; read its `Value`
  through the column path `RightLevel.Value`: 0 denied, 1 read, 2 edit — the lookup's ids differ per site), `Position`.
  clio's own login reads it through DataService `SelectQuery` and OData. There is no entity `SysEntitySchemaColumnRight`,
  and a DataService DELETE on the view is refused.
- **The switch** is `administratedByColumns` in `RightManagementService.svc/GetAdministratedObject`.
- **The read filters.** With the switch ON, `entitySchemaColumnsRights` OMITS a column whose only row is the default
  (All employees `a29a3ba5-4b0d-de11-9a51-005056c00008`, edit), lists a column with no rows with `rights: []`, and
  lists the others with their rows. With the switch OFF it lists no column at all, though the rows stay stored.
- **Save with a list** (`SaveAdministratedObject`, switch on): each column IN the list gets exactly the listed rows
  (`rights: []` → none); each column of the object NOT in the list gets one default row (All employees, edit, position
  0). Rows written by one save come back with the positions sent.
- **Save with `null`**: the stored rows are untouched, also when the switch changes.
- **Enable with `null`** on an object with nothing stored leaves every column with no rows; **enable with `[]`** gives
  every column the default row.
- The save answers `success: true` whatever it stores, so only a storage read-back proves a save.

**Documented, not measured** — Creatio's "Column permissions" documentation (8.0 and 8.3 alike; the measured site runs
configuration 10.1.0): the higher a row is in a column's list, the higher its priority, so a user in several roles gets
the access of the highest of their rows; object operation and record permissions outrank column permissions; users and
roles with no row in a column follow the object's operation permissions, so a column with no rows leaves every user to
them (new columns are readable by everyone); a role added in the designer starts with edit. Positions are reported as
the stored order (0 first).

**Why it is this way** — `GetAdministratedObject` is the System Designer "Column permissions" tab's own read, which
hides the default row it shows as "no restriction"; the save has no per-column endpoint, so a list must name every
column whose rows are not the default.

**What breaks if you ignore it** — a writer that builds its list from the filtered read leaves out the columns it
did not see: a column with rows the read omitted is reset to the default, and a list sent while the switch is off
(when the read shows nothing) resets every column. `set-column-rights` therefore builds the list from storage — every column whose stored rows are not exactly the
default, a column with no rows sent with none while the switch is on and left to the default when the call turns it
on — and checks the result against storage. A reader that trusts the filtered read reports a default column as
missing and cannot tell it from a column with no rows; `get-column-rights` reads storage and says which is which, and
falls back to the read only with a warning.
