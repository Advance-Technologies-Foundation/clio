---
description: Claude Code 2.1.289 at default settings waits past clio's 150 s response deadline, so a client-side MCP timeout ("Request timed out", "timed out after 60s") reproduces only with MCP_TOOL_TIMEOUT set below it
applies-to:
  - clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs
  - clio/Command/McpServer/Tools/McpProgressHeartbeat.cs
ticket: ENG-102333
date: 2026-10-07
---

**What is true** — measured on 2026-10-07 on a .NET Framework stand where a process-name `compile-creatio`
ran about five minutes, the call made by a sub-agent of a headless Claude Code CLI 2.1.289. With default
settings the client waited 152.5 s and 153 s: clio answered first, at its 150 s
`CLIO_MCP_RESPONSE_DEADLINE_SECONDS` default, with the in-progress note. With `MCP_TOOL_TIMEOUT=60000` the
call ended at 60 s (`MCP server "clio" tool "clio-run" timed out after 60s`) and the client sent
`notifications/cancelled`. The ENG-102333 reporter's Claude Code 2.1.228 desktop timed out below 150 s at
default settings; why the two differ was not measured.

**Why it is this way** — the client's tool timeout and clio's response deadline are independent knobs
owned by different products; the in-progress note only reaches a client that waits past 150 s.

**What breaks if you ignore it** — a reproduction or a fix check run with a current Claude Code at its
defaults never reaches the client-timeout path and passes for the wrong reason. Set `MCP_TOOL_TIMEOUT`
below the deadline for that run. Users can set it lower too, so the cancellation path is a supported
configuration, not only an old-client quirk.
