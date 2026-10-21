# Test Plan: Process file element (ENG-92719)

**Feature**: eng-92719-file-processing-element
**Jira**: ENG-92719 File processing element (Story; epic ENG-92704 Create BP via AI Toolkit); ENG-96505 Element
readiness and object attachments mode (story 1; a Task that relates to ENG-92719, converted from a Sub-task on
2026-10-14); Sub-tasks ENG-96506 Generated report + process parameter modes (story 2), and the not-yet-created Sub-tasks "SysFile attachment storage in the Process file element" (story 3), "Refuse
collection parameters as filter values" (story 4) and "Builder-made user tasks do not set SerializeToDB" (story 5)
**PRD**: [prd-eng-92719-file-processing-element.md](../prd/prd-eng-92719-file-processing-element.md) - FE-1..FE-6,
OA-1..OA-12, RP-1..RP-10, SF-1
**ADR**: [adr-eng-92719-file-processing-element.md](../adr/adr-eng-92719-file-processing-element.md) - D10-D29, O5
**Stories**: [1](../stories/story-eng-92719-file-processing-element-1.md) ·
[2](../stories/story-eng-92719-file-processing-element-2.md) ·
[3](../stories/story-eng-92719-file-processing-element-3.md) ·
[4](../stories/story-eng-92719-file-processing-element-4.md) ·
[5](../stories/story-eng-92719-file-processing-element-5.md)
**Source**: the analysis [test-plan](../eng-92719-file-processing-element/eng-92719-file-processing-element-test-plan.md)
(TC-01..TC-92, stand scenarios ST-01..ST-08, design-time checks DT-01..DT-04, measurement methods M1..M26), whose ids
are kept in the "Orig" column below. It holds the full case text, the harness, the mocking recipes and the evidence
queries; this plan maps its ids to the repository convention and adds the cases of stories 4 and 5. The
parameter-type cases (ENG-95984 File process parameter type) are in its own test plan.
**Status**: Draft (the cases follow the owner's answers to Q13-Q18, agreed as recommended on 2026-10-14; the cases
that move with a still-open item are in "Cases that move with an open decision")
**Created**: 2026-10-14

Path aliases: `PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/`; clio and clio-knowledge paths are relative to their repository roots.

---

## Levels and id scheme

| Level | Project | Ids here | Orig ids | What it can prove | What it cannot |
|---|---|---|---|---|---|
| Unit, package | crt-process-builder `PBT/` (NUnit, NSubstitute, FluentAssertions) | TC-U-nn, nn = the analysis TC number | TC-nn with level P | handler, identity, variant registry, binder, applier, resolver, scope filter, filter target, sort codec, describe block, refusals, notices, the stored metadata shape; the platform's user-task sync on `SchemaUId` (the ENG-95984 harness recipe) | a `ProcessSchemaManager` save, the platform's pre-save validation, the runtime reader (`internal`), the designer |
| Unit, clio | `clio.tests` (`Module=ProcessModel`, `McpServer`, `Common`, `Command`) | TC-U-nn | TC-nn with level C | the describe DTO and its bags, "forwards the block verbatim", description wording and budgets, the call shape, the key set, archive pins, floors, the curated guidance names, prompt pins | anything the server does |
| Unit, knowledge | clio-knowledge `automation/Clio.Knowledge.Bundle.Tests` | TC-U-88 | TC-88 (level K) | the guide states each rule; sizes; registration; the dedicated-type count | whether agents follow it |
| Integration (MCP e2e) | `clio.mcp.e2e`, category `McpE2E.ProcessDesigner` | TC-I-nn | TC-nn with level E | JSON member names, build + describe round trips and refusal texts over the real MCP path, against a stand on the cut | run-time behaviour (these fixtures build and read; they do not run processes). TeamCity excludes the category, so a manual run is the only e2e evidence |
| Manual (stand) | the .NET Framework stand, core 10.1.37 | TC-M-01..TC-M-08 (= ST-01..ST-08); TC-M-11..TC-M-14 (= DT-01..DT-04); TC-M-15..TC-M-17 (per-cut protocol); TC-M-18..TC-M-20 (capture session SC-0, SC-1 + SC-2, SC-4); TC-M-21..TC-M-46 (TC-M-(20+n) = Mn) | ST, DT, SC, M | files really moved, storage, names, temporary files, iteration counts; the designer opening builder output | - |
| Side Sub-tasks | as above | TC-U-101..TC-U-106, TC-I-101, TC-M-51 (story 4); TC-U-111..TC-U-114, TC-M-52 (story 5) | new, proposed | as above | - |

The rule: one analysis case keeps its number. A case at a unit level (P, C or K) becomes TC-U-nn, a case at level E
becomes TC-I-nn, so a case marked "P, E" maps to both TC-U-nn and TC-I-nn. The "Repo" column says which repository's
suite holds a TC-U case. TC-91 and TC-92 keep their numbers. The parameter-type plan has its own TC-U / TC-I ids; where
a case here is the same test, the "Orig" column names it.

Each manual case states the level it reaches: **Stored** (metadata written), **Design time** (the designer opens it) or
**Runtime** (the process ran and `SysProcessElementLog` shows it).

## Scope

### In scope

- D10-D20 of the ADR at package-unit level, per cut; the clio surface (DTO, descriptions, budgets, call shape, key set,
  pins, floors, prompts, `list-user-tasks`) at clio-unit level; the MCP contract over the wire per cut; the
  `process-files` sections and the `element-catalog` row; the stand questions no unit test can reach; the two side
  Sub-tasks' sketches.

### Out of scope, with reason

| Excluded | Reason |
|---|---|
| File and FileCollection parameters, the two-level binder, dotted process paths | ENG-95984 File process parameter type and its test plan; this plan relies on them (TC-72, TC-91, ST-02..ST-05) |
| Running the user tasks in a unit test | the platform's own suite; only the stored shape is ours |
| Outer-only `Files` at run time (M5) | the builder refuses it (TC-66); skipped |
| FastReport and DevExpress at run time | no printable and no package on the stand (M15); refused and unit-only (TC-51) |
| Process file -> Send email attachments; -> Creatio.ai call | ENG-95985 Send email attachments; Creatio.ai is out of scope (ADR D22) |
| The designer's own defects (the SysFile record-object sort list, H-G3-1) | observed in DT-01 and M11, reported, not fixed here |
| Concurrency of two runs of one ST process; record order inside a multi-record report | the platform specifies neither; the guide says the order is unspecified |
| A ClioRing gate run | Ring consumes only catalog names, Purpose text and Destructive flags, none of which change; each clio PR states "ClioRing compatibility reviewed, no Ring-consumed contract changed" and confirms the Purpose leads in `get-tool-contract` `detail=index-entries` (a Q20 trim could change a lead) |

## Risk assessment

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-1 | A claimed variant without its generic-route refusal, or a refusal without the claim (no route at all) | Med without the registry | High | O5 registry; TC-U-05 "all five or none" per cut; TC-U-12 tolerated case in story 1 |
| R-2 | An empty filter copies up to 50 arbitrary files onto a record, or one Word document per record of the entity, with success | High without the refusal | High | TC-U-22, TC-U-56 (pre-gate refusal, `failedOperationIndex: null`); TC-I-22 |
| R-3 | A filter rooted on the record object reads zero files with success | High (the natural phrasing) | High | TC-U-20 root rule; TC-U-18 the record scope in the group |
| R-4 | The sort-codec extraction changes a Read data refusal text | Med | Med | TC-U-23; `ReadDataConfigBinderTests` and `ReadDataConfigApplierTests` green with their assertions unchanged |
| R-5 | A source change or action switch drops values or leaves dangling mappings, also inside one batch | Med | High | TC-U-29, TC-U-33 (dependents from `IProcessDependencySnapshot`, the same-batch case) |
| R-6 | An inconsistent storage pair throws at run time (H-G3-1, not reproduced in memory) | Low | High | TC-U-34 repair on touch; TC-U-43 issue; TC-M-11 (DT-01) |
| R-7 | The SysFile read and write paths behave differently from the source trace | unknown (0 shipped cases) | High | refused in story 1 (TC-U-17); story 3 only after M7, M8, M21, M23, M25; TC-M-08 |
| R-8 | A description, the wire guard, the call-shape drift tests or the key-set test fails on the rebundle | High (budget full) | High | TC-U-82, TC-U-92, the key-set tests (story 1 OA.10 c); reviewer-approved trims (Q20 = A) |
| R-9 | The first `name=process-files` merges before the generation that carries it is published | Med | Med | TC-U-83 re-pin; merge edge E5; TC-M-17 (`info-knowledge`) |
| R-10 | An e2e that went to `Assert.Ignore` on an older package or a missing fixture is read as a pass | Med | High | TC-M-16: Passed with Ignored = 0; `UsrFpOaDesignerFixture` built before the run |
| R-11 | The session's MCP clio is older than the PR and silently drops the block | Med | Med | TC-M-15 checks `get-tool-contract` before any other stand case |
| R-12 | The single-file path ships without evidence | Med | Med | TC-U-64 switch; TC-M-03 (ST-03) only if M1 (TC-M-21) passed |
| R-13 | The serialization comparison "passes" by a rule nobody wrote down | Med | Med | the TC-41 rule is a finite list (PRD); TC-U-41, TC-U-59, TC-U-69, TC-M-12, TC-M-14 |
| R-14 | A wrong or unrelated printable is picked | Low | High | TC-U-50, TC-U-51 (ambiguous caption refused, never picked) |
| R-15 | A foreign package cut lands between ours and satisfies the floor without the block | Med | Med | TC-U-85 capability probe on `ProcessElementUpdateDescriptor.FileProcessing` |
| R-16 | Describe output does not resubmit (describe-only members, snapshots, cultures) | Med | Med | TC-U-44, TC-U-45 |
| R-17 | A package test names a `Terrasoft.Configuration` type and builds only where that assembly exists | Low | Med | TC-U-03 mirrored constants; `-c dev-n8` run per PR |

## Conventions and commands

- **Package fixtures (crt-process-builder house rule)**: `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`
  and nothing else; C# 7.3; explicit `// Arrange`, `// Act`, `// Assert`; `because:` on every assertion;
  `[Description]` on every test, naming the decision id. Tests never name `Terrasoft.Configuration` types. Elements are
  hand-built through `PBT/FileProcessingTestSupport.cs` (owned by ENG-95984's package PR, guarded by
  `FileProcessingTemplateDriftTests`); every task schema a test resolves is registered on ONE user-task schema manager;
  a build-path test that places a file element on a `TestProcessSchema` writes `SchemaUId` by reflection; the platform's
  sync runs through the ENG-95984 recipe. A precondition assert in Arrange wherever the arrange could make the test
  pass vacuously (an element that must start unconfigured).
- **clio.tests and clio.mcp.e2e**: explicit Arrange / Act / Assert; a `because` on every assertion; `[Description]` on
  every test; runnable on macOS, Linux and Windows. clio unit tests carry `[Category("Unit")]` (never `UnitTests`) and
  the fixture's `[Property("Module", ...)]`.
- **e2e**: `[NonParallelizable]`, unique `Usr...E2e{Guid:N}` names, no process RUN inside a fixture, arrange through
  `ProcessDesignerE2EArrange` with `MinimumPackageVersion` = the floor, every graph read-back with `mode=full` or
  `element=<name>`; a raw `SysSchema` row (never describe) for byte comparisons.

```bash
# package, from a short-path worktree (MAX_PATH) with .application copied in; net472 is what CI runs
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf   # before every push
dotnet build MainSolution.slnx -c dev-n8                                                        # once per PR
# clio unit: four modules change, so the full unit suite is mandatory before commit; filter by TestCategory only
dotnet build clio.tests/clio.tests.csproj -c Debug
dotnet test clio.tests/clio.tests.csproj --no-build --filter "TestCategory=Unit" < /dev/null
# e2e, one fixture per run, sequentially (a parallel schema-write burst downs a .NET Framework app pool)
McpE2E__Sandbox__EnvironmentName=<env> dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -c Debug -f net10.0 \
  --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~ProcessFileElementToolE2ETests" < /dev/null
# knowledge, in its own worktree
dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj
```

## Regression scope

- **Package**: the whole suite before every push. Must stay green with assertions unchanged:
  `ReadDataConfigBinderTests` and `ReadDataConfigApplierTests` (the sort codec moves out; every existing
  `SetElementOperation` construction gains the applier argument, 14 in 9 test files, the applier suite's three among
  them), `UserTaskElementHandlerCreateTests` (the merged generic-route
  refusals), `ElementConfigurationLedgerTests` and `ElementConfigurationTrackingTests` (one rule added per cut),
  `DataNodeFilterTargetTests` (Report elements stay on it until story 2), the ENG-95984 binder suites.
- **clio**: the full unit suite; specifically `BundledProcessBuilderPackageTests` (pins, floor sentences),
  `ProcessDesignerRequiresPackageAttributeTests`, `ToolContractPayloadBudgetTests` (the ceiling and the 19,000 B wire
  guard), the three call-shape drift tests in `ToolContractShortFormTests`, `ProcessBlockKeyCheckTests`,
  `WorkspaceTemplateGuidanceDriftTests`, `ManagerMapResolveDataIdTests`, `ServerProcessDescriberTests`,
  `ListUserTasksToolTests`, the prompt pins in `CreateBusinessProcessToolTests`, `ModifyBusinessProcessToolTests` and
  `DescribeProcessToolTests`, and `ProcessParameterBindingWarningsTests` (ENG-103081 Warn when a process-parameter
  mapping is bound twice or to a branch some start skips).
- **e2e** (TC-I-87, same stand, one fixture at a time after the new one): `ListUserTasksToolE2ETests`,
  `ValidateProcessGraphToolE2ETests`, `DescribeProcessToolE2ETests`, `SubProcessMultiInstanceToolE2ETests`,
  `RecordColumnSourceToolE2ETests`, `FileParameterToolE2ETests`.
- **knowledge**: `ProcessGuideResponseSizeTests`, `GuidanceInlineReplyBudgetTests`, `ProcessDigestTests`,
  `ProcessPreconfiguredPageCatalogGuidanceTests`, `ProcessBuilderInstallConsentGuidanceTests`,
  `ClioToolSyncGuidanceTests`, `ProcessSplitCoreRuleTests`, `InternalTaskReferenceTests`, `GuidanceMigrationTests`.

---

## Unit and integration cases

Columns: **ID** (this plan), **Orig** (the analysis TC), **Repo** (pkg / clio / kb for TC-U; e2e for TC-I), **Story**
(the story whose PR adds the case; a case extended in a later story names both), **Case** (short; the full text is the
analysis row), **AC** (the PRD labels). Refusal and notice ids are the ADR catalogue; a test asserts its key words, not
the full sentence. Every refusal case also asserts that the schema is unchanged.

### Harness and drift

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-01 | TC-01 | pkg | ENG-95984 (inherited by 1; = its TC-U-50) | drift guard for the three schemas; the parameter set unchanged after `SchemaUId` | OA-4, FE-5 |
| TC-U-02 | TC-02 | pkg | ENG-95984 (inherited; = its TC-U-50) | each capture's SHA-256 equals `PROVENANCE.md` | FE-5 |
| TC-U-03 | TC-03 | pkg | 1 | mirrored constants equal their literals (ResultActionType 0/1, item keys, SysFile, BaseFile, the file type, the three names) | ADR D21 |
| TC-U-04 | TC-04 | pkg | 1 | the composition-root tripwire: the handler before `UserTaskElementHandler`; the filter target between Signal and DataNode | OA-4 |
| TC-U-05 | TC-05 | pkg | 1, 2, 3 | the registry: all five consumers agree per registered UId, none for an unregistered one; story 3 flips the SysFile switch only | OA-4 |

### Element identity and routing (D10, D13)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-06, TC-I-06 | TC-06 | pkg, e2e | 1 | create with `fileProcessing` + `attachments`: schema, manager item, 69x55, lane, `SerializeToDB`; describe `buildType: fileprocessing` | OA-1, OA-4, FE-1 |
| TC-U-07 | TC-07 | pkg | 1 | alias `processFile` builds the same element; describe emits `fileprocessing` | OA-4 |
| TC-U-08 | TC-08 | pkg | 1 | no group; two groups; another variant's group on update: refused | OA-4 |
| TC-U-09 | TC-09 | pkg | 1 | `source` equal / different / entity-like | OA-4 |
| TC-U-10 | TC-10 | pkg | 1 | `userTaskName` matching (ignored) / contradicting (refused) | OA-4 |
| TC-U-11 | TC-11 | pkg | 1 | a `fileProcessing` block on another element type: refused by the strict gate | OA-4 |
| TC-U-12, TC-I-12 | TC-12 | pkg, e2e | 1, 2 | the generic route for the Object schema (case, padding, `performTask`, another name for the same UId) refused on create (e2e covers padding and the exact name only: on the platform another letter case is not found at all, the user-task schema lookup being case-sensitive, so the case variant is TC-U-12 only); Report and Process tolerated in story 1, refused in story 2 | OA-5 |
| TC-U-13 | TC-13 | pkg | 1, 2 | raw `addMapping` onto the binder-owned parameters refused naming the block member (story 1: the 10 of 13 on the Object task; story 2: the three report parameters); onto `ConnectedObjectId`, `Files`, `Files.File`, `ReportName` allowed with the block's rules (`ConnectedObjectId`: typed against the target's record object, any source refused while the element saves to no object) | OA-5 |
| TC-U-14 | TC-14 | pkg | 1 | the task schema cannot be resolved: "cannot resolve", not "wrong kind" | OA-4 |

### Object attachments: object, storage, record scope, filter, sort (D15 dedicated, D16)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-15 | TC-15 | pkg | 1 | resolver over the fake hierarchy (AccountFile, ContactFile, FileLead, the LeadFile ambiguity, DNSGuideFile, a view, a SysFile-family object, a tag object, no claimant, SysFile missing); `Decode` never throws | OA-6 |
| TC-U-16 | TC-16 | pkg | 1 | the record object or its file object give the same pair; no `SourceDataEntitySchemaUId` for a legacy source; an orphan attachment object named directly is dedicated with no record object; a sent object rewrites the side's pair, an update without it keeps a consistent pair and re-resolves an inconsistent one with a notice | OA-6 |
| TC-U-17, TC-I-17 | TC-17 | pkg, e2e | 1 | a SysFile-mode source or target refused with the D15 text; the switch tested on its own (re-pointed by story 3 to its positive form: SysFile written on either side) | OA-6 |
| TC-U-18, TC-I-18 | TC-18 | pkg, e2e | 1 | `attachments.recordId` per source; CurrentUserContact compiles to the macro; another formula refused | OA-7, FE-4 |
| TC-U-19 | TC-19 | pkg | 1 | record-scope type check | OA-7 |
| TC-U-20 | TC-20 | pkg | 1 | filter root, unconfigured element, scope set twice: refused | OA-7 |
| TC-U-21 | TC-21 | pkg | 1 | `Split` / `Join`: each writer keeps the other half; OR wrap and unwrap; a corpus filter lifts and resubmits byte-stable; a lossy decode lifts nothing; `isNull` survives | OA-2 |
| TC-U-22, TC-I-22 | TC-22 | pkg, e2e | 1 | empty filter at the end of the request: saving refused before the gate (`failedOperationIndex: null`), "use in process" notice after the gate; a later `setFilter` in the batch satisfies both; untouched elements ignored, and a raw `addMapping` (onto `ConnectedObjectId` too) is no touch | OA-9 |
| TC-U-23 | TC-23 | pkg | 1 | `sort`: one direct root column; a path or non-root column refused; a stored multi-entry `OrderByInfo` kept; the extracted codec with both element kinds and members; the describe half (primary entry, hidden-entries issue) asserted with TC-U-43 | OA-7 |
| TC-U-24 | TC-24 | pkg | 1 | `numberOfRecords` 1..5000; omitted on create = the template copy with its `GS5` and a notice on every such create; an explicit 50 a new value | OA-7 |

### Action and save target (D14, D15)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-25 | TC-25 | pkg | 1 | action inference on create; contradictions refused; always written | OA-8 |
| TC-U-26 | TC-26 | pkg | 1 | `saveTo` a legacy object: the UO-4 shape; a `linkColumn` override with W6; `"none"` refused | OA-8 |
| TC-U-27 | TC-27 | pkg | 1 | `saveTo.recordId` constant: unparseable, empty, unknown or of another object refused; existing accepted (the record stub cannot tell another object's record from a missing id) | OA-8 |
| TC-U-28, TC-I-28 | TC-28 | pkg, e2e | 1 | `saveTo.recordId` from a process parameter, `sourceColumn`, CurrentUserContact, each with the notice; a source not of the target's record object refused; a bad meta-path token refused | OA-8, FE-4 |

### `setElement` semantics (D12)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-29 | TC-29 | pkg | 1 | same group = no-op; another variant's group refused naming the dependents from the snapshot; the same-batch case refused | OA-2 |
| TC-U-30 | TC-30 | pkg | 1 | member-wise merge; empty clears; a value source replaced whole | OA-2 |
| TC-U-31 | TC-31 | pkg | 1 | retarget Account -> Contact clears filter, sort and scope unless the block sends their replacement (a new `recordId` becomes the whole filter) | OA-2 |
| TC-U-32 | TC-32 | pkg | 1 | `saveTo.object` changed without `saveTo.recordId`: refused | OA-8 |
| TC-U-33 | TC-33 | pkg | 1 | action on update; save -> use in process clears the four target fields and is refused while `CreatedObjectFileIds` is mapped | OA-8 |
| TC-U-34 | TC-34 | pkg | 1 | repair on touch over stored pairs (H-G3-1, unpaired, consistent): an inconsistent pair re-resolved with a notice, a consistent one kept unless its object is sent | OA-2 |

### Outputs, consumers, serialization (D20)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-35, TC-I-35 | TC-35 | pkg, e2e | 1 | `ObjectFiles.File` and `CreatedObjectFileIds.Id` used by two consumers in one request, both levels stored | OA-3, FE-2 |
| TC-U-36 | TC-36 | pkg | 1 | a later mapping in the create batch embeds the RE-MINTED nested UId | OA-1 |
| TC-U-37 | TC-37 | pkg | 1 | template copies on create; never re-pinned on edit; derived values carry the process `GS5` | FE-5 |
| TC-U-38 | TC-38 | pkg | 1 | nested UIds re-minted on create only; kept on modify with their consumers | FE-5 |
| TC-U-39 | TC-39 | pkg | 1 | nested-source display value in the designer form on every binding path, element and process-parameter forms; a top-level source keeps its bare caption | FE-5 |
| TC-U-40 | TC-40 | pkg | 1 | a template parameter missing after `GetDesignInstance`: refused, never hand-created | OA-1 |
| TC-U-41 | TC-41 | pkg | 1 | parity with SkillFilesValidationProcess (read side) and FileCopyProcessPP (save side) under the TC-41 rule; the parameters FileCopyProcessPP predates (`ConsiderTimeInFilter`, `SourceDataEntitySchemaUId`, `TargetDataEntitySchemaUId`) are excluded and named in the test until SC-1's E2 replaces that oracle | OA-10, FE-5 |

### Describe (D19) and the clio read path

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-42 | TC-42 | pkg | 1 | effective values (inherited `RecordsToRead`, `actionStored: false`, storage, lifted `recordId`, outputs) | OA-2 |
| TC-U-43 | TC-43 | pkg | 1 | `issues` per hazard | OA-2 |
| TC-U-44 | TC-44 | pkg | 1, 2 | round trip: describe -> drop describe-only (`actionStored`, `outputs`, `issues`, every `display`) -> resubmit unchanged, the scope described once; identity check equal (or equal to the describe value of a kept side) accepted, changed refused; a uk-UA caption | OA-2, RP-7 |
| TC-U-45 | TC-45 | pkg | 1, 2 | snapshot tolerance per variant (M13: `Guid` + `referenceSchema`; a Lookup-typed `ConnectedObjectId` reads like a Guid) | OA-2, RP-7 |
| TC-U-46, TC-I-46 | TC-46 | pkg, e2e | 1, 2 | per-cut preservation: Report and Process stay generic and byte-identical through a modify; a designer-built SysFile element described and its change refused (on `UsrFpOaDesignerFixture`); story 2 extends TC-I-46: the fixture's F2 now describes as `fileprocessing`, read-only | OA-2, RP-7 |
| TC-U-47 | TC-47 | clio | 1, 2 | `DescribedFileProcessing` keeps an unknown member at every level; outbound wire names pinned | OA-2 |
| TC-U-48 | TC-48 | clio | 1 | an older clio keeps the block through `DescribedElement`'s bag | OA-2 |
| TC-U-49 | TC-49 | clio | 1 | create and modify forward the block verbatim | OA-1 |

### Generated report (D17)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-50 | TC-50 | pkg | 2 | `report.printable` as an id, a macro, a caption; an ambiguous caption narrowed or refused (unit only) | RP-1, RP-3 |
| TC-U-51 | TC-51 | pkg | 2 | missing, empty, unknown, unreadable, DevExpress, FastReport, no object: refused, with the Report setup hint and candidates | RP-1, RP-3 |
| TC-U-52, TC-I-52 | TC-52 | pkg, e2e | 2 | `report.recordId`: `Id = value` on the report entity; type check; the root | RP-1, FE-4 |
| TC-U-53 | TC-53 | pkg | 2 | `separateReports`: Word omitted -> true; explicit false accepted with a notice | RP-1 |
| TC-U-54 | TC-54 | pkg | 2 | `fileNameSuffix` (resource vs inline) / `fileNameSuffixColumn` (text column, separate on; both together refused) | RP-1 |
| TC-U-55 | TC-55 | pkg | 2 | action and outputs; the temporary-file notice into an Out parameter | RP-2 |
| TC-U-56 | TC-56 | pkg | 2 | Word with no selecting filter refused before the gate; the "all N files to one record" notice | RP-4 |
| TC-U-57 | TC-57 | pkg | 2 | printable change: same object keeps filter, scope and suffix column; another object clears them | RP-7 |
| TC-U-58 | TC-58 | pkg | 2 | notices through the faked feature seam (feature off; `ConvertInPDF`); never a refusal | RP-1 |
| TC-U-59 | TC-59 | pkg | 2 | parity with PrintInvoiceReport under the TC-41 rule; its exclusions are taken from the TC-M-18 (SC-0) record of the parameter set the stand holds, each named in the test (none expected: the corpus copy carries the full set) | RP-8, FE-5 |
| TC-I-60 | TC-60 | e2e | 2 | Word "save to attachments" built and described, with the `separateReports: false` notice and the refusals of an unknown printable (the Report setup hint) and a non-text name column; `Assert.Ignore` when `list-printables` has 0 rows | RP-1, RP-3, RP-7 |
| TC-I-61 | TC-61 | e2e | 2 | read-only describe of the four shipped product processes: `buildType: fileprocessing`, block decoded, raw parameters lossless; never modified | RP-7 |

### Process parameter (D18)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-62 | TC-62 | pkg | 2 | `files.processParameter` = a FileCollection binds both levels | RP-5, FE-4 |
| TC-U-63 | TC-63 | pkg | 2 | `files` from element outputs; the item resolved, never assumed | RP-5, RP-6 |
| TC-U-64 | TC-64 | pkg | 2 | a single File over the M1 switch: nested-only, or refused with "wrap it in a FileCollection" | RP-5, FE-4 |
| TC-U-65 | TC-65 | pkg | 2 | `files.expression` written as given onto `Files.File`, with the notice | RP-5 |
| TC-U-66, TC-I-66 | TC-66 | pkg, e2e | 2 | refusals: `value`, no FileLocator item, outer-only, none, a mapping FROM `Files`, `useInProcess`, `saveTo` missing | RP-6 |
| TC-U-67 | TC-67 | pkg | 2 | outputs `ObjectFiles` (item `ObjectFile`) and `CreatedObjectFileIds` mappable downstream | RP-6 |
| TC-U-68 | TC-68 | pkg | 2 | describe of `files` and `bindingLevels`; resubmitting an outer-only element repairs it to both | RP-7 |
| TC-U-69 | TC-69 | pkg | 2 | parity with FileParameterProcess (element source, process-parameter source) under the TC-41 rule, both binding levels included; `TargetDataEntitySchemaUId`, which both captures predate, is excluded and named in the test, and the source FileCollection's item `FileCollectionParameter` is role-normalised to `File`, until SC-1's E8 and E9 replace these oracles | RP-8, FE-5 |
| TC-I-91 | TC-91 | e2e | 2 | the `Files` mapping rules over the wire on a builder-configured Process variant (ENG-95984's E2E-14a-d, its XT-14a-d; O-TP1 option B) | RP-5 |

### Pattern tests over MCP (FE-6)

Each builds and describes the pattern; the matching stand scenario runs it.

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-I-70 | TC-70 | e2e | 1 | Read data -> Process file (`attachments.recordId <- RD1.ResultEntity` + `sourceColumn: Id`) | FE-6, OA-7 |
| TC-I-71 | TC-71 | e2e | 1 | Process file -> Modify data re-finding the created file (built and described, not run) | FE-6 |
| TC-I-72 | TC-72 | e2e | 1 | Process file -> multi-instance sub-process per file (needs ENG-95984 merged); both levels described | FE-6, OA-3 |
| TC-I-73 | TC-73 | e2e | 2 | Object -> Process chain, and Report "use in process" -> Process | FE-6, RP-2 |
| TC-I-74 | TC-74 | e2e | 2 | Add data -> generated report (DNS Requirements); `Assert.Ignore` when that printable is absent | FE-6, RP-9 |
| TC-I-75 | TC-75 | e2e | 2 | two consumers of one output (the FileParameterProcess shape) | FE-2 |

### SysFile storage (story 3)

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-76 | TC-76 | pkg | 3 | resolver in SysFile mode (the UO-3 shape); a target -> SysFile + `RecordId` + `TargetDataEntitySchemaUId`; Account never SysFile | SF-1 |
| TC-U-77 | TC-77 | pkg | 3 | the SysFile scope in the shape M7 captured; `Split` accepts both shapes; a foreign reference is an issue | SF-1 |
| TC-U-78 | TC-78 | pkg | 3 | sort on the SysFile root: a record-object column refused, a SysFile column accepted | SF-1 |
| TC-U-79 | TC-79 | pkg | 3 | SysFile -> SysFile retarget clears the scope | SF-1 |
| ~~TC-U-80~~ | TC-80 | pkg | 3 | DROPPED by the owner on 2026-10-20 (M25: the designer flag is on by default since 2023-02-01 and never changed). Was: the M25 notice through the faked seam; never a refusal | SF-1 |
| TC-I-81 | TC-81 | e2e | 3 | the SysFile region of `ProcessFileElementToolE2ETests` and the SysFile targets of `ProcessFileReportAndParameterToolE2ETests`: Object on a SysFile entity; Report and Process into a SysFile target; story 1's refusal case re-pointed to its positive form | SF-1 |

### clio surface, knowledge and regressions

| ID | Orig | Repo | Story | Case | AC |
|---|---|---|---|---|---|
| TC-U-82 | TC-82 | clio | 1, 2 | `ToolContractPayloadBudgetTests`: under the unchanged 35,072 B ceiling after the reviewer-approved trims (Q20 = A); the comment's figures re-measured (on `79fab4786` create had 11 B and modify 2 B left before ENG-95984's trim); stories 1 and 2: the budget test stays green after the floor raise (a longer literal is paid by a reviewer-approved trim, Q20 = A) | OA-12, RP-10 |
| TC-U-92 | TC-92 | clio | 1, 2 | the seven process-designer contracts within 19,000 B on the wire; `CreateCallShape_Should_NameExactlyTheMembersOfTheBundledServerContract` green with `fileProcessing` in `CreateElementBlockShape`; the two modify call-shape tests green | OA-12, RP-10 |
| TC-U-83 | TC-83 | clio | 1 | the first `name=process-files`: `curated-knowledge-names.json` re-pinned in the same commit; `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` green | OA-12 |
| TC-U-84 | TC-84 | clio | 1, 2, 3 | pins and floors; the e2e `MinimumPackageVersion` equals the floor (story 3: no floor raise) | OA-12, RP-10 |
| TC-U-85 | TC-85 | clio | 1 | the capability probe on `ProcessElementUpdateDescriptor.FileProcessing` (old archive 0, new 1), kept as a permanent guard | OA-12 |
| TC-U-86, TC-I-86 | TC-86 | clio, e2e | 1 | `validate-process-graph` over a described `fileprocessing` element: no UNKNOWN finding (the `ManagerMap` arm shipped with ENG-95984) | OA-1 |
| TC-I-87 | TC-87 | e2e | 1, 2 | the must-stay-green fixtures (Regression scope) | OA-12 |
| TC-U-88 | TC-88 | kb | 1, 2, 3 | knowledge pins per rule family; the endpoint sentence and no "rarely an endpoint"; the M26 sentence only after M26; size target; FOUR -> FIVE with its three tests and the card; consent wording; read-backs with `mode=full`; no Jira keys | OA-11, RP-9, FE-3 |
| TC-U-89 | TC-89 | clio | 1, 2 | `list-user-tasks` description: the claimed schemas with `fileProcessing` and a "no generic fallback" clause; no count word | OA-5, OA-12, RP-10 |
| TC-U-90 | TC-90 | clio | 1, 2 | the four prompt sentences pinned (`ListUserTasksPrompt`, `CreateBusinessProcessPrompt`, `ModifyBusinessProcessPrompt`, `DescribeProcessPrompt`) | OA-12, RP-10, FE-6 |
| (new) TC-U-93 | - | clio | 1, 2 | `ProcessBlockKeyCheckTests`: `KeySets_Should_EqualTheMembersOfTheBundledServerContracts` and `ReadBackFields_Should_BeDescribeOnlyMembers` green with the `fileProcessing` key set (ENG-103085 Refuse element and block keys the process builder would drop silently, on clio master since 2026-10-14; the analysis test plan predates it): story 1 with all three groups and their write members, story 2 adding the describe-only members of `report` and `files` | OA-12, RP-10 |

## Side Sub-tasks (proposed; refined when each Sub-task is created)

| ID | Story | Repo | Case | AC |
|---|---|---|---|---|
| TC-U-101 | 4 | pkg | a filter value through `processParameter` resolving to a `CompositeObjectList` parameter: refused naming the parameter and the per-item route; schema unchanged | story 4 |
| TC-U-102 | 4 | pkg | the same through `elementParameter` (`RD1.ResultCompositeObjectList`, `OF1.CreatedObjectFileIds`) | story 4 |
| TC-U-103 | 4 | pkg | the same through an `expression` token | story 4 |
| TC-U-104 | 4 | pkg | every scalar right-hand value keeps today's behaviour (the existing filter tests run unchanged) | story 4 |
| TC-U-105 | 4 | pkg | a modify that does not write a filter already stored with a collection value leaves it byte-identical (criterion 3) | story 4 |
| TC-U-106 | 4 | kb | a pin test of the filter-value sentence in the process filter guidance (criterion 5) | story 4 |
| TC-I-101 | 4 | e2e | the refusal over the wire, only if clio advertises the rule | story 4 |
| TC-U-111 | 5 | pkg | a generic-route user task stores `SerializeToDB` from the task schema (a true and a false schema) | story 5 |
| TC-U-112 | 5 | pkg | the dedicated handlers' output unchanged | story 5 |
| TC-U-113 | 5 | pkg | an untouched element of a modify is not rewritten | story 5 |
| TC-U-114 | 5 | pkg | a generic-route user task compared key for key with a designer capture of the same task: no `BO2` difference (criterion 3) | story 5 |

---

## Manual cases: stand

Rules for every row: the .NET Framework stand (core 10.1.37). Every write (a saved process, a run, an upload, a
fixture record, a no-op designer save) and every `install-process-builder` call needs the user's explicit go-ahead, row
by row; an install restarts the site for every connected user. Schema writes and runs go one at a time. Evidence comes
from `SysProcessElementLog` (a `SysProcessLog` root row with no `CompleteDate` is parked, not hung), read with one
`execute-esq` call at a time against an Id baseline taken before the run (never a `CreatedOn` filter). A callee's inputs
are read through a gateway oracle, never from the process trace. UI results (attachment lists) are checked by the user.
Fixtures are disposable records named `ST<nn> ...`; the one exception is the existing Invoice of ST-06 / ST-07, selected
read-only. Cleanup goes through one `execute-dataservice-batch` after the user's go-ahead. Every record names the
package version. A failed runtime case stops the package merge.

### Runtime scenarios (TC-M-0n = ST-0n)

| ID | Orig | Story | Scenario | Pass | Reaches | AC |
|---|---|---|---|---|---|---|
| TC-M-01 | ST-01 | 1 | an Account with 2 attachments saved to a Contact (dedicated storage) | 2 new `ContactFile` rows, `Type = File`, `Version = 1`, Name and Size equal to the sources; sources unchanged; `CreatedObjectFileIds` 2 rows; the user sees both | Runtime | OA-8, FE-1 |
| TC-M-02 | ST-02 | 1 (after ENG-95984 merged) | Object "use in process" -> multi-instance sub-process per file | 2 callee runs and 2 "ST02" activities, not 1; describe shows both levels | Runtime | OA-3, FE-6 |
| TC-M-03 | ST-03 | 2 (only if M1 passed) | a single File into the Process variant inside ST-02's callee | 1 copy per iteration | Runtime | RP-5 |
| TC-M-04 | ST-04 | 2 | the builder-built M4 chain: Object -> FileCollection -> Process variant | PF1 Completed; 2 copies; `bindingLevels: both` | Runtime | RP-5, RP-6 |
| TC-M-05 | ST-05 | 2 | one output, two consumers | 2 rows on each target; sources unchanged | Runtime | FE-2 |
| TC-M-06 | ST-06 | 2 | Word report on an existing Invoice, saved, re-found by Modify data (Tag "ST06"); run once | 1 InvoiceFile row `Invoice. <Number>.docx`, `Type = File`, `Tag = "ST06"` | Runtime | RP-1, RP-2 |
| TC-M-07 | ST-07 | 2 | Report "use in process" -> Process variant -> a Perform task; parked, then completed with `odata-update` | temporary `SysProcessFile` rows while parked, 0 at completion; 1 copy | Runtime | RP-2 |
| TC-M-08 | ST-08 | 3 (after M8 and M23) | SysFile target, SysFile source, optional Freedom UI upload | rows with `RecordId`, `RecordSchemaName`, `Type = File`; copies found; visible in the Freedom UI list | Runtime | SF-1 |

### Design-time checks (TC-M-1n = DT-0n; M22)

| ID | Orig | Story | What the user does | Pass | Reaches | AC |
|---|---|---|---|---|---|---|
| TC-M-11 | DT-01 | 1 | opens ST-01's element, checks every field, closes without changes, saves | metadata equal before and after; no `SourceDataEntitySchemaUId = AccountFile` (H-G3-1) | Design time | OA-10, OA-12 |
| TC-M-12 | DT-02 | 1 | builds the same Object element by hand and saves | equal to the builder's under the TC-41 rule | Stored | OA-10 |
| TC-M-13 | DT-03 | 2 | opens ST-04's and ST-06's elements; a no-op save | the right variant shown, the Word checkbox forced on, `Files` bound; metadata unchanged | Design time | RP-8 |
| TC-M-14 | DT-04 | 3 | opens ST-08's elements; compares with the M21 capture | metadata unchanged; equal to M21 under the TC-41 rule | Stored | SF-1 |

Metadata for TC-M-11..TC-M-14 is read from the raw `SysSchema` row, or with `pull-pkg` / `export-schema` into a scratch
folder, plus the stored resources; never from describe, and never compared by eye.

### Per-cut protocol (every cut: stories 1, 2, 3)

| ID | Orig | What | Pass | Reaches |
|---|---|---|---|---|
| TC-M-15 | pr-split section 14, step 2 | instrument check: build the clio PR; `install-process-builder` from the refreshed BUILD OUTPUT after the user's approval; `list-packages` shows the cut; one behaviour probe only the new code answers (story 1: a create with `fileProcessing` + `attachments`; story 2: an unknown `report.printable` gets the D17 text; story 3: a SysFile `saveTo` is accepted); `get-tool-contract` shows the new vocabulary | all three; otherwise run nothing else | Stored |
| TC-M-16 | pr-split section 14, step 3 | the story's new e2e fixture, then the must-stay-green fixtures, sequentially | Passed with **Ignored = 0** (story 2: `Assert.Ignore` only for the documented 0-printables case) | Stored |
| TC-M-17 | plan section 6.1 | after the knowledge release, `info-knowledge` shows the new libraryVersion before any guidance-dependent run | version matches; otherwise re-run `update-knowledge` | - |

### Capture session (analysis serialization-capture section 12; plan FE.0d)

| ID | Orig | What | Pass | Reaches |
|---|---|---|---|---|
| TC-M-18 | SC-0 | read-only calibration: export PrintInvoiceReport and diff it with its shipped copy; record the parameter set its Report element holds on the stand | equal apart from recorded install artefacts, so no read-path artefact is charged to the builder; the recorded parameter set fixes TC-U-59's named exclusions (none expected) | Stored |
| TC-M-19 | SC-1 + SC-2 | the user builds `UsrFpSc1Capture` (E1-E11) in the designer and saves once; read back by `pull-pkg` / `export-schema` | the predictions checked one by one; M7's stored form and M21's capture half recorded | Stored |
| TC-M-20 | SC-4 | per cut, the builder twin `UsrFpSc4<Cut>` in one create request: story 1 E1, E2 (E3-E5 refused); story 2 E6-E9 (E10 only if M1 passed); story 3 E3, E4, E5, E11 | equal under the TC-41 rule; one DIFF fails | Stored |

### Measurements (TC-M-(20+n) = Mn; methods in the analysis test plan section 8, recipes in open-questions Part B)

| ID | Orig | Write | Gates | State (2026-10-14) |
|---|---|---|---|---|
| TC-M-21 | M1 | yes | story 2: the single-file path | open |
| TC-M-22 | M2 | yes | story 2: the guide wording for an unset single File | open |
| TC-M-24 | M4 | yes | verification (repeated as TC-M-04) | open; the builder-written repeat V7 passed on 1.6.6.98 (2026-10-14) |
| TC-M-27 | M7 | no | story 3: the SysFile scope shape | done 2026-10-20 (lookup In with a primary display column, Guid compare without) |
| TC-M-28 | M8 | yes | story 3: SysFile sources | PASS 2026-10-20 |
| TC-M-29 | M9 | yes | no code (refusal severity) | open |
| TC-M-30 | M10 | no | story 1: refusals R3-R5 | done 2026-10-02: matches |
| TC-M-31 | M11 (a)(b), (c) optional | (c) yes | story 1: issue severity, guide warning | done 2026-10-02 in memory: not reproduced; (c) optional |
| TC-M-32 | M12 | no | none (a later override) | open |
| TC-M-33 | M13 | no | stories 1 and 2: the TC-U-45 fixtures | done 2026-10-02: `Guid` + `referenceSchema` |
| TC-M-34 | M14 | yes | story 1: the D20 pins | done 2026-10-02 |
| TC-M-35 | M15 | no | story 2: Q18 | done 2026-10-02: none |
| TC-M-36 | M16 | yes | no code (notice wording; TC-M-07 closes it) | open |
| TC-M-37 | M17 (Q7 read-only, then runs) | Q7 no, runs yes | the empty-record notice severity | Q7 done 2026-10-02; runs open |
| TC-M-38 | M18 | yes | no code (culture note), after story 2's cut | open |
| TC-M-39 | M19 | yes | story 4's urgency | done 2026-10-02: accepted at build, fails at run |
| TC-M-40 | M20 | yes | the floor premise | done 2026-10-02: dropped silently |
| TC-M-41 | M21 | yes | story 3: the SysFile capture | captured 2026-10-20 (`UsrFpSc1SfCapture`; TC-M-19 holds its capture half) |
| TC-M-42 | M22 | yes | verification | = TC-M-11..TC-M-14 |
| TC-M-43 | M23 (a)-(d) | yes | story 3: SysFile targets, per case | PASS 2026-10-20, (a)-(d) |
| TC-M-44 | M24 | yes | the `attachments.sort` guide sentence | done 2026-10-02: nulled by the binder, refused later with a misleading text |
| ~~TC-M-45~~ | M25 | no | DROPPED by the owner on 2026-10-20 (M25: the designer flag is on by default since 2023-02-01 and never changed). Was: story 3, the designer-flag notice only | - |
| TC-M-46 | M26 | yes | story 1: the guide sentence and the knowledge record on `ObjectFiles` after a save; no code | open |

M3, M3b (MI-0), M6 and M27 belong to ENG-95984's test plan; M5 is skipped (refused anyway).

### Side Sub-tasks

| ID | Story | What | Pass | Reaches |
|---|---|---|---|---|
| TC-M-51 | 4 | the M19 recipe on the cut (Read data as the consumer, never Modify or Delete data) | refused at build; nothing saved | Stored |
| TC-M-52 | 5 | the M14 generic-route shape on the cut, read from the raw `SysSchema` row; then the user's no-op designer save | `BO2` present, equal to the task schema's value; the save changes nothing | Design time |

---

## Cases that move with an open decision

The "AC affected" column names the PRD acceptance rows that encode the trigger's recommended answer (the "Owner answer"
column in the PRD). For an open trigger, a different answer rewrites those rows together with the cases listed here.
Q13-Q18 were agreed as recommended on 2026-10-14: their rows are pasted into Jira as written (plan FE.0b), and the last
column records the alternative that was not taken.

| Trigger | AC affected | Cases affected | If it goes the other way |
|---|---|---|---|
| Q13 (token, discriminator; agreed 2026-10-14) | FE-1, OA-1, OA-4 | TC-U-06..TC-U-11, TC-I-06 | a required `source` enum or a `fileSource` key: the "group present" cases become "key present" cases; the refusal texts F-E3 change; FE-1, OA-1 and OA-4 are rewritten |
| Q14 (naming bundle; agreed 2026-10-14) | OA-7, RP-1 (and member names in every row) | every case naming a member; TC-U-24 | a rename before code changes names only; `numberOfRecords` defaulting to "all" drops the notice of TC-U-24 and rewrites OA-7 |
| Q15 (action inference; agreed 2026-10-14) | none (OA-8's text encodes no answer; the stories' action inference does) | TC-U-25, TC-U-33 | the designer default (use in process) or a mandatory `action`: TC-U-25 asserts the other rule |
| Q16 (record scope, empty-filter policy; agreed 2026-10-14) | OA-7, OA-9, RP-1, RP-4 | TC-U-18, TC-U-20, TC-U-21, TC-U-22, TC-I-22, TC-U-52, TC-U-56 | "notice everywhere": the refusal cases become notice cases, the pre-gate check and its touched-element record are not built, the `failedOperationIndex: null` assertion goes, and OA-9 and RP-4 are rewritten; a record scope in `filter` instead of the group rewrites OA-7, RP-1 and the scope cases |
| Q17 (storage details; agreed 2026-10-14) | OA-6 | TC-U-15, TC-U-26, TC-U-80 | reading the designer flag for behaviour adds flag-OFF cases; `linkColumn: "none"` adds an accepted case with a warning |
| Q18 (FastReport; agreed 2026-10-14) | RP-1 | TC-U-51 | accepted with a notice: TC-U-51 asserts the notice instead of the refusal, and RP-1 is rewritten |
| O-TP1 (where TC-91 runs) | none | TC-I-91 | option A: the cases move to ENG-95984's clio PR on the generic route, which story 2 later refuses and rewrites |
| M1 / M2 | RP-5 (already written both ways) | TC-U-64, TC-M-03, TC-M-20 (E10) | M1 fails: TC-U-64 asserts the refusal, TC-M-03 is dropped, E10 leaves SC-4 |
| M7, M8, M21, M23, M25 | SF-1 (proposed), FE-1 (the PRD's open item "What FE-1 means if SF narrows") | TC-U-76..TC-U-79 (TC-U-80 dropped with M25), TC-I-81, TC-M-08, TC-M-14 | a failing case keeps story 1's refusal: its positive case is dropped and its refusal case stays |
| M26 | OA-11 (the guide sentence) | TC-U-88 | not confirmed: the sentence and the record are not written, and TC-U-88 asserts their absence |
| SC-0 (TC-M-18) | RP-8 | TC-U-59 | PrintInvoiceReport on the stand lacks a parameter: TC-U-59 excludes it and names it (the PRD's predated-parameter class) |
| Contingency X2 (RP's Process half fails ST-04) | RP-5, RP-6 and the Process half of RP-7, RP-8 | TC-U-62..TC-U-69, TC-I-91, TC-I-73, TC-M-03..TC-M-05 | the Process-parameter cases move to a new Sub-task "Process parameter source of the Process file element" |

Decided and reflected above: SysFile in its own Sub-task (Q3), RP before SF (Q4), the consumer split (Q5), the guide
name (Q19), the description bytes by trim (Q20).

## Traceability

| AC | Package unit | clio unit | Knowledge unit | Integration | Manual |
|---|---|---|---|---|---|
| FE-1 | TC-U-06, 46, 52, 62 | - | - | TC-I-06, 46, 52, 60, 81 | TC-M-01, 04, 06, 08 |
| FE-2 | TC-U-35 | - | - | TC-I-35, 75 | TC-M-05 |
| FE-3 | TC-U-51 | - | TC-U-88 | - | - |
| FE-4 | TC-U-18, 28, 52, 62, 64 | - | - | TC-I-18, 28, 52 | TC-M-02, 03 |
| FE-5 | TC-U-01, 02, 37..41, 59, 69, 76 | - | - | TC-I-81 | TC-M-12, 14, 18..20 |
| FE-6 | - | TC-U-90 | - | TC-I-70..75 | TC-M-02, 05, 06 |
| OA-1 | TC-U-06, 36, 40 | TC-U-49, 86 | - | TC-I-06, 86 | TC-M-15 |
| OA-2 | TC-U-21, 29..31, 34, 42..46 | TC-U-47, 48 | - | TC-I-46 | - |
| OA-3 | TC-U-35 | - | - | TC-I-35, 72 | TC-M-02 |
| OA-4 | TC-U-01, 04, 05..11, 14 | - | - | TC-I-06 | - |
| OA-5 | TC-U-12, 13 | TC-U-89 | - | TC-I-12 | - |
| OA-6 | TC-U-15..17 | - | - | TC-I-17 | TC-M-30 |
| OA-7 | TC-U-18..24 | - | - | TC-I-18, 70 | - |
| OA-8 | TC-U-25..28, 32, 33 | - | - | TC-I-28 | TC-M-01 |
| OA-9 | TC-U-22 | - | - | TC-I-22 | - |
| OA-10 | TC-U-41 | - | - | - | TC-M-11, 12, 20 |
| OA-11 | - | - | TC-U-88 | - | TC-M-46 (gates the sentence) |
| OA-12 | - | TC-U-82..85, 89, 90, 92, 93 | TC-U-88 | TC-I-87 | TC-M-15..17 |
| RP-1 | TC-U-50..54, 58 | - | - | TC-I-52, 60 | TC-M-06 |
| RP-2 | TC-U-55 | - | - | TC-I-73 | TC-M-06, 07 |
| RP-3 | TC-U-50, 51 | - | - | TC-I-60 | - |
| RP-4 | TC-U-56 | - | - | - | - |
| RP-5 | TC-U-62..65 | - | - | TC-I-91 | TC-M-03, 04, 21 |
| RP-6 | TC-U-63, 66, 67 | - | - | TC-I-66 | TC-M-04 |
| RP-7 | TC-U-44, 45, 57, 68 | TC-U-47 | - | TC-I-46, 60, 61 | - |
| RP-8 | TC-U-59, 69 | - | - | - | TC-M-13, 18, 20 |
| RP-9 | - | - | TC-U-88 | TC-I-74 | - |
| RP-10 | - | TC-U-82, 84, 89, 90, 92, 93 | TC-U-88 | TC-I-87 | TC-M-15..17 |
| SF-1 | TC-U-05, 76..79 (80 dropped) | TC-U-84 | TC-U-88 | TC-I-81 | TC-M-08, 14, 20, 27, 28, 41, 43 (45 dropped) |

## Exit criteria

Per story, before its package PR leaves draft and before its clio PR is handed to the human merger:
- **Story 1**: every TC-U and TC-I case whose Story column names 1 implemented and green (TC-I against the stand on the
  cut, Ignored = 0, `UsrFpOaDesignerFixture` present); the package suite, the clio full unit suite and the knowledge
  suite green; TC-M-01, TC-M-02, TC-M-11, TC-M-12, TC-M-15..TC-M-17 and TC-M-20 (story 1 rows) executed and recorded;
  TC-M-46 recorded before the guide sentence is written.
- **Story 2**: TC-M-21 and TC-M-22 (M1, M2) recorded before the package PR leaves draft; TC-M-18 (SC-0) recorded
  before TC-U-59's exclusions are fixed; the same for Story 2 cases; TC-M-03 (only if TC-M-21 passed),
  TC-M-04..TC-M-07, TC-M-13, TC-M-15..TC-M-17, TC-M-20 (story 2 rows); TC-I-61 recorded read-only; or contingency X2
  executed and the owner informed.
- **Story 3**: TC-M-27, TC-M-28, TC-M-41, TC-M-43 recorded before the dependent code (TC-M-45 dropped with M25); the Story 3 cases;
  TC-M-08, TC-M-14, TC-M-15..TC-M-17, TC-M-20 (story 3 rows).
- **Stories 4 and 5**: their rows refined and implemented when their Sub-tasks are created.
- No case reworded to pass: a difference outside the TC-41 rule is fixed, or recorded with the owner as a new tolerated
  row. The rule's predated-parameter class is not such a row: it excludes only parameters a shipped oracle predates,
  each NAMED in its test (TC-U-41, TC-U-59, TC-U-69), until the SC-1 capture replaces that oracle.
