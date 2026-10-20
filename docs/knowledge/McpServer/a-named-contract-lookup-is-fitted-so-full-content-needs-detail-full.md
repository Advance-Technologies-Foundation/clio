---
description: a default named get-tool-contract lookup is FITTED to one inline reply (18 KB) and returns large contracts SHORT - a test or tool that needs the full text must pass detail=full, and a duty survives shortening only when its wording carries a SafetyDuty marker; widening the markers blows the budget
applies-to:
  - clio/Command/McpServer/Tools/ToolContractShortForm.cs
  - clio/Command/McpServer/Tools/ToolContractGetTool.cs
  - clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs
  - clio.tests/Command/McpServer/ToolContractShortFormTests.cs
  - clio.mcp.e2e/DeleteDataElementContractToolE2ETests.cs
  - clio.mcp.e2e/ToolContractGetToolE2ETests.cs
  - clio.tests/Common/BundledProcessBuilderSource.cs
ticket: ENG-100154
date: 2026-09-26
---

**What is true** — since ENG-100154 `get-tool-contract` with `tool-names` and no `detail` returns every
contract in full only while the serialized reply fits `ToolContractShortForm.InlineReplyBudgetBytes`
(18 KB); otherwise the LARGEST contracts are replaced, one at a time, by a short form marked
`detail: "short"` and `full-contract-bytes`. The contracts over the budget alone are the large
process-designer and business-rule ones (`create-`, `modify-`, `describe-business-process`,
`create-entity-business-rules`, `create-page-business-rules`, `update-entity-business-rules`), so their
default read is short (`create-business-process` 34 KB -> 4 KB); the set moves with every description
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
- Before cutting any text, a reply over the budget carries each repeated set of error codes once - later
  contracts get `{codes: [], same-as: <first>}`. Most contracts share the same generic codes, so for the
  seven process contracts this alone is about 1.5 KB, and fitting spends it keeping small contracts whole
  where the reply allows. In the seven-contract bundle it does not: that reply was the FLOOR (every
  contract short, codes shared) before ENG-102797 too, reaching the budget only with the last contract,
  and since the call shapes below the floor itself is over Fit's budget. The short-form note is paid once
  per short contract; a quoted value in it costs twelve bytes more under the escaping encoder.
- A contract whose short form would be no smaller is left complete and unmarked, and a field label such
  as "Optional, default true." is kept in front of the field's first sentence, not instead of it.
- The short `create-business-process` and `modify-business-process` contracts also carry a `SHAPE:` block
  (`ToolContractShortForm.CallShapes`, ENG-102797), about 0.9 KB each and names-and-nesting only: the
  descriptor fields and element blocks, and every modify operation with its fields. It is appended after
  the kept lead and safety sentences, so fitting never cuts it. With both shapes the seven-contract bundle
  stays over 18 432 bytes as Fit measures it (default encoder) even all short, so Fit returns all seven
  short - as before - about 1.2 KB over its own budget. That is a trade accepted on purpose: no contract
  loses text it kept before, every short form keeps its safety duties, and the shapes cut the
  `detail=full` fetch of the 33-134 KB full contracts, which always spilled (ENG-99970 A/B: 5 of 6 runs
  without the shapes, 7 of 14 with them). On the wire the same reply is under 19 000 bytes,
  the inline limit the release gate is budgeted against, and that is what
  `GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether` and its e2e twin
  measure (`McpResultSize`); Fit itself still budgets with the default encoder.
  `GetToolContracts_ShouldReturnEverySevenProcessDesignerContractShort_WhenTheCallShapesOutgrowFitsBudget`
  pins the all-short outcome and the over-budget floor, so a change to either is a visible decision. The
  shape's lists are held to the args records and to the bundled CrtProcessBuilder's `Operations.All`,
  `BuildProcessRequest`, `ProcessElementDescriptor`, `ProcessFlowDescriptor`,
  `ProcessParameterDescriptor`, `ProcessMappingDescriptor` and `ProcessOperationDescriptor` by
  `ToolContractShortFormTests`, so a new server operation, field or element block fails the unit lane until
  it is shaped.

Known gap, by decision: ordinary imperatives - "do NOT run compile-creatio", "Do NOT remove the flow and
add a plain one", "do not paraphrase" - are NOT markers and are dropped from the process contracts' short
forms. The same rules are in the process guidance (`core-rules`, the process articles) the agent reads
before building. The short form's note therefore says it kept the "marked sentences", not all rules.

**Why it is this way** — agent CLIs do not show a large tool result inline: Copilot CLI spilled every result
from 21.3 KB up in the CAADT transcripts ENG-100154 read, and the agent then spent 3-9 shell turns per
process run grepping the dumped contract back. Release build 16156272 and the ENG-99970 A/B on creatio_3
(October 2026) narrowed that limit to results of up to ~19.8 KB inline and ~20.7 KB and over spilled.
Fitting (rather than "always short") leaves every small contract byte-identical to before wherever the reply
can fit; the seven-contract bundle cannot, and comes back all short, as above. Every widening was measured on the seven-contract request: any "never" took it to 32 KB (the
process reference text says "the runtime never writes", "this tool never reads" dozens of times), "and/,
never" to 24 KB, an emphatic "do NOT" to 21 KB. Each is the spill the short form exists to remove.

**What breaks if you ignore it** — a test that asserts a large contract's description, examples or field
text through a default lookup now asserts the SHORT form: it fails if it checks deep text, and worse, it
PASSES VACUOUSLY if it measures size, because it measures short forms. Pass
`ToolContractShortForm.FullDetail` whenever the full text is the subject. A new duty worded outside the
markers is silently dropped from a short form, and the pattern-driven test
`ShortLookup_Should_KeepEverySafetySentence_OfEveryTool` cannot see it, because it uses the same pattern:
add the duty to the hand-written `ShortLookup_Should_KeepTheReviewedDuty` cases, which fail on a miss.
Widening a marker, lengthening a process tool's description or a call shape can push the seven-contract
reply over 19 000 bytes on the wire, which
`GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether` reports;
a change that keeps one of the seven whole, or brings their floor back under Fit's budget, is reported by
`GetToolContracts_ShouldReturnEverySevenProcessDesignerContractShort_WhenTheCallShapesOutgrowFitsBudget`,
so the trade is re-decided rather than drifting.

Both budgets are nearly spent. Measured on master after ENG-96506 (2026-10-20, PR #1847 "Statements"): the
full `modify-business-process` contract is 35 016 bytes, 56 under the 35 072-byte per-contract ceiling in
`ToolContractPayloadBudgetTests`, and the seven-contract reply 18 815 bytes, 185 under 19 000 on the wire.
The next sentence on that tool pays by a swap, as the ceiling's comment
records for earlier tickets, and the order to cut in is: provenance first (version floors, history, which
archive a rule arrived in), then text a process guide already owns (per-element reference, the detail of a
refusal message). Never cut a sentence carrying a `SafetyDuty` marker, a refusal the caller has to act on, or
a consent flag such as `confirm-layout-change`. An unmarked sentence past the kept lead never reaches a short
form, so it costs the per-contract ceiling only; the lead, a marked sentence or a field description costs the
seven-contract reply as well.
