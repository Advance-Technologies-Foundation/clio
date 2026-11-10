# ENG-105556 AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page - estimate

| | |
|---|---|
| Issue | ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page" (Task; epic ENG-92704 "Create BP via AI Toolkit") |
| Written | 2026-11-08, local, untracked. Before any owner answer: every figure is sized against the RECOMMENDED options of [the decisions file](eng-105556-minimal-dcm-decisions.md) (Q1..Q29, all "open - owner decides"); a different answer moves the figure by the row named in section 5 |
| Inputs | [chosen design](eng-105556-minimal-dcm-design.md) (sections cited as "design §N"); decisions Q1..Q29; research notes in `<research-dir>` (`_verdicts.md` and g1..g6 override r1..r10); the three design drafts `design-{a-reuse,b-extensible,c-risk}.md` there (line sizes) |
| Calibration | the measured history of the same epic as recorded in the ENG-92710 "Call web service element" estimate, alias **E92710** = `spec/eng-92710-call-web-service-element/eng-92710-call-web-service-element-estimate.md` (its sources: git `--numstat` per merge, transcript activity intervals; tagged there measured / prior-measured) |
| Baselines | CrtProcessBuilder `main` a3f58852 (1.6.6.140); clio `master` 69788ccd5; clio-knowledge `master` 622134b (libraryVersion 1.15.180); stand `Creatio` (.NET Framework, read-only for this research, runs CrtProcessBuilder 1.6.6.142, an unmerged feature build, v r10-C2) |

Model (owner rule): an AI agent writes the code, the tests, the docs and the guidance. Each work item gives (1) AI
active (agent wall) time, (2) human time that does not compress (decisions, review, approvals, browser and stand
checks, merges), (3) calendar time on the critical path. Every figure below is **[inferred]** (an estimate) unless it
carries another tag; the calibration inputs are **prior-measured** (measured earlier, recorded in E92710). No future
CrtProcessBuilder cut number is projected (Q22 only fixes the rule "above every deployed build").

Evidence tags as in the design: `[traced path:line]`, `[corpus N/M]`, `[measured]`, `[note]`, `[inferred]`;
`(v rX-CY)` = row of `_verdicts.md`; `(g5 §3)` = research note section.

---

## 0. Headline

| Quantity | Likely | Range (low-high) | Note |
|---|---|---|---|
| **AI active time** | **~35 h** (no-surprise); **~42-50 h** with the compound buffer | 21-58 h | ~10.5 h line-rated code (9.5k authored lines x 1.1 h/1k) + ~24 h line-independent work (BMAD docs, day-0 checks, DCM unit-test harness spike, stand probes M1-M10, cuts, E2E and CAADT runs on two runtimes, KB fact-check, headless agent runs KB-7/AR-1/AR-2, review gates); section 2. The buffer of section 5.1 (R2 + R4 + R10 + R11 + half of R13) is the realistic expectation: R10 is close to certain, because stand `Creatio` already runs a 1.6.6.142 feature build above main (v r10-C2) |
| Human time (owner) | ~15.4 h (no-surprise); ~17-19 h with the buffer | 8.9-25.8 h | ~1.9-2.4 working days of attention spread over the calendar; 29 decisions, diff review, browser checks on both runtimes, .NET 8 stand provisioning, SVN seed commit, merges; section 2 |
| Calendar, decisions -> last merge | **7.5 wd** (no-surprise); **~9.5-11.5 wd** with the buffer | 4-15.25 wd | critical path in section 4; the long poles are owner latency on 29 decisions, the M1 designer STOP gate and the .NET 8 stand (none reachable today, g5 §1) |
| Authored lines (all repos) | ~9.5k | 6.8-14.3k | planned 6.8k (package src 1.4k + tests 2.2k, clio src 0.85k + unit 1.3k + E2E 0.5k + docs 0.05k, KB 0.35k, CAADT 0.15k) x review growth 1.0 / 1.4 / 2.1; section 1.3 |
| PRs | 6 + 1 SVN commit | 6-9 | BMAD docs (clio), package, KB, clio feature, CAADT prompt (`creatio-adaclio-testing`), CAADT spec (`creatio-playwright-tests`), SVN `AutoTestClioMcp` seed; 0-3 follow-ups; section 6 |
| CrtProcessBuilder cuts | 4 | 3-6 settled; 10-25 if review keeps finding platform-fidelity errors | section 6 |

Against the pre-plan rough estimate (~12-20 h AI, ~1.5-2 days human, 6-9 working days): AI time is **up ~1.7-2.5x**
(no-surprise 35 h, with the buffer 42-50 h, vs a 20 h top), human time is **inside to slightly above** (15.4-19 h ~
1.9-2.4 d), calendar is **inside without surprises** (7.5 wd) and **above with the buffer** (9.5-11.5 wd), with a
**wider tail** (15.25 wd). The no-surprise figures are the "everything goes right" case, not the expectation. Why:
section 8.

Not in these figures: the research, the three designs, the judge's merge, the decisions file and this estimate
(2026-11-08, spent); reviewers' own time (E92710 put it at 3-6 h over 4-5 PRs [prior-inferred]); the later
"steps inside stages" task (Q27, design §12); route B of the save (Q6) unless risk R1 fires.

---

## 1. Calibration and sizing basis

### 1.1 Constants reused from the epic's measured history

| Constant | Value | Evidence |
|---|---|---|
| Code-only agent rate | 0.8 / 1.1 / 1.5 h per 1k authored lines | E92710:120 (derived there from ENG-95985 "Send email attachments": 30.5 h build minus 10-13 h line-independent work over 20.7k lines = 0.85-0.99 h/1k, raised to 1.1 central) [prior-measured + inferred] |
| Agent h per element-sized feature (no research phase) | 11-35 h | E92710:121 [prior-measured] |
| Review growth of authored lines | ENG-95985 came in at 2.1x the planned lines; about half of the overrun was scope the owner added in review | E92710:102 and :103 [prior-measured] |
| tests/src | 1.0-2.6 | E92710:122 [prior-measured] |
| Owner diff review | 0.25 / 0.35 / 0.5 h per 1k lines (mostly in the browser, above the in-transcript 0.2-0.45 h/1k) | E92710:123, :273 [prior-measured + inferred] |
| Decisions | 20 questions = 1.5 / 2.25 / 3 owner h | E92710:267 [prior-inferred]; scaled x 29/20 here |
| BMAD docs PR incl. gate 1 | 2.5 / 3.5 / 5 agent h | E92710:187-190 [prior-inferred] |
| Cut cycle (claim, restamp, tag, rebundle with pins, rebuild clio, install on the owner's yes, probe) | 1.5 / 2 / 3 h for 5-6 cuts, ~0.35-0.4 h each | E92710 §2.5 row "Cut cycles" [prior-inferred] |
| KB fact-check | 1 / 1.5 / 2 agent h | E92710 §2.5 row "KB fact-check" [prior-inferred] |
| Cuts per feature | 3-6 settled; 13-29 when review keeps finding scope or platform-fidelity errors | E92710:124 [prior-measured] |
| Review ready -> merge | ~1 working day | E92710:126 [prior-measured] |

### 1.2 What makes this ticket different from the calibration set

- **No compile, no restart for the artifact itself**: a DCM is interpreted (v r1-C1, v r6-C2) and the page is a
  Freedom UI schema; no compile cycle enters the stand loop (unlike script-task and web-service elements) [traced
  design §8]. Down.
- **No external-schema sync and no designer capture as an oracle**: the shape is the designer's minimal one, already
  traced (full mesh, `#0C8701`, fresh UIds; `_verdicts.md` conflicts 6-7). The parity oracle is the shipped
  `KnowledgeArticlePublishing` metadata fixture, not a new capture (design §14.1 `DcmMetadataParityTests`). Down.
- **A new manager family** (`DcmSchemaManager`, not `ProcessSchema`): no existing package handler builds a
  `BaseProcessSchema` that is not a `ProcessSchema` (v r6-C5), so the handler, repository and composer are new code,
  not extensions. Up.
- **Two writes in two repositories for one call** (package: case + settings + bindings; clio: page fragment) with
  compensation and a three-outcome report (design §5, §6). Up.
- **Two runtimes with no .NET 8 stand** (g5 §1): provisioning is an owner action and a separate run set (Q24). Up.
- **An E2E arrange that does not exist yet** (g5 §3): seeded lookup column in SVN `AutoTestClioMcp` (Q25) or a
  `LocalOnly` fixture, because adding a lookup column publishes (g5 §3.2). Up.

### 1.3 Planned lines (before review growth)

| Bucket | Planned lines | Source |
|---|---|---|
| Package production | ~1,400 | design §11 "New code" (design A's 1,250 plus the C/B grafts) |
| Package tests | ~2,200 | design A §15 put 1,600 lines on 8 fixtures; the chosen design lists 11 fixtures (design §14.1) -> 1,600 x 11/8 [inferred]; tests/src 1.57, inside 1.0-2.6 |
| clio production | ~850 | design §11; design A §2.4 split: tool/options/command/service ~650, `RecordPageResolver` ~150, fragment ~40 |
| clio unit tests | ~1,300 | design A §15 put ~1,200; plus the refusal and page-placement fixtures of design §14.2 [inferred] |
| clio E2E | ~500 | design A §15 ~400; plus cases 4-6 of design §14.3 [inferred] |
| clio docs (`install-process-builder` tool list, `McpCapabilityMap.md`) | ~50 | design §15 step 4 [inferred] |
| KB article + routing rows + `process-modeling` fix + test class | ~350 | design A §2.4: ~8 KB article (~150-200 Markdown lines) + ~150 test lines |
| CAADT prompt + Playwright spec | ~150 | design §14.4 (1 YAML, 1 spec) [inferred] |
| **Total** | **~6.8k** | growth 1.0 / 1.4 / 2.1 -> **6.8 / 9.5 / 14.3k** |

Growth 1.4 likely: half of ENG-95985's 2.1x was owner-added scope (E92710:103); here 29 open decisions can add
scope the same way, but the shape is narrower (no capture-driven rework) [inferred].

---

## 2. Work items

Rate arithmetic for line-rated items: lines x growth (1.0 / 1.4 / 2.1) x rate (0.8 / 1.1 / 1.5 h per 1k); owner diff
review = grown lines x 0.25 / 0.35 / 0.5 h per 1k. "Calendar" = working days this item puts ON the critical path
(section 4); "par." = runs beside the path.

| WI | Work item | AI active h low / likely / high | Human h low / likely / high | Calendar wd on path low / likely / high | Depends on |
|---|---|---|---|---|---|
| WI-0 | **Owner decisions Q1-Q29**; agent records the answers in the decisions file and updates the design | 0.25 / 0.5 / 0.75 | 2.2 / 3.3 / 4.35 (29 questions at the E92710 rate) | 0.25 / 0.5 / 1.5 (owner latency) | - |
| WI-1 | **Day 0**: (a) core-API presence check against the CI core 8.3.4 (trap T1, section 5); (b) next free `KnownRoute` after rebase (`_verdicts.md` conflict 4); (c) tags of both repos and the open branches for the version rule (Q22); (d) re-read the budgets: discovery index 18,312 / 18,432 B (v r7-C4), `process-modeling` ~18,987 / 19,000 B (v r7-C8); (e) claim comment / Jira text drafts, each on the owner's yes | 0.5 / 1 / 1.5 | 0.2 / 0.3 / 0.5 | 0.25 / 0.5 / 0.75 (together with WI-2) | WI-0 |
| WI-2 | **BMAD docs PR** (PRD via the full `/bmad` pipeline, ADR, stories, test plan, tracker rows; repository rule "no code before the ADR") with gate 1 | 2.5 / 3.5 / 5 | 0.5 / 1 / 1.5 | (in WI-1's slot) | WI-0 |
| WI-3 | **CrtProcessBuilder `BuildCase`**: contracts, gate `EnsureCanManageDcm` (Q5), `IPackageCatalog` split, planner (Q8, Q10, Q11, Q29), ESQ reader (Q9), composer, `DcmSchemaRepository` + `IDcmSchemaSaver` (Q6), settings writer + bindings + read-back (Q13-Q15), dependency planner (Q16), verifier + compensation (Q7, Q12), op + composition; 11 fixtures; net472 and local `dev-n8` runs (v r8-C6). 3.6k planned lines. PLUS, line-independent: a **DCM unit-test harness spike** 1.5 / 2.25 / 3 h ahead of slices B/C (first composer and repository fixtures on dev-nf and dev-n8: `RequireMock.DCM` is used 0 times in the package tests, v r8-C4; `InternalSaveSchema` stubbed via `NSubstituteExtension.Protected` with 9 matchers, v r8-C7; `TestDcmSchemaManager` / `ForPartsOf` for `CreateItem`, v r8-C3) and the **P-D3 package documentation** 0.25 / 0.5 / 0.75 h (capture doc, architecture doc, package spec, package `AGENTS.md` gate paragraph, diary, glossary, ADR) | 4.65 / 8.29 / 15.09 (line-rated 2.9 / 5.54 / 11.34 + spike + P-D3) | 0.9 / 1.8 / 3.8 (diff review) | 0.25 / 0.25 / 0.5 (harness spike) + 0.75 / 1.25 / 2 (first half: contracts -> composer -> repository -> settings writer) + 0.75 / 1.25 / 2 (second half: verifier, compensation, dependency planner, gate 1, mid cut) | WI-2 |
| WI-4 | **Stand probes M1-M7, M9, M10** on a read-write .NET Framework stand (design §14.5): experimental cut installed, then M1 designer open (STOP gate), M2, M5 residue of `RemoveItemByUId`, M6 binder target and rights, M4 non-admin gate, M7 runtime start, M9 `AutoAddPackageDependenciesInProcesses`, M10 compile marking of a dependency add; cleanup in the g5 §4 order, strictly sequential | 2.25 / 3.5 / 5.25 (incl. cleanup 0.25 / 0.5 / 0.75) | 0.75 / 1.25 / 2 (write approvals 0.25 / 0.5 / 0.75; browser M1, M3, M7 0.5 / 0.75 / 1.25) | 0.25 / 0.5 / 1.5 (M1 gate) + 0.25 / 0.5 / 1 (E2E NF, M3/M4/M7) | WI-3 first half; a read-write NF stand (which one: **unknown**, section 9) |
| WI-5 | **clio-knowledge**: `processes/dynamic-case.md`, itemId `process-dynamic-case` (Q23), routing rows, `process-modeling.md:157-158` correction, `ProcessDynamicCaseGuidanceTests`, `libraryVersion` bump; refute-first fact-check of the article against code | 1.3 / 2.0 / 3.1 (code 0.3 / 0.5 / 1.1 + fact-check 1 / 1.5 / 2) | 0.1 / 0.2 / 0.35 | par. (drafted once the contract is frozen after M1) | M1 passed |
| WI-6 | **clio `create-dynamic-case`**: tool, options, command, `DynamicCaseService` (validate -> page checks -> dependency add -> build -> page append), `RecordPageResolver` (g4 §7), fragment constant, route (both runtimes), DI, floor, docs; unit tests incl. the budget, wire, pin and compile-note gates (design §14.2). 2.2k planned lines | 1.8 / 3.4 / 6.9 | 0.55 / 1.1 / 2.3 | par. (beside WI-3 second half, contract frozen) | M1 passed |
| WI-7 | **E2E** `CreateDynamicCaseToolE2ETests` (design §14.3) + arrange: SVN `AutoTestClioMcp` seed prepared for the owner's commit (Q25) and a `LocalOnly` interim fixture (`sync-schemas`); runs on NF | 1.9 / 3.0 / 5.3 (code 0.4 / 0.8 / 1.6 + seed and runs 1.5 / 2.25 / 3.75) | 0.4 / 0.75 / 1.5 (diff + SVN commit + lane check) | (in WI-4's E2E slot) | WI-6; final or mid cut |
| WI-8 | **Rebundle and cuts**: 3 / 4 / 6 cuts with `rebundle-process-builder.ps1 -Version`, both security pins in the same commit (`ExpectedOperationContractCount` 8 -> 9 and a NEW `EnsureCanManageDcm` call-site pin, design §14.2), floor (Q22), install, `list-packages` | 1.2 / 1.6 / 2.4 | 0.25 / 0.4 / 0.6 (install approvals; each install restarts the stand) | 0.25 / 0.5 / 0.75 (final cut + KB final + clio final) | WI-3, WI-6 |
| WI-9 | **CAADT**: business-level prompt in `creatio-adaclio-testing` + Playwright spec in `creatio-playwright-tests` (design §14.4); one NF run | 0.9 / 1.5 / 2.5 (code 0.1 / 0.2 / 0.5 + runs 0.75 / 1.25 / 2) | 0.3 / 0.6 / 0.9 (diff + two GHE PRs) | par. | WI-6, a cut on the stand |
| WI-10 | **.NET 8**: owner provisions a `StudioNet8` stand (`DeployApplicationCommon`, BuildPath = a `StudioNet8` zip; a trunk zip needs `BuildProductNet8`, g5 §6) and installs the cut; agent registers it, runs M8 (M1-M7 reads), the E2E fixture and a local CAADT run (the matrix cannot select a runtime, g5 §5) | 1.5 / 2.5 / 4 | 0.75 / 1.5 / 3 (provisioning + browser M8) | 0.25 / 0.5 / 1 if provisioned in parallel from WI-3 onward; +1-3 wd if not (R9) | Q24; a cut |
| WI-11 | **Review gates and merges**: gate 1 full 3-lens on the package (new rights gate = security-sensitive) and on clio (`Common`/DI touched); per-commit triage; gate 3 on all PRs; one reviewer round; merges package -> KB (publishes) -> clio re-pin -> clio | 1.5 / 2.5 / 4 | 1.5 / 2.5 / 3.7 (review rounds 1 / 1.75 / 2.5 + merges, tags, claims 0.5 / 0.75 / 1.2) | 0.5 / 0.75 / 2 (gate 3 + round) + 0 / 0.25 / 0.5 (package, KB merges) + 0.25 / 0.5 / 1.25 (clio re-pin, gate 3, merge) | all |
| WI-12 | **Headless agent runs** KB-7 / AR-1 / AR-2 (test plan section 6): a clean-context agent with only shipped guidance and the clio branch build, against the read-write stand; transcript review; feedback into the article | 0.75 / 1.25 / 2 (runs + one feedback loop) | 0.5 / 0.75 / 1.25 (transcript review) | 0 / 0.25 / 0.5 (before the KB PR leaves draft) | WI-5 draft, WI-6 branch build, a cut on the stand |
| | **Total** | **20.9 / 34.6 / 57.8** | **8.9 / 15.4 / 25.8** | **4.0 / 7.5 / 15.25** | |

Arithmetic (before WI-12, the WI-3 spike and P-D3; those add AI 2.5 / 4.0 / 5.75, human 0.5 / 0.75 / 1.25, calendar
0.25 / 0.5 / 1.0 wd, giving the Total row). AI low: 0.25 + 0.5 + 2.5 + 2.88 + 2.25 + 1.28 + 1.76 + 1.9 + 1.2 + 0.87 + 1.5 + 1.5 = 18.39. Likely:
0.5 + 1 + 3.5 + 5.54 + 3.5 + 2.04 + 3.39 + 3.02 + 1.6 + 1.48 + 2.5 + 2.5 = 30.57. High: 0.75 + 1.5 + 5 + 11.34 +
5.25 + 3.1 + 6.93 + 5.33 + 2.4 + 2.47 + 4 + 4 = 52.07. Of that, line-rated code = 5.4 / 10.5 / 21.4 h (6.8 / 9.5 /
14.3k lines) and line-independent work = 13.0 / 20.1 / 30.7 h.
Human low: 2.2 + 0.2 + 0.5 + 0.9 + 0.75 + 0.1 + 0.55 + 0.38 + 0.25 + 0.29 + 0.75 + 1.5 = 8.37. Likely: 3.3 + 0.3 + 1 +
1.76 + 1.25 + 0.2 + 1.08 + 0.75 + 0.4 + 0.57 + 1.5 + 2.5 = 14.61. High: 4.35 + 0.5 + 1.5 + 3.78 + 2 + 0.35 + 2.31 +
1.53 + 0.6 + 0.91 + 3 + 3.7 = 24.53.

Cross-check against the analog band: 18-52 h sits on the 11-35 h element band (E92710:121) and above it at the high
end, because the high end carries 2.1x growth AND the two-runtime run set; the likely 31 h is inside the band.

---

## 3. Totals by repository (likely)

| Repository / place | AI h | Human h | Lines (grown) |
|---|---|---|---|
| the package repository (WI-3 incl. harness spike and P-D3, share of WI-8, WI-11) | ~11.25 | ~3 | ~5.0k |
| clio (WI-2, WI-6, WI-7, share of WI-8, WI-11) | ~11.5 | ~4 | ~3.8k |
| clio-knowledge (WI-5, WI-12) | ~3.25 | ~1.25 | ~0.5k |
| CAADT, two GHE repos (WI-9) | ~1.5 | ~0.6 | ~0.2k |
| SVN `AutoTestClioMcp` (inside WI-7) | ~0.75 | ~0.5 | platform-exported schema/data files, not counted |
| Stands NF + .NET 8 (WI-4, WI-10) | ~6 | ~2.75 | - |
| Decisions, day 0 (WI-0, WI-1) | ~1.5 | ~3.6 | - |

The split of WI-8 and WI-11 across repositories is proportional to lines [inferred]; the rows round to the section 2
totals.

---

## 4. Critical path and calendar

Path: WI-0 decisions -> WI-1/WI-2 day 0 + BMAD (ADR before code) -> WI-3 first half -> experimental cut + install ->
**M1 STOP gate** (a route-A DCM opens in the designer with the expected stages, design §17) -> WI-3 second half, gate 1,
mid cut (WI-6 clio and WI-5 KB drafted in parallel from the frozen contract) -> E2E NF + M3/M4/M7 + CAADT NF -> .NET 8
runs (stand provisioned in parallel since WI-3) -> final cut, KB final, clio final -> gate 3 + reviewer round ->
merges package -> KB (publishes) -> clio re-pin, gate 3, merge.

| # | Step | On-path wd low / likely / high | Runs beside it |
|---|---|---|---|
| 1 | Decisions Q1-Q29 (WI-0) | 0.25 / 0.5 / 1.5 | agent pre-reads for WI-1 |
| 2 | Day 0 + BMAD docs PR drafted, gate 1, opened on the owner's word (WI-1, WI-2) | 0.25 / 0.5 / 0.75 | owner starts .NET 8 provisioning (WI-10) |
| 3 | WI-3 harness spike, then first half with unit tests | 1 / 1.5 / 2.5 | SVN seed prepared (WI-7) |
| 4 | Experimental cut + install + **M1 STOP gate**, M2, M5, M6 (WI-4, WI-8) | 0.25 / 0.5 / 1.5 | |
| 5 | WI-3 second half, gate 1, mid cut | 0.75 / 1.25 / 2 | WI-6 clio, WI-5 KB, WI-9 CAADT drafted |
| 6 | E2E NF, M3, M4, M7, M9, M10, CAADT NF (WI-4, WI-7, WI-9); headless agent runs KB-7/AR-1/AR-2 before the KB PR leaves draft (WI-12) | 0.25 / 0.75 / 1.5 | owner SVN commit of the seed |
| 7 | .NET 8: M8, E2E, CAADT local (WI-10) | 0.25 / 0.5 / 1 | |
| 8 | Final cut, KB final, clio final with floor and pins (WI-8) | 0.25 / 0.5 / 0.75 | |
| 9 | Gate 3 package + KB, reviewers on the owner's ask, one round, possible re-cut (WI-11) | 0.5 / 0.75 / 2 | |
| 10 | Merges package -> KB; the KB generation publishes | 0 / 0.25 / 0.5 | |
| 11 | clio re-pin, gate 3, reviewers, merge | 0.25 / 0.5 / 1.25 | CAADT PRs merge |
| | **Total** | **4.0 / 7.5 / 15.25** | |

Low: 0.25 + 0.25 + 1 + 0.25 + 0.75 + 0.25 + 0.25 + 0.25 + 0.5 + 0 + 0.25 = 4.0. Likely: 0.5 + 0.5 + 1.5 + 0.5 +
1.25 + 0.75 + 0.5 + 0.5 + 0.75 + 0.25 + 0.5 = 7.5. High: 1.5 + 0.75 + 2.5 + 1.5 + 2 + 1.5 + 1 + 0.75 + 2 + 0.5 + 1.25 = 15.25.
With the compound buffer of section 5.1 (+2-4 wd): ~9.5-11.5 wd likely.

Off the path while it holds: the KB and clio drafts (step 5), the SVN seed (lane coverage arrives with the next lane
deploy after the commit; until then the fixture runs `LocalOnly`, g5 §3.2), the CAADT PRs. On the path only when
late: the .NET 8 stand (step 7; R9 adds 1-3 wd when provisioning starts after step 6). The repository order
"package -> KB -> clio" is fixed by the bundled-package rules and KB publication (design §15).

Illustration (holidays ignored): answers on Mon 2026-11-09 put the last merge on Fri 11-13 (low), Wed-Thu 11-18/19
(no-surprise), Mon-Tue 11-23/24 (with the compound buffer), Mon 11-30 (high).

---

## 5. Risk buffers (not in the totals; each tied to a named decision or trap)

### 5.1 Up

| # | Event | Tied to | Mechanism | AI h | Human h | Calendar |
|---|---|---|---|---|---|---|
| R1 | **M1 fails**: a route-A DCM does not open cleanly in the designer | Q6, Q27; design §17 | switch the saver to route B (`new DcmSchemaDesigner(uc).Save`) and write the readable-to-design JSON transform in the undocumented dialect (g2 §2); traced equal for an element-less DCM (g2 §4), so not expected | +6-10 | +1 | +1-2 wd, +1 cut |
| R2 (trap T1) | **CI core 8.3.4 lacks a member the design uses**: CI compiles and unit-tests against 8.3.4 [traced pb105556/docs/ci-cd.md:177; pb105556/AGENTS.md:74]; the design traced core trunk a95349820. Presence of public `DcmEntityUtilities.FindEnabledDcmSchemas`, `GetIsActiveVersion(Guid)`, `IPackageSchemaDataCreator` in 8.3.4 is **not measured** (the local `.application` folder of pb105556 is absent) | Q5, Q7, Q12 (S7, V1, W3 of design §5) | an ESQ fallback for the enabled-DCM query (the internal `GetIsActiveVersion(item)` is not available, design §1), or a raised platform floor; WI-1 (a) settles it on day 0 | +1-3 | +0.25 | +0.25-0.5 wd |
| R3 | **M5**: `RemoveItemByUId` leaves the start-event row or flow schema | Q7 | extra compensation deletes, or narrower `failed-leftover` wording only | +1-2 | - | - |
| R4 | **M6**: `IPackageSchemaDataCreator` from a WCF call binds into another package or under other rights | Q15, W3 | rework of the settings writer's binder adapter | +1-3 | +0.25 | +0.5 wd |
| R5 | **M4**: the gate refuses or admits differently from what the saves enforce for a non-admin | Q5 | adjust the gate and its fixture | +0.5-1 | +0.25 | - |
| R6 | Q2 answered "sibling `DcmDesignService`" | Q2 | new mirror test, unmeasured route family (B's O1), its own route tests | +1.5-3 | +0.25 | +0.25 wd |
| R7 | Q18 answered "extract `IPageFragmentWriter`" | Q18 | refactor inside the 836-line `PageUpdateTool` with its guard tests [traced, judge CLIO/clio/Command/McpServer/Tools/PageUpdateTool.cs:29-31] | +3-5 | +0.5 | +0.5 wd |
| R8 | SVN seed (Q25) not committed or not deployed by the lane before "done" | Q25 | lane coverage waits; the `LocalOnly` interim carries the evidence | 0 | +0.25 | +0.5-1 wd if "done" requires a lane run |
| R9 | **.NET 8 stand late**: no registered `isNetCore` stand answers (8/8 refused) [measured g5 §1]; a trunk `StudioNet8` zip needs `BuildProductNet8` (g5 §6) | Q24 | provisioning starts after step 6 instead of step 2 | +0.5 | +1-2 | +1-3 wd |
| R10 | Foreign cut on `main`, or the 1.6.6.142 feature branch merges first (v r10-C2) | Q22 | re-cut above the new top; approvals dismissed by a GHE push | +1-2 | +0.3-0.8 | +0.5-1 wd, +1 cut |
| R11 | Another branch takes the last discovery-index line first (120 B left, one line fits, v r7-C4) | Q3, Q4 | byte swap or a deliberate ceiling re-pin | +0.5-1.5 | +0.25 | +0.25 wd |
| R12 | KB: `libraryVersion` collision with another open KB branch; `process-modeling` edit exceeds ~13 B headroom (v r7-C8) | Q23 | re-bump, size-gate re-run, curated re-pin | +0.5-1 | +0.25 | +0.25-0.5 wd |
| R13 | **Review rounds of the platform-fidelity class** | Q6, Q7, Q29 | ENG-92707 "Sub-process: selection + parameter sync" ran 23+ rounds and 27 descriptor versions (E92710:85, :124) [prior-measured] | +8-15 | +2-5 | +2-3 wd, cuts to 10-25 |
| R14 | `KnownRoute` renumber after rebase (two remote clio branches claim 122, `_verdicts.md` conflict 4) | - | renumber + route tests | +0.25 | - | - |
| R15 | E2E arrange publishes / parallel schema writes take the NF app pool down (g5 §3.2, §4) | Q25 | sequential runs, `LocalOnly` tier, a rerun | +0.5-1 | +0.25 | +0.25 wd |
| R16 | M10: a dependency add marks something for compile | Q16 | obligations entry for the dependency artifact + note text | +0.25-0.5 | - | - |
| R17 | Q9/Q8 dispute after review (culture compare vs collation; default order) | Q8, Q9 | `Select` ORDER BY with `HasOrderByItem` pin (v r8-C4) instead of the in-memory sort | +1-2 | +0.25 | - |

Realistic compound buffer (R2 + R4 + R10 + R11 + half of R13, the events most likely to co-occur [inferred]):
**+7-15 AI h, +1.5-4 human h, +2-4 wd**. R1 and full R13 are the tail.

### 5.2 Down

| Event | Tied to | Effect |
|---|---|---|
| Q3 answered (c): case tool only, the bar through a documented `update-page` call | Q3 | -1.5-2.5 AI h (no resolver, no page step in the service), -0.5 human h |
| Q16 answered (c): refuse with the exact calls | Q16 | -0.5-1 AI h |
| Q12 answered (b): any enabled case -> `case-exists` | Q12 | -0.5 AI h |
| Q24 answered (c): code-level parity accepted | Q24 | -2.5 AI h, -1.5 human h, step 7 leaves the path (-0.5 wd) |
| All 29 answers in one sitting; M1 passes on the experimental cut | Q1-Q29 | steps 1 and 4 at their low values (-1.25 wd) |
| ENG-95985's pattern repeats (build at 0.73 of the likely estimate, E92710:101) | - | ~22 AI h, ~11 human h, ~5 wd |

---

## 6. PRs and cuts

| # | Repository | PR | Order |
|---|---|---|---|
| 1 | clio | BMAD docs (PRD via the full `/bmad` pipeline, ADR, stories, test plan, tracker rows, this folder + the evidence companion) | first; before code |
| 2 | the package repository | `BuildCase` feature | merges first, tag on the remote |
| 3 | clio-knowledge | article, routing, `process-modeling` fix, test class, `libraryVersion` | after the final cut; merges second (publishes) |
| 4 | clio | tool, route, service, resolver, fragment, rebundle with both pins, floor, docs, unit + E2E | draft beside 2; leaves draft after the KB publishes; merges last |
| 5 | `creatio-adaclio-testing` (GHE) | CAADT prompt | after 4 is usable on a stand |
| 6 | `creatio-playwright-tests` (GHE) | CAADT spec | with 5 |
| SVN | `AutoTestClioMcp` | seeded object + lookup + page + add-on (Q25) | owner commits; any time before "done" |
| 7-9 | any | restamp/rebundle-only after a foreign cut (R10), review follow-up; an AI Toolkit skill PR if plan D0.9 finds one | conditional (the curated-names re-pin is mandatory and rides in PR 4 or, after approval, its own PR) |

Cuts: experimental (M1 gate) -> mid (after gate 1; E2E, M3-M10) -> final (both runtimes) -> 0-2 review re-cuts:
3-6 for a settled design (E92710:124). Every number is claimed after checking the tags of both repositories (Q22).

---

## 7. Session and agent effort

Model names: Opus 5.5 is the model running this plan; "a smaller model" means any cheaper model the orchestrator
offers. Effort levels low / medium / high / xhigh / max. `max` is not recommended by default for any kind of work
here: it is the costliest level and nothing below is a one-shot irreversible decision that `xhigh` cannot carry
[inferred].

| Kind of work | Model + effort | Workflow / parallel agents | Why (one line) |
|---|---|---|---|
| Orchestrating session (holds the decisions, gates, stand approvals, cut claims) | Opus 5.5, high | one long session; launches the workflows below; never runs stand writes in parallel | it carries 29 decisions and the package -> KB -> clio order; high is enough because the hard reasoning is delegated |
| Research readers (any further source reads, e.g. WI-1 (a) core 8.3.4 presence) | Opus 5.5, medium | parallel workflow, one reader per source family (core, PackageStore, creatio-ui, clio, KB) | reads are mechanical; the epic's notes needed a fact-check anyway (58 of 78 verdicts "partly", 2 refuted [traced _verdicts.md:8]), so reader effort buys little |
| Fact-check (research claims, KB article claims, guidance sweeps) | Opus 5.5, xhigh | parallel refute-first agents, one claim group each, barred from the author's notes | 60 of 78 research claims needed correction [traced _verdicts.md:8]; a wrong KB rule ships to every user (the DCM "compile obligations" sentence is one, design §13) |
| Architecture / design (only if a decision reopens it, e.g. R1 route B, Q2 sibling service, the later steps task Q27) | Opus 5.5, xhigh | workflow: 2-3 independent designs + a judge, as for this design | the merge of three designs caught facts each single design got wrong (design §1 table) |
| Package C# coding (WI-3) | Opus 5.5, high | one agent sequentially for the handler, DI and composition; up to 2 parallel agents only for the pure units (planner, dependency planner) | shared files (composition root, facade, service) conflict under parallel edits; platform-fidelity of the saved shape needs care, not breadth |
| clio MCP coding (WI-6) | Opus 5.5, high | one agent; `create-mcp-tool` skill | budget pins (one index line left), pins and floors interlock in one commit; a single writer keeps them consistent |
| Docs / KB writing (WI-5, clio docs) | Opus 5.5, high for the KB article; medium for the clio docs sweep | one agent each; KB article followed by the xhigh fact-check | the article is agent-facing guidance (routing, refusals, undo order); the clio docs are list updates |
| Unit-test writing (package and clio) | Opus 5.5, high for package fixtures; medium for clio pin/budget/wire tests | can run as a second agent beside the coder once each component's interface is fixed | package mocks are non-obvious (`RequireMock.DCM`, `NSubstituteExtension.Protected(...)`, v r8-C1, v r8-C7 refuted a "cannot" claim); clio pin tests follow templates |
| E2E / stand verification (WI-4, WI-7, WI-10) | Opus 5.5, xhigh | **no parallelism**: one agent, strictly sequential writes, each write on the owner's yes | parallel schema writes take a .NET Framework app pool down (g5 §4); a wrong cleanup order deletes another case's rows (g5 §4, v r4-C8) |
| Code review gates (gate 1, gate 3) | Opus 5.5, xhigh | parallel 3-lens workflow (quality, correctness/performance, security), as the repository requires | the package adds a rights gate and writes system tables; gates 1 and 3 are the quality bar |
| Per-commit review (gate 2) | Opus 5.5, medium (single lens) or skip for docs-only | single agent | the repository's triage rule: < ~50 lines in one module = one lens |
| Rebundle and cuts (WI-8) | Opus 5.5, medium | one agent, scripted (`rebundle-process-builder.ps1`), then a high-effort check of the two hand-maintained counts | the script does the archive and provenance pins; only `ExpectedOperationContractCount` and the gate call-site pin are by hand (clio AGENTS.md "Bundled Creatio packages") |
| CAADT prompt + spec (WI-9) | Opus 5.5, medium | one agent | business-level prompt and a short spec with known assertions (design §14.4) |

---

## 8. Against the pre-plan estimate (~12-20 h AI, ~1.5-2 days human, 6-9 working days)

| Aspect | Pre-plan | This estimate | What changed |
|---|---|---|---|
| AI active time | 12-20 h | 21-58 h, no-surprise ~35 h, with the buffer ~42-50 h | **up**: the chosen design grafts C's risk mechanisms (a five-check rights gate, rows-first write order with compensation and a three-outcome report, post-save verification with a competing-case count, 32 refusal codes [traced design §3.2]) and B's shapes (dry-run, `already-present` re-run, describe-shaped response, per-artifact obligations) onto A (design header); the page step got a resolver with the RelatedPage add-on rule (g4 §7) and read-only placement checks; a dependency add path (g3); 11 package fixtures instead of 8; and ~20 h of line-independent work the pre-plan did not itemise: BMAD docs, 9 stand probes, a .NET 8 run set on a stand that does not exist yet (g5 §1), an E2E arrange that does not exist yet (g5 §3), CAADT in two GHE repositories |
| Human time | 1.5-2 days | 8.9-25.8 h, no-surprise 15.4 h (~1.9 d), with the buffer ~17-19 h | **inside**: 29 decisions (3.3 h) and .NET 8 provisioning (1.5 h) are new, but the diff to review is small (~9.5k lines vs 16.4k for ENG-92710 "Call web service element", E92710:27) and there are no designer capture sessions |
| Calendar | 6-9 wd | 4-15.25 wd, no-surprise 7.5 wd, with the buffer ~9.5-11.5 wd | **inside at the likely value, wider tail**: the M1 STOP gate (R1) and the .NET 8 stand (R9) are the new long poles; no compile/restart loop for the DCM (v r1-C1) and no capture campaign keep the likely value low |
| Down vs pre-plan | - | - | core unchanged and fully public API (design §0); the DCM needs no compile and no restart; the page write reuses `PageUpdateCommand` and the dependency add reuses `IPackageDependencyManager` (design §11) |

Net: the pre-plan's 12-20 h AI fits the minimal design A without grafts (design A §15: ~1,250 + 850 production lines)
and without the two-runtime evidence; the owner's acceptance ("works on .NET 8 and .NET Framework stands", "covered
by unit tests + an E2E/CAADT scenario") and the risk grafts are what move the AI figure to ~35 h without surprises and ~42-50 h with the realistic buffer.
The human figure stays near the pre-plan band because the new work is mostly agent work; the calendar leaves it once
the buffer is counted.

---

## 9. Unknowns that the figures assume away

| Unknown | Assumed here | Settled by |
|---|---|---|
| Which read-write .NET Framework stand runs M1-M10 and the E2E (stand `Creatio` is read-only in this research) | a disposable stand or stand `Creatio` opened for writes by the owner, one at a time (design §14.5) | owner, before step 4 |
| Core 8.3.4 has every public member the design uses | yes | WI-1 (a); R2 |
| A route-A DCM opens in the designer | yes (traced equal to route B, g2 §4) | M1; R1 |
| `IPackageSchemaDataCreator` binds into the requested package from a WCF call | yes (ClassFactory-bound on both hosts, r4 §4.2) | M6; R4 |
| A `StudioNet8` zip usable for provisioning exists when needed | 10.0.0.942 exists; a trunk zip needs `BuildProductNet8` (g5 §6) | owner, Q24; R9 |
| The owner's answers match the recommendations | yes | Q1-Q29; section 5 rows |
| 8 h per working day of attention (to compare hours with "days") | yes | - |
