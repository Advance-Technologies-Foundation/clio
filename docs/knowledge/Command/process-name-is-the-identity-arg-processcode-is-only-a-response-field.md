---
description: process-name is the single identity argument across get-process-signature / describe- / modify-business-process, while processCode survives legitimately as a response field and as McpE2E Sandbox.ProcessCode - do not sweep the two together
applies-to:
  - clio/Command/GetProcessSignatureCommand.cs
  - clio/Command/DescribeProcessCommand.cs
  - clio.mcp.e2e/appsettings.example.json
ticket: ENG-90883
date: 2026-08-19
---

**What is true** — a process schema's identifier is its `Name`, and every process-designer surface
takes it under that name: `GetProcessSignatureCommand` accepts it as the positional
`[Value(0, MetaName = "ProcessName")]`, and the describe / modify MCP tools take `process-name`.
The count of `process-code` identity arguments in the repository is zero. Two `processCode`
spellings remain and are correct: the `processCode` field of the **response** `get-process-signature`
returns, which shipped guidance reads, and `McpE2E:Sandbox:ProcessCode` in the e2e harness
configuration (`clio.mcp.e2e/appsettings.example.json`).

**Why it is this way** — `get-process-signature` is shipped, so its positional argument is a public
contract that cannot be renamed. Independently, "code" is a label clio would be inventing: the
platform has no process code, only a schema `Name`, and the surrounding vocabulary (element `name`,
`process-name`) was aligned on the platform term deliberately.

**What breaks if you ignore it** — a rename sweep driven by a grep for `processCode` looks like
leftover inconsistency and is tempting to "finish". Renaming the response field breaks the shipped
signature contract and the guidance that reads it; renaming the harness key silently detaches the e2e
settings from the fixtures, which then skip or fail on a blank process. Conversely, reintroducing
`process-code` as an argument breaks callers of the shipped verb.
