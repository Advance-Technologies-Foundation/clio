---
description: the served-content telemetry counters (guidance_reads, guidance_bytes, contract_bytes, ...) exist only on events recorded by the clio process that served - never on the CAADT hook's events, which come from separate clio processes
applies-to:
  - clio/Common/Telemetry/ServedContentMeter.cs
  - clio/Common/Telemetry/TelemetryService.cs
  - clio/Command/McpServer/Tools/SendTelemetryTool.cs
  - clio/Command/McpServer/Tools/GuidanceGetTool.cs
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
ticket: ENG-100157
date: 2026-10-08
---

**What is true** - the CAADT toolkit hook records its session-start floor and every `session_usage`
reading by spawning a detached `clio mcp-server` per dispatch (`hooks/telemetry/dispatch.mjs` in
creatio-ai-app-development-toolkit). That process serves nothing, so those events never carry the
counters; only the agent's own stage events, recorded through the agent's MCP connection, do. The stamp
also depends on `send-telemetry`, `get-guidance` and `get-tool-contract` all running in-process in the
stdio host, where they share one meter.

**Why it is this way** - the counters are an in-memory count of one process, and no channel links the
agent's process to the hook's.

**What breaks if you ignore it** - a consumer that expects the counters on `session_usage` rows finds
none, and joining hook tokens to served bytes by `session_id` works only when the agent reused the
hook's session id (the guidance otherwise tells it to generate its own). Routing `send-telemetry` to an
MCP worker makes the stamp disappear silently; only `ServedContentTelemetryE2ETests` would notice.
