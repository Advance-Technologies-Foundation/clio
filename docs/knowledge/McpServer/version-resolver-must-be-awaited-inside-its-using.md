---
description: get-component-info/get-request-info version resolution must be awaited inside the resolver's using or the owned client is disposed mid-probe (probe-error)
applies-to:
  - clio/Command/ComponentInfoCommand.cs
  - clio/Command/McpServer/Tools/ComponentInfoTool.cs
  - clio/Command/McpServer/Tools/RequestInfoTool.cs
ticket: ENG-96840
date: 2026-09-09
---

**What is true** — every caller that resolves a platform version through
`IPlatformVersionResolverFactory.CreateOwned(settings)` must `await` `ResolveAsync` **inside** the
`using` scope. The `ResolveVersionAsync` helpers in `ComponentInfoCommand`, `ComponentInfoTool` and
`RequestInfoTool` must be `async` and `return await resolver.ResolveAsync(ct)`; they must not be
non-async methods that `return resolver.ResolveAsync(ct)` from inside the `using`.

**Why it is this way** — `PlatformVersionResolver.CreateOwned` builds a fresh
`IOwnedApplicationClient` and the resolver's `Dispose()` disposes it. `ResolveAsync` reaches its first
real `await` at `await Task.Run(() => _applicationClient.ExecutePostRequest(...))` — i.e. it offloads
the ApplicationInfoService / cliogate `GetSysInfo` probe to a thread-pool thread and returns an
*incomplete* Task to its caller. A non-async caller that does `return resolver.ResolveAsync(ct)` from
inside a `using` then exits the `using` scope the instant that incomplete Task is returned, disposing
the resolver — and its owned `CreatioClient` — while the probe is still running. The probe's next
touch of the client throws `ObjectDisposedException: Cannot access a disposed object. Object name:
'CreatioClient'`, which the resolver catches as a thrown probe → `TransientError` →
`resolvedFrom: latest-fallback`, `resolvedFromReason: probe-error`.

**What breaks if you ignore it** — every environment-scoped `get-component-info` /
`get-request-info` call degrades to `probe-error` on a fully reachable stand (ENG-96840), regardless
of framework, cliogate, or reachability, because the disposal race is deterministic — verified live:
before the fix `get-component-info crt.Button --environment <stand>` returned `probe-error`; after
awaiting inside the `using` it returned `resolvedFrom: environment`. `describe-environment` was
unaffected because it is a fully synchronous `RemoteCommand` with no `Task.Run` and no owned-resolver
`using`, which is why the two tools disagreed on the same stand. Note the probe threading itself is
correct — the bug is purely the caller disposing the owned client before the awaited probe completes.
