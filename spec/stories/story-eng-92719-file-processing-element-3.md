# Story 3: SysFile attachment storage in the Process file element

**Feature**: eng-92719-file-processing-element
**Jira**: ENG-103937 Process file element: SysFile attachment storage (a Task of ENG-92719 File processing element,
work package SF; owner decision O1 / Q3).
**PRD**: [prd-eng-92719-file-processing-element.md](../prd/prd-eng-92719-file-processing-element.md) - SF-1 (and its
share of FE-1, FE-5)
**ADR**: [adr-eng-92719-file-processing-element.md](../adr/adr-eng-92719-file-processing-element.md) - D15 (SysFile
half), D16 (SysFile scope and sort), D12 (SysFile retarget), D20, D26 (no floor raise)
**Test plan**: [tp-eng-92719-file-processing-element.md](../test-plans/tp-eng-92719-file-processing-element.md)
**Status**: in-progress (the measurements are recorded, the package draft is cut and proven on the stand, the clio
and knowledge drafts are open; the code-review findings of 2026-10-20 are fixed in cut 1.6.6.112)
**Size**: M (about 3-8 h of AI-agent time and 4-8 h of the owner's time, most of it the designer probes; about 2-4
working days of calendar; the package PR is estimated at 1.2-2k added lines)
**Repo**: crt-process-builder, then clio, then clio-knowledge (one PR each, titled
`ENG-103937 Process file element: SysFile attachment storage`, branch
`feature/ENG-103937-process-file-sysfile-storage`)
**Depends on**: story 2, ENG-96506 Generated report + process parameter modes (link L4, slot order RP then SF: one set
of measurements and one SysFile lift for all three variants): the package PR leaves draft after story 2's package PR
merged (E3) and merges after story 2's clio PR merged (E4). Stand measurements M7, M8, M21, M23 (a)-(d) and M25
recorded before the code that depends on each. Under
contingency X3 (the owner ranks custom-object attachments above reports) the slots of story 2 and this story swap and
this story lifts SysFile for the Object variant only.
**Blocks**: story 5 (its slot is after this story's package PR).

---

## As a

coding agent building a process for an object that has no attachment object of its own (the typical case for an object
an AI Toolkit app creates; inference)

## I want

the Process file element to read the attachments of such a record and to save files to such a record, in all three
variants

## So that

the element covers every object the designer offers, and a SysFile-stored file the element writes shows in the
record's Freedom UI attachment list

---

## Acceptance Criteria

The full text is the PRD's SF-1 (decisions D-5, the SysFile row). It is **proposed, confirmed by the owner on ENG-103937**
(not yet pasted as its acceptance criteria): Q1 agreed the criteria of ENG-95984, FE, OA and RP only. Each line below is part of it in short. A case
whose measurement fails keeps story 1's refusal, and this story narrows to the cases that passed (a PRD addition taken
from D15's fallback); what FE-1 then means is an open owner item in the PRD.

- [x] **SF-1a** - `Resolve` lifts SysFile mode per variant through the registry: an Object source on a SysFile-stored
  object (`SourceEntitySchemaUId` = SysFile, `SourceDataEntitySchemaUId` = the record object), and SysFile targets for
  all three variants (SysFile + link `RecordId` + `TargetDataEntitySchemaUId`); an object with its own attachment object
  is never put in SysFile mode.
- [x] **SF-1b** - the record scope uses the shape the designer writes (M7, 2026-10-20: a lookup InFilter on `RecordId`
  with the record object as its reference schema when the record object has a primary display column, a Guid compare
  otherwise); `Split` accepts both SysFile shapes; a stored scope whose reference is not the record object is a describe
  issue.
- [x] **SF-1c** - a sort on a record-object column is refused on the SysFile root; a SysFile column is accepted.
- [x] **SF-1d** - a SysFile-to-SysFile retarget clears the scope (tested Account address -> FP Doc in the package and
  Account address -> City in the e2e; Contact address was not needed for the rule).
- ~~**SF-1e**~~ DROPPED by the owner (2026-10-20, M25): the designer flag has been on by default since 2023-02-01
  (RND-35864) and never changed again; no notice, no TC-80. Was: when the server feature read of the designer's storage flag reads off, a SysFile-mode element built or
  reconfigured in the request gets a notice, never a refusal; this ships only if M25 confirms the read returns true on
  the stand.
- [x] **SF-1f** - serialization of the builder's SysFile elements equals the M21 designer capture under the TC-41
  comparison rule, with the differences the owner accepted on 2026-10-20 named (see "Owner decisions").
- [ ] **SF-1g** - `process-files` states the SysFile hazards (record-object sort columns, `RecordSchemaName`, "check the
  record's Freedom UI attachment list"), and the "SysFile storage is refused" sentences are removed behind a version
  gate.

---

## Gating: measurements

All five are stand writes or designer reads that the user takes part in; each write needs the user's go-ahead and runs
one at a time. Status on 2026-10-20 (stand d_krestov_n, CrtProcessBuilder 1.6.6.109, CrtProcessDesigner 7.8.0): M7
done (the shape depends on the record object), M8 and M23 (a)-(d) PASS (sources and targets lift for all three
variants), M21 captured (`UsrFpSc1SfCapture` `9da69679-b204-4219-aaa4-7bec2cbef362`, kept on the stand as the oracle),
M25 dropped by the owner.

| M | Question | Method | Gates |
|---|---|---|---|
| M7 | the exact `DataSourceFilters` JSON the designer writes for a SysFile `RecordId` scope (a constant record, and a process parameter) | the UO method: a new unsaved process in the designer, read in memory, closed unsaved; SC-1's E3 stores it | SF-1b, the scope shape |
| M8 | an Object element finds a Freedom UI upload stored in SysFile | read-only select of the upload's SysFile row first, then a disposable upload and a "use in process" run | SysFile sources |
| M21 | a designer SAVE capture of a SysFile-mode Object element with a scope, a target and a sort | SC-1 E3 (`UsrFpSc1Capture`, the user's one save), read back by SC-2; M23 (b)'s saved metadata cross-checks it | SF-1f |
| M23 (a)-(d) | the SysFile write path: (a) legacy -> SysFile target, (b) SysFile source -> ContactFile, (c) Word report -> SysFile target, (d) Process variant -> SysFile target; `RecordId`, `RecordSchemaName`, `Type = File`, Name, Size and the record's Freedom UI attachment list | designer-built probes by the user, runs, read-only selects | SysFile targets, per case |
| M25 | `Features.GetIsEnabled` reads the storage flag as true on the stand, and its code name | a designer-built script-task probe that throws the three reads (partly answered: the client read is true) | SF-1e only |

Not gating: M9 (a SysFile sort on a record-object column throws; the refusal severity) and M12 (a later SysFile
override). M17's SysFile half sets the notice wording: `SysFile.RecordId` has no foreign key (measured 2026-10-02,
M17-Q7), so an empty `ConnectedObjectId` is predicted to orphan the file instead of failing (source, not measured; the
M17 runs are owed).

If the owner had rejected O1, this content would fold back into story 1 with these five measurements on its path;
O1 was agreed on 2026-10-07.

---

## Tasks

Ids are the analysis plan's work packages (section 5.3).

**Stand (before code)**

- [x] **SF.0** M7, M8, M21, M23 (a)-(d), M25, with the M8 upload and the M21 / M23 probes built by the user in the
  designer; results recorded with date, package version and basis in the analysis open-questions.

**CrtProcessBuilder** (base `main`; draft on story 2's branch until it merges)

- [x] **SF.1** `Resolve` SysFile mode lifted per variant through the registry (Object read and write; Report and
  Process targets), only for the cases whose measurement passed; the SysFile scope in the shape M7 captured, `Split`
  accepting both shapes; the SysFile-root sort rule (record-object columns refused); the `RecordSchemaName`
  expectations; the M25 designer-flag notice (only if M25 confirmed the read) through the feature seam of story 2; the
  SysFile-to-SysFile retarget. Files: `PB/FileProcessing/AttachmentStorageResolver.cs`,
  `FileProcessingScopeFilter.cs`, `PB/Filters/FileProcessingFilterTarget.cs`,
  `PB/FileProcessing/FileProcessingFeatureReader.cs`.
- [x] **SF.2** Tests: the resolver in SysFile mode; scope compile and `Split`; a SysFile-to-SysFile retarget
  (Account address -> FP Doc) clears the scope; the SysFile-root sort rule; the registry test with the SysFile switch
  flipped (the notice through the faked seam dropped with SF-1e); the capture doc checked against M21. Restamp
  LAST to the number claimed before cutting (PR description AND the shared cut claim on ENG-92719 File processing
  element); tag verified on the remote; diary entry.

**clio** (base `master`)

- [x] **SF.3** Rebundle with **no floor raise** (an older server refuses SysFile loudly); the SysFile e2e (a region of
  `ProcessFileElementToolE2ETests` plus the targets in `ProcessFileReportAndParameterToolE2ETests`, floor 1.6.6.111;
  the setFilter `RecordId` refusal, floor 1.6.6.112): an Object element on a SysFile-stored object (create, describe,
  `saveTo` a SysFile target); Report and Process into a SysFile target; `Assert.Ignore` when no record of the object
  exists; story 1's SysFile-refusal e2e re-pointed to its positive form (the F1 case describes only, see below). Knowledge records: DELETE
  `docs/knowledge/ProcessModel/process-file-sysfile-storage-is-refused-until-sf.md`; UPDATE
  `platform/attachment-storage-is-chosen-per-entity-by-the-designer.md`; NEW
  `platform/a-sysfile-attachment-read-needs-recordschemaname-and-type-file.md` (after M8) and the write-path record M23
  produces (not written: M23 found nothing the code does not say - the copy into SysFile stamps `RecordSchemaName`
  in the shipped `FileProcessing` code, and the M8 record already covers the read side). No description change. Status flips: this story's `in-progress`; story 2's `done` does NOT ride in this
  PR, because story 2's Definition of Done still has its Jira item open (RP-1..RP-10 not pasted as ENG-96506's
  acceptance criteria), so it stays `review`.
  Carried over from story 2 on 2026-10-20 (the SysFile work is tracked in Jira as ENG-103937 Process file element:
  SysFile attachment storage, per the ENG-96506 delivery comment 558569): rebuild `UsrFpOaDesignerFixture`
  in the designer as story 1's OA.0 describes (it was removed from the stand with its Custom package and not rebuilt),
  and run story 2's `DesignerFixture_Should_DescribeTheReportElement_AsFileProcessing` (TC-I-46, extended) together
  with story 1's two designer-fixture cases, Passed with Ignored = 0. **Not done in this PR**: the fixture is still
  absent from the stand, so the fixture cases stay Ignored; the SysFile half is covered by `UsrFpSc1SfCapture` (SC-4,
  DT-04) instead. Done differently from the plan: the SysFile cases are a region of `ProcessFileElementToolE2ETests`
  (not a new file: `Select-McpE2eTestFilter.ps1` keeps one source file per fixture class, so a partial fixture split
  across files loses its tool references and drops out of pull-request selection), the Report and Process targets are
  in `ProcessFileReportAndParameterToolE2ETests`, the F1 case is describe-only (F1 is never written: a builder write would replace the designer's serialization the
  other fixture cases compare against - an open owner decision, see the code review of 2026-10-20), and the
  knowledge records are `platform/sysfile-record-scope-shape-depends-on-the-primary-display-column.md` (M7) and
  `platform/freedom-ui-upload-stamps-record-schema-name-from-the-page.md` (M8).

**clio-knowledge**

- [x] **SF.4** `process-files` storage section rewritten: the SysFile hazards (record-object sort columns,
  `RecordSchemaName`, "the user checks the record's Freedom UI attachment list"); the refusal sentences removed behind a
  version gate ("from clio <version>", the real number at merge); the guidance rules of story 1 apply; libraryVersion
  above master at merge. Done differently (owner, 2026-10-20): the article had no room, so the Process parameter group
  moved into its own article `process-files-parameter`; the boundary is the package version ("1.6.6.109 or older
  refuses SysFile", with the `install-process-builder` remedy), since no clio release number exists yet; the storage
  rule by name (`<X>File`, `FileLead`, an unpaired attachment object named directly) added; source-only fact-checked;
  libraryVersion 1.15.168 (clio-knowledge #300).

**Tail**

- [ ] **SF.5** Stand proofs (below); gates 1 and 3; one review round; rebundle and reinstall; knowledge publication.

---

## Stand verification

After the cut, before the package PR leaves draft (the common protocol of story 1; the behaviour probe: a SysFile
`saveTo` is accepted):
- **ST-08** (a) Account attachments saved to an Account address record (SysFile): 2 SysFile rows with `RecordId` = the
  address, `RecordSchemaName = 'AccountAddress'`, `Type = File`, `Version = 1`; (b) a second process reads that
  address's attachments (SysFile source) and saves them to a Contact through the Process variant: 2 ContactFile copies;
  (c) optional: a Freedom UI upload on a record of a SysFile-stored custom object, read by an Object element. The user
  sees the files in the Freedom UI attachment list.
- **DT-04** the user opens ST-08's elements and compares with the M21 capture: metadata unchanged by a no-op save, and
  equal to M21 under the TC-41 rule.
- **SC-4 (TC-M-20)** the builder twin of SC-1's E3, E4, E5 and E11: equal under the TC-41 rule; this closes the SF
  serialization criterion.
- **e2e** the SysFile region of `ProcessFileElementToolE2ETests`, the SysFile targets of
  `ProcessFileReportAndParameterToolE2ETests` and story 1's re-pointed case: Passed with Ignored = 0.

A failing case keeps its refusal and narrows this story (D15 fallback: if M23 fails, SysFile sources only, if M8
passed).

## Owner decisions (2026-10-20)

The SC-4 / DT-04 comparisons with `UsrFpSc1SfCapture` left these differences; the owner accepted each as named, so
the TC-41 rule tolerates them for SysFile elements:
- `leftExpressionCaption` of a filter leaf (edit form): the builder writes the column path (`RecordId`), the designer
  the column caption (`Record Id`), for every filter the service builds; nothing reads it at run time, and a designer
  re-save rewrites it. A change for all filters would be a separate issue.
- An Object element nobody filtered (E5): the designer stores an empty filter group rooted on SysFile, the builder
  stores none (no condition either way, by source; not run).
- Process parameter `Files` from a FileCollection process parameter (E11): the designer bound the nested `File` alone,
  to the collection itself; the builder binds both levels (the shipped `FileParameterProcess` Process3 form and the
  designer's own M23d element, which ran). describe flags the designer's E11 binding.
- The display value of a record taken from a top-level process parameter (`PContact` vs `[#PContact#]`): the known
  "top-level display values" gap of every element, not SysFile's to fix.
- The designer renders a constant record of an object without a primary display column as
  `[#Lookup.Account address.<record not found>#]`; the builder stores no display value there.

Fixed instead of accepted: a SysFile side of an object that has its own attachment object (SysFile + Account) is kept
on update but named as a describe issue (package 1.6.6.111).

## Status convention

`ready-for-dev` when the Sub-task exists and the measurements are recorded (a clio commit); then as in story 1:
`in-progress` when this story's clio draft opens; `review` when it leaves draft. This is the last story of the main
chain, so its `done` flip rides in the next clio PR that touches `spec/sprint-status.yaml`, whichever that is
(pr-split section 5.6 expects the first clio PR of ENG-95985 Send email attachments, by link L5), and is never pushed
after an approval.

## Definition of Done

- [x] M7, M8, M21, M23 (a)-(d) recorded before the code that depends on each (M25 dropped by the owner); SysFile
      lifted only for the cases that passed (all of them).
- [x] Package tests green on both configurations; the capture doc checked against M21; restamped last; tag verified on
      the remote.
- [ ] clio: rebundle with no floor raise; the SysFile e2e (see "Testing") Passed with Ignored = 0 (today 1 Ignored:
      the designer-fixture case, `UsrFpOaDesignerFixture` absent); story 1's
      refusal e2e re-pointed; the temporary refusal record deleted and the storage record updated; full unit suite
      green; the PR-description statements of story 1.
- [ ] clio-knowledge: the storage section rewritten; the knowledge suites green; published, and `info-knowledge` shows
      it.
- [ ] ST-08, DT-04 and SC-4 (TC-M-20, the story 3 rows) passed; the user confirmed each written file in the record's
      Freedom UI attachment list; every test-plan case whose Story column names 3 implemented.
- [ ] Review gates 1, 2 (per commit, tier stated) and 3; no open Blocker or High.
- [ ] The issue created in Jira (ENG-103937, done) and its key written into this file and its
      `spec/sprint-status.yaml` row (done); SF-1 confirmed (or narrowed) by the owner and pasted as its acceptance
      criteria; link L4 (ENG-96506 blocks this issue) in place; PR titles exactly
      `ENG-103937 Process file element: SysFile attachment storage`; merged by a human package -> clio -> knowledge.

### Story-level Definition of Done: ENG-92719 File processing element

The Story has no row of its own in `spec/sprint-status.yaml`; its Definition of Done (analysis plan section 11) lives
here, because this story is the last of its main chain. ENG-92719 closes when all of these hold:
- [ ] This BMAD set (the docs-only clio PR titled `ENG-92719 File processing element`) merged before the first FE code
      PR opened.
- [ ] ENG-96505 Element readiness and object attachments mode (story 1), ENG-96506 Generated report + process parameter
      modes (story 2) and this Sub-task done, each when its own knowledge PR merged. Stories 4 and 5 do not gate it.
- [ ] Jira: the Story's acceptance criteria replaced with FE-1..FE-6 (FE-1 as agreed with Q13 on 2026-10-14, amended if SF
      narrowed: the PRD's open item "What FE-1 means if SF narrows"); the criteria of the issues of stories 1-3 replaced;
      the D23 re-links L1-L5 done.
- [ ] Each FE row traceable to a test or a guide section (the test plan's traceability table): FE-1 and FE-2 (the OA and
      RP e2e), FE-3 (`process-files`, the D17 refusal), FE-4 (ENG-95984 plus the value-source e2e), FE-5 (the three
      captures plus M21), FE-6 (Read data -> Process file, the Modify data re-find and the per-file multi-instance case in
      story 1; the Object -> Process chain and Add data -> report in story 2; Send email in ENG-95985 Send email
      attachments; Creatio.ai stated as not buildable).

## Dev Agent Record

- Jira key and creation date: ENG-103937 Process file element: SysFile attachment storage (a Task of ENG-92719).
- Measurement results (2026-10-20, 1.6.6.109, CrtProcessDesigner 7.8.0): M7 done - lookup InFilter for a record object
  with a primary display column (UsrFpDoc), Guid compare without one (AccountAddress); M8 PASS (a `create-app` page's
  upload stores `RecordSchemaName` and Type File; an Object element copied it); M21 captured; M23 (a) PASS 3 rows,
  (b) PASS 1 copy, (c) PASS the `.docx`, (d) PASS 2 rows, all stamped and visible in the Freedom UI list; M25 dropped.
- Package cut claimed: 1.6.6.110 (2026-10-20, superseded by the owner's describe-issue decision), 1.6.6.111
  (2026-10-20, superseded by the code-review fixes) and 1.6.6.112 (2026-10-20), all in ENG-92719 comment 550565;
  producing commit `7840df0f`, restamp `7d9df4c0`, tag `crtprocessbuilder-1.6.6.112`; package PR
  https://creatio.ghe.com/engineering/crt-process-builder/pull/101 (draft).
- Tests passing: package 4315/4315 on `-c dev-nf` and `-c dev-n8`; clio unit (Command, McpServer, Common) 15310/0 on
  the 1.6.6.112 bundle; e2e on 1.6.6.112 (net10.0): ProcessFileElementToolE2ETests and
  ProcessFileReportAndParameterToolE2ETests 52 passed, 3 Ignored (the designer fixtures are absent); knowledge
  539/539.
- Stand records (on 1.6.6.110; SC-4 repeated on 1.6.6.111, identical): ST-08 (a) 3 SysFile rows on an Account address,
  `RecordSchemaName = AccountAddress`; (b) 3 ContactFile copies through the Process variant; (c) the Freedom UI upload
  copied; the Contact's Freedom UI list shows all four. SC-4 equal except the accepted differences. DT-04 (twin and
  ST-08 processes): the designer re-save rewrote only the caption and E5's empty group; no
  `SourceDataEntitySchemaUId` appeared.
- Notes: no case narrowed, X3 did not fire; M25 dropped; story 1's designer fixture not rebuilt (its cases stay
  Ignored).
