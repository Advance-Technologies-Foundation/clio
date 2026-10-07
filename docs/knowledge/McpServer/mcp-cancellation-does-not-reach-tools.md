---
description: a client cancelling CallToolAsync does not flip the tool's CancellationToken in the e2e harness - nor the parent's dispatcher token; an e2e that "proves" cancellation that way passes for the wrong reason; send notifications/cancelled with a known request id instead
applies-to:
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Command/McpServer/Tools/ODataReadToFileTool.cs
  - clio.mcp.e2e/ODataFileModeSuccessE2ETests.cs
  - clio.mcp.e2e/CompileCreatioClientTimeoutE2ETests.cs
  - clio.mcp.e2e/Support/Mcp/McpServerSession.cs
ticket: "1221"
date: 2026-10-07
---

**What is true** — an MCP tool method can take a `CancellationToken`, the SDK binds it, and `clio-run`
forwards it into the dispatched tool (`ClioRunTool.DispatchAsync` → `tool.InvokeAsync(context, token)`).
But when the e2e client cancels its `CallToolAsync` token, the token the tool observes does **not** flip:
measured on a six-row `odata-create` batch with a 1.2 s delay per row, all six POSTs still reached the
stub after the call was cancelled at 3 s and the assertion waited longer than the whole batch.

**Why it is this way** — cancelling the client call abandons the await locally. For the server to cancel,
the `notifications/cancelled` message has to be read and applied while the tool is still running, and that
did not happen in this harness. It is a property of the client/server message loop, not of the tool.

**What breaks if you ignore it** — an e2e that cancels a call and then counts requests **too early** passes
without proving anything: the counter is simply behind. The first version of the `odata-create`
cancellation e2e waited only two row-delays and passed; waiting longer than the entire batch showed all six
rows had been sent. Any test of this shape must wait longer than the work would take if nothing stopped it,
and if it then fails, the conclusion is that cancellation is not delivered — not that the tool ignores it.
Cancellation guards belong in unit tests, where the token is under the test's control.

**It is not just the tool's token (ENG-102333, measured 2026-10-07).** The PARENT's token does not flip
either: with the worker boundary in place, the unfixed parent reaps a sticky starter's worker when its
caller cancels, yet an e2e that cancelled `CallToolAsync` once the compile was running inside the worker
saw `compile-status` answer `running` - the parent had never been told, so the test passed against the
very defect it was written for. A real client does tell it: Claude Code with `MCP_TOOL_TIMEOUT=60000`
killed the worker at exactly 60 s on a stand. What works in this harness is to write the wire sequence
yourself - `session.Client.SendRequestAsync` with a `JsonRpcRequest` whose `Id` you chose, then
`session.Client.SendNotificationAsync(NotificationMethods.CancelledNotification, new CancelledNotificationParams
{ RequestId = thatId })` - after which the same e2e failed on the unfixed server with `not-found`, as it
must. `CompileCreatioClientTimeoutE2ETests.CallCompileAndGiveUpAsync` is the worked example.
