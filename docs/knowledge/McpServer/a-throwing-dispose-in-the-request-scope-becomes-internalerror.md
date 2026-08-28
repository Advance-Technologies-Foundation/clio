---
description: a Dispose that throws on any disposable in the MCP DI graph surfaces as an opaque InternalError (-32603) for every tool that resolves it, because the SDK disposes the per-request scope after the handler returns - outside the tool method and outside the call-tool filter
applies-to:
  - clio/FakeKubernetes.cs
ticket: ENG-91830
date: 2026-08-19
---

**What is true** — the ModelContextProtocol SDK runs each tool call in its own DI scope and disposes
that scope **after** the handler has returned its result. An exception thrown from `Dispose()` of
anything the handler resolved therefore escapes from `ServiceProviderEngineScope.DisposeAsync()`
inside the SDK's scoped invocation, not from the tool. The client sees only
`McpProtocolException: InternalError (-32603)`; the tool's own code looks innocent and returned
successfully. `FakeKubernetes.Dispose()` is a deliberate no-op for exactly this reason - it is the
fallback `IKubernetes` registered when the host has no kubeconfig, so before it was fixed every
infrastructure tool failed with -32603 on any machine without Kubernetes.

**Why it is this way** — the scope teardown is the SDK's, so neither a `try/catch` inside the tool
method nor one wrapped around `next()` in a call-tool filter can observe it; both were tried and both
were blind. Nothing in the protocol error carries the inner exception, so the failure is
indistinguishable from a serialization or transport defect.

**What breaks if you ignore it** — you spend the investigation on the wrong layer. Two written
diagnoses of this exact failure blamed SDK structured-content conversion of typed POCO returns and
"the tool needs a reachable Kubernetes", and both were wrong. The only thing that identifies it is the
server-side exception: attach a stderr `ILoggerProvider` to the MCP server host, reproduce, and read
the real stack. When adding an `IDisposable` (or `IAsyncDisposable`) service to the MCP DI graph -
especially a graceful fallback stub whose other members throw `NotImplementedException` - make
`Dispose` unconditionally safe and pin it with a test.
