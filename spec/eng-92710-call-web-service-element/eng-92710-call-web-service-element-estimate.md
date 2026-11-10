# ENG-92710 Call web service element: estimate

Written 2026-11-02; updated 2026-11-03 for the agreed decisions (section 0.1).

Basis: crt-process-builder `origin/main` `da719366` (CrtProcessBuilder 1.6.6.131, history), clio `origin/master`
`6be5dde28`, clio-knowledge `origin/master` `5f14ceb` (libraryVersion 1.15.176), CrtProcessDesigner 7.8.0 and the
PackageStore corpus, core platform trunk at `468c4c71b` (8.3.3.1722), a read-only inventory of
stand `Creatio` (Creatio 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.131) taken 2026-11-02 and the day-0 probes
of 2026-11-03. Inputs: the answers Q1-Q20 of the [decisions file](eng-92710-call-web-service-element-decisions.md),
agreed by the owner on 2026-11-03 (its "Agreed by the owner" lines override the PROPOSED text), the slices,
tasks and gates of the [plan](eng-92710-call-web-service-element-plan.md) (P-A1..P-D4, KB-1..KB-6, CL-1..CL-10), the
measurement ids EM0-EM12 of the [measurements file](eng-92710-call-web-service-element-measurements.md), and the
calibration in research note r10 (local research notes) as corrected by the
fact-check verdicts in `_workflow-result.json` (r10-C1, C2, C3, C4, C7, C8, C14). Where a verdict corrected r10, the
corrected figure is used and the verdict is cited.

Model (owner rule): an AI agent writes the code, the tests, the docs and the guidance. The estimate gives (1) agent
wall time, (2) human time that does not compress, (3) calendar time set by owner latency, stand gates, review latency
and the package -> KB -> clio order. The headline is the agent number. No future CrtProcessBuilder cut is named;
versions below are history only. Every figure in sections 2-6 is **inferred** (an estimate); calibration inputs are
**measured** (git, transcripts, corpus) or **prior-measured** (measured earlier and recorded in a cited file).

## 0. Headline

| Quantity | Likely | Range | Note |
|---|---|---|---|
| **Agent wall time (AI writes)** | **~37 h** | 22-65 h | remaining from 2026-11-03: ~17.75 h code (16.4k lines x 1.1 h/1k code-only rate = 18.0, minus 0.25 for the agreed changes, 0.1) + ~19.15 h line-independent work (EM0-EM8 and EM10-EM12, EM9 spent; stand cleanup, cut cycles, stand e2e, agent runs AR-1 / AR-2, BMAD docs PR, KB fact-check, day 0); arithmetic in 0.1 and 2.6 |
| Owner time (does not compress) | ~12 h | 6.25-21.75 h | remaining: Chrome capture sessions, stand-write, cleanup and install approvals, diff review, review rounds, merges; the 20 decisions are spent (2026-11-03); section 3 |
| Calendar, owner's answers -> last merge | 6 working days | 3.2-12.75 | gate-ordered (EM5 STOP gate, KB publish before clio leaves draft); adverse events in section 6 add 0.5-3 wd each |
| Authored lines (all repos) | ~16.4k | 10.4-25.1k | package src 4.6k, package tests 7.8k, package docs 0.35k, clio 3.0k, KB 0.65k; EXCLUDES capture fixtures and the spec folder (next two rows) |
| Capture fixtures (copied, not costed) | 6,528 lines / 295,306 B for the three corpus pairs (pattern A, verbatim) | + EM2-EM4 stand captures (pattern B, committed redacted) | measured 2026-11-02 (`wc`, PackageStore 7.8.0): CheckMarketplaceAppNeedUpdateSubProcess 1,896 + CreatioMarketplaceApi 147, ActionGeminiSearch 700 + GeminiApiSearch 389, ExecuteUpgradeBsf 2,238 + teamcitybpmonline_BSF 1,158; the EM2-EM4 captures are committed with the stand address replaced by `<stand>` and the redacted keys listed in `PROVENANCE.md` ([serialization §8.1](eng-92710-call-web-service-element-serialization.md), compared under W14), sized only after EM2-EM4 (inferred 1-3k lines) |
| Spec lines (BMAD docs PR incl. this folder) | ~4-6k | | not authored code; precedents ENG-95985 Send email attachments 5,250, ENG-92707 Sub-process element: selection + parameter sync 4,057 (r10 §1, measured) |
| PRs | 5 | 4-7 | 4 planned (BMAD docs, package, KB, clio) + 0-3 follow-ups; section 4 |
| CrtProcessBuilder cuts | 5-6 | 4-8 settled; 10-25 if review keeps finding scope errors | section 4.2 |

Not in these figures: the research and this plan (2026-11-02, spent, not measured here; the ENG-95985 analog was 3.5 h
of agent wall time, r10-C1); the work spent on 2026-11-03 (the twenty decisions, EM9, the Q4 BK15 probe and the
day-0 halves listed in 0.1; not measured here); the ENG-92730 Complex / structured parameter types scope (mirror relaxation, ObjectList, 3+ declared
levels, the designer's "Create from element" CompositeObject wrapper, Q6); a discovery tool (Q10); registering a
web service (ENG-105475 Register a REST web service through the AI Toolkit); reviewers' own time (about 3-6 h over 4-5 PRs, inferred from the ENG-95985 estimate).

### 0.1 Changes on 2026-11-03

The owner agreed Q1-Q20 on 2026-11-03; the day-0 probes ran the same day. Line figures (sections 0, 2.1-2.3) were
sized on 2026-11-02 and are not re-sized: the dropped and added components sit inside rows that have no line count of
their own, so their hours enter as one adjustment row in 2.6. Agent h, low / likely / high:

| Item | Source of the delta | Low | Likely | High | Lands in |
|---|---|---|---|---|---|
| Agent total before | 2.6 as of 2026-11-02 | 21.8 | 37.4 | 66.35 | |
| Q7: IX4 written directly; design C11's capability guard (`IWebServicePlatformCapabilities`, reflection on `IsIgnoreErrors`), its fake-absence tests and the "ignoreErrors without IX4" refusal dropped | decisions Q7; the 2026-11-02 6.2 row "EM9 outcome (a)": -0.5-1 agent h | -0.5 | -0.75 | -1 | 2.6 adjustment row |
| Q10 addition, texts and tests: registration pointer in the unknown-service refusal, the no-service refusal, TC-U-105, the KB "task without details" section, TC-K-13 | decisions Q10: about +0.5 agent h | +0.5 | +0.5 | +0.5 | 2.6 adjustment row |
| Q10 addition, agent scenario runs AR-1, AR-2 | decisions Q10: about +0.5 agent h | +0.5 | +0.5 | +0.5 | 2.5 new row |
| EM9 done (local, outcome (a)) | 2.5 EM9 row | -0.5 | -0.75 | -1.5 | 2.5 EM table |
| **Agent total after** | 21.8 - 0.5 + 0.5 + 0.5 - 0.5; 37.4 - 0.75 + 0.5 + 0.5 - 0.75; 66.35 - 1 + 0.5 + 0.5 - 1.5 | **21.8** | **36.9** | **64.85** | headline ~37 h (22-65) |

Owner h: 7.75 / 14.2 / 24.75 minus the spent decisions row (1.5 / 2.25 / 3) = **6.25 / 11.95 / 21.75**, headline
~12 h. Calendar: unchanged at 3.2 / 6.0 / 12.75 wd; the answers it counts from came on 2026-11-03.

Not sized from this file's rows, so left in the figures (each an overstatement, never an understatement):
- Q3 drops the feature-reader seam (`IWebServiceFeatureReader`), notice N11 and the feature-off half of TC-U-100.
  They sit inside WP A's lines with no row of their own.
- Q5's JSON check of a constant `RequestBody` and the `RequestBody`-vs-Body conflict notice were never costed here:
  no delta.
- Q17 chose EP option E4 (E1-E3 not chosen). The E2 endpoint and its compile were costed only as 6.1 row 7, an
  up-event outside the headline, so the headline does not move; row 7 is settled. Under E4, EM6 shrinks to what a
  stand answer shows and EM12's run half is not measurable on stand `Creatio`. Their rows (EM6 0.75 / 1.25 / 2,
  EM12 0.25 / 0.4 / 0.75) are not split, so they stay whole: at most 1 / 1.65 / 2.75 agent h too high.
- Day-0 halves done on 2026-11-03: EM1 in part (`UsrWscEchoService` with one GET method and the header, decisions
  Q16), EM3 route (a) (decisions Q6), EM4's service change (decisions Q11), EM8's current-builder half (decisions
  Q12), and the designer halves of EM10 and EM11 (decisions Q9). The rows have no split, so they stay whole: at most
  3.1 / 4.75 / 7.75 agent h too high (EM1 1.5 / 2 / 3 + EM3 0.5 / 0.75 / 1.25 + EM4 0.25 / 0.5 / 0.75 + EM8
  0.5 / 0.75 / 1.25 + EM10 0.25 / 0.5 / 1 + EM11 0.1 / 0.25 / 0.5, before batching). The owner's Chrome-session and
  stand-write approval rows (section 3) are partly spent by the same probes and are kept whole for the same reason.
- The Q4 BK15 probe (`UsrWscBk15Probe`) has no EM row: spent, never in the figures.

## 1. Calibration (delivered features)

Source: r10 §0-§4 (git `--numstat` per merge, transcript union of activity intervals with a 15-minute idle cutoff),
corrected by the verdicts. Lines are authored added lines in all three repos, without verbatim test data and without
clio `spec/**`.

| Ticket | Analog (A schema sync / B composite / C mapping) | Authored lines | Agent h | h / 1k | Owner engaged h (in transcript) | Calendar | PRs pkg/clio/kb | Cuts | Note |
|---|---|---|---|---|---|---|---|---|---|
| **ENG-92707 Sub-process: selection + parameter sync** | **A** / - / C | 9.0k (7.9k prod+test basis) | **22.6** (prior-measured) | 2.9 on 7.9k (r10-C8: do not pair 22.6 h with 9.0k) | 5.3 | 5.07 d (09-16 -> 09-21) | 6 feature PRs (2/1/3) + clio #1642 records correction = 7 merged (r10-C8) | **27 descriptor versions** (1.6.3.0-.26) on its branch, 19 tags (r10-C7) | **23+ numbered review rounds** plus ~7 review-fix commits after a main merge (r10-C7); a rough re-run gives 24-30 h (r10-C4) |
| ENG-99856 "Sub-process element: support MULTI-INSTANCE (running the callee once per item of a collection)" Sub-process multi-instance | - / B (list) / C | 10.6k | 15.6 (prior; one session; 20.4-25.3 h with post-review and research sessions, r10-C3) | 1.6 | 3.0 | 2.0 d | 1/1/1 | 14 (r10-C7) | cuts in two days |
| ENG-91844 "Implement full parameter mapping (sources)" Mapping sources | - / - / C | 3.5k | 9.1 (prior) | 2.8 | 0.8 | 5.6 d | 1/1/1 | 9 | waited on merge order |
| ENG-92711 "Script task element" Script task | - / - / C | 12.4k | 24.4 main PR set; **~35 incl. QA round 2** (r10-C4) | 2.3 (main set) | 3.4 | 5.0 d + 3 d QA round | 2/2/2 | 29 in total incl. the QA set (r10-C7) | platform-fidelity review rounds |
| ENG-95984 "File process parameter type" File parameter type (PT) | - / (B type) / C | 10.3k | 14.1 | 1.4 | 2.7 | 0.75 d code | 1/3/1 | 3 | analysis done before |
| ENG-96505 "Element readiness and object attachments mode" Object attachments (OA) | - / B (list outputs) / C | 19.3k | 18.4 (17.3-19.5 re-run, r10-C3) | 0.95 | 4.3 | 4.9 d incl. weekend | 2/2/1 | 4 + 1 follow-up | follow-up PRs |
| ENG-96506 "Generated report + process parameter modes" Report + parameter (RP) | - / B / C | 12.7k | 17.9 (13.9-15.3 on a narrower split, r10-C3) | 1.1-1.4 | 4.1 | 1.0 d | 1/1(+1 docs)/1 | 5 | src +40% over plan |
| ENG-103937 "Process file element: SysFile attachment storage" SysFile storage (SF) | - / - / C | 6.0k | 11.1-14.0 (r10-C4) | 1.9-2.3 | 2.5 | 0.95 d | 2/2/1 | 5 + 2 follow-up | lines 2.5-4x the plan (r10-C3) |
| ENG-103162 "Declared item shape for Collection process parameters" Collection item shape | - / B (item shape) / - | 1.5k | ~5.8 (low confidence) | ~3.9 | n/m | 2.6 d | 1/2/1 | 3 | waited for approvals |
| **ENG-95985 Send email attachments** | - / **B** (element-owned CompositeObjectList + nested items) / **C** | **20.4k** itemized (6,007 / 9,810 / 3,852 / 712); 20.7k like-for-like with 316 descriptor, docs and DI lines (r10-C2) | **34.1** at the 15-min cutoff (29.8-37.3 at 5-30 min); research 3.5 + build/review **30.5** (r10-C1) | 1.65 (1.70 on the plan's prod+test basis, r10-C3) | 6.9 (107 prompts) | 4.95 d; **3 working days after the decisions** | 4 (BMAD docs, pkg, clio, kb) | 6 | +11,588 verbatim capture lines, +5,250 spec lines |
| Outlier ENG-91853 "Exclusive and parallel gateways, conditional/default flows + basic Y auto-layout" Gateways + auto-layout | | | 134.1 (prior, r10-C4) | | | | | | not element-sized; shows the tail |

### 1.1 The last estimate against its actual (ENG-95985)

| Quantity | Estimate (2026-10-28) | Actual (measured 2026-11-02) | Ratio | Lesson used here |
|---|---|---|---|---|
| Agent wall time | ~42 h (23-68), research not included | 30.5 h build + review (26.3-33.8 by cutoff), r10-C1 | 0.73 | the fair comparison excludes research (r10-C1 (3)) |
| Authored lines | ~9.8k (6.9-12.7k) | 20.4k itemized / 20.7k like-for-like | **2.1x** | lines are sized with an explicit review-growth row (2.2) and a tests/src ratio of ~1.7 |
| Package src | 1.5-2.5k | 6.0k | 2.4-3.9x | about half of the overrun was scope the owner added in review (reuse consolidation, new refusals and notices), half was the planned slots being larger (r10 §1.1) |
| Rate | 2.75 h/1k central (code-only) | 1.65 h/1k all-in | 0.6x | the code-only rate drops to 0.8-1.5, central 1.1 (constants below) |
| Calendar | 7 wd (4-12) | 3 wd | 0.43 | decisions came in one sitting; review took ~1 working day once ready |
| Cuts | 4 (3-5) | 6 | 1.5x | budget 5-6 for a settled design |
| PRs | 5-6 | 4 | lower | follow-ups deferred, not yet opened |
| Owner | ~15 h | 6.9 h in transcript + untracked (merges, review by others, Jira by hand) | in-transcript ~0.46 | the in-transcript share is about half of the owner figure |

Hours per feature were steadier than lines: the line count was under-sized 2.1x, the rate over-sized 0.6x, and the
two errors cancelled for ENG-95985 and PT, but not per element for SF and RP, whose lines were 2.5-4x the plan and
whose lower rate did not make up for it (r10-C3).

### 1.2 Constants used below

| Constant | Value | Evidence |
|---|---|---|
| All-in agent h per 1k authored lines, October owner features | 0.95-2.3, median ~1.5 | r10 §4 [measured]; on the plan's prod+test basis 1.0-2.5 (r10-C3) |
| Same, September (incl. the external-schema analog) | 1.6-2.9; ENG-92707 2.9 | prior-measured; the September figures count fewer sessions, so they are if anything low (r10-C3 caveat 2) |
| **Code-only rate used here** | **0.8-1.5, central 1.1 h/1k** | [inferred] ENG-95985: 30.5 h build minus ~10-13 h of line-independent work (EM campaign, 6 cut cycles, 21 TC runs, BMAD docs) = 17.5-20.5 h over 20.7k lines = 0.85-0.99 h/1k; raised to 1.1 central because slices A and D are the ENG-92707 class (2.9 h/1k in September) |
| Agent h per element-sized feature | 11-35 h, without a research or redesign phase | r10-C4 [measured / prior-measured] |
| tests/src | 1.0-2.6 (ENG-95985 1.63, OA 1.29, PT 2.04, SF 2.6) | r10 §4 [measured] |
| Owner engaged h in transcript | 2.5-6.9 per feature; 0.2-0.45 per 1k lines | r10 §2, §4 [measured]; excludes browser review, merges, Jira by hand |
| Cuts per feature | 3-6 when the design is settled; 13-29 when review keeps finding scope or platform-fidelity errors (ENG-99856 14, ENG-92707 27 versions, ENG-92711 29) | r10-C7 [measured] |
| PRs per feature | 3-5 plus 0-2 follow-ups; ENG-92707 7 | r10 §1, r10-C8 [measured] |
| Review ready -> merge | ~1 working day (ENG-95985); File family 0.2-7.9 h | r10 §4 [prior-measured] |
| Merge tail package -> last of clio/KB | 32 min (ENG-95985); earlier 1-72 min | r10 §4 [measured] |
| Research + plan fan-out | ~3.5-5 h wall | r10-C1 [measured] |
| Composite half of this ticket (mapping in/out of WS nested items) | ~10 h; ~25 h if mirror relaxation / ObjectList / 3 levels enter scope | g3 §5 [inferred there from traced rules] |

## 2. Size and effort per work package

Agent h = lines x 0.8 (low) / 1.1 (likely) / 1.5 (high) h per 1k. Owner diff review = lines x 0.25 / 0.35 / 0.5 h
per 1k (section 3). Lines were sized on 2026-11-02 against the PROPOSED answers; low and high are the two ends of
what those allowed. The answers agreed on 2026-11-03 drop or add small components inside rows A-C and the KB row;
they are not re-sized by line but taken as hours in the 2.6 adjustment row (arithmetic in 0.1), so these line
figures still hold the dropped Q3 and Q7 components. A later change of answer moves the row named in section 6. Slice and task ids are the plan's; component names are working names (the
[design file](eng-92710-call-web-service-element-design.md) owns them).

### 2.1 crt-process-builder (one feature PR)

| WP | Content (plan tasks) | src low / likely / high | tests low / likely / high | Agent h low / likely / high |
|---|---|---|---|---|
| A | Slice A: service/method resolver with case-insensitive canonical names, ambiguity refusal and refusals that list codes with captions, never a default or auth text (P-A1, Q9, Q16; the Q10 registration pointer and no-service refusal are in the 2.6 adjustment row); the pure parameter shaper porting `process-webservice-schema.js`: `_In`/`_Out`, L17 Tag, recursive L18 always (Q3: no feature gate; the feature-reader seam and notice N11 these lines were sized with are dropped and not re-sized, 0.1), ItemValue child, flattened JSONPath scalars, MaxSizeText, IsRequired, caption resources with the " - Input"/" - Output" suffix only where a caption is shared across request and response (scope pinned by EM2), result params in designer order (P-A2, Q3); platform-sync seam + probe (P-A3); `Snapshot`/`Diff` generalized from the sub-process code over `ProcessSchemaParametrizedFlowNode` with characterization tests of every sub-process notice (P-A4) | 1,000 / 1,250 / 1,800 | 1,100 / 1,700 / 2,500 | 1.7 / 3.2 / 6.5 |
| B | Slice B: `webService` contract + wire rows (P-B1, Q2, Q8); handler with ManagerItemUId stamp and strict block gate, DI line (P-B2, Q1); applier Create mode: IX1/IX2/IX3, IX4 written directly (Q7, EM9), shaper output, platform sync tolerated (P-B3); the first package-authored BK15 rows behind one writer (P-B4, Q4); same-request ordering in the `afterElementsPlaced` hook (P-B5, Q10) | 750 / 1,000 / 1,350 | 900 / 1,400 / 2,000 | 1.3 / 2.6 / 5.0 |
| C | Slice C: describe `webService` member + stored parameter tree, no defaults (P-C1, Q14, Q16); guards: generic-route `WebServiceUserTask` refusal, the multi-instance refusal and the SOAP notice N10 (P-C2, Q1, Q20; the IX4 platform guard these lines were sized with is dropped by Q7, its hours taken out in 2.6); notices: cross-package service, a sourceless inner object array whose leaves are mapped (ENG-55920 Unable to use array inside array in web-service business process element), unresolved element (P-C3, Q9, Q13); parity suite: capture oracle, pattern A verbatim, pattern B committed redacted, pattern C, `PROVENANCE.md`, `.gitattributes -text` (P-C4); C13 mirror refusals for WS outputs: the CompositeObjectList mirror message gains the declared-Collection pointer, the ObjectList mirror is refused (P-C6, Q6 v1; not re-sized: design C13 puts it at ~0.5 agent h, inside this row's range) | 650 / 950 / 1,450 | 1,300 / 2,000 / 3,000 | 1.6 / 3.2 / 6.7 |
| D | Slice D: `setElement` Update mode with replace semantics and the dropped-bindings notice (P-D1, Q11); retarget guard on `FindDependents` (P-D2); drift issues and the latent-loss notice on modify (P-D3, Q12; the resolvability check only, no raw stored-list read); `modifyAsNewVersion` round trip (P-D4); Q11 A3 (keep the result params' UIds, TC-U-107: about 0.25 agent h, inside this row's range, not re-sized) | 450 / 750 / 1,100 | 700 / 1,100 / 1,700 | 0.9 / 2.0 / 4.2 |
| M | Composite in/out mapping coverage on the existing mapping surface (P-B6, Q5, Q6; `WebServiceMappingTests`, TC-U-56..64, 66, 67; TC-U-65 (P-C6) and TC-U-68 (P-C1) were sized in this row too, not re-sized, 0.1): the three corpus shapes of g3 (item-level into an IN collection, COL<-COL, declared response Collection <- WS outputs over two levels), `RequestBody` raw text by constant and formula, the refusals that already exist (ObjectList, source deeper than 2). g3: no rule change needed, so src is near zero | 0 / 50 / 150 | 300 / 500 / 800 | 0.2 / 0.6 / 1.4 |
| H | Harness churn: `CrtProcessBuilderAppTests` composition rows, `CreateProcessOperations` and `CreateGraphBuilder` constructors, the service-manager substitute wiring of r8 | - | 150 / 300 / 500 | 0.1 / 0.3 / 0.8 |
| G | **Review-driven growth**: reuse rounds, refusals and notices added in review, the kind that made ENG-95985's src 2.4-3.9x its plan (r10 §1.1) | 300 / 600 / 1,300 | 300 / 800 / 1,500 | 0.5 / 1.5 / 4.2 |
| Docs | `docs/web-service-element-capture.md`, the handler list of `docs/process-builder-architecture.md` section 3.5, descriptor, diary (P-C5) | 200 / 350 / 550 docs | | 0.2 / 0.4 / 0.8 |
| | **Package total** | **3,150 / 4,600 / 7,150** (+200 / 350 / 550 docs) | **4,750 / 7,800 / 12,000** | **6.5 / 14.0 / 29.6** |

Package lines: 8,100 / 12,750 / 19,700. tests/src likely 7,800 / 4,600 = 1.70, beside ENG-95985's 1.63: the shaper is
table-tested against the core's own case tables and the corpus pairs (r8), and every refusal and notice is pinned
with repository test style (AAA comments, a `because` on every assertion, a `[Description]` on every test).
Capture JSON (pattern A verbatim, pattern B redacted) is copied, not authored, and sits outside these lines (section 0).

Sanity check against the analogs: ENG-92707 package src was 2,981 lines for the sync half alone (slices A and D here
are its class); ENG-95985 was 6,007 for one element-owned composite member. 4.6k likely sits between them, 7.15k high
is the ENG-95985 class.

### 2.2 clio (one feature PR)

| WP | Content (plan tasks) | Lines low / likely / high | Agent h low / likely / high |
|---|---|---|---|
| K-src | CL-2 key sets, CL-3 short-form block, CL-4 floors, CL-5 descriptions paid by swaps (net lines small; the byte re-measurement by lowering the ceiling on the branch is the work), CL-6 prompts, CL-7 typed `DescribedWebService` and its children | 300 / 500 / 800 | |
| K-tests | Unit tests: pass-through, key-set and call-shape drift, description and prompt pins, describe typing | 500 / 900 / 1,400 | |
| K-e2e | CL-9 `WebServiceElementToolE2ETests` (create + describe, same-request mappings, modify replace with the retarget guard, refusals with nothing created, the EM2 fixture read-only, validate-process-graph). Precedents 1,228-1,874 lines; ENG-95985 1,587 | 900 / 1,400 / 1,900 | |
| K-docs | CL-8 capability map and curated re-pin (after the KB publish), CL-10 knowledge records | 100 / 200 / 350 | |
| CL-1 | Rebundle + PROVENANCE pins, once per cut | in the pins | in cut cycles (2.5) |
| | **clio total** | **1,800 / 3,000 / 4,450** | **1.4 / 3.3 / 6.7** |

ENG-95985's clio PR delivered 3,852 lines against a forecast of 1,460-3,050 (r5-C16); this element adds a new block
and a typed describe member rather than a member on a known block, closer to OA (2,911) and RP (1,791 + 693 docs).

### 2.3 clio-knowledge (one PR)

| WP | Content | Lines low / likely / high | Agent h low / likely / high |
|---|---|---|---|
| KB-1..KB-5 | New article `process-web-service` (its Q10 "task without details" section and TC-K-13 are in the 2.6 adjustment row); `element-catalog` flip from READ-ONLY to buildable with the floor pin; `process-modeling` swap (~13 B headroom) and `routing` row distinguishing configuration web services; `bundle-source.json`; `WebServiceGuidanceTests`, cross-reference, migration and size gates | 450 / 650 / 900 | 0.4 / 0.7 / 1.4 |
| KB-6 | Fact-check by refute-first agents barred from own records; every refutation re-checked by hand | | in 2.5 |
| KB-7 | Agent scenario runs AR-1, AR-2 on the draft article (Q10) | | in 2.5 |

Precedents: ENG-95985 712, RP 760, OA 282, ENG-92707 288 over three PRs (r10 §1, measured).

### 2.4 BMAD docs PR (clio, docs-only)

SPEC, ADR, two stories, test plan, tracker rows and this folder (plan section 2). Derived from the plan files, so not
costed by the line rate; spec lines are reported in section 0. Agent 2.5 / 3.5 / 5 h incl. gate 1 (full 3-lens
fan-out, 0.5-1 h of it).

### 2.5 Line-independent agent work

| Item | Agent h low / likely / high | Note |
|---|---|---|
| EM0-EM8, EM10-EM12 (table below) | 6.0 / 8.25 / 12.5 | the 12 probes as sized (EM9 spent); EM0-EM4 and the designer halves of EM7, EM8, EM10, EM11 ran on 2026-11-03 (status column below), not taken out (0.1); batched: EM2-EM4 in one Chrome session, EM6/EM7/EM12 in one probe process |
| Stand cleanup | 0.75 / 0.9 / 1 | every `UsrWsc*` artefact, about 20 deletes, one at a time on the owner's yes, never by HTTP DELETE ([measurements §6](eng-92710-call-web-service-element-measurements.md), session 5) |
| Cut cycles, 5-6 cuts (4-8) | 1.5 / 2 / 3 | claim after checking the tags of both repos, restamp, tag, rebundle with pins, rebuild clio, install on the owner's yes, `list-packages`, one probe |
| Stand e2e and install-and-probe runs, 2-3 | 1 / 2 / 3 | the ProcessDesigner e2e category never runs in CI: net10 + net8 on stand `Creatio`, Ignored = 0; schema writes strictly sequential |
| BMAD docs PR | 2.5 / 3.5 / 5 | 2.4 |
| KB fact-check | 1 / 1.5 / 2 | KB-6 |
| Agent scenario runs AR-1, AR-2 | 0.5 / 0.5 / 0.5 | KB-7; Q10 addition (decisions Q10, about +0.5 agent h; a point figure, so the same at all three ends) |
| Day 0 (plan steps 0.2-0.4): Jira text draft (Q18), claim comment draft, drift check of the three repos | 0.25 / 0.5 / 0.75 | each write waits for the owner's yes |
| **Total** | **13.5 / 19.15 / 27.75** | was 13.5 / 19.4 / 28.75: minus EM9 0.5 / 0.75 / 1.5, plus AR 0.5 / 0.5 / 0.5 (0.1) |

EM campaign per probe (the [measurements file](eng-92710-call-web-service-element-measurements.md) owns the content):

| EM | What | Agent h low / likely / high | Settles | Status 2026-11-10 |
|---|---|---|---|---|
| EM0 | read-only baseline: four features, versions | 0.25 / 0.25 / 0.5 | Q7, Q13 preconditions | **done** (M-22) |
| EM1 | the three test services in Chrome (plan 0.7), base URI = the stand root (EP option E4, Q17): `UsrWscEchoService` (nested POST body, 3-level response, header with a non-secret default), `UsrWscDriftService` (EM8, EM11), `UsrWscDeadService` (EM7); every parameter code `UsrWsc...` (Studio refuses a code without `Usr`) | 1.5 / 2 / 3 | test services for EM2-EM12 | **done** (M-19, M-23) |
| EM2 | designer capture A, the AC5 oracle (MetaData + resources) | 1 / 1.25 / 2 | Q3, Q4, Q20 | S1 **done** (M-24); S2-S4 (SOAP) **done** 2026-11-05 (M-32..M-35); step 9 read-back **spent** 2026-11-09 (M-36); not repeated on the shipping build (the later changes touch only refusal texts and the validator) |
| EM3 | capture B: response into a process Collection (mirror + hand-made) | 0.5 / 0.75 / 1.25 | Q6 | **done**: route (a) (M-15), route (b) on `UsrWscDriftService.UsrWscOrders2` (M-25) |
| EM4 | capture C: method and service change on an existing element | 0.25 / 0.5 / 0.75 | Q11 | **done** (M-20, M-26, M-27) |
| EM5 | STOP gate: designer opens and re-saves a builder-made element (BK15 on/off, Success L8), diff; the builder modify of step 5 runs on a designer copy `UsrWscEm5OracleCopy`, `UsrWscEm2CaptureA` is only read; re-run on the final cut | 0.5 / 0.75 / 1.25 | Q3, Q4 residual risk | **spent**: passed on the experimental cut 2026-11-04 and in the final-cut campaign 2026-11-09 (M-37; steps 4, 5 not run); not repeated on the shipping build (the later changes touch only refusal texts and the validator) |
| EM6 | run time: result params, StatusCode, Success; under E4 no request bytes and no `$.orders` response read unless the owner adds a JSON source | 0.75 / 1.25 / 2 | Q5, Q6, AC3, AC4 | **spent** 2026-11-09, reduced by E4 (M-38; EM6-B M-39); re-run on the shipping build 2026-11-10 (M-55) |
| EM7 | run time ignoreErrors true vs false on a failing call | 0.25 / 0.5 / 0.75 | Q7 | designer half **done** (M-28); builder half **spent** 2026-11-09 (M-40); re-run on the shipping build 2026-11-10 (M-56) |
| EM8 | modify latent loss on a disposable copy (g4 recipe) | 0.5 / 0.75 / 1.25 | Q12 | current-builder, service-save and designer halves **done** (M-18, M-29..M-31); step 8 **spent** 2026-11-09 (M-41), re-measured on the claimed cut (refused with N7, M-51, M-52) |
| EM9 | compile floor against 8.3.1 / 8.1.3 core binaries, local | spent (was 0.5 / 0.75 / 1.5) | Q7, Q13: outcome (a), IX4 written directly | **done** (decisions Q7) |
| EM10 | cross-package service: designer vs builder `SysPackageDependency` | 0.25 / 0.5 / 1 | Q9 notice | designer half done (decisions Q9); builder half **spent** 2026-11-09 (M-42); re-run on the shipping build 2026-11-10 (M-57) |
| EM11 | wrong-case method through the platform | 0.1 / 0.25 / 0.5 | Q9 canonicalisation | designer half done (decisions Q9); builder half **spent** 2026-11-09 (M-43; the refusal lists codes without captions, F-3); re-run on the shipping build 2026-11-10 (M-57) |
| EM12 | nested array-in-array on 10.1.37 | 0.25 / 0.4 / 0.75 | Q13 notice | run half not measurable under E4 (Q17); builder notice step **spent** 2026-11-09 (M-44); re-run on the shipping build 2026-11-10 (M-57) |
| | Sum before batching | 6.1 / 9.15 / 15.0 | | was 6.6 / 9.9 / 16.5, minus EM9 |
| | After batching (EM2-EM4 one session; EM6/EM7/EM12 one probe process) | **6.0 / 8.25 / 12.5** | | was 6.5 / 9 / 14, minus EM9 (batching never covered EM9) |

ENG-95985 ran 14 probes with two designer captures. This campaign planned 13 and is costed as planned, above
ENG-95985's estimated 6-12 h at the likely end. On 2026-11-03 EM9, EM0-EM4 (the three test services and the three
captures) and the designer halves of EM7, EM8, EM10 and EM11 ran. EM5 ran on the experimental cut on 2026-11-04; the
rest (EM2's read-back, EM5's final run, EM6 and EM6-B, the builder halves of EM7, EM8, EM10, EM11 and EM12) was spent in
the final-cut campaign on 2026-11-09 (M-36..M-46), and EM8 step 8 was re-measured on the claimed cut (M-51, M-52).
The gate is met 2026-11-10: EM6, EM6-B, EM7, EM8 step 8, EM10, EM11, EM12 and KB-7 AR-1 re-run on the shipping build (M-53..M-58); EM5 not repeated (the later changes touch only refusal texts and the validator); not sized here; the spent part is
not taken out of the figures (0.1).

### 2.6 Totals and arithmetic

| Bucket | Lines low / likely / high | Agent h low / likely / high |
|---|---|---|
| Package (2.1) | 8,100 / 12,750 / 19,700 | 6.5 / 14.0 / 29.6 |
| clio (2.2) | 1,800 / 3,000 / 4,450 | 1.4 / 3.3 / 6.7 |
| KB (2.3) | 450 / 650 / 900 | 0.4 / 0.7 / 1.4 |
| **Code subtotal** | **10,350 / 16,400 / 25,050** | **8.3 / 18.0 / 37.6** (10.35 x 0.8; 16.4 x 1.1; 25.05 x 1.5) |
| Agreed-decision adjustment (0.1), not re-sized by line: Q7 guard dropped -0.5 / -0.75 / -1; Q10 texts and tests +0.5 / +0.5 / +0.5 | | 0 / -0.25 / -0.5 |
| Line-independent (2.5, incl. BMAD 2.4, stand cleanup, AR-1 / AR-2) | | 13.5 / 19.15 / 27.75 |
| **Agent total** | | **21.8 / 36.9 / 64.85 -> ~37 h (22-65)** (8.3 + 0 + 13.5; 18.0 - 0.25 + 19.15; 37.6 - 0.5 + 27.75) |

The code-only rate already holds the review rounds of its calibration feature (ENG-95985's 30.5 h included them), so
review fixes are not added again; their lines are row G.

Triangulation (all inferred):

| Method | Result | Reading |
|---|---|---|
| Bottom-up (this section) | 36.9 h | headline |
| Analog sum: ENG-92707 (schema-sync half) 22.6 h + composite half ~10 h (g3 §5) | 32.6 h | before the BMAD docs PR and a measurement campaign heavier than ENG-92707's |
| All-in October rate x sized lines: 16.4k x 1.5 (median) / 1.65 (ENG-95985) / 2.3 (top) | 24.6 / 27.1 / 37.7 h | lower because this ticket's line-independent share (13 probes, a self-made service, three captures, three run-time probes) is larger than the rate's features carried |
| Per-feature band 11-35 h (r10-C4) | top of the band | consistent with r10-C14 (2): the platform provider builds only flat params, so the low-end condition of r10's 30-45 h range (platform generates the parameters) is not met |
| r10 §4.1 inference | 30-45 h | the likely value sits inside |

Split in time: about 60% before review starts (day 0, captures, slices A-D, the EM campaign, KB and clio drafts),
about 40% of the code subtotal (~7 h) after it: review fixes, one re-cut, rebundle, e2e re-run, gate 3 re-runs.
Serial agent sessions at 7-9 h a day are 4-5 days; KB and clio drafts run beside the package, so the agent is
co-critical with the stand gates here, not idle (ENG-95985: 30.5 h over 4.95 calendar days).

## 3. Human time that does not compress

| Item | Owner h low / likely / high | Note |
|---|---|---|
| Decisions Q1-Q20: read the file, answer, record | spent (was 1.5 / 2.25 / 3) | all twenty agreed on 2026-11-03, one at a time; out of the totals (0.1) |
| Q18 Jira description text: read the draft, say yes | 0.2 / 0.3 / 0.4 | the lead edits only after that yes; a HeroCoders checklist is not touched through the API |
| Chrome capture sessions: log in, watch EM1-EM5 and the session-2 designer halves | 0.25 / 0.5 / 0.75 | the lead drives Claude in Chrome on stand `Creatio` (Q17); partly spent on 2026-11-03 (EM1 in part, EM3, EM4, EM10, EM11 halves, the Q4 probe), row not split (0.1) |
| Stand-write approvals | 0.6 / 0.9 / 1.4 | by [measurements §6](eng-92710-call-web-service-element-measurements.md) session: 1 EM1 (extending `UsrWscEchoService`, two more services; done 2026-11-03) and the EM2-EM4 saves; 2 (plan 0.9, day 0 or 1) EM7's designer half (the EM8, EM10 and EM11 halves ran on 2026-11-03; EM12's is not measurable under E4); 3 the EM5 writes incl. the designer copy `UsrWscEm5OracleCopy`; 4 the builder halves of EM6-EM8, EM10-EM12; 5 the EM2/EM5 re-run; one yes per write; row not split for the spent part (0.1) |
| Cleanup approvals | 0.1 / 0.2 / 0.3 | about 20 deletes of every `UsrWsc*` artefact, one yes each (measurements §6, session 5) |
| Install approvals, 5-6 cuts (4-8) | 0.4 / 0.6 / 0.9 | each install restarts stand `Creatio` for every user |
| Reviewing the AI's diffs (0.25 / 0.35 / 0.5 h per 1k lines) | 2.6 / 5.7 / 12.5 | 10.35k / 16.4k / 25.05k lines; above the measured in-transcript 0.2-0.45 h/1k because diff review happens mostly in the browser |
| BMAD docs PR review | 0.5 / 1 / 1.5 | SPEC, ADR, two stories, test plan, tracker rows |
| Review rounds with other reviewers | 1 / 1.75 / 2.5 | reviewers only on the owner's ask, per PR; findings read in review bodies too |
| Merges | 0.3 / 0.5 / 0.8 | BMAD first; then package (tag verified on the remote) -> KB (publishes) -> clio; merges are human |
| Claim-comment edits, tag pushes, branch pushes | 0.3 / 0.5 / 0.7 | each on the owner's explicit yes, one step at a time |
| **Owner total, remaining** | **6.25 / 11.95 / 21.75** | ~1.5 working days of attention spread over ~6 working days |

Low: 0.2 + 0.25 + 0.6 + 0.1 + 0.4 + 2.6 + 0.5 + 1 + 0.3 + 0.3 = 6.25. Likely: 0.3 + 0.5 + 0.9 + 0.2 + 0.6 + 5.7 +
1 + 1.75 + 0.5 + 0.5 = 11.95. High: 0.4 + 0.75 + 1.4 + 0.3 + 0.9 + 12.5 + 1.5 + 2.5 + 0.8 + 0.7 = 21.75. With the
spent decisions row the whole-feature figure was 7.75 / 14.2 / 24.75.

Against the measurements: the in-transcript share of owner time was 6.9 h on ENG-95985 and 5.3 h on ENG-92707
(r10 §0); the rest (browser review, merges, Jira by hand, approvals given outside the chat) is not measurable from
transcripts. r10 inferred 8-15 h for this ticket; the likely whole-feature 14.2 h (11.95 h remaining) is at its top
because three designer captures, a self-made service and a designer re-save gate each need a login and a yes per
write.

## 4. PRs and cuts

### 4.1 PRs: 4 planned + 0-3 follow-ups = 4-7, likely 5

| # | Repo | PR | Carries | Gate / order |
|---|---|---|---|---|
| 1 | clio | BMAD docs, `ENG-92710 Call web service element` | SPEC, ADR, stories 1-2, test plan, tracker rows (`in-progress`), this folder | gate 1 full 3-lens; pushed and opened on the owner's word; merges before PR 2 leaves draft (Q19) |
| 2 | crt-process-builder (GHE) | package feature | slices A-D, parity suite, docs; one restamp per review round inside the PR | gate 1 full 3-lens (shared sub-process code P-A4, the build hook P-B5, Q16 security); ready after gate 3; merges first, tag on the remote |
| 3 | clio-knowledge | guidance | KB-1..KB-6, `libraryVersion` above master and every open branch claim | after the final cut (a real floor); merges second (publishes) |
| 4 | clio | feature | CL-1..CL-10 | draft beside the package; leaves draft only after the KB generation is published and re-pinned; merges last |
| 5 | crt-process-builder + clio | restamp-only + rebundle-only | a new cut from the merge commit | only if a foreign cut lands on `main` after approval (ENG-103934 Process file element: describe misreports an item-only Files.File binding on the Process parameter variant, precedent) |
| 6 | clio | curated re-pin | `curated-knowledge-names.json` | only if the KB generation publishes after PR 4 is approved (no push after approval) |
| 7 | crt-process-builder or clio | follow-up fix | review findings deferred past merge | File-family and ENG-95985 precedents |

ENG-92707, the schema-sync analog, ended with 7 merged PRs (r10-C8); ENG-95985 with 4. The story `done` flip rides in
the next clio PR that touches the tracker.

### 4.2 Cuts: 5-6 likely, 4-8 for a settled design

| Cut | Purpose | Usual fate |
|---|---|---|
| Experimental (slice B, stand-only stamp; also claimed in the claim comment and marked there "experimental, stand-only, superseded", the ENG-95985 .126 precedent, verdict r10-C14) | EM5 STOP gate on stand `Creatio` | superseded |
| Mid (after gate 1, slices A-D) | EM6, EM7, EM8, EM10, EM11, EM12; KB and clio drafts; first e2e run | superseded, or final if gate 3 changes nothing |
| Final | EM2/EM5 re-run against the final archive, e2e with Ignored = 0, package and KB PRs ready | shipped |
| Review-round re-cut, 1-2 | fixes; a GHE push dismisses approvals | shipped instead of the final |
| Foreign-cut re-cut | another branch's cut lands on `main` | shipped via PR 5 |

Every number is claimed in the claim comment on ENG-92710 after checking the tags of both repositories (Q19); plan
documents never project one. The external-schema class can run far above this: ENG-92707 left 27 descriptor versions
on its branch and 19 tags; if review keeps finding scope errors here, 10-25 cuts is the realistic band (section 6,
row 5). No tag is pushed before the late measurements of that cut have run (the ENG-95985 burned-tag lesson, r10 §5).

## 5. Calendar and critical path

Critical path (the plan's section 0): answers (in on 2026-11-03) -> day 0 (Jira text, claim comment, drift check,
EM0; EM9 done) -> EM1 ->
EM2/EM3/EM4 captures (measurements session 1, plan 0.8; EM2 is the oracle of P-B3/P-B4) -> slices A + B (measurements
session 2, plan 0.9, runs beside slice A, off the path) -> experimental cut -> EM5 STOP gate -> slices C + D, gate 1, mid cut -> EM6-EM12
-> final cut, EM2/EM5 re-run -> KB final, clio draft final -> package + KB review -> merges package, KB -> KB generation
published -> clio re-pin, gate 3, review -> clio merge.

| # | Step | Depends on | Runs in parallel | On-path working days low / likely / high |
|---|---|---|---|---|
| 1 | Day 0 (Q18 text, claim comment, drift check, EM0); the answers and EM9 are done (2026-11-03); the range is kept as sized, not re-sized for the answers being in | - | - | 0 / 0.25 / 1 |
| 2 | Rest of EM1 (extend `UsrWscEchoService`, two more services), then the remaining EM2/EM3/EM4 captures in Chrome (owner login, a yes per write) | 1 | BMAD docs PR drafted, gate 1, opened on the owner's word | 0.25 / 0.5 / 1 |
| 3 | Slices A + B with unit tests; shaper parity against the corpus pairs and EM2 | 2 (EM2 is the oracle of P-B3/P-B4) | measurements session 2 (plan 0.9): EM7's designer half (the EM8, EM10 and EM11 halves ran on 2026-11-03), owner's yes per write; optional, day 0 or 1 | 0.75 / 1 / 2 |
| 4 | Experimental cut + install | 3; owner's yes; site restart | | 0.1 / 0.25 / 0.5 |
| 5 | **EM5 STOP gate** (designer re-save diff) | 4; owner login | | 0.1 / 0.25 / 0.5 |
| 6 | Slices C + D, gate 1, mid cut claimed, install | 5 passed | KB and clio drafted once the contract is frozen; BMAD PR merged | 0.75 / 1.25 / 2 |
| 7 | EM6, EM7, EM8, EM10, EM11, EM12 on that cut | 6 | | 0.25 / 0.5 / 1 |
| 8 | Final cut, EM2/EM5 re-run, KB final, clio draft final (rebundle, floors, e2e Ignored = 0) | 7 | KB and clio in parallel | 0.25 / 0.5 / 1 |
| 9 | Gate 3 package + KB; reviewers on the owner's ask; one round; possible re-cut | 8 | | 0.5 / 0.75 / 2 |
| 10 | Merges package -> KB; the KB generation is published on merge | 9 | | 0 / 0.25 / 0.5 |
| 11 | clio: curated re-pin, gate 3, ready, reviewers on the owner's ask, merge; follow-ups | 10 | | 0.25 / 0.5 / 1.25 |
| | **Total** | | | **3.2 / 6.0 / 12.75** |

Low: 0 + 0.25 + 0.75 + 0.1 + 0.1 + 0.75 + 0.25 + 0.25 + 0.5 + 0 + 0.25 = 3.2. Likely: 0.25 + 0.5 + 1 + 0.25 + 0.25 +
1.25 + 0.5 + 0.5 + 0.75 + 0.25 + 0.5 = 6.0. High: 1 + 1 + 2 + 0.5 + 0.5 + 2 + 1 + 1 + 2 + 0.5 + 1.25 = 12.75.

Illustration (holidays ignored): the answers came on Tue 2026-11-03; counting from Wed 11-04,
put the last merge on Mon 11-09 (low), Wed 11-11 (likely), Fri 11-20 (high). The long poles are owner latency (steps
1, 2, 5), the stand approvals (4, 6, 7, 8), the EM5 gate and the clio step that waits for the KB publication (11).
Review is not the long pole when the design holds (ENG-95985: ready -> merged in ~1 working day); it becomes one when
review finds scope errors (section 6, row 5).

## 6. What moves the estimate

### 6.1 Up

| # | Event | Mechanism | Agent h | Owner h | Calendar | Cuts / PRs |
|---|---|---|---|---|---|---|
| 1 | **ENG-92730 scope pulled in** (Q6 agreed A on 2026-11-03; the row prices a later pull-in) | relax `CloneItemProperties` for WS outputs (copy Tag verbatim), a declarable ObjectList, a third declared collection level: the composite half grows from ~10 h to ~25 h (g3 §5); touches the shared parameter service (full 3-lens), and the `process-parameters` article is already over its 19,000 B reply (~19,129 B, r5) | **+15** | +2-4 | +1.5-3 wd | +1-2 cuts |
| 2 | Discovery tool (Q10 agreed A; the row prices a later tool) | clio `list-web-services` / `describe-web-service` over `WebServiceManager` with secret stripping, a `KnownRoute`, docs, e2e on both frameworks, tool-index ceiling re-pins, a guidance article; g5 put it at +1-1.5 days in the Jira unit; a package operation instead also moves the security pins 8 -> 9; figure as in decisions Q10 option B | +8-12 | +1-2 | +0.5-1.5 wd | +0-1 PR |
| 3 | Refusal instead of a notice on drift (Q12 agreed A; option B is a follow-up) | a `ConfirmLayoutChange`-style confirm needs the pre-sync stored parameter list, i.e. a raw `SysSchema.MetaData` read (g4: helpers are candidates, unverified); a new contract key against modify's 3 B description headroom; figure as in decisions Q12 option B | +3-5 | +0.5 | +0.5 wd | |
| 4 | Platform floor work (EM9 / EM12) | settled 2026-11-03: EM9 gave outcome (a) (decisions Q7), so IX4 is written directly and no floor is introduced; folded into 0.1. Left without a figure of its own: CI compiles against 8.3.4 while the local core-bin is 10.1.37 (g2, measured), so a post-8.3.4 member is green locally and red in CI (plan §10). EM12 changes notice wording only | 0 | 0 | 0 | |
| 5 | **Review rounds typical of external-schema elements** | ENG-92707: 23+ numbered review rounds, 27 descriptor versions, 19 tags, 7 PRs (r10-C7, C8); the rule from there: narrow the claim instead of re-fixing | +8-15 | +2-5 | +2-3 wd | cuts to 10-25, +1-2 PRs |
| 6 | EM5 STOP gate fails (did not happen: passed 2026-11-04 and 2026-11-09, M-37) | the designer rewrites a builder-made element, duplicates BK15 rows or drops Success L8; Q3/Q4 re-opened, shaper and BK15 writer reworked against a second capture; if no stored shape survives a re-save, stop and re-plan | +4-8 | +1-2 | +1-3 wd | +1 cut |
| 7 | No on-stand echo endpoint (Q17) | settled 2026-11-03: EP option E4 (base URI = the stand root; E1-E3 not chosen). EM6's request bytes and `$.orders` response read and EM12's run half rest on the core unit tests and the ENG-55920 comment; no endpoint is written. A JSON source the owner adds later for EM6's response read is not sized here | 0 | 0 | 0 | |
| 8 | Description swap disputed | modify has 3 B headroom (r5-C3, measured); a reviewer refuses the swapped text; fallback is a ceiling raise, which reopens Q15 | +0.5-1.5 | +0.3-0.5 | +0.5-1 wd | one more clio round |
| 9 | Foreign cut on `main` after approval, or a KB `libraryVersion` collision | re-cut (a GHE push dismisses approvals) or restamp-only + rebundle-only PRs; re-bump, size-gate re-run, curated re-pin | +1-2 | +0.3-0.8 | +0.5-1 wd | +1 cut, +1-2 PRs |
| 10 | Stand contention | each install restarts stand `Creatio` for everyone; a reviewer whose clio bundles a newer cut than the stand is refused by convergence until the stand is brought up | | +0.2-0.5 | +0.5 wd per clash | |

Outcomes that do NOT move the estimate: EM0, the builder halves of EM10 and EM11 (one notice or one guidance line
each; their designer halves ran on 2026-11-03 and moved nothing), EM12 (notice wording), EM4's method change
(replace semantics is costed either way), Q20's SOAP branch (D, 2026-11-05: built like REST with N10, after the
SOAP capture showed the same shape).

### 6.2 Down

| Event | Effect |
|---|---|
| All twenty answers on one day | happened 2026-11-03; step 1 is left at its sized range (0.1) |
| EM1-EM4 in one Chrome session (done on day 0, 2026-11-03) | step 2 at 0.25 wd |
| Q4 "skip BK15" | not taken (Q4 agreed A, 2026-11-03) |
| EM9 outcome (a) | happened 2026-11-03; folded into 0.1 (-0.5 / -0.75 / -1 agent h); the clio floor wording (~0.25 h) only if a declared floor is stated, inside the same figure |
| No foreign cut, reviewers requested the morning the PRs are ready, KB published within the hour (ENG-95985 tail 32 min) | steps 9-11 at their low values |
| The ENG-95985 pattern repeats (build came in at 0.73 of the likely estimate) | ~27 agent h (0.73 x 36.9), ~9 owner h (0.73 x 11.95), ~4 wd |

## 7. Against the Jira "~3 days"

The Jira description says "~3 days"; the Confluence task list 4758143001 (v43) row "22 - Call web service" has AI Est.
3 d. That column is **working days for one person, with the AI writing the code**, and the page says each estimate is
dominated by review, unit/e2e tests and QA (r10-C14 (3), correcting r10's "developer days"). It is therefore
comparable with this estimate's calendar and, in part, with the owner's hours, not with agent hours. The page's own
caveat: "a hard-to-capture element (e.g. Web service composite bodies...) can swing ±50%" (r1), i.e. 1.5-4.5 d.

| Aspect | Jira / Confluence | This estimate | Effect |
|---|---|---|---|
| Unit | working days, 1 person, AI writes code | agent hours + owner hours + calendar | Q18 (agreed): the corrected description states the unit |
| Calendar | 3 d (1.5-4.5 with the caveat) | 6 wd likely (3.2-12.75) | 2.0x likely; the low end is inside the caveat, the likely value is above its 4.5 d top, the high end is 4.25x |
| Human effort | 3 days of one person | ~12 owner h remaining (~1.5 days of attention; the decisions are spent), plus ~37 agent h | the person-days fall below 3; the calendar does not, because it is set by captures, stand gates, review and the KB-before-clio order |
| Parameter generation | implied "let the platform sync" (r1 §5) | a package-owned port of the client algorithm (Q3): the platform provider builds only flat top-level params, no L18, no ItemValue, no result params, no BK15 (r10-C14 (2), r4-C4) | up: slice A's shaper and slice C's parity suite |
| Composite types | "Task 5 types" | Task 5 is closed; composite types moved to Task 37 = ENG-92730, still To Do; v1 is declared-only (Q6); r1 notes the CompositeObject slice "is not in the 3-day estimate" | the ENG-92730 pull-in is priced separately (+15 h, section 6 row 1) |
| Drift and latent loss | not seen | every design-time load re-syncs by Name and prunes tagged params of an unresolved method (g4); describe issues and a modify notice (Q12), EM8 | up, inside slice D |
| Platform floors | not seen | IX4 exists from core 8.3.2.3590 (g2); before the ENG-55920 fix (absent in 10.1.37, stand `Creatio`) an inner object array whose container has no source of its own while its leaves are mapped is sent without that inner list (null), a mapped inner container is sent, and that such a process saves without error is not measured (verdict r1-C7; Q13, EM9, EM12) | IX4 written directly (EM9, 2026-11-03: the package already needs about 8.3.2, Q7); up: a notice (N5) and EM12, whose run half is not measurable on the stand under E4 |
| Resolution and secrets | not seen | case-insensitive resolution with canonical spelling and enumerating refusals, never a default (Q9, Q16); g5 put this at +0.5 day in the Jira unit; registered services only, registration is ENG-105475 (Q10, Q18) | up, inside slice A |
| Evidence | one capture | 13 probes incl. a self-made REST service (no Body-parameter service exists on the stand, r9-S4), three designer captures, a designer re-save gate, run-time probes as far as E4 lets them show; EM9 and several day-0 halves done on 2026-11-03 | up: ~9.15 agent h remaining (8.25 for the EMs, 0.9 for cleanup), ~2 owner h of stand work |
| History of the unit | | ENG-92707: AI Est. 2.5 d -> ~4 working days (5.07 calendar days); ENG-92719 "File processing element" File processing: AI Est. 2 d -> about two weeks across four tickets (r10-C14 (3), r1) | the AI Est. column has run 1.6x and more on elements that sync from an external schema |

Net: the human effort fits inside the 3 days; the calendar does not. A likely 6 working days from the owner's answers
to the last merge is twice the Jira figure and above its ±50% caveat, for the same reasons the closest analog ran
1.6x: an external schema whose sync the builder must reproduce, a designer capture that decides parity, and review
rounds on an element whose platform behaviour is traced before it is measured. Pulling the ENG-92730 scope in or
adding a discovery tool (section 6, rows 1-2) widens the gap further; both are out of v1 under the agreed answers.
