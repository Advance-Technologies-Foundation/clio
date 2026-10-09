---
description: Claude Code desktop 2.1.293 gives up on an MCP tool call after 60 s whatever progress it gets, then restarts the clio MCP server on its next call and so ends every worker and operation record - including other agents' calls, when ANY tool runs past 60 s; the CLI 2.1.289 at defaults waited past 150 s and never restarted it
applies-to:
  - clio/Command/McpServer/Tools/McpProgressHeartbeat.cs
  - clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs
  - clio/Command/McpServer/Tools/RestartTool.cs
  - clio/Command/McpServer/Tools/CompileStatusTool.cs
  - clio/Command/McpServer/Tools/CompileCreatioTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs
  - clio.mcp.e2e/CompileCreatioClientTimeoutE2ETests.cs
ticket: ENG-102333
date: 2026-10-09
---

**What is true** — measured by QA on 2026-10-08, Claude Code desktop 2.1.293, a sub-agent, default timeouts, three
compiles out of three: `Tool 'clio-run' failed after 60s: Error: Request timed out`; on the next call the desktop
logged `[LocalMcpServerManager] Closing clio` and reconnected; the compile's worker died with the server and the new
server answered `compile-status` with `not-found` while the compiles ran. Every agent of a session shares that
server, so one agent's timeout also ended the others' in-flight calls. A headless CLI 2.1.289 at defaults waited
past 150 s; with `MCP_TOOL_TIMEOUT=60000` it gave up at 60 s but kept the server.

QA's re-test on 2026-10-09 showed the restart does not need the compile to be slow: a second agent's three `clio-run`
calls that ran past 60 s (most likely `run-process`, then held to 150 s) restarted the server, and the compile's
sticky worker died mid-compile with it. A server restarted before the build reached the environment loses the compile
outright: killed about 55 s after `compile-creatio` was called, it left no compilation-history row in 8 minutes;
killed at 118 s, the compile finished. On that stand the first project started 50-80 s after the call.

**Why it is this way** — the ceiling and the restart are the client's. A restart takes every worker with it, and
with it the operation records and any work a worker was in the middle of, a compile request not yet taken by the
environment included.

**What breaks if you ignore it** — an answer that reaches this client after 60 s is lost with its record, and so is
every other call in flight. The default response deadline is 45 s for that reason, but it counts from when the tool
starts: a saturated worker pool's queueing and a worker's start still push an answer past it. Only the tools that
race that deadline answer by it (compile, restart, create-app-section, the bundled installs and, since round 3,
`run-process`); reads still wait up to 120 s, a per-call worker up to its 120 s budget, and most write tools have no
bound at all - the owner chose on 2026-10-09 to fix only `run-process` for now. So "no compilation-history row since
the call" can mean a compile that never ran, and an earlier compile's green `last-compilation-log` must not be read
as its verdict. A fix checked only with the CLI at its defaults never reaches this path; the CLI with
`MCP_TOOL_TIMEOUT=60000` reproduces the timeout but not the restart.
