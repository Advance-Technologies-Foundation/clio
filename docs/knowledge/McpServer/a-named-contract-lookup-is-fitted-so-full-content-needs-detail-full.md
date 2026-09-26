---
description: a default named get-tool-contract lookup is FITTED to one inline reply (18 KB) and returns large contracts SHORT - a test or tool that needs the full text must pass detail=full, and a safety duty survives shortening only when its wording matches the SafetyDuty pattern
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

Which text survives is decided by a pattern, not by structure:

- Every sentence matching `ToolContractShortForm.SafetyDuty` is kept, in the description AND in field
  descriptions (a matching field description is kept whole), for EVERY tool. The pattern covers the ways
  the catalog words a duty today: "explicit yes", "explicitly confirms", "ASK FIRST", "on your own
  initiative", "Never chain", "permanent", "irreversible", "destructive", "do not retry" and a few more.
- A destructive tool whose description OPENS with its warning (a matching sentence starts inside the
  first 600 characters) keeps a lead of up to 1 500 characters, so the rest of that warning block - which
  has sentences with no marker, such as "An ABSENT filter is the WIDE state ... nothing warns you" - stays
  too. Every other tool keeps a 600-character lead. That split is what lets the seven process contracts
  fit one reply together (18.3 KB of the 18 KB budget, measured on 2026-09-26: there is little headroom).

**Why it is this way** — agent CLIs do not show a large tool result inline: Copilot CLI spilled every result
from 21.3 KB up in the CAADT transcripts, and the agent then spent 3-9 shell turns per process run grepping
the dumped contract back. Fitting (rather than "always short") leaves every small contract - where examples
earn their place - byte-identical to before. The first short form kept only clauses worded "explicit yes"
and lost the ask-first rule of `set-active-business-process-version`, the never-chain and permanence rules
of `modify-business-process-as-new-version`, and the confirmation duty in `update-page`'s `force` field.

**What breaks if you ignore it** — a test that asserts a large contract's description, examples or field
text through a default lookup now asserts the SHORT form: it fails if it checks deep text, and worse, it
PASSES VACUOUSLY if it measures size - `GetToolContracts_ShouldKeepLargestContractWithinBudget_WhenEveryIndexedToolIsNamed`
stayed green while measuring short forms until it was given `detail=full`. Pass
`ToolContractShortForm.FullDetail` whenever the full text is the subject. A new duty worded outside the
pattern ("check with them before", "make sure they want it") is silently dropped from the short form, and
`ShortLookup_Should_KeepEverySafetySentence_OfEveryTool` cannot see it, because that test uses the same
pattern: word the duty with one of the phrases above, or add the new wording to `SafetyDuty`. Adding a
long description to any process tool can push the seven-contract reply over the budget, which
`GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether` reports.
