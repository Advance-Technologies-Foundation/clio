# Story 1: IFeatureToggleService-gated core/long-tail MCP profile

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Decision 5 ("Gating uses IFeatureToggleService"), "Empirical basis" (replace env-var scaffold)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Risk**: MEDIUM — touches the single MCP registration seam; opt-in by default keeps it safe
**Blocked by**: story-mcp-lazy-schema-0 (feature-key string + default-profile decision)

---

## As a

clio MCP server author

## I want

the spike's throwaway `CLIO_MCP_TOOL_TYPES` env-var gate replaced by a config-driven `IFeatureToggleService` profile that decides which tool types register at startup

## So that

core-vs-long-tail membership is production-grade (no env var, fail-closed, opt-in) and every later story has a real gate to register behind

---

## Acceptance Criteria

- [ ] **AC-01** — Given the feature flag is OFF (default), when the MCP server starts, then `tools/list` is byte-for-byte the current FULL catalog (~124 tools) — no behavior change for existing consumers.
- [ ] **AC-02** — Given the feature flag is ON, when the MCP server starts, then only the core profile tool types register and `tools/list` is the reduced set, gated through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (the existing seam, `IEnumerable<Type>` — never `Type[]`, never `*FromAssembly`).
- [ ] **AC-03** — Given the flag string, when `IFeatureToggleService.IsEnabled` is consulted, then the exact feature-key from Story 0 is used and comparison is case-insensitive.
- [ ] **AC-04** — Given core-vs-long-tail membership, when defined, then it is config-driven (not hardcoded scattered constants) and unit-assertable.
- [ ] **AC-05** — Given the env var `CLIO_MCP_TOOL_TYPES`, when grepped after this story, then no production code path reads it (scaffold removed).
- [ ] **AC-ERR** — Given a malformed/absent flag, when the server starts, then it fails closed to the FULL catalog (never partially registers an undefined profile) and logs nothing user-facing.

## Implementation Notes

Key files:
- `clio/BindingsModule.cs:632-655` — `RegisterEnabledPrimitives` seam; gate which `[McpServerToolType]` types are passed.
- `Clio.Command.IFeatureToggleService.IsEnabled(Type)` — the single predicate; do NOT re-implement.
- Spike `CLIO_MCP_TOOL_TYPES` reader (in `RegisterEnabledPrimitives` per ADR "Empirical basis") — remove.
- Feature flags live in `appsettings.json` `features` object; manage via `clio experimental`.

Pattern to follow: existing `[FeatureToggle("key")]` gating (project-context.md "Feature toggles", four enforcement surfaces). Note MCP needs its own attribute on `[McpServerToolType]` classes — but here gating is profile-membership, not per-command hiding, so apply at the registration filter, not per-tool attribute.

CAUTION (do not regress): never pass `Type[]` to `WithTools`/`WithResources`/`WithPrompts` — binds to generic overload and registers nothing.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | profile selection given flag on/off; core membership set; fail-closed on malformed flag | `clio.tests/Command/McpServer/McpProfileGatingTests.cs` |
| Integration `[Category("Integration")]` | `RegisterEnabledPrimitives` registers expected type count per profile | `clio.tests/Command/McpServer/McpProfileRegistrationTests.cs` |
| E2E `[Category("E2E")]` | `tools/list` size flag-off vs flag-on over stdio (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` on every assert + `[Description]` on every test.

## Definition of Done

- [ ] Code compiles without CLIO001-CLIO005 warnings
- [ ] No new CLI flags (config-only); any new options kebab-case
- [ ] Unit tests `[Category("Unit")]` (never `UnitTests`)
- [ ] `RegisterEnabledPrimitives` still uses `IEnumerable<Type>` (no regression)
- [ ] Flag OFF reproduces current FULL catalog (golden assertion)
- [ ] PR description references this story file
- [ ] Validated filter recorded (e.g. `Category=Unit&Module=McpServer`)

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
