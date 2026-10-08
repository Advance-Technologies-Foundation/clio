# ENG-92719 File processing element — implementation plan

[ENG-92719](https://creatio.atlassian.net/browse/ENG-92719) File processing element · Story · component *bpms tools* ·
epic [ENG-92704](https://creatio.atlassian.net/browse/ENG-92704) Create BP via AI Toolkit · sub-tasks
[ENG-96505](https://creatio.atlassian.net/browse/ENG-96505) Element readiness and object attachments mode and
[ENG-96506](https://creatio.atlassian.net/browse/ENG-96506) Generated report + process parameter modes · all three
**To Do**, no story points and no time estimate in Jira (measured 2026-10-01).

Read [platform-reference](eng-92719-file-processing-element-platform-reference.md) and
[traps](eng-92719-file-processing-element-traps.md) first. Every design choice below is argued in
[decisions](eng-92719-file-processing-element-decisions.md) (D1-D29, measurements M1-M26); the PR mechanics are in
[pr-split](eng-92719-file-processing-element-pr-split.md); the test cases are in
[test-plan](eng-92719-file-processing-element-test-plan.md); what the owner still has to decide is in
[open-questions](eng-92719-file-processing-element-open-questions.md). The parameter-type half of the feature has its
own [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md).
Other siblings: [README](README.md) · [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) ·
[use-cases](eng-92719-file-processing-element-use-cases.md) · [reuse](eng-92719-file-processing-element-reuse.md).

Written 2026-10-01, read-only: nothing was built, committed, or written to Jira or to the stand. **Basis labels:**
**source** = read in code or metadata (for runtime behaviour this is a hypothesis); **measured** = observed on the
stand, counted over the shipped corpus, or read from git/Jira, with the date; **inference** = derived from both.

| Alias | Meaning |
|---|---|
| **FE** | ENG-92719 File processing element (this Story) |
| **OA** | ENG-96505 Element readiness and object attachments mode |
| **RP** | ENG-96506 Generated report + process parameter modes |
| **SF** | NEW Sub-task of FE, "SysFile attachment storage in the Process file element" (key assigned on creation, owner decision O1) |
| **PT** | ENG-95984 File process parameter type (a separate Task; a dependency only where section 4.2 says so) |
| `pkg:` | crt-process-builder repository root (https://creatio.ghe.com/engineering/crt-process-builder, `main` d9571626, CrtProcessBuilder 1.6.6.85, re-pinned 2026-10-08; the stand measurements of 2026-10-02 ran on 1.6.6.54, `3f4cce50`); `PB` = `pkg:packages/CrtProcessBuilder/Files/src/cs`, `PBT` = `pkg:tests/UnitTests/CrtProcessBuilder.Tests` |
| `clio:` | clio repository root (https://github.com/Advance-Technologies-Foundation/clio, `master` db6e2bf9e, re-pinned 2026-10-08) |
| `kb:` | clio-knowledge repository root (https://github.com/Advance-Technologies-Foundation/clio-knowledge, `master` 73a619e, libraryVersion 1.15.100, re-pinned 2026-10-08) |
| `PD` | `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas` (the designer package the stand serves) |
| `CORE` | `C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib` (10.1.37, the stand's core) |

---

## 0. Recommendation in one paragraph

Build "Process file" as **one dedicated element type, `fileProcessing`** (alias `processFile`), whose source variant
is simply the group the caller sends — `attachments`, `report` or `files` — because the designer presents one
element while the platform stores three user-task schemas, and a source change is a schema change the designer
itself performs by replacing the element (D10, D12). Back it with a **variant registry** keyed by schema UId, so the
handler claim, the generic-route refusal, the raw-mapping refusal, the filter-target claim and the describe block of
a variant always land together and each later cut *adds a registration* instead of editing five shared files
([pr-split](eng-92719-file-processing-element-pr-split.md) §5.3, owner decision O5). Ship it as **three package → clio → knowledge triples, one per Jira issue, in this order**: ENG-96505 Element
readiness and object attachments mode (Object attachments with dedicated `<X>File` storage only), ENG-96506 Generated
report + process parameter modes (MS Word reports and the process-parameter source), and a **new Sub-task for SysFile
storage**, which has no runtime evidence at all — 0 of 30 shipped elements use it — and five pre-code measurements,
three of which need a human to build designer probes (O1). The code is the smaller half. The platform accepts
silently almost everything that can go wrong with this element — an empty filter reads 50 arbitrary files, a filter
rooted on the record object reads zero, an unset action saves, a copy with no link column lands as a duplicate on the
source record, an outer-only `Files` binding throws per row — so most of the work is **refusals, a storage resolver,
a record scope that compiles into the filter, and a describe block that round-trips** (D13-D16, D19). ENG-95984 File
process parameter type is a real prerequisite only for ENG-96506 Generated report + process parameter modes, but its
clio PR must merge before the package PR of ENG-96505 Element readiness and object attachments mode, because it carries
the `ManagerMap` arm (pr-split E4, E6). Its knowledge PR must be published before the
clio PR of ENG-96505 Element readiness and object attachments mode, because that PR writes the first
`name=process-files` (E5; D10, D23, D25). Estimate, with an AI agent writing the code: **about 26-52 h of
agent time and 15-33.5 h of the owner's time, about 7-10 working days after ENG-95984 File process parameter type
merges**, dominated by serialized human review (one of our package PRs in review at a time), stand verification and
the user-built probes (section 8).

---

## 1. The task

### 1.1 Goal (as written in ENG-92719 File processing element)

Support the **Process file** element so that coding agents can generate processes that read a record's attachments,
take files from a process parameter, or generate a printable report, and then either hand the file collection to a
following element or save it to a record's Attachments detail. "Process file" is an entry for three user-task
schemas, selected by "What is the source of the file?":

| Source option | User-task schema | UId |
|---|---|---|
| Object attachments | `ObjectFileProcessingUserTask` | `9387c794-8d84-5925-ab77-c47e7d876286` |
| Process parameter | `ProcessFileProcessingUserTask` | `6c620dd2-026e-560c-489f-030c5be5f2c3` |
| Generated report | `ReportFileProcessingUserTask` | `c2bf0416-54c6-6c56-58e0-41162c7795f0` |

Why customers use it, what Academy documents and how the shipped processes use it:
[use-cases](eng-92719-file-processing-element-use-cases.md). How the element runs:
[platform-reference](eng-92719-file-processing-element-platform-reference.md).

### 1.2 Acceptance criteria, as written (numbered here for reference)

| Id | Issue (alias, see the table above) | AC (condensed from Jira, 2026-10-01) |
|---|---|---|
| AC-FE1 | FE | All three source variants and the target record/attachment can be built via create/modify; read back by `describe-process` |
| AC-FE2 | FE | An element's output collection can be mapped by more than one downstream element |
| AC-FE3 | FE | Guidance covers which variant to pick and the pairings from the use-case inventory; creating a report is out of scope, it must already exist, and the user is told to create it otherwise |
| AC-FE4 | FE | Values set via "Task 6", including the "Binary / File parameter type (Task 5)" |
| AC-FE5 | FE | Server serialization matches a designer-built capture per source variant |
| AC-FE6 | FE | Tests cover the five patterns in the use-case inventory; docs and MCP surface updated |
| AC-OA1 | OA | The element can be added to a layout through create/modify and connected like any other element |
| AC-OA2 | OA | `describe-business-process` returns the element with its variant and configuration, round-trippably; an existing element, "including a source variant not configurable in this sub-task", reads back without loss or corruption on a later modify |
| AC-OA3 | OA | The output file collection can be mapped by more than one downstream element |
| AC-OA4 | OA | Identity resolves to the right schema per variant, so a later sub-task does not change the layout contract |
| AC-OA5 | OA | The source object and the record whose attachments are read are set through the established value sources (ENG-91844 Implement full parameter mapping (sources): process parameter, element parameter, entity column, Current user contact) |
| AC-OA6 | OA | Files can be saved to a target record's Attachments detail, the record set through the same value sources |
| AC-OA7 | OA | Server serialization matches a designer-built `ObjectFileProcessingUserTask` capture |
| AC-OA8 | OA | Guidance states that Process file "is rarely an endpoint" and covers the Object attachments pairings |
| AC-OA9 | OA | Unit tests, stand verification against a designer capture, docs, MCP descriptions and guidance in the same change |
| AC-RP1 | RP | The report is selected from the reports that already exist "on the target object"; the record is set through the value sources |
| AC-RP2 | RP | The generated file can be saved to Attachments or handed to a following element as a file collection |
| AC-RP3 | RP | Creating a printable is out of scope; the agent tells the person to create it and never silently picks an unrelated one |
| AC-RP4 | RP | The Process-parameter source is "a process parameter of the File type", set through the value sources |
| AC-RP5 | RP | The file can be saved to Attachments "or handed to a following element as a file collection" |
| AC-RP6 | RP | Server serialization matches a designer-built `ProcessFileProcessingUserTask` capture |
| AC-RP7 | RP | `describe-business-process` round-trips both variants |
| AC-RP8 | RP | Guidance covers variant choice and the pairings Send email, Read data, Add data, Modify data and a Creatio.ai call |
| AC-RP9 | RP | Docs, MCP descriptions and guidance updated in the same change |

### 1.3 What the tickets say that is not true

The proposed replacement texts are D24 in [decisions](eng-92719-file-processing-element-decisions.md) unless a row
says "new".

| # | Statement | What is true | Basis | Correction |
|---|---|---|---|---|
| N1 | AC-FE4: "Binary / File parameter type (Task 5)", "Values set via Task 6" | Binary `B7342B7A` is a `Stream` type that the flow-engine store cannot hold (`CORE/Terrasoft.Core/Process/FlowEngineStateService.cs:139-148, 409-429`). The platform name `"File"` resolves to a DIFFERENT BLOB type, `BA40CFC5` "File (BLOB)" (`CORE/Terrasoft.Core/DataValueTypeManager.cs:341-349`). The file reference is FileLocator `A33C9252` (one file); several files are a Collection with one FileLocator item. The corpus has 0 Binary/BLOB parameters. "Task 6" and "Task 5" are ENG-91844 Implement full parameter mapping (sources) and ENG-95984 File process parameter type | source; corpus measured | D24: "values set through the ENG-91844 Implement full parameter mapping (sources) value sources, including File / FileCollection parameters (ENG-95984 File process parameter type)" |
| N2 | AC-FE5, AC-OA7, AC-RP6: "matches a designer-built capture per source variant" | Storage is a second axis: an Object element is written either in the legacy `<X>File` encoding or in the SysFile encoding, chosen per entity (measured UO-3/UO-4, 2026-10-01). 0 of 30 shipped elements use SysFile, so no shipped capture of it exists. The designer keeps the template's `ModifiedInSchemaUId` (GS5) on unchanged defaults (14 of 14 Object elements) and stamps the process UId only on a changed value; a server-created parameter takes `DefValueForExistingProcess` (`CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:306-320`) | measured (corpus, UO) + source | D24: per variant AND per storage, against SkillFilesValidationProcess, PrintInvoiceReport, FileParameterProcess, under the serialization-capture section 10 rule (UIds ignored; GS5 normalised for template defaults, because D20 stamps an explicit `numberOfRecords: 50` with the process UId; N2, N9, N10 and N13 tolerated); the SysFile capture (M21) moves to SF |
| N3 | AC-FE6 "five patterns"; the description's "all of which combine it with a second element: Send email, Read data, Add data, Modify data, or a Creatio.ai call" | Read data and Add data never consume a file output (0 corpus cases): they are upstream (find the record; create the report's data row). Send email and Creatio.ai read files through dynamic `<Prefix><N>` slots the builder cannot create. The Send email pairing is ENG-95985 Send email attachments (To Do); ENG-92725 Execute AI Intent element (BP generation) is Closed, Won't Do | corpus measured; source; Jira measured | D24 row for "five patterns"; section 10, C-3: Modify data re-find and Add data → report get guidance AND an e2e each (TC-71, TC-74) |
| N4 | AC-OA8: "Process file is rarely an endpoint" | In shipped product content it **is** the endpoint: 4 product processes, 5 elements, all Generated report + Save to object attachments (MS Word); none maps any output of the element — each output UId occurs only in its own definition and its own mapping row. A following Modify data re-finds the saved file by filter, and a Pre-configured page shows the file list. The "plumbing" shapes (Object or Process-parameter variants feeding another element) occur only in test packages | corpus measured (16 processes, 30 elements) | **new**: "most shipped uses end in a record's attachments; when a following element needs the files, map the element's output collection" (section 10, C-2) |
| N5 | AC-OA5: the source object and the record are "set through the established value sources" | The source object is a schema choice: the designer writes `SourceEntitySchemaUId` (and in SysFile mode `SourceDataEntitySchemaUId`) as ConstValue schema UIds (measured UO-3/UO-4). The record is a condition inside `DataSourceFilters` (the link column, or `SysFile.RecordId`), not a parameter | measured + source | D24: object by name, storage derived; the record in `attachments.recordId` through the value sources, `Current user contact` as `[#SysVariable.CurrentUserContact#]` |
| N6 | AC-OA6: "Files can be saved to a target record's Attachments detail" | Where files land depends on the target's storage. For an entity without a dedicated `<X>File`, the designer writes SysFile + `RecordId`, a write path that is unmeasured and unshipped | source + corpus | O1: ENG-96505 Element readiness and object attachments mode ships dedicated storage; SysFile targets and sources are refused with a message; SF lifts it |
| N7 | AC-OA2: "including a source variant not configurable in this sub-task" | Per-cut scoping (D13): ENG-96505 Element readiness and object attachments mode claims only the Object schema; Report and Process-parameter elements are described and preserved as generic user tasks (lossless raw `parameters[]`, no block) until ENG-96506 Generated report + process parameter modes | source | D24 row |
| N8 | AC-OA4: a later sub-task "does not change the layout contract" | The layout contract does not change (`ManagerItemUId` = schema UId, 69x55, lane). Describe output does: with ENG-96506 Generated report + process parameter modes the Report and Process elements — including the four shipped product processes PrintInvoiceReport, PrintQuotationReport, PrintContractsReport, GenerateDNSRecordsSpecification — move from `buildType: usertask` to `fileprocessing` | source | listed as a visible change in the PK-RP description |
| N9 | Jira: ENG-95984 File process parameter type **blocks** ENG-96505 Element readiness and object attachments mode (link 560203); ENG-96506 Generated report + process parameter modes has no link | The Object variant needs no File process parameter: its outputs are element parameters, mappable today by dotted path (`PB/ProcessSchemaElementLocator.cs:166-241`). The Process-parameter variant of ENG-96506 Generated report + process parameter modes is what needs File/FileCollection, dotted process paths and the two-level binder | source; Jira measured | D23: ENG-95984 File process parameter type blocks ENG-96506 Generated report + process parameter modes and relates to ENG-96505 Element readiness and object attachments mode; the delivery order stays PT → OA → RP |
| N10 | AC-RP1: reports "that already exist on the target object" | The printable's object is the record being reported on (`SysModuleReport.SysEntitySchema`), not the save target, which is a different object. The designer lists every readable printable of every type; only MS Word is accepted here (FastReport refused in this ticket, DevExpress throws `NotSupportedException` at run time). The stand has 5 MS Word printables, none for Account or Contact | source; stand measured | D24 row |
| N11 | AC-RP2: report files "handed to a following element" | True, but "Use in process" report files are temporary `SysProcessFile` rows deleted when the owning process completes, and `CreatedObjectFileIds` stays empty (`PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:98-112`) | source, pending M16 | notice + guidance (D14) |
| N12 | AC-RP4: "a process parameter of the File type" | `Files` takes a FileCollection, a File, or an element's file output. A single File binds only the nested `Files.File`, which is pending M1 | source | D24 row |
| N13 | AC-RP5: Process-parameter files "handed to a following element as a file collection" | "Use in process" does not exist for this source: the runtime throws `NotSupportedException` for anything but SaveToFiles (`PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:89-97`, re-read 2026-10-01), and the designer hides the control and forces 0. The copies are available afterwards as `ObjectFiles.ObjectFile` and `CreatedObjectFileIds` | source | D24 row |
| N14 | AC-RP8: guidance covers "a Creatio.ai call" | Not buildable through this tool (dynamic `Files<N>` slots; ENG-92725 Execute AI Intent element (BP generation) is Won't Do) | source + Jira | guidance says "not through this tool yet", never "Creatio cannot" |
| N15 | AC-FE2 / AC-OA3: an output mapped by more than one downstream element | Already true at the mapping level: nothing limits readers (corpus: one case, FileParameterProcess, a test package). The trap is semantic: after "Save to object attachments" the Object variant's `ObjectFiles` holds the SOURCE files; the copies' Ids are only in `CreatedObjectFileIds` (`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:71-87`) | source + corpus | an e2e with two consumers, plus the guide sentence |
| N16 | AC-FE1: "read back by `describe-process`" | The MCP tool is `describe-business-process` | source | wording |

---

## 2. What exists, what is missing

| Capability | Today | Basis | Delivered by |
|---|---|---|---|
| Drawing the element (`ManagerItemUId` = schema UId, 69x55, lane) | The generic route `{type: userTask, userTaskName: "ObjectFileProcessingUserTask"}` already writes it (`PB/Elements/UserTaskElementHandler.cs:74-90, 329-339`; size and logging in `PB/Elements/ProcessElementFactory.cs:114-125`). creatio-ui has no file-processing logic; both designers use the classic CrtProcessDesigner property pages | source; corpus 30/30 | reused by the dedicated handler (OA) |
| Configuration of the 13 / 8 / 13 parameters | **Nothing.** A generic element saves green with no `ResultActionType` (runs as SaveToFiles), no source and no target, and throws at run time on `GetInstanceByUId(Guid.Empty)` | source | OA (Object), RP (Report, Process) |
| Describe | Generic `usertask` with a raw, provenance-filtered `parameters[]`: inherited defaults, an empty link column and a nested-only `Files` binding are invisible; `ReportId` and `TargetEntitySchemaUId` are bare GUIDs. Lossless at the parameter level, not round-trippable | measured on PrintQuotationReport (stand, 2026-10-01) | OA, RP: typed `fileProcessing` block (D19) |
| Filters on these elements | `DataNodeFilterTarget` claims any node with `DataSourceFilters` (`PB/Filters/DataNodeFilterTarget.cs:57-58`) and roots the filter on whatever object the caller names: `{object: "Account", Id = X}` runs as `AccountFile.Id = X` — zero files, success | source | OA: `FileProcessingFilterTarget` (D16); RP adds Report |
| Empty-filter guard | None. Object reads up to `RecordsToRead` arbitrary files (and with save, copies them onto the target); a Word report generates one document per record of the whole entity | source | OA (Object), RP (Report): end-of-request ledger (D16) |
| Sort | `ReadDataConfigBinder.ResolveOrderInfoValue` + `ParseSortDirection` and `DescribeSort` + `DescribeSortDirection` exist (`PB/Elements/ReadDataConfigBinder.cs:793-825, 917-962`), but all four are `private static` and their refusals say `Read data '<label>': ...` (`:799-807`, `:822-824`), so the new binder cannot call them | source, re-read 2026-10-01 | **reuse + change** (OA.4, reuse RF2): extracted into a shared internal sort codec with the element label injected; the Read data messages stay byte-identical |
| Value sources | `IProcessMappingService.ApplyMapping` (`PB/Mappings/ProcessMappingService.cs:48-72`) with the ENG-91844 Implement full parameter mapping (sources) sources, incl. `sourceColumn` | source | reused |
| Element nested-item paths (`OF1.ObjectFiles.File`) | Work for element parameters (`PB/ProcessSchemaElementLocator.cs:166-241`) | source | reused |
| Process-level File / FileCollection, dotted process paths, the two-level binder (`BindCollection`, P1/P2) | Missing | source | PT (dependency) |
| FileLocator → FileLocator type check | Exact-UId match already accepts it (`PB/Mappings/ParameterTypeCompatibility.cs:255-257`) | source | reused |
| Attachment storage choice per entity | Missing server-side; it is designer logic (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:502-560`) | source + measured UO-3/UO-4 | OA (dedicated), SF (SysFile) |
| Record-existence check for a constant | `ProcessParameterValueValidator.EnsureReferencedRecordExists` exists (`PB/Parameters/ProcessParameterValueValidator.cs:311`, called from the lookup branch `:168-204`), but `ConnectedObjectId` is Guid-typed, so today's validator takes the format-only branch (`:235-236`) | source | OA: new internal entry point (D15) |
| Report discovery and resolution | clio's `list-printables` returns `templateId` / `printableCaption` / `convertInPDF` (`clio:clio/Command/McpServer/Tools/ListPrintablesTool.cs`); the package has no report reader | source | RP: `IReportTemplateReader` (D17) |
| Feature reads | `Creatio.FeatureToggling` is already referenced (`pkg:packages/CrtProcessBuilder/Files/CrtProcessBuilder.csproj:137-141`) | source | RP (report notice), SF (SysFile notice) behind one seam |
| End-of-request checks, dependents, refusal style | `IDeleteDataNoticeLedger` (registered `pkg:.../CrtProcessBuilderApp.cs:341`), `ProcessElementDependencyScanner` (`PB/Graph/ProcessElementDependencyScanner.cs:69-132`), `EmailTemplateResolver` (`PB/Email/EmailTemplateResolver.cs:40-206`) | source | reused as patterns |
| clio pass-through | `descriptor` / `operations` are JSON values since ENG-100153 O5 (a string holding the same JSON is still accepted), passed through as JSON text with no client allow-list; `DescribedElement` carries an undeclared block in its `[JsonExtensionData]` bag (`clio:clio/Command/ProcessModel/IProcessDescriber.cs:729-736`), but a bag keeps a field alive, not addressable | source | OA: typed `DescribedFileProcessing` with a bag on every nested type |
| `validate-process-graph` | `ManagerMap.ResolveDataId` has no `fileprocessing` / `processfile` arm (`clio:clio/Command/ProcessModel/Schema.cs:1144-1145`): the descriptor token falls to `Unknown`, a hard Error on a graph that builds | source | PT's clio PR (one release ahead, D10) |
| `list-user-tasks` advice | Lists the three schemas with "pass this as userTaskName" (`PB/Contracts/ListUserTasksContracts.cs:43`), and its clio description tells agents to fall back to a generic userTask when a dedicated type is rejected (`clio:clio/Command/McpServer/Tools/ListUserTasksTool.cs:46-62`) | source | OA, RP (section 10, C-1) |
| Shipped guidance | No article; `element-catalog.md` does not list the element | source | `process-files` is born in PT's knowledge PR; OA, RP and SF add sections (D25) |
| `SerializeToDB` (`BO2`) | Every builder-made user task lacks it; the designer writes it from the task schema | corpus + source | the dedicated handler writes it (D20); the generic route is a side Sub-task |

---

## 3. Design decisions (summary)

The reasoning, options and consequences are in [decisions](eng-92719-file-processing-element-decisions.md); this
table is an index. None of them uses an existing prototype as an argument.

| D | Decision | Cut | Owner decision |
|---|---|---|---|
| D10 | One token `fileProcessing` (second token `processFile`); the variant is the single group present (`attachments` / `report` / `files`); `source` is an optional identity CHECK; a contradicting `userTaskName` is refused; the block is gated strictly on the token | OA | **yes** (token name, discriminator form) |
| D11 | Block contract: one group per variant, shared `action` and `saveTo`, the element-level `filter` unchanged; one shared value-source descriptor for every `recordId` / `fileNameSuffix`; read-only members are either identity CHECKs or describe-only | OA, RP | **yes** (naming bundle) |
| D12 | A source change is refused, naming the dependents; `setElement` merges member-wise, clears filter/scope/sort on a retarget, changes the action only when sent, and repairs an inconsistent storage pair it touches | OA | no |
| D13 | The generic `userTask` route and raw `addMapping` onto the binder-owned parameters are refused, **per cut** (Object in OA; Report and Process in RP) | OA, RP | no |
| D14 | `action` inferred on create from `saveTo`, always written as ConstValue; the Process variant accepts only `saveToAttachments` | OA, RP | **yes** |
| D15 | `IAttachmentStorageResolver` derives storage per entity like the designer; the designer flag is not read for behaviour (notice only, after M25); no `linkColumn: "none"`; no SysFile override; a constant `saveTo.recordId` must be an existing record | OA (dedicated), SF (SysFile) | **yes** |
| D16 | The record scope lives in the group (`attachments.recordId`, `report.recordId`) and is compiled into `DataSourceFilters` by one `Split`/`Join` helper; the filter root is the runtime root; an empty filter on a saving Object element or a Word report is refused at the end of the request (touched elements only); `sort` is the readData shape, resolved by the readData sort codec **extracted** from `ReadDataConfigBinder` (reuse RF2); `numberOfRecords` defaults to 50, not "all" | OA, RP | **yes** (R2 shape, refuse vs notice) |
| D17 | `IReportTemplateReader`; `report.printable` by `templateId`, macro or caption; MS Word only (FastReport refused, DevExpress refused); Word `separateReports` written true when omitted, explicit `false` accepted with a notice; never create or pick an unrelated printable | RP | **yes** (FastReport) |
| D18 | `files` takes one source; a collection binds both levels; a single File binds the nested item only, pending M1; an `expression` is written as given (only its meta-path tokens are checked); outer-only, none, `value` and mappings FROM `Files` are refused | RP | no (M1 decides) |
| D19 | A typed describe block with EFFECTIVE values, in the write vocabulary, plus `issues`; describe-only members are never accepted back | OA, RP | no |
| D20 | The handler writes `SchemaUId`, `ManagerItemUId`, `SerializeToDB`, size, lane; template defaults are exact copies (provenance included); nested item UIds are re-minted on create only; nested-source `DisplayValue` in the designer form | OA | no |
| D21 | Reuse map (core, CrtProcessBuilder, what must be mirrored because `Terrasoft.Configuration` cannot be referenced) — see [reuse](eng-92719-file-processing-element-reuse.md). Three of its rows are corrected there and change work packages here (C-6): Sort is **reuse + change** (RF2, OA.4); element identity mirrors the three schema **names**, the UIds live in test fixtures only (RF1, OA.1); the end-of-request ledger needs a **refusing** reconcile, which is new (RF3, OA.4) | all | no |
| D22 | Consumers: Send email attachments → ENG-95985 Send email attachments; Creatio.ai out of scope; a NEW Sub-task "Refuse collection parameters as filter values" | FE | **yes** |
| D23 | Jira: ENG-95984 File process parameter type blocks ENG-96506 Generated report + process parameter modes, relates to ENG-96505 Element readiness and object attachments mode; OA blocks RP; RP blocks SF (pr-split §12.2, L1-L5) | FE | **yes** |
| D24 | AC corrections (section 1.3) | FE | **yes** |
| D25 | One new guide, `process-files`, born in PT's knowledge PR; OA, RP, SF add sections; `element-catalog.md` gets the buildable row and the "server refuses the generic route" sentence | all | **yes** (guide name) |
| D26 / pr-split §9 | Floors: create, modify and modify-as-new-version move to **each PR's final cut** on OA and RP; SF raises none (an older server refuses SysFile loudly); version numbers are claimed at cut time, never projected | all | no |
| D27 / pr-split | One triple per Jira issue; SysFile carved out into SF (O1); the variant registry (O5); at most one of our package PRs in human review at a time; spec in a docs-only clio PR (O4) | all | **yes** (O1-O8) |
| D28 | Test strategy and C# mocking — see [test-plan](eng-92719-file-processing-element-test-plan.md) | all | no |
| D29 | Stand measurements before code (section 6) | all | no; every stand write needs the user's go-ahead |

From PT, ENG-92719 File processing element relies on: D5 (the binder rules; the Process variant's `Files` is a "file-consuming target", so P2
pairs its item and R-M2 refuses a collection without a FileLocator item), D8 (single-token element sources decode to
`sourceElement` / `sourceElementParameter`), and `BindCollection` on `IProcessMappingService`.

---

## 4. Delivery shape

### 4.1 PRs that belong to ENG-92719 File processing element

Full mechanics, invariants and contingencies: [pr-split](eng-92719-file-processing-element-pr-split.md) sections 4-14.
A human merges every PR; the agent never merges, never enables auto-merge, and never pushes to a PR with a live
approval without asking.

| PR | Repository | Title (exact) | May leave draft / merge when |
|---|---|---|---|
| CL-DOC | clio | `ENG-92719 File processing element` | owner decisions taken; merges before any FE code PR opens (BMAD: no PR before its story file) |
| PK-OA | crt-process-builder | `ENG-96505 Element readiness and object attachments mode` | opens only after CL-DOC merged (pr-split E7); leaves draft after PK-PT merged; **merges only after CL-PT merged** (never two unbundled cuts on `main`; the `ManagerMap` arm must already be in clio); pre-code measurements M10, M11(a)(b), M14, M17-Q7 done |
| CL-OA | clio | `ENG-96505 Element readiness and object attachments mode` | PK-OA merged and tagged; **KB-PT published** (the first `name=process-files` in a description must resolve) |
| KB-OA | clio-knowledge | `ENG-96505 Element readiness and object attachments mode` | CL-OA merged |
| PK-RP | crt-process-builder | `ENG-96506 Generated report + process parameter modes` | PK-OA and CL-OA merged; M1, M13, M15 done |
| CL-RP | clio | `ENG-96506 Generated report + process parameter modes` | PK-RP merged and tagged |
| KB-RP | clio-knowledge | `ENG-96506 Generated report + process parameter modes` | CL-RP merged |
| PK-SF | crt-process-builder | `<SF-KEY> SysFile attachment storage in the Process file element` | PK-RP and CL-RP merged; M7, M8, M21, M23(a-d), M25 done |
| CL-SF | clio | `<SF-KEY> SysFile attachment storage in the Process file element` | PK-SF merged and tagged; **no floor raise** |
| KB-SF | clio-knowledge | `<SF-KEY> SysFile attachment storage in the Process file element` | CL-SF merged |

Package PRs are opened at https://creatio.ghe.com/engineering/crt-process-builder/pulls, the others at
https://github.com/Advance-Technologies-Foundation/clio/pulls and
https://github.com/Advance-Technologies-Foundation/clio-knowledge/pulls; a PR is referenced by its full URL.

One window to keep short (pr-split §7): between the CL-OA and KB-OA merges, a clio built from master points at
`process-files` sections that are not yet published. KB-OA is approved before CL-OA is handed over, the human merges
KB-OA right after CL-OA, and no clio release is cut between the two merges. The same holds for CL-RP / KB-RP and
CL-SF / KB-SF.

### 4.2 Where ENG-95984 File process parameter type is a dependency (and where it is not)

| ENG-92719 File processing element work | Needs from ENG-95984 File process parameter type | Why |
|---|---|---|
| OA code | nothing | the Object variant's inputs are schema choices and filter conditions; its outputs are element parameters |
| OA merge | **CL-PT merged** | CL-PT ships the `ManagerMap` arm (`fileprocessing`, `processfile`) one release before any package emits the token, and `main` must never hold two unbundled cuts (D10, pr-split E4/E6) |
| CL-OA merge | **KB-PT published** | KB-PT births `process-files`; CL-OA writes the first `name=process-files` and re-pins `curated-knowledge-names.json` in the same commit (decisions §0, correction X2) |
| OA e2e "multi-instance sub-process per file" | a callee with a File parameter; P1 binds `InputRecordCollection` | D22 |
| RP Process-parameter variant | File / FileCollection, dotted process paths (D4), `BindCollection` and P2/R-M2 (D5) | D18 |
| RP describe of `files` | the single-token decode (D8) | the element block decodes its own bindings, so the D8 follow-up Sub-task is not needed |

---

## 5. Work packages

The hours in these tables are relative size weights from an earlier human-scale costing ("AI" = rows the agent
writes; "NC" = stand, human probes, review rounds, publication). They rank the work packages; they are not the
estimate. The estimate, with an AI agent writing the code, is section 8. Paths are proposals where the
file is new; the `FileProcessing/` folder follows the existing per-family folders `PB/Email/` and `PB/Approval/`.

### 5.0 Day 0 (shared, before any code)

| Id | Task | Who | AI h | NC h |
|---|---|---|---|---|
| FE.0a | Owner decisions D10, D11, D14-D17, D22-D25, O1-O8 ([open-questions](eng-92719-file-processing-element-open-questions.md)) | owner | - | 1-1.5 |
| FE.0b | Jira: create SF and the side Sub-tasks (section 5.4; its conditional rows only when their trigger fires, and no Sub-task for Send email attachments, which ENG-95985 Send email attachments already owns), all of type Sub-task; re-link per D23 ([pr-split](eng-92719-file-processing-element-pr-split.md) §12.2, L1-L5); apply the D24 AC edits (decisions Part D); claim the cut in one shared place (a comment on ENG-92719 File processing element) and agree a merge window (O8) with the owners of ENG-99970 CAADT runs for BPMS Tools cost 6-15x other teams - find and close the gap, ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph validator into the write path (ENG-88414) (ENG-88414 is AI-driven application development) and ENG-92113 Deliver clioprocessbuiilder package - CI for adding package into clio release | human | - | 0.5-1 |
| FE.0c | CL-DOC (pr-split §5.6): the BMAD set named per AGENTS.md — `spec/prd/prd-eng-92719-file-processing-element.md`, `spec/adr/adr-eng-92719-file-processing-element.md` (D10-D29 condensed, including the variant registry, O5), `spec/stories/story-eng-92719-file-processing-element-1.md` (OA), `-2` (RP), `-3` (SF); `-4` and `-5` for the two side Sub-tasks of section 5.4 ("Refuse collection parameters as filter values", "Builder-made user tasks do not set SerializeToDB"), so that their package PRs open with a story file (pr-split §5.8); `spec/test-plans/tp-eng-92719-file-processing-element.md` (with a column mapping TC-nn to the TC-U-/TC-I- ids of the repository convention), and `spec/sprint-status.yaml` rows in `ready-for-dev` with `jira`, `file`, `prd`, `adr`, `test_plan`, `depends_on` / `blocks` in the shape of the ENG-99856 Sub-process element: support MULTI-INSTANCE (running the callee once per item of a collection) rows (`clio:spec/sprint-status.yaml:4170-4196`); plus this analysis folder if the owner wants it in the repository (the ENG-92707 Sub-process element: selection + parameter sync folder `spec/eng-92707-sub-process-element/` is the precedent on master). If both docs PRs carry their analysis folders, CL-DOC merges after CL-PT-DOC, so this folder's links into ../eng-95984-file-parameter-type/ resolve on master (pr-split E10) | agent | 2-3 | 1-2 (owner review) |
| FE.0d | Capture session of [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §12: SC-0 (read-only calibration on PrintInvoiceReport), SC-1 (the user builds and saves `UsrFpSc1Capture` once, one schema write, go-ahead), SC-2 (read-back by `pull-pkg` / `export-schema`) | user + agent | - | 1.5-2.5 |

### 5.1 ENG-96505 Element readiness and object attachments mode (OA)

Scope: the element family, the Object attachments variant, **dedicated `<X>File` storage only**. SysFile sources and
targets are refused with the D15 fallback text; a designer-built SysFile element is described (block decoded, issues
reported) and `setElement` / `setFilter` on it are refused until SF. Report and Process-parameter elements stay
generic.

| Id | Repo | Task | Files | AI h | NC h |
|---|---|---|---|---|---|
| OA.0 | stand | Pre-code measurements (section 6.2) M10, M11(a)(b), M17-Q7 (version-independent, may run on PT's cut) and M14 (on day 0, on 1.6.6.54, before PT's first cut is installed: open-questions B.0); M26 (traps T-19: a designer-built Object save plus a multi-instance callee; it gates the OA.12 sentence and the CL-OA record, not code); the user builds the e2e fixture `UsrFpOaDesignerFixture` that OA.11 needs (one schema write, go-ahead; recipe in OA.11) | - | - | 4-5.5 |
| OA.1 | pkg | **Family core.** `ElementTypes.FileProcessing = "fileprocessing"` + `"processfile"`; the variant registry (`IFileProcessingVariant`, keyed by the schema UId that `UserTaskSchemaIdentity` resolves by NAME through `FindInstanceByName`, `PB/Elements/UserTaskSchemaIdentity.cs:48-56, 71-74`; production code mirrors the three schema names next to `ProcessDesignConstants.UserTasks` `PB/ProcessDesignConstants.cs:578`, and the UIds live in the test fixtures and the drift guard only, reuse RF1) with the Object variant as its first entry; `FileProcessingElementHandler` (`Create`: `SchemaUId`, `ManagerItemUId`, `SerializeToDB` from the task schema, nested item UIds re-minted with `IL2` = element UId; `CanBuild` / `CanDescribe` through the registry), registered **before** `UserTaskElementHandler`; strict block gate; `WritesAConfigurationBlock`; the `setElement` "no field" list and dispatch; `FileProcessingDescriptor` (+ groups and the shared `FileProcessingValueSourceDescriptor`) on the build and update contracts. If the strict keys of ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph validator into the write path (ENG-88414) (ENG-88414 is AI-driven application development) landed first, teach its allow-list the write members and its read-back list the describe-only members | `PB/ProcessDesignConstants.cs:475+`; NEW `PB/FileProcessing/IFileProcessingVariant.cs`, `FileProcessingVariantRegistry.cs`; NEW `PB/Elements/FileProcessingElementHandler.cs`; `pkg:packages/CrtProcessBuilder/Files/src/CrtProcessBuilderApp.cs:114-152`; `PB/Elements/ProcessElementFactory.cs:100-113`; `PB/Graph/ProcessGraphBuilder.cs:442-451`; `PB/Operations/ElementOperations.cs:278-290`; `PB/Contracts/ProcessDescriptorContracts.cs:13-246`; `PB/Contracts/ModifyContracts.cs:307-514` | 2.5 | - |
| OA.2 | pkg | **Binder and applier.** A static `FileProcessingConfigBinder` shared by the handler's `Configure` and by `setElement` (the `OpenEditPageConfigBinder` structure): validate everything before the first write; the D11 parameter table; D14 inference; values the caller chose are new `ProcessSchemaParameterValue`s (`Source` before `Value`, `ModifiedInSchemaUId` = process); template defaults (`ConsiderTimeInFilter`, an omitted `RecordsToRead`) copied exactly (D20); value sources through `ApplyMapping`. Every value-source `expression` (`saveTo.recordId` here; `report.*` and `files` in RP) runs the same length bound and `MetaPathTokenReference` check as `openEditPage.recordId.expression` (`PB/Elements/OpenEditPageConfigBinder.cs:874-881`): through `ApplyMapping`'s expression arm (`PB/Mappings/ProcessMappingService.cs:457-468`) where the value goes through it, directly otherwise. The new surfaces join the check's call-site list (`PB/Formulas/MetaPathTokenReference.cs:52-55`) and get a PBT case each (a misspelled token and a token from another process refused). `FileProcessingConfigApplier`: D12 rules 1-5; a source change refused with dependents from `ProcessElementDependencyScanner`; a missing template parameter after `GetDesignInstance` refused, never hand-created | NEW `PB/FileProcessing/FileProcessingConfigBinder.cs`; NEW `PB/Operations/IFileProcessingConfigApplier.cs`, `FileProcessingConfigApplier.cs`; `PB/Mappings/ProcessMappingService.cs:48-72` (consumer only) | 3 | - |
| OA.3 | pkg | **Storage resolver (dedicated mode).** `IAttachmentStorageResolver.Resolve` / `Decode` (never throws) over a thin `IEntitySchemaHierarchyReader` adapter (`SchemaManager.GetAllParents` is non-virtual); eligibility rules and the `<X>File` / `FileLead` rule (D15); SysFile results refused through a switch SF flips; refusals R1-R9, warnings W6/W7; a constant `saveTo.recordId` validated as an existing record of the record object through a new internal entry point of `EnsureReferencedRecordExists`; a mapped `recordId` gets the "may be empty at run time" notice | NEW `PB/FileProcessing/IAttachmentStorageResolver.cs`, `AttachmentStorageResolver.cs`, `IEntitySchemaHierarchyReader.cs`, `EntitySchemaHierarchyReader.cs`; `PB/Parameters/ProcessParameterValueValidator.cs:168-204` | 2.5 | - |
| OA.4 | pkg | **Record scope, filter target, sort, empty filter.** `FileProcessingScopeFilter.Split` / `Join` (legacy scope = an InFilter on the link column; a root OR wrapped as `AND(scope, OR)`; `Split` lifts no `recordId` from a decode that is not `DecodedCompletely`, and `Join` keeps the `isNull` flag, both since 1.6.6.82, ENG-99970); `FileProcessingFilterTarget` (priority 50, between Signal 100 and DataNode 0) claiming through the registry, with V1 root, V2 unconfigured, V3 type, scope-set-twice refusals; **sort codec extraction** (reuse RF2): `ResolveOrderInfoValue` + `ParseSortDirection` and `DescribeSort` + `DescribeSortDirection` move out of `ReadDataConfigBinder` (all `private static` today) into a shared internal codec that takes the element label and the element kind, so the Read data refusals stay byte-identical (`Read data '<label>': ...`) and the file element gets its own wording; `ReadDataConfigBinder` then calls the codec; the file element sorts on one direct column of the runtime root; `numberOfRecords` 1..5000 + the "at most 50 files" notice; an end-of-request ledger on the `IDeleteDataNoticeLedger` pattern whose reconcile may **refuse** (new: today's ledger only adds notices, `PB/Elements/DeleteDataNoticeLedger.cs:46`, reuse RF3), called at both reconcile sites, the modify middle `PB/Design/ProcessEditPipeline.cs:135` (next to the precedent refusal `EnsureFormulasStillBelongOnTouchedConnectors` `:139`, before layout and the pre-save gate) and the build path `PB/Design/ProcessBuildHandler.cs:547` (refuse Object + save with no selecting filter, notice for use-in-process; thrown outside the operation loop, so `failedOperationIndex: null`); `ProcessFilterApplier` refusal text lists the element | NEW `PB/FileProcessing/FileProcessingScopeFilter.cs`, `IFileProcessingFilterLedger.cs`, `FileProcessingFilterLedger.cs`; NEW `PB/Filters/FileProcessingFilterTarget.cs`; NEW shared sort codec (proposed `PB/Elements/ReadDataSortCodec.cs`); **changed** `PB/Elements/ReadDataConfigBinder.cs:66, 127, 793-825, 917-962` (the four methods move out, the two call sites call the codec); `PB/Design/ProcessEditPipeline.cs:135-139`; `PB/Design/ProcessBuildHandler.cs:547`; `CrtProcessBuilderApp.cs:175-180, 341`; `PB/Filters/ProcessFilterApplier.cs:66-72` | 3.5 | - |
| OA.5 | pkg | **Refusals of the other routes.** Generic `userTask` naming the Object schema, keyed on the resolved schema UId right after `FindInstanceByName`; raw `addMapping` onto the binder-owned configuration parameters (value inputs `ConnectedObjectId` stays legal and runs the block's rules). Since 1.6.6.69 raw `addMapping` writes a correct schema UId onto the storage parameters, so the refusal takes away a working route; it stays because the route bypasses the block's checks and still accepts a `SysSchema` row Id (traps T-54). Extend ENG-102113's `FindDataObjectSlot` / `EnsureTheDataObjectIsKept` slot table (keyed through `UserTaskSchemaIdentity`) instead of adding a second mechanism. ENG-102112 (unmerged, claims 1.6.6.87) inserts its generic Pre-configured page refusal at the same point in `UserTaskElementHandler.cs`: follow its shape, and expect a merge conflict | `PB/Elements/UserTaskElementHandler.cs:76-79`; `PB/Mappings/ProcessMappingService.cs:96-111, 136-183, 203-226` (target resolution, the ENG-102113 guards) | 1 | - |
| OA.6 | pkg | **Describe and serialization.** `DescribeFileProcessingInfo` on `DescribeProcessElement`: effective values, identity CHECK vs describe-only members, `storage` from `Decode`, the record scope lifted by `Split`, value sources decoded from single tokens, `issues` (H-G3-1 source half as "throws at run time" if M11 confirms it; link column missing; a sort/filter column the root lacks; hidden `OrderByInfo` entries; unset action); tolerant of unconverged snapshots. Nested-source `DisplayValue` in the designer form `[#<el>.<collection>:<item>#]` | `PB/Contracts/DescribeContracts.cs:112-378`; `PB/Describe/ProcessDescriber.cs`; `PB/ProcessSchemaElementLocator.cs:285-297`; `PB/Mappings/ProcessMappingService.cs` (`BuildSourceValue`) | 2.5 | - |
| OA.7 | pkg | **Package tests** (cases in [test-plan](eng-92719-file-processing-element-test-plan.md)): handler, binder, applier, resolver (fake hierarchy reader), scope filter, filter target, ledger, describe round trip (issues, uk-UA culture, identity CHECK refusal), registry ("a variant gets all five claims or none"), D20 writes (template copies, re-mint before a same-request mapping, modify keeps template UIds), parity with SkillFilesValidationProcess; the tripwire and the fixtures' own handler list; the extracted sort codec (both labels, both directions, the stored-entry ranking) and the **Read data regression**: `PBT/ReadDataConfigBinderTests.cs` and `PBT/ReadDataConfigApplierTests.cs` (35 and 6 sort references today) run green **unchanged**, which is the proof that the Read data messages stayed byte-identical; the end-of-request refusal on both the build and the modify path (`failedOperationIndex` null). Every new fixture `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`, AAA, `because:`, `[Description]` | NEW `PBT/FileProcessing*Tests.cs`, `AttachmentStorageResolverTests.cs`, `ReadDataSortCodecTests.cs`; `PBT/ReadDataConfigBinderTests.cs`, `PBT/ReadDataConfigApplierTests.cs` (must stay green unedited); `PBT/CrtProcessBuilderAppTests.cs:139-199`; `PBT/ProcessDesignTestSupport.cs:140-165` | 4.5 | 0.5-1 (net472 run from `C:/Projects/workspace/<short>`, MAX_PATH) |
| OA.8 | pkg | **Package docs and restamp.** Capture doc (the convention of the 15 existing `pkg:docs/*-capture.md`), architecture rows, diary entry, descriptor restamp via the rebundle script, tag `crtprocessbuilder-<ver>` on the producing commit | NEW `pkg:docs/process-file-element-capture.md`; `pkg:docs/process-builder-architecture.md` §3.4/§3.5; `pkg:.codex/workspace-diary.md`; `pkg:packages/CrtProcessBuilder/descriptor.json` | 0.5 | - |
| OA.9 | clio | **Rebundle and floor.** Archive + four pins; the three `[RequiresPackage]` literals to the final cut with their comment blocks, `ProcessDesignerRequiresPackageAttributeTests.cs:67-68, 106`, the enforced-floor clause in three descriptions, `McpCapabilityMap.md` floor rows, the new fixture's `MinimumPackageVersion` — all in the archive's commit | `clio:clio/CrtProcessBuilder/*.gz`; `clio:clio.tests/Common/BundledProcessBuilderPackageTests.cs:318`; `clio:clio/Command/CreateBusinessProcessCommand.cs:247`; `ModifyBusinessProcessCommand.cs:203`; `ModifyProcessAsNewVersionCommand.cs:66`; `clio:docs/McpCapabilityMap.md` | 0.5 | - |
| OA.10 | clio | **Descriptions and DTO.** create / modify / describe: the token in the type list, the block clause naming `get-guidance name=process-files`, the `addElement` / `setElement` lists, the describe clause; re-measure by lowering the ceiling and rewrite the figures in the budget-test comment (expected end state about 307 B of headroom on create and 44 B on modify, estimated from the 2026-10-08 sizes with no budget swap; re-measured in the PR); keep the seven process-designer short forms within 18,432 B (a new sentence worded with "never" or "must not" counts there); re-pin `curated-knowledge-names.json` in the **same commit** to the published KB-PT generation. `list-user-tasks` description: add the Object schema to the dedicated-type exceptions and exclude it from the "fall back to a generic userTask" advice, and drop the stale count word (section 10, C-1). MCP prompts (decisions D25): `ListUserTasksPrompt` excludes ObjectFileProcessingUserTask from its generic-`userTask` advice and points to `fileProcessing`; `CreateBusinessProcessPrompt` gains the `fileProcessing` sentence (the group present is the variant; not a generic `userTask` named ObjectFileProcessingUserTask; `get-guidance name=process-files` owns the block); `ModifyBusinessProcessPrompt` gains the D12 merge rule; `DescribeProcessPrompt` lists `fileProcessing` among the blocks; each pinned by TC-90. Typed `DescribedFileProcessing` with `[JsonExtensionData]` on every nested type, and `///` summaries on `DescribedFileProcessing`, every nested type and every public member (the AGENTS.md inline-documentation policy; the sibling `Described*` types in the same file are the model); no new `CLIO*` analyzer warning in any touched file; "forwards the block verbatim" tests; a permanent capability probe on `ProcessElementUpdateDescriptor.FileProcessing`; capability-map rows | `clio:clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs`, `ModifyBusinessProcessTool.cs`, `DescribeProcessTool.cs`; `clio:clio/Command/McpServer/Tools/ListUserTasksTool.cs:46-62`; `clio:clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs`; `clio:clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`; `clio:clio/Command/ProcessModel/IProcessDescriber.cs`; `clio:clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs`; `clio:clio.tests/Command/McpServer/ListUserTasksToolTests.cs`; `clio:clio/Command/McpServer/Prompts/ListUserTasksPrompt.cs:22-25`, `clio:clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs`, `ModifyBusinessProcessPrompt.cs`, `DescribeProcessPrompt.cs`; the prompt pins in `clio:clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs`, `ModifyBusinessProcessToolTests.cs`, `DescribeProcessToolTests.cs` | 3 | - |
| OA.11 | clio | **e2e** `ProcessFileElementToolE2ETests` (`[Category(McpE2ECategories.ProcessDesigner)]`, which TeamCity excludes, so it is run on the stand by hand): Object create + describe in dedicated storage; `saveTo.recordId` from a constant, a process parameter, `sourceColumn`, `[#SysVariable.CurrentUserContact#]`; `attachments.recordId` from Read data (TC-70); **Process file → Modify data re-finding the created file (TC-71**: Object save to a Contact, then `changeData` on `ContactFile` filtered by Contact + `Name` contains + `CreatedOn` = CurrentHour, setting `Tag`; built and described, not run); two consumers of `ObjectFiles`; multi-instance sub-process per file (TC-72); a partial `setElement` that keeps the sort; refusals (generic route, source change, filter root, empty-filter save, a SysFile source and target). **Two cases on the designer-built fixture `UsrFpOaDesignerFixture`** (TC-46): (1) its SysFile Object element F1 is described read-only (`storage: sysFile`, block decoded, `issues` listed), then `setElement` and `setFilter` on F1 are refused naming SF, and a re-read shows F1's node unchanged; (2) an unrelated `addParameter` (a Text parameter with a unique name) leaves the Report element F2 intact: F2's node in `SysSchema.MetaData` and its resource rows, read from the raw `SysSchema` row (describe is not a capture read path: serialization-capture §1, S6), are byte-compared before and after within the same run, so a re-run on an already builder-saved fixture stays valid. **The fixture**: process `UsrFpOaDesignerFixture` (caption "FP OA designer fixture") in package `Custom`, built once by the user in the designer and never run (OA.0); a Lookup parameter `PInvoice` → Invoice; F1 = Process file, Object attachments, `Account address` (SysFile storage, SC-1 E5 shape, nothing else set); F2 = Generated report, printable `Invoice`, filter Id = `PInvoice`, Save to object attachments → `Invoice (<InvoiceFile caption>)`, record = `PInvoice` (SC-1 E6 shape); Start → F1 → F2 → End. It is separate from SC-1 `UsrFpSc1Capture` because both cases issue a modify, and SC-1 must stay as captured for SC-2/SC-3. Arrange reads it by name, read-only; when it is absent: `Assert.Ignore("Designer fixture UsrFpOaDesignerFixture not found on '<env>'. Build it as described in plan §5.1 OA.11 to run this test.")` — the DoD run still needs Passed with Ignored = 0, so the fixture is built before that run. RP.7 and SF.3 reuse it. Must stay green: `ListUserTasksToolE2ETests`, `ValidateProcessGraphToolE2ETests`, the PT fixtures | NEW `clio:clio.mcp.e2e/ProcessFileElementToolE2ETests.cs`; pattern `clio:clio.mcp.e2e/SubProcessElementToolE2ETests.cs`; floor gate `clio:clio.mcp.e2e/Support/Mcp/ProcessDesignerE2EArrange.cs:73-93` | 3 | - |
| OA.12 | kb | `process-files` sections: the element and "the group is the variant"; `attachments` (`recordId`; `numberOfRecords` defaults to 50 and does NOT read all; the sort shape with the M24 result); the member-wise merge sentence; identity CHECK vs describe-only ("remove the describe-only members before resubmitting"); dedicated storage and "SysFile storage is refused in this version"; the H-G3-1 warning if M11 confirms it; the action per variant, and the sentence "after saving, `ObjectFiles` points at the source files" only after M26 confirms it, as "measured <date>" (until then the guide states only which outputs each action value fills; traps T-19); the endpoint sentence of decisions D-3 ("most shipped uses end in a record's attachments; when a following element needs the files, map the element's output collection"), never "rarely an endpoint" (TC-88); consumers (Send email attachments and Creatio.ai not buildable through this tool yet). `element-catalog.md`: the buildable row (about +313 chars; 456 left on 2026-10-08) and the generic-route sentence worded for this cut. Pin test; size test; libraryVersion bump; own worktree under `.worktrees/<task>/` | `kb:guidance/mcp/guides/processes/files.md`; `kb:guidance/mcp/guides/processes/element-catalog.md`; `kb:bundle-source.json`; NEW `kb:automation/Clio.Knowledge.Bundle.Tests/ProcessFileGuidanceTests.cs` | 2 | - |
| OA.13 | all | **Non-compressible tail.** Stand verification after the final cut (section 6.3) incl. the user's designer checks (M22); serialization parity against SkillFilesValidationProcess; gates 1 and 3 (comprehensive, three lenses) and their fixes; one human review round (one push: fixes + restamp + rebundle); two rebundle/reinstall cycles; knowledge publication and the `info-knowledge` check | - | - | 12-15 |
| | | **OA total** | | **28.5-31.5** | **16.5-21.5** |

Visible changes to list in PK-OA's description: Object elements of existing processes describe as
`buildType: fileprocessing` (was `usertask`); `{type: userTask, userTaskName: "ObjectFileProcessingUserTask"}` is
refused; `setFilter` on an Object element now validates its root. Listed as a refactor with no visible change: the
Read data sort codec moved out of `ReadDataConfigBinder` into a shared codec, and the Read data refusal texts are
byte-identical (OA.4, OA.7).

### 5.2 ENG-96506 Generated report + process parameter modes (RP)

Two variant **registrations**, one commit group each, so that the Process-parameter half can be cherry-picked out
(pr-split contingency X2) if its stand proof fails while the report half is ready.

| Id | Repo | Task | Files | AI h | NC h |
|---|---|---|---|---|---|
| RP.0 | stand | Pre-code measurements M1 (+ M2), M13, M15 (section 6.2); M1 needs a designer-built probe | - | - | 2.5-3.5 |
| RP.1 | pkg | **Report variant.** `IReportTemplateReader` (ESQ on `SysModuleReport`, `UseAdminRights = false`, columns `Id`, `Caption`, `Type.Name`, `SysEntitySchema.Name`, the module-entity fallback, `ConvertInPDF`); the `report` group; D17 refusals (missing / unknown printable with the "create it in System Designer → Report setup" hint and up to N candidates by `templateId`; ambiguous caption; DevExpress; FastReport; no object; bad `fileNameSuffixColumn`); Word `separateReports`; `report.recordId` compiled by `FileProcessingScopeFilter` as `Id = value`; `fileNameSuffix` constant through the localizable resource path; a `report.*` value-source `expression` checked as in OA.2; the same-object printable swap keeps the filter (V3 re-run), a different object clears it; notices (ConvertInPDF, several records into one target, feature off, temporary files into an Out parameter, Word `false`); the one-method feature seam | NEW `PB/FileProcessing/IReportTemplateReader.cs`, `ReportTemplateReader.cs`, `IFileProcessingFeatureReader.cs`, `FileProcessingFeatureReader.cs`; `PB/FileProcessing/FileProcessingConfigBinder.cs`; `PB/FileProcessing/FileProcessingScopeFilter.cs` | 3 | - |
| RP.2 | pkg | **Process-parameter variant.** The `files` binder over PT's `BindCollection`: a collection binds `Files` and `Files.File` (item resolved, never assumed: `File`, `ObjectFile` or the declared name); a single File binds `Files.File` only **if M1 passed** (else refused, "wrap one file in a FileCollection"); `expression` onto `Files.File` as given, with a notice (it goes through `ApplyMapping`, so the meta-path check runs; nested-item tokens such as `ObjectFiles.File` pass); refusals F-P1 (no FileLocator item, outer-only, none, mapping FROM `Files`, `value`); `useInProcess` refused; `saveTo` required | `PB/FileProcessing/FileProcessingConfigBinder.cs`; `PB/Mappings/IProcessMappingService.cs:13-22` (consumer) | 2 | - |
| RP.3 | pkg | **Claims through the registry** for both schemas (identity, generic-route and raw-mapping refusals, the Report filter-target claim, describe incl. `report.*`, `files` and `bindingLevels`); the `ProcessFilterApplier` text for the Process variant | `PB/FileProcessing/FileProcessingVariantRegistry.cs`; `PB/Contracts/DescribeContracts.cs`; `PB/Filters/ProcessFilterApplier.cs:66-72` | 1 | - |
| RP.4 | pkg | **Package tests**: reader fake; printable by id, macro, caption and the ambiguous case; Word default and the explicit-`false` notice; name-column rules; same- vs different-object swap; FastReport / DevExpress refusals (unit only, no stand fixture exists); Process metadata at both levels (GS1/GS2 and the nested item UId reference); the single-file switch in both positions; F-P1; parity with PrintInvoiceReport and FileParameterProcess | NEW `PBT/ReportFileProcessing*Tests.cs`, `ProcessFileProcessing*Tests.cs` | 3 | - |
| RP.5 | pkg | Capture-doc sections for both variants; diary; restamp; tag | `pkg:docs/process-file-element-capture.md`; `pkg:.codex/workspace-diary.md` | 0.5 | - |
| RP.6 | clio | Rebundle; floor raise (the clause keeps its length); capability-map rows; typed `report` / `files` DTO members, each with a bag and `///` summaries on every new type and public member; no new `CLIO*` analyzer warning in any touched file; `ListPrintablesEnvelope` gains `count` and `printables`; `list-user-tasks` exceptions and `ListUserTasksPrompt` extended to the two schemas; `CreateBusinessProcessPrompt`'s `fileProcessing` sentence extended to the `report` and `files` groups (TC-89 and TC-90 extended) | `clio:clio.mcp.e2e/Support/Results/ListPrintablesEnvelope.cs:12-13`; `clio:clio/Command/ProcessModel/IProcessDescriber.cs`; `clio:clio/Command/McpServer/Tools/ListUserTasksTool.cs:46-62`; `clio:clio/Command/McpServer/Prompts/ListUserTasksPrompt.cs`; `clio:clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs` | 2 | - |
| RP.7 | clio | **e2e**: Word SaveToFiles with `report.recordId`; Report use-in-process into the Process variant; Process variant from a FileCollection and (if M1 passed) a single File; the Object → Process chain (TC-73); **Add data → generated report (TC-74**: `addData` of a `DNSGuideReport` row, then a Report on the "DNS Requirements" printable with `report.recordId <- AddData1.RecordId`, then `deleteData` of the row; built and described, not run; `Assert.Ignore` when `list-printables` has no "DNS Requirements" row); the `separateReports: false` notice; the fixture `UsrFpOaDesignerFixture`'s F2 now describes as `fileprocessing` (read-only); refusals (unknown printable with the Report setup hint, a non-text name column, `useInProcess` on the Process variant, the generic route for both schemas); `Assert.Ignore` when `list-printables` returns 0 rows; a **read-only** describe of the four shipped product processes (never modified on the stand); the Process variant's `Files` mapping rules over the wire (TC-91 = E2E-14a-d of the ENG-95984 File process parameter type test-plan, owner decision O-TP1 option B) | extend `ProcessFileElementToolE2ETests.cs` or NEW `clio:clio.mcp.e2e/ProcessFileReportAndParameterToolE2ETests.cs` | 3 | - |
| RP.8 | kb | Report section (`templateId` → `report.printable`, Word `separateReports`, the same-object swap keeps the filter, temporary files, record order unspecified, captions in the runner's culture); process-parameter section (`files`, `expression` as given, an unset single File throws, an empty source copies nothing); "Add data creates the report's data row first"; `element-catalog.md` generic-route sentence extended to both schemas; split out `process-files-report` if the guide passes 80% of its budget (own registration) | `kb:guidance/mcp/guides/processes/files.md`; `kb:guidance/mcp/guides/processes/element-catalog.md`; `kb:bundle-source.json` | 1.5 | - |
| RP.9 | all | **Non-compressible tail**: stand proofs (section 6.3); parity against PrintInvoiceReport and FileParameterProcess; gates 1 and 3; one review round; rebundle/reinstall; publication | - | - | 10-12.5 |
| | | **RP total** | | **16-17.5** | **12.5-16** |

Visible changes for PK-RP's description: Report and Process-parameter elements, including PrintInvoiceReport,
PrintQuotationReport, PrintContractsReport and GenerateDNSRecordsSpecification, describe as `fileprocessing`;
`setFilter` on a Report element moves from `DataNodeFilterTarget` to the new target (root checked).

### 5.3 NEW Sub-task: SysFile attachment storage in the Process file element (SF)

| Id | Repo | Task | Files | AI h | NC h |
|---|---|---|---|---|---|
| SF.0 | stand | Pre-code measurements M7, M8, M21, M23(a)-(d), M25; the M8 upload and the M21/M23 probes are built by the user in the designer (section 6.2) | - | - | 5-7 |
| SF.1 | pkg | `Resolve` SysFile mode lifted per variant through the registry (Object read and write; Report and Process targets); the SysFile scope in the shape M7 captures (default: a lookup InFilter on `RecordId` with `referenceSchemaName` = the record object), `Split` accepting both shapes; V9 for the SysFile root (record-object sort columns refused); `RecordSchemaName` expectations; the M25 designer-flag notice (only if M25 confirmed the read returns true on the stand); SysFile-to-SysFile retarget | `PB/FileProcessing/AttachmentStorageResolver.cs`, `FileProcessingScopeFilter.cs`; `PB/Filters/FileProcessingFilterTarget.cs`; `PB/FileProcessing/FileProcessingFeatureReader.cs` | 3 | - |
| SF.2 | pkg | Tests: resolver in SysFile mode; scope compile and `Split`; Account address → Contact address retarget clears the scope; V9; the notice through the faked seam (ON: none, OFF: notice, never a refusal); capture doc checked against M21 | `PBT/AttachmentStorageResolverTests.cs`, `FileProcessingScopeFilterTests.cs`; `pkg:docs/process-file-element-capture.md` | 2 | - |
| SF.3 | clio | Rebundle; **no floor raise**; NEW `ProcessFileSysFileStorageToolE2ETests` (`MinimumPackageVersion` = this cut): Object on a SysFile entity (create, describe, `saveTo` into a SysFile target), Report and Process into a SysFile target; OA's SysFile-refusal e2e re-pointed to its positive form (on `UsrFpOaDesignerFixture` F1, `setElement` / `setFilter` are now accepted and read back) | NEW `clio:clio.mcp.e2e/ProcessFileSysFileStorageToolE2ETests.cs` | 2.5 | - |
| SF.4 | kb | Storage section rewritten: SysFile hazards (record-object sort columns, `RecordSchemaName`, "the user checks the record's Freedom UI attachment list"); the refusal sentences removed behind a version gate | `kb:guidance/mcp/guides/processes/files.md` | 1 | - |
| SF.5 | all | **Non-compressible tail**: stand proofs; gates 1 and 3; review round; rebundle; publication | - | - | 6-8 |
| | | **SF total** | | **8.5-10** | **11-15** |

If the owner rejects O1, SF's content folds back into OA under D15's fallback (SysFile pending M8/M23), and OA's
pre-code gate grows by the five SF measurements.

### 5.4 Side Sub-tasks (each its own triple, off the critical path, never inside a final review window)

| Proposed Sub-task (type Sub-task) | Parent | Trigger / slot | Size |
|---|---|---|---|
| Refuse collection parameters as filter values (one guard in `PB/Filters/ProcessFilterService.cs:533-595`, which since 1.6.6.75 covers all three spellings; M19 sets its urgency and still holds on 1.6.6.85) | ENG-92719 File processing element | any gap while PK-OA is a draft | S (fast lane): about 4-6 h for its triple (inference), outside the FE total |
| Builder-made user tasks do not set SerializeToDB (generic route; D20) | ENG-92719 File processing element | after PK-SF | S: about 4-6 h for its triple (inference), outside the FE total |
| Report the designer storage re-save defect to CrtProcessDesigner (H-G3-1: opening and saving a legacy Object element writes `SourceDataEntitySchemaUId = <X>File`) | ENG-92719 File processing element | only if M11 confirms it | report only, no PR in these repositories |
| Process parameter source of the Process file element | ENG-92719 File processing element | only on contingency X2 | replaces RP's Process half |
| Process file → Send email attachments | **No Sub-task is created.** ENG-95985 Send email attachments already owns exactly this scope (it reuses `BindCollection` and adds its slots to the file-consuming targets); the only Jira action is link L5 (ENG-95984 File process parameter type and ENG-96505 Element readiness and object attachments mode **block** ENG-95985 Send email attachments). Decisions D27 and pr-split §12.3 carry no such row (C-5) | its own work starts after PK-OA and PK-PT merge | - |

---

## 6. Stand verification — what no unit test can cover

Two things are out of reach of a package unit test: the platform's parameter sync on `SchemaUId`
(`PBT/UserTaskElementHandlerCreateTests.cs:118-125`) and the runtime reader, which is `internal`
(`CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs`). Everything runtime-critical that is only
basis=source is measured here first.

### 6.1 Rules

- Stand `Creatio` (core 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.54 when measured on 2026-10-02). A clio
  built from master (bundle 1.6.6.85, floors 1.6.6.77) refuses the process tools against a stand still on 1.6.6.54,
  so any re-measurement needs the stand upgraded or an older clio, and every record names the package version.
- **Every write (a saved process, a run, an upload, a fixture record) needs the user's explicit go-ahead.**
- Schema writes and runs strictly one at a time: a parallel burst trips IIS rapid-fail and takes the app pool down.
- Cleanup through `execute-dataservice-batch`: this stand rejects HTTP DELETE. A remote `delete-schema` takes about
  6 minutes; use the CLI `--timeout`.
- Designer probes use the UO method (a NEW unsaved process; state read from
  `Terrasoft.ProcessSchemaManager.items[0].instance.flowElements`; tab closed unsaved) or are built by the user.
  Probes that need a File parameter or the Process variant are built by a human, because the builder cannot build
  them until these tickets land. UI results (attachment lists, card display) are checked by the user.
- Evidence comes from `SysProcessElementLog` (not `SysProcessLog`, which has the root row only).
- The stand carries one cut at a time. Reinstall in the same breath as a rebundle and tell every verifier.
- Before trusting a guidance-dependent run, check that `info-knowledge` shows the expected library version (a failed
  `update-knowledge` keeps serving the old guidance silently), and that the session's MCP clio is the PR's clio
  (`get-tool-contract` shows the new vocabulary).

### 6.2 Before code (gates)

| M | What | Method | Write? | Gates |
|---|---|---|---|---|
| M10 | The designer's object list equals the resolver's prediction (Account, Lead / FileLead, Account address, DNS guide file, Uploaded file, FeedFile, VwSysProcessFile) | UO, read the full dropdown | no | OA: D15 refusal set R3-R5 |
| M11 (a)(b) | H-G3-1 both halves: (a) open PrintQuotationReport's element and close it, read in memory; (b) a disposable designer-built legacy Object element "Account", opened and closed with no change, in memory: does `SourceDataEntitySchemaUId` become `AccountFile`? (c, optional, go-ahead) save it and run once | UO; (c) a disposable run | (a)(b) no | OA: issue severity, guide warning, bug-report Sub-task |
| M14 | Server-built element on 1.6.6.54 through the generic route: `ConsiderTimeInFilter` "false", template nested UIds, no nested mapping rows, `BO2` absent, `BL7`; the user opens the card to check a builder-written nested binding's display | On day 0, on 1.6.6.54, before PT's first cut is installed (open-questions B.0): disposable process, read `SysSchema.MetaData` | yes | OA: D20 pins |
| M17-Q7 | Read-only FK pre-check for an empty `ConnectedObjectId` (legacy target fails on the FK, SysFile orphans) | read-only query | no | OA: notice severity |
| M1 (+M2) | Nested-only `Files.File <- FileParam` copies one file; without the value the element errors with an NRE | designer-built probe `UsrG1FilesBindingProbe`, run, queries | yes | RP: the single-file path (D18) |
| M13 | describe of PrintContractsReport: converged (`ConnectedObjectId` Guid) or snapshot (Lookup) | read-only describe | no | RP: describe fixtures |
| M15 | FastReport packages and printables present? | read-only `list-packages` | no | RP: FastReport stays refused unless present and working |
| M7 | The exact `DataSourceFilters` JSON the designer writes for SysFile `RecordId = <record>` and `= <process parameter>` | UO, read-only | no | SF: scope shape |
| M8 | SysFile READ: a Freedom UI upload to a SysFile-stored record is found by an Object element | read-only select first; then a disposable upload and a use-in-process run | yes | SF: SysFile sources |
| M21 | A designer SAVE capture of a SysFile-mode Object element (scope, target, sort) | the user builds and saves a disposable process | yes | SF: the SysFile serialization AC |
| M23 (a)-(d) | SysFile WRITE: legacy → SysFile target; SysFile source → ContactFile; Report → SysFile target; Process variant → SysFile target. Check `RecordId`, `RecordSchemaName`, `Type = File`, `Name`, size, `CreatedObjectFileIds`, and with the user the record's Freedom UI attachment list | designer-built probes by the user, runs, selects | yes | SF: SysFile targets |
| M25 | `Features.GetIsEnabled("<code name>")` for `UseSysFileInObjectFileProcessing` reads true on the stand, and the exact code name | a disposable script task or log line | yes | SF: whether the designer notice ships |

Not gating: M19 (a collection as a filter value), M20 (the silent block drop) and M24 (an array sent into an object
member at the WCF binder; OA guide sentence for `attachments.sort`) run on day 0 on 1.6.6.54 (open-questions B.0).
M9 (SysFile sort on a record-object column throws; SF), M16 (use-in-process report files: alive while parked, gone at
completion, dangling when returned from a sub-process; RP notice wording) and M18 (constant `fileNameSuffix` under a
non-default culture; RP) run when convenient. M26 (after a save, `ObjectFiles` holds the source locators; traps
T-19) runs before the OA.12 sentence and its record are written.

### 6.3 After each cut (before the package PR leaves draft)

Common protocol: build the clio PR and run `install-process-builder` from the refreshed build output (an install reads
the archive from the build output, so `clio compress` into the repo path proves nothing until clio is rebuilt);
`list-packages` shows the cut; one behaviour probe only the new code can answer; `get-tool-contract` shows the new
vocabulary; the PR's e2e and the must-stay-green fixtures run sequentially and the new fixtures show **Passed with
Ignored = 0**; runtime proofs below, read from `SysProcessElementLog`; the user's designer checks (M22). Fixtures,
build descriptors and exact pass conditions: [test-plan](eng-92719-file-processing-element-test-plan.md) §6 (ST
scenarios), §7 (DT checks) and §9 (evidence queries and cleanup).

| Cut | Runtime proof | Test plan | Pass condition | If it fails |
|---|---|---|---|---|
| OA | Object SaveToFiles in dedicated storage: an Account with 2 attachments copied to a Contact | ST-01 | 2 `ContactFile` rows with `Type = File`, linked to the target; the source rows unchanged; `CreatedObjectFileIds` has 2 rows | do not merge |
| OA | Object use-in-process into a multi-instance sub-process per file (needs PT merged) | ST-02 | iterations = attachments (2, not 1: P1 bound the parent) | do not merge |
| OA | Empty-filter save refused at the end of the request | e2e (OA.11) | refusal with `failedOperationIndex: null`, naming the element | do not merge |
| OA | A designer-built SysFile element is described and its `setElement` refused; a modify of the same designer-built process leaves its Report element intact | e2e (OA.11, TC-46) on `UsrFpOaDesignerFixture` | block decoded, issues listed, refusal names SF, F1 unchanged on re-read; F2's `SysSchema.MetaData` node and resource rows byte-identical before and after the unrelated `addParameter`; not Ignored | do not merge |
| OA | M22: the user opens a builder-made dedicated-storage Object element and does a no-op save; the user builds the same element by hand | DT-01, DT-02 | metadata equal before and after; no `SourceDataEntitySchemaUId = AccountFile` afterwards, or the guide warning is present | per D15 |
| RP | Builder-made M4 chain: Object use-in-process → FileCollection → Process variant SaveToFiles | ST-04 | 2 copies; describe shows both binding levels (M4 measures what the Process variant's `ObjectFiles` holds) | do not merge, or cherry-pick the Report half (X2) |
| RP | Single File → Process variant (only if M1 passed) | ST-03 | 1 copy per iteration | withdraw the single-file path |
| RP | One output, two consumers (the FileParameterProcess shape) | ST-05 | copies on both targets; the source rows unchanged | do not merge |
| RP | Word SaveToFiles on an Invoice with `report.recordId`, re-found by Modify data | ST-06 | 1 `.docx` on the record, named `Invoice. <Number>.docx` | do not merge |
| RP | Report use-in-process into the Process variant, parked, then completed | ST-07 | temporary `SysProcessFile` rows present while parked, gone at completion (M16) | reword the notice |
| RP | The user opens the RP elements in the designer; a no-op save | DT-03 | the right source variant shown, the Word checkbox forced on, metadata unchanged | do not merge |
| RP | Read-only describe of the four product processes | e2e (RP.7) | `buildType: fileprocessing`, block decoded, raw `parameters[]` lossless | do not merge |
| SF | Builder-made versions of M23 (a)-(d); an Object element reading a Freedom UI upload on a SysFile entity (M8 shape); the user compares with M21 | ST-08, DT-04 | rows carry `RecordId`, `RecordSchemaName`, `Type = File`; the user sees them in the Freedom UI attachment list; equal to M21 | keep the refusal for the failing case and narrow SF |

---

## 7. Knowledge records owed

Records go in **the same PR** that introduces what they describe, and only for what the code does not say
(`clio:docs/knowledge/README.md`). Run `make check-knowledge` on each clio PR. Facts still basis=source are written
only after the named measurement, and say "measured <date>".

| Repository | Record (proposed path) | The fact | PR | Condition |
|---|---|---|---|---|
| clio | `docs/knowledge/platform/the-process-file-source-is-three-user-task-schemas.md` | "What is the source of the file?" is not a parameter; the palette offers only the Object schema; a source change replaces the element; the selector is hidden behind `EnableReportFileProcessingUserTask`, which the server never reads | CL-OA | - |
| clio | `docs/knowledge/platform/an-unset-result-action-type-runs-as-save-to-files.md` | No metadata default; the designer defaults Object/Report to 1; a generic element therefore saves | CL-OA | - |
| clio | `docs/knowledge/platform/object-file-processing-filters-on-the-file-object.md` | The runtime root is `SourceEntitySchemaUId`, the stored `rootSchemaName` is ignored, reads always add `Type = File`; a record-object filter reads zero files with success; an empty filter reads `RecordsToRead` arbitrary files | CL-OA | - |
| clio | `docs/knowledge/platform/after-saving-object-files-points-at-the-source-files.md` | After SaveToFiles the Object variant's `ObjectFiles` holds the SOURCE locators; the copies are only in `CreatedObjectFileIds` | CL-OA | after M26 (traps T-19) |
| clio | `docs/knowledge/platform/attachment-storage-is-chosen-per-entity-by-the-designer.md` | The `<X>File` / SysFile rule, the flag's code default, the server never reading it, 0 of 30 shipped SysFile elements | CL-OA; updated by CL-SF | - |
| clio | `docs/knowledge/platform/a-file-copy-without-a-link-column-lands-on-the-source-record.md` | `CopyAttributes` copies only what the target has not set (`CORE/Terrasoft.File/File.cs:174-180`): a same-schema copy with no link column duplicates onto the source record | CL-OA | - |
| clio | `docs/knowledge/platform/a-server-built-user-task-takes-the-existing-process-default.md` | `DefValueForExistingProcess` makes a server-created `ConsiderTimeInFilter` "false"; server-built nested items keep the template's UIds | CL-OA | after M14 |
| clio | `docs/knowledge/platform/designer-stamps-modified-in-schema-only-on-a-changed-value.md` | an untouched template default keeps the template's GS5, so "set by the designer" means GS5 = process; a cleared value keeps the process stamp without a source (serialization-capture §14) | CL-OA | - |
| clio | `docs/knowledge/platform/a-designer-resave-of-a-dedicated-object-attachments-element-throws.md` | H-G3-1 | CL-OA | only if M11 confirms it |
| clio | UPDATE `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md` | it owns which `Described*` types carry a bag; `IProcessDescriber.cs` is in its `applies-to` | CL-OA | - |
| clio | `docs/knowledge/ProcessModel/report-and-parameter-file-elements-stay-generic-until-eng-96506.md` | temporary decision (per-cut scoping, D13) | CL-OA; **deleted** by CL-RP | - |
| clio | `docs/knowledge/ProcessModel/process-file-sysfile-storage-is-refused-until-sf.md` | temporary decision (O1) | CL-OA; **deleted** by CL-SF | - |
| clio | `docs/knowledge/platform/use-in-process-report-files-are-deleted-with-their-process.md` | temporary `SysProcessFile` rows, lifetime per process instance | CL-RP | after M16 |
| clio | `docs/knowledge/platform/a-process-file-report-runs-only-word-and-fastreport.md` | dispatch by `Type.Name`; DevExpress throws; Word always one file per record; PDF only with a converter package; the file-name rule | CL-RP | - |
| clio | `docs/knowledge/platform/the-process-parameter-file-source-only-saves.md` | `NotSupportedException` for use-in-process; its output item is `ObjectFile` while its input key is `File` | CL-RP | - |
| clio | `docs/knowledge/platform/a-nested-only-files-binding-reads-one-row.md` | the single-file path (`ProcessInstanceParametersDataReader.cs:444-453`) | CL-RP | after M1 |
| clio | `docs/knowledge/infra/the-dev-stand-has-only-five-word-printables.md` | 5 MS Word printables, none for Account/Contact, `ConvertInPDF = false`, no FastReport — why the report e2e skips on 0 rows | CL-RP | - |
| clio | `docs/knowledge/platform/a-sysfile-attachment-read-needs-recordschemaname-and-type-file.md` and the write-path record M23 produces | SysFile read/write facts | CL-SF | after M8 / M23 |
| crt-process-builder | `.codex/workspace-diary.md` entry per PR (append-only, the package's CLAUDE.md rule); `docs/process-file-element-capture.md`; `docs/process-builder-architecture.md` rows | - | PK-OA, PK-RP, PK-SF | - |

clio-knowledge has no internal knowledge base; its deliverable is the shipped guidance itself.

---

## 8. Estimate

**Model (the owner's rule):** an AI coding agent writes all code, tests, docs and guidance, and fixes review findings.
The human time below is the owner's: decisions and Jira, designer-built probes and stand checks, steering the agent
and reading its output, merges and publication checks. Other reviewers' effort is not counted; their latency is in the
calendar. The per-package hours in section 5 are relative size weights from the earlier human-scale costing, not
effort; this section replaces them.

Expected change sizes (inference from the change lists, added lines incl. tests): PK-OA about 6-6.8k, PK-RP 2.5-4k,
PK-SF 1.2-2k; clio adds a median 0.2x of the package lines (measured, 12 features), knowledge less.

### 8.1 AI agent time (wall clock of agent sessions, incl. builds, test runs and review-fix rounds)

| Issue | Added lines, all repos | h |
|---|---|---|
| Shared day 0: CL-DOC (BMAD set, story rows) | docs | 1-2 |
| ENG-96505 Element readiness and object attachments mode (OA.1-OA.12) | about 7.2-8.2k | 15-27 |
| ENG-96506 Generated report + process parameter modes (RP.1-RP.8) | about 3-4.8k | 7-15 |
| SF, "SysFile attachment storage in the Process file element" (SF.1-SF.4) | about 1.4-2.4k | 3-8 |
| **Total** | | **26-52** |

Rate used: 2-3.5 h per 1k added lines, the measured range of the owner's AI-written features of 3-10k lines
(section 8.4).

### 8.2 Human time (the owner)

| Issue | Decisions and Jira | Designer probes and stand checks | Steering and reading the agent's output | Merges, publication, review replies | Total h |
|---|---|---|---|---|---|
| Shared day 0 | 1.5-2.5 (Q3-Q5, Q13-Q18, AC D-2..D-4, links, Sub-tasks) | - | 0.5-1 (CL-DOC review) | - | 2-3.5 |
| ENG-96505 Element readiness and object attachments mode | - | 1.5-2.5 (M26 probe, M22 after the cut, DT-01/DT-02; M10/M11 are read by the agent in a logged-in browser) | 3.5-9.5 | 0.5-1 | 5.5-13 |
| ENG-96506 Generated report + process parameter modes | - | 1.5-2.5 (the M1+M2 script-task probe, M22, DT-03) | 1.5-5.5 | 0.5-1 | 3.5-9 |
| SF | - | 3-4 (M8 upload and probe, SC-1/M21, M23a-d, M25, DT-04) | 0.5-3 | 0.5-1 | 4-8 |
| **Total** | | | | | **15-33.5** |

Steering is costed at 0.5-1.2 h per 1k added lines (measured median 0.7, section 8.4). The optional measurements that
gate no code (M4, M9, M16, the M17 runs, M18, M19, M20, M24) add about 2-3 h of human time if they are all run.

### 8.3 Calendar

| Step | Working days | Basis |
|---|---|---|
| Day 0: decisions, day-0 builder probes on 1.6.6.54 (M13, M6, M3, M3b, M14, M19, M20, M24, one at a time), CL-PT-DOC and CL-DOC | about 1 | inference; decision turnaround dominates |
| ENG-95984 File process parameter type to its knowledge merge | 2-4 after day 0 | its plan section 8 |
| ENG-96505 Element readiness and object attachments mode: development overlaps the previous review; its own review of a 4.5k+ line package PR | +2-3 | median 23.5 h review start to merge, 1.58 approval dismissals per PR (measured) |
| ENG-96506 Generated report + process parameter modes | +1.5-2 | 1.5-4.5k bucket median 18.2 h (measured) |
| SF | +1.5-2.5 | the M8/M23 gates may extend it |
| **ENG-92719 File processing element** | **about 7-10 after ENG-95984 File process parameter type merges** | |
| **Both issues** | **about 2-3 weeks from day 0** | |

The clio and knowledge tails take 30-71 minutes after each package merge (measured). At most one of our package PRs is
in review at a time, and the stand carries one cut at a time, so these steps add up rather than overlap.

### 8.4 Calibration (measured 2026-10-01)

Twelve features delivered in these three repositories between 2026-08-10 and 2026-09-30. For seven of them, written
by the owner with AI agents, the agent wall clock was read from the session transcripts:

| Feature | Added lines (prod + test, all repos) | Agent wall h | h per 1k lines | Owner engaged h | Calendar days |
|---|---|---|---|---|---|
| ENG-92707 Sub-process element: selection + parameter sync | 7.9k | 22.6 | 2.9 | 5.3 | 5.1 |
| ENG-99856 Sub-process element: support MULTI-INSTANCE (running the callee once per item of a collection) | 9.9k | 15.6 | 1.6 | 3.0 | 2.0 |
| ENG-92711 Script task element | 10.4k | 24.4 | 2.3 | 3.4 | 5.0 |
| ENG-91844 Implement full parameter mapping (sources) | 3.3k | 9.1 | 2.8 | 0.8 | 5.6 |
| ENG-91853 Exclusive and parallel gateways, conditional/default flows + basic Y auto-layout | 18.8k | 134.1 | 7.1 | 55.9 | 11.2 |

- Agent time over all seven: median 2.9 h per 1k lines (p25-p75 2.6-5.8). The upper tail comes from small fixes and from
  ENG-91853 Exclusive and parallel gateways, conditional/default flows + basic Y auto-layout, which also carried its
  research and an autolayout redesign; this work's research is already done, so the 3-10k feature range (1.6-2.9) is
  used, widened to 2-3.5.
- Owner engaged time (prompting plus reading the reply, capped at 10 minutes per prompt): median 0.7 h per 1k lines
  (p25-p75 0.3-1.9). It does not include designer probes, which section 8.2 adds separately.
- About 40% of agent time falls after review starts (review-fix rounds), and it is inside the agent figures above.
- Review start to merge, package PRs: 18.2 h median for 1,500-4,500 added lines (range 5.1-136.4), 23.5 h for 4,500+;
  the first human review arrives after a median of about 33 h.

What pushes it up: a re-cut forced by a foreign restamp on `main` after approvals (each costs a cut, a reinstall and
re-requested reviews); M1 or M23 failing (the single-file path or SysFile narrows, X2 may fire); M11 confirming
H-G3-1 (a guide warning, a describe issue and a bug-report Sub-task); the strict descriptor keys of ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph validator into the write path (ENG-88414) (ENG-88414 is AI-driven application development) landing first (its
allow-list must learn every new write member). What pulls it down: a second disposable .NET Framework stand (O6),
which removes the one-cut-at-a-time wait between PT, OA and RP verification.

---

## 9. Out of scope — stated explicitly

| Item | Why / where it goes |
|---|---|
| Creating a printable | AC-FE3 / AC-RP3; the refusal tells the user to create it in System Designer → Report setup |
| FastReport and DevExpress reports | FastReport refused in this ticket (no assemblies or printables on the stand; its process path is unverified), DevExpress throws at run time; both refusals are additive to lift later (D17) |
| Process file → Send email attachments | ENG-95985 Send email attachments (D22) |
| Process file → Creatio.ai call | not buildable through this tool; ENG-92725 Execute AI Intent element (BP generation) is Won't Do (D22) |
| `linkColumn: "none"`, an explicit `storage: "sysFile"` override | v1 refuses both; revisit after M12 (D15) |
| "Attachments of the records matching a condition" in SysFile mode | needs an unmeasured reverse join; in dedicated storage it already works through the link lookup (D16) |
| Multi-column sort | the readData shape takes one entry; a stored multi-entry `OrderByInfo` is kept verbatim while `sort` is not sent (D16) |
| A new report-lookup MCP tool | the compact tool index has about 25 bytes left; `list-printables` is the discovery route (D17) |
| Reproducing the designer's flag-OFF object list | the runtime never reads the flag (D15 option B rejected) |
| Fixing H-G3-1 in CrtProcessDesigner | not these repositories; reported through a Sub-task if M11 confirms it |
| SerializeToDB on the generic route; collection parameters as filter values | side Sub-tasks (section 5.4) |
| Decoding process-parameter sources into names; a declared item shape for collections; a mirror type allow-list | ENG-95984 File process parameter type follow-ups (its plan) |
| creatio-ui | no file-processing logic there; the diagram needs only what the handler writes (D20) |
| ClioRing | consumes only catalog names, Purpose text and Destructive flags, and dispatches non-destructive tools generically (`clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95`, `clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157`); names and flags stay unchanged, and the Purpose leads are confirmed unchanged when the descriptions are re-measured |

---

## 10. Corrections found while writing this plan

| # | Finding | Basis | Action |
|---|---|---|---|
| C-1 | The `list-user-tasks` description tells agents to "fall back to a generic userTask named after the schema" when an environment rejects a dedicated type (`clio:clio/Command/McpServer/Tools/ListUserTasksTool.cs:58-60`). For the Process file element that fallback builds an unconfigured element that saves green and fails at run time, and from OA the server refuses it. The same description says "Three exceptions" and then lists four (sendEmail, approval, addData, changeAccessRights; `:48-57`) — the counted-claim drift `clio:docs/knowledge/McpServer/counted-claims-in-shipped-text-have-no-drift-oracle.md` describes. The decisions and the PR split do not cover this description | source, read 2026-10-01 | OA.10 adds the Object schema to the exceptions with "no generic fallback" and drops the count word; RP.6 adds the other two schemas; MCP review lists `list-user-tasks` |
| C-2 | AC-OA8 "rarely an endpoint" contradicts the shipped product corpus (N4) | corpus measured | new guidance wording in OA.12; an AC edit proposed to the owner |
| C-3 | D22 assigns "Process file → Modify data (re-find by filter)" and "Add data creates the report's data row" to guidance only, while the proposed AC-FE6 text (decisions D-2) says "Tests cover ... Process file -> Modify data (re-finding the created file) ... and Add data -> generated report", use-cases §5 rows P3 and P4 say "**add** an e2e", and the test plan carries them as TC-71 (CL-OA) and TC-74 (CL-RP) | source (decisions, use-cases, test-plan) | **the tests are kept**, matching D-2: TC-71 is in OA.11 and TC-74 in RP.7 (0.5 h each). Both are build-and-describe e2e cases, no run. D22's "guidance only" for these two rows now means "guidance plus the e2e"; the guidance sections stay as planned (OA.12, RP.8) |
| C-4 | With O1 the Story is done only when SF is done, not when its two original sub-tasks are | inference | the D24/O1 AC edits move the SysFile rows to SF; ENG-92719 File processing element closes after KB-SF merges |
| C-5 | Whether a new Sub-task "Process file to Send email attachments" is created: D27's last clause and the last row of pr-split §12.3 ("day 0", parent ENG-95985 Send email attachments) say yes; this plan's §5.4 and D22 ("exists in the backlog with exactly this scope") say no, and the two creation lists, decisions D-5 and open-questions Q9, omit it | source (the four documents, re-read 2026-10-01) | **no new Sub-task**: ENG-95985 Send email attachments already owns the scope; only link L5 is made; D27 and pr-split §12.3 dropped the row in the reconciliation of 2026-10-01 |
| C-6 | reuse RF1-RF3 correct D16 and D21, and each changes a work package: RF2, the sort methods are `private static` with Read data wording (`PB/Elements/ReadDataConfigBinder.cs:793`, `:917`), so "reused as it is" is impossible; RF1, identity resolves by schema NAME (`PB/Elements/UserTaskSchemaIdentity.cs:48-56`), so production code mirrors names, not UIds; RF3, the notice ledger never refuses (`PB/Elements/DeleteDataNoticeLedger.cs:46`), so a refusing reconcile at `PB/Design/ProcessEditPipeline.cs:135` and `PB/Design/ProcessBuildHandler.cs:547` is new | source, re-read 2026-10-01 | applied here: §2 Sort row, §3 D16/D21 rows, OA.1 (RF1), OA.4 (RF2 extraction and RF3 refusing reconcile, +0.5 h), OA.7 (codec tests and the unedited Read data suites, +0.5 h). D16, D11 and D21 now read "the extracted readData sort codec" / "Reuse + change"; the README "Resolved during review" lists RF1-RF3 |
| C-7 | The sibling [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) misses two mandates that this plan carries: PB-10 has no `.codex/workspace-diary.md` entry, which the package's `CLAUDE.md` "Workspace diary" section makes mandatory after non-trivial work, and its DoD names review gates 1 and 3 but not gate 2 (per commit, with the tier stated) | source (both plans, `pkg:CLAUDE.md`) | applied in that plan: PB-10 and its DoD carry the diary entry, and its DoD carries gate 2 per commit with the tier stated |
| C-8 | The estimate was first costed at human scale (99-122 h; 12.5-15 effort days; 3-4 weeks) | the owner, 2026-10-01: estimate on the assumption that an AI agent writes the code | section 8 is re-done on that model and calibrated on 12 delivered features (8.4); the README estimate table follows it; the section 5 hours are kept only as relative size weights |

---

## 11. Definition of Done

### ENG-96505 Element readiness and object attachments mode

**Package**
- [ ] `fileProcessing` / `processFile` build the same element; the variant registry holds the Object variant; the
      handler is registered before `UserTaskElementHandler` and the tripwire pins set and order.
- [ ] D10-D14, D16, D19, D20 and D15 (dedicated storage) implemented; SysFile sources and targets refused with the
      D15 text; designer-built SysFile elements described without loss.
- [ ] Generic route and raw configuration mappings refused for the Object schema only; Report and Process elements
      untouched and still generic.
- [ ] Package tests green on `-c dev-nf` (from `C:/Projects/workspace/<short>`) and `-c dev-n8`; every new fixture
      `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` with AAA, `because:` and `[Description]`; the
      sort codec extracted from `ReadDataConfigBinder` with `ReadDataConfigBinderTests` and `ReadDataConfigApplierTests`
      green and unedited (the Read data messages byte-identical).
- [ ] `docs/process-file-element-capture.md` matches SkillFilesValidationProcess (under the TC-41 rule of
      serialization-capture section 10: UIds ignored; GS5 normalised for template defaults; N2, N9, N10, N13
      tolerated; N11, N14-N16 normalised; anything else fails); architecture rows and the diary entry added; restamped; tag pushed and verified on the remote.

**clio**
- [ ] Rebundled to the final cut; four pins; floors on create / modify / modify-as-new-version equal the final cut,
      with every file in [pr-split](eng-92719-file-processing-element-pr-split.md) §9.2.
- [ ] Descriptions re-measured and the budget-test comment rewritten; `curated-knowledge-names.json` re-pinned to the
      **published** KB-PT generation in the same commit as the first `name=process-files`.
- [ ] `list-user-tasks` description corrected (C-1); `DescribedFileProcessing` with a bag on every nested type; the
      capability probe; the knowledge records in section 7.
- [ ] `///` summaries on `DescribedFileProcessing`, every nested type and every public member; no new `CLIO*`
      analyzer warning in any touched file (AGENTS.md inline-documentation and analyzer policies).
- [ ] Unit: `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null` green (four modules
      touched, so the full unit suite); the filter is quoted in the PR description as `Validated:`.
- [ ] e2e `ProcessFileElementToolE2ETests` run against the stand carrying the cut: **Passed, Ignored = 0**, recorded
      per fixture in the PR description, plus the must-stay-green fixtures.
- [ ] PR description states: "MCP reviewed (tools create-business-process, modify-business-process,
      modify-process-as-new-version, describe-business-process, list-user-tasks, validate-process-graph; prompts
      ListUserTasksPrompt, CreateBusinessProcessPrompt, ModifyBusinessProcessPrompt, DescribeProcessPrompt; resources
      and clio/tpl: no update required)"; "docs reviewed, no update required" for `help/en`, `docs/commands`,
      `Commands.md`, `WikiAnchors.txt` (MCP-only tools); "ClioRing compatibility reviewed, no Ring-consumed contract
      changed", citing `clio-ring/ClioRing.Desktop/actions.json`,
      `clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157` and `clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95`;
      names, Destructive flags and Purpose leads unchanged; the re-measured budget figures; no telemetry vocabulary
      change.
- [ ] The four prompt sentences changed and pinned (TC-90); the `list-user-tasks` description pinned (TC-89).

**clio-knowledge**
- [ ] `process-files` sections and `element-catalog.md` for this cut; pin and size tests green; libraryVersion and
      sequence above master at merge; the refute-first fact check of the guide text by independent reviewers that
      may not cite our own records (pr-split §4.2); **published**, and `info-knowledge` shows it.

**Process**
- [ ] Pre-code measurements M10, M11(a)(b), M17-Q7 and M14 (on day 0, on 1.6.6.54) recorded with dates; M26
      recorded before the OA.12 sentence and its record are written; the fixture `UsrFpOaDesignerFixture` built by the
      user (OA.11); section 6.3 OA proofs passed.
- [ ] Gate 1 (comprehensive) before the PR, gate 2 per commit with the tier stated, gate 3 (comprehensive) before
      ready; no open Blocker/High.
- [ ] `spec/sprint-status.yaml` row `story-eng-92719-file-processing-element-1` and its story file: `in-progress` when
      the draft opens, `review` when it leaves draft, `done` riding in the next clio PR of the chain (never pushed after
      an approval; pr-split §5.6); the story's Definition of Done checklist fully ticked.
- [ ] Every [test-plan](eng-92719-file-processing-element-test-plan.md) TC row whose PR column names PK-OA or CL-OA
      implemented; ST-01, ST-02, DT-01, DT-02 passed; the AC-to-test traceability rows (test-plan §11) for OA filled.
- [ ] PR titles exactly `ENG-96505 Element readiness and object attachments mode` in all three repositories; merged
      by a human in the order package → clio → knowledge.

### ENG-96506 Generated report + process parameter modes

- [ ] Report and Process variants registered (one commit group each); claims, refusals, filter target and describe for
      both; MS Word only; the single-file path present only if M1 passed.
- [ ] Package tests green on both configurations; capture sections match PrintInvoiceReport and FileParameterProcess
      under the TC-41 rule (TC-59, TC-69).
- [ ] clio: rebundle; floors to the final cut (byte-neutral clause); `ListPrintablesEnvelope`; DTO members with bags
      and `///` summaries on every new type and public member, no new `CLIO*` analyzer warning in any touched file;
      `list-user-tasks` exceptions extended; `ListUserTasksPrompt` and `CreateBusinessProcessPrompt` extended (TC-90);
      TC-91 (E2E-14a-d) Passed; the temporary "stay generic" knowledge record deleted; e2e Passed with
      Ignored = 0 (or `Assert.Ignore` only for the documented 0-printables case); the read-only describe of the four
      product processes recorded.
- [ ] Same PR-description statements, review gates, knowledge publication, `story-eng-92719-file-processing-element-2`
      status flips, and exact titles `ENG-96506 Generated report + process parameter modes`.
- [ ] Section 6.3 RP proofs passed (ST-03 only if M1 passed; ST-04 to ST-07; DT-03), or contingency X2 executed and
      the owner informed; every test-plan TC row naming PK-RP or CL-RP implemented.

### SysFile attachment storage in the Process file element (SF)

- [ ] M7, M8, M21, M23(a)-(d), M25 recorded before code; SysFile lifted only for the cases that passed.
- [ ] Package tests green; capture doc checked against M21.
- [ ] clio: rebundle with **no floor raise**; `ProcessFileSysFileStorageToolE2ETests` Passed with Ignored = 0; OA's
      refusal e2e re-pointed; the temporary refusal record deleted, the storage record updated.
- [ ] ST-08 and DT-04 passed; the user confirmed each written file in the record's Freedom UI attachment list; every
      test-plan TC row naming PK-SF or CL-SF implemented.
- [ ] Same statements, gates, publication, `story-eng-92719-file-processing-element-3` flips; titles
      `<SF-KEY> SysFile attachment storage in the Process file element`.

### ENG-92719 File processing element (the Story)

- [ ] CL-DOC merged before the first FE code PR opened.
- [ ] ENG-96505 Element readiness and object attachments mode, ENG-96506 Generated report + process parameter modes and SF done; the D24 / O1 AC edits applied in Jira; the D23 re-link done.
- [ ] Each AC-FE row traceable to a test or a guide section: AC-FE1/FE2 (OA and RP e2e), AC-FE3 (`process-files`,
      the D17 refusal), AC-FE4 (PT plus the value-source e2e), AC-FE5 (the three captures plus M21), AC-FE6 (the
      pattern map in D22 with C-3 applied: Read data → Process file (TC-70), Modify data re-find (TC-71) and
      multi-instance per file (TC-72) in OA; Object → Process chain (TC-73) and Add data → report (TC-74) in RP;
      Send email in ENG-95985 Send email attachments; Creatio.ai stated as not buildable).
