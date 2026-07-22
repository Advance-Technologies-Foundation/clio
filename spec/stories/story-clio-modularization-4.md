# Story 4: Phase 4 — Extract the embedded MCP server into a new `Clio.Mcp` project

**Feature**: clio-modularization
**ADR coverage**: Phase 4 · D1, D2, D6, D7, D8, Q1, Q4, Q5 · risks R1, R3, R4, R5, R6
**ADR**: [adr-clio-modularization.md](../adr/adr-clio-modularization.md) (D6, D7, D8)
**PRD**: none — the ADR is self-contained (requirements embedded in the ADR Context)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: **Story 1, Story 2, Story 3** — all four leak buckets must be lifted first. A straight move before the lifts yields a circular `Clio.Core ↔ Clio.Mcp` project reference (Alternative C, rejected/infeasible). **Blocks** Story 5 and Story 6.

---

## As a

clio maintainer

## I want

a new `Clio.Mcp` project that owns the 286 `clio/Command/McpServer/**` files plus the ASP.NET + MCP-SDK dependencies (moved off core), with `InternalsVisibleTo` bridging (D6), the feature-toggle assembly scan re-pointed at `Clio.Mcp` (D7), the `clio.tests`/`clio.mcp.e2e` internals re-pointed, and a host-startup tool-count assertion

## So that

the plain CLI no longer compiles/carries `ModelContextProtocol`/`ModelContextProtocol.AspNetCore`/`Microsoft.AspNetCore.App`/`JwtBearer`, the dependency graph is one-directional (`Clio.Mcp → Core`; the composition root references `Clio.Mcp` only), and the MCP tool contract is byte-for-byte unchanged (pure move, D8)

---

## Acceptance Criteria

- [ ] **AC-01 (D1/Q4/Q5)** — Given the new `Clio.Mcp.csproj`, when built, then it contains `Command/McpServer/**`, references the core project, owns `Microsoft.AspNetCore.App` + `Microsoft.AspNetCore.Authentication.JwtBearer` + `ModelContextProtocol` + `ModelContextProtocol.AspNetCore`, and multi-targets `net8.0` + conditional `net10.0`. **No** `Clio.Mcp.Http` project is created (Q4).
- [ ] **AC-02** — Given the plain CLI/core project, when built, then it no longer references `Microsoft.AspNetCore.App`, `JwtBearer`, `ModelContextProtocol`, or `ModelContextProtocol.AspNetCore` (formerly `clio.csproj:135-140` and `:166-167`); there is no core→MCP project edge and no circular reference.
- [ ] **AC-03 (D6/R1)** — Given internal members MCP consumes, when the split surfaces them, then `[InternalsVisibleTo("Clio.Mcp")]` is added to the core assembly and only compiler-flagged types are promoted to `public` (start from the 4 named implicit-internal types: `ValidationPackageCommand`, `AssemblyCommand`, `EnvironmentResult`, `MarketplaceApplicationModel`) — no blanket public sweep.
- [ ] **AC-04 (D7/R3)** — Given `McpFeatureToggleFilter.RegisterEnabledPrimitives` (`McpFeatureToggleFilter.cs:127-141`) and `ExperimentalCommand` (`GetAttributedTypes`), when re-pointed, then both scan the **`Clio.Mcp` assembly** (e.g. `typeof(BaseTool).Assembly` or a marker type in `Clio.Mcp`), still passing `IEnumerable<Type>` to `WithTools`/`WithResources`/`WithPrompts` — never `Type[]`/`*FromAssembly`. The stdio call-tool handler remains registered at the stdio call-site (`BindingsModule.cs:149-156`), not inside the transport-neutral `RegisterMcpServer`.
- [ ] **AC-05 (R3)** — Given host startup, when the MCP host is registered, then a tool-count assertion verifies the expected number of `[McpServerTool]` types register (`> 0`, matching the catalog); a mis-pointed scan fails loudly instead of silently registering zero tools.
- [ ] **AC-06 (D8)** — Given the MCP surface, when this story lands, then no tool is renamed or removed and no tool's args/defaults/destructive classification/result content/error envelope changes (pure move); if a rename is ever needed, a `McpToolCompatibilityCatalog` entry is added instead of leaving a dangling name. `WorkspaceTemplateGuidanceDriftTests` stays green with no `clio/tpl/**` edits.
- [ ] **AC-07 (R5)** — Given `clio.tests` / `clio.mcp.e2e`, when types move into the core assembly and `Clio.Mcp`, then the `InternalsVisibleTo` set is updated (mirroring `clio.csproj:49-59`) so each test/e2e assembly still sees the internals it tests, and both projects compile.
- [ ] **AC-08** — Given the composition-root change, when the full unit suite runs, then it is green with no new CLIO001/CLIO005 (R4).

## Implementation Notes

ADR §Scope Phase 4 (D6, D7, D8). After Stories 1-3 the only remaining coupling is `MCP → Core` (clean one-way). DI already survives an assembly boundary: `BindingsModule.Register(..., registerMcpHost=false)` (`:145`); `RegisterMcpServer` `internal static` (`:988`) → `AddMcpServer` (`:994`) → `RegisterEnabledPrimitives` (`:1000`); the filter already takes an `Assembly` (`McpFeatureToggleFilter.cs:127-141`).

- New `Clio.Mcp.csproj` (Library), added to `clio.slnx`. Move `Command/McpServer/**` (use `git mv`; namespaces stay `Clio.Command.McpServer.*` per ADR "Namespace stability" — cross-assembly `using`s are unaffected).
- Relocate `clio.csproj:135-140` (ASP.NET `FrameworkReference` + `JwtBearer`) and `:166-167` (`ModelContextProtocol[.AspNetCore]`) onto `Clio.Mcp`; confirm the plain CLI/core compiles without them (AC-02).
- **D6:** `[InternalsVisibleTo("Clio.Mcp")]` on the core assembly; promote only compiler-flagged types (the IVT net makes the leak list a compile-time itemization, not a blocker — R1).
- **D7:** pass the `Clio.Mcp` assembly to `RegisterEnabledPrimitives` (and `ExperimentalCommand`'s `GetAttributedTypes`). Do **not** reintroduce `*FromAssembly` and do **not** pass `Type[]` (project-context.md "MCP registration caveat"; R3).
- Keep the stdio call-tool handler at the stdio call-site (`BindingsModule.cs:149-156`; matches adr-mcp-durable-invocation D1).
- Re-point `clio.tests` / `clio.mcp.e2e` `InternalsVisibleTo` (R5).
- **Q1:** keep the tool assembly file name `clio.dll` and `PackageId=clio`; this story leaves `clio` playing both Core and Cli (D9) — the `Clio.Cli` rename is Story 5.

Key files: `clio/clio.csproj` (`:49-59`, `:135-140`, `:166-167`), `clio/BindingsModule.cs` (`:145`, `:149-156`, `:988-1000`), `clio/Command/McpServer/McpFeatureToggleFilter.cs` (`:127-141`), `clio/Command/ExperimentalCommand.cs`.
Pattern to follow: the existing `Assembly`-based scan in `McpFeatureToggleFilter.RegisterEnabledPrimitives`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Host-startup tool-count assertion — N `[McpServerTool]` types register, N > 0, matches the catalog (R3) | `clio.tests/Command/McpServer/` (host registration test) |
| Unit `[Category("Unit")]` | Feature-toggle registration enumerates tools from the `Clio.Mcp` assembly via the `IEnumerable<Type>` path (not `Type[]`) | `clio.tests/Command/McpServer/McpFeatureToggleFilterTests.cs` |
| E2E `[Category("E2E")]` | `clio.mcp.e2e` compiles against the new layout; the tool list is unchanged (manual — not in CI) | `clio.mcp.e2e/` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.

## Definition of Done

- [ ] `Clio.Mcp.csproj` created; `McpServer/**` moved; ASP.NET + MCP-SDK refs relocated off core (AC-01/AC-02); dual-target `net8.0` + `net10.0` (Q5)
- [ ] `[InternalsVisibleTo("Clio.Mcp")]` + minimal public promotions (AC-03/R1); `clio.tests` / `clio.mcp.e2e` IVT re-pointed (AC-07/R5)
- [ ] `RegisterEnabledPrimitives` + `ExperimentalCommand` scan the `Clio.Mcp` assembly; `IEnumerable<Type>` preserved; stdio handler at the stdio site (AC-04/R3)
- [ ] Host-startup tool-count assertion added and green (AC-05/R3)
- [ ] Pure move — no MCP tool rename/arg/destructive/error-envelope change (D8/AC-06); `WorkspaceTemplateGuidanceDriftTests` green with **no** `clio/tpl/**` edits
- [ ] **Full unit suite green** — composition root (`BindingsModule.cs`/`Program.cs`) touched: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`; no new CLIO001/CLIO005 (R4)
- [ ] **ClioRing MCP compatibility gate (MANDATORY — the moved surface includes the Ring-consumed `_meta` forwarder):** `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` **and** `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true`. State "**ClioRing compatibility reviewed**" + results
- [ ] MCP artifacts (Tools/Prompts/Resources) reviewed — pure move; state "reviewed, no contract change"
- [ ] Docs reviewed: `clio.slnx`/csproj layout docs updated if any name the project split; AGENTS.md `dotnet clio/bin/Release/net10.0/clio.dll …` steps still valid (Q1 keeps `clio.dll`); otherwise "docs reviewed, no update required"
- [ ] Agentic code review — **full 3-lens** (multiple modules + composition root + security-sensitive) run before the PR; Blocker/High resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
