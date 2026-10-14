# Story 3: Allow-list the data types a typeFromElement mirror may copy

**Feature**: eng-95984-file-parameter-type (follow-up)
**Jira**: ENG-103163 - Sub-task of ENG-103304 File and collection process parameters: follow-ups (created 2026-10-14
under ENG-95984 File process parameter type and moved there the same day, so that ENG-95984 can close)
**Spec**: [spec-eng-95984-file-parameter-type.md](../prd/spec-eng-95984-file-parameter-type.md) (non-goals)
**ADR**: [adr-eng-95984-file-parameter-type.md](../adr/adr-eng-95984-file-parameter-type.md) - D6, rejected option b
**Status**: deferred (moves to ready-for-dev when its slot opens and its criteria are refined)
**Size**: S (inference: one check in the mirror path plus a corpus scan; re-sized when refined)
**Repo**: crt-process-builder, clio, clio-knowledge (its own PR triple; clio and knowledge only if the refusal is
advertised)
**Depends on**: story 1 (ENG-95984 File process parameter type) merged; **earliest slot: after ENG-96506 Generated
report + process parameter modes** (its package PR merged)
**Blocks**: nothing; it does not gate ENG-95984's closure

---

## Goal

The `typeFromElement` mirror (`addParameter` with `typeFromElement` / `typeFromElementParameter`) copies the data type
of an element parameter verbatim, with no allow-list. Restrict it to the data types a process parameter can hold, so a
mirror can never create a parameter of a type the flow engine cannot store or the designer never offers.

## Why it is out of ENG-95984

- ENG-95984 fixes the part that is a live bug: a DOTTED mirror source (`ObjectFiles.File`) silently created an unbound
  parameter, and is now refused (D6 option a). A type allow-list on every mirror is a different change.
- Done without evidence, an allow-list could refuse a mirror that works today, unannounced. It is gated on a scan of
  the output types the shipped user tasks expose (D6, agreed with the follow-up list on 2026-10-07).

## Acceptance sketch (to be refined)

- [ ] A recorded corpus scan of the output parameter types of the shipped user-task schemas, with the date and the
      count per type, decides the allow-list.
- [ ] A mirror of a type on the list behaves exactly as before (regression over every type the scan found mirrorable).
- [ ] A mirror of a type outside the list (for example Binary or the BLOB type the platform names "File") is refused
      with a message that names the type and the alternative; the schema is unchanged.
- [ ] The refusal text is server-side and costs no tool-description bytes; guidance mentions it only if agents need
      to know the alternative.

## Test sketch

Package unit cases beside the ENG-95984 mirror refusals (one per allowed type family, one per refused type, the
schema-unchanged assertion); an e2e refusal case only if the clio surface changes. Test ids are added to
[tp-eng-95984-file-parameter-type.md](../test-plans/tp-eng-95984-file-parameter-type.md) when the criteria are
refined.

## Definition of Done

- [ ] Criteria refined with the owner, the scan recorded, and the status moved to ready-for-dev in
      `spec/sprint-status.yaml`.
- [ ] The repository rules of story 1 apply unchanged (package fixture categories, AAA, `because:`, `[Description]`,
      a floor raise only if clio starts advertising the refusal, review gates, only a human merges).
