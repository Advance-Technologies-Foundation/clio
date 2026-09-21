# get-object-rights

## Command Type

Object rights

## Name

get-object-rights - read whether external (portal) users have object operation access to an object and its connected (lookup) entities

## Description

Read-only companion of `set-object-rights`. For the root object and every entity connected to it
through a lookup column, it reports whether the external (portal) audience (the `All external users`
role) already has object operation access (read/create/edit), and lists the objects that still need a
grant.

This mirrors the Freedom UI page designer's red "objects not available to external users"
notification: use it to grant only where needed and to verify a grant made by `set-object-rights`.

An object that is not administered by operation permissions is reported as available to all (no grant
needed). Connected lookup objects are enumerated from the root entity schema; if that read fails the
root object is still checked (with a warning).

## Synopsis

```bash
clio get-object-rights --entity-schema-name <EntitySchemaName> -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Root object (entity schema) name to check (e.g. UsrOrder). Required.
Its connected lookup objects are checked too.

-e, --environment NAME
Registered environment to read.
```

## Examples

Check external-user object access for a section object and its lookups:

```bash
clio get-object-rights --entity-schema-name UsrOrder -e production
```

## Notes

- Backed by the native Creatio `RightManagementService.svc/GetAdministratedObject` service (the same
  service the System Designer "Object permissions" section uses). Read-only; no confirmation needed.
- The schema name is resolved to its UId via a DataService `SelectQuery` over `SysSchema`.
- Scope: object operation permissions for the `All external users` role. Record and column permissions
  are not reported.
- Pair with `set-object-rights` (the destructive write) to grant the objects this command reports as
  missing.
