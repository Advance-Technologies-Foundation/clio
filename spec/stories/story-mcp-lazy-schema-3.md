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

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
