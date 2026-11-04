# Story 1: Build and describe a Call web service element through the process builder

**Feature**: eng-92710-call-web-service-element
**Jira**: ENG-92710 Call web service element (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-92710-call-web-service-element.md](../prd/spec-eng-92710-call-web-service-element.md) - AC1-AC5,
AC7 (read back), AC8, AC9
**ADR**: [adr-eng-92710-call-web-service-element.md](../adr/adr-eng-92710-call-web-service-element.md) - Q1, Q2, Q3,
Q4, Q6, Q7, Q10; related Q5, Q8, Q9, Q13, Q14, Q16, Q20
**Test plan**: [tp-eng-92710-call-web-service-element.md](../test-plans/tp-eng-92710-call-web-service-element.md)
**Analysis**: [`spec/eng-92710-call-web-service-element/`](../eng-92710-call-web-service-element/README.md) (plan
sections 3.1-3.3, 4 and 5 hold the task tables this story restates; section 9 is the Definition of Done)
**Status**: in-progress (set in this docs-only BMAD PR because implementation starts before it merges, so
`ready-for-dev` is never true; plan section 2). The clio feature PR moves it to `review` when it opens; `done` is
flipped in the next clio PR that touches `spec/sprint-status.yaml` after the last merge, with only the really ticked
Definition of Done boxes.
**Size**: the estimate's package work packages A, B, C and M (with H, G and Docs), clio K-src, K-tests, K-e2e and
K-docs, and clio-knowledge KB-1..KB-7 less the story 2 lines; hours are the
[estimate](../eng-92710-call-web-service-element/eng-92710-call-web-service-element-estimate.md)'s and are not split
per story
**Repo**: crt-process-builder, then clio-knowledge, then clio (one feature PR each, titled
`ENG-92710 Call web service element`, branch `feature/ENG-92710-call-web-service-element`), after this docs-only clio
PR (branch `docs/ENG-92710-call-web-service-element-spec`). Story 2 ships in the same three PRs.
**Depends on**: ENG-91843 Add and modify process parameters (Closed). This BMAD set merges before the package PR leaves
draft (Q19).
**Blocks**: [story 2](story-eng-92710-call-web-service-element-2.md) (slice D follows slice C and the EM5 STOP gate).
Related, not blocking: ENG-92730 Complex / structured parameter types (the deferred mirror, ObjectList and 3+ level
scope) and ENG-105475 Register a REST web service through the AI Toolkit (registration).

---

## As a

coding agent building a business process through the clio MCP process tools

## I want

to add a Call web service element that calls a service registered in Studio > Web services, fill its request
(including a composite body or the raw request body) and read its response through the mappings I already use, and
read the element back in the shape I write it

## So that

a process can call a registered REST service and use its result without the Process Designer, and the stored element
is the one the designer would have saved

---

## Acceptance Criteria

The full text is the SPEC's AC1-AC9 (written to ENG-92710 Call web service element on 2026-11-03, decisions Q18). This story owns:

- [ ] **AC1** - the `webService` block builds `ProcessSchemaWebService` on create, `addElement` and `setElement`;
  request values, a composite body and `RequestBody` are set through the existing mappings by the stored names, in the
  same request.
- [ ] **AC2** - an unknown or ambiguous service or method fails with a list of codes and captions (or of the service's
  methods); a missing service points to registration in Studio > Web services; an environment with no registered
  service gets its own refusal; names are matched case-insensitively and stored canonically.
- [ ] **AC3** - inputs map in through the existing mappings; a composite response is read into a declared Collection
  (up to two levels) with item mappings; the web-service mirror cases refuse with a pointer to that route (Q6).
- [ ] **AC4** - `ignoreErrors` is stored as `IX4`; `Success`, `StatusCode` and `Response` are usable as outputs.
- [ ] **AC5** - server serialization matches the designer-built captures, the 4 BK15 rows included; a builder-made
  element survives a designer re-save with no byte change (EM5).
- [ ] **AC7 (read back)** - describe returns the `webService` member and the stored parameter tree, unbound inputs
  included. The issues and the modify notice for an unresolved element are story 2.
- [ ] **AC8** - no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output
  (describe, refusals, notices, logs, test snapshots of builder output); the element's stored bindings (process data
  the caller can already read through describe, M-31), not service data, are returned by design through describe's
  generic `parameters[]` (Q16 as agreed). The pattern-A corpus
  fixtures are copied verbatim from the shipped corpus (SHA pins) and may carry the shipped services' non-credential defaults; TC-P-08 asserts they
  carry no credential.
- [ ] **AC9** - package, clio and knowledge tests, package docs, the guidance article and the MCP surface updated.

---

## Gating

EM status as the measurements file records it on 2026-11-03.

| Gate | What | Who | Before |
|---|---|---|---|
| Q1-Q20 | answered and recorded | owner | done 2026-11-03 |
| Q18 | Jira description and the 9 AC written, checklist field untouched, read back | owner, then lead | done 2026-11-03 |
| 0.3 / Q19 | claim comment on ENG-92710 Call web service element (empty claim table; every number claimed there after checking the tags of both repositories) | lead, on the owner's yes | the first cut, the experimental one included |
| 0.4 | drift check against the basis (fetch first) | lead | slice A |
| BMAD | this SPEC and the ADR on the docs branch | lead | any package code |
| EM0, EM1, EM9 | baseline; the test services `UsrWscEchoService`, `UsrWscDriftService`, `UsrWscDeadService`; compile floor | lead | done 2026-11-03 (M-22; M-19, M-23; M-14) |
| EM2 | designer capture A, the AC5 oracle | lead in Claude in Chrome, owner logs in | S1 done 2026-11-03 (M-24); the read-back step runs on the final cut |
| EM3 | the designer's "Create from element" shape (route (a)) and a declared Collection with item mappings (route (b)) | lead | done 2026-11-03 (M-15, M-25) |
| EM7 | `ignoreErrors` true vs false on a 404 and on a refused connection | lead, owner's yes per write | designer half done 2026-11-03 (M-28); builder half on the claimed cut, before the PRs leave draft |
| STOP | EM5: a builder-made element re-saved by the designer and modified by an unrelated builder modify, stored bytes diffed; the row-less case only through a designer copy | lead, owner's yes per write | slice C; re-run on the final cut |
| EM6 | a builder-made process runs and the result params arrive (reduced by EP option E4) | lead, owner's yes | the PRs leave draft |
| EM10, EM11, EM12 | builder halves: no package dependency added (N6), canonical method name, N5 binding stored; EM12's run half is not measurable under E4 | lead, owner's yes | the PRs leave draft |
| gate 1 | full 3-lens fan-out over the package diff (shared sub-process code in P-A4, the build hook in P-B5, the parameter service in P-C6, Q16 as a security check) | lead | the draft package PR opens |
| KB publish | the knowledge generation that publishes `process-web-service` | human merge | CL-8 re-pin; the clio PR leaves draft |

If EM5 shows a rewrite, a duplicate BK15 row or a lost Success L8, work stops, the diff goes to the owner, Q4 (and
possibly Q3) is re-opened and the estimate is redone before slice C starts.

---

## Tasks

Ids are the analysis plan's (sections 3-5); components C1-C14 and K1-K11 are the design file's. TC ids are the test
plan's; the rows that story 2 owns are listed there.

**CrtProcessBuilder - slice A: resolver, shaper, behaviour-neutral seams** (one commit each; gate 2 single combined
lens for P-A1..P-A3, full lens for P-A4 if its diff exceeds ~50 lines)

- [ ] **P-A1** C2 service catalog / resolver (Q9, Q10, Q16): service by UId, code, caption, case-insensitive,
  ambiguity refused; method by code within the service; canonical spelling and manager-item UId returned; refusals
  list codes with captions, the unknown-service refusal adds the registration pointer, an empty catalog has its own
  refusal; no default, auth info or base URI in any text (TC-U-15..21, 25, 26, 105).
- [ ] **P-A2** C3 parameter shaper, a pure port of the designer client: names, tags, types, nested items always (no
  feature read), `ItemValue` children, IsRequired rule, captions with the top-level suffix rule (EM2), result params,
  Boolean "false" values, designer order (TC-U-01..14, 94, 100).
- [ ] **P-A3** C4 platform-sync seam and its probe; no production caller (TC-U-81..86).
- [ ] **P-A4** C5: the characterization commit first (TC-U-93), then the lift of the sub-process `Snapshot` / `Diff`
  and drift report over `ProcessSchemaParametrizedFlowNode` in its own commit; every existing `SubProcess*Tests` passes
  unchanged.

**CrtProcessBuilder - slice B: handler, applier, block, BK15, same-request ordering**

- [ ] **P-B1** C1 contract `webService {service, method, timeoutSeconds?, ignoreErrors?}` on the element and
  element-update descriptors, `ElementTypes` token `webservice`; wire-contract rows; the server refuses undeclared
  request keys from 1.6.6.136 (`UnknownRequestKeyDetector`), and the read-back-only keys are derived automatically
  because the write and describe members are both keyed `webService`; nothing to register; operation count stays 8 (TC-U-87, 88).
- [ ] **P-B2** C6 `WebServiceElementHandler`: `ManagerItemUId` stamp, strict block gate, block-less create refused, DI
  line (TC-U-22, 27, 30, 89, 90).
- [ ] **P-B3** C7 applier, Create mode: `IX1` / `IX2` canonical, `IX3` = seconds x 1000, `IX4` written directly (Q7),
  shaper output, platform sync tolerated (TC-U-28, 29, 91).
- [ ] **P-B4** C8 BK15 writer: one row per result param, as the designer writes them; `A2` not rewritten on a rename
  (TC-U-40..45, 106).
- [ ] **P-B5** C9 same-request ordering: selection in the `afterElementsPlaced` hook on create, before later operations
  on modify; a mapping onto a name the method does not produce is refused with the parameter list (TC-U-31, 32).
- [ ] **P-B6** mapping coverage (Q5, Q6): the three corpus shapes, `RequestBody` by constant and formula, the existing
  refusals; no mapping-rule change, no JSON check and no conflict notice (TC-U-56..64, 66, 67).
- [ ] Experimental cut (claimed first, marked "experimental, stand-only, superseded" in the claim comment), installed on
  stand `Creatio` on the owner's yes; EM5 recorded in the measurements file (STOP gate).

**CrtProcessBuilder - slice C: describe, guards, notices, parity suite**

- [ ] **P-C1** C10 describe: the `webService` member and the stored parameter tree (direction, tag, type, items,
  bound, result), unbound inputs included, read from the element, never a default; `buildType: "webservice"`; the
  read-back-only keys are derived automatically because the write and describe members are both keyed `webService`;
  nothing to register; a described block replayed through the server key check (TC-U-68..70, 74, 75, 77..80, 99, 109). The replay half of the round trip (TC-U-78, TC-I-06;
  TC-U-68 for the bindings) sends the described block back unchanged, read-back-only keys (`serviceUId`,
  `serviceResolved`, `methodResolved`, `issues`, `parameters`) included; it goes through the same-pair no-op of story 2's P-D1, so it turns green
  only once P-D1 lands.
- [ ] **P-C2** C11 guards: `userTask` = `WebServiceUserTask` refused with a pointer to the block (Q1); SOAP refused;
  `setElement` with a `webService` block on a designer-built element that carries `BP6` refused (Q20; a
  `webService.multiInstanceOptions` key on create is clio's key-check refusal, TC-C-02); nothing written after a
  refusal (TC-U-24, 47, 48).
- [ ] **P-C3** C12 notices: N1 generated names, N2 canonicalised method name, N3 feature gate (one single-purpose
  reader of `DisableIgnoreErrorsInWebServiceUserTask` with a test fake, as `FileProcessingFeatureReader`), N5 nested
  array-in-array on every target (Q13), N6 cross-package service (Q9), N8 required input without a source, N9
  duplicate nested name; when each fires and when it does not (TC-U-19 notice half, 23, 52, 55, 95..98).
- [ ] **P-C4** parity suite: pattern A (corpus pairs under `Fixtures/WebService/`, `PROVENANCE.md` SHA pins, a
  `.gitattributes` `-text` line), pattern B (EM2 and EM3 captures, committed redacted), pattern C (writer JSON);
  every exclusion named in WSCR (TC-P-01..11, 13, 14). The pattern-A corpus pairs are copied verbatim from the shipped
  corpus and not redacted (SHA pins); they may carry the shipped services' non-credential defaults, and TC-P-08
  asserts no credential (no `IQ5` value with `IV2` 1, no constant `Authorization` default).
- [ ] **P-C5** C14 docs: `docs/web-service-element-capture.md` with the EM results; the handler list in
  `docs/process-builder-architecture.md` section 3.5.
- [ ] **P-C6** C13 mirror refusals: the WS CompositeObjectList mirror message gains the declared-Collection pointer;
  the WS ObjectList mirror is refused with nothing written; gate 2 full fan-out (shared parameter service)
  (TC-U-65, 101).
- [ ] Gate 1 before the draft package PR opens; claimed cut, install, EM6, EM7, EM10, EM11, EM12 builder halves; final
  cut, EM2 read-back step and the EM5 re-run.

**clio-knowledge**

- [ ] **KB-1** new article `guidance/mcp/guides/processes/web-service.md` (`process-web-service`): scope, the
  FULL-contract gate and the real floor (no TBD at merge), the block, resolution and canonical names, registered
  services only, request values and when to use `RequestBody`, responses into a declared Collection, errors and
  `ignoreErrors`, run-time facts (N5), read back, refusals and notices, the "task without details" section (Q10). The
  modify and drift sections are story 2's.
- [ ] **KB-2** `element-catalog.md` and `element-catalog-details.md`: `webService` flipped to buildable with the floor;
  the `ProcessGuideContentPreservationTests` READ-ONLY pin replaced by the version-floor pin in the same commit.
- [ ] **KB-3** `process-modeling.md` cannot-create list, paid by a swap (about 13 B of headroom measured r5 on master;
  re-measured on the branch); `routing.md` row
  `name=process-web-service`, separated from the `configuration-webservice` guides.
- [ ] **KB-4** `bundle-source.json`: resource with no `legacyUris`, `requirements.itemIds` and `resourceUris`, the
  `process-element-catalog` and `process-modeling` descriptions, `libraryVersion` above master and every open branch
  claim, re-checked at merge; no authored `sequence`.
- [ ] **KB-5** tests: `WebServiceGuidanceTests`, `ProcessGuideCrossReferenceTests`,
  `GuidanceMigrationTests.PostMigrationGuidance`, size gates, manifest (TC-K-01..04, 06..11, 13; TC-K-05 shared with
  story 2).
- [ ] **KB-6** fact-check gate: one refute-first agent per claim cluster, barred from citing `docs/knowledge/` or the
  analysis folder; every refutation re-checked by hand; a traced-only claim names its EM or is cut (TC-K-12).
- [ ] **KB-7** agent runs AR-1 (service not named) and AR-2 (service not registered: the agent stops) on the draft
  article; a failure is fed back before the KB PR leaves draft.

**clio** (read `docs/agent-instructions/bundled-packages.md` before CL-1 and CL-3, and `docs/knowledge/ProcessModel/`
and `docs/knowledge/McpServer/` before any edit)

- [ ] **CL-1** rebundle the final cut (`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <pkg> -Version
  <claimed>`), PROVENANCE pins in the same commit, security counts unchanged (TC-C-08).
- [ ] **CL-2** K2 `WebServiceBlock` key set in `ProcessBlockKeyCheck` (`Blocks`, `Element`, `ElementUpdate`) and the
  describe read-back keys, no child key sets (TC-C-01..05). The key set and short form held equal to the bundled archive (CL-2,
  CL-3; TC-C-04..06) land in the rebundle commit, so no commit is red.
- [ ] **CL-3** K3 `"webService"` in `CreateElementBlockShape` within the wire-bundle headroom (TC-C-06).
- [ ] **CL-4** K4 floors: the `[RequiresPackage]` literal on create / modify / modify-as-new-version moves to the cut
  claimed in the claim comment; floor-history paragraph; `webService` in the floor-reason sentences (TC-C-07).
- [ ] **CL-5** K5 descriptions (Q15): create type enum and one `webService?` pointer clause, modify lists, describe
  read-back clause, `validate-process-graph` buildable list, the `WebServiceUserTask` refusal in `list-user-tasks`;
  paid by swaps, re-measured with the ceilings lowered on the branch, ASCII only (TC-C-09, 12; TC-C-11 shared with
  story 2).
- [ ] **CL-6** K6 prompts (create / modify / describe process prompts, `ListUserTasksPrompt`) (TC-C-13, shared with
  story 2).
- [ ] **CL-7** K7 typed `DescribedWebService` and its children, each with an overflow bag; recount the
  `described-filter-types-have-no-json-overflow-bag.md` record (TC-C-10, 15).
- [ ] **CL-8** K8 `docs/McpCapabilityMap.md` rows; curated re-pin to the knowledge generation that publishes
  `process-web-service`. The clio PR stays draft until that generation is published; if KB publishes after the clio PR
  is approved, the re-pin goes into a separate PR instead of a push (TC-C-14).
- [ ] **CL-9** K9 e2e `WebServiceElementToolE2ETests`: create + describe, same-request mappings, refusals with nothing
  created, the EM2 fixture read-only, run with result params and ignore-errors, `validate-process-graph`, N5; `MinimumPackageVersion` = the bundled version; strictly sequential; run on stand `Creatio` on net10 and net8,
  Ignored = 0 (TC-I-01..04, 06..09, 11, 12; TC-C-16; TC-I-05 and TC-I-10 are story 2's). TC-I-13 in the existing
  `ListUserTasksToolE2ETests`: the `list-user-tasks` contract text names `WebServiceUserTask` as refused on the
  generic route.
- [ ] **CL-10** K10 knowledge records only for facts the code does not say (the package's real compile floor of about
  8.3.2, EM9, why `IX4` is referenced directly; BK15 rows runtime-neutral); every knowledge record whose `applies-to`
  path the clio diff touches updated or deleted (AGENTS.md); `make check-knowledge` read.

Validation: package `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
and `-c dev-n8`, every new fixture carrying the CI categories; clio `dotnet test clio.tests/clio.tests.csproj --filter
"TestCategory=Unit" < /dev/null` (more than three modules change: full unit suite); clio-knowledge the whole
`Clio.Knowledge.Bundle.Tests` project. The clio PR body carries "MCP reviewed" (tools, prompts, resources, `clio/tpl`,
`clio.mcp.e2e`; the `list-user-tasks` change is description-only (CL-5) plus its prompt (CL-6), covered by TC-C-11,
TC-C-13 and the e2e row TC-I-13), "docs reviewed, no update required" (the process tools have no CLI verb), the
ClioRing statement with the inspected paths, the re-measured budgets and the `Validated:` command. Manual Jira cases of this story: TC-01..06,
TC-09..11 (test plan section 8).

Merge order (human merges only, Q19): this docs PR; then package (tag pushed on the remote) -> knowledge (generation
published) -> clio. Approvals are kept: no push after an approval without asking.

---

## Definition of Done

Plan section 9, one box per row: AC1, AC2, AC3, AC4, AC5, AC7 (describe), AC8, AC9 and the general row (story 2
owns AC6 and the drift half of AC7). Ticked only when true.

- [ ] **AC1** `webService {service, method, timeoutSeconds?, ignoreErrors?}` on create `addElement` / `setElement` /
  modify builds `ProcessSchemaWebService`; request params, including a composite request body (mappings into nested
  items) and `RequestBody` raw text (Q5), are configured through the existing mapping surface in the same request,
  addressed by the names describe returns; round trip writes nothing (its replay closes with story 2's P-D1); EM5
  passed.
- [ ] **AC2** unknown or ambiguous service or method refused with the service list (codes and captions) or the
  resolved service's methods; a missing service and an empty catalog point to Studio > Web services; names matched
  case-insensitively and stored canonical (Q9, Q10; EM11's builder half on the claimed cut).
- [ ] **AC3** constants, formulas, process parameters and element outputs map into `_In` params and dotted item paths;
  a composite response is read into a declared Collection (two levels) with item mappings, container bound
  as well (Q6, EM3 route (b) parity); the request bytes and the composite response read rest on the core unit tests
  and the parity suite unless the owner adds a JSON source (Q17), and the PR body says so; the scope of ENG-92730
  Complex / structured parameter types recorded as the follow-up, not done here.
- [ ] **AC4** `IX4` written and read back; Success / StatusCode / Response usable in conditions and mappings; run time
  proven on stand `Creatio` by EM6 as far as EP option E4 shows (result params, StatusCode, Success) and by EM7's
  builder half matching its designer half (M-28).
- [ ] **AC5** parity patterns A (corpus pairs), B (EM2 / EM3 captures; the EM4 capture is story 2's TC-P-12) and C (writer diff) green; the 4 BK15
  rows written (Q4); a builder-made element survives a designer re-save with no diff (EM5, re-run on the final cut).
- [ ] **AC7 (describe)** describe returns the `webService` member and the stored parameter tree, unbound inputs
  included (Q14).
- [ ] **AC8** no service-side data (service parameter default, `authInfo`, base-URI credential) in builder output
  (describe, refusals, notices, logs, test snapshots of builder output); the element's stored bindings (process data
  the caller can already read through describe, M-31), not service data, are returned by design, only through
  describe's generic `parameters[]`; the corpus fixtures carry no credential (TC-P-08); Q16 tests and grep.
- [ ] **AC9** package tests on dev-nf and dev-n8 plus the first CI run of the draft package PR; clio `TestCategory=Unit`
  full suite; KB test project; MCP surface (descriptions, prompts, key check, short form, typed describe, capability
  map, curated re-pin) updated; e2e on stand `Creatio` on net10 and net8 with Ignored = 0; docs: capture doc,
  architecture doc, KB article, catalog flip.
- [ ] **General** every TC row of the test plan that this story owns implemented and green (all rows except story 2's,
  TC-U-109 and TC-I-13 included; the rows whose replay or retarget half needs story 2's P-D1 close together with story
  2: TC-U-19, 23, 68, 78, 95, 98 and TC-I-06) and agent runs AR-1 and AR-2 passed; gates 1-3
  passed and their tiers stated; KB fact-check passed; the clio PR body carries "MCP reviewed", "docs reviewed, no
  update required", the ClioRing statement with the inspected paths, the `Validated:` filter and the re-measured
  budgets; knowledge records in the clio PR, every record whose `applies-to` path is touched updated or deleted, and
  the `make check-knowledge` report read; the tracker rows moved as plan section 2 says; story DoD boxes ticked only
  when true; no service-side data in builder output and no credential in any committed file (Q16 grep); stand
  leftovers listed (measurements file register), cleanup run only on the owner's yes.
- [ ] **General (code hygiene)** `CLIO*` diagnostics clean in modified files; new public members carry `///` docs
  (AGENTS.md).

## Dev Agent Record

- 2026-11-03: analysis, decisions Q1-Q20 (all agreed), measurements EM0-EM4, EM9, the designer halves of EM7, EM10
  and EM11 and EM8's current-builder, service-save and designer halves (M-18, M-29..M-31) done; the description of
  ENG-92710 Call web service element written after the owner's yes; this BMAD set written. No product code yet.
