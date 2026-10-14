---
description: object operation permission rows are a PRIORITY list, not a union of flags - a user in several roles gets the operations of the highest matching row (position 0 first), decided per row, so a row with no operations is a deny; removing an emptied row can WIDEN access and a new row at the bottom can be shadowed by a broader row above it
applies-to:
  - clio/Common/ObjectRights/ObjectRightsPlanner.cs
  - clio/Common/ObjectRights/ObjectRightsModel.cs
  - clio/Command/ObjectRights/GetObjectRightsCommand.cs
  - clio/Command/ObjectRights/SetObjectRightsCommand.cs
ticket: ENG-99741
date: 2026-09-30
---

**What is true** — the rows of an object's operation permissions (`SysEntitySchemaOperationRight`, the
"Object permissions" grid) are ordered by `position`, 0 the highest. A user who is in several roles gets
the operations of the HIGHEST matching row, and the rule is per row: a `false` in that row is a deny, not
"unset", so lower rows do not add to it. Probed on Creatio 8.3.4 (stand `kravchuk_0922`, 2026-09-29) as an
internal user in a test role and All employees, with no "…any data" system operations, through DataService
from that user's session:

| Rows (position: role → operations) | Read | Create |
|---|---|---|
| 0: role → none, 1: All employees → RCED | no | no |
| 0: role → R, 1: All employees → RCED | yes | no |
| 0: All employees → R, 1: role → RCE | yes | no (the role's grant is shadowed) |
| All employees → RCED alone | yes | yes |

A change applies to sessions that are already logged in. A denied read comes back from DataService as a
SUCCESS with zero rows, not as an error; a denied insert is a `SecurityException`. The designer's "+ Add"
puts a new role at the highest position + 1.

**Why it is this way** — it is the platform's model (Creatio Academy describes the same "highest role in
the list" rule); nothing in the `GetAdministratedObject` payload says so, and the flags alone look like a
set to be merged.

**What breaks if you ignore it** — a tool that treats the rows as a union of flags gets access wrong in
both directions, silently. A revoke that clears a role's row and then REMOVES the row lets the role's
members fall through to a lower, broader row — the "revoke" widens their access. A grant appended at the
bottom looks applied but is shadowed for every user who is also in a role above it. That is why
`set-object-rights` keeps a revoked row (as a deny), never removes or moves a row, adds a new row at the
lowest priority and names the rows above it, and why `get-object-rights` prints every row with its
position plus the rule itself. "Clear every operation" (a deny) and "remove the role" are different
operations; the design is recorded in [`spec/adr/adr-ENG-99741-object-rights.md`](../../../spec/adr/adr-ENG-99741-object-rights.md).
