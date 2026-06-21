# Story 6: list-themes command + read-only MCP tool

**Feature**: theming-clio-devflow (ENG-90636 — Theming with AI, Clio dev flow, Contour A)
**Capability coverage**: CAP-05 (enumerate the custom themes available on an environment — verification surface for the dev flow)
**SPEC**: [spec-theming-clio-devflow.md](../prd/spec-theming-clio-devflow.md)
**ADR**: [adr-theming-clio-devflow.md](../adr/adr-theming-clio-devflow.md)
**Status**: review
**Size**: M (half day)

---

## As a

coding agent (or developer) driving the Creatio theme dev flow with clio

## I want

a `list-themes` command and a read-only MCP tool that enumerate the custom themes available on a target environment

## So that

after pushing a package and running `clear-themes-cache` I can confirm a theme is actually registered (and read its `id` / `cssClassName` before setting `DefaultTheme`) without opening Creatio

---

## Acceptance Criteria

- [x] **AC-01** — Given the CLI command, when inspected, then it derives from `RemoteCommand<ListThemesOptions>` (verb `list-themes`, alias `get-themes`), posts to `ServiceModel/ThemeService.svc/GetAvailableThemes` via `KnownRoute.GetAvailableThemes = 42`, and has **no** `[RequiresPackage]` (native endpoint; runtime auth = `CanCustomizeBranding`).
- [x] **AC-02** — Given a successful `ListResponse`, when parsed, then every `values[]` entry is exposed with all four descriptor fields (`id`, `caption`, `cssClassName`, `cssFilePath`) and printed as a table; an empty `values[]` (e.g. an unlicensed caller) is success with an empty list, not an error.
- [x] **AC-03** — Given an explicit `success=false` response, when handled, then the command exits non-zero and surfaces `errorInfo.message`; a non-JSON / empty body is tolerated as an empty catalog (mirrors `clear-themes-cache` `ProceedResponse`).
- [x] **AC-04** — Given the MCP tool, when inspected, then it derives from `BaseTool<ListThemesOptions>`, resolves the per-call environment (`ResolveCommand<ListThemesCommand>`), reads the catalog through the command's **non-logging** `TryGetAvailableThemes` data method, and returns **structured JSON** `{ success, themes:[{id,caption,cssClassName,cssFilePath}] }` (not a log-message envelope). Safety flags `ReadOnly=true`, `Destructive=false`, `Idempotent=true`; connection-mode names `list-themes-by-environment` and `list-themes-by-credentials` (kebab-case).
- [x] **AC-05** — Given the MCP server starts, when tools are registered, then both tools are registered via assembly scan through `McpFeatureToggleFilter.RegisterEnabledPrimitives` (the `IEnumerable<Type>` seam) — no `Type[]`, no `*FromAssembly`. (E2E asserts both names are advertised; manual run — E2E not in CI.)
- [x] **AC-ERR** — Given an MCP call with no resolvable environment / empty required argument, when the tool runs, then it returns a graceful `{ success:false, error }` result, not an unhandled exception.

## Implementation Notes

Mirrors the `clear-themes-cache` stack (Stories 1 + 3) but for the read-only `GetAvailableThemes` endpoint, with one deliberate divergence: the MCP tool returns **structured JSON** (the catalog is the payload), like `GetPkgListTool` / `get-user-culture`, instead of a `CommandExecutionResult` log envelope. The shared data method `ListThemesCommand.TryGetAvailableThemes` (no logger writes) keeps console output off the MCP JSON-RPC channel; the CLI `ExecuteRemoteCommand` override calls the same method and prints.

Key files:
- `clio/Common/ServiceUrlBuilder.cs` — `KnownRoute.GetAvailableThemes = 42` → `ServiceModel/ThemeService.svc/GetAvailableThemes`.
- `clio/Command/ListThemesCommand.cs` — command + `ListThemesOptions` + `ThemeDescriptor` record.
- `clio/Command/McpServer/Tools/ListThemesTool.cs` — `BaseTool<ListThemesOptions>` + `ListThemesResult` / `ThemeDescriptorResult` records.
- `clio/Program.cs` + `clio/BindingsModule.cs` — register option type, dispatch, `AddTransient<ListThemesCommand>`.
- Docs: `help/en/list-themes.txt`, `docs/commands/list-themes.md`, `Commands.md`, `Wiki/WikiAnchors.txt`, `docs/McpCapabilityMap.md`.
- `clio/Command/McpServer/Resources/ThemingGuidanceResource.cs` — add the `list-themes-by-environment` verification step; remove the stale "clio has no list-themes tool" note.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | request URL (NetFW/NetCore), values parsing → `Themes`, empty list, `success=false` → error, non-JSON tolerance | `clio.tests/Command/ListThemesCommand.Tests.cs` |
| Unit `[Category("Unit")]` | MCP arg mapping → `ListThemesOptions`; env-aware command resolution; structured success/failure mapping; safety flags; empty-arg failure | `clio.tests/Command/McpServer/ListThemesToolTests.cs` |
| E2E `[Category("E2E")]` (manual, NOT in CI) | real `clio mcp-server` advertises both `list-themes-by-environment` / `list-themes-by-credentials` | `clio.mcp.e2e/ListThemesToolE2ETests.cs` |

## Definition of Done

- [x] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005)
- [x] CLI command + read-only MCP tool implemented per AC-01..AC-ERR
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [x] `clio.mcp.e2e` advertise coverage added (flag: not in CI — manual)
- [x] Docs (help/docs/Commands.md/WikiAnchors/McpCapabilityMap) + theming guidance updated
- [ ] Targeted/full unit suite run (Program/BindingsModule/Common touched → full suite)
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-19
- Implementation completed: 2026-06-19
- Tests passing: pending suite run
- MCP E2E (manual) run: pending live stand
- Notes: read-only counterpart to `clear-themes-cache`; verifies a theme is registered after a push + cache refresh.
