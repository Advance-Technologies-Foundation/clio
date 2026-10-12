---
description: get-tool-contract with no names returns text lines under tool-index by default; the per-tool objects stay under index and come back only for detail=index-entries - turning index itself into strings empties ClioRing's catalog
applies-to:
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio-ring/ClioRing.Ipc/ClioIpcClient.cs
  - clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs
  - clio.mcp.e2e/Support/Mcp/McpServerSession.cs
ticket: ENG-102796
date: 2026-10-12
---

**What is true** — a no-names `get-tool-contract` call answers with `legend` plus `tool-index`: one string
per tool, `name (R)(D) [aka old-name]: purpose`, the purpose cut to 48 characters. The object form
(`name`, `purpose`, `contract-available`, `resident`, `destructive`, `aliases`) is still served, under the
same `index` key and in the same shape as before, but only for `detail=index-entries`. ClioRing sends only
that request; the e2e helpers `GetToolContractIndexAsync` / `ListReachableToolNamesAsync` ask for it too. A
clio from before the change ignores the unknown detail value (its no-names branch returns the object index for
any detail other than `full`) and returns the objects, so one request covers both generations.

**Why it is this way** — the object index was ~47 KB for ~230 tools. Copilot CLI inlines a tool result up
to ~19.8 KB and spills anything from ~20.7 KB to a file the agent reads back in pieces; CAADT release build
16156272 did that 15 times, and every session makes this call. The lines are ~16.9 KB on the wire. The
A/B prototype put the strings under `index` itself; `ClioIpcClient.ParseCatalog` skips every array item
that is not an object, so a Ring built from the same sources received an empty catalog with no error. The
strings got a new key so the object shape, and the Ring parser, stay as they were. The change is still NOT
additive for a Ring built before it: that Ring sends `{}` and `{"args":{}}`, both now return lines only, and
it shows the "incompatible clio" state. AGENTS.md asks for a transition for a released Ring; none was added
because no Ring was ever released - no `ring/v*` tag and no `ring-latest` release on the clio repository,
and the clio-ring README calls it an internal preview (checked 2026-10-12). Once a Ring ships, a change like
this one needs that transition.
The legend says "(R) = resident, resident=true in detail=index-entries" verbatim because core-rules (in
clio-knowledge) tells agents resident tools are the `resident=true` entries of this index, and the CAADT
toolkit mirrors that sentence word for word in a repository released on its own train.

**What breaks if you ignore it** — moving the strings back under `index`, or dropping `index-entries`,
empties the Ring catalog silently (it shows the "incompatible clio" state, not an error) and turns every
e2e assertion on `resident` / `destructive` into a lookup over an empty list. Reading flags out of the
lines instead couples a program to wording meant for an agent. Raising `MaxToolIndexSerializedBytes` in
`ToolContractPayloadBudgetTests` past 19 000 brings the read-back back; shorten the purpose cut or the
legend instead. Rewording the legend's resident sentence makes the mirrored core-rules line false.
