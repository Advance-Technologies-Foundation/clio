---
description: every MCP tool declares [McpToolFacts] (effect read/change/destroy, environment impact, scope local/environment/machine, connections none/read/change) plus [McpToolArgumentFacts] for argument values that narrow them; a host reads them from tools/list _meta "clio/facts" and from get-tool-contract detail=facts, never from the tool's name; the index, index-entries and contracts deliberately carry none
applies-to:
  - clio/Command/McpServer/McpToolFacts.cs
  - clio/Command/McpServer/McpToolFactsReader.cs
  - clio/Command/McpServer/McpToolFactsListing.cs
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio.tests/Command/McpServer/McpToolFactsCoverageTests.cs
date: 2026-11-14
---

**What is true** — each MCP tool method carries `[McpToolFacts]`: what the call does at most (`Effect`: `Read`,
`Change`, `Destroy`), whether it builds or restarts the environment (`EnvironmentImpact`), the widest place it
reaches (`Scope`: `Local`, `Environment`, `Machine`) and whether it shows or changes clio's registered environments
and credentials (`Connections`). `[McpToolArgumentFacts(argument, value)]` narrows them for one argument value:
`manage-role` with `action=list` is a `Read`. A host reads them in two places: a resident tool's
`_meta["clio/facts"]` in `tools/list`, and `get-tool-contract` with `detail=facts` for every tool, the long tail
behind `clio-run` and the deprecated aliases included.

**Why it is this way** — a host that runs clio for an agent lets a call through, asks the person or refuses it.
`(D)` and `destructiveHint` could not tell `create-app` from `delete-app`, said nothing about a compile, a
machine-wide install or clio's connections, and a composite tool is one name for a read and a delete. So hosts kept
regexes on clio's verbs and lists of tool names, and each clio release made them wrong (an AI app-building run on
14 Nov 2026 was refused `create-dynamic-case`, and `manage-role list` raised a change approval).

Two exclusions are deliberate. Writing a file the call names is not an effect (a `get-...-to-file` tool is a
`Read`; which arguments name a host file is a separate per-argument fact), and neither are clio's own caches. And
the facts are not on the line index, the `index-entries` objects or the contracts: those are budgeted to the byte
(`ToolContractPayloadBudgetTests`) and ClioRing parses `index-entries`. A host asks once with `detail=facts`.

**What breaks if you ignore it** — a new tool without `[McpToolFacts]`, with a field left out, with facts its
`ReadOnly` / `Destructive` contradict, or with an argument entry wider than the tool fails
`McpToolFactsCoverageTests`. Declaring the facts from the tool's name instead of its code brings back exactly what
the hosts had: `create-app` and `create-lookup` are a `Change` that rebuilds the environment, `execute-sql-script`
is a `Destroy` even for a SELECT, `get-page` is a `Read` that writes only clio's own baseline, and
`update-client-unit-schema` is not a machine-wide `update-cli`. Moving the facts into the index would move every
payload budget and the shape ClioRing reads. Design: `spec/adr/adr-mcp-tool-facts.md`.
