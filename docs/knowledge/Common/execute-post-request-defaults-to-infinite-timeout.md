---
description: IApplicationClient.ExecutePostRequest defaults requestTimeout to Timeout.Infinite, so a Common-layer client that omits the argument can hang forever; CreatioRequestOptions supplies the bounded 100s default instead
applies-to:
  - clio/Common/IApplicationClient.cs
  - clio/Common/CreatioRequestOptions.cs
  - clio/Common/CreatioServiceClient.cs
date: 2026-08-19
---

**What is true** — every request method on `IApplicationClient` except
`CallConfigurationService` declares `int requestTimeout = Timeout.Infinite`. Passing no timeout is
therefore not "use a sensible default", it is "wait forever". Common-layer service clients get a
bound only because `CreatioServiceClient.PostAndDeserialize` requires a
`CreatioRequestOptions`, whose defaults (`TimeOut = 100_000`, `MaxAttempts = 3`,
`RetryDelay = 1`) mirror `RemoteCommandOptions`. A caller constructing a bare
`new CreatioRequestOptions()` is relying on that record's default, not on the client's.

**Why it is this way** — the infinite default predates the retry/timeout options and is preserved
for compatibility with existing call sites (`CreatioClientAdapter` even carries a Sonar
suppression comment for keeping it).

**What breaks if you ignore it** — an unresponsive or misrouted Creatio endpoint makes the call
never return. In an MCP tool that means the tool invocation hangs until the client's own ceiling
kills it, with no clio-side error to explain what happened; the environment can stay wedged for the
lifetime of a long-running `mcp-server` process. Do not remove `CreatioRequestOptions` from a new
client's signature "because the defaults are fine" — the bounded default lives only there.
