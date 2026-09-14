---
description: the first sentence of a tool's [Description] is what an agent sees in the get-tool-contract compact index, so a description opening with a safety warning advertises the warning instead of the tool
applies-to:
  - clio/Command/McpServer/Tools/
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio.tests/Command/McpServer/ToolContractGetToolTests.cs
  - clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs
  - clio.mcp.e2e/ToolContractGetToolE2ETests.cs
ticket: ENG-96389
date: 2026-09-13
---

**What is true** — `ToolContractCatalog.BuildPurpose` distils the compact-index entry for a tool from
the **first sentence** of its description, capped at 120 characters. The compact index is the only
discovery surface a non-resident tool has: it is absent from `tools/list`, so an agent looking for a
capability reads these one-liners and nothing else. A description that opens with a safety warning
therefore advertises the warning, not the tool.

Two rules govern where the sentence ends. A period counts only when followed by whitespace, so `en-US`
and version numbers are safe by construction. Abbreviations whose OWN period is followed by whitespace
are not, and are listed in `SentenceSafeAbbreviations` — membership requires that the abbreviation is
never sentence-FINAL in English, because a wrong skip merges two sentences and pulls the next one
(often the safety warning) into the one-liner. `etc.` is excluded for exactly that reason and the
exclusion is deliberate: its mid-sentence forms `etc.,` / `etc.)` are already handled by the
whitespace rule, so the only form that would reach the list is the ambiguous one.

**Why it is this way** — the index exists to let an agent see WHAT tools exist without paying for the
full contract, so it has to be short, and the first sentence is the only part of a free-text
description a machine can take as a summary. Nothing in the attribute says the first sentence is
load-bearing, and nothing near the warning-writing site hints that position matters.

**What breaks if you ignore it** — putting a warning first does not make it more prominent; it makes
the tool invisible. The warning is still delivered on the full contract, which is what an agent reads
before calling, so moving it to second position costs nothing and restores discovery. The failure is
silent in both directions: nothing used to cover the purpose, the tool still worked when called
directly by name, and the only symptom was an agent that did not find the tool — or picked the wrong
one of two identical lines and paid ~7 700 tokens for the wrong 30 000-character contract.

ENG-96389 repaired five one-liners. `create-business-process` and `modify-business-process` both
opened with the same warning and produced a BYTE-IDENTICAL purpose ("BEFORE CALLING with an
accessRights block: …"); each now leads with what the tool does. `validate-process-graph` and
`get-user-culture` were cut mid-example at `e.g.`, which is what the abbreviation list above was added
for. `clear-redis-db-by-environment` and `clear-redis-db-by-credentials` shared one line and differ
precisely in the thing it did not mention — how the target is identified; that pair surfaced only once
the uniqueness guard existed.

**The audit that found them had a blind spot worth repeating.** It read the `[Description]` attribute
straight from source and applied the distillation to it — which is right only for an UNCURATED tool.
On that basis `compile-creatio` was reported defective and its attribute was edited; in fact it has a
curated contract, so its index line was already "Recompiles a registered Creatio environment and forces
a runtime reload." and the edit changed nothing observable. Review caught it. Measure the SERVED
purpose (`GetToolContracts().Index`), never the attribute, or the curated tools lie to you.

The property is now enforced, not merely recorded: `ToolContractGetToolTests` drives `BuildPurpose`
directly over synthetic input (each abbreviation, the whole-token guard, case, start- and
end-of-text), pins the two repaired production purposes, and asserts every advertised purpose is
pairwise unique; `clio.mcp.e2e/ToolContractGetToolE2ETests` re-asserts the survival and the
uniqueness over the real stdio server. `BuildPurpose` is `internal` rather than `private` for that
first reason — routing the rules through production prose would let a reword silently delete the
coverage.

Related: `ToolContractPayloadBudgetTests` ratchets the SIZE of these payloads. It bounds growth and
says nothing about whether a purpose reads correctly — a different guard for a different failure. See
also [[curated-tool-contract-wins-over-the-description-attribute]]: for a tool that HAS a curated
contract the index one-liner comes from the curated string, and editing the attribute changes nothing.
