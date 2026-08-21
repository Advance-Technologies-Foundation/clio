---
description: a warning written only with _logger.WriteWarning is not part of an MCP tool's response - BaseTool.WithCleanLog calls logger.ClearMessages() in its finally, so gaps must travel in the response object
applies-to:
  - clio/Command/McpServer/Tools/BaseTool.cs
  - clio/Common/ConsoleLogger.cs
  - clio/Command/GetClassicPageSourcesCommand.cs
date: 2026-08-19
---

**What is true** — inside an MCP tool, log lines exist only for the duration of the executor.
`BaseTool.WithCleanLog` sets `logger.PreserveMessages = true`, runs the executor, and then calls
`logger.ClearMessages()` in its `finally`. The response object the executor returned is serialized
after that, so anything a command wrote only to the logger is already gone. A tool that genuinely
wants log text has to harvest `logger.LogMessages` *inside* the scope and copy it into its result -
`CompileCreatioTool`, `SchemaSyncTool` and `EntitySchemaTool` all do exactly that.

**Why it is this way** — the capture buffer is `AsyncLocal` per flow (FR-06) so concurrent tool
invocations from different tenants cannot read each other's lines. A per-flow buffer that is not
reset would leak the previous call's output, so the scope both establishes and tears down the
buffer; the teardown is unconditional.

**What breaks if you ignore it** — a command that reports a completeness gap with
`_logger.WriteWarning` looks correct in CLI use and is silent through MCP. The agent then reads a
technically successful result with a missing part and no signal, which is exactly how
`sectionLayerCount:0` became indistinguishable from "this entity has no section". Non-fatal gaps
belong in a `Warnings` field on the response, as `GetClassicPageSourcesCommand` does.
