# Manual test cases — activity connections ("Connected to")

**Feature**: process-element-connections
**ADR**: [adr-process-element-connections.md](../adr/adr-process-element-connections.md)
**Stories**: [1](../stories/story-process-element-connections-1.md) · [2](../stories/story-process-element-connections-2.md) · [3](../stories/story-process-element-connections-3.md) · [4](../stories/story-process-element-connections-4.md) · [5](../stories/story-process-element-connections-5.md)

Each case is written as a **prompt you paste to an agent** that has clio's MCP server registered — the way
the capability is actually used. No case tells the agent which tool to call: picking `modify-business-process`
over `addMapping`, or refusing outright, is part of what is under test. Phrase the prompt in any language;
the messages quoted below are what the server returns regardless.

## Why every case has TWO expected results

A connection can be written, persist, survive a configuration build, and read back perfectly — and still
write **nothing** when the process runs. That is not a hypothetical: the value is stored as a script-sourced
macro, and unless its `ModifiedInSchemaUId` names the process schema, code generation emits no assignment.
Nothing about that is visible in the designer or in `describe-business-process`.

So a case is only passed when **both** planes are checked:

| Plane | What it means | How to check |
|---|---|---|
| **Design-time** | The connection is stored on the element and reads back in the shape it was written | `describe-business-process`, and for the UI cases the process designer itself |
| **Runtime** | The record the element creates actually carries the linked record | run the process, then read the created `Activity` row |

A case that passes design-time and fails runtime is the exact defect this feature exists to prevent, so
report it as a failure, not as a partial pass.

## Preconditions (once per environment)

1. An environment registered in clio, and `CrtProcessBuilder` installed **from this branch's archive**:
   ```bash
   dotnet run --project clio/clio.csproj --framework net8.0 -- install-process-builder -e <env> --force
   ```
   `--force` is required when the environment already records the same version, and it also compiles the
   configuration — a source-only package needs that or every `/rest/ProcessDesignService/*` call fails while
   the package still reports as present.
2. A **real** `Contact` and a **real** `Account` record, and their ids. Runtime cases must bind real ids:
   `Activity.ContactId` / `AccountId` are foreign keys, so a random GUID persists fine at design-time and
   fails the insert at run time.
3. A package you may write to (`Custom` is fine).

Two constraints that apply to every case:

- **Do not create record-triggered signal starts.** Every runtime case below starts its process explicitly.
  A live signal fires on real user activity and turns a test into a background job nobody asked for.
- **Run schema-write requests one at a time.** A parallel burst of them trips IIS rapid-fail protection and
  takes the application pool down on a .NET Framework stand; the failure looks like an unrelated outage.

---

## TC-01 — Link the created activity to a fixed record

> Create a business process `UsrConnTc01` in package `Custom`: a start event, a "Perform task"
> (`ActivityUserTask`) named `Follow up`, and an end event. Then link the activity that task creates to
> contact `<contact name>`. Show me the element afterwards.

**Design-time** — `describe-business-process` reports, on element `Follow up`:

```
connections: [ { column: "Contact", registered: true, source: "Script",
                 value: "[#Lookup.<contactSchemaUId>.<recordId>#]",
                 recordId: "<recordId>", referenceSchema: "Contact" } ]
writesConnectionsAtRuntime: true
```

`recordId` decoded back out is the point: the caller supplied a record id and no schema UId, and the macro
was synthesised from the target column's own reference entity.

**Runtime** — run the process. The created `Activity` row carries `ContactId = <recordId>`. Read it back by
title or by `CreatedOn` — do not trust the process log alone, a completed process says nothing about which
columns were written.

---

## TC-02 — Link the created activity to the current record

> In `UsrConnTc02` (start → Perform task `Log the call` → end), add an activity and link it to the current
> record — the record the process was started for.

**Design-time** — the connection's decoded source is a **process parameter**, not a record id:

```
connections: [ { column: "Contact", source: "Script", value: "[#<parameter metapath>#]",
                 processParameter: "<the parameter the agent used or created>" } ]
```

If the process had no such parameter, the agent is expected to add one and bind it — that is a correct
solution, not a workaround. What must **not** happen is a hard-coded `recordId`.

**Runtime** — start the process twice with two different contacts in that parameter. Each run's activity
carries the contact of **its own** run. One shared value across both runs means the macro was resolved once
at design time.

---

## TC-03 — Link the created activity to the output of an earlier element

> In `UsrConnTc03`, put a "Read data" element that reads one contact, then a Perform task after it, and link
> the task's activity to the contact that the read element found.

**Design-time** — the decoded source is the element output pair:

```
connections: [ { column: "Contact", source: "Script",
                 sourceElement: "<read element name>", sourceElementParameter: "<its output parameter>" } ]
```

**Runtime** — the created activity's `ContactId` equals the contact the read element actually selected.
Change the read filter so it finds a different contact, re-run, and the activity follows it.

---

## TC-04 — A second call must not silently clear the first connection

> Take `UsrConnTc01`. Now also link the activity to account `<account name>`. Leave everything else alone.

**Design-time** — the element carries **both** connections: `Contact` from TC-01 **and** the new `Account`.
`setConnections` is an upsert keyed on `column`: columns you do not list are left alone, so clearing is only
ever explicit.

**Runtime** — the created activity carries both `ContactId` and `AccountId`.

**Fails silently if** the implementation treats the request as the full set: the first connection disappears
and nothing in the response says so. Check `Contact` explicitly; do not settle for "the new one is there".

---

## TC-05 — Clearing one connection leaves the others alone

> On `UsrConnTc01`, remove the link to the contact. Keep the account link.

**Design-time** — `Contact` is gone from `connections[]`; `Account` is still there untouched. Note that
clearing is not deleting: the element's parameter survives with no source, which is why the cleared column
stops being reported rather than turning into an empty entry.

**Runtime** — the new activity has `ContactId` empty and `AccountId` still populated. Keep an activity from
before the clear as the control: it must still carry its contact, since clearing changes the process, not
the records it already created.

---

## TC-06 — Refuse a connection on an element that creates no record

> In `UsrConnTc06`, add a script task (or a sub-process) and link the record it creates to contact
> `<contact name>`.

**Design-time** — the request is **refused**, with a message saying the element is not a user task, creates
no record, and therefore has no connections — and pointing at `addMapping` / `setParameter` for an ordinary
input mapping. The process is unchanged: `describe-business-process` shows no connection and no new
parameter.

**Runtime** — nothing to run. If the request was instead accepted, stop and re-check the element type
before running anything.

**Why this case exists**: the candidate columns are seeded from the host entity's whole registry regardless
of the element, so a same-named parameter on an unrelated element ("Contact" on a sub-process) can be
matched and unbound while the response reports a cleared connection. A shipped build did exactly that. So
also run the reverse: give that element a parameter genuinely named `Contact`, wired as an input mapping,
then ask to *remove the contact link*. The mapping must survive.

---

## TC-07 — A column with no registry row succeeds, with a caveat

> Link the activity created by `UsrConnTc01`'s task to a record through a column of `Activity` that is not
> registered as a connection (ask which columns qualify if you need to).

**Design-time** — the call **succeeds** and the response carries a warning naming the column
(`Connection '<column>' is not registered …`). `describe-business-process` reports the connection with
`registered: false`.

That combination is deliberate: the value is written and works, but the platform's own connection surfaces
(the record page's connections detail, Next Steps, email auto-relation, quick-add defaults) key off the
registry, so an unregistered column will not appear there. A failure would be worse — the column exists and
the write is legitimate.

**Runtime** — the created activity carries the value in that column.

---

## TC-08 — A connection that will never be written must say so

> Create `UsrConnTc08` with a "User dialog" (`UserQuestionUserTask`) element, make sure it is **not**
> configured to create an activity, and link its activity to contact `<contact name>`.

**Design-time** — two acceptable outcomes, and both must be legible:

- the request is refused, naming the element's own gate (`CreateActivity`) and how to turn it on; or
- it is accepted and `describe-business-process` reports `writesConnectionsAtRuntime: false` on that element.

What must **not** happen is acceptance with `writesConnectionsAtRuntime: true`, and what must not happen
either is `null` — `null` means "not established" (not a user task, or the state could not be evaluated),
which is a different answer from "established, and it is false".

**Runtime** — run it. No activity is created, so nothing is written. That is the whole point of the field:
the design-time state looks correct and the runtime does nothing.

**Known gap, do not report as new**: batching `[link the activity, then switch CreateActivity off]` in ONE
request saves the inert state without a warning, because the verdict is evaluated when the link is made and
not again at save. It is discoverable afterwards through `writesConnectionsAtRuntime`.

---

## TC-09 — An environment with an older package must be told, not ignored

> Point at an environment whose `CrtProcessBuilder` predates this feature and try TC-01 there.

**Design-time** — you get a **loud** refusal, one of:

- clio refuses before the call, naming the installed version, the version it ships, and
  `install-process-builder` as the remedy; or
- the server refuses by operation name (`Operation 'setConnections' is not supported. Supported: …`).

**Runtime** — nothing was written; the process is unchanged.

**Fails if** the call reports success and the connection is simply absent. An unknown *member* of a known
contract is dropped in silence by the platform's serializer — that is why an unknown *operation name* being
refused by name is the property under test here.

---

## TC-10 — What describe reports must be re-appliable unchanged

> Show me the connections on `UsrConnTc01`'s task, then apply exactly what you just showed me back to the
> same element, changing nothing.

**Design-time** — accepted, and the element's state is byte-identical afterwards: same column, same macro,
same decoded source. Run it for all three dialects (TC-01 fixed record, TC-02 process parameter, TC-03
element output) — the read-back is only useful if it round-trips for each.

**Runtime** — unchanged behaviour from the corresponding case above.

**Note** — dropping `referenceSchema` when re-applying is not neutral: the server recomposes the macro from
the column, so an entry that named the *other* entity's record loses that qualification. Re-send what
describe reported.

---

## TC-11 — A connection column created minutes ago

> Register a new connection column on `Activity` in package `Custom`, then link `UsrConnTc01`'s task through
> it — the task was built before that column existed.

**Design-time** — the element gains a **new parameter** for that column (it declared none), and the
connection reports against the canonical column name.

**Runtime** — the created activity carries the new column's value.

**Why this case exists**: every other case binds a column the element already declared a parameter for. This
is the only one that exercises parameter *creation*, and it is the path where a failed write must roll the
created parameter back rather than leave a stray parameter behind. If the call fails, re-describe and
confirm no orphan parameter was left.

---

## TC-12 — The designer must show what we wrote, and keep it

> (No prompt — do this in the browser.)

Open `UsrConnTc01` in the process designer, select the Perform task, and look at its **Connected to** area.

**Design-time** — the connections written through the API are listed there with the right records. Now change
something unrelated in the designer (the element's caption), save, and re-run `describe-business-process`:
the connections are still there, unchanged.

**Runtime** — run the process from the designer's own Run and confirm the created activity carries them, so
the same result is proven through the UI path and not only through the service.

---

## TC-13 — Deprecated user tasks are marked

> Which user-task elements can I use in a business process?

**Design-time** — the answer marks deprecated schemas as deprecated rather than listing them as equals, and
two entries share the caption "Send email" — the live one and the superseded one. An agent picking by
caption alone will choose wrong half the time, which is why the marking is under test.

**Runtime** — not applicable.

---

## Coverage map

| Case | What it pins |
|---|---|
| TC-01 | the base write path; `recordId` needs no schema UId |
| TC-02 | process-parameter dialect; resolved per run, not once |
| TC-03 | element-output dialect |
| TC-04 | D1a upsert semantics — unlisted columns survive |
| TC-05 | clear ≠ delete, and clearing is scoped to what was named |
| TC-06 | the user-task gate on BOTH operations; an input mapping is not a connection |
| TC-07 | unregistered column: success plus caveat, not failure |
| TC-08 | the effectiveness verdict, and its known batching gap |
| TC-09 | D8 — a stale package is loud, not silent |
| TC-10 | D11 — the read-back is re-appliable per dialect |
| TC-11 | created-parameter path and its rollback |
| TC-12 | the designer round-trip |
| TC-13 | deprecation marking on the element catalogue |
