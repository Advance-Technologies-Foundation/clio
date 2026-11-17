# get-column-rights

## Command Type

Object rights

## Name

get-column-rights - read column permissions (per column, every role's access level denied/read/edit) for an object

## Description

Read-only companion of `get-object-rights` for the **column** (field-level) layer. Reports whether "Use column
permissions" is ON (from the `RightManagementService.svc/GetAdministratedObject` read) and, per column, the rows
stored in **`VwSysEntitySchemaColumnRight`**, in **stored position order**. Storage is read because
`GetAdministratedObject` filters: while the switch is ON it omits a column whose only row is the default (All
employees, edit), and while it is OFF it lists no column. Which row wins for a user in several roles is not measured
for columns, so the order claims nothing more.

The object is named by its **code** (entity schema name), and the code always wins. When no object has that code,
the object whose **title** it is is read, and the output says so; when several objects have that title, the call is
refused and lists them as `'title' (code: name)` — re-run with the code. The object is shown by its title next to its
code when its title differs from the code.

Each column is shown by its **code**, resolved from the column's UId through the entity schema (every package layer,
inherited columns included), with the caption the service returns. When the columns cannot be read, a warning says
so and each column is shown by its UId and caption with `(code not resolved)`; a stored column the schema does not
have is shown by its UId and caption with `(not a column of the schema)`. Entries the service returns for the same
column are shown as one column. Each row is shown as `[position] grantee name (grantee id): denied | read | edit`;
positions are listed from 0, and a row without a position is shown as `[?]` after the others. A level the service
returns that is none of these is shown as `unknown (N)`, a value that is not a number as `unknown (value)`, and a row
without a level as `unknown (no level)`.

Per object the output is one of:

- `Column permissions: OFF — every column is governed only by object and record permissions; stored rows are kept but
  not applied.` The stored rows are listed after it.
- `Column permissions: ON. Columns with rows:` followed by each column whose stored rows are not just the default, with
  its rows (or `No column has rows other than the default row`); then `Default row only (All employees: edit), not a
  restriction (N): <columns>`; then `No rows - per Creatio's documentation only system administrators can access these
  columns (N): <columns>` for the schema's columns with no stored row. Creatio Academy's column permissions
  documentation states that for a column with no rows "users, except for system administrators, will not be able to
  access that column". Long column lists are cut after 20.
- `Column permissions: unknown — the service did not return the column switch (administratedByColumns).` Stored rows,
  if any, are listed after it. It is never reported as OFF.

When the stored rows cannot be read, a warning says so and the `GetAdministratedObject` answer is shown instead;
default and locked columns cannot then be told apart.

With `--column` only that column is shown, named by its code as the schema spells it: its rows, or that it has the
default row only, or no rows. When `--column` is omitted, every column with rows is listed. With `--grantee`, per column, the rows
whose grantee is exactly that id are shown first, followed by the column's other rows; in a column where the grantee
has no row, that is said and every row of the column is listed. Role membership is not resolved: a user's id does not
match the rows of its roles. The command draws no coverage verdict.

## Synopsis

```bash
clio get-column-rights --entity-schema-name <EntitySchemaName> [--column <ColumnCode>] [--grantee <SysAdminUnitId>] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
The object to read: its code (entity schema name, for example UsrOrder), or its title when no object has that code.
Required.

--column CODE
Optional column code (for example UsrDescription): show only that column's rows. When omitted, every column with rows
is listed.

--grantee GUID
Optional SysAdminUnit id (role or user): per column, show the rows whose grantee is exactly that id first, then the
column's other rows. Role membership is not resolved: a user's id does not match its roles' rows. When omitted, every
row is listed.

-e, --environment NAME
Name of the registered environment to read.
```

## Exit codes

| Code | Meaning |
|------|---------|
| 0    | The read ran; the facts are printed. |
| 1    | The entity schema name was missing or names no object by its code or its title, several objects have it as their title, the title lookup failed, `--column` was not a column code or names no column of the object, the columns could not be read for `--column`, `--grantee` was not a GUID, or the object's permissions could not be read. A failed read is never reported as OFF. |

## Examples

List every column with rows, in stored position order:

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
- On MCP the call is a read bounded by the read-response deadline, with the request limits of `get-object-rights`:
  one attempt of at most 30 s per request, and a 90 s limit shared by all requests.
