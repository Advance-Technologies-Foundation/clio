---
description: the *-to-file twin of a read tool (execute-esq, get-component-info, get-request-info, list-entity-client-schemas, odata-read) is a separate [McpServerToolType] class; a twin method on the read tool's class would become resident and write-capable
applies-to:
  - clio/Command/McpServer/Tools/ExecuteEsqToFileTool.cs
  - clio/Command/McpServer/Tools/ComponentInfoToFileTool.cs
  - clio/Command/McpServer/Tools/RequestInfoToFileTool.cs
  - clio/Command/McpServer/Tools/ListEntityClientSchemasToFileTool.cs
  - clio/Command/McpServer/Tools/McpOutputFileWriter.cs
  - clio/Command/McpServer/McpCoreToolProfile.cs
ticket: ENG-101592
date: 2026-09-29
---

**What is true** — a read tool whose result can be too large for the model context gets a `*-to-file`
twin: `execute-esq-to-file`, `get-component-info-to-file`, `get-request-info-to-file`,
`list-entity-client-schemas-to-file`, next to the older `odata-read-to-file`. Each twin is its own
`[McpServerToolType]` class, takes the read tool's arguments plus `output-file`, and runs the read through
the read tool itself (`ExecuteEsqTool.Run`, `ComponentInfoTool.GetComponentInfo`, ...), so both refuse the
same input and return the same data. The file is created through `IMcpOutputFileWriter`: confined to the
workspace or the OS temp directory, resolved before the remote call, never overwriting an existing file.

**Why it is this way** — two properties are static per tool type, not per call. The MCP safety annotations
(`ReadOnly` / `Idempotent`; see [odata-file-mode-is-a-separate-tool.md](odata-file-mode-is-a-separate-tool.md))
would make the read tool write-capable for every ordinary call. And residency: `McpCoreToolProfile.CoreToolTypes`
lists CLASSES, so every `[McpServerTool]` method on `ComponentInfoTool` or `RequestInfoTool` (both resident)
is sent in every session's `tools/list`, which `McpProfileGatingTests` holds to a byte budget. A twin as a
separate class stays long-tail and costs only its compact-index entry.

`execute-esq-to-file` reads the whole DataService response into memory before it checks the
`ODataFileContract.MaxResponseBytes` ceiling: `IApplicationClient.ExecutePostRequest` has no bounded
variant, unlike the streamed GET `odata-read-to-file` uses. The ceiling limits what is written, not what
is read.

**What breaks if you ignore it** — adding the twin as a second method on the read tool's class passes every
unit test of the twin and silently puts it into `tools/list` of every session; `McpProfileGatingTests` then
fails on bytes, or, if its ceiling is raised in passing, every session pays for a tool it rarely calls.
