---
description: AddSingleton in BindingsModule gives one instance per container, not per process - a genuinely process-wide service must be held in a static readonly field and registered as that instance
applies-to:
  - clio/BindingsModule.cs
  - clio/Theming/GoogleFontsCatalog.cs
  - clio/Command/McpServer/Tools/ToolCommandResolver.cs
ticket: ENG-93985
date: 2026-08-19
---

**What is true** — `BindingsModule.Register` creates a fresh `ServiceCollection` on every call
(`clio/BindingsModule.cs:154`) and builds its own provider from it. On the MCP path
`ToolCommandResolver` calls `new BindingsModule().Register(...)` per environment/session
(`clio/Command/McpServer/Tools/ToolCommandResolver.cs:124`, `:247`, `:353`). So
`services.AddSingleton<IFoo, Foo>()` means "one per container": one per CLI invocation and one per
MCP tenant. A service that must be shared across all of them has to be instantiated once in a
`static readonly` field and registered as that instance — the shape used for
`SharedGoogleFontsAvailabilityCache` (`clio/BindingsModule.cs:95`, registered at `:248`).

**Why it is this way** — clio has no single application-lifetime container. The composition root is
a method, and the MCP host deliberately builds one container per tenant so per-environment settings
cannot leak between them.

**What breaks if you ignore it** — the service still resolves and still behaves correctly, so tests
and DI validation pass. Only the sharing is gone: a cache registered with plain `AddSingleton`
starts cold in every container, which for the Google Fonts availability cache meant re-paying a
network probe on every call from every tenant, and for a registry keyed by operation id means two
containers each holding their own empty table while status polling silently finds nothing.
