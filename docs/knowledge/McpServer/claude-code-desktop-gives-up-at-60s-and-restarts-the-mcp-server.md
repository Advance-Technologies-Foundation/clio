---
description: Claude Code desktop 2.1.293 gives up on an MCP tool call after 60 s whatever progress it gets, then restarts the clio MCP server on its next call and so ends every worker and operation record; the CLI 2.1.289 at defaults waited past 150 s and never restarted it
applies-to:
  - clio/Command/McpServer/Tools/McpProgressHeartbeat.cs
  - clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs
  - clio/Command/McpServer/Tools/RestartTool.cs
  - clio/Command/McpServer/Tools/CompileStatusTool.cs
  - clio.mcp.e2e/CompileCreatioClientTimeoutE2ETests.cs
ticket: ENG-102333
date: 2026-10-08
---

**What is true** — measured by QA on 2026-10-08, Claude Code desktop 2.1.293, a sub-agent, default timeouts, three
compiles out of three: `Tool 'clio-run' failed after 60s: Error: Request timed out`; on the next call the desktop
logged `[LocalMcpServerManager] Closing clio` and reconnected; the compile's worker died with the server and the new
server answered `compile-status` with `not-found` while the compiles ran. Every agent of a session shares that
server, so one agent's timeout also ended the others' in-flight calls. A headless CLI 2.1.289 at defaults waited
past 150 s; with `MCP_TOOL_TIMEOUT=60000` it gave up at 60 s but kept the server.

**Why it is this way** — the ceiling and the restart are the client's. A restart takes every worker with it, and
with it the operation records and any work a worker was in the middle of.

**What breaks if you ignore it** — an answer that reaches this client after 60 s is lost with its record. The
default response deadline is 45 s for that reason, but it counts from when the tool starts: a saturated worker
pool's queueing, a worker's start, and a restart request that alone outlasts 60 s still push an answer past it. A
fix checked only with the CLI at its defaults never reaches this path; the CLI with `MCP_TOOL_TIMEOUT=60000`
reproduces the timeout but not the restart.
