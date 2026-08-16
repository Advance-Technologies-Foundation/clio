# Story 3: Worker mode: no host bootstrap, frozen tool generation

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 3
**Status**: ready-for-dev
**Size**: M

## As a
child `clio mcp-server` serving exactly one call

## I want
to start without the host's bootstrap work and with the parent's tool generation frozen in

## So that
spawn stays near 0.65 s and the worker can never disagree with the parent about which tools exist

## Design
- Worker startup runs **no** host bootstrap: no telemetry flush/drain, no catalog refresh (rule 11). Telemetry stays the parent's job — N workers posting where one process did is a regression, not a feature.
- The enabled-tool generation is resolved once in the parent and passed down **frozen**. A worker that re-read `appsettings.json` could disagree mid-session; four toggles exist today (`deploy-identity`, `process-designer`, `mobile-page-converter`, `watch-compilation`).
- Deadline environment handling is asymmetric and deliberate: a **sticky** worker **keeps** `CLIO_MCP_RESPONSE_DEADLINE_SECONDS` (its in-progress envelope is what returns the call — stripping it turned a 25 s backend call into a 77 s block in the prototype); an **ordinary** worker must not inherit a read-deadline override, because the parent enforces the budget by killing.
- **Credentials:** stdio workers read `appsettings.json` directly and receive only the environment **name** — no secret crosses the channel. Secret material never appears on a command line (R-1). The HTTP channel is Stage 5.
- The worker builds its client through the **same** `ApplicationClientFactory` path as the parent (R-3). A second construction site is how a bearer principal silently became `Supervisor` once before.

## Acceptance Criteria
- [ ] AC-01 — No host bootstrap in worker mode (TC-U-301).
- [ ] AC-02 — Frozen tool generation; a mid-session toggle change does not alter the worker's tool set (TC-U-302).
- [ ] AC-03 — Sticky keeps `CLIO_MCP_RESPONSE_DEADLINE_SECONDS`; ordinary does not inherit a read-deadline override (TC-U-303).
- [ ] AC-04 — No secret in the worker's command line, or its environment block where readable (TC-E-301).
- [ ] AC-05 — **Fail-first identity assertion**: a non-Supervisor bearer principal is observed as that principal at the Creatio end (TC-E-302). "The call succeeded" is explicitly not sufficient.
- [ ] AC-06 — A worker given unusable material **refuses**; it never falls back to registry credentials or a default identity (TC-E-303).

## Tests
Unit TC-U-301…303; E2E TC-E-301…303. **Full unit suite required.**
