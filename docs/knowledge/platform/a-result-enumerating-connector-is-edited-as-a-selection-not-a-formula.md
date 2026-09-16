---
description: A conditional flow whose source enumerates activity results is edited in the designer as a CHECKBOX SELECTION (GV2) and never as a formula - a formula written there (CI3) runs, is invisible, and marks the connector invalid; write the selection with flows[].results or setFlowResults
applies-to:
  - clio/Command/ProcessModel/ProcessGraphValidator.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/
  - spec/eng-91853-gateways-and-flows/
ticket: ENG-91853
date: 2026-09-15
---

**What is true** — the designer picks the editor for a conditional flow's connector by **two** gates,
both in `CrtProcessDesigner/.../ConditionalSequenceFlowPropertiesPage.js` (7.8.0). When both pass it
shows a checkbox list headed `What is the result of an element "<name>"?` and **no formula field at
all**; that list reads and writes only `ProcessActivitiesSelectedResults` (`GV2`).

1. `loadEditModule` — the source must resolve to exactly ONE activity carrying a result parameter.
   `getPreviousProcessActivities` walks through ONE intervening gateway's non-conditional incoming
   flows, **one hop only**, so two chained gateways fall back to the formula field. But
   `getProcessActivityBySelectedResults` runs FIRST: a connector that already carries a selection is
   resolved from the UId stored *in* that selection, skipping the topology test entirely — re-routing
   the diagram cannot recover such a branch.
2. `onResultParameterValuesLoaded` — that activity's `getResultParameterAllValues()` must return a
   NON-EMPTY set. The base `ProcessFlowElementPropertiesPage` returns `{}`; nine pages override it.
   Approval is `VisaStatus IsFinal=true` and is therefore **never** empty. Perform task and the retired
   Call element read `ActivityCategoryResultEntry` by category — note `CallAsTask`
   (`03DF85BF-6B19-4DEA-8463-D5D49B80BB28`) DOES ship nine rows, seeded in
   `CrtBase/.../Data/**CallActivityCategoryResultEntry**/data.json`, a data package whose directory
   name differs from the table's, so a scan of `Data/ActivityCategoryResultEntry/` alone concludes the
   category is empty and is wrong. Preconfigured page is its buttons with `PerformClosePage=True`;
   Open edit page only with `resultsByColumn` enabled AND a column set. User dialog, Auto-generated
   page and Copilot's Execute intent enumerate results too.

clio and `CrtProcessBuilder` write only `ConditionExpression` (`CI3`). There is **no** operation that
writes `GV2` — it appears in the package solely in `ProcessDescriber` (read, as
`branchesOnActivityResult`) and in the two `ProcessGraphBuilder` refusals that fire when it is already
non-empty.

**Why it is this way** — the two slots are disjoint by design and the runtime picks one:
`ProcessSchemaConditionalFlow.CreateSequenceFlowElement` reads `GV2` first and only falls back to
`ExpressionText = ConditionExpression` when it is empty, after which `FlowConditionalGateway.Accept`
routes a non-empty `ExpressionText` to the expression evaluator instead of `CheckCondition`. Nothing
on the write path asks whether the flow's SOURCE enumerates results, so the formula is stored without
a word.

**What breaks if you ignore it** — the failure is silent in both directions, and the obvious probes
all come back clean:

- The process **runs correctly**. Severity is "unmaintainable", not "broken", so a runtime test passes.
- The schema **saves clean** until a human opens the connector's card. Element validation runs only
  through the element's own properties page (`BaseProcessSchemaElementPropertiesPage.js:621-624`
  writes `isValid`); the stamp defaults to `true` (`BaseProcessSchemaItem.cs:44`, meta key `BL11`) and
  clio never writes it false. From the first save after that open the designer raises `Required fields
  of some elements are not filled in` naming the connector. A green designer save is therefore not
  evidence a branch is finished.
- Do NOT read `condition` to tell the two apart. The slots are disjoint in practice as well as in
  principle: of the 344 conditional flows in the shipped 7.8.0 corpus that carry no formula, 337 are
  selection branches, and every conditional flow there carries a formula OR a result set, never both
  (the census is in `ProcessGraphValidator`). `branchesOnActivityResult` says THAT a selection decides
  the branch - `false` means "the selection is empty", not "a formula belongs here" - and `results`
  says WHICH results decide it.

**What closed it** - this record was written while clio could write only the formula, and that half is
no longer true. `CrtProcessBuilder` 1.6.2.18 writes the selection, and clio reaches it through
`flows[].results` on the build path and `setFlowResults` on the modify path, with `describe` reading it
back as `results` + `resultsActivity`. What did NOT change is everything above: the designer still
picks the editor the same way, and a formula there still runs invisibly - which is why writing one is
now REFUSED rather than left to the caller. The decisive evidence for that refusal is the designer's
own: Change type -> Conditional flow on such a connector offers the checkbox list and no formula
option, so nothing in the UI invites the state. It EXISTS anyway - the shipped corpus carries such
flows - so the guard is authoring-only and existing content still describes and re-saves. Two
independent scans of the corpus disagreed several-fold on how many, so no count is recorded here;
re-measure before quoting one. A gateway-sourced connector is handled too, keyed on the activity
behind the gateway, one hop. `Send email` is the exception: its server-side
schema declares results while its properties page shows a formula field, so it takes a condition and
the selection is refused there.

Measured on `UsrOrder_Handle` (Approval element `Approve order`) on a dev stand, 2026-09-15. The
shipped guidance was corrected in `clio-knowledge` PR #171; the manual suite is TC-01..TC-12 in the
ENG-91853 comment thread and in
`spec/eng-91853-gateways-and-flows/eng-91853-gateways-and-flows-activity-result-dialect-manual-test-cases.md`.
The read-side census of the same two slots is in
`docs/knowledge/ProcessModel/conditional-flow-condition-lives-in-two-places.md`.
