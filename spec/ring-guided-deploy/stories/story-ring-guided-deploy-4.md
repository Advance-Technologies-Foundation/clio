# Story 4: MCP tool forwarding — deploy/uninstall tools stream ClioStageEvent via progress _meta

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio` (foundation)
**FR coverage**: FR-08 (typed events over MCP progress `_meta`), FR-13/FR-14 (emit through the deploy/uninstall MCP tools), FR-15 (emit through the MCP tool boundary)
**AC coverage**: AC-09 (manifest→stage→run-completed sequence), AC-12 (no secrets on the wire), AC-ERR (terminal event carries friendly summary + detail/errorCode)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D1, D4)
**Status**: review
**Size**: L (full day)
**Depends on**: story-ring-guided-deploy-2, story-ring-guided-deploy-3
**Blocks**: story-ring-guided-deploy-5

---

## As a

Ring MCP client (and QA E2E author)

## I want

`InstallerCommandTool` and `UninstallCreatioTool` to forward each `ClioStageEvent` into `ProgressNotificationParams.Meta["clioStageEvent"]` over the existing `notifications/progress` channel

## So that

the Ring receives a genuinely typed, ordered `_meta` envelope for deploy/uninstall — no console parsing, no JSON-in-message — additive and forward-compatible for non-progress clients

---

## Acceptance Criteria

- [ ] **AC-01** — Given a `deploy-creatio` tool call with a `ProgressToken`, when the deploy runs, then the tool subscribes to the resolved command's `IStageEventSource.StageChanged` and sends a `notifications/progress` for each event whose `_meta.clioStageEvent` is the serialized `ClioStageEvent`, `Progress=stage.index`, `Total=stage.total`, `Message=stage.message`.
- [ ] **AC-02** — Given a full run, when observed via a raw `notifications/progress` handler, then the sequence is `manifest` first, ordered `stage` events, then a terminal `run-completed` (AC-09); no fabricated per-step "pending" events are sent.
- [ ] **AC-03** — Given `ProgressToken is null`, when the tool runs, then NO progress notifications are sent and behavior is byte-for-byte identical to today (no-op, mirroring `StartTool`/heartbeat).
- [ ] **AC-04** — Given the tool executes, when it dispatches, then it uses `InternalExecute<TCommand>(options, configureCommand: cmd => ((IStageEventSource)cmd).StageChanged += OnStageChanged)` so the per-request environment-bound command instance is subscribed (environment-sensitive MCP rule).
- [ ] **AC-05** — Given the tools are invoked through the resident `clio-run` / `clio-run-destructive` dispatcher, when progress is raised by the inner tool, then it forwards through the shared `RequestContext` unchanged (ADR fact 2).
- [ ] **AC-06** — Given a stage failure, when the terminal event is emitted, then `run-completed` carries a friendly `summary` plus `detail`/`errorCode`, and the notification stream reflects active=`failed`, remaining=`skipped` (AC-ERR).
- [ ] **AC-07** — Given a notification-send failure, when forwarding, then it is swallowed and never breaks the deploy/uninstall operation (like `McpLogNotifier`/heartbeat).
- [ ] **AC-08** — Given the tools, when inspected, then `deploy-creatio` and `uninstall-creatio` remain `Destructive=true`; no agent auto-invocation is introduced (initiation gating lives in the Ring, stories 8/9).
- [ ] **AC-ERR** — Given no secret material may cross the wire, when any forwarded `_meta` envelope is inspected in the E2E, then no connection string/credential/token appears in any field (redaction is at source, story 2/3).

## Implementation Notes

From ADR D4 + "files to modify":

- `clio/Command/McpServer/Tools/InstallerCommandTool.cs` (modify) — inject `ModelContextProtocol.Server.McpServer` and capture `RequestContext<CallToolRequestParams>` (for `Params.ProgressToken`), exactly like `StartTool`. Execute via `InternalExecute<InstallerCommand>(options, configureCommand: subscribe)`. `OnStageChanged` serializes the event into `ProgressNotificationParams.Meta["clioStageEvent"]` and calls `server.SendNotificationAsync("notifications/progress", …)`. No-op when `ProgressToken is null`.
- `clio/Command/McpServer/Tools/UninstallCreatioTool.cs` (modify) — same forwarding pattern against `UninstallCreatioCommand`.
- `clio.mcp.e2e/DeployUninstallProgressTests.cs` (new) — real `clio mcp-server`: register a raw `RegisterNotificationHandler("notifications/progress", …)` handler, read `params._meta.clioStageEvent`, assert the manifest→stage→run-completed sequence and no-secret fields. NOTE: `clio.mcp.e2e` is NOT in CI — manual run against a live stand.
- Keep `Destructive=true` on both tools; do not add auto-invocation.

Key files: `clio/Command/McpServer/Tools/InstallerCommandTool.cs`, `clio/Command/McpServer/Tools/UninstallCreatioTool.cs`, `clio.mcp.e2e/DeployUninstallProgressTests.cs`
Pattern to follow: `StartTool` (`InternalExecute<StartCommand>(configureCommand: c => c.StatusChanged += …)` + `SendNotificationAsync`), `McpProgressHeartbeat` no-op-when-null precedent. Use the `create-mcp-tool` and `test-mcp-tool` skills.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `OnStageChanged` populates `Meta["clioStageEvent"]` + `Progress`/`Total`/`Message`; no-op when `ProgressToken` null; send-failure swallowed; `Destructive=true` preserved | `clio.tests/Command/McpServer/InstallerCommandToolTests.cs`, `UninstallCreatioToolTests.cs` |
| E2E `[Category("E2E")]` (manual, not in CI) | real server emits manifest→stage→run-completed `_meta` via `notifications/progress`; no-secret assertion | `clio.mcp.e2e/DeployUninstallProgressTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` + `[Description]`.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [ ] Code compiles without new `CLIO*` warnings; behavior via DI; `McpServer`+`RequestContext` injected
- [ ] No CLI flags introduced; MCP change is additive
- [ ] No-op when `ProgressToken` null (byte-for-byte preservation for non-progress clients)
- [ ] `deploy-creatio`/`uninstall-creatio` remain `Destructive=true`; no agent auto-invocation
- [ ] Unit tests added with `[Category("Unit")]`; `clio.mcp.e2e` coverage added (flag: NOT in CI, manual)
- [ ] MCP surface reviewed; state "MCP reviewed / updated" in PR description
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests passing:
- Notes:
