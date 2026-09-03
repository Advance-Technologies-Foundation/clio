# Story 8: Spike: measure the version-creation mechanics on a stand

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-15
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: clio
**Status**: ready-for-dev
**Size**: L

---

## As a

developer

## I want

the open version-creation mechanics measured on a live stand before the operation is written

## So that

the create handler is written against measured behaviour instead of inferred behaviour

---

## Acceptance Criteria

- [ ] **AC-01** — Given a draft create implementation run twice against a toolkit-created root, when `SysSchemaProperty` is read, then the findings note records the observed `Version` values and `IsActiveVersion` values verbatim
- [ ] **AC-02** — Given a created version, when `SysSchema` is read, then the note records whether its `ParentId` equals the root's `SysSchema.Id`
- [ ] **AC-03** — Given one version exists, when `GetMaxProcessVersionInPackage(uc, root.Id, packageUId)` is called, then the note records the returned number for the same package and for a different package
- [ ] **AC-04** — Given a source process in a non-editable package, when the version is created, then the note names the package it landed in and closes OQ-01
- [ ] **AC-05** — Given a root whose name carries no `Usr` prefix, when the version is saved, then the note records whether `validateNamePrefixes` rejected it
- [ ] **AC-06** — Given a version was saved, when the ROOT's `SysSchemaProperty` rows are read, then the note records whether they survived
- [ ] **AC-07** — Given the stand, when `UseNewSchemaHierarchyFolding` is read, then the note records its state and closes OQ-02
- [ ] **AC-ERR** — Given any measurement contradicts this feature's ADR, when the spike ends, then the ADR is amended in the same PR and the contradiction is named in its Notes section

## Implementation Notes

Output is evidence, not shipped behaviour: a findings note plus, where a fact is non-obvious and silent, a `docs/knowledge/` record. No production code merges from this story.
Method: diff a package-created version against a designer-created version of the same source process, field by field.
Anchors (search BY NAME — the platform tree moves): `SchemaManager.SaveExtraProperties` (~`:3223`), `DesignMode/DBSchemaContentProvider.cs:294-329`, `BaseProcessSchemaManager.GetMaxProcessVersionInPackage:1285`, `Schema.cs:126` + `SchemaManager.cs:2316-2318` (`ExtendParent` renames the schema to its parent's name), `ProcessSchemaBaseElement` `IsInherited` memoisation, `base-process-schema-manager.js:88`/`:140-146`.
ENG-95335 changed package-collision reporting on schema save — measure on the current platform, not against the traps write-up.
Run schema writes SEQUENTIALLY: a parallel burst trips IIS rapid-fail and downs the .NET Framework app pool.
BLOCKER 1 as originally written is already refuted: `Assign(instance)` → `ParseObject` rewrites every extra property on each save.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Integration `[Category("Integration")]` | the measurements above, executed against a dedicated sandbox and recorded | evidence, not a suite — `[Explicit]`, self-ignoring, `[NonParallelizable]` if any harness is written |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Findings recorded in the ADR Notes and, where silent, as knowledge records
- [ ] OQ-01 and OQ-02 closed in the PRD, or restated with what is still unknown
- [ ] Code compiles without Roslyn analyzer warnings
- [ ] All new tool names and flags are kebab-case
- [ ] Unit tests use `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] No `catch (Exception)` added to clio code paths
- [ ] Knowledge records whose `applies-to` names a file this story touches are updated or deleted **in this PR** (`AGENTS.md:456-457`)
- [ ] Docs verdict stated explicitly in the PR body, including "no update required" where that is the verdict
- [ ] MCP verdict stated in the PR body ("MCP reviewed, no update required" where that applies)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
