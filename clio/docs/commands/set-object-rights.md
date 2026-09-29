# set-object-rights

## Command Type

Object rights

## Name

set-object-rights - grant or revoke object operation permissions (read/create/edit/delete) for one role on one object

## Description

Grants (or, with `--revoke`, revokes) **object operation permissions** for one role on **one object** — the
`SysSchemaOperationRight` / "Object permissions" layer that decides who may read/create/edit/delete ANY
record of an entity. It works like the Object permissions designer, one object per call, and for **any** role.
It is the object-level analog of `set-record-rights` (which is per-record).

The rows of an object are a **priority list**: position 0 is the highest, and a user who is in several roles gets
the operations of the highest matching row — decided per row, so a row with no operations denies them. The command
follows that model:

- A new row goes at the lowest priority (one past the highest position), as in the designer. The result names the
  rows above it: for a user who is also in one of those roles, that row decides first.
- A revoke clears the operations on the role's row and **keeps the row**, so the cleared operations are denied to
  the role's members. No row is ever removed and no row is ever moved.

Every change that alters who can reach the object must be **named** in the arguments, or the call is refused and
nothing is written:

- A grant on an object that does not use operation permissions yet would turn them **ON** — after which only its
  rows decide who can reach it. That needs `--enable-operation-permissions`. The same save keeps (or, when the
  object has none, adds) an `All employees` row with read/create/edit/delete, so internal users keep their access.
- A revoke that would leave the object with no row granting any operation needs `--disable-operation-permissions`,
  which turns operation permissions **OFF** instead: the object becomes available to all internal users.
- On a security or system object, a grant beyond read, or a disable, needs `--allow-security-object`.

To cover an object's lookups, read them first with `get-object-rights --include-connected`, decide per object,
and run one `set-object-rights` per object.

It is a read-modify-write over the native `RightManagementService`: the object is read, the change is planned,
the planned state is saved, and the object is read back and compared with the plan. It does **not** change column
or record permissions.

**Destructive.** In a non-interactive run it refuses to apply unless `--confirm` is passed; in an interactive run
it shows the planned change and asks for a `y/n` confirmation. `--preview` writes nothing and shows the planned
change. On MCP the call applies the change: the host's approval of the call is the confirmation, and the
arguments name every access-changing transition, so the approval shows everything the call can do.

The object name is trimmed and must be a plain schema identifier (letters, digits, `_`).

## Synopsis

```bash
clio set-object-rights --entity-schema-name <EntitySchemaName> --grantee <SysAdminUnitId> [--operations read,create,edit,delete] [--revoke] [--enable-operation-permissions] [--disable-operation-permissions] [--allow-security-object] (--confirm | --preview) -e <environment>
```

## Options

```bash
--entity-schema-name NAME
The one object (entity schema) whose operation permissions are changed. Required.

--grantee GUID
SysAdminUnit id (role or user) to grant/revoke. Names are not unique — pass the id. It must exist.

--operations LIST
Comma-separated: read,create,edit,delete. Default: read,create,edit (delete not granted by default).

--revoke
Revoke instead of grant. The role's row is kept: with its operations cleared it denies them to its members.

--enable-operation-permissions
Allow a grant to turn the object's operation permissions ON. Without it, a grant on an object that does not use
operation permissions is refused (exit 1) and the refusal names the rows that would start to decide. Not valid
with --revoke.

--disable-operation-permissions
With --revoke: turn the object's operation permissions OFF, which makes it available to ALL internal users. The
rows are kept and apply again if operation permissions are turned back on. Needed when the revoke would leave no
row that grants any operation; not valid without --revoke.

--allow-security-object
Allow a grant beyond read, or --disable-operation-permissions, on a security or system object (SysAdmin*,
SysUser*, SysSchema*, SysPackage*, SysSettings*, SysLic*, SysProcess*, Vw*, *Right/*Rights). Without it such an
object may only be granted read. A plain revoke on it is allowed: it only narrows access.

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

Grant a role the default read/create/edit on an object that already uses operation permissions:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --confirm -e production
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

Return an object to "available to all internal users" when the revoke takes away its last granting row:

```bash
clio set-object-rights --entity-schema-name UsrOrder --grantee <role-id> --operations read,create,edit,delete --revoke --disable-operation-permissions --confirm -e production
```

## Notes

- Backed by `RightManagementService.svc/GetAdministratedObject` + `SaveAdministratedObject` (a
  read-modify-write). The untouched record, column and entity-operation collections are sent as null.
- The output and the exit code come from the planned change and the read-back. If the object read back does not
  show the grantee's planned row, or the switch is not as planned, the call fails (exit 1). A row that differs
  from the plan for another role, or a row the plan did not write, is reported as a warning. If the read-back
  itself fails, the call warns and asks to check the object with `get-object-rights`.
- Exit code 1 when the object is not found or cannot be read, when the grantee does not exist in `SysAdminUnit`,
  when the plan is refused, and when the save fails. A re-run that changes nothing reports "no change" (exit 0).
- A grantee with more than one row on the object is refused: which of them decides depends on the other rows, so
  the command changes none of them. Remove the duplicates in the Object permissions designer, then re-run.
- A revoke on an object that does not use operation permissions is refused: every internal user reaches it
  whatever its rows say.
- Read-modify-write is last-writer-wins: a change another client saves between the read and the save is
  overwritten. The read-back reports any difference from the plan.
- On MCP, an unknown or misspelled argument name is refused before any read or write (the serializer would
  otherwise drop it silently — e.g. `revok` would bind as a grant).
