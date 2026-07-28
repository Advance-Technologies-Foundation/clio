# Story 5: MCP e2e coverage + docs/MCP-surface review

**Feature**: mcp-durable-invocation
**FR coverage**: NFR-3
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md)
**Status**: ready-for-dev
**Size**: M
**Depends on**: story-mcp-durable-invocation-2

## As a
release owner

## I want
end-to-end proof that the forgiving path behaves correctly over the real MCP protocol, and the docs/MCP surface kept in sync

## So that
the change is releasable per repo policy (unit tests are necessary but insufficient for MCP changes)

## Design
- New `clio.mcp.e2e/DurableInvocationToolE2ETests.cs` exercising the live stdio server:
  - direct long-tail non-destructive name → executes + advisory note;
  - direct destructive name → `confirmation-required`, no side effect;
  - deprecated alias → resolves;
  - unknown/foreign name → structured did-you-mean + hint;
  - `tools/list` count unchanged.
- Docs/MCP review per policy: update `docs/McpCapabilityMap.md` (describe forgiving invocation + compatibility catalog); confirm no per-command help/docs need changes (mechanism, not a verb); state "MCP reviewed" outcome in the change summary.
- Use skills: `$test-mcp-tool` (e2e), `$document-command` (docs) as applicable.

## Acceptance Criteria
- [ ] AC-01 — e2e cases above pass against the real server.
- [ ] AC-02 — `docs/McpCapabilityMap.md` documents the forgiving-invocation behavior.
- [ ] AC-03 — Change summary states MCP surface review outcome.
- [ ] AC-04 — Optional: TeamCity `Team_Atf_ClioMcpE2eTests` on the branch shows no new failures vs trunk baseline.

## Tests
`clio.mcp.e2e/DurableInvocationToolE2ETests.cs`.
