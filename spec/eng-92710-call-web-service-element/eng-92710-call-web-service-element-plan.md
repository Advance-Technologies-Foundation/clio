# ENG-92710 Call web service element: work plan

Written 2026-11-02. The order of work, the work packages per repository, the PR split, the gates,
and the definition of done. It follows the answers Q1-Q20 the owner agreed on 2026-11-03 (the "Agreed by the owner"
lines of the [decisions file](eng-92710-call-web-service-element-decisions.md), which override its PROPOSED text).
The [design file](eng-92710-call-web-service-element-design.md) says
what each component does; the [measurements file](eng-92710-call-web-service-element-measurements.md) owns EM0-EM12;
the [test plan](eng-92710-call-web-service-element-test-plan.md) owns the test ids; the
[estimate](eng-92710-call-web-service-element-estimate.md) owns the hours. Status 2026-11-10: package slices A-D are
implemented; the final-cut campaign ran on 2026-11-09 (M-36..M-46) and the unknown-service refusal, AR-1 and EM8 step 8
were re-measured on the claimed cut (M-47, M-48, M-51, M-52; findings F-2, F-5, K-1 in measurements section 0); the
late-measurement gate (section 6) is met 2026-11-10: EM6, EM6-B, EM7, EM8 step 8, EM10, EM11, EM12 and KB-7 AR-1 re-run on the shipping build (M-53..M-58); EM5 not repeated (the later changes touch only refusal texts and the validator); the pull requests (package, knowledge, clio, docs) are open and in review, and both stories are `review` in `spec/sprint-status.yaml`. This folder was written on the clio plan branch `feature/ENG-92710-call-web-service-element-plan` (pushed
2026-11-03) and merged with the BMAD docs PR (clio #1890, 2026-11-04). Jira on 2026-11-03: ENG-105475 "Register a REST web service through the AI
Toolkit" created; the ENG-92710 description written after
the owner's yes and read back (step 0.2). On 2026-11-03 the day-0 probes and captures wrote to stand `Creatio`: the
services `UsrWscEchoService`, `UsrWscDriftService` and `UsrWscDeadService` (EM1), the `UsrWsc*` probe and capture
processes, and `UsrProcess_3389428` in the new package `UsrWscEm10Proc`; all are on the measurements leftovers
register.

Basis: crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history), clio `origin/master`
`6be5dde28`, clio-knowledge `origin/master` `5f14ceb` (libraryVersion 1.15.176), core platform trunk at
`468c4c71b` (8.3.3.1722), stand `Creatio` (Creatio 10.1.37, .NET Framework, CrtProcessBuilder
1.6.6.131) read-only on 2026-11-02 and measured on 2026-11-03 and 2026-11-05 (measurements M-01..M-31 on 2026-11-03, M-32..M-35 on 2026-11-05;
M-36..M-52 on 2026-11-09 on the registered test stands `eng105744-web02` and `eng92710-web03`; M-53..M-59 on
2026-11-10 on `eng92710-web03` (M-53..M-58) and `eng92710-web04` (M-59); decisions Q4, Q6, Q7, Q9,
Q11, Q12, Q14, Q16).
Sources outside the public repositories (the CrtProcessBuilder package and its tests, CrtProcessDesigner and the other
shipped packages, the core platform) are cited by schema, class, file or view name only, never by repository path;
`CLIO/`, `KB/` = the clio and clio-knowledge repository roots. Component names below are working names; the design file is authoritative.
Tags: **traced** = read in source (a hypothesis about run time), **measured** = observed in data, corpus or on the
stand; each traced claim the plan leans on names the EM that settles it. The research notes `r1`..`r10`, the gap
notes `g1`..`g5` and their verdicts are local research notes, not in this repository.

## 0. Critical path

```
Q1-Q20 agreed 2026-11-03 ──> day 0: claim comment, drift check (Jira text, EM0, EM9 done)
      │                         │
      │                         └──> EM1, EM2 / EM3 / EM4 captures (AC5 oracle), EM7 designer half (done) ──> 0.9 EM8 halves
      │                                                        │
      └──> BMAD docs PR (clio) ── merged before the package PR leaves draft
                                                               v
              package slice A (resolver + shaper, behaviour-neutral seams) ──> slice B (handler, applier,
              block, BK15, same-request ordering)
                                                               │
                                experimental cut, install ──> EM5 STOP gate (designer re-save diff)
                                                               │
              slice C (describe, guards, notices, parity suite) <──┘
                                   │
              slice D (modify: replace semantics, retarget guard, drift issues)
                                   │
              gate 1 ──> claimed cut, install ──> EM6, EM7, EM8, EM10, EM11, EM12 (builder halves)
                                   │
              final cut, install ──> EM2/EM5 re-run against the final archive
                                   │
              KB PR (article, catalog flip, fact-check) ──> clio PR (rebundle, keys, floors, budgets, e2e), draft
                                   │
              gate 3: package + KB ──> ready, reviewers (on the owner's ask) ──> human merge: package, then KB
                                   │
              KB generation published ──> clio re-pin, clio gate 3 ──> ready, reviewers ──> human merge
```

The STOP gate is the residual risk of Q3/Q4: nothing yet proves that the designer opens a server-built element
(package-generated L18 items, ItemValue children, caption resources, result params, first package-authored BK15 rows)
and re-saves it without rewriting it (traced only: g1, g4). The Q4 probe (2026-11-03, decisions Q4) showed it for a
designer-built element only: a re-save keeps its 4 BK15 rows unchanged, and a row-less copy saves and runs. EM5
settles it for the builder's element: it passed on the experimental cut (2026-11-04) and in the final-cut campaign
(2026-11-09, M-37; steps 4 and 5 not run), so the STOP gate holds. If EM5 shows a rewrite, a duplicate
BK15 row, or a lost Success L8, work stops, the diff goes to the owner, Q4 (and possibly Q3) is re-opened, and the
estimate is redone before slice C starts.

No package code is written before the SPEC and the ADR of the BMAD docs PR exist on its branch (AGENTS.md: no code
until the ADR exists); they need not be merged for slice A to start.

## 1. Phase 0: decisions and day 0

| Step | What | Who | Output |
|---|---|---|---|
| 0.1 | Answer Q1-Q20 | owner | **done 2026-11-03**: all twenty agreed (decisions file); the changes from the recommendation (Q3, Q5, Q7, Q10, Q13, Q17) are carried into design, test plan, estimate and this plan by the 2026-11-03 sweep |
| 0.2 | **done 2026-11-03** (written after the owner's yes, read back the same day; the 9 AC are the ones in the [Jira description draft](eng-92710-call-web-service-element-jira-description-draft.md)). Jira description corrections (Q18): Task 21 -> Task 22, Task 5 -> Task 37 / ENG-92730 "Complex / structured parameter types", element identity (core `ProcessSchemaWebService`, not a user task), estimate unit (Confluence 4758143001 "AI Est." = working days, 1 person); the element calls registered services only, registration is ENG-105475 "Register a REST web service through the AI Toolkit"; the AC block folded from the Q2, Q6, Q7, Q9, Q11, Q14 and Q16 drafts | lead drafts, owner approves the text; the lead edits only the description, only after that yes, and reads it back | corrected description; the HeroCoders checklist (if any) stays untouched |
| 0.3 | Claim comment on ENG-92710 (Q19): an empty claim table; every later cut number is claimed there after checking the tags of both repositories | lead, on the owner's yes | comment id recorded at the top of this file |
| 0.4 | Drift check: `git log da719366..origin/main` over the package's Elements, Mappings, Parameters, Describe, Operations, Contracts, Design and Graph folders, `git log 6be5dde28..origin/master -- clio/Command/ProcessModel clio/Command/McpServer clio.tests/Command/McpServer`, `git log 5f14ceb..origin/master -- guidance/mcp/guides/processes bundle-source.json`; re-read anything that moved (fetch first: a stale ref is not evidence of absence). Found at gate 1 (2026-11-03): clio master `f86d8098f` rebundled CrtProcessBuilder 1.6.6.136, whose server refuses undeclared request keys (`UnknownRequestKeyDetector`, `ReadBackKeys`); clio's key set is still needed for older servers, the read-back-only keys are derived automatically because the write and describe members are both keyed `webService`; nothing to register (P-B1 / P-C1; decisions Q2, gate-1 note) | lead | a drift note at the top of this file |
| 0.5 | **done 2026-11-03** (M-22: client `ProcessParameterCollections` and `AutoAddPackageDependenciesInProcesses` true, `DisableIgnoreErrorsInWebServiceUserTask` and `DisableParametersWithNestedArrays` false; versions as M-01, M-02). EM0 read-only baseline: features `DisableIgnoreErrorsInWebServiceUserTask`, `DisableParametersWithNestedArrays`, `ProcessParameterCollections` (a platform fact for EM6 only: the builder does not gate on it, Q3), `AutoAddPackageDependenciesInProcesses` (already read ON in the designer page, decisions Q9 EM10); platform and package versions | lead, read-only | measurements file |
| 0.6 | EM9 compile floor | lead | **done 2026-11-03** (local, decisions Q7 EM9 block): `origin/main` already fails to compile on 8.3.1.4498 and 8.1.3.6947 for other reasons and builds on 8.3.2.4199, so IX4 is written directly (Q7, Q13); no guard, no declared floor |
| 0.7 | **done 2026-11-03** (M-19, M-23; Q17, EP option E4: base URI = the stand root, no echo endpoint). EM1 test REST services, as [measurements §3.2](eng-92710-call-web-service-element-measurements.md) defines them: `UsrWscEchoService` (`2ac0b905-...`, package `Custom`) with `UsrWscEcho` (POST: Body scalars, a required scalar array, an object array with a nested object array and a nested scalar array, flattened `$.customer.*` scalars; response object array nested 3 levels, scalar array, scalars, shared captions "Reference" and "Status"), `UsrWscGet` (URL segment, required Query, header `UsrWscProbe` with the non-secret default Const `wsc-probe`) and `UsrWscMissing` (static path `/UsrWscNoSuchRoute.js`, which answers 404, M-28); `UsrWscDriftService` (`7dd8d33d-...`: `UsrWscDrift`, `UsrWscTc12`, and the 2-level response method `UsrWscOrders2` added for EM3 route (b)); `UsrWscDeadService` (`69899196-...`: base URI `http://127.0.0.1:1`, the owner's pick, method `UsrWscDead`). Every service-parameter code carries the `Usr` prefix (Studio refuses a code without it) | lead in Claude in Chrome, owner logs in; owner's yes for each write | UIds and metadata of the three services in the measurements file; leftovers register kept |
| 0.8 | **done 2026-11-03**. EM2 capture A (the AC5 oracle, M-24): `UsrWscEm2CaptureA` on `UsrWscEcho` matches every expected row; new: the " - Input" / " - Output" suffix applies to top-level parameters only, a Text constant keeps its value only in the `.Value` / `.DisplayValue` resources, an unbound Boolean In is `L8 {"GS2":"false","GS5":<P>}`. EM3 route (a) on `GeminiApiSearch` (M-15: "Create from element" makes a CompositeObject wrapper, ENG-92730 scope) and route (b) on `UsrWscDriftService.UsrWscOrders2`, process `UsrWscEm3CaptureB` (M-25: the designer binds a declared Collection item-first and then binds the container itself). EM4 S1-S3 (M-26, M-27): a method change and a service change ("All parameters will be lost") replace every parameter with new UIds, result params included, and re-create the 4 BK15 rows; `IX3` kept on a method change and reset on a service change, `IX4` kept; caption kept on a method change, renamed on a service change; dependents keep dangling UIds and the save stores the process with "Schema has errors". EM2's read-back (step 9) done in the final-cut campaign 2026-11-09 (M-36); not repeated on the shipping build (section 6) | lead in Claude in Chrome; owner's yes per save | SysSchema.MetaData + SysLocalizableValue rows for the pattern-B fixtures, committed redacted (stand address -> `<stand>`, measurements rule 11; [serialization §8.1](eng-92710-call-web-service-element-serialization.md)) |
| 0.9 | Measurements session 2 ([measurements §6](eng-92710-call-web-service-element-measurements.md)). Done 2026-11-03: EM8's current-builder half (M-18, decisions Q12: an unrelated modify through current clio deleted the 8 tagged params of an unresolved element, no warning), EM10's and EM11's designer halves (M-16, M-17, decisions Q9), EM7's designer half (M-28, decisions Q7: an HTTP error status never fails the element; `IX4` false fails the element and the process only on a transport failure; with `IX4` true that failure gives Success false, StatusCode 0). EM12's run half is not measurable under E4 (Q17). EM8's service-save and designer halves done the same day (M-29..M-31, decisions Q12: a service save evicts nothing and a cold load prunes; a designer save of an unresolved element deletes every parameter and its BK15 rows, so the KB puts the remedy before any designer save). Optional, may slip to day 1; the builder halves run on the claimed cut after gate 1 | lead in Claude in Chrome; owner's yes per write | EM8's service-save and designer results recorded before slice D's notice text (P-D3); leftovers register updated |

EM2 S2-S4, the SOAP capture, ran on the owner's request on 2026-11-05 (M-32..M-35): the designer builds a SOAP element
with the REST shape, so Q20 is D (SOAP built like REST, notice N10). Step 0.9 is optional: if the capture campaign has no slack it slips to day 1; it never blocks slice
A, but EM8's result is read before slice D writes the N7 notice text.

## 2. Phase 1: BMAD docs PR (clio, docs-only)

Branch `docs/ENG-92710-call-web-service-element-spec` from `origin/master`; PR title
`ENG-92710 Call web service element` (Jira title verbatim). Draft until the owner approves the content.

| File | Content |
|---|---|
| `spec/prd/spec-eng-92710-call-web-service-element.md` | the feature as a SPEC (small feature, fewer than 5 stories): the 9 Jira AC of the ENG-92710 "Call web service element" description (AC1-AC9), scope (registered services only), out of scope (ENG-92730 "Complex / structured parameter types" mirror relaxation, ObjectList declaration, 3+ declared levels, the designer's "Create from element" CompositeObject wrapper; discovery tool; registering a service, ENG-105475 "Register a REST web service through the AI Toolkit"; multi-instance) |
| `spec/adr/adr-eng-92710-call-web-service-element.md` | Q1 (element identity), Q2 (contract on the existing mapping surface), Q3 (package-owned generator, nested items always, no feature gate), Q4 (BK15 rows), Q6 (declared-only response), Q7 (IX4 written directly, EM9), Q10 (no discovery op), Q11 (replace semantics) as decisions with their rejected alternatives |
| `spec/stories/story-eng-92710-call-web-service-element-1.md` | build + describe: package slices A-C, KB, clio; DoD = one box per section 9 row AC1, AC2, AC3, AC4, AC5, AC7 (describe), AC8, AC9, plus the general row |
| `spec/stories/story-eng-92710-call-web-service-element-2.md` | modify semantics and drift: slice D, its KB and clio lines; DoD = section 9 rows AC6 and AC7 (drift issues, notice N7) |
| `spec/test-plans/tp-eng-92710-call-web-service-element.md` | from the [test plan](eng-92710-call-web-service-element-test-plan.md) |
| `spec/sprint-status.yaml` | both story rows as `in-progress` (implementation has started by the time this PR merges, so `ready-for-dev` is never true), after the header comment line `  # --- eng-92710-call-web-service-element (ENG-92710 Call web service element) - added 2026-11-03 ---`; story 1's row carries `blocks: [story-eng-92710-call-web-service-element-2]` (story 1's replay rows close together with story 2); ENG-92730 has no story row in the tracker, so the story files name it as related. The clio feature PR flips the rows to `review` when it opens; `done` is flipped in the next clio PR that touches the tracker after the last merge, with only the really ticked DoD boxes |
| `spec/eng-92710-call-web-service-element/` | this folder, after a grep for the stand address (must be 0) and for any credential or secret-bearing default (must be 0, Q16) |

Review: gate 1 is the full 3-lens fan-out over the complete diff (section 7 lenses; for a docs-only PR the
correctness lens checks the docs against the code, plus a consistency pass), as AGENTS.md requires before every PR; gate 2 skips docs-only commits. It merges before the package PR
leaves draft. Pushing the branch and opening the PR wait for the owner's word.

## 3. Phase 2: package (crt-process-builder)

A local package worktree (short path: net472 MAX_PATH), branch
`feature/ENG-92710-call-web-service-element` from `origin/main`. Junction `.application` from the main checkout before
the first build. The local core-bin is 10.1.37 while CI compiles against 8.3.4 (g2, measured): both expose
`IsIgnoreErrors`, but keep platform UIds (ManagerItemUId `652b598d-...`, DataValueType UIds) as literals in tests so
a newer local core cannot hide a CI difference.

Validation for every commit: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
and `-c dev-n8`; every new fixture `[TestFixture(Category = "UnitTests"), Category("PreCommit")]` (`CiContractGuardTests`
fails otherwise).

### 3.1 Slice A: resolver, shaper, behaviour-neutral seams

| Task | Component | Done when (exit criteria) |
|---|---|---|
| P-A1 | Service catalog / resolver (Q9, Q10): service by UId, then code, then caption, case-insensitive, ambiguity refused; method by code, case-insensitive within the service; returns the canonical spelling and the manager-item UId; refusal texts list codes with captions of the services, or of the resolved service's methods; the unknown-service refusal also gives each listed service's methods, bounded (first 30 services, at most 8 methods each; services 31-100 by name and caption; the rest counted; when the list is cut, a service not shown is used by its exact name or UId), and an ambiguous match lists the matching names with their methods, no caption (K-1, measurements section 0; the unknown-service refusal measured M-47, the ambiguous one pinned by TC-U-17); the unknown-method refusal lists method codes without captions (F-3, open); the unknown-service refusal adds that a missing service has to be registered first in Studio > Web services (base URI, methods, authentication); an environment with no registered service gets its own refusal, with no empty list; reads no parameter default, auth info or base URI into any text (Q16). Behind an interface in DI; reaches `ServiceSchemaManager` through `SchemaManagerProvider.GetManager("ServiceSchemaManager")` | unit tests over a substituted manager (`TestUserConnection.SetServiceSchemaManager`, r8): each resolution order, wrong case canonicalised (EM11's designer half measured the platform failure this prevents: saves green, load prunes, run fails with `ItemNotFoundException`), two case-only twins refused, unknown name lists the candidates with the registration pointer, an empty manager refused (TC-U-105), a service with a `Bearer` header default and a basic-auth password produces messages that contain neither |
| P-A2 | Parameter shaper (Q3): pure function `ServiceMethod -> element parameter tree` porting `process-webservice-schema.js`: `<svc>_In/_Out`, L17 Tag = service name, CompositeObjectList with recursive L18 and ObjectList with one `<Name>ItemValue_<dir>` child, always (Q3: no `ProcessParameterCollections` gate, no feature-reader seam, no notice N11; the designer's flat shape on a clean 8.2.1+ database is the defect ENG-68545 "No data binding exists for ProcessParameterCollections feature", not copied), flattened single objects as JSONPath scalars, MaxSizeText for Text/empty, IsRequired rule, caption resources (the " - Input"/" - Output" suffix only where a top-level caption is shared across request and response, never on a nested item: EM2, M-24), then the untagged result params Success, Response, StatusCode, RequestBody, in the designer's order; Value "false" on every Boolean, Success included (tagged, nested and Success: L8 `{"GS2":"false","GS5":<P>}`, serialization §2.4, I7; 14/14 tagged corpus Booleans) | the shaper reproduces, through a capture oracle projection, the parameter trees of the pattern-A pairs (corpus process capture + its service schema; the three pairs of serialization §8.1: CheckMarketplaceAppNeedUpdateSubProcess + CreatioMarketplaceApi, ActionGeminiSearch + GeminiApiSearch, ExecuteUpgradeBsf + teamcitybpmonline_BSF): names, tags, types, L18, ItemValue, IsRequired, captions, Boolean L8 defaults (ExecuteUpgradeBsf's nested Boolean included); the 9 corpus elements that do not put service params first (g1) are named as exclusions, not silently matched; TC-U-100 pins that nested items are always emitted, with no feature read |
| P-A3 | Platform-sync seam: `ProcessSchemaWebService.SynchronizeParameters` behind a package interface (the provider is internal and resolved from `CoreApiContainer`, null in package tests, r8); one probe test in the `SubProcessPlatformProbeTests` pattern | the probe states what the harness returns; no production code calls the seam yet |
| P-A4 | Generalize the sub-process `Snapshot` / `Diff` (private, typed to `ProcessSchemaSubProcess`) and the drift report over `ProcessSchemaParametrizedFlowNode` | the TC-U-93 characterization commit lands first and pins each sub-process notice text; the lift (design C5) is its own commit after it and changes no existing test file; every existing `SubProcess*Tests` passes unchanged |

One commit each. Gate 2: single combined lens for P-A1..P-A3 (new code, one module); P-A4 touches shared sub-process
code: full lens if its diff exceeds ~50 lines. Slice exit: all existing suites green on dev-nf and dev-n8, no
behaviour change visible through `BuildProcess` / `ModifyProcess` / `DescribeProcess`.

### 3.2 Slice B: handler, applier, block, BK15, same-request ordering

| Task | Component | Done when (exit criteria) |
|---|---|---|
| P-B1 | Contract (Q2, Q8): `webService {service, method, timeoutSeconds?, ignoreErrors?}` as a `[DataContract]` on `ProcessElementDescriptor` and `ProcessElementUpdateDescriptor`; `ElementTypes` token `webservice`; wire-contract rows; the read-back-only keys are derived automatically because the write and describe members are both keyed `webService`; nothing to register, and the server key check of 1.6.6.136 and later accepts them (decisions Q2, gate-1 note) | wire-contract tests pin the new DataMember names; operation count stays 8 |
| P-B2 | `WebServiceElementHandler` (Q1): own handler, `CanBuild/CanDescribe => element is ProcessSchemaWebService`; stamps `ManagerItemUId` (the platform ctor does not, sub-process precedent); strict `EnsureBlockMatchesHandler` gate in the factory; a block-less create refused; DI line in `CrtProcessBuilderApp.cs` | `CrtProcessBuilderAppTests` tripwire green; block on the wrong type refused; element keys BO2/BO3 true, BN2 `69;55` |
| P-B3 | `WebServiceApplier` Create mode: resolve (P-A1), write IX1 manager-item UId and IX2 canonical name, IX3 = seconds x 1000 (0/absent = unset), IX4 `IsIgnoreErrors` written directly (Q7: EM9 showed the package already needs about 8.3.2, so the reference adds no floor; no capability seam, no reflection), shaper output (P-A2) on the attached element, then the platform sync tolerated (no-op on a correct shape, P-A3) | stored element projected through the capture oracle equals the shaper output plus EM2's element keys; IX3/IX4 omitted at their defaults |
| P-B4 | BK15 rows (Q4, agreed A): one `ProcessSchemaMapping` row per result param, as the designer writes them (the Q4 probe measured the shape on stand `Creatio`, EM2 the same 4 rows on `UsrWscEcho`, M-13, M-24); first package-authored BK15 rows, behind one writer; `A2` is not rewritten when the element is renamed, as the designer does not rewrite it | rows equal EM2's BK15 rows under the oracle rule (UIds not compared) |
| P-B5 | Same-request ordering (Q10): selection runs in the `afterElementsPlaced` hook (`SelectReferencedSources`, `ProcessBuildHandler`) as Sub-process and Pre-configured page do, so `mappings[]`, other blocks and `typeFromElement` in the same request see the generated params; `addElement` applies selection before later operations | a build that maps a process parameter into `<svc>_In`, a constant into a dotted item path, and a formula into `RequestBody` in one request succeeds; a mapping onto a param the method does not produce is refused with the param list |
| P-B6 | Mapping coverage on the element (Q5, Q6; estimate WP M): the three corpus shapes of g3 (item-level into an IN collection, COL <- COL, declared response Collection <- WS outputs over two levels, with each container bound as well as its items, as the designer stores it: EM3 route (b), M-25), `RequestBody` raw text by constant and formula, the refusals that already exist (ObjectList, source deeper than two levels); new `WebServiceMappingTests` fixture, no mapping-rule change expected (g3, traced); no JSON check of a constant `RequestBody` and no `RequestBody`-vs-Body conflict notice in v1 (dropped by Q5, 2026-11-03) | TC-U-56..64, 66, 67 green (TC-U-65 is P-C6's; TC-U-68, the describe round trip, sits under P-C1 and closes with P-D1) |

Then the EXPERIMENTAL CUT (section 6), stand-only, installed on stand `Creatio` with the owner's yes, and **EM5 (STOP
gate)**, as the EM5 procedure defines it: the designer opens and re-saves a builder-made element and the stored bytes
are diffed; an unrelated builder modify of it and of a designer copy of EM2 is diffed too; the row-less case is
covered only through a designer copy-paste of the builder-made element (optional). Record the result in the
measurements file. Stop on failure (section 0).

### 3.3 Slice C: describe, guards, notices, parity suite

| Task | Component | Done when (exit criteria) |
|---|---|---|
| P-C1 | Describe (Q14): handler `Describe` adds `webService {service, serviceUId, method, timeoutSeconds, ignoreErrors, serviceResolved, methodResolved, issues[]}` (`service` the write-side name string, null when unresolved; `serviceUId` the raw `IX1`, read-back only); the stored parameter tree (direction, tag, type, items, bound), unbound inputs included, read from the element, never re-derived from the service; `buildType:"webservice"`; never a service param default; the tree carries no value (a bound input's value, one of the element's stored bindings, process data the caller can already read through describe, comes back through the generic `parameters[]` as today, M-31); the read-back-only keys `serviceUId`, `serviceResolved`, `methodResolved`, `issues`, `parameters` are derived automatically because the write and describe members are both keyed `webService`; nothing to register. Today (1.6.6.131) the package's describe already returns each output's `tag` and its nested tree (`itemProperties`, down to three levels) and the bound inputs with their value or token, source and `tag` (M-31); unbound inputs and an unbound `RequestBody` are omitted, there is no service, method, timeout or ignore-errors member, and it reads the synced (pruned) view (decisions Q14, corrected 2026-11-03); the work here is the unbound inputs, the `bound` / `result` flags and the `webService` member, not tags or nesting | round trip build -> describe -> replay writes nothing (TC-U-68, closing with P-D1); TC-U-109 `Describe_ShouldReplayTheWebServiceBlock_ThroughTheServerKeyCheck` replays a described block through the server key check; describe of each corpus pattern-A process reports service, method and tree; a secret-default fixture never appears in the reply |
| P-C2 | Guards: `userTask` = `WebServiceUserTask` on the generic route refused with a pointer to the `webService` block (Q1, pattern `RefuseUnconfigurablePageTask`, keyed on the resolved schema UId); `setElement webService` on a designer-built element carrying BP6 (multi-instance) refused, with a describe issue for such an element (TC-U-48, TC-U-79; on create, `webService.multiInstanceOptions` is refused by clio's key check before sending, TC-C-02 / TC-I-03). No IX4 platform guard (dropped by Q7, 2026-11-03); design C11 keeps `WebServiceGuards.cs` for these refusals only. Not a guard: a SOAP service is built like REST, with notice N10 naming the element (Q20 D, 2026-11-05; build and notice tests TC-U-24, 110, 111) | one test per refusal (TC-U-47, 48); nothing written after a refusal |
| P-C3 | Notices: canonicalised name (N2, Q9: on Create and retarget only, on a case-insensitive hit); cross-package service (Q9, no dependency added; EM10's designer half measured that the designer adds the `SysPackageDependency` row, the builder adds none, measured 2026-11-09, M-42); nested array-in-array request binding warning (Q13, N5: before the ENG-55920 "Unable to use array inside array in web-service business process element" fix, absent in 10.1.37, the stand, an inner object array whose container has no source of its own while its leaves are mapped is sent without that inner list (null); a mapped inner container is sent; that such a process saves without error is not measured); N5 is shown on every target, names the affected version line and the remedy (map the container); suppression on fixed builds is decided after EM12, whose run half rests on the core unit tests and the ENG-55920 comment under E4 (Q17); unconfigured / unresolved element at the end of the request (ledger) | each notice text; when each fires and when it does not |
| P-C4 | Parity suite (AC5): pattern A (corpus pairs copied verbatim from the shipped PackageStore corpus into the internal package repository, under `WebService` with `PROVENANCE.md` SHA pins and a `.gitattributes` `-text` line; not redacted, so they may carry the shipped services' non-credential `IU5` defaults; AC8 / Q16 govern builder output, and TC-P-08 asserts the fixtures carry no credential: no `IQ5` value with `IV2` 1, no Const `Authorization` default), pattern B (EM2/EM3/EM4 stand captures, committed redacted under `Stand`, the redacted keys excluded by WSCR W14; EM5 is a re-save diff, not a fixture), pattern C (writer JSON diff of a builder-made element vs EM2) | parity suite green; every exclusion named in WSCR ([serialization §8.2-8.3](eng-92710-call-web-service-element-serialization.md)) |
| P-C5 | Docs: `docs/web-service-element-capture.md` (corpus survey, element keys, BP2 keys, BK15 rows, what the builder writes, drift, EM results); handler list in `docs/process-builder-architecture.md` section 3.5 | - |
| P-C6 | C13 mirror refusals (Q6 v1, [design §4.14](eng-92710-call-web-service-element-design.md)): the `typeFromElement` mirror of a WS CompositeObjectList output keeps refusing, and its message gains the declared-Collection pointer; the mirror of a WS ObjectList output, which today silently creates a shapeless parameter, is refused. Touches the shared parameter service (`ProcessParameterService`): gate 2 full fan-out | TC-U-65 asserts the C13 pointer text; a new TC-U row (id assigned in the test plan) pins the ObjectList mirror refusal with nothing written |

### 3.4 Slice D: modify semantics, retarget guard, drift issues

| Task | Component | Done when (exit criteria) |
|---|---|---|
| P-D1 | `setElement` Update mode (Q11): a service or method change drops every generated param and its bindings and regenerates them (the designer's behaviour, EM4 measured 2026-11-03), except that `Success` / `Response` / `StatusCode` keep their UIds (Q11 A3; the designer gives them new ones, M-26); the 4 BK15 rows re-created (M-26), each with a fresh `GT5` as on create (the designer keeps the old `GT5`, which WSCR W10 ignores); `IX3` kept on a method change and reset on a service change, `IX4` kept (M-26); the element caption follows the designer (M-26): a service change always renames it to "<service caption> <N>", a method change keeps it; the same service and method sent again, in any letter case, is a no-op; notice N4 lists every dropped input binding, `RequestBody` included, in write shape with its value (constants and formulas alike: the element's stored bindings, process data the caller can already read through describe, M-31, not service data, Q16), so one call re-applies those that still apply; `service` alone (no `method`) is refused with the new service's method list, nothing written (TC-U-108); timeout / ignoreErrors change alone touches nothing else | TC-P-12: the EM4 S1 capture, with its Formula reader of the tagged output removed first (A3 would refuse the change otherwise), retargeted by the builder in a call that also maps `UsrWscQ_In` (as EM4 step 2 did before the S2 save); its element node and BK15 rows equal the S2 capture's under WSCR (the S2 capture's dangling Formula token is outside the comparison; the designer's `BL11:false` on the S2 element, its required `UsrWscId` unbound, is a named WSCR allowed difference, serialization §8.3; the package never writes `IsValid`) |
| P-D2 | Retarget guard: `IProcessDependencySnapshot.FindDependents(element, survivingParameterUIds: {Success, Response, StatusCode UIds}, includeOwner: false)` (the element's own parameters are all replaced, so its own references die with them) refuses a change when anything (another element, a process parameter, a flow condition, a formula body) reads any parameter of the element other than `Success` / `Response` / `StatusCode`: tagged inputs, nested items and `RequestBody` included, since all are regenerated with new UIds (decisions Q11, gate-1 second pass: A3 stated exactly; the package's `ProcessElementDependencyScanner` walks `schema.Parameters` too; the ENG-100041 "Sub-process retarget guard scans the live schema instead of the request dependency snapshot" pattern); readers of `Success` / `Response` / `StatusCode` never block (Q11 A3) | refusal names the dependent (element, process parameter, flow or formula) and the param it reads, for a tagged output, a tagged input, a nested item and `RequestBody`; nothing written |
| P-D3 | Drift issues (Q12): describe reports `serviceResolved` / `methodResolved` false and nested-L18 drift as issues; a modify of a process whose WS element is unresolved gets a notice naming the element and the method, saying that the platform load prunes its tagged params (EM8's current-builder half, 2026-11-03: an unrelated modify through current clio deleted the 8 tagged params of such an element from the database, with no warning); the modify is not refused by the package (refusal is Q12 option B, a follow-up) | one test per issue shape; the notice fires on any modify of such a process, the unresolved element itself included when the request does not resolve it (owner, 2026-11-05; TC-U-53, 114); a modify the platform's validation refuses carries the notice in the refusal (F-2, F-5; TC-U-115..124); EM8's builder half confirms on the claimed cut (done 2026-11-09: refused with N7, M-51, M-52) |
| P-D4 | `modifyAsNewVersion`: the version clone keeps the element, its params and BK15 rows | clone round trip |

Gate 1 before the PR opens: full 3-lens fan-out (quality, performance + correctness, security), because shared
sub-process code (P-A4) and the build hook (P-B5) change and Q16 is a security rule. Blocker/High fixed before the
draft PR opens. Pushing the branch and opening the draft PR wait for the owner's word. The PR stays draft through
the cuts.

### 3.5 Package PR

GHE `engineering/crt-process-builder`, base `main`, title `ENG-92710 Call web service element`. Body: what changed,
the EM results with dates, the cut claimed in the claim comment, the test counts on dev-nf and dev-n8, the security
pins unchanged (8 operations / 6 gate call sites), the merge order, "draft until the BMAD PR merges". No reviewers
until the owner asks.

## 4. Phase 3: knowledge (clio-knowledge)

Worktree `<knowledge worktree>`, branch `feature/ENG-92710-call-web-service-element` from a fresh
`origin/master`. Pushing the branch and opening the draft PR wait for the owner's word.

| Task | What |
|---|---|
| KB-1 | New article `guidance/mcp/guides/processes/web-service.md` (`process-web-service`): scope (owns the `webService` block; process-parameters owns collections), the FULL-contract gate and the version floor written as the real floor (no TBD at merge), the block, resolution and canonical names, registered services only (registration in Studio > Web services; registering through the toolkit is a follow-up), request values through mappings (stored names, dotted item paths), when to use `RequestBody` raw text (a method without a Body parameter such as `GeminiApiSearch`, the inner-list workaround for builds before the platform fix for arrays inside arrays, shapes mappings cannot express) and that a non-null value replaces the generated body (Q5), response into a declared Collection (two levels, container and items bound; in the designer an item is mapped first, EM3 route (b); deeper, ObjectList and the designer's "Create from element" wrapper -> the structured parameter types follow-up), Success / StatusCode / Response, ignoreErrors (it matters only for a transport failure; an HTTP error status never fails the element, EM7 designer half), modify replace semantics (dropped bindings listed with their values in write shape, re-appliable in one call; a service change renames the element caption to "<service caption> <N>" (always: `elementUpdate` has no `caption` member), a method change keeps it) and the retarget guard (anything reading a parameter of the element other than Success / Response / StatusCode, inputs and nested items included, refuses the change; readers of those three stay valid, Q11 A3), drift issues and the designer-save loss (M-30: a designer save of a process whose web service element no longer resolves deletes every parameter of the element, result params included, and its BK15 rows and leaves the element `BL11:false`, so the remedy comes before any designer save: a renamed or removed method is restored in Studio > Web services and the request repeated; a service that is no longer registered is restored by installing the package that carries it, not by registering it again in Studio (another UId, the element stays unresolved; TC-U-124); otherwise the reads of the missing parameters go first, then the element is re-pointed; describe's `methodResolved: false` is the signal; TC-K-05), the nested array-in-array run-time caveat, notices, refusals, read back; a "task without details" section (Q10 addition 2: find the service through the refusal list and ask when several fit; stop when none fits, no placeholder process and no Script task in its place; build in two steps; a key or token parameter from a system setting or a process parameter, never a constant; TC-K-13). Size measured on the branch by the KB-5 size gates |
| KB-2 | `element-catalog.md` and `element-catalog-details.md`: flip `webService` from READ-ONLY / not buildable to buildable with the floor and a pointer; `ProcessGuideContentPreservationTests` READ-ONLY pin replaced by the version-floor pin in the same commit (formulaTask / scriptTask precedent) |
| KB-3 | `process-modeling.md`: drop `webService` from the cannot-create list, paid by a swap (~13 B headroom, measured r5, re-measured on the branch); `routing.md`: a row `name=process-web-service` that distinguishes calling a registered service from a process from writing a C# configuration service (`configuration-webservice` guides) |
| KB-4 | `bundle-source.json`: resource with NO `legacyUris`, `requirements.itemIds` and `resourceUris`, the `process-element-catalog` and `process-modeling` descriptions, `libraryVersion` bumped above master and every open branch claim, re-checked at merge; no authored `sequence` (derived from `libraryVersion`) |
| KB-5 | Tests: `WebServiceGuidanceTests` (contract, floor, FULL-contract gate, routing disambiguation), `ProcessGuideCrossReferenceTests`, `GuidanceMigrationTests.PostMigrationGuidance`, size gates (`GuidanceInlineReplyBudgetTests`, `ProcessGuideResponseSizeTests`), manifest |
| KB-6 | Fact-check gate: one refute-first agent per claim cluster, given the source paths, forbidden to cite `docs/knowledge/` or this spec folder as evidence; every refutation re-checked by hand; any article statement that rests on a traced-only claim names the EM that measured it or is removed |
| KB-7 | Agent scenario runs AR-1 (service not named) and AR-2 (service not registered: the agent must stop) from the [test plan](eng-92710-call-web-service-element-test-plan.md), on the draft article (Q10); a failure is fed back into the article before the KB PR leaves draft. Run 2026-11-09: AR-2 passed (M-46); AR-1 needed two refusals on the first draft (M-45) and passes with one after the unknown-service refusal gained the methods (K-1, M-48) |

## 5. Phase 4: clio

Worktree `<clio worktree>` from a fresh `origin/master`, branch
`feature/ENG-92710-call-web-service-element`. Pushing the branch and opening the draft PR wait for the owner's word.
Before CL-1 and CL-3 read `CLIO/docs/agent-instructions/bundled-packages.md`; before any clio edit read
`CLIO/docs/knowledge/ProcessModel/` and `CLIO/docs/knowledge/McpServer/`. `CLIO*` diagnostics stay clean in modified
files; new public members carry `///` docs.

| Task | What | Guard that bites |
|---|---|---|
| CL-1 | Rebundle the final cut (`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <pkg> -Version <claimed>`), PROVENANCE pins in the same commit; security counts unchanged (Q10) | `BundledProcessBuilderPackageTests` |
| CL-2 | `WebServiceBlock` key set in `ProcessBlockKeyCheck` (`Blocks`, `Element`, `ElementUpdate`), read-back keys on the describe type | `KeySets_Should_EqualTheMembersOfTheBundledServerContracts`, `ReadBackFields_Should_BeDescribeOnlyMembers` |
| CL-3 | `"webService"` in `CreateElementBlockShape` (wire-bundle headroom ~168 B, measured r5, re-measured on the branch) | `ToolContractShortFormTests` call-shape drift, `MaxInlineWireReplyBytes` |
| CL-4 | Floors: the `[RequiresPackage]` literal on create / modify / modify-as-new-version moves to the cut claimed in the claim comment; one floor-history paragraph; `webService` added to the floor-reason sentence of create and modify | `ProcessDesignerRequiresPackageAttributeTests`, description pins |
| CL-5 | Descriptions (Q15): create type enum + one `webService?` pointer clause to `process-web-service`; modify `addElement` / `setElement` lists; describe read-back clause; `validate-process-graph` buildable list; generic-route refusal of `WebServiceUserTask` in `list-user-tasks`. Paid by swaps, not a ceiling raise: re-measure by lowering the ceiling on the branch (create 39 B, modify 3 B, describe 43 B headroom at `6be5dde28`, measured); ASCII only; non-ASCII, apostrophe, double quote, `<`, `>`, `&`, `+` and backtick cost 6 B each | `ToolContractPayloadBudgetTests` |
| CL-6 | Prompts (`Create/Modify/Describe` process prompts, `ListUserTasksPrompt`) | prompt pins |
| CL-7 | Typed `DescribedWebService` (and children, each with its overflow bag) mirroring the server describe type field for field; recount the record `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`. No data-id arm: `webservice` already resolves to `EventType.WebServiceTask` (`Schema.cs`, traced) | `ServerProcessDescriberTests` |
| CL-8 | `docs/McpCapabilityMap.md` rows; curated re-pin `curated-knowledge-names.json` to the KB generation that publishes `process-web-service`. The clio PR stays draft until the KB PR merges and its generation is published; if KB publishes after the clio PR is approved, the re-pin goes into a separate PR instead of a push | `WorkspaceTemplateGuidanceDriftTests` |
| CL-9 | e2e `WebServiceElementToolE2ETests` (create + describe, same-request mappings, modify replace with the retarget guard, refusals with nothing created, the designer-built EM2 fixture read-only, validate-process-graph over a described element); TC-I-13 in `ListUserTasksToolE2ETests`: the `list-user-tasks` contract text names `WebServiceUserTask` as refused on the generic route (AGENTS.md: every changed MCP tool gets e2e coverage); `MinimumPackageVersion` = the bundled version; Ignored with build instructions when `UsrWscEchoService`, `UsrWscDriftService` (with `UsrWscOrders2`), `UsrWscDeadService` or the EM2 fixture `UsrWscEm2CaptureA` is absent; strictly sequential; run manually on a registered test stand on net10 and net8 (the ProcessDesigner category never runs in CI), Ignored = 0 | stand run recorded in the PR |
| CL-10 | Knowledge records only for facts the code does not say (for example the load-path re-sync that prunes tagged params of an unresolved element, measured by EM8; the package's real compile floor of about 8.3.2, EM9); update or delete every knowledge record whose `applies-to` path the PR touches (AGENTS.md) | `make check-knowledge` (advisory) |

Validation: `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null` (more than three
modules change: full unit suite; `< /dev/null` because clio.tests git calls hang on inherited stdin). PR body
statements: "MCP reviewed" with the surface list (tools, prompts, resources, `clio/tpl` templates, `clio.mcp.e2e`),
adding that the `list-user-tasks` change (CL-5 description, CL-6 prompt) is covered by TC-C-11 and the e2e row
TC-I-13 (`ListUserTasksToolE2ETests`);
"docs reviewed, no update required" for help / docs/commands / Commands.md / WikiAnchors (the process tools have no
CLI verb, r5); the ClioRing statement after inspecting `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing` and
`clio-ring/ClioRing.Desktop/actions.json` for the process tools and `clio-run` command names: "ClioRing compatibility
reviewed, no Ring-consumed contract changed" with those paths, or, if Ring consumes them, the Ring test and Windows
x64 NativeAOT publish commands with their results; the re-measured budgets; the `Validated:` command.

## 6. Cuts, installs, merge order

- Claim before stamping: check `git ls-remote --tags` of crt-process-builder and the version clio bundles; claim the
  first free number in the ENG-92710 claim comment (Q19); then cut. Each claim-comment edit (a Jira write), each tag
  push and each branch push happens only on the owner's explicit yes, one step at a time.
- Cut procedure: run the rebundle script from the clio checkout BEFORE committing the restamp (it refuses an already
  stamped descriptor); commit the restamped `descriptor.json` in the package (`set-pkg-version` moves PackageVersion
  and ModifiedOnUtc together); tag the pre-restamp producing commit `crtprocessbuilder-<version>` and push the tag;
  rebuild clio afterwards (an install reads the archive from the build output).
- Expected cuts: experimental (slice B, stand-only stamp; also claimed in the claim comment and marked there
  "experimental, stand-only, superseded", the ENG-95985 "Send email attachments" .126 precedent, verdict r10-C14), mid (after gate 1), final, plus review-round or
  foreign-cut re-cuts. Calibration (r10, verdicts): settled designs took 3-6 per feature (r10-C7); external-schema / platform-
  fidelity elements took far more (ENG-92707 "Sub-process element: selection + parameter sync": 27 descriptor versions on its branch). Superseded numbers are marked in
  the claim comment, never reused. No tag is pushed before the late measurements (EM6-EM12) of that cut have run.
  Status: met 2026-11-10: EM6, EM6-B, EM7, EM8 step 8, EM10, EM11, EM12 and KB-7 AR-1 re-run on the shipping build (M-53..M-58); EM5 not repeated (the later changes touch only refusal texts and the validator).
- Final-cut check (with the EM2 / EM5 re-run): the Boolean constant, measurements EM6-B (decisions, "Boolean constant at
  run time"; owner 2026-11-05: A + B in one run). Measured 2026-11-09 (M-39): by trace (oracle B) the builder's
  ConstValue holds `UsrWscFlag_In = true` at run time; oracle A (a flow condition on the element input) cannot be saved
  (F-1, resolved 2026-11-10: the oracle is dropped from the EM6-B steps); no code change, and the TC-P-15 companion test pins the difference permanently.
- Every install on a registered test stand needs the owner's explicit yes: it restarts the site for every user. A reviewer
  whose clio bundles a cut newer than the stand is refused by convergence until the stand is brought up.
- Merge order (human merges only): BMAD docs PR first; then package (tag on the remote) -> knowledge (publishes) ->
  clio. Approvals are kept: no push after an approval without asking (a GHE push dismisses approvals).

## 7. Review gates (AGENTS.md)

| Gate | When | Scope |
|---|---|---|
| 1 | before each PR opens | every PR: full 3-lens fan-out over the complete diff (quality and maintainability; performance and correctness; security, with Q16 as an explicit check); KB: 3 lenses over article and tests plus the KB-6 fact-check; each review states in the PR thread which tier ran and why |
| 2 | each commit pushed to an open PR | skip for restamp / rebundle / docs / fixtures; one combined lens for a small single-module diff (< ~50 lines); full fan-out for changes to shared sub-process code, the build hook, the mapping service or the parameter service; only Blocker/High block |
| 3 | before ready-for-review | one comprehensive adversarial review per PR over the whole diff; Medium/Low findings from gate 2 addressed or recorded as follow-ups |

Review findings are read from both the threads and the review body.

## 8. Stand use and cleanup

- Every write on a registered test stand (test service, designer captures, builder runs, installs, run-time probes) needs the
  owner's yes, one step at a time; the owner logs in to Claude in Chrome when needed. Schema writes run strictly one
  after another (a parallel burst takes a .NET Framework app pool down).
- The run-time probes (EM6, EM7) call only `UsrWsc*` services (`UsrWscEchoService`, whose base URI is the stand
  root, EP option E4, Q17, with `UsrWscMissing` on the static path `/UsrWscNoSuchRoute.js` for a 404, since the stand
  root redirects an unknown route to the login page, M-28; `UsrWscDeadService` for EM7: `http://127.0.0.1:1`, a
  loopback port with no listener, the owner's pick, [measurements §3.2](eng-92710-call-web-service-element-measurements.md));
  no external host is called. Under E4 there is no echo: EM6 shows what a stand answer can (result params,
  StatusCode, Success), its request-byte check and EM12's run half rest on the core unit tests and the ENG-55920
  comment, and the `$.orders` response read needs a JSON source the owner adds, or stays unmeasured on the stand. No
  shipped service is invoked from here on; the Q4 probe of 2026-11-03 ran both of its elements against the Creatio
  Marketplace API with empty inputs (decisions Q4).
- Names: every artefact carries the `UsrWsc` prefix (service, capture processes, builder-made processes, e2e
  processes), so the leftovers register can be checked by prefix. One exception from 2026-11-03: the EM10 designer
  process `UsrProcess_3389428` (`0576c2d6`, the designer's default code) in package `UsrWscEm10Proc`; it is
  registered by id. EM2 S2-S4 (2026-11-05) added `UsrWscSoapService` (`197ff0c0`) and `UsrWscEm2SoapS2`
  (`dbc77d7c`) to the leftovers register.
- No secret enters a capture fixture, a measurement record or a test snapshot (Q16): EM1's header default is
  non-secret by construction; any capture is grepped for defaults and auth blocks before it is committed.
- Cleanup goes through `execute-dataservice-batch` (the stand answers HTTP DELETE with 405, measured at ENG-95985)
  and `clio delete-schema <name> --remote -e Creatio --timeout 600000`; the measurements file keeps the leftovers
  register. Cleanup is proposed at the end and runs only on the owner's yes. The EM2 capture process stays until the
  clio e2e no longer needs it as its read-only fixture; it is never run or modified (measurements rule 10): EM5's
  builder modify runs on the designer copy `UsrWscEm5OracleCopy`.

## 9. Definition of done

Rows follow the 9 AC of the ENG-92710 description ([Jira description draft](eng-92710-call-web-service-element-jira-description-draft.md)).

| AC | Done when |
|---|---|
| AC1 | `webService {service, method, timeoutSeconds?, ignoreErrors?}` on create `addElement` / `setElement` / modify builds `ProcessSchemaWebService`; request params, including a composite request body (mappings into nested items) and `RequestBody` raw text (Q5), are configured through the existing mapping surface in the same request, addressed by the names describe returns; round trip writes nothing; EM5 passed |
| AC2 | unknown or ambiguous service or method refused with the service list (codes and captions, and each service's methods, bounded; K-1) or the resolved service's methods; a missing service and an empty catalog point to Studio > Web services; names matched case-insensitively and stored canonical (Q9, Q10; EM11's builder half on the claimed cut) |
| AC3 | constants, formulas, process parameters and element outputs map into `_In` params and dotted item paths (Task 6); a composite response is read into a declared Collection (two levels) with item mappings, container bound as well (Q6, EM3 route (b) parity); the request bytes and the composite response read rest on the core unit tests and the parity suite unless the owner adds a JSON source (Q17), and the PR body says so; ENG-92730 scope recorded as the follow-up, not done here |
| AC4 | IX4 written and read back; Success / StatusCode / Response usable in conditions and mappings; run time: the success path proven by the manual cases on the shipping build (M-59): TC-01 Success true, StatusCode 200, Response the JSON body, the outputs filled; TC-02 5 rows read into a Collection, Success true, 200; TC-03 StatusCode 201, total quantity 96; the failure path by EM6 (M-38, M-55) and EM7's builder half matching its designer half (M-28, M-40, M-56) |
| AC5 | parity patterns A (corpus pairs), B (EM2 / EM3 / EM4 captures) and C (writer diff) green; the 4 BK15 rows written (Q4); a builder-made element survives a designer re-save with no diff (EM5, re-run on the final cut) |
| AC6 | a method / service change replaces params and bindings as the designer does (EM4 parity under the oracle rule), keeps the UIds of `Success` / `Response` / `StatusCode` (Q11 A3), and notice N4 lists the dropped input bindings; the retarget guard refuses any reader of a parameter of the element other than `Success` / `Response` / `StatusCode` |
| AC7 | describe returns the `webService` member and the stored parameter tree, unbound inputs included (Q14); unresolved service / method and nested drift reported as issues; the latent-loss notice N7 tested (Q12), also on a modify the platform's validation refuses (F-2, F-5), and EM8's halves recorded |
| AC8 | no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output (reply, refusal, notice, log or test snapshot of builder output; Q16 tests and grep); the element's stored bindings are returned by design, through describe's generic `parameters[]` and in N4; the verbatim pattern-A corpus fixtures carry no credential (TC-P-08) |
| AC9 | package tests on dev-nf and dev-n8 plus the first CI run of the draft package PR; clio `TestCategory=Unit` full suite; KB test project; MCP surface (descriptions, prompts, key check, short form, typed describe, capability map, curated re-pin) updated; e2e on a registered test stand on net10 and net8 with Ignored = 0; docs: capture doc, architecture doc, KB article, catalog flip |
| general | every TC row of the test plan implemented and green; gates 1-3 passed and their tiers stated; KB fact-check passed; the clio PR body carries "MCP reviewed", "docs reviewed, no update required", the ClioRing statement with the inspected paths, the `Validated:` filter and the re-measured budgets; knowledge records in the clio PR, every record whose `applies-to` path is touched updated or deleted, and the `make check-knowledge` report read; the tracker rows moved as section 2 says; `CLIO*` diagnostics clean in modified files and new public members carrying `///` docs (AGENTS.md); story DoD boxes ticked only when true; no secret in any artefact (Q16 grep); stand leftovers listed |

## 10. Risks

| Risk | Effect | Mitigation |
|---|---|---|
| Platform load re-sync (g4): every design-time load re-syncs WS params by Name; an unresolved or renamed method prunes tagged params, appended params land flat at the end; measured 2026-11-03 (EM8's current-builder half: an unrelated modify deleted the 8 tagged params of an unresolved element from the database, no warning) | silent, permanent loss of params and bindings on an unrelated modify; describe after load differs from what the builder wrote | Q3 shape equals the platform's at the top level so its sync is a no-op (probe P-A3, EM5); Q12 drift issues and latent-loss notice; EM8 step 8 re-measured on the claimed cut (refused with N7, M-51, M-52); refusal by the package (Q12 option B) a follow-up |
| ENG-55920 (core#1119, fixVersion 10.1) absent on 10.1.37: an inner object array whose container has no source of its own while its leaves are mapped is sent without that inner list (null); a mapped inner container is sent (traced, verdict r1-C7) | a correct build sends an incomplete body on the stand; that such a process saves without error is not measured | Q13 warning notice N5 on such bindings, on every target; under E4 EM12's run half is not measurable on the stand (Q17) and the caveat rests on the core unit tests and the ENG-55920 comment; the KB article states it |
| No echo endpoint (Q17, EP option E4) | EM6's request bytes and composite response read, and EM12's run half, are not shown on stand `Creatio` | parity suite and core unit tests carry the request side; the AC3 and AC4 DoD rows and the PR body say what was not run; the owner may add a JSON source for EM6's response read |
| Package compile floor: `origin/main` already needs about 8.3.2 (EM9, 2026-11-03: fails on 8.3.1.4498 and 8.1.3.6947, builds on 8.3.2.4199); IX4 `IsIgnoreErrors` exists from 8.3.2.3590 | none added by IX4 (written directly, Q7); builds between 8.3.1.4498 and 8.3.2.4199 are not measured and none is a supported target | the KB article states the real floor (KB-1); CI compiles against 8.3.4, the local core-bin is 10.1.37 (last row) |
| Budgets at the ceiling: modify 3 B, create 39 B, describe 43 B at `6be5dde28`; `process-modeling` ~13 B | a reviewer must accept the swapped text; risk of a forced ceiling raise | Q15 agreed: pointer clauses only, swaps, no ceiling raise; the headroom is re-measured on the branch before writing text; ASCII only |
| ENG-92730 scope creep (mirror relaxation, ObjectList, 3+ levels) | composite half grows from ~10 h toward ~25 h (g3) and touches the shared parameter service | Q6 v1 declared-only; any pull-in is an explicit owner decision with a re-estimate before slice C |
| Many review rounds for external-schema elements (ENG-92707: 23+ rounds, 27 descriptor versions on its branch) | calendar +2-3 d, more cuts, approvals dismissed by re-cuts | STOP gate before the larger slices; narrow claims instead of re-fixing; parity suite from captures, not from traces; claim cuts early; no tag before the late EMs |
| Secrets in service metadata (corpus: basic-auth passwords, `Bearer` header defaults) | leak through describe, refusals, notices, logs, test snapshots or captures | Q16 as a gate-1 security check; resolver and describe never read defaults into text; a secret-default fixture test; grep of every capture before commit |
| EM5 fails (designer rewrites or duplicates package-authored BK15 rows, drops Success L8) | Q4 / Q3 re-opened, estimate moves | STOP gate right after slice B; BK15 behind one writer so it can be switched off. Passed: experimental cut 2026-11-04, final-cut campaign 2026-11-09 (M-37) |
| Wrong-case or unknown method yields empty params silently (platform ordinal lookups, g5); measured 2026-11-03 (EM11's designer half: a case-only slip saves green, the next load prunes every tagged param, the run fails with `ItemNotFoundException`) | element saved with params that the next load drops, then a failing run | Q9 canonicalisation; EM11's builder half passed in the final-cut campaign (M-43) |
| Another ticket cuts on `main` or a parallel KB PR claims the same `libraryVersion` | re-cut / re-pin after approval | claim early in the claim comment; pick above every open branch claim; re-check at merge |
| Local core-bin 10.1.37 vs CI 8.3.4 | green locally, red in CI | literal platform UIds in tests; first CI run on the draft PR before gate 3 |
