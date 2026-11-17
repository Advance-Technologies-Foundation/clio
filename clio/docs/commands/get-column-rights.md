# get-column-rights

## Command Type

Object rights

## Name

get-column-rights - read column permissions (per column, every role's access level denied/read/edit) for an object

## Description

Read-only companion of `get-object-rights` for the **column** (field-level) layer, from the same
`RightManagementService.svc/GetAdministratedObject` read. Reports whether "Use column permissions" is ON for the
object and, per column that has rows, every row in **priority order**.

The object is named by its **code** (entity schema name), and the code always wins. When no object has that code,
the object whose **title** it is is read, and the output says so; when several objects have that title, the call is
refused and lists them as `'title' (code: name)` — re-run with the code. The object is shown by its title next to its
code when its title differs from the code.

Each column is shown by its **code**, resolved from the column's UId through the entity schema (every package layer,
inherited columns included), with the caption the service returns. When the columns cannot be read, a warning says
so and each column is shown by its UId and caption. Each row is shown as
`[position] grantee name (grantee id): denied | read | edit`; position 0 is the highest. A level the service returns
that is none of these is shown as `unknown (N)`.

Per object the output is one of:

- `Column permissions: OFF — every column is governed only by object and record permissions.` Rows stored while the
  switch is OFF are listed after it.
- `Column permissions: ON, and no column has a permission row.`
- `Column permissions: ON. Columns with rows:` followed by each column and its rows.

The service does not store a column whose rows all grant edit, so such a column never appears.

With `--column` only that column is shown; a column with no rows is reported as such. With `--grantee`, per column,
the grantee's row is shown followed by the rows above it; in a column where the grantee has no row, every row of the
column is listed. The command draws no coverage verdict.

## Synopsis

```bash
clio get-column-rights --entity-schema-name <EntitySchemaName> [--column <ColumnCode>] [--grantee <SysAdminUnitId>] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
The object to read: its code (entity schema name), or its title when no object has that code. Required.

--column CODE
Optional column code (for example UsrDescription): show only that column's rows.

--grantee GUID
Optional SysAdminUnit id (role or user): per column, show its row and the rows above it (every row of a column where
it has none). Omit to list every row.

-e, --environment NAME
Registered environment to read.
```

## Examples

List every column with rows, in priority order:

```bash
clio get-column-rights --entity-schema-name UsrOrder -e production
```

Read one column's rows for one role:

```bash
clio get-column-rights --entity-schema-name UsrOrder --column UsrAmount --grantee <role-id> -e production
```

## Notes

- Backed by the native `RightManagementService.svc/GetAdministratedObject` service (the same read as
  `get-object-rights`): `administratedByColumns` is the switch, `entitySchemaColumnsRights` the columns and their rows
  (`rightLevel` 0 Access denied, 1 Reading, 2 Editing). Read-only.
- The column codes come from the merged entity schema (the same read as `get-entity-schema-properties` without a
  package).
- Exit code 1 when no object has the name as its code or its title, when several objects have it as their title,
  when the title lookup fails, when `--column` is not a column code or names no column of the object (or the columns
  cannot be read to find it), when `--grantee` is not a GUID, or when the object's permissions cannot be read. A
  failed read is never reported as OFF.
- On MCP the call is a read bounded by the read-response deadline, with the request limits of `get-object-rights`:
  one attempt of at most 30 s per request, and a 90 s limit shared by all requests.
