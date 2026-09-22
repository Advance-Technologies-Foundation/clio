# ENG-92707 — Sub-process element: what the platform already does

Read this before the plan. It answers the ticket's central question — *who performs the parameter
synchronization* — and the answer changes the shape of the work.

Sources are the local checkouts: core `C:/Projects/Creatio2/TSBpm/Src/Lib` (note: **not**
`C:/Projects/Creatio/TSBpm`, which does not exist on this host), the classic designer package
`C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0`, the NUI client resources under
`Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/`, and the 7.8.0
package corpus `C:/Projects/PackageStore`.

---

## 1. The short answer

**The server already implements the whole of AC3.** Assigning `ProcessSchemaSubProcess.SchemaUId`
synchronously runs the platform's own add / drop / preserve diff against the called process's
parameters. There is nothing to invent:

```csharp
// Terrasoft.Core/Process/ProcessSchemaSubProcess.cs:62-71
private Guid _schemaUId;
[MetaTypeProperty("{1350ADFA-8D64-40F7-B485-433058D258B0}")]
public override Guid SchemaUId {
    get => _schemaUId;
    set {
        _schemaUId = value;
        _isSchemaInitialized = false;
        _schema = null;
        ((IParametrizedProcessSchemaElement)this).SynchronizeParameters();
    }
}
```

This is the one structural difference from the Pre-configured page element (ENG-92705 / ENG-95461),
and it is why `PreconfiguredPageParameterSync` must **not** be cloned. A Pre-configured page is
referenced by a *parameter* of a user task, so the platform's `GetSchemaParameters()` returns the
**user-task schema's** parameters and never the page's — the package had to write its own diff. A
sub-process references the callee through the element's own `SchemaUId`, and the override returns the
**callee's** parameters:

```csharp
// ProcessSchemaSubProcess.cs:189-196
protected override ProcessSchemaParameter FindSchemaParameterByUId(Guid schemaParameterUId) {
    return (ProcessSchemaParameter)Schema?.FindBaseElementByUId(schemaParameterUId);
}

protected override ProcessSchemaParameterCollection GetSchemaParameters() {
    return Schema == null ? new ProcessSchemaParameterCollection() : Schema.Parameters;
}
```

So the package's job for ENG-92707 is **ordering, guarding and reporting**, not diffing.

---

## 2. The element type

`Terrasoft.Core.Process.ProcessSchemaSubProcess : ProcessSchemaActivity, IProcessSchemaFlowElementsContainer`.

One CLR class serves two BPMN shapes. `TriggeredByEvent` distinguishes them, and
`ProcessSchemaEventSubProcess` *inherits* from `ProcessSchemaSubProcess` — so an `is` / cast check on
`ProcessSchemaSubProcess` also catches the embedded event sub-process. Any identity predicate must
test more than the CLR type.

| Concern | Value | Where |
|---|---|---|
| BPMN element name | `"SP"` | `BpmnElementVocabulary.SubProcessName`, set in `Initialize()` |
| Design-mode name prefix | `SubProcess` | `[DesignModeClass(DefNamePrefix = "SubProcess")]` |
| Called process | `Guid SchemaUId`, meta key **`CK4`**, `IsRequired = true` | `[DesignModeProperty(Name = "SchemaUId", MetaPropertyName = "CK4", ...)]` |
| Candidate filter | `ValuesProvider = "ProcessSchemaManagerExceptedUIdValuesProvider"` | same attribute |
| Version switch | `bool UseLastSchemaVersion`, meta key **`CK5`** | `[MetaTypeProperty("{4FD6C6B7-790C-45D3-9029-D47865EC3E29}")]` |
| Event flavour | `bool TriggeredByEvent`, meta key **`CK1`** | `[MetaTypeProperty("{B10C0EE2-9A52-4984-81C6-FBBD755996DB}")]` |
| Inline children | `FlowElements` **`CK2`**, `Artifacts` **`CK3`** — both `UsageType.None` in design mode | `[DesignModeProperty(...)]` |
| Palette / manager item UId | `49eafdbb-a89e-4bdf-a29d-7f17b1670a45` | `process-subprocess-schema.js:12`; present on **every** corpus instance as `BL7` |
| Default size | `69 x 55` | `process-subprocess-schema.js:18,24`; `BN2:"69;55"` in the corpus |
| Properties page | `SubProcessPropertiesPage` | `process-subprocess-schema.js:83` |
| Client type name | `Terrasoft.Core.Process.ProcessSchemaSubProcess` | `process-subprocess-schema.js:57` |
| Icons / colour | `CallActivitySmall.svg` / `CallActivityLarge.svg`, `#E6C600` | `process-subprocess-schema.js:60-77` |

`ProcessSchemaSubProcess`'s constructor sets **only** `BpmnElementName`. Unlike `ProcessSchemaUserTask`
and both gateway classes, it does **not** stamp its own `ManagerItemUId` or `DragGroupName`. A handler
that forgets to write `BL7` produces an element that every shipped instance has and the new one does not.

Serialization order, from `WriteMetaData` (`ProcessSchemaSubProcess.cs:246-258`): base (which writes
`Parameters` as `BP2`), then `CK1` `TriggeredByEvent` (omitted when false), `CK4` `SchemaUId` (omitted
when `Guid.Empty`), `CK5` `UseLastSchemaVersion` (omitted when false), then `CK2` and `CK3`
collections — which are written **unconditionally**, so an empty call activity still emits
`"CK2": []`, `"CK3": []`.

`AnalyzePackageDependencies` reports the callee as a schema dependency:
`reporter.ReportSchemaDependency(SchemaUId, nameof(ProcessSchemaManager), Name)`.

---

## 3. The synchronization algorithm, exactly

`ProcessSchemaActivity.SynchronizeParameters()` (`ProcessSchemaActivity.cs:587-599`):

```csharp
public virtual void SynchronizeParameters() {
    if (!GetCanSynchronizeParameters()) {            // silent no-op, see 3.1
        return;
    }
    if (SchemaUId.IsEmpty()) {
        ClearParameters();                           // removes EVERY parameter AND its mapping row
        return;
    }
    ICollection<ProcessSchemaParameter> removedParameters = GetRemovedSchemaParameters();
    RemoveSchemaParameters(removedParameters);       // + InvalidateDependentElements
    UpdateParameters();                              // refresh caption/group/type of survivors
    FillNewSchemaParameters();                       // add what the callee has and the element lacks
}
```

`ProcessSchemaSubProcess` overrides it to add one step:

```csharp
// ProcessSchemaSubProcess.cs:240-243
public override void SynchronizeParameters() {
    base.SynchronizeParameters();
    ClearParametersSourceValue();
}
```

### 3.1 The guard — three ways to be a silent no-op

```csharp
// ProcessSchemaActivity.cs:324-326
private bool GetCanSynchronizeParameters() {
    return !(BaseProcessSchema.UId.IsEmpty() || SchemaUId.Equals(BaseProcessSchema.UId) || UId.IsEmpty());
}
```

`BaseProcessSchema` is `(ProcessSchema ?? ParentMetaSchema)` (`ProcessSchemaBaseElement.cs:101`).
So the sync does nothing, and says nothing, when:

1. the host schema has no `UId` yet;
2. the element has no `UId` yet — **assigning `SchemaUId` before the element's own `UId` loses the sync**;
3. **`SchemaUId == the host schema's UId`** — a process calling itself. This is the platform's only
   self-reference guard, and it is invisible: the element is written, the parameters simply never appear.

**None of these three guards protects a multi-instance element**, because the guard sits one method too
deep — see 3.6.

### 3.2 The matching key is the mapping row, not the name

Every synced element parameter is paired with the callee's parameter by a `ProcessSchemaMapping` row
in the **caller** schema's `Mappings` collection:

```csharp
// ProcessSchemaActivity.cs:211-225
private ProcessSchemaMapping CreateProcessSchemaMapping(ProcessSchemaParameter sourceParameter,
        ProcessSchemaParameter targetParameter) {
    return new ProcessSchemaMapping {
        CreatedInSchemaUId = BaseProcessSchema.UId,
        ModifiedInSchemaUId = BaseProcessSchema.UId,
        Name = Name,                                    // the ELEMENT's name
        ParentMetaSchema = BaseProcessSchema,
        Source = sourceParameter.SourceValue,
        SourceSchemaUId = SchemaUId,                    // the CALLEE's schema UId
        SourceParameterUId = sourceParameter.UId,       // the CALLEE's parameter UId
        TargetUId = targetParameter.UId,                // the ELEMENT parameter's UId
        TargetMetaPath = $"[Element:{{{UId}}}].[Parameter:{{{targetParameter.UId}}}]",
        UId = Guid.NewGuid()
    };
}
```

Forward lookup is `Mappings.FindByTargetUId(elementParameter.UId)`; backward lookup is
`Mappings.FindMappingsBySource(SchemaUId, sourceParameter.UId)`. **Name is only the adoption fallback
in the ADD phase** (`Parameters.FindByName(source.Name)` in `FillNewSchemaParameters`).

Drop rules (`GetRemovedSchemaParameters`, `ProcessSchemaActivity.cs:253-269`):

* no mapping row **and** `target.CreatedInSchemaUId == SchemaUId` → remove;
* mapping row exists but its `SourceParameterUId` is not in the callee any more, and the target is not
  `IsDynamic` → remove the parameter **and** the mapping row;
* otherwise keep.

Preserve (`UpdateParameters`): for every surviving target with a mapping row whose source still exists
and which is not `IsDynamic`, refresh caption, group and data value type, and keep the mapping — hence
the stored value.

Add (`FillNewSchemaParameters`): for every callee parameter with no live target
(`GetHasNotContainedTargetParameter`), adopt a same-named element parameter if one exists, else create
one with a **fresh UId** (`CreateElementParameterFromUserTaskSchemaParameter`), insert at the callee's
ordinal, and create the mapping row.

### 3.3 Idempotency, and the one pass it is short

Re-running against an unchanged callee changes nothing: `GetHasNotContainedTargetParameter` blocks
re-adding, and every drop rule requires the source to be gone. Adoption is the exception —
`FillNewSchemaParameters` adopts a survivor by name and creates the mapping row but never calls
`SynchronizeParameter`, so a *renamed-or-retyped* adopted parameter's caption, type, direction and
requiredness are only corrected on the **next** run. It gets that next run for free:
`BaseProcessSchemaManager.FindDesignItem` and `GetItemFromMetaData` call `SynchronizeParameters()` on
every read of a process schema (`BaseProcessSchemaManager.cs:961,966,1090`), so CrtProcessBuilder's own
`GetDesignInstance` re-syncs every sub-process element before the package ever sees it.

**Consequence for the plan:** a "stale" sub-process element is not a state the C# object model will
hand you. A server-side re-sync therefore cannot observe what changed *after the fact* — the only
window is a snapshot taken before the write.

### 3.4 Dependents are flagged, not refused

`RemoveSchemaParameters` calls `InvalidateDependentElements`, which text-searches every other element
parameter's `SourceValue.Value` for the removed parameter's UId and sets `dependentElement.IsValid =
false` / `dependentParameter.IsValid = false` (`ProcessSchemaActivity.cs:271-289`). Nothing throws.
The failure surfaces later, at process **start**: `ProcessSchema.GetInvalidElementNames` walks into the
called schema too, and `Verify` → `CheckSchemaHasInvalidElements` raises `ValidateException`.

### 3.5 Values survive only under two conditions

```csharp
// ProcessSchemaSubProcess.cs:140-183
private static bool GetIsChangedParameterValue(ProcessSchemaParameter parameter, Guid parentMetaSchemaUId) {
    ProcessSchemaParameterValue sourceValue = parameter.SourceValue;
    return parameter.CreatedInSchemaUId != sourceValue.ModifiedInSchemaUId &&
        (!parameter.IsNested || sourceValue.ModifiedInSchemaUId == parentMetaSchemaUId);
}

private static bool GetIsAssignable(ProcessSchemaParameterDirection direction) {
    return direction.GetIsAssignable() ||
        !GlobalAppSettings.FeatureClearSubProcessParametersSourceValue;
}

private void ClearParametersSourceValue() {
    foreach (ProcessSchemaParameter parameter in Parameters) {
        ProcessSchemaParameterValue sourceValue = parameter.SourceValue;
        bool isChanged = GetIsChangedParameterValue(parameter, ParentMetaSchema.UId);
        bool isAssignable = GetIsAssignable(parameter.Direction);
        if (!isChanged || !isAssignable) {
            sourceValue.ClearParameterSourceValue();
        }
    }
}
```

* `GetIsAssignable` (`ProcessSchemaParameterDirectionUtils.cs:22`) is `true` **only** for `In` and
  `Variable`. An `Out` or `Internal` parameter's source value is wiped on **every** sync.
* `FeatureClearSubProcessParametersSourceValue` defaults to **`true`**
  (`GlobalAppSettings.cs:381`), overridable by the app-config key
  `Feature-ClearSubProcessParametersSourceValue`.
* `isChanged` is **not a value comparison.** It is `CreatedInSchemaUId != SourceValue.ModifiedInSchemaUId`
  — a provenance stamp pair. A value counts as "the caller set this" only when the parameter was created
  under the callee's schema and the value was modified under the caller's.

**This is the single most consequential rule for a writer.** The corpus confirms it directly (section 4):
element parameters carry `A3`/`A4` = the **callee's** schema UId, and a mapped value carries
`L8.GS5` = the **caller's**. Copying the Pre-configured page's
`existing.CreatedInSchemaUId = schema.UId` stamp onto a sub-process element makes
`isChanged` false for every parameter, and the next sync silently erases every value written.

Relevant enums (`Terrasoft.Core/Process/ProcessSchemaParameter.cs:15-55`):

```
ProcessSchemaParameterValueSource : None=0, ConstValue=1, Mapping=2, Script=3,
                                    SystemValue=4, SystemSetting=5, EntityMapping=6, SamplingEntityMapping=7
ProcessSchemaParameterDirection   : In=0, Out=1, Variable=2, Internal=3
```

### 3.6 Multi-instance takes a different path entirely

The `SchemaUId` setter calls the **explicit-interface** member, which routes through
`SynchronizeParametersInternal` (`ProcessSchemaActivity.cs:373-395`). When
`IsMultiInstanceModeEnabled`, that clones the two collection parameters and the three counters, calls
**`Parameters.Clear()` unconditionally**, and only then delegates to the inner `SynchronizeParameters`
where `GetCanSynchronizeParameters()` lives. The callee's parameters then live as `ItemProperties` of
the two collections rather than on the element.

**Two consequences, and the second is a Blocker.** First, everything in 3.2-3.5 describes the
single-instance path only. Second — because the `Clear()` precedes the guard — **the self-reference
and empty-UId protections of 3.1 do not apply to a multi-instance element at all.** Any code path that
reaches the setter or the interface method rebuilds such an element as two collections plus three
counters, discarding the callee's parameters and every value mapped into them. 61 of the 416 shipped
elements are in that state. See plan D9 and trap T-25.

---

## 4. What the designer writes — a real, decoded example

`C:/Projects/PackageStore/BulkFileManagement/branches/7.8.0/Schemas/RunFileCleanup/metadata.json`
(full decode in the serialization capture). The element:

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaSubProcess",
  "UId": "1ea89d86-1364-42b5-bf4b-15163eee0fdf",
  "A2":  "SubProcess1",
  "A3":  "d4aa9448-...",                     // CreatedInSchemaUId  = the CALLER schema
  "A4":  "d4aa9448-...",                     // ModifiedInSchemaUId = the CALLER schema
  "IL2": "7310a170-...",                     // ContainerUId (the lane)
  "BL3": "328;172",                          // position
  "BL7": "49eafdbb-a89e-4bdf-a29d-7f17b1670a45",   // ManagerItemUId
  "BN2": "69;55",                            // size
  "BP2": [ /* element parameters */ ],
  "CK4": "b2aae6b3-...",                     // SchemaUId = the CALLEE
  "CK2": [], "CK3": []
}
```

One of its parameters:

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaParameter",
  "UId": "5a685723-...",                     // FRESH - never the callee's parameter UId
  "A2":  "TargetTableNamesString",
  "A3":  "b2aae6b3-...",                     // CreatedInSchemaUId  = the CALLEE   <-- see 3.5
  "A4":  "b2aae6b3-...",
  "IL2": "1ea89d86-...",                     // ContainerUId = the element
  "L1":  "c0f04627-...",                     // DataValueTypeUId
  "L8":  { "GS1": 3,                         // Source = Script
           "GS2": "[#[IsOwnerSchema:false].[IsSchema:false].[Element:{89ea736c-...}].[Parameter:{5e5d173e-...}]#]",
           "GS5": "d4aa9448-..." },          // ModifiedInSchemaUId = the CALLER   <-- see 3.5
  "L12": 0                                   // Direction = In
}
```

And the mapping row that binds them, in the caller schema's `BK15` collection:

```jsonc
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaMapping",
  "UId": "4cc1dd40-...",
  "A2":  "SubProcess1",                      // the ELEMENT's name
  "A3":  "d4aa9448-...", "A4": "d4aa9448-...",   // the CALLER schema
  "GT2": "[Element:{1ea89d86-...}].[Parameter:{5a685723-...}]",   // TargetMetaPath
  "GT3": "5a685723-...",                     // TargetUId          = the element parameter
  "GT5": "2a87c3f8-...",                     // SourceParameterUId = the CALLEE's parameter
  "GT4": "b2aae6b3-...",                     // SourceSchemaUId    = the CALLEE
  "GT1": { "GS2": "", "GS5": "b2aae6b3-..." }
}
```

The designer writes **one mapping row per synced parameter**, nested collection items included. That
is why the platform's own re-sync is non-destructive on a shipped process: the rows are there for
`FindByTargetUId` to find.

---

## 5. What the classic designer does that the server does not

`CrtProcessDesigner/branches/7.8.0/Schemas/SubProcessPropertiesPage/SubProcessPropertiesPage.js`,
parent `RootUserTaskPropertiesPage`.

**Candidate list.** `Schema` is a virtual `SysSchema` lookup, `isRequired`. `getSchemaListFilter`
builds `{PackageUId, EnabledOnly: false, ExcludedSchemas: [parentSchema.uId, currentlySelected]}` —
package-scoped unless the `AutoAddPackageDependenciesInProcesses` feature is on, disabled processes
included, and the **only** cycle guard is excluding the immediately containing process. No ancestor
check.

**Version.** `getSubProcessSchemaInstance` calls
`Terrasoft.ProcessSchemaManager.getActualVersionUId(schemaUId)` and then loads *that*. Opening the
card silently re-points `schemaUId` to the callee's actual version. `useLastSchemaVersion` is declared
and serialized and **never written by any UI** in the package — it is dead.

**Two different sync paths, behaving oppositely.**

| Trigger | Path | Effect |
|---|---|---|
| User picks a **different** process | `onBeforeSchemaChanged` → `canChangeSchema` → `confirmSchemaChange` → `onAfterSchemaChanged` → `initSchemaParameters` | **Destructive.** Confirmation `Resources.Strings.ChangeSchemaWarningMessage` with a `change` button; the element's parameters and mappings are cleared, multi-instance is converted back to single, and the element caption is unconditionally renamed to the callee's display value by `setElementCaptionBySchema`. |
| The **same** callee's parameters changed | `synchronizeActualSchemaParameters` | **Preserving.** Snapshots the old root parameters, re-syncs, then for each old parameter finds `findParameterByNameOrByUId(name, uId)` and restores it through `synchronizeSchemaParameter`. |

`RootUserTaskPropertiesPage._synchronizeSchemaParameter` is the restore, and its rules matter:

```js
if (!newParameter || newParameter.dataValueType !== oldParameter.dataValueType) { return; }  // a retype loses the value, silently
const newUId = newParameter.uId;
element.removeParameterByUId(newUId);
newParameter.uId = oldParameter.uId;                       // element parameter UIds stay STABLE
...
const canBeMapped = newParameter.processFlowElementSchema.getCanAssignParameterSourceValue(newParameter);
if (canBeMapped) { newParameter.setMappingValue(oldParameter.getMappingValue()); }           // In/Variable only, as on the server
newParameter.isValid = oldParameter.isValid;
if (!newParameter.getIsDynamic() && newUId !== oldUID) { this._changeSchemaParameterMapping(element, newUId, oldUID); }
this._synchronizeNestedParameters(element, newParameter, oldParameter);                      // behind ManageProcessCollectionParameters
```

**The designer refuses a retarget the server allows.** `canChangeSchema` →
`getCanRemoveElement`: if any other element parameter, process parameter or flow condition still maps
from this element, the lookup is reverted and the user is told the element cannot be removed. The C#
`SchemaUId` setter has no such guard — it drops the parameters and sets `IsValid = false` on the
dependents (3.4), and the process then refuses to start.

---

## 6. Runtime — why a stale element fails silently

**Traced first-hand 2026-09-14. Both directions run through the same method**, so the rule is one rule:

```
inbound   ProcessComponentSet.InitParameterValues(dataReader)            (:461-464)
            -> ReadPropertiesData(dataReader, parametersWriter.CopyCurrentValue)

outbound  ProcessComponentSet.WriteProcessParameters()                   (:1150-1163)
            -> Owner.ReadSubProcessParameters(element, this)
            -> WriteParametersToInterpretedOwner(subProcessElementSchema) (:466-484)
                 while (reader.Read().IsNotNullOrWhiteSpace())
                     parameterWriter.CopyCurrentValue(reader)
```

And `CopyCurrentValue` (`ProcessInstanceParametersDataWriter.cs:503-514`) is where the silence lives:

```csharp
public void CopyCurrentValue(IProcessParametersDataReader reader) {
    string currentName = reader.CurrentName;                       // a NAME
    if (ShouldSkipCopyingCurrentValue(currentName)) {              // only: name is null/whitespace
        return;
    }
    if (TryGetProcessParameterPath(currentName, out string parameterPath) &&
            (_settings.ForceWrite || _parameterStore.GetContainsMapPath(parameterPath))) {
        (object value, DataValueType valueType) = reader.ReadValue();
        WriteValue(currentName, valueType.ValueType, value, null);
    }
    // no else. an unmatched name simply falls off the end.
}
```

```csharp
private bool TryGetProcessParameterPath(string parameterName, out string parameterPath) {   // :160-171
    ...
    ProcessSchemaParameter schemaParameter = FindProcessSchemaParameter(parameterName);
    if (schemaParameter == null) {
        parameterPath = null;
        return false;                                              // no throw, no log
    }
    ...
}

protected virtual ProcessSchemaParameter FindProcessSchemaParameter(string name) =>          // :218-219
    _schemaParameters.FindScalarParameterByName(name);
```

So: the key is the parameter **NAME**; the lookup is over **scalar** parameters only; `Direction` is
never consulted; and a name the other side does not carry makes `TryGetProcessParameterPath` return
`false`, which skips the write **without an exception and without a log line**. `IsRequired` is never
validated for a sub-process element on either side.

A sub-process whose callee lost or renamed a parameter therefore keeps running and quietly delivers
nothing — the opposite of the loud failure in 3.4, and the whole reason AC3's re-sync is worth
shipping.

`SubProcessClassGenerator` / `SubProcessProxy` resolve the callee through `GetActiveVersion`, so
`SchemaUId` is **not** a version pin even though it looks like one. When the callee cannot be resolved
at generation time the generator emits an unassigned
`public ProcessFlowElement <Name> { get; }`.

`SysSubProcessInProcess` (the "Used as sub-process in processes" detail) is rewritten only through the
`ProcessInfoActualized` event inside `GetSchemaSources`, i.e. only when source generation runs — it is
not a design-time write obligation.

---

## 7. The new Angular designer carries no obligation

`C:/Projects/creatio-ui` knows the element by exactly one type string, `callActivity`
(`ProcessElementType.callActivity`), renders it as a 69x55 non-container task with a forced
"Collapsed" (+) marker, and offers it in the palette and the Change-type popup. There is **no**
called-process selection and **no** parameter sync anywhere in the monorepo: an exhaustive grep for
`calledElement` / `calledProcess` over `apps` + `libs` returns zero hits, and both BPMN import and
export drop the reference. The Creatio-embedded designer is a bare `<ts-process-diagram>` that
forwards only `{itemName, id, caption, parentId, position}` on selection — every configuration surface
is still the classic NUI page of section 5. Verified negative; nothing in ENG-92707 changes `creatio-ui`.

---

## 8. Provenance

Sections 1-6 were read directly against the cited files during this analysis, and independently
re-verified by a second adversarial pass that completed on 2026-09-13 (31/31 agents, zero errors).
Section 7's negative finding for `creatio-ui` has now been confirmed twice. What remains single-sourced
is listed in [open-questions §B.4](eng-92707-sub-process-element-open-questions.md) — chiefly the
derived corpus percentages, not the base counts.

Two further facts that second pass established, and which qualify section 3:

* **`ApplyMetaDataValue` writes the field, not the property** —
  `case SchemaUIdPropertyName: _schemaUId = reader.GetGuidValue(); break;` (`:214-216`). Deserializing
  metadata, and a whole-schema save through
  `Terrasoft.Nui.ServiceModel.WebService/ProcessSchemaManagerService.Post(ContractProcessSchema)` — how
  the designer saves — therefore **bypass the setter and do not synchronize**. The sync on that path
  arrives later, from the read-time `FindDesignItem` / `GetItemFromMetaData` call.
* **The copy constructor fires the setter on a detached element.**
  `ProcessSchemaSubProcess(ProcessSchemaSubProcess source)` assigns `SchemaUId = source.SchemaUId` at
  `:46`, so `Clone()` hits the same NRE as a premature assignment in `Create()` —
  `ProcessSchemaBaseElement(ProcessSchema)` sets only `ProcessSchema`, never `ParentMetaSchema`
  (`:60-63` vs `:69-72`), while `ClearParametersSourceValue` dereferences `ParentMetaSchema.UId` with
  no null guard.
