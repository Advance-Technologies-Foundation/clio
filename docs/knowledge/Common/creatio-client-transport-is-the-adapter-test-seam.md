---
description: CreatioClientAdapter talks to the NuGet CreatioClient only through ICreatioClientTransport, created inside the adapter (not DI) and skipped in the BindingsModule auto-registration scan
applies-to:
  - clio/Common/ICreatioClientTransport.cs
  - clio/Common/CreatioClientTransport.cs
  - clio/Common/CreatioClientAdapter.cs
  - clio/BindingsModule.cs
ticket: "#1313"
date: 2026-09-12
---

**What is true** — `CreatioClientAdapter` no longer holds a `Lazy<CreatioClient>` field. It holds an
`ICreatioClientTransport`, which mirrors the NuGet client one-for-one (requests, login, cookies,
listener events, `IsCreated`, `Dispose`) and adds no behaviour. The production implementation
`CreatioClientTransport` is constructed **inside** the adapter's private constructor over the lazy
client, the same way `ReauthExecutor` and `LoginDiagnostics` are - so every construction site gets it,
including the MCP per-tenant child container that builds its Creatio connections inline and never goes
through `ApplicationClientFactory`. `ICreatioClientTransport` is therefore in the `ValidateOnBuild`
skip list in `BindingsModule`, next to `IReauthExecutor` and `ILoginDiagnostics`.

The transport also exposes `EnsureCreated()`, and the adapter calls it **inside** `_lifetimeSync`,
where it holds the disposed check. Creation used to happen under that lock because the adapter
resolved the `Lazy` itself; letting the transport create the client lazily on first use would move
creation outside the lock, so a caller that had already passed the disposed check could create a
client after `Dispose` read `IsCreated` as `false` - and that client, with its pooled HTTP transport,
would never be released.

**Why it is this way** — `CreatioClient` is a concrete NuGet class with no interface and no virtual
members, so the adapter's unit tests could not substitute it. They resolved the adapter's `Lazy` to
`null` and never invoked the callback handed to the reauth executor, which left the delegation itself
untested: a PUT routed to the underlying DELETE, or a dropped url / body / timeout argument, passed
every test. Proving "the write is issued exactly once" for the no-replay rule needs to count the
underlying calls, which is impossible without the seam.

**What breaks if you ignore it** — registering the transport in DI instead cannot work: its only
constructor argument is the adapter's own lazy client, so the container has nothing to supply, and
leaving it out of the skip list makes the auto-registration scan pick it up and fail `ValidateOnBuild`,
which refuses to start the whole host, `mcp-server` included. Two tests reach the lazy client by
reflection through `_transport` (`CredentialPassthroughClientIdentityTests`); renaming that field
breaks them with no compile error.
