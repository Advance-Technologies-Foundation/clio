# ENG-92719 File processing element: test plan

**Summary.** This plan says how the Process file element is tested in all three repositories, and what only a
running process on the stand can prove. In scope: ENG-92719 File processing element (Story), its sub-tasks
ENG-96505 Element readiness and object attachments mode and ENG-96506 Generated report + process parameter modes,
and the proposed Sub-task "SysFile attachment storage in the Process file element". Package unit tests must not name
the three user-task classes, because the dev-n8 build compiles without `Terrasoft.Configuration.dll`. They also cannot
run the platform's parameter sync or its runtime reader. So the package suite builds every element by hand: the real schema
UIds, the real parameter names, UIds and types, and the direction the metadata really declares (Variable unless
`L12` is set). One helper, `FileProcessingTestSupport`, builds these elements, and a drift guard checks the helper
against the shipped CrtProcessDesigner 7.8.0 metadata. The platform's own tests in `C:/Projects/UnitTests` are used
to understand the runtime, not ported. They show which runtime facts are pinned by a platform test, and the one fact
that is not: how the runtime builds the `Files` rows from a mapping. That fact, and everything about storage and
reports at run time, is proven on the stand by the scenarios in section 6, with the read-only evidence recipe in
section 9. The test cases are TC-01..TC-91 in section 5, each mapped to an acceptance criterion (the replacement text
proposed in [decisions](eng-92719-file-processing-element-decisions.md) Part D) and a decision. This document
also holds the methods for the pre-code stand measurements M1..M26 (section 8).

Sibling documents: [README](README.md) ·
[platform-reference](eng-92719-file-processing-element-platform-reference.md) ·
[serialization-capture](eng-92719-file-processing-element-serialization-capture.md) ·
[use-cases](eng-92719-file-processing-element-use-cases.md) · [traps](eng-92719-file-processing-element-traps.md) ·
[reuse](eng-92719-file-processing-element-reuse.md) · [decisions](eng-92719-file-processing-element-decisions.md) ·
[plan](eng-92719-file-processing-element-plan.md) · [pr-split](eng-92719-file-processing-element-pr-split.md) ·
[open-questions](eng-92719-file-processing-element-open-questions.md) ·
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) ·
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md).
Decision ids (D1-D29), measurement ids (M1-M26) and PR ids (PK-/CL-/KB-) are the ones defined in
[decisions](eng-92719-file-processing-element-decisions.md) and [pr-split](eng-92719-file-processing-element-pr-split.md).

Written 2026-10-01, read-only: nothing was built, run, committed, or written to Jira or to the stand.

| Alias | Jira issue |
|---|---|
| **FE** | ENG-92719 File processing element (Story) |
| **OA** | ENG-96505 Element readiness and object attachments mode (Sub-task of FE) |
| **RP** | ENG-96506 Generated report + process parameter modes (Sub-task of FE) |
| **SF** | NEW Sub-task of FE, "SysFile attachment storage in the Process file element" (key assigned on creation) |
| **PT** | ENG-95984 File process parameter type (Task; its cases are in its own [test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md)) |
| **SE** | ENG-95985 Send email attachments (owns Process file -> Send email) |

| Path alias | Root |
|---|---|
| `PB/` | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/` (main `3f4cce50`, 1.6.6.54) |
| `PBT/` | crt-process-builder `tests/UnitTests/CrtProcessBuilder.Tests/` |
| clio paths | repository-relative to clio master `03ef3944f` (`clio/`, `clio.tests/`, `clio.mcp.e2e/`) |
| `KBM/` | clio-knowledge master `d0b5a2b` (libraryVersion 1.15.90) |
| `CORE/` | `C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib` (10.1.37, the stand's core) |
| `PD/` | `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas` (the bytes the stand serves) |
| `UT/` | `C:/Projects/UnitTests/ProcessDesigner.UnitTests` (platform configuration tests) |
| `PS/` | `C:/Projects/PackageStore` (shipped corpus) |

Basis labels: **source** = read in code or metadata (a runtime claim with this label is a hypothesis until
measured); **measured** = observed on the stand or counted over the corpus, with a date.

---

## 1. Where the coverage lives

| Level | Project | What it can prove | What it cannot |
|---|---|---|---|
| **P** package unit | `PBT/` (NUnit 4.4, NSubstitute 5.3, FluentAssertions 7.2) | handler, identity, variant registry, binder, resolver, scope filter, describe block, refusals, notices, the stored metadata shape | the parameter sync on `SchemaUId`, the runtime reader, the platform pre-save validation, the designer |
| **C** clio unit | `clio.tests/` | describe DTOs and their extension bags, "forwards the block verbatim", description budget and wording pins, archive pins, floors, curated guidance names | anything the server does |
| **E** MCP end-to-end | `clio.mcp.e2e/` | the tool contract over the wire against a live stand: JSON member names, build + describe round trips, refusal texts | run-time behaviour (these fixtures build and read; they do not run processes) |
| **K** knowledge | `KBM/automation/Clio.Knowledge.Bundle.Tests` | guide pins, sizes, registration, cross references | whether agents follow the guide |
| **S** stand, runtime | manual, recorded, user go-ahead per run | that files really move: rows written, storage, names, temporary files, iteration counts | (the top level) |
| **D** stand, design time | the user, in the classic designer | the designer opens builder-made elements, and a no-op save changes nothing | run time |

The P/S boundary is fixed. `ProcessSchemaUserTask.SchemaUId`'s setter runs `SynchronizeParameters()`
(`CORE/Terrasoft.Core/Process/ProcessSchemaUserTask.cs:106-115`), and the repository documents that the real sync
stops at the configuration-backed `DesignModeClassResolver` (`PBT/UserTaskElementHandlerCreateTests.cs:118-125`).
`ProcessInstanceParametersDataReader` is internal. No unit test may claim to prove what the runtime does with a
`Files` binding, a storage pair or a report. The stand reports in section 6 must also say which level they reached:
**Stored** (metadata written), **Design time** (the designer opens it) or **Runtime** (the process ran).

## 2. Corrections this plan applies to earlier research

The research behind this plan contained errors that change how tests are written. Each correction below was re-checked
in source or measured.

| # | Earlier claim | Correct fact | Basis |
|---|---|---|---|
| C1 | The user-task parameters without `L12` are direction **In** (the research test-mocking table, section 3.2) | They are **Variable**: `ProcessSchemaParameter.Direction` defaults to Variable (`CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:416`), is read from `L12` (`:868-869`), and is not written when Variable (`:1034`). Only these carry `L12`: `Files` and `Files.File` = In (0) (`PD/ProcessFileProcessingUserTask/metadata.json:108, 119`), and every output collection and its item = Out (1). The helper and the drift guard use this (section 3.2). | source |
| C2 | "On this stand the designer will not produce the old storage; the old path has to be arranged on purpose" | Storage is chosen **per entity**. Account and Contact keep `AccountFile` / `ContactFile` with the feature on (UO-4, measured 2026-10-01); only objects without a `<X>File` go to SysFile (UO-3, measured 2026-10-01, in memory). 0 of 30 shipped elements use SysFile. | measured |
| C3 | Type compatibility for file types is at `ParameterTypeCompatibility.cs:31-36` | Those lines are the class remark; the exact-UId rule is `PB/Mappings/ParameterTypeCompatibility.cs:240-242`. FileLocator -> FileLocator already passes. | source |
| C4 | An outer-only `Files` binding copies zero files silently | The trace predicts `{File: null}` rows and an NRE in `FileFactory.CreateFileInstance` when the source has rows. The builder refuses outer-only anyway (D18, FB-3), so no stand run is spent on it (M5 skipped). | source |
| C5 | The one-row single-file path is `HasCollectionWithOneElement` | It is `TryGetListValue` (`ProcessInstanceParametersDataReader.cs` about 444-453). That flag only feeds the multi-instance iteration count. Whether an ordinary task reads a nested-only `Files` at all is still unmeasured: M1. | source |
| C6 | "Add files" on the Process-parameter page creates a dynamic `COMPOSITE_OBJECT_LIST` input | The page binds the schema's own nested `Files.File`. Dynamic `<Prefix><N>` slots belong to Send email and the Creatio.ai call. No test expects a dynamic slot on this element. | source |
| C7 | The latest SysFile rows on the stand show "Database" content storage | Those rows are configuration logs (`RecordSchemaName = ConfActivityLog`), not attachments. Evidence queries exclude them. | measured 2026-10-01 |

## 3. Harness per repository

### 3.1 CrtProcessBuilder (`PBT/`)

| Constraint | Rule for these tests | Evidence |
|---|---|---|
| Two configurations | `-c dev-nf` (net472) is what CI runs on Windows; `-c dev-n8` (net8.0) is the macOS route. Run the tests with the configuration you built. New tests must compile under both. | `PBM/CLAUDE.md` "Building locally"; `PBT/CrtProcessBuilder.Tests.csproj:3-29` |
| No configuration types | The test project references `Terrasoft.Configuration.dll` unconditionally (`csproj:72-74`). dev-nf makes a missing DLL an error; dev-n8 demotes MSB3245 to a message and builds without it. So a test that names `ObjectFileProcessingUserTask`, `ResultActionTypeEnum` or `FileConsts` breaks the dev-n8 build. Tests know the tasks by UId and name only, and constants are mirrored as literals (TC-03). | `PBM/.build-props/env.dev-n8.props`, `env.dev-nf.props:13-20` |
| C# 7.3 | `LangVersion 7.3` (`csproj:32`): no switch expressions, `is not`, records, target-typed `new`, `using` declarations or nullable annotations. | source |
| Fixture categories | `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` on every fixture. Without a CI category, the fixture is skipped by Jenkins while the stage stays green. `CiContractGuardTests` (`PBT/CiContractGuardTests.cs:56`) fails such a fixture, and `ci.runsettings:15` (`TreatNoTestsAsError`) fails an empty run. | source |
| DB-backed arrange | `[MockSettings(RequireMock.DBEngine)]` on fixtures that read the palette (`SysProcessUserTask`) or run an ESQ (`PBT/UserTaskElementHandlerCreateTests.cs:26-57`). | source |
| Per-test arrange | Arrange task-schema stubs in `[SetUp]`, never lazily. NUnit reuses the fixture instance while `UserConnection` is rebuilt per test. A lazy arrange passed alone and failed in a full run (measured, as recorded at `PBT/SendEmailApplierTests.cs:34-46`). | measured (recorded) |
| Worktree path | net472 tests from a git worktree must run from a short path: `C:/Projects/workspace/<short>`. A longer path exceeds MAX_PATH, and every test fails in `SetUp` with a misleading `FileNotFoundException`. Copy `.application` into the worktree; never junction it, because worktree removal follows junctions and deletes the install. | user rule; `PBM/CLAUDE.md` "Local setup" |
| Commands | `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf` (full suite, before every push); `... --filter "FullyQualifiedName~FileProcessing"` while iterating; `dotnet build MainSolution.slnx -c dev-n8` once per PR when `.application/net-core` exists (proves no configuration type is named). | `PBM/CLAUDE.md` |

**The SchemaUId ordering rule (basis=source).** Assigning `SchemaUId` runs the sync. The base step returns early when
the host process UId is empty (`CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:324-326, 587-590`). The user-task
step then asks the `ProcessUserTaskSchemaManager` for the task schema (`ProcessSchemaUserTask.cs:273-288`). It is
gated by the internal `FeatureProcessParameterCollections`, which is initialised from `GlobalAppSettings`
(`:90-91`), so a package test cannot flip it. Every existing fixture survives this in one arrangement: a host
`new ProcessSchema(UserConnection.ProcessSchemaManager)` with an empty UId, and the task schema as a substitute
(`PBT/DeleteDataConfigBinderTests.cs:37-70`, `PBT/SendEmailApplierTests.cs:76-100`). The helper keeps that order:
(1) build the elements on a host with an empty UId; (2) only then set `host.UId` when a test needs
`ModifiedInSchemaUId = schema.UId`; (3) then run the handler or binder. TC-01 checks that the helper's parameter set
is unchanged after `SchemaUId` was assigned, so a sync that started running would fail the guard, not the
feature tests.

### 3.2 The three user-task schemas the stubs carry

Source: `PD/<Task>/metadata.json`, `FJ1` (CrtProcessDesigner 7.8.0, byte-identical to what the stand serves). Types:
Lookup `B295071F`, MetaDataText `394E160F`, Integer `6B6B74E2`, Guid `23018567`, Boolean `90B65BF8`, Text `8B3F29BB`,
CompositeObjectList `651EC16F`, FileLocator `A33C9252`. "Var" = Variable (no `L12`, C1). Every Lookup references
SysSchema `6c7394db-06ff-4050-91ef-8278e21dce15`. Template values are given as `{GS1 source, GS2 value, GS5, GS8}`.

**ObjectFileProcessingUserTask `9387c794-8d84-5925-ab77-c47e7d876286`** (13 parameters)

| Parameter | UId | Type | Dir | Req | Template value | Item |
|---|---|---|---|---|---|---|
| SourceEntitySchemaUId | 0bca8251-a3e3-7646-d820-34eaa38a419c | Lookup | Var | yes | - | |
| DataSourceFilters | de39b14b-bb98-ad1e-d465-385f79e499ce | MetaDataText | Var | | - | |
| TargetEntitySchemaUId | 272a0430-93be-b8f0-6660-26f28dd038d3 | Lookup | Var | yes | - | |
| RecordsToRead | f7f25988-3214-fa45-f594-d4c5f467a3d5 | Integer | Var | | {1 Const, "50", 9387c794} | |
| OrderByInfo | 64214e9b-933f-f7d6-bee2-50d84b845729 | MetaDataText | Var | | - | |
| CreatedObjectFileIds | ed25f049-23b1-da21-8f47-901fda242da7 | CompositeObjectList | Out | | - | `Id` 3a3ff2a6-f9d1-4198-84dd-030aeeb0a903 Guid Out |
| ConnectedObjectId | e3ed4c9d-ee0e-1342-d260-2850fe56bc20 | Guid | Var | | - | |
| ConnectedObjectColumnUId | 16365f85-76a8-5f34-526d-02b187987a2e | Guid | Var | | - | |
| ResultActionType | 819a8b59-b345-7b36-a2d8-8bd95b2c03b6 | Integer | Var | yes | - (unset runs as 0) | |
| ObjectFiles | b80cbf95-72c5-d9ab-58cb-5a49d9e6bd23 | CompositeObjectList | Out | | - | `File` 1ddb6de7-5ed0-4fb1-984f-941038d9274e FileLocator Out |
| ConsiderTimeInFilter | 0660022b-b5ae-d098-8fd2-8bd2a5f32a67 | Boolean | Var | | {3 Script, "true", 9387c794, "false"} | |
| SourceDataEntitySchemaUId | 89f5bbf1-1a6a-22be-ee2c-3aec92cf5802 | Lookup | Var | | - | |
| TargetDataEntitySchemaUId | 1259ceb2-8fd6-0843-04e1-ec8aeee47e0b | Lookup | Var | | - | |

**ProcessFileProcessingUserTask `6c620dd2-026e-560c-489f-030c5be5f2c3`** (8 parameters)

| Parameter | UId | Type | Dir | Req | Item |
|---|---|---|---|---|---|
| ObjectFiles | 8adc2a26-256b-c933-bd58-c21de291a62c | CompositeObjectList | Out | | `ObjectFile` 2d5e0436-1617-46b2-bead-523247269e8b FileLocator Out |
| ResultActionType | 6c3b7ae8-e3d5-412c-ee1b-308bcedae842 | Integer | Var | yes | |
| ConnectedObjectColumnUId | 3e5bc847-8a30-9aa3-1004-daae47c7f2ab | Guid | Var | | |
| ConnectedObjectId | 43c60e4a-2ebe-f829-c824-1cb974ef14bd | Guid | Var | | |
| TargetEntitySchemaUId | 0b4a2b0a-ce56-6bf0-d280-9bc2c78bf7b6 | Lookup | Var | yes | |
| CreatedObjectFileIds | b0cffa17-06a3-864e-d512-940b2e54359f | CompositeObjectList | Out | | `Id` 349634bd-7d9b-49ae-9d78-b39d67903a92 Guid Out |
| **Files** | 17a54879-4fbf-6a72-0e98-5b2e5c165a23 | CompositeObjectList | **In** | **yes (outer only)** | `File` e5903ef0-2223-451f-ba8d-586ad28596c2 FileLocator **In**, no `L6` |
| TargetDataEntitySchemaUId | 6cacb38f-8614-fa7e-dde1-246766c1357f | Lookup | Var | | |

**ReportFileProcessingUserTask `c2bf0416-54c6-6c56-58e0-41162c7795f0`** (13 parameters)

| Parameter | UId | Type | Dir | Req | Template value | Item |
|---|---|---|---|---|---|---|
| DataSourceFilters | 4aff901d-fa78-6b52-0117-5a71be08e927 | MetaDataText | Var | | - | |
| IsSeparateReports | fbdf3f95-2ec5-3f5d-3d82-7f2dc480d004 | Boolean | Var | yes | - | |
| ReportId | edcfbf9e-502c-6560-efb0-214a986def22 | Guid | Var | yes | - | |
| ResultActionType | df14becf-6454-a55a-78ce-bb11f3e30eac | Integer | Var | yes | - | |
| ReportName | a98ea4e2-3b1e-c950-d6fd-2a8ca6cdb4e9 | Text | Var | | - | |
| CreatedObjectFileIds | 74c1b25a-d7c0-fbd2-96c3-5030b5e60854 | CompositeObjectList | Out | | - | `Id` 44af58f6-4acb-4b7e-8d2e-bbf3df3c661b Guid Out |
| ConnectedObjectColumnUId | aa4775cf-516b-97fe-6f83-b436ed6967d3 | Guid | Var | | - | |
| ConnectedObjectId | f556b7e6-8e8b-f341-161b-b1485d72d2fa | Guid | Var | | - | |
| TargetEntitySchemaUId | 543caa3e-7315-f608-356b-5f463581844e | Lookup | Var | **no** | - | |
| ReportFiles | 73fdf30e-bf9f-b6ea-f9ad-027fa10b7078 | CompositeObjectList | Out | | - | `File` efe60cce-51e4-43bb-850d-0500582c691a FileLocator Out |
| ReportNameDataSourceColumnUId | b86c2795-e007-2963-f62e-87e33c8e77b7 | Guid | Var | | - | |
| ConsiderTimeInFilter | a7ad6936-4f28-b7e9-c2dd-ba9ae36b09bc | Boolean | Var | | {3 Script, "true", c2bf0416, "false"} | |
| TargetDataEntitySchemaUId | 9d655eda-1618-4ae9-a24e-a4773404f490 | Lookup | Var | | - | |

No nested item carries a `Tag` (`L17`): the 3 templates and all 62 shipped items checked (measured). These UIds are the
TASK SCHEMA's own. An element in a process gets fresh top-level UIds; the designer gives nested items fresh UIds with
`IL2` = the element UId, while a server-side sync is predicted to keep the template's nested UIds (D20, pending M14).
Tests therefore key on **names**, and use UIds only where the stored shape is the subject.

### 3.3 `FileProcessingTestSupport` (new, `PBT/FileProcessingTestSupport.cs`)

One helper, next to `SubProcessTestSupport`, for the same reason that helper exists: the parameter set is a SET that
must be right in every fixture. Sketch (C# 7.3):

```csharp
internal static class FileProcessingTestSupport {
	internal static readonly Guid ObjectTaskUId = new Guid("9387c794-8d84-5925-ab77-c47e7d876286");
	internal static readonly Guid ProcessTaskUId = new Guid("6c620dd2-026e-560c-489f-030c5be5f2c3");
	internal static readonly Guid ReportTaskUId = new Guid("c2bf0416-54c6-6c56-58e0-41162c7795f0");

	/// <summary>Who produced the synced set: the server's sync (template nested UIds, GS8 applied; pending M14)
	/// or the designer (fresh nested UIds, IL2 = element UId, template values kept).</summary>
	internal enum SyncShape { Server, Designer }

	/// <summary>One row per parameter or item, exactly as section 3.2. The drift guard compares it with the capture.</summary>
	internal static IReadOnlyList<TemplateParameter> Template(Guid taskUId) { /* literal table */ }

	/// <summary>Registers the three task schemas on the substituted ProcessUserTaskSchemaManager with the REAL UIds
	/// and names: FindInstanceByName (case-insensitive predicate), FindInstanceByUId, FindItemByUId (an
	/// ISchemaManagerItem with Name). Each substitute carries the template parameters, so the handler's template copies
	/// (D20) and SerializeToDB have something to read. Call from [SetUp].</summary>
	internal static void RegisterFileTaskSchemas(this TestUserConnection userConnection, bool serializeToDB = true) { }

	/// <summary>Arranges the SysProcessUserTask palette read (curated = one row), as UserTaskElementHandlerCreateTests does.</summary>
	internal static void ArrangePaletteRow(this TestUserConnection userConnection, bool isCurated) { }

	/// <summary>A hand-built element with the synced set, attached to host.FlowElements. Assigns SchemaUId while
	/// host.UId is still empty (section 3.1). DataValueType is assigned as an OBJECT, so a later value assignment
	/// does not re-resolve through the null AppManagerProvider (PBT/SendEmailApplierTests.cs:70-90).</summary>
	internal static ProcessSchemaUserTask AnObjectFileElement(ProcessSchema host, string name,
		SyncShape shape = SyncShape.Server) { }
	internal static ProcessSchemaUserTask AReportFileElement(ProcessSchema host, string name,
		SyncShape shape = SyncShape.Server) { }
	internal static ProcessSchemaUserTask AProcessFileElement(ProcessSchema host, string name,
		SyncShape shape = SyncShape.Server) { }

	/// <summary>Designer-made configurations from the corpus, for describe, repair and parity tests: SkillFilesValidationProcess
	/// Object1, FileCopyProcessPP Object1, PrintInvoiceReport Report1, FileParameterProcess Process2/Process3, and the UO-3/UO-4
	/// in-memory states. Values only; element and nested UIds re-keyed to the element under test.</summary>
	internal static void ApplyCorpusConfiguration(ProcessSchemaUserTask element, CorpusCase corpusCase) { }
}
```

Rules:
- No default type fallback: an unknown type name in the table throws (the `SubProcessTestSupport.AParameterOn`
  rule, `PBT/SubProcessTestSupport.cs:346-380`). `new DataValueTypeManager()` registers FileLocator and
  CompositeObjectList with no database (`CORE/Terrasoft.Core/DataValueTypeManager.cs`).
- Collections are built as in `PBT/MultiInstanceMappingTests.cs:41-55` (outer parameter + `ItemProperties.Add`).
  Dotted addressing (`Files.File`, `ObjectFiles.File`) then resolves through
  `ProcessSchemaElementLocator.DescendItemProperties`.
- Parameter names are literals in the helper's table, never production constants. A renamed constant must fail a
  test (the rule stated at `PBT/DeleteDataConfigBinderTests.cs:44-46`).
- `SyncShape.Server` is the default, because builder-made elements are synced on the server. Its two predicted
  differences from the designer (`ConsiderTimeInFilter = "false"` from `GS8`, and template nested UIds) are pending
  M14. When M14 reports, the helper changes in the same commit as the D20 tests.

### 3.4 Drift guard (`PBT/FileProcessingTemplateDriftTests.cs`)

Without a guard, the helper is a second, unverified copy of the platform contract.
- **Captures.** Copy the three `PD/<Task>/metadata.json` files verbatim into
  `PBT/Fixtures/FileProcessing/<Task>.metadata.json`. A header file `PBT/Fixtures/FileProcessing/PROVENANCE.md`
  records the source path, the CrtProcessDesigner version (7.8.0) and the SHA-256 of each file. Read them by
  ancestor search, as `PBT/PackageDescriptorTests.cs:58-70` reads `descriptor.json`. This needs no csproj change,
  and the depth of `bin/<cfg>/<tfm>` does not matter.
- **Reader.** Walk `MetaData.Schema.FJ1` with the platform's `JsonDataReader`, and deserialise each entry with
  `ProcessSchemaParameter.ReadMetaData` on a `TestProcessSchema` host that uses the connection's
  `DataValueTypeManager`. Use one `Read()` and no `ReadInto()` (the trap recorded at
  `PBT/ProcessParameterServiceItemPropertiesTests.cs:60-85`). The platform reader then supplies the Variable default
  itself (C1), so the guard does not depend on this plan's reading of `L12`. Fallback, if the walk proves fiddly:
  compare the raw short keys (`A2`, `UId`, `L1`, `L12`, `L6`, `L9`, `L8`, `L18`) with an explicit "absent `L12` =
  Variable" rule, and record the reason.
- **Asserts.** The reader's precondition: 13 / 8 / 13 parameters, so the walk cannot pass vacuously. Then:
  `Template(taskUId)` equals the capture with strict ordering, on name, UId, type UId, effective direction,
  IsRequired, ReferenceSchemaUId, template `SourceValue` (`GS1`, `GS2`, `GS5`, `GS8`) and items (name, UId, type,
  direction, Tag null).
- **Refresh.** When the stand's CrtProcessDesigner moves (read with `list-packages`), re-copy the three files from
  the matching PackageStore branch and re-pin PROVENANCE.md in one commit. The guard then shows the delta.

### 3.5 Fakes for the new seams

| Seam (new in PK-OA / PK-RP) | Why it is a seam | Fake | Used by |
|---|---|---|---|
| `IEntitySchemaHierarchyReader` | `SchemaManager.GetAllParents` is non-virtual and cannot be substituted (D15) | a hand-written `FakeEntitySchemaHierarchy` holding rows `(name, uid, parents[], columns{name -> type, reference}, isView, isVirtual, isReplacement)` | TC-15..TC-17, TC-26, TC-76 |
| `IReportTemplateReader` | ESQ on `SysModuleReport` with `UseAdminRights = false` (D17) | substitute returning rows `(Id, Caption, TypeName, EntitySchemaName, ConvertInPDF)`; one fixture tests the real reader with `SetupEsqData` (`PBT/ProcessDesignTestSupport.cs:300-306`) | TC-50..TC-58 |
| Feature seam (one method, `GetIsEnabled(code)`) | `Creatio.FeatureToggling.Features.GetIsEnabled(string)` behind an interface (D15, D17) | substitute; ON gives no notice, OFF gives a notice, and the seam is never a refusal | TC-58, TC-80 |
| Record existence | `ProcessParameterValueValidator.EnsureReferencedRecordExists` through a new internal entry point (D15) | the raw-Select arrange `ProcessMappingServiceTests` already uses (`SetupReferencedRecordRead`, defined at `PBT/ProcessMappingServiceTests.cs:99-108`, first used at `:706`) | TC-27 |
| Entity schemas (AccountFile, ContactFile, SysFile, Account, Contact, Invoice, InvoiceFile) | columns for the filter target and sort resolution | `MockEntitySchemaWithColumns` (`PBT/BaseComposableAppTestFixture.cs:386-394`) plus `new ProcessFilterService(UserConnection)` (`PBT/DataNodeFilterTargetTests.cs:38-84`) | TC-18..TC-24, TC-52 |
| Palette read | `HasDedicatedPaletteElement` reads `SysProcessUserTask` | `ArrangePaletteRow` (the `PBT/UserTaskElementHandlerCreateTests.cs:43-57` arrange) | TC-06 |

### 3.6 clio unit (`clio.tests/`)

- **Fixture shapes.** Describe DTO tests extend `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs`
  (`[Category("Unit")]`, `[Property("Module", "ProcessModel")]`, `:27-30`). Tool tests extend
  `clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs` (`[Property("Module", "McpServer")]`, test-level
  `[Category("Unit")]`; the verbatim-forwarding pattern at `:122, :160, :241`). A NEW command-level fixture derives
  from `BaseCommandTests<TOptions>`: it registers its doubles in `AdditionalRegistrations`, resolves the command with
  `Container.GetRequiredService<TCommand>()`, and calls `ClearReceivedCalls` in teardown (AGENTS.md). The existing
  process command fixtures construct the command over substitutes (`clio.tests/Command/CreateBusinessProcessCommandTests.cs:26-34`).
  They are extended in that shape, not rewritten inside a feature PR.
- **Filters.** Always `TestCategory=`. A `Category=` alias that selects more than 2,000 tests silently runs the whole
  assembly (measured 2026-09-25). Run from bash with `< /dev/null`, because a child `git init` hangs on an inherited
  stdin (measured 2026-09-25). Check the executed count, not the exit code: a filter that selects nothing exits 0.
  - iterating: `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=ProcessModel|Module=McpServer)" --no-build < /dev/null`
  - every rebundle PR (CL-OA, CL-RP, CL-SF: the archive and pins sit in `Module=Common`): `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null`

### 3.7 MCP end-to-end (`clio.mcp.e2e/`)

- **Arrange.** Use `ProcessDesignerE2EArrange.StartAsync(subject, MinimumPackageVersion)`
  (`clio.mcp.e2e/Support/Mcp/ProcessDesignerE2EArrange.cs:44-64`). It ignores the test when no sandbox is configured
  or reachable, or when the stand's CrtProcessBuilder is older than `MinimumPackageVersion` (`:73-93`). That version
  is a gate, so it is the PR's floor (the final cut).
- **Attributes.** `[TestFixture] [AllureNUnit] [AllureFeature(...)] [NonParallelizable]
  [Category(McpE2ECategories.ProcessDesigner)]`, as in `clio.mcp.e2e/RecordColumnSourceToolE2ETests.cs`. The
  category is excluded from TeamCity (`clio.mcp.e2e/Support/Configuration/McpE2ECategories.cs:31-37`), so these
  fixtures run only by hand. That is why the PBT pins stay the automated guard for every refusal text.
- **New fixtures.** `ProcessFileElementToolE2ETests.cs` (CL-OA); `ProcessFileReportAndParameterToolE2ETests.cs` or
  an extension of the former (CL-RP); `ProcessFileSysFileStorageToolE2ETests.cs` (CL-SF).
- **`Assert.Ignore` when a fixture is missing, with an actionable message.**
  - `list-printables` returns 0 MS Word rows: "No MS Word printable on '<env>'. Create one in Report setup to run
    this test." The envelope maps only `success` and `error` today (`clio.mcp.e2e/Support/Results/ListPrintablesEnvelope.cs:11-13`);
    add the `count` and `printables` members the command already returns (`clio/Command/ListPrintablesCommand.cs:51, 60`).
  - A constant record id is needed, and a read-only `execute-esq` top-1 read of the object returns no row.
  - CL-SF: no record of a SysFile-stored object (Account address) exists.
  - Prefer `processParameter` sources, which build without any record. Only the constant-source tests need a record.
- **Naming and residue.** Processes are named `UsrClioBpProcessFileE2e{Guid:N}`. As in every existing
  process-designer fixture, they stay on the stand. Cleanup is a separate batch that needs the user's go-ahead
  (section 9.3).
- **No process runs in a fixture.** Runtime proof is section 6, by hand. An opt-in fixture that does call
  `run-process` (`Destructive = true`, `clio/Command/McpServer/Tools/ProcessDesigner/RunProcessTool.cs:66`) must also
  honour `McpE2E:AllowDestructiveMcpTests` (`clio.mcp.e2e/RunProcessToolE2ETests.cs:39-40`).
- **Run.** One fixture at a time on the .NET Framework stand:
  `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~ProcessFile" < /dev/null`.
  The PR description records Passed / Ignored / Failed per fixture, and the new fixtures must show **Ignored = 0** on
  a stand that carries the cut. Before the run, `get-tool-contract` must show the new vocabulary, because the
  session's MCP clio can be stale. The `clio-mcp-e2e-live-stand` skill prepares the stand.

### 3.8 clio-knowledge (`KBM/`)

Work in a worktree under `.worktrees/<task>/` (`KBM/AGENTS.md`). Run `dotnet test automation/Clio.Knowledge.Bundle.Tests`.
The guidance pins follow `CollectionParameterGuidanceTests`. Also run `ProcessGuideResponseSizeTests`,
`ProcessGuideCrossReferenceTests`, `GuidanceMigrationTests` and `ProcessGuideSet.GoLiveFloor`. After a knowledge
merge, check the library version with `info-knowledge` before any guidance-dependent run: a failed
`update-knowledge` keeps serving the old guidance without saying so.

## 4. Platform mocking recipes, adapted from `C:/Projects/UnitTests`

The platform suite compiles against the whole configuration and subclasses the real user-task classes. In
CrtProcessBuilder those classes are off limits (section 3.1). So recipes R4-R6 are references for the runtime
contract we mirror, and are not copied. R1-R3, R7 and R8 adapt directly. The platform tests do not follow our
conventions (no AAA, no `because`, no `[Description]`); ours must (section 10).

**R1. Connection.** Platform: `SubstituteUtilities.CreateEmptyUserConnection()` (`UT/FileProcessing_Tests.cs:56`).
PBT: the `TestUserConnection` from `BaseComposableAppTestFixture`, built by
`ProcessDesignTestSupport.CreateUserConnection` (`PBT/ProcessDesignTestSupport.cs:219-230`): workspace, DB, current
user, a real `DataValueTypeManager`, a substituted `ProcessSchemaManager`.

**R2. File-object shapes.** Platform: `TestProcessUserTaskUtilities.GetFileEntitySchema` builds `Id, Data, Name,
Size, Version, Type`, and `SetupSysFileEntitySchema` adds `RecordSchemaName`, `RecordId`
(`UT/TestProcessUserTaskUtilities.cs:52-145`). PBT, adapted:

```csharp
// Inside a BaseComposableAppTestFixture subclass: DataValueType is the fixture's nested enum, and lookup
// columns go in the second dictionary (column name -> referenced schema), as RecordColumnSourceTests.cs:65-70 does.
MockEntitySchemaWithColumns("Account", new Dictionary<string, DataValueType> {
	{ "Id", DataValueType.Guid }, { "Name", DataValueType.ShortText } });
MockEntitySchemaWithColumns("AccountFile", new Dictionary<string, DataValueType> {
	{ "Id", DataValueType.Guid }, { "Name", DataValueType.ShortText }, { "CreatedOn", DataValueType.DateTime } },
	new Dictionary<string, string> { { "Account", "Account" }, { "Type", "FileType" } });
// SysFile for SF: RecordId (Guid), RecordSchemaName (ShortText on the stand, measured 2026-10-01), Type, Name, CreatedOn.
// The hierarchy fake (section 3.5) answers the questions SchemaManager.GetAllParents cannot be stubbed for.
hierarchy.Add("AccountFile", accountFileUId, parents: new[] { "File", "BaseFile" }, link: ("Account", "Account"));
```

**R3. ESQ reads.** Platform: `dbExecutor.SetupExecuteReader("File", FileConsts.FileTypeUId)` matches the SQL as a
regex and requires the `Type = File` parameter (`UT/ObjectFileProcessingUserTask_Tests.cs:159-171`). PBT: the
`TestData` + `SetupEsqData(dataTable, sql => sql.Contains("SysModuleReport"))` style
(`PBT/ProcessDesignTestSupport.cs:300-306`). It is used to test the real `IReportTemplateReader` once (columns,
`UseAdminRights = false`, and the module-entity fallback); everything else uses the fake.

**R4. File factory (reference only).** This is how the platform pins a copy:

```csharp
// UT/FileProcessing_Tests.cs:106-147 - platform suite only, it names configuration types
IFileFactory files = userConnection.SetupFileFactory();          // registered in RequestServices
files.Get(Arg.Any<EntityFileLocator>()).Returns(sourceFile);
files.Create(Arg.Any<EntityFileLocator>()).Returns(targetFile);
// run the task, then:
await sourceFile.Received(1).CopyAsync(targetFile);              // sync Copy is AsyncPump over CopyAsync
targetFile.Received(1).SetAttribute("<link column>", connectedObjectId);
```

**R5. Report engine and temporary files (reference only).** `ProcessExecutionTestUtilities.SetupTestProcess` +
`process.SetupCreateTempFile()` + a substitute `IReportEngine` on the task's public setter; the filter factory is
rebound with `ClassFactory.RebindWithFactoryMethod` (`UT/ReportFileProcessingUserTask_Tests.cs:113-135`).

**R6. Running a task (reference only).** A private subclass exposes `InternalExecute(new ProcessExecutingContext(...))`,
and `Files` is assigned directly as `CompositeObjectList<CompositeObject>` rows `{ "File": locator }`
(`UT/ProcessFileProcessingUserTask_Tests.cs:27-48, 69-101`). The platform therefore **never tests building the
rows from a mapping**, and that gap is why M1, M4, ST-03 and ST-04 exist.

**R7. Design-time element.** Platform: `ProcessSchemaUserTask` with a substituted `ParentMetaSchema`, bare named
parameters, `SourceValue.Value` (`UT/ObjectFileProcessingUserTask_Tests.cs:86-106, 203-221`). PBT: section 3.3
(Strategy A, the house pattern of every applier fixture).

**R8. The storage matrix.** Platform: four `[TestCase]`s over (source, target) in {SysFile, File}
(`UT/FileProcessing_Tests.cs:173-230`), plus `[Values] bool useSysFile` on the Process and Report variants
(`UT/ProcessFileProcessingUserTask_Tests.cs:69`, `UT/ReportFileProcessingUserTask_Tests.cs:185`). PBT copies the
**shape**, as `[TestCase]` over storage pairs in the resolver, describe and repair tests (TC-15, TC-34, TC-43,
TC-76).

**Runtime facts our code mirrors, and who pins them.**

| Fact the builder relies on | Pinned by the platform suite | Our test | Still needs the stand |
|---|---|---|---|
| ResultActionType: Object and Report run with 0 and 1 and throw on any other value; Process accepts only 0 (1 throws) | Object: 0 at `UT/ObjectFileProcessingUserTask_Tests.cs:130`, 1 at `:179`, 5 throws at `:188-200`; Report: 1 at `UT/ReportFileProcessingUserTask_Tests.cs:130, 149`, 0 at `:174, 201`, 5 throws at `:322-334`; Process: 1 throws at `UT/ProcessFileProcessingUserTask_Tests.cs:103-112` | TC-25, TC-66 | no |
| Row keys: outputs `File` (Object, Report) and `ObjectFile` (Process); the Process input is read by `File` | `UT/ObjectFileProcessingUserTask_Tests.cs:157-185`; `UT/ProcessFileProcessingUserTask_Tests.cs:74-78` | TC-03 | no |
| `RecordSchemaName` is written only when `TargetDataEntitySchemaUId` resolves; a SysFile read filters by it | `UT/FileProcessing_Tests.cs:173-230`, `UT/ProcessFileProcessingUserTask_Tests.cs:69-101`, `UT/ReportFileProcessingUserTask_Tests.cs:185-211`, `UT/ObjectFileProcessingUserTask_Tests.cs:224-258` | TC-76, TC-77 | yes, end to end (M23, ST-08) |
| Object reads always filter `Type = File` | `UT/ObjectFileProcessingUserTask_Tests.cs:163` | describe issue, guide | M8 (Freedom UI uploads) |
| Report file name = caption [. ReportName] [ (i) / . column value] + extension | `UT/ReportFileProcessingUserTask_Tests.cs:215-320` | TC-54 | ST-06 |
| Report "use in process" writes temporary files through `CreateTempFile` | `UT/ReportFileProcessingUserTask_Tests.cs:119-135` | TC-55 | lifetime: M16, ST-07 |
| `Files` rows built from a mapping (both levels, nested only, outer only) | **none** | TC-62..TC-65 (stored shape only) | **yes: M1, M4, ST-03, ST-04** |

## 5. Test cases

Columns: **Lvl** (section 1), **PR** (the PR that adds the test, per [pr-split](eng-92719-file-processing-element-pr-split.md)),
**Traces** (acceptance criteria from [decisions](eng-92719-file-processing-element-decisions.md) Part D, labelled as in
section 11, and decision ids). A test added in one PR and extended in a later one names both.

### 5.1 Harness and drift

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-01 | Drift guard for the three schemas (section 3.4); after `SchemaUId` was assigned, the element's parameter set is unchanged | helper == capture on every listed property; the sync added and removed nothing | P | PK-PT (inherited by PK-OA; = PU-50 of the ENG-95984 File process parameter type test-plan) | OA-4, FE-5 |
| TC-02 | Each committed capture's SHA-256 equals PROVENANCE.md | equal; a hand edit of a capture fails | P | PK-PT (inherited by PK-OA; = PU-50 of the ENG-95984 File process parameter type test-plan) | FE-5 |
| TC-03 | Mirrored constants as literals: ResultActionType 0/1; item keys `File`, `ObjectFile`, `Id`; SysFile `70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a`; BaseFile `556c5867`; `FileConsts.FileTypeUId` `529BC2F8-0EE0-DF11-971B-001D60E938C6`; the three schema names (production code mirrors the names; the UIds are literals of `FileProcessingTestSupport` and the drift guard only, reuse RF1) | each equals its literal | P | PK-OA | D21 |
| TC-04 | The composition-root tripwire (`PBT/CrtProcessBuilderAppTests.cs:139-199`) gets `FileProcessingElementHandler` and a `ContainInOrder` pair before `UserTaskElementHandler`; `FileProcessingFilterTarget` is registered between Signal (100) and DataNode (0) | present and in order | P | PK-OA | OA-4, D10, D16 |
| TC-05 | Variant registry: for every registered schema UId, all five consumers agree (handler `CanBuild`/`CanDescribe`, generic-route refusal, raw-mapping refusal, filter-target claim, describe block); for an unregistered one, none of them claims it | PK-OA: Object only; PK-RP: + Report, Process; PK-SF: no new variant, SysFile flags flip | P | PK-OA, PK-RP, PK-SF | OA-4, D13 |

### 5.2 Element identity and routing (D10, D13)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-06 | `create` with `{type: fileProcessing, attachments: {...}}` and a curated palette row | `ProcessSchemaUserTask`, `SchemaUId = ManagerItemUId = 9387c794-...`, 69x55, in a lane, `SerializeToDB` = the task schema's; describe `buildType: fileprocessing` + `userTaskName` | P, E | PK-OA, CL-OA | OA-1, OA-4, FE-1 |
| TC-07 | Alias `processFile` | the same element; describe emits only `fileprocessing` | P | PK-OA | D10 |
| TC-08 | No group; two groups; on update, a group of another variant | refused: "name the file source..."; both groups named; dependents named (TC-29) | P | PK-OA | OA-4, D10 |
| TC-09 | `source` check member: equal / different / entity-like ("Account") | accepted / refused with the derived value / targeted refusal pointing to `attachments.object` | P | PK-OA | D10, D11 |
| TC-10 | `userTaskName` on `fileProcessing`: the implied schema in another case; a contradicting schema | ignored; refused with both names. The stub's `FindInstanceByName` uses a case-insensitive predicate | P | PK-OA | D10 |
| TC-11 | A `fileProcessing` block on another element type | refused by the strict block gate (`EnsureBlockMatchesHandler`) | P | PK-OA | D10 |
| TC-12 | Generic route `{type: userTask, userTaskName: ObjectFileProcessingUserTask}`, plus mixed case, padding and the `performTask` alias | refused, keyed on the RESOLVED schema UId, with a message that names `fileProcessing`; in PK-OA the Report and Process generic routes still build (regression); PK-RP refuses them too | P, E | PK-OA, PK-RP | OA-5, D13 |
| TC-13 | Raw `addMapping` onto the 13 binder-owned parameters of a claimed schema; onto `ConnectedObjectId`, `Files`, `Files.File`, `ReportName` | refused, pointing to `setElement fileProcessing`; allowed, with the block's rules applied (D5, D18) | P | PK-OA, PK-RP | D13 |
| TC-14 | The environment cannot resolve the task schema (`FindInstanceByName` returns null) | identity never matches; create says "cannot resolve", not "wrong kind" (`UserTaskSchemaIdentity.IsTaskSchemaResolvable`) | P | PK-OA | OA-4 |

### 5.3 Object attachments: object, storage, record scope, filter, sort (D15 legacy, D16)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-15 | Resolver over the fake hierarchy: Account -> AccountFile / `Account`; Contact -> ContactFile / `Contact` (`f442867d`); Lead -> FileLead; LeadFile + FileLead ambiguity; DNSGuideFile (no conforming link); VwSysProcessFile (a DB view); FeedFile (SysFile family); an AccountInTag-style object; an object with no claimant; SysFile missing | dedicated pairs as the designer derives them; ineligible ones excluded; no claimant gives SysFile mode; `Decode` never throws (`dedicated`, `sysFile`, `unpaired`, `invalid`) | P | PK-OA | OA-6, D15 |
| TC-16 | `attachments.object` given as the record object or as its file object (`AccountFile`) | the same pair; `SourceDataEntitySchemaUId` never written for a legacy source; the whole pair rewritten on every block write | P | PK-OA | OA-6 |
| TC-17 | A SysFile-mode object (Account address) as the source or as the `saveTo` target | refused with the D15 fallback text; the switch has its own test, so it can ship either way | P, E | PK-OA | OA-6 |
| TC-18 | `attachments.recordId` per source: `value` (existence-checked), `processParameter`, `sourceElement` + `sourceElementParameter` (+ `sourceColumn`), `expression` `[#SysVariable.CurrentUserContact#]`, any other formula | a legacy InFilter on the link column; the expression compiles to the `CurrentUserContact` macro; the other formula is refused with the working list | P, E | PK-OA, CL-OA | OA-7, FE-4, D16 |
| TC-19 | Record-scope type check (V3) | a Contact parameter for Account attachments is refused; a Guid is accepted; CurrentUserContact only when the object is Contact | P | PK-OA | D16 |
| TC-20 | Filter root (V1), unconfigured element (V2), scope set twice | `filter.object` other than the runtime root is refused (it names the root and the record object); `setFilter` before `setElement` is refused; a `recordId` together with a condition on the scope column is refused | P | PK-OA | D16 |
| TC-21 | Split/Join | the block writer keeps the filter remainder; `setFilter` keeps the scope; `AND(scope, OR(...))` wraps and unwraps; the designer-made `Contact = RD1.Id` (FileCopyProcessPP) lifts to `recordId` and resubmits byte-stable; an undecodable scope stays in `filter.conditions` | P | PK-OA | OA-2, D16 |
| TC-22 | Empty-filter ledger at the end of the request (touched elements only) | Object saving with no selecting filter: refused, `failedOperationIndex: null`, names the element and the operations; Object "use in process": notice; a `setFilter` later in the same batch satisfies it; an untouched empty-filter element (CRM60006PP shape) is ignored | P, E | PK-OA, CL-OA | OA-9, D16 |
| TC-23 | `sort` (readData shape) | one direct column of the runtime root, canonical name, `desc` -> `CreatedOn:2:1`; a path or a non-root column is refused; a stored multi-entry `OrderByInfo` is kept while `sort` is not sent; describe shows the primary entry plus an issue | P | PK-OA | OA-7, D16 |
| TC-24 | `numberOfRecords` | 1..5000 (0 and 5001 refused); omitted: the template copy "50" with `GS5` = template, plus the "at most 50 files" notice on a saving or forwarding element; an explicit 50: a new value with `GS5` = process | P | PK-OA | OA-7, D16, D20 |

### 5.4 Action and save target (D14, D15)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-25 | Action inference on create | `saveTo` present: 0; absent: 1; `useInProcess` + `saveTo`: refused; `saveToAttachments` without `saveTo`: refused; `ResultActionType` always written as a ConstValue | P | PK-OA | OA-8, D14 |
| TC-26 | `saveTo` to a legacy object | `TargetEntitySchemaUId` = ContactFile `e9eafee9-c4e4-4793-ad0a-003bd2c6a9b4`; `ConnectedObjectColumnUId` = `f442867d-73ca-49b3-a8ba-8a2566b1fc59`; `ConnectedObjectId.ReferenceSchemaUId` = Contact; no `TargetDataEntitySchemaUId` (the UO-4 shape, measured 2026-10-01); a `linkColumn` override is accepted with W6; `"none"` is refused | P | PK-OA | OA-8, D15 |
| TC-27 | `saveTo.recordId` as a constant | unparseable, `Guid.Empty`, unknown, or a record of another object: refused; an existing record: accepted, with its display value | P | PK-OA | OA-8 |
| TC-28 | `saveTo.recordId` from a process parameter, from `sourceColumn`, and from `[#SysVariable.CurrentUserContact#]` | built and described; each mapped source gets a notice ("an empty value at run time fails a legacy target") | P, E | PK-OA, CL-OA | OA-8, FE-4 |

### 5.5 `setElement` semantics (D12)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-29 | The same group again; another variant's group | a no-op; refused, naming the dependents from `ProcessElementDependencyScanner` | P | PK-OA | OA-2, D12 |
| TC-30 | Member-wise merge | `{attachments: {numberOfRecords: 10}}` keeps the sort and the record; `{saveTo: {linkColumn: "X"}}` keeps the object and the record; empty values clear where clearing is legal; a value-source descriptor is replaced whole | P | PK-OA | D12 |
| TC-31 | Retarget Account -> Contact (legacy) | the filter (scope and remainder), the sort and the record scope are cleared unless a new `recordId` is sent; the notice lists what was cleared | P | PK-OA | D12 |
| TC-32 | `saveTo.object` changed without `saveTo.recordId` | refused | P | PK-OA | D12 |
| TC-33 | Action on update | a partial update of a saving element keeps 0 and the target; `saveTo` sent to an in-process element switches it to 0 with a notice; save -> use in process clears all four target fields, including `TargetDataEntitySchemaUId`, and is refused while `CreatedObjectFileIds` is mapped | P | PK-OA | D12, D14 |
| TC-34 | Repair on touch, `[TestCase]` over stored pairs (H-G3-1: `SourceDataEntitySchemaUId = AccountFile`; unpaired; consistent) | a touched element is rewritten from `Resolve` with a notice; a consistent pair is untouched; untouched elements of the modify are not examined | P | PK-OA | D12, D15 |

### 5.6 Outputs and consumers (FE-2, OA-3)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-35 | `ObjectFiles.File` and `CreatedObjectFileIds.Id` used by two downstream consumers in one request: a FileCollection process parameter (D5 binds both levels) and a multi-instance sub-process (`InputRecordCollection.F <- OF1.ObjectFiles.File`, P1 binds the parent) | both stored at both levels; describe shows both | P, E | PK-OA, CL-OA | OA-3, FE-2 |
| TC-36 | A create batch whose later mapping reads `OF1.ObjectFiles.File` | the mapping token embeds the RE-MINTED nested UId (D20 ordering) | P | PK-OA | D20 |

### 5.7 Serialization parity and diagram (D20)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-37 | Template copies | create: `ConsiderTimeInFilter` = Script "true", `GS5` = template UId, `GS8` "false" (an exact copy, not the server's "false"); edit: never repinned; derived values carry `GS5` = process | P | PK-OA | FE-5, D20 |
| TC-38 | Nested UIds | create: fresh UIds with `IL2` = element UId, before `Configure`; modify of an element that still carries template nested UIds: kept, and its consumer tokens stay valid | P | PK-OA | D20 |
| TC-39 | Display value of a nested source | `[#<element caption>.<collection caption>:<item caption>#]` | P | PK-OA | D20 |
| TC-40 | A template parameter missing after `GetDesignInstance` | refused ("not synchronized"); never hand-created; encoded by the template type | P | PK-OA | D20 |
| TC-41 | Parity, Object, legacy storage. The read side against SkillFilesValidationProcess Object1; the save side against FileCopyProcessPP Object1, with the parameters that file predates excluded and named in the test | equal key for key under the serialization-capture section 10 rule. Element UId, name and lane are ignored (elements paired by caption), and so are parameter and nested UIds; a nested item's `IL2` is still compared. `GS5` is normalised for template-default values (N7). Tolerated: N2 (`BL8` absent), N9 (process-parameter `IL2`), N10 (no nested `BK15` rows), N13 (DisplayValue rows of hidden constants reported, not compared). Normalised: N11, N14, N15, N16. Parameters the oracle predates are excluded and named in the test. Any other difference fails. This is the TC-41 rule that TC-59, TC-69, DT-02 and DT-04 cite | P | PK-OA | OA-10, FE-5 |

### 5.8 Describe (D19) and the clio read path

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-42 | Effective values | inherited `RecordsToRead` 50; an unset `ResultActionType` reads `action: saveToAttachments` + `actionStored: false`; `storage`, `fileObject`, `linkColumn`; the lifted `recordId`; `outputs` | P | PK-OA | OA-2 |
| TC-43 | `issues`, `[TestCase]` per hazard | H-G3-1 source half "throws at run time" (pending M11), target half "cosmetic"; target with no link column; sort or filter column not on the root; hidden `OrderByInfo` entries; unset `ResultActionType` | P | PK-OA | OA-2, D19 |
| TC-44 | Round trip | build -> describe -> remove describe-only members -> resubmit: accepted, unchanged; an identity check member kept equal is accepted; a changed one is refused with the derived value; a uk-UA caption does not break the resubmission | P | PK-OA, PK-RP | OA-2, RP-7 |
| TC-45 | Snapshot tolerance (M13), `[TestCase]` per variant. PK-OA: the Object elements (the three CRM60006PP Object elements carry a Lookup-typed `ConnectedObjectId`; 11 of 14 shipped Object elements lack `SourceDataEntitySchemaUId`). PK-RP: the Report elements (PrintContractsReport, FileReportNamesProcess x2, PortalReportbySignal carry the Lookup type). Measured over the corpus, 2026-10-01: the counts are those of traps T-50, the per-process list is the corpus scan's, and `PS/ProcessTests/branches/7.8.0/Schemas/CRM60006PP/metadata.json` was re-read for this row (3 of 3 `ConnectedObjectId` typed `b295071f`). M13's answer sets the fixtures of both PRs, so it gates PK-OA as well as PK-RP (section 8, M13); it runs on day 0 (open-questions B.2), so the answer is known before PK-OA code | absent parameters read as null; a Lookup-typed `ConnectedObjectId` (corpus drift) reads like a Guid | P | PK-OA, PK-RP | OA-2, RP-7 |
| TC-46 | Per-cut describe and preservation | in PK-OA, Report and Process elements describe as generic `usertask` with raw parameters, and a modify of a process holding one leaves it byte-identical; a designer-built SysFile Object element is described (`storage: sysFile`), and `setElement`/`setFilter` on it is refused | P, E | PK-OA, CL-OA | OA-2 |
| TC-47 | `DescribedFileProcessing` with `[JsonExtensionData]` on every nested type | an unknown member at every level survives deserialisation and serialisation; outbound wire names pinned (the `ServerProcessDescriberTests.cs:57` rule: the outbound assertion is the one that fails) | C | CL-OA, CL-RP | OA-2 |
| TC-48 | An older clio | the block survives through `DescribedElement`'s bag (`clio/Command/ProcessModel/IProcessDescriber.cs:725`); pinned with a raw JSON payload | C | CL-OA | D19, D26 |
| TC-49 | create/modify forward the block verbatim | the tool test sends `fileProcessing` and asserts the forwarded descriptor (pattern `CreateBusinessProcessToolTests.cs:122`) | C | CL-OA | OA-1 |

### 5.9 Generated report (D17)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-50 | `report.printable` as an id, a `[#Lookup...#]` macro, a caption; an ambiguous caption (two different printables captioned "Contract" ship in CoreContracts and CrtOrderContractMgmtApp; the stand carries one, so this case is unit-only) | resolved; narrowed by `report.recordId`'s object when exactly one candidate matches, otherwise refused with every candidate's id, type and object | P | PK-RP | RP-1 |
| TC-51 | Missing; empty GUID; unknown or unreadable; DevExpress; FastReport; a printable with no object | refused; unknown carries the Report setup hint and the `templateId` bridge, plus up to N candidates for the object; DevExpress and FastReport are unit-only (no stand fixture) | P | PK-RP | RP-1, RP-3 |
| TC-52 | `report.recordId` | `Id = value` on the report entity; V3 against that entity; the filter root is the report entity | P, E | PK-RP, CL-RP | RP-1, FE-4 |
| TC-53 | `separateReports` | Word, omitted: written true; an explicit false: accepted as given, with a notice; FastReport: n/a (refused) | P | PK-RP | D17 |
| TC-54 | `fileNameSuffix` / `fileNameSuffixColumn` | a constant goes to the resource `BaseElements.<El>.Parameters.ReportName.Value`, a formula inline; the column must be a TEXT column of the report entity with separate on; both together, or a lookup column, refused | P | PK-RP | D17 |
| TC-55 | Action and outputs | "use in process": `ReportFiles` mappable, and the temporary-file notice when it flows into an Out parameter; `saveTo` InvoiceFile / `Invoice` | P | PK-RP | RP-2 |
| TC-56 | Word with no selecting filter; a scope that can match several records with save | refused at the end of the request; the "all N files go to one record" notice | P | PK-RP | RP-4 |
| TC-57 | Changing the printable | same report object: filter, scope and suffix column kept, V3 re-run; another object: cleared, with a notice | P | PK-RP | D12 |
| TC-58 | Notices through the faked seam | `EnableReportFileProcessingUserTask` OFF: notice, never a refusal; ON: none; `ConvertInPDF = true`: the converter notice | P | PK-RP | D17 |
| TC-59 | Parity against PrintInvoiceReport Report1 | equal under the TC-41 rule | P | PK-RP | RP-8, FE-5 |
| TC-60 | Word "save to attachments" built and described over MCP | built; `Assert.Ignore` when `list-printables` has 0 rows | E | CL-RP | RP-1, RP-7 |
| TC-61 | Read-only describe of PrintInvoiceReport, PrintQuotationReport, PrintContractsReport and GenerateDNSRecordsSpecification on the stand | `buildType: fileprocessing`, the block decoded, raw `parameters[]` lossless; never modified | E | CL-RP | RP-7 |

### 5.10 Process parameter (D18)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-62 | `files.processParameter` = a FileCollection | `Files <- [#Param#]` and `Files.File <- [#Param.File#]`, both `GS1` 3; the nested token references the source's nested-item UId | P | PK-RP | RP-5 |
| TC-63 | `files` from an element output: `OF1.ObjectFiles`, `OF1.ObjectFiles.File`, `RF1.ReportFiles`, and another Process element's `ObjectFiles.ObjectFile` | both levels; the item is resolved (`File`, `ObjectFile`, the declared name), never assumed | P | PK-RP | RP-5, RP-6 |
| TC-64 | A single File source, `[TestCase]` over the M1 switch | M1 passed: `Files.File` only, `Files` Source None; M1 failed: refused, with "wrap it in a FileCollection" | P | PK-RP | RP-5 |
| TC-65 | `files.expression` | written verbatim onto `Files.File`, `Files` None, with the "not checked" notice | P | PK-RP | RP-5 |
| TC-66 | Refusals | `value`; a structured source with no FileLocator item (a Read data collection, R-M2); outer only; nothing bound; a mapping FROM `Files`; `useInProcess`; `saveTo` missing | P, E | PK-RP, CL-RP | RP-6 |
| TC-67 | Outputs | `ObjectFiles` (item `ObjectFile`, the copies) and `CreatedObjectFileIds` listed and mappable downstream | P | PK-RP | RP-6 |
| TC-68 | Describe of `files` | decodes to `processParameter` / dotted `sourceElementParameter`; `bindingLevels` is both, itemOnly, outerOnly or none; an `issues` entry for outer-only or an undecodable shape; resubmitting an outerOnly element repairs it to both | P | PK-RP | RP-7 |
| TC-69 | Parity against FileParameterProcess Process2 (element source) and Process3 (process-parameter source) | equal under the TC-41 rule, both binding levels included. `TargetDataEntitySchemaUId`, which both 7-parameter captures predate (serialization-capture G-C2), is excluded and named in the test; SC-1 E8/E9 replace these oracles once captured. For Process3, the source FileCollection's item name (`FileCollectionParameter`, renamed `File` by D2) is role-normalised | P | PK-RP | RP-8, FE-5 |
| TC-91 | The `Files` mapping rules over the wire on a builder-configured Process variant (= E2E-14a-d of the ENG-95984 File process parameter type test-plan; owner decision O-TP1, option B). (a) `Files <- Docs`: P2 pairs `Files.File`, with a notice. (b) `Files.File <- Docs.File`: both levels bound (P1). (c) `Files.File <- Doc` (a single File): `Files` is listed although its outer level is unbound (the D8 nested-only listing); only if M1 passed, otherwise the refusal is asserted. (d) `Files <- ReadData1.ResultCompositeObjectList`: refused (R-M2) | as listed, each with a describe read-back | E | CL-RP | RP-5, D5, D8 (and ENG-95984 File process parameter type AC-5, AC-7, AC-9) |

### 5.11 Pattern tests over MCP (FE-6; patterns from [use-cases](eng-92719-file-processing-element-use-cases.md))

Each test builds and describes the pattern; the matching stand scenario (section 6) runs it.

| TC | Pattern | Build | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-70 | Read data -> Process file (P2a) | `attachments.recordId <- RD1.ResultEntity` + `sourceColumn: Id` | E | CL-OA | FE-6, OA-7 |
| TC-71 | Process file -> Modify data, re-finding the file (P4; use-cases §5; decisions D-2) | Object saving to a Contact, then `changeData` on ContactFile filtered by Contact + Name contains + CreatedOn = CurrentHour, setting Tag | E | CL-OA | FE-6 |
| TC-72 | Process file -> multi-instance sub-process per file (P8; needs PT) | callee with a File parameter `Doc` (In); `InputRecordCollection.Doc <- OF1.ObjectFiles.File`; describe shows both levels | E | CL-OA | FE-6, OA-3 |
| TC-73 | Object -> Process chain, and Report "use in process" -> Process (P6) | `files.sourceElement: OF1, sourceElementParameter: ObjectFiles`; the same from `RF1.ReportFiles` | E | CL-RP | FE-6, RP-2 |
| TC-74 | Add data -> report (P3; use-cases §5; decisions D-2) | `addData` of a DNSGuideReport row, then a Report on the "DNS Requirements" printable with `report.recordId <- AddData1.RecordId`, then `deleteData` of the row | E | CL-RP | FE-6, RP-9 |
| TC-75 | Two consumers of one output (the FileParameterProcess shape) | `OF1.ObjectFiles` -> PF1 directly, AND -> FileCollection `Docs` -> PF2 | E | CL-RP | FE-2 |

Process file -> Send email belongs to SE's test plan. Process file -> Creatio.ai is out of scope; the guidance says
it is not buildable through this tool yet.

### 5.12 SysFile storage (SF)

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-76 | Resolver in SysFile mode | Account address -> `SourceEntitySchemaUId` = SysFile, `SourceDataEntitySchemaUId` = the record object (UO-3 shape, measured 2026-10-01); a target -> SysFile + link `RecordId` + `TargetDataEntitySchemaUId`; Account (dedicated) never SysFile | P | PK-SF | SF-1 |
| TC-77 | SysFile record scope | the shape M7 captured (default: a lookup InFilter on `RecordId` with `referenceSchemaName` = the record object); `Split` accepts both shapes; a stored scope whose lookup reference is not the record object gives a describe issue | P | PK-SF | SF-1 |
| TC-78 | Sort on the SysFile root (V9) | a record-object column (`GPSE`) refused; a SysFile column (`CreatedOn`) accepted | P | PK-SF | SF-1 |
| TC-79 | SysFile -> SysFile retarget (Account address -> Contact address) | the scope is cleared (`ClearIfForeign` is not used) | P | PK-SF | SF-1, D12 |
| TC-80 | The M25 notice through the faked seam | OFF: notice; ON: none; never a refusal; ships only if M25 confirmed the read | P | PK-SF | SF-1 |
| TC-81 | `ProcessFileSysFileStorageToolE2ETests`: Object on a SysFile entity (create, describe, `saveTo` a SysFile target); Report and Process into a SysFile target; the PK-OA refusal e2e re-pointed to its positive form | built and described; `Assert.Ignore` when no record of the object exists | E | CL-SF | SF-1 |

### 5.13 clio surface and regressions

| TC | Case | Expect | Lvl | PR | Traces |
|---|---|---|---|---|---|
| TC-82 | `ToolContractPayloadBudgetTests` | under the 35,072 B ceiling; the figures in its comment re-measured | C | CL-OA, CL-RP | OA-12 |
| TC-83 | The first `name=process-files` in a description | `curated-knowledge-names.json` re-pinned in the same commit; `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` (`WorkspaceTemplateGuidanceDriftTests.cs:537-594`) is green | C | CL-OA | OA-12 |
| TC-84 | Pins and floors | `BundledProcessBuilderPackageTests` (archive, SHA, version, stamp), `ProcessDesignerRequiresPackageAttributeTests`, the enforced-floor sentences, the capability-map literals; the e2e `MinimumPackageVersion` equals the floor | C | CL-OA, CL-RP, CL-SF | OA-12, RP-10 |
| TC-85 | Capability probe on `ProcessElementUpdateDescriptor.FileProcessing` | the old archive gives 0 and the new one gives 1; kept as a permanent guard | C | CL-OA | D26 |
| TC-86 | `validate-process-graph` over a described `fileprocessing` element | no UNKNOWN finding (the ManagerMap arm shipped in CL-PT) | C, E | CL-OA | OA-1 |
| TC-87 | Fixtures that must stay green | `ListUserTasksToolE2ETests`, `ValidateProcessGraphToolE2ETests`, `DescribeProcessToolE2ETests`, `SubProcessMultiInstanceToolE2ETests`, `RecordColumnSourceToolE2ETests` | E | CL-OA, CL-RP | OA-12 |
| TC-88 | Knowledge | pins per rule family (group = variant, the 50-file default, describe-only members, storage refusal per cut, temporary report files, `files` expression verbatim); the endpoint sentence of decisions D-3 ("most shipped uses end in a record's attachments; when a following element needs the files, map the element's output collection"), and no "rarely an endpoint"; the "after saving, ObjectFiles points at the source files" sentence only once M26 is recorded; `process-files` at most 22,000 chars (split `process-files-report` past 80%); the element-catalog generic-route sentence worded per cut | K | KB-OA, KB-RP, KB-SF | OA-11, RP-9, FE-3 |
| TC-89 | `list-user-tasks` description, in `clio.tests/Command/McpServer/ListUserTasksToolTests.cs` (fixture shape `:10-16`; the attribute is read as in `CreateBusinessProcessToolTests.cs:326-349`, from `typeof(ListUserTasksTool).GetMethod(nameof(ListUserTasksTool.ListUserTasks))`). Today the text says "Three exceptions", lists four, says "rejects either", and gives the generic-userTask fallback to every exception (`clio/Command/McpServer/Tools/ListUserTasksTool.cs:46-62`; plan section 10, C-1) | CL-OA: names `ObjectFileProcessingUserTask` with `fileProcessing` and a pinned "no generic fallback" clause; the sentence that carries "fall back to a generic userTask" names no file schema and no `fileProcessing`; no count word (no `\b(one\|two\|three\|four\|five\|six\|\d+) exceptions\b` match, case-insensitive, and no "either" over the list). CL-RP: the same for `ReportFileProcessingUserTask` and `ProcessFileProcessingUserTask` | C | CL-OA, CL-RP | OA-5, OA-12, RP-10, D13 |
| TC-90 | MCP prompt pins (decisions D25), beside the existing prompt pins (`clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs:334`, `ModifyBusinessProcessToolTests.cs`, `DescribeProcessToolTests.cs:94-109`), with `ListUserTasksPrompt` in `ListUserTasksToolTests` | CL-OA: `ListUserTasksPrompt` (`clio/Command/McpServer/Prompts/ListUserTasksPrompt.cs:22-25`) names ObjectFileProcessingUserTask as built with `fileProcessing`, and its generic-`userTask` sentence names no claimed schema; `CreateBusinessProcessPrompt` carries the `fileProcessing` sentence (group = variant; not a generic userTask; `get-guidance name=process-files`); `ModifyBusinessProcessPrompt` carries the D12 merge rule; `DescribeProcessPrompt` lists `fileProcessing`. CL-RP: the same for the other two schemas and the `report` / `files` groups | C | CL-OA, CL-RP | OA-12, RP-10, FE-6, D13, D25 |

## 6. Stand scenarios that run a process

Each scenario runs once, after the PR's cut is installed and before the package PR leaves draft (the
[pr-split](eng-92719-file-processing-element-pr-split.md) stand protocol). The builder builds the process; probes the
builder cannot build yet belong to section 8.

Rules:
- Every run, upload, fixture record and cleanup needs the user's explicit go-ahead.
- One write at a time: parallel schema writes crash this .NET Framework stand's app pool.
- Fixtures are disposable records named `ST<nn> ...`, never customer records. The one exception is the existing
  Invoice of ST-06 and ST-07. It is selected read-only (top 1; Invoice has required lookups, so it is never
  inserted), and cleanup removes only the file rows those runs create.
- The user checks UI results (attachment lists).
- Evidence follows section 9.
- A failed scenario stops the package merge. Only a human merges.

| ST | PR | Scenario | Fixtures (user) | Build (after the cut) | Pass condition (Runtime level) |
|---|---|---|---|---|---|
| **ST-01** Legacy storage, save | PK-OA | Object attachments of an Account saved to a Contact | Account `ST01 Source` with 2 uploaded attachments (AccountFile); Contact `ST01 Target` | `fileProcessing {attachments: {object: Account, recordId: {processParameter: AccountId}}, saveTo: {object: Contact, recordId: {processParameter: ContactId}}}` | OF1 Completed; 2 new ContactFile rows with `Contact` = target, `Type = File`, `Version = 1`, Name and Size equal to the sources; the source AccountFile rows unchanged; the user sees both attachments on the contact |
| **ST-02** Per-file iteration | PK-OA (after PT is merged) | Object "use in process" -> multi-instance sub-process per file | Contact `ST02 Source` with 2 attachments | callee `UsrSt02PerFile` (File parameter `Doc` In, then Add data of an Activity titled "ST02"); caller: OF1 `useInProcess` -> sub-process with `InputRecordCollection.Doc <- OF1.ObjectFiles.File` | 2 callee instances and 2 "ST02" activities (not 1: P1 bound the parent); describe shows both levels |
| **ST-03** Single File into the Process variant | PK-RP (only if M1 passed) | ST-02's callee gains a Process element `files: {processParameter: Doc}` saving to an Account | ST-02 plus Account `ST03 Target` | as ST-02 | 2 AccountFile copies on the target, one per iteration (the nested-only path, D18) |
| **ST-04** Process-variant consumption | PK-RP | the M4 chain, builder-built: OF1 `useInProcess` -> FileCollection `Docs` -> PF1 saving to an Account | Contact with 2 attachments; Account `ST04 Target` | `addMapping Docs <- OF1.ObjectFiles` (P2 pairs the item); PF1 `files: {processParameter: Docs}` | PF1 Completed; 2 AccountFile copies; describe of PF1 shows `bindingLevels: both` |
| **ST-05** Multiple downstream consumers | PK-RP | the FileParameterProcess shape: one output, two consumers | Contact with 2 attachments; Contact `ST05 T1`; Account `ST05 T2` | OF1 `useInProcess` -> PF1 (`files <- OF1.ObjectFiles`, save to T1), and `Docs <- OF1.ObjectFiles` -> PF2 (`files <- Docs`, save to T2), on sequential flows | 2 ContactFile rows on T1 and 2 AccountFile rows on T2; the source rows unchanged |
| **ST-06** Report, save, re-find | PK-RP | Word report on an Invoice, saved, then Modify data re-finds it and sets a Tag (the PrintInvoiceReport product pattern) | an existing Invoice, read-only (top 1 `Invoice.Id, Number`; never insert, because Invoice has required lookups); printable `e1f1a474-1a77-82f8-4bc7-b1da23699e13` "Invoice" | `report: {printable: "e1f1a474-...", recordId: {processParameter: InvoiceId}, fileNameSuffixColumn: "Number"}`, `saveTo: {object: Invoice, recordId: {processParameter: InvoiceId}}`; then `changeData` on InvoiceFile (Invoice = param, Name contains "Invoice", CreatedOn = CurrentHour), Tag "ST06" | 1 new InvoiceFile row, `Invoice` = id, `Name = "Invoice. <Number>.docx"` (`.docx`, because `ConvertInPDF` is false on all 5 printables, measured 2026-10-01), `Type = File`, `Tag = "ST06"` |
| **ST-07** Report "use in process", temporary files | PK-RP (closes M16 for the post-code wording) | Report `useInProcess` -> Process variant, with a parking step | the ST-06 Invoice; Contact `ST07 Target` | RF1 `report: {printable: "e1f1a474-1a77-82f8-4bc7-b1da23699e13", recordId: {processParameter: InvoiceId}, fileNameSuffixColumn: "Number"}`, `useInProcess` -> PF1 `files: {sourceElement: RF1, sourceElementParameter: ReportFiles}` save to the contact -> a Perform task -> End | while parked: SysProcessFile rows for the instance >= 1; after the Activity is completed (`odata-update`, a write): the instance completes, SysProcessFile = 0, and 1 ContactFile copy named `Invoice. <Number>.docx` |
| **ST-08** SysFile storage | PK-SF (after M8 and M23) | (a) Account attachments -> saved to an **Account address** record (no `<X>File`: SysFile); (b) a second process reads that address's attachments (SysFile source) and saves them to a Contact through the Process variant; (c) optional: a Freedom UI upload on a record of a SysFile-stored custom object, read by an Object element | Account with 2 attachments; one `AccountAddress` record of that account; Contact `ST08 Target`; for (c), a disposable `Usr` object with an Attachments component | (a) `saveTo: {object: AccountAddress, recordId: ...}`; (b) `attachments: {object: AccountAddress, recordId: ...}`, `useInProcess` -> PF1 -> Contact | (a) 2 SysFile rows: `RecordId` = the address, `RecordSchemaName = 'AccountAddress'`, `Type = File`, `Version = 1`; (b) 2 ContactFile copies (the SysFile read found what the write wrote); (c) the copy exists, and the user sees the files in the Freedom UI list |

The ST-06 hazard is deliberate: the product pattern re-finds the file by "same hour". A second run in the same
hour also tags the first file. Run it once, and read the count before and after.

## 7. Design-time checks (the user, in the classic designer)

| DT | PR | What the user does | Pass |
|---|---|---|---|
| DT-01 | PK-OA | Opens ST-01's element, checks that source, record, sort, target and record field display, closes the panel without changes, and saves | the metadata read before and after is equal; in particular `SourceDataEntitySchemaUId` is NOT `AccountFile` afterwards (H-G3-1, M22). If it is, the D15 guide warning ships and the bug Sub-task is filed |
| DT-02 | PK-OA | Builds the same Object element by hand (legacy source, scope, sort, save target) and saves it | equal to the builder's element under the TC-41 rule (the AC "stand verification against a designer-built capture") |
| DT-03 | PK-RP | Opens ST-04's and ST-06's elements: the source selector shows the right variant, the Word "separate" checkbox is forced on, `Files` shows the bound parameter; a no-op save | metadata unchanged |
| DT-04 | PK-SF | Opens ST-08's elements; compares the result with the M21 capture | metadata unchanged; equal to M21 under the TC-41 rule |

Read the metadata for DT-01..DT-04 from the raw `SysSchema` row of the process (`execute-esq`, or path C of
[serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §12.2), or with `pull-pkg` /
`export-schema` into a scratch folder, plus the stored resources. Never from describe: it hides inherited defaults,
empty link columns and nested-only bindings (serialization-capture §1, S6). Never compare by eye.

## 8. Pre-code measurements: methods

Status lives in [open-questions](eng-92719-file-processing-element-open-questions.md); the decision each one
settles is in [decisions](eng-92719-file-processing-element-decisions.md) D29.

How these probes are run:
- **UO method:** a new unsaved process in the classic designer, read in memory with
  `Terrasoft.ProcessSchemaManager.items[0].instance.flowElements`; the tab is then closed without saving
  (user-observations, 2026-10-01).
- **Probes with File parameters or the Process variant** are built by a human in the designer, because the builder
  cannot build them yet.
- **Script tasks** must be saved in the designer, which compiles them; a server-side save is invisible to Build.

| M | Method | Write? | Pass / record |
|---|---|---|---|
| M1 | Designer probe `UsrG1FilesBindingProbe`: process parameter `FileParam` (File, plain Add, Variable); a script task sets it to `new Terrasoft.File.EntityFileLocator("ContactFile", <S.Id>)` for an uploaded attachment S of Contact `G1 Probe Source`; a Process element whose "Files" field is `[#FileParam#]`, saving to `Contact (Contact attachment)` of `G1 Probe Target`. Before the run, confirm in memory that `Files` source = 0 and `Files.File` source = 3 (describe hides nested-only). Run; EV-3, EV-5 | yes | element Completed and 1 ContactFile copy -> the single-file path ships; Error or 0 copies -> it is withdrawn (TC-64) |
| M2 | M1 without the script task | yes | element Error with an NRE: the guide wording "a single File must be set before the element runs" |
| M3 | (PT) As the ENG-95984 File process parameter type plan W0 and open-questions B.4. Callee `UsrFpM3Callee` records the result in branch captions (`M3 name empty` / `M3 name set`). Probe `UsrFpM3MirrorProbe`: Read data of 3 contacts (collection) -> `typeFromElement` mirror `P` on 1.6.6.54 -> a multi-instance sub-process over `P`, with ONE designer step by the user mapping the callee's `Name` from `P > Name` (1.6.6.54 refuses the dotted `P.Name` from the builder, D4). Control `UsrFpM3Control` on element paths only. Run each once | yes | three "name empty" against a control of three "name set" confirms H-1 (EV-4) |
| M4 | Designer-built: Object `useInProcess` on a contact with 2 attachments -> `Docs` (both levels) -> Process element saving to an Account | yes | 2 AccountFile copies |
| M6 | (PT) Designer-built `UsrFpM6FlatFile` (open-questions B.4): process parameter `F` (File); OF1 = Object attachments of `G1 Probe Source`, "Use in process"; a script task throws the text of `F`'s locator. On 1.6.6.54 the builder adds `F <- OF1.ObjectFiles.File` (accepted there); the user re-saves in the designer so the script compiles; run once; EV-3 | yes | the locator text, empty, or an NRE: it decides R-M1 |
| M7 | UO method: source Account address; scope `RecordId` = a constant record, then = a process parameter; read `DataSourceFilters` | no | record leaf `filterType`, `dataValueType`, `referenceSchemaName` and the right-hand shape (default assumed: a lookup InFilter) |
| M8 | Read-only first: the SysFile row of `FP Doc A` (`RecordSchemaName`, `TypeId`; open-questions B.4). Then the M23b run `UsrFpM23bSysFileToLegacy` (Object on `UsrFpDoc`, record `FP Doc A`, save to `Contact (Contact attachment)`) | yes | 1 ContactFile copy: the read finds Freedom UI uploads |
| M9 | A SysFile Object element with `OrderByInfo = GPSE:1:1`; run | yes | element Error: the severity of the TC-78 refusal |
| M10 | UO method: open "Which object to receive file from?" fully, and list the entries | no | each entry matches the TC-15 prediction (legacy entries read `<Entity> (<file entity caption>)`, UO-2) |
| M11 | (a) Open PrintQuotationReport's report element, close the panel, read `TargetDataEntitySchemaUId` in memory; (b) unsaved process: an Object element on `Account (File and link of account)`, close the panel, reopen it, close without change, read `SourceDataEntitySchemaUId` in memory; (c) optional, with a go-ahead: save that state and run once | (a)(b) no; (c) yes | (b) = AccountFile confirms the harmful half; the TC-43 severity, the guide warning, the bug Sub-task |
| M12 | UO method: SysFile + Account set by JS; reload; in-memory re-save | no | recorded for a later SysFile override |
| M13 | On day 0, on 1.6.6.54, before the first cut of this work is installed (open-questions B.0): read-only describe of PrintContractsReport: the type of `ConnectedObjectId` | no | Guid = converged; Lookup = snapshot. It decides the TC-45 fixtures, so it gates PK-OA (Object elements) as well as PK-RP (Report elements); decisions D29, open-questions B.2 and the README stand table say the same |
| M14 | On day 0, on 1.6.6.54, before the first cut of this work is installed (open-questions B.0): clio 1.6.6.54, generic route `{type: userTask, userTaskName: ObjectFileProcessingUserTask}`, plus a consumer mapping from `OF1.ObjectFiles.File`; read the stored metadata; the user opens the card | yes | record `ConsiderTimeInFilter`, nested UIds versus the template, `IL2`, nested BK15, `BO2`, `CL2`, `BL7`, the display of the nested binding. Sets the helper's `SyncShape.Server` and TC-37..TC-39 |
| M15 | Read-only `list-packages` (FastReport*), plus `execute-esq` on `SysModuleReport` where `Type.Name = 'FastReport'` | no | none present: FastReport stays refused |
| M16 | Report `useInProcess` + a Perform task: EV-7 while parked; complete the Activity with `odata-update`; EV-7 again; variant: the report returned from a sub-process | yes | >= 1 then 0: the lifetime wording |
| M17 | Q7 read-only: the foreign keys of ContactFile (`SELECT fk.name, OBJECT_NAME(fk.referenced_object_id) FROM sys.foreign_keys fk WHERE fk.parent_object_id = OBJECT_ID('ContactFile')`; run through `execute-sql-script`, which on cliogate 2.0.0.53+ logs the statement in `ClioSqlRequestLog`: an audit row, no business data), and `Contact` with an empty Id = 0 rows. Runs only after that: an empty `ConnectedObjectId` to ContactFile, and to SysFile | Q7 no; runs yes | FK error (legacy) / orphan `RecordId = 0000...` (SysFile): the severity of the notice |
| M18 | A constant `fileNameSuffix` with a non-default profile culture; run | yes | the file name: the culture note |
| M19 | 1.6.6.54: a collection parameter as a filter value; run | yes | the error: the urgency of the filter-value Sub-task |
| M20 | 1.6.6.54: `setElement` with an unknown block next to a known field | yes | the field changes and the block is dropped silently: the floor premise |
| M21 | SC-1 E3 of [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §12.4: the user's one save of `UsrFpSc1Capture` holds a SysFile-mode Object element with a scope, a target and a sort; read back by SC-2 (`pull-pkg`, `export-schema` or the `SysSchema` row). M23b's saved metadata cross-checks it | yes | the SysFile capture for TC-81 and DT-04 |
| M22 | = DT-01..DT-04 (after code): the user opens builder-made parameters and elements and saves with no change | yes (a no-op designer save is a schema write; go-ahead) | metadata before and after identical, apart from known designer normalisations |
| M23 | Designer probes, one at a time: (a) AccountFile source -> SysFile target (custom entity), save; (b) SysFile source -> ContactFile; (c) Word report -> SysFile target; (d) Process element -> SysFile target. EV-5 on SysFile and ContactFile; the user checks the Freedom UI list | yes | rows carry `RecordId`, `RecordSchemaName`, `Type = File`, Name, Size, and are visible in the list. A failing case keeps its refusal (D15 fallback) |
| M24 | A disposable process: send an array into `readData.sort` (an object member), and an object into an array member | yes | loud fault / null / other: the guide sentence for `attachments.sort` |
| M25 | As open-questions B.4: designer-built `UsrFpM25Feature` in `Custom`, one script task: `throw new System.Exception("M25 a=" + Creatio.FeatureToggling.Features.GetIsEnabled("ProcessFeatures.UseSysFileInObjectFileProcessing") + " b=" + Creatio.FeatureToggling.Features.GetIsEnabled("UseSysFileInObjectFileProcessing") + " control=" + Creatio.FeatureToggling.Features.GetIsEnabled("ProcessFeatures.NoSuchFeatureXyz"));`. Save it in the designer, which compiles it; run once; read the text from EV-3 `ErrorDescription`. If the string overload does not compile, the compiler message is the result. The throw writes no business record, so only the probe needs cleanup. Alternative where a record is acceptable: write the same three values into a process parameter and then into an Activity title | yes | `a` or `b` reads true and `control` reads false (the client read, open-questions Part C, measured 2026-10-01: `ProcessFeatures.UseSysFileInObjectFileProcessing` true, a control code false): the TC-80 notice ships under the code name that read true. Both read false, or `control` reads true: the TC-80 notice does not ship |
| M26 | Designer-built probe ([traps T-19](eng-92719-file-processing-element-traps.md#t-19)): OF1 = Object attachments of Contact `M26 Source` (2 attachments), "Save to object attachments" onto Account `M26 Target`; a multi-instance sub-process with `InputRecordCollection.Doc <- OF1.ObjectFiles.File`, whose callee's script task writes `Doc`'s `EntitySchemaName` and `RecordId` into the title of an Activity it adds; run once; read ContactFile, AccountFile and Activity | yes | the titles carry `ContactFile` and the source Ids, not the new AccountFile Ids: the OA.12 sentence and the CL-OA record ship; otherwise neither does |

## 9. Read-only evidence recipe

### 9.1 Queries

Use `execute-esq` (`ReadOnly = true`, `clio/Command/McpServer/Tools/ExecuteEsqTool.cs:37`), one call at a time, in
this order. Do not filter by `CreatedOn`: ESQ resolves date filters in the profile's time zone. Take an **Id
baseline** before the run, and compare after it.

| EV | When | Root, columns, filter | Reads as |
|---|---|---|---|
| EV-0 | before the run | the target file object (`ContactFile` filter `Contact = <target>`, `AccountFile` filter `Account = <target>`, `InvoiceFile` filter `Invoice = <id>`, or `SysFile` filter `RecordId = <target>` AND `RecordSchemaName = '<Entity>'`): `Id`; the same for the source rows: `Id, Name, Size` | the baseline sets |
| EV-1 | the run | `run-process` (`Destructive`, go-ahead per run) returns the process id; otherwise `SysProcessLog` filtered by `SysSchema.Name = '<code>'`, ordered by `StartDate` desc, top 1 | the instance id |
| EV-2 | after | `SysProcessLog` by `Id`: `Status.Name, StartDate, CompleteDate` | a root row without `CompleteDate` is parked or running, not hung; the root row is not the evidence |
| EV-3 | after | `SysProcessElementLog` filter `SysProcess = <instance>`: `Caption, SchemaElementUId, Status.Name, StartDate, CompleteDate, ErrorDescription` | **the evidence**: each file element Completed, its successor present; an error text verbatim |
| EV-4 | multi-instance | `SysProcessLog` filter `SysSchema.Name = '<callee>'` and `Id` not in the EV-0 child baseline (the parent link column is confirmed on the first run) | the iteration count |
| EV-5 | after | EV-0's root with `Id` NOT IN the baseline: `Id, Name, Size, Version, Type.Name, CreatedOn` (+ `RecordId, RecordSchemaName, SysFileStorage.Name` on SysFile; + `Tag` for ST-06) | the files written, their link and their storage |
| EV-6 | after | source rows again (`Name, Size`), compared with EV-5 | Names and Sizes equal, new Ids, sources unchanged |
| EV-7 | while parked, then after | `SysProcessFile` filter `SysProcess = <instance>`: count, `Name` | temporary report files exist only while the instance lives (the instance id is confirmed against `SysProcessData` on the first run) |
| EV-8 | after (report) | EV-5's `Name` against the TC-54 formula | the name rule |
| EV-9 | after | the user opens the record page | the files show in the attachment list |

Exclude configuration-log noise from SysFile reads (`RecordSchemaName <> 'ConfActivityLog'`, C7).

### 9.2 Recording

Record every scenario in the PR description:
- the observation level it reached;
- the instance id;
- EV-3 status per element, and EV-5 row counts against the expected counts;
- verbatim error text;
- the package version from `list-packages`.

A scenario that stopped at Stored or Design time must say so. That is not a Runtime PASS.

### 9.3 Cleanup (writes; ask first)

1. Delete the created file rows and the fixture records with ONE `execute-dataservice-batch` of DeleteQuery items.
   The stand rejects HTTP DELETE.
2. Delete the scenario processes with `delete-schema` and a CLI `--timeout`: a remote delete takes about 6 minutes
   on this stand.
3. Re-run EV-5 to confirm that the rows are gone.

## 10. Conventions (AGENTS.md "Test style policy", applied to all three repositories)

| Rule | How it applies here |
|---|---|
| AAA | explicit `// Arrange`, `// Act`, `// Assert`; a precondition assert in Arrange whenever the arrange could make the test pass vacuously (the TC-01 count, the "starts unconfigured" check of `UserTaskElementHandlerCreateTests`) |
| `because` | on every assertion: it says what breaks for the user, not what the code does |
| `[Description("...")]` | on every test method: the behaviour and why it matters, including the decision id |
| Categories | PBT `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`; clio `[Category("Unit")]` + `[Property("Module", ...)]`; e2e `[Category(McpE2ECategories.ProcessDesigner)]` |
| Cross-platform | no OS paths in tests (ancestor search for captures); runs on Windows, Linux and macOS (dev-n8 on macOS) |
| Names | `Member_Should<Outcome>_When<Condition>`, as the neighbouring fixtures do |

A sketch of a package test in the house style (C# 7.3):

```csharp
[Test]
[Description("D16 empty-filter policy: an Object element that SAVES with no record scope and no filter condition is refused at the END of the request, so a setFilter later in the same batch still counts; without the refusal it copies up to 50 arbitrary files onto the target and reports success.")]
public void Modify_ShouldRefuseSavingObjectElement_WhenNoSelectingFilterAtEndOfRequest() {
	// Arrange
	UserConnection.RegisterFileTaskSchemas();
	ProcessSchema schema = CreateSchema();
	ProcessSchemaUserTask element = FileProcessingTestSupport.AnObjectFileElement(schema, "OF1");
	MockEntitySchemaWithColumns("ContactFile", ContactFileColumns);
	element.Parameters.FindByName("ResultActionType").SourceValue.Source.Should().Be(
		ProcessSchemaParameterValueSource.None, because: "the element must start unconfigured, or the test is vacuous");

	// Act
	Action act = () => Apply(schema, SetElement("OF1", "{attachments: {object: 'Contact'}, saveTo: {...}}"));

	// Assert
	act.Should().Throw<ArgumentException>(
			because: "a saving element without a selecting filter copies arbitrary files with success")
		.WithMessage("*attachments.recordId*setFilter*");
}
```

## 11. Traceability: acceptance criteria to tests

The criteria are the proposed replacement texts in [decisions](eng-92719-file-processing-element-decisions.md)
Part D, labelled by bullet in order: FE = D-2 (ENG-92719 File processing element), OA = D-3 (ENG-96505 Element
readiness and object attachments mode), RP = D-4 (ENG-96506 Generated report + process parameter modes), SF = the
SysFile row of D-5. D-3 has 13 bullets: OA-1..OA-5 are bullets 1-5, OA-6 covers bullets 6 and 7 (object by name;
SysFile not part of this sub-task), and OA-7..OA-12 are bullets 8-13. These labels are not the plan's section 1.2 ids
(AC-OA1..AC-OA9, AC-RP1..AC-RP9), which number the Jira text as written. The ENG-95984 File process parameter type
criteria (AC-1..AC-10 of its plan §1.4) are traced in its own
[test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md).

| AC | Short | Tests |
|---|---|---|
| FE-1 | three variants + save target via create/modify, describe | TC-06, TC-46, TC-52, TC-60, TC-62, TC-81; ST-01, ST-04, ST-06, ST-08 |
| FE-2 | an output mapped by more than one downstream element | TC-35, TC-75; ST-05 |
| FE-3 | guidance: variant per intent, pairings, never create a printable | TC-88, TC-51 |
| FE-4 | values through the ENG-91844 Implement full parameter mapping (sources) value sources, incl. File / FileCollection | TC-18, TC-28, TC-52, TC-62, TC-64; ST-02, ST-03 |
| FE-5 | serialization per variant and per storage | TC-01, TC-02, TC-37..TC-41, TC-59, TC-69, TC-81; DT-02, DT-04 |
| FE-6 | the five buildable patterns tested; docs and MCP | TC-70..TC-75, TC-90; ST-02, ST-05, ST-06 |
| OA-1 | added and connected, drawn as the designer draws it | TC-06, TC-49, TC-86 |
| OA-2 | describe round trip; existing elements; other variants preserved; SysFile described, change refused | TC-21, TC-29, TC-42..TC-48 |
| OA-3 | an output mapped by more than one downstream element | TC-35, TC-72; ST-02 |
| OA-4 | identity per variant; the group is the variant | TC-01, TC-04, TC-05, TC-06, TC-08, TC-14 |
| OA-5 | the generic route refused for the Object schema | TC-12, TC-89 |
| OA-6 | object by name, storage derived; SysFile refused | TC-15..TC-17 |
| OA-7 | `attachments.recordId` value sources; numberOfRecords; one sort column | TC-18..TC-24, TC-70 |
| OA-8 | save to a target record; a constant must exist | TC-25..TC-28; ST-01 |
| OA-9 | empty-filter refusal / notice | TC-22 |
| OA-10 | parity with SkillFilesValidationProcess | TC-41; DT-02 |
| OA-11 | guidance | TC-88 |
| OA-12 | coordinated change | TC-82..TC-84, TC-87, TC-89, TC-90; ST-01, ST-02; DT-01, DT-02 |
| RP-1 | printable by templateId / caption / macro; record via `report.recordId`; FastReport / DevExpress refused | TC-50..TC-52, TC-60; ST-06 |
| RP-2 | saved, or handed on (temporary) | TC-55, TC-73; ST-06, ST-07 |
| RP-3 | never create or pick an unrelated printable | TC-50, TC-51 |
| RP-4 | a Word report with no scope refused | TC-56 |
| RP-5 | `files` sources, both levels; single File pending M1 | TC-62..TC-65, TC-91; ST-03, ST-04 |
| RP-6 | saved only; copies available; "use in process" refused | TC-63, TC-66, TC-67; ST-04 |
| RP-7 | describe round trip; shipped product processes describe as fileProcessing | TC-44, TC-45, TC-60, TC-61, TC-68 |
| RP-8 | parity with PrintInvoiceReport and FileParameterProcess | TC-59, TC-69; DT-03 |
| RP-9 | guidance pairings | TC-74, TC-88 |
| RP-10 | coordinated change | TC-84, TC-87, TC-89, TC-90 |
| SF-1 | SysFile read and save for all variants, after M7/M8/M21/M23/M25 | TC-76..TC-81; ST-08; DT-04 |

## 12. Deliberately not covered

- **Running the user tasks in a unit test.** That is the platform's suite (section 4). Only the stored shape is ours.
- **The platform's pre-save validation.** It needs a deployed Creatio; PBT substitutes it as `IProcessSchemaValidator`.
  An empty required `Files` or `TargetEntitySchemaUId` is refused by our binder before the platform sees it.
- **Outer-only `Files` at run time (M5).** The builder refuses it (TC-66).
- **FastReport and DevExpress at run time.** There are no printables and no packages on the stand (measured
  2026-10-01). These are refused, unit-only (TC-51).
- **Process file -> Send email, and -> Creatio.ai.** Send email is SE's test plan. Creatio.ai is out of scope (D22).
- **The designer's own defects** (the SysFile record-object sort list, H-G3-1). They are observed in DT-01 and M11,
  and reported, not fixed here.
- **Concurrency of two runs of one ST process,** and record order inside a multi-record report. The platform
  specifies neither, and the guide says the order is unspecified.
