---
description: a clio command's CreatioClient comes from one of three independent construction sites, so a new authentication mode wired only into ApplicationClientFactory reaches roughly a third of the commands
applies-to:
  - clio/Common/ApplicationClientFactory.cs
  - clio/BindingsModule.cs
  - clio/Program.cs
ticket: none
date: 2026-09-17
---

**What is true** — which client a command runs on depends on how `Program` dispatches it, and there
are three independent construction sites:

1. `ApplicationClientFactory.CreateClient` / `CreateEnvironmentClient` — the factory, for callers
   that inject `IApplicationClientFactory`.
2. `BindingsModule.BuildCreatioClient` and `BuildRemoteDataProvider` — the DI registrations for the
   active environment and for every per-environment MCP child container. They build their clients
   **inline** and never call the factory.
3. `Program.CreateRemoteCommandClient`, used by `ExecuteRemoteCommand` — the dispatch path for
   commands constructed with `Activator.CreateInstance`, such as `ping`. It also built its adapter
   inline until the external-access mode was added.

**Why it is this way** — the three paths have different ownership and lifetime rules: the factory
hands out shared or leased clients, the DI registrations own a process-wide lazy singleton, and the
`ExecuteRemoteCommand` path owns a client for exactly one invocation and disposes it. Each was
written where its lifetime is managed.

**What breaks if you ignore it** — a new authentication mode added only to the factory is simply not
applied on the other two paths, and the fall-through is what makes it dangerous rather than merely
incomplete: site 2's login/password branch defaults to `Supervisor`/`Supervisor`, and site 3's
defaults to whatever the environment happens to carry. The command then connects successfully **as a
different identity** and reports nothing. This was observed while adding external access: `ping`
answered `Unauthorized` and `get-info` answered "unexpected response" while the token sat unused —
neither message mentioned authentication mode at all. When you add a mode, cover all three, and
prove it by running one command from each dispatch path rather than by reading the factory.
