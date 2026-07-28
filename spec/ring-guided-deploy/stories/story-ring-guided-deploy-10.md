# Story 10: Ring logging + NDJSON deployment receipt from the same event stream

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio\clio-ring` (branch `spike/ring-clio-ipc`)
**FR coverage**: FR-17 (logs default `C:\Tools\clio-ring\Logs`, appsettings-configurable, rotation + redaction, "Open logs"), FR-18 (receipt reconstructed from the same typed event stream)
**AC coverage**: AC-12 (no secrets on disk), AC-13 (receipt matches UI, same stream), AC-14 (logs path + rotation + redaction + "Open logs")
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D6)
**Status**: review
**Size**: M (half day)
**Depends on**: story-ring-guided-deploy-6
**Blocks**: —

---

## As a

QA engineer / support diagnosing a user's run

## I want

logs in a known folder (default `C:\Tools\clio-ring\Logs`, appsettings-configurable) with rotation + redaction and an "Open logs" action, and a per-run deployment receipt written as NDJSON of the same typed event stream the UI renders plus a rolled-up summary

## So that

I can trust the on-disk receipt matches exactly what the user saw, with no secret material and no second derivation that could disagree

---

## Acceptance Criteria

- [ ] **AC-01** — Given default settings, when the app writes logs, then they go to `C:\Tools\clio-ring\Logs`; when appsettings overrides the path, logs go to the configured path (AC-14).
- [ ] **AC-02** — Given logs are written, when the directory grows, then rotation applies (per-run files capped by directory age + total size) and redaction is applied (AC-14).
- [ ] **AC-03** — Given the "Open logs" UI action, when clicked, then it opens the active logs directory (default or overridden) (AC-14).
- [ ] **AC-04** — Given a run, when the receipt is written, then it is a per-`runId` NDJSON file with one JSON line appended as each `ClioStageEvent` arrives (literally the wire stream), plus a final rolled-up JSON summary (per-stage outcome + duration + terminal outcome) (FR-18 / OQ-05).
- [ ] **AC-05** — Given a completed run, when the receipt is replayed, then its per-stage outcomes/durations match the UI model for that run byte-for-byte (SM-03 replay equality / AC-13) — because both derive from the same stream.
- [ ] **AC-06** — Given the receipt/logs are inspected, when checked for secrets, then no connection string, credential, or token appears in any field (redaction inherited from the clio emitter source, D3 — nothing to re-redact, but the Ring adds no secret material) (AC-12).
- [ ] **AC-ERR** — Given a failed run, when the receipt is written, then it records the failed stage + skipped-after-failure stages + terminal `run-completed outcome=failure` (non-success outcome recorded).

## Implementation Notes

From ADR D6:

- Ring logging config — logs default to `C:\Tools\clio-ring\Logs`, appsettings-configurable path; rotation (per-run files capped by age + total size); redaction; "Open logs" UI action opening the active directory.
- Receipt writer — subscribes to the same typed `ClioStageEvent` stream the pipeline VM renders (story 6/7). Writes one NDJSON line per event to a per-`runId` file (append the live wire stream — no second derivation), then a final rolled-up JSON summary object.
- The NDJSON stream is replayable byte-for-byte for the SM-03 replay test (build a pipeline model from the file and assert equality with the UI model).
- Secrets are excluded at source (clio emitter, D3); the Ring writes only what it receives and adds nothing secret.

Key files: `ClioRing/` (receipt NDJSON writer + logging config + "Open logs" action)
Pattern to follow: append-the-stream receipt (D6); consume the typed stream from story 6.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit (`ClioRing.Tests`) | receipt NDJSON = one line per event; rolled-up summary shape; replay-from-file equals UI model (SM-03); no-secret assertion | `ClioRing.Tests/DeploymentReceiptTests.cs` |
| Integration (real FS) | default vs appsettings-overridden path; rotation by age + total size; "Open logs" targets active dir | `ClioRing.Tests/LoggingIntegrationTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`. Use temp dirs (cross-OS safe) for FS tests.

## Definition of Done

- [ ] Logs default to `C:\Tools\clio-ring\Logs`, appsettings-configurable, with rotation + redaction
- [ ] "Open logs" action opens the active directory
- [ ] Receipt = per-run NDJSON of the same typed event stream + rolled-up summary
- [ ] SM-03 replay equality (receipt matches UI) proven by test
- [ ] No secret material on disk (AC-12)
- [ ] Unit + integration tests green; AAA + `because` + `[Description]`; FS tests cross-OS safe
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
