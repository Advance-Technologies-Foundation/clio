# Story 10: Materialise the clone through a metadata round-trip

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: ready-for-dev
**Size**: M

---

## As a

developer

## I want

the clone produced from the source's metadata rather than from an object-graph copy

## So that

editing the clone cannot reach into the live source instance

---

## Acceptance Criteria

- [ ] **AC-01** — Given a source process, when the clone is materialised, then it comes from the source's serialized metadata with the source schema UId replaced throughout and `CreatedInOwnerSchemaUId` (`BL8`) repaired back to the original
- [ ] **AC-02** — Given the materialised clone, when its elements are inspected, then no element holds a reference to the source schema and none shares a `LocalizableString` instance with it
- [ ] **AC-03** — Given the clone, when its `ParentSchemaUId` is read, then it equals the family root — the root's own UId when the source is the root, and the root (never the source) when the source is itself a version
- [ ] **AC-04** — Given the clone, when `IsActiveVersion` and `IsDelivered` are read, then both are explicitly false, and `Version` is not the source's copied value
- [ ] **AC-05** — Given the clone, when its caption is read, then it equals the source's on BOTH the manager item and the instance
- [ ] **AC-06** — Given the clone was materialised, when the SOURCE instance in the manager cache is inspected, then its elements' `Outgoings`/`Incomings` counts and its `Group` resource binding are unchanged
- [ ] **AC-ERR** — Given the metadata cannot be materialised, when the operation returns, then it reports `success:false` and no schema was registered

## Implementation Notes

ADR choice 17 is the whole story. Do **not** use `ProcessSchema.Clone()` / the copy constructor: cloned elements keep `ProcessSchema` pointing at the source (`ProcessSchemaBaseElement.cs:86`), the collection's repair never fires because `ProcessSchema.cs:149-153` sets the parent in an object initializer that runs after the constructor body has already added every element, and a sequence flow's `SourceRefUId` setter then *writes* into the source's `Outgoings` (`ProcessSchemaSequenceFlow.cs:143-152`). The source instance is app-cached (`Manager.cs:320-329`), so that corruption outlives the request while the source's database row stays byte-for-byte identical.
`Group` is aliased outright by the same copy path (`ProcessSchema.cs:143` + `LocalizableString.cs:270-272`), and `InitializeLocalizableValues()` (`:1412`) then rebinds the SOURCE's binding to the clone's resources.
The metadata route avoids both, and it is what the platform itself does: `CreateSchemaCopy:929-942` calls `Clone()` and discards it in favour of `CloneSchemaUsingMetaData:532-555`. Repair `BL8` afterwards — that key is the one the blunt replace gets wrong, and repairing it is what makes this a version rather than a copy.
`Clone()` also silently drops or mis-sets: `Id`, `Associations`, `OwnerSchema`/`IsEmbedded`, `Enabled`, `UseForceCompile`, `CompiledMethodsBody`, `IsCoreSchema`, `ReferenceSchemaUIds`, `ManagerItem`; plus `IsActiveVersion` defaulting to true, `Version` copied verbatim and `CreatedInVersion` overwritten with the running assembly version. Enumerate the normalisation rather than inferring it.
Nothing here saves anything; story 11 edits the clone and story 12 persists it.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | metadata replace scope, `BL8` repair, no shared references, root re-parenting from a non-root source, explicit flags, source instance untouched | `tests/CrtProcessBuilder/ProcessVersionSaveHandlerTests.cs` |
| Integration `[Category("Integration")]` | field-by-field diff against a designer-created version of the same source | manual on a dedicated sandbox, recorded in the PR |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] The diff against a designer-made version is attached to the PR — a unit test cannot establish this
- [ ] An assertion over the SOURCE instance's `Outgoings` counts and `Group` binding exists, because a row comparison cannot see this class of defect
- [ ] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [ ] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [ ] PR description references this story file
- [ ] `docs/process-builder-architecture.md` and `.puml` updated together
- [ ] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`)

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
