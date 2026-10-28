# ENG-95985 Send email attachments: work plan

Written 2026-10-28. The order of work, the work packages per repository, the PR split, the gates,
and the definition of done. It follows the answers the owner agreed on 2026-10-28 (decisions file); the design file says what each
component does. No product code has been written; nothing has been written to the stand, Jira, GitHub or GHE.

> **Drift note (step 0.4, 2026-10-28).** crt-process-builder `origin/main` is still `389198a0` (no commits since the
> basis); clio `origin/master` moved `358e9db52` -> `0b25e89e6` (ENG-104694 update-page resources-updated, ENG-104665
> upload-file: no file under `clio/Command/ProcessModel`, the process-designer tools, `clio/CrtProcessBuilder` or the
> curated knowledge names changed; `ToolContractPayloadBudgetTests` gained an update-page headroom guard and the
> compact index ceiling moved to 193*256 for upload-file, neither of which touches the seven process-designer
> contracts or their 19,000 B wire bundle); clio-knowledge `origin/master` moved `534e913` -> `a49f258` (ENG-104665:
> `core-rules-details` and `core-rules` only; `libraryVersion` is now 1.15.174, so KB-4 must go above it and above
> every open branch claim). Nothing in the drift changes the plan.

Basis: crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125), clio `origin/master` `358e9db52`,
clio-knowledge `origin/master` `534e913`, stand `Creatio` read-only on 2026-10-28.
Paths: `PB/` = `packages/CrtProcessBuilder/Files/src/cs/`, `PBT/` = `tests/UnitTests/CrtProcessBuilder.Tests/`,
`CLIO/`, `KB/` = repository roots.

## 0. Critical path

```
Q1-Q20 answered ──> EM1 designer capture ──> package slice A (refactors) ──> slice B (slots + block)
      │                                                                          │
      └──> BMAD docs PR (clio) ── merged before the package PR leaves draft      v
                                                     experimental cut, install ──> EM2 + EM3 (STOP gate)
                                                                                    │
                       slice C (guards, describe, notices, parity) <───────────────┘
                                   │
                   gate 1 ──> claimed cut, install ──> EM4, EM4c, EM5, EM8, EM10, EM11, EM13, EM15
                                   │
                     final cut, install ──> EM2 re-run, EM7 (Q17: once the owner says mail sending on `Creatio` works)
                                   │
                     KB PR (article, fact-check) ──> clio PR (rebundle, keys, floors, descriptions, e2e), draft
                                   │
                     gate 3: package + KB ──> ready, reviewers (on the owner's ask) ──> human merge: package, then KB
                                   │
                     KB generation published ──> clio re-pin (CL-6), clio gate 3 ──> ready, reviewers ──> human merge
```

The STOP gate is the refinement's residual risk: nothing yet proves that the platform's pre-save gate and the
designer's files control accept a server-built nested slot. At the STOP gate EM2 runs its save, pre-save-gate and
stored-bytes steps, and EM3 runs in the designer; the describe steps of EM2 (round trip, stale-parent issue) run in
the EM2 re-run on the final cut, after P-C3. If EM2 or EM3 fails, work stops, the cause goes to the owner, and the
estimate is redone before slice C starts.

No package code is written before the SPEC and the ADR of the BMAD docs PR exist on its branch (AGENTS.md: no code
until the ADR exists); they need not be merged for slice A to start.

## 1. Phase 0: decisions and day 0

| Step | What | Who | Output |
|---|---|---|---|
| 0.1 | Answer Q1-Q20 | owner | done 2026-10-28: all twenty agreed and recorded in the decisions file (Q9, Q16, Q17 changed from the first recommendation) |
| 0.2 | Jira AC wording (Q19) | owner, by hand | AC text; the HeroCoders checklist stays intact |
| 0.3 | Claim comment on ENG-95985 (Q13): an empty claim table; every later number is claimed there after checking the tags of both repositories | lead, on the owner's yes | comment id recorded in the plan |
| 0.4 | Drift check: `git log 389198a0..origin/main -- PB/Email PB/Mappings PB/FileProcessing PB/Elements/SendEmailElementHandler.cs PB/Graph PB/Design` and the clio/KB equivalents; re-read anything that moved | lead | a drift note at the top of this file |
| 0.5 | EM0 completion: platform version of stand `Creatio`, the Invoice printable, an email template to reuse | lead, read-only | measurements file |
| 0.6 | EM1 designer capture (Q18) | lead in Claude in Chrome, owner logs in; owner's yes for the writes | capture metadata + resources, pattern-B rows |

## 2. Phase 1: BMAD docs PR (clio, docs-only)

Branch `docs/ENG-95985-send-email-attachments-spec` from `origin/master`; PR title `ENG-95985 Send email attachments`
(Jira title verbatim). Draft until the owner approves the content.

| File | Content |
|---|---|
| `spec/prd/spec-eng-95985-send-email-attachments.md` | the feature as a SPEC (small feature, one story) |
| `spec/adr/adr-eng-95985-send-email-attachments.md` | Q1, Q4, Q6, Q8, Q11 as decisions with rejected alternatives |
| `spec/stories/story-eng-95985-send-email-attachments-1.md` | one story, tasks P-A..P-C, KB, CL; DoD = section 9 |
| `spec/test-plans/tp-eng-95985-send-email-attachments.md` | from the test-plan file |
| `spec/sprint-status.yaml` | the story row as `in-progress` (implementation has started by the time this PR merges, so `ready-for-dev` is never true for it), `blocks:` links fixed; the inherited flip of `story-eng-92719-file-processing-element-3` with only its really done boxes. The clio feature PR flips the row to `review` when it opens; `done` is flipped in the next clio PR that touches the tracker after the last merge, with only the really ticked DoD boxes |
| `spec/eng-95985-send-email-attachments/` | this folder, after a grep for the stand address (must be 0). Before the commit: drop the "Local, untracked" line from each header, replace absolute local paths with repository-relative ones, and mark the research citations (`R/`, r0-r10) as local notes that are not in the repository. `README.md` keeps its name, as in the committed `spec/eng-92707-sub-process-element/` and `spec/eng-95891-formula-expressions/` |

Review: gate 1 is the full 3-lens fan-out over the complete diff (quality, correctness against the code,
consistency), as AGENTS.md requires before every PR; gate 2 skips docs-only commits. It merges before the package PR
leaves draft. Pushing the branch and opening the PR wait for the owner's word.

## 3. Phase 2: package (crt-process-builder)

A short-path worktree (net472 MAX_PATH), branch `feature/ENG-95985-send-email-attachments`
from `origin/main`. The worktree has no `.application`; junction it from the main checkout before the first build
(`docs/running-tests-outside-the-main-checkout.md`). The local core-bin is newer than CI's 8.3.4: keep the FileLocator
type UId as a literal in tests, as `PBT/FileProcessParameterTests.cs` does.

Validation for every commit: `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf`
and `-c dev-n8`; every new fixture `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`.

### 3.1 Slice A: behaviour-neutral refactors

| Task | Component | Done when |
|---|---|---|
| P-A1 | C2: lift `PlanFiles` / `FilesBinding` / `FileItemOf` into `FileSourcePlanner` behind `IFileSourcePlanner` (DI) (label, member path, target names, consequence sentence and the write entry as parameters: raw `ApplyMapping` for `files`, the block entries for a slot, design 4.2); `ProcessFileProcessingBinder` calls it | every existing `FileProcessing*Tests` passes unchanged; a characterization test pins each `files` message |
| P-A2 | C7 part 1: generalize `ProcessFileProcessingFilesBinding.Read` and the issue texts of `ProcessFileProcessingDescriber` over (collection, item) names | the `files` describe tests and the Process parameter notice-rule tests pass unchanged (`ProcessFileProcessingNoticeRule` reads the same binding, `PB/FileProcessing/ProcessFileProcessingNoticeRule.cs:38`) |

One commit each. Gate 2: single combined lens (refactor, one module).

### 3.2 Slice B: slots and the block

| Task | Component | Tests (test-plan ids) |
|---|---|---|
| P-B1 | C1 contract: `SendEmailAttachmentDescriptor`, `SendEmailDescriptor.Attachments`, describe types; wire-contract rows | wire contract |
| P-B2 | C3 `EmailAttachmentSlot` + `IEmailAttachmentSlotWriter` (find, create with explicit stamps and `In`, remove with `Parameters.Remove` after the dependency check, lowest free number over every parameter of the element); DI registration. Whether a removed slot's resource keys survive is settled by EM1/EM2 (design 4.3) | slot shape, stamps on a schema with a REAL UId, numbering (a non-slot `Attachments1` blocks its number) |
| P-B3 | C4 `ApplyAttachments`: plan all entries before the first write, reconcile (Q4) with the dependency check before a removal, bind via the block form of `BindCollection` / `ApplyBlockMapping` (the `fromBlock` argument lands here, the slot guard in P-C1); `EnsureConfiguresSomething` counts attachments; refusals of design §5.1; Q16: `email.attachments` on a multi-instance Send email (`MultiInstanceOptions` set on the activity; `CollectionBindingRules.IsMultiInstance` answers for a Sub-process only, `PB/Mappings/CollectionBindingRules.cs:194-195`) is refused: "attachments of a multi-instance Send email are not supported through this tool"; Q20: track every Process file source in the filter ledger, and let `ObjectFileProcessingEmptyFilterRule` refuse an unscoped element read by a Send email slot whatever its action (the Report rule already refuses any unscoped element), judged by `FileProcessingFilterLedger.EnsureSelectingFilters` at the end of the request; designer display values on slots | sources x4 kinds, replace, refusals, untouched-after-refusal, multi-instance Send email refused with nothing written, unscoped Object and Report sources refused at the end of the request (TC-U-78, 78a, 78b), four mode combinations, display values on slots only; the version clone keeps the slots owned (TC-U-71, design 3.3) |
| P-B4 | C9 comment fixes (`CollectionBindingRules` Creatio.ai promise, recipient numbering comment) | - |

Then the EXPERIMENTAL CUT (section 6), installed on stand `Creatio` with the owner's yes, and EM2 + EM3. Record the
result in the measurements file. Stop on failure (section 0).

### 3.3 Slice C: guards, describe, notices, parity

| Task | Component | Tests |
|---|---|---|
| P-C1 | C5 guards in `Plan` (onto a slot under `!fromBlock`, keyed on the name) and in `ResolveStructuredSource` (from a slot); `IProcessMappingService` change documented | raw `addMapping`, a non-block `BindCollection` and build `mappings[]` onto a slot refused; the block's own writes pass; another user task's `Attachments1` unaffected (PU-42 mirror); describe's generic `parameters[]` offers no names for slots |
| P-C2 | C6 order checks: build-path declaration-order refusal; flow-reachability warning on build and modify | later-declared source refused; earlier accepted; unreachable source warned on both paths |
| P-C3 | C7 part 2: `DescribeAttachments`, read-back members, issues (a multi-instance Send email is reported as found, with an issue, Q16), `configured` | round trip build -> describe -> replay writes nothing; one test per issue shape, including an attachment that reads an unscoped source (TC-U-78c) |
| P-C4 | C4 notices of design §5.2 that remain (single File, expression, the same source twice, removal; the reachability warning comes with P-C2). Q9: no feature reader and no feature-off notice | each notice text; when each fires (every entry of a sent list, kept or new; per removed slot; none when `attachments` is absent) |
| P-C5 | Parity: pattern A (3 corpus processes committed under `PBT/Fixtures/SendEmail/` with PROVENANCE SHA pins and a `.gitattributes` `-text` line), pattern B (EM1 rows), pattern C (writer JSON diff) | parity suite |
| P-C6 | C10 capture doc (`docs/send-email-element-capture.md`, attachments section with EM1/EM2 results) | - |

Gate 1 before the PR opens: full 3-lens fan-out (quality, performance+correctness, security), because the shared
mapping service changes. Blocker/High fixed before the draft PR opens. Pushing the branch and opening the draft PR
wait for the owner's word. The PR stays draft through the cuts.

### 3.4 Package PR

GHE `engineering/crt-process-builder`, base `main`, title `ENG-95985 Send email attachments`. Body: what changed, the
EM results with dates, the claimed cut, the test counts on dev-nf and dev-n8, the merge order, "draft until the BMAD
PR merges". No reviewers until the owner asks (owner rule).

## 4. Phase 3: knowledge (clio-knowledge)

A short-path worktree (the `KB/` of the other files of this folder; reuse after resetting to a
fresh `origin/master`), branch `feature/ENG-95985-send-email-attachments`. Pushing the branch and opening the draft PR
wait for the owner's word.

| Task | What |
|---|---|
| KB-1 | New article `guidance/mcp/guides/processes/send-email-attachments.md` (`process-send-email-attachments`), content per design §7, the cut written as the real floor (no TBD at merge) |
| KB-2 | `files.md`: split the "not buildable here yet" sentence; `ProcessFileGuidanceTests` pin updated in the same commit |
| KB-3 | `send-email.md` owned-scope line + pointer; `element-catalog.md` Send email row; `process-modeling.md` route line paid by a swap (1 B headroom); `routing.md` row `name=process-send-email-attachments` beside the Send email rows (no swap needed) |
| KB-4 | `bundle-source.json`: resource with NO `legacyUris` (follow `process-files-parameter`, not `process-send-email-template`), `requirements.itemIds` and `resourceUris`, the `process-send-email` AND `process-modeling` entry descriptions, `libraryVersion` bumped above master and every open branch claim, re-checked at merge. No `sequence` field is written: the sequence is derived from `libraryVersion` at build time, and `GenerationSequenceTests.RepositoryManifest_ShouldNotDeclare_AnAuthoredSequence` refuses an authored one (`KB/CONTRIBUTING.md:166-177`; the Producer contract workflow step, not a test, compares against the base branch) |
| KB-5 | Tests: `SendEmailGuidanceTests` (contract, order rule, the feature troubleshooting line of Q9, retired-claim sweep), `ProcessGuideCrossReferenceTests` (routing), `GuidanceMigrationTests` (`PostMigrationGuidance` gains the name), size gates, manifest |
| KB-6 | Fact-check gate: one refute-first agent per claim cluster, given the source paths, forbidden to cite `docs/knowledge/` or this spec folder as evidence; every refutation re-checked by hand |

## 5. Phase 4: clio

A short-path worktree (reuse after resetting to a fresh `origin/master`), branch
`feature/ENG-95985-send-email-attachments`. Components K1-K12 of the design. Pushing the branch and opening the draft
PR wait for the owner's word.

Before CL-1 and CL-3, read `CLIO/docs/agent-instructions/bundled-packages.md`; before any clio edit, read
`CLIO/docs/knowledge/ProcessModel/` and `CLIO/docs/knowledge/McpServer/`. The PR keeps `CLIO*` diagnostics clean in
modified files and carries `///` docs on the new public members (`DescribedEmail.Attachments`, the K7 types). CL-8
also updates or deletes the records whose `applies-to` paths `make check-knowledge` reports as touched.

| Task | What | Guard that bites |
|---|---|---|
| CL-1 | Rebundle the final cut (`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <pkg> -Version <claimed>`), pins in the same commit | `BundledProcessBuilderPackageTests` |
| CL-2 | K2 key check + tests | `KeySets_Should_EqualTheMembersOfTheBundledServerContracts`, `ReadBackFields_Should_BeDescribeOnlyMembers` |
| CL-3 | K3 floors + pins | `ProcessDesignerRequiresPackageAttributeTests`, description pins |
| CL-4 | K4/K5 descriptions and prompts; re-measure, with the ceilings lowered on the branch, the three contracts AND the seven-contract wire bundle held to 19,000 B (`MaxInlineWireReplyBytes`; its recorded 18,815 B is stale); swap per Q12 (no ceiling raise) | `ToolContractPayloadBudgetTests` (incl. `GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether`) |
| CL-5 | K6 typed describe, K7 read-back check | `ServerProcessDescriberTests`, `EmailBlockExpectationTests` |
| CL-6 | K8 capability map, K9 curated re-pin. The clio PR stays draft until the KB PR merges and its generation is published; then the re-pin, clio gate 3, and only then reviewers. If KB publishes after the clio PR is approved, the re-pin goes into a separate PR instead of a push | `WorkspaceTemplateGuidanceDriftTests` |
| CL-7 | K10 e2e `SendEmailAttachmentsToolE2ETests`, run on stand `Creatio` on net10 and net8, Ignored = 0 | stand run recorded in the PR |
| CL-8 | K11 knowledge records (only facts the code does not say) | `make check-knowledge` (advisory) |

Validation: `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null` (more than three
modules change: full unit suite). PR body statements: "MCP reviewed" with the surface list; "docs reviewed, no update
required" for help/docs/Commands.md/WikiAnchors; "ClioRing compatibility reviewed, no Ring-consumed contract changed"
with the inspected Ring paths; the re-measured budgets; the `Validated:` command.

## 6. Cuts, installs, merge order

- Claim before stamping: check `git ls-remote --tags` of crt-process-builder and the version clio bundles; claim the
  first free number in the ENG-95985 claim comment (Q13); then cut. Each claim-comment edit (a Jira write), each tag
  push and each branch push happens only on the owner's explicit yes, one step at a time.
- Cut procedure: run the rebundle script from the clio checkout BEFORE committing the restamp (the script refuses an
  already stamped descriptor); commit the restamped `descriptor.json` in the package; tag the pre-restamp producing
  commit `crtprocessbuilder-<version>` and push the tag; rebuild clio afterwards (an install reads the archive from the
  build output, and a running e2e locks `clio/bin`).
- Expected cuts: 3-5: experimental (slice B), mid (claimed after gate 1), final, plus a review-round or foreign-cut
  re-cut (estimate 4.2). Superseded numbers are marked in the claim comment, never reused.
- Every install on stand `Creatio` needs the owner's explicit yes: it restarts the site for every user. A reviewer
  whose clio bundles a cut NEWER than the stand is refused by convergence until the stand is brought up
  (`CLIO/clio/Common/BundledPackageConvergence.cs`: no refusal when the installed version is at or ahead of the
  bundled one); a stand ahead of a reviewer's clio is not refused.
- Merge order (human merges only): package (tag on the remote) -> knowledge (publishes) -> clio. The BMAD docs PR merges
  first of all. Approvals are kept: no push after an approval without asking (a GHE push dismisses approvals).

## 7. Review gates (AGENTS.md)

| Gate | When | Scope |
|---|---|---|
| 1 | before each PR opens | every PR: full 3-lens fan-out over the complete diff; KB: 3 lenses over article and tests plus the KB-6 fact-check; each review states in the PR thread which tier ran and why |
| 2 | each commit pushed to an open PR | skip for restamp/rebundle/docs; one lens for small single-module diffs; full for mapping-service changes |
| 3 | before ready-for-review | one comprehensive adversarial review per PR over the whole diff |

## 8. Stand use and cleanup

- Every write (designer capture, builder runs, installs, test data, the real mail EM7 sends) needs the owner's yes,
  one step at a time. Schema writes run strictly one after another.
- Cleanup goes through `execute-dataservice-batch` (the stand answers HTTP DELETE with 405, measured 2026-09-27,
  measurements M-16) and `clio delete-schema <name>
  --remote -e Creatio --timeout 600000`; the measurements file keeps the leftovers register. Cleanup is proposed at
  the end, and runs only on the owner's yes.

## 9. Definition of done

| AC | Done when |
|---|---|
| AC1 | `email.attachments` builds slots in the designer's shape; describe returns the list in the write shape; round-trip test green; EM2 + EM3 passed |
| AC2 | the three Process file variants (EM4) and a process FileCollection passed from a parent process (EM4c) build and run; one collection feeds two Send email elements (EM11) |
| AC3 | two or more slots, and a slot with several files, attach every file (EM4) |
| AC4 | manual+custom (EM4), manual+template (EM5), and auto+custom, auto+template and the Create activity variant with its `ActivityFile` rows by real delivery (EM7), all on stand `Creatio` after the owner configures mail sending there (Q17) |
| AC5 | every refusal of design §5.1 has a unit test, and the e2e refusal test shows nothing was created |
| AC6 | the article states both order rules with EM10's measured result; build-path guard tested |
| AC7 | parity patterns A, B, C green against the corpus and EM1; tests, docs, MCP surface updated; e2e on stand with Ignored = 0 |
| general | every TC-U, TC-P, TC-C, TC-I and TC-K row of the test plan implemented and green (TC-U-42 and TC-U-48 are removed rows, Q9; TC-K-12 is the fact-check gate; TC-K-13 follows EM8) (package on dev-nf and dev-n8 plus the first CI run of the draft package PR; clio `TestCategory=Unit` full suite; the KB test project); gates 1-3 passed; KB fact-check passed; the clio PR body carries "MCP reviewed" (tools, prompts, resources, `clio/tpl`), "docs reviewed, no update required", "ClioRing compatibility reviewed, no Ring-consumed contract changed" with the inspected paths, the `Validated:` filter and the re-measured budgets; CL-8 knowledge records in the clio PR and the `make check-knowledge` report read; the tracker row moved as section 2 says; story DoD boxes ticked only when true; stand leftovers listed |

## 10. Risks

| Risk | Effect | Mitigation |
|---|---|---|
| EM2/EM3 fail (server-built slot refused, or designer rewrites it) | estimate moves; design may change | STOP gate right after slice B, before the larger slices |
| clio description budget (2-3 B free) | a reviewer must agree to the swapped text | Q12 decided up front; measure on the branch first |
| mail sending on stand `Creatio` not yet configured when EM7 is due | auto-mode delivery unproven, AC4 open | Q17: the owner configures mail on stand `Creatio` before EM7; EM7 waits for the owner's word that it works |
| another ticket cuts on `main` during review | re-cut after approval (approvals dismissed) | claim early; X8 fallback (merge the feature PR, restamp in a 1-file PR) |
| KB `libraryVersion` collision with a parallel KB PR | re-pin round | pick above every open branch claim; re-check at merge |
| shared mapping service change (C5) | regressions in other elements' raw mappings | the guard keys on Send email identity + slot predicate; PU-42 mirror test; full regression suites |
| local core-bin newer than CI 8.3.4 | green locally, red in CI | literal type UIds in tests; first CI run on the draft PR |
