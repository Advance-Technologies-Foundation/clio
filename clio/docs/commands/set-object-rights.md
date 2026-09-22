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

A revoke only ever narrows access. Removing an object's **last** rights row is the one case that would
not: it turns operation permissions off, which makes the object available to **all internal users**. That
is refused unless `--disable-operation-permissions` asks for it explicitly.

**Destructive.** In a non-interactive run it refuses to apply unless `--confirm` is passed; in an
interactive run it asks for a `y/n` confirmation.

## Synopsis

```bash
clio set-object-rights --entity-schema-name <EntitySchemaName> --grantee <SysAdminUnitId> [--operations read,create,edit,delete] [--revoke] [--disable-operation-permissions] [--include-connected] --confirm -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Object (entity schema) name whose operation permissions are changed. Required.

--grantee GUID
SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id.
All external users = 720b771c-e7a7-4f31-9cfb-52cd21c3739f.

--operations LIST
Comma-separated: read,create,edit,delete. Default: read,create,edit (delete not granted by default).

--revoke
Revoke instead of grant. A role left with no operations is removed.

--disable-operation-permissions
Allow a revoke to remove the object's LAST rights row, turning operation permissions OFF and making
the object available to ALL internal users. Without it such a revoke changes nothing and exits 1.

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

Return an object to "available to all internal users" by removing its last role grant:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --revoke --disable-operation-permissions --confirm -e production
```

## Notes

- Backed by `RightManagementService.svc/GetAdministratedObject` + `SaveAdministratedObject` (a
  read-modify-write). Read the result back with `get-object-rights`.
- Scope: object operation permissions only. Column permissions are out of scope. The "Use operation
  permissions" toggle is turned ON by a grant; a revoke never turns it OFF unless
  `--disable-operation-permissions` is passed, because doing so widens access to every internal user.
- A revoke of the object's last rights row without that flag writes nothing and exits 1, naming the
  widening it avoided. Neither of the two possible end states — administered with zero grants (reachable
  by nobody) or unadministered (reachable by everybody) — is a side effect a per-role revoke may cause.
- The portal-section flow uses `--grantee <All external users> --include-connected`; this is one case of
  a general capability.
```
