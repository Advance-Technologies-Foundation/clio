# ADR: default record rights — the record layer of object permissions

- **Status:** accepted by the author (2026-10-03); implemented on the same branch.
  - Decided (2026-10-03): ADR-Q2 — `RevokeOnNotAdministered` is dropped; a revoke while the switch is off is allowed
    (RD4).
  - Decided (2026-10-03): ADR-Q1 — a call is a rule change or a switch-only change (RD2); AC3 reads "operations are
    required for a rule change". The built-in default with no rules (platform model 5a) is stated in the output and
    the guidance.
- **Date:** 2026-10-03
- **Jira:** [ENG-100406](https://creatio.atlassian.net/browse/ENG-100406) (related ENG-99741, ENG-98669, ENG-102029)
- **Extends:** [adr-ENG-99741-object-rights.md](adr-ENG-99741-object-rights.md). Its principle, threat model, D4
  (host approval), D6 (read → plan → policy → apply → verify), accepted risks R1–R4 and review baseline apply here
  unchanged. This ADR states only what is different for the record layer.

---

## Context

The System Designer "Object permissions" page has three layers. ENG-99741 covers the operation layer
(`administratedByOperations` + `entitySchemaOperationsRights`). This ADR covers the record layer: the "Use record
permissions" switch (`administratedByRecords`) and the object's default record rules
(`entitySchemaRecordDefRights`): "records created by author role X get read / edit / delete rights for grantee role
Y". It also covers applying the current rules to records that already exist.

The same service pair is used: `RightManagementService.svc` `GetAdministratedObject` / `SaveAdministratedObject`.
Per-record rights (`get-record-rights` / `set-record-rights`, `Sys<Entity>Right`) are a different, existing surface
and are not changed. Package persistence of either layer is ENG-102029.

## Platform model

Checked on a 10.2 stand (core 10.2.367) on 2026-10-02. Each fact is the basis of a decision below.

1. **A rule** is `{authorSysAdminUnit, granteeSysAdminUnit, readRightLevel, editRightLevel, deleteRightLevel,
   doNotApplyForManager}`. A level is 0 (not set), 1 (granted) or 2 (granted with the right to delegate). A rule has
   no id and no position, and the order of rules has no meaning: rights from several rules add up (Academy). The
   identity of a rule is its (author, grantee) pair.
2. **The save replaces the whole rule list.** A rule missing from `entitySchemaRecordDefRights` is deleted; `null`
   leaves the list untouched.
3. **The server validates nothing and loses data silently**, always answering `success: true`:
   - a rule with all three levels 0 is dropped;
   - for two rules with the same pair, the last one wins and the first is lost;
   - a level outside 0..2 is stored as is.
4. **Turning the switch on** adds no rule and gives existing records no rights rows. Until the rules are applied
   (model 7), an existing record is reachable only by its author/owner, their managers and holders of "view any data"
   (Academy).
5. **Rules apply to new records on insert.** A level 0 creates no rights row; the author always gets a full right.
   - **5a. The built-in default when no rule exists:** with the switch on and NO rule, every user sees only the
     records they created themselves (the author right), plus what their managers and "view any data" holders see
     (Academy). So an enable alone is not "nothing changes": it is the "own records only" access model. This is the
     answer to "what happens if I just turn it on".
6. **Turning the switch off keeps everything**: the rules and the existing rights rows stay, and come back into effect
   when the switch is turned on again. A record inserted while the switch is off gets no rights rows.
7. **Applying the rules to existing records** is the process `ObjectRecordRightsActualizationProcess` (input
   `EntitySchemaUId`), started through `ProcessEngineService.svc/RunProcess`. It returns at once with `processId` and
   status Running and keeps running; its end is visible in `SysProcessLog(<processId>).StatusId` (Completed / Error /
   Canceled). It deletes the rights rows that came from rules and adds rows for the current rules; manual grants stay.
   Academy calls it resource-intensive ("3 minutes or more"). The Freedom UI never runs it on its own: it asks the
   user after a save.
8. **`recordRightsDenied`** (10.2 only; absent on 8.3.4) has no control on the page and is ignored by the save. It is
   a legacy property: sent back as read, and never used otherwise.
9. **The switch and the rules are environment state, not package content** (the service writes the switch to a
   user-level overlay on 10.2; rules are data). ENG-102029 handles persistence.

### How the record layer differs from the operation layer

| | Operation layer (ENG-99741) | Record layer (this ADR) |
|---|---|---|
| Row identity | grantee + position | author + grantee |
| Order | priority list, highest row decides | none, rights add up |
| A removed row | can widen access (a lower row starts to decide) | only narrows access |
| An all-false row | an explicit deny | cannot exist (the server drops it) |
| What the save sends | only the changed rows; the rest as read | the WHOLE list, every rule |
| Effect of a change | immediate | new records only, until the rules are applied |

So ENG-99741 D3 ("rows are never removed") and invariant 3 do not carry over: here removing a rule is the only way to
revoke its last operation, and it cannot widen access.

## Decision

**Principle (unchanged): the arguments describe the whole effect.** One object per call; every transition that
changes who can reach records is named in the arguments, or the call is refused.

**RD1 — Read: `get-object-rights` is extended, no new read tool.** The same `GetAdministratedObject` call already
returns the record layer. The output adds, after the operation rows (which stay unchanged):
- `record permissions: on|off`;
- every rule: author → grantee, read / edit / delete level, `doNotApplyForManager`;
- when the switch is off and rules exist: they are listed as "stored, not in effect while record permissions are
  off";
- the number of existing records of the object (RD6), for the named object only, never for `--include-connected`
  objects;
- filters: `--grantee` (existing) also filters rules by grantee; a new `--author` filters rules by author.

**RD2 — Write: a new tool `set-default-record-rights`, one rule and/or the switch of one object per call.**
- Args: `environment-name`, `entity-schema-name`, `author`, `grantee`, `operations` (read|edit|delete), `level`
  (granted|delegated, default granted), `do-not-apply-for-manager` (optional bool), `revoke`,
  `enable-record-permissions`, `disable-record-permissions`, `preview`.
- **A call is either a rule change or a switch-only change.** A rule change names `author`, `grantee` and
  `operations` together; any of the three alone is refused before any read. `operations` is never defaulted. A
  switch-only call names none of the three and must carry `enable-record-permissions` or
  `disable-record-permissions`. A call with neither a rule nor a switch flag is refused.
- `author` and `grantee` are SysAdminUnit ids, each checked to exist (the ENG-99741 grantee lookup) before the write.
- **Grant** sets each named operation to `level` and leaves the other two as read. It can therefore lower a
  delegated operation to granted; the output shows every level as before → after, so that is never silent.
- **Revoke** sets the named operations to 0. When all three become 0 the rule is removed from the list (platform
  model 3 would drop it anyway), and the output says "rule removed".
- **`do-not-apply-for-manager`**: when given, sets the rule's flag; when omitted, an existing rule keeps its value and
  a new rule gets `false` (the page's default). To change only the flag, the call repeats the rule's current
  operations and level.
- The save sends: the snapshot as read, with `administratedByRecords` as planned and `entitySchemaRecordDefRights` =
  the FULL planned list (every other rule exactly as read) when the plan changes a rule, or `null` ("leave untouched")
  when it changes only the switch — a switch-only call never re-sends a rule. `entitySchemaOperationsRights`,
  `entitySchemaColumnsRights` and `entityOperationGrantees` are `null`; `administratedByOperations`,
  `administratedByColumns` and `recordRightsDenied` (when present) are sent as read. The existing operation save keeps
  sending `entitySchemaRecordDefRights: null`.
- Destructive and idempotent: `--confirm` on the CLI, host approval on MCP (ENG-99741 D4). A call whose plan changes
  nothing reports "no change" and does not save. `preview` is a dry run rendered from the same plan.

- Review follow-up (2026-10-05, RC-9): kept as is. A grant sets the named operations to `level` (default granted),
  which can lower delegated; the preview, which the guidance requires before every write, shows it as before → after.
  No extra rule for an omitted `level`.

**RD3 — The switch changes only with its flag; both directions are allowed when named.**
- A grant on an object whose switch is off is refused without `enable-record-permissions`. The refusal names the
  stored rules that would come into effect.
- `enable-record-permissions` alone is allowed, also on an object with no rules. With no rules the built-in default
  applies (platform model 5a): everyone sees only the records they create. That is a legitimate "own records only"
  setup, so it is not refused; the output states it explicitly when the object has no rules, together with platform
  model 4 (existing records get no rights until they are applied) and the record count.
- `disable-record-permissions` alone is allowed: the call names it, so the approval shows that every user with read
  operation rights will reach every record. The output states platform model 6.
- `disable-record-permissions` together with a grant is refused (`DisableNotNeeded`): the grant would have no effect.
  Together with a revoke it is allowed.
- Both flags together are refused (`ContradictoryFlags`).

**RD4 — Policy table.** Evaluated after the read and before the write; a refusal writes nothing.

| Refusal | When |
|---|---|
| `EnableNotRequested` | grant on an object whose switch is off, without `enable-record-permissions` |
| `DisableNotNeeded` | `disable-record-permissions` together with a grant |
| `ContradictoryFlags` | `enable-record-permissions` and `disable-record-permissions` together (refused before the read) |
| `DuplicatePairs` | the stored list already has two rules for one (author, grantee) pair, any pair (platform model 3) |
| `InvalidStoredLevel` | a stored level outside 0..2, any rule (platform model 3) |

**No refusal for a revoke while the switch is off** (decided 2026-10-03, ADR-Q2; the task's `RevokeOnNotAdministered`
is dropped). On the operation layer such a revoke changes nothing for employees, so it is refused there. Here it removes
or narrows a stored rule that would come back into effect on the next enable (platform model 6): it is the clean-up
step BEFORE an enable, and it changes nobody's access now. The output says so: "stored rule changed/removed; record
permissions are off, so access did not change now". A grant while off still needs `enable-record-permissions`
(`EnableNotRequested`): there the caller wants access to change, and without the enable it would not.

`DuplicatePairs` and `InvalidStoredLevel` are checked on the WHOLE list, not only on the target rule, whenever the call
would send the list (it changes a rule): the save sends every rule back, so a bad rule anywhere would be resent and
lose data again. A switch-only call sends the list as `null` and is not refused for it. They need an explicit repair in the
designer; the tool does not choose which duplicate wins.

**RD5 — Read-back.** After the save the object is read again and compared with the plan: the switch, and the rules as
an unordered set keyed by (author, grantee), every field included. Any difference — the target rule, or ANY other
rule — fails the call as "saved, but NOT verified" (exit 1) and names the difference. The ENG-99741 infrastructure is
reused unchanged: one call deadline (`RequestDeadline`), the save sent once (`MaxAttempts = 1`), `OutcomeUnknown` on
a timeout or transport fault ("may still be applied, re-read"), redaction.

**RD6 — Record count.** `get-object-rights` and every `set-default-record-rights` result that enables the switch or
changes a rule report the number of existing records, read with one DataService count query under the caller's
account. It is the fact the user needs to decide on RD7. A failed count is reported as "not counted" and never fails
the call.
- Decided after the PR review (2026-10-05): the count stays an exact DataService `COUNT(Id)`, one attempt of at most
  10 s; `get-object-rights` runs it last, after every object is read, so it never takes the read budget of the
  connected objects. Measured on kravchuk_0922 (8.3.4): `COUNT` over tables of up to 18 020 rows cost no more than a
  `ping`; its cost on a much larger customer table is not measured (accepted risk R9).

**RD7 — Apply: a separate tool `apply-default-record-rights`; the agent never runs it on its own.**
- Separate from `set` on purpose: several rule changes are followed by one heavy run, and its cost and risk differ.
- Args: `environment-name`, `entity-schema-name`, `wait` (default true), `timeout-seconds`.
- Refused when the switch is off (`NotAdministered`) — the process would only rewrite rights nobody evaluates.
- Starts `ObjectRecordRightsActualizationProcess` once, through the existing `KnownRoute.RunProcess`, with
  `MaxAttempts = 1` (a retry would start a second run). The response is interpreted by the existing
  `RunProcessCommand.BuildResponse` (refusal, no-handle and failure rules are the same).
- Returns `processId`. A status read that fails is retried at the next poll; when no status was ever read (no
  `SysProcessLog` row, or every read failed) the result says the status could not be read — never "still running".
  With `wait`, polls `SysProcessLog` by primary key until Completed / Error / Canceled or the
  deadline. The deadline gives "still running, process <id>", exit 0, not a failure; Error gives exit 1.
- Destructive, NOT idempotent (each call starts a run). On MCP the whole call — read, record count, launch and wait —
  shares a 100 s budget (worker budget policy `ParentKillDefault`); `timeout-seconds` defaults to 60 there.
- **The agent never applies rules on its own initiative** (Jira decision Q2): after any enable or rule change
  the guidance requires asking the user, with the record count and the cost, and the user decides whether and when.

**Repeated grant on an object that is OFF (AC6).** It is refused (`EnableNotRequested`) every time, like the first
identical call: that call could not have landed, so the refusal is the stable outcome. A call that names the enable
reports "no change" once the state is in place.

**A changed rule is written field by field.** Only the levels and the flag the plan changes are written; a field the
change does not touch stays exactly as the service stored it (decided after the PR review, 2026-10-05; a separate
"unreadable level" state was considered and dropped as unneeded).

**RD8 — Version tolerance.** Target is 10.2. The parser tolerates a missing `recordRightsDenied` (8.3.4) and a missing
`entitySchemaRecordDefRights` (treated as no rules). No behaviour depends on the version.

## Responsibilities: tool vs guidance

**The tools:** make exactly the change the arguments name on one object; refuse the transitions in RD4; report facts —
the rules, the switch, stored rules that come into effect, levels before → after, "rule removed", the record count,
the process status.

**The guidance** (new `default-record-rights` article; updates to `object-rights`, `record-permissions`,
`record-rights`, `routing`): when to use operation vs record vs per-record rights; the switch semantics (models 4, 6);
the full-replace save (model 2) as the reason for one change per call; **the built-in default (model 5a): turning
record permissions on with no rules means everyone sees only the records they created — so before an enable the agent
says this to the user and asks which rules are needed, if any**; portal access needs a rule (author "All
external users" or a portal role); the apply step — **ask the user, state the record count and the cost, never
auto-apply**; verification by re-reading with `get-object-rights` and, per record, `get-record-rights`.

## Alternatives considered

| Alternative | Verdict |
|---|---|
| Add record arguments to `set-object-rights` | Rejected. One call would then change two layers with different identity and save rules, and the approval would mix them. |
| A rule list in one call (replace all rules) | Rejected. The task scopes one rule per call; a whole-list argument would hide which rules are removed. |
| Start the actualization from `set` (a flag) | Rejected (Jira decision Q2): apply is the user's decision, and one run after several changes is cheaper. |
| Reuse `run-process` as is for apply | Rejected as the user-facing tool: it does not know the switch, refuses nothing, and does not poll. Its launch interpretation (`BuildResponse`) is reused. |
| Auto-repair duplicates / bad levels | Rejected. Which duplicate is "right" is a business decision; the tool refuses and names them. |

## Consequences

- New: `set-default-record-rights`, `apply-default-record-rights` (CLI + MCP long-tail), a record-rule model, a pure
  `DefaultRecordRightsPlanner` with the RD4 table, a record read-back, `get-object-rights` output for the record layer.
- Changed: `RightManagementServiceClient` parses the record layer and gains a record save path.
- Docs, help, `Commands.md`, wiki anchors, knowledge records for models 2/3, 6 and 7; guidance in clio-knowledge.
- **Accepted risks** (in addition to ENG-99741 R1–R4; its R5 — shadowing — does not apply, rules add up):
  - **R6 — Last writer wins on the WHOLE rule list.** A rule saved by someone else between our read and our save is
    lost, not only a concurrent change of the same rule (platform model 2). The read-back detects it after the fact.
  - **R7 — The record count is under the caller's rights.** For a caller without "view any data" on an object with
    record permissions on, it counts only the records visible to that caller. The output says whose count it is.
  - **R9 — The record count is an exact COUNT.** On a very large table it can take long on the server even after
    clio's 10 s client timeout; not measured on such a table (the largest measured: 18 020 rows, as fast as a ping).
  - **R8 — No destructive e2e in CI** (as R4 of ENG-99741): the round trip is an opt-in LocalOnly test, run once at
    the final head.

## Invariants (review baseline)

1. A call writes only the object named in its arguments.
2. A call changes at most one rule (one author+grantee pair); every other rule is saved exactly as read, and the
   read-back proves it.
3. `administratedByRecords` changes only with its explicit flag.
4. Nothing is granted by default: `operations` is always named for a rule change.
5. A list with duplicate pairs or invalid levels is never saved.
6. Policy is decided before the write; a refused call writes nothing.
7. Output and exit code come from the plan and the read-back; an unverified save is never reported as done.
8. Unknown argument names are refused before any read or write.
9. `apply-default-record-rights` starts at most one run per call, never automatically.

## Open questions (ADR-Q*, not the Jira Q1–Q4)

- ~~ADR-Q1 — Switch-only calls vs "operations required".~~ Decided (2026-10-03), see RD2 and the status.
- ~~ADR-Q2 — `RevokeOnNotAdministered`.~~ Decided (2026-10-03): dropped, see RD4.
- ~~ADR-Q3 — Stand.~~ Decided (2026-10-03): studioenu_16127697_1003 (10.2.370), kravchuk_0922 (8.3.4) as the fallback.