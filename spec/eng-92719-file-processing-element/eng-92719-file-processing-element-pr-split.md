# PR split: ENG-95984 File process parameter type and ENG-92719 File processing element

**Summary.** In all three repositories the work is split into **one PR per Jira issue per repository**, with
no further split by layer or by size: ENG-95984 File process parameter type, ENG-96505 Element readiness and
object attachments mode, ENG-96506 Generated report + process parameter modes, and one NEW Sub-task that takes
SysFile attachment storage out of ENG-96505 Element readiness and object attachments mode (a second NEW Sub-task,
for the collection-mirror defect, exists only if stand measurement M3 confirms the defect). That is **4 package
PRs, 6 clio PRs** (two extra docs-only PRs, one per spec set: ENG-95984 File process parameter type and ENG-92719
File processing element) **and 4 knowledge PRs: 14 in total, or 17 with the conditional defect fix.** Each issue
ships as a triple - package PR, then the clio rebundle PR, then the knowledge PR - and the issues ship one after
another in the order PT -> OA -> RP -> SF (aliases in section 1), with at most one of our package PRs in human
review at a time while the next ticket iterates as a stacked draft. The critical path is the four serialized
package reviews (about 7-9 calendar days at the measured review medians; inference), each followed by a 30-71 minute clio and knowledge tail (measured). On
Jira: ENG-96505 Element readiness and object attachments mode does **not** functionally depend on ENG-95984 File
process parameter type (only the delivery order does); the ticket that ENG-95984 File process parameter type really
blocks is ENG-96506 Generated report + process parameter modes, which has no link today (section 12). No PR
combines two issues, and only a human merges.

The reasons are measured:
- package `main` is a release candidate at every moment;
- pushes dismiss approvals on the package repository;
- clio's guard tests bind a rebundle to its feature, so the two cannot be separated;
- PRs of 1,500-4,500 added lines wait as long as PRs of 4,500+ lines.

**Only a human merges.** The agent never merges into `main` or `master`, never enables auto-merge, and never
pushes to a PR with a live approval without asking first.

Sibling documents: [README](README.md) ·
[platform-reference](eng-92719-file-processing-element-platform-reference.md) ·
[serialization-capture](eng-92719-file-processing-element-serialization-capture.md) ·
[use-cases](eng-92719-file-processing-element-use-cases.md) · [traps](eng-92719-file-processing-element-traps.md) ·
[reuse](eng-92719-file-processing-element-reuse.md) · [decisions](eng-92719-file-processing-element-decisions.md) ·
[plan](eng-92719-file-processing-element-plan.md) · [test-plan](eng-92719-file-processing-element-test-plan.md) ·
[open-questions](eng-92719-file-processing-element-open-questions.md) ·
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) ·
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md).
D-numbers (D1-D29) and measurement ids (M1-M26) are the ones defined in [decisions](eng-92719-file-processing-element-decisions.md)
(M26 is the T-19 probe of [traps](eng-92719-file-processing-element-traps.md#t-19)).

Written 2026-10-01, read-only: nothing was built, committed, pushed, merged, or written to Jira or to the stand.
**Basis labels:**
- **measured**: observed by reading git, `gh api` or Jira on 2026-10-01, or recounted from delivery data;
- **source**: read in code or documentation (a runtime claim with this label is a hypothesis);
- **inference**: derived from the two above.

---

## 1. Names used in this document

| Alias | Jira issue | Type, parent |
|---|---|---|
| **PT** | ENG-95984 File process parameter type | Task, epic ENG-92704 Create BP via AI Toolkit |
| **FE** | ENG-92719 File processing element | Story, epic ENG-92704 Create BP via AI Toolkit |
| **OA** | ENG-96505 Element readiness and object attachments mode | Sub-task of FE |
| **RP** | ENG-96506 Generated report + process parameter modes | Sub-task of FE |
| **SF** | NEW: "SysFile attachment storage in the Process file element" (key assigned on creation) | Sub-task of FE |
| **MH** | NEW, only if M3 confirms defect H-1: "typeFromElement collection mirror leaves its items unbound" | Sub-task of PT |

In-flight work that shares files with this split:
- **CA** = ENG-99970 CAADT runs for BPMS Tools cost 6-15x other teams - find and close the gap;
- **SK** = ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17
  graph validator into the write path (ENG-88414). ENG-88414 is AI-driven application development;
- **CI** = ENG-92113 Deliver clioprocessbuiilder package - CI for adding package into clio release.

PR ids:
- `PK-` is a crt-process-builder PR (https://creatio.ghe.com/engineering/crt-process-builder, base `main`);
- `CL-` is a clio PR (https://github.com/Advance-Technologies-Foundation/clio, base `master`);
- `KB-` is a clio-knowledge PR (https://github.com/Advance-Technologies-Foundation/clio-knowledge, base `master`).

A **cut** is a rebundled CrtProcessBuilder archive under a claimed version number.

---

## 2. Verdict per repository

| Repository | Verdict | Reasons |
|---|---|---|
| **crt-process-builder** | **Split: 4 PRs, plus 1 conditional.** One PR each for PT, OA, RP and SF. MH is added only if M3 confirms H-1. | `main` is a release candidate: any rebundle by any team cuts from it, and CI (https://creatio.ghe.com/engineering/crt-process-builder/pull/83) would open a clio rebundle PR on every `main` build. So every merged package PR must leave a releasable product state. A split by layer fails that test (section 10.2). A split by size slows delivery here (section 3, row 9). A push dismisses approvals (section 3, row 1), so the PRs are reviewed one at a time. |
| **clio** | **Split: 6 PRs, plus 1 conditional.** Two docs-only PRs, `ENG-95984 File process parameter type` (CL-PT-DOC) and `ENG-92719 File processing element` (CL-DOC), the BMAD spec sets, plus exactly ONE rebundle PR per package PR. | Four guard tests bind the archive, the four pins, the `[RequiresPackage]` literals, the enforced-floor sentences, the capability-map literals and the e2e floor to one tree (section 3, row 5). So a rebundle cannot leave its feature PR, and two cuts cannot share one clio PR (the first cut would ship in no clio). The early "groundwork" clio PRs proposed during analysis prevent no silent failure. They are kept only as contingency X1 (section 11). |
| **clio-knowledge** | **Split: 4 PRs, plus 1 conditional.** One guidance generation per package PR, each merged after its clio PR. | Knowledge reaches every installed clio the moment it is published. So each generation must name a version that a merged clio bundles. The one knowledge-first merge in this epic shipped a floor that no clio bundled (https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/198) and needed a corrective PR (https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/201). |

**Why one PR per Jira issue:**
- one floor and one guide generation per blast radius;
- one revert path per issue;
- each issue moves to done on its own merge;
- no two FEATURE PRs in one repository share an issue, so the title rule `<KEY> <Jira title>` names one feature PR
  per issue and repository. Two clio PRs share a title by that same rule, and are told apart by branch: the
  docs-only CL-PT-DOC and CL-PT (both `ENG-95984 File process parameter type`); only if contingency X1 fires, the
  early budget PR (section 11) is a third. These are the sanctioned exceptions.

The split was chosen from three competing proposals:
- risk-first;
- throughput-first;
- reviewer-load-first.

Reviewer-load-first won, 25 of 30 points; risk-first scored 24 and throughput-first 19. The final split keeps
the reviewer-load structure. It takes the ordering and evidence rules from risk-first, and the data-driven
variant registry from throughput-first.

---

## 3. Evidence the split rests on

| # | Fact | Basis | Where |
|---|---|---|---|
| 1 | Package `main`: ruleset 82837 sets `dismiss_stale_reviews_on_push: true` and allows the merge methods `merge` and `squash`. Ruleset 82852 has Copilot review every push except on drafts (`review_draft_pull_requests: false`). Ruleset 1555232 requires `continuous-integration/jenkins/pr-head`, with the strict up-to-date policy. Ruleset 82837 covers the default branch, `branches/**`, `creatio-branches/**` and `release/**`, so `feature/*` bases are outside it. | measured 2026-10-01 | `gh api --hostname creatio.ghe.com repos/engineering/crt-process-builder/rules/branches/main` |
| 2 | clio `master`: no dismissal on push. Required checks, strict: Detect changes, Unit Tests, Integration Tests, Analyzer Tests, SonarCloud Code Analysis. Thread resolution is required. clio-knowledge `master`: no dismissal; one required check, "Producer contract suite", not strict. | measured 2026-10-01 | `gh api repos/Advance-Technologies-Foundation/{clio,clio-knowledge}/rules/branches/master` |
| 3 | Two of our package PRs in review at the same time stamped the same version twice: 1.6.6.42 (`8c3acf07` ENG-91844 Implement full parameter mapping (sources); `9eae8795` ENG-92711 Script task element) and 1.6.6.49 (`4442d802`, `c7415f67`). | measured | `git show <sha>:packages/CrtProcessBuilder/descriptor.json` |
| 4 | Open package PRs: https://creatio.ghe.com/engineering/crt-process-builder/pull/80 (SK, draft), https://creatio.ghe.com/engineering/crt-process-builder/pull/81 (CA, draft), https://creatio.ghe.com/engineering/crt-process-builder/pull/82 (ENG-95255 [Arch debt] Developer-Centric: make the cli-process-builder repository self-sufficient for a newcomer and an agent (ENG-92704); ENG-92704 is Create BP via AI Toolkit), https://creatio.ghe.com/engineering/crt-process-builder/pull/83 (CI). Their descriptor stamps (1.6.6.36, .39, .24, .24) are below `main`'s 1.6.6.54, so each one re-cuts when it merges. The highest stamp in history is 1.6.6.54; the reverted 1.6.6.900 probe stamp is ignored. | measured (the stamps come from the research checkout's refs; run `git fetch` before claiming a number) | `gh api ... pulls?state=open`; `git log --all -p -- packages/CrtProcessBuilder/descriptor.json` |
| 5 | Guard tests that bind a rebundle into one tree: `BundledArchive_ShouldCarryAtLeastEveryDeclaredRequirement` (:940), `ToolContractVersionLiterals_ShouldNotExceedTheBundledArchiveVersion` (:1335), `EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral` (:1504), `CapabilityMapVersionLiterals_ShouldNotExceedTheBundledArchiveVersion` (:1565). `ExpectedArchiveVersion = "1.6.6.54"` (:318). | source (read 2026-10-01) | `clio.tests/Common/BundledProcessBuilderPackageTests.cs` |
| 6 | The floor literals are `1.6.6.40` in `CreateBusinessProcessCommand.cs:240`, `ModifyBusinessProcessCommand.cs:196` and `ModifyProcessAsNewVersionCommand.cs:59`. | source (read 2026-10-01) | clio `clio/Command/` |
| 7 | TeamCity never runs the process-designer e2e: `baseFilter` is `TestCategory!=McpE2E.ProcessDesigner&TestCategory!=McpE2E.Manual`. A stand with an older package turns a new fixture into **Ignored**, not Failed (`Assert.Ignore`). | source (read 2026-10-01) | `clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:46`; `clio.mcp.e2e/Support/Mcp/ProcessDesignerE2EArrange.cs:73-93` |
| 8 | The ManagerMap arm list has no `fileprocessing`. Its suffix arm matches only tokens ending in `usertask`, so an older clio meeting the `fileprocessing` token returns `Unknown`, which is a hard `validate-process-graph` Error. | source (read 2026-10-01) | `clio/Command/ProcessModel/Schema.cs:1144-1149` |
| 9 | Size against review latency, over the package's merged feature PRs. 1,500-4,500 added lines: 12 PRs, median 48.7 h open, 0.75 dismissed approvals per PR. 4,500+ lines: 12 PRs (the `nitro/sprint-3-release` merge https://creatio.ghe.com/engineering/crt-process-builder/pull/73 excluded), median 50.5 h, 1.58 dismissals per PR. Under 1,500 lines: 4-20 h, depending on which chore PRs are excluded. | measured (recount 2026-10-01 of `research/prsplit/pkg_stats.tsv`) | delivery data |
| 10 | Delivery history: one package, clio and knowledge triple per ticket. Package merge to last merge took 30-71 min for ENG-92706 Send email element (custom message), ENG-96230 Collection process parameter type, ENG-99856 Sub-process element: support MULTI-INSTANCE (running the callee once per item of a collection), ENG-91844 Implement full parameter mapping (sources) and ENG-92711 Script task element. The one outlier is about 16 h, for ENG-96504 Read data element: collection mode. | measured | `research/delivery-surface-verification.md` C13 |
| 11 | How `main` was merged, over its 13 most recent first-parent commits. Merge commits: https://creatio.ghe.com/engineering/crt-process-builder/pull/72, https://creatio.ghe.com/engineering/crt-process-builder/pull/73, https://creatio.ghe.com/engineering/crt-process-builder/pull/74, https://creatio.ghe.com/engineering/crt-process-builder/pull/75, https://creatio.ghe.com/engineering/crt-process-builder/pull/76, https://creatio.ghe.com/engineering/crt-process-builder/pull/77, https://creatio.ghe.com/engineering/crt-process-builder/pull/78, https://creatio.ghe.com/engineering/crt-process-builder/pull/79 and https://creatio.ghe.com/engineering/crt-process-builder/pull/84. Squashes: https://creatio.ghe.com/engineering/crt-process-builder/pull/68, https://creatio.ghe.com/engineering/crt-process-builder/pull/69, https://creatio.ghe.com/engineering/crt-process-builder/pull/70 and https://creatio.ghe.com/engineering/crt-process-builder/pull/71. | measured | `git log --first-parent origin/main` |
| 12 | `ProcessMappingService.ResolveProcessParameter` matches process parameters by flat name only; element parameters go through `ResolveElementParameterForMapping`, which accepts dotted paths. So the Object variant's outputs (element parameters) can be mapped today, and process-parameter file paths cannot. | source | `PB/Mappings/ProcessMappingService.cs:221, 280, 425-435, 445` |
| 13 | Jira today: link 560203 says ENG-95984 File process parameter type **blocks** ENG-96505 Element readiness and object attachments mode. ENG-96506 Generated report + process parameter modes has no links. FE's only children are OA and RP, and PT has none. FE "is blocked by" ENG-91843 Add and modify process parameters, which is Closed. | measured 2026-10-01 | Jira |
| 14 | The package's CLAUDE.md defines no review gates; clio's AGENTS.md gates are applied to the package by convention. Gate 1 (before the PR opens) is "ALWAYS (comprehensive)". The size triage applies only to gate 2 (every new commit). | source (read 2026-10-01) | `pbm/CLAUDE.md`; clio `AGENTS.md` "Code review" |

---

## 4. The PRs

### 4.1 Identity and order

Merge order is global and runs top to bottom. Within a ticket the order is always package, then clio, then
knowledge. A human merges each row.

| Merge # | Id | Repo | Title (exact) | Branch | Base | Depends on (hard edges in section 7) |
|---|---|---|---|---|---|---|
| 0 | **CL-PT-DOC** | clio | `ENG-95984 File process parameter type` | `feature/ENG-95984-file-parameter-type-spec` | `master` | the owner decisions O-1..O-10 of the ENG-95984 File process parameter type plan; the M3 result, so MH's story rides in it if M3 confirms H-1 |
| 0b | **CL-DOC** | clio | `ENG-92719 File processing element` | `feature/ENG-92719-process-file-spec` | `master` | owner decisions (section 13); CL-PT-DOC merged, when both carry their analysis folders (E10) |
| 1 | PK-MH (cond.) | pkg | `<MH-KEY> typeFromElement collection mirror leaves its items unbound` | `feature/<MH-KEY>-mirror-item-binding` | `main` | M3 confirmed; owner chose a separate fix (D9); CL-PT-DOC merged (E9) |
| 2 | CL-MH (cond.) | clio | same title | same | `master` | PK-MH merged and tagged |
| 3 | KB-MH (cond.) | kb | same title | same | `master` | CL-MH merged |
| 4 | **PK-PT** | pkg | `ENG-95984 File process parameter type` | `feature/ENG-95984-file-process-parameter-type` | draft on PK-MH's branch while that PR is open, else `main`; retargeted to `main` before leaving draft | M6; M3 decided; PK-MH merged (if it exists); CL-PT-DOC merged (E9) |
| 5 | **CL-PT** | clio | `ENG-95984 File process parameter type` | same | draft on CL-MH's branch, then `master` | PK-PT merged and tagged |
| 6 | **KB-PT** | kb | `ENG-95984 File process parameter type` | same | draft on KB-MH's branch, then `master` | CL-PT merged; **must be published** before CL-OA (merge row 8) |
| 7 | **PK-OA** | pkg | `ENG-96505 Element readiness and object attachments mode` | `feature/ENG-96505-process-file-object-attachments` | draft on PK-PT's branch, then `main` | PK-PT merged **and CL-PT merged**; CL-DOC merged; M10, M11(a)(b), M14, M17-Q7 |
| 8 | **CL-OA** | clio | `ENG-96505 Element readiness and object attachments mode` | same | draft on CL-PT's branch, then `master` | PK-OA merged and tagged; **KB-PT published** |
| 9 | **KB-OA** | kb | `ENG-96505 Element readiness and object attachments mode` | same | draft on KB-PT's branch, then `master` | CL-OA merged |
| 10 | **PK-RP** | pkg | `ENG-96506 Generated report + process parameter modes` | `feature/ENG-96506-process-file-report-and-parameter` | draft on PK-OA's branch, then `main` | PK-OA and CL-OA merged; M1, M13, M15 |
| 11 | **CL-RP** | clio | `ENG-96506 Generated report + process parameter modes` | same | draft on CL-OA's branch, then `master` | PK-RP merged and tagged |
| 12 | **KB-RP** | kb | `ENG-96506 Generated report + process parameter modes` | same | draft on KB-OA's branch, then `master` | CL-RP merged |
| 13 | **PK-SF** | pkg | `<SF-KEY> SysFile attachment storage in the Process file element` | `feature/<SF-KEY>-process-file-sysfile-storage` | draft on PK-RP's branch, then `main` | PK-RP and CL-RP merged; M7, M8, M21, M23(a-d), M25 |
| 14 | **CL-SF** | clio | `<SF-KEY> SysFile attachment storage in the Process file element` | same | draft on CL-RP's branch, then `master` | PK-SF merged and tagged |
| 15 | **KB-SF** | kb | `<SF-KEY> SysFile attachment storage in the Process file element` | same | draft on KB-RP's branch, then `master` | CL-SF merged |

CL-PT-DOC and CL-DOC are numbered 0 and 0b: CL-PT-DOC must merge before PK-PT (or PK-MH) opens (E9); CL-DOC only
needs to merge before PK-OA opens (E7) and can merge in parallel with PT, after CL-PT-DOC when both carry their
analysis folders (E10).
Knowledge branches live in a dedicated worktree under `.worktrees/<task>/` (`kbm/AGENTS.md:26-30`). net472
package worktrees live on a short path, `C:/Projects/workspace/<short>`, because of MAX_PATH.

### 4.2 Version, size, tests, review gate

| Id | Version / rebundle | Floor raise | Estimated size (inference) | Tests (detail in the test plans) | Review gate |
|---|---|---|---|---|---|
| CL-PT-DOC | none | none | docs only | none | gate 1 comprehensive; gate 2 skipped (docs); gate 3 comprehensive |
| CL-DOC | none | none | docs only | none | gate 1 comprehensive; gate 2 skipped (docs); gate 3 comprehensive |
| PK-MH | descriptor restamp inside the PR; claims the first free number at or above 1.6.6.55 | - | under 1.5k lines (fast lane) | PBT `ProcessParameterServiceTests` mirror cases | gates 1 and 3 comprehensive; gate 2 single lens |
| CL-MH | rebundle (archive + 4 pins) | **yes**, to the final cut | small | pins, floor tests, one e2e | gates 1 and 3 comprehensive |
| PK-PT | restamp inside the PR | - | 2.3-3.2k lines | PBT D1-D8 lists, D5 regressions | gates 1 and 3 comprehensive (shared `ApplyMapping` funnel); gate 2 full on binder commits |
| CL-PT | rebundle | **yes** | medium | full `TestCategory=Unit` (4 modules) + NEW `FileParameterToolE2ETests.cs` | gates 1 and 3 comprehensive |
| KB-PT | libraryVersion above master at merge | - | small-medium (new guide) | pin, size and cross-reference tests | refute-first fact check |
| PK-OA | restamp inside the PR | - | 6-6.8k lines (largest) | PBT D10-D16, D19, D20 lists; registry test; tripwire | gates 1 and 3 comprehensive; gate 2 full on element commits |
| CL-OA | rebundle | **yes** | medium-large | full Unit + NEW `ProcessFileElementToolE2ETests.cs` | gates 1 and 3 comprehensive |
| KB-OA | libraryVersion bump | - | small | pins, size | refute-first fact check |
| PK-RP | restamp inside the PR | - | 2.5-4k lines | PBT report reader fake, Process-variant binder | gates 1 and 3 comprehensive |
| CL-RP | rebundle | **yes** (the clause keeps its byte length) | medium | e2e report + process-parameter modes; read-only describe of 4 product processes | gates 1 and 3 comprehensive |
| KB-RP | libraryVersion bump | - | small | pins, size | refute-first fact check |
| PK-SF | restamp inside the PR | - | 1.2-2k lines | PBT resolver in SysFile mode, scope, V9 | gates 1 and 3 comprehensive (runtime path not yet measured) |
| CL-SF | rebundle | **no** (an older server refuses SysFile loudly, as in the ENG-96230 Collection process parameter type precedent) | small | NEW `ProcessFileSysFileStorageToolE2ETests.cs` | gates 1 and 3 comprehensive; gate 2 single lens |
| KB-SF | libraryVersion bump | - | small | pins | refute-first fact check |

**Version numbers are rules, not projections.** Each cut claims a number before cutting, in the PR
description and in the shared cut claim (section 9). The claim is the first number at or above 1.6.6.55 that
is free in both histories. Re-cuts burn numbers.

The **floor** is the **final** cut of the clio PR, equal to `ExpectedArchiveVersion` when it merges. It is not
the first cut. Two reasons:
- a foreign archive stamped with our first number could satisfy that floor without the feature (fact 3 shows
  that number collisions happen here);
- a review round can add a write member after the first cut.

The guidance sentences therefore cite the final cut. Knowledge merges last, so that number is known when the
sentences are written. An earlier draft of D26 (not attached) fixed the numbers
1.6.6.55, .56 and .57; they would hold only if nothing else landed and no PR were re-cut, so the attached D26 states
the rule instead.

The package total, about 12-16k lines (12-17.5k with MH), is the main argument against one PR (section 10.1).

---

## 5. Contents per PR

Each package row lists only what the PR must contain. The design is in [plan](eng-92719-file-processing-element-plan.md) and the
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md); test cases are in
[test-plan](eng-92719-file-processing-element-test-plan.md) and the
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md).

### 5.1 MH (conditional): typeFromElement collection mirror leaves its items unbound

| Repo | Contents |
|---|---|
| pkg | `BindCollection(ProcessSchema, ProcessMappingDescriptor outer, IReadOnlyList<(string TargetItem, string SourceItem)> itemPairs)` on `IProcessMappingService` (`PB/Mappings/IProcessMappingService.cs:13-22`), with explicit pairs only. `ProcessParameterService.BindMirroredCollection` (`PB/Parameters/ProcessParameterService.cs:500-512`) calls it with the clone pairs, and comment K3 (`:440-447`) is corrected. The M3 evidence goes in the PR description. Restamp. Tests: extend `AddProcessParameter_ShouldBindMirroredCollection_ToSourceOutput` (`PBT/ProcessParameterServiceTests.cs:1760`). |
| clio | Rebundle; floor raise (every file in 9.2); McpCapabilityMap floor rows; an e2e where a Read data `typeFromElement` mirror is then described with both levels bound. |
| kb | The ENG-96230 Collection process parameter type mirror sentence in `guidance/mcp/guides/processes/parameters.md`, version-gated; a pin test. |

If M3 refutes H-1, this triple does not exist (contingency X4), and D9's parity change becomes one commit in PK-PT.

### 5.2 PT: ENG-95984 File process parameter type

| Repo | Contents |
|---|---|
| pkg | The PR description maps each commit to its D-id. The commits:<br>(1) D1 aliases, and the Binary/BLOB refusal, which names File and keeps the substrings `not supported` and `Binary`;<br>(2) D2 `FileCollection` write alias and describe predicate; D3 defaults;<br>(3) D4 `ResolveProcessParameterPath`, with both `ResolveProcessParameter` callers switched (`ProcessMappingService.cs:221, 280`); behaviour-neutral, pinned by the existing tests;<br>(4) D5 binder rules P1/P2/P2-MI/P3/R-M1/R-M2 in `ApplyMapping` (`:48-63`), and the P2 policy on `BindCollection` (the whole API if MH does not exist);<br>(5) D6 dotted-mirror refusal;<br>(6) D7 constants, `referenceSchema`, delete guard, `setParameter` shape;<br>(7) D8 nested-only listing (`PB/Describe/ProcessDescriber.cs:187-193`) and the element-source decode;<br>(8) D9 parity, if there is no MH;<br>(9) `docs/file-parameter-capture.md`;<br>(10) restamp, last.<br>The probe `AddProcessParameter_ShouldNameCollection_InUnsupportedTypeMessage` (`PBT/ProcessParameterServiceTests.cs:1670-1678`) moves from `Binary` to `Image` or `Color`. Otherwise it would stop exercising the generic message once Binary gets its own. It is the only automated guard of the refusal text, because the clio e2e that pins the same substrings is in `McpE2E.ProcessDesigner`, which TeamCity never runs (section 3, row 7). `:144-150` is unchanged.<br>The PR's "Visible behaviour changes" section lists: (a) rule P1 of D5 changes what an item-only `InputRecordCollection` mapping writes: when the source is an item of a collection, the parent `InputRecordCollection` is now bound to the source's collection too, so a multi-instance sub-process iterates once per source row instead of once; (b) describe reports `sourceElement`/`sourceElementParameter` for every single-token element-to-element value; (c) `type: File` and `type: FileCollection` are accepted, and Binary/BLOB are refused with a message that names File. |
| clio | Rebundle; floor raise; type lists in create and modify (+30 B / +28 B). Budget swap S1 + C6 (create) and S2 (modify), which must keep `this clio requires <floor>`, `multiInstanceOptions {...}` and the `stopped validating formulas` collapse clause, with no floor literal in the 60 characters before it (ENG-95984 File process parameter type plan CL-2). `ModifyProcessAsNewVersionTool.cs:83-88` floor parenthetical rewritten once. Describe decoded-source clause. **ManagerMap arm `"fileprocessing" or "processfile"`** (`Schema.cs:1144-1145`), with `ManagerMapResolveDataIdTests` cases and the update to `docs/knowledge/ProcessModel/subprocess-build-token-needs-a-managermap-arm.md`. McpCapabilityMap rows. The Binary e2e `[Description]` reworded (refused for good). PT's story status flips (in-progress, review; the done flip rides in CL-OA). The spec set is CL-PT-DOC's (section 5.7). NEW `clio.mcp.e2e/FileParameterToolE2ETests.cs` (`[Category(McpE2ECategories.ProcessDesigner)]`, `MinimumPackageVersion` = floor). Must stay green on the stand, each result recorded in the PR: `SubProcessMultiInstanceToolE2ETests`, `ModifyBusinessProcess_Should_RejectUnsupportedParameterType`, `RecordColumnSourceToolE2ETests`, `DescribeProcessToolE2ETests`. |
| kb | **Births the `process-files` guide** (no banner), with full registration: `bundle-source.json` entry, `requirements.itemIds`, `resourceUris`, `GuidanceMigrationTests.PostMigrationGuidance`, routing row, `ProcessGuideSet.GoLiveFloor`, a pin test. `parameters.md`: the type list plus the FileCollection exception, measured against the 345 chars left. `sub-process.md:133-137` and `sub-process-when.md:49-51` rewritten with a version gate, keeping the "send both" advice. |

### 5.3 OA: ENG-96505 Element readiness and object attachments mode

| Repo | Contents |
|---|---|
| pkg | `FileProcessingElementHandler` (token `fileprocessing`, alias `processfile`), registered before `UserTaskElementHandler` (`PBA:122-150`). **Variant registry** (`IFileProcessingVariant`, keyed by schema UId; Object `9387c794-8d84-5925-ab77-c47e7d876286` is its first entry). Five things read from the registry: `CanBuild`/`CanDescribe`, the generic-route refusal, the raw-`addMapping` refusal, the `FileProcessingFilterTarget` claim (priority 50, `PBA:173-178`) and the `ProcessFilterApplier` message. Config binder and applier (D12). `IAttachmentStorageResolver` + `IEntitySchemaHierarchyReader`, **legacy storage only**: SysFile sources and targets are refused with the D15 text. Record scope and the end-of-request empty-filter ledger (D16). Describe block: designer-built SysFile elements are decoded, and `setElement`/`setFilter` on them is refused until SF. D20 writes, contracts, `docs/process-file-element-capture.md`, restamp. Visible change: Object elements describe as `buildType: fileprocessing`; Report and Process elements stay generic user tasks. |
| clio | Rebundle; floor raise; descriptions (token, block clause with `get-guidance name=process-files`, `addElement`/`setElement` lists, describe clause). **In the same commit**, `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` is re-pinned to the PUBLISHED KB-PT generation. Typed `DescribedFileProcessing` in `clio/Command/ProcessModel/IProcessDescriber.cs`, with `[JsonExtensionData]` on every nested type, and the update to `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`. Permanent capability-probe guard on `ProcessElementUpdateDescriptor.FileProcessing`. `list-user-tasks` description: ObjectFileProcessingUserTask joins the dedicated-type exceptions with "no generic fallback", and the count word goes (TC-89). Prompts (decisions D25): `ListUserTasksPrompt` excludes the Object schema from the generic-route advice; `CreateBusinessProcessPrompt` gains the `fileProcessing` sentence; `ModifyBusinessProcessPrompt` the D12 merge rule; `DescribeProcessPrompt` the block name; all pinned (TC-90). NEW `ProcessFileElementToolE2ETests.cs` (with SysFile-refusal cases and no SysFile positives; the multi-instance-per-file case is the one OA case that needs PT's File parameter). Must stay green: `ListUserTasksToolE2ETests`, `ValidateProcessGraphToolE2ETests`, plus the PT list. |
| kb | `process-files` gains these sections: the element; legacy storage, plus "SysFile storage is refused in this version"; action per variant; consumers. `element-catalog.md`: buildable row, plus a generic-route sentence **worded for this cut**. Only the Object schema is refused; Report and Process-parameter elements are "not buildable yet; do not build them as a generic userTask". |

### 5.4 RP: ENG-96506 Generated report + process parameter modes

| Repo | Contents |
|---|---|
| pkg | **Two variant registrations, one commit group each**, so that contingency X2 can cherry-pick:<br>(a) Report (`c2bf0416-54c6-6c56-58e0-41162c7795f0`): `IReportTemplateReader` (ESQ on `SysModuleReport`, `UseAdminRights = false`), the `report` group, the D17 rules (MS Word only; FastReport and DevExpress refused), `report.recordId` scope, suffix, the `EnableReportFileProcessingUserTask` notice;<br>(b) Process parameter (`6c620dd2-026e-560c-489f-030c5be5f2c3`): the D18 `files` binder over `BindCollection`. A single-File source binds the nested level only, and **only if M1 passed**; `useInProcess` is refused; `saveTo` is required.<br>Restamp. Visible change: the shipped PrintInvoiceReport, PrintQuotationReport, PrintContractsReport and GenerateDNSRecordsSpecification describe as `fileprocessing`. |
| clio | Rebundle; floor raise (byte-neutral clause); typed `report`/`files` members, each with an overflow bag; `ListPrintablesEnvelope` gains `count` and `printables` (`clio.mcp.e2e/Support/Results/ListPrintablesEnvelope.cs:12-13`). e2e for the report and process-parameter modes, plus a **read-only** describe of the four product processes, which are never modified on the stand. `list-user-tasks` exceptions and `ListUserTasksPrompt` extended to the Report and Process schemas, and `CreateBusinessProcessPrompt` to the `report` and `files` groups (TC-89, TC-90 extended). The Process variant's `Files` mapping rules over the wire: TC-91 (= E2E-14a-d of the ENG-95984 File process parameter type test-plan, owner decision O-TP1). |
| kb | Report and process-parameter sections; `element-catalog.md` generic-route sentence extended to both schemas; the "Add data creates the report's data row" pattern. If the guide passes 80% of the size budget, a new `process-files-report` article with its own full registration. |

### 5.5 SF: SysFile attachment storage in the Process file element

| Repo | Contents |
|---|---|
| pkg | SysFile mode lifted in `Resolve`, per variant through the registry. The SysFile scope in the shape M7 captured (default: lookup InFilter on `RecordId`). V9 for the SysFile root (record-object sort columns refused). `RecordSchemaName` expectations. The M25 notice, only if M25 confirmed the read. PK-OA's refusals lifted. Capture doc checked against M21. Restamp. |
| clio | Rebundle; **no floor raise**; NEW `ProcessFileSysFileStorageToolE2ETests.cs`; PK-OA's SysFile-refusal e2e re-pointed to its positive form. |
| kb | Storage section rewritten: the SysFile hazards, and the refusal sentences removed behind a version gate. |

### 5.6 CL-DOC: ENG-92719 File processing element

- The BMAD spec set, named per AGENTS.md: `spec/prd/prd-<name>.md`, `spec/adr/adr-<name>.md` (which records the
  variant registry, owner decision O5), `spec/stories/story-<name>-N.md` for OA, RP and SF,
  `spec/test-plans/tp-<name>.md`, and `spec/sprint-status.yaml` entries in `ready-for-dev`.
- This analysis folder, if the owner wants it in the repository. The precedent `spec/eng-92707-sub-process-element/`
  is on clio master.

It merges before any FE code PR opens, so the stories exist first. Later status flips then edit a merged file
and never conflict with it.

**Status flips.** Each clio PR flips its own stories:
- `in-progress` when its draft opens;
- `review` when it leaves draft;
- `done`: this flip is not pushed after an approval. It rides in the next clio PR of the chain; the last stories of
  a chain (SF's, and any story with no later clio PR) ride in the next clio PR that touches
  `spec/sprint-status.yaml`, which by link L5 is the first clio PR of ENG-95985 Send email attachments.

Convention, recorded in each story file: `review` is set when the PR leaves draft, not when it opens as a draft. This
is a deliberate reading of the AGENTS.md sprint tracker rule for stacked drafts.

### 5.7 CL-PT-DOC: ENG-95984 File process parameter type

- The /bmad-spec set named in the ENG-95984 File process parameter type plan CL-0:
  `spec/prd/spec-eng-95984-file-parameter-type.md`, `spec/adr/adr-eng-95984-file-parameter-type.md` (D1-D9, D23,
  D25, D26), `spec/stories/story-eng-95984-file-parameter-type-1.md`, `spec/test-plans/tp-eng-95984-file-parameter-type.md`,
  and `spec/sprint-status.yaml` rows in `ready-for-dev`.
- The stories of PT's children: MH (if M3 confirmed H-1) and the three follow-up Sub-tasks.
- The `../eng-95984-file-parameter-type/` folder, if the owner wants it in the repository.

### 5.8 BMAD artifacts each issue needs before its package PR opens

| Issue | PRD / spec | ADR | Story | Test plan | sprint-status | Carried by | Before |
|---|---|---|---|---|---|---|---|
| ENG-95984 File process parameter type | `spec-eng-95984-file-parameter-type.md` | `adr-eng-95984-file-parameter-type.md` | `story-eng-95984-file-parameter-type-1.md` | `tp-eng-95984-file-parameter-type.md` | one row | CL-PT-DOC | PK-PT opens (E9) |
| MH (only if M3 confirms H-1) | PT's | PT's | its story, numbered in creation order | PT's (PU-53) | one row | CL-PT-DOC | PK-MH opens (E9) |
| PT follow-ups (Declared item shape; mirror allow-list; describe decode) | PT's | PT's | one each | own rows | one row each | CL-PT-DOC | their package PRs open |
| ENG-92719 File processing element (Story) | `prd-eng-92719-file-processing-element.md` | `adr-eng-92719-file-processing-element.md` (D10-D29, O5) | delivered through its Sub-tasks | `tp-eng-92719-file-processing-element.md` (with a TC-nn to TC-U-/TC-I- column) | Story row | CL-DOC | PK-OA opens (E7) |
| ENG-96505 Element readiness and object attachments mode | FE's | FE's | `story-eng-92719-file-processing-element-1.md` | FE's | one row | CL-DOC | PK-OA opens (E7) |
| ENG-96506 Generated report + process parameter modes | FE's | FE's | `story-eng-92719-file-processing-element-2.md` | FE's | one row | CL-DOC | PK-RP opens |
| SF | FE's | FE's | `story-eng-92719-file-processing-element-3.md` | FE's | one row | CL-DOC (SF is created on day 0) | PK-SF opens |
| FE side Sub-tasks (filter values; SerializeToDB) | FE's | FE's | `story-eng-92719-file-processing-element-4.md`, `-5.md` | own rows | one row each | CL-DOC | their package PRs open |
| X2 Sub-task (only on that contingency) | FE's | FE's | its story | its rows | its row | a docs-only clio PR titled with that Sub-task's key and title | its package PR opens |
| H-G3-1 report (only if M11 confirms it) | - | - | none: no PR in these repositories | - | - | - | - |

---

## 6. Sequencing diagram

```
DAY 0 - in parallel; nothing merges except CL-PT-DOC and CL-DOC
  Owner : decisions O1-O8 (section 13) + D2 D3 D5 D9 D10 D11 D14-D17 D22-D25
  Jira  : create SF (+ MH if M3 confirms); re-link (section 12); AC edits
  Stand : one at a time; every write needs the user's go-ahead; read-only probes first
          [M13][M6][M3]+baseline[M14][M19][M20][M24] .. on 1.6.6.54, BEFORE the first MH or PT cut is installed
                    (M6, M3 gate PK-MH / PK-PT; M14 gates PK-OA; M13 gates PK-RP; M19, M20, M24 gate no code)
          [M10][M11ab][M17-Q7] ...... gate PK-OA  (version-independent; may run during PK-PT review)
          [M1][M15] ................. gate PK-RP  (version-independent; may run during PK-PT / PK-OA review)
          [M7][M8][M21][M23a-d][M25] gate PK-SF  (user builds the probes; off the path)
          [M26] ..................... before CL-OA / KB-OA (gates the CL-OA record and the KB-OA sentence; no code)
  CL-PT-DOC: gate 1 -> review -> human merge   (before PK-PT / PK-MH opens, E9)
  CL-DOC: gate 1 -> review -> human merge      (before PK-OA opens, E7)

Per ticket: package PR (GHE) ==> clio PR ==> knowledge PR. Tickets run top to bottom.

MH? PK-MH  draft -> gate 3 -> review -> M(tag) ==> CL-MH M ==> KB-MH M
      |
PT  PK-PT  draft (stacked) .. final cut -> gate 3 -> review -> M(tag) ==> CL-PT M ==> KB-PT M + PUBLISHED
      |                                                                  |              |
      |                                          E4/E6: PK-OA merges after CL-PT   E5: before CL-OA merges
      v
OA  PK-OA  draft on PK-PT .. retarget .. cut -> stand -> gate 3 -> review -> M(tag) ==> CL-OA M ==> KB-OA M
      |
RP  PK-RP  draft on PK-OA .. retarget .. cut -> stand -> gate 3 -> review -> M(tag) ==> CL-RP M ==> KB-RP M
      |
SF  PK-SF  draft on PK-RP .. retarget .. cut -> stand -> gate 3 -> review -> M(tag) ==> CL-SF M ==> KB-SF M

"->"  next step inside one PR        "==>"  next PR of the triple        "M"  merged by a human
"|"/"v": the next package PR leaves draft only after the previous package PR merged (E3), and
         merges only after the previous clio PR merged (E4).
SIDE (in gaps only, never in a final review window of the main track): "Refuse collection
         parameters as filter values" as its own triple, while PK-OA is still a draft.
```

---

## 7. Hard edges and critical path

| # | Edge (B cannot happen before A) | Why | Basis |
|---|---|---|---|
| E1 | package PR tagged `crtprocessbuilder-<ver>` and merged -> its clio PR merges | the pins name the producing commit, and the tag keeps it reachable whichever merge method is used. Verify with `git ls-remote --tags origin` (local tags can differ from the remote). | source `docs/agent-instructions/bundled-packages.md:318-338` |
| E2 | clio PR merged -> its knowledge PR merges | the guidance states a floor that a merged clio bundles | measured precedent: https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/198 needed https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/201 |
| E3 | `pkg(n)` merged -> `pkg(n+1)` leaves draft | only one of our package PRs is in review at a time (NC4) | measured, section 3 rows 1 and 3 |
| E4 | `clio(n)` merged -> `pkg(n+1)` merges | `main` must never hold two cuts that no clio has bundled yet. Otherwise the next rebundle by anyone, including the CI automation, ships both under one floor. | inference from section 3 rows 4 and 5 |
| E5 | KB-PT **published** -> CL-OA merges | CL-OA writes `name=process-files` into a description. CI checks only the curated fixture, never publication, and a failed `update-knowledge` keeps serving the old library silently. "Published" means: merged, the release exists, and `info-knowledge` shows the library version. | source `WorkspaceTemplateGuidanceDriftTests.cs:540` |
| E6 | CL-PT merged (it carries the ManagerMap arm) -> PK-OA merges | an older clio meeting `fileprocessing` raises a hard validator Error (section 3 row 8). With the arm in CL-PT, it ships one release before any package emits the token. | source (read 2026-10-01; section 3 row 8) |
| E7 | CL-DOC merged -> any FE code PR opens (PT's equivalent is E9) | BMAD: no PR is opened before its story file exists | AGENTS.md |
| E8 | M6 -> R-M1 code in PK-PT; M3 -> MH or D9 parity; M1 -> the D18 single-file path; M7, M8, M21, M23, M25 -> PK-SF code | the code rests on runtime behaviour that has been traced in source but not yet measured | decisions D29 |
| E9 | CL-PT-DOC merged -> PK-PT (and PK-MH, if it exists) opens | BMAD: no PR is opened before its story file exists; PT's spec, ADR, story and test plan are in CL-PT-DOC | AGENTS.md; ENG-95984 File process parameter type plan CL-0 |
| E10 | CL-PT-DOC merged -> CL-DOC merges, when both carry their analysis folders | the FE folder links into `../eng-95984-file-parameter-type/`, which resolves on master only after that folder lands | source (link check of the folder) |

**Critical path:**
1. Owner decisions, M6 and M3 (day 0).
2. PK-PT review (1,500-4,500-line bucket, median about 2 days), then the CL-PT and KB-PT tail.
3. PK-OA stand run, gate 3 and review (4,500+ bucket, median about 2 days, more with review rounds), then the tail.
4. PK-RP (about 2 days), then the tail.
5. PK-SF (about 1-2 days), then the tail.

If every PR hits its bucket median, that is **about 7-9 calendar days of serialized review after day 0**
(inference). The medians include draft time, so they overstate pure review time.

- **Tails.** Each tail (clio plus knowledge after the package merge) takes 30-71 minutes, unless `main` moved and
  forces a re-cut (measured history, section 3 row 10).
- **MH** is off the path as long as it finishes before PK-PT is ready for review.
- **PK-OA development** overlaps PK-PT's review. It is on the path only if it takes longer.
- **The stand.** It carries one cut at a time, so PK-OA's first stand run waits until PK-PT's verification ends.
  A second disposable .NET Framework stand (O6, X7) removes that wait.
- **SysFile as its own ticket (SF)** adds about 1-2 days at the END. In exchange, the MIDDLE no longer waits on
  five measurements, three of which need the user to build designer probes.

**A window to keep short** (inference). Between the CL-OA merge and the KB-OA merge, a clio built from master
points at `process-files` before its element sections are published. The mitigation:
- have KB-OA approved before CL-OA is handed over;
- the human merges KB-OA right after CL-OA;
- no clio release is cut between the two merges.

The same applies to CL-RP and KB-RP, and to CL-SF and KB-SF.

---

## 8. What must land together, and what must not be combined

### 8.1 Must land together

| # | Together | What fails if they are split | Basis |
|---|---|---|---|
| T1 | In PK-PT: `FileCollection` (D2), dotted process paths (D4), the binder rules (D5), the constant refusals (D7) | an outer-only mapping onto a FileCollection saves green and every row is `{File: null}`; `addMapping value` stores text in a file input | source `ProcessInstanceParametersDataReader.cs:441-488` |
| T2 | PK-PT and the version-gated rewrite of `sub-process.md:133-137` / `sub-process-when.md:49-51` in KB-PT | the shipped guidance states the opposite of what the server does from that version on | D5, D25 |
| T3 | For each variant: identity, generic-route refusal, raw-mapping refusal, filter-target claim, describe | a claimed variant without its refusal keeps an unconfigured generic route; a refusal without the claim leaves no route at all. The registry makes this structural, and a registry test checks "all five or none". | D13 |
| T4 | In PK-OA: the element, the record scope, the empty-filter ledger (D16) | without them, an Object element copies 50 arbitrary files and reports success | source |
| T5 | clio rebundle: archive, 4 pins, 3 floor literals, enforced-floor sentences, capability map, description version literals, e2e `MinimumPackageVersion` | the guards in section 3 row 5 fail, or worse: a floor below the archive lets an older server drop the block silently | source |
| T6 | The first `name=process-files` in a description and the curated-fixture re-pin, in one commit (CL-OA) | `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` fails | source |
| T7 | One knowledge generation: article, `bundle-source.json` entries, routing row, `GoLiveFloor`, libraryVersion, pin tests | manifest, routing, migration or size tests fail, or an article is published unregistered | source |
| T8 | The descriptor restamp and the tag, inside the feature PR (the convention since ENG-96503 Read data element: count + aggregation) | `main` holds a descriptor version that does not describe its bytes | measured history |
| T9 | The typed describe DTO, its overflow bags, `ServerProcessDescriberTests` and the knowledge-record update (CL-OA) | AGENTS.md: a record whose `applies-to` file changes is updated in the same PR | source |

### 8.2 Must not be combined

| # | Keep apart | Why |
|---|---|---|
| NC1 | Two Jira issues in one PR | it breaks the title rule; two blast radii would share one floor and one guide version, and a defect in either would hold back both |
| NC2 | Two package cuts in one clio PR | the first cut would never be bundled, and a guide that cites it would name a version that no clio carries |
| NC3 | A knowledge generation and content of a later cut | it publishes vocabulary that no released clio and package pair accepts |
| NC4 | Two of our package PRs in human review at once | it produced the duplicate 1.6.6.42 and 1.6.6.49 stamps (measured), and it doubles the load on one reviewer pool |
| NC5 | The out-of-scope Sub-tasks (section 12.3) and the feature PRs | each changes behaviour outside the file feature, and needs its own verification and revert path |
| NC6 | A foreign team's rebundle and one of our stand-verification windows | it moves the stand or `main` under the verifier (the 1.4.0.63 vs 1.4.0.61 incident, `bundled-packages.md:170-178`) |

---

## 9. Cut, version and floor rules

### 9.1 Choosing and claiming a number

1. Go up from clio's `ExpectedArchiveVersion` (1.6.6.54 today). Check the candidate in both histories, using the
   `git log --all -p ... | sort -u -V` commands in `docs/agent-instructions/bundled-packages.md` (rules at
   :127-340). Ignore the 1.6.6.900 probe stamp. Never adopt another branch's height.
2. Claim the number BEFORE cutting, in the PR description and in **one shared cut claim**: a comment on
   ENG-92719 File processing element. Agree a merge window per ticket with the owners of CA, SK and CI (O8).
3. Cut with `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <package at PR head> -Version <claimed>`. An
   install reads the archive from the build output, so install from the output the script refreshed.
4. Every cut with new bytes takes a new number. Rebundle when the archive's behaviour changes, and once more at
   the end.
5. If two branches collide, the second to merge re-cuts from the merged tree and never picks a side.
   - If SK lands first, our next package PR teaches SK's key allow-list the new write members, and its read-back
     list the describe-only members.
   - If ours lands first, SK adds them on rebase.
6. If a foreign cut landed between two of ours, run the capability probe on a named-type field (for example
   `ProcessElementUpdateDescriptor.FileProcessing`) both ways: the old archive gives 0, the new one gives 1.
   ("A numeric floor cannot express a CAPABILITY", `bundled-packages.md`.)
7. Run the currency check before opening a PR and again before marking it ready:
   `git -C <pkg> log --oneline <ExpectedProducingCommit>..HEAD -- packages/CrtProcessBuilder/`.

### 9.2 Files that move with a floor raise (CL-MH, CL-PT, CL-OA, CL-RP)

All of them move to the PR's final cut, in the same commit as the archive:
- `CreateBusinessProcessCommand.cs:240`, `ModifyBusinessProcessCommand.cs:196`, `ModifyProcessAsNewVersionCommand.cs:59`,
  each with its comment block;
- `ProcessDesignerRequiresPackageAttributeTests.cs:67-68, 106`;
- the enforced-floor clause in the create, modify and modify-as-new-version descriptions;
- the `McpCapabilityMap.md` floor rows;
- the new e2e fixtures' `MinimumPackageVersion`.

What does not move:
- describe, list-user-tasks and validate-process-graph stay presence-only;
- no new WCF operation is added, so `ExpectedOperationContractCount` and `ExpectedAuthorizationGateCallSites` do
  not change.

### 9.3 Statements every clio PR description carries

- "MCP reviewed", naming the whole surface reviewed: tools, prompts (`ListUserTasksPrompt`,
  `CreateBusinessProcessPrompt`, `ModifyBusinessProcessPrompt`, `DescribeProcessPrompt`), resources and `clio/tpl`,
  with "no update required" for each item left unchanged (decisions D25). CL-PT also names `validate-process-graph`:
  its ManagerMap arm is inert until PK-OA, and its e2e evidence is TC-86 in CL-OA.
- "docs reviewed, no update required" for `help/en`, `docs/commands`, `Commands.md` and `WikiAnchors.txt`.
  These are MCP-only long-tail tools with no CLI verb.
- "ClioRing compatibility reviewed, no Ring-consumed contract changed", citing
  `clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95` (the catalog entry: Ring reads Name, Purpose and Destructive, and
  counts Resident for its summary line) and `clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157` (any
  non-destructive tool is dispatched generically through clio-run). Tool names, Destructive flags, resident
  membership and the Purpose leads are unchanged.
- The re-measured description-budget figures.
- The `Validated:` test command, run by `TestCategory` from bash with `< /dev/null`.

---

## 10. Why not one PR per repository, and why not finer

### 10.1 Not one PR per repository

| Argument | Detail |
|---|---|
| Size | The package diff would be about 12-16k lines, 12-17.5k with MH (inference, the sum of section 4.2). PRs of 4,500+ lines collect 1.58 dismissed approvals each (measured). https://creatio.ghe.com/engineering/crt-process-builder/pull/73 (+13,992 lines, 92 files) shows a diff of that size can only be rubber-stamped, which leaves the AGENTS.md final gate as the only real review. |
| Blast radius | One floor and one guide version would cover four different risks. PT changes what existing mapping operations write in every process. OA adds an element. RP re-routes describe and `setFilter` for four shipped product processes. SF is a runtime path with no measurement yet. A defect in any one would hold back all four, and revert granularity would be lost. |
| Long-lived branch | Four package PRs are open now, each with its own descriptor stamp (measured). Every conflict lands on one branch that cannot merge until everything is done, and each conflict forces a re-cut, often after approvals. |
| Tracking | Nothing reaches users until the end. Three Jira issues close on one merge, and the guide cannot state a released floor per sub-task. |
| It protects nothing | Per-cut scoping (D13) already makes every cut a releasable state. |
| Integration-branch variant | A `nitro/sprint-3-release`-style branch with sub-PRs merged into it escapes the dismissal ruleset, but it still ends in one big PR. That line once diverged 82/133 from `main`, and its version numbers crossed, so a higher number carried less functionality. That is the "newer is not contains" trap a floor cannot see. |

### 10.2 Not finer

Layer splits leave a `main` that is shippable, saves green, and fails at run time:

| Split | What `main` would ship | Basis |
|---|---|---|
| PT: type before binder | an outer-only FileCollection mapping that saves green, `{File: null}` rows, and an NRE in the consumer | source |
| OA: scaffold before configuration | an element with no `ResultActionType`; it runs as SaveToFiles and throws on `GetInstanceByUId(Guid.Empty)` | source |
| OA: element before filter policy | 50 arbitrary files copied, reported as success | source |
| Describe before write | an Object element that is described but no longer rebuilds | source |

Other finer splits also fail:
- **The rebundle cannot leave its feature PR**: the guard tests (section 3 row 5) fail if its parts land in
  different commits.
- **Size splits are slower here.** A 1,500-4,500-line PR waits about as long as a 4,500+ one: 48.7 h against
  50.5 h median (measured). So two serialized medium PRs take about twice the wall clock of one large one. A
  split pays only if one part drops into the fast lane (under 1,500 lines), isolates a risk-distinct change, or
  takes a blocking dependency off the critical path.
  - MH qualifies: fast lane, and it fixes a shipped defect.
  - SF qualifies: an unmeasured runtime path whose measurements need a human, taken off the path.
  - The two RP halves do not: both are medium. A second RP triple would cost one more cut, floor raise, stand
    reinstall and knowledge generation, and would give two feature PRs the same title.
- **An inert "element readiness" scaffold PR** (the throughput proposal) is rejected:
  - once SysFile is carved out, nothing is gained by building Object and Report as siblings;
  - the cost stays: an L-sized API reviewed with no consumer, two PRs with one title, and four PRs in review
    at once for a pool of 4-5 reviewers.
- **Early clio groundwork PRs** (budget swap, ManagerMap arm) prevent no silent failure:
  - the budget test fails loudly;
  - an older clio without the arm gives a loud validator Error.
  They are kept only as contingency X1.

---

## 11. Contingency splits (only on the named trigger)

| # | Trigger | Action |
|---|---|---|
| X1 | CL-PT cannot merge before an in-flight branch needs create/modify description bytes. Candidates: CA (+42 B on create), ENG-100153 O5: accept an object for create-business-process 'descriptor', and ENG-100154 O4: short-form get-tool-contract by default. | Move the S1/C6/S2 swap and the ManagerMap arm into an early clio PR `ENG-95984 File process parameter type` from `master`, branch `feature/ENG-95984-description-budget`. It keeps `this clio requires 1.6.6.40`, so the floor guard stays green, and it merges before CL-PT. This PR shares CL-PT's title by the title rule and is told apart by its branch (section 2). |
| X2 | PK-RP's Process-parameter variant fails its stand proof (the M4 chain) while the Report variant is ready | Cherry-pick the Report commit group into PK-RP. Move the Process-parameter group to a NEW Sub-task under FE, "Process parameter source of the Process file element". RP is re-scoped to the Report mode (owner decision). |
| X3 | The owner ranks custom-object attachments above reports (AI Toolkit apps create custom objects, which is the typical SysFile case; inference) | Swap the PK-RP and PK-SF slots. PK-SF then lifts SysFile for the Object variant only. PK-RP ships with SysFile targets refused through the registry, unless M23 (c)(d) passed before its code freeze. |
| X4 | M3 refutes H-1 | No MH triple; the D9 parity change becomes one commit in PK-PT. |
| X5 | PK-OA's diff exceeds about 8k lines at gate 3 | Look for a seam again. None is known: describe-first and element-before-policy are both unsafe (section 10.2). |
| X6 | CI (https://creatio.ghe.com/engineering/crt-process-builder/pull/83) merges | Our clio PR keeps its own cut during review. If the automated rebundle PR merges first, ours takes master's archive and pins on conflict, and re-cuts if its package is newer. An automated rebundle that ships our package before our clio PR does is safe, but un-advertised: the ManagerMap arm is already in CL-PT (E6), and describe passes an unknown block through `DescribedElement`'s overflow bag. |
| X7 | A second disposable .NET Framework stand becomes available | The next ticket's draft iterates there, and the E3 stand wait disappears. Merge order is unchanged. |
| X8 | Descriptor conflicts force a re-cut after approval more than once | Switch the remaining tickets to the pre-September convention: the restamp goes into a 2-line chore PR right after the feature PR merges (precedents: the chore package PRs https://creatio.ghe.com/engineering/crt-process-builder/pull/39, https://creatio.ghe.com/engineering/crt-process-builder/pull/49, https://creatio.ghe.com/engineering/crt-process-builder/pull/50, https://creatio.ghe.com/engineering/crt-process-builder/pull/51, https://creatio.ghe.com/engineering/crt-process-builder/pull/52, https://creatio.ghe.com/engineering/crt-process-builder/pull/59 and https://creatio.ghe.com/engineering/crt-process-builder/pull/67). |

---

## 12. Jira link corrections and new issues

### 12.1 Does ENG-96505 Element readiness and object attachments mode really depend on ENG-95984 File process parameter type?

**Functionally, no** (basis=source):
- The Object attachments variant needs no File process parameter. Its outputs (`ObjectFiles.File`,
  `CreatedObjectFileIds.Id`) are element parameters, and dotted element paths can be mapped today (section 3,
  row 12).
- Only one OA e2e case needs PT: the multi-instance sub-process per file, whose callee declares a File parameter.

**For delivery, yes.** PK-OA merges after CL-PT (E4, E6) because:
- CL-PT carries the ManagerMap arm;
- the three package PRs share `descriptor.json`, `ProcessDescriptorContracts.cs`, `ProcessMappingService.cs` and
  `ProcessDescriber.cs`;
- OA's two-consumer and per-file tests benefit from binder rule P1.

The ticket that PT **really** blocks is RP. Its Process-parameter variant takes a File or FileCollection source,
dotted process-parameter addressing and the two-level binder, and RP has no Jira link at all today.

### 12.2 Proposed link changes (owner decision D23)

| # | Today (measured 2026-10-01) | Proposed | Why |
|---|---|---|---|
| L1 | ENG-95984 File process parameter type **blocks** ENG-96505 Element readiness and object attachments mode (link 560203) | change to **relates to** | no functional dependency (12.1); the PR order in section 4 enforces the delivery order. If the team uses "blocks" for sequencing, keeping 560203 is acceptable. That is the owner's choice. |
| L2 | ENG-96506 Generated report + process parameter modes: no links | ENG-95984 File process parameter type **blocks** ENG-96506 Generated report + process parameter modes | the Process-parameter variant consumes File / FileCollection parameters |
| L3 | none | ENG-96505 Element readiness and object attachments mode **blocks** ENG-96506 Generated report + process parameter modes | PK-RP adds registrations to the handler and registry that PK-OA introduces (section 4.1, row 10) |
| L4 | none | ENG-96506 Generated report + process parameter modes **blocks** SF (reversed to "ENG-96505 Element readiness and object attachments mode blocks SF" under X3) | default slot order O3 |
| L5 | none | ENG-95984 File process parameter type and ENG-96505 Element readiness and object attachments mode **block** ENG-95985 Send email attachments | Process file to Send email attachments is the scope of ENG-95985 Send email attachments; it reuses `BindCollection` and needs a file source (D22) |
| L6 | ENG-92719 File processing element is blocked by ENG-91843 Add and modify process parameters (Closed) | no change | already satisfied |
| L7 | ENG-92725 Execute AI Intent element (BP generation) is Closed | no link | the Creatio.ai pattern leaves FE's acceptance criteria (D24); the guidance says "not buildable through this tool yet" |

MH needs no link: it is a child of PT, and PK-PT's dependency on PK-MH is in the PR table.

### 12.3 New issues (all of type Sub-task)

| Proposed title | Parent | When | Slot |
|---|---|---|---|
| SysFile attachment storage in the Process file element (**SF**) | ENG-92719 File processing element | day 0 (owner decision O1) | main track, after RP |
| typeFromElement collection mirror leaves its items unbound (**MH**) | ENG-95984 File process parameter type | only if M3 confirms H-1 (O2) | main track, first |
| Refuse collection parameters as filter values (D22) | ENG-92719 File processing element | day 0 | side lane, while PK-OA is still a draft (touches only `PB/Filters/ProcessFilterService.cs:525-591`) |
| Builder-made user tasks do not set SerializeToDB (D20) | ENG-92719 File processing element | day 0 | after PK-SF |
| Allow-list the data types a typeFromElement mirror may copy (D6) | ENG-95984 File process parameter type | day 0 | after PK-RP |
| Describe: decode process-parameter sources into re-appliable names (D8) | ENG-95984 File process parameter type | day 0 | after PK-RP |
| Declared item shape for Collection process parameters (D2) | ENG-95984 File process parameter type | day 0 | after PK-RP |
| Report the designer storage re-save defect to CrtProcessDesigner (H-G3-1; D15) | ENG-92719 File processing element | only if M11 confirms it | no PR in these repositories |
| Process parameter source of the Process file element | ENG-92719 File processing element | only on contingency X2 | replaces RP's Process half |

Process file to Send email attachments needs no Sub-task: ENG-95985 Send email attachments already owns that scope,
and link L5 is the only Jira action ([plan](eng-92719-file-processing-element-plan.md) section 5.4, C-5).

Every side-lane triple also restamps, so none of them may overlap a final review window on the main track.

### 12.4 Acceptance-criteria edits that follow from this split

The full AC correction list is D24 in [decisions](eng-92719-file-processing-element-decisions.md). The split
itself (O1) changes three things:
- **OA's SysFile row.** "Supported if M8 and M23 pass" becomes: *"Attachment storage in SysFile is not part of
  this sub-task. Such sources and targets are refused with a message, designer-built SysFile elements are
  described without loss, and SF delivers the support."*
- **The SysFile capture (M21).** The FE and OA AC rows that name it move to SF.
- **When each issue is done.** OA moves to done when KB-OA merges, RP when KB-RP merges, SF when KB-SF merges,
  and FE when all three are done (CL-DOC long merged). PT moves to done when KB-PT merges; MH, if it exists,
  is done before that. The follow-up Sub-tasks do not gate their parent. The three ENG-95984 File process parameter
  type follow-ups (after PK-RP) and the two FE side Sub-tasks (after PK-SF) are separate scope; PT and FE close with
  them open, and the owner confirms that the Jira workflow allows this (open-questions Q9).

---

## 13. Owner decisions this split needs

These are also collected in [open-questions](eng-92719-file-processing-element-open-questions.md).

| # | Decision | Recommendation |
|---|---|---|
| O1 | SysFile storage leaves OA and becomes the NEW Sub-task SF under ENG-92719 File processing element. Accept one interim release that refuses SysFile-mode sources and targets. | yes |
| O2 | If M3 confirms H-1, the NEW Sub-task MH under ENG-95984 File process parameter type lands first (D9) | yes |
| O3 | Slot order: RP before SF, or the reverse (X3) | RP first: one set of measurements and one SysFile lift for all three variants. Revisit if AI Toolkit custom-object attachments are the priority. |
| O4 | The FE spec goes in a docs-only clio PR (CL-DOC) | yes |
| O5 | The variant registry becomes OA's design, recorded in the ADR | yes |
| O6 | Ask for a second disposable .NET Framework stand for draft iteration | yes (it speeds things up; the plan does not depend on it) |
| O7 | Lower PRs with a stacked draft above them are merged with "Create a merge commit". If one is squashed anyway, the draft is rebuilt with a non-interactive `git rebase --onto origin/main <old lower tip>` while it is still a draft. | yes |
| O8 | A merge window per ticket, agreed with the owners of CA, SK and CI | yes |
| PT O-10 | ENG-95984 File process parameter type's BMAD set goes in the docs-only CL-PT-DOC (section 5.7), merged before PK-PT opens (E9); the alternative is an owner-approved exception | yes |
| D23 | Jira re-link L1-L5 (section 12.2) | yes |
| D27 | One PR per Jira issue per repository (this document) | yes |

---

## 14. Review, merge and stand protocol (per ticket, all three repositories)

1. **Open the drafts.** Package, clio and knowledge drafts open together, after gate 1 on each. Each draft is
   stacked on the lower ticket's branch in the SAME repository. Drafts get no Copilot review and sit outside
   the dismissal ruleset (section 3, row 1).
2. **Iterate in draft.** Gate 2 runs on every push:
   - skipped for restamp-, rebundle- and docs-only commits;
   - one lens for small single-module changes;
   - full for the shared mapping funnel and for element code.

   Cut for the stand only when the behaviour the e2e verifies changes.
3. **Retarget.** The lower ticket's package PR merges (merge commit), then its clio PR merges. Retarget to `main`
   / `master`. Do not merge the base in any other way before review.
4. **Final cut.** Run gate 3 over the whole diff and fix every Blocker and High. Then the FINAL CUT: restamp,
   tag, rebundle, stand reinstall, and the stand protocol below. The currency check must show only the restamp.
5. **Request review.** Clear draft on all three PRs and request review. The PR description carries:
   - the decision -> commit map;
   - the visible behaviour changes;
   - the claimed version;
   - the measurement ids, each with its basis;
   - the stand evidence (Passed / Ignored / Failed per fixture);
   - the commands run;
   - the follow-ups.
6. **One push per review round:** the fixes, one restamp and one rebundle together. Copilot findings join the
   same round.
7. **After the first approval,** push only for a Blocker or High, and tell the approvers first. Medium and Low
   findings get a reply or a follow-up Sub-task. Re-request review only from people without a live approval.
8. **Base drift.** "Behind base" or conflict events before approval are reported in the PR description, not
   resolved. The strict up-to-date checks are met at approval time, inside the agreed merge window. If `main`
   moved with a foreign restamp: re-cut once, re-run the capability probe and the e2e, and re-request the
   dismissed approvers.
9. **Gate 3 again** only if the head changed beyond a restamp or a rebundle since the gate 3 run in step 4.
10. **Hand over to the human merger** in this order:
   - the package PR (tag pushed and verified on the remote first);
   - the clio PR (updated with master, CI green);
   - the knowledge PR (libraryVersion above master at merge).

   Then check `info-knowledge`'s library version before any run that depends on the guidance.

**Stand protocol** (stand `Creatio`, core 10.1.37, .NET Framework):

Rules:
- Every write needs the user's explicit go-ahead.
- Schema writes and runs go one at a time; a parallel burst crashes the app pool.
- Cleanup uses `execute-dataservice-batch`, because the stand rejects HTTP DELETE.
- The stand carries ONE ticket's cut at a time. Reinstall it together with the rebundle, and tell every verifier.

After each cut:
1. Build the clio PR and run `install-process-builder` from the refreshed build output.
2. Check the instruments:
   - `list-packages` shows the cut;
   - one behaviour probe that only the new code can answer gives the new answer: PK-PT `addParameter type: File`;
     PK-OA a create with `fileProcessing` + `attachments`; PK-RP an unknown `report.printable` gets the D17 text;
     PK-SF a SysFile `saveTo` is accepted;
   - `get-tool-contract` shows the new vocabulary (the session's MCP clio can be stale).
3. Run the PR's e2e and the must-stay-green fixtures, one after another. The new fixtures must show
   **Passed with Ignored = 0**, because an older package on the stand turns them Ignored (section 3, row 7).
4. Run the per-ticket runtime proofs in [test-plan](eng-92719-file-processing-element-test-plan.md) and the
   [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md), and read
   `SysProcessElementLog`.
5. The user checks in the designer that builder-made artifacts open, and that a no-op save changes nothing (M22).

A failed runtime proof stops the package merge: the PR does not go to the human merger. The merge-gating proofs,
in short (basis = measured once run; the cases and recipes are in the two test plans):

| Ticket | Runtime proof on the cut | Pass condition | If it fails |
|---|---|---|---|
| MH | M3 re-run | every caller run shows only "M3 name set" rows (EV-4); the control matches | do not merge |
| PT | Item-only multi-instance (`InputRecordCollection.Name <- RD.ResultCompositeObjectList.Name`) over 3 contacts; baseline taken on 1.6.6.54 during the day-0 measurements | 3 iterations on the cut against 1 on the baseline (this is what KB-PT's version-gated sentence states) | hold CL-PT and KB-PT |
| PT | SC-0 first (read-only), then SC-4's PK-PT twin `UsrFpSc4Pt` (File, Variable and Out FileCollection), compared with the shipped `FileParameterProcess` and `MarkProcessesToCancel` captures | equal under the AC-8 comparison rule ([ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) section 4.2: N5, PT-a, N9, N15, N14, PT-b; anything else fails) | fix before merge |
| OA | Object SaveToFiles in dedicated storage (Account with 2 attachments copied to a Contact); Object useInProcess into a multi-instance sub-process per file; empty-filter save refused at the end of the request; a designer-built SysFile element described and its `setElement` refused | 2 `ContactFile` rows with `Type = File`; `CreatedObjectFileIds` has 2 rows; iterations = attachments; the refusal has `failedOperationIndex: null` | do not merge |
| OA | M22 / H-G3-1: the user opens a builder-made dedicated-storage Object element in the designer and does a no-op save | no `SourceDataEntitySchemaUId = AccountFile` afterwards, or the guide carries the D15 warning | per D15 |
| RP | Builder-made Object useInProcess -> FileCollection -> Process variant SaveToFiles (the M4 chain); single File (only if M1 passed); Word SaveToFiles on an Invoice with `report.recordId`; Report useInProcess into the Process variant, run to completion | 2 copies; 1 copy; 1 `.docx` named by the suffix rules; the temporary `SysProcessFile` rows are gone at completion (M16) | do not merge, withdraw the single-file path, or reword the notice; X2 if only the Process half fails |
| RP | Read-only describe of PrintInvoiceReport, PrintQuotationReport, PrintContractsReport, GenerateDNSRecordsSpecification | `buildType: fileprocessing`, block decoded, raw `parameters[]` lossless | do not merge |
| SF | Builder-made versions of M23 (a)-(d); Object reading a Freedom UI upload on a SysFile entity (the M8 shape) | rows carry `RecordId`, `RecordSchemaName`, `Type = File`; the user sees them in the Freedom UI attachment list | keep the refusal for the failing case and narrow SF |
