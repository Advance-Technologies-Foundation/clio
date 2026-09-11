---
description: adding `= null` to an MCP args-record parameter adds `,"default":null` (15 bytes) to the tools/list payload for every RESIDENT tool, so relaxing a resident record can trip the serialized-size ratchet even though it also shortens `required`
applies-to:
  - clio/Command/McpServer/McpFeatureToggleFilter.cs
  - clio.tests/Command/McpServer/McpProfileGatingTests.cs
date: 2026-09-06
---

**What is true** — giving an MCP argument-record parameter a default value does two opposite things to
the serialized `tools/list` payload: it removes the parameter's name from the schema's `required` array,
and it makes the SDK emit `,"default":null` on that property. The annotation costs a flat 15 bytes; the
removed `required` entry saves only the length of the name plus quoting. Relaxing a parameter on a
RESIDENT tool therefore GROWS the payload measured by
`McpProfileGatingTests.RegisterEnabledPrimitives_ShouldKeepToolsSerializedSizeWithinBudget_WhenCalled`.
Issue #965 relaxed 13 resident parameters (`list-pages`, `get-page`, `get-entity-schema-properties`) and
the payload went from 32741 to 32773 bytes: +195 of annotation against −163 of `required`.

**Why it is this way** — the annotation is emitted by the SDK's schema generator, and the resident
surface is registered through `McpFeatureToggleFilter.RegisterEnabledPrimitives` →
`IMcpServerBuilder.WithTools(IEnumerable<Type>, JsonSerializerOptions)`. That overload accepts no
`McpServerToolCreateOptions`, so the `SchemaCreateOptions.TransformSchemaNode` hook that could strip a
null-valued `default` is unreachable from it. Threading the hook in means re-implementing the SDK's
per-method DI-factory registration inside `RegisterEnabledPrimitives`, and mirroring the same options in
`McpToolInvokerRegistry` so the two surfaces do not diverge — two places to keep in step, both under
`RequiresUnreferencedCode`. Measured saving if it were done: 56 occurrences, 840 bytes on the resident
set alone.

**What breaks if you ignore it** — the size ratchet fails with a difference of a few bytes and nothing in
the diff explains it: the change that tripped it looks like a pure removal from `required`. Issue #965
found the ceiling already at 27 bytes of headroom on master (32741 against 32*1024), i.e. saturated
before the change under review, and re-baselined it to 33*1024. Read the measured number out of the
failure message before assuming the branch caused the growth.
