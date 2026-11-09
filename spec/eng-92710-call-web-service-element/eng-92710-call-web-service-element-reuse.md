# ENG-92710 Call web service element: reuse

Written 2026-11-02, updated 2026-11-03. Phase-1 plan for Jira ENG-92710 "Call web service element". This file is
research, not a design. Every decision it names (Q1-Q20) was agreed by the owner on 2026-11-03; where the agreed line
differs from the earlier proposal, this file follows the agreed line. Each one is described in
[decisions](eng-92710-call-web-service-element-decisions.md). The facts and their evidence are in
[facts](eng-92710-call-web-service-element-facts.md). The target bytes are in
[serialization](eng-92710-call-web-service-element-serialization.md).

Basis: crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history only), clio `origin/master`
`6be5dde28`, clio-knowledge `origin/master` `5f14ceb`, CrtProcessDesigner 7.8.0 and the PackageStore corpus, core
`<core>` `468c4c71b` (8.3.3.1722), a read-only inventory of stand `Creatio` (Creatio 10.1.37, .NET
Framework) taken 2026-11-02, and the designer probes and EM9 of 2026-11-03 recorded in the decisions file. The older checkout `<Creatio2>/TSBpm` (2025-07) predates `IsIgnoreErrors` (IX4).
Where it disagrees with the current core, the current core wins.

Paths:
- `PB/` = `packages/CrtProcessBuilder/Files/src/cs/`
- `PBT/` = `tests/UnitTests/CrtProcessBuilder.Tests/`
- `CORE/` = `<core>/TSBpm/Src/Lib/`
- `PD/` = `CrtProcessDesigner/branches/7.8.0/Schemas/`
- `CLIO/` and `KB/` = the repository roots

Evidence tags:
- traced: read in source. A trace is a hypothesis about run time until an EM settles it.
- measured: seen in the corpus, in shipped binaries or on the stand.

The research notes r1-r10 and g1-g5 are local research notes, not in this repository; they are cited as rN / gN.
Fact-check verdicts override the notes they correct.

## 1. The reuse picture in one paragraph

The platform provides a public service model, which the builder can call. It does not provide a parameter generator
that matches the designer. The only generator a package can reach, `ProcessSchemaWebService.SynchronizeParameters()`,
builds flat top-level parameters only. It builds no `L18` items, no `ItemValue` child, no caption suffix, no result
parameters and no BK15 rows (r3 H6, r3-C3/C5 verdicts). So the shape the designer writes is a port of the client
algorithm, and that port is the one large piece of genuinely new code (Q3).

From crt-process-builder, the pieces that work as-is are:
- the transport
- the pre-save validator
- the dependency snapshot
- the whole mapping engine. It produces the bytes of the three corpus composite shapes without a rules change (g3 §5).

The generic describe projection is reused only in part: measured 2026-11-03 (decisions Q14), the package's describe of
a designer-built element returns every output with its `tag` and nested `itemProperties` and the bound inputs only (M-31), but no
service, method, timeout or ignore-errors member (section 3.1).

The element skeleton is a pattern taken from the Sub-process family (an own `ProcessSchemaActivity` class, resolving an
external schema, Create/Update modes, the retarget guard, the drift report). The port discipline comes from
`PreconfiguredPageParameterSync`, and the parity tests come from the Process file capture oracle.

clio and the KB pass the block through, but four clio guards and the KB "read-only" pin have to move in the same
release train (r5 §0). Nothing on the clio side resolves services, and clio's one web-service reader is not reused
(Q10, Q16).

## 2. (A) Core APIs

Assembly abbreviations: `TC` = Terrasoft.Core, `TS` = Terrasoft.Services. Both are already referenced by the package
and by its test project (`Files/CrtProcessBuilder.csproj:282-283`; test csproj :237-238, r8 §4), so no new reference
is needed.

| API | Asm | Visibility | What for | Verdict | Risk / note |
|---|---|---|---|---|---|
| `ProcessSchemaWebService` (ctor; `ServiceSchemaUId` IX1, `MethodName` IX2, `Timeout` IX3 in ms) | TC | public | the element the builder creates (Q1) | **call** | No `(ProcessSchema)` ctor, so set `UId`, the parent and `ManagerItemUId` by hand (r4 §3). Both setters are plain auto-properties and run no sync, unlike `ProcessSchemaSubProcess.SchemaUId` (r4 H6, verdict confirmed). The ctor resolves the parameter provider from `CoreApiContainer`; see the next rows and section 5.2 |
| `ProcessSchemaWebService.IsIgnoreErrors` (IX4) | TC | public **from 8.3.2.3590** | `ignoreErrors` (Q7) | **call** (direct) | Absent on 8.3.1.4498 and 8.1.3.6947 (g2 §b, measured on binaries). EM9 (2026-11-03, local builds): the package on `origin/main` already fails to compile on both of those cores for other reasons and builds on 8.3.2.4199, so the direct reference adds no floor; the reflection guard is dropped (Q7, Q13). Run time also needs the feature `DisableIgnoreErrorsInWebServiceUserTask` off (traced). EM7 measures the run-time effect |
| `ProcessSchemaElementManager.WebServiceTaskUId` (`652b598d-…`) | TC | public static | stamp `ManagerItemUId` | **call** | `SubProcessElementHandler` stamps `ManagerItemUId` the same way (r4 §2) |
| `ProcessSchemaWebService.SynchronizeParameters()` | TC | public | the flat platform sync | **avoid as the generator; tolerate at load** (Q3) | It runs anyway on every design-time load: `FindDesignItem` and the 6-arg `GetItemFromMetaData`, including their cache hits (g4 §0). Hazards (traced): an empty IX1 runs `ClearParameters()` and drops ALL parameters, the result parameters and BK15 rows included. An unknown or wrong-case method drops every tagged parameter silently (measured 2026-11-03: EM11 designer half, EM8 current-builder half, where an unrelated modify deleted 8 tagged params from the database). Matching is by Name, ordinal. A match keeps UId, `SourceValue` and `L18`, but overwrites Caption, type, IsRequired, Direction and `IsValueSerializable` (g4 §1). "No-op on a correct shape" is therefore a hypothesis for the captions; EM2 and EM5 settle it. In the bare package harness the call NREs (section 5.2) |
| `IProcessSchemaElementParametersProvider` / `ProcessSchemaWebServiceParametersProvider` | TC / TS | **internal** | the top-level rules | **mirror the algorithm** (top-level rules only) | It cannot be called from a package: there is no `InternalsVisibleTo` for configuration assemblies (r3 §1). Its rules match the designer for the top level (r3-C3 verdict). Its core test case tables are reused as an oracle (section 5.4) |
| `ProcessSchemaActivity.SynchronizeParameter(target, source)` | TC | internal static | update-in-place rules | **avoid** | Not needed under Q11's replace semantics. The load-time sync applies it anyway |
| `UserConnection.GetServiceSchemaManager()` → `ServiceSchemaManager`: `GetItems()`, `FindItemByUId`, `FindItemByName`, `GetInstanceByUId` | TS / TC | public, virtual lookups | resolve the service (Q9) | **call behind `IWebServiceCatalog`** | `FindItemByName` returns the first match and compares ordinally (g5 §b), so the package does the case-insensitive resolution and the ambiguity refusal itself. Call `FindItemByUId` before `GetInstanceByUId`, the order the platform uses. IX1 stores the manager-item UId; in the corpus that equals `realUId` (32/32 corpus services, measured; stand `Creatio` has 3 service schemas) |
| `ServiceSchema` (`Type` IQ2, `ServiceMethods` IQ4), `ServiceMethod` (`Request`/`Response.Parameters`), `ServiceParameter` {`Name`, `Caption`, `Path`, `Type`, `DataValueTypeName`, `IsArray`, `IsRequired`, `ItemProperties`} | TS | public | read the parameter tree, plus the SOAP check (Q20) | **call (read only)** | `ServiceMethods.FindByName` compares ordinally, so the method is resolved case-insensitively by a scan (Q9; EM11). Never read into a DTO: `ServiceParameter.DefValue` (IU5) and `ServiceSchema.AuthInfo` (IQ5). The corpus holds a literal basic-auth password and a `Bearer …` header default (g5 §c, measured; Q16). `ServiceDataValueTypeNames` is internal, so compare against the literal `"Object"`. `ServiceParameterCollection.FindByPath` is internal |
| `DataValueType` UId constants (`MaxSizeText`, `CompositeObjectList` 651ec16f, `ObjectList` 4b51a8b5, `Text` 8b3f29bb, `Integer`, `Boolean`, Object 84ed6865) and `DataValueTypeManager.GetInstanceByUId` | TC | public | the type map of the port | **call** | `MaxSizeTextDataValueType.MaxsizeTextValueTypeName` is internal, so resolve by UId and never by name. Resolving "Text" by name gives Text, not MaxSizeText (r3 §7) |
| `ProcessSchemaParameter`, `ProcessSchemaNestedParameterCollection` (`ItemProperties`, L18), `Tag` (L17) | TC | public | build the tagged and nested parameters | **call** | Nested items are not back-filled by the collection: stamp `ContainerUId`, A3 and A4 explicitly on every level (g1 §b; A3 = A4 = process UId on 751/751, measured) |
| `ProcessSchemaMapping` (BK15) | TC | public | the four result-parameter rows (Q4) | **call** (Q4: written; the first rows the package authors itself; `A2` not rewritten on a rename) | Order matters: set the parameter's `SourceValue` first, then add the row. Otherwise the setter aliases the row's Source and writes GS5 into Success GT1 (g1 §c, traced). The rows do not affect run time: the Q4 probe (2026-11-03) measured that the designer's copy/paste is row-less, a re-save keeps it row-less and leaves designer rows unchanged, and the row-less element runs. EM5 checks what a designer re-save does to package-written rows |
| `ProcessSchemaParameter.SourceValue` setter (keeps the BK15 row's Source in step) | TC | public | RequestBody bound after its row exists, so GT1 = L8 as in Gemini | **reuse via the mapping service** | traced (g1 §c) |
| `WebServiceUserTaskImplementation` (run time) | TS | public | not called | **avoid; it constrains the shape** | `GetByName` on Success, StatusCode and Response throws when one is missing. Composite keys are re-mapped Name↔Tag through one flat `Dictionary.Add`, so a Name or Tag that repeats at any depth throws (r3 §4, g3 §6; traced). A composite parameter without `L18` sends nulls or defaults and receives empty items (r3-C6 verdict). Under EP option E4 (Q17, no echo endpoint) EM6 cannot show the request side, and its response read needs a JSON source with an object array, which E4 lacks; the core run-time tests (section 5.4) carry this |
| `ProcessSchemaWebService.AnalyzePackageDependencies` / `PackageElementDependencyApplier` | TC / Nui | public | the dependency on a service in another package | **avoid** | The builder's `SaveSchema` path applies no dependency for any element (g5 §b, traced; the builder half of EM10 is still to do), so a notice is given instead (Q9). The designer adds the `SysPackageDependency` row (EM10 designer half, measured 2026-11-03) |
| `ServiceManagerService` / `DataService/json/SyncReply/ServiceSchemaRequest` | Nui | endpoint | the designer's service read | **avoid** | The designer's service read returns the service's authentication settings; the builder never calls it and never echoes it (g5 §c, traced) |
| `VwWebServiceV2` | ServiceDesigner SQL view | - | the designer's service picker | **avoid in the package** (use the manager) | It returns one row per `SysSchema` row with no type or active filter (g5 §a). The package resolves through the same manager the run time uses |
| `ProcessSchemaManager.SaveSchema` / `DesignSchema` | TC | public, non-virtual | save and load | **existing E2E boundary** | Unchanged. On save, `AnalyzePackageDependencies` also reaches the service manager (r8 §1.1) |

## 3. (B) crt-process-builder components

Verdict key:
- **as-is**: no change.
- **extend**: an edit to an existing file.
- **pattern-only**: the shape is copied into new code and the file itself is not touched.

### 3.1 Production code

| Path | What it does | Verdict | Why |
|---|---|---|---|
| `PB/EntryPoints/WebService/ProcessDesignService.cs` | 8 WCF operations | **as-is** | Build, Modify, ModifyAsNewVersion and Describe carry a new block unchanged. Adding no operation keeps clio's pins of 8 operations and 6 gates (r4 H9, verdict confirmed; Q10) |
| `Files/src/CrtProcessBuilderApp.cs` (composition root) | `AddScoped<IProcessElementHandler>` per handler | **extend** | Add the handler and the scoped registrations of design §4.7: catalog (C2), shaper and parameter writer (C3), platform-sync seam (C4), applier (C7), result-mapping writer (C8) and describer (C10). No platform-capability seam and no feature reader (dropped by Q3 and Q7, 2026-11-03). `CanBuild` is type-keyed (`element is ProcessSchemaWebService`), so the order among handlers does not decide anything here |
| `PB/Elements/IProcessElementHandler.cs`, `ProcessElementHandlerBase.cs` | the element strategy | **as-is** (base class) | `SupportedTypes`, `Create`, `Configure`, `CanBuild`/`CanDescribe`/`Describe` |
| `PB/Elements/ProcessElementFactory.cs` | derives the type tokens; `EnsureBlockMatchesHandler` | **extend** | Add one strict gate line (`webService` block ⇔ `webService` type), as for `subProcess`/`scriptTask` (r4 §1) |
| `PB/ProcessDesignConstants.cs` `ElementTypes`, `TaskWidthPx`/`TaskHeightPx` | type tokens and size | **extend** (token) / **as-is** (size) | The `webService` token. The corpus `BN2 = "69;55"` equals the task size (measured, r4 §1) |
| `PB/Elements/SubProcessElementHandler.cs` | own-class handler | **pattern-only** | Stamp `ManagerItemUId`. Refuse a create with no block (`EnsureCalledProcessNamed`). In `Describe`, fall back to the raw UId when the target is missing |
| `PB/Elements/SubProcessApplier.cs`, `ISubProcessApplier.cs` | external-schema resolution and sync | **pattern-only**, plus **extend** for the diff helper | The patterns: resolution by UId, then name, then caption with an ambiguity refusal; `SubProcessApplyMode` Create/Update; `EnsureNoLiveDependents` through the snapshot (Q11); a before/after `Snapshot`+`Diff`. `Snapshot`/`Diff` are private and typed to `ProcessSchemaSubProcess` (r4 §2). Lift them to a helper over `ProcessSchemaParametrizedFlowNode`, with the sub-process tests as the guard, or write a WS-local diff (~80 lines) if lifting would touch reviewed sub-process behaviour |
| `PB/Elements/SubProcessReader.cs`, `ISubProcessReader.cs` | `FindItemByNameOrNull`, confirms the spelling | **pattern-only** | The shape of `IWebServiceCatalog`. Unlike sub-process, the refusal lists every valid name (codes with captions, Q9, Q10), because the lists are tiny: 32 services in the whole PackageStore corpus, at most 7 methods each (g5 §c); stand `Creatio` has 3 service schemas, all REST (measured 2026-11-03) |
| `PB/Elements/SubProcessSyncReport.cs`, `SubProcessSyncNotices.cs` | Added/Removed/Retyped/DanglingReferences, and their notices | **pattern-only** | The notice listing dropped bindings on a method or service change (Q11), and the drift notices (Q12) |
| `PB/Elements/PreconfiguredPageParameterSync.cs`, `PreconfiguredPageSyncNotices.cs` | a server port of a client sync algorithm | **pattern-only** | The discipline the port follows: mirror the JS rule by rule, stay idempotent, keep a read-only `IsInSync` twin for describe, and never delete on an unreadable source (r4 §2) |
| `PB/Parameters/ProcessParameterDataValueType.cs` (`Assign`) | sets a parameter type from a DataValueType object | **as-is** | Used by the parameter writer |
| `PB/Elements/UserTaskElementHandler.cs` | the generic `userTask` route | **extend** | Refuse the user task `WebServiceUserTask` (e7acb2e5), keyed on the resolved schema UId and pointing to the `webService` block, in the same way as `RefuseUnconfigurablePageTask` (Q1). Today that user task builds without a refusal (r4 H2) |
| `PB/Design/ProcessBuildHandler.cs` (`SelectReferencedSources` hook), `PB/Graph/ProcessGraphBuilder.cs` (place → hook → configure → flows) | build ordering | **extend** (one branch) | Resolve the service, write IX1/IX2 and generate the parameters in the post-placement hook. Then `mappings[]` and other elements' blocks in the SAME request can reference `<svc>_In`/`_Out` (Q10). Sub-process and Pre-configured page already do this |
| `PB/Operations/ElementOperations.cs` (`addElement`, `setElement`) | modify operations | **extend** | `addElement` applies the block. `setElement` applies replace semantics behind the retarget guard (Q11). The "no field set" refusal lists every block, so `webService` is added there |
| `PB/Contracts/ProcessDescriptorContracts.cs`, `ModifyContracts.cs`, `DescribeContracts.cs` | wire DTOs | **extend** (new `[DataContract]` types) | WCF `DataContractJsonSerializer` drops undeclared keys silently, so every member is declared (r4 §1). These names are what clio's key check mirrors (section 4) |
| `PB/Describe/ProcessDescriber.cs` and `ProcessParameterService.ToDescribeParameter` | the generic element and parameter projection | **extend** for the `webService` member, the element's parameter tree and the unbound-input filter | Measured 2026-11-03 on 1.6.6.131 (decisions Q14, the package's raw `DescribeProcess` on a designer-built element): every output comes back with its `tag` and nested `itemProperties` (r9-S6 holds); bound inputs only (unbound tagged inputs and an unbound `RequestBody` omitted, M-31) and no service, method, timeout or ignore-errors member. A reply without tags or nesting seen the same day came from an older MCP clio build. Gaps for Q14: (1) the filter at `ProcessDescriber.cs:226` omits unbound inputs, such as a header parameter or an unbound RequestBody; (2) no `webService` member; (3) nested items report `isOutput=false` even when their direction is Out (r9-S6 side fact). All three are fixed for WS elements by the read of the stored tree (Q14). Describe reads the synced view (EM11), so the tree it shows is what the platform sync left |
| `PB/Mappings/IProcessMappingService.cs` (`ApplyMapping`, `BindCollection`, `BindCollectionItems`), `PB/ProcessSchemaElementLocator.cs` (dotted paths at any depth), `PB/Mappings/CollectionBindingRules.cs` (P1), `PB/Mappings/ParameterTypeCompatibility.cs` | inputs in, outputs out (Task 6, Q2/Q5/Q6) | **as-is** | All three corpus shapes come out byte-identical with no rules change (g3, traced write, measured target): (a) an item-level IN bound from a scalar, a formula or a constant with the parent left None; (b) COL item ← COL item; (c) a declared Collection ← the Gemini COL/COL output. Limits that stay, and that the guidance must state: COL↔ObjectList is refused (exact UId, g3 M5); a source deeper than its target is refused (M4); a formula item whose parent is unbound gets no notice (g3 §3) |
| `PB/Mappings/ProcessMappingService.cs` (`Commit`) | assigns `SourceValue` | **as-is** | Binding RequestBody after its BK15 row exists gives Gemini's GT1 = L8 with no extra code (g1 §c) |
| `PB/Parameters/ProcessParameterService.cs` (`CloneItemProperties`, `NormalizeParameterTypeName`, `MaxCollectionLevels = 2`) | declared and mirrored process parameters (Task 5) | **as-is** for a declared Collection (v1, Q6); **extend** with one WS-specific refusal | Today a `typeFromElement` mirror of a WS CompositeObjectList output is refused with a misleading "no column identity" message, and a mirror of a WS ObjectList output is accepted silently as an itemless, unbound parameter (r4-C7 verdict). v1 refuses both with a pointer to a declared Collection. Relaxing the mirror, declaring ObjectList and allowing 3+ levels go to ENG-92730 Complex / structured parameter types (Q6) |
| `PB/Validation/ProcessSchemaValidator.cs` | the fail-closed pre-save gate | **as-is** | Runs for every element kind |
| `PB/Design/IProcessDesignNotices.cs`, `PB/Elements/IElementConfigurationLedger.cs`, `ElementConfigurationLedger.cs` | end-of-request notices and the "cannot run" ledger | **extend** | New notices: unresolved service or method, cross-package service (Q9), the nested-list notice N5 for builds before ENG-55920 Unable to use array inside array in web-service business process element (Q13), latent loss (Q12), dropped bindings (Q11) |
| `PB/Graph/IProcessDependencySnapshot.cs` (`Capture`, `FindDependents(element, survivingParameterUIds, includeOwner)`) | who reads an element's outputs: another element or a process parameter (the package's `ProcessElementDependencyScanner` walks `schema.Parameters` too) | **as-is** | The retarget guard (Q11 A3), as used for sub-process (ENG-100041 Sub-process retarget guard scans the live schema instead of the request dependency snapshot) |
| `PB/Graph/ProcessGraphBuilder.cs` `RemoveElement` | deletes BK15 rows whose `TargetMetaPath` holds the element UId | **as-is** | Removing an element already cleans its WS rows (g1 §c) |
| `PB/Graph/ProcessElementDependencyScanner.cs` | reads `SourceValue`, never the rows | **as-is** | The BK15 rows stay invisible to it, as they should |
| `PB/Schema/ProcessSchemaRepository.cs` (`LoadForDescribe`, save) | load and save | **as-is** | The instance it returns has already run the platform sync (g4 §2). Drift is therefore detected against the catalog, not against the stored bytes (Q12). Measured 2026-11-03: describe showed the pruned element (EM11), and one unrelated modify through the current builder deleted the 8 tagged params of an unresolved element with no warning (EM8, current-builder half) |
| `PB/SafeText.cs` (`Sanitize`) | text safe for messages | **as-is** | Service and method names in refusals |

### 3.2 Tests and documentation

| Path | What it does | Verdict | Why |
|---|---|---|---|
| `PBT/FileProcessingCaptureOracle.cs`, `FileProcessingParityTests.cs`, `Fixtures/FileProcessing/PROVENANCE.md` | verbatim captures, SHA-256 provenance, a projection compared under an explicit rule | **pattern-only** | Becomes `WebServiceCaptureOracle`, `WebServiceParityTests` and `Fixtures/WebService/` (section 5.5) |
| `PBT/SendEmailAttachmentsStandCaptureParityTests.cs`, `Fixtures/SendEmail/PROVENANCE.md` | parity against a stand capture | **pattern-only** | For the EM2-EM4 stand captures (pattern B, committed redacted; EM2 is the AC5 oracle; serialization §8.1) |
| `PBT/EntryPoints/WebService/ProcessDesignServiceWireContractTests.cs` | pins `DataMember` names and the operation count | **extend** (names) / **as-is** (count) | New member names are pinned. The count stays the same under Q10 |
| `PBT/CiContractGuardTests.cs` | every fixture has `Category("UnitTests")`/`"PreCommit"` | **as-is** | New fixtures follow it |
| `PBT/CrtProcessBuilderAppTests.cs`, `ProcessDesignTestSupport.CreateGraphBuilder`, `BaseComposableAppTestFixture.CreateProcessOperations` | composition-root tripwires and the shared construction sites | **extend all three** | A new collaborator injected into `AddElementOperation`/`SetElementOperation` breaks every operation fixture until all three sites know it (r8 §4) |
| `PBT/SubProcessTestSupport.cs` (`SetupSchemaManager`) | substitutes `SchemaManagerProvider.GetManager(string)` | **extend** | Route `GetManager("ServiceSchemaManager")` and `GetManager("ProcessSchemaManager")` separately, never through `Arg.Any<string>()`. Keep the "a miss answers null" defaults (r8 §4) |
| `PBT/SubProcessPlatformProbeTests.cs` | measures platform behaviour inside the unit harness | **pattern-only** | Becomes `WebServicePlatformProbeTests` (section 5.2) |
| `PB/FileProcessing/IReportTemplateReader.cs` + `PBT/FakeReportTemplateReader.cs` | an internal reader seam with a `Standard()` fake and a `Reads` counter | **pattern-only** | The shape of `IWebServiceCatalog` and `FakeWebServiceCatalog` |
| `PBT/PreconfiguredPageParameterSyncTests.cs` | tests of the client-algorithm port | **pattern-only** | The style of the shaper tests |
| `docs/<element>-capture.md`, `docs/process-builder-architecture.md` §3.5 | one capture document per element, and a list of every handler | **pattern-only** / **extend** | A new `docs/web-service-element-capture.md` and the §3.5 row |

Not reused, with the reason:

| Item | Reason |
|---|---|
| `SynchronizeParameters()` as the generator | The shape is not the designer's (§2) |
| The sub-process setter-triggered sync | There is no setter for WS; the sync runs only at load |
| The `typeFromElement` mirror for WS outputs in v1 | Q6 |
| A new package discovery operation | Q10 |
| A feature-reader seam over `Features.GetIsEnabled` (precedent `IFileProcessingFeatureReader`) and a reflection capability seam for IX4 | not needed: no `L18` gate (Q3) and a direct IX4 write (Q7), 2026-11-03 |

## 4. (C) clio and clio-knowledge

clio passes element blocks through, but these guards and surfaces have to move. K numbers are design §6 (= plan CL-n).

| Item | Path | Verdict | Note |
|---|---|---|---|
| Pre-send key check (refuses undeclared keys) and its drift tests | `CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs`; `clio.tests/Command/ProcessModel/ProcessBlockKeyCheckTests.cs` (`KeySets_Should_EqualTheMembersOfTheBundledServerContracts`, `ReadBackFields_Should_BeDescribeOnlyMembers`) | **extend** (K2) | A `WebServiceBlock` key set plus the read-back keys. It mirrors the package DTOs in §3 |
| The short-form call shape | `CLIO/clio/Command/McpServer/Tools/ToolContractShortForm.cs` `CreateElementBlockShape` | **extend** (K3) | `"webService"`. The list is never cut (r5 §2) |
| `[RequiresPackage]` floor | `CreateBusinessProcessCommand.cs`, `ModifyBusinessProcessCommand.cs`, `ModifyProcessAsNewVersionCommand.cs`, plus the floor lists in the descriptions | **extend** (K4) | The literal becomes the cut claimed in the claim comment. `webService` joins both floor-reason lists |
| Tool descriptions (create, modify, describe, validate, list-user-tasks) | `CLIO/clio/Command/McpServer/Tools/ProcessDesigner/*.cs`, `ListUserTasksTool.cs` | **extend by swap** (K5; Q15) | Full tool contracts (not description lengths) at `6be5dde28`: create 39 B, modify 3 B and describe 43 B of headroom under the 35,072 B `MaxToolContractSerializedBytes` ceiling (r5-C3 verdict, measured; re-measured on the branch, Q15). Each tool gets one pointer clause (`get-guidance name=process-web-service`). ASCII only; non-ASCII, apostrophe, double quote, `<`, `>`, `&`, `+` and backtick cost 6 B each. Cite ENG-92719 File processing element Q20 as the precedent (ENG-95985 Send email attachments Q12) |
| The data-id arm | `CLIO/clio/Command/ProcessModel/Schema.cs` (`"webservice" => EventType.WebServiceTask`, Activity) | **as-is** | The `webService` token already validates as an activity in `validate-process-graph` (r5 §1.3) |
| Typed describe | `IProcessDescriber.cs` (`DescribedWebService` plus children, each with its overflow bag) | **extend** (K7) | Per `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md` |
| Rebundle pins and security counts | `clio/CrtProcessBuilder/*.gz`, `clio.tests/Common/BundledProcessBuilderPackageTests.cs` | **extend** (pins) / **as-is** (counts 8 and 6) (K1; Q10) | The counts move only if an operation is added |
| Guidance-name fixture | `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` | **extend** (K8) | Re-pinned to the KB generation that publishes `process-web-service`. The KB is published before clio leaves draft (Q19) |
| Unit and E2E models | `clio.tests/Command/McpServer/{Create,Modify}BusinessProcessToolTests.cs`, `DescribeProcessToolTests.cs`, `ServerProcessDescriberTests.cs`; `clio.mcp.e2e/SubProcessElementToolE2ETests.cs`, `ProcessFileElementToolE2ETests.cs` | **pattern-only** | Becomes `WebServiceElementToolE2ETests` (K9) |
| `WebServiceManager.GetAllServices()` / `VwWebServiceV2` model / `get-webservice-url` | `CLIO/clio/Common/WebServiceManager.cs`, `clio/CreatioModel/VwWebServiceV2.cs` | **avoid** | It reads every `MetaData` blob, secrets included. Its `ServiceSchemaRequest` route is hard-coded rather than a `KnownRoute`. It has no MCP tool and decodes no methods (g5 §c). A clio discovery tool is out of v1 (Q10) |
| KB element catalog | `KB/guidance/mcp/guides/processes/element-catalog.md`, `element-catalog-details.md:70-71`, `bundle-source.json`, and the KB test pinning "READ-ONLY here" | **extend** | Flip `webService` to buildable, with a version-floor pin (Q15) |
| KB `process-modeling` | `process-modeling.md:182-185` | **extend by swap** | About 13 B of reply budget is left (r5 §0) |
| A new KB article `process-web-service`, plus a routing line separating it from the configuration-webservice (C# service) guides | `KB/guidance/...`, routing article, `libraryVersion` bump (the `sequence` is derived from it; no authored `sequence`, plan KB-4) | **new** | Q15 |

## 5. (D) Test seams and fakes

### 5.1 Seams

Folder: `PB/WebService/`, as in design §4.1 (the single source for paths and component ids C1-C14). It is a
sibling of `PB/EntryPoints/WebService/`, the package's own WCF entry point, not part of it. Every interface is
`internal`, is registered in DI, and has a substitute or a fake in tests.

```csharp
// The only class that touches ServiceSchemaManager. Prod: UserConnection.GetServiceSchemaManager();
// FindItemByUId before GetInstanceByUId; case-insensitive scans; canonical names out (Q9).
internal interface IWebServiceCatalog {
    IReadOnlyList<WebServiceInfo> List();                       // names for refusals; never DefValue/AuthInfo (Q16)
    WebServiceInfo TryFind(Guid uid);                           // null = dangling; describe never throws
    WebServiceResolution Resolve(string serviceRef);            // UId -> name -> caption; ambiguity -> refusal
    WebServiceMethodInfo TryFindMethod(Guid serviceUId, string method); // case-insensitive; Name is canonical
}
internal sealed record WebServiceInfo(Guid UId, string Name, string Caption, ServiceType Type, Guid PackageUId);
internal sealed record WebServiceMethodInfo(string Name, string Caption,
    IReadOnlyList<WebServiceParameterInfo> Request, IReadOnlyList<WebServiceParameterInfo> Response);
internal sealed record WebServiceParameterInfo(string Name, string Caption, string DataValueTypeName,
    bool IsArray, bool IsRequired, ServiceParameterType Placement, IReadOnlyList<WebServiceParameterInfo> Items);
    // DefValue is absent by construction: a default can hold a Bearer header or a password (g5 §c)

// C3. Pure port of process-webservice-schema.js (Q3): no UserConnection, no manager, no platform mock.
// No feature input: L18 and the ItemValue child are always generated (Q3, no ProcessParameterCollections gate).
internal interface IWebServiceParameterShaper {
    WebServiceParameterPlan Shape(WebServiceMethodInfo method, ShapeContext context);
}
internal sealed record ShapedParameter(string Name, string Tag, Guid DataValueTypeUId, bool IsOut,
    bool IsRequired, string Caption, IReadOnlyList<ShapedParameter> Items);   // Items = L18, ItemValue child
internal sealed record WebServiceParameterPlan(IReadOnlyList<ShapedParameter> Tagged,
    IReadOnlyList<ShapedParameter> Result);                                     // Success, Response, StatusCode, RequestBody

// C3. Materialises a plan on an attached element: UIds, A3=A4=process UId, IL2 on every level, L12,
// Value "false" on every Boolean, Success included (L8 {"GS2":"false","GS5":<P>}; serialization 2.4, I7),
// caption resources.
internal interface IWebServiceParameterWriter {
    void Write(ProcessSchema process, ProcessSchemaWebService element, WebServiceParameterPlan plan);
}

// C8. One BK15 ProcessSchemaMapping row per result parameter (Q4; SourceValue first, row second).
internal interface IWebServiceResultMappingWriter {
    void Write(ProcessSchema process, ProcessSchemaWebService element, WebServiceParameterPlan plan);
}

// IX4 needs no seam: the applier and the describer set and read element.IsIgnoreErrors directly (Q7, EM9).

// C4 (design 4.5). Prod: element.SynchronizeParameters(). Production code does not call it in v1 (the load path runs
// the sync anyway); the seam exists for the platform probe (section 5.2).
internal interface IWebServicePlatformSync { void Synchronize(ProcessSchemaWebService element); }
```

The test doubles:
- `FakeWebServiceCatalog.Standard()` follows the `FakeReportTemplateReader` shape: a fixed catalog with a `Reads`
  counter, a dangling UId, two services whose names differ only in case (for the ambiguity refusal), a SOAP service
  (Q20), and a service in another package (Q9); an empty catalog covers the "no registered service" refusal (Q10).
- No capability or feature fakes: the IX4 guard and the `ProcessParameterCollections` gate are dropped (Q7, Q3), so
  no test fakes a missing `IsIgnoreErrors` or a feature turned off.

### 5.2 Harness facts (r8-C2 verdict, traced and checked against the shipped binaries)

- `ProcessSchemaWebService`'s ctor calls `CoreApiContainer.Resolve<IProcessSchemaElementParametersProvider>()`. That
  call swallows exceptions and returns `null`, and the package harness starts with an empty `ServiceCollection`.
  Constructing the element therefore never throws, but `SynchronizeParameters()` NREs. The production path does not
  depend on this, because Q3 does not call the platform generator.
- The provider can still be bound for a probe by reflection, as long as it is bound BEFORE the element is
  constructed (the ctor captures it):

```csharp
var t = Type.GetType("Terrasoft.Core.Process.IProcessSchemaElementParametersProvider, Terrasoft.Core");
TestDIUtilities.Configure(s => s.AddSingleton(t, Substitute.For(new[] { t }, new object[0])));
// or the real internal Terrasoft.Services.ProcessSchemaWebServiceParametersProvider;
// Castle may proxy it: both assemblies grant InternalsVisibleTo DynamicProxyGenAssembly2
```

- `WebServicePlatformProbeTests` records one probe, on both net472 and net8: the platform sync run over a
  shaper-written element. Its expected result is "keeps L18, result parameters and UIds". If the probe shows a change,
  for example to captions, Q3's "tolerated afterwards" claim is corrected before the applier relies on it.

### 5.3 Platform doubles (traced, r8 §2-§3)

```csharp
var schema = new ServiceSchema(Substitute.For<ISchemaManager>()) {          // constructible bare
    ServiceMethods = { new ServiceMethod { Name = "Echo", Request = { Parameters = { /* ServiceParameter tree */ } } } }
};
var manager = Substitute.ForPartsOf<ServiceSchemaManager>();                  // lookups are virtual
manager.FindItemByUId(uid).Returns(item);  manager.GetInstanceByUId(uid).Returns(schema);
manager.FindItemByUId(Arg.Any<Guid>()).Returns((ServiceSchemaManagerItem)null); // a miss answers null
((TestUserConnection)UserConnection).SetServiceSchemaManager(manager);       // Libs/UnitTest.dll
// GetServiceSchemaManager() casts GetSchemaManager("ServiceSchemaManager"). If the fixture's connection is
// not a TestUserConnection, substitute that call instead (not verified which one ProcessDesignTestSupport builds)
```

Open, settled by the first probe:
- Core tests wrap the `ServiceSchemaManager` substitution in `#if NETFRAMEWORK` (CRM-44720, now ENG-4179 "(not_mvp) [RFMT] Adapt
  Terrasoft.Services", English rendering of the Russian Jira title), so whether it works
  under the package's net8 build is unknown.
- Whether `MetaDataTestUtils.ReadMetaData` can read a PackageStore service capture into a `ServiceSchema` is unknown
  (r8 B6). If it cannot, the parity service schemas are rebuilt in code and guarded by the capture hash.

Two more notes:
- IX1-IX4 serialization is round-tripped with `MetaDataTestUtils.ApplyMetaData`/`GetMetaData` (public in the shipped
  `Libs/Terrasoft.TestFramework.dll`).
- Use `UserConnection.DataValueTypeManager` from `BaseComposableAppTestFixture`, not a bare `new DataValueTypeManager()`.

### 5.4 Case tables to copy

Copy the cases, not the base classes. The base classes are core-only, internal or in assemblies that are not shipped.

| Source | Cases |
|---|---|
| `CORE/Terrasoft.Services.Tests/ProcessSchemaWebServiceParametersProvider.Tests.cs` | the type matrix (array → ObjectList / CompositeObjectList, unknown/Text → MaxSizeText); the IsRequired matrix; the empty-result guards; Name, Tag, ContainerUId and A3/A4 asserts. The top-level oracle for the shaper |
| `CORE/Terrasoft.Core.Tests/Process/ProcessSchemaWebService.Tests.cs` | the sync semantics: keep untagged, remove tagged non-actual, update existing. Feeds the probe and the Q12 drift tests |
| `CORE/Terrasoft.Services.Tests/WebServiceUserTaskImplementation.Tests.cs` | the run-time shape: Tag as key, nested `<tag>_Out` keys, a nested In with Source None skipped, RequestBody override, the result parameters. Feeds the EM6 expectations, and under EP option E4 (Q17) it is the evidence for the request side and the nested-list case (EM12) that stand `Creatio` cannot show |
| `CORE/Terrasoft.Services.Tests/PetstoreServiceSchema.cs` | a REST schema with a response array with nested arrays and a full request body. The model for a package `ServiceSchemaFixtures` that mirrors the EM1 service `UsrWscEchoService` |
| `CORE/.../process-flow-element-schema-manager/tests/process-webservice-schema.unit.spec.js` | the client's own asserts (createdIn and the nesting). The cases for the port's L18, ItemValue and caption-suffix rules (g1) |
| `<UnitTests>`: `Base.UnitTests/CreatioMarketplaceApiClient_Tests.cs`, `ServiceDesigner.UnitTests/CallServiceSchemaService_Tests.cs`, `ServiceSchemaParameterBuilder_Tests.cs` | the substitute recipes (`For` and `ForPartsOf`) and pure nested-map tests |

### 5.5 Parity oracle (AC5)

Corpus pairs, each a process capture plus the service capture it points at (r8 B5, verdict confirmed; g1 §c):

| Pair | Covers |
|---|---|
| CrtBase `CheckMarketplaceAppNeedUpdateSubProcess` + `CreatioMarketplaceApi` | flat; Timeout 600000; four BK15 rows; a stale row `A2` after a rename |
| CrtGeminiApiIntegraion `ActionGeminiSearch` + `GeminiApiSearch` | composite Out nested to the ObjectList third level; RequestBody bound by a formula; four rows with GT1 = L8 |
| StudioFreeUpdate `ExecuteUpgradeBsf` + `teamcitybpmonline_BSF` | composite In bound at item level; three rows; no RequestBody (an older save) |

The comparison rule is WSCR, [serialization](eng-92710-call-web-service-element-serialization.md) §8.2 (W1-W14), with
the allowed differences of §8.3 (AD1-AD5 and AD10; AD6 dropped by EM0), in the Process file "TC-41" style: UIds are removed (W2), the stale row
`A2` is AD2 and the older-save absences are AD5. Result parameters are not required to come last for re-synced
elements (W9): 9/66 elements append a later service parameter after them (g1 §a). The six capture files are committed
byte for byte under `PBT/Fixtures/WebService/` with a `PROVENANCE.md` (SHA-256 as the last cell) and a `.gitattributes`
`-text` rule. Their resource files are not committed: resources are compared on the stand only (W13).

Before any service capture is committed verbatim, check its IQ5 and IU5: 3 corpus services carry literal basic-auth
credentials; 1 product service carries literal bearer header defaults (g5 §c). Such a file is either rebuilt in code
or replaced by another pair (Q16). The three pairs above carry no credential (TC-P-08).

The authoritative AC5 oracle is the stand capture EM2 on `UsrWscEchoService` (EM1); EM3 and EM4 are further pattern-B
captures, and EM5 is a re-save diff, not a fixture. EM2-EM4 are committed redacted under `PBT/Fixtures/WebService/Stand/`,
and WSCR leaves the redacted keys out of the comparison (W14; serialization §8.1). EM1 is done (2026-11-03, M-19,
M-23): package `Custom`, base URI = the stand root (EP option E4), three services: `UsrWscEchoService` (`UsrWscEcho`,
`UsrWscGet`, `UsrWscMissing`; the header `UsrWscProbe` (`X-Wsc-Probe`) has the non-secret default Const `wsc-probe`,
Q16, Q17), `UsrWscDriftService` and `UsrWscDeadService`.

## 6. (E) Genuinely new code

Rough authored sizes, not counting the verbatim capture data. They are a floor: on ENG-95985 the authored lines came
out at 20.4k against 9.8k planned (r10, measured). Time is in
[estimate](eng-92710-call-web-service-element-estimate.md).

| New piece | Where | Rough size (src / tests) | Notes |
|---|---|---|---|
| Contract DTOs: `webService` write block, update block, describe member and its issues | `PB/Contracts/*` | 120-180 / covered by wire and key tests | Q2, Q14 |
| `WebServiceElementHandler` (create, configure, describe, IX4 read-back) | `PB/Elements/` | 150-220 / 200-300 | Sub-process shape |
| `WebServiceCatalog` (prod) + `FakeWebServiceCatalog` | `PB/WebService/`, `PBT/` | 150-220 / 150-200 | Q9, Q10: resolution, canonical spelling, refusals that list codes with captions and point to Studio > Web services, the "no registered service" refusal, SOAP and cross-package detection |
| `WebServiceParameterShaper` (the port) | `PB/WebService/` | 250-350 / 400-600 | Q3: L18 recursion (always, no feature gate), ItemValue child, type map, IsRequired, the conditional caption suffix, the four result parameters. Most cases are pure |
| `WebServiceParameterWriter` (C3: UIds, stamps, caption resources, Success value) | `PB/WebService/` | 90-130 / 110-180 | Q3 |
| `WebServiceResultMappingWriter` (C8: the four BK15 rows) | `PB/WebService/` | 30-50 / 40-70 | Q4. These are the first BK15 rows the package authors itself |
| `WebServiceApplier` (Create/Update, replace semantics, retarget guard, dropped-binding report) | `PB/WebService/` | 250-400 / 400-600 | Q11. Plus the lifted or local snapshot/diff helper (~80) |
| Drift and issue detection for describe and modify (serviceResolved, methodResolved, nested drift, latent loss) | `PB/WebService/`, `PB/Describe/` | 120-200 / 200-300 | Q12, Q14 |
| Notices (unresolved, cross-package, nested-list N5, latent loss, dropped bindings) | `PB/Design/`, `PB/Elements/` | 80-150 / covered above | Q9, Q11-Q13 |
| Glue: factory gate, `ElementTypes` token, DI, build-hook branch, `ElementOperations`, `WebServiceUserTask` refusal, WS mirror refusal, describe filter and direction fix | existing files | 100-180 / 150-250 | §3 "extend" rows |
| `WebServiceCaptureOracle`, `WebServiceParityTests`, `WebServicePlatformProbeTests`, fixtures with `PROVENANCE.md` | `PBT/` | - / 400-600 (+ verbatim data) | AC5 |
| Package documents: `docs/web-service-element-capture.md` and the architecture §3.5 row | `docs/` | 250-400 prose | |
| clio: key sets, short form, floor, typed describe, description swaps, prompts, MCP e2e | `CLIO/` | 400-800 / 1,500-3,000 | ENG-95985 delivered 709 src and 3,046 tests (r5-C16 verdict, measured) |
| KB: the `process-web-service` article, catalog flip, routing line, tests, `bundle-source.json` | `KB/` | 300-750 | ENG-95985 delivered 712 (r5-C16 verdict) |

Package total, the sum of the package rows above (contracts to glue, plus the test-only oracle row): about 1.34k-2.08k
src lines (+ ~80 for the snapshot/diff helper) and 2.05k-3.1k test lines. The C11 capability seam and guard
(30-50 / 60-100) left the table with Q7 (2026-11-03); the feature reader never had its own row. The mapping engine
adds nothing. The composite half grows by about 15 h if the mirror relaxation, ObjectList or a third collection level
enters scope (g3 §5); those go to ENG-92730 (Q6, agreed 2026-11-03).

Sized figures: [estimate](eng-92710-call-web-service-element-estimate.md) §2.1 (src 3.15/4.6/7.15k, tests
4.75/7.8/12k) supersede this floor.
