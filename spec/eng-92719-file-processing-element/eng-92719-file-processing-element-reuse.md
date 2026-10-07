# ENG-92719 File processing element: reuse map

| | |
|---|---|
| Issues | ENG-92719 File processing element (Story), with its sub-tasks ENG-96505 Element readiness and object attachments mode and ENG-96506 Generated report + process parameter modes; ENG-95984 File process parameter type (Task), on which the element depends |
| Epic | ENG-92704 Create BP via AI Toolkit |
| Status | Proposed, 2026-10-01. This is the full evidence behind decision D21 in [decisions](eng-92719-file-processing-element-decisions.md) |
| Baselines | CrtProcessBuilder `main` `3f4cce50` (package 1.6.6.54, also installed on the stand); clio `master` `03ef3944f`; clio-knowledge `master` `d0b5a2b` (guidance libraryVersion 1.15.90); Creatio core 10.1.37 (the stand's core); CrtProcessDesigner 7.8.0 |
| How it was made | Read-only. Every `path:line` below was re-read in those checkouts on 2026-10-01. No stand call was made for this document; measured facts are quoted with their date from the measurements recorded in the sibling documents ([open-questions](eng-92719-file-processing-element-open-questions.md) section C). No code on any branch is used as an argument for an option |

## Summary

Most of what the Process file element and the File parameter type need already exists. The work is to pick
the right piece and to respect its limit. From the **core platform** the builder reuses the data value types
(FileLocator, the generic collection), the parameter sync that runs when an element's `SchemaUId` is assigned,
the template parameter definitions of the three user-task schemas, meta paths, the entity-schema hierarchy, ESQ,
the feature service and the pre-save validation gate. That gate checks formula and mapping types only. It never
checks required parameters, constants or collection shapes, so the builder's own refusals are not optional.
From **CrtProcessDesigner** nothing can be referenced, because CrtProcessBuilder's production code has no
`Terrasoft.Configuration` reference. About twenty constants and seven designer algorithms are therefore mirrored
as a specification, behind a drift guard built on verbatim metadata captures. From **CrtProcessBuilder** the
element skeleton is the Open edit page structure with three `UserTaskSchemaIdentity` instances. Values go through
`ApplyMapping`, filters go through a new target on `ProcessFilterTargetBase`, and refusals and notices use the
existing channels. Four existing pieces must change before they can be reused: the sort codec is private and
worded for Read data, the notice ledger cannot refuse, the constant validator has no record-existence path for a
Guid-typed parameter, and process-parameter addressing is flat-only. **clio** contributes the tool, DTO, floor,
rebundle and e2e patterns, plus `list-printables` as the report discovery hint. The **platform UnitTests**
contribute runtime-contract recipes that are copied as shapes and never referenced. Section 10 lists what is
genuinely new: on the parameter side, the dotted process-parameter resolver and the two-level binder; on the
element side, the family handler and its variant registry, the binder and applier, the storage resolver, the
record-scope filter helper and its target, the end-of-request ledger, the report reader, the feature seam, the
describe block, and the matching clio DTO and e2e fixtures.

---

## How to read this

**Basis labels.** *source* = read in code or metadata; for runtime behaviour this is a hypothesis. *measured* =
observed on the stand or counted over the shipped corpus, with the date. *inference* = a conclusion drawn from
several source facts.

**Verdicts used in every table.**

| Verdict | Meaning |
|---|---|
| **Reuse** | Call it as it is today |
| **Reuse + change** | Call it after the named change, made in the PR named in the row |
| **Pattern** | Copy its structure. The new code is ours, and the original stays untouched |
| **Mirror** | Specification only: a constant or algorithm that lives in code we cannot reference, reproduced in package code and guarded against drift |
| **Avoid** | Do not use it. The row says why |

**Path aliases** (the same as in [decisions](eng-92719-file-processing-element-decisions.md)).

| Alias | Path |
|---|---|
| PB | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/` (https://creatio.ghe.com/engineering/crt-process-builder, `main` `3f4cce50`) |
| PBA | crt-process-builder `packages/CrtProcessBuilder/Files/src/CrtProcessBuilderApp.cs` |
| PBC | crt-process-builder `packages/CrtProcessBuilder/Files/CrtProcessBuilder.csproj` |
| PBT | crt-process-builder `tests/UnitTests/CrtProcessBuilder.Tests/` |
| CLIO | clio repository root (https://github.com/Advance-Technologies-Foundation/clio, `master` `03ef3944f`) |
| KB | clio-knowledge repository root (https://github.com/Advance-Technologies-Foundation/clio-knowledge, `master` `d0b5a2b`) |
| CORE | Creatio core `TSBpm/Src/Lib` at 10.1.37 (`C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib`), the stand's core |
| CORE83 | the 8.3.x/trunk checkout `C:/Projects/Creatio2/TSBpm/Src/Lib`, cited only for test-framework sources |
| GEN | `CORE/Terrasoft.WebApp.Loader/Terrasoft.WebApp/Terrasoft.Configuration/Autogenerated/Src` (compiled configuration of 10.1.37) |
| PD | `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas` (byte-identical to what the stand serves) |
| PS | `C:/Projects/PackageStore` (the shipped corpus) |
| UT | `C:/Projects/UnitTests/ProcessDesigner.UnitTests` (platform configuration unit tests) |

**Ticket aliases** (used in dense tables; prose names the key with its title).

| Alias | Jira issue |
|---|---|
| PT | ENG-95984 File process parameter type |
| FE | ENG-92719 File processing element |
| OA | ENG-96505 Element readiness and object attachments mode |
| RP | ENG-96506 Generated report + process parameter modes |
| SF | NEW Sub-task of FE, "SysFile attachment storage in the Process file element" (key assigned on creation) |
| MH | not created: the conditional Sub-task of PT, "typeFromElement collection mirror leaves its items unbound", waited on measurement M3, and M3 refuted the defect on 2026-10-02 |

**Sibling documents.** [README](README.md) ·
[platform-reference](eng-92719-file-processing-element-platform-reference.md) (what the platform does) ·
[serialization-capture](eng-92719-file-processing-element-serialization-capture.md) (what the designer writes) ·
[use-cases](eng-92719-file-processing-element-use-cases.md) · [traps](eng-92719-file-processing-element-traps.md) ·
[decisions](eng-92719-file-processing-element-decisions.md) (why) · [plan](eng-92719-file-processing-element-plan.md)
(work packages) · [test-plan](eng-92719-file-processing-element-test-plan.md) (cases and the full mocking recipe) ·
[pr-split](eng-92719-file-processing-element-pr-split.md) ·
[open-questions](eng-92719-file-processing-element-open-questions.md) · for the parameter type:
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) and
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md).

---

## 0. Refinements to other documents found while writing

Each one was re-read in source on 2026-10-01. None of them changes a decision; each one changes how a decision
is implemented, or a figure in a sibling document. RF1-RF3 and RF7 are applied in decisions D11, D16 and D21, plan
OA.1, OA.4 and OA.7, and test-plan TC-03 and section 3.5 (2026-10-01).

| # | Where it was stated | Refinement | Basis |
|---|---|---|---|
| RF1 | D21 "Element identity: mirror the three schema UIds"; test-plan TC-03 | Production code does not need the schema **UIds**. `UserTaskSchemaIdentity` resolves the task UId by **name**, once per scope, through `FindInstanceByName` (`PB/Elements/UserTaskSchemaIdentity.cs:48-56`), and every check compares the element's `SchemaUId` with that resolved value (`:71-74`). The generic-route refusal (D13) and the filter-target claim (D16) can compare against the same resolved value. What production code mirrors is the three schema **names** (in `ProcessDesignConstants.UserTasks`, `PB/ProcessDesignConstants.cs:560`). The UIds belong to the test fixtures and the drift guard. | source |
| RF2 | D16 / D21 "Sort: `ReadDataConfigBinder.ResolveOrderInfoValue` / `DescribeSort` as they are" | Both are `private static` (`PB/Elements/ReadDataConfigBinder.cs:793`, `:917`), and their refusals say `Read data '<label>': ...` (`:799-807`, `:822-824`). Reuse needs an extraction into a shared internal codec with the element label injected. The Read data messages must stay byte-identical, or every Read data test that pins them turns red. | source |
| RF3 | D16 "empty-filter policy through the `IDeleteDataNoticeLedger` pattern"; refuse at the end of the request | The ledger reconciles into **notices** only: `Reconcile(schema, notices)` (`PB/Elements/DeleteDataNoticeLedger.cs:46`). A refusing reconcile is new. A precedent for an end-of-request **refusal** exists: `EnsureFormulasStillBelongOnTouchedConnectors`, which runs after the operations and before layout and the pre-save gate, "because it can still refuse the whole edit" (`PB/Design/ProcessEditPipeline.cs:135-139`). Thrown there, outside the operation loop (`:115-129`), the error carries no `failedOperationIndex`, which is exactly what D16 asks for. The build path needs the same call next to `ProcessBuildHandler.cs:547`. | source |
| RF4 | D15 "`EnsureReferencedRecordExists` through a new internal entry point" | `ValidateConstantValue` already has a public overload that takes an `effectiveReference` (`PB/Parameters/ProcessParameterValueValidator.cs:133-134`). Its record-existence check runs only for a **Lookup**-typed target (`:164-180`). `ConnectedObjectId` is Guid-typed, so it takes the scalar branch, which checks the format only (`:212-213`). The new entry point is a thin internal wrapper over the private `EnsureReferencedRecordExists` (`:286`) that the binder calls with the record object. | source |
| RF5 | D21 "Feature reads: `Creatio.FeatureToggling` already referenced" | It is referenced (`PBC:137-141`), but no production file calls it today (grep over `src/**/*.cs`: 0 hits). The first call introduces the seam. The test project pairs that assembly with `Creatio.FeatureToggling.TestKit`, and `PBT/CiContractGuardTests.cs:68-79` guards the version pair. | source |
| RF6 | D17 "`ReportEngine` is configuration code" | Confirmed, and the reference that looks like a way in is not one. `Terrasoft.Reports` is referenced for net472 only (`PBC:87-91`) and holds report-schema classes (`CORE/Terrasoft.Reports/ReportSchema*.cs`), not `ReportEngine`. `ReportEngine` is generated configuration code (`GEN/ReportEngine.Reports.cs:163-186`). | source |
| RF7 | test-plan section 3.5, "`SetupReferencedRecordRead`, around `PBT/ProcessMappingServiceTests.cs:775`" | The helper is defined at `PBT/ProcessMappingServiceTests.cs:99-108`. Its first use is at `:706`. | source |
| RF8 | D11 "a static `FileProcessingConfigBinder` (the OpenEditPage structure)" | `OpenEditPageConfigBinder` is static and receives its collaborators as arguments (`PB/Elements/OpenEditPageElementHandler.cs:97`). The file binder needs at least four collaborators: `IProcessMappingService`, `IAttachmentStorageResolver`, `IReportTemplateReader`, and the notices and ledger. A static binder with that signature works. An injected scoped `IFileProcessingConfigBinder` that both the handler and the applier receive is equivalent and easier to fake. Choose either at implementation; the decision does not depend on it. | inference |

---

## 1. At a glance

| Need | Use | Verdict | Cut | Section |
|---|---|---|---|---|
| File and file-collection types | `DataValueType.FileLocatorDataValueTypeUId`, `FileLocatorDataValueType.FileLocatorDataValueTypeName`, `CompositeObjectListDataValueType` | Reuse | PT | 2.1 |
| The 13 / 8 / 13 element parameters | the platform sync on `SchemaUId` | Reuse | OA, RP | 2.2 |
| Template defaults (`ConsiderTimeInFilter`, `RecordsToRead`, `SerializeToDB`) | `ProcessUserTaskSchema.Parameters` and `.SerializeToDB`, read at run time | Reuse | OA | 2.2 |
| Pre-save validation | `IProcessSchemaValidator` -> `GetProcessValidationResult` | Reuse (does not cover our refusals) | all | 2.5 |
| Element identity of the three schemas | `UserTaskSchemaIdentity` x3 behind a variant registry | Reuse + Pattern | OA, RP | 4.1 |
| Handler, binder and applier skeleton | `OpenEditPageElementHandler` / `OpenEditPageConfigBinder` / `OpenEditPageConfigApplier` | Pattern | OA | 4.1 |
| Value sources (`recordId`, `fileNameSuffix`, `files`) | `IProcessMappingService.ApplyMapping`, `RecordColumnReference` | Reuse | OA, RP | 4.2 |
| Process-parameter items (`Docs.File`) | `ProcessSchemaElementLocator.DescendItemProperties` | Reuse + change (D4) | PT | 4.2 |
| Two-level collection binding | `ApplyMapping` | Reuse + change (D5, new `BindCollection`) | PT | 4.2 |
| File-collection declaration and the mirror | ENG-96230 Collection process parameter type code in `ProcessParameterService` | Reuse + change (D1, D2, D6, D7, D9) | PT | 4.3 |
| Filter serialisation and decode | `ProcessFilterService.BuildFilterValue`, `FilterDescriptorReader`, `ProcessFilterTargetBase` | Reuse | OA, RP | 4.4 |
| Filter root rule | `SignalStartFilterTarget` object-equals-root check | Pattern | OA, RP | 4.4 |
| "Has a selecting filter" | `DataSourceFilterValue.HasStoredFilter` / `HasFilterConditions` | Reuse | OA, RP | 4.4 |
| Sort | `ReadDataConfigBinder.ResolveOrderInfoValue` / `DescribeSort` | Reuse + change (RF2) | OA | 4.4 |
| Per-file sub-process | `MultiInstanceApplier` and the platform's `FillCollectionParameters` | Reuse | OA (after PT) | 4.5 |
| End-of-request refusal | the `IDeleteDataNoticeLedger` pattern plus the formula end-check | Pattern + change (RF3) | OA, RP | 4.6 |
| Record id constant must exist | `ProcessParameterValueValidator.EnsureReferencedRecordExists` | Reuse + change (RF4) | OA | 4.6 |
| Printable resolution by id, macro or caption | `EmailTemplateResolver` | Pattern | RP | 4.6 |
| Report lookup | ESQ on `SysModuleReport`, query shape from `GEN/ReportEngine.Reports.cs` | Mirror (query) + Reuse (ESQ) | RP | 2.4 |
| Attachment storage per object | designer algorithm over `EntitySchemaManager` and `SchemaManager.GetAllParents` | Mirror + Reuse | OA, SF | 2.4, 3.3 |
| Feature flags (notice only) | `Creatio.FeatureToggling.Features.GetIsEnabled(string)` | Reuse behind a seam | RP, SF | 2.6 |
| Configuration constants (`ResultActionType`, `FileConsts`, SysFile, BaseFile, ...) | literals in `ProcessDesignConstants` | Mirror | OA, RP, SF | 3.2 |
| clio describe of the block | `DescribedElement` bag + a typed DTO with a bag on every nested type | Pattern | OA, RP | 5.1 |
| clio e2e | `ProcessDesignerE2EArrange`, `DescribedProcessGraph` | Reuse | PT, OA, RP, SF | 5.2 |
| Report discovery | `list-printables` (`templateId`) | Reuse (hint only) | RP | 5.3 |
| Platform runtime semantics in tests | `UT/*FileProcessing*_Tests.cs` | Pattern (copy shapes) | all | 7 |

---

## 2. Core platform

Everything here lives in `Terrasoft.Core`, `Terrasoft.File(.Abstractions)` or `Creatio.FeatureToggling`, all
referenced by the package (`PBC:137-141, 162-166, 207-216`).

### 2.1 Types and values

| What | Where (CORE 10.1.37) | How the builder uses it | Limits |
|---|---|---|---|
| `DataValueType.FileLocatorDataValueTypeUId` `A33C9252-D401-453E-949D-169157067ED9` | `Terrasoft.Core/DataValueType.cs:297` | **Reuse.** The type of a File parameter and of every file item; it is also what the D7 constant refusal compares against | none |
| `FileLocatorDataValueType` (name `FileLocator`, `ValueType = IFileLocator`, client `FILE_LOCATOR`) | `DataValueType.cs:3003-3030` (`:3017`, `:3027`, `:3030`) | **Reuse.** `FileLocatorDataValueType.FileLocatorDataValueTypeName` is what the D1 aliases (`file`, `filelocator`, `file locator`) return from `NormalizeParameterTypeName` (`PB/Parameters/ProcessParameterService.cs:1033-1078`) | Never pass the friendly name `File` through. `DataValueTypeManager` maps the NAME `File` to the BLOB type `BA40CFC5-...` ("File (BLOB)", `Terrasoft.Core/DataValueTypeManager.cs:341-349`), which no designer page offers |
| `CompositeObjectListDataValueType` `651EC16F-...` | `DataValueType.cs:291` (UId), `:2935` | **Reuse.** Already used by the ENG-96230 Collection process parameter type code (`ProcessParameterService.cs:1035-1036`). A FileCollection is this type with one FileLocator item | The type check does not compare item shapes (`PB/Mappings/ParameterTypeCompatibility.cs:240-242`), which is why D5 adds R-M2 |
| `BinaryDataValueTypeUId` `B7342B7A-...` | `DataValueType.cs:255` | **No code use.** The D1 refusal keys on the names `binary` / `blob`, before any type is resolved ("a process parameter carries a file by reference, not its bytes") | Binary cannot hold a process value (D1) |
| `DataValueTypeManager` FileLocator registration | `DataValueTypeManager.cs:501-508`; `IFileLocator -> FileLocatorDataValueType` at `:612-613` | **Reuse.** A `new DataValueTypeManager()` registers FileLocator and the collection type with no database, which is what makes them usable in package tests (`PBT/ProcessDesignTestSupport.cs:224`) | none |
| `IFileLocator`, `EntityFileLocator(EntitySchemaName, RecordId)` | `Terrasoft.File.Abstractions/IFileLocator.cs:10`; `Terrasoft.File/EntityFileLocator.cs:12` | **No design-time use.** The builder never creates a locator; locators exist only at run time. The locator's schema is the FILE object (`ContactFile`, `SysFile`), which is why describe calls that object `fileObject` | The D7 refusal can test `DataValueTypeUId == FileLocatorDataValueTypeUId`; `typeof(IFileLocator)` works too (`PBC:212-216`), but the UId check is simpler |

### 2.2 User-task schema, parameter sync, template definitions

| What | Where | How the builder uses it | Limits |
|---|---|---|---|
| `ProcessUserTaskSchemaManager.FindInstanceByName` / `FindItemByUId` | used at `PB/Elements/UserTaskElementHandler.cs:76`, `:127` | **Reuse.** Create resolves the variant's schema by name, and describe reads `userTaskName` back from the element's `SchemaUId` | A missing schema throws "was not found" (`:77-79`); keep that message |
| `ProcessSchemaUserTask.SchemaUId` setter -> `SynchronizeParameters` | `Terrasoft.Core/Process/ProcessSchemaUserTask.cs:105-115` (call at `:113`), user-task part `:273-287` | **Reuse.** Assigning `SchemaUId` creates the 13 / 8 / 13 parameters with their types, directions and nested items. The handler never creates a configuration parameter by hand (D20) | **Not unit-testable**: the sync stops at the config-backed `DesignModeClassResolver` (`PBT/UserTaskElementHandlerCreateTests.cs:118-125`), so package tests hand-build the synced set (section 4.8) |
| The diff: `GetRemovedSchemaParameters`, `UpdateParameters`, `FillNewSchemaParameters`, `CreateElementParameterFromUserTaskSchemaParameter` | `Terrasoft.Core/Process/ProcessSchemaActivity.cs:253`, `:238`, `:291-305`, `:308-322`; entry `:587-599`; gate `GetCanSynchronizeParameters` `:324-326` | **Reuse.** It runs again on every design-time load (`GetDesignInstance`), so `setElement` always edits a converged set and an older element's drift is repaired before our binder sees it (source; every drift state in the 30 shipped elements is one the diff repairs, measured 2026-10-01, see [serialization-capture](eng-92719-file-processing-element-serialization-capture.md)) | Top-level parameters only: nested items keep the **template's** UIds and an empty `IL2`, hence D20's re-mint on create. A server-created parameter takes `DefValueForExistingProcess` (GS8, `:315`), so `ConsiderTimeInFilter` becomes `"false"`, not the designer's Script `"true"` (pending M14). Describe of a compiled process may be **unconverged** (source; M13 measures which case the stand serves); the describe reader must tolerate a missing parameter and a Lookup-typed `ConnectedObjectId` |
| Template parameter definitions (`ProcessUserTaskSchema.Parameters`, each `SourceValue` with `Source`, `Value`, `ModifiedInSchemaUId`, `DefValueForExistingProcess`) | `ProcessSchemaParameterValue.cs:21-37` (GS8); template values in `PD/<Task>/metadata.json` `FJ1` | **Reuse.** D20 copies the template value **from the template object at run time** (`ConsiderTimeInFilter` Script `"true"` with GS5 = template UId; `RecordsToRead` 50) instead of hard-coding it. Precedent for pinning a template default: `ChangeDataConfigBinder.PinIgnoreErrorsDefault` (`PB/Elements/ChangeDataConfigBinder.cs:943-948`) | In tests the task schema is a substitute, so `FileProcessingTestSupport.RegisterFileTaskSchemas` must give it the template parameters ([test-plan](eng-92719-file-processing-element-test-plan.md) section 3.3) |
| `ProcessUserTaskSchema.SerializeToDB` (`FK4`) and element `ProcessSchemaFlowNode.SerializeToDB` (`BO2`) | `ProcessUserTaskSchema.cs:86`, `:286`; `ProcessSchemaFlowNode.cs:20`, `:76` | **Reuse.** The handler sets `element.SerializeToDB = taskSchema.SerializeToDB` (designer parity, D20). The generic route lacks it today for every user task (side Sub-task) | none |
| `ProcessSchemaActivity.FillCollectionParameters` | `ProcessSchemaActivity.cs:336-352` | **Reuse**, indirectly. A multi-instance sub-process whose callee declares a File parameter gets a FileLocator item in `InputRecordCollection` with no new code (section 4.5) | Needs PT (a callee File parameter) |

### 2.3 Meta paths, localizable values, mapping rows

| What | Where | How the builder uses it | Limits |
|---|---|---|---|
| `ProcessSchemaParameter.GetMetaPath()` | `Terrasoft.Core/Process/ProcessSchemaParameter.cs:1097-1110` | **Reuse** through `ProcessMappingService.BuildSourceValue` (`PB/Mappings/ProcessMappingService.cs:275, 283`). An element item gets `[Element:{el}].[Parameter:{item}]` when its `ContainerUId` is set (the locator backfills it, `PB/ProcessSchemaElementLocator.cs:135`); a process-level item keeps `[Parameter:{item}]` | D4: the process-parameter resolver must **not** backfill `ContainerUId`, or the shipped short form is lost |
| `SourceValue` setter (refreshes the schema `Mappings` row for an element target) | described at `ProcessMappingService.cs:57-62` | **Reuse.** Always assign a NEW `ProcessSchemaParameterValue`, never mutate one in place | No `Mappings` row is created for a nested item; this is runtime-neutral (6 shipped cases, measured 2026-10-01) |
| Localizable constant routing (`Value` setter -> `LocalizableValue`) and `schema.InitializeLocalizableValues()` | `ProcessSchemaParameterValue.cs:221-230`; called at `PB/Design/ProcessBuildHandler.cs:156` and `PB/Design/ProcessEditPipeline.cs:145` | **Reuse.** A constant `report.fileNameSuffix` written through `ApplyMapping` lands in the resource key `BaseElements.<El>.Parameters.ReportName.Value`, the designer's shape (D17) | Measured for a LocalizableString parameter only; for the Text-typed `ReportName` it is basis=source. Culture behaviour is M18 |

### 2.4 Entity schemas and reports

| What | Where | How the builder uses it | Limits |
|---|---|---|---|
| `EntitySchemaManager.FindItemByName` / `FindItemByUId` / `FindInstanceByUId` | used by `PB/EntitySchemaResolver.cs:26-50` | **Reuse** inside the new `IEntitySchemaHierarchyReader` adapter: name, UId, `IsVirtual`, `IsDBView`, columns and their reference schema | `FindInstanceByUId` loads the full schema; use it only when columns are needed, as `EntitySchemaResolver.FindByUId` already says (`:34-37`) |
| `SchemaManager.GetAllParents(item)` (nearest parent first, excludes self, handles replacements) | `Terrasoft.Core/SchemaManager.cs:4076-4097` | **Reuse** inside the adapter, for "descends from BaseFile / SysFile" and the tag/folder exclusion (D15) | **Non-virtual**: it cannot be substituted, so the resolver logic sits behind `IEntitySchemaHierarchyReader` and is tested with a hand-written fake ([test-plan](eng-92719-file-processing-element-test-plan.md) section 3.5) |
| `EntitySchemaQuery` with `UseAdminRights = false` | core ESQ | **Reuse** for `IReportTemplateReader`: columns `Id`, `Caption`, `Type.Name`, `SysEntitySchema.Name`, `SysModule.SysModuleEntity.[SysSchema:UId:SysEntitySchemaUId].Name`, `ConvertInPDF`. Same rights model and same culture as the runtime (D17) | The query shape is **mirrored** from configuration code (`GEN/ReportEngine.Reports.cs:163-186`, with the module-entity fallback at `:180-186`). There is no code column on `SysModuleReport`, so callers pass an id, a lookup macro or a caption |

### 2.5 Validation seams

| What | Where | What it gives the builder | What it does NOT give |
|---|---|---|---|
| `IProcessSchemaValidator.EnsureValidForSave` -> `ProcessSchemaManager.GetProcessValidationResult` | `PB/Validation/ProcessSchemaValidator.cs:56-66` (fails closed on no verdict); `CORE/Terrasoft.Core/Process/ProcessSchemaManager.cs:420-425`; registered `PBA:162` | **Reuse, unchanged.** Every build and modify already runs it before save (`PB/Design/ProcessBuildHandler.cs:160`, `PB/Design/ProcessEditPipeline.cs:148`) | It is the only platform gate the package runs; nothing else validates a schema before it is saved |
| `ParameterValuesValidationRule` | `Terrasoft.Core/Process/ParameterValuesValidationRule.cs:525-552` (`Validate`); source switch `:508-517` | A `Script` or `Mapping` value onto `Files.File` or `ConnectedObjectId` is type-checked. The platform's own test asserts that an int formula into a FileLocator parameter is an error (`CORE83/Terrasoft.Core.Process.Tests/ParameterValuesValidationRule.Tests.cs:105-117`). That covers the D5 / D18 `expression` source, which our rules write verbatim | **ConstValue sources are skipped** (default arm, `:515-516`), and there is **no required-parameter rule**: none of `ParameterValuesValidationRule`, `ParameterConstValuesValidationRule` or `ProcessSchemaValidationRule` reads `IsRequired` (grep, 0 hits each). An unconfigured element, a constant onto a file input, an empty `Files`, a wrong storage pair and an empty filter all save green. That is why D7, D13 to D18 refuse in the builder |
| `ParameterConstValuesValidationRule` | `ParameterConstValuesValidationRule.cs:11`, gated by `GlobalAppSettings.FeatureUseProcessParameterProviderInExpressionValidation` at `:95` | nothing we can rely on | Gated by a setting; treat it as absent |
| Test seam | `PBT/ProcessDesignerRoundTripTests.cs:94-97` substitutes `IProcessSchemaValidator` | **Reuse** the substitution. The real gate is reached only on the stand (e2e and D29) | none |

### 2.6 Feature reads

| What | Where | How the builder uses it | Limits |
|---|---|---|---|
| `Creatio.FeatureToggling.Features.GetIsEnabled(string)` | assembly referenced at `PBC:137-141` | **Reuse behind a one-method seam** (`IFileProcessingFeatureReader`, new). It feeds the D17 notice (`EnableReportFileProcessingUserTask` off) and the SF notice (`ProcessFeatures.UseSysFileInObjectFileProcessing` off). It is **never a refusal**: the server runtime reads neither flag | The SysFile flag has no `Feature` row on the stand and is code-defined (`Terrasoft.Core/Process/ProcessFeatures.cs:252-268`). Whether the server read returns the code default (true) is **M25**; the SF notice ships only after it |
| `ProcessFeatures` | `ProcessFeatures.cs:13` (`internal class`) | **Avoid**: unreachable by type | - |

---

## 3. CrtProcessDesigner: mirror as specification

### 3.1 Why nothing is referenced

- The package's production project has no `Terrasoft.Configuration` reference (`PBC`: none), and
  `packages/CrtProcessBuilder/descriptor.json` declares `"DependsOn": []`. basis=source.
- The TEST project references `Terrasoft.Configuration.dll` unconditionally (`PBT/CrtProcessBuilder.Tests.csproj:72-74`).
  The `dev-nf` build (CI) makes a missing DLL an error. The `dev-n8` build (macOS) demotes it to a message and
  builds without the DLL (crt-process-builder `.build-props/env.dev-n8.props`, `.build-props/env.dev-nf.props:13-20`). So test
  code that names `ResultActionTypeEnum`, `FileConsts` or a user-task class breaks `dev-n8`. Tests mirror the same literals as production.
- The enum lives in namespace `Terrasoft.Configuration` (`PD/FileProcessing/FileProcessing.cs:1`), so every
  configuration-level value below is a literal in `PB/ProcessDesignConstants.cs`, beside the existing families
  (`ElementTypes` at `:457`, `UserTasks` at `:560`).

### 3.2 Constants to mirror

| Constant | Value | Source of truth | Used by (new code) | Production or tests |
|---|---|---|---|---|
| Schema names | `ObjectFileProcessingUserTask`, `ProcessFileProcessingUserTask`, `ReportFileProcessingUserTask` | `PD/<Task>/descriptor.json` | variant registry identities (RF1), generic-route refusal, refusal texts | production |
| Schema UIds | `9387c794-8d84-5925-ab77-c47e7d876286`, `6c620dd2-026e-560c-489f-030c5be5f2c3`, `c2bf0416-54c6-6c56-58e0-41162c7795f0` | `PD/<Task>/metadata.json`; corpus `J6` = `BL7` on 30 of 30 elements (measured 2026-10-01) | `FileProcessingTestSupport`, drift guard | tests only (RF1) |
| Parameter names (13 / 8 / 13) | e.g. `SourceEntitySchemaUId`, `SourceDataEntitySchemaUId`, `TargetEntitySchemaUId`, `TargetDataEntitySchemaUId`, `ConnectedObjectId`, `ConnectedObjectColumnUId`, `ResultActionType`, `RecordsToRead`, `OrderByInfo`, `DataSourceFilters`, `ConsiderTimeInFilter`, `ReportId`, `IsSeparateReports`, `ReportName`, `ReportNameDataSourceColumnUId`, `Files`, `ObjectFiles`, `ReportFiles`, `CreatedObjectFileIds` | `PD/<Task>/metadata.json` `FJ1`; the full table with UIds, types and directions is in [platform-reference](eng-92719-file-processing-element-platform-reference.md) section 3 | binder, applier, describe, raw-mapping refusal | production (names); tests use their own literals, never the production constants (`PBT/DeleteDataConfigBinderTests.cs:45-46` states the rule) |
| Nested item names | `File` (Object `ObjectFiles`, Report `ReportFiles`, Process `Files`), `ObjectFile` (Process `ObjectFiles`), `Id` (`CreatedObjectFileIds`) | metadata `L18`; runtime keys `PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:64`, `PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:40, 73` | refusal texts and tests | prefer reading the item from the synced element; D18 says "resolved, never assumed" |
| `ResultActionType` | `SaveToFiles = 0`, `UseInProcess = 1` (implicit ordinals); the designer writes the ConstValue strings `"0"` / `"1"` | `PD/FileProcessing/FileProcessing.cs:19-30`; client `PD/ProcessUserTaskConstants/ProcessUserTaskConstants.js:167-170` | binder (D14), describe | production |
| `FileConsts.FileTypeUId` | `529BC2F8-0EE0-DF11-971B-001D60E938C6` ("File"; links are never read) | `CORE/Terrasoft.WebApp.Loader/Terrasoft.WebApp/Terrasoft.Configuration/Pkg/CrtBaseConsts/Autogenerated/Src/FileConsts.CrtBaseConsts.cs:13` | describe issues and guidance only (D21) | production |
| SysFile | `70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a`; columns `RecordId`, `RecordSchemaName` resolved **by name**, never by UId | `PS/CrtCoreBase/branches/7.8.0/Schemas/SysFile/descriptor.json`; `PS/CrtPlatform7x/branches/7.8.0/Schemas/SectionDesignerEnums/SectionDesignerEnums.js:155`; the designer finds `RecordId` by name (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:527-541`) | storage resolver (OA decodes it, SF writes it) | production |
| BaseFile ("File") | `556c5867-60a7-4456-aae1-a57a122bef70` | `PS/CrtCoreBase/branches/7.8.0/Schemas/File/descriptor.json`; `SectionDesignerEnums.js:119` | storage resolver | production |
| Tag and folder bases excluded from the object list | BaseFolder `d602bf96-d029-4b07-9755-63c8f5cb5ed5`, BaseItemInFolder `4f63bafb-e9e7-4082-b92e-66b97c14017c`, BaseTag `9e3f203c-e905-4de5-9468-335b193f2439`, BaseEntityInTag `5894a2b0-51d5-419a-82bb-238674634270` | `SectionDesignerEnums.js:115, 127, 131, 135`; the filter in `PD/EntitySchemaDesignerUtilities/EntitySchemaDesignerUtilities.js:402-407` | storage resolver eligibility (D15) | production |
| `<X>File` naming rule and the `FileLead` exception | `relatedName = name == "FileLead" ? "Lead" : name without the trailing "File"` | `EntitySchemaDesignerUtilities.js:412-458` (exception at `:426-428`) | storage resolver | production |
| Designer link column | the file-object column named like the record object AND referencing it | `findConnectedColumn`, `EntitySchemaDesignerUtilities.js:526-549` | storage resolver (D15 R8, W6) | production |
| Report type names | `"MS Word"` and `"FastReport"` run; `DevExpress` throws `NotSupportedException` at run time | dispatch by `Type.Name`, `GEN/ReportEngine.Reports.cs`; enum `GEN/IReportEngine.CrtBase.cs:81-95` | report reader and the D17 refusals | production |
| MS Word printable type Id | `8bc259ef-4276-4906-b7a6-23dc59be7fe2` | `CLIO/clio/Command/ListPrintablesCommand.cs:78` (clio's own constant) | not needed by the package (it matches by `Type.Name`); e2e only | tests only |
| Feature codes | `ProcessFeatures.UseSysFileInObjectFileProcessing`, `EnableReportFileProcessingUserTask` | `BaseFileProcessingUserTaskPropertiesPage.js:17`, `:496` | feature seam (notices only) | production |
| `OrderByInfo` format | `<Column>:<1 asc / 2 desc>:<position>`, primary entry position 1 | existing `ReadData` constants via `ReadDataConfigBinder.cs:810, 813-825` | sort codec (RF2) | already in the package |
| `RecordsToRead` range and default | 1..5000; template default 50 | validator `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:353-357` -> `validateRowsCountRange` (`PD/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:476-477`: min 1, max 5000); template `PD/ObjectFileProcessingUserTask/metadata.json` | binder (D16) | production (range); the default is read from the template (2.2) |
| Nested-source display value | `[#<element caption>.<collection caption>:<item caption>#]`, or `[#<parameter caption>:<item caption>#]` for a process parameter | corpus `PS/ProcessTests/branches/7.8.0/Resources/FileParameterProcess.Process/resource.en-US.xml` | `ProcessMappingService.BuildSourceValue` element arm (D20) | production |

### 3.3 Designer algorithms to reproduce

| Algorithm | Designer source | Reproduced in | Note |
|---|---|---|---|
| Storage per object: dedicated `<X>File` keeps the legacy encoding; any other eligible object uses SysFile + `RecordId` + `*DataEntitySchemaUId` | `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:502-560, 776-780`; `EntitySchemaDesignerUtilities.js:314-325, 364, 412-458, 526-549, 622` | `IAttachmentStorageResolver.Resolve` / `Decode` (OA: dedicated mode; SF: SysFile mode) | Measured in the designer, in memory: UO-3 (SysFile) and UO-4 (legacy), 2026-10-01. Algorithm in [platform-reference](eng-92719-file-processing-element-platform-reference.md) section 7.3 |
| Source change = replace the element (same UId, position and caption; new name; values dropped), refused while it has dependents | `BaseFileProcessingUserTaskPropertiesPage.js:242-311` | `FileProcessingConfigApplier` refuses instead (D12) | Refusal, not reproduction |
| Process variant `Files` binding: nested item; outer level too when the source is a collection item | `PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:44-79, 117-136`; `PD/MappingEditMixin/MappingEditMixin.js:895-905, 947-995, 1075-1096, 1132-1156` | P1 / P2 in `ApplyMapping` (PT) and the `files` binder (RP) | corpus: 11 of 11 file-collection bindings bind both levels (measured 2026-10-01) |
| MS Word forces `IsSeparateReports = true` and disables the checkbox | `PD/ReportFileProcessingUserTaskPropertiesPage/ReportFileProcessingUserTaskPropertiesPage.js:153-163` | binder: written `true` when omitted; an explicit `false` is accepted with a notice (D17) | server does not enforce it (`PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:182-188`) |
| Process variant hides "What to do with file?" and forces `"0"` | `ProcessFileProcessingUserTaskPropertiesPage.js:174-176`; runtime `ProcessFileProcessingUserTask.cs:89-97` | binder refuses `useInProcess` (D14) | - |
| Retarget clears filter, sort and name column | `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:184-202, 228-248`; `ReportFileProcessingUserTaskPropertiesPage.js:204-212, 352-356` | applier rule 2 (D12), with the deliberate same-object exception for reports | - |
| Report file name: `caption[. ReportName (no column)][ (i) / . <column value> (separate)] + extension` | `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:53-58, 156-175` | guidance and describe wording only; the builder writes the inputs | pinned by `UT/ReportFileProcessingUserTask_Tests.cs:215-320` |

### 3.4 Designer behaviour deliberately not mirrored

| Behaviour | Where | Why not |
|---|---|---|
| The SysFile-mode sort list offers columns of the RECORD object | `ObjectFileProcessingUserTaskPropertiesPage.js` (UO-1 "GPS E") | the runtime query is rooted on SysFile; predicted column-not-found (D16 V9, basis=source) |
| H-G3-1: an unchanged open-and-save of a legacy Object element writes `SourceDataEntitySchemaUId = <X>File` | `BaseFileProcessingUserTaskPropertiesPage.js:853-861, 910-922`; `ObjectFileProcessingUserTaskPropertiesPage.js:186, 319-328, 379-391` | predicted run-time throw; the builder repairs on touch (D12 rule 5), pending M11 |
| The flag-OFF object list built from `ModuleStructure` | `EntitySchemaDesignerUtilities.js:250-292, 509-515` | the runtime never reads the flag; D15 option A |
| "Use in process" as the page default for Object and Report | `BaseFileProcessingUserTaskPropertiesPage.js:11-12, 894-903` | D14 infers the action from `saveTo` on create |
| `saveToAttachments` -> `useInProcess` forgets `TargetDataEntitySchemaUId` | `BaseFileProcessingUserTaskPropertiesPage.js:380-389` | D12 rule 4 clears all four target fields |

### 3.5 Drift guard

A mirrored constant without a guard is a second, unverified copy of the platform contract. Guard it in layers.
The cases are TC-01 to TC-03 in the [test-plan](eng-92719-file-processing-element-test-plan.md).

| Layer | What it guards | How | Basis |
|---|---|---|---|
| 1. Verbatim metadata captures | parameter names, UIds, types, directions, required flags, template values, nested items of the three schemas | Copy `PD/<Task>/metadata.json` into `PBT/Fixtures/FileProcessing/` with a `PROVENANCE.md` that records the source path, the CrtProcessDesigner version (7.8.0) and SHA-256. Read them by ancestor search, as `PBT/PackageDescriptorTests.cs:58-70` reads `descriptor.json`. Parse with the platform reader: one `Read()`, no `ReadInto()` (`PBT/ProcessParameterServiceItemPropertiesTests.cs:63-85`). Compare with `FileProcessingTestSupport.Template(...)` | source (design) |
| 2. Entity UIds | SysFile and BaseFile UIds | Copy `PS/CrtCoreBase/branches/7.8.0/Schemas/{SysFile,File}/descriptor.json` into the same fixture folder and assert the mirrored literals against them | source |
| 3. Literal pins | `ResultActionType` 0/1, `FileConsts.FileTypeUId`, item names | One test per literal (TC-03). It catches an accidental edit of the production constant, not a platform change | source |
| 4. Optional reflection probe (proposal) | `ResultActionTypeEnum` ordinals and `FileConsts.FileTypeUId` in the real configuration | Under `dev-nf` the test output carries `Terrasoft.Configuration.dll` (the reference has no `Private=False`, `PBT/CrtProcessBuilder.Tests.csproj:72-74`). `Type.GetType("Terrasoft.Configuration.ResultActionTypeEnum, Terrasoft.Configuration")` compares the ordinals and calls `Assert.Ignore` when the type is absent (`dev-n8`), so nothing names the type at compile time | inference; check on the first CI run that the DLL is really in the output |
| 5. Refresh rule | captures moving with the stand | When `list-packages` shows a new CrtProcessDesigner on the stand, re-copy the three files from the matching PackageStore branch and re-pin `PROVENANCE.md` in one commit; the guard then shows the delta | process |

---

## 4. CrtProcessBuilder

### 4.1 Element family skeleton

| What | Where | Verdict and how | Limits |
|---|---|---|---|
| `UserTaskSchemaIdentity` | `PB/Elements/UserTaskSchemaIdentity.cs:20-74` | **Reuse**, three instances, one per schema name, held by a new variant registry (`IFileProcessingVariant` keyed by schema UId). The registry feeds the five consumers that must agree: `CanBuild` / `CanDescribe`, the generic-route refusal, the raw-mapping refusal, the filter-target claim and the describe block (pr-split T3) | `IsTaskSchemaResolvable` (`:64`) separates "wrong element kind" from "this environment lacks the task"; refusal texts must use it |
| `OpenEditPageElementIdentity` | `PB/Elements/OpenEditPageElementIdentity.cs:18-72` | **Pattern** for a named identity class wrapping the predicate | - |
| `OpenEditPageElementHandler` | token `:53`; `Create` `:63-86` (`SchemaUId` at `:74`, `ManagerItemUId = taskSchema.UId` at `:78`); `Configure` `:89-103`; `CanBuild` / `CanDescribe` `:106-111`; `Describe` contributes only its own block `:119-123` | **Pattern** for `FileProcessingElementHandler`. Configuration goes in `Configure`, not `Create`, because a value source may name an element placed later in the request (comment `:79-84`). `ManagerItemUId` is set directly, as here, which gives the diagram's `BL7 = SchemaUId` (corpus 30 of 30) | The handler adds what OpenEditPage does not: `SerializeToDB` (2.2) and the D20 re-mint of nested item UIds, before `Configure` |
| `OpenEditPageConfigBinder` (`Apply` `:57`, `Describe` `:225`) and `OpenEditPageConfigApplier.ApplyOpenEditPageConfig` | `PB/Elements/OpenEditPageConfigBinder.cs`; `PB/Operations/OpenEditPageConfigApplier.cs:38-74` | **Pattern**: one binder shared by build (`Configure`) and modify (`setElement`), validate before the first write, clear the branch being left only after the write succeeded (`:70-73`), require the new payload with a destructive retarget (`:62-65`) | RF8: the file binder has more collaborators |
| `UserTaskElementHandler.Create` and `ResolveUserTaskName` | `PB/Elements/UserTaskElementHandler.cs:74-90`, `:161-182` | **Reuse + change** (D13): the generic-route refusal goes right after `FindInstanceByName` (`:76-79`), keyed on the resolved schema UId, so case, whitespace and the `performTask + userTaskName` alias are covered. `Describe` (`:125-151`) keeps filling `userTaskName` for every user task | `HasDedicatedPaletteElement` (`:329-339`) is private; the dedicated handler does not need it (it always sets `ManagerItemUId`) |
| `ProcessElementFactory` | token map `:30-38` (a later registration of the same token overwrites); unknown type `:57-66`; strict block gate `:100-113` via `EnsureBlockMatchesHandler` `:145-154`; size and `IsLogging = true` `:114-125`; `ResolveBuildType` = first `CanBuild` handler's first token `:132-133` | **Reuse**: one more strict gate line for `fileProcessing`; `fileprocessing` listed first in `SupportedTypes` and `processfile` second, so describe emits one spelling (D10) | Without the strict gate, an older server silently drops the block on a known type |
| Composition root and tripwire | handlers `PBA:124-151` ("MUST precede `UserTaskElementHandler`"); tripwire `PBT/CrtProcessBuilderAppTests.cs:139-199` (set via `BeEquivalentTo` `:148`, order via `ContainInOrder` `:171`) | **Reuse**: register before `UserTaskElementHandler`, extend both assertions | The tripwire pins the set and four pairwise orders, not a full order (source) |
| Forward-reference guard and the `setElement` field list | `PB/Graph/ProcessGraphBuilder.cs:442-451` ("EXTEND THIS"); `PB/Operations/ElementOperations.cs:278-290` | **Reuse + change**: add `fileProcessing` to both | Forgetting the first leaves forward references unguarded, silently |
| Layout constants | `PB/ProcessDesignConstants.cs:1887-1888` (`TaskWidthPx = 69`, `TaskHeightPx = 55`) | **Reuse** as the handler's `DefaultSize` | - |

### 4.2 Value sources and mappings

| What | Where | Verdict and how | Limits |
|---|---|---|---|
| `IProcessMappingService.ApplyMapping` | `PB/Mappings/IProcessMappingService.cs:13-22`; `PB/Mappings/ProcessMappingService.cs:48-63` | **Reuse** for every value input of the element: `ConnectedObjectId` (`saveTo.recordId`), `ReportName` (`report.fileNameSuffix`), `Files` / `Files.File` (`files`). Five sources in one place (`BuildSourceValue` `:259-329`): element output (`:270-278`), process parameter (`:279-286`), formula (`:287-301`), constant (`:302-324`), record column (`:345`). Precedents: `PB/Connections/EntityConnectionBinder.cs:110-118`, `PB/Approval/ApprovalApplier.cs:840-849` | (a) The constant arm's Lookup check looks for a `SysSchema` RECORD, so it cannot write a schema UId: the storage parameters (`*EntitySchemaUId`, `*DataEntitySchemaUId`, `ConnectedObjectColumnUId`, `ResultActionType`, `RecordsToRead`, `OrderByInfo`, `ReportId`, `IsSeparateReports`, `ReportNameDataSourceColumnUId`) are written directly as ConstValue, the way `DeleteDataConfigBinder.Apply` writes its object (`PB/Elements/DeleteDataConfigBinder.cs:39-44`). (b) `EnsureCompatibleTypes` (`:453-461`) accepts ANY Lookup or Guid into the Guid-typed `ConnectedObjectId`, because a Guid target has no reference constraint (`PB/Mappings/ParameterTypeCompatibility.cs:208-220`). The D16 V3 "references the record object" check is the binder's own. (c) A constant `ConnectedObjectId` takes the format-only Guid branch (RF4). (d) The display value of an element source is the bare caption (`:276`, `:284`); D20 changes it for nested sources |
| Type compatibility | `PB/Mappings/ParameterTypeCompatibility.cs:240-242` | **Reuse**: FileLocator and the collection type fall through to an exact-UId match, so FileLocator -> FileLocator passes and File (BLOB) or Binary -> FileLocator is refused. No change | Item shapes are not compared (D5 R-M2 is new) |
| Dotted element paths | `PB/ProcessSchemaElementLocator.cs:121` (`ResolveElementParameter`), `:135` (`...ForMapping`, backfills `ContainerUId`), `:166-241` (`DescendItemProperties`) | **Reuse** for `OF1.ObjectFiles.File` and `PF1.Files.File` on both sides of a mapping; pinned by `PBT/MultiInstanceMappingTests.cs` | Element parameters only |
| Process-parameter addressing | `ProcessMappingService.ResolveProcessParameter` `:425-435` (flat only), callers `:221`, `:280` | **Reuse + change** (D4, PT): refactor `DescendItemProperties` to take a root parameter collection and an owner label; add `ResolveProcessParameterPath` with **no** `ContainerUId` backfill | Shipped content binds `$FileCollection.FileCollectionParameter` (`PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:47`), which is not expressible today except as a raw `expression` |
| Describe decode of a single token | `ProcessSchemaElementLocator.TryNameNestedParameter` `:285-297`; `RecordColumnReference.TryDecode` `PB/Mappings/RecordColumnReference.cs:257-266` | **Reuse**: the D8 single-token element decode (PT) and the block's own value-source decode (D19) | A process-parameter twin of `TryNameNestedParameter` is new (D8 follow-up Sub-task; the element block decodes its own bindings without it) |
| ENG-91844 Implement full parameter mapping (sources) | `RecordColumnReference` `PB/Mappings/RecordColumnReference.cs:42-236` (`ResolveRecordColumn` `:74`, `BuildReference` `:220`, `FindRecordSchema` `:236`) | **Reuse** through `ApplyMapping`'s `sourceColumn` arm, for example `saveTo.recordId {sourceElement: RD1, sourceElementParameter: ResultEntity, sourceColumn: Account}` | none |
| Value-source descriptor member names | `OpenEditPageRecordDescriptor` `PB/Contracts/ProcessDescriptorContracts.cs:930`; single-source rule of `ApprovalRecordDescriptor` `:525` | **Pattern** for the shared `FileProcessingValueSourceDescriptor` (`value`, `processParameter`, `sourceElement`, `sourceElementParameter`, `sourceColumn`, `expression`) | No shared base type exists (the only contract inheritance is `FilterDescriptor : FilterGroupDescriptor`, `PB/Contracts/FilterContracts.cs:66`); the new type is a copy of the member set |
| Two-level collection binding | `ApplyMapping` (`:48-63`) | **Reuse + change** (D5, PT): P1 / P2 / P2-MI / P3 / R-M1 / R-M2 inside `ApplyMapping` for structured sources, plus `BindCollection(schema, outer, itemPairs)` on `IProcessMappingService`. RP's `files` binder and, later, ENG-95985 Send email attachments call it | `BindCollection` lands in PT (ENG-95984 File process parameter type plan, PB-4) |

### 4.3 Collection parameter code from ENG-96230 Collection process parameter type

| What | Where (`PB/Parameters/ProcessParameterService.cs`) | Verdict and how | Limits |
|---|---|---|---|
| Collection aliases | `:31-33`, normalised at `:1035-1036` | **Reuse + change** (D2): add a `FileCollection` alias set beside it | `compositeobjectlist` stays an accepted alias, so a describe output fed back as `CompositeObjectList` builds a shapeless collection; D2's FileCollection read-back is the fix |
| `NormalizeParameterTypeName` | `:1033-1078`; doc comment `:1021-1032` | **Reuse + change** (D1): File aliases return the FileLocator name; dedicated `binary` / `blob` refusal; generic list names `File, FileCollection` | Existing Binary pins stay green (`PBT/ProcessParameterServiceTests.cs:144-150`); the probe at `:1671-1678` moves to another unsupported type |
| `EnsureNotCollectionConstant` | `:425-431` | **Reuse + change** (D7): generalised to "no constant form" for File too | The mapping route has no equivalent; D7 adds it to `ProcessParameterValueValidator.ValidateConstantValue` (`PB/Parameters/ProcessParameterValueValidator.cs:133-221`), which covers every constant route |
| `CloneItemProperties` | `:456-492` (refuses no items `:458-464`, refuses an item without a GUID `Tag` `:466-473`) | **Reuse, refusal kept** (D2 option B rejected); only the message changes, pointing to `type: FileCollection` | Do not use it to shape a declared FileCollection; D2 creates the one `File` item directly (`ContainerUId = schema.UId`, `Tag = null`) |
| `BindMirroredCollection` | `:500-512` (outer level only, rolls back on failure) | **Reuse + change** (D9, PT): call `BindCollection` with the clone pairs, as parity (contingency X4) | M3 refuted H-1 (2026-10-02): the outer mapping already copies the items' values, so this is parity, not a defect fix |
| Mirror source resolution | `ResolveTypeSourceParameter` `:402-410` (accepts dotted paths) | **Reuse + change** (D6): flat only, with a targeted refusal for an item path | today a dotted mirror of `ObjectFiles.File` silently creates an unbound FileLocator parameter |
| Type-change rule | `SetProcessParameter` `:222-237` ("Remove the parameter and add it with the new type instead", `:236`) | **Pattern** for source immutability on `setElement` (D12) | - |
| Describe projection | `ToDescribeParameter` `:141-181`; `DecodeRecordColumnSource` `:184-220`; `WithNestedParameters` `:937-971` | **Reuse + change** (D2, D8): the FileCollection predicate; the nested-only listing walks items like `WithNestedParameters` | `DescribedParameter` in clio has no bag (5.1); no new per-parameter fields |
| Delete guard | `FindParameterUsages` `:576-647` | **Reuse + change** (D7): test every item UId too | today an item reference does not block a delete |

### 4.4 Filters, record scope, sort

| What | Where | Verdict and how | Limits |
|---|---|---|---|
| `IProcessFilterTarget` and `ProcessFilterTargetBase` | `PB/Filters/IProcessFilterTarget.cs:18-49`; `PB/Filters/ProcessFilterTargetBase.cs:16-66` (`ResolveRootSchema` `:66`) | **Reuse** as the base of the new `FileProcessingFilterTarget` (priority 50, between Signal 100 and DataNode 0, `PB/ProcessDesignConstants.cs:1706, 1709`; registered `PBA:173-178`) | - |
| `ProcessFilterService.BuildFilterValue` | `PB/Filters/ProcessFilterService.cs:56` | **Reuse unchanged**: the remainder of `DataSourceFilters` is serialised exactly as for any data element; a lookup column with `equal` already becomes the InFilter shape the legacy scope needs | `processParameter` right-hand sides are not type-checked (`ResolveReference`, `PB/Filters/ProcessFilterService.cs:525-536`); the record scope's V3 check is ours |
| `FilterDescriptorReader.Read` | `PB/Filters/FilterDescriptorReader.cs:32` | **Reuse** in `FileProcessingScopeFilter.Split` and in describe | Decodes the modern wrapper only; a legacy-format filter reads back null |
| Root rule | `SignalStartFilterTarget.Apply` `PB/Filters/SignalStartFilterTarget.cs:45-65` | **Pattern** for V1: omitted `object` = the runtime root, any other value refused | - |
| `DataNodeFilterTarget` | `PB/Filters/DataNodeFilterTarget.cs:57-58` (claims any node with `DataSourceFilters`), `:66-96` | **Reuse** as is for unclaimed elements; the new target outranks it per cut | today it roots the filter on any caller object, unchecked |
| "Is the filter selecting?" | `DataSourceFilterValue.HasStoredFilter` / `HasFilterConditions` `PB/Operations/DataSourceFilterValue.cs:33-59` | **Reuse** for the D16 empty-filter rule, together with the record scope | An undecodable filter counts as HAVING conditions (`:46-50`), the quiet direction, which is right for a refusal |
| `DataSourceFilterValue.ClearIfForeign` | `:62-85` | **Avoid** for this element (D12): its root-name comparison keeps a foreign SysFile scope, because the root is SysFile for every record object | - |
| Refusal text for unfilterable elements | `PB/Filters/ProcessFilterApplier.cs:64-73` | **Reuse + change**: the message also names the Process file element (Object and Report) per cut | - |
| `CurrentUserContact` macro | filter grammar `PB/Filters/MacrosCatalog.cs:67`; formula `PB/ProcessDesignConstants.cs:661` | **Reuse**: the record scope compiles `[#SysVariable.CurrentUserContact#]` to the filter macro (D16) | the only formula the scope accepts |
| Sort shape and codec | `ReadDataSortDescriptor` `PB/Contracts/ProcessDescriptorContracts.cs:1297`; `ResolveOrderInfoValue` `PB/Elements/ReadDataConfigBinder.cs:793-811`; `ParseSortDirection` `:813-825`; `DescribeSort` `:917-945` | **Reuse + change** (RF2): extract to an internal codec with the element label injected; keep the Read data messages identical | one direct column of the runtime root; describe reports the primary entry only |

### 4.5 Multi-instance

| What | Where | Verdict and how | Limits |
|---|---|---|---|
| ENG-99856 Sub-process element: support MULTI-INSTANCE (running the callee once per item of a collection) | `PB/Elements/MultiInstanceApplier.cs` (input / output items cloned from the callee, `:596-598`); `MultiInstanceNotices` | **Reuse unchanged** for "for each attachment, call a sub-process": `InputRecordCollection.F <- OF1.ObjectFiles.File`, with P1 binding the parent. Corpus precedent: `PS/ProcessTests/branches/7.8.0/Schemas/CRM60006PP` iterating into `CRM60006SP.SPFiles` | needs PT (a callee File parameter); D5 adds the P2-MI notice and the regressions |

### 4.6 Notices, ledgers, refusals, dependents

| What | Where | Verdict and how | Limits |
|---|---|---|---|
| `IProcessDesignNotices` | `PB/Design/IProcessDesignNotices.cs:18-32`; registered `PBA:335` | **Reuse** for every notice in the catalogue (decisions appendix) | Attached to a SAVED response only |
| `IDeleteDataNoticeLedger` | interface `PB/Elements/IDeleteDataNoticeLedger.cs:24`; `Reconcile` `PB/Elements/DeleteDataNoticeLedger.cs:46`; registered `PBA:339`; tracked at `PB/Operations/ElementOperations.cs:138`, `PB/Operations/FilterOperations.cs:66, 135`, `PB/Operations/DeleteDataConfigApplier.cs:127`, `PB/Design/ProcessBuildHandler.cs:637`; reconciled at `ProcessBuildHandler.cs:547` and `PB/Design/ProcessEditPipeline.cs:135` | **Pattern + change** (RF3): a file-processing ledger tracked at the same sites (the new `FileProcessingConfigApplier` in place of `DeleteDataConfigApplier`) and reconciled at the same two; its reconcile may **refuse** | Key on the element object, not its name (the interface explains why, `:32-39`) |
| End-of-request refusal precedent | `ProcessEditPipeline.cs:135-139` | **Pattern** for the D16 refusal: after the operation loop, before layout and the pre-save gate | - |
| `ProcessElementDependencyScanner` | `PB/Graph/ProcessElementDependencyScanner.cs:69-72` (element), `:103-105` (one parameter), `:113-115` (token), `:127-134` (snapshot) | **Reuse**: name the dependents in the source-change refusal (D12) and refuse `useInProcess` while `CreatedObjectFileIds` is mapped (`:103-105`) | The scanner is element-qualified, so the template nested UIds that two server-built elements share do not confuse it |
| `EmailTemplateResolver` | `PB/Email/EmailTemplateResolver.cs:66-206` (`Resolve` `:83`, by id `:108`, by name `:152`) | **Pattern** for `report.printable` (id, `[#Lookup...#]` macro or caption; empty, unknown, ambiguous refused) | It reads with a raw `Select`; the report reader uses ESQ for rights and culture (2.4) |
| "Not synchronized" refusal | `ChangeDataConfigBinder.ResolveParameter` `PB/Elements/ChangeDataConfigBinder.cs:959-968` | **Pattern** (D20): a missing template parameter after `GetDesignInstance` is refused, never hand-created | - |
| Direct ConstValue write | `ChangeDataConfigBinder.AssignConstValue` `:975-984` | **Pattern** (private per binder): a NEW value, `ModifiedInSchemaUId = schema.UId`, `Source` before `Value` | - |
| Record-existence check | `ProcessParameterValueValidator.EnsureReferencedRecordExists` `PB/Parameters/ProcessParameterValueValidator.cs:286` | **Reuse + change** (RF4) | display value comes back from the same read |
| Not refusing what the runtime accepts | `PB/Elements/OpenEditPageCandidateReader.cs:58-62` | **Pattern** for the feature-flag policy (D15, D17) | - |
| Log-safe names | `SafeText.Sanitize` (`PB/SafeText.cs`) | **Reuse** in every refusal that echoes caller text | - |

### 4.7 Describe

| What | Where | Verdict and how | Limits |
|---|---|---|---|
| Per-kind describe block | `DescribeProcessElement` `PB/Contracts/DescribeContracts.cs:112`; templates `DescribeOpenEditPageInfo` `:937`, `DescribeSubProcessInfo` `:1512` | **Pattern** for `DescribeFileProcessingInfo` (D19) | - |
| Element parameter listing | `PB/Describe/ProcessDescriber.cs:187-193` | **Reuse + change** (D8, PT): also list an input whose nested item carries a value stamped by this schema | - |
| Generic user-task describe | `UserTaskElementHandler.Describe` `PB/Elements/UserTaskElementHandler.cs:125-151` | **Reuse**: keeps `userTaskName`; the dedicated handler adds only its block (as OpenEditPage does) | Report and Process elements stay generic until RP (D13) |
| No strict keys on the server | `PB/Contracts/VersionContracts.cs:22-26` | Constraint, not reuse: unknown members are dropped, so describe-only members are never declared on the write contract (D11) | ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph validator into the write path (ENG-88414) (the key in that title is ENG-88414 AI-driven application development) must learn the new members if it lands first |

### 4.8 Package test harness

The full recipe is in the [test-plan](eng-92719-file-processing-element-test-plan.md) sections 3 and 4 and, for
the parameter type, in the [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md)
section 3. Two run rules apply to every piece below: net472 tests from a git worktree run only from a short path
(`C:/Projects/workspace/<short>`), because a longer path exceeds MAX_PATH and every test fails in `SetUp`; and every
new fixture carries `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`. What each piece is reused for:

| Piece | Where | Reused for |
|---|---|---|
| `ProcessDesignTestSupport.CreateUserConnection` | `PBT/ProcessDesignTestSupport.cs:219-230` | the connection with a real `DataValueTypeManager` (FileLocator and the collection type with no database) |
| `SetupEmailTaskSchema` / `SetupPreconfiguredPageTaskSchema` | `:263-271`, `:290-298` | the shape of `RegisterFileTaskSchemas`: a substituted `ProcessUserTaskSchema` behind `FindInstanceByName` / `FindInstanceByUId` (identity resolves by name, RF1) |
| `SetupEsqData` | `:301-306` | the one test of the real `IReportTemplateReader` query |
| `CreateGraphBuilder` | `:136` | graph-level tests (a 6-handler subset; add the new handler where a test needs it) |
| `SubProcessTestSupport.AParameterOn` | `PBT/SubProcessTestSupport.cs:346-358`; no type fallback `:370-380` | typed parameters with `"FileLocator"` / `"CompositeObjectList"` |
| `AnElementWithACollection` | `PBT/MultiInstanceMappingTests.cs:42-55` | the outer collection plus `ItemProperties.Add` for `Files.File`, `ObjectFiles.File` |
| `MockEntitySchemaWithColumns` | `PBT/BaseComposableAppTestFixture.cs:386-400` | AccountFile, ContactFile, SysFile, Account, Contact, Invoice for the filter target and sort |
| Filter-target arrange | `PBT/DataNodeFilterTargetTests.cs:40-41` | `new ProcessFilterService(UserConnection)` + mocked schemas |
| Palette read arrange | `PBT/UserTaskElementHandlerCreateTests.cs:27, 53-57` | the `ManagerItemUId` pin |
| Task-schema stub with an item | `PBT/DeleteDataConfigBinderTests.cs:64-68` | `FindItemByUId` returning a named item (describe reads `.Name`) |
| `[SetUp]` arrange rule | `PBT/SendEmailApplierTests.cs:37-45` | arrange stubs per test; a lazy arrange failed in a full run (measured, as recorded there) |
| Record-existence arrange | `PBT/ProcessMappingServiceTests.cs:99-108` | the `saveTo.recordId` constant check |
| Metadata round trip | `PBT/ProcessParameterServiceItemPropertiesTests.cs:63-85` | serialization parity pins and the drift guard |
| Real-core probe precedent | `PBT/SubProcessPlatformProbeTests.cs` | pinning platform behaviour with real core types where they work |
| CI category guard | `PBT/CiContractGuardTests.cs` | fails a new fixture that lacks the CI categories, so a skipped fixture cannot pass silently |

---

## 5. clio

### 5.1 Tool and contract patterns

clio unit tests for these patterns are run by `TestCategory` (a `Category=` alias over more than 2,000 tests runs the
whole assembly), from bash with `< /dev/null` (a child `git init` hangs on an inherited stdin), as the
[test-plan](eng-92719-file-processing-element-test-plan.md) section 3.6 says.

| What | Where (CLIO) | Verdict and how | Limits |
|---|---|---|---|
| Opaque descriptor pass-through | `clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs` (descriptor is a JSON string); no client allow-list exists | **Reuse**: no clio code change is needed to carry `type: File` or the `fileProcessing` block | descriptions are byte-limited (see the contract-budget row below) |
| "Forwards the block verbatim" unit tests | `clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs:120-122` (sendEmail), `:158-160` (openEditPage), `:239-241` (accessRights) | **Pattern** for a `fileProcessing` forwarding test in create and modify | - |
| `ManagerMap.ResolveDataId` explicit arm | `clio/Command/ProcessModel/Schema.cs:1144-1145` (suffix arm `:1149`); tests `clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs`; record `docs/knowledge/ProcessModel/subprocess-build-token-needs-a-managermap-arm.md` | **Reuse + change**: add `fileprocessing` and `processfile`, shipped in PT's clio PR one release before any package emits them (D10) | without it `validate-process-graph` reports a hard error on a graph that builds |
| Describe DTO with bags | `DescribedElement.AdditionalData` `clio/Command/ProcessModel/IProcessDescriber.cs:719-726`; typed blocks with a bag on every nested type: `DescribedOpenEditPage` `:834` (bag `:914`) with nested types bagged at `:941`, `:988`, `:1015`; `DescribedSubProcess` `:1134` (bag `:1219`) | **Pattern** for `DescribedFileProcessing` and every nested type (`attachments`, `report`, `files`, `saveTo`, the value source, the output row), each with `[JsonExtensionData]` (D19). Test in `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` | `DescribedParameter` (`:1886-2016`) has **no** bag: never add a per-parameter field. Update `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md` in the same PR |
| Package floor | `[RequiresPackage]` at `clio/Command/CreateBusinessProcessCommand.cs:240`, `ModifyBusinessProcessCommand.cs:196`, `ModifyProcessAsNewVersionCommand.cs:59`; enforced-floor sentence test `clio.tests/Common/BundledProcessBuilderPackageTests.cs:1503-1560` | **Reuse**: one raise per package cut (D26) | the floor clause must stay in three descriptions (decisions X3) |
| Rebundle and pins | `rebundle-process-builder.ps1`; `clio.tests/Common/BundledProcessBuilderPackageTests.cs` (`ExpectedArchiveVersion` `:318`) | **Reuse** unchanged | install reads the archive from the BUILD OUTPUT; rebuild clio before a local install |
| Contract budget | `clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs:136-142` (ceiling 35,072 B) | **Reuse**: re-measure in every description PR | about 200 B of headroom; contract text goes to the `process-files` guide (D25) |
| Guide names in descriptions | `clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs:537-594`; `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` | **Reuse**: re-pin in the same commit that first writes `name=process-files` (decisions X2) | the fixture moves only to a published generation |
| Read-back block expectations | `clio/Command/ProcessModel/{Email,AccessRights,Approval}BlockExpectation.cs`, `FlowLabelExpectation.cs`, `BlockExpectationReporter.cs` | **Not needed** (D19): the floor raise refuses older servers, and the strict type token refuses loudly | - |
| Capability map | `docs/McpCapabilityMap.md:761, 762, 767` | **Reuse**: update the create, modify and describe rows and the floor | no version literal there may exceed the bundled archive |

### 5.2 e2e arrange helpers

| What | Where (CLIO `clio.mcp.e2e/`) | Verdict and how |
|---|---|---|
| `ProcessDesignerE2EArrange.StartAsync(subject, minimumPackageVersion)` | `Support/Mcp/ProcessDesignerE2EArrange.cs:44-93` (ignored with a message when no sandbox is configured `:50`, when it is unreachable `:54`, or when CrtProcessBuilder is older than the minimum `:89`) | **Reuse** in every new fixture; the minimum is the PR's floor |
| `CallToolAsync`, `CreatedVersionName`, `DescribeAsync`, `DisposeAsync` | same file `:154`, `:174`, `:201`, `:222` | **Reuse** for create -> describe round trips |
| `DescribedProcessGraph.Read` | `Support/Mcp/DescribedProcessGraph.cs:31-38` | **Reuse** to assert on the `fileProcessing` block in raw JSON |
| Fixture shape | `RecordColumnSourceToolE2ETests.cs:39, 50` (`MinimumPackageVersion` constant passed to `StartAsync`) | **Pattern** for `FileParameterToolE2ETests` (PT), `ProcessFileElementToolE2ETests` (OA), the RP and SF fixtures |
| Loud refusal proof | `SubProcessElementToolE2ETests.cs:366-368` | **Pattern** for "an unknown block on another type is refused, not dropped" |
| Category | `Support/Configuration/McpE2ECategories.cs:38` (`McpE2E.ProcessDesigner`, excluded by the TeamCity job, `:31-37`) | **Reuse**: these fixtures run by hand on the stand; the PBT pins stay the automated guard |
| Binary refusal | `ModifyBusinessProcessToolE2ETests.cs:2178-2205` | **Reuse unchanged** (Binary stays refused, decisions X7); only its `[Description]` wording changes |
| Stand preparation | the `clio-mcp-e2e-live-stand` skill | **Reuse** before the first live run; check `get-tool-contract` first, because the session's MCP clio can be stale |

### 5.3 list-printables and list-user-tasks

| What | Where (CLIO) | Verdict and how | Limits |
|---|---|---|---|
| `list-printables` | `clio/Command/ListPrintablesCommand.cs` (MS Word type id `:78`, columns `:84-93`, the only server filter `Type.Id` `:186-197`, payload `:60-61`); `clio/Command/McpServer/Tools/ListPrintablesTool.cs:23, 39-51` (not resident, reached through `clio-run`) | **Reuse as the discovery hint**: the guide says "pass list-printables' `templateId` as `report.printable`". The authority stays server-side: `IReportTemplateReader` resolves the reference with the runtime's rules and refuses with candidates (D17) | MS Word only, by design (its contract is the print button); the entity filter matches either path in memory; it reads as the clio user, the element reads as the process runner. Do not change it (D17) |
| `ListPrintablesEnvelope` | `clio.mcp.e2e/Support/Results/ListPrintablesEnvelope.cs:11-13` (`success`, `error` only) | **Reuse + change** (RP): add `count` and `printables` for the e2e skip rule "no MS Word printable on this stand" | - |
| `list-user-tasks` | `clio/Command/McpServer/Tools/ListUserTasksTool.cs:46-62`; server text `PB/Contracts/ListUserTasksContracts.cs:43` ("pass this as userTaskName on a userTask element") | **Touch, not reuse**: the description must exclude the three schemas from the "fall back to a generic userTask" advice, per cut (D13). `ListUserTasksPrompt` (`clio/Command/McpServer/Prompts/ListUserTasksPrompt.cs:22-25`) gets the same per-cut exclusion; TC-89 and TC-90 pin both (decisions D25). | otherwise the tool advertises a route the server refuses |

---

## 6. clio-knowledge

| What | Where (KB) | Reused for |
|---|---|---|
| Guidance pin test | `automation/Clio.Knowledge.Bundle.Tests/CollectionParameterGuidanceTests.cs:24-29` | the pin pattern of `FileParameterGuidanceTests` / `ProcessFileGuidanceTests`, and the re-pin when `parameters.md:35-37` gains the FileCollection exception |
| Response-size budget | `automation/Clio.Knowledge.Bundle.Tests/ProcessGuideResponseSizeTests.cs` | keeping `process-files` at most 22,000 chars and the edited articles under budget |
| Registration | `bundle-source.json` (libraryVersion `:6`), `GuidanceMigrationTests.cs`, `ProcessGuideCrossReferenceTests.cs`, the routing article, `ProcessGuideSet.GoLiveFloor` | registering the new guide (D25) |
| Filter vocabulary | `guidance/mcp/guides/processes/data-source-filters.md` | the record-scope and filter-root wording reuses its terms; the file element's own rules stay in `process-files` |

After a knowledge merge, check `info-knowledge` for the library version before a guidance-dependent run: a failed
`update-knowledge` keeps serving the old guidance silently.

---

## 7. Platform UnitTests: recipes to copy, never reference

`UT` compiles against the whole configuration and subclasses the real user-task classes. CrtProcessBuilder must
not (section 3.1). Its vendored `PBT/Libs/Terrasoft.TestFramework.dll` and `UnitTest.dll` are referenced as they
are today; the `UT` **sources** are copied as shapes only. The platform tests do not follow the house test style
(no AAA, no `because`, no `[Description]`); copies must. The adapted code is in
[test-plan](eng-92719-file-processing-element-test-plan.md) section 4 (R1-R8).

| Recipe | Platform source | Verdict | What CrtProcessBuilder takes from it |
|---|---|---|---|
| R1 Connection | `CORE83/Terrasoft.TestFramework/SubstituteUtilities.cs:230` (`CreateEmptyUserConnection`); `UT/FileProcessing_Tests.cs:56` | adapt | already adapted as `ProcessDesignTestSupport.CreateUserConnection` |
| R2 File-object shapes | `UT/TestProcessUserTaskUtilities.cs:52-145` (`GetFileEntitySchema`, `SetupSysFileEntitySchema`) | adapt | column sets for `MockEntitySchemaWithColumns` and the hierarchy fake. The stand's `SysFile.RecordSchemaName` is ShortText, not MediumText as the fake has it (measured 2026-10-01) |
| R3 ESQ reads | `UT/ObjectFileProcessingUserTask_Tests.cs:163, 235` (`SetupExecuteReader("File", FileConsts.FileTypeUId)`); `SubstituteUtilities.cs:347-368` (SQL matched as a regex) | adapt | the `SetupEsqData` style for the report reader; the `Type = File` filter is a runtime fact the guide states |
| R4 File factory | `UT/FileProcessing_Tests.cs:131-138`; `SubstituteUtilities.cs:825` | reference only | how the runtime copies (`CopyAsync`, `SetAttribute(<link column>)`); the builder never copies files |
| R5 Report engine and temporary files | `UT/ReportFileProcessingUserTask_Tests.cs:115-135` | reference only | the UseInProcess temporary-file contract behind the D14 notice |
| R6 Running a task | `UT/ProcessFileProcessingUserTask_Tests.cs:43, 74` (private subclass, `Files` assigned as `CompositeObjectList<CompositeObject>`) | reference only | shows the platform **never** tests building `Files` rows from a mapping; that gap is why M1 and M4 run on the stand |
| R7 Design-time element | `UT/ObjectFileProcessingUserTask_Tests.cs:86-106` | adapt | already the house pattern (hand-built `ProcessSchemaUserTask`, section 4.8) |
| R8 Storage matrix | `UT/FileProcessing_Tests.cs:174-177` (four `[TestCase]`s), `:150` (`[Values]`) | copy the shape | `[TestCase]` over storage pairs in the resolver, describe and repair tests |

Runtime facts these suites pin, which the builder mirrors without re-testing: `ResultActionType` per variant
(`UT/ObjectFileProcessingUserTask_Tests.cs:188-200`, `UT/ReportFileProcessingUserTask_Tests.cs:322-334`,
`UT/ProcessFileProcessingUserTask_Tests.cs:103-112`); output row keys; `RecordSchemaName` written only when
`TargetDataEntitySchemaUId` resolves; the report file-name formula (`UT/ReportFileProcessingUserTask_Tests.cs:215-320`).

---

## 8. Shipped captures to reuse as fixtures

Designer-built metadata in the corpus, used for parity tests and the D20 / D24 capture comparison. Read-only;
copy the relevant `BP2` parameter blocks into test resources.

| Capture | Path | Reused for |
|---|---|---|
| Object attachments, legacy storage | `PS/CopilotAutoTest/branches/7.8.0/Schemas/SkillFilesValidationProcess/metadata.json` | OA serialization parity (designer nested UIds, `IL2`, both-level consumer bindings) |
| Generated report, MS Word, SaveToFiles | `PS/CrtInvoice/branches/7.8.0/Schemas/PrintInvoiceReport/metadata.json` | RP parity; product recipe |
| Process parameter variant, File and FileCollection parameters | `PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json` (parameters `:13-55`, nested mapping `:1417-1440`) | PT parity (Variable FileCollection, item name `FileCollectionParameter`), RP `files` decode |
| Out FileCollection | `PS/ProcessLibrary/branches/7.8.0/Schemas/MarkProcessesToCancel/metadata.json:76-91` | PT parity for an explicitly declared Out FileCollection (the D3 default is Variable, compared with `FileParameterProcess.FileCollection`) |
| Multi-instance per file | `PS/ProcessTests/branches/7.8.0/Schemas/CRM60006PP`, callee `CRM60006SP` (`SPFiles` at `metadata.json:17-20`) | OA per-file pattern |
| Legacy record scope | `PS/ProcessTests/branches/7.8.0/Schemas/FileCopyProcessPP/metadata.json` (`Contact = RD1.Id`) | the D16 describe lift |
| Product report processes (describe only) | `PS/CrtLeadOppMgmtApp/branches/7.8.0/Schemas/PrintQuotationReport`; `PS/CrtEmailMarketingApp/branches/7.8.0/Schemas/GenerateDNSRecordsSpecification` | describe fixtures, including the unlinked `DNSGuideFile` shape (D15 `linkColumn` repair) |
| SysFile-mode Object element | **none shipped** (0 of 30 elements, measured 2026-10-01) | needs the designer capture M21 before SF |

---

## 9. Avoid list

| Do not reuse | Where | Why |
|---|---|---|
| The platform name `File` as a parameter type | `CORE/Terrasoft.Core/DataValueTypeManager.cs:341-349` | it is the BLOB type `BA40CFC5`, which no designer page offers and the process store cannot hold (D1) |
| Core `IFileSchemaProvider` / `FileSchemaProvider` | `GEN/IFileSchemaProvider.CrtNUI.cs:10-28`, `GEN/FileSchemaProvider.CrtNUI.cs:43-128` | configuration code, and a different rule (strips a `File` prefix or suffix, includes SysFile, first match wins) that disagrees with the designer (D15) |
| `EntitySchema.MasterRecordColumn` as the link signal | e.g. `GEN/AccountFileSchema.CrtBase.cs:67-73` | null for many file objects; the designer ignores it |
| `ReportEngine`, `ReportType`, `IPdfConverter` | `GEN/ReportEngine.Reports.cs`, `GEN/IReportEngine.CrtBase.cs:81-95` | configuration code; the query is mirrored instead (2.4) |
| `Terrasoft.Reports` | `PBC:87-91` | net472-only, and it does not hold `ReportEngine` (RF6) |
| `ResultActionTypeEnum`, `FileConsts`, the user-task CLR classes | `PD/FileProcessing/FileProcessing.cs:19-30` and siblings | `Terrasoft.Configuration`; breaks the `dev-n8` build (3.1) |
| `UserConnection.GetIsFeatureEnabled` / `FeatureUtilities` | configuration package CrtFeatureToggling | configuration code, and its DB-only fallback reads OFF for a code-defined feature with no `Feature` row (the SysFile flag on the stand) |
| `ProcessFeatures` | `CORE/Terrasoft.Core/Process/ProcessFeatures.cs:13` | `internal` |
| `DataSourceFilterValue.ClearIfForeign` for this element | `PB/Operations/DataSourceFilterValue.cs:62-85` | keeps a foreign SysFile scope (D12) |
| `DataNodeFilterTarget` for claimed elements | `PB/Filters/DataNodeFilterTarget.cs:57-58, 66-96` | roots the filter on any caller object, unchecked (D16) |
| `ApplyMapping`'s constant arm for schema-UId parameters | `PB/Mappings/ProcessMappingService.cs:302-324` | the Lookup check looks for a `SysSchema` record, not a schema UId (4.2) |
| `CloneItemProperties` to shape a declared FileCollection | `PB/Parameters/ProcessParameterService.cs:456-492` | it exists to copy a Read data shape with column Tags; a file item has none (D2) |
| The generic `userTask` route for the three schemas | `PB/Elements/UserTaskElementHandler.cs:74-90` | builds an unconfigured element that saves green and throws at run time; refused per cut (D13) |
| `ProcessFilesControlSchema` dynamic slots | `PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:255-291` | they belong to Send email (`Attachments<N>`) and the Creatio.ai call (`Files<N>`), not to this element (decisions X6); Send email is ENG-95985 Send email attachments. The server-side precedent for creating dynamic slots is `SendEmailApplier.CreateRecipientParameter` (`PB/Email/SendEmailApplier.cs:876-884`) |
| `HasCollectionWithOneElement` as the "single file" signal | `CORE/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs` | it only feeds the multi-instance iteration count; the one-row single-file path is `TryGetListValue` (`ProcessInstanceParametersDataReader.cs:444-453`, pending M1) |
| `ProcessInstanceParametersDataReader` in tests | `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs` | `internal`; the runtime reader is proved on the stand (M1, M2, M4), never in a package unit test |
| `FileCopier` | `PD/FileCopier/FileCopier.cs:29-69` | not used by any of the three tasks |
| The designer's SysFile-mode sort list | section 3.4 | predicted runtime failure |

---

## 10. What must be written new

Everything below has no reusable equivalent today. Work-package ids refer to the
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) (PB-n, CL-n, KB-n) and
the [plan](eng-92719-file-processing-element-plan.md) (OA.n, RP.n, SF.n). New files under `PB/FileProcessing/`
follow the existing per-family folders `PB/Email/` and `PB/Approval/`.

### 10.1 Package (https://creatio.ghe.com/engineering/crt-process-builder)

| New | Cut | Sits on (reuse) | Work package |
|---|---|---|---|
| File and FileCollection aliases; the one `File` item a FileCollection is born with; the dedicated Binary refusal | PT | `NormalizeParameterTypeName`, the collection aliases, `FileLocatorDataValueType` | PB-1, PB-2 |
| `ProcessSchemaElementLocator.ResolveProcessParameterPath` (no `ContainerUId` backfill) | PT | `DescendItemProperties` refactored to a root collection | PB-3 |
| P1, P2, P2-MI, P3, R-M1, R-M2 inside `ApplyMapping`; `BindCollection` on `IProcessMappingService` | PT | `ApplyMapping`, the locator, `IProcessDesignNotices` | PB-4 |
| Dotted-mirror refusal; the FileCollection hint in `CloneItemProperties`; constant refusal for FileLocator and collection targets; the item-aware delete guard | PT | `ResolveTypeSourceParameter`, `ValidateConstantValue`, `WithNestedParameters` | PB-5, PB-6 |
| Nested-only listing and the single-token element-source decode in describe; the FileCollection predicate | PT | `ProcessDescriber`, `TryNameNestedParameter`, `ToDescribeParameter` | PB-7 |
| `BindMirroredCollection` binding items (parity; M3 refuted H-1, X4) | PT | `BindCollection` | PB-8 |
| `ElementTypes.FileProcessing` (`fileprocessing`, `processfile`); mirrored constants (section 3.2) | OA | `ProcessDesignConstants` | OA.1 |
| Variant registry (`IFileProcessingVariant`, `FileProcessingVariantRegistry`) | OA (Object), RP (Report, Process) | `UserTaskSchemaIdentity` x3 | OA.1, RP.3 |
| `FileProcessingElementHandler` (Create, Configure, CanBuild, CanDescribe, Describe; `SerializeToDB`; nested-UId re-mint) | OA | `OpenEditPageElementHandler` pattern, the platform sync, template definitions | OA.1 |
| `FileProcessingDescriptor` + groups + `FileProcessingValueSourceDescriptor` on the build and update contracts; `DescribeFileProcessingInfo` | OA, RP | `OpenEditPageRecordDescriptor` member names, `ReadDataSortDescriptor` | OA.1, OA.6 |
| `FileProcessingConfigBinder` and `FileProcessingConfigApplier` (+ interface) | OA, RP | `ApplyMapping`, direct ConstValue writes, `ProcessElementDependencyScanner`, the OpenEditPage binder and applier pattern (RF8) | OA.2 |
| `IAttachmentStorageResolver` + `IEntitySchemaHierarchyReader` (+ implementations) | OA (dedicated), SF (SysFile) | `EntitySchemaManager`, `SchemaManager.GetAllParents`, the mirrored designer algorithm | OA.3, SF.1 |
| Record-existence entry point for a Guid-typed record id | OA | `EnsureReferencedRecordExists` (RF4) | OA.3 |
| `FileProcessingScopeFilter` (`Split` / `Join`) and `FileProcessingFilterTarget` | OA (Object), RP (Report), SF (SysFile shape) | `ProcessFilterTargetBase`, `BuildFilterValue`, `FilterDescriptorReader`, the signal-start root rule | OA.4, SF.1 |
| End-of-request file-processing ledger that may refuse | OA | the `IDeleteDataNoticeLedger` pattern, the formula end-check (RF3) | OA.4 |
| Shared sort codec extracted from `ReadDataConfigBinder` | OA | `ResolveOrderInfoValue`, `DescribeSort` (RF2) | OA.4 |
| Generic-route refusal and raw-mapping refusal, per cut | OA, RP | `UserTaskElementHandler.Create`, the registry | OA.5, RP.3 |
| Nested-source display value in the designer form | OA | `ProcessMappingService.BuildSourceValue` element arm | OA.6 |
| `IReportTemplateReader` (+ implementation) and the `report` group rules | RP | ESQ, the mirrored `ReportEngine` query, the `EmailTemplateResolver` pattern | RP.1 |
| `IFileProcessingFeatureReader` (+ implementation) | RP (report notice), SF (SysFile notice, after M25) | `Creatio.FeatureToggling.Features.GetIsEnabled` (RF5) | RP.1, SF.1 |
| The `files` binder of the Process variant | RP | `BindCollection`, P2 / R-M2 | RP.2 |
| `FileProcessingTestSupport` (the 13 / 8 / 13 template table, the three builders, `SyncShape.Server`), `FileProcessingTemplateDriftTests`, the captured fixtures and `PROVENANCE.md` | PT (inherited by OA and RP) | section 4.8, the drift-guard layers of section 3.5 | PB-9 (ENG-95984 File process parameter type plan) |
| the helper's element additions (`RegisterFileTaskSchemas`, `ArrangePaletteRow`, `ApplyCorpusConfiguration`, `SyncShape.Designer`) and the hierarchy fake | OA | section 4.8 | OA.7 |
| `docs/process-file-element-capture.md` and `docs/file-parameter-capture.md` | PT, OA, RP, SF | the 15 existing `docs/*-capture.md` (convention of `docs/sub-process-element-capture.md`) | PB-10, OA.8, RP.5 |

### 10.2 clio (https://github.com/Advance-Technologies-Foundation/clio)

| New | Cut | Sits on (reuse) | Work package |
|---|---|---|---|
| `ManagerMap` arm `fileprocessing` / `processfile` + `[TestCase]`s | PT | `Schema.cs:1144-1145` | CL-3 |
| Typed `DescribedFileProcessing` with a bag on every nested type | OA, RP | `DescribedOpenEditPage` / `DescribedSubProcess` pattern | OA.10, RP.6 |
| Description clauses, floor raises and capability-map rows | PT, OA, RP | the guards of section 5.1 | CL-1, CL-2, CL-4, OA.9, OA.10, RP.6 |
| `ListPrintablesEnvelope.count` / `printables` | RP | the existing envelope | RP.6 |
| e2e fixtures `FileParameterToolE2ETests`, `ProcessFileElementToolE2ETests`, the RP fixture, `ProcessFileSysFileStorageToolE2ETests` | PT, OA, RP, SF | `ProcessDesignerE2EArrange`, `DescribedProcessGraph` | CL-6, OA.11, RP.7, SF.3 |
| `list-user-tasks` description exceptions | OA, RP | `ListUserTasksTool.cs:46-62` | OA.10, RP.6 |
| Prompt sentences: `ListUserTasksPrompt` (the per-cut generic-route exclusion), `CreateBusinessProcessPrompt` (the `fileProcessing` block), `ModifyBusinessProcessPrompt` (the D12 merge rule), `DescribeProcessPrompt` (the block list), each pinned (TC-90) | OA, RP | the existing prompt pins (`clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs:334`, `ModifyBusinessProcessToolTests.cs:246, 441, 499`, `DescribeProcessToolTests.cs:94-109`) | OA.10, RP.6 |

### 10.3 clio-knowledge (https://github.com/Advance-Technologies-Foundation/clio-knowledge)

| New | Cut | Sits on (reuse) | Work package |
|---|---|---|---|
| The `process-files` guide (born in PT, extended by OA, RP, SF) and its pin tests | PT, OA, RP, SF | `CollectionParameterGuidanceTests` pattern, the size and registration tests | KB-1, OA.12, RP.8, SF.4 |
| Version-gated rewrites of the multi-instance passages and the FileCollection exception in `parameters.md` | PT | existing articles and their pins | KB-2, KB-3 |

Nothing in sections 10.1 to 10.3 needs a new WCF operation, so `ExpectedOperationContractCount` and
`ExpectedAuthorizationGateCallSites` in clio do not move (D26), and no Ring-consumed contract changes: ClioRing
consumes only catalog names, Purpose text and Destructive flags and dispatches non-destructive tools generically
(`clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95`, `clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157`); names
and flags stay unchanged.
