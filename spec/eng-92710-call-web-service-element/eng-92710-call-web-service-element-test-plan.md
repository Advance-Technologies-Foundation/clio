# ENG-92710 Call web service element: test plan

Written 2026-11-02; swept 2026-11-03 for the owner's decisions. Nothing in this file was built or
run, and nothing was written to Jira, GitHub or GHE; the stand writes of 2026-11-03 are the measurements file's (EM
status below).

Basis: crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history), clio `origin/master`
`6be5dde28`, clio-knowledge `origin/master` `5f14ceb` (`libraryVersion` 1.15.176), CrtProcessDesigner 7.8.0 + the
PackageStore corpus, core `TSBpm` trunk and `<core>` `468c4c71b` (8.3.3.1722), Creatio Academy, the
read-only inventory of stand `Creatio` (Creatio 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131) taken
2026-11-02, and the stand measurements of 2026-11-03 and 2026-11-05 (measurements file M-01..M-31 on 2026-11-03, M-32..M-35 on 2026-11-05, and the "Measured" blocks of the
decisions file). Research notes are cited as rN /
gN with a section; where a verdict of the fact-check corrected a reader, the corrected statement is used.

Every decision Q1-Q20 was agreed by the owner on 2026-11-03 (decisions file, the "Agreed by the owner" line of each Q);
a row that names a Q tests the agreed outcome. Where the agreement differs from the earlier recommendation (Q3: no
`L18` feature gate, no N11; Q5: no JSON check of a constant `RequestBody`, no conflict notice; Q7/Q13: IX4 written
directly, no capability guard; Q11: A3, agreed after EM4, `Success` / `Response` / `StatusCode` keep their UIds on a
retarget and the guard refuses while anything reads any other parameter of the element; Q17: EP option E4), the rows below follow the
agreement, and a test that existed only for a dropped item keeps its id as a one-line "dropped" row. Measurement ids
EM0-EM12 are the ones the measurements file defines. Status 2026-11-03: done EM0 (M-22), EM1 (M-19, M-23: the three
test services), EM2 S1 (M-24), EM3 routes (a) and (b) (M-15, M-25), EM4 S1-S3 (M-20, M-26, M-27), EM9 (local), the
designer halves of EM7 (M-28), EM10 (M-17) and EM11 (M-16), EM8's current-builder half (M-18), its service-save and
designer halves (M-29..M-31) and the Q4 BK15 probe (M-13); 2026-11-05: EM2 S2-S4, the SOAP capture (M-32..M-35). Still open: EM5; EM6 (reduced by E4); EM8's claimed-cut half (the notice); EM2's
read-back on the final cut; every builder half on the claimed cut (EM7, EM8, EM10, EM11, EM12). Under E4 (Q17) EM6's
request-byte check and EM12's run are not measurable on stand `Creatio`: the rows that depend on them rest on the core unit
tests (r3 §4) and the fix's issue comment (g2 (c)) and say so. A row that depends on an EM still open is written only
after the EM ran: no placeholder assertion is committed. Task ids P-A1..P-D4 are the plan's. Rows whose retarget
half needs story 2's P-D1 (the same-pair no-op and the replace) close together with story 2: TC-U-19, 23, 68, 78, 95,
98 and TC-I-06; each says so. AC1-AC9 are the 9
acceptance criteria of the Jira description (jira-description-draft file).

Sibling files: [README](README.md) · [facts](eng-92710-call-web-service-element-facts.md) ·
[usage](eng-92710-call-web-service-element-usage.md) · [reuse](eng-92710-call-web-service-element-reuse.md) ·
[serialization](eng-92710-call-web-service-element-serialization.md) ·
[design](eng-92710-call-web-service-element-design.md) · [decisions](eng-92710-call-web-service-element-decisions.md) ·
[measurements](eng-92710-call-web-service-element-measurements.md) · [plan](eng-92710-call-web-service-element-plan.md) ·
[estimate](eng-92710-call-web-service-element-estimate.md).

| Path alias | Root |
|---|---|
| `PB/` | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/` (worktree `<package worktree>`, plan §3) |
| `PBT/` | crt-process-builder `tests/UnitTests/CrtProcessBuilder.Tests/` |
| `CLIO/` | clio repository (`clio/`, `clio.tests/`, `clio.mcp.e2e/`) |
| `KB/` | clio-knowledge (`guidance/`, `automation/Clio.Knowledge.Bundle.Tests/`) |
| `PD/` | `<PackageStore>/CrtProcessDesigner/branches/7.8.0/Schemas/` |
| `PS/` | `<PackageStore>/` (shipped corpus) |
| `CORE/` | `<core>/TSBpm/Src/Lib/` (current core; has IX4) |
| `JS/` | `CORE/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/` (designer client) |
| `TS/` | `<Creatio2>/TSBpm/Src/Lib/` (stale 2025-07 checkout, lacks IX4; used only for the platform test case tables) |

Test ids: **TC-U-nn** package unit (PBT), **TC-P-nn** package parity (PBT), **TC-C-nn** clio unit (`clio.tests`),
**TC-I-nn** clio MCP e2e (`clio.mcp.e2e`, on stand `Creatio`), **TC-K-nn** clio-knowledge tests and gates,
**TC-01..TC-12** manual Jira cases (section 8). "Existing" means the test is already in the tree and only re-runs or
gains rows. Tags: **traced** = read in source (a hypothesis about run time), **measured** = observed in data, corpus
or stand; the EM that settles a traced row is named in it.

---

## 1. Coverage matrix

| Item | What must hold | Package unit | Parity | clio unit | e2e | KB | Stand | Manual |
|---|---|---|---|---|---|---|---|---|
| **AC1** the `webService` configuration (service, method, timeout in whole seconds, ignore-errors) accepted on create, `addElement`, `setElement`; request params incl. a composite body and the raw request body set through the existing mappings by the names describe returns | the block builds the core element on every route; values through the mapping surface by stored names, no aliases | TC-U-27..34, 56..62, 67, 87, 97 | TC-P-06, 10 | TC-C-01..05, 09 | TC-I-01, 06 | TC-K-01, 03 | EM1, EM2 (measured) | TC-01, 02, 04 |
| **AC2** an unknown or ambiguous service or method refused with the services (codes, captions) or the methods listed; a missing service pointed to registration; case-insensitive match, canonical spelling stored | resolver case tables, refusal texts, the no-service refusal | TC-U-15..22, 26, 105, 110, 111 | - | - | TC-I-03, 04 | TC-K-05, 13 | EM10, EM11 (designer halves measured) | TC-09, 10 |
| **AC3** inputs mapped in; response read into process params; a composite response through a declared Collection (<= 2 levels) with item mappings | scalar, constant, formula, process-parameter and item-level bindings into `_In` params; response read into a declared `Collection`, container and items bound (M-25); mirror, ObjectList and 3+ levels refused | TC-U-56..65, 67, 68, 101 | TC-P-06, 10, 11 | - | TC-I-01, 02, 08 | TC-K-03, 04 | EM2, EM3 (measured), EM6 | TC-01..04 |
| **AC4** ignore-errors stored on the element; Success, Http status code, Response body available as outputs | IX4 written when true; result params as ordinary outputs | TC-U-29, 66, 96 | - | - | TC-I-01, 09 | TC-K-06 | EM0, EM7 (designer half measured) | TC-05, 06 |
| **AC5** server serialization = a designer-built capture, incl. the mapping rows | element keys, BP2 order and key sets, L18, ItemValue, captions, result params, BK15 rows equal under the comparison rule (section 3) | TC-U-01..14, 41..46, 91, 94, 106 | TC-P-01..15 | - | TC-I-01 (stored bytes), 07 | - | EM2, EM3, EM4 (measured), EM5 | TC-11 |
| **AC6** a service or method change replaces the params as the designer does, Success / Http status code / Response body keep their identity, the dropped bindings are listed with their values, refused while another element or a process parameter reads a parameter that would be regenerated | replace; dropped bindings listed with their values; `Success` / `Response` / `StatusCode` keep their UIds (Q11 A3); the guard refuses while anything (another element, a process parameter, a flow condition, a formula) reads any other parameter of the element, tagged inputs, nested items and `RequestBody` included; `service` without `method` refused | TC-U-35..39, 46, 107, 108, 112, 113 | TC-P-12, 16 | - | TC-I-05 | TC-K-05 | EM4 (measured) | TC-07, 08 |
| **AC7** describe returns service, method, timeout, ignore-errors and the param tree incl. unbound inputs; an unresolved service or method is an issue; a modify that lets the platform prune says so | `webService` member, stored tree, issues, notice N7; the described block passes the server key check | TC-U-53, 54, 68..75, 77..80, 99, 102..104, 109, 114 | TC-P-03 | TC-C-03, 10 | TC-I-01, 06, 07 | TC-K-02, 05 | EM2, EM8 (current-builder, service-save and designer halves measured) | TC-11, 12 |
| **AC8** no data stored in the web service itself (defaults, authentication settings, base-URI credentials) in any builder output; the element's own bindings returned as describe returns them | no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output; the element's stored bindings are returned by design; the planted sentinel is absent from describe, refusals, notices (N4, N7, the retarget refusal), logs and test snapshots of builder output; the verbatim corpus fixtures carry no credential (TC-P-08) | TC-U-11, 25, 35, 37, 53, 75 | TC-P-08 | - | TC-I-07 | TC-K-12 | EM1 (Q16 part measured) | TC-11 |
| **AC9** tests, docs, guidance article, MCP surface | package, clio and KB suites green; clio surface aligned; e2e evidence run with Ignored = 0 on both frameworks | TC-U-87..93 | TC-P-01 | TC-C-01..16 | all TC-I | TC-K-01..13 | - | - |
| **Q1** core element; `WebServiceUserTask` refused on the generic route | handler builds `ProcessSchemaWebService`; the generic `userTask` route refuses `WebServiceUserTask` with a pointer | TC-U-27, 47 | - | TC-C-11, 13 | TC-I-03, 13 | TC-K-05 | EM2 (measured: the core class) | - (no manual case; the refusal is TC-U-47 and TC-I-03) |
| **Q2** contract `webService {service, method, timeoutSeconds?, ignoreErrors?}`; values via the mapping surface by stored names; no aliases | key check, wire names, stored-name addressing, alias refusal | TC-U-30, 31, 67, 87 | - | TC-C-01..05 | TC-I-01 | TC-K-01 | EM2, EM6 | TC-01 |
| **Q3** package-owned generator, nested items always emitted; platform sync tolerated (no-op) | shaper case tables; `L18` and the `ItemValue` child on every target, no feature gate and no N11 (agreed 2026-11-03); the real platform sync changes nothing on the shaper output | TC-U-01..14, 73, 82..86, 94, 98, 99, 100 | TC-P-03 | - | TC-I-01 | - | EM2, EM5 | - |
| **Q4** BK15 rows emitted (A) | 4 rows, designer key set, own source value, kept in step, cleaned on remove; `A2` not rewritten on a rename | TC-U-40..46, 106 | TC-P-04, 13 | - | TC-I-01 | - | Q4 probe, EM2, EM4 (measured: 4 rows; a method or service change re-creates them with new row UIds and the same `GT5`, M-26), EM5 | - |
| **Q5** composite request body: nested items AND RequestBody raw text; no JSON check of a constant RequestBody, no RequestBody-vs-items conflict notice in v1 | g3 cases (a)/(b); RequestBody from constant/formula/parameter, stored as given | TC-U-56..62 | TC-P-06, 10 | - | TC-I-01, 08 | TC-K-03 | EM6 (E4-reduced) | TC-02, 04 |
| **Q6** composite response v1 = declared Collection; mirror/ObjectList/3+ levels and the designer's "Create from element" CompositeObject wrapper deferred to ENG-92730 Complex / structured parameter types | declared 2-level read works; mirror (C13 pointer text), ObjectList and depth 3+ refused | TC-U-63..65, 101 | TC-P-06, 11 | - | TC-I-02 | TC-K-04 | EM3 (routes (a) and (b) measured, M-15, M-25), EM6 (reduced by E4) | TC-03 |
| **Q7** ignoreErrors written to IX4 directly; result params as ordinary outputs | IX4 written when true, omitted otherwise, never dropped; no capability guard (EM9: the package floor is already about 8.3.2); N3 while `DisableIgnoreErrorsInWebServiceUserTask` is on; Success/StatusCode/Response usable | TC-U-29, 66, 96 | - | - | TC-I-01, 09 | TC-K-06 | EM0 (measured: the feature off, M-22), EM7 (designer half measured, M-28), EM9 (done) | TC-05, 06 |
| **Q8** timeout in whole seconds, stored x1000 | 30 -> IX3 30000; 0/absent -> unset; describe divides back | TC-U-28, 77 | TC-P-09 | - | TC-I-01 | TC-K-06 | EM2 (measured: 7 s stored as `IX3` 7000, M-24) | TC-06 |
| **Q9** resolution order, case, ambiguity, canonical spelling, listing refusals, cross-package notice | resolver case tables | TC-U-15..26 | - | - | TC-I-03, 04 | TC-K-05 | EM10, EM11 (designer halves measured: the designer adds the dependency; a case-only method slip saves green, is pruned on load and fails at run time) | TC-09, 10 |
| **Q10** no new operation, no clio discovery tool; same-request references; the unknown-service refusal points to registration; a "task without details" guidance section | security pins unchanged; mappings reference generated params in the same request; registration pointer in both refusals; the agent stops when no service fits | TC-U-18, 31, 32, 88, 95, 105 | - | TC-C-08 | TC-I-01, 03 | TC-K-01, 13 | - | TC-01; agent runs AR-1, AR-2 |
| **Q11** method/service change = replace, guarded (A3) | regenerate, drop bindings with a notice that lists them with their values, keep the result params' UIds, refuse when anything (another element, a process parameter, a flow condition, a formula) reads any parameter of the element other than `Success` / `Response` / `StatusCode`, refuse `service` without `method` | TC-U-35..39, TC-U-107, 108 | TC-P-12 | - | TC-I-05 | TC-K-05 | EM4 (measured) | TC-07, 08 |
| **Q12** drift issues; notice on a modify that lets the platform prune an unresolved element | `serviceResolved`, `methodResolved`, nested drift, case-only method match, missing result param, `IsValid` false as issues; notice on any modify that leaves the element unresolved, a settings-only change of that element included (owner, 2026-11-05) | TC-U-53, 54, 71..73, 102..104, 114 | - | - | TC-I-07 | TC-K-05 | EM8 (current-builder half measured: the loss is real and silent; service-save and designer halves measured, M-29..M-31; the claimed-cut half open) | TC-12 |
| **Q13** platform floors: N5 on the pre-fix nested-list predicate, on every target; IX4 settled by Q7 | N5 names the version line and the remedy (map the container); suppression on fixed builds decided after EM12 | TC-U-52 | - | - | TC-I-12 | TC-K-06 | EM9 (done), EM12 (not measurable on stand `Creatio` under E4) | - |
| **Q14** describe read-back | `webService` member + stored tree incl. unbound inputs, nested items and tags, never re-derived, never defaults; today's describe (1.6.6.131) returns the tags and nested items and the bound inputs only (unbound inputs and an unbound `RequestBody` are omitted, M-31), and no `webService` member (measured 2026-11-03) | TC-U-69..75, 77..80, 102..104, 109 | TC-P-03 | TC-C-03, 10 | TC-I-01, 06, 07 | TC-K-02 | EM2, EM8 | TC-11 |
| **Q15** text budgets by swap; KB article; catalog flip; routing split | contract ceilings hold; catalog pin flipped to a version floor; routing names both guides | - | - | TC-C-06, 11, 12, 14 | - | TC-K-07..11 | - | - |
| **Q16** no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output; the element's stored bindings are returned by design | the planted sentinel is absent from describe, refusals, notices (N4, N7, the retarget refusal), logs and test snapshots of builder output; the verbatim corpus fixtures carry no credential (TC-P-08) | TC-U-11, 25, 35, 37, 53, 75 | TC-P-08 | - | TC-I-07 | TC-K-12 | EM1 (Q16 part measured: a header default stays in the service) | TC-11 |
| **Q17** stand captures and runs on the same stand, EP option E4, cleanup listed | test services use the stand root as base URI (no echo endpoint; E1-E3 not chosen); no external host; request-byte checks and EM12 are not runnable on stand `Creatio`; everything created is listed | - | - | - | all TC-I (cleanup), TC-I-08 | - | EM1..EM11 (EM6 reduced) | all |
| **Q18** Jira description corrections | owner-approved text, applied by the lead | - | - | - | - | - | - | process gate, not a test |
| **Q19** PR split and order | merge order package -> KB (published) -> clio; clio stays draft until the KB generation is published | - | - | TC-C-14 (red while process-web-service is unpinned) | - | TC-K-11 | - | process gate |
| **Q20** multi-instance refused; SOAP built like REST with N10 (D, 2026-11-05) | SOAP 1.1 / 1.2 build with N10 naming each element, a settings-only change on a SOAP element writes only the setting, retargets between SOAP and REST work; `setElement webService` on a designer-built element carrying `BP6` refused; describe reports such designer elements as found with an issue; `webService.multiInstanceOptions` on create is refused by clio's key check before sending | TC-U-24, 48, 79, 110..113 | TC-P-15, 16 | TC-C-02 | TC-I-03 (key-check refusal) | TC-K-05 | EM2 S2-S4 (M-32..M-35) | - |

---

## 2. Package unit tests (PBT)

### 2.1 Harness rules

| Rule | Why | Evidence |
|---|---|---|
| Every new fixture is `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` and inherits `BaseComposableAppTestFixture` | CI selects by category; `CiContractGuardTests.EveryFixture_CarriesACategoryTheCiFilterSelects` fails an uncategorised fixture | r8 §4 |
| Each test has `[Description]`, explicit AAA comments, `because:` on every assertion | house rule | r8 §4 (`PreconfiguredPageParameterSyncTests.cs`) |
| The process schema comes from `UserConnection.CreateSchema()` (a real `UId`); every stamp test starts with `schema.UId.Should().NotBe(Guid.Empty, ...)` | A3 = A4 = process UId is asserted on every level; an empty schema UId makes it vacuous | g1 (b): 751/751 params A3 = A4 = process UId, measured |
| A service schema is built in code by a `WebServiceSchemaFixtures` builder: `new ServiceSchema(Substitute.For<ISchemaManager>()) { ServiceMethods = { new ServiceMethod { Request = { Parameters = {...} }, Response = {...} } } }`, nested shapes through `ServiceParameter.ItemProperties` | `ServiceSchema` is constructible over any `ISchemaManager`; the package and its tests already reference `Terrasoft.Services.dll` | r8 B2, §2.1, §2.4 (model: TS `Terrasoft.Services.Tests/PetstoreServiceSchema.cs`, internal, so copied) |
| The service manager is `Substitute.ForPartsOf<ServiceSchemaManager>()` answering BOTH `FindItemByUId` and `GetInstanceByUId` (and `FindItemByName`), with misses answering `null` explicitly | the platform checks the item before the instance; an auto-substituted miss would hide dangling-reference tests | r8 §2.1, §4 (`SubProcessTestSupport.SetupSchemaManager`) |
| `SchemaManagerProvider.GetManager` is routed BY NAME: `"ServiceSchemaManager"` -> the service substitute, `"ProcessSchemaManager"` -> the process one; never `Arg.Any<string>()` for both; `manager.AppConnection.Returns(userConnection.AppConnection)` | the platform sync and `AnalyzePackageDependencies` resolve the service manager by string | r8 §4; `PBT/SubProcessTestSupport.cs:84-104` (r4 §4) |
| Resolver and describe tests go through `FakeWebServiceCatalog.Standard()` (a `Reads` counter, pattern `FakeReportTemplateReader`); only `WebServiceCatalogTests` touches the substituted manager | one class touches `ServiceSchemaManager`; the rest stays platform-free | r8 §5 |
| `new ProcessSchemaWebService()` (no `(ProcessSchema)` ctor), then attach; its parameter provider is `null` in the harness (`CoreApiContainer.Resolve` swallows and returns null) | constructing never throws; `SynchronizeParameters()` NREs unless a probe binds the provider (section 2.10) | r8 C2 verdict; r4 §3 |
| The DataValueTypeManager is `UserConnection.DataValueTypeManager` from the base fixture, not a bare `new DataValueTypeManager()` | known-good path in the package harness | r8 §6 |
| `[MockSettings(RequireMock.DBEngine)]` on fixtures that resolve sources or describe | the describer and the locator read through `DBExecutor` | r8 §4 |
| "Untouched" is asserted on bytes: the element's `WriteMetaData` JSON (and its BK15 rows) before == after | a field check misses a param removed and re-added with a new UId | pattern of the existing refusal fixtures |
| Platform round trip: `WriteMetaData(JsonDataWriter)`, then `ReadMetaData` after ONE `reader.Read()`; or `MetaDataTestUtils.ApplyMetaData` | the shipped `Terrasoft.TestFramework.dll` carries `MetaDataTestUtils` | r8 §5 |

**lit marker.** Platform UIds are literals in tests, the independent oracle; a newer local core-bin (10.1.37) cannot
hide a CI (8.3.4) difference (g2 (a), measured): ManagerItemUId `652b598d-cdd5-49d5-a86d-aafac88213f3`; MaxSizeText
`C0F04627-4620-4bc0-84E5-9419DC8516B1`, Text `8B3F29BB-EA14-4ce5-A5C5-293A929B6BA2`, Boolean
`90B65BF8-0FFC-4141-8779-2420877AF907`, Integer `6B6B74E2-820D-490E-A017-2B73D4CCF2B0`, ObjectList
`4B51A8B5-1EE9-4437-9D58-F35E083CBCDF`, CompositeObjectList `651EC16F-D140-46DB-B9E2-825C985A8AC2`, Object
`84ED6865-9692-4c98-AAED-4D15B96A95C2` (`CORE/Terrasoft.Core/DataValueType.cs:104, 124, 129, 175, 287, 292, 2594`).

**probe marker.** The row calls a platform member whose behaviour in the harness is not yet measured (provider
binding by reflection; `ServiceSchemaManager` substitution under dev-n8, where a core test carries
`#if NETFRAMEWORK // TODO #CRM-44720`, r8 §2.2; CRM-44720 is now ENG-4179 "(not_mvp) [RFMT] Adapt Terrasoft.Services",
English rendering of the Russian Jira title). The first CI run of the draft PR decides; the fallback is named.

Run: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf` and `-c dev-n8`
(junction `.application` into the worktree first). Iterating:
`--filter "FullyQualifiedName~WebService|FullyQualifiedName~SubProcess"`; before every commit the whole project on
both configurations.

### 2.2 New fixtures

| Fixture | Plan task | Harness specifics |
|---|---|---|
| `WebServiceParameterShaperTests` | P-A2 | no DB, no manager; `CreateSchema()` + a detached-then-attached element; service schemas from `WebServiceSchemaFixtures`; no feature seam: nested items are always emitted (Q3, 2026-11-03) |
| `WebServiceCatalogTests`, `WebServiceResolverTests` | P-A1 | substituted `ServiceSchemaManager` via `TestUserConnection.SetServiceSchemaManager` (catalog); `FakeWebServiceCatalog` (resolver) |
| `WebServiceApplierTests` | P-B2, P-B3, P-B5, P-D1, P-D2 | DBEngine mock; `CreateGraphBuilder` / `CreateProcessOperations` with the new applier; the dependency snapshot captured before the request (`FindDependents` throws before `Capture`) |
| `WebServiceResultMappingRowTests` | P-B4 | as above; rows read from `schema.Mappings` |
| `WebServiceGuardTests` | P-C2, P-C3, P-D3 | as above; no platform-capability seam (dropped by Q7, 2026-11-03: IX4 is written directly by P-B3 and tested in `WebServiceApplierTests`, TC-U-29); the `DisableIgnoreErrorsInWebServiceUserTask` read of N3 is faked per row (TC-U-96) |
| `WebServiceMappingTests` | P-B6 (mapping coverage, estimate WP M: TC-U-56..64, 66, 67), P-C1 (TC-U-68, closing with P-D1), P-C6 (C13 mirror refusals: TC-U-65, 101) | `ProcessDesignTestSupport.CreateMappingService`; DBEngine mock |
| `WebServiceElementHandlerDescribeTests` | P-C1, P-D3 | DBEngine mock; `CreateDescriber`; TC-U-109 replays the described block through the package's request key check (`UnknownRequestKeyDetector`; the read-back-only keys are derived automatically because the write and describe members are both keyed `webService`; nothing to register) |
| `WebServicePlatformProbeTests` | P-A3 | real internal provider bound by reflection (section 2.10); both configurations |
| `SubProcessSyncCharacterizationTests` | P-A4 | written on `main` BEFORE the generalisation; pins every sub-process sync notice verbatim |
| rows in `ProcessDesignServiceWireContractTests`, `CrtProcessBuilderAppTests`, `ProcessElementFactoryTests`, `UserTaskElementHandler*Tests`, `ProcessVersionCloneFactoryTests`, `ProcessGraphBuilderTests` | P-B1, P-B2, P-C2, P-D4 | existing harness of each file |

### 2.3 Parameter shaper (Q3) - `WebServiceParameterShaperTests`

The shaper is pure: a service method in, an element parameter tree, caption resources and result params out. It ports
the client algorithm (`JS/process-webservice-schema.js`), because the platform provider builds flat top-level params
only (r8 B4, r2 C5 verdict).

| TC | Test | Proves | CI |
|---|---|---|---|
| TC-U-01 | `Shape_ShouldMapEveryServiceTypeToTheDesignersType` [Text, empty, unknown name -> MaxSizeText; Integer, Float, Boolean, DateTime, Guid -> same-named type; Object array -> CompositeObjectList; Integer array, Text array -> ObjectList; a non-array Object is not a shaped type here: it is refused, TC-U-94] | the client and server type rule (r2 C3 verdict, traced both sides) | lit |
| TC-U-02 | `Shape_ShouldNameAndTagEveryLevel` [request `query` -> `query_In`, L17 `query`; response `WPValue` -> `WPValue_Out`; nested `VDLinks` -> `VDLinks_Out`, L17 `VDLinks`] | name = service param name + `_In`/`_Out`, Tag = the service param name, at top level AND nested (539/539 corpus tagged params, measured r2 C3 verdict); L12 0 In / 1 Out written on every param | - |
| TC-U-03 | `Shape_ShouldStampEveryLevelWithTheProcessAndTheElement` | A3 = A4 = process UId and IL2 = element UId on top-level, nested and result params (297/297, 242/242, 212/212 measured, g1 (b)) | - |
| TC-U-04 | `Shape_ShouldApplyTheRequiredRule` [In scalar required -> L6; In scalar optional -> none; Out required (`UsrResponse` IU7 true) -> none; In Object array required -> L6; In scalar array required -> parent none, ItemValue child L6; a UrlSegment and a required Query -> L6; result params none] | `IsRequired = (!IsArray \|\| type == Object) && required && In`; the ItemValue child is computed with `isArray:false` (r9 S2 and r6 C3 verdicts; g1 (b)); the 2 ProcessTests `UsrArray_In` with L6 are stale drift, not a rule; EM2 and EM4 S2 measured the rule on the designer (`UsrWscIdsItemValue_In` L6, its parent none; `UsrWscId_In` and `UsrWscQ_In` L6, M-24, M-26) | - |
| TC-U-05 | `Shape_ShouldGiveAScalarArrayOneItemValueChild` [Text array In; Integer array Out; Guid array] | exactly one child `<p>ItemValue_<dir>`, L17 `<p>ItemValue`, item primitive type (Text -> MaxSizeText), no L18, caption "Item value" (24/24 measured, g1 (b)) | lit |
| TC-U-06 | `Shape_ShouldRecurseObjectArraysIntoNestedItems` [Gemini-like `Candidates` > `CParts` > `PText`; `GMGSupports` > `GSGCIndices` (Integer array) > `GSGCIndicesItemValue`; an object array holding a nested object array and a nested scalar array] | L18 mirrors the service `IU6` tree recursively, in service order, to depth 4 (r9 §2.2, r6 C3 verdict: 486 pairs, 0 L18-vs-IU6 mismatch) | - |
| TC-U-07 | `Shape_ShouldKeepFlattenedObjectFieldsAsScalars` [service params with Path `$.canceledInfo.user.username`; in-item Path `vcs-root-instance.id`] | a non-array object reaches the element as JSONPath scalars, never as a container (r1 C6 verdict) | - |
| TC-U-08 | `Shape_ShouldOrderParametersAsAFreshDesignerBuild` | BP2 = [In, request order][Out, response order][Success, Response, StatusCode, RequestBody] (g1 (a): the fresh-build order; 57/66 corpus elements keep it, the 9 others are re-synced elements) | - |
| TC-U-09 | `Shape_ShouldAddTheFourResultParameters` [a method with params; a method with none] | Success Boolean Out, Response Text (not MaxSizeText) Out, StatusCode Integer Out, RequestBody Text In; no L17, L6, L18; Success L8 `{GS2:"false", GS5:<process>}` (61/66 measured), the other three `{}` (g1 (b), r9 S2 verdict (4)) | lit |
| TC-U-10 | `Shape_ShouldWriteCaptionResources` | key `BaseElements.<El>.Parameters.<A2 path joined by '.'>.Caption`, nested path by NAMES; service captions; result captions "Success", "Response body", "Http status code", "Request body"; ItemValue "Item value"; the " - Input"/" - Output" suffix only where the same caption occurs more than once among the TOP-LEVEL params across the method's request and response; a nested item never gets it and is not counted (design C3, serialization §4, SQ3; measured by EM2, M-24: `UsrWscStatusIn_In` / `UsrWscStatus_Out` are "Status - Input" / "Status - Output", while the nested `UsrWscOrders_In.UsrWscOrdNo_In` and the top-level `UsrWscEchoText_Out` both stay plain "Reference") | - |
| TC-U-11 | `Shape_ShouldLeaveEveryServiceParameterUnbound_AndCopyNoDefault` [service params with a Const `Authorization: Bearer <sentinel>` default, a SysSetting default, a Const non-secret header; a top-level Boolean Out (`UsrWscEchoOk`), a nested Boolean Out, a Boolean In (`UsrWscFlag`)] | tagged and nested non-Boolean L8 = `{}`; tagged and nested Boolean L8 = `{GS2:"false", GS5:<process>}` (14/14 corpus: 3 top-level Out, 11 nested Out, e.g. BingSearchProcess `VIFFriendly_Out`, `VNCache_Out`, measured; serialization §2.4, I7); an unbound Boolean In is `L8 {"GS2":"false","GS5":<P>}` as well (SQ5, no corpus instance; measured by EM2 on `UsrWscFlag_In`, M-24); no service `DefValue` reaches L8 or a resource (Q16) | - |
| TC-U-12 | `Shape_ShouldMarkEveryParameterValueSerializable` | `IsValueSerializable = true`: the platform sync forces it on every matched param, so anything else is rewritten on the next load (g4 §1 step 4, §5) | - |
| TC-U-13 | `Shape_ShouldAgreeWithThePlatformProvider_AtTopLevel` | the provider's own case tables (TS `Terrasoft.Services.Tests/ProcessSchemaWebServiceParametersProvider.Tests.cs:46-70, 316-371`: types, IsRequired, empty-result guards) copied as rows: names, tags, types and required flags of the top level equal the provider's; nested and result params are outside it | - |
| TC-U-14 | `Shape_ShouldBeDeterministic_ExceptForUIds` | two shapes of one method are equal with UIds normalised; every UId is fresh | - |
| TC-U-94 | `Shape_ShouldRefuseANonArrayObjectParameter` [a service method with a non-array `Object` request param; the same on the response side] | refused with the design §5.1 text ("... has an object parameter 'p' that is not an array; such parameters are not supported"), nothing shaped or written (design C3 type rule, C11). 0 in the corpus and unreachable through the service designer (57/57 Object params are arrays, r1 C8 verdict, measured); the server provider would map it to Object `84ED6865` (r2 C5 verdict, traced), which the design does not follow | lit |
| TC-U-100 | `Shape_ShouldAlwaysGenerateNestedItems` [an object array, a nested object array and a scalar array get L18 / the `ItemValue` child as TC-U-05, 06; the shaper has no feature input, and no notice is raised on any shape] | Q3 as agreed 2026-11-03: nested items on every target, no `ProcessParameterCollections` gate and no N11. The builder does not copy the designer's client gate, which reads a DB feature row that a clean 8.2.1+ database lacks (an open designer bug; decisions Q3); the run-time re-keying reads the config value, on in every checked build from 7.13 to 10.2.931 (decisions Q3, measured in build configs). A regression guard against reintroducing the gate | - |

### 2.4 Catalog and resolver (Q9, Q16, Q20) - `WebServiceCatalogTests`, `WebServiceResolverTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-15 | `Resolve_ShouldFindTheService_ByUId_ThenName_ThenCaption` [UId text; exact name; caption "Bing search webservice"; a name equal to another service's caption -> the name wins] | Q9 order (g5 option A, the `SubProcessReader` shape) |
| TC-U-16 | `Resolve_ShouldIgnoreCase_AndStoreTheManagerItemUId` [`bingsearchapicall`] | IX1 = the manager-item UId (what the designer writes, g5 (a)); case-insensitive while `FindItemByName` is ordinal first match (g5 (b)) |
| TC-U-17 | `Resolve_ShouldRefuseAnAmbiguousService_ListingTheCandidates` [two names differing only in case; two services sharing a caption] | ambiguity refused; 0 such cases in the corpus, so defensive until g5 open question 4 is answered |
| TC-U-18 | `Resolve_ShouldRefuseAnUnknownService_ListingEveryServiceName` | the refusal lists service codes with captions (sanitised, `SafeText.Sanitize`), nothing else about them (g5 option A), and ends with the registration pointer ("... has to be registered first in Studio > Web services (base URI, methods, authentication); then repeat the call.", Q10 addition 1) |
| TC-U-19 | `Resolve_ShouldMatchTheMethodIgnoringCase_AndWriteItsCanonicalName` [`search` -> IX2 `Search`] | the platform lookup is ordinal and an inexact spelling empties the params silently (g4 §1 step 2, g5 (b)); unit side of EM11, whose designer half measured it on stand `Creatio` 2026-11-03 (a case-only slip saves green, the next load prunes every tagged param, the run fails with `ItemNotFoundException`); N2 (design §5.2) names the input and the canonical spelling ("webService.method 'search' was written as 'Search'.") on create and retarget; the retarget half needs story 2's P-D1 and closes with story 2 |
| TC-U-20 | `Resolve_ShouldRefuseAnUnknownMethod_ListingTheServicesMethods` | lists the resolved service's method names only |
| TC-U-21 | `Resolve_ShouldRefuseTwoMethodsThatDifferOnlyInCase` | 0 in the PackageStore corpus (32 service schemas, 63 methods, g5 (b)) and on stand `Creatio` (3 service schemas); defensive |
| TC-U-22 | `Create_ShouldRefuseABlockWithoutServiceOrMethod` [no `service`; no `method`; empty strings] | an element with an empty `ServiceSchemaUId` is cleared by the next load, result params included (g4 §1 step 1); refused before anything is written |
| TC-U-23 | `Resolve_ShouldNoticeACrossPackageService_AndAddNoDependency` [service in another package; in the process package] | notice names the service and its package; no `SysPackageDependency` write (the package has no dependency applier, g5 (b)); the designer does add one (EM10 designer half, measured 2026-11-03: one new row `UsrWscEm10Proc -> CrtGeminiApiIntegraion`), so the notice says the dependency is not added; the builder half is traced and runs on the claimed cut; the retarget half needs story 2's P-D1 and closes with story 2 |
| TC-U-24 | `Resolve_ShouldBuildASoapService_WithANotice` [REST, Undefined, SOAP 1.1, SOAP 1.2] | Q20 D: every type builds; N10 for SOAP only (EM2 S2, M-33: the designer's SOAP element has the REST shape) |
| TC-U-25 | `Refusals_ShouldCarryNoServiceSecret` [a planted sentinel as basic-auth password, as a Const `Authorization` default, inside a base URI] | the sentinel is absent from every refusal, notice and captured log line of the resolver (Q16; secrets exist in the corpus, g5 (c)) |
| TC-U-26 | `Resolve_ShouldReadTheCatalogOncePerRequest` | `Reads` counter; a build with three web-service elements reads the catalog once |

### 2.5 Handler and applier (Q1, Q2, Q8, Q10, Q11) - `WebServiceApplierTests`

| TC | Test | Proves | CI |
|---|---|---|---|
| TC-U-27 | `Create_ShouldBuildTheCoreWebServiceElement` [build `elements[].webService`; `addElement`] | `BL1` `Terrasoft.Core.Process.ProcessSchemaWebService`; ManagerItemUId stamped (the ctor does not, r4 §3); BO2/BO3 true; BN2 `69;55`; IX1 = item UId, IX2 canonical | lit |
| TC-U-28 | `Create_ShouldStoreTheTimeoutInMilliseconds` [30 -> IX3 30000; 1 -> 1000; 0 -> no IX3; absent -> no IX3; negative or fractional -> per the design file's validation sentence] | Q8; IX3 is ms and the designer stores seconds x 1000 (r7 A3 verdict) | - |
| TC-U-29 | `Create_ShouldStoreIgnoreErrors_OnlyWhenTrue` [true -> IX4 true; false, absent -> no IX4] | Q7 as agreed: a direct write, no capability guard (EM9: the package already fails to compile below 8.3.2, so the reference adds no floor); the writer omits the default (g2: `WriteValue("IX4", IsIgnoreErrors, false)`) | - |
| TC-U-30 | `Create_ShouldRefuseATypeWithoutItsBlock_AndTheBlockOnAnotherType` [`type: webService` with no block; the block on a `userTask`, a `subProcess`] | strict `EnsureBlockMatchesHandler`, like `subProcess` / `scriptTask` (r4 §1) | - |
| TC-U-31 | `Build_ShouldLetTheSameRequestReferenceTheGeneratedParameters` [`mappings[]` into `WS1.query_In`; a formula reading `WS1.StatusCode`; a conditional flow on `WS1.Success`; another element's block reading `WS1.Response`; a mapping onto a param the method does not produce -> refused, listing the element's params] | Q10: selection runs in the `afterElementsPlaced` hook (P-B5) | - |
| TC-U-32 | `Modify_ShouldResolveGeneratedParametersInOperationOrder` [`addElement WS1` then `addMapping` into `WS1.query_In`; reversed] | in order it builds; reversed the locator refuses naming the param | - |
| TC-U-33 | `SetElement_ShouldTouchNothingElse_WhenOnlyTimeoutOrIgnoreErrorsChange` | BP2, resources and BK15 rows byte-identical | - |
| TC-U-34 | `SetElement_ShouldBeANoOp_ForTheSameServiceAndMethod` [same spelling; different case] | no write, no notice: N2 fires on create and retarget only, and a setElement re-sending the same service and method in different case is neither (design §5.2 N2, 4.8 step 5) | - |
| TC-U-35 | `SetElement_ShouldReplaceEveryGeneratedParameter_WhenTheMethodChanges` [the service carries a planted sentinel as a Const header default] | Q11: old tagged params and their bindings gone; the new set in fresh-build order; one notice (N4) listing every dropped input binding in the write shape WITH its value, constants and formulas alike, so one call re-applies those that still apply; a constant `RequestBody` binding is dropped and comes back in N4 with its value (asserted); `Success` / `Response` / `StatusCode` keep their UIds (Q11 A3, TC-U-107); `IX3`, `IX4` and the element caption kept (the caption follows the designer, M-26); the sentinel is absent from N4 (Q16). Designer oracle (EM4 S2, M-26: the designer asks "Are you sure you want to change method? All parameters will be lost.", replaces every param with new UIds, re-creates the 4 BK15 rows with new row UIds and the same `GT5`, keeps `IX3`, `IX4` and the caption) | - |
| TC-U-36 | `SetElement_ShouldReplace_WhenTheServiceChanges` | same as TC-U-35 across services; the caption becomes "<service caption> <N>": `setElement` has no caption member, so a service change always renames (the caption follows the designer, M-26, M-35). Designer oracle (EM4 S3 and M-20, measured 2026-11-03, M-26): the confirm "Are you sure you want to change element schema? All parameters will be lost."; every param replaced, Success / Response / StatusCode come back with NEW UIds, the 4 BK15 rows are re-created with new row UIds and the same `GT5`, `IX3` reset, `IX4` kept, the element name is kept and the caption becomes "<service caption> 1"; the builder asserts the replacement of every tagged param, the KEPT result-param UIds (Q11 A3, TC-U-107; the oracle does not compare UIds), `IX3` reset, `IX4` kept and exactly 4 rows (TC-U-46) | - |
| TC-U-37 | `SetElement_ShouldRefuseAChange_ThatStrandsADependent` [a formula reads `WS1.Version_Out`; another element maps from `WS1.Candidates_Out.CParts_Out` (a nested item); a conditional flow reads a tagged output; a process parameter (a declared Collection) bound to `WS1.Candidates_Out`; a formula reads the tagged input `WS1.query_In`; a formula reads `WS1.RequestBody`; the service carries a planted sentinel as a Const header default] | `IProcessDependencySnapshot.FindDependents(element, survivingParameterUIds: {Success, Response, StatusCode UIds}, includeOwner: false)` (sub-process retarget guard; the package's `ProcessElementDependencyScanner` walks `schema.Parameters` too; Q11 A3 stated exactly: every parameter other than the three is regenerated with a new UId, so a reader of a tagged input, a nested item or `RequestBody` dangles as a reader of a tagged output does; the element's own references die with its parameters, hence `includeOwner: false`); the refusal ("its parameters are still read by ...") names each dependent and the param it reads; the sentinel is absent from it (Q16); bytes unchanged | - |
| TC-U-38 | `SetElement_ShouldRefuseAChange_WhenADependentReadsASameNamedOutput` [the new method also has `Version_Out`, same type; a formula reads `WS1.Version_Out`] | refused like TC-U-37, naming the formula; bytes unchanged. References are by param UId, and design §3.2 regenerates every parameter other than `Success` / `Response` / `StatusCode` with a fresh UId, so a same-named same-type tagged output does not keep its readers (Q11's edge, answered by the design). Only those three keep their UIds (Q11 A3, TC-U-107) | - |
| TC-U-39 | `Apply_ShouldWriteNothing_WhenAnyMemberIsRefused` [unknown method with a valid timeout; a negative `timeoutSeconds` with a valid method and `ignoreErrors: true`] | validate-all-before-write (the sub-case "`ignoreErrors` on a platform without IX4" is dropped with the guard, Q7, 2026-11-03) | - |
| TC-U-108 | `SetElement_ShouldRefuseAServiceWithoutAMethod_ListingItsMethods` [`setElement` with `webService {service}` naming another service and no `method`] | story 2 (P-D1): refused with the new service's method list; nothing written (element bytes and BK15 rows before == after) | - |
| TC-U-40 | `RemoveElement_ShouldRemoveTheElementAndItsMappingRows` | existing `ProcessGraphBuilder.RemoveElement` deletes rows whose `TargetMetaPath` contains the element UId (`PB/Graph/ProcessGraphBuilder.cs:1915-1922`, g1 (c)) | - |

### 2.6 BK15 rows (Q4) - `WebServiceResultMappingRowTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-41 | `Create_ShouldWriteOneMappingRowPerResultParameter` | 4 rows, in BP2 result order (66/66 measured, g1 (c)) |
| TC-U-42 | `Row_ShouldCarryTheDesignersKeySet` | key set exactly `BL1 UId A2 A3 A4 GT2 GT3 GT5 GT1`, no GT4; A2 = the element name when the rows are written (not rewritten on a rename, TC-U-106); A3 = A4 = process UId; GT2 `[Element:{el}].[Parameter:{p}]`, GT3 = the param UId; GT5 fresh, not GT3 and not any BP2 UId |
| TC-U-43 | `Row_ShouldHoldItsOwnSourceValue` | Success GT1 = `{GS2:"false"}` WITHOUT GS5, others `{}`; a separate value object, so the param's `SourceValue` is set before the row is added (the setter would otherwise alias it, g1 (c)) |
| TC-U-44 | `BindingTheRequestBody_ShouldKeepItsRowInStep` | after a formula binding on RequestBody, GT1 == L8 incl. GS5 (14/14 measured), through the platform `SourceValue` setter |
| TC-U-45 | `Rows_ShouldNeverTargetTaggedOrNestedParameters` | 0 rows for service params (297 + 242 measured) |
| TC-U-107 | `SetElement_ShouldKeepTheResultParameters_SoTheirReadersSurvive` [a conditional flow reads `WS1.Success`; a formula reads `WS1.StatusCode`; a process parameter bound to `WS1.Response`; change the method, then the service] | Q11 A3: the change is not refused; `Success` / `Response` / `StatusCode` keep their UIds, so the flow, the formula and the process parameter still resolve; every other param is regenerated with a fresh UId; a reader of any of them (tagged input or output, nested item, `RequestBody`; element, process parameter, flow or formula) still refuses (TC-U-37) |
| TC-U-46 | `MethodChange_ShouldLeaveExactlyOneRowPerResultParameter` | no duplicate, no orphan row after TC-U-35 and TC-U-36 (the designer re-creates exactly 4 rows on a method and on a service change, new row UIds, same `GT5`, EM4, M-26; `GT5` is outside the comparison, W10) |
| TC-U-106 | `RenameElement_ShouldLeaveTheRowsA2Unchanged` [rename `WS1` to `WS9` by `setElement`] | Q4 as agreed 2026-11-03: `A2` is not rewritten on a rename, as the designer does not rewrite it either (the corpus carries stale `A2`, AD2); every other byte of the rows unchanged |

Q4 was agreed 2026-11-03 as A (write the rows). The Q4 probe (`UsrWscBk15Probe`, measured 2026-11-03) showed: a
designer-built element gets exactly 4 rows in this key set (Success `GT1` without `GS5`); a designer copy/paste is
row-less; a designer reload and re-save neither duplicates nor rewrites the rows and keeps the copy row-less; the
row-less element runs. EM2 measured the same 4 rows on `UsrWscEcho` (M-24); EM4 measured that a method or service
change re-creates them with new row UIds and the same `GT5` (M-26). EM5 (a builder-made element re-saved in the
designer) is still to run; only if it shows the
designer duplicating or rewriting builder rows does Q4 flip to "omit" (this fixture then asserts no row, and W10
takes BK15 rows as optional on the builder side, serialization §8.2).

### 2.7 Guards and notices (Q1, Q7, Q12, Q13, Q20) - `WebServiceGuardTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-47 | `UserTaskRoute_ShouldRefuseWebServiceUserTask_PointingToTheBlock` [create `userTask` `userTaskName: WebServiceUserTask`; `addElement`; by its UId e7acb2e5-...] | Q1: keyed on the resolved schema UId (pattern `RefuseUnconfigurablePageTask`, `PB/Elements/UserTaskElementHandler.cs:382-398`); today it builds (r4 H2); other user tasks unaffected |
| TC-U-48 | `MultiInstance_ShouldBeRefused_OnAWebServiceElement` [`setElement webService` on a designer-built element carrying `BP6`] | Q20: refused, element bytes unchanged; describe of the same element reports it as found with an issue (TC-U-79). `webService.multiInstanceOptions` on create never reaches the package: clio's key check refuses it before sending (TC-C-02, TC-I-03) |
| TC-U-49 | dropped (Q7, 2026-11-03) | the capability guard and its "`ignoreErrors: true` refused where IX4 is absent" refusal are gone: EM9 showed the package already fails to compile below 8.3.2 |
| TC-U-50 | dropped (Q7, 2026-11-03) | the direct write is TC-U-29 |
| TC-U-51 | dropped (Q7, 2026-11-03) | the source scan for `IsIgnoreErrors` outside a guard has nothing to guard; EM9 (done) was its compile proof and ruled the guard out |
| TC-U-52 | `NestedListRequestBinding_ShouldRaiseAPlatformNotice` [a leaf bound inside a CompositeObjectList nested in a CompositeObjectList `_In` whose own source is None -> N5; the same nested list bound to a collection of its own -> no notice; a single-level item -> no notice; a nested `_Out` read -> no notice] | Q13, design 4.12 N5 predicate (a sourceless nested CompositeObjectList with a bound leaf, r1 C7 verdict (a)): before the core fix (absent in 8.3.4, 10.0.0.950 and 10.1.37, the stand; present in 10.1.830 and 10.2.931, g2 (c)) such an inner list is sent without its value (null); a mapped inner container is sent. N5 is raised on every target (the fix state is not readable) and names the affected builds in words, with no Jira key ("Creatio builds before the platform fix for arrays inside arrays (10.1.37 among them)"), and the remedy (map the container) (Q13 as agreed). Under E4 EM12 is not measurable on stand `Creatio`: the run-time drop rests on the fix's issue comment and the core unit tests, and whether such a process saves without error stays unmeasured; suppression on fixed builds is decided after EM12, if a source for it is added. A nested ObjectList is outside N5 (section 11) |
| TC-U-53 | `ModifyOfAnotherElement_ShouldNotice_WhenTheMethodNoLongerResolves` [`GetMeetingTranscript` shape: WS3 method missing, `setElement` on a formula task; a resolvable WS -> no notice; an element with an empty service UId -> the notice says every param incl. results is cleared; the service carries a planted sentinel as a Const header default, absent from N7 (Q16)] | Q12: the design load prunes tagged params and the save persists it (g4 §3); measured 2026-11-03 (EM8 current-builder half): one unrelated `addParameter` through the current clio + package deleted the 8 tagged params of an unresolved element from the database, exit 0, no warning, `IX2` unchanged |
| TC-U-54 | `ModifyOfAnotherElement_ShouldNotice_WhenTheServiceIsMissing` | `serviceResolved` false gives the same notice |
| TC-U-114 | `ModifyOfTheElement_ShouldNotice_OnlyWhenItStillDoesNotResolve` (`WebServiceGuardTests`) | story 2 (P-D3), owner 2026-11-05: N7 also covers the unresolved element configured in the request; a settings-only `setElement` leaves it unresolved and gets N7, a block that resolves it (a retarget) gets none |
| TC-U-55 | `Notices_ShouldBeWarningsOfTheSavedEdit_Only` | notices land in the request's `IProcessDesignNotices`; a refused request carries none |
| TC-U-95 | `CreateAndRetarget_ShouldListTheGeneratedParameters` [create; `addElement`; `setElement` to another method; `setElement` with only a timeout change -> no N1] | N1 (design §5.2, Q10): one info notice per Create and retarget naming the element, `service.method`, every generated input (nested items in brackets, required marked `*`, `RequestBody`) and output (`Success`, `Response`, `StatusCode`), by stored name, so the same request's mappings can address them; no service default in the text (Q16); the retarget half needs story 2's P-D1 and closes with story 2 |
| TC-U-96 | `IgnoreErrors_ShouldBeSavedWithANotice_WhenTheFeatureGateIsOn` [the `DisableIgnoreErrorsInWebServiceUserTask` read reports on: `ignoreErrors: true` -> IX4 written + N3; reports off -> IX4 written, no N3; `ignoreErrors` absent with the gate on -> no N3] | N3 (design 4.12, §5.2): saved, not refused; the run time ignores the flag while the feature is on (g2 (b), traced); EM0 measured the feature off on stand `Creatio` (M-22), so the "on" rows stay unit-only |
| TC-U-97 | `EndOfRequest_ShouldNoticeARequiredInputWithoutASource` [required `UsrWscReq_In` unbound -> N8 naming it; bound in the same request by a later `addMapping` -> no N8; a required ItemValue child of a required scalar array unbound -> N8; an unbound optional input -> no N8] | N8 (design C12, §5.2): judged at the END of the request, after every mapping of it applied; the designer marks such an element invalid (r2 §6, traced) |
| TC-U-98 | `Shape_ShouldNoticeADuplicateNestedNameOrTag` [a service tree with the same item name at two depths of one composite param; the same Tag at two depths; unique names -> no N9] | N9 (design C3 duplicate guard, §5.2): on Create and retarget; the run time builds one flat dictionary with `Add` and throws (r3 C6 verdict (3), g3 §6, traced; not reproduced, EM1 avoids the shape on purpose); the retarget half needs story 2's P-D1 and closes with story 2 |

### 2.8 Mapping into and out of nested items (Q2, Q5, Q6, Q7) - `WebServiceMappingTests`

The mapping rules need no change for the corpus shapes (g3 §5). These rows prove it on a `ProcessSchemaWebService`
element, which no test uses today (g3 M8).

| TC | Test | Proves |
|---|---|---|
| TC-U-56 | `Map_ShouldBindALeafFromAProcessParameter_LeavingItsContainerUnbound` [`PutBuildsInQueue.PropertiesProperty_In.PropertyValue_In`; ObjectList child `roles_In.rolesItemValue_In`] | g3 case (a): GS1 3, GS2 `[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{p}]#]`, GS5 schema UId; the parent keeps L8 `{}` (byte-equal to `ExecuteUpgradeBsf`, measured) |
| TC-U-57 | `Map_ShouldWriteAFormulaOrConstant_OntoTheNamedItemOnly` | no collection rule runs for an unstructured source (g3 M2); the stored bytes are the designer's (EM2, EM4 S1, M-24): a Text constant `L8 {"GS1":1,"GS5":<P>}` with the value only in the `.Value` / `.DisplayValue` resources, an Integer constant `L8 {"GS1":1,"GS2":"1","GS5":<P>}`, a formula `GS1` 3 with the token text in `GS2` |
| TC-U-58 | `Map_ShouldBindTheContainer_WhenAnItemReadsAnotherCollectionsItem` [`UsrUpdateLabels_In.UsrLabelsAdd_In` <- `RD.ResultCompositeObjectList.Name`] | g3 case (b): P1 binds the parent to the source collection; equal to the stored parent of `UsrTestJiraReadDataRuntimeCollection` (measured target) |
| TC-U-59 | `Map_ShouldBindACollectionToACollection_AcrossTwoWebServiceElements` | COL <-> COL passes exact type match (g3 M5) |
| TC-U-60 | `Map_ShouldRefuseAListOfObjectsItemFromAListOfScalarsItem` | COL <- ObjectList refused; the whole plan writes nothing (g3 M5, PU-44 pattern) |
| TC-U-61 | `Map_ShouldRefuseASourceDeeperThanTheTarget` [`Candidates_Out.CParts_Out.PText_Out` into a depth-1 item] | P1 depth rule (g3 M4) |
| TC-U-62 | `Map_ShouldSetTheRequestBody_FromAConstantAFormulaOrAParameter` [constant JSON text; `string.Format(...)` formula (the Gemini shape); a Text process parameter] | Q5 escape hatch; stored as the mapping service writes it, with no JSON check of the constant and no notice when Body parameters are bound too (Q5, 2026-11-03); the generated params are untouched (the run time replaces the generated body when RequestBody is non-null, traced, core unit tests r3 §4; not measurable on stand `Creatio` under E4) |
| TC-U-63 | `Map_ShouldReadTheResponseIntoADeclaredCollection` [declare `Candidates` Collection [`ContentParts` Collection [`Text` LongText]]; map `Candidates.ContentParts.Text` <- `WS1.Candidates_Out.CParts_Out.PText_Out`] | g3 case (c): P1 binds both parents innermost first; equal to the stored Gemini mapping under the role rule; Tag is read nowhere on the mapping path |
| TC-U-64 | `Map_ShouldRefuseAThirdLevelOrScalarListResponse` [`...GSGCIndices_Out.GSGCIndicesItemValue_Out` into any declarable target; a declared Collection <- an ObjectList output] | Q6: depth and type refusals; Complex / structured parameter types follow-up |
| TC-U-65 | `TypeFromElement_ShouldRefuseAWebServiceCollectionMirror_PointingToADeclaredCollection` [mirror of `WS1.rows_Out` (CompositeObjectList); the same mirror from a non-WS collection source -> the existing message unchanged] | C13 (design 4.14, plan P-C6): the mirror still refuses non-GUID item Tags (`PB/Parameters/ProcessParameterService.cs:972-1012`), and for a WS source the message carries the design §5.1 C13 text verbatim ("... cannot be mirrored: its items carry service names, not column identities. Declare a Collection with itemProperties and map the items from '<el>.<param>'."); nothing written |
| TC-U-101 | `TypeFromElement_ShouldRefuseAWebServiceObjectListMirror` [mirror of a WS ObjectList output, e.g. `WS1.UsrWscEchoIds_Out`] | C13: today such a mirror silently creates a parameter with no shape and no binding (r5 C6 verdict (1), traced); now refused with the same pointer text as TC-U-65; process bytes before == after |
| TC-U-66 | `Map_ShouldReadResultParametersAsOrdinaryOutputs` [`WS1.Success` -> Boolean param; `StatusCode` -> Integer; `Response` -> Text; a conditional flow on `StatusCode == 200`] | Q7 |
| TC-U-67 | `Map_ShouldAddressParametersByTheirStoredNames_Only` [`query_In` resolves; `query` (the Tag) refused with the locator's not-found text] | Q2: no alias |
| TC-U-68 | `DescribeThenReapply_ShouldRoundTripNestedBindings` [cases a, b, c] | describe decodes each binding (a constant or formula with its value, through the generic `parameters[]`) to names that re-apply unchanged; a `webService` block sent with it goes back unchanged, read-back-only keys included; P-C1, closes with story 2's P-D1 |

### 2.9 Describe (Q12, Q14, Q16, Q20) - `WebServiceElementHandlerDescribeTests`

| TC | Test | Proves |
|---|---|---|
| TC-U-69 | `Describe_ShouldReportTheWebServiceMember` | `buildType` `webservice` (the lowercase build token); `webService {service, serviceUId, method, timeoutSeconds, ignoreErrors, serviceResolved, methodResolved, parameters[], issues[]}`; IX3 30000 -> 30 |
| TC-U-70 | `Describe_ShouldReportTheStoredTree_NotTheServicesTree` [element tree differs from the service: an extra stale tagged param, a missing nested item] | Q14: direction, tag, type, items and bound read from the element; an item's direction comes from its own L12 (items come back `isOutput:false` even when Out, r9 S6 verdict) |
| TC-U-71 | `Describe_ShouldFallBackToTheRawUId_WhenTheServiceIsGone` | `serviceResolved` false, `service` null, `serviceUId` the raw `IX1`, an issue; never throws |
| TC-U-72 | `Describe_ShouldRaiseAnIssue_WhenTheMethodDoesNotResolve` | `methodResolved` false; the issue says the next save of this process permanently removes its service params (g4 §5: the reliable signal on every load path; EM8's current-builder half measured the deletion 2026-11-03) |
| TC-U-73 | `Describe_ShouldRaiseAnIssue_ForNestedDrift` [missing nested item; extra nested item; a CompositeObjectList with no L18 (the `UsrUpdateLabelsCopy_In` shape)] | the platform sync never touches L18, so nested drift is visible on every path (g4 §5); a composite without L18 sends items rendered from the service tree with null leaves or service defaults, and receives N empty items (verdict r3-C6). No feature-dependent variant (no N11, Q3, 2026-11-03) |
| TC-U-74 | `Describe_ShouldRaiseNoIssue_ForEveryShapeThePackageWrites` [TC-U-01..12 outputs; designer elements without RequestBody; Success L8 `{}`] | no false alarm (RequestBody on 14/66 only; Success `{}` on 5 test elements, measured) |
| TC-U-75 | `Describe_ShouldNeverCarryAServiceDefaultOrCredential` | the serialized reply carries no planted sentinel (Const header default, basic-auth password, base URI user info) (Q16) |
| TC-U-76 | dropped (Q7, 2026-11-03) | no guard: describe reads IX4 directly, covered by TC-U-69 |
| TC-U-77 | `Describe_ShouldReportTheTimeoutAsUnset_WhenIX3IsAbsent` [absent; 600000 -> 600; 1500 -> 1.5] | Q8 and design §2.5: `ms / 1000` reported as a fraction, not rounded (1500 ms -> 1.5, never 2); corpus values are multiples of 1000 (r7 A3 verdict) |
| TC-U-78 | `DescribeThenReplay_ShouldChangeNothing` [the described `webService` member and mappings sent back unchanged, read-back-only keys included] | element bytes and BK15 rows equal; no notice. The same-pair no-op needs story 2's P-D1, so the row closes with story 2 |
| TC-U-79 | `Describe_ShouldReportAMultiInstanceElement_AsFoundWithAnIssue` | Q20: found, not an error; nothing refused on describe; a SOAP element is an ordinary element with no issue (Q20 D) |
| TC-U-80 | `Describe_ShouldReportAGenericWebServiceElement_ThatLostItsMethod_WithoutThrowing` [the `GetMeetingTranscript` capture shape] | the 2 product elements whose method is missing (r6 C11 verdict) describe cleanly |
| TC-U-99 | `Describe_ShouldRaiseAnIssue_ForADuplicateNestedNameOrTag` [a stored element whose composite param holds the same item name, or Tag, at two depths; unique -> no issue] | design §5.3 row "duplicate nested name / tag" (N9 text) on read-back; never throws |
| TC-U-102 | `Describe_ShouldRaiseAnIssue_WhenTheMethodMatchesOnlyIgnoringCase` [stored IX2 `usrwscecho` against the service's `UsrWscEcho`; exact spelling -> no issue] | design §5.3 row "method found only case-insensitively": the issue names the stored and the service spelling and says the next load drops every tagged parameter and the run fails, because the platform looks methods up exactly (g4 §1 step 2; EM11 designer half, measured 2026-11-03: saved green, pruned on load, `ItemNotFoundException` at `ServiceClient.CreateRequest`) |
| TC-U-103 | `Describe_ShouldRaiseAnIssue_WhenAResultParameterIsMissing` [a stored element without `StatusCode`; without `Success`; without `Response`; all present -> no issue; `RequestBody` absent -> no issue] | design §5.3 row "a result param missing": the run time reads them with `GetByName`, which throws (r9 S3 verdict, traced); a missing `RequestBody` is read optionally and raises nothing (r6 C4 verdict) |
| TC-U-104 | `Describe_ShouldRaiseAnIssue_WhenTheElementIsMarkedInvalid` [`BL11` false on the element; on one of its params; valid -> no issue] | design §5.3 row "`IsValid = false` (BL11)": a stored `BL11` false is reported as found. Measured basis (EM4 S2, M-26): the designer writes `BL11:false` on the web-service element itself while a required input is unbound (`UsrWscId_In` after the method change); it does NOT mark a dependent whose read parameter was removed (the Formula task and the conditional flow keep the removed UIds with no `BL11`), so the issue text must not claim that cause; never throws |
| TC-U-105 | `Resolve_ShouldRefuse_WhenNoServiceIsRegistered` [an empty service manager] | the "no service registered" refusal (design 5.1, Q10 addition 1): says that none is registered and how to register one; no empty list is printed; nothing is created |
| TC-U-110 | `SoapNotice_ShouldBeRaisedOncePerElement` [two elements on one SOAP service] | Q20 D: N10 names its element, so two elements give two notices |
| TC-U-111 | `Resolve_ShouldExposeTheServiceType_WithoutRefusingSoap` | the resolver reports REST / SOAP 1.1 / SOAP 1.2 and refuses none of them (Q20 D) |
| TC-U-112 | `SetElement_ShouldChangeOnlyTheSetting_OfASoapElement` | story 2: a settings-only change on a stored SOAP element writes only `IX3` / `IX4`, no N10 (M-34) |
| TC-U-113 | `SetElement_ShouldRetarget_BetweenSoapAndRest` [SOAP -> REST; REST -> SOAP] | story 2: a full retarget both ways as for REST (Q11 A3); N10 only onto SOAP; no SOAP leftover after SOAP -> REST (M-35) |
| TC-U-109 | `Describe_ShouldReplayTheWebServiceBlock_ThroughTheServerKeyCheck` [a described `webService` member sent back unchanged as `elementUpdate.webService`, read-back-only keys kept] | story 1 (P-B1, P-C1): CrtProcessBuilder 1.6.6.136 (bundled by clio master `f86d8098f`, 2026-11-03) refuses undeclared request keys (`UnknownRequestKeyDetector`); the read-back-only keys (`serviceUId`, `serviceResolved`, `methodResolved`, `issues`, `parameters`) are derived automatically because the write and describe members are both keyed `webService`; nothing to register. The server key check accepts them as read-back keys, so the replay passes and a misspelled write key is still refused |

### 2.10 Platform probes (Q3, Q9, Q12) - `WebServicePlatformProbeTests`

The provider is internal and resolved in the element's constructor; a package test binds it through reflection
(`TestDIUtilities.Configure` + `Type.GetType("Terrasoft.Services.ProcessSchemaWebServiceParametersProvider,
Terrasoft.Services")`) BEFORE constructing the element (r8 C2 verdict). All rows: probe, both configurations.

| TC | Test | Measures |
|---|---|---|
| TC-U-81 | `Harness_ShouldHoldNoParametersProvider_ByDefault` | the field is null and `SynchronizeParameters()` NREs: the reason the package never calls it unguarded |
| TC-U-82 | `PlatformSync_ShouldBeANoOp_OnTheShapersOutput` | Q3: bytes before == after on a shaped element (names, Tag, types, IsRequired, IsValueSerializable as the provider maps them; L18 and results kept) |
| TC-U-83 | `PlatformSync_ShouldPruneTaggedParameters_WhenTheMethodDoesNotResolve` [unknown name; wrong case] | tagged removed, results kept (g4 §1 steps 2, 5-6); unit side of EM11 (designer half measured 2026-11-03: the wrong-case element's tagged params vanish on load, results stay) |
| TC-U-84 | `PlatformSync_ShouldClearEverything_WhenTheServiceUIdIsEmpty` | results and BK15 rows gone too (g4 §1 step 1) |
| TC-U-85 | `PlatformSync_ShouldAppendANewServiceParameterFlat_AtTheEnd` | no L18, after the result params, new UId (g1 (a) step 4, g4 §4) |
| TC-U-86 | `PlatformSync_ShouldKeepTheNestedItemsOfAMatchedParameter` | UId, SourceValue and L18 kept (g4 §1 step 4) |

Fallback: if the reflection binding fails on a configuration, the row is replaced by EM5 (designer re-save) and EM8
(modify on the stand) evidence and its `[Description]` says so. That is decided on the first CI run of the draft PR.

### 2.11 Contract, DI, factory, round trip, clone - rows in existing fixtures

| TC | Test | Proves |
|---|---|---|
| TC-U-87 | `WebServiceMembers_ShouldKeepTheirWireNames` (new method in `PBT/EntryPoints/WebService/ProcessDesignServiceWireContractTests.cs`) [`ProcessElementDescriptor.WebService` and `ProcessElementUpdateDescriptor.WebService` -> `webService`; `service`, `method`, `timeoutSeconds`, `ignoreErrors`; describe type members incl. `serviceResolved`, `methodResolved`, `issues`] | a renamed `DataMember` compiles and is then dropped silently by the serializer |
| TC-U-88 | `OperationContractCount_MustStayPinned` (existing) | Q10: no new operation |
| TC-U-89 | `CompositionRoot_RegistersEveryElementHandler` and `..._ResolvesEveryCollaborator_FromScope` (existing, `PBT/CrtProcessBuilderAppTests.cs`) | the handler, catalog, shaper, row writer and the guards (multi-instance refusal, Q20; no capability or nesting-feature seam) resolve in a scope; `CreateGraphBuilder` and `CreateProcessOperations` carry the new applier (r8 §4) |
| TC-U-90 | `SupportedTypes_ShouldNameWebService` (factory) | the token list derives from handlers; `webService` is accepted, `callWebService` is not (clio validates `webservice` already, r5 §1.3) |
| TC-U-91 | `MetaData_ShouldRoundTripTheElementKeys` | IX1-IX4, BO2/BO3, BP2 survive write/read (TS `ProcessSchemaWebService.Tests.cs:107` pattern) |
| TC-U-92 | `CreateVersionClone_ShouldKeepTheWebServiceElementOwnedByTheNewVersion` (`ProcessVersionCloneFactoryTests`) | A3/A4/GS5 of every param and BK15 row = the clone's UId (P-D4) |
| TC-U-93 | `SubProcessSync_ShouldEmitExactlyThePinnedNotices` (`SubProcessSyncCharacterizationTests`) + `EveryFixture_CarriesACategoryTheCiFilterSelects` (existing) | P-A4 proof: written on `main` before the `Snapshot`/`Diff` generalisation; the generalising commit changes no existing test file (`git diff --stat <before>..<lift> -- tests/` shows only this file, added one commit earlier) |

---

## 3. Parity tests (PBT)

Comparison rule: WSCR, [serialization](eng-92710-call-web-service-element-serialization.md) §8.2 (W1-W14), with the
allowed differences of §8.3 (AD1-AD5 and AD10 for committed tests; AD6 dropped by EM0). That file owns the rule; this
plan quotes its ids and restates none of it.
Every parity test names the W and AD ids it applies; any other difference fails, and the failure lists every
difference by path. In particular: "no value" is `{}` only (W8), the generic element keys incl. `BN2` are excluded
(W12), BK15 `GT5` is removed (W10), resources are compared on the stand only (W13), and the keys a pattern-B
`PROVENANCE.md` lists as redacted are excluded on both sides (W14).

Corpus-sweep-only differences. Three differences occur in the corpus but in none of the committed pairs of 3.1; the
sweep below needs them, the committed tests do not. They are serialization §8.3 AD7-AD9:

| AD | Difference | Where (measured) |
|---|---|---|
| AD7 | Success `L8` `{}` instead of `{"GS2":"false","GS5":<process>}` | 5 test elements (61/66 carry the Boolean default) |
| AD8 | a stale CompositeObjectList without `L18` | `UsrUpdateLabelsCopy_In` |
| AD9 | a param type drifted from its service | `Chatid`: Text in the service, Integer in `UpdateBsfNotification` |

The committed fixture set (3.1) is chosen so that only W10 (`GT5` removed), AD2 (stale BK15 `A2`, e.g.
`CheckMarketplaceAppNeedUpdateSubProcess` rows say `WebService2`) and AD5 (`ExecuteUpgradeBsf`, 2020, predates
`RequestBody`) apply to it; AD4 (culture caption), the W9 re-synced-order clause (9/66 elements, g1 (a)) and AD7-AD9
apply to the corpus sweep.

### 3.1 Pattern A: corpus pairs, committed verbatim

The set is serialization §8.1's: three pairs, six files, about 295 KB; resource files are not committed (W13).
Committed under `PBT/Fixtures/WebService/` with a `PROVENANCE.md` (source path, size, copy date, SHA-256 as the LAST
cell, the layout the existing oracle's hash reader expects) and one `.gitattributes` line
`tests/UnitTests/CrtProcessBuilder.Tests/Fixtures/WebService/* -text`. Measured here 2026-11-02: process captures are
CRLF without a BOM (first bytes `7b 0d 0a`); service files carry a BOM (`ef bb bf`). PROVENANCE says so.

| File (relative to `PS/`) | Role | Size | SHA-256 |
|---|---|---|---|
| CrtBase/branches/7.8.0/Schemas/CheckMarketplaceAppNeedUpdateSubProcess/metadata.json | process: 4 In (Query, Const/SysSetting defaults), 4 Out scalars, Success L8 false, 4 rows | 109,079 B | d79f889800f948e017e725055a673dc87ebb36b85dfbbf5a295a855c94e92358 |
| CrtBase/branches/7.8.0/Schemas/CreatioMarketplaceApi/metadata.json | its service | 4,355 B | a06df74e0d9e54ecac35a52db74b1211f247f9f94f166f48960e5066df36eb18 |
| CrtGeminiApiIntegraion/branches/7.8.0/Schemas/ActionGeminiSearch/metadata.json | process: composite response to depth 4, ObjectList children, RequestBody formula, `Candidates` read into a declared Collection | 31,704 B | ce76f30e64728241eacbb723dfdeeb4bfffa380ded5b057cfd1262f4b4924772 |
| CrtGeminiApiIntegraion/branches/7.8.0/Schemas/GeminiApiSearch/metadata.json | its service | 13,279 B | 85b797388b354fe7f94aa1436569199b86e7adf023f3981e42f9c9f0b8694766 |
| StudioFreeUpdate/branches/7.8.0/Schemas/ExecuteUpgradeBsf/metadata.json | process: composite REQUEST `PropertiesProperty_In`, item-level bound | 100,336 B | ae3b9d32a194c659039e51556d48da1867ffd7a94025e3b0120489f154a1cef9 |
| StudioFreeUpdate/branches/7.8.0/Schemas/teamcitybpmonline_BSF/metadata.json | its service (auth block `IQ5` with SysSetting references, `IV2` 2, measured here) | 36,553 B | 75bd4318f266f7e706c85f7d09a036a8b68105b777894bf40540bcd9fea5c187 |

Oracle: extend `PBT/FileProcessingCaptureOracle.cs` (fixture folder as a parameter; `L1`, `L6`, `L12`, `L17`, recursive
`L18`; BK15 rows; roles) rather than fork it. No resource oracle over committed XML: captions are compared on the
stand (TC-P-05, W13).

Fixture `WebServiceParityTests`:

| TC | Test | Proves |
|---|---|---|
| TC-P-01 | `Capture_ShouldHashToTheValuePinnedInProvenance` [x6] | a hand edit or a line-ending normalisation fails |
| TC-P-02 | `Capture_ShouldHoldTheShapeTheOracleReads` [the 3 elements] | tripwire: counts of tagged, nested, result params and rows per element; a refresh that changes them fails here first |
| TC-P-03 | `Element_ShouldEqualTheShippedElement` [CheckMarketplace `GetMarketplaceAppPublishedVersionWebService`; ActionGeminiSearch `WebService1`; ExecuteUpgradeBsf `PutBuildsInQueue`] | AC5: the shaper, given the captured service, stores the captured BP2 under WSCR (pairing W1, UIds and roles W2-W5, key sets W6, values W7, no value W8, order W9, result params W11 with AD5 for ExecuteUpgradeBsf, generic keys W12) |
| TC-P-04 | `MappingRows_ShouldEqualTheShippedRows` [the 3 elements] | Q4: W10 (`GT5` removed, `GT1` exact, key set without `GT4`) with AD2 |
| TC-P-06 | `Bindings_ShouldEqualTheShippedBindings` [`PropertyValue_In` <- process param (case a); Gemini `Candidates` declared Collection read (case c); Gemini RequestBody formula] | AC3/Q5/Q6: the mapping service writes the captured bytes (g3 §2, §4) under W5 and W7 |
| TC-P-07 | `ServiceCapture_ShouldLoadThroughThePlatformReader` | probe (r8 B6): `ServiceSchema.ReadMetaData` via `MetaDataTestUtils` on a capture; if it cannot, the services are rebuilt in code from the capture and drift-guarded by the hash |
| TC-P-08 | `Fixtures_ShouldCarryNoCredential` | AC8 / Q16 govern builder output, not these files: the pattern-A captures are copied verbatim from the shipped PackageStore corpus into the internal package repository and may carry the shipped services' non-credential `IU5` defaults; they are not redacted (the SHA pins of TC-P-01). The test asserts no credential: no `IQ5` value with `IV2` 1, no Const `Authorization` default in any committed file (measured here: none in the 3 services) |

TC-P-05 is a stand test (3.2): resources are not committed.

Corpus sweep (a script, not committed; result into the package PR body): the shaper over every corpus element whose
method resolves (62 elements), against its stored BP2 under WSCR with AD1-AD5 and AD7-AD9. Expected from
the measured scan (r6 C3 verdict): 486 service/process parameter pairs, 0 name, Tag or L18 mismatch, 484/486 types
(AD9).

### 3.2 Pattern B: the stand designer captures, committed redacted

The corpus has no product composite request body bound from a process collection, no product ignoreErrors (IX4 true
only on 2 BusinessRulesTest elements, saved 2025-11-20, verdicts r2-C2, r3-C2), no timeout set by
this designer version, and it is from older designers. EM2/EM3/EM4 captured these on stand `Creatio` (designer 7.8.0,
platform 10.1.37) on 2026-11-03 (M-24..M-27); EM2 S2-S4 captured a SOAP element and its switch to REST on 2026-11-05
(M-33..M-35). Their reads are committed REDACTED, not byte-verbatim, under `PBT/Fixtures/WebService/Stand/`
(serialization §8.1): the stand address is replaced by `<stand>` before saving (measurements rule 11), in the service
`IQ1` and any value that carries it. `PROVENANCE.md` records per file the capture date, the platform and package
versions from EM0, the redacted keys and the SHA-256 of the committed (redacted) bytes; under WSCR the redacted keys
are excluded (W14) and every other byte is compared as for pattern A. EM5 is a re-save diff, not a committed fixture.
The `[Description]` names the captures. TC-P-13 is written only after EM5 ran.

| TC | Test | Proves | Source |
|---|---|---|---|
| TC-P-05 | `Captions_ShouldEqualTheStandCapture` [EM2 resources; EM3 resources] | W13 with AD3 (until the source re-check) and AD4: the builder element's `SysLocalizableValue` rows equal the capture's, keyed by the full resource key, values exact in the authoring culture; the suffix rule of TC-U-10. Resources are compared on the stand only (serialization §8.1), so this row is not a PBT test: its evidence is TC-I-01's `SysLocalizableValue` rows (and TC-I-02's for EM3) diffed against the EM2/EM3 capture with the section 5 stored-bytes helpers | EM2, EM3 |
| TC-P-09 | `Element_ShouldEqualTheStandCapture` | AC5 on the current designer, under WSCR incl. W14: IX1-IX4 (`IX3` 7000, `IX4` true), BP2 of `UsrWscEchoService.UsrWscEcho` with every service-param kind of EM1 (`UsrWscEm2CaptureA.WebService1`, M-24) | EM2 |
| TC-P-10 | `Bindings_ShouldEqualTheStandCapture` [scalar from a process param; constant; formula; item-level mapping into an IN collection] | AC1/AC3; the constant bytes of TC-U-57 (value in the resources only) | EM2 |
| TC-P-11 | `ResponseCollection_ShouldEqualTheStandCapture` [declared Collection `Orders {OrdNo, Lines {Sku}}` + item mappings on `UsrWscDriftService.UsrWscOrders2`] | Q6 v1 shape (the declared-Collection route, the shipped `ActionGeminiSearch` shape). Oracle: EM3 route (b), process `UsrWscEm3CaptureB` (M-25): the designer binds item-first, and binding an item binds the container to `UsrWscOrd2_Out` and the nested collection to `UsrWscOrd2Lines_Out`; the container is type 651ec16f, every level `GS1` 3 with an `[Element:{ws}].[Parameter:{uid}]` token; the builder binds the container as well as the items. The designer's "Create from element" shape is measured (EM3 route (a), `UsrWscEm3Probe`, M-15: a CompositeObject wrapper `WebService1Results1` holding a Collection whose items are only the first-level scalars, Tags `<Element>.<Param>`, nested lists dropped) and is ENG-92730 scope, not built | EM3 |
| TC-P-12 | `MethodChange_ShouldEqualTheStandCapture` | Q11: input is the EM4 S1 capture (`UsrWscEm4MethodChange`, M-26) with its Formula reader of the tagged output removed first (A3 would refuse the change otherwise); the builder call maps `UsrWscQ_In` in the same request, as EM4 step 2 did before the S2 save; the builder's method change is compared with the S2 capture on the element node and its BK15 rows under WSCR, which does not compare UIds, so the kept result-param UIds (A3) are not a difference; the designer's `BL11:false` on the S2 element (unbound required `UsrWscId`) is AD10 (serialization §8.3), and the package never writes `IsValid`; the S2 capture's dangling Formula token is outside the comparison | EM4 |
| TC-P-15 | `SoapElement_ShouldEqualTheStandCapture` (and `SoapElement_ShouldDifferOnlyInTheBooleanConstant_WhenItIsSentAsAValue`) | Q20 D: the builder's create on `UsrWscSoapService.UsrWscSoapEcho` equals the S2 capture (`UsrWscEm2SoapS2`, M-33) under WSCR, with AD10 for the designer's `BL11:false`; the Boolean flag is sent in the designer's Script form, and the companion test pins that `value: "true"` differs from the capture in that one binding only (`{GS1: 1, GS2: "true"}` vs Script `[#BooleanValue.True#]`) until the final-cut check | EM2 S2 |
| TC-P-16 | `SoapToRestSwitch_ShouldEqualTheStandCapture` | Q20 D, Q11 A3: the builder's retarget of the S3 element to `UsrWscEchoService.UsrWscEcho` equals the S4 capture (M-35) on the element node and its BK15 rows under WSCR; UIds not compared (A3 keeps the result-param UIds, the designer gives new ones); AD10 for `BL11:false` | EM2 S4 |
| TC-P-13 | `DesignerResave_ShouldChangeNoByte` [builder element with and without BK15 rows; with and without Success L8] | residual-risk STOP gate restated: diff under WSCR = 0 or an allowed difference of serialization §8.3 (whether AD1 applies to a stand capture is what EM5 shows). For the designer's own elements the Q4 probe already showed a re-save keeps the rows unchanged and a copy row-less (measured 2026-11-03); EM5 is the builder-element half | EM5 |

### 3.3 Pattern C: the platform writer's JSON

Fixture `WebServiceSerializationTests`: `WriteMetaData` of a builder element -> JSON -> WSCR (W2-W8) -> structural `Diff`
listing every difference by path; `ReadBack` proves the platform reader accepts the bytes; a harness-stamp check pins
the role-normalised keys by VALUE so the rule cannot hide a wrong stamp.

| TC | Test | Oracle |
|---|---|---|
| TC-P-14 | `WriteMetaData_ShouldStoreTheDesignersShape` [a fresh element of the Gemini method; of the EM1 method] | the quoted `WebService1` node of `ActionGeminiSearch`; EM2's node (`UsrWscEm2CaptureA.WebService1`, M-24) |

---

## 4. clio unit tests (`CLIO/clio.tests`)

| TC | Fixture.test | Proves | Module |
|---|---|---|---|
| TC-C-01 | `ProcessBlockKeyCheckTests.Check_Should_AcceptTheWebServiceBlock_OnEveryRoute` [create `elements[].webService`; `addElement`; `setElement` `elementUpdate.webService`] | the `WebServiceBlock` key set exists in `Blocks` and both link points (`clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:327-357`); today clio refuses the key pre-send (r5 §3) | ProcessModel |
| TC-C-02 | `ProcessBlockKeyCheckTests.Check_Should_RefuseAnUnknownWebServiceKey_NamingItsPath` [`timeout`, `serviceName`, `multiInstanceOptions`] | the path reads `webService.<key>` and lists the accepted keys; nothing is sent (a multi-instance `webService` block is refused here, before the package's Q20 refusal) | ProcessModel |
| TC-C-03 | `ProcessBlockKeyCheckTests.Check_Should_TolerateTheReadBackFields_OfADescribedBlock` [`serviceUId`, `serviceResolved`, `methodResolved`, `issues`, `parameters`] | a described block fed back with its read-back fields is warned about, not refused (TC-U-78 and TC-I-06 send them back as is) | ProcessModel |
| TC-C-04 | `KeySets_Should_EqualTheMembersOfTheBundledServerContracts` (existing, `ProcessBlockKeyCheckTests.cs:635-651`) | red on the rebundle until the key set mirrors `WebServiceDescriptor` | ProcessModel |
| TC-C-05 | `ReadBackFields_Should_BeDescribeOnlyMembers` (existing, `:653-666`) | the read-back keys exist on the describe type only | ProcessModel |
| TC-C-06 | `ToolContractShortFormTests` call-shape drift (existing, `:572`) + the short-form bundle ceiling `MaxInlineWireReplyBytes`, which lives in `ToolContractPayloadBudgetTests` (`:339`) | `"webService"` added to `CreateElementBlockShape` (`ToolContractShortForm.cs:84-88`); headroom 168 B at `6be5dde28` (r5 §2, measured); re-measured on the branch (Q15) | McpServer |
| TC-C-07 | `ProcessDesignerRequiresPackageAttributeTests` literals moved to the cut claimed in the claim comment; `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` (existing) | the create/modify/modify-as-new-version floor (1.6.6.127 at `6be5dde28`, history; `CreateBusinessProcessCommand.cs:293`) moves; one floor-history paragraph | Command, Common |
| TC-C-08 | `BundledProcessBuilderPackageTests` (existing) | hash, version, producing commit, `ModifiedOnUtc` moved by `rebundle-process-builder.ps1`; `ExpectedOperationContractCount` 8 and `ExpectedAuthorizationGateCallSites` 6 UNCHANGED (Q10; `:362, 374`) | Common |
| TC-C-09 | `CreateBusinessProcessToolTests` / `ModifyBusinessProcessToolTests` `..._Should_Forward_WebService_Verbatim` [create; `addElement`; `setElement`] | the block reaches the command unchanged | McpServer |
| TC-C-10 | `ServerProcessDescriberTests.Describe_ShouldReadTheWebServiceMember_WhenServerReportsIt`, `..._LeaveItNull_WhenServerOmitsIt`, `..._KeepUnknownMembers_InTheBag` | typed `DescribedWebService` mirrors the server describe type field for field, each type with a `[JsonExtensionData]` bag (`IProcessDescriber.cs:487, 847` at `6be5dde28`); the parameter tree's tag, items and unbound inputs reach the agent: today's package describe (CrtProcessBuilder 1.6.6.131) returns for a designer-built WS element every output with its `tag` and nested `itemProperties`, and the bound inputs only (unbound inputs and an unbound `RequestBody` omitted, M-31), but no service, method, timeout or ignore-errors member (measured on stand `Creatio` 2026-11-03, Q14), so the unbound inputs and the `webService` member are new on both sides and the typed DTO must keep `tag` / `itemProperties` (an older clio build dropped both from the reply); `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md` recounted | ProcessModel |
| TC-C-11 | description pins in `CreateBusinessProcessToolTests`, `ModifyBusinessProcessToolTests`, `DescribeProcessToolTests`, `ModifyProcessAsNewVersionToolTests`, `ValidateProcessGraphToolTests`, `ListUserTasksToolTests` | the type enum names `webService`; one pointer clause `webService? (...; get-guidance name=process-web-service owns the block)`; modify `addElement` / `setElement` lists; the describe read-back clause; `ValidateProcessGraphTool.cs:114` buildable list; `ListUserTasksTool.cs:58-60` names `WebServiceUserTask` as refused on the generic route (Q1); the floor sentence lists | McpServer |
| TC-C-12 | `ToolContractPayloadBudgetTests` (existing, `:315`, 35,072 B) | Q15: the swap pays for every clause (headroom at `6be5dde28` create 39, modify 3, describe 43 B, r5 C3 verdict, measured; re-measured on the branch, Q15); sizes read before/after by lowering the ceiling on the branch; both numbers into the PR body | McpServer |
| TC-C-13 | prompt pins (`CreateBusinessProcessPrompt.cs:30`, `ModifyBusinessProcessPrompt.cs:36-37`, `ListUserTasksPrompt.cs:32`) | the generic-route refusal lists gain `WebServiceUserTask` | McpServer |
| TC-C-14 | `WorkspaceTemplateGuidanceDriftTests.UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` (existing, `:539-597`) with `Fixtures/curated-knowledge-names.json` re-pinned | a description may cite only a pinned guide: red while a tool description names `process-web-service` and the fixture is not re-pinned to a generation that has it. The fixture is hand-edited and nothing in CI compares it with the published generation, so keeping the clio PR draft until the KB PR is published is policy (Q19, r5-C5), not something this test enforces | McpServer |
| TC-C-15 | `ManagerMapResolveDataIdTests` (existing) | `webservice` resolves to `EventType.WebServiceTask` as an activity (`clio/Command/ProcessModel/Schema.cs:1123`); no new arm needed for the token `webService` | ProcessModel |
| TC-C-16 | `McpE2eSelectionCoverageTests` (existing) | the new e2e fixture is selected when its tool changes | Core |

New clio tests go into existing fixtures and follow the host fixture's placement of `[Category("Unit")]` and
`[Property("Module", ...)]`; no category is added to a `BaseCommandTests` fixture; AAA, `because`, `[Description]`
without a Jira key. The change spans ProcessModel, McpServer, Command, Common (test-side pins) and Core: more than
three modules, so the FULL unit suite runs before every commit. Write `TestCategory`, never `Category`; close stdin
(`McpE2eSelectionCoverageTests` runs `git init`):

```
# iterating
dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=ProcessModel|Module=McpServer|Module=Command|Module=Common|Module=Core)" --no-build < /dev/null
# before every commit (quoted in the commit message and the PR body)
dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null
```

---

## 5. clio MCP e2e (`CLIO/clio.mcp.e2e/WebServiceElementToolE2ETests.cs`)

Fixture shape copied from `SendEmailAttachmentsToolE2ETests` / `ProcessFileElementToolE2ETests`:
`[TestFixture][AllureNUnit][AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)][NonParallelizable][Category(McpE2ECategories.ProcessDesigner)]`
(r5 §6).

| Rule | How |
|---|---|
| Version gate | `MinimumPackageVersion` = the cut claimed in the claim comment, the same literal as the clio floor, written in the clio PR; the whole fixture is Ignored below it; a convergence refusal becomes Ignored, never green |
| Writes | strictly sequential (a burst of schema writes takes a .NET Framework app pool down, r5 §6); one framework at a time: `-f net10.0`, then `-f net8.0` |
| Stand prerequisites | made by EM1 and EM2 on 2026-11-03 (M-23, M-24) and present on stand `Creatio`: `UsrWscEchoService` (`UsrWscEcho`, `UsrWscGet`, `UsrWscMissing`) and `UsrWscDriftService` (current methods `UsrWscDriftGone`, EM8's rename of `UsrWscDrift`, `UsrWscTc12` and `UsrWscOrders2`), base URI the stand root (Q17, EP option E4: no echo endpoint, no external host); the failing-call service `UsrWscDeadService` (for TC-I-09; its base URI is the stand host's loopback with no listener); the designer-built process `UsrWscEm2CaptureA`. A test that needs one of them is Ignored WITH the build instructions (measurements file, EM1/EM2) when it is absent (for example after a stand reset); the evidence run has all of them, so Ignored = 0 |
| Cleanup | every created process in a list, deleted newest-first in `finally`; records created by run-time tests deleted through `execute-dataservice-batch`; leftovers reported, not thrown, and listed in the measurements file's cleanup register |
| Stored bytes | private helpers reading `SysSchema.MetaData` and `SysLocalizableValue` (copied from `ProcessFileElementToolE2ETests`; a lift into `Support/**` would make every PR run the whole suite) |
| Secrets | `UsrWscEchoService` carries one header param with a NON-secret Const default (EM1); every test that reads a reply asserts that default's text is absent (Q16) |
| Assertions | describe text AND stored bytes; refusals assert the package's text and that nothing was created; AAA, `because`, `[Description]` without a Jira key |

| TC | Test | Asserts |
|---|---|---|
| TC-I-01 | `CreateBusinessProcess_Should_StoreTheWebServiceElementInTheDesignersShape_AndDescribeItBack` | one process: `webService {service: "UsrWscEchoService", method: "UsrWscEcho", timeoutSeconds: 30, ignoreErrors: true}`; a scalar from a process parameter, a constant, a formula, an item-level mapping into the IN collection, RequestBody unset. Build: success. Describe: the `webService` member; the tree with tags and nested items. Bytes: IX1-IX4, BO2/BO3, BP2 order, L18, ItemValue child, result params, 4 BK15 rows; captions in `SysLocalizableValue` |
| TC-I-02 | `CreateBusinessProcess_Should_ReadTheResponseIntoADeclaredCollection` | declared 2-level Collection with item mappings from the response object array of `UsrWscDriftService.UsrWscOrders2`; stored bindings as TC-U-63 and as EM3 route (b) (`UsrWscEm3CaptureB`, M-25: the container and the nested collection bound as well as the items); describe decodes them |
| TC-I-03 | `CreateBusinessProcess_Should_RefuseAndCreateNothing` [unknown service -> the names listed; unknown method -> the service's methods listed; `userTask` `WebServiceUserTask`; `multiInstanceOptions` on `webService`; the block on a `userTask`] | refused with the package's text, except `webService.multiInstanceOptions`, which clio's key check refuses before sending (its text names the path and the accepted keys, not the Q20 text); describe of the process name answers "was not found" |
| TC-I-04 | `CreateBusinessProcess_Should_StoreTheCanonicalMethodName` [method sent in lower case] | IX2 = the service's spelling; the params exist (validates Q9 against EM11) |
| TC-I-05 | `ModifyBusinessProcess_Should_ReplaceOnMethodChange_AndGuardDependents` | a formula reads a tagged output (`UsrWscEchoText_Out`) and a conditional flow reads `Success`; `setElement` to `UsrWscGet` is refused naming the formula only; after the formula is removed, the change builds, the flow still resolves `Success` (same UId, Q11 A3) and the process validates; the notice lists the dropped bindings with their values; tagged params regenerated in fresh-build order. Second case: a declared Collection process parameter bound to the tagged output `UsrWscEchoOrders_Out` blocks the change likewise, the refusal naming the parameter, nothing written |
| TC-I-06 | `ModifyBusinessProcess_Should_ChangeNoStoredByte_WhenTheDescribedBlockIsSentBack` | create -> describe -> `setElement` with the described `webService` member sent back unchanged -> element node, rows and resources byte-identical; the same-pair no-op needs story 2's P-D1, so the row closes with story 2 |
| TC-I-07 | `DescribeBusinessProcess_Should_ReadTheDesignerBuiltFixture` | read-only on `UsrWscEm2CaptureA`: service, method, timeoutSeconds, ignoreErrors, tree; `serviceResolved`/`methodResolved` true; the header default absent from the reply |
| TC-I-08 | `RunProcess_Should_CallTheService_AndReadTheResponse` | the builder-made process of TC-I-02 plus an Add data element writing `StatusCode` and `Success` into an Activity title; `run-process` watching `Activity`; assert the row carries the result params of the stand's own HTTP answer (E4: base URI = the stand root), Success consistent with StatusCode (true for 200..399); cleanup in `finally`. Reduced to what E4 can show, like EM6 (its regression form): the request bytes and the response item value need an echo or a JSON source with the `$.orders` shape, which E4 lacks; the request side rests on the core unit tests (r3 §4). If the owner adds such a source, the item-value assertion is restored |
| TC-I-09 | `RunProcess_Should_Continue_WhenTheCallFailsAndErrorsAreIgnored` | the builder half of EM7, against its designer half (M-28): a call that gets NO HTTP answer (`UsrWscDeadService`, the no-listener endpoint on the stand host); ignoreErrors true -> the element completes with Success false and StatusCode 0 and the process reaches its end; false -> the element fails and the process ends in Error (`System.Net.WebException` in the process log). A 404 answer (`UsrWscMissing`, a static path; an unknown stand-root route answers 302 -> login page, M-28) completes the element with Success false and StatusCode 404 whatever IX4 is (measured, M-28), so it is asserted only as the Success=false branch, never as proof of ignoreErrors |
| TC-I-10 | `ModifyProcessAsNewVersion_Should_KeepTheWebServiceElement` | the clone's params and rows carry the new version's UId; describe equal; the version deleted in `finally` |
| TC-I-11 | `ValidateProcessGraph_Should_AcceptADescribedWebServiceElement` | `validate-process-graph` over the described element (precedent TC-I-86 in `ProcessFileElementToolE2ETests.cs:511`, r5 §1.3) |
| TC-I-12 | `CreateBusinessProcess_Should_WarnAboutANestedListRequestBinding` | N5 (Q13) on stand `Creatio` (10.1.37, no core fix): the notice text only, no run. The run-time drop itself is not measurable on stand `Creatio` under E4 (EM12); it rests on the fix's issue comment and the core unit tests |
| TC-I-13 | `ListUserTasks_Should_NameWebServiceUserTaskAsRefusedOnTheGenericRoute` (in `ListUserTasksToolE2ETests`) | the `list-user-tasks` tool's contract text, read over MCP, names `WebServiceUserTask` as refused on the generic `userTask` route and points to the `webService` block (Q1; the description change of TC-C-11 seen end to end, AGENTS.md) |

**Evidence.** The PR lane excludes `McpE2E.ProcessDesigner` (`clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:46`,
`baseFilter`), so a green PR proves nothing here. The evidence is a stand run on `Creatio`, recorded in the clio PR
body with passed / failed / ignored per framework and Ignored = 0:

```
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~WebServiceElementToolE2ETests"
dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net8.0  --filter "TestCategory=McpE2E.ProcessDesigner&FullyQualifiedName~WebServiceElementToolE2ETests"
```

plus the regression fixtures of section 9 with the same category filter. Every run waits for the owner's word (stand
writes).

---

## 6. clio-knowledge tests (`KB/automation/Clio.Knowledge.Bundle.Tests`)

| TC | Test | Proves |
|---|---|---|
| TC-K-01 | `WebServiceGuidanceTests.Guide_ShouldOwnTheBlock_AndTheEntryArticlesShouldRouteToIt` | the new article `process-web-service` carries the contract anchor (`webService {service, method, timeoutSeconds?, ignoreErrors?}`), the FULL-contract gate and the real floor (the precedent of `send-email-attachments.md`), the stored-name rule (`<svc>_In`, dotted item paths, as describe returns them; no aliases); `element-catalog.md` routes to it by name |
| TC-K-02 | `WebServiceGuidanceTests.Guide_ShouldStateTheReadBack` | the describe member, the stored tree, `serviceUId`/`serviceResolved`/`methodResolved`/`issues`, the replay recipe (send the described block back unchanged); describe never shows service defaults |
| TC-K-03 | `WebServiceGuidanceTests.Guide_ShouldStateTheRequestBodyRules` | nested items through the mapping surface; map the container explicitly when the items are formulas (g3 §3: nothing warns); when to use `RequestBody` (a method without a Body parameter such as `GeminiApiSearch`, the nested-list workaround, shapes mappings cannot express) and that a non-null value replaces the generated body (from the core unit tests, r3 §4: not measurable on stand `Creatio` under E4); no JSON check of a constant body and no notice when both `RequestBody` and Body parameters are bound (Q5, 2026-11-03), so the article states the rule instead |
| TC-K-04 | `WebServiceGuidanceTests.Guide_ShouldStateTheResponseRules` | declare a Collection (<= 2 levels) and map items, which binds the container too; in the designer, map an item first, because the container picker offers no web-service object array (M-25); mirror of a web-service output, scalar lists and a third level are not buildable yet |
| TC-K-05 | `WebServiceGuidanceTests.Guide_ShouldStateResolutionReplaceAndRefusals` | resolution order and case; refusals list names; a method change replaces every generated param, keeps `Success` / `Response` / `StatusCode` readable, and is refused while something reads any other parameter of the element (tagged inputs, nested items and `RequestBody` included); `WebServiceUserTask` refused on the generic route; multi-instance refused; a SOAP service built like REST with notice N10; the drift issues and the notice on any modify that leaves an element unresolved; a designer save of a process whose web-service element no longer resolves deletes every parameter of the element (result params included) and its BK15 rows and leaves the element `BL11:false`, so the remedy (fix the service, or retarget the element) comes before any designer save, and describe's `methodResolved: false` is the signal (M-30) |
| TC-K-06 | `WebServiceGuidanceTests.Guide_ShouldStateTheRunTimeFacts` | Success/StatusCode/Response as outputs; the timeout covers retries (r7 A3 verdict); ignoreErrors works only where feature `DisableIgnoreErrorsInWebServiceUserTask` is off (no platform-property caveat: the package floor is already about 8.3.2, Q7); an HTTP error answer completes the element with Success false and its status whatever ignoreErrors is, and a call with no HTTP answer fails the element and the process unless ignoreErrors is on, when Success is false and StatusCode 0 (EM7 designer half, M-28); on platforms without the core fix (the version line N5 names), a request list nested in a list whose container has no source of its own while its items are mapped is sent without that inner list, and the remedy is to map the container (the fix's issue comment and the core unit tests, stated as such: EM12 is not measurable on stand `Creatio` under E4; no version promise beyond the measured builds) |
| TC-K-07 | `ProcessGuideContentPreservationTests` (existing, `:191`) | the `webService ... READ-ONLY here` pin is replaced with the VERSION FLOOR pin (formulaTask/scriptTask precedent `:181-197`) |
| TC-K-08 | `WebServiceGuidanceTests.NoProcessArticleOrManifestDescription_ShouldClaimWebServiceIsUnbuildable` | `element-catalog.md:136-141`, `element-catalog-details.md:70-71`, `process-modeling.md:182-185` and the `process-element-catalog` manifest description no longer say read-only (whitespace-insensitive) |
| TC-K-09 | `WebServiceGuidanceTests.Routing_ShouldSeparateTheProcessElementFromConfigurationServices` | `routing.md` has a `process-web-service` row whose text distinguishes it from `configuration-webservice` / `configuration-webservice-tests` / `portal-service-routing` (`routing.md:185`) |
| TC-K-10 | size gates (existing): `GuidanceInlineReplyBudgetTests` (19,000 B; `process-modeling` ~13 B headroom, so its edit is a swap), `ProcessGuideResponseSizeTests` (27,793 chars per article) | Q15; the sizes are re-measured on the branch |
| TC-K-11 | manifest and routing (existing): declared in `resources[]` with no `legacyUris`, `requirements.itemIds`/`resourceUris`, `GuidanceMigrationTests.PostMigrationGuidance` (`:25-58`) gains the item, `ProcessGuideCrossReferenceTests` (`:132`), `GenerationSequenceTests` after the `libraryVersion` bump | the article is reachable and the generation is new; that it is above every open branch claim is a manual check at merge |
| TC-K-12 | `WebServiceGuidanceTests.Guide_ShouldCarryNoInternalReferenceOrSecret` + the refute-first fact-check gate (below) | no ticket key, spec path or placeholder floor; no example with a credential-looking default |
| TC-K-13 | `WebServiceGuidanceTests.Guide_ShouldStateTheTaskWithoutDetails` | Q10 addition 2: find the service through the refusal list and ask when several fit; when none fits, stop (no placeholder process, no Script task in its place) and name what registration needs; build in two steps; a key or token parameter comes from a system setting or a process parameter, never a constant |

Run: `dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj` (whole project).

**Fact-check gate (TC-K-12, not a unit test).** Before the KB PR leaves draft, every factual claim of the new article
and the edited sentences is checked by independent agents told to REFUTE it, one per cluster: (1) contract,
resolution, refusals, replace rule; (2) parameter shape and read-back; (3) run time: result params, timeout,
ignoreErrors, nested lists, response re-keying; (4) mapping rules. Each agent gets the source trees (`PB/`, `CORE/`,
`JS/`, `PD/`) and the EM results as dated measurements, and may NOT cite `CLIO/docs/knowledge/`, this spec folder,
the research folder, or another agent's output. Verdict per claim: confirmed / refuted / unverifiable with
`file:line` or the EM id; every refuted or unverifiable claim is fixed or removed; the verdict table goes into the KB
PR body.

**Agent scenario runs (Q10, not unit tests).** After the claimed cut is installed and the KB article is published
to a local library, two headless agent sessions run on stand `Creatio`, each from a clean context with only the
shipped guidance, one task each, transcript kept as evidence in the clio PR:

| Id | Task given to the agent | Pass |
|---|---|---|
| AR-1 | "Build a process that gets the published version of a Marketplace application" (no service or method named) | the agent reaches `CreatioMarketplaceApi.GetAppPublishedVersion` through at most one refusal, builds the element in two steps and maps the inputs by their stored names; no secret in the transcript |
| AR-2 | "Build a process that pulls today's weather from OpenWeather" (no such service registered) | the agent stops without creating a process, a placeholder element or a Script task, and tells the user to register the service in Studio > Web services, naming base URL, method, parameters and authentication |

A failure of either run is fed back into the article before the KB PR leaves draft; repeated failures of AR-1 are the
signal for the discovery tool (Q10 option B, a follow-up).

---

## 7. Stand checks mapped to EM ids

Procedures, gates, expected results and cleanup: [measurements](eng-92710-call-web-service-element-measurements.md).
Every stand write waits for the owner's yes.

| EM | Proves | AC / Q | Feeds or gates |
|---|---|---|---|
| EM0 | done 2026-11-03 (M-22): read-only baseline. `DisableIgnoreErrorsInWebServiceUserTask` off, `DisableParametersWithNestedArrays` off, `ProcessParameterCollections` on, `AutoAddPackageDependenciesInProcesses` on; versions as M-01, M-02; captures saved by an administrator login, culture en-US | all | PROVENANCE of TC-P-09..13; preconditions of TC-I-08/09 and TC-06; TC-U-96 (the N3 gate is off on the stand) |
| EM1 | done 2026-11-03 (M-19, M-23): `UsrWscEchoService` (`2ac0b905-591d-4623-a8bf-990846cea7d3`, package `Custom`, base URI = the stand root, E4) with `UsrWscEcho` (every planned param kind), `UsrWscGet` and `UsrWscMissing` (address `/UsrWscNoSuchRoute.js`, a 404, M-28); `UsrWscDriftService` with `UsrWscDrift`, `UsrWscTc12` and the 2-level `UsrWscOrders2` (for EM3 route (b)); `UsrWscDeadService` with `UsrWscDead` (no listener). Codes `UsrWsc...` (Studio refuses a parameter code without `Usr`); the `IU1` paths are the codes (`$.UsrWscText`), not `$.orders` / `ordNo`. The Q16 part: the default stays only in the service; the designer element stores an empty `L8`; today's describe omits that unbound input (it returns bound inputs only, M-31) | AC1, AC8, Q16, Q17 | every TC-I; TC-P-09; manual Conditions |
| EM2 | S1 done 2026-11-03 (M-24): designer capture A = the AC5 oracle, `UsrWscEm2CaptureA.WebService1` on `UsrWscEcho`, `IX3` 7000, `IX4` true; every expected row matched; settles the unbound Boolean In `L8`, the Text-constant bytes (value in the resources only) and the top-level-only caption suffix. S2-S4 (SOAP) done 2026-11-05 (M-32..M-35): the SOAP element has the REST shape, a timeout-only save moves `IX3` alone, a switch to REST is a full rebuild with no SOAP leftover; the read-back is re-run on the final cut | AC1, AC5, Q8, Q20 | TC-P-05, 09, 10, 14, 15, 16; TC-U-04, 10, 11, 24, 57, 110..113; `UsrWscEm2CaptureA` for TC-I-07 |
| EM3 | done 2026-11-03. Route (a) (M-15, `UsrWscEm3Probe` on `GeminiApiSearch`): the "Create from element" CompositeObject wrapper shape. Route (b) (M-25) on `UsrWscDriftService.UsrWscOrders2`, process `UsrWscEm3CaptureB`: the container picker offers no web-service object array; binding an item binds the container and the nested collection | AC3, Q6 | TC-P-05, 11; TC-I-02; TC-K-04; the ENG-92730 input |
| EM4 | done 2026-11-03 (M-20, M-26, M-27; `UsrWscEm4MethodChange`): a method change ("Are you sure you want to change method? All parameters will be lost.") and a service change ("Are you sure you want to change element schema? All parameters will be lost.") replace every param with new UIds, result params included; the 4 BK15 rows re-created (new row UIds, same `GT5`); `IX3` / `IX4` kept on a method change, `IX3` reset and `IX4` kept on a service change; dependents keep the removed UIds, are not marked invalid, and the save stores the process with "Schema has errors"; hence Q11 A3. M-27: the designer's formula window resolves an output by caption | AC5, AC6, Q11 | TC-P-12; TC-U-35, 36, 46, 104, 107; TC-I-05 |
| EM5 | (to do; the Q4 probe on designer elements is done, M-13) residual-risk STOP gate: builder element opened and re-saved in the designer; its builder modify runs on the designer copy `UsrWscEm5OracleCopy`, never on `UsrWscEm2CaptureA` | AC5, Q3, Q4 | TC-P-13; Q4 final (TC-U-41..46, or W10 with BK15 rows optional on the builder side); fallback of TC-U-81..86 |
| EM6 | (to do) run time, reduced under E4 to what the stand's own answer shows: result params, StatusCode, Success (the ignoreErrors behaviour is EM7's). The request-body bytes are not measurable on stand `Creatio` (core unit tests, r3 §4); the response read into a declared Collection needs a JSON source with the `$.orders` shape, which E4 lacks, unless the owner adds one. Also on the final cut: the Boolean-constant check, measurements EM6-B (A + B in one run; only a value other than `true` makes web service elements write the Script form; TC-P-15 pins the difference meanwhile) | AC3, AC4, Q5, Q6 | TC-I-08; TC-K-03, 06; TC-P-15 |
| EM7 | designer half done 2026-11-03 (M-28): a 404 completes the element with Success false, StatusCode 404 and the process continues, `IX4` true or false; a refused connection with `IX4` true completes with Success false, StatusCode 0; with `IX4` false the element and the process fail (`System.Net.WebException`). The builder half is to do on the claimed cut | AC4, Q7 | TC-I-09; TC-K-06; TC-06 |
| EM8 | modify latent loss on a disposable copy (g4 recipe); current-builder half done 2026-11-03 (`UsrWscBk15Probe`: an unrelated modify deleted the 8 tagged params of the unresolved `WebService2`, no warning); the service-save and designer halves done 2026-11-03 (M-29..M-31: a service save evicts nothing, a cold load prunes; a designer save deletes every parameter and the BK15 rows); the claimed-cut half (the notice) is to do | AC7, Q12 | TC-U-53, 72; TC-12 |
| EM9 | done 2026-11-03 (local): `origin/main` already fails to compile on 8.3.1.4498 and 8.1.3.6947 and builds on 8.3.2.4199, so the real floor is about 8.3.2 | Q7, Q13 | IX4 written directly; TC-U-49, 50, 51, 76 dropped |
| EM10 | cross-package service: designer vs builder `SysPackageDependency`; designer half done 2026-11-03 (the designer adds the row); the builder half is to do on the claimed cut | AC2, Q9 | TC-U-23 notice text |
| EM11 | wrong-case method via the platform; designer half done 2026-11-03 (saves green, pruned on load, run fails with `ItemNotFoundException`); the builder half is to do | AC2, Q9 | TC-U-19, 83, 102; TC-I-04 |
| EM12 | nested list in a list on 10.1.37: an inner list with no source of its own and mapped leaves is sent without its value; whether the save succeeds. Not measurable on stand `Creatio` under E4 (no echo); the rows it fed rest on the fix's issue comment and the core unit tests | Q13 | TC-U-52, TC-I-12, TC-K-06 (wording without a stand measurement) |

---

## 8. Manual Jira test cases

Written for the Jira issue's test-case section. Drafted here; the owner pastes them by hand (Q18 covers the
description, not this section; both wait for the owner).

**Test cases**

**Conditions:** A Creatio environment with the CrtProcessBuilder version this issue ships, and a clio build with this
issue's clio change connected to an AI agent as an MCP server. The REST web service "WSC echo service" (code
`UsrWscEchoService`) made by EM1 is registered in the Web services section and is never changed by these cases; its
base URI is the environment's own root (no echo endpoint), so the cases need no internet. Its POST method `UsrWscEcho`
takes a text `UsrWscText`, a required text `UsrWscReq`, a required list of numbers `UsrWscIds`, a list of orders `UsrWscOrders`
(each order: a number `UsrWscOrdNo`, a list of lines `UsrWscLines` with `UsrWscSku` and `UsrWscQty`, and a list of tags `UsrWscTags`),
the customer's name and city (`UsrWscCustName`, `UsrWscCustCity`), an optional flag `UsrWscFlag` (Boolean) and a text
`UsrWscStatusIn` captioned "Status"; the order number `UsrWscOrdNo` is captioned "Reference"; it and `UsrWscGet` send the header
`X-Wsc-Probe` (parameter `UsrWscProbe`) with the default value `wsc-probe`. It returns a list of orders `UsrWscEchoOrders` (each:
`UsrWscEchoOrdNo` and a list of lines `UsrWscEchoLines`), a list of numbers `UsrWscEchoIds`, the scalars `UsrWscEchoText`,
`UsrWscEchoQty`, `UsrWscEchoOk`, with `UsrWscEchoText` captioned "Reference", and a text `UsrWscStatus` captioned "Status". The
same service has the GET methods `UsrWscGet` (required query text `UsrWscQ` and a url segment `UsrWscId`, text without a
default, path `/{UsrWscId}`; returns `UsrWscGetText` and `UsrWscGetCount`) and `UsrWscMissing` (calls a static path that does not
exist, so it answers 404; an unknown route of the environment root would redirect to the login page instead). `UsrWscDeadService` (EM1) has the method `UsrWscDead`, whose base URI has no listener, so
its call gets no HTTP answer at all. `UsrWscDriftService` (EM1) has, besides `UsrWscDriftGone` (EM8 renamed `UsrWscDrift`) and EM3's `UsrWscOrders2`, the GET method
`UsrWscTc12` (query text `UsrWscE`; returns `UsrWscF`), used by TC-12 only. Test data: the contact "WSC Customer" and two
order records "WSC-001" and "WSC-002" for that contact. Each case is given to the agent as a business task in plain words. Design time is checked
in the Process Designer: open the process, select the Call web service element, look at its service, method, request
and response parameters and their values. Run time is checked by running the process and opening the records or the
process log it produced. Because the base URI is the environment root, every call
receives the environment's own HTTP answer: what the request carried and what an echo would return cannot be
observed. The run-time bullets marked "(not run)" are therefore not checked on this environment; they rest on the
platform's own unit tests, and a JSON source with the orders shape would make them checkable. The run record says so. Delete
the test processes and the records they created after each case.

### `TC-01` Call a service with values from the process

**Preconditions:**
"WSC echo service" is registered.

**Steps:**
1. Give the agent the task: "Create a process 'WSC Order echo' that takes a customer name and a reference text, sends them to the method 'UsrWscEcho' of 'WSC echo service' as the customer name and as 'UsrWscReq', with the number 7 as the only item of 'UsrWscIds', and saves the returned 'UsrWscEchoText' to a process parameter."
2. Open the process in the Process Designer and select the Call web service element.
3. Run the process with "WSC Customer" and the reference "manual".

**Expected result:**
* Design time: the element calls "WSC echo service", method `UsrWscEcho`; `UsrWscCustName` and `UsrWscReq` are mapped from the process parameters; `UsrWscIds` holds 7; `UsrWscEchoText` is mapped to a process parameter.
* Run time: the process reaches its end (an HTTP answer of any status completes the element); the element's "Http status code" output holds the environment's own answer.
* Run time (not run): the parameter holds the text the service returned.

---

### `TC-02` Send a list of orders in the request

**Preconditions:**
As in TC-01; the orders "WSC-001" and "WSC-002" exist for "WSC Customer".

**Steps:**
1. Give the agent the task: "Create a process that reads the orders of a customer and sends each order's number as an order of 'UsrWscOrders' to 'UsrWscEcho' of 'WSC echo service', with 'manual' as 'UsrWscReq' and 7 in 'UsrWscIds'."
2. Open the process in the Process Designer and select the Call web service element.
3. Run the process with "WSC Customer".

**Expected result:**
* Design time: the orders of the request are mapped from the collection the Read data element returns, the order number per order.
* Run time (not run): the service receives two orders with the numbers "WSC-001" and "WSC-002".

---

### `TC-03` Keep the returned orders for later steps

**Preconditions:**
As in TC-02.

**Steps:**
1. Give the agent the task: "Extend the process so that the orders returned by 'UsrWscEcho' are kept in a process parameter with the order number per row, and the number of returned orders is written to a new activity's subject."
2. Open the process in the Process Designer; check the process parameters and the response mapping of the Call web service element.
3. Run the process with "WSC Customer"; open the created activity.

**Expected result:**
* Design time: a collection parameter with an order-number item is filled from the returned `UsrWscEchoOrders`.
* Run time (not run): the activity subject states two orders.

---

### `TC-04` Build the request body as text

**Preconditions:**
"WSC echo service" is registered.

**Steps:**
1. Give the agent the task: "Create a process that calls 'UsrWscEcho' of 'WSC echo service' with a request body I write myself: the customer name from a process parameter inside a fixed JSON text."
2. Open the process in the Process Designer and select the Call web service element.
3. Run the process with "WSC Customer".

**Expected result:**
* Design time: the element's "Request body" parameter holds a formula that builds the text from the customer name.
* Run time (not run): the service receives exactly that text as the body.

---

### `TC-05` Branch on the result of the call

**Preconditions:**
As in TC-01.

**Steps:**
1. Give the agent the task: "Create a process that calls 'UsrWscEcho' with 'manual' as 'UsrWscReq' and 7 in 'UsrWscIds' and, if the call succeeded with status 200, creates an activity 'WSC accepted'; otherwise an activity 'WSC failed' with the status code in the subject."
2. Open the process in the Process Designer; check the conditions of the flows after the Call web service element.
3. Run the process.

**Expected result:**
* Design time: the conditions read the element's "Success" and "Http status code" outputs.
* Run time: the activity matching the environment's own answer exists: "WSC accepted" for status 200, otherwise "WSC failed" with that status code in the subject.

---

### `TC-06` Continue the process when the service gives no answer, within a time limit

**Preconditions:**
`UsrWscDeadService` and "WSC echo service" are registered; the environment's Call web service element supports continuing on errors (its designer shows the option).

**Steps:**
1. Give the agent the task: "Create a process that calls 'UsrWscDead' of 'UsrWscDeadService' with a time limit of 20 seconds and keeps running if the call fails, then creates an activity 'WSC continued'."
2. Open the process in the Process Designer and select the Call web service element.
3. Run the process.
4. In the Process Designer, turn the option to continue on errors off, save, and run the process again.
5. Give the agent the task: "Create a process that calls 'UsrWscMissing' of 'WSC echo service', without continuing on errors, and creates an activity with the call's success flag and status code in the subject." Run it.

**Expected result:**
* Design time: the maximum execution time is 20 seconds; the option to continue on errors is on.
* Run time, step 3: the call gets no answer; the process reaches its end; the "WSC continued" activity exists.
* Run time, step 4: the process stops at the Call web service element with an error in the process log; no new activity.
* Run time, step 5: the service answers 404; the element completes although the option is off; the activity shows that the call did not succeed and the status 404. An HTTP error answer does not depend on the option; only a call without any answer does.

---

### `TC-07` Switch an existing call to another method

**Preconditions:**
'WSC Order echo' from TC-01 exists; "WSC echo service" also has the method `UsrWscGet` with its own parameters.

**Steps:**
1. Give the agent the task: "In 'WSC Order echo', call 'UsrWscGet' instead of 'UsrWscEcho'."
2. Read the agent's answer; open the process in the Process Designer and select the Call web service element.

**Expected result:**
* The first switch is refused, naming the process parameter that holds `UsrWscEchoText` (an output `UsrWscGet` does not have); the agent removes that mapping in a separate call (or asks first), then switches.
* The agent reports which values it dropped because they belonged to the old method.
* Design time: the element calls `UsrWscGet`; its parameters are those of `UsrWscGet` (`UsrWscQ`, `UsrWscId`, the header `UsrWscProbe`, `UsrWscGetText`, `UsrWscGetCount` and the result parameters); no parameter of `UsrWscEcho` remains.

---

### `TC-08` Refuse to switch the method while another step uses a returned value

**Preconditions:**
The process from TC-05 exists, and the subject of its "WSC accepted" activity also carries the returned `UsrWscEchoText` (ask the agent to add it before this case). Its flow conditions read the element's "Success" and "Http status code" outputs.

**Steps:**
1. Give the agent the task: "In the process from the previous case, switch the call to 'UsrWscGet'."
2. Read the agent's answer; open the process in the Process Designer.
3. Give the agent the task: "Remove the returned text from the subject of 'WSC accepted', then switch the call to 'UsrWscGet'."
4. Read the agent's answer; open the process in the Process Designer; check the conditions of the flows after the Call web service element.

**Expected result:**
* Step 1: the agent does not switch silently: it explains that the "WSC accepted" activity reads `UsrWscEchoText`, which `UsrWscGet` does not return, and names it. It does not name the flow conditions: "Success" and "Http status code" exist on every method and stay valid.
* Step 1: the process is unchanged.
* Step 4: the element calls `UsrWscGet`; the agent reports the values it dropped; the flow conditions still read "Success" and "Http status code" of the element, and the process opens without an error about them.

---

### `TC-09` Ask for a service or method that does not exist

**Preconditions:**
"WSC echo service" is registered; no service "WSC Billing" exists.

**Steps:**
1. Give the agent the task: "Create a process that calls the 'Send invoice' method of the 'WSC Billing' service."
2. Give the agent the task: "Create a process that calls the 'Send invoice' method of 'WSC echo service'."
3. Open the list of processes.

**Expected result:**
* For step 1 the agent says the service does not exist and names the services that do.
* For step 2 the agent says the method does not exist and names the methods of "WSC echo service" (`UsrWscEcho`, `UsrWscGet`, `UsrWscMissing`).
* No process was created by either step.

---

### `TC-10` Accept a method name written in another case

**Preconditions:**
"WSC echo service" is registered.

**Steps:**
1. Give the agent the task: "Create a process that calls 'usrwscECHO' of 'usrwscechoservice' with 'manual' as 'UsrWscReq' and 7 in 'UsrWscIds'."
2. Open the process in the Process Designer and select the Call web service element.

**Expected result:**
* Design time: the element calls "WSC echo service", method `UsrWscEcho` written as in the service; its request parameters are present and `UsrWscReq` is set.

---

### `TC-11` Explain a designer-built call without changing it

**Preconditions:**
The process `UsrWscEm2CaptureA` ("WSC EM2 capture A"), built by hand in the Process Designer by EM2: a Call web service element on `UsrWscEcho` with a time limit and continue-on-errors on, inputs mapped from a process parameter, a constant, a formula and an item-level mapping into the orders. Note the process's "Modified on".

**Steps:**
1. Give the agent the task: "Explain what the web service call in 'WSC EM2 capture A' sends and receives, and where each value comes from. Do not change the process."
2. Open the process in the Process Designer.

**Expected result:**
* The answer names the service, the method, the time limit, that errors do not stop the process, and the source of each mapped value.
* The answer does not show the `X-Wsc-Probe` header's default value `wsc-probe`.
* The process is unchanged: the same "Modified on".

---

### `TC-12` Warn before editing a process whose service method was removed

**Preconditions:**
Build a process with a Call web service element on `UsrWscTc12` of `UsrWscDriftService` and an activity step, save it, then rename the method code `UsrWscTc12` in the Web services section, so the element now calls a method that no longer exists. TC-12 uses its own method `UsrWscTc12`, not EM8's `UsrWscDrift`. "WSC echo service" is not touched.

**Steps:**
1. Give the agent the task: "In this process, rename the activity step to 'Log result'."
2. Read the agent's answer; open the process in the Process Designer.

**Expected result:**
* The agent states that the Call web service element calls a method that no longer exists and that saving the process removes its request and response parameters, before or together with the rename.
* The activity step is renamed.

---

## 9. Regression scope

| Repository | Suite | Why it is at risk |
|---|---|---|
| PBT | `UserTaskElementHandler*Tests` | the new `WebServiceUserTask` refusal on the generic route |
| PBT | `ProcessElementFactoryTests`, `ProcessBuildHandlerTests`, `ProcessGraphBuilderTests` | new supported token, strict block gate, the `afterElementsPlaced` selection, `RemoveElement` row cleanup |
| PBT | `SubProcess*Tests`, `SubProcessPlatformProbeTests` | P-A4 generalises `Snapshot`/`Diff` and the drift report (TC-U-93 proof rule) |
| PBT | `ProcessMappingServiceTests`, `FileCollectionMappingTests`, `MultiInstanceMappingTests`, `ElementSourceDecodeTests` | the Q13 notice sits on the mapping path; P1 now runs on a new element kind |
| PBT | `ProcessParameterService*Tests`, `ParameterTypeCompatibilityTests` | the mirror refusal message changes for WS sources and the WS ObjectList mirror is newly refused (C13, TC-U-65, 101); non-WS mirrors keep today's message |
| PBT | `ProcessModifyHandlerTests`, `Operations/*ElementOperation*Tests`, `ElementConfigurationLedgerTests` | the Q12 notice on modify; `addElement` / `setElement` branches; the ledger |
| PBT | `ProcessDescriberTests`, `ProcessVersionCloneFactoryTests` | the new describe member; clone of the element and its rows |
| PBT | `ProcessDesignServiceWireContractTests`, `CrtProcessBuilderAppTests`, `CiContractGuardTests` | wire names, DI graph, categories |
| PBT | the whole project on dev-nf and dev-n8 | before every commit |
| clio | `TestCategory=Unit` (full suite) | ProcessModel + McpServer + Command + Common (test-side pins) + Core |
| clio e2e | `WebServiceElementToolE2ETests` (new); `SubProcessElementToolE2ETests`; `CreateBusinessProcessToolE2ETests`; `ModifyBusinessProcessToolE2ETests`; `ProcessFileElementToolE2ETests`; `ListUserTasksToolE2ETests` (TC-I-13) | the shared build/modify path and the sub-process sync after the rebundle and the floor move; stand run, Ignored = 0, both frameworks |
| KB | the whole `Clio.Knowledge.Bundle.Tests` project | new article, catalog flip, routing row, `process-modeling` swap |

---

## 10. Conventions (all three repositories)

| Rule | Applies as |
|---|---|
| AAA | explicit `// Arrange`, `// Act`, `// Assert`; a precondition assert in Arrange wherever the arrange could make the test vacuous (empty schema UId, an element that starts with params) |
| `because` | on every assertion: what breaks for the user, not what the code does |
| `[Description("...")]` | the behaviour and why it matters, with the decision id (Q-number) where one applies; no Jira key in a test name, a `[Description]` or an `AllureName` |
| Categories | PBT `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`; clio `[Category("Unit")]` where the host fixture places it + the host's `[Property("Module", ...)]`; e2e `[Category(McpE2ECategories.ProcessDesigner)]` |
| Cross-platform | no OS paths (fixtures and source scans found by ancestor search); PBT runs on dev-nf and dev-n8 |
| Secrets | the only credential-like strings in any test are planted sentinels, asserted absent; no real token, password or header value from a stand in a fixture, snapshot or log, and no builder-output snapshot carries a service default (Q16); the pattern-A corpus fixtures are verbatim and may carry the shipped services' non-credential defaults, never a credential (TC-P-08) |

A sketch of a package test in the house style:

```csharp
[Test]
[Description("Q11 replace rule with the retarget guard: changing the method of a Call web service element must not leave another element reading an output that the new method no longer produces, so the change is refused naming the reader and nothing is written, the way the sub-process retarget guard works.")]
public void SetElement_ShouldRefuseAChange_ThatStrandsADependent() {
	// Arrange
	ProcessSchema schema = UserConnection.CreateSchema();
	schema.UId.Should().NotBe(Guid.Empty, because: "an empty process UId makes every stamp assertion vacuous");
	ProcessSchemaWebService element = AWebServiceElement(schema, "WS1", Catalog.Marketplace, "GetAppPublishedVersion");
	AFormulaReading(schema, "FormulaTask1", "WS1", "Version_Out");
	string before = ElementBytes(schema, "WS1");

	// Act
	Action act = () => Apply(schema, SetElement("WS1", "{webService: {service: 'CreatioMarketplaceApi', method: 'OtherMethod'}}"));

	// Assert
	act.Should().Throw<ArgumentException>(because: "FormulaTask1 would read a parameter that no longer exists")
		.Which.Message.Should().Contain("FormulaTask1", because: "the agent must learn what blocks the change");
	ElementBytes(schema, "WS1").Should().Be(before,
		because: "a refused change must leave the element exactly as it was");
}
```

---

## 11. Deliberately not covered

| What | Why |
|---|---|
| `typeFromElement` mirror of a web-service output, `ObjectList` process parameters, a third collection level | Q6: follow-up ENG-92730; only the refusals are pinned (TC-U-64, 65, 101) |
| A clio discovery tool or a package operation listing services | Q10; refusals list the names instead (TC-U-18, 20) |
| Adding a package dependency for a cross-package service | Q9: notice only (TC-U-23); a cross-element decision outside this story |
| A run of a service tree with duplicate nested names or Tags at two depths (the run time builds one flat dictionary and throws, g3 §6, traced) | the run-time throw is not reproduced (EM1 avoids the shape on purpose); the N9 notice (TC-U-98) and the describe issue (TC-U-99) are tested |
| A sourceless ObjectList nested in a CompositeObjectList `_In` with a bound `ItemValue` leaf | outside the design 4.12 N5 predicate, which names a nested CompositeObjectList only; the ENG-55920 Unable to use array inside array in web-service business process element fix does not cover a nested ObjectList either (traced, r1-C6 verdict), so the drop would not depend on the platform line. No notice and no row in v1; EM12 could add one such list, but is not measurable on stand `Creatio` under E4, so the design review decides whether N5 widens without a stand measurement |
| A raw read of the stored, unsynced element list on describe | describe reads what the load returns, which is already synced on an interpretable process (g4 §2); the reliable signals are tested instead (TC-U-71..73) |
| Real external endpoints, an echo endpoint | Q17 with EP option E4: every run-time call goes to the stand root or, for `UsrWscDeadService`, the stand host's loopback with no listener; E1 (an existing echo route), E2 (a configuration echo service) and E3 (an external echo host) were not chosen |
| Request bytes on the stand (EM6's request-body check, EM12) | not measurable under E4; the request side rests on the core unit tests (r3 §4) and the nested-list drop on the fix's issue comment (g2 (c)); TC-I-08 and manual TC-01..TC-04 are reduced accordingly |
| A JSON check of a constant `RequestBody`; a notice when both `RequestBody` and Body parameters are bound | Q5, 2026-11-03: not in v1; TC-K-03 states the rule in the article |
| A platform-capability guard for `IsIgnoreErrors`, a nesting-feature gate (`ProcessParameterCollections`, N11) | dropped by Q7 and Q3, 2026-11-03; TC-U-49, 50, 51, 76 are dropped rows; TC-U-100 guards against reintroducing the nesting gate |
| SOAP elements at run time; multi-instance elements | no SOAP endpoint on stand `Creatio` (the SOAP service is never called); multi-instance is refused (Q20); 0 of each in the corpus |