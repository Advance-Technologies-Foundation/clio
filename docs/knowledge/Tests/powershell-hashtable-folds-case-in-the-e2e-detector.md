---
description: a PowerShell @{} hashtable is case-insensitive, so the e2e detector's type-name maps must be ordinal or a local `command` becomes a reference to `Command`
applies-to:
  - .github/scripts/Select-McpE2eTestFilter.ps1
ticket: ENG-101587
date: 2026-09-29
---

**What is true** - in `Select-McpE2eTestFilter.ps1` every map keyed by a C# type name
(`TypeBody`, `TypeFiles`, `BaseList`, `Consumers`, `VerbsByType`, `TypesByFile`) is created with
`New-OrdinalMap`, not `@{}`. A PowerShell `@{}` compares keys case-insensitively; `HashSet[string]`
created with `New-Object` is already ordinal.

**Why it is this way** - the graph is built by looking every identifier token up in `TypeBody`. With
`@{}`, the local variable `command` matched the type `Command`, so 2651 type bodies became consumers
of `Command`; a comment reading `(this command is MCP-callable` then made three commands look like
extensions of it and handed them those consumers. `LocalizePageCommand` reached 237 of 237 tool
files through that path, and PR #1713 ran the whole suite.

**What breaks if you ignore it** - nothing fails. A new `@{}` keyed by type name silently adds
edges, the selection only widens, and pull requests go back to full runs. The synthetic test
`Script_ShouldMatchTypeNamesCaseSensitivelyAndOutsideComments` in
`clio.tests/McpE2eSelectionCoverageTests.cs` catches it for the `TypeBody` lookup only; the other maps
have no test, so review a new map for its comparer.
