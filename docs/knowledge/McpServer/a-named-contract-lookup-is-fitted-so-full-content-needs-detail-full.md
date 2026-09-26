---
description: a default named get-tool-contract lookup is FITTED to one inline reply (18 KB) and returns large contracts SHORT - a test or tool that needs the full text must pass detail=full, and a destructive tool's confirmation duty survives shortening only when worded "explicit yes"
applies-to:
  - clio/Command/McpServer/Tools/ToolContractShortForm.cs
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs
  - clio.tests/Command/McpServer/ToolContractShortFormTests.cs
  - clio.mcp.e2e/DeleteDataElementContractToolE2ETests.cs
ticket: ENG-100154
date: 2026-09-26
---

**What is true** — since ENG-100154 `get-tool-contract` with `tool-names` and no `detail` returns every
contract in full only while the serialized reply fits `ToolContractShortForm.InlineReplyBudgetBytes`
(18 KB); otherwise the LARGEST contracts are replaced, one at a time, by a short form marked
`detail: "short"` and `full-contract-bytes`. Measured on the default surface: every process-designer
write contract and four business-rule/page contracts are over the budget alone, so their default read is
short (`create-business-process` 34 KB -> 3 KB). `detail: "full"` returns everything; `detail: "short"`
shortens everything.

Two properties are carried by conventions the code relies on rather than enforces:

- A destructive tool's short form keeps its description's lead (up to 1 500 chars - the purpose plus the
  call-time safety warning the catalog puts second) AND every clause containing `explicit yes`, wherever it
  stands. That phrase is how the whole catalog words a confirmation duty; the deleteData "count, name the
  object, get an explicit yes" duty sits ~14 000 characters into `create-business-process` and survives only
  because of it.
- A non-destructive tool keeps only a 600-char lead, which is what lets seven process contracts fit together.

**Why it is this way** — agent CLIs do not show a large tool result inline: Copilot CLI spilled every result
from 21.3 KB up in the CAADT transcripts, and the agent then spent 3-9 shell turns per process run grepping
the dumped contract back. Fitting (rather than "always short") leaves every small contract - where examples
earn their place - byte-identical to before.

**What breaks if you ignore it** — a test that asserts a large contract's description, examples or field
text through a default lookup now asserts the SHORT form: it fails if it checks deep text, and worse, it
PASSES VACUOUSLY if it measures size - `GetToolContracts_ShouldKeepLargestContractWithinBudget_WhenEveryIndexedToolIsNamed`
stayed green while measuring short forms until it was given `detail=full`. Pass
`ToolContractShortForm.FullDetail` whenever the full text is the subject. And a new confirmation duty
written as "ask the user first" or "confirm with them" in a destructive tool's description is silently
dropped from its short form; word it "get an explicit yes", which
`DefaultLookup_Should_KeepEveryConfirmationDuty_OfEveryDestructiveTool` counts across the catalog.
