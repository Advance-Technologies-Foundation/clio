# Story 20: Stop the set-active contract forbidding what the platform allows

**Feature**: process-versioning
**Jira**: ENG-94374
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: review
**Size**: S

---

## As a

no-code builder who wants to undo everything and go back to the original process

## I want

the toolkit to tell me that is possible

## So that

I am not told to copy or re-version my way around a rollback the platform performs directly

---

## Source

Manual testing of this story, 2026-09-11 (defect **D9**). `set-active-business-process-version`
stated *"it must be the version itself, not the family root"*. Activating the root
(`UsrBPEng94374Base`, version 0) succeeded normally on the stand — and it is the natural "go back to
the original" gesture. No guard existed in clio or in `CrtProcessBuilder`; the restriction lived in
that one sentence.

## Acceptance Criteria

- [x] **AC-01** — Given the `set-active-business-process-version` description, when it is read, then it states that any member of the family is a valid target, the ROOT included, and that activating the root is the go-back-to-the-original gesture
- [x] **AC-02** — Given the `version-name` / `version-uid` argument descriptions, when they are read, then neither implies the root is excluded
- [x] **AC-03** — Given a family whose active member is a version, when the root is activated over the real MCP path, then the call succeeds and describe reports the root active and the version not

## Implementation Notes

Text only — there was no guard to remove. The measurement that settles it is recorded in
`docs/knowledge/ProcessModel/the-family-root-can-be-made-the-actual-version.md`; do not reintroduce
the claim in a description, an article or a validation without a measurement that contradicts it.

The matching guidance sentence is story 21, in `clio-knowledge`.
