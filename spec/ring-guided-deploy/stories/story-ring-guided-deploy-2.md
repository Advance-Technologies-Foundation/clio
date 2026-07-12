# Story 2: Deploy stage-event seam — IStageEventSource + emitter in CreatioInstallerService

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio` (foundation)
**FR coverage**: FR-05 (deploy stage list), FR-13 (instrument `CreatioInstallerService.Execute`), FR-15 (redaction at emission boundary)
**AC coverage**: AC-05 (ordered stages w/ status/duration/message/detail), AC-10 (failure cascade), AC-12 (secret redaction at source)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D3)
**Status**: review
**Size**: L (full day)
**Depends on**: story-ring-guided-deploy-1
**Blocks**: story-ring-guided-deploy-4

---

## As a

developer instrumenting the deploy pipeline

## I want

an `IStageEventSource` seam plus a `StageEventEmitter` that `CreatioInstallerService` uses to raise a manifest, per-stage transitions, a failure cascade, and a run-completed event — with a single redaction boundary

## So that

the MCP tool (story 4) can subscribe to one uniform typed event surface and stream honest, secret-free deploy progress derived from the real execution path

---

## Acceptance Criteria

- [ ] **AC-01** — Given a resolved deploy execution path, when `Execute` begins, then a single `manifest` event is emitted first listing every stage that will run (`stage-build`→`unzip`→`copy-files`→`restore-db`→`deploy-app`→`configure-conn-strings`→`register-env`→`wait-ready`), with `total` = manifest length and `stage-build` flagged `conditional=true` when the source is not a network drive.
- [ ] **AC-02** — Given each deploy stage runs, when it starts and completes, then the emitter raises `stage` events `running` then `done` in order, with `index`/`total` matching the manifest, `startedAtUtc` set on `running`, and `durationMs` set on `done`.
- [ ] **AC-03** — Given `stage-build` is inert because the source is not a network drive, when the manifest runs, then that stage is emitted `status=skipped` with `skipReason=not-applicable` (distinct from failure-cascade skips).
- [ ] **AC-04** — Given a stage throws, when the emitter wrapper catches it, then the active stage is emitted `failed` (with `detail`/`errorCode`), every remaining manifest stage is emitted `skipped` with `skipReason=after-failure`, then a `run-completed` event with `outcome=failure` is emitted — in that order.
- [ ] **AC-05** — Given a fully successful deploy, when the last stage completes, then a `run-completed` event with `outcome=success` and a friendly `summary` (plus `derivedUrl`/`derivedPath` where known) is emitted.
- [ ] **AC-06** — Given the emitter raises any event, when a redaction guard inspects `message`/`detail`/`errorCode`, then no connection string, credential, or token appears in any field (deny-list assertion passes); `errorCode` is a stable symbolic code and `detail` is non-secret technical context.
- [ ] **AC-07** — Given `CreatioInstallerService`, when cast to `IStageEventSource`, then subscribing to `StageChanged` receives all of the above events with a stable per-run `runId` and a monotonically increasing `sequence`.
- [ ] **AC-ERR** — Given no subscriber is attached (`StageChanged` has no handlers), when `Execute` runs, then deploy behavior is byte-for-byte unchanged (emission is inert, never breaks the operation).

## Implementation Notes

From ADR "files to create/modify" + D3 (deploy half):

- `clio/Command/McpServer/Progress/IStageEventSource.cs` (new) — `interface IStageEventSource { event EventHandler<ClioStageEvent> StageChanged; }`.
- `clio/Command/McpServer/Progress/StageEventEmitter.cs` (new) — holds per-run `runId` + `sequence`; builds the manifest from the resolved execution path (NOT a hardcoded list — A-02); provides a `Run(stageId, action)` wrapper emitting `running`/`done`/`failed` and driving the failure cascade + `run-completed`; is the **single redaction boundary** (deny-list guard over every field). This is behavior → register in DI and inject; `ClioStageEvent` (DTO) may be `new`'d inside it.
- `clio/Command/CreatioInstallCommand/CreatioInstallerService.cs` (modify) — implement `IStageEventSource`; wrap the 8 deploy stages of `Execute` (ADR fact 7 / FR-05) with the emitter transitions. Do not rewrite stage logic — instrument only.
- `clio/BindingsModule.cs` (modify) — register the emitter / `IStageEventSource` wiring if not covered by existing command registration.
- Manifest built up front from the network-source decision (known before `Execute` begins) so it cannot misrepresent runtime order.

Key files: `clio/Command/CreatioInstallCommand/CreatioInstallerService.cs`, `clio/Command/McpServer/Progress/StageEventEmitter.cs`, `IStageEventSource.cs`
Pattern to follow: `StartCommand.StatusChanged` typed-event seam (ADR fact 1); failure/redaction handled in the emitter wrapper, not scattered in stage bodies.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | manifest built from resolved path; conditional `stage-build` skip; running/done ordering; failure cascade (active=failed, rest=skipped after-failure, then run-completed); sequence monotonicity; inert when no subscriber | `clio.tests/Command/McpServer/StageEventEmitterTests.cs` |
| Unit `[Category("Unit")]` | redaction deny-list guard rejects secrets in `message`/`detail`/`errorCode` | `clio.tests/Command/McpServer/StageEventEmitterTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`. NSubstitute for collaborators; prefer `BaseCommandTests<PfInstallerOptions>` for the command surface.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=McpServer|Module=CreatioInstallCommand)"`

## Definition of Done

- [ ] Code compiles without new `CLIO*` warnings; behavior classes resolved via DI (no `new` except DTO records)
- [ ] No CLI flags introduced
- [ ] Manifest generated from the resolved execution path, not hardcoded
- [ ] Redaction boundary + deny-list assertion in place; no secret reaches any event field
- [ ] Unit tests added with `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] Emission is inert with no subscriber (happy-path deploy behavior unchanged)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
