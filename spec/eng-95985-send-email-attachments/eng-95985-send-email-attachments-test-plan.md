# ENG-95985 Send email attachments: test plan

Written 2026-10-28. Nothing here was built, run or written to the stand, Jira, GitHub or GHE.

Basis: crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125), clio `origin/master` `358e9db52`,
clio-knowledge `origin/master` `534e913` (`libraryVersion` 1.15.173), CrtProcessDesigner 7.8.0 (PackageStore), core
`TSBpm` trunk. The plan follows the AGREED answers of the decisions file (Q1-Q20, all agreed by the owner on
2026-10-28; Q9, Q16 and Q17 differ from the first recommendation); a row that names a Q follows that answer.
Measurement ids EM0-EM15 are the ones the measurements file defines (EM6 dropped by Q17, EM12 by Q9); procedures live
there, not here.

Sibling files: [decisions](eng-95985-send-email-attachments-decisions.md) ·
[design](eng-95985-send-email-attachments-design.md) · [plan](eng-95985-send-email-attachments-plan.md) ·
[measurements](eng-95985-send-email-attachments-measurements.md) · [README](README.md).

| Path alias | Root |
|---|---|
| `PB/` | `packages/CrtProcessBuilder/Files/src/cs/` (crt-process-builder repository) |
| `PBT/` | `tests/UnitTests/CrtProcessBuilder.Tests/` (crt-process-builder repository) |
| `CLIO/` | the clio repository root (`clio/`, `clio.tests/`, `clio.mcp.e2e/`) |
| `KB/` | the clio-knowledge repository root (`guidance/`, `automation/Clio.Knowledge.Bundle.Tests/`) |
| `PD/` | `CrtProcessDesigner/branches/7.8.0/Schemas/` (PackageStore repository) |
| `PS/` | the PackageStore repository root (shipped corpus) |
| `TS/` | `TSBpm/Src/Lib/` (core repository) |
| `UT/` | the platform `UnitTests` repository root (platform configuration tests; reference only, not ported) |

Test ids: **TC-U-nn** package unit (PBT), **TC-P-nn** package parity (PBT), **TC-C-nn** clio unit (`clio.tests`),
**TC-I-nn** clio MCP e2e (`clio.mcp.e2e`, on stand `Creatio`), **TC-K-nn** clio-knowledge tests and gates,
**TC-01..TC-10** manual Jira cases (section 8), **EM0-EM15** stand measurements (measurements file).
"Existing" means the test is already in the tree and is only re-run or extended with rows.

---

## 1. Coverage matrix

| Item | What must hold | Package unit | Parity | clio unit | e2e | KB | Stand | Manual |
|---|---|---|---|---|---|---|---|---|
| **AC1** block + describe read-back | `email.attachments` builds one designer row per entry on build, `addElement`, `setElement`; describe returns the list in the write shape | TC-U-01..07, 09..14, 19, 59..63, 69, 74 | TC-P-03, 08 | TC-C-01..03, 07..11 | TC-I-01, 06, 09 | TC-K-01 | EM2 (save, pre-save gate, stored bytes at the STOP gate; describe steps on the final-cut re-run), EM3 | TC-10 |
| **AC2** the Process file variants as sources; one collection feeds several emails | Object, Report, Process-parameter variant, process FileCollection (Q2); a collection read by two Send email elements | TC-U-09, 10, 15, 20 | TC-P-03, 04 | - | TC-I-01, 02, 03 | TC-K-06 | EM4, EM4c (FileCollection passed from a parent), EM11, EM14 | TC-01, 02, 03, 06 |
| **AC3** more than one attachment | N slots on one element; a slot with N rows | TC-U-14, 25, 28 | TC-P-05, 06 | TC-C-09, 14 | TC-I-01, 05 | TC-K-01 | EM4 | TC-07 |
| **AC4** manual/auto x custom/template | identical slots in all four combinations; a mode switch keeps them | TC-U-16, 17 | TC-P-03 (4 corpus combinations), 07 | - | TC-I-03, 10 | TC-K-06 | EM4, EM5, EM7 (real delivery on stand `Creatio` once the owner says its mailbox works; Q17), EM15 | TC-01, 02, 04, 05 |
| **AC5** clear error naming the source | every refusal of design §5.1; element untouched; nothing created | TC-U-31..41, 54, 57, 76, 78, 78a, 78b, 80 | - | TC-C-02 | TC-I-04 | TC-K-01 | - | TC-09 |
| **AC6** ordering rule | guidance states configure-first and flow-before; the build path refuses a later-declared source; build and modify warn on a source that cannot reach the email | TC-U-54..58, 77 | - | - | TC-I-04 (later-declared row) | TC-K-03 | EM10 | - |
| **AC7** parity, tests, docs, MCP | builder slot = designer slot under the comparison rule; designer display values on slots only; clio surface aligned; e2e with Ignored = 0 | TC-U-03, 04, 13, 69, 70, 73 | TC-P-01..09 | TC-C-04..06, 16..22 | all TC-I (stored bytes in TC-I-01, 05, 06, 08) | TC-K-07..12 | EM1, EM2, EM3 | - |
| **Q3** single File accepted, warned | item-only binding; notice that an empty File fails the element | TC-U-11, 43 | TC-P-04 (File row), 09 | - | TC-I-01 (Doc row + warning) | TC-K-06 | EM8 | TC-10 |
| **Q4** replace semantics | matched slots kept as they are (one-to-one matching; designer odd shapes never match); unmatched removed, refused while something reads the slot; the removal notice carries each removed entry in the write shape; `[]` removes all; absent untouched; duplicates warned | TC-U-21..30, 44, 45, 76 | - | TC-C-08, 12..15, 23 | TC-I-05, 06 | TC-K-04 | EM1 (removed-slot resource keys) | TC-08 |
| **Q5** lowest free number | new slot takes the lowest k >= 1 that NO parameter of the element is named `Attachments<k>`, counted over every parameter of the element whatever its stamp | TC-U-02, 25 | TC-P-06 | - | TC-I-05 | - | EM1 | - |
| **Q6** AC5 meaning (C) | refuse no / unknown / fileless / nested / own / later-declared source; WARN on build and modify when the source cannot reach the email along sequence flows | TC-U-31..41, 47, 54..57, 77, 79 | - | - | TC-I-04 | TC-K-03 | EM10 | TC-09 |
| **Q8** every non-block mapping onto a slot refused | `addMapping`, build `mappings[]` (also onto a slot not built yet) and a non-block `BindCollection` are refused naming `email.attachments`; the block's own writes pass; `TakesFiles` unchanged; other `Attachments`-named parameters as before; describe's generic `parameters[]` offers no re-appliable names for any name the guard predicate `IsReservedName` matches | TC-U-49, 50, 52, 53, 74, 75 | - | - | TC-I-07 | - | EM9 | - |
| **Q9** no build-time check; KB troubleshooting line (agreed) | no feature reader and no feature-off notice (`UseProcessEmailAttachments` is a rollout toggle, on everywhere since 7.17.1); the article carries one troubleshooting line: "an email without its files: check feature UseProcessEmailAttachments for the user who runs the process" | - | - | - | - | TC-K-05 | - | - |
| **Q10** mapping FROM a slot refused | names the slot, says it only receives files; `BindCollectionItems` included; a Formula body too (addendum) | TC-U-51, 83 | - | - | - | - | - | - |
| **Q11** describe shape | write-shape entries + `slot`, `bindingLevels`, `issues`, in stored order (SQ10); null when none | TC-U-59..66, 82 | - | TC-C-03, 09..11 | TC-I-01, 06, 09 | TC-K-04 | EM2 (describe steps, final-cut re-run) | TC-10 |
| **Q16** multi-instance Send email refused (agreed) | `email.attachments` on a Send email whose activity carries `MultiInstanceOptions` (a designer conversion) is refused, element untouched; describe reports such an element as found, with an issue; the per-recipient multi-instance Sub-process pattern is documented only if EM8 passes | TC-U-80, 81 | - | - | - | TC-K-13 | EM8, EM11 | - |
| **Q20** unscoped Process file source refused (agreed) | Object attachments AND Generated report with no record scope / no selecting filter are refused as attachment sources, judged at the end of the request (files are read with system rights and sent out); files handed across a Sub-process boundary get a notice on each side | TC-U-78, 78a, 78b, 78c, 91, 92 | - | - | TC-I-04 (unscoped row) | TC-K-06 | - | - |
| **G3** modify-as-new-version | the clone keeps the slots owned by the new version | TC-U-71 | - | TC-C-06 | TC-I-08 | - | EM13 | - |

---

## 2. Package unit tests (PBT)

### 2.1 Harness rules

| Rule | Why | Evidence |
|---|---|---|
| Every new fixture is `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` and inherits `BaseComposableAppTestFixture` | CI selects `TestCategory=PreCommit\|TestCategory=Integration`; `CiContractGuardTests.EveryFixture_CarriesACategoryTheCiFilterSelects` fails an uncategorised fixture | `PBT/CiContractGuardTests.cs:55-65` |
| The schema comes from `UserConnection.CreateSchema()` (a REAL `UId`), never from `new ProcessSchema(...)` | `SendEmailApplierTests.CreateSchema()` builds a schema with `UId == Guid.Empty` (`PBT/SendEmailApplierTests.cs:47`), so any `CreatedInSchemaUId == schema.UId` assertion there passes vacuously; the stamp IS the run-time discriminator | `PBT/FileProcessingTestSupport.cs:222-230`; `TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:78-94` |
| Every stamp test starts with a precondition assert `schema.UId.Should().NotBe(Guid.Empty, because: "...vacuous...")` | guards a future harness change from making the test vacuous again | house rule (precondition asserts in Arrange) |
| Tasks are registered by ONE call: `UserConnection.RegisterFileTaskSchemas(("EmailTemplateUserTask", emailTaskUId))` in `[SetUp]`, not `SetupEmailTaskSchema()` plus a second manager | each `SetupProcessUserTaskSchemaManager()` installs a NEW manager; registering one task per call leaves only the last one resolvable | `PBT/FileProcessingTestSupport.cs:359-390` (doc comment) |
| Source elements from `AnObjectFileElement` / `AReportFileElement` / `AProcessFileElement` (real task UIds, literal templates) | outputs `ObjectFiles.File`, `ReportFiles.File`, `ObjectFiles.ObjectFile` exactly as the platform declares them | `PBT/FileProcessingTestSupport.cs:314-326` |
| `[MockSettings(RequireMock.DBEngine)]` on fixtures that resolve sources or describe | the locator and the describer read through `DBExecutor` | `PBT/SendEmailElementHandlerDescribeTests.cs:26`, `PBT/FileCollectionMappingTests.cs:34` |
| The mapping service comes from `ProcessDesignTestSupport.CreateMappingService` | the single constructor site of `ProcessMappingService` | `PBT/ProcessDesignTestSupport.cs:304-307` |
| Platform round trip: `WriteMetaData(JsonDataWriter)`, then `ReadMetaData` after ONE `reader.Read()` | a `ReadInto()` silently returns defaults | `PBT/ProcessParameterServiceItemPropertiesTests.cs:56-84` |
| "Untouched" is asserted on bytes: the element's `WriteMetaData` JSON before == after | a field-by-field check misses a slot added and removed, or a re-stamped item | pattern of `PBT/FileCollectionMappingTests.cs` PU-44 |
| No PBT test subclasses or names a configuration user-task class (`EmailTemplateUserTask` and friends) | the dev-n8 build compiles without `Terrasoft.Configuration.dll`; UT patterns are references only | `UT/ProcessDesigner.UnitTests/BaseEmailTemplateUserTaskProviderTestCase.cs` (reference) |

**8.3.4 compile caveat (column CI below).** CI compiles PBT against the 8.3.4 core-bin while the main checkout's
core-bin is 10.1.37, so "green locally" can be "does not compile in CI". Two markers:

- **lit**: the test pins a data value type. It uses literals, FileLocator `a33c9252-d401-453e-949d-169157067ed9` and
  CompositeObjectList `651ec16f-d140-46db-b9e2-825c985a8ac2`, as `PBT/FileProcessParameterTests.cs:53` does. The
  constants themselves DO compile on 8.3.4: production code uses both (`PB/Mappings/CollectionBindingRules.cs:140, 144`)
  and `PBT/ParameterTypeCompatibilityTests.cs:163` already cross-checks the literal against
  `DataValueType.FileLocatorDataValueTypeUId`. The literal is kept as the INDEPENDENT oracle: a test that compares the
  writer's type with the same constant the writer uses would pass if both were wrong.
- **probe**: the test calls a platform member whose presence on 8.3.4 is not verified. The first CI run of the draft
  PR decides; the fallback is named in the row.

Run: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf` (Windows, what CI
runs) and `-c dev-n8`. The worktree has no `.application`: junction it from the main checkout first
(`docs/running-tests-outside-the-main-checkout.md`). Iterating: add
`--filter "FullyQualifiedName~SendEmail|FullyQualifiedName~EmailAttachment|FullyQualifiedName~FileSourcePlanner"`;
before every commit run the whole project on both configurations.

### 2.2 New fixtures

| Fixture | Component (design §4.1) | Harness specifics |
|---|---|---|
| `EmailAttachmentSlotTests` | C3 slot predicate, names, writer | no DB; `CreateSchema()`; a hand-built Send email element |
| `SendEmailAttachmentsApplierTests` | C4 sources, shape, modes, routes | DBEngine mock; file task schemas + `EmailTemplateUserTask`; process parameters `Docs` (FileCollection), `Doc` (File) declared through the parameter service |
| `SendEmailAttachmentsReplaceTests` | C4 reconcile (Q4, Q5) | as above; slots pre-built through the block, or by hand for designer shapes; the dependency snapshot captured before the request (`IProcessDependencySnapshot.FindDependents` throws before `Capture`, `PB/Graph/IProcessDependencySnapshot.cs:48-58`) |
| `SendEmailAttachmentsRefusalTests` | §5.1 refusals (Q6, Q16) | as above; one byte snapshot of the element per test |
| `SendEmailAttachmentsNoticeTests` | §5.2 notices (Q3, Q4, Q6) | notices read from the request's `IProcessDesignNotices` |
| `SendEmailAttachmentsRawMappingTests` | C5 slot guards in `ProcessMappingService.Plan` and beside `EnsureNotReadFromAFilesInput`; the block form of `BindCollection` (Q8, Q10) | `CreateMappingService`; DBEngine mock |
| `SendEmailAttachmentsDescribeTests` | C7 describe (Q11, Q16) | DBEngine mock; `CreateDescriber` (`PBT/BaseComposableAppTestFixture.cs:256-270`) |
| `FileSourcePlannerCharacterizationTests` | C2 refactor proof | written on `main` BEFORE the lift; drives `fileProcessing.files` only |
| rows in `ProcessBuildHandlerTests`, `ProcessGraphBuilderTests`, `ProcessModifyHandlerTests`, `ProcessDesignServiceWireContractTests`, `CrtProcessBuilderAppTests`, `ProcessVersionCloneFactoryTests` | C6, C1, DI, clone | existing harness of each file |

### 2.3 Slots (C3) - `EmailAttachmentSlotTests`

| TC | Test | Proves | CI |
|---|---|---|---|
| TC-U-01 | `IsSlot_ShouldMatchExactlyWhatTheRunTimeReads` [TestCase `Attachments1` true, `Attachments12` true, `attachments1` false, `Attachments` false, `AttachmentsNote` false, `Attachments1FileLocator` false; a matching name stamped by ANOTHER schema false; a matching name nested under another parameter false] | the read/write predicate `IsSlot` = the run-time filter (the guard predicate `IsReservedName` is TC-U-50, TC-U-75): anchored, case-sensitive `^Attachments\d+$`, top level, `CreatedInSchemaUId` = this process (`TS/.../ProcessUserTaskExtensions.cs:19, 78-94`) | - |
| TC-U-02 | `NextFreeNumber_ShouldTakeTheLowestFreeNumberFromOne` [none -> 1; 1,2 -> 3; 1,3 -> 2; 2,3 -> 1; 1 + `AttachmentsNote` -> 2; an inherited `Attachments1` stamped by another schema (`A3` foreign) -> 2; 1, 2 and an item named `Attachments3` nested under another parameter -> 4] | Q5 designer parity: the lowest k >= 1 such that NO parameter of the element is named `Attachments<k>`, counted over every parameter of the element whatever its `CreatedInSchemaUId` (ordinal; design 4.3). The designer's `generateItemUniqueName` scans the element's `processElement.parameters` (`PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:244-260`, `PD/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:465-479`), a flat collection of roots and nested items while the client feature `ProcessParameterCollections` is on, roots only otherwise (serialization 5.1), so a top-level non-slot blocks its number there too; counting nested items gives the same numbers as either collection in practice (item names end in `FileLocator`; the nested-item row can only skip a number, never reuse one) | - |
| TC-U-03 | `Create_ShouldBuildTheDesignersTwoLevelShape` | outer: name, CompositeObjectList, Direction `In`, `ContainerUId` = the element, caption "Attachments collection"; exactly ONE item: `<slot>FileLocator`, FileLocator, `In`, `ContainerUId` = the element, nested, caption "Attachment" (design §3 invariants) | lit |
| TC-U-04 | `Create_ShouldStampBothLevelsWithTheProcess` | `CreatedInSchemaUId` = `ModifiedInSchemaUId` = schema UId on the outer AND the item, set explicitly (the item joins its parent before the parent joins the schema, so `MetaItemCollection.InsertItem` never back-fills it) | - |
| TC-U-05 | `Create_ShouldNameTheItemAfterItsSlot` [1, 7, 12] | the run time reads the row key `<slot>FileLocator` (`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-119`); a mismatch drops the slot silently | - |
| TC-U-06 | `Remove_ShouldRemoveBothLevels` | the outer and its item leave `element.Parameters` (`Parameters.Remove`, as every removal in the package does); nothing else on the element changes (bytes). The package deletes no `SysLocalizableValue` row: whether a save drops the removed slot's resource keys is EM1/EM2, not this test | - |
| TC-U-07 | `Find_ShouldListSlotsInNumericOrder_AndSkipEverythingElse` [`Attachments10`, `Attachments2`, `AttachmentsNote`, a foreign-stamped `Attachments3`] | numeric, not lexicographic order (2 before 10); non-slots skipped | - |
| TC-U-08 | `RunTimeFilter_ShouldReturnExactlyTheBuiltSlots` | the platform's own `GetFilteredOwnParametersList("Attachments")` on a substituted `ProcessUserTask` whose element is the builder's returns exactly the built slots. Feasibility-gated: needs `TestProcess` from `Libs/Terrasoft.TestFramework.dll`; if it cannot be reached, the test replicates the predicate and says so in its `[Description]` | probe |

### 2.4 Sources, shape, modes, routes (C4) - `SendEmailAttachmentsApplierTests`

| TC | Test | Proves | CI |
|---|---|---|---|
| TC-U-09 | `Apply_ShouldBindBothLevels_FromACollectionSource` [OF1.`ObjectFiles` -> item `File`; RF1.`ReportFiles` -> `File`; PF1.`ObjectFiles` -> `ObjectFile`; process `Docs` -> `Docs.File`; a sub-process FileCollection output -> its file item] | design §3.1 row 1: outer bound to the collection, item bound to the collection's FileLocator item FOUND BY TYPE (the Process-parameter variant's item is `ObjectFile`, not `File`) | lit |
| TC-U-10 | `Apply_ShouldBindParentAndItem_FromACollectionItemPath` [`OF1.ObjectFiles.File`, `Docs.File`, `PF1.ObjectFiles.ObjectFile`] | design §3.1 row 2: the same stored shape as TC-U-09's row for the same collection | - |
| TC-U-11 | `Apply_ShouldBindTheItemOnly_FromASingleFile` [process File `Doc`; an element's File output] | Q3: outer stores nothing (`IsNothing`), item bound to the File. If EM8 shows the item-only row does NOT attach, Q3 falls back to B and this row becomes a refusal ("wrap it in a FileCollection") | - |
| TC-U-12 | `Apply_ShouldWriteTheExpressionOntoTheItemOnly` | outer stores nothing, item carries the formula as given | - |
| TC-U-13 | `Apply_ShouldStoreScriptBindingsStampedWithTheProcess` | every bound level: Source `Script`, value `[#...#]`, `SourceValue.ModifiedInSchemaUId` (GS5) = schema UId; the pre-save gate registers only Script sources; GS5 = process UId is designer/corpus parity, and the compiled-code generator skips an element parameter whose GS5 is another schema (`TS/Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:296-298`). Not `UseOnlyModifiedParameters`: that filter applies only to the sub-process start reader (`TS/Terrasoft.Core/Process/ProcessComponentSet.cs:1292-1297`); the multi-instance count reader returns before it (`TS/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:734-737`) | - |
| TC-U-14 | `Apply_ShouldCreateOneSlotPerEntry_InEntryOrder` [1, 3, 5 entries] | AC3: `Attachments1..N`, entry i -> slot i on a fresh element | - |
| TC-U-15 | `Apply_ShouldLetOneCollectionFeedTwoSendEmailElements` | AC2: SE1 and SE2 both bound to OF1.`ObjectFiles`; OF1 unchanged (bytes); reading a collection is side-effect free | - |
| TC-U-16 | `Apply_ShouldBuildIdenticalSlots_InEveryModeAndMessageSource` [manual+custom, manual+template, auto+custom, auto+template] | AC4: the slots' bytes (UIds and stamps normalised) are equal across the four; no mode-specific code | - |
| TC-U-17 | `SetElement_ShouldKeepTheSlots_WhenOnlyTheModeOrMessageSourceChanges` [manual -> auto; custom -> template] | AC4: `ApplyTemplateMode` and the mode switch do not touch dynamic slots (bytes of every slot unchanged) | - |
| TC-U-18 | `Apply_ShouldCountAttachmentsAsConfiguration` [only `attachments`; only `[]`] | `EnsureConfiguresSomething` (`PB/Email/SendEmailApplier.cs:203`) accepts a block that only carries attachments | - |
| TC-U-19 | `Apply_ShouldBuildTheSameSlots_OnEveryRoute` [build `elements[].email`, `addElement`, `setElement`] | AC1: the three routes reach one applier (`PB/Design/ProcessBuildHandler.cs:557-560`, `CreateProcessOperations`) and store equal bytes | - |
| TC-U-20 | `Apply_ShouldBindAnObjectSourceWithSysFileStorage_AsAnyOther` | an Object element whose record object keeps files in SysFile feeds a slot exactly like a dedicated-storage one; unit side of EM14 | - |
| TC-U-73 | `Apply_ShouldWriteTheDesignersDisplayValues_OnSlotsOnly` [collection source: outer `[#<OF1 caption>.Collection of files#]`, item `[#<OF1 caption>.Collection of files:File#]`; single File: item `[#<Doc caption>#]`, outer none; a non-slot mapping on the same element keeps its bare caption; only if EM1/EM2 show a removed slot's key surviving: a reused or unbound outer gets an explicit empty DisplayValue] | design §3 (serialization AD1/AD2): the slot writer sets the designer form after binding, on slots only; `SourceDisplayValue` keeps the bare caption for every other top-level source (`PB/Mappings/ProcessMappingService.cs:963-967`). Pattern `PBT/MappingNestedDisplayValueTests.cs:60-77` | - |

### 2.5 Replace semantics (Q4, Q5) - `SendEmailAttachmentsReplaceTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-21 | `SetElement_ShouldKeepAMatchedSlotAsItIs` | a slot whose stored binding equals an entry's planned binding keeps name, both UIds, resource rows: element bytes for that slot unchanged |
| TC-U-22 | `SetElement_ShouldRemoveEveryUnmatchedSlot` | slots no entry matches are removed with both levels (TC-U-06) |
| TC-U-23 | `SetElement_ShouldRemoveEverySlot_WhenTheListIsEmpty` | `attachments: []` leaves no slot; the rest of the element is unchanged (bytes) |
| TC-U-24 | `SetElement_ShouldLeaveSlotsUntouched_WhenAttachmentsIsAbsent` | a `setElement` that changes only the subject leaves every slot byte-identical |
| TC-U-25 | `SetElement_ShouldGiveANewEntryTheLowestFreeNumber_AfterAGap` | slots 1 and 3 kept, a new entry becomes `Attachments2` (Q5) |
| TC-U-26 | `SetElement_ShouldMatchOnTheResolvedBinding_NotOnTheSpelling` [`OF1.ObjectFiles` vs `OF1.ObjectFiles.File`; upper- vs lower-case GUID inside an expression is out of scope] | matching compares planned bindings (resolved meta paths), as the mapping service does, not raw entry text |
| TC-U-27 | `Apply_ShouldBeIdempotent_WhenTheSameListIsSentTwice` | the second apply writes nothing and raises no notice |
| TC-U-28 | `Apply_ShouldCreateTwoSlots_WhenTwoEntriesNameTheSameSource` [fresh element; an element that already has one slot reading that source] | duplicates are allowed (designer parity) and produce two slots; matching is one to one, so the existing slot is kept for one entry and a new slot is made for the other; the notice is TC-U-44 |
| TC-U-29 | `SetElement_ShouldNotTouchASlotStampedByAnotherSchema` [list `[]`; list with one new entry] | a foreign-stamped `Attachments2` is not a slot (TC-U-01): never removed, never renumbered over (TC-U-02), described with an issue (TC-U-64) |
| TC-U-30 | `SetElement_ShouldReplaceADesignerShapeThePackageNeverWrites` [outer-only slot; stale parent over a single File; outer and item from different sources] | the stored binding matches no planned binding, so the old slot is removed and a new one written in the package's shape; the removal notice names it |
| TC-U-76 | `SetElement_ShouldRefuseToRemoveASlot_ThatSomethingStillReads` [a parameter of another element mapped from `SE1.Attachments2`, written by hand past the C5 guard] | before a removal the applier asks `IProcessDependencySnapshot.FindDependents(element, slot)` (`PB/Graph/IProcessDependencySnapshot.cs:57-58`) and refuses naming the dependents, as the designer's `canRemoveParameter` does (`PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:392-405`); element bytes unchanged, no notice |

### 2.6 Refusals (design §5.1, Q6, Q16) - `SendEmailAttachmentsRefusalTests`

Every row asserts three things: the exception text (by fragment: the element, `email.attachments[<i>]` with the
zero-based index as `ChangeAccessRightsApplier`'s `add[<i>]` labels and clio's key-check paths use, the source), the
element's bytes unchanged, and no notice recorded.

| TC | Test | Case |
|---|---|---|
| TC-U-31 | `Apply_ShouldRefuse_WhenAnEntryNamesNoSource` | `{}`; the sentence has NO "keeps the process from starting" clause (that consequence belongs to the Process file element) |
| TC-U-32 | `Apply_ShouldRefuse_WhenAnEntryNamesMoreThanOneSource` [parameter + element; parameter + expression; element + expression] | "must set exactly ONE of" |
| TC-U-33 | `Apply_ShouldRefuseAValue_NamingTheConstantForm` | "a file has no constant form" |
| TC-U-34 | `Apply_ShouldRefuseAnUnresolvableSource_NamingTheEntry` [unknown element; unknown element parameter; unknown item path; unknown process parameter; `sourceElement` without `sourceElementParameter`] | existing locator messages (`PB/ProcessSchemaElementLocator.cs:60-66, 247-252, 262-268`) prefixed with the entry path |
| TC-U-35 | `Apply_ShouldRefuseASourceThatHoldsNoFiles` [`RD.ResultCompositeObjectList`; a Text process parameter; a Lookup element output] | "holds no files ... the email would attach nothing" |
| TC-U-36 | `Apply_ShouldRefuseACollectionWithSeveralFileItems` | inherited from the shared planner (`PB/FileProcessing/ProcessFileProcessingBinder.cs:218-224`); not listed in design §5.1, so the review keeps or drops it and this row follows |
| TC-U-37 | `Apply_ShouldRefuseANestedCollection_WithTheSharedSentence` | `CollectionBindingRules.NestedSourceSentence` |
| TC-U-38 | `Apply_ShouldRefuseTheElementsOwnParameter` [`SE1.Attachments1`; `SE1.Attachments1.Attachments1FileLocator`] | "names the element's own parameter" |
| TC-U-39 | `Apply_ShouldRefuseAttachments_OnAnElementThatIsNotSendEmail` | the existing `EnsureSendEmailElement` refusal (a Read data element), counting attachments as configuration |
| TC-U-40 | `Apply_ShouldLeaveTheElementUntouched_WhenAnyEntryIsRefused` [refused entry first, middle, last; the same block also changes subject and recipients] | validate-all-before-write (PU-44 pattern): no slot, no subject, no recipient written |
| TC-U-41 | `Apply_ShouldNameTheEntryByItsPosition` [index 0, 2] | the path in the message is `email.attachments[0]` / `[2]` for the refused entry, never another entry's |
| TC-U-78a | `Apply_ShouldRefuseAnUnscopedReportSource` [RF1 MS Word with no selecting filter read by an attachment -> refused with the report wording naming SE1; RF1 with a filter -> builds; PF1 -> not judged] | Q20 (agreed for both variants): `ReportFileProcessingEmptyFilterRule` judges the tracked source |
| TC-U-78b | `Modify_ShouldJudgeTheSourceAtTheEndOfTheRequest` [one batch: setElement email.attachments reads OF1, then setFilter scopes OF1 -> builds; reversed order -> builds; clearFilter on OF1 in a later request while SE1 reads it -> refused] | Q20: the filter ledger runs `EnsureSelectingFilters` at the end of the request; the Object rule asks whether a Send email slot reads the element |
| TC-U-78c | `Describe_ShouldReportAnAttachmentThatReadsAnUnscopedSource` [designer-built SE1 + unscoped OF1 -> an issue on the entry; nothing refused on describe] | Q20 / design 4.7 issues |
| TC-U-78 | `Apply_ShouldRefuseAnUnscopedObjectAttachmentsSource` [OF1 with no record scope and no filter condition, in EITHER action -> refused; OF1 with a record scope -> builds; OF1 with a selecting filter -> builds; PF1 -> not judged] | Q20 / design 4.9: the run time reads attachment files with rights disabled and sends them out (`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:76-104`); "no scope" is the judgement D16 already uses for saving (`PB/FileProcessing/ObjectFileProcessingNoticeRule.cs:113` `SelectsFiles`, read by `ObjectFileProcessingEmptyFilterRule.cs:40`); the message names the entry, the source element and the missing record condition |
| TC-U-80 | `SetElement_ShouldRefuseAttachments_OnAMultiInstanceSendEmail` [a Send email converted the way the designer converts it: `MultiInstanceOptions` set and the root parameters moved into the `InputRecordCollection` / `OutputRecordCollection` item properties, a former `Attachments1` among them; `setElement` with one collection entry; with `[]`; the same block also changing the subject; control: a `setElement` with no `attachments` member is not refused by this rule] | Q16 (agreed): "attachments of a multi-instance Send email are not supported through this tool", naming the element; the whole member is refused, so the text carries no entry index. Detected by `MultiInstanceOptions` on the activity (`TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:122`), NOT by `CollectionBindingRules.IsMultiInstance`, which answers only for a Sub-process (`PB/Mappings/CollectionBindingRules.cs:194-195`). The package never makes a Send email multi-instance (`PB/Elements/MultiInstanceApplier.cs:1050-1058`), so only `setElement` on a designer-built element reaches it. The arrange mirrors `_fillCollectionParameters` (`TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-activity-schema.js:317-338`) |

### 2.7 Notices (design §5.2, Q3, Q4, Q6) - `SendEmailAttachmentsNoticeTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-42 | (removed: Q9 agreed 2026-10-28) | no build-time feature check, so no feature-off notice; the article's troubleshooting line is TC-K-05 |
| TC-U-43 | `Apply_ShouldWarnThatAnEmptySingleFileFailsTheElement` | Q3 text names the entry and the File; wording settled after EM8 |
| TC-U-44 | `Apply_ShouldWarnThatTheSameFilesAreAttachedTwice` | names both entry positions |
| TC-U-45 | `SetElement_ShouldNameEachRemovedSlotAndWhatItRead` | one notice per removed slot: the slot name and the removed entry in the WRITE shape (decoded source), so adding that entry back to the list restores it in one call |
| TC-U-46 | `Apply_ShouldNoticeThatAnExpressionIsNotChecked` | the shared planner's expression notice, re-worded for the slot item |
| TC-U-47 | `Notices_ShouldBeWarningsOfTheSavedEdit_Only` | notices land in the request's `IProcessDesignNotices`; a refused request carries none |
| TC-U-79 | `Notices_ShouldFireForEveryEntryOfASentList_KeptOrNew` [single File: new, and kept by a re-sent list; a duplicate pair, kept; an unreachable source, kept; the same element afterwards with no `attachments` member] | design §5.2 firing rules: the single-File, duplicate and reachability notices fire for every entry of a SENT list, kept or new; an absent `attachments` member raises none |
| TC-U-48 | (removed: Q9 agreed 2026-10-28) | no feature reader is built (design C8 removed) |

### 2.8 Slot guards (C5, Q8, Q10) - `SendEmailAttachmentsRawMappingTests`

There is no separate raw route: `ApplyMapping`, `ApplyBlockMapping` and `BindCollection` all plan in
`ProcessMappingService.Plan` (`PB/Mappings/ProcessMappingService.cs:61-86, 133-198`), and `fromBlock` is the only switch
(`BindCollection` passes `false` today, `:85`).

| TC | Test | Proves |
|---|---|---|
| TC-U-49 | `ApplyMapping_ShouldRefuseASlotTarget_NamingTheBlockMember` [target `Attachments1`, `Attachments1.Attachments1FileLocator`] x [source element output, process parameter, expression, constant] | Q8: refused whatever the source, the message names `email.attachments` and `setElement`; bytes unchanged |
| TC-U-50 | `ApplyMapping_ShouldRefuseASlotTarget_OnEveryNonBlockRoute` [`addMapping` op; build `mappings[]` onto `SE1.Attachments1` and onto its item; a non-block `BindCollection` onto a slot; a foreign-stamped `Attachments2`; the near-miss `attachments1`] | the guard sits in `Plan` in a new `if (!fromBlock)` block placed first, BEFORE `ResolveTargetParameter` (`PB/Mappings/ProcessMappingService.cs:135`), keyed on a Send email element and `EmailAttachmentSlot.IsReservedName` (a root name matching `^Attachments\d+$` case-insensitively, whatever its stamp; not `IsSlot`, TC-U-01). On build, `mappings[]` runs BEFORE `email` (`PB/Design/ProcessBuildHandler.cs:553-561`), so the slot does not exist yet: the row asserts the `email.attachments` sentence, not the locator's not-found refusal |
| TC-U-74 | `BlockWrites_ShouldPassTheSlotGuard` [the block form of `BindCollection` for a collection entry; `ApplyBlockMapping` for an item-only entry] | the applier's own writes plan with `fromBlock = true` and are not refused, while the same calls in the non-block form are (TC-U-50); the block form is documented on `IProcessMappingService` |
| TC-U-51 | `ApplyMapping_ShouldRefuseASlotAsTheSource` [`SE1.Attachments1`, `SE1.Attachments1.Attachments1FileLocator` into a FileCollection and a File process parameter; `BindCollectionItems` with a slot as the outer source; a build `mappings[]` entry reading `SE1.Attachments1`] | Q10: "only receives files". The guard sits beside `EnsureNotReadFromAFilesInput`, called right after `ResolveStructuredSource` on both paths (`PB/Mappings/ProcessMappingService.cs:110, 705`), so `BindCollectionItems` is covered too. On build the slot does not exist yet when `mappings[]` runs; the row asserts a refusal naming `SE1.Attachments1` and pins its text once P-C1 settles whether this guard keys on the name (design 4.5 says so only for the target guard) |
| TC-U-52 | `ApplyMapping_ShouldTreatOtherAttachmentsNamedParametersAsBefore` [a process parameter `Attachments1`; a sub-process input `Attachments1` whose callee has a File item; `AttachmentsNote` on SE1] | mirror of PU-42 (`PBT/FileCollectionMappingTests.cs:1682`): outer value only, no item, no notice, the same bytes as on 1.6.6.125 |
| TC-U-53 | `TakesFiles_ShouldStayFalse_ForASendEmailSlot` | Q8 option B was not taken: `CollectionBindingRules.TakesFiles` (`PB/Mappings/CollectionBindingRules.cs:164-181`) is unchanged for every element |

### 2.9 Order checks (C6, Q6 C, Q7) - rows in `ProcessBuildHandlerTests` / `ProcessGraphBuilderTests` / `ProcessModifyHandlerTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-54 | `BuildProcess_ShouldRefuseAnAttachmentReadingAnElementDeclaredLater` [collection source; collection item path; a later element's single File output] | the forward-reference sentence (`PB/Graph/ProcessGraphBuilder.cs:458-462`) names `email.attachments[<i>]` and both elements; the guard runs on PLANNED names, so the item-only single-File reference that `RefuseForwardReference` would miss (top-level scan, `:370-380`) is caught |
| TC-U-55 | `BuildProcess_ShouldBuildAnAttachmentReadingAnElementDeclaredEarlier` | mirror of `BuildGraph_WhenAConfiguredBlockBindsToAnEarlierElementOrItself_ShouldBuild` (`PBT/ProcessGraphBuilderTests.cs:289`) |
| TC-U-56 | `BuildProcess_ShouldNotOrderCheckAProcessParameterSource` | process parameters precede every element; no false refusal |
| TC-U-57 | `Modify_ShouldResolveAttachmentSourcesInOperationOrder` [`addElement OF1` then `setElement SE1`; reversed] | the modify path needs no declaration-order guard: in order it builds, reversed it is refused by name resolution, naming the entry |
| TC-U-58 | `ElementDescriptor_BlockInventory_ShouldMatchWhatTheForwardReferenceGuardKnowsAbout` (existing, `PBT/ProcessGraphBuilderTests.cs:415`) | extended so the email block's attachments are in the inventory; `templateEntity` stays out (Q7, follow-up) |
| TC-U-77 | `Apply_ShouldWarn_WhenTheSourceCannotReachTheEmailOnTheFlow` [build: OF1 declared before SE1 but wired after it; build: OF1 on a branch that never reaches SE1; modify: `setElement SE1` reading an OF1 that lies after SE1; OF1 before SE1 on build and modify -> no notice; a process-parameter source -> never judged] | Q6 C: a WARNING on both paths, never a refusal; it reuses the breadth-first `ProcessGraphBuilder.Traverse` (`PB/Graph/ProcessGraphBuilder.cs:2250-2270`, called at `:2228-2230`) over the schema's flows, which exist on build before the email pass (`PB/Design/ProcessBuildHandler.cs:166-172`); text names `email.attachments[<i>]`, the source and the element |

### 2.10 Describe (C7, Q11, Q16) - `SendEmailAttachmentsDescribeTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-59 | `Describe_ShouldReportEachSlotInTheWriteShape` [element collection -> `sourceElement` + `sourceElementParameter` (the collection); process FileCollection -> `processParameter`; single File -> `processParameter`, `bindingLevels` `itemOnly`; element File output; expression -> `expression`] | AC1: decoded with `FileProcessingBlockDescriber.DecodeValueSource`; `slot` and `bindingLevels` set; the entry is re-sendable as is |
| TC-U-60 | `Describe_ShouldListEntriesInTheirStoredOrder` [`Attachments10` stored before `Attachments2`] | stored (designer) order, not slot number (SQ10, revised 2026-10-29) |
| TC-U-82 | `Describe_ShouldListTheStoredOrder_AfterAGapAndAReAdd` [four rows, row 2 removed, a row re-added as `Attachments2`] | describe lists 1, 3, 4, 2 with each entry's own source - the order the email attaches - and the described list sent back changes nothing |
| TC-U-61 | `Describe_ShouldOmitAttachments_WhenTheElementHasNone` | null, not `[]` (like the other optional members) |
| TC-U-62 | `Describe_ShouldCountAttachmentsInConfigured` | the block's `configured` flag is true for an element that only has attachments |
| TC-U-63 | `DescribeThenReplay_ShouldChangeNothing` [five entries with a gap; read-back members sent back as described] | Q4 + Q11 modify recipe: build -> describe -> `setElement` with the described list writes nothing (element bytes equal) and raises only the notices design 5.2 gives KEPT entries (single File, formula, a sub-process output source; corrected 2026-10-29: "no notice" contradicted 5.2), never a removal notice; an entry describe reports with no source (formula on the collection level, foreign or moved slot) is refused on replay (pattern `PBT/ElementSourceDecodeTests.cs:775`) |
| TC-U-64 | `Describe_ShouldRaiseAnIssue_ForAStoredShapeThatFailsOrSurprises` [outer-only; item-only bound to a collection item; outer and item from different sources; stale parent over a single File (EM1d); nothing bound; slot stamped by another schema] | one issue per row of design §4.7 (its last two rows, the unscoped source and the multi-instance element, are TC-U-78c and TC-U-81), pinned by fragment; `bindingLevels` per row (`outerOnly`, `itemOnly`, `both`, `both`, `none`, as stored); wording of rows 1 and 4 settled after EM9 and EM1d |
| TC-U-65 | `Describe_ShouldRaiseNoIssue_ForEveryShapeThePackageWrites` [the shapes of TC-U-09..12] | no false alarm on the package's own output |
| TC-U-66 | `Describe_ShouldNotReportANonSlotAsAnAttachment` [`AttachmentsNote`] | only `EmailAttachmentSlot.IsSlot` parameters reach `email.attachments` |
| TC-U-75 | `DescribeParameters_ShouldNotDecodeASendEmailSlotIntoReAppliableNames` [a collection-bound slot; a single-File slot; a foreign-stamped `Attachments2`; the near-miss `attachments1`; another element's mapped parameter] | the non-decoding keys on the guard predicate `EmailAttachmentSlot.IsReservedName`, not `IsSlot`, so the foreign-stamped and near-miss names are not decoded either; the generic element `parameters[]` (`PB/Describe/ProcessDescriber.cs:197-218`, decoded by `ProcessParameterService.DecodeElementSource`) reports a slot and its item raw, with no `sourceElement` / `processParameter` names that a re-sent `addMapping` would carry into the C5 refusal; the other element decodes as before |
| TC-U-81 | `Describe_ShouldReportAMultiInstanceSendEmail_AsFoundWithAnIssue` [the converted element of TC-U-80, a former `Attachments1` among its moved item properties; a converted Send email that never had a slot] | Q16 (agreed): describe returns the element (found, not an error) with an issue saying that attachments of a multi-instance Send email are not supported through this tool; no `email.attachments` entry is decoded from the moved item properties, which the run time's top-level slot search does not read (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:78-98`); nothing is refused on describe; wording pinned by fragment |

### 2.11 The C2 refactor proof - `FileSourcePlannerCharacterizationTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-67 | `FilesBinder_ShouldEmitExactlyThePinnedMessages` [value; no source; more than one; own parameter; neither file nor collection; nested; collection with no File item; collection with several File items; expression notice; unknown element; unknown parameter] | written and committed on `main` BEFORE the lift: every `fileProcessing.files` message (`PB/FileProcessing/ProcessFileProcessingBinder.cs:137-224, 300-301`) pinned VERBATIM; unchanged after the lift |
| TC-U-68 | `FileSourcePlanner_ShouldSpeakInTheCallersTerms` [files; `email.attachments[2]`] | label and member path come from the caller; the "keeps the process from starting" consequence appears only for `files`; the write entry is the caller's too: `files` keeps the raw `ApplyMapping` for a single file and an expression (`PB/FileProcessing/ProcessFileProcessingBinder.cs:299, 305`), an email slot passes `ApplyBlockMapping` |

Proof rule for both refactor commits (C2, C7 part 1): the commit changes no existing test file. Checked with
`git diff --stat <before>..<lift> -- tests/` (only the characterization file, added one commit earlier, may appear),
and the full PBT run is green on dev-nf and dev-n8 before and after.

### 2.12 Contract, DI, clone - rows in existing fixtures

| TC | Test | Proves |
|---|---|---|
| TC-U-69 | `SendEmailAttachmentMembers_ShouldKeepTheirWireNames` (new method in `PBT/EntryPoints/WebService/ProcessDesignServiceWireContractTests.cs`, same signature as `RecordColumnSourceMembers_ShouldKeepTheirWireNames`) [`SendEmailDescriptor.Attachments` -> `attachments`; `SendEmailAttachmentDescriptor` `processParameter`, `sourceElement`, `sourceElementParameter`, `expression`, `value`; `DescribeSendEmailInfo.Attachments` -> `attachments`; `DescribeSendEmailAttachment` the five plus `slot`, `bindingLevels`, `issues`] | a renamed `DataMember` compiles and is then dropped by the serializer with success; `OperationContractCount_MustStayPinned` (existing, `:153`) stays at its count (no new operation) |
| TC-U-70 | `CompositionRoot_ResolvesEveryCollaborator_FromScope` and `CompositionRoot_ResolvesProcessDesigner_WithFullCollaboratorGraph` (existing, `PBT/CrtProcessBuilderAppTests.cs:75, 89`) | rows for `IEmailAttachmentSlotWriter` and the planner interface; `ISendEmailApplier` resolves with its new constructor inside a scope (scope validation catches a captive dependency) |
| TC-U-71 | `CreateVersionClone_ShouldKeepAttachmentSlotsOwnedByTheNewVersion` (in `PBT/ProcessVersionCloneFactoryTests.cs`) | A3, A4 and GS5 of both levels equal the clone's UId after materialisation; unit side of EM13 |
| TC-U-72 | `EveryFixture_CarriesACategoryTheCiFilterSelects` (existing) | covers the new fixtures with no change |

---

### 2.13 Reuse round - rows in `StoredRuntimeFilterTests`, `SendEmailAttachmentsSweepTests`, `SendEmailAttachmentsRawRouteTests`, `FileProcessingDescribeTests`, `FileProcessingFilterTargetTests` and others

| TC | Test | Proves |
|---|---|---|
| TC-U-83 | `FormulaApplier_ShouldRefuseABodyReadingASlot`, `FormulaApplier_ShouldRefuseABodyNamingASlot_AfterTheNameIsExpanded`, `FormulaApplier_ShouldWriteABodyReadingAParameterThatIsNoSlot` | a Formula body reading a slot or its FileLocator item is refused, with and without a target and in the name form; a body reading `Subject` or a non-Send-email `Attachments1` is written |
| TC-U-84 | `EnsureSelectingFilters_ShouldRefuseAReportSource_WhenItsOnlyConditionIsDisabledInAnotherSpelling`, `..._WhenItsRootIsDisabledInAnotherSpelling`, `..._ShouldReadAnItemByItsFilterType_WhenAConditionAlsoCarriesItems`, `..._WhenTheWrapperHoldsNoRuntimeFilter` | the Q20 sweep reads the stored filter with the platform's model: a disabled condition or root in another spelling selects nothing; a condition carrying `items` is a condition |
| TC-U-85 | `EnsureSelectingFilters_ShouldRefuseAFilterLinkedByReferences_WhenAnEmailSendsIt`, `Rule_ShouldNotJudgeAFilterLinkedByReferences`, `CarriesReferenceMetadata_ShouldRefuseAnEscapedKey`, `CarriesReferenceMetadata_ShouldReadAsDeepAsTheHost`, `IsBoundedTree_ShouldRefuseAGroupReachedTwice`, `IsBoundedTree_ShouldRefuseAModelOverTheItemBudget` | `$id`/`$ref` metadata (also escaped), a shared or self-containing group and an oversized tree read as unreadable and end; nesting up to the host's depth is read |
| TC-U-86 | `EnsureSelectingFilters_ShouldRefuseAnObjectSourceWithAnUnmarkedFilter_WhenAnEmailSendsIt`, `EnsureSelectingFilters_ShouldReadTheMarkerGate_ForTheObjectVariantOnly`, `Rule_ShouldNotJudgeAFilterWithoutTheDataContractMarker` | an Object attachments filter without the data-contract marker is unreadable (the Object run time reads it with the legacy reader); a Report source and a package-written Object filter are unaffected |
| TC-U-87 | `Describe_ShouldNotLiftANegatedScope_InAnySpelling`, `Describe_ShouldNotSplitAScopeOutOfAnOrRoot_InAnySpelling`, `Writers_ShouldNotRewriteANegatedScope_InAnySpelling`, `Writers_ShouldNotSplitAScopeOutOfAnOrRoot_InAnySpelling`, `Reader_ShouldReadTheLastOfAFlagInTwoSpellings`, `Reader_ShouldNotSplitARootWhoseLogicalOperationIsTrue`, `TryReadBool_ShouldReadAFlagAsTheRunTimeDoes`, `TryReadInt_ShouldReadACodeAsTheRunTimeDoes` | flags read as the run time reads them; a negated record condition is never lifted or rewritten as positive, an OR root never split; an unreadable flag is a loss; the lower-case literal forms read as before |
| TC-U-88 | `Readers_ShouldReadAConditionDisabledInAnotherSpelling_AsDisabled`, `SetFilter_ShouldRemoveAnItemDisabledInAnotherSpelling`, `Rule_ShouldSkipAnItemDisabledInAnotherSpelling`, `Rule_ShouldNotJudgeALeafWithNoFilterType` | describe, the scope lift, setFilter and the SysFile notice agree with the sweep on a disabled item |
| TC-U-89 | `EnsureSelectingFilters_ShouldRefuseAnUnscopedDevExpressSource_WithoutClaimingItReportsOnEveryRecord`, `EnsureSelectingFilters_ShouldRefuseADevExpressSourceWithAnUnreadableFilter_WithoutClaimingItGeneratesOne` | the refusal still fires for a printable the run time does not generate, in a wording that does not claim a report |
| TC-U-90 | `Apply_ShouldBindTheCollectionsFileItemFoundByType_WhenAnotherItemDiffersOnlyInCase`, `Plan_ShouldCarryTheResolvedSource`, `JsonStringLiteral_*`, `SetElement_ShouldDropALoneSurrogateHalf_FromTheRestoreEntry`, `Walks_ShouldStop_AtAParameterThatIsItsOwnAncestor`, `FindParameterByUId_ShouldReturnTheAncestors_OutermostFirst`, `Assign_ShouldSet*` | consolidation pins: the file item found by type is bound; notice literals never split or carry a lone surrogate; walks stop at a cycle; the shared type helper |
| TC-U-91 | `EnsureSelectingFilters_ShouldRefuseAnUnscopedSource_FilledIntoAProcessParameterElementByAFormula`, `EnsureSelectingFilters_ShouldPassAScopedSource_FilledIntoAProcessParameterElementByAFormula` (`SendEmailAttachmentsSweepTests`) [a Formula element whose result targets a Process parameter element's `Files` input, its body reading an Object attachments element's files; a slot reading that element's `ObjectFiles`; on the end-of-request sweep, a modify and a build; control: the same shape with a scoped source] | the sweep follows the `Files` input as the element parameter it is, Formula results onto it included: an unscoped source is refused at the end of the request, a scoped one is sent (decisions Q20, fixed 2026-10-30) |
| TC-U-92 | rows in `SendEmailAttachmentsNoticeTests` and `SendEmailAttachmentsSweepTests`, named when they are written [a parent mapping an unscoped Object attachments element's files into a Sub-process `In` FileCollection, and into a `Variable` one; an unscoped Generated report likewise; a Send email slot reading the process's `In` and `Variable` FileCollection; controls: a scoped source mapped into the Sub-process, a slot reading a FileCollection the schema itself fills] | each hand-off gets its notice and the process saves - no refusal: the parent is told the called process may email the files and to scope the source; the email is told the caller decides the files, which are sent outside Creatio; neither notice fires on a control (decisions Q20, owner's choice 2026-10-30) |

## 3. Parity tests (PBT)

Comparison rule: the attachment comparison rule (ACR) of serialization §6.2, R1-R11, unchanged. In short: parameter
`UId`s removed; `A3`, `A4` and `GS5` compared as ROLES (this process / another schema); `IL2` as a role (this element /
other); the GUIDs inside a `GS2` Script mapped to roles (source element, source collection, source item); `BL1`, `A2`,
`L1`, `L12`, `GS1` and the `L18` item count compared by VALUE; key sets compared EXACTLY at every level (R7); the
relative order of the slots in `BP2` compared (R9); no `BK15` row on either side (R11). "No value" is R6 only: an `L8`
with no `GS1`, so `{}` equals `{"GS5": <process>}`. Do NOT use `FileProcessingCaptureOracle.StoresNothing` / `IsNothing`
(`PBT/FileProcessingCaptureOracle.cs:67-76`): they also equate a value-less constant (`GS1` 1) with nothing. Captions
and display values are not in the metadata: they are compared on the stand (EM2, TC-I-01).

### 3.1 Pattern A: the corpus, committed

Commit three processes verbatim under `PBT/Fixtures/SendEmail/` with a `PROVENANCE.md` and one `.gitattributes` line,
`tests/UnitTests/CrtProcessBuilder.Tests/Fixtures/SendEmail/*.metadata.json -text` (the existing rule at
`.gitattributes:21` names only `Fixtures/FileProcessing/`). Measured 2026-10-28: the three files are CRLF WITHOUT a BOM
(first bytes `7b 0d 0a`), like the two committed process captures (PrintInvoiceReport, FileParameterProcess); only the
three task-template captures carry a BOM. PROVENANCE says so.

| File | Source (relative to `PS/`) | Process UId | Size | SHA-256 |
|---|---|---|---|---|
| ProcessFileSendMail.metadata.json | ProcessTests/branches/7.8.0/Schemas/ProcessFileSendMail/metadata.json | 30fc7866-0657-4f6b-8a56-79fa7a26b610 | 252,943 B | 8fb74e92123a7dc9da218d9fd9d36be28e9b48d06dfa61835790d065a8ce7df3 |
| ProcessFileReport.metadata.json | ProcessTests/branches/7.8.0/Schemas/ProcessFileReport/metadata.json | 4892b8d8-a6c1-44ec-bfdc-549ce2a4d7ea | 201,467 B | 72bc4a991e5339769a059ca2f61f7e5406ce08b7fb88e499f5a6b0fdb14c8713 |
| MailConnectionsProcess.metadata.json | ProcessTests/branches/7.8.0/Schemas/MailConnectionsProcess/metadata.json | 01d2c911-8b8d-44c8-b174-2559ac7935e9 | 155,216 B | 74e321d0bddd785604e4847176333562b525402a83e0daac73902b00743a6740 |

Oracle reader: extend `PBT/FileProcessingCaptureOracle.cs` rather than fork it. Today it reads `A2`, `GS1`, `GS2`,
`GS5` and `L18` (`:113-133`) and resolves files under `Fixtures/FileProcessing` only
(`PBT/FileProcessingTestSupport.cs:197-210`). Add: the fixture folder as a parameter; `L1`, `L12`; `A3`/`A4` and `IL2`
as roles; the element/parameter GUIDs of a `GS2` reference decoded to names through the capture.

Fixture `SendEmailAttachmentsParityTests`:

| TC | Test | Proves |
|---|---|---|
| TC-P-01 | `Capture_ShouldHashToTheValuePinnedInProvenance` [x3] | a hand edit or a line-ending normalisation fails (pattern `PBT/FileProcessingProcessParityTests.cs:63`) |
| TC-P-02 | `Capture_ShouldHoldTheSlotShapeTheOracleReads` [the 4 elements below] | tripwire: each captured element has exactly `Attachments1` with one item; a refresh that changes this fails here, not deep in TC-P-03 |
| TC-P-03 | `Slot_ShouldEqualTheShippedElement` [ProcessFileSendMail `EmailTemplateUserTask1` manual + custom, Object source (slot at `:2933`); `EmailTemplateUserTask2` auto + custom, Report (`:3939`); ProcessFileReport `EmailTemplateUserTask1` manual + template, Report (`:2655`); MailConnectionsProcess `EmailTemplateUserTask1` auto + template, Object (`:2489`)] | AC2/AC4/AC7: the builder, given the captured source element and mode/message source, stores the captured slot under the comparison rule, both binding levels included |

### 3.2 Pattern B: the stand designer capture (EM1), restated as rows

The corpus lacks the Process-parameter variant, a FileCollection or File source, two or more slots on one element and
a numbering gap, and it is from 2021-2022. EM1 captures them on the current platform; its rows are restated in code
per parameter (source kind, value role, `L1`, `L12`, stamp roles), as `PBT/FileProcessingSysFileParityTests.cs:20-53`
restates its stand capture. The rows are written only after EM1 ran; no placeholder assertion is committed. The test's
`[Description]` names the capture date and the platform and package versions EM0 recorded.

Fixture `SendEmailAttachmentsStandCaptureParityTests`:

| TC | Test | Proves |
|---|---|---|
| TC-P-04 | `Slot_ShouldEqualTheStandCapture` [OF1 Object; RF1 Report; PF1 Process-parameter variant (`ObjectFiles` / `ObjectFile`); `Docs` FileCollection item; `Doc` File (outer stores nothing)] | AC2, Q3: every source kind, both levels |
| TC-P-05 | `Element_ShouldEqualTheStandCapture_WithFourSlots` | AC3: count, names, order by number (four: the designer picker does not offer PF1's output, EM1 2026-10-29; the PF1 row is checked against the EM3 addendum instead) |
| TC-P-06 | `Numbering_ShouldEqualTheStandCapture_AfterAGapAndAReAdd` | Q5: clear row 2, save, add a row: the new row's name equals the capture's (expected `Attachments2`) |
| TC-P-07 | `TemplateAutoElement_ShouldEqualTheStandCapture` (SE2) | AC4 on the current platform |

### 3.3 Pattern C: the platform writer's JSON

Fixture `SendEmailAttachmentsSerializationTests`, built like `PBT/FileProcessParameterTests.cs:481-783` (PU-13):
`WriteMetaData` -> JSON -> comparison rule -> structural `Diff` listing every difference by path; `ReadBack` proves the
platform reader accepts the bytes; a harness-stamp check pins the normalised keys by VALUE so the rule cannot hide a
wrong stamp.

| TC | Test | Oracle | Rule additions over PU-13 |
|---|---|---|---|
| TC-P-08 | `WriteMetaData_ShouldStoreTheDesignersShape_ForAnAttachmentSlot` | the quoted fragment `PS/ProcessTests/branches/7.8.0/Schemas/ProcessFileSendMail/metadata.json:2930-2961` (outer + item) | `IL2` = the ELEMENT, compared as a role (PU-13 removes it from the diff and pins it to the schema by value, `PBT/FileProcessParameterTests.cs:606, 708-711`); `GS2` element/parameter GUIDs to roles; `A3` = `A4` = `GS5` = the process on both levels pinned by value; exactly one `L18` item named `Attachments1FileLocator` |
| TC-P-09 | `WriteMetaData_ShouldStoreTheDesignersShape_ForASingleFileSlot` | EM1's `Doc` row (restated) | the outer's `L8` carries no `GS2`; the item's `GS2` names the process File |

---

## 4. clio unit tests (`CLIO/clio.tests`)

| TC | Fixture.test | Proves | Module |
|---|---|---|---|
| TC-C-01 | `ProcessBlockKeyCheckTests.Check_Should_AcceptAnAttachmentsList_OnEveryRoute` [create `elements[].email`; `addElement`; `setElement` `elementUpdate.email`; an empty list] | K2: `attachments` is a known key; today it is refused pre-send (`clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:185-193`) | ProcessModel |
| TC-C-02 | `ProcessBlockKeyCheckTests.Check_Should_RefuseAnUnknownAttachmentEntryKey_NamingItsPath` [`sourceElementParam`, `file`] | the child key set; the path reads `email.attachments[0].<key>` and lists the accepted entry keys | ProcessModel |
| TC-C-03 | `ProcessBlockKeyCheckTests.Check_Should_TolerateTheReadBackFields_OfADescribedEntry` [`slot`, `bindingLevels`, `issues`] | a described entry fed back is warned about, not refused (the Q4 modify recipe) | ProcessModel |
| TC-C-04 | `KeySets_Should_EqualTheMembersOfTheBundledServerContracts` (existing, `:449`) | turns red on the rebundle until K2 adds `attachments` to the EmailBlock keys (`SendEmailDescriptor` gains the member). It does NOT force the entry's child key set: the test walks only the key sets clio declares (`ProcessBlockKeyCheck.All`, `clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:342-359`), and a declared key with no child is skipped (`:509-511`), so every entry key would go unchecked. TC-C-02 is the forcing test for the child set; once that set exists, this test holds it equal to the entry contract | ProcessModel |
| TC-C-05 | `ReadBackFields_Should_BeDescribeOnlyMembers` (existing, `:469`) | the three read-back fields exist on the describe type and not on the write type | ProcessModel |
| TC-C-06 | `ProcessDesignerRequiresPackageAttributeTests` TestCase literals (`:72-73, 111`) moved to the final cut; `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` (existing) green | K3: create, modify and modify-as-new-version floors; the shipped archive satisfies every literal | Command, Common |
| TC-C-07 | `CreateBusinessProcessToolTests.CreateBusinessProcess_Should_Forward_SendEmailAttachments_Verbatim` | the descriptor reaches the command unchanged (mirror of `:122`) | McpServer |
| TC-C-08 | `ModifyBusinessProcessToolTests.ModifyBusinessProcess_Should_Forward_SendEmailAttachments_Verbatim` [a list; `[]`] | an EMPTY list survives forwarding (Q4: `[]` removes all; dropping it would leave attachments in place) (mirror of `:194`) | McpServer |
| TC-C-09 | `ServerProcessDescriberTests.Describe_ShouldReadSendEmailAttachments_WhenServerReportsIt` | K6: typed `DescribedEmail.Attachments`, two entries with a gap (`Attachments1`, `Attachments3`), `bindingLevels`, `issues` (mirror of `:1329`) | ProcessModel |
| TC-C-10 | `ServerProcessDescriberTests.Describe_ShouldLeaveEmailAttachmentsNull_WhenServerOmitsIt` | null, not empty (mirror of `:1548`) | ProcessModel |
| TC-C-11 | `ServerProcessDescriberTests.Describe_ShouldKeepUnknownEmailMembers_InTheExtensionBag` | the bag (`IProcessDescriber.cs:1240-1249`) still carries a member clio does not type | ProcessModel |
| TC-C-12 | `EmailBlockExpectationTests.AttachmentElements_ShouldBeDetected_OnEveryRoute` | K7: create descriptor, `addElement`, `setElement`; absent `attachments` is not tracked; `[]` is tracked as zero | ProcessModel |
| TC-C-13 | `EmailBlockExpectationTests.AttachmentElementsFromOperations_ShouldKeepTheLastListSentForAnElement` | a later `setElement` in the batch replaces the expectation (mirror of `:439`) | ProcessModel |
| TC-C-14 | `EmailBlockExpectationTests.UnlandedAttachments_ShouldReportAnElementDescribedWithFewerEntries` | sent N, described fewer or null (an older package dropped the member and answered success) | ProcessModel |
| TC-C-15 | `EmailBlockExpectationTests.UnlandedAttachments_ShouldStayQuiet_WhenEveryEntryLanded` [N landed; `[]` and null] | no false warning | ProcessModel |
| TC-C-16 | `EmailBlockExpectationTests.BuildAttachmentWarning_ShouldStateTheConsequenceAndTheFix_WithoutAVersion` | qualitative wording; no version number in the text (`docs/knowledge/Common/a-version-floor-in-a-caller-facing-message-is-unreachable-advice.md`) | ProcessModel |
| TC-C-17 | description pins in `CreateBusinessProcessToolTests`, `ModifyBusinessProcessToolTests`, `DescribeProcessToolTests`, `ModifyProcessAsNewVersionToolTests` | K4: create names `attachments?` in the email shape and the guide `process-send-email-attachments`; modify states "attachments replaces the list: to add one, send the described list plus the new one"; describe states the read-back line; the floor sentences (`CreateBusinessProcessToolTests.cs:431`, `ModifyBusinessProcessToolTests.cs:706`, `ModifyProcessAsNewVersionToolTests.cs:316`) move with the floor | McpServer |
| TC-C-18 | `ToolContractPayloadBudgetTests` (existing: `GetToolContracts_ShouldKeepLargestContractWithinBudget_WhenEveryIndexedToolIsNamed`, `..._ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether`, `..._ShouldReturnEverySevenProcessDesignerContractShort_WhenTheCallShapesOutgrowFitsBudget`) | Q12: the swap pays for the clause. The sizes are read before and after by lowering the ceiling on the branch and reading the failure; both numbers go into the PR body | McpServer |
| TC-C-19 | prompt pins (`CreateBusinessProcessToolTests.cs:494-510`, `ModifyBusinessProcessToolTests.cs:765`) | K5: the Send email sentence names attachments; the Process file paragraph names the consumer | McpServer |
| TC-C-20 | `BundledProcessBuilderPackageTests` (existing) | K1: hash, version, producing commit, `ModifiedOnUtc` moved by `rebundle-process-builder.ps1`; `ExpectedOperationContractCount` 8 and `ExpectedAuthorizationGateCallSites` 6 UNCHANGED - a failure there means the package changed its service surface, which this feature must not | Common |
| TC-C-21 | `WorkspaceTemplateGuidanceDriftTests` (existing) with `Fixtures/curated-knowledge-names.json` re-pinned to the generation that publishes the new article | K9: a description may cite only a published guide name. The re-pin lands only after the KB PR merges and its generation is published; the clio PR stays draft until then | McpServer |
| TC-C-22 | `McpE2eSelectionCoverageTests` (existing) | the new e2e fixture is selected when its tool changes (it names `CreateBusinessProcessTool.CreateBusinessProcessToolName`) | Core |
| TC-C-23 | `EmailBlockExpectationTests.RemovedAttachments_ShouldSayHowManyTheReplaceRuleRemoved` [one removed; three removed; none removed -> no line] | K7: the read-back says "N attachments were removed by the replace rule"; qualitative wording, no version in the text | ProcessModel |

New clio tests go into the existing fixtures and follow the host fixture's placement: `[Category("Unit")]` stays where
the fixture puts it (on the fixture in the ProcessModel fixtures, on each method in the McpServer tool fixtures), no
category attribute is added to a `BaseCommandTests` fixture, and `[Property("Module", ...)]` is the host's. AAA,
`because` on every assertion, `[Description]` without a Jira key.

Validation filter (AGENTS.md "Smart regression testing"): the change spans ProcessModel, McpServer, Command, Common
(the test-side pins: `rebundle-process-builder.ps1` writes `clio.tests/Common/BundledProcessBuilderPackageTests.cs`, and
the archive is `clio/CrtProcessBuilder/CrtProcessBuilder.gz`, neither under `clio/Common/`) and Core (TC-C-22,
`[Property("Module", "Core")]`, `clio.tests/McpE2eSelectionCoverageTests.cs:38`): more than three modules, so the FULL
unit suite runs before every commit. Write `TestCategory`, never `Category` (an unrecognised filter is replaced by an
empty one above 2,000 tests). Close stdin, or `McpE2eSelectionCoverageTests`' `git init` hangs on an inherited pipe:

```
# iterating
dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=ProcessModel|Module=McpServer|Module=Command|Module=Common|Module=Core)" --no-build < /dev/null
# before every commit (and quoted in the commit message / PR body)
dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null
```

---

## 5. clio MCP e2e (`CLIO/clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs`)

Fixture shape, copied from `ProcessFileElementToolE2ETests` (`:44-48`):
`[TestFixture][AllureNUnit][AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)][NonParallelizable][Category(McpE2ECategories.ProcessDesigner)]`.

| Rule | How |
|---|---|
| Version gate | `private const string MinimumPackageVersion` = the final attachments cut, the same literal as the clio floor, written in the clio PR (not here); `ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion)` Ignores below it; `IgnoreWhenProcessBuilderIsBehind` turns a convergence refusal into Ignored, never green |
| Writes | strictly sequential (parallel schema writes take a .NET Framework app pool down); one framework at a time: `-f net10.0`, then `-f net8.0` |
| Cleanup | every created process in a list, deleted newest-first in `finally` (`ProcessDesignerE2ESupport.DeleteProcessAsync`), failures reported, not thrown; email templates through `EmailTemplateE2EArrange.CreateAsync` / `DeleteAsync`; records through `execute-dataservice-batch` (stand `Creatio` answers HTTP DELETE with 405: measured 2026-09-27, measurements M-16 and rule 7) |
| Stored bytes | private helpers reading `SysSchema.MetaData` and `SysLocalizableValue`, copied from `ProcessFileElementToolE2ETests.cs:1530-1683` (`ReadElementNodesAsync`, `ReadElementResourceRowsAsync`, `SelectInlineAsync`, `SelectToFileAsync`, `DecodeMetaData`, `FindNodeByUId`, `TextOf`; a shorter copy does not compile); a lift into `Support/**` would make every PR run the whole suite |
| Fixtures the stand must carry | an MS Word printable on Invoice (read with `list-printables`, as `ProcessFileReportAndParameterToolE2ETests.cs:106` does); the EM1 process kept as `UsrSeAttachmentsDesignerFixture`. Each test that needs one is Ignored WITH the build instructions when it is absent; the evidence run must have both, so Ignored = 0 |
| Assertions | `ShouldHaveBuilt` / `ShouldBeRefused` / `WarningText` (`Support/Mcp/ProcessDesignerE2ESupport.cs:77-109`); describe text AND stored bytes; AAA, `because`, `[Description]` without a Jira key |

| TC | Test | Asserts |
|---|---|---|
| TC-I-01 | `CreateBusinessProcess_Should_StoreEveryAttachmentInTheDesignersShape_AndDescribeItBack` | one process: OF1 Object attachments of a Contact (use in process), PF1 Process-parameter variant fed by `Docs`, process `Docs` (FileCollection) and `Doc` (File), SE1 manual + custom with four entries. Build: success, the single-File warning. Describe: `email.attachments` = four entries in the write shape, `slot` `Attachments1..4`, `bindingLevels` `both` x3 + `itemOnly`. Bytes: per slot `L1` CompositeObjectList, `L12` 0, `A3` = `A4` = process UId, `IL2` = SE1's UId, one `L18` item `<slot>FileLocator` (FileLocator), Script on bound levels with `GS5` = process UId; PF1's item references `ObjectFile`. Resources: captions "Attachments collection" / "Attachment" in the active culture, and the DisplayValue rows in the designer form on slots only (outer `[#<element caption>.<collection caption>#]`, item `[#<element caption>.<collection caption>:<item caption>#]`, `Doc`'s item `[#<Doc caption>#]`; TC-U-73) |
| TC-I-02 | `CreateBusinessProcess_Should_AttachAGeneratedReport` | RF1 on the Invoice printable -> SE1: describe and bytes as TC-I-01 for `ReportFiles` / `File`; Ignored without the printable |
| TC-I-03 | `CreateBusinessProcess_Should_FeedFourSendEmailElementsFromOneCollection_InEveryModeCombination` | OF1.`ObjectFiles` feeds SE1 manual + custom, SE2 manual + template, SE3 auto + custom (no sender: the build does not require one), SE4 auto + template (template made by `EmailTemplateE2EArrange`): four equal slots under the comparison rule; OF1's bytes equal to a twin process without the emails |
| TC-I-04 | `CreateBusinessProcess_Should_RefuseAnAttachmentWithoutFiles_AndCreateNothing` [`{}`; unknown element; `RD.ResultCompositeObjectList`; a source declared after SE1; an Object attachments source (use in process) with no record scope and no filter condition] | refused; the error names `email.attachments[0]` and the source (the later-declared row: the forward-reference sentence; the unscoped row: the Q20 sentence); describe of the process name answers "was not found" (pattern `CreateBusinessProcessToolE2ETests.cs:1373-1426`) |
| TC-I-05 | `ModifyBusinessProcess_Should_ReplaceThenRemoveAttachments` | create with three entries; `setElement` with [entry 1, a new source]: slot 1 keeps its stored UIds (bytes), slots 2 and 3 are gone (their `SysLocalizableValue` keys as EM1/EM2 recorded: gone, or orphaned under a name no slot uses), the new entry is `Attachments2`, the warnings carry each removed entry in the write shape; one `setElement` that drops a collection-sourced entry and adds a single-File entry reusing the freed number: the reused outer's DisplayValue row carries no stale `[#...#]` value and describe lists the re-added row after the kept one; `setElement` with `[]`: describe has no `attachments`, no `Attachments*` parameter is stored; a subject-only `setElement` afterwards leaves the element's other bytes unchanged |
| TC-I-06 | `ModifyBusinessProcess_Should_ChangeNoStoredByte_WhenTheDescribedListIsSentBack` | create -> describe -> `setElement` with the described entries VERBATIM (read-back fields included: clio warns, does not refuse) -> SE1's stored node and its resource rows byte-identical before and after |
| TC-I-07 | `ModifyBusinessProcess_Should_RefuseARawMappingOntoAnAttachment` | `addMapping` onto `SE1.Attachments1` is refused naming `email.attachments`; stored bytes unchanged |
| TC-I-08 | `ModifyProcessAsNewVersion_Should_KeepTheAttachmentsOwnedByTheNewVersion` | the clone's slots carry `A3` = `A4` = `GS5` = the NEW version's UId on both levels; describe of the new version reports the same entries; the version is deleted in `finally` (shape side of EM13; its run is EM13) |
| TC-I-09 | `DescribeBusinessProcess_Should_ReadTheDesignerBuiltAttachmentsFixture` | read-only on `UsrSeAttachmentsDesignerFixture`: five entries decoded in the write shape, the stale-parent row (EM1d) with its issue, SE2's entries in auto + template; no write. Ignored with the EM1 build instructions when absent |
| TC-I-10 | `RunProcess_Should_PutTheGeneratedReportOnTheManualEmailActivity` | see below |

**TC-I-10, the run-time test.** File seeding is the obstacle for every source except a generated report:
`execute-dataservice-batch` accepts `data-value-type` 0-12 only, so it cannot write a Blob
(`clio/Command/DataServiceBatchCommand.cs:153`), and no clio command uploads through `FileApiService`
(`upload-image` targets images). A generated report needs no seeding: the run makes the file. So:

1. Arrange: the Invoice printable from `list-printables` and an existing invoice found by `read` (Ignored when either is
   absent); `UseProcessEmailAttachments` read for the run user (Ignored, with the reason, when off).
2. Build: `InvoiceRef` (Lookup Invoice) -> RF1 Generated report, use in process -> SE1 manual + custom, subject with a
   unique token.
3. Act: `run-process` with `watch: ["Activity", "ActivityFile"]`, `wait-timeout` short, `cleanup: false`. The run parks
   on SE1: status running, `waitingOn` names SE1.
4. Assert: `created` holds one Activity (title = the token) and one ActivityFile; reading that ActivityFile row gives
   `Activity` = the created activity, `Size` > 0, a `.docx` name.
5. Cleanup in `finally`, in this order: set the activity's `StatusId` to Canceled (`201cfba8-58e6-df11-971b-001d60e938c6`)
   with `execute-dataservice-batch`, which ends the parked instance; delete the ActivityFile and Activity rows with
   `execute-dataservice-batch`; delete the process. There is no `cleanup: true` fallback: `run-process` deletes through
   OData DELETE (`clio/Command/RuntimeVerification/RecordDeletionService.cs:14-24, 144-152`), which stand `Creatio`
   answers with 405. If this cleanup proves flaky, the test records the ids it created and the leftovers go to the
   measurements register for CU-2.

The Object, Process-parameter and File sources at run time stay with EM4 and EM8, a process FileCollection passed in
from a parent process with EM4c, and all of them with the manual cases (TC-02, TC-03, TC-07). A helper that uploads
through `FileApiService` would add a stand-write path no other test uses; it is not planned unless the owner asks.

**Evidence.** The PR lane excludes `McpE2E.ProcessDesigner` (`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:46`,
`baseFilter`). The plan-run job's filter is in TeamCity job arguments (described, not proven, in
`Support/Configuration/McpE2ECategories.cs:31-36`). Either way a green PR proves nothing here. The evidence is a stand run on `Creatio`, recorded in the clio PR body with
passed / failed / ignored counts per framework and Ignored = 0:

```
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~SendEmailAttachmentsToolE2ETests"
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net8.0  --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~SendEmailAttachmentsToolE2ETests"
```

plus the regression fixtures of section 9 with the same category filter.

---

## 6. clio-knowledge tests (`KB/automation/Clio.Knowledge.Bundle.Tests`)

| TC | Test | Proves |
|---|---|---|
| TC-K-01 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldOwnTheBlockAndCarryTheRealFloor`, `Guide_ShouldStateTheEntryAndItsRefusals`, `Resource_ShouldBeRegisteredAndReachedFromEveryArticleThatMentionsTheBlock` | the new article `process-send-email-attachments` carries the contract anchor (`email.attachments`, one entry per row, the source vocabulary of `fileProcessing.files`), the REAL floor (the only defence for a clio built before 2026-10-13, which has no `ProcessBlockKeyCheck` and forwards the member to an older package that drops it with success), the refusals; `send-email.md` and `files.md` route to it by name (pattern `TemplateGuide_ShouldOwnTemplateMode_AndTheEntryShouldRouteToIt`, `:122`) |
| TC-K-02 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldKeepTheTwoAttachmentsWordsApart` | the sentence that separates `email.attachments` (files the email sends) from `fileProcessing.attachments` (files a Process file element reads) is in both articles |
| TC-K-03 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldStateBothOrderRules` | AC6: configure the Process file element first; keep it on the flow BEFORE the Send email; a source that has not run gives an email without files and no error (EM10's measured result, with its date) |
| TC-K-04 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldStateTheReplaceRuleAndTheRecipe`, `Guide_ShouldStateTheReadBack` | Q4/Q11: describe IMMEDIATELY before -> edit -> send back (modify has no concurrency token, so a stale list deletes rows added since); to add one, send the described list plus the new one; `[]` removes all; absent is untouched; the removal notice carries what was removed; recipients still append (one line) |
| TC-K-05 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldCarryTheFeatureLine_AndNoArticleShouldClaimABuildTimeCheck` | Q9 (agreed: no build-time check): the article carries one troubleshooting line, "an email without its files: check feature UseProcessEmailAttachments for the user who runs the process", and claims no build-time check or warning for the feature |
| TC-K-06 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldStateTheRunTimeFactsAndTheScopeRefusal` | AC2/AC4: works in all four combinations; manual mode puts the files on the draft email activity; attachment files are read with system rights, not the process user's (Q20); a generated report is a Word file; the empty single File sentence only if EM8 confirmed it |
| TC-K-07 | `SendEmailAttachmentsGuidanceTests.NoArticleOrDescription_ShouldClaimSendEmailAttachmentsAreNotBuildable` | the retired claim is gone from every declared process article and every manifest description, whitespace-insensitive (pattern `:165`) |
| TC-K-08 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldCarryNoPlaceholderOrInternalReference_AndNoUnmeasuredPattern` | no ticket key, no spec path, no "TBD" floor (`KB/CONTRIBUTING.md:179-183`) |
| TC-K-09 | `ProcessFileGuidanceTests.Section_ShouldStateTheEndpointConsumersAndTheReFind` (existing, `:169`) | the pinned clause "Send email attachments and Creatio.ai file inputs are not buildable here yet." (`:181`) is split in the SAME commit as `files.md`: Send email attachments route to the new article; Creatio.ai file inputs stay "not buildable here yet"; the "Creatio cannot" absence check stays |
| TC-K-10 | size gates (existing, plus `SendEmailAttachmentsGuidanceTests.Guide_ShouldStayInsideItsDesignTarget`): `ProcessGuideResponseSizeTests.EveryProcessArticle_ShouldFitInOneGetGuidanceResponse`, `GuidanceInlineReplyBudgetTests.SplitArticles_ShouldFitTheInlineReplyBudget` (`process-modeling.md` has 1 B of headroom: the route line is paid by a swap), `ProcessDigestTests.TheCard_ShouldStayWithinItsBudget` (card untouched) | Q12 |
| TC-K-11 | manifest and routing (existing): `EveryProcessArticleOnDisk_ShouldBeDeclaredInTheManifest`, `ProcessGuideCrossReferenceTests.Routing_ShouldNameEveryProcessArticle_AndTheEntryArticleShouldIndexThem`, `Routing_ShouldOnlyNameDeclaredGuidanceTopics`, `ManifestEntryDescription_ShouldIndexEveryArticleTheEntryArticleIndexes`, `ManifestStrictnessTests.BundleSource_ShouldContainNoDuplicateProperties`, `GenerationSequenceTests`, `GuidanceMigrationTests.BundleSource_ShouldDeclareCanonicalMultiSourceIdentity` (`PostMigrationGuidance` gains `process-send-email-attachments`) | the article is declared in `resources[]` with NO `legacyUris` (an article authored after the migration has none, `GuidanceMigrationTests.cs:235-244`; follow `process-files-parameter`, not `process-send-email-template`), `requirements.itemIds` and `requirements.resourceUris`; `routing.md` has a `name=process-send-email-attachments` row; the `process-send-email` and `process-modeling` entry descriptions name it; it carries the set banner so it joins the go-live set (`ProcessGuideSet.cs:74-93`), and the banner is what obliges `process-modeling.md` to index it (`ProcessGuideCrossReferenceTests.cs:475-488`, `ProcessGuideSet.cs:27`; the `process-files*` articles carry none). `libraryVersion` is bumped and derives a sequence above the last authored one (`GenerationSequenceTests`; no `sequence` field is authored, `RepositoryManifest_ShouldNotDeclare_AnAuthoredSequence`). That it is above master is checked only by the Producer contract workflow step (`KB/CONTRIBUTING.md:173-177`); that it is above every open branch claim, by no test: a manual check at merge |
| TC-K-12 | fact-check gate (not a unit test); its corrections are pinned by `SendEmailAttachmentsGuidanceTests.Guide_ShouldCarryTheCorrectedClaims` | below |
| TC-K-13 | `SendEmailAttachmentsGuidanceTests.Guide_ShouldCarryNoPlaceholderOrInternalReference_AndNoUnmeasuredPattern` (its per-recipient check) | Q16: written after EM8; asserts the multi-instance sub-process pattern (one File per recipient) when EM8a passed, and its absence otherwise |

Run: `dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj` (whole project).

**TC-K-12, the refute-first fact-check gate.** Before the KB PR leaves draft, every factual claim of the new article
and of the edited sentences is checked by independent agents told to REFUTE it, one agent per claim cluster: (1)
contract, refusals, replace rule; (2) designer row shape and numbering; (3) run time: modes, manual activity,
feature gate, empty File, report format; (4) order rules. Each agent gets the source trees (`PB/`, `PD/`, `TS/`) and
the EM results as dated measurements. Each agent may NOT cite as evidence `CLIO/docs/knowledge/`, anything under
`CLIO/spec/eng-95985-send-email-attachments/`, the research folder, or another agent's output. Verdict per claim:
confirmed / refuted / unverifiable, with `file:line` or the EM id. Every refuted or unverifiable claim is re-checked by
hand and fixed or removed; the verdict table goes into the KB PR body.

---

## 7. Stand checks mapped to EM ids

Procedures, gates, expected results and cleanup: measurements file. Every write to the stand waits for the owner's yes.

| EM | Proves | AC / Q | Feeds or gates |
|---|---|---|---|
| EM0 | read-only baseline: platform and package versions, feature states, mailbox rows, printable, template | all | the version recorded in TC-P-04..07; e2e fixture readiness |
| EM1 | designer capture: five rows (OF1, RF1, PF1, `Docs` item, `Doc`), gap + re-add, SE2 auto + template; resource keys of a removed slot | AC7, AC3, Q3, Q4, Q5 | TC-P-04..07, TC-P-09; `UsrSeAttachmentsDesignerFixture` for TC-I-09; TC-I-05's resource assertion and TC-U-73's empty-DisplayValue row |
| EM1d | stale parent over a single File | Q11 | issue wording of TC-U-64 row 4 |
| EM2 | server-built twin passes the pre-save gate and diffs clean against EM1 | AC1, AC7, Q11 | STOP gate after slice B: save, pre-save gate and stored-bytes steps only; its describe steps (round trip, stale-parent issue) run in the EM2 re-run on the final cut, after P-C3 |
| EM3 | the designer opens the builder-made element; a no-op save is byte-equal | AC1, AC7 | residual-risk STOP gate (no describe) |
| EM4 | run, manual + custom, 2+ slots -> ActivityFile rows | AC2, AC3, AC4 | the three Process file variants at run time; AC3 run-time evidence |
| EM4c | a parent process passes OF1.`ObjectFiles` into the callee's FileCollection parameter `Docs`; the callee's SE1 (manual) has attachments [`processParameter` `Docs`] -> ActivityFile rows = the files | AC2, Q2 | DoD AC2 (a process FileCollection at run time); mirror of manual TC-03 |
| EM5 | manual + template | AC4 | |
| EM6 | (removed: Q17 agreed 2026-10-28) | - | - |
| EM7 | real delivery on stand `Creatio`, once the owner says its mailbox works: auto + custom, auto + template, and the Create activity variant that leaves ActivityFile rows | AC4, Q17 | DoD AC4 with EM4 and EM5; until EM7 runs, auto mode rests on the unit tests and the shared run-time path |
| EM8 | single File: set -> one file; unset -> element error | Q3, Q16 | TC-U-11, TC-U-43, TC-K-06, TC-K-13; failure flips Q3 to B |
| EM9 | outer-only slot: loud or silent | Q8 | TC-U-64 row 1 wording; the article's troubleshooting line |
| EM10 | unexecuted source -> 0 files, no error | AC6, Q6 | TC-K-03; the reachability warning's consequence clause (TC-U-77) |
| EM11 | one collection -> two emails; `removeElement` of the source | AC2, Q16 | a follow-up only if the platform saves a dangling reference green |
| EM12 | (removed: Q9 agreed 2026-10-28) | - | - |
| EM13 | modify-as-new-version: the clone's slots attach at run time | G3 | TC-U-71, TC-I-08 shape side |
| EM14 | SysFile-root Object source | AC2 | TC-U-20 |
| EM15 | report temp file readable from ActivityFile after the process ends | AC4 | TC-K-06; UC1/UC9 durability |

---

## 8. Manual Jira test cases

Written for the Jira issue's test-case section. Drafted here; the owner pastes them by hand.

**Test cases**

**Conditions:** A Creatio environment with the CrtProcessBuilder version this issue ships, and a clio build with this
issue's clio change connected to an AI agent as an MCP server. Feature `UseProcessEmailAttachments` is on for the
users who build and run the processes (the package turns it on for all employees and all external users). Test data: the contact "SE Doc Target" with an email
address and two attachments uploaded in this order, `Contract.txt` then `Price list.txt`; the invoice "INV-1 (sample)"
(shipped demo data) with the MS Word printable "Invoice"; an email template "SE Offer template" with its own subject
and body. Cases that send automatically need a mailbox connected for the sender; on an environment without one, run
only their design-time part. Each case is given to the agent as a business task in plain words. Design time is checked
in the Process Designer: open the process, select the Send email element, look at the rows of its "Add attachments"
block. Run time is checked by running the process: in manual mode, open the email activity the run created and its
attachments; in automatic mode, open the received email. Delete the test processes, the activities and the files they
created after each case.

### `TC-01` Email the generated invoice to the customer

**Preconditions:**
"INV-1 (sample)" exists; a sender mailbox is connected (run time only).

**Steps:**
1. Give the agent the task: "Create a process 'SE Invoice email' that takes an invoice, generates its 'Invoice' printable and emails the document automatically to the invoice's contact."
2. Open the process in the Process Designer and select the Process file element and the Send email element.
3. Run the process with "INV-1 (sample)".
4. Open the received email.

**Expected result:**
* Design time: a Process file element with source "Generated report", report "Invoice", set to use the file in the process; the Send email element sends automatically with a custom message, and its "Add attachments" block has one row mapped to the generated files of the Process file element.
* Run time: the email arrives with one Word attachment generated from the "Invoice" printable; the process completes.

---

### `TC-02` Forward the latest file of a contact

**Preconditions:**
"SE Doc Target" has `Contract.txt` and, uploaded later, `Price list.txt`.

**Steps:**
1. Give the agent the task: "Create a process that takes a contact and prepares an email to that contact with only the most recently uploaded file from the contact's attachments. I will check the email before it is sent."
2. Open the process in the Process Designer and select the Process file element and the Send email element.
3. Run the process with "SE Doc Target".
4. Open the email activity the run created.

**Expected result:**
* Design time: an Object attachments Process file element reading the contact's files, sorted by creation date descending and limited to one file, set to use the files in the process; the Send email element is in manual mode with a custom message and one attachment row mapped to that element's files.
* Run time: the draft email activity has exactly one attachment, `Price list.txt`; the contact's attachments are unchanged.

---

### `TC-03` Email files passed in as a parameter

**Preconditions:**
As in TC-02.

**Steps:**
1. Give the agent the task: "Create a process 'SE Send documents' with a list-of-files parameter 'Documents' and a contact parameter, which prepares an email to the contact with the documents attached."
2. Give the agent the task: "Create a process 'SE Files parent' that reads all attachments of a given contact and runs 'SE Send documents' with those files and that contact."
3. Open both processes in the Process Designer; check the parameters of 'SE Send documents' and select its Send email element.
4. Run 'SE Files parent' with "SE Doc Target".
5. Open the email activity the run created.

**Expected result:**
* Design time: 'SE Send documents' has a "Documents" parameter of type collection of files; its Send email element (manual mode) has one attachment row mapped to "Documents"; 'SE Files parent' passes the Object attachments element's files to "Documents".
* Run time: the draft email activity has both files, `Contract.txt` and `Price list.txt`.

---

### `TC-04` Prepare a templated draft with the contact's files for review

**Preconditions:**
As in TC-02; "SE Offer template" exists.

**Steps:**
1. Give the agent the task: "Create a process that takes a contact and prepares a draft email from the template 'SE Offer template' with all of the contact's attachments, for the account manager to review and send."
2. Open the process in the Process Designer and select the Send email element.
3. Run the process with "SE Doc Target".
4. Open the email activity the run created.

**Expected result:**
* Design time: the Send email element is in manual mode with the template message "SE Offer template"; its "Add attachments" block has one row mapped to the files of an Object attachments element.
* Run time: the draft email activity carries the template's subject and body and both files.

---

### `TC-05` Send a templated email with a document pack automatically

**Preconditions:**
As in TC-04; a sender mailbox is connected (run time only).

**Steps:**
1. Give the agent the task: "Create a process that emails the contact automatically using the template 'SE Offer template' and attaches every file from the contact's attachments."
2. Open the process in the Process Designer and select the Send email element.
3. Run the process with "SE Doc Target".
4. Open the received email.

**Expected result:**
* Design time: the Send email element sends automatically with the template message "SE Offer template"; one attachment row mapped to the Object attachments element's files.
* Run time: the received email has the template's content and both files.

---

### `TC-06` Use one generated report in two emails

**Preconditions:**
As in TC-01.

**Steps:**
1. Give the agent the task: "Create a process that generates the 'Invoice' printable for a given invoice once, emails it automatically to the invoice's contact, and also prepares a draft email with the same document for the invoice owner."
2. Open the process in the Process Designer and select each Send email element.
3. Run the process with "INV-1 (sample)".
4. Open the received email (where a mailbox exists) and the draft email activity.

**Expected result:**
* Design time: one Process file element generating the report; two Send email elements, one automatic and one manual, each with one attachment row mapped to the same generated files; the Process file element comes before both on the flow.
* Run time: the draft email activity has the Word file; where a mailbox exists, the received email has the same file.

---

### `TC-07` Attach files from two sources to one email

**Preconditions:**
As in TC-01 and TC-02.

**Steps:**
1. Give the agent the task: "Create a process 'SE Invoice and contract' that takes an invoice and a contact, generates the 'Invoice' printable, and prepares an email to the contact with the invoice document and the contact's file 'Contract.txt' attached."
2. Open the process in the Process Designer and select the Send email element.
3. Run the process with "INV-1 (sample)" and "SE Doc Target".
4. Open the email activity the run created.

**Expected result:**
* Design time: the "Add attachments" block has two rows: one mapped to the generated report's files, one mapped to the files of an Object attachments element filtered to `Contract.txt`.
* Run time: the draft email activity has two attachments, the Word invoice and `Contract.txt`.

---

### `TC-08` Change and remove the attachments of an existing process

**Preconditions:**
The process 'SE Invoice and contract' from TC-07 exists.

**Steps:**
1. Give the agent the task: "In 'SE Invoice and contract', stop attaching the contract and attach the contact's 'Price list.txt' instead. Keep the invoice."
2. Open the process in the Process Designer and select the Send email element; run it with "INV-1 (sample)" and "SE Doc Target"; open the draft email activity.
3. Give the agent the task: "In 'SE Invoice and contract', send the email without any attachments."
4. Open the process in the Process Designer again; run it; open the new draft email activity.

**Expected result:**
* Design time after step 1: two rows, the unchanged invoice row and a row for the price list; no row for the contract. The agent reports which attachment it removed.
* Run time after step 1: the draft has the Word invoice and `Price list.txt`, not `Contract.txt`.
* Design time after step 3: the "Add attachments" block has no rows; the rest of the element (recipients, subject, body) is unchanged.
* Run time after step 3: the draft has no attachments.

---

### `TC-09` Refuse an attachment whose source holds no files

**Preconditions:**
"SE Doc Target" exists.

**Steps:**
1. Give the agent the task: "Create a process that reads the contact 'SE Doc Target' with a Read data step and attaches what that step reads to an email to the contact."
2. Read the agent's answer; open the list of processes.

**Expected result:**
* The agent does not build an attachment from the Read data step: the build is refused, or the agent explains before building, that this source holds no files.
* The answer names the attachment and the source, and offers a working alternative (a Process file element reading the contact's attachments).
* No process with an attachment row bound to the Read data step exists.

---

### `TC-10` Explain a designer-built email with attachments without changing it

**Preconditions:**
A process 'SE Designer attachments' built by hand in the Process Designer: a contact parameter, a File parameter "Signed copy", an Object attachments element reading the contact's files (use in process), a Generated report element for "Invoice", and a manual Send email element with three attachment rows: the Object attachments files, the report files, and "Signed copy". Note the process's "Modified on".

**Steps:**
1. Give the agent the task: "Explain what the email in 'SE Designer attachments' attaches and where each file comes from. Do not change the process."
2. Open the process in the Process Designer and select the Send email element.

**Expected result:**
* The answer lists three attachments with their sources: the contact's files, the generated invoice, and the "Signed copy" parameter.
* The answer warns that the email step fails if "Signed copy" is empty when the process runs.
* The process is unchanged: the same three rows, the same "Modified on".

---

## 9. Regression scope

| Repository | Suite | Why it is at risk | Counts (`[Test]` + `[TestCase(` lines today) |
|---|---|---|---|
| PBT | `FileCollectionMappingTests` | C5 sits in the shared `ProcessMappingService.Plan`; `TakesFiles` must not move | 79 |
| PBT | `ProcessMappingServiceTests`, `MultiInstanceMappingTests`, `ApprovalApplierTests`, `Connections/*` | every non-block mapping reaches the new guard in `Plan`; `BindCollection` gains a block form | 33, 11, 52, - |
| PBT | `ElementSourceDecodeTests` | C7 shares the decoder; PU-52 round trip | 45 |
| PBT | `SendEmailApplierTests`, `SendEmailApplierTemplateTests`, `SendEmailApplierPerformerTests`, `SendEmailApplierSenderTests` | C4 changes the applier and its constructor; recipients keep match-or-append | 29, 37, 10, 6 |
| PBT | `SendEmailElementHandlerDescribeTests`, `SendEmailElementHandlerDescribeTemplateTests`, `SendEmailElementHandlerCreateTests` | C7 adds `Attachments` and `configured` | 8, 6, 5 |
| PBT | `FileProcessingProcessTests`, `FileProcessingDescribeTests`, `FileProcessingRawMappingTests`, `FileProcessingNestedItemTests`, `FileProcessing*ParityTests`, every other `FileProcessing*Tests` | C2 and C7 refactors must be byte-neutral for `fileProcessing.files` | 62, 98, 52, 3, 8 |
| PBT | `ProcessGraphBuilderTests`, `ProcessBuildHandlerTests` | C6 order guard and reachability warning over `Traverse`; the block inventory | 62, 80 |
| PBT | `Elements/ElementConfigurationLedgerTests`, `Operations/ElementConfigurationTrackingTests` | the ledger tracks configured elements and sources; a removed slot must not confuse it | 43, - |
| PBT | `ProcessVersionCloneFactoryTests`, `ProcessModifyHandlerTests`, `ProcessDescriberTests` | clone stamps; modify batches and the reachability warning on modify; describe composition and the generic `parameters[]` (slots no longer decoded) | 15, 27, 21 |
| PBT | `ProcessDesignServiceWireContractTests`, `CrtProcessBuilderAppTests`, `CiContractGuardTests` | wire names, DI graph, fixture categories | 13, 17, 2 |
| PBT | the whole project on dev-nf and dev-n8 | before every commit | - |
| clio | `TestCategory=Unit` (full suite) | ProcessModel + McpServer + Command + Common (test-side pins) + Core (TC-C-22) changed | - |
| clio e2e | `SendEmailAttachmentsToolE2ETests` (new); `CreateBusinessProcessToolE2ETests` (`CreateBusinessProcess_Should_BuildSendEmailHtmlBody_AndReadItBack`, `..._AcceptTheFullEmailContract_AndReadEveryFieldBack`, `..._BuildTemplateModeSendEmail_AndReadTemplateBack`); `ModifyBusinessProcessToolE2ETests` (`ModifyBusinessProcess_Should_MatchOrAppendSendEmailRecipients`, `..._SwitchSendEmailToTemplateModeAndBack`); `ProcessFileElementToolE2ETests`; `ProcessFileReportAndParameterToolE2ETests` | the email block and the Process file element on a real stand after the rebundle and the floor raise | stand run, Ignored = 0 |
| KB | the whole `Clio.Knowledge.Bundle.Tests` project | new article, routing swap, pinned sentence | - |

---

## 10. Conventions (all three repositories)

| Rule | Applies as |
|---|---|
| AAA | explicit `// Arrange`, `// Act`, `// Assert`; a precondition assert in Arrange wherever the arrange could make the test pass vacuously (empty schema UId, an element that starts with slots) |
| `because` | on every assertion: what breaks for the user, not what the code does |
| `[Description("...")]` | on every test: the behaviour and why it matters, with the decision id (Q-number) where one applies; no Jira key in a test name, a `[Description]` or an `AllureName` |
| Categories | PBT `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`; clio: `[Category("Unit")]` where the host fixture places it (fixture or method level; none added to a `BaseCommandTests` fixture) + the host's `[Property("Module", ...)]`; e2e `[Category(McpE2ECategories.ProcessDesigner)]` |
| Cross-platform | no OS paths (fixtures found by ancestor search); PBT runs on dev-nf (Windows) and dev-n8 (macOS/Linux) |
| Language | PBT is C# 7.3 (no records, no target-typed `new`, no `is not`) |

A sketch of a package test in the house style:

```csharp
[Test]
[Description("Q4 replace rule: a setElement whose attachments list no longer names a slot's source removes that slot - the outer collection and its file item - and says so in a notice that carries the removed entry, while a slot the list still names keeps its name and UIds byte for byte, so a designer row the agent did not mean to touch survives the edit.")]
public void SetElement_ShouldRemoveEveryUnmatchedSlot() {
	// Arrange
	ProcessSchema schema = UserConnection.CreateSchema();
	schema.UId.Should().NotBe(Guid.Empty, because: "an empty process UId makes every stamp assertion vacuous");
	ProcessSchemaUserTask element = AnEmailElementWithSlots(schema, "SE1", "OF1.ObjectFiles", "RF1.ReportFiles");
	string keptBefore = SlotBytes(element, "Attachments1");

	// Act
	Apply(schema, SetElement("SE1", "{attachments: [{sourceElement: 'OF1', sourceElementParameter: 'ObjectFiles'}]}"));

	// Assert
	element.Parameters.FindByName("Attachments2").Should().BeNull(
		because: "the list no longer names RF1's files, so the email must stop attaching them");
	SlotBytes(element, "Attachments1").Should().Be(keptBefore,
		because: "a slot the list still names is kept as it is, not rebuilt with new UIds");
	Notices.Should().ContainSingle(notice => notice.Contains("Attachments2") && notice.Contains("RF1.ReportFiles"),
		because: "the agent must learn which attachment its list removed");
}
```

## 11. Deliberately not covered

| What | Why |
|---|---|
| A flow-reachability REFUSAL | not built (Q6 C): reachability is only a warning (TC-U-77), because a source on another gateway branch can reach the email and still not run; the guidance states the flow rule with EM10's result |
| Attachments on a multi-instance Send email; one file per recipient | not built (Q16 agreed): `email.attachments` on a multi-instance Send email is refused (TC-U-80) and described with an issue (TC-U-81); the guide documents the multi-instance Sub-process pattern only if EM8 passes (TC-K-13); a `removeElement` guard only as a follow-up if EM11 shows a silent break |
| Creatio.ai `Files<N>` slots | out of scope (Q15); only the package comment changes |
| `templateEntity` forward-reference guard | not built (Q7 B, follow-up) |
| Feature `UseProcessEmailAttachments` off | not tested (Q9 agreed): a rollout toggle on everywhere since 7.17.1 and never switched off; no build-time check is built, and the article's troubleshooting line is TC-K-05 |
| File size limits and blocked extensions (UC12) | platform behaviour for process sends is undocumented and unchanged by this feature |
| Real SMTP delivery in automated tests | needs a mailbox; EM7 on stand `Creatio` (Q17) and the manual auto-mode cases only |
