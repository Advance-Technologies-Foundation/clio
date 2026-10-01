---
description: an object that is NOT administered by operation permissions and has no stored rows is READ by GetAdministratedObject with a synthesized "All employees" read/create/edit/delete row at position 0 (a new row id on every read), and SaveAdministratedObject stores exactly the rows it is sent - the server never adds that row, so an enabling save without it (or with rows null or []) leaves internal users without access
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ObjectRights/ObjectRightsPlanner.cs
  - clio/Common/ObjectRights/ObjectRightsReadBackVerifier.cs
ticket: ENG-99741
date: 2026-09-30
---

**What is true** — for an object that is not administered by operation permissions and has no rows in
`SysEntitySchemaOperationRight`, `GetAdministratedObject` returns a synthesized `All employees` row with
read/create/edit/delete at position 0, with a NEW row id on every read. A save that turns operation permissions on
stores exactly the rows it sends: keep that row and it becomes a real row; drop it and the server adds NOTHING. With
the rows sent as `null` or `[]` the object is left administered with zero rows, and it is then read with zero rows.
Once rows are stored (a disable keeps them), the read returns those rows and no synthesized one. Observed on Creatio
8.3.4.2845 (stand `kravchuk_0922`, 2026-09-28) with three first-enable payloads (rows without All employees, `null`,
`[]`). The Object permissions designer behaves the same: it keeps the synthesized row on enable, and adds no All
employees row when stale rows exist.

**Why it is this way** — the synthesized row is how the designer shows the "switch off" state (every employee has
every operation) as a grid; it is not stored until a save sends it back.

**What breaks if you ignore it** — an enabling save built from the grantee's row alone, on the belief that "the
server adds All employees", cuts every internal user outside the grantee off the object; a save with no rows locks
out everyone but the "…any data" holders. That is why the save sends the rows it read, the planner adds an All
employees row itself only when an enable meets stored rows without one, and the read-back treats a missing All
employees row after an enable as a failed call. A disable never leaves an object without stored rows: it is accepted
only when the revoke empties the last granting row, and that cleared row stays stored.
