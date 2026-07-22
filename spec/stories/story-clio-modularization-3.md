# Story 3: Phase 3 — Relocate MCP-hosted utilities into Core/Common and neutralize config templating (buckets 3 + 4)

**Feature**: clio-modularization
**ADR coverage**: Phase 3 · D5 · risk R4
**ADR**: [adr-clio-modularization.md](../adr/adr-clio-modularization.md) (D5)
**PRD**: none — the ADR is self-contained (requirements embedded in the ADR Context)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: none — independent leak-bucket lift; **blocks** Story 4

---

## As a

clio core developer decoupling the core project from the embedded MCP server

## I want

the three utilities that live under `clio/Command/McpServer/Tools/` but are consumed by core relocated into Core/Common (keeping their interfaces + DI registrations), the two doc-comment `<see cref>` references converted so they do not force a project reference, and a test asserting the config templating still emits the `mcp-server` verb

## So that

after buckets 3 and 4 are lifted the only remaining core↔MCP coupling is the clean one-directional `MCP → Core` reference, which is the last prerequisite before `Clio.Mcp` can be extracted

---

## Acceptance Criteria

- [ ] **AC-01** — Given the three utilities, when relocated into Core/Common, then each keeps its interface + DI registration (`IToolCommandResolver` stays registered — former `BindingsModule.cs:605`), introducing **no** CLIO005 dead registration and **no** CLIO001 manual-`new` (R4).
- [ ] **AC-02** — Given the core consumers, when the utilities move, then `ComponentInfoCommand`, `PageBaselineGuard`, `PageDesignerPresenceNotifier`, `PageFileWriter`, `BuildThemeCommand`, `ComponentRegistryRefreshCommand`, and `ListPrintablesCommand` compile against the new location with unchanged behavior.
- [ ] **AC-03** — Given the two doc-comment `<see cref>` references (`clio/Command/ProcessModel/IProcessGraphValidator.cs:81`, `clio/Common/EnvironmentNotFoundError.cs:10`), when converted to text or to a type in the referencing assembly, then they no longer force a project reference onto the MCP surface.
- [ ] **AC-04 (bucket 4)** — Given `MergeClioMcpServer`, when it emits launch config, then the args still spell the `mcp-server` verb; a test asserts this. Bucket 4 needs **no** structural change (it has no `McpServer` type dependency; it stays valid as long as the `mcp-server` verb survives — it does: `clio/Command/McpServer/McpServerCommand.cs:11` `[Verb("mcp-server", Aliases=["mcp"])]`).
- [ ] **AC-05** — Given the DI/Common change, when the full unit suite runs, then it is green with no new CLIO001/CLIO005.

## Implementation Notes

Buckets 3 and 4 (ADR §Scope Phase 3; D5).

Bucket 3 — relocate into `Clio.Core` (Common), keeping interfaces + DI registrations; namespaces stay `Clio.*` so cross-assembly `using`s are unaffected (ADR "Namespace stability"):
- `clio/Command/McpServer/Tools/CompactPrimitiveArrayJsonElementConverter.cs` → used by `clio/Command/ComponentInfoCommand.cs:59`.
- `clio/Command/McpServer/Tools/McpToolExecutionLock.cs` (`McpToolExecutionLock` / `CwdLock`) → used by `clio/Command/PageBaselineGuard.cs`, `clio/Command/PageDesignerPresence/PageDesignerPresenceNotifier.cs`, `clio/Command/PageFileWriter.cs`.
- `clio/Command/McpServer/Tools/ToolCommandResolver.cs` (`IToolCommandResolver` / `ToolCommandResolver`, DI-registered at `BindingsModule.cs:605`) → used by `clio/Command/Theming/BuildThemeCommand.cs`, imported by `clio/Command/ComponentRegistryRefreshCommand.cs` and `clio/Command/ListPrintablesCommand.cs`.

- Move interface + DI registration together (R4). If the registration line (`BindingsModule.cs:605`) needs a `using` update, that keeps this a full-suite trigger.
- Convert the two `<see cref>` doc comments (`IProcessGraphValidator.cs:81`, `EnvironmentNotFoundError.cs:10`) to text or a same-assembly type — do **not** add a project reference for a cosmetic doc link (AC-03).

Bucket 4 — `MergeClioMcpServer` in `clio/Common/Skills/CodexTomlConfigEditor.cs:30` (verb arg `:41`) and `clio/Common/Skills/JsonConfigEditor.cs:51` (verb arg `:64`) embeds the literal `mcp-server` launch verb; no code change, only add the assertion test (AC-04).

Key file: `clio/Command/McpServer/Tools/ToolCommandResolver.cs`
Pattern to follow: the existing `IToolCommandResolver` DI registration (`BindingsModule.cs:605`) — keep it interface-based and alive.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Utilities behave identically post-move; `IToolCommandResolver` resolves from DI (no manual `new`) | `clio.tests/Command/` (resolver + converter + lock tests) |
| Unit `[Category("Unit")]` | `MergeClioMcpServer` emits the `mcp-server` verb in the launch args | `clio.tests/Common/Skills/` (Codex TOML + JSON editor tests) |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.

## Definition of Done

- [ ] Three utilities in Core/Common with interface + DI registration intact; no new CLIO001/CLIO005 (R4)
- [ ] Two doc-comment `<see cref>` refs converted (no forced project reference) (AC-03)
- [ ] `MergeClioMcpServer` `mcp-server`-verb assertion test added and green (AC-04)
- [ ] **Full unit suite green** — `BindingsModule.cs` (DI registration) + `Common/**` touched, a full-suite trigger: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`
- [ ] MCP reviewed: utilities moved out of `McpServer/Tools/` with no MCP tool contract change — state result; `WorkspaceTemplateGuidanceDriftTests` green with no `clio/tpl/**` edits
- [ ] ClioRing: relocated utilities are **not** a Ring-consumed contract — state "ClioRing compatibility reviewed, no Ring-consumed contract changed" citing the three moved utility paths
- [ ] Docs reviewed — no user-facing change: "docs reviewed, no update required"
- [ ] Agentic code review run before the PR; Blocker/High resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
