---
description: 132 of 243 CLI verbs are published as an MCP tool of the same name, which is the only link between a command class and its e2e coverage
applies-to:
  - clio/Command/McpServer/Tools/
  - .github/scripts/Select-McpE2eTestFilter.ps1
ticket: GH-1570
date: 2026-09-17
---

**What is true** - an MCP tool is usually the MCP surface of a CLI command, and the two carry the
same name: `[Verb("create-app")]` on `ApplicationCreateCommand` and
`ApplicationCreateToolName = "create-app"` in `ApplicationTool`. Measured 2026-09-17: 132 of 243
verbs are also MCP tool names. Nothing in the source states the correspondence - there is no shared
constant, no attribute and no registry entry linking the command class to its tool.

**Why it is this way** - the tool dispatches through the command resolver by verb name, so the name
is the contract. An e2e fixture references the tool through the tool class
(`ApplicationTool.ApplicationCreateToolName`), never through the command class, so no text anywhere
connects `ApplicationCreateCommand` to the tests that exercise it.

**What breaks if you ignore it** - any analysis that walks references concludes a command class is
uncovered. The pull-request e2e detector did exactly that before this was found: a change to
`ApplicationCreateCommand` resolved to "no fixture can observe this" and queued no build, while
`ApplicationToolE2ETests` and `ApplicationSectionToolE2ETests` were the tests that would have caught
a regression. `Select-McpE2eTestFilter.ps1` now maps a verb to the tool published under that name.
