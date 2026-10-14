# ADR: object-rights tools — the arguments describe the whole effect

- **Status:** accepted by the author (2026-09-30); to be confirmed by the PR's approver.
  - Decided: D1 (one object per call), D4 (host approval), D8 (grant and revoke on MCP), `remove-role` as a follow-up.
  - Decided after the self-review (2026-09-30): D5 (no separate opt-in for security/system objects), `operations`
    required on every grant and revoke (D4, invariant 8), R3 (the approval is of `clio-run`), a revoke on an object
    that is not administered stays refused.
  - Decided after the author's independent review (2026-10-01): the save is sent once, with no transport retry; on
    MCP each `get` read is one attempt of at most 30 s and the listing has a 90 s budget.
  - Decided after the manual test run (2026-10-05), superseding the 2026-10-01 rule that accepted
    `disable-operation-permissions` only on a revoke that empties the last granting row: the switch and the rows
    change separately (D9). The disable is a call of its own that keeps every row; the enable can be one too.
  - Decided after the retest (2026-10-05): the object is named by its code, and its title is shown next to it (D10).
  - The facts under "Platform model" were checked on a stand on 2026-09-28/29.
- **Date:** 2026-09-28 (updated 2026-10-05)
- **Jira:** [ENG-99741](https://creatio.atlassian.net/browse/ENG-99741) (related ENG-99969, ENG-100406, ENG-100407)
- **PR:** clio [#1655](https://github.com/Advance-Technologies-Foundation/clio/pull/1655); guidance clio-knowledge #221, #227; toolkit #208

---

## Context

`get-object-rights` and `set-object-rights` read and change object operation permissions: the
`SysEntitySchemaOperationRight` layer, shown as the "Object permissions" grid. They do this with a read-modify-write of
the whole administrated object through `RightManagementService.svc` (`GetAdministratedObject` +
`SaveAdministratedObject`). They are a general capability: read and change the operation permissions of any role on
any object. What a result means for a particular scenario belongs to the guidance for that scenario, not to the tools.

Two properties of the current contract drive this ADR:
- **Effects that the arguments do not show.**
  - A grant can turn operation permissions on for an object. That narrows access for internal users, and can widen
    it for external ones whose stored rows come back.
  - `include-connected` lets the tool choose which objects to write.
  - A revoke can remove a row.

  The operator approves arguments that do not reveal these effects.
- **A model that does not match the platform.** The platform evaluates the rows as a priority list (Platform model
  1). The current code treats them as a union of flags, so its "last grant" and "lockout" checks and its row removal
  on revoke are computed in the wrong model.

This ADR fixes three things the implementation must follow: the platform model, the threat model and the contract.
Reviews are judged against it (see "Review baseline").

## Platform model

1. **The rows are an ordered priority list, not a union of flags.** A row is
   `{sysAdminUnit, position, canRead, canAppend, canEdit, canDelete}`, and position 0 is the highest. Creatio
   Academy: *"A user who is a part of several roles will get the access permissions of the highest role in the
   list."* This has three consequences:
   - An all-false row DENIES its role's members; it is not "nothing".
   - Removing a row changes which lower row applies. With `[0] Sales managers: read` and
     `[1] All employees: read/create/edit/delete`, a sales manager can only read. Remove the first row and the same
     user can do everything.
   - A row below a broader role can be shadowed. With `[0] All employees: read` and
     `[1] Sales managers: read/create/edit`, a sales manager can still only read. Academy says to place the narrower
     role higher.

   **Reproduced on a stand** (kravchuk_0922, 8.3.4.2845, 2026-09-29). The test user was an internal user in a test
   role and in All employees, with no "…any data" system operation. Rows were changed as Supervisor, and each probe
   ran as that user through DataService:

   | Rows, priority 0 → 1 | Read | Create | Highest row wins | Flags add up |
   |---|---|---|---|---|
   | `All employees: RCED` (control) | yes | yes | yes / yes | yes / yes |
   | `role: none`, `All employees: RCED` | **no** | **no** | no / no | yes / yes |
   | `role: R`, `All employees: RCED` | yes | **no** | yes / no | yes / yes |
   | `All employees: R`, `role: RCE` | yes | **no** | yes / no | yes / yes |
   | `All employees: RCED` (the row above removed) | yes | yes | yes / yes | yes / yes |

   - The highest matching row decides, **per row and not per operation**: a `false` in that row is a deny, not "unset".
   - Removing the restricting row widened create back (the last row of the table).
   - A change applies immediately to a session that is already logged in; no new login is needed.
   - DataService reports a denied read as success with **zero rows**, not as an error. A denied create is a
     `SecurityException`. So "the query succeeded" does not prove read access.
2. **The "Use operation permissions" switch** (`administratedByOperations`), checked on the same stand on 2026-09-28.
   - While the switch is off, the designer states three rules:
     - company employees have full access, and the rows are ignored;
     - external users have no access;
     - technical users follow the rows.

     External users are therefore deny-by-default: they reach an object only through an explicit grant.
   - A new object has no stored rows (`SysEntitySchemaOperationRight` is empty for it). `GetAdministratedObject` then
     returns a synthesized `All employees: read/create/edit/delete` row at position 0, with a new row id on every read.
   - A save that turns the switch on stores exactly the rows it sends. If the save keeps the synthesized row, internal
     users keep their access. **The server never adds that row on its own.** Three first-enable saves prove it:
     - rows sent without All employees: only the sent rows remain;
     - rows sent as `null`: no rows at all;
     - rows sent as an empty array: no rows at all.

     In the last two cases the object ends up administered with no rows. Only holders of the "…any data" system
     operations can then reach it (invariant 4). The designer keeps the synthesized row at position 0.
   - Turning the switch off keeps the stored rows. Turning it on again revives them at their positions. In that state
     the read has no synthesized row, and the designer adds no All employees row either: a designer enable can leave
     only the stale rows.
   - A role added in the designer lands at max position + 1, with read/create/edit/delete.
3. **System operations override object permissions.** `CanSelectEverything`, `CanInsertEverything`,
   `CanUpdateEverything` and `CanDeleteEverything` make object rights irrelevant for their holders.
4. **The save has no version token, so the last writer wins.**
   - Collections that did not change are sent as `null`.
   - A schema name resolves to several `SysSchema` rows, and only the base row answers.
   - See the knowledge record `administrated-object-read-modify-write`.
5. **The designer's `SectionService/SetConnectedEntitiesAdministratedByEntity` is not usable.** It grants one fixed
   audience, gives no read-back, and failed server-side on 8.3.4 (a NullReferenceException in
   `VwWorkspaceObjects_CrtBase`).

## Threat model

- **Caller.** An AI agent acting for a developer, through an environment registered in clio, usually with an
  administrator account. That account can already do everything these tools do, through the Object permissions UI.
- **Security boundary.** The boundary is that account's Creatio rights plus the MCP host approval of each
  destructive call. It is the same boundary as for `set-record-rights` and `manage-access`. The tools do not defend
  against a malicious caller: anyone who can reach them can do the same in the UI.
- **What the tools prevent** — mistakes by the agent or the operator:
  - a typo that inverts the change;
  - a change whose targets or side effects the operator did not see in the arguments;
  - a change reported as done when it had no effect, or the opposite effect.
- **Guard-rails are not boundaries.** The security/system name list keeps those objects out of the connected listing,
  the step before granting. A way around it is a Minor defect, not a vulnerability.

## Decision

**Principle: the arguments describe the whole effect.** Every access-changing transition is named in the arguments,
or the call is refused. The host approval then shows the operator everything the call can do.

**D1 — One object per call** *(decided)*.
- `set-object-rights` changes exactly one object, `entity-schema-name`. It no longer resolves lookups, so
  `include-connected` and `connected-operations` are removed from it.
- `include-connected` stays on `get-object-rights` as the discovery step: it lists the object's own lookups.
- To give a role access to an object and its lookups, the guidance works in three steps:
  1. read the lookups with `get-object-rights --include-connected`;
  2. show them to the developer;
  3. make one `set` call per approved object, each with its own operations (for example `read` on a lookup).
- This removes:
  - unseen targets;
  - enumeration failure inside a write;
  - the deny-list as a fan-out filter;
  - the root/connected defaults;
  - the partial fan-out semantics (which object failed, whether to continue).
- *Needs:*
  - AC1 changes to "one call per object".
  - The fan-out ACs are dropped: lookups get READ; a revoke leaves lookups untouched; a failed root write stops the
    fan-out; connected objects are re-resolved on every call.

**D2 — Turning operation permissions on is explicit.**
- A grant on an object that is not administered is refused unless `enable-operation-permissions` is given. The refusal
  names the existing rows that would become effective. The flag can also be passed alone (D9).
- With the flag, the save keeps the synthesized `All employees` row that the read returns for an object with no
  stored rows.
- When the object has stale rows and no `All employees` row, the same save adds one, as the current implementation
  does. That is deliberately safer than the designer, which adds none (Platform model 2).
- The added row goes below the stale rows (a new grantee row goes below it), so no row is renumbered (invariant 3). The stale rows above
  it then decide for their members (Platform model 1): a restriction for internal members, and possibly a grant for
  external ones. So the refusal and the preview name each of them as a row that starts to decide.
- Each call names its one object, so enabling operation permissions on a shared lookup is its own call, with the flag
  visible in the arguments.
- This removes the implicit enable (a narrowing for internal users, a possible widening for external ones) and the
  lockout detection after the write.

**D3 — Positions are part of the model; rows are never removed implicitly.**
- `RoleOperationRights` carries `Position`. `get-object-rights` lists rows in priority order, with the position, and
  states the priority rule.
- A revoke clears flags on the grantee's row and keeps the row: an all-false row is an explicit deny. In this PR a row
  is never removed; an explicit `remove-role` action is a follow-up.
- A new grant row goes at the lowest priority, as in the designer (Platform model 2). The result names the rows above
  it that can shadow it for users who are in both roles.
- The tools state no conclusion about one user's effective access, such as "locked out" or "last grant", from flags
  alone. The only structural check kept is invariant 4, which holds whatever the priority rule is.
- *Required by the existing AC* "A revoke never widens access". Removing an emptied row that sits above a broader
  role widens access (the last row of the table in Platform model 1), so the current implicit row removal violates
  this AC.

**D4 — On MCP, the confirmation is the host approval** *(decided)*.
- The tool applies the change in one call (`Confirm = true`), like `set-record-rights` and `manage-access`. The host
  asks the operator to approve the call and shows its arguments.
- After D1–D3 the arguments show the whole effect: one object, one role, the operations, and whether permissions are
  turned on or off.
- `operations` is required on every grant and revoke (a call that only turns the switch, D9, names none). Nothing is
  granted by default, so the approval shows
  exactly what is granted or taken away. A default of read/create/edit would be invisible in the approval, and wrong
  for the main consumer, which grants read only.
- The two-step preview + confirmation-code protocol is removed.
  - It existed to show targets and side effects that the arguments hid.
  - The code does not prove that the operator said yes. It only proves that nothing changed between the two calls,
    and an agent can make both calls without asking anyone.
- A read-only `preview` (a dry run) stays on the CLI and MCP. It is rendered from the same plan as the write (D6) and
  carries no code.
- ENG-99969 (a shared destructive-confirmation gate) builds on this.

**D5 — No separate opt-in for security and system objects on `set`.**
- Each call names its one object (D1), so a change to a security or system object (`SysAdmin*`, `SysSettings*`, …) is
  named in the arguments the host shows, as it is in the designer. The boundary is the caller's Creatio rights
  (Threat model).
- An opt-in flag for such objects (`allow-security-object`) existed while `set` fanned out to lookups, where one could be
  written without being named. With D1 it only added rules of its own (for a grant beyond read, for the All employees
  row an enable adds, for the stale rows an enable revives, for a disable), so it was removed.
- `get-object-rights --include-connected` still does not list security and system objects as connected objects, so the
  discovery step never offers them for a grant. The names are normalised before that filter, as for a named object.

**D6 — The internal flow is read → plan → policy → apply → verify.**
- `Plan(before, request) → (after, transitions)` is a pure function with no HTTP. It computes the full row list
  after the change, positions included.
- Policy is one table from transition to required flag. It is evaluated before the write, and a refusal writes
  nothing.
- `RightManagementServiceClient` only reads and saves; it holds no policy.
- Each call does one read before the write and one read-back after it. The save sends exactly `after`: the rows the
  plan changes or adds are written, found by grantee and position, and every other row is sent as it was read. The
  read-back is compared with `after`, row by row. A difference, such as a row the server added, is reported as a
  fact.
- Output, exit code and the dry run are all rendered from the plan, so they cannot diverge from the write.

**D7 — Duplicate rows for one grantee are refused** as needing explicit repair, as `manage-access` does. The tool
does not edit several rows whose positions give them different effects.

**D8 — MCP `set` offers grant and revoke** *(decided)*, with the D1–D7 semantics, plus the explicit enable and
disable flags.

**D9 — The switch and the rows change separately** *(decided 2026-10-05, after the manual test run)*.
- `disable-operation-permissions` is a call of its own: the object name and the flag, no grantee, operations or
  revoke. It turns the switch off and keeps every row exactly as it is, operations included, as the designer's switch
  does, so turning it back on restores the same access. Already off: no change.
- `enable-operation-permissions` can be a call of its own too: it turns the switch on with the stored rows as they
  are, under the D2 rule for the `All employees` row, and is refused when no row would grant anything (invariant 4).
  Already on: no change. With a grant it stays as D2 describes, so the portal flow is still one call.
- A revoke never turns the switch off. A revoke that would leave no granting row is refused, and the refusal names
  the disable call.
- *Why.* The disable used to ride on a revoke, the explicit replacement for the old "the last revoke turns it off".
  Two rules were tried and both failed:
  - a disable on any revoke opened the object to every internal user while the call read as a revoke;
  - a disable only on the revoke that empties the last granting row (decided 2026-10-01) meant that turning the
    switch off took one revoke per granting row and emptied every row. Turned back on, such an object denied
    everyone. A manual test on `Activity` (rows for All external users and All employees) needed two calls for what
    the designer does with one switch.

  Both came from tying the switch to a revoke. A disable that names only the switch reads exactly as what it does,
  and keeps the rows.
- *Consequences.* While the switch is off, external users have no access whatever the rows say, so a disable closes
  the object to the external roles its rows grant; the guidance has the agent name those rows before a disable, as it
  does for an enable. Technical users keep following the rows. Emptying the last granting row is not possible with
  the tool, before or after a disable, because a revoke on an object that is off is refused; the designer can do it.

**D10 — The object is named by its code; its title is shown next to it** *(decided 2026-10-05, after the retest)*.
- `entity-schema-name` is the object's code (entity schema name), and the code always wins: an object with that code
  is the one read or written.
- The output shows the object's title next to its code — `'Creatio functionality' (Feature)` — whenever the two
  differ: `get` for every object it lists, `set` in its preview, its result and its refusals.
- `get` reads by title when no object has the code: the one object with that title is read, and the output says so.
  Several objects with the title are refused, listed as `'title' (code: name)`, the form the process tools use for
  the candidates of a caption. A name that is not a schema identifier is only ever looked up as a title.
- `set` refuses a title and names the code(s) it belongs to, as `run-process` refuses a process caption: the approval
  shows the code, and a title is not unique.
- The `set-object-rights` `[Description]` tells the agent that the object is named by its code and to name it to the
  developer by code and title before a write.
- *Why.* In the retest the developer asked for rights on "object Feature". The agent passed "Feature" as the code and
  changed schema `Feature`, titled "Creatio functionality", while the object titled "Feature" is schema
  `Specification`, and said nothing: it never opened the guidance, whose rule covers this, and the output showed the
  code only. A rule that must hold cannot live in the guidance alone. The title in the output and the rule in the
  description reach an agent that reads only the contract.
- *Not done.* A word that is one object's code and another object's title is not flagged: the code wins silently, as
  in the process tools. No clio tool reports such a namesake, and it would take a title lookup on every call; the
  title in the output already shows the developer which object the code named.

## Responsibilities: tool vs guidance

The tools work like the designer, one object at a time, plus a few guard-rails that the designer does not have. They
report facts. The guidance explains what the facts mean and decides what to do.

**The tool:**
- makes exactly the one change that the arguments name, on one object, which it names by its code and its title (D10);
- refuses a risky transition that the arguments do not name:
  - enabling or disabling operation permissions without its flag — the switch changes only through its flag, and a
    disable is a call of its own that changes no row (D9);
  - duplicate rows for the grantee;
  - a change that would leave an administered object with no granting row — a revoke that empties the last granting
    row, or an enable over rows that grant nothing;
  - a revoke on an object that is not administered, which company employees reach whatever its rows say;
- never removes a row and never reorders rows;
- reports facts:
  - rows in priority order, with their positions (`get`; with `--include-connected`, also for the object's own
    lookups);
  - the rows above a new grant that can shadow it;
  - the stale rows that become effective on enable;
- draws no conclusion about who can actually reach the object.

**The guidance** (the `object-rights` article, and each scenario's guidance built on it):
- decides which objects, roles and operations a scenario needs, and asks the developer before each write;
- explains the consequences the agent must weigh:
  - the priority rule: the highest matching row decides, per row, and a `false` is a deny; a lower row can be
    shadowed;
  - turning operation permissions on narrows access for internal users and can widen it for external ones, because
    the stale rows that come back can grant them; the synthesized All employees row;
  - turning them off is the mirror: it opens the object to every internal user and closes it to the external roles
    its rows grant, while the rows are kept for a later enable;
  - external users are deny-by-default and need an explicit grant;
  - the "…any data" system operations override object permissions;
  - `read` on a shared lookup (Contact, Account and the like) is read on the whole table for that role;
- explains how to verify:
  - re-read with `get-object-rights`;
  - a denied DataService read returns success with zero rows, not an error;
  - a change applies to logged-in sessions immediately.

## Alternatives considered

| Alternative | Verdict |
|---|---|
| Keep the current contract and fix findings one by one | Rejected. The effect depends on hidden object state, so every added guard adds combinations to check, and the union-of-flags model stays wrong. |
| Keep the tool-side fan-out and the confirmation code; fix the model only (D3 + D6) | Partial. Correctness is fixed, but the targets stay outside the arguments. The two-step protocol, the enumeration failure mode and the deny-list as a filter all remain. |
| An explicit `connected-objects` list in one `set` call | Rejected by the author in favour of one object per call, which is the simpler contract. |
| Compute per-user effective access (priority + role membership via `SysAdminUnitInRole`, nested roles) | Rejected for this PR. It is expensive and no consumer needs it. It is a possible follow-up if "who can actually reach this object" becomes a requirement. |
| Designer's `SetConnectedEntitiesAdministratedByEntity` | Rejected: fixed audience, no read-back, fails on 8.3.4 (Platform model 5). |
| A scenario-level tool (one call per business scenario) | Rejected. The ticket asks for a reusable capability for any role; scenario logic belongs to the guidance of each scenario. |

## Consequences

- **`set` contract.**
  - Removed: `include-connected`, `connected-operations`, `confirmation-code` and the two-step MCP flow, implicit
    enable, implicit row removal, the lockout detection after the write, `allow-security-object` (D5) and the default
    operations.
  - Added: `enable-operation-permissions`, `operations` required on every grant and revoke, and positions in the
    `get` and `set` output.
  - D9: `disable-operation-permissions` is a call of its own that keeps every row, `enable-operation-permissions` can
    be one too, and a revoke never changes the switch. On a security or system object (no opt-in, D5) the disable is
    one explicit call as well: its name and the flag are in the arguments the host shows, and the guidance never
    proposes such an object.
  - D10: the output shows the object's title next to its code; `set` refuses a title, naming the code, and `get` reads
    by title only when no object has the code.
  - `preview` stays as a dry run.
- **Jira ENG-99741 ACs** need a revision:
  - AC1 becomes one call per object;
  - the fan-out ACs are dropped (D1);
  - new safe-by-default bullets: "a grant never turns operation permissions on implicitly" (D2) and "rows are never
    removed implicitly" (D3).
- **Guidance.** Every guidance article that calls `set-object-rights` changes the same way: clio-knowledge #221
  (`object-rights`), #227, and toolkit #208. The flow becomes three steps:
  1. read the lookups with `get --include-connected`;
  2. ask the developer;
  3. make one `set` call per approved object.

  With N lookups that is N + 1 calls, and N + 1 approvals. The `object-rights` article also gains the revoke semantics
  (the row is kept) and the priority rule.
- **Code and tests.**
  - Removed: the fan-out loop, the connected-object resolution in `set`, the root/connected branches, the preview and
    confirmation-code machinery, the reporting of state nobody planned, and the policy inside the client.
  - Added: a pure planner and a policy table. Most command-level mock tests become table-driven planner and policy
    tests.
- **Follow-up.** An explicit `remove-role` (removing a role's row) is a separate task.
- **Knowledge records and comments.**
  - `administrated-object-read-modify-write` gains the priority rule. Its claim that the server adds All employees on
    save is replaced with Platform model 2.
  - The `KeepInternalAudience` comment ("Creatio 8.3.4 adds that row itself") is corrected the same way.

### Accepted risks

- **R1 — Last writer wins.** The save carries no version, so a change saved between our read and our save is lost.
  This is the same read-to-save gap as in other clio write tools. Concurrent edits are rare and admin-only, and this
  is documented.
- **R2 — The name list is incomplete by design.** It filters the connected listing only; it is not a boundary.
- **R3 — The host approval is only as strong as the host.** Auto-approve modes skip it, as they do for every
  destructive clio MCP tool. The host approves the `clio-run` call that carries `set-object-rights` and its
  arguments, and every destructive long-tail tool (`odata-delete`, `set-record-rights`, `delete-schema`, …) is reached
  the same way. So "always allow clio-run" removes the approval for all of them at once, not only for this tool. A
  split into a safe `clio-run` and a `clio-run-destructive` was tried and reverted (6fc54cc37, 2026-06-19: models
  looped on the redirect and never acted; see the lazy-schema ADR), so this is an accepted platform risk, and this
  tool does not add to it.
- **R4 — Live destructive behaviour is not in CI.** It is covered by the opt-in Sandbox e2e (Explicit) and the
  read-only Sandbox e2e. There is no destructive e2e in CI.
- **R5 — Shadowing is reported, not prevented.** A grant row below a broader row may have no effect for users who are
  in both roles. The tool names the rows above it and never reorders rows.

## Review baseline

Reviews of this feature are judged against this ADR:
- A finding that contradicts a decision above is a proposal to revisit the ADR, raised on the ADR. It is not a defect
  of the implementation.
- A finding that asks the tool to explain consequences, or to decide for a scenario, belongs to the guidance (see
  "Responsibilities"), not to the tool.
- Blocker or Major is reserved for a violated invariant below, a violated acceptance criterion, data loss, or a
  breach of the security boundary. A way around a guard-rail is Minor. A test or doc gap is Minor unless it leaves an
  invariant untested.
- Fix commits get a scoped review of their own diff (AGENTS.md gate 2). The comprehensive review runs once more,
  before draft is cleared (gate 3).

Invariants:
1. A call writes only the object named in its arguments.
2. `administratedByOperations` changes only with its explicit flag (enable or disable). A disable is a call of its own
   that changes no row, and a revoke never changes the switch (D9).
3. A row is created only as the arguments say, a row is never removed, and existing rows never change position.
4. An administered object never ends up without a row that grants some operation: a revoke or an enable that would
   leave it so is refused.
5. Policy is decided before the write, and a refused call writes nothing.
6. Output and exit code come from the plan and the read-back. A change that was not saved is never reported as done.
7. Unknown argument names are refused before any read or write.
8. A call grants or revokes only the operations it names; no operation is implied by default.
9. The object a write changes is named by its code: a title is refused, naming the code it belongs to (D10).

## Open questions

None. All decisions are made.
