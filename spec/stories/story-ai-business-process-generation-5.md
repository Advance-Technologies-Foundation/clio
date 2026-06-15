# Story 5: validate-process-graph MCP Tool + Prompt + E2E

**Feature**: ai-business-process-generation
**FR coverage**: FR-08, FR-16, FR-17, FR-18 (capability-map entry for this tool)
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) ("MCP surface — Tool 1")
**Status**: ready-for-dev
**Size**: M (half day)

---

## As a

AI agent (MCP client)

## I want

a `validate-process-graph` MCP tool that accepts my planned node/edge graph (catalog `data-id` node types + flow kinds) and returns the validator's structured findings

## So that

I catch invalid connections (R1–R17) cheaply over MCP, before I drive the designer — without an environment or any I/O

---

## Acceptance Criteria

- [ ] **AC-01** — Given a valid `Start → Read data → End` graph passed as MCP args (`nodes: [{id, type}]`, `edges: [{source, target, flow-kind}]`), when `validate-process-graph` is called, then the result has **zero `error` findings** and status is success. (PRD AC-02)
- [ ] **AC-02** — Given a graph whose start has an incoming flow, when the tool runs, then the response contains an `error` finding with `ruleId = "R1"` and the offending node/edge. (PRD AC-03)
- [ ] **AC-03** — Given a default flow with no sibling conditional, when the tool runs, then the response contains an `error` with `ruleId = "R14"`. (PRD AC-04)
- [ ] **AC-04** — Given an orphan node that cannot reach an end, when the tool runs, then the response contains an `error` with `ruleId = "R15"`. (PRD AC-05)
- [ ] **AC-05** — Given a designer-accepted graph, when the tool runs, then no `error` is returned (advisory `warning` R12/R17 permitted). (PRD AC-06)
- [ ] **AC-06** — Given the MCP tool registration, when inspected, then it carries `ReadOnly = true`, `Destructive = false`, `Idempotent = true`, `OpenWorld = false`, and is **not** environment-sensitive (pure in-memory, no environment arg). (FR-08)
- [ ] **AC-07** — Given the tool, when it executes, then it injects `IProcessGraphValidator` directly (does **not** need `IToolCommandResolver`; not routed as an environment-aware command), constructs `ProcessGraph` from args, and returns a `ProcessGraphValidationResult`-shaped response. (FR-08)
- [ ] **AC-08** — Given `docs/McpCapabilityMap.md`, when this story merges, then `validate-process-graph` is listed (ReadOnly) with correct safety flags. (PRD AC-11, FR-18)
- [ ] **AC-ERR** — Given malformed args (e.g. an edge referencing a missing node, or an empty `nodes` array), when the tool runs, then it returns a structured finding/error (R2 / missing-node, or no-start R3) rather than an unhandled exception or a stack trace.

## Implementation Notes

`ValidateProcessGraphTool : BaseTool<ValidateProcessGraphOptions>` — **non-environment-sensitive** (pure in-memory analysis). It uses the direct path: inject `IProcessGraphValidator`, map args to `ProcessGraph`, call `Validate`, return the result. It is **MCP-only** — NOT added to `Program.cs` (no CLI verb, no environment).

Args (kebab-case JSON property names): `nodes` (`[{id, type}]`), `edges` (`[{source, target, flow-kind}]`); `type` = catalog `data-id` (OQ-05).

Files to create:
- `clio/Command/McpServer/Tools/ValidateProcessGraphTool.cs` (+ `ValidateProcessGraphOptions`/args)
- `clio/Command/McpServer/Prompts/ValidateProcessGraphPrompt.cs` (guides the agent to validate before driving — MCP policy)
- `clio.tests/Command/McpServer/ValidateProcessGraphToolTests.cs`
- `clio.mcp.e2e/ValidateProcessGraphToolE2ETests.cs`

Files to modify:
- `docs/McpCapabilityMap.md` — add `validate-process-graph` (ReadOnly) with safety flags (FR-18).

Pattern to follow: an existing read-only `BaseTool` that injects a service directly (e.g. the `get-guidance` / read-only tool family). MCP tools/prompts are auto-discovered by `WithToolsFromAssembly` / `WithPromptsFromAssembly` — no explicit DI registration. Depends on `IProcessGraphValidator` (Story 4). Per MCP maintenance policy, E2E coverage is mandatory even though `clio.mcp.e2e` is **not in CI**.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Arg → `ProcessGraph` mapping (nodes, edges, `flow-kind` parse); finding serialization shape; safety flags ReadOnly/non-destructive/idempotent; valid graph returns zero errors; R1/R14/R15 surface in the response | `clio.tests/Command/McpServer/ValidateProcessGraphToolTests.cs` |
| E2E `[Category("E2E")]` | `validate-process-graph` over the real MCP path (valid + R1 cases) | `clio.mcp.e2e/ValidateProcessGraphToolE2ETests.cs` (NOT in CI) |

Test naming: `MethodName_ShouldBehavior_WhenCondition` (AAA + `because` + `[Description]`).

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] Tool args are kebab-case (`flow-kind`); no camelCase
- [ ] Tool carries `ReadOnly = true`, `Destructive = false`, `Idempotent = true`, `OpenWorld = false`; non-environment-sensitive (direct injection of `IProcessGraphValidator`)
- [ ] Tool is MCP-only — NOT wired into `Program.cs`
- [ ] Prompt added and aligned to the tool contract (MCP policy)
- [ ] `clio.mcp.e2e` coverage added (flagged: not in CI)
- [ ] `docs/McpCapabilityMap.md` updated; PR records "MCP reviewed, no update required" for `generate-process-model`/`execute-esq`
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Targeted tests pass: `dotnet test --filter "Category=Unit&Module=McpServer"`
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12
- Tests passing: yes — `ValidateProcessGraphToolTests` (7: valid graph, R1/R13/R14/R15 surfaced, missing-node, safety flags). Module=McpServer 1116 passed; full unit suite 3888 passed, 0 failed, 20 skipped (one transient PostgreSQL/timing flake on the first run did not reproduce). `clio.mcp.e2e` builds (3 tests, not in CI).
- Notes: Created `ValidateProcessGraphTool` (standalone `[McpServerToolType]` like `GuidanceGetTool`, NOT BaseTool — pure in-memory, injects `IProcessGraphValidator`; ReadOnly/non-destructive/idempotent/closed-world; kebab args `nodes`/`edges`/`flow-kind`) + `ValidateProcessGraphPrompt` (`process-design-guidance`). Registered `AddTransient<ValidateProcessGraphTool>()` in BindingsModule (matches the tool-registration pattern → full-suite trigger). NOT wired into Program.cs (MCP-only, no CLI verb). Added `clio.mcp.e2e/ValidateProcessGraphToolE2ETests.cs` (hermetic — no environment). `docs/McpCapabilityMap.md` gained section 11 "Business Process Modeling". MCP reviewed for generate-process-model/execute-esq: no update required. Built/tested in Release (clio MCP server locks bin/Debug).
