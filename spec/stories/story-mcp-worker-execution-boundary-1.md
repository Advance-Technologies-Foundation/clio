# Story 1: Execution metadata + catalog coverage test

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 1
**Status**: ready-for-dev
**Size**: L

## As a
router that must decide where a tool call executes

## I want
six reflected execution-metadata fields on every `[McpServerTool]`, and a test that fails when one is missing

## So that
routing is decided by declared intent rather than inferred from safety hints that were never meant to carry it

## Design
- New attribute (or extension of the existing tool metadata) carrying `Location`, `Lifetime`, `OperationFamily`, `BudgetPolicy`, `RequiresClientRequests`, `SharedFileResource`.
- **Routing cannot reuse an existing property** (rule 7): `IMcpToolInvokerRegistry` exposes only `ReadOnly` / `Destructive` / retry-safety, and `McpCoreToolProfile` is residency, not execution. `get-page` is resident *and* must run in a worker; most long-running tools are non-resident.
- **Resolution unwraps `clio-run` / `clio-run-destructive` first** and keys on the inner command — the long-running tools are non-resident and agents reach them that way. Routing on the outer name sends every long-running call to one place.
- Populate from the execution-metadata inventory. **Confirm each row rather than paste it**: the inventory's `Location` column is a file-level heuristic that over-assigns `worker`; §4 of the inventory names the classes to re-check by hand.
- Coverage test fails when: an enabled canonical tool is unclassified; a starter and its status poller disagree on `OperationFamily` or `Lifetime`; a hint-unbounded tool has no explicit `BudgetPolicy`.

## Acceptance Criteria
- [ ] AC-01 — All six fields declared per tool; the type is reflected, not a side table that can drift.
- [ ] AC-02 — Coverage test fails on an unclassified enabled canonical tool (TC-U-101).
- [ ] AC-03 — A **synthetic** new tool with no metadata fails the coverage test, proving it is not vacuous (TC-U-102).
- [ ] AC-04 — Starter/status disagreement fails the test, for all five pairs in inventory §5.1 (TC-U-103).
- [ ] AC-05 — Metadata resolution unwraps `clio-run` and keys on the inner command (TC-U-104).
- [ ] AC-06 — Each of the 37 hint-unbounded tools carries an explicit `BudgetPolicy` (TC-U-105).
- [ ] AC-07 — Feature-disabled tools are excluded from the coverage requirement but stay in the catalog (TC-U-106).
- [ ] AC-08 — No behaviour change: metadata is declared and asserted, nothing reads it to route yet.

## Tests
`clio.tests/Command/McpServer/` — `[Category("Unit")]`, `Module=McpServer`. TC-U-101…106.

## Notes
This story is metadata only. It must not change dispatch — that is Stage 4 onward.
