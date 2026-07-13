# ADR: Progress-heartbeat keep-alive for long-running MCP tools

- **Status:** Accepted
- **Date:** 2026-06-10
- **Feature:** `mcp-progress-heartbeat`
- **Jira:** [ENG-91274](https://creatio.atlassian.net/browse/ENG-91274) (sub-task of ENG-90506)
- **PRD source:** the ENG-91274 description is treated as the PRD (atomized analysis of ENG-90506 `run1`).

---

## Context

The clio MCP server (`clio/Command/McpServer/**`, `ModelContextProtocol` 1.3.0, stdio
transport) exposes application-lifecycle tools that call the Creatio backend
synchronously. Evidence from ENG-90506 `run1` (Copilot CLI / Windows):

- `#46` `get-app-info` + `list-app-sections` → *"still running after 240 seconds"*
- `#89` `create-app-section` → *"still running after 240 seconds"*

### Root cause (confirmed by code reading)

1. The *"still running after N seconds"* message is **not** produced by clio. It is the
   **MCP client's** inactivity timeout (30s/240s depending on client). clio cannot set
   the client timeout directly.
2. `ApplicationSectionCreate` / `ApplicationSectionGetList` / `ApplicationGetInfo` /
   `ApplicationCreate` run their real work **synchronously** against the backend
   (`applicationSectionCreateService.CreateSection(...)` at
   `clio/Command/McpServer/Tools/ApplicationTool.cs:201`, etc.). The method is `async Task`
   only because of an icon-palette `await`; the backend call blocks the thread.
3. During that blocking call the server sends **no** `notifications/progress`. The MCP
   protocol resets a client's inactivity timer whenever a progress notification with the
   request's `progressToken` arrives. With silence, the client times out while the server
   keeps working — costing the assistant extra verification round-trips and, in
   less-supervised runs, pushing it onto long fallback paths (raw SQL, manual UI).
4. A working server-side pattern already exists: `StartTool.cs:34-43` forwards
   `command.StatusChanged` as `notifications/progress` using
   `RequestContext<CallToolRequestParams>.Params.ProgressToken`.

### Constraints

- `ModelContextProtocol` 1.3.0; stdio transport; `WithToolsFromAssembly`.
- Application tools are plain `[McpServerToolType]` classes (NOT `BaseTool<T>`-derived) and
  therefore do **not** run under the global `McpToolExecutionLock`.
- The MCP client SDK (`McpClient.CallToolAsync`) has an `IProgress<ProgressNotificationValue>`
  overload — progress notifications are observable from tests (enables deterministic E2E).
- CLI/MCP conventions: kebab-case tool names, `[Category("Unit")]`, AAA + `because` +
  `[Description]` tests, no new `CLIO*` warnings, DI-first (no `new` for behavior classes).

---

## Decision

Adopt a **progress-heartbeat keep-alive**. While a long-running application tool performs
its synchronous backend work, a background timer emits `notifications/progress` for the
current request at a fixed cadence (**15 s**), each notification resetting the client's
inactivity timer. The tool **still returns its final structured result synchronously** — the
AI-facing contract is unchanged (no operation handles, no polling loop).

### Mechanism

- A reusable helper, `McpProgressHeartbeat`, runs the synchronous `work` delegate on the
  calling thread while a background `Task` (driven by a linked `CancellationTokenSource`)
  invokes a `beat` callback every `interval`. When work returns (or throws), the heartbeat
  is cancelled and awaited. Exceptions from `work` propagate unchanged.
- The `beat` callback is an abstraction (`Func<int, …>`), so unit tests verify cadence with
  a fake sink and a short interval — no real `McpServer` needed.
- **No-op when the client did not send a `progressToken`** (`RequestContext.Params.ProgressToken == null`):
  the helper runs `work` inline with zero added behavior. This preserves byte-for-byte
  current behavior for clients that do not request progress.
- Each beat sends `ProgressNotificationValue { Progress = elapsedSeconds, Message = "<tool> still running… (Ns elapsed)" }`
  (monotonically increasing progress, indeterminate total). This doubles as human-readable
  status, so AI sees activity instead of silence.

### Scope (per ENG-91274 + decision to cover the whole family)

Heartbeat is wired into the application tool family in `ApplicationTool.cs`:

| Tool | Today |
|------|-------|
| `create-app` | already `async`, no `RequestContext`/progress |
| `create-app-section` | already takes `McpServer`, no `RequestContext`/progress |
| `update-app-section` | already takes `McpServer`, no `RequestContext`/progress |
| `delete-app-section` | sync, no progress |
| `list-app-sections` | sync, no progress |
| `get-app-info` | sync, no progress |

Each tool gains a `RequestContext<CallToolRequestParams>` parameter (to read `progressToken`)
and wraps its backend service call with `McpProgressHeartbeat`.

---

## Alternatives considered

1. **Fire-and-poll** (return an operation id; separate status tool polled by the client).
   *Rejected for this iteration:* breaks the AI contract (callers must run a polling loop),
   requires server-side operation state across otherwise stateless tool calls, larger
   surface and higher risk. ENG-91274 explicitly allows "and/or", and heartbeat satisfies
   every acceptance criterion at a fraction of the complexity.
2. **"Raise client-side timeouts."** clio cannot set the client's timeout; the only
   protocol lever is progress notifications — which *is* the heartbeat. So this reduces to
   the chosen decision.
3. **Make the backend work truly async + propagate `CancellationToken`.** Worthwhile
   independently, but it does not by itself stop the client timeout (still silent on the
   wire) and is a much larger change to the underlying services. Out of scope here;
   heartbeat is orthogonal and sufficient.

---

## Consequences

### Positive
- `create-app-section` (and the family) no longer hit a premature client timeout under
  normal backend latency.
- Reuses the existing, proven `StartTool` progress pattern; minimal new surface.
- AI observes steady progress, so it does not abandon the tool for raw-SQL / manual-UI
  fallbacks (the ENG-90506 failure mode).
- Zero behavior change for clients that do not request progress (no-op path).

### Negative / risks
- Heartbeat reports elapsed time, not true percent-complete (synchronous work has no
  intermediate milestones). Acceptable: the goal is keep-alive, not a progress bar.
- If the backend genuinely hangs forever, the heartbeat would too — mitigated by linking
  the heartbeat CTS to the request `CancellationToken`, so client disconnect / server
  shutdown stops the beats.
- One background `Task` per long-running call. Negligible: application tools are not
  high-frequency and are effectively serialized in practice.

---

## Acceptance criteria mapping (ENG-91274)

| AC | How this ADR satisfies it |
|----|---------------------------|
| `create-app-section` no longer returns a premature timeout under normal latency | 15 s heartbeat resets the client inactivity timer for the whole family |
| Long-running tools expose an explicit poll/await contract (documented) | tool `[Description]`, `McpServerInstructions`, app-modeling/maintenance resources, `ApplicationPrompt` updated with an "await + progress" contract |
| Unit tests + `clio.mcp.e2e` cover the timeout / poll path | `McpProgressHeartbeat` cadence/no-op/exception unit tests; E2E asserts ≥1 progress notification via the `IProgress` overload |
| Tool docs + MCP capability map updated | `docs/McpCapabilityMap.md` + affected command docs/help updated |

---

## Implementation notes

- New file: `clio/Command/McpServer/Tools/McpProgressHeartbeat.cs`.
- Constant `DefaultInterval = TimeSpan.FromSeconds(15)` (below common client inactivity
  thresholds; not user-configurable in this iteration).
- Heartbeat send path mirrors `StartTool.OnStatusChanged` (`server.SendNotificationAsync("notifications/progress", …)`),
  with failures swallowed like `McpLogNotifier` so keep-alive never breaks tool execution.
- Tests live in `clio.tests/Command/McpServer/`; E2E extends `clio.mcp.e2e/Support/Mcp/McpServerSession.cs`.

---

## Addendum: ENG-93087 — tool-level stage markers

ENG-91274 (above) shipped a **content-free keep-alive beat**. ENG-93087 extends the same
`McpProgressHeartbeat` primitive with **semantic stage markers** and applies progress to
`sync-schemas` (which previously had none).

- **Why not the `BaseTool` `StatusChanged` path (the original scope's wording).** `StatusChanged`
  is a bespoke event on only `StartCommand`/`StopCommand`; none of `create-app`,
  `create-app-section`, or `sync-schemas` runs through a `BaseTool` or a command that raises it
  (the first two go through services, the third is a batch loop over commands). So the markers are
  emitted **at the tool level** through the same `notifications/progress` send path, not via
  `configureCommand`.
- **How.** `RunWithProgressAsync` / `RunWithProgressAndDeadlineAsync` gained reporter-aware overloads
  whose `work` receives an `Action<string> reportStage`. A single `ProgressChannel` serializes both
  the timer beats and caller-pushed stage markers through one monotonically-increasing `Progress`
  counter (no regression, no interleaved partial write). No-op when the client sent no progress token.
- **What each tool reports.** `sync-schemas` pushes a per-operation marker (`"<i>/<n>: <op> <schema>"`)
  before each operation and its seed step (purely tool-level, no command change). `create-app` and
  `create-app-section` push coarse markers ("enriching…", "creating application", "creating section")
  around their service calls; finer sub-stages would require threading a callback into the services
  (deferred).
- **Tests.** `McpProgressHeartbeatTests` covers the reporter overloads, `ProgressChannel` monotonic
  sequencing, and the reporter no-op path; `SchemaSyncToolE2ETests` asserts a per-operation stage
  marker reaches the client via the `IProgress` overload.
