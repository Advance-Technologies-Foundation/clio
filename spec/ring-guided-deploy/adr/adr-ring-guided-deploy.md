# ADR: Ring-Guided Creatio Deploy & Uninstall — Typed MCP Progress Pipeline

- **Status:** Proposed
- **Author:** Architect Agent
- **Date:** 2026-07-11
- **PRD:** [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md) (22 FRs, 18 ACs)
- **Predecessor ADR:** `clio-ring/spec/adr/adr-ring-clio-ipc.md` (MCP-over-stdio transport baseline)
- **stepsCompleted:** [1, 2, 3, 4]
- **Mode:** Autonomous (`--auto`) — no checkpoints taken; all decisions resolved below.
- **Scope:** Cross-repo — `C:\Projects\clio` (typed MCP progress emission) + `C:\Projects\clio\clio-ring` (Ring guided UX), branch `spike/ring-clio-ipc`.

---

## Context

The Ring is becoming clio's primary GUI (predecessor ADR: MCP-over-stdio, one persistent clio child per session, Ring as MCP client). Today Deploy and Uninstall are shell-and-scrape wizards: the Ring guesses state from console/JSON text, dry-run plumbing leaks into the UI, and the on-disk diagnostics can drift from what the user saw. clio already runs a well-defined ordered sequence of deploy/uninstall stages internally (`CreatioInstallerService.Execute`, `CreatioUninstaller`) but emits no typed, ordered progress a UI can trust.

Now that clio source changes are authorized, clio can become the single source of truth for stage progress. The design must:

- turn Deploy/Uninstall into a guided, GitHub-Actions-style step pipeline reachable from the main radial Ring (FR-01..07);
- carry **genuinely typed**, versioned, ordered stage events from clio to the Ring over MCP — not parsed console text and not JSON-in-message (FR-08..12);
- instrument the two existing stage sequences without rewriting them (FR-13..16);
- feed both the UI and an on-disk deployment receipt from the **same** event stream (FR-17..18, SM-03);
- keep secrets out at source, JIT-only, and forbid agent-initiated real operations (FR-12, FR-21, FR-22).

### Source-verified facts this ADR is designed against

1. **MCP progress plumbing exists and is proven.** `StartCommand` raises `event EventHandler<ProgressNotificationValue> StatusChanged`; `StartTool` subscribes via `InternalExecute<StartCommand>(options, configureCommand: c => c.StatusChanged += OnStatusChanged)` and forwards each as `server.SendNotificationAsync("notifications/progress", new ProgressNotificationParams { ProgressToken, Progress })`. Both repos are on `ModelContextProtocol` 1.4.0. `McpProgressHeartbeat` is the keep-alive precedent (per-tool progress during in-flight work already works — this is **not** the "live events / server-initiated subscription" gap tracked by the predecessor IPC ADR).
2. **Deploy/uninstall tools do not stream today.** `InstallerCommandTool` (`deploy-creatio`) and `UninstallCreatioTool` (`uninstall-creatio`) call `InternalExecute(options)` — a single result, no progress subscription. They dispatch through the resident `clio-run` / `clio-run-destructive` reusing the same `RequestContext`, so inner-tool progress flows through unchanged.
3. **The Ring already consumes progress but flattens it.** `ClioIpcClient.CallToolAsync(..., IProgress<string> progress, ...)` wraps the sink in `ProgressAdapter : IProgress<ProgressNotificationValue>` and collapses it to a display string.
4. **`ProgressNotificationValue` is too thin for a typed envelope** — it carries only `{ double Progress, double? Total, string? Message }`.
5. **`ProgressNotificationParams` carries a first-class `_meta` object (empirically verified, 1.4.0).** It inherits `ModelContextProtocol.Protocol.NotificationParams`, whose `JsonObject Meta` property serialises to the JSON-RPC `_meta` field. A probe serialising a populated instance produced:
   `{"progressToken":"tok-1","progress":3,"total":8,"message":"Restore database","_meta":{"clioStageEvent":{...}}}`.
   So a structured envelope can travel in `_meta` **alongside** the human `message`, with no SDK bump.
6. **The SDK's `IProgress<ProgressNotificationValue>` callback drops `_meta`.** The client-side progress overload maps params → the thin `ProgressNotificationValue`, discarding `progressToken` and `_meta`. To read `_meta` the Ring must register a raw notification handler: `McpClient.RegisterNotificationHandler("notifications/progress", (JsonRpcNotification n, ct) => …)` (present in 1.4.0), reading `n.Params._meta`.
7. **Deploy stages** (`CreatioInstallerService.Execute`): stage-build (network only) → unzip → copy-files → restore-db → deploy-app (IIS/dotnet) → configure-conn-strings (DB+Redis) → register-env → wait-ready. The service **is** the command (`Command<PfInstallerOptions>`) and already takes `ILogger` + collaborators via DI.
8. **Uninstall stages** (`Common/CreatioUninstaller.cs`, invoked by `UninstallCreatioCommand`): read-config → stop-iis → delete-iis → drop-db → delete-files → unregister (final, only after cleanup succeeds). Configuration is validated before IIS is stopped so a safe abort leaves the instance available.

---

## Decisions

### D1 — Typed transport: the MCP progress `_meta` envelope (no SDK bump)

**Decision.** clio emits each typed stage event as a JSON envelope placed in `ProgressNotificationParams.Meta` under a single well-known key `clioStageEvent`, sent via the existing `notifications/progress` channel for the in-flight tool's `progressToken`. `Progress`/`Total` continue to carry the numeric bar (index/total); `Message` continues to carry human text. **No JSON in `Message`.** The Ring reads the envelope from `_meta` via a raw `RegisterNotificationHandler("notifications/progress", …)` handler (fact 6), not the `IProgress<ProgressNotificationValue>` overload.

**Why this over the alternatives:**
- It is *genuinely typed* per the signed-off requirement, and it is **empirically supported in 1.4.0** (fact 5) — verified by serialisation probe, not inferred.
- It is additive and forward-compatible: existing consumers that only read `progress/total/message` are unaffected (they ignore `_meta`), satisfying FR-12's unknown-field tolerance at the protocol level.
- It reuses the proven `StartTool` forwarding seam (fact 1) — minimal new surface, consistent with `adr-mcp-progress-heartbeat`.
- No SDK bump is warranted; `structuredContent`/`outputSchema` (predecessor IPC ADR gap #2) is a *result*-typing concern for the final tool return, orthogonal to *progress* streaming and not needed here.

**Fallback (documented, not taken):** had `_meta` not survived the custom `ProgressNotificationParams.Converter`, the fallback would have been a second notification method (a bespoke `notifications/clio/stage`) — rejected because it is non-standard, some clients would drop unknown methods, and it duplicates the progress-token correlation the SDK already gives us. The probe removed the need.

### D2 — Envelope schema: versioned, ordered, one shared contract, string stage keys

**Decision.** One envelope record, defined once in clio and **mirrored** (not shared as a binary) in the Ring. Ordered fields:

```jsonc
{
  "schemaVersion": 1,                 // int, bumped on any breaking field change
  "eventType": "manifest|stage|run-completed",
  "runId": "guid",                    // one per real operation
  "sequence": 0,                      // monotonically increasing per run; consumers de-dup / drop out-of-order
  "operation": "deploy|uninstall",
  // eventType=manifest:
  "stages": [ { "stageId": "restore-db", "name": "Restore database", "index": 3, "total": 8, "conditional": false } ],
  // eventType=stage:
  "stage": { "stageId", "name", "index", "total",
             "status": "running|done|failed|skipped",
             "startedAtUtc?", "durationMs?",
             "message", "detail?", "errorCode?", "skipReason?" },
  // eventType=run-completed:
  "runCompleted": { "outcome": "success|failure", "summary", "detail?", "errorCode?", "derivedUrl?", "derivedPath?" }
}
```

- **Location (OQ-01):** the emit-side record lives in clio under `clio/Command/McpServer/Progress/` (new namespace) so the MCP emitter and any clio-side reader share one type; it is a plain DTO `record` (allowed to `new`, per DI policy). The Ring defines a mirrored `ClioStageEvent` record in `ClioRing.Ipc`. Cross-repo sync is by **contract, not code-sharing**: `schemaVersion` is the compatibility gate, and a committed JSON sample fixture (identical bytes in both repos) is asserted by a contract test on each side. Rationale: the repos ship independently (predecessor ADR: dual-mode packaging, independent cadence); a shared NuGet contract package is heavier than the one-record surface justifies.
- **Stage keys (OQ-02):** `stageId` values are **stable kebab-case string constants** (e.g. `stage-build`, `unzip`, `copy-files`, `restore-db`, `deploy-app`, `configure-conn-strings`, `register-env`, `wait-ready`; `stop-iis`, `read-config`, `delete-iis`, `drop-db`, `delete-files`, `unregister`, `delete-apppool-profile`), defined once as constants in the clio Progress namespace. **Not enum ordinals** — string keys keep receipts comparable across clio versions even when stages are inserted/reordered (satisfies SM-03 replay stability).
- **Ordering (OQ-06):** the `manifest` lists every stage that *will* run given the resolved inputs, with `total` = manifest length (stable denominator). A stage that is inert by condition (e.g. `stage-build` when the source is not a network drive) is included in the manifest with `conditional: true` and later emitted as `status: skipped, skipReason: "not-applicable"`. A stage skipped by the **failure cascade** is emitted as `status: skipped, skipReason: "after-failure"`. The two are distinguished by `skipReason`, never conflated.

### D3 — clio stage-event seam: a typed C# event on the command, plus redaction at source

**Decision.** Follow the proven `StartCommand.StatusChanged` pattern rather than injecting an `IProgress<>`/reporter. Introduce a typed event surface consumed by the MCP tool via `configureCommand`:

- Define `interface IStageEventSource { event EventHandler<ClioStageEvent> StageChanged; }`.
- `CreatioInstallerService` (which *is* the deploy command) implements `IStageEventSource` and raises: one `manifest` event built up front from the resolved execution path (network-source decision known before `Execute` begins), then per-stage `running`/`done`/`failed`/`skipped` transitions, then `run-completed`. A `sequence` counter and `runId` are held per-run.
- `UninstallCreatioCommand` implements `IStageEventSource` and **re-raises** events from its collaborator `CreatioUninstaller` (the stages live in `Common/CreatioUninstaller.cs`, which is not the command). `CreatioUninstaller` gains a lightweight internal callback the command wires; the command owns `runId`/`sequence` and the manifest. This keeps the tool's subscription seam uniform across both operations (it always subscribes to the resolved *command* instance).
- **Manifest built up front (A-02):** generated from the actual resolved execution path, not a hardcoded list, so it cannot misrepresent runtime order.
- **Failure cascade:** on any stage throw, the active stage is emitted `failed` (with `detail`/`errorCode`), every remaining manifest stage is emitted `skipped` (`skipReason: after-failure`), then `run-completed` with `outcome: failure`. Implemented in the emitter wrapper around each stage, not scattered through stage bodies.
- **Two uninstall corrections (FR-07/FR-14):** `read-config` failure emits `failed` + safe abort (environment is **not** unregistered, run **not** reported success — AC-07); `delete-apppool-profile` emits `skipped` (`skipReason: not-supported`) only when a profile exists (AC-08). This ADR does **not** implement real profile deletion (PRD non-goal).
- **Secrets excluded at source (FR-12, A-05, AC-12):** the emitter is the single redaction boundary. Stage bodies never place connection strings, credentials, or tokens into `message`/`detail`/`errorCode`; `errorCode` is a stable symbolic code, `detail` is non-secret technical context. A redaction guard (deny-list + assertion in tests) runs on every field at the emission point so a future stage cannot leak by omission.

**Alternative rejected — injected `IProgress<ClioStageEvent>` reporter.** More DI-idiomatic in the abstract, but the deploy/uninstall commands are startup-injected singletons; a per-call sink would need scoped resolution the MCP layer does not cleanly provide, whereas the event + `configureCommand` seam is exactly what `StartTool`/`InternalExecute<TCommand>` already support (fact 1, decision D4). Uniformity with the existing, CI-covered pattern wins.

### D4 — MCP tool forwarding: switch deploy/uninstall tools to `InternalExecute<TCommand>(…, configureCommand:)`

**Decision.** Rework `InstallerCommandTool` and `UninstallCreatioTool` to the `StartTool` shape:

- Inject `ModelContextProtocol.Server.McpServer` and capture the `RequestContext<CallToolRequestParams>` (for `Params.ProgressToken`), exactly as `StartTool`.
- Execute via `InternalExecute<TCommand>(options, configureCommand: cmd => ((IStageEventSource)cmd).StageChanged += OnStageChanged)`. Using the generic overload also gives the correct per-request environment-bound command instance (MCP `AGENTS.md` rule for environment-sensitive tools; deploy/uninstall are environment-bound).
- `OnStageChanged` serialises the `ClioStageEvent` into `ProgressNotificationParams.Meta["clioStageEvent"]`, sets `Progress = stage.index`, `Total = stage.total`, `Message = stage.message`, and `SendNotificationAsync("notifications/progress", …)` — no-op when `ProgressToken is null` (byte-for-byte preservation for non-progress clients, mirroring `StartTool`/heartbeat).
- **Verified path:** because `clio-run` / `clio-run-destructive` reuse the same `RequestContext` (fact 2), progress raised by the inner deploy/uninstall tool forwards through the dispatcher unchanged. `deploy-creatio` stays `Destructive=true`, `uninstall-creatio` stays `Destructive=true`; the guided real operation is still gated by the Ring's explicit user click (D8) and clio's destructive-confirmation contract.
- Notification-send failures are swallowed (like `McpLogNotifier`/heartbeat) so streaming never breaks the operation.

### D5 — Ring architecture: raw `_meta` handler → structured adapter → pipeline VM/view

**Decision (clio-ring, `spike/ring-clio-ipc`):**

- **New structured progress path in `ClioRing.Ipc`.** Add `ClioStageEvent` (mirrored record, D2) and a `CallToolAsync` overload taking `IProgress<ClioStageEvent>` (or an event). It registers `RegisterNotificationHandler("notifications/progress", …)` for the duration of the call, correlates by `progressToken`, deserialises `params._meta.clioStageEvent`, applies FR-12 tolerance (unknown fields ignored; `sequence` de-dup + drop out-of-order per `runId`), and raises typed events. The existing `IProgress<string>` overload stays for the read-only workflows already shipped.
- **Pipeline view-model + view.** A `DeployPipelineViewModel` builds its step list from the `manifest` event (no fabricated "pending" steps — AC-09), updates each step's status/duration/message/detail from `stage` events, and renders the terminal state from `run-completed`. A GitHub-Actions-style step list view (Pending/Running/Done/Failed, duration, friendly message, expander for `detail`/`errorCode`) satisfies FR-04/FR-05/FR-07 and AC-05/AC-10/AC-ERR.
- **Main-ring entries** for Deploy and Uninstall (FR-01, AC-01) — primary radial actions, not tray.
- **Guided Install form** (FR-02, AC-02): DB source + Redis source (local or Rancher), build/ZIP, instance name, pre-selected editable free port; **no dry-run control anywhere**.
- **Preflight as the first pipeline step (FR-03, AC-03/AC-04).** Validation/preflight runs internally and appears as a "Check requirements" step in the *same* pipeline. On a problem it shows exactly one human message + one corrective action and does **not** start install; with no problem, install starts immediately on the Install click.
- **Uninstall** (FR-06, AC-06): pick a local environment → Uninstall → simple "Are you sure? Yes/No" (no exact-name typing) → Yes runs the same pipeline; No cancels with no changes.
- **Local-environment picker (OQ-04):** sourced from clio's **registered environments** (via `list-environments`), filtered to local, matching `uninstall-creatio`'s `environment-name` contract. Discovery of orphan IIS sites is explicitly out of scope (a separate concern).

### D6 — Logging & receipts: one event stream, NDJSON + rolled-up summary

**Decision (clio-ring):**

- Logs default to `C:\Tools\clio-ring\Logs`, **appsettings-configurable**, with rotation + redaction and an "Open logs" UI action (FR-17, AC-14).
- **The deployment receipt is reconstructed from the same typed `ClioStageEvent` stream the UI renders** (FR-18, SM-03, AC-13) — single source of truth. **Format (OQ-05):** one **NDJSON** file per `runId` (one JSON line appended as each event arrives — literally the wire stream), plus a final rolled-up JSON summary object (per-stage outcome + duration + terminal outcome) for quick diagnosis. NDJSON is chosen because appending the live stream *is* the receipt (no second derivation can disagree) and it is replayable byte-for-byte for the SM-03 replay test.
- **Rotation:** per-run files, capped by directory age + total size; redaction is inherited from the source (D3) so no secret can reach disk (AC-12).

### D7 — Settings: default clio, dev-clio override, visible identity

**Decision.** Ring settings default to the normal clio build/config; an explicit **dev-clio path override** is supported (FR-19). The UI **visibly identifies which clio is connected** (normal vs dev override) using the handshake identity the predecessor ADR already surfaces (`serverInfo.name/version` + resolved path) (FR-20, AC-15).

### D8 — Safety invariants

**Decision.**
- **No agent-initiated real operation (FR-21, AC-16).** A real install/uninstall starts **only** from the user's Install click or Uninstall "Yes" click. The MCP `deploy-creatio`/`uninstall-creatio` tools remain `Destructive=true` and are not auto-invoked by any agent; the Ring is the sole initiator and only on explicit user action. No dry-run affordance is exposed (PRD non-goal).
- **JIT-only (FR-22, AC-17).** No AOT publish is produced or required until the JIT experience is proven. The reflection-heavy MCP SDK + the new `_meta` deserialisation stay isolated in the non-AOT `ClioRing.Ipc` project (consistent with the predecessor ADR's AOT isolation), so a later AOT pass is not foreclosed.

---

## Alternatives Considered

| Area | Option | Status |
|------|--------|--------|
| Transport (D1) | JSON blob inside progress `Message` | Rejected — not typed, violates signed-off requirement + FR-10 ("no JSON-in-message"). |
| Transport (D1) | Bespoke `notifications/clio/stage` method | Rejected — non-standard; loses progress-token correlation; unknown-method drop risk. Unneeded once `_meta` was proven. |
| Transport (D1) | Bump SDK / adopt `structuredContent` for progress | Rejected — `structuredContent` is result-typing (IPC ADR gap #2), orthogonal to progress; `_meta` already works in 1.4.0. |
| Contract sync (D2) | Shared NuGet contract package across repos | Rejected — heavier than a one-record surface; repos ship on independent cadence. Chosen: mirrored record + `schemaVersion` gate + committed JSON fixture contract test. |
| Stage keys (D2) | Enum ordinals | Rejected — reordering/insertion breaks cross-version receipt comparison. Chosen: stable kebab-case string constants. |
| Seam (D3) | Injected `IProgress<ClioStageEvent>` reporter | Rejected — awkward per-call scoping against startup-injected singleton commands; the event + `configureCommand` seam is the proven, CI-covered pattern. |
| Ring consume (D5) | Reuse `IProgress<ProgressNotificationValue>` overload | Rejected — SDK drops `_meta` (fact 6). Chosen: raw `notifications/progress` handler reading `params._meta`. |
| Receipt (D6) | Single derived JSON built after the run | Rejected — a second derivation can disagree with the UI. Chosen: append the live event stream as NDJSON (+ rolled-up summary). |

---

## Consequences

**Positive**
- Ring UI, receipts, and QA assertions all derive from one typed, versioned, ordered stream — UI and diagnostics cannot disagree (SM-03).
- Additive, forward-compatible MCP change: unknown `_meta` is ignored by every existing consumer; no CLI flags change; no SDK bump.
- Reuses the proven `StartTool` forwarding + `InternalExecute<TCommand>(configureCommand:)` seam; small, familiar surface.
- Two uninstall correctness bugs (silent config-read skip; misleading profile-deletion doc) are fixed as a by-product of honest stage reporting.

**Trade-offs / negative**
- The event contract is duplicated across two repos; kept honest only by `schemaVersion` + the committed JSON fixture contract test on each side (a discipline, not a compiler guarantee).
- `_meta` correlation on the Ring requires a raw notification handler (more code than the `IProgress` shortcut) and careful `progressToken`↔`runId` correlation for concurrent calls.
- Stage-level instrumentation adds an emitter wrapper around each stage in two services; care needed so a stage refactor keeps its `stageId` stable.

**Breaking change:** No. No CLI flags added/renamed; MCP surface is additive; the uninstall change is a doc correction + more-honest failure reporting (no behavior regression on the happy path). No `RELEASE.md` migration entry required beyond noting the additive `_meta` progress envelope.

**Cross-cutting MCP/doc obligations (per repo policy):** update `docs/McpCapabilityMap.md` for `deploy-creatio`/`uninstall-creatio`; align command help/docs (`clio/help/en/*.txt`, `clio/docs/commands/*.md`, `Commands.md`) including the uninstall AppPool-profile doc correction (FR-16); add/extend `clio.mcp.e2e` coverage asserting the manifest/stage/run-completed `_meta` sequence via a notification handler. Use the `document-command`, `create-mcp-tool`, and `test-mcp-tool` skills.

---

## Risks

| Risk | Mitigation |
|------|-----------|
| `_meta` reordered/dropped on a long deploy (A-01) | Ordered `manifest` + terminal `run-completed` bound the stream; `sequence` de-dup + out-of-order drop (FR-12); pipeline can reconcile against the manifest even if intermediate `stage` events are lost. |
| Manifest misrepresents runtime order (A-02) | Manifest generated from the resolved execution path, not hardcoded. |
| Concurrent tool calls collide on progress | Correlate strictly by `progressToken`→`runId` in the Ring handler; ignore events for unknown runs. |
| Secret leaks into an event/receipt field (A-05) | Single redaction boundary at the emitter + deny-list assertion tests (AC-12). |
| Stage `stageId` drift across clio versions | String constants in one place; contract test pins the manifest ids. |
| JIT perf insufficient to prove the experience (A-06) | That is the intended gate signal; AOT stays blocked until JIT passes (D8). |

---

## Testing Strategy (pointer)

Full plan: `spec/ring-guided-deploy/test-plans/tp-ring-guided-deploy.md` (to be authored by qa-planner).

| Layer | Framework | Coverage | Where |
|-------|-----------|----------|-------|
| Unit (clio) | NUnit + NSubstitute + FluentAssertions, `[Category("Unit")]`, `Module=McpServer`/`Command`/`Common` | envelope serialise/`_meta` shape; manifest generation; failure cascade (active=failed, rest=skipped); redaction guard; the two uninstall corrections | `clio.tests/Command/McpServer/`, `clio.tests/` |
| Unit (Ring) | ClioRing.Tests | `_meta` deserialise; unknown-field tolerance; duplicate/out-of-order `sequence` drop; pipeline VM state transitions; receipt-from-stream replay equality (SM-03) | `ClioRing.Tests` |
| Integration | real FS | receipt NDJSON write + rotation + redaction; logs path override | `clio.tests/` / `ClioRing.Tests` |
| E2E (MCP) | `clio.mcp.e2e` (NOT in CI — manual) | real `clio mcp-server`: assert `deploy-creatio`/`uninstall-creatio` emit manifest→stage→run-completed `_meta` via a `notifications/progress` handler | `clio.mcp.e2e/` |

---

## Implementation Plan

### clio — files to create

| File | Purpose |
|------|---------|
| `clio/Command/McpServer/Progress/ClioStageEvent.cs` | Versioned envelope DTO (`record`) + `manifest`/`stage`/`run-completed` shapes |
| `clio/Command/McpServer/Progress/StageIds.cs` | Stable kebab-case `stageId` string constants (deploy + uninstall) |
| `clio/Command/McpServer/Progress/IStageEventSource.cs` | `event EventHandler<ClioStageEvent> StageChanged` |
| `clio/Command/McpServer/Progress/StageEventEmitter.cs` | Per-run `runId`/`sequence`, manifest build, transition + failure-cascade emission, single redaction boundary |
| `clio.tests/Command/McpServer/StageEventEmitterTests.cs` | Emitter/manifest/cascade/redaction unit tests |
| `clio.mcp.e2e/DeployUninstallProgressTests.cs` | E2E `_meta` sequence assertion |

### clio — files to modify

| File | Change |
|------|--------|
| `clio/Command/CreatioInstallCommand/CreatioInstallerService.cs` | Implement `IStageEventSource`; wrap the 8 deploy stages with emitter transitions |
| `clio/Common/CreatioUninstaller.cs` | Add stage callback; config-read FAILED+abort; profile skipped/not-supported |
| `clio/Command/UninstallCreatioCommand.cs` | Implement `IStageEventSource`; own `runId`/manifest; re-raise uninstaller events |
| `clio/Command/McpServer/Tools/InstallerCommandTool.cs` | Inject `McpServer`+`RequestContext`; `InternalExecute<InstallerCommand>(…, configureCommand: forward)` |
| `clio/Command/McpServer/Tools/UninstallCreatioTool.cs` | Same forwarding pattern |
| `clio/BindingsModule.cs` | Register any new interfaces (if not covered by existing command registration) |
| `docs/McpCapabilityMap.md`, uninstall help/docs, `Commands.md` | MCP map + FR-16 doc correction |

### clio-ring (`spike/ring-clio-ipc`) — files to create/modify

| File | Change |
|------|--------|
| `ClioRing.Ipc/ClioStageEvent.cs` (new) | Mirrored envelope record + committed JSON fixture |
| `ClioRing.Ipc/ClioIpcClient.cs` | New `CallToolAsync(…, IProgress<ClioStageEvent>)` overload using `RegisterNotificationHandler("notifications/progress", …)`, `_meta` deserialise, `sequence` de-dup/order tolerance |
| `ClioRing/**` (new VM + view) | `DeployPipelineViewModel` + GitHub-Actions step-list view; guided Install form; Uninstall Yes/No; main-ring Deploy/Uninstall entries; preflight as first step |
| Ring logging/settings | Receipt NDJSON writer from the same stream; `C:\Tools\clio-ring\Logs` default + appsettings override + "Open logs"; dev-clio override + visible clio identity |

### Pre-implementation checklist

- [ ] No new CLI options introduced; if any added during impl, kebab-case (CLIO001).
- [ ] Deploy/uninstall tools switched to `InternalExecute<TCommand>(configureCommand:)`, `McpServer`+`RequestContext` injected; no-op when `ProgressToken` is null.
- [ ] `deploy-creatio`/`uninstall-creatio` remain `Destructive=true`; no agent auto-invocation.
- [ ] Envelope `schemaVersion=1`; `stageId`s are string constants; contract JSON fixture committed identically in both repos.
- [ ] Redaction boundary + deny-list assertion in place; no secret in any field.
- [ ] `docs/McpCapabilityMap.md`, command help/docs, uninstall doc correction updated; `clio.mcp.e2e` coverage added.
- [ ] Behavior classes resolved via DI (no `new` except DTO records); no new `CLIO*` warnings.
- [ ] JIT-only; MCP SDK + `_meta` deserialise isolated in non-AOT `ClioRing.Ipc`.

---

## Resolved Open Questions (from PRD)

| # | Resolution |
|---|-----------|
| OQ-01 | Envelope `record` in `clio/Command/McpServer/Progress/`; Ring mirrors it; sync by `schemaVersion` + committed JSON fixture contract test (no shared binary). (D2) |
| OQ-02 | `stageId` = stable kebab-case string constants, not enum ordinals. (D2) |
| OQ-03 | **No transport gap.** `_meta` on `ProgressNotificationParams` is empirically supported in 1.4.0 (probe-verified); progress-during-in-flight already works (StartTool). FR-08 unblocked, no SDK bump. (D1) |
| OQ-04 | Uninstall picker sources clio's registered environments (local-filtered), matching the `environment-name` contract; orphan IIS-site discovery is out of scope. (D5) |
| OQ-05 | Receipt = per-run **NDJSON** of the live event stream + a final rolled-up JSON summary; per-run rotation capped by age + total size. (D6) |
| OQ-06 | Manifest includes conditional stages (`conditional: true`, `total` = full manifest length); condition-off ⇒ `skipped skipReason=not-applicable`; failure cascade ⇒ `skipped skipReason=after-failure` — distinguished, never conflated. (D2) |
