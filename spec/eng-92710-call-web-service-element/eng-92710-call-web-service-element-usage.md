# Usage: ENG-92710 Call web service element

Written 2026-11-02, updated 2026-11-03. Nothing was built, saved, called or run for the 2026-11-02
rows. Rows marked "measured 2026-11-03" come from the owner-approved stand probes of that day, recorded in full in
[decisions](eng-92710-call-web-service-element-decisions.md). All decisions it links to were agreed by the owner on
2026-11-03.

**Summary.** How and why the Call web service element is used: what the product documentation promises (section 1),
what the shipped corpus actually does with it (section 2), what stand `Creatio` holds (section 3), and what that means
for the builder's scope and for the guidance agents get (section 4). The usage picture is small and simple: 25
product elements, all REST, no SOAP call and no multi-instance anywhere; request values are almost always `[#..#]`
tokens or formulas; composite request bodies are rare and bound at item level; composite responses are common, deep
and mostly ignored; the error outputs are almost never read. That bounds what v1 must build well (scalars, tokens,
raw `RequestBody`, the full output tree) and what it can leave to ENG-92730 "Complex / structured parameter types".

Sibling documents: [README](README.md) · [facts](eng-92710-call-web-service-element-facts.md) ·
[reuse](eng-92710-call-web-service-element-reuse.md) ·
[serialization](eng-92710-call-web-service-element-serialization.md) ·
[design](eng-92710-call-web-service-element-design.md) ·
[decisions](eng-92710-call-web-service-element-decisions.md) ·
[measurements](eng-92710-call-web-service-element-measurements.md) ·
[plan](eng-92710-call-web-service-element-plan.md) · [test plan](eng-92710-call-web-service-element-test-plan.md) ·
[estimate](eng-92710-call-web-service-element-estimate.md).

**Basis.** Creatio Academy and community pages read 2026-11-02; the PackageStore corpus (CrtProcessDesigner 7.8.0
branch rule, below) scanned 2026-11-02; core TSBpm trunk and `<core>` `468c4c71b` (8.3.3.1722) for
traces; crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131) for what describe returns today;
a read-only inventory of stand `Creatio` (Creatio 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131) taken
2026-11-02. Research notes r6 (corpus), r7 (Academy), r9 (stand), g2-g5 (gap notes) in
`<research notes, local>/` (local), cited as rN/gN §section. Where the adversarial fact-check
(`_workflow-result.json`, "verdicts") corrected a note, the correction is used here and the claim id is cited
(e.g. r6-C5).

**Tags.** [doc] Academy or community text, not measured. [measured: corpus] counted over shipped metadata on
2026-11-02. [measured: stand] observed on stand `Creatio` on 2026-11-02, read-only, or on 2026-11-03 where the row
says so. [traced] read in source; for run time a hypothesis, and the EM that settles it is named. [inferred] my
reading of the cited facts. Where a row names EM6 for request bytes on the wire, or EM12: decisions Q17 chose no echo
endpoint (EP option E4), so these are not measurable on stand `Creatio` and rest on the core unit tests and the
ENG-55920 "Unable to use array inside array in web-service business process element" comment.

---

## 1. Academy: what the element is for and what it promises

### 1.1 Pages read

| Id | Page | Version label | URL |
|---|---|---|---|
| A1 | Call web service process element | 10 (8.0-8.3 selectable) | https://academy.creatio.com/docs/8.x/no-code-customization/bpm-tools/process-elements-reference/system-actions/call-web-service-process-element |
| A2 | [Call web service] process element (old) | 7.17/7.18 | https://academy.creatio.com/docs/user/bpm_tools/process_elements_reference/system_actions/call_web_service |
| A3 | Set up the REST web service integration | 10 | https://academy.creatio.com/docs/8.x/no-code-customization/base-integrations/web-services/set-up-the-rest-web-service-integration |
| A4 | Set up the REST web service integration | 8.1 | https://academy.creatio.com/docs/8.x/no-code-customization/8.1/base-integrations/web-services/set-up-the-rest-web-service-integration |
| A5 | Set up the SOAP web service integration | 8.0 | https://academy.creatio.com/guides/no-code-customization/8.0/base-integrations/web-services/set-up-the-soap-web-service-integration |
| A6 | Web service authentication | 10 | https://academy.creatio.com/guides/no-code-customization/base-integrations/web-services/web-service-authentication |
| A7 | Getting started with low-code web service integration | 10 | https://academy.creatio.com/guides/no-code-customization/base-integrations/web-services/getting-started-with-low-code-web-service-integration |
| A8 | Update currency exchange rates with web service integration (worked example) | 10 | https://academy.creatio.com/documents?id=2364 |
| A10 | Process collections | 10 | https://academy.creatio.com/guides/no-code-customization/bpm-tools/business-process-setup/process-collections |
| A11 | 7.17.4 release notes | 7.17.4 | https://academy.creatio.com/guides/resources/release-notes/7174-release-notes |
| A12 | REST setup, 7.12 administration docs (origin of the feature) | 7.12 | https://academy.creatio.com/documents/administration/7-12/integration-web-services |
| C1-C4 | Community threads: raw request body from 7.17.4; raw response body; plain string arrays; element error stops the process | 2018-2025 | r7 §1 |

Most current pages carry 7.16-7.18 screenshots unchanged; no Freedom UI variant of the element is documented
(r7 §5) [doc].

### 1.2 Purpose and integration scenarios

- **A no-code call of an integration that already exists.** The element only references a service configured in
  the Studio "Web services" section and one of its methods; creating the service is a separate setup (A1, A3)
  [doc]. The designer offers no other route: its service list has no "new service" entry, and the button next to the
  field opens the selected service for editing (measured 2026-11-03, decisions Q10). Confirmed in source and data: the element stores only `IX1` ServiceSchemaUId, `IX2` MethodName and `IX3`
  Timeout, plus `IX4` IsIgnoreErrors on core 8.3.2.3590+ (r7-A1, g2) [traced] [measured: corpus].
- **Scenarios the documentation shows**: pull reference data on a schedule (the Fixer exchange-rate example feeding
  currency rates from a timer process, A7/A8); push Creatio data out (a batch contact insert through an
  object-collection Body parameter, A1); chain several methods, passing outputs to inputs (A12 7.12) [doc].
- **Consuming a response collection**: A10 names five ways (pass it to another collection parameter, Script task,
  pre-configured page, user task code, Sub-process one instance per item, the preferred one) [doc].

### 1.3 What the element exposes, as documented

| Aspect | Academy statement | Correction from source/data |
|---|---|---|
| Service / method | "Which service to call?" lists configured REST and SOAP integrations; "Which method to call?" lists its methods (A1) | The picker reads `VwWebServiceV2` with no type or active filter (g5 §a) [traced] |
| Timeout | "Maximum execution time, in seconds"; the limit includes retries; at expiry the outputs show the last response (A1) | Stored as milliseconds in `IX3` (designer saves seconds x 1000); 0 or absent = no element limit, only the method's per-request timeout applies (r7-A3) [traced] [measured: corpus, all stored values are multiples of 1000] |
| Request parameters | Appear after the method is chosen; value entered manually or mapped (A1) | Tagged In params `<name>_In`; arrays become collections with nested items (serialization file) [traced] [measured: corpus] |
| Unset request parameters | "Still sent (as empty)" (A1 Fixer example) | Traced the other way: an In param with no source on itself and on its items is skipped and the service default reaches the wire (r6 §4.1, `ShouldSkip`) [traced, not measured; EM6] |
| Response parameters | Each response parameter of the method becomes an outgoing parameter; collections visible in advanced mode (A1) | Full nested tree (`L18`) on designer saves with `ProcessParameterCollections` on (55/56 CompositeObjectList, 24/24 ObjectList; the designer builds `L18` only while that client feature is on) (r6 §2) [measured: corpus] [traced] |
| Error outputs | "Success" (code < 400) and "Http status code" (A1); a worked example branches on `Success == true/false` (A8) | Success is `200 <= code < 400`; run-time names `Success`, `StatusCode`, `Response` (r7-A2) [traced] |
| Raw bodies | "Request body" in advanced mode from 7.17.4 (A11, C1); "Response body" read as `[#<element>.Response body#]` (C2) | Untagged `RequestBody` (Text, In; replaces the generated body when non-null) and `Response` (Text, Out) (r7-A5) [traced] |
| Array literals | "Array values can't be set ... use a Script task" (A3/A4/A5) | The sentence sits in the response-parameter table, not in a request rule; a one-item request collection from item-level values is supported and used in product (r7-A5) [doc] [traced] [measured: corpus] |
| Tree-shaped responses | Only top-level nested parameters are mappable downstream (A1, about Sub-process) | The builder's mapping engine walks dotted item paths at any depth; declared collections stop at 2 levels (g3 §4) [traced] |
| Object type | Object implies Is array for request params (A4) | The service designer refuses a non-array Object; single objects are flattened into JSONPath scalars; 57/57 Object service params are arrays, 0 CompositeObject element params (r1-C8) [traced] [measured: corpus] |
| Authentication | Basic (login in a Text setting, password in an Encrypted setting, not carried by packages) or OAuth 2.0 (A6) | Literal basic-auth passwords and `Bearer ...` header defaults also occur in service metadata (g5 §c) [measured: corpus] |

Not found on the Academy by r7: when composite (Object / Is array) parameters appeared (present in 7.16 docs), OpenAPI
import, multipart, file transfer, pagination. Whether the current A1 page documents the "ignore errors" option of
ENG-50229 "Possibility to continue execution of the business process when Web-Service element fails" was not checked (open; g2 has the platform facts).

### 1.4 Version history

| Version | Change | Source |
|---|---|---|
| 7.12 | Studio REST web services section + Call web service element; several elements per process with parameter passing | A12 [doc] |
| 7.17.4 | Raw "Request body" parameter (JSON / XML / plain text) in advanced mode; SOAP setup by WSDL URL | A11, C1 [doc] |
| 7.18.0 | "Send a test request" from the method page | A3 [doc] |
| 8.0.1 | XML content type for REST methods | A3 [doc] |
| 8.0.0.4926 and later saves | Element carries `RequestBody` (14/66 corpus elements; older saves have only Success/Response/StatusCode) | r9-S3 [measured: corpus] |
| 8.3.2 (from 8.3.2.3590) | `IsIgnoreErrors` (`IX4`): continue the process when the call fails (ENG-50229 "Possibility to continue execution of the business process when Web-Service element fails"); gated by feature `DisableIgnoreErrorsInWebServiceUserTask` | g2 [traced] |
| 10.1 (ENG-55920, core#1119; absent in 10.1.37, present in 10.1.830 and 10.2.931, first fixed 10.1 build not bisected) | An inner object array whose container has no source of its own while its leaves are mapped reaches the wire; before the fix it is sent without that inner list (null). A mapped inner container was always sent; that such a process saves without error is not measured (EM12) | r1-C7, g2 §c [traced] |

### 1.5 What users fall back to

The documentation and the community push the hard cases to a **Script task**: array values (A3-A5), parsing a raw
JSON response (C2), plain scalar arrays ("an array of single-key objects is sent", C3, 2022-2024, unanswered) and
error tolerance (C4, 2025-04: an element error stops the process, which predates 8.3.2) [doc]. The corpus shows the
same fallbacks in product code (section 2.4): composite outputs read in C# scripts, a raw JSON body assembled with
`string.Format` in `RequestBody`. C3 is partly overtaken: scalar arrays are `ObjectList` params with one
`<Name>ItemValue_In` item, used in product (IdentitySync `roles_In`); the wire shape of such a body is not measured
(EM6) [measured: corpus] [traced].

---

## 2. Corpus: how the shipped processes use it

### 2.1 Scope and counts

Branch rule: one branch per package, `branches/7.8.0` when present, else the highest branch or trunk; test split by
`is_test()` copied from `spec/sub-process-decomposition/corpus-scan/analyze.py:8-13` (regex
`test|demo|sandbox|sample|example|^DevPkg$` plus `EXTRA_TEST`, `KEEP={'RealtyDevelopers'}`). Scan scripts and raw
output: `<research notes, local>/scripts/` (`scan_ws.py`, `product.txt`, `test.txt`, `agg.txt`,
`crosscheck.txt`) (r6 §0) [measured: corpus].

| | Product | Test | Total |
|---|---|---|---|
| Process schemas with a `ProcessSchemaWebService` element | 19 (3.1% of 615) | 19 | 38 (all in `branches/7.8.0`) |
| Distinct process families (version copies merged, `BK8`/`IJ9`) | 15 | 18 | 33 |
| Elements | 25 (21 distinct) | 41 (40 distinct) | 66 |
| Packages | 8 | 3 (ProcessTests 16 schemas, BusinessRulesTest 2, DepTest_Level3 1) | 11 |
| Services / service.method pairs called | 10 / 19 | 11 / 20 | |
| Service type of every called method | REST 25/25 | REST 41/41 | SOAP called by 0 |
| Multi-instance (`BP6`) | 0 | 0 | 0 |
| Legacy `WebServiceUserTask` (e7acb2e5) references | 0 | 0 | 0 |

Two caveats (r6-C5): StudioFreeUpdate is internal release automation that passes the
filter; it holds 9 of the 19 product schemas and 13 of the 25 product elements. Without it, the customer-facing
product usage is **10 schemas, 12 elements in 7 packages**. And 4 product schemas are version copies (UpdateBSF
1/2/3; ActionGeminiSearchCrtCustomer360AI is version 1 of ActionGeminiSearch), so a parity fixture set must
de-duplicate them.

Service inventory behind the calls (r6 §6): 12 product service schemas / 22 methods (REST 12; auth None 9, Basic 2,
OAuth 2.0 1; GET 11, POST 7, PUT 2, DELETE 2; JSON 22/22); 20 test schemas / 41 methods (REST 16, SOAP 4). Max
request depth 2, max response depth 3 service levels (4 element levels with the synthetic `ItemValue`) in both
[measured: corpus].

### 2.2 Product processes

Request binding kinds: **P** whole-token process parameter, **E** whole-token element output, **E.col** element output
entity column, **F** formula, **S** system setting token, **D** no value (service default applies), **RB** raw
`RequestBody` formula. "Composite" = a CompositeObjectList or ObjectList element parameter.

| Package | Process (elements) | Service.method | Purpose | Request bindings | Composite request | Composite response | Response consumed | Error handling |
|---|---|---|---|---|---|---|---|---|
| CrtBase | CheckMarketplaceAppNeedUpdateSubProcess (1; `IX3` 600000) | CreatioMarketplaceApi.GetAppPublishedVersion (GET) | Ask Marketplace for an installed app's published version | E.col 1, F 2 (ternaries), S 1 | no | no (4 Text) | Scalars to process params and to the user task `UpdateInstalledApp` | Conditional flow `![#Success#] \|\| ...` to a Terminate end ("Response unsuccessful or application not found in marketplace"); the only product branch on `Success` |
| CrtCustomer360AI | ActionGeminiSearchCrtCustomer360AI (1; version 1 of ActionGeminiSearch below) | GeminiApiSearch.GenerateContent (POST; service in CrtGeminiApiIntegraion) | AI-skill action: grounding search | D 2 (API-key header from a sys setting, `Content-Type` const), RB 1 | no; raw JSON | yes (`Candidates` 3 service levels + `ItemValue`, `UMPTDetails`) | 10 outputs incl. `Success`, `StatusCode`, `Response` copied to process params (the action's outputs); designer grouping param `WebService1Results1` (CompositeObject, 41 items) | Exposed to the caller, not branched |
| CrtCustomer360AI | BingSearchProcess (1; caption = base URI + " 1", the designer default) | BingSearchApiCall.Search (GET) | AI-skill action: web search | P 1 (`query_In`, required), D 1 (subscription-key header) | no | yes (`WPValue` 22 items with nested `VDLinks`, `MItems`) | Scalars, `Success`, `StatusCode`, `Response` to process params; unbound grouping param `WebService1Results1` (32 items) | Exposed to the caller, not branched |
| CrtGeminiApiIntegraion | ActionGeminiSearch (1) | GeminiApiSearch.GenerateContent (POST) | AI-skill action: grounding search | D 2, RB 1 | no; raw JSON | yes | `Candidates_Out` into a process Collection `Candidates` with item mappings (`ContentParts` <- `CParts_Out`, `ContentParts.Text` <- `PText_Out`, names differ, bound by UId) | none |
| CrtKnowledgeManagementTools | ConvertArticleToMarkdownFormat (1) | ConvertHtmltoMarkdown.HtmlToMarkdown (POST, external tool) | Convert a KB article from HTML to Markdown | P 1 | no | no | Raw `Response` written back by Modify data | none |
| CrtMsTeamsMeetingTranscript | GetMeetingTranscript (3 chained) | UsrMsGraph.UsrOnlineMeetings / GetTranscripts / GetTranscriptContent (GET; the last two do not exist in the service) | Find a meeting, list transcripts, download content | P, F (`"JoinWebUrl eq '" + [#..#] + "'"`), E (output of call 1), D (header defaults) | no | no (the service declares no response params) | Call 2 takes call 1's output; raw `Response` of call 3 to Add data; formula tasks read outputs | none |
| IdentitySync | IdentitySynchronizationAddRights, ...DeleteRights, ...DeleteAllRights (3) | IdentityClientUser.CreateClientUser (PUT) / RemoveClientUser / RemoveAllClientUser (DELETE); OAuth 2.0 | Push user-role changes to the identity service | P 6 (one of them an item), D 1 | AddRights: `roles_In` ObjectList, container unbound, item `rolesItemValue_In` <- process param (one-item list) | no | none (fire and forget) | none |
| MkpNotifications | MkpMarketplaceCreatioWebService (1; older save, no `RequestBody`) | MkpServiceUpdatedApp.MkpGetUpdatedApp (GET) | Check Marketplace for a new app version, notify | P 3 | no | no | `StatusCode` copied to a process param | Formula task + gateway on that status code |
| StudioFreeUpdate (internal) | CanUpdateBSF (2; `IX3` 1000000), CheckUpdatePlanB (2), ExecuteUpgradeBsf (2), GetLastRelease (1), UpdateBSF + 3 version copies (4), UpdateBsfNotification (2) | 3 internal services, `teamcitybpmonline_BSF` among them (Basic auth from system settings) | Internal release automation | P, F, E, D; 2 constants (`{"GS1":1}`, value 0 in resources, on `Timeout_In`) | ExecuteUpgradeBsf `PutBuildsInQueue`: `PropertiesProperty_In` (CompositeObjectList), container unbound, item `PropertyValue_In` <- process param | yes on 9 of 13 elements | C# scripts read outputs by name (`Get<ICompositeObjectList<ICompositeObject>>("GetUpdates.Result_Out")`, then `"MessageDate_Out"`); conditions on response fields (`State_Out == "finished"`, `string.IsNullOrEmpty(Number_Out)`); outputs passed to sub-processes; most calls not read | Branches on response values only, never on `Success` |

Sources: `scripts/product.txt`, r6 §4, r6-C5, r9-S3, g3 §1, g5 §c [measured: corpus].

### 2.3 Request bindings, counted

69 tagged In params on product elements, nested included (r6 §4.1) [measured: corpus]:

| Binding (from `L8`) | Count | Example |
|---|---|---|
| Whole-token process parameter `[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{uid}]#]` (`GS1=3`) | 25 | IdentitySync `clientId_In` |
| No value: the service default applies | 24 | Gemini API-key header (default = sys setting `GeminiApiKey`) |
| Formula (`GS1=3`) | 9 | `cond ? a + ".zip" : ""` |
| Whole-token element output `[#..[Element:{uid}].[Parameter:{uid}]#]` | 7 | MS Graph call 2 <- call 1 |
| Element output entity column | 1 | CrtBase reads a Read data column |
| System setting token `[#SysSettings.ConfigurationVersion<String>#]` | 1 | CrtBase |
| Constant (`GS1=1`; literal in resources `.Value`) | 2 (CanUpdateBSF `Timeout_In` = 0, x2) | StudioFreeUpdate |

No product WS parameter uses `GS1=2` (Mapping). The two constants carry no `GS2`: the literal `0` is in
`BaseElements.<El>.Parameters.Timeout_In.Value`/`.DisplayValue` of `CanUpdateBSF.Process/resource.en-US.xml`
(r6 §4.1 read them as empty strings; corrected, see serialization correction 1) [measured: corpus resources]. Of the 24 top-level product In params that r6's `agg.py` counted as having
no value and that resolve to a live method (the two constants are among them), 20 have a service default (Const 7, SysSetting 13); credentials and API keys live in service defaults bound to system
settings, not in the process (r6 §4.1) [measured: corpus].

### 2.4 Patterns

| Pattern | Where | Weight | Builder relevance |
|---|---|---|---|
| **Token bindings.** Every product value but two constants is a `[#..#]` token or formula (`GS1=3`) - the dialect other elements already use | all product elements | 43/69 bound params | Reuse the existing mapping surface (Q2) |
| **Raw JSON `RequestBody` via `string.Format`.** `string.Format("{{\"contents\":[{{\"parts\":[{{\"text\":\"{0}\"}}]}}],\"tools\":[{{\"google_search\":{{}}}}]}}", [#SearchQuery#])` (argument abridged: the stored one is the full parameter token wrapped in `.Replace(...)`); the method declares only two headers, no Body param | both Gemini processes | the only 2 bound `RequestBody` in the corpus (14 present, 12 empty) | Escape hatch for bodies the service does not model (Q5) |
| **Item-level composite binding.** Container `L8 {}`, a leaf bound to a scalar process param; the run time builds a ONE-item list | IdentitySync `roles_In` (ObjectList), ExecuteUpgradeBsf `PropertiesProperty_In` (CompositeObjectList) | the only 2 product composite request bodies | Works through the mapping engine as written (g3 §2) [traced; EM6] |
| **Collection-to-collection request** (container bound to another element's collection, items evaluated per row) | test only (21 top-level In collections: 18 <- another WS, 3 <- Read data) | 0 product | Covered by P1 parent binding (g3 §3); guidance must say "map the parent explicitly when items are formulas" |
| **Composite responses fetched, rarely used.** 12/25 product elements have a composite Out param; 1 maps it whole into a process Collection with item mappings, 3 read it in C# scripts, none maps a nested item into another element | Gemini, Bing, StudioFreeUpdate | nesting up to 4 element levels | The generator emits the full tree even when nothing reads it, always (Q3: no feature gate); v1 reads it into declared Collections (Q6) |
| **Designer "parameter from element outputs" grouping.** A CompositeObject process param `WebService1Results1` (Tag `SourceElement:WebService1`) with scalar copies of nested outputs. Measured 2026-11-03 (EM3 designer half, decisions Q6): "Create from element" on Gemini `Candidates` makes the wrapper with a Collection `WebService1Candidates_Out` whose items are only the first-level scalars; nested lists are not offered | both CrtCustomer360AI processes | 2 | Not reproducible (CompositeObject not declarable; P1 refuses it) - out of v1, ENG-92730 (Q6) |
| **Success rarely checked.** Of 25 product elements, 4 read `Success` or `StatusCode` at all: CrtBase branches on `Success`, MkpNotifications gates on `StatusCode`, Bing and the Customer360 Gemini copy expose both as process outputs. So 21/25 product elements never read Success/StatusCode (r6 §4.3 table; r6's summary says 22/25, which is inconsistent with its own table). No other error construct exists (no boundary event) | all | 1 branch on `Success` | Guidance must teach the failure mode, not assume authors check it (Q7) |
| **Fire and forget.** 11/25 product elements have no output read by any element, flow, parameter or script | IdentitySync, StudioFreeUpdate, MS Graph call 2 | 11 | A minimal `webService` block with no mappings is a real use case |
| **Raw `Response` consumed** | KB markdown, MS Graph content, 2 AI actions | 4 | `Response` is an ordinary Text output (Q7) |
| **Timeout rarely set.** `IX3` on 3/25 product (600000, 1000000 x2) and 3/41 test (1000 x2, 5000) | | 6/66 | `timeoutSeconds` optional (Q8) |

### 2.5 Test-package coverage usable as parity fixtures

| Fixture (ProcessTests unless noted) | Covers | Candidate use |
|---|---|---|
| UsrTestJiraCollectionRunTime, ...RunTimeCustom1 (version 1), UsrTestJiraReadDataRuntimeCollection | Composite In list fed from another WS or from Read data; items bound by formula, mapping, system value, constant | Mapping parity into nested items (Q5); `UsrTestJiraCollectionRunTime` WebService3/5 call a missing method (drift) |
| UsrRuntimeCollectionFormulas (9 elements, captions name each case) | Text into collection, unmapped item ignored, text collection to integer plain array, plain array from process param + formula, constant into collection, shuffle mapping; a stale `L18`-less `UsrUpdateLabelsCopy_In` | ObjectList / `ItemValue` shape, item formulas, drift (Q12) |
| UsrTestCollectionFormulaWithValueType, UsrTestRuntimePlainArray | ObjectList in and out, `int.Parse(...) * 10` per item; required-array `L6` on the `ItemValue` child | Required rule and `ItemValue` parity (Q3) |
| UsrTestJiraRuntimeCollectionWithDefaultValue | Defaults on composite request/response params | Defaults are never echoed (Q16) |
| WebServiceErrorHandlingProcess, UsrWebServiceErrorHandle | 401 and 404 cases; `Success`, `StatusCode`, `Response` on a page; conditional flows on `[#Success#]`; `IX3=5000`; `Success` with empty `L8` (5 test elements) | Error outputs in conditions (Q7), EM7 |
| BusinessRulesTest / UsrBPWithInvalidWebService, UsrTestWSInvalidCredsProcess | Invalid base URL, invalid OAuth credentials; the only 2 corpus elements with `IX4` true | `ignoreErrors` parity (Q7) |
| DepTest_Level3 / DepTestProcess | Service schema in another package (dependency analysis) | Cross-package notice (Q9, EM10) |
| UsrPrivatBankRatesProcessInterpreted / ...Compile | Same call interpreted vs compiled; Float outputs; `IX3=1000` | Describe on a compiled instance (g4 §2) |
| UsrRefreshTokenWithOAuth20Integration / ...Header / ...Query, CallingWebServiceUnderPortalUserQuerySysSettings | OAuth token refresh; portal user | Out of builder scope (auth is the service's), but secrets handling (Q16) |

No test process calls a SOAP service; the 4 SOAP schemas are install/ALM fixtures only (r6 §7) [measured: corpus].

### 2.6 Drift and gotchas in the data

| Finding | Count / evidence | Tag |
|---|---|---|
| An element names a method the committed service does not have | 4 elements: 2 product (GetMeetingTranscript WebService3/4 -> `GetTranscripts`, `GetTranscriptContent`; `UsrMsGraph` has `UsrMe`, `UsrOnlineMeetings`, `UsrGetTranscript`), 2 test (UsrTestJiraCollectionRunTime -> `UsrEditIssueRemove`) (r6-C11) | measured: corpus |
| Stored tagged params differ from the current method | In set 5, Out set 4 elements overall; product 1 + 1 (`UpdateBsfNotification.SendMessage` lacks `parseMode`; GetMeetingTranscript WebService2 has an extra `MeetingId_Out`) (r6-C11) | measured: corpus |
| Type drift | 2 of 486 param pairs (`Chatid` Text in the service, Integer in the process, UpdateBsfNotification) (r6-C3) | measured: corpus |
| `BP2` order: a tagged param after the result params | 9/66 (2 product, 7 test); all fit "the service gained a param after the first sync, appended at the end" (g1 §a) | measured: corpus; cause inferred |
| `RequestBody` present only on saves from 8.0.0.4926 on | 14/66 (11/25 product); the run time requires only `Success`, `StatusCode`, `Response` (`GetByName` throws) (r9-S3) | measured: corpus; traced |
| `Success` `L8 {GS2:"false", GS5:<schema>}` (Source None, not a constant) | 61/66; 5 test elements have `L8 {}` (r6-C4) | measured: corpus |
| `BK15` row names keep the element's original name after a rename (e.g. rows "WebService2" for element GetMarketplaceAppPublishedVersionWebService) | rows bind by `GT3`/`GT2`, not by `A2` (r9-S3) | measured: corpus |
| What describe returns is already re-synced: for an interpretable process (19/19 product WS processes are interpretable) the load runs the platform sync, so dropped and appended params are what describe shows, not the stored list; an unrelated modify of GetMeetingTranscript would persist the removal of 8 tagged params (WebService3/4, methods missing: 3 + 4; WebService2, method present with no response params: `MeetingId_Out`) and leave two formula tasks with dangling references and no invalid flag. Measured 2026-11-03 on stand `Creatio` (EM8 current-builder half, decisions Q12): one unrelated modify through the current clio deleted the 8 tagged params of an element with a wrong-case method from the database, with no warning; the dangling-reference half is not measured | g4 §2-3; decisions Q12 | traced; measured on data; measured: stand |

---

## 3. Stand `Creatio`

Read-only, 2026-11-02, through the clio MCP server; no write, compile, call or restart (r9). The owner-approved
probes of 2026-11-03 are summarised in 3.5.

### 3.1 Platform

Creatio 10.1.37 (`ConfigurationVersion`), .NET Framework (from the clio registration; `describe-environment` failed
with an authentication error, so the framework is not confirmed at run time), CrtProcessBuilder 1.6.6.131,
CrtProcessDesigner / ServiceDesigner / a `WebServiceUserTask` package 7.8.0 [measured: stand]. 10.1.37 has no
ENG-55920 fix (g2, r1-C7) [traced]. Features read in `AdminUnitFeatureState`: `ProcessParameterCollections`,
`ManageProcessCollectionParameters`, `PassProcessParameterValueAsArray`, `WebServiceDesigner`,
`WebServiceOAuth20Auth` all on [measured: stand]. The `GlobalAppSettings` value of `ProcessParameterCollections`
that the run time reads is not visible to a database read (EM6). Client values read in the designer page on
2026-11-03 (EM0, M-22; EM10, decisions Q9): `ProcessParameterCollections` ON, `DisableIgnoreErrorsInWebServiceUserTask`
OFF, `DisableParametersWithNestedArrays` OFF, `AutoAddPackageDependenciesInProcesses` ON, `AutoAddPackageDependencies`
OFF, `EncodeWebServiceElementQueryParameters` OFF [measured: stand].

### 3.2 Services

3 `ServiceSchemaManager` schemas, all shipped, none user-created; `VwWebServiceV2` returns the same 3 with `MetaData`
as JSON (r9 §2) [measured: stand]. The 32 service schemas / 63 methods of section 2.1 are the PackageStore corpus,
not this stand. Since 2026-11-03 the stand also holds the test service `UsrWscEchoService` (3.5), and since
2026-11-05 the SOAP 1.1 test service `UsrWscSoapService` (entered by hand, never called; measurements EM2 S2-S4).

| Service (package) | Method | Request | Response |
|---|---|---|---|
| BingSearchApiCall (CrtCustomer360AI) | `Search` GET | `query` (Query, required); subscription-key header (default from a sys setting) | 3 levels: `WPValue` Object[] (22 items, nested `VDLinks` Object[]), `MItems`, scalars |
| GeminiApiSearch (CrtGeminiApiIntegraion) | `GenerateContent` POST | 2 headers only (API key from a SecureText sys setting, `Content-Type` const); **no Body param** | `Candidates` > `CParts` > `PText`; `GMGSupports` > `GSGCIndices` Integer[]; `GMWSQueries` Text[]; scalars |
| CreatioMarketplaceApi (CrtBase) | `GetAppPublishedVersion` GET | 4 Query params (3 const, 1 sys setting) | 4 required Text scalars |

No SOAP service, no service with an auth block, and **no service with a Body request parameter**: the stand holds no
designer capture of a composite request body, so AC1/AC5 for request bodies need a new test service (EM1, Q17)
[measured: stand].

### 3.3 The two processes

| Process (package) | Element | Service.method | Shape worth noting |
|---|---|---|---|
| BingSearchProcess (CrtCustomer360AI) | `WebService1` | BingSearchApiCall.Search | `query_In` <- process param `SearchQuery` (token); 3-level output tree; grouping param `WebService1Results1` (CompositeObject, 32 items, unbound) |
| ActionGeminiSearch (CrtGeminiApiIntegraion) | `WebService1` | GeminiApiSearch.GenerateContent | `RequestBody` = `string.Format` formula; `Candidates_Out` read into process Collection `Candidates` with item mappings by UId |

Both carry `Success`, `Response`, `StatusCode`, `RequestBody` and 4 `BK15` rows; the stand-wide search is
incomplete (a `contains` filter on `MetaData` fails; 101 process schemas in non-shipped packages were not scanned)
(r9 §3) [measured: stand]. These two are the only designer-made read-back fixtures until EM2/EM3 capture new ones.

### 3.4 What describe returns today

On CrtProcessBuilder 1.6.6.131 (raw `DescribeProcess` call, r9-S6): type `ProcessSchemaWebService`, `buildType`
null, no member for `IX1`/`IX2`/`IX3`; unbound inputs omitted (Bing header and `RequestBody` absent); every tagged
param carries its `tag` and the recursive `itemProperties` (Bing `WPValue_Out` 22 items; Gemini `Candidates_Out` 8
items down to `GSGCIndicesItemValue_Out`); item properties come back with `isOutput=false` even for Out items
[measured: stand]. The first reading ("tag and nesting missing") came from a stale clio MCP client (8.1.0.135,
bundled process-builder 1.6.6.50) that drops those fields; verification must use a current clio build (r9-S6).

Confirmed 2026-11-03 (decisions Q14) on a designer-built element with nothing mapped (`UsrProcess_3389428`, on
`GeminiApiSearch.GenerateContent`; stored: 2 tagged inputs, 8 tagged outputs, 3 of them nested down to three levels,
the 4 result params, 4 `BK15` rows): the package returns the outputs with tags and the nested tree and bound inputs only (M-31), so
none of this element's inputs (neither the tagged inputs nor `RequestBody`), and no service, method, timeout or ignore-errors member. So an agent
today does not see the unbound inputs it has to map. A describe reading that day without tags or nesting came again from an older
session clio build. Describe reads the synced view: an element with a wrong-case method was described without its
still-stored tagged params (EM11, decisions Q9) [measured: stand].

### 3.5 Probes of 2026-11-03

Owner-approved stand writes, each recorded in the decisions file; every object is on the cleanup list
[measured: stand].

| Probe | What it showed | Decisions |
|---|---|---|
| `UsrWscBk15Probe` (Marketplace `GetAppPublishedVersion`) | a designer-built element gets 4 `BK15` rows in the traced shape; a designer copy gets none, stays row-less on re-save, and runs | Q4 |
| `UsrWscEm3Probe` (Gemini) | "Create from element" makes a CompositeObject wrapper holding a Collection of first-level scalars only | Q6 |
| package `UsrWscEm10Proc`, process `UsrProcess_3389428` | the service list is unfiltered with `AutoAddPackageDependenciesInProcesses` ON; the designer save adds one `SysPackageDependency` row to the service's package | Q9 |
| `UsrWscBk15Probe` WebService2, `IX2` in the wrong case | the save answers green, the next load prunes every tagged param, the run fails with `ItemNotFoundException` | Q9 |
| `UsrWscBk15Probe`, an unrelated `addParameter` through the current clio | the 8 tagged params of the wrong-case element are deleted from the database, no warning | Q12 |
| `UsrWscEchoService` (package `Custom`, base URI = the stand root, GET `UsrWscGet`, header `UsrWscProbe` with default Const `wsc-probe`) | the header default stays in the service: the designer element stores an empty value and the string appears nowhere in the process; Studio refuses a parameter code without the `Usr` prefix | Q16, Q17 |
| `UsrProcess_3389428` switched from Gemini to `UsrWscEchoService` | a service change asks "All data will be lost", regenerates every param, result params get new UIds, 4 `BK15` rows remain, the caption is renamed, the element name kept | Q11 |
| the designer's service field | no "new service" entry; the button next to it opens the selected service for editing | Q10 |

---

## 4. Implications for the builder's scope and guidance

Each row is a consequence of the evidence above; the decision it feeds was agreed by the owner on 2026-11-03 (see
[decisions](eng-92710-call-web-service-element-decisions.md)).

| Evidence | Consequence for the builder | What agents must be told | Q / EM |
|---|---|---|---|
| The element is `ProcessSchemaWebService`; the legacy `WebServiceUserTask` is used by 0 processes but is still reachable through the generic userTask route | Build the core element; refuse `userTask=WebServiceUserTask` with a pointer to the `webService` block | "Call web service" = the `webService` block, never a user task | Q1 |
| The element only references a registered service, as the designer does (no "new service" entry, measured 2026-11-03); services are few (3 on the stand, all REST; 32 in the PackageStore corpus, at most 7 methods each) | No discovery tool; resolve by UId, code, caption, case-insensitively, and list codes with captions in the refusal; a separate refusal for an environment with no registered service | Services are registered in Studio > Web services (base URI, methods, authentication), not by the builder; when none fits, stop and say what registration needs | Q9, Q10 |
| Platform lookups are ordinal and an unknown or wrong-case method silently yields no params; measured 2026-11-03: a case-only slip saves green, the next load prunes every tagged param, the run fails | Canonicalise the spelling before writing `IX2` | Spell-insensitive input is accepted, the stored name is the canonical one | Q9, EM11 |
| Product values are tokens and formulas; 24/69 params rely on the service default | Values go through the existing mapping surface, addressed by the stored names; unbound = default | Leave header/key params unbound to use the service default; do not copy keys into the process | Q2, Q16 |
| Raw JSON in `RequestBody` is the AI-action pattern; the service may declare no Body param | Accept `RequestBody` as constant or formula | `RequestBody` replaces the whole generated body; escape quotes in `string.Format` | Q5 |
| Composite request bodies are rare, depth 1, bound at item level (one-item list); collection-to-collection only in tests | Mapping into nested items needs no rule change (g3) | Item-level values build ONE item; for one item per source row map the container to the source collection too | Q5, EM6 |
| Before the ENG-55920 fix (absent in 10.1.37, the stand), an inner object array whose container has no source of its own while its leaves are mapped is sent without that inner list (null); a mapped inner container is sent; that such a process saves without error is not measured | Warn, do not refuse: notice N5 on every target (the fix state is not readable), naming the affected builds in words ("Creatio builds before the platform fix for arrays inside arrays (10.1.37 among them)") and the remedy | On platforms without the fix, an inner list with no source of its own is sent with no value even when its items are bound; bind the inner container to a collection, or build the body in `RequestBody` | Q13, EM12 |
| Composite responses are common, deep (4 element levels with `ItemValue`) and rarely consumed | Always generate the full `L18` tree (Q3: no feature gate); read-back exposes it (today's describe already returns the output tree, not the inputs, 3.4) | Read a response into a declared Collection (2 levels) with item mappings; ObjectList third levels and the "parameter from element outputs" mirror are not supported in v1 | Q3, Q6, Q14 |
| `Success` is checked by 1 product element; before 8.3.2 a failed call stops the process (C4; only `RequestCanceled` is caught, traced) | Write `ignoreErrors` to `IX4` directly (the package already fails to compile below about 8.3.2, EM9); result params are ordinary outputs | With `ignoreErrors` on, branch on `Success` / `StatusCode`, or the failure passes silently | Q7, EM7 |
| The designer edits seconds, `IX3` stores ms, timeouts are rare | `timeoutSeconds` optional, x1000 | Absent = the method's own timeout; the limit includes retries | Q8 |
| Service defaults carry literal bearer tokens and basic-auth passwords in the corpus; a designer element stores no default (measured 2026-11-03: the default stays in the service) | Never echo defaults, auth info or base-URI credentials | Describe will not show defaults; that is deliberate; fill a key or token parameter from a system setting or a process parameter, never a constant | Q14, Q16, Q10 |
| Methods vanish and services gain params after the first save; describe and every modify re-sync; measured 2026-11-03: an unrelated modify deletes an unresolved element's params, and a service change in the designer regenerates every param with new UIds | Report drift in describe; notice before a modify persists a prune (not refused); replace semantics on method or service change, guarded like sub-process retarget | A modify of any element can drop the params of a web-service element whose method no longer exists; describe's `issues[]` says so | Q11, Q12, EM4, EM8 |
| One product process calls a service in another package (CrtCustomer360AI -> CrtGeminiApiIntegraion); the designer save adds the dependency (measured 2026-11-03), the builder adds none (traced) | Notice, no dependency added | Export the service's package too | Q9, EM10 |
| 0 SOAP calls, 0 multi-instance WS elements | Refuse multi-instance; build SOAP like REST with notice N10 (the SOAP capture showed the same shape, M-33) | multi-instance web-service calls are not built; SOAP ones are, by the REST rules | Q20 D |
| "Call a web service" collides with the backend guides for writing C# configuration services | Route line in `routing` plus a new `process-web-service` article; catalog flips `webService` to buildable | A process calling a registered service is `process-web-service`; writing a service is `configuration-webservice` | Q15 |
| The stand has no Body-param service and no reachable echo endpoint of its own | Test REST services `UsrWsc*` with the stand root as base URI (EP option E4, no echo; `UsrWscEchoService` exists); parameter codes carry the `Usr` prefix Studio requires; the request bytes (EM6) and EM12 are not measurable on the stand | (none; owner-gated stand writes) | Q17, EM1-EM3, EM6 |
| Version copies and internal tooling inflate the product count | Parity fixtures from the 21 distinct product elements, weighted to the 12 customer-facing ones | (none) | test plan |
