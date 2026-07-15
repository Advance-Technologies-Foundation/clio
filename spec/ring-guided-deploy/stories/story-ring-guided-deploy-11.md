# Story 11: Ring settings — default clio + dev-clio path override + visible connected-clio identity

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio\clio-ring` (branch `spike/ring-clio-ipc`)
**FR coverage**: FR-19 (default normal clio + dev-clio path override), FR-20 (UI visibly identifies which clio is connected)
**AC coverage**: AC-15 (dev-clio override ⇒ visible identity; no override ⇒ normal clio default)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D7)
**Status**: review
**Size**: M (half day)
**Depends on**: — (Ring settings + handshake identity; independent of the event-contract chain)
**Blocks**: —

---

## As a

developer (Ring user) who sometimes runs a dev build of clio

## I want

Ring settings that default to the normal clio build/config with an explicit dev-clio path override, and a UI that visibly identifies which clio is connected

## So that

I always know whether the Ring is driving the shipped clio or my dev build, and I can point it at a dev build without editing config by hand

---

## Acceptance Criteria

- [ ] **AC-01** — Given no override is configured, when the Ring starts, then it connects to the normal clio build/config by default (FR-19).
- [ ] **AC-02** — Given a dev-clio path override is configured in settings, when the Ring starts, then it connects to the dev clio at that path (FR-19).
- [ ] **AC-03** — Given a dev-clio override is active, when the Ring is running, then the UI visibly identifies that the dev-override clio is connected (not the normal build) (AC-15).
- [ ] **AC-04** — Given no override, when the Ring is running, then the UI visibly identifies that the normal clio is connected (AC-15).
- [ ] **AC-05** — Given the connected clio, when identity is shown, then it uses the handshake identity the predecessor IPC ADR surfaces (`serverInfo.name`/`version` + resolved path) — not a hardcoded label (D7).
- [ ] **AC-ERR** — Given the dev-clio override path is invalid/missing, when the Ring starts, then it surfaces a clear settings error (and does not silently fall back without indicating so).

## Implementation Notes

From ADR D7:

- Ring settings — default to the normal clio build/config; add an explicit dev-clio path override (settings-driven).
- Connected-clio identity — surface it in the UI using the handshake identity from the predecessor IPC ADR (`serverInfo.name`/`version` + resolved path); show normal vs dev-override distinctly.
- This is a settings/identity story; it does not depend on the typed event contract chain (stories 1–7) and can proceed in parallel. It builds on the existing Ring↔clio handshake.

Key files: `ClioRing/` (settings model + settings view + connected-clio identity indicator)
Pattern to follow: existing Ring settings + the predecessor IPC ADR handshake identity surface.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit (`ClioRing.Tests`) | default ⇒ normal clio; override set ⇒ dev clio; identity indicator reflects normal vs dev-override; invalid override ⇒ clear error | `ClioRing.Tests/ClioSettingsViewModelTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`.

## Definition of Done

- [ ] Default = normal clio build/config; explicit dev-clio path override supported
- [ ] UI visibly identifies which clio (normal vs dev-override) is connected, using handshake identity
- [ ] Invalid override path surfaces a clear error (no silent fallback)
- [ ] Unit tests green; AAA + `because` + `[Description]`
- [ ] JIT-only / non-AOT preserved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
