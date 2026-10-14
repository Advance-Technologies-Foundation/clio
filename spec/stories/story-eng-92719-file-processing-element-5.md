# Story 5: Builder-made user tasks do not set SerializeToDB

**Feature**: eng-92719-file-processing-element (side Sub-task)
**Jira**: not created yet. A new Sub-task of ENG-92719 File processing element (epic ENG-92704 Create BP via AI
Toolkit), titled "Builder-made user tasks do not set SerializeToDB"; Q9 (agreed 2026-10-07) creates it on day 0, and
its key (`<KEY>` below) is assigned on creation. Not created as of 2026-10-14.
**PRD**: [prd-eng-92719-file-processing-element.md](../prd/prd-eng-92719-file-processing-element.md) (scope: side
Sub-tasks)
**ADR**: [adr-eng-92719-file-processing-element.md](../adr/adr-eng-92719-file-processing-element.md) - D20
**Test plan**: [tp-eng-92719-file-processing-element.md](../test-plans/tp-eng-92719-file-processing-element.md) - the
story 5 rows (TC-U-111..TC-U-113, TC-M-52)
**Status**: deferred (its slot is after story 3's package PR, SysFile storage; it moves to ready-for-dev then, once its
Sub-task exists)
**Size**: S (about 4-6 h for its triple, inference; outside the ENG-92719 estimate)
**Repo**: crt-process-builder, then clio (the rebundle), then clio-knowledge only if the change is advertised: its own
triple, titled `<KEY> Builder-made user tasks do not set SerializeToDB`
**Depends on**: story 3 (slot only, analysis plan section 5.4 and pr-split section 12.3; no functional dependency)
**Blocks**: nothing; it does not gate the closure of ENG-92719 File processing element

---

## Goal

Every user task the process builder creates carries `SerializeToDB` as the designer writes it: copied from the
user-task schema.

## Why it is a separate Sub-task

- The designer writes the element key `BO2` (SerializeToDB) from the task schema's `SerializeToDB` (`FK4`): `true` on 30
  of 30 shipped file elements (corpus). The builder writes none: M14 measured `BO2` absent on a generic-route element on
  CrtProcessBuilder 1.6.6.54 (2026-10-02), and the analysis records the gap for every builder-made user task (corpus and
  source).
- Story 1's dedicated handler writes `SerializeToDB = taskSchema.SerializeToDB` for the Object variant, and story 2's
  registrations inherit it (ADR D20). Every other user task built by the builder keeps the gap, so the fix belongs to
  the user-task handlers in general, not to the file element (decisions D20, D-5).
- What the missing key changes at run time is not measured; the analysis records it as a serialization difference that
  the comparison rule must match (serialization-capture row N3), not as a runtime defect.

## Acceptance (proposed; confirmed when the Sub-task is created)

- [ ] A user task created through the generic `{type: userTask, userTaskName}` route stores `SerializeToDB` equal to
      the task schema's value. Scope to confirm when refined: the analysis names the generic route; whether a dedicated
      user-task handler other than the file element also omits it is checked then.
- [ ] Elements the request does not touch are not rewritten. Whether a modify that touches an existing builder-made
      user task adds the key (the "repair on touch" shape of ADR D12) or leaves it is decided when refined.
- [ ] A builder-made generic user task compared with a designer capture of the same task shows no `BO2` difference.
- [ ] On a stand carrying the cut, M14's generic-route shape read back from `SysSchema.MetaData` carries `BO2`, and a
      no-op designer save of it changes nothing.

## Test sketch

Package unit cases (the user-task handler tests): TC-U-111, a generic-route create stores `SerializeToDB` from the task
schema, for a schema whose value is true and one whose value is false; TC-U-112, the dedicated handlers' output is
unchanged; TC-U-113, an untouched element of a modify is not rewritten. One stand case (TC-M-52): M14's recipe on the
cut, read from the raw `SysSchema` row (never from describe, which does not report the key), plus the user's no-op
designer save. Ids are this story's rows of the test plan; they are refined with the criteria.

## Definition of Done

- [ ] The Sub-task created in Jira and its key written into this file, its `spec/sprint-status.yaml` row and the PR
      titles; criteria confirmed with the owner and the status moved to ready-for-dev.
- [ ] The repository rules of story 1 apply unchanged: package fixture categories, AAA, `because:`, `[Description]`;
      the restamp claimed in the shared cut claim; the floor rule; the MCP, docs and ClioRing review statements;
      review gates 1, 2 and 3; only a human merges; never inside a final review window of the main track.
- [ ] Status flips in the clio PR of this triple: `in-progress` when its draft opens, `review` when it leaves draft;
      `done` rides in the next clio PR that touches `spec/sprint-status.yaml`.
