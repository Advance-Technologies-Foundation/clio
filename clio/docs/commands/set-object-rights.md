# set-object-rights

## Command Type

Object rights

## Name

set-object-rights - grant or revoke object operation permissions (read/create/edit/delete) for a role on an object

## Description

Grants (or, with `--revoke`, revokes) **object operation permissions** for one role on an object — the
`SysSchemaOperationRight` / "Object permissions" layer that decides who may read/create/edit/delete ANY
record of an entity. It is the object-level analog of `set-record-rights` (which is per-record), and it
works for **any** role.

It is a read-modify-write over the native `RightManagementService`: the object's per-role grid is read,
the grantee's row is added/updated (or removed when a revoke empties it), and the object is saved.
Granting to an object that does not yet use operation permissions **turns them on** — an access
**narrowing** for every other role, which the confirmation and the result line both name. Creatio may also
add an `All employees` row with read/create/edit/delete at that point (observed on Creatio 8.3.4 for the
root and for connected lookups alike, not a documented contract); read the result back. It does **not** change column permissions.

A revoke only ever narrows access. Removing an object's **last** rights row is the one case that would
not: it turns operation permissions off, which makes the object available to **all internal users**. That
is refused unless `--disable-operation-permissions` asks for it explicitly.

**Destructive.** In a non-interactive run it refuses to apply unless `--confirm` is passed; in an
interactive run it asks for a `y/n` confirmation.

## Synopsis

```bash
clio set-object-rights --entity-schema-name <EntitySchemaName> --grantee <SysAdminUnitId> [--operations read,create,edit,delete] [--revoke] [--disable-operation-permissions] [--include-connected] [--connected-operations read,...] --confirm -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Object (entity schema) name whose operation permissions are changed. Required.

--grantee GUID
SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id.

--operations LIST
Comma-separated: read,create,edit,delete. Default: read,create,edit (delete not granted by default).

--revoke
Revoke instead of grant. A role left with no operations is removed.

--disable-operation-permissions
Allow a revoke to remove the object's LAST rights row, turning operation permissions OFF and making
the object available to ALL internal users. Without it such a revoke changes nothing and exits 1.

--include-connected
Also apply to the root object's own lookup objects. The lookups get
--connected-operations (read only by default), not --operations. Security and system objects
(SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, *Right/*Rights) are skipped with a warning;
name one as --entity-schema-name to change it. With --revoke the lookups are left untouched unless
--connected-operations is given.

--connected-operations LIST
Operations for the connected lookup objects. Default on a grant: read — picking a lookup value only needs
read, so create/edit are never fanned out to shared dictionaries unless passed here explicitly. On a revoke
there is no default: without this option the lookups are not changed.

--confirm
Confirm the destructive change without a prompt. Required in non-interactive runs.

-e, --environment NAME
Registered environment to change.
```

## Examples

Make an object and its lookups readable by a role (lookups get read only):

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations read --include-connected --confirm -e production
```

Grant a functional role the default read/create/edit on one object:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --confirm -e production
```

Grant a functional role full access, including delete:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations read,create,edit,delete --confirm -e production
```

Revoke delete from a role:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations delete --revoke --confirm -e production
```

Return an object to "available to all internal users" by removing its last role grant (revoke every
operation, so the row is emptied even when the role also holds delete):

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations read,create,edit,delete --revoke --disable-operation-permissions --confirm -e production
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
- Exit code 1 also when the named (root) object is not found — nothing was written, so it is not reported
  as a success. A connected lookup that is not found only warns.
- When the root change fails (error, not found, refused last-row revoke), the connected objects are not
  attempted. A failure on one connected object is named and the rest are still attempted (exit code 1).
- With `--include-connected`, if the connected objects cannot be enumerated nothing is written and the
  command exits 1, rather than changing the root alone and reporting success.
- `--disable-operation-permissions` applies to the root object only; a connected lookup is never turned
  off as a side effect of a fan-out.
- Read-modify-write is last-writer-wins: a change another client saves between the read and the save is
  overwritten. Read the result back with `get-object-rights` when concurrent edits are possible.
- On MCP, an unknown or misspelled argument name is refused before any write (the serializer would
  otherwise drop it silently — e.g. `revok` would bind as a grant). On MCP the change is applied without
  a prompt (the tool is flagged destructive), so check the targets first with
  `get-object-rights --include-connected`.
