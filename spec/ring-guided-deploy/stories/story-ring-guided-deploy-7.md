# Story 7: DeployPipelineViewModel + GitHub-Actions step-list view

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio\clio-ring` (branch `spike/ring-clio-ipc`)
**FR coverage**: FR-04 (step pipeline UI), FR-05 (deploy step mapping), FR-07 (uninstall step mapping)
**AC coverage**: AC-05 (ordered steps w/ status/duration/message/expander), AC-09 (manifest-driven, no fabricated pending), AC-10 (failure cascade rendering), AC-ERR (terminal failure = one message + corrective action + detail behind expander)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D5)
**Status**: review
**Size**: L (full day)
**Depends on**: story-ring-guided-deploy-6
**Blocks**: story-ring-guided-deploy-8, story-ring-guided-deploy-9

---

## As a

developer (Ring user) watching a deploy or uninstall

## I want

a GitHub-Actions-style step pipeline that builds its step list from the `manifest` event and updates each step from `stage`/`run-completed` events

## So that

I can see exactly which stage is running, how long each took, a friendly message, and expand technical detail on failure — all derived from typed events, never console text

---

## Acceptance Criteria

- [ ] **AC-01** — Given a `manifest` event, when the pipeline renders, then the step list is built from the manifest (one step per manifest stage, in order) with each step initially `Pending` — NO fabricated per-step "pending" events are synthesized (AC-09).
- [ ] **AC-02** — Given ordered `stage` events, when they arrive, then each step transitions Pending→Running→Done/Failed/Skipped, showing a duration once complete, a friendly `message`, and an expander exposing `detail`/`errorCode` (AC-05).
- [ ] **AC-03** — Given a stage fails, when events arrive, then the active step shows Failed, remaining steps show Skipped, the terminal state reflects `run-completed outcome=failure`, and exactly ONE human message + one corrective action is shown with technical detail behind the expander (AC-10 / AC-ERR).
- [ ] **AC-04** — Given a `run-completed outcome=success`, when it arrives, then the pipeline shows terminal success with the friendly summary and any `derivedUrl`/`derivedPath`; no error affordance/expander noise on the happy path.
- [ ] **AC-05** — Given `stage` events for skipped-by-condition (`skipReason=not-applicable`) vs skipped-after-failure (`skipReason=after-failure`), when rendered, then the two are visually distinguishable (not conflated).
- [ ] **AC-06** — Given deploy steps, when rendered, then they map 1:1 to the FR-05 clio stages; given uninstall steps, then they map 1:1 to the FR-07 clio stages (VM is operation-agnostic, driven by the manifest).
- [ ] **AC-ERR** — Given intermediate `stage` events are lost but the `manifest` and `run-completed` arrive, when the VM reconciles, then it reflects the terminal outcome against the manifest rather than stalling (A-01 mitigation).

## Implementation Notes

From ADR D5 (pipeline VM + view):

- `ClioRing/**` (new) — `DeployPipelineViewModel`: builds steps from the `manifest` event; updates status/duration/message/detail from `stage` events; renders terminal state from `run-completed`. Subscribes to the typed `ClioStageEvent` stream from story 6's `CallToolAsync(…, IProgress<ClioStageEvent>)` overload.
- A GitHub-Actions-style step-list view: Pending / Running / Done / Failed / Skipped, duration, friendly message, expander for `detail`/`errorCode`.
- VM is operation-agnostic (same VM/view for deploy and uninstall) — the manifest defines the steps.
- Avalonia MVVM; keep the VM unit-testable (feed it a synthetic `ClioStageEvent` stream).

Key files: `ClioRing/` new `DeployPipelineViewModel` + step-list view
Pattern to follow: existing Ring VMs/views; consume the typed event stream from `ClioRing.Ipc` (story 6).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit (`ClioRing.Tests`) | manifest builds step list (no fabricated pending); Pending→Running→Done transitions; failure cascade rendering; not-applicable vs after-failure distinction; reconcile-against-manifest when intermediate events lost | `ClioRing.Tests/DeployPipelineViewModelTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`. Feed a synthetic typed event stream (no real clio process in unit tests).

## Definition of Done

- [ ] Step list built from the `manifest` (no fabricated per-step pending events)
- [ ] Per-step status/duration/message + expander for `detail`/`errorCode`
- [ ] Failure cascade + terminal outcome rendered; one message + corrective action on failure
- [ ] not-applicable vs after-failure skips visually distinct
- [ ] VM unit-tested with a synthetic typed event stream; AAA + `because` + `[Description]`
- [ ] JIT-only / non-AOT preserved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
