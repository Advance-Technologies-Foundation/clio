# ADR: set-object-rights moves and removes a role's row

- **Status:** accepted by the author (2026-10-25); to be confirmed by the PR's approver.
  - Decided with the operator (2026-10-24): rows left after a move or a removal are renumbered 0..n-1, as the
    designer does; the effect is computed per role against the universal row (below).
  - `position` past the last row is refused (the operator was asked; the recommended default was kept).
  - Decided with the operator (2026-10-27): no `--below` (D6); the ticket's acceptance criteria are amended to match.
  - Decided with the operator (2026-10-27): when a logged-in user gets the new decision is the platform's rights
    caching, which clio does not control — the change can reach them at once, but reliably only after they log in
    again — so the ticket's "without re-login" criterion is dropped rather than verified. The server side was
    probed on 8.3.4 (a DataService session got the change at once); the application side is operator experience
    (2026-10-28).
- **Date:** 2026-10-25
- **Jira:** [ENG-104066](https://creatio.atlassian.net/browse/ENG-104066) (follows ENG-99741, the `remove-role`
  follow-up named in [adr-ENG-99741-object-rights.md](adr-ENG-99741-object-rights.md) D3)
- **Amends:** [adr-ENG-99741-object-rights.md](adr-ENG-99741-object-rights.md) invariant 3, its Responsibilities
  bullet "never removes a row and never reorders rows" and R5 ("never reorders rows"): a grant or a revoke still
  never removes or reorders a row; a row moves or goes only through a layout call of its own (D1). Where the two
  ADRs differ on rows moving or going, this ADR wins (see "Review baseline"). It also replaces ADR-99741's "a
  change applies immediately to a session that is already logged in": the change can reach a logged-in user at
  once, but for the permissions to apply reliably the user logs in to the site again (operator, 2026-10-28).

---

## Context

`set-object-rights` (ENG-99741) never moves or removes a row: a grant adds the role's row at the lowest
priority, and a revoke keeps the row as a deny. A row placed below `All employees` is shadowed for every internal
user, so a restriction such as "technicians cannot delete" could not be finished from clio. ENG-99741 D3 kept
positions in the model and left an explicit row removal as a follow-up; this ADR is that follow-up, plus the move.

Platform facts, probed on stand `sae_m_seeenu_16229277_1024` (2026-10-25) and read from the designer's source
(`creatio-ui` `rights-by-operations-detail.component.ts`), recorded in
[`object-operation-rows-save-replaces-the-list`](../../docs/knowledge/platform/object-operation-rows-save-replaces-the-list.md):
- `SaveAdministratedObject` replaces the whole row list: a row left out is deleted, a row sent with its `id` at a
  new position is moved.
- The designer renumbers every row 0..n-1 after a drag or a delete.
- `manage-access operation-position` moves a SYSTEM-operation grant through `RightsService` (cliogate); it is a
  different table and service, so it is not reused here. The object save is the same path grants already use.

## Decisions

**D1 — A move or a removal is a call of its own.** `--grantee` with exactly one of `--position`, `--above`,
`--remove-row`; any of them with `--operations`, `--revoke` or a switch flag is refused before any read.
One call has one before and one after, so the approval shows the whole effect (ENG-99741's rule that the arguments
describe the whole effect).

**D2 — The rows are renumbered like the designer.** After a move or a removal the rows are numbered 0..n-1 in their
new order; the other rows keep their relative order. A move that leaves the order unchanged writes nothing, even
over gapped positions: renumbering alone would rewrite rows nobody named. *Rejected:* keeping the other rows'
numbers — a shape the designer never sends, with no stand evidence that every reader of the table tolerates it.

**D3 — A separate layout save.** `IObjectRightsWriter.SaveRowLayout` rebuilds the row array from the READ nodes in
the planned order (matching a planned row to a read row by grantee and operations, in read order) and replaces only
`position`. The row save (`Save`) matches by grantee AND position and never drops a node; teaching it to reorder
would weaken that guarantee for grants. A layout plan sets `RewritesAllRows`, and the read-back then treats every
row as written: any difference fails the call ("saved, but NOT verified" / "does not match the plan").

**D4 — Refusals.** No row for the grantee; several rows for the grantee (as a grant); `--above` naming a role
with no row or several; a position past the last row; a removal that leaves an administered object with no
granting row (the revoke rule). While operation permissions are OFF a move or a removal is allowed and says that
the rows decide nothing until they are turned on.

**D5 — The effect is named per role, for a member in no other role with a row.** Such a member is decided by the
higher of the role's own row and its universal row — `All employees` for an internal role, `All external users` for
an external one (`SysAdminUnit.ConnectionType = 1`, one DataService query for every grantee on the object). Every
role whose operations change is listed with old and new operations and the deciding row before and after, external
roles named as such and any operation they lose (a narrowing, or the lost part of a change both ways) stated as a
revoke of external access; a removal always names the row
its members fall to. *Rejected:* following the role hierarchy (parent org roles) — more queries and more ways to be
wrong; the output states the limit instead. A failed external lookup fails the call before the save.

**D6 — `position` and `above`, no `below`.** `--above` exists so that clio, not the caller, turns "decide before
role Y" into an index: it is computed from the rows read in the same call, so it still names the right place when the
rows changed since the caller read them, and it spares the caller the off-by-one of a final index (a row moving up
takes the anchor's position, one moving down does not). That is the main case — a narrower role's row moved above a
row that shadows it, which `get-object-rights --grantee` points at. `--position` covers the rest: the top, the bottom
and any exact place. *Rejected:* `--below` (asked for by the ticket) — rows are moved down rarely, and every place it
names is reachable with `--position` or with `--above` the next row, so it would only add an exclusive option, its
refusals and its tests; `--position` alone — complete, but leaves the index arithmetic to the caller, checked only if
the caller previews.

## Consequences

- On MCP a move or a removal makes six requests instead of five; the existing 100 s budget and the "save only while
  two timeouts are left" rule are unchanged.
- The object-rights guidance (clio-knowledge) documents both operations and drops "done in the designer" for them.

## Review baseline

Reviews of `set-object-rights` are judged against
[adr-ENG-99741-object-rights.md](adr-ENG-99741-object-rights.md) "Review baseline", with its invariants 3 and 4
replaced by the ones below; its severity rules apply unchanged. Invariants this ADR adds or replaces:
- **3 (replaces ENG-99741 invariant 3).** A grant or a revoke creates a row only as the arguments say, and never
  removes a row or changes an existing row's position. A row moves or is removed only by a layout call (D1); the
  rows it does not name keep their relative order and are renumbered 0..n-1 (D2), and a move that leaves the order
  unchanged writes nothing.
- **4 (replaces ENG-99741 invariant 4).** An administered object never ends up without a row that grants some
  operation: a revoke, an enable or a removal that would leave it so is refused (D4).
- **10.** A layout call takes `--grantee` and exactly one of `--position`, `--above`, `--remove-row`, and nothing
  else that changes rows or the switch: `--operations`, `--revoke` or a switch flag with it is refused before any
  read (D1).
- **11.** A layout save is verified against the whole row list: the read-back treats every row as written, and any
  difference from the planned rows — a row missing, extra, or at another position — fails the call (D3).
