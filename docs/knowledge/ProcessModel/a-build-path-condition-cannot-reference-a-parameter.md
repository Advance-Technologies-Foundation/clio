---
description: create-business-process accepts flows[].condition but a condition references a parameter by UId meta-path and those UIds do not exist until the call that creates them, so 88% of real conditions - every one on a process parameter or an element output - cannot be declared on the build path at all and must come from a later modify-business-process
applies-to:
  - clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs
  - clio/CrtProcessBuilder/CrtProcessBuilder.gz
ticket: ENG-91853
date: 2026-09-06
---

**What is true** — `flows[].condition` on the build path works only for conditions that carry no
parameter reference. A condition addresses a parameter by its **UId meta-path**
(`[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{guid}]#]`), never by name, and on
`create-business-process` those UIds do not exist yet: the process, its parameters and its elements are
all created by that same call, and `ProcessParameterDescriptor` has **no `uid` field**, so a caller
cannot pre-generate one either. Writing the name instead is not a way round it — `[#Amount#]` is refused
by the platform's pre-save gate with `Formula value error: Expression expected (at index 0)`, and
because that gate runs on the whole schema the entire build is aborted and nothing is created.

**How much this covers**, measured over the 7.8.0 corpus (1 405 conditional flows, 1 402 with a stored
expression, of which ~341 decode to an empty one the runtime replaces with `true`):

| Expression shape | Count | Buildable at create |
|---|---|---|
| element output `[Element:{uid}].[Parameter:{uid}]` | 487 | no |
| process parameter `[Parameter:{uid}]` | 445 | no |
| literal / call into the schema's own generated code | 92 | rarely |
| `[#SysSettings.Code<Type>#]` | 37 | **yes** |

932 of the 1 061 non-empty expressions — 87.8% — carry a UId that cannot exist yet. `SysSettings` is the
only writable family and it is 3.5%. `BulkFileManagement/DeleteFilesInTable`,
`BulkFileManagement/ScheduleFileCleanup` and `BpmGDPR/BpmProcess5` are examples the create path cannot
express; `CrtBase/ExpireLicenseNotificationProcess` is one it can.

**Why it is this way** — the condition is stored verbatim (`ProcessGraphBuilder`,
`ConditionExpression = condition`) and never resolved, while the neighbouring mapping surface DOES
resolve: `ProcessMappingService` takes `processParameter` **by name** and expands it through
`ResolveProcessParameter` + `ProcessSchemaParameter.GetMetaPath()` — on the same schema object, in the
same build call — and does the same for an element output. The asymmetry is an accident of which
surface was written when, not a platform limitation.

**What breaks if you ignore it** — an agent following the create tool's own guidance writes a
descriptor with a name-based condition, the pre-save gate aborts the whole build, and the agent gets a
formula error that says nothing about UIds or about the build path. Measured in a clean-room manual run:
the executor never once used `flows[].condition` and routed every branch through a later
`modify-business-process` — the exact two-step route the description tells callers to avoid.

**Until this is resolved**, say so where callers read: the create tool's description and
`branch-conditions.md` now both state which conditions belong here and which belong to the modify step.
If name→meta-path resolution is ever added to the build path (the shape would be accepting
`[#ParameterName#]` and expanding it in a pass over `flows[]` **after** the parameters are created,
covering both the process-parameter and the element-output forms), those two texts are the first things
that become wrong again.
