---
description: external (portal) users are deny-by-default for object access - a non-administered object is available to INTERNAL users only, and external users reach an object only through an explicit SysSchemaOperationRight grant to All external users (the Freedom mechanism, not PortalSchemaAccessList); so a grantee check must never count a non-administered object as covered
applies-to:
  - clio/Command/ObjectRights/GetObjectRightsCommand.cs
  - clio/Common/ObjectRights/ObjectRightsReader.cs
ticket: ENG-99741
date: 2026-09-25
---

**What is true** — an object with no operation-permission administration (`administratedByOperations`
false) is available to every INTERNAL user, but NOT to external (portal) users. External users are
**deny-by-default**: they reach an object ONLY when their role holds an explicit grant. In Freedom UI that
grant is an operation-permission row for `All external users` in `SysSchemaOperationRight`. That row is what
`set-object-rights` writes, and what the designer's red "objects not available to external users" warning
checks (creatio-ui `lib.studio-enterprise.related-pages-designer` → `ObjectPermissionsService`).
`PortalSchemaAccessList` (package CrtNUI/SSP) belongs to the CLASSIC self-service portal and stays EMPTY in
the Freedom flow. So `get-object-rights` says "available to all internal users (external users still need
an explicit grant)" for such an object, and with `--grantee` it never counts the object as covered: it lists it
separately as having no explicit grant (reachable only if the role is internal).

**Why it is this way** — internal roles usually hold the "View/Add/Edit/Delete any data" system
operations. Those OUTRANK object permissions, so an unadministered object is open to internal users.
Portal licenses do not carry those operations, so an external user has no such bypass, and without an
explicit object grant access is denied. Nothing in the `GetAdministratedObject` response says this: the
same `administratedByOperations: false` means "open" for one audience and "closed" for the other.

**What breaks if you ignore it** — a grantee check that treats "not administered" as "covered" prints an
all-clear for the portal audience, and the agent skips the grant it still needs. The portal section then
shows empty lists or blank lookups for external users, even though the page binding and workplace are
correct, because the object itself is invisible to them. Any change to the coverage rule in
`GetObjectRightsCommand` has to keep a non-administered object out of the all-clear whenever a grantee is
being checked.
