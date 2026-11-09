# ENG-92710 Call web service element: stand measurements (EM0-EM12)

Written 2026-11-02; updated 2026-11-03 with the owner's agreed decisions (decisions Q1-Q20, "Agreed" lines) and the
first stand runs: EM9 is done; EM1, EM3, EM4, EM8, EM10 and EM11 are partly done; the Q4 BK15 probe is done (M-13 to
M-21, and the "Measured" block of each EM); later the same day M-22 to M-31 (section 0). Measured facts: M-01 to M-46 (M-32 to M-35: EM2 S2-S4, 2026-11-05; M-36 to M-44: the final-cut runs on stand `eng105744-web02`, 2026-11-09; M-45, M-46: the KB-7 agent runs AR-1 / AR-2, same day). Every other expected result is a hypothesis until its EM runs.

Basis:
- crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history only): PB.
- clio `origin/master` `6be5dde28`: CLIO. clio-knowledge `origin/master` `5f14ceb`: KB.
- CrtProcessDesigner 7.8.0 (PackageStore branch) and the PackageStore 7.8.0 corpus: PD, PS.
- Core: `<core>` trunk `468c4c71b` (8.3.3.1722): CORE. The `<Creatio2>/TSBpm` checkout
  (2025-07) is stale and lacks `IX4` (g2); it is not used here.
- Creatio Academy (r7).
- Stand `Creatio` (a registered clio environment): read-only inventory of 2026-11-02 (r9), Creatio 10.1.37, .NET
  Framework, CrtProcessBuilder 1.6.6.131; the writes and runs of 2026-11-03 recorded in the decisions file (Q4, Q6,
  Q9, Q11, Q12, Q14, Q16).
- Research notes r1-r10 and gap notes g1-g5 (local, not part of the plan), and the adversarial verdicts in
  `R/_workflow-result.json`. A verdict's correction overrides a note; g notes override r notes where they disagree.

Paths legend (every evidence path is repository-relative after its prefix):

| Prefix | Root |
|---|---|
| `CORE/` | `<core>/TSBpm/Src/Lib` |
| `PD/` | `<PackageStore>/CrtProcessDesigner/branches/7.8.0` |
| `PS/` | `<PackageStore>` |
| `PB/` | `packages/CrtProcessBuilder/Files/src/cs` of crt-process-builder `origin/main` (`git show`) |
| `CLIO/` | `<clio>` |
| `R/` | `<research notes, local>` (research notes, cited as rN / gN with a section) |
| `CAP/` | `<research notes, local>/captures` (where every measurement saves its artifacts) |

Tags: [traced] read in source, a hypothesis about run time; [corpus] measured in shipped metadata; [measured
<date>] observed on stand `Creatio` or in a binary on that date; [doc] Academy; [inferred] my inference, not read
anywhere. A traced expectation is not a fact until the EM named next to it has run.

Sibling files: [README](README.md) · [facts](eng-92710-call-web-service-element-facts.md) ·
[usage](eng-92710-call-web-service-element-usage.md) · [reuse](eng-92710-call-web-service-element-reuse.md) ·
[serialization](eng-92710-call-web-service-element-serialization.md) (WSCR, §8.2-8.3: the oracle rule a capture is
compared under; "the oracle rule" below means WSCR) ·
[design](eng-92710-call-web-service-element-design.md) · [decisions](eng-92710-call-web-service-element-decisions.md)
(Q1-Q20, agreed by the owner 2026-11-03) · [plan](eng-92710-call-web-service-element-plan.md) (slices A-D, gates) ·
[test plan](eng-92710-call-web-service-element-test-plan.md) · [estimate](eng-92710-call-web-service-element-estimate.md).

## 0. Summary

- Thirteen measurement ids, EM0-EM12. EM0 is read-only. EM9 is local and needs no stand. Every other EM writes to
  stand `Creatio` and runs only on the owner's explicit yes, one write at a time.
- **Status on 2026-11-03.** Done: EM9 (local; IX4 is written directly, Q7), the Q4 BK15 probe (M-13), EM3's
  designer route (a) (M-15), EM11's and EM10's designer halves (M-16, M-17), EM8's current-builder half (M-18).
  Partly done: EM1 (`UsrWscEchoService` exists with one method, M-19), EM4 (a service change only, M-20). Still to
  do: EM0, EM1's remaining services and shapes, EM2, EM3 route (b), EM4's method change, EM5, EM6, EM7, EM8's
  remaining halves, and every builder half on the claimed cut.
- **Status later on 2026-11-03 (session 1 and the EM7 part of session 2).** Done: EM0 (M-22), EM1 (M-23; three
  services, plus a 2-level method `UsrWscOrders2` on `UsrWscDriftService` for EM3), EM2 (M-24), EM3 route (b) (M-25,
  on `UsrWscDriftService.UsrWscOrders2`), EM4 S1-S3 (M-26, M-27), EM7 designer half (M-28). Still to do: EM5, EM6,
  EM8's service-save and designer halves, and every builder half on the claimed cut. What differed from the
  expectation and what it changes: the "Measured 2026-11-03" block of each EM, and decisions Q6, Q7, Q11.
- **EM8's service-save and designer halves: done 2026-11-03 (session 2; M-29, M-30, M-31).** The designer's prune of an
  element whose method is gone is harsher than the builder's (M-18): it also deletes the result params and the BK15
  rows (decisions Q12). Only EM8's claimed-cut half (the N7 notice) stays open.
- **EM2 S2-S4 (SOAP): done 2026-11-05 (M-32..M-35).** Run on the owner's request: the designer builds a SOAP element
  with the REST shape, so decisions Q20 is D (SOAP built like REST, notice N10).
- **Final cut 1.6.6.146: run 2026-11-09 on stand `eng105744-web02` (10.2.1024, .NET Core; M-36..M-44; `CAP/em-final146/`,
  report `em-final146-report.md` there).** Stand `Creatio` is retired; the fixtures were recreated on the new stand
  (package `UsrWscTestFixtures`). PASS: EM2 step 9, EM5 with step 7 (the STOP gate holds), EM6, EM6-B (by the trace;
  oracle A cannot be saved, M-39), EM7 step 3, EM10, EM11 (the refusal lists codes without captions), EM12. Against
  expectation: EM8 step 8 - the unrelated modify is refused by the platform because a reader of a pruned parameter
  exists, nothing is written and N7 is not in the reply (M-41). KB-7 on the draft article: AR-2 PASS (M-46); AR-1
  reaches the right service and method, builds in two steps and maps by stored names, but through two refusals where
  the test plan allows one (M-45).
- Four groups by gate (section 4):
  - **Before any package code (day 0):** EM0, EM1 (the test service), EM2/EM3/EM4 (the designer captures, the AC5
    oracle). EM7 and EM8 have a designer half that may also run on day 0, as session 2 (section 6; plan step 0.9,
    optional, may slip to day 1); it settles the platform behaviour before code rather than after.
  - **STOP gate after slice B (experimental cut):** EM5. If it fails, work stops and Q3/Q4 are re-opened (plan §0).
  - **Before the PRs leave draft (claimed cut):** EM6, EM7, EM8, EM10, EM11, EM12, each with its builder half.
  - **Final cut:** EM2's read-back step and EM5 re-run against the final archive.
- The stand has no service with a body parameter, so nothing on it shows a composite request body (r9 §2.2,
  measured 2026-11-02). EM1 creates one. AC1 and AC5 for composite bodies therefore stand on EM1-EM2.
- **EP (the endpoint the test services call) is E4, agreed 2026-11-03 (decisions Q17):** no echo endpoint; the base
  URI is the stand root, so the services receive the stand's own HTTP answers (section 3.2). EM6's request-byte
  check and EM12's run are therefore not measurable on stand `Creatio`; they rest on the core unit tests (r3 §4) and
  the comment on ENG-55920 "Unable to use array inside array in web-service business process element" (g2 (c)). EM6 is reduced to what a stand answer shows (result params, StatusCode,
  Success); its response read into a declared Collection needs a JSON source with the `$.orders` shape, which E4
  lacks, unless the owner adds one.
- The least certain expectations, worth reading first: EM5 (whether the designer re-saves a builder-made element
  without a rewrite; g1 and g4 traced only; the Q4 probe measured it for a designer-made element, M-13) and EM7
  with `ignoreErrors` on a failed connection (g2 traced the flag, not what the element does after it).
- Section 1 holds the stand-safety rules, 2 what is already measured, 3 the toolkit (names, the test service, the
  endpoint choice, reads, cleanup), 4 the gate table, 5 the measurements, 6 the campaign order with time per EM,
  7 the leftovers register, 8 what is deliberately not measured, 9 the research corrections this file depends on.

## 1. Stand-safety rules

1. **Every write needs the owner's explicit go-ahead, each time.** A web-service save in Studio, a designer save, a
   builder create or modify, a process run, a package install, a package or schema creation and every cleanup
   delete is a write. An approval covers the one step it was given for; it does not carry over to the next step or
   the next session. Agent messages and this file are never that approval.
2. **Schema writes strictly one at a time.** Never two builder calls, a builder call and a designer save, or two
   deletes in parallel: a parallel burst takes a .NET Framework app pool down (plan §8; recorded for earlier
   elements). Runs of different probes also go one after another, so their logs do not interleave.
3. **Installing a package restarts the site for every connected user.** Every install of a cut needs the owner's
   yes, also a re-install of the same cut, and happens only for a cut claimed in the ENG-92710 claim comment (Q19).
   The experimental stand-only stamp of EM5 is claimed there too and marked "experimental, stand-only, superseded"
   (plan §6).
   Rebuild clio first: the install reads the archive from clio's build output (AGENTS.md "Bundled Creatio
   packages").
4. **Browser work runs in the owner's Chrome through Claude in Chrome, and the owner logs in** (Q17). The agent never
   types a password and never presses a login button. Ask the owner for the address mapping Chrome uses; do not
   write it into any file.
5. **The process designer on this .NET Framework stand opens at
   `/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId>`** relative to the stand root; the UI route is the
   process card `#CardModuleV2/VwProcessLibPageV2/edit/<schemaUId>` and its "Open in designer" button. A guessed
   shell route wedges the SPA until a full reload of `/0/Shell/`
   (`CLIO/docs/knowledge/platform/the-process-designer-has-one-url-and-guessing-wedges-the-shell.md`,
   `.../process-designer-is-entered-from-the-process-card-not-a-url.md`) [measured earlier, recorded there].
6. **Open the Call web service card of every element before a designer save.** The page re-syncs parameters on
   open (`synchronizeActualSchemaParameters`, r2 §6), and only an opened element is re-serialized from the page
   [traced]. A save without opening the card measures nothing about the page.
7. **Cleanup never uses HTTP DELETE.** The stand rejects HTTP DELETE
   (`CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-open-questions.md:469-477`, which
   gives no status code); the 405 code is measured at ENG-95985 "Send email attachments"
   (`CLIO/spec/eng-95985-send-email-attachments/eng-95985-send-email-attachments-plan.md:195`). Rows go through `execute-dataservice-batch` (DeleteQuery over POST); processes through
   `clio delete-schema <name> --remote -e Creatio --timeout 600000` from the CLI (a remote delete took about 6 min on
   2026-09-27 and 7-50 s on 2026-10-02, same file; through MCP it dies at the 100 s default).
8. **Use a current clio for every read through the package.** The clio MCP server of the 2026-11-02 session was the
   global tool 8.1.0.135 (bundling 1.6.6.50); it dropped `tag`, `itemProperties` and `isOutput` from the
   describe reply, which the package itself returns (verdict r9-S6; on 2026-11-03 an older MCP clio build again showed
   a describe without tags or nesting, M-21). What the package 1.6.6.131 really omits is every unbound input (bound
   inputs come back, M-31) and the `webService` member (M-21). Before any describe or
   builder call, check `get-tool-contract` and the client version; use a clio built from `origin/master` or the
   feature branch. A builder probe is judged by the stored bytes (RB-1), never by its success line alone.
9. **Read stored state, not a design session.** The capture is `SysSchema.MetaData` and `SysLocalizableValue`
   (RB-1, RB-2), read with `execute-esq`. `execute-sql-script` is not used for reads without the owner's yes: its
   tool contract is classed destructive (r9 header). Describe is never taken as the capture: for an interpretable
   process it returns a re-synced instance, not the stored list (g4 §2).
10. **Never run or modify a capture process** (EM2 capture A, EM3 capture B). They are the AC5 oracle and the
    read-only e2e fixture (plan CL-9; EM2's is also the process of manual TC-11, test plan §8); a run adds history
    and logs to them, and a builder modify could rewrite them.
    Run-time probes use their own processes; EM5's modify-on-a-designer-element check uses the copy
    `UsrWscEm5OracleCopy`.
11. **Redact before saving.** The test service's base URI (`IQ1` in the service metadata) is the stand's own address,
    and the designer's default element caption may embed it (r9 §3.2 measured Bing's default caption as base URI +
    index; r2 §6 traces service caption + index; EM2 records which). Every file saved under `CAP/` replaces the
    stand address with `<stand>` before it is written. The redacted EM2-EM4 reads are what pattern B commits under
    `PBT/Fixtures/WebService/Stand/` (committed redacted, not byte-verbatim); each `PROVENANCE.md` lists the redacted
    keys, which WSCR leaves out of the comparison (serialization §8.1, W14). No credential, token, cookie or
    connection string goes into any file, register or test snapshot (Q16).
12. **No shipped service is called.** Bing, Gemini and Marketplace are never invoked; their sys settings hold keys
    (r9 §4). Run-time probes call only `UsrWsc*` services. (The Q4 probe's run of 2026-11-03 sent one GET with empty
    inputs to the Creatio Marketplace API, decisions Q4; no further call to a shipped service is planned.)
13. **No feature-state change** is planned on shared stand `Creatio`. EM0 reads the states; if a measurement needs a
    different state, it is reported to the owner, who decides.

## 2. Facts already measured

| # | Fact | Date | Evidence |
|---|---|---|---|
| M-01 | `ConfigurationVersion` 10.1.37; .NET Framework from the clio registration (`isNetCore:false`), not probed at run time: `describe-environment` failed with "Authentication failed" while ESQ reads worked | measured 2026-11-02 | r9 §1 |
| M-02 | CrtProcessBuilder 1.6.6.131; CrtProcessDesigner, ProcessDesigner, BaseProcessDesigner, ServiceDesigner, WebServiceUserTask packages 7.8.0; cliogate 2.0.0.48 | measured 2026-11-02 | r9 §1 |
| M-03 | 3 web-service schemas, all shipped (BingSearchApiCall, GeminiApiSearch, CreatioMarketplaceApi), all REST, none with an auth block, none with a Body request parameter; no `UsrWsc*` service | measured 2026-11-02 | r9 §2, §2.2 |
| M-04 | 2 processes store a Call web service element (BingSearchProcess, ActionGeminiSearch) as `ProcessSchemaWebService`, `BL7` 652b598d; describe on 1.6.6.131 types it `ProcessSchemaWebService` with no `buildType` and no service/method member, but does return `tag` and nested `itemProperties` of the outputs (the stale MCP client dropped them); no unbound input (M-21; bound inputs come back, M-31) | measured 2026-11-02 | r9 §3.1, §3.4; verdict r9-S6; M-21 |
| M-05 | `AdminUnitFeatureState`: `ProcessParameterCollections`, `ManageProcessCollectionParameters`, `PassProcessParameterValueAsArray`, `WebServiceDesigner`, `WebServiceOAuth20Auth` = 1. Not read yet: `DisableIgnoreErrorsInWebServiceUserTask`, `DisableParametersWithNestedArrays` (the client states of `AutoAddPackageDependenciesInProcesses` and `AutoAddPackageDependencies` are M-17) | measured 2026-11-02 | r9 §4 |
| M-06 | Read technique: `execute-esq` on `SysSchema` returns `MetaData` as plain JSON (about 58 KB for BingSearchProcess); keep one row per call (200,000-byte ESQ budget); a `contains` filter on `MetaData` fails with a NullReferenceException; `execute-esq` on `VwWebServiceV2` with all columns returns the service `MetaData` JSON | measured 2026-11-02 | r9 §5 |
| M-07 | `ProcessSchemaWebService.IsIgnoreErrors` is present in the 10.1.37 core binaries (the line the stand runs) and in 8.3.2.4199, 8.3.4.2753 (CI), 10.0.0.950, 10.1.830, 10.2.931; absent in 8.1.3.6947 and 8.3.1.4498; the boundary is core tag 8.3.2.3590 | measured 2026-11-02 (binary metadata, not the stand's own DLL) | g2 (a), (b) |
| M-08 | The ENG-55920 fix's member names (`IsNestedListParameter`, `IsUnmappedEmptyList`) are absent from 10.1.37 `Terrasoft.Core.dll` (local core-bin, 2026-06-23) and present in 10.1.830 and 10.2.931 (first fixed 10.1 build not bisected) | measured 2026-11-02 (binary, not the stand) | g2 (c) |
| M-09 | HTTP DELETE rejected on this stand (405, measured at ENG-95985); remote `delete-schema` 6 min (2026-09-27) and 7-50 s (2026-10-02) | measured (delete-schema timings 2026-09-27, 2026-10-02) | rule 7: the 405 code from the ENG-95985 plan `:195`; rejection and timings from eng-92719 open-questions `:469-477` |
| M-10 | No SQL route reads a run's parameter values: a completed element's `SysProcessElementData` row is deleted; branch on the value and read `SysProcessElementLog`, whose captions persist | measured 2026-09-22 | `CLIO/docs/knowledge/platform/a-running-process-parameter-value-is-not-readable-from-sql.md` |
| M-11 | `RunProcess` with result parameter names runs synchronously; an unknown name aborts the launch; `success` is not the verdict, `processStatus` is (2 Done, 3 Error) | measured 2026-08-26 | `CLIO/docs/knowledge/platform/runprocess-result-parameters-switch-the-execution-mode.md`, `.../runprocess-success-flag-is-not-the-run-verdict.md` |
| M-12 | An element that throws during a run writes the exception to the root `SysProcessLog.ErrorDescription` before RunProcess returns; RunProcess itself answers only "check the process log" | measured (record 2026-10-02) | `CLIO/docs/knowledge/platform/runprocess-hides-an-element-exception-behind-a-generic-message.md` |
| M-13 | Q4 BK15 probe (`UsrWscBk15Probe`, Creatio Marketplace API `GetAppPublishedVersion`): a designer-built element gets 8 tagged params, Success / Response / StatusCode / RequestBody and exactly 4 BK15 rows of the C-04 shape; the designer's copy/paste gets the same 12 params with fresh UIds and no rows; a reload, open and re-save keeps the copy row-less and the original's 4 rows unchanged; the row-less element runs without an exception | measured 2026-11-03 | decisions Q4 |
| M-14 | EM9: crt-process-builder `origin/main` already fails to compile on 8.3.1.4498 (CS1061, CS0117) and 8.1.3.6947 (MSB3245) and builds on 8.3.2.4199, with or without a direct `IsIgnoreErrors` reference; the package floor is already about 8.3.2, so IX4 is written directly | measured 2026-11-03 (local) | decisions Q7; EM9 |
| M-15 | EM3 route (a), "Create from element" on `GeminiApiSearch.GenerateContent` (`UsrWscEm3Probe`): a CompositeObject wrapper `WebService1Results1` (Tag `SourceElement:WebService1`) holding a Collection `WebService1Candidates_Out` (Tag `WebService1.Candidates_Out`) whose items are only the first-level scalars; nested collections are not offered | measured 2026-11-03 | decisions Q6 |
| M-16 | EM11 designer half (`UsrWscBk15Probe`, `WebService2`'s `IX2` replayed as `getAppPublishedVersion`): the save answers success with all params stored; the next load drops every tagged param; the run fails with `ItemNotFoundException` at `ServiceClient.CreateRequest` | measured 2026-11-03 | decisions Q9 |
| M-17 | EM10 designer half: client `AutoAddPackageDependenciesInProcesses` on, `AutoAddPackageDependencies` off; the designer's service list is unfiltered; a designer save in the new package `UsrWscEm10Proc` (0 dependencies) with an element on `GeminiApiSearch` adds exactly one row `UsrWscEm10Proc -> CrtGeminiApiIntegraion` | measured 2026-11-03 | decisions Q9 |
| M-18 | EM8 current-builder half (`UsrWscBk15Probe` after M-16): an unrelated `addParameter` through the current clio + CrtProcessBuilder answers "1 operation(s) applied" with no notice and permanently deletes `WebService2`'s 8 tagged params; its result params stay, `IX2` is unchanged; `WebService1` keeps all 12 params and 4 rows | measured 2026-11-03 | decisions Q12 |
| M-19 | EM1 partial: `UsrWscEchoService` (`2ac0b905-591d-4623-a8bf-990846cea7d3`, `Custom`, base URI = the stand root) with one GET method `UsrWscGet` (`/UsrWscGet`) and one header `UsrWscProbe` (`X-Wsc-Probe`, default Const `wsc-probe`, stored `IU5 {IV1 "wsc-probe", IV2 1}`); a designer element on it stores `UsrWscProbe_In` with an empty `L8` and the default nowhere in the process; Studio refuses a parameter code without the `Usr` prefix | measured 2026-11-03 | decisions Q16 |
| M-20 | EM4 partial, a service change in the designer (`UsrProcess_3389428`, `GeminiApiSearch` -> `UsrWscEchoService`): confirm "All data will be lost", every param replaced, the result params with NEW UIds, 4 BK15 rows remain, caption "WSC echo service 1", element name `WebService1` kept | measured 2026-11-03 | decisions Q11 |
| M-21 | Describe on 1.6.6.131 for a designer-built element (`UsrProcess_3389428` on `GeminiApiSearch`): the package's DescribeProcess returns the outputs with each `tag` and the full nested `itemProperties` tree (down to 3 levels) and Success / Response / StatusCode; no unbound input (tagged `_In` or RequestBody; nothing was bound, and M-31 shows that a bound input comes back) and no `webService` member (service, method, timeout, ignore-errors). The reply without tags or nesting seen that day came from the session's older MCP clio build (rule 8). Describe reads the synced (pruned) view (M-16) | measured 2026-11-03 | decisions Q14 (corrected) |
| M-22 | EM0: versions as M-01, M-02; of the six EM0 feature codes only `ProcessParameterCollections` has a `Feature` row (`All employees` = 1); client `Terrasoft.Features.getIsEnabled`: `ProcessParameterCollections` true, `DisableIgnoreErrorsInWebServiceUserTask` false, `DisableParametersWithNestedArrays` false, `AutoAddPackageDependenciesInProcesses` true, `AutoAddPackageDependencies` false, `EncodeWebServiceElementQueryParameters` false; captures saved by an administrator login, culture en-US | measured 2026-11-03 | `CAP/em0/baseline.md` |
| M-23 | EM1: Studio stores every planned shape. Object array: `IU2` Object, `IU3` true (Is array forced on, no Required for an Object), nested `IU6` with relative `IU1` paths, a third response level inside `IU6`; scalar array `IU3` true; a single JSON object is flattened into scalars `$.customer.name` / `$.customer.city` (C-08); Required -> `IU7` true; a URL segment is always required in the UI (`IU7` true, `IU5 {IV1 "null"}`); a Query parameter is Required by default (`IU7` true, `IU5 {IV1 "null", IV2 1}`); an unbound Boolean or Text body parameter stores `IU5 {IV1 "null"}`; two parameters may share a caption | measured 2026-11-03 | EM1 block; `CAP/em1/` |
| M-24 | EM2: the designer element on `UsrWscEcho` matches every expected row (element keys, BP2 order, A3/A4/IL2/L12, L6 rule, types, ItemValue, result params, 4 BK15 rows). New: an unbound Boolean In is `L8 {"GS2":"false","GS5":<P>}` (SQ5); a Text constant is `L8 {"GS1":1,"GS5":<P>}` with the value only in the `.Value` / `.DisplayValue` resources, an Integer constant `L8 {"GS1":1,"GS2":"1","GS5":<P>}` (EM4 S1); the " - Input" / " - Output" suffix applies to a top-level pair ("Status") and not to a nested item ("Reference" stays plain on both); element caption "WSC echo service 1"; a 3-method service selects no method | measured 2026-11-03 | EM2 block; `CAP/em2/` |
| M-25 | EM3 route (b): the container value picker of a declared "Collection of records" never offers a web-service object-array output ("There are no parameters of required type", for a 2- and a 3-level output); binding an item (`Orders.OrdNo` <- `UsrWscOrd2No_Out`) binds the container to the output array, and binding a nested item binds the nested collection. Stored: the declared collection is type 651ec16f (as the element's), items have no `L17`/`L12`, every level `GS1 3` with an `[Element:{ws}].[Parameter:{uid}]` token | measured 2026-11-03 | EM3 block; `CAP/em3/` |
| M-26 | EM4: a method change ("Are you sure you want to change method? All parameters will be lost.") and a service change ("Are you sure you want to change element schema? All parameters will be lost.") replace every parameter with new UIds and re-create the 4 BK15 rows with new row UIds but the same `GT5` values; `IX3`/`IX4` survive a method change, a service change resets `IX3` and keeps `IX4`; caption kept on a method change, renamed on a service change, name kept; the dependent Formula task and conditional flow keep the removed UIds and are not marked invalid, the save answers "Schema has errors" (the flow's condition) and stores the process | measured 2026-11-03 | EM4 block; `CAP/em4/` |
| M-27 | EM4: the designer's formula window resolves a double-clicked output by caption: "Reference" (top-level `UsrWscEchoText_Out`) bound the nested INPUT `UsrWscOrdNo_In` with the same caption; the save reported a schema error and stored it | measured 2026-11-03 | EM4 block |
| M-28 | EM7 designer half: a 404 completes the element with Success false, StatusCode 404 and the process continues, whether IX4 is true or false; a refused connection with IX4 true completes with Success false, StatusCode 0 and the process continues; with IX4 false it fails the element and the process (status Error, `System.Net.WebException` in `SysProcessLog.ErrorDescription`); under E4 an unauthenticated call to an unknown stand-root route answers 302 -> login page (200), a static path (`/UsrWscNoSuchRoute.js`) answers 404 | measured 2026-11-03 | EM7 block; `CAP/em7/` |
| M-29 | EM8: a service save that removes a method (its code renamed `UsrWscDrift` -> `UsrWscDriftGone`) writes nothing to the processes that call it (stored bytes identical) and does not evict a process already in the cache: describe of `UsrWscEm8Drift2`, described once before the save, still returns both elements' tagged params after it (twice); a process first loaded after the save (`UsrWscEm8Drift3`, `UsrWscEm4MethodChange`) describes with only Success / Response / StatusCode, no issue and no flag | measured 2026-11-03 | EM8 block; `CAP/em8/` |
| M-30 | EM8: the designer shows an element whose method is gone with an empty method field, no parameters and no timeout field; its save warns "Required fields ... are not filled in" for both elements (not for the Formula task), then "Schema has errors: The "FormulaTask1" element has an invalid value for the parameter "Body"" and stores: EVERY parameter of both elements deleted (the tagged ones AND Success / Response / StatusCode / RequestBody), all 8 BK15 rows deleted, both elements `BL11:false`, `IX1` / `IX2` unchanged (`UsrWscDrift`), the Formula token dangling with no flag; the caption resources of the deleted params are gone too | measured 2026-11-03 | EM8 block; `CAP/em8/` |
| M-31 | Describe on 1.6.6.131 returns a bound input (`UsrWscA_In` with its value / token, `sourceElement`, `tag`), not only outputs; an unbound RequestBody is not returned (a bound RequestBody is returned: `IsSetInThisSchema`, seen on the Gemini element by a raw describe, facts §6); an element with no parameters left returns none and no issue | measured 2026-11-03 | EM8 block (corrects M-21's "no input" for bound inputs) |
| M-32 | EM2 step 0 (SOAP): no `Feature` row has the code `DontUseSoap` or any code containing `Soap`, so the flag is off; Studio's "New web service" menu offers "SOAP service"; nothing changed | measured 2026-11-05 | EM2 S2-S4 block; `CAP/em2-soap/` |
| M-33 | EM2 S2 (OF-16): on `UsrWscSoapService` (SOAP 1.1, entered by hand: the WSDL wizard did not load, and the SOAP card forces every Object parameter to an array), the designer element on `UsrWscSoapEcho` has the same element keys as S1, the same tree (names, `L17` tags incl. `<code>ItemValue`, `L18` nesting, `L1` types, `L6` from `IU7`, `L12`), the same result params and 4 BK15 rows, caption "WSC SOAP service 1"; no `IQ7`, `IR5`, `IT1`, `IU1` or `IU8` value reaches the element. `BL11:false` comes from an unbound required input (AD10). A Boolean constant "True" is stored as Script `L8 {GS1: 3, GS2: "[#BooleanValue.True#]"}` with DisplayValue `[#Boolean value.True#]`; S1 never captured a Boolean constant (its `{GS2: "false"}` is the unbound default) | measured 2026-11-05 | EM2 S2-S4 block; `CAP/em2-soap/` |
| M-34 | EM2 S3: a timeout-only designer save of the SOAP element changes only `IX3` (7000 -> 9000); every parameter, UId, value, BK15 row and resource byte-identical. Outside the element, the schema's `BK34` went `en-US` -> `null` on this first re-save | measured 2026-11-05 | EM2 S2-S4 block |
| M-35 | EM2 S4: switching the SOAP element to `UsrWscEchoService.UsrWscEcho` ("Are you sure you want to change element schema? All parameters will be lost.") removes all 14 parameters (result params included) and creates the 32 of the REST tree with new UIds, re-creates the 4 BK15 rows with the same `GT5`, drops `IX3`, keeps `IX4` and the element UId and name, renames the caption to "WSC echo service 1"; no SOAP leftover (no SOAP service UId, key, parameter or resource); the new tree equals S1 apart from UIds | measured 2026-11-05 | EM2 S2-S4 block |
| M-36 | EM2 step 9 (final cut 1.6.6.146, stand `eng105744-web02`, 10.2.1024): describe of `UsrWscEm2CaptureA` returns the `webService` block (`service`, `serviceUId`, `method`, `timeoutSeconds` 7.0, `ignoreErrors` true, `serviceResolved` / `methodResolved` true, `issues` []) and all 32 stored parameters with tags, nesting and `required` / `bound` / `result` flags; `wsc-probe` appears nowhere; the stored bytes are unchanged by the describe | measured 2026-11-09 | `CAP/em-final146/em2/` |
| M-37 | EM5 on the final cut: the builder's element equals EM2 S1 under WSCR (known AD1 / AD3 / W12 differences only); a designer re-save and an unrelated builder modify leave the element node and the 4 BK15 rows byte-identical; step 7, `setElement` with the element's own describe `webService` block, answers `changes: {}` and the stored schema and resources stay byte-identical (the read-back members are ignored with a warning). The designer re-save added `SysPackageDependency` `Custom -> UsrWscTestFixtures` (the service's package), as M-17 | measured 2026-11-09 | `CAP/em-final146/em5/` |
| M-38 | EM6: a builder-made element on `UsrWscEcho` runs (Completed); `OutOk` false, `OutCode` 404, `OutBody` "" (on 10.2.1024 the stand root answers an unauthenticated `POST /UsrWscEcho` with 404); the declared `Orders` binding equals EM3 route (b) | measured 2026-11-09 | `CAP/em-final146/em6/` |
| M-39 | EM6-B: with tracing on, `SysPrcElementTraceLog` (readable by DataService ESQ on this stand) shows the element's values at run time: `UsrWscFlag_In = true` from the builder's ConstValue `{GS1: 1, GS2: "true"}`; `UsrWscOrders_In` one item `{UsrWscOrdNo_In: "A-1"}` from an item mapping; `UsrWscIds_In = [5]` from the item formula; an unmapped `RequestBody` is null (not ""); the header input `UsrWscProbe_In` is null (the service default is not an element value). The planned oracle A, a flow condition on the element INPUT `[#WebService1.UsrWscFlag_In#]`, is refused by the platform's pre-save validation ("cannot be used as a data source") | measured 2026-11-09 | `CAP/em-final146/em6/` |
| M-40 | EM7 builder half: `ignoreErrors: true` stores `IX4` true, absent stores none; the twins run exactly as the designer half (M-28): 404 -> `ok=False code=404` either way; refused connection -> `ok=False code=0` and Completed with `IX4`, element and process Error (`WebException`) without | measured 2026-11-09 | `CAP/em-final146/em7/` |
| M-41 | EM8 step 8: after the method rename, describe reports `methodResolved: false` with an issue on both elements (warm cache: parameters still listed); an unrelated `addParameter` through the feature build is REFUSED by the platform's pre-save validation (the Formula task reads `WebService1.UsrWscB_Out`, which the load pruned), nothing is written, and the reply carries no N7 notice: N7 is an end-of-request notice and the refused request never reaches it | measured 2026-11-09 | `CAP/em-final146/em8/` |
| M-42 | EM10 builder half: a builder create in a new package with 0 dependencies, on `GeminiApiSearch` (`CrtGeminiApiIntegraion`), adds no `SysPackageDependency` and answers with the cross-package notice | measured 2026-11-09 | `CAP/em-final146/em10/` |
| M-43 | EM11 builder half: `method: "USRWSCGET"` stores `IX2 = "UsrWscGet"` with a notice; `method: "nope"` is refused, nothing created, the refusal lists the method codes (no captions) | measured 2026-11-09 | `CAP/em-final146/em11/` |
| M-44 | EM12 builder half: with both containers unmapped and two leaves bound, the answer carries N5 for `UsrWscOrders_In.UsrWscLines_In` only | measured 2026-11-09 | `CAP/em-final146/em12/` |
| M-45 | KB-7 AR-1 (isolated headless agent, draft article kb92710 `9c85351`): the agent finds `CreatioMarketplaceApi` through a refused create naming a non-existent service, then `GetAppPublishedVersion` through a second refused create naming a non-existent method (the service refusal lists no methods), creates the element alone, then binds 4 inputs and 5 outputs in one modify by the stored names; no secret in the transcript; the stored schema carries the 9 bindings | measured 2026-11-09 | `CAP/em-final146/kb7/` |
| M-46 | KB-7 AR-2: after one refused create naming `OpenWeather`, the agent stops, creates nothing, and tells the user to register the service in Studio > Web services with base URL, method, parameters and authentication (key in a system setting or the service default, never a process constant) | measured 2026-11-09 | `CAP/em-final146/kb7/` |

Corpus facts the expected results lean on (PS 7.8.0, 38 process schemas, 66 elements, 25 product):

| # | Fact | Evidence |
|---|---|---|
| C-01 | BP2 order: 57/66 elements put the tagged IN params (request order), then tagged OUT (response order), then Success, Response, StatusCode[, RequestBody]; 9 do not (a param the service gained later lands after the result params) | g1 (a) |
| C-02 | RequestBody on 14/66 (11/25 product); every save stamped 8.0.0.4926 or later has it; the run time requires only Success, StatusCode and Response | g1; verdict r9-S3 |
| C-03 | Success `L8` = `{"GS2":"false","GS5":<process UId>}` on 61/66 (Source None with a value, not a constant); `{}` on 5 test elements | g1 (b); verdicts r6-C4, r9-S3 |
| C-04 | BK15: one `ProcessSchemaMapping` row per result param (52 x 3, 14 x 4), key set `BL1 UId A2 A3 A4 GT2 GT3 GT5 GT1`, no GT4, `A3 = A4 = process UId`, row order = BP2 result order, Success `GT1 = {"GS2":"false"}`; `A2` = the element name at creation (10 rows stale after a rename) | g1 (c) |
| C-05 | `A3 = A4 = process UId` on 751/751 parameters; `IL2 = element UId` at every level; `L12` written on every param; `L6` only when true | g1 (b) |
| C-06 | A scalar array becomes ObjectList with one child `<Name>ItemValue_<dir>`, Tag `<Name>ItemValue`, caption "Item value"; a required IN scalar array has no `L6` on the parent and `L6:true` on the child | g1 (b); verdict r6-C3 |
| C-07 | `IX3` on 6/66 elements; `IX4` on 2/66 (both `true`, saved 2025-11-20) | r2 §2; verdict r3-C2 |
| C-08 | 0 non-array Object / CompositeObject element params: the service designer flattens a single object into JSONPath scalars | verdicts r1-C6, r1-C8 |
| C-09 | 0 SOAP calls, 0 multi-instance WS elements | r6; verdict r6-C5 |

## 3. Toolkit: names, the test service, the endpoint, reads, cleanup

### 3.1 Names

- Prefix `UsrWsc` on every artifact, so the leftovers register can be checked by prefix (plan §8). Captions
  "WSC <...>". Package `Custom` unless an EM says otherwise. Service-parameter codes carry the prefix too: Studio
  refuses a "Code in Creatio" without `Usr` (M-19), so the element params are `UsrWsc..._In` / `_Out`. One created
  object breaks the name rule: the EM10 designer process kept its default code `UsrProcess_3389428` (section 7); a
  prefix check misses it.
- Services: `UsrWscEchoService` (EM1, the capture and run-time service; exists, M-19), `UsrWscDriftService` (EM4 S3,
  EM8, manual TC-12: a method is deleted on purpose, so the capture service is never touched), `UsrWscDeadService`
  (EM7: an endpoint with no listener). EM10 uses the shipped `GeminiApiSearch` (package `CrtGeminiApiIntegraion`),
  never run (rule 12).
- Processes created 2026-11-03: `UsrWscBk15Probe` (Q4, EM11 designer half, EM8 current-builder half),
  `UsrWscEm3Probe` (EM3 route (a)), `UsrProcess_3389428` (EM10 designer half, Q16, EM4 partial), and later the same
  day `UsrWscEm2CaptureA`, `UsrWscEm3CaptureB`, `UsrWscEm4MethodChange`, `UsrWscEm7Ignore`, `UsrWscEm7Strict`,
  `UsrWscEm8Drift2` and `UsrWscEm8Drift3` (section 7). Still planned: `UsrWscEm5Twin`, `UsrWscEm5OracleCopy` (a
  designer copy of EM2, the only process EM5 step 5 modifies), `UsrWscEm6Run`, the EM7 builder twins
  `UsrWscEm7IgnoreB` / `UsrWscEm7StrictB`, `UsrWscEm10Builder`, `UsrWscEm11Builder`, `UsrWscEm12Builder`.
- Packages (EM10 only): `UsrWscEm10Proc` (created 2026-11-03, holds the designer process), `UsrWscEm10Proc2`
  (planned, the builder process; outside `CrtGeminiApiIntegraion`'s closure).
- Service-parameter names are short and unique across all nesting levels: the run time flattens every level of a
  composite parameter into one dictionary with `Add`, so a Name or Tag repeated at two depths would throw (r3 §4;
  g3 §6, traced). EM1 avoids that case on purpose; it is not one of the things measured.

### 3.2 The test service (EM1) and the endpoint (EP = E4)

`UsrWscEchoService`, caption "WSC echo service", REST, base URI = the stand root (EP E4), package `Custom`. It exists
since 2026-11-03 (UId `2ac0b905-591d-4623-a8bf-990846cea7d3`) with only method `UsrWscGet` (address `/UsrWscGet`)
and its header `UsrWscProbe` (M-19); EM1 extends it to the table below:

| Method (code) | HTTP | Request parameters (code, kind, type, flags) | Response parameters |
|---|---|---|---|
| `UsrWscEcho` | POST, JSON, timeout 5000 ms, address `/UsrWscEcho` | Body: `UsrWscText` Text; `UsrWscReq` Text, required; `UsrWscFlag` Boolean, not required (the Boolean In default, serialization SQ5); `UsrWscIds` Integer, array, required; `UsrWscOrders` Object array with items `UsrWscOrdNo` Text with caption "Reference" (the nested half of a shared caption, SQ3), `UsrWscLines` Object array (items `UsrWscSku` Text, `UsrWscQty` Integer), `UsrWscTags` Text array; `UsrWscCustName` path `$.customer.name` and `UsrWscCustCity` path `$.customer.city` (a single object, which the service designer flattens, C-08); `UsrWscStatusIn` Text with caption "Status". Header: `UsrWscProbe` (header name `X-Wsc-Probe`), default Const `wsc-probe` (non-secret by construction, Q16) | `UsrWscEchoOrders` Object array (`$.orders`) with items `UsrWscEchoOrdNo` (`ordNo`), `UsrWscEchoLines` Object array (`lines`) with items `UsrWscEchoSku` (`sku`) and `UsrWscEchoParts` Object array (`parts`, item `UsrWscEchoPart` Text): three object levels; `UsrWscEchoIds` Integer array (`$.ids`); scalars `UsrWscEchoText` (`$.text`) with caption "Reference" (the top-level half of the nested shared caption), `UsrWscEchoQty` Integer, `UsrWscEchoOk` Boolean; `UsrWscStatus` Text with caption "Status" (the duplicate caption) |
| `UsrWscGet` | GET, path `/UsrWscGet/{UsrWscId}` (today `/UsrWscGet`) | UrlSegment `UsrWscId` Text, no default (serialization §2.3: `L6` true exactly when the segment has no default); Query `UsrWscQ` Text, required; Header `UsrWscProbe` (`X-Wsc-Probe`, default Const `wsc-probe`; exists) | scalars `UsrWscGetText`, `UsrWscGetCount` Integer |
| `UsrWscMissing` | GET, path `/UsrWscNoSuchRoute` (since 2026-11-03 EM7: `/UsrWscNoSuchRoute.js`; the stand root redirects an unknown route to the login page, a static path answers 404, M-28) | none | `UsrWscMissText` |

Why these shapes: each row of the designer emission rules (r2 §3, g1 port checklist) gets one instance. A required
IN scalar array (C-06), an object array with a nested object array and a nested scalar array, a third object level
in the response (Q6: unreachable by a declared Collection), a flattened object (C-08), a header default (Q14, Q16:
it must not come back from describe), a caption shared by a request and a response parameter (the
" - Input"/" - Output" suffix rule, r2 §3, and r3 open question 4), a caption shared by a nested request item and a
top-level response parameter ("Reference": whether the suffix counts nested items, SQ3), an unbound Boolean request
parameter (`UsrWscFlag`: the Boolean In default, SQ5) and a UrlSegment parameter without a default (`UsrWscId`: the `L6`
rule of serialization §2.3). The Academy states that Object needs Is array
for request parameters (r7 §3, [doc]); EM1 records what the Studio actually allows.

`UsrWscDriftService` (EM4 S3, EM8, manual TC-12): REST, base URI = the stand root; methods `UsrWscDrift` (GET, query
`UsrWscA` Text, response `UsrWscB` Text) and `UsrWscTc12` (GET, query `UsrWscE` Text, response `UsrWscF` Text; manual
TC-12's own method, which only TC-12 renames, so TC-12 does not depend on EM8's deleted method), and, since 2026-11-03, `UsrWscOrders2` (GET, response `UsrWscOrd2` object array with `UsrWscOrd2No` and the object array `UsrWscOrd2Lines {UsrWscOrd2Sku}`: the 2-level output EM3 route (b) binds, M-25). The former EM11
method `UsrWscCase` is not created: EM11's designer half ran on `UsrWscBk15Probe` (M-16) and its builder half needs
no case-renamed method.
`UsrWscDeadService` (EM7): REST, base URI `http://127.0.0.1:1` (the stand host's own loopback, a port with no
listener: a connection failure without a DNS lookup and without any host outside the stand) [inferred]; method
`UsrWscDead` GET, no parameters. The plan's alternative, an unresolvable host name, asks the stand's resolver but
calls no endpoint; either is acceptable, the owner picks.

**EP: E4, agreed by the owner on 2026-11-03 (decisions Q17).** No echo: the base URI of `UsrWscEchoService` and
`UsrWscDriftService` is the stand root, so their methods return the stand's own HTTP answers. The captures (EM1-EM5),
EM7, EM8, EM10 and EM11 do not depend on it: the designer builds the element from the service metadata, not from the
endpoint. Consequences: EM6's request bytes and EM12's run are not measurable on stand `Creatio`; they rest on the core
unit tests (r3 §4) and the ENG-55920 comment (g2 (c)); EM6's response read needs a JSON source with the `$.orders`
shape, which E4 lacks, so EM6 is reduced to the result params unless the owner adds a source. An existing echo route
(E1), a configuration echo service (E2) and an external echo host (E3) were not chosen.

### 3.3 Reads (all read-only)

All through a current clio (rule 8), `execute-esq` (MCP or CLI), `-e Creatio`. Keep every raw read in `CAP/em<n>/`,
redacted (rule 11).

| Id | Reads | How |
|---|---|---|
| RB-1 | stored process metadata | `execute-esq` root `SysSchema`, columns `Id, UId, Name, ModifiedOn, MetaData`, filters `Name = <process>` and `ManagerName = 'ProcessSchemaManager'`; one row (M-06) |
| RB-2 | the element's resources | `execute-esq` root `SysLocalizableValue`, columns `Key, Value, SysCulture.Name`, filters `SysSchema.Id = <RB-1 Id>` and `Key` starts with `BaseElements.<element name>.` [inferred: not tried on this stand; if ESQ refuses, ask the owner for a `SELECT` through `execute-sql-script`] |
| RB-3 | a service definition | `execute-esq` root `VwWebServiceV2`, all columns, filter `Name = <service>` (M-06); redact `IQ1` |
| RB-4 | name availability | `execute-esq` on `VwWebServiceV2` and on `SysSchema`, filter `Name` starts with `UsrWsc` |
| RB-5 | a run | `execute-esq` root `SysProcessLog`, columns `Id, StartDate, CompleteDate, Status.Name, ErrorDescription`, filter `Id = <processId>` from the run (M-12) |
| RB-6 | which elements of a run ran | `execute-esq` root `SysProcessElementLog`, columns `Caption, Status.Name, StartDate, CompleteDate`, filter `SysProcess.Id = <run>` (M-10) |
| RB-7 | feature states | `execute-esq` root `AdminUnitFeatureState`, columns `Feature.Code, SysAdminUnit.Name, FeatureState`, filter `Feature.Code` in the EM0 list (r9 §4 used this route) |
| RB-8 | package dependencies | `execute-esq` root `SysPackageDependency`, columns `SysPackage.Name, DependOnPackage.Name` [inferred column names], filter on the EM10 packages |
| RB-9 | the package's view | `describe-business-process` from a current clio (rule 8); never the capture (rule 9) |
| RB-10 | versions | `list-packages -e Creatio`, `get-sys-setting ConfigurationVersion`, `get-tool-contract` for create, modify, describe and run-process |

Output oracle for runs (M-10, M-11): scalar outputs (Success, StatusCode, Response text, a count) are read back
either as result parameters of a synchronous `run-process` with `resultParameterNames` (an `Out` process parameter
mapped from the element output) or, where the value is not a plain scalar, by a conditional flow per expected value
whose arms carry captions that state the value ("WSC OK status=200", "WSC other") and RB-6. A collection's content
is proven the same way: a Formula task reads the first row's item into a scalar, a condition branches on it.
Whether `run-process` returns a Collection result readably is not known [inferred]; it is not relied on.

### 3.4 Cleanup operations (each a write)

| Id | Operation | How |
|---|---|---|
| CU-1 | delete a probe process | `clio delete-schema <name> --remote -e Creatio --timeout 600000`, CLI, one at a time, newest version first; a refusal for a process with run history is recorded in the register and left to the owner |
| CU-2 | delete a `UsrWsc*` web service | `clio delete-schema <service> --remote` [inferred: not tried for a `ServiceSchemaManager` schema]; fallback: Studio > Web services, delete the record in the UI (lead in Chrome, owner's yes). Only after every process that calls it is gone, so no load prunes a live element (g4 §1) |
| CU-3 | delete an EM10 package | `clio delete-pkg-remote <name> -e Creatio` [inferred behaviour on this stand]; fallback: Configuration, delete the package in the UI; only after its processes are gone (CU-1) |
| CU-4 | delete rows (only if a probe creates records) | `execute-dataservice-batch` DeleteQuery items; re-read afterwards (a batch-level error can omit item outcomes, `CLIO/docs/knowledge/platform/dataservice-batch-stop-errors-can-omit-item-outcomes.md`) |
| CU-6 | run logs | `SysProcessLog` / `SysProcessElementLog` rows are not deleted; they are the stand's history |

CU-5 (removing an E2 echo endpoint) is dropped with E2 (Q17, 2026-11-03); the ids are kept stable.

## 4. Gate table

"Blocks" names the plan step that waits for the measurement ([plan](eng-92710-call-web-service-element-plan.md)).

| EM | When | Blocks | Settles or confirms | Research ids |
|---|---|---|---|---|
| EM0 | **done 2026-11-03** (M-22) | every EM; plan 0.5 | feature states the captures and runs stand on; versions recorded with every capture; whether the IX4 checkbox is visible (Q7) | r9 §4, §6; g2 (b); g5 open 1 |
| EM9 | **done 2026-11-03** (local) | nothing now: P-B3 writes IX4 directly | Q7 / Q13: the package floor is already about 8.3.2, so a direct `IsIgnoreErrors` reference adds none (M-14) | g2 design consequence 1 |
| EM1 | **done 2026-11-03** (M-19, M-23) | EM2-EM8, EM12's builder half | the service the captures use; what Studio stores for each shape (IU1-IU7) | r9 §6; r3 open 1 |
| EM2 | **S1 done 2026-11-03** (M-24); **S2-S4 (SOAP) done 2026-11-05** (M-32..M-35); final-cut read-back **done 2026-11-09** (M-36) | slice A shaper (P-A2), slice B (P-B3, P-B4); parity pattern B; AC5 | Q3 byte shape; Q4 BK15 rows on this service (shape measured on another, M-13); Q8 (IX3 = s x 1000, confirmation only); IX4 storage; caption suffix; constant/formula/item-level mapping bytes | r3 open 1; g1 port checklist; verdict r7-A3 |
| EM3 | route (a) **done 2026-11-03** (M-15); route (b) **done 2026-11-03** (M-25) | describe of response mappings; parity pattern B | Q6 scope (settled by route (a)); a hand-made Collection's item mappings, route (b) on `UsrWscDriftService.UsrWscOrders2` | r9 §6; r2 §7; g3 §1, §4 |
| EM4 | **done 2026-11-03** (M-20, M-26, M-27) | slice D replace semantics (P-D1) | Q11: what a method change clears and keeps; what a service change does to bindings, dependents and `IX3` | r2 §6 |
| EM5 | STOP gate after slice B (experimental cut); final-cut re-run with step 7 **done 2026-11-09** (M-37) | slice C start; AC5 "survives a designer re-save" | Q3 (platform sync a no-op), Q4 (rows not duplicated or rewritten), Success L8 kept; r3 open 4 (suffix overwritten by a server load?) | g1 (c) "verify once on a stand"; g4 Q3, M3 |
| EM6 | **done 2026-11-09** on the final cut (M-38; EM6-B M-39) | AC4 run time (result params) | under E4: a builder-made element runs and Success / StatusCode / Response arrive in process parameters; the request bytes, the one-item list, RequestBody null vs "" and the declared-Collection read are not measurable on stand `Creatio` (section 3.2) | g3 §2, §4; r3 open 3 |
| EM7 | designer half **done 2026-11-03** (M-28); builder half **done 2026-11-09** (M-40) | AC4; Q7 KB guidance | `ignoreErrors` true vs false on a 404 and on a failed connection | g2 (b); r3 §4.6 |
| EM8 | current-builder half **done 2026-11-03** (M-18); service-save and designer halves **done 2026-11-03** (M-29, M-30); feature-build half **run 2026-11-09**, refused by the platform without N7 (M-41) | P-D3 (the notice; Q12 = A, agreed) | the latent loss on `origin/main` (measured); whether a service save evicts the process cache; the dangling references | g4 M1, M3, Q2 |
| EM10 | designer half **done 2026-11-03** (M-17); builder half **done 2026-11-09** (M-42) | Q9 cross-package notice | designer adds a package dependency (measured), the builder does not | g5 open 2 |
| EM11 | designer half **done 2026-11-03** (M-16); builder half **done 2026-11-09** (M-43) | Q9 canonicalisation | a case-only method mismatch empties the params and fails the run (measured) | g5 (b); g4 §5 |
| EM12 | run half not measurable under E4 (Q17); builder half **done 2026-11-09** (M-44) | Q13 notice N5 | the inner list is dropped on 10.1.37: rests on the core unit tests and the ENG-55920 comment | g2 (c); verdict r1-C7 |

## 5. Measurements

### EM0 Read-only baseline

| | |
|---|---|
| Question | Which versions and switches do the captures and runs stand on; are the names free; do the read paths work? |
| Settles | Q7 (is the IX4 checkbox shown at all), Q13 (nested-array switch state), the version line recorded with every capture; Q9's designer service list and dependency auto-add are already measured (M-17) |
| Gate | day 0, before EM1; repeated (steps 1-2) before each install session |
| Preconditions | none; read-only |
| Writes | none |
| Owner | tells the lead which login will save the captures and its profile culture (step 7) |

**Measured 2026-11-03 (M-22; `CAP/em0/baseline.md`).** Every row as expected. Versions as M-01, M-02 (step 1:
`get-info` not run; `isNetCore:false` from the registration). Step 2: this session's MCP clio is the global
8.1.0.135, the stale build of rule 8; it was used only for `execute-esq` / `list-packages` / `get-sys-setting`, and
every capture read in EM0-EM7 went through `clio call-service` -> DataService SelectQuery instead (no describe, no
builder call in this session). Step 3: only `ProcessParameterCollections` has a `Feature` row (`All employees` = 1);
the other five codes have no `Feature` row, so their client values are code defaults. Step 5: the `UsrWsc*` objects
were exactly section 7's. Step 6: the three shipped services unchanged (ModifiedOn 2023-2025). Step 7: an administrator login,
en-US. Step 8: `ProcessParameterCollections` true, `DisableIgnoreErrorsInWebServiceUserTask` false,
`DisableParametersWithNestedArrays` false, `AutoAddPackageDependenciesInProcesses` true, `AutoAddPackageDependencies`
false, `EncodeWebServiceElementQueryParameters` false. Nothing in "If not" applies.

**Steps:**
1. RB-10: `list-packages` (CrtProcessBuilder, CrtProcessDesigner, ServiceDesigner, cliogate), `get-sys-setting
   ConfigurationVersion`, `clio get-info -e Creatio` (if it fails like `describe-environment` did on 2026-11-02,
   keep the registration's `isNetCore:false` and say so).
2. Check the session's clio (rule 8): `get-tool-contract` for `create-business-process`, `modify-business-process`,
   `describe-business-process`, `run-process`, `execute-esq`, `execute-dataservice-batch`; record the client
   version; stop if it is the stale 8.1.0.135.
3. RB-7 for `DisableIgnoreErrorsInWebServiceUserTask`, `DisableParametersWithNestedArrays`,
   `ProcessParameterCollections`, `AutoAddPackageDependenciesInProcesses`, `AutoAddPackageDependencies`,
   `EncodeWebServiceElementQueryParameters`.
4. Record that `GlobalAppSettings.FeatureProcessParameterCollections` (true in the repository `Web.config` /
   `app.config`, traced, r2 §3; not measured on a deployed stand, verdict r3-C6 (5)) is not readable from the
   database (r9 §4). The decisions Q3 research found it true in the shipped Web.config of every checked build from
   7.13 to 10.2.931; the builder always emits nested items and does not depend on it (Q3).
5. RB-4: the `UsrWsc*` services and schemas are exactly the ones section 7 lists as created.
6. RB-3 on the three shipped services once (already in r9 §2): confirm none has changed since 2026-11-02.
7. The profile culture of the login that will save the captures: resources land in the saving user's culture
   (g1 measured Russian rows in en-US files). The owner confirms it, or the lead reads it in the profile page.
8. In Chrome, on any page of the stand, read-only JavaScript: `Terrasoft.Features.getIsEnabled(...)` for
   `ProcessParameterCollections` and `DisableIgnoreErrorsInWebServiceUserTask` (the client gates of r2 §2-3);
   `AutoAddPackageDependenciesInProcesses` was read on 2026-11-03 (M-17). Nothing is clicked.

**Expected:**

| Read | Expected | Basis |
|---|---|---|
| versions | as M-01, M-02 | measured 2026-11-02 |
| `DisableIgnoreErrorsInWebServiceUserTask` | not enabled: the checkbox is shown and IX4 takes effect | default state on a fresh stand not measured (g2 (b)) |
| `DisableParametersWithNestedArrays` | irrelevant on 10.1.37: the fix it switches off is absent (M-08) | g2 (c) |
| `ProcessParameterCollections` | 1, server and client; the builder does not depend on it (Q3), EM2's nested rows do | r9 §4 (server row); client [traced, r2 §3] |
| `AutoAddPackageDependenciesInProcesses` | on, client; the designer service list unfiltered | measured 2026-11-03 (M-17) |
| `UsrWsc*` | the section 7 objects only | section 7 |

**If not:**

| Outcome | What changes |
|---|---|
| `DisableIgnoreErrorsInWebServiceUserTask` enabled | the designer hides the checkbox (EM2 cannot set IX4 there) and the run time ignores IX4 (EM7 cannot prove it); tell the owner before EM2; the KB article names the switch |
| client `ProcessParameterCollections` off | the designer emits flat composites with no `L18` (r2 §3; the open defect ENG-68545 "No data binding exists for ProcessParameterCollections feature"), while the builder still emits nested items (Q3): EM2 is then not an oracle for the nested rows, which come from the corpus (pattern A); ask the owner whether to capture with the client feature on (a feature-state change, rule 13) |
| the session clio is stale | rebuild or switch clio before any describe or builder probe |

**Pass:** every row read and recorded in `CAP/em0/baseline.md`. **Fail:** a read path does not work; the EM that
needs it gets another route before it runs. **Cleanup:** none.

### EM1 Create the test REST service

| | |
|---|---|
| Question | Can Studio hold every shape the element must handle, and what does it store for each (`IU1`-`IU7`, `IU6` nesting, flattened single objects)? |
| Settles | the input every capture is built from; r9 §6 (no composite request body on the stand) |
| Gate | day 0, after EM0 (EP is settled: E4) |
| Preconditions | EM0 done; RB-4 shows only the section 7 objects |
| Writes | 1 save per service (`UsrWscEchoService` extended, `UsrWscDriftService`, `UsrWscDeadService`); each with the owner's yes |
| Owner | logs in to Chrome; approves each save (3) |

**Measured 2026-11-03 (partial: step 1 and the header of step 5; M-19, decisions Q16).** `UsrWscEchoService`
("WSC echo service", `2ac0b905-591d-4623-a8bf-990846cea7d3`, `Custom`, base URI = the stand root) exists with one GET
method `UsrWscGet` (`/UsrWscGet`) and one header parameter `UsrWscProbe` (header `X-Wsc-Probe`, not required, default
Const `wsc-probe`). Stored: `IU1 "X-Wsc-Probe"`, `IU2 Text`, `IU4 4`, `IU5 {IV1 "wsc-probe", IV2 1}`, as traced. A
designer element on it stores `UsrWscProbe_In` with an empty `L8`; `wsc-probe` occurs nowhere in the process, and
today's describe shows no unbound input (M-21; bound inputs come back, M-31). Studio refuses a parameter "Code in Creatio" without the `Usr` prefix
("Code of the method must contain prefix "Usr""), so every parameter code in this file is `UsrWsc...` (element
params `UsrWsc..._In` / `_Out`). Not measured: what the run time sends for an unbound header with a default (needs an
echo, which E4 lacks) and `authInfo` (no auth block; the agent does not type credentials). The remaining steps extend
this service and create the other two.

**Measured 2026-11-03, the rest (M-23; `CAP/em1/`, owner's yes for 3 saves, then 2 more saves approved for EM3 and
EM7).** Route: Quick setup "Example in JSON" for every body and response (step 3's alternative); JSON pasted, request
`{"UsrWscText":"t","UsrWscReq":"r","UsrWscFlag":true,"UsrWscIds":[1,2],"UsrWscOrders":[{"UsrWscOrdNo":"A-1",
"UsrWscLines":[{"UsrWscSku":"S-1","UsrWscQty":1}],"UsrWscTags":["x"]}],"customer":{"name":"n","city":"c"},
"UsrWscStatusIn":"open"}`, response `{"UsrWscEchoOrders":[{"UsrWscEchoOrdNo":"A-1","UsrWscEchoLines":[{"UsrWscEchoSku":
"S-1","UsrWscEchoParts":[{"UsrWscEchoPart":"p"}]}]}],"UsrWscEchoIds":[1,2],"UsrWscEchoText":"t","UsrWscEchoQty":1,
"UsrWscEchoOk":true,"UsrWscStatus":"s"}`. Quick setup generates abbreviated codes (`UsrUWText`, `UsrUWOUWTags`); every
code was replaced by the planned `UsrWsc...` one and Name set to "Reference" / "Status" where section 3.2 asks.
Substitution: the `IU1` paths are the codes (`$.UsrWscText`, nested `UsrWscOrdNo`), not `$.orders` / `ordNo`; the
element stores only the code (`L17`), so no capture sees a path. Header, Query and URL-segment parameters were added
by hand (the segment through the wand next to "Method address").
- Stored as the key map traced (r9 §2.1): object arrays `IU2` Object + `IU3` true + `IU6`; scalar arrays `IU3` true;
  the third response level is `IU6` in `IU6` in `IU6`; the header `IU5 {IV1 "wsc-probe", IV2 1}` (now on both
  methods); every other parameter `IU5 {IV1 "null"}` (no `IV2`), except the Query `UsrWscQ` (`{IV1 "null", IV2 1}`).
- Studio facts: Is array is forced on (disabled) for Object and Object has no Required box; the single object
  `customer` becomes two scalars named "Customer Name" / "Customer City" with `IU1` `$.customer.name` / `$.customer.city`
  (C-08; no non-array Object is offered); a URL segment's Required is forced on (`IU7` true; serialization §2.3's
  "L6 exactly when no default" therefore always gives `L6` for a UI-made segment); a Query is Required by default;
  a GET method offers no Body parameter type; two parameters may share a caption ("Reference", "Status").
- Service resources: one `ServiceMethods.<m>.{Request|Response}.Parameters.<path>.Caption` key per parameter, nested
  paths through `.ItemProperties.`, each key stored twice for en-US (two `SysLocalizableValue` rows).
- `UsrWscDriftService` (`7dd8d33d-b760-4fb1-bec0-ec14c4ae7c83`): `UsrWscDrift` (Query `UsrWscA`, response `UsrWscB`),
  `UsrWscTc12` (`UsrWscE` / `UsrWscF`), and, added for EM3 route (b) on the owner's choice, `UsrWscOrders2` (GET
  `/UsrWscOrders2`, response `UsrWscOrd2 [ {UsrWscOrd2No, UsrWscOrd2Lines [ {UsrWscOrd2Sku} ]} ]`, two object levels).
  `UsrWscDeadService` (`69899196-4f2b-4151-94bf-91c9c9600345`): base URI `http://127.0.0.1:1` (owner's pick), method
  `UsrWscDead` GET, no parameters.
- Found on the way: "New web service" asks for an address and splits it at the first `_` of a host name
  (base URI `http://<host up to the first _>` + method address `<rest of the host name>`); the URI and the auto-made method were corrected by hand.
- `UsrWscMissing`'s address changed during EM7 from `/UsrWscNoSuchRoute` to `/UsrWscNoSuchRoute.js` (owner's yes): the
  stand root answers an unknown route with 302 -> login page, a static path with 404 (M-28). Its parameters did not
  change; no process element uses `UsrWscMissing` except EM7's.
- Step 8 (Send test request) not done.

**Steps (Studio, in Chrome):**
1. Done 2026-11-03: `UsrWscEchoService`, REST, URI = the stand root, package `Custom` (r7 §3 lists the fields)
   [doc]; step 7 reads back what was saved.
2. Add method `UsrWscEcho`: code, address `/UsrWscEcho`, request type POST, content JSON, timeout 5000 ms, "Use
   authentication" off.
3. Request parameters, one by one, as section 3.2: Body `UsrWscText`, `UsrWscReq` (Required), `UsrWscFlag` (Boolean, not
   Required), `UsrWscIds` (Integer, Is array, Required), `UsrWscOrders` (Object, Is array) with nested `UsrWscOrdNo` (caption
   "Reference"), `UsrWscLines` (Object, Is array, nested `UsrWscSku`, `UsrWscQty`), `UsrWscTags` (Text, Is array); `UsrWscCustName` with request code `customer.name` and `UsrWscCustCity`
   `customer.city` (record whether Studio keeps a dotted path, makes a `$.` JSONPath, or offers an Object without
   Is array: the Academy says it refuses, r7 §3); `UsrWscStatusIn` caption "Status"; Header `UsrWscProbe`, header name
   `X-Wsc-Probe`, default value Const `wsc-probe`. If the quick-setup "from JSON example" route is used instead,
   record that and the JSON pasted (r7 §3).
4. Response parameters as section 3.2: `UsrWscEchoOrders` (`$.orders`, Object, Is array) with nested `UsrWscEchoOrdNo`,
   `UsrWscEchoLines` (nested `UsrWscEchoSku`, `UsrWscEchoParts` with `UsrWscEchoPart`), `UsrWscEchoIds` (Integer, Is array),
   `UsrWscEchoText` (caption "Reference"), `UsrWscEchoQty`, `UsrWscEchoOk`, `UsrWscStatus` caption "Status".
5. Method `UsrWscGet` (exists with its header): change the address to `/UsrWscGet/{UsrWscId}`, add UrlSegment
   `UsrWscId` with no default and Query `UsrWscQ`, and the response scalars; add `UsrWscMissing` as section 3.2.
   Save (write 1).
6. `UsrWscDriftService` (methods `UsrWscDrift`, `UsrWscTc12`), save (write 2). `UsrWscDeadService`
   (method `UsrWscDead`), save (write 3).
7. RB-3 on each service; save the decoded `MetaData` to `CAP/em1/<service>.json`, `IQ1` redacted.
8. Optional, read-only in Studio: "Send a test request" on `UsrWscGet` (r7 §3: since 7.18.0) to see what the stand
   root answers. It is the ServiceClient path, not the process path (verdict r1-C7), so it proves reachability
   only. Not on a shipped service.

**Expected:**

| Observation | Expected | Basis |
|---|---|---|
| key map | `IQ1` BaseUri, `IQ2` 1 (Rest), `IQ4` methods; per method `IR1` request, `IR2` response, `IR3` timeout; request `IT2` 2 (POST) or 1 (GET); parameter `IU1` path, `IU2` type name, `IU3` IsArray, `IU4` kind (1 Body, 2 UrlSegment, 3 Query, 4 Header), `IU5` default, `IU6` nested, `IU7` required | r9 §2.1 [traced in core, confirmed on 3 shipped services] |
| object arrays | `IU2` Object, `IU3` true, `IU6` holding the nested parameters with relative paths | r2 §3.1 [corpus] |
| single object | flattened into scalars with a dotted / JSONPath `IU1`; no non-array Object parameter | C-08; verdict r1-C8 |
| header default | `IU5` = `{IV1 "wsc-probe", IV2 1}` (Const) | measured 2026-11-03 on `UsrWscGet` (M-19) |
| the 3rd response level | stored like the 2nd (`IU6` inside `IU6`) | the Gemini service has 3 levels (r9 §2.2) |

**If not:**

| Outcome | What changes |
|---|---|
| Studio refuses a request object array, a nested array or `Required` on an array | the shape is not producible by a user; drop it from EM2 and from the parity fixtures, record it in [usage](eng-92710-call-web-service-element-usage.md) |
| a single object is stored as a non-array Object parameter | the server provider maps it to the Object type (84ed6865, verdict r2-C5); Q6 / ENG-92730 "Complex / structured parameter types" scope note changes; the shaper (P-A2) must cover it |
| Studio rejects two parameters with the same caption | drop the duplicate-caption pairs ("Status", "Reference"); the suffix rule stays traced only and serialization §9 marks SQ3 not settled in v1 |
| Studio refuses the Boolean request parameter or the UrlSegment parameter | drop it; serialization §9 marks SQ5 (Boolean In) or the §2.3 UrlSegment row not settled in v1 |

**Pass:** the three services saved with every shape stored as above. **Artifacts:** `CAP/em1/*.json`, one
`CAP/em1/PROVENANCE.md` (date, versions from EM0, saving login's culture, EP E4). **Cleanup:** CU-2 at the end
of the campaign (section 7); `UsrWscEchoService` and `UsrWscDeadService` stay until the clio e2e evidence run and
the manual Jira cases no longer need them (plan CL-9 is Ignored when they are absent; TC-I-09 and manual TC-06 call
`UsrWscDeadService`, test plan).

### EM2 Designer capture A: the AC5 oracle

| | |
|---|---|
| Question | What does the current designer store for a Call web service element on `UsrWscEcho`, with every binding kind the builder will write? |
| Settles | Q3 (the shape the package-owned generator must produce, nested items always emitted), Q4 (the BK15 rows on this service; the row shape is measured, M-13), Q8 (`IX3` = seconds x 1000: already settled by source, verdict r7-A3; recorded as confirmation), IX4 storage, the " - Input"/" - Output" suffix, the default element caption; AC5 pattern B |
| Gate | day 0, before slice A's shaper (P-A2); the read-back (step 9) re-runs on the final cut |
| Preconditions | EM0 (client `ProcessParameterCollections` on, so the designer's nested rows are an oracle; IX4 checkbox visible), EM1 |
| Writes | 1 designer save (S1); S2-S4 (SOAP, the owner's request of 2026-11-05): one SOAP service, one process, three saves. Side effect of that session: an owner-approved `clio compile-configuration --all` ended with build result 1 (CS0006 in `UsrSrvVisitA1` / `UsrSrvVisitA2`, not ENG-92710 objects) and the stand header shows "Compilation error" since; later installs and runs must not blame a package cut for it |
| Owner | logs in; approves the save |

**Measured 2026-11-03, S1 (M-24; `CAP/em2/`).** Process `UsrWscEm2CaptureA` (`0ca3d151-afdb-482c-ab97-3e479eedc21e`),
element `WebService1` (`08ce0f5e-91e9-4e84-8444-3f4eb60679c2`); all parameter UIds in `CAP/em2/UsrWscEm2CaptureA-uids.md`.
Saved once, as the steps say (S2-S4, SOAP, ran 2026-11-05, see below). Every row of the table below matched; the open rows settle so:
- Element keys `BL1 BL3 BL7 BL8 BN2 BO2 BO3 IL2 IX1 IX2 IX3 IX4 BP2`, `IX3` 7000, `IX4` true, no `BP6`; the method
  was not auto-selected (three methods); the timeout field shows "Default (5 sec)" (the method's `IR3`) until set.
- `UsrWscFlag_In` (unbound Boolean In) is `L8 {"GS2":"false","GS5":<P>}`: SQ5 settled as inferred.
- Bound values: `PText` -> `GS1` 3 + token, `GS5` P, resource `.DisplayValue` `[#PText#]`; the formula -> `GS1` 3,
  `GS2` = the token text + ` + 1`, `.DisplayValue` `[#PCount#] + 1`; a Text constant -> `L8 {"GS1":1,"GS5":<P>}` with
  NO value in the metadata, the value only in the resources `.Value` and `.DisplayValue` (`A-1`, `open`).
- Nested shared caption: `UsrWscOrders_In.UsrWscOrdNo_In` and `UsrWscEchoText_Out` are both plain "Reference"; the
  "Status" pair is "Status - Input" / "Status - Output". The suffix counts top-level parameters only (SQ3 settled,
  nested scope excluded). EM4 S1 found the cost of this rule in the designer (M-27).
- Element caption "WSC echo service 1" (service caption + index, r2 §6), not the base URI.
- BK15: 4 schema-level rows in BP2 result order, keys `BL1 UId A2 A3 A4 GT2 GT3 GT5 GT1`, Success `GT1 {"GS2":"false"}`.
- Resources: `BaseElements.WebService1.Parameters.<A2 path>.Caption` for every parameter (nested by `.`), "Item value"
  on both ItemValue children, "Success", "Response body", "Http status code", "Request body".
- `UsrWscTagsItemValue_In` (not required) has no `L6`; `UsrWscIdsItemValue_In` (required array) has `L6` true, its
  parent none (C-06). The panel lists required inputs first.
Nothing in "If not" applies; the serialization file's oracle rule needs the Text-constant-in-resources row (P-B3 writes
`.Value` / `.DisplayValue` resources for Text constants) and the top-level-only suffix row.

**Steps (process designer, in Chrome):**
1. Process library > new process; code `UsrWscEm2CaptureA`, caption "WSC EM2 capture A", package `Custom`.
2. Process parameters: `PText` (Text), `PCount` (Integer).
3. Elements Start -> Call web service -> End. On the element: service "WSC echo service"; method `UsrWscEcho`
   (record whether it is auto-selected: a single-method service auto-selects, r2 §2; this one has three methods);
   "Maximum execution time, in seconds" = 7 (verdict r7-A3 has the label); ignore errors checked.
4. Bindings: `UsrWscReq` <- process parameter `PText`; `UsrWscStatusIn` <- constant `open`; `UsrWscText` left unbound;
   `UsrWscIds` item (`UsrWscIdsItemValue`) <- formula `[#PCount#] + 1` (container left unmapped); `UsrWscOrders` item
   `UsrWscOrdNo` <- constant `A-1` (container left unmapped: the one-item list of g3 §2); `UsrWscFlag`, `UsrWscProbe` and
   `RequestBody` left unbound (RequestBody is visible only in extended mode, r2 §3.2).
5. Open the element card again (rule 6), close it, save (S1).
6. RB-1 and RB-2; save to `CAP/em2/UsrWscEm2CaptureA-metadata.json` and `-resources.json`, redacted.
7. Record the element's UId, every parameter UId, and the process UId once.
8. S2-S4 (SOAP, Q20; run on the owner's request of 2026-11-05, results below): a SOAP service from a hand-written WSDL file (Studio
   accepts a WSDL file, r7 §3), one method, one element in a second process; compare the parameter shape with S1.
   S3: change only the timeout and save; S4: switch the element to `UsrWscEchoService.UsrWscEcho` and save.
9. Final cut only, read-only: RB-9 on `UsrWscEm2CaptureA`: the `webService` member and the stored parameter tree
   come back as Q14 says; the header default `wsc-probe` appears nowhere in the reply (Q14, Q16).

**Expected (S1):**

| Observation | Expected | Basis | Tag |
|---|---|---|---|
| element keys | `BL1` ProcessSchemaWebService, `BL7` 652b598d, `BO2` true, `BO3` true, `BP2`, `IX1` = the service's manager-item UId (`VwWebServiceV2.UId`), `IX2` `UsrWscEcho`, `IX3` 7000, `IX4` true; no `BP6` | g5 (a); r2 §2; C-07 | [traced] [corpus] |
| BP2 order | tagged IN in request order, tagged OUT in response order, then Success, Response, StatusCode, RequestBody | C-01; g1 (a) | [traced] [corpus] |
| every parameter, every level | fresh UId, `A3 = A4 = process UId`, `IL2 = element UId`, `L12` written, `L8 {}` unless bound; Boolean ones (`UsrWscEchoOk_Out`) `{"GS2":"false","GS5":<P>}` | C-05; 14/14 tagged Boolean params in the corpus (facts §3.3; serialization §2.4, I7) | [corpus] |
| `UsrWscFlag_In` (unbound Boolean In) | `L8 {"GS2":"false","GS5":<P>}` like the Out Booleans | serialization §2.4, SQ5 (no corpus instance) | [inferred]; settles SQ5 |
| tagged names | `A2 = <code>_In` / `<code>_Out`, `L17 = <code>`, nested items too | g1 (b) | [corpus] |
| types | Text -> MaxSizeText c0f04627; Integer 6b6b74e2; Boolean 90b65bf8; object array -> CompositeObjectList 651ec16f with recursive `L18`; scalar array -> ObjectList 4b51a8b5 with one `<code>ItemValue_<dir>` child, Tag `<code>ItemValue` | r2 §3; C-06 | [traced] [corpus] |
| `L6` | `UsrWscReq_In` true; `UsrWscIds_In` absent, `UsrWscIdsItemValue_In` true; never on `_Out` | C-06; verdict r9-S2 | [corpus] |
| flattened fields | `UsrWscCustName_In`, `UsrWscCustCity_In` as top-level scalars | C-08 | [corpus] |
| result params | no `L17`, `L6`, `L18`; Success Boolean Out `L8 {"GS2":"false","GS5":<P>}`; Response Text 8b3f29bb Out; StatusCode Integer Out; RequestBody Text In `{}` | C-02, C-03; g1 port checklist | [corpus] |
| BK15 | 4 rows, `A2` = element name, `A3 = A4 = P`, `GT2 = [Element:{el}].[Parameter:{GT3}]`, random `GT5`, no `GT4`, Success `GT1 {"GS2":"false"}`, others `{}`; order = BP2 | C-04; M-13 (the same keys on the Marketplace probe) | [corpus] [measured 2026-11-03, another service] |
| bound values | `PText` -> `GS1` 3, `GS2 "[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{<PText>}]#]"`, `GS5` P; the formula -> `GS1` 3 with the formula text; the item constants on the items with the containers' `L8 {}` | g3 §2 (M6) | [traced]; the constant's `GS1` (1, ConstValue, or another) is recorded, not predicted |
| nested shared caption | `UsrWscOrders_In.UsrWscOrdNo_In` "Reference - Input" and `UsrWscEchoText_Out` "Reference - Output" if the suffix counts nested items; plain "Reference" on both if it does not: record which (it pins the suffix scope) | serialization §4 suffix row, SQ3 (`ChangeId_Out` = "Id - Output" suggests yes) | open; settles SQ3 (nested scope) |
| resources | `BaseElements.<El>.Parameters.<A2 path>.Caption` for every parameter; "Status - Input" and "Status - Output"; "Item value" on ItemValue children; "Success", "Response body", "Http status code", "Request body"; `.DisplayValue` rows for the bound params | g1 caption resources; r2 §3 | [traced] [corpus] |
| element caption | "WSC echo service 1" (r2 §6) or the base URI + index (r9 §3.2 on Bing): record which | r2 §6 vs r9 §3.2; a service change onto this service gave "WSC echo service 1" (M-20) | open for a new element |

**If not:**

| Outcome | What changes |
|---|---|
| no `L18` under `UsrWscOrders_In` or no ItemValue child | the client feature is off (EM0; ENG-68545): the builder still emits nested items (Q3) and the nested oracle rows come from the corpus; otherwise the rule changed: the shaper follows EM2, not the corpus; the serialization file is updated |
| no RequestBody, or a different result-param order | the shaper and P-B4 follow EM2 (the current designer), not the 2021-2022 corpus |
| BK15 rows absent or of another shape | it differs from M-13 for this service: the shaper follows EM2 (Q4 = A writes what the designer writes) and the owner is told |
| the suffix is missing on the "Status" pair | the duplicate-caption rule is not in the current designer; the shaper drops it |
| `IX3` is not 7000 | Q8's unit is wrong: stop, re-read the page (verdict r7-A3 says seconds x 1000) |
| `IX4` absent with the box checked | the stand's designer does not store it: Q7 changes before slice B |

**Pass:** S1 recorded; every row above compared and the differences written into the serialization file's oracle
rule. **Artifacts:** `CAP/em2/` (metadata, resources, UIds, `PROVENANCE.md` with date, versions, saving login,
culture, redacted keys). **Cleanup:** kept (rule 10) until the clio e2e evidence run (TC-I-07) and manual TC-11 no
longer need it; then CU-1.

**Measured 2026-11-05, S2-S4 (SOAP; M-32..M-35; `CAP/em2-soap/`).** Run on the owner's request (Q20).
- Step 0, read only: `DontUseSoap` has no `Feature` row, so it is off; Studio offers "SOAP service" (M-32).
- The service `UsrWscSoapService` (`197ff0c0-f168-4bbf-9db9-81f86102e40f`, SOAP 1.1, `Custom`, endpoint
  `http://localhost/UsrWscSoap`, never called) was entered by hand in the SOAP card: the WSDL wizard did not load. The
  card forces every Object parameter to an array, so a non-array complex type is unmeasured. Method `UsrWscSoapEcho`:
  in a Text, a required Integer, a required Boolean, a Text array and an Object array with a nested Text; out a Text
  and an Object array with a nested Text.
- S2 (OF-16), process `UsrWscEm2SoapS2` (`dbc77d7c-8f37-446b-8a07-d77c767804e4`): the element equals S1 under WSCR on
  every point of the S1 table (keys, `IX1`-`IX4`, names, tags, nesting, types, `L6`, `L12`, result params, BK15 rows,
  default caption, parameter captions, "Item value"); `BL11:false` is AD10 (the required Integer left unbound). Side
  findings: a Boolean constant "True" is Script `{GS1: 3, GS2: "[#BooleanValue.True#]"}`, and a formula on a Text
  item needs `.ToString()` (M-33).
- S3: timeout 7 -> 9 changes `IX3` alone; the schema's `BK34` went `en-US` -> `null` on this first re-save (M-34).
- S4: the switch to `UsrWscEchoService.UsrWscEcho` is a full rebuild: every parameter and row replaced, `IX3`
  dropped, `IX4` and `GT5` kept, caption renamed, no SOAP leftover; the new tree equals S1 apart from UIds (M-35).
- Consequence: decisions Q20 D (SOAP built like REST, N10). The package's create (S2) and switch (S4) are compared with
  these captures in its parity suite (test plan TC-P-15, TC-P-16).

### EM3 Designer capture B: the response read into a process Collection

| | |
|---|---|
| Question | How does the designer bind a composite web-service output into process parameters: its own "create parameter from element" route (a), and a hand-made Collection with item mappings (b)? |
| Settles | Q6 (settled by route (a): v1 = declared Collection with item mappings; the mirror stays with ENG-92730); route (b): the bytes a declared-Collection binding must equal (parity pattern B) |
| Gate | route (a) done; route (b) day 0, before slice C's describe of response mappings |
| Preconditions | EM1; EM2 done (same session is fine) |
| Writes | 1 designer save (route (b)) |
| Owner | approves the save |

**Measured 2026-11-03, route (a) (M-15, decisions Q6).** Process `UsrWscEm3Probe` (`14825024-d8e7-4322-b43a-edf23939a73f`,
`Custom`), element on `GeminiApiSearch.GenerateContent`, Parameters > Add parameter > "Create from element", only
`Candidates` ticked. The designer creates a CompositeObject wrapper `WebService1Results1` (type `632e4371`, Tag
`SourceElement:WebService1`, no value of its own) holding a Collection `WebService1Candidates_Out` (Tag
`WebService1.Candidates_Out`, bound `[#...[Element:{ws}].[Parameter:{Candidates_Out}]#]`) whose items are only the
first-level scalars (`CRole`, `CFReason`, `SEPRContent`, `CIndex`), each bound to the element item. The nested
collections of the element tree (`CParts_Out > PText_Out`, ...) are not offered and not created. So the designer's own
"mirror" is the CompositeObject flat shape (element-prefixed names, `<Element>.<Param>` Tags, one wrapper level,
nested lists dropped), which needs a declarable CompositeObject (ENG-92730); Q6 = A was agreed on it. Route (a) is not
repeated on `UsrWscEcho`.

**Measured 2026-11-03, route (b) (M-25; `CAP/em3/`).** Not runnable as written: for a declared "Collection of
records" `Orders`, the container's value picker (Process parameter > Process elements) offers no web-service object
array at all: on `UsrWscEcho` it listed only `UsrWscEchoIds`, `UsrWscIds` and `UsrWscOrders > UsrWscTags`, never
`UsrWscEchoOrders_Out`, whether `Orders` was declared with 2 or 3 levels. The echo service has no 2-level output, so
on the owner's choice a 2-level method `UsrWscDriftService.UsrWscOrders2` was added (EM1 block) and the capture was
taken on it: process `UsrWscEm3CaptureB` (`fc2d199a-0715-4892-8784-3a9db67fc519`), element `WebService1`
(`8d238493-8b22-47c2-be35-7e59bb82991a`), `Orders {OrdNo, Lines {Sku}}`.
- The container picker still says "There are no parameters of required type" for `UsrWscOrd2_Out`. The working route is
  item-first: binding `Orders.OrdNo` <- the item `UsrWscOrd2No` (the item picker shows the output tree) bound the
  container `Orders` to `UsrWscOrd2_Out` by itself, and binding `Orders.Lines.Sku` <- `UsrWscOrd2Sku` bound
  `Orders.Lines` to `UsrWscOrd2Lines_Out`.
- Stored (as the expected row, item names differ, mapping by UId): `Orders` type 651ec16f (CompositeObjectList, the
  element's own type; the UI's "Collection of records" is not the ObjectList 4b51a8b5), `OrdNo` / `Sku` 5ca35f10
  ("Text (500 characters)"); every level `L8 {"GS1":3,"GS2":"[#[IsOwnerSchema:false].[IsSchema:false].[Element:{ws}].
  [Parameter:{uid}]#]","GS5":<P>}`; process parameters carry only `UId A2 L1 L8` (no `L17`, `L12`, `L6`).
- Resources: `Parameters.Orders.DisplayValue` `[#WSC drift service 1.UsrWscOrd2#]`, nested items with a `:` path
  (`[#WSC drift service 1.UsrWscOrd2:UsrWscOrd2Lines:UsrWscOrd2Sku#]`).
- Level 3: not bindable through the container picker (no object-array output is); whether an item of a 3-level output
  can be bound item-first was not tried (Q6 keeps it out of v1).
- The element (no inputs, no timeout, no ignore-errors set) stores no `IX3` / `IX4`; caption "WSC drift service 1".
What changes: nothing in Q6 (the declared-Collection route exists in the designer, item-first); the KB article and the
manual cases describe the item-first route, and slice C's describe of response mappings and the builder's write both
bind the container as well as its items, as the designer does.

**Steps (route (b)):**
1. New process `UsrWscEm3CaptureB`, one element on `UsrWscEcho` as in EM2 (required inputs bound to constants).
2. Route (b): process parameter `Orders`, Collection of records, items `OrdNo` (Text) and `Lines` (Collection of
   records, item `Sku`, Text); bind `Orders` <- `UsrWscEchoOrders_Out`, `Orders.OrdNo` <- `UsrWscEchoOrders_Out.UsrWscEchoOrdNo_Out`,
   `Orders.Lines` <- `...UsrWscEchoLines_Out`, `Orders.Lines.Sku` <- `...UsrWscEchoLines_Out.UsrWscEchoSku_Out`. Record
   whether the window lets `UsrWscEchoParts_Out` (level 3) be bound into anything.
3. Open the card, save. RB-1, RB-2 to `CAP/em3/`.

**Expected:**

| Route | Expected | Basis | Tag |
|---|---|---|---|
| (a) | measured: see above | M-15 | [measured 2026-11-03] |
| (b) | every level `GS1` 3, `GS2 "[#[IsOwnerSchema:false].[IsSchema:false].[Element:{<ws>}].[Parameter:{<out>}]#]"`, `GS5` = process UId; item names differ from the element's (mapping by UId); items have no Tag | g3 §4 (the Gemini `Candidates` shape) | [corpus] [traced write] |
| level 3 | not bindable into a two-level Collection | g3 §4 (declared depth 2; P1 "source deeper" refusal) | [traced] |

**If not:**

| Outcome | What changes |
|---|---|
| route (b) stores items with Tags or another token form | the declared-Collection parity rows follow EM3 |
| level 3 is bindable | record how; it stays out of v1 (Q6) unless the owner pulls it in |

**Artifacts:** `CAP/em3/`. **Cleanup:** `UsrWscEm3CaptureB` kept with EM2 (never run, rule 10), CU-1 at the end;
`UsrWscEm3Probe` CU-1 at the end (section 7).

### EM4 Designer capture C: changing the method and the service

| | |
|---|---|
| Question | What does the designer clear, keep and regenerate when the method (and then the service) of an existing, bound element changes? |
| Settles | Q11 (agreed A, replace; whether the retarget guard mirrors what the designer does to dependents; if the designer keeps result-param bindings, A keeps them too) |
| Gate | day 0, before slice D (P-D1) |
| Preconditions | EM1 |
| Writes | 3 designer saves (S1-S3) |
| Owner | approves each save; the designer's confirm dialogs are answered by the lead with the owner watching |

**Measured 2026-11-03, a service change only (M-20, decisions Q11).** On `UsrProcess_3389428` (the EM10 process, an
element with nothing mapped), switching the element from `GeminiApiSearch` to `UsrWscEchoService` asked "Are you sure
you want to change element schema? All data will be lost.", then replaced every parameter; Success / Response /
StatusCode came back with NEW UIds, 4 BK15 rows remain, the caption became "WSC echo service 1", the element name
stayed `WebService1`. Not shown there: a method change (S2), and what a service change does to bindings, to dependent
elements and to `IX3` (the element had none of them); S3 below covers those.

**Measured 2026-11-03, S1-S3 (M-26, M-27; `CAP/em4/`).** Process `UsrWscEm4MethodChange`
(`6a9bed7b-1f82-4df6-a053-7dc0887fa926`), element `WebService1` (`74e2d640-...`), as the steps say, plus a required
`UsrWscIds` item bound to constant 1. Four saves, not three: the first S1 (S1a) bound the Formula task to the wrong
parameter (M-27, below); it was rebuilt on "Status - Output" and saved again.
- S1a: in the formula window, double-clicking the output "Reference" (`UsrWscEchoText_Out`) wrote a token to the
  nested INPUT `UsrWscOrdNo_In`, captioned "Reference" too (no suffix on nested items, EM2); the save answered "Schema
  has errors: The input parameter "UsrWscOrdNo_In" of the element "WebService1" cannot be used as a data source for the
  parameter "Body" of the element "FormulaTask1"" and stored the process; on reload the field read
  `[#WSC echo service 1.UsrWscOrders.Refere...`.
- S2 (method -> `UsrWscGet`): confirm "Are you sure you want to change method? All parameters will be lost."; every
  parameter replaced with new UIds (Success `f4a12bfa` -> `afa3a061`), the new tagged params in service order
  (`UsrWscProbe_In`, `UsrWscId_In`, `UsrWscQ_In`) then the 4 result params; `UsrWscId_In` `L6` true (URL segment) and
  `UsrWscQ_In` `L6` true (required Query); the 4 BK15 rows re-created with NEW row UIds but the SAME `GT5` values;
  `IX3` 7000 and `IX4` true kept; caption "WSC echo service 1" kept; the element itself got `BL11:false` (its required
  `UsrWscId` unbound, as planned). The Formula task (`CH1`) and the conditional flow (`CI3`) keep the removed UIds and
  get no `BL11`; the save warns "Required fields ... are not filled in" (SAVE PROCESS), then answers "Schema has errors:
  The "ConditionalSequenceFlow1" element has an invalid value for the parameter "ConditionExpression" ... ParameterUId
  f4a12bfa" and stores the process.
- S3 (service -> "WSC drift service"): confirm "Are you sure you want to change element schema? All parameters will be
  lost." (M-20 recorded "All data will be lost": today's wording is "All parameters"); the method picker is empty
  (three methods); the timeout returns to "Default (5 sec)" and `IX3` is omitted; `IX4` true kept; caption renamed
  "WSC drift service 1", name `WebService1` kept; parameters and BK15 rows as in S2 (new row UIds, same `GT5`); the
  dependents still dangle, with the same two save messages.
Against the expected table: S2 matches except "the dependent element marked invalid (`BL11:false`)": the designer does
not mark dependents; it leaves dangling tokens that only the save-time validation reports, and saves anyway. `IX3` /
`IX4` on a method change: kept (now measured). S3 matches, plus `IX4` survives a service change. Per "If not",
"dependents are not marked invalid -> the retarget guard is stricter than the designer; the owner confirms Q11 stays":
asked in decisions Q11, answered the same day: A3 (`Success` / `Response` / `StatusCode` keep their UIds, the guard covers tagged
outputs only). Same-named bindings: none could survive (no same-named params between the methods).

**Steps:**
1. New process `UsrWscEm4MethodChange`: Start -> Call web service (`UsrWscEcho`, timeout 7 s, ignore errors on, `UsrWscReq`
   <- constant, `UsrWscOrders.UsrWscOrdNo` <- constant) -> Formula task setting a process parameter from `UsrWscEchoText_Out`
   -> conditional flow on `Success` -> End. Open the card, save (S1). RB-1, RB-2.
2. Change the method to `UsrWscGet`; record the confirm text ("All parameters will be lost", r2 §6); bind `UsrWscQ`,
   leave the UrlSegment `UsrWscId` unbound; save (S2). RB-1, RB-2. (EM2's element is on `UsrWscEcho`, so the
   UrlSegment `L6` row is read here, not in EM2.)
3. Change the service to "WSC drift service"; record the confirm, the method picker state, the element caption
   after the change; pick `UsrWscDrift`; save (S3). RB-1, RB-2.

**Expected:**

| Save | Expected | Basis | Tag |
|---|---|---|---|
| S2 | every parameter of the element gone (tagged and result) with its BK15 row; new tagged params with fresh UIds for `UsrWscGet`; 4 fresh result params and 4 fresh BK15 rows; the bindings to the old params gone; the Formula task and the condition that read `UsrWscEchoText_Out` / `Success` left pointing at removed UIds and the dependent element marked invalid (`BL11:false`); `IX3` and `IX4` kept; element caption unchanged | `clearParamters` + `synchronizeParameters` (r2 §2, §6); `invalidateDependentElements` (r2 §6; g4 §1) | [traced]; whether `IX3`/`IX4` survive a method change is not traced beyond "only a service pick resets the timeout" (r2 §2) |
| S2, UrlSegment | `UsrWscId_In` `L6` true (a UrlSegment without a default is recomputed as required on read) | serialization §2.3 (`Metadata/ServiceParameter.cs:230-235`) | [traced]; settles the §2.3 UrlSegment row |
| S3 | `IX2` cleared then set to `UsrWscDrift`; `IX3` reset to 0 (omitted) by the service pick; element caption renamed to "WSC drift service <N>"; params as for S2 | `onAfterSchemaChanged` (r2 §2, §6); M-20 (confirm, full replacement with new result-param UIds, 4 rows kept, caption renamed, name kept) | [traced]; [measured 2026-11-03] for the M-20 parts |

**If not:**

| Outcome | What changes |
|---|---|
| bindings to same-named params survive a method change | Q11's "replace" is wrong for the designer: P-D1 keeps same-named bindings, the notice lists only dropped ones |
| dependents are not marked invalid | the retarget guard (refuse when other elements reference removed outputs) is stricter than the designer; the owner confirms Q11 stays |
| `IX3` / `IX4` reset on a method change | P-D1 follows the designer |

**Artifacts:** `CAP/em4/` (S1-S3; the M-20 reads of `UsrProcess_3389428`). **Cleanup:** CU-1 at the end (never
run); `UsrProcess_3389428` goes with EM10 (section 7).

### EM5 STOP gate: the designer opens and re-saves a builder-made element

| | |
|---|---|
| Question | Does the builder's element equal capture A, does the designer open and re-save it without a rewrite, and does an unrelated builder modify leave a web-service element unchanged? |
| Settles | Q3 (package-owned generator; the platform sync is a no-op on the correct shape), Q4 (rows not duplicated or rewritten on a builder-made element; measured for a designer-made one, M-13), Success `L8` kept; r3 open 4 (does a server load overwrite the " - Input" suffix) |
| Gate | STOP gate right after slice B, on the experimental cut; re-run on the final cut |
| Preconditions | the experimental cut installed (owner's yes, rule 3); the session clio is the feature build (rule 8); EM2 recorded |
| Writes | 1 install; 1 builder create; 1 designer save; 1 designer copy of EM2 (`UsrWscEm5OracleCopy`); 2 builder modifies; optional 1 designer copy-paste save. `UsrWscEm2CaptureA` is only read (rule 10) |
| Owner | approves the install (site restart) and each write; reads the diff if the gate fails |

**Steps:**
1. Builder create `UsrWscEm5Twin`: the same process parameters, elements and bindings as EM2, the element as a
   `webService` block (`service`, `method`, `timeoutSeconds: 7`, `ignoreErrors: true`) and the bindings through the
   mapping surface on the stored names (`UsrWscReq_In`, `UsrWscOrders_In.UsrWscOrdNo_In`, ...) in the same request (Q2,
   Q10). Record the notices and the parameter tree in the answer.
2. RB-1, RB-2. Diff the twin's element against EM2 S1 under the serialization oracle rule (UIds not compared).
3. Open the twin in the designer, open the element card (rule 6), close it, save. RB-1, RB-2. Diff against step 2.
4. Optional row-less variant: in the designer, copy and paste the twin's element, open the pasted card, save. RB-1:
   does the save add rows for the pasted element? The Q4 probe already showed it for a designer-made element (the
   copy gets no rows and keeps none after a re-save, M-13); the step only repeats it on a builder-made one.
5. On the owner's yes, copy `UsrWscEm2CaptureA` in the designer to `UsrWscEm5OracleCopy` (open the element card
   before the save, rule 6). RB-1: the copy's baseline, diffed against EM2 S1 under the oracle rule (UIds not
   compared). Then builder `modify-business-process` on `UsrWscEm5OracleCopy` changing only the End event caption.
   RB-1. Diff the web service element against the copy's baseline. `UsrWscEm2CaptureA` is not touched.
6. The same modify on the twin. RB-1. Diff against step 3.
7. Final-cut re-run only, read-only: RB-9 on the twin (describe round trip, Q14) and a builder modify of the twin
   with its own describe output as input writes nothing (plan DoD, the round-trip row).

**Expected:**

| Step | Expected | Basis | Tag |
|---|---|---|---|
| 2 | equal under the oracle rule, BK15 included (Q4 = A) | P-B3, P-B4 target EM2's bytes | design |
| 3 | no change: the page matches params by name and keeps UIds; result params are matched by NAME, so none is added; existing rows are kept; Success `L8` kept; captions may be rewritten in the saving login's culture | `synchronizeDynamicParameter` by name, `initMappings` (g1 (c)); C-03 | [traced] |
| 4 (optional) | the pasted element stays row-less after the save: rows are created only when a result param is missing by name | g1 (c); M-13 | [measured 2026-11-03] for a designer-made element |
| 5, 6 | the web-service element byte-equal before and after: the server sync matches by Name and keeps UIds, `L18` and untagged params, and overwrites caption, type, required flag only where they differ (none on a designer-correct shape) | g4 §1; r3 §7 | [traced]; the caption suffix is the open point: `SynchronizeParameterCaption` may drop " - Input"/" - Output" (r3 open 4) |

**If not (STOP rule, plan §0):**

| Outcome | What changes |
|---|---|
| step 2 differs | slice B fixes the shaper before slice C; the diff goes into the serialization file |
| step 3 rewrites params, duplicates a result param or a BK15 row, or drops Success `L8` | work stops; the diff goes to the owner; Q4 (and possibly Q3) is re-opened; the estimate is redone before slice C |
| step 4 adds rows | rows are self-healing in the designer: Q4 can still emit them, and the row-less variant becomes an accepted shape |
| steps 5-6 drop the suffix | the platform load normalizes captions: the shaper writes what the load leaves (or the suffix is excluded from the oracle rule), so the next load is a no-op |
| steps 5-6 change anything else | Q3's "platform sync tolerated afterwards" is wrong for that field: the shaper follows the platform for it |

**Pass:** steps 2-6 as expected, or each difference accepted by the owner and written into the oracle rule.
**Artifacts:** `CAP/em5/` (twin and copy metadata before and after each step, diffs, the notices). **Cleanup:** the
twin stays for the final-cut re-run, then CU-1; `UsrWscEm5OracleCopy` goes with CU-1 after step 5.

**Measured 2026-11-04, experimental cut 1.6.6.137 (STOP gate: passed for steps 1-3 and 6; step 5 not run; `CAP/em5/`,
`PROVENANCE.md` there; owner's yes for steps 1, 2 and 3 of the EM5 run).** Cut: crt-process-builder branch
`feature/ENG-92710-call-web-service-element`, stamp commit `11f90c64` (slices A and B, not pushed), claimed in the ENG-92710
claim comment as experimental, stand-only, superseded; installed with `push-pkg` (the install-log fetch answered an IIS 500
page; `SysPackage` records 1.6.6.137 and the site answered ping). Builder calls went straight to the package
(`clio call-service` -> `rest/ProcessDesignService/BuildProcess` / `ModifyProcess`), because clio's key check does not know the
`webService` block yet; reads RB-1 / RB-2 through DataService SelectQuery.

- Step 1: `UsrWscEm5Twin` (`060da1eb-5f49-4034-ae4d-1f21ce493637`, `Custom`), EM2's parameters, elements and three of its
  four bindings in one `BuildProcess`; success, no warnings. The fourth binding (the formula `PCount + 1` on
  `UsrWscIds_In.UsrWscIdsItemValue_In`) needed a second call (`addMapping`): a mapping `expression` accepts a process parameter
  only as its meta-path UId (`namesAccepted: false`), which exists only after the build.
- Step 2 (vs EM2 S1, UIds compared by role): the element node has 0 differing paths (BP2 order, names, tags, types, `L6`,
  `L18`, `ItemValue` children, Boolean `L8`, result params, IX1-IX4) and the 4 BK15 rows are equal (key set, Success `GT1`
  without `GS5`); the element caption "WSC echo service 1" equal. Differences, all known: `BL8` absent on every builder
  element (generic, W12); a bound process parameter's `.DisplayValue` is `PText` where the designer writes `[#PText#]` (AD3);
  the formula's `.DisplayValue` row is not written (AD3). Captions after the build: "Status - Input" / "Status - Output"
  as EM2; after the second (formula) call both read "Status" (the load sync, below).
- Step 3 (designer opened, element card opened and closed, Save; "Successfully saved"): element node and BK15 rows
  byte-identical to before (same parameter and row UIds, same `GT5`, same key order); Success `L8` kept; no duplicate or
  rewritten row. The designer rewrote resources only: the suffix put back ("Status - Input" / "Status - Output") and the two
  display values in its own form (`[#PText#]`, `[#PCount#] + 1`).
- Step 4 (optional): not run.
- Step 5: not run. The designer's Save menu offers "Save new version" and "Save current version" only; no copy route was
  found that does not open the read-only oracle `UsrWscEm2CaptureA` for editing (rule 10). `UsrWscEm2CaptureA` checked
  unchanged afterwards (ModifiedOn 2026-11-03 16:28:42.523). Owner's call whether to run it.
- Step 6 (unrelated builder modify of the twin): `setElement` carries no caption member, so the End-event caption change
  was replaced by `addParameter UsrWscEm5Marker` (Text), the unrelated modify EM8 used. Element node and BK15 rows
  byte-identical to step 3; the captions dropped the suffix again ("Status").

Verdict: the STOP conditions (a rewrite, a duplicate BK15 row, a lost Success `L8`) did not occur; Q3 and Q4 hold for a
builder-made element. r3 open 4 is answered: every server load of the process (any builder modify) removes the " - Input" /
" - Output" suffix from top-level captions and every designer save puts it back, so the stored caption flips with the last
writer. Per the "If not" table (steps 5-6 drop the suffix) the owner decides: the shaper writes what the load leaves, or the
suffix is excluded from the oracle rule.

**Owner's decision 2026-11-04:** the suffix is excluded from the oracle rule (serialization 8.3 AD1, now permanent for
stand captures too); the shaper keeps writing the designer's suffixed form, so a fresh builder build equals a fresh
designer save.

### EM6 Run time: a builder-made process calling `UsrWscEcho`

| | |
|---|---|
| Question | Does a builder-made element with composite bindings run, and do Success, StatusCode and Response arrive in process parameters? |
| Settles | AC4 run time for the result params. Reduced by EP E4 (Q17): the request bytes (the one-item list from an item mapping, a constant RequestBody replacing the generated body, r3 open 3's unmapped RequestBody null vs "", the header default sent) and the response read into a declared Collection (Q6 v1) are not measurable on stand `Creatio`; they rest on the core unit tests (r3 §4, §4.3) and g3 §2, §4 [traced], unless the owner adds a JSON source with the `$.orders` shape |
| Gate | claimed cut, before the PRs leave draft; no tag is pushed before it has run (plan §6) |
| Preconditions | the claimed cut installed; EM5 passed |
| Writes | 1 builder create, 1 run |
| Owner | approves each write and run |

**Steps:**
1. Builder create `UsrWscEm6Run`: process parameters `PText` (Text), `PCount` (Integer), `OutOk` (Boolean, Out),
   `OutCode` (Integer, Out), `OutBody` (Text, Out), `Orders` (Collection: `OrdNo` Text, `Lines` Collection: `Sku`
   Text). Element: `webService` `UsrWscEchoService` / `UsrWscEcho`; bindings `UsrWscReq_In` <-
   `PText`, `UsrWscStatusIn_In` <- constant, `UsrWscIds_In.UsrWscIdsItemValue_In` <- formula, `UsrWscOrders_In.UsrWscOrdNo_In` <-
   constant, `UsrWscCustName_In` <- constant; `RequestBody` unbound. Outputs: `OutOk` <- `Success`, `OutCode` <-
   `StatusCode`, `OutBody` <- `Response`, `Orders` <- `UsrWscEchoOrders_Out` with item mappings as EM3 route (b).
   RB-1: the `Orders` binding diffed against EM3 route (b) under the oracle rule.
2. Run: `run-process` with `resultParameterNames` `OutOk`, `OutCode`, `OutBody` (synchronous, M-11). RB-5, RB-6.
   Save `OutBody` (the stand's answer, redacted, rule 11) to `CAP/em6/run1-body.txt`.

**Expected:**

| Observation | Expected | Basis | Tag |
|---|---|---|---|
| status | `processStatus` 2: an HTTP answer completes the element whatever its code | r3 §4.6 | [traced] |
| result params | `OutCode` = the status the stand root answers for `POST /UsrWscEcho` (recorded, not predicted); `OutOk` = `200 <= OutCode < 400`; `OutBody` = the stand's response text | r3 §4.6 (`Success = 200 <= code < 400`) | [traced] |
| `Orders` | the stored binding equals EM3 route (b); no rows expected (the stand's answer has no `$.orders`) | EM3 | design |

**If not:**

| Outcome | What changes |
|---|---|
| RB-1's `Orders` binding differs from EM3 route (b) | slice C fixes the binding write before the PRs leave draft |
| a run fails | RB-5 `ErrorDescription` (M-12) goes into the record; the cause decides the fix |

**Pass:** the run as expected. **Artifacts:** `CAP/em6/` (process metadata, run id, result values, the response text,
RB-5/RB-6 reads). **Cleanup:** CU-1 for the process; logs stay (CU-6).

### EM6-B Final cut: a Boolean constant at run time (owner, 2026-11-05)

| | |
|---|---|
| Question | Does a Boolean input bound by the builder's `value: "true"` (stored ConstValue `{GS1: 1, GS2: "true"}`) hold `true` at run time, as the designer's Script form `[#BooleanValue.True#]` (M-33) does? |
| Settles | whether web service elements keep the shared ConstValue encoding for a Boolean constant (decisions, "Boolean constant at run time", owner 2026-11-05: A + B) |
| Gate | final cut, one run, with the EM2 / EM5 re-run |
| Preconditions | the final cut installed; `UsrWscEchoService` (`UsrWscEcho` has the Boolean body input `UsrWscFlag`) |
| Writes | 1 builder create, 1 user-property write (tracing on for the run's user: `SysSchemaUserProperty` `IsTracing`), 1 run |
| Owner | approves each write and the run |

Why a stand run: none of the 66 web service elements of the PackageStore corpus (38 files) has a Boolean input
parameter, so the corpus cannot answer it [measured: corpus]; under EP option E4 there is no echo (section 3.2), so the
request bytes are not read directly.

**Steps:**
1. (A) Builder create `UsrWscEm6Bool`: element `WebService1` on `UsrWscEchoService.UsrWscEcho` with
   `UsrWscFlag_In` <- `value: "true"`; after it a conditional flow on `[#WebService1.UsrWscFlag_In#] == true` to a
   marker element, and a default flow to another marker element. RB-1: `UsrWscFlag_In` `L8` is `{GS1: 1, GS2: "true"}`.
2. (B) Tracing on for the run's user (`IsTracing`) in the same session.
3. Run once (RB-5, RB-6).

**Oracle:**
- A: `SysProcessElementLog` shows which marker completed: the conditional one means the element held `true` at run time.
- B: the trace shows the element's parameter values and, if it records one, the request body. If the trace shows no
  body, the value-to-JSON step (`JsonRequestBodyRenderer`) is closed by a source trace and labelled [traced].

**If not:** only if A shows a value other than `true` do web service elements write the designer's Script form for a
Boolean constant; until then there is no code change (package test TC-P-15 pins the one-binding difference).

**Artifacts:** `CAP/em6b/`. **Cleanup:** CU-1 for the process; tracing switched back off; logs stay (CU-6).

### EM7 Run time: `ignoreErrors` true vs false

| | |
|---|---|
| Question | What do a 404 response and a failed connection do to the element and the process, with `IX4` true and false? |
| Settles | AC4 error handling; Q7 (agreed: `ignoreErrors` written directly as IX4; Success / StatusCode usable in conditions); the KB article's text on errors |
| Gate | designer half day 0; builder half on the claimed cut |
| Preconditions | EM0 (`DisableIgnoreErrorsInWebServiceUserTask` not enabled), EM1 (`UsrWscMissing`, `UsrWscDeadService`); no echo needed |
| Writes | 2 designer saves + 4 runs (day 0); 2 builder creates + 4 runs (claimed cut) |
| Owner | approves each write and run |

**Measured 2026-11-03, designer half (M-28; `CAP/em7/`, `PROVENANCE.md` there).** Two processes, one per `IX4` value,
each Start -> `UsrWscMissing` -> Formula `PRes1 = "ok=" + Success + " code=" + StatusCode` -> `UsrWscDead` -> Formula
`PRes2` (same) -> End: `UsrWscEm7Ignore` (`5fd83882-...`, both elements `IX4` true) and `UsrWscEm7Strict`
(`9a8691c1-...`, `IX4` omitted). The 404 call goes first, so its outcome shows even when the second call fails the
process (instead of the plan's branch by a process parameter); read back by a synchronous RunProcess with
`resultParameterNames` and RB-5 / RB-6.
- Round 1 (`UsrWscMissing` at `/UsrWscNoSuchRoute`): `ok=True code=200` in both processes. Under E4 an unauthenticated
  call to an unknown stand-root route is answered 302 -> `/Login/NuiLogin.aspx`, and the element follows the redirect:
  no 404. A static path answers 404 (`/UsrWscNoSuch.js`), `/0/rest/...` answers 401; on the owner's yes the method's
  address became `/UsrWscNoSuchRoute.js` and both processes ran again (round 2).
- Round 2: 404, `IX4` true -> `ok=False code=404`, element and process continue (Done); 404, `IX4` false -> the same
  `ok=False code=404`, element Completed, process continues; refused connection, `IX4` true -> `ok=False code=0`,
  element Completed, process Done; refused connection, `IX4` false -> element Error, process Error (`processStatus` 3,
  RunProcess "An error has occurred during the process execution. Please check the process log for details"),
  `SysProcessLog.ErrorDescription` = `System.Net.WebException: Unable to connect to the remote server --->
  SocketException ... 127.0.0.1:1` at `ServiceClient.GetResponseData` <- `WebServiceUserTaskImplementation.Execute`.
- A single-method service (`UsrWscDeadService`) auto-selects its method in the designer (r2 §2 confirmed).
Every expected row holds; the untraced one settles: with `IX4` true a transport failure leaves `Success` false and
`StatusCode` 0. So `ignoreErrors` matters only for transport failures (no HTTP response); an HTTP error status never
fails the element, and Success / StatusCode are usable in conditions with or without it (decisions Q7).

**Steps:**
1. Designer: `UsrWscEm7Ignore` with ignore errors ON on two elements in sequence: `UsrWscMissing` (404), then
   `UsrWscDead` (no listener); after each, conditional arms on `Success` and `StatusCode` with value captions
   ("WSC 404 ok=false code=404", "WSC dead ok=false", "WSC other"). `UsrWscEm7Strict`: the same with ignore errors
   OFF, the two calls in two separate branches started by a process parameter (so one failure does not hide the
   other), or as two processes. Save each (open the cards first).
2. Run each case once; RB-5, RB-6.
3. Claimed cut: builder twins `UsrWscEm7IgnoreB` / `UsrWscEm7StrictB` (`ignoreErrors: true` / absent); RB-1 shows
   `IX4` true / omitted; run; compare with step 2.

**Expected:**

| Case | Expected | Basis | Tag |
|---|---|---|---|
| 404, IX4 false or true | an HTTP response exists: the element completes, `Success` false, `StatusCode` 404, `Response` = the stand's 404 body; the process continues | r3 §4.6 | [traced] |
| no listener, IX4 false | no response: `GetResponseData` rethrows; only `RequestCanceled` is caught; the element fails, the process ends in Error with the WebException in `SysProcessLog.ErrorDescription` | r3 §4.6; M-12 | [traced] |
| no listener, IX4 true | the element completes and the process continues; what `Success`/`StatusCode` hold then is not traced | g2 (b) (`isIgnoreErrors = isEnabledIgnoreErrorFeature && userTask.IsIgnoreErrors`) | [traced flag only] |
| builder twins | the same as their designer counterparts | Q7 | design |

**If not:**

| Outcome | What changes |
|---|---|
| a 404 fails the element with IX4 false | Success/StatusCode are not usable in conditions without `ignoreErrors`: the KB article and Q7's guidance say so |
| IX4 true does not keep the process alive on a connection failure | Q7's promise narrows to what EM7 shows; the KB text follows |
| step 3 differs from step 2 | slice C fixes the IX4 write |

**Artifacts:** `CAP/em7/`. **Cleanup:** CU-1 for the four processes.

### EM8 Modify of an unrelated element: the latent loss

| | |
|---|---|
| Question | When a process's web-service method no longer exists, does a builder modify of an unrelated element persist the platform's prune of that element's params, and what does describe show before it? |
| Settles | P-D3 (Q12 = A agreed: describe issues plus a notice on a modify that lets the platform prune; not refused); g4 Q2 (does a service save evict the process cache); the dangling-reference half of the loss |
| Gate | current-builder half done 2026-11-03 (M-18); service-save and designer halves day 0; feature-build half on the claimed cut ("EM8 confirms on the claimed cut", plan P-D3) |
| Preconditions | EM1 (`UsrWscDriftService.UsrWscDrift`); never on a shipped process: g4's M1 recipe used `GetMeetingTranscript`, which this campaign does not touch |
| Writes | 1 designer save (`UsrWscEm8Drift2`), optional 1 more (`UsrWscEm8Drift3`), 1 service save (method deletion), 1 builder modify, optional 1 designer save |
| Owner | approves each write |

**Measured 2026-11-03, the current-builder half (M-18, decisions Q12).** Not on `UsrWscDriftService` but on
`UsrWscBk15Probe` after EM11's designer half (M-16): `WebService2` stored `IX2 = "getAppPublishedVersion"` (the
service has `GetAppPublishedVersion`) with all 8 tagged params and the 4 result params; `WebService1` exact, as the
control. One unrelated modify through the current clio + CrtProcessBuilder 1.6.6.131 (`addParameter`
`UsrWscEm8Marker`, Text) answered exit 0, "1 operation(s) applied", no notice. Afterwards `WebService2` holds only
Success, Response, StatusCode, RequestBody: its 4 tagged `_In` and 4 tagged `_Out` params are deleted from the
database; `IX2` is unchanged, so nothing in the stored element says what it lost. `WebService1` keeps all 12 params
and its 4 BK15 rows. So the latent loss on `origin/main` is real and silent; the "keeps the params" branch is ruled
out. Not measured: the drift made by a service save (the probe's drift is a wrong-case `IX2`, so g4 Q2 stays open),
an element whose lost params had readers (the dangling-reference half), and the designer's own prune (step 7). The
steps below cover those on `UsrWscDriftService`.

**Measured 2026-11-03, the service-save and designer halves (M-29, M-30, M-31; `CAP/em8/`, owner's yes for four
writes).** Processes `UsrWscEm8Drift2` (`ae6dfe62-faec-4303-9bfd-b6bd186c05df`) and `UsrWscEm8Drift3`
(`8bfe02dc-1e5a-4732-a28c-efa861b41804`), each as step 1 says: `WebService1` on `UsrWscDrift` (`UsrWscA_In` <- constant
`a`), `WebService2` on `UsrWscDrift` (`UsrWscA_In` <- `WebService1.UsrWscB_Out`), a Formula task `PRes =
WebService1.UsrWscB_Out`; 6 params per element, 8 BK15 rows. Step 3 renamed the method's code `UsrWscDrift` ->
`UsrWscDriftGone` instead of deleting it (no delete in this campaign; to an element it is the same: `IX2` resolves to
nothing).
- Step 2 / 5: describe of `UsrWscEm8Drift2` before the service save, and twice after it, returned the same full set
  (both elements' `UsrWscA_In` / `UsrWscB_Out` plus Success / Response / StatusCode): the service save did not evict the
  process already in the cache (g4 Q2: no eviction). `UsrWscEm8Drift3` and `UsrWscEm4MethodChange` (its element is on
  `UsrWscDrift` since EM4 S3), described for the first time after the save, returned only Success / Response /
  StatusCode per element: a fresh load is pruned. No issue, no flag in either reply. Describe returns a bound input
  (M-31), so the stored-vs-synced difference is visible only when the cache is cold.
- Step 4: `SysSchema.MetaData` of both processes byte-identical before and after the service save (ModifiedOn
  unchanged).
- Step 7 (`UsrWscEm8Drift3` opened from its designer URL, both cards opened, saved): the cards show the service, an
  empty method field ("Enter a value"), no parameters and no timeout. The save warned "Required fields of some elements
  are not filled in" for both web-service elements (not the Formula task) -> SAVE PROCESS, then "Schema has errors: The
  "FormulaTask1" element has an invalid value for the parameter "Body" ... ParameterUId 1e4d47d6 (WebService1)" and
  stored the process. Stored: both elements have NO parameters at all (the tagged ones and Success / Response /
  StatusCode / RequestBody), `BK15` is empty (8 rows deleted), both are `BL11:false`, `IX1` / `IX2` are unchanged
  (`UsrWscDrift`), the Formula task's token dangles with no flag, and the deleted params' caption resources are gone.
  Describe afterwards returns the two elements with no parameters.
Against the expected table: step 5 matches for a cold cache and differs for a warm one (the cached instance keeps the
pre-change set until it is evicted); step 7 differs from M-18: the designer deletes more than the builder's load does
(the builder kept the 4 result params and the BK15 rows). Per "If not", the KB article says which tool loses what: an
unrelated builder modify loses the tagged params (M-18), a designer save of the process loses every parameter of the
element and its BK15 rows (M-30). Step 8 (feature build) stays on the claimed cut.

**Steps:**
1. Designer: `UsrWscEm8Drift2`: `WebService1` on `UsrWscDrift` (`UsrWscA_In` <- constant), `WebService2` on
   `UsrWscDrift` with `UsrWscA_In` <- `WebService1.UsrWscB_Out`, a Formula task setting a process parameter from
   `WebService1.UsrWscB_Out`. Save. Optional: `UsrWscEm8Drift3` the same way (a separate save, for step 7). RB-1.
2. Describe `UsrWscEm8Drift2` (RB-9) once before the service change.
3. Studio: delete method `UsrWscDrift` from `UsrWscDriftService` (or rename its code), save (the service write).
   RB-3.
4. RB-1 on `UsrWscEm8Drift2`: the stored bytes are unchanged (a service save writes no process) [traced].
5. RB-9 on `UsrWscEm8Drift2` twice: does it show the pruned set (re-synced on load) or the pre-change set (a cached
   instance) (g4 §2, Q2)?
6. Done 2026-11-03 on `UsrWscBk15Probe` (above): the current builder's unrelated modify persists the prune.
7. Optional (g4 M3): open `UsrWscEm8Drift3` in the designer, open the cards, save. RB-1, diff against M-18's prune
   (tagged params removed, result params kept) and record the dangling readers.
8. Claimed cut: a modify of `UsrWscEm8Drift2` changing only the Formula task caption, through the feature build:
   record the notice (Q12) and RB-1; RB-9 shows `methodResolved: false` (Q14).

**Expected:**

| Step | Expected | Basis | Tag |
|---|---|---|---|
| 5 | describe already shows `WebService1`'s and `WebService2`'s tagged params pruned (an interpretable process is re-synced on every load; describe reads the synced view, M-21), unless the cache was filled before step 3 | g4 §2 | [traced] |
| 6 | measured: the tagged params removed, Success/Response/StatusCode/RequestBody kept, the answer silent | M-18 | [measured 2026-11-03] |
| 7 | the same prune as M-18 (the designer goes through the same server load and its own client sync) | g4 §3 | [traced, client parity unknown] |
| 8 | the prune of M-18 on both elements; `WebService2` marked invalid (`BL11:false`) only if `WebService1` is synced first; the Formula task's token left dangling with no flag; plus a notice naming the element, the unresolved method and the params the load removes | g4 §1, §3; Q12 = A | [traced]; notice = design |

**If not:**

| Outcome | What changes |
|---|---|
| step 7 differs from M-18 | the designer keeps what the server prunes: the KB article says which tool loses data |
| step 5 shows the stored set | describe can see drift directly: Q14's issue list can compare stored vs generated |

**Artifacts:** `CAP/em8/` (each process before and after, the diffs, the answers; the M-18 reads). **Cleanup:** CU-1
on `UsrWscEm8Drift2` (and `UsrWscEm8Drift3` if made); `UsrWscBk15Probe` with the Q4 probe (section 7); the method
stays deleted (manual TC-12 uses its own `UsrWscTc12` of the same service).

### EM9 Compile floor (local, no stand): DONE 2026-11-03

| | |
|---|---|
| Question | Does the package source compile against 8.3.1 and 8.1.3 core binaries today, and with a direct `IsIgnoreErrors` reference? |
| Settled | Q7 and Q13's IX4 part: the package already fails below 8.3.2, so `ignoreErrors` is written directly as IX4, with no reflection guard and no platform check (decisions Q7, agreed 2026-11-03; M-14) |
| Ran | locally, crt-process-builder `origin/main` `da719366` in a short scratch worktree; `clio dconf --build` from the DFS zips g2 read; `dotnet build` of the package project with `-c dev-nf` (net472, as CI), as is and with a one-line scratch reference to `ProcessSchemaWebService.IsIgnoreErrors` (never committed) |
| Writes | none on any stand |

**Result** (logs `CAP/em9/`, decisions Q7):

| Core | `origin/main` as is | + `IsIgnoreErrors` |
|---|---|---|
| 8.3.2.4199 (Studio) | builds | builds |
| 8.3.1.4498 (Studio) | fails: CS1061 `ProcessUserTaskSchemaManager.FindRuntimeSchemaFromMetaData` (`UserTaskDeprecationPolicy.cs:136`), CS0117 `DataValueType.Money0/1/3DataValueTypeUId`, `Float0DataValueTypeUId` (`ParameterTypeCompatibility.cs:53-55`) | fails, plus the expected CS1061 on `IsIgnoreErrors` |
| 8.1.3.6947 (SalesEnterprise_Marketing_ServiceEnterprise) | fails: MSB3245, `Creatio.Copilot.Abstractions` and `Terrasoft.Authentication.Contract` absent | fails the same way |

The package's real floor is already above 8.3.1, so a direct reference adds no new floor. Not measured: builds
between 8.3.1.4498 and 8.3.2.4199 (none is a supported target today). CI on 8.3.4 cannot show a floor (g2 design
consequence 1), and nothing in the test plan fakes the member's absence any more (dropped by Q7).

**Cleanup:** the scratch worktree was removed; the copied zips are deleted locally if still present.

### EM10 Cross-package service: the package dependency

| | |
|---|---|
| Question | When a process in package P calls a service in package Q that P does not depend on, does the designer save add a `SysPackageDependency` P -> Q, and does the builder add none? |
| Settles | Q9's cross-package notice (the run time needs no dependency; an exported P breaks without it, g5 (b)); Q9 = A agreed: the builder adds no dependency, adding one like the designer is a separate ticket |
| Gate | designer half done 2026-11-03 (M-17); builder half on the claimed cut |
| Preconditions | the claimed cut installed |
| Writes | 1 package creation (`UsrWscEm10Proc2`), 1 builder create; cleanup deletes |
| Owner | approves each write; package creation and deletion are the heaviest writes of the campaign |

**Measured 2026-11-03, the designer half (M-17, decisions Q9).** Not with three packages and `UsrWscEm10Service`, but
with one new package `UsrWscEm10Proc` (created in Configuration with the defaults, 0 `SysPackageDependency` rows) and
the existing `GeminiApiSearch` (package `CrtGeminiApiIntegraion`). Client feature `AutoAddPackageDependenciesInProcesses`
on, `AutoAddPackageDependencies` off. A designer process `UsrProcess_3389428` ("Business process 1",
`0576c2d6-bd8b-45e4-9a5b-d69f0183bc60`, its default code kept) in `UsrWscEm10Proc` with one element on
`GeminiApiSearch`: the service picker listed all 3 services although the package depends on nothing (unfiltered, as
traced); after the save there is exactly one new row, `UsrWscEm10Proc -> CrtGeminiApiIntegraion`. So the designer adds
the dependency. The element was later switched to `UsrWscEchoService` (M-19, M-20); `UsrWscEm10Proc` now depends on
`CrtGeminiApiIntegraion`, so it cannot host the builder half.

**Steps (the builder half, claimed cut):**
1. Configuration: create package `UsrWscEm10Proc2` (UI, default dependencies). RB-8: `CrtGeminiApiIntegraion` is not
   in its dependency closure.
2. Builder create `UsrWscEm10Builder` in `UsrWscEm10Proc2` with one element on `GeminiApiSearch.GenerateContent`
   (never run, rule 12). Record the notice. RB-8.

**Expected:**

| Step | Expected | Basis | Tag |
|---|---|---|---|
| designer half | a new row P -> the service's package | M-17 | [measured 2026-11-03] |
| 2 | no new row (the builder saves through `SaveSchema`, which applies no dependency); the answer carries the Q9 cross-package notice | g5 (b) | [traced]; notice = design |

**If not:** the builder adds a row -> the notice is dropped from Q9.

**Artifacts:** `CAP/em10/`. **Cleanup:** CU-1 on `UsrProcess_3389428` and `UsrWscEm10Builder`, then CU-3 on
`UsrWscEm10Proc` and `UsrWscEm10Proc2`, in that order.

### EM11 A wrong-case method name through the platform

| | |
|---|---|
| Question | When `IX2` differs from the service's method code only in case, does the platform produce an empty parameter set without an error and fail the run? |
| Settles | Q9 (agreed A: resolve case-insensitively, write the canonical spelling; a miss lists codes and captions) |
| Gate | designer half done 2026-11-03 (M-16); builder half on the claimed cut |
| Preconditions | the claimed cut installed; `UsrWscEchoService.UsrWscGet` (exists, M-19) |
| Writes | claimed cut: 1 builder create, 1 refused call (writes nothing) |
| Owner | approves the create |

**Measured 2026-11-03, the designer half (M-16, decisions Q9).** Not on `UsrWscDriftService` but on `UsrWscBk15Probe`
(the Q4 probe): the designer's own save request (DataService `ContractProcessSchema`) was replayed with `WebService2`'s
`methodName` changed to `getAppPublishedVersion` (the service method is `GetAppPublishedVersion`); `WebService1` kept
the exact name as the control. The save answered `success: true`, `hasErrors: false`, no message; the stored bytes
hold `IX2 = "getAppPublishedVersion"` and all 8 tagged and 4 result params (the save does not sync). The next
design-time load (`describe-business-process`) shows `WebService2` with only Success / Response / StatusCode;
`WebService1` keeps its 4 `_Out` params. The run: `WebService1` Completed, `WebService2` Error,
`Terrasoft.Common.ItemNotFoundException: Collection item with name getAppPublishedVersion not found` at
`ServiceClient.CreateRequest`; the process run fails. So a case-only slip saves green, is pruned on the next load
and fails at run time: canonicalisation is needed, as traced (g5 (b); g4 §1, §5; r3 §4.1). No Studio case rename and
no `UsrWscCase` method are needed any more.

**Steps (the builder half, claimed cut):**
1. Builder create `UsrWscEm11Builder` with `service: "UsrWscEchoService"`, `method: "USRWSCGET"`; RB-1 shows
   `IX2 = "UsrWscGet"` and the generated params.
2. A create with `method: "nope"` on the same service is refused; the refusal lists the service's method codes with
   captions, and nothing is created (Q9). RB-4 confirms.

**Expected:**

| Step | Expected | Basis | Tag |
|---|---|---|---|
| designer half | saves green, pruned on load, run fails | M-16 | [measured 2026-11-03] |
| 1 | canonical spelling `UsrWscGet` written | Q9 | design |
| 2 | refusal lists `UsrWscGet` and the service's other methods, codes with captions; no schema created | Q9 | design |

**Artifacts:** `CAP/em11/` (the M-16 reads and run, and the builder half). **Cleanup:** CU-1 on `UsrWscEm11Builder`;
`UsrWscBk15Probe` with the Q4 probe (section 7).

### EM12 A nested array-in-array request body on 10.1.37

| | |
|---|---|
| Question | On 10.1.37, without the ENG-55920 fix, is the inner list dropped from the sent body when its container is unmapped and its leaves are mapped? |
| Settles | Q13's notice N5 (shown on every target, naming the version line and the remedy: map the container); the KB run-time caveat |
| Gate | builder half (the notice) on the claimed cut; the run half is not measurable on stand `Creatio` under EP E4 (Q17) |
| Preconditions | the claimed cut installed; EM1 (`UsrWscEcho` with its nested request items); M-08 |
| Writes | claimed cut: 1 builder create (no run) |
| Owner | approves the create |

The run half (a designer process on `UsrWscEcho` with both containers unmapped and the leaves mapped, run, the sent
body read back) needs an endpoint that returns the request, which E4 lacks. The answer rests on the core unit tests
and the ENG-55920 comment 469862 (a 38-byte body, no error, g2 (c)): pre-fix `CreateListItem` gives a sourceless nested
list `GetDefValue` = null (verdict r1-C7), and the fix is absent from 10.1.37 (M-08) [traced] + [measured in a
binary, not on the stand]. Whether a nested ObjectList (`UsrWscTags_In` inside `UsrWscOrders_In`, test plan §11)
arrives is unmeasured for the same reason; it is outside the N5 predicate. Q13's later question (suppress N5 on fixed
builds) therefore has no stand evidence from this campaign.

**Steps (claimed cut):**
1. Builder create `UsrWscEm12Builder`, one element on `UsrWscEcho`: required inputs bound;
   `UsrWscOrders_In.UsrWscOrdNo_In` <- constant `N-1`; `UsrWscOrders_In.UsrWscLines_In.UsrWscSku_In` <- constant `S-1`;
   both containers (`UsrWscOrders_In`, `UsrWscLines_In`) left unmapped (the ENG-55920 configuration, verdict r1-C7).
   RB-1. Not run.

**Expected:**

| Step | Expected | Basis | Tag |
|---|---|---|---|
| 1 | the answer carries notice N5 for the nested CompositeObjectList `UsrWscLines_In` only, naming the version line and the remedy (map the container) | Q13; design N5 | design |

**Artifacts:** `CAP/em12/`. **Cleanup:** CU-1 on `UsrWscEm12Builder`.

## 6. Campaign order and time

Dependencies: EM0 -> EM1 -> (EM2, EM3 route (b), EM4, and the day-0 halves of EM7 and EM8). EM5 needs the
experimental cut and EM2. The claimed-cut halves need EM5 passed. Within a session the order is strict (rule 2).
EP is settled (E4) and EM9 is done, so neither waits any more.

| Session | When | Order | Owner approvals (approx.) |
|---|---|---|---|
| done | 2026-11-03 (spent) | EM9 (local); Q4 BK15 probe; EM3 route (a); EM11 designer half; EM8 current-builder half; EM10 designer half; EM1 partial (`UsrWscEchoService`, Q16 part); EM4 partial (service change) | given at the time |
| 1 | day 0 | EM0 -> EM1 (the remaining shapes, two services) -> EM2 S1 -> EM3 route (b) -> EM4 S1-S3 | 3 service saves; 5 designer saves (EM2 1, EM3 1, EM4 3) |
| 2 | day 0 or day 1 (plan 0.9, optional) | EM8 steps 1-5 and 7 -> EM7 steps 1-2 | about 8-10 writes and runs (EM8 2-4, EM7 6); no package creation |
| 3 | experimental cut | install -> EM5 steps 1-6 (STOP gate; step 4 optional) | install; about 6 writes (step 5 adds the designer copy) |
| 4 | claimed cut | install -> EM6 -> EM7 step 3 -> EM8 step 8 -> EM10 steps 1-2 -> EM11 steps 1-2 -> EM12 step 1 | install; about 13 writes and runs (EM6 2, EM7 6, EM8 1, EM10 2 incl. a package creation, EM11 1, EM12 1) plus EM11's refused call |
| 5 | final cut | install -> EM2 step 9 -> EM5 re-run with step 7 -> cleanup proposal -> cleanup on the owner's yes | install; about 4 writes; then the deletes |

Time per EM: the values are the [estimate](eng-92710-call-web-service-element-estimate.md) section 2.5 figures,
copied, and the estimate owns them [inferred]. Agent hours include waiting for the stand and the designer. The
Status column is this file's; the hours are not re-split for the parts already spent, and the totals are the
estimate's as they stand.

| EM | Agent h low / likely / high | Status 2026-11-03 | Note |
|---|---|---|---|
| EM0 | 0.25 / 0.25 / 0.5 | **spent** 2026-11-03 | read-only |
| EM1 | 1.5 / 2 / 3 | **spent** 2026-11-03 | three services by hand in Studio (Quick setup from JSON), plus `UsrWscOrders2` for EM3 and the `UsrWscMissing` address change for EM7 |
| EM2 | 1 / 1.25 / 2 | **spent** (step 9 2026-11-09, M-36) | |
| EM3 | 0.5 / 0.75 / 1.25 | **spent** 2026-11-03 | route (b) on `UsrWscDriftService.UsrWscOrders2` |
| EM4 | 0.25 / 0.5 / 0.75 | **spent** 2026-11-03 | four saves (S1 twice, M-27) |
| EM5 | 0.5 / 0.75 / 1.25 | experimental cut done 2026-11-04; final cut **spent** 2026-11-09 (steps 1-3, 6, 7; steps 4, 5 not run) | install wait; the STOP decision if it fails; the designer copy of step 5 included |
| EM6 | 0.75 / 1.25 / 2 | **spent** 2026-11-09 (with EM6-B) | one create and one run; the trace gave the element's request values |
| EM7 | 0.25 / 0.5 / 0.75 | **spent** (builder half 2026-11-09) | two processes, two rounds of two runs (the first round had no 404, M-28) |
| EM8 | 0.5 / 0.75 / 1.25 | **spent** (step 8 2026-11-09, M-41) | the no-reader N7 case is not measured |
| EM9 | 0.5 / 0.75 / 1.5 | **spent** | zip copies dominate |
| EM10 | 0.25 / 0.5 / 1 | **spent** (builder half 2026-11-09) | the deletions are left |
| EM11 | 0.1 / 0.25 / 0.5 | **spent** (builder half 2026-11-09) | |
| EM12 | 0.25 / 0.4 / 0.75 | **spent** (builder half 2026-11-09) | no run |
| **EM0-EM8, EM10-EM12 after batching** | **6.0 / 8.25 / 12.5** | | estimate 2.5 (EM9 spent and not counted): EM2-EM4 in one Chrome session, EM6/EM7/EM12 in one probe process |
| cleanup | 0.75 / 0.9 / 1 | to do | about 20 deletes, one at a time; estimate 2.5 row "Stand cleanup" |
| **with cleanup** | **6.75 / 9.15 / 13.5** | | |

Owner hours are not split per EM. The estimate's section 3 carries them: the Chrome capture sessions (0.25 / 0.5 /
0.75), the stand-write approvals (0.6 / 0.9 / 1.4) and the cleanup approvals (0.1 / 0.2 / 0.3), both estimate §3;
about 2 h likely (estimate 7); the installs are counted there separately. Calendar: sessions 0-2 fit day 0-1; sessions 3-5 follow
the cuts.

## 7. Leftovers register

Filled as each measurement runs. Ids and names only; no stand address. "Kept" rows say until when. "Lead" = the agent
driving Claude in Chrome or clio, the owner logged in and approving.

Created so far (2026-11-03):

| EM | Kind | Name or id | Created (date, by) | Removal | Removed (date) | Note |
|---|---|---|---|---|---|---|
| EM1 | web service | `UsrWscEchoService` (`2ac0b905-591d-4623-a8bf-990846cea7d3`, `Custom`) | 2026-11-03, lead | CU-2 after the clio e2e evidence run and the manual Jira cases, and after every process that calls it (`UsrProcess_3389428` included) | | kept: the EM1 service (M-19), extended by EM1; capture, e2e and manual-case service (plan CL-9 is Ignored without it; test plan §8 Conditions) |
| Q4, EM11, EM8 | process | `UsrWscBk15Probe` (`cacc6ca5-1ee8-420e-bdba-56b818e2c89e`, `Custom`) | 2026-11-03, lead | CU-1 | | on the shipped Creatio Marketplace API; holds a wrong-case `IX2` on `WebService2` (M-16), whose tagged params the M-18 modify deleted, and the process parameter `UsrWscEm8Marker` (M-18); runs: SysProcessLog `c0133215-df84-46c8-9b16-cf9f631999d9` (Q4) and the M-16 run |
| EM3 | process | `UsrWscEm3Probe` (`14825024-d8e7-4322-b43a-edf23939a73f`, `Custom`) | 2026-11-03, lead | CU-1 | | route (a) on `GeminiApiSearch`; never run |
| EM10, Q16, EM4 | process | `UsrProcess_3389428` ("Business process 1", `0576c2d6-bd8b-45e4-9a5b-d69f0183bc60`, package `UsrWscEm10Proc`) | 2026-11-03, lead | CU-1, before CU-3 on `UsrWscEm10Proc` and CU-2 on `UsrWscEchoService` | | not `UsrWsc`-prefixed: a prefix check (RB-4) misses it; its element was on `GeminiApiSearch` (M-17) and now calls `UsrWscEchoService` (M-19, M-20) |
| EM10 | package | `UsrWscEm10Proc` | 2026-11-03, lead | CU-3 after `UsrProcess_3389428` | | holds the designer-added `SysPackageDependency` `UsrWscEm10Proc -> CrtGeminiApiIntegraion` (M-17) |
| any | run logs | the Q4 and M-16 runs of `UsrWscBk15Probe` | 2026-11-03 | not deleted (CU-6) | | decisions Q4 lists "its process log rows" for cleanup, which CU-6 does not do: the owner decides |
| EM1 | web service | `UsrWscDriftService` (`7dd8d33d-b760-4fb1-bec0-ec14c4ae7c83`, `Custom`) | 2026-11-03, lead | CU-2 after EM4, EM8 and manual TC-12, and after `UsrWscEm3CaptureB` and `UsrWscEm4MethodChange` | | methods `UsrWscDrift`, `UsrWscTc12`, and `UsrWscOrders2` (added for EM3 route (b)); `UsrWscDrift` is deleted (or renamed) by EM8; manual TC-12 renames `UsrWscTc12` (test plan §8) |
| EM1 | web service | `UsrWscDeadService` (`69899196-4f2b-4151-94bf-91c9c9600345`, `Custom`, base URI `http://127.0.0.1:1`) | 2026-11-03, lead | CU-2 after the clio e2e evidence run, the manual Jira cases and the EM7 processes | | kept: TC-I-09 and manual TC-06 call it (test plan) |
| EM1, EM7 | web service change | `UsrWscEchoService`: methods `UsrWscEcho`, `UsrWscMissing` added and `UsrWscGet` extended (EM1); `UsrWscMissing` address `/UsrWscNoSuchRoute` -> `/UsrWscNoSuchRoute.js` (EM7) | 2026-11-03, lead | with the service (CU-2) | | the first row of this table holds the service itself |
| EM2 | process | `UsrWscEm2CaptureA` (`0ca3d151-afdb-482c-ab97-3e479eedc21e`, `Custom`) | 2026-11-03, lead | CU-1 after the clio e2e evidence run and manual TC-11 | | kept: AC5 oracle, e2e fixture (TC-I-07) and manual TC-11's process; never run or modified |
| EM3 | process | `UsrWscEm3CaptureB` (`fc2d199a-0715-4892-8784-3a9db67fc519`, `Custom`) | 2026-11-03, lead | CU-1 with EM2 | | route (b) on `UsrWscDriftService.UsrWscOrders2`; never run |
| EM4 | process | `UsrWscEm4MethodChange` (`6a9bed7b-1f82-4df6-a053-7dc0887fa926`, `Custom`) | 2026-11-03, lead | CU-1 | | never run; stored with schema errors (dangling Formula / flow tokens, S2-S3) |
| EM7 | process | `UsrWscEm7Ignore` (`5fd83882-23fe-48b4-98f3-6246c4ca0284`, `Custom`) | 2026-11-03, lead | CU-1 | | runs `80f35e55-ddb2-4aff-a2c9-8f89acc7878d`, `8ad3e2d6-7dfc-487a-84b6-ca6028b7d467` (Done) |
| EM7 | process | `UsrWscEm7Strict` (`9a8691c1-0342-4f99-98ab-4ca9c6de7f26`, `Custom`) | 2026-11-03, lead | CU-1 | | runs `eddd83e2-f9e7-4033-950d-abd9c9592baf`, `ec934f63-3aa6-4bea-b530-363a62b86319` ended in Error (refused connection); the instances stay in the process log (CU-6) |
| any | run logs | the four EM7 runs | 2026-11-03 | not deleted (CU-6) | | as the row above for the Q4 / M-16 runs |
| EM8 | process | `UsrWscEm8Drift2` (`ae6dfe62-faec-4303-9bfd-b6bd186c05df`, `Custom`) | 2026-11-03, lead | CU-1 | | stored bytes still the pre-drift set (never re-saved); kept for step 8 (claimed cut); never run |
| EM8 | process | `UsrWscEm8Drift3` (`8bfe02dc-1e5a-4732-a28c-efa861b41804`, `Custom`) | 2026-11-03, lead | CU-1 | | re-saved by the designer after the drift: both elements without parameters, `BL11:false`, schema errors; never run |
| EM8 | web service change | `UsrWscDriftService`: method code `UsrWscDrift` -> `UsrWscDriftGone` | 2026-11-03, lead | with the service (CU-2) | | drifts `UsrWscEm8Drift2`, `UsrWscEm8Drift3` and `UsrWscEm4MethodChange` on purpose; manual TC-12 uses `UsrWscTc12` |
| EM5 | process | `UsrWscEm5Twin` (`060da1eb-5f49-4034-ae4d-1f21ce493637`, `Custom`) | 2026-11-04, package session | CU-1 after the final-cut re-run | | builder-made; designer re-saved once; process parameter `UsrWscEm5Marker` added by step 6; never run |
| EM5 | install | CrtProcessBuilder 1.6.6.137 (experimental, stand-only) | 2026-11-04, package session | not removed | | replaced the stand's 1.6.6.131; superseded by the claimed cut |
| EM2 S2 | web service | `UsrWscSoapService` ("WSC SOAP service", `197ff0c0-f168-4bbf-9db9-81f86102e40f`, `Custom`; method `UsrWscSoapEcho` `d9def665-c857-4101-ae5e-97c7f681c570`) | 2026-11-05, lead | CU-2 after `UsrWscEm2SoapS2` | | SOAP 1.1, entered by hand; endpoint `http://localhost/UsrWscSoap`; never called |
| EM2 S2-S4 | stand state | "Compilation error" in the header after the owner-approved `clio compile-configuration --all` of 2026-11-05 (build result 1, CS0006 in `UsrSrvVisitA1` / `UsrSrvVisitA2`) | 2026-11-05, measurements session | not ours to fix; left as is on the owner's word | | pre-existing configuration errors, not ENG-92710 objects; do not attribute a later install or run failure to a package cut without checking it |
| EM2 S2-S4 | process | `UsrWscEm2SoapS2` ("WSC EM2 SOAP S2", `dbc77d7c-8f37-446b-8a07-d77c767804e4`, `Custom`; element `WebService1` `c4c5c167-d18c-4f14-9402-502f55d21bed`) | 2026-11-05, lead | CU-1 | | saved three times (S2-S4); now on `UsrWscEchoService.UsrWscEcho`, `BL11:false`; never run |

Created on stand `eng105744-web02` by the final-cut runs (2026-11-09, lead; `CAP/em-final146/em-final146-report.md`):

| EM | Kind | Name or id | Removal | Note |
|---|---|---|---|---|
| EM5 | process | `UsrWscEm5Twin` (`0dbbd07f-7c67-4205-b45d-c8c0a4c0605e`, `Custom`) | CU-1 | never run; its designer save added `SysPackageDependency` `Custom -> UsrWscTestFixtures` (owner decides) |
| EM6 | process | `UsrWscEm6Run` (`ce1dde0b-87ee-44eb-b085-4c2765e5ddfa`, `Custom`) | CU-1 | run `2f8f03c7-4947-4602-9f93-ced0d10827ff`; tracing switched off again (the property row stays, False) |
| EM7 | processes | `UsrWscEm7IgnoreB` (`59cd4e41-f568-40cc-9a8c-773642614c73`), `UsrWscEm7StrictB` (`6b2d133d-d781-4c00-b9d4-d146fbf6ca23`), `Custom` | CU-1 | runs `ff2bef70-5566-47af-9ec7-de91bceddfac` (Completed), `62b5f486-f757-409a-ba35-bde1ada59dfa` (Error) |
| EM8 | process | `UsrWscEm8Drift2` (`1315e802-b5ab-4a22-9922-6fc368e5acdb`, `Custom`) | CU-1 | drifted on purpose; never run |
| EM8 | web service change | `UsrWscDriftService` method `UsrWscDriftGone` -> `UsrWscDriftGone2` | rename back or keep | the `UsrWscTestFixtures` archive no longer matches the stand |
| EM10 | package, process | `UsrWscEm10Proc2` (`cd63d31d-023c-4ac7-b2fb-50b950e8e658`); `UsrWscEm10Builder` (`afb60d5f-e211-4de3-ab48-b3a7766844f3`) | CU-1, then CU-3 | never run |
| EM11 | process | `UsrWscEm11Builder` (`4f586752-0203-471f-96ac-a8e35589845a`, `Custom`) | CU-1 | never run |
| EM12 | process | `UsrWscEm12Builder` (`bbbdd124-fb90-4918-83f7-4a68a371972e`, `Custom`) | CU-1 | never run |
| KB-7 | process | `UsrMarketplaceApp_GetPublishedVersion` (`bb6a5adf-353d-46fa-be46-403b4529b9c1`, `Custom`) | CU-1 | created by the AR-1 agent; calls the shipped `CreatioMarketplaceApi`; never run |

Planned (stand `Creatio`, superseded by the rows above where the same name appears):

| EM | Kind | Name or id | Created (date, by) | Removal | Removed (date) | Note |
|---|---|---|---|---|---|---|
| EM5 | process | `UsrWscEm5OracleCopy` (designer copy of `UsrWscEm2CaptureA`, step 5) | | CU-1 after EM5 | | step 5's modify target; `UsrWscEm2CaptureA` is never modified (rule 10) |
| EM6 | process | `UsrWscEm6Run` | | CU-1 | | run history stays (CU-6) |
| EM7 | processes | `UsrWscEm7IgnoreB`, `UsrWscEm7StrictB` (builder twins) | | CU-1 | | claimed cut |
| EM10 | package, process | `UsrWscEm10Proc2`; `UsrWscEm10Builder` | | CU-1, then CU-3 | | the builder process on `GeminiApiSearch`, never run |
| EM11 | process | `UsrWscEm11Builder` | | CU-1 | | on `UsrWscEchoService` |
| EM12 | process | `UsrWscEm12Builder` | | CU-1 | | not run |
| any | run logs | `SysProcessLog` / `SysProcessElementLog` rows | | not deleted (CU-6) | | stand history |
| any | installs | the experimental, claimed and final cuts of CrtProcessBuilder | | not removed | | the stand keeps the last installed cut; the owner decides whether to bring it back |

## 8. Considered and not planned

| Item | Source | Why not |
|---|---|---|
| Appended-param UId instability across a worker recycle | g4 M2 | needs a site restart on shared stand `Creatio` for every user; the unit tests cover the rule (do not key on such a UId) |
| Whether a tracing-only modify goes through `SetPropertyValue` (no sync) | g4 Q4 | a source question, answered in slice D by reading the code path, not on a stand |
| The designer's service read returns the service's authentication settings | Q16 | an implementer rule, not a measurement: the builder never calls that read and never echoes it |
| Two services with the same name in one workspace | g5 open 4 | defensive refusal; unit-tested; reproducing it needs a crafted package install |
| A duplicate Name/Tag at two nesting levels throws at run time | r3 open 5; g3 §6 | needs a deliberately broken service; the builder mirrors the service tree; recorded as a KB note candidate |
| Whether the package harness resolves the internal parameter provider; whether `ServiceSchema.ReadMetaData` reads a PackageStore capture | r8 open B3, B6 | local unit-test probes, owned by the [test plan](eng-92710-call-web-service-element-test-plan.md) |
| A multi-instance web-service element | C-09 | 0 in the corpus; v1 refuses it (Q20); unit-tested |
| The stand copy of corpus pair 2 (`ActionGeminiSearch` / `GeminiApiSearch`) diffed against its PackageStore capture | serialization §8.1 | optional read-only RB-1 / RB-3, not a step of EM0; pattern A compares the PackageStore bytes, which do not depend on the stand |

## 9. Research corrections this file depends on

| # | Note and claim | What holds instead |
|---|---|---|
| 1 | r9 S6: describe on 1.6.6.131 omits Tag and nested item parameters | refuted: the package returns them; the session's stale MCP client 8.1.0.135 dropped them (verdict r9-S6), and an older MCP clio build did the same on 2026-11-03. What the package does omit is every unbound input (bound inputs come back, M-31) and the `webService` member (M-21). Rule 8 |
| 2 | r3, r6: `IX4` is an orphan key / absent in core | true only of the stale `<Creatio2>/TSBpm` (2025-07); current core has `IX4` from 8.3.2.3590 (g2; verdict r3-C2) |
| 3 | r3: 65/66 elements put service params first | 57/66; 9 elements have a later-added param after the result params (g1 (a)) |
| 4 | r9 S3: every designer-built element carries four result params | RequestBody on 14/66 (11/25 product); the run time needs only three (verdict r9-S3) |
| 5 | r9 §3.3: Success is "const false" | Source None with a value `"false"` (no `GS1`), on 61/66; `{}` on 5 test elements (verdicts r6-C4, r9-S3) |
| 6 | r7 A3: the timeout unit must be confirmed by a capture | already settled by source: the page stores seconds x 1000, the run time reads milliseconds (verdict r7-A3); EM2 only confirms it |
| 7 | r1 C7: nested array-in-array request bodies need 10.1+ | the defect covers only an inner list whose container is unmapped and whose leaves are mapped; "saves green" is unverified (verdict r1-C7). EM12 measures exactly that configuration |
| 8 | r3 C6: a composite without item properties sends empty objects | on the wire the body builder walks the service's item properties and writes null leaves or service defaults; the empty items are in the intermediate value and in the response (verdict r3-C6) |
| 9 | r8 C2: a package test cannot bind the internal provider | it can, through reflection before the element is constructed (verdict r8-C2); not a stand question |
