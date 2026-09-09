# Story 0: Design artifacts: ADR, inventories, stories, test plan

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 0
**Status**: review
**Size**: M

## As a
maintainer about to change how every MCP call executes

## I want
the decision, the three inventories, the stories and the test plan written down before any production code

## So that
Stage 1 has a worklist that is complete and re-measured, and no implementation starts from a four-day-old census

## Design
- ADR records the decision, the rejected alternatives, the twelve binding implementation rules (eleven from the issue plus the transport-ownership rule from the relay spike), and how it composes with `adr-mcp-durable-invocation.md`.
- Three inventories: execution metadata per tool, state that survives between MCP calls, threat model for the parent→child credential channel.
- The census is **re-measured on master**, not copied from the issue: the parser is stated so the number is reproducible.
- No PRD: the Jira issue is the PRD. Recorded explicitly in the ADR rather than skipped silently.

## Acceptance Criteria
- [x] AC-01 — ADR exists, status Accepted, with the rejected alternatives and their reasons.
- [x] AC-02 — All twelve implementation rules recorded, each with its citation in current code.
- [x] AC-03 — Execution-metadata inventory covers **every** tool in the catalog, with the derivation method stated and its known-weak rows named.
- [x] AC-04 — Cross-call state inventory classifies each item as disappears / moves to parent / interprocess hazard, and states the ordering constraint between deletions and gates.
- [x] AC-05 — Credential threat model covers channel exposure **and** credential downgrade (fail-open), with a fail-first identity assertion as the discriminator.
- [x] AC-06 — Test plan states the assert-on-request-counters rule and maps cases to stages.
- [x] AC-07 — Census re-measured against master; drift from the issue's numbers reconciled, not ignored.
- [x] AC-08 — No production code in this story.

## Tests
None — design artifacts. Verified by review.
