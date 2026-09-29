# get-object-rights

## Command Type

Object rights

## Name

get-object-rights - read object operation permissions (read/create/edit/delete per role) for an object

## Description

Read-only companion of `set-object-rights`. Reports the object's **per-role operation permissions** —
the `SysEntitySchemaOperationRight` / "Object permissions" layer (who may read/create/edit/delete ANY record
of the entity). By default every role's row is listed; pass `--grantee` to focus on one role.

The rows are listed in **priority order**, each with its `[position]` (0 is the highest). A user who is in
several roles gets the operations of the highest matching row — decided per row, so a row with no operations
denies them. Every listing states that rule once. Per object the output is one of:

- `administered by operation permissions. Rows in priority order:` followed by the rows;
- `administered by operation permissions, with NO rows` — only holders of the "…any data" system operations
  reach it;
- `not administered by operation permissions` — available to all **internal** users; external users reach it
  only through an explicit grant. The rows that would start to decide once operation permissions are turned on
  are listed below it (for an object with no stored rows, that is the `All employees` row the service shows).

With `--grantee` the grantee's row is shown (every row, each with its own position, when it has several), or
`has NO row (no operations granted)`, followed by the rows above it: for a user who is also in one of those
roles, they decide first. The command draws no coverage verdict — which roles a user is in, and what the facts
mean for a given audience, is up to the caller.

With `--include-connected` the root object's own lookup objects are read too. This is the discovery step before
granting: decide per object, then run one `set-object-rights` per object. Security and system lookups
(SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, SysLic*, SysProcess*, Vw*, *Right/*Rights) are not
read as connected objects and are named in a warning. A connected object that cannot be read, or a connected set
that cannot be enumerated, is reported with a warning.

## Synopsis

```bash
clio get-object-rights --entity-schema-name <EntitySchemaName> [--grantee <SysAdminUnitId>] [--include-connected] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Object (entity schema) name to read. Required.

--grantee GUID
Optional SysAdminUnit id (role or user): show its row and the rows above it. Omit to list every row.

--include-connected
Also read the root object's own lookup objects. Security and system objects are skipped with a warning.

-e, --environment NAME
Registered environment to read.
```

## Examples

List every row of an object, in priority order:

```bash
clio get-object-rights --entity-schema-name UsrOrder -e production
```

Before granting a role access to an object and its lookups, read the role's rows on all of them:

```bash
clio get-object-rights --entity-schema-name UsrOrder --grantee <role-id> --include-connected -e production
```

## Notes

- Backed by the native `RightManagementService.svc/GetAdministratedObject` service (the same service the
  System Designer "Object permissions" section uses). Read-only.
- The schema name is resolved to its UId via a DataService `SelectQuery` over `SysSchema`.
- Pair with `set-object-rights` to change the rows this command reports.
- Exit code 1 when the named (root) object is not found or its rights cannot be read, or when the name is not
  a plain schema identifier (it is trimmed first). A connected object that cannot be read only warns.
- On MCP the call is a read bounded by the read-response deadline (120 s by default,
  `CLIO_MCP_READ_DEADLINE_SECONDS`); `--include-connected` on an object with many lookups can reach it — read
  the lookups one by one then.
