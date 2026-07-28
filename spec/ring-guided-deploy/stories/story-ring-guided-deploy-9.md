# Story 9: Guided Uninstall on the main ring — local-env picker + Yes/No confirm + pipeline

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio\clio-ring` (branch `spike/ring-clio-ipc`)
**FR coverage**: FR-01 (Uninstall on main radial ring), FR-06 (local-env pick → Uninstall → Yes/No → pipeline), FR-21 (no agent-initiated real op)
**AC coverage**: AC-01 (Uninstall on main ring), AC-06 (simple Yes/No, No cancels, Yes runs pipeline), AC-16 (no agent initiation)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D5, D8)
**Status**: review
**Size**: M (half day)
**Depends on**: story-ring-guided-deploy-7
**Blocks**: —

---

## As a

developer (Ring user)

## I want

Uninstall as a primary main-ring action that lets me pick a local environment, confirm with a simple "Are you sure? Yes/No", and then runs the same step pipeline

## So that

I can tear down a local instance safely and quickly, without typing an exact name and without a raw console

---

## Acceptance Criteria

- [ ] **AC-01** — Given the main radial ring, when opened, then "Uninstall" is present as a primary ring action (not tray/taskbar) and the full flow completes from the ring (AC-01).
- [ ] **AC-02** — Given the Uninstall flow, when opened, then it shows a picker of **local** registered environments (sourced from clio `list-environments`, local-filtered, matching the `uninstall-creatio` `environment-name` contract) (OQ-04).
- [ ] **AC-03** — Given the user selects an environment and clicks Uninstall, when confirmation appears, then it is a simple "Are you sure? Yes/No" — NO exact-name typing (AC-06).
- [ ] **AC-04** — Given the confirmation, when the user clicks No, then it cancels with no changes and no clio call is made (AC-06).
- [ ] **AC-05** — Given the confirmation, when the user clicks Yes, then the same `DeployPipelineViewModel` step pipeline runs the FR-07 uninstall stages (AC-06).
- [ ] **AC-06** (FR-21/AC-16) — Given any agent (AI/automation), when no user Yes click occurred, then no real uninstall runs — the Ring is the sole initiator and only on the explicit Yes click.
- [ ] **AC-ERR** — Given the config-read stage fails during uninstall (clio correction 1, story 3), when the pipeline renders, then the "Read configuration" step shows Failed, the run terminates as failure, and the environment is NOT shown as unregistered (honest reporting end-to-end).

## Implementation Notes

From ADR D5 (Uninstall Yes/No, local-env picker, main-ring entry) + D8 (safety):

- `ClioRing/**` — add the Uninstall main-ring entry + flow: local-environment picker → Uninstall → "Are you sure? Yes/No" → Yes runs the shared `DeployPipelineViewModel` pipeline against the `uninstall-creatio` tool (story 6 typed `CallToolAsync`); No cancels.
- Local-environment list (OQ-04): from clio's registered environments (`list-environments`), filtered to local, matching the `environment-name` contract. Orphan IIS-site discovery is out of scope.
- Safety (D8): real `uninstall-creatio` invoked ONLY on the user's Yes click; no agent auto-invocation. Tool stays `Destructive=true` on the clio side.

Key files: `ClioRing/` (Uninstall ring entry + picker + confirm view/VM), reuses `DeployPipelineViewModel` (story 7) and `CallToolAsync(IProgress<ClioStageEvent>)` (story 6)
Pattern to follow: existing Ring radial-action + confirm-dialog patterns.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit (`ClioRing.Tests`) | picker lists local registered envs; Yes/No confirm (no exact-name typing); No ⇒ no clio call; Yes ⇒ uninstall tool invoked once; config-read failure renders Failed + not-unregistered | `ClioRing.Tests/UninstallFlowViewModelTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`. Mock the IPC client + env source.

## Definition of Done

- [ ] Uninstall is a primary main-ring action (not tray); full flow completes from the ring
- [ ] Local registered-env picker (matching `environment-name`); simple Yes/No confirm, no exact-name typing
- [ ] No cancels with no changes; Yes runs the shared step pipeline (FR-07 stages)
- [ ] Real uninstall starts ONLY on the user's Yes click (no agent initiation)
- [ ] Config-read failure surfaces honestly (Failed + not-unregistered) end-to-end
- [ ] Unit tests green; AAA + `because` + `[Description]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
