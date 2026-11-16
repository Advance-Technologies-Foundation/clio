# ENG-105556 AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page - reuse

Written 2026-11-08. Phase-1 research for Jira ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and
add a progress bar to the record page" (Task, epic ENG-92704 "Create BP via AI Toolkit"). This file is a reuse map,
not a design: it lists every existing piece the chosen design touches, says how it is used (call, extend, port,
pattern, avoid) and what new code is left over. The design is
[eng-105556-minimal-dcm-design.md](eng-105556-minimal-dcm-design.md); every choice with an alternative is an open
question in [eng-105556-minimal-dcm-decisions.md](eng-105556-minimal-dcm-decisions.md) (Q1..Q29, all "open - owner
decides"). This file states the recommended option of each Q and changes nothing about them.

Basis: CrtProcessBuilder `origin/main` `a3f58852` (1.6.6.140) at `<package repository>`; clio `origin/master`
`69788ccd5`; clio-knowledge `origin/master` `622134b` (libraryVersion 1.15.180); core TSBpm `a95349820`; creatio-ui
`7ca9210bd0e`; PackageStore `*/branches/7.8.0`; stand `Creatio` (.NET Framework, read-only, runs an unmerged
CrtProcessBuilder 1.6.6.142 build, v r10-C2). Research notes r1..r10, g1..g6 and `_verdicts.md` in
`<research-dir>` (a verdict or a g-note overrides the r-note it corrects).

Path aliases:

| Alias | Path |
|---|---|
| CORE | `<core>/Src/Lib/` |
| PB | `<package repository>/packages/CrtProcessBuilder/Files/src/cs/` |
| PBF | `<package repository>/packages/CrtProcessBuilder/Files/` |
| PBT | `<package repository>/tests/UnitTests/CrtProcessBuilder.Tests/` |
| CLIO | `<clio>/` |
| E2E | `CLIO/clio.mcp.e2e/` |
| KB | `<clio-knowledge>/` |
| PS | `<PackageStore>/<Package>/branches/7.8.0/` |
| DD / DL | `PS` of `DcmDesigner` / `DcmLibrary` |
| UI | `<creatio-ui>/` |
| AM | `UI/libs/studio-enterprise/feature/application-management/src/lib/` |
| UT | `<UnitTests>/` |

Evidence tags: `[traced path:line]` source read (a trace is a hypothesis about run time until a stand settles it);
`[traced, today]` re-read for this file; `[corpus N/M]`; `[measured]` stand read (query in the cited note); `[note]` the
owner's DCM overview; `[inferred]` reasoning. `(v r4-C2)` = row of `_verdicts.md`; `(r6 §11)`, `(g3)` = note section.
Measurement ids M1..M10 are those of design §14.5.

---

## 1. The reuse picture in one paragraph

Core already does everything a minimal DCM needs and exposes it publicly: the object model, `CreateSchema`,
`SaveSchema` (which also registers the start signal and generates the flow schema), the platform's own "which cases
would start" query, the package-data binder and every rights check (v r1-C1, v r1-C4, v r4-C7). Core is not changed
(Q1). What core does NOT do is the "designer" layer: one stage per lookup value, full-mesh connections, a colour the 8.x
bar can render, the two settings rows, their package bindings, and the pre-flight checks (v r1-C5, v r2-C13). In the
whole 7.8.0 corpus exactly one server-side piece does that layer: CrtGenAI `GeneratedDcmSaver` [corpus 1/1040
packages] (r4 §1). It is **ported** (about 55 lines, GeneratedDcmSaver.cs:105-182, six defects fixed), not called (section 4, Q1 option d). The
rest of the designer layer exists only as 7.x client JavaScript in DcmDesigner/DcmLibrary and is **ported as rules**
(section 5). CrtProcessBuilder contributes its transport, gate shape, request hygiene, package resolution, error
envelope and the save/rollback/cache-release seam (section 3); its element handlers, layout engine and the whole
script-task compile machinery do not apply to an element-less DCM (section 6). On the page side nothing new is
written: clio's related-page add-on reader, `PageGetCommand` (merged view tree in `Bundle.ViewConfig`), data-source validator
and `PageUpdateCommand` append path (dry run first, then the write) carry the app wizard's two-insert fragment, which is ported from creatio-ui as a constant (sections 7-8).
The genuinely new code is the case planner/composer/settings writer/verifier in the package and the tool, service and
record-page resolver in clio (section 10).

---

## 2. (A) Core APIs

All in assemblies CrtProcessBuilder already references (`Terrasoft.Core`, `Terrasoft.Core.Applications`,
`Terrasoft.Nui.ServiceModel`) [traced PBF/CrtProcessBuilder.csproj:167-168,262-263 per design A]; `DependsOn []` stays
(r6 §1). Internals are unreachable: `Terrasoft.Core` has no `InternalsVisibleTo` for this assembly [traced
CORE/Terrasoft.Core/Properties/FriendlyAssembliesInfo.cs:7-36] (r6 §3). No `#if` in `Terrasoft.Core/DcmProcess`
(v r1-C1), so every row holds on both runtimes by code; .NET 8 is not measured (Q24, M8).

Verdict key: **call** = used as is; **call via seam** = used behind a package interface so tests can drive it;
**mirror** = re-implement the check because the original is internal or lives in the wrong place; **avoid** = not used
now, reason given.

| API | Visibility | What for | Verdict | Evidence / note |
|---|---|---|---|---|
| `DcmSchemaManager.CreateSchema(name, DefSchema, uc, Guid.NewGuid(), false)` | public (inherited) | in-memory item + schema: stamps `Id`, `UId`, `ParentUId`, `CreatedInVersion`; `addToDesignItems=false` registers nothing; a null base = `DefSchema` | **call** (design S13, from `IDcmSchemaRepository.CreateItem`, never from the composer) | [traced, judge CORE/Terrasoft.Core/SchemaManager.cs:3335-3355,5506-5515]; same call the package makes for processes [traced PB/Schema/ProcessSchemaRepository.cs:65-68]. Does NOT set `CultureName` for a DCM (the process manager does), so the composer sets it (v r6-C1) |
| hand-built `new DcmSchemaManagerItem{Instance, PackageUId}; Assign; Id; Descriptor` (GeneratedDcmSaver shape) | public | alternative item construction | **avoid** | sets `Id == UId`, no `UserId`/`ParentUId` on the item [traced PS CrtGenAI/Schemas/GeneratedDcmSaver/GeneratedDcmSaver.cs:150-157] (r4 §9 item 3b); `CreateSchema` gives the designer's item (g2 §3 row B8) |
| `DcmSchema` / `DcmSchemaStage` / `DcmSchemaStageConnection(s)` object model | public ctors and setters | stages (`UId`, `Name`, `Caption`, `StageRecordId`, `Color`), full mesh, `EntitySchemaUId`, `StageColumnUId`, `Enabled` | **call** | ctor sets `Filters = ""`, `Enabled = true` [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:115-119]; class map r1 §1. `IsDefault` is not persisted, so it is not set (v r4-C3) |
| `SchemaManager.SaveSchema(item, uc, true, true)` | public, non-virtual | persist + `ActualizeSchemaInfo` (start-event row, flow schema, `InvalidateRemote`) | **call via seam** (`IDcmSchemaRepository` / `IDcmSchemaSaver`, Q6) | v r1-C1, v r1-C2 (side effects are NOT rolled back with the save; Q7 puts it LAST); forwards to protected virtual `InternalSaveSchema`, which runs `CheckOperationUserRights` (General + `CanManageDcm`), `CheckIsPackageForeign`, name-prefix check [traced CORE/Terrasoft.Core/SchemaManager.cs:3214-3221,5589-5595] (v r8-C2) |
| `DcmSchemaManager.GetProcessValidationResult(schema, uc)` | public (inherited) | pre-save gate; runs flow generation as a smoke test | **call**, fail closed | [traced, judge CORE/Terrasoft.Core/Process/BaseProcessSchemaManager.cs:1373-1378]; for a DCM only `ParameterValuesValidationRule` [traced, judge CORE/Terrasoft.Core/DcmProcess/DcmInterpretationValidator.cs:27-31]; weak by nature, the tool's own checks carry the semantics (v r1-C5) |
| `DcmSchemaManager.RemoveItemByUId(uid, uc)` | public (inherited) | compensation after a failed save / failed verify | **call via seam** | AutoTest deletes DCMs this way [traced PS AutoTest/Schemas/ProcessTestService/ProcessTestService.cs:141-150] (r4 §4.3); what it leaves behind (start-event row, flow schema) is unmeasured - M5 |
| `DcmEntityUtilities.FindEnabledDcmSchemas(entityUId)` | public sealed class, public ctor, public method | "is there an enabled, active-version case for this object" = the exact set the runtime starts | **call** (S7 existing case, V2 competing case; Q12) | [traced, judge CORE/Terrasoft.Core/DcmProcess/DcmEntityUtilities.cs:19,57,434-438,467-476]; its interface `IDcmEntityUtilities` is internal [traced, judge IDcmEntityUtilities.cs:14], so the package news up the class inside a DI-registered `IEnabledCaseReader` adapter (the sealed class cannot be substituted; handler and verifier tests substitute the adapter, the adapter is covered by E2E). Replaces designs B and C's own scan |
| `GetIsActiveVersion(Guid schemaId)` | public | read-back V1 | **call** | [traced, judge CORE/Terrasoft.Core/Process/BaseProcessSchemaManager.cs:1522]. The `(ISchemaManagerItem)` overload is **internal** [traced, judge :1262] - designs B and C relied on it |
| `IBaseProcessSchemaManagerItem.GetIsEnabled()` / `SchemaManagerItem.GetPropertyValue(s => s.Enabled)` on the root item | public | read-back V1 | **call** | [traced, judge CORE/Terrasoft.Core/Process/IBaseProcessSchemaManagerItem.cs:41]; [traced CORE/Terrasoft.Core/SchemaManagerItem.cs:760] (g1 "What the tool must do" 3) |
| `DcmSchemaManager.SetEnabled` / `GetIsEnabled(Guid)`; `DcmSchemaDesigner.SetEnabled` | internal virtual / public | activation | **avoid** | a new root DCM saved with `Enabled = true` is already active and the active version; calling either is at best a no-op (g1 (a)-(c)) |
| `DcmSchemaDesigner.Save(ContractDcmSchema, response)` (route B) | public | designer save incl. `ReinitializeSystemElements`, dependency auto-add, `TrackSchemaSaved` | **avoid now**, kept as the upgrade path behind `IDcmSchemaSaver` (Q6, Q27) | needs a readable-to-design JSON transform; persists the same thing as route A for an element-less DCM (g2 verdict items 1, 3, 5) |
| `DcmSchema.ReinitializeSystemElements` | internal | rebuild change-stage tasks for step conditions | **avoid** | `SystemElements` is `[]` for element-less stages in both routes; a later task either uses route B or rebuilds with public API (v r8-C12) |
| `IPackageSchemaDataCreator.CreatePackageSchemaData(name, packageUId, ids, schemaName)` via `ClassFactory.Get<>` | public interface, internal impl | bind `SysDcmSettings` + `SysDcmSchemaInSettings` rows into P (W3) | **call** + read-back of `SysPackageSchemaData` | ClassFactory-bound on both hosts [traced CORE Terrasoft.WebApp/Global.asax.cs:748; Terrasoft.WebHost/ApplicationBindings.cs:295] (r4 §4.2). Same name in the same package REPLACES data; returns silently only when the record is already bound in the same package, other failures throw (v r4-C7). Under which user it binds from a package WCF call: M6 |
| `PackageElementUtilities.DeletePackageSchemaData(name, packageUId)` | public | compensation of W3 | **call** | [traced, judge CORE/Terrasoft.Core/Packages/PackageElementUtilities.cs:1685-1697] |
| `DBSecurityEngine.GetCanExecuteOperation("CanManageDcm")`, `GetCanModifyPackageElement()`, `GetEntitySchemaOperationsRightLevel` | public | up-front gate S1 (Q5) | **mirror** the platform checks up front | `DcmSchemaManager.CheckOperationUserRights` [traced, judge CORE/Terrasoft.Core/DcmProcess/DcmSchemaManager.cs:132-139]; binder check [traced, judge PackageElementUtilities.cs:1820-1825; DBSecurityEngine.cs:5344-5347]; settings-table rights [traced, judge DBSecurityEngine.cs:3707; PS CrtBase/Data/SysEntitySchemaOperationRight_SysDcmSettings/data.json]. Without the mirror a missing right fails INSIDE the save with an unfilled `{0}` message (v r6-C3) |
| `WorkspaceUtilities.GetPackageIdByUId` + `GetPackageHierarchyIdsById`; `EntitySchemaColumn.CreatedInSchemaUId` | public | dependency plan S12 (Q16) | **call** | g3 "What the tool should do" 1; `FindItemWhereColumnIsDefined` is internal [traced EntitySchemaColumn.cs:824; EntitySchemaManager.cs:1487 per g3] |
| `WorkspaceUtilities.GetIsPackageForeignByUId` | public | read-only package refusal | **call** (through the package's existing `IsPackageReadOnly`) | the predicate `InternalSaveSchema` runs, asked up front to name the package (r6 §2 step 5) |
| `EntitySchemaManager` runtime instance (`GetInstanceByName`) | public | object + column + reference schema | **call** (through `EntitySchemaResolver`) | never `GetDesignInstance` (GeneratedDcmSaver defect 2, r4 §7) |
| `DcmSchema.WriteMetaData(DataWriter)` | public | parity test of the composed DCM | **call** (tests only) | [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:659-676] (r8 §3.10) |
| CrtNUI `DcmSchemaManagerCacheResetMonitor` (client cache hash) | configuration, ClassFactory monitor | client cache reset after any server `SaveSchema` | **rely on, do not duplicate** | [traced PS CrtNUI/Schemas/DcmSchemaManagerCacheResetMonitor/DcmSchemaManagerCacheResetMonitor.cs:10-48] (r4 §6); asynchronous; the SPA still memoizes the DCM list per tab -> note N1 (v r3-C1) |
| core `GeneratedDcm` DTO, `IGenAIApplicationContentGenerator` | public | the AI app route | **avoid** | the only core route to the saver also saves entities and pages (v r4-C2); DTO shaped for invented stages [traced CORE/Terrasoft.Core/Applications/Abstractions/Creation/GeneratedDcm.cs:12-33] |
| `BaseProcessSchema.UseSystemSecurityContext` (IJ10) | public | designer-current parity | optional, default omitted | affects only script-task rights, none in an element-less DCM; 21/61 shipped DCMs lack it [corpus 40/61 set] (g2 §3 row IJ10) |

---

## 3. (B) the package repository components

Verdict key: **as-is** no change; **extend** an edit to an existing file; **pattern-only** the shape is copied into new
code, the file is not touched; **avoid**; **later** reused by the steps task (Q27), not now.

### 3.1 Production code

| Path | What it does | Verdict | How it is used / why |
|---|---|---|---|
| `PB/EntryPoints/WebService/ProcessDesignService.cs` | 8 `[OperationContract]` ops, per-request scope [traced, judge :27-46] | **extend** (Q2 a) | ninth op `BuildCase`, same `[WebInvoke(POST, Json, Wrapped)]`; the service doc comment names both gates. Sibling `DcmDesignService` (Q2 b) would escape the package mirror test (v r7-C3) |
| `PB/Design/ProcessDesigner.cs` (facade, `Execute<TResponse>` :164) | thin transport -> handler | **extend** | `BuildCase(request)` -> `CaseBuildHandler` (r6 §1) |
| `PBF/src/CrtProcessBuilderApp.cs` | MS.DI root, `ScopedUserConnection` [traced :84-94,552] | **extend** | scoped registrations of the new handler, planner, reader, composer, repository, settings writer, dependency planner, verifier; composition tripwires must learn them (r6 §12) |
| `PB/Design/ProcessDesignGuard.cs:37-67` | `EnsureCanManageProcessDesign` (General + `CanManageProcessDesign`) | **extend** | add `EnsureCanManageDcm` (General + `CanManageDcm` + `CanManageSolution` + append on both settings tables, Q5). `CanManageProcessDesign` is NOT required (design §10) |
| `PB/RequestKeys/UnknownRequestKeyDetector.cs:35`, `PB/Contracts/ExtensibleRequestContract.cs` | refuse undeclared JSON keys | **as-is** | `BuildCaseRequest` and `CaseStageRequest` derive from `ExtensibleRequestContract`; a `steps` key maps to `steps-not-supported` (design §3.1) |
| `PB/SafeText.cs:79,243` | caller-safe message, log sanitising | **as-is** | error envelope (r6 §8) |
| `PB/Design/ProcessDesignNotices.cs` (`IProcessDesignNotices`) | request-scoped dedup warnings | **as-is** | notes N1-N8 (design §3.4) |
| `PB/ProcessDesignConstants.cs:467` `MaxRequestItems` | request bounds | **pattern-only** | `MaxCaseStages = 20` constant (Q11) |
| `PB/Schema/IProcessSchemaRepository.cs:20,92,125,131` package members (`ResolvePackageUId`, `ResolvePackageName`, `IsPackageReadOnly`, `GetSchemaNamePrefix`) | package by name, foreign check, prefix | **extend** (interface split) | split into `IPackageCatalog`, implemented by `ProcessSchemaRepository` too; no behaviour change. The DCM handler then does not depend on a process repository [traced, judge] |
| `PB/Schema/ProcessSchemaRepository.cs:65-68,184,189,329-345,350-381` | `CreateSchema`, `SaveItem` protected virtual, rollback via `RemoveItem`, `ReleaseResourceCache(uid)` in `finally` | **pattern-only** | `DcmSchemaRepository` with the same seams over `DcmSchemaManager` (Q6). The repository is typed to `ProcessSchema` (v r6-C1). Cache release matters little for a NEW schema (no pre-edit cache, r6 §3) but costs nothing and is needed by a later modify |
| `PB/Schema/ProcessSchemaRepository.cs:71-79` `ProcessExists` | case-insensitive name scan of `GetItems()` | **pattern-only** | name check S11 over `DcmSchemaManager` (the object-model save does not check uniqueness, v r1-C5; Q26) |
| `PB/Schema/ProcessSchemaRepository.cs:153-177`, `IProcessVersionCloneFactory` | metadata round-trip (`GetMetaDataSerializer` + `ReadMetaData`) | **later** | the public route to wrap a step into `DcmSchemaElement` (v r6-C7; Q27) |
| `PB/EntitySchemaResolver.cs:26-41` `ResolveByName` | runtime entity schema by name | **as-is** | S5 (fixes GeneratedDcmSaver defect 2) |
| `PB/Graph/ActivityResultResolver.cs:130-160` | lookup captions by ESQ in the profile culture | **pattern-only** | `StageValueReader` reads the lookup rows with ESQ under the caller's rights (Q9 a); a raw `Select` returns base-culture text (r6 §5) |
| `ProcessFilterService.ResolveLookupValue`, `ProcessParameterValueValidator.RecordOfObjectExists/FindRecordDisplayValue` | single-value lookup resolution | **pattern-only** (ideas) | no existing "all rows of a lookup with order columns" reader (r6 §14) |
| `PB/Validation/ProcessSchemaValidator.cs` (`EnsureValidForSave`, static `DescribeErrors` / `PlatformValidationMessage.Explain`) | pre-save validation + bounded message | **as-is** (formatter) / **pattern-only** (validator, typed on `ProcessSchema`) | S14 calls `DcmSchemaManager.GetProcessValidationResult` and formats with the existing static (r6 §7) |
| `PB/Design/ProcessBuildHandler.cs:115-127,230-470` | gate-first refusal; three failure outcomes + leftover probe | **pattern-only** | `CaseBuildHandler` keeps the wording discipline: refused before any write / `failed-rolled-back` / `failed-leftover` (design §5) |
| `PB/Design/ProcessBuildHandler.cs:476-487` `CreateSchemaDraft` | `PackageUId` on schema AND item, `Caption`, `InitializeLocalizableValues` | **pattern-only** | `DcmSchemaComposer` (S13); `IsInterpretable`/`SerializeToDB` are forced true by `DcmSchema` [traced, today CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:205-217] |
| `PB/Describe/ProcessDescriber.cs` (`ReadBackKeys`, read-back keys tolerated on write) | describe output is re-sendable | **pattern-only** | the describe-shaped `case` block of the result (design §3.3); the class itself is `ProcessSchema`-typed (r6 §9) |
| `PB/Scripts/ScriptTaskNotices.cs`, `PB/Design/ProcessCompileHandler.cs`, `PB/Compilation/ConfigurationCompiler.cs` | compile demand and compile | **avoid** (marker discipline only) | section 6 |
| `PB/Layout/` (14-phase layout engine) | BPMN canvas geometry | **avoid** | a DCM has no canvas; GeneratedDcmSaver sets no geometry (r6 §10) |
| `PB/Graph/IProcessDependencySnapshot.cs` | retarget guards on modify | **avoid** now / **later** | a later "change stage column / remove a stage a step references" (r6 §10) |
| `PB/Elements/*` handlers (`UserTaskElementHandler`, `ApprovalElementHandler`, `OpenEditPageElementHandler`, `SendEmailElementHandler`, `ChangeAccessRightsApplier`, `SubProcessElementHandler`/`SubProcessApplier`) | element builders | **later** | they cover the whole DCM palette [traced PB/ProcessDesignConstants.cs:491-556] (r6 §13.1), but are typed on `ProcessSchema` (712 occurrences, 166 files, v r6-C5) and inner step content differs (v r6-C6). Not generalised now (design §12) |
| `PB/Catalog/UserTaskCatalog.cs` | raw `Select` over `SysProcessUserTask` | **later** | a DCM palette reader is the same code over `SysDcmUserTask` (`SchemaUId`, `Position`) (r6 §13.1) |

### 3.2 Package tests and documents (reused structure)

| Path | Verdict | Use |
|---|---|---|
| `PBT/BaseComposableAppTestFixture.cs:24,450-456,496-508,510-515` (`MockEntitySchemaWithColumns`, `SetUpTestData`, `SubstituteEmptyEntity`, `MockLczValues`) | **as-is** | `StageValueReaderTests`, `CaseBuildHandlerTests`, `DcmSettingsWriterTests` (r8 §3.1) |
| `PBT/.../ProcessDesignTestSupport.cs:236-250` (`CreateUserConnection`) | **as-is** | standalone connection with a substituted `DBSecurityEngine` |
| `PBT/.../ProcessSchemaRepositoryTests.cs` (`RecordingSaveRepository`, `FailingRemovalRepository`), `ProcessSchemaRepositoryDbTests.cs:15-36,91-95` | **pattern-only** | `DcmSchemaRepositoryTests` seams; assert bound parameter values, not SQL text ("the mocked engine renders no SQL text") |
| `PBT/.../ActivityResultResolverTests.cs:42-45,416-470` | **pattern-only** | ESQ read stubbing and its known limit (`Has(x)` pins values, not the filter column) |
| `PBT/.../CrtProcessBuilderAppTests` (`CompositionRoot_RegistersEvery...`), `ProcessDesignServiceTests.cs:35-52` (`InjectedServices`) | **extend** | tripwires and delegation for `BuildCase` (v r6-C4) |
| `PBT/EntryPoints/WebService/ProcessDesignServiceWireContractTests.cs:50-56,131-135,183-192` | **extend** | mirror count 8 -> 9 (it reflects only `ProcessDesignService`, v r7-C3) |
| `PBT/.../FileProcessParameterTests.cs:539-547,711-735` (`JsonDataWriter` capture vs fixture) | **pattern-only** | `DcmMetadataParityTests` against the golden `KnowledgeArticlePublishing` shape [traced PS CrtKnowledgeManagementVersions/Schemas/KnowledgeArticlePublishing/metadata.json:14-96 per v r5-C10] |
| `PBT/.../CiContractGuardTests.cs:13,23,28,63-64` + `ci.runsettings` | **as-is** (constraint) | every new fixture carries `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` |
| `PBT/CrtProcessBuilder.Tests.csproj:10-15,56` (`-c dev-n8`) | **as-is** | the suite builds and runs net8.0 locally; CI is net472 only (v r8-C6 refutes "net472 only") |
| `pb105556/docs/process-builder-architecture.md` §2-4 | **extend** | one row for the case operation (r6 §1) |

---

## 4. (C) CrtGenAI `GeneratedDcmSaver`: call or port

File: `PS CrtGenAI/Schemas/GeneratedDcmSaver/GeneratedDcmSaver.cs` (202 lines, `Terrasoft.Configuration.GenAI`,
`[DefaultBinding(typeof(IGeneratedDcmSaver))]`) [traced, today: 202 lines] (r4 §2).

**Verdict: port, do not call** (Q1 d rejected by all three designs). Why, with the reason the verdict corrected
(v r4-C2):
- The barrier is NOT "an assembly package cannot reference configuration": 35/86 assembly packages do, when they
  depend on a configuration package [corpus 35/86] (v r4-C2). The real barriers are CrtProcessBuilder's
  `"DependsOn": []` plus the repository rule "no `Terrasoft.Configuration`" [traced pb105556 descriptor.json;
  ProcessDesignConstants.cs:1944 per v r4-C2], and the fact that the only core route to the saver
  (`IGenAIApplicationContentGenerator`) also saves entities and pages.
- The contract misfits: it invents NEW stage Guids and NEW lookup rows from AI captions, has no existing-settings
  check, and leaves the binding to its caller under bare names (r4 §2.2, §2.6; v r9-C3: not a precedent for
  "one stage per EXISTING value").
- It is gated only through its caller's `GenAIFeatures.GenerateDcm` (r4 §8); a dependency would also inherit that
  coupling [inferred].
- The useful part is about 55 lines (GeneratedDcmSaver.cs:105-182) over public core types that CrtProcessBuilder
  already references (r4 §9; v r4-C2).

Line-by-line:

| GeneratedDcmSaver piece | Lines | Verdict | What the port does instead (decision) |
|---|---|---|---|
| `CreateGeneratedDcm` (stage Guids from AI captions), `GenerateColumnForDcmStages`, `SetStageColumnDefaultValue` | 49-103 | **drop** | the object and its lookup column already exist; stages come from lookup rows (S9-S10) |
| entity by `GetDesignInstance` | 109-112 | **port with fix** | runtime instance via `EntitySchemaResolver` (defect 2) |
| silent return when the stage column is missing | 113-117 | **port with fix** | refuse `column-not-found` / `column-not-published` / `column-not-lookup` (core checks none of this, v r1-C5) |
| `new DcmSchema(manager){Name = <Entity>Case, Caption, EntitySchemaUId, StageColumnUId, UId = Id, PackageUId}` | 123-130 | **port with fix** | `CreateSchema` (section 2); unique prefixed name, default suffixed, explicit refused when taken (defect 1; Q26); `CultureName` set; `Enabled = true` set explicitly (g1 item 1) |
| stage loop `UId = StageRecordId = stage.Id`, `Name = "Stage{i}"`, `IsDefault = i == 1`, `Color = "#54AA4D"` | 29, 132-142 | **port with fixes** | fresh `UId` per stage, `StageRecordId` = lookup row Id (defect 6: system-element UIds are derived by XOR with stage UIds, [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:334-337 per r4 §7]); no `IsDefault` (not persisted, v r4-C3); colour `#0C8701` (the designer saves it; `#54AA4D` renders as `#0C8701`, v r9-C5, v r3-C5; Q29) |
| full-mesh `StageConnections` n*(n-1) | 143-148 | **port as is** | mandatory for a clickable bar; core and `SaveSchema` do not fill it (v r5-C6, v r3-C4) |
| manager item hand-build + `SaveSchema(item, uc)` | 150-158 | **port with fix** | `CreateSchema` item; `SaveSchema(item, uc, true, true)` behind the repository seam, LAST in the write order (Q6, Q7) |
| `SaveDcmSettings`: `GetEntityByName("SysDcmSettings").SetColumnValue(SysSchemaUId, StageColumnUId).Save()` + link row | 161-182 | **port with fixes** | create-or-reuse the settings row (defect 3; Q13); object form + `Filters = "[{\"columnUId\":null}]"` for designer parity (Q14; 35/44 shipped rows, v r5-C8); written BEFORE the schema (Q7; the link row's `SysDcmSchemaUId` is a plain Guid [traced, judge PS CrtBase/Schemas/SysDcmSchemaInSettings/metadata.json:21-28]) |
| binding by the caller under bare names `SysDcmSettings` / `SysDcmSchemaInSettings` | GenAIApplicationContentGenerator.cs:444-449 | **port with fixes** | names `<Schema>_<id:N>` (defect 4; Q15; 97/97 corpus bindings are suffixed, v r4-C8) and a read-back of `SysPackageSchemaData` (defect 5) |
| `SaveDcmStagesData` (insert stage lookup rows after the build) | 184-197 | **drop** | values exist; no `<Lookup>_Dcm_Data` binding (Q21, note N4) |
| no transaction across schema / settings / binding | GenAIApplicationContentGenerator.cs:432-457 | **replace** | rows -> link -> bindings -> schema LAST -> verify, with compensation (Q7, design §5) |
| `UT/CrtGenAI.UnitTests/GeneratedDcmSaver.Tests.cs:18-20,140-180` | - | **pattern-only** (test) | `[MockSettings(RequireMock.DCM)]`, find the `InternalSaveSchema` call in `ReceivedCalls()` and assert the item's stages (v r8-C1); `SaveDcmSettings` itself has no test there, so the settings-writer test is new ground (r8 §3.7) |

Ported size: the composer + settings writer absorb the ~45 ported lines; the fixes are what make them larger
(section 10).

---

## 5. (D) DcmDesigner / DcmLibrary logic

There is no 8.x DCM designer; 8.x reuses the 7.x pieces (r2 §0 item 7). Almost all orchestration is client JS
(r2 §0 item 6), so it is ported as RULES into the package, never called.

| Logic | Where (7.8.0) | Verdict | Use in the minimal DCM |
|---|---|---|---|
| default all-to-all stage connections | `DD/Schemas/DcmSchema/DcmSchema.js:247-255` | **port as rule** | planner S10 (with GeneratedDcmSaver:143-148) |
| stage colour default and 8.x palette normalisation | `DD/Schemas/DcmSchemaStage/DcmSchemaStage.js:35,108`; `DcmConstants.js:78,116` | **port as rule** | constant `#0C8701` (v r9-C5; Q29) |
| one `SysDcmSettings` per section/object, found by `SysModuleEntity` or `SysSchemaUId` | `DL SectionDcmSettings.js:296-310`; `SysDcmSettingsManager.js` `findBy...` | **port as rule** | S8 create / reuse / `settings-conflict` (Q13) |
| stage-column change deactivates mismatching cases | `DL SectionDcmSettings.js:839-925` | **avoid** (refuse instead) | changing an existing row's column is modifying an existing DCM (out of scope) |
| settings row read predicate (`SysModuleEntity.SysEntitySchemaUId = @uid OR SysSchemaUId = @uid`, TOP 1 by `ModifiedOn`) | `DD/Schemas/DcmService/DcmService.cs:55-77` | **port as predicate** | S8 uses the same predicate, so the platform reader and the tool see the same rows (v r4-C6) |
| post-save chain: link row (create if missing) -> settings -> deactivate others (only with filters) -> new/changed lookup rows; bindings `SysDcmSchemaInSettings_<id>`, `SysDcmSettings_<id>`, `<Lookup>_Dcm_Data` | `DD/Schemas/DcmSchemaDesignerViewModel/DcmSchemaDesignerViewModel.js:457-517,1381-1392`; managers' `getPackageSchemaDataName` (`SysDcmSettingsManager.js:87-90`) | **port** (rows + names), **drop** (deactivation, lookup rows) | W1-W3; binding names Q15; the designer's order is schema-first, the tool's is rows-first (Q7) |
| open-by-UId needs a `SysDcmSchemaInSettings` row ("DcmSettingsId not found.") | `DcmSchemaDesignerViewModel.js:269-273,1093-1106` | **constraint** | W2 is mandatory for "opens in the DCM designer" (v r2-C4); M1 |
| on open, a stage whose `stageRecordId` is not a visible lookup row is reset and blocks the designer save | `DcmSchemaDesignerViewModel.js:568-613,1426-1454` | **constraint** | values read under the caller's rights (Q9 a; v r2-C5) |
| stage caption = lookup display value, re-read on open | `DD/.../DcmSchemaStagePropertiesPage.js:692-700` | **port as rule** | caption = display value in the caller's culture (S10) |
| uniqueness of `stageRecordId` across stages | `DcmSchemaStagePropertiesPage.js:365-402` | **port as rule** | trivially true for one stage per value; `stage-list-duplicate` for explicit lists (Q10) |
| stage column candidates = lookup columns | `DL SectionDcmSettings.js:494-526` | **port as rule** | `column-not-lookup` (S6) |
| filter validation | `DD/Schemas/DcmSchemaManager/DcmSchemaManager.js:223-466` | **avoid** | no filters in scope |
| Activity connection column + `EntityConnection` prep | `DL SectionWizardCasesSettings.js:424-684` | **later** | needed only when steps create activities |
| `VwSysDcmLib` view + `DcmDesigner.DBTests` tSQLt contract (`IsActive`, `StageColumnUId`, `EntitySchemaUId`) | `PS DcmDesigner.DBTests/SqlScripts/*` | **optional read** | an alternative E2E read-back; the design reads `DcmSchemaManagerRequest` as the bar does (design §14.3) (r2 §6, r8 §2.2) |
| `DcmService.RunDcmProcess` | `DD DcmService.cs:32-77` | **avoid** | starts an instance with no checks (v r1-C7); not the tool's job |

---

## 6. Script-task study: what of the script-task compile machinery applies to DCM

The question: CrtProcessBuilder carries a full "does this edit need a compile, and compile it" machinery built for
the Script task element. Which of it carries over to a DCM?

### 6.1 The machinery (process side)

| Piece | What it does | Evidence |
|---|---|---|
| `ScriptTaskElementHandler.Create` | `new ProcessSchemaScriptTask(schema)` with `UseFlowEngineScriptVersion = true`; body normalised by `ScriptTaskCode` | r6 §11.1 |
| `ScriptTaskNotices.NameCompiledCode` / `DescribeCompileRequirement` / `CarriesCompiledCode` | computes the compile demand: every script task, process methods (`MethodsBody`, `CompiledMethodsBody`, legacy `Methods`), every user task with `AfterActivitySaveScript`; the warning carries `CompileRequiredMarker = "until the configuration is compiled"` | [traced, today PB/Scripts/ScriptTaskNotices.cs:36,167-192,265,307] |
| callers of the notice | build (`ProcessBuildHandler.cs:226`), modify via script-body diff (`ProcessEditPipeline.cs:165,223-224`), version save (`ProcessVersionSaveHandler.cs:248`), activation (`ProcessVersionActivateHandler.cs:181-187`) | [traced, today grep `ScriptTaskNotices` in PB/Design] |
| `ProcessCompileHandler` | both gates; `CompileRequired = !CanUseFlowEngine(item) \|\| CarriesCompiledCode(item.Instance)`; when required, compiles | [traced, today PB/Design/ProcessCompileHandler.cs:70-71,94-95] |
| `ConfigurationCompiler` | `WorkspaceBuilderUtility.CreateWorkspaceBuilder(appConnection).Build(names)`, then `Workspace.WorkspaceChanged()` | [traced, today PB/Compilation/ConfigurationCompiler.cs:52,89-92] |
| `CompileProcess` response | `{success, errorMessage, processName, packageName, packageType, compileRequired, compiled, durationMs, errors, errorCount}`; no restart member on `main` | [traced, today PB/Contracts/CompileContracts.cs:86-127]; the stand's 1.6.6.142 adds .NET 8 restart reporting on an unmerged branch (v r10-C2) |
| `ProcessSchemaRepository.CanUseFlowEngine` -> `ProcessSchemaManager.GetCanUseFlowEngine` | flow-engine eligibility | [traced, today PB/Schema/ProcessSchemaRepository.cs:149-150; CORE/Terrasoft.Core/Process/ProcessSchemaManager.cs:351] - process-only, not on `BaseProcessSchemaManager<T>` (r6 §3) |
| `Build.log` `compiled: N [...]` oracle; "which compile picks an edit up" | proves a compile took the edit | `pb105556/docs/script-task-element-capture.md` (r6 §11.1) |
| clio side: `CommandExecutionResult.WithCompileNotRequiredNote()`; `compile-creatio process-name` -> `KnownRoute.CompileProcess` | adds "compile-creatio not required" unless the log carries the marker | [traced, today CLIO/clio/Command/McpServer/Tools/CommandExecutionResult.cs:35,50,64-80]; r7 §4 |

### 6.2 What the DCM side looks like in core

- No code generation: `DcmSchemaManager.GetSchemaSources` runs `ActualizeSchemaInfo` and returns an EMPTY sources
  collection; `GetGenerator` throws `NotImplementedException` [traced, today CORE/Terrasoft.Core/DcmProcess/DcmSchemaManager.cs:86-90,148-150].
- `DcmSchema.IsInterpretable` is always true and its setter throws on `false`; `SerializeToDB` is always true
  [traced, today CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:203-217].
- The compile decision skips DCMs: `Package.HasCompilableSchemas` lists `DcmSchemaManager` among non-compilable
  managers [traced, today CORE/Terrasoft.Core/Packages/Package.cs:464-472]; 0/61 corpus DCMs carry source [corpus 0/61]
  (v r4-C4, v r6-C2). The "skip by name" sites r6 §11.3 cites are file/VS/install lists, not the compile decision
  (v r6-C2).
- No script or methods surface in `Terrasoft.Core/DcmProcess` (r6 §11.3 grep).
- The save is NOT inert: `ActualizeSchemaInfo` writes the start-event row and the flow schema outside the save
  transaction (v r1-C2). So "no compile" never means "write `SysSchema` directly".

### 6.3 Verdict per piece

| Script-task piece | Applies to DCM? | Why |
|---|---|---|
| `ScriptTaskNotices` compile-demand predicate | **no** | typed on `ProcessSchema`; a DCM has no scripts, methods or `AfterActivitySaveScript` (6.2) |
| `CompileRequiredMarker` phrase discipline | **yes, as a constraint** | clio suppresses its "compile-creatio not required" note when the log contains the marker; every DCM note must therefore never contain "until the configuration is compiled" (design §3.4; pinned by `CaseBuildHandlerTests` and `CompileNotRequiredNoteGateTests`) |
| `ProcessCompileHandler` / `CompileProcess` op | **no** | `CanUseFlowEngine` is process-only, and a DCM package compile would build nothing for the DCM (`HasCompilableSchemas`); the tool never calls `compile-creatio` (design §16) |
| `ConfigurationCompiler` (`WorkspaceBuilder.Build`) | **no** | same reason; a compile reloads the runtime for every user, so calling it would only add cost |
| `{compileRequired, compiled}` answer shape | **yes, as a pattern** | becomes the `obligations` block `{compileRequired: false, restartRequired: false, reloadOpenPages: true, artifacts[]}` (design §3.3) - per artifact, so a later step can raise one artifact's verdict without a contract change (design §12) |
| "a save never compiles as a side effect" rule | **yes** | already true for DCM; the tool reports, never performs |
| `Build.log` oracle | **no** | nothing to compile, so `Build.log` proves nothing about a DCM; the oracle is the read-back V1-V2 and `DcmSchemaManagerRequest` (g1; design §14.3 case 2) |
| restart reporting (1.6.6.142 feature build) | **no** | not on `main`; nothing in the DCM save rebuilds assemblies (r6 §11.3); installing CrtProcessBuilder itself still needs a configuration build + restart once per environment (v r7-C2) |
| `UseSystemSecurityContext` (IJ10) | **no** | affects script-task rights only (g2 §3) |
| clio `WithCompileNotRequiredNote()` | **yes** | the tool opts in (v r7-C5: the helper is opt-in, used by 4 process tools) |

Knowledge-base consequence: `KB/guidance/mcp/guides/processes/process-modeling.md:157-158` lists "DCM" among schemas
that "carry their own compile obligations"; the sentence names no obligation and is wrong for the DCM schema
(v r1-C1, v r4-C4). "DCM, " is dropped (-5 B of about 13 B headroom) and the new article owns the "a case needs no
compile" rule (Q23).

### 6.4 What the later steps task inherits

- A step inside a stage is a user task or a sub-process [corpus 81/81]; the DCM palette (`SysDcmUserTask`) offers no
  script task, and no stored DCM step is one (r6 §13.1-13.2). A DCM never generates a `MethodsWrapper`, so a script
  task inside a stage would have no method to bind at run time [inferred from r6 §11.2-11.3].
- A step that needs `ExecutionMode.Compile` makes the DCM save FAIL (there is no compile fallback) (v r1-C1 point 2).
- A step referencing a CUSTOM user-task schema keeps that schema's own compile obligation; it is that artifact's
  verdict, which the per-artifact `obligations.artifacts[]` already carries (v r6-C2; design §12).
- So the only script-task asset the steps task can reuse is the reporting pattern (predicate shared by the save notice
  and the answer, marker phrase, never compile on save) [inferred].

---

## 7. (E) creatio-ui: the progress bar

Nothing in creatio-ui is called from clio or the package; facts are ported as a constant fragment or as rules the
DCM must satisfy for the bar to render.

| Piece | Where | Verdict | Use |
|---|---|---|---|
| app-wizard inserts `ProgressBarContainer` (`crt.FlexContainer`, index 0 of `MainContainer`) + `crt.EntityStageProgressBar` `ProgressBar` with `entityName`, `saveOnChange: true`, `askUserToChangeSchema: false` | `AM/services/page-content-generator/page-content-generators/page-with-tabs/page-with-tabs-freedom-content-generator.ts:57-109,318-321` (spec `...generator.spec.ts:175-245`) | **port as constant** | `ProgressBarFragment` in clio (design §7.3; Q19); only `PageWithTabsFreedomContentGenerator` implements `addProgressBar` (r3 §7.3) |
| wizard trigger `section.hasDcm`; `AppGenDcm {dcmSchemaUId, entitySchemaUId, stageColumnName, stageColumnUId, stagesEntitySchemaUId, stages}` | `AM/services/page-content-generator/page-content-generator.service.ts:84-110`; `AM/services/application-installer.service.ts:31-55` | **reference only** | confirms the bar is added client-side, by no server code (r4 §3 step 7); the `case` result block plays the same role for the agent [inferred] |
| runtime preprocessor creates every binding (`recordId`, `value`, `stages`, outputs) | `entity-stage-progress-bar-preprocessor.ts:72-74,164-168,206-279,326-344` | **constraint** | never emit those keys [corpus 0/24 pages wire them] (v r3-C2); `ProgressBarPlacementTests` pins their absence |
| bar resolves the FIRST enabled, active-version DCM of `entityName` via `DcmSchemaManagerRequest`, not `SysDcmSettings` | preprocessor.ts:87-97,164-168; `load-data.handler.ts:244-263`; `entity-stage-schema-filter.service.ts:14-34` | **constraint** | page and DCM are independent writes; `entityName` = primary data source entity (`page-entity-mismatch`) (v r3-C1, `_verdicts.md` conflict 1) |
| clickable only with a `StageConnection` from the current stage | `entity-stage-progress-bar-process-stage-permissions.handler.ts:45-56` | **rule** | full mesh (v r3-C4) |
| `currentColor.toUpperCase()` without a null guard | `util/entity-stage-progress-bar-utils/src/services/entity-stage-progress-bar-color-mapper.service.ts:22,60,85-112` | **rule** | every stage gets `#0C8701` (v r3-C5) |
| step id = `StageRecordId`, caption = lookup display value (fallback stage caption), order = DCM array, last = final | `load-data.handler.ts:117-155,293-311` | **rule** | planner writes the order itself (v r3-C6) |
| click writes `{value, displayValue}` into `<PDS>.<column>`; `saveOnChange` decides immediate save | `stage-changed.handler.ts:47-65,96-127`; `component.ts:30-33,189-205` | **verification only** | both sync directions hold by construction; proven only in the browser (M3, CAADT) (v r3-C7, v r9-C6) |
| SPA memoizes `DcmSchemaManagerRequest` per tab | `data-access/common-api/src/lib/dcm/dcm-schema-manager-service/dcm-schema-manager.service.ts:111-135` | **note** | N1 "reload (F5)" (r3 §0 item 7) |
| designer URL `/Nui/ViewModule.aspx?vm=DcmDesigner#case/{0}` | `data-access/common-api/src/lib/app-info/app-info.service.ts:422-429` | **port as string template** | `case.designerUrl`, `0/` on .NET Framework (v r9-C8) |
| `PageWithTabsAndProgressBarTemplate` (bar in the template, flags false/true, `entityName` macro) | `PS CrtUIPlatform/Schemas/PageWithTabsAndProgressBarTemplate/PageWithTabsAndProgressBarTemplate.js:43-83` | **detect** | pages on it land in `page.status = already-present` (design §7.2) |
| `DisableEntityStageProgressBarPreprocessing` kill switch | preprocessor | **unmeasured** | off by default, not read on the stands; guidance troubleshooting line (design §17) |

---

## 8. (F) clio and clio-knowledge

### 8.1 Process-tool patterns

| Piece | Path | Verdict | Use |
|---|---|---|---|
| `BaseTool<TOptions>.InternalExecute<TCommand>` (per-call container, tenant `McpToolExecutionLock`, re-entrant) | `CLIO/clio/Command/McpServer/Tools/BaseTool.cs:216-238` | **as-is** | the whole orchestration runs in one call under one lock [traced, judge] |
| `[RequiresPackage]` enforcement for the per-call target | `BaseTool.cs:376-390` | **as-is** | floor on the new options type (Q22) |
| `CreateBusinessProcessTool` argument handling (null args, `[JsonExtensionData]` unknown keys, mini contract, JSON-or-string argument) | `CLIO/clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs:358-424,430-457` | **pattern-only** | `CreateDynamicCaseTool`; `stages` as array or JSON string [traced :32 `NullArgsError`, :372-395 null-args refusal / `ExtensionData` / mini contract, :455-456 `[JsonExtensionData]`] |
| `McpToolArgumentSupport` (`BuildUnknownArgumentError`, `TryReadJsonDocumentArgument`) | r7 §1.2 | **as-is** | C1 |
| `CreateBusinessProcessService` (wrapped `{"request": ...}` body, envelope parse, "outcome UNKNOWN" text) | `CLIO/clio/Command/CreateBusinessProcessCommand.cs:323-443` | **pattern-only** | `DynamicCaseService`; a 404 / non-envelope reply on the validate pass maps to `process-builder-without-build-case` (Q22) |
| `RequiredPackageChecker` + `BundledPackageConvergence` | `CLIO/clio/Common/RequiredPackageChecker.cs:211-241`; `BundledPackageConvergence.cs:19-111` | **as-is** | an older archive on the environment is refused with the install hint, never upgraded (v r7-C2) |
| `ServiceUrlBuilder.KnownRoute` / `KnownRoutes` | `CLIO/clio/Common/ServiceUrlBuilder.cs:518-529` | **extend** | one route `/rest/ProcessDesignService/BuildCase` at the next free value after rebase (122 is claimed by two remote branches, `_verdicts.md` conflict 4); route tests for both runtimes in `CLIO/clio.tests/Common/ServiceUrlBuilder.cs` |
| `CommandExecutionResult.WithCompileNotRequiredNote()` | `CommandExecutionResult.cs:64-80` | **as-is** (opt-in) | section 6.3 |
| `BindingsModule.cs:537-545` (service + `AddTransient<XxxCommand>()`) | r7 §1.2 | **extend** | DI of command, service, resolver |
| file set of commit `e6d2740b9` (set-active-business-process-version: tool, command/options/service, route, DI, `install-process-builder` docs, `McpCapabilityMap.md`, tool/service/attribute tests, passthrough registry, archive description pin, E2E, knowledge record, sprint status) | r7 §1.5 | **pattern-only** | the checklist for a new process-designer tool; MCP-only, so the four CLI doc targets are "reviewed, no update required" |
| `CreateUserTaskCommand.cs:229,535-548` (`DcmSmallSvgImage`, `dcmEditPageSchemaUId`) | r7 §4 | **later** | DCM-aware user-task DTO fields, relevant only to the steps task |

### 8.2 Page tools

| Piece | Path | Verdict | Use |
|---|---|---|---|
| `IRelatedPageAddonService.Get` (hierarchy view) | `CLIO/clio/Command/RelatedPages/RelatedPageAddonService.cs:110-124` | **as-is** | record-page resolution step 2 (design §7.1; g4 §7) |
| `DefaultPageAddonReader.ReadWebDefaultPageUId` / `RelatedPageAddonMetadata.TryFindUntypedDefault` (both internal, mobile converter) | `CLIO/clio/Command/McpServer/Tools/MobilePageConverter/DefaultPageAddonReader.cs:36-49,85-119`; `RelatedPageAddonMetadata.cs:41` | **avoid** | ignores role and `IsSspDefault`; could return the portal page (g4 §6) |
| `list-entity-client-schemas`, `list-pages`, `create-app-section` readback | `ListEntityClientSchemasCommand.cs`, `PageListTool.cs` | **avoid** | do not answer "the record page of X" (g4 §6) |
| `SysModuleEdit` DataService read | as `ListEntityClientSchemasCommand` | **pattern-only** | fallback step 3, Freedom page only; no new `KnownRoute` (g4 §6) |
| `PageMergedConfigResolver.ResolveMergedConfig` | `CLIO/clio/Command/McpServer/Tools/PageMergedConfigResolver.cs:28-70` | **avoid** | returns only `(ViewModelConfigJson, ModelConfigJson)` (:43), no view tree; the view checks use `PageGetCommand.TryGetPage(...).Bundle.ViewConfig` / `ModelConfig` (`CLIO/clio/Command/PageGetOptions.cs:90`, `PageModels.cs:487-514`) and the append dry run of `PageUpdateCommand` (`PageUpdateOptions.cs:488-520`) (design §6 C3, §7.2) |
| `IPageDataSourceReferenceValidator` | `PageUpdateTool` chain | **as-is** | run on the fragment before the write; the data-source check lives in the tool layer, not in the command (v r7-C6) |
| `PageUpdateCommand.TryUpdatePage` (`Mode = append`), resolved environment-scoped | `CLIO/clio/Command/PageUpdateOptions.cs:216,287`; resolve pattern `PageEditToolHelpers.cs:22-35` | **as-is** | the only page writer (Q18 a) |
| `PageBodyMerger.Merge` (client-side append; same `operation`+`name` REPLACES) | `CLIO/clio/Command/PageBodyMerger.cs:229-284` | **as-is** (indirect) | reason for the `_<8 hex>` name suffix on collision (design §7.2) |
| design-package resolution `GetDesignPackageUId` | `CLIO/clio/Command/PageDesignerHierarchyClient.cs:20-42` | **as-is** | page lands in the default design package (Q17) |
| `PageUpdateTool` agent-body guards (AST lint, baseline guard, run-process-button signature) | `CLIO/clio/Command/McpServer/Tools/PageUpdateTool.cs:29-31,132-141,239,531` | **avoid** (Q18 a) / extract later (Q18 b) | the fragment is a clio constant, pinned by a unit test |
| `crt.EntityStageProgressBar` registry entry | `CLIO/clio.tests/Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json:5332-5422` | **reference** | confirms the input names the fragment uses (`entityName`, `saveOnChange`, `askUserToChangeSchema`) |
| `PageWithTabsAndProgressBarTemplate` in the known templates | `CLIO/clio/Command/ListEntityClientSchemasCommand.cs:122-123` | **reference** | template note N6 |

### 8.3 Package dependencies

| Piece | Path | Verdict | Use |
|---|---|---|---|
| `IPackageDependencyManager.AddDependencies` over `PackageService.svc/SavePackageProperties` (reports cycles as storage errors) | `CLIO/clio/Package/IPackageDependencyManager.cs:37`; `PackageDependencyManager.cs:171-186` | **as-is** | C5: add what the validate pass reports missing, opt-out `add-package-dependencies=false` (Q16) |
| platform applier (`PackageValidator.GetCanAddPackageDependency` + `Package.AddDependency`) | `CORE PackageValidator.cs:1924; Package.cs:452` per g3 | **avoid** | the designer's applier is internal; re-implementing it in the package is not recommended (g3 item 2) |

### 8.4 clio-knowledge

| Piece | Path | Verdict | Use |
|---|---|---|---|
| process article layout (file without prefix, itemId with `process-`) | `KB/bundle-source.json:141,345,1884` (`send-email-attachments.md` -> `process-send-email-attachments`) | **pattern-only** | `processes/dynamic-case.md`, itemId `process-dynamic-case` (Q23) |
| routing article | `KB/guidance/mcp/guides/routing.md` | **extend** | Business processes row + Pages pointer |
| `process-modeling.md:157-158` | | **extend** (fix) | drop "DCM, " (section 6.3) |
| `related-data/page-binding.md:3-15,58-59,69` | | **as-is** | the add-on is already documented as the page binding (g4 §6) |
| side finding `backend/package-dependencies.md:22-30` | | out of scope | says clio adds a dependency by itself; clio only reports it on the entity-designer path (g3) |

---

## 9. (G) Test helpers

### 9.1 Platform unit-test harness (`UT/`, referenced by `PBT` through `Libs/UnitTest.dll`, `Terrasoft.TestFramework.dll`)

| Helper | Path | Use |
|---|---|---|
| `BaseConfigurationTestFixture` + `[MockSettings(RequireMock.DBEngine \| RequireMock.DCM)]` | `UT/UnitTest/BaseConfigurationTestFixture.cs:170-173,341-386` | `DcmSchemaRepositoryTests`, `DcmSchemaComposerTests` (v r8-C1, v r8-C3) |
| `TestUserConnection.SetDcmSchemaManager` | `UT/UnitTest/TestUserConnection.cs:147-170,202-274` | sufficient for reads of `UserConnection.DcmSchemaManager`; not for provider lookups or `SystemUserConnection` (v r8-C3) |
| `TestDcmSchemaManager` (UnitTest, not core's internal one) | `UT/UnitTest/TestDcmSchemaManager.cs:9-37` | when code under test enumerates `GetItems()` (name check S11) (r8 §3.3 option C) |
| `NSubstituteExtension.Protected(name, args).Returns(true/false)` on `InternalSaveSchema` (all 9 args as matchers) | `Terrasoft.TestFramework` `NSubstituteExtension.cs:34-68` | save success and failure branches (v r8-C7 refutes "no Protected()"); pass a real item with `Instance` set or the wrapper NREs (v r8-C2) |
| `TestEntitySchemaManager.CreateEntitySchemaMock(..., lookupColumns)` (reference schema first) | `UT/UnitTest/TestEntitySchemaManager.cs:316-328,337-340` | object + lookup column + settings-table schemas |
| `SelectData(...).AddRow(...).MockUp()` | `UT/UnitTest/SelectData.cs:19-72` | lookup rows for `StageValueReaderTests` (needs `RequireMock.DBEngine`) |
| `QueryAssert.Got<Insert>(uc, "SysDcmSettings", ...)` | `UT/UnitTest/QueryAssert.cs:71-93` | settings and link row inserts |
| `SelectAnalyzer.HasOrderByItem` | `UT/UnitTest/SelectAnalyzer.cs:42-49` | **not used** under Q9 a: ordering is done in the pure planner; the ESQ shape under the mocked engine is unproven (g6 §5). Becomes the pin if Q9 b (`Select` ORDER BY) is chosen |
| `CrtGenAI.UnitTests/GeneratedDcmSaver.Tests.cs:18-20,140-180` | | the only test in the corpus that builds and saves a DCM with stages (r8 §2.1) |
| `DcmDesigner.UnitTests/DcmService_Tests.cs:31-41,52-59` | | stubbing the `SysDcmSettings` read and `GetInstanceByUId` |
| `CrtPlaybook.UnitTests/PlaybookStageLoader_Tests.cs:29-47` | | stubbing a stage-lookup ESQ |
| core techniques (copy, not reference): `Substitute.ForPartsOf<DcmSchema>` + `When(s => s.LoadLocalizableValues()).DoNotCallBase()`; metadata round-trip with `BeEquivalentTo(...WithStrictOrdering())`; `DesignedDcmSchema.txt`; `DcmProcessTestUtilities.SetupCase` | `CORE/Terrasoft.Core.Tests/DcmProcess/BaseDcmProcessTestCase.cs:44-59,99-122`; `DcmSchema.Tests.cs:146-171,250-281`; `Terrasoft.Core.Tests/TestFiles/DesignedDcmSchema.txt`; `DcmProcessTestUtilities.cs:71-109` (v r9-C3) | parity and composer tests; core's internal `TestDcmSchemaManager` hooks are unreachable (r8 §2.3) |

All of it is net472-proven; `UnitTest.dll` loads in the net8 host under `-c dev-n8` (v r8-C6).

### 9.2 clio unit tests

| Helper / pin | Path | Use |
|---|---|---|
| tool test pattern (reflection pin of annotations; `IToolCommandResolver` substitute; resolved command receives options) | `CLIO/clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs:14-80` | `CreateDynamicCaseToolTests` |
| service test pattern (substituted `IServiceUrlBuilder` / `IApplicationClient`, wrapped body, response mapping) | `CLIO/clio.tests/Command/CompileBusinessProcessServiceTests.cs:28-75` | `DynamicCaseServiceTests` |
| `ProcessDesignerRequiresPackageAttributeTests` | `CLIO/clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs:25-153` | floor literal and hint |
| `BundledProcessBuilderPackageTests` (`ExpectedOperationContractCount` 8 -> 9; a NEW pin for `EnsureCanManageDcm` call sites; `EnsureCanManageProcessDesign` stays 6; description map) | `CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:292,361,373,384,651-700` | moved by hand in the rebundle commit, package side first (v r6-C4, v r7-C3) |
| `PassthroughToolClassificationRegistry`, `ToolContractPayloadBudgetTests`, `ToolContractShortFormTests`, `McpToolArgsWireContractTests`, `CompileNotRequiredNoteGateTests`, `McpE2eSelectionCoverageTests` | r7 §6.1 | one index line (18,312 / 18,432 B, v r7-C4) |

### 9.3 MCP E2E (`E2E`)

| Helper | Path | Use |
|---|---|---|
| `ProcessDesignerE2EArrange.StartAsync(subject, minimumPackageVersion)` | `E2E/Support/Mcp/ProcessDesignerE2EArrange.cs:45-63,236-247` | session, reachable env, floor gate |
| `ProcessDesignerE2ESupport.IgnoreUnlessProcessBuilderIsRequired` | `E2E/Support/Mcp/ProcessDesignerE2ESupport.cs:86-91` | old package fails the ProcessDesigner lane instead of ignoring |
| `ProcessDesignerStoredReads.SelectInlineAsync` / `SelectToFileAsync` / `DecodeMetaData` | `E2E/Support/Mcp/ProcessDesignerStoredReads.cs:13-26,120,137,164` | `SysSchema`, decoded stages, settings rows, `SysPackageSchemaData` bindings |
| `SeededApplicationResolver.ResolveOrIgnoreAsync` | `E2E/Support/Mcp/SeededApplicationResolver.cs:40` | seeded `AutoTestClioMcp` (its entity has no lookup column yet; Q25) |
| `sync-schemas` `create-lookup` with `seed-rows`, `update-entity` add Lookup; `LookupRegistrationProbe` | `CLIO/clio/Command/McpServer/Tools/SchemaSyncTool.cs:42-54,399,548-552`; `E2E/Support/Creatio/LookupRegistrationProbe.cs` | the `LocalOnly` interim arrange (publishing = destructive tier, g5 §3.2) |
| `remove-data-binding-row-db`, `delete-schema` | `CLIO/clio/Command/McpServer/Tools/DataBindingDbTool.cs:86,97`; `DeleteSchemaTool.cs:14-36` | cleanup: link binding, settings binding, DCM, page replacing schema, strictly sequential (g5 §4) |
| fixture shape (`AllureFeature`, `NonParallelizable`, `McpE2ECategories.ProcessDesigner`, cleanup in `finally`) | `E2E/ProcessFileElementToolE2ETests.cs:40-59,115-140`; `E2E/Support/Configuration/McpE2ECategories.cs:24-40` | `CreateDynamicCaseToolE2ETests` |

### 9.4 Knowledge-base tests

`SendEmailAttachmentsGuidanceTests` (precedent for `ProcessDynamicCaseGuidanceTests`), `ProcessGuideCrossReferenceTests.cs:51,463-549`
(routing `name=` token), `ProcessGuideSet.cs:20,27,88-112`, `GuidanceInlineReplyBudgetTests.cs:47,72` (v r7-C8).

### 9.5 CAADT

`creatio-adaclio-testing` `prompts/bpms-tools/` prompt and a `creatio-playwright-tests`
`tests/_generated/features/caadt/specs/` spec: existing homes, new content (r7 §7, g5 §5).

---

## 10. (H) Genuinely new code

Clean-slate production lines. The per-piece figures are design A's floor (design A §2.4); the chosen design
grafts the composer split, the verifier, the settings-table rights check and the compensation on top, which takes the
package from about 1,260 to the design's about 1,400 (design §11) [inferred split of the difference]. Test lines are
not estimated here.

| New piece | Where | Size (src, lines) | Why nothing existing does it |
|---|---|---|---|
| `BuildCaseRequest` / `CaseStageRequest` / `BuildCaseResponse` | `PB/Contracts/` | ~150 | a DCM is not a BPMN descriptor; bolting it onto `BuildProcessRequest` breaks the descriptor drift tests and the 16 B contract headroom (v r7-C4) |
| `CaseBuildHandler` (S1-S21, write order, compensation, three outcomes) | `PB/Design/` | ~350 + graft | no handler builds a `BaseProcessSchema` that is not a `ProcessSchema` (v r6-C5) |
| `CaseStagePlanner` (pure: order precedence, ties, bounds, names, UIds, colour, mesh) | `PB/` | ~150 | no order rule exists anywhere (g6; v r9-C4) |
| `StageValueReader` (ESQ, bounded `MaxCaseStages + 1`) | `PB/` | ~120 | no "all rows of a lookup with order columns" reader (r6 §14) |
| `DcmSchemaComposer` (graft from design B) | `PB/` | part of the graft | separates composition from saving so the steps task adds a `DcmStepComposer` (design §12) |
| `DcmSchemaRepository` + `IDcmSchemaSaver` | `PB/Schema/` | ~180 | `ProcessSchemaRepository` is typed to `ProcessSchema` (v r6-C1) |
| `DcmSettingsWriter` (rows, bindings, read-back, compensation) | `PB/` | ~150 | core never writes these rows (v r5-C8); the package has no entity-write precedent [traced grep PB: no `GetEntityByName`, per design A] |
| `CaseDependencyPlanner` (open input set) | `PB/` | ~100 | the platform applier is internal (g3 §3.1) |
| `CaseVerifier` (V1-V2, competing-case count) (graft from design C) | `PB/` | part of the graft | nothing reads a case back the way the bar does |
| `EnsureCanManageDcm`, DI, op, facade, `IPackageCatalog` split | existing files | ~60 | - |
| `CreateDynamicCaseTool`, options, command, `DynamicCaseService` | `CLIO/clio/Command/...` | ~650 | new tool contract |
| `RecordPageResolver` (role, portal flag, type, `SysModuleEdit` fallback) | `CLIO/clio/Command/...` | ~150 | no existing reader filters role, portal flag and type (g4 §6-7) |
| `ProgressBarFragment` (constant builder, name-collision suffix) | `CLIO/clio/Command/...` | ~40 | - |
| route, DI lines | `ServiceUrlBuilder.cs`, `BindingsModule.cs` | small | - |
| KB article + routing rows + `process-modeling` fix + test class | `KB/` | ~8 KB text, ~150 test lines (design A) | - |
| SVN `AutoTestClioMcp` seeded object, lookup with `Position`, tabbed Freedom page, add-on (Q25) | PackageStore | not sized | the seeded entity has only `Name` (g5 §3.1) |
| CAADT prompt + Playwright spec | CAADT repos | not sized | - |

Package total about 1,400, clio about 850 (design §11).

---

## 11. Unknowns this map depends on

| Unknown | Settled by |
|---|---|
| whether a route-A DCM opens in the designer with no error, and a designer re-save keeps one binding per row | M1 |
| `IsActive && IsActiveVersion` in `DcmSchemaManagerRequest` right after the save | M2 / E2E case 2 |
| under whose rights and into which package `ClassFactory.Get<IPackageSchemaDataCreator>()` binds from a package WCF call | M6 |
| what `RemoveItemByUId` leaves (start-event row, flow schema) - decides how complete compensation can be | M5 (Q7) |
| whether a package-dependency add marks anything for compile on either runtime | M10 (Q16) |
| whether ESQ-built ordering can be pinned with `HasOrderByItem` | only matters if Q9 b is chosen (g6 §5) |
| `NSubstituteExtension.Protected` on `InternalSaveSchema` and `WriteMetaData` over `Substitute.For<DcmSchemaManager>()` | traced, not executed (v r8-C7; r8 §3.10) - first package test run |
| every .NET 8 behaviour | M8 on an owner-provisioned stand (Q24) |
