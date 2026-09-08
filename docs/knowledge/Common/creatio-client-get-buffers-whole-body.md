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
completion option is not a parameter. In `Creatio.Client` 2.0.2 (the newest published version) the private
`SendAsync` DOES take an `HttpCompletionOption`, and the configured `HttpClient` is a private property, so
neither is reachable from here. What IS reachable is `CreatioClient.DownloadFileByGetAsync`, which issues
its own request with `ResponseHeadersRead` and copies the body incrementally to a path on disk. The bounded
GET therefore runs through the ONE configured, authenticated client and stages the response in a scratch
file, rather than rebuilding a transport beside it.

An earlier design borrowed `ExportSessionCookies()` into a fresh `HttpClientHandler`. Do not go back to it:
a parallel handler drops the OAuth/bearer token, the configured certificate-validation policy
(`useUntrustedSsl` is held by the client, never by this adapter) and the session-recovery retry — so it
worked for cookie sessions only, and everything else fell back to the buffered path that defeats the
ceiling.

**What it consequently does NOT cover** — two limitations are still open, both of them in
`DownloadFileByGetAsync` rather than here:

- It does not distinguish response statuses: a non-2xx body is staged like a successful one, so the tool
  classifies the staged content afterwards instead of being told by the transport.
- It enforces no byte bound of its own. The ceiling is applied on this side by watching the scratch file
  grow and abandoning the transfer, so the overshoot is whatever was already in flight when the watcher
  tripped — not a hard limit the transport honours.

Closing either properly means a bounded GET on `Creatio.Client` itself
(`Advance-Technologies-Foundation/creatioclient`) that reports status and enforces the ceiling in the
transport. Until then `ODataReadToFileTool` has **no buffered fallback at all**: a client that cannot
stream is a hard failure (`NotSupportedException`, surfaced redacted), because falling back would silently
read the whole body into the long-lived MCP process.

**What breaks if you ignore it** — a ceiling checked on the result of `ExecuteGetRequestAsync` is
decorative. The long-lived MCP process has already allocated the whole body by the time the check runs, so
one call against a large OData projection can exhaust it and the error message arrives too late to matter.
Measured with a 512 MiB response against a 64 MiB ceiling: the streamed path lets the server push about
72 MiB before the connection drops (the overshoot is bytes already in flight), while the buffered path
drains all 512 MiB first. `ODataReadToFileTool` uses the streamed path only; a transport that
does not implement it fails the call instead of falling back.
