# Story 11: Apply the edits to the clone through the shared applier

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-16
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: ready-for-dev
**Size**: M

---

## As a

no-code builder

## I want

my edit list applied to the new version by the same code that applies it in place

## So that

an operation that works on a process works the same when saving a new version

---

## Acceptance Criteria

- [ ] **AC-01** — Given a list of edits, when the operation runs, then every descriptor is applied to the CLONE through the same `IProcessOperationExecutor` the in-place path uses, and the applied count is reported
- [ ] **AC-02** — Given the shared pipeline, when it runs, then it performs the same post-loop steps in the same order as the in-place path: primary lane, executor loop, layout, `InitializeLocalizableValues()`, `EnsureValidForSave`
- [ ] **AC-03** — Given one operation in the list is rejected, when the operation returns, then it reports `success:false` and **nothing was persisted**
- [ ] **AC-04** — Given an empty or absent edit list, when the operation runs, then the clone is left exactly as materialised
- [ ] **AC-05** — Given edits that add or remove flows and elements, when they have been applied, then the SOURCE instance's element graph is unchanged
- [ ] **AC-ERR** — Given `EnsureValidForSave` rejects the edited clone, when the operation returns, then it reports `success:false` naming the validation failure and nothing was persisted

## Implementation Notes

ADR choice 13. `ProcessModifyHandler.ModifyProcess` (`Design/ProcessModifyHandler.cs:55-110`) is: resolve → open design session → get design instance → primary lane → `foreach` descriptor `_operationExecutor.Apply(schema, lane, operation)` → `_layoutEngine.Apply(schema)` → `schema.InitializeLocalizableValues()` → `_schemaValidator.EnsureValidForSave(schema)` → save. Extract the middle (lane through validation) into one implementation both handlers call, and pin it with a test asserting both run the same executor instance.
The guard at `:61` stays OUTSIDE the extracted middle — each handler keeps its own `EnsureCanManageProcessDesign()` call, which is what keeps the authorization-gate call-site count at +2 rather than +1.
`InitializeLocalizableValues()` is only safe to share because story 10 removed the `Group` aliasing; if story 10 is not merged first, this step repoints the source's binding.
Atomicity is inherited, not built: nothing is persisted until story 12's save, so a rejected descriptor leaves no schema at all. Assert it rather than assume it.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | shared-executor delegation, post-loop order, atomic abort on a rejected descriptor, empty-list no-op, source graph untouched | `tests/CrtProcessBuilder/ProcessVersionSaveHandlerTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] A test pins that both modify paths delegate to the same `IProcessOperationExecutor`
- [ ] The extracted middle excludes the authorization guard
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
