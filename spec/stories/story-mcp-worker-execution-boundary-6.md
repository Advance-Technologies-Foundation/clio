# Story 6: First cohort routed to workers

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 6
**Status**: ready-for-dev
**Size**: M

## As a
developer whose agent was forced off MCP onto the CLI

## I want
the retry-safe stdio reads to run in workers

## So that
the exact commands that wedged environments stop wedging them, and the branch's own test runs exercise the path that was actually built

## Design
- Cohort: `get-page`, `list-pages`, `list-app-sections`, `get-schema`, `get-related-page-addon`, SQL/OData — precisely the commands agents were forced off in [creatio-ai-app-development-toolkit#93](https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit/pull/93).
- **No feature toggle** (ADR §5). Cohort membership is the tool's `Location` metadata from Stage 1 — data, not a switch — so the branch tests the path that was built. Still out of bounds here: `mcp-http` proxying, destructive/deploy/uninstall/sticky operations ahead of stages 7-8, deleting any existing deadline or guard (Stage 10), and any change to ClioRing behaviour.
- The shipping artifact is the **C# port of the lab harness** in `clio.mcp.e2e` — the deterministic stub with request counters, not the Python spike.

## Acceptance Criteria
- [ ] AC-01 — **Wedge scenario passes on request counters**: A/B/C each issue their own backend request and end at the budget; **D succeeds with ≥ 1 request** (TC-E-601). Timing assertions are explicitly insufficient — the wedged system also finishes at 12 s.
- [ ] AC-02 — **No unintended route change**: every tool still classified `Location = in-process` behaves byte-identically to master, asserted by the existing e2e suite running unchanged (TC-E-602).
- [ ] AC-03 — Cohort tools return identical results through the worker and in-process, the in-process arm obtained by substituting the metadata reader in DI (TC-E-603).
- [ ] AC-04 — Environment recovers as soon as the backend does, with no restart (TC-E-604).
- [ ] AC-05 — Nothing outside the cohort changes route.
- [ ] AC-06 — **No cohort tool writes `.clio-pages` until story 9's file gate exists.** `get-page` is a `.clio-pages` reader-writer and a child escapes `CwdLock` simply by being another process — the gate must land with (or before) this cohort, or `get-page` stays out of it (cross-call state §5).

## Tests
E2E TC-E-601…604 in `clio.mcp.e2e`. **Must be run on TeamCity** (`Team_Atf_ClioMcpE2eTests`) — GitHub CI does not run this suite — and the result stated in the PR.
