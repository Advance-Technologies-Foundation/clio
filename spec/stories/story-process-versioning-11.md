# Story 11: Apply the edits to the clone through the shared applier

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-07, FR-16
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: done
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

- [x] **AC-01** — Given a list of edits, when the operation runs, then every descriptor is applied to the CLONE through the same `IProcessOperationExecutor` the in-place path uses, and the applied count is reported
- [x] **AC-02** — Given the shared pipeline, when it runs, then it performs the same post-loop steps in the same order as the in-place path: primary lane, executor loop, layout, `InitializeLocalizableValues()`, `EnsureValidForSave`
- [x] **AC-03** — Given one operation in the list is rejected, when the operation returns, then it reports `success:false` and **nothing was persisted**
- [x] **AC-04** — Given an empty or absent edit list, when the operation runs, then the clone is left exactly as materialised
- [x] **AC-05** — Given edits that add or remove flows and elements, when they have been applied, then the SOURCE instance's element graph is unchanged
- [x] **AC-ERR** — Given `EnsureValidForSave` rejects the edited clone, when the operation returns, then it reports `success:false` naming the validation failure and nothing was persisted

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

- [x] A test pins that both modify paths delegate to the same `IProcessOperationExecutor` — `BothModifyPaths_ShouldReachTheSameExecutor_WhenEachAppliesAnEdit`
- [x] The extracted middle excludes the authorization guard
- [x] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [x] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [x] PR description references this story file
- [x] `docs/process-builder-architecture.md` and `.puml` updated together
- [x] ClioRing MCP compatibility verdict recorded — the anchor `AGENTS.md:241-299` does not resolve in this repository. Verdict: no MCP surface changes, nothing for ClioRing to be compatible with until stories 16-17.

## Dev Agent Record

- Implementation started: 2026-09-04
- Implementation completed: 2026-09-04
- Tests passing: `dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf` → **954 passed, 0 failed** (10 new: 6 in `ProcessEditPipelineTests`, 4 in `ProcessVersionSaveHandlerTests`). Build clean, 0 warnings.
- Notes:
  - `ProcessModifyHandler` kept the session lifecycle, the gate and the notices; the middle moved to
    `IProcessEditPipeline` / `ProcessEditPipeline`. Its constructor went from seven collaborators to four.
  - **The guard stays out of the extracted middle**, as the story requires. A gate inside the shared
    pipeline would let a new handler inherit one it never declared, and would decouple the gate call-site
    count from the operation count — which clio pins against the shipped archive from the other side.
  - **The refusal reports nothing about the work done.** The handler runs the pipeline and still refuses,
    and deliberately does NOT set `appliedOperations` or `warnings` on that response: `success:false`
    beside `appliedOperations: 3` reads as three edits having landed somewhere, when they landed in an
    object about to be discarded. Both members belong to a SAVED version, so story 12 populates them.
  - **The existing fixtures were not rewritten, and should not have been.** `ProcessModifyHandlerTests`
    and `ProcessDesignerRoundTripTests` now construct a REAL `ProcessEditPipeline` over the same
    substitutes they already had, so every assertion about the executor, layout and validator keeps
    working verbatim. Substituting the pipeline in them would have deleted that coverage rather than
    relocating it.
  - **Correction to story 10's deviation:** `ProcessVersionSaveHandlerTests` is no longer plain NUnit. The
    handler now hands a real `ProcessSchema` to the pipeline and `ProcessSchema`'s constructor throws on a
    null manager, so the fixture inherits `BaseComposableAppTestFixture`. The story-9/10 justification for
    staying harness-free stopped being true; the fixture moved rather than the argument being kept alive.
  - AC-05 is asserted at pipeline level (`Apply_ShouldLeaveAnotherSchemaUntouched_WhenTheCloneIsEdited`):
    an operation that adds an element to the clone leaves the other schema's `FlowElements` and its
    element's `Outgoings` unchanged. A row comparison cannot see this class of defect, which is why it is
    asserted over instances.
  - Runtime self-verification not performed: still nothing persisted and still unreachable through clio.
    It becomes meaningful at story 12.
