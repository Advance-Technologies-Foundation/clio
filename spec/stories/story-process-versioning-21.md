# Story 21: Correct the process-versions article where the run disagreed with it

**Feature**: process-versioning
**Jira**: ENG-94374
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio-knowledge
**Status**: review
**Size**: S

---

## As a

no-code builder being advised by an agent that reads this article

## I want

the article to be right about what the environment does

## So that

the agent does not send me to clean up nothing, or steer me to the identity that reads the wrong graph

---

## Source

Manual testing of this story, 2026-09-11 — two defects against the shipped article:

**D4.** The article claims the false *"a partially created process schema may still exist"* message is
*"a CrtProcessBuilder defect up to 1.6.1.0, fixed in 1.6.1.1"*. The stand ran **1.6.1.9** and still
emitted it on a descriptor-validation failure, with `SysSchema` count 0 afterwards. The fix in
1.6.1.1 was applied to `ProcessVersionSaveHandler` — the `modify-business-process-as-new-version`
path — and never to the create path. The message itself belongs to `crt-process-builder` (story 22);
the **claim that it is fixed** is this article's.

**D5.** From the `TC-01` session, verbatim to the builder: *"Resolving this process by its caption is
ambiguous and silently picks v2 ... If you script anything against it, use the schema code, not the
caption."* Caption → active version is the designed resolution this article documents; the CODE
resolves to the root, which is normally not what runs. The reply framed correct behaviour as a hazard
and recommended the anti-pattern.

## Acceptance Criteria

- [x] **AC-01** — Given the "A rejected edit saves nothing" section, when it is read, then it scopes the false message by PATH — clean on the version path, still emitted on the create path — and names no fix version it cannot stand behind
- [x] **AC-02** — Given that section, when it is read, then it still says plainly that the message is wrong and that nothing is to be deleted on its advice
- [x] **AC-03** — Given the "Choosing an identity" section, when it is read, then it forbids advising a builder to prefer the code over the caption for "what runs", and says the ambiguity refusal is a designed refusal rather than a silent pick
- [x] **AC-04** — Given a place where a CODE is genuinely required, when the article is read, then it says to take that code from `activeVersionName`
- [x] **AC-05** — Given the version-field list, when it is read, then it carries `packageName` and says to report the package by name
- [x] **AC-06** — Given the rollback section, when it is read, then it states that the family ROOT is a valid activation target

## Implementation Notes

Sibling articles are the likely source of D5 and are correct in their own context — a button's
`processName` and a script task do need a code. The cure is a rule in the article that OWNS the
version model, plus a cross-reference, not a rewrite of those two.

Needs a `libraryVersion` + `sequence` bump in `bundle-source.json`.
