# Story 25: Document `versions[].packageName` once a clio build ships it

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

no-code builder asking which package a version of my process lives in

## I want

the agent's guidance to know the field exists, and from which build

## So that

it names the package instead of resolving one itself, and never reports an absent name as a failure

---

## Blocked by

Story 19 shipping in a released clio. The field exists only on this branch.

## Source

The review of story 21 rewrote the `versions[]` entry for `packageName` from a TEMPORAL claim into a
CONDITIONAL one. It had been documented as a present field with an ABSENT rule — "absent when the
package could not be named, and the warning says so" — while no released clio returns it at all, so on
every current environment an agent would have read the shape of the response as a reported failure.
That is the same wrong-by-a-version claim D4 was, from the other direction.

The field is still documented, conditionally: use a name when one is there, resolve it yourself when
it is not, and read `versionReadWarning` first. What is missing is the BOUNDARY — which build starts
returning it — and that cannot be written before one does.

## Acceptance Criteria

- [ ] **AC-01** — Given the `versions[]` field list, when the build that first returns `packageName` is released, then the conditional wording gains a clio BOUNDARY naming that build, in the same shape as the block-level boundary the other version fields already carry
- [ ] **AC-02** — Given that entry, when it is read, then it says to name the package by `packageName` and that `packageUId` is the identity rather than the answer to the question
- [ ] **AC-03** — Given the ABSENT rule, when it is read, then it separates "this build does not return it" from "the package could not be named", and only the second is tied to `versionReadWarning`
- [ ] **AC-04** — Given the sentence telling the agent to resolve the name itself, when the field ships, then it is scoped to builds below the boundary rather than left standing
- [ ] **AC-05** — Given the new text, when the guidance fixture runs, then the field and its boundary are pinned — the D5 correction shipped unpinned once and the review caught it

## Implementation Notes

Do not name the boundary version before it exists. That is the D4 defect, and this story exists
because the first attempt made the same shape of claim from the other direction.
