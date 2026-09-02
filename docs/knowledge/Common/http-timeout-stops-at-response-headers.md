---
description: HttpClient.Timeout stops governing a request once ResponseHeadersRead returns, so a streamed body needs its own linked deadline
applies-to:
  - clio/Common/CreatioClientAdapter.cs
  - clio/Common/ICreatioApplicationClient.cs
  - clio/Command/McpServer/Tools/ODataReadToFileTool.cs
ticket: "1221"
date: 2026-09-02
---

**What is true** — `HttpClient.Timeout` covers a request issued with
`HttpCompletionOption.ResponseHeadersRead` only up to the moment the headers arrive. Every subsequent read
from `response.Content`'s stream is governed by nothing but the token those reads are given.
`ExecuteGetRequestBoundedAsync` therefore builds a linked `CancellationTokenSource`, calls `CancelAfter`
with the request timeout, and passes THAT token to the send, to `ReadAsStreamAsync` and to every body read
and buffer write.

**Why it is this way** — the two ways this call can end mean different things and must stay
distinguishable. The deadline expiring is the server failing to deliver in time, and it surfaces as
`TimeoutException` with a message naming the timeout. The caller's own token firing is the caller
withdrawing the request, and it surfaces as `OperationCanceledException`. Both arrive out of the linked
source as the same exception type, so the `when` filter checks the caller's token first: only a
cancellation the caller did not ask for is translated into a timeout.

**What breaks if you ignore it** — a server that answers the headers immediately and then withholds the
body leaves the call bounded by caller cancellation alone, and an MCP host is not guaranteed to deliver
cancellation at all (see `mcp-cancellation-does-not-reach-tools.md`). The invocation then hangs with no
bound whatsoever, and the configured `requestTimeout` reads as if it applied. Measured against a loopback
server that sent headers and one chunk and then stalled: with the linked deadline the call ends after the
configured 500 ms; without it, `BoundedGetDeadlineTests` does not fail — it never returns.
