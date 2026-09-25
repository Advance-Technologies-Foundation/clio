# get-object-rights

## Command Type

Object rights

## Name

get-object-rights - read object operation permissions (read/create/edit/delete per role) for an object

## Description

Read-only companion of `set-object-rights`. Reports the object's **per-role operation permissions** —
the `SysSchemaOperationRight` / "Object permissions" layer (who may read/create/edit/delete ANY record
of the entity). By default every role's rights are listed; pass `--grantee` to filter to one role.

The output is the facts of that layer, one line per object: the operations each role (or the grantee)
holds, `NO object operations granted` for a grantee without a row, or `not administered by operation
permissions`. An object that is not administered is available to all **internal** users; external users
reach it only through an explicit grant. The command draws no coverage verdict — what the facts mean for a
given audience is up to the caller.

With `--include-connected` the root object's own lookup objects are read too. Security and system lookups
(SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, *Right/*Rights) are not read as connected
objects and are named in a warning. A connected object that cannot be read, or a connected set that cannot
be enumerated, is reported with a warning.

## Synopsis

```bash
clio get-object-rights --entity-schema-name <EntitySchemaName> [--grantee <SysAdminUnitId>] [--include-connected] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Object (entity schema) name to read. Required.

--grantee GUID
Optional SysAdminUnit id (role or user) to filter to one role. Omit to report every role.

--include-connected
Also read the root object's own lookup objects. Security and system objects are skipped with a warning.

-e, --environment NAME
Registered environment to read.
```

## Examples

List every role's object permissions:

```bash
clio get-object-rights --entity-schema-name UsrOrder -e production
```

Read one role's operations on an object and its lookups:

```bash
clio get-object-rights --entity-schema-name UsrOrder --grantee <role-id> --include-connected -e production
```

## Notes

- Backed by the native `RightManagementService.svc/GetAdministratedObject` service (the same service the
  System Designer "Object permissions" section uses). Read-only.
- The schema name is resolved to its UId via a DataService `SelectQuery` over `SysSchema`.
- Pair with `set-object-rights` to change the operations this command reports.
- Exit code 1 when the named (root) object is not found or its rights cannot be read. A connected object
  that cannot be read only warns.
