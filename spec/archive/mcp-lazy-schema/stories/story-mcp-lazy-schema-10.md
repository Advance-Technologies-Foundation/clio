# Story 10: Deprecation aliases (flat name → clio-run proxy) + default-profile flip

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Risks ("Breaking consumers" — deprecation aliases), Open Decision 2 (default profile)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: L (full day)
**Risk**: HIGH — breaking MCP wire change for long-tail; aliases must prevent consumer breakage
**Blocked by**: story-mcp-lazy-schema-9 (alias list), story-mcp-lazy-schema-4, story-mcp-lazy-schema-6, story-mcp-lazy-schema-8

---

## As a

an existing MCP consumer (CAADT / adaclio / e2e) that hardcodes flat long-tail tool names

## I want

each removed flat long-tail tool name to still resolve, transparently proxying to `clio-run`, before any default profile flip

## So that

the migration does not break hardcoded integrations, and the core-by-default profile (if chosen in Story 0) can be enabled safely

---

## Acceptance Criteria

- [ ] **AC-01** — Given the Story 9 alias list, when implemented, then each flat long-tail tool name still accepted by the server maps internally to the correct `clio-run`/`clio-run-destructive` invocation with the same args/output envelope.
- [ ] **AC-02** — Given a deprecation alias is invoked, when executed, then behavior matches the pre-migration flat tool (golden-compared on a representative sample), and a deprecation notice is surfaced (log/response field, not a hard error).
- [ ] **AC-03** — Given Story 0 chose "opt-in until aliases+inventory land", when this story closes, then the default-profile flip is performed ONLY if its precondition (aliases + inventory complete) is met; otherwise the flip stays gated and that is recorded.
- [ ] **AC-04** — Given a destructive command's flat alias, when invoked, then it proxies to the destructive surface (Story 8), preserving the never-auto-approve guarantee.
- [ ] **AC-05** — Given the alias layer, when a flat name is NOT in the alias list (genuinely removed), then calling it returns `Error: tool 'X' has moved to clio-run` + pointer, non-success.
- [ ] **AC-ERR** — Given an alias maps to an unknown/renamed command, when the registry can't resolve it, then it fails closed with a clear error (no silent no-op).

## Implementation Notes

Key files:
- Alias resolution layer at the MCP dispatch boundary — reuse `ICommandOptionsRegistry` (Story 3) + the clio-run executor (Story 4) + destructive split (Story 8).
- Story 9 `mcp-lazy-schema-migration-inventory.md` — authoritative alias list.
- Profile config (Story 1) — the flip target.

Pattern: aliases are a thin proxy, not duplicated logic. Honor the Story 0 default-profile decision exactly. Breaking change policy (project-context.md): renamed surfaces get an alias.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | each alias resolves to correct clio-run target + args; destructive alias → destructive surface; removed-not-aliased → moved error; unknown target → fail closed | `clio.tests/Command/McpServer/ClioRunAliasTests.cs` |
| Integration `[Category("Integration")]` | alias invocation output == pre-migration flat output (sampled golden) | `clio.tests/Command/McpServer/AliasParityTests.cs` |
| E2E `[Category("E2E")]` | CAADT/adaclio/e2e representative calls still work via alias on 3 hosts (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings
- [ ] Every alias from Story 9 inventory implemented + parity-tested
- [ ] Default flip honors Story 0 decision (done only if preconditions met; else gated + recorded)
- [ ] Destructive aliases preserve never-auto-approve
- [ ] Consumer e2e (mandatory) — flagged NOT in CI
- [ ] PR references this story file + Story 9 inventory

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
