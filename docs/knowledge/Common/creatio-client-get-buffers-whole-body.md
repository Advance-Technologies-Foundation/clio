---
description: ICreatioApplicationClient.ExecuteGetRequestAsync completes only after the whole body is buffered; a size ceiling applied to its result cannot prevent the allocation
applies-to:
  - clio/Common/ICreatioApplicationClient.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/Command/McpServer/Tools/ODataReadToFileTool.cs
ticket: "1221"
date: 2026-09-02
---

**What is true** — `ExecuteGetRequestAsync` goes through `Creatio.Client`'s `SendAsync` with the default
`HttpCompletionOption.ResponseContentRead`, so the returned task completes only once the ENTIRE response
body has been buffered in memory. `ExecuteGetRequestBoundedAsync` exists alongside it for the case where
the body must be bounded: it issues the request with `ResponseHeadersRead` and pulls the body in chunks,
abandoning the transfer once the ceiling is passed.

**Why it is this way** — the streamed path cannot reuse `Creatio.Client`'s GET, because that method's
completion option is not a parameter. It borrows the session instead: the cookies come from
`ExportSessionCookies()` on the already-authenticated client, so no second authentication path exists.

**What breaks if you ignore it** — a ceiling checked on the result of `ExecuteGetRequestAsync` is
decorative. The long-lived MCP process has already allocated the whole body by the time the check runs, so
one call against a large OData projection can exhaust it and the error message arrives too late to matter.
Measured with a 512 MiB response against a 64 MiB ceiling: the streamed path lets the server push about
72 MiB before the connection drops (the overshoot is bytes already in flight), while the buffered path
drains all 512 MiB first. `ODataReadToFileTool` prefers the streamed path and falls back to the buffered
one only for a transport that does not implement it.
