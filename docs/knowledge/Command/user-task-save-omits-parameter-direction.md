---
description: Native user-task GetSchema and SaveSchema parameter DTOs omit Direction, so unrelated edits can erase L12.
applies-to:
  - clio/Command/ModifyUserTaskParametersCommand.cs
  - clio/Command/UserTaskMetadataDirectionApplier.cs
ticket: clio-1601
date: 2026-09-17
---

**What is true** — On Creatio 10.1.585.0, creating explicit Out parameters and then
adding a parameter through the native designer service can remove the existing
parameters' L12 direction metadata. The native SchemaParameterDto has no direction
member; adding `direction` to clio's DTO does not make that service persist it.

**Why it is this way** — Task authoring uses the native designer DTO plus the existing
FSM metadata direction adapter. Modification must capture explicit directions before
SaveSchema/BuildPackage overwrite the workspace, then reapply surviving directions
alongside requested changes through the FSM import/build flow.

**What breaks if you ignore it** — Adding an unrelated parameter silently changes
existing inputs and outputs to the platform's default direction. This was reproduced
sequentially against a linked FSM package, independently of concurrent test execution.
