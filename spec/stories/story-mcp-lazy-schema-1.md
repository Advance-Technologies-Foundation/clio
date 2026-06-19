# Story 1: IFeatureToggleService-gated core/long-tail MCP profile

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Decision 5 ("Gating uses IFeatureToggleService"), "Empirical basis" (replace env-var scaffold)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: review
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

- [x] **AC-01** — Given the feature flag is OFF (default), when the MCP server starts, then `tools/list` is the current FULL catalog — `SelectToolTypes(enabled, lazyToolsEnabled:false)` returns the enabled set unchanged (test `SelectToolTypes_ShouldReturnFullSet_WhenLazyToolsDisabled`; the SDK-parity test `RegisterEnabledPrimitives_ShouldRegisterSamePrimitivesAsSdkFromAssemblyScan_WhenNothingGated` continues to pass via the default-false overload).
- [x] **AC-02** — Given the feature flag is ON, only the core profile tool types + the 3 always-on executor/contract types register, gated through the single `McpFeatureToggleFilter.RegisterEnabledPrimitives` seam using the `IEnumerable<Type>` overload (no `Type[]`, no `*FromAssembly`). Tests `SelectToolTypes_ShouldReturnCorePlusExecutors_WhenLazyToolsEnabled`, `...ShouldSelectOnlyCoreAndAlwaysOnTypes...`.
- [x] **AC-03** — `BindingsModule` consults `IFeatureToggleService.IsFeatureEnabled(McpCoreToolProfile.FeatureKey)` with key `mcp-lazy-tools`; `IsFeatureEnabled`/`ISettingsRepository.IsFeatureEnabled` compare case-insensitively against the same `appsettings.json features` store.
- [x] **AC-04** — Membership is a single maintained constant set `McpCoreToolProfile.CoreToolTypes` + `AlwaysOnLazyToolTypes` (public, `typeof`-checked), unit-assertable directly and via `SelectToolTypes`.
- [x] **AC-05** — `ApplyToolProfile`/`CLIO_MCP_TOOL_TYPES` removed; test `SelectToolTypes_ShouldIgnoreSpikeEnvVar_WhenSet` proves the env var is no longer consulted.
- [x] **AC-ERR** — `SelectToolTypes` returns the full set whenever `lazyToolsEnabled` is false; `IsFeatureEnabled` returns false for an absent/false/malformed flag, so the server fails closed to the FULL catalog with no user-facing log.

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

- Implementation started: 2026-06-19
- Implementation completed: 2026-06-19
- Tests passing: `dotnet test clio.tests/clio.tests.csproj -c Release --filter "Category=Unit&(Module=McpServer|Module=Command)" -f net10.0` → 2972 passed, 26 skipped, 0 failed.
- Notes:
  - New: `clio/Command/McpServer/McpCoreToolProfile.cs` — `FeatureKey = "mcp-lazy-tools"`, `CoreToolTypes` (18 declaring `[McpServerToolType]` classes for the inventory's ~20 proposed core tools — several core tools share a class), `AlwaysOnLazyToolTypes` ({ClioRunTool, ClioRunDestructiveTool, ToolContractGetTool}). Marked provisional pending Story 7.
  - `McpFeatureToggleFilter`: removed the `ApplyToolProfile`/`CLIO_MCP_TOOL_TYPES` spike scaffold; added public pure `SelectToolTypes(enabledToolTypes, lazyToolsEnabled)` and a `bool lazyToolsEnabled = false` optional parameter on `RegisterEnabledPrimitives` (default false keeps the existing 4-arg parity-test call and full-catalog behaviour).
  - `BindingsModule.cs`: passes `mcpFeatureToggleService.IsFeatureEnabled(McpCoreToolProfile.FeatureKey)` into the seam.
  - `ExperimentalCommand.cs`: added `StandaloneFeatureKeys` so `mcp-lazy-tools` is a recognized key (listed by `clio experimental`, no "unknown key" warning on `--enable/--disable`). Reused existing `IFeatureToggleService.IsFeatureEnabled(string)` — no new string-key capability was needed.
  - Budget ratchet: lazy `tools/list` = **27 tools** (per-TYPE: core classes declare extra `[McpServerTool]` methods, e.g. DataForgeTool 8). Tests assert tool COUNT ≤ 30 and serialized `ProtocolTool` payload ≤ 48 KiB.
  - Tests: `clio.tests/Command/McpServer/McpProfileGatingTests.cs` (7) + `ExperimentalCommandTests` recognized-key test.
  - MCP/docs: reviewed — no public tool/option/help/docs change (profile gating is a registration-filter behaviour, not a command/option change). Integration + E2E test rows from the story's Test Requirements are deferred (E2E is not in CI; manual gate per project-context.md).
