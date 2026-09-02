---
description: a Change access rights element with NO record filter, or with add and remove both empty, builds green and then changes no permissions at run time - it has no output parameters, so nothing reports it; a filter that IS present but has no conditions is the opposite hazard - it matches every record - and is refused at build
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs
  - clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs
  - clio/Command/ProcessModel/AccessRightsBlockExpectation.cs
ticket: ENG-92717
date: 2026-09-01
---

**What is true** — the Change access rights element (`changeAccessRights` /
`ChangeAdminRightsUserTask`) has **no output parameters**. Nothing downstream can branch on whether
permissions changed, and the runtime writes no error or log an agent can read back. Its runtime
silently does nothing in three cases, and only ONE of them is refused when the process is built:

| Configuration | Refused at build? |
|---|---|
| target object does not use record permissions (`AdministratedByRecords` off) | **yes** |
| the element has NO record `filter` at all | no |
| `add` and `remove` are both empty (a block carrying only `object` is one) | no |

So a successful build is **not** evidence the element will do anything. The dangerous direction is a
REVOKE: a revocation that silently does nothing leaves privileges in place while every signal the
caller can see reports success.

The opposite state is NOT in that table and must not be confused with it: a record `filter` that IS
present but carries no conditions narrows nothing, so it would match EVERY record of the target
object. That one IS refused at build (ENG-92717 round 2 review): the same recursive emptiness predicate now
guards the signal filter, the grantee filter and this record filter, scoped to this element type so
readData / changeData semantics are unchanged. "Empty filter" is therefore an ambiguous phrase and
is deliberately avoided in the shipped text — the two states have opposite blast radius.

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

**Verified** — 2026-09-01 on a stand (`Creatio 8.x`, CrtProcessBuilder built from the ENG-92717
branch): 35/35 manual cases, including live grant/revoke runs checked with `get-record-rights`. The
runtime guards were read from the disassembled `ChangeAdminRightsUserTask` and are recorded in
`docs/change-access-rights-element-capture.md` in `cli-process-builder`.
