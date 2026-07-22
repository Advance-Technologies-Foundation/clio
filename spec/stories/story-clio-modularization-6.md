# Story 6: Cross-cutting — verification & docs closure for the `Clio.Mcp` extraction

**Feature**: clio-modularization
**ADR coverage**: Phase 6 (work item 6) · D2, D8, Q5 · risks R3, R6
**ADR**: [adr-clio-modularization.md](../adr/adr-clio-modularization.md) (D8)
**PRD**: none — the ADR is self-contained (requirements embedded in the ADR Context)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: **Story 4** (transitively Stories 1-3). Closes the extraction (Phases 1-4). Does **not** depend on Story 5 — Phase 5 is a deferred follow-up that carries its own verification in its DoD.

---

## As a

QA-minded clio maintainer

## I want

a consolidated verification & docs pass over the `Clio.Mcp` extraction — full unit suite, the ClioRing gate commands, the drift test, the "pure move" MCP note, csproj/solution doc updates, and dual-target confirmation

## So that

the extraction (Phases 1-4) is provably complete and shippable as exactly one `clio` dotnet tool with MCP bundled (D2), independent of the deferred Core/Cli split

---

## Scope note

This story is the closure gate for **Stories 1-4** (the `Clio.Mcp` extraction). Phase 5 (Story 5, the physical `Clio.Core`/`Clio.Cli` split) is a **deferred follow-up** (D9/Q2) and is verified by its own DoD; Story 6 does not block on it. "All new projects" for the dual-target check means `Clio.Mcp` now, and `Clio.Core`/`Clio.Cli` once Story 5 lands.

---

## Acceptance Criteria

- [ ] **AC-01** — Given the extraction landed, when the full unit suite runs, then `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` is green (the composition root was touched by Stories 1/3/4 — a full-suite trigger).
- [ ] **AC-02 (D8, ClioRing)** — Given the moved MCP surface + `_meta` contract, when the ClioRing gate runs, then `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` passes **and** `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true` succeeds with zero IL2026/IL3050 warnings; "ClioRing compatibility reviewed" is recorded with results.
- [ ] **AC-03 (D8)** — Given the shipped templates, when `WorkspaceTemplateGuidanceDriftTests` runs, then it is green with **no** `clio/tpl/**` edits, and the MCP change summary states "pure move, no tool-contract change".
- [ ] **AC-04 (R6)** — Given `dotnet pack`, when the tool is packed, then it produces the `clio` tool bundling `Clio.Mcp.dll` (and, after Story 5, `Clio.Core.dll`), and the tool runs on both target frameworks.
- [ ] **AC-05 (Q5)** — Given every new project, when built, then `net8.0` + conditional `net10.0` is confirmed and ASP.NET/MCP-SDK resolve only in `Clio.Mcp`.
- [ ] **AC-06** — Given docs that name the project layout, when reviewed, then `clio.slnx`, any layout/csproj docs, and AGENTS.md build/deploy steps naming `clio.dll` are updated where needed, or "docs reviewed, no update required" is stated.

## Implementation Notes

ADR §Scope work item 6. Verification-only + docs; no behavior change. Ground the gate commands in AGENTS.md "Required Ring validation commands" and the smart-regression full-suite trigger (composition root touched).

- Run the full unit suite and the two ClioRing gate commands; capture results for the PR.
- Confirm `WorkspaceTemplateGuidanceDriftTests` is green with no `clio/tpl/**` edits (the shipped `AGENTS.md`/`.mcp.json` reference tool **names**, which do not change in a pure move — D8).
- Record the MCP review conclusion: "pure move, no tool-contract change".
- Update `clio.slnx` and any docs that describe the assembly layout; confirm the `clio.dll` file name is preserved (Q1) so AGENTS.md/CI invocations still work.
- Confirm the `net8.0` + `net10.0` dual-target for each new project (Q5).

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Full suite green post-extraction | `clio.tests/` |
| E2E `[Category("E2E")]` | ClioRing gate (external) — `ClioRing.Tests` + NativeAOT publish; MCP tool list unchanged in `clio.mcp.e2e` (manual — not in CI) | `clio-ring/ClioRing.Tests/`, `clio.mcp.e2e/` |

## Definition of Done

- [ ] Full unit suite green (AC-01)
- [ ] ClioRing gate — both commands pass; "**ClioRing compatibility reviewed**" + results recorded (AC-02)
- [ ] `WorkspaceTemplateGuidanceDriftTests` green with no template edits; MCP "pure move, no tool-contract change" note stated (AC-03)
- [ ] `dotnet pack` bundles `Clio.Mcp.dll`; dual-target `net8.0`/`net10.0` confirmed (AC-04/AC-05; R6/Q5)
- [ ] `clio.slnx` / csproj / AGENTS.md docs updated, or "docs reviewed, no update required" (AC-06)
- [ ] Agentic code review — final comprehensive 3-lens gate over the full extraction diff before marking ready-to-merge
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
