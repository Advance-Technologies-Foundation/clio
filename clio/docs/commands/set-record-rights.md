# set-record-rights

## Command Type

Record rights

## Name

set-record-rights - grant or revoke a record-level access right on a single Creatio record

## Description

Grants or revokes a single record-level access right. It does **not** read the current rights:
it sends exactly one row change to the RightsService (an insert for a grant, a delete for a
revoke). The service acts only on the row it is sent, so every other grant on the record is left
untouched.

**Destructive.** This command changes access. In a non-interactive run it refuses to apply
unless `--confirm` is passed; in an interactive run it asks for a `y/n` confirmation. Re-granting
a right that already exists is idempotent on the server; revoking one that does not exist is a
safe no-op.

Address the target the same way as `get-record-rights`: `--entity` + `--record-id` (both
required). A client-unit schema/dashboard is addressed like any entity:
`--entity SysSchemaAdminUnit --record-id <schema UId>`.

## Synopsis

```bash
clio set-record-rights --entity <EntitySchemaName> --record-id <guid> \
    --grantee <guid> --operation <read|edit|delete> \
    [--level <granted|delegated>] [--revoke] --confirm -e <environment>
```

## Options

```bash
--entity ENTITY
Entity schema name of the target record (e.g. Contact). Required. Use with --record-id.
For a client-unit schema/dashboard, pass SysSchemaAdminUnit.

--record-id GUID
Primary column value (record id) of the target record. Required. Use with --entity.
For a client-unit schema/dashboard, the schema UId.

--grantee GUID
Grantee SysAdminUnit GUID. A value that is not a GUID is rejected.

--operation read|edit|delete
Operation to grant or revoke.

--level granted|delegated
Right level for a grant. Default: granted. Ignored for --revoke.

--revoke
Revoke (remove) the right instead of granting it.

--confirm
Confirm the destructive apply without a prompt. Required in non-interactive runs.

-e, --environment NAME
Registered environment to change.
```

## Examples

Grant read to a grantee on a Contact record:

```bash
clio set-record-rights --entity Contact --record-id 7f3b869f-... --grantee 8ab1343f-cb58-49c7-95a2-058b5f60acd3 --operation read --confirm -e production
```

Grant edit with delegation to a client-unit schema/dashboard (addressed as `SysSchemaAdminUnit`):

```bash
clio set-record-rights --entity SysSchemaAdminUnit --record-id 7bdd745c-1111-2222-3333-444444444444 --grantee 7f3b869f-34f3-4f20-ab4d-7480a5fdf647 --operation edit --level delegated --confirm -e production
```

Revoke delete from a grantee:

```bash
clio set-record-rights --entity Contact --record-id 7f3b869f-... --grantee 8ab1343f-cb58-49c7-95a2-058b5f60acd3 --operation delete --revoke --confirm -e production
```

## Notes

- The apply sends a single-row change, so other grants are untouched — the service acts only
  on the row it is sent, not because the command echoes the full current set.
- The MCP `set-record-rights` tool is marked destructive; `--confirm` is CLI-only; on the MCP
  surface the Destructive flag is the gate.
