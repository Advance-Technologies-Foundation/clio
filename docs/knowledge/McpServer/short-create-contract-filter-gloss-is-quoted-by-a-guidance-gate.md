---
description: the filter? gloss in the short create-business-process call shape, "any of several lookup records is ONE condition with values", is quoted word for word by the clio-knowledge process-data-source-filters gate for values - reword it and agents pay a detail=full read or fall back to an or group, while clio fails nothing but its two pins
applies-to:
  - clio/Command/McpServer/Tools/ToolContractShortForm.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio.tests/Command/McpServer/ToolContractShortFormTests.cs
  - clio.mcp.e2e/ToolContractGetToolE2ETests.cs
ticket: ENG-105557
date: 2026-11-16
---

**What is true** - the sentence "any of several lookup records is ONE condition with values" stands twice in
clio: in the full `create-business-process` description (`CreateBusinessProcessTool`) and as the gloss on
`filter?` in `ToolContractShortForm.CreateElementShape`, which every short create contract carries. The
`process-data-source-filters` guide in clio-knowledge quotes it as its gate: an agent sends `values` only when
the contract it read says that sentence, and a silent short form sends it to `detail=full` first. Released
clio 8.1.0.150 to 8.1.0.152 say it in the full form only.

**Why it is this way** - agents read the default short form and seldom ask for `detail=full`, so the gate
needs a marker the short form carries. A sentence of the contract itself is the one marker no release or
CrtProcessBuilder number can overtake, which is why the guide gates on it and not on a version.

**What breaks if you ignore it** - the guide lives in another repository, so rewording the sentence fails
nothing in clio except `Lookup_Should_SayTheValuesGateSentence_InBothFormsOfCreateBusinessProcess` and its e2e
twin `GetToolContracts_ShouldSayTheValuesGateSentence_InTheShortCreateContract`. Reworded in the gloss, every
agent pays a `detail=full` read (about 35 KB, spilled to a file) before a filter over several lookup records.
Reworded in both places, agents build such a filter as an `or` group of `equal` conditions: the right records,
but one designer row per value instead of one condition, as a dev-testing run on 2026-11-15 did while the short
form was silent. Change the wording only together with the guide's gate.
