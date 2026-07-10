# Story 3: Drift-guard test (resident-or-bridged oracle) + maintenance ownership

**Feature**: mcp-durable-invocation
**FR coverage**: FR-6
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md) (D6)
**Status**: ready-for-dev
**Size**: M
**Depends on**: story-mcp-durable-invocation-1
**Revised**: 2026-07-10 after Codex adversarial review (B4, M11)

## As a
clio maintainer

## I want
a test that fails when shipped guidance directly invokes a tool that is neither resident nor routed through the `clio-run`/`get-tool-contract` bridge

## So that
the PR #743 regression class can never ship uncaught — and, unlike a naive existence check, the test actually detects it

## Design
- New `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs` (`[Category("Unit")]`, `Module=McpServer`).
- Scan sources: `clio/tpl/workspace/AGENTS.md`, `clio/tpl/ui-project*/AGENTS.md`, `McpServerInstructions.Text`, enabled `GuidanceCatalog` article bodies.
- **Oracle (B4 — existence is NOT the test):** because the registry contains the full long-tail, "name resolves in the registry" proves existence, not valid direct invocation. Classify each reference as **direct-imperative-invocation** vs **mention**, then assert:
  - a direct imperative MCP-tool reference MUST name a **resident** tool OR explicitly use the `clio-run` / `get-tool-contract` bridge;
  - a direct imperative CLI reference MUST be a current `[Verb]` name/alias;
  - a guide reference MUST resolve in `GuidanceCatalog`;
  - external tokens (`dotnet`, `npm`, skill names) ⇒ explicit allowlist.
- **Tokenization specified (M11 — not ad-hoc):** prefer an explicit machine-readable reference marker (or sidecar manifest) over prose scraping. Where prose is scanned, define exact rules for code-fences, backticked paths/shell/examples, negated instructions ("do NOT call `x`"), and a **deterministic feature baseline** for feature-gated guides. Comprehensive fixtures.
- Add `clio/tpl/**` to root `AGENTS.md` documentation + MCP maintenance-target lists and trigger-conditions.

## Acceptance Criteria
- [ ] AC-01 — Test **fails** on the current `tpl/workspace/AGENTS.md` (imperative `get-fsm-mode` etc. are non-resident and un-bridged) — proven before Story 4. (This is the fix for the naive oracle that would have passed.)
- [ ] AC-02 — Test passes after Story 4 template fix.
- [ ] AC-03 — Fixture: a `` `not-a-tool` `` imperative fails; a hardcoded non-resident imperative without the bridge fails; a mention (non-imperative) does not.
- [ ] AC-04 — Feature-gated guide references resolve deterministically under the fixed baseline (no CI nondeterminism).
- [ ] AC-05 — Root `AGENTS.md` lists `clio/tpl/**` as a maintenance target.

## Tests
The test file + fixtures are the deliverable; verified by the fail→pass transition across Story 4 and the negative/positive fixtures.
