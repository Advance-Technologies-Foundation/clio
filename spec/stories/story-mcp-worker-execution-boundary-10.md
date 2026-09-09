# Story 10: Expand by cohort, then delete the old machinery

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 10
**Status**: ready-for-dev
**Size**: L

## As a
maintainer of clio's MCP server

## I want
the monitor, the read deadline, its gate, `CwdLock` and session pinning deleted once nothing needs them

## So that
this work ends by removing machinery rather than adding a second layer of it

## Design
- Expand cohort by cohort until every environment-touching tool routes to a worker.
- Then delete: the universal per-tenant monitor; `McpReadResponseDeadline` + `McpReadDeadlineGate`; `CwdLock`; session-container pinning; the `CLIO_MCP_READ_DEADLINE_SECONDS` contract.
- **Ordering (cross-call state §5):** the `.clio-pages` gate (Story 9) lands before `CwdLock` goes; the parent-owned reservation (Story 7) lands before per-call children reach `compile-creatio`; the monitor and deadline go **last**, since they are the only bound for any cohort still in-process.
- Revert the CLI-first exception in [creatio-ai-app-development-toolkit#93](https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit/pull/93) — that PR names this work as its own reversal condition. Its two prohibitions (never hand-roll a transport to clio; a timeout means switch transport, not retry) stay regardless.

## Acceptance Criteria
- [ ] AC-01 — Full cohort suite green with all five pieces removed (TC-E-1001).
- [ ] AC-02 — No code path references `CLIO_MCP_READ_DEADLINE_SECONDS` (TC-U-1001).
- [ ] AC-03 — Ordering guard: a test fails if `CwdLock` removal lands before the `.clio-pages` gate (TC-E-1002).
- [ ] AC-04 — Toolkit CLI-first exception reverted and the revert verified against a live stand.
- [ ] AC-05 — Feature DoD met: stalled call cannot affect another; environment recovers when the backend does; no MCP-reachable path waits on Creatio unbounded; transport/auth failure never surfaces as a domain answer.

## Tests
E2E TC-E-1001, TC-E-1002; unit TC-U-1001.
