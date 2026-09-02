# Capture — "Perform task" as the Creatio Process Designer serializes it

**Source:** a real process (`UsrCreateTaskForContactAndUserAccount`, schema UId
`36696f7b-a6e5-498f-8d34-6794c56cf4b6`, `CreatedInVersion` 10.1.480.0) authored **in the designer UI** and exported
by the user on 2026-08-14, together with a screenshot of the element's properties panel. Supplied to settle the
questions the plan had marked UNVERIFIED.

This is the **ground truth for what a human-authored Perform task looks like on disk.** Everything the ProcessBuilder
writes should be explainable against it. Where the builder diverges, the divergence must be a deliberate, recorded
decision — not an accident.

The full JSON is not reproduced here; it lives in the ticket. What follows is what it *proves*.

---

## 1. The designer writes an element-level property the builder does not: `PerformerAssignmentOptions`

On the `ProcessSchemaUserTask` element, a **sibling of `Parameters`**, not a parameter:

```json
"PerformerAssignmentOptions": {
  "PerformerParameterUId": "d44f730c-51d2-4088-9199-7372d4830eb5",   // -> the element's OwnerId parameter
  "RoleParameterUId":      "dde6caf2-55f8-44ba-8bab-498c33da6d90",   // -> a RoleId parameter (see §2)
  "AssignmentType": 1                                                 // AssignmentType.User
}
```

Platform contract:

| Fact | Evidence |
|---|---|
| Declared on `ProcessSchemaActivity` (base of `ProcessSchemaUserTask`) as meta-property **`BP7`** | `Terrasoft.Core/Process/ProcessSchemaActivity.cs:24-25`, `:35`, `:128` |
| Shape: `PerformerParameterUId` (`JH1`), `RoleParameterUId` (`JH2`), `AssignmentType` (`JH3`) | `Terrasoft.Core/Process/ProcessSchemaPerformerAssignmentOptions.cs:22-24`, `:53-66` |
| `AssignmentType`: `None=0`, **`User=1`**, `Manager=2`, `Role=3` | `Terrasoft.Core/Process/AssignmentType.cs:9-31` |

`AssignmentType: 1` matches the screenshot's **"Who performs the task?" = User**. So that dropdown is `AssignmentType`,
and the field under it (Contact) is whichever parameter the type selects.

**The ProcessBuilder writes none of this** — `grep -rn "PerformerAssignmentOptions\|IsPerformer\|RoleId"` over
`packages/CrtProcessBuilder/Files/src/cs` returns nothing.

## 2. `RoleId` is created by the designer; it is not part of `ActivityUserTask`

```json
{ "Name": "RoleId",
  "CreatedInSchemaUId": "36696f7b-…",              // the PROCESS schema — not b5c726f2 (ActivityUserTask)
  "DataValueType": "b295071f-…",                    // Lookup
  "ReferenceSchemaUId": "84f44b9a-4bc3-4cbf-a1a8-cec02c1c029c",
  "ReferenceSchemaName": "SysAdminUnit" }
```

`CreatedInSchemaUId` is the process, so this parameter **does not exist until something creates it**. Any Role-based
assignment therefore requires creating `RoleId` *and* pointing `PerformerAssignmentOptions.RoleParameterUId` at it.
That is why Role assignment is out of scope in the plan's D3 — it is two mechanisms, not a value.

## 3. `IsPerformer` is a real platform flag, and it is what makes `OwnerId` work

The exported `OwnerId` parameter carries `"IsPerformer": true`. This is not designer decoration:

| Fact | Evidence |
|---|---|
| `ProcessSchemaParameter.IsPerformer`, meta-property **`L14`** | `Terrasoft.Core/Process/ProcessSchemaParameter.cs:100`, `:136`, `:425` |
| `GetPerformerParameter()` returns the single `IsPerformer` parameter (throws if >1) | `Terrasoft.Core/Process/ProcessSchemaParameterCollection.cs:127-137` |

It is set on `ActivityUserTask.OwnerId` in the **shipped schema metadata** (`L14: true` on entry 2 of `FJ1` in
`CrtProcessDesigner/branches/7.8.0/Schemas/ActivityUserTask/metadata.json`), so it is present on every element the
builder creates, for free, via the `SchemaUId` parameter sync.

## 4. Why setting `OwnerId` alone is sufficient for a User-assigned task

The runtime does **not** require `PerformerAssignmentOptions`. Chain, in order:

1. `UserTaskActivityHandler.SetPerformer` runs at `:249`, **before** `SetColumnValuesFromParameters` at `:253`.
2. `SetPerformer` writes the Activity owner from the assignment options:
   `activity.SetColumnValue("OwnerId", GetGuidColumnValue(assignmentOptions.PerformerId))`
   — `Terrasoft.Core.Process/UserTaskActivityHandler.cs:67-69`.
3. `GetAssignmentOptions()` falls back when the **runtime** options object is null —
   `ProcessActivity.PerformerAssignmentOptions` (`:361`, meta `HL6`), which is a *different property* from the
   schema-level `BP7`; do not conflate the two same-named members. It returns
   `new PerformerAssignmentOptions { AssignmentType = None, PerformerId = GetPerformer() }`
   — `Terrasoft.Core/Process/ProcessActivity.cs:1060-1063`. The runtime object is populated earlier by
   `InitializePerformerAssignment()` → `AssignmentOptionsInitializer.Init()` (`ProcessActivity.cs:1012-1016`).
4. `GetPerformer()` reads the `IsPerformer` parameter, **defaulting to the current user's contact when empty**:
   `TryGetPerformer(out id) && id.IsNotEmpty() ? id : UserConnection.CurrentUser.ContactId`
   — `Terrasoft.Core/Process/ProcessActivity.cs:481-485`.

Two consequences worth putting in the guidance:

- **A builder-made element with `OwnerId` mapped assigns the performer correctly**, without `BP7`.
- **Leaving `OwnerId` unset does not leave the task unassigned** — it silently assigns the *current user* (whoever
  started the process). There is no "nobody" state. An agent that omits the performer is making a choice, not
  deferring one.

What the absent `BP7` costs depends on a **third** feature flag,
`GlobalAppSettings.UsePerformerCultureInUserTask` (`AssignmentOptionsInitializer.cs:74-77`) — distinct from
`UseProcessPerformerAssignment` (plan R1). Record its state alongside R1.

- **Flag OFF:** `Init()` returns at `:208-211`, the runtime options stay null, and `GetAssignmentOptions()` yields
  the `AssignmentType.None` / `GetPerformer()` fallback described above.
- **Flag ON:** `Init()` takes `:212-218` instead — runtime options are built with `AssignmentType.User`, the empty
  performer is coerced at `:215`, and `InitCultureForUser` (`:144-148`) **does** run.

Unreachable in **both** states: Role and Manager assignment. `GetOptions`' switch (`:119-134`) is entered only when
`BP7` is present, so `InitRoleAssignment` (`:106-113`) and `InitCultureForRole` (`:150-154`) never execute. The
performer outcome is identical either way — which is precisely why `OwnerId` alone suffices. Note also that when
`BP7` *is* present with `AssignmentType.User`, an empty performer parameter is coerced to the current user too
(`:97-99`), so that fallback is not a quirk of the absent-options path.

## 5. A Lookup constant CAN be a plain Guid `ConstValue` — the validator's blanket rejection is wrong

*(Observed on the two enum-combo lookups. No designer-written record-constant on a **connection** lookup appears in
this sample — §8 shows those written as `Source: 3` Script — so this section says nothing about that case, which is
what plan D4's "risk accepted" paragraph reasons about.)*

The single most consequential line in the capture. The designer wrote, on `ActivityCategory`:

```json
"SourceValue": { "Source": 1, "Value": "f51c4643-58e6-df11-971b-001d60e938c6" }
```

`Source: 1` is `ConstValue`, and the value is a **bare Guid** — not a `[#Lookup.{object}.{record}#]` macro.
`ActivityPriority` is stored identically (`Source: 1`, `Value: "ab96fa02-7fe6-df11-971b-001d60e938c6"`).

This directly contradicts the premise of
`CrtProcessBuilder/Files/src/cs/Parameters/ProcessParameterValueValidator.cs:58-68`, whose comment asserts a Lookup
constant "is a `[#Lookup…#]` formula token … never a plain `ConstValue`". The designer proves otherwise for this
element. See plan §6 D4.

Not contradicted: the **Date/Date-time/Time** rejection at `:70-80`. This capture contains no date constant, so that
branch stands unchallenged either way.

## 6. `Recommendation` is written as an empty ConstValue

```json
{ "Name": "Recommendation", "DataValueType": "95c6e6c4-…",
  "SourceValue": { "Source": 1, "ModifiedInSchemaUId": "36696f7b-…" } }     // Source, but NO Value
```

Schema-level `"LocalizableStrings": []` is empty. The element caption is `"Create task"` (screenshot, and the element's
`Name`). So the designer left the subject empty and the Activity title comes from the caption — consistent with
`GetActivityTitle()`'s caption fallback and with plan D5.

This capture does **not** show a designer-written non-empty `Recommendation`, so it does not settle where a non-empty
value would be stored. Probe P3 still stands.

## 7. Connection parameters carry two different tags

| Tag | Which | Example |
|---|---|---|
| `"EntityColumnValue"` | the **19** shipped connection columns (of **22** `EntityColumnValue`-tagged parameters in total) | `Lead`, `Account`, `Contact`, `Opportunity`, `Invoice`, `Document`, `Incident`, `Case`, `Order`, `Requests`, `Listing`, `Property`, `Contract`, `Project`, `Problem`, `Change`, `Release`, `Application`, `FinApplication` |
| `"EntityColumnValue"`, **non**-connection | designer group "General" | `ActivityCategory`, `OwnerId`, `ShowInScheduler` — tagged, but not connections. **`OwnerId` is tagged and still never copied**: the host column is `Owner`, not `OwnerId`, so the by-name match at `UserTaskActivityHandler.cs:85` cannot reach it (`ProcessDesignConstants.cs:127-129`). The tag is not what assigns the performer. |
| `"ActivityConnection"` | dynamically added connections | `UsrUsrTestApprovalElement`, `OmniChat`, `UsrTestConnection`, `UsrTestConnection2` |
| *(none)* | neither | `QueueItem` (`ReferenceSchemaName: "VwQueueItem"`), `ActivityPriority` |

`GetActivitySchemaColumns` copies only `EntityColumnValue`-tagged parameters
(`UserTaskActivityHandler.cs:79-87`), which is the mechanism behind plan §4's `EntityColumnValue` column — and the
reason `QueueItem`'s status stays open (plan R8): it is untagged, but so is `ActivityPriority`, which is demonstrably
live through an explicit assignment.

Also present: `ExecutionContext` carries `"IsValueSerializable": false`; `ActivityResult` carries `"IsResult": true`
while **`CurrentActivityId` carries neither `IsResult` nor `Direction: Out`** — which is exactly why the latter is
invisible to `describe-process` (plan G6).

## 8. Value-source encodings observed

| Source | Meaning | Example from the capture |
|---|---|---|
| `1` | `ConstValue` | `ActivityCategory` = `f51c4643-…`; `Duration` = `"20"`; `ShowInScheduler` = `"true"` |
| `3` | `Script` / formula | `OwnerId` = `[#SysVariable.CurrentUserContact#]`; `Account` = `[#SysVariable.CurrentUserAccount#]` |
| `{}` | unset | every untouched connection parameter |

The process-parameter reference form is longer than the plan's `[#…[Parameter:{uid}]#]` shorthand suggests — the
`Contact` connection reads:

```
[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{2498a359-1442-442b-a73b-377ff9aca7da}]#]
```

pointing at the process-level `ContactId` parameter. Compare what `ProcessSchemaParameter.GetMetaPath()` produces in
`ProcessMappingService.BuildSourceValue` before asserting the builder's form round-trips against a
designer-authored process.

## 9. Miscellany worth not re-discovering

- The element's `ManagerItemUId` equals the `ActivityUserTask` schema UId `b5c726f2-…` (not the generic user-task
  container), confirming the dedicated-palette promotion in `UserTaskElementHandler.HasDedicatedPaletteElement`.
- `Mappings[]` contains one row per element parameter, most with `Source: {}` and the placeholder name
  `"ProcessSchemaMapping1"`; the dynamic-connection rows are instead named after the element (`"CreateTask"`).
  Relevant to plan G10.
- The process's end element is a `ProcessSchemaTerminateEvent`, not a plain end.
