# set-default-record-rights

## Command Type

Object rights

## Name

set-default-record-rights - turn record permissions on/off for one object and grant or revoke one of its default
record rules

## Description

Changes the **record layer** of an object's permissions — the "Use record permissions" page of the Object
permissions designer: the switch (`administratedByRecords`) and the object's **default record rules**. A rule says:
records created by members of the **author** role get read / edit / delete rights for the **grantee** role, each at a
level — not set, granted, or granted with the right to delegate. Rules have no order and their rights add up. This is
not `set-record-rights`, which changes the rights of ONE record; operation permissions are `set-object-rights`.

One call changes at most one rule (one author + grantee pair) and/or the switch of ONE object. A call is either:

- a **rule change** — `--author`, `--grantee` and `--operations` together (nothing is granted by default),
  optionally with `--enable-record-permissions`;
- a **switch-only change** — none of them, with `--enable-record-permissions` or `--disable-record-permissions`.

A grant sets each named operation to `--level` (`granted` by default) and leaves the other two as they are; the
output shows every level before → after. A revoke sets the named operations to "not set"; a rule left with no right
is **removed**. A revoke is allowed while record permissions are OFF: it cleans a stored rule before an enable brings
it into effect.

Every transition of the switch is named in the arguments, or the call is refused and nothing is written:

| Refusal | When |
|---|---|
| record permissions are OFF | a grant without `--enable-record-permissions`; the refusal names the stored rules that would come into effect |
| disable with a rule | `--disable-record-permissions` with any rule argument (before any read) — the disable is a call of its own and keeps every rule |
| revoke with a switch flag | `--revoke` with `--enable-record-permissions` (before any read) — a revoke never changes the switch |
| both flags | `--enable-record-permissions` and `--disable-record-permissions` together (before any read) |
| duplicate pairs | the stored list has two rules for one author + grantee pair, and the call would send the list |
| invalid stored level | a stored level outside not set / granted / delegated, and the call would send the list |

Turning record permissions ON with **no rule** applies the built-in default: every user sees only the records they
create (their managers and holders of "view any data" see them too). The output says so.

The command **never applies the rules to existing records.** An enable or a rule change affects records created from
then on; until `apply-default-record-rights` runs, an existing record keeps the record rights it had (after a first
enable it has none). The output reports the number of existing records, counted under the calling account, so the
user can decide whether and when to apply.

It is a read-modify-write over the native `RightManagementService`. The platform **replaces the whole rule list** on
save and checks nothing (a duplicate pair keeps only its last rule, an all-"not set" rule is dropped), so a call that
changes a rule sends every rule — each other one exactly as read — and a call that changes only the switch sends the
list as untouched. The object is read back and compared with the plan: the switch and every rule; any difference
fails the call as "saved, but NOT verified".

**Destructive.** A non-interactive run needs `--confirm`; an interactive run asks. `--preview` writes nothing. On MCP
the host's approval of the call is the confirmation.

## Synopsis

```bash
clio set-default-record-rights --entity-schema-name <Name> [--author <id> --grantee <id> --operations read,edit,delete [--level granted|delegated] [--do-not-apply-for-manager true|false] [--revoke]] [--enable-record-permissions | --disable-record-permissions] [--confirm | --preview] -e <environment>
```

## Options

```bash
--entity-schema-name NAME          The one object whose record permissions change. Required.
--author GUID                      SysAdminUnit id of the rule's author (with --grantee and --operations).
--grantee GUID                     SysAdminUnit id that gets the rights on those records.
--operations LIST                  read, edit, delete. Required for a rule change.
--level granted|delegated          Level a grant sets (default granted). Not with --revoke.
--do-not-apply-for-manager BOOL    Set the rule's "Do not apply for manager" flag. Omitted: kept / false for a new rule. Not with --revoke.
--revoke                           Set the named operations to "not set"; a rule left with no right is removed.
--enable-record-permissions        Turn record permissions ON.
--disable-record-permissions       Turn record permissions OFF (every user with read operation rights reaches every record).
--confirm                          Apply without a prompt.
--preview                          Write nothing; show the plan or the refusal.
-e, --environment NAME             Registered environment.
```

## Examples

```bash
# Turn record permissions on; all employees read and edit records created by any employee
clio set-default-record-rights --entity-schema-name UsrOrder --author a29a3ba5-4b0d-de11-9a51-005056c00008 --grantee a29a3ba5-4b0d-de11-9a51-005056c00008 --operations read,edit --enable-record-permissions --confirm -e production

# "Everyone sees only their own records": turn record permissions on with no rule
clio set-default-record-rights --entity-schema-name UsrOrder --enable-record-permissions --confirm -e production

# Remove a stored rule before an enable (allowed while record permissions are off)
clio set-default-record-rights --entity-schema-name UsrOrder --author <author-id> --grantee <role-id> --operations read,edit,delete --revoke --confirm -e production
```

## Notes

- `author` and `grantee` must exist in `SysAdminUnit`. Common roles: All employees
  `a29a3ba5-4b0d-de11-9a51-005056c00008`, All external users `720b771c-e7a7-4f31-9cfb-52cd21c3739f`, System
  administrators `83a43ebc-f36b-1410-298d-001e8c82bcad`. Portal users see a record only through a rule (author or
  grantee "All external users" or a portal role).
- The save is sent exactly once. A save that got no answer may land after the read-back: re-read with
  `get-object-rights` before retrying.
- A re-run of a call that already landed changes nothing and exits 0.
- Last writer wins on the whole rule list: a rule another client saves between the read and the save is lost; the
  read-back reports it.
- Related: `get-object-rights` (read both layers), `apply-default-record-rights` (existing records),
  `set-record-rights` (one record), `set-object-rights` (operation layer).
