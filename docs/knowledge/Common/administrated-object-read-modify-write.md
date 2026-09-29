---
description: object operation rights are read/written via RightManagementService.svc GetAdministratedObject + SaveAdministratedObject - name->UId returns MULTIPLE SysSchema rows for a granted/extended schema and only the administrable one answers (the others fault with a non-JSON error page); the save is a read-modify-write that must change only the planned rows, found by grantee AND position, and must null the rights collections it did not change
applies-to:
  - clio/Common/ObjectRights/RightManagementServiceClient.cs
  - clio/Common/ServiceUrlBuilder.cs
  - clio/Package/SelectQueryHelper.cs
ticket: ENG-99741
date: 2026-09-30
---

**What is true** — `get-object-rights` / `set-object-rights` read and write object operation permissions through the
native `RightManagementService.svc` (`GetAdministratedObject`, `SaveAdministratedObject`), NOT `/rest/...`: the
`/rest/RightManagementService/...` alias returns 404; only `ServiceModel/RightManagementService.svc/<Method>` works
(verified on stand). Three traps, none visible from the service signatures:

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

**Why it is this way** — the only write for a role's object operations is a coarse object-level save, and object
rights are a per-package-layer artifact whose UId is not the plain schema UId a single `SysSchema` lookup returns.

**What breaks if you ignore it** — take `SysSchema.rows[0]` for the UId and the tool fails intermittently with an
opaque "Request Error" on exactly the granted objects. Match rows by grantee alone and a role's two rows are merged
into one — the flags of its lower row overwrite its higher row, widening that role's access on a row nobody named.
Deserialize the object into a lossy POCO and save it back, or send the untouched collections in full, and you wipe
or re-process record and column rights you never touched. Use `/rest/...` and every call returns 404.
