# Story 13: SetActiveProcessVersion with a verified read-back

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-08, FR-09
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: ready-for-dev
**Size**: L

---

## As a

no-code builder

## I want

an operation that makes a named version the actual one and proves it took effect

## So that

a wrong change can be rolled back without trusting an unverified success

---

## Acceptance Criteria

- [ ] **AC-01** — Given a family member, when the operation runs, then the manager reports that member as the active version afterwards and the response carries the read-back value
- [ ] **AC-02** — Given the read-back does not match the request, when the operation returns, then it reports `success:false` naming the member that is actually active
- [ ] **AC-03** — Given siblings whose deactivation failed, when the operation returns, then `deactivationFailureCount` is non-zero, `warnings` says so, and the result is not reported as plain success
- [ ] **AC-04** — Given the requested version is already active, when the operation runs, then it succeeds without changing state
- [ ] **AC-05** — Given a family large enough that every sibling is re-saved, when the operation returns, then `warnings` reports the number of schemas re-saved
- [ ] **AC-06** — Given the caller lacks `CanManageProcessDesign`, when the operation runs, then it is refused before any write
- [ ] **AC-ERR** — Given a version identity that does not resolve, when the operation runs, then it returns `success:false` naming the identity

## Implementation Notes

Shape: `_guard.EnsureCanManageProcessDesign()` → `Manager.FindItemByUId(...)` → `Manager.SetActiveVersionItem(target)` (public virtual) → **mandatory** `Manager.GetActiveVersionItem(target)` read-back → `Success = nowActive?.UId == target.UId`.
The read-back is not defensive: `BaseProcessSchemaManager.cs:519` logs and swallows the deactivation failure of every sibling it turns off, so a partial activation leaves two members flagged active with `PackagePosition` deciding the winner.
Read back through the MANAGER, not the view: the manager's per-root cache is invalidated by the event this path raises.
Do NOT wrap the platform's `SetIsActualVersion` REST op — it has no authorization check and is reachable as a single POST with a query parameter.
`SetActiveVersionItem` re-saves every sibling inside one transaction; that is what the family-size warning reports.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | guard ordering, read-back verdict, swallowed-failure count and warning, already-active no-op, family-size warning, not-found | `tests/CrtProcessBuilder/ProcessVersionActivateHandlerTests.cs` |
| Unit `[TestFixture(Category = "UnitTests")]` (package-repo convention) | operation count and BodyStyle | `tests/.../ProcessDesignServiceWireContractTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`

## Definition of Done

- [ ] Operation count and gate call sites move together, expressed as deltas
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
