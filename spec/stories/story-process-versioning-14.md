# Story 14: Stamp the package version for the rebundle

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-08
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: ready-for-dev
**Size**: S

---

## As a

developer

## I want

the package's descriptor stamped with a version higher than the shipped one

## So that

clio's rebundle has something to bundle and every environment is offered the update

---

## Acceptance Criteria

- [ ] **AC-01** — Given the package repository on a clean checkout of `main`, when the version is stamped, then `PackageVersion` and `ModifiedOnUtc` move together
- [ ] **AC-02** — Given the chosen version, when it is compared with the previously shipped one, then it is strictly higher
- [ ] **AC-03** — Given the other in-flight version stamps in this repository, when the version is chosen, then the choice is recorded together with the merge order it assumes
- [ ] **AC-04** — Given `Schemas/CrtProcessBuilderCompileMarker/descriptor.json`, when the stamp runs, then that marker is left untouched — clio verifies it rather than rewriting it
- [ ] **AC-05** — Given `docs/bundling-into-clio.md:122`, when this story lands, then the reference to the deleted `BundledPackages.ProcessBuilderVersion` is replaced with `ExpectedArchiveVersion`
- [ ] **AC-ERR** — Given a version not higher than the descriptor's, when the stamp is attempted, then it is refused

## Implementation Notes

`clio set-pkg-version` moves both fields in one step and leaves the provenance marker the pins expect.
Sequence against the other in-flight stamps in this repository (PRD OQ-03) — the queue is split, so the merge order is part of the decision.
Delete `Files/Bin` before anything is packed: `.clio/clioignore` does not filter it and a stray assembly breaks source-only delivery.
The stale doc line at `docs/bundling-into-clio.md:122` lives in this repository, so it is fixed here rather than in the clio story — that keeps each story to one repository.
This story touches no code, so the architecture diagram and the ClioRing gate do not apply.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | descriptor version and stamp move together | `tests/CrtProcessBuilder/` |
| Integration `[Category("Integration")]` | a clean-checkout pack produces a reproducible archive | manual, recorded in the PR |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Rebundle only from a clean checkout — a tree carrying just-written files produces an unreproducible SHA
- [ ] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [ ] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [ ] PR description references this story file
- [ ] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`)

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
