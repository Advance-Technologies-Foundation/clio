---
description: creatio.client 2.0.2's synchronous ExecuteGetRequest discards its maxAttempts argument and swallows transport exceptions into an empty string, so a caller asking for 3 attempts gets exactly one request and reads a transient failure as an empty body
applies-to:
  - Directory.Packages.props
  - clio/Common/CreatioClientAdapter.cs
  - clio/Common/IApplicationClient.cs
  - clio/Command/McpServer/Tools/ODataFieldValidation.cs
ticket: GH-1315
date: 2026-09-12
---

**What is true** — in the pinned `creatio.client` 2.0.2 (`Directory.Packages.props`), the SYNCHRONOUS
`CreatioClient.ExecuteGetRequest(url, requestTimeout, maxAttempts, delaySec)` passes the literal `1`
into its own `SendAsync` retry loop in place of the caller's `maxAttempts`, and wraps the call in
`catch (HttpRequestException) { return string.Empty; }` /
`catch (TaskCanceledException) { return string.Empty; }`. So the argument produces no second request
at any value, and a connection failure or a timeout arrives at the caller as an EMPTY BODY rather
than as an exception. Established by
`~/.dotnet/tools/ilspycmd -t Creatio.Client.CreatioClient ~/.nuget/packages/creatio.client/2.0.2/lib/netstandard2.0/Creatio.Client.dll`.
The ASYNC `ExecuteGetRequestAsync` DOES forward `maxAttempts`; only the string-returning synchronous
overload — the one `IApplicationClient` exposes and nearly every clio caller uses — does not.
Neither overload calls `EnsureSuccessStatusCode` on this path (`ReadResponseBody` only reads the
content), so a 4xx/5xx is NOT a transport failure here: its error page or envelope is returned as the
body. Only a connection failure or a timeout produces the empty string.

**Why it is this way** — the synchronous overload is a compatibility wrapper over the async pipeline
and it flattens both the retry and the failure channel to keep the `string` return type total. The
interface signature still advertises `maxAttempts`, so nothing at the call site says the value is
inert, and the argument reads as an implemented feature.

**What breaks if you ignore it** — a bounded retry expressed as `ExecuteGetRequest(url, timeout, 3, 1)`
does not exist: the call is made once, and its unit test proves only that the argument was passed.
`ODataFieldValidation`'s pre-write check shipped exactly that shape (GH-1227) and was rewritten to
loop around the fetch AND its classification, retrying the empty-body outcome, because that outcome
is the only visible trace a transient failure leaves. Any retry you need from a synchronous
`IApplicationClient` GET must be written above the call and must key off the returned BODY, not off an
exception that will never be thrown.
