# Story 2: Generated report and Process parameter variants of the Process file element

**Feature**: eng-92719-file-processing-element
**Jira**: ENG-96506 Generated report + process parameter modes (Sub-task of ENG-92719 File processing element; epic
ENG-92704 Create BP via AI Toolkit)
**PRD**: [prd-eng-92719-file-processing-element.md](../prd/prd-eng-92719-file-processing-element.md) - RP-1..RP-10
(and its share of FE-1..FE-6)
**ADR**: [adr-eng-92719-file-processing-element.md](../adr/adr-eng-92719-file-processing-element.md) - D13, D14, D16,
D17, D18, D19; D25 and D26 for this cut
**Test plan**: [tp-eng-92719-file-processing-element.md](../test-plans/tp-eng-92719-file-processing-element.md)
**Status**: review (2026-10-19: M1 (+ M2) and O-TP1 recorded, cut 1.6.6.106 (1.6.6.105 superseded by the final-review fixes), the clio PR is open; it was deferred
until those two were recorded)
**Size**: M (about 7-15 h of AI-agent time and 3.5-9 h of the owner's time; about 3-4 working days of calendar; the
package PR is estimated at 2.5-4k added lines)
**Repo**: crt-process-builder, then clio, then clio-knowledge (one PR each, titled
`ENG-96506 Generated report + process parameter modes`, branch `feature/ENG-96506-process-file-report-and-parameter`;
each a draft stacked on story 1's branch in the same repository until story 1's PR merges)
**Depends on**: story 1, ENG-96505 Element readiness and object attachments mode (link L3: this story registers its
variants in the handler and registry story 1 introduces): the package PR leaves draft after story 1's package PR merged
(E3) and merges after story 1's clio PR merged (E4). ENG-95984 File process parameter type
(`story-eng-95984-file-parameter-type-1`, link L2, functional): the Process-parameter variant consumes File and
FileCollection parameters, dotted process-parameter paths and `BindCollection`. Owner questions Q18 (and Q14, Q16 of
story 1) were answered on 2026-10-14, all as recommended, before the package code and before RP-1 and RP-4 are pasted
into Jira. Stand measurement M1 (+ M2)
RECORDED before the package PR leaves draft (analysis plan section 4.1, pr-split section 4.1); it also decides the
single-file path.
**Blocks**: story 3, SF (link L4: slot order RP then SF, Q4).

---

## As a

coding agent building a business process through the clio MCP process tools

## I want

to generate an MS Word report for a record and save it to a record's attachments or hand it on, and to save files
taken from a process parameter or another element to a record's attachments, through the same `fileProcessing` element

## So that

the product's own recipe (a generated report saved to the record's attachments) and the file hand-over between elements
can be built, read back and changed without a generic user task that saves green and fails at run time

---

## Acceptance Criteria

The full text is the PRD's RP table (it replaces the Jira acceptance criteria; agreed 2026-10-07). Each line below is
that criterion in short. A row marked "Qnn, agreed" encodes the owner's answer to that question (agreed 2026-10-14, as
recommended); it is pasted into Jira (plan FE.0b) as written.

- [ ] **RP-1** (Q18, Q16, Q14, agreed) - `report.printable` accepts list-printables' `templateId` (preferred),
  a `[#Lookup...#]` macro or a caption; an ambiguous caption is narrowed by `report.recordId`'s object or refused with
  every candidate; only MS Word printables of the reported object are accepted; FastReport, DevExpress and a printable
  with no object are refused; `report.recordId` takes the value sources and compiles to `Id = value` on the report
  entity.
- [ ] **RP-2** - a report element saves to a record's attachments or, with `useInProcess`, hands `ReportFiles` on; a
  notice says the files are temporary when they flow into an Out parameter (their lifetime is source, pending M16;
  ST-07 closes it).
- [ ] **RP-3** - an unknown or missing printable is refused with "create it in System Designer -> Report setup" and up
  to N candidates by `templateId`; nothing is created or picked.
- [ ] **RP-4** (Q16, agreed) - a Word report with no record scope and no filter condition at the end of the
  request is refused before the pre-save gate (the pre-gate check story 1 adds); a scope that can match several records
  with save gets the "all N files go to one record" notice.
- [ ] **RP-5** - `files` takes exactly one of `processParameter`, `sourceElement` + `sourceElementParameter`, or
  `expression`; a collection binds `Files` and `Files.File` (the item resolved, never assumed); a single File binds
  `Files.File` only and only if M1 passed, otherwise it is refused with "wrap one file in a FileCollection".
- [ ] **RP-6** - the Process-parameter variant requires `saveTo`; `useInProcess`, `value`, an outer-only or empty
  binding, a source with no FileLocator item and a mapping FROM `Files` are refused; `ObjectFiles` (item `ObjectFile`)
  and `CreatedObjectFileIds` are listed as outputs.
- [ ] **RP-7** - describe round-trips both variants (`report.*`, `files`, `bindingLevels`); the four shipped product
  processes describe as `buildType: fileprocessing`, read-only.
- [ ] **RP-8** - the Report element equals PrintInvoiceReport and the Process element FileParameterProcess under the
  TC-41 comparison rule, with the predated parameters excluded and named in the test: FileParameterProcess's
  `TargetDataEntitySchemaUId` (its item `FileCollectionParameter` role-normalised to `File`), and for PrintInvoiceReport
  whatever SC-0 (TC-M-18) records (none expected).
- [ ] **RP-9** - `process-files` gains the report and process-parameter sections and "Add data creates the report's
  data row first"; Send email attachments and Creatio.ai file passing are stated as not buildable through this tool
  yet.
- [ ] **RP-10** - unit tests, e2e, descriptions, prompts and guidance in the same change.

---

## Gating: owner questions and measurements

| Item | State (2026-10-14) | What waits on it |
|---|---|---|
| Q18 FastReport printables | agreed 2026-10-14: B, refused (as recommended) | the package code (RP.1) AND the AC row RP-1, which stands as written |
| Q14 naming bundle, Q16 record scope and empty-filter policy (story 1's questions; Q13, Q15, Q17 shape story 1's shared code) | agreed 2026-10-14, as recommended | the package code of this story too (the `report` group, `report.recordId`, the Word empty-filter refusal) AND the AC rows RP-1 and RP-4 |
| M1 (+ M2): a nested-only `Files.File <- File` copies one file; an unset single File fails with an NRE | open; a designer-built probe `UsrG1FilesBindingProbe` (script task), a write | RECORDED before the package PR leaves draft; decides the single-file path (RP.2, TC-64, ST-03); M2 sets the guide wording |
| M13 describe of PrintContractsReport | done 2026-10-02 (`Guid` with `referenceSchema: Contract`) | the TC-45 Report fixtures |
| M15 FastReport packages and printables | done 2026-10-02 (none) | Q18 stands as recommended |
| M16 lifetime of temporary report files | open; a write | the notice wording (D14, D17) and the RP-2 lifetime wording; no code; ST-07 closes it after the cut |
| M18 a constant file-name suffix under another culture | open; after this story's cut | the culture note in the guide; no code |
| M4 the end-to-end Object -> FileCollection -> Process chain | open as a designer probe; its builder-written repeat V7 passed on 1.6.6.98 (2026-10-14) | verification only; ST-04 repeats it on this cut |
| O-TP1 where TC-91 (ENG-95984's E2E-14a-d) runs | open (recommended B: in this story's clio PR) | TC-I-91 in RP.7 |
| RF1 exception: ENG-95984's `CollectionBindingRules.TakesFiles` (`PB/Mappings/CollectionBindingRules.cs:168`) compares the element's `SchemaUId` with the literal `ProcessDesignConstants.FileProcessing.ProcessFileTaskSchemaUId`, the one production use of a file-task UId, against reuse RF1 (production resolves the task schemas by NAME; the UIds live in the test fixtures and the drift guard only) | open; decided in this story, which registers the Process variant | RP.2 / RP.3: either `TakesFiles` asks the variant registry for the Process variant (resolved by name) and the constant is deleted, or the constant stays as an exception to RF1 named in reuse RF1 and pinned by the drift guard |

Contingency X2: if the Process-parameter half fails its stand proof (the M4 chain, ST-04) while the Report half is
ready, the Report commit group is cherry-picked into this PR, the Process-parameter group moves to a new Sub-task
"Process parameter source of the Process file element", and this story is re-scoped to the Report mode (owner
decision; the owner is informed).

---

## Tasks

`PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/`. Ids are the analysis plan's work packages (section 5.2). Two variant
registrations, one commit group each, so that X2 can cherry-pick.

**Stand (before the package PR leaves draft)**

- [ ] **RP.0** M1 (+ M2) on the designer-built probe (the user's go-ahead; one at a time), recorded with date, package
  version and verdict; M13 and M15 are done.

**CrtProcessBuilder** (base `main`; draft on story 1's branch until it merges)

- [ ] **RP.1** Report variant (D17): `IReportTemplateReader` (ESQ on `SysModuleReport`, `UseAdminRights = false`,
  columns `Id`, `Caption`, `Type.Name`, `SysEntitySchema.Name`, the module-entity fallback, `ConvertInPDF`); the
  `report` group's binding (the request contract already declares the group and its members; story 1 refuses it as
  not written yet, and registering the variant replaces that refusal); the D17 refusals (missing or unknown printable with the Report setup hint and candidates by
  `templateId`; ambiguous caption; DevExpress; FastReport; no object; bad `fileNameSuffixColumn`); Word
  `separateReports` written true when omitted, an explicit `false` accepted with a notice; `report.recordId` compiled
  by `FileProcessingScopeFilter` as `Id = value`; a constant `fileNameSuffix` through the localizable resource path; a
  `report.*` value-source `expression` checked as in story 1's OA.2; the same-object printable swap keeps the filter (the
  record-scope type check re-run), another object clears it; notices (ConvertInPDF, several records into one target,
  feature off, temporary files into an Out parameter, Word `false`); the one-method feature seam
  (`IFileProcessingFeatureReader`). NEW `PB/FileProcessing/IReportTemplateReader.cs`, `ReportTemplateReader.cs`,
  `IFileProcessingFeatureReader.cs`, `FileProcessingFeatureReader.cs`.
- [ ] **RP.2** Process-parameter variant (D18): the `files` binder over ENG-95984's `BindCollection`; a collection binds
  `Files` and `Files.File` (item `File`, `ObjectFile` or the declared name, resolved); a single File binds `Files.File`
  only **if M1 passed** (else refused); `expression` onto `Files.File` as given, with a notice (it goes through
  `ApplyMapping`, so the meta-path check runs; `ObjectFiles.File` tokens pass); refusals F-P1; `useInProcess` refused;
  `saveTo` required.
- [ ] **RP.3** Claims through the registry for both schemas (identity, generic-route and raw-mapping refusals, the
  Report filter-target claim, describe with `report.*`, `files` and `bindingLevels`); the Report and Process notice rules
  in `ElementConfigurationNotices`; the `ProcessFilterApplier` text for the Process variant.
- [ ] **RP.4** Package tests: the report reader fake; a printable by id, macro, caption and the ambiguous case; the Word
  default and the explicit-`false` notice; the name-column rules; same- vs different-object swap; FastReport and
  DevExpress refusals (unit only: no stand fixture exists); Process metadata at both levels; the single-file switch in
  both positions; F-P1; parity with PrintInvoiceReport (TC-U-59, exclusions taken from the SC-0 record) and
  FileParameterProcess (TC-U-69, `TargetDataEntitySchemaUId` excluded and named, the item name role-normalised); the
  TC-12 "tolerated" case of story 1 flipped to refused for both schemas. Fixture categories, AAA, `because:` and
  `[Description]` as in story 1.
- [ ] **RP.5** Capture-doc sections for both variants in `docs/process-file-element-capture.md`; diary entry; restamp
  LAST to the number claimed before cutting (PR description AND the shared cut claim on ENG-92719 File processing
  element; the next free number in it at cut time); tag
  `crtprocessbuilder-<version>` verified on the remote.

**clio** (base `master`)

- [ ] **RP.6** Rebundle; floor raise to the final cut (describe stays presence-only). A longer floor literal (a new
  digit) costs a byte in three descriptions: the budget test catches it, this PR trims (ADR D25). Capability-map rows;
  typed `report` / `files` members of `DescribedFileProcessing`, each with a `[JsonExtensionData]` bag and `///`
  summaries; `clio.mcp.e2e/Support/Results/ListPrintablesEnvelope.cs` gains `count` and `printables`; the
  `list-user-tasks` exceptions and `ListUserTasksPrompt` extended to ReportFileProcessingUserTask and
  ProcessFileProcessingUserTask; `CreateBusinessProcessPrompt`'s `fileProcessing` sentence extended to the `report` and
  `files` groups (TC-U-89 and TC-U-90 extended); the `fileProcessing` key set of story 1 (`ProcessBlockKeyCheck`)
  already carries the `report` and `files` groups and their write members, because story 1's server contract declares
  them (so that it can refuse them by name), so this cut adds only the describe-only members its describe additions
  bring (`report.caption`, `report.convertToPdf`, `bindingLevels`) to the read-back keys, in the archive's commit
  (`KeySets_Should_EqualTheMembersOfTheBundledServerContracts` and `ReadBackFields_Should_BeDescribeOnlyMembers` hold
  the key sets equal to the archive; ENG-103085 Refuse element and block keys the process builder would drop
  silently; TC-U-93); no new `CLIO*` analyzer warning.
- [ ] **RP.7** e2e (extend `ProcessFileElementToolE2ETests` or NEW
  `clio.mcp.e2e/ProcessFileReportAndParameterToolE2ETests.cs`):
  - Word SaveToFiles with `report.recordId` (TC-I-52, TC-I-60), with the `separateReports: false` notice and the
    refusals of an unknown printable (Report setup hint) and a non-text name column (extending TC-I-60);
  - Report `useInProcess` into the Process variant, and the Object -> Process chain (TC-I-73);
  - two consumers of one output (TC-I-75);
  - Add data -> generated report (TC-I-74: `addData` of a DNSGuideReport row, a Report on the "DNS Requirements"
    printable with `report.recordId <- AddData1.RecordId`, then `deleteData` of the row; built and described, not run);
  - the Process-variant refusals, `useInProcess` among them (TC-I-66), and the generic route refused for both schemas
    (TC-I-12, extended);
  - `UsrFpOaDesignerFixture`'s F2 now describes as `fileprocessing`, read-only (TC-I-46, extended);
  - a **read-only** describe (`mode=full`) of the four shipped product processes, never modified (TC-I-61);
  - the `Files` mapping rules over the wire, ENG-95984's E2E-14a-d (TC-I-91), the single File only if M1 passed.
  `Assert.Ignore` when `list-printables` returns 0 rows (or no "DNS Requirements" row for TC-I-74).
- [ ] **Knowledge records and status** (each states its basis): NEW
  `docs/knowledge/platform/a-process-file-report-runs-only-word-and-fastreport.md` (basis: source, not measured; it
  records the dispatch by `Type.Name`, that DevExpress throws and that FastReport's process path is unverified, not that
  FastReport runs), `platform/the-process-parameter-file-source-only-saves.md` (basis: source, not measured: the
  `NotSupportedException` for "use in process"), `infra/the-creatio-sandbox-has-five-word-printables-and-no-fastreport.md` and
  `platform/invoice-quotation-and-contract-printables-have-no-sysentityschema.md` (measured 2026-10-01); `platform/use-in-process-report-files-are-deleted-with-their-process.md` only after M16;
  `platform/a-nested-only-files-binding-reads-one-row.md` only after M1 and
  `platform/an-unset-single-file-fails-a-process-parameter-file-element.md` only after M2; DELETE story 1's temporary
  `ProcessModel/report-and-parameter-file-elements-stay-generic-until-eng-96506.md`; review the `make check-knowledge`
  report. Status flips, including story 1's `done`, which rides in this PR.

**clio-knowledge**

- [ ] **RP.8** `process-files`: the report section (`templateId` -> `report.printable`, Word `separateReports`, the
  same-object swap keeps the filter, temporary files, record order unspecified, captions in the runner's culture); the
  process-parameter section (`files`, `expression` as given, an unset single File throws, an empty source copies
  nothing); "Add data creates the report's data row first"; the `element-catalog.md` generic-route sentence extended to
  both schemas; the report section split into `process-files-report` (its own full registration) once the guide would
  exceed about 21,000 characters. The guidance rules of story 1 (read-backs with `mode=full`, consent wording, no Jira
  keys) apply; libraryVersion above master at merge.

**Tail**

- [ ] **RP.9** Stand proofs (below); parity against PrintInvoiceReport and FileParameterProcess; gates 1 and 3; one
  review round; rebundle and reinstall; knowledge publication and the `info-knowledge` check.

---

## Stand verification

After the cut, before the package PR leaves draft (the common protocol of story 1; the behaviour probe: an unknown
`report.printable` gets the D17 text):
- **ST-03** (only if M1 passed) a single File into the Process variant inside ST-02's callee: 1 copy per iteration.
- **ST-04** the builder-built M4 chain: Object `useInProcess` -> FileCollection `Docs` (P2 pairs the item) -> Process
  variant saving to an Account: 2 copies; describe of PF1 shows `bindingLevels: both`. Fails: do not merge, or X2.
- **ST-05** one output, two consumers (the FileParameterProcess shape): copies on both targets, sources unchanged.
- **ST-06** Word SaveToFiles on an existing Invoice (selected read-only, never inserted) with `report.recordId` and
  `fileNameSuffixColumn: "Number"`, re-found by Modify data (Tag "ST06"): 1 new InvoiceFile row named
  `Invoice. <Number>.docx`. Run once; a second run in the same hour also tags the first file.
- **ST-07** Report `useInProcess` -> Process variant -> a Perform task, parked, then completed through `odata-update`:
  temporary `SysProcessFile` rows present while parked, gone at completion (closes M16 for the wording).
- **DT-03** the user opens the RP elements: the right source variant, the Word "separate" checkbox forced on, `Files`
  bound; a no-op save leaves the metadata unchanged.
- **e2e** Passed with Ignored = 0 (or `Assert.Ignore` only for the documented 0-printables case); the read-only describe
  of the four product processes recorded.
- **SC-4 (TC-M-20)** the builder twin of SC-1's E6-E9 (E10 only if M1 passed): equal under the TC-41 rule, with the
  predated parameters excluded and named; SysFile targets refused until SF.

## Status convention

As in story 1: `in-progress` when this story's clio draft opens; `review` when it leaves draft; `done` after the
knowledge PR merges, carried in story 3's clio PR and never pushed after an approval.

## Definition of Done

- [ ] Report and Process variants registered, one commit group each; claims, refusals, filter target and describe for
      both; MS Word only; the single-file path present only if M1 passed.
- [ ] Package tests green on `-c dev-nf` (short-path worktree) and `-c dev-n8`; capture sections match PrintInvoiceReport
      and FileParameterProcess under the TC-41 rule with its named exclusions (TC-U-59, TC-U-69); restamped last; tag
      verified on the remote.
- [ ] clio: rebundle; floors to the final cut, the budget test green after the raise and its
      figures re-measured; `ListPrintablesEnvelope`; DTO members with bags and `///` summaries; the key set extended; no
      new `CLIO*` analyzer warning; `list-user-tasks` exceptions, `ListUserTasksPrompt` and `CreateBusinessProcessPrompt`
      extended and pinned (TC-U-89, TC-U-90); TC-I-91 Passed; full unit suite green (`TestCategory=Unit`,
      `< /dev/null`); e2e Passed with Ignored = 0 (or the documented 0-printables ignore); the read-only describe of the
      four product processes recorded.
- [ ] Knowledge records added, deleted or deferred as in the task list, each with its basis; `make check-knowledge`
      report reviewed.
- [ ] The same PR-description statements as story 1 (MCP reviewed, docs reviewed, ClioRing compatibility, the budget
      figures, `Validated:`).
- [ ] clio-knowledge: sections and `element-catalog.md` for this cut; the knowledge suites green; refute-first fact
      check; libraryVersion above master; published, and `info-knowledge` shows it.
- [ ] ST-04 to ST-07, DT-03 and SC-4 (TC-M-20, the story 2 rows) passed (ST-03 only if M1 passed), or X2 executed and
      the owner informed; every test-plan case whose Story column names 2 implemented.

**Process**
- [ ] M1 (+ M2) recorded before the package PR left draft.
- [ ] Jira: RP-1..RP-10 pasted as this issue's acceptance criteria, RP-1 and RP-4 encoding Q14, Q16 and Q18 as
      agreed on 2026-10-14; link L2 (ENG-95984 blocks this issue)
      and L3 (ENG-96505 blocks this issue) in place.
- [ ] Review gates 1, 2 (per commit, tier stated) and 3; no open Blocker or High.
- [ ] Status flips as in "Status convention"; PR titles exactly `ENG-96506 Generated report + process parameter modes`;
      merged by a human package -> clio -> knowledge.

## Dev Agent Record

- Implementation started: 2026-10-19 (package RP.1a in parallel with M1; owner decisions of the same day: O-TP1 = B,
  RF1 = `TakesFiles` asks the variant registry and the UId constant is deleted).
- Owner answers to Q14, Q16, Q18: 2026-10-14, all as recommended; O-TP1 settled as: B, TC-I-91 in this story's clio PR
  (option A lapsed when ENG-95984 merged without E2E-14).
- M1 / M2 results (date, package version, verdict on the single-file path): 2026-10-19, CrtProcessBuilder 1.6.6.104,
  core 10.1.37 .NET Framework, on a hybrid designer-written probe. M1 PASS: a nested-only `Files.File <- [#FileParam#]`
  copied exactly one file, so the single-file path ships (TC-U-64 "passed" arm, TC-I-91(c) positive, ST-03 runs, SC-4
  keeps E10). M2 as predicted: an unset File stops the element with a NullReferenceException in
  `FileFactory.CreateFileInstance` and copies nothing (guide wording only, no code).
- SC-0 record of PrintInvoiceReport's parameter set (date) and the exclusions TC-U-59 takes from it: 2026-10-19,
  read-only, CrtProcessBuilder 1.6.6.104: the stored metadata is byte-identical to PackageStore CrtInvoice 7.8.0;
  en-US resources equal; ru-RU differs as an install/translation artefact. TC-U-59 takes no parameter exclusion.
- Package cut claimed (number, date, where claimed): 1.6.6.105, 2026-10-19, in ENG-92719 comment 550565 (edited in
  place, read back); producing commit `5792f25d`, restamp `2388dd93`, tag `crtprocessbuilder-1.6.6.105` verified on
  the remote; package draft PR crt-process-builder#100. Superseded the same day by **1.6.6.106** (claimed in the same
  comment after the final review): producing commit `b548c47f`, restamp `fde3d8b4`, tag `crtprocessbuilder-1.6.6.106`
  verified on the remote, cut by `rebundle-process-builder.ps1` (1.6.6.105 took the manual route).
- Bytes trimmed per Q20 = A, if the floor raise lengthened the literal (which text per contract, the reviewer's
  approval): the floor literal kept its length (1.6.6.103 -> 1.6.6.105 -> 1.6.6.106). The additions naming the report and files
  groups put modify 31 B over the ceiling; trim T1 (modify setFlowCondition: "the character index comes only with the
  'Formula value error:' PARSE family", -87 B; process-formulas owns the rule) paid it, approved by the owner on
  2026-10-19. No ceiling raise.
- Tests passing (package, clio unit, e2e with Ignored count, knowledge): package: recorded on crt-process-builder#100;
  clio unit: the rebundle's bundle, key-set and budget tests 88/88 at the install, the full targeted run on the clio
  PR's `Validated:` line; e2e: `ProcessFileReportAndParameterToolE2ETests` ran against the .NET Framework sandbox on
  2026-10-19 with 21 passed, 0 failed, 1 skipped on both net10.0 and net8.0 - the skip is the designer-fixture test,
  whose fixture process `UsrFpOaDesignerFixture` is not present on that sandbox. Run first on CrtProcessBuilder
  1.6.6.105 and again on 1.6.6.106 after the final-review cut, with the same result; the isolated agent run of TC-01..TC-12
  passed on 1.6.6.105; knowledge: clio-knowledge PR 297, its bundle tests green.
- Stand records (ST-03..ST-07, DT-03, SC-4): all PASS on 2026-10-19, CrtProcessBuilder 1.6.6.105. ST-03 2 callee
  copies, one per iteration; ST-04 the M4 chain with `bindingLevels: both`, 2 copies; ST-05 two consumers, copies on
  both targets, sources unchanged; ST-06 1 InvoiceFile `Invoice. <Number>.docx` re-found and tagged; ST-07 1
  `SysProcessFile` row while parked, 0 after completion (closes M16 for the top-level case); DT-03 every runtime
  value unchanged by a designer no-op save (display and bookkeeping normalizations only); SC-4 0 differences on all
  five elements, negative control 30.
- Notes (X2 fired or not): X2 did not fire. Left on the stand for cleanup on the owner's word: the probe and ST/SC
  processes, their fixture records and the file rows they created.
