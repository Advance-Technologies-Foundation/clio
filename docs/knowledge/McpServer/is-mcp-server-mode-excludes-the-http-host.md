---
description: Program.IsMcpServerMode is true for the mcp-server/mcp verb ONLY and is false on mcp-http, so anything gated on it is stdio-only; McpHostTransport.Current is the transport-neutral flag
applies-to:
  - clio/Program.cs
  - clio/Command/McpServer/McpAdvisoryLog.cs
  - clio/Command/McpServer/McpServerCommand.cs
  - clio/Command/McpServer/McpToolErrorFilter.cs
  - clio/Common/ConsoleLogger.cs
  - clio/Common/EnvironmentNotFoundError.cs
ticket: ENG-95885
date: 2026-09-07
---

**What is true** — `Program.IsMcpServerMode` is assigned once, from `Program.IsMcpCommand`, which matches
the verb name `mcp-server` or its alias `mcp` and nothing else. The HTTP host is a separate verb
(`McpHttpServerCommand`, `[Verb("mcp-http")]`), so `IsMcpServerMode` is **false** in an `mcp-http`
process. Every behaviour gated on that flag — `ConsoleLogger`'s console suppression,
`McpServerCommand.WarnDuringStartup`'s stderr mirror, `McpToolErrorFilter.ReportArgumentShape`'s stderr
mirror — is therefore **stdio-only**, and on stdio one process is one agent session.

The transport-neutral flag is `McpHostTransport.Current`: `Program` sets it to `Stdio` for the
`mcp-server`/`mcp` verb and `McpHttpServerCommand.Run` sets it to `Http`, with `Unknown` as the
fail-closed default. Gate on that when a behaviour must cover both hosts.

**Why it is this way** — the flag predates the HTTP host and its original job was "is stdout the JSON-RPC
channel", which is a property of the stdio transport, not of MCP hosting in general. `McpHostTransport`
was added later (ENG-95262) precisely because the routing questions needed a three-valued,
transport-declared answer that `IsMcpServerMode` could not give.

**What breaks if you ignore it** — two silent failure modes, one of which has already shipped into review
twice:

1. You write a fix for `mcp-http` behind an `IsMcpServerMode &&` gate. The `&&` short-circuits, the code
   never executes on that host, and no test catches it because the in-process suites are never in MCP
   server mode either. ENG-95885 round 8 rescoped the shape-mirror rate gate from a process-wide set to a
   `ConditionalWeakTable` keyed on `RequestContext.Server` to fix a per-session leak on `mcp-http` — a
   scenario the short-circuit makes unreachable. The rescoping then **lost the cap on stdio**, because
   `RequestContext.Server` is not one stable reference per session, so every call got its own set and the
   synchronous `Console.Error.WriteLine` ran on every normalized call. Only the e2e
   (`McpToolErrorFilterE2ETests.FlatCall_ShouldMirrorOneShapeLineToServerStandardError_AndCapTheRepeat`,
   which drives a real stdio child and reads its stderr) caught it, and that check is advisory, so it
   nearly merged.
2. You read the flag as "the process is hosting MCP" and reason about `mcp-http` from it. A stale comment
   in `clio/Common/EnvironmentNotFoundError.cs` states the flag is set from the verb
   "(mcp-server / mcp-http)", which is wrong and is plausibly where that assumption entered.

If a session-scoped cap is ever genuinely wanted on `mcp-http`, note that
`McpHttpServerCommand.ConfigureHttpTransport` sets
`SessionMode = HttpServerSessionMode.StatefulForInitializeClients`: a non-initializing client is served
statelessly with a fresh server instance per request, so object identity is not a session key there
either. It needs a stable session identifier plus a process-wide backstop.
