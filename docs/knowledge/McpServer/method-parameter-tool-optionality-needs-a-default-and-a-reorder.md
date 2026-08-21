---
description: making a method-parameter MCP tool argument optional needs both dropping [Required] and giving it a default, which forces it after the still-required parameters and silently rebinds positional call sites
applies-to:
  - clio/Command/McpServer/Tools/LinkFromRepositoryTool.cs
  - clio.tests/Command/McpServer/LinkFromRepositoryToolTests.cs
  - clio.tests/Command/McpServer/LinkFromRepositoryToolPassthroughTests.cs
ticket: ENG-93347
date: 2026-08-19
---

**What is true** — for a tool whose arguments are plain method parameters (not an argument record),
relaxing one argument takes two edits: remove `[Required]` and give the parameter a default value.
C# then requires the parameter to sit after every parameter that still has none, so the signature is
reordered. `LinkFromRepositoryTool.LinkFromRepositoryByEnvironment` shows the result:
`repoPath`, `packages` stay `[Required]`, and `environmentName = null` follows them.

**Why it is this way** — the schema generator reads only nullability and the presence of a default; a
parameter without a default stays in `required` however it is attributed. This is the
method-parameter counterpart of `emitted-schema-required-comes-from-the-record-stj-binds.md`, which
covers argument records - read both before relaxing anything.

**What breaks if you ignore it** — MCP and e2e callers pass named JSON and are unaffected, so the
protocol surface looks fine. The damage is in C# call sites: a positional call that used to read
`(environmentName, repoPath, packages)` still compiles after the reorder and now binds different
values to different parameters. The test asserts a wrong scenario while staying green. The fixtures
above are written with named arguments (`environmentName: "dev"`) for exactly this reason - keep any
new one that way.
