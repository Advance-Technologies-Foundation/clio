---
description: output-file lives in odata-read-to-file, not on odata-read; a read tool that can write loses raw-name compatibility and the bounded retry-safe read routing
applies-to:
  - clio/Command/McpServer/Tools/ODataReadTool.cs
  - clio/Command/McpServer/Tools/ODataReadToFileTool.cs
  - clio/Command/McpServer/Tools/ODataReadQuery.cs
ticket: "1221"
date: 2026-09-01
---

**What is true** — the file destination for an OData query is its own MCP tool, `odata-read-to-file`.
`odata-read` takes no `output-file` and is annotated `ReadOnly = true, Idempotent = true`. The query
itself is not duplicated: both tools validate, build and parse through `ODataReadQuery`.

**Why it is this way** — the MCP safety annotations (`ReadOnly` / `Idempotent`) and the durable read
routing (`McpReadResponseDeadline`) are STATIC PER TOOL, not per call. An optional `output-file`
argument therefore forces the whole tool to be declared write-capable, for every ordinary query that
mutates nothing. Making routing argument-aware was considered and rejected: it would mean teaching the
routing layer to read arguments, for one tool.

**What breaks if you ignore it** — reintroducing `output-file` on `odata-read` (or annotating it
`ReadOnly = false`) makes every plain query lose two things at once: raw-name compatibility, and the
bounded retry-safe read semantics the read-deadline pipeline only applies to a `ReadOnly` tool. The
symptom seen on the branch that tried it was a canonical `odata-read` call with no `output-file`
answering `confirmation-required`. `DurableInvocationGateCompletenessTests` carries `odata-read` in its
reviewed silently-executable baseline and will fail if the annotation moves.
