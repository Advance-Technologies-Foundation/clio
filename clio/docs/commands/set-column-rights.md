# set-column-rights

## Command Type

Object rights

## Name

set-column-rights - set one role's access (denied/read/edit) to one column of one object, move or remove that role's
row, or turn the object's column permissions on or off (destructive)

## Description

Changes the **column** (field-level) permissions of ONE object — one column and one grantee per call — through
`RightManagementService.svc/SaveAdministratedObject`, the payload of the System Designer "Column permissions" tab.
Read the result with [`get-column-rights`](get-column-rights.md).

The object is named by its **code** (entity schema name); when no object has that code, the one object whose title it
is is changed and the output says so; several objects with that title are refused. `--column` is the column **code**,
resolved to its UId through the entity schema (every package layer included). `--grantee` is a SysAdminUnit id, which
must exist.

- `--access denied|read|edit` sets the grantee's row in the column. A grantee with no row gets one after the column's
  last row; an existing row keeps its place and gets the new level.
- `--position N` places the row (with `--access`) or moves the grantee's row (alone) to `N` in the column's **stored
  position order**, 0 first, and renumbers the column's rows `0..n-1`. Only an explicit number is accepted: how
  Creatio ranks the rows of one column against each other is **not measured**, so the command claims no precedence.
- `--remove-row` deletes the grantee's row.

The switch changes only through its flags: a grant while column permissions are OFF is refused unless
`--enable-column-permissions` is passed with it; `--enable-column-permissions` alone turns them on with the stored rows
as they are; `--disable-column-permissions` alone turns them off and keeps every stored row.

### What the save sends, and how it is checked

Measured on Creatio (17 Nov 2026): `entitySchemaColumnsRights: null` leaves the stored column rows alone, a list
**replaces every column's rows**, and `[]` clears them all. So a row change sends the **whole** list read, with only
the named column changed — every other column goes back exactly as read; a switch-only call sends the list as null.
The operation, record and entity-operation collections are always sent as null.

The service answers `success: true` even for rows it did not store, so after every save the object is **read back and
compared** with the plan. Any difference fails the call and names it. A column whose rows all grant `edit` is not
stored by the service (edit is the default): when the plan leaves a column all-edit, the preview says so and the
result reports that the column has no rows — by design, not a failure.

### No package binding

The rows are written to the environment only; they are **not bound to a package**. On the measured site there is no
`SysEntitySchemaColumnRight` entity schema (the rows live behind the view `VwSysEntitySchemaColumnRight`), and no
package binds column rights, so how Creatio carries them in a package is not established. Binding is a follow-up once
the platform's delivery path is measured.

## Synopsis

```bash
clio set-column-rights --entity-schema-name <EntitySchemaName> --column <ColumnCode> --grantee <SysAdminUnitId> --access <denied|read|edit> [--position <N>] [--enable-column-permissions] [--preview | --confirm] -e <environment>
clio set-column-rights --entity-schema-name <EntitySchemaName> --column <ColumnCode> --grantee <SysAdminUnitId> (--remove-row | --position <N>) [--preview | --confirm] -e <environment>
clio set-column-rights --entity-schema-name <EntitySchemaName> (--enable-column-permissions | --disable-column-permissions) [--preview | --confirm] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
The object: its code, or its title when no object has that code. Required.

--column CODE
Column code whose rows change. Required unless the call only turns the switch.

--grantee GUID
SysAdminUnit id (role or user) whose row changes. Required unless the call only turns the switch.

--access denied|read|edit
The level the grantee's row gets.

--position N
Position in the column's stored position order, 0 first: with --access it places the row, alone it moves the
grantee's row. Past the end is refused.

--remove-row
Remove the grantee's row. A call of its own: not with --access, --position or a switch flag.

--enable-column-permissions
Turn column permissions ON: with --access, or alone.

--disable-column-permissions
Turn column permissions OFF, keeping every stored row. A call of its own.

--preview
Write nothing: show the change, the column's rows before and after, and whether it would be refused.

--confirm
Apply without a prompt (required in a non-interactive run).

-e, --environment NAME
Registered environment.
```

## Examples

Preview denying a role the Amount column, turning column permissions on:

```bash
clio set-column-rights --entity-schema-name UsrOrder --column UsrAmount --grantee <role-id> --access denied --enable-column-permissions --preview -e dev
```

Let a role read the column, first in the column's position order:

```bash
clio set-column-rights --entity-schema-name UsrOrder --column UsrAmount --grantee <role-id> --access read --position 0 --confirm -e dev
```

Remove the role's row, or turn column permissions off keeping the rows:

```bash
clio set-column-rights --entity-schema-name UsrOrder --column UsrAmount --grantee <role-id> --remove-row --confirm -e dev
clio set-column-rights --entity-schema-name UsrOrder --disable-column-permissions --confirm -e dev
```

## Notes

- Refused before any write: a grant while column permissions are OFF without `--enable-column-permissions`; a removal
  or a move of a row the grantee does not have; a grantee with several rows in the column; a position past the end; an
  unknown column, an unknown grantee, several objects with the title; a read with no readable switch (never taken for
  OFF); a row of the changed column with no readable position, level or grantee (its rows must go back exactly as
  stored); a column entry whose UId is not a GUID (it could be the changed column, which would then be stored twice).
- Columns the call does not change are sent back as their raw read nodes, so a repeated entry or an irregular row in
  them is stored exactly as it was. When the service lists the changed column twice, nothing is sent.
- Exit code 0 when the save's read-back matches the plan (a column dropped as all-edit is reported), when the call
  changes nothing, for a preview, or when the prompt is answered no; 1 otherwise.
- On MCP the tool is `set-column-rights` (destructive, idempotent: a repeated call finds its change in place). The host
  approval is the confirmation; `preview=true` writes nothing. Each request gets one attempt of at most 25 s, and all
  of them share a 100 s limit, as for `set-object-rights`.
- See `docs/knowledge/platform/column-permissions-come-from-the-administrated-object.md` for the measured service
  facts.
