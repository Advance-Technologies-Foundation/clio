---
description: docs/McpCapabilityMap.md restates MCP tool attributes and descriptions by hand, is absent from AGENTS.md's required MCP targets, and no test pins it - so it silently starts lying when a tool changes
applies-to:
  - docs/McpCapabilityMap.md
  - clio/Command/McpServer/Tools/
date: 2026-08-19
---

**What is true** — `docs/McpCapabilityMap.md` spells out, per tool, the `ReadOnly` / `Destructive` /
`Idempotent` / `OpenWorld` flags, whether a restart happens, and what re-running does. Every one of
those statements is a hand-written copy of a tool attribute or `[Description]`. Nothing checks the
copy: there is no test over the file, and AGENTS.md's "Required MCP targets" list does not name it
(only `project-context.md` mentions it, as a reference).

**Why it is this way** — the file is prose written for agents, not a generated artifact, and it
lives outside `clio/Command/McpServer/**`, so a diff that changes a tool never touches it and no
reviewer is prompted to open it.

**What breaks if you ignore it** — the document keeps asserting the opposite of the code, to the one
audience that trusts it. Observed cases: `Destructive=false` documented where the attribute said
`true`; "needs no application restart" and "re-running does nothing" for a tool that restarts the
instance and pays a configuration build per run; "runs offline" for a tool whose `OpenWorld` had
flipped to `true`. When you change a tool attribute, a description, or a behaviour claim, grep this
file for the tool name in the same change — it is the only control there is.
