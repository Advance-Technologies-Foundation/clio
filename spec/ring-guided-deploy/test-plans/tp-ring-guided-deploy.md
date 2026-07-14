# Test Plan: Ring-Guided Creatio Deploy & Uninstall (Typed MCP Progress Pipeline)

**Feature**: ring-guided-deploy
**Stories**: [1](../stories/story-ring-guided-deploy-1.md) · [2](../stories/story-ring-guided-deploy-2.md) · [3](../stories/story-ring-guided-deploy-3.md) · [4](../stories/story-ring-guided-deploy-4.md) · [5](../stories/story-ring-guided-deploy-5.md) · [6](../stories/story-ring-guided-deploy-6.md) · [7](../stories/story-ring-guided-deploy-7.md) · [8](../stories/story-ring-guided-deploy-8.md) · [9](../stories/story-ring-guided-deploy-9.md) · [10](../stories/story-ring-guided-deploy-10.md) · [11](../stories/story-ring-guided-deploy-11.md)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md) (22 FRs, 18 ACs)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-07-11

---

## Autonomous Mode Summary

Produced in autonomous mode (`--auto`); no checkpoints taken. Decisions made without pausing:

- **QA-01** — Cross-repo plan with two test surfaces: **clio** (`C:\Projects\clio`, `clio.tests` unit + `clio.mcp.e2e`) and **clio-ring** (`C:\Projects\clio\clio-ring`, branch `spike/ring-clio-ipc`, `ClioRing.Tests`). Test-case IDs are numbered once across both repos so traceability is unambiguous; each section states its repo + project.
- **QA-02** — Every TC traces to a **story ID + PRD FR/AC ID**. Coverage verified: all **22 FRs (FR-01..22)** and all **18 ACs (AC-01..17 + AC-ERR)** map to at least one TC (see Traceability matrix).
- **QA-03** — Clio test rules applied verbatim: `[Category("Unit")]` (never `UnitTests`), `[Category("Integration")]`, `[Category("E2E")]`; naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` on every assertion + `[Description]` on every test; NUnit 4 + FluentAssertions + NSubstitute; command tests prefer `BaseCommandTests<TOptions>`.
- **QA-04** — **Safety invariants are first-class test cases, not prose.** No agent-initiated real install/uninstall (TC-U-28, TC-U-50, TC-U-56); no-AOT-until-proven gate (TC-U-65). All unit/integration tests use stubs / dry paths; **no test ever triggers a real deploy or uninstall**. E2E runs only against a disposable throwaway stand, gated by `Assert.Ignore` when unreachable — never a production environment.
- **QA-05** — `clio.mcp.e2e` is **NOT in CI** (project-context.md) — all TC-E2E-* are manual, live-stand-only, with a PR-checklist manual gate.
- **QA-06** — Cross-repo envelope drift is guarded by the byte-identical JSON fixture asserted on both sides (TC-U-05 clio, TC-U-30 ring); the fixture-parity discipline is called out in Regression Scope.
- **QA-07** — The headless-screenshots-mask-crashes hazard is captured as TC-E2E-05 (interaction soak) plus an explicit **gap** note: `measurements/soak-interaction.ps1` (32 lines today) does not open the new pipeline/Install-form/Uninstall-confirm windows — closing that gap is a DoD item for stories 7/8/9.

---

## Scope

### In scope

**clio repo — typed MCP event layer (stories 1–5):**
- `ClioStageEvent` envelope (`manifest` / `stage` / `run-completed`) + `schemaVersion` + `StageIds` string constants; optional-field omission; unknown-field tolerance at the type level.
- The committed JSON fixture contract test — the pinned byte-level envelope both repos share.
- Deploy stage-event emitter (`CreatioInstallerService` → `IStageEventSource` + `StageEventEmitter`): manifest from the resolved execution path, real running/done transitions, conditional `stage-build` skip, **failure cascade** (active=failed + remaining=skipped `after-failure` + `run-completed` failure), monotonic sequence, inert with no subscriber.
- Uninstall emitter (`UninstallCreatioCommand` + `CreatioUninstaller`) incl. the **3 corrections**: (1) config-read failure ⇒ visible **failed** step + safe abort (NOT silent skip-but-report-success); (2) AppPool profile ⇒ skipped/not-supported only if a profile exists; (3) `unregister` final, only after cleanup succeeds.
- **Secret exclusion at source** — deny-list guard asserts no connection string / password / redis creds / token enters any event field.
- MCP tool forwarding (`InstallerCommandTool` / `UninstallCreatioTool`): emit progress with typed `_meta.clioStageEvent`; `InternalExecute<TCommand>(configureCommand:)`; no-op when `ProgressToken` null; `Destructive=true` preserved; send-failure swallowed.
- Docs / capability-map / uninstall AppPool-profile doc correction (story 5).

**clio-ring project group — guided UX (stories 6–11):**
- Raw `RegisterNotificationHandler` `_meta` adapter: parse `ClioStageEvent`, tolerate unknown fields, ignore duplicate/out-of-order `sequence` per `runId`, correlate by `progressToken`→`runId`, mirror fixture parity.
- `DeployPipelineViewModel` step-state transitions (Pending→Running→Done/Failed, remaining→Skipped on failure; not-applicable vs after-failure distinction; reconcile-against-manifest).
- Install form validation + pre-selected free port; preflight as first pipeline step; Uninstall Yes/No gating + local-env picker; logging path/rotation/redaction; receipt NDJSON = same event stream (SM-03 replay equality); settings dev-clio override + visible connected-clio identity.

### Out of scope (with reason)

- **Real AppPool-profile deletion** — PRD non-goal; only the skipped/not-supported reporting + doc correction are tested (AC-08 / FR-16).
- **Rewriting deploy/uninstall stage logic** — stages are instrumented as-is; only the two uninstall honest-reporting corrections change behavior (PRD non-goal).
- **AOT publish path / AOT-published artifact** — PRD non-goal (AC-17/FR-22); we assert the JIT-only gate and non-AOT isolation, not an AOT build.
- **A real deploy/uninstall against any live/production Creatio** — forbidden by the safety invariant; E2E uses a disposable stand only.
- **Orphan IIS-site discovery for the uninstall picker** — OQ-04 resolved to registered-environments only; out of scope.
- **General-purpose MCP eventing framework** — PRD non-goal; only the deploy/uninstall progress contract is covered.
- **Transport reliability / performance benchmarking of `_meta`** — A-01 mitigation (reconcile-against-manifest) is asserted (TC-U-44); raw transport throughput is not benchmarked.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation / test |
|------|-----------|--------|-------------------|
| **Agent-initiated real deploy/uninstall** (destructive op fires without a user click) | Low | **Critical** | Safety invariant. TC-U-50 (Install click sole initiator), TC-U-56 (Yes click sole initiator), TC-U-28 (`Destructive=true` preserved, no auto-invocation). All tests stub the IPC client; **no test triggers a real op**. |
| **Cross-repo envelope drift** — clio emits a shape the Ring mirror can't parse | Med | High | Byte-identical JSON fixture asserted on both sides: TC-U-05 (clio) + TC-U-30 (ring); `schemaVersion` gate TC-U-07 / TC-U-35; fixture-parity discipline in Regression Scope. |
| **Secret leak** into an event / wire / receipt field | Med | High | Redaction at source: TC-U-13 (emitter deny-list); on the wire TC-E2E-03; on disk TC-U-60. Single redaction boundary (ADR D3). |
| **Failure cascade wrong** — remaining stages not skipped, or run reported success on failure | Med | High | TC-U-11 (deploy), TC-U-21 (uninstall), TC-U-40 (Ring render), TC-U-18 (config-read safe abort). |
| **`_meta` SDK behavior** — SDK `IProgress<ProgressNotificationValue>` drops `_meta` (ADR fact 6); Ring must use raw handler | Med | High | TC-U-31 asserts the raw `RegisterNotificationHandler` path (NOT the dropping overload); TC-E2E-04 asserts mid-call delivery. |
| **`_meta` reorder/drop on a long deploy** (A-01) | Med | Med | `sequence` de-dup/order tolerance TC-U-33; reconcile-against-manifest TC-U-44; ordered manifest + terminal event bound the stream. |
| **Config-read silent-skip regression** (the bug being fixed) | Med | High | TC-U-18 (FAILED + no unregister + not-success), TC-U-57 (Ring renders honestly end-to-end). |
| **No-op path regressed** — progress sent when `ProgressToken` null (protocol misuse for non-progress clients) | Low | Med | TC-U-25; ADR D4 no-op-when-null. |
| **Emission breaks the operation** — a subscriber/send failure aborts deploy | Low | High | TC-U-15 (inert with no subscriber), TC-U-27 (send-failure swallowed). |
| **Headless masks crashes** — Avalonia windows crash only when actually shown; headless test/screenshot passes | High | Med | TC-E2E-05 interaction soak (NON-headless). **GAP**: `soak-interaction.ps1` does not yet open the new pipeline/forms windows — closing it is a DoD item (stories 7/8/9). |
| **No-AOT gate violated** — an AOT publish slips in before JIT is proven | Low | Med | TC-U-65 asserts no `PublishAot`; SDK + `_meta` deserialize isolated in non-AOT `ClioRing.Ipc` (ADR D8). |
| **SM-03 UI/receipt disagreement** — receipt is a second derivation that drifts | Med | High | TC-U-59 replay-from-file equals UI model (NDJSON = the wire stream, no second derivation). |
| **clio.mcp.e2e not in CI** | High | Med | All TC-E2E-* manual; PR-checklist gate; run status (verified on stand / unverified) recorded in PR description. |

---

## Unit Tests — clio repo (`clio.tests/`)

All: `[Category("Unit")]`, NUnit 4 + FluentAssertions + NSubstitute, AAA + `because` on every assertion + `[Description]` on every test, naming `MethodName_ShouldBehavior_WhenCondition`. Command surfaces prefer `BaseCommandTests<TOptions>`; collaborators (IIS/DB/file/HTTP) via NSubstitute. **No test performs a real install/uninstall** — stage bodies are stubbed/mocked so only the emitter/tool wiring executes.

### Story 1 — `ClioStageEvent` contract (`Module=McpServer`)
**File**: `clio.tests/Command/McpServer/ClioStageEventContractTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-01 | `Serialize_ShouldEmitVersionedManifestShape_WhenEventTypeIsManifest` | `schemaVersion=1`, `eventType=manifest`, `runId` (guid), `sequence` (int), `operation` in {deploy,uninstall}, `stages[]` of `{stageId,name,index,total,conditional}` — names exactly per ADR D2 | S1 / FR-09,FR-11 / AC-09 |
| TC-U-02 | `Serialize_ShouldEmitStageShapeAndOmitNulls_WhenEventTypeIsStage` | `stage`={stageId,name,index,total,status,startedAtUtc?,durationMs?,message,detail?,errorCode?,skipReason?}; optional fields omitted when null (`WhenWritingNull`) | S1 / FR-10 / AC-09 |
| TC-U-03 | `Serialize_ShouldEmitRunCompletedShape_WhenEventTypeIsRunCompleted` | `runCompleted`={outcome(success\|failure),summary,detail?,errorCode?,derivedUrl?,derivedPath?} | S1 / FR-11 / AC-ERR |
| TC-U-04 | `StageIds_ShouldExposeKebabStringConstants_ForAllDeployAndUninstallStages` | all deploy keys (`stage-build`,`unzip`,`copy-files`,`restore-db`,`deploy-app`,`configure-conn-strings`,`register-env`,`wait-ready`) + uninstall keys (`stop-iis`,`read-config`,`delete-iis`,`drop-db`,`delete-files`,`unregister`,`delete-apppool-profile`); string constants, NOT enum ordinals | S1 / FR-10 / AC-09 |
| TC-U-05 | `RoundTrip_ShouldBeByteIdenticalToFixture_WhenDeserializedAndReserialized` | committed JSON fixture (manifest+stage+run-completed) round-trips byte-identical — **cross-repo compatibility anchor** (paired with ring TC-U-30) | S1 / FR-15 / AC-09 |
| TC-U-06 | `Deserialize_ShouldTolerateUnknownField_WhenFixtureHasExtraProperty` | unknown extra field ⇒ no throw (FR-12 unknown-field tolerance at the type level) | S1 / FR-12 / AC-11 |
| TC-U-07 | `SchemaVersion_ShouldBeReadable_WhenEnvelopeVersionDiffersFromEmitter` | `schemaVersion` exposed so a consumer can gate — the compatibility gate (ADR D2) | S1 / FR-12 / AC-11 |

### Story 2 — deploy emitter (`Module=McpServer` / `Module=CreatioInstallCommand`)
**File**: `clio.tests/Command/McpServer/StageEventEmitterTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-08 | `Execute_ShouldEmitManifestFromResolvedPath_WhenDeployBegins` | single `manifest` first, 8 deploy stages in order, `total`=manifest length, `stage-build` `conditional=true` when source not a network drive (built from resolved path, NOT hardcoded — A-02) | S2 / FR-05,FR-13 / AC-05 |
| TC-U-09 | `Execute_ShouldEmitRunningThenDoneInOrder_WhenStagesRun` | per-stage `running` then `done`; `startedAtUtc` on running; `durationMs` on done; `index`/`total` match manifest | S2 / FR-05 / AC-05 |
| TC-U-10 | `Execute_ShouldEmitSkippedNotApplicable_WhenStageBuildInert` | `stage-build` non-network ⇒ `status=skipped skipReason=not-applicable` (distinct from failure-cascade skip — OQ-06) | S2 / FR-05 / AC-05 |
| TC-U-11 | `Execute_ShouldCascadeFailure_WhenAStageThrows` | active stage `failed` (with detail/errorCode) → every remaining stage `skipped skipReason=after-failure` → `run-completed outcome=failure`, in that order | S2 / FR-13 / AC-10 |
| TC-U-12 | `Execute_ShouldEmitRunCompletedSuccess_WhenAllStagesSucceed` | terminal `run-completed outcome=success` + friendly `summary` + `derivedUrl`/`derivedPath` where known | S2 / FR-13 / AC-05 |
| TC-U-13 | `Emit_ShouldRejectSecrets_WhenMessageOrDetailOrErrorCodeContainsCredential` | deny-list guard: no connection string / password / redis creds / token in `message`/`detail`/`errorCode`; `errorCode` is a stable symbolic code, `detail` non-secret | S2 / FR-15 / AC-12 |
| TC-U-14 | `StageChanged_ShouldYieldStableRunIdAndMonotonicSequence_WhenSubscribed` | cast to `IStageEventSource`; all events carry one per-run `runId` + strictly increasing `sequence` | S2 / FR-13 / AC-05 |
| TC-U-15 | `Execute_ShouldBeInert_WhenNoSubscriberAttached` | no `StageChanged` handlers ⇒ deploy behavior byte-for-byte unchanged; emission never throws/breaks the op (**safety**) | S2 / FR-13 / AC-ERR |

### Story 3 — uninstall emitter + 3 corrections (`Module=Command` / `Module=Common`)
**Files**: `clio.tests/Command/UninstallCreatioCommandTests.cs` (prefer `BaseCommandTests<TOptions>`), `clio.tests/CreatioUninstallerTests.cs` (new/extend)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-16 | `Execute_ShouldEmitManifestWithUnregisterLast_WhenUninstallBegins` | manifest order `read-config`→`stop-iis`→`delete-iis`→`drop-db`→`delete-files`→`unregister`; configuration validated before IIS is stopped; `unregister` positioned final | S3 / FR-07 / AC-10 |
| TC-U-17 | `Execute_ShouldEmitRunningDoneWithSameEnvelopeShapeAsDeploy_WhenStagesRun` | `running`/`done` with `index`/`total`/`durationMs`; identical envelope shape to deploy | S3 / FR-07 / AC-10 |
| TC-U-18 | `Execute_ShouldFailReadConfigAndSafeAbort_WhenConfigReadThrows` | **correction 1**: `read-config` `status=failed`; environment **NOT** unregistered; run **NOT** reported success; `run-completed outcome=failure` follows | S3 / FR-07,FR-14 / AC-07 |
| TC-U-19 | `Execute_ShouldSkipProfileNotSupported_WhenAppPoolProfileExists` | **correction 2**: `delete-apppool-profile` `status=skipped skipReason=not-supported` when a profile exists; **absent from manifest** when none exists; never silently succeeded | S3 / FR-07,FR-14 / AC-08 |
| TC-U-20 | `Execute_ShouldUnregisterOnlyAfterCleanupSucceeds_WhenAllStagesPass` | **correction 3**: `unregister` runs as final stage only after prior cleanup succeeds → `run-completed outcome=success` | S3 / FR-07 / AC-10 |
| TC-U-21 | `Execute_ShouldCascadeFailure_WhenAnUninstallStageThrows` | active=`failed`, remaining=`skipped skipReason=after-failure`, then `run-completed outcome=failure` — same cascade as deploy | S3 / FR-14 / AC-10 |
| TC-U-22 | `StageChanged_ShouldUseCommandOwnedRunIdAndReRaiseUninstallerCallback_WhenSubscribed` | command owns `runId`/`sequence`/manifest; re-raises `CreatioUninstaller`'s lightweight stage callback (uniform seam) | S3 / FR-14 / AC-07 |
| TC-U-23 | `Execute_ShouldApplyCorrectionsRegardlessOfSubscriber_WhenNoSubscriberAttached` | no subscriber ⇒ behavior unchanged **except** the two honest-reporting corrections (config-read FAILED+abort, profile skipped/not-supported) apply regardless of subscription | S3 / FR-14 / AC-07 |

### Story 4 — MCP tool forwarding (`Module=McpServer`)
**Files**: `clio.tests/Command/McpServer/InstallerCommandToolTests.cs`, `clio.tests/Command/McpServer/UninstallCreatioToolTests.cs`

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-24 | `OnStageChanged_ShouldPopulateMetaAndProgressFields_WhenStageEventRaised` | `_meta["clioStageEvent"]` = serialized `ClioStageEvent`; `Progress`=stage.index, `Total`=stage.total, `Message`=stage.message; sent via `notifications/progress` | S4 / FR-08 / AC-09 |
| TC-U-25 | `Execute_ShouldSendNoNotifications_WhenProgressTokenIsNull` | no-op; behavior byte-for-byte identical to today for non-progress clients (mirrors `StartTool`/heartbeat) | S4 / FR-08 / AC-09 |
| TC-U-26 | `Execute_ShouldSubscribeViaConfigureCommand_WhenToolDispatches` | uses `InternalExecute<TCommand>(options, configureCommand: cmd => ((IStageEventSource)cmd).StageChanged += OnStageChanged)` — per-request environment-bound command instance | S4 / FR-13,FR-14 / AC-09 |
| TC-U-27 | `OnStageChanged_ShouldSwallowSendFailure_WhenNotificationSendThrows` | send failure swallowed; never breaks the operation (like `McpLogNotifier`/heartbeat) | S4 / FR-08 / AC-ERR |
| TC-U-28 | `Tools_ShouldRemainDestructiveWithNoAutoInvocation_WhenInspected` | `deploy-creatio` + `uninstall-creatio` stay `Destructive=true`; no agent auto-invocation introduced (**safety** — initiation gated in the Ring) | S4 / FR-08 / AC-16 |
| TC-U-29 | `OnStageChanged_ShouldForwardTerminalFailureDetail_WhenRunCompletedFailure` | terminal `run-completed` carries `summary`+`detail`/`errorCode`; stream reflects active=`failed`, remaining=`skipped` | S4 / FR-08 / AC-ERR |

### No-AOT gate (build config)
**File**: `clio.tests/` or `ClioRing.Tests/` project-config assertion (whichever hosts the gate)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-65 | `RingProject_ShouldNotEnableAot_WhenBuildConfigInspected` | no `PublishAot=true` / no AOT publish profile in the Ring build; SDK-reflection + `_meta` deserialize isolated in non-AOT `ClioRing.Ipc` (ADR D8) — JIT-only gate | S8 / FR-22 / AC-17 |

---

## Unit Tests — clio-ring project group (`ClioRing.Tests/`, branch `spike/ring-clio-ipc`)

Same style rules (AAA + `because` + `[Description]`, `MethodName_ShouldBehavior_WhenCondition`). IPC client + env source mocked; VMs fed a **synthetic** typed `ClioStageEvent` stream — **no real clio process, no real deploy/uninstall**.

### Story 6 — mirror + raw `_meta` adapter
**File**: `ClioRing.Tests/ClioStageEventAdapterTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-30 | `RoundTrip_ShouldMatchClioFixtureBytes_WhenDeserializedIntoMirror` | clio story-1 fixture copied byte-identically ⇒ deserialize into mirrored `ClioStageEvent`, all fields map, re-serialize byte-identical — **cross-repo anchor** (paired with clio TC-U-05) | S6 / FR-08 / AC-11 |
| TC-U-31 | `Handler_ShouldRaiseTypedEventFromMeta_WhenProgressNotificationHasClioStageEvent` | raw `RegisterNotificationHandler("notifications/progress",…)` reads `params._meta.clioStageEvent` and raises a typed `ClioStageEvent` — NOT the `_meta`-dropping `IProgress<ProgressNotificationValue>` overload (ADR fact 6) | S6 / FR-08 / AC-11 |
| TC-U-32 | `Handler_ShouldTolerateUnknownField_WhenEnvelopeHasExtraProperty` | unknown extra field ⇒ no throw | S6 / FR-12 / AC-11 |
| TC-U-33 | `Handler_ShouldIgnoreDuplicateOrOutOfOrderSequence_WhenSameRunId` | duplicate/out-of-order `sequence` for a `runId` ⇒ ignored (no double-raise, no crash) | S6 / FR-12 / AC-11 |
| TC-U-34 | `Handler_ShouldCorrelateByProgressToken_WhenConcurrentCalls` | events correlated strictly `progressToken`→`runId`; foreign/unknown-run events ignored | S6 / FR-12 / AC-11 |
| TC-U-35 | `Handler_ShouldExposeVersionMismatch_WhenSchemaVersionDiffers` | `schemaVersion` mismatch detectable ⇒ graceful degrade rather than misparse | S6 / FR-08 / AC-11 |
| TC-U-36 | `CallToolAsync_ShouldLeaveStringProgressOverloadUnchanged_WhenTypedOverloadAdded` | pre-existing `IProgress<string>` overload unchanged and still works (**regression**) | S6 / FR-08 / AC-11 |
| TC-U-37 | `Handler_ShouldSkipSafely_WhenMetaAbsentOrClioStageEventMalformed` | absent `_meta` or malformed `clioStageEvent` ⇒ skipped safely (no throw, no fabricated event) | S6 / FR-12 / AC-11 |

### Story 7 — `DeployPipelineViewModel`
**File**: `ClioRing.Tests/DeployPipelineViewModelTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-38 | `BuildSteps_ShouldUseManifestWithNoFabricatedPending_WhenManifestEventArrives` | step list built one-per-manifest-stage in order, each initially `Pending`; NO synthesized per-step "pending" events | S7 / FR-04 / AC-09 |
| TC-U-39 | `UpdateStep_ShouldTransitionWithDurationAndDetail_WhenStageEventsArrive` | Pending→Running→Done/Failed/Skipped; duration once complete; friendly `message`; expander exposes `detail`/`errorCode` | S7 / FR-04,FR-05 / AC-05 |
| TC-U-40 | `Render_ShouldShowFailureCascadeAndOneCorrectiveAction_WhenStageFails` | active step Failed, remaining Skipped, terminal `run-completed outcome=failure`; exactly ONE human message + one corrective action, technical detail behind expander | S7 / FR-04 / AC-10 |
| TC-U-41 | `Render_ShouldShowTerminalSuccessNoErrorAffordance_WhenRunCompletedSuccess` | terminal success + summary + `derivedUrl`/`derivedPath`; no error affordance/expander noise on happy path | S7 / FR-04 / AC-05 |
| TC-U-42 | `Render_ShouldDistinguishNotApplicableFromAfterFailureSkip_WhenSkipReasonsDiffer` | `skipReason=not-applicable` vs `after-failure` visually distinguishable (not conflated) | S7 / FR-05,FR-07 / AC-05 |
| TC-U-43 | `BuildSteps_ShouldMapOneToOneToStages_WhenDeployOrUninstallManifest` | deploy 1:1 FR-05 stages; uninstall 1:1 FR-07 stages; VM operation-agnostic (manifest-driven) | S7 / FR-05,FR-07 / AC-05 |
| TC-U-44 | `Reconcile_ShouldReflectTerminalOutcomeAgainstManifest_WhenIntermediateStageEventsLost` | manifest + `run-completed` arrive but intermediate `stage` events lost ⇒ terminal outcome reconciled, no stall (A-01 mitigation) | S7 / FR-04 / AC-ERR |

### Story 8 — guided Install form + preflight
**File**: `ClioRing.Tests/InstallFormViewModelTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-45 | `MainRing_ShouldExposeDeployAsPrimaryAction_WhenOpened` | "Deploy Creatio" is a primary radial-ring action (not tray/taskbar) | S8 / FR-01 / AC-01 |
| TC-U-46 | `Form_ShouldExposeGuidedFieldsAndNoDryRunControl_WhenOpened` | DB source + Redis source (local/Rancher) + build/ZIP + instance name + pre-selected editable free port; **NO dry-run control anywhere** | S8 / FR-02 / AC-02 |
| TC-U-47 | `Install_ShouldBlockWithOneMessageAndCorrectiveAction_WhenPreflightFails` | preflight problem ⇒ no clio call; exactly one human message + one corrective action; "Check requirements" first step Failed | S8 / FR-03 / AC-03 |
| TC-U-48 | `Install_ShouldInvokeDeployToolOnceImmediately_WhenPreflightPasses` | valid form ⇒ deploy tool invoked once immediately (no dry-run, no extra confirmation); "Check requirements" Done then FR-05 stages | S8 / FR-04 / AC-04 |
| TC-U-49 | `Preflight_ShouldRunAsFirstPipelineStepAndRevalidatePort_WhenInstallClicked` | preflight is the first step in the SAME pipeline (not a modal) and pre-selects/re-validates a free port | S8 / FR-03 / AC-04 |
| TC-U-50 | `Deploy_ShouldInvokeToolOnlyOnUserInstallClick_WhenNoAgentInitiation` | **SAFETY**: real `deploy-creatio` invoked ONLY on the user's Install click; stubbed IPC asserts zero invocation absent a click; no agent auto-initiation | S8 / FR-21 / AC-16 |

### Story 9 — guided Uninstall flow
**File**: `ClioRing.Tests/UninstallFlowViewModelTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-51 | `MainRing_ShouldExposeUninstallAsPrimaryAction_WhenOpened` | "Uninstall" is a primary radial-ring action (not tray/taskbar) | S9 / FR-01 / AC-01 |
| TC-U-52 | `Picker_ShouldListLocalRegisteredEnvironments_WhenUninstallOpened` | picker sourced from clio `list-environments`, local-filtered, matching the `uninstall-creatio` `environment-name` contract (OQ-04) | S9 / FR-06 / AC-06 |
| TC-U-53 | `Confirm_ShouldBeSimpleYesNoWithNoExactNameTyping_WhenUninstallClicked` | simple "Are you sure? Yes/No"; no exact-name typing | S9 / FR-06 / AC-06 |
| TC-U-54 | `Confirm_ShouldCancelWithNoClioCall_WhenNoClicked` | No ⇒ cancels, no changes, no clio call | S9 / FR-06 / AC-06 |
| TC-U-55 | `Confirm_ShouldRunSharedPipelineOnce_WhenYesClicked` | Yes ⇒ shared `DeployPipelineViewModel` runs FR-07 stages; uninstall tool invoked once | S9 / FR-06 / AC-06 |
| TC-U-56 | `Uninstall_ShouldInvokeToolOnlyOnUserYesClick_WhenNoAgentInitiation` | **SAFETY**: real `uninstall-creatio` invoked ONLY on the user's Yes click; stubbed IPC asserts zero invocation absent a click; no agent auto-initiation | S9 / FR-21 / AC-16 |
| TC-U-57 | `Render_ShouldShowFailedNotUnregistered_WhenConfigReadFails` | config-read failure (clio correction 1) ⇒ "Read configuration" Failed, run terminates failure, environment NOT shown unregistered (honest reporting end-to-end) | S9 / FR-06 / AC-07 |

### Story 10 — NDJSON receipt + logging
**File**: `ClioRing.Tests/DeploymentReceiptTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-58 | `Receipt_ShouldWriteOneNdjsonLinePerEventPlusSummary_WhenRunCompletes` | per-`runId` NDJSON, one JSON line appended per `ClioStageEvent` (the literal wire stream) + a final rolled-up JSON summary (per-stage outcome+duration+terminal outcome) | S10 / FR-18 / AC-13 |
| TC-U-59 | `Replay_ShouldEqualUiModel_WhenReceiptReplayed` | pipeline model rebuilt from the NDJSON file equals the UI model byte-for-byte for that run (SM-03 replay equality — both derive from the same stream) | S10 / FR-18 / AC-13 |
| TC-U-60 | `Receipt_ShouldContainNoSecret_WhenInspected` | no connection string / credential / token on disk (redaction inherited from clio source; Ring adds no secret material) | S10 / FR-17 / AC-12 |
| TC-U-61 | `Receipt_ShouldRecordNonSuccessOutcome_WhenRunFails` | failed run ⇒ receipt records failed stage + skipped-after-failure stages + `run-completed outcome=failure` | S10 / FR-18 / AC-13 |

### Story 11 — settings + connected-clio identity
**File**: `ClioRing.Tests/ClioSettingsViewModelTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-U-62 | `Settings_ShouldConnectNormalClioAndShowIdentity_WhenNoOverride` | no override ⇒ connects to normal clio build/config; UI identifies "normal clio" connected | S11 / FR-19,FR-20 / AC-15 |
| TC-U-63 | `Settings_ShouldConnectDevClioAndShowOverrideIdentity_WhenOverrideSet` | dev-clio path override ⇒ connects to dev clio; UI visibly identifies dev-override (vs normal) via handshake identity (`serverInfo.name`/`version` + resolved path), not a hardcoded label | S11 / FR-19,FR-20 / AC-15 |
| TC-U-64 | `Settings_ShouldSurfaceClearError_WhenOverridePathInvalid` | invalid/missing override path ⇒ clear settings error; no silent fallback | S11 / FR-19 / AC-15 |

*(TC-U-65 — no-AOT gate — listed in the clio section above; it asserts the Ring build config.)*

---

## Integration Tests (`ClioRing.Tests/`, real FS)

`[Category("Integration")]`. Real filesystem via OS temp directories (cross-OS safe — no hardcoded `C:\` in the assertion, the default path is overridden to a temp dir and the *default constant* is asserted separately). No real deploy/uninstall.

**File**: `ClioRing.Tests/LoggingIntegrationTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-I-01 | `Logs_ShouldUseDefaultThenOverriddenPath_WhenAppsettingsConfigured` | default path constant = `C:\Tools\clio-ring\Logs`; when appsettings overrides, logs land at the configured (temp) path | S10 / FR-17 / AC-14 |
| TC-I-02 | `Logs_ShouldRotateByAgeAndTotalSize_WhenDirectoryGrows` | per-run files rotated, capped by directory age + total size; redaction applied | S10 / FR-17 / AC-14 |
| TC-I-03 | `OpenLogs_ShouldTargetActiveDirectory_WhenInvoked` | "Open logs" opens the active directory (default or overridden) | S10 / FR-17 / AC-14 |

---

## E2E Tests (`clio.mcp.e2e/`)

**⚠️ CI status: `clio.mcp.e2e` is NOT in CI — manual execution against a live stand only.** Record run status (verified on stand / flagged unverified) in the PR description; do not silently skip. **Safety: run only against a disposable throwaway stand — never a production/shared Creatio.** Gate on a reachable stand with `Assert.Ignore` when unreachable (existing destructive-section pattern).

**File**: `clio.mcp.e2e/DeployUninstallProgressTests.cs` (new)

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-E2E-01 | `DeployCreatio_ShouldEmitManifestStageRunCompletedMeta_WhenInvokedWithProgressToken` | real `clio mcp-server`: register raw `RegisterNotificationHandler("notifications/progress",…)`; assert `_meta.clioStageEvent` sequence = `manifest` first → ordered `stage` → terminal `run-completed`; `sequence` monotonic | S4 / FR-08 / AC-09 |
| TC-E2E-02 | `UninstallCreatio_ShouldEmitMetaSequenceThroughDestructiveDispatcher_WhenInvoked` | same manifest→stage→run-completed `_meta` sequence for `uninstall-creatio`, forwarded through resident `clio-run-destructive` unchanged (ADR fact 2) | S4 / FR-08 / AC-09 |
| TC-E2E-03 | `ForwardedMeta_ShouldContainNoSecret_WhenInspected` | no connection string / credential / token in any forwarded `_meta` field (redaction at source, stories 2/3) | S4 / FR-08 / AC-12 |
| TC-E2E-04 | `Progress_ShouldArriveMidCall_WhenLongRunningDeploy` | notifications arrive MID-CALL (not batched at end) for a long-running deploy — proves the `_meta` streaming path and A-01 assumption | S4 / FR-08 / AC-09 |

### Ring interaction soak (non-headless) — `measurements/soak-interaction.ps1`

| ID | Test | Expected | Story / FR / AC |
|----|------|----------|-----------------|
| TC-E2E-05 | Interaction soak drives the new **deploy pipeline**, **Install form**, and **Uninstall Yes/No confirm** windows in a NON-headless session, catching crashes headless tests/screenshots mask. **Uses a stub/dry IPC path — must NOT trigger a real deploy/uninstall.** **⚠️ GAP**: `soak-interaction.ps1` (32 lines today) opens only env-load + a clio run — it does **not** open the new windows. Extending it to cover the pipeline/forms is a **DoD item for stories 7/8/9**. | S7,S8,S9 / FR-04,FR-01 / AC-05,AC-01 |

---

## Regression Scope

Tests that MUST stay green after this feature ships, with the targeted smart-regression filter per the module-to-source policy.

### clio repo

| Test file / fixture | Scope at risk | Why at risk |
|---------------------|---------------|-------------|
| `clio.tests/Command/McpServer/InstallerCommandToolTests.cs` | Tool ctor + execution path | `InstallerCommandTool` gains `McpServer`+`RequestContext` injection and switches to `InternalExecute<TCommand>(configureCommand:)` — instantiation/wiring changes ripple |
| `clio.tests/Command/McpServer/McpProgressHeartbeatTests.cs` | Progress no-op-when-null precedent | Same `notifications/progress` seam; the no-op pattern must remain intact |
| `clio.tests/CreatioInstallerServiceTests.cs`, `clio.tests/CreatioInstallerService.LocalRestoreTests.cs` | Deploy happy path | `CreatioInstallerService.Execute` is wrapped with emitter transitions — deploy behavior must be byte-for-byte unchanged with no subscriber (TC-U-15) |
| `clio.tests/ApplicationInstallerTests.cs`, `clio.tests/PackageInstallerTests.cs` | Install collaborators | Shared install path; must be untouched by instrumentation |
| existing `UninstallCreatioCommand` / `CreatioUninstaller` fixtures (if present) | Uninstall behavior | Two corrections change failure reporting; the happy path must stay unchanged |
| `clio.mcp.e2e/DeployCreatioToolE2ETests.cs`, `InstallApplicationToolE2ETests.cs` | Existing deploy/install E2E | Tool contract must stay compatible (additive `_meta` only); NOT in CI — manual |
| docs gate (ReadmeChecker / docs-consistency, if present for the verbs) | Story 5 docs | Uninstall doc correction + capability-map row must keep the gate green |

Targeted filters (clio):
- Stories 1, 4: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`
- Story 2: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=McpServer|Module=CreatioInstallCommand)"`
- Story 3: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=Common)"`
- **Full-suite trigger**: story 2/3 touch `clio/BindingsModule.cs` for new DI registration (`IStageEventSource`/emitter). If `BindingsModule.cs` or `clio/Common/**` changes, run the full unit suite: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`.

### clio-ring project group (`ClioRing.Tests`, branch `spike/ring-clio-ipc`)

| Test file / fixture | Scope at risk | Why at risk |
|---------------------|---------------|-------------|
| `ClioRing.Tests/DeployReceiptTests.cs` | Receipt writer | Story 10 reworks the receipt to append the typed NDJSON stream — existing receipt assertions may change shape |
| `ClioRing.Tests/DeployWizardViewModelTests.cs` | Deploy wizard/pipeline VM | Story 7/8 add the manifest-driven `DeployPipelineViewModel`; the wizard flow must not regress |
| `ClioRing.Tests/RingViewModelConfirmTests.cs` | Confirm dialog | Story 9's Yes/No uninstall confirm reuses/extends the confirm surface |
| `ClioRing.Tests/ActionCatalogUninstallCreatioTests.cs` | Uninstall action catalog | Story 9 adds the main-ring Uninstall entry + local-env picker |
| `ClioRing.Ipc/ClioIpcClient.cs` (via its tests) | IPC client | Story 6 adds a `CallToolAsync(IProgress<ClioStageEvent>)` overload; the existing `IProgress<string>` overload must stay unchanged (TC-U-36) |
| `ClioRing.Ipc/SecretRedactor.cs`, `PreflightGate.cs`, `DeployPlan.cs` | Redaction / preflight / plan | Reused by stories 8/10; redaction must remain the on-disk safety net |

Targeted run (ring): `dotnet test ClioRing.Tests/ClioRing.Tests.csproj`.

### Cross-repo fixture-parity discipline

The clio JSON fixture (story 1, TC-U-05) and the ring mirror fixture (story 6, TC-U-30) MUST remain **byte-identical**. This is a discipline, not a compiler guarantee (ADR trade-off). Any change to `ClioStageEvent` fields requires: bump `schemaVersion`, update BOTH fixtures identically, and re-run TC-U-05 + TC-U-30. Flag in both PRs.

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit (clio) | 22 (TC-U-01…29 clio subset + TC-U-65) | ~ripple in `InstallerCommandToolTests`, `CreatioInstallerServiceTests` (emitter wrapping) | Files: `ClioStageEventContractTests.cs`, `StageEventEmitterTests.cs`, `UninstallCreatioCommandTests.cs`, `CreatioUninstallerTests.cs`, `InstallerCommandToolTests.cs`, `UninstallCreatioToolTests.cs` |
| Unit (Ring) | 36 (TC-U-30…64) | ripple in `DeployReceiptTests`, `DeployWizardViewModelTests`, `RingViewModelConfirmTests`, `ActionCatalogUninstallCreatioTests` | Files: `ClioStageEventAdapterTests.cs`, `DeployPipelineViewModelTests.cs`, `InstallFormViewModelTests.cs`, `UninstallFlowViewModelTests.cs`, `DeploymentReceiptTests.cs`, `ClioSettingsViewModelTests.cs` |
| Integration (Ring) | 3 (TC-I-01…03) | 0 | Real FS via temp dirs; `LoggingIntegrationTests.cs` |
| E2E | 5 (TC-E2E-01…05) | 0 | Manual only — NOT in CI; TC-E2E-05 is the non-headless interaction soak (gap to close in stories 7/8/9) |

**Totals: 65 Unit (TC-U), 3 Integration (TC-I), 5 E2E (TC-E2E).**

---

## Traceability

### Story → acceptance test cases

| Story | Repo | Test cases |
|-------|------|-----------|
| 1 — envelope + StageIds + fixture | clio | TC-U-01, TC-U-02, TC-U-03, TC-U-04, TC-U-05, TC-U-06, TC-U-07 |
| 2 — deploy emitter | clio | TC-U-08, TC-U-09, TC-U-10, TC-U-11, TC-U-12, TC-U-13, TC-U-14, TC-U-15 |
| 3 — uninstall emitter + 3 corrections | clio | TC-U-16, TC-U-17, TC-U-18, TC-U-19, TC-U-20, TC-U-21, TC-U-22, TC-U-23 |
| 4 — MCP tool forwarding | clio | TC-U-24, TC-U-25, TC-U-26, TC-U-27, TC-U-28, TC-U-29; TC-E2E-01, TC-E2E-02, TC-E2E-03, TC-E2E-04 |
| 5 — docs + capability map + doc correction | clio | Docs gate (Story 5 supports AC-08 via FR-16); no new production TC — reviewed under regression docs gate |
| 6 — mirror + `_meta` adapter | ring | TC-U-30, TC-U-31, TC-U-32, TC-U-33, TC-U-34, TC-U-35, TC-U-36, TC-U-37 |
| 7 — DeployPipelineViewModel | ring | TC-U-38, TC-U-39, TC-U-40, TC-U-41, TC-U-42, TC-U-43, TC-U-44; TC-E2E-05 |
| 8 — Install form + preflight | ring | TC-U-45, TC-U-46, TC-U-47, TC-U-48, TC-U-49, TC-U-50, TC-U-65; TC-E2E-05 |
| 9 — Uninstall flow | ring | TC-U-51, TC-U-52, TC-U-53, TC-U-54, TC-U-55, TC-U-56, TC-U-57; TC-E2E-05 |
| 10 — logging + NDJSON receipt | ring | TC-U-58, TC-U-59, TC-U-60, TC-U-61; TC-I-01, TC-I-02, TC-I-03 |
| 11 — settings + connected-clio identity | ring | TC-U-62, TC-U-63, TC-U-64 |

### FR → test case (all 22 covered)

| FR | Test cases |
|----|-----------|
| FR-01 | TC-U-45, TC-U-51 |
| FR-02 | TC-U-46 |
| FR-03 | TC-U-47, TC-U-49 |
| FR-04 | TC-U-38, TC-U-39, TC-U-40, TC-U-41, TC-U-44, TC-U-48 |
| FR-05 | TC-U-08, TC-U-09, TC-U-10, TC-U-42, TC-U-43 |
| FR-06 | TC-U-52, TC-U-53, TC-U-54, TC-U-55, TC-U-57 |
| FR-07 | TC-U-16, TC-U-17, TC-U-18, TC-U-19, TC-U-20, TC-U-42, TC-U-43 |
| FR-08 | TC-U-24, TC-U-25, TC-U-27, TC-U-28, TC-U-29, TC-U-30, TC-U-31, TC-U-35, TC-U-36; TC-E2E-01…04 |
| FR-09 | TC-U-01 |
| FR-10 | TC-U-02, TC-U-04 |
| FR-11 | TC-U-01, TC-U-03 |
| FR-12 | TC-U-06, TC-U-07, TC-U-32, TC-U-33, TC-U-34, TC-U-37 |
| FR-13 | TC-U-08, TC-U-11, TC-U-12, TC-U-14, TC-U-15, TC-U-26 |
| FR-14 | TC-U-18, TC-U-19, TC-U-21, TC-U-22, TC-U-23, TC-U-26 |
| FR-15 | TC-U-05, TC-U-13 |
| FR-16 | Story 5 docs gate (uninstall AppPool-profile doc correction) |
| FR-17 | TC-U-60, TC-I-01, TC-I-02, TC-I-03 |
| FR-18 | TC-U-58, TC-U-59, TC-U-61 |
| FR-19 | TC-U-62, TC-U-63, TC-U-64 |
| FR-20 | TC-U-62, TC-U-63 |
| FR-21 | TC-U-50, TC-U-56 |
| FR-22 | TC-U-65 |

### AC → test case (all 18 covered)

| AC | Test cases |
|----|-----------|
| AC-01 | TC-U-45, TC-U-51; TC-E2E-05 |
| AC-02 | TC-U-46 |
| AC-03 | TC-U-47 |
| AC-04 | TC-U-48, TC-U-49 |
| AC-05 | TC-U-09, TC-U-39, TC-U-41, TC-U-42, TC-U-43; TC-E2E-05 |
| AC-06 | TC-U-52, TC-U-53, TC-U-54, TC-U-55 |
| AC-07 | TC-U-18, TC-U-57 |
| AC-08 | TC-U-19; Story 5 doc correction |
| AC-09 | TC-U-01, TC-U-02, TC-U-04, TC-U-24, TC-U-25, TC-U-26, TC-U-38; TC-E2E-01, TC-E2E-02, TC-E2E-04 |
| AC-10 | TC-U-11, TC-U-16, TC-U-17, TC-U-20, TC-U-21, TC-U-40 |
| AC-11 | TC-U-06, TC-U-07, TC-U-30, TC-U-31, TC-U-32, TC-U-33, TC-U-34, TC-U-35, TC-U-36, TC-U-37 |
| AC-12 | TC-U-13, TC-U-60; TC-E2E-03 |
| AC-13 | TC-U-58, TC-U-59, TC-U-61 |
| AC-14 | TC-I-01, TC-I-02, TC-I-03 |
| AC-15 | TC-U-62, TC-U-63, TC-U-64 |
| AC-16 | TC-U-28, TC-U-50, TC-U-56 |
| AC-17 | TC-U-65 |
| AC-ERR | TC-U-03, TC-U-15, TC-U-27, TC-U-29, TC-U-44 |

---

## Safety Invariants (explicit gate)

These are non-negotiable and must be verified before any story closes:

1. **No agent-initiated real install/uninstall (FR-21 / AC-16).** The real `deploy-creatio` / `uninstall-creatio` tool fires ONLY on the user's Install click / Uninstall "Yes" click. Asserted by TC-U-50, TC-U-56 (Ring, stubbed IPC) and TC-U-28 (`Destructive=true`, no auto-invocation on the clio side). **No test in this plan triggers a real deploy or uninstall** — unit/integration use stubs and dry paths; E2E runs only against a disposable throwaway stand with `Assert.Ignore` when unreachable.
2. **No-AOT-until-proven gate (FR-22 / AC-17).** JIT-only for this feature; TC-U-65 asserts no `PublishAot`, and the reflection-heavy MCP SDK + `_meta` deserialize stay isolated in the non-AOT `ClioRing.Ipc` project (ADR D8). No AOT publish is produced or required.
3. **Secrets excluded at source (AC-12).** Single redaction boundary in the emitter (TC-U-13); verified again on the wire (TC-E2E-03) and on disk (TC-U-60). The Ring adds no secret material.
4. **Honest failure reporting (AC-07/AC-10).** Config-read failure = visible FAILED + safe abort, never silent-skip-report-success (TC-U-18, TC-U-57); failure cascade never reports a run as success (TC-U-11, TC-U-21, TC-U-40).

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] All TC-I-* implemented with `[Category("Integration")]`; FS tests use temp dirs (cross-OS safe)
- [ ] All TC-E2E-* documented in `clio.mcp.e2e/` with `[Category("E2E")]`; run status (verified on stand / unverified) in the PR description — manual gate, NOT in CI; run only against a disposable stand
- [ ] Every assertion carries `because`; every test carries `[Description]`; AAA throughout
- [ ] Test naming follows `MethodName_ShouldBehavior_WhenCondition`
- [ ] **Safety**: TC-U-28, TC-U-50, TC-U-56 green — no agent-initiated real op; TC-U-65 green — no AOT publish
- [ ] Cross-repo fixture byte-parity verified: TC-U-05 (clio) and TC-U-30 (ring) assert the identical fixture; `schemaVersion` bump discipline noted in both PRs
- [ ] Regression fixtures green per repo (clio: `InstallerCommandToolTests`, `CreatioInstallerServiceTests`, `McpProgressHeartbeatTests`; ring: `DeployReceiptTests`, `DeployWizardViewModelTests`, `RingViewModelConfirmTests`, `ActionCatalogUninstallCreatioTests`)
- [ ] Targeted filter command quoted in each PR (clio) / `ClioRing.Tests` run recorded (ring)
- [ ] **Interaction-soak gap closed**: `measurements/soak-interaction.ps1` extended to open the deploy pipeline + Install form + Uninstall confirm windows (TC-E2E-05) — DoD for stories 7/8/9
- [ ] MCP E2E coverage added for the `_meta` sequence (TC-E2E-01…04); PR states "MCP reviewed / updated"
- [ ] PR includes new + modified test files in the changed-files list
