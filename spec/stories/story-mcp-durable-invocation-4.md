# Story 4: Fix workspace AGENTS.md template (delegate to live channel) + strip BOM

**Feature**: mcp-durable-invocation
**FR coverage**: FR-7
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md) (D7)
**Status**: ready-for-dev
**Size**: S
**Depends on**: story-mcp-durable-invocation-3

## As a
developer starting work in a fresh `clio createw` workspace

## I want
the shipped AGENTS.md deploy guidance to point at the live MCP channel instead of hardcoded long-tail tool names

## So that
the guidance stays valid as the tool surface evolves, and a new agent is steered to the discoverable path

## Design
- Rewrite the "Deploying changes to Creatio" / FSM section of `clio/tpl/workspace/AGENTS.md`:
  - replace direct tool-name imperatives (`get-fsm-mode`, `compile-creatio`, `push-workspace`, `restart-by-environment-name`, `pkg-to-file-system`, `pkg-to-db`) with the discovery flow: read `get-guidance name=routing` + `name=core-rules`, use `get-tool-contract` for the exact tool, invoke long-tail via `clio-run`;
  - keep durable structural facts (packages/projects layout, `dotnet build MainSolution.slnx -c dev-n8`, data-access rules, diary);
  - add a line: the live clio MCP guidance is authoritative over this static section.
- Strip UTF-8 BOM from `clio/tpl/workspace/*` text files (verified harmless to Claude Code; corrected while touching).

## Acceptance Criteria
- [ ] AC-01 — Deploy section names no non-resident tool imperatively without the `clio-run`/`get-tool-contract` bridge.
- [ ] AC-02 — Story 3 drift test passes.
- [ ] AC-03 — `clio/tpl/workspace/*` have no BOM (`head -c3` ≠ EF BB BF).
- [ ] AC-04 — `createw` still produces a valid workspace (existing CreateWorkspace tests green).

## Tests
Story 3 drift test; existing `CreateWorkspaceCommand.Tests` remain green.
