# get-object-rights

## Command Type

Object rights

## Name

get-object-rights - read object operation permissions (read/create/edit/delete per role) for an object

## Description

Read-only companion of `set-object-rights`. Reports the object's **per-role operation permissions** —
the `SysSchemaOperationRight` / "Object permissions" layer (who may read/create/edit/delete ANY record
of the entity). By default every role's rights are listed; pass `--grantee` to filter to one role.

An object that is not administered by operation permissions is available to all **internal** users only.
External/portal users are deny-by-default and reach an object only through an explicit grant, so with
`--grantee` such an object is never counted as covered: it is listed separately as having no explicit grant
(reachable only if the role is internal). With
`--include-connected` the root object's own lookup objects are read too; combined with
`--grantee` this lists the objects that role cannot **read** — the same information as the Freedom
page designer's red "objects not available to external users" notification. The bar is READ on every
object, because that is what `set-object-rights` grants on connected lookups by default; the operations
each object does hold are printed per object.

Security and system lookups (SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, *Right/*Rights)
are not part of the connected check and are named in a warning. An object that could not be read, or a
connected set that could not be enumerated, is reported as unverified: the command never prints a
coverage line for objects it did not read.

## Synopsis

```bash
clio get-object-rights --entity-schema-name <EntitySchemaName> [--grantee <SysAdminUnitId>] [--include-connected] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Object (entity schema) name to read. Required.

--grantee GUID
Optional SysAdminUnit id to filter to one role. All external users = 720b771c-e7a7-4f31-9cfb-52cd21c3739f.
Omit to report every role.

--include-connected
Also read the root object's own lookup objects (portal-section convenience). Security and system
objects are skipped with a warning.

-e, --environment NAME
Registered environment to read.
```

## Examples

List every role's object permissions:

```bash
clio get-object-rights --entity-schema-name UsrOrder -e production
```

Check whether portal users have access to an object and its lookups:

```bash
clio get-object-rights --entity-schema-name UsrOrder --grantee 720b771c-e7a7-4f31-9cfb-52cd21c3739f --include-connected -e production
```

## Notes

- Backed by the native `RightManagementService.svc/GetAdministratedObject` service (the same service the
  System Designer "Object permissions" section uses). Read-only.
- The schema name is resolved to its UId via a DataService `SelectQuery` over `SysSchema`.
- Pair with `set-object-rights` to grant the operations this command reports as missing.
- Exit code 1 when the named (root) object is not found or its rights cannot be read — the check did not
  happen. A connected object that cannot be read only warns and is reported as unverified.
