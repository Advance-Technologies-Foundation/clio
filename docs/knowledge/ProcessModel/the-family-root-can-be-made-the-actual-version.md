---
description: SetActiveProcessVersion accepts the family ROOT as the target and the platform activates it normally - there is no root guard in clio or in CrtProcessBuilder, and activating the root is the go-back-to-the-original rollback
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/SetActiveProcessVersionTool.cs
  - clio/Command/SetActiveProcessVersionCommand.cs
ticket: ENG-94374
date: 2026-09-11
---

**What is true** — any member of a version family can be made the actual one, the **root included**.
Measured on a stand during ENG-94374 manual testing: a three-member family was switched v2 → v1 → v0
(root) → v2, exactly one member active at every hop, with the process-library view agreeing
independently each time.

**Nothing refuses it, anywhere.** `SetActiveProcessVersionCommand` validates only that exactly one of
name/uid is supplied. `ProcessVersionActivateHandler` in `CrtProcessBuilder` contains no rootness
check at all. The restriction existed in **one sentence of the MCP tool description** and in no code.

**Why it is this way** — the root is not a special kind of schema. A family is flat: every member,
root included, is a `SysSchema` row whose active-version flag the platform sets and clears the same
way. The root is simply the member others point at, and `VwProcessLib` computes
`VersionParentUId = COALESCE(parent.UId, own.UId)` so a root is its own family key. There is no field
that would make it unactivatable and no code that consults one.

**What breaks if you ignore it** — the failure is a wrong refusal, and it costs the user the one
rollback they are most likely to ask for. "Undo everything, go back to the original" IS activating the
root, because the root is the version that ran before the family existed. An agent that believes the
target must be a version reports that rolling back to the original is impossible, and the alternatives
it then offers — copy the original under a new name, save yet another version — are both worse and
both permanent (a version cannot be deleted). Do not reintroduce the claim in a tool description, a
guidance article, or a validation guard without a measurement that contradicts the one above.
