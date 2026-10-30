# Story 1: Send email attachments through the process builder

**Feature**: eng-95985-send-email-attachments
**Jira**: ENG-95985 Send email attachments (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-95985-send-email-attachments.md](../prd/spec-eng-95985-send-email-attachments.md) - AC1-AC7
**ADR**: [adr-eng-95985-send-email-attachments.md](../adr/adr-eng-95985-send-email-attachments.md) - D1-D5 (Q1, Q4,
Q6, Q8 + Q10, Q11), related Q9, Q16, Q20
**Test plan**: [tp-eng-95985-send-email-attachments.md](../test-plans/tp-eng-95985-send-email-attachments.md)
**Analysis**: [`spec/eng-95985-send-email-attachments/`](../eng-95985-send-email-attachments/README.md) (plan
sections 3-5 hold the task tables this story restates; section 9 is the Definition of Done)
**Status**: in-progress (set in this docs-only BMAD PR on 2026-10-28: implementation starts before this PR merges, so
`ready-for-dev` is never true for it; plan section 2). The clio feature PR flips it to `review` when it opens; `done` is
flipped in the next clio PR that touches `spec/sprint-status.yaml` after the last merge, with only the really ticked
Definition of Done boxes.
**Size**: L
**Repo**: crt-process-builder, then clio-knowledge, then clio (one feature PR each, titled
`ENG-95985 Send email attachments`, branch `feature/ENG-95985-send-email-attachments`), after this docs-only clio PR
(branch `docs/ENG-95985-send-email-attachments-spec`)
**Depends on**: ENG-95984 File process parameter type (story-eng-95984-file-parameter-type-1) and ENG-96505 Element
readiness and object attachments mode (story-eng-92719-file-processing-element-1), link L5 of ENG-92719 File
processing element; their code is merged. This BMAD set merges before the package PR leaves draft (Q14).
**Blocks**: nothing in the tracker.

---

## As a

coding agent building a business process through the clio MCP process tools

## I want

to put the files a Process file element, another element or a process parameter holds onto a Send email element as
its attachments, change that list later, and read it back in the shape I write it

## So that

a process can email a generated report, a record's attachments or files passed in, in manual or automatic mode and
with a custom or a template message, without the designer

---

## Acceptance Criteria

The full text is the SPEC's AC1-AC7 (the proposed Jira text; decisions Q19: the lead writes it into the description
after the owner approves the wording).

- [ ] **AC1** - `email.attachments` builds one designer row per entry on build, `addElement` and `setElement`;
  describe returns the list in the write shape; a sent list replaces the element's attachments, `[]` removes them.
- [ ] **AC2** - the three Process file variants and a process FileCollection / File parameter are accepted sources;
  one collection feeds several Send email elements.
- [ ] **AC3** - more than one attachment, and a slot with several files.
- [ ] **AC4** - manual/auto x custom/template; automatic sending proven by real delivery on stand `Creatio` (Q17).
- [ ] **AC5** - a missing, fileless or later-declared source fails with an error naming the entry and the source; a
  source not on a flow path before the email warns; an unscoped Process file source is refused (Q20); attachments on a
  multi-instance Send email are refused (Q16).
- [ ] **AC6** - guidance states the configure-first and flow-before rule and names `UseProcessEmailAttachments` for
  troubleshooting (Q9).
- [ ] **AC7** - serialization matches a designer-built capture (2+ attachments, a numbering gap, the Process parameter
  variant); tests, docs and MCP surface updated.

---

## Gating

| Gate | What | Who | Before |
|---|---|---|---|
| Q1-Q20 | answered and recorded | owner | done 2026-10-28 |
| Q19 | Jira AC text approved, then written into the description only (checklist field untouched), read back | owner, then lead | the feature PRs leave draft |
| Q13 | claim comment on ENG-95985 (empty claim table; every number claimed there after checking the tags) | lead, on the owner's yes | the first cut |
| 0.4 | drift check against the basis | lead | done 2026-10-28 (the plan's drift note) |
| EM0 | platform version of stand `Creatio`, the Invoice printable, an email template | lead, read-only | EM1 |
| EM1 | designer capture (AC7 oracle) | lead in Claude in Chrome, owner logs in; writes on the owner's yes | P-B2/P-B3 (slot numbering, stamps, sources); slice A and C1 may proceed on the agreed answers; its rows feed the P-C5 parity pattern B |
| STOP | EM2 (save, pre-save gate, stored bytes) + EM3 (designer) on the experimental cut | lead, owner's yes per write | slice C |
| EM7 | real delivery once the owner says mail on stand `Creatio` works | lead, owner's yes | AC4 closed |

If EM2 or EM3 fails, work stops, the cause goes to the owner, and the estimate is redone before slice C starts.

---

## Tasks

Ids are the analysis plan's (sections 3-5). Components C1-C10 and K1-K12 are the design file's.

**CrtProcessBuilder - slice A: behaviour-neutral refactors** (one commit each; gate 2 single combined lens)

- [ ] **P-A1** C2: lift `PlanFiles` / `FilesBinding` / `FileItemOf` into `FileSourcePlanner` behind
  `IFileSourcePlanner`: `Plan(schema, element, IFileSourceDescriptor source, FileSourceWording wording,
  UserConnection)`, `FileSourceWording(label, member, noSourceConsequence, nothingConsequence)`, returning a data
  `FileSourcePlan` (`Mapping(elementName, target)`); the target names and the write route stay with the caller. The
  variant composes `new ProcessFileProcessingBinder(new FileSourcePlanner())`; the DI registration of
  `IFileSourcePlanner` lands in P-B3. Done when every existing `FileProcessing*Tests` passes unchanged, a
  characterization test (TC-U-67, committed before the lift) pins each `files` message, and the planner speaks in the
  caller's terms (TC-U-68).
- [ ] **P-A2** C7 part 1: generalize `ProcessFileProcessingFilesBinding.Read` (renamed `FileCollectionBinding`, with
  `Read(element, name)`, `Of(collection)`, `Shape(...)`) and the issue texts of `ProcessFileProcessingDescriber` over
  (collection, item) names. Done when the `files` describe tests and the Process
  parameter notice-rule tests pass unchanged.

**CrtProcessBuilder - slice B: slots and the block**

- [ ] **P-B1** C1 contract: `SendEmailAttachmentDescriptor`, `SendEmailDescriptor.Attachments`, describe types;
  wire-contract rows (TC-U-69).
- [ ] **P-B2** C3 `EmailAttachmentSlot` + `IEmailAttachmentSlotWriter` (find, create with explicit stamps and `In`,
  remove with `Parameters.Remove` after the dependency check, lowest free number over every parameter of the element);
  DI registration (TC-U-01..08).
- [ ] **P-B3** C4 `ApplyAttachments`: plan all entries before the first write; reconcile (Q4) with the dependency check
  before a removal; bind via the block form of `BindCollection` / `ApplyBlockMapping`; `EnsureConfiguresSomething`
  counts attachments; refusals of design section 5.1; the Q16 multi-instance refusal (`MultiInstanceOptions` on the
  activity); the Q20 ledger tracking and the Object rule extension, judged at the end of the request; designer display
  values on slots; the version clone keeps the slots owned; the DI registration of `IFileSourcePlanner` (first
  container consumer) (TC-U-09..41, 70, 71, 73, 76, 78, 78a, 78b, 80).
- [ ] **P-B4** C9 comment fixes (`CollectionBindingRules` Creatio.ai promise, Q15; recipient numbering comment).
- [ ] Experimental cut (claimed first), installed on stand `Creatio` on the owner's yes; EM2 + EM3 recorded in the
  measurements file (STOP gate). Owner's choice 2026-10-29: the cut may precede P-C1 (the raw-mapping guards) because
  it lives only on stand `Creatio`; it never reaches a clio bundle, and no released cut precedes P-C1.

**CrtProcessBuilder - slice C: guards, describe, notices, parity**

- [ ] **P-C1** C5 guards: in `Plan`, a new `if (!fromBlock)` block placed first, before `ResolveTargetParameter`
  (onto a slot, keyed on `EmailAttachmentSlot.IsReservedName` through the name-only `ResolveTargetOwner`), and beside
  `EnsureNotReadFromAFilesInput` (from a slot); the `IProcessMappingService` change documented (TC-U-49..53, 74).
- [ ] **P-C2** C6 order checks: build-path declaration-order refusal; flow-reachability warning on build and modify
  (TC-U-54..58, 77).
- [ ] **P-C3** C7 part 2: `DescribeAttachments`, read-back members, issues (multi-instance element reported as found,
  with an issue), `configured`; the generic `parameters[]` does not decode a name `IsReservedName` matches (TC-U-59..66,
  75, 78c, 81).
- [ ] **P-C4** C4 notices of design section 5.2 (single File, expression, the same source twice, removal; a
  sub-process output source, decisions Q20 "Indirect sources"); Q9: no feature reader and no feature-off notice
  (TC-U-43..47, 79).
- [ ] **P-C5** parity: pattern A (3 corpus processes under `Fixtures/SendEmail/` with PROVENANCE SHA pins and a
  `.gitattributes` `-text` line), pattern B (EM1 rows), pattern C (writer JSON diff) (TC-P-01..09); every new fixture
  carries a CI category (TC-U-72).
- [ ] **P-C6** C10 capture doc (`docs/send-email-element-capture.md`, attachments section with the EM1/EM2 results).
- [ ] Gate 1 (full 3-lens fan-out: the shared mapping service changes) before the draft package PR opens; claimed cut,
  install, EM4, EM4c, EM5, EM8, EM10, EM11, EM13, EM15; final cut, EM2 re-run (describe steps), EM7.

**clio-knowledge**

- [ ] **KB-1** new article `guidance/mcp/guides/processes/send-email-attachments.md` (`process-send-email-attachments`),
  content per design section 7, the cut written as the real floor (no TBD at merge).
- [ ] **KB-2** `files.md`: split the "not buildable here yet" sentence; the `ProcessFileGuidanceTests` pin updated in
  the same commit.
- [ ] **KB-3** `send-email.md` owned-scope line + pointer; `element-catalog.md` Send email row; `process-modeling.md`
  route line paid by a swap (1 B headroom); `routing.md` row `name=process-send-email-attachments`.
- [ ] **KB-4** `bundle-source.json`: resource with NO `legacyUris`, `requirements.itemIds` and `resourceUris`, the
  `process-send-email` and `process-modeling` entry descriptions, `libraryVersion` above master (1.15.174 on
  2026-10-28) and every open branch claim, re-checked at merge; no authored `sequence` field.
- [ ] **KB-5** tests: `SendEmailGuidanceTests`, `ProcessGuideCrossReferenceTests` (routing), `GuidanceMigrationTests`
  (`PostMigrationGuidance` gains the name), size gates, manifest (TC-K-01..11, 13).
- [ ] **KB-6** fact-check gate: one refute-first agent per claim cluster, given the source paths, forbidden to cite
  `docs/knowledge/` or the analysis folder as evidence; every refutation re-checked by hand (TC-K-12).

**clio** (read `docs/agent-instructions/bundled-packages.md` before CL-1 and CL-3, and `docs/knowledge/ProcessModel/`
and `docs/knowledge/McpServer/` before any edit)

- [ ] **CL-1** rebundle the final cut (`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <pkg> -Version
  <claimed>`), pins in the same commit (`BundledProcessBuilderPackageTests`; TC-C-20).
- [ ] **CL-2** K2 key check + tests (`KeySets_Should_EqualTheMembersOfTheBundledServerContracts`,
  `ReadBackFields_Should_BeDescribeOnlyMembers`; TC-C-01..05).
- [ ] **CL-3** K3 floors + pins (`ProcessDesignerRequiresPackageAttributeTests`, description pins, the e2e floor
  literals; TC-C-06).
- [ ] **CL-4** K4/K5 descriptions and prompts; re-measure with the ceilings lowered on the branch, including the
  seven-contract wire bundle held to 19,000 B; swaps per Q12, no ceiling raise (`ToolContractPayloadBudgetTests`;
  create and modify forward the list verbatim; TC-C-07, 08, 17..19).
- [ ] **CL-5** K6 typed describe, K7 read-back checks (`ServerProcessDescriberTests`, `EmailBlockExpectationTests`;
  TC-C-09..16, 23).
- [ ] **CL-6** K8 capability map, K9 curated re-pin. The clio PR stays draft until the KB PR merges and its generation
  is published; then the re-pin, clio gate 3, and only then reviewers (`WorkspaceTemplateGuidanceDriftTests`;
  TC-C-21, 22).
- [ ] **CL-7** K10 e2e `SendEmailAttachmentsToolE2ETests`, run on stand `Creatio` on net10.0 and net8.0, Ignored = 0
  (TC-I-01..10).
- [ ] **CL-8** K11 knowledge records (only facts the code does not say); records whose `applies-to` paths
  `make check-knowledge` reports as touched updated or deleted.

Validation: package `dotnet test tests/UnitTests/CrtProcessBuilder.Tests/CrtProcessBuilder.Tests.csproj -c dev-nf` and
`-c dev-n8`; clio `dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit" < /dev/null` (more than three
modules change: full unit suite); clio-knowledge the whole `Clio.Knowledge.Bundle.Tests` project. The clio PR body
carries "MCP reviewed" (tools, prompts, resources, `clio/tpl`), "docs reviewed, no update required", "ClioRing
compatibility reviewed, no Ring-consumed contract changed" with the inspected paths, the re-measured budgets and the
`Validated:` command.

---

## Definition of Done

Plan section 9, as checkboxes. Ticked only when true.

- [ ] **AC1** `email.attachments` builds slots in the designer's shape; describe returns the list in the write shape;
  round-trip test green; EM2 + EM3 passed.
- [ ] **AC2** the three Process file variants (EM4) and a process FileCollection passed from a parent process (EM4c)
  build and run; one collection feeds two Send email elements (EM11).
- [ ] **AC3** two or more slots, and a slot with several files, attach every file (EM4).
- [ ] **AC4** manual+custom (EM4), manual+template (EM5), and auto+custom, auto+template and the Create activity
  variant with its `ActivityFile` rows by real delivery (EM7), all on stand `Creatio` after the owner configures mail
  sending there (Q17).
- [ ] **AC5** every refusal of design section 5.1 has a unit test, and the e2e refusal test shows nothing was created.
- [ ] **AC6** the article states both order rules with EM10's measured result; build-path guard tested.
- [ ] **AC7** parity patterns A, B, C green against the corpus and EM1; tests, docs, MCP surface updated; e2e on stand
  with Ignored = 0.
- [ ] Every TC-U, TC-P, TC-C, TC-I and TC-K row of the test plan implemented and green (TC-U-42 and TC-U-48 are removed
  rows, Q9; TC-K-12 is the fact-check gate; TC-K-13 follows EM8): package on dev-nf and dev-n8 plus the first CI run of
  the draft package PR; clio `TestCategory=Unit` full suite; the KB test project.
- [ ] Review gates 1-3 passed; KB fact-check passed.
- [ ] The clio PR body carries "MCP reviewed" (tools, prompts, resources, `clio/tpl`), "docs reviewed, no update
  required", "ClioRing compatibility reviewed, no Ring-consumed contract changed" with the inspected paths, the
  `Validated:` filter and the re-measured budgets.
- [ ] CL-8 knowledge records in the clio PR and the `make check-knowledge` report read.
- [ ] The tracker row moved as plan section 2 says; story DoD boxes ticked only when true.
- [ ] The inherited flip of `story-eng-92719-file-processing-element-3` in the tracker reviewed (only really done
  boxes).
- [ ] Stand leftovers listed (measurements file register); cleanup proposed, run only on the owner's yes.

## Dev Agent Record

- 2026-10-28: analysis, decisions Q1-Q20 (all agreed), plan and test plan written; drift check done (the
  plan's drift note). No product code written yet; nothing written to the stand, Jira, GitHub or GHE.
