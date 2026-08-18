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
- [ ] AC-07 — **The stdio-only constraint is asserted by a NEGATIVE test, not by prose** (TC-U-601). A cohort tool resolved on a host serving `mcp-http` must **not** take the worker path: it stays in-process, under a named transport-gated disposition, and the call is still served exactly as it was before the worker path existed.
  - **Why this is now an acceptance criterion and not a scope note.** The Design section above listed `mcp-http` proxying as "out of bounds", which is a statement about intent — nothing failed if it happened. Since **story 5 was deferred on 2026-08-18** (ADR §5, OQ-9), it is a *correctness* requirement: the credential channel a child would need does not exist, so a cohort tool relayed over `mcp-http` would either fail outright or fall back to whatever identity the child could find on its own. A silently-crossed privilege boundary is strictly worse than a failed call, which is why the gate must be tested rather than intended.
  - **Gate on the declared transport, never on "the credential context happens to be null."** An absent credential context is an accident of one request; the transport is a decision. The day `mcp-http` serves one unauthenticated request, a null-context check opens the worker path and a transport check does not.
  - **Tier: unit, deliberately.** The premise of the deferral is that `mcp-http` does not currently work, so an E2E that drives a real cohort call over that transport may not be runnable at all — and a skipped E2E asserts nothing. The dispatch-seam unit test is the assertion that actually holds.
  - **Coverage already exists and this AC names it rather than commissioning it:** `McpExecutionRouterTests.Resolve_ShouldRefuseToRelay_WhenHostTransportIsNotStdio` asserts `InProcessTransportGated` with `ExecutesInProcess == true`, and the sibling `Resolve_ShouldRefuseToRelay_WhenThisProcessIsItselfAWorker` covers the recursion guard separately so the two reasons cannot be confused (`clio.tests/Command/McpServer/McpExecutionRouterTests.cs`, read 2026-08-18). The gate itself is `clio/Command/McpServer/IMcpWorkerPathGate.cs`, whose fail-closed zero value (`Unknown`) means an undeclared transport is also refused.
  - **Reviving `mcp-http` means lifting this gate deliberately, with the channel in place — never deleting the check because a test went red** (OQ-9).

## Tests
E2E TC-E-601…604 in `clio.mcp.e2e`. **Must be run on TeamCity** (`Team_Atf_ClioMcpE2eTests`) — GitHub CI does not run this suite — and the result stated in the PR.

Unit TC-U-601 for AC-07 (the stdio-only gate), which runs in the ordinary GitHub CI unit suite.
