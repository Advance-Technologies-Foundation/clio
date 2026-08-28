---
description: BindingsModule.RegisterAssemblyInterfaceTypes auto-registers every concrete class implementing any Clio.I* interface, so a non-service type that picks up such an interface breaks ValidateOnBuild and clio mcp-server exits 1 with zero output
applies-to:
  - clio/BindingsModule.cs
ticket: ENG-93365
date: 2026-08-19
---

**What is true** — `BindingsModule.RegisterAssemblyInterfaceTypes` reflects over the whole clio
assembly and registers every non-abstract class against **each** interface it implements whose
namespace starts with `Clio` and whose name starts with `I`. Membership is decided by the interface
name alone; nothing checks that the class is a service. The container is then built with
`ValidateOnBuild`, which tries to construct every registration. Two skips exist for this reason: an
explicit skip-list for interfaces whose implementations take primitive constructor arguments, and a
blanket `typeof(Exception).IsAssignableFrom(type)` skip, because an exception's `string message`
constructor can never be resolved.

**Why it is this way** — the auto-scan removes the need to hand-register the several hundred
`I<Thing>`/`<Thing>` pairs clio has, and the naming rule is the only signal available at reflection
time. Marker interfaces (a type implementing `Clio.I*` purely to be recognised, not to be resolved)
are outside what that rule can express.

**What breaks if you ignore it** — a marker interface added to any non-service type takes the whole
host down at startup, `clio mcp-server` included: `ValidateOnBuild` throws before any output is
written, so the process exits 1 with an empty stdout and the MCP client reports only that the server
would not start. Nothing points at the new interface. When you introduce a `Clio.I*` marker, check
whether its implementers are constructible by DI, and extend the skips rather than renaming the
interface out of the pattern.
