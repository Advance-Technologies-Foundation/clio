# Story 14: Stamp the package version for the rebundle

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-08
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: done
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

- [x] **AC-01** — Given the package repository on a clean checkout of `main`, when the version is stamped, then `PackageVersion` and `ModifiedOnUtc` move together
- [x] **AC-02** — Given the chosen version, when it is compared with the previously shipped one, then it is strictly higher
- [x] **AC-03** — Given the other in-flight version stamps in this repository, when the version is chosen, then the choice is recorded together with the merge order it assumes
- [x] **AC-04** — Given `Schemas/CrtProcessBuilderCompileMarker/descriptor.json`, when the stamp runs, then that marker is left untouched — clio verifies it rather than rewriting it
- [x] **AC-05** — Given `docs/bundling-into-clio.md:122`, when this story lands, then the reference to the deleted `BundledPackages.ProcessBuilderVersion` is replaced with `ExpectedArchiveVersion`
- [x] **AC-ERR** — Given a version not higher than the descriptor's, when the stamp is attempted, then it is refused

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

- [x] Rebundle only from a clean checkout — a tree carrying just-written files produces an unreproducible SHA (the stamp was taken on a committed tree; the rebundle itself is story 15)
- [x] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [x] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [x] PR description references this story file — open in crt-process-builder#47 (https://creatio.ghe.com/engineering/crt-process-builder/pull/47)
- [x] N/A — ClioRing MCP compatibility verdict: this story ships no code, per the Implementation Notes

## Dev Agent Record

- Implementation started: 2026-09-06
- Implementation completed: 2026-09-06
- Tests passing: 985 (981 + 4 new in `tests/CrtProcessBuilder/PackageDescriptorTests.cs`)
- Notes:

**Version chosen: `1.5.0.0`** (from `1.4.0.40`), stamp `/Date(1788725001000)/` = 2026-09-06T20:03:21Z.

**AC-03 — the merge order it assumes: none.** That is the point of the number. Five remote branches are open
ABOVE `main`'s `1.4.0.40`, under two conventions at once:

| Branch | Stamp |
|---|---|
| `feature/ENG-96230-collection-parameter-type` | 1.4.0.41 |
| `feature/ENG-95153-read-data-modes` | 1.4.0.42 |
| `tooling/mutation-gate` | 1.4.0.47 |
| `feature/ENG-95891-formula-expressions` | 1.4.0.53 |
| `feature/ENG-92713-approval-element` | **1.4.10.0** |

`1.4.10.0` is deliberate (its commit message explains a doc-only bump from `1.4.9.0`) and sorts above every
`1.4.0.NN`. So any fourth-component number would have to be restamped if the approval-element branch merged
first. `1.5.0.0` clears all five in either direction and matches what this repository already did per feature
(`1.2.0.0` readData, `1.3.0.0` changeData, `1.4.0.0` Pre-configured page). **PRD OQ-03 is answered by removing
the ordering constraint rather than by choosing an order.** The rule is recorded for the next rebundler in
`docs/bundling-into-clio.md` → "Choosing the version", with the branch-scan command.

**AC-ERR — correction to the Implementation Notes.** `clio set-pkg-version` does **not** refuse a
non-increasing version: it refuses only a missing or unparseable one, and merely WARNS below four parts
(`SetPackageVersionCommand.Execute`). The must-increase guard is in clio's `rebundle-process-builder.ps1`
(two refusals — against the descriptor, and against the version inside the committed archive). Following this
story's own instruction to stamp with `set-pkg-version` therefore bypasses it. So the refusal now also exists
in THIS repository: `PackageDescriptorTests` pins the last SHIPPED pair
(`ExpectedArchiveVersion` / `ExpectedDescriptorModifiedOnUtc`) and fails unless BOTH moved past it. Verified
by raising the pin and watching it fail — not assumed.

**AC-01 measured.** `set-pkg-version` moved both fields together, cleared the milliseconds (the provenance
oracle clio asserts), wrote correct UTC on a UTC+3 host, and all nine descriptor fields survived the DTO
round-trip. **AC-04**: `git diff --stat packages/CrtProcessBuilder/Schemas/` is empty — the compile marker
was not touched.

**AC-05**: the stale line said the deleted `BundledPackages.ProcessBuilderVersion` "IS the `[RequiresPackage]`
version floor". Both halves were wrong after the floor redesign, so the paragraph was rewritten rather than
find-and-replaced: the pins are TEST-side and read at runtime by nothing, and the floors are declared by the
commands and checked against the bundled version.

**Blocker handed to story 15**: `rebundle-process-builder.ps1` is `#Requires -Version 7.0` and this machine
has only Windows PowerShell 5.1 — install pwsh 7 or use the runbook's manual fallback.
