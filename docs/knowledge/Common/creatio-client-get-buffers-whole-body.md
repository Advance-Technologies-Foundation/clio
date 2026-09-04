---
description: ICreatioApplicationClient.ExecuteGetRequestAsync completes only after the whole body is buffered; a size ceiling applied to its result cannot prevent the allocation, so the bounded path goes through Creatio.Client's own byte-counting download
applies-to:
  - clio/Common/ICreatioApplicationClient.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/Command/McpServer/Tools/ODataReadToFileTool.cs
ticket: "1221"
date: 2026-09-04
---

**What is true** — `ExecuteGetRequestAsync` goes through `Creatio.Client`'s `SendAsync` with the default
`HttpCompletionOption.ResponseContentRead`, so the returned task completes only once the ENTIRE response
body has been buffered in memory. `ExecuteGetRequestBoundedAsync` exists alongside it for the case where
the body must be bounded: it calls `CreatioClient.DownloadFileByGetBoundedAsync`, which issues the request
with `ResponseHeadersRead` and copies the body to a scratch file through a loop that counts bytes and
refuses to write past the ceiling. A crossing arrives as `CreatioResponseTooLargeException` and is
translated here into clio's `ResponseTooLargeException`, so callers of `IApplicationClient` never have to
reference the transport package to catch it.

**Why it is this way** — the streamed path cannot reuse `Creatio.Client`'s GET, because that method's
completion option is not a parameter: the private `SendAsync` does take an `HttpCompletionOption` and the
configured `HttpClient` is a private property, so neither is reachable from here. The bounded download runs
through the ONE configured, authenticated client, which keeps the OAuth/bearer token, the configured
certificate-validation policy (`useUntrustedSsl` is held by the client, never by this adapter) and the
session-recovery retry.

An earlier design borrowed `ExportSessionCookies()` into a fresh `HttpClientHandler`. Do not go back to it:
a parallel handler drops all three, so it worked for cookie sessions only and everything else fell back to
the buffered path that defeats the ceiling.

**Why the ceiling lives in the transport and not here** — clio held it for one release by watching the
scratch file grow from another task at a 25 ms interval. That is a TIME bound, not a byte bound: the
producer is not scheduled in step with the observer, so an arbitrary amount gets through between two
observations. Measured against this same 64 MiB ceiling on a CI agent, that shape reported 134,676,480
bytes on net10.0 and 137,232,384 on net8.0. It could not be fixed from clio — `Creatio.Client` 2.0.2 had no
per-chunk hook, no `Stream`-returning or `Stream`-accepting download, and no `HttpMessageHandler`,
`HttpClientHandler` or `HttpClient` seam anywhere in its public surface (all six `CreatioClient`
constructors take only strings, an `ICredentials`, a time-zone offset and the `isNetCore`/`useUntrustedSsl`
flags), so there was nowhere to inject a counting wrapper either. The fix was therefore a transport change,
and the ceiling has been inside its copy loop since `creatio.client` 2.1.0.

**What the transport now guarantees, and what that buys** — the count is tested before each write, so the
staged file never exceeds the ceiling and at most one 80 KiB read buffer past it is taken off the socket;
`ODataFileModeSuccessE2ETests` pins the reported count to `[MaxResponseBytes, MaxResponseBytes + 81920]`,
which is a host-independent assertion the poll-based shape cannot satisfy. EVERY status streams through
that loop, so a non-2xx body is bounded as well AND is readable from the scratch file. Before 2.1.0 a final
non-2xx was drained whole into a `MemoryStream` and no file was written at all, so this adapter's read
failed with `FileNotFoundException` and the real server error was lost — a 500 with a 128 MiB body
downloaded all 128 MiB and allocated about 465 MiB on the way to that failure.

`ODataReadToFileTool` still has **no buffered fallback**: a client that cannot stream is a hard failure
(`NotSupportedException`, surfaced redacted), because falling back would silently read the whole body into
the long-lived MCP process.

**What breaks if you ignore it** — a ceiling checked on the result of `ExecuteGetRequestAsync` is
decorative. The long-lived MCP process has already allocated the whole body by the time the check runs, so
one call against a large OData projection can exhaust it and the error message arrives too late to matter.
Measured with a 512 MiB response against a 64 MiB ceiling: the bounded download stops the client at
67,190,784 bytes and lets the server push about 72 MiB before the connection drops (that remainder is
socket buffering, which is a property of the host), while the buffered path drains all 512 MiB first.
