# Story 6: First cohort behind an off-by-default flag

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
the retry-safe stdio reads to run in workers, behind a flag

## So that
the exact commands that wedged environments stop wedging them, without betting the default dispatcher on it

## Design
- Cohort: `get-page`, `list-pages`, `list-app-sections`, `get-schema`, `get-related-page-addon`, SQL/OData — precisely the commands agents were forced off in [creatio-ai-app-development-toolkit#93](https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit/pull/93).
- **Off by default.** This PR must not switch the default dispatcher, enable `mcp-http` proxying, proxy destructive/deploy/uninstall/sticky operations, delete any existing deadline or guard, or change ClioRing behaviour.
- The shipping artifact is the **C# port of the lab harness** in `clio.mcp.e2e` — the deterministic stub with request counters, not the Python spike.

## Acceptance Criteria
- [ ] AC-01 — **Wedge scenario passes on request counters**: A/B/C each issue their own backend request and end at the budget; **D succeeds with ≥ 1 request** (TC-E-601). Timing assertions are explicitly insufficient — the wedged system also finishes at 12 s.
- [ ] AC-02 — Flag **off** ⇒ byte-identical behaviour to today for every cohort tool (TC-E-602).
- [ ] AC-03 — Cohort tools return identical results through the worker and in-process (TC-E-603).
- [ ] AC-04 — Environment recovers as soon as the backend does, with no restart (TC-E-604).
- [ ] AC-05 — Nothing outside the cohort changes route.

## Tests
E2E TC-E-601…604 in `clio.mcp.e2e`. **Must be run on TeamCity** (`Team_Atf_ClioMcpE2eTests`) — GitHub CI does not run this suite — and the result stated in the PR.
