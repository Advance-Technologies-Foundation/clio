# Story 7: Sticky supervision + parent-owned configuration-build reservation

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 7
**Status**: ready-for-dev
**Size**: L

## As a
caller polling a long-running operation

## I want
my status polls to reach the worker that is running the operation

## So that
`compile-status` answers from the process that holds the compile, not from an empty registry in another one

## Design
- **Private completion signal** between worker and parent (rule 5). "Reap on terminal status" cannot work: only two operation registries exist — `ICompileOperationRegistry` (`BindingsModule.cs:738`) and `IRestartOperationRegistry` (`:744`). `install-process-builder` and `create-app-section` have none, and `restart-by-credentials` is deliberately unreportable, so three of the four long-running modes have no terminal status to reap on.
- **Registry cardinality — two different keys, and conflating them fails either way** (cross-call state §3). The compile/restart *status* registries stay keyed like the sticky worker (`principal + normalised target + credential fingerprint`): they answer "whose operation is this". The `configuration-build` *exclusion* is keyed by `normalised target + resource` only: Creatio's configuration build is server-wide, so putting the principal in that key lets two principals on one environment compile concurrently and corrupt each other's package compilation state. Keying exclusion by target alone is correct but means one stuck build denies the whole environment — which is precisely why the **30-minute reclaim ceiling is the maximum lock-hold time**, not an incidental detail.
- Move the shared `configuration-build` reservation to the parent, keyed by **normalised tenant + resource**. Today it is `McpToolExecutionLock._configurationBuildInFlight` (`:215`), in-process, held by `CompileCreatioTool.cs:66` and `InstallProcessBuilderTool.cs:167`. Its 30-minute reclaim ceiling and monotonic ownership tokens carry over unchanged — they were designed for the "holder may never release" case.
- Prototype behaviour to preserve: `compile-creatio` returned in-progress at 8 s and three `compile-status` polls answered `running` from the same worker in 0.00–0.02 s.
- Sticky lifetime bounded by credential validity with an explicit maximum (T-8) — a threat that per-call workers do not have, and the reason stickiness stays confined to these four families.

## Acceptance Criteria
- [ ] AC-01 — `compile-creatio` returns in-progress; subsequent `compile-status` polls reach the **same** worker (TC-E-701).
- [ ] AC-02 — Private completion signal reaps workers for the three families with no registry (TC-U-701).
- [ ] AC-03 — Parent-owned reservation excludes compile ↔ install-process-builder **across processes and across principals**, keyed by normalised tenant + resource with the 30-minute ceiling as its maximum hold (TC-U-702).
- [ ] AC-04 — Sticky lifetime bounded by credential validity, explicit maximum (TC-U-703).
- [ ] AC-05 — **OQ-4 resolved**: whether `create-app-section` gains a real registry or only the private signal.

## Tests
E2E TC-E-701; unit TC-U-701…703. **Full unit suite required.**
