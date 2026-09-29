---
description: object operation rights are read/written via RightManagementService.svc GetAdministratedObject + SaveAdministratedObject - name->UId returns MULTIPLE SysSchema rows for a granted/extended schema and only the administrable one answers (the others fault with a non-JSON error page); an object with no stored rows is READ with a synthesized All employees row that the server never adds on save; the save stores exactly the rows it sends and must null the rights collections it did not change
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ObjectRights/ObjectRightsPlanner.cs
  - clio/Common/ServiceUrlBuilder.cs
  - clio/Package/SelectQueryHelper.cs
ticket: ENG-99741
date: 2026-09-30
---

**What is true** — `get-object-rights` / `set-object-rights` read and write object operation
permissions through the native `RightManagementService.svc` (`GetAdministratedObject`,
`SaveAdministratedObject`), NOT `/rest/...`: the `/rest/RightManagementService/...` alias returns 404,
only `ServiceModel/RightManagementService.svc/<Method>` works (verified on stand). Four traps the code
handles, none visible from the service signatures:

1. **A granted or extended entity schema resolves to MORE THAN ONE `SysSchema` (EntitySchemaManager)
   row** — a base UId and a replacing-schema UId, with the same Name. `GetAdministratedObject` accepts
   ONLY the administrable UId and FAULTS with a non-JSON "Request Error" HTML page on the other, and the
   order the rows come back in is NOT deterministic. `RightManagementServiceClient` therefore resolves
   ALL candidate UIds and tries each, using the first that returns a parseable `administratedObject`.
   Heavily layered OOTB objects (Contact, Account — reached through `--include-connected`) can carry many
   layers, and the candidate `SelectQuery` has a row cap that `rowCount` applies to an UNORDERED result,
   so the query is ordered server-side by `ExtendParent` ascending (base row, `false`, first) BEFORE the cap
   and probed in that order — the same trap `ClassicEntitySchemaQuery.ColumnOrderedAsc` documents.

2. **An object with no stored rows is READ with a row that does not exist.** For such an object
   `GetAdministratedObject` returns a synthesized `All employees` row with read/create/edit/delete at
   position 0, with a NEW row id on every read. A save that turns operation permissions on stores exactly
   the rows it sends: keep that row and it becomes real; drop it and the server adds NOTHING — with rows
   sent as `null` or `[]` the object is left administered with zero rows. Once rows are stored (for example
   after a disable, which keeps them), the read returns those rows and no synthesized one. Observed on
   Creatio 8.3.4.2845 (stand `kravchuk_0922`, 2026-09-28) with three first-enable payloads (rows without
   All employees, `null`, `[]`), and matched by the Object permissions designer, which keeps the synthesized
   row on enable and adds no All employees row when stale rows exist. So the client saves the rows it read
   (updating the planned ones by grantee, never removing one), and the planner adds an `All employees` row
   itself only when an enable meets stored rows without one.

3. **`SaveAdministratedObject` is a read-modify-write** — there is no per-right endpoint. You GET the
   whole object, mutate `entitySchemaOperationsRights` (rows carry `id`, `position`, `canRead`,
   `canAppend` (= Create), `canEdit`, `canDelete`, `sysAdminUnit`), and POST it back under
   `{"administratedObject": ...}`. Positions are stored exactly as sent, and they are the rows' priority
   (see [`object-operation-permissions-are-a-priority-list.md`](../platform/object-operation-permissions-are-a-priority-list.md)).

4. **The save sends the collections it did NOT change as `null`** — `entitySchemaRecordDefRights`,
   `entitySchemaColumnsRights`, `entityOperationGrantees`. This mirrors the Freedom "Object permissions"
   client (`creatio-ui lib.studio-enterprise.administrated-object`: "the page keeps a snapshot ... strips
   unchanged rights collections before save ... untouched collections are sent as null").

**Why it is this way** — the write we needed (grant/revoke a role's object operations) is only exposed
as a coarse object-level save, and object rights are a runtime, per-package-layer artifact whose UId is
not the plain schema UId a single SysSchema lookup returns.

**What breaks if you ignore it** — pick `SysSchema.rows[0]` for the UId and the tool intermittently
fails with an opaque "Unexpected response ... `<?xml ...>Request Error`" on exactly the granted objects
(the ones with two rows), passing on the ungranted ones — a flaky, data-dependent failure. Build the
enabling save from the grantee's row alone, trusting "the server adds All employees", and every internal
user outside the grantee loses the object; send no rows and nobody but "…any data" holders reaches it.
Deserialize the whole object into a lossy POCO and save it back and you WIPE the record/column-rights
collections you never touched; send them back in full instead of null and the server may re-process them.
Use `/rest/...` and every call 404s.
