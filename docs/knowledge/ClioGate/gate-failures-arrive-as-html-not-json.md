---
description: a failing ClioGate endpoint answers with an IIS/Creatio HTML error page, so JsonSerializer.Deserialize throws "'<' is an invalid start of a value" - every gate call must wrap that into an actionable message
applies-to:
  - clio/Package/PackageUnlocker.cs
  - clio/Command/ShowPackageFileContentCommand.cs
date: 2026-08-30
---

**What is true** — when a `/rest/CreatioApiGateway/<Method>` call throws server-side, the response
body is an HTTP error page (HTML), not the declared JSON. `IApplicationClient.ExecutePostRequest`
returns that body as a string, and an unguarded `JsonSerializer.Deserialize<bool>` on it fails with
`System.Text.Json.JsonException: '<' is an invalid start of a value`. An empty body is also
possible. `PackageUnlocker.CallGate` is the reference handling: it rejects an empty response, catches
`JsonException`, and rethrows an `InvalidOperationException` naming the route and pointing at the
Creatio `Error.log`.

**Why it is this way** — the gate is a WCF service behind the web server; an unhandled exception is
turned into a status page by the host long before clio's deserializer sees it. The same happens when
the URL is wrong or the deployed cliogate is older than the method being called.

**What breaks if you ignore it** — the surfacing error blames JSON parsing, so the failure reads as
a clio serialization defect and sends the reader to the wrong code. Real cases behind that message
have been a null-payload crash inside the gate and a stale installed cliogate; neither is
discoverable from the `'<'` text. New gate callers must not deserialize the raw body directly.
