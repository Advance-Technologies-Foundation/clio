# Story 1: Compatibility/alias catalog + fail-fast collisions

**Feature**: mcp-durable-invocation
**FR coverage**: FR-4
**PRD**: [prd-mcp-durable-invocation.md](../prd/prd-mcp-durable-invocation.md)
**ADR**: [adr-mcp-durable-invocation.md](../adr/adr-mcp-durable-invocation.md) (D4)
**Status**: ready-for-dev
**Size**: M

## As a
maintainer of clio's MCP surface

## I want
a single source of truth mapping legacy/renamed/deprecated MCP tool names to their canonical name

## So that
the forgiving handler, `clio-run`, `get-tool-contract`, and the drift test all agree, and name collisions fail fast instead of silently resolving to the first registration

## Design
- New `clio/Command/McpServer/McpToolCompatibilityCatalog.cs`: `IMcpToolCompatibilityCatalog` + impl, DI-registered in `BindingsModule` (interface-based, CLIO001).
- Entry record: `McpToolCompatibilityEntry(CanonicalName, Aliases, CompatibilityKind, DeprecatedSince?, Replacement?, SurfaceOwner, ArgumentAdapter?)`.
- Seed entries from known cases (e.g. `restart-by-environmentName` → `restart-by-environment-name`); the duplicate `[McpServerTool]` alias method on `RestartTool` is superseded by a catalog entry (remove the duplicate method in a later story only after the catalog path is proven).
- `McpToolInvokerRegistry.cs:112` silent-first-duplicate becomes a fail-fast collision error at build time of the registry.
- Resolution API: `TryResolve(name, out canonical, out entry)` — case-insensitive, emits canonical; feature-disabled canonical stays unresolved.

## Acceptance Criteria
- [ ] AC-01 — Alias resolves to canonical (case-insensitive) via `TryResolve`.
- [ ] AC-02 — Duplicate canonical or alias in the catalog throws at startup (and a unit test asserts it).
- [ ] AC-03 — Registry construction throws on duplicate tool name instead of skipping.
- [ ] AC-04 — Feature-disabled canonical target does not resolve.
- [ ] AC-05 — Catalog is injectable and consumed by `clio-run` resolution (parity with registry).

## Tests
`clio.tests/Command/McpServer/McpToolCompatibilityCatalogTests.cs` — resolve, collision-fail, feature-gating; `[Category("Unit")]`, `Module=McpServer`.
