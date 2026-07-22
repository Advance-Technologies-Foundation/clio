# Story 1: Phase 1 — Core run-mode service replaces the `Program.IsMcpServerMode` static (bucket 1)

**Feature**: clio-modularization
**ADR coverage**: Phase 1 · D3 · risk R4
**ADR**: [adr-clio-modularization.md](../adr/adr-clio-modularization.md) (D3)
**PRD**: none — the ADR is self-contained (problem + requirements embedded in the ADR Context; no separate PRD)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: none — independent leak-bucket lift; **blocks** Story 4 (the `Clio.Mcp` extraction)

---

## As a

clio core developer decoupling the core project from the embedded MCP server

## I want

an injectable, Core-owned run-mode abstraction (e.g. `IExecutionContext` / `IRuntimeMode` exposing `bool IsMcpServerMode`) that `ConsoleLogger` consumes by constructor injection, replacing every read of the `Program.IsMcpServerMode` static

## So that

the cross-cutting run-mode concern lives in Core instead of being read from a static on the CLI entry point — the first of the four leak-bucket lifts that must land before `Clio.Mcp` can be extracted without creating a circular assembly reference (Alternative C is infeasible while this leak exists)

---

## Acceptance Criteria

- [ ] **AC-01** — Given the new run-mode abstraction, when the DI container is built, then `IExecutionContext`/`IRuntimeMode` (exposing `bool IsMcpServerMode`) resolves as a registered singleton and `ConsoleLogger` receives it via constructor injection (no manual `new` — CLIO001).
- [ ] **AC-02** — Given `IsMcpServerMode` is `true`, when `ConsoleLogger` writes at each of the seven former suppression sites, then console output is suppressed exactly as today (no behavioral change).
- [ ] **AC-03** — Given `IsMcpServerMode` is `false` (default CLI run), when `ConsoleLogger` writes, then output is emitted exactly as today.
- [ ] **AC-04** — Given the codebase after this story, when `clio/Common/ConsoleLogger.cs` is grepped, then it contains zero reads of `Program.IsMcpServerMode`; `Program` sets the run-mode value into the service at startup (the former set-site `clio/Program.cs:1240`), and `registerMcpHost: IsMcpServerMode` threading (`clio/Program.cs:1610`) still resolves through the service.
- [ ] **AC-05** — Given the composition-root change, when the full unit suite runs, then it is green and no new CLIO001/CLIO005 diagnostics appear in modified files (R4).

## Implementation Notes

Bucket 1 of the four prerequisite lifts (ADR §Scope Phase 1; D3). `Program.IsMcpServerMode` (`clio/Program.cs:272` `internal static bool`; set `:1240`; read `:1446`, `:1610`) is read by `clio/Common/ConsoleLogger.cs` at **7 sites** (`:195,218,232,245,256,481,514`) to suppress console output under MCP — a run-mode concern that leaked into `Common`.

- Add the abstraction + implementation in the core project (today `clio/Common` or `clio/Core`), interface-based and DI-registered — behavior class keeps interface + registration (CLIO001/CLIO005, R4). Namespace stays `Clio.*` (ADR "Namespace stability").
- Inject the service into `clio/Common/ConsoleLogger.cs`; replace the 7 static reads (`:195,218,232,245,256,481,514`).
- `Program` writes the run-mode value into the service during startup (former set-site `clio/Program.cs:1240`); keep the `registerMcpHost: IsMcpServerMode` wiring (`clio/Program.cs:1610`) intact.
- Do **not** touch the `mcp-server` / `mcp-http` dispatch (`clio/Program.cs:594-595`); this story only relocates the run-mode state.

Key file: `clio/Common/ConsoleLogger.cs`
Pattern to follow: existing constructor-injected `Clio.Common` services already consumed via DI.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Output suppressed at each former site when `IsMcpServerMode` is true; emitted when false; service resolves as a singleton and is injected (not `new`ed) | `clio.tests/Common/ConsoleLoggerTests.cs` (+ run-mode service test) |

Test naming `MethodName_ShouldBehavior_WhenCondition`; explicit Arrange/Act/Assert; `because` on every assertion; `[Description]` on every test; NSubstitute for mocks.

## Definition of Done

- [ ] Code compiles without CLIO001-CLIO005 warnings; no new `CLIO*` in modified files; behavior class keeps interface + DI registration (R4)
- [ ] `ConsoleLogger` has zero `Program.IsMcpServerMode` reads (AC-04)
- [ ] **Full unit suite green** — composition root (`Program.cs`/`BindingsModule.cs`) **and** `Common/**` touched, a full-suite trigger: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`
- [ ] No new or renamed CLI flags in this story (kebab-case rule unaffected)
- [ ] MCP reviewed: no tool/prompt/resource contract change — state "MCP reviewed, no update required"
- [ ] ClioRing: run-mode/console suppression is **not** a Ring-consumed contract (Ring uses typed `_meta` stage events, not console text) — state "ClioRing compatibility reviewed, no Ring-consumed contract changed" citing `clio/Common/ConsoleLogger.cs` + `clio/Program.cs`
- [ ] Docs reviewed — no command behavior/flag change: state "docs reviewed, no update required"
- [ ] Agentic code review (quality/correctness/security) run before opening the PR; Blocker/High findings resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
