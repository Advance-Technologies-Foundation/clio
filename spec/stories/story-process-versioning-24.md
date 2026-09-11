# Story 24: Re-pin clio's guidance fixture after the process-versions split

**Feature**: process-versioning
**Jira**: ENG-94374
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: S

---

## As a

developer whose workspace templates are validated against the shipped guidance set

## I want

clio's curated-name fixture to know the article set actually published

## So that

`WorkspaceTemplateGuidanceDriftTests` validates against the real set rather than a stale one

---

## Blocked by

Story 21 being published from `clio-knowledge`. The fixture pins a GENERATION — `libraryVersion` plus
its derived `sequence` — so it can only be re-pinned to a library that exists.

## Source

Story 21 split `process-versions` into a read half (same id) and a new `process-version-writes`. The
split was forced rather than chosen: the article stood at 99.9% of the single-response budget, so no
correction fit until it was cut. `ProcessGuideResponseSizeTests` reports 70.9% and 50.7% afterwards.

## Acceptance Criteria

- [ ] **AC-01** — Given the published library, when `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` is read, then its `libraryVersion` and `sequence` name that generation
- [ ] **AC-02** — Given that fixture, when its name lists are read, then they carry `process-version-writes` and `creatio.process-version-writes` beside the existing `process-versions` entries
- [ ] **AC-03** — Given the re-pin, when `WorkspaceTemplateGuidanceDriftTests` runs, then it is green

## Implementation Notes

The fixture currently pins 1.14.2 while master already publishes 1.14.4, so it lags by design — it is
re-pinned when a story needs it to, not on every library release. This is the same step story 7
performed for the original article.

`process-versions` keeps its id, its `sourcePath` and its single `legacyUris` entry, so nothing that
already routes to it breaks; the new article is additive.
