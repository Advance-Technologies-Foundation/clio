# Story 2: Runtime-bound settings and canonical lifecycle identity

**Feature**: mcp-http-passthrough-runtime-routing
**Jira**: [ENG-93348](https://creatio.atlassian.net/browse/ENG-93348)
**FR coverage**: FR-03, FR-05, FR-06, FR-07 · AC-01, AC-02, AC-04, AC-06, AC-07, AC-08, AC-09
**PRD**: [prd-mcp-http-passthrough-runtime-routing.md](../prd/prd-mcp-http-passthrough-runtime-routing.md)
**ADR**: [adr-mcp-http-passthrough-runtime-routing.md](../adr/adr-mcp-http-passthrough-runtime-routing.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1

---

## As a

gateway operator targeting both .NET Core and .NET Framework Creatio tenants

## I want

the validated runtime to configure every ephemeral client and participate in the canonical tenant identity

## So that

routes, cached containers, tenant locks, and in-flight accounting can never cross runtime families.

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-01, AC-02, AC-04)** — `BuildEphemeralSettings` assigns `EnvironmentSettings.IsNetCore` from `CredentialContext.IsNetCore` before any environment-bound provider, client, route, or command is built; no CLR default, fixed default, or probe is used.
- [ ] **AC-02 (PRD AC-01, AC-02)** — existing `ServiceUrlBuilder` behavior is proven for ephemeral settings: Core routes omit `/0/`, Framework routes contain exactly one `/0/`.
- [ ] **AC-03 (PRD AC-06)** — `BuildPassthroughCacheKey` includes runtime in its existing secret-hashed material. The same URL/auth with different runtime values produces different opaque keys, while equivalent requests keep `GetTenantKey`, acquired cache key, `LastResolvedTenantKey`, tenant lock, and in-flight key aligned.
- [ ] **AC-04 (PRD AC-08, AC-09)** — keys, logs, exceptions, snapshots, and persistence contain neither credentials nor raw header data; neither runtime selection nor ephemeral settings are written to disk.
- [ ] **AC-05 (PRD AC-10)** — bearer and login/password mapping is otherwise byte-for-byte equivalent; stdio, default HTTP, and registered/named environment settings retain their existing `IsNetCore` source and behavior.
- [ ] **AC-06** — no cache, lock, or client API is redesigned; `SessionContainerCache`, `TenantExecutionLockProvider`, `McpToolExecutionLock`, and `ServiceUrlBuilder` continue consuming the existing settings/key contracts.

## Implementation Notes

Use the `create-mcp-tool` skill because the change is in `clio/Command/McpServer/Tools/ToolCommandResolver.cs`, and use `test-mcp-tool` for its contract coverage.

- Set `IsNetCore = context.IsNetCore` in `BuildEphemeralSettings`.
- Add a stable runtime discriminator to `BuildPassthroughCacheKey` before hashing. Never put the raw credential or a human-readable secret into a key.
- Reuse the one canonical key already consumed by cache, locks, and in-flight guards; do not introduce parallel key builders.
- Review tools, prompts, resources, guidance, and destructive metadata. No external MCP argument or tool-contract change is expected.

## Test Requirements

- `ToolCommandResolverTests`: both runtime values, both supported auth paths, and unchanged registered-environment behavior.
- `ToolCommandResolverCacheKeyTests`: runtime discrimination, deterministic equivalence, and secret-free key material.
- `TenantKeyEquivalenceTests`: acquire/resolved/lock identity equality for each runtime and inequality across runtime values.
- `ToolCommandResolverNoWriteTests` and `CredentialPassthroughSecretHygieneTests`: no persistence and no leakage.
- Focused route/client identity coverage proving no `/0/` for Core and exactly one `/0/` for Framework.

Tests must use `[Description]`, explicit Arrange/Act/Assert, and a `because` explanation on every assertion.

## Definition of Done

- [ ] Runtime is applied before client construction and included in canonical identity.
- [ ] `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"` passes.
- [ ] No new `CLIO*` diagnostics exist in modified files; `git diff --check` passes.
- [ ] MCP reviewed and either aligned or recorded as `MCP reviewed, no update required`.
- [ ] Story 1 remains green; no runtime detector or tool argument is introduced.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
