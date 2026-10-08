# ENG-92719 File processing element and ENG-95984 File process parameter type: serialization capture

| | |
|---|---|
| Issues | ENG-92719 File processing element (Story), sub-tasks ENG-96505 Element readiness and object attachments mode and ENG-96506 Generated report + process parameter modes; ENG-95984 File process parameter type (Task) |
| Epic | ENG-92704 Create BP via AI Toolkit |
| Acceptance criteria served | "Server serialization matches a designer-built capture" in all four issues (plan AC-FE5, AC-OA7, AC-RP6; ENG-95984 File process parameter type plan AC-8) |
| Baselines | Creatio core 10.1.37 (the stand's core); CrtProcessDesigner 7.8.0 (byte-identical to what the stand serves). Anchors point to CrtProcessBuilder `main` `d9571626` = 1.6.6.85 and clio `master` `db6e2bf9e`, re-pinned 2026-10-08; the stand measurements of 2026-10-02 ran on CrtProcessBuilder 1.6.6.54 (`3f4cce50`) |
| How it was made | Read-only, 2026-10-01. Corpus re-scanned for this document (16 processes, 30 elements, 400 stored parameter entries, 394 mapping rows). Nothing was built, saved or run on the stand |

## Summary

This is the oracle that the "matches a designer-built capture" criteria are diffed against. It decodes every metadata
key that the three Process file schemas (Object attachments `9387c794`, Process parameter `6c620dd2`, Generated report
`c2bf0416`) and the File / file-collection process parameters write, and it states the rules behind the values rather
than one example's values. The rules are measured over the whole shipped corpus. Every element writes the same
16 element keys. Every element parameter and nested item carries the user-task schema as `A3`/`A4` and the element as
`IL2` (400 of 400). A value carries the process UId in `GS5` only when the designer actually changed it: an untouched
template default keeps the template UId (14 of 14 `RecordsToRead`, 18 of 18 `ConsiderTimeInFilter`). A collection
bound to a collection is bound at both levels (11 of 11), and the nested item points straight at the source's
nested-item UId. A constant file-name suffix (`ReportName`) is not in the metadata at all: it lives in the schema
resources, in the authoring culture only. Three things are missing, and section 12 says how to capture each of
them on the stand. First, no saved SysFile-mode element exists anywhere (0 of 30; UO-3 and UO-4 are in-memory
readings). Second, no Process-variant capture has the current 8-parameter set, and none has a single-File source.
Third, no Object "save to attachments" element and no Report "use in process" element has the full parameter set.
One designer-built, never-run capture process (SC-1, one schema write) covers all of them and also answers M7 and
the capture half of M21. Section 10 also lists where builder output already differs from the designer's bytes, and
names four of those differences as tolerated deviations (N2, N9, N10, N13). The test plan's TC-41 rule
states the same list; the ENG-95984 File process parameter type AC-8 rule (its test-plan section 4.2) states N9, the
one of them that applies to process parameters.

Sibling documents: [README](README.md) ·
[platform-reference](eng-92719-file-processing-element-platform-reference.md) ·
[use-cases](eng-92719-file-processing-element-use-cases.md) · [traps](eng-92719-file-processing-element-traps.md) ·
[reuse](eng-92719-file-processing-element-reuse.md) · [decisions](eng-92719-file-processing-element-decisions.md) ·
[plan](eng-92719-file-processing-element-plan.md) · [test-plan](eng-92719-file-processing-element-test-plan.md) ·
[pr-split](eng-92719-file-processing-element-pr-split.md) ·
[open-questions](eng-92719-file-processing-element-open-questions.md) ·
[ENG-95984 File process parameter type: plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) ·
[ENG-95984 File process parameter type: test plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md)

---

## 0. How to read this

**Basis labels.** *source*: read in code or metadata (for runtime or designer behaviour this is a hypothesis).
*corpus*: counted over the shipped packages by a script, 2026-10-01. *measured*: observed on the stand `Creatio`
(core 10.1.37.0, .NET Framework) with the date. *predicted*: what a save is
expected to write, derived from source plus corpus; section 12 measures it.

**Path aliases.**

| Alias | Path |
|---|---|
| `CORE` | `C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib` (10.1.37, the stand's core) |
| `NUI` | `CORE/Terrasoft.Nui/Resources/Terrasoft` (classic shell client, 10.1.37) |
| `PD` / `PDR` | `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas` / `.../Resources` |
| `PS` | `C:/Projects/PackageStore` (shipped corpus; every file cited is under `<Package>/branches/7.8.0/`) |
| `PB` / `PBD` | https://creatio.ghe.com/engineering/crt-process-builder, `packages/CrtProcessBuilder/Files/src/cs` / `docs`, main `d9571626` (1.6.6.85) |
| `CLIO` | clio repository root, master `db6e2bf9e` |

**Placeholders in JSON fragments.** `<P>` = the process schema UId, `<T>` = the user-task schema UId (`9387c794…`,
`6c620dd2…` or `c2bf0416…`), `<E>` = the element UId, `<lane>` = the lane UId. `…` elides a UId's tail or a long string.
Fragments are trimmed: `BL1` and the repeated `A3`/`A4`/`IL2` of parameters are dropped once section 3 has stated them.

**Measurement IDs.** M1-M26 are the stand measurements of [decisions](eng-92719-file-processing-element-decisions.md)
D29, tracked in [open-questions](eng-92719-file-processing-element-open-questions.md) section B. SC-0 to SC-4 are the
capture steps defined in section 12 of this document. UO-1 to UO-4 are the designer observations of 2026-10-01,
documented in [platform-reference](eng-92719-file-processing-element-platform-reference.md) §7.4.

**Ticket aliases in dense tables.** PT = ENG-95984 File process parameter type; OA = ENG-96505 Element readiness and
object attachments mode; RP = ENG-96506 Generated report + process parameter modes; SF = the proposed Sub-task
"SysFile attachment storage in the Process file element" ([decisions](eng-92719-file-processing-element-decisions.md) D15).
PK-`<cut>` and CL-`<cut>` are the package PR and the clio PR of a cut (PK-PT, PK-OA, PK-RP, PK-SF; CL-PT, CL-OA, …), as
named in [pr-split](eng-92719-file-processing-element-pr-split.md) section 1.

---

## 1. What is compared, and which capture is the oracle

Population (corpus, reproduced for this document): 16 shipped process schemas hold 30 Process file elements: Object
14, Process 2, Report 14. They hold 338 top-level element parameters and 62 nested items, and 394 `BK15` mapping
rows. The four product processes hold 5 Report elements; all Object and Process elements are in test packages (use
cases in [use-cases](eng-92719-file-processing-element-use-cases.md) §2).

"Full set" below means that the element carries every parameter its user-task schema declares today: 13 for Object,
8 for Process, 13 for Report. An older element misses some of them until a design-time load adds them back
(section 4.5).

| Comparison | Oracle in the corpus | Full set? | Status |
|---|---|---|---|
| Object, legacy storage, "Use in process" (OA) | `PS/CopilotAutoTest/.../SkillFilesValidationProcess/metadata.json:1426` `ObjectFileProcessingUserTask1` | yes | usable |
| Object, legacy storage, "Save to object attachments" (OA) | `PS/ProcessTests/.../FileCopyProcessPP/metadata.json:2656` `ObjectFileProcessingUserTask1` | **no**: lacks `ConsiderTimeInFilter`, `SourceDataEntitySchemaUId`, `TargetDataEntitySchemaUId`. All 7 shipped SaveToFiles Object elements lack both `*DataEntitySchemaUId`; 6 of them also lack `ConsiderTimeInFilter` | partial; SC-1 E2 replaces it |
| Object, SysFile source or target (SF) | none: 0 of 30 elements use SysFile | - | **missing**; SC-1 E3-E5 (and M21) |
| Report, legacy target, "Save to object attachments" (RP) | `PS/CrtInvoice/.../PrintInvoiceReport/metadata.json:884` `ReportFileProcessingUserTask1` (product; installed on the stand) | yes | usable; SC-0 re-reads it off the stand |
| Report, "Use in process" (RP) | `PS/ProcessTests/.../ProcessFileReport/metadata.json:3212` `ReportFileProcessingUserTask5` | **no**: lacks `TargetDataEntitySchemaUId` (so does the only other one, `ProcessFileSendMail` `ReportFileProcessingUserTask1`) | partial; SC-1 E7 |
| Report, constant file-name suffix (RP) | `PS/ProcessTests/.../FileReportNamesProcess/metadata.json` `ReportFileProcessingUserTask4` + its resources | no (lacks `TargetDataEntitySchemaUId`) | the resource shape is usable; SC-1 E6 |
| Process variant, collection from an element / from a process parameter (RP) | `PS/ProcessTests/.../FileParameterProcess/metadata.json:1133` (`ProcessFileProcessingUserTask2`) and `:1293` (`…3`) | **no**: 7 of 8 parameters (lacks `TargetDataEntitySchemaUId`) | partial; SC-1 E8, E9 |
| Process variant, single File source, nested-only binding (RP) | none | - | **missing**; SC-1 E10 (stored shape only; M1 runs it) |
| Process variant, SysFile target (SF) | none | - | **missing**; SC-1 E11 |
| File process parameter (PT) | `FileParameterProcess/metadata.json:14-24` `FileParameter` | n/a | usable |
| File collection, Variable / Out (PT) | `FileParameterProcess/metadata.json:25-52` `FileCollection`; `PS/ProcessLibrary/.../MarkProcessesToCancel/metadata.json:76-91` `FilesCollection` | n/a | usable; SC-1 refreshes both |

**`B8` is not the version of the last save.** The schema key `B8` is `CreatedInVersion`
(`CORE/Terrasoft.Core/Schema.cs:82`). It is stamped when the schema is created (`:143`;
`CORE/Terrasoft.Core/Process/BaseProcessSchemaManager.cs:903`), not on each save. The corpus research labelled it
"saved-in". When a capture was last saved by a designer is therefore unknown. Parameter-set drift is the only
datable signal: an element that lacks `TargetDataEntitySchemaUId` was last saved before the template gained it.
basis=source.

**Do not compare describe output.** `describe-business-process` hides inherited template defaults, empty link
columns and nested-only bindings (`PB/Describe/ProcessDescriber.cs:188-199`), and it resolves a process by name to
its base version (`CLIO/docs/knowledge/ProcessModel/describe-business-process-reads-the-base-version-not-the-active-one.md`).
The capture is the stored metadata plus the stored resources, nothing else (section 12.2).

---

## 2. Key dictionary

Every key below is a short serialization name declared in core 10.1.37. "Corpus" counts are over the 30 elements
(element keys) or the 400 stored parameter entries (338 top-level + 62 nested).

### 2.1 Process-schema level (only the keys this work touches)

| Key | Property | Evidence | Note |
|---|---|---|---|
| `FJ1` | Parameters (process parameters; also the declared parameters of a user-task schema) | `CORE/Terrasoft.Core/Process/BaseProcessUserTaskSchema.cs:29` | File and FileCollection parameters live here (section 8) |
| `BK4` | FlowElements | `CORE/Terrasoft.Core/Process/ProcessSchema.cs:96` | the 30 elements are all direct `BK4` children (corpus) |
| `BK15` | Mappings (`ProcessSchemaMapping` rows) | `CORE/Terrasoft.Core/Process/BaseProcessSchema.cs:48` | section 2.5 |
| `BK3` | LaneSets | `ProcessSchema.cs:95` | the element's `IL2` points at a lane in here |
| `B8` | CreatedInVersion | `CORE/Terrasoft.Core/Schema.cs:82` | not a save version (section 1) |
| `B6` | PackageUId | `Schema.cs:90` | |

### 2.2 Element keys (`ProcessSchemaUserTask`)

All 16 keys are present on 30 of 30 shipped elements (corpus).

| Key | Property | Evidence | Designer value (corpus) |
|---|---|---|---|
| `BL1` | CLR type name | `CORE/Terrasoft.Core/Process/BaseProcessSchemaItem.cs:34` | `Terrasoft.Core.Process.ProcessSchemaUserTask` |
| `UId` | element UId | `CORE/Terrasoft.Core/MetaItem.cs:89` | fresh |
| `A2` | Name | `MetaItem.cs:90` | `<SchemaName><N>`, e.g. `ObjectFileProcessingUserTask1`. A source switch gives the new schema's prefix ([platform-reference](eng-92719-file-processing-element-platform-reference.md) §8.3) |
| `A3` / `A4` | CreatedInSchemaUId / ModifiedInSchemaUId | `MetaItem.cs:88, :81` | `<P>` (30 of 30) |
| `A5` | CreatedInPackageId | `MetaItem.cs:82` | the package's `SysPackage.Id`; the save fills an empty `A5` (`PBD/script-task-element-capture.md`, measured 2026-09-25) |
| `IL2` | ContainerUId | `CORE/Terrasoft.Core/Process/BaseProcessSchemaElement.cs:38`, written when not empty `:217` | `<lane>` |
| `BL3` | Position `"x;y"` | `CORE/Terrasoft.Core/Process/ProcessSchemaBaseElement.cs:37` | layout |
| `BL7` | ManagerItemUId | `ProcessSchemaBaseElement.cs:40` | `= J6` (30 of 30); the diagram keys on it |
| `BL8` | CreatedInOwnerSchemaUId | `ProcessSchemaBaseElement.cs:41` | `<P>` in 26 of 30. The 4 others are the elements of `FileCopyProcessPP`, which keep the UId of the schema they were first created in (`0e66a957…`) |
| `BN2` | Size `"w;h"` | `CORE/Terrasoft.Core/Process/ProcessSchemaFlowElement.cs:22` | `"69;55"` (30 of 30) |
| `BO2` | SerializeToDB | `CORE/Terrasoft.Core/Process/ProcessSchemaFlowNode.cs:20` | `true` (30 of 30), copied from the user-task schema's `FK4` = true (`PD/*FileProcessingUserTask/metadata.json`; key `CORE/Terrasoft.Core/Process/ProcessUserTaskSchema.cs:86`) |
| `BO3` | IsLogging | `ProcessSchemaFlowNode.cs:21` | `true` (30 of 30) |
| `CL2` | FillColor | `CORE/Terrasoft.Core/Process/ProcessSchemaTask.cs:16` | `"FFFFFFFF"` (30 of 30) |
| `J6` | SchemaUId (the user-task schema) | `CORE/Terrasoft.Core/Process/ProcessSchemaUserTask.cs:32` | one of the three schema UIds; **this is the variant** |
| `BP2` | Parameters | `CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:41` | section 4 |
| `BL10` | Caption | `BaseProcessSchemaElement.cs:28` | never in metadata: the caption is the resource `BaseElements.<A2>.Caption` (section 7) |

Not written by any file element in the corpus: `BP6` (multi-instance options, `ProcessSchemaActivity.cs:34`), `J7`
(`ProcessSchemaUserTask.cs:33`), `BL5`, `BL6`, `BL9`, `IL3`.

### 2.3 Parameter keys (`ProcessSchemaParameter`, top-level `BP2` entries and nested `L18` items)

Evidence for the L-keys: `CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:124-143`. The writer is `:1015-1040`.
A key whose value equals the writer default is omitted, so **absence carries meaning**.

| Key | Property | Absent means | Corpus (338 top-level / 62 nested) |
|---|---|---|---|
| `UId` | parameter UId | - | fresh per element; never the template's UId (section 3) |
| `A2` | Name | - | the template parameter's name |
| `A3` / `A4` | Created / Modified in schema | - | `<T>` on 400 of 400 |
| `IL2` | ContainerUId | Empty | `<E>` on 400 of 400 (element parameters). **Absent on process-level parameters** (section 8) |
| `L1` | DataValueTypeUId | - | section 2.6 |
| `L6` | IsRequired | false | 90 / 0: `SourceEntitySchemaUId`, `TargetEntitySchemaUId` and `ResultActionType` (Object); `ResultActionType`, `TargetEntitySchemaUId` and `Files` (Process); `IsSeparateReports`, `ReportId` and `ResultActionType` (Report) |
| `L8` | SourceValue | - | always present, possibly `{}` (section 2.4) |
| `L9` | ReferenceSchemaUId | Empty | 76 / 0. On the schema lookups it is `6c7394db…` (SysSchema). On `ConnectedObjectId` it is the RECORD object (e.g. Contact `16be3651…`), even though `L1` is Guid |
| `L12` | Direction: In 0, Out 1, Variable 2, Internal 3 | **Variable** (`:416`, written at `:1034` only when not Variable) | 62 / 62: `1` on every output collection and its item; `0` on `Files` and `Files.File` |
| `L13` | IsResult | false | 0 |
| `L17` | Tag | empty | 0 / 0 |
| `L18` | ItemProperties (nested parameters) | none | 62 top-level collections, exactly 1 item each |

### 2.4 Value keys (`L8` = `ProcessSchemaParameterValue`) and the eight stored value shapes

| Key | Property | Evidence |
|---|---|---|
| `GS1` | Source: None 0 (omitted), ConstValue 1, Mapping 2, Script 3, SystemValue 4, SystemSetting 5, EntityMapping 6, SamplingEntityMapping 7 | `CORE/Terrasoft.Core/Process/ProcessSchemaParameterValue.cs:31`; enum `ProcessSchemaParameter.cs:16-26`; written at `:460` |
| `GS2` | Value. **Written only for a formula, a complex-localizable type or a non-localizable type**; a constant of a text type goes to the resources | `ProcessSchemaParameterValue.cs:32`; rule `:462-465`; text type list `:299-309` |
| `GS4` | MetaPath | `:33`; not used by file elements (corpus 0) |
| `GS5` | ModifiedInSchemaUId | `:34`; section 3 |
| `GS8` | DefValueForExistingProcess: the value the platform uses when it ADDS this parameter to an existing element | `:37` |
| `GS9` | DefValueForDcm | `:38`; corpus 0 |

The corpus holds exactly eight shapes (400 entries, corpus 2026-10-01):

| Shape | Count | Meaning | Parameters it occurs on |
|---|---|---|---|
| `{}` | 176 | never set | every output collection and output item; untouched optional inputs |
| `{"GS5":<P>}` | 14 | set by the designer, then cleared to None (`clearSourceValue` keeps the stamp, `NUI/manager/process-flow-element-schema-manager/process-schema-parameter.js:437-447`) | `*DataEntitySchemaUId` for a legacy entry; `ReportName` when a name column was picked; an emptied target |
| `{"GS1":1,"GS2":v,"GS5":<P>}` | 147 | a constant written by the page | object UIds, link columns, filters, sort, `ResultActionType`, `ReportId`, `IsSeparateReports`, changed `RecordsToRead` |
| `{"GS1":1,"GS2":"50","GS5":<T>}` | 12 | the untouched template default | `RecordsToRead` |
| `{"GS1":1,"GS5":<P>}` (no `GS2`) | 5 | a ConstValue whose value is in the resources (`ReportName`, 2) or is null (`TargetDataEntitySchemaUId`, 3) | see sections 7 and 9 |
| `{"GS1":3,"GS2":"[#…#]","GS5":<P>}` | 28 | a mapping or a formula; **the designer never writes source 2 (Mapping)**, every mapping is a Script | `ConnectedObjectId`, `Files`, `Files.File`, a formula `ReportName` |
| `{"GS1":3,"GS2":"true","GS5":<T>,"GS8":"false"}` | 17 | the template copy | `ConsiderTimeInFilter` |
| `{"GS1":3,"GS2":"true","GS5":<T>}` | 1 | the same without `GS8` | `ConsiderTimeInFilter` |

`ResultActionType` is an explicit constant on 30 of 30 elements; no shipped element relies on the unset value.

### 2.5 Mapping-row keys (`BK15` rows, `ProcessSchemaMapping`)

Evidence: `CORE/Terrasoft.Core/Process/ProcessSchemaMapping.cs:20-24`; rows are created by
`ProcessSchemaActivity.CreateProcessSchemaMapping` (`ProcessSchemaActivity.cs:211-224`).

| Key | Property | Designer value |
|---|---|---|
| `A2` | Name | **the element's `A2`**: rows are attributed to an element by name |
| `A3` / `A4` | | `<P>` |
| `GT2` | TargetMetaPath | `[Element:{<E>}].[Parameter:{<element parameter UId>}]` |
| `GT3` | TargetUId | the element parameter UId |
| `GT4` | SourceSchemaUId | `<T>` |
| `GT5` | SourceParameterUId | the TEMPLATE parameter UId (section 4); this is how the design-time sync pairs an element parameter with its template |
| `GT1` | Source: a value snapshot | equals the parameter's `L8` without `GS5` in 388 of 394 rows. The 6 exceptions are `{"GS1":1}` rows on `*DataEntitySchemaUId` parameters whose `L8` was later cleared (corpus-verification C13) |

The designer writes one row per top-level parameter **and one per nested item**: 15 rows for a full-set Object or
Report element (13 + 2), 10 for the 7.17-era Process element (7 + 3). 394 of the 400 entries have a row; the 6
without one are nested items of three Report elements. The rows are design-time pairing for the parameter sync; the
runtime builds its map from the parameters' own values (`CORE/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:1041-1063`,
basis=source).

### 2.6 Data value types that occur

| UId | Name (`CORE/Terrasoft.Core/DataValueType.cs`) | Client enum (`NUI/core/enums/sysenums.js`) | Used by |
|---|---|---|---|
| `a33c9252-d401-453e-949d-169157067ed9` | FileLocator `:297` | `FILE_LOCATOR` 41 (`:244`) | every file item; a single File process parameter |
| `651ec16f-d140-46db-b9e2-825c985a8ac2` | CompositeObjectList `:291-292` | `COMPOSITE_OBJECT_LIST` 39 (`:240`) | every collection; there is no file-collection type |
| `b295071f-7ea9-4e62-8d1a-919bf3732ff2` | Lookup `:235` | `LOOKUP` 10 (`:172`) | `*EntitySchemaUId` (with `L9` = SysSchema `6c7394db-06ff-4050-91ef-8278e21dce15`) |
| `23018567-a13c-4320-8687-fd6f9e3699bd` | Guid `:240` | `GUID` 0 (`:156`) | `ConnectedObjectId`, `ConnectedObjectColumnUId`, `ReportId`, `ReportNameDataSourceColumnUId`, `Id` items |
| `6b6b74e2-820d-490e-a017-2b73d4ccf2b0` | Integer `:175` | | `ResultActionType`, `RecordsToRead` |
| `90b65bf8-0ffc-4141-8779-2420877af907` | Boolean `:104` | | `IsSeparateReports`, `ConsiderTimeInFilter` |
| `394e160f-c8e0-46fa-9c0d-75d97e9e9169` | MetaDataText `:283` | | `DataSourceFilters`, `OrderByInfo` (not localizable: always inline in `GS2`) |
| `8b3f29bb-ea14-4ce5-a5c5-293a929b6ba2` | Text `:129` | | `ReportName` (localizable: a constant goes to the resources) |
| `b7342b7a-…` Binary `:255`; `ba40cfc5-…` "File (BLOB)" `:2168` | | | **never** in a process or user-task parameter (corpus 0) |

Inside filter JSON, the client writes its own type numbers: `26` = MAPPING (`sysenums.js:206`) for a mapped right-hand
value and `10` = LOOKUP for a lookup leaf.

### 2.7 Reading an in-memory designer state (the UO method) against stored keys

UO-3 and UO-4 were read from `Terrasoft.ProcessSchemaManager.items[0].instance.flowElements`, not from storage. The
client model maps onto the stored keys as follows (source):

| Client (`NUI/manager/process-flow-element-schema-manager/process-schema-parameter.js`) | Stored |
|---|---|
| `sourceValue.source` | `L8.GS1` |
| `sourceValue.value` | `L8.GS2`, or the resource `….Value` for a text constant |
| `sourceValue.displayValue` (a LocalizableString) | resource `….DisplayValue` only, never metadata |
| `sourceValue.modifiedInSchemaUId`, set to the process by `updateSchemaMapping` (`:885-891`) whenever `setMappingValue` (`:846-864`) or `setValue` (`:870-882`) changes something. Both return early when the new value equals the old one (`getIsSourceValueEquals`, `:415-432`) | `L8.GS5` |
| `itemProperties` | `L18` |
| `direction`, `isRequired`, `referenceSchemaUId`, `containerUId`, `tag` | `L12`, `L6`, `L9`, `IL2`, `L17` |

So an in-memory reading shows source and value but not provenance. A saved capture is needed for `GS5` and for
everything that lands in the resources.

---

## 3. Provenance stamps: the rules a writer must reproduce

| Rule | Corpus | Mechanism | Builder status |
|---|---|---|---|
| Element `A3`/`A4` = the process; `BL8` = the schema the element was created in | 30/30; `BL8` 26/30 (4 copied) | | `BL8` absent on every builder-made element (existing, generic: `PBD/script-task-element-capture.md`) |
| Element parameter and nested item `A3`/`A4` = the user-task schema `<T>`; `IL2` = `<E>` | 400/400 | copied from the template when the element is created | the platform's sync does the same on `SchemaUId` assignment (`CORE/Terrasoft.Core/Process/ProcessSchemaUserTask.cs:106-115`) |
| Element parameter UIds are fresh; `GT5` ties each back to its template UId | 400/400 fresh | | top level: fresh; **nested: the template's UIds** on a server-built element ([decisions](eng-92719-file-processing-element-decisions.md) D20 re-mints them on create) |
| `GS5` = `<P>` on every value the designer set; `<T>` on an untouched template default | `RecordsToRead`: value `50` with `<T>` 12/12, a changed value with `<P>` 2/2; `ConsiderTimeInFilter` `<T>` 18/18 | client early return on an equal value (section 2.7) | D20 copies template defaults verbatim. An explicit `numberOfRecords: 50` is stamped `<P>`, which the designer cannot produce; hence the GS5 normalisation in section 10 |
| A value cleared after it was set keeps `GS5` = `<P>` with no `GS1` | 14 | `clearSourceValue` | runtime-equivalent to `{}` (no value) |
| The runtime reads an element value only when `GS5` = `<P>` | - | `UseOnlyModifiedParameters` (`CORE/Terrasoft.Core/Process/IProcessParameterValueProvider.cs:85`; filter `ProcessParameterValueProvider.cs:742-745`), basis=source | every builder write must be a NEW value object with `ModifiedInSchemaUId = schema.UId` (D20; G4 a.4) |

---

## 4. The three element variants, key by key

### 4.1 The element envelope (identical for all three)

`PS/CrtInvoice/branches/7.8.0/Schemas/PrintInvoiceReport/metadata.json:884` (product; the stand holds the same
process, measured present 2026-10-01):

```jsonc
{ "BL1": "Terrasoft.Core.Process.ProcessSchemaUserTask",
  "UId": "2f0f2d85-4a06-4e4a-b3d1-4ce46ce68203", "A2": "ReportFileProcessingUserTask1",
  "A3": "<P>", "A4": "<P>", "A5": "7bde2918-…",            // A5 = SysPackage.Id of CrtInvoice
  "IL2": "<lane>", "BL3": "231;172",
  "BL7": "c2bf0416-54c6-6c56-58e0-41162c7795f0",           // = J6
  "BL8": "<P>", "BN2": "69;55", "BO2": true, "BO3": true, "CL2": "FFFFFFFF",
  "J6": "c2bf0416-54c6-6c56-58e0-41162c7795f0",
  "BP2": [ /* 13 parameters, section 4.3 */ ] }
// element caption: resource BaseElements.ReportFileProcessingUserTask1.Caption = "Print Invoice report"
```

Every parameter in `BP2` has this envelope (the fragments below drop it):

```jsonc
{ "BL1": "Terrasoft.Core.Process.ProcessSchemaParameter", "UId": "<fresh>", "A2": "<template name>",
  "A3": "<T>", "A4": "<T>", "IL2": "<E>", "L1": "<type>", "L8": { /* section 2.4 */ } /* , L6, L9, L12, L18 */ }
```

### 4.2 Object attachments: `ObjectFileProcessingUserTask` `9387c794-8d84-5925-ab77-c47e7d876286`

Template: `PD/ObjectFileProcessingUserTask/metadata.json` (`FJ1`); captions in
`PDR/ObjectFileProcessingUserTask.ProcessUserTask/resource.en-US.xml`. Template UIds are what `BK15.GT5` points at.

| Parameter (caption) | Template UId | Type, L6, L12 | Template `L8` | Designer writes: legacy `<X>File` | Designer writes: SysFile |
|---|---|---|---|---|---|
| SourceEntitySchemaUId ("Source object") | `0bca8251-a3e3-7646-d820-34eaa38a419c` | Lookup→SysSchema, L6 | `{}` | const `<X>File` UId, e.g. ContactFile `e9eafee9-c4e4-4793-ad0a-003bd2c6a9b4`; no DisplayValue row | const SysFile `70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a` (UO-3, in memory) |
| DataSourceFilters | `de39b14b-bb98-ad1e-d465-385f79e499ce` | MetaDataText | `{}` | const wrapper JSON, `rootSchemaName` = the `<X>File` (section 6) | the same, root SysFile (predicted; G2) |
| TargetEntitySchemaUId ("Target object") | `272a0430-93be-b8f0-6660-26f28dd038d3` | Lookup→SysSchema, **L6** | `{}` | SaveToFiles: const `<X>File`; Use in process: `{}`. L6 is not enforced: 7 of 7 shipped Use-in-process elements leave it empty | SaveToFiles to a SysFile entry: const SysFile |
| RecordsToRead ("Number of records to read") | `f7f25988-3214-fa45-f594-d4c5f467a3d5` | Integer | `{"GS1":1,"GS2":"50","GS5":<T>}` | template copy, or a changed const with `<P>` | same |
| OrderByInfo ("Columns order") | `64214e9b-933f-f7d6-bee2-50d84b845729` | MetaDataText | `{}` | const `Name:2:1` (section 6.3), or `{}` | same format |
| CreatedObjectFileIds ("Id of created files") + item `Id` | `ed25f049-23b1-da21-8f47-901fda242da7` + `3a3ff2a6-f9d1-4198-84dd-030aeeb0a903` | List Out + Guid Out | `{}` | `{}` | `{}` |
| ConnectedObjectId | `e3ed4c9d-ee0e-1342-d260-2850fe56bc20` | Guid | `{}` | SaveToFiles: Script `[#…#]` with `L9` = the record object | same |
| ConnectedObjectColumnUId | `16365f85-76a8-5f34-526d-02b187987a2e` | Guid | `{}` | SaveToFiles: const link column, e.g. ContactFile.Contact `f442867d-73ca-49b3-a8ba-8a2566b1fc59`, with a DisplayValue row = the same text | const `SysFile.RecordId` `5d2bc4fd-dcd0-2e6a-d194-76812388ad13` (`PS/CrtCoreBase/branches/7.8.0/Schemas/SysFile/metadata.json:142-150`; designer `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:527-541`) |
| ResultActionType ("File action type") | `819a8b59-b345-7b36-a2d8-8bd95b2c03b6` | Integer, **L6** | `{}`, no default | const `"0"` SaveToFiles or `"1"` Use in process; page default `"1"` (`BaseFile…js:894-903`); DisplayValue row `"0"`/`"1"` | same |
| ObjectFiles ("Collection of files") + item `File` | `b80cbf95-72c5-d9ab-58cb-5a49d9e6bd23` + **`1ddb6de7-5ed0-4fb1-984f-941038d9274e`** | List Out + FileLocator Out | `{}` | `{}` | `{}` |
| ConsiderTimeInFilter ("Consider time in the filter") | `0660022b-b5ae-d098-8fd2-8bd2a5f32a67` | Boolean | `{"GS1":3,"GS2":"true","GS5":<T>,"GS8":"false"}` | template copy (the page never writes it; `FilterModuleMixin.saveDataSourceFilters` only reads it, `PD/FilterModuleMixin/FilterModuleMixin.js:831-863`) | same |
| SourceDataEntitySchemaUId ("Source data object") | `89f5bbf1-1a6a-22be-ee2c-3aec92cf5802` | Lookup→SysSchema | `{}` | cleared: `{}` or `{"GS5":<P>}` (`PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:319-328`) | const = the record object, e.g. AccountAddress `8ab0fe8a-0340-41ac-8b09-b11f65dd83da` (UO-3) |
| TargetDataEntitySchemaUId ("Target data object") | `1259ceb2-8fd6-0843-04e1-ec8aeee47e0b` | Lookup→SysSchema | `{}` | cleared (UO-4) | SaveToFiles: const = the record object. Use in process: value-less `{"GS1":1,"GS5":<P>}` (predicted; section 9) |

The legacy encoding is the shipped one. The SysFile column comes from the designer source and the in-memory UO-3/UO-4
readings; no saved SysFile element exists. The per-entity rule that picks the column is in
[platform-reference](eng-92719-file-processing-element-platform-reference.md) §7.3.

**Capture, Use in process** (`PS/CopilotAutoTest/branches/7.8.0/Schemas/SkillFilesValidationProcess/metadata.json:1426`,
`B8` 8.3.1.3829, full set; parameter envelopes dropped):

```jsonc
"J6": "9387c794-8d84-5925-ab77-c47e7d876286",
"BP2": [
  { "A2": "SourceEntitySchemaUId", "L8": {"GS1":1, "GS2":"513b42c9-6e8e-4ae6-92f4-24934ed04a02", "GS5":"<P>"},
    "L6": true, "L9": "6c7394db-…" },                                  // CreatioAIIntentFile, legacy
  { "A2": "DataSourceFilters", "L8": {"GS1":1, "GS2":"{\"className\":\"Terrasoft.FilterGroup\",…}", "GS5":"<P>"} },
  { "A2": "TargetEntitySchemaUId", "L8": {}, "L6": true, "L9": "6c7394db-…" },  // required, yet empty
  { "A2": "RecordsToRead", "L8": {"GS1":1, "GS2":"50", "GS5":"<T>"} },  // untouched default
  { "A2": "OrderByInfo", "L8": {} },
  { "A2": "CreatedObjectFileIds", "L8": {}, "L12": 1, "L18": [ { "A2": "Id", "L8": {}, "L12": 1 } ] },
  { "A2": "ConnectedObjectId", "L8": {} }, { "A2": "ConnectedObjectColumnUId", "L8": {} },
  { "A2": "ResultActionType", "L8": {"GS1":1, "GS2":"1", "GS5":"<P>"}, "L6": true },
  { "UId": "44eecc87-…", "A2": "ObjectFiles", "L8": {}, "L12": 1,
    "L18": [ { "UId": "cf64c3fc-…", "A2": "File", "L1": "a33c9252-…", "L8": {}, "L12": 1 } ] },  // fresh nested UId
  { "A2": "ConsiderTimeInFilter", "L8": {"GS1":3, "GS2":"true", "GS5":"<T>", "GS8":"false"} },
  { "A2": "SourceDataEntitySchemaUId", "L8": {}, "L9": "6c7394db-…" },
  { "A2": "TargetDataEntitySchemaUId", "L8": {"GS1":1, "GS5":"<P>"}, "L9": "6c7394db-…" }    // value-less ConstValue
]
// BK15: 15 rows named "ObjectFileProcessingUserTask1"; the nested row:
// {"GT2":"[Element:{1e4f9888-…}].[Parameter:{cf64c3fc-…}]","GT3":"cf64c3fc-…",
//  "GT4":"9387c794-…","GT5":"1ddb6de7-5ed0-4fb1-984f-941038d9274e","GT1":{}}
```

**Capture, Save to object attachments** (`PS/ProcessTests/branches/7.8.0/Schemas/FileCopyProcessPP/metadata.json:2656`,
lacks `ConsiderTimeInFilter` and both `*DataEntitySchemaUId`). Only the values that differ from the Use-in-process
shape:

```jsonc
{ "A2": "SourceEntitySchemaUId", "L8": {"GS1":1, "GS2":"e9eafee9-…", "GS5":"<P>"} },     // ContactFile
{ "A2": "TargetEntitySchemaUId", "L8": {"GS1":1, "GS2":"e9eafee9-…", "GS5":"<P>"} },     // ContactFile
{ "A2": "RecordsToRead", "L8": {"GS1":1, "GS2":"1", "GS5":"<P>"} },                      // changed -> <P>
{ "A2": "OrderByInfo", "L8": {"GS1":1, "GS2":"Name:2:1", "GS5":"<P>"} },
{ "A2": "ConnectedObjectId", "L1": "23018567-…",
  "L8": {"GS1":3, "GS2":"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{4b003746-…}].[Parameter:{fed35ead-…}].[EntityColumn:{ae0e45ca-c495-4fe7-a39d-3ab7278e1617}]#]", "GS5":"<P>"},
  "L9": "16be3651-8fe2-4159-8dd0-a803d4683dd3" },                                          // record object Contact
{ "A2": "ConnectedObjectColumnUId", "L8": {"GS1":1, "GS2":"f442867d-…", "GS5":"<P>"} },  // ContactFile.Contact
{ "A2": "ResultActionType", "L8": {"GS1":1, "GS2":"0", "GS5":"<P>"}, "L6": true }
```

### 4.3 Generated report: `ReportFileProcessingUserTask` `c2bf0416-54c6-6c56-58e0-41162c7795f0`

Template: `PD/ReportFileProcessingUserTask/metadata.json`; captions in
`PDR/ReportFileProcessingUserTask.ProcessUserTask/resource.en-US.xml`.

| Parameter (caption) | Template UId | Type, L6, L12 | Template `L8` | Designer writes |
|---|---|---|---|---|
| DataSourceFilters | `4aff901d-fa78-6b52-0117-5a71be08e927` | MetaDataText | `{}` | const wrapper JSON rooted on the report entity, e.g. `Invoice` (section 6.2) |
| IsSeparateReports ("Generate a separate report for each record") | `fbdf3f95-2ec5-3f5d-3d82-7f2dc480d004` | Boolean, **L6** | `{}` | const `"true"`/`"false"`; forced `"true"` for an MS Word printable (all 12 Word elements: `true`; both FastReport elements: `false`) |
| ReportId ("Report Id") | `edcfbf9e-502c-6560-efb0-214a986def22` | Guid, **L6** | `{}` | const `SysModuleReport.Id`, e.g. Invoice `e1f1a474-1a77-82f8-4bc7-b1da23699e13`, DisplayValue row = the same text |
| ResultActionType ("Report action type") | `df14becf-6454-a55a-78ce-bb11f3e30eac` | Integer, **L6** | `{}` | const `"0"`/`"1"`, page default `"1"` |
| ReportName ("File name") | `a98ea4e2-3b1e-c950-d6fd-2a8ca6cdb4e9` | **Text** | `{}` | constant: `{"GS1":1,"GS5":<P>}` + resources (section 7); formula: Script inline; column picked: cleared `{"GS5":<P>}` |
| CreatedObjectFileIds + item `Id` | `74c1b25a-d7c0-fbd2-96c3-5030b5e60854` + `44af58f6-4acb-4b7e-8d2e-bbf3df3c661b` | List Out + Guid Out | `{}` | `{}` |
| ConnectedObjectColumnUId | `aa4775cf-516b-97fe-6f83-b436ed6967d3` | Guid | `{}` | const link column (Invoice: InvoiceFile.Invoice `68b6e313-9f26-471f-9dcc-ca8200b57895`); GenerateDNSRecordsSpecification leaves it empty (an orphan file object) |
| ConnectedObjectId | `f556b7e6-8e8b-f341-161b-b1485d72d2fa` | Guid | `{}` | Script with `L9` = the record object; DisplayValue row e.g. `[#Invoice#]` |
| TargetEntitySchemaUId ("Target object") | `543caa3e-7315-f608-356b-5f463581844e` | Lookup→SysSchema, **not** L6 | `{}` | const `<X>File` (InvoiceFile `e7d51836-a27a-4e52-979c-29d114414085`) |
| ReportFiles ("Collection of reports") + item `File` | `73fdf30e-bf9f-b6ea-f9ad-027fa10b7078` + **`efe60cce-51e4-43bb-850d-0500582c691a`** | List Out + FileLocator Out | `{}` | `{}` |
| ReportNameDataSourceColumnUId ("Column Id from selection for file name") | `b86c2795-e007-2963-f62e-87e33c8e77b7` | Guid | `{}` | const column UId of the report entity (Invoice.Number `fdd77a82-fa25-4c0f-94d6-56cf0254521f`), only with IsSeparateReports |
| ConsiderTimeInFilter | `a7ad6936-4f28-b7e9-c2dd-ba9ae36b09bc` | Boolean | template copy, as Object | template copy |
| TargetDataEntitySchemaUId | `9d655eda-1618-4ae9-a24e-a4773404f490` | Lookup→SysSchema | `{}` | legacy: cleared; SysFile target: the record object (predicted) |

**Capture** (`PrintInvoiceReport/metadata.json:884`, product, `B8` 8.1.0.6428, full set):

```jsonc
"J6": "c2bf0416-54c6-6c56-58e0-41162c7795f0",
"BP2": [
  { "A2": "DataSourceFilters", "L8": {"GS1":1, "GS2":"{…rootSchemaName \"Invoice\"; Id = [Parameter:{444d03a0-…}]…}", "GS5":"<P>"} },
  { "A2": "IsSeparateReports", "L8": {"GS1":1, "GS2":"true", "GS5":"<P>"}, "L6": true },
  { "A2": "ReportId", "L8": {"GS1":1, "GS2":"e1f1a474-1a77-82f8-4bc7-b1da23699e13", "GS5":"<P>"}, "L6": true },
  { "A2": "ResultActionType", "L8": {"GS1":1, "GS2":"0", "GS5":"<P>"}, "L6": true },
  { "A2": "ReportName", "L1": "8b3f29bb-…", "L8": {"GS5":"<P>"} },          // a name column is used instead
  { "A2": "CreatedObjectFileIds", "L8": {}, "L12": 1, "L18": [ { "A2": "Id", "L8": {}, "L12": 1 } ] },
  { "A2": "ConnectedObjectColumnUId", "L8": {"GS1":1, "GS2":"68b6e313-…", "GS5":"<P>"} },   // InvoiceFile.Invoice
  { "A2": "ConnectedObjectId", "L8": {"GS1":3, "GS2":"[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{444d03a0-…}]#]", "GS5":"<P>"},
    "L9": "bfb313dd-…" },                                                          // Invoice
  { "A2": "TargetEntitySchemaUId", "L8": {"GS1":1, "GS2":"e7d51836-…", "GS5":"<P>"}, "L9": "6c7394db-…" },  // InvoiceFile
  { "A2": "ReportFiles", "L8": {}, "L12": 1, "L18": [ { "A2": "File", "L1": "a33c9252-…", "L8": {}, "L12": 1 } ] },
  { "A2": "ReportNameDataSourceColumnUId", "L8": {"GS1":1, "GS2":"fdd77a82-…", "GS5":"<P>"} },  // Invoice.Number
  { "A2": "ConsiderTimeInFilter", "L8": {"GS1":3, "GS2":"true", "GS5":"<T>", "GS8":"false"} },
  { "A2": "TargetDataEntitySchemaUId", "L8": {"GS5":"<P>"}, "L9": "6c7394db-…" }               // cleared (legacy)
]
// BK15: 15 rows (13 + 2 nested)
```

A formula suffix stays inline (`PS/CrtLeadOppMgmtApp/branches/7.8.0/Schemas/PrintQuotationReport/metadata.json`,
`ReportName`):
`{"GS1":3, "GS2":"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{f9758d08-…}].[Parameter:{72cd5708-…}].[EntityColumn:{790563cf-…}]#]+\" \"+[#SysVariable.CurrentDateTime#]", "GS5":"<P>"}`.

### 4.4 Process parameter: `ProcessFileProcessingUserTask` `6c620dd2-026e-560c-489f-030c5be5f2c3`

Template: `PD/ProcessFileProcessingUserTask/metadata.json`; captions in
`PDR/ProcessFileProcessingUserTask.ProcessUserTask/resource.en-US.xml`. That resource file has **no caption for any
nested item** (`Files.File`, `ObjectFiles.ObjectFile`, `CreatedObjectFileIds.Id`); a process stores the item name as
the caption (`FileParameterProcess` resources: `ObjectFiles.ObjectFile.Caption = "ObjectFile"`).

| Parameter | Template UId | Type, L6, L12 | Designer writes |
|---|---|---|---|
| ObjectFiles + item **`ObjectFile`** (the COPIES) | `8adc2a26-256b-c933-bd58-c21de291a62c` + `2d5e0436-1617-46b2-bead-523247269e8b` | List Out + FileLocator Out | `{}` |
| ResultActionType | `6c3b7ae8-e3d5-412c-ee1b-308bcedae842` | Integer, **L6** | always const `"0"` (`PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:174-177`) |
| ConnectedObjectColumnUId | `3e5bc847-8a30-9aa3-1004-daae47c7f2ab` | Guid | const link column (always: the page treats the variant as a copy) |
| ConnectedObjectId | `43c60e4a-2ebe-f829-c824-1cb974ef14bd` | Guid | Script with `L9` = record object |
| TargetEntitySchemaUId | `0b4a2b0a-ce56-6bf0-d280-9bc2c78bf7b6` | Lookup→SysSchema, **L6** | const `<X>File` or SysFile |
| CreatedObjectFileIds + item `Id` | `b0cffa17-06a3-864e-d512-940b2e54359f` + `349634bd-7d9b-49ae-9d78-b39d67903a92` | List Out | `{}` |
| **Files** + item **`File`** | `17a54879-4fbf-6a72-0e98-5b2e5c165a23` + **`e5903ef0-2223-451f-ba8d-586ad28596c2`** | List **In (`L12: 0`), L6** + FileLocator In | section 5 |
| TargetDataEntitySchemaUId | `6cacb38f-8614-fa7e-dde1-246766c1357f` | Lookup→SysSchema | **never captured** (both shipped elements predate it) |

**Capture** (`PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:1133`,
`ProcessFileProcessingUserTask2`, `B8` 7.17.3.396, 7 of 8 parameters):

```jsonc
"J6": "6c620dd2-026e-560c-489f-030c5be5f2c3",
"BP2": [
  { "A2": "ObjectFiles", "L8": {}, "L12": 1, "L18": [ { "A2": "ObjectFile", "L1": "a33c9252-…", "L8": {}, "L12": 1 } ] },
  { "A2": "ResultActionType", "L8": {"GS1":1, "GS2":"0", "GS5":"<P>"}, "L6": true },
  { "A2": "ConnectedObjectColumnUId", "L8": {"GS1":1, "GS2":"f442867d-…", "GS5":"<P>"} },   // ContactFile.Contact
  { "A2": "ConnectedObjectId", "L8": {"GS1":3, "GS2":"[#…[Element:{7abd8ceb-…}].[Parameter:{156be1c7-…}].[EntityColumn:{ae0e45ca-…}]#]", "GS5":"<P>"},
    "L9": "16be3651-…" },
  { "A2": "TargetEntitySchemaUId", "L8": {"GS1":1, "GS2":"e9eafee9-…", "GS5":"<P>"}, "L6": true, "L9": "6c7394db-…" },
  { "A2": "CreatedObjectFileIds", "L8": {}, "L12": 1, "L18": [ { "A2": "Id", "L8": {}, "L12": 1 } ] },
  { "UId": "a596d4b2-…", "A2": "Files", "L6": true, "L12": 0,
    "L8": {"GS1":3, "GS2":"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{56d91458-…}].[Parameter:{1357783a-…}]#]", "GS5":"<P>"},
    "L18": [ { "UId": "4dc48a23-…", "A2": "File", "L1": "a33c9252-…", "L12": 0,
               "L8": {"GS1":3, "GS2":"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{56d91458-…}].[Parameter:{e2d5944a-…}]#]", "GS5":"<P>"} } ] }
]
// 1357783a = ObjectFileProcessingUserTask5.ObjectFiles, e2d5944a = its nested File (:1593, :1604)
// BK15: 10 rows; the two Files rows carry the mapping in GT1 (:170-197)
```

### 4.5 Parameter-set drift in the corpus

The platform adds a missing template parameter (with its `GS8` value) and retypes a mistyped one on every design-time
load ([decisions](eng-92719-file-processing-element-decisions.md) D20; G4 a.1). A stored snapshot is therefore not
what the designer would write today. Per element (corpus):

| Parameter missing | Elements | Created in (`B8`) |
|---|---|---|
| none (full set) | 9: AutoTestUC Report1; CallCreatioAIProcessElementFiles Object1; SkillFilesValidationProcess Object1, Object2; GenerateDNSRecordsSpecification Report2, Report3; PrintInvoiceReport, PrintQuotationReport, PrintContractsReport Report1 | 8.0.10.3825 to 8.3.2.217 |
| `TargetDataEntitySchemaUId` only | Process ×2, Report ×7 | 0.0.0.0 to 8.0.4.826 |
| `SourceDataEntitySchemaUId` + `TargetDataEntitySchemaUId` | 2 Object | 0.0.0.0, 7.18.2.172 |
| `ConsiderTimeInFilter` + both `*DataEntitySchemaUId` | 9 Object: 6 of the 7 SaveToFiles Object elements, plus 3 Use-in-process ones | 0.0.0.0 to 7.17.3.396 |
| `ConsiderTimeInFilter` + `TargetDataEntitySchemaUId` | 1 Report | 0.0.0.0 |

`ConnectedObjectId` is typed Lookup `b295071f` instead of Guid in 7 elements, among them the product
PrintContractsReport. A reader must accept both types (M13 decides whether describe returns the converged type).

---

## 5. Nested-item mappings

| Fact | Evidence | Basis |
|---|---|---|
| A collection bound to a collection is bound at BOTH levels: the outer parameter to the source collection, the nested item to the source's nested item, each with its own Script `[#…#]` and its own `GS5` = `<P>` | 11 of 11 file-collection bindings in the 16 files (Send email ×4, Creatio.ai ×2, multi-instance sub-process ×2, Process variant ×2, process parameter ×1); 0 outer-only (corpus-verification C6) | corpus |
| The nested item's token names the source's NESTED UId directly, with no segment for the parent: `[Element:{el}].[Parameter:{itemUId}]` for an element item, `[Parameter:{itemUId}]` for a process-level item | `FileParameterProcess/metadata.json:1272` (element item), `:1432` (process item `a89b2b38…`); `GetMetaPath` short form when `ContainerUId` is Empty or the process (`CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:1097-1110`, `:1103`) | corpus + source |
| The designer writes a `BK15` row for the nested item, `GT5` = the template's nested UId (`e5903ef0…` for `Files.File`), `GT1` = the mapping | `FileParameterProcess/metadata.json:186-197` | corpus |
| The Process-variant page edits only the nested `Files.File`; when the picked source is itself a nested item, it also maps the outer `Files` to the source's collection; when the source is a single File, the outer stays untouched | `PD/MappingEditMixin/MappingEditMixin.js:1132-1156`, `:947-962`, `:1075-1096`; page `ProcessFileProcessingUserTaskPropertiesPage.js:44-57, :73-79` | source |
| A single-File source therefore produces a NESTED-ONLY binding: `Files` `{}` and `Files.File` = `[#…[Parameter:{<File parameter UId>}]#]` | no shipped instance; SC-1 E10 stores one, M1 runs one | predicted |

**Display forms** are resources, never metadata. `getFullCaption` joins parent and item with `:`
(`NUI/manager/process-flow-element-schema-manager/process-schema-parameter.js:679-684`), prefixed by
`<element caption>.` (`NUI/manager/process-schema-manager/formula-parser-utils.js:163-175`). The colon form appears
only while the client feature `ProcessParameterCollections` is on (`:167`); the stand's `Web.config` sets
`Feature-ProcessParameterCollections = true` (stand research, source). Shipped rows
(`PS/ProcessTests/branches/7.8.0/Resources/FileParameterProcess.Process/resource.en-US.xml`):

| Key | Value | Line |
|---|---|---|
| `BaseElements.ProcessFileProcessingUserTask2.Parameters.Files.DisplayValue` | `[#GetFile.Collection of files#]` | `:25` |
| `BaseElements.ProcessFileProcessingUserTask2.Parameters.Files.File.DisplayValue` | `[#GetFile.Collection of files:File#]` | `:27` |
| `BaseElements.ProcessFileProcessingUserTask3.Parameters.Files.DisplayValue` | `[#FileCollection#]` | `:40` |
| `BaseElements.ProcessFileProcessingUserTask3.Parameters.Files.File.DisplayValue` | `[#FileCollection:FileCollectionParameter#]` | `:42` |
| `Parameters.FileCollection.DisplayValue` / `Parameters.FileCollection.FileCollectionParameter.DisplayValue` | `[#GetFile.Collection of files#]` / `[#GetFile.Collection of files:File#]` | `:96`, `:98` |

`GetFile` is the source element's caption (`BaseElements.ObjectFileProcessingUserTask5.Caption`, `:5`). The card of
the Process variant shows the stored nested display value as is. A builder that stores the bare item caption ("File")
shows "File" on the card ([decisions](eng-92719-file-processing-element-decisions.md) D20 fixes the form; M14 checks).

---

## 6. Filter and sort values

### 6.1 The `DataSourceFilters` wrapper

`GS2` is a JSON object holding **two JSON strings** (`PD/FilterModuleMixin/FilterModuleMixin.js:831-863`):

| Member | Content |
|---|---|
| `className` | `"Terrasoft.FilterGroup"` |
| `serializedFilterEditData` | the editor's form: `className` on every node, `leftExpressionCaption`, `displayValue` of mapped values, `rootSchemaName`, the filter `key`s. Captions are frozen in the authoring culture; they are not resources |
| `dataSourceFilters` | the lean form the runtime reads (`PB/Filters/ProcessFilterService.cs:22`, `PB/Filters/FilterDescriptorReader.cs:62`): no `className`, no captions; parameter display values removed by `deleteParameterDisplayValues` |

The page writes it only when the filter changed. Before writing, it applies `ConsiderTimeInFilter`: every date-time
comparison gets `trimDateTimeParameterToDate = !ConsiderTimeInFilter`. The stored `rootSchemaName` is ignored at run
time; the runtime root is `SourceEntitySchemaUId` (Object) or the report entity (Report)
([platform-reference](eng-92719-file-processing-element-platform-reference.md) §5; G2). A diff must parse both inner
strings; comparing `GS2` as text compares random filter keys.

### 6.2 Decoded record scopes (corpus)

Legacy Object scope, `FileParameterProcess` `ObjectFileProcessingUserTask5`, `serializedFilterEditData` (trimmed):

```jsonc
{ "className": "Terrasoft.FilterGroup", "filterType": 6, "logicalOperation": 0, "isEnabled": true,
  "rootSchemaName": "ContactFile", "key": "",
  "items": { "5bcd1298-…": {
      "className": "Terrasoft.InFilter", "filterType": 4, "comparisonType": 3,       // IN, equal
      "leftExpression": { "className": "Terrasoft.ColumnExpression", "expressionType": 0, "columnPath": "Contact" },
      "dataValueType": 10, "referenceSchemaName": "Contact", "leftExpressionCaption": "Contact",
      "trimDateTimeParameterToDate": false, "isAggregative": false, "key": "5bcd1298-…",
      "rightExpressions": [ { "className": "Terrasoft.ParameterExpression", "expressionType": 2,
          "parameter": { "className": "Terrasoft.Parameter", "dataValueType": 26,          // MAPPING
            "value": { "value": "[IsOwnerSchema:false].[IsSchema:false].[Element:{150f3842-…}].[Parameter:{43c023b8-…}].[EntityColumn:{ae0e45ca-…}]",
                       "displayValue": "ContactFrom.First item of resulting collection.Id", "Id": "3c9adcee-…" } } } ] } } }
```

The mapped right-hand value is a metapath WITHOUT `[#…#]` (unlike a parameter value). The `dataSourceFilters` twin
is the same tree without `className`, captions and `displayValue`; it keeps the value's `Id`.

Report scope, `PrintInvoiceReport`: a `Terrasoft.CompareFilter` (`filterType` 1, `comparisonType` 3) on `columnPath`
`"Id"` with a `dataValueType` 26 parameter `[IsOwnerSchema:false].[IsSchema:false].[Parameter:{444d03a0-…}]`
(display `"Invoice"`), `rootSchemaName` `"Invoice"`. The SysFile scope (`RecordId = <record>`) has **no capture**.
Source predicts a leaf on `RecordId` that the editor re-types as a LOOKUP of the record object
([platform-reference](eng-92719-file-processing-element-platform-reference.md) §7.6); M7 reads it in memory, and SC-1
E3 stores it.

### 6.3 `OrderByInfo`

Format `<ColumnName>:<OrderDirection>:<OrderPosition>[,…]`. The designer writes `1` as the position of the FIRST
entry and `0` for every later one (`PD/SortingOrderControlsMixin/SortingOrderControlsMixin.js:371-399`). Direction is
the client `Terrasoft.OrderDirection`: ASC 1, DESC 2 (`NUI/core/enums/sysenums.js:686-692`). The runtime splits on
`,` and `:`, skips an entry without exactly 3 parts, parses the direction as `Terrasoft.Common.OrderDirection`, and
adds the column to the query if it is missing (`PD/ProcessUserTaskUtilities/ProcessUserTaskUtilities.cs:455-472`).
An empty sort is source None (`{}` or `{"GS5":<P>}`). Corpus values: `Name:2:1` (FileCopyProcessPP);
`SysFileStorage:1:1,Contact:1:0` (DepTestProcess). The column name alone does not say whether it was picked from the
file object or from the record object; this is the SysFile sort trap ([traps](eng-92719-file-processing-element-traps.md); M9).

---

## 7. Localizable constants and other resource rows

**Where resources live.** Element-level rows are keyed `BaseElements.<element A2>.<…>`
(`CORE/Terrasoft.Core/Process/ProcessSchema.cs:1410-1417`). A parameter binds `<group>.Caption`,
`<group>.Value` (text types and MetaDataText) and `<group>.DisplayValue`, recursing into nested items as
`<group>.<item name>.…` (`CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:915-949`). Process-level parameters
use `Parameters.<Name>.…` (`BaseProcessUserTaskSchema.cs:142`). Stored rows: package
`Resources/<Process>.Process/resource.<culture>.xml`, or the `SysLocalizableValue` table (`Key`, `Value`,
`SysCultureId`, `SysSchemaId`; `CORE/Terrasoft.Core/ConfigurationActivityLog/SchemaLocalizationChangeLogRecordProvider.cs:252-259`).

**`ReportName` is the only localizable value of the three schemas.** Its type is Text, so a constant is written to
`LocalizableValue` (`Value` property, `ProcessSchemaParameterValue.cs:206-231`) and the writer omits `GS2`
(`:462-465`):

| Form | Metadata | Resources (authoring culture) | Evidence |
|---|---|---|---|
| constant `aaa` | `{"GS1":1, "GS5":<P>}` | `BaseElements.ReportFileProcessingUserTask1.Parameters.ReportName.DisplayValue = "aaa"` and `….ReportName.Value = "aaa"` | `PS/DepTest_Level3/branches/7.8.0/Resources/DepTestProcess.Process/resource.en-US.xml:677-679` |
| constant `Report` | `{"GS1":1, "GS5":<P>}` | `….ReportFileProcessingUserTask4.Parameters.ReportName.DisplayValue` / `.Value = "Report"` | `PS/ProcessTests/branches/7.8.0/Resources/FileReportNamesProcess.Process/resource.en-US.xml:61-62` |
| formula | `{"GS1":3, "GS2":"…", "GS5":<P>}` | DisplayValue row only | PrintQuotationReport, GenerateDNSRecordsSpecification |
| name column instead | `ReportName` `{"GS5":<P>}`; `ReportNameDataSourceColumnUId` const | DisplayValue `[#Selection result.<caption>#]` | designer-client §1.5; PrintInvoiceReport |

- **Culture.** `FileReportNamesProcess` ships four cultures. `ReportName.Value` and every `DisplayValue` row exist in
  `en-US` only; `ru-RU`, `ar-SA` and `he-IL` carry translated captions and nothing else (corpus). A builder writes the
  value under the request culture, and the read side falls back to the default culture. A constant written under a
  non-default profile culture is M18 ([decisions](eng-92719-file-processing-element-decisions.md) D17).
- **Resources can lie.** `PrintContractsReport` has `ReportName` with no source in metadata, yet its resource carries
  `ReportName.Value = "[#Selection result.Number#]"`. `PortalReportbySignal`'s resources describe a deleted element.
  Decide "set" from the metadata `GS1`, never from a resource row (corpus.md §6, items 4 and 14).

**Other rows a designer save leaves** (PrintInvoiceReport, `PS/CrtInvoice/branches/7.8.0/Resources/PrintInvoiceReport.Process/resource.en-US.xml`):

| Row | Present for | Absent for |
|---|---|---|
| `….<Param>.Caption` | every parameter and every nested item (copied from the template's resources) | - |
| `….<Param>.DisplayValue` = the value text | constants written through `saveParameter` (`ConnectedObjectColumnUId`, `ReportId`, `ResultActionType`, `IsSeparateReports`, `ReportNameDataSourceColumnUId`), `ConsiderTimeInFilter` (`"true"`), and mappings (`ConnectedObjectId` = `[#Invoice#]`) | the schema lookups written through `saveReferenceSchemaUId` with no display text (`Source/TargetEntitySchemaUId`, `FilterModuleMixin.js:809-822`), the filters, cleared values, and outputs |

---

## 8. File and file-collection process parameters (ENG-95984 File process parameter type)

Designer captures (`PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:13-53`, `B8`
7.17.3.396; `PS/ProcessLibrary/branches/7.8.0/Schemas/MarkProcessesToCancel/metadata.json:76-91`, product):

```jsonc
"FJ1": [
  { "BL1": "Terrasoft.Core.Process.ProcessSchemaParameter", "UId": "696b79d7-…", "A2": "FileParameter",
    "A3": "<P>", "A4": "<P>", "L1": "a33c9252-d401-453e-949d-169157067ed9", "L8": {"GS5":"<P>"} },
    // File, plain Add: no IL2, no L6, no L9, no L12 (= Variable), no L17, no L18
  { "UId": "ac1857ae-…", "A2": "FileCollection", "A3": "<P>", "A4": "<P>", "L1": "651ec16f-…",
    "L8": {"GS1":3, "GS2":"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{56d91458-…}].[Parameter:{1357783a-…}]#]", "GS5":"<P>"},
    "L18": [ { "UId": "a89b2b38-…", "A2": "FileCollectionParameter", "A3": "<P>", "A4": "<P>", "L1": "a33c9252-…",
               "L8": {"GS1":3, "GS2":"[#…[Element:{56d91458-…}].[Parameter:{e2d5944a-…}]#]", "GS5":"<P>"} } ] }
    // Variable collection, bound at both levels from an Object element's ObjectFiles(.File)
]
// MarkProcessesToCancel (product):
{ "UId": "d78362ad-…", "A2": "FilesCollection", "A3": "<P>", "A4": "<P>", "L1": "651ec16f-…",
  "L8": {"GS5":"<P>"}, "L12": 1,                                     // Out, unbound (filled by a script task)
  "L18": [ { "UId": "d2b4b481-…", "A2": "File", "A3": "<P>", "A4": "<P>", "L1": "a33c9252-…", "L8": {"GS5":"<P>"} } ] }
                                                                     // item: no L12 (= Variable) under an Out root
```

| Fact | Count / evidence | Basis |
|---|---|---|
| Process-level parameters and items carry **no `IL2`** | 0 of 3,335 top-level process parameters and 0 of 678 nested items in the 7.8.0 JSON process schemas (corpus, 2026-10-01) | corpus |
| No process-level collection or item carries a Tag | 0 of 133 collections, 0 of 614 items (corpus-verification C12) | corpus |
| Captions: `Parameters.<Name>.Caption`, `Parameters.<Coll>.<Item>.Caption`; a bound one also gets `….DisplayValue` | `FileParameterProcess` resources `:95-98`; `MarkProcessesToCancel` resources `:24-25` | corpus |
| Plain Add > Other > File writes Variable, no Tag; Add > Other > Collection of records writes an EMPTY collection; the nested Add writes the item with Variable passed but not shown | `PD/ProcessSchemaPropertiesPage/ProcessSchemaPropertiesPage.js:1204-1236`; designer-client §5 | source |
| "Create from element" writes Out plus Tags on a COMPOSITE_OBJECT root | `ProcessSchemaPropertiesPage.js:665-715`; 0 shipped file instances | source; not a parity target ([decisions](eng-92719-file-processing-element-decisions.md) D2) |

**What the builder will write** (D2, D3; basis=source): `type: File` gives `L1` FileLocator with no `L12` and no
Tag, which matches `FileParameter`. `type: FileCollection` gives CompositeObjectList with exactly one item `File`
(caption "File", FileLocator, Variable, no Tag); the root is Variable by default (no `L12`, matching `FileCollection`;
D3, agreed 2026-10-07) or Out when declared so (`L12: 1`, matching `FilesCollection`). One predicted difference is named in both comparison rules (TC-41, and the
ENG-95984 File process parameter type AC-8 rule):
`ProcessParameterService.AddProcessParameter` sets `ContainerUId = schema.UId` on every process parameter and every
cloned item (`PB/Parameters/ProcessParameterService.cs:65, :478`). The writer emits `IL2` whenever ContainerUId is
not Empty (`CORE/Terrasoft.Core/Process/BaseProcessSchemaElement.cs:217`). So a builder-made process parameter
stores `"IL2": "<P>"` where the designer stores nothing. The meta path is identical either way (`GetMetaPath`,
`ProcessSchemaParameter.cs:1103`), and the same difference already ships for ENG-96230 Collection process parameter
type collections. The comparison treats it as equivalent (section 10, N9); M22 checks that the designer opens such a
parameter.

---

## 9. UO-3 and UO-4: the in-memory states and what they save to

The measured tables are in [platform-reference](eng-92719-file-processing-element-platform-reference.md) §7.4 and
are not repeated here. Below is what a designer SAVE of the same states is expected to write: the source/value pair
is measured, `GS5` and the resource rows are predicted from section 2.7 and the corpus shapes. SC-1 E5 and E2 measure
these predictions.

**UO-3, SysFile source `Account address`, defaults untouched, "Use in process"** (predicted):

```jsonc
{ "A2": "SourceEntitySchemaUId",     "L8": {"GS1":1, "GS2":"70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a", "GS5":"<P>"} },  // SysFile
{ "A2": "SourceDataEntitySchemaUId", "L8": {"GS1":1, "GS2":"8ab0fe8a-0340-41ac-8b09-b11f65dd83da", "GS5":"<P>"} },  // AccountAddress
{ "A2": "DataSourceFilters", "L8": {"GS1":1, "GS2":"{\"className\":\"Terrasoft.FilterGroup\",… empty items, rootSchemaName \"SysFile\" (predicted) …}", "GS5":"<P>"} },
{ "A2": "RecordsToRead",     "L8": {"GS1":1, "GS2":"50", "GS5":"<T>"} },         // untouched: template stamp
{ "A2": "ResultActionType",  "L8": {"GS1":1, "GS2":"1", "GS5":"<P>"} },          // page default, written
{ "A2": "ConsiderTimeInFilter", "L8": {"GS1":3, "GS2":"true", "GS5":"<T>", "GS8":"false"} },
{ "A2": "TargetDataEntitySchemaUId", "L8": {"GS1":1, "GS5":"<P>"} },            // see note
{ "A2": "TargetEntitySchemaUId", "L8": {} }, { "A2": "OrderByInfo", "L8": {} },
{ "A2": "ConnectedObjectId", "L8": {} }, { "A2": "ConnectedObjectColumnUId", "L8": {} }
```

Note on `TargetDataEntitySchemaUId`: with the flag on, every save calls `saveReferencedDataSchemas`
(`BaseFile…js:841-862`). The "is a file-object entry" flag is set only by the target-change handler, so on a
Use-in-process element it is undefined and the page calls `setParameterConstValue(…, undefined)`
(`PD/ProcessSchemaParametersEditMixin/ProcessSchemaParametersEditMixin.js:201-206`). That stores a value-less
ConstValue, the shape that 3 of 14 shipped Object elements carry (SkillFilesValidationProcess, section 4.2). The UO-3
reading recorded "unset" without separating source 1 with no value from source 0. Both are "no value" at run time
(`FindEntitySchemaName` returns "" for an empty UId, platform-reference §7.2).

**UO-4, legacy source `Account (File and link of account)`, "Save to object attachments" to `Contact (Contact
attachment)`** (predicted; the record left empty in UO-4 must be set before the designer lets the panel save):

```jsonc
{ "A2": "SourceEntitySchemaUId", "L8": {"GS1":1, "GS2":"149d2eaf-cbd2-49fa-b565-637748ff823c", "GS5":"<P>"} },  // AccountFile
{ "A2": "SourceDataEntitySchemaUId", "L8": {} },        // or {"GS5":<P>}: cleared for a file-object entry
{ "A2": "TargetEntitySchemaUId", "L8": {"GS1":1, "GS2":"e9eafee9-c4e4-4793-ad0a-003bd2c6a9b4", "GS5":"<P>"} },  // ContactFile
{ "A2": "TargetDataEntitySchemaUId", "L8": {} },        // or {"GS5":<P>}: cleared
{ "A2": "ConnectedObjectColumnUId", "L8": {"GS1":1, "GS2":"f442867d-73ca-49b3-a8ba-8a2566b1fc59", "GS5":"<P>"} },  // ContactFile.Contact
{ "A2": "ConnectedObjectId", "L8": {"GS1":3, "GS2":"[#…#]", "GS5":"<P>"}, "L9": "16be3651-…" },   // once a record is set
{ "A2": "ResultActionType", "L8": {"GS1":1, "GS2":"0", "GS5":"<P>"} },
{ "A2": "DataSourceFilters", "L8": {"GS1":1, "GS2":"{… rootSchemaName \"AccountFile\" …}", "GS5":"<P>"} }
// RecordsToRead and ConsiderTimeInFilter as UO-3
```

**The two encodings side by side** (predicted for SysFile, corpus for legacy):

| Key | Legacy | SysFile |
|---|---|---|
| `Source/TargetEntitySchemaUId` | `<X>File` | SysFile `70ec5d9f…` |
| `Source/TargetDataEntitySchemaUId` | no value | the record object |
| `ConnectedObjectColumnUId` | the `<X>` lookup column of `<X>File` | `SysFile.RecordId` `5d2bc4fd-dcd0-2e6a-d194-76812388ad13` |
| Filter root / leaf | `<X>File`; InFilter on `<X>` (dataValueType 10) | SysFile; leaf on `RecordId` (shape unknown, M7) |
| `ConnectedObjectId.L9` | the record object | the record object |

---

## 10. Builder output against designer output, and the comparison rule

This table is the comparison rule. The test plan's TC-41 rule (also used by TC-59, TC-69, DT-02 and DT-04) is this
table for elements. The ENG-95984 File process parameter type AC-8 rule (its test-plan section 4.2) takes the
process-parameter rows. The table lists every known or predicted difference and says how the comparison treats it.
"Today" means the generic route on 1.6.6.54, where M14 measured it ([decisions](eng-92719-file-processing-element-decisions.md) D13; G4 f.1);
the generic route's code is unchanged on 1.6.6.85.

| # | Key | Designer | Builder today | After D20 | Comparison | Measured by |
|---|---|---|---|---|---|---|
| N1 | element `UId`, `A2`, `BL3`, `IL2` (lane) | fresh, auto-name, layout | caller's | caller's | ignore; pair elements by caption | - |
| N2 | `BL8` | `<P>` | absent on every builder element | absent | **tolerated deviation**, generic and not file-specific. It is not fixed in this work, and no Sub-task is proposed for it (decisions D-5). The package's script-task capture records the gap for every element it builds (`PBD/script-task-element-capture.md`) | SC-4 |
| N3 | `BO2` | `true` | absent | `true` (`SerializeToDB = taskSchema.SerializeToDB`) | must match | M14, SC-4 |
| N4 | `A5` | package Id | filled by the save | same | must match | SC-4 |
| N5 | top-level parameter UIds | fresh | fresh | fresh | ignore (paired by `A2`) | - |
| N6 | nested item UIds and their `IL2` | fresh, `IL2` = `<E>` | the template's UIds (`1ddb6de7…`, `efe60cce…`, `2d5e0436…`, `e5903ef0…`), `IL2` empty | fresh on create, `IL2` = `<E>` | `UId` ignored; `IL2` = `<E>` must match | M14 |
| N7 | `GS5` | `<T>` on untouched defaults, `<P>` on set values | `<T>` for platform-synced defaults | template copies keep `<T>`; caller-chosen values `<P>` | normalise: for a value equal to the template default, `<T>` and `<P>` are equal. Every other `GS5` must match | SC-4 |
| N8 | `ConsiderTimeInFilter` | Script `"true"`, `<T>`, `GS8 "false"` | Script `"false"` from `GS8` (G4 a.2) | pinned to the template copy on create | must match | M14 |
| N9 | process-level parameter `IL2` | absent | `<P>` (section 8) | `<P>` | **tolerated deviation**, named: absent and `<P>` compare equal, because the meta path is the same either way (section 8, basis=source). The classification table records it as tolerated N9, not as normalised. The ENG-95984 File process parameter type AC-8 rule (its test-plan section 4.2) applies the same equality but labels it "normalised"; the outcome is the same | SC-4, M22 |
| N10 | `BK15` nested rows | one per nested item | none (rows come only from the sync of top-level parameters, `CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:291-305`) | none | **tolerated deviation**, runtime-neutral by source (G4 c.2); 6 shipped nested items have no row either. Owner-visible: D20 does not decide it. Writing the rows in the handler's create path is the alternative | M14, M22 |
| N11 | `BK15.GT1` | snapshot, partly stale (section 2.5) | refreshed from the value | same | not compared; `GT2` / `GT4` / `GT5` pairing compared | - |
| N12 | DisplayValue of a nested source | `[#<el>.<coll>:<item>#]` | bare item caption | designer form | must match | M14 (the user opens the card) |
| N13 | DisplayValue rows of hidden constants (`ResultActionType`, `ConnectedObjectColumnUId`, `ReportId`, `IsSeparateReports`, `ConsiderTimeInFilter`) | value text | unknown | unknown | reported, not failing: the cards render these from the value. Must match for displayed values (`ConnectedObjectId`, `Files`, `Files.File`, `ReportName`) | SC-4 |
| N14 | resource cultures | authoring culture | request culture | same | compare the authoring culture only | - |
| N15 | "no value" forms `{}`, `{"GS5":<P>}`, `{"GS5":<T>}` | all three occur | `{}` | - | equal | - |
| N16 | `DataSourceFilters` | wrapper with two inner strings | `ProcessFilterService.BuildFilterValue` (both forms) | same | parse both inner strings; filter `key`s and the value `Id`s are normalised to positions; `rootSchemaName` must match | SC-4 |

TC-41 and DT-02 carry N2, N9, N10 and N13 as named, finite exceptions, and so does the ENG-95984 File process
parameter type AC-8 rule for N9 (its plan AC-8 and V4). The table sorts its rows into four classes, and the
section 12.8 classification table uses the same ones:
- ignored: N1, N5 and N6 (UIds).
- normalised: N7, N11, N14, N15 and N16. These format differences are removed before the comparison.
- tolerated: N2, N9, N10 and N13. Each is a named deviation of the builder and is recorded by its number.
- must match: every other row.

Any difference that is not in this table fails.

---

## 11. What is missing

| Id | Missing | Why it matters | Captured by |
|---|---|---|---|
| G-C1 | Any SAVED SysFile-mode element: Object source, any variant's SysFile target, the `RecordId` scope filter, a sort in SysFile mode | the SF serialization AC; D16's scope shape; the value-less `TargetDataEntitySchemaUId` prediction | SC-1 E3, E4, E5, E11 (+ M7 in memory, M21 capture half) |
| G-C2 | A Process-variant element with the current 8 parameters (`TargetDataEntitySchemaUId`) | AC-RP6 compares against 7-parameter captures | SC-1 E8, E9 |
| G-C3 | A Process-variant element fed by a single File (nested-only binding) | its stored shape and display value; M1 runs it | SC-1 E10 |
| G-C4 | A full-set Object "Save to object attachments" element | the OA save side compares against FileCopyProcessPP minus three parameters (TC-41) | SC-1 E2 |
| G-C5 | A full-set Report "Use in process" element | RP | SC-1 E7 |
| G-C6 | Any capture known to be saved by the stand's designer (10.1.37 core, CrtProcessDesigner 7.8.0) | every shipped capture may predate client changes; only `B8` (creation version) is recorded | SC-1 (all), SC-0 (a known-good read path) |
| G-C7 | File / FileCollection parameters saved by the current designer | PT compares against 7.17.3.396 and 0.0.0.0 captures; the ENG-95984 File process parameter type test plan's V0 checks them in memory only | SC-1 `PFile`, `PFiles`, `PFilesOut` (optional for PT) |
| G-C8 | The server-built shape (generic route, 1.6.6.54) | the D20 pins | M14 (defined in D29; SC-4 repeats it per cut) |
| - | Report to a SysFile target | same base-page code as E4 (`BaseFile…js:421-436`, basis=source) | not captured; M23c runs it if SF needs the runtime half |
| - | FastReport / DevExpress, "Create from element" on a file output, consumers (Send email, Creatio.ai) | refused in this work, not a parity target, or owned by ENG-95985 Send email attachments | not captured |

---

## 12. How to capture on the stand

### 12.1 Rules

- Stand `Creatio`. **Every write needs the user's explicit go-ahead.** A designer
  save, a builder create or modify, a no-op re-save and a delete are all writes.
- The user builds and saves in the classic designer; nobody types credentials for an agent. The agent may read the
  in-memory state with read-only JavaScript (the UO method) and does the read-back.
- One schema write at a time; never in parallel with another write or a builder run (a burst crashes the
  .NET Framework app pool).
- Capture processes are **never run**. That needs no fixture data and creates no file rows. A process with run history
  can be saved as a new version, which is a separate schema
  (`CLIO/docs/knowledge/ProcessModel/describe-business-process-reads-the-base-version-not-the-active-one.md`).
- Package `Custom`, names `UsrFpSc<n><Short>` (the `UsrFp` convention of
  [open-questions](eng-92719-file-processing-element-open-questions.md) B.0).
- Read back **stored** state only: the package serializer, the export service or the database. Never use
  describe (section 1) or the design session. While a process is open in a design session, its resource manager
  answers unsaved values (`CLIO/docs/knowledge/platform/a-design-session-serves-unsaved-resources-from-the-shared-manager.md`).
- **Open every element's card before the save.** The designer re-serializes only elements whose card was
  materialized (`PBD/add-data-element-capture.md`, measured on the package's Add data round trip).

### 12.2 Read-back paths (all read-only for the stand)

| Path | Command | Gives | Needs |
|---|---|---|---|
| A (primary) | `clio pull-pkg Custom -e Creatio -d <scratch>/sc -r` | `Schemas/<Process>/metadata.json` + `Resources/<Process>.Process/resource.<culture>.xml`, in the SAME format as the corpus (the platform's package serializer) | nothing; downloads the whole `Custom` package |
| B (one schema) | `clio export-schema <Process> --package-name Custom -e Creatio -d <scratch>/sc` | `<Process>/metadata.json` (the stored `MetaData`, prettified), `resources/resource.<culture>.json`, `schema-data.json` (`CLIO/clio/Command/SchemaTransfer/SchemaBundleStore.cs:297-333`) | cliogate 2.0.0.46+ (`CLIO/clio/help/en/export-schema.txt`). Resources come as JSON, not XML |
| C (database) | `clio execute-sql-script "SELECT s.UId, CAST(s.MetaData AS VARCHAR(MAX)) AS M FROM SysSchema s WHERE s.Name = '<Process>' AND s.ManagerName = 'ProcessSchemaManager'" -e Creatio -v json -d <scratch>/sc/meta.json`, then `"SELECT lv.[Key], c.Name AS Culture, lv.Value FROM SysLocalizableValue lv JOIN SysCulture c ON c.Id = lv.SysCultureId JOIN SysSchema s ON s.Id = lv.SysSchemaId WHERE s.Name = '<Process>'"` | the raw row and its resources | cliogate 2.0.0.41+; cliogate 2.0.0.53+ logs each statement in `ClioSqlRequestLog` (`CLIO/clio/help/en/execute-sql-script.txt`) |

`get-schema` cannot be used: it reads C# source-code schemas only (ManagerName `SourceCodeSchemaManager`,
`CLIO/clio/help/en/get-schema.txt`). Check the cliogate version first with `clio list-packages -e Creatio` (read-only).
The version on the stand has not been recorded.

### 12.3 SC-0: calibrate the pipeline on a shipped element (read-only)

1. `clio export-schema PrintInvoiceReport --package-name CrtInvoice -e Creatio -d <scratch>/sc0` (path B), or
   `clio pull-pkg CrtInvoice -e Creatio -d <scratch>/sc0 -r` (path A).
2. Run the section 12.8 diff with the stand copy on both sides first (it must report nothing), then the stand copy
   against `PS/CrtInvoice/branches/7.8.0/Schemas/PrintInvoiceReport/metadata.json` and its resources.
3. Expected: equal, apart from what the stand's install has rewritten. **Record any difference.** A difference found
   here is a read-path or install artefact, and it must not be attributed to the builder later.

### 12.4 SC-1: the designer capture process (one write, built by the user)

Process `UsrFpSc1Capture` (caption "FP SC1 designer capture") in `Custom`. Built in
`…/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/`. Prerequisites, all measured already (2026-10-01): the features
`EnableReportFileProcessingUserTask` and `ProcessFeatures.UseSysFileInObjectFileProcessing` read true in the
designer; the stand has 5 MS Word printables, Invoice among them (the shipped Invoice printable is
`e1f1a474-1a77-82f8-4bc7-b1da23699e13`); CrtInvoice is installed, so `Invoice (<InvoiceFile caption>)` is a legacy
entry.

Process parameters (Add parameter):

| Name | Menu | Settings | Captures |
|---|---|---|---|
| `PContact` / `PAccount` / `PAddr` / `PInvoice` | Lookup → Contact / Account / Account address / Invoice | default | record references; no records needed |
| `PFile` | Other → File | default (Variable) | G-C7 single File |
| `PFiles` | Other → Collection of records; on it, Add → Other → File named `File` (caption "File") | default (Variable) | G-C7 Variable FileCollection (the D2 item name; the D3 default) |
| `PFilesOut` | as `PFiles`, Direction Out | | G-C7 Out FileCollection (an explicit Out) |

Elements. Drag "Process file" from the palette each time; for a Report or Process element, change "What is the source
of the file?" on the still-unconfigured element. Chain them Start → E1 → … → E11 → End in this order, so that every
mapping source comes before its consumer.

| E | Caption | "What is the source of the file?" and settings | Captures |
|---|---|---|---|
| E1 | `SC1 legacy use` | Object attachments. Which object: `Contact (Contact attachment)`. Filter: Contact = process parameter `PContact`. Read first `10`. Sort: `Name` ascending. What to do: Use in process | legacy source; InFilter scope; changed `RecordsToRead`; `OrderByInfo` |
| E2 | `SC1 legacy save` | Object attachments; `Contact (Contact attachment)`; filter Contact = `PContact`; read first `50` (untouched); no sort; Save to object attachments → `Account (File and link of account)`; record Account = `PAccount` | G-C4; link column `AccountFile.Account`; UO-4 prediction |
| E3 | `SC1 sysfile scope save` | Object attachments; `Account address`; filter RecordId = `PAddr`; sort `Created on` descending; Save to object attachments → `Contact (Contact attachment)`; record Contact = `PContact` | G-C1: SysFile source, `RecordId` scope (M7's stored form), SysFile-mode sort; **= M21** (SysFile Object with scope, target and sort) |
| E4 | `SC1 legacy to sysfile` | Object attachments; `Contact (Contact attachment)`; filter Contact = `PContact`; Save to object attachments → `Account address`; record = `PAddr` | G-C1: SysFile target (`TargetEntitySchemaUId` SysFile, `TargetDataEntitySchemaUId` AccountAddress, `SysFile.RecordId`) |
| E5 | `SC1 sysfile default` | Object attachments; `Account address`; nothing else | UO-3 as saved (section 9) |
| E6 | `SC1 report save` | Generated report; report `Invoice`; filter Id = `PInvoice`; File name constant `SC1`; Save to object attachments → `Invoice (<InvoiceFile caption>)`; record Invoice = `PInvoice` | product shape (refreshes PrintInvoiceReport); localizable constant `ReportName`; forced `IsSeparateReports` |
| E7 | `SC1 report use` | Generated report; report `Invoice`; filter Id = `PInvoice`; File name from the column `Selection result.Number`; Use in process | G-C5; `ReportNameDataSourceColumnUId`; cleared `ReportName` |
| E8 | `SC1 files from element` | Process parameter; Files = `SC1 legacy use` → Collection of files → File; save to `Contact (Contact attachment)`, record `PContact` | G-C2: both levels from an element, 8 parameters |
| E9 | `SC1 files from param` | Process parameter; Files = `PFiles` → File; save to `Contact (Contact attachment)`, record `PContact` | G-C2: both levels from a process collection |
| E10 | `SC1 single file` | Process parameter; Files = `PFile`; save to `Contact (Contact attachment)`, record `PContact` | G-C3 nested-only binding (the stored half of M1) |
| E11 | `SC1 files to sysfile` | Process parameter; Files = `PFiles` → File; save to `Account address`, record `PAddr` | G-C1 Process-variant SysFile target |

Before saving, take an **in-memory snapshot** (read-only, agent), so that SC-2 can show what the save itself
changed (for example the `GS5` stamps and the value-less ConstValue):

```js
// read-only; confirm items[0] is UsrFpSc1Capture by its caption first
const inst = Terrasoft.ProcessSchemaManager.items[0].instance;
const fileTasks = ["9387c794-8d84-5925-ab77-c47e7d876286", "6c620dd2-026e-560c-489f-030c5be5f2c3",
                   "c2bf0416-54c6-6c56-58e0-41162c7795f0"];
const dump = p => ({ n: p.name, s: p.sourceValue && p.sourceValue.source, v: p.sourceValue && p.sourceValue.value,
  m: p.sourceValue && p.sourceValue.modifiedInSchemaUId, items: p.itemProperties ? p.itemProperties.getItems().map(dump) : [] });
JSON.stringify(inst.flowElements.getItems().filter(e => fileTasks.includes(String(e.managerItemUId)))
  .map(e => ({ caption: String(e.caption), name: e.name, params: e.parameters.getItems().map(dump) })));
// adjust property names if the API differs (the UO readings used .caption, .name, .parameters, .sourceValue)
```

Then the user saves once. That is the only write of SC-1.

### 12.5 SC-2: read back SC-1

1. Path A or B (section 12.2) into `<scratch>/sc1`. Keep the in-memory snapshot next to it.
2. Record `SysSchema.UId` of `UsrFpSc1Capture`, `list-packages` versions (CrtProcessBuilder, CrtProcessDesigner),
   and the date.
3. Check the predictions of sections 4, 5, 6.2, 7 and 9 one by one: E5 against UO-3; E2 against UO-4 (the
   `*DataEntitySchemaUId` "no value" form); E3's filter leaf (M7's answer: lookup InFilter or Guid CompareFilter);
   E10's `Files` `{}` with a set `Files.File`, and its `DisplayValue` (predicted `[#PFile#]`); E6's `ReportName` in
   the resources only; E8/E9 `BK15` rows for the nested `Files.File`; `PFile`/`PFiles` without `IL2`.
4. Record the result in [open-questions](eng-92719-file-processing-element-open-questions.md) (M7: stored form;
   M21: capture half) and correct sections 9 and 11 of this document.

### 12.6 SC-3 (optional): a designer no-op re-save

The user reopens every card of `UsrFpSc1Capture`, changes nothing, closes each one, and saves (write, go-ahead).
Read back as in SC-2 and diff SC-2 against SC-3. Every difference is a designer normalisation that the comparison
must tolerate, or a designer defect. Watch especially E2's `SourceDataEntitySchemaUId`: H-G3-1 predicts that it
becomes `AccountFile` on a re-save ([platform-reference](eng-92719-file-processing-element-platform-reference.md)
§7.6). That is M11 (c) without a run. If SC-3 runs, DT-01's "no-op save changes nothing" has a designer baseline.

### 12.7 SC-4: the builder twin, per cut

After each package cut is installed on the stand (PK-PT, PK-OA, PK-RP, PK-SF;
[pr-split](eng-92719-file-processing-element-pr-split.md)), the agent builds `UsrFpSc4<Cut>` with
`create-business-process`, one request (a write, go-ahead). It has the same captions, the same process parameters and
the D11 descriptor equivalent of the SC-1 elements that the cut supports:

| Cut | Elements | Expected |
|---|---|---|
| PK-PT | `PFile`, `PFiles` (FileCollection, `direction: Variable`), `PFilesOut` (FileCollection, default) | equal under the AC-8 comparison rule (ENG-95984 File process parameter type test-plan section 4.2: N5, PT-a, N9, N15, N14, PT-b) |
| PK-OA | E1, E2 | equal; E3-E5 are refused with the D15 text until SF |
| PK-RP | E6, E7, E8, E9; E10 only if M1 passed | equal; SysFile targets refused until SF |
| PK-SF | E3, E4, E5, E11 | equal; this closes the SF serialization AC |

Before each build, check that the session's MCP clio is current (`get-tool-contract` shows the new arguments; an older
client drops unknown arguments silently). Record the exact request next to the capture. M14 is the same procedure on
1.6.6.54 with the generic route, and it is the baseline for N3, N6, N8 and N12.

### 12.8 Diff procedure

1. **Load** both sides: `metadata.json` (`utf-8-sig`), the resources of the authoring culture, and, for SC-1, the
   in-memory snapshot.
2. **Pair** elements by caption (`BaseElements.<A2>.Caption`), parameters by `A2`, nested items by `A2` under their
   parent, process parameters by `A2`.
3. **Replace UIds by roles** everywhere, including inside `GS2` tokens and filter JSON: process → `<P>`, user-task
   schema → `<T:Object|Process|Report>`, element → `<E:caption>`, element parameter → `<EP:caption.name>`, nested
   item → `<EP:caption.outer.inner>`, process parameter → `<PP:name>`, template parameter (in `GT5`) → `<TP:name>`,
   lane → `<lane>`. Known entity, column and report UIds stay literal.
4. **Decode** `DataSourceFilters`: parse the wrapper and both inner strings; replace `items` keys, `key` and value
   `Id` fields by their ordinal; compare the trees.
5. **Normalise** per section 10: N7 (`GS5` on template-default values), N11 (`BK15.GT1` dropped; step 6 compares
   the rest of the row), N14 (authoring culture only, as loaded in step 1), N15 ("no value" forms), N16 (filter
   keys and value `Id`s replaced by their positions, step 4), N1/N5/N6 (UIds, steps 2-3).
6. **Compare** the element keys of section 2.2, the parameter keys of 2.3, `L8` per 2.4; `BK15` as a set of
   (target role, `GT4`, `GT5` role) per element; resource `Caption`, `Value` and `DisplayValue` rows per parameter.
7. **Classify** each difference as equal, normalised, tolerated (N2, N9, N10, N13; each named) or DIFF. A
   process-parameter `IL2` that is absent on the designer side and `<P>` on the builder side is not removed in
   step 5. It reaches this step and is recorded as tolerated N9. One DIFF fails the comparison.
8. **Record** the table in the package capture document: `docs/process-file-element-capture.md` (element; OA, RP,
   SF sections) and `docs/file-parameter-capture.md` (parameters), the package convention, under
   https://creatio.ghe.com/engineering/crt-process-builder/tree/main/docs.

Sketch of the normaliser (Python, for the package capture document; not run yet):

```python
import json, re
GUID = re.compile(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")
TEMPLATE_DEFAULTS = {"RecordsToRead": "50", "ConsiderTimeInFilter": "true"}

def roles(schema, captions):
    r = {schema["UId"]: "<P>"}
    for p in schema.get("FJ1", []):
        r[p["UId"]] = f"<PP:{p['A2']}>"
        for i in p.get("L18", []): r[i["UId"]] = f"<PP:{p['A2']}.{i['A2']}>"
    for e in schema["BK4"]:
        cap = captions.get(f"BaseElements.{e['A2']}.Caption", e["A2"])
        r[e["UId"]] = f"<E:{cap}>"
        for p in e.get("BP2", []):
            r[p["UId"]] = f"<EP:{cap}.{p['A2']}>"
            for i in p.get("L18", []): r[i["UId"]] = f"<EP:{cap}.{p['A2']}.{i['A2']}>"
    return r

def sub(text, r): return GUID.sub(lambda m: r.get(m.group(0).lower(), m.group(0).lower()), text)

def norm_l8(name, l8, r):
    v = {k: l8[k] for k in ("GS1", "GS2", "GS5", "GS8") if k in l8}
    if v.get("GS1", 0) == 0: return {"value": None}                          # N15
    if name == "DataSourceFilters" and "GS2" in v: v["GS2"] = norm_filter(v["GS2"], r)   # N16
    elif "GS2" in v: v["GS2"] = sub(v["GS2"], r)
    if TEMPLATE_DEFAULTS.get(name) == v.get("GS2"): v.pop("GS5", None)       # N7
    else: v["GS5"] = sub(v.get("GS5", ""), r)
    return v

def norm_filter(gs2, r):
    outer = json.loads(gs2)
    def tree(s):
        t = json.loads(s)
        def walk(n):
            if isinstance(n, dict):
                if isinstance(n.get("items"), dict): n["items"] = [walk(x) for x in n["items"].values()]
                n.pop("key", None); n.get("value", {}).pop("Id", None) if isinstance(n.get("value"), dict) else None
                return {k: walk(x) for k, x in n.items()}
            if isinstance(n, list): return [walk(x) for x in n]
            return sub(n, r) if isinstance(n, str) else n
        return walk(t)
    return {"edit": tree(outer["serializedFilterEditData"]), "runtime": tree(outer["dataSourceFilters"])}
# element and parameter comparison, BK15 sets and resource rows follow section 12.8 steps 6-7
```

### 12.9 Cleanup and recording

- Keep `UsrFpSc1Capture` on the stand until the PK-SF comparison is recorded. It is the oracle for four cuts and is
  never run, so it owns no data. Then the user approves the deletion: `delete-schema` with a CLI `--timeout` (a
  remote delete takes about 6 minutes on this stand). The `UsrFpSc4<Cut>` twins are deleted the same way after
  their cut's comparison. No row cleanup is needed, because nothing ran.
- Every comparison result goes into the package PR description of its cut, with the package version, the capture
  process UIds, the dates and the classification table. See the review protocol in
  [pr-split](eng-92719-file-processing-element-pr-split.md) section 14.

---

## 13. Corrections to earlier research, found while writing this

S3, S4 and S6 are applied in the documents they name (2026-10-01).

| # | Earlier statement | Correction | Basis |
|---|---|---|---|
| S1 | corpus research: schema `B8` = "saved-in" version | `B8` is `CreatedInVersion`, stamped at creation (`CORE/Terrasoft.Core/Schema.cs:82, :143`) | source |
| S2 | task text: read back "via get-schema" | `get-schema` reads C# source-code schemas only; use `pull-pkg`, `export-schema` or SQL (section 12.2) | source (`CLIO/clio/help/en/get-schema.txt`) |
| S3 | ENG-95984 File process parameter type plan V4 and the test-plan TC-41 rule: "equal apart from UIds" | builder-made process parameters store `IL2 = <P>`, which the designer never writes (N9); builder-made elements have no nested `BK15` rows (N10). Both need named exceptions or a fix | source + corpus |
| S4 | D20: "normalise `ModifiedInSchemaUId` for values equal to the template default (the designer itself varies there)" | the designer does not vary (14 of 14 `RecordsToRead`: `50` ↔ `<T>`, changed ↔ `<P>`). The normalisation is needed because D20 stamps an explicit `numberOfRecords: 50` with `<P>` | corpus + source |
| S5 | UO-3 records `TargetDataEntitySchemaUId` as "unset" | a designer save is predicted to store a value-less ConstValue `{"GS1":1,"GS5":<P>}` there (section 9); SC-1 E5 decides | source + corpus |
| S6 | test plan §7: read DT metadata "with a read-only describe plus the raw `SysSchema` row" | describe is not a capture read path (section 1); the raw row, `pull-pkg` or `export-schema` only, plus the stored resources | source |
| S7 | corpus research: "the designer writes source 2/3" (implicitly, mappings) | in the file elements every mapping is Script (3); Mapping (2) occurs 0 times in 400 entries | corpus |

## 14. Knowledge records this capture suggests

Only for what the code does not say ([plan](eng-92719-file-processing-element-plan.md) §7 holds the others):

| Record (proposed, clio) | Fact | PR |
|---|---|---|
| `docs/knowledge/ProcessModel/corpus-scan-b8-is-the-creation-version.md` | `B8` dates the schema's creation, not its last save; date a capture by its parameter-set drift | CL-PT (its V4 comparison is the first to date a capture by `B8`) |
| `docs/knowledge/platform/designer-stamps-modified-in-schema-only-on-a-changed-value.md` | an untouched template default keeps the template's `GS5`, so "set by the designer" means `GS5 = process`; a cleared value keeps the process stamp without a source | CL-OA |
| `docs/knowledge/platform/builder-process-parameters-carry-il2.md` | only after SC-4 (PK-PT) measures it: builder-made process parameters store `IL2 = process`, the designer none; same meta path | CL-PT, after V4 (SC-4's PK-PT row) |

The [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md)
§7 and the [plan](eng-92719-file-processing-element-plan.md) §7 list these records with their PRs.
