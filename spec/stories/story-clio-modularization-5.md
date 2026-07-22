# Story 5: Phase 5 (DEFERRED follow-up) — Split the single assembly into `Clio.Core` (library) + `Clio.Cli` (Exe)

**Feature**: clio-modularization
**ADR coverage**: Phase 5 · D1, D2, D9, Q1, Q2, Q5 · risks R5, R6
**ADR**: [adr-clio-modularization.md](../adr/adr-clio-modularization.md) (D9)
**PRD**: none — the ADR is self-contained (requirements embedded in the ADR Context)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: **Story 4** — **DEFERRED follow-up per D9/Q2.** Do NOT start before Story 4 (the `Clio.Mcp` extraction) has merged; the Core/Cli split is only cheap once no core→MCP edges remain.

---

## As a

clio maintainer

## I want

the remaining single assembly split into `Clio.Core` (Library) and `Clio.Cli` (Exe, `PackAsTool`, `PackageId=clio`, referencing `Clio.Core` + `Clio.Mcp`), keeping the packed tool's assembly file name `clio.dll`

## So that

the target three-assembly topology (D1) is complete with a strictly one-directional graph, while exactly one `clio` dotnet tool still ships with MCP bundled (D2)

---

## Sequencing (read first)

This is the **deferred** phase (D9, accepted as Q2). With `Clio.Mcp` already extracted (Story 4) and no core→MCP edges left, separating Core from Cli is largely a project-file + reference exercise — namespaces stay `Clio.*`, so cross-assembly `using`s are unaffected (ADR "Namespace stability" / Consequences). It is sequenced strictly **after Story 4** and can be done incrementally at far lower risk than doing all three assemblies at once. Status is `ready-for-dev` but the `depends_on: story-clio-modularization-4` gate must be satisfied before it starts.

---

## Acceptance Criteria

- [ ] **AC-01 (D1)** — Given the split, when built, then `Clio.Core` is `OutputType=Library` (no ASP.NET / MCP SDK) and `Clio.Cli` is `OutputType=Exe` with `PackAsTool=true`, `PackageId=clio`, referencing `Clio.Core` + `Clio.Mcp` (the latter only at the composition root).
- [ ] **AC-02 (Q1)** — Given `Clio.Cli`, when built, then the produced tool assembly file name is `clio.dll` (so `dotnet clio/bin/Release/net10.0/clio.dll …` invocations in AGENTS.md/CI still resolve; `AssemblyName=clio`).
- [ ] **AC-03 (D2/R6)** — Given `dotnet pack`, when the tool is packed, then the package contains `Clio.Core.dll` + `Clio.Mcp.dll` (`CopyLocalLockFileAssemblies=true` bundles project refs — `clio.csproj:31`) and the packed tool runs on both target frameworks.
- [ ] **AC-04 (Q5)** — Given all three projects, when built, then each multi-targets `net8.0` + conditional `net10.0`; ASP.NET / MCP-SDK resolve only in `Clio.Mcp`.
- [ ] **AC-05 (R5)** — Given the `InternalsVisibleTo` consumers (`clio.tests`, `clio.mcp.e2e`), when types settle into `Clio.Core` / `Clio.Cli`, then the IVT sets are updated so both test assemblies compile and pass.
- [ ] **AC-06** — Given the composition-root split, when the full unit suite runs, then it is green with no new CLIO001/CLIO005.

## Implementation Notes

ADR §Scope Phase 5 (D9; Q1; Q2).

- Create `Clio.Core.csproj` (Library) + `Clio.Cli.csproj` (Exe). Move `Program.cs` + command options + the DI composition root to `Clio.Cli`; the behavior library (`Common`, services, `Environment`, `Workspaces`, `Package`, `Theming`, command services, plus the run-mode + `Clio.Core.Progress` contracts lifted in Stories 1-2) stays in `Clio.Core`. Namespaces stay `Clio.*` (no cross-assembly `using` churn).
- Keep the tool assembly file name `clio.dll` on `Clio.Cli` (`AssemblyName=clio`), `PackAsTool`, `PackageId=clio` (Q1/D2).
- Update `clio.slnx`; update the `InternalsVisibleTo` sets on `Clio.Core` / `Clio.Cli` (R5).
- Verify the packed tool bundles all three DLLs and runs on `net8.0` and `net10.0` (R6).

Key files: `clio/clio.csproj` (source of `PackAsTool`/`PackageId`/`CopyLocalLockFileAssemblies`/IVT), `clio/Program.cs` (moves to `Clio.Cli`), `clio.slnx`.
Pattern to follow: the already-separate `cliogate` / `Clio.Analyzers` / `clio-ring` projects in `clio.slnx`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Full suite green after the project split (behavior unchanged) | `clio.tests/` |
| Integration `[Category("Integration")]` | `dotnet pack` output contains `Clio.Core.dll` + `Clio.Mcp.dll`; packed tool runs on `net8.0` and `net10.0` (R6) | pack/run smoke (manual/CI) |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`.

## Definition of Done

- [ ] `Clio.Core` (Library) + `Clio.Cli` (Exe, `PackAsTool`, `PackageId=clio`) per D1/D2; assembly file name `clio.dll` (Q1)
- [ ] `dotnet pack` contains `Clio.Core.dll` + `Clio.Mcp.dll`; tool runs on `net8.0` + `net10.0` (R6; AC-03/AC-04)
- [ ] IVT re-pointed for `clio.tests` / `clio.mcp.e2e` (R5); both compile and pass
- [ ] **Full unit suite green** — composition-root split: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`; no new CLIO001/CLIO005
- [ ] **ClioRing compatibility gate** (assembly-layout/pack change; Ring launches clio via CLI/MCP): `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` **and** `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true`. Confirm `clio.dll` file name is preserved (Q1) so nested `clio-run` invocations are unaffected. State "**ClioRing compatibility reviewed**" + results
- [ ] MCP reviewed: project-file exercise, no tool contract change — state result; `WorkspaceTemplateGuidanceDriftTests` green with no `clio/tpl/**` edits
- [ ] Docs reviewed: csproj/solution docs updated for the three-project layout; confirm AGENTS.md `dotnet … clio.dll` steps still valid
- [ ] Agentic code review run before the PR; Blocker/High resolved
- [ ] PR description references this story file
- [ ] Deferred-gate honored: Story 4 merged before this story started (D9/Q2)

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
