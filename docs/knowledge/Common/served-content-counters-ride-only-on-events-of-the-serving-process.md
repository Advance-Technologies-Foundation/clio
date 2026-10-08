---
description: the served-content telemetry counters (guidance_reads, guidance_rereads, guidance_bytes, contract_reads, contract_bytes, guidance_library_version) are cumulative per stdio clio mcp-server process and absent until it served something - the CAADT hook's events come from separate clio processes and never carry them, and mcp-http keeps an inert meter
applies-to:
  - clio/Common/Telemetry/ServedContentMeter.cs
  - clio/Common/Telemetry/TelemetryService.cs
  - clio/BindingsModule.cs
  - clio/Command/McpServer/Tools/GuidanceGetTool.cs
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
ticket: ENG-100157
date: 2026-10-08
---

**What is true** - `ServedContentMeter` counts what one process served and is registered only in the
stdio host block of `BindingsModule.Register`; every other container (CLI, per-environment builds,
mcp-http) resolves `NullServedContentMeter`. `TelemetryService` stamps the totals on every event the SAME
process records and stamps nothing while that process has served nothing. The CAADT toolkit hook
(`hooks/telemetry/dispatch.mjs` in creatio-ai-app-development-toolkit) records its session-start floor
and every `session_usage` reading by spawning a detached `clio mcp-server` per dispatch, which serves
nothing, so those events never carry the counters; only the agent's own stage events do.

**Why it is this way** - a stdio process is one agent session, so a process total is a session total,
and the hook's process cannot see the agent process's memory. A zero stamped by the hook's process would
claim a session that cost nothing. mcp-http serves many sessions from one process, where one count would
stamp a session's cost on another session's events.

**What breaks if you ignore it** - moving the counting meter into `RegisterInto` makes mcp-http report a
mix of sessions; stamping zeros puts a free session in ClickHouse for every hook dispatch; a consumer that
reads an absent counter on a schema 3 event as zero, or that expects the counters on `session_usage`
rows, under-counts every run. A token-plus-served-bytes join by `session_id` works only when the agent
reused the hook's session id - the guidance otherwise tells it to generate its own.
