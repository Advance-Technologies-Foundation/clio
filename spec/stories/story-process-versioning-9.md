# Story 9: ModifyProcessAsNewVersion contracts, guard and transport

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-09, FR-14
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

the operation's contracts, its authorization guard and its service entry point

## So that

the clone-and-edit logic lands on a shape that is already wired and tested

---

## Acceptance Criteria

- [ ] **AC-01** — Given the service, when its contracts are reflected, then `ModifyProcessAsNewVersion` is present with `BodyStyle = WebMessageBodyStyle.Wrapped` like every sibling
- [ ] **AC-02** — Given the request contract, when it is inspected, then it carries the source identity, an optional `packageName` and the **same** `List<ProcessOperationDescriptor>` shape `ModifyProcessRequest` takes
- [ ] **AC-03** — Given the caller lacks `CanManageProcessDesign`, when the operation runs, then it is refused before any repository call
- [ ] **AC-04** — Given a request with neither `name` nor `uid`, when the operation runs, then it returns `success:false` naming the missing identity
- [ ] **AC-05** — Given the wire-contract test, when it runs, then the expected operation count is the previous baseline plus one
- [ ] **AC-ERR** — Given an identity that does not resolve, when the operation runs, then it returns `success:false` naming the identity — never an unhandled `ItemNotFoundException` (pre-validate with `FindItemByUId`, never `GetItemByUId`)

## Implementation Notes

New: `Files/src/cs/Contracts/VersionContracts.cs` (shapes in the ADR), `Files/src/cs/Design/ProcessVersionSaveHandler.cs` (guard + transport only at this stage), one `[OperationContract]` after `Ping` (`ProcessDesignService.cs:122-129`), `IProcessDesigner`/`ProcessDesigner.cs` gains the use case, `CrtProcessBuilderApp.cs` gains an `AddScoped` through the `Connection(sp)` helper — the container validates scopes.
Reuse `ProcessOperationDescriptor` itself; do not clone the type. The whole point of this shape is that one descriptor list serves both entry points.
Follow `ProcessModifyHandler` for the response contract: `success` / `errorMessage` / `appliedOperations` / `warnings`. Not `ErrorOr` — referenced but used by no source file.
Express the wire-contract pin as a delta: two other stories rebundle the same package and move the baseline first.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | guard call ordering, identity validation, descriptor reuse, contract reflection | `tests/CrtProcessBuilder/ProcessVersionSaveHandlerTests.cs` |
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | operation count and per-operation `BodyStyle` | `tests/.../ProcessDesignServiceWireContractTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Operation count and gate call sites move together, in this commit
- [ ] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [ ] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [ ] `docs/process-builder-architecture.md` and `.puml` updated together
- [ ] ClioRing MCP compatibility verdict recorded (`AGENTS.md:241-299`)
- [ ] PR description references this story file

## Dev Agent Record

{Left blank — filled by dev agent during implementation}
- Implementation started: 
- Implementation completed: 
- Tests passing: 
- Notes: 
