---
description: ReauthExecutor performs Login plus a retry with no logging at all - the warning line "Detected expired Creatio session" was shipped and then removed on purpose, and neither ReauthExecutor nor CreatioClientAdapter takes an ILogger
applies-to:
  - clio/Common/ReauthExecutor.cs
  - clio/Common/CreatioClientAdapter.cs
ticket: ENG-90393
date: 2026-08-19
---

**What is true** — when `ReauthExecutor.Execute` detects an expired session it re-authenticates and
retries the call without emitting a single log line. This is a deliberate design point, not an
oversight: an earlier version wrote `Detected expired Creatio session; re-authenticated and retrying
the request.` as a warning, and that line was removed together with the whole logger plumbing.
`ReauthExecutor` has no `ILogger` field and no logger constructor parameter, and
`CreatioClientAdapter` no longer takes one either — the warning was its only consumer.

**Why it is this way** — MCP consumers read the command's log lines back as part of the tool result.
A warning about an expired session appears there as a problem with the operation the agent asked for,
even though clio recovered transparently; it produced questions about whether the ticket should be
reopened. Recovery that succeeded is not an event the caller has to act on.

**What breaks if you ignore it** — adding a warning (or an info line) back into the re-auth path
puts a scary, unactionable message into every MCP response that happened to cross a session
expiry, and reintroduces an `ILogger` dependency into the adapter for that single call site. If you
need visibility while debugging, add it locally and do not commit it; the behaviour itself is pinned
by `CreatioClientAdapterReauthTests` and `ReauthExecutorTests`.
