---
description: a default named get-tool-contract lookup is FITTED to one inline reply (18 KB) and returns large contracts SHORT - a test or tool that needs the full text must pass detail=full, and a duty survives shortening only when its wording carries a SafetyDuty marker; widening the markers blows the budget
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
`detail: "short"` and `full-contract-bytes`. The contracts over the budget alone are the large
process-designer and business-rule ones (`create-`, `modify-`, `describe-business-process`,
`create-entity-business-rules`, `create-page-business-rules`, `update-entity-business-rules`), so their
default read is short (`create-business-process` 34 KB -> 3 KB); the set moves with every description
edit. `detail: "full"` returns everything; `detail: "short"` shortens everything.

Which text survives is decided by word patterns, not by structure, and the patterns are a compromise
MEASURED against the budget:

- A description sentence survives when it carries a `SafetyDuty` marker: "explicit yes/confirm", "ASK
  FIRST", "on your own initiative", "must not", "irreversible", "permanent", "destructive", "do not retry",
  "secret", "password", "as instructions", "not available to you", "tell/ask/warn the user" at the START of
  a clause, and "never" followed by a base-form verb from a fixed list ("never retry", "Never chain",
  "never point"). A matching FIELD description is kept whole, and fields use the wider `FieldDuty` net
  (any "never", "do not", "delet", "retry", "confirm", ...), because a field is short.
- A destructive tool whose description OPENS with its warning (a marker sentence starts inside the first
  500 characters) keeps a lead of up to 1 500 characters, so the unmarked sentences of that warning block
  stay too. Every other tool keeps a 500-character lead.
- Seven process contracts in one call fit the budget by a few dozen bytes. The short-form note is paid
  once per short contract, so lengthening the note, or any process tool's description, can push that
  reply over; a quoted value in the note costs twelve bytes more under the escaping encoder.
- A contract whose short form would be no smaller is left complete and unmarked, and a field label such
  as "Optional, default true." is kept in front of the field's first sentence, not instead of it.

Known gap, by decision: ordinary imperatives - "do NOT run compile-creatio", "Do NOT remove the flow and
add a plain one", "do not paraphrase" - are NOT markers and are dropped from the process contracts' short
forms. The same rules are in the process guidance (`core-rules`, the process articles) the agent reads
before building. The short form's note therefore says it kept the "safety sentences", not all rules.

**Why it is this way** — agent CLIs do not show a large tool result inline: Copilot CLI spilled every result
from 21.3 KB up in the CAADT transcripts, and the agent then spent 3-9 shell turns per process run grepping
the dumped contract back. Fitting (rather than "always short") leaves every small contract byte-identical
to before. Every widening was measured on the seven-contract request: any "never" took it to 32 KB (the
process reference text says "the runtime never writes", "this tool never reads" dozens of times), "and/,
never" to 24 KB, an emphatic "do NOT" to 21 KB. Each is the spill the short form exists to remove.

**What breaks if you ignore it** — a test that asserts a large contract's description, examples or field
text through a default lookup now asserts the SHORT form: it fails if it checks deep text, and worse, it
PASSES VACUOUSLY if it measures size, because it measures short forms. Pass
`ToolContractShortForm.FullDetail` whenever the full text is the subject. A new duty worded outside the
markers is silently dropped from a short form, and the pattern-driven test
`ShortLookup_Should_KeepEverySafetySentence_OfEveryTool` cannot see it, because it uses the same pattern:
add the duty to the hand-written `ShortLookup_Should_KeepTheReviewedDuty` cases, which fail on a miss.
Widening a marker, or lengthening a process tool's description, can push the seven-contract reply over
the budget, which `GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether`
reports.
