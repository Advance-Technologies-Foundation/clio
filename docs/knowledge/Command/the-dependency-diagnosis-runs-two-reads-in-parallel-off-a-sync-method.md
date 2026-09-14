---
description: EntitySchemaDependencyResolver.Resolve offloads the schema search with Task.Run - the dependency read is now issued even when nothing contributes the schema, and a warning written on the offloaded thread reaches the MCP response only because Task.Run flows the ExecutionContext
applies-to:
  - clio/Command/EntitySchemaDesigner/EntitySchemaDependencyResolver.cs
ticket: "#1461"
date: 2026-09-11
---

**What is true** — two facts the method does not state (the reasoning for the blocking wait and for the
concurrent use of one `IApplicationClient` is in the comment inside `Resolve` and is not repeated here):

- **The dependency read is issued even when no package contributes the schema.** The old code returned
  early on an empty schema search and never asked. It now costs one `GetPackageProperties` round-trip in
  that case, at no wall-clock cost, because it was already running.
- **A warning written inside the offloaded read reaches the MCP tool result only by inheritance.**
  `Task.Run` flows the `ExecutionContext`, so the `AsyncLocal` capture buffer `ConsoleLogger` appends to is
  the caller's, and `BaseTool` copies those lines into the result. Replace `Task.Run` with anything that
  suppresses the flow — `ExecutionContext.SuppressFlow`, a raw `Thread`, a custom scheduler — and those
  warnings vanish from the tool result with nothing failing anywhere.

**Why it is this way** — the reads exist only to enrich an error message. Run one after another against an
environment that accepts the connection and then stops answering they added up: the schema search, then the
dependency read, which was itself TWO round-trips before this change (the full installed-package list to
resolve the package name, then its properties), then the installed-application ranking. Each request is
bounded at 30 s, but the implicit `Login()` inside Creatio.Client is not, so the 30 s is a bound on the read
and not a guarantee about the call.

**What breaks if you ignore it** — the bound is per request, not a wall-clock guard around the group, so any
read added here sequentially reintroduces the sum and pushes the tool call back towards the MCP client's own
ceiling, where the caller gets an opaque abort instead of the candidate list. And a warning that silently
stops reaching the response is invisible in CLI use, where it still prints.
