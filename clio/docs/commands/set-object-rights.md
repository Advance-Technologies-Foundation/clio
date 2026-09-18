# set-object-rights

## Command Type

Object rights

## Name

set-object-rights - grant operation and record permissions to an object and its connected (lookup) entities

## Description

Turns on operation and record permissions for an object, and operation permissions for every entity
connected to it through a lookup column, making it available to the external (portal) audience. It is
the object-level analog of `set-record-rights` (which grants a single per-record right) and wraps the
Freedom UI page designer's "Update object permissions now" action.

Only the root object is sent; the server (`SectionService`) resolves the connected lookup objects from
the root entity schema and grants them itself — you do **not** enumerate them. Unlike
`set-record-rights` there is no grantee/operation argument: the platform service is coarse and grants
the external audience the access those objects need.

It does **not** change column permissions (that is a separate opt-in). The call is asynchronous on the
server (rights are recalculated) and can take tens of seconds.

**Destructive.** This command changes access. In a non-interactive run it refuses to apply unless
`--confirm` is passed; in an interactive run it asks for a `y/n` confirmation. Re-granting the same
object access is idempotent.

## Synopsis

```bash
clio set-object-rights --entity-schema-name <EntitySchemaName> --confirm -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Root object (entity schema) name whose access is granted (e.g. UsrOrder). Required.
Connected lookup objects are resolved server-side.

--confirm
Confirm the destructive change without a prompt. Required in non-interactive runs.

-e, --environment NAME
Registered environment to change.
```

## Examples

Grant object access for a section object and its lookups:

```bash
clio set-object-rights --entity-schema-name UsrOrder --confirm -e production
```

## Notes

- The server resolves and grants the connected (lookup) objects from the root schema; the command
  sends only the root object name.
- Scope: object operation permissions (root + connected) plus record permissions on the root object.
  Column permissions are out of scope — a separate opt-in.
- The MCP `set-object-rights` tool is marked destructive; `--confirm` is CLI-only; on the MCP surface
  the Destructive flag is the gate.
