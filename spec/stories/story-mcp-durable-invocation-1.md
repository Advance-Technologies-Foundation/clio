# Story 1: Compatibility/alias catalog + fail-fast collisions (eager)

**Feature**: mcp-durable-invocation
**FR coverage**: FR-4
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md) (D2, D4)
**Status**: ready-for-dev
**Size**: M
**Revised**: 2026-07-10 after Codex adversarial review (H7, H8, H9, L13)

## As a
maintainer of clio's MCP surface

## I want
a single, eagerly-validated source of truth mapping legacy/renamed/deprecated MCP tool names to their canonical name, returning a discriminated resolution result

## So that
the forgiving handler, `clio-run`, `get-tool-contract`, and the drift test all agree, collisions fail at startup, and feature-disabled is distinguishable from unknown

## Design
- New `clio/Command/McpServer/McpToolCompatibilityCatalog.cs`: `IMcpToolCompatibilityCatalog` + impl, DI-registered (interface-based, CLIO001).
- Entry record: `McpToolCompatibilityEntry(CanonicalName, Aliases, CompatibilityKind, DeprecatedSince?, Replacement?, SurfaceOwner, ArgumentAdapter?)`.
- **Discriminated resolution result** (H8): `Resolved(canonical, tool) | Disabled(canonical) | Unknown | Foreign | Ambiguous(candidates)`. Feature-disabled canonical ⇒ `Disabled`, never `Unknown`.
- **Alias precedence** (H7): a catalog-declared alias wins over a raw registry hit for the same string, so deprecation metadata/adapters are never bypassed.
- **Restart seed** (H7): declare `restart-by-environmentName` → `restart-by-environment-name` in the catalog. The duplicate `[McpServerTool]` method is **NOT removed here** — removal happens **atomically in Story 2**, together with wiring the handler that consumes the catalog, so there is no regression window where the alias is un-served (nothing reads the catalog until Story 2).
- **Collisions fail-fast + eager** (H9, L13): duplicate canonical or alias ⇒ throw at construction; make the catalog (and the registry duplicate check) run at **startup** (eagerly-constructed immutable singleton or startup validator), not on first resolution. There are **no duplicate keys today**, so use a **synthetic** duplicate fixture to test the throw, plus a production-uniqueness test over all 150 `[McpServerTool]` names.
- `McpToolInvokerRegistry.cs:108/112` silent-first-duplicate becomes a fail-fast collision error.

## Acceptance Criteria
- [ ] AC-01 — Alias resolves to canonical (case-insensitive); catalog alias wins over raw registry hit.
- [ ] AC-02 — Synthetic duplicate canonical/alias throws at **startup** (host construction), asserted by test.
- [ ] AC-03 — Production catalog + all registered tool names are collision-free (uniqueness test).
- [ ] AC-04 — Catalog declares `restart-by-environmentName` → `restart-by-environment-name` and validates that the canonical exists in the registry tool-name set.
- [ ] AC-05 — Catalog is injectable (singleton) and constructs (validates) at startup. Full resolution composition (`Resolved|Disabled|Unknown|Foreign|Ambiguous`, alias precedence over registry, fuzzy) is Story 2's handler concern; Story 1 provides the alias map + validation it builds on.

## Tests
`clio.tests/Command/McpServer/McpToolCompatibilityCatalogTests.cs` — resolve, alias-precedence, discriminated results, synthetic collision at startup, production uniqueness, feature-gating; `[Category("Unit")]`, `Module=McpServer`.
