# Story 2: Phase 2 — Move the StageEvent/progress contract to `Clio.Core.Progress` and decouple Start/Stop from the MCP SDK (buckets 2 + 2b)

**Feature**: clio-modularization
**ADR coverage**: Phase 2 · D4, Q3 · risks R2, R4
**ADR**: [adr-clio-modularization.md](../adr/adr-clio-modularization.md) (D4)
**PRD**: none — the ADR is self-contained (requirements embedded in the ADR Context)
**Status**: ready-for-dev
**Size**: L (full day)
**Depends on**: none — independent leak-bucket lift; **blocks** Story 4

---

## As a

clio core developer decoupling the core project from the embedded MCP server

## I want

the neutral StageEvent/progress contract relocated into Core under the `Clio.Core.Progress` namespace (Q3) with JSON wire/byte/schema parity preserved, and `StartCommand`/`StopCommand` decoupled from the MCP-SDK type `ProgressNotificationValue` (sub-leak 2b)

## So that

`Clio.Core` stops importing `ModelContextProtocol` and no longer hosts a shared contract from a folder named `McpServer`, while ClioRing — which depends only on the JSON emitted into `_meta.clioStageEvent`, not on clio's C# type — sees no change

---

## Acceptance Criteria

- [ ] **AC-01** — Given the five neutral contract types (`ClioStageEvent`, `ClioStageEventContract`, `IStageEventSource`, `IStageEventEmitter`, `StageIds`), when moved to Core, then they live under namespace `Clio.Core.Progress` and the ~4 emitters compile against the new namespace (mechanical `using` update — the types are already `public`, so this is a relocation, not a rewrite).
- [ ] **AC-02 (R2)** — Given the moved `ClioStageEvent`, when serialized into the `clioStageEvent` envelope, then output is byte-identical to today: every serialized property name, casing, enum string value, and field order is preserved; a serialization byte/schema-parity test asserts this.
- [ ] **AC-03 (R2)** — Given ClioRing's consumer fixture `clio-ring/ClioRing.Tests/Fixtures/ClioStageEvent.contract.ndjson` (Ring keeps its own DTO `clio-ring/ClioRing.Ipc/ClioStageEvent.cs` + `ClioStageEventAdapter.cs`), when the parity test runs, then additive/unknown-field tolerance is preserved and no field is renamed, removed, or reordered.
- [ ] **AC-04** — Given the MCP `_meta` forwarder `StageEventProgressForwarder` (and anything referencing `ModelContextProtocol`), when the contract moves, then the forwarder **stays in the MCP surface** (it does not move to Core); only the neutral contract moves.
- [ ] **AC-05 (2b)** — Given `StartCommand`/`StopCommand`, when this story lands, then neither imports `ModelContextProtocol` nor exposes `ProgressNotificationValue`; their status is routed through the Core-owned progress contract (or a neutral Core progress DTO), and the MCP tool wrappers translate to the SDK type inside the MCP surface.
- [ ] **AC-06** — Given the codebase after this story, when grepped for `using ModelContextProtocol` outside `clio/Command/McpServer/**`, then `StartCommand.cs` and `StopCommand.cs` no longer appear (Core no longer imports the MCP SDK for progress).
- [ ] **AC-07** — Given the composition-root/Common change, when the full unit suite runs, then it is green with no new CLIO001/CLIO005 (R4).

## Implementation Notes

Bucket 2 + sub-leak 2b (ADR §Scope Phase 2; D4; Q3). The six files under `clio/Command/McpServer/Progress/` (`ClioStageEvent.cs`, `ClioStageEventContract.cs`, `IStageEventSource.cs`, `StageEventEmitter.cs`, `StageEventProgressForwarder.cs`, `StageIds.cs`) carry an already-`public` contract emitted by core long-running commands.

- Move the **neutral** contract types to Core, namespace `Clio.Core.Progress` (Q3). **Freeze** serialization attributes/property names on the move (R2) — do not change `[JsonPropertyName]`, casing, enum string values, or field order.
- Keep `StageEventProgressForwarder` (the MCP `_meta` forwarder, references `ModelContextProtocol`) in the MCP surface (AC-04).
- Emitters to re-`using`: `clio/Command/CreatioInstallCommand/CreatioInstallerService.cs:106` (implements `IStageEventSource`, injects `IStageEventEmitter` `:138`), `clio/Command/CreatioInstallCommand/InstallerCommand.cs:246`, `clio/Command/UninstallCreatio.cs:96`, `clio/Common/CreatioUninstaller.cs:108`.
- **Sub-leak 2b:** `clio/Command/StartCommand.cs:12` and `clio/Command/StopCommand.cs:14-15` import the raw SDK and expose `event EventHandler<ProgressNotificationValue> StatusChanged` (`StartCommand.cs:37`, `StopCommand.cs:58`, plus many step sites). Route their status through the Core progress contract / a neutral Core progress DTO; translate to `ProgressNotificationValue` only inside the MCP tool wrappers (`Clio.Mcp`).

Key files: `clio/Command/McpServer/Progress/*` (move), `clio/Command/StartCommand.cs`, `clio/Command/StopCommand.cs` (decouple).
Pattern to follow: the existing `IStageEventEmitter` injection in `CreatioInstallerService`.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `ClioStageEvent` serialization byte/schema parity (property names/casing/enum strings/field order) + unknown-field tolerance | `clio.tests/Command/McpServer/` StageEvent tests (or a new `Clio.Core.Progress` test) |
| Unit `[Category("Unit")]` | `StartCommand`/`StopCommand` `StatusChanged` emits the neutral Core type; no `ProgressNotificationValue` on the Core surface | `clio.tests/Command/StartCommandTests.cs` / `StopCommandTests.cs` |
| E2E `[Category("E2E")]` | ClioRing gate (external) — `ClioRing.Tests` parity + NativeAOT publish (see DoD); not in clio CI | `clio-ring/ClioRing.Tests/` |

Test naming `MethodName_ShouldBehavior_WhenCondition`; AAA + `because` + `[Description]`; NSubstitute.

## Definition of Done

- [ ] Neutral contract lives in `Clio.Core.Progress`; the `_meta` forwarder stays in MCP (AC-04); no new CLIO001/CLIO005 (R4)
- [ ] StageEvent JSON byte/schema-parity test added and green (R2, AC-02/AC-03)
- [ ] `StartCommand`/`StopCommand` decoupled from `ProgressNotificationValue`; Core no longer imports `ModelContextProtocol` for progress (AC-05/AC-06)
- [ ] **Full unit suite green** — `Common/**` + `CreatioInstallCommand` + `Command` + `McpServer` touched (>3 modules incl. `Common`): `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`
- [ ] **ClioRing MCP compatibility gate (MANDATORY — moves a Ring-consumed `_meta.clioStageEvent` contract, D4/D8):** run `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` **and** `dotnet publish clio-ring/ClioRing.Desktop/ClioRing.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true` (zero IL2026/IL3050). State "**ClioRing compatibility reviewed**" + the exact commands/results in the PR
- [ ] MCP reviewed: pure contract relocation — no tool rename/arg/destructive/error-envelope change; state result. `WorkspaceTemplateGuidanceDriftTests` green with no `clio/tpl/**` edits
- [ ] Docs reviewed — no user-facing command/flag change: "docs reviewed, no update required"
- [ ] Agentic code review run before the PR; Blocker/High resolved
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
