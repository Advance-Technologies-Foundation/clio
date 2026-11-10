# Facts: ENG-92710 Call web service element

Written 2026-11-02, updated 2026-11-03. Nothing was built, saved or run for the 2026-11-02 rows.
Rows marked "measured 2026-11-03" come from the owner-approved stand probes and the local compile run (EM9) of that
day; each points to the decisions section that records it in full
([decisions](eng-92710-call-web-service-element-decisions.md) Q4, Q6, Q7, Q9, Q10, Q11, Q12, Q14, Q16).

**Summary.** What is true today, before any code: the element's identity, its stored keys, what the classic designer
writes, what the platform server does on load and save, what the run time does with the stored shape, what
CrtProcessBuilder, clio and the guidance library do with the element now, what stand `Creatio` holds, and which
statements in Jira and Confluence are stale. This is the evidence base of the plan. Every row carries a tag; where a
research note said something else, the adversarial verdict (or a later gap note) wins and the difference is listed
under [Corrections to the research](#corrections-to-the-research).

Sibling documents: [README](README.md) · [usage](eng-92710-call-web-service-element-usage.md) ·
[reuse](eng-92710-call-web-service-element-reuse.md) ·
[serialization](eng-92710-call-web-service-element-serialization.md) ·
[design](eng-92710-call-web-service-element-design.md) ·
[decisions](eng-92710-call-web-service-element-decisions.md) ·
[measurements](eng-92710-call-web-service-element-measurements.md) ·
[plan](eng-92710-call-web-service-element-plan.md) · [test plan](eng-92710-call-web-service-element-test-plan.md) ·
[estimate](eng-92710-call-web-service-element-estimate.md).

**Basis.**
- crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131 at that commit, history).
- clio `origin/master` `6be5dde28` (the ENG-95985 Send email attachments merge).
- clio-knowledge `origin/master` `5f14ceb`, `libraryVersion` 1.15.176.
- CrtProcessDesigner 7.8.0 and the PackageStore corpus (`branches/7.8.0`).
- Core: `<core>` trunk `468c4c71b` (2026-01-24, tag `builds-windows/8.3.3.1722`) is the reference for
  every current-core fact. The `<Creatio2>/TSBpm` checkout (trunk `8f6745caa`, 2025-07-21) is STALE: it
  predates `IsIgnoreErrors`/`IX4` and the ENG-55920 Unable to use array inside array in web-service business process element fix. Line numbers that the r-notes cite from it are marked `TS25`.
- Shipped core binaries measured by metadata (g2): 8.1.3.6947, 8.3.1.4498, 8.3.2.4199, 8.3.4.2753 (the CI
  reference), 10.0.0.950, 10.1.37 (the local `core-bin` of every package worktree), 10.1.830, 10.2.931.
- Creatio Academy pages read 2026-11-02 (r7).
- Stand `Creatio`: read-only inventory taken 2026-11-02 (r9): Creatio 10.1.37, .NET Framework, CrtProcessBuilder
  1.6.6.131; designer, run and describe probes on 2026-11-03 (decisions Q4-Q16).
- Research notes r1-r10 and gap notes g1-g5 in `<research notes, local>/` (local), and the
  verdict list in `_workflow-result.json` (63 verdicts: 21 confirmed, 42 partly). Cited as `(r6 §4.3)`, `(g4 §3)`,
  `(verdict r3-C7)`.

**Paths.** Every `prefix/path:line` is relative to the prefix.

| Prefix | Path | Revision |
|---|---|---|
| `PB` | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs` (read with `git show origin/main:`) | `da719366` |
| `PBR` | crt-process-builder repository root (`docs/`, `README.md`, `AGENTS.md`, `tests/UnitTests/CrtProcessBuilder.Tests` = `PBT`) | same |
| `PD` | `<PackageStore>/CrtProcessDesigner/branches/7.8.0` (`WSP` = `Schemas/WebServiceUserTaskPropertiesPage/WebServiceUserTaskPropertiesPage.js`) | 7.8.0 |
| `PS` | `<PackageStore>` | shipped corpus |
| `CORE` | `<core>/TSBpm/Src/Lib` | `468c4c71b` |
| `PWS` | `CORE/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-webservice-schema.js` (client element class) | same |
| `TS25` | `<Creatio2>/TSBpm/Src/Lib` (stale) | `8f6745caa` |
| `CLIO` | clio repository | `6be5dde28` |
| `KB` | clio-knowledge repository | `5f14ceb` |

**Tags.** [traced] read in source or metadata; for run-time behaviour a source trace is a hypothesis.
[measured] counted in corpus data, read from a shipped binary, observed on stand `Creatio`, or read in Jira/git on
2026-11-02, or on 2026-11-03 where the row says so. [doc] product or repository documentation. [inferred] my inference from the cited evidence. Section 10
names the measurement (EM id, see [measurements](eng-92710-call-web-service-element-measurements.md)) that settles
each open trace.

---

## Short answer

- **The element is not a user task.** It is the core flow element `Terrasoft.Core.Process.ProcessSchemaWebService :
  ProcessSchemaActivity` (run time `ProcessWebService`), ManagerItemUId `652b598d-cdd5-49d5-a86d-aafac88213f3`, edited
  by the page `WebServiceUserTaskPropertiesPage`. The CrtProcessDesigner user task `WebServiceUserTask` (`e7acb2e5`)
  is a legacy SOAP stub referenced by 0 processes, but the builder's generic `userTask` route would build it today
  (section 1) [traced] [measured].
- **Four element keys, defaults elided.** `IX1` ServiceSchemaUId, `IX2` MethodName (a string), `IX3` Timeout (int
  ms; the page edits seconds), `IX4` IsIgnoreErrors (core 8.3.2.3590 and later only); `BO2`/`BO3` true; parameters in
  `BP2` (section 2) [traced] [measured].
- **The designer client, not the server, makes the shape.** The client generates `<svc>_In/_Out` parameters with
  `L17` Tag = service parameter name, recursive `L18` items for object arrays, one `<Name>ItemValue_<dir>` child for
  scalar arrays, captions in resources, then four untagged result parameters (Success, Response, StatusCode,
  RequestBody) and one `BK15` mapping row per result parameter (section 3) [traced] [measured]. On a current 10.1.37
  designer the 4 rows come in the traced shape, a designer copy has none, and the row-less copy runs (3.5, measured
  2026-11-03, decisions Q4).
- **The platform's own sync is flat and runs on every design-time load.** `ProcessSchemaWebService.SynchronizeParameters`
  (public) builds only top-level parameters through an internal provider, matches by Name, removes tagged parameters
  the method no longer produces, appends new ones flat at the end, and never creates `L18`, result parameters or
  `BK15` rows. An unresolved method or service therefore prunes every tagged parameter on the next load, and a
  builder modify of ANY element saves that pruning silently: a latent loss already reachable on `origin/main`
  (section 4) [traced; measured 2026-11-03, EM8 current-builder half: an unrelated modify deleted the 8 tagged
  parameters of an unresolved element with no warning, decisions Q12].
- **The run time keys on Tag and on result-parameter names.** Success/StatusCode/Response are written with
  `GetByName` (throws if absent); a non-null `RequestBody` replaces the generated body; composite values are re-keyed
  Name<->Tag through `L18` when `ProcessParameterCollections` is on; a composite parameter without `L18` sends items
  rendered from the service tree with null leaves or service defaults, and receives N empty items; before the
  ENG-55920 fix (absent in 10.1.37, the stand), an inner object array whose container has no source of its own while
  its leaves are mapped is sent without that inner list (null), a mapped inner container is sent, and that such a
  process saves without error is not measured (section 5) [traced, not measured: EM6; the request bytes and EM12 are
  not measurable on stand `Creatio` with no echo endpoint (decisions Q17) and rest on the core unit tests and the
  ENG-55920 comment].
- **CrtProcessBuilder has zero web-service code.** Describe reports the element generically (`type
  ProcessSchemaWebService`, `buildType` null) and DOES return each parameter's `tag` and the nested `itemProperties`
  tree, but omits unbound inputs (tagged `_In` params, `RequestBody`) and has no service, method, timeout or
  ignore-errors member (verdict r9-S6; confirmed 2026-11-03 on an unmapped designer-built element, decisions Q14);
  the mapping engine already handles the three corpus composite
  shapes without change; the gaps are on the process-parameter side (typeFromElement mirror of a web-service
  collection refused, ObjectList not declarable, declared nesting capped at 2 collection levels) (section 6) [traced]
  [measured].
- **clio and the KB pass blocks through but four guards bite**, contract text budgets have 3-43 B of headroom, and
  the KB pins `webService` as read-only (section 7) [traced] [measured].
- **Stand `Creatio` has no designer capture of a composite request body**: 3 shipped service schemas, all REST, none
  with a Body parameter or an auth block (the "32 service schemas / 63 methods" figure is the PackageStore corpus, not
  the stand); 2 shipped processes with the element (section 8) [measured]. The designer works only with registered
  services: its list has no "new service" entry (3.1.6, measured 2026-11-03).
- **Jira's text is stale in four places**: Task 21 (is 22), Task 5 (composite types are Task 37 = ENG-92730 Complex / structured parameter types), the
  element identity implied by the state doc, and the estimate unit (section 9) [measured].

---

## 1. Element identity and the legacy `WebServiceUserTask`

| # | Fact | Evidence | Tag |
|---|---|---|---|
| 1.1 | The Call web service element is its own flow-element class `Terrasoft.Core.Process.ProcessSchemaWebService : ProcessSchemaActivity`; run-time class `ProcessWebService : ProcessActivity`. Both are in `Terrasoft.Core/Process`, not in `Terrasoft.Services`. | `CORE/Terrasoft.Core/Process/ProcessSchemaWebService.cs`; verdict r7-A14 correction 1 | traced |
| 1.2 | ManagerItemUId `652b598d-cdd5-49d5-a86d-aafac88213f3` = `ProcessSchemaElementManager.WebServiceTaskUId` (public static). | `TS25/Terrasoft.Core/Process/ProcessSchemaElementManager.cs:258` (r3 H1) | traced |
| 1.3 | Client class `Terrasoft.manager.ProcessWebServiceSchema` (alias `Terrasoft.ProcessWebServiceSchema`): `name "WebService"`, `typeName Terrasoft.Core.Process.ProcessSchemaWebService`, `editPageSchemaName "WebServiceUserTaskPropertiesPage"`, `isUserTask false`, `serializeToDB true`, `isLogging true`, `timeout 0`, `isIgnoreErrors false`; caption "Call web service" (`Terrasoft.Nui.resx:3907`). | `PWS:3-123` (r2 §1) | traced |
| 1.4 | Server default name prefix `WebService` (`[DesignModeClass(DefNamePrefix = "WebService")]`); BPMN name `CoreUserTaskName`. Corpus element names are `WebService1..N` unless renamed. | `ProcessSchemaWebService.cs:15, 96` (r2 §1) | traced, measured |
| 1.5 | All 66 corpus elements carry `BL1 = Terrasoft.Core.Process.ProcessSchemaWebService` and `BL7 = 652b598d-...`; both shipped processes on stand `Creatio` store the same. | r6 §1; r9 §3.1 | measured |
| 1.6 | `ProcessSchemaWebService` has no `(ProcessSchema)` constructor; its constructor sets `SerializeToDB = true`, `IsLogging = true`, `BpmnElementName`, and resolves `IProcessSchemaElementParametersProvider` from `CoreApiContainer`. A server-side creator must stamp `ManagerItemUId` itself, as `SubProcessElementHandler` does for sub-process. | `ProcessSchemaWebService.cs:44-57, 85-91` (r4 §3); `PB/Elements/SubProcessElementHandler.cs:64-75` | traced |
| 1.7 | `CrtProcessDesigner/Schemas/WebServiceUserTask` (UId `e7acb2e5-efc6-41d3-8c02-589ae6b943f1`, ProcessUserTaskSchemaManager, caption "Call web service") is a legacy SOAP user task: lookups to `WebService` (`34a72c74`) and `WebServiceURL` (`543e7919`), parameters ServiceUrl, ServiceMethod, RequestBodyInternal, Responce, IsIgnoreErrors and others; `InternalExecute` posts a hand-built SOAP 1.2 envelope with the hard-coded `webserviceX.NET` namespace. | `PD/Schemas/WebServiceUserTask/metadata.json:15-150`, `WebServiceUserTask.cs:28-104` (r2 §1, verdict r7-A14) | traced |
| 1.8 | No file in the PackageStore corpus references `e7acb2e5` except its own `metadata.json` and `descriptor.json`; no `SysProcessUserTask` data row registers it (25 `Data/SysProcessUserTask*` folders checked); on stand `Creatio` neither `list-user-tasks` (23 tasks) nor `SysProcessUserTask` (28 rows) lists it, though its `SysSchema` row exists. | r2 §1, r4 H2, r9 §3.1 | measured |
| 1.9 | Name collisions: the legacy user task's caption is also "Call web service", and the modern element's edit page is named `WebServiceUserTaskPropertiesPage`. The page in CrtProcessDesigner belongs to the MODERN element (`schemaManagerName ServiceSchemaManager`, REST and SOAP service types); only `Schemas/WebServiceUserTask` is legacy. | verdict r7-A14 correction 2 | traced |
| 1.10 | The builder's generic route would build the legacy task today: `UserTaskElementHandler` resolves any `userTaskName` through `ProcessUserTaskSchemaManager.FindInstanceByName` and refuses only PreconfiguredPage, AutoGenerated/UserQuestion and the FileProcessing variants. `{"type":"userTask","userTaskName":"WebServiceUserTask"}` is accepted (feeds Q1). | `PB/Elements/UserTaskElementHandler.cs:83-102, 353, 382, 410` (r4 H2) | traced |
| 1.11 | clio's process model already knows the element as an activity: `EventType.WebServiceTask`, `WebServiceTaskUId = 652B598D-...`, `ResolveDataId` maps the token `webservice` to it and classifies it `ProcessElementRole.Activity`, so `validate-process-graph` accepts a `webService` data id today. | `CLIO/clio/Command/ProcessModel/Schema.cs:794, 1006, 1123, 1178` (r5 §1.3) | traced |

---

## 2. Element keys, defaults, units and version boundaries

### 2.1 Keys

| Key | Property | Type | Default (not written) | MetaTypeProperty UId | Corpus (66 elements) | Tag |
|---|---|---|---|---|---|---|
| `IX1` | `ServiceSchemaUId` | Guid (the service's manager-item UId) | `Guid.Empty` | `34CD5A1C-C1A2-448F-8C71-EFCE2F3F67D4` | 66/66 | traced, measured |
| `IX2` | `MethodName` | string, the method **Name** (not a UId) | `""` | `30101DFC-414E-4755-805D-AE945693B2C1` | 66/66 | traced, measured |
| `IX3` | `Timeout` | int, **milliseconds** | `0` | `D5031394-A0C3-4011-9135-3206B151F5DA` | 6/66: 600000, 1000000 x2, 5000, 1000 x2; never stored as 0 | traced, measured |
| `IX4` | `IsIgnoreErrors` | bool | `false` | `517D88EF-18F1-4C79-9F79-19203F81F50B` | 2/66, both `true`, both BusinessRulesTest (UsrBPWithInvalidWebService, UsrTestWSInvalidCredsProcess, saved 2025-11-20) | traced, measured |
| `BO2` / `BO3` | SerializeToDB / IsLogging | bool | `false` (inherited flow-node writer) | - | `true` 66/66 (constructor default); a `false` is never written and reloads as `true` | traced, measured |
| `BP2` | Parameters | collection | always written | - | 66/66 | measured |
| `BP6` | MultiInstanceOptions | - | written only when not null | - | 0/66 (no multi-instance element anywhere) | measured |

Evidence: `CORE/Terrasoft.Core/Process/ProcessSchemaWebService.cs:16-32, 70-89, 135-136, 200-206`; `WriteValue(...,
default)` elision through `DataWriter.GetSkipDefValue`; `ProcessSchemaFlowNode.cs:20-21, 170-171`;
`ProcessSchemaActivity.cs:34, 41, 555-567` (verdicts r2-C2, r3-C2, r6-C2; g2). Other element keys observed on both
stand captures: `BL1, UId, A2, A3, A4, A5, IL2, BL3, BL7, BL8, BN2 "69;55"` (r9 §3.3); the package's task size
constants are 69 x 55 (`PB/ProcessDesignConstants.cs:2134-2135`, r4 §1).

### 2.2 Timeout

| Fact | Evidence | Tag |
|---|---|---|
| The page edits seconds and stores milliseconds: `saveValues` writes `timeout = RequestTimeout * 1000` (`_toMilliseconds`); load shows `Math.ceil(ms / 1000)`; an empty field gives 0, which is omitted; picking a service resets `timeout` to 0. | `WSP:414, 424, 431, 655, 741-743` (verdict r2-C2 (4)) | traced |
| Corpus `IX3` values are all multiples of 1000. | r2 §2 | measured |
| Field label "Maximum execution time, in seconds"; Academy: "Limit total time for calling the web service by this element. The limit includes retries." | verdict r7-A3 correction 1 | doc |
| Placeholder "Default ({0} sec)" = `method.getRetryRequestTimeout(service.retryCount)` = requestTimeout + the sum of (requestTimeout + retry back-off); validator `positiveNumberValidator`. | `WSP:477-480`; `service-method.js:237-243` (r2 §2) | traced |
| Leaving the field empty saves 0, not the placeholder value (Jira ENG-88713 Web Service User Task timeout placeholder is misleading when process element timeout is not explicitly set, To Do). 0 or absent means no overall element limit; only the per-request method timeout (`IR3`) applies. | r1 §4; verdict r7-A3 addition | doc, traced |
| The unit is settled as ms by three sources (page, core property doc, corpus); no capture is needed for it. | verdict r7-A3 correction 2 | traced, measured |

### 2.3 Version boundaries (platform floors)

| Capability | < 8.3.2.3590 (8.1.3, 8.3.1) | 8.3.2.3590 .. 10.0.x, 10.1 without the fix (incl. CI 8.3.4 and 10.1.37) | 10.1 with the fix (10.1.830), 10.2.x | Evidence | Tag |
|---|---|---|---|---|---|
| `IX1`/`IX2`/`IX3` | yes | yes | yes | g2 matrix | traced, measured |
| `ProcessSchemaWebService.IsIgnoreErrors` / `IX4` | absent: no member, no reader case (an `IX4` key is read past and dropped on the next save); a direct reference fails compilation with CS1061 | present | present | core commit `5e53b027c9b` (2025-11-12, RND-50229 = ENG-50229 Possibility to continue execution of the business process when Web-Service element fails, fixVersion 8.3.2); first tag `builds-windows/8.3.2.3590`, last without `8.3.2.3586`; binaries 8.1.3.6947 and 8.3.1.4498 lack it, 8.3.2.4199 and later have it (g2 §a, §b) | traced, measured |
| Run-time gate `DisableIgnoreErrorsInWebServiceUserTask` | - | present (literal measured in 10.1.37, 10.2.931) | present | `CORE/Terrasoft.Services/WebServiceUserTaskImplementation.cs:137-138`, commit `3deb116212b` (g2) | traced, measured |
| Nested array-in-array REQUEST body (ENG-55920): an inner CompositeObjectList with no source of its own whose leaves are mapped (a mapped inner container is sent on every build, 5.17) | inner list sent as null | inner list sent as null (fix members `IsNestedListParameter`/`IsUnmappedEmptyList` absent in 8.3.2.4199, 8.3.4.2753, 10.0.0.950, 10.1.37) | sent unless `DisableParametersWithNestedArrays` is on (members present in 10.1.830, 10.2.931; first fixed 10.1 build not bisected) | g2 §c; core PR engineering/core#1119, merge `74889283`, merged 2026-08-19; ENG-55920 fixVersion 10.1 | measured (binaries), traced (behaviour) |
| `ServiceParameter.ItemProperties` (`IU6`) for reading a service's nested shape | yes | yes | yes | in core since `7e6fbcfd690` (2018-01-09); present in every measured build (g2 §c) | traced, measured |

- The package declares no platform floor: `descriptor.json` has no platform field and `DependsOn: []`; clio has no
  platform-version gate; the README's empirical bound is `PBR/README.md:37` ("8.1.2 was measured to lack core APIs the
  package uses") (g2 §b) [traced].
- The real compile floor is already above 8.3.1 (EM9, measured 2026-11-03, local `dotnet build ... -c dev-nf` of
  `origin/main` `da719366` against the DFS cores, decisions Q7): it builds on 8.3.2.4199; it fails on 8.3.1.4498
  (CS1061 `ProcessUserTaskSchemaManager.FindRuntimeSchemaFromMetaData` in `UserTaskDeprecationPolicy.cs:136`, CS0117
  `DataValueType.Money0/1/3DataValueTypeUId` and `Float0DataValueTypeUId` in `ParameterTypeCompatibility.cs:53-55`)
  and on 8.1.3.6947 (MSB3245: `Creatio.Copilot.Abstractions`, `Terrasoft.Authentication.Contract` absent). Adding a
  direct `IsIgnoreErrors` reference changes neither result beyond the expected CS1061 on 8.3.1. Builds between
  8.3.1.4498 and 8.3.2.4199 were not tried [measured].
- CI compiles and unit-tests against 8.3.4.2753 (`PBR/AGENTS.md:67, 157`, `PBR/docs/ci-cd.md:125-177`), while every
  local package worktree's `.application/net-framework/core-bin` is 10.1.37. Neither can detect that a source compiles
  on 8.3.4 but not on 8.3.1 (g2 §a) [measured].
- The package is source-only and compiled by the target, so a compile error takes down every CrtProcessBuilder tool on
  that target, not only the web-service block [inferred; EM9 measured the compile, not the effect on a target].
- History of ENG-55920: a 2024 fix without a toggle shipped in 8.1.4.2820, 8.1.4.2824, 8.1.4.2844 and was removed from
  8.1.4.2889 onward (ENG-59116 Fix for supporting nested arrays in Web-service calls leads to unexpected behaviour with nested collections in business processes, reverted in core `bd69f5232ad` / `3fe7cbc9e3e`); the current fix is absent from
  `releases/10.0.0.894`, `8.3.4.2753`, `8.3.3.3293` and branch 8.3.4 (verdict r1-C7 (b); g2 §c) [measured].

---

## 3. Designer client behaviour

### 3.1 The page and the service picker

| # | Fact | Evidence | Tag |
|---|---|---|---|
| 3.1.1 | Service list = ESQ on `VwWebServiceV2` (`value` = `UId`, `displayValue` = primary display column); the only filter is `PackageUId IN <package hierarchy of the process package>`, applied only while the client feature `AutoAddPackageDependenciesInProcesses` is OFF (when ON, `_packageHierarchy = null`: every service in the workspace). The same gate decides the Sub-process, entity and mapping pickers. Measured 2026-11-03 (EM10 designer half, decisions Q9): with the feature ON, the picker of a process in a new package with 0 `SysPackageDependency` rows listed all 3 stand services. | `WSP:143-157, 452-469, 844-848`; `SubProcessPropertiesPage.js:174` and siblings (g5 §a) | traced, measured |
| 3.1.2 | `VwWebServiceV2` = `VwSysSchemaInWorkspace` rows with `ManagerName = 'ServiceSchemaManager'`, one row per `SysSchema` row (per package copy), `TypeName` REST / SOAP 1.1 / SOAP 1.2. No type filter, no active-state filter, no dedup; the combobox filters client-side by `CONTAIN`. `DontUseSoap` (a client feature flag) only hides the "create SOAP service" menu items; no package registers it, and stand `Creatio` has no `Feature` row for it, so it is off (M-32). | `PS/ServiceDesigner/branches/7.8.0/SqlScripts/VwWebServiceV2MSSQL/VwWebServiceV2MSSQL.sql:6-47`; `PS/CrtCoreBase/.../VwSysSchemaInWorkspaceMSSql.sql:33-76`; `WSP:787-800, 936-939` (g5 §a) | traced, measured |
| 3.1.3 | Server default of the matching flag: `GlobalAppSettings.FeatureAutoAddPackageDependenciesInProcesses` default `true` (and `FeatureAutoAddPackageDependencies`). On stand `Creatio` the client value read in the designer page is `AutoAddPackageDependenciesInProcesses` ON and `AutoAddPackageDependencies` OFF (measured 2026-11-03, EM10, decisions Q9); the server `GlobalAppSettings` values were not read. | `CORE/Terrasoft.Core/GlobalAppSettings.cs:2307-2315` (g5 §a) | traced, measured (client) |
| 3.1.4 | `IX1` stores `ServiceSchemaInstance.uId` (the manager item), never a package copy's `realUId` (used only to open the service designer). In the corpus the two never differ (32 service schemas, 0 duplicate names, 0 duplicate UIds). | `WSP:350-353, 441, 650-653` (g5 §a) | traced, measured |
| 3.1.5 | The service is loaded with `Terrasoft.ServiceSchemaManager.findInstanceByUId(uId)` -> `ServiceSchemaRequest` -> `ServiceManagerService.Post` -> `DesignItem` -> `DesignSchema`; method list = `service.methods` keyed by `name`, displayed `caption || name`; resolved by `findMethodByName(name)`. | `WSP:122-139, 219-235, 687-689`; `CORE/Terrasoft.Nui.ServiceModel.WebService/ServiceManagerService.cs:51-66` (g5 §a) | traced |
| 3.1.6 | The page works only with registered services: the service list has no "new service" entry, and the button next to the service field opens the SELECTED service for editing; an unregistered API is out of reach in the designer as well. | stand `Creatio`, 2026-11-03 (decisions Q10) | measured |

### 3.2 Service pick, method pick, method change, re-sync on open

| Gesture | What the page does | Evidence | Tag |
|---|---|---|---|
| Pick a service | `ServiceSchemaUId = lookup.value`, `MethodName = null`, `timeout = 0`, clear the method, load the service; a service with exactly one method gets it auto-selected (parameters synced); the element caption becomes `generateItemUniqueName(serviceCaption + " ", ...)` = "<service caption> <N>". | `WSP:270-286, 598-603, 609-640` (r2 §2, §6) | traced |
| (caption, measured) | Stored captions do not all follow that rule: Bing `WebService1` caption is `https://api.bing.microsoft.com 1`; corpus has "JiraRuntimeCollection 1/2/3", "InvalidUrlService 1". The element caption is not a run-time key. | r2 §6; r9 §3.2 | measured |
| Pick a method | `MethodName = method.name`; `element.clearParamters()` removes ALL parameters (result parameters included) and every mapping row keyed by their UIds; then `initSchemaParameters` -> `element.synchronizeParameters(service)`. | `WSP:288-301, 541-553, 816-819`; `PPE` `clearParameters` (g1 §a) | traced |
| Change the method on a configured element | confirm "All parameters will be lost", then `clearParameters` (all bindings to those parameters dropped), then sync. A service change with a method set goes through `canChangeSchema` + confirm. | `WSP:241-252, 541-553`; `RootUserTaskPropertiesPage.js:429-441, 475-487` (r2 §6) | traced; method change open: EM4 |
| (service change, measured 2026-11-03) | Switching a configured element from `GeminiApiSearch` to `UsrWscEchoService` asks "Are you sure you want to change element schema? All data will be lost.", then replaces every parameter: Success / Response / StatusCode come back with NEW UIds, 4 `BK15` rows remain, the caption becomes "WSC echo service 1", the element name stays `WebService1`. | stand `Creatio`, EM4 partial (decisions Q11) | measured |
| Open an existing element | `onElementDataLoad` -> `synchronizeActualSchemaParameters(element, currentService)` + `invalidateDependentElements(links)`: elements whose mapping points at a removed parameter get `isValid = false`. `onSchemaOutdated` (service edited elsewhere) saves and re-syncs. | `WSP:436-449, 562-563`; `RootUserTaskPropertiesPage.js:563-579, 596-607` (r2 §6) | traced |
| Save | `timeout` x1000 and `isIgnoreErrors` are written; parameter mappings are saved by the base page only outside extended mode. | `WSP:709-715, 741-743`; `ProcessFlowElementPropertiesPage.js:970-995` | traced |
| Ignore errors checkbox | visible only while feature `DisableIgnoreErrorsInWebServiceUserTask` is disabled (`IsIgnoreErrorsParameterVisible = Features.getIsDisabled(...)`); loads `element.isIgnoreErrors`, saves it. Page change: SVN r356140..r357726 (2025-11-19..12-04, RND-50229). | `WSP:70-72, 417, 743, 1071-1084` (g2 §b) | traced |
| Validation | `ServiceSchema` and `ServiceMethod` are required page attributes; a required IN parameter must have a mapping value (or a boolean), enforced only for `ProcessWebServiceSchema`; a new element starts `isValid: false`; OUT parameters are not shown on the page; a compiled process gets an info notice when any IN parameter is a collection (`initHasCompileModeLimitations`). | `ProcessSchemaParameterViewModel.js:211-217, 346-348`; `process-schema-parameter.js:268-276`; `process-flow-element-schema-manager.js:176-180`; `WSP:823-833, 1170-1172` (r2 §6) | traced |
| Parameter list order on the page | by caption with a direction prefix, required first; UI only, not the serialized order. | `WSP:722-727` | traced |
| RequestBody on the page | filtered out of `getEditableParameters` unless extended mode; `_checkHasRequestParameters` ignores it. | `PWS:396-402`; `WSP:384-390` | traced |

### 3.3 Parameter emission (client `synchronizeParameters`)

Algorithm (`PWS:318-396`, `_getParameterConfig :260-282`, `_getItemProperties :235-255`, `_synchronizeResultParameters
:335-346`; g1 §a-§b, r2 §3) [traced]:

1. Method not found -> `clearParameters()` and return.
2. Request parameters -> IN, in request order; response parameters -> OUT, in response order. An existing parameter
   found by NAME keeps its UId and mapping and gets type, required flag and caption updated (recursing into items by
   name); a new one is appended at the end.
3. `_removeNotActualParameters`: removes root parameters with a non-empty Tag that were not synced.
4. `_synchronizeResultParameters`: adds Success, Response, StatusCode, RequestBody as `DynamicProcessSchemaParameter`
   only when no parameter of that NAME exists, each with a `BK15` row (`synchronizeDynamicParameter` ->
   `_updateMapping`, `PPE:119-160`).

Per parameter:

| Field | Rule | Key | Tag |
|---|---|---|---|
| Name | `<service param name>_In` / `_Out`, at every nesting level | `A2` | traced; 539/539 tagged (297 root + 242 nested) measured |
| Tag | the bare service parameter name; for the synthetic child of a scalar array `<Name>ItemValue` | `L17` | traced; 24/24 ItemValue children measured |
| Type | empty, unknown or Text -> MaxSizeText (`c0f04627`); array of Object -> CompositeObjectList (`651ec16f`); any other array -> ObjectList (`4b51a8b5`); non-array Object -> Object `84ed6865` (CUSTOM_OBJECT, 0 in corpus); otherwise the service type | `L1` | traced; 484/486 type pairs match the service (2 stale: StudioFreeUpdate `Chatid`) measured |
| Required | scalar array -> false; else `isRequired && direction == IN`; the ItemValue child is computed with `isArray: false`, so it is `true` under a required IN array | `L6` (written only when true) | traced; `L6:true` on 40 root + 6 nested IN params, never on OUT; 2 stale exceptions (ProcessTests UsrRuntimeCollectionFormulas WebService3/4 `UsrArray_In`) measured |
| Direction | IN 0 / OUT 1, written on every parameter including 0 | `L12` | measured 751/751 |
| Source | `{source: None}` -> `L8 {}`, except Booleans: `{"GS2":"false","GS5":<P>}` on 14/14 tagged Boolean params (3 top-level, 11 nested; e.g. `BingSearchProcess` `VIFFriendly_Out`, `VNCache_Out`), measured; Success as in the result table below; Boolean In has no corpus instance (serialization 2.4, SQ5). Measured 2026-11-03: an input whose service parameter has a default (the Const header `UsrWscProbe` = `wsc-probe` of `UsrWscEchoService`; the sys-setting API-key header behind `Xgoogapikey_In` of `GeminiApiSearch`) is stored with an empty `L8`, the page shows it as "Select value", and the default string occurs nowhere in the process metadata: the default stays in the service (decisions Q14, Q16) | `L8` | measured |
| Items | object array -> one child per service `itemProperties`, recursive; scalar array -> one child `<Name>ItemValue_<dir>`, caption `DefaultNestedParameterCaption` ("Item value"), the item's primitive type (MaxSizeText 13, Integer 10, Guid 1); built only when `Terrasoft.Features` "ProcessParameterCollections" is on or `Terrasoft.isDebug` | `L18` | traced; 79 params with `L18` (55 CompositeObjectList, 24 ObjectList), 55/56 CompositeObjectList carry it (the exception is a stale parameter) measured |
| Container | `containerUId = element UId` at every level | `IL2` | measured 297/297 top, 242/242 nested, 212/212 result |
| Stamps | client: `element.createdInSchemaUId`; server provider: `BaseProcessSchema.UId`; the two coincide on every shipped element | `A3`, `A4` | measured 751/751 parameters `A3 = A4 =` process UId |
| Caption | clone of the service caption; " - Input" / " - Output" appended in every culture ONLY when more than one parameter across request and response has the same caption | resources `BaseElements.<El>.Parameters.<A2 path joined by '.'>.Caption` | traced (`PWS:192-205`); 508/509 top + 242/242 nested have a caption row measured |

Result parameters (`PWS:135-168`, captions from `Terrasoft.Nui.resx`) [traced] [measured]:

| Name | Type | Dir | Default caption | `L8` measured | Present |
|---|---|---|---|---|---|
| Success | Boolean `90b65bf8` | Out | "Success" | `{"GS2":"false","GS5":<process UId>}` (Source None, Value "false") 61/66, all 25 product; `{}` on 5 ProcessTests elements | 66/66 |
| Response | Text `8b3f29bb` | Out | "Response body" | `{}` | 66/66 |
| StatusCode | Integer `6b6b74e2` | Out | "Http status code" | `{}` | 66/66 |
| RequestBody | Text `8b3f29bb` | In | "Request body" | `{}` or the bound value | 14/66 (11/25 product) |

- Result parameters have the key set `BL1 UId A2 A3 A4 IL2 L1 L8 L12`: no `L17`, no `L6`, no `L18`; every parameter's
  `BL1` is `Terrasoft.Core.Process.ProcessSchemaParameter` (g1 §b) [measured].
- RequestBody on 14/66 reflects save age, not optionality: every save stamped 8.0.0.4926 or later has it, the
  7.12-7.16 saves lack it; the current client adds it on every sync if missing (verdicts r6-C4, r9-S3) [measured].
- Where the client sets Success's "false" was not pinned (`PSP` `convertDataValueByType`, `PSP:974-978`; the calling
  path not traced) (g1 §b) [traced, open: EM2].
- Because `A3` = process UId, client `getIsDynamic()` is true for EVERY web-service parameter; only the missing `L17`
  and the `BK15` row distinguish result parameters (g1 §b) [traced].

### 3.4 BP2 order (measured, g1 §a)

| Shape | Elements |
|---|---|
| tagged IN, tagged OUT, then Success, Response, StatusCode, RequestBody | 12 |
| same without RequestBody (older saves) | 45 |
| a tagged parameter AFTER the result parameters (service gained it after the first sync) | 9 (2 product: CrtMsTeamsMeetingTranscript GetMeetingTranscript WebService2/3; 7 ProcessTests) |
| result parameters in the relative order Success, Response, StatusCode[, RequestBody] | 66/66 |

So 57/66 follow the fresh-build order; both the client re-sync and the server sync append new tagged parameters at
the end (`PWS:332`; `Parameters.Add` -> `InsertItem(Count)`, `ProcessSchemaParameterCollection.cs:50-55`, g4 §4)
[traced]. The service-edit history behind the 9 was not read (consistent with the code, not proven per element).

### 3.5 BK15 mapping rows (g1 §c)

| Fact | Evidence | Tag |
|---|---|---|
| Created by the client only: `PPE _updateMapping -> createMappingInfo`, reached for a web service only through `_synchronizeResultParameters`. Values: `A3 = A4 =` process UId, `A2 =` element name at that moment, `GT1 =` a clone of the parameter's source, `GT2 = [Element:{el}].[Parameter:{p}]`, `GT3 =` the parameter UId, `GT5 =` the UId of the seeded metadata entry, no `GT4` (no source schema). | `PPE:128-137, 480-499` | traced |
| 212/212 rows target a result parameter; 0 tagged, 0 nested; 52 elements x 3 rows, 14 x 4; key set `BL1 UId A2 A3 A4 GT2 GT3 GT5 GT1`; `GT5` random (190 distinct of 212, none a BP2 UId); row order = BP2 result-parameter order 66/66. | corpus | measured |
| A current designer-built element (10.1.37, `UsrWscBk15Probe` WebService1 on `CreatioMarketplaceApi.GetAppPublishedVersion`) gets 8 tagged params, then Success / Response / StatusCode / RequestBody, and exactly 4 rows: `A3 = A4 =` process, no `GT4`, Success `GT1 = {"GS2":"false"}` without `GS5` while Success `L8` has `GS5`: the traced shape in these keys. A designer reload, re-open and re-save writes no duplicate and rewrites nothing; result-param UIds are unchanged. | stand `Creatio`, 2026-11-03 (decisions Q4) | measured |
| `A2` equals the CURRENT element name in 202/212; 10 differ after a rename (CrtBase `GetMarketplaceAppPublishedVersionWebService` rows say `WebService2`; StudioFreeUpdate `SendMessage`/`PinMessage` say `WebService1/2`). Rows bind through `GT2`/`GT3`, not `A2`. | corpus | measured |
| `GT1`: Success `{"GS2":"false"}` (its `L8` minus `GS5`) 61, `{}` 5; Response/StatusCode `{}`; RequestBody `GT1` = its `L8` exactly, GS5 included (Gemini). | corpus; `PSP:885-890`, `BPS:913-922` explain the missing GS5 | measured, traced |
| The server never writes them: `ProcessSchemaActivity.CreateProcessSchemaMapping` is reached only from the BASE `SynchronizeParameters`, which the web-service override never calls. | `CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:209-222, 291-304, 587-599` | traced |
| Nothing at run time or design time requires them: the parameter `SourceValue` setter only keeps `GT1` in step (null-guarded); `ClearParameters` and element removal clean them (safe when absent); code generators exclude `Mappings`; `Terrasoft.Services` has no reference to `Mappings`; client `synchronizeDynamicParameter` matches by NAME (a missing row does not duplicate result parameters); a row whose `A3` is not the process UId is read by client `initMappings` as an inherited-element marker. Measured 2026-11-03: a row-less element (the designer copy below) ran Completed with no error in a designer-started run (SysProcessLog Completed; Success / StatusCode values not read). A builder-made element's re-save is still EM5. | `ProcessSchemaParameter.cs:450-458`; `ProcessSchemaGenerator.cs:1663`; `BPS:722-745`; stand `Creatio` (decisions Q4) | traced, measured (run); open: EM5 |
| The designer itself produces row-less elements: copy/paste (`cloneParameters`) creates rows only for non-dynamic parameters, and every web-service parameter is dynamic. Measured 2026-11-03: the designer copy of WebService1 (WebService2) got the same 12 params with fresh UIds and no `BK15` row, and stayed row-less after a reload, a properties-page open and a re-save. | `process-schema-designer-copy-mixin.js:117-147`; stand `Creatio` (decisions Q4) | traced, measured |

### 3.6 Reading responses and the generic "parameter from element"

| Fact | Evidence | Tag |
|---|---|---|
| The generic designer gesture "create process parameter from element outputs" (not web-service specific) makes a process parameter of type CompositeObject (`632e4371`) with `L17 = "SourceElement:<elementName>"`, items named `<elementName><paramName>` with `L17 = "<elementName>.<paramName>"`, each bound by a Script token to the nested element parameter. Shipped in 2 CrtCustomer360AI processes (`WebService1Results1`, 32 items on Bing, unbound there). Measured 2026-11-03 (EM3 designer half, `UsrWscEm3Probe`, element on `GeminiApiSearch.GenerateContent`, Parameters > Add parameter > "Create from element", only `Candidates` ticked): a CompositeObject wrapper `WebService1Results1` (Tag `SourceElement:WebService1`, no value of its own) holding a Collection `WebService1Candidates_Out` (Tag `WebService1.Candidates_Out`, bound `[#...[Element:{ws}].[Parameter:{Candidates_Out}]#]`) whose items are ONLY the first-level scalars (`CRole`, `CFReason`, `SEPRContent`, `CIndex`), each bound to the element item; the nested collections (`CParts_Out > PText_Out`, `GMGChunks_Out`, ...) are not offered and not created. | `ProcessSchemaPropertiesPage.js:401-419, 978-1060` (r2 §7); verdicts r1-C8, r5-C6; r9 §3.3; stand `Creatio` (decisions Q6) | traced, measured |
| Reading a composite response into a hand-made process Collection: Gemini `Candidates` (CompositeObjectList, Out) has `L8 = [#...[Element:{ws}].[Parameter:{Candidates_Out}]#]`, and each nested item carries its own Script mapping to the matching nested element parameter by UId (`ContentParts` <- `CParts_Out`, `ContentParts.Text` <- `PText_Out`); item names differ from the element's. | r9 §3.3, verdict r9-S5 | measured |
| Process parameters with a Script source `[#...WebService.<Out>#]`: 5 top-level + 9 inside nested items; 68 references to web-service nested parameters in total. | r2 §7 | measured |

---

## 4. Platform server

### 4.1 Service model (public API the package can call)

| Fact | Evidence | Tag |
|---|---|---|
| `ServiceSchema` (public, MetaType `161DEEB9-...`) and `ServiceSchemaManager : SchemaManager<ServiceSchema>` (public, ItemTypeName "Service"); `userConnection.GetServiceSchemaManager()`; `GetItems`, `FindItemByName`, `FindItemByUId`, `GetInstanceByUId` / `FindInstanceByUId` are public and virtual. The package csproj already references `Terrasoft.Services`. | `TS25/Terrasoft.Services/ServiceSchema.cs:26`, `ServiceSchemaManager.cs:15`, `ServiceSchemaManagerExtensions.cs:18`; `PB/../CrtProcessBuilder.csproj:282-283` (r3 §1, §7; r8 §4) | traced |
| Metadata keys: schema `IQ1` BaseUri, `IQ2` Type (1 Rest, 2 Soap11, 3 Soap12), `IQ3` RetryCount, `IQ4` methods, `IQ5` AuthInfo (None/Basic/Digest/OAuth20), `IQ7` namespaces; method `IR1` request, `IR2` response, `IR3` per-request timeout (ms), `IR4` use auth, `IR5` SOAPAction; request `IT1` Uri, `IT2` HTTP method (1 GET .. 5 DELETE), `IT3` body builder; communication `IW1` parameters (unique names per collection), `IW2` content type (1 Json, 2 Xml); parameter `IU1` Path, `IU2` DataValueTypeName (free string), `IU3` IsArray, `IU4` placement (1 Body, 2 UrlSegment, 3 QueryString, 4 HttpHeader, 5 Cookie), `IU5` DefValue {`IV1` value, `IV2` source 0 None / 1 Const / 2 SysSetting}, `IU6` ItemProperties (recursive), `IU7` IsRequired (recomputed for UrlSegment as "default empty"), `IU8`/`IU9` SOAP. | `TS25/Terrasoft.Services/Metadata/*.cs` (r3 §2, r9 §2.1) | traced, measured |
| `ServiceDataValueTypeNames` and `ServiceParameterCollection.FindByPath/GetByPath` are internal; compare type names with literals ("Object", "Text", ...). | r3 §1, §7 | traced |
| The service designer flattens a non-array object into JSONPath scalars, on the response side (`UMPTCount` path `$.usageMetadata.promptTokenCount`) and on the request side (`teamcitybpmonline_BSF` Body param `BuildTypeId` `IU1 $.buildType.id` -> `ExecuteUpgradeBsf` `PutBuildsInQueue.BuildTypeId_In`, measured 2026-11-02), and refuses type Object unless Is Array is set; arrays of objects carry `IU6` with relative paths. Measured: 57/57 Object-typed service parameters across 12 service schemas are `IsArray`; 0 element parameters of type Object or CompositeObject in the 38 process schemas. | verdict r1-C8 (1); r2 §3.1 | traced, measured |
| Service-name uniqueness is enforced only client-side (`WebServiceV2Page.duplicateNameValidator`), method-name uniqueness only client-side and exact; no server unique-name check. `FindItemByName` = first match, ordinal, over all packages; `ServiceMethods.FindByName` = ordinal (`ThreadSafeCache<string,T>`). | `PS/ServiceDesigner/.../WebServiceV2Page.js:805-815`; `CORE/Terrasoft.Core/Manager.cs:281-284`; `ManagerItemCollection.cs:363-366`; `FactoryMetaItemCollection.cs:64-65, 97-99` (g5 §b) | traced |
| Studio refuses a service parameter whose "Code in Creatio" lacks the `Usr` prefix ("Code of the method must contain prefix "Usr""), so every test-service parameter code is `UsrWsc...` (element params then `UsrWsc..._In` / `_Out`). | stand `Creatio`, 2026-11-03, while creating `UsrWscEchoService` (decisions Q16) | measured |
| `ProcessSchemaWebService.AnalyzePackageDependencies` reports the service item as a dependency; the visual designer applies it on save (`BaseProcessSchemaDesigner.ValidateSchema` -> `AddPackageDependencies` when `FeatureAutoAddPackageDependencies`). Measured 2026-11-03 (EM10 designer half, decisions Q9): a designer save of `UsrProcess_3389428` in the new package `UsrWscEm10Proc` (0 dependency rows before) with one element on `GeminiApiSearch` added exactly one `SysPackageDependency` row, `UsrWscEm10Proc -> CrtGeminiApiIntegraion`. The builder half (no row from `SaveSchema`, 6.1) is traced only. | `ProcessSchemaWebService.cs:157-164`; `CORE/Terrasoft.Nui.ServiceModel/WebService/BaseProcessSchemaDesigner.cs:192-197, 235-250` (g5 §b); stand `Creatio` | traced, measured (designer) |

### 4.2 `SynchronizeParameters` and the internal provider

| Fact | Evidence | Tag |
|---|---|---|
| `ProcessSchemaWebService.SynchronizeParameters()` is public; it resolves the schema-manager provider from `ParentMetaSchema.Manager.AppConnection.Workspace.SchemaManagerProvider` (needs a manager-owned schema) and calls `_parametersProvider.GetActualParameters`. | `ProcessSchemaWebService.cs:93-97, 157-178` (r3 §7, r8 §1.1) | traced |
| `IProcessSchemaElementParametersProvider` (Terrasoft.Core), `ProcessSchemaWebServiceParametersProvider`, `ServiceBindings` (Terrasoft.Services) and `CoreApiContainer` are internal; the provider has one registration (`ServiceBindings.cs:62`); neither assembly grants `InternalsVisibleTo` to a configuration package (they do grant it to `DynamicProxyGenAssembly2`). | verdict r8-C2 | traced |
| The provider is captured in BOTH constructors through `CoreApiContainer.Resolve<T>()`, which swallows exceptions and returns null. `SynchronizeParameters()` has no guard and no early return, so it NREs when the field is null. | verdict r8-C2; IL of the shipped binary confirmed | traced, measured |
| `ServiceSchemaUId` and `MethodName` are auto-properties: assigning them runs nothing (unlike `ProcessSchemaSubProcess.SchemaUId`, whose setter runs the platform diff). The web-service override never calls `GetCanSynchronizeParameters()`, so there is no silent no-op case. | `ProcessSchemaWebService.cs:67-79` (r4 H6, g4 §0) | traced |
| Provider output: empty list when the UId is empty, the method blank, the service item not found or the method not found (ordinal). Otherwise flat top-level parameters only: Name `<p>_In/_Out`, Tag `<p>`, new UId every call, `A3 = A4 = BaseProcessSchema.UId`, `ContainerUId =` element UId, caption cloned (no suffix), Source None, type and required rule as in 3.3. It never reads `IU6` and never sets `ItemProperties`. | `Terrasoft.Services/ProcessSchemaWebServiceParametersProvider.cs:22-93` (verdicts r3-C3, r8-C3) | traced |
| Sync steps: (1) empty `ServiceSchemaUId` -> `ClearParameters()` removes ALL parameters (result parameters too) and their mapping rows, then continues; (2) match generated params by `Parameters.FindByName` (ordinal); a hit keeps UId, `SourceValue` and `L18`, and has Caption, Group, DataValueType, Name, IsRequired, IsPerformer, IsValueSerializable (generated default true), Direction, ReferenceSchemaUId overwritten; a miss is appended at the end; (3) every unmatched parameter with a non-blank Tag is removed, after `InvalidateDependentElements` sets `IsValid = false` (persisted as `BL11`) on any parametrized element and parameter whose `SourceValue` mentions the removed UId; mapping rows are not touched; (4) untagged parameters are never touched except by step 1. | `ProcessSchemaActivity.cs:273-289, 506-544`; `BaseProcessSchemaItem.cs:25, 87-100, 121` (g4 §1; verdicts r3-C7, r8-C5) | traced |
| A case-only method mismatch is pruned on load, not at save (EM11 designer half, measured 2026-11-03, decisions Q9): the designer's own save request replayed with `methodName` `getAppPublishedVersion` (service method `GetAppPublishedVersion`) answered `success: true`, `hasErrors: false`; `SysSchema.MetaData` kept that `IX2` and all 4 tagged `_In`, 4 tagged `_Out` and 4 result params (the save does not sync); the next design-time load (`describe-business-process`) showed only Success / Response / StatusCode for that element, while the exact-name control kept its params. | stand `Creatio`, `UsrWscBk15Probe` WebService2 | measured |
| There is no web-service-specific validation rule: nothing checks that `IX1` resolves, that `IX2` exists, that Tags match the method or that result parameters exist; the generic interpreter rules run. | `TS25/Terrasoft.Core/Process/ProcessInterpretationValidator.cs:264-273` (r3 §6) | traced |

### 4.3 When the sync runs (g4)

| Load path | Syncs? | Evidence | Tag |
|---|---|---|---|
| `BaseProcessSchemaManager.GetItemFromMetaData` (6-arg override), on a new item AND on a `MetaItems` hit | yes | `BaseProcessSchemaManager.cs:949-969` | traced |
| `FindDesignItem` override, unconditionally (also on a `RequestData` cache hit) | yes | `BaseProcessSchemaManager.cs:1088-1091`; `SchemaManager.cs:4764-4769` | traced |
| `DesignSchema` -> `TryDesignItem` -> `GetItemFromMetaData` | yes | `SchemaManager.cs:2666-2675, 4033-4036, 5257-5264` | traced |
| `FindInstanceFromMetaData` on a `MetaItems` hit | no (returns the instance synced at fill time) | `SchemaManager.cs:3369-3378` | traced |
| 8-arg `GetItemFromMetaData` (`SchemaManagerItem.SaveMetaDataValue`, `FindInstanceFromMetaDataWithLock`) | no | `SchemaManager.cs:3516-3522, 3641-3645`; `SchemaManagerItem.cs:478-492` | traced |
| Compiled process instance from the assembly | no (compile-time set) | `BaseProcessSchemaManager.cs:751-758` | traced |

Consequences [traced; the measured parts say so]:
- **describe** (`LoadForDescribe` -> manager instance, fallback design instance, `PB/Schema/ProcessSchemaRepository.cs:265-310`)
  never returns the STORED parameter list of an interpretable process: it returns a freshly synced or a fill-time
  synced list. Measured 2026-11-03: the EM11 element was described without its still-stored tagged params
  (decisions Q9, Q14). All 19 product processes with the element are `IsInterpretable: True` (18/19 test) [measured]. A flat
  "tagged set vs generated set" comparison is therefore tautological on fresh instances and vacuously true when the
  method is missing (the twin of the sub-process "inSync vacuously true" record). Reliable signals on every path:
  `serviceResolved` (`FindItemByUId != null`), `methodResolved` (ordinal `FindByName != null`), nested `L18` drift
  against the service (the server sync never touches `L18`), and `IsValid == false` (`BL11`) (g4 §2, §5).
- **modify of any element** (`ProcessModifyHandler.ModifyProcess` -> `OpenDesignSession` (sync #1) ->
  `GetDesignInstance` (sync #2) -> edit -> `SaveSchema`) persists whatever the synced instance holds; `PB` reports
  none of it. Worked example on corpus data, `CrtMsTeamsMeetingTranscript/GetMeetingTranscript`: an unrelated modify
  would remove all tagged parameters of WebService3 (3) and WebService4 (4), whose methods do not exist, and
  `MeetingId_Out` from WebService2, whose method `UsrOnlineMeetings` exists but declares no response parameter
  (drift): 8 in total; FormulaTask2/3 keep dangling references with no invalid flag (FormulaTask's `BP2` is
  empty, so `InvalidateDependentElements` does not reach it) (g4 §3) [traced]. Measured 2026-11-03 (EM8
  current-builder half, decisions Q12) on `UsrWscBk15Probe` after EM11: one unrelated modify through the current clio
  + CrtProcessBuilder 1.6.6.131 (`addParameter` `UsrWscEm8Marker`) answered exit 0, "1 operation(s) applied", no
  warning; afterwards the wrong-case element stored only Success, Response, StatusCode, RequestBody, its 4 tagged
  `_In` and 4 tagged `_Out` params deleted from the database, `IX2` unchanged, so nothing in the stored element says
  what it lost; the exact-name control kept all 12 params and its 4 `BK15` rows [measured]. Not measured: the
  dangling-reference half (no mapping read the lost params).
- `ModifyProcessAsNewVersion` serializes the synced source instance, so the new version carries the pruning (g4 §3)
  [traced].
- An appended parameter gets `Guid.NewGuid()` on every sync that misses it by name; until a save persists it, each
  `MetaItems` rebuild reports a different UId for the same parameter (g4 §4) [traced].
- The visual designer opens and saves through the same server paths, so it plausibly persists the same flat pruning;
  client-side parity of that is not measured (g4 §6 Q3) [inferred].
- A candidate for reading the stored (unsynced) list is a raw `SysSchema.MetaData` read with the repository's
  `ReadSchemaMetaData` helper (`PB/.../ProcessVersionCloneFactory.cs:66-72, 105-111`), unverified (g4 §5).

---

## 5. Run time

All rows traced in `Terrasoft.Services/WebServiceUserTaskImplementation.cs` (lines `TS25` unless noted) and its
callees; a source trace is a hypothesis about run time.

| # | Fact | Evidence | Settled by |
|---|---|---|---|
| 5.1 | `ProcessWebService.InternalExecute` delegates to `IWebServiceUserTask` (one implementation, `ServiceBindings.cs:61`) resolved from `CoreApiContainer`; the implementation calls `ServiceSchemaManager.GetInstanceByUId(ServiceSchemaUId)`, `CreateServiceClient`, `CreateRequest(MethodName)`. `CreateRequest` uses `ServiceMethods.GetByName` and throws when the method is missing or renamed. Measured 2026-11-03 (EM11, decisions Q9): the wrong-case element of 4.2 failed the run with `Terrasoft.Common.ItemNotFoundException: Collection item with name getAppPublishedVersion not found` at `ServiceClient.CreateRequest`; the exact-name element in the same process Completed. | `TS25/Terrasoft.Core/Process/ProcessWebService.cs:84-99`; `:127-133`; `Runtime/ServiceClient.cs:82-85` (r3 §4); stand `Creatio` | traced, measured |
| 5.2 | The service is resolved workspace-wide by UId; the run time needs no package dependency. | `:139-142` (g5 §b) | traced |
| 5.3 | Request: every IN parameter with a non-empty Tag AND a defined source (`GetIsSourceValueDefined` = own source != None OR any nested source != None) goes to `RequestParameters[Tag]`; others are skipped (`ShouldSkip`), so the service parameter's `DefValue` (Const or SysSetting) applies; a mapped parameter whose value is null is sent as null. | `:31-57`; `Runtime/ServiceParameterValueProvider.cs:66-77, 97-99`; `ProcessSchemaParameter.cs:890-894` | EM6 |
| 5.4 | A collection whose root has no source but whose nested items do is sent as a ONE-item list built from the nested values; an ObjectList parent yields its first item's value per row. Measured in product: IdentitySync `roles_In` (only `rolesItemValue_In` bound), StudioFreeUpdate ExecuteUpgradeBsf `PropertiesProperty_In`. | `ProcessInstanceParametersDataReader.cs:441-452, 474-477`; `BaseFlowSchemaGenerator.cs:466-488` (verdict r7-A5) | EM6 |
| 5.5 | Composite request values: a CompositeObjectList IN gets `ServiceRequestDataAdapters.CompositeObjectAdapter` and, when `GlobalAppSettings.FeatureProcessParameterCollections` is on, is re-keyed Name -> Tag by a map built from `ItemProperties.GetParametersWithNested()`, FILTERED to nested IN items with a non-empty Tag and a defined source; `CompositeObjectListUtilities.Transform` keeps only mapped keys, so unmapped nested values are dropped from each row. The map is one flat `Dictionary` across all levels: a duplicate Name (request) or Tag (response) at two depths throws at execution. The composite parameter's Tag must resolve to a service request parameter path (`GetByPath` throws). | `:40-46, 53-67`; `Terrasoft.Common/CompositeObjectListUtilities.cs:17-26` (verdicts r7-A6, r3-C6) | EM6 |
| 5.6 | A composite parameter without `L18` (as the server sync creates it): the intermediate request value holds empty CompositeObjects (item count kept), and the JSON body renderer walks the SERVICE method's `ItemProperties`, so each wire item becomes `{"field": null, ...}` or carries the service defaults, not `{}`. On the response side the OUT parameter receives N empty items. | `RequestBodyBuilder.cs:93-200`; `JsonRequestBodyRenderer` (verdict r3-C6 correction 1) | EM6 |
| 5.7 | `RequestBody` is read by `FindByName` (optional); a non-null value replaces the generated body entirely. The run-time value of an UNMAPPED RequestBody is null, not "" (measured 2026-11-09 by EM6-B's trace, M-39); 12 corpus elements have it unmapped and work. The wire bytes are not measured. | `:22-29`; `Runtime/ServiceClientRequest.cs:241-252` (r3 §4, §10 Q3) | EM6 |
| 5.8 | Body builders: REST + JSON -> `RequestBodyBuilder` + `JsonRequestBodyRenderer`; SOAP 1.1/1.2 -> `SoapRequestBodyBuilder`; a custom `BodyBuilderId`; REST + non-JSON otherwise `NotSupportedException`. URL = BaseUri + Uri with `{Path}` placeholders from UrlSegment params (null -> `NullOrEmptyException`); query-name encoding behind feature `EncodeWebServiceElementQueryParameters`. | `Runtime/RequestBodyBuilderFactory.cs:52-74`; `ServiceClientRequest.cs:85-130, 262-279` (r3 §4) | traced |
| 5.9 | Timeout: `client.Execute(request, TimeSpan.FromMilliseconds(Timeout), ...)`; `HttpWebRequestActor.BeginRequest` schedules one overall `OperationCanceledException` timer before the first attempt, only when Timeout > 0; retries (`RetryCount`, on `WebException` other than RequestCanceled) run inside the same actor, so the limit covers retries; the method's `IR3` is `HttpWebRequest.Timeout` per request. | `:136-137`; `Runtime/HttpWebRequestActor.cs:94-118`; `HttpWebExtensions.cs:278` (verdict r7-A3) | traced |
| 5.10 | Compiled processes: the generated runtime property sets `ServiceSchemaUId` and `MethodName` but not `Timeout`; interpretable processes copy Timeout in `CreateProcessFlowElement`. `SchemaElementsRule` does not force compile for this element. | `TS25/Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:2284-2288, 3231-3236`; `ProcessSchemaWebService.cs:133-141`; `ProcessInterpretationValidator.cs:80-140` (r3 §5) | traced; no EM (interpretable processes are the corpus norm) |
| 5.11 | Response with an HTTP answer (4xx/5xx included, after retries): `Success = 200 <= code < 400` (Academy says "< 400"), `StatusCode`, `Response` = raw body; written by `FillResultParameters` with `GetByName`, which throws `ItemNotFoundException` when a parameter is absent, at execution time after the HTTP call (nothing checks at save). | `:109-115`; `HttpWebExtensions.cs:259-262`; `MetaItemCollection.cs:214-221` (verdict r2-C6 (5)) | traced |
| 5.12 | No HTTP answer (DNS/connect failure, element timeout): `ServiceClient.GetResponseData` rethrows; only `WebExceptionStatus.RequestCanceled` is caught (returns false); anything else fails the element. Academy community (2025) reports that an element error stops the process. | `ServiceClient.cs:70-73`; `:139-143` (r3 §4.6, r7 §2) | EM7 |
| 5.13 | Ignore errors: `isIgnoreErrors = Features.GetIsDisabled("DisableIgnoreErrorsInWebServiceUserTask") && userTask.IsIgnoreErrors`. What the flag changes on the failure path beyond this computation is not traced in the notes. ENG-50229's AC text also names "Has errors"/"Error message" outputs; no such parameter exists in the current client (`PWS` result parameters are the four in 3.3). | `CORE/.../WebServiceUserTaskImplementation.cs:137-138` (g2); r1 §4 | EM7 |
| 5.14 | Response values: OUT parameters looked up by Tag in `response.ParameterValues` (JSON `SelectToken(Path)`, XML, headers, cookies); CompositeObjectList OUT gets the composite response adapter and is re-keyed Tag -> Name recursively with the feature on (`FindByPath`, null-safe); ObjectList OUT gets no adapter, only `ObjectList.Create`. Scripts in the corpus read nested keys by `<child>_Out` names (`"MessageDate_Out"`). | `:69-107`; `Runtime/ServiceClientResponse.cs:79-110`; `JsonResponseBodyParser.cs:91-123` (r3 §4.7, r6 §5) | EM6 |
| 5.15 | A declared process Collection bound to a web-service collection output rebuilds rows from ITS items and reads each source item by the SOURCE item's NAME (`ProcessParameterValueProvider.cs:528-534`, missing key -> null). With the feature off the source rows keep Tag keys (`CParts`) while the lookup asks for `CParts_Out`: empty items. | `ProcessInstanceParametersDataReader.cs:525-533` (g3 §4) | EM6 |
| 5.16 | `GlobalAppSettings.FeatureProcessParameterCollections` comes from the config key `Feature-ProcessParameterCollections`, default false in code, true in the repository's `Web.config` and `app.config`. Stand `Creatio` has the DB feature `ProcessParameterCollections` on for All employees; whether the GlobalAppSettings flag follows the DB row is not verified. Feature history (research 2026-11-03, decisions Q3): the toggle dates from 2018 (CRM-38674 "Run time: mapping to a collection-type parameter (in interpreted processes)", on for release in CRM-39571 "Enable the collections functionality in the release" / 7.12.3; both English renderings of the Russian Jira titles); every checked build from 7.13 to 10.2.931 ships `Feature-ProcessParameterCollections=true` in its Web.config / WebHost config, which the run time reads; the designer's client gate reads DB rows only, and ENG-47367 "[RFMT] Remove feature toggling in web services released in 7.12.4" (English rendering of the Russian Jira title; 8.2.1) deleted the CrtProcessDesigner data binding, so a clean 8.2.1+ database has the designer emit flat composites (open bug ENG-68545 "No data binding exists for ProcessParameterCollections feature"). | `GlobalAppSettings.cs:1727, 2467, 2611`; `Terrasoft.WebApp/Web.config:526`; `Terrasoft.WebHost/app.config:321` (verdict r3-C6 (5)); r9 §4; decisions Q3 | EM0, EM6 |
| 5.17 | ENG-55920 scope: only an inner CompositeObjectList with NO source of its own whose leaves are mapped was dropped (`CreateListItem` returned the default); a mapped nested container, and an outer sourceless list whose value is already rows, were materialised. The fix routes a nested CompositeObjectList through the value provider even with Source None unless `DisableParametersWithNestedArrays` is on; it does not cover nested ObjectList or single objects; `IsUnmappedEmptyList` keeps an unmapped empty nested list null. "Send test request" serialized nested arrays before the fix. "Saves green" has no recorded evidence; "silently dropped" rests on the ticket author's run (a 38-byte body, no error). | `ProcessInstanceParametersDataReader.cs:444-446, 482-485`; core PR #1119; comment 469862 (verdicts r1-C6, r1-C7) | EM12, not measurable on stand `Creatio` (no echo endpoint, decisions Q17): rests on the core unit tests and the ENG-55920 comment |
| 5.18 | ENG-82334 "Case #SR-01449388: The attribute value becomes null when goes to the “Web service” business process element." (Closed 2025-11-27): a collection value became null when passed into the element (customer case). ENG-103155 "Case #SR-01554222: Web service "Send test request" shows array-of-values items as empty objects" (To Do): designer "Send test request" display bug for arrays of values, run time unaffected. ENG-89253 "Web-services: Add possibility to use associated arrays" (To Do): associative JSON objects unsupported. | r1 §4 | doc |

---

## 6. CrtProcessBuilder today (`PB`, `da719366`)

### 6.1 No web-service code

| Fact | Evidence | Tag |
|---|---|---|
| No handler, contract member, reader or test touches `ProcessSchemaWebService`, `ServiceSchema` or `ServiceSchemaManager`; `git grep` finds only binary Libs. `docs/` has no web-service capture document. | r4 H3, r6 §8, r8 §4 | traced |
| No `[OperationContract]` reads services; the 8 WCF operations carry new blocks unchanged (`PB/EntryPoints/WebService/ProcessDesignService.cs:40-192`). | r4 §1 | traced |
| The package has never authored a `BK15` row (`new ProcessSchemaMapping` / `Mappings.Add` absent in `packages/` and `tests/`). Sub-process rows come from the platform diff behind its setter; send-email asserts "no BK15 row added". `ProcessGraphBuilder.RemoveElement` deletes every row whose `TargetMetaPath` contains the element UId; `ProcessMappingService.Commit` assigns `SourceValue`, so the platform setter keeps an existing row's `GT1` in step. | `PB/Graph/ProcessGraphBuilder.cs:1915-1922`; `PB/Mappings/ProcessMappingService.cs:221-235`; `PBR/docs/sub-process-element-capture.md` (g1 §c) | traced |
| The package save path is `Manager.SaveSchema(item, uc, true, true)`, not the designer service; nothing in `PB` applies package dependencies for any element. | `PB/Schema/ProcessSchemaRepository.cs:337, 345`; `git grep ApplyDependencies` = 0 (g5 §b) | traced |

### 6.2 Describe today

| Fact | Evidence | Tag |
|---|---|---|
| The element is typed by its CLR class only: `type: "ProcessSchemaWebService"`, `managerItemUId`, `buildType: null` (no handler's `CanBuild` matches, since `UserTaskElementHandler.CanBuild` is `element is ProcessSchemaUserTask`), `userTaskName: null`. No member for `IX1`-`IX4`. | `PB/Describe/ProcessDescriber.cs:117-137`; `PB/Elements/UserTaskElementHandler.cs:131-134` (verdict r9-S6) | traced, measured |
| Generic `parameters[]` DO carry `tag` and recursive `itemProperties` (`ProcessParameterService.ToDescribeParameter`, lines 259, 263): a raw POST to `DescribeProcess` on stand `Creatio` returned `query_In` tag `query`, `WPValue_Out` with 22 items (`VDLinks_Out` nests 3), Gemini `Candidates_Out` with 8 items nested 3 levels, and the process parameter `WebService1Results1` with tag `SourceElement:WebService1` and 32 items. Confirmed 2026-11-03 (decisions Q14) on `UsrProcess_3389428` (designer-built element on `GeminiApiSearch.GenerateContent`, nothing mapped; stored: 2 tagged inputs, 8 tagged top-level outputs, 3 of them `CompositeObjectList` with `L18` items down to three levels, the 4 result params, 4 `BK15` rows): the package returns the outputs with tags and the nested tree, none of its inputs (nothing was bound; a bound input comes back, M-31), and no service / method / timeout / ignore-errors member. A describe reading on that day that showed no tags and no nesting came from the session's older clio MCP build, which drops those fields (the stale-client effect of 7.1); current clio source carries `tag` and `itemProperties`. | verdict r9-S6; decisions Q14 | measured |
| Only top-level parameters get `isOutput` stamped; item properties come back `isOutput: false` even when their direction is Out. | `ProcessDescriber.ProjectParameter` (verdict r9-S6) | traced |
| Unbound inputs are omitted (`IsGenericOutput || IsSetInThisSchema || HasAnItemSetInThisSchema`): Bing `OcpApimSubscriptionKey_In` and `RequestBody` are absent, Gemini's bound `RequestBody` appears; confirmed 2026-11-03 on the unmapped `UsrProcess_3389428` element (nothing bound, so no input returned; describe returns bound inputs only, M-31). | `ProcessDescriber.cs:226` (verdict r9-S6; decisions Q14) | traced, measured |
| Describe reads a synced instance (section 4.3), so a describe of an element whose method is missing shows the already-pruned set. | g4 §2; EM11, 2026-11-03 (decisions Q9) | traced, measured |

### 6.3 Parameter types (Task 5 / ENG-92730 side)

| Fact | Evidence | Tag |
|---|---|---|
| A declared `type` (and item `type`) goes through `NormalizeParameterTypeName`: Text/ShortText, LongText, Integer, Float (stored Float2), Money, Boolean, Date, DateTime, Time, Guid, Lookup (needs referenceSchema), File (FileLocator), Collection and FileCollection (CompositeObjectList); Binary/Blob get a dedicated refusal; everything else is refused, including CompositeObject, Entity, Color, Image, Enum, SecureText, ObjectList and MaxSizeText. A bare shapeless Collection is accepted. | `PB/Parameters/ProcessParameterService.cs:1826-1881, 796`; `PB/Contracts/ProcessDescriptorContracts.cs:2299-2305, 2400-2403` (verdict r1-C4) | traced |
| Declared nesting is capped at 2 collection levels (`MaxCollectionLevels = 2`): a top-level collection's item may be a collection, that one's items may not. | `ProcessParameterService.cs:93-98, 219, 830-834` (verdict r4-C8) | traced |
| A `typeFromElement` mirror bypasses the allow-list and copies the source's DataValueTypeUId verbatim. For a CompositeObjectList source `CloneItemProperties` clones items and refuses at the first item whose Tag does not parse as a GUID ("carries no column identity"); web-service items carry the service parameter name, so a mirror of a web-service object-array output is refused (194/194 CompositeObjectList items non-GUID, 165/165 on Out collections). | `ProcessParameterService.cs:144-160, 974-1026` (refusal `:997-1004`) (verdict r4-C7) | traced, measured |
| A mirror of an ObjectList output (scalar array) takes the scalar branch: it copies the ObjectList type, clones no item, writes no provenance tag, binds nothing, and is accepted silently; a described `type` "ObjectList" fed back is then refused, so describe/replay breaks. Corpus: 9 top-level ObjectList Out parameters, all in test packages; product has 4 nested ObjectList Out items (Gemini `GMWSQueries_Out` and `GSGCIndices_Out`, both copies). | `CollectionBindingRules.IsCollection` matches CompositeObjectList only (`PB/Mappings/CollectionBindingRules.cs:143-144`) (verdicts r4-C7, r4-C8, r5-C6) | traced |
| ObjectList counts: 16 top-level + 8 nested including tests; product has 1 top-level (IdentitySync `roles_In`) and 4 nested (Gemini `GMWSQueries_Out`, `GMGSupports_Out.GSGCIndices_Out`, each in both Gemini copies). CompositeObjectList alone never nests deeper than 2 in the corpus; the deepest path is `Candidates_Out > GMGSupports_Out > GSGCIndices_Out (ObjectList) > GSGCIndicesItemValue_Out`: 3 collection levels, 4 parameter levels, in 2 product elements. | verdict r4-C8 | measured |
| An item shape cannot be changed in place ("Remove the parameter and add it again"). | `ProcessParameterService.cs:418-421` (r1 §3.2) | traced |

### 6.4 Mapping engine against web-service nested items (g3)

| # | Fact | Evidence | Tag |
|---|---|---|---|
| M1 | The dotted walk records every parent of any type as an ancestor; `IsItem` = ancestors > 0; P1 does not consult `IsCollection`. | `PB/ProcessSchemaElementLocator.cs:336, 346`; `CollectionBindingRules.cs:143-144, 244-246, 746` | traced |
| M2 | Collection rules run only for a STRUCTURED source (element or process parameter); a formula, constant or record column writes the named target alone. | `ProcessMappingService.cs` `Plan` ~211-215, `IsStructuredSource` 1042-1046 | traced |
| M3-M4 | item <- item plans P1 (innermost first; source deeper than target refused; target parent already bound to the paired source parent skipped; bound to anything else refused; unbound bound). item <- non-item resets FILE items only. | `CollectionBindingRules.cs:70-101, 174-182, 397-435, 473-483` | traced |
| M5 | Every P1 parent write checks types; outside the text/number/date/lookup groups the rule is an exact UId match: COL<->COL and ObjectList<->ObjectList pass, COL<->ObjectList and CompositeObject<->COL are refused and the whole plan writes nothing. | `ProcessMappingService.cs:1126-1134, 1182-1191`; `ParameterTypeCompatibility.cs:231-279`; test PU-44 | traced |
| M6 | Every parameter-to-parameter binding is written `GS1=3`, `GS2="[#<meta path>#]"`, `GS5=<schema UId>`; an element item is adopted (ContainerUId backfilled) by the walk. | `ProcessMappingService.cs:1071-1077, 1117-1120` | traced |
| M7 | The element is a `ProcessSchemaParametrizedFlowNode`, so element targets/sources and dotted paths work on it; no `Plan` guard is web-service specific. | `ProcessSchemaActivity.cs:26`; locator 60-67 | traced |
| M8 | No unit test uses a `ProcessSchemaWebService` element or an ObjectList parameter; the non-file P1 path is covered generically (PU-29). | git grep (g3 §0) | traced |

Results per corpus shape (g3 §1-§4) [traced write, measured target]:

| Shape | Corpus | What `PB` writes today | Same bytes as the corpus? |
|---|---|---|---|
| (a) IN nested leaf <- scalar process parameter, formula or constant, container left at None | product x2 (ExecuteUpgradeBsf `PropertyValue_In`, IdentitySync `rolesItemValue_In`); test 9+1+4 | the leaf only; no P1, no notice; parent untouched | yes (leaf value and GS5 = schema UId) |
| (b) IN item <- item of a Read data / web-service CompositeObjectList output | test 13 (`same`), 1 ObjectList<-ObjectList | P1 binds the parent to the source collection | yes for COL<-COL (`UsrTestJiraReadDataRuntimeCollection` parent GS2/GS5 equal) |
| (b') COL item <- ObjectList item | test 2 (`UsrTestJiraCollectionRunTimeCustom1`) | refused by M5 | no (cannot be built) |
| (b'') formula item reading row items of a collection the parent is not bound to | the corpus process maps the parent explicitly | nothing warns; at run time the parent is Source None and the formula is evaluated outside a row (reads empty by the file analogue; for scalars a hypothesis) | n/a |
| (c) declared Collection with itemProperties <- web-service CompositeObjectList Out | product Gemini `Candidates` / `ContentParts` / `Text` | P1 binds `ContentParts <- CParts_Out` and `Candidates <- Candidates_Out`; Tags are read nowhere on the mapping path | yes (all three levels GS1=3 tokens, GS5 schema UId) |
| (c') third (ObjectList) level, e.g. `...GSGCIndices_Out.GSGCIndicesItemValue_Out` | product 2 bindings, both in the CompositeObject "parameter from element" shape | not reachable: ObjectList not declarable, depth > 2 refused, COL <- ObjectList refused | no |
| (c'') CompositeObject "parameter from element outputs" (Customer360, 41 bindings) | product | 30 refused by M5 (CO <- COL), 11 by the depth rule; CompositeObject not declarable anyway | no |

- `ResolveTargetObject` (package-internal object targets) supports only a DIRECT item (`ProcessMappingService.cs:693-711`)
  (g3 §3) [traced].
- Schema-level `BK15` rows carry no row for any web-service parameter in these processes, so `SourceValue` is the whole
  stored binding (g3 §1) [measured].
- Untested hypothesis: a declared item collection WITHOUT itemProperties takes source rows verbatim
  (`ProcessInstanceParametersDataReader.cs:469-472`), nested ObjectList included (g3 §4) [traced, open: EM6].

### 6.5 Reusable patterns (named only; see [reuse](eng-92710-call-web-service-element-reuse.md))

Sub-process (`SubProcessElementHandler`, `SubProcessApplier` resolve by name/UId with ambiguity refusal and the
retarget guard, `SubProcessReader`, `SubProcessSyncReport`/`SubProcessSyncNotices`, Create/Update modes),
`PreconfiguredPageParameterSync` (server port of a client sync with a read-only `IsInSync` twin), the mapping service
(`ApplyMapping`, `BindCollection`, P1), `IProcessDependencySnapshot.FindDependents`, the FileProcessing capture
oracle (`PBT/FileProcessingCaptureOracle.cs`, `Fixtures/*/PROVENANCE.md`) (r4 §2-§4) [traced].

### 6.6 Test harness facts

| Fact | Evidence | Tag |
|---|---|---|
| The harness starts each test with an empty `ServiceCollection`; `ServiceBindings` is never loaded, so a constructed `ProcessSchemaWebService` holds a null provider and `SynchronizeParameters()` NREs. The provider CAN be bound by reflection before construction (`TestDIUtilities.Configure`, `Type.GetType("Terrasoft.Core.Process.IProcessSchemaElementParametersProvider, Terrasoft.Core")`, a non-generic NSubstitute proxy or the real internal provider) or injected into the private field. | verdict r8-C2 | traced, measured (binary metadata) |
| `TestUserConnection.SetServiceSchemaManager` routes `GetServiceSchemaManager()` to a substitute; `ServiceSchema` is constructible over any `ISchemaManager`; `SubProcessTestSupport.SetupSchemaManager` substitutes `SchemaManagerProvider.GetManager(string)`, the seam the provider uses. | `UnitTest/TestUserConnection.cs:35, 166-168, 268-270`; `PBT/SubProcessTestSupport.cs:84-104` (r8 §3-§4) | traced |
| Core test tables ready to copy: `ProcessSchemaWebServiceParametersProvider.Tests.cs` (type matrix, IsRequired matrix, empty guards), `WebServiceUserTaskImplementation.Tests.cs` (Tag keys, nested Source None skipped, RequestBody override), `PetstoreServiceSchema.cs` (nested response arrays, a request body). | r8 §2 | traced |
| Capture pairs exist in the corpus (process + its service schema): CrtBase `CheckMarketplaceAppNeedUpdateSubProcess` + `CreatioMarketplaceApi` (flat), CrtGeminiApiIntegraion `ActionGeminiSearch` + `GeminiApiSearch` (composite Out), StudioFreeUpdate `ExecuteUpgradeBsf` + `teamcitybpmonline_BSF` (composite In). | verdict r8-C10 | measured |

---

## 7. clio and the guidance library today

### 7.1 clio (`CLIO`, `6be5dde28`)

| Fact | Evidence | Tag |
|---|---|---|
| clio passes element blocks through, but refuses before sending any key its `ProcessBlockKeyCheck` key sets do not declare (they mirror the server `[DataMember]`s because WCF `DataContractJsonSerializer` drops undeclared members silently). A `webService` block is refused until a `WebServiceBlock` key set is added to `Blocks` and linked into `Element` and `ElementUpdate`. | `clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:22-40, 286-357` (verdict r5-C1) | traced |
| Drift tests: `KeySets_Should_EqualTheMembersOfTheBundledServerContracts` and `ReadBackFields_Should_BeDescribeOnlyMembers` (`clio.tests/Command/ProcessModel/ProcessBlockKeyCheckTests.cs:635-666`); the short-form call-shape test (`clio.tests/Command/McpServer/ToolContractShortFormTests.cs:572`) fails until `"webService"` joins `CreateElementBlockShape` (`clio/Command/McpServer/Tools/ToolContractShortForm.cs:84-88`). | verdict r5-C2 | traced |
| `[RequiresPackage(CrtProcessBuilder, ...)]` on create, modify and modify-as-new-version carries one versioned floor literal (1.6.6.127 at this commit, history); describe, list-user-tasks and validate-process-graph are presence-only; the floor history comment records one paragraph per raise; pinned by `ProcessDesignerRequiresPackageAttributeTests`. | `clio/Command/CreateBusinessProcessCommand.cs:215-293`; `ModifyBusinessProcessCommand.cs:227`; `ModifyProcessAsNewVersionCommand.cs:91` (r5 §5) | traced |
| Full-contract ceiling `MaxToolContractSerializedBytes = 137*256 = 35,072 B`; measured at this commit: modify 35,069 (3 B left), create 35,033 (39 B), describe 35,029 (43 B). The default encoder writes 6 bytes for every non-ASCII character and for `'`, `"`, `<`, `>`, `&`, `+` and the backtick. The ceiling was raised 134 -> 136 on 2026-09-15 and 136 -> 137 on 2026-09-30 (deliberately, ENG-92711 Script task element met ENG-91844 "Implement full parameter mapping (sources)", Task 6 value mapping); no ticket has raised it since; every later ticket either fit the headroom or swapped text. Precedent decision: ENG-92719 File processing element Q20 (ENG-95985 Q12). | `clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs:303-315` (verdict r5-C3) | measured |
| Short-form bundle `MaxInlineWireReplyBytes = 19,000` at 18,832 B (168 B left); `"webService"` joins the never-cut block-name list; re-measured on the branch. | `ToolContractPayloadBudgetTests.cs:322-339` (r5 §2) | measured |
| Security pins `ExpectedOperationContractCount = 8`, `ExpectedAuthorizationGateCallSites = 6`; a block inside build/modify/describe moves neither; a new operation moves the count to 9 (and the gate count unless routed through `ProcessDesigner.Execute`). | `clio.tests/Common/BundledProcessBuilderPackageTests.cs:293-374` (verdict r4-C11; g5 §c) | traced |
| Tool descriptions may name only guidance the pinned generation publishes (`clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`, libraryVersion 1.15.176, sequence 1015176000); `process-web-service` is not in it, so a description naming it turns `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` red until the fixture lists it. The fixture is hand-edited: publish-before-clio is policy, not a CI gate; package-first is a team convention (ENG-95985 Q13), not enforced by a test. | `WorkspaceTemplateGuidanceDriftTests.cs:539-597` (verdict r5-C5) | traced |
| The process tools have no CLI `[Verb]`; their "docs" surface is `docs/McpCapabilityMap.md:792-798`, the tool `[Description]`s (no curated `ToolContractGetTool` entry overrides them), the prompts and the KB articles. No clio MCP surface mentions web services today (`git grep -i webservice clio/Command/McpServer` = artifact-merge vocabulary only). | r5 §1 | traced |
| Describe client: `DescribedElement` has typed per-kind members and a `[JsonExtensionData]` bag, so an untyped `webService` read-back survives the round trip in the bag; `DescribedParameter` has no bag. | `clio/Command/ProcessModel/IProcessDescriber.cs:580-847` (r5 §4) | traced |
| clio's own web-service code: `WebServiceManager.GetAllServices()` reads every `VwWebServiceV2` row with all columns, `MetaData` included; `GetServiceUrl` POSTs to a hard-coded `DataService/json/SyncReply/ServiceSchemaRequest` (not a `KnownRoute`) and reads only `$.schema.baseUri`; verbs `set-webservice-url` / `get-webservice-url`; no MCP tool; nothing decodes methods or parameters. The designer's service read (`ServiceSchemaRequest`) returns the service's authentication settings; the builder never calls it and never echoes it. | `clio/Common/WebServiceManager.cs:13-84`; `clio/SetWebServiceUrlCommand.cs`; `CORE/Terrasoft.Nui.ServiceModel/DataContract/ServiceDataContract.cs`, `AuthInfoDataContract.cs:120-131` (g5 §c) | traced |
| A stale clio MCP client drops fields: the global tool clio 8.1.0.135 (installed 2026-09-30, bundled process builder 1.6.6.50) omitted `tag`, nested items and `isOutput` that the package sends. | verdict r9-S6 | measured |
| Process-designer MCP e2e never runs on TeamCity (`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:46` excludes `McpE2E.ProcessDesigner`); it runs manually, one framework at a time, strictly sequentially. | r5 §6 | traced |

### 7.2 Guidance library (`KB`, `5f14ceb`)

| Fact | Evidence | Tag |
|---|---|---|
| `element-catalog.md` lists `webService` as NOT yet buildable ("MUST NOT be put in a build descriptor", "also marked READ-ONLY"); `element-catalog-details.md:70-71` "`webService` Call web service - call a registered service; outputs Success + Http status code. READ-ONLY here."; `process-modeling.md:182-185` lists it among constructs that "survive a save untouched as data"; the `bundle-source.json` description of `process-element-catalog` says webService remains read-only. | `guidance/mcp/guides/processes/element-catalog.md:136-141` (r1 §3.3, r5 §8) | traced |
| The read-only marker is pinned by `automation/Clio.Knowledge.Bundle.Tests/ProcessGuideContentPreservationTests.cs:191`; the formulaTask/scriptTask precedent replaced such a pin with a version-floor pin. | r5 §8 | traced |
| Routing collision: `configuration-webservice`, `configuration-webservice-tests` and `portal-service-routing` (backend guides) cover WRITING C# configuration services; a "call a web service from a process" request can land there. | `routing.md:185` (r5 §8) | traced |
| Reply budgets (19,000 B gate on the split set): `process-modeling` ~13 B left; `process-element-catalog-details` ~7,947 B; `routing` is over and excluded from that gate. Precedent new-article size: `process-send-email-attachments` 11,402 B reply; re-measured on the branch. | r5 §8 (python re-implementation, approximate) | measured |
| Every content change bumps `libraryVersion`; `sequence` derives from it and must not be reused; new articles carry no `legacyUris`. | `CONTRIBUTING.md:158-192` (r5 §8) | traced |
| A multi-instance Sub-process iterating a collection is buildable (ENG-99856 Sub-process element: support MULTI-INSTANCE (running the callee once per item of a collection)), so a response array mapped to a Collection process parameter has a downstream consumer. | r1 §3.3; element-catalog lines 144-145 (verdict r1-C8 (4)) | traced |

---

## 8. Stand `Creatio` (read-only inventory, 2026-11-02, r9; probes 2026-11-03)

| Fact | Tag |
|---|---|
| `ConfigurationVersion` 10.1.37; .NET Framework per the clio registration (`isNetCore: false`), not probed at run time: `describe-environment` failed with "Authentication failed" (correlation-id 2842a09c4b7b) while `list-packages` and ESQ reads worked. | measured |
| CrtProcessBuilder 1.6.6.131 (same as `origin/main` `da719366`); CrtProcessDesigner, ProcessDesigner, BaseProcessDesigner, ServiceDesigner 7.8.0; a package named `WebServiceUserTask` 7.8.0; cliogate 2.0.0.48. | measured |
| `SysSchema` with `ManagerName = 'ServiceSchemaManager'`: 3 rows, all shipped, none user-created: `BingSearchApiCall` (CrtCustomer360AI, `Search` GET, timeout 5000), `GeminiApiSearch` (CrtGeminiApiIntegraion, `GenerateContent` POST, timeout 120000), `CreatioMarketplaceApi` (CrtBase, retry 3, `GetAppPublishedVersion` GET, timeout 10000). All REST; no SOAP service; no `IQ5` auth block. (The 32 service schemas / 63 methods of 3.1.4 and g5 are the PackageStore corpus, not this stand.) Since 2026-11-03 the stand also holds the test service `UsrWscEchoService` (package `Custom`, REST, base URI = the stand root, one GET method `UsrWscGet` with header `UsrWscProbe` / `X-Wsc-Probe`, default Const `wsc-probe`; decisions Q16, Q17), on the cleanup list. Since 2026-11-05 also the SOAP 1.1 test service `UsrWscSoapService` (`Custom`, entered by hand, endpoint `http://localhost/UsrWscSoap`, never called; EM2 S2-S4, M-33), on the cleanup list. | measured |
| No service declares a Body request parameter: Gemini's POST has only 2 headers (`x-goog-api-key` from a SysSetting, `Content-Type` const) and builds its body as a raw JSON `string.Format` formula in `RequestBody`. So the stand has NO designer capture of a composite request body. Composite responses are present: Bing `WPValue` Object[] (22 items, nested `VDLinks`), Gemini `Candidates > CParts > PText`, `GMGSupports > GSGCIndices` Integer[], `GMWSQueries` Text[]. | measured |
| Processes with the element, found by owning package (a stand-wide `contains` filter on `SysSchema.MetaData` fails with an NRE): `BingSearchProcess` (`WebService1`, `query_In` bound to process parameter `SearchQuery`; unbound `WebService1Results1` CompositeObject with 32 items) and `ActionGeminiSearch` (`WebService1`, `RequestBody` formula, `Candidates_Out` read into process parameter `Candidates`). Both carry the four result parameters and 4 `BK15` rows. The 101 process schemas in non-Creatio packages are earlier test artefacts; their metadata was not scanned. | measured |
| `AdminUnitFeatureState` (state 1): `ProcessParameterCollections`, `ManageProcessCollectionParameters`, `PassProcessParameterValueAsArray`, `WebServiceDesigner`, `WebServiceOAuth20Auth` on. Client feature values read in the designer page on 2026-11-03: `AutoAddPackageDependenciesInProcesses` ON, `AutoAddPackageDependencies` OFF (EM10, decisions Q9). `DisableIgnoreErrorsInWebServiceUserTask` OFF and `DisableParametersWithNestedArrays` OFF (EM0, M-22). | measured |
| Sys settings referenced by the services: `BingSearchApiKey` (ShortText), `GeminiApiKey` (SecureText). | measured |
| Read-only capture technique: `execute-esq rootSchemaName=SysSchema columns={Name, MetaData} filter Name=<process>` returns the stored process metadata as JSON (about 58 KB for BingSearchProcess; one row per call under the 200,000-byte ESQ budget); `execute-esq rootSchemaName=VwWebServiceV2 allColumns=true` returns service definitions. | measured |
| The ENG-55920 fix is absent in the 10.1.37 build's binaries (measured on the local `core-bin` of that build, g2 §c); that the stand runs the same binaries is assumed from its version. EM12 cannot observe it here: with no echo endpoint (decisions Q17) the request body is not visible on this stand. | measured (binaries), inferred (stand) |
| Probe objects created 2026-11-03 with the owner's approval, all on the cleanup list: processes `UsrWscBk15Probe` (package `Custom`; now holds a wrong-case `IX2` on WebService2 and a parameter `UsrWscEm8Marker`), `UsrWscEm3Probe` (`Custom`), `UsrProcess_3389428` (package `UsrWscEm10Proc`; its element now calls `UsrWscEchoService`); package `UsrWscEm10Proc` (one `SysPackageDependency` row added by the designer); service `UsrWscEchoService` (kept as the EM1 service). Details in decisions Q4, Q6, Q9, Q12, Q14, Q16. | measured |

---

## 9. Jira and Confluence: what is stale

| # | Statement | Today | Evidence | Tag |
|---|---|---|---|---|
| 9.1 | ENG-92710 "Source: Task 21" | Task 22 in the Confluence index 4758143001 (v43, 2026-08-24) and in `CLIO/spec/process-design-service/task-list.md:157-166`; Task 21 is Delete data (ENG-92709 Delete data element) | r1 §1 | measured |
| 9.2 | "Task 5 (composite parameter types)" in the former Jira AC2 (before the 2026-11-03 rewrite) | Task 5 = ENG-91843 "Add and modify process parameters" (scalar + Lookup, Closed 2026-07-06, the only "is blocked by" link). Composite types moved to Task 37 = ENG-92730 "Complex / structured parameter types" (To Do, unassigned, no comments, updated 2026-08-31; names Call web service "the main consumer"). Carved out and Closed: ENG-96230 "Collection process parameter type" (2026-09-11), ENG-95984 "File process parameter type" File/FileCollection (2026-10-14), ENG-103162 Declared item shape for Collection process parameters (2026-10-26). Not done: single CompositeObject, Entity, Color, Image, Enum, SecureText; ENG-92730 never re-scoped. | r1 §1, §3; verdicts r1-C3, r1-C4 | measured |
| 9.3 | "Task 6" (mapping) | correct: ENG-91844 (Closed 2026-10-01), 6.1 ENG-92127 "Implement element's parameters mapping to the process parameters" (Closed 2026-07-06), formula authoring ENG-95891 "Formula authoring and evaluation in mappings and conditions" (Closed 2026-09-10) | r1 §1 | measured |
| 9.4 | Estimate "~3 days" | the "AI Est." column of 4758143001, defined on that page as working days for 1 person, the AI writing the code, each estimate dominated by review, unit/e2e tests, QA and capturing the designer serialization: total delivery days, not AI-coding days. No Jira time estimate (`timeoriginalestimate` null), Story Points empty. The page's +/-50% caveat ("a hard-to-capture element (e.g. Web service composite bodies, intermediate events) can swing +/-50%") is page-wide and names Filters (#4) as the single biggest risk. | verdict r1-C11, r10-C14 (3) | measured |
| 9.5 | State doc 4769087495 "Backend designer - capability status": "Call web service - T22 - NOT IMPL. - Needs the composite parameter types, not supported yet." | stale: last edit v90 2026-07-07, pinned to early package PRs; most rows it marks NOT IMPL. have shipped. Its premise is also wrong for the request side: the request body lives on the element's own parameters, which need no process-parameter type; Collection types exist for responses (section 6.3). | r1 §1; verdict r1-C8 | measured, traced |
| 9.6 | Implied element identity "Call web service user task" (r-notes' task brief, `WebServiceUserTask`) | the element is `ProcessSchemaWebService`, not a user task (section 1) | section 1 | traced, measured |
| 9.7 | ENG-92710 itself | Story, To Do, unassigned, no comments, no fixVersion, updated 2026-10-22; epic ENG-92704 Create BP via AI Toolkit Open; among the last unbuilt element stories (with Timer start, Message start, Intermediate events, Link entity, Inclusive/event gateways, Send chat message) | r1 §1-§2 | measured |

The correction is decided (decisions Q18, agreed 2026-11-03): the lead edits the description only, after the owner
approves the text; it also states that the element calls registered services only and points to ENG-105475
"Register a REST web service through the AI Toolkit".

---

## 10. Open facts and the measurement that settles each (status 2026-11-10, from the measurements file, sections 0 and 4)

| # | Open fact | Basis today | Settled by |
|---|---|---|---|
| OF-1 | States of `DisableIgnoreErrorsInWebServiceUserTask`, `DisableParametersWithNestedArrays` and whether `GlobalAppSettings.FeatureProcessParameterCollections` is on at run time. Settled 2026-11-03 for the client values (EM0, M-22): `ProcessParameterCollections` on, `DisableIgnoreErrorsInWebServiceUserTask` off, `DisableParametersWithNestedArrays` off, `AutoAddPackageDependenciesInProcesses` on, `AutoAddPackageDependencies` off, `EncodeWebServiceElementQueryParameters` off. The run-time `GlobalAppSettings` flag is not readable from the database; it is true in the config of every checked build (5.16) | 3.1.3, 5.13, 5.16 | EM0 (done, M-22); the run-time flag: EM6 |
| OF-2 | Settled 2026-11-03 (EM1, M-23; EM2 S1, M-24): the current designer's bytes for a composite REQUEST body (nested object array, nested scalar array, flattened object fields, required scalar array, header default), Success `L8`, the `BK15` rows, captions and order match the corpus-derived rows; new: an unbound Boolean In is `L8 {"GS2":"false","GS5":<P>}`, a Text constant stores its value in the resources only | 3.3-3.5; `UsrWscEm2CaptureA` | EM1, EM2 (done); EM2's read-back done in the final-cut campaign 2026-11-09 (M-36). Re-run on the claimed cut owed (measurements section 0). |
| OF-3 | The " - Input"/" - Output" suffix rule on duplicate captions. Settled for a fresh save 2026-11-03 (EM2, M-24): it counts TOP-LEVEL parameters only ("Status - Input" / "Status - Output"; the nested "Reference" stays plain). Settled for a re-save 2026-11-04 (EM5, experimental cut): a server load drops the suffix and a designer save puts it back, so the stored caption follows the last writer (serialization AD1) | `PWS:192-205`; M-24 | EM2 (done); EM5 (done) |
| OF-4 | Settled 2026-11-03 (EM3, M-15, M-25): the "Create from element" shape is a CompositeObject wrapper with first-level scalars only (3.6); a hand-made Collection is bound item-first (the container picker offers no web-service object array), binding an item binds the container and the nested collection, every level `GS1` 3 with an `[Element:{ws}].[Parameter:{uid}]` token, items carry no Tag | 3.6; `UsrWscEm3Probe`, `UsrWscEm3CaptureB` | EM3 (done) |
| OF-5 | Settled 2026-11-03 (EM4, M-20, M-26, M-27): a method change and a service change replace every parameter with new UIds, result params included; the 4 `BK15` rows are re-created (new row UIds, same `GT5`); `IX3` / `IX4` kept on a method change, `IX3` reset and `IX4` kept on a service change; caption kept on a method change, renamed on a service change; dependents keep the removed UIds and are not marked invalid | 3.2; `UsrWscEm4MethodChange` | EM4 (done) |
| OF-6 | A builder-made element, with and without `BK15` rows and Success `L8`, opens in the designer and re-saves without a diff in the compared keys; for designer-made elements, rows are kept and a row-less copy stays row-less and runs (2026-11-03, 3.5). Settled: passed on the experimental cut 2026-11-04 and in the final-cut campaign 2026-11-09 (M-37; steps 4, 5 not run). Re-run on the claimed cut owed (measurements section 0). | 3.5, 4.3 | EM5 (done) |
| OF-7 | Request body bytes on the wire (nested arrays; a one-item list from item-level bindings; unmapped items dropped; unmapped RequestBody null vs ""), Success/StatusCode/Response, a response read into a declared Collection with the feature on | 5.3-5.7, 5.14-5.16 | EM6, reduced by decisions Q17 (no echo endpoint), done in the final-cut campaign 2026-11-09: result params, StatusCode and Success measured (M-38); the one-item list from item-level bindings and the unmapped RequestBody (null, not "") measured by EM6-B's trace (M-39); the wire bytes and the `$.orders` response read stay unmeasured. Re-run on the claimed cut owed (measurements section 0). |
| OF-8 | Settled for the designer 2026-11-03 (EM7, M-28): a 404 completes the element with Success false and StatusCode 404 whatever `IX4` is; a call with no HTTP answer completes with Success false and StatusCode 0 when `IX4` is true and fails the element and the process when it is false. Settled for the builder-made element 2026-11-09 (M-40): it behaves the same. Re-run on the claimed cut owed (measurements section 0). | 5.11-5.13 | EM7 (done) |
| OF-9 | Settled 2026-11-03 (EM8, M-18, M-29..M-31): an unrelated builder modify deletes the tagged params of an unresolved element with no warning; a designer save deletes every parameter of it, result params included, and its `BK15` rows, marks it `BL11:false`, and leaves the readers' tokens dangling with no flag. On 2026-11-09 (M-41; re-measured on the claimed cut, M-51, M-52): when a reader of a pruned parameter exists, the unrelated modify is refused by the platform's validation, nothing is written, and the refusal carries N7 with the repair order | 4.3 (g4 §3 worked example) | EM8 (all halves done; the no-reader case not measured) |
| OF-10 | Settled 2026-11-03 (EM9, 2.3): the package already fails to compile on 8.3.1.4498 and 8.1.3.6947 without the reference, builds on 8.3.2.4199; a direct `IsIgnoreErrors` reference adds no new floor. Builds between 8.3.1.4498 and 8.3.2.4199 not tried | 2.3 | EM9 (done) |
| OF-11 | The builder save adds no `SysPackageDependency` for a cross-package service; the designer half is measured (2026-11-03, M-17: the designer save adds one row); the builder half is measured (2026-11-09, M-42: no row, with the notice). Re-run on the claimed cut owed (measurements section 0). | 4.1, 6.1 | EM10 (done) |
| OF-12 | Builder half: a create with a wrong-case method on the claimed cut writes the canonical `IX2`, and an unknown method is refused with the method list. The platform half is measured (2026-11-03, M-16: a case-only slip saves green, the next load prunes every tagged param, the run fails with `ItemNotFoundException`). The builder half is measured (2026-11-09, M-43): canonical `IX2` with a notice; the unknown-method refusal lists method codes without captions (F-3, open). Re-run on the claimed cut owed (measurements section 0). | 4.1 ordinal lookup, 4.2 | EM11 (done) |
| OF-13 | On 10.1.37 an array-in-array request body built with an unbound inner container is sent without the inner list | 2.3, 5.17 | EM12: not measurable on stand `Creatio` (decisions Q17); rests on the core unit tests and the ENG-55920 comment |
| OF-14 | Settled 2026-11-03 (EM8, M-29): a service-schema save does not evict a process already in the cache (describe keeps the pre-change set until it is evicted); a process first loaded after the save describes pruned, with no issue | g4 §6 Q2; M-29 | EM8 (done) |
| OF-15 | The stored (unsynced) parameter list can be read through `ReadSchemaMetaData` | 4.3 | part of EM8 if the design needs it |
| OF-16 | SOAP methods produce the same element parameter shape as REST | **settled 2026-11-05: yes** (EM2 S2-S4, M-33..M-35): same element keys, tree, result parameters and BK15 rows; no SOAP-only field on the element. Unmeasured: a non-array complex type (the service was entered by hand) | decisions Q20 D: SOAP built like REST |
| OF-17 | The package unit harness can bind the internal provider by reflection on both net472 and net8 | 6.6 | a probe test in the package (plan), not a stand EM |
| OF-18 | Two service schemas with the same name can coexist in one workspace (no server unique-name check) | 4.1 | no EM; the resolver's ambiguity refusal stays defensive |

---

## Corrections to the research

Where a verdict or a later gap note corrected a reader, the corrected statement is what this file uses.

1. **`IX4` exists** (r3 H3 and r6 C2 "the element-specific key set is exactly IX1-IX3", and the r6-C2 verdict's
   "orphan key, the next save drops it"). Both read the stale `TS25` checkout. Current core defines `IX4`
   IsIgnoreErrors from 8.3.2.3590 (`5e53b027c9b`); r6-C2's description is correct only for pre-8.3.2.3590 platforms
   (g2, verdicts r2-C2, r3-C2).
2. **BP2 order** (r3 §3.2 "65/66"). It is 57/66; the 9 exceptions carry a tagged parameter after the result parameters
   (g1 §a; r2 had it right).
3. **Result parameters are not on every element** (r2 C6, r9 S3 "every designer-built element carries four"). Success,
   Response, StatusCode are on 66/66; RequestBody on 14/66 (11/25 product), a sign of save age; the run time needs
   only the three (verdicts r2-C6, r9-S3, r6-C4, r8-C5).
4. **Success's value** (r6 C4 "L8 {GS2:"false"} on all 66"; r9 S3 "const false"). 61/66, all 25 product; the 5 others
   (ProcessTests) are `{}`; the form is `{GS2:"false", GS5:<process UId>}` with no `GS1`, i.e. Source None with a
   value, not a ConstValue (verdicts r6-C4, r9-S3).
5. **BK15 row names** (r9 S3 "named after the element"). Named after the element at creation; 10/212 differ after a
   rename; rows bind by `GT2`/`GT3`; `GT1` copies the parameter's value (verdict r9-S3, g1 §c).
6. **Describe does report Tags and nested items** (r9 S6). Refuted on the stand by a raw POST on 1.6.6.131; the reader
   saw a stale clio MCP client (8.1.0.135, bundled 1.6.6.50) that drops `tag`, items and `isOutput` (verdict r9-S6).
   Confirmed again on 2026-11-03 (decisions Q14), when an older session clio build showed the same loss; unbound
   inputs are omitted, as 6.2 says.
7. **Server and designer do not agree** (r3 C3 "server provider and designer JS agree"). They agree on top-level
   Name, Tag, type and required rule only; they disagree on `L18`, the conditional caption suffix, the result
   parameters and the source of `A3`/`A4` (verdicts r3-C3, r2-C5).
8. **Caption suffix is conditional** (r2 §3 table reads as unconditional). Only when more than one parameter across
   request and response shares a caption (verdict r2-C5 (3)).
9. **The sync matches by Name, not Tag** (r3 C7 "so Tags must equal service parameter Names exactly"). A wrong Name
   loses the parameter's UId and binding on the next load; a wrong Tag is not repaired by the sync and breaks the
   run time (verdict r3-C7).
10. **Not every load syncs, but every design-time load does** (r3 H8/C7 "every load"; r3-C7's verdict "cache hits
    skip"). The 8-arg overload, `SaveMetaDataValue`, `FindInstanceFromMetaData` hits and compiled instances skip it;
    `FindDesignItem` and the 6-arg override re-sync even on their own cache hits (g4 §0).
11. **Describe cannot read the stored list, and modify cannot skip the sync** (r6 §8 and C11 "describe must read the
    stored params; modify must not re-sync silently"). The platform syncs before builder code runs; the achievable
    requirement is to detect and report drift (verdict r6-C11, g4 §2, §5).
12. **A composite without `L18` does not send `{}`** (r3 H7, r3-C6 "empty body items"). The wire body walks the
    service's item properties and writes null or service defaults; only the intermediate value is empty; the response
    side receives N empty items (verdict r3-C6).
13. **Containers do not always have Source None, and ENG-55920 is narrower** (r1 C6, C7). A container can carry its
    own source (rows iterated); only an unmapped inner CompositeObjectList with mapped leaves was dropped; the 2024
    fix shipped in three 8.1.4 builds and was removed; 10.1 is the current boundary (verdicts r1-C6, r1-C7).
14. **There is no single-object response** (r1 C8 "storing a single-object response needs CompositeObject"). The
    service designer flattens non-array objects into scalar parameters; CompositeObject appears only as the generic
    "parameter from element" grouping (verdict r1-C8).
15. **The package's type refusal covers the declared path only** (r1 C4, r5 C6 "still refuses"). A `typeFromElement`
    mirror copies any type verbatim; an ObjectList mirror is accepted silently, itemless and unbound (verdicts r1-C4,
    r5-C6, r4-C7).
16. **Mirror refusal counts** (r4 H7 "210/210"). 194 CompositeObjectList items (165 on Out collections) are what
    `CloneItemProperties` sees; the 210 included ObjectList items and both directions (verdict r4-C7).
17. **ObjectList counts and depth** (r4 H8 "16 + 8", "depth 3"). The counts include test packages (product: 1 + 4);
    depth 3 counts ObjectList as a level; CompositeObjectList alone never nests deeper than 2 in the corpus (verdict
    r4-C8).
18. **Required flag and ItemValue** (r9 S2). `L6` never on OUT; the ItemValue child is suffixed by direction, tagged
    `<Name>ItemValue`, and is required under a required IN array while the ObjectList parent is not (verdicts r9-S2,
    r6-C3, r2-C3).
19. **The designer JS is local** (r9 S2 "not in the local checkout"). It is in `CORE/Terrasoft.Nui/...` (g1).
20. **Collections are not fed only by mapping** (r7 A5). Nested-only sources build a one-item list (2 product
    elements); the RequestBody facts come from source, not from Academy (verdict r7-A5).
21. **Timeout** (r7 A3). Label is "Maximum execution time, in seconds"; the unit needs no capture: ms by page, core
    doc and corpus (verdict r7-A3).
22. **Estimate unit** (r1 C11 "3 AI-days"; r10 C14 "developer days"). Working days for one person with the AI writing
    the code, review/QA/capture included; the +/-50% caveat is page-wide (verdicts r1-C11, r10-C14).
23. **Budget rules** (r5 C3). The encoder also escapes `"`, `<`, `>`, `&`, `+`, backtick; the ceiling was raised 134 -> 136 on
    2026-09-15 and 136 -> 137 on 2026-09-30 (deliberately, ENG-92711 met ENG-91844); no ticket has raised it since; "Q20" is ENG-92719's decision, cited by ENG-95985 as Q12 (verdict r5-C3).
24. **Merge order enforcement** (r5 C5 "cannot merge until the KB PR is published"). KB-before-clio is a policy backed
    by a test only when a new guidance name appears; package-first is a convention (verdict r5-C5).
25. **The provider can be bound in package tests** (r8 C2 "a package unit test cannot bind it"). Not through the
    generic API, but through reflection before construction or by setting the private field (verdict r8-C2).
26. **Corpus units** (r6 C5 "38 processes"). 38 process-schema files; 4 product schemas are versions of others (15
    product families with 21 distinct elements; 33 families overall); StudioFreeUpdate (9 of 19 product schemas) is
    internal tooling; 2 product elements call methods that do not exist (verdict r6-C5).
27. **Stand address.** r-notes and g-notes name stand hosts and a second stand; this file names the stand only as
    `Creatio` and omits every address.
