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

The object is named by its **code** (entity schema name); a title is refused, naming the code to pass. `--column` is the column **code**,
resolved to its UId through the entity schema (every package layer included). `--grantee` is a SysAdminUnit id, which
must exist.

- `--access denied|read|edit` sets the grantee's row in the column. A grantee with no row gets one after the column's
  last row; an existing row keeps its place and gets the new level.
- `--position N` places the row (with `--access`) or moves the grantee's row (alone) to `N` in the column's **stored
  position order**, 0 first, and renumbers the column's rows `0..n-1`. Only an explicit number is accepted: how
  Creatio ranks the rows of one column against each other is **not measured**, so the command claims no precedence.
- `--remove-row` deletes the grantee's row; a repeated removal is no change. Removing a column's last row other than
  the default leaves it the default row (All employees: edit), which **widens** access; the summary says so.

The switch changes only through its flags: a grant while column permissions are OFF is refused unless
`--enable-column-permissions` is passed with it; a move or a removal while they are OFF is refused (turn them on
first); `--enable-column-permissions` alone turns them on; `--disable-column-permissions` alone turns them off and keeps
every stored row, not applied.

### What the save sends, and how it is checked

Measured on Creatio storage (`VwSysEntitySchemaColumnRight`, 17 Nov 2026): a save with a column list gives each column
**in** the list exactly the listed rows (`[]`: none) and **every other column of the object the default row** (All
employees, edit, position 0); `entitySchemaColumnsRights: null` leaves the stored rows alone. `GetAdministratedObject`
filters what it returns (it omits a default-only column while the switch is ON, and every column while it is OFF), so
the list is built from **storage**:

- every column whose stored rows are not exactly the default, as stored, with the named column changed;
- while column permissions are ON, a column with no rows is sent with `rights: []`, so it stays as it is (per Creatio's
  documentation only system administrators can access it);
- when the call turns them ON, a column with no rows is not sent, so it gets the default row and no column is locked
  (enabling with `null` would leave every such column locked); the output names those columns;
- a column whose last row is removed is not sent, so it gets the default row;
- a default-only column is not sent: the service writes the same default.

A disable sends `null`. The operation, record and entity-operation collections are always sent as null. Removing the
only row of a default-only column is refused: the default would come back.

### Guards before the save

- **Cross-check (switch ON):** the stored rows must match what `GetAdministratedObject` lists — every restricted column
  with its rows, a column with no rows with none, a default column omitted. Any disagreement refuses the call: a save
  built from a short storage read would lock every column it missed.
- **Counted read:** storage is counted before it is read; a read with fewer rows than the count is refused.
- **Second read when enabling:** while column permissions are OFF the service lists nothing, so turning them on reads
  the rows again by the object's column UIds; rows of this object the first read missed refuse the call. Every output
  says how many stored rows the plan was read from.
- **One write per object at a time:** a write takes a lease on the object — keyed by the environment's resolved URL and
  the object's code; an exclusive OS file handle, released if the process dies — for its whole run, shared with
  `set-object-rights` and `set-default-record-rights`; a second write of the object while it is held is refused.
  **Calls for one object must not run in parallel.**
- **Re-read before the save:** the object and its stored rows are read again right before the save; if any of the three
  switches (column, operation, record permissions), the schema UId or a stored row changed since the plan, nothing is
  saved. The save goes out from that fresh read.

### Read-back

The service answers `success: true` whatever it stores, so after every save the switches and the **stored rows are
read back**:

- after a save that names columns, any difference from the plan **fails** the call (every column the plan names must
  hold its expected rows; a column the list leaves out, the default row), and the error says whether nothing was
  applied or the change was **partially applied**;
- a changed operation or record switch **fails** it too (the save sends them as read);
- after a disable (no list), a changed stored row is only a **warning**: another client may have changed it.

### Row order

Which row wins for a user in several roles is not measured for columns. When a restriction ends up after an All
employees row with edit (for example a grant that turns column permissions on, on a column with no rows, appends after
the default row), the output warns; put a restriction first with `--position 0`.

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
The object, by its code. A title is refused, naming the code. Required.

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
Remove the grantee's row; no change when it has none. Removing the column's last row other than the default leaves the
default row (All employees: edit), which widens access. A call of its own: not with --access, --position or a switch
flag.

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

Deny a role the column, first in the column's position order (recommended for a restriction):

```bash
clio set-column-rights --entity-schema-name UsrOrder --column UsrAmount --grantee <role-id> --access denied --position 0 --confirm -e dev
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

- Refused before any write: a grant while column permissions are OFF without `--enable-column-permissions`; a move or a
  removal while they are OFF; a removal or a move of a row the grantee does not have; a grantee with several rows in
  the column; a position past the end of the rows the change starts from; removing a default-only column's row; an
  unknown column, an unknown grantee, several objects with the title; a read with no readable switch (never taken for
  OFF); a stored row with no readable position, level or grantee (every restricted column goes back as stored);
  storage that disagrees with the service, is cut short or cannot be read; schema columns that cannot be read; another
  call holding the object's lease; stored rows that changed before the save; a time limit with no room for the save.
- Exit code 0 when the storage read-back matches the plan, when the call changes nothing, for a preview, or when the
  prompt is answered no; 1 otherwise.
- On MCP the tool is `set-column-rights` (destructive, idempotent: a repeated call finds its change in place). The host
  approval is the confirmation; `preview=true` writes nothing. A write makes up to thirteen requests in sequence; each
  gets one attempt of at most 20 s, all of them share a 100 s limit, and the save is sent only while four timeouts are
  left for it and its counted read-back.
- See `docs/knowledge/platform/column-permissions-come-from-the-administrated-object.md` for the measured service
  facts.
