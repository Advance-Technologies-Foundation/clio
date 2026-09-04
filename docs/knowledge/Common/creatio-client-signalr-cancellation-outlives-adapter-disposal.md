---
description: CreatioClient SignalR cancellation can re-enter Login after its adapter is disposed, so a client that started Listen must not dispose the pooled HTTP transport during provider teardown
applies-to:
  - clio/Common/CreatioClientAdapter.cs
  - clio/Command/McpServer/SessionContainerCache.cs
date: 2026-08-31
---

**What is true** — Creatio.Client 2.x starts its SignalR listener on background work. Cancelling the
listener token does not synchronously finish that work: its reconnect path can still call `Login()`
while the owning service provider is being disposed.

**Why it is this way** — `StartListening` returns no task or listener handle that clio can await.
Request-only adapters own and dispose their pooled client normally, but an adapter that has started
listening must leave the client alive until process or GC teardown after cancellation drains.

**What breaks if you ignore it** — disposing that client during listener teardown races the reconnect,
throws `ObjectDisposedException` from `CreatioClient.Login`, and crashes the entire NUnit/MCP host
instead of merely ending the listener.
