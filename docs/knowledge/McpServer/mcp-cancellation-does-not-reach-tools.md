---
description: a client cancelling CallToolAsync does not flip the tool's CancellationToken in the e2e harness; an e2e that "proves" cancellation by counting requests too early passes for the wrong reason
applies-to:
  - clio/Command/McpServer/Tools/ODataCreateTool.cs
  - clio/Command/McpServer/Tools/ODataReadToFileTool.cs
  - clio.mcp.e2e/ODataFileModeSuccessE2ETests.cs
ticket: "1221"
date: 2026-09-02
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
