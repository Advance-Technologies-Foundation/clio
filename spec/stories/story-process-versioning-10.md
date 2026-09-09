# Story 10: Materialise the clone through a metadata round-trip

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: in-progress
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

- [x] **AC-01** — Given a source process, when the clone is materialised, then it comes from the source's serialized metadata with the source schema UId replaced throughout and `CreatedInOwnerSchemaUId` (`BL8`) repaired back to the original
- [x] **AC-02** — Given the materialised clone, when its elements are inspected, then no element holds a reference to the source schema and none shares a `LocalizableString` instance with it
- [x] **AC-03** — Given the clone, when its `ParentSchemaUId` is read, then it equals the family root — the root's own UId when the source is the root, and the root (never the source) when the source is itself a version
- [x] **AC-04** — Given the clone, when `IsActiveVersion` and `IsDelivered` are read, then both are explicitly false, and `Version` is not the source's copied value
- [x] **AC-05** — Given the clone, when its caption is read, then it equals the source's on BOTH the manager item and the instance
- [x] **AC-06** — Given the clone was materialised, when the SOURCE instance in the manager cache is inspected, then its elements' `Outgoings`/`Incomings` counts and its `Group` resource binding are unchanged
- [x] **AC-ERR** — Given the metadata cannot be materialised, when the operation returns, then it reports `success:false` and no schema was registered

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

- [x] The diff against a designer-made version is attached to the PR — a unit test cannot establish this. **DONE at story 12**, once there was something persisted to compare: on `creatio_2` the designer's own `Save new version` produced a version identical to this build's on every `SysSchema` column and every `SysSchemaProperty` value except the number itself. Recorded in story 12's Dev Agent Record and the workspace diary.
- [x] An assertion over the SOURCE instance's `Outgoings` counts and `Group` binding exists, because a row comparison cannot see this class of defect
- [x] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [x] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [ ] PR description references this story file
- [x] `docs/process-builder-architecture.md` and `.puml` updated together
- [x] ClioRing MCP compatibility verdict recorded — the anchor `AGENTS.md:241-299` does not resolve in this repository (no `AGENTS.md`; `CLAUDE.md` is 150 lines with no ClioRing section). Verdict: no MCP surface changes, nothing for ClioRing to be compatible with until stories 16-17.

## Dev Agent Record

- Implementation started: 2026-09-04
- Implementation completed: 2026-09-04
- Tests passing: `dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf` → **944 passed, 0 failed** (10 new in `ProcessVersionCloneFactoryTests`). Build clean, 0 warnings.
- Notes:
  - **The platform's own clone helper is unreachable, but its parts are not.** `CloneSchemaUsingMetaData`
    is private and `SaveClonedSchema` is protected — and the latter deliberately RESETS versioning
    (`Version = 0`, `ParentSchemaUId = DefSchema.UId`, `IsActiveVersion = true`), so it is Copy, not
    Version. The pieces are public: `GetMetaDataSerializer` is `public override` on
    `BaseProcessSchemaManager:1114` even though `IInternalSchemaManager`, which declares it, is
    `internal`; `ProcessJsonDataReader`, `ProcessJsonMetaDataSerializer` and
    `MetaItem.ReadMetaData/WriteMetaData` are public too. Nothing in the platform is touched.
  - The serializer's envelope must be stepped into — `Read(); ReadInto(); ReadInto();` before
    `ReadMetaData` — exactly as the platform's clone path does. That framing lives in the repository, not
    in a caller.
  - **`BL8` is per ELEMENT, not per schema.** `CreatedInOwnerSchemaUId` is on `ProcessSchemaBaseElement`.
    Only the elements whose key the replace rewrote (source UId → clone UId) are repaired; ones inherited
    from an ancestor carry that ancestor's UId and repairing them would be a second defect.
  - **The family-root test is not a null check.** A non-version process carries
    `ParentSchemaUId == manager.GetDefSchemaUId()`, not `Guid.Empty`, so "the source is itself a version"
    is `parent != Empty && parent != defSchemaUId` — the predicate the platform's client composer uses. A
    server-side copy of `ParentSchemaUId` would root the family at the BASE process schema. Both halves
    are set: the schema's `ParentSchemaUId` (which the save translates into `SysSchema.ParentId`) and the
    manager item's `ParentUId` (which `GetAllVersionItems` reads).
  - **Deviation from the Test Requirements table, argued:** the clone tests live in a NEW fixture
    `ProcessVersionCloneFactoryTests` rather than in `ProcessVersionSaveHandlerTests`. AC-02 and AC-06
    need REAL `ProcessSchema` objects, so the fixture must inherit `BaseComposableAppTestFixture`, while
    the story-9 handler fixture is deliberately plain NUnit. One fixture cannot be both.
  - **`ProcessExists(Guid)` from story 9 was removed.** `FindSchemaItem(Guid)` is the same `FindItemByUId`
    pre-validation and the handler needs the item anyway; two members for one question was one too many.
    AC-ERR of story 9 still holds through the surviving member.
  - Runtime self-verification not performed: this story persists nothing and the operation is still
    unreachable through clio. It becomes meaningful at story 12.
