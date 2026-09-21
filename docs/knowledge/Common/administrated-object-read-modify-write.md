---
description: object operation rights are read/written via RightManagementService.svc GetAdministratedObject + SaveAdministratedObject - name->UId returns MULTIPLE SysSchema rows for a granted/extended schema and only the administrable one answers (the others fault with a non-JSON error page), and the save must null the rights collections it did not change
applies-to:
  - clio/Common/ObjectRights/ObjectRightsReader.cs
  - clio/Common/ServiceUrlBuilder.cs
ticket: ENG-99741
date: 2026-09-22
---

**What is true** — `get-object-rights` / `set-object-rights` read and write object operation
permissions through the native `RightManagementService.svc` (`GetAdministratedObject`,
`SaveAdministratedObject`), NOT `/rest/...`: the `/rest/RightManagementService/...` alias returns 404,
only `ServiceModel/RightManagementService.svc/<Method>` works (verified on stand). Three traps the code
handles, none visible from the service signatures:

1. **A granted or extended entity schema resolves to MORE THAN ONE `SysSchema` (EntitySchemaManager)
   row** — a base UId and a replacing-schema UId, with the same Name. `GetAdministratedObject` accepts
   ONLY the administrable UId and FAULTS with a non-JSON "Request Error" HTML page on the other, and the
   order the rows come back in is NOT deterministic. `RightManagementServiceClient` therefore resolves
   ALL candidate UIds and tries each, using the first that returns a parseable `administratedObject`.

2. **`SaveAdministratedObject` is a read-modify-write** — there is no per-right endpoint. You GET the
   whole object, mutate `entitySchemaOperationsRights` (rows carry `id`, `position`, `canRead`,
   `canAppend` (= Create), `canEdit`, `canDelete`, `sysAdminUnit`), and POST it back under
   `{"administratedObject": ...}`. Granting to a not-yet-administered object sets
   `administratedByOperations=true`; the server then also grants `All employees` by default. Row
   `position` matters (lower rows widen).

3. **The save sends the collections it did NOT change as `null`** — `entitySchemaRecordDefRights`,
   `entitySchemaColumnsRights`, `entityOperationGrantees`. This mirrors the Freedom "Object permissions"
   client (`creatio-ui lib.studio-enterprise.administrated-object`: "the page keeps a snapshot ... strips
   unchanged rights collections before save ... untouched collections are sent as null").

**Why it is this way** — the write we needed (grant/revoke a role's object operations) is only exposed
as a coarse object-level save, and object rights are a runtime, per-package-layer artifact whose UId is
not the plain schema UId a single SysSchema lookup returns.

**What breaks if you ignore it** — pick `SysSchema.rows[0]` for the UId and the tool intermittently
fails with an opaque "Unexpected response ... `<?xml ...>Request Error`" on exactly the granted objects
(the ones with two rows), passing on the ungranted ones — a flaky, data-dependent failure. Deserialize
the whole object into a lossy POCO and save it back and you WIPE the record/column-rights collections
you never touched; send them back in full instead of null and the server may re-process them. Use
`/rest/...` and every call 404s.
