# set-object-rights

## Command Type

Object rights

## Name

set-object-rights - grant or revoke object operation permissions (read/create/edit/delete) for a role on an object

## Description

Grants (or, with `--revoke`, revokes) **object operation permissions** for one role on an object — the
`SysSchemaOperationRight` / "Object permissions" layer that decides who may read/create/edit/delete ANY
record of an entity. It is the object-level analog of `set-record-rights` (which is per-record), and it
works for **any** role, not only the portal audience.

It is a read-modify-write over the native `RightManagementService`: the object's per-role grid is read,
the grantee's row is added/updated (or removed when a revoke empties it), and the object is saved.
Granting to an object that does not yet use operation permissions **turns them on** (Creatio then also
grants `All employees` by default so internal users keep access). It does **not** change column
permissions.

**Destructive.** In a non-interactive run it refuses to apply unless `--confirm` is passed; in an
interactive run it asks for a `y/n` confirmation.

## Synopsis

```bash
clio set-object-rights --entity-schema-name <EntitySchemaName> --grantee <SysAdminUnitId> [--operations read,create,edit,delete] [--revoke] [--include-connected] --confirm -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Object (entity schema) name whose operation permissions are changed. Required.

--grantee GUID
SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id.
All external users = 720b771c-e7a7-4f31-9cfb-52cd21c3739f.

--operations LIST
Comma-separated: read,create,edit,delete. Default: all four.

--revoke
Revoke instead of grant. A role left with no operations is removed.

--include-connected
Also apply to the root object's own lookup objects (portal-section convenience).

--confirm
Confirm the destructive change without a prompt. Required in non-interactive runs.

-e, --environment NAME
Registered environment to change.
```

## Examples

Make an object and its lookups available to portal users (grant the external audience):

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee 720b771c-e7a7-4f31-9cfb-52cd21c3739f --operations read,create,edit --include-connected --confirm -e production
```

Grant a functional role full access to one object:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --confirm -e production
```

Revoke delete from a role:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations delete --revoke --confirm -e production
```

## Notes

- Backed by `RightManagementService.svc/GetAdministratedObject` + `SaveAdministratedObject` (a
  read-modify-write). Read the result back with `get-object-rights`.
- Scope: object operation permissions only. Column permissions are out of scope; the "Use operation
  permissions" toggle is turned ON by a grant but is not turned OFF by a revoke (that is a manual step).
- The portal-section flow uses `--grantee <All external users> --include-connected`; this is one case of
  a general capability.
```
