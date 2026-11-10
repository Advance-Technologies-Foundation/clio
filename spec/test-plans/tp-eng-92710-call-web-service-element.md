# Test Plan: Call web service element (ENG-92710)

**Feature**: eng-92710-call-web-service-element
**Jira**: ENG-92710 Call web service element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-92710-call-web-service-element.md](../prd/spec-eng-92710-call-web-service-element.md) - AC1-AC9
(the acceptance criteria written to the Jira description on 2026-11-03)
**ADR**: [adr-eng-92710-call-web-service-element.md](../adr/adr-eng-92710-call-web-service-element.md) - Q1, Q2, Q3,
Q4, Q6, Q7, Q10, Q11
**Stories**: [1](../stories/story-eng-92710-call-web-service-element-1.md) build + describe ·
[2](../stories/story-eng-92710-call-web-service-element-2.md) modify semantics and drift
**Source**: the analysis test plan
[`spec/eng-92710-call-web-service-element/eng-92710-call-web-service-element-test-plan.md`](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-test-plan.md).
This file keeps its ids and its coverage; the full rows (test arguments, oracles, harness rules, the WSCR comparison
rule, the e2e fixture rules, the manual case steps) are in that file and in
[serialization](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-serialization.md) §8 and
[measurements](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-measurements.md), and are
not duplicated here. A row changes there first.
**Status**: Draft (the cases follow the owner-agreed answers Q1-Q20 of 2026-11-03,
[decisions](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-decisions.md); measurement
status as the measurements file records it on 2026-11-05, measured facts M-01..M-31 on 2026-11-03, M-32..M-35 on 2026-11-05, EM8's service-save and designer
halves included. Status 2026-11-10: the final-cut campaign ran on 2026-11-09 (M-36..M-46); the unknown-service
refusal, AR-1 and EM8 step 8 were re-measured on the claimed cut (M-47, M-48, M-51, M-52): EM8 step 8 refused with N7;
the unknown-service refusal lists methods; AR-1 passes with one refusal. Plan gate met 2026-11-10: EM6, EM6-B, EM7, EM8 step 8, EM10, EM11, EM12 and KB-7 AR-1 re-run on the shipping build (M-53..M-58); EM5 not repeated (the later changes touch only refusal texts and the validator))
**Created**: 2026-11-03

`KB/` = the clio-knowledge repository root; clio paths are relative to the clio repository root. Sources outside the
public repositories (the CrtProcessBuilder package and its tests, CrtProcessDesigner and the other shipped packages,
the core platform) are cited by file or type name, or by the platform PR number; no repository paths of non-public
repositories.

---

## Test id classes

| Class | Where | Ids | Count |
|---|---|---|---|
| **TC-U** | package unit tests | TC-U-01..TC-U-127; TC-U-49, 50, 51, 76 are dropped rows (Q7, EM9); TC-U-107 (Q11 A3), TC-U-108 (`service` without `method`), TC-U-109 (server key check replay), TC-U-110..113 (SOAP built like REST, Q20 D), TC-U-114 (wide N7), TC-U-115..124 (N7 on a pre-save refusal, F-2, F-5) and TC-U-125..127 (methods in the unknown-service refusal, K-1) are new | 123 live |
| **TC-P** | package parity tests | TC-P-01..TC-P-16 (TC-P-05 is stand evidence read through TC-I-01/02, not a package test) | 16 |
| **TC-C** | clio unit tests (`clio.tests`) | TC-C-01..TC-C-16 | 16 |
| **TC-I** | clio MCP e2e (`clio.mcp.e2e/WebServiceElementToolE2ETests.cs`; TC-I-13 in `clio.mcp.e2e/ListUserTasksToolE2ETests.cs`; on a registered test stand) | TC-I-01..TC-I-13 | 13 |
| **TC-K** | clio-knowledge tests and gates (`KB/automation/Clio.Knowledge.Bundle.Tests`) | TC-K-01..TC-K-13 (TC-K-12 also carries the fact-check gate) | 13 |
| **TC-01..TC-12** | manual Jira cases (business tasks given to an agent) | TC-01..TC-12 | 12 |
| **AR-1, AR-2** | headless agent scenario runs (Q10), not unit tests | AR-1, AR-2 | 2 |
| **EM0-EM12** | stand measurements (analysis measurements file); EM9 is local; EM6-B is a sub-run of EM6 | EM0..EM12 | 13 |

TC-U, TC-P, TC-C and TC-K are this plan's unit level and TC-I its integration level; every live row of all five
classes is a mandatory exit criterion (AGENTS.md "all TC-U-* and TC-I-* test cases must be implemented" applies to all
of them). "Existing" means the test is already in the tree and is only re-run or extended with rows.

Story 2 (plan slice D, P-D1..P-D4) owns TC-U-33..39, 46, 53, 54, 71..73, 92, 102..104, 107, 108, 112..124, TC-P-12, TC-P-16, TC-I-05,
TC-I-10 and manual TC-07, 08, 12; TC-K-05, TC-C-11 and TC-C-13 are shared by both stories; every other row is story 1,
TC-U-109 and TC-I-13 included. Story 1's rows whose replay or retarget half needs story 2's P-D1 stay story 1 but close
together with story 2: TC-U-19, 23, 68, 78, 95, 98 and TC-I-06.

## Coverage matrix

AC numbering is the 9 Jira AC, as in the SPEC and the plan section 9 Definition of Done.

| Item | Package unit | Parity | clio unit | e2e | KB | Stand | Manual |
|---|---|---|---|---|---|---|---|
| **AC1** `webService` block on create, `addElement`, `setElement`; request values incl. composite body and `RequestBody` through the mapping surface by stored names; timeout in whole seconds | TC-U-27, 28, 30..32, 52, 56..62, 67, 87, 90, 97 | TC-P-06, 09, 10 | TC-C-01..05, 09 | TC-I-01, 06, 12 | TC-K-01, 03 | EM1, EM2, EM6 | TC-01, 02, 04 |
| **AC2** unknown / ambiguous service or method refused with the list (the unknown-service list gives each service's methods, bounded; K-1); registration pointer; case-insensitive, canonical spelling | TC-U-15..26, 105, 110, 111, 125..127 | - | - | TC-I-03, 04 | TC-K-05, 13 | EM10, EM11, M-47 | TC-09, 10; AR-1 (one refusal, M-48), AR-2 (M-46) |
| **AC3** inputs mapped in; response read into a declared Collection (<= 2 levels) with item mappings | TC-U-56..68, 101 | TC-P-06, 11 | - | TC-I-01, 02, 08 | TC-K-03, 04 | EM3, EM6 | TC-01..03 |
| **AC4** ignore-errors stored; Success, Http status code, Response body as outputs | TC-U-09, 29, 66, 96 | - | - | TC-I-01, 08, 09 | TC-K-06 | EM0, EM7, EM9 | TC-05, 06 |
| **AC5** serialization equals a designer-built capture, mapping rows included | TC-U-01..14, 40..45, 81..86, 91, 94, 98, 100, 106 | TC-P-01..05, 07..09, 13..15 | - | TC-I-01 (stored bytes), 07 | - | EM2, EM4, EM5, Q4 probe | TC-11 |
| **AC6** service / method change replaces, lists dropped bindings, refused while anything reads a parameter of the element other than Success / Response / StatusCode | TC-U-33..39, 46, 95, 107, 108, 112, 113 | TC-P-12, 16 | - | TC-I-05 | TC-K-05 | EM4 | TC-07, 08 |
| **AC7** describe returns the block and the full stored tree; unresolved service / method as issues; latent-loss notice on modify, also carried by a modify the pre-save validation refuses (F-2, F-5) | TC-U-53..55, 68..75, 77..80, 99, 102..104, 109, 114..124 | TC-P-03 | TC-C-03, 10 | TC-I-06, 07, 11 | TC-K-02, 05 | EM2, EM8 (step 8: M-51, M-52) | TC-11, 12 |
| **AC8** no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output; the element's stored bindings are returned by design; corpus fixtures carry no credential | TC-U-11, 25, 35, 37, 53, 75 | TC-P-08 | - | TC-I-07 and every reply-reading TC-I (fixture rule) | TC-K-12 | EM1 | TC-11 |
| **AC9** tests, docs, guidance article, MCP surface | TC-U-87..93 | TC-P-01 | TC-C-06..08, 11..16 | all TC-I (stand run, Ignored = 0, net10 and net8) | TC-K-01, 07..12 | - | - |
| **Scope refusals and SOAP**: `WebServiceUserTask` on the generic route (Q1) and multi-instance (Q20) refused (TC-U-47, 48, 79); SOAP built like REST with N10, build and notice tests (Q20 D: TC-U-24, 110..113, TC-P-15, 16) | TC-U-24, 47, 48, 79, 110..113 | TC-P-15, 16 | TC-C-02 (`webService.multiInstanceOptions`), 11, 13 | TC-I-03, 13 | TC-K-05 | EM2 S2-S4 | - |

## Decision coverage (Q1-Q20, agreed 2026-11-03)

| Q | Agreed outcome | Tests |
|---|---|---|
| Q1 | core `ProcessSchemaWebService`; `WebServiceUserTask` refused on the generic route | TC-U-27, 47; TC-C-11, 13; TC-I-03, 13; TC-K-05 |
| Q2 | `webService {service, method, timeoutSeconds?, ignoreErrors?}`; values via mappings by stored name; no aliases | TC-U-30, 31, 67, 87; TC-C-01..05; TC-I-01; TC-K-01 |
| Q3 | package-owned shaper, nested items always, no feature gate, platform sync a no-op | TC-U-01..14, 73, 81..86, 94, 98, 100; TC-P-03; TC-I-01; EM2, EM5 |
| Q4 | 4 BK15 rows written; `A2` not rewritten on rename | TC-U-40..46, 106; TC-P-04, 13; TC-I-01; Q4 probe, EM5 |
| Q5 | nested item mappings + `RequestBody` raw text; no JSON check, no conflict notice | TC-U-56..62; TC-P-06, 10; TC-I-01, 08; TC-K-03 |
| Q6 | declared Collection <= 2 levels; mirror / ObjectList / 3+ levels refused, ENG-92730 "Complex / structured parameter types" | TC-U-63..65, 101; TC-P-06, 11; TC-I-02; TC-K-04; EM3 |
| Q7 | IX4 written directly (EM9), N3 under the feature gate; result params as outputs | TC-U-29, 66, 96; TC-I-01, 09; TC-K-06; EM0, EM7, EM9 |
| Q8 | whole seconds, stored x1000; 0 / absent unset | TC-U-28, 77; TC-P-09; TC-I-01; TC-K-06; EM2 |
| Q9 | UId -> code -> caption, case-insensitive, ambiguity refused, canonical names, listing refusals (services with their methods, K-1), cross-package notice | TC-U-15..26, 125..127; TC-I-03, 04; TC-K-05; EM10, EM11, M-47 |
| Q10 | no new operation, no discovery tool; same-request references; registration pointer (a service left out of a cut list is used by its exact name or UId, not sent to registration); "task without details"; one refusal finds service and method (K-1) | TC-U-18, 31, 32, 88, 95, 105, 125..127; TC-C-08; TC-I-01, 03; TC-K-01, 13; AR-1 (one refusal, M-48), AR-2 (M-46) |
| Q11 | A3: replace, `Success` / `Response` / `StatusCode` keep their UIds, the change refused while anything reads another parameter of the element (tagged inputs, nested items and `RequestBody` included), dropped bindings listed with their values; the caption follows the designer | TC-U-33..39, 107, 108; TC-P-12; TC-I-05; TC-K-05; EM4 |
| Q12 | describe issues + latent-loss notice on any modify that leaves an element unresolved (a settings-only change of that element included, owner 2026-11-05), not refused by the package; a modify the pre-save validation refuses carries the notice in the refusal, with the repair order (F-2, F-5) | TC-U-53, 54, 71..73, 102..104, 114..124; TC-I-07; TC-K-05; EM8 (step 8: M-51, M-52) |
| Q13 | N5 on a sourceless nested list, on every target | TC-U-52; TC-I-12; TC-K-06 (EM12 not measurable under E4) |
| Q14 | `webService` member + stored tree, unbound inputs included, never re-derived, never defaults; replays through the server key check | TC-U-69..75, 77..80, 102..104, 109; TC-P-03; TC-C-10; TC-I-01, 06, 07; TC-K-02 |
| Q15 | text budgets by swap; KB `process-web-service`; catalog flip; routing split | TC-C-06, 11, 12, 14; TC-K-07..11 |
| Q16 | no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output; the element's stored bindings (process data the caller can already read through describe, M-31), not service data, are returned by design through `parameters[]`; corpus fixtures verbatim, no credential | TC-U-11, 25, 35, 37, 53, 75; TC-P-08; TC-I-07; TC-K-12; EM1 |
| Q17 | stand captures and runs on stand `Creatio`, EP option E4 (stand root, no echo), cleanup listed | the e2e prerequisite and cleanup rules (all TC-I); TC-I-08, 09 reduced to what E4 shows; the request-byte side is "Deliberately not covered" |
| Q18 | Jira description corrections | no test, because it is a Jira-text change, written 2026-11-03 after the owner's yes |
| Q19 | PR split docs -> package -> KB -> clio, human merges | TC-C-14 (red while the guide is unpinned), TC-K-11; the merge order itself is a process gate, no test |
| Q20 | multi-instance: `setElement webService` on a designer-built element that carries `BP6` refused, describe reports it with an issue; `webService.multiInstanceOptions` on create refused by clio's key check before sending; SOAP built like REST with N10 naming each element (D, 2026-11-05) | TC-U-24, 48, 79, 110..113; TC-P-15, 16; TC-C-02; TC-I-03 (key-check refusal); TC-K-05 |

## TC-U - package unit tests

Every new fixture is `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`; the process schema comes from
`UserConnection.CreateSchema()` with a precondition assert on its `UId`; "untouched" is asserted on the element's
`WriteMetaData` bytes and its BK15 rows; platform UIds are literals (the local core-bin is newer than CI's). The
service schema is built in code (`WebServiceSchemaFixtures`), resolver and describe rows go through
`FakeWebServiceCatalog`; harness detail is analysis test plan §2.1. Rows marked *probe* call a platform member whose
harness behaviour the first CI run of the draft PR decides (fallback: EM5 / EM8 evidence).

### Parameter shaper (Q3) - `WebServiceParameterShaperTests` (P-A2)

| Id | Test | Intent |
|---|---|---|
| TC-U-01 | `Shape_ShouldMapEveryServiceTypeToTheDesignersType` | service type -> designer data type, arrays to CompositeObjectList / ObjectList |
| TC-U-02 | `Shape_ShouldNameAndTagEveryLevel` | `<p>_In` / `<p>_Out`, Tag = service name, at top level and nested |
| TC-U-03 | `Shape_ShouldStampEveryLevelWithTheProcessAndTheElement` | A3 = A4 = process UId, IL2 = element UId on every level |
| TC-U-04 | `Shape_ShouldApplyTheRequiredRule` | the designer's IsRequired rule incl. the ItemValue child |
| TC-U-05 | `Shape_ShouldGiveAScalarArrayOneItemValueChild` | one `<p>ItemValue_<dir>` child, no L18 |
| TC-U-06 | `Shape_ShouldRecurseObjectArraysIntoNestedItems` | L18 mirrors the service tree recursively, in service order |
| TC-U-07 | `Shape_ShouldKeepFlattenedObjectFieldsAsScalars` | a non-array object arrives as JSONPath scalars |
| TC-U-08 | `Shape_ShouldOrderParametersAsAFreshDesignerBuild` | BP2 order In, Out, then the four result params |
| TC-U-09 | `Shape_ShouldAddTheFourResultParameters` | Success, Response, StatusCode, RequestBody with the designer's types and values |
| TC-U-10 | `Shape_ShouldWriteCaptionResources` | caption keys by name path; the " - Input"/" - Output" suffix on a top-level shared caption only, not on a nested item (EM2, M-24) |
| TC-U-11 | `Shape_ShouldLeaveEveryServiceParameterUnbound_AndCopyNoDefault` | non-Boolean L8 `{}`, Boolean L8 `false` (Boolean In measured by EM2, M-24); no service default copied (Q16) |
| TC-U-12 | `Shape_ShouldMarkEveryParameterValueSerializable` | `IsValueSerializable` true, as the platform sync forces |
| TC-U-13 | `Shape_ShouldAgreeWithThePlatformProvider_AtTopLevel` | top level equals the platform provider's own case tables |
| TC-U-14 | `Shape_ShouldBeDeterministic_ExceptForUIds` | equal with UIds normalised; every UId fresh |
| TC-U-94 | `Shape_ShouldRefuseANonArrayObjectParameter` | a non-array Object param refused, nothing shaped |
| TC-U-100 | `Shape_ShouldAlwaysGenerateNestedItems` | no feature input, no notice: guard against reintroducing the nesting gate (Q3) |

### Catalog and resolver (Q9, Q10, Q16, Q20) - `WebServiceCatalogTests`, `WebServiceResolverTests` (P-A1)

| Id | Test | Intent |
|---|---|---|
| TC-U-15 | `Resolve_ShouldFindTheService_ByUId_ThenName_ThenCaption` | Q9 resolution order |
| TC-U-16 | `Resolve_ShouldIgnoreCase_AndStoreTheManagerItemUId` | case-insensitive; IX1 = manager-item UId |
| TC-U-17 | `Resolve_ShouldRefuseAnAmbiguousService_ListingTheCandidates` | ambiguity refused, the matching names listed with their methods, no caption (defensive) |
| TC-U-18 | `Resolve_ShouldRefuseAnUnknownService_ListingEveryServiceName` | codes with captions and methods listed (K-1), then the registration pointer |
| TC-U-19 | `Resolve_ShouldMatchTheMethodIgnoringCase_AndWriteItsCanonicalName` | canonical IX2; N2 names the input and the spelling |
| TC-U-20 | `Resolve_ShouldRefuseAnUnknownMethod_ListingTheServicesMethods` | only the resolved service's methods listed |
| TC-U-21 | `Resolve_ShouldRefuseTwoMethodsThatDifferOnlyInCase` | case-only method twins refused (defensive) |
| TC-U-22 | `Create_ShouldRefuseABlockWithoutServiceOrMethod` | refused before anything is written |
| TC-U-23 | `Resolve_ShouldNoticeACrossPackageService_AndAddNoDependency` | notice; no `SysPackageDependency` write |
| TC-U-24 | `Resolve_ShouldBuildASoapService_WithANotice` | REST, Undefined, SOAP 1.1 and SOAP 1.2 build; N10 for SOAP only (Q20 D) |
| TC-U-25 | `Refusals_ShouldCarryNoServiceSecret` | planted sentinel absent from every refusal, notice and log line |
| TC-U-26 | `Resolve_ShouldReadTheCatalogOncePerRequest` | one catalog read per request |
| TC-U-105 | `Resolve_ShouldRefuse_WhenNoServiceIsRegistered` | own refusal for an empty service list, how to register |
| TC-U-125 | `Resolve_ShouldListMethodsOfTheFirstServicesOnly_WhenRefusingAnUnknownService` | AC2, K-1: the first 30 services carry their methods (at most 8 each), services 31-100 by name and caption only |
| TC-U-126 | `Resolve_ShouldNotSendARegisteredServiceToRegistration_WhenTheListIsCut` | AC2, K-1: past 100 the rest are counted; a service not shown is used by its exact name or UId, not sent to registration |
| TC-U-127 | `Resolve_ShouldSayWhenAListedServiceHasNoReadableMethods` | AC2, K-1: "methods not readable" / "declares no methods"; still the unknown-service refusal |

### Handler and applier (Q1, Q2, Q7, Q8, Q10, Q11) - `WebServiceApplierTests` (P-B2, P-B3, P-B5, P-D1, P-D2)

| Id | Test | Intent |
|---|---|---|
| TC-U-27 | `Create_ShouldBuildTheCoreWebServiceElement` | `ProcessSchemaWebService`, ManagerItemUId stamped, IX1/IX2 |
| TC-U-28 | `Create_ShouldStoreTheTimeoutInMilliseconds` | seconds x1000; 0 / absent -> no IX3 |
| TC-U-29 | `Create_ShouldStoreIgnoreErrors_OnlyWhenTrue` | IX4 written directly when true (Q7) |
| TC-U-30 | `Create_ShouldRefuseATypeWithoutItsBlock_AndTheBlockOnAnotherType` | strict block / type gate |
| TC-U-31 | `Build_ShouldLetTheSameRequestReferenceTheGeneratedParameters` | mappings, formulas, flows and blocks in the same request see the params |
| TC-U-32 | `Modify_ShouldResolveGeneratedParametersInOperationOrder` | operation order decides |
| TC-U-33 | `SetElement_ShouldTouchNothingElse_WhenOnlyTimeoutOrIgnoreErrorsChange` | BP2, resources, rows byte-identical |
| TC-U-34 | `SetElement_ShouldBeANoOp_ForTheSameServiceAndMethod` | same service and method in any case: no write, no notice |
| TC-U-35 | `SetElement_ShouldReplaceEveryGeneratedParameter_WhenTheMethodChanges` | tagged params regenerated, dropped bindings listed, `Success` / `Response` / `StatusCode` keep their UIds; the caption is kept, as the designer keeps it; a planted service-default sentinel is absent from N4 (Q16) |
| TC-U-36 | `SetElement_ShouldReplace_WhenTheServiceChanges` | the same across services (EM4 oracle); the caption becomes `<service caption> <N>`, as the designer renames it (`setElement` has no caption member) |
| TC-U-37 | `SetElement_ShouldRefuseAChange_ThatStrandsADependent` | anything (another element, a process parameter, a flow condition, a formula body) reading a parameter of the element other than Success / Response / StatusCode (a tagged input, a nested item, `RequestBody` included) refuses the change (`FindDependents(element, survivingParameterUIds: {Success, Response, StatusCode UIds}, includeOwner: false)`); a planted service-default sentinel is absent from the refusal (Q16) |
| TC-U-38 | `SetElement_ShouldRefuseAChange_WhenADependentReadsASameNamedOutput` | a same-named parameter of the new method does not save a reader: references are by UId |
| TC-U-39 | `Apply_ShouldWriteNothing_WhenAnyMemberIsRefused` | validate all before write |
| TC-U-108 | `SetElement_ShouldRefuseAServiceWithoutAMethod_ListingItsMethods` | `service` alone (no `method`) refused with the new service's method list; nothing written (P-D1) |
| TC-U-40 | `RemoveElement_ShouldRemoveTheElementAndItsMappingRows` | existing remove deletes the element's BK15 rows |

### BK15 rows (Q4) and result-param survival (Q11 A3) - `WebServiceResultMappingRowTests` (P-B4, P-D1)

| Id | Test | Intent |
|---|---|---|
| TC-U-41 | `Create_ShouldWriteOneMappingRowPerResultParameter` | 4 rows in BP2 result order |
| TC-U-42 | `Row_ShouldCarryTheDesignersKeySet` | the designer's key set, no GT4 |
| TC-U-43 | `Row_ShouldHoldItsOwnSourceValue` | Success GT1 without GS5; no aliasing of the param value |
| TC-U-44 | `BindingTheRequestBody_ShouldKeepItsRowInStep` | GT1 follows L8 after a binding |
| TC-U-45 | `Rows_ShouldNeverTargetTaggedOrNestedParameters` | rows for result params only |
| TC-U-46 | `MethodChange_ShouldLeaveExactlyOneRowPerResultParameter` | no duplicate or orphan row after a change |
| TC-U-106 | `RenameElement_ShouldLeaveTheRowsA2Unchanged` | `A2` not rewritten on a rename (Q4) |
| TC-U-107 | `SetElement_ShouldKeepTheResultParameters_SoTheirReadersSurvive` | readers of Success / StatusCode survive a method and a service change; a reader of any other parameter of the element still refuses (Q11 A3) |

### Guards and notices (Q1, Q7, Q10, Q12, Q13, Q20) - `WebServiceGuardTests` (P-C2, P-C3, P-D3)

| Id | Test | Intent |
|---|---|---|
| TC-U-47 | `UserTaskRoute_ShouldRefuseWebServiceUserTask_PointingToTheBlock` | generic `userTask` route refuses `WebServiceUserTask` (Q1) |
| TC-U-48 | `MultiInstance_ShouldBeRefused_OnAWebServiceElement` | `setElement` with a `webService` block on a designer-built element that carries `BP6` refused, nothing written (Q20); the describe issue is TC-U-79 |
| TC-U-52 | `NestedListRequestBinding_ShouldRaiseAPlatformNotice` | N5 on the sourceless nested-list predicate, every target (Q13) |
| TC-U-53 | `ModifyOfAnotherElement_ShouldNotice_WhenTheMethodNoLongerResolves` | latent-loss notice on a modify that leaves an element unresolved, unrelated or not (Q12, EM8; TC-U-114 covers the element itself); a planted service-default sentinel is absent from N7 (Q16) |
| TC-U-54 | `ModifyOfAnotherElement_ShouldNotice_WhenTheServiceIsMissing` | the same for a missing service |
| TC-U-55 | `Notices_ShouldBeWarningsOfTheSavedEdit_Only` | a refused request carries no notice as a warning (N7 on a pre-save refusal is in the refusal text, TC-U-115) |
| TC-U-95 | `CreateAndRetarget_ShouldListTheGeneratedParameters` | N1 lists every generated param by stored name (Q10) |
| TC-U-96 | `IgnoreErrors_ShouldBeSavedWithANotice_WhenTheFeatureGateIsOn` | N3: saved, not refused, while the feature is on; one single-purpose reader of `DisableIgnoreErrorsInWebServiceUserTask` with a test fake, as `FileProcessingFeatureReader` |
| TC-U-97 | `EndOfRequest_ShouldNoticeARequiredInputWithoutASource` | N8 judged at the end of the request |
| TC-U-98 | `Shape_ShouldNoticeADuplicateNestedNameOrTag` | N9 on create and retarget |
| TC-U-114 | `ModifyOfTheElement_ShouldNotice_OnlyWhenItStillDoesNotResolve` | N7 also on the unresolved element configured in the request: a settings-only change gets it, a resolving retarget does not (P-D3, owner 2026-11-05) |
| TC-U-115 | `EnsureValidForSave_ShouldCarryTheDriftNotices_WhenTheRefusedReferenceNamesADriftedElement` (`ProcessSchemaValidatorTests`) | AC7, F-2: the pre-save refusal keeps the platform's text and appends N7: the named element first, under the repair-order lead, in the refusal form; the other drifted element after it with the ordinary notice (EM8 step 8 shape, M-51, M-52) |
| TC-U-116 | `EnsureValidForSave_ShouldTieTheRawPlatformReferenceToTheDriftedElement` (`ProcessSchemaValidatorTests`) | AC7, F-2: the named element is found in the platform's raw message; the element name is compared cut to the same length on both sides |
| TC-U-117 | `EnsureValidForSave_ShouldCarryTheDriftNotices_WhenNoRefusedMessageNamesTheElement` (`ProcessSchemaValidatorTests`) | AC7, F-2: no named element -> the "This process also has ..." lead and the ordinary notices |
| TC-U-118 | `WithWebServiceDrift_ShouldCountTheNoticesPastTheCap` (`ProcessSchemaValidatorTests`) | AC7, F-2: at most 3 notices, named ones first, the rest counted |
| TC-U-119 | `WithWebServiceDrift_ShouldKeepEqualNoticesAndSkipNullOnes` (`ProcessSchemaValidatorTests`) | AC7, F-2: equal notices both kept; a null notice skipped |
| TC-U-120 | `WithWebServiceDrift_ShouldCarryTheNotice_WhenItHasNoRefusalForm` (`ProcessSchemaValidatorTests`) | AC7, F-2: a notice without a refusal form is carried in its end-of-request form |
| TC-U-121 | `EnsureValidForSave_ShouldKeepThePlatformRefusal_WhenThereIsNoDriftOrTheReporterFails` (`ProcessSchemaValidatorTests`) | AC7, F-2: no drift, or a failing reporter -> the platform's refusal alone |
| TC-U-122 | `EnsureValidForSave_ShouldNotAskForDrift_WhenThePlatformAccepts` (`ProcessSchemaValidatorTests`) | AC7, F-2: no drift read when the platform accepts the save |
| TC-U-123 | `DriftNotices_ShouldNameTheElementOfEachNotice` | AC7, Q12 A: each drift notice comes with the element it is about |
| TC-U-124 | `DriftNotices_ShouldDropTheReselectRemedy_FromTheRefusalForm` | AC7, F-5: the refusal form is the cause without the re-select / `setElement` remedy; an unregistered service is restored by installing its package |

Dropped: TC-U-49, 50, 51 (the IX4 capability guard and its source scan) and TC-U-76 (describe of IX4 behind the
guard) by Q7, 2026-11-03: EM9 showed the package already needs about 8.3.2, so IX4 is written directly (TC-U-29)
and read directly (TC-U-69).

### Mapping into and out of nested items (Q2, Q5, Q6, Q7) - `WebServiceMappingTests` (P-B6: TC-U-56..64, 66, 67; P-C1: TC-U-68, closing with P-D1; P-C6: TC-U-65, 101)

| Id | Test | Intent |
|---|---|---|
| TC-U-56 | `Map_ShouldBindALeafFromAProcessParameter_LeavingItsContainerUnbound` | item-level binding, container `{}` |
| TC-U-57 | `Map_ShouldWriteAFormulaOrConstant_OntoTheNamedItemOnly` | no collection rule for an unstructured source |
| TC-U-58 | `Map_ShouldBindTheContainer_WhenAnItemReadsAnotherCollectionsItem` | container bound to the source collection |
| TC-U-59 | `Map_ShouldBindACollectionToACollection_AcrossTwoWebServiceElements` | COL <-> COL |
| TC-U-60 | `Map_ShouldRefuseAListOfObjectsItemFromAListOfScalarsItem` | COL <- ObjectList refused, nothing written |
| TC-U-61 | `Map_ShouldRefuseASourceDeeperThanTheTarget` | depth rule |
| TC-U-62 | `Map_ShouldSetTheRequestBody_FromAConstantAFormulaOrAParameter` | stored as given, no JSON check, no notice (Q5) |
| TC-U-63 | `Map_ShouldReadTheResponseIntoADeclaredCollection` | two-level declared Collection read (Q6) |
| TC-U-64 | `Map_ShouldRefuseAThirdLevelOrScalarListResponse` | depth 3 and ObjectList refused (Complex / structured parameter types follow-up) |
| TC-U-65 | `TypeFromElement_ShouldRefuseAWebServiceCollectionMirror_PointingToADeclaredCollection` | mirror refused with the declared-Collection pointer |
| TC-U-101 | `TypeFromElement_ShouldRefuseAWebServiceObjectListMirror` | the ObjectList mirror, silently shapeless today, refused |
| TC-U-66 | `Map_ShouldReadResultParametersAsOrdinaryOutputs` | Success / StatusCode / Response in mappings and flows (Q7) |
| TC-U-67 | `Map_ShouldAddressParametersByTheirStoredNames_Only` | the Tag is not an alias (Q2) |
| TC-U-68 | `DescribeThenReapply_ShouldRoundTripNestedBindings` | described bindings re-apply unchanged (the described block sent back as is) |

### Describe (Q12, Q14, Q16, Q20) - `WebServiceElementHandlerDescribeTests` (P-C1, P-D3)

| Id | Test | Intent |
|---|---|---|
| TC-U-69 | `Describe_ShouldReportTheWebServiceMember` | `buildType`, `webService {...}`, IX3 back in seconds |
| TC-U-70 | `Describe_ShouldReportTheStoredTree_NotTheServicesTree` | tree read from the element, direction from the item's own L12 |
| TC-U-71 | `Describe_ShouldFallBackToTheRawUId_WhenTheServiceIsGone` | `serviceResolved` false, `service` null, `serviceUId` the raw UId, never throws |
| TC-U-72 | `Describe_ShouldRaiseAnIssue_WhenTheMethodDoesNotResolve` | the issue warns of the loss on the next save |
| TC-U-73 | `Describe_ShouldRaiseAnIssue_ForNestedDrift` | missing / extra nested item, composite without L18 |
| TC-U-74 | `Describe_ShouldRaiseNoIssue_ForEveryShapeThePackageWrites` | no false alarm |
| TC-U-75 | `Describe_ShouldNeverCarryAServiceDefaultOrCredential` | no planted sentinel in the reply (Q16) |
| TC-U-77 | `Describe_ShouldReportTheTimeoutAsUnset_WhenIX3IsAbsent` | ms / 1000 as a fraction, unset when absent |
| TC-U-78 | `DescribeThenReplay_ShouldChangeNothing` | replaying the described block unchanged, read-back-only keys included, writes nothing |
| TC-U-79 | `Describe_ShouldReportAMultiInstanceElement_AsFoundWithAnIssue` | a multi-instance element found with an issue, not an error; a SOAP element is ordinary (Q20) |
| TC-U-80 | `Describe_ShouldReportAGenericWebServiceElement_ThatLostItsMethod_WithoutThrowing` | product elements with a lost method describe cleanly |
| TC-U-99 | `Describe_ShouldRaiseAnIssue_ForADuplicateNestedNameOrTag` | N9 text on read-back |
| TC-U-102 | `Describe_ShouldRaiseAnIssue_WhenTheMethodMatchesOnlyIgnoringCase` | case-only stored method reported (EM11) |
| TC-U-103 | `Describe_ShouldRaiseAnIssue_WhenAResultParameterIsMissing` | Success / Response / StatusCode missing reported; RequestBody optional |
| TC-U-104 | `Describe_ShouldRaiseAnIssue_WhenTheElementIsMarkedInvalid` | BL11 false reported |
| TC-U-109 | `Describe_ShouldReplayTheWebServiceBlock_ThroughTheServerKeyCheck` | the read-back-only keys are derived automatically because the write and describe members are both keyed `webService`; nothing to register. A described block sent back unchanged, read-back-only keys kept, passes the server's undeclared-key check (`UnknownRequestKeyDetector`, from 1.6.6.136), which accepts them as read-back keys (P-B1, P-C1) |
| TC-U-110 | `SoapNotice_ShouldBeRaisedOncePerElement` (`WebServiceRefusalTests`) | N10 names its element: two elements on one SOAP service give two notices (Q20 D) |
| TC-U-111 | `Resolve_ShouldExposeTheServiceType_WithoutRefusingSoap` (`WebServiceResolverTests`) | the resolver reports the service type and refuses no SOAP service (Q20 D) |
| TC-U-112 | `SetElement_ShouldChangeOnlyTheSetting_OfASoapElement` (`WebServiceRetargetTests`) | a settings-only change on a stored SOAP element writes only the setting, no N10 (P-D1) |
| TC-U-113 | `SetElement_ShouldRetarget_BetweenSoapAndRest` (`WebServiceRetargetTests`) | SOAP -> REST and REST -> SOAP retarget as for REST; N10 only onto SOAP (P-D1) |

### Platform probes (Q3, Q9, Q12) - `WebServicePlatformProbeTests` (P-A3), *probe*, both configurations

| Id | Test | Intent |
|---|---|---|
| TC-U-81 | `Harness_ShouldHoldNoParametersProvider_ByDefault` | why the package never calls the sync unguarded |
| TC-U-82 | `PlatformSync_ShouldBeANoOp_OnTheShapersOutput` | the platform sync changes no byte of the shaper output |
| TC-U-83 | `PlatformSync_ShouldPruneTaggedParameters_WhenTheMethodDoesNotResolve` | unit side of EM11 |
| TC-U-84 | `PlatformSync_ShouldClearEverything_WhenTheServiceUIdIsEmpty` | results and rows gone too |
| TC-U-85 | `PlatformSync_ShouldAppendANewServiceParameterFlat_AtTheEnd` | appended flat, after the result params |
| TC-U-86 | `PlatformSync_ShouldKeepTheNestedItemsOfAMatchedParameter` | UId, value and L18 kept |

### Contract, DI, factory, round trip, clone - rows in existing fixtures

| Id | Test | Intent |
|---|---|---|
| TC-U-87 | `WebServiceMembers_ShouldKeepTheirWireNames` (`ProcessDesignServiceWireContractTests`) | wire names of the new members |
| TC-U-88 | `OperationContractCount_MustStayPinned` (existing) | no new operation (Q10) |
| TC-U-89 | `CompositionRoot_RegistersEveryElementHandler`, `..._ResolvesEveryCollaborator_FromScope` (existing) | the new collaborators resolve in a scope |
| TC-U-90 | `SupportedTypes_ShouldNameWebService` (factory) | token `webService` accepted |
| TC-U-91 | `MetaData_ShouldRoundTripTheElementKeys` | IX1-IX4, BO2/BO3, BP2 survive write/read |
| TC-U-92 | `CreateVersionClone_ShouldKeepTheWebServiceElementOwnedByTheNewVersion` (`ProcessVersionCloneFactoryTests`) | clone stamps on params and rows (P-D4) |
| TC-U-93 | `SubProcessSync_ShouldEmitExactlyThePinnedNotices` (`SubProcessSyncCharacterizationTests`) + `EveryFixture_CarriesACategoryTheCiFilterSelects` (existing) | P-A4 proof: pinned before the `Snapshot` / `Diff` lift, which changes no existing test file |

## TC-P - package parity tests

Comparison rule: WSCR, serialization §8.2 (W1-W14) with the allowed differences of §8.3 (AD1-AD6, and AD10: the
designer's `BL11:false` on the EM4 S2 element, whose required `UsrWscId` is unbound; the package never writes
`IsValid`); every parity test names the W and AD ids it applies. AD7-AD9 are for the corpus sweep only (a script, result in the package PR
body, not a committed test).

| Pattern | Fixture | Ids | Oracle |
|---|---|---|---|
| A - corpus pairs, committed verbatim (`PROVENANCE.md` SHA pins, `-text`) | `WebServiceParityTests` | TC-P-01..04, 06..08 | `CheckMarketplaceAppNeedUpdateSubProcess` + `CreatioMarketplaceApi`, `ActionGeminiSearch` + `GeminiApiSearch`, `ExecuteUpgradeBsf` + `teamcitybpmonline_BSF` |
| B - stand designer captures, committed redacted | `WebServiceParityTests` (stand rows) | TC-P-05, 09..13, 15, 16 | EM2, EM3, EM4 (measured 2026-11-03), EM2 S2-S4 (2026-11-05); TC-P-13 waits for EM5 |
| C - the platform writer's JSON | `WebServiceSerializationTests` | TC-P-14 | the quoted `ActionGeminiSearch` node; EM2's node |

| Id | Test | Intent |
|---|---|---|
| TC-P-01 | `Capture_ShouldHashToTheValuePinnedInProvenance` | a hand edit or line-ending change fails |
| TC-P-02 | `Capture_ShouldHoldTheShapeTheOracleReads` | tripwire on tagged / nested / result / row counts |
| TC-P-03 | `Element_ShouldEqualTheShippedElement` | the shaper stores the captured BP2 (AC5) |
| TC-P-04 | `MappingRows_ShouldEqualTheShippedRows` | BK15 rows under W10 with AD2 (Q4) |
| TC-P-05 | `Captions_ShouldEqualTheStandCapture` | stand-only: TC-I-01/02 `SysLocalizableValue` rows equal the EM2/EM3 capture (W13) |
| TC-P-06 | `Bindings_ShouldEqualTheShippedBindings` | item-level, declared-Collection read and RequestBody formula bytes |
| TC-P-07 | `ServiceCapture_ShouldLoadThroughThePlatformReader` | *probe*: the platform reader loads a service capture |
| TC-P-08 | `Fixtures_ShouldCarryNoCredential` | no credential in any committed file: no `IQ5` value with `IV2` 1, no constant `Authorization` default; the verbatim corpus pairs may keep the shipped services' non-credential `IU5` defaults (Q16) |
| TC-P-09 | `Element_ShouldEqualTheStandCapture` | the current designer's element incl. IX3 / IX4 (EM2) |
| TC-P-10 | `Bindings_ShouldEqualTheStandCapture` | scalar, constant, formula and item-level bindings (EM2) |
| TC-P-11 | `ResponseCollection_ShouldEqualTheStandCapture` | hand-made declared Collection read (EM3 route (b)) |
| TC-P-12 | `MethodChange_ShouldEqualTheStandCapture` | the builder's method change on the EM4 S1 capture, its Formula reader of the tagged output removed first (A3 would refuse otherwise) and `UsrWscQ_In` mapped in the same request (as EM4 step 2 did before the S2 save), equals the S2 capture under WSCR for the element node and its BK15 rows; UIds not compared; the S2 capture's dangling Formula token outside the comparison; the designer's `BL11:false` on the S2 element (unbound required `UsrWscId`) is AD10; the package never writes `IsValid` |
| TC-P-13 | `DesignerResave_ShouldChangeNoByte` | the STOP gate restated: builder element re-saved by the designer, diff = 0 or an allowed AD (EM5) |
| TC-P-14 | `WriteMetaData_ShouldStoreTheDesignersShape` | writer JSON diff by path; the reader accepts the bytes |
| TC-P-15 | `SoapElement_ShouldEqualTheStandCapture` | the builder's SOAP create equals the S2 capture under WSCR (AD10); the companion `SoapElement_ShouldDifferOnlyInTheBooleanConstant_WhenItIsSentAsAValue` pins the one Boolean-constant difference permanently: EM6-B's trace showed the builder's ConstValue holds `true` at run time (M-39), oracle A cannot be saved (F-1, resolved 2026-11-10: dropped from the EM6-B steps), no code change |
| TC-P-16 | `SoapToRestSwitch_ShouldEqualTheStandCapture` | the builder's SOAP -> REST retarget equals the S4 capture on the element node and its rows; UIds not compared (A3) |

## TC-C - clio unit tests

New rows go into existing fixtures and follow the host fixture's `[Category("Unit")]` and `[Property("Module", ...)]`
placement.

| Id | Test | Intent | Module |
|---|---|---|---|
| TC-C-01 | `ProcessBlockKeyCheckTests.Check_Should_AcceptTheWebServiceBlock_OnEveryRoute` | the key set exists on create, `addElement`, `setElement` | ProcessModel |
| TC-C-02 | `ProcessBlockKeyCheckTests.Check_Should_RefuseAnUnknownWebServiceKey_NamingItsPath` | `webService.<key>` path and the accepted keys; `webService.multiInstanceOptions` on create is refused here, before sending (Q20) | ProcessModel |
| TC-C-03 | `ProcessBlockKeyCheckTests.Check_Should_TolerateTheReadBackFields_OfADescribedBlock` | the read-back fields `serviceUId`, `serviceResolved`, `methodResolved`, `issues`, `parameters` warned, not refused | ProcessModel |
| TC-C-04 | `KeySets_Should_EqualTheMembersOfTheBundledServerContracts` (existing) | key set mirrors the server descriptor after the rebundle | ProcessModel |
| TC-C-05 | `ReadBackFields_Should_BeDescribeOnlyMembers` (existing) | read-back keys exist on the describe type only | ProcessModel |
| TC-C-06 | `ToolContractShortFormTests` call-shape drift + `ToolContractPayloadBudgetTests.MaxInlineWireReplyBytes` (existing) | `webService` in the short form within the wire budget | McpServer |
| TC-C-07 | `ProcessDesignerRequiresPackageAttributeTests` + `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` (existing) | the create / modify / modify-as-new-version floor moves to the claimed cut | Command, Common |
| TC-C-08 | `BundledProcessBuilderPackageTests` (existing) | pins moved by the rebundle script; operation and gate counts unchanged (Q10) | Common |
| TC-C-09 | `CreateBusinessProcessToolTests` / `ModifyBusinessProcessToolTests` `..._Should_Forward_WebService_Verbatim` | the block reaches the command unchanged | McpServer |
| TC-C-10 | `ServerProcessDescriberTests.Describe_ShouldReadTheWebServiceMember_WhenServerReportsIt`, `..._LeaveItNull_WhenServerOmitsIt`, `..._KeepUnknownMembers_InTheBag` | typed `DescribedWebService` with an overflow bag; tags, items and unbound inputs reach the agent (Q14) | ProcessModel |
| TC-C-11 | description pins (create, modify, describe, modify-as-new-version, validate-process-graph, list-user-tasks tools) | one pointer clause to `process-web-service`; `WebServiceUserTask` named as refused (Q1, Q15) | McpServer |
| TC-C-12 | `ToolContractPayloadBudgetTests` (existing) | the swap pays for every clause; before/after sizes in the PR body (Q15) | McpServer |
| TC-C-13 | prompt pins (create, modify process prompts; `ListUserTasksPrompt`) | generic-route refusal lists gain `WebServiceUserTask` | McpServer |
| TC-C-14 | `WorkspaceTemplateGuidanceDriftTests.UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` (existing) + `curated-knowledge-names.json` re-pin | red until the fixture is re-pinned to a generation with `process-web-service`; draft-until-published is policy (Q19) | McpServer |
| TC-C-15 | `ManagerMapResolveDataIdTests` (existing) | `webservice` still resolves to the web-service event type | ProcessModel |
| TC-C-16 | `McpE2eSelectionCoverageTests` (existing) | the new e2e fixture is selected when its tool changes | Core |

## TC-I - clio MCP e2e (a registered test stand)

Fixture `WebServiceElementToolE2ETests` (TC-I-13: the existing `ListUserTasksToolE2ETests`), category
`McpE2E.ProcessDesigner`, shape copied from
`SendEmailAttachmentsToolE2ETests` / `ProcessFileElementToolE2ETests`: `MinimumPackageVersion` = the claimed cut
(below it the fixture is Ignored); writes strictly sequential; cleanup newest-first in `finally`; stand prerequisites
`UsrWscEchoService`, `UsrWscDriftService` (methods `UsrWscDriftGone` and `UsrWscOrders2`), `UsrWscDeadService` and the designer-built
`UsrWscEm2CaptureA` (all exist since 2026-11-03,
measurements M-23, M-24), a test needing an absent one is Ignored with the build instructions; every test reading a
reply asserts the `X-Wsc-Probe` default is absent (Q16). The PR lane excludes this category, so the evidence is a
stand run recorded in the clio PR body per framework with Ignored = 0.

| Id | Test | Asserts |
|---|---|---|
| TC-I-01 | `CreateBusinessProcess_Should_StoreTheWebServiceElementInTheDesignersShape_AndDescribeItBack` | full block on `UsrWscEcho`, four binding kinds; describe, then stored bytes (IX1-IX4, BP2, L18, ItemValue, result params, 4 rows, captions) |
| TC-I-02 | `CreateBusinessProcess_Should_ReadTheResponseIntoADeclaredCollection` | two-level declared Collection read; describe decodes it |
| TC-I-03 | `CreateBusinessProcess_Should_RefuseAndCreateNothing` | unknown service / method, `WebServiceUserTask`, block on a `userTask`: refused by the server, nothing created; `webService.multiInstanceOptions`: refused by clio's key check before sending (a key-check refusal, not the Q20 text), nothing created |
| TC-I-04 | `CreateBusinessProcess_Should_StoreTheCanonicalMethodName` | lower-case method stored canonical, params present (EM11) |
| TC-I-05 | `ModifyBusinessProcess_Should_ReplaceOnMethodChange_AndGuardDependents` | refused while another element (a formula) or a process parameter reads a parameter of the element other than Success / Response / StatusCode; after the reader is removed the change builds and N4 lists the dropped bindings with their values |
| TC-I-06 | `ModifyBusinessProcess_Should_ChangeNoStoredByte_WhenTheDescribedBlockIsSentBack` | describe -> replay of the described block unchanged is byte-neutral |
| TC-I-07 | `DescribeBusinessProcess_Should_ReadTheDesignerBuiltFixture` | read-only on `UsrWscEm2CaptureA`; resolved flags true; no header default |
| TC-I-08 | `RunProcess_Should_CallTheService_AndReadTheResponse` | result params of the stand's own answer land in an Activity; reduced under E4 (Q17) |
| TC-I-09 | `RunProcess_Should_Continue_WhenTheCallFailsAndErrorsAreIgnored` | no-answer call: ignoreErrors true reaches the end, false ends in Error; a 404 only as the Success = false branch (EM7 designer half, M-28) |
| TC-I-10 | `ModifyProcessAsNewVersion_Should_KeepTheWebServiceElement` | the clone's params and rows carry the new version's UId |
| TC-I-11 | `ValidateProcessGraph_Should_AcceptADescribedWebServiceElement` | `validate-process-graph` over the described element |
| TC-I-12 | `CreateBusinessProcess_Should_WarnAboutANestedListRequestBinding` | N5 text only, no run (Q13) |
| TC-I-13 | a new row in the existing `ListUserTasksToolE2ETests` (same category) | the `list-user-tasks` tool's contract text names `WebServiceUserTask` as refused on the generic route (Q1) |

## TC-K - clio-knowledge tests and gates

Run the whole `automation/Clio.Knowledge.Bundle.Tests` project.

| Id | Test | Intent |
|---|---|---|
| TC-K-01 | `WebServiceGuidanceTests.Guide_ShouldOwnTheBlock_AndTheEntryArticlesShouldRouteToIt` | `process-web-service` owns the block, the FULL-contract gate, the real floor and the stored-name rule; `element-catalog` routes to it |
| TC-K-02 | `WebServiceGuidanceTests.Guide_ShouldStateTheReadBack` | describe member, stored tree, resolved flags, issues, replay recipe (the described block sent back unchanged) |
| TC-K-03 | `WebServiceGuidanceTests.Guide_ShouldStateTheRequestBodyRules` | nested items via mappings; when to use `RequestBody`; no JSON check, no conflict notice (Q5) |
| TC-K-04 | `WebServiceGuidanceTests.Guide_ShouldStateTheResponseRules` | declare a Collection (<= 2 levels); mirror, scalar lists, third level not buildable yet |
| TC-K-05 | `WebServiceGuidanceTests.Guide_ShouldStateResolutionReplaceAndRefusals` | resolution, replace rule with A3, the refusals, drift issues and the modify notice; a designer save of a process whose web-service element no longer resolves deletes every parameter of the element (result params included) and its BK15 rows and leaves it `BL11:false`, so the remedy (restore the service or method in Studio > Web services, or first remove the reads of the missing parameters, then re-point the element) comes before any designer save, with `methodResolved: false` as the signal |
| TC-K-06 | `WebServiceGuidanceTests.Guide_ShouldStateTheRunTimeFacts` | result params, timeout, ignoreErrors and its feature gate, an HTTP error answer vs no answer (EM7), the nested-list caveat stated as unmeasured on the stand |
| TC-K-07 | `ProcessGuideContentPreservationTests` (existing) | the READ-ONLY pin replaced by the version-floor pin |
| TC-K-08 | `WebServiceGuidanceTests.NoProcessArticleOrManifestDescription_ShouldClaimWebServiceIsUnbuildable` | the retired read-only claim is gone everywhere |
| TC-K-09 | `WebServiceGuidanceTests.Routing_ShouldSeparateTheProcessElementFromConfigurationServices` | the routing row tells it apart from the configuration web-service guides |
| TC-K-10 | size gates (existing): `GuidanceInlineReplyBudgetTests`, `ProcessGuideResponseSizeTests` | the `process-modeling` edit is a swap (Q15) |
| TC-K-11 | manifest and routing (existing): `GuidanceMigrationTests.PostMigrationGuidance`, `ProcessGuideCrossReferenceTests`, `GenerationSequenceTests` | the article is declared and reachable; the generation is new |
| TC-K-12 | `WebServiceGuidanceTests.Guide_ShouldCarryNoInternalReferenceOrSecret` + refute-first fact-check gate | no ticket key, spec path, placeholder floor or credential-looking default; fact-check: one agent per claim cluster, barred from `docs/knowledge/`, the spec folder, research notes and other agents' output; verdict table in the KB PR body |
| TC-K-13 | `WebServiceGuidanceTests.Guide_ShouldStateTheTaskWithoutDetails` | find the service through the refusal list, ask when several fit, stop when none fits, two-step build, keys from a setting or parameter (Q10) |

## Agent scenario runs (Q10)

After the claimed cut is installed and the article is published to a local library: two headless sessions on a
registered test stand, clean context, shipped guidance only, transcript kept as evidence in the clio PR. A failure is fed back
into the article before the KB PR leaves draft.

| Id | Task | Pass |
|---|---|---|
| AR-1 | "Build a process that gets the published version of a Marketplace application" (no service named) | reaches `CreatioMarketplaceApi.GetAppPublishedVersion` through at most one refusal, builds in two steps, maps by stored names; no credential in the transcript. Met 2026-11-09 (M-48): one refused create gave service and method (K-1); re-run on the shipping build 2026-11-10 (M-58) |
| AR-2 | "Build a process that pulls today's weather from OpenWeather" (not registered) | stops without creating a process, placeholder or Script task; tells the user what registration needs. Met 2026-11-09 (M-46) |

## Manual Jira cases (TC-01..TC-12)

Business tasks given to an agent, drafted in the analysis test plan §8 (conditions, steps, expected results) for the
owner to paste. Under E4 the run-time bullets marked "(not run)" there are not checked on the test stand.

| Id | Task |
|---|---|
| TC-01 | Call a service with values from the process |
| TC-02 | Send a list of orders in the request |
| TC-03 | Keep the returned orders for later steps |
| TC-04 | Build the request body as text |
| TC-05 | Branch on the result of the call |
| TC-06 | Continue the process when the service gives no answer, within a time limit |
| TC-07 | Switch an existing call to another method |
| TC-08 | Refuse to switch the method while another step uses a returned value |
| TC-09 | Ask for a service or method that does not exist |
| TC-10 | Accept a method name written in another case |
| TC-11 | Explain a designer-built call without changing it |
| TC-12 | Warn before editing a process whose service method was removed |

## Stand measurements

Procedures, gates and cleanup: the measurements file. Every stand write waits for the owner's yes. The final-cut
campaign ran on 2026-11-09 (M-36..M-46); the unknown-service refusal, AR-1 and EM8 step 8 were re-measured on the
claimed cut (M-47, M-48, M-51, M-52). The plan's late-measurement gate is met 2026-11-10: EM6, EM6-B, EM7, EM8 step 8, EM10, EM11, EM12 and KB-7 AR-1 re-run on the shipping build (M-53..M-58); EM5 not repeated (the later changes touch only refusal texts and the validator).

| EM | Status 2026-11-10 | Feeds |
|---|---|---|
| EM0 | done (M-22) | PROVENANCE of TC-P-09..13; preconditions of TC-I-08/09, TC-06 |
| EM1 | done (M-19, M-23): `UsrWscEchoService`, `UsrWscDriftService`, `UsrWscDeadService` | every TC-I; TC-P-09; manual conditions |
| EM2 | S1 done (M-24); S2-S4 (SOAP) done (M-32..M-35); step 9 read-back done in the final-cut campaign (M-36); not repeated on the shipping build (the later changes touch only refusal texts and the validator) | TC-P-05, 09, 10, 14, 15, 16; TC-U-04, 10, 11, 24, 57, 110..113; `UsrWscEm2CaptureA` for TC-I-07, TC-11 |
| EM3 | routes (a) and (b) done (M-15, M-25) | TC-P-05, 11; ENG-92730 "Complex / structured parameter types" input |
| EM4 | done (M-20, M-26, M-27) | TC-P-12; TC-U-35, 36, 107 |
| EM5 | STOP gate passed on the experimental cut (2026-11-04); final-cut campaign steps 1-3, 6, 7 passed (M-37), steps 4, 5 not run; not repeated on the shipping build (the later changes touch only refusal texts and the validator) | TC-P-13; final Q4; fallback of TC-U-81..86 |
| EM6 | done in the final-cut campaign, reduced under E4 (M-38: runs, `OutOk` false, `OutCode` 404, the declared `Orders` binding equals EM3 route (b)); the wire bytes stay unmeasured; re-run on the shipping build 2026-11-10 (M-55) | TC-I-08; TC-K-03, 06 |
| EM6-B | done in the final-cut campaign (M-39): by trace (oracle B) the builder's ConstValue holds `UsrWscFlag_In = true` at run time, a one-item list arrives from an item mapping, an unmapped `RequestBody` is null (not ""); oracle A (a flow condition on the element input) cannot be saved (F-1, resolved 2026-11-10: dropped from the EM6-B steps); no code change, TC-P-15's companion test pins the difference permanently; re-run on the shipping build 2026-11-10 with the same trace (M-55) | TC-P-15 |
| EM7 | designer half done (M-28); builder half done in the final-cut campaign (M-40); re-run on the shipping build 2026-11-10 (M-56) | TC-I-09; TC-K-06; TC-06 |
| EM8 | current-builder half done (M-18); service-save and designer halves done (M-29..M-31); step 8 re-measured on the claimed cut: the unrelated modify is refused by the pre-save validation, nothing is written, and the refusal carries N7 with the repair order (M-41, M-51, M-52); re-run on the shipping build 2026-11-10 (M-54); the no-reader case is not measured | TC-U-53, 72, 115, 124; TC-K-05; TC-12 |
| EM9 | done (local, M-14) | TC-U-29; TC-U-49, 50, 51, 76 dropped |
| EM10 | designer half done (M-17); builder half done in the final-cut campaign (M-42: no dependency added, notice given); re-run on the shipping build 2026-11-10 (M-57) | TC-U-23 |
| EM11 | designer half done (M-16); builder half done in the final-cut campaign (M-43: canonical `IX2` with a notice; the unknown-method refusal lists codes without captions, F-3, open); re-run on the shipping build 2026-11-10 (M-57) | TC-U-19, 83, 102; TC-I-04 |
| EM12 | run half not measurable under E4; builder half done in the final-cut campaign (M-44); re-run on the shipping build 2026-11-10 (M-57) | TC-U-52, TC-I-12, TC-K-06 |

## Targeted test filters

| Repository | Iterating | Before every commit / evidence |
|---|---|---|
| crt-process-builder | `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf --filter "FullyQualifiedName~WebService\|FullyQualifiedName~SubProcess\|FullyQualifiedName~ProcessSchemaValidator"` (and `-c dev-n8`) | the whole project on `-c dev-nf` and `-c dev-n8` |
| clio unit | `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&(Module=ProcessModel\|Module=McpServer\|Module=Command\|Module=Common\|Module=Core)" --no-build < /dev/null` | `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null` (more than three modules and `Common` change: full suite; quoted in the commit message and the PR body) |
| clio e2e | - | `dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --filter "TestCategory=McpE2E.ProcessDesigner&(FullyQualifiedName~WebServiceElementToolE2ETests\|FullyQualifiedName~ListUserTasksToolE2ETests)"`, then `-f net8.0`; plus the regression fixtures below with the same category filter |
| clio-knowledge | - | `dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj` |

Write `TestCategory`, never `Category`; close stdin for `clio.tests` (`McpE2eSelectionCoverageTests` runs `git init`).

## Regression scope

Analysis test plan §9: in the package tests the `UserTaskElementHandler*`, `ProcessElementFactory`, `ProcessBuildHandler`,
`ProcessGraphBuilder`, `SubProcess*` (incl. `SubProcessPlatformProbeTests`), mapping (`ProcessMappingService`,
`FileCollectionMapping`, `MultiInstanceMapping`, `ElementSourceDecode`), `ProcessParameterService*` /
`ParameterTypeCompatibility`, `ProcessModifyHandler` / element-operation / `ElementConfigurationLedger`,
`ProcessSchemaValidator` (the pre-save gate now appends N7 to a platform refusal), `ProcessDescriber`, `ProcessVersionCloneFactory`, wire-contract, composition-root and `CiContractGuard` suites, and the
whole project on dev-nf and dev-n8; the clio full unit suite; the e2e fixtures `SubProcessElementToolE2ETests`,
`CreateBusinessProcessToolE2ETests`, `ModifyBusinessProcessToolE2ETests`, `ProcessFileElementToolE2ETests` on both
frameworks; the whole KB test project.

## Risk assessment

The risks and their mitigations are the analysis plan §10; the tests that answer them:

| Risk | Answered by |
|---|---|
| platform load re-sync prunes tagged params of an unresolved element (measured, EM8) | TC-U-53, 54, 72, 82..86, 115..124; EM8 builder half (step 8, M-51, M-52) |
| the designer rewrites a builder-made element (STOP gate) | TC-P-13 (EM5), TC-U-82 |
| the core nested-list fix ENG-55920 Unable to use array inside array in web-service business process element (core#1119) absent on stand `Creatio` | N5: TC-U-52, TC-I-12, TC-K-06 |
| no echo endpoint (E4): request bytes not observable | parity suite (TC-P-06, 10, 14); stated in the AC3 row of the plan §9 DoD and in the PR body |
| wrong-case or unknown method saves green and fails at run time (EM11) | TC-U-19, 83, 102; TC-I-04 |
| secrets in service metadata | TC-U-11, 25, 75; TC-P-08; TC-I-07; TC-K-12 |
| description and guidance budgets at the ceiling | TC-C-06, 12; TC-K-10 |
| scope creep into ENG-92730 "Complex / structured parameter types" | TC-U-64, 65, 101 pin the refusals |
| shared sub-process code changed by the `Snapshot` / `Diff` lift | TC-U-93 characterization first; `SubProcess*Tests` unchanged |
| local core-bin newer than CI | literal platform UIds; first CI run of the draft PR before gate 3 |

## Conventions

AAA with explicit `// Arrange`, `// Act`, `// Assert`; `because` on every assertion; a `[Description]` on every test
naming the Q id where one applies; no Jira key in test names, descriptions or Allure names; categories as the host
fixture places them; no OS paths; package tests on dev-nf and dev-n8; the only credential-like strings in any test are planted
sentinels asserted absent (analysis test plan §10).

## Exit criteria

The stories' Definition of Done (analysis plan §9), with every live row of TC-U, TC-P, TC-C, TC-K and TC-I implemented
and green (see "Test id classes"); TC-U-49, 50, 51, 76 are dropped rows; TC-P-05 is stand evidence; TC-K-12 includes
the fact-check gate; AR-1 and AR-2 transcripts in the clio PR; the e2e stand run with Ignored = 0 on net10 and net8.

## Deliberately not covered

`typeFromElement` mirror of a web-service output, ObjectList parameters, a third level and the designer's "Create from
element" wrapper (Q6, ENG-92730 "Complex / structured parameter types"; refusals only); a discovery tool (Q10);
adding a package dependency for a cross-package service (Q9, notice only); registering a service (ENG-105475
"Register a REST web service through the AI Toolkit"); a run of a duplicate nested name or Tag (N9 and its issue are
tested); a sourceless ObjectList nested in a CompositeObjectList (outside N5); a raw read of the unsynced element on
describe; real external endpoints or an echo (Q17, E4); request bytes on the stand (EM6 request check, EM12); a JSON
check of a constant `RequestBody` and a conflict notice (Q5); an IX4 capability guard and a nesting-feature gate
(Q7, Q3); SOAP at run time (no SOAP endpoint on the stand); multi-instance (Q20, refused).
