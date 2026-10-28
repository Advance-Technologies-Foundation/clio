# ENG-95985 Send email attachments: estimate

Written 2026-10-28.

Basis: crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125), clio `origin/master` `358e9db52`,
clio-knowledge `origin/master` `534e913`; the decisions file (Q1-Q20, all agreed by the owner 2026-10-28; Q9, Q16 and
Q17 changed from the first recommendation, Q20 agreed for both Process file variants), the design file (C1-C10, K1-K12,
K12 review only and not costed, guidance), measurement ids EM0-EM15 (measurements file; EM6 and EM12 dropped), and the calibration in `R/r9-calibration.md`
(git and PR-API history of six delivered features), checked against `R/r10-critic.md`. Jira ENG-95985 "Send email
attachments" refinement comment 493117 (2026-09-08) read on 2026-10-28.

Paths: `PB/` = `packages/CrtProcessBuilder/Files/src/cs/`, `PBT/` = `tests/UnitTests/CrtProcessBuilder.Tests/` (both relative to the
crt-process-builder repository root), `CLIO/` = the clio repository root, `KB/` = the clio-knowledge repository root,
`UT/` = the platform `UnitTests` repository root, `PS/` = the PackageStore repository root, `R/` = the local research
notes (not in the repository; see README).

Model (owner rule): an AI agent writes the code, the tests, the docs and the guidance. The estimate gives (1) agent wall
time, (2) human time that does not compress, (3) calendar time set by review latency and gate order. The headline is
the agent number. No future CrtProcessBuilder cut is named; versions below are history only.

## 0. Headline

| Quantity | Likely | Range | Note |
|---|---|---|---|
| **Agent wall time (AI writes)** | **~42 h** | 23-68 h | ~29 h code by the calibrated rate + ~16 h line-independent work (EM campaign, BMAD docs, cut cycles, stand e2e runs), minus the ~3 h of it the rate already contains (subtracted in every column) |
| Owner time (does not compress) | ~15 h | 11.2-22.3 h | decisions (done 2026-10-28), designer captures, mail sending on stand `Creatio`, stand approvals, diff review, review rounds, merges |
| Calendar, owner's answers (2026-10-28) -> last merge | 7 working days | 4-12 | gate-ordered; the clio PR leaves draft only after the KB generation is published; adverse events in section 6 add 1-3 days each |
| Authored lines (all repos, excl. spec folder and verbatim capture data) | ~9.8k | 6.9-12.7k | PB src 1.5-2.5k, PB tests 3.5-6.2k |
| PRs | 5-6 | 5-7 | 4 planned + 1-3 follow-ups |
| CrtProcessBuilder cuts | 4 | 3-5 | 1-2 superseded |

## 1. Calibration (delivered features)

Source: `R/r9-calibration.md` §1.1-1.5 (git `--shortstat` of each merge, GHE/GitHub PR events). Lines are added lines.

| Feature | PB src | PB tests | tests/src | clio | kb | pkg other | All-repo | Calendar (first commit -> last merge) | PRs pkg/clio/kb | Cuts | Package ready -> merge | Merge tail |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ENG-92706 Send email element (custom message) | 1,667 | 1,989 | 1.19 | 1,182 | 83 | 262 | ~5.2k | 7.6 d (7 wd) | 1/1/1 | 1 (restamped on `main` after merge) | ~6.7 d from review request (Aug convention) | 72 min |
| ENG-95986 Support template message mode in the Send Email element | 925 | 1,201 | 1.30 | 1,049 | 283 | 163 | ~3.6k | 2.0 d (3 wd) | 1/1/1 | 5 + 1 post-merge re-cut, 1 number collision | 6.9 h | n/a (integration branch, reverse order) |
| ENG-95984 File process parameter type | 2,706 | 5,533 | 2.04 | 1,330 + 120 story-done (+1,873 spec PR) | 572 | 721 | ~11.0k | ~1.5 d code | 1/3/1 | 3 (1 burned, 1 superseded) | 2.9 h | 37 min |
| ENG-96505 Element readiness and object attachments mode | 6,741 (+52 follow-up) | 8,576 (+164) | 1.27 | 2,916 | 282 | 606 | ~19.3k | 4.7 d (3 wd + weekend) | 2/2/1 | 4 (3 burned/superseded) + 1 follow-up | 1.2 h (+0.2 h follow-up) | 1.2 min |
| ENG-96506 Generated report + process parameter modes | 5,588 | 4,250 .cs (8,405 all) | 0.76 / 1.50 | 2,070 (+693 docs) | 760 | 306 | ~17.1k | 0.8 d | 1/1-2/1 | 5 (4 superseded) | 7.9 h, 2 approval dismissals | 14 min |
| ENG-103937 Process file element: SysFile attachment storage | 1,262 (+56 follow-up) | 3,157 (+271) | 2.50 | 826 | 309 | 252 | ~6.1k | 0.8 d | 2/2/1 | 5 (4 superseded) + 2 follow-up | 0.8 h (+2.6 h follow-up) | 37 min |
| Comparator ENG-92707 Sub-process element: selection + parameter sync | | | | | | | 7.9k | 5.1 d | | | | agent 22.6 h, owner 5.3 h (Jira ENG-92719 "File processing element" comment 527598) |

"pkg other" = added lines of the package merges outside `src/cs` and the tests' `.cs` files (docs, descriptor,
non-`.cs` fixtures; for ENG-96506 the 4,155 non-`.cs` test lines sit in "8,405 all" instead), measured 2026-10-28
with `git diff --numstat` of the same merge commits. All-repo is the row's sum without the bracketed spec and docs
PRs.

Constants used below:

| Constant | Value | Evidence |
|---|---|---|
| Agent wall time per 1k added lines (prod + test, all repos, 3-10k features) | 2.0-3.5 h; 2.75 h central | `CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-plan.md:520-540` [prior-measured]; ENG-92707 = 2.86 h/1k |
| Owner engaged time per 1k lines | 0.7 h, plus designer probes | same; ENG-92707 = 0.67 h/1k |
| Share of agent time after review starts | ~40% | same |
| Review latency once ready (File family) | 0.2-7.9 h; budget one dismissal round | r9 §1.3 (#100 dismissed twice) |
| Merge tail package -> last of clio/kb | 1-72 min | r9 §1.4 |
| Cuts per ticket | 3-5; review rounds ≈ superseded cuts | r9 §1.1, §1.3 |
| PR count vs plan | File family planned 14, delivered 19: follow-ups and docs-only PRs were not planned | r9 §1.5 |
| Size lesson | earlier estimates matched SOURCE lines; tests added 1-2.5x on top | r9 §1.5 [inferred there] |

The last row is why this estimate sizes tests separately per work package. The planned ENG-95985 tests/src ratio is
2.3-2.4, at the top of the family (2.04 and 2.50). Reason: every slot-shape error saves green and fails only at run
time (`R/r10-critic.md` §7), so the unit suite pins each invariant of design §3 on a schema with a real UId (the
existing `SendEmailApplierTests.CreateSchema()` builds an empty-UId schema, `PBT/SendEmailApplierTests.cs:48`; the new
fixtures use `UserConnection.CreateSchema()`, `PBT/FileProcessingTestSupport.cs:222`), and parity uses three patterns
(`R/r8-testing.md` §3). Repository test style (AAA comments, a `because` on every assertion, a `[Description]` on every
test, LangVersion 7.3 in PBT) is priced into the test lines.

## 2. Size and effort per work package

Agent h = lines x 2.0 (low) to lines x 3.5 (high); owner h = lines x 0.7/1k (diff review only). Low/high lines are the
two ends of the scope the design allows (for example, describe issue sentences settled by EM1d, refusals added after EM8).

### 2.1 crt-process-builder (one feature PR)

| WP | Content | src | tests | Agent h | Owner h |
|---|---|---|---|---|---|
| C1 | Contracts: `SendEmailAttachmentDescriptor`, `SendEmailDescriptor.Attachments`, `DescribeSendEmailAttachment`, `DescribeSendEmailInfo.Attachments`, `setElement` doc; wire-contract rows (`PBT/EntryPoints/WebService/ProcessDesignServiceWireContractTests.cs`) | 120-200 | 60-120 | 0.4-1.1 | 0.1-0.2 |
| C2 | `FileSourcePlanner` lifted out of `PB/FileProcessing/ProcessFileProcessingBinder.cs` (326 lines; removes ~150-180); label, member path, target names, consequence sentence and the write entry as parameters (`FilesBinding.Write` writes a single file and an expression through the raw `ApplyMapping`, `:299`, `:305`; a slot takes the block entry); messages byte-identical, own commit first | 250-380 | 250-450 | 1.0-2.9 | 0.35-0.6 |
| C3 | `EmailAttachmentSlot` + `IEmailAttachmentSlotWriter` (find, create both levels with explicit stamps and `In`, remove by `Parameters.Remove`, numbering = lowest k that no parameter of the element names `Attachments<k>` (Q5 agreed; the designer scans the element's `parameters`, `PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:243-259`), designer display values on slots only, an explicit empty outer DisplayValue for reused or unbound outers if EM1/EM2 show stale keys survive) | 330-500 | 500-800 | 1.7-4.6 | 0.6-0.9 |
| C4 | `SendEmailApplier.ApplyAttachments`: plan-all-first, one-to-one replace reconcile with `IProcessDependencySnapshot.FindDependents` before each removal, the block form of `BindCollection` / `ApplyBlockMapping`, refusals incl. a multi-instance Send email (Q16 agreed: `MultiInstanceOptions` on the activity, `TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:122`; +10-20 src, +30-60 tests) and an unscoped Process file source, Object attachments AND Generated report (Q20 agreed: every source element tracked in `PB/FileProcessing/FileProcessingFilterLedger.cs`, `ObjectFileProcessingEmptyFilterRule` extended to an element a Send email slot reads whatever its action, `ReportFileProcessingEmptyFilterRule` already refuses any unscoped element, judged by `EnsureSelectingFilters` at the end of the request; the Report and end-of-request part +20-40 src, +100-200 tests, TC-U-78a, 78b), notices fired per entry of a sent list (removal notice in the write shape), `EnsureConfiguresSomething`, DI (`packages/CrtProcessBuilder/Files/src/CrtProcessBuilderApp.cs`) | 390-630 | 1,230-2,060 | 3.2-9.4 | 1.15-1.9 |
| C5 | Slot guards in `PB/Mappings/ProcessMappingService.cs`: onto a slot in `Plan` under `!fromBlock`, before `ResolveTargetParameter`, keyed on the name (so a build `mappings[]` entry onto a slot not created yet is refused too); from a slot in `ResolveStructuredSource` (covers `BindCollectionItems`); the block form of `IProcessMappingService.BindCollection`; every non-block route as TestCases, PU-42 mirror. Shared mapping service: full 3-lens review tier | 80-160 | 250-450 | 0.7-2.1 | 0.25-0.45 |
| C6 | Declaration-order refusal on build (item-level names included) + flow-reachability warning on build and modify, reusing `ProcessGraphBuilder.Traverse` (`PB/Graph/ProcessGraphBuilder.cs:2250-2270`, called at `:2228-2230`) over the schema's flows | 110-220 | 240-500 | 0.7-2.5 | 0.25-0.5 |
| C7 | Describe: `DescribeAttachments`, binding reader generalized from `ProcessFileProcessingFilesBinding.Read`, six issue shapes, build -> describe -> replay; the generic `parameters[]` projection (`PB/Describe/ProcessDescriber.cs:197-218`) leaves slots undecoded, sharing the slot predicate with C5; a multi-instance Send email reported as found with an issue (Q16) and an entry that reads an unscoped source reported with an issue (Q20, TC-U-78c): +10-20 src, +45-85 tests | 250-410 | 485-865 | 1.5-4.5 | 0.5-0.9 |
| C8 | Removed (Q9 agreed: no build-time check of `UseProcessEmailAttachments`; one troubleshooting line in the KB article, inside KB-1) | 0 | 0 | 0 | 0 |
| C9 | Comment fixes (`PB/Mappings/CollectionBindingRules.cs:164-169`, recipient numbering comment) | 10-30 | 0 | 0.1 | 0.05 |
| C10 | `docs/send-email-element-capture.md` attachments section (EM1 record) | 100-250 docs | | 0.2-0.9 | 0.05-0.2 |
| P | Parity: oracle reader extension (L1/L12/IL2 roles), parity tests (patterns A/B/C), `PROVENANCE.md`, `.gitattributes -text` line. Verbatim capture JSON (the three corpus processes of pattern A, ~11.5k lines / ~610 KB) is copied, not authored, and is not costed; EM1 is restated as rows (pattern B), its raw reads are not committed | | 400-750 | 0.8-2.6 | 0.3-0.5 |
| H | Harness churn: existing Send email suites after the applier's constructor grows, `CrtProcessBuilderAppTests` composition rows | | 80-200 | 0.2-0.7 | 0.05-0.15 |
| | **Package total** | **1,540-2,530** (+100-250 docs) | **3,495-6,195** | **10.5-31.4** | **3.65-6.35** |

### 2.2 clio (one feature PR)

| WP | Content | src | tests | Agent h | Owner h |
|---|---|---|---|---|---|
| K1 | Rebundle + four pins in `CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs`; repeated per cut | 10-25 (pins) | | in "cut cycles" (2.4) | <0.05 |
| K2 | `ProcessBlockKeyCheck` email key + entry child key set + read-back fields | 25-50 | 80-160 | 0.2-0.7 | 0.1-0.15 |
| K3 | Floors on create, modify, modify-as-new-version + literal pins | 10-25 | 10-30 | 0.1-0.2 | <0.05 |
| K4 | Tool descriptions with swaps under the 35,072 B ceiling (modify: "attachments replaces the list: to add one, send the described list plus the new one"); sizes re-read by lowering the ceiling on the branch (`CLIO/clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs:292`): the three contracts AND the seven-contract wire bundle under `MaxInlineWireReplyBytes` = 19,000 B (`:314`; its recorded 18,815 B, `:313`, is stale, decisions Q12) | 10-40 net | 30-90 | 0.6-1.5 (0.5-1.0 of it the byte re-measurement) | 0.05-0.1 |
| K5 | Prompts (`CLIO/clio/Command/McpServer/Prompts/ProcessDesigner/`) | 10-30 | 10-40 | 0.1-0.3 | <0.05 |
| K6 | Typed `Attachments` on `DescribedEmail` (`CLIO/clio/Command/ProcessModel/IProcessDescriber.cs`) | 40-90 | 60-130 | 0.2-0.8 | 0.1-0.15 |
| K7 | Read-back checks "attachments did not land" and "N attachments were removed by the replace rule" (`EmailBlockExpectation.cs`, `BlockExpectationIntent.cs`, `BlockExpectationReporter.cs`) | 100-210 | 190-380 | 0.6-2.1 | 0.2-0.4 |
| K8 | `CLIO/docs/McpCapabilityMap.md` | 10-30 docs | | 0.1 | <0.05 |
| K9 | Curated knowledge names re-pin, after the KB generation is published (the clio PR stays draft until then) | 5-20 | | 0.1 (+ waits on KB publish) | <0.05 |
| K10 | `CLIO/clio.mcp.e2e/SendEmailAttachmentsToolE2ETests.cs` + run-time helper (ActivityFile read, cleanup through a DataService batch). Precedents 1,157 and 1,874 lines | | 800-1,500 | 1.6-5.3 | 0.55-1.05 |
| K11 | Knowledge records in `CLIO/docs/knowledge/ProcessModel/` (3-6) | 60-200 docs | | 0.1-0.7 | 0.05-0.15 |
| | **clio total** | **195-445** (+85-275 docs/pins) | **1,180-2,330** | **3.7-11.8** | **1.05-2.1** |

### 2.3 clio-knowledge (one PR) and BMAD docs (one clio PR)

| WP | Content | Lines | Agent h | Owner h |
|---|---|---|---|---|
| KB-1 | New article `process-send-email-attachments` (4-6 KB; precedent `send-email-template.md` 76 lines / 7.6 KB), incl. "describe immediately before sending" and one troubleshooting line for `UseProcessEmailAttachments` (Q9) | 50-100 | | |
| KB-2 | `files.md` sentence split + the `ProcessFileGuidanceTests.cs:180` pin in the same commit | 10-25 | | |
| KB-3 | `send-email.md` scope + pointer, `element-catalog.md` row, `process-modeling.md` route line paid by a swap, `routing.md` row `name=process-send-email-attachments` | 25-55 | | |
| KB-4 | `bundle-source.json`: resource with NO `legacyUris`, `requirements.itemIds` and `resourceUris`, the `process-send-email` AND `process-modeling` entry descriptions, `libraryVersion` above master and every open branch claim (the generation `sequence` is derived from it at build time, `automation/Clio.Knowledge.Bundle.Tests/GenerationSequenceTests.cs`) | 15-30 | | |
| KB-5 | Tests: `SendEmailGuidanceTests` additions incl. retired-claim sweep, `ProcessGuideCrossReferenceTests` (routing), `GuidanceMigrationTests` (`PostMigrationGuidance` gains the name), size gates, manifest | 200-450 | | |
| KB-6 | Fact-check: refute-first agents barred from our own records, every refutation re-checked by hand | | 1-2 | |
| KB | Subtotal | 300-660 | 1.6-4.3 | 0.2-0.45 |
| BMAD | clio docs-only PR: spec, ADR, story, test plan, tracker rows (the story row as `in-progress`), the inherited done-flip of `story-eng-92719-file-processing-element-3` (`CLIO/spec/sprint-status.yaml:4919`), this folder; gate 1 is the full 3-lens fan-out (0.5-1 h of the agent time). Derived from the plan files, so not costed by the line rate | ~600-1,200 authored | 2.5-5 | in 3 |

### 2.4 Line-independent agent work

| Item | Count | Agent h | Note |
|---|---|---|---|
| EM campaign EM0-EM15 | 14 probes (EM6 and EM12 dropped, Q17 and Q9 agreed); EM4 (with EM4c: a parent process passes `OF1.ObjectFiles` into a callee's FileCollection), EM5, EM8, EM10, EM14, EM15 batched into 2-3 probe processes per cut; EM2 split: save, pre-save gate and stored bytes at the STOP gate, its describe steps in the re-run on the final cut | 6-12 | EM1 via Claude in Chrome alone is 2.25-4 h (5 rows, gap + re-add, SE2, EM1d, the one-save step that clears a collection row and reuses its number for a single File, metadata + resources restated as rows); EM7 on stand `Creatio` once the owner's mail sending works, 1-2 h (auto + custom, auto + template, the Create activity variant with `ActivityFile` rows; no second-stand install). Dropped against the earlier 7-14 h: EM6 0.5-1 h, EM12 0.25-0.75 h, the EM0 per-user reader check 0.25 h |
| Cut cycles | 3-5 | 1-3 | claim (`git ls-remote --tags` in both repos), restamp, tag the producing commit, rebundle with K1 pins, rebuild clio, install, `list-packages`, one probe |
| Stand e2e evidence runs | 2-3 | 1-3.6 | `McpE2E.ProcessDesigner` never runs on TeamCity; Ignored = 0 recorded in the PR (r9 §4.8); schema writes strictly sequential |
| BMAD docs PR | 1 | 2.5-5 | section 2.3 |

### 2.5 Totals

| Bucket | Lines | Agent h low-high | Agent h likely |
|---|---|---|---|
| Package (2.1) | 5,135-8,975 | 10.5-31.4 | 19.4 |
| clio (2.2) | 1,460-3,050 | 3.7-11.8 | 7.0 |
| KB (2.3) | 300-660 | 1.6-4.3 | 2.8 |
| **Code subtotal** | **6.9-12.7k** | **15.8-47.5** | **~29** |
| EM campaign | | 6-12 | 8.5 |
| Cut cycles + stand e2e runs | | 2-6.6 | 4 |
| BMAD docs PR | | 2.5-5 | 3.5 |
| Already inside the calibrated rate (cut and e2e work the rate's features had) | | -3 | -3 |
| **Agent total** | | **23-68** | **~42** |

Likely = lines at the central 2.75 h/1k (clio adds K4's 0.75 h byte re-measurement; KB adds 1.5 h of fact-check).
Split in time: about 60% before review starts (the slices, EM1-EM15, the rest of the package, drafts of KB and clio),
about 40% of the code subtotal (~12 h) after it: review fixes, one re-cut, rebundle, e2e re-run, gate 3 re-runs.
Serial agent sessions at 7-8 h a day would be ~6 days; KB and clio drafts run in parallel with the package, so the
agent is never the calendar's bottleneck.

Sanity check: ENG-92707 took 22.6 agent h for 7.9k lines (2.86 h/1k); ~9.8k authored lines here gives ~28 h of code,
the code subtotal above. The other ~14 h buy what ENG-92707 did not have: 14 measurements with two designer captures,
a BMAD PR and three repositories.

## 3. Human time that does not compress

| Item | Owner h | Note |
|---|---|---|
| Decisions Q1-Q20: read the file, answer, record "Agreed" | 1.75-3.25 | done 2026-10-28 in one sitting: all twenty agreed, Q9, Q16 and Q17 changed after research the owner asked for; the hours stay in the total as spent (estimated, not measured) |
| Q19 Jira AC wording, by hand | 0.3-0.5 | the HeroCoders checklist breaks on API edits |
| EM1 designer capture | 0.5-0.9 | the owner logs in, the lead drives Claude in Chrome; by hand 0.5-0.75 h (Q18); includes the clear-and-reuse save |
| EM3 designer opens the builder-made element, no-op save | 0.25-0.5 | login again; a fresh session for the `forceRemove` case |
| Install approvals, 3-5 cuts | 0.4-0.85 | no second-stand install since EM7 runs on stand `Creatio` (Q17); every install restarts the site for every user of stand `Creatio`; after a mid-review rebundle, a reviewer whose clio bundles a newer cut than the stand is refused by convergence until the stand is brought up (`CLIO/docs/agent-instructions/bundled-packages.md:172-176`) |
| Environment-change approvals | 0.25-0.5 | EM4c parent and callee processes, cleanup of probe processes, Activity and ActivityFile rows (EM6's MailboxSyncSettings record and EM12's feature change are dropped) |
| Mail sending on stand `Creatio` (Q17) | 0.5-1 | owner side: configure real mail sending on stand `Creatio` and say when the mailbox works; EM7 waits for it |
| EM7 real delivery | 0.25-0.5 | yes for the sending runs (auto + custom, auto + template, the Create activity variant), read the received mail |
| Reviewing the AI's diffs (0.7 h/1k, sections 2.1-2.3) | 4.9-8.9 | package 3.65-6.35, clio 1.05-2.1, KB 0.2-0.45 |
| BMAD docs PR review | 0.5-1.5 | spec, ADR, story, test plan, tracker, done-flip boxes |
| Review rounds with other reviewers | 1.0-2.5 | reviewers requested only on the owner's ask, per PR; findings read in review bodies too; one dismissal round budgeted |
| Merges | 0.3-0.8 | package (tag verified on the remote) -> KB (publishes) -> clio; plus 1-3 follow-up PRs; an agent never merges |
| Claim-comment edits, tag pushes, branch pushes | 0.25-0.6 | each only on the owner's explicit yes, one step at a time: a claim edit and a tag push per cut (Q13), a push per review round |
| **Owner total** | **11.2-22.3, likely ~15** | ~2 working days of attention, spread over ~7 working days |

Not on the owner's clock [inferred]: package reviewers (1-2 approvals needed) and clio/KB approvers, about 3-6 h in
total over 4-5 PRs.

The calibrated 0.7 h/1k (ENG-92707) covered diff review and its review rounds. Dropping the separate review-round and
merge rows to compare like with like gives 10-19 h; the rest is this ticket's 20 decisions, two designer captures and
the mail setup on stand `Creatio`.

## 4. PRs and cuts

### 4.1 PRs: 4 planned + 1-3 follow-ups = 5-7

| # | Repo | PR | Carries | Gate / order |
|---|---|---|---|---|
| 1 | clio | BMAD docs, `ENG-95985 Send email attachments` | spec, ADR, story, test plan, tracker rows (the story row as `in-progress`), inherited done-flip (only boxes really done), this folder | after the decisions; gate 1 full 3-lens; pushed and opened on the owner's word; merges before PR 2 leaves draft (Q14) |
| 2 | crt-process-builder (GHE) | package feature | C1-C7, C9, C10, P, H (C8 removed, Q9); one restamp per review round inside the PR | draft after slice C and gate 1 (full 3-lens: the shared mapping service changes); ready after gate 3; merges first, tag on the remote |
| 3 | clio-knowledge | guidance | KB-1..KB-6, `libraryVersion` above master and every open branch claim | after the final cut (a real floor, never `TBD`); gate 1 full 3-lens plus the KB-6 fact-check; merges second (publishes) |
| 4 | clio | feature | K1-K11; K12 as review statements in the PR body | drafted beside the package; stays draft until the KB PR merges and its generation is published; then the curated re-pin, gate 3, reviewers on the owner's ask; merges last |
| 5 | crt-process-builder + clio | restamp-only + rebundle-only (X8 variant) | a new cut from the merge commit | only if a foreign cut lands on `main` after approval (precedent ENG-103934 "Process file element: describe misreports an item-only Files.File binding on the Process parameter variant", #109 / #1872) |
| 6 | clio | curated re-pin | `curated-knowledge-names.json` | only if the KB generation publishes after PR 4 is approved (pushes after approval are avoided; precedent #1871) |
| 7 | crt-process-builder or clio | follow-up fix | review findings deferred past merge | File-family precedents #99, #102, #1846, #1861 |

The story `done` flip rides in the next clio PR that touches `CLIO/spec/sprint-status.yaml`; it is not a PR of its own.

### 4.2 Cuts: 3-5, 1-2 superseded

| Cut | Purpose | Usual fate |
|---|---|---|
| Experimental (slices A + B: C1-C3, C7 part 1, C4 complete incl. replace reconcile and refusals, C9) | STOP gate on stand `Creatio`: EM2 save, pre-save gate and stored bytes (replace included), EM3 in the designer | superseded |
| Mid (whole package before gate 3) | EM4-EM15 except EM7 (EM4c included; EM6 and EM12 dropped), KB and clio drafts, first e2e run | superseded, or final if gate 3 changes nothing |
| Final | gate 3, EM2 describe steps re-run (after P-C3), EM7 on stand `Creatio` once the owner's mail sending works (Q17), e2e with Ignored = 0, package and KB PRs ready | shipped |
| Review-round re-cut | one round of fixes; a package push dismisses approvals | shipped instead of the final |
| Foreign-cut re-cut | another branch's cut lands on `main` | shipped via PR 5 |

Every number is claimed in a claim comment on ENG-95985 after `git ls-remote --tags` in both repositories (Q13);
plan documents never project one. Each claim-comment edit and each tag push happens only on the owner's yes. Rebundle when behaviour changes and once at the end, not per commit
(`CLIO/docs/agent-instructions/bundled-packages.md:160-175`).

## 5. Calendar and critical path

Critical path: decisions (done 2026-10-28) -> EM1 -> package slices A + B -> experimental cut -> EM2/EM3 STOP gate ->
slice C, mid cut -> final cut, EM7 -> KB final -> package and KB review -> merges package, KB -> KB generation published -> clio re-pin,
gate 3, review -> clio merge.

| # | Step | Depends on | Runs in parallel | On-path working days low / likely / high |
|---|---|---|---|---|
| 1 | Owner answers Q1-Q20 (done 2026-10-28); Q19 AC edits by hand, off the path | - | EM0 rest (read-only, partly done 2026-10-28); C1 and slice A (C2, C7 part 1) start on the agreed answers | 0 / 0 / 0 |
| 2 | EM1 designer capture | owner login | BMAD docs PR drafted, gate 1, opened on the owner's word | 0 / 0.25 / 0.5 |
| 3 | Rest of slice B with unit tests (C3, C4 complete incl. replace reconcile and refusals, C9); gate 2 per push | 1 (contract), 2 (C3 target shape) | | 0.5 / 1 / 1.75 |
| 4 | Experimental cut + install | 3; owner's yes; site restart | | 0.1 / 0.25 / 0.5 |
| 5 | **EM2 + EM3 STOP gate** (EM2: save, pre-save gate, stored bytes and replace diffs vs EM1; EM3: designer opens and saves unchanged) | 4; owner login for EM3 | | 0.25 / 0.5 / 1 |
| 6 | Slice C (C5, C6, C7 part 2, notices, C10, P, H), gate 1, mid cut, EM4-EM15 except EM7, incl. EM4c (EM6, EM12 dropped) | 5 passed | KB and clio drafted once the contract is frozen; BMAD PR merged; the owner configures mail sending on stand `Creatio` | 1.5 / 2 / 3 |
| 7 | Final cut + EM2 describe re-run + EM7 on stand `Creatio` + gate 3 (package) | 6; the owner's word that the mailbox works | | 0.25 / 0.5 / 0.5 |
| 8 | KB final (real floor, `libraryVersion`, fact-check, gate 3); clio draft final (rebundle, floors, e2e Ignored = 0) | 7 | KB and clio in parallel | 0.5 / 0.75 / 1 |
| 9 | Review of the package and KB PRs: reviewers requested on the owner's ask, one round, possible re-cut | 8 | | 0.5 / 1 / 2 |
| 10 | Merges package -> KB; the KB generation is published on merge | 9 | | 0 / 0.25 / 0.5 |
| 11 | clio: curated re-pin, gate 3, ready, reviewers on the owner's ask, merge; follow-ups | 10 | | 0.25 / 0.5 / 1 |
| | **Total** | | | **3.85 / 7 / 11.75** |

Illustration (holidays ignored): answers given on Wed 2026-10-28; counting from Thu 10-29 gives the last merge on Tue
11-03 (low), Fri 11-06 (likely), Fri 11-13 (high). Review is not the long pole: File-family review took hours once
ready, and the ~12 agent hours after review starts fit in the review windows (steps 9 and 11). The long poles are owner
latency (steps 2, 5; step 1 is done), the owner's mail setup before step 7, stand approvals (4, 6, 7), the EM2/EM3
gate and the clio step that waits for the KB publication (11).

## 6. What moves the estimate up

| # | Event | Mechanism | Agent h | Owner h | Calendar | Cuts / PRs |
|---|---|---|---|---|---|---|
| 1 | **EM2 or EM3 fails** (the refinement's residual risk) | the pre-save gate refuses a server-built nested item (traced only, `R/r4-runtime.md` §5), or the designer control does not render the rows / a no-op save changes the bytes; C3 and C7 are reworked against a second capture | +4-10 | +1-2 | +1-3 wd | +1 cut |
| 1a | ... and no stored shape is accepted | the feature cannot ship as designed | stop, re-plan with the owner; this estimate no longer holds | | | |
| 2 | Description budget swap needs reviewer agreement | create/modify have ~2-3 B of headroom (computed, `R/r7-clio-kb.md` §2); a reviewer refuses the swapped text; fallback = a ceiling raise, which reopens Q12 (agreed: swap, no raise) | +0.5-1.5 | +0.3-0.5 | +0.5-1 wd | one more clio review round |
| 3 | Mail sending on stand `Creatio` not working when the final cut is ready | DoD AC4 cites EM7 (Q17 agreed), so the package PR waits for the owner's mail setup; until then auto mode rests on the unit tests and the shared run-time path. No second stand and no extra install | +0.5-1 | +0.5-1.5 | +0.5-2 wd, open-ended | 0 |
| 4 | A foreign cut on `main` after approval | re-cut; a package push dismisses approvals (#100, twice), or X8 restamp-only + rebundle-only PRs. Two open package PRs on 2026-10-28 (#80 draft, #82); history of collisions: 1.6.2.3 stamped twice in four minutes, 1.6.6.42 and .49 double-stamped | +1-2 | +0.3-0.8 | +0.5-1 wd | +1 cut, +1-2 PRs |
| 5 | KB `libraryVersion` collision | three non-draft clio-knowledge PRs open on 2026-10-28 (#303, #305, #306) each bump the version; a collision forces a re-bump, size-gate re-run and a curated re-pin, maybe as PR 6 | +0.5-1 | +0.1-0.3 | +0.5 wd | +0-1 PR |
| 6 | An agreed decision is reopened (all twenty agreed 2026-10-28; the changed Q9, Q16, Q17 and the Q20 scope are priced above) | Q4 C (removal op): a new op token costs bytes in the 19,000 B wire bundle and the op drift tests, +3-6 h. Q8 B (widen `TakesFiles`): shared mapping rules of every element, PU-42, full 3-lens review, +3-6 h, +1 owner h. Q6 B (reachability as a refusal instead of a warning): the same `Traverse` reuse, about the same size; Q6 A (no reachability check) lowers C6 by 1-2 h. Q20 B (warn instead of refuse): the same size. Q4 B lowers the estimate by 2-4 h (no reconcile, dependency check, removal notice or removal read-back), Q3 B by 1-2 h | see text | | +0.5-3 wd | |
| 7 | CI compiles against 8.3.4 core-bin, the local main checkout has 10.1.37, the worktree has no `.application` | a post-8.3.4 member compiles locally and fails in CI (`R/r8-testing.md` §1) | +0.5-2 | 0 | +0-0.5 wd | +1 push (+1 cut if after review) |
| 8 | Stand contention | each install restarts stand `Creatio` for everyone; after a rebundle, a reviewer whose clio bundles a newer cut than the stand is refused by convergence until the stand is brought up (a stand ahead of the reviewer's clio is not refused, `CLIO/clio/Common/BundledPackageConvergence.cs`) | | +0.2-0.5 | +0.5 wd per clash | |

Outcomes that do NOT move the estimate: EM8 (Q3 A or B is the same size), EM9 (guidance wording only), EM10, EM13,
EM14, EM15 (one guidance line each), EM11 (a dangling reference saved green becomes a follow-up proposal, Q16).

What moves it down (the decisions already came in one sitting, 2026-10-28): EM1 the next day, the mailbox on stand
`Creatio` working before the final cut, no foreign cut, reviewers requested the morning the PRs are ready, the KB
generation published within the hour: about 4 working days, ~24 agent h, ~11 owner h.

## 7. Against the refinement estimate

Refinement comment 493117 (2026-09-08): "blocked, 7-8d (6-11) once unblocked", revised down from 11 d because a
prototype did the parameter machinery and nested mapping in ~2 d instead of 5 d.

| Aspect | Refinement | This estimate | Effect |
|---|---|---|---|
| Unit | developer days of one implementer | agent hours + owner hours + calendar (owner's model) | 7-8 developer days become ~15 owner hours (~2 days of attention) plus ~42 agent hours |
| Calendar | 7-8 d (6-11) | 7 wd (4-12) from the answers of 2026-10-28 | about the same: calendar is set by decisions, captures, stand approvals, review and the KB-before-clio order, not by typing |
| Blocked | yes: AC2 needs ENG-92719 "File processing element", AC4 needs ENG-95986 "Support template message mode in the Send Email element" | no: both shipped (ENG-92719 closed 2026-10-27; ENG-95986 resolved 2026-09-21) | the dependency wait disappears |
| Prototype credit | 5 d -> 2 d for the slot machinery | none: costed clean-slate (owner rule); C3 is sized in full | up |
| `ProcessParameterService` allow-list lift + regression pass over every element | in scope (blast radius) | not needed: element-owned slots set the type by UId and never reach the allow-list (`R/r10-critic.md` C3) | down; the shared surface becomes the slot guard in the mapping service's `Plan` (C5) |
| Delivery proof | needs a configured mailbox, stands have none | manual mode through ActivityFile rows (EM4, EM4c, EM5); auto mode and real delivery through EM7 on stand `Creatio` once the owner has configured mail sending there (Q17 agreed) | the hard blocker becomes an owner task of ~0.5-1 h |
| Feature flag | build fails when off | no build-time check (Q9 agreed): a rollout toggle on by default since 7.17.1 and re-asserted by every install; one troubleshooting line in the KB article | down: no reader seam, no notice, no EM12 |
| Removal semantics | "decide up front" | replace (Q4): one-to-one reconcile, a dependency check before each removal, a removal notice in the write shape, clio's removal read-back, idempotent re-send | up, ~4-6 agent h inside C3/C4/K7 |
| Scope the refinement did not see | | shared planner (C2), describe with issues (C7), slot guards (C5, Q8/Q10), build order refusal and reachability warning (C6, Q6), unscoped-source refusal for Object attachments and Generated report (Q20), multi-instance Send email refusal (Q16), read-back (K7), new KB article + curated re-pin, BMAD docs PR, 14 measurements instead of one probe | up: ~9.8k authored lines vs the refinement's implied smaller slice |
| Residual risk | one deploy-and-open probe before the sprint; failure "moves the estimate materially" | the EM2/EM3 gate on the critical path after the experimental cut; failure +1-3 wd (section 6, row 1) | same risk, placed and priced |
| Upper bound | 11 d = probe failure | 12 wd without adverse events; rows 1-5 of section 6 can add 1-3 wd each | comparable |

Net: the calendar lands inside the refinement's 7-8 d, while human effort falls by about 4x. The scope is
larger than the refinement assumed (replace semantics, describe issues, guards, a reachability warning, a new
guidance article, a BMAD PR, 14 measurements). Two refinement costs disappear, the shared parameter-service change
and the build-time feature check, and the mailbox dependency shrinks to the owner's mail setup on stand `Creatio`.
