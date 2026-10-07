---
description: object operation rights are read/written via RightManagementService.svc GetAdministratedObject + SaveAdministratedObject - name->UId returns MULTIPLE SysSchema rows for a granted/extended schema and only the administrable one answers (the others fault with a non-JSON error page); the save is a read-modify-write that must change only the planned rows, found by grantee AND position, must null the rights collections it did not change, and must be sent ONCE (a new row has no id, so a transport retry of a committed save adds it twice)
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ServiceUrlBuilder.cs
  - clio/Package/SelectQueryHelper.cs
ticket: ENG-99741
date: 2026-10-01
---

**What is true** — `get-object-rights` / `set-object-rights` read and write object operation permissions through the
native `RightManagementService.svc` (`GetAdministratedObject`, `SaveAdministratedObject`), NOT `/rest/...`: the
`/rest/RightManagementService/...` alias returns 404; only `ServiceModel/RightManagementService.svc/<Method>` works
(verified on stand). Four traps, none visible from the service signatures:

1. **A granted or extended entity schema resolves to MORE THAN ONE `SysSchema` (EntitySchemaManager) row** — a base
   UId and replacing-schema UIds with the same Name. `GetAdministratedObject` accepts ONLY the administrable UId and
   faults with a non-JSON "Request Error" page on the others, and the rows come back in no fixed order. So the client
   tries every candidate UId. The candidate query has a row cap applied to an UNORDERED result, so it is ordered
   server-side by `ExtendParent` ascending (the base row first) BEFORE the cap — the trap
   `ClassicEntitySchemaQuery.ColumnOrderedAsc` documents.
2. **The save is a read-modify-write with no per-right endpoint and no version.** You GET the whole object, change
   `entitySchemaOperationsRights` (rows carry `id`, `position`, `canRead`, `canAppend` (= Create), `canEdit`,
   `canDelete`, `sysAdminUnit`) and POST it back under `{"administratedObject": ...}`. One role can hold several rows,
   so a planned row is matched to the row read by grantee AND position, and only the rows the plan changes or adds are
   written; every other row is sent exactly as read.
3. **The collections the save did NOT change are sent as `null`** — `entitySchemaRecordDefRights`,
   `entitySchemaColumnsRights`, `entityOperationGrantees` — as the Freedom "Object permissions" client does
   (`creatio-ui lib.studio-enterprise.administrated-object` strips unchanged collections before the save).
4. **The save is sent exactly once.** A row the plan adds is sent without an `id` (the server assigns it), and
   `Creatio.Client` re-sends a request after ANY exception, a timeout included, up to `maxAttempts`. A save the server
   committed but answered too late would then be sent again, and the server takes an id-less row as a new one, so it could add the row a second time (inferred from both halves, not reproduced on a stand). So the save goes with
   `maxAttempts: 1`, like the other clio writes (`manage-access`, the schema designer's save and build), and a save that
   got no answer — it did not answer in time, or the connection broke after the request went out — is reported as
   possibly applied, to be re-read before a retry. `Creatio.Client` returns the body of an HTTP error status instead of
   throwing, so an `HttpRequestException` is a fault of the connection itself, not the server's answer.

**Why it is this way** — the only write for a role's object operations is a coarse object-level save, and object
rights are a per-package-layer artifact whose UId is not the plain schema UId a single `SysSchema` lookup returns.

**What breaks if you ignore it** — take `SysSchema.rows[0]` for the UId and the tool fails intermittently with an
opaque "Request Error" on exactly the granted objects. Match rows by grantee alone and a role's two rows are merged
into one — the flags of its lower row overwrite its higher row, widening that role's access on a row nobody named.
Deserialize the object into a lossy POCO and save it back, or send the untouched collections in full, and you wipe
or re-process record and column rights you never touched. Leave the transport retry on and a slow stand could give a
role two rows from one grant — after which the tool refuses that role as a duplicate until the designer repairs it.
Use `/rest/...` and every call returns 404.
