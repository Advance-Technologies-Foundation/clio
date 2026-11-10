# ENG-92710 Call web service element: owner decisions

Written 2026-11-02.

**All twenty decisions were agreed by the owner on 2026-11-03**, one at a time; each section ends with an "Agreed by
the owner" line, which overrides the "Recommendation (PROPOSED)" text above it where they differ (Q3, Q5, Q7, Q10,
Q13, Q17). Several were settled by stand measurements taken the same day (Q4, Q6, Q7, Q9, Q12, Q14, Q16). The other
plan files (design, test plan, estimate, plan, README, serialization, measurements, reuse) were swept for these
changes on 2026-11-03. Every stand write, Jira write, push and reviewer request still waits for the owner's explicit word, and
merges are done by a human.

Basis: crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history), clio `origin/master`
`6be5dde28`, clio-knowledge `origin/master` `5f14ceb`, CrtProcessDesigner 7.8.0 and the PackageStore corpus, core
TSBpm trunk and `<core>` `468c4c71b` (8.3.3.1722), Creatio Academy, and a read-only inventory of stand
`Creatio` (Creatio 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131) taken 2026-11-02. Research notes:
`<research notes, local>/` (`r1`..`r10`, gap notes `g1`..`g5`, fact-check verdicts in
`_workflow-result.json`; a verdict correction overrides a note, a `g` note supersedes an `r` note).

Paths: `PB/` = `packages/CrtProcessBuilder/Files/src/cs/` at `da719366`; `CORE/` = `<core>/TSBpm/Src/Lib/`;
`NUI/` = `CORE/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/`; `PD/` =
`CrtProcessDesigner/branches/7.8.0/Schemas/`; `CLIO/`, `KB/` = repository roots. The older checkout
`<Creatio2>/TSBpm` is stale (2025-07) and lacks IX4; it is not cited for current behaviour.

Tags: **traced** = read in source, a hypothesis about run time until an EM settles it; **measured** = observed in
corpus data, a binary, git, or the stand inventory.

Rule for arguing options (owner feedback): options are compared on clean-slate cost and platform constraints.
Existing prototype or draft code is never an argument for or against an option.

Hours below are note-level sizing inferred from the calibration (r10 §4, g3 §5, g5 "Estimate impact"), not measured.
For a non-recommended option they are deltas against the recommended baseline, and where estimate §6.1 / §6.2 carries
the same event that row is cited. Sizes for recommended options come from the research notes and are superseded by
the estimate's bottom-up; do not add them. The totals live in [estimate](eng-92710-call-web-service-element-estimate.md); the
measurements in [measurements](eng-92710-call-web-service-element-measurements.md); the work order in
[plan](eng-92710-call-web-service-element-plan.md).

## Summary

| # | Question | Agreed 2026-11-03 | Settled or changed by |
|---|---|---|---|
| Q1 | Element identity | build `ProcessSchemaWebService`; refuse `userTask` = `WebServiceUserTask` with a pointer to `webService` | EM2 |
| Q2 | Contract | `webService {service, method, timeoutSeconds?, ignoreErrors?}` on create / addElement / setElement; request values through the existing mapping surface on the element's own parameters by stored name; no aliases | EM2, EM6 |
| Q3 | Parameter generation | package-owned pure generator (port of the designer algorithm), nested items always emitted (no `L18` feature gate, no N11); platform sync tolerated afterwards | EM2, EM5 (passed, M-37) |
| Q4 | BK15 rows | emit the 4 result-param rows for byte parity | EM5 (passed, M-37) |
| Q5 | Composite request body | nested item mappings (work today) + `RequestBody` raw text as escape hatch; no new parameter type; no JSON check or conflict notice in v1 | EM6 (M-38), EM6-B trace (M-39), EM12 (builder half M-44) |
| Q6 | Composite response | v1 declared Collection (<= 2 levels) + item mappings; mirror / ObjectList / 3+ levels to ENG-92730 "Complex / structured parameter types" | EM3, EM6 |
| Q7 | Errors | `ignoreErrors` written to IX4 directly (EM9: package floor already ~8.3.2, no guard); Success / StatusCode / Response are ordinary outputs | EM0, EM7 (builder half M-40), EM9 (done) |
| Q8 | Timeout | whole seconds in the contract, stored as ms; 0 / absent = unset | EM2 |
| Q9 | Resolution | service by UId, name, caption, case-insensitive, ambiguity refused; method case-insensitive; canonical spelling written; refusals list valid names (the unknown-service refusal with each service's methods, bounded, K-1 2026-11-09); cross-package notice | EM10, EM11 (designer halves done; builder halves M-42, M-43 in the final-cut campaign); M-47 |
| Q10 | Discovery | no new package operation, no clio tool in v1; tree returned by create/modify and describe; same-request mappings; the unknown-service refusal points to registration; KB "task without details"; registration = ENG-105475 "Register a REST web service through the AI Toolkit" | agent runs AR-1 (passes with one refusal, M-48), AR-2 (passes, M-46) |
| Q11 | Method/service change | A3: replace semantics, `Success` / `Response` / `StatusCode` keep their UIds, retarget guard over every other parameter of the element, notice lists dropped bindings | EM4 (done) |
| Q12 | Drift / latent loss | describe issues `serviceResolved` / `methodResolved` / nested drift; notice on a modify that would prune, also carried by a modify the platform's validation refuses (F-2, F-5, 2026-11-09) | EM8 (current-builder half done: the loss is real; step 8 re-measured on the claimed cut: refused with N7, M-51, M-52) |
| Q13 | Platform floors | IX4 direct (Q7); warning N5 on a sourceless nested object list (ENG-55920 "Unable to use array inside array in web-service business process element") on every target | EM9 (done), EM12 |
| Q14 | Describe read-back | `webService` member + stored parameter tree, never re-derived, never defaults | EM2, EM8 |
| Q15 | Text budgets | swap, no ceiling raise; one pointer clause to KB `process-web-service`; flip catalog; routing disambiguation | - |
| Q16 | Secrets | no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output; the element's stored bindings are returned by design | EM1 |
| Q17 | Stand captures and runs | lead in Claude in Chrome on stand `Creatio`; test services `UsrWsc*` with the stand root as base URI (EP option E4, no echo); cleanup list | EM0, EM1 |
| Q18 | Jira description corrections | Task 22, Task 37 / ENG-92730, element identity, estimate unit; owner approves the text first | - |
| Q19 | PR split and order | BMAD docs PR, package, clio-knowledge, clio; cuts in a claim comment; merge package -> KB -> clio, human merges | - |
| Q20 | Legacy / edge | refuse multi-instance; SOAP built exactly like REST, with notice N10 (D, 2026-11-05: the SOAP capture showed the same shape) | EM2 S2-S4 |

---

## Q1. Element identity

**Question.** Which platform element does the builder create for "Call web service"?

**Why it matters.** Every other decision hangs on it: contract, handler, describe, parity oracle. The Jira text and the
stale Confluence state doc (4769087495, v90 2026-07-07) treat the element as something waiting for composite types; the
generic `userTask` route would build the wrong thing today without complaint.

**Facts.**
- The designer's element is the core class `Terrasoft.Core.Process.ProcessSchemaWebService : ProcessSchemaActivity`
  (run time `ProcessWebService`), ManagerItemUId `652b598d-cdd5-49d5-a86d-aafac88213f3`, edited by
  `WebServiceUserTaskPropertiesPage`; it is not a `ProcessSchemaUserTask` (`CORE/Terrasoft.Core/Process/ProcessSchemaWebService.cs:22`;
  r3 C1, r4 C1 confirmed). Measured: 66/66 corpus elements carry `BL1 = ...ProcessSchemaWebService`, `BL7 = 652b598d`; the
  stand inventory shows the same class (r9 S1).
- CrtProcessDesigner's `WebServiceUserTask` (`e7acb2e5`) is a legacy SOAP user task with a hard-coded envelope; 0 processes
  reference it and no `SysProcessUserTask` row registers it (r4 H2, r6). Traced: `{"type":"userTask","userTaskName":"WebServiceUserTask"}`
  would still build it, because `UserTaskElementHandler` resolves any user-task name (`PB/Elements/UserTaskElementHandler.cs:83-102`).
- The package has no web-service code; describe types the element `ProcessSchemaWebService`, `buildType: null` (r4 H3/H4, r9 S6).
- Precedents for a refused generic route exist: pre-configured page, auto-generated page, Process file variants
  (`PB/Elements/UserTaskElementHandler.cs:353, 382, 410`).

**Options.**
- **A. Build `ProcessSchemaWebService` through a new handler; refuse `userTask` = `WebServiceUserTask`** with a refusal
  that names `type: "webService"`. Cost: in base; the refusal is ~0.5 agent h.
- B. Build `ProcessSchemaWebService`, leave the legacy route open. 0 h, keeps a trap that produces an unrunnable SOAP stub
  next to the real element.
- C. Build `WebServiceUserTask`. Fails AC5 by construction (no designer capture of it exists; it is not the Academy element).

**Recommendation (PROPOSED): A.** **Agreed by the owner on 2026-11-03: A.**

**What changes it.** EM2: if the capture shows a different class or ManagerItemUId (traced and measured agree, so not expected).

---

## Q2. Contract

**Question.** What does the agent send to put a Call web service element into a process and give it request values?

**Why it matters.** It fixes the clio key check, the call-shape drift tests, the description bytes (Q15) and how the
agent addresses composite bodies.

**Facts.**
- The element stores four own keys: IX1 `ServiceSchemaUId`, IX2 `MethodName` (string), IX3 `Timeout` (int ms), IX4
  `IsIgnoreErrors` (core 8.3.2.3590+), each omitted at its default; `BO2`/`BO3` true; parameters in `BP2` (r3 C2, g2).
- The element is a `ProcessSchemaParametrizedFlowNode`; the existing mapping service resolves dotted item paths on any
  such element, takes constants, formulas, process parameters, element outputs and record columns, and writes the
  corpus byte shape for parameter-to-parameter bindings (g3 M6, M7; byte-equal to the shipped `ExecuteUpgradeBsf`
  item binding; traced write, measured target).
- Stored names are `<svc>_In` / `<svc>_Out` with Tag `L17` = the service parameter name; nested names are suffixed too;
  a scalar array has one `<Name>ItemValue_<dir>` child (r6 C3 measured 242/242 nested suffixed). Describe already
  reports tags and nested items on 1.6.6.131 (r9 S6 corrected), but omits unbound inputs (`PB/Describe/ProcessDescriber.cs:226`).
- clio refuses any block key it does not declare, because WCF drops undeclared members silently (r5 C1, C2); that
  holds for servers before CrtProcessBuilder 1.6.6.136 (gate-1 note below).
- Precedent: the `subProcess` block selects the callee; its inputs are then set through mappings by stored name.

**Options.**
- **A. Block `webService {service, method, timeoutSeconds?, ignoreErrors?}` on `create` element descriptors and on
  `modify` `addElement` / `setElement`; request values (incl. `RequestBody`) through the existing mapping surface,
  addressed by the stored names describe returns (`Body_In`, `Items_In.Name_In`); no name aliases.** Cost: in base
  (one block, four members).
- B. A plus an inline `request: {<serviceParamName>: <source>}` map inside the block. Shorter calls, but a second binder
  beside the mapping service, a second key set in clio, more description bytes, and the bare service names are not
  unique across directions and depths. About +4-6 agent h, +1 owner h review.
- C. A with aliases (accept `Body` for `Body_In`). About +2-3 agent h; two spellings in guidance and describe; ambiguity
  rules for In/Out pairs of the same service name.

**Recommendation (PROPOSED): A.** One way to address a parameter, the same one describe shows. **Agreed by the owner on 2026-11-03: A.**

**What changes it.** EM2 (the capture's element keys), EM6 (a builder-made binding runs).

**Draft AC wording (AC1).** "The `webService` configuration (service, method, optional timeout in seconds and
ignore-errors flag) is accepted on create, `addElement` and `setElement`; request parameters, including a composite
request body and the raw request body, are set through the existing mappings on the element's own parameters,
addressed by the names `describe-business-process` returns."

**Settled at gate 1, second pass (2026-11-03): baseline drift.** clio master `f86d8098f` (2026-11-03) rebundled
CrtProcessBuilder 1.6.6.136, whose server refuses undeclared request keys (`UnknownRequestKeyDetector`). At 1.6.6.136
`ReadBackKeys` is derived, not listed: `RootPairs` pairs `ProcessElementDescriptor` / `ProcessElementUpdateDescriptor`
with `DescribeProcessElement`, and nested blocks that share a key pair automatically. clio's own key set is still
needed for older servers; "WCF drops an undeclared member silently" holds only before 1.6.6.136. The read-back-only
keys are derived automatically because the write and describe members are both keyed `webService`; nothing to
register, and TC-U-109 replays a described block through the server key check (plan P-B1, P-C1, step 0.4).

---

## Q3. Parameter generation

**Question.** Who creates the element's parameters: the platform's `SynchronizeParameters`, or the package?

**Why it matters.** AC5 (byte parity) and run time both depend on it.

**Facts.**
- The designer (client `NUI/process-webservice-schema.js`) emits: tagged `_In`/`_Out` params; CompositeObjectList with
  recursive nested `L18` items; ObjectList with one `ItemValue` child; MaxSizeText for Text/empty; the IsRequired rule;
  captions in resources (`BaseElements.<El>.Parameters.<path>.Caption`, " - Input"/" - Output" only where a caption
  is shared across request and response; the scope is pinned by EM2); then untagged Success (Boolean Out), Response
  (Text Out), StatusCode (Integer Out), RequestBody (Text In); Value "false" on every Boolean, Success included: every
  Boolean parameter, tagged, nested or Success, stores `L8 {"GS2":"false","GS5":<process>}` (serialization §2.4, I7;
  14/14 tagged corpus Booleans, 3 top-level and 11 nested, measured; Boolean In settled by EM2, SQ5); `A3 = A4 =
  process UId` and `IL2 = element UId` at every level (g1 (b), measured 751/751). It builds `L18` only while feature `ProcessParameterCollections` is on (or debug) (r2 C3).
- The platform `ProcessSchemaWebService.SynchronizeParameters` (public) -> internal `ProcessSchemaWebServiceParametersProvider`
  builds only flat top-level params: no `L18`, no ItemValue child, no caption suffix, no result params, no BK15 (r3 C5,
  r4 C4 confirmed).
- Run time reads Success, StatusCode and Response with `GetByName`, which throws when they are missing (r3 C4 confirmed).
  A platform-only element therefore fails at run time, not only at parity.
- The provider is resolved from the internal `CoreApiContainer`; it is null in the package's test harness and
  `SynchronizeParameters` then throws (r8 C2). The platform sync cannot run in package unit tests without binding an
  internal type.
- Every design-time load re-runs the platform sync, matching by Name; a correct shape (names, Tags, provider types,
  IsRequired, `IsValueSerializable = true`) makes it a no-op (g4 §1, §5; traced, not measured).
- Assigning `ServiceSchemaUId` / `MethodName` runs nothing (auto-properties), unlike sub-process `SchemaUId` (r4 C5).
- Precedent: `PB/Elements/PreconfiguredPageParameterSync.cs` already ports a designer client algorithm, with a read-only
  `IsInSync` twin.

**Options.**
- **A. A package-owned pure generator** (input: the service method's `ServiceParameter` tree; output: the parameter tree,
  caption resources and result params), a port of the client algorithm, unit-tested without the platform provider; the
  platform sync is tolerated afterwards. Cost: in base (~4-6 agent h generator + its parity tests).
- B. Platform `SynchronizeParameters`, then a package pass that adds `L18`, ItemValue children, captions and result
  params. Still ports every hard part of the algorithm, adds a dependency on the internal provider (null in tests), and
  splits one shape across two writers. About +2-3 agent h for the test seam, no saving.
- C. Platform sync only. Fails AC5 and fails at run time (no Success / StatusCode / Response). Not viable.

**Sub-point for the owner (inside Q3).** The `L18` gate: follow the designer and emit nested items only while
`ProcessParameterCollections` is on (read through a new feature-reader seam: an interface over the
`Features.GetIsEnabled` read the IX4 feature gate also uses, so tests set both states), emitting the flat shape with
notice N11 otherwise (design C3, §5.2). The feature is on in the repository Web.config/app.config (traced) and as a
DB feature on stand `Creatio` (r9 §4, measured); the GlobalAppSettings value the run time reads is unverified and
settled by EM0/EM6 (verdict r3-C6 (5)).

**Recommendation (PROPOSED): A**, with the designer's `L18` gate.

**Agreed by the owner on 2026-11-03: A WITHOUT the `L18` gate** - always emit nested items. Basis (research
2026-11-03, `<research notes, local>` feature-history run): the toggle is from 2018 (CRM-38674, on
for release in CRM-39571 / 7.12.3) and every checked build from 7.13 to 10.2.931 ships `Feature-ProcessParameterCollections=true`
in Web.config / WebHost config, which is what the run-time re-keying reads; ENG-47367 (8.2.1, the 7.12.4 cleanup that removed the web-service feature toggles) deleted the
CrtProcessDesigner data binding but left the core client readers, which read DB rows only, so a clean 8.2.1+ database
has the designer emit flat composites (open bug ENG-68545 "No data binding exists for ProcessParameterCollections
feature"). The builder does not copy that defect; the feature-reader seam and notice N11 are dropped.

**Settled at gate 1, second pass (2026-11-03): the Jira titles of the keys above** (English renderings of the
Russian Jira titles): CRM-38674 "Run time: mapping to a collection-type parameter (in interpreted processes)";
CRM-39571 "Enable the collections functionality in the release"; ENG-47367 "[RFMT] Remove feature toggling in web
services released in 7.12.4".

**What changes it.** EM2 (the oracle bytes the generator must match); EM5 (a builder-made element opened and re-saved in
the designer comes back unchanged, so the platform and client syncs are no-ops on our shape).

---

## Q4. BK15 rows

**Question.** Does the package write the four `ProcessSchemaMapping` (BK15) rows the designer writes for the result
params?

**Why it matters.** They would be the first BK15 rows the package ever authors; skipping them breaks byte parity.

**Facts.**
- The designer writes one row per result param (66/66 elements, 212 rows: 52 x 3, 14 x 4), never for tagged or nested
  params; key set `BL1 UId A2 A3 A4 GT2 GT3 GT5 GT1`, no `GT4`; Success `GT1 = {"GS2":"false"}` without `GS5` (g1 (c),
  measured).
- Traced: nothing at run time or in the server design-time sync reads them; the designer's own copy/paste produces
  row-less web-service elements (g1 (c), "What reads them"). They are byte parity only; not stand-verified.
- The package has never written a BK15 row (git grep, g1); `ProcessGraphBuilder.RemoveElement` already deletes rows that
  target an element (`PB/Graph/ProcessGraphBuilder.cs:1915-1922`).

**Options.**
- **A. Emit the rows**, mirroring `createMappingInfo` (set the param value first, then add the row, so Success `GT1`
  stays without `GS5`). Cost: ~1-2 agent h (one helper + the parity assertion).
- B. Skip them and record a named, runtime-neutral divergence in the parity rule ("BK15 for result params: optional").
  0 h; the comparator carries an exception forever.

**Measured on stand `Creatio` (10.1.37), 2026-11-03**, process `UsrWscBk15Probe` (`cacc6ca5-1ee8-420e-bdba-56b818e2c89e`,
package Custom, left for cleanup), service Creatio Marketplace API, method `GetAppPublishedVersion`:
- A designer-built element (WebService1) gets 8 tagged params, then Success / Response / StatusCode / RequestBody, and
  exactly 4 BK15 rows: `A3 = A4 = process`, no `GT4`, Success `GT1 = {"GS2":"false"}` without `GS5`, Success `L8`
  with `GS5` - the traced shape, byte for byte in these keys.
- The designer's own copy/paste (WebService2) gets the same 12 params with fresh UIds and **no BK15 rows**.
- After a designer reload, opening WebService2's properties page, moving it and saving again: still no rows for
  WebService2, WebService1's 4 rows unchanged (no duplicate, no rewrite), result-param UIds unchanged.
- Run, same day: flows re-drawn to Start -> WebService1 -> WebService2 -> End (WebService2 still row-less after that
  save), process run from the designer; SysProcessLog `c0133215-df84-46c8-9b16-cf9f631999d9` Completed, both web-service
  elements Completed with no error (real GET to the Creatio Marketplace API, empty inputs). The Success / StatusCode
  values were not read (no tracing): the run proves only that a row-less element executes without an exception.
So a row-less element is a state the designer itself produces, keeps, and runs; B is safe on the designer and run-time
sides. Cleanup: the process `UsrWscBk15Probe`; its process log rows stay as stand history (measurements CU-6).

**Recommendation (PROPOSED): A**, unless EM5 shows the designer duplicates or rewrites the rows on re-save; then B.
**Agreed by the owner on 2026-11-03: A (write the rows).** `A2` is not rewritten when the element is renamed, as the
designer does not rewrite it either.

**What changes it.** EM5.

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM2, EM4; measurements M-24, M-26).** The designer element on
`UsrWscEcho` (`UsrWscEm2CaptureA`) got exactly 4 schema-level BK15 rows of the C-04 shape in BP2 result order, as on the
Marketplace probe. New for the shaper: a method or a service change re-creates the 4 rows with NEW row UIds but keeps
their `GT5` values (EM4 S2, S3). The rest of the generated shape (Q3) matched the corpus rules row by row; EM2 adds the
unbound Boolean In `L8 {"GS2":"false","GS5":<P>}`, a Text constant stored as `L8 {"GS1":1,"GS5":<P>}` with its value
only in the `.Value` / `.DisplayValue` resources, and a " - Input" / " - Output" suffix that ignores nested items.

---

## Q5. Composite request body

**Question.** How does the agent fill a composite request body (an array of objects, an array of scalars, nested arrays)?

**Why it matters.** AC1 names it; the Confluence index's page-wide ±50% caveat names web-service composite bodies as
one example of a hard-to-capture element (its biggest named risk is Filters, #4) (r1 §1, verdict r1-C11).

**Facts.**
- Measured, corpus: composite request bodies are rare in product use (2 product methods, both bound at item level with
  the container at Source None); Gemini builds raw JSON in `RequestBody` through `string.Format` (r6 C8, g1 L8 table).
- Traced, g3 §2-§3: the existing mapping service already handles (a) a nested leaf from a scalar process parameter,
  formula or constant (container untouched; run time builds a one-item list from the item sources), and (b) an item from
  an item of another collection (P1 binds the parent, COL<-COL); the written bytes equal the corpus items. COL<-ObjectList
  is refused by exact type match (M5).
- A non-array object never becomes a parameter: the service designer flattens it into JSONPath scalars (r1 C8 corrected).
- `RequestBody`, when non-null at run time, replaces the generated body (r3).
- Before the ENG-55920 fix (core#1119, fixVersion 10.1; absent in 10.1.37, the stand, g2 (c), measured by binary
  member names), an inner object array whose container has no source of its own while its leaves are mapped is sent
  without that inner list (null); a mapped inner container is sent; that such a process saves without error is not
  measured (traced, verdict r1-C7; EM12).

**Options.**
- **A. Nested item mappings (as they work today) and `RequestBody` raw text (constant or formula) as the escape hatch;
  no new process-parameter type.** Cost: in base (guidance, fixtures and parity tests; no rule change, g3 §5).
- B. A plus a single `CompositeObject` process-parameter type to hold a request object. The service designer never
  produces a non-array object parameter, so nothing consumes it; it is ENG-92730 scope. About +10-15 agent h.
- C. `RequestBody` only. 0 code, but AC1's "composite request body via create/modify" would be met only as text, and
  bindings to records would be formulas.

**Recommendation (PROPOSED): A.** **Agreed by the owner on 2026-11-03: A as stated** - nested item mappings as they
work today plus `RequestBody` (constant or formula) as the escape hatch; no new process-parameter type; mapping
rules unchanged. Discussed and NOT taken into v1: a JSON check of a constant `RequestBody` and a notice when both
`RequestBody` and Body parameters are bound; the KB article explains when to use `RequestBody` (methods without a
Body parameter such as `GeminiApiSearch`, the ENG-55920 workaround, shapes mappings cannot express) and that a
non-null value replaces the generated body.

**What changes it.** EM6 (the request body bytes of a builder-made element, incl. a one-item list from item-level
mappings); EM12 (the sourceless inner list sent as null on 10.1.37, which Q13's notice states).

---

## Q6. Composite response

**Question.** How is a composite response (array of objects, nested up to 4 levels in the corpus) read into process
parameters?

**Why it matters.** The old Jira AC2 (now AC3) said "incl. a composite response body (Task 5 types)". The Task 5 reference is stale (Q18);
which types exist decides the scope.

**Facts.**
- Measured: composite responses are common (12/25 product elements), nested up to 4 levels, mostly unused or read in C#
  scripts (r6).
- Traced, g3 §4: a declared `Collection` with `itemProperties` (<= 2 levels, `MaxCollectionLevels = 2`) plus item
  mappings from the WS output produces the stored Gemini shape (COL/COL/text, all three levels `GS1 = 3` tokens); Tag is
  read nowhere on the mapping path.
- Gaps (Task 37 / ENG-92730): the `typeFromElement` mirror of a WS collection is refused (item Tags are service names,
  not GUIDs, `PB/Parameters/ProcessParameterService.cs:974-1026`); ObjectList is not declarable; declared collections
  stop at 2 levels while WS nesting reaches 3-4; single CompositeObject refused (never produced by the service designer)
  (r4 C7, C8; r5 C6).
- Run time re-keys response values Tag->Name only while feature `ProcessParameterCollections` is on (r3 C6). On the
  stand the DB feature row is on (r9 §4, measured); the GlobalAppSettings value the run time reads is unverified
  (EM0/EM6, verdict r3-C6 (5)). Not measured for a builder-made element.
- g3 §5: the composite half is ~10 agent h as declared-only; ~25 agent h with mirror relaxation, ObjectList and a third
  level.

**Options.**
- **A. v1 = declared Collection (<= 2 levels) + item mappings from WS outputs; mirror of WS outputs, ObjectList and 3+
  levels go to ENG-92730.** Inside v1 the mirror refusals change for WS sources only (design C13, plan P-C6): the
  existing CompositeObjectList mirror refusal gains a pointer to a declared Collection with item mappings, and a WS
  ObjectList mirror source, which today silently creates a parameter with no shape (r5 C6 verdict (1), traced), is
  refused with the same pointer. Cost: in base (~10 agent h for the composite half; C13 ~0.5 agent h of it).
- B. A plus relaxing the mirror for WS outputs (copy Tag verbatim) and an ObjectList declarable type. About +15 agent h,
  +2-4 owner h (review rounds on a rule shared with Read data mirrors), and one more cut by the r10 pattern.
- C. Scalars only: map flattened response scalars; collections stay unread. Fails AC3's composite clause.

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM3, designer half)**, process `UsrWscEm3Probe`
(`14825024-d8e7-4322-b43a-edf23939a73f`, package Custom, left for cleanup), element on `GeminiApiSearch.GenerateContent`,
process Parameters > Add parameter > "Create from element", only `Candidates` ticked:
- The designer creates a **CompositeObject wrapper** `WebService1Results1` (type `632e4371`, Tag
  `SourceElement:WebService1`, no value of its own) holding a Collection `WebService1Candidates_Out` (Tag
  `WebService1.Candidates_Out`, bound `[#...[Element:{ws}].[Parameter:{Candidates_Out}]#]`), whose items are ONLY the
  first-level scalars (`CRole`, `CFReason`, `SEPRContent`, `CIndex`), each bound to the element item. The nested
  collections of the element tree (`CParts_Out > PText_Out`, `GMGChunks_Out`, ...) are not offered in the dialog and
  not created.
- So the designer's own "mirror" is the CompositeObject flat shape: names prefixed with the element name, Tags
  `<Element>.<Param>` (not GUIDs), one wrapper level, nested lists dropped. Neither the builder today nor option B's
  "copy the Tag verbatim" relaxation reproduces it: it needs a declarable CompositeObject (ENG-92730).
- The hand-made route (a declared Collection with item mappings) is the shipped `ActionGeminiSearch` shape, already
  measured on the stand (r9 §3.3); not rebuilt in this probe.

**Recommendation (PROPOSED): A.** The owner may pull B into this story; the estimate then moves by the amounts above.
**Agreed by the owner on 2026-11-03: A** (declared Collection <= 2 levels with item mappings; mirror, ObjectList,
3+ levels and the designer's CompositeObject "Create from element" shape stay in ENG-92730; the WS mirror refusals
point to the declared-Collection route).

**What changes it.** EM3 (the designer's "create parameter from element" shape and Tags: if it is a CompositeObject
flat shape, as in the shipped Customer360 process, A cannot reproduce it and guidance says so); EM6 (the response
lands in a declared Collection with the feature on).

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM3 route (b), measurements M-25).** The designer has the
declared-Collection route, item-first: the container value picker of a "Collection of records" offers no web-service
object array ("There are no parameters of required type", for 2- and 3-level outputs alike), but binding an item
(`Orders.OrdNo` <- `UsrWscOrd2No_Out`) binds the container `Orders` to the output array by itself, and binding a nested
item binds the nested collection. Stored on `UsrWscEm3CaptureB` (on `UsrWscDriftService.UsrWscOrders2`, a 2-level
response added for this): the declared collection has type 651ec16f (CompositeObjectList, as the element's own
output), its items no `L17` / `L12`, every level `GS1` 3 with an `[Element:{ws}].[Parameter:{uid}]` token, and
`.DisplayValue` resources with a `:` path (`[#WSC drift service 1.UsrWscOrd2:UsrWscOrd2No#]`). A agreed holds; for the
builder and the KB: bind the container as well as the items (the designer's bytes), and guide a designer user to map
an item first. Whether an item of a 3-level output can be bound item-first was not tried (out of v1).

**Draft AC wording (AC3).** "Inputs are mapped in through the existing mappings; the response is read into process
parameters, a composite response through a declared Collection parameter (up to two levels) with item mappings;
mirroring a web-service output, scalar-array (ObjectList) parameters and deeper nesting are ENG-92730 scope."

---

## Q7. Errors

**Question.** Does the block expose "continue on web-service errors" (IX4), and how are failures read?

**Why it matters.** IX4 is a compile dependency in a source-only package compiled by the target.

**Facts.**
- IX4 `IsIgnoreErrors` arrived in core commit `5e53b027c9b` (ENG-50229 "Possibility to continue execution of the business process when Web-Service element fails", fixVersion 8.3.2), first in build 8.3.2.3590;
  present in CI 8.3.4.2753, 10.0, 10.1.37, 10.2; absent in 8.1.3.6947 and 8.3.1.4498, where a direct reference fails with
  CS1061 (g2 (a), (b), measured in binaries).
- A compile error in the package takes down every CrtProcessBuilder tool on that target (inference, g2 (b)). No platform
  floor is declared anywhere (g2 (b)).
- At run time IX4 works only while `DisableIgnoreErrorsInWebServiceUserTask` is off; the 7.8.0 designer hides the
  checkbox under the same gate (g2 (b), traced).
- Success is true for status 200..399 (r7 A2); Success / StatusCode / Response are ordinary output params; 21/25 product
  elements never read Success or StatusCode (r6 §4.3 table; r6's summary says 22/25, which is inconsistent with its
  own table).
- An HTTP 4xx/5xx answer completes the element with Success false whatever IX4 is; only a call with no HTTP answer
  (connection failure, timeout) depends on IX4 (facts 5.11, r3 §4.6, traced; EM7).

**Options.**
- **A. Expose `ignoreErrors` with a reflection guard** (property absent: `ignoreErrors: true` is refused with a named
  message, never dropped silently; `false` is accepted with nothing written, the only behaviour there; describe reads
  it the same way, design C11); Success / StatusCode / Response usable in conditions and mappings like any output.
  Cost: ~1.5-2 agent h incl. a test that fakes the member's absence.
- B. Declare a platform floor of 8.3.2.3590 and reference IX4 directly. The direct reference is ~0.5 agent h, but no
  floor mechanism exists today: introducing one is +1-4 agent h (estimate §6.1 row 4), and it locks every older
  target out of the whole package.
- C. Leave IX4 out of v1. 0 h; designer parity on the checkbox is lost and agents fall back to Success checks.

**Recommendation (PROPOSED): A.** EM9 (Q13) has two outcomes that change it. (a) The package already fails to compile
below 8.3.2 for other reasons: IX4 is written directly and the guard is dropped, -0.5-1 agent h against A, plus the
clio floor wording only if a declared floor is then stated (estimate §6.2). (b) The reflection guard is not viable:
B's declared floor of 8.3.2.3590 is introduced, +1-4 agent h (estimate §6.1 row 4).

**What changes it.** EM0 (the feature state on the stand), EM7 (a call with no HTTP answer, IX4 true vs false), EM9.

**EM9 measured 2026-11-03** (local, crt-process-builder `origin/main` `da719366`, `clio dconf --build` from the DFS
zips, `dotnet build ... -c dev-nf`; logs `<research notes, local>/captures/em9/`, scratch worktree
removed):

| Core | `origin/main` as is | + `ProcessSchemaWebService.IsIgnoreErrors` |
|---|---|---|
| 8.3.2.4199 (Studio) | builds | builds |
| 8.3.1.4498 (Studio) | **fails**: CS1061 `ProcessUserTaskSchemaManager.FindRuntimeSchemaFromMetaData` (`UserTaskDeprecationPolicy.cs:136`), CS0117 `DataValueType.Money0/1/3DataValueTypeUId`, `Float0DataValueTypeUId` (`ParameterTypeCompatibility.cs:53-55`) | fails, plus the expected CS1061 on `IsIgnoreErrors` |
| 8.1.3.6947 (SalesEnterprise_Marketing_ServiceEnterprise) | **fails**: MSB3245, assemblies `Creatio.Copilot.Abstractions` and `Terrasoft.Authentication.Contract` absent | fails the same way |

Outcome (a): the package's real floor is already above 8.3.1, so a direct `IsIgnoreErrors` reference adds no new
floor; the reflection guard is not needed.

**Agreed by the owner on 2026-11-03: A with a direct IX4 write** (`ignoreErrors` in the block, no reflection guard and
no platform check; Success / StatusCode / Response are ordinary outputs). Design C11's guard and its fake-absence test
are dropped; Q13's IX4 part is settled by the same measurement. "No platform check" means no version or member check for IX4; the feature
notice N3 (`DisableIgnoreErrorsInWebServiceUserTask` on: the run time ignores IX4) stays as one feature read in the
guards (design 4.12). Not measured: builds between 8.3.1.4498 and 8.3.2.4199 (none of them is a
supported target today).

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM7 designer half, measurements M-28).** Two runs per `IX4` value
(`UsrWscEm7Ignore`, `UsrWscEm7Strict`): an HTTP 404 completes the element with Success false and StatusCode 404 and
the process continues, with `IX4` true or false; a refused connection with `IX4` true completes the element with
Success false and StatusCode 0 and the process continues; with `IX4` false it fails the element and the process
(Error, `System.Net.WebException` in `SysProcessLog.ErrorDescription`). Unchecked stores no `IX4`. Confirms A: the
flag matters only for transport failures (no HTTP response); Success / StatusCode are usable in conditions either way,
and the KB article says that an HTTP error status never fails the element. Under E4, an unknown stand-root route
answers 302 -> login page (200), so the 404 case needed a static path (`/UsrWscNoSuchRoute.js`).

**Draft AC wording (new).** "The ignore-errors flag is stored on the element; Success, Http status code and Response
body are available as element outputs."

---

## Q8. Timeout

**Question.** In which unit does the agent give the timeout?

**Facts.** IX3 is milliseconds (core doc and run time `TimeSpan.FromMilliseconds`); the page edits "Maximum execution
time, in seconds" and saves `seconds * 1000`; the limit covers retries (r7 A3 corrected). Corpus: 6/66 elements set it
(600000, 1000000 x2, 1000, 5000, 1000; r3 C2). ENG-88713 "Web Service User Task timeout placeholder is misleading when process element timeout is not explicitly set": an empty field is saved as 0, not the method default (r1 §4).

**Options.** **A. `timeoutSeconds` (whole seconds, designer unit), stored as ms (x1000); 0 or absent = unset.** B.
`timeoutMs` (storage unit): agents read the designer and Academy in seconds, so a x1000 error is likely. Both ~0.5 h.

**Recommendation (PROPOSED): A.** Describe returns seconds; a stored value that is not a whole second (none in the
corpus) is reported as a fraction, not rounded (1500 ms -> 1.5; design §2.5).

**What changes it.** EM2 (the stored IX3 for the timeout set in the capture).

**Agreed by the owner on 2026-11-03: A** (`timeoutSeconds`, whole seconds, stored as ms x1000; 0 or absent = unset;
describe returns seconds, a non-whole-second stored value as a fraction).

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM2, M-24).** "Maximum execution time, in seconds" = 7 stored
`IX3` 7000; an unset field shows "Default (<method timeout> sec)" and stores no `IX3`. Confirms A.

---

## Q9. Resolution of service and method

**Question.** How are `service` and `method` matched, and what does a miss return?

**Why it matters.** An inexact spelling is silent on the platform: the next load prunes every tagged param.

**Facts.**
- The designer offers every row of `VwWebServiceV2`, REST and SOAP, no active or type filter; IX1 stores the
  manager-item UId (g5 (a)).
- `FindItemByName` is a first-match ordinal scan; method lookup (`ServiceMethods.FindByName`) is ordinal; an unknown or
  wrong-case method gives an empty parameter set, so the sync drops every tagged param, with no error (g5 (b), g4 §1).
- Measured in the PackageStore corpus (not on a stand): 32 service schemas, 0 duplicate names or UIds; 63 methods, max
  7 per service, median 1, no case-only duplicates (g5). Stand `Creatio` shipped 3 service schemas before the test service was added (`BingSearchApiCall`,
  `GeminiApiSearch`, `CreatioMarketplaceApi`), measured 2026-11-03. The runtime resolves the service workspace-wide; the designer save adds a package dependency, the
  builder save adds none for any element (g5 (b)). 64/66 corpus elements use a service of their own package; 2 sit in
  the dependency closure (g5, measured).
- Precedent: `PB/Elements/SubProcessApplier.cs:560-606` (UId / name / caption, ambiguity refused) and
  `PB/Elements/SubProcessReader.cs:176-193` (`FindItemByNameOrNull`) (g5).

**Options.**
- **A. Service by UId, then name, then caption, case-insensitive, ambiguity refused; method case-insensitive within the
  service; write the canonical spelling (IX1 = manager-item UId, IX2 = `method.Name`); a miss lists the valid names
  (all services, or the resolved service's methods), sanitized; a service in a package outside the process package's
  dependencies gets a notice (no dependency is added).** Cost: ~3-4 agent h (g5: about +0.5 day).
- B. Exact ordinal names only. ~1 h less; an agent's case slip saves green and loses every parameter on the next load.
- C. A, but add the package dependency like the designer. Cross-element behaviour (sub-process has the same gap); +3-5 h
  and its own review; better as a separate ticket.

**Recommendation (PROPOSED): A.**

**What changes it.** EM11 (a wrong-case method through the platform leaves the params empty, confirming why the builder
canonicalises); EM10 (designer vs builder `SysPackageDependency`; if the designer adds one, the notice wording says so).

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM11 and EM10, designer halves).**
- EM11. Process `UsrWscBk15Probe` (the Q4 probe): the designer's own save request (DataService `ContractProcessSchema`)
  was replayed with `methodName` of `WebService2` changed to `getAppPublishedVersion` (service method
  `GetAppPublishedVersion`); `WebService1` kept the exact name as the control.
  - The save answered `success: true`, `hasErrors: false`, no validation message.
  - Stored bytes (`SysSchema.MetaData`): `IX2 = "getAppPublishedVersion"`; all 4 tagged `_In` and 4 tagged `_Out` params
    and the 4 result params still stored. The save does not sync.
  - Design-time load (`describe-business-process`): `WebService2` shows only Success / Response / StatusCode; every
    tagged param is gone. `WebService1` keeps its 4 `_Out` params.
  - Run: `WebService1` Completed; `WebService2` Error, `Terrasoft.Common.ItemNotFoundException: Collection item with
    name getAppPublishedVersion not found` at `ServiceClient.CreateRequest`. The process run fails.
  So a case-only slip saves green, is pruned on the next load and fails at run time: canonicalisation (A) is
  confirmed as needed; B would ship exactly this failure.
- EM10. Client feature `AutoAddPackageDependenciesInProcesses` is ON (read in the designer page); `AutoAddPackageDependencies`
  is OFF. A new package `UsrWscEm10Proc`, created in Configuration with the defaults, had 0 `SysPackageDependency` rows.
  In it a designer process `UsrProcess_3389428` ("Business process 1",
  `0576c2d6-bd8b-45e4-9a5b-d69f0183bc60`) with one element on `GeminiApiSearch` (package `CrtGeminiApiIntegraion`):
  - the service picker listed all 3 services although the package depends on nothing (unfiltered, as traced);
  - after the save there is exactly one new row, `UsrWscEm10Proc -> CrtGeminiApiIntegraion`.
  So the designer adds the dependency; the builder half (no row from `SaveSchema`) is still traced only and runs on the
  claimed cut. The cross-package notice in A stands; C (add the dependency like the designer) stays a separate ticket.
- Left for cleanup: the `WebService2` edit in `UsrWscBk15Probe` (the whole probe is already on the cleanup list) and its
  run; process `UsrProcess_3389428`; package `UsrWscEm10Proc` (after the process).

**Agreed by the owner on 2026-11-03: A** (service by UId, then code, then caption, case-insensitive, ambiguity refused;
method by code, case-insensitive, within the service; canonical IX1/IX2 written; a miss lists names and captions only;
a service outside the process package's dependency closure saves with a notice and no dependency is added). Adding the
dependency like the designer (C) is a separate ticket for every element, not this one.

**Changed in the claimed cut, re-measured 2026-11-09 (finding K-1, measurements section 0; within A).** "A miss lists names and captions only" no longer
holds for a service miss: the unknown-service refusal lists every registered service sorted by name, the first 30 as
`Name (caption; methods: A, B)` (at most 8 methods each, then ", and N more"; "declares no methods"; "methods not
readable" when the schema does not load), services 31-100 as `Name (caption)`, the rest counted ("and N more not
shown"); an ambiguous match lists the matches with their methods (no caption). The methods are there because the block
needs one, so one refusal finds service and method (measured M-47; AR-1 passes with one refusal, M-48). The
unknown-method refusal still lists method codes without captions (finding F-3, open, Low).

**Draft AC wording (now AC2).** "An unknown or ambiguous service or method fails with a message listing the
available services, or the methods of the resolved service; names are matched case-insensitively and stored in their
canonical spelling."

---

## Q10. Discovery

**Question.** How does the agent learn which services, methods and parameters exist before building?

**Facts.**
- clio has no web-service discovery tool. The designer's service read returns the service's authentication
  settings; the builder never calls it and never echoes it (g5 (c), traced).
- clio security pins: 8 operation contracts, 6 authorization gate call sites; a block inside build/modify/describe moves
  neither; a new operation moves the contract count (r4 C11 confirmed, g5 (c)).
- Services are few, so a refusal can list them all (Q9). Sub-process precedent: selection runs in the build's
  `afterElementsPlaced` hook, so mappings in the same request can reference the synced params (r4 §1).
- Describe omits unbound inputs today (`PB/Describe/ProcessDescriber.cs:226`); Q14 closes that for this element.

**Options.**
- **A. No new package operation and no clio tool in v1; the generated parameter tree is returned by the create/modify
  result and by describe; mappings may reference the generated params in the same request (build ordering like
  sub-process).** Cost: in base.
- B. A clio `list-web-services` / `describe-web-service` MCP tool. About +8-12 agent h (+1-1.5 day by g5), +1-2 owner h;
  needs a `KnownRoute`, secret stripping, docs, e2e, an index re-pin and a KB article; it shows the SERVICE tree, not the
  element names the builder accepts.
- C. A new package read operation. B plus a contract-count move (8 -> 9), a guard-deny test and a rebundle.

**Recommendation (PROPOSED): A.** File B as a follow-up only if agents demonstrably need discovery before the first
create.

**What changes it.** No EM; isolated agent runs in the [test plan](eng-92710-call-web-service-element-test-plan.md)
show whether an agent reaches a correct element without discovery.

**Agreed by the owner on 2026-11-03: A, with two additions.** The Process Designer also works only with registered
services (its list is `VwWebServiceV2`, no "new service" entry; the button next to the field opens the selected service
for editing; checked on stand `Creatio` 2026-11-03), so an unregistered API is out of reach for both.
1. The "unknown service" refusal lists codes with captions and says that a missing service has to be registered first
   in Studio > Web services (base URI, methods, authentication); a new refusal covers an environment with no registered
   service (design 5.1).
2. The guidance article gets a "task without details" section: find the service through the refusal list and ask the
   user when several fit; when none fits, stop (no placeholder process, no Script task in its place) and tell the user
   what registration needs; build in two steps (element first, mappings from the returned tree); fill a key or token
   parameter (as `Xgoogapikey` of `GeminiApiSearch`) from a system setting or a process parameter, never a constant.
Registering a web service is a separate story: ENG-105475 "Register a REST web service through the AI Toolkit". About
+0.5 agent h (texts, a test, the article section) plus about +0.5 agent h for two agent scenario runs (test plan AR-1:
service not named; AR-2: service not registered, the agent must stop). The isolated runs this section cites under
"What changes it" did not exist in the test plan before; AR-1 and AR-2 are them.

**Changed in the claimed cut, re-measured 2026-11-09 (finding K-1, addition 1).** The unknown-service refusal now also lists each
service's methods, bounded (Q9). When services are left out of the list, the pointer says a service not shown is used
by its exact name or UId, and "If the service you need is not registered" replaces "If the service you need is not
listed", so a registered service left out of the list is not sent to registration. AR-1 passes with one refusal
(M-48; the first run on the draft article needed two, M-45); AR-2 passes (M-46).

---

## Q11. Method or service change on modify

**Question.** What does `setElement` with a different `service` or `method` do to the element's parameters and bindings?

**Facts.**
- The designer asks "All parameters will be lost", then `clearParameters` drops every parameter (result params
  included) and their mappings, then regenerates (r2 §method change; g1 (a) step 2). A service change runs
  `canChangeSchema` and a confirm.
- The package has a retarget guard for sub-process: `IProcessDependencySnapshot.FindDependents(element,
  survivingParameterUIds, includeOwner)` refuses a retarget that would drop outputs still referenced (`PB/Elements/SubProcessApplier.cs:753-800`; r4 §2).
- Generated UIds are fresh on every regeneration (g4 §4).

**Options.**
- **A. Replace: all generated params and their bindings are dropped and regenerated; the same service and method sent
  again, in any letter case, is a no-op (no write, no notice; notice N2 fires on create and retarget only); refused
  when other elements reference outputs that would disappear; the notice lists the dropped input bindings so one call restores them.** Every output, `Success` / `Response` / `StatusCode` and a same-named
  output of the new method included, is regenerated with a fresh UId, so any reader of any current output blocks the
  change (design §3.2 step 3), unless EM4 shows otherwise (superseded after EM4 by A3, end of this section: the result
  params keep their UIds). Cost: ~3-5 agent h, reusing the snapshot guard.
- B. Merge by name: keep params and bindings whose name survives. About +2-3 agent h; diverges from the designer and
  keeps bindings whose type may no longer fit.
- C. Refuse a change; require remove + add. ~1 h; every dependent flow and mapping is lost with the removed element.

**Recommendation (PROPOSED): A.**

**What changes it.** EM4 (what the designer clears and keeps on a method change; if it keeps result-param bindings, A
keeps them too).

**Agreed by the owner on 2026-11-03: A** (replace, as the designer does; a same service and method in any case is a
no-op; refused while another element reads an output that would disappear; the notice lists the dropped input
bindings). EM4 is not measured yet; if the designer keeps result-param bindings, A keeps them too.
Partial EM4 (service change, 2026-11-03, during the Q16 check): switching an element from `GeminiApiSearch` to
`UsrWscEchoService` in the designer asks "Are you sure you want to change element schema? All data will be lost.",
then replaces every parameter; Success / Response / StatusCode come back with NEW UIds (`79f8d049...` became
`d3b855d1...` for Success), 4 BK15 rows remain, the caption becomes "WSC echo service 1", the element name stays
`WebService1`. A method change (the rest of EM4) is not measured.

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM4 S1-S3, measurements M-26, M-27).** Process
`UsrWscEm4MethodChange`: a Formula task reading an output and a conditional flow on `Success` after the element.
- Method change (`UsrWscEcho` -> `UsrWscGet`, confirm "Are you sure you want to change method? All parameters will be
  lost."): every parameter, the result parameters included, replaced with new UIds; the 4 BK15 rows re-created (new
  row UIds, same `GT5`); `IX3` and `IX4` kept; caption kept. The dependent Formula task and conditional flow keep the
  removed UIds and are NOT marked invalid (no `BL11`); the save first warns about the unfilled required input, then
  answers "Schema has errors: The "ConditionalSequenceFlow1" element has an invalid value for the parameter
  "ConditionExpression"" and stores the process anyway.
- Service change (-> `UsrWscDriftService.UsrWscDrift`, confirm "... change element schema? All parameters will be
  lost."): the same, plus `IX3` reset (omitted), `IX4` kept, caption renamed "WSC drift service 1", name kept.
- Found on the way (M-27): the designer's formula window resolves an output by caption; "Reference" bound the nested
  INPUT `UsrWscOrdNo_In` instead of the top-level output with the same caption, and the save stored the error.
So A's "replace" matches the designer (and P-D1 keeps `IX3` / `IX4` on a method change, resets `IX3` and keeps `IX4` on
a service change). A's guard (refuse while another element reads an output that would disappear) is STRICTER than the
designer, which saves a process with dangling references. Per the EM4 "If not" row this needed the owner's word.

**Agreed by the owner on 2026-11-03, after EM4: A3** (A with the result parameters kept). A guard over every output
would block nearly every real retarget, because a condition on `Success` right after the element is the common case
and `Success` / `Response` / `StatusCode` exist on every method with a fixed type. So the replace keeps the UIds of
those three output parameters (their readers stay valid, nothing is rewritten in other elements) and regenerates
everything else as the designer does; the guard refuses only when another element or a process parameter reads an output
that really disappears (a tagged output of the old method; the package's dependency scanner counts process
parameters as dependents, so a response mapped into a process parameter blocks the change too); the notice lists the dropped input bindings, `RequestBody` included.
Rejected: A1 (guard over every output, result params included: refuses the common case) and A2 (allow like the
designer and report the dangling readers: the builder would itself write a process that fails validation). The
package has no reference-rewrite helper (`IProcessDependencySnapshot` only finds dependents), so A3 keeps the UIds
instead of rewriting readers; the capture oracle does not compare UIds (serialization W10, measurements oracle rule),
so the kept UIds do not break parity with EM4's capture. Cost: about 0.25 agent h inside slice D's range.

**Draft AC wording (new).** "Changing the service or method replaces the element's parameters as the designer does,
lists the bindings it drops, and is refused while other elements still read an output that would disappear." At
gate 1 (2026-11-03, owner's yes) Jira AC6 was rewritten to match A3 as settled: "Changing the service or method replaces
the element's parameters as the designer does; Success, Http status code and Response body keep their identity, so
steps that read them stay valid. The change lists the bindings it drops, with their values, and is refused while
another element or a process parameter still reads a parameter that would be regenerated."

**Settled at gate 1 (2026-11-03), the lead's clarifications within A3.**
- Dependents: the package's `ProcessElementDependencyScanner` walks `schema.Parameters` too, so a process parameter
  that reads the element is a dependent as well as another element; what is read is stated in the second pass below.
- Caption: the builder follows the designer (M-26): a service change always renames the element caption to
  "<service caption> <N>"; a method change keeps it. `elementUpdate` has no `caption` member: neither the package's
  `ProcessElementUpdateDescriptor` nor clio's `ProcessBlockKeyCheck.ElementUpdate` declares one.
- TC-P-12 input: the EM4 S1 capture with its Formula reader of the tagged output removed first (A3 would refuse
  the change otherwise); the element node and its BK15 rows are compared with the S2 capture under WSCR; the S2
  capture's dangling Formula token is outside the comparison.

**Settled at gate 1, second pass (2026-11-03).**
- Retarget guard, A3 stated exactly: the guard refuses when anything (another element, a process parameter, a flow
  condition, a formula body) reads ANY parameter of the element other than `Success` / `Response` / `StatusCode`:
  tagged inputs, nested items and `RequestBody` included, because all of them are regenerated with new UIds. The
  agreed A3 said "tagged output"; a reader of a regenerated input dangles the same way, so this is A3 stated exactly,
  not a change of it. Call: `IProcessDependencySnapshot.FindDependents(element, survivingParameterUIds: {Success,
  Response, StatusCode UIds}, includeOwner: false)`; the element's own parameters are all replaced, so its own
  references die with them (design §3.2 step 3, plan P-D2).
- TC-P-12: the builder call also maps `UsrWscQ_In` in the same request, as EM4 step 2 did before the S2 save. The
  designer's `BL11:false` on the S2 element (its required `UsrWscId` unbound) is a named WSCR allowed difference
  (serialization §8.3); the package never writes `IsValid`.

---

## Q12. Drift and latent loss

**Question.** What does the builder report, or prevent, when a web-service element no longer matches its service?

**Why it matters.** A data-loss path already exists on `origin/main`: any modify of any element loads the process
through the design path, which re-syncs every WS element; an unresolved method prunes all its tagged params and the save
persists it, with no notice (g4 §3, traced).

**Facts.**
- Measured corpus case: `CrtMsTeamsMeetingTranscript/GetMeetingTranscript` calls two methods its service no longer has;
  an unrelated modify would remove 8 tagged params and leave FormulaTask references dangling without an invalid flag (g4
  §3, traced on measured data).
- On an interpretable process describe already returns the synced list, so a flat in-sync comparison is tautological;
  reliable signals are `serviceResolved`, `methodResolved` and nested `L18` drift, which the server sync never touches
  (g4 §5).
- Showing what will be lost needs the stored list, i.e. a raw metadata read (g4 §6 Q1, unverified).

**Options.**
- **A. Describe reports `serviceResolved`, `methodResolved` and nested drift as issues; a modify that would make the
  platform prune params of an unresolved WS element gets a notice naming the element and method.** Cost: ~2-3 agent h.
- B. Refuse such a modify unless confirmed (a `ConfirmLayoutChange`-style flag), listing the params to be removed.
  About +3-5 agent h (raw read), plus a contract member and its bytes (Q15); blocks routine edits of such processes,
  which the designer also prunes.
- C. Nothing. 0 h; the silent loss stays.

**Recommendation (PROPOSED): A** in v1; B is the owner's option.

**What changes it.** EM8 (on a disposable copy: does an unrelated modify really prune? If it does not, A shrinks to
the describe issues).

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM8, current-builder half).** Process `UsrWscBk15Probe` after
EM11 (Q9): `WebService2` stored `IX2 = "getAppPublishedVersion"` (the service has `GetAppPublishedVersion`) with all 8
tagged params and the 4 result params still in `SysSchema.MetaData`; `WebService1` exact, as the control.
- One unrelated modify through the current clio + CrtProcessBuilder: `addParameter` `UsrWscEm8Marker` (Text). Answer:
  exit 0, "1 operation(s) applied", no warning, no notice.
- Stored bytes afterwards: `WebService2` has only Success, Response, StatusCode, RequestBody; the 4 tagged `_In` and
  4 tagged `_Out` params are deleted from the database. `WebService1` keeps all 12 params and its 4 BK15 rows.
  `IX2` is unchanged (`getAppPublishedVersion`), so nothing in the stored element now says what it lost.
So the latent loss on `origin/main` is real and silent: a modify of something else in the process permanently deletes
the parameters of an unresolved web-service element. It is the "If it does not" branch that is ruled out; A stays as
written (describe issues + a notice on such a modify). Not measured: an element whose lost params had mappings (the
dangling-reference half of the corpus case `CrtMsTeamsMeetingTranscript/GetMeetingTranscript`). Left for cleanup:
parameter `UsrWscEm8Marker` in `UsrWscBk15Probe` (the probe is already on the cleanup list).

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM8, service-save and designer halves; measurements M-29,
M-30, M-31).** Two processes on `UsrWscDriftService.UsrWscDrift` with a reader of the first element's output (the second
element's input and a Formula task); then the method's code was renamed (`UsrWscDriftGone`) and saved in Studio.
- The service save writes nothing to the processes and does not evict a process already in the cache: describe of the
  process described before the save still returned the full set twice afterwards; a process first loaded after the
  save described with only Success / Response / StatusCode per element. Describe gave no issue and no flag either way.
- The designer, opening and saving the process, deleted MORE than the builder's modify did (M-18): every parameter of
  both elements, the result params included, and all 8 BK15 rows; both elements `BL11:false`; `IX2` unchanged; the
  Formula task's token left dangling (the save reported it as a schema error and stored the process).
A stays as agreed (the notice is about the builder's modify, and its text names the tagged params the load removes).
What it adds: the KB article says that a designer save of such a process deletes every parameter of the element and
its BK15 rows, so the remedy (restore the service or method in Studio > Web services, or first remove the reads of the missing parameters, then re-point the element) comes before any designer save; describe's
`methodResolved: false` (Q14) is the signal, because the synced parameter list of a cold load already shows the prune
and a warm cache shows the stored set.

**Agreed by the owner on 2026-11-03: A** (describe reports `serviceResolved`, `methodResolved` and nested drift as
issues; a modify that lets the platform prune an unresolved web-service element carries a notice naming the element and
the method; the modify is not refused). B stays a follow-up option.

**Agreed by the owner on 2026-11-05 (within A): N7 widened.** N7 is raised for every web service element whose
service or method does not resolve, configured in the request or not: a `setElement` that changes only settings does
not resolve the method, and the save still runs the platform's load sync. Package test
`ModifyOfTheElement_ShouldNotice_OnlyWhenItStillDoesNotResolve` (TC-U-114).

**Changed in the claimed cut, re-measured 2026-11-09 (findings F-2 and F-5, measurements section 0; within A).** N7 was an end-of-request notice only, so a
modify the platform's validation refuses never carried it: when something reads a parameter the load pruned, the
check refuses, nothing is written, and the reply said only what the platform said (EM8 step 8, M-41). The package's
pre-save check (`ProcessSchemaValidator.EnsureValidForSave`, which runs the platform's validation) now appends N7 to such a refusal, after the platform's
text: the elements a refused message names first, under a lead that gives the repair order (restore the service or
method in Studio > Web services and repeat, unless the process was saved in the Process Designer since; otherwise
first remove or replace every read of those parameters in a request of its own, then re-point the element and bind
the reads to its new parameters, those two in one request), each in its refusal form, which gives the cause without
the re-select advice (F-5: the advice contradicted the lead); then the other drifted elements with the ordinary
notice; with no named element, the lead "This process also has a web service element that no longer resolves."; at
most 3 notices, the rest counted. Still not refused by the package: the refusal is the platform's. Measured M-51,
M-52; package tests TC-U-115..124.

**Settled at gate 1 (2026-11-03), the lead's clarification within A.** KB-1 (its modify part) and TC-K-05 state
that a designer save of a process whose web-service element no longer resolves deletes every parameter of the element
(result params included) and its BK15 rows and leaves the element `BL11:false` (M-30), so the remedy (restore the service or method in Studio > Web services, or first remove the reads of the missing parameters, then re-point the element) comes before any designer save; describe's `methodResolved: false` is the signal.

---

## Q13. Platform floors

**Question.** What does the builder promise on older platforms?

**Facts.**
- IX4: see Q7 (absent below 8.3.2.3590).
- An inner CompositeObjectList with no source of its own whose leaves are mapped loses its list at run time before
  the ENG-55920 fix (traced; "saves green" unverified, verdict r1-C7; EM12): the pre-fix reader sends no value (null)
  for that inner list, while a mapped inner container is sent. The fix (core #1119, fixVersion 10.1) is absent from
  8.3.4 (CI), 10.0.0.950 and 10.1.37 (the stand); present in 10.1.830 and 10.2.931 (first fixed 10.1 build not
  bisected) (g2 (c), measured); kill-switch `DisableParametersWithNestedArrays`.
- `ServiceParameter.ItemProperties` exists in every measured build: no floor for generating nested params (g2 (c)).
- CI compiles against 8.3.4, so CI cannot show a floor; README records that 8.1.2 lacked APIs the package uses (g2 (a),
  (b)).

**Options.**
- **A. IX4 behind the guard (Q7); a warning notice (design N5) on an IN CompositeObjectList item nested in another
  collection whose own source is None while a leaf below it has a source (the pre-fix defect predicate, verdict
  r1-C7 (a)), naming the boundary; EM9 measures the package's real compile floor before guard vs declared floor is
  fixed.** Cost: ~1-2 agent h for the notice, ~0.5-1.5 agent h for EM9 (likely 0.75, estimate §2.5).
- B. Declared floor 10.1 with the fix. Excludes the stand and every 8.x target from the whole element.
- C. No notice. The inner list is lost at run time with nothing said at build time (whether the save itself is clean
  is not measured, EM12).

**Recommendation (PROPOSED): A.** The notice cannot read the platform's fix state cheaply, so it warns on every
target and names the version line; whether it can be suppressed on fixed builds is decided after EM12.

**What changes it.** EM9, EM12, EM0 (`DisableParametersWithNestedArrays` state).

**Agreed by the owner on 2026-11-03: A**, with the IX4 part already settled by Q7 (direct write, EM9: the package floor
is about 8.3.2 already). What remains is notice N5 on the pre-fix predicate (an IN nested collection with no source of
its own while a leaf below it has one), shown on every target because the fix state is not readable, naming the
affected version line and the remedy (map the container). Suppressing it on fixed builds was to be decided after
EM12; under Q17's E4 the run half of EM12 is not measurable on stand `Creatio`, so N5 stays on every target unless a
later measurement on a fixed build (10.1.830+) with an echo endpoint shows otherwise. An offline harness (the core
request builder from the 10.1.37 and 10.1.830 binaries, as EM9 did for the compile) was offered on 2026-11-03 and
declined by the owner: EM12's run half stays unmeasured and rests on the core unit tests and the ENG-55920 comment.

---

## Q14. Describe read-back

**Question.** What does `describe-business-process` return for the element?

**Facts.**
- Today: `type: ProcessSchemaWebService`, `buildType: null`, no service, method or timeout; tags and nested items are
  reported; unbound inputs are not (r9 S6 corrected; `PB/Describe/ProcessDescriber.cs:226`).
- What describe reads on an interpretable process is already re-synced; nested `L18` is untouched by the sync (g4 §2,
  §5).
- Service param defaults can hold secrets (Q16).

**Options.**
- **A. A `webService` member `{service, serviceUId, method, timeoutSeconds, ignoreErrors, serviceResolved,
  methodResolved, issues[]}` plus the stored parameter tree (direction, tag, type, items, bound), unbound inputs
  included, read from the element and never re-derived from the service; never service param default values.** Cost:
  ~3-4 agent h.
- B. A, with the tree re-derived from the service. Hides drift (Q12) and diverges from the element the run time uses.
- C. Only the four settings; the tree stays in the generic parameters. The agent cannot see unbound inputs it must map.

**Recommendation (PROPOSED): A.**

**What changes it.** EM2 (the capture is read back through describe), EM8 (the issues on a drifted element).

**Measured on stand `Creatio` (10.1.37, CrtProcessBuilder 1.6.6.131), 2026-11-03.** Process `UsrProcess_3389428`
(EM10, designer-built element on `GeminiApiSearch.GenerateContent`, nothing mapped). Stored element
(`SysSchema.MetaData`): `IX1` = the service UId, `IX2 = "GenerateContent"`, no `IX3` / `IX4` (unset timeout, unticked
ignore-errors are omitted); 2 tagged inputs (`Xgoogapikey_In` header, `CType_In`), 8 tagged top-level outputs, 3 of them
`CompositeObjectList` with `L18` items down to three levels (`Candidates_Out.CParts_Out.PText_Out`,
`...GMGSupports_Out.GSGCIndices_Out.GSGCIndicesItemValue_Out`), the 4 result params, 4 BK15 rows. Every input's value
`L8` is empty: the designer copied no service default into the element.
Read-back, two layers (corrected the same day):
- The package's raw `DescribeProcess` (1.6.6.131, `{request:{name}}`), read on `UsrWscEm3Probe` (Gemini element):
  every output carries its `tag` (`L17`) and the whole nested tree as `itemProperties`, down to three levels
  (`Candidates_Out.CParts_Out.PText_Out`, `...GMGSupports_Out.GSGCIndices_Out.GSGCIndicesItemValue_Out`). No unbound input
  is returned (neither a tagged `_In` param nor an unbound `RequestBody`; M-31 later showed that a bound input is
  returned with its value), and there is no service, method, timeout or ignore-errors member. So the fact above ("tags and nested items are reported; unbound inputs are not") holds.
- The `describe-business-process` output seen in this session showed no `tag` and no nested items. That was the MCP
  clio serving this session, an older build: current clio source carries both fields
  (`CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:2192` `tag`, `:2217` `itemProperties`, at `6be5dde28`). It is not a gap of
  the package or of current clio.
So what A has to add is the unbound inputs (tagged `_In`, an unbound `RequestBody`), the bound state of every input and
the `webService` member; tags and nesting are already there. The Q9 EM11 run also shows that describe reads the synced view: the pruned element
was described without its stored tagged params.

**Agreed by the owner on 2026-11-03: A** (a `webService` member with service, method, timeout, ignore-errors, the two
resolved flags and issues; the element's parameter tree with direction, tag, type, nested items and bound state,
unbound inputs included, read from the element and never re-derived from the service; no service default value ever).

**Draft AC wording (now AC7).** "`describe-business-process` returns the service, method, timeout, ignore-errors
flag and the element's parameter tree, including unbound inputs, and reports an unresolved service or method as an
issue."

**Settled at gate 1 (2026-11-03), the lead's clarification within A.** Describe today (1.6.6.131) returns bound
inputs only, with their value or token, source and tag (M-31); unbound inputs and an unbound `RequestBody` are
omitted. What A adds is the unbound inputs, the `bound` / `result` flags and the
`webService` member.

**Changed in review (2026-11-04), the owner's decision.** A was agreed with `service {name, uid}`. A reviewer of the
BMAD docs PR pointed out that every other block keeps its write shape on read and puts describe-only data in its own
read-back key (`readData.sourceSchemaUId`, `subProcess.processCaption` beside `processName` / `processUId`), so a
described block is fed back unchanged with warnings only; an object-valued `service` broke that and needed a clio
shape refusal plus a rewrite step in the replay recipe. Now `service` is the same string the write side takes (the
service name; null when `IX1` resolves to no service) and `serviceUId` is a read-back-only key holding the raw
`IX1`. A described block replays unchanged; the clio object-shape refusal (former TC-C-17) and the rewrite step are
gone.

---

## Q15. Text budgets

**Question.** Where do the new words go, given the byte ceilings?

**Facts.**
- Full tool contracts: ceiling 35,072 B; at `6be5dde28` create 35,033 (39 B left), modify 35,069 (3 B), describe 35,029
  (43 B), measured (r5 C3). The default encoder writes non-ASCII, `'`, `"`, `<`, `>`, `&`, `+` and backtick as 6-byte
  escapes. Every
  recent element ticket paid by swap.
- KB: `process-modeling` has ~13 B under its 19,000 B reply budget; `element-catalog` marks `webService` read-only and
  not buildable, pinned by a KB test; tool descriptions may only name articles listed in
  `curated-knowledge-names.json` (r5 C5, §4).

**Options.**
- **A. Swap, do not raise ceilings; one pointer clause per tool description to a new KB article `process-web-service`;
  flip the catalog's `webService` from read-only to buildable with a version-floor pin; the routing article separates it
  from the configuration-webservice (C# services) guides.** Cost: ~2-3 agent h clio text, ~3-4 agent h KB.
- B. Raise the ceilings. Rejected by the ENG-95985 "Send email attachments" precedent (owner: trim, no raise).
- C. No KB article; rules in descriptions. Does not fit in 3-43 B.

**Recommendation (PROPOSED): A.**

**What changes it.** No EM; the budget tests decide the exact swap.

**Agreed by the owner on 2026-11-03: A** (swap, no ceiling raise; one pointer clause per tool description to
`process-web-service`; catalog flip with a version-floor pin; a routing row separating it from the configuration
web-service guides). The byte headroom above is from `6be5dde28` and is re-measured on the branch.

---

## Q16. Secrets

**Question.** What service data may appear in the builder's output?

**Facts.** Measured in the corpus: 3 corpus services carry literal basic-auth credentials; 1 product service carries
literal bearer header defaults; others use sys-setting references (g5 (c)). The designer's service read returns the
service's authentication settings; the builder never calls it and never echoes it (traced). The element params
themselves carry Source None; the defaults live in the service.

**Options.** **A. No service param default values, auth info or base-URI credentials in describe, refusals, notices,
logs or test snapshots; refusals list names only.** ~1 agent h (tests asserting absence). B. Echo defaults to help the
agent: leaks secrets into transcripts and Jira attachments.

**Recommendation (PROPOSED): A.**

**What changes it.** EM1 (the test service carries a non-secret header default, and a test asserts it is never
echoed).

**Agreed by the owner on 2026-11-03: A.** Supporting measurement (Q14, 2026-11-03): the designer-built element on
`GeminiApiSearch.GenerateContent` stores empty values for every input, the API-key header `Xgoogapikey_In` included,
so service defaults stay in the service and the element itself carries none.

**Measured on stand `Creatio` (10.1.37), 2026-11-03 (EM1, the Q16 part only).** A reduced `UsrWscEchoService`
("WSC echo service", `2ac0b905-591d-4623-a8bf-990846cea7d3`, package `Custom`, base URI = the stand root, i.e. EP
option E4 for now): one GET method `UsrWscGet` (`/UsrWscGet`) with one header parameter `UsrWscProbe` (header
`X-Wsc-Probe`, not required, default Const `wsc-probe`). Q17 then chose E4 (this base URI); the full EM1 shape set extends this service.
- Stored service metadata: the parameter is `IU1 "X-Wsc-Probe"`, `IU2 Text`, `IU4 4` (header), `IU5 {IV1 "wsc-probe",
  IV2 1}` (Const), as traced.
- Designer element on it (the EM10 process `UsrProcess_3389428`, its element switched from `GeminiApiSearch` to this
  service in the designer, then saved): the panel shows the header input as "Select value" (no default offered);
  the stored element has `UsrWscProbe_In` with an empty `L8`, and the string `wsc-probe` occurs nowhere in the process
  metadata.
- `describe-business-process` (1.6.6.131) on the same element returns only Success / Response / StatusCode: no unbound
  input (only bound inputs come back, M-31), so nothing of the default either.
So the default lives only in the service schema; neither the designer's element nor today's describe carries it.
The A rule ("never a service default") costs nothing to keep for the element tree; the designer's service read
returns the service's authentication settings, and the builder never calls it and never echoes it. Not measured: what the
run time sends for an unbound header with a default (needs an echo endpoint, EP), and `authInfo` (an auth block was
not created: the agent does not type credentials, even fake ones; the owner can add one if this needs measuring).
Found on the way: Studio refuses a parameter "Code in Creatio" without the `Usr` prefix ("Code of the method must
contain prefix "Usr""), so the EM1 parameter codes in the measurements plan (`WscText`, `WscReq`, ...) must become
`UsrWsc...`. Left for cleanup: service `UsrWscEchoService` (kept: it is the planned EM1 service).

**Draft AC wording (new).** "No service parameter default, authentication value or credential is returned in any
builder output." At gate 1 (2026-11-03, owner's yes) Jira AC8 was rewritten to the agreed scope: "No data stored in the
web service itself (parameter default values, authentication settings, credentials in the base URI) is returned in any
builder output. The element's own bindings are returned the way describe returns them today; the guidance tells agents
to take keys and tokens from a system setting or a process parameter, never a constant."

**Settled at gate 1 (2026-11-03), the lead's clarifications within A.**
- Scope: no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output; the
  element's stored bindings are returned by design. The mapped values (constants, formulas, `RequestBody` text) are
  the element's stored bindings (process data the caller can already read through describe, M-31), not service data:
  describe returns a bound input's value through the generic `parameters[]` as today (the round-trip tests TC-U-68,
  TC-U-78 and TC-I-06 need it); the `webService.parameters` tree carries no value. The residual risk (a key typed
  as a constant in the designer) is recorded once, in design 4.16. The KB article tells agents that keys and
  tokens come from a system setting or a process parameter, never a constant.
- N4: the dropped input bindings are listed in the write shape WITH their values (constants and formulas alike,
  `RequestBody` included), so one call restores those that still apply. The earlier rule that replaced a constant's
  value with a placeholder is reversed: it made N4 inconsistent with describe and is not required by this decision.
- Fixtures: A and AC8 govern builder OUTPUT (describe, refusals, notices, logs, test snapshots of builder
  output). The pattern-A corpus fixtures are copied verbatim from the shipped PackageStore corpus into the internal
  package repository and SHA-pinned, so they are not redacted; they may carry the shipped services'
  non-credential `IU5` defaults; TC-P-08 asserts that they carry no credential (no `IQ5` value with `IV2` 1, no Const
  `Authorization` default).

---

## Q17. Stand captures and runs

**Question.** Who drives the designer captures and run-time probes, and against what?

**Facts.** Stand `Creatio` has no SOAP service, no service with an auth block and no service with a Body request
parameter; its only POST builds raw JSON in `RequestBody` (r9 S4, measured). The ENG-95985 precedent: the lead drove
captures in Claude in Chrome, the owner logged in (ENG-95985 Q18). Isolated TC runs cost little agent time (r10 §2).

**Options.**
- **A. The lead drives the captures in Claude in Chrome on stand `Creatio` (the owner logs in when asked); test REST
  services `UsrWsc*` are created there (`UsrWscEchoService`; `UsrWscDriftService` for EM8/EM11 and, through its own
  method `UsrWscTc12`, manual TC-12; `UsrWscDeadService` for EM7 and TC-I-09, a base URI with no listener;
  measurements §3.2) whose base URIs point at the same stand host (no external
  host), so run-time probes need no internet; which endpoint answers, and with which non-secret authentication, is
  settled in EM1; everything created is listed for cleanup.** Owner ~1-2 h (estimate §3: Chrome sessions plus
  stand-write approvals); lead 3.25 / 4.5 / 7 agent h for EM1-EM4 (estimate §2.5 per-EM rows, before batching).
- B. The owner builds EM1 by hand (~45 min) and the lead only reads metadata.
- C. An external echo host. Adds an internet dependency and a host outside the stand.

**Recommendation (PROPOSED): A.** Every stand write is asked for first, with the list of objects it creates.

**What changes it.** EM0, EM1 (if no same-stand endpoint fits, the owner chooses B or C).

**Agreed by the owner on 2026-11-03: A with EP option E4** (no echo endpoint: the test services' base URI is the stand
root, so they receive the stand's own HTTP answers; measurements §3.2). Consequences: EM1-EM5, EM7, EM8, EM10, EM11 run
as planned; EM6's request-byte check and EM12 (the nested list on 10.1.37) are not measurable on stand `Creatio` and rest
on the core unit tests and the ENG-55920 comment; EM6's response read needs another JSON source or is dropped to the
result params. `UsrWscEchoService` already exists with this base URI (Q16 measurement).

---

## Q18. Jira description corrections

**Question.** Which statements in the ENG-92710 description are corrected, and how?

**Facts.** "Task 21" is Task 22 in the Confluence index (v43) and in clio `spec/process-design-service/task-list.md:157`;
"Task 5" (composite types) moved to Task 37 = ENG-92730; the element is the core `ProcessSchemaWebService`, not a user
task; the "~3 days" is the Confluence "AI Est." column, defined as working days for one person including review, QA
and capture, not AI-coding days (r1 §1, C11 corrected).

**Options.** **A. The lead edits only the description (never the checklist field), after the owner approves the text,
and reads it back.** B. The owner edits by hand.

**Recommendation (PROPOSED): A.** The AC drafts in Q2, Q6, Q7, Q9, Q11, Q14 and Q16 are folded into one text block for
approval once those decisions are answered; nothing is written before.

**Agreed by the owner on 2026-11-03: A.** The text also states that the element calls registered services only and
points to ENG-105475 "Register a REST web service through the AI Toolkit" for registration. The AC block is drafted
after Q20 and shown before any Jira write. Written to the ENG-92710 description on 2026-11-03 after the owner's yes and read back
(text: `eng-92710-call-web-service-element-jira-description-draft.md`); the checklist field was not touched.

---

## Q19. PR split and order

**Question.** How many PRs, in which order, and how are package cuts claimed?

**Facts.** ENG-95985 shipped as 4 PRs (BMAD docs, package, clio, KB) and 6 cuts; ENG-92707 "Sub-process element: selection + parameter sync" as 6-7 PRs with 19 cut tags
and 27 descriptor versions on its branch (r10 C8, C14 corrected). Merge order package -> KB (published) -> clio is
not forced by a test: package-first is a team convention (ENG-95985 Q13, no test); KB-before-clio is policy, and the
curated-names fixture test turns red only while a tool description names an unpinned guide (r5-C5).

**Options.** **A. Docs-only BMAD clio PR first; package PR; clio-knowledge PR; clio PR; cut numbers claimed in a claim
comment on ENG-92710 (the experimental stand-only stamp too, marked there "experimental, stand-only, superseded", the
ENG-95985 precedent, verdict r10-C14); merge order package -> KB (published) -> clio; merges are human.** B. Fold the BMAD documents into
the clio PR: one PR less, a larger review.

**Recommendation (PROPOSED): A.**

**Agreed by the owner on 2026-11-03: A** (BMAD docs PR, package PR, clio-knowledge PR, clio PR; cuts claimed in an
ENG-92710 comment, the stand-only experimental cut included; merge order package -> KB -> clio; merges are human).

---

## Q20. Legacy and edge cases

**Question.** What happens to a multi-instance web-service element and to SOAP methods?

**Facts.** Measured: 0 multi-instance and 0 SOAP calls among the 66 corpus elements; SOAP service schemas exist only
as install fixtures (r6 C5, r6). The platform runs a SOAP body through a different builder (`SoapRequestBodyBuilder`,
r3); stand `Creatio` has no SOAP service (r9).

**Options.**
- **A. Refuse multi-instance on `webService`; build SOAP only if EM2 shows an identical parameter shape, else refuse
  SOAP with a notice.** ~1 agent h.
- B. Support both. No corpus case and no stand service to capture from; the parity claim would be unproven.
- D (added 2026-11-05, after the SOAP capture). Refuse multi-instance; build SOAP exactly like REST on every path, with
  notice N10 naming the element. Evidence: EM2 S2-S4 (M-32..M-35).

**Recommendation (PROPOSED): A.** EM2 as planned captures a REST method on EM1's service, so unless a SOAP capture is
added with the owner's word, the default outcome of A is the refusal.

**What changes it.** EM2 (extended to a SOAP method only if the owner asks).

**Agreed by the owner on 2026-11-03: A** (multi-instance refused; SOAP refused with a notice unless an owner-requested
SOAP capture shows the same parameter shape). Stand `Creatio` has 3 service schemas, all REST (measured 2026-11-03).

**Measured 2026-11-05 (EM2 S2-S4, M-32..M-35, `CAP/em2-soap/`).** The owner asked for the SOAP capture, A's condition.
On a SOAP 1.1 service entered by hand on stand `Creatio`, the designer builds the element exactly as for REST (OF-16):
the same element keys, the same tree (names, tags, nesting, types, `L6` from `IU7`, directions), the same result
parameters and the same 4 BK15 rows. No SOAP-only service field (`IQ7`, `IR5`, `IT1`, `IU1`, `IU8`) reaches the
element; only the service `IX1` points at tells the protocol. A timeout-only save changes `IX3` alone. A switch to a
REST service is a full rebuild with no SOAP leftover. Caveat: the WSDL wizard did not load, so the service was entered
in the SOAP card, which forces every Object parameter to an array; a non-array complex type is unmeasured, and the
shaper's existing non-array-object refusal covers it for SOAP as for REST.

**Agreed by the owner on 2026-11-05: D** (the condition of A is met). SOAP services are built exactly like REST on
every path: create, a retarget onto a SOAP service, and `setElement` of a stored SOAP element (settings or retarget).
Create and retarget onto SOAP raise notice N10, which names the element; a settings-only change does not.
Multi-instance stays refused. Describe reports a SOAP element as an ordinary element.

---

## Boolean constant at run time (owner, 2026-11-05)

**Agreed by the owner on 2026-11-05: a Boolean constant is checked at run time, A + B in one run on the final cut**
(measurements EM6-B). The designer stores a Boolean constant as Script `[#BooleanValue.True#]` (M-33); the builder's
`value: "true"` goes through the shared mapping and stores ConstValue `{GS1: 1, GS2: "true"}`. None of the 66 web
service elements of the PackageStore corpus (38 files) has a Boolean input parameter, so the corpus cannot answer it.
- A: the builder creates a web service element with `value: "true"`; a downstream conditional flow on
  `[#WebService1.<flag>#] == true` shows the run-time value through `SysProcessElementLog`.
- B: process tracing on for the user (`IsTracing`) in the same run, to see the parameter values and the request body.
  If the trace shows no body, the value-to-JSON step (`JsonRequestBodyRenderer`) is closed by a source trace, labelled
  traced.
- Only if A shows a value other than `true` do web service elements write the designer's Script form. No code change
  before that.

**Measured 2026-11-09 (EM6-B, M-39), final-cut campaign.** By trace (oracle B) the builder's ConstValue holds
`UsrWscFlag_In = true` at run time; the trace also shows a one-item list from an item mapping and an unmapped
`RequestBody` as null (not ""). Oracle A cannot be saved: the platform refuses an element input as a condition data
source (finding F-1, a plan defect, resolved 2026-11-10: the oracle is dropped from the EM6-B steps). Outcome: the shared ConstValue encoding stays, no code change; the TC-P-15
companion test pins the one-binding difference from the designer's Script form permanently. Re-run on the shipping build
2026-11-10 with the same trace (M-55).

---

## Decisions table

| Q | Agreed 2026-11-03 | Blocks which plan step | Cost (note-level sizing from r10/g3/g5; recommended options are inside estimate §2, not additive) |
|---|---|---|---|
| Q1 | build `ProcessSchemaWebService`; refuse legacy user task | package: element handler | in base, WP B (the legacy-route refusal sits in WP C, P-C2) |
| Q2 | `webService` block + existing mappings, stored names | package: contracts; clio: key check | in base, WP B; inline request map (B) +4-6 h, aliases (C) +2-3 h (note-level, no estimate §6.1 row) |
| Q3 | package generator, nested items always (no `L18` gate, no N11) | package: generator + parity | in base, WP A; hybrid (B) +2-3 h (note-level, no estimate §6.1 row) |
| Q4 | emit 4 BK15 rows | package: P-B4, P-C4; EM5 gate | in base, WP B; skip BK15 (B) -0.5-1 h (estimate §6.2) |
| Q5 | item mappings + `RequestBody`; no JSON check or conflict notice in v1 | guidance, fixtures | in base, WP M; CompositeObject type (B) +10-15 h (note-level, no estimate §6.1 row) |
| Q6 | declared Collection <= 2 levels; WS mirror refusals point to it | package: response tests, P-C6; KB article | in base, WP M (C13 mirror refusals in WP C, P-C6); mirror/ObjectList/3 levels (B) +15 h, +2-4 owner h (estimate §6.1 row 1) |
| Q7 | `ignoreErrors` written to IX4 directly (EM9 outcome (a), done) | package: applier + describe | in base, WP B (P-B3); the guard is dropped, -0.5-1 h (estimate §6.2) |
| Q8 | seconds, stored ms | package: contracts | in base, WP B |
| Q9 | UId/name/caption, canonical, listing refusals | package: resolver | in base, WP A; package dependency like the designer (C) +3-5 h (note-level, no estimate §6.1 row) |
| Q10 | no discovery tool; registration pointer in the refusal; KB "task without details"; registration = ENG-105475 | scope of clio PR | in base (same-request ordering in WP B, P-B5); clio tool (B) +8-12 h, +1-2 owner h (estimate §6.1 row 2) |
| Q11 | replace + retarget guard over every parameter except `Success` / `Response` / `StatusCode`, which keep their UIds (A3) | package: modify | in base, WP D; merge by name (B) +2-3 h (note-level, no estimate §6.1 row) |
| Q12 | describe issues + modify notice | package: describe, modify | in base, WP D; refusal with confirm (B) +3-5 h, +0.5 owner h (estimate §6.1 row 3) |
| Q13 | IX4 direct (Q7); sourceless nested-list notice N5 on every target | package: notices (P-C3) | in base, WP C (P-C3); EM9 spent (estimate §2.5) |
| Q14 | `webService` member + stored tree | package: describe; clio DTO | in base, WP C (P-C1); clio DTO in estimate §2.2 (CL-7) |
| Q15 | swap; KB `process-web-service` | clio PR, KB PR | in base, clio/KB estimate §2.2-2.3; disputed swap +0.5-1.5 h (estimate §6.1 row 8) |
| Q16 | no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output; the element's stored bindings are returned by design | all outputs, tests | in base, WP A (resolver refusals) and WP C (describe, P-C1) |
| Q17 | lead in Chrome, same-stand test services, EP option E4 (stand root, no echo) | day 0: plan 0.5, 0.7, 0.8 (EM0-EM4); 0.9 session 2, optional | in base, estimate §2.5: 3.25 / 4.5 / 7 agent h for EM1-EM4 (before batching); owner Chrome sessions and stand-write approvals, estimate §3; no on-stand endpoint +1.5-3 h (estimate §6.1 row 7) |
| Q18 | lead edits description after approval | Jira, after answers | in base: draft in day 0 (estimate §2.5), owner 0.2 / 0.3 / 0.4 h (estimate §3) |
| Q19 | 4 PRs, claim comment, package -> KB -> clio | every cut and merge | in base (estimate §4; cut cycles in §2.5) |
| Q20 | refuse multi-instance; SOAP built like REST with N10 (D) | package: handler, notices | in base, WP C (P-C2) |
