# Story 19: Name the package of every version-family member

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

no-code builder asking which package a version of my process lives in

## I want

the answer to name the package

## So that

I can decide whether that version is somewhere I may edit, without resolving a GUID myself

---

## Source

Manual testing of this story, 2026-09-11 (defect **D2**), on studioenu_16008805_0914 /
CrtProcessBuilder 1.6.1.9 / clio master 1ce39514e. `TC-03` requires every family member to say which
package it lives in; `versions[]` carried `packageUId` only, so the builder-facing answer read back
verbatim as *"lives in package a00051f4-cde3-4f3f-b08e-c5ad1a5c735a"*.

## Acceptance Criteria

- [x] **AC-01** — Given a described process with a version family, when the response is read, then every `versions[]` entry carries `packageName` beside `packageUId`
- [x] **AC-02** — Given a family whose members live in different packages, when the response is read, then each entry names its OWN package
- [x] **AC-03** — Given a package UId with no `SysPackage` row, when the response is read, then that entry's `packageName` is absent, `packageUId` survives, and NO warning is raised — an unresolved name is not an unestablished standing
- [x] **AC-04** — Given the package table cannot be read at all, when the response is read, then the version facts are still established and `versionReadWarning` says the packages could not be named
- [x] **AC-05** — Given the `describe-business-process` tool description, when it lists the family-entry fields, then it names `packageName` and says to report the package by name rather than by UId

## Implementation Notes

`VwProcessLib` has no package-name column, so the name comes from a second, unfiltered `SysPackage`
query joined in memory. Why unfiltered, and why not one query per UId, is recorded in
`docs/knowledge/ProcessModel/version-family-package-names-need-a-second-query.md` — read it before
"optimising" the query.

`packageUId` stays the authority and is always present; `packageName` is added beside it, never in
place of it.
