# Story 13: SetActiveProcessVersion with a verified read-back

**Feature**: process-versioning
**Jira**: ENG-94374
**FR coverage**: FR-08, FR-09
**PRD**: [prd-process-versioning.md](../prd/prd-process-versioning.md)
**ADR**: [adr-process-versioning.md](../adr/adr-process-versioning.md)
**Test plan**: [tp-process-versioning.md](../test-plans/tp-process-versioning.md)
**Repository**: crt-process-builder
**Status**: in-progress
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

- [x] **AC-01** — Given a family member, when the operation runs, then the manager reports that member as the active version afterwards and the response carries the read-back value
- [x] **AC-02** — Given the read-back does not match the request, when the operation returns, then it reports `success:false` naming the member that is actually active
- [x] **AC-03** — Given siblings whose deactivation failed, when the operation returns, then `deactivationFailureCount` is non-zero, `warnings` says so, and the result is not reported as plain success
- [x] **AC-04** — Given the requested version is already active, when the operation runs, then it succeeds without changing state
- [x] **AC-05** — Given a family large enough that every sibling is re-saved, when the operation returns, then `warnings` reports the number of schemas re-saved
- [x] **AC-06** — Given the caller lacks `CanManageProcessDesign`, when the operation runs, then it is refused before any write
- [x] **AC-ERR** — Given a version identity that does not resolve, when the operation runs, then it returns `success:false` naming the identity

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

- [x] Operation count and gate call sites move together, expressed as deltas — `VersioningOperationContractCount` 1 → 2 (baseline 5 + 2 = 7), gate call sites 4 → 5, same commit
- [x] Code compiles clean; tests use fixture-level `[TestFixture(Category = "UnitTests")]` — this repo's convention, and the opposite of clio's
- [x] Workspace-diary entry added (`CLAUDE.md:131-150`) — mandatory in this repo
- [ ] PR description references this story file
- [x] `docs/process-builder-architecture.md` and `.puml` updated together
- [x] ClioRing MCP compatibility verdict recorded — the anchor `AGENTS.md:241-299` does not resolve in this repository. Verdict: no MCP surface changes, nothing for ClioRing to be compatible with until stories 16-17.

## Dev Agent Record

- Implementation started: 2026-09-04
- Implementation completed: 2026-09-04
- Tests passing: `dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf` → **965 passed, 0 failed** (8 new in `ProcessVersionActivateHandlerTests`). Build clean, 0 warnings.
- Notes:
  - **The read-back is the operation, not a precaution around it.** `SetActiveVersionItem` turns off every
    sibling inside one transaction and logs-and-swallows each failure, so a caller trusting the absence of
    an exception could be told the rollback landed while two members stay flagged active and package ORDER
    decides which runs.
  - **A swallowed sibling deactivation is reported as a FAILURE, not a warning.** The read-back can name
    the requested version as active while a sibling is still flagged; the response then carries
    `deactivationFailureCount` and `success:false`, because the PRD's counter says a partial activation
    must not be reported as success and "success with a note" is exactly that.
  - **`GetActiveVersionItemByUId` resolves through the throwing `GetItemByUId`**, so the read-back uses
    `GetActiveVersionItem(item)` on the item the caller already found. Nothing here wraps the platform's
    own `SetIsActualVersion` REST op, which has no authorization check at all.
  - Already-active is a no-op: re-running the write would re-save the whole family to reach the state it
    is already in. Confirmed on the stand — the second activation returned success with NO warning.
  - **VERIFIED ON A STAND** (`creatio_2`, port 40002, core 10.2.20.0), after redeploying the package:
    - Activating `UsrVerifyStory12Custom1` → `success:true`, read-back names it,
      `deactivationFailureCount:0`, warning *"Activating this version re-saved all 4 schemas in the
      version family."* — AC-01 and AC-05 observed.
    - Re-activating the same version → `success:true`, **no** warning — AC-04 observed, nothing re-saved.
    - Rolling back to the root → `success:true`, root named in the read-back — the rollback gesture the
      whole feature exists for, working end to end.
    - An identity that does not resolve → `success:false` naming it, no version facts — AC-ERR observed.
    - The READ half then reports the root active at 0 with all three versions inactive, so the rollback is
      visible through `describe-business-process` as well.
    - AC-02 and AC-03 stay unit-only: a read-back that disagrees and a swallowed deactivation cannot be
      provoked on a healthy stand without corrupting it. Both are covered by substituted tests.
  - **Test-harness trap worth carrying forward:** NSubstitute silently loses a configuration when a call on
    one substitute happens while CONFIGURING another (`_repo.FindSchemaItem(target.UId).Returns(target)`
    reads `target.UId` mid-configuration). The symptom is an auto-substitute coming back later — an empty
    string where a name was expected — not an error. Hoist every substitute read into a local first.
  - **An open designer tab took the stand down for ~25 minutes** after the redeploy, and it was not the
    install: `appcmd list requests` showed ten `GET /0/Nui/ViewModule.aspx.ashx` each running 1.5 million
    ms — the classic designer's long-poll connections from a Chrome tab left open. Closing it drained them
    and `/0/ping` answered 200 at once. Flat `w3wp` CPU is what told a blocked pipeline apart from a
    compile; a cumulative CPU figure read as current load is the trap.
