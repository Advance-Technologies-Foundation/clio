# PRD: Process file element (ENG-92719)

**Jira**: ENG-92719 File processing element (Story; epic ENG-92704 Create BP via AI Toolkit), delivered through
ENG-96505 Element readiness and object attachments mode (OA; a Task that relates to ENG-92719, converted from a
Sub-task on 2026-10-14), ENG-92719's Sub-task ENG-96506 Generated report + process parameter modes (RP), and a new Sub-task "SysFile attachment storage in the Process file element" (SF; not created in
Jira as of 2026-10-14, its key is assigned on creation)
**ADR**: [adr-eng-92719-file-processing-element.md](../adr/adr-eng-92719-file-processing-element.md)
**Stories**: [1](../stories/story-eng-92719-file-processing-element-1.md) (OA) ·
[2](../stories/story-eng-92719-file-processing-element-2.md) (RP) ·
[3](../stories/story-eng-92719-file-processing-element-3.md) (SF) · side Sub-tasks
[4](../stories/story-eng-92719-file-processing-element-4.md) ·
[5](../stories/story-eng-92719-file-processing-element-5.md)
**Test plan**: [tp-eng-92719-file-processing-element.md](../test-plans/tp-eng-92719-file-processing-element.md)
**Analysis**: [`spec/eng-92719-file-processing-element/`](../eng-92719-file-processing-element/README.md) on the same
branch (README first). It holds the evidence: the platform reference, the serialization capture, the use-case
inventory, traps T-1..T-68, decisions D1-D30 with their source anchors, the plan, the test plan TC-01..TC-92, the PR
split and the open questions. This PRD restates what the stories need; where it is shorter, the analysis holds the
detail.
**Status**: Accepted in part. The owner agreed Q1-Q12 on 2026-10-07 (Q7 answered by stand measurement M3), Q19 on
2026-10-08, and Q13-Q18 (as recommended) and Q20 (A: trim existing text) on 2026-10-14. Among them: the replacement acceptance criteria (Q1), the
Jira links (Q2), SysFile storage in its own Sub-task (Q3), RP before SF (Q4), the consumer split (Q5) and the PR split
(Q6). The six element-contract questions Q13-Q18 were agreed as recommended (see "Open owner items"), so the rows of
the replacement text that encode them stand as written: they name their question in the column "Owner answer" below,
and are pasted into Jira (plan FE.0b). The SF criterion is proposed, not agreed (decisions D-5); it is confirmed when the SF Sub-task is created.
**Created**: 2026-10-14
**Size estimate**: three main issues (OA, RP, SF), each one package -> clio -> knowledge PR triple, plus two side Sub-tasks of
size S. With an AI agent writing the code: about 26.5-52.5 h of agent time and 15-33.5 h of the owner's time, about
7-10 working days of calendar after ENG-95984 File process parameter type merges (analysis plan section 8). The side
Sub-tasks are outside that total, about 4-6 h each for their triple (inference). The full BMAD form is used because the
Story has five stories.

Baselines: CrtProcessBuilder `main` at 1.6.6.96 (`a6bfcbf6`); clio `master` `79fab4786` (bundles CrtProcessBuilder
1.6.6.96; the create, modify and modify-as-new-version floors at 1.6.6.92); clio-knowledge `master` at libraryVersion
1.15.143. ENG-95984 File process parameter type, which this work follows, is in flight: its package cut, the number
in the shared claim list (ENG-92719 comment 550565), is in the draft
https://creatio.ghe.com/engineering/crt-process-builder/pull/96 (1.6.6.97 was installed once and burned, 1.6.6.98
superseded), and its clio and knowledge PRs are open as
https://github.com/Advance-Technologies-Foundation/clio/pull/1814 and
https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/285. The analysis anchors point to clio
`19e6f9c53` and clio-knowledge `0b56d4a`; the description budget and the client-side key check below are read on clio
`79fab4786`. Stand measurements of 2026-10-02 ran on CrtProcessBuilder 1.6.6.54. Platform claims were read in Creatio
core 10.1.37. Basis labels: *source* (read in code or metadata; a runtime claim with this label is a hypothesis),
*measured* (observed on the stand or counted over the shipped corpus, with the date), *inference*.

---

## Why

An agent that builds a process through `create-business-process` / `modify-business-process` cannot configure the
"Process file" element. The only route today is the generic `{type: userTask, userTaskName:
"ObjectFileProcessingUserTask"}`. It draws the element correctly and configures nothing: no `ResultActionType` (so the
element runs as "Save to object attachments"), no source, no target. The element saves green and throws at run time on
`GetInstanceByUId(Guid.Empty)` (source; still so on 1.6.6.96). `describe-business-process` reports it as a generic
`usertask` whose raw parameter list hides the inherited defaults, an empty link column and a nested-only `Files`
binding, and shows `ReportId` and `TargetEntitySchemaUId` as bare GUIDs (measured on PrintQuotationReport, 2026-10-01).

Three facts shape the work:
- **"Process file" is three user-task schemas behind one caption.** "What is the source of the file?" is not a
  parameter: each option is a schema, the palette offers only the Object attachments schema, and a source change
  replaces the element (a new name, every value dropped), which the designer refuses while other elements reference it
  (source; the palette and the replace-menu state measured 2026-10-01).
- **The platform accepts almost every configuration mistake silently.** No variant has a `Validate` override, and
  `IsRequired` is never enforced (source). An empty filter reads 50 arbitrary files (Object) or generates one Word
  document per record of the whole entity (Report); the natural filter `{object: "Account", Id = X}` runs as
  `AccountFile.Id = X`, finds zero files and reports success; a same-schema copy with no link column lands on the source
  record as a duplicate; an outer-only `Files` binding gives rows `{File: null}` (all source). The traps catalogue of
  the analysis has 68 entries, 59 of them silent. Most of the work is therefore refusals, a storage resolver, a record
  scope compiled into the filter, and a describe block that round-trips.
- **Attachment storage is a second axis.** The designer chooses storage per object: an object with its own `<X>File`
  object keeps it (Account -> AccountFile; Lead -> FileLead), and every other eligible object is stored in SysFile
  (measured in the designer, in memory: the per-object choice, and for a SysFile source the file object SysFile with
  `SourceDataEntitySchemaUId` = the record object; UO-2..UO-4, 2026-10-01). The `RecordId` link and scope and a SysFile
  target's `TargetDataEntitySchemaUId` are source, not measured (pending M7, M21, M23). 0 of 30 shipped elements use
  SysFile (corpus), and its read and write paths are source traces only, so SysFile storage ships in its own Sub-task
  after five stand measurements.

What the Jira acceptance criteria say against what is true (the full table is the analysis plan section 1.3, N1-N16):

| Jira AC says (plan section 1.2 id) | What is true |
|---|---|
| "Values set via Task 6, including the Binary / File parameter type (Task 5)" (AC-FE4) | A file travels by reference: one file is FileLocator, several files a collection with one FileLocator item. Binary cannot be held by the flow engine. "Task 6" and "Task 5" are ENG-91844 Implement full parameter mapping (sources) and ENG-95984 File process parameter type |
| "Server serialization matches a designer-built capture per source variant" (AC-FE5, AC-OA7, AC-RP6) | Per variant AND per storage. No shipped SysFile capture exists (0 of 30), and a bare "UIds ignored" rule fails on four keys the builder writes differently, so the comparison is a written rule with four named tolerated deviations (ADR D20) |
| "Tests cover the five patterns", all combined with "Send email, Read data, Add data, Modify data, or a Creatio.ai call" (AC-FE6) | Read data and Add data never consume a file output (0 corpus cases); they sit upstream. Send email and the Creatio.ai call read files through dynamic slots the builder cannot create. Send email moves to ENG-95985 Send email attachments; ENG-92725 Execute AI Intent element (BP generation) is closed as Won't Do |
| "Process file is rarely an endpoint" (AC-OA8) | In shipped product content it is the endpoint: 4 product processes hold 5 elements, all Generated report saved to the record's attachments, and none maps an output of the element (corpus, 2026-10-01) |
| The source object and the record are "set through the established value sources" (AC-OA5) | The object is a schema choice; the record is a condition inside `DataSourceFilters`, not a parameter |
| An existing element "including a source variant not configurable in this sub-task" reads back without loss (AC-OA2) | Each cut claims only the variant it configures; Report and Process-parameter elements stay generic user tasks until RP |
| Reports "that already exist on the target object" (AC-RP1) | A printable's object is the record being reported on, not the save target. Only MS Word is accepted here: FastReport has no stand evidence, DevExpress throws at run time |
| Process-parameter files "handed to a following element as a file collection" (AC-RP5) | "Use in process" throws `NotSupportedException` for that source; the copies are available to following elements after saving |
| "read back by `describe-process`" (AC-FE1) | The tool is `describe-business-process` |

## Goal

`create-business-process` and `modify-business-process` build and change the Process file element as one element type,
`fileProcessing`, for all three source variants, and refuse the shapes that save green and fail at run time.
`describe-business-process` reads every such element back, designer-built ones included, in a block that rebuilds it.

## Users

- A coding agent driving the clio MCP process tools (create, modify, describe), and a developer using the same tools.
- The product's own recipe: a Generated report from an MS Word printable, saved to the record's attachments (all 5
  elements of the 4 shipped product processes that hold the element, corpus 2026-10-01).
- The next ticket of the chain, ENG-95985 Send email attachments, which takes its files from this element (Jira link
  L5).

## Scope

| Variant (the group sent) | User-task schema | Schema UId | Delivered by |
|---|---|---|---|
| Object attachments (`attachments`) | `ObjectFileProcessingUserTask` | `9387c794-8d84-5925-ab77-c47e7d876286` | OA (story 1) for objects with their own attachment object; SF (story 3) for SysFile storage |
| Generated report (`report`) | `ReportFileProcessingUserTask` | `c2bf0416-54c6-6c56-58e0-41162c7795f0` | RP (story 2); SysFile targets in SF |
| Process parameter (`files`) | `ProcessFileProcessingUserTask` | `6c620dd2-026e-560c-489f-030c5be5f2c3` | RP (story 2); SysFile targets in SF |

- **OA (story 1)**: the element family. One element type `fileProcessing` (second token `processFile`) whose variant is
  the group the caller sends; the variant registry; the handler, binder and applier; the storage resolver in dedicated
  mode; the record scope `attachments.recordId`, compiled into the filter; a dedicated filter target rooted on the
  runtime root; the readData sort codec extracted for shared use; the empty-filter refusal and notice; the typed
  describe block; the generic-route and raw-mapping refusals for the Object schema. SysFile sources and targets are
  refused with a message, and designer-built SysFile elements are described without loss. Report and Process-parameter
  elements stay generic user tasks.
- **RP (story 2)**: the Report variant (MS Word printables only, chosen by `templateId`, lookup macro or caption, with
  `report.recordId`) and the Process-parameter variant (`files`, bound at both levels through ENG-95984's
  `BindCollection`; a single File only if stand measurement M1 passes). The four shipped product report processes start
  describing as `buildType: fileprocessing`.
- **SF (story 3)**: SysFile storage for all three variants, after stand measurements M7, M8, M21, M23 and M25.
- **Side Sub-tasks**, off the critical path, not file-specific: "Refuse collection parameters as filter values"
  (story 4) and "Builder-made user tasks do not set SerializeToDB" (story 5).
- **Per cut, in clio**: the rebundle; the floor raise (OA, RP; none for SF); the descriptions; the `fileProcessing` key
  set of the client-side key check; the `fileProcessing` entry in the short create contract's call shape; the typed
  describe DTO; the `list-user-tasks` description and the four process prompts; capability-map rows; knowledge records;
  a new MCP e2e fixture. **In clio-knowledge**: sections of the `process-files` guide (born in ENG-95984's knowledge
  PR) and the `element-catalog` row.

## Non-goals

- Creating a printable. The element never creates one and never picks an unrelated one; the refusal tells the user to
  create it in System Designer, Report setup.
- FastReport and DevExpress printables: refused in this work, additive to lift later (ADR D17).
- Process file -> Send email attachments (ENG-95985 Send email attachments) and Process file -> a Creatio.ai call (not
  buildable through this tool; the guidance says "not through this tool yet", never "Creatio cannot").
- `linkColumn: "none"` and an explicit `storage: "sysFile"` override (v1 refuses both; revisit after measurement M12).
- "Attachments of the records matching a condition" in SysFile mode (an unmeasured reverse join; in dedicated storage it
  works through the link lookup).
- Multi-column sort: the readData shape takes one entry; a stored multi-entry `OrderByInfo` is kept while `sort` is not
  sent.
- A new report-lookup MCP tool: `list-printables` is the discovery route.
- Fixing the designer's storage re-save hazard H-G3-1 in CrtProcessDesigner. It is reported through a Sub-task only if
  M11 confirms it; M11 (a) and (b) did not reproduce it in memory on 2026-10-02.
- A file passed as a `run-process` start value, and any constant, upload or byte form of a file.
- creatio-ui (verified to hold no file-processing logic; the diagram needs only what the handler writes) and ClioRing
  (it consumes catalog names, Purpose text and Destructive flags, none of which change).
- The three ENG-95984 follow-ups: ENG-103162 Declared item shape for Collection process parameters, ENG-103163
  Allow-list the data types a typeFromElement mirror may copy, ENG-103164 Describe: decode process-parameter sources
  into re-appliable names.

## Acceptance criteria

The criteria below **replace** the acceptance criteria of ENG-92719 File processing element, ENG-96505 Element readiness
and object attachments mode and ENG-96506 Generated report + process parameter modes. The owner agreed the replacement
on 2026-10-07 (Q1, decision D24). The text pasted into Jira is Part D of the analysis decisions (D-2, D-3, D-4). The
SF criterion is the SysFile row of D-5, which is a proposal: Q1 covered ENG-95984, FE, OA and RP only. The tables below
are a faithful condensed copy with editorial differences only, except where a row says otherwise. The labels (FE-n,
OA-n, RP-n, SF-1) number the replacement bullets in order, as the analysis test plan section 11 does; the plan's
section 1.2 ids (AC-FE1..AC-RP9) number the Jira text as written and are not used here.

**Rows that encode an owner answer.** The column "Owner answer" names the question whose answer the row encodes (see
"Open owner items"). Q13-Q18 were agreed as recommended on 2026-10-14, so these rows stand as written and are pasted
into Jira (plan FE.0b); the test plan lists the test cases tied to each question. Member names in any row
(`fileProcessing`, `attachments`, `report`, `files`, `saveTo`, `recordId`, and the rest) follow Q13 and Q14, as agreed.

**The comparison rule** (shared by FE-5, OA-10, RP-8 and SF-1; analysis serialization-capture section 10): element UId,
name and lane are ignored (elements are paired by caption), and so are parameter and nested UIds; a nested item's `IL2`
is still compared. `ModifiedInSchemaUId` (`GS5`) is normalised for values equal to the template default. Four
deviations are named and tolerated: N2 (`BL8` absent on builder-made elements), N9 (a builder-made process parameter
stores `IL2` = the process UId), N10 (a nested item has no `BK15` mapping row), N13 (the DisplayValue rows of hidden
constants are reported, not compared; displayed values must match). The format normalisations N11 and N14-N16 apply.
**Predated parameters**: a parameter that the shipped oracle predates is excluded from that comparison and NAMED in the
test, until the SC-1 designer capture replaces the oracle. The named set today (analysis serialization-capture section
1 and test plan TC-41, TC-69): FileCopyProcessPP's Object element (the save-side oracle) lacks `ConsiderTimeInFilter`,
`SourceDataEntitySchemaUId` and `TargetDataEntitySchemaUId`; FileParameterProcess's two Process elements lack
`TargetDataEntitySchemaUId`, and its source FileCollection's item `FileCollectionParameter` is role-normalised to `File`
(the item name of ENG-95984's FileCollection). PrintInvoiceReport carries the full set in the corpus; SC-0 (TC-M-18)
records the set the stand holds, and TC-U-59's exclusions are taken from that record (none expected). Any other
difference fails. This exclusion class is not new: the analysis test plan decided it in TC-41 and TC-69, and decisions
Part D's shorter "any other difference fails" is read with it; the PRD states it here as a clarification. The test
plan calls the whole rule the TC-41 rule.

### ENG-92719 File processing element (Story)

| # | Acceptance criterion | Owner answer |
|---|---|---|
| FE-1 | All three source variants (Object attachments, Generated report, Process parameter) and the save target can be built through create and modify as one element, type `fileProcessing`, and read back by `describe-business-process`. Delivered by OA (Object attachments), RP (Generated report, Process parameter) and SF (objects without their own attachment object) | Q13, agreed 2026-10-14: one type `fileProcessing`; the SF half depends on SF (see "Open owner items") |
| FE-2 | An element's output collection can be mapped by more than one downstream element | - |
| FE-3 | Guidance covers which source variant to pick for a given intent and the element pairings from the use-case inventory. Creating a new printable is out of scope: when none fits, the refusal tells the user to create it in System Designer, Report setup, and the element never picks an unrelated one | - |
| FE-4 | Values are set through the ENG-91844 Implement full parameter mapping (sources) value sources, including File and FileCollection parameters (ENG-95984 File process parameter type) | - |
| FE-5 | Server serialization matches a capture per source variant and per storage: SkillFilesValidationProcess (Object attachments, dedicated storage), PrintInvoiceReport (Generated report), FileParameterProcess (Process parameter); SysFile storage is compared in SF against a designer capture taken on the stand (M21), because no shipped SysFile capture exists. Under the comparison rule above | - |
| FE-6 | Tests cover Read data -> Process file, Process file -> Modify data (re-finding the created file), Process file -> multi-instance sub-process per file, Object attachments -> Process parameter chain, and Add data -> generated report. Process file -> Send email is covered by ENG-95985 Send email attachments; Process file -> Creatio.ai call is out of scope, and the guidance says it is not buildable through this tool yet. Docs and MCP surface updated | - |

### ENG-96505 Element readiness and object attachments mode (OA)

| # | Acceptance criterion | Owner answer |
|---|---|---|
| OA-1 | The element (type `fileProcessing`, alias `processFile`) can be added through create and modify and connected like any other element; it is drawn as the designer draws it (manager item = the schema, 69x55, in a lane) | Q13, agreed 2026-10-14: token `fileProcessing`, alias `processFile` |
| OA-2 | `describe-business-process` returns the element with its source variant and configuration in a `fileProcessing` block in the build vocabulary; feeding the write members back rebuilds it. An Object element in an existing process, designer-built or builder-built, is read back and modified without loss. Report and Process-parameter elements are described and preserved as generic user tasks until RP; a modify of a process that holds one leaves it intact. A designer-built SysFile element is described without loss; changing it is refused until SF | - |
| OA-3 | The element's output file collection can be mapped by more than one downstream element | - |
| OA-4 | Element identity resolves to the user-task schema per source variant; the variant is the group the request sends, so adding a variant later does not change the layout contract | Q13, agreed 2026-10-14: the group present is the variant |
| OA-5 | Building ObjectFileProcessingUserTask as a generic `userTask` is refused with a message that points to `fileProcessing` | - |
| OA-6 | The source object is chosen by name in `attachments.object`, and its attachment storage is derived per object as the designer derives it. SysFile storage is not part of this issue: such sources and targets are refused with a message, designer-built SysFile elements are described without loss, and SF delivers the support | Q17 for the details, agreed 2026-10-14: the designer flag is not read for behaviour; no `linkColumn: "none"`; no SysFile override |
| OA-7 | The record whose attachments are read is set in `attachments.recordId` through the value sources: a record id, a process parameter, an element output or one column of it, or `[#SysVariable.CurrentUserContact#]`. The number of files (default 50, at most 5000) and one sort column can be set | Q16 and Q14, agreed 2026-10-14: the record scope inside the group; `numberOfRecords` defaults to 50 |
| OA-8 | Files can be saved to a target record's Attachments detail (`saveTo`), with the record set through the same value sources; a constant record id must be an existing record of the target object | - (the row's text encodes no owner answer; the action inference that stories and tests attach to `saveTo` is Q15's, agreed 2026-10-14) |
| OA-9 | A saving element with no record scope and no filter condition is refused; a "use in process" element without one gets a notice | Q16, agreed 2026-10-14: refuse |
| OA-10 | Server serialization matches the designer-built capture SkillFilesValidationProcess, under the comparison rule. The test plan's TC-41 also compares the save side against FileCopyProcessPP, with the parameters it predates excluded and named (a PRD addition taken from the analysis test plan; decisions D-3 names only SkillFilesValidationProcess) | - |
| OA-11 | Guidance states "Most shipped uses end in a record's attachments; when a following element needs the files, map the element's output collection." (pinned by TC-88), covers the Object attachments pairings from the use-case inventory, and says which consumers are not buildable yet | - |
| OA-12 | Unit tests, e2e, stand verification, clio MCP tool descriptions and clio-knowledge guidance updated in the same change, merged package, then clio, then knowledge. Public surface: coordinated change | - |

### ENG-96506 Generated report + process parameter modes (RP)

| # | Acceptance criterion | Owner answer |
|---|---|---|
| RP-1 | The report is chosen in `report.printable` from the MS Word printables whose object is the record being reported on, preferably by the `templateId` that `list-printables` returns (a caption or a lookup macro is also accepted; an ambiguous caption is refused with the candidates). The record is set in `report.recordId` through the value sources. FastReport and DevExpress printables are refused | Q18, Q16 and Q14, agreed 2026-10-14: FastReport refused; `report.recordId` in the group; `report.printable` takes an id, a macro or a caption |
| RP-2 | The generated files can be saved to a record's Attachments detail, or handed to a following element as a file collection (temporary files that live as long as the process instance; source, pending M16, and TC-M-07 (ST-07) closes it) | - |
| RP-3 | Creating a new printable is out of scope. When no suitable printable exists, the refusal tells the person to create it in System Designer, Report setup, and the element never picks an unrelated printable | - |
| RP-4 | A Word report with no record scope and no filter condition is refused | Q16, agreed 2026-10-14: refuse |
| RP-5 | The Process-parameter source is set in `files`: a FileCollection or File process parameter, an element's file output, or a formula. A collection source binds both levels of the element's `Files` input. A single File is accepted if M1 shows that the platform copies it; otherwise it is refused, and the guidance says to wrap it in a FileCollection | - |
| RP-6 | The Process-parameter files are saved to a record's Attachments detail (a target is required); the copies are then available to following elements (`ObjectFiles.ObjectFile`, `CreatedObjectFileIds`). "Use in process" does not exist for this source and is refused | - |
| RP-7 | `describe-business-process` round-trips both variants (the source, the printable or the input files, the record, the save target). The shipped product processes with report elements describe as `buildType: fileprocessing` (checked read-only) | - |
| RP-8 | Server serialization matches the shipped captures PrintInvoiceReport (Generated report) and FileParameterProcess (Process parameter), under the comparison rule (`Files.File` has no `BK15` row: N10; `ReportId` and `IsSeparateReports` DisplayValue rows: N13; FileParameterProcess's predated `TargetDataEntitySchemaUId` excluded and named, its item `FileCollectionParameter` role-normalised to `File`) | - |
| RP-9 | Guidance covers which source variant to pick and the pairings of these two variants: Read data, Add data (creating the report's data row), Modify data (re-finding the file); it states that Send email attachments and Creatio.ai file passing are not buildable through this tool yet | - |
| RP-10 | Unit tests, e2e, clio MCP tool descriptions and clio-knowledge guidance updated in the same change | - |

### SysFile attachment storage in the Process file element (SF)

**Proposed, confirmed when the SF Sub-task is created** (decisions D-5 lists it as a proposed Sub-task; Q1 agreed the
criteria of ENG-95984, FE, OA and RP only).

| # | Acceptance criterion (proposed) |
|---|---|
| SF-1 | Objects without their own attachment object can be read from and saved to (SysFile storage) by all three variants, after stand measurements M7, M8, M21, M23 and M25; the record scope uses the shape the designer writes; record-object sort columns are refused; a SysFile-mode element built where the designer's storage flag reads off gets a notice; serialization matches a designer capture taken on the stand (M21) under the comparison rule; guidance states the SysFile hazards. A case whose measurement fails keeps its refusal (a PRD addition, taken from D15's fallback: "if M23 fails, SF ships SysFile sources only, if M8 passed, and keeps the target refusal") |

The two side Sub-tasks have no agreed criteria yet (D-5: "to be refined"). Stories 4 and 5 carry proposed criteria,
confirmed when their Sub-tasks are created.

## Constraints

- **C1 No `Terrasoft.Configuration` reference.** Package production code and tests never name the three user-task
  classes or other configuration types: the dev-n8 build has no such assembly. Production code mirrors the three schema
  NAMES (identity resolves by name); the UIds live in test fixtures and the drift guard only. Package sources are C# 7.3.
- **C2 Per-cut scoping.** Each cut claims only the variants it makes configurable (identity, generic-route refusal,
  raw-mapping refusal, filter-target claim, describe block), all five together through the variant registry, so every
  release is self-consistent.
- **C3 Versions and floors.** A cut takes the first free number above the highest number claimed anywhere at cut time
  (clio's bundle, package `main`, every branch and tag of both repositories), claimed before cutting in the PR
  description and in the one shared cut claim, a comment on ENG-92719 File processing element (comment 550565),
  which alone holds the numbers: each cut takes the next free number in it at cut time. The create, modify and
  modify-as-new-version floors move to the FINAL cut of the OA and RP clio PRs; SF raises no floor (an older server
  refuses SysFile loudly).
  Each rebundle goes through `rebundle-process-builder.ps1 -Version <claimed>`; the version only goes up.
- **C4 Description budget.** On clio `79fab4786` (2026-10-14) create-business-process has 11 B and
  modify-business-process 2 B left under the 35,072 B per-contract ceiling; ENG-95984's clio PR trims first. OA adds
  about +128 B on create and +74 B on modify (estimated), and its `fileProcessing` entry in the short create contract's
  call shape (about 25-30 B, never cut) lands under the 19,000 B wire guard for the seven process-designer contracts,
  which had 14 B left (18,986 B measured on clio `19e6f9c53`). The bytes come from reviewer-approved trims of existing
  text (Q20, agreed 2026-10-14: A); the ceilings are not raised. Describe had 1,053 B left (measured on clio
  `19e6f9c53`, 2026-10-13) and OA's +128 B fits. A floor raise that lengthens the version literal costs a byte
  in three descriptions; the budget test catches it at that cut (ADR D25).
- **C5 Client-side key check.** Since ENG-103085 Refuse element and block keys the process builder would drop silently
  (clio master `79fab4786`), clio refuses an element or block key it does not know before sending the request
  (`clio/Command/ProcessModel/ProcessBlockKeyCheck.cs`). OA's clio PR adds a `fileProcessing` key set in the commit
  that carries the rebundle; without it clio refuses every create or `setElement` that carries the block.
- **C6 Delivery order.** One PR per Jira issue per repository; within an issue, package, then clio, then knowledge;
  a human merges every PR. This docs PR merges before OA's package PR opens (pr-split edge E7). OA's package PR merges
  only after ENG-95984's clio PR (it carries the `ManagerMap` arm for `fileprocessing` / `processfile`, E4/E6), and OA's
  clio PR only after ENG-95984's knowledge generation is PUBLISHED (the first `name=process-files`, E5). At most one of
  our package PRs is in human review at a time (E3).
- **C7 Stand discipline.** Every stand write (a saved process, a run, an upload, a fixture record, a no-op designer
  save) and every `install-process-builder` call needs the user's explicit go-ahead; an install restarts the site for
  every connected user. Schema writes and runs go one at a time, because a parallel burst downs the app pool. Cleanup
  goes through `execute-dataservice-batch`. Evidence comes from `SysProcessElementLog`.
- **C8 Guidance.** Shipped guidance names no Jira key, issue or PR; a version boundary is the clio version that ships the
  change, and a CrtProcessBuilder version appears only as inline evidence. Every read-back says
  `describe-business-process mode=full` (or `element=<name>`); every `install-process-builder` mention carries consent
  wording. The `process-files` guide targets about 21,000 characters; the report section splits into
  `process-files-report` past that.
- **C9 No prototype arguments.** Every option in the ADR is argued from clean-slate cost and constraints.

## Success signal

On the .NET Framework stand carrying each cut (analysis pr-split section 14):
- **OA**: an Account with 2 attachments copied to a Contact gives 2 `ContactFile` rows with `Type = File`, linked to
  the target, the sources unchanged and `CreatedObjectFileIds` with 2 rows (ST-01); an Object "use in process" element
  feeding a multi-instance sub-process per file iterates once per attachment, not once (ST-02); an empty-filter save is
  refused with `failedOperationIndex: null`; a designer-built SysFile element is described and its `setElement` refused;
  `ProcessFileElementToolE2ETests` passes with Ignored = 0.
- **RP**: a builder-made Object -> FileCollection -> Process-parameter chain makes 2 copies (ST-04); one output feeds two
  consumers (ST-05); a Word report on an Invoice is saved as `Invoice. <Number>.docx` and re-found by Modify data
  (ST-06); temporary report files exist while the process is parked and are gone at completion (ST-07); the four
  product processes describe as `buildType: fileprocessing` (read-only).
- **SF**: the builder-made versions of M23 (a)-(d) write rows with `RecordId`, `RecordSchemaName` and `Type = File`
  that the user sees in the Freedom UI attachment list (ST-08), equal to the M21 capture (DT-04).

## Open owner items

The six element-contract questions were agreed on 2026-10-14, each as recommended. The stories plan on these answers;
the acceptance rows marked with a question above and the test cases the test plan lists for it under "Cases that move
with an open decision" (analysis open-questions Part A) stand as written, and the Jira paste of those rows (FE.0b) no
longer waits.

| Q | Question | Agreed 2026-10-14 (as recommended) | ADR | Needed before |
|---|---|---|---|---|
| Q13 | Element token and variant discriminator | `fileProcessing` (alias `processFile`); the group present (`attachments`, `report`, `files`) is the variant; `source` is an optional identity check | D10 | OA package code |
| Q14 | Naming bundle of the block | as proposed, including `numberOfRecords` defaulting to 50 (not "read all" as in `readData`) | D11 | OA package code |
| Q15 | `ResultActionType` when `action` is omitted on create | inferred from `saveTo` (present: save; absent: use in process), always written | D14 | OA package code |
| Q16 | Record scope and empty-filter policy | `attachments.recordId` / `report.recordId`; refuse saving and Word elements with no selecting filter, notice for "use in process" | D16 | OA package code |
| Q17 | Storage policy details | the designer flag is not read for behaviour; no `linkColumn: "none"`; no SysFile override | D15 | OA package code |
| Q18 | FastReport printables | refused in this work | D17 | RP package code |

**Q16 note (2026-10-13).** For the analogous Delete data / Modify data empty-filter hazard, ENG-102537 Process
elements built without their configuration block save green and fail or hang at run time settled "a NOTICE, never a
refusal" by owner decision, because creating a bare element and configuring it in a later call is a supported flow.
The refusal is still recommended here, because the file element's empty filter is predicted to do something different
(source, not measured): saving copies up to 50 arbitrary files onto a record (a harmful action), and a Word element
generates one document per record of the whole entity (a mass side effect). The owner answered Q16 on 2026-10-14
knowing this precedent and kept the refusal (the reason is recorded in analysis open-questions Q16 and ADR D16); the
"notice everywhere" alternative was not taken.

**What FE-1 means if SF narrows** (open, added 2026-10-14). FE-1 promises all three variants with the save target, and
SF delivers objects without their own attachment object. If a SysFile measurement fails, SF keeps that case's refusal
(D15 fallback), and FE-1 would then hold for dedicated storage only. The owner decides whether FE-1 is amended to say
so, or whether the Story stays open until the failing case is solved.

Two smaller items:
- **O-TP1.** Where the e2e cases on the Process-parameter variant's `Files` run (TC-91, ENG-95984's E2E-14a-d).
  Recommended B: in RP's clio PR, with the element built by its own block.
- **Nested `BK15` rows.** Builder-made elements have none for nested items; ADR D20 tolerates this as N10. The
  alternative, writing the rows in the handler's create path, is the owner's call if strict parity is wanted.

Measurements that still gate code or wording: M1 and M2 (recorded before RP's package PR leaves draft; they decide the
single-file path), M7, M8, M21, M23 (a)-(d) and M25 (SF), M26 (OA's guide sentence and knowledge record about
`ObjectFiles` after a save; no code), M16 (the lifetime wording of temporary report files; ST-07 closes it), M18 (the
culture note on a constant file-name suffix, after RP's cut) and the M17 runs (the severity of the empty-record
notice). Every one except M7 is a stand write that needs the user's go-ahead.

## Dependencies

Jira links, agreed 2026-10-07 (D23; analysis pr-split section 12.2):

| # | Link | Why |
|---|---|---|
| L1 | ENG-95984 File process parameter type **relates to** ENG-96505 (replaces today's "blocks", link 560203) | the Object variant needs no File process parameter; the delivery order is enforced by the PR order |
| L2 | ENG-95984 **blocks** ENG-96506 | the Process-parameter variant consumes File and FileCollection, dotted process paths and the two-level binder |
| L3 | ENG-96505 **blocks** ENG-96506 | RP registers its variants in the handler and registry OA introduces |
| L4 | ENG-96506 **blocks** SF | slot order RP then SF (Q4) |
| L5 | ENG-95984 and ENG-96505 **block** ENG-95985 Send email attachments | that ticket reuses `BindCollection` and needs a file source |
| L6 | ENG-92719 is blocked by ENG-91843 Add and modify process parameters (Closed) | already satisfied |
| L7 | ENG-92725 Execute AI Intent element (BP generation) (Won't Do) | no link; the Creatio.ai pattern leaves the criteria |

As of 2026-10-14 the Jira side of day 0 is not done: the acceptance-criteria replacement, the re-links L1-L5 and the
creation of SF and of the two side Sub-tasks are still open (owner, plan FE.0b).

Merge edges (analysis pr-split section 7): E3 the next package PR leaves draft only after the previous one merged; E4
and E6 OA's package PR merges after ENG-95984's clio PR; E5 OA's clio PR merges after ENG-95984's knowledge generation
is published; E7 this docs PR merges before any FE code PR opens. E10 (this PR after ENG-95984's docs PR when both
carry the analysis folders) does not bind as written, because both analysis folders ride on this branch. This PR still
merges after the ENG-95984 BMAD PR https://github.com/Advance-Technologies-Foundation/clio/pull/1811: both append their
rows at the same place, the end of `spec/sprint-status.yaml`, and this set's rows name
`story-eng-95984-file-parameter-type-1`, which lands with that PR. The base of this PR is synced while it is still a
draft.

## Companion notes

- **Visible behaviour changes per cut.** OA: Object elements of existing processes describe as `buildType:
  fileprocessing` (was `usertask`); the generic route for ObjectFileProcessingUserTask is refused; `setFilter` on an
  Object element checks its root. RP: Report and Process-parameter elements, including the shipped PrintInvoiceReport,
  PrintQuotationReport, PrintContractsReport and GenerateDNSRecordsSpecification, describe as `fileprocessing`;
  `setFilter` on a Report element moves to the new filter target. OA also lists, as a refactor with no visible change,
  the readData sort codec moved out of `ReadDataConfigBinder` with byte-identical Read data messages.
- **Day-0 measurements, done on 2026-10-02 (CrtProcessBuilder 1.6.6.54):** M10 (the designer's object list matches the
  resolver's prediction), M11 (a)(b) (H-G3-1 not reproduced in memory), M13 (PrintContractsReport describes
  `ConnectedObjectId` as `Guid` with `referenceSchema: Contract`), M14 (the server-built element shape: template nested
  UIds, no `SerializeToDB`, `ConsiderTimeInFilter` "false"), M15 (no FastReport package or printable), M17-Q7 (foreign
  keys on `ContactFile` / `AccountFile`, none on `SysFile.RecordId`), M19 (a collection parameter as a filter value is
  accepted at build and fails at run), M20 (an unknown block is dropped silently), M24 (a wrong JSON shape is nulled by
  the binder and refused with a misleading text).
- **Estimate per Sub-task** (AI agent writes the code; analysis README): OA about 15.5-27.5 h agent and 5.5-13 h owner
  (4-6 working days); RP 7-15 h and 3.5-9 h (3-4 days); SF 3-8 h and 4-8 h (2-4 days).
