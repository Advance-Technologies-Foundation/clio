# ENG-92710 Call web service element: serialization

Written 2026-11-02; updated 2026-11-03 from the stand measurements M-22..M-31 (section 9: the open bytes and their
measured answers). Phase-1 plan: no product code.

Basis:
- crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history only)
- clio `origin/master` `6be5dde28`; clio-knowledge `origin/master` `5f14ceb`
- CrtProcessDesigner 7.8.0 and the PackageStore corpus (branch `7.8.0` checkouts)
- core: trunk `468c4c71b` (tag 8.3.3.1722, 2026-01-24) for every core fact. An older core checkout (2025-07-21)
  predates `IX4` and is not used here (g2).
- stand `Creatio` (Creatio 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131): read-only inventory of 2026-11-02 (r9),
  plus the designer probes and captures of 2026-11-03 (measurements file M-13..M-31; decisions Q4, Q6, Q9, Q11, Q12,
  Q14, Q16)

| Prefix | Root (paths after the prefix are repository-relative) |
|---|---|
| (none) | Sources outside the public repositories (the CrtProcessBuilder package and its tests, CrtProcessDesigner and the other shipped packages, the core platform) are cited by schema, class, file or view name only, never by repository path; the client schemas are `process-webservice-schema`, `parametrized-process-schema-element`, `process-schema-parameter` and `dynamic-process-schema-parameter`, the page is `WebServiceUserTaskPropertiesPage`; research notes r1-r10, g1-g5 and the `_workflow-result.json` verdicts are local |

Tags: [traced] read in source, a hypothesis about run time until measured; [corpus] stored PackageStore metadata or
resources; [measured <date>] counted or observed (corpus scans of 2026-11-02 are this file's own, scripts in the
session scratchpad, unless a note is named); [inferred] my inference. Every [traced] or [inferred] byte names the EM
that settles it. Decision ids Q1-Q20 are the [decisions](eng-92710-call-web-service-element-decisions.md) file's,
agreed by the owner on 2026-11-03; EM ids are the [measurements](eng-92710-call-web-service-element-measurements.md)
file's. Run as of 2026-11-03: EM0 (M-22), EM1 (M-23), EM2 S1 (M-24), EM3 routes (a) and (b) (M-15, M-25), EM4
S1-S3 (M-26, M-27), EM9, the designer halves of EM7, EM10 and EM11, EM8's current-builder, service-save and designer
halves (M-18, M-29..M-31) and the Q4 BK15 probe (M-13). Run since: EM5 on the experimental cut (2026-11-04), and in
the final-cut campaign of 2026-11-09 EM2's read-back, EM5, EM6, EM6-B and the builder halves (M-36..M-46); the plan's
late-measurement gate is met 2026-11-10: EM6, EM6-B, EM7, EM8 step 8, EM10, EM11, EM12 and KB-7 AR-1 re-run on the shipping build (M-53..M-58); EM5 not repeated (the later changes touch only refusal texts and the validator). EP option E4 is chosen (Q17: the test services' base URI is the stand root, no echo
endpoint), so EM6's request side and EM12 are not measurable on stand `Creatio`.

Siblings: [README](README.md) · [facts](eng-92710-call-web-service-element-facts.md) ·
[usage](eng-92710-call-web-service-element-usage.md) · [reuse](eng-92710-call-web-service-element-reuse.md) ·
[design](eng-92710-call-web-service-element-design.md) · [decisions](eng-92710-call-web-service-element-decisions.md) ·
[measurements](eng-92710-call-web-service-element-measurements.md) · [plan](eng-92710-call-web-service-element-plan.md) ·
[test plan](eng-92710-call-web-service-element-test-plan.md) · [estimate](eng-92710-call-web-service-element-estimate.md)

## 0. Summary

- The element is a flow-element `Terrasoft.Core.Process.ProcessSchemaWebService` (`BL7` `652b598d-...`), not a user
  task. Its own keys are four: `IX1` service UId, `IX2` method name, `IX3` timeout ms, `IX4` ignore errors; each is
  omitted at its default. Everything else is `BP2`: tagged parameters generated from the service method, then four
  untagged result parameters. [traced; corpus: IX1/IX2/BO2/BO3/BP2 66/66, IX3 6/66, IX4 2/66;
  Success/Response/StatusCode 66/66, RequestBody 14/66 (11/25 product); one BK15 row per stored result param:
  52 x 3, 14 x 4]
- The target shape is the DESIGNER's, not the platform's: the client (`process-webservice-schema`) builds the nested `L18` trees, the
  `ItemValue` child of a scalar array, the conditional caption suffix, the four result parameters and one `BK15` row
  per result parameter. The platform's `SynchronizeParameters` builds only flat top-level tagged parameters. The
  package therefore owns a port of the client algorithm (Q3) and tolerates the platform sync afterwards (section 7).
  Unlike the client, the port always writes the nested items (Q3: no `L18` gate, 2.5). [traced; verdicts r2-C5,
  r3-C3, r6-C3]
- Every parameter at every level carries `A3 = A4 = <process UId>` and `IL2 = <element UId>`; `L12` is always
  written; `L6` only when true; `L17` on tagged parameters only; every Boolean parameter carries
  `L8 {"GS2":"false","GS5":<process UId>}`, tagged and nested ones included. [corpus, measured 2026-11-02]
- Parity (AC5) is proven against two oracles: the EM2-EM4 designer captures on stand `Creatio`, committed redacted
  (EM2 is the only source for a composite request body, which no stand service declares), and three verbatim corpus
  pairs (process + service capture), under one comparison rule with named allowed differences (section 8).
- Of the fourteen open bytes of section 9, the 2026-11-03 captures settled twelve (EM1-EM4, M-23..M-27): the
  designer writes the corpus key sets and the fresh order, the Boolean default on inputs as inferred (SQ5), and the
  caption suffix on TOP-LEVEL parameters only (SQ3, a generator rule). Two stay open: the wire rendering of a
  flattened request object (SQ8, not measurable on the stand under E4) and what a designer re-save does to a
  builder-made element (SQ13, EM5); SQ13 is now settled by EM5 (measured: passed on the experimental cut 2026-11-04 and in the final-cut campaign 2026-11-09, M-37; a server load drops the top-level caption suffix and a designer save puts it back).

## 1. Element-level keys

### 1.1 Writer chain and defaults

The element is written by the platform writer chain; a `WriteValue(key, value, default)` writes nothing when the
value equals its default (`JsonDataWriter` skips defaults unless `WriteDefValues`, verdict r2-C2). The package
writes through platform objects, so key ORDER and default elision come from the platform; the package controls
values only. [traced]

| Key | Property | Value the package writes | Default (omitted) | Evidence | Tag |
|---|---|---|---|---|---|
| `BL1` | type name | `Terrasoft.Core.Process.ProcessSchemaWebService` | - | `ProcessSchemaWebService`; 66/66 | traced, corpus |
| `BL7` | ManagerItemUId | `652b598d-cdd5-49d5-a86d-aafac88213f3` (`ProcessSchemaElementManager.WebServiceTaskUId`) | `Guid.Empty` | `ProcessSchemaElementManager`; 66/66 | traced, corpus |
| `BO2` | SerializeToDB | `true` (set by `Initialize()`) | `false` | `ProcessSchemaWebService.cs:85-91`; `ProcessSchemaFlowNode.cs:20-21`; 66/66 | traced, corpus |
| `BO3` | IsLogging | `true` (set by `Initialize()`) | `false` | same; 66/66. Side effect: a `false` is never written and reloads as `true` (verdict r3-C2) | traced, corpus |
| `IX1` | ServiceSchemaUId | the resolved service's `SysSchema` UId (Q9) | `Guid.Empty` | `ProcessSchemaWebService.cs` writer; 66/66 | traced, corpus |
| `IX2` | MethodName | the method's canonical NAME (not a UId), spelled as the service stores it (Q9) | `""` | 66/66 | traced, corpus |
| `IX3` | Timeout, int, milliseconds | `timeoutSeconds * 1000` (Q8); 0 or absent = unset | `0` | page saves `seconds * 1000` (`WebServiceUserTaskPropertiesPage` `saveValues`, verdict r2-C2(4)); corpus 6/66: 600000, 1000000 x2, 5000, 1000 x2, all multiples of 1000 | traced, corpus |
| `IX4` | IsIgnoreErrors, bool | `true` when `ignoreErrors: true`, set directly on `IsIgnoreErrors` (Q7: no reflection guard; EM9) | `false` | `ProcessSchemaWebService.cs:22-23, 32, 88-89, 135-136, 205` (commit `5e53b027c9b`, first in `builds-windows/8.3.2.3590`); corpus 2/66, both `true`, both BusinessRulesTest, saved 2025-11-20 (g2) | traced, corpus |
| `BP2` | Parameters | section 2 and 3 | always written | `ProcessSchemaActivity.cs:41`; 66/66 | traced, corpus |
| `BP6` | MultiInstanceOptions | absent: v1 refuses multi-instance on `webService` (Q20) | written only when not null | `ProcessSchemaActivity.cs:555-567`; 0/66 carry it | traced, corpus |

The generic flow-node keys (`UId`, `A2` element name, `A3`/`A4` = process UId 66/66, `A5` package UId 66/66, `IL2`
= lane, `BL3` position, `BL8`, `BN2` = `69;55` 66/66) are written by the package's existing element path, as for
every other element; they are not web-service specific and the parity rule normalises them (8.2 W12). [corpus,
measured 2026-11-02]

Corpus element key sets (measured 2026-11-02, 66 elements): `BL1, UId, A2, A3, A4, A5, IL2, BL3, BL7, BL8, BN2,
BO2, BO3, BP2, IX1, IX2` (58), `+ IX3` (6), `+ IX4` (2). No element carries `BL11`, `BP6`, `BP1`, `BP4` or `BP7`.
Measured 2026-11-03: the current designer writes the same element keys (EM2, M-24: `IX3` 7000, `IX4` true, no
`BP6`), and writes `BL11:false` on the element itself while a required input is unbound (EM4 S2, M-26: `UsrWscId_In`
after the method change) and after it pruned an element whose method is gone (EM8, M-30). The package never writes
`IsValid` (`BL11`); a capture side's element `BL11:false` is AD10 (8.3).

### 1.2 Version and run-time notes on the element keys

| Key | Note | Tag; settled by |
|---|---|---|
| `IX4` | On a core without commit `5e53b027c9b` (< 8.3.2.3590: 8.1.3.6947, 8.3.1.4498 measured) the member is absent and a raw `IX4` key is read past and dropped on the next save. EM9 (2026-11-03): the package on `origin/main` already fails to compile on both of those cores for other reasons (CS1061/CS0117 on 8.3.1.4498, MSB3245 on 8.1.3.6947) and builds on 8.3.2.4199, so the direct `IsIgnoreErrors` reference adds no floor. Effective at run time only while feature `DisableIgnoreErrorsInWebServiceUserTask` is off (`WebServiceUserTaskImplementation`); the 7.8.0 page hides the checkbox under the same gate (`WebServiceUserTaskPropertiesPage`). | g2 (measured binaries); EM9 (measured, local builds); EM0 (feature state), EM7 (run time) |
| `IX3` | The element timeout schedules an actor cancel (`HttpWebRequestActor`); the METHOD's own timeout (service `IR3`) is the `HttpWebRequest.Timeout`. The page shows `Math.ceil(ms/1000)`, so a ms value that is not a multiple of 1000 does not round-trip through the designer; the builder only writes multiples of 1000, and describe reports a stored value as `ms / 1000`, a fraction, not rounded (Q8, design 2.5). Changing the service on the page resets the timeout to 0 (verdict r2-C2(4)). | traced; EM4 records what a method change keeps |
| `IX1`/`IX2` | Not validated on save by any rule; the effective check is the next design load (section 7). An empty `IX1` clears every parameter on load; a non-resolving `IX2` (wrong case included: `FindByName` is ordinal) prunes every tagged parameter. Measured 2026-11-03: a wrong-case `IX2` saves green, the next load shows only the result params, and the run fails with `ItemNotFoundException` (EM11, designer half); an unrelated modify through the current builder then deletes the 8 tagged params from the database (EM8, current-builder half). | traced (g4 §1); measured (EM8, EM11) |

## 2. Generated parameters (tagged)

### 2.1 Key set and fixed values on every level

| Key | Value | Evidence | Tag |
|---|---|---|---|
| `BL1` | `Terrasoft.Core.Process.ProcessSchemaParameter` (751/751, the client's DynamicProcessSchemaParameter serializes under the server type name too) | g1 §b | corpus |
| `UId` | fresh GUID per parameter, per level | `process-webservice-schema` `_getParameterConfig` | traced, corpus |
| `A2` | `<service param name>_In` (request) / `_Out` (response), at every level | `process-webservice-schema`, `:272`; 539/539 tagged (297 top-level + 242 nested) satisfy `A2 = L17 + suffix` (verdict r2-C3) | traced, corpus |
| `A3`, `A4` | `<process UId>` at every level (751/751) | client: element `createdInSchemaUId`; server provider: `BaseProcessSchema.UId`; equal on every shipped element (g1 §b) | traced, corpus |
| `IL2` | `<element UId>` at every level (297/297 top-level, 242/242 nested) | `process-webservice-schema` `_getParameterConfig` sets `containerUId: this.uId` recursively (g1 §b) | traced, corpus |
| `L1` | type map 2.2 | | |
| `L8` | `{}` when unbound; `{"GS2":"false","GS5":<P>}` for any Boolean (2.4); a binding otherwise (section 5) | | |
| `L6` | `true` only by the required rule 2.3; never written as `false` | `L6` appears only when true (g1 §b) | traced, corpus |
| `L17` | `<service param name>`; for the `ItemValue` child `<name>ItemValue` | `process-webservice-schema`, `:272`; verdict r2-C3 | traced, corpus |
| `L12` | `0` In / `1` Out, written on EVERY parameter (only `Variable` is the default) | g1 §b | corpus |
| `L18` | nested items, only on arrays (2.5) | | |

Measured key sets (g1 §b, 66 elements): top-level tagged `BL1 UId A2 A3 A4 IL2 L1 L8 L17 L12` (198), `+ L18` (59),
`+ L6` (38, +2 with `L18`); nested the same (218), `+ L18` (18), `+ L6` (6, all `ItemValue_In` children). On disk the
writer order is `BL1, UId, A2, A3, A4, IL2, L1, L8, L6, L17, L12, L18` (`IdentitySync`
`IdentitySynchronizationAddRights` `WebService2.clientId_In`). Absent on every WS parameter: `A5`, `BL11` (unless an
invalidation wrote `false`, section 7), `L2`-`L5`, `L9`, `L11` (IsValueSerializable default `true`), `L13`-`L16`,
`IsLazy`, `SourceParameterUId`; no caption key (captions go to resources, `process-schema-parameter`). [traced, corpus]

### 2.2 Type map (`L1`)

Rule, identical in the client and the server provider: `name = DataValueTypeName`, or `MaxSizeText` when blank; if
`IsArray`: `CompositeObjectList` when the name is `Object`, else `ObjectList`; resolve by name; unknown or `Text` ->
`MaxSizeText`. [traced: `process-webservice-schema`; `ProcessSchemaWebServiceParametersProvider`;
verdicts r3-C3, r8-C3]

| Service param (`IU2`, `IU3`) | `L1` | Corpus (top-level + nested tagged, g1/r2) |
|---|---|---|
| `Text`, blank, unknown | MaxSizeText `c0f04627-4620-4bc0-84e5-9419dc8516b1` (never Text `8b3f29bb`) | 362 |
| `Integer` | `6b6b74e2-820d-490e-a017-2b73d4ccf2b0` | 68 tagged (134 incl. StatusCode) |
| `Boolean` | `90b65bf8-0ffc-4141-8779-2420877af907` | 14 tagged (80 incl. Success) |
| `Float` | `57ee4c31-5ec4-45fa-b95d-3a2868aa89a8` | 12 |
| `Date` | `603d4960-a1a2-45e9-b232-206a54421b01` | 2 |
| `Guid` | `23018567-a13c-4320-8687-fd6f9e3699bd` | 1 |
| `DateTime`, `Time` | the DataValueType of that name (resolved by name; UIds not pinned) | 0 |
| any non-Object, `IU3 = true` | ObjectList `4b51a8b5-1ee9-4437-9d58-f35e083cbcdf` | 24 |
| `Object`, `IU3 = true` | CompositeObjectList `651ec16f-d140-46db-b9e2-825c985a8ac2` | 56 |
| `Object`, `IU3 = false` | Object `84ed6865-...` (client `CUSTOM_OBJECT`) | 0: no corpus service and no stand service declares one; the service designer flattens a single object into JSONPath scalars (2.6). The package does not write this row: it refuses such a parameter (design C3, §5.1) |

### 2.3 Required rule (`L6`)

`L6 = true` iff direction In AND service `IU7` true AND NOT (scalar array). Equivalent server form
`(!IsArray || type == Object) && IsRequired && In` (provider `:56-62`). [traced, verdict r3-C3]

| Case | `L6` | Evidence | Tag |
|---|---|---|---|
| required In scalar, required In object array | `true` | 40 top-level In | corpus |
| any Out parameter, even with `IU7 = true` | absent (`UsrResponse_Out` of a required `UsrResponse`) | verdict r9-S2(1) | corpus |
| required In scalar array (ObjectList parent) | absent | `process-webservice-schema` | traced; corpus 2 exceptions = legacy drift (`ProcessTests` `UsrRuntimeCollectionFormulas` `WebService3/4.UsrArray_In`) |
| its `ItemValue_In` child | `true` (child computed with `isArray: false`) | 6/6 nested `L6` are such children | traced, corpus |
| UrlSegment request param | service `IsRequired` is RECOMPUTED on read as `DefValue` empty (`ServiceParameter`), so `L6 = true` exactly when the segment has no default | r3 §2 | traced; measured (EM4 S2, M-26: `UsrWscGet` UrlSegment `UsrWscId`, Text, no default, path `/{UsrWscId}`, stored `UsrWscId_In` `L6 = true`; the required Query `UsrWscQ_In` `L6 = true` too) |

### 2.4 Value (`L8`) of an unbound generated parameter

| Parameter | `L8` | Evidence | Tag |
|---|---|---|---|
| any non-Boolean, unbound | `{}` (client `sourceValue: {source: None}`, None elided; no `GS5`) | 389/389 tagged Out empty or Boolean default (r6 §2) | corpus |
| any Boolean (top-level or nested, tagged or Success) | `{"GS2":"false","GS5":<P>}`, no `GS1` (= Source None with Value `"false"`) | tagged Boolean: 14/14 Out (3 top-level, 11 nested; e.g. `BingSearchProcess` `WebService1.VIFFriendly1_Out`, `ExecuteUpgradeBsf` `PutBuildsInQueue.PropertiesProperty_Out.PropertyInherited_Out`) [measured 2026-11-02]; Success 61/66 (5 ProcessTests elements `{}`) | corpus |
| Boolean In | `{"GS2":"false","GS5":<P>}`, as the Out ones | no corpus instance (no Boolean request param is called); EM2 (M-24): the unbound Body param `UsrWscFlag` (Boolean, not required) is stored as `UsrWscFlag_In` `L8 {"GS2":"false","GS5":<P>}` | measured 2026-11-03 (SQ5) |

The server writes `GS2` for a non-localizable type whenever Value is non-null, and `GS5` when non-empty
(`ProcessSchemaParameterValue.cs:459-470`), so the port sets `Value = "false"` AND `ModifiedInSchemaUId = <P>`
explicitly; leaving them unset yields `{}`. Where the client sets `"false"` was not pinned (`process-schema-parameter`
`convertDataValueByType` is the candidate) (g1 §b). [traced]

Service parameter DEFAULTS (`IU5`, Const or SysSetting) are never copied into the element: an unbound parameter is
`{}` and the run time skips it, so the service default reaches the wire (`WebServiceUserTaskImplementation.cs:53-55`
`ShouldSkip`; `ServiceParameterValueProvider`). Defaults can hold secrets (Bearer header,
basic-auth password, g5); the element bytes never carry them, and describe never reads them (Q14, Q16). [traced]
Measured 2026-11-03 for the designer (decisions Q14, Q16): on `GeminiApiSearch.GenerateContent` every input's `L8` is
empty, the API-key header `Xgoogapikey_In` included; on `UsrWscEchoService` the header `UsrWscProbe` (service `IU5
{IV1 "wsc-probe", IV2 1}`, Const) is stored as `UsrWscProbe_In` with an empty `L8`, and `wsc-probe` occurs nowhere in
the process metadata (M-19; the EM2 element, M-24, leaves it unbound the same way). What the run time sends for that
unbound header is not measured (needs an echo endpoint; E4).

### 2.5 Arrays: `L18`

| Service shape | Element shape | Evidence | Tag |
|---|---|---|---|
| scalar array `X` (`IU3`, not Object) | ObjectList `X_<dir>` with EXACTLY one child: `A2 = XItemValue_<dir>`, `L17 = XItemValue`, `L1` = the item's primitive type (MaxSizeText 13, Integer 10, Guid 1), no `L18`, `L6` per 2.3, caption `DefaultNestedParameterCaption` | `process-webservice-schema` (`Ext.merge({}, svcParam, {isArray:false, name: svcName+"ItemValue"})`); 24/24 ObjectList, `L17` 24/24 | traced, corpus |
| object array `X` with item properties `IU6` | CompositeObjectList `X_<dir>`, `L18` = one child per `IU6` item in service order, each built by the same rules recursively (names suffixed, tags raw, `IL2` = element, `A3`/`A4` = process) | `process-webservice-schema` `_getItemProperties` `:235-255`; 55/56 (the 1 without `L18` is the stale `UsrUpdateLabelsCopy_In`); 76/76 joinable `L18` sets equal the service `IU6` exactly (verdict r3-C3) | traced, corpus |
| object array inside an object array | nested CompositeObjectList with its own `L18`; corpus depth: CompositeObjectList nests 2 deep at most | Gemini `Candidates_Out > CParts_Out > PText_Out` | corpus |
| scalar array inside an object array | nested ObjectList with its `ItemValue` child; deepest corpus path 4 parameter levels / 3 collection levels | `Candidates_Out > GMGSupports_Out > GSGCIndices_Out (ObjectList) > GSGCIndicesItemValue_Out (Integer)` (`ActionGeminiSearch`) | corpus |
| array-in-array REQUEST (object array holding a nested array) | same bytes as above on the In side; corpus has none with a nested array on In (max request depth 2, r6 §6) | EM2 `UsrWscOrders_In` (nested `UsrWscLines_In`, `UsrWscTags_In` with its `ItemValue` child) | measured (EM2, M-24, SQ7); run time not measurable on stand `Creatio` under E4 (section 6 I15) |

The client builds `L18` only while client feature `ProcessParameterCollections` is on (or debug) (`process-webservice-schema`). The
port does not copy that gate (Q3, agreed 2026-11-03): it always writes `L18` and the `ItemValue` child. Basis: every
checked build from 7.13 to 10.2.931 ships `Feature-ProcessParameterCollections=true` in Web.config / WebHost config,
which the run-time re-keying reads; the client readers read DB rows only, so on a clean 8.2.1+ database the designer
emits flat composites (open bug ENG-68545 "No data binding exists for ProcessParameterCollections feature", decisions Q3). On stand `Creatio` the DB feature is on
(`AdminUnitFeatureState` state 1, r9 §4) and the client reads it as on (EM0, M-22), so the EM2 oracle is taken with
nesting on: it stores `L18` under the object arrays and the `ItemValue` children (M-24). [traced, measured on the
stand 2026-11-02 and 2026-11-03]

### 2.6 Request param kinds and flattened objects

| Service param kind (`IU4`) | Element param | Evidence | Tag |
|---|---|---|---|
| Body (1), UrlSegment (2), QueryString (3), HttpHeader (4), Cookie (5) | all identical: one In `X_In`; the kind is NOT stored on the element; the run time resolves it by `Tag` against the service schema | r2 §3.1; `WebServiceUserTaskImplementation.cs:31-51` | traced, corpus |
| header with a default (e.g. `x-goog-api-key` <- SysSetting `GeminiApiKey`) | `Xgoogapikey_In`, `L8 {}`; the default stays in the service | `ActionGeminiSearch` `WebService1.Xgoogapikey_In` | corpus |
| response scalar under a nested object, service path `$.usageMetadata.promptTokenCount` | flat top-level `UMPTCount_Out` (the service designer flattened it; the element sees a scalar) | `GeminiApiSearch` / `ActionGeminiSearch` | corpus |
| request field of a single (non-array) object | flattened by the service designer into a top-level scalar with a JSONPath `IU1`, as on the response side: element sees an ordinary `X_In` | `teamcitybpmonline_BSF` `Method3` Body param `BuildTypeId` `IU1 $.buildType.id` (`IU4` 1) -> `ExecuteUpgradeBsf` `PutBuildsInQueue.BuildTypeId_In`; also `MLWebServiceProxy.recommend` `$.predictionParams.*`, ProcessTests `UsrJiraRuntimeCollection` `UsrUpdateLabels` `$.update.labels` [measured 2026-11-02] | corpus; the wire JSON is SQ8 (not measurable on the stand under E4) |

### 2.7 Order in `BP2`

Fresh build (designer = port): `[In tagged, service request order][Out tagged, service response order][Success,
Response, StatusCode, RequestBody]`; `L18` children in service `IU6` order. The page's caption sort is UI only. A
parameter the service gains after the first sync is APPENDED at the end, after the result parameters, by both the
client (`process-webservice-schema`) and the server sync (`ProcessSchemaParameterCollection.cs:50-55`). [traced; g1 §a, g4 §4]

| Shape | Elements (g1 §a) |
|---|---|
| all tagged before the result params, result order S, R, SC, RB | 12 |
| same, S, R, SC (no RequestBody: an older save) | 45 |
| a tagged param after the result params (re-synced later) | 9 (2 product `GetMeetingTranscript` `WebService2/3`, 7 test) |
| result params' relative order S, R, SC[, RB] | 66/66 |

So the fresh order holds on 57/66, not 65/66 (correction 2). The current designer keeps it, request params in
service order whatever their kind (EM2, M-24; EM4 S2, M-26: header `UsrWscProbe_In`, URL segment `UsrWscId_In`, query
`UsrWscQ_In`, then the four result params; SQ2). The package emits the fresh order on create, and on a
modify that re-targets it regenerates everything (Q11), which yields the fresh order again; an append-at-end can only
come from the platform sync (section 7).

## 3. Result parameters and `BK15` rows

### 3.1 The four untagged parameters

| `A2` | `L1` | `L12` | `L8` | Run-time use | Corpus |
|---|---|---|---|---|---|
| `Success` | Boolean `90b65bf8-...` | 1 | `{"GS2":"false","GS5":<P>}` | `GetByName` in `FillResultParameters`: throws when missing; `200 <= code < 400` | 66/66 (L8 as shown 61/66) |
| `Response` | Text `8b3f29bb-ea14-4ce5-a5c5-293a929b6ba2` (not MaxSizeText) | 1 | `{}` | `GetByName`, raw body | 66/66 |
| `StatusCode` | Integer `6b6b74e2-...` | 1 | `{}` | `GetByName` | 66/66 |
| `RequestBody` | Text `8b3f29bb-...` | 0 | `{}` unbound; a binding otherwise (5.2 B6) | `FindByName` (optional); a NON-NULL value replaces the generated body entirely (`ServiceClientRequest`) | 14/66 (11/25 product): the current client always adds it, so its absence dates a save (verdict r6-C4) |

Key set `BL1 UId A2 A3 A4 IL2 L1 L8 L12`: no `L17`, no `L6`, no `L18` (212/212). `A3 = A4 = <P>`, `IL2 = <E>`. The
missing `L17` is load-bearing: the platform sync removes only TAGGED parameters it does not produce, so a tagged
result parameter would be deleted on the next load (section 7). The port writes all four, `RequestBody` included
(evidence `WebServiceUserTaskImplementation.cs:22-29, 109-115`; verdicts r3-C4, r2-C6). [traced, corpus]

### 3.2 `BK15` (`ProcessSchemaMapping`) rows

One row per result parameter, created by the CLIENT only (`parametrized-process-schema-element` `_updateMapping` -> `createMappingInfo`
`:128-137, :480-499`, reached through `_synchronizeResultParameters`); the server never writes them; no reader at
run time or in the server design-time sync depends on them (g1 §c). Byte parity only; the package writes them (Q4,
agreed 2026-11-03), the first rows it authors itself. [traced]

Measured on stand `Creatio` 2026-11-03 (decisions Q4, process `UsrWscBk15Probe`): a designer-built element gets
exactly 4 rows with this section's key set (`A3 = A4 = <P>`, no `GT4`, Success `GT1 {"GS2":"false"}` without `GS5`);
the designer's own copy/paste of it gets the same 12 params and no rows; a reload, a properties-page open, a move and a
re-save add no row to the copy and leave the original's 4 rows unchanged; the row-less element runs to Completed. So a
row-less element is a state the designer produces, keeps and runs; the rows remain byte parity only. EM2 (M-24)
measured the same 4 rows on `UsrWscEcho`, `GT1` of the unset `RequestBody` `{}` (SQ4); EM4 (M-26) measured that a
method or a service change re-creates the 4 rows with new row UIds and the SAME `GT5` values (W10 removes `GT5`); a
designer save of an element whose method is gone deletes all of them (EM8, M-30).

| Field | Value | Measured (212 rows, 66 elements, g1 §c) |
|---|---|---|
| `BL1` | `Terrasoft.Core.Process.ProcessSchemaMapping` | 212/212 |
| `UId` | fresh GUID | - |
| `A2` | the element NAME at creation time; the package does not rewrite it on a rename either (Q4) | 202 current; 10 stale after a rename (CrtBase rows say `WebService2` for `GetMarketplaceAppPublishedVersionWebService`) |
| `A3`, `A4` | `<P>` (the client treats a row whose `A3` is not the process UId as an inherited-element marker, BPS `initMappings` `:722-745`) | 212/212 |
| `GT2` | `[Element:{<E>}].[Parameter:{<result param UId>}]` | 212/212 |
| `GT3` | the result parameter's UId | 212/212 |
| `GT4` | ABSENT (no source schema; `sourceSchemaUId` undefined for a web service) | 212/212 |
| `GT5` | a random GUID (the UId of the client's seeded metadata entry), not any `BP2` UId | 212/212; 190 distinct |
| `GT1` | Success `{"GS2":"false"}` (L8 without `GS5`: the client clones before stamping); Response, StatusCode `{}`; RequestBody = its `L8` exactly, `GS5` included | 61 / 66 / 66 / 14 of 14 |
| key set | `BL1 UId A2 A3 A4 GT2 GT3 GT5 GT1` | 212/212 |
| order | the `BP2` result-param order | 66/66 |
| count | 3 or 4 = the number of result params | 52 x 3, 14 x 4 |

Writing them in the package (g1 §c): set each parameter's `SourceValue` FIRST, then add the row with a SEPARATE
value object for `GT1` (Success `{Value:"false"}` without `ModifiedInSchemaUId`). In the other order the platform
`SourceValue` setter aliases the row's `Source` to the parameter's value (`ProcessSchemaParameter.cs:450-458`) and
writes `GS5` into Success `GT1`. A later `RequestBody` binding through `ProcessMappingService.Commit` assigns
`SourceValue`, so the setter keeps `GT1 == L8` for free, which is Gemini's stored shape. The package's
`removeElement` already deletes every row whose `TargetMetaPath` contains the element UId
(`ProcessGraphBuilder`). A designer re-save leaves designer-written rows as they are (Q4 probe);
it neither duplicates nor rewrites PACKAGE-written rows either (SQ13, EM5 (measured: passed on the experimental cut 2026-11-04 and in the final-cut campaign 2026-11-09, M-37; a server load drops the top-level caption suffix and a designer save puts it back)). [measured]

Tagged parameters (297 top-level, 242 nested) never get a row; neither does any user binding. [corpus]

## 4. Captions in resources

Captions, display values and localizable constant values live in `Resources/<Process>.Process/resource.<culture>.xml`
(`SysLocalizableValue` on a stand), never in `BP2`. [traced, corpus]

| Key | Value | Evidence | Tag |
|---|---|---|---|
| `BaseElements.<El>.Caption` | designer: `generateItemUniqueName(<service caption> + " ")` -> `<service caption> <N>`; kept on a method change, renamed on a service change | `WebServiceUserTaskPropertiesPage`; "JiraRuntimeCollection 1", "InvalidUrlService 1"; Bing's is `https://api.bing.microsoft.com 1`; EM2 "WSC echo service 1" (M-24), EM4 S3 "WSC drift service 1" (M-26) | traced, corpus, measured (SQ12) |
| `BaseElements.<El>.Parameters.<A2>.Caption` | clone of the service parameter caption (all cultures the service has) | `process-webservice-schema` | traced, corpus |
| nested: `BaseElements.<El>.Parameters.<A2>.<A2 child>...Caption` | path joined by `.` from parameter NAMES, e.g. `...Parameters.Candidates_Out.GMGSupports_Out.GSGCIndices_Out.GSGCIndicesItemValue_Out.Caption` | `resource.en-US.xml`; 508/509 top-level and 242/242 nested have a row (g1 §b) | corpus |
| `ItemValue` child | `DefaultNestedParameterCaption` = `Item value` (18; `Значение элемента` 6: the designing user's culture written under en-US) | g1 §b | corpus |
| suffix | ` - Input` / ` - Output` appended in every culture ONLY when the same caption occurs more than once among the TOP-LEVEL parameters across the method's request and response; a nested item never gets it and is not counted | `process-webservice-schema` (`filteredParameters.getCount() > 1`); verdict r2-C5(3); EM2 (M-24) | traced; measured for a fresh save (SQ3); the corpus stored form is inconsistent (below) |
| Success / Response / StatusCode / RequestBody | `Success`, `Response body`, `Http status code`, `Request body` (`Terrasoft.Nui.resx`) | 57 en-US + 9 Russian rows in en-US files | traced, corpus |
| bound parameter | `.DisplayValue` (112 rows) and, for a constant on a localizable type, `.Value` (14) | g1 §b; 5.2 B3 | corpus |

The suffix as stored [measured 2026-11-02, corpus resources]: five rows carry it (`ProcessTests`
`CallingWebServiceUnderPortalUserQuerySysSettings` and `UsrTestRuntimePlainArray` `UsrArray_In`/`_Out` = `Array -
Input`/`Array - Output`; `StudioFreeUpdate` `ExecuteUpgradeBsf` nested
`WebService2.LastChangesChange_Out.ChangeId_Out` = `Id - Output`). Two product elements with a duplicate top-level
caption across In and Out carry NO suffix: CrtBase `MarketplaceAppId_In`/`_Out` (both `Marketplace application ID`,
while a downstream display value elsewhere still says `Marketplace application ID - Output`, r2 §5) and
`ExecuteUpgradeBsf` `WebService2.Id_In`/`Id_Out` (both `Id`) next to the suffixed nested `ChangeId_Out`. Hypothesis
[traced, not measured]: a later server-side save ran the platform sync, whose `SynchronizeParameter` overwrites
`Caption.Value` of every MATCHED top-level parameter with the unsuffixed service caption, and never touches nested
items (verdict r3-C7(c)). If true, the suffix is designer-correct but not stable on top-level parameters, and the
comparison rule must tolerate both forms there (8.3 AD1). EM2 settled the fresh form (M-24, SQ3): on `UsrWscEcho` the
top-level pair `UsrWscStatusIn_In` / `UsrWscStatus_Out` is "Status - Input" / "Status - Output", while the nested
request item `UsrWscOrders_In.UsrWscOrdNo_In` and the top-level response `UsrWscEchoText_Out` both stay plain
"Reference": the current designer counts TOP-LEVEL parameters only, so the corpus's nested `Id - Output` comes from an
older designer. Its cost (EM4 S1, M-27): the designer's formula window resolves an output by caption and bound the
nested INPUT "Reference" instead of the output. A server load strips the suffix and a designer save puts it back, so the stored caption follows the last writer (SQ13, EM5 measured 2026-11-04; AD1).

Culture: the client writes captions as plain strings, i.e. in the designing user's culture; the package writes
localizable values in the request thread's culture (precedent ENG-95985 Send email attachments, serialization 2.3). Parity compares the
authoring culture only (8.3 AD4). [traced, inferred]

Display values the package writes for bindings come from the existing mapping service. On 1.6.6.125 it stored the
bare caption for a TOP-LEVEL source, where the designer stores `[#<element caption>.<param caption>#]` (ENG-95985
serialization 2.2 AD1, `ProcessMappingService`); the designer form for a web-service input is
corpus, e.g. `[#Put build in queue.Id#]` (`ExecuteUpgradeBsf` `WebService2.Id_In`), `[#GetArray.Array:Item value#]`
(item). The current designer's forms are measured (M-24, M-25): a process-parameter source `[#PText#]`, a formula
`[#PCount#] + 1`, an element output `[#WSC drift service 1.UsrWscOrd2#]`, its nested items joined by `:`
(`[#WSC drift service 1.UsrWscOrd2:UsrWscOrd2Lines:UsrWscOrd2Sku#]`). The package's form on `da719366` is re-checked
in source before TC-P-05; until then it is allowed difference AD3 (resources are compared on the stand only).
[traced on an older cut, corpus, measured]

## 5. Binding shapes

### 5.1 What the mapping engine writes

Every parameter -> parameter binding the package writes is `{"GS1":3, "GS2":"[#<meta path>#]", "GS5":<P>}`, with
`<meta path>` = `M[Parameter:{<uid>}]` for a process parameter and `M[Element:{<container>}].[Parameter:{<uid>}]` for
an element parameter at any depth (a nested item is addressed by its OWN UId, no collection segment), `M` =
`[IsOwnerSchema:false].[IsSchema:false].` (g3 M6; `ProcessMappingService`;
`ProcessSchemaParameter`). Formulas, constants and record columns run no
collection rule (g3 M2). `Tag` is read nowhere on the mapping path (g3 §4). The rules need no change for the three
corpus composite shapes (g3 §5). [traced]

### 5.2 Per source kind, with corpus exemplars

`<P>` process UId, `<E>` this element, `<S>` a source element, `<p>` a process parameter.

| # | Source kind | Target | Stored `L8` (designer) | Corpus exemplar | Package today | Tag |
|---|---|---|---|---|---|---|
| B1 | process parameter, whole token | scalar In | `{GS1:3, GS2:"[#M[Parameter:{<p>}]#]", GS5:<P>}` | `IdentitySync` `IdentitySynchronizationAddRights` `WebService2.clientId_In` (also `L6: true`) | same bytes (M6) | corpus, traced |
| B2 | system setting token / formula with tokens | scalar In | `{GS1:3, GS2:"<formula as typed>", GS5:<P>}` | CrtBase `CheckMarketplaceAppNeedUpdateSubProcess`: `CompatibilityVersion_In` = `[#SysSettings.ConfigurationVersion<String>#]`; `FileName_In` = `[#M[Parameter:{...}]#] ? ([#...EntityColumn...#] + ".zip") : ""` | `expression` route, written as sent (M2) | corpus, traced |
| B3 | constant on a text (MaxSizeText) input | scalar In | `{GS1:1, GS5:<P>}`, NO `GS2`; the literal is in resources `...Parameters.<A2>.Value` and `.DisplayValue` | `DepTest_Level3` `DepTestProcess` `Parameter1_In` (`111`), `BusinessRulesTest` `UsrBPWithInvalidWebService` `UsrParameter1_In` (`test value`), `StudioFreeUpdate` `CanUpdateBSF` `Timeout_In` (`0`); 13 such bindings, 0 non-empty `GS2` constants on any WS input [measured 2026-11-02]; EM2 `UsrWscStatusIn_In` <- `open`, `UsrWscOrders_In.UsrWscOrdNo_In` <- `A-1` (M-24) | existing constant path; parity EM2 | corpus; measured (EM2, SQ10) |
| B3b | constant on a non-text input (Integer, Boolean, ...) | scalar In | `{GS1:1, GS2:"<literal>", GS5:<P>}`: `GS2` in metadata (non-localizable type, `ProcessSchemaParameterValue.cs:459-470`) | nested `UsrRuntimeCollectionFormulas` `WebService1.UsrLabelsAdd_In` = Script `10.ToString()`; EM4 S1 (M-24): the Integer item `UsrWscIdsItemValue_In` <- constant 1 is `{"GS1":1,"GS2":"1","GS5":<P>}` | existing constant path | traced, measured (EM4 S1) |
| B4 | element output, whole token | scalar In | `{GS1:3, GS2:"[#M[Element:{<S>}].[Parameter:{<o>}]#]", GS5:<P>}` | `ExecuteUpgradeBsf` `WebService2.Id_In` <- `PutBuildsInQueue.Id_Out` (`L6: true`); `GetMeetingTranscript` `WebService3.MeetingId_In` <- `WebService2.MeetingId_Out` | same bytes (M6) | corpus, traced |
| B4b | element output entity column (3 segments) | scalar In | `...[Element:{<S>}].[Parameter:{<o>}].[EntityColumn:{<c>}]#]` | CrtBase `MarketplaceAppId_In` <- ReadData `ResultEntity` column | existing record-column path | corpus |
| B5 | item-level into an In collection, parent unbound | `X_In.Y_In` / `X_In.XItemValue_In` | parent `{}`; item `{GS1:3, GS2:"[#M[Parameter:{<p>}]#]", GS5:<P>}` | `ExecuteUpgradeBsf` `PutBuildsInQueue.PropertiesProperty_In.PropertyValue_In` (CompositeObjectList); `IdentitySynchronizationAddRights` `WebService2.roles_In.rolesItemValue_In` (ObjectList); EM2 `UsrWscIds_In.UsrWscIdsItemValue_In` <- formula, `UsrWscOrders_In.UsrWscOrdNo_In` <- constant, both containers `{}` (M-24) | accepted as written, parent untouched (g3 §2) | corpus, traced, measured (EM2, SQ10) |
| B6 | formula into `RequestBody` | `RequestBody` | `{GS1:3, GS2:"string.Format(\"{{\\\"contents\\\":...}}\", ([#M[Parameter:{<p>}]#]).Replace(...))", GS5:<P>}`; `BK15` `GT1` = the same object | CrtGeminiApiIntegraion `ActionGeminiSearch` `WebService1.RequestBody` (2/14 bound in the corpus) | `expression` route; `GT1` follows via the setter (3.2) | corpus, traced |
| B7 | collection -> collection (P1) | In collection from another element's collection | parent `{GS1:3, GS2:"[#M[Element:{<S>}].[Parameter:{<coll>}]#]", GS5:<P>}`; items read the source row | `ProcessTests` `UsrTestJiraReadDataRuntimeCollection` `WebService2/5.UsrUpdateLabels_In` <- ReadData `ResultCompositeObjectList`, item `UsrLabelsAdd_In` = a formula over row items (test only, pattern C of r6 §5) | a single item->item reference plans the parent binding by P1 (same bytes); a FORMULA item runs no rule, so the caller maps the parent explicitly (g3 §3) | corpus, traced |
| B8 | WS Out composite -> declared process Collection, two levels | process `Candidates` (Out), item `ContentParts` (Collection), its item `Text` (LongText `5ca35f10-...`) | every level `{GS1:3, GS2:"[#M[Element:{<E>}].[Parameter:{<level UId>}]#]", GS5:<P>}`: `Candidates` <- `Candidates_Out`, `ContentParts` <- `CParts_Out`, `Text` <- `PText_Out`; process items carry no `IL2`, no `L17`, no `L12` (Variable); item names differ from the element's, the link is by UId | `ActionGeminiSearch` process parameter `Candidates` (r9 S5 confirmed); EM3 route (b) (M-25): `UsrWscEm3CaptureB` `Orders {OrdNo, Lines {Sku}}` on `UsrWscDriftService.UsrWscOrders2`, the declared collection type 651ec16f, process items with only `UId A2 L1 L8`; the designer binds item-first, and binding an item binds the container and the nested collection | declare the Collection, map the leaf; P1 binds both parents innermost first (g3 §4) | corpus, traced, measured (EM3, SQ11); run time EM6 only with a JSON source of that shape, which E4 lacks |
| B9 | unbound | any In | `{}`; service default applies at run time | 24 product In (r6 §4.1) | nothing written | corpus |

Shapes the builder does NOT write in v1 (describe reports them as stored):

| Shape | Origin | Why not | Tag |
|---|---|---|---|
| designer "create parameter from element outputs": process CompositeObject `WebService1Results1`, `L17 = SourceElement:<el>`, flat items `<el><param>` tagged `<el>.<param>` | generic page `ProcessSchemaPropertiesPage.js:401-419, 978-1060`; Bing (32 items, unbound), Customer360 Gemini (41 item bindings). Current designer (EM3, designer half, 2026-11-03, `GeminiApiSearch.GenerateContent`, only `Candidates` ticked): wrapper `WebService1Results1` (Tag `SourceElement:WebService1`, no value) holding a Collection `WebService1Candidates_Out` (Tag `WebService1.Candidates_Out`, bound to the element output) whose items are only the first-level scalars, each bound to the element item; the nested lists are not offered and not created | CompositeObject is not declarable and P1 refuses CompositeObject <- CompositeObjectList (g3 §1); ENG-92730 Complex / structured parameter types (Q6) | corpus, traced, measured |
| `typeFromElement` mirror of a WS CompositeObjectList output | `CloneItemProperties` refuses item Tags that are not GUIDs; 194/194 CompositeObjectList items are non-GUID (verdict r4-C7) | Q6: ENG-92730 follow-up | traced, corpus |
| mirror of a WS ObjectList output | accepted today as an unbound ObjectList with no items; a described `ObjectList` fed back through `type` is refused (verdict r4-C8) | Q6 | traced |
| COL item <- ObjectList item; ObjectList item <- COL item; source deeper than target | type or depth refusal (g3 M4, M5) | 2 test processes only | traced |
| response leaf at the third collection level (`...GSGCIndices_Out.GSGCIndicesItemValue_Out`) into a declared parameter | declared collections stop at 2 levels and ObjectList is not declarable | Q6 | traced |

## 6. Invariants the package must keep

| # | Invariant | What breaks otherwise | Evidence | Tag |
|---|---|---|---|---|
| I1 | `A2` = `L17` + `_In`/`_Out` exactly, every level | the platform sync matches top-level params by NAME (ordinal); a mismatch is removed (tagged, not produced) and re-added flat with a new UId: its binding and every downstream reference are lost | g4 §1 step 4-5; verdict r3-C7 | traced |
| I2 | `L17` = the service parameter name exactly (`ItemValue` child: `<name>ItemValue`) | the run time keys requests and responses by Tag, skips an In with an empty Tag, and re-keys composite items Name <-> Tag; the sync never repairs a wrong Tag on a matched param | `WebServiceUserTaskImplementation.cs:31-109`; verdict r3-C7 | traced |
| I3 | every array parameter carries its full `L18` (and the scalar array its `ItemValue` child) | a composite without `L18` sends items rendered from service defaults/`null` and RECEIVES N empty items; the sync never adds `L18` | verdict r3-C6; `CompositeObjectListUtilities.cs:17-26` | traced; the port always writes `L18` (Q3); EM6's response read needs a JSON source with an object array, which E4 lacks |
| I4 | Success, Response, StatusCode present, untagged, types as 3.1 | missing: `ItemNotFoundException` at execution, after the HTTP call (not at save); tagged: removed by the next sync | `WebServiceUserTaskImplementation.cs:109-115`; verdict r2-C6(5) | traced |
| I5 | `A3 = A4 = <P>`, `IL2 = <E>`, every level, set explicitly | parity; client "dynamic" detection; a nested collection back-fills a stamp only when it already has a `ParentMetaSchema` (ENG-95985 serialization I4) | g1 §b; 751/751 | traced, corpus |
| I6 | `L1`, `L6`, IsValueSerializable (`true`) exactly as the provider maps them | the sync overwrites a matched top-level param's type and required flag on every load (nested ones keep the wrong value forever) | g4 §5; verdict r3-C7(c) | traced |
| I7 | every Boolean `L8 = {"GS2":"false","GS5":<P>}` | parity only (the 5 test elements without it run; AD7 in the corpus sweep) | 2.4 | corpus |
| I8 | `IX1` non-empty and resolving; `IX2` the canonical, ordinally matching method name (Q9) | empty `IX1`: the next load clears ALL params incl. result params and `BK15` rows; non-resolving or wrong-case `IX2`: every tagged param is pruned on load; a designer save then deletes every param of the element, result params included, and its `BK15` rows and marks the element `BL11:false`, while the readers keep dangling tokens with no flag (M-30) | g4 §1, §5; g5 | traced; measured (EM11 designer half, EM8, 1.2) |
| I9 | the four result params carry no `L17`; no other untagged param is added | untagged params survive every sync; an extra untagged param would never be removed | g4 §1 step 6 | traced |
| I10 | one `BK15` row per result param (Q4): fields of 3.2, `GT1` a separate object, no `GT4`; `A2` not rewritten on a rename | parity only (a row-less element saves, survives a re-save and runs, Q4 probe); a row with `A3` != `<P>` makes the client treat the element as inherited | g1 §c | traced; measured (Q4 probe) |
| I11 | captions only in resources, keyed by parameter NAMES; names unique per level | resource keys collide; names are what the keys embed | 4 | traced, corpus |
| I12 | nested Names (request) and Tags (response) unique across the WHOLE tree of one top-level param | `GetParameterKeysMap` flattens all levels into one `Dictionary.Add`: a duplicate throws `ArgumentException` at run time; the service enforces unique names per collection only (`ServiceParameterCollection.cs:42`) | g3 §6; r3 §4 | traced; notice N9 and a describe issue (design C3, §5.2, §5.3); run-time throw not reproduced |
| I13 | `RequestBody` unbound = `{}` (never an empty constant) | a non-null `RequestBody` replaces the generated body; whether an unbound Text reads null at run time is not measured | `ServiceClientRequest.cs:241-252`; r3 §10 Q3 | traced; EM6's request side is not measurable under E4, so this rests on the core unit tests |
| I14 | `IX3` a multiple of 1000; `IX4` written directly, only when `ignoreErrors: true` | the page rounds ms up to whole seconds; `IX4` has no other byte rule: the package does not compile below about 8.3.2 anyway (EM9), so the direct reference adds no floor | 1.2 | traced; measured (EM9) |
| I15 | an inner CompositeObjectList of an In array with no source of its own while its leaves are mapped gets the Q13 warning notice (N5) | before the ENG-55920 Unable to use array inside array in web-service business process element fix (absent in 10.1.37 = the stand, 10.0.0.950, 8.3.4.2753; present in 10.1.830 and 10.2.931, first fixed 10.1 build not bisected), that inner list is sent without a value (null); a mapped inner container is sent; that such a process saves without error is not measured (verdict r1-C7). A sourceless nested ObjectList is not covered by the fix either (traced, r1-C6) and is outside the N5 predicate | g2 §c; verdict r1-C7 | traced, measured binaries; EM12 is not measurable on stand `Creatio` under E4 and rests on the core unit tests and the ENG-55920 comment |

## 7. What the platform re-sync does

`ProcessSchemaWebService.SynchronizeParameters` runs on every design-time load (`FindDesignItem`, the 6-argument
`GetItemFromMetaData` incl. its cache hits), so on every `modify` and every designer open; not on a `MetaItems` hit of
`FindInstanceFromMetaData` (describe's path for an interpretable process) and not for a compiled instance. It has no
gate and no setter trigger. One sync: empty `IX1` -> `ClearParameters()`; provider returns empty when the service or
method does not resolve; otherwise it generates FLAT `<n>_In`/`<n>_Out` (`Tag = n`, `Source = None`, no `L18`, fresh
UId), matches by `Parameters.FindByName` (ordinal), updates matched ones in place, appends misses at the end, removes
unmatched TAGGED ones after `InvalidateDependentElements`. [traced; g4 §0-1] Describe reads the synced view: the
EM11 element was described without the tagged params it still stored (measured 2026-11-03, decisions Q9, Q14). A
service save that removes a method writes nothing to the processes that call it and does not evict a process already
in the cache: a warm cache keeps describing the pre-change set, a process first loaded after the save describes
pruned, with no issue (EM8, M-29).

| Stored element | Effect of one sync, then a save | Tag |
|---|---|---|
| correct shape (sections 2-3) | no parameter added, removed or reordered; UIds, `L8`, `L18`, result params and `BK15` rows kept; `L1`/`L6`/IsValueSerializable/`L12` rewritten to equal values (no byte change). Top-level `Caption.Value` overwritten with the service caption: a no-op EXCEPT where the designer added the ` - Input`/` - Output` suffix (4, SQ3) | traced; EM5 |
| top-level name not `<tag>_<dir>` | removed if tagged (dependants `BL11: false` where a parametrized element's `SourceValue` names it; FormulaTask/flow references are left dangling with no flag) and re-added flat with a new UId at the end | traced (g4 §3) |
| wrong `L17` on a matched name | kept as is: run-time failure, not repaired | traced |
| array param without `L18` | stays without `L18` (sync never builds it); a service param missing by name is re-added WITHOUT `L18` | traced (g4 §3) |
| wrong `L1` / `L6` on a top-level param | silently retyped / re-flagged; nested ones never corrected | traced |
| result param carrying a Tag | removed (tagged, not produced) | traced |
| `IX2` wrong case or deleted method | every tagged param removed, result params kept; e.g. `GetMeetingTranscript` (product): an unrelated modify makes `WebService3`/`WebService4` (methods missing) lose all 7 tagged params, and `WebService2` (method `UsrOnlineMeetings` present, no response params: drift) lose `MeetingId_Out`: 8 in total (g4 §3). Measured 2026-11-03 on `UsrWscBk15Probe.WebService2` (wrong-case `IX2`): one unrelated `addParameter` through the current builder answered with no warning and deleted the 4 tagged `_In` and 4 tagged `_Out` params from the database; result params, `RequestBody` and `IX2` kept. A DESIGNER save of such an element (`UsrWscEm8Drift3`, M-30) deletes more: every parameter, result params and `RequestBody` included, all its `BK15` rows and their caption resources; the element becomes `BL11:false`, `IX1` / `IX2` stay, readers' tokens dangle with no flag | traced; measured (EM11 designer half, EM8) |
| empty `IX1` | everything removed, result params and their `BK15` rows included | traced |
| service gained a param | appended flat at the end, after the result params; its UId changes on every unsaved rebuild | traced; corpus 9/66 order |
| nested drift (service `IU6` changed) | invisible to the sync, which never touches `L18`: the only drift describe can see on every path (Q12) | traced |

Consequences for the bytes, not the design (the design file owns the notices): the generator's output must be a
fixed point of the sync on its top-level fields (I1, I2, I6, I9); the parity rule tolerates the top-level caption
suffix both ways, as EM5 measured it flips with the last writer (AD1); a comparator must not require "result params last" for a capture that was
re-synced (8.2 W9).

## 8. Parity method (AC5)

### 8.1 Oracles

| Oracle | What it covers | Pattern |
|---|---|---|
| EM2 designer capture A on stand `Creatio` (EM1's `UsrWscEchoService`; `UsrWscEm2CaptureA.WebService1`, measured 2026-11-03, M-24) | THE AC5 oracle: current designer, composite request body (object array with a nested object array and a nested scalar array, required scalar, required scalar array, flattened object fields, header with a non-secret default), timeout, ignore errors, the request source kinds B1, B2, B3, B5 of 5.2 (B4, an element output, needs a second element and comes from the corpus pairs), `RequestBody` unset, `BK15`, resources | B: committed redacted (see below) |
| EM3 designer capture B | B8 shape (hand-made Collection with item mappings) and the designer's mirror shape and Tags; both measured 2026-11-03: the mirror on `UsrWscEm3Probe` (M-15, 5.2), the hand-made Collection on `UsrWscEm3CaptureB` over `UsrWscDriftService.UsrWscOrders2` (M-25) | B: committed redacted |
| EM4 designer capture C | what a method change and a service change clear and keep (Q11), measured 2026-11-03 on `UsrWscEm4MethodChange` S1-S3 (M-26, M-27; SQ14) | B: committed redacted |
| corpus pair 1: CrtBase `CheckMarketplaceAppNeedUpdateSubProcess` / `CreatioMarketplaceApi` | flat request and response, Query params with Const/SysSetting defaults, B2 formulas, B4b entity column, `IX3` 600000, 4 result params, 4 rows with a STALE `A2` (`WebService2`) | A: committed verbatim |
| corpus pair 2: CrtGeminiApiIntegraion `ActionGeminiSearch` / `GeminiApiSearch` | composite response 4 levels incl. ObjectList + `ItemValue`, header defaults, B6 `RequestBody` formula with `GT1 == L8`, B8 process Collection | A |
| corpus pair 3: StudioFreeUpdate `ExecuteUpgradeBsf` / `teamcitybpmonline_BSF` | composite REQUEST (`PropertiesProperty_In`, B5 item-level), nested Boolean default, B4 element output, 2 elements, NO `RequestBody` (2020 save), nested caption suffix | A |
| platform writer JSON | the package's own parameter written by `WriteMetaData` and read back, diffed against a quoted corpus parameter | C (precedent `FileProcessParameterTests` `WriteMetaData`/`ReadBack`/`Diff`) |

The three pairs are drift-free on tags against their committed service (the local research notes lists none of
them) and keep the fresh `BP2` order. [measured 2026-11-02]

Corpus fixtures to commit (pattern A), byte for byte, under a new `WebService` with its own
`PROVENANCE.md` (SHA-256 as the LAST cell of each row, the layout the existing oracle's hash reader expects) and a
`.gitattributes` `-text` rule for the folder in the same commit. AC8 / Q16 govern builder OUTPUT (describe, refusals,
notices, logs, test snapshots of builder output), not these files: they are copied verbatim from the shipped
PackageStore corpus into the internal package repository and may carry the shipped services' non-credential `IU5`
defaults. They are not redacted (the SHA pins); TC-P-08 asserts that no committed file carries a credential (no `IQ5`
value with `IV2` 1, no Const `Authorization` default). Measured 2026-11-02 in the PackageStore checkout:

| File (shipped corpus, `branches/7.8.0`) | Bytes | SHA-256 | First bytes | Saved (`ModifiedOnUtc`) |
|---|---|---|---|---|
| `CrtBase/.../CheckMarketplaceAppNeedUpdateSubProcess/metadata.json` (process `b08d57c4-b1f4-4a1e-8146-b528e7fff3de`) | 109,079 | `d79f889800f948e017e725055a673dc87ebb36b85dfbbf5a295a855c94e92358` | `7b 0d 0a` (no BOM, CRLF) | 2023-07-06 |
| `CrtBase/.../CreatioMarketplaceApi/metadata.json` (service `1ee7f166-3184-47d5-8164-43ffee35dec9`) | 4,355 | `a06df74e0d9e54ecac35a52db74b1211f247f9f94f166f48960e5066df36eb18` | `ef bb bf` (BOM) | 2023-07-04 |
| `CrtGeminiApiIntegraion/.../ActionGeminiSearch/metadata.json` (process `318ab439-7fb3-4d4f-af13-7af957af3dfd`) | 31,704 | `ce76f30e64728241eacbb723dfdeeb4bfffa380ded5b057cfd1262f4b4924772` | `7b 0d 0a` | 2026-08-28 |
| `CrtGeminiApiIntegraion/.../GeminiApiSearch/metadata.json` (service `a78ca5e9-6b76-4037-82f0-142e47662c44`) | 13,279 | `85b797388b354fe7f94aa1436569199b86e7adf023f3981e42f9c9f0b8694766` | `ef bb bf` | 2026-08-28 |
| `StudioFreeUpdate/.../ExecuteUpgradeBsf/metadata.json` (process `88a56860-671c-4eaf-8956-27507b38ad3c`) | 100,336 | `ae3b9d32a194c659039e51556d48da1867ffd7a94025e3b0120489f154a1cef9` | `7b 0d 0a` | 2020-03-06 |
| `StudioFreeUpdate/.../teamcitybpmonline_BSF/metadata.json` (service `e13c9ba7-15df-4495-825f-572665f1ae6e`) | 36,553 | `75bd4318f266f7e706c85f7d09a036a8b68105b777894bf40540bcd9fea5c187` | `ef bb bf` | 2020-02-20 |

About 295 KB. Process captures have no BOM, service captures have one: the oracle reader strips a BOM before parsing
and hashes the raw bytes. The service captures are the generator's INPUT for the parity test (the package builds the
element from the same service the capture was made from); r8 B6 decides whether `MetaDataTestUtils` can deserialize
them into a `ServiceSchema` or the test builds the service in code from the same file. Pair 2 exists on stand `Creatio` too:
process `ActionGeminiSearch` (r9 §3.2; `BingSearchProcess` is the other stand process with this element) and service
`GeminiApiSearch` (r9 §2). An optional read-only RB-1 of the process and RB-3 of the service
([measurements](eng-92710-call-web-service-element-measurements.md) 3.3), diffed against the PackageStore capture,
shows whether the stand copy differs; it is not yet a step of EM0. Resource files of the corpus pairs are not
committed: their resources are compared on the stand only (W13).

Pattern B storage (EM2, EM3, EM4; EM5 is a re-save diff, not a fixture): the reads are committed REDACTED under
`Stand`, not byte-verbatim. The stand address is replaced by `<stand>` before saving
(measurements rule 11): the service `IQ1` and any value that carries it, such as a designer default element caption
`<base URI> <N>` (r9 §3.2). `PROVENANCE.md` records, per file, the capture date, the platform and package versions
from EM0, the redacted keys and the SHA-256 of the committed (redacted) bytes. Under WSCR the redacted keys are
excluded from comparison (W14); every other byte is compared as for pattern A.

### 8.2 The web-service comparison rule (WSCR)

Sides: a designer save (EM2/EM3/EM4 or a corpus pair) and a package build of the same configuration with the same
element name, against the same service. Each step says what may differ; anything else fails.

| # | Step | Detail |
|---|---|---|
| W1 | Pairing | elements by `A2`; parameters by `A2` path from the top level (`Candidates_Out/CParts_Out/PText_Out`); a parameter on one side only fails |
| W2 | `UId` | removed at every level (parameters, rows) |
| W3 | `A3`, `A4`, `GS5` | role-replaced: the side's process UId -> `<process>`; any other value kept, so it shows |
| W4 | `IL2` | role-replaced: the element UId -> `<element>`; any other value kept |
| W5 | `GS2` | every `[Element:{g}]` -> `<element A2>` and every `[Parameter:{g}]` -> its `A2` path, resolved in that side's own metadata; an unresolvable GUID kept as written; the remaining text compared exactly |
| W6 | Key sets | exact per level after W2: tagged `{BL1, A2, A3, A4, IL2, L1, L8, L17, L12}` + `L6` when required + `L18` when array; result `{BL1, A2, A3, A4, IL2, L1, L8, L12}`; bound `L8` `{GS1, GS2, GS5}`, text constant `{GS1, GS5}`, Boolean default `{GS2, GS5}`. An extra `A5`, `BL11`, `L6:false`, `L11`, `GS4` on either side fails |
| W7 | Exact values | `BL1`, `A2`, `L1`, `L6`, `L12`, `L17`, `GS1`, `GS2` (after W5), `L18` length; element `BL1`, `BL7`, `BO2`, `BO3`, `IX1`, `IX2`, `IX3`, `IX4` |
| W8 | "no value" | `{}` only; NOT equal to `{"GS5": ...}` (the designer writes `{}`; unlike ENG-95985 R6 there is no tolerated variant until a capture shows one) |
| W9 | Order | `BP2` order and `L18` order compared exactly for a fresh build; for a capture whose tagged params appear after the result params (re-synced, 2.7) only the relative order within each group is compared |
| W10 | `BK15` | rows of the element paired by `GT3` role (the result param's `A2`); `GT5` removed (random); `A2` compared against the element NAME on the package side and as AD2 on a corpus side; `GT1` compared exactly after W3; key set exact (no `GT4`); the package side writes all rows (Q4). A row-less element (the designer's copy/paste, Q4 probe) is not used as an oracle |
| W11 | Result params | all four on the package side; on a capture side `RequestBody` and its row may be absent (AD5) |
| W12 | Generic element keys | `A5`, lane `IL2`, `BL3`, `BL8`, `BN2` excluded (layout and package placement, not web-service bytes) |
| W13 | Resources (stand only, EM2/EM3) | keys of section 4 paired by key; values exact in the authoring culture; AD1, AD3, AD4 apply |
| W14 | Redacted keys (pattern B only) | every key `PROVENANCE.md` lists as redacted (`<stand>`: the service `IQ1`, a caption carrying the base URI) excluded on both sides; a `<stand>` token anywhere else fails |

### 8.3 Named allowed differences (candidates; each is removed or confirmed by the EM named)

| # | Difference | Why tolerated | Removed or confirmed by |
|---|---|---|---|
| AD1 | top-level caption with vs without the ` - Input`/` - Output` suffix | stored corpus is inconsistent (4); a fresh designer save suffixes a top-level pair (EM2, M-24), so the builder writes it | confirmed for corpus sides by EM2; confirmed for stand captures by EM5 (2026-11-04: every server load of the process drops the suffix, every designer save restores it). Owner's decision 2026-11-04: excluded from the oracle, permanent; the builder keeps writing the designer's suffixed form |
| AD2 | `BK15` `A2` = an older element name on a corpus side | the client never renames rows (10/212 stale); the package writes the name at creation and does not rewrite it either (Q4) | none: permanent for corpus pairs |
| AD3 | `.DisplayValue` of a top-level source: bare caption vs `[#<element>.<param>#]` | existing mapping-service behaviour (4); the designer forms are measured (EM2, EM3, M-24, M-25) | removed if the source re-check on `da719366` shows the package writes the designer form |
| AD4 | resource culture (Russian text under en-US on a corpus side) | the designer writes the user's culture | none: compare the authoring culture (the stand captures are en-US, M-22) |
| AD5 | `RequestBody` and its row absent on a capture side | older saves predate it (ExecuteUpgradeBsf 2020); the current client always adds it | confirmed for corpus sides only: EM2 shows `RequestBody` and its row present (M-24) |
| AD6 | dropped (EM0, M-22) | `DisableIgnoreErrorsInWebServiceUserTask` is off on the stand and EM2 stores `IX4` true (M-24); no committed pair sets ignore errors | - |
| AD7 | Success `L8` `{}` instead of `{"GS2":"false","GS5":<process>}` on a capture side | 5 ProcessTests elements store it (61/66 carry the Boolean default, 2.4) | none: corpus sweep only, in no committed pair |
| AD8 | a stale CompositeObjectList without `L18` on a capture side | `UsrUpdateLabelsCopy_In` (55/56 carry `L18`, 2.5) | none: corpus sweep only |
| AD9 | a parameter type drifted from its service on a capture side | `UpdateBsfNotification` `Chatid`: Text in the service, Integer in the process (484/486 type pairs match, verdict r6-C3) | none: corpus sweep only |
| AD10 | element `BL11:false` on a capture side | the designer marks an element invalid while a required input is unbound: the EM4 S2 element after the method change (`UsrWscId_In` unbound, M-26), the EM2 S2 and S4 elements (a required input unbound, M-33, M-35); the package never writes `IsValid` | none: TC-P-12 (EM4 S2), TC-P-15 (EM2 S2), TC-P-16 (EM2 S4) |

AD1-AD5 and AD10 may apply to a committed test; AD7-AD9 apply only to the corpus sweep (test plan 3.1), whose
committed pairs need W10, AD2 and AD5 only.

### 8.4 Tests per pattern (names carry no ticket key; ids are the test plan's)

| Fixture (package tests) | Pattern | Oracle |
|---|---|---|
| the existing `FileProcessingCaptureOracle`, extended (fixture folder as a parameter; `L1`, `L6`, `L12`, `L17`, recursive `L18`; BK15 rows; roles), not forked; hash test TC-P-01 | A, B | `PROVENANCE.md` pins of 8.1 |
| `WebServiceParityTests`: one test per corpus pair (element built from the paired service capture under WSCR; TC-P-01..04, 06..08) | A | 8.1 pairs |
| `WebServiceParityTests`, stand rows: EM2 element, EM3 Collection read, EM4 re-target, EM5 re-save (TC-P-09..13; TC-P-05 is the stand-side caption diff) | B | EM2-EM4 redacted captures under `Fixtures/WebService/Stand/` (W14) |
| `WebServiceSerializationTests`: `WriteMetaData` of a generated composite param and of the four result params + rows, diffed against quoted corpus entries | C | `ActionGeminiSearch` `Candidates_Out`, `ExecuteUpgradeBsf` `PropertiesProperty_In`, CrtBase rows |
| `WebServiceParameterShaperTests`: type map, required rule, `ItemValue`, recursion, suffix, order (pure, no platform mocks) | - | sections 2-4 |
| `WebServicePlatformProbeTests`: real platform sync on a generated element (provider bound by reflection, verdict r8-C2) is a fixed point (I1, I2, I6, I9) | probe | section 7 |
| clio e2e: stored bytes + resource rows of a built element on stand `Creatio` | stand | EM2; floor = the cut claimed in the claim comment |

## 9. Open bytes and their measured answers (status 2026-11-03)

| # | Question | Why it matters | Settled by | Answer |
|---|---|---|---|---|
| SQ1 | Does the current designer write the corpus key sets (no `A5`, `BL11`, `L11`, `GS4` on params; row key set without `GT4`)? | W6 is built on 2020-2026 corpus saves | Settled by EM2 (M-24) | yes: every expected key set matched; the element's own `BL11:false` while a required input is unbound is AD10 (EM4 S2, M-26) |
| SQ2 | Fresh `BP2` order with mixed Header/Body/Query request params: service request order or grouped by kind? | W9 | Settled by EM2 (M-24) and EM4 S2 (M-26) | service request order: header, URL segment, query as declared, then the four result params (2.7) |
| SQ3 | Caption suffix: does it apply across nested items too (`ChangeId_Out` = `Id - Output` suggests yes), and does a server-side save strip it from top-level params? | generator rule and AD1 | Settled for a fresh save by EM2 (M-24); the re-save half by EM5 (2026-11-04) | top-level parameters only: "Status - Input" / "Status - Output", while the nested `UsrWscOrdNo_In` and the top-level `UsrWscEchoText_Out` stay plain "Reference" (4); a server load strips it and a designer save puts it back (SQ13) |
| SQ4 | Four `BK15` rows on a current designer save; `GT1` of an unset `RequestBody` = `{}` | Q4, W10 | Settled by the Q4 probe (M-13) and EM2 (M-24) | 4 rows in `BP2` result order, Success `GT1 {"GS2":"false"}`, the others `{}` (3.2) |
| SQ5 | Boolean In default: `{"GS2":"false","GS5":<P>}` like the Out ones? | I7 on the In side | Settled by EM2 (M-24) | yes: `UsrWscFlag_In` `L8 {"GS2":"false","GS5":<P>}` (2.4) |
| SQ6 | Required scalar array: parent without `L6`, `ItemValue_In` child with `L6: true` on a current save | 2.3 | Settled by EM2 (M-24) | yes: `UsrWscIds_In` none, `UsrWscIdsItemValue_In` `L6` true; the optional `UsrWscTagsItemValue_In` none |
| SQ7 | Object array holding a nested object array and a nested scalar array on the In side: same recursion as on Out? | 2.5 for request bodies (AC1) | Settled by EM2 (M-24) | yes: `UsrWscOrders_In` carries `L18` with the nested `UsrWscLines_In` and `UsrWscTags_In` with its `ItemValue` child (2.5) |
| SQ8 | Does the request body builder render a nested JSON object from dotted `IU1` paths of flattened request fields (the element side is measured flat, 2.6)? | AC1 composite body via flattened fields | not measurable on stand `Creatio` under E4 (EM6's request side); the core unit tests (r3 §4) | open: element shape as in 2.6; wire form unknown |
| SQ9 | Header param with a non-secret default: element `L8 {}`, nothing of the default in metadata or resources | Q16 | Settled for the metadata by EM1 (M-19) and EM2 (M-24); describe by EM2's read-back on the final cut | empty `L8`, `wsc-probe` nowhere in the process metadata (2.4); TC-P-05 checks the committed resources, TC-I-07 the reply |
| SQ10 | Item-level binding into an In collection with the parent unset (B5): does the current designer leave the parent `{}`? Constant (B3) and formula (B2) forms on a current save | 5.2 | Settled by EM2 (M-24) and EM4 S1 | parent `{}`; a Text constant `{GS1:1, GS5:<P>}` with the value in `.Value` / `.DisplayValue` only; an Integer constant `{GS1:1, GS2:"1", GS5:<P>}`; a formula `GS1` 3 with the text in `GS2` (5.2 B2, B3, B3b, B5) |
| SQ11 | Response read into a Collection: designer mirror ("create parameter from element") type, item Tags (`<el>.<param>`?), and a hand-made Collection's bindings on all levels | Q6, B8 | Settled by EM3 route (a) (M-15) and route (b) on `UsrWscDriftService.UsrWscOrders2` (M-25) | the mirror is a CompositeObject wrapper with Tags `<el>.<param>` and first-level items only; a declared Collection is bound item-first, every level `GS1` 3 with an element-parameter token, items without Tag (5.2 B8) |
| SQ12 | Element caption `<service caption> <N>` on a current save; does the builder set it (design) | parity of `BaseElements.<El>.Caption` | Settled by EM2 (M-24) and EM4 (M-26) | "WSC echo service 1"; kept on a method change, "WSC drift service 1" after a service change; the builder follows it (4) |
| SQ13 | Designer re-save of a builder element (with and without `BK15` rows, with Success `L8`): rows duplicated or rewritten, Success rewritten, `RequestBody` added, captions re-suffixed, order changed? | the residual risk of Q3/Q4 | EM5 | settled: no rows duplicated or rewritten, Success kept, the element node byte-identical after a re-save; only the top-level caption suffix follows the last writer (experimental cut 2026-11-04; final-cut campaign 2026-11-09, M-37) |
| SQ14 | Method change in the designer: params and rows regenerated with fresh UIds; result params re-created; `IX3` kept or reset; downstream references flagged `BL11`? | Q11 replace semantics | Settled by EM4 (M-26, decisions Q11) | method and service change both replace every parameter with fresh UIds, result params included; the 4 `BK15` rows are re-created (new row UIds, same `GT5`); `IX3` kept on a method change and reset on a service change, `IX4` kept; dependents are NOT flagged `BL11` and keep the removed UIds; the builder keeps the result params' UIds (Q11 A3) |

EM6-EM12 measure run time, compile floor, dependencies and drift; they settle no stored-bytes question beyond I3,
I13, I15 and the `IX2` canonicalisation. EM9 and the designer half of EM11 are done (1.2); under E4 the run-time
halves of I3, I13 and I15 (EM6's request side, EM12) are not measurable on stand `Creatio`.

## 10. Corrections to the research

| # | Note and claim | What the data or source says |
|---|---|---|
| 1 | r6 §4.1 and §8: `{"GS1":1}` with no `GS2` on a WS input is an empty-string constant (`CanUpdateBSF` `Timeout_In` x2) | it is a constant on a LOCALIZABLE (text) parameter, whose literal lives in resources `.Value`/`.DisplayValue`: `CanUpdateBSF` `GetUpdates`/`GetMessages` `Timeout_In` = `0`, `DepTestProcess` `Parameter1_In` = `111`, `UsrBPWithInvalidWebService` `UsrParameter1_In` = `test value` [measured 2026-11-02, corpus resources]. The two product cases are bound constants, not empty strings |
| 2 | r3 §3.2 table: result params after service params in 65/66 elements | 57/66; 9 elements have a tagged param after the result params (g1 §a) |
| 3 | r6 §2 / verdict r6-C2: `IX4` is an orphan key no core writes | true only of the stale older core checkout; `IX4` is read and written by core from 8.3.2.3590 (g2) |
| 4 | r9 §3.4: describe on 1.6.6.131 omits `L17` and nested `L18` | wrong; verdict r9-S6 holds: the package's raw `DescribeProcess` returns each output's `tag` and its nested `itemProperties` (re-measured 2026-11-03, decisions Q14, down to three levels). It returns bound inputs only (unbound tagged inputs and an unbound `RequestBody` are omitted, M-31) and no service, method, timeout or ignore-errors member. A reply without tags or nesting seen the same day came from an older MCP clio build |
| 5 | r6 §2 table: Success `L8 {"GS2":"false"}` on 66/66 | 61/66, full form `{"GS2":"false","GS5":<P>}`; and it is not specific to Success: every Boolean WS parameter carries it, 14/14 tagged Boolean outputs including nested ones [measured 2026-11-02] |
| 6 | r2 §3 and §5: the caption suffix is the designer's rule | it is conditional (verdict r2-C5(3)), and two product captures with a duplicate top-level caption store no suffix while a nested parameter does (section 4); a builder that always or never suffixes fails parity somewhere |
| 7 | r9 §2.2: the stand offers no composite request body | confirmed (r9-S4); hence EM1 gave `UsrWscEchoService` the POST method `UsrWscEcho` that declares one (M-23), and EM2 is the only AC5 oracle for AC1's composite request body (M-24) |
