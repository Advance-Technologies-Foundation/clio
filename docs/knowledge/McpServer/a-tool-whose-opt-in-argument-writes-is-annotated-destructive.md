---
description: page-smoke is Destructive=true although its default call only reads - MCP annotations are per tool, not per call, and the opt-in click can press Delete or Save; the flag reroutes nothing (clio-run runs any tool), it changes the (D) index marker, the confirmation reason and what a host sees if the tool is ever made resident
applies-to:
  - clio/Command/McpServer/Tools/PageSmokeTool.cs
  - clio/Command/McpServer/Tools/ClioRunTool.cs
  - clio/Command/McpServer/McpDurableCallToolHandler.cs
ticket: ENG-103032
date: 2026-10-14
---

**What is true** — `page-smoke` reads a page by default, and only its opt-in `click` writes. It is still annotated
`Destructive=true`, like `trigger-smoke`, because `readOnlyHint`/`destructiveHint` describe the tool, not one call:
`click` presses an arbitrary button, Delete and Save included.

**Why it is this way** — while the tool is long-tail, a host never sees its own annotation: the call goes through
`clio-run`, which is itself `Destructive=true`, and the durable handler refuses any non-read-only tool with
confirmation-required. The flag therefore reroutes nothing (`clio-run` and the `clio-run-destructive` alias both
execute any tool), and the read-only call keeps its shape. What it does change is the signal an agent reads before
calling: the `destructive` field and the `(D)` marker ("it may delete or overwrite data") in the `get-tool-contract`
index, the "can overwrite or delete existing state" reason in a confirmation-required reply, and the annotation a
host would act on directly if the tool were ever made resident.

**What breaks if you ignore it** — with `Destructive=false` the index shows `page-smoke` without `(D)`, so an agent
that decides from the index whether to confirm with the user treats `click="Delete"` on a production record page as
harmless; and making the tool resident later would let a client that auto-approves non-destructive tools run that
click unasked.
