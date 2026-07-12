# Story 8: Guided Install form on the main ring + preflight as first pipeline step

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio\clio-ring` (branch `spike/ring-clio-ipc`)
**FR coverage**: FR-01 (Deploy on main radial ring), FR-02 (guided Install form, no dry-run), FR-03 (internal preflight, one message + corrective action), FR-04 (step pipeline), FR-21 (no agent-initiated real op), FR-22 (JIT-only)
**AC coverage**: AC-01 (Deploy on main ring), AC-02 (form fields, no dry-run control), AC-03 (preflight problem ⇒ one message + fix, no start), AC-04 (valid ⇒ install starts immediately), AC-16 (no agent initiation), AC-17 (JIT build)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D5, D8)
**Status**: review
**Size**: L (full day)
**Depends on**: story-ring-guided-deploy-7
**Blocks**: —

---

## As a

developer (Ring user)

## I want

Deploy Creatio as a primary action on the main radial ring, opening one guided Install form (DB+Redis source, build/ZIP, instance name, pre-selected free port), then a single Install click that runs preflight as the pipeline's first step

## So that

I install without knowing clio flags, without a raw console, and without any dry-run affordance — starting a real install only on my explicit click

---

## Acceptance Criteria

- [ ] **AC-01** — Given the main radial ring, when opened, then "Deploy Creatio" is present as a primary ring action (not tray/taskbar) and the full guided form → pipeline path completes from the ring (AC-01).
- [ ] **AC-02** — Given the guided Install form, when opened, then it exposes: DB source, Redis source (local **or** Rancher), build/ZIP choice, instance name, and a pre-selected editable free port — and exposes **NO** dry-run control anywhere (AC-02).
- [ ] **AC-03** — Given a preflight problem detected internally, when the user clicks Install, then the install does NOT start and the Ring shows exactly ONE human-readable message plus one corrective action (surfaced as the "Check requirements" first pipeline step in Failed state) (AC-03).
- [ ] **AC-04** — Given a valid form with no preflight problem, when the user clicks Install, then the install starts immediately (no dry-run, no extra confirmation) and the step pipeline appears with "Check requirements" as the first step, Done, then the FR-05 stages (AC-04).
- [ ] **AC-05** — Given preflight, when it runs, then it appears as the first step in the SAME pipeline (not a separate modal) and pre-selects/re-validates a free port.
- [ ] **AC-06** (FR-21/AC-16) — Given any agent (AI/automation), when no user Install click occurred, then no real install runs — the Ring is the sole initiator and only on the explicit Install click.
- [ ] **AC-07** (FR-22/AC-17) — Given the build artifact for this feature, when produced, then it is a JIT build; no AOT publish is produced or required.
- [ ] **AC-ERR** — Given the form has invalid input (e.g. port in use, missing name), when Install is clicked, then preflight blocks with one human message + corrective action and no clio call is made.

## Implementation Notes

From ADR D5 (guided Install form, preflight as first step, main-ring entry) + D8 (safety):

- `ClioRing/**` — add the Deploy main-ring entry (primary radial action) and the guided Install form: DB source + Redis source (local/Rancher), build/ZIP, instance name, pre-selected editable free port. NO dry-run control.
- Preflight runs internally and is rendered as the first step ("Check requirements") of the `DeployPipelineViewModel` (story 7). On a problem: exactly one human message + one corrective action; do not invoke clio's `deploy-creatio`. On success: invoke the deploy tool (via story 6's typed `CallToolAsync`) immediately.
- Safety (D8): the real `deploy-creatio` MCP tool is invoked ONLY from the user's Install click; no agent auto-invocation. Tool stays `Destructive=true` on the clio side (story 4).
- JIT-only build; no AOT publish (isolate SDK usage in `ClioRing.Ipc`).

Key files: `ClioRing/` (Deploy ring entry + Install form view/VM), reuses `DeployPipelineViewModel` (story 7) and `CallToolAsync(IProgress<ClioStageEvent>)` (story 6)
Pattern to follow: existing Ring radial-action + form patterns; preflight-as-first-step per ADR D5.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit (`ClioRing.Tests`) | form exposes required fields + NO dry-run control; preflight problem ⇒ no clio call + one message + corrective action; valid ⇒ deploy tool invoked once; free-port pre-select/re-validate | `ClioRing.Tests/InstallFormViewModelTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`. Mock the IPC client; assert the tool is invoked only on the Install click.

## Definition of Done

- [ ] Deploy is a primary main-ring action (not tray); full path completes from the ring
- [ ] Guided Install form exposes DB/Redis source, build/ZIP, name, editable pre-selected free port; NO dry-run control anywhere
- [ ] Preflight is the first pipeline step; problem ⇒ one message + corrective action + no install start; valid ⇒ immediate install
- [ ] Real install starts ONLY on the user's Install click (no agent initiation)
- [ ] JIT build; no AOT publish
- [ ] Unit tests green; AAA + `because` + `[Description]`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
