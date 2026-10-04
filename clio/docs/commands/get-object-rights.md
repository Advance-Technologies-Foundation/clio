# get-object-rights

## Command Type

Object rights

## Name

get-object-rights - read object operation permissions (read/create/edit/delete per role) and record permissions for an object

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
  only through an explicit grant. Every row that would start to decide once operation permissions are turned on
  is listed below it, also with `--grantee` (for an object with no stored rows, that is the `All employees` row
  the service shows, which is not stored). When those rows have none for `All employees`, the output says that
  `set-object-rights --enable-operation-permissions` adds one with read/create/edit/delete below them, unless the
  grant is for `All employees` itself.

With `--grantee` the grantee's row is shown (every row, each with its own position, when it has several),
followed by the rows above it: for a user who is also in one of those roles, they decide first. When the grantee
has NO row, `has NO row (no operations granted)` is followed by every row: a grant adds the row at the lowest
priority, below all of them. The command draws no coverage verdict — which roles a user is in, and what the facts
mean for a given audience, is up to the caller.

With `--include-connected` the root object's own lookup objects are read too. This is the discovery step before
granting: decide per object, then run one `set-object-rights` per object. Security and system lookups
(SysAdmin*, SysUser*, SysSchema*, SysPackage*, SysSettings*, SysLic*, SysProcess*, Vw*, *Right/*Rights) are not
read as connected objects and are named in a warning. A connected object that cannot be read, or a connected set
that cannot be enumerated, is reported with a warning. A read that times out stops the listing — every further read
against the same stand would most likely wait as long — and the objects not read yet are named, to be read one by
one.

Each object also gets its **record layer**: whether "Use record permissions" is ON, and every default record rule —
records created by the author get read / edit / delete at a level (granted, or delegated) for the grantee, plus
"do not apply for manager". Rules stored while record permissions are OFF are listed as not in effect; ON with no rule
means every user sees only the records they create. `--grantee` and `--author` filter the rules. For the named object
the number of existing records is reported (counted under the calling account) — the fact a user needs before
deciding to run `apply-default-record-rights`. Change the record layer with `set-default-record-rights`.

## Synopsis

```bash
clio get-object-rights --entity-schema-name <EntitySchemaName> [--grantee <SysAdminUnitId>] [--author <SysAdminUnitId>] [--include-connected] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
Object (entity schema) name to read. Required.

--grantee GUID
Optional SysAdminUnit id (role or user): show its row and the rows above it (every row when it has none or when the
object is not administered). Omit to list every row. Also filters the default record rules by their grantee.

--author GUID
Optional SysAdminUnit id (role or user): list only the default record rules whose author it is.

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
  `CLIO_MCP_READ_DEADLINE_SECONDS`). So that the answer arrives before it, every request gets one attempt of at
  most 30 s, all requests share a 90 s limit (a request gets at most what is left of it), and the listing stops once
  it is spent, naming the objects not read yet — read them one by one then.
