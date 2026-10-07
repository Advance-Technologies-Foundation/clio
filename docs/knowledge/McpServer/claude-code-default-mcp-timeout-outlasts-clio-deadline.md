---
description: Claude Code 2.1.289 at default settings waits past clio's 150 s response deadline, so a client-side MCP timeout ("Request timed out", "timed out after 60s") reproduces only with MCP_TOOL_TIMEOUT set below it; then it sends notifications/cancelled
applies-to:
  - clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs
  - clio/Command/McpServer/Tools/McpProgressHeartbeat.cs
  - clio/Command/McpServer/Tools/CompileCreatioTool.cs
ticket: ENG-102333
date: 2026-10-07
---

**What is true** — measured on 2026-10-07 against local `Creatio` (.NET Framework), where a process-name
`compile-creatio` ran 4.5-5 minutes, with the call made by a sub-agent of a headless Claude Code CLI
2.1.289 session:

- default settings (`MCP_TOOL_TIMEOUT` unset): the client waited 152.5 s and 153 s in two runs; clio
  answered first, at its own 150 s deadline (`CLIO_MCP_RESPONSE_DEADLINE_SECONDS`), with the in-progress
  note, so the client never timed out;
- `MCP_TOOL_TIMEOUT=60000`: the call ended at 60 s as `MCP server "clio" tool "clio-run" timed out after 60s`,
  and the client sent `notifications/cancelled` - the clio parent acted on it (on master the sticky worker
  was gone at exactly 60 s).

The reporter of ENG-102333 used Claude Code 2.1.228 desktop at default settings and got `Request timed out`
below 150 s on four of four compiles; the seconds were not measured. So the default differs between those
versions, or between the desktop app and the CLI; which of the two was not measured.

**Why it is this way** — the client's tool timeout and clio's response deadline are independent knobs
owned by different products. clio's in-progress answer only reaches a client that waits past 150 s; a
client with a shorter timeout cancels first.

**What breaks if you ignore it** — a reproduction or a fix check run with a current Claude Code at its
defaults never reaches the client-timeout path: it shows the deadline path instead and passes for the
wrong reason. Set `MCP_TOOL_TIMEOUT` below the deadline for that run (60000 here). Users can set it lower
as well, so the cancellation path is a supported configuration and not only an old-client quirk.
