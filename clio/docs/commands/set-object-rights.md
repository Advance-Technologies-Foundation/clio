# set-object-rights

## Command Type

Object rights

## Name

set-object-rights - grant or revoke object operation permissions (read/create/edit/delete) for one role on one object, or turn an object's operation permissions on or off

## Description

Grants (or, with `--revoke`, revokes) **object operation permissions** for one role on **one object** — the
`SysEntitySchemaOperationRight` / "Object permissions" layer that decides who may read/create/edit/delete ANY
record of an entity — or turns the object's "Use operation permissions" switch on or off. It works like the Object
permissions designer, one object per call, and for **any** role. It is the object-level analog of `set-record-rights`
(which is per-record).

A grant or revoke names the role in `--grantee` and the operations in `--operations`: nothing is granted by default.
A call that only turns the switch names neither. The switch and the rows change separately: a revoke never turns the
switch off, and turning it off keeps every row.

The rows of an object are a **priority list**: position 0 is the highest, and a user who is in several roles gets
the operations of the highest matching row — decided per row, so a row with no operations denies them. The command
follows that model:

- A new row goes at the lowest priority (one past the highest position), as in the designer. The result names the
  rows above the grantee's row: for a user who is also in one of those roles, that row decides first.
- A revoke clears the named operations on the role's row and **keeps the row**. For a user whose highest matching row
  it is, the cleared operations are then denied; removing the row would let a lower row decide instead. No row is ever
  removed and no row is ever moved.

Every change that alters who can reach the object must be **named** in the arguments, or the call is refused and
nothing is written:

- A grant on an object that does not use operation permissions yet would turn them **ON** — after which its rows
  decide who can reach it. That needs `--enable-operation-permissions`. The same save keeps the `All employees` row
  the service shows for an object with no stored rows; when the object has stored rows but none for `All employees`,
  it adds one with read/create/edit/delete below them. Rows above it still decide first for their members.
- `--enable-operation-permissions` alone — with no `--grantee` and no `--operations` — turns operation permissions
  **ON** with the stored rows as they are, under the same `All employees` rule. It is refused when no row would grant
  any operation (every internal user would be cut off): grant the operations in the same call instead — for internal
  users to `All employees`, whose row decides before a new row of any other role.
- `--disable-operation-permissions` is a call of its own — no `--grantee`, `--operations` or `--revoke` — that turns
  operation permissions **OFF** and keeps every row exactly as it is, operations included, as the designer's switch
  does: the object becomes available to ALL internal users, external users have no access while it is off, and the
  rows apply again when the switch is turned back on.
- A revoke never turns operation permissions off. A revoke that would leave the object with no row granting any
  operation is refused; to turn the switch off instead, make the disable call.

To cover an object's lookups, read them first with `get-object-rights --include-connected`, decide per object,
and run one `set-object-rights` per object. The listing leaves out security and system objects (`SysAdmin*`,
`SysSettings*`, …); a call that names one of them is applied like any other, and the object's name is in the
arguments the MCP host shows.

It is a read-modify-write over the native `RightManagementService`: the object is read, the change is planned,
the rows the plan changes or adds are saved (every other row is sent exactly as it was read), and the object is read
back and compared with the plan, row by row. It does **not** change column or record permissions.

**Destructive.** In a non-interactive run it refuses to apply unless `--confirm` is passed; in an interactive run
it shows the planned change and asks for a `y/n` confirmation. `--preview` writes nothing and shows the planned
change. On MCP the call applies the change: the host's approval of the call is the confirmation, and the
arguments name the operations and every access-changing transition, so the approval shows everything the call can do.

The object is named by its **code** (entity schema name: letters, digits, `_`; trimmed), which the approval shows.
A title is refused, naming the code it belongs to — `'Creatio functionality' is not an object code: it is the title
of Feature` — as `run-process` refuses a process caption: a title is not unique, and the same word can be one
object's code and another object's title. The code always wins. The output shows the object's title next to its
code — `'Creatio functionality' (Feature)` — so the developer sees which object the code names.

## Synopsis

```bash
clio set-object-rights --entity-schema-name <EntitySchemaName> --grantee <SysAdminUnitId> --operations <read,create,edit,delete> [--revoke] [--enable-operation-permissions] [--confirm | --preview] -e <environment>
clio set-object-rights --entity-schema-name <EntitySchemaName> (--enable-operation-permissions | --disable-operation-permissions) [--confirm | --preview] -e <environment>
```

## Options

```bash
--entity-schema-name NAME
The one object whose operation permissions are changed, by its code (entity schema name). A title is refused,
naming the code it belongs to. Required.

--grantee GUID
SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id. It must exist. Required for a
grant or revoke; not given when the call only turns operation permissions on or off.

--operations LIST
Comma-separated operations to grant or revoke: read,create,edit,delete. Required for a grant or revoke: no operation
is granted by default. A value that names no operation ("", ",") is refused.

--revoke
Revoke the operations named in --operations instead of granting them. The role's row is kept, with those operations
cleared.

--enable-operation-permissions
Turn the object's operation permissions ON. With --grantee and --operations it lets the grant turn them on; without
it, a grant on an object that does not use operation permissions is refused (exit 1) and the refusal names the rows
that would start to decide. Alone it turns them on with the stored rows as they are. Not valid with --revoke.

--disable-operation-permissions
Turn the object's operation permissions OFF, keeping every row as it is, operations included: the object becomes
available to ALL internal users, and the rows apply again if operation permissions are turned back on. A call of its
own: not valid with --grantee, --operations, --revoke or --enable-operation-permissions.

--confirm
Confirm the destructive change without a prompt. Required in non-interactive runs. Not valid with --preview.

--preview
Write nothing: show the planned change — which rows start or keep deciding, where the grantee's row sits, what
turns on or off — or why the call would be refused (exit 1).

-e, --environment NAME
Registered environment to change.
```

## Examples

Show what a grant would change, without writing anything:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations read --preview -e production
```

Grant a role read/create/edit on an object that already uses operation permissions:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations read,create,edit --confirm -e production
```

Grant read on an object that does not use operation permissions yet (turns them on, keeps All employees):

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations read --enable-operation-permissions --confirm -e production
```

Cover an object's lookups: list them, then grant read on each approved lookup in its own call:

```bash
clio get-object-rights --entity-schema-name UsrOrder --include-connected -e production
clio set-object-rights --entity-schema-name UsrOrderStatus --grantee <role-id> --operations read --enable-operation-permissions --confirm -e production
```

Revoke delete from a role (its row stays, now without delete):

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations delete --revoke --confirm -e production
```

Turn an object's operation permissions off — available to all internal users — keeping every row as it is:

```bash
clio set-object-rights --entity-schema-name UsrOrder --disable-operation-permissions --confirm -e production
```

Turn them back on with the rows as they are:

```bash
clio set-object-rights --entity-schema-name UsrOrder --enable-operation-permissions --confirm -e production
```

## Notes

- Backed by `RightManagementService.svc/GetAdministratedObject` + `SaveAdministratedObject` (a
  read-modify-write). The untouched record, column and entity-operation collections are sent as null.
- The output and the exit code come from the planned change and the read-back, compared row by row (grantee,
  position, operations). If the object read back does not show a row this call writes — the grantee's row, or the
  `All employees` row an enable stores — where it was planned, or the switch is not as planned, the call fails
  (exit 1). A row of another role that differs from the plan, or that the plan did not write, is reported as a
  warning. If the read-back itself fails, the call fails too (exit 1): the change is reported as saved but NOT
  verified, with a request to check the object with `get-object-rights` before retrying.
- A save that reports an error — a timeout, for example — may still have been committed, so the object is read back:
  when it shows the planned change, the call succeeds with a warning; otherwise it fails and shows the object as read.
  A save that got no answer — it did not answer in time, or the connection broke after the request went out — may
  even land after that read-back, so its failure says the change may still be applied: re-read the object with
  `get-object-rights` before retrying or reporting a failure.
- The save is sent exactly once, whatever the retry settings, like the other clio writes: a transport retry of a
  save the server already committed could add a new row a second time (it is sent without an id). The read-back
  after a failed save is one attempt too.
- On MCP the worker is killed at its budget (120 s by default), so every request gets one attempt of at most 25 s,
  all requests share a 100 s limit (a request gets at most what is left of it), and the save is sent only while
  50 s are left for it and the read-back. Otherwise the call fails before the save (exit 1) and nothing is changed:
  re-run it. A save that gets no answer is thus still read back and reported before the worker is killed.
- Exit code 1: invalid input (a name that is an object's title rather than its code — the refusal names the code — or
  that is neither, a grantee that is not a GUID, a missing
  `--operations` on a grant or revoke, an unknown operation, an `--operations` value that names no operation,
  `--preview` with `--confirm`, `--enable-operation-permissions` with `--revoke`,
  `--disable-operation-permissions` with `--grantee`, `--operations`, `--revoke` or `--enable-operation-permissions`,
  a call that names no change at all); a missing `--confirm` in a non-interactive run; an object that is not found or
  cannot be read; a title lookup that fails (when the name is a code no object has, the error says that too); a grantee that does not exist in `SysAdminUnit`; a refused plan; a failed save whose read-back does
  not show the plan; a successful save whose read-back fails; a read-back that does not show a row this call writes,
  or the planned switch.
- A re-run that changes nothing says which row already is in the requested state, or that the grantee has no row to
  revoke from, or that the switch already is where the call puts it (exit 0): a retry after a timeout is safe. A
  disable on an object that is already off says that the object is available to all internal users.
- A disable of an administered object with no stored rows writes no row. The object read back — now off — shows the
  `All employees` row the service synthesizes for an object with no stored rows; the read-back does not count it as a
  difference.
- When the grantee is `All employees` and the object has no row for it, the new row gets exactly the operations the
  call names: an enable then adds no second `All employees` row with every operation.
- A grantee with more than one row on the object is refused: which of them decides depends on the other rows, so
  the command changes none of them. Remove the duplicates in the Object permissions designer, then re-run.
- A revoke on an object that does not use operation permissions is refused: company employees reach it whatever its
  rows say (only technical users follow the rows while it is off). To limit access, turn operation permissions on
  first (a grant with `--enable-operation-permissions`, which keeps the object's `All employees` row as it is or adds
  one with every operation), then revoke from that row what employees must not have.
- Read-modify-write is last-writer-wins: a change another client saves between the read and the save is
  overwritten. The read-back reports any difference from the plan.
- On MCP, an unknown or misspelled argument name is refused before any read or write (the serializer would
  otherwise drop it silently — e.g. `revok` would bind as a grant).
