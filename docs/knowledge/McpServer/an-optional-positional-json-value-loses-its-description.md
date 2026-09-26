---
description: measured - the MCP SDK emits an optional POSITIONAL JsonElement?/JsonNode?/object? args parameter as {"default":null} and silently drops its [Description]; an init property keeps it
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyProcessAsNewVersionTool.cs
  - clio/Command/McpServer/Tools/McpToolArgumentSupport.cs
  - clio/Command/McpServer/Tools/McpToolRegistrySchemaContract.cs
  - clio.tests/Command/McpServer/ProcessDesignerEmittedSchemaTests.cs
ticket: ENG-100153
date: 2026-09-26
---

**What is true** — measured through `McpServerTool.Create` on the production serializer options (SDK
pinned in `Directory.Packages.props` on 2026-09-26), for an args record carrying `[Description]` on each
member:

```
JsonElement  A            (positional, no default) -> {"description":"A desc"}   required
JsonElement? B = null     (positional)             -> {"default":null}            description GONE
JsonNode?    C = null     (positional)             -> {"default":null}            description GONE
object?      D = null     (positional)             -> {"default":null}            description GONE
JsonElement? F {get;init} (init property)          -> {"description":"F desc"}   not required
JsonElement  E = default  (positional)             -> schema generation THROWS
```

None of them carries a `type`: a "any JSON value" member is emitted typeless, and
`McpToolRegistrySchemaContract.ReadType` then reports it as `"object"` in the derived contract — for an
array argument too.

**Why it is this way** — the SDK's schema for a JSON-value type is the boolean `true` schema; for a
defaulted parameter it builds the node from the default value, and the description is not attached to
that path. An init property has no parameter default, so it takes the path that keeps the description.

**What breaks if you ignore it** — for a non-resident tool the emitted schema IS the contract an agent
reads (`get-tool-contract` derives it), so a dropped description ships an argument with no explanation at
all, and nothing fails: the tool binds and runs. This is why
`ModifyProcessAsNewVersionArgs.Operations` is an init property rather than a positional parameter like
its siblings; moving it back into the constructor to "match the others" deletes the only text that says
the argument is optional and what omitting it does. `ProcessDesignerEmittedSchemaTests` pins the
description and the required status of each process tool's JSON-document argument. Do not give a
`JsonElement` member a `= default` either: that breaks schema generation for the whole tool.
