---
description: a Change access rights element with add and remove both empty builds green and then changes no permissions - it has no output parameters, so nothing reports it; a record filter that is PRESENT but conditionless is the same runtime no-op but is REFUSED at build by a current package; an element with NO record filter at all is the opposite and far worse hazard - it acts on EVERY record of the object
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
  - clio/Command/ProcessModel/AccessRightsBlockExpectation.cs
  - clio.tests/Command/ProcessModel/AccessRightsBlockExpectationTests.cs
  - docs/McpCapabilityMap.md
ticket: ENG-92717
date: 2026-09-01
---

**What is true** — the Change access rights element (`changeAccessRights` /
`ChangeAdminRightsUserTask`) has **no output parameters**. Nothing downstream can branch on whether
permissions changed, and the runtime writes no error or log an agent can read back. Its runtime
silently does nothing in three cases, TWO of which are refused when the process is built:

| Configuration | Refused at build? |
|---|---|
| target object does not use record permissions (`AdministratedByRecords` off) | **yes** |
| the record `filter` is PRESENT but carries no conditions | yes (package refusal; a designer-built element can still be in this state) |
| `add` and `remove` are both empty (a block carrying only `object` is one) | no |

So a successful build is **not** evidence the element will do anything. The dangerous direction is a
REVOKE: a revocation that silently does nothing leaves privileges in place while every signal the
caller can see reports success.

The opposite state is NOT in that table and is far more dangerous: an element with **NO record filter
at all** never enters the runtime's filter block, so its query runs UNFILTERED and the grant or revoke
lands on EVERY record of the target object. That query also sets `UseAdminRights = false`, so the radius
is every row in the table rather than the rows the caller can see. The PLATFORM neither refuses nor warns
it — but clio's post-operation read-back does, loudly and specifically
(`AccessRightsBlockExpectation.BuildNoFilterWarning`), which is the only signal a caller gets. "Empty filter" is therefore an ambiguous phrase and is deliberately avoided in the shipped text —
the two states have opposite blast radius, and reading one by analogy with the other inverts both.

**Why it is not enforced** — the server's `EnsureConfiguresSomething`
(`packages/CrtProcessBuilder/Files/src/cs/AccessRights/ChangeAccessRightsApplier.cs` in
`cli-process-builder`) rejects only a block whose fields are ALL null, so `{"object": "Order"}` and
`{"add": [], "remove": []}` both pass. Refusing the resulting-empty state would also refuse the
legitimate two-step flow (`setElement {object}` first, entries in a later call) that the
partial-update contract otherwise permits. Tracked as open decision **D9** in that repo's
`.ai/specs/ENG-92717-change-access-rights-element.md`; until it is decided, every agent-facing
surface states the hazard instead.

**What this means for clio** — the tool descriptions, both process prompts, `docs/McpCapabilityMap.md`
and the `process-access-rights` guidance article all carry this, and they must keep carrying it: an
agent that reads "the build succeeded" as "the rights changed" is the failure this record exists to
prevent. Separately, `AccessRightsBlockExpectation` guards a different silent failure on the same
element — a deployed `CrtProcessBuilder` that predates the element DISCARDS the `accessRights` block
during deserialization and still answers success, so both commands read the process back and warn
when the block did not land.

**CORRECTED 2026-09-03.** The two record-filter states in this note were previously stated backwards.
They were read from the disassembled `ChangeAdminRightsUserTask` and summarised as "the record filter is
empty" — a phrase this note itself flags as ambiguous — and four surfaces then agreed with each other
because they all derived from that one summary. Two reviewers independently read
`ChangeAdminRightsUserTask.InternalExecute` as C# source (byte-identical to the deployed copy): the
`Exit: filters empty` early return fires only for a filter that IS present and conditionless, while an
ABSENT filter never enters the block at all and the query runs unfiltered. The stand run below did not
catch it because no manual case exercised an element with no record filter.

**Verified** — 2026-09-01 on a stand (`Creatio 8.x`, CrtProcessBuilder built from the ENG-92717
branch): 35/35 manual cases, including live grant/revoke runs checked with `get-record-rights`. That
covers the grant/revoke paths, NOT the filter-state semantics above. The remaining guards were read from
the disassembled `ChangeAdminRightsUserTask` and are recorded in
`docs/change-access-rights-element-capture.md` in `cli-process-builder`.
