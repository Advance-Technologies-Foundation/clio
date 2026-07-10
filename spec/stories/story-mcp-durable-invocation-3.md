# Story 3: Drift-guard test + maintenance-target ownership for tpl

**Feature**: mcp-durable-invocation
**FR coverage**: FR-6
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md) (D6)
**Status**: ready-for-dev
**Size**: M
**Depends on**: story-mcp-durable-invocation-1

## As a
clio maintainer

## I want
a test that fails when shipped guidance names a tool/verb/guide that does not resolve

## So that
template/guidance drift like the PR #743 regression can never ship uncaught again

## Design
- New `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` (`[Category("Unit")]`, `Module=McpServer`).
- Scan sources: `clio/tpl/workspace/AGENTS.md`, `clio/tpl/ui-project*/AGENTS.md`, `McpServerInstructions.Text`, enabled `GuidanceCatalog` article bodies.
- Extract imperatively-referenced tokens (backticked names in "Call/run" contexts) and classify:
  - MCP name ⇒ must resolve via resident set ∪ `IMcpToolInvokerRegistry` ∪ `IMcpToolCompatibilityCatalog`;
  - CLI name ⇒ current `[Verb]` name or alias;
  - guide name ⇒ `GuidanceCatalog`;
  - external token (`dotnet`, `npm`, skill names) ⇒ explicit allowlist constant.
- Assert no ambiguous alias; unresolved token fails with the offending file + token.
- Add `clio/tpl/**` to root `AGENTS.md` documentation + MCP maintenance-target lists and trigger-conditions.

## Acceptance Criteria
- [ ] AC-01 — Test fails on the current (unfixed) `tpl/workspace/AGENTS.md` (naming `get-fsm-mode` etc. without resolution) — proven before Story 4 fix.
- [ ] AC-02 — Test passes after Story 4 template fix.
- [ ] AC-03 — Adding a bogus `` `not-a-tool` `` imperative reference to a scanned file makes the test fail (self-check).
- [ ] AC-04 — Root `AGENTS.md` lists `clio/tpl/**` as a maintenance target.

## Tests
The test file itself is the deliverable; verified by the fail→pass transition across Story 4.
