# ADR: set-object-rights binds the object's operation permission rows to a package

- **Status:** accepted by the author (2026-11-08); to be confirmed by the PR's approver.
  - Decided with the operator (2026-11-08): a removed row is deleted from the database and taken out of the binding,
    like the Object permissions designer deletes it; there is no install-time delete script. The ticket's
    "a removed row must not come back after the package is installed again" therefore holds for a clean install
    only, and the result says so.
- **Date:** 2026-11-08
- **Jira:** [ENG-105290](https://creatio.atlassian.net/browse/ENG-105290) (follows ENG-99741 and ENG-104066)

## Context

`set-object-rights` writes `SysEntitySchemaOperationRight` rows into the database of one site. Nothing binds them to a
package, so an app installed on another site arrives with default permissions. Platform facts read from the core
source are recorded in
[`package-install-never-deletes-rows-missing-from-a-binding`](../../docs/knowledge/platform/package-install-never-deletes-rows-missing-from-a-binding.md):
install upserts bound rows by key, copies only `IsForceUpdate` columns onto an existing row, and never deletes a row
the binding does not carry.

## Decisions

**D1 — One binding per object, re-saved after every successful call.** `SysEntitySchemaOperationRight_<object code>`
holds exactly the rows stored for the object (read by `SubjectSchemaUId` after the save), ordered by `Position`. A
call that changes no row re-saves it as well, so a binding that a failed call left stale is repaired by running the
same call again. An object with no stored rows has its binding deleted; an empty binding is never saved.

**D2 — Keyed on `Id`; every other column (operations, position, role, object) force-updated.** The rows are created on the source
site, so their ids travel with them. Force-update makes a later install carry a changed row onto a site that already
has it. *Rejected:* a natural key (`SubjectSchemaUId` + `SysAdminUnit`) — a role can have several rows, which the
key could not tell apart.

**D3 — The package is decided before any write.** `--package`, else the package of the object's base schema
(`SysSchema`, `ExtendParent = false`). It is resolved through `IPackageTargetResolver` with `requireEditable`; a
missing or locked package refuses the call with nothing written and names the `CurrentPackageId` package when that
one is editable.

**D3a — The object's own binding stays where it is.** Without `--package`, a
`SysEntitySchemaOperationRight_<code>` binding that an earlier call wrote into another editable package is the
target, so a later change never leaves a stale copy behind. With `--package` naming another package, the call is
refused before any write, naming the package that holds the binding (pass it, or remove that binding first): saving
the rows while that binding keeps the previous ones would ship rights the site no longer has (PR #1908 review). A
preview never fails on a refused package: it shows the refusal and exits 0. A call that changes no row writes the binding only when it is confirmed.

**D4 — A binding found elsewhere is reported, not duplicated.** Bindings of `SysEntitySchemaOperationRight` in
editable packages are read; one that holds a row of this object (by `SubjectSchemaUId` or by row id) under another
name or package is named in the result and no binding is written. A binding named exactly
`SysEntitySchemaOperationRight_<code>` for another entity schema that exists (one batched `SysSchema` read) belongs to
that object and is not read row by row; every other name, a prefixed one whose suffix is no object included, is read
row by row, and locked packages are not searched: each read costs a request, and the caller's own bindings live in
editable packages. Those reads share the call's request budget, and they are skipped once the
target package holds the object's own binding: the first bind already ran the check.

**D5 — A failed binding after a saved change fails the call.** The rows stay saved; the error says they are not bound
and that re-running the same call binds them (D1). The binding requests are not under the MCP call budget.

**D6 — The result states what does not travel.** Whether the object's schema (and with it the "Use operation
permissions" switch) is in the binding's package; that rows decide nothing while the switch is OFF; every role other
than All employees and All external users, which must exist with the same id on the target; and, after a removal,
that a site which already installed the row keeps it.

## Consequences

- A product object in a locked package can no longer be changed without `--package` naming an editable package.
- In file system development mode the binding reaches the database only; `PackageDataBindingWriter` warns and names
  `pkg-to-file-system`.
