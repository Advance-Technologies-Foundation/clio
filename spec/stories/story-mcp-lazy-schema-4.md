# Story 4: clio-run executor — kebab/enum/required-aware arg binding + output contract

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Decision 3 (`clio-run` generic executor), "Dispatch & arg binding" (arg binding + Output contract)
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: L (full day)
**Risk**: HIGH — the new executor built from scratch; arg binding is the ADR's "hard part"
**Blocked by**: story-mcp-lazy-schema-3 (registry + resolver), story-mcp-lazy-schema-0

---

## As a

clio MCP client (model)

## I want

a single `clio-run(command, args)` MCP tool where `args` is a free-form JSON object, bound to the command's CommandLineParser option model with kebab/enum/required awareness, executed in the env-scoped container

## So that

the long tail of commands is callable without their full schemas sitting in `tools/list`, and outputs are a uniform envelope

---

## Acceptance Criteria

- [ ] **AC-01** — Given `clio-run(command, args)` with `args` a free-form JSON object (NOT a discriminated `anyOf`), when invoked with valid kebab-keyed args, then the command executes and returns the same `CommandExecutionResult` envelope (execution-log messages) that flat tools return.
- [ ] **AC-02** — Given args keyed by `[Option]` long names (kebab-case), when bound, then binding uses CommandLineParser semantics as the source of truth — e.g. flatten to `--kebab value` argv and `Parser.ParseArguments` — NOT raw `JsonSerializer.Deserialize<TOptions>`.
- [ ] **AC-03** — Given a `Required=true` option is missing, when bound, then binding fails with the verbatim parser error (not silently defaulted).
- [ ] **AC-04** — Given an enum-typed option, when a string value is passed, then it parses via CommandLineParser enum handling (case rules per parser), and an invalid enum value is a parse error.
- [ ] **AC-05** — Given an unknown arg key, when bound, then it is an error (NOT silently dropped).
- [ ] **AC-06** — Given an unknown `command`, when invoked, then a structured "unknown command" result is returned (from Story 3's resolver), not an exception.
- [ ] **AC-07** — Given any of the 74 long-tail commands, when run via `clio-run`, then the output envelope shape is identical across commands (1 executor unifies outputs).
- [ ] **AC-ERR** — Given a binding/parse failure, when it occurs, then `clio-run` returns a user-friendly `Error: ...` payload and a non-success result (inline-contract behavior is Story 5; this story returns the parse error verbatim).

## Implementation Notes

Key files:
- New `clio/Command/McpServer/Tools/ClioRunTool.cs` (`[McpServerToolType]`, gated by the Story 1 feature-key) + an `IClioRunArgBinder` service.
- Reuse Story 3 `ICommandOptionsRegistry` + generalized `ResolveFromCallContainer`.
- CommandLineParser `Parser.ParseArguments` — re-pack JSON object → argv. Beware bool flags (presence), repeated values, `[Value]` positionals.
- `CommandExecutionResult` envelope — match the flat-tool shape exactly.

Pattern: env-aware execution path of `BaseTool` (do not execute startup-injected command directly — env-scoped per AGENTS.md MCP rules). DI: binder is a behavior class → interface → registration.

Security note: `clio-run` is NOT `ReadOnly`/auto-approve (per Story 0 decision); destructive split is Story 8.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | kebab key binding; missing Required → parser error; enum parse + invalid enum; unknown key → error; bool flag; positional `[Value]`; unknown command → structured miss; envelope shape | `clio.tests/Command/McpServer/ClioRunArgBinderTests.cs`, `ClioRunToolTests.cs` |
| Integration `[Category("Integration")]` | end-to-end bind→resolve→execute a representative non-destructive command via env-scoped container | `clio.tests/Command/McpServer/ClioRunExecutionTests.cs` |
| E2E `[Category("E2E")]` | model composes a `clio-run` call and reads the envelope on claude/codex/copilot (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]`.

## Definition of Done

- [ ] No CLIO* warnings; binder + tool DI-registered with interfaces
- [ ] Binding uses CommandLineParser, NOT raw STJ (asserted by test)
- [ ] `clio-run` gated by the Story 1 feature-key (CLI + MCP attribute surfaces)
- [ ] Envelope shape matches flat tools (golden assertion)
- [ ] MCP e2e added in `clio.mcp.e2e` (mandatory per AGENTS.md) — flagged NOT in CI
- [ ] Validated filter recorded
- [ ] PR references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
