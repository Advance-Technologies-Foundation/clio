# Story 4: Describe: decode process-parameter sources into re-appliable names

**Feature**: eng-95984-file-parameter-type (follow-up)
**Jira**: KEY-PENDING - Sub-task of ENG-95984 File process parameter type, created on day 0
**Spec**: [spec-eng-95984-file-parameter-type.md](../prd/spec-eng-95984-file-parameter-type.md) (non-goals)
**ADR**: [adr-eng-95984-file-parameter-type.md](../adr/adr-eng-95984-file-parameter-type.md) - D8
**Status**: deferred (moves to ready-for-dev when its slot opens and its criteria are refined)
**Size**: M (inference: a package decode, a new typed member on a clio DTO with its tests and docs, guidance; re-sized
when refined)
**Repo**: crt-process-builder, then clio, then clio-knowledge (its own PR triple)
**Depends on**: story 1 (ENG-95984 File process parameter type) merged; **earliest slot: after ENG-96506 Generated
report + process parameter modes** (its package PR merged)
**Blocks**: nothing; it does not gate ENG-95984's closure

---

## Goal

When a stored value is exactly one process-parameter token (a flat parameter, or a dotted item such as `Docs.File`),
`describe-business-process` reports it as a structured, re-appliable name (for example a `processParameter` member), so
a described binding replays as a structured mapping that goes through the two-level binder's rules, instead of as an
`expression` that bypasses them.

## Why it is out of ENG-95984

- ENG-95984 decodes single-token ELEMENT sources into `sourceElement` + `sourceElementParameter`, members clio's
  `DescribedParameter` already declares, so clio needs no DTO change (D8).
- A process-parameter source needs a NEW member on `DescribedParameter`, which has no overflow bag: an undeclared field
  is dropped by clio, so the member must be added and typed in clio, documented, and floored. That is its own triple.
- Until then those bindings replay as expressions, accepted within the same process because describe reports the
  canonical token (ENG-95984 test plan, the replay round trip).

## Acceptance sketch (to be refined)

- [ ] A value that is exactly one process-parameter token is reported by name (dotted for an item), only when the write
      side, given that name back, would store this very value; `value` stays.
- [ ] A multi-token formula, a stale UId and a dot-ambiguous name report no name (the same negative set as ENG-95984's
      element-source decode).
- [ ] The clio DTO declares the member, with a `///` doc that says when it is null; an older clio drops it, so the
      describe consumer contract is reviewed for a floor.
- [ ] Round trip: build, describe, replay the described parameters as structured mappings, and both levels of every
      collection binding are identical to the original.
- [ ] The guide's read-back instructions name the new member.

## Test sketch

Package unit cases beside ENG-95984's element-source decode cases (positive and negative); clio
`ServerProcessDescriberTests` for the member's pass-through and omission when null; one MCP e2e describe read-back.
Test ids are added to [tp-eng-95984-file-parameter-type.md](../test-plans/tp-eng-95984-file-parameter-type.md) when
the criteria are refined.

## Definition of Done

- [ ] Criteria refined with the owner and the status moved to ready-for-dev in `spec/sprint-status.yaml`.
- [ ] The repository rules of story 1 apply unchanged (package fixture categories, AAA, `because:`, `[Description]`,
      MCP and docs review statements, review gates, only a human merges).
