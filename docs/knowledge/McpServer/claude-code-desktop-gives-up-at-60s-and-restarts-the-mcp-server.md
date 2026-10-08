---
description: Claude Code desktop 2.1.293 gives up on an MCP tool call after 60 s whatever progress it gets, then restarts the clio MCP server on its next call and so ends every operation the server tracked; the CLI 2.1.289 at defaults waited past 150 s and never restarted it
applies-to:
  - clio/Command/McpServer/Tools/McpProgressHeartbeat.cs
  - clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs
  - clio/Command/McpServer/Tools/RestartTool.cs
  - clio/Command/McpServer/Tools/CompileStatusTool.cs
  - clio.mcp.e2e/CompileCreatioClientTimeoutE2ETests.cs
ticket: ENG-102333
date: 2026-10-08
---

**What is true** — measured by QA on 2026-10-08 with Claude Code desktop 2.1.293, a sub-agent and default
timeouts, three compiles out of three: the client logged `Tool 'clio-run' failed after 60s: Error: Request
timed out`, before clio's in-progress note (then due at 150 s). On its next tool call the desktop logged
`[LocalMcpServerManager] Closing clio` and reconnected. The compile's sticky worker died with the server, and
the new server answered `compile-status` with `not-found`, although the compiles ran (`CompilationHistory`).
Every agent of a session shares that one server, so one agent's timeout also ended the other agents'
in-flight calls (`Connection closed` after 43-56 s). The day before, a headless Claude Code CLI 2.1.289 at
defaults waited 152.5 s and 153 s, so clio answered first. With `MCP_TOOL_TIMEOUT=60000` the CLI gave up at
60 s and sent `notifications/cancelled`, but kept the server.

**Why it is this way** — the ceiling and the restart are the client's, and why the CLI and the desktop
differ was not measured. A server restart takes every operation record with it (the registries live in the
worker processes), so nothing clio keeps in memory survives it. clio defends in two places: the default
response deadline is 45 s, so the in-progress note arrives before 60 s (a full compile on a stand answered at
46.2 s); and a `compile-status` not-found answer reads `CompilationHistory`, which lives in the environment.
The 45 s is counted from when the tool starts, so the parent's queueing for a worker slot and the worker's
start are not inside it; under a saturated pool the answer can still cross 60 s.

**What breaks if you ignore it** — a default deadline within a few seconds of 60 (the answer reaches the
client 1-3 s after the deadline) brings the defect back on the desktop. Every compile, restart or section
create that outlasts 60 s then loses its record, and kills sibling agents' calls. A fix checked only with the
CLI at its defaults never reaches this path and passes for the wrong reason. The CLI with
`MCP_TOOL_TIMEOUT=60000` reproduces the timeout but not the restart.
