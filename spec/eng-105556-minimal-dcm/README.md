# ENG-105556 Minimal DCM (stages from a lookup) and record-page progress bar: implementation plan

Written 2026-11-08. Phase 1 only: research, design and plan. No product code has been written; nothing has been
pushed to GitHub, GHE or SVN; Jira is not edited. **All decisions are open**: Q1..Q29 in the
[decisions](eng-105556-minimal-dcm-decisions.md) file each carry a recommended option and "open - owner decides";
every other file is written against the recommended options and names the Q a different answer would move. Stand
`Creatio` was only read (no schema save, install, data write, compile or restart).

Jira: ENG-105556 "AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page"
(Task, component bpms tools, epic ENG-92704 "Create BP via AI Toolkit"; no comments, no links).

Basis: the package repository `origin/main` `a3f58852` (CrtProcessBuilder 1.6.6.140), clio `origin/master` `69788ccd5`,
clio-knowledge `origin/master` `622134b` (libraryVersion 1.15.180), core TSBpm `a95349820`, creatio-ui `7ca9210bd0e`,
PackageStore `*/branches/7.8.0` (1,040 packages scanned, 61 DCM schemas), the owner's DCM overview note, the Academy,
and a read-only inventory of stand `Creatio` taken 2026-11-08 (.NET Framework 4.8.1, MSSql, runs CrtProcessBuilder
1.6.6.142, an unmerged feature build that is not `main`; no .NET 8 stand reachable) [measured r10 §1, g5 §1]
(v r10-C1, v r10-C2). Research notes (local, not part of the plan): `<research-dir>/`
(r1-r10, gap notes g1-g6, three independent designs `design-{a-reuse,b-extensible,c-risk}.md`, and `_verdicts.md`
with 78 adversarial verdicts: 18 confirmed, 58 partly, 2 refuted; a verdict overrides its note, a g note supersedes
an r note).

Evidence tags as in every file of this folder: `[traced path:line]`, `[traced, judge]` (re-read by the design judge on
2026-11-08), `[corpus N/M]`, `[measured]` (query in the cited note), `[academy URL]`, `[note]`, `[inferred]`;
`(v rX-CY)` = row of `_verdicts.md`; `design §n`, `facts §n` = sections of the files below.

## 1. Short answer

- **A minimal DCM is an interpreted process plus two settings rows.** `DcmSchema : BaseProcessSchema` generates no
  C# [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:48,210-217] (v r1-C1). The stage-to-lookup link is only
  `DcmSchemaStage.StageRecordId` = lookup row Id, and the stage order is the `Stages` array order (v r1-C3, v r9-C4).
  The 7.x designer, which 8.x still opens by UId, also needs a `SysDcmSettings` row and a `SysDcmSchemaInSettings`
  link row (v r2-C4, v r9-C8). Step-less cases are the shipped norm: 12 of 13 product DCMs have no steps [corpus,
  v r5-C2]. The reference shape is `CrtKnowledgeManagementVersions/KnowledgeArticlePublishing`
  ([use cases](eng-105556-minimal-dcm-use-cases.md) §4, v r5-C10).
- **Where the code goes.** Core is not changed. CrtProcessBuilder gets one new operation, `BuildCase`, on the existing
  `ProcessDesignService` (Q1, Q2) [traced, judge PB/EntryPoints/WebService/ProcessDesignService.cs:27-46]. It runs
  the rights gate, resolves the object, the column and the lookup values, plans the stages, writes the settings row,
  the link row and both package bindings, saves the DCM LAST, reads it back the way the bar does, and compensates on
  failure (Q7; design §5). clio gets one long-tail MCP tool that runs a validate pass, plans the record page, adds any
  missing package dependency, runs the build pass and appends the page fragment (design §6). clio-knowledge gets one
  article (Q23).
- **What is reused.** Every core member the task needs is public: `DcmSchemaManager.CreateSchema` / `SaveSchema` /
  `GetProcessValidationResult` / `RemoveItemByUId`, `DcmEntityUtilities.FindEnabledDcmSchemas` (the runtime's own
  "which case starts" predicate), `GetIsActiveVersion(Guid)`, `IPackageSchemaDataCreator`,
  `PackageElementUtilities.DeletePackageSchemaData` and the `DBSecurityEngine` checks (design §11; facts §0). From
  CrtProcessBuilder the task reuses the service transport, unknown-key refusal, `SafeText`, notices, package resolution
  and the save/rollback/cache-release seam. CrtGenAI `GeneratedDcmSaver` is ported (about 55 lines, GeneratedDcmSaver.cs:105-182, six defects
  fixed), not called, because a dependency would break `DependsOn []` (v r4-C2). The designer's client rules (full
  mesh, 8.x colour, binding names) are ported from DcmDesigner JavaScript. On the clio side the tool reuses the
  related-page add-on reader, `PageGetCommand`, `PageUpdateCommand` append (dry run, then write) and `IPackageDependencyManager`
  ([reuse](eng-105556-minimal-dcm-reuse.md) §1). Genuinely new code is about 1,400 package lines and 850 clio lines
  (reuse §10) [inferred].
- **MCP contract in one line:** `create-dynamic-case {environment-name, package-name, entity-schema-name,
  stage-column-name, stages?, order-column?, name?, caption?, add-progress-bar=true, page-schema-name?,
  add-package-dependencies=true, dry-run=false}` -> `outcome` + a describe-shaped `case` block + `settings` +
  `dependencies` + `page` + per-artifact `obligations`. It is called through `clio-run-destructive`, has 32 stable
  refusal codes, and is gated by `[RequiresPackage]` (design §3; Q3, Q4, Q22). It takes the last free line of the
  discovery index (18,312 / 18,432 B) (v r7-C4).
- **Stages written.** One stage per lookup value (an explicit `stages` list must be a permutation, Q10), 2..20 stages
  (Q11), names `Stage1..N`, fresh stage UIds, colour `#0C8701`, full-mesh connections n*(n-1), `Enabled = true`
  (Q29). The bar freezes without connections and crashes on a null colour (`_verdicts.md` conflicts 6-7). No platform
  lookup order exists, so the default order is: explicit list > `order-column` > `Position` / `StageOrder` / `Number`
  / `StageNumber` > display value. `orderSource` and the ordered captions are always returned (Q8; g6 §3-4).
- **Progress bar.** The page change is the AI app wizard's two inserts: `ProgressBarContainer` (`crt.FlexContainer`) at
  index 0 of `MainContainer`, then `crt.EntityStageProgressBar` with `entityName`, `saveOnChange` from
  `save-on-change` (default false) and `askUserToChangeSchema: true`: the wizard's layout
  [traced UI page-with-tabs-freedom-content-generator.ts:57-109] (v r3-C8) with the bar's own flag defaults (Q19). The bar
  reads the first enabled, active-version DCM of the entity, not `SysDcmSettings` (`_verdicts.md` conflict 1), so
  both sync directions hold by construction (v r3-C7). The record page comes from the object's RelatedPage add-on (g4
  §7) and is written to the default design package (Q17). Typed, mini and Classic pages are refused before any write
  (Q20; design §7).
- **Compile and restart obligations.** Neither the DCM, the page nor the settings data needs a compile or a restart
  on either runtime (v r1-C1, v r4-C4, v r6-C2). The result says so per artifact (`compileRequired: false`,
  `restartRequired: false`, `reloadOpenPages: true`) and clio adds `WithCompileNotRequiredNote()` [traced
  CLIO/clio/Command/McpServer/Tools/CommandExecutionResult.cs:64]. The KB sentence that a DCM "carries its own compile
  obligations" is wrong and is corrected [traced, judge KB/guidance/mcp/guides/processes/process-modeling.md:157-158].
  Still open: whether a package-dependency add marks anything for compile (M10). Installing CrtProcessBuilder itself
  remains a once-per-environment build and restart (v r7-C2).
- **.NET Framework and .NET 8.** The DCM code has no `#if` in `Terrasoft.Core/DcmProcess`, and the binder is bound on
  both hosts (v r1-C1; r4 §4.2). .NET 8 is NOT measured: none of the 8 registered `isNetCore` environments answers
  [measured g5 §1]. "Done" waits for an owner-provisioned `StudioNet8` stand (Q24). Until then results and guidance say
  "expected", never "measured" (design §8).
- **Seams for user tasks and sub-processes inside stages (later task).** Stages travel as objects, and `steps` is
  refused with `steps-not-supported`. Stage UIds are kept separate from `StageRecordId`. Planner, composer and saver
  are split; the save goes through route A behind an `IDcmSchemaSaver` seam (Q6). The dependency planner takes an open
  input set, obligations are per artifact, and the `case` block can later back `describe-dynamic-case` /
  `modify-dynamic-case`. Steps would be built by the existing `IProcessElementFactory` handlers against a transient
  host `ProcessSchema` and wrapped through `DcmSchemaElement.ReadMetaData`; this is spiked first in that task (Q27;
  design §12). The 166 `ProcessSchema`-typed element files are deliberately not generalised now (v r6-C5).
- **What is not proven yet.** Nothing has run on a read-write stand. The STOP gate is M1: a route-A case must open in
  the DCM designer with the expected stages and no error. It is traced equal to route B (g2 §4) but not measured.
  M1, M2, M4, M5 and M6 (designer open, activation, non-admin rights, `RemoveItemByUId` residue, binder target) and
  the presence of the reused core members on the CI platform build 8.3.4 (D0.3) are settled at the package STOP gate
  (plan §3.5, V-2); M3, M7, M9 and M10 (browser sync, runtime start, the dependency feature flag, dependency-add
  compile) during the clio/E2E phase (plan V-1, V-4..V-6); M8 (.NET 8) on the .NET 8 stand after the final cut
  ([plan](eng-105556-minimal-dcm-plan.md) §0-§1; [traps](eng-105556-minimal-dcm-traps.md) §17).
- **Estimate (recommended options, before any answer):** ~35 AI h no-surprise, ~42-50 h with the realistic compound
  buffer (range 21-58), ~15.4 owner h (17-19 with the buffer; 8.9-25.8), 7.5 working days no-surprise and ~9.5-11.5
  with the buffer from answers to the last merge (4-15.25), ~9.5k authored lines, 6 PRs + 1 SVN commit, 4 CrtProcessBuilder cuts
  (3-6) ([estimate](eng-105556-minimal-dcm-estimate.md) §0) [inferred, calibrated on the epic's prior-measured history].

## 2. Files

| File | What it holds |
|---|---|
| README.md (this file) | entry point: status, basis, short answer, file index, decision list |
| [facts](eng-105556-minimal-dcm-facts.md) | what is true today: core DCM model and keys, manager save behaviour, runtime, the two settings tables, the 7.x designer used from 8.x, `crt.EntityStageProgressBar`, feature flags, rights, .NET Framework vs .NET 8, stand and corpus numbers, unknowns, corrections to the research |
| [use cases](eng-105556-minimal-dcm-use-cases.md) | why products use a case, what an 8.x user sees, corpus statistics (61 DCMs, 14 product schemas, 24 bar pages), the minimal shape mirrored (`KnowledgeArticlePublishing`), scenarios U1-U12 enabled and L1-L12 left to later tasks |
| [reuse](eng-105556-minimal-dcm-reuse.md) | reuse map: core APIs, CrtProcessBuilder components, the `GeneratedDcmSaver` port, DcmDesigner/DcmLibrary rules, the script-task compile study, creatio-ui, clio and clio-knowledge pieces, test helpers, genuinely new code |
| [design](eng-105556-minimal-dcm-design.md) | chosen design (judge's merge of designs A, B, C): components per repository, the `create-dynamic-case` contract, `BuildCase` contract and algorithm S1-S21/W1-W4/V1-V2, clio orchestration C1-C8, page resolution and fragment, runtimes, idempotency, rights, extension seams, guidance, tests, delivery order, not done, not verified |
| [decisions](eng-105556-minimal-dcm-decisions.md) | Q1-Q29: context, options, recommendation, status (all open); score table of the three designs; decision index |
| [traps](eng-105556-minimal-dcm-traps.md) | silent-failure modes by area (compile, caches, package binding, order, duplicates, rights, versioning, runtime, 7.x vs 8.x, bar, runtimes, compensation, release mechanics, stands, unit tests, later steps task) with guards and ranked residual risks |
| [plan](eng-105556-minimal-dcm-plan.md) | critical path, day 0 (D0.1-D0.8), BMAD docs PR, package slices A-D with the M1 STOP gate, KB and clio phases, E2E seed and CAADT, cuts and merge order, `AGENTS.md` policy obligations, review gates, stand verification, cleanup, definition of done, risks |
| [test plan](eng-105556-minimal-dcm-test-plan.md) | coverage matrix, package unit and parity tests (TC-U, TC-P), clio unit (TC-C), MCP e2e (TC-I-01..14), clio-knowledge tests (TC-K), agent runs (AR), CAADT checks (CA-1..6), stand measurements M1-M10, manual Jira cases TC-01..TC-13 |
| [estimate](eng-105556-minimal-dcm-estimate.md) | AI time, owner time and calendar per work item WI-0..WI-11, totals by repository, critical path, risk buffers, PRs and cuts, model and effort per kind of work, comparison with the pre-plan estimate |

## 3. Open decisions

All open; the owner decides. The recommended option and the evidence for each are in the
[decisions](eng-105556-minimal-dcm-decisions.md) file.

| Q | Question | Recommended |
|---|---|---|
| Q1 | Where does the DCM write run? | new CrtProcessBuilder operation + clio tool |
| Q2 | `BuildCase` on `ProcessDesignService` or a sibling `DcmDesignService`? | on `ProcessDesignService` |
| Q3 | One tool, two tools, or a documented sequence? | one tool with `dry-run`; `update-page` as the retry path |
| Q4 | Tool name | `create-dynamic-case` |
| Q5 | Up-front rights gate | General + `CanManageDcm` + `CanManageSolution` + append on both settings tables |
| Q6 | Save route | route A behind `IDcmSchemaSaver` |
| Q7 | Write order | settings row, link row, bindings, schema save LAST, verify |
| Q8 | Default stage order when `stages` is not given | explicit > order column > display value; always reported |
| Q9 | How the lookup values are read; caption culture | ESQ under the caller's rights + pure planner |
| Q10 | May an explicit `stages` list name a subset? | no, a permutation |
| Q11 | Stage count bounds | 2..20 |
| Q12 | Re-run on an object that already has an equivalent case | `already-present`, no write |
| Q13 | Existing `SysDcmSettings` row for the object | reuse when same column and no filter column, else refuse |
| Q14 | Settings row form and `Filters` | object form + `Filters` literal |
| Q15 | Package-data binding names | `<Schema>_<id:N>` |
| Q16 | Missing package dependencies | clio adds them, opt-out flag, refuse on a cycle |
| Q17 | Package that receives the page change | default design package |
| Q18 | How clio writes the page | `PageUpdateCommand` append + data-source validator |
| Q19 | Progress-bar flags and whether to expose them | template values + `save-on-change` (default false) |
| Q20 | Typed record pages, mini pages, Classic pages | refuse before any write |
| Q21 | Bind the stage lookup values to the package | no; note N4 |
| Q22 | Package version and the `[RequiresPackage]` floor | above every deployed build |
| Q23 | Knowledge-base placement and naming | `processes/dynamic-case.md`, itemId `process-dynamic-case` |
| Q24 | .NET 8 evidence and definition of done | owner-provisioned `StudioNet8` stand gates "done" |
| Q25 | E2E arrange | seeded object in SVN `AutoTestClioMcp` + `LocalOnly` interim |
| Q26 | Name collision | suffix the default name, refuse a taken explicit name |
| Q27 | Steps approach for the later task (recorded now so this task does not pre-empt it) | spike the host-process wrap first |
| Q28 | Feature toggle | none |
| Q29 | Stage shape defaults | fresh UIds, `#0C8701`, full mesh, `Enabled` |

Not covered by Q1..Q29 and also the owner's call: the read-write stand for M1-M10 and the E2E (plan D0.6), where cut
numbers are claimed (plan D0.7), and two guidance questions from use-cases §7:

- **Stage-column default.** New records whose stage column is empty start no stage the bar can show (N3). Recommended:
  the article tells the agent to OFFER setting the column's default to the first stage through the existing entity
  tools, with the user's yes; `create-dynamic-case` itself never changes the entity (design §16) [inferred].
- **`EnableHistoryDCM`.** Whether stage history is on by default on 8.x stands is not measured (r3 §3.1).
  Recommended: read the `Feature` row in V-1 (read-only); the article says nothing about stage history until it is
  measured [inferred].
