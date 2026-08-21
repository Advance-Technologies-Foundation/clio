---
description: an McpServerTool.Create fixture must pass BindingsModule.CreateMcpSerializerOptions() as SerializerOptions - with JsonSerializerOptions.Default the camelCase wire arguments bind nothing and record fields silently arrive at their defaults
applies-to:
  - clio/BindingsModule.cs
  - clio.tests/Command/McpServer/ClioRunNativeDispatchTests.cs
ticket: ENG-93370
date: 2026-08-19
---

**What is true** — any test that builds a tool with `McpServerTool.Create` must pass
`new McpServerToolCreateOptions { SerializerOptions = BindingsModule.CreateMcpSerializerOptions() }`.
That helper copies `McpJsonUtilities.DefaultOptions`, whose camelCase naming policy is what makes the
wire arguments (`environmentName`, `packageName`) bind to the PascalCase record properties the tool
method declares. Omit it, or substitute `JsonSerializerOptions.Default`, and the binding is
case-exact with no naming policy: nothing matches, and the arguments record arrives with every
nullable/defaulted field at its default.

**Why it is this way** — the XML summary on `CreateMcpSerializerOptions` documents only
`AllowOutOfOrderMetadataProperties` (the polymorphic-discriminator ordering LLMs do not guarantee),
so the helper reads as being about discriminator ordering. The camelCase half is inherited from
`McpJsonUtilities.DefaultOptions` and is nowhere stated, which makes substituting the framework
default look harmless.

**What breaks if you ignore it** — no exception and no binding error: the tool executes against a
fully-defaulted args record, so the test asserts on whatever the tool does with empty input. The
assertion that fails is usually about the tool's output, several layers away from the fixture's
`Create` call, so the failure reads as a defect in the tool rather than in the fixture. It also
passes deceptively for any assertion that happens to hold for the defaults.
