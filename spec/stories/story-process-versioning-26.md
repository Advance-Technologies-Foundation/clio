# Story 26: Name the shipped fix version in the create-path paragraph

**Feature**: process-versioning
**Jira**: ENG-94374
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio-knowledge
**Status**: ready-for-dev
**Size**: S

---

## As a

no-code builder whose `create-business-process` call was refused

## I want

the guidance to tell me which build stopped emitting the false leftover message

## So that

I know whether the environment I am on still carries it

---

## Blocked by

The archive shipping. Story 23 cut it; the create-path paragraph names no version until a release
carries those bytes.

## Source

Carried out of story 23, whose AC-05 closed as "still names NO version" rather than as done. That is
the deliberate outcome, not a gap: D4 was a guidance paragraph naming a fix version before the fix
shipped, and the same round then repeated the shape from the other direction in the D9 clause — a
version CEILING that was already false when written, because the correction was unmerged.

Both are the same rule. A version number in an article is a claim about the field, and the field is
only knowable after a release.

## Acceptance Criteria

- [ ] **AC-01** — Given the create-path paragraph in `process-version-writes`, when the archive has shipped, then it names the version that fixes it and drops "a fix is in flight"
- [ ] **AC-02** — Given that edit, when the by-path scoping is read, then it survives: `modify-business-process-as-new-version` clean since 1.6.1.1, `create-business-process` from the newly named version — re-merging the two paths is the original defect
- [ ] **AC-03** — Given the fixture, when it runs, then the existing pins still hold — every mention of a version names the path it is true of, and no "up to and including" ceiling appears

## Implementation Notes

The pins added in story 21 are what makes this safe to edit: they fail if the two paths are merged
back together or if a ceiling reappears. Read them before rewording.
