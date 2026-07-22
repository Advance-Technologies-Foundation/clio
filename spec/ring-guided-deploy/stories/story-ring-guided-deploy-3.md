# Story 3: Uninstall stage-event seam — CreatioUninstaller + UninstallCreatioCommand incl. 3 corrections

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio` (foundation)
**FR coverage**: FR-07 (uninstall stage list + corrections), FR-14 (instrument uninstall path + two corrections)
**AC coverage**: AC-07 (config-read FAILED + safe abort), AC-08 (AppPool profile skipped/not-supported), AC-10 (failure cascade)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D3)
**Status**: review
**Size**: L (full day)
**Depends on**: story-ring-guided-deploy-1
**Blocks**: story-ring-guided-deploy-4

---

## As a

developer instrumenting the uninstall pipeline

## I want

`UninstallCreatioCommand` to implement `IStageEventSource` and re-raise typed stage events from `CreatioUninstaller` — including the three signed-off corrections — reusing the same emitter seam as deploy

## So that

the MCP tool subscribes to one uniform command instance for both operations, and uninstall reports honest failure instead of silently reporting success

---

## Acceptance Criteria

- [ ] **AC-01** — Given an uninstall run, when it begins, then a `manifest` event lists the uninstall stages in order (`read-config`→`stop-iis`→`delete-iis`→`drop-db`→`delete-files`→`unregister`), with configuration validated before IIS is stopped and `unregister` positioned as the final stage that runs only after cleanup succeeds.
- [ ] **AC-02** — Given each uninstall stage runs, when it starts/completes, then `running`/`done` `stage` events are emitted in order with `index`/`total`/`durationMs`, identical envelope shape to deploy.
- [ ] **AC-03** (correction 1) — Given reading configuration fails, when the `read-config` stage runs, then it is emitted `status=failed` (with `detail`/`errorCode`), the environment is **NOT** unregistered, the run is **NOT** reported success, and a `run-completed` with `outcome=failure` follows (safe abort — AC-07).
- [ ] **AC-04** (correction 2) — Given an AppPool profile exists but deletion is unsupported, when the `delete-apppool-profile` stage runs, then it is emitted `status=skipped` with `skipReason=not-supported` — never silently succeeded (AC-08). When no profile exists the stage is absent from the manifest.
- [ ] **AC-05** (correction 3) — Given cleanup stages all succeed, when the run reaches `unregister`, then `unregister` runs as the final stage only after prior cleanup succeeded, then `run-completed` with `outcome=success` is emitted.
- [ ] **AC-06** — Given any uninstall stage throws, when the emitter wrapper catches it, then active=`failed`, remaining=`skipped` (`skipReason=after-failure`), then `run-completed outcome=failure` — same cascade as deploy.
- [ ] **AC-07** — Given `UninstallCreatioCommand`, when cast to `IStageEventSource`, then `StageChanged` yields all events with the command-owned `runId`/`sequence`/manifest (the command owns run identity; `CreatioUninstaller` supplies a lightweight stage callback).
- [ ] **AC-ERR** — Given no subscriber, when uninstall runs, then behavior is unchanged except the two corrections above (config-read FAILED+abort and profile skipped/not-supported are honest-reporting fixes, applied regardless of subscription).

## Implementation Notes

From ADR "files to modify" + D3 (uninstall half):

- `clio/Command/UninstallCreatioCommand.cs` (modify) — implement `IStageEventSource`; own `runId`/`sequence`/manifest (reuse `StageEventEmitter` from story 2); re-raise events from `CreatioUninstaller`. Keeps the tool's subscription seam uniform (it always subscribes to the resolved *command* instance).
- `clio/Common/CreatioUninstaller.cs` (modify) — add a lightweight internal stage callback the command wires; implement the two corrections: `read-config` failure ⇒ FAILED + safe abort (do not unregister, do not report success); `delete-apppool-profile` ⇒ skipped/not-supported only when a profile exists. Does **NOT** implement real profile deletion (PRD non-goal).
- Uninstall stages per ADR fact 8: `read-config`→`stop-iis`→`delete-iis`→`drop-db`→`delete-files`→`unregister`.
- Redaction is inherited from the shared emitter (single boundary from story 2).
- Behavior classes via DI; `ClioStageEvent` DTO may be `new`'d.

Key files: `clio/Command/UninstallCreatioCommand.cs`, `clio/Common/CreatioUninstaller.cs`
Pattern to follow: story 2's `StageEventEmitter`/`IStageEventSource`; `CreatioUninstaller` is a `Common` collaborator (not the command) so the command re-raises its callback.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | manifest order incl. `unregister` last; running/done sequence; failure cascade | `clio.tests/Command/UninstallCreatioCommandTests.cs` |
| Unit `[Category("Unit")]` | correction 1: config-read failure ⇒ FAILED + no unregister + not-success + run-completed failure | `clio.tests/CreatioUninstallerTests.cs` |
| Unit `[Category("Unit")]` | correction 2: profile-exists ⇒ skipped/not-supported; no-profile ⇒ stage absent | `clio.tests/CreatioUninstallerTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`. Prefer `BaseCommandTests<TOptions>` for the command; NSubstitute for IIS/DB/file collaborators.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=Common)"`

## Definition of Done

- [ ] Code compiles without new `CLIO*` warnings; behavior via DI
- [ ] No CLI flags introduced
- [ ] Both corrections implemented and unit-covered (config-read FAILED+abort; profile skipped/not-supported)
- [ ] `unregister` runs only after cleanup succeeds; failure cascade matches deploy
- [ ] Redaction inherited from the shared emitter boundary; no secret in any field
- [ ] Unit tests added with `[Category("Unit")]`; AAA + `because` + `[Description]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
