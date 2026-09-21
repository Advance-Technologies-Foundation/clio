---
description: external (portal) users are deny-by-default for object access - a non-administered object is available to INTERNAL users but NOT to external users, who can only reach objects explicitly granted to their role via a SysSchemaOperationRight grant to All external users (the Freedom mechanism, not PortalSchemaAccessList)
applies-to:
  - clio/Command/ObjectRights/GetObjectRightsCommand.cs
  - clio/Common/ObjectRights/ObjectRightsReader.cs
ticket: ENG-99741
date: 2026-09-22
---

**What is true** — `get-object-rights` reports an object with no operation-permission administration as
"available to all". That is true for INTERNAL users only. External (portal) users are
**deny-by-default**: they can read an object ONLY when access is explicitly granted to their role. In
Freedom UI that grant is an object operation-permission row for the `All external users` role in
`SysSchemaOperationRight` — the same thing `set-object-rights` writes and the designer's red "objects
not available to external users" warning checks (creatio-ui
`lib.studio-enterprise.related-pages-designer` → `ObjectPermissionsService`). `PortalSchemaAccessList`
(pkg CrtNUI/SSP) is the CLASSIC self-service path and stays EMPTY for the Freedom flow — it is not the
mechanism.

**Why it is this way** — internal roles typically hold the system operations "View/Add/Edit/Delete any
data", which OUTRANK object permissions, so an unadministered object is open to them. Portal licenses do
NOT carry those system operations, so for an external user there is no such bypass: absent an explicit
object grant, access is denied.

**What breaks if you ignore it** — reading `get-object-rights` output literally, an agent concludes a
non-administered lookup is reachable by portal users and skips the grant; the portal section then shows
empty or errors for external users even though the page, binding and workplace are all correct, because
the object itself is invisible to them. When checking portal readiness, treat "not administered" as
"NOT reachable by external users" and grant `All external users` on every object the section needs
(`set-object-rights --grantee <All external users id> --include-connected`).
