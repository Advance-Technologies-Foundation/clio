# PRD: Ring-Guided Creatio Deploy & Uninstall (Typed MCP Progress Pipeline)

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-11
**Jira**: TBD

---

## Autonomous Mode Summary

This PRD was produced in autonomous mode (`--auto`); no user checkpoints were taken. Decisions made without pausing for confirmation:

- **AD-01** — Feature name kept as `ring-guided-deploy` (per invocation). File saved as `spec/ring-guided-deploy/prd/prd-ring-guided-deploy.md`.
- **AD-02** — Treated the stakeholder brief as already signed off (per instruction); scope captured verbatim, not re-litigated. All 9 product requirements and the signed-off event contract are transcribed into FRs.
- **AD-03** — Assigned FR/AC/SM/A/OQ IDs by grouping: guided UX (FR-01..07), typed event contract (FR-08..12), cross-repo clio changes (FR-13..16), logging/settings (FR-17..20), safety/non-initiation (FR-21..22).
- **AD-04** — Classified the typed MCP event contract as a **Must** functional requirement (FR-08..12), because both the UI pipeline and the on-disk deployment receipt consume the same stream — the contract is the load-bearing interface, not an implementation detail.
- **AD-05** — Recorded AOT publish, agent-initiated deploys, and real AppPool-profile deletion as explicit **non-goals** (per stakeholder), not deferred FRs.
- **AD-06** — Verified the two source stage lists against `clio/Command/CreatioInstallCommand/CreatioInstallerService.cs` (Execute) and `clio/Common/CreatioUninstaller.cs` before writing; both files exist and are referenced as audit facts.
- **AD-07** — Captured cross-repo scope: `C:\Projects\clio` (typed events over MCP) + `C:\Projects\clio\clio-ring` (Ring UX, branch `spike/ring-clio-ipc`). No code written — PRD only.
- **AD-08** — Feature touches the MCP surface (progress `_meta` envelope on deploy/uninstall tools); flagged `docs/McpCapabilityMap.md` and `clio.mcp.e2e` alignment per repo MCP maintenance policy.

Recommended next step: `architect-agent spec/ring-guided-deploy/prd/prd-ring-guided-deploy.md --auto`.

---

## Problem Statement

The clio-ring app today treats **Deploy Creatio** and **Uninstall** as a CLI-caller wizard: it shells out to clio, then parses console/JSON output to guess what happened. For a human standing in front of the Ring this is opaque — there is no honest, real-time view of which deploy stage is running, how long it took, or what to do when something fails; and the "dry-run" plumbing leaks into the UI. Meanwhile clio already runs a well-defined ordered sequence of deploy and uninstall stages internally, but emits no typed, ordered progress a UI can trust — so the Ring and any diagnostic log can drift out of sync with reality. Now that clio source changes are authorized, we can make clio the single source of truth for stage progress and turn the Ring into a guided, GitHub-Actions-style step pipeline.

## Goals

- [ ] Goal 1 — Make Deploy/Uninstall a guided human experience reachable directly from the main radial Ring. Success metric **SM-01**: 100% of Deploy and Uninstall flows are initiated from the main ring (not tray/taskbar) and complete the full guided form → pipeline path without the user ever seeing a raw CLI console. / Counter: no increase in clicks-to-install versus the current wizard for the happy path (target ≤ the current step count).
- [ ] Goal 2 — Replace output-parsing with typed, ordered progress events from clio over MCP. Success metric **SM-02**: 100% of stage/progress/terminal state rendered by the Ring is derived from typed `_meta` envelope events; zero UI state is derived from parsing console text or ad-hoc JSON-in-message. / Counter: no regression in deploy/uninstall success rate versus the current CLI path.
- [ ] Goal 3 — UI and on-disk diagnostics can never disagree. Success metric **SM-03**: the deployment receipt written to the logs directory is reconstructed from the SAME typed event stream the UI renders; a byte-level replay test shows identical stage outcomes/durations in UI model and receipt for 100% of recorded runs. / Counter: receipt generation adds no secret material (redaction assertion passes).
- [ ] Goal 4 — Failures are actionable, not cryptic. Success metric **SM-04**: on any preflight or stage failure the Ring shows exactly ONE human-readable message plus a corrective action, with technical detail available on expand; measured by 100% of failure test scenarios producing a non-empty `Message` + corrective action and a populated `detail`/`errorCode` behind the expander. / Counter: happy-path UI shows no error affordance and no expander noise.

## Non-goals

- Will NOT: ship an AOT-published Ring — **no AOT publish until the JIT experience is proven** (explicit stakeholder gate). JIT-only for this feature.
- Will NOT: allow any agent (AI or automation) to initiate a real install/uninstall — **only the user's Install click or Uninstall "Yes" click** starts a real operation.
- Will NOT: implement real AppPool-profile deletion — profile cleanup is surfaced as an explicit skipped/not-supported step only when a profile exists; actual deletion is a separate change. This PRD also corrects the inaccurate doc that implies deletion happens.
- Will NOT: expose dry-run controls, dry-run toggles, or any dry-run affordance in the guided Install form or pipeline UI.
- Will NOT: require exact-name typing to confirm Uninstall — a simple Yes/No confirmation is the agreed UX.
- Will NOT: redesign or replace clio's deploy/uninstall stage logic itself — stages are instrumented and surfaced as-is; behavior changes are limited to the two named uninstall corrections (config-read failure = visible FAILED + safe abort; profile step = explicit skipped/not-supported).
- Will NOT: build a general-purpose MCP eventing framework beyond the deploy/uninstall progress contract.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| developer (Ring user) | Deploy Creatio and Uninstall as primary actions on the main radial ring | I can start a deploy without hunting through tray menus or a terminal |
| developer (Ring user) | one guided Install form (DB+Redis source, build/ZIP, instance name, pre-selected free port) then a single Install click | I install without knowing clio flags or running preflight myself |
| developer (Ring user) | a GitHub-Actions-style step pipeline with per-step status, duration, friendly message and expandable detail | I can see exactly where a deploy is and understand a failure without reading logs |
| developer (Ring user) | on a preflight problem, ONE human message plus the fix | I know what to change instead of decoding a stack trace |
| developer (Ring user) | Uninstall a local environment behind a simple Yes/No confirm | I can tear down an instance safely and quickly |
| QA engineer | a versioned, ordered typed event contract from clio | I can assert deploy/uninstall progress deterministically instead of scraping console output |
| QA engineer / support | a redacted deployment receipt in a known logs folder, built from the same events the UI showed | I can diagnose a user's failed run and trust it matches what they saw |
| CI / diagnostics author | secrets excluded at source and unknown-field-tolerant consumers | receipts and future consumers stay safe and forward-compatible |

## Feature Requirements

### Guided UX (clio-ring)

| ID | Requirement | Priority |
|----|------------|----------|
| FR-01 | Deploy Creatio and Uninstall are **primary actions on the main radial ring** (not tray/taskbar); both flows are fully reachable from the main ring | Must |
| FR-02 | A single guided **Install form**: choose DB source + Redis source (local **or** Rancher), choose build/ZIP, enter instance name, accept or edit a **pre-selected free port**, then **Install**. No dry-run controls anywhere on the form | Must |
| FR-03 | The Ring runs validation/preflight **internally** before install. On a problem it shows **exactly one** human-readable message plus the corrective action and does not start the install; with no problem it installs immediately after the Install click | Must |
| FR-04 | The install/uninstall UI is a **step pipeline**: each step shows Pending / Running / Done / Failed, a duration, a friendly message, and an expander for technical detail. Steps map 1:1 to the real clio deploy/uninstall stages | Must |
| FR-05 | Deploy pipeline steps (from `CreatioInstallerService.Execute`): Stage build (only when network) → Unzip build → Copy application files → Restore database → Deploy application (IIS / dotnet) → Configure connection strings (DB + Redis) → Register environment → Wait for server ready | Must |
| FR-06 | Uninstall flow: pick a **local** environment → Uninstall → **simple "Are you sure? Yes/No"** → Yes runs the same step pipeline; No cancels. No exact-name typing | Must |
| FR-07 | Uninstall pipeline steps (from `CreatioUninstaller`): Stop IIS site → Read configuration → Delete IIS site + app pool → Drop database → Delete application files → **Unregister environment (final, only after cleanup succeeds)**. Config-read failure is a **visible FAILED step + safe abort** (never silent-skip-but-report-success); AppPool profile cleanup is an **explicit skipped/not-supported** step shown only if a profile exists | Must |

### Typed Event Contract (clio ⇄ clio-ring, over MCP)

| ID | Requirement | Priority |
|----|------------|----------|
| FR-08 | clio emits **typed per-stage progress events over the MCP progress `_meta` envelope** — not parsed console text and not JSON embedded in a message. The Ring renders exclusively from these typed events | Must |
| FR-09 | Envelope is **versioned + ordered** and carries: `schemaVersion`, `eventType` (`manifest` \| `stage` \| `run-completed`), `runId`, `sequence`, `operation` (`deploy` \| `uninstall`) | Must |
| FR-10 | `stage` events carry `{ stageId, name (user-language), index, total, status (running \| done \| failed \| skipped), startedAtUtc?, durationMs?, message, detail?, errorCode? }`. Human text lives in `message`; **no JSON-in-message** | Must |
| FR-11 | A **manifest** event defines the ordered steps up front (so the UI shows real steps, not fake per-step "pending" events). A terminal **run-completed** event carries `{ outcome, friendly summary, detail/errorCode, derivedUrl/path }`. On failure: the active stage = `failed`, remaining stages = `skipped`, then `run-completed` | Must |
| FR-12 | **Consumers tolerate unknown fields** and **ignore duplicate / out-of-order `sequence`**. **Secrets are excluded AT SOURCE** (connection strings, credentials, tokens never enter any event field) | Must |

### Cross-repo clio changes (`C:\Projects\clio`)

| ID | Requirement | Priority |
|----|------------|----------|
| FR-13 | Instrument `CreatioInstallerService.Execute` to emit the manifest, per-stage, and run-completed events for the deploy stages in FR-05 via the MCP progress channel | Must |
| FR-14 | Instrument `CreatioUninstaller` to emit the manifest, per-stage, and run-completed events for the uninstall stages in FR-07, including the two corrections (config-read FAILED+abort; profile skipped/not-supported) | Must |
| FR-15 | Define the typed event envelope as a shared, versioned contract type in clio and emit it through the MCP tool progress `_meta` for the deploy and uninstall MCP tools; secret redaction enforced at the emission boundary | Must |
| FR-16 | **Fix the inaccurate uninstall doc** that implies AppPool-profile deletion happens today (real deletion is a separate change); keep MCP surface + docs aligned (`docs/McpCapabilityMap.md`, command help/docs, `clio.mcp.e2e`) | Must |

### Logging & Settings (clio-ring)

| ID | Requirement | Priority |
|----|------------|----------|
| FR-17 | Logs default to `C:\Tools\clio-ring\Logs`, **configurable via appsettings**, with rotation + redaction and a UI **"Open logs"** action | Must |
| FR-18 | A **deployment receipt** is written to the logs directory and is reconstructed from the **same typed event stream** the UI renders (UI and diagnostics cannot disagree) | Must |
| FR-19 | Ring settings **default to the normal clio build/config**, with an explicit **dev-clio path override** | Must |
| FR-20 | The UI **visibly identifies which clio is connected** (normal vs dev-clio override) | Must |

### Safety / Non-initiation

| ID | Requirement | Priority |
|----|------------|----------|
| FR-21 | No agent initiates a real install or uninstall; a real operation starts **only** from the user's Install click or Uninstall "Yes" click | Must |
| FR-22 | JIT-only delivery for this feature; **no AOT publish** is produced or required until the JIT experience is proven | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| No new/changed CLI **flags** | This feature adds typed **MCP progress events**, not CLI options. The deploy/uninstall verbs and their flags are unchanged | No |
| MCP surface (additive) | Deploy and uninstall MCP tools emit a versioned typed progress `_meta` envelope (manifest / stage / run-completed). Consumers ignore unknown fields, so this is additive and forward-compatible | No |
| Docs correction | Uninstall docs corrected re: AppPool-profile deletion (doc-only; behavior unchanged) | No |

All flags: **kebab-case only** (CLIO001 enforced). No new flags are introduced by this feature; any option added during implementation must be kebab-case.

This feature touches the MCP surface (deploy + uninstall tool progress). Keep `docs/McpCapabilityMap.md` and `clio.mcp.e2e` coverage aligned per repo MCP maintenance policy; reference `docs/McpCapabilityMap.md` for the affected tools.

## Acceptance Criteria

- [ ] AC-01: Given the main radial ring, when the user opens it, then Deploy Creatio and Uninstall are present as primary ring actions (not in tray/taskbar) and both flows can be completed from the ring.
- [ ] AC-02: Given the guided Install form, when the user opens it, then it exposes DB source, Redis source (local or Rancher), build/ZIP choice, instance name, and a pre-selected editable free port, and exposes NO dry-run control.
- [ ] AC-03: Given a preflight problem detected internally, when the user clicks Install, then the install does NOT start and the Ring shows exactly one human-readable message plus a corrective action.
- [ ] AC-04: Given a valid form with no preflight problem, when the user clicks Install, then the install starts immediately (no dry-run, no extra confirmation) and the step pipeline appears.
- [ ] AC-05: Given a running deploy, when the pipeline renders, then it shows the FR-05 stages in order, each with a status (Pending/Running/Done/Failed), a duration once complete, a friendly message, and an expandable technical detail — all sourced from typed events.
- [ ] AC-06: Given the user selects a local environment and clicks Uninstall, when confirmation appears, then it is a simple "Are you sure? Yes/No"; No cancels with no changes; Yes runs the uninstall step pipeline.
- [ ] AC-07: Given an uninstall where reading configuration fails, when the pipeline runs, then the "Read configuration" step is shown FAILED and the operation safely aborts (environment is NOT unregistered and the run is NOT reported as success).
- [ ] AC-08: Given an uninstall where an AppPool profile exists but deletion is unsupported, when that step runs, then it is shown as explicitly skipped/not-supported (never silently succeeded).
- [ ] AC-09: Given any deploy or uninstall run, when clio emits progress, then the Ring receives a `manifest` event first (ordered steps up front), then ordered `stage` events, then a terminal `run-completed` event; the Ring renders no fabricated per-step "pending" events.
- [ ] AC-10: Given a stage fails, when clio emits events, then the active stage is `failed`, all remaining stages are `skipped`, a `run-completed` event with outcome=failure follows, and the Ring reflects exactly that.
- [ ] AC-11: Given events arrive with an unknown extra field or a duplicate/out-of-order `sequence`, when the Ring consumes them, then unknown fields are tolerated and duplicate/out-of-order events are ignored (no crash, no double-render).
- [ ] AC-12: Given any event or receipt, when inspected, then no secret material (connection strings, credentials, tokens) appears in any field — redaction is enforced at source.
- [ ] AC-13: Given a completed run, when the deployment receipt is written to the logs directory, then it is reconstructed from the same typed event stream and its stage outcomes/durations match the UI for that run.
- [ ] AC-14: Given default settings, when the app writes logs, then they go to `C:\Tools\clio-ring\Logs` with rotation + redaction; when appsettings overrides the path, logs go to the configured path; the "Open logs" action opens the active directory.
- [ ] AC-15: Given a dev-clio path override is configured, when the Ring is running, then the UI visibly identifies which clio (normal vs dev override) is connected; with no override it defaults to the normal clio build/config.
- [ ] AC-16: Given any agent (AI/automation) attempts to trigger deploy/uninstall, when no user Install/Yes click occurred, then no real operation runs.
- [ ] AC-17: Given this feature ships, when the build artifact is produced, then it is a JIT build; no AOT publish is required or produced.
- [ ] AC-ERR: Given a deploy/uninstall failure, when the terminal event is emitted, then `run-completed` carries a friendly summary plus `detail`/`errorCode`, the Ring shows one human message + corrective action with technical detail behind the expander, and the process/receipt records a non-success outcome.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | The MCP progress `_meta` channel can carry the structured typed envelope reliably and in order to the Ring for a long-running deploy | If the transport drops/reorders beyond what `sequence` de-dup handles, the pipeline may stall; mitigated by ordered manifest + terminal event and out-of-order tolerance (FR-11/FR-12). |
| A-02 | The deploy/uninstall stages in FR-05/FR-07 accurately reflect current source (`CreatioInstallerService.Execute`, `CreatioUninstaller`) | If stages differ or reorder at runtime, the manifest misrepresents progress; mitigated by generating the manifest from the actual execution path, not a hardcoded list. |
| A-03 | A free port can be reliably pre-selected during preflight for the Install form | If port detection is flaky, the pre-filled port may collide; user can edit it (FR-02), and preflight re-validates. |
| A-04 | Preflight problems can be expressed as a single human message + one corrective action | Some failures may have multiple causes; corrective action then points to the most likely fix + logs. |
| A-05 | Secrets can be excluded at the emission boundary without losing diagnostic value | Over-redaction could hide useful detail; balanced by `detail`/`errorCode` fields carrying non-secret technical context. |
| A-06 | JIT performance of the Ring pipeline UI is acceptable to prove the experience before any AOT work | If JIT startup/latency is poor, the "prove it first" gate may be hard to clear; that is the intended signal (AOT stays gated until this passes). |
| A-07 | The clio-ring app already has (or can add) an MCP client capable of subscribing to tool progress events | If not, IPC wiring on branch `spike/ring-clio-ipc` must land first; tracked as a dependency. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Exact shape/type for the shared event envelope in clio (record contract) and where it lives (clio.Common vs MCP layer) so both emission and the receipt reader share one type | Architect | Before ADR |
| OQ-02 | How `stageId` values are assigned and kept stable across clio versions (enum vs string keys) so receipts remain comparable over time | Architect | Before ADR |
| OQ-03 | Is the MCP progress `_meta` `structuredContent` path available in the current clio MCP transport, or is a transport gap to close first (see clio MCP transport notes) | Architect / implementer | Before FR-08 implementation |
| OQ-04 | How the Ring resolves "local environment" list for the Uninstall picker (registered environments vs discovered IIS sites) | Architect | Before FR-06 implementation |
| OQ-05 | Receipt file format (JSON/NDJSON) and rotation policy specifics for the logs directory | Architect | Before FR-18 implementation |
| OQ-06 | Whether the manifest `total`/`index` accounts for conditionally-executed stages (e.g. "Stage build" only when network) — how skipped-by-condition differs from failure-`skipped` | Architect | Before ADR |

## Dependencies

- Spans **two repos**:
  - `C:\Projects\clio` — typed event contract + instrumentation of deploy/uninstall (FR-08..16). Audit facts: deploy stages in `clio/Command/CreatioInstallCommand/CreatioInstallerService.cs` (`Execute`); uninstall stages in `clio/Common/CreatioUninstaller.cs`.
  - `C:\Projects\clio\clio-ring` — Ring guided UX, step pipeline, settings, logging (FR-01..07, FR-17..22), branch **`spike/ring-clio-ipc`**.
- Depends on: the clio MCP tool progress transport carrying the typed `_meta` envelope (see OQ-03); an MCP client in the Ring app (A-07).
- Depends on: existing deploy/uninstall stage logic remaining the source of truth (instrumented, not rewritten).
- Blocks: ADR `spec/ring-guided-deploy/adr/adr-ring-guided-deploy.md`, stories `spec/ring-guided-deploy/stories/story-ring-guided-deploy-*.md`, and test plan `spec/ring-guided-deploy/test-plans/tp-ring-guided-deploy.md`.
- Related MCP maintenance: `docs/McpCapabilityMap.md`, deploy/uninstall command help + docs, and `clio.mcp.e2e` coverage must be updated alongside FR-13..16.
