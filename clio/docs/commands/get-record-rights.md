# get-record-rights

## Command Type

Record rights

## Name

get-record-rights - read the record-level access rights of a single Creatio record

## Description

Reads and prints the record-level access rights of a single record, mirroring the read half
of the Creatio UI's record-rights panel. Each grant is printed as
`<operation> / <level> -> <grantee display name> (<grantee id>)`, where operation is
`read`/`edit`/`delete` and level is `granted`/`delegated`.

Address the target with `--entity` and `--record-id` (both required). A client-unit
schema/dashboard is addressed like any entity: `--entity SysSchemaAdminUnit --record-id <schema UId>`.

## Synopsis

```bash
clio get-record-rights --entity <EntitySchemaName> --record-id <guid> -e <environment>
```

## Options

```bash
--entity ENTITY
Entity schema name of the target record (e.g. Contact). Required. Use with --record-id.
For a client-unit schema/dashboard, pass SysSchemaAdminUnit.

--record-id GUID
Primary column value (record id) of the target record. Required. Use with --entity.
For a client-unit schema/dashboard, the schema UId.

-e, --environment NAME
Registered environment to query.
```

## Examples

Read the rights of a Contact record:

```bash
clio get-record-rights --entity Contact --record-id 7f3b869f-34f3-4f20-ab4d-7480a5fdf647 -e production
```

Read the rights of a client-unit schema/dashboard (addressed as `SysSchemaAdminUnit`):

```bash
clio get-record-rights --entity SysSchemaAdminUnit --record-id 7bdd745c-1111-2222-3333-444444444444 -e production
```

## Notes

- Read-only; it never changes access.
- The rights table for an entity is resolved by the verified `Sys<Entity>Right` naming
  convention.
