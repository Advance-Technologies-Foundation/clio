---
description: in clio.mcp.e2e, cancelling the SDK client's CallToolAsync never reaches the clio parent either - an e2e about a client that gave up must send notifications/cancelled for a request id it chose, or it passes against the defect
applies-to:
  - clio.mcp.e2e/CompileCreatioClientTimeoutE2ETests.cs
  - clio.mcp.e2e/Support/Mcp/McpServerSession.cs
  - clio/Command/McpServer/Relay/McpWorkerCallDispatcher.Sticky.cs
ticket: ENG-102333
date: 2026-10-07
---

**What is true** — in this harness the clio PARENT does not see a cancelled `McpServerSession.CallToolAsync`
either, not only the tool (`mcp-cancellation-does-not-reach-tools.md`). Measured: an e2e that cancelled its
`compile-creatio` call once the compile was running in the sticky worker passed against master, whose
parent reaps that worker on caller cancellation. The same e2e failed on master with `not-found`, as it must,
once it sent the request through `session.Client.SendRequestAsync` with an `Id` it chose and then
`session.Client.SendNotificationAsync(NotificationMethods.CancelledNotification, new CancelledNotificationParams
{ RequestId = thatId })`. A real client does reach the parent: Claude Code with `MCP_TOOL_TIMEOUT=60000`
killed the worker at exactly 60 s on a stand.

**Why it is this way** — not measured. The SDK client only abandons its local await here; why its own
cancellation does not arrive was not traced.

**What breaks if you ignore it** — a test of what clio does when a client gives up goes green on the code
it was written to catch. Write the wire sequence explicitly; `CallCompileAndGiveUpAsync` in
`CompileCreatioClientTimeoutE2ETests` is the worked example.
