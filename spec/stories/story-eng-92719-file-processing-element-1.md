# Story 1: Element readiness and the Object attachments variant of the Process file element

**Feature**: eng-92719-file-processing-element
**Jira**: ENG-96505 Element readiness and object attachments mode (Task, converted on 2026-10-14 from a Sub-task of ENG-92719 File
processing element, which it now "relates to"; epic ENG-92704 Create BP via AI Toolkit)
**PRD**: [prd-eng-92719-file-processing-element.md](../prd/prd-eng-92719-file-processing-element.md) - OA-1..OA-12
(and its share of FE-1, FE-2, FE-4, FE-6)
**ADR**: [adr-eng-92719-file-processing-element.md](../adr/adr-eng-92719-file-processing-element.md) - D10-D16, D19,
D20, D21, O5; D25 and D26 for this cut
**Test plan**: [tp-eng-92719-file-processing-element.md](../test-plans/tp-eng-92719-file-processing-element.md)
**Status**: review (package #97, clio #1841 and knowledge #295 merged 2026-10-19, follow-ups #99 and #1846 the same
day). Reconciled box by box on 2026-10-20: OA-2, OA-11, OA-12, OA.12 and the Process item on PR titles and merge order
are reworded to the decision or PRD row actually taken, each citing where it is recorded, and ticked; OA.0 and the clio
analyzer item are ticked on evidence. Later the same day: OA.13 and Process items 3-5 closed by owner decisions; the
M26 record written, the `make check-knowledge` review of #1841 recorded, and clio items 7 and 8 ticked. Still open,
each with its reason: task "Knowledge records and status" (its status flips only); Definition of Done: Process items
2 (Jira) and 6 (status flips). Not `done` until they close
**Size**: L (about 15.5-27.5 h of AI-agent time and 5.5-13 h of the owner's time; about 4-6 working days of calendar;
the package PR is estimated at 6-6.8k added lines, the largest of the cycle)
**Repo**: crt-process-builder, then clio, then clio-knowledge (one PR each, titled
`ENG-96505 Element readiness and object attachments mode`, branch `feature/ENG-96505-process-file-object-attachments`;
each a draft stacked on the branch of ENG-95984 File process parameter type in the same repository until that PR
merges)
**Depends on**: this BMAD set merged before the package PR opens (pr-split edge E7). ENG-95984
(`story-eng-95984-file-parameter-type-1`) for delivery, not function (ADR D23: relates): the package PR leaves
draft only after ENG-95984's package PR merged (E3) and merges only after ENG-95984's clio PR
https://github.com/Advance-Technologies-Foundation/clio/pull/1814 merged, because that PR carries the `ManagerMap` arm
for `fileprocessing` / `processfile` (E4, E6); this story's clio PR merges only after ENG-95984's knowledge generation
https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/285 is PUBLISHED, because it writes the first
`name=process-files` (E5). Owner questions Q13-Q17 were answered on 2026-10-14, all as recommended, before the
package code and before the acceptance rows that encode them are pasted into Jira (see "Gating").
**Blocks**: story 2, ENG-96506 Generated report + process parameter modes (Jira link L3: RP registers its variants in
the handler and registry this story introduces); ENG-95985 Send email attachments (link L5, together with ENG-95984).

---

## As a

coding agent building a business process through the clio MCP process tools

## I want

to add, configure, change and read back a Process file element that reads a record's attachments, either handing the
files to a following element or saving them to another record's attachments, with one element type `fileProcessing`

## So that

the element is configured the way the designer configures it, the shapes that save green and fail at run time are
refused, and an element built in the designer or by an earlier generation reads back and survives a modify

---

## Acceptance Criteria

The full text is the PRD's OA table (it replaces the Jira acceptance criteria; agreed 2026-10-07). Each line below is
that criterion in short. A row marked "Qnn, agreed" encodes the owner's answer to that question (agreed 2026-10-14, as
recommended); it is pasted into Jira (plan FE.0b) as written.

- [x] **OA-1** (Q13, agreed) - `{type: fileProcessing}` (alias `processFile`) with an `attachments` group is
  added through create and `addElement`, connected like any element, and drawn as the designer draws it
  (`ManagerItemUId` = `SchemaUId`, 69x55, in a lane, `SerializeToDB` from the task schema).
  Evidence: package #97 (`1e8f7b30`): `FileProcessingElementHandler`, tests
  `FactoryCreate_ShouldBuildTheObjectElement_ForTheTokenAndItsAlias` and
  `Create_ShouldWriteSchemaUIdManagerItemAndSerializeToDbFromTheTaskSchema`; clio e2e
  `CreateBusinessProcess_Should_BuildAProcessFileElement_InDedicatedStorage` (size `69;55`), 73/73 on 1.6.6.103 (clio
  #1841).
- [x] **OA-2** (reworded 2026-10-20 back to the PRD's OA-2 row) - describe returns a `fileProcessing` block in the write
  vocabulary with effective values, identity checks and describe-only members; feeding the write members back rebuilds
  the element. Designer-built and builder-built Object elements read back and survive a modify. Report and
  Process-parameter elements are described and preserved as generic user tasks in this cut; a modify of a process that
  holds one leaves it intact, as the PRD's OA-2 row says: the first unrelated builder save applies the platform's
  one-time re-sync of the element's parameters to the user-task template (an allow-list of changes, no runtime value
  changed), and every later save is byte-identical. A designer-built SysFile element is described (`storage: sysFile`,
  issues listed) and `setElement` / `setFilter` on it are refused, naming SF.
  Basis of the rewording: the PRD's OA-2 row, which this list only shortens, says "leaves it intact", not
  "byte-identical"; the first-save change is a measured platform fact
  (`docs/knowledge/platform/the-first-builder-save-normalizes-a-designer-built-file-task.md`: the design-session load
  re-syncs the parameters to the template; the run time reads the retyped `ConnectedObjectId` as a Guid either way),
  and the e2e asserts an allow-list for it, which #1841's gate-1 re-review accepted ("Review"). No separate owner
  decision on "amend or accept" was recorded before; the owner confirmed "accept intact" on 2026-10-20.
  Evidence: `FileProcessingBlockDescriber` (package `8f10526f`); e2e
  `DesignerFixture_Should_DescribeTheSysFileElement_AndRefuseItsChanges` and
  `DesignerFixture_Should_ChangeOnlyByTheFirstSaveNormalization_OnUnrelatedModifies` (TC-I-46), both passed in #1841's
  73/73 run on 1.6.6.103 (0 skipped). That run used a fixture an earlier builder save had already normalized, so on
  1.6.6.103 it proves the byte identity of later saves; the first-save allow-list was checked offline against the
  fixture's designer-original nodes (#1841 "Review"; a fresh-fixture run is a gate-3 advisory). The "generic user
  tasks" half held for this cut; story 2 (ENG-96506) registered the Report and Process-parameter variants.
- [x] **OA-3** - `ObjectFiles` / `ObjectFiles.File` and `CreatedObjectFileIds.Id` can be mapped by more than one
  downstream element in one request (a FileCollection and a multi-instance sub-process).
  Evidence: clio e2e `ModifyBusinessProcess_Should_BindObjectFiles_IntoTwoConsumers` (TC-I-35) and
  `CreateBusinessProcess_Should_RunASubProcessPerFile_OfTheProcessFileElement` (TC-I-72), green on 1.6.6.103 (clio
  #1841).
- [x] **OA-4** (Q13, agreed) - the variant registry resolves the Object schema by name; the group sent is the
  variant; no group, two groups, another variant's group on update, a mismatching `source` and a contradicting
  `userTaskName` are refused.
  Evidence: package #97: `FileProcessingVariantRegistryTests.FindByTaskSchemaUId_ShouldResolveTheObjectTaskByName`,
  `FileProcessingElementHandlerTests.Create_ShouldRefuseABlockThatNamesNoSingleVariant` and
  `Create_ShouldRefuseAUserTaskName_ThatContradictsTheGroup`,
  `FileProcessingConfigApplierTests.Apply_ShouldRefuseTheGate`.
- [x] **OA-5** - `{type: userTask, userTaskName: ObjectFileProcessingUserTask}` (any case, padding, the `performTask`
  alias, another name resolving to the same UId) is refused on create with a message pointing to `fileProcessing`; the
  Report and Process generic routes still build in this cut.
  Evidence: package `81c44e76`: `RefuseFileProcessingOnGenericRoute` (`UserTaskElementHandler.cs:91`),
  `UserTaskElementHandlerCreateTests.Create_ShouldTolerateGenericRoute_ForTheUnclaimedFileProcessingTasks`; clio e2e
  `CreateBusinessProcess_Should_RefuseTheGenericRoute_ForTheObjectTask` (TC-I-12).
- [x] **OA-6** (Q17 for the details, agreed) - `attachments.object` takes the record object or its file object;
  storage is derived as the designer derives it; SysFile-mode sources and targets are refused with the D15 text.
  Evidence: package `ccaadc85`: `AttachmentStorageResolver`, `AttachmentStorageResolverTests`; clio e2e
  `CreateBusinessProcess_Should_RefuseSysFileStorage` (TC-I-17).
- [x] **OA-7** (Q16 and Q14, agreed) - `attachments.recordId` from a constant (existence-checked), a process
  parameter, an element output (+ `sourceColumn`) or `[#SysVariable.CurrentUserContact#]` compiles into the filter;
  `numberOfRecords` 1..5000, default 50 with a notice; one sort column of the runtime root.
  Evidence: package `c6b41fd1`: `FileProcessingScopeFilter`, `FileProcessingRecordScopeTests`,
  `FileProcessingConfigBinderTests.Apply_ShouldRefuseANumberOfRecordsOutOfRange`; clio e2e
  `CreateBusinessProcess_Should_ScopeTheAttachments_FromEachValueSource` (TC-I-18).
- [x] **OA-8** - `saveTo` writes the legacy target pair and the link column; a constant `saveTo.recordId` must be an
  existing record of the target object; a mapped one gets the "may be empty at run time" notice; `action` is inferred
  on create and always written (the inference is Q15's, agreed 2026-10-14; the PRD row itself encodes no owner answer).
  Evidence: package `FileProcessingRecordRules`,
  `FileProcessingConfigBinderTests.Apply_ShouldWriteTheTargetTriple_WhenTheElementSaves` and
  `Apply_ShouldWriteTheAction_OnCreate`; clio e2e
  `CreateBusinessProcess_Should_StoreTheTargetRecord_FromEachValueSource` (TC-I-28).
- [x] **OA-9** (Q16, agreed) - a saving Object element with no record scope and no filter condition at the end
  of the request is refused before the pre-save gate with `failedOperationIndex: null`; a "use in process" one gets a
  notice; untouched elements are never examined.
  Evidence: package `FileProcessingEmptyFilterTests.Modify_ShouldRefuseBeforeTheGate_WithNoFailedOperationIndex` and
  `Modify_ShouldNoticeAUseInProcessElement_ThatSelectsNothing`; clio e2e
  `ModifyBusinessProcess_Should_RefuseASavingElementWithAnEmptyFilter_WithNoOperationIndex` (TC-I-22).
- [x] **OA-10** - the builder's Object element equals SkillFilesValidationProcess (read side) and FileCopyProcessPP
  (save side) under the TC-41 comparison rule, with the parameters FileCopyProcessPP predates (`ConsiderTimeInFilter`,
  `SourceDataEntitySchemaUId`, `TargetDataEntitySchemaUId`) excluded and named in the test until SC-1's E2 replaces
  that oracle.
  Evidence: package `FileProcessingParityTests.ObjectElement_ShouldEqualSkillFilesValidationProcess_OnTheReadSide` and
  its save-side twin; `PredatedByFileCopyProcessPP` names the three excluded parameters.
- [x] **OA-11** (reworded 2026-10-20 to the decision taken) - `process-files` gains the element and Object sections,
  with an endpoint sentence that states the shipped pattern (the shipped product processes end in a record's
  attachments; when a following element needs the files, map its output collection) and never "rarely an endpoint".
  Decision: analysis decision D-3 (`spec/eng-92719-file-processing-element/eng-92719-file-processing-element-decisions.md`,
  the D-3 change table and review item R3-3) replaced the Jira wording "rarely an endpoint", because the corpus shows
  the 4 product processes (5 elements) all end in a record's attachments; TC-88 pins the replacement and the absence of
  "rarely an endpoint". The owner's reply 557730 on ENG-96505 (2026-10-19) declined Artem Kulykov's request (QA comment
  557683) to add "rarely an endpoint", citing D-3 and TC-88. The shipped wording is not D-3's literal sentence: the
  gate-3 refute-first fact check of #295 narrowed "Most shipped uses" to "Shipped product processes ... (Generated
  report)" (clio-knowledge `80a206c`; final wording `4aa62d3`), because the Object and Process-parameter variants occur
  only in test packages; D-3 and the PRD row still carry the "Most shipped uses" wording, and 557730 and #297's
  description quote that wording as if the guide kept it. That narrowing is the author's fact-check correction, not a
  separate owner decision.
  Evidence: clio-knowledge #295 (published as 1.15.159): `guidance/mcp/guides/processes/files.md` reads "Shipped
  product processes end in a record's attachments (Generated report); when a following element needs the files, map
  its output collection (Recipes)."; pinned by
  `ProcessFileGuidanceTests.Section_ShouldStateTheEndpointConsumersAndTheReFind`, which also asserts that no process
  article contains "rarely an endpoint" (TC-U-88). The sentence is unchanged on clio-knowledge master 1.15.163.
- [x] **OA-12** (reworded 2026-10-20 to the decision taken) - unit tests, e2e, stand verification, descriptions,
  prompts and guidance in the same change, merged package, then knowledge, then clio.
  Decision: the gate-3 review of #1841 changed the merge order so that the guide is published before the clio
  descriptions point agents at `process-files` (#1841 "Gate 3": "the merge order changed so the guidance goes first";
  the merge-order lines of #1841 and #295: an older clio or package refuses `type: fileProcessing` loudly, so the
  guidance can go first).
  Evidence: one coordinated change, #97 (package, 3989 tests), #295 (guidance) and #1841 (clio unit tests, e2e,
  descriptions, prompts), merged by d-krestov in that order on 2026-10-19 (08:35:49Z, 08:36:09Z, 08:37:01Z). Stand
  verification before the merge: ST-01, ST-02, DT-01, DT-02 and SC-4 passed on 1.6.6.100-1.6.6.102 (#1841
  "Validation"; dates and versions in the Dev Agent Record below) and the e2e passed 73/73 on the cut 1.6.6.103. The
  repeat on the final cut is OA.13's item, not this criterion's.

---

## Gating: owner questions and measurements

| Item | State (2026-10-14) | What waits on it |
|---|---|---|
| Q13 token and discriminator; Q14 naming bundle; Q15 action inference; Q16 record scope and empty-filter policy; Q17 storage details | agreed 2026-10-14, each as recommended in the ADR (Q16 knowing the ENG-102537 Process elements built without their configuration block save green and fail or hang at run time precedent) | the package code (OA.1-OA.4) AND the acceptance rows OA-1, OA-4 (Q13), OA-6 (Q17), OA-7 (Q14, Q16), OA-9 (Q16) and the action inference of OA-8 (Q15): they stand as written and are pasted into Jira (plan FE.0b). Q16's "notice everywhere" was not taken, so OA.4 keeps the pre-gate refusal and its touched-element record |
| Q20 description bytes | agreed 2026-10-14: A (a reviewer-approved trim per tool) | the clio descriptions (OA.10) |
| M10, M11 (a)(b), M14, M17-Q7, M13 | done 2026-10-02 (CrtProcessBuilder 1.6.6.54) | the package code: refusal set R3-R5, the H-G3-1 issue severity (not reproduced in memory; the guide warning and the bug-report Sub-task follow only if the optional M11 (c) or DT-01 confirms it), the D20 pins, the empty-record notice wording, the TC-45 Object fixtures |
| M24 | done 2026-10-02 | the guide sentence on `attachments.sort`: a wrong JSON shape is nulled by the WCF binder and refused later with a misleading text |
| M26 (after a save, `ObjectFiles` holds the SOURCE locators; traps T-19) | open; a designer-built probe by the user, a write | the OA.12 sentence and the CL-OA record `after-saving-object-files-points-at-the-source-files.md` only; no code |
| Fixture `UsrFpOaDesignerFixture` | not built; built once by the user in the designer (a write) | the two designer-fixture e2e cases of OA.11 (TC-46); without it they are Ignored, and the Definition-of-Done run needs Ignored = 0 |
| Capture session SC-0, SC-1, SC-2 (plan FE.0d; SC-1 is one designer-built process `UsrFpSc1Capture`, saved once by the user) | pending | the SC-4 comparison of this cut (E1, E2); SC-1 also serves M7 and M21 for story 3 |
| M22 (DT-01, DT-02) | after the cut | the package PR leaving draft |

---

## Tasks

`PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/`. Ids are the analysis plan's work packages (section 5.1).

**Stand (before code)**

- [x] **OA.0** M10, M11 (a)(b), M14 and M17-Q7 are done (2026-10-02); what remains is M26 (a designer-built probe;
  gates the guide sentence and the record, not code) and the fixture `UsrFpOaDesignerFixture` that OA.11 reads: process
  in package `Custom`, caption "FP OA designer fixture", built once by the user and never run; a Lookup parameter
  `PInvoice` -> Invoice; F1 = Process file, Object attachments, `Account address` (SysFile storage, nothing else set);
  F2 = Generated report, printable `Invoice`, filter Id = `PInvoice`, Save to object attachments -> Invoice's attachment
  object, record = `PInvoice`; Start -> F1 -> F2 -> End. Each write after the user's go-ahead.
  Evidence: M26 measured 2026-10-15 on CrtProcessBuilder 1.6.6.100, recorded in clio-knowledge #295 "Evidence"
  ("after saving, `ObjectFiles` points at the source files"; the ST-01 run trace: `ObjectFiles` holds the source
  locators, the copies only `CreatedObjectFileIds`). `UsrFpOaDesignerFixture` built in the designer on 2026-10-15
  07:51 UTC (UId 33c13bf7-ae2d-4982-ac12-ec8ebc9d4406, the owner's stand handoff); the tracked record
  `docs/knowledge/platform/the-first-builder-save-normalizes-a-designer-built-file-task.md` measures on it, and both
  designer-fixture e2e cases ran green in #1841's 73/73 (0 skipped) on 1.6.6.103. The fixture was later removed with
  the stand's Custom package (#1846) and has not been rebuilt; rebuilding it is carried to ENG-103937 (story 3), whose
  SF.3 re-points the fixture's F1 case.

**CrtProcessBuilder** (base `main`; draft on ENG-95984's branch until it merges)

- [x] **OA.1** Family core (ADR D10, O5): `ElementTypes.FileProcessing = "fileprocessing"` plus `"processfile"`; the
  variant registry (`IFileProcessingVariant`, `FileProcessingVariantRegistry`, keyed by the schema UId that
  `UserTaskSchemaIdentity` resolves by NAME; the three names mirrored next to `ProcessDesignConstants.UserTasks`, the
  UIds only in fixtures and the drift guard) with the Object variant as its first entry; `FileProcessingElementHandler`
  (`Create`: `SchemaUId`, `ManagerItemUId`, `SerializeToDB` from the task schema, nested item UIds re-minted with `IL2`
  = the element UId; `CanBuild` / `CanDescribe` through the registry), registered BEFORE `UserTaskElementHandler` in
  `CrtProcessBuilderApp.cs`; the strict block gate; `WritesAConfigurationBlock`; the `setElement` field list and
  dispatch; `FileProcessingDescriptor` (all three groups with their members, so that "two groups" and "another
  variant's group" can be refused by name, and the shared `FileProcessingValueSourceDescriptor`) on the build and
  update contracts (`PB/Contracts/ProcessDescriptorContracts.cs`, `PB/Contracts/ModifyContracts.cs`); a group no
  registered variant owns (`report`, `files`) is refused as not written yet. The registry is built from the
  connection by every consumer, not registered in the container. The strict keys of ENG-95244 [Arch debt] Proven
  Solutions: give the process descriptor a real schema and wire the R1-R17 graph validator into the write path
  (ENG-88414) (ENG-88414 is AI-driven application development) and this block meet in whichever of the two lands
  second, which teaches the allow-list the write members and the read-back list the describe-only members; this
  package change writes no allow-list.
  Evidence: package `1e8f7b30`: `ProcessDesignConstants.cs:591,597`, `FileProcessing/FileProcessingVariantRegistry.cs`,
  `Elements/FileProcessingElementHandler.cs`, registered before `UserTaskElementHandler` in
  `CrtProcessBuilderApp.cs:149-155`; tripwire `CrtProcessBuilderAppTests.cs:184,222`.
- [x] **OA.2** Binder and applier (D11, D12, D14): a static `FileProcessingConfigBinder` shared by the handler's
  `Configure` and by `setElement` (the `OpenEditPageConfigBinder` structure): validate everything before the first
  write; the D11 parameter table; D14 inference; caller-chosen values as new `ProcessSchemaParameterValue`s (`Source`
  before `Value`, `ModifiedInSchemaUId` = the process); template defaults copied exactly (D20); value sources through
  `ApplyMapping` (a mapped `saveTo.recordId` through the new `IProcessMappingService.ApplyBlockMapping`, which skips
  the raw-route `ConnectedObjectId` rules the binder has already run; it is the one write that can still refuse, so
  it goes first). The binder is static and split into two internal static helpers, `FileProcessingStoragePlanner`
  (the storage pairs and the identity checks) and `FileProcessingRecordRules` (the `saveTo.recordId` rules a raw
  `ConnectedObjectId` mapping shares). Every value-source `expression` (`saveTo.recordId` here) runs the length bound and the
  `MetaPathTokenReference` check `openEditPage.recordId.expression` runs; the new surfaces join that check's call-site
  list, with a PBT case each (a misspelled token, a token from another process). `FileProcessingConfigApplier`: D12
  rules 1-5 (rule 5 per side: a sent object rewrites the pair; without it a consistent pair is kept and an
  inconsistent one re-resolved with a notice); the source-change refusal and the `useInProcess` refusal while
  `CreatedObjectFileIds` or its item is mapped (an action bound to a mapping counts as possibly saving there) take their
  dependents from an injected `IProcessDependencySnapshot` (`FindDependents(element)`, `FindDependents(element,
  parameter)`), worded as the refusals of ENG-100041 Sub-process retarget guard scans the live schema instead of the
  request dependency snapshot ("or did when this request started"; "in a SEPARATE, earlier call"), each dependent
  named qualified (`<element>.<parameter>`, `process parameter '<name>'`), asked from the applier only, never from the
  create path; a template parameter missing after `GetDesignInstance` is refused.
  Evidence: package `70fa897f`, split in `816bd9d9`: `FileProcessingConfigBinder`, `FileProcessingStoragePlanner`,
  `FileProcessingRecordRules`, `IProcessMappingService.ApplyBlockMapping`, `FileProcessingConfigApplier` over
  `IProcessDependencySnapshot`;
  `FileProcessingConfigApplierTests.Apply_ShouldNameTheDependents_WhenASourceChangeIsRefused`.
- [x] **OA.3** Storage resolver, dedicated mode (D15): `IAttachmentStorageResolver.Resolve` / `Decode` (never throws)
  over a thin `IEntitySchemaHierarchyReader` adapter; eligibility and the `<X>File` / `FileLead` rule; SysFile results
  refused through a switch SF flips; refusals R1-R9 (R9 also refusing a column every attachment object inherits from
  `File`, and a `linkColumn` that references another object than the record object), warnings W6 / W7; an
  attachment object no record object pairs with (`DNSGuideFile`), named directly, is dedicated storage with no
  record object, as the designer lists it; a constant `saveTo.recordId` validated as an existing record of the
  target's record object through `ProcessParameterValueValidator.EnsureRecordOfObjectExists`, a new internal entry
  point over the private `EnsureReferencedRecordExists`; a mapped `recordId` gets the notice and is typed against the
  target's record object (a Lookup of it or a Guid; one column of an element's record a lookup of it or its `Id`).
  Evidence: package `ccaadc85`: `IAttachmentStorageResolver` / `AttachmentStorageResolver`,
  `IEntitySchemaHierarchyReader`, `ProcessParameterValueValidator.EnsureRecordOfObjectExists`
  (`ProcessParameterValueValidator.cs:319`); `AttachmentStorageResolverTests`.
- [x] **OA.4** Record scope, filter target, sort, empty filter (D16): `FileProcessingScopeFilter.Split` / `Join`
  (legacy scope = a lookup InFilter on the link column; a root OR wrapped as `AND(scope, OR)`; no lift from a decode
  that is not `DecodedCompletely`; `Join` keeps `isNull`); `FileProcessingFilterTarget` (priority 50, between Signal 100
  and DataNode 0) claiming through the registry, with the root, unconfigured-element, type and scope-set-twice
  refusals; the filter target keeps the record scope (`setFilter` writes `Join(storedScope, newRemainder)`,
  `clearFilter` keeps the scope) and `TryRead` reports the filter without it; the **sort codec extraction**:
  `ResolveOrderInfoValue` + `ParseSortDirection` and `DescribeSort` + `DescribeSortDirection` move out of
  `ReadDataConfigBinder` into a shared internal codec, `PB/Elements/ReadDataSortCodec.cs`, that takes the element
  kind, its label and the member name, so the Read data refusals stay byte-identical, and `ReadDataConfigBinder`
  calls the codec; `numberOfRecords` 1..5000 and the "at most 50 files" notice on every create that omits it; the
  empty filter in two halves: the **notice** is one `Rules` entry for the Object schema name in
  `PB/Elements/ElementConfigurationNotices.cs` plus a `Describe...` method, reusing `HasSelectingFilter` and
  `ProcessSchemaElementLocator.HasEffectiveValue` (no new tracking, no new reconcile site); the **refusal** is a new
  pre-gate check with its own record of touched elements, a separate `IFileProcessingFilterLedger`
  (`IElementConfigurationLedger`'s contract unchanged), told by the caller of each touch (the build for `create`,
  `AddElementOperation` for `addElement`, the applier, the filter target) about the block written and the filter set
  or cleared, never about a raw `addMapping`, called on modify next to
  `EnsureFormulasStillBelongOnTouchedConnectors` (`PB/Design/ProcessEditPipeline.cs`) and on build at the end of
  `ApplyDeclarativeContent` (`PB/Design/ProcessBuildHandler.cs`), thrown outside the operation loop so
  `failedOperationIndex` is null; the `ProcessFilterApplier` refusal text lists the element.
  Evidence: package `c6b41fd1`: `FileProcessingScopeFilter`, `Filters/FileProcessingFilterTarget.cs`
  (`FileProcessingFilterTargetTests.Priority_ShouldSitBetweenTheSignalAndTheDataNodeTargets`),
  `Elements/ReadDataSortCodec.cs`, `IFileProcessingFilterLedger`; `FileProcessingEmptyFilterTests`.
- [x] **OA.5** Refusals of the other routes (D13): the generic `userTask` route naming the Object schema, keyed on the
  resolved schema UId, `RefuseFileProcessingOnGenericRoute` in `UserTaskElementHandler.Create`
  (`PB/Elements/UserTaskElementHandler.cs:91`), after the two merged refusals of ENG-102112
  create-business-process silently accepts a generic userTask PreconfiguredPageUserTask without the preconfiguredPage
  block and ENG-102537 (`:89-90`) and before the element is created (`:92`), in their shape (create-only; the message
  names the element, the task, the type used, `fileProcessing` with its group and the `setElement` repair); tests for
  the plain name, another name resolving to the same UId, and a "tolerated" case for the Report and Process schemas
  (pattern `PBT/UserTaskElementHandlerCreateTests.cs`, every task schema on ONE manager). Raw `addMapping` onto the
  13 binder-owned configuration parameters (10 exist on the Object task; `ReportId`, `IsSeparateReports` and
  `ReportNameDataSourceColumnUId` only on the Report task) refused, naming the block member, by a guard of its own,
  `EnsureNotABlockOwnedFileParameter` (`PB/Mappings/ProcessMappingService.cs:366-379`), next to the
  `FindDataObjectSlot` / `EnsureTheDataObjectIsKept` slot table of ENG-102113 modify-business-process addMapping: Add
  data object (EntitySchemaId) is validated against the record Id instead of the schema UId, which returns no slot
  for a file element; `ConnectedObjectId` stays legal and runs `saveTo.recordId`'s rules (any source refused while
  the element saves to no object). `UserTaskElementHandler` builds the registry from the connection, as it builds
  the identities, so its constructor, its registration and its direct constructions in the tests are unchanged.
  Evidence: package `81c44e76`: `UserTaskElementHandler.cs:91` and `:410`, `EnsureNotABlockOwnedFileParameter`
  (`ProcessMappingService.cs:366`); `UserTaskElementHandlerCreateTests`, `FileProcessingRawMappingTests`.
- [x] **OA.6** Describe and serialization (D19, D20): `DescribeFileProcessingInfo` on `DescribeProcessElement`: effective
  values, identity checks vs describe-only members (an identity check also accepts the describe value of a side the
  block keeps, so a described unpaired or invalid pair resubmits and is repaired; describe-only: `actionStored`,
  `outputs`, `issues` and the `display` of every value source), `storage` from `Decode`, the record scope lifted by
  `Split` and reported once (`FileProcessingFilterTarget.TryRead` returns the filter without it), value sources
  decoded from single tokens, `issues` (link column missing; a sort or filter column the root lacks; hidden
  `OrderByInfo` entries; an unset action; an inconsistent storage pair), tolerant of unconverged snapshots;
  nested-source display values in the designer form `[#<el>.<collection>:<item>#]` on every binding path (this
  changes the multi-instance per-item display too); a top-level source keeps its bare caption (the designer's
  `[#X#]` for it is out of scope, plan section 9).
  Evidence: package `8f10526f`: `FileProcessing/FileProcessingBlockDescriber.cs`; `FileProcessingDescribeTests` (for
  example `Describe_ShouldLiftTheRecordScope_FromEachValueSource`).
- [x] **OA.7** Package tests (test-plan unit cases of this story): handler, binder, applier, resolver (fake hierarchy
  reader), scope filter, filter target, empty-filter refusal on both paths and the notice rule, describe round trip
  (issues, a uk-UA caption, the identity-check refusal), the registry ("all five or none"), the D20 writes, parity with
  SkillFilesValidationProcess, the tripwire; the extracted sort codec and the **Read data regression**:
  `PBT/ReadDataConfigBinderTests.cs` and `PBT/ReadDataConfigApplierTests.cs` green with their assertions unchanged
  (every existing `SetElementOperation` construction gains the applier argument: 14 constructions in 9 test files,
  the Read data applier suite's three among them). Every new fixture
  `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`, AAA, `because:`, `[Description]`.
  Evidence: package #97: every new `FileProcessing*`, `AttachmentStorageResolverTests`,
  `EntitySchemaHierarchyReaderTests` and `ReadDataSortCodecTests` fixture is `[TestFixture(Category = "UnitTests"),
  Category("PreCommit")]`; `ReadDataConfigBinderTests.cs` is unchanged and `ReadDataConfigApplierTests.cs` gains only
  the applier argument (`eb465c8c..4a277295`); 3989 passed on `-c dev-nf` and `-c dev-n8`.
- [x] **OA.7a** Carried over from the review of the ENG-95984 package PR
  (https://creatio.ghe.com/engineering/crt-process-builder/pull/96), deferred there so that its approval stays:
  (a) An empty `value` on a File or FileCollection target is refused like any other constant:
  `ProcessParameterValueValidator.ValidateConstantValue` runs the File and FileCollection refusals before the
  empty-value no-op, which every other type keeps; pinned with a test on every constant route.
  (b) `ProcessMappingService.Commit`'s doc says that a throw from the platform's `SourceValue` setter (its Mappings
  sync) can leave earlier writes of the same plan applied in memory with no notice, and that nothing persists
  because the request does not save. `Plan`'s guarantee covers only our own refusals.
  Evidence: (a) package `f180ecc3` (`ProcessParameterValueValidator`), stated under "Visible behaviour changes" in #97;
  (b) the `Commit` doc at `ProcessMappingService.cs:182-190`.
- [x] **OA.8** Package docs and restamp: NEW `docs/process-file-element-capture.md` (the convention of the existing
  `docs/*-capture.md`), `docs/process-builder-architecture.md` rows, `.codex/workspace-diary.md` entry; restamp
  `packages/CrtProcessBuilder/descriptor.json` LAST to the number claimed before cutting, in the PR description AND in
  the one shared cut claim (a comment on ENG-92719 File processing element; the next free number in it at cut time);
  tag `crtprocessbuilder-<version>` pushed and verified with `git ls-remote --tags origin`.
  Evidence: `docs/process-file-element-capture.md`, `docs/process-builder-architecture.md:147-164`,
  `.codex/workspace-diary.md:5174`; restamp `55517e20` last before the merge `0091cab9`; 1.6.6.103 claimed in ENG-92719
  comment 550565 (#97 "Cut"); `git ls-remote --tags origin` shows `crtprocessbuilder-1.6.6.103` -> `ca243062` (checked
  2026-10-20).

**clio** (base `master`; one PR, because the guards bind archive, pins, floors, descriptions, call shapes and key sets
to one tree)

- [x] **OA.9** Rebundle and floor (D26): `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <package at PR head>
  -Version <claimed>`; the archive and the four pins in `clio.tests/Common/BundledProcessBuilderPackageTests.cs`; the
  three `[RequiresPackage]` literals (`clio/Command/CreateBusinessProcessCommand.cs`,
  `ModifyBusinessProcessCommand.cs`, `ModifyProcessAsNewVersionCommand.cs`) to the FINAL cut with their comment blocks;
  `ProcessDesignerRequiresPackageAttributeTests`; the enforced-floor clause in three descriptions; `McpCapabilityMap.md`
  floor rows; the new fixture's `MinimumPackageVersion`; all in the archive's commit. Install from the refreshed BUILD
  OUTPUT, each install after the user's explicit approval. A longer floor literal (a new digit) costs a byte in three
  descriptions: the budget test catches it, this PR trims (ADR D25).
  Evidence: clio `e4d471042` (`-Version 1.6.6.103`, #1841 "Rebundle"): the archive, the pins in
  `BundledProcessBuilderPackageTests.cs`, the three `[RequiresPackage]` literals at 1.6.6.103,
  `ProcessDesignerRequiresPackageAttributeTests`, the tool descriptions, `docs/McpCapabilityMap.md` and the e2e
  `MinimumPackageVersion`; installed from the build output (`eng-96505-stand-handoff.md`).
- [x] **OA.10** Descriptions, call shape, key set, DTO, prompts (D11, D13, D19, D25):
  - (a) create / modify / describe descriptions: the token in the type list, the block clause naming
    `get-guidance name=process-files`, the `addElement` / `setElement` lists, the describe clause (about +128 / +74 /
    +128 B, estimated). The bytes come from a reviewer-approved trim of existing text per tool (Q20 = A), measured by
    lowering the ceiling, re-measured after ENG-95984's clio PR has trimmed first (on `79fab4786` create had 11 B and
    modify 2 B left); the ceiling is not raised; the figures in the comment of
    `clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs` rewritten; `this clio requires <floor>` and the
    `stopped validating formulas` clause left intact.
  - (b) in the archive's commit, `CreateElementBlockShape` (`clio/Command/McpServer/Tools/ToolContractShortForm.cs`)
    gains the `fileProcessing` block, or `CreateCallShape_Should_NameExactlyTheMembersOfTheBundledServerContract`
    fails; the entry (about 25-30 B) is never cut, so kept short-form text of one of the seven process-designer
    contracts is trimmed (reviewer-approved) to stay under the 19,000 B wire guard.
  - (c) in the archive's commit, a `fileProcessing` `KeySet` in `clio/Command/ProcessModel/ProcessBlockKeyCheck.cs`
    (ENG-103085 Refuse element and block keys the process builder would drop silently): its groups as children, all
    three of them (`attachments`, `report` and `files`, with their members, because the server contract declares
    them from OA on), the
    describe-only members of `DescribeFileProcessingInfo` as read-back keys, listed in `Blocks` so it links into the
    create element and the `setElement` update; `KeySets_Should_EqualTheMembersOfTheBundledServerContracts` and
    `ReadBackFields_Should_BeDescribeOnlyMembers` (`clio.tests/Command/ProcessModel/ProcessBlockKeyCheckTests.cs`)
    green. Without it clio refuses every create or `setElement` that carries the block before it reaches the server.
  - (d) in the commit that writes the first `name=process-files`,
    `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` re-pinned to the PUBLISHED generation that
    carries `process-files`, with `process-files` and `creatio.process-files` added in sorted order.
  - (e) typed `DescribedFileProcessing` in `clio/Command/ProcessModel/IProcessDescriber.cs`, `[JsonExtensionData]` on
    every nested type, `///` summaries on every new type and public member; "forwards the block verbatim" tests; a
    permanent capability probe on `ProcessElementUpdateDescriptor.FileProcessing`; capability-map rows.
  - (f) `list-user-tasks` description (`clio/Command/McpServer/Tools/ListUserTasksTool.cs`): ObjectFileProcessingUserTask
    joins the exceptions as a dedicated type whose generic route is refused, outside the EmailTemplateUserTask /
    ApprovalUserTask fallback. Prompts: `ListUserTasksPrompt` excludes it from the generic-`userTask` advice and points
    to `fileProcessing`; `CreateBusinessProcessPrompt` gains the `fileProcessing` sentence (the group is the variant;
    not a generic `userTask`; `get-guidance name=process-files` owns the block); `ModifyBusinessProcessPrompt` gains the
    D12 merge rule; `DescribeProcessPrompt` lists `fileProcessing`; each pinned.
  Evidence: (a) trims approved by the owner 2026-10-19 (`7b0fc971e`, #1841 "Rebundle"), budget comment rewritten in
  `5768b62cb`; (b) `ToolContractShortForm.cs:87`, in the archive's commit `5768b62cb`; (c) `ProcessBlockKeyCheck.cs:276`
  with `ProcessBlockKeyCheckTests`; (d) already in place: ENG-95984's `99ea69cce` pinned `process-files` at guidance
  1.15.146 before `5768b62cb` wrote the first `name=process-files`, so no re-pin was due (#295); (e)
  `IProcessDescriber.cs:2255`, `[JsonExtensionData]` and `///` on every nested type, probe
  `BundledArchive_ShouldDeclareTheFileProcessingWireNamesClioReads`; (f) `ListUserTasksTool.cs:58`, the four prompts
  pinned by `ListUserTasksPrompt_ShouldRouteObjectFileProcessingToFileProcessing`,
  `ModifyBusinessProcessPrompt_ShouldStateTheFileProcessingClearingAndPairRules`,
  `CreateBusinessProcessToolTests.cs:460` and `DescribeProcessToolTests.cs:465`.
- [x] **OA.11** e2e: NEW `clio.mcp.e2e/ProcessFileElementToolE2ETests.cs` (`[Category(McpE2ECategories.ProcessDesigner)]`,
  `MinimumPackageVersion` = the floor, every describe through `ProcessDesignerE2EArrange.DescribeAsync` or with
  `mode=full` / `element=<name>`):
  - Object create + describe in dedicated storage (TC-I-06); `attachments.recordId` per source (TC-I-18);
    `saveTo.recordId` from a constant, a process parameter, `sourceColumn` and `[#SysVariable.CurrentUserContact#]`
    (TC-I-28);
  - Read data -> Process file (TC-I-70); Process file -> Modify data re-finding the created file (TC-I-71, built and
    described, not run); two consumers of `ObjectFiles` (TC-I-35); a multi-instance sub-process per file (TC-I-72,
    needs ENG-95984's File parameter); a partial `setElement` that keeps the sort (extends TC-I-06);
  - the refusals: the generic route (TC-I-12), the empty-filter save with `failedOperationIndex: null` (TC-I-22), a
    SysFile source and target (TC-I-17), and the source change and the filter root (extending TC-I-17);
  - `validate-process-graph` over a described `fileprocessing` element (TC-I-86);
  - two cases on `UsrFpOaDesignerFixture` (TC-I-46): F1 described read-only and its `setElement` / `setFilter` refused
    naming SF, F1 unchanged on re-read; an unrelated `addParameter` leaves F2's `SysSchema.MetaData` node and resource
    rows byte-identical (read from the raw `SysSchema` row, never from describe). When the fixture is absent:
    `Assert.Ignore` with the build instructions.
  - must stay green (TC-I-87): `ListUserTasksToolE2ETests`, `ValidateProcessGraphToolE2ETests`,
    `DescribeProcessToolE2ETests`, `SubProcessMultiInstanceToolE2ETests`, `RecordColumnSourceToolE2ETests`, the
    ENG-95984 fixture `FileParameterToolE2ETests`.
  Evidence: `clio.mcp.e2e/ProcessFileElementToolE2ETests.cs` (`MinimumPackageVersion` 1.6.6.103; TC-I-06, 12, 17, 18,
  22, 28, 35, 46, 70, 71, 72, 86 and the partial-`setElement` sort case); #1841: 73/73 passed, 0 skipped on 1.6.6.103,
  per fixture. TC-I-46 (2) asserts the first-save allow-list, not byte identity (see OA-2).
- [ ] **Knowledge records and status** (analysis plan section 7), in this clio PR; each record states its basis: NEW
  `docs/knowledge/platform/the-process-file-source-is-three-user-task-schemas.md` (the palette and the replacement
  measured in the designer, 2026-10-01; the rest source),
  `platform/an-unset-result-action-type-runs-as-save-to-files.md` (basis: source, not measured: the runtime reading of an
  unset value),
  `platform/object-file-processing-filters-on-the-file-object.md` (basis: source, not measured: the runtime root, the
  zero-file read and the arbitrary-file read),
  `platform/attachment-storage-is-chosen-per-entity-by-the-designer.md` (the per-object choice measured in the designer,
  UO-2..UO-4; the SysFile link and target keys source),
  `platform/a-file-copy-without-a-link-column-lands-on-the-source-record.md` (basis: source, not measured:
  `CopyAttributes`),
  `platform/a-server-built-user-task-takes-the-existing-process-default.md` (M14 measured it),
  `platform/designer-stamps-modified-in-schema-only-on-a-changed-value.md` (measured over the corpus),
  `ProcessModel/report-and-parameter-file-elements-stay-generic-until-eng-96506.md` (temporary; deleted by RP),
  `ProcessModel/process-file-sysfile-storage-is-refused-until-sf.md` (temporary; deleted by SF);
  `platform/after-saving-object-files-points-at-the-source-files.md` only after M26; UPDATE
  `ProcessModel/described-filter-types-have-no-json-overflow-bag.md`; review the `make check-knowledge` report.
  Status flips in `spec/sprint-status.yaml` (see "Status convention"), including the `done` flip of
  `story-eng-95984-file-parameter-type-1`, which rides in this PR.
  Re-checked 2026-10-20. Done: the nine records and the update of
  `described-filter-types-have-no-json-overflow-bag.md` are on master (`56e6aebee`, `5768b62cb`), and story 2's clio PR
  #1847 deleted the temporary `report-and-parameter-file-elements-stay-generic-until-eng-96506.md` as planned. The M26
  record `platform/after-saving-object-files-points-at-the-source-files.md` was written on 2026-10-20 in the docs PR
  #1857 (basis: the stored outputs measured on 1.6.6.100 in the ST-01 run's element trace, the mechanism source; the
  multi-instance consumer half of M26 was never run, and the record says so). The `make check-knowledge` review of
  #1841 is recorded: 103 records listed for `24bfa471b^1..24bfa471b`, 96 kept, 6 updated in #1857, 1 already deleted
  by #1847 (log in the Dev Agent Record; every record in
  `spec/eng-92719-file-processing-element/eng-92719-file-processing-element-knowledge-review.md`).
  Open: the status flips did not follow the convention: `in-progress` was never recorded, `review` arrived only with
  #1847, and the `done` flip of `story-eng-95984-file-parameter-type-1` was not made (it stays at `review`).

**clio-knowledge** (base `master`, own worktree under `.worktrees/<task>/`)

- [x] **OA.12** (reworded 2026-10-20 to the decision taken: the endpoint sentence as in OA-11, and the saved copies
  re-selected with Read data, not Modify data) `guidance/mcp/guides/processes/files.md` (`process-files`) sections: the element and "the group is the
  variant"; `attachments` (`recordId`; `numberOfRecords` defaults to 50 and does NOT read all; the sort shape with the
  M24 result); the member-wise merge; identity checks vs describe-only ("remove the describe-only members before
  resubmitting", naming `actionStored`, `outputs`, `issues` and each value source's `display`); dedicated storage
  and "SysFile storage is refused in this version"; the action per variant (the
  sentence "after saving, `ObjectFiles` points at the source files" only after M26, as "measured <date>"); the endpoint
  sentence of OA-11 as reworded above, never "rarely an endpoint"; consumers (Send email attachments and Creatio.ai not
  buildable through this tool yet); the re-find pattern for the saved copies (Read data on the target's attachment
  object) with its hazards. `element-catalog.md`: the
  buildable row and the generic-route sentence worded for this cut (only the Object schema is refused; Report and
  Process-parameter elements are "not buildable yet; do not build them as a generic userTask"); "FOUR user tasks have
  their own dedicated build type" becomes FIVE together with the three knowledge tests that pin FOUR
  (`ProcessDigestTests.TheCardsDedicatedTypes_ShouldEqualTheCatalogsFour`, the `RulesHandedBackToTheirOwners` tuple in
  `ProcessDigestTests`, `ProcessPreconfiguredPageCatalogGuidanceTests`) and the `process-digest` card's "(never as a
  userTask)" list, which must list `fileProcessing` (about 3 bytes free: a trim on the card). Every read-back says
  `describe-business-process mode=full` (or `element=<name>`) under the "on clio older than 8.1.0.141, omit `mode`"
  gate; consent wording within 80 characters of every `install-process-builder`; no Jira key or PR link. NEW pin test
  `automation/Clio.Knowledge.Bundle.Tests/ProcessFileGuidanceTests.cs`; libraryVersion above master and every branch
  claim at merge.
  Decision: the gate-3 refute-first fact check of #295 (two agents barred from the author's records) corrected two
  items, recorded in #295 "Evidence" and in clio-knowledge `80a206c`: the endpoint sentence (see OA-11, decision D-3),
  and the re-find, because Modify data has no output to read the copies with, so they are re-selected with Read data.
  Evidence: clio-knowledge #295 (published as 1.15.159, 2026-10-19) carries the sections, FOUR -> FIVE with its three
  tests and the card, `ProcessFileGuidanceTests` (the endpoint and re-find pins in
  `Section_ShouldStateTheEndpointConsumersAndTheReFind`), and libraryVersion 1.15.159 over master's 1.15.156; the
  M26 sentence follows the 2026-10-15 measurement (#295 "Evidence").

**Tail**

- [x] **OA.13** Stand verification after the final cut (below); gates 1 and 3 (comprehensive, three lenses) and their
  fixes; one human review round (one push: fixes, restamp and rebundle together); knowledge publication and the
  `info-knowledge` check.
  Re-checked 2026-10-20. Done:
  - gates 1 and 3 and their fixes: recorded in #97, #1841 ("Review", "Gate 3") and #295 ("Evidence");
  - one human review round with at most one push per PR: on #1841 Anton Chernov commented at 07:56Z, one push followed
    (`0efe302d5`: two e2e gate constants and comment text, from the review's two notes), and Artem Kulykov approved at
    08:10Z; #97 and #295 got no push after their
    reviews (#97's last commit is the restamp `55517e20`, made before the reviews);
  - knowledge publication: release 1.15.159 (#295, 2026-10-19); the `info-knowledge` check, run on 2026-10-20 from a
    build of clio master `ea3aaf580`: library 1.15.163, revision `6fa3f91` (the #297 merge, which contains #295's
    merge `8452003`);
  - on the final cut 1.6.6.103: the e2e 73/73 with 0 skipped (#1841) and TC-01..TC-10 as business tasks in isolated
    agent sessions, all PASS (the owner's report `eng-96505-test-run-report.md`; TC-01 is the ST-01 shape, a run
    copying an account's files to a contact; TC-04 the ST-02 shape, a sub-process per file); on the follow-up cut
    1.6.6.104, Artem Kulykov's QA pass, 12 of 14 PASS with both defects fixed in ENG-96506 (ENG-96505 comments 557683
    and 557730; his TC-01 includes a no-op designer save that keeps the process working).

  Accepted by the owner 2026-10-20: the 1.6.6.102 runs stand for the final cut 1.6.6.103, whose only change over
  1.6.6.102 is a modify-time refusal (`ca243062`) that neither comparison exercises.
  Was open: the DT-02 and SC-4 comparisons (and the literal ST-01, ST-02 and DT-01 procedures) ran on 1.6.6.100-1.6.6.102
  only, before the final cut (dates and versions in the Dev Agent Record). The final cut adds one modify-time refusal
  over 1.6.6.102 (package `ca243062`), but no comparison was repeated on it. Close by repeating DT-02 and SC-4 on the
  current package, or by an owner decision to accept the 1.6.6.102 runs.

---

## Stand verification

After the cut, before the package PR leaves draft, on the .NET Framework stand (common protocol: build this clio PR,
`install-process-builder` from the refreshed build output after the user's approval, `list-packages` shows the cut, one
behaviour probe only the new code answers - a create with `fileProcessing` + `attachments` -, `get-tool-contract` shows
the new vocabulary):
- **ST-01** Object SaveToFiles in dedicated storage: an Account with 2 attachments copied to a Contact. Pass: 2
  `ContactFile` rows with `Type = File`, linked to the target; the sources unchanged; `CreatedObjectFileIds` 2 rows.
- **ST-02** Object "use in process" into a multi-instance sub-process per file (needs ENG-95984 merged). Pass: 2
  iterations, not 1 (P1 bound the parent).
- **e2e** `ProcessFileElementToolE2ETests` and the must-stay-green fixtures, one after another: **Passed with Ignored =
  0** (the empty-filter refusal with `failedOperationIndex: null`; the designer-fixture cases on
  `UsrFpOaDesignerFixture`).
- **DT-01 / DT-02 (M22)**: the user opens ST-01's element and does a no-op save (metadata equal before and after; no
  `SourceDataEntitySchemaUId = AccountFile` afterwards), and builds the same element by hand (equal under the TC-41
  rule).
- **SC-4** the builder twin of SC-1's E1 and E2 (`UsrFpSc4<Cut>`, one create request): equal under the TC-41 rule;
  E3-E5 refused with the D15 text until SF.

A failed runtime proof stops the merge. Every write and every install needs the user's go-ahead; one at a time;
evidence from `SysProcessElementLog`; cleanup through `execute-dataservice-batch`.

## Status convention

The tracker lives in clio, so a status flip needs a clio commit: `in-progress` when this story's clio draft opens;
`review` when the clio PR leaves draft (deliberately not when it opens as a draft, because the chain is stacked);
`done` only after the knowledge PR merges, carried in the next clio PR of the chain (story 2's) and never pushed after
an approval.

## Definition of Done

**CrtProcessBuilder**
- [x] `fileProcessing` / `processFile` build the same element; the registry holds the Object variant; the handler is
      registered before `UserTaskElementHandler`, and the tripwire pins set and order.
      Evidence: `FileProcessingElementHandlerTests.FactoryCreate_ShouldBuildTheObjectElement_ForTheTokenAndItsAlias`,
      `FileProcessingVariantRegistryTests.RegisteredVariants_ShouldBeExactlyTheObjectAttachmentsVariant`,
      `CrtProcessBuilderApp.cs:149-155`, tripwire `CrtProcessBuilderAppTests.cs:184,222`.
- [x] D10-D14, D16, D19, D20 and D15 (dedicated storage) implemented; SysFile sources and targets refused with the D15
      text; designer-built SysFile elements described without loss.
      Evidence: package commits OA.1-OA.6 (`1e8f7b30`..`8f10526f`) and the designer-form commit `b84fd381`;
      `FileProcessingConfigApplierTests.Apply_ShouldRefuseASysFileObject_WithTheProductionResolver`; clio e2e
      `CreateBusinessProcess_Should_RefuseSysFileStorage` and
      `DesignerFixture_Should_DescribeTheSysFileElement_AndRefuseItsChanges`.
- [x] The generic route and raw configuration mappings refused for the Object schema only; Report and Process elements
      untouched and still generic.
      Evidence:
      `UserTaskElementHandlerCreateTests.Create_ShouldTolerateGenericRoute_ForTheUnclaimedFileProcessingTasks`,
      `FileProcessingRawMappingTests.ApplyMapping_ShouldTolerateTheRawRoute_OnAnUnclaimedVariant`; record
      `docs/knowledge/ProcessModel/report-and-parameter-file-elements-stay-generic-until-eng-96506.md`.
- [x] Package tests green on `-c dev-nf` (from a short-path worktree, MAX_PATH) and `-c dev-n8`; every new fixture
      `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` and nothing else, with AAA, `because:` and
      `[Description]`; the sort codec extracted with `ReadDataConfigBinderTests` and `ReadDataConfigApplierTests` green
      and their assertions unedited.
      Evidence: #97 "Tests": 3989 passed, 0 failed on `-c dev-nf` and on `-c dev-n8`; fixture attributes and the Read
      data suites as in OA.7.
- [x] `docs/process-file-element-capture.md` matches SkillFilesValidationProcess (read side) and FileCopyProcessPP (save
      side, its predated parameters excluded and named) under the TC-41 rule; architecture rows and the diary entry
      added; restamped last; tag pushed and verified on the remote.
      Evidence: `docs/process-file-element-capture.md:409-410` names both oracles and the predated parameters;
      `docs/process-builder-architecture.md:147-164`; `.codex/workspace-diary.md:5174`; restamp `55517e20` last;
      `crtprocessbuilder-1.6.6.103` -> `ca243062` on the remote (`git ls-remote`, 2026-10-20).

**clio**
- [x] Rebundled to the final cut with a strictly increasing `-Version`; four pins; the three floors equal the final cut,
      with every file that moves with a floor raise.
      Evidence: `e4d471042`, `-Version 1.6.6.103` (above the burned 1.6.6.100-1.6.6.102), four pins written by the
      script, floors at 1.6.6.103 in `CreateBusinessProcessCommand.cs:267`, `ModifyBusinessProcessCommand.cs:223` and
      `ModifyProcessAsNewVersionCommand.cs:87`, moved together with the tool descriptions, e2e gates and capability map.
      The follow-up cut 1.6.6.104 (#1846) kept the floors at 1.6.6.103 on purpose
      (`docs/agent-instructions/bundled-packages.md`).
- [x] Descriptions re-measured and the budget comment rewritten, the bytes found by a reviewer-approved trim (Q20 = A);
      `CreateElementBlockShape` names `fileProcessing` in the archive's commit; the three call-shape drift tests and the
      19,000 B wire guard green; the `fileProcessing` key set in `ProcessBlockKeyCheck` with its two tests green;
      `curated-knowledge-names.json` re-pinned to the PUBLISHED generation in the same commit as the first
      `name=process-files`.
      Evidence: OA.10 (a)-(d); the call-shape drift tests and the 19,000 B guard ran in the full unit run of #1841
      (17325 passed).
- [x] `list-user-tasks` corrected; `DescribedFileProcessing` with a bag on every nested type and `///` summaries; the
      capability probe; the four prompt sentences pinned; no new `CLIO*` analyzer warning in any touched file.
      Evidence: list-user-tasks, `DescribedFileProcessing`, the probe and the four prompt pins as in OA.10 (e), (f).
      Analyzer run, 2026-10-20: a clean `dotnet build clio/clio.csproj -c Debug -f net10.0 --no-incremental` of clio
      master `ea3aaf580` (which contains #1841, #1846 and #1847; `Clio.Analyzers` is referenced by `clio/clio.csproj`
      only) reports `CLIO*` warnings in one file, `clio/Command/McpServer/Tools/MobileDiffApplyValidator.cs` (two
      CLIO001), which none of the three PRs touches; none of the `clio/` source files #1841 changed has a `CLIO*`
      warning.
- [x] Every new or changed test in `clio.tests` and `clio.mcp.e2e`: explicit Arrange / Act / Assert, a `because` on every
      assertion, `[Description]` on every test, runnable on macOS, Linux and Windows; clio unit tests carry
      `[Category("Unit")]`.
      Evidence: a scripted check of the #1841 diff (`24bfa471b^1..24bfa471b`, `clio.tests` and `clio.mcp.e2e`): 36 new
      tests, each with `[Description]` and Arrange / Act / Assert, all 219 added `Should()` assertions with `because`,
      no absolute path, no `Category("UnitTests")`; the CI unit shards and TeamCity runs passed.
- [x] Full unit suite green: `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null` (four
      modules touched), quoted as `Validated:` in the PR description.
      Evidence: #1841 "Validation": `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit"` at
      `e4d471042`, 17325 passed, 0 failed, 25 skipped, quoted under Validation; the `Validated:` line itself quotes the
      targeted run (13208), not the full suite.
- [x] `ProcessFileElementToolE2ETests` and the must-stay-green fixtures run against the stand on the cut: Passed,
      Ignored = 0, recorded per fixture in the PR description.
      Evidence: #1841 "Validation": stand d_krestov_n, 1.6.6.103, 73/73 passed, 0 skipped, per fixture
      (ProcessFileElement 25, ListUserTasks 2, ValidateProcessGraph 14, DescribeProcess 6, SubProcessMultiInstance 10,
      RecordColumnSource 3, FileParameter 13). On the follow-up cut 1.6.6.104 (#1846) the two designer-fixture cases
      skipped because the fixture had been removed from the stand.
- [x] Knowledge records added, updated or deferred as in the task list; `make check-knowledge` report reviewed.
  Evidence: the nine records and the `described-filter-types-have-no-json-overflow-bag.md` update on master; the
  deferred M26 record `platform/after-saving-object-files-points-at-the-source-files.md` added in #1857 on 2026-10-20;
  the report over #1841's diff (103 records) reviewed on 2026-10-20 against master `ea3aaf580` and package tag
  `crtprocessbuilder-1.6.6.109`: 96 kept, 6 updated in #1857, 1 already deleted by #1847 as planned (Dev Agent Record;
  `spec/eng-92719-file-processing-element/eng-92719-file-processing-element-knowledge-review.md`).
- [x] The PR description states: "MCP reviewed" (tools create-business-process, modify-business-process,
      modify-process-as-new-version, describe-business-process, list-user-tasks, validate-process-graph; prompts
      ListUserTasksPrompt, CreateBusinessProcessPrompt, ModifyBusinessProcessPrompt, DescribeProcessPrompt; resources and
      `clio/tpl`: no update required); "docs reviewed, no update required" (`clio/help/en`, `clio/docs/commands`,
      `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt`: MCP-only tools); "ClioRing compatibility reviewed, no
      Ring-consumed contract changed" (inspected `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`,
      `clio-ring/ClioRing.Desktop/actions.json`), with the Purpose leads confirmed unchanged in `get-tool-contract`
      `detail=index-entries`; the re-measured budget figures of both guards; no telemetry vocabulary change.
      Evidence: #1841 states "Docs reviewed, no update required" and "ClioRing compatibility reviewed, no Ring-consumed
      contract changed" with the three inspected paths; the rest is in the section "MCP review (completed 2026-10-20)"
      appended to #1841's description on 2026-10-20: the touched tools, prompts and resources; the served purposes
      (`get-tool-contract` `detail=index-entries`) byte-identical at `85a3953e2` and `24bfa471b`, with
      `GetToolContracts_ShouldAdvertiseTheRepairedPurpose_WhenCompactIndexIsBuilt` and
      `GetToolContracts_ShouldGiveEveryToolADistinctPurpose_WhenCompactIndexIsBuilt` passing; the budgets re-measured at
      `24bfa471b` (modify 35042 B, 30 B headroom; create 34917 B, 155 B; describe 34644 B, 428 B; wire bundle 18815 of
      19000 B); and no telemetry vocabulary change.

**clio-knowledge**
- [x] `process-files` sections and `element-catalog.md` for this cut; FOUR -> FIVE with its three tests and the card;
      pin and size tests, `GuidanceInlineReplyBudgetTests`, `ProcessDigestTests`,
      `ProcessBuilderInstallConsentGuidanceTests`, `ClioToolSyncGuidanceTests`, `ProcessSplitCoreRuleTests` and
      `InternalTaskReferenceTests` green; a refute-first fact check of the guide text by reviewers who may not cite our
      own records; libraryVersion and sequence above master at merge; **published**, and `info-knowledge` shows it.
      Evidence: clio-knowledge #295 (merged 2026-10-19): the `processes/files.md` and `element-catalog.md` sections;
      FIVE pinned by `ProcessDigestTests.TheCardsDedicatedTypes_ShouldEqualTheCatalogsFive` and
      `ProcessPreconfiguredPageCatalogGuidanceTests`; the digest card lists `fileProcessing`; 455 bundle tests passed;
      refute-first fact checks by two agents barred from the author's records; libraryVersion 1.15.159 over master's
      1.15.156; release `clio-knowledge 1.15.159` published 2026-10-19 08:37Z; TC-11..TC-14 ran on 1.15.159
      (`eng-96505-test-run-report-104.md`). `info-knowledge`, run 2026-10-20 from a build of clio master `ea3aaf580`,
      shows library 1.15.163 at revision `6fa3f91`, which contains #295's merge `8452003`.

**Process**
- [x] This BMAD set merged before the package PR opened (E7).
  Evidence: the BMAD set, clio #1809, merged 2026-10-14 11:41Z; package #97 opened 2026-10-14 17:51Z.
- [ ] Jira: OA-1..OA-12 pasted as this issue's acceptance criteria, the rows encoding Q13-Q17 as agreed on
      2026-10-14; link L1 (ENG-95984 relates to this issue)
      in place of the "blocks" link 560203.
      Not met (Jira read 2026-10-20): the ENG-96505 description still carries the original acceptance criteria (OA-1..OA-12
      were not pasted; the owner pastes them by hand, because an API edit breaks the HeroCoders checklist), and the issue
      still has the "blocks" link 560203 from ENG-95984, with no "relates to" link L1 in its place. ENG-96505 is Closed.
- [x] M26 recorded before the OA.12 sentence and its record are written; `UsrFpOaDesignerFixture` built; ST-01, ST-02,
      DT-01, DT-02 and SC-4 (TC-M-20, the story 1 rows) passed and recorded with dates, package version and basis.
      Met in part: M26 measured 2026-10-15 on 1.6.6.100 and cited with its date and version in the guide sentence of
      #295 (OA.0); the fixture built 2026-10-15 (OA.0); the five proofs passed (#1841 "Validation": "ST-01 and ST-02
      run-time checks passed", SC-4 0 differences, DT-02 one cosmetic difference normalized by N16) and are now recorded
      under "Stand records" in the Dev Agent Record below (written 2026-10-20 from the owner's stand handoff).
      Not met: the package version of ST-02 is not recorded anywhere (the handoff gives none, and #1841 groups ST-01,
      which ran on 1.6.6.100, under its 1.6.6.102 run, so it is no reliable source); DT-01's version is inferred, not
      recorded. Close by recording ST-02's version from the stand's data, or by an owner decision to accept it without one.
      Waived by the owner 2026-10-20: ST-02 is accepted without a recorded package version (it ran in the 1.6.6.100-1.6.6.102
      window); DT-01's version stays inferred. Historical record keeping, not repeatable now.
- [x] Every test-plan case whose Story column names 1 implemented; the AC traceability rows of OA filled.
  Not met as far as checked (crt-process-builder `origin/main` `4617c369`, 2026-10-20): the OA traceability rows are
  filled; TC-U-39 is implemented (`MappingNestedDisplayValueTests`, the D20 nested display value) and TC-U-38's create
  half (`FileProcessingElementHandlerTests.RemintNestedItems_*`), and TC-I-35 in the e2e; no package test was found for
  TC-U-35 (two consumers, both levels stored, at package level), TC-U-36 (a later mapping in the create batch embeds
  the re-minted nested UId) or TC-U-38's modify half (nested UIds kept on modify with their consumers). Close by
  pointing each to an existing test, or by adding the tests in a package PR.
  Carried by the owner's decision of 2026-10-20 to ENG-103934 (Process file element: describe misreports an item-only
  Files.File binding on the Process parameter variant), item 5, which ships with the next CrtProcessBuilder cut: TC-U-35,
  TC-U-36 and TC-U-38's modify half need a package change, so they cannot land in this docs PR.
- [x] Review gate 1 (comprehensive) before each PR opens; gate 2 on every new commit with the tier stated; gate 3
      (comprehensive) before ready-to-merge; no open Blocker or High.
      Not met as recorded: gate 1 before #97 (fixes `654e53d8` at 17:12Z, PR opened 17:51Z) and before #1841, and gate 3 on
      all three PRs with no open Blocker or High, are recorded in the PR descriptions; a tier is stated on 2 of #1841's
      15 commits (`Review tier: skipped ...`), and gate 1 for #295 is not recorded (its pre-PR refute-first fact check
      `e668672` is a fact check, not the three-lens gate).
      Waived by the owner 2026-10-20: the missing per-commit tier statements and the unrecorded gate 1 of #295 are
      history that cannot be recorded after the fact; gate 1 and gate 3 ran with no open Blocker or High.
- [ ] Status flips as in "Status convention"; this Definition of Done has no unchecked item before `done`.
  Not met: `in-progress` was never recorded on master, `review` arrived with #1847, and this Definition of Done still
  has an open item (Process 2, Jira).
- [x] (reworded 2026-10-20 to the decision taken) PR titles exactly `ENG-96505 Element readiness and object attachments
      mode` in all three repositories; merged by a human in the order package (tag verified on the remote), knowledge,
      clio; no clio release cut between the package and clio merges.
      Decision: the guide-first merge order of #1841's gate 3, as in OA-12.
      Evidence: the titles are exact in #97, #1841 and #295; d-krestov merged #97 at 08:35:49Z, #295 at 08:36:09Z and
      #1841 at 08:37:01Z on 2026-10-19, so no release could fall between them; `crtprocessbuilder-1.6.6.103` ->
      `ca243062` on the remote (`git ls-remote`, 2026-10-20).

## Dev Agent Record

Filled in on 2026-10-20 from the merged PRs and the owner's stand handoff (`eng-96505-stand-handoff.md`, a working file
outside the repository).

- Implementation started: 2026-10-14 (package code; draft #97 opened 2026-10-14 17:51Z).
- Owner answers to Q13-Q17 (date, answer, any change to this story): 2026-10-14, all as recommended; no change to this story.
- Package cut claimed (number, date, where claimed): 1.6.6.103, 2026-10-19, in ENG-92719 comment 550565 (#1841
  "Rebundle"); 1.6.6.100-1.6.6.102 are burned, each installed on the dev stand before a byte-changing fix. Follow-up cut
  1.6.6.104 (#99, #1846), floors kept at 1.6.6.103.
- Bytes trimmed per Q20 = A (which text per tool, and the reviewer's approval), description and wire figures re-measured:
  the six trims listed in #1841 "Rebundle" (create: four; modify: one; modify-as-new-version: one), approved by the
  owner on 2026-10-19 (`7b0fc971e`); re-measured at `24bfa471b` in #1841's "MCP review (completed 2026-10-20)":
  modify 35042 B, create 34917 B, describe 34644 B of 35072 B; wire bundle 18815 of 19000 B (clio item 8).
- Tests passing (package, clio unit, e2e with Ignored count, knowledge): package 3989/3989 on `-c dev-nf` and `-c dev-n8`
  (#97); clio unit 17325 passed, 0 failed, 25 skipped (#1841); e2e 73/73, 0 skipped, on 1.6.6.103 (#1841); knowledge
  455 passed (#295).
- Stand records (ST-01, ST-02, DT-01, DT-02, SC-4, M26), all on the .NET Framework dev stand, basis measured:
  - ST-01 PASS, 2026-10-15, CrtProcessBuilder 1.6.6.100, run `8c951966`: 2 new ContactFile rows (Type File, linked to the
    target), the source AccountFile rows unchanged, `CreatedObjectFileIds` = the copies' Ids (process trace).
  - M26, the same run: after SaveToFiles `ObjectFiles` holds the SOURCE locators (AccountFile rows); the copies are only in
    `CreatedObjectFileIds`.
  - ST-02 PASS, 2026-10-17, run `b6f78e28`: Object "use in process" into a multi-instance sub-process, the callee ran 2
    times, all Completed. The package version is not recorded in the handoff; #1841 lists ST-02 under its 1.6.6.102 run.
  - DT-01 PASS, 2026-10-17; version not recorded, inferred as 1.6.6.100 (the builder still wrote `ConnectedObjectId` as
    Guid, which 1.6.6.101 changed):
    the designer draws the builder-built element like a hand-made one; a no-op save changed only `ConnectedObjectId`
    Guid -> Lookup, and `SourceDataEntitySchemaUId` did not become AccountFile (H-G3-1 not confirmed).
  - DT-02, 2026-10-17: on 1.6.6.100, 3 runtime-neutral differences against the hand-built twin, which led to the owner's
    decision that the builder writes the designer's form (1.6.6.101/1.6.6.102); on 1.6.6.102, 1 difference (the
    `AccountLogo` key of `serializedFilterEditData`), normalized by the N16 rule, so equal under TC-41.
  - SC-4, 2026-10-17, on 1.6.6.102: `UsrFpSc4OaBuilder` against the designer capture `UsrFpSc1OaCapture` (E1, E2): 0
    differences.
- Knowledge review (`make check-knowledge` over #1841's diff `24bfa471b^1..24bfa471b`, 2026-10-20): 103 records
  listed, each read against master `ea3aaf580` and, for claims about the package, tag `crtprocessbuilder-1.6.6.109`.
  96 kept, 6 updated in #1857, 1 already deleted by #1847 (the temporary
  `report-and-parameter-file-elements-stay-generic-until-eng-96506.md`, as planned). Updated, each for a claim false
  on master:
  - `Common/requirespackage-version-floor-survives-convergence-degraded-modes.md`: "that state has ended"; #1841 raised
    the floors to the cut it bundled (1.6.6.103), and #1847 did again (1.6.6.109).
  - `Common/a-presence-only-requirespackage-still-takes-a-convergence-refusal.md`: its table gave three floors as
    1.6.6.14 (master: 1.6.6.109); the cells now point at the attribute.
  - `McpServer/a-named-contract-lookup-is-fitted-so-full-content-needs-detail-full.md`: headroom of 2 B and 225 B;
    master: modify 35 016 of 35 072 B, wire bundle 18 815 of 19 000 B.
  - `ProcessModel/subprocess-build-token-needs-a-managermap-arm.md`: "one release before any CrtProcessBuilder emits
    them"; 1.6.6.103 builds both tokens and describe emits only `fileprocessing`.
  - `platform/the-designer-stores-a-lookup-constant-two-different-ways.md`: "only in a change-data column mapping"; the
    Process file `ConnectedObjectId` is stored as `Script` + `[#Lookup#]` (measured 2026-10-17), the form #1841's
    archive writes.
  - `infra/mcp-e2e-processdesigner-category-is-consumed-only-by-teamcity-job-args.md`: "the five fixtures" and "59
    today"; 22 fixture files carry the category on master, #1841's `ProcessFileElementToolE2ETests` among them.
  Kept: the rest, each with a one-line reason in
  `spec/eng-92719-file-processing-element/eng-92719-file-processing-element-knowledge-review.md`; the Process file, floor
  and describe claims were re-read in source (the SysFile refusal, nested items re-minted on create only, the
  designer's stored form, the record name read with the caller's rights, the meta-path check, the bag recount 28/9).
- Notes: `UsrFpOaDesignerFixture` was built 2026-10-15 and later removed with the stand's Custom package, so the two
  designer-fixture e2e cases skipped on 1.6.6.104 (#1846); rebuilding it is carried to ENG-103937 (story 3, SF.3).
