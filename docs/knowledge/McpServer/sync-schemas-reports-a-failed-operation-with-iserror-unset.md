---
description: a sync-schemas batch that fails on an operation still returns a typed SchemaSyncResponse with success:false and leaves the MCP CallToolResult.IsError unset, and the stage marker is pushed BEFORE each operation so a failed op 1 suppresses every later marker
applies-to:
  - clio/Command/McpServer/Tools/SchemaSyncTool.cs
  - clio.mcp.e2e/SchemaSyncToolE2ETests.cs
date: 2026-08-19
---

**What is true** — `SchemaSyncTool.ExecuteBatch` runs stop-on-first-failure (`ExecuteBatchOperation`
returning false `break`s the loop) and `ExecuteBatchOperation` pushes its
`"{i}/{total}: {op} {schema}"` stage marker through `ctx.ReportStage` **before** doing the work — except
when the operation is rejected up front by the argument-shape, `schema-name` or seed-row validation, which
returns earlier still and therefore emits NO marker at all, not even its own (GH-1303). A
business failure is not an MCP protocol failure: the tool still returns a well-formed
`SchemaSyncResponse` carrying `success:false`, and `CallToolResult.IsError` stays unset.

**Why it is this way** — the batch has per-operation results and no "in progress, poll" envelope, so
a partial batch has to come back as a payload the caller can read rather than as a transport error.
`IsError` is reserved for the binding layer (unparseable arguments, an unreachable environment).

**What breaks if you ignore it** — a test or an agent that treats `IsError` as the precondition and
then asserts on progress markers reports the wrong defect: the environment failed operation 1, every
later marker was therefore never emitted, and the failure surfaces as "the `2/2: create-lookup`
marker is missing", i.e. as a progress-path bug in clio. Assert `success` from the structured payload
before anything downstream of it, and dump the raw result so the failing operation names itself.
