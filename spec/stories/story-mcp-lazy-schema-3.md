# Story 3: command→optionsType registry + generalized container resolution

**Feature**: mcp-lazy-schema
**FR coverage**: ADR "Dispatch & arg binding" (registry + generalize ResolveFromCallContainer)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Risk**: MEDIUM — replaces BaseTool's hardcoded switch with a general resolver; foundation for Story 4
**Blocked by**: story-mcp-lazy-schema-0

---

## As a

clio MCP executor author

## I want

a `command → optionsType` registry (reflected from `[Verb]` on options classes) and a generalized env-scoped resolver that is not limited to the four hardcoded option types

## So that

the generic `clio-run` executor (Story 4) has a single resolution layer instead of `BaseTool<T>`'s hardcoded per-type `options switch`

---

## Acceptance Criteria

- [ ] **AC-01** — Given options classes carrying `[Verb]`, when the registry builds, then it maps every verb name (canonical + aliases) to its options `Type`, using the same source the CLI parser reflects over.
- [ ] **AC-02** — Given a known command name, when `ResolveOptionsType(command)` is called, then it returns the correct options `Type`; given an unknown name, it returns a miss (no throw used for control flow at the boundary).
- [ ] **AC-03** — Given `BaseTool<T>`'s hardcoded `options switch` (`BaseTool.cs:99-128`, throws "Unsupported options type"), when generalized, then `ResolveFromCallContainer` resolves `Command<TOptions>` for any registered options type from the env-scoped container — not just the four current types.
- [ ] **AC-04** — Given the existing four flat tools that use `BaseTool<T>`, when this story lands, then their behavior is unchanged (the general resolver subsumes the switch without regression).
- [ ] **AC-05** — Given two verbs mapping to ambiguous/duplicate option types, when the registry builds, then the collision is detected at build/startup (not silently last-wins).
- [ ] **AC-ERR** — Given an unknown command, when resolution is attempted, then a structured "unknown command 'X'" result is produced (consumed by Story 4 / Story 5), not an unhandled exception.

## Implementation Notes

Key files:
- `clio/Command/McpServer/Tools/BaseTool.cs:99-128` — the hardcoded `options switch` to generalize; `ResolveFromCallContainer` is the env-scoped resolution to reuse.
- Verb reflection source — same as CLI parser (`clio/Program.cs` verb wiring).
- New service: `ICommandOptionsRegistry` (interface + impl, DI-registered per project-context.md DI policy) + generalized resolver.

Pattern: behavior class → interface → DI registration (project-context.md). No MediatR. Reuse `BaseTool`'s env-scoped container resolution; do not re-implement env scoping.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | registry maps verb→optionsType incl. aliases; unknown→miss; collision detection; resolver returns Command for arbitrary registered type | `clio.tests/Command/McpServer/CommandOptionsRegistryTests.cs` |
| Integration `[Category("Integration")]` | env-scoped resolve of a real `Command<TOptions>` from the container | `clio.tests/Command/McpServer/GeneralizedResolverTests.cs` |
| E2E | n/a (covered via Story 4) | — |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings; new service has interface + DI registration (CLIO001/CLIO005 clean)
- [ ] Existing four `BaseTool<T>` tools regression-tested unchanged
- [ ] Collision detection covered by a unit test
- [ ] Validated filter recorded (`Category=Unit&Module=McpServer`)
- [ ] PR references this story file

## Dev Agent Record

- Implementation started: 2026-06-19
- Implementation completed: 2026-06-19
- Tests passing: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" -f net10.0` → 1256 passed, 1 skipped (pre-existing), 0 failed. New: `CommandOptionsRegistryTests` (11), `EnvironmentScopedCommandExecutorTests` (4).
- Notes:
  - New `ICommandOptionsRegistry` / `CommandOptionsRegistry` (`clio/Command/McpServer/Tools/CommandOptionsRegistry.cs`): reflects `[Verb]` over `Program.GetCommandOptionTypes()` (same source the CLI parser uses); maps canonical name + aliases → options `Type`; case-insensitive; `TryResolveOptionsType` returns a miss (no throw) for unknown/blank. A test-only `IEnumerable<Type>` ctor overload injects synthetic colliding verbs.
  - **AC-05 collision detection adjusted to fit production reality:** the real verb set contains a genuine duplicate ALIAS `comp-pkg` (on both `generate-pkg-zip` and `compile-package`). A blanket throw on any duplicate would break startup. Resolution: **canonical-name** collisions throw at construction (true parser-breaking ambiguity); **alias** collisions are detected and the ambiguous alias is removed so it resolves to a MISS (never a silent wrong dispatch) — canonical names always win over aliases. This satisfies AC-05's intent (detected, not silent last-wins) without breaking the existing CLI. Flagged as a follow-up below.
  - New `IEnvironmentScopedCommandExecutor` / `EnvironmentScopedCommandExecutor` generalizes BaseTool's env-scoped resolution + locked-execute + log-flush + notify for a runtime-only-known options type (reflection over the generic `IToolCommandResolver.Resolve`/`ResolveWithoutEnvironment` + `Command<T>.Execute`; unwraps `TargetInvocationException`). The env-less special-case decision lives once in `UsesEnvironmentlessResolution` and is shared with `BaseTool` (AC-03/AC-04 — zero behavioral drift; the 4 flat tools now route through the same decision and all their existing tests pass).
  - DI: interfaces registered in `BindingsModule.cs`; CLIO* clean (build runs analyzers as errors, 0 errors).
  - Integration test (`GeneralizedResolverTests`, env-scoped real container) NOT added — the resolution layer is covered at unit level via a substituted `IToolCommandResolver`; a real-container integration test is deferred (would require a live BindingsModule build).

### Dev Agent Record — 2026-06-19 rework ([Verb]→MCP-tool dispatch pivot)

- **`ICommandOptionsRegistry`/`CommandOptionsRegistry` REMOVED.** The empirical bug: lazy mode hides 99 long-tail tools and exposes them only through `clio-run`, but `clio-run` resolved a CLI `[Verb]` and executed `Command<TOptions>`. **42 hidden tools are MCP-ONLY** (no `[Verb]` — `sync-schemas`, `odata-*`, `execute-esq`, `create-user-task`, …), so `clio-run {command:"sync-schemas"}` returned `unknown command 'sync-schemas'. It is not a registered clio verb or alias.` The `[Verb]`-over-registry abstraction was wrong; dispatch must be over the **MCP tool catalog**, so the registry was deleted (would otherwise be a CLIO005 dead registration). `CommandOptionsRegistryTests` removed.
- **`EnvironmentScopedCommandExecutor` reduced to its static `UsesEnvironmentlessResolution` helper** (still consumed by `BaseTool`). The instance behavior (`IEnvironmentScopedCommandExecutor` interface, `ResolveAndExecute`, reflection-over-`Command<T>`) was only consumed by the old `clio-run` executor and is gone with it; `EnvironmentScopedCommandExecutorTests` removed. The four flat `BaseTool<T>` tools are unaffected (still resolve via `IToolCommandResolver` + the shared static helper) — their existing tests pass.
- Replacement is `IMcpToolInvokerRegistry`/`McpToolInvokerRegistry` (Story 4 record). Net-net: the env-less decision (AC-03/04) is preserved for the flat tools; the verb registry (AC-01/02/05) is obsolete for the MCP executor and removed.
