---
description: DataForgeReadClient throws InvalidOperationException when the DataForgeSchemaReadService payload has Success=false - the throw is the contract DataForgeTool and DataForgeContextService catch, do not convert it to a structured failure
applies-to:
  - clio/Common/DataForge/DataForgeReadClient.cs
  - clio/Common/DataForge/DataForgeContextService.cs
  - clio/Command/McpServer/Tools/DataForgeTool.cs
ticket: ENG-92147
date: 2026-08-19
---

**What is true** — all three read methods on `DataForgeReadClient` (`FindSimilarTables`,
`FindSimilarLookups`, `GetTableRelationships`) throw `InvalidOperationException` carrying the server
message when the envelope reports `Success = false`. That looks like a design smell for a client
whose transport already models failure, but every consumer depends on it: `DataForgeTool` wraps each
call in try/catch and turns the exception into a structured `Success = false` MCP response, and
`DataForgeContextService` catches per term and downgrades the single failed term into a warning while
the rest of the context is still built.

**Why it is this way** — the per-term graceful degradation in `DataForgeContextService` is what needs
the throw. With a "return a structured failure" signature every call site would have to branch on the
result, and the service's loop would have to be rewritten to distinguish "no matches" from "the read
failed" by inspecting a status field instead of a catch.

**What breaks if you ignore it** — widening the return type to a result envelope is not a local
change: it silently turns each failed term into an empty match list, so `dataforge-context` starts
reporting a clean context with terms quietly missing instead of a warning naming the cause. If the
throw ever does have to go, `DataForgeContextService` has to be rewritten in the same change, and the
pin is `DataForgeReadClientTests.FindSimilarTables_Should_Throw_When_Proxy_Returns_Error`.
