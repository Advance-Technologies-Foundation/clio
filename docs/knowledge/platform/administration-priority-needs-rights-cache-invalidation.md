---
description: Native operation-priority reorder persists without invalidating the backend administrated-operations cache.
applies-to:
  - clio/Command/Administration/AdministrationService.Access.cs
  - clio/Command/Administration/ManageAccessCommand.cs
  - cliogate/Files/cs/CreatioApiGateway.cs
ticket: clio-968
date: 2026-09-11
---

**What is true** — On the exclusive Creatio 10.1.585.0 runtime with
UseAdministratedOperationsCachePerUser enabled, RightsService.SetAdminOperationGranteePosition
changes Position but leaves the previous backend permission decision cached, including after logout/login.
The public DBSecurityEngine.ClearUserAdministratedOperationsCache method in that deployed assembly
expires the shared cache across users. Updating IClientCacheStore's RightsCacheHashKey alone is insufficient
for the backend; the bridge performs both operations.

**Why it is this way** — The inspected source checkout had an older implementation of the same public
method that only cleared the current session. Decompilation of the actual lab Terrasoft.Core.dll showed
the added ExpireAllUsersAdministratedOperationsCache call, also used by native grant updates. The
verified runtime version is therefore required. The newer public client-cache interface is resolved
through ClassFactory by reflection because the package still compiles against the older Creatio SDK.

**What breaks if you ignore it** — A stored deny at priority zero can still allow a backend request.
Reasserting an existing grant to invalidate its cache risks overwriting a concurrent administrator's
change. Verification must exercise the affected user's actual permission decision, not just Position
readback. The live allow-first/deny-first probe passed after both invalidations.
