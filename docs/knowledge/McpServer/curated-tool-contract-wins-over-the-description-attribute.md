---
description: a handwritten ToolContractGetTool.Contracts entry takes precedence over a tool's [Description] attribute, and for a non-resident tool that curated string is the only description an agent ever reads
applies-to:
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio/Command/McpServer/McpCoreToolProfile.cs
ticket: ENG-94385
date: 2026-08-19
---

**What is true** — `ToolContractGetTool.TryResolveFullContract` resolves a tool's contract through a
cascade: a handwritten `Contracts` entry first, then a registry-schema-derived contract, then the
reflection catalog. The `[Description]` attribute on the tool method is **not merged in**. For a
tool that is not in `McpCoreToolProfile` (non-resident, so absent from `tools/list` and invoked via
`clio-run` / `get-tool-contract`), the curated string is the entire description the agent receives.

**Why it is this way** — the curated contracts exist precisely to say more than an attribute can
carry, and merging both would produce two versions of the same claim that drift apart. The
precedence is intentional; the invisibility of the attribute is the side effect.

**What breaks if you ignore it** — editing the `[Description]` attribute alone ships nothing. The
agent keeps reading the stale curated text, and the more authoritative-looking string is the one with
no effect. This has already produced the worst version of the failure: a curated contract asserting
"it ALWAYS installs - there is no skip" while the command had grown a refusal, so agents were told a
behaviour the code no longer had. Verifying against the attribute reproduces the mistake. When you
change a tool's behaviour, change the curated entry, and pin the agent-facing claim with a unit
assertion over the resolved `contract.Description` - the E2E is advisory and cannot fail a merge.
