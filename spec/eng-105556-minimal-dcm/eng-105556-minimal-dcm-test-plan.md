# ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" - test plan

| | |
|---|---|
| Issue | ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" (Task; epic ENG-92704 "Create BP via AI Toolkit") |
| Status | Proposal, 2026-11-08. Nothing here was built or run; nothing was written to Jira, GitHub, GHE, SVN or a stand. Every Q1..Q29 in [decisions](eng-105556-minimal-dcm-decisions.md) is still "open - owner decides"; a row that names a Q tests the RECOMMENDED option and changes when the owner picks another one |
| Basis | the chosen design [design](eng-105556-minimal-dcm-design.md) (sections cited as "design §n"); research notes `<research-dir>/` r1..r10, g1..g6 and `_verdicts.md` (a verdict overrides its note; `(v rX-CY)` = verdict row); CrtProcessBuilder `main` a3f58852 (1.6.6.140); clio `master` 69788ccd5; clio-knowledge `master` 622134b (`libraryVersion` 1.15.180); core TSBpm a95349820; creatio-ui 7ca9210bd0e; PackageStore `*/branches/7.8.0`; platform unit-test harness `<UnitTests>` |
| Stands | `Creatio` (.NET Framework 4.8.1, CrtProcessBuilder 1.6.6.142 = an unmerged feature build, not `main`, v r10-C1, v r10-C2) is READ-ONLY during research; no row targets it by default, and it becomes a write target only on the owner's explicit word (plan D0.6, the one rule for this stand across the folder). No reachable .NET 8 stand exists [measured g5 §1: 8 registered `isNetCore` environments refuse the TCP connection]. Every write row needs a disposable read-write stand the owner provides (plan D0.6); the .NET 8 stand per Q24 |

Evidence tags: `[traced path:line]` source read; `[corpus N/M]` count over packages or test files; `[measured]` stand or
file read (query in the cited note); `[academy URL]`; `[note]` the owner's DCM overview; `[inferred]` reasoning.
**probe** marks a row that relies on harness behaviour not yet observed in a run (the first CI run of the draft PR
decides; the fallback is named in the row).

| Path alias | Root |
|---|---|
| `CORE/` | `<core>/Src/Lib/` |
| `PB/` | `<package repository>/packages/CrtProcessBuilder/Files/src/cs/` |
| `PBT/` | `<package repository>/tests/UnitTests/CrtProcessBuilder.Tests/` |
| `UT/` | `<UnitTests>/` (platform harness, compiled into `PBT/Libs/UnitTest.dll`) |
| `CLIO/` | `<clio>/` (`clio/`, `clio.tests/`, `clio.mcp.e2e/`) |
| `KB/` | `<clio-knowledge>/` (`guidance/`, `automation/Clio.Knowledge.Bundle.Tests/`) |
| `PS/` | `<PackageStore>/<Package>/branches/7.8.0/` |
| `UI/` | `<creatio-ui>/` |

Test ids: **TC-U-nn** package unit (`PBT/`), **TC-P-nn** package parity (`PBT/`), **TC-C-nn** clio unit
(`clio.tests`), **TC-I-nn** clio MCP e2e (`clio.mcp.e2e`), **TC-K-nn** clio-knowledge tests, **AR-n** headless agent
runs, **CA-n** CAADT scenario checks, **M1..M10** stand measurements (design §14.5), **TC-01..TC-13** manual Jira
cases (section 9). Test method names and `[Description]` strings below carry no Jira key (house rule).

BMAD names (plan §2). clio `AGENTS.md` makes "all TC-U-* and TC-I-* test cases" mandatory, so the BMAD test plan
renames every family into one of the two: TC-U-nn and TC-P-nn -> **TC-U-PB-nn**; TC-C-nn -> **TC-U-CL-nn**; TC-K-nn ->
**TC-U-KB-nn**; TC-I-nn -> **TC-I-nn**; CA-n -> **TC-I-CA-n**; AR-n -> **TC-I-AR-n**; Mn -> **TC-I-M-n**. Manual
TC-01..TC-13 keep their names. Plan AC5 lists every family by name, so none falls outside the definition of done.

AC numbering is owned by the plan (plan §12: AC5 = tests, AC6 = guidance); the coverage matrix below follows it.

---

## 0. What each layer can prove

| Claim | Unit (package) | Unit (clio) | MCP e2e | CAADT / agent run | Manual (owner, browser) |
|---|---|---|---|---|---|
| Stage plan: one stage per value, order, names, UIds, colour, full mesh, bounds | yes (pure planner) | - | stored bytes | - | designer + bar |
| What is handed to `DcmSchemaManager.SaveSchema` | yes (intercepted `InternalSaveSchema`, v r8-C1, v r8-C2) | - | stored `SysSchema` + properties | - | - |
| Rights (`CanManageDcm`, package editability) as the platform enforces them | NO: they run inside the intercepted 9-argument `InternalSaveSchema` [traced CORE/Terrasoft.Core/SchemaManager.cs:3214-3219] | - | admin only | - | M4 (non-admin) |
| `ActualizeSchemaInfo` side effects (start signal, flow schema) | NO (v r1-C2) | - | optional runtime case (TC-I-12) | - | TC-06, M5, M7 |
| Settings rows and bindings | `QueryAssert.Got<Insert>` + substituted binder | - | rows + `SysPackageSchemaData` | - | designer opens (TC-02) |
| Designer opens with the expected stages | open PRECONDITIONS only (link row, record Ids) | - | open preconditions (TC-I-03) | Playwright opens the URL (CA-5) and the "Set up cases" route (CA-7) | TC-02, M1 |
| Bar renders; bar -> lookup; lookup -> bar | - | fragment shape | page body only | Playwright (CA-2..CA-4) | TC-03..TC-05, M3 |
| .NET 8 | package suite builds and runs under `-c dev-n8` locally (v r8-C6, refutes "net472 only") | runtime-neutral | needs a .NET 8 stand (Q24) | local run against that stand (g5 §5) | TC-12, M8 |

The clio test host framework (`-f net10.0` / `-f net8.0`) is the framework of the clio PROCESS that drives the MCP
server; it says nothing about the Creatio stand's runtime. ".NET Framework vs .NET 8" in the acceptance means the
STAND runtime and is proven only by running TC-I and CA rows against one stand of each kind [inferred from g5 §2.2].

---

## 1. Coverage matrix

| Item | What must hold | Package unit / parity | clio unit | e2e | KB / agent | CAADT | Stand / manual |
|---|---|---|---|---|---|---|---|
| **AC1** one call (or short sequence) creates the DCM for object + lookup column and saves it to the target package | `create-dynamic-case` validate pass -> build pass; DCM in P; settings rows and bindings in P | TC-U-30..37, 40..45, 50..57, 80..104 | TC-C-01..17 | TC-I-01, 02 | TC-K-01, AR-1 | CA-1 | TC-01 |
| **AC2** DCM opens in the DCM designer with the expected stages and no errors | link row with the DCM UId and a non-empty settings id (v r2-C4); every `StageRecordId` a real lookup row (v r2-C5); captions = display values; full mesh; colour set | TC-U-01..18, 50..51, TC-P-01..03 | - | TC-I-03, 04 | - | CA-5 | TC-02, M1 |
| **AC3** record page shows the bar with those stages; bar -> lookup and lookup -> bar | fragment = wizard values (design §7.3); DCM enabled + active version (v r9-C1); full mesh (`_verdicts.md` conflict 7); colour `#0C8701` (conflict 6) | TC-U-15, 16, 70..74 | TC-C-18..20 | TC-I-04, 05 | TC-K-03 | CA-2..CA-4 | TC-03..TC-05, M2, M3 |
| **AC4** .NET Framework and .NET 8; compile/restart obligations handled or reported | obligations per artifact all `false`, `reloadOpenPages: true`; no compile call; route with/without `0/` | TC-U-99, 100 | TC-C-07, 21, 26 | TC-I-02 (`obligations`), whole fixture on both stands | TC-K-03, 11 | CA-6 (.NET 8 local run) | TC-11, TC-12, M8, M10 |
| **AC5** unit tests + E2E/CAADT scenario for a sample object (plan §12 AC5) | sections 2-7 | all | all | all | TC-K all | CA-1..CA-7 | M1..M10 |
| **AC6** clio-knowledge article: when/how, what is not supported, linked from `routing` (plan §12 AC6) | `processes/dynamic-case.md`, itemId `process-dynamic-case`, no banner, routing rows, `process-modeling.md:157-158` corrected (Q23) | - | TC-C-29 (mandatory re-pin) | - | TC-K-01..13, AR-1, AR-2 | - | - |
| **Scope 2** one stage per lookup value, in lookup order, value = caption | explicit > order column > display value; `orderSource` always reported (Q8, Q10) | TC-U-01..13, 20..23 | TC-C-04 | TC-I-01, 02, 08 | TC-K-02 | CA-2 | TC-01, TC-07 |
| **Scope 3** stages empty, default order only | `Elements` empty, no `SystemElements`, no conditions, no `IsDefault` | TC-U-18, 35, TC-P-01 | - | TC-I-02 | TC-K-04 | - | TC-02 |
| **Existing records** | N3 note; bar shows the current stage; case starts on next stage-column change, new record starts on insert (v r1-C7) | TC-U-102 (note text) | - | TC-I-12 (optional) | TC-K-03 | CA-4 | TC-06, M7 |
| **Refusals** no lookup column, existing DCM, empty lookup (+ rights, settings conflict, unknown value) | stable `errorCode`, nothing written (design §3.2) | TC-U-17, 80..93, 110..114 | TC-C-11..13, 17 | TC-I-07..10 | TC-K-05, AR-2 | - | TC-08..TC-10, TC-13, M4 |
| **Owner constraint** later steps inside stages | stage is an object; `steps` refused with `steps-not-supported`; planner / composer / saver split; open dependency-planner input set (design §12) | TC-U-64, 81, 134 | TC-C-05 | TC-I-08 | TC-K-04 | - | - |
| **Q12** equivalent re-run | `already-present`, no write, page step still runs | TC-U-86 | TC-C-01 (annotation) | TC-I-06 | - | - | TC-09 |
| **Q13** existing settings row | reuse same column without filter, else `settings-conflict` | TC-U-52, 57, 89 | - | TC-I-11 | - | - | - |
| **Q16** missing package dependencies | computed exactly; clio adds with opt-out; cycle stops before the build pass | TC-U-60..64, 92 | TC-C-10, 11 | TC-I-13 | TC-K-05 | - | M10 |
| **Q17-Q20** page package, write path, bar flags, typed/mini/Classic pages | default design package; `PageUpdateCommand` append + data-source validator; wizard flags; refuse before any write | - | TC-C-13, 18..20 | TC-I-05, 14 | TC-K-05 | - | TC-03 |
| **Q22** version floor | `[RequiresPackage]` at the cut; a 404 / non-envelope reply -> `process-builder-without-build-case` | TC-U-130..133 | TC-C-17, 22, 23 | fixture gate | - | - | - |

---

## 2. Package unit tests (`PBT/`)

### 2.1 Harness rules

| Rule | Why | Evidence |
|---|---|---|
| Every new fixture carries `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` | Jenkins filters `TestCategory=PreCommit\|TestCategory=Integration`; `CiContractGuardTests` fails an uncategorised fixture; `ci.runsettings` sets `TreatNoTestsAsError` | r8 §3.1 [traced PBT/CiContractGuardTests.cs:13,23,28,63-64] |
| Fixtures that touch the platform inherit `BaseComposableAppTestFixture`; pure-domain fixtures (planner) need no base | the dominant idiom is the platform harness (134/196 fixtures), adapters only where the harness falls short | v r8-C5 [corpus 136/219 files] |
| C# 7.3 only: no `using var`, no switch expressions, no target-typed `new()` | package and tests compile at `LangVersion 7.3` | r8 §3.1 [traced PBT/CrtProcessBuilder.Tests.csproj:32] |
| `[Description]` on every test, explicit `// Arrange // Act // Assert`, `because:` on every assertion, no Jira key | house rule (clio AGENTS.md "Test style policy") | - |
| `DcmSchemaManager` is the plain substitute of `[MockSettings(RequireMock.DCM)]` (`Substitute.For<DcmSchemaManager>()` + `SetDcmSchemaManager`); combine with `RequireMock.DBEngine` whenever an ESQ or `Entity.Save` runs | `UserConnection.DcmSchemaManager` is `GetSchemaManager(nameof(DcmSchemaManager))`, routed by `TestUserConnection` | r8 §3.1, §3.2 [traced UT/UnitTest/BaseConfigurationTestFixture.cs:170-173,354-371]; v r8-C4 (`RequireMock.DCM` never gives `TestDcmSchemaManager`) |
| Code that needs the manager's provider, `AppConnection`, or the non-virtual `CreateSchema` path uses `SubstituteUtilities.SetupDcmSchemaManager`, `new TestDcmSchemaManager()` or `Substitute.ForPartsOf<DcmSchemaManager>()` instead of the plain substitute | the plain substitute has no `SchemaManagerProvider`, the provider's `GetManager("DcmSchemaManager")` returns null, `CreateSchema` / `SaveSchema` are non-virtual | v r8-C3 [traced UT/UnitTest/SubstituteUtilities.cs:633-650; UT/UnitTest/TestDcmSchemaManager.cs:9-33] |
| The saved item is read from the `InternalSaveSchema` received call, matched by name, argument of type `DcmSchemaManagerItem`; the item handed to `SaveSchema` is a REAL item with `Instance` set | `SaveSchema` is public non-virtual and reads `designItem.Instance.PackageUId` before forwarding to the protected virtual 9-argument `InternalSaveSchema` [traced CORE/Terrasoft.Core/SchemaManager.cs:5589-5595]; a null `Instance` NREs | v r8-C1, v r8-C2; precedent [traced UT/CrtGenAI.UnitTests/GeneratedDcmSaver.Tests.cs:18,168-176] |
| Success and failure of the platform save are stubbed with `Terrasoft.TestFramework.NSubstituteExtension.Protected("InternalSaveSchema", <9 Arg matchers>).Returns(true \| false)`; all nine arguments as matchers (the extension picks the virtual overload by name AND argument count) | the substitute answers `false` by default; NSubstitute itself has no protected-member API, the shipped extension does | v r8-C7 (refutes r8 §3.3) [traced CORE/Terrasoft.TestFramework/NSubstituteExtension.cs:34-68; core precedent CORE/Terrasoft.Core.Tests/ClientUnitSchemaManager.Tests.cs:1169-1170]. **probe**: not yet executed from `PBT/`; fallback = a hand-written `SavingDcmSchemaManager : DcmSchemaManager` overriding the 9-argument overload |
| Lookup reads stub rows through `SetUpTestData(schema, filter, rows...)` (wraps `SelectData(...).AddRow(...).MockUp()`); the lookup schema is mocked FIRST with `MockEntitySchemaWithColumns`; value strings in column maps are DATA VALUE TYPE names (`"MediumText"`, `"Integer"`, `"DateTime"`, `"Guid"`) | `SelectData` resolves the schema by name and needs `RequireMock.DBEngine` | r8 §3.5, §3.6 [traced PBT/BaseComposableAppTestFixture.cs:450-468,496-508; PBT/ActivityResultResolverTests.cs:42-45,440-446] |
| Row ORDER is not observable through `SelectData` (rows come back as stubbed) - ordering is a pure-planner concern (Q9 option a) and is tested there, never through query mocks | `Has(x)` matches parameter VALUES only; no test in either corpus uses `SelectAnalyzer.HasOrderByItem` | r8 §3.6 [corpus 0 outside UT/UnitTest/] |
| Entity writes are asserted with `QueryAssert.Got<Insert>(UserConnection, "<table>", analyzer => ...)` over schemas mocked with `CreateEntitySchemaMock` | `GetEntityByName` returns `schema.CreateEntity(uc)` for a mocked schema; `Entity.Save()` over the mocked engine emits the `Insert` | r8 §3.7 [traced UT/UnitTest/QueryAssert.cs:71-93; corpus 39 files use `Got<Insert>`]. `GeneratedDcmSaver.SaveDcmSettings` has NO unit test, so this is new ground (r8 §3.7) - **probe** |
| `IPackageSchemaDataCreator` (a `ClassFactory` binding) is reached only through a package adapter interface that tests substitute; no test binds `ClassFactory` | the adapter keeps `ClassFactory` out of domain tests; precedent `ClassFactory.RebindWithFactoryMethod` exists but is global state | r8 §2.1 (`DcmSchemaManagerCacheResetMonitor_Tests.cs:28`); design §14.1 |
| Platform reads the handler needs (`FindEnabledDcmSchemas`, `GetIsActiveVersion(Guid)`, `GetIsEnabled()`, package hierarchy, rights) sit behind small package interfaces (`IEnabledCaseReader`, `IPackageCatalog`, the guard) and are substituted in handler tests; each adapter gets one thin test or is left to e2e | `DcmEntityUtilities` is a public sealed class with an internal interface [traced CORE/Terrasoft.Core/DcmProcess/IDcmEntityUtilities.cs:14]; `GetIsActiveVersion(ISchemaManagerItem)` is internal (design §1) | design §1, §5; r8 §4 |
| Composition: endpoint tests swap the domain service through `CrtProcessBuilderApp.InjectedServices` + `Instance.Reset()` | existing endpoint pattern | r8 §3.9 [traced PBT/EntryPoints/WebService/ProcessDesignServiceTests.cs:35-52] |
| Internal types are substitutable only when the local `.build-props/env.*.props` set `CrtProcessBuilderIncludeTestVisibility` | `InternalsVisibleTo` for the tests and `DynamicProxyGenAssembly2` is emitted only then | r8 §3.9 [traced packages/CrtProcessBuilder/Files/CrtProcessBuilder.csproj:416-431] |

**lit marker.** Literals that are product values, not computed in the test, are the oracle: colour `#0C8701`
(`_verdicts.md` conflict 6), `Filters` literal `[{"columnUId":null}]` (v r5-C8), binding name shape
`SysDcmSettings_<id:N>` / `SysDcmSchemaInSettings_<id:N>` (Q15), error codes of design §3.2.

Run (junction `.application` into the worktree first; `dev-n8` needs `.application/net-core/core-bin`, v r8-C6):

```
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf
dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-n8
# iterating
--filter "FullyQualifiedName~Case|FullyQualifiedName~Dcm|FullyQualifiedName~ProcessDesignGuard|FullyQualifiedName~ProcessDesignService"
```

CI runs net472 only (by provisioning, not by design, v r8-C6); the `dev-n8` run is a local step before the rebundle
and its result goes into the package PR body.

### 2.2 New fixtures and their harness

| Fixture (`PBT/`) | Class under test (design §2, §5) | Harness |
|---|---|---|
| `Case/CaseStagePlannerTests` | `CaseStagePlanner` (pure, S10) | plain NUnit, in-memory `LookupRow` / `LookupColumnInfo` DTOs; no base fixture |
| `Case/StageValueReaderTests` | `StageValueReader` (ESQ, S9) | `BaseComposableAppTestFixture`, `[MockSettings(RequireMock.DBEngine)]`, `SetUpTestData` |
| `Case/DcmSchemaComposerTests` | `DcmSchemaComposer` (S13) | `[MockSettings(RequireMock.DBEngine \| RequireMock.DCM)]`; the composer RECEIVES a real item `new DcmSchemaManagerItem(mgr){Instance = new DcmSchema(mgr)}` (GeneratedDcmSaver.Tests pattern) and never calls `CreateSchema` (a plain substitute returns a null `DefSchema` and a null `Instance`, v r8-C3); creation is `IDcmSchemaRepository.CreateItem`, tested in 2.6 through `TestDcmSchemaManager` / `ForPartsOf` |
| `Case/DcmSchemaRepositoryTests` | `DcmSchemaRepository` behind `IDcmSchemaRepository` / `IDcmSchemaSaver` (W4) | `[MockSettings(RequireMock.DBEngine \| RequireMock.DCM)]`; `Protected("InternalSaveSchema", ...)` |
| `Case/DcmSettingsWriterTests` | `DcmSettingsWriter` (+ the settings reader of S8), binder adapter `IPackageSchemaDataBinder` | `[MockSettings(RequireMock.DBEngine)]`; `CreateEntitySchemaMock("SysDcmSettings" ...)`, `("SysDcmSchemaInSettings" ...)`; `QueryAssert.Got<Insert>`; substituted binder |
| `Case/CaseDependencyPlannerTests` | `CaseDependencyPlanner` (S12) | substituted `IPackageHierarchyReader` (wraps `WorkspaceUtilities.GetPackageIdByUId` + `GetPackageHierarchyIdsById`, g3 rec. 1) and a substituted item lookup by `RealUId` (wraps `((IManager)mgr).FindItemByRealUId`, design §5 S12); mocked entity schemas with `CreatedInSchemaUId` |
| `Case/CaseVerifierTests` | `CaseVerifier` (V1, V2) | substituted `IEnabledCaseReader` (design §5 S7) |
| `Case/CaseBuildHandlerTests` | `CaseBuildHandler` (S1..S21, W1..W4, compensation) | `BaseComposableAppTestFixture`; every collaborator substituted; `Received.InOrder` for the write order |
| rows in `ProcessDesignGuardTests` | `ProcessDesignGuard.EnsureCanManageDcm` | existing fixture; substituted security reads |
| `Case/DcmMetadataParityTests` | composer output vs a shipped DCM | `JsonDataWriter` capture (r8 §3.10); fixture committed under `PBT/Fixtures/Dcm/` |
| rows in `EntryPoints/WebService/ProcessDesignServiceTests`, `ProcessDesignServiceWireContractTests`, `CrtProcessBuilderAppTests`, `CiContractGuardTests` | op, wire contract, composition | existing harness of each file (v r6-C4) |

The `Case/` folder and class names follow design §2; the owner may rename with Q2 (a sibling service changes only
the endpoint rows, section 2.11).

### 2.3 Stage planner (Q8, Q10, Q11, Q29) - `CaseStagePlannerTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-01 | `Plan_ShouldKeepTheExplicitOrder_WhenStagesAreGiven` | the explicit list wins over every column; `orderSource = explicit` (g6 §4 level 1) |
| TC-U-02 | `Plan_ShouldMatchAnExplicitValueById_BeforeItsDisplayValue` [an Id string that is also another row's display value] | Id match first, then exact display value (design §3.1 `stages`) |
| TC-U-03 | `Plan_ShouldRefuseAnUnknownValue_NamingIt` | `stage-value-unknown`; the message names the value and lists the lookup's values |
| TC-U-04 | `Plan_ShouldRefuseADisplayValueThatMatchesSeveralRows` | `stage-value-ambiguous` lists the matching Ids |
| TC-U-05 | `Plan_ShouldRefuseAnExplicitListThatOmitsAValue` | `stage-list-incomplete` names the omitted values (Q10: permutation) |
| TC-U-06 | `Plan_ShouldRefuseAValueNamedTwice` | `stage-list-duplicate` |
| TC-U-07 | `Plan_ShouldOrderByTheNamedOrderColumn` [`order-column` = an integer column that is not a recognised name] | `orderSource = orderColumn:<name>`; the explicit column wins over the recognised names |
| TC-U-08 | `Plan_ShouldRefuseANamedOrderColumnThatIsMissingOrNotInteger` [missing; `MediumText`; `Boolean`] | `order-column-invalid` |
| TC-U-09 | `Plan_ShouldPickTheFirstRecognisedIntegerColumn` [`Position` only; `StageOrder` + `StageInnerOrder`; `Number`; `StageNumber`; `Position` and `StageOrder` both present -> `Position`; `StageOrder` and `StageNumber` -> `StageOrder`] | g6 §4 level 2 precedence; `StageInnerOrder` is the second key [corpus g6: QualifyStatus reproduces LeadCase only with StageOrder] |
| TC-U-10 | `Plan_ShouldIgnoreATextNumberColumn_AndFallBack` | a text `Number` is not an order column (g6 §4, integer check) |
| TC-U-11 | `Plan_ShouldFallBackToDisplayValueAscending_AndReportIt` [values "New", "In progress", "Done"] | order "Done, In progress, New"; `orderSource = displayValue`; the N2 note lists the captions (g6 §4 level 3) |
| TC-U-12 | `Plan_ShouldBreakTiesByCreatedOnThenId_AndWarn` [two rows with equal `Position`; equal `CreatedOn` too] | ties only; a tie warning names the tied captions (g6 §4 level 4) |
| TC-U-13 | `Plan_ShouldNameStagesStage1ToN_AndCaptionThemWithTheDisplayValue` | `Stage1..StageN` (designer naming, v r2-C1); caption = display value (v r5-C5) |
| TC-U-14 | `Plan_ShouldGiveEveryStageAFreshUId_DistinctFromItsRecordId` | `UId != StageRecordId`, all UIds distinct; `StageRecordId` = row Id (Q29; r4 §7 item 6: record-Id UIds collide with derived system-element UIds later) |
| TC-U-15 | `Plan_ShouldColourEveryStage` | every stage `#0C8701` (lit; a null colour crashes the 8.x bar, v r3-C5) |
| TC-U-16 | `Plan_ShouldConnectEveryOrderedPair_WithoutSelfLoops` [n = 2 -> 2; 3 -> 6; 4 -> 12; 20 -> 380] | full mesh n*(n-1), Source/Target are stage UIds (v r3-C4, v r5-C6; conflict 7) |
| TC-U-17 | `Plan_ShouldRefuseOutsideTheStageBounds` [0 -> `lookup-empty`; 1 -> `lookup-too-small`; 21 -> `lookup-too-large`; 2 and 20 accepted] | Q11 bounds 2..20 from ONE constant; the test reads the constant, not a literal, for the upper bound so a later raise is a one-line change |
| TC-U-18 | `Plan_ShouldMarkNoStageAsDefault_AndPlanNoElements` | no `IsDefault` (not persisted, v r4-C3); stage descriptors carry no elements (scope 3) |
| TC-U-19 | `Plan_ShouldBeDeterministic_ForTheSameInput` | two runs over the same rows give the same order and names (UIds aside) - the precondition of the Q12 equivalence check |

### 2.4 Lookup reader (Q9) - `StageValueReaderTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-20 | `Read_ShouldReturnIdDisplayValueAndCreatedOn_ForEveryRow` | the row DTOs carry Id, the PRIMARY DISPLAY column value (not a hard-coded `Name`), `CreatedOn` (v r5-C5) |
| TC-U-21 | `Read_ShouldRequestTheCandidateOrderColumns_ThatExistOnTheLookup` [lookup with `Position` and `Number`; lookup with none] | only existing integer candidates (and `order-column` when given) are selected |
| TC-U-22 | `Read_ShouldStopAtTheBoundPlusOneRows` | at most `MaxCaseStages + 1` rows come back, so `lookup-too-large` is decided without reading a large table. **probe**: whether the ESQ row limit is visible on the mocked `Select`; fallback = the reader builds its query in a protected factory method that the test inspects |
| TC-U-23 | `Read_ShouldKeepRecordRights` | the query is built WITHOUT turning rights off (Q9: only rows the caller sees become stages, because the designer resets unseen records, v r2-C5). **probe**: the observable member on the mocked query; fallback = the factory-method inspection of TC-U-22 |

### 2.5 Composer (S13) - `DcmSchemaComposerTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-30 | `Compose_ShouldBindTheObjectAndTheStageColumn` | `EntitySchemaUId` = runtime object UId, `StageColumnUId` = column UId |
| TC-U-31 | `Compose_ShouldSetThePackageOnSchemaAndItem` | `PackageUId` on the schema AND on the manager item (r8 §3.4; v r1-C4) |
| TC-U-32 | `Compose_ShouldUseThePlannedUIdAndName` | the DCM UId is known before the save (W2 needs it) and equals the item's `UId` / `Id` |
| TC-U-33 | `Compose_ShouldSetCultureNameToTheCallersCulture` | the DCM manager does not set it (v r6-C1) |
| TC-U-34 | `Compose_ShouldBeEnabled_AndLeaveFiltersEmpty` | `Enabled = true` set explicitly (g1 verdict); `Filters` default empty (start conditions out of scope) |
| TC-U-35 | `Compose_ShouldCarryThePlannedStagesAndConnections_InOrder` | stage order, captions, `StageRecordId`s, colours, connection pairs equal the plan, `WithStrictOrdering` |
| TC-U-36 | `Compose_ShouldLeaveStageElementsAndSystemElementsEmpty` | `Elements` empty per stage; `SystemElements` empty (v r8-C12: only element `Conditions` create them) |
| TC-U-37 | `CreateItem_ShouldNotRegisterTheItemInTheManager` (`DcmSchemaRepositoryTests`) | `IDcmSchemaRepository.CreateItem` calls `CreateSchema(name, DefSchema, uc, uid, addToDesignItems: false)`, which registers nothing [traced CORE/Terrasoft.Core/SchemaManager.cs:3335-3355,5506-5515]; through `TestDcmSchemaManager` / `ForPartsOf` (v r8-C3) |

### 2.6 Repository / saver (Q6, W4) - `DcmSchemaRepositoryTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-40 | `Save_ShouldHandTheComposedItemToThePlatformSave` | `InternalSaveSchema` received once with the composed `DcmSchemaManagerItem` (ReceivedCalls pattern, v r8-C1) |
| TC-U-41 | `Save_ShouldPassTheDesignFlags` | the flags of design §5 W4 reach the 9-argument overload (`generateBundles` and `lockSchemaInSourceControlStorage` positions [traced CORE/Terrasoft.Core/SchemaManager.cs:5589-5595]) |
| TC-U-42 | `Save_ShouldReportSuccess_WhenThePlatformSaves` | `Protected("InternalSaveSchema", 9 matchers).Returns(true)` -> success (v r8-C7) |
| TC-U-43 | `Save_ShouldReportFailure_WhenThePlatformAnswersFalse` and `..._WhenThePlatformThrows` [`SecurityException`; `InvalidOperationException`] | the repository maps both to a failure the handler compensates (no exception escapes) |
| TC-U-44 | `Save_ShouldReleaseTheResourceCache_EvenWhenTheSaveThrows` | `try/finally ReleaseResourceCache(uid)` (pattern [traced PB/Schema/ProcessSchemaRepository.cs:184,189,336,371]) |
| TC-U-45 | `Remove_ShouldRemoveByUId_OnlyWhenTheItemIsRegistered` | compensation calls `RemoveItemByUId` only for a registered item; an unregistered one is reported, not thrown |

Skeleton (C# 7.3; names are the design's, placeholders where the design leaves a DTO open):

```csharp
[MockSettings(RequireMock.DBEngine | RequireMock.DCM)]
[TestFixture(Category = "UnitTests"), Category("PreCommit")]
public class DcmSchemaRepositoryTests : BaseComposableAppTestFixture {

	private static DcmSchemaManagerItem SavedItem(DcmSchemaManager manager) {
		ICall save = manager.ReceivedCalls().Single(call => call.GetMethodInfo().Name == "InternalSaveSchema");
		return (DcmSchemaManagerItem)save.GetArguments().First(arg => arg is DcmSchemaManagerItem);
	}

	private static void PlatformSaveAnswers(DcmSchemaManager manager, bool result) {
		manager.Protected("InternalSaveSchema", Arg.Any<ISchemaManagerItem<DcmSchema>>(), Arg.Any<Guid>(),
			Arg.Any<bool>(), Arg.Any<UserConnection>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(),
			Arg.Any<bool>(), Arg.Any<bool>()).Returns(result);
	}

	[Test]
	[Description("A composed case is handed to the platform save unchanged and the repository reports success when the platform saves it.")]
	public void Save_ShouldReportSuccess_WhenThePlatformSaves() {
		// Arrange
		DcmSchemaManager manager = UserConnection.DcmSchemaManager;
		PlatformSaveAnswers(manager, true);
		DcmSchemaManagerItem item = DcmTestData.ComposedItem(manager, stageCount: 3);   // real item, Instance set
		var sut = new DcmSchemaRepository(UserConnection);

		// Act
		CaseSaveResult result = sut.Save(item);

		// Assert
		result.Success.Should().BeTrue(because: "the platform save answered true");
		SavedItem(manager).Should().BeSameAs(item, because: "the composed item reaches the platform save as is");
	}
}
```

### 2.7 Settings rows and bindings (Q7, Q13, Q14, Q15) - `DcmSettingsWriterTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-50 | `WriteSettings_ShouldInsertTheObjectFormRow` | `Insert` into `SysDcmSettings` with `SysSchemaUId` = object, `StageColumnUId`, `Filters` = lit, `SysModuleEntity` null (Q14; port of [traced PS/CrtGenAI/Schemas/GeneratedDcmSaver/GeneratedDcmSaver.cs:161-177]) |
| TC-U-51 | `WriteLink_ShouldInsertTheLinkRow_WithThePlannedDcmUId` | `SysDcmSchemaInSettings {SysDcmSettings = row Id, SysDcmSchemaUId = planned UId}` written BEFORE any schema save (plain Guid column [traced PS/CrtBase/Schemas/SysDcmSchemaInSettings/metadata.json:21-28]) |
| TC-U-52 | `Write_ShouldInsertOnlyTheLinkRow_WhenTheSettingsRowIsReused` | no `Insert` into `SysDcmSettings` (Q13 reuse) |
| TC-U-53 | `Bind_ShouldNameEachBindingAfterItsTableAndRowId` | `SysDcmSettings_<id:N>`, `SysDcmSchemaInSettings_<id:N>` (lit, Q15); package = P; one row Id per binding |
| TC-U-54 | `Bind_ShouldNotBindAReusedRow_ThatIsAlreadyBound` | a reused settings row bound in another package is not rebound; N8 is produced |
| TC-U-55 | `Bind_ShouldFail_WhenTheReadBackFindsNoBinding` | the `SysPackageSchemaData` read-back by name + package is mandatory, because binder validation failures return silently (design §5 W3) |
| TC-U-56 | `Compensate_ShouldDeleteBindingsBeforeRows` | `DeletePackageSchemaData(name, packageUId)` for each binding, then the link row, then the settings row (only if created by this call) |
| TC-U-57 | `FindSettings_ShouldMatchObjectFormAndSectionFormRows` | the S8 predicate `SysSchemaUId = entity OR SysModuleEntity.SysEntitySchemaUId = entity` (the platform reader's own, v r4-C6); both forms are found (v r10-C5) |

### 2.8 Dependency planner (Q16) - `CaseDependencyPlannerTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-60 | `Plan_ShouldRequireThePackageWhereTheColumnIsDefined` | `FindItemByRealUId(column.CreatedInSchemaUId)` kept when its `Name` is the entity name, else the root item (g3 rec. 1a; design §5 S12) |
| TC-U-65 | `Plan_ShouldRequireTheReplacementPackage_WhenTheColumnWasAddedBelowTheTopReplacement` | column added in a replacement in package X that is not the top replacement -> X, not the top replacement's package (`Items` hold the last replacement, CORE/Terrasoft.Core/SchemaManager.cs:4026-4042) |
| TC-U-66 | `Plan_ShouldRequireTheRootPackage_WhenTheObjectHasTwoUnrelatedReplacements` | object and lookup root packages via `FindItemByRealUId(runtimeSchema.UId)`, never `GetItemByName(...).PackageUId` |
| TC-U-61 | `Plan_ShouldRequireTheObjectAndLookupRootPackages` | g3 rec. 1b |
| TC-U-62 | `Plan_ShouldRequireCrtCoreBaseAndCrtBase_ForTheSettingsBindings` | g3 rec. 1c |
| TC-U-63 | `Plan_ShouldDropPackagesAlreadyInTheHierarchy` | missing = not in P's hierarchy; an empty "missing" set is the common case [corpus 61/61 shipped DCMs satisfy the closure, g3] |
| TC-U-64 | `Plan_ShouldAcceptAdditionalInputs_WithoutAContractChange` | the input set is open (design §12: later steps add user-task / callee / page schemas) - a seam test |

### 2.9 Verifier (V1, V2) - `CaseVerifierTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-70 | `Verify_ShouldPass_WhenTheItemIsEnabledActiveAndMatchesThePlan` | root `GetIsEnabled()`, `GetIsActiveVersion(item.Id)` (public Guid overload), entity and column properties, stage list, colours, connection count (g1 "What the tool must do" 3) |
| TC-U-71 | `Verify_ShouldFail_WhenTheItemIsNotRegistered` | - |
| TC-U-72 | `Verify_ShouldFail_WhenNotEnabledOrNotTheActiveVersion` [each] | the bar needs both (v r9-C1) |
| TC-U-73 | `Verify_ShouldFail_WhenStagesDifferFromThePlan` [order; a `StageRecordId`; a missing colour; a connection count] | - |
| TC-U-74 | `Verify_ShouldFail_WhenAnotherEnabledCaseCompetes` | V2: the substituted `IEnabledCaseReader.FindEnabled(entity)` must return exactly ours (the adapter over `FindEnabledDcmSchemas` is covered by TC-I-02 / TC-I-06 only) |

### 2.10 Handler (S1..S21, W1..W4, compensation) - `CaseBuildHandlerTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-80 | `Build_ShouldRefuseBeforeAnyRead_WhenARightIsMissing` [each of: portal user, `CanManageDcm`, `CanManageSolution`, append on `SysDcmSettings`, append on `SysDcmSchemaInSettings`] | `rights-missing` names the right; the entity resolver, reader and repository receive nothing (Q5; no name probing) |
| TC-U-81 | `Build_ShouldRefuseUnknownKeys_AndAnswerStepsNotSupportedForSteps` [top-level `color`; `stages[0].steps`; `stages[0].required`] | `unknown-keys`; a `steps` member gets `steps-not-supported` (design §3.1; seam) |
| TC-U-82 | `Build_ShouldRefuseAMissingRequiredField` [each of the three] | `invalid-request` |
| TC-U-83 | `Build_ShouldRefuseAMissingOrForeignOrLockedPackage` | `package-not-found` / `package-read-only` |
| TC-U-84 | `Build_ShouldRefuseAnUnknownObject_AndViewsOrVirtualObjects` | `entity-not-found` / `entity-not-supported`; the RUNTIME schema is resolved (GeneratedDcmSaver defect 2, r4 §7) |
| TC-U-85 | `Build_ShouldRefuseTheColumn` [absent from the runtime schema but present in the design instance -> `column-not-published`; absent -> `column-not-found`; text column -> `column-not-lookup`; lookup without primary display column -> `column-not-lookup`] | core checks none of this (v r1-C5) |
| TC-U-86 | `Build_ShouldAnswerAlreadyPresent_WithoutWrites_WhenAnEquivalentCaseExists` | same object, column, ordered `StageRecordId` list, link row present -> `case.status = already-present`; writer, binder, repository receive nothing (Q12) |
| TC-U-87 | `Build_ShouldRefuseCaseExists_WhenADifferentEnabledCaseExists` [other column; other order; other value set] | `case-exists` names the case and its column (v r1-C8) |
| TC-U-88 | `Build_ShouldCreate_AndNoteDisabledCases` | disabled DCMs only (the `LeadCase` precedent) -> created + N5 |
| TC-U-89 | `Build_ShouldRefuseSettingsConflict` [other stage column; a filter column; several rows] | `settings-conflict` (Q13) |
| TC-U-90 | `Build_ShouldRefuseAnExplicitNameThatIsInvalidOrTaken` [no prefix; taken case-insensitively] | `name-invalid` / `name-taken`; never suffixed (Q26) |
| TC-U-91 | `Build_ShouldSuffixTheDefaultName_AndWarn` [`UsrOrderCase` taken] | `UsrOrderCase2` + warning (Q26) |
| TC-U-92 | `Build_ShouldRefuseDependenciesMissing_WhenNotAllowed` and `Build_ShouldListMissingDependencies_WhenAllowed` | `AllowMissingDependencies` false -> refusal listing the exact `add-package-dependency` calls; true -> `MissingDependencies` in the planned answer (Q16) |
| TC-U-93 | `Build_ShouldRefuse_WhenPlatformValidationReportsErrorsOrNothing` | `platform-validation-failed` on errors AND on a null result (fail closed; S14) |
| TC-U-94 | `Build_ShouldWriteNothing_WhenValidateOnly` | `planned` with ordered captions, `orderSource`, name, settings plan, required/missing packages, obligations; no writer/binder/repository call |
| TC-U-95 | `Build_ShouldWriteSettingsThenLinkThenBindingsThenSchema` | `Received.InOrder`: W1 -> W2 -> W3 (+ read-back) -> W4 -> V1 -> V2 (Q7) |
| TC-U-96 | `Build_ShouldDeleteItsRows_WhenABindingIsMissing` | `failed-rolled-back`; no schema save attempted |
| TC-U-97 | `Build_ShouldRemoveEverythingItWrote_WhenTheSaveFails` [false; throws] | `RemoveItemByUId` if registered, bindings, rows; leftover probe -> `failed-rolled-back` or `failed-leftover` listing schema name, start-event rows by `SysSchema` Id, binding names, row Ids (wording discipline of `ProcessBuildHandler`, r6 §2 step 13) |
| TC-U-98 | `Build_ShouldRemoveTheCase_WhenVerificationFails` | V1 or V2 mismatch -> compensation; V2 -> `case-exists` |
| TC-U-99 | `Build_ShouldReportObligations_PerArtifact` | `compileRequired = false`, `restartRequired = false`, `reloadOpenPages = true`; artifacts `dcm`, `settings-data` each `compile: false` (v r1-C1, v r4-C4) |
| TC-U-100 | `Build_NotesShouldNeverContainTheCompileMarker` [every note and warning of every outcome] | no note text matches the compile marker phrase clio gates on (v r7-C5; design §3.4) |
| TC-U-101 | `Build_ShouldReadTheLookupOnlyAfterTheGateAndTheCaseCheck` | S1 -> S7 -> S8 -> S9 ordering: an `already-present` or `case-exists` answer reads no lookup rows |
| TC-U-102 | `Build_ShouldAddTheNotesThatApply` [N1, N2, N3 always on success; N4 when the lookup values are not bound by this call; N5 with disabled cases] | note texts are the design §3.4 shapes; N3 states the existing-records rule (v r1-C7) |
| TC-U-103 | `Build_ShouldSerialiseCallersOnTheSameObject` | two concurrent calls for one entity UId: the second sees the first's case (-> `already-present` or `case-exists`), never two saves (design §5 lock). **probe**: a `Barrier` + substituted repository that blocks; drop the row if it proves flaky and rely on V2 |
| TC-U-104 | `Build_ShouldResolveThePackageThroughThePackageCatalog` | the `IPackageCatalog` split of `IProcessSchemaRepository` changes no existing behaviour: existing process handler tests stay green unchanged (regression row) |

Skeleton of the write-order row:

```csharp
[TestFixture(Category = "UnitTests"), Category("PreCommit")]
public class CaseBuildHandlerTests : BaseComposableAppTestFixture {

	[Test]
	[Description("The settings row, the link row and both bindings are written before the case schema is saved, and the case is verified after the save.")]
	public void Build_ShouldWriteSettingsThenLinkThenBindingsThenSchema() {
		// Arrange
		CaseBuildTestContext context = CaseBuildTestContext.ForNewCase(this, lookupValues: 3);   // all collaborators substituted
		CaseBuildHandler sut = context.CreateHandler();

		// Act
		BuildCaseResponse response = sut.Build(context.Request);

		// Assert
		response.Outcome.Should().Be("created", because: "every check passed and every write succeeded");
		Received.InOrder(() => {
			context.SettingsWriter.WriteSettings(Arg.Any<CaseSettingsPlan>());
			context.SettingsWriter.WriteLink(Arg.Any<Guid>(), context.PlannedDcmUId);
			context.Binder.Bind(Arg.Any<IReadOnlyList<CaseBinding>>());
			context.Repository.Save(Arg.Any<DcmSchemaManagerItem>());
			context.Verifier.Verify(Arg.Any<CaseVerificationPlan>());
		});
	}
}
```

### 2.11 Gate, endpoint, wire contract, composition

| TC | Test (fixture) | Proves |
|---|---|---|
| TC-U-110 | `EnsureCanManageDcm_ShouldRefuseAPortalUser` (`ProcessDesignGuardTests`) | `ConnectionType != General` refused (mirrors [traced CORE/Terrasoft.Core/DcmProcess/DcmSchemaManager.cs:132-139]) |
| TC-U-111 | `EnsureCanManageDcm_ShouldRefuse_WithoutCanManageDcm` | operation right |
| TC-U-112 | `EnsureCanManageDcm_ShouldRefuse_WithoutCanManageSolution` | `GetCanModifyPackageElement` [traced CORE/Terrasoft.Core/DB/DBSecurityEngine.cs:5344-5347] |
| TC-U-113 | `EnsureCanManageDcm_ShouldRefuse_WithoutAppendOnEitherSettingsTable` [each table] | `GetEntitySchemaOperationsRightLevel` has `CanAppend` [traced CORE/Terrasoft.Core/DB/DBSecurityEngine.cs:3707] |
| TC-U-114 | `EnsureCanManageDcm_ShouldNotRequireCanManageProcessDesign` | Q5: the platform does not require it for cases |
| TC-U-130 | `BuildCase_ShouldDelegateToTheDesigner` (`ProcessDesignServiceTests`) | the op reaches the facade through `InjectedServices` (r8 §3.9) |
| TC-U-131 | `ProcessDesignServiceWireContractTests` (existing rows + new) | operation mirror count 8 -> 9; `BuildCaseRequest` and `CaseStageRequest` derive from `ExtensibleRequestContract`; wire names match design §4 [traced PBT/EntryPoints/WebService/ProcessDesignServiceWireContractTests.cs:50-56,131-135,183-192 per v r6-C4] |
| TC-U-132 | `CrtProcessBuilderAppTests` (existing tripwires + new) | the composition root resolves `CaseBuildHandler` and every collaborator of 2.2 |
| TC-U-133 | `CiContractGuardTests` (existing) | every new fixture is categorised |
| TC-U-134 | `BuildCaseRequest_ShouldKeepAStepsMemberInExtensionData_AndTheHandlerRefusesIt` | the `steps` seam: a later additive `Steps` member does not break today's wire (design §4, §12) |

Under Q2 option (b) (sibling `DcmDesignService`) TC-U-130/131 move to a new fixture and a NEW mirror test is needed,
because the package mirror reflects only `ProcessDesignService` (v r7-C3).

---

## 3. Parity tests (`PBT/`) - `DcmMetadataParityTests`

| TC | Test | Proves |
|---|---|---|
| TC-P-01 | `WriteMetaData_ShouldProduceTheShippedKeySet_ForAThreeStageCase` | the composed case's `DcmSchema.WriteMetaData` (public [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:659-676]) has the key set of the shipped minimal flat, unfiltered, element-less DCM `KnowledgeArticlePublishing` [traced PS/CrtKnowledgeManagementVersions/Schemas/KnowledgeArticlePublishing/metadata.json:14-96] (v r5-C10), committed verbatim under `PBT/Fixtures/Dcm/KnowledgeArticlePublishing.metadata.json`; UIds replaced by role (case, stage i, connection i), colour and `IJ10` set aside (IJ10 is a writer choice, 40/61 v r5-C10). **probe**: whether `WriteMetaData` runs over the plain `RequireMock.DCM` substitute (core uses `ForPartsOf`, r8 §3.10); fallback = `Substitute.ForPartsOf<DcmSchema>` with `LoadLocalizableValues` not called (r8 §1 item 6) |
| TC-P-02 | `WriteMetaData_ShouldWriteNoIsDefaultAndNoElements` | element-less, no `IsDefault` key, `SystemElements` empty (v r4-C3, v r8-C12) |
| TC-P-03 | `WriteMetaData_ShouldWriteAllConnections_AsStageUIdPairs` | n*(n-1) connection objects reference stage UIds, never record Ids (conflict 7) |

No designer capture is needed for parity: g2 §3-§4 traced route A and route B as byte-equivalent for a minimal DCM
except `B8` / `IJ10`, both legitimately absent in shipped DCMs. M1 is the run-time confirmation.

---

## 4. clio unit tests (`CLIO/clio.tests`)

| TC | Fixture.test | Proves | Module |
|---|---|---|---|
| TC-C-01 | `CreateDynamicCaseToolTests.Tool_ShouldBeDestructiveAndIdempotent` | annotations `ReadOnly=false`, `Destructive=true`, `Idempotent=true` (Q12 option a; `false` if the owner picks b), `OpenWorld=false`; `[McpToolExecution(Location=Worker, Lifetime=PerCall)]` (design §3) | McpServer |
| TC-C-02 | `CreateDynamicCaseToolTests.Tool_ShouldPassEveryArgumentToTheResolvedCommand` | environment-aware `InternalExecute<CreateDynamicCaseCommand>`; options mapped field for field | McpServer |
| TC-C-03 | `CreateDynamicCaseToolTests.Tool_ShouldRefuse` [null args; an unknown key via the `[JsonExtensionData]` bag; each missing required key] | copied argument handling [traced CLIO/clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs:32,372-395,455-456] | McpServer |
| TC-C-04 | `CreateDynamicCaseToolTests.Tool_ShouldNormaliseStages` [array of `{value}`; array of strings; a JSON string of either] | one wire shape `[{"value": ...}]` reaches the service | McpServer |
| TC-C-05 | `CreateDynamicCaseToolTests.Tool_ShouldRefuseStepsInsideAStage_BeforeAnyCall` | `steps-not-supported` on the clio side, no HTTP call (seam, design §3.1) | McpServer |
| TC-C-06 | `CreateDynamicCaseToolTests.Tool_ShouldMapDryRunToValidateOnly_AndCallNoWriter` | `dry-run` -> one validate pass, no dependency add, no page write | McpServer |
| TC-C-07 | `CreateDynamicCaseToolTests.Tool_ShouldCarryTheCompileNotRequiredNote` | `CommandExecutionResult.WithCompileNotRequiredNote()` [traced CLIO/clio/Command/McpServer/Tools/CommandExecutionResult.cs:64] | McpServer |
| TC-C-08 | `DynamicCaseServiceTests.Execute_ShouldPostAWrappedBody_ToTheBuildCaseRoute` | `BodyStyle = Wrapped` body; route from `IServiceUrlBuilder.Build(KnownRoute.BuildCase)` | Command |
| TC-C-09 | `DynamicCaseServiceTests.Execute_ShouldRunTheValidatePassBeforeTheBuildPass` [`add-package-dependencies` true and false] | `ValidateOnly = true` first with `AllowMissingDependencies` = `add-package-dependencies`; the build pass ALWAYS sends `AllowMissingDependencies = false` (design §4) | Command |
| TC-C-10 | `DynamicCaseServiceTests.Execute_ShouldAddOnlyTheMissingDependencies_AndOnlyWhenAllowed` | `IPackageDependencyManager.AddDependencies(P, missing)` [traced CLIO/clio/Package/IPackageDependencyManager.cs:37]; not called when nothing is missing or the flag is `false` (Q16) | Command |
| TC-C-11 | `DynamicCaseServiceTests.Execute_ShouldStopBeforeTheBuildPass_OnADependencyCycle` | storage validation error -> `dependency-cycle`; build pass never called | Command |
| TC-C-12 | `DynamicCaseServiceTests.Execute_ShouldSkipThePageReads_WhenTheServerRefuses` | a rights or input refusal on the validate pass reaches no page read | Command |
| TC-C-13 | `DynamicCaseServiceTests.Execute_ShouldNeverCallTheBuildPass_WhenThePageStepIsRefused` [`record-page-not-found`, `record-page-ambiguous`, `record-page-not-freedom`, `page-entity-mismatch`, `placement-unsupported`, `page-append-rejected` from the append dry run] | refusals BEFORE any write, including the save's own gates run by `PageUpdateCommand.TryUpdatePage` with `DryRun = true` (Q20; design §6 C3) | Command |
| TC-C-14 | `DynamicCaseServiceTests.Execute_ShouldContinueToThePageStep_WhenTheCaseIsAlreadyPresent` | Q12: `already-present` + page insert or `page.status = already-present` | Command |
| TC-C-15 | `DynamicCaseServiceTests.Execute_ShouldKeepTheCase_AndReturnRetryArgs_WhenThePageWriteFails` | `outcome = created-page-failed`, `page.retry = {tool: "update-page", args}` + the fragment; nothing is undone | Command |
| TC-C-16 | `DynamicCaseServiceTests.Execute_ShouldSayOutcomeUnknown_ForAnUnreadableReply` | the `CreateBusinessProcessService` wording for a reply that is not an envelope after a write | Command |
| TC-C-17 | `DynamicCaseServiceTests.Execute_ShouldMapA404OrANonEnvelopeReply_ToProcessBuilderWithoutBuildCase` | on the VALIDATE pass only (nothing written yet); covers the stand's unmerged 1.6.6.142 build (v r10-C2; Q22) | Command |
| TC-C-18 | `RecordPageResolverTests` [one untyped default; typed-only (`Activity` shape) -> `record-page-ambiguous` with `{pageSchemaName, typeColumnValue}`; portal-first ordering (`Case` shape); role "All employees" vs another role; `isDefault` missing -> not resolved; empty add-on + Freedom `SysModuleEdit` (`AppFeature` shape); empty add-on + Classic card -> `record-page-not-freedom` with the remedy; explicit `page-schema-name` still checked] | the g4 §7 algorithm over a substituted `IRelatedPageAddonService` [traced CLIO/clio/Command/RelatedPages/RelatedPageAddonService.cs:110-124] and an ESQ executor; `DefaultPageAddonReader.ReadWebDefaultPageUId` / `RelatedPageAddonMetadata.TryFindUntypedDefault` (internal, mobile converter) not reused (g4 §6) | Command |
| TC-C-19 | `ProgressBarPlacementTests` [exact fragment of design §7.3 as a marker-wrapped append body (`/**SCHEMA_VIEW_CONFIG_DIFF*/` pairs), the same string in `page.fragment` and `retry.args`; view checks read `PageGetCommand` `Bundle.ViewConfig`, not `PageMergedConfigResolver`; forbidden keys `recordId`, `value`, `stages`, `loadData`, `stageChanged`, `setAllowedStages` absent; bar already present -> `already-present`; mini page (`crt.GridContainer` `MainContainer`) -> `placement-unsupported`; primary data source entity differs -> `page-entity-mismatch`; `ProgressBarContainer` / `ProgressBar` names taken -> both suffixed `_<8 hex>`; non-tabbed template -> N6; `EntityEditPagesUITypes` Classic -> N7] | wizard values (v r3-C8), forbidden keys (v r3-C2, [corpus 0/24]), append replaces same-name entries (r7 §2.2) | McpServer |
| TC-C-20 | `ProgressBarPlacementTests.Write_ShouldValidateDataSourcesThenAppend_InTheDesignPackage` | `IPageDataSourceReferenceValidator` then `PageUpdateCommand.TryUpdatePage` `Mode = append` in the default design package (Q17, Q18; [traced CLIO/clio/Command/PageUpdateOptions.cs:287]); N8 when it differs from P | McpServer |
| TC-C-21 | `ServiceUrlBuilderCommandTests` rows (`clio.tests/Common/ServiceUrlBuilder.cs`) | `KnownRoute.BuildCase` at the next free value after rebase (122 is claimed by two remote branches, `_verdicts.md` conflict 4): `.../0/rest/ProcessDesignService/BuildCase` (NF) and `.../rest/ProcessDesignService/BuildCase` (.NET 8), pattern [traced CLIO/clio.tests/Common/ServiceUrlBuilder.cs:192-193] | Common |
| TC-C-22 | `ProcessDesignerRequiresPackageAttributeTests` (+ `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement`, existing) | the new options type declares `[RequiresPackage("CrtProcessBuilder", "<Q22 cut>")]` (v r7-C2) | Command |
| TC-C-23 | `BundledProcessBuilderPackageTests` (existing) | archive hash, version, producing commit, `ModifiedOnUtc` moved by `rebundle-process-builder.ps1`; by hand in the SAME commit: `ExpectedOperationContractCount` 8 -> 9, a NEW pin for `EnsureCanManageDcm` call sites = 1; `EnsureCanManageProcessDesign` stays 6, `EnsureCanCompileConfiguration` stays 1 [traced CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:292,361,373,384] (v r6-C4) | Common |
| TC-C-24 | `PassthroughToolClassificationRegistry` (`clio.tests/Command/McpServer/PassthroughToolClassificationRegistry.cs`) | `create-dynamic-case` classified destructive, reached through `clio-run-destructive` (long-tail) | McpServer |
| TC-C-25 | `ToolContractPayloadBudgetTests`, `ToolContractShortFormTests` (existing) | one index line fits (18,312 / 18,432 B; v r7-C4); the object index (49,209 / 49,408 B) is re-measured on the branch and re-pinned deliberately if needed; both numbers into the PR body | McpServer |
| TC-C-26 | `McpToolArgsWireContractTests` (existing) | kebab-case wire keys of design §3.1 | McpServer |
| TC-C-27 | `CompileNotRequiredNoteGateTests` (existing) | the tool opts in; no server note carries the marker (pairs with TC-U-100) | McpServer |
| TC-C-28 | `McpE2eSelectionCoverageTests` (existing) | the new e2e fixture is selected when its tool or service changes | (root) |
| TC-C-29 | `WorkspaceTemplateGuidanceDriftTests` (existing) + `Fixtures/curated-knowledge-names.json` | MANDATORY: the tool [Description] names `process-dynamic-case` (plan CL-8), and `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` fails on a name absent from `availableNames` [traced CLIO/clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs:540-588]; re-pin `libraryVersion`, `sequence`, `availableNames` to the published KB generation, as a commit after the KB merge (plan §7) | McpServer |
| TC-C-30 | `DynamicCaseResultTests.Result_ShouldKeepTheDescribeShape_AndUnknownServerMembers` | the `case` block keys of design §3.3; per-artifact obligations; a `[JsonExtensionData]` bag on each result type keeps members a newer package adds (the later `describe-dynamic-case` reuses the type) | Command |
| TC-C-31 | `CreateDynamicCaseCommandTests` (on `BaseCommandTests<CreateDynamicCaseOptions>`, command resolved from DI) [one test per row of design §6.1: `planned`, `created`, `already-present`, `created-page-skipped`, `created-page-failed`, `refused`, `failed-rolled-back`, `failed-leftover`, unreadable reply] | `success`, exit code, MCP `isError` and the printed notes/warnings per outcome; `created-page-failed` never says "nothing written" (plan CL-14; precedent CLIO/clio.tests/Command/CreateBusinessProcessCommandTests.cs:17-19) | Command |
| TC-C-32 | `ProcessDesignerGoLiveTests` (existing) | `CreateDynamicCaseTool` and its tool name added: no `[FeatureToggle]`, resolvable by default (Q28 (a) locked in) [traced CLIO/clio.tests/Command/McpServer/ProcessDesignerGoLiveTests.cs:28-48,61,80]; the prompt list unchanged (no prompt, plan §8.1) | McpServer |
| TC-C-33 | `DynamicCaseServiceTests.Execute_ShouldUndoAddedDependencies_WhenTheBuildPassDoesNotCreate` [build-pass refusal after an add; `failed-rolled-back` after an add; `RemoveDependencies` throws] | `RemoveDependencies(P, added)` [traced CLIO/clio/Package/IPackageDependencyManager.cs:88] -> `dependencies.status = removed-after-failure`; removal failure -> `left-after-failure` + warning and no "nothing written" claim (design §3.2, §6 C6) | Command |

New tests follow the host fixture's placement of `[Category("Unit")]` and `[Property("Module", ...)]`; a
`BaseCommandTests<TOptions>` fixture gets no category (clio AGENTS.md). The change spans McpServer, Command and
Common; `Common` is a full-suite trigger, so the full unit suite runs before every commit. Write `TestCategory`,
never `Category`; close stdin (`McpE2eSelectionCoverageTests` runs `git init`, as the call-web-service-element plan
of the same epic records in its section 4):

```
# iterating
dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=McpServer|Module=Command|Module=Common)" --no-build < /dev/null
# before every commit (quoted in the commit message and the PR body)
dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null
```

---

## 5. clio MCP e2e (`CLIO/clio.mcp.e2e/CreateDynamicCaseToolE2ETests.cs`)

### 5.1 Fixture rules

| Rule | How |
|---|---|
| Attributes | `[TestFixture][AllureNUnit][AllureFeature(CreateDynamicCaseTool.ToolName)][NonParallelizable][Category(McpE2ECategories.ProcessDesigner)]` (`"McpE2E.ProcessDesigner"` [traced CLIO/clio.mcp.e2e/Support/Configuration/McpE2ECategories.cs:40]) |
| Session and gate | `ProcessDesignerE2EArrange.StartAsync("Minimal dynamic case", MinimumPackageVersion)`; `MinimumPackageVersion` = the Q22 cut, the same literal as the clio floor; unreachable environment = Ignored; lower package = Ignored, or Fail under `McpE2E__RequireProcessBuilder` [traced CLIO/clio.mcp.e2e/Support/Mcp/ProcessDesignerE2EArrange.cs:45-63,236-247] |
| Arrange (Q25 option a) | the SVN `AutoTestClioMcp` package gains: object `AutoTestClioMcpDcm` (name to confirm) with a Lookup column `Status` -> lookup `AutoTestClioMcpDcmStatus` with an integer `Position` and 4 values "New" (1), "In progress" (2), "On hold" (3), "Done" (4), inserted in the order Done, On hold, New, In progress so that `Position`, `CreatedOn` and display order all differ; a second Lookup column `Priority` -> an EMPTY lookup; a text column `Note`; a tabbed Freedom UI record page (`PageWithTabsFreedomTemplate`) registered in the object's RelatedPage add-on. `AutoTestClioMcp` is deployed on every ProcessDesigner-lane stand [measured g5 §2.1]; today its entity has no lookup column [traced g5 §3.1] |
| Interim arrange (until the SVN change ships) | a `LocalOnly` + `[Explicit]` + `McpE2E.Manual` + TeamCity-guard twin that builds the same shapes with `sync-schemas` (`create-lookup` + `seed-rows`, `update-entity` add Lookup), because adding a column PUBLISHES (clio.mcp.e2e/AGENTS.md:19-38; g5 §3.2). Whether `seed-rows` can set `Position` is not traced: fallback = an `update-entity` Integer column + a DataService update per row |
| Reads | `ProcessDesignerStoredReads.SelectInlineAsync` / `SelectToFileAsync` / `DecodeMetaData` [traced CLIO/clio.mcp.e2e/Support/Mcp/ProcessDesignerStoredReads.cs:13-26,120,137,164]; `get-page` for the page body |
| Writes | strictly sequential; one stand at a time (parallel schema writes take a .NET Framework app pool down [traced CLIO/clio.mcp.e2e/ProcessFileElementToolE2ETests.cs:40-41 per g5 §4]) |
| Cleanup | `finally`, sequential, in this order: `remove-data-binding-row-db` on the `SysDcmSchemaInSettings_<id:N>` binding, then on the `SysDcmSettings_<id:N>` binding ONLY when the run reported `settings.status = created` (it deletes the live row and cascades every link row under it; TC-I-11's `reused` row is left alone, design §9), then `delete-schema` on the DCM, then `delete-schema` on the page replacing schema if this run created it (g5 §4). Leftovers are reported, not thrown. Binding names are unique per DCM (Q15), so cleanup never touches another case's rows |
| Assertions | tool reply AND stored bytes; refusals assert the `errorCode` and that nothing was written (row counts before == after); AAA, `because`, `[Description]` without a Jira key |
| Stand runtime | the whole fixture runs once against a .NET Framework stand and once against a .NET 8 stand (Q24); the clio host framework (`-f net10.0` / `-f net8.0`) is independent of that |

### 5.2 Cases

| TC | Test | Asserts |
|---|---|---|
| TC-I-01 | `CreateDynamicCase_ShouldPlanWithoutWriting_OnDryRun` | `outcome = planned`, `orderSource = orderColumn:Position`, captions New, In progress, On hold, Done; `dependencies.status`; `page.status = planned` with the fragment; no `SysSchema` row with `ManagerName = DcmSchemaManager` for the object, no settings rows, page body unchanged |
| TC-I-02 | `CreateDynamicCase_ShouldStoreTheCase_TheSettingsAndTheBindings` | `SysSchema` row in P; decoded metadata: 4 stages in `Position` order with the lookup Ids as `StageRecordId`, `#0C8701` each, 12 connections, no elements; `SysSchemaProperty` `Enabled = True`, `EntitySchemaUId`, `StageColumnUId` (g1 (a)); one `SysDcmSettings` row (object form, `Filters` lit) and one `SysDcmSchemaInSettings` row with `SysDcmSchemaUId` = the case UId; both `SysPackageSchemaData` bindings in P with the Q15 names; reply `obligations.compileRequired = false`, `restartRequired = false`, `reloadOpenPages = true`; no compile call was made |
| TC-I-03 | `CreateDynamicCase_ShouldMeetTheDesignerOpenPreconditions` | the link row exists for the opened UId with a non-empty `SysDcmSettings` (else "DcmSettingsId not found.", v r2-C4); every `StageRecordId` is a row of the stage lookup readable by the test user (else the designer resets the stage, v r2-C5); `case.designerUrl` has `0/` on .NET Framework and none on .NET 8 (v r9-C8). The open itself is CA-5 / TC-02 |
| TC-I-04 | `CreateDynamicCase_ShouldBeActiveForTheProgressBar_Immediately` | the bar's own source answers: `DcmSchemaManagerRequest` (DataService) returns the case with `IsActive && IsActiveVersion` right after the call (M2; g1 verdict). If clio has no generic DataService manager-request helper, the fallback read is `VwSysDcmLib` (`IsActive`, `IsActiveVersion`), measured readable on stand `Creatio` [measured g1 (a)] |
| TC-I-05 | `CreateDynamicCase_ShouldInsertTheProgressBar_IntoTheRecordPage` | `page.resolvedBy = related-page-addon`; `get-page` body: `ProgressBarContainer` (`crt.FlexContainer`) at index 0 of `MainContainer`, `ProgressBar` (`crt.EntityStageProgressBar`) with `entityName`, `saveOnChange: true`, `askUserToChangeSchema: false`; none of the forbidden keys; written to the default design package (Q17) |
| TC-I-06 | `CreateDynamicCase_ShouldAnswerAlreadyPresent_OnAnIdenticalRerun` | second identical call: `case.status = already-present`, `page.status = already-present`; `SysSchema`, settings, link and binding row counts unchanged (Q12) |
| TC-I-07 | `CreateDynamicCase_ShouldRefuseCaseExists_ForADifferentOrderOnTheSameObject` | explicit `stages` in display order on the object of TC-I-02: `case-exists` names the case and `Status`; nothing written |
| TC-I-08 | `CreateDynamicCase_ShouldRefuseAndWriteNothing` [`Note` -> `column-not-lookup`; a missing column -> `column-not-found`; `stages` with an unknown value -> `stage-value-unknown`; `stages` naming 3 of 4 values -> `stage-list-incomplete`; a stage with `steps` -> `steps-not-supported`] | on a fresh object without a case (or before TC-I-02); row counts before == after |
| TC-I-09 | `CreateDynamicCase_ShouldRefuseAnEmptyLookup` [`Priority`] | `lookup-empty`; nothing written |
| TC-I-10 | `CreateDynamicCase_ShouldRefuseAPageItCannotPlace_BeforeAnyWrite` [an `Activity`-shaped typed page set without `page-schema-name` -> `record-page-ambiguous`, if the arranged object offers one; otherwise `page-schema-name` = a mini page -> `placement-unsupported`] | refusal before the build pass: no `SysSchema` row for the object afterwards |
| TC-I-11 | `CreateDynamicCase_ShouldReuseALeftOverSettingsRow` | after TC-I-02's DCM is removed with `delete-schema` ONLY (the settings row and its binding stay, g5 §4), a re-run answers `settings.status = reused`, writes and binds only the link row (Q13) |
| TC-I-12 | `CreateDynamicCase_CaseShouldRunForANewRecord` (optional, `[Category("McpE2E.Manual")]` until stable) | insert a record with "New" -> a running instance of the case for the record; update `Status` to "Done" -> the instance moves (v r1-C7). The exact process-log query is fixed when the test is written; M7 measures it first |
| TC-I-13 | `CreateDynamicCase_ShouldAddAMissingPackageDependency` (`LocalOnly` twin only) | P that does not depend on the object's package: `dependencies.status = added`, `dependencies.added` lists exactly the missing set; with `add-package-dependencies = false` -> `dependencies-missing` with the calls (Q16) |
| TC-I-14 | `CreateDynamicCase_ShouldWriteOnlyTheCase_WhenTheBarIsNotWanted` | `add-progress-bar = false`: case created, page body unchanged, `page.status = skipped` |

### 5.3 Evidence

The PR lane excludes `McpE2E.ProcessDesigner` (`baseFilter` [traced CLIO/clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:51]),
so a green PR proves nothing here. The ProcessDesigner TeamCity lane deploys .NET Framework only (Studio, PostgreSQL,
IIS; `ProductName=Studio` pinned by the trigger workflow [traced CLIO/.github/workflows/teamcity-mcp-e2e.yml:436,474,514];
g5 §2). Evidence = the lane run (.NET Framework) plus a hand run against the owner's .NET 8 stand, both recorded in the
clio PR body with passed / failed / ignored and Ignored = 0:

```
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~CreateDynamicCaseToolE2ETests"
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net8.0  --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~CreateDynamicCaseToolE2ETests"
```

(`McpE2E__Sandbox__EnvironmentName` names the stand; run once per stand runtime.) A .NET 8 lane run would need a
`StudioNet8` product zip (`BuildProductNet8`, on demand) and `ProductName=StudioNet8` + `BuildPath` overrides that
have never been run (g5 §2.3); that is a Q24 option (b), not part of this plan. Every stand write waits for the
owner's word.

---

## 6. clio-knowledge tests (`KB/automation/Clio.Knowledge.Bundle.Tests`)

New class `ProcessDynamicCaseGuidanceTests` (precedent `SendEmailAttachmentsGuidanceTests.cs`).

| TC | Test | Proves |
|---|---|---|
| TC-K-01 | `Guide_ShouldNameTheToolItsArgumentsAndPrerequisites` | `create-dynamic-case` via `clio-run-destructive`; required arguments; prerequisites: object published, Lookup column, values exist, editable package; the Q22 floor |
| TC-K-02 | `Guide_ShouldStateTheOrderRule` | explicit > order column > display value; display order turns "New, In progress, Done" into "Done, In progress, New" (g6 §4); "pass `stages` when the order matters; use `dry-run` to preview" |
| TC-K-03 | `Guide_ShouldStateTheRuntimeTruth` | no compile, no restart; reload open pages (F5); existing records start on the next change of the column, new records on insert (v r1-C7); .NET 8 stated as "expected" until M8 (Q24) |
| TC-K-04 | `Guide_ShouldListWhatIsNotSupportedYet` | steps inside stages, conditions, alternative / transition rules, modifying an existing case, Classic / mobile / mini pages (design §16) |
| TC-K-05 | `Guide_ShouldMapEveryRefusalToARemedy` | each `errorCode` of design §3.2 with its remedy (e.g. `column-not-lookup` -> add a Lookup column first; `record-page-ambiguous` -> pass `page-schema-name`; `case-exists` -> modifying is not supported yet) |
| TC-K-06 | `Guide_ShouldStateTheUndoOrder` | link binding; settings binding ONLY when the call reported `settings.status = created` (never when `reused`); `delete-schema` (case); page replacing schema (design §9) |
| TC-K-07 | `Guide_ShouldCarryNoProcessSetBanner` | no banner, so `process-modeling` need not index it (v r7-C8) |
| TC-K-08 | `Routing_ShouldPointToTheArticle_FromProcessesAndPages` | `routing.md` Business processes row and Pages pointer carry `name=process-dynamic-case` (Q23) |
| TC-K-09 | `ProcessModeling_ShouldNotListDcmAmongCompileObligations` | whitespace-insensitive check that `KB/guidance/mcp/guides/processes/process-modeling.md:157-158` no longer names "DCM" in the "carry their own compile obligations" list (design §13) |
| TC-K-10 | size gates (existing): `GuidanceInlineReplyBudgetTests` (`process-modeling` has about 13 B left, so the edit removes bytes), `ProcessGuideResponseSizeTests` | v r7-C8 |
| TC-K-11 | manifest (existing): full resource entry (itemId `process-dynamic-case`, `title`, catalog `description`, `topicId: creatio.process-dynamic-case`, `role: guidance`, `sourcePath`, `bundlePath`, uri, `mediaType: text/plain`; plan KB-2), `requirements.itemIds` + `requirements.resourceUris` (`docs://knowledge/com.creatio.clio/process-dynamic-case`), `GuidanceMigrationTests` with `process-dynamic-case` APPENDED to `PostMigrationGuidance` [traced KB/automation/Clio.Knowledge.Bundle.Tests/GuidanceMigrationTests.cs:25-45,228-245], `ProcessGuideCrossReferenceTests`, `GenerationSequenceTests` after the `libraryVersion` bump (sequence derived, `_verdicts.md` conflict 5) | the article is reachable and the generation is new |
| TC-K-12 | `Guide_ShouldCarryNoInternalReference` | no ticket key, spec path or placeholder floor |
| TC-K-13 | `PagesCreation_ShouldSayTheProgressBarTemplateNeedsACase` | `guidance/mcp/guides/pages/creation.md` says the `PageWithTabsAndProgressBarTemplate` bar renders only when the object has an enabled case and points to `name=process-dynamic-case` (plan KB-9); size gates still green |

Run: `dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj`.

**Fact-check gate (not a unit test).** Before the KB PR leaves draft, every factual claim of the article and of the
edited sentences is checked by independent agents told to REFUTE it, per cluster: (1) contract, refusals, undo;
(2) order rule and stage shape; (3) run time: no compile, start on insert / on change, reload, .NET 8 wording. They
get `PB/`, `CORE/`, `UI/`, `PS/` and the M results as dated measurements, and may not cite this folder, the research
folder or `CLIO/docs/knowledge/`. Verdict table into the KB PR body.

**Agent runs (not unit tests).** After the cut is installed on a disposable stand and the article is published to a
local library, headless agent sessions run from a clean context with only shipped guidance; transcripts are kept as
evidence in the clio PR.

| Id | Task given to the agent | Pass |
|---|---|---|
| AR-1 | "Orders in the DCM Orders app move through their Status: New, In progress, On hold, Done. Show those stages on the order page so users can move an order between them." | one `create-dynamic-case` call (optionally a `dry-run` first) with `stages` in the business order; the agent relays N1 (reload) and does not compile or restart |
| AR-2 | "Show the stages of the Priority of DCM orders on the order page." (`Priority` lookup is empty) | the agent relays `lookup-empty`, invents no values and asks the user what the stages are |

---

## 7. CAADT scenario

### 7.1 What it is

CAADT = Creatio AI App Development Toolkit; a CAADT scenario is a business-level prompt that an agent executes
end-to-end against a stand with clio MCP, followed by a Playwright spec that checks the result in the browser
(r7 §7 [traced META/README.md:3-20; CLIO/clio/Common/Skills/ToolkitDistribution.cs:4]). It lives in two GHE
repositories, none of them clio:

| Piece | Repository | Path |
|---|---|---|
| prompt | internal `creatio-adaclio-testing` repository | `prompts/bpms-tools/<name>.yaml` (fields `name`, `description`, `prompt`, `expected`, `playwright: {script, path}`, `cleanup: {display_names, code_prefixes}`; placeholders `{app_code}`, `{creatio_url}`, `{environment_name}`, `{creatio_login}`, `{creatio_password}`) [traced r7 §7] |
| schedule (optional) | same | `prompts/bpms-tools/scheduler-matrix.yaml` entry (`agent`, `model`, `reasoning_effort`, `prompt`, `no_skill_invocation`, `clio_mcp_only`, `caadt_soft_timeout`; NO stand or runtime field) [traced g5 §5, main 7b6ea343] |
| verification spec | internal `creatio-playwright-tests` repository | `tests/_generated/features/caadt/specs/<name>.spec.ts`, script `tests-creatio-team-bpms-tools-clio-mcp`, tags `@tm_bpms-tools`, `@clio-mcp`, `APP_CODE` from env [traced r7 §7] |

Prompt rules: business words only, no tool names, recipes or workarounds; a failure on a clean prompt is a product
gap (r7 §7, `docs/caadt-automation-guide.md:99-110`). Current `prompts/bpms-tools/` on main: activity-connected-to,
client-batch-chain, contact-review-chain, phone-number-behaviors, request-handling-tasks (g5 §5, supersedes the
r7 list). No existing prompt covers a case or a progress bar (r7 §7 grep, on the older local copy - re-check on main
before adding).

### 7.2 Adding the scenario (sketch; written in those repositories, not here)

```yaml
# prompts/bpms-tools/order-stages-progress-bar.yaml
name: order-stages-progress-bar
description: A status lookup drives an order's lifecycle; the order page shows the stages and lets users move between them.
prompt: |
  In the Creatio environment {environment_name} ({creatio_url}) create an application with code {app_code} for
  tracking orders. An order has a name and a Status chosen from: New, In progress, On hold, Done (in that business
  order). Orders move through these statuses as stages. Show the stages at the top of the order page so users can see
  the current one and move an order to another stage by clicking it.
expected: |
  The app exists; its order object has a Status lookup with the four values; a case with four stages in the business
  order exists for the object; the order page shows the stage bar; clicking a stage changes Status.
playwright:
  script: tests-creatio-team-bpms-tools-clio-mcp
  path: tests/_generated/features/caadt/specs/order-stages-progress-bar.spec.ts
cleanup:
  display_names: [ ... ]
  code_prefixes: [ "{app_code}" ]
```

| CA | Spec step (Playwright) | Expected |
|---|---|---|
| CA-1 | open the app's list, create an order "CA order 1" with Status "New", save | record saved |
| CA-2 | reload the order page | the bar shows New, In progress, On hold, Done in that order; New is current |
| CA-3 | click "On hold" (non-adjacent); reload | Status field shows "On hold" (bar -> lookup; the prompt asks for a save on click, so the agent passes `save-on-change: true` and the click persists at once, v r9-C6; with the default the spec would have to press Save) |
| CA-4 | set Status to "In progress" in the field, save, reload | the bar marks "In progress" (lookup -> bar, v r3-C7) |
| CA-5 | open `<root>[/0]/Nui/ViewModule.aspx?vm=DcmDesigner#case/<uid>` (UId read from `SysSchema` by the spec, or from the agent transcript) | no error box, no "DcmSettingsId not found.", four stages listed with the lookup captions (v r2-C4, v r2-C5, v r9-C8) |
| CA-6 | the same prompt + spec run locally against the .NET 8 stand (`local-config.yaml` `environment.isNetCore: true`) | CA-1..CA-5 and CA-7 hold (Q24) |
| CA-7 | the 8.x route: open the order record page in the Freedom UI page designer, select the bar, click "Set up cases" (`?vm=SectionCasesWizard#Cases/...`, facts table rows "Set up cases" and `UseDcmForGeneralObject`) | the wizard lists the case as active with the stage column "Status"; opening it shows the four stages with no error; the spec records whether `UseDcmForGeneralObject` was on (read in V-1; traps UIX-1, UIX-4) |

The scheduled job deploys `ProductName=Studio`, `IsNetCore=false`; a .NET 8 CAADT run is therefore a LOCAL run (or a
hand-parameterised job run with ProductName, BuildPath AND `IsNetCore=true` set together, never done) [traced g5 §5].
The spec owns cleanup through `cleanup.code_prefixes`; whether app uninstall removes the bound `SysDcmSettings` rows
was not traced (g5 §7), so the spec also deletes the case with the design §9 undo order when it runs on a reused stand.

---

## 8. Stand measurements (design §14.5) and where they feed

Every row needs a disposable read-write stand (plan D0.6), one write at a time, the owner's yes per write. Stand
`Creatio` only on the owner's explicit word (plan D0.6).

| M | Question | Feeds |
|---|---|---|
| M1 | a route-A case opens in the designer with the expected stages and no error, by URL AND through the 8.x route (page designer -> bar -> "Set up cases" -> `SectionCasesWizard`), on both runtimes, with the `UseDcmForGeneralObject` value recorded; a designer re-save keeps one binding per row | TC-02, CA-5, CA-7, TC-I-03; Q15 |
| M2 | `DcmSchemaManagerRequest` right after the call: `IsActive && IsActiveVersion` | TC-I-04 |
| M3 | bar renders after F5; click persists; field change moves the bar | TC-03..TC-05, CA-2..CA-4 |
| M4 | non-admin with `CanManageDcm` + `CanManageSolution` + settings-table append: the gate matches what the saves enforce | TC-13, TC-U-110..114; Q5 |
| M5 | what `RemoveItemByUId` leaves after a DCM save (`SysEntityPrcStartEvent`, flow schema) | TC-U-97 wording of `failed-leftover`; TC-I cleanup |
| M6 | `IPackageSchemaDataCreator` from a CrtProcessBuilder WCF call binds into the requested package under the caller | TC-I-02 bindings; TC-U-53, 55 |
| M7 | new record starts a case; existing record starts on stage change; after the final stage a qualifying update starts a new case | TC-06, TC-I-12, N3 |
| M8 | M1..M7 on a .NET 8 stand | TC-12, CA-6; Q24 "done" |
| M9 | `AutoAddPackageDependenciesInProcesses` value on the stands | TC-I-13 preconditions; g3 §1 |
| M10 | does adding a package dependency mark anything for compile on either runtime | TC-U-99 (dependency artifact), TC-K-03 wording |

A row that depends on an M not yet run is written only after it ran; no placeholder assertion is committed.

---

## 9. Manual Jira test cases

Drafted for the Jira issue's test-case section; the owner pastes them by hand.

**Test cases**

**Conditions:** Two disposable Creatio environments with Freedom UI: one on .NET Framework and one on .NET 8, each
with the CrtProcessBuilder version this issue ships and a clio build with this issue's change connected to an AI
agent as an MCP server. The tester is a system administrator unless a case says otherwise. Test data, created once per
environment before the cases with the usual app tools: an application "DCM Orders" (code `UsrDcmOrders`) whose object
"DCM order" (`UsrDcmOrder`) has a text column "Note" (`UsrNote`), a lookup column "Status" (`UsrStatus`) pointing to
the lookup "DCM order status" (`UsrDcmOrderStatus`) with an integer column "Position" and the values "New" (1),
"In progress" (2), "On hold" (3), "Done" (4), and a lookup column "Priority" (`UsrPriority`) pointing to the EMPTY
lookup "DCM order priority" (`UsrDcmOrderPriority`); a second lookup "DCM order level" (`UsrDcmOrderLevel`) with one
value "Only" and a lookup column "Level" (`UsrLevel`) on the object; the app's order record page uses the tabbed
Freedom UI template (`PageWithTabsFreedomTemplate`). Create two orders BEFORE TC-01: "Existing 1" with Status
"In progress" and "Existing 2" with Status empty. Each case is given to the agent as a business task in plain words.
The case designer is opened from the link the agent returns, or `<site>/0/Nui/ViewModule.aspx?vm=DcmDesigner#case/<id>`
on .NET Framework and the same without `0/` on .NET 8. Reload (F5) every open record page after a case is created:
open pages keep the old case list until reloaded. To undo a case between cases: in the package "DCM Orders" remove the
data bindings `SysDcmSchemaInSettings_*` and `SysDcmSettings_*` that the case created (with their records), then delete
the case schema, then the order page replacement if the case created one. TC-01..TC-10 and TC-13 run on the
.NET Framework environment (TC-11 records that); TC-12 repeats the core cases on .NET 8.

### `TC-01` Create a case whose stages come from the order status

**Preconditions:**
No case exists for "DCM order".

**Steps:**
1. Give the agent the task: "Orders in DCM Orders move through their Status. Make the statuses the stages of the order and show the stages on the order page."
2. Read the agent's answer.
3. Open Configuration, package "DCM Orders".

**Expected result:**
* The agent reports one case with four stages "New", "In progress", "On hold", "Done", in that order, and says the order came from the "Position" column of the lookup.
* The agent says no compilation and no restart are needed and asks to reload open order pages.
* Package "DCM Orders" contains the new case schema and two data bindings, one for the case settings and one for the case link.
* The agent ran no compilation and no restart.

### `TC-02` The case opens in the case designer with the expected stages

**Preconditions:**
TC-01 passed.

**Steps:**
1. Open the case designer link from the agent's answer.
2. Look at the case properties and the stage list.
3. Open each stage's properties.
4. Save the case in the designer without changes.
5. Open the order record page in the Freedom UI page designer, select the stage bar and click "Set up cases".

**Expected result:**
* The designer opens with no error message (in particular no "DcmSettingsId not found.").
* The case is bound to the object "DCM order" and the stage column "Status".
* Four stages are shown in the order New, In progress, On hold, Done; each stage is linked to the lookup value of the same name; no stage is marked invalid.
* Every stage has no steps.
* The save succeeds; package "DCM Orders" still has exactly one case-settings binding and one case-link binding.
* "Set up cases" opens the cases wizard; it lists the case as active with the stage column "Status", and opening it shows the four stages with no error (note whether the `UseDcmForGeneralObject` feature was on). Run on both runtimes.

### `TC-03` The order page shows the stage bar

**Preconditions:**
TC-01 passed. Order "Existing 1" (Status "In progress") exists.

**Steps:**
1. Reload the browser tab.
2. Open the order "Existing 1".

**Expected result:**
* A stage bar is shown at the top of the page, above the page content.
* The bar shows New, In progress, On hold, Done in that order, each with the lookup value's caption.
* "In progress" is marked as the current stage.

### `TC-04` Clicking a stage changes the order's Status

**Preconditions:**
TC-03 passed; order "Existing 1" is open.

**Steps:**
1. Click "Done" on the bar (two stages ahead).
2. Look at the "Status" field without saving.
3. Reload the page.
4. Click "New" on the bar (back to the first stage) and reload the page.

**Expected result:**
* After step 1 the bar marks "Done" and the "Status" field shows "Done".
* After step 3 the page still shows "Done" in the bar and in the field: the click was saved at once.
* After step 4 the bar and the field show "New": moving back and skipping stages are allowed.

### `TC-05` Changing the Status field moves the bar

**Preconditions:**
TC-03 passed; order "Existing 1" is open.

**Steps:**
1. Set "Status" to "On hold" in the field.
2. Look at the bar before saving.
3. Save the record and reload the page.
4. Change "Status" of "Existing 1" to "In progress" from the order list page (or another way that does not use the record page), then open the record.

**Expected result:**
* After step 1 the bar marks "On hold" without saving.
* After step 3 the bar and the field show "On hold".
* After step 4 the bar shows "In progress".

### `TC-06` Existing and new records

**Preconditions:**
TC-01 passed; orders "Existing 1" and "Existing 2" were created before the case.

**Steps:**
1. Open "Existing 2" (Status empty) and look at the bar.
2. Set its Status to "New" and save.
3. Open the process log and look for the case run of "Existing 2".
4. Create a new order "New 1" with Status "In progress" and save.
5. Open the process log and look for the case run of "New 1".

**Expected result:**
* Step 1: the bar shows the four stages with no current stage.
* Step 3: a running case for "Existing 2" exists after the Status change (existing records get a case on their next change of the stage column).
* Step 5: a running case for "New 1" exists right after it was created.

### `TC-07` Preview and choose the stage order

**Preconditions:**
No case exists for "DCM order".

**Steps:**
1. Give the agent the task: "Before changing anything, show me which stages the DCM order would get from its Status and in which order."
2. Check the environment.
3. Give the agent the task: "Create them in this order instead: New, On hold, In progress, Done, and show them on the order page."
4. Open the case designer and an order record (after a reload).

**Expected result:**
* Step 1: the agent shows the four stages and their order source and says nothing was created.
* Step 2: no case, no case data binding and no page change exist yet.
* Step 4: the designer and the bar show New, On hold, In progress, Done.

### `TC-08` Refuse an object column that is not a lookup

**Preconditions:**
No case exists for "DCM order".

**Steps:**
1. Give the agent the task: "Make the Note of DCM orders the stages of the order and show them on the order page."

**Expected result:**
* The agent reports that "Note" is not a lookup column and that a case needs a lookup column whose values become the stages.
* No case, case data binding or order page change was created.

### `TC-09` Refuse a second case; repeat the same request safely

**Preconditions:**
TC-01 passed (the case exists and is active).

**Steps:**
1. Give the agent the task of TC-01 again.
2. Give the agent the task: "Make the statuses of DCM orders the stages, in the order Done, In progress, New, On hold."

**Expected result:**
* Step 1: the agent reports that the same case and the bar already exist; nothing new is created (still one case, two data bindings, one bar on the page).
* Step 2: the agent reports that "DCM order" already has an active case on "Status" and that changing an existing case is not supported yet; nothing is changed.

### `TC-10` Refuse an empty or one-value lookup

**Preconditions:**
No case exists for "DCM order".

**Steps:**
1. Give the agent the task: "Make the Priority of DCM orders the stages of the order."
2. Give the agent the task: "Make the Level of DCM orders the stages of the order."

**Expected result:**
* Step 1: the agent reports that the "DCM order priority" lookup has no values and asks which stages are wanted; nothing is created.
* Step 2: the agent reports that one value is not enough for a case (at least two stages are needed); nothing is created.

### `TC-11` .NET Framework run

**Preconditions:**
The .NET Framework environment.

**Steps:**
1. Run TC-01 to TC-10 and TC-13 on it.

**Expected result:**
* All pass; the run record names the environment's runtime (.NET Framework) and the CrtProcessBuilder version.

### `TC-12` .NET 8 run

**Preconditions:**
The .NET 8 environment with the same test data.

**Steps:**
1. Run TC-01 to TC-06 and TC-09 on it.
2. In TC-02 open the designer link the agent returns.

**Expected result:**
* All pass with the same results as on .NET Framework.
* The designer link has no `0/` segment and opens the case.
* No compilation and no restart are requested or needed.

### `TC-13` A user without the case design right is refused

**Preconditions:**
No case exists for "DCM order". A second user with access to the app and to the "DCM Orders" package but without the operation permission `CanManageDcm` (granted by default to System administrators only, v r6-C3); the agent is connected as that user.

**Steps:**
1. Give the agent the task of TC-01.

**Expected result:**
* The agent reports that the user lacks the right to manage cases, naming it.
* No case, case data binding or order page change was created.

---

## 10. Regression scope

| Area | Why | Run |
|---|---|---|
| CrtProcessBuilder whole test project, `dev-nf` and `dev-n8` | `IPackageCatalog` split of `IProcessSchemaRepository` (TC-U-104), guard extension, composition root | section 2.1 |
| clio full unit suite | `Common` changed (route, pins) | section 4 |
| `CreateBusinessProcessToolE2ETests`, `DescribeProcessToolE2ETests` with the ProcessDesigner filter | the rebundle moves the floor for every gated process tool (v r7-C2) | section 5.3 command, other fixture names |
| page tool e2e that use `PageUpdateCommand` append | the tool resolves the command in its own container under the tenant lock | the existing page fixtures on the lane |
| clio-knowledge whole test project | routing, `process-modeling` edit, size gates | section 6 |

---

## 11. Conventions (all repositories)

- AAA comments, `because:` on every assertion, `[Description]` on every test; no Jira key in a test name,
  `[Description]`, prompt, spec or article.
- Package tests: C# 7.3; `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`.
- clio tests: `[Category("Unit")]` + `[Property("Module", ...)]` as the host fixture does; DI resolution
  (`Container.GetRequiredService<T>()`) for command tests; OS-neutral paths.
- e2e: sequential writes; cleanup in `finally`; a convergence refusal is Ignored, never green.
- Literals that are product facts (colour, `Filters`, binding name shapes, error codes) are written as literals.

---

## 12. Deliberately not covered, and unknowns

- Steps inside stages, conditions, alternative stages, modifying an existing case (out of scope, design §16): only
  the refusal seam (TC-U-81, 134; TC-C-05; TC-I-08) is tested.
- Mobile pages, mini pages beyond the refusal, Classic pages beyond the N7 warning.
- Web-farm propagation of a new case (g1 (d): traced only, no farm available).
- The CrtNUI kill switch `DisableEntityStageProgressBarPreprocessing` (off by default, not measured on the stands,
  v r3-C1): a troubleshooting line in the article, no test.
- Culture-aware display sort vs database collation edge cases (Q9): the reply always states the order; no test pins
  collation.
- Unknown until measured: M1..M10 (section 8); whether `seed-rows` can set an integer column (5.1); whether app
  uninstall removes bound `SysDcmSettings` rows (g5 §7); the exact process-log read for TC-I-12 / TC-06.
- Every probe row of sections 2-3 (TC-U-22, 23, 37, 42, 103; TC-P-01): decided by the first CI run of the draft
  package PR, fallbacks named in the rows.
