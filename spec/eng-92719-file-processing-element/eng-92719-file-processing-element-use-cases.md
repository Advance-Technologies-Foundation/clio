# ENG-92719 File processing element: use cases

[ENG-92719 File processing element](https://creatio.atlassian.net/browse/ENG-92719) (Story), with its sub-tasks
[ENG-96505 Element readiness and object attachments mode](https://creatio.atlassian.net/browse/ENG-96505) and
[ENG-96506 Generated report + process parameter modes](https://creatio.atlassian.net/browse/ENG-96506); related
[ENG-95984 File process parameter type](https://creatio.atlassian.net/browse/ENG-95984); epic
[ENG-92704 Create BP via AI Toolkit](https://creatio.atlassian.net/browse/ENG-92704).

This document answers *why* people use the Process file element and *for what*, and maps every use to what the
builder can do before and after each ticket. Start at the [README](README.md). The mechanics are in
[platform-reference](eng-92719-file-processing-element-platform-reference.md), the silent failures in
[traps](eng-92719-file-processing-element-traps.md), the design choices (D-numbers below) in
[decisions](eng-92719-file-processing-element-decisions.md), and the work in
[plan](eng-92719-file-processing-element-plan.md) and
[../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md).

Written 2026-10-01. Read-only: nothing was built, saved, run or written on the stand or in any repository.

**Basis labels.** *doc* = an Academy page or a Community thread (intended behaviour, not proof). *corpus* = counted
in shipped metadata. *source* = read in code (for runtime behaviour this is a hypothesis). *measured* = observed on
the stand (core 10.1.37) on the date given. *backlog* = Jira state, used for scope only, never for a platform fact.

| Alias | Path |
|---|---|
| PS | `C:/Projects/PackageStore` (the shipped corpus) |
| PD | `PS/CrtProcessDesigner/branches/7.8.0` (byte-identical to what the stand serves) |
| CORE | `C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib` (10.1.37, the stand's core) |
| PB | crt-process-builder `main` `d9571626` (package 1.6.6.85, re-pinned 2026-10-08; the stand measurements of 2026-10-02 ran on 1.6.6.54, `3f4cce50`), `packages/CrtProcessBuilder/Files/src/cs` |
| `<P>:<n>` in the corpus tables | `PS/<Package>/branches/7.8.0/Schemas/<Process>/metadata.json:<n>` |

---

## Summary

Academy sells Process file as plumbing: it reads the attachments of a record, takes files from a process
parameter, or generates an MS Word report, and then either hands the files to a following element (the
documented pairing is Send email attachments; since 8.3.1, also Creatio.ai workflow Sub-agents) or saves them to
a record's attachments. The product itself uses much less of it. Across the 1,090 package directories of the corpus exactly 16 processes
contain the element (30 elements). Only 4 are product processes, with 5 elements, and all 5 follow one recipe with small variations: a
Freedom UI form button runs a process that generates an MS Word printable for the open record, saves it to a
dedicated `<Entity>File` attachment object, re-finds the new file with a Modify data filter to tag or link it, and
shows the file list on a pre-configured page. No product process maps an element output, and the Object attachments and Process
parameter variants, Send email attachments and the Creatio.ai pairing occur only in test packages. Of the five
pairings ENG-92719 File processing element names, three are buildable once both sub-tasks land: Read data as the
upstream source of the record, Modify data re-finding the saved file, and Add data creating a report's data row.
Send email needs ENG-95985 Send email attachments. The Creatio.ai pairing cannot be built while
ENG-92725 Execute AI Intent element (BP generation) stays Won't Do. Three corpus-only patterns (Object to Process
parameter chain, a file collection process parameter, a multi-instance sub-process per file) are covered by
ENG-95984 File process parameter type together with the two sub-tasks. The issue refers to "the attached use-case
inventory", but it has no attachment (read 2026-10-01). This document is proposed as that inventory. The
recommended guidance is short: choose the variant by where the file is now, and choose the action by whether the
file must outlive the process (section 6).

---

## 1. Why customers use the element: what Academy documents

Sources (basis=doc, every page read on 2026-10-01):

- Process file element: https://academy.creatio.com/guides/no-code-customization/bpm-tools/process-elements-reference/system-actions/process-file-element. The text is identical in 8.0 to 10, apart from one product name. The 7.17 copy is at https://academy.creatio.com/docs/7-17/user/bpm_tools/process_elements_reference/system_actions/process_file.
- Send email element: https://academy.creatio.com/guides/no-code-customization/bpm-tools/process-elements-reference/user-actions/send-email-process-element.
- Use text files in Creatio.ai: https://academy.creatio.com/guides/no-code-customization/ai-tools/creatio-ai/use-text-files.
- Release notes: https://academy.creatio.com/guides/resources/category/release-notes (all 43 pages read).

### 1.1 Documented purpose

The article says the element automates file operations in a process. It has three sources: (1) read and copy
files from the attachments of records; (2) take files from process parameters; (3) generate Word reports. The
result is used in one of two ways: (a) at run time, "for example send them as email attachments", or passed to
another business process; (b) saved to the attachments of other records. The article rates the element LEVEL:
INTERMEDIATE. Every figure in it shows the classic designer.

### 1.2 Documented use cases and pairings

| # | Use case | Variant / action | Documented where (doc) | What source and the corpus add |
|---|---|---|---|---|
| U1 | Copy the N most recent attachments of one record to another record | Object attachments, save | Article, example 1 (10 newest files, contact to contact) | Corpus: FileCopyProcessPP (test) copies a contact's files to Contact, Account and Activity. The product never does it |
| U2 | Store a file that arrived in a process parameter on a record | Process parameter (save only) | Article, example 2 | Corpus: FileParameterProcess (test) only. The page has no "What to do with file?" control and forces save (`PD/Schemas/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:174-177, 333-334`). The runtime throws `NotSupportedException` for "use in process" (`PD/Schemas/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:89-95`). Both basis=source |
| U3 | Generate a Word report for a filtered set of records and file it on another record | Generated report, save | Article, example 3 (Orders to a Partnership) | Product: 4 processes, each filing the report on the record it was generated for, never on another record (section 2.3). The stand has no Order or Partnership printable: it has 5 MS Word printables, none for Account or Contact (measured 2026-10-01) |
| U4 | Send files as email attachments | any variant, then Send email "Add attachments" | Send email article ("Set up the email attachments": configure Process file first, then map its file collection); release notes 7.17.1 and 7.17.2 | The runtime reads every `Attachments<N>` row by the key `<name>FileLocator` (`PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-119, 280-287`; source). It drops attachments silently when feature `UseProcessEmailAttachments` is off (`:207-209`; source). The feature is ON on the stand (measured 2026-10-01). Corpus: 4 bindings, all in test packages. Builder: ENG-95985 Send email attachments |
| U5 | Pass the files to another business process | Use in process, then a parameter or sub-process | Article; release note 7.17.1 (the "File" parameter passes file information "between process elements and between business processes") | Corpus: a multi-instance sub-process per file (CRM60006PP) and a process-level file collection (FileParameterProcess), both test packages |
| U6 | Pass existing files to a Creatio.ai workflow Sub-agent | any variant, then Call Creatio.ai (Sub-agent) | Use text files in Creatio.ai ("Pass files through business process logic to workflow Sub-agents"); release note 8.3.1 | The runtime reads `Files<N>` rows by `<name>FileLocator` (`PS/CrtCopilot/branches/7.8.0/Schemas/ExecuteIntentUserTask/ExecuteIntentUserTask.cs:93, 112`; source). The Call Creatio.ai article itself documents no file input. Only small text files are supported; files live for the AI session. Corpus: 2 bindings, test only. Builder: none (section 3, P5) |
| U7 | Connect other elements to the Ids of the copied files | Save, then `CreatedObjectFileIds` | Article NOTE ("Creatio will save the Ids of the copied file records") | It is a collection of Ids. A filter's right-hand side takes one value (source; D29 M19), so the working route is a multi-instance iteration (FileCopyProcessPP; pattern P8) |
| U8 | Email the attachments of a custom Freedom UI record | Object on "Uploaded file" (SysFile), then Send email | Community, not product docs: https://community.creatio.com/questions/process-file-business-process-element-not-working-sysfile (Apr 2025 to Aug 2026) and https://community.creatio.com/questions/how-use-file-attached-custom-record-sending-email-using-bussiness-process | Users report an empty collection, and the last comment (Aug 2026) asks whether it was ever fixed. The designer has a SysFile path behind `ProcessFeatures.UseSysFileInObjectFileProcessing`, which defaults to on and is on on the stand (measured). **0 of 30** shipped elements use SysFile, and the read path is unmeasured (D29 M8). Builder: refused in ENG-96505 Element readiness and object attachments mode, and moved to the new Sub-task "SysFile attachment storage in the Process file element" ([pr-split](eng-92719-file-processing-element-pr-split.md)) |

Other customer signals (doc, Community and Marketplace):

- A 2024 thread asks how to attach a generated file to a record from a script that expects an `IFileLocator`
  (https://community.creatio.com/questions/how-attach-generated-file-record-business-process).
- Marketplace add-ons convert printables to PDF and send them as attachments
  (https://marketplace.creatio.com/app/printable-attachment-email-creatio). Customers want PDF, and the element
  does not document it. It writes `.docx` unless the printable has `ConvertInPDF` and a converter package is
  installed (source; D17). All 5 printables on the stand have `ConvertInPDF = false` (measured).

Academy documents **no** pairing with Read data, Add data or Modify data, and no Script task that reads the outputs
(only a 2021 Community script does). ENG-92719 File processing element nevertheless names those three among its
five use cases. Section 3 tests each against the corpus.

### 1.3 Version history (doc)

| Version | Change for this element | Source |
|---|---|---|
| 7.16.4 | "Collection of records" process parameter type | https://academy.creatio.com/guides/resources/release-notes/7164-release-notes |
| **7.17.1** | Element introduced (object attachments, database-stored files only). New "File" process parameter. Send email can add attachments, with Process file as the source | https://academy.creatio.com/guides/resources/release-notes/7171-release-notes |
| **7.17.2** | Generated report (MS Word or FastReport). File API for external file storages | https://academy.creatio.com/guides/resources/release-notes/7172-release-notes; article note "7.17.2 and up" |
| **7.17.3** | Process parameter source; "File name" for generated reports | https://academy.creatio.com/guides/resources/release-notes/7173-release-notes |
| 8.0 (docs) | FastReport sentences removed; "Word always generates individual reports for each record". The figures were not updated | the article, 8.0 vs 7.17 |
| 8.1.1 | Attachments of deleted records are removed from the "Uploaded file" (SysFile) table | release note 8.1.1 |
| 8.2.1 / 8.2.3 | Call Creatio AI element; "Collection of records" input | release notes 8.2.1, 8.2.3 |
| **8.3.1** | "pass files to API Skills directly within business process elements" | https://academy.creatio.com/guides/resources/release-notes/8-3-1-twin-release-notes |
| 8.3.2 | OData file API; Entity-class File API (`UseEntityFileApi`, documented on the Entity file API page only) | release note 8.3.2 |
| 8.3.4 | Call Creatio.ai renamed to the Sub-agent element | release note 8.3.4 |
| 10 | Nothing for this element | release notes 10X |

### 1.4 Terminology: the shipped captions win

Where Academy and the product disagree, guidance and tool texts use the **shipped** caption. The resource lines are
basis=source. The builder vocabulary is the D10/D11 proposal and still awaits the owner's decision on naming.

| Concept | Academy wording | Shipped caption | Evidence | Builder term (proposed) |
|---|---|---|---|---|
| Element | Process file | Process file (all three user-task schemas); the `SysProcessUserTask` row is "File Processing" | `PD/Resources/{Object,Report,Process}FileProcessingUserTask.ProcessUserTask/resource.en-US.xml:5`; `PD/Data/SysProcessUserTask_ObjectFileProcessing/data.json` | `type: fileProcessing` (alias `processFile`) |
| Source field | What is the source of the file? | same | `PD/Resources/BaseFileProcessingUserTaskPropertiesPage.ClientUnit/resource.en-US.xml:18` | the group present (D10) |
| Source 0 | "Attachments and notes of the object" | **Object attachments** | same file `:15` | `attachments` |
| Source 1 | Generated report | Generated report | `:16` | `report` |
| Source 2 | Process parameter | Process parameter | `:17` | `files` |
| Action field | What to do with file? | same | `:14` | `action` |
| Action 0 | Save to "Attachments and notes" of the object | **Save to object attachments** | `:12` | `saveToAttachments` |
| Action 1 | Use in process | Use in process | `:13` | `useInProcess` |
| Target | What object to save file to? (+ a record field named after the object) | same | `:19` | `saveTo.object`, `saveTo.recordId` |
| Object list entries | "Contact attachment" (the file object) | a legacy object shows as `<Entity> (<file object caption>)`, e.g. "Contact (Contact attachment)"; any other object shows its plain caption and is stored in SysFile | measured UO-2, 2026-10-01 | `attachments.object` takes the record object (or its file object) |
| Read limit, sort | How to filter records? + Read first N records; How to sort records? | same; the template default is 50 | `PD/Resources/ObjectFileProcessingUserTaskPropertiesPage.ClientUnit/resource.en-US.xml:7, 9-11` | `filter`, `attachments.numberOfRecords`, `attachments.sort` |
| Report fields | What report to generate?; Section; Generate separate report for each record; File name | same; the File name info text says the name is the report name **plus** this value | `PD/Resources/ReportFileProcessingUserTaskPropertiesPage.ClientUnit/resource.en-US.xml:7, 9-12` | `report.printable`, `report.separateReports`, `report.fileNameSuffix` / `fileNameSuffixColumn` |
| Files input | Files ("a single file or a collection") | Files | `PD/Resources/ProcessFileProcessingUserTaskPropertiesPage.ClientUnit/resource.en-US.xml:7` | `files` |
| One file (type) | "File" | caption "File" = FileLocator `A33C9252`. The platform NAME "File" is a different BLOB type, caption "File (BLOB)", `BA40CFC5` | `CORE/Terrasoft.Core/Resources/Terrasoft.Core.resx:7562-7563, 9415-9416`; `CORE/Terrasoft.Core/DataValueType.cs:297, 2168` | `File` |
| Several files (type) | "Collection of records" with a nested File | Collection of records = CompositeObjectList `651EC16F` with one FileLocator item | `DataValueType.cs:292` | `FileCollection` (D2) |
| Binary | not a no-code term | `B7342B7A` cannot hold a process value; the corpus has 0 Binary process parameters | `DataValueType.cs:255` | refused |
| AI consumer | Call Creatio.ai (article); Sub-agent element (release note 8.3.4) | user task "Sub-agent"; palette row "Run Creatio.ai Skill" | `PS/CrtCopilot/branches/7.8.0/Resources/ExecuteIntentUserTask.ProcessUserTask/resource.en-US.xml:5`; `.../Data/SysProcessUserTask_ExecuteIntentUserTask/data.json:19` | none (not buildable) |

The source order in code (0 Object, 1 Report, 2 Process parameter) differs from the order Academy uses, and the
option is not a parameter at all: each source is a separate user-task schema, and changing the source replaces the
element ([platform-reference](eng-92719-file-processing-element-platform-reference.md)). The Academy statements that
the stand contradicts, or never covers, are listed in [traps](eng-92719-file-processing-element-traps.md). The
three that matter for use cases:

- The Process parameters article has no "File" type in any version.
- "File name" is described twice, and both descriptions conflict. Source says the value is a suffix.
- The SysFile behaviour is undocumented.

---

## 2. The corpus: every shipped process that uses the element

### 2.1 How it was counted (corpus, 2026-10-01; reproduced by an independent refute-first recount)

- Scope: PS has 1,090 package directories (1,085 with a `branches` folder) and about 19,700 `metadata.json` files, of
  which 1,665 are process schemas in JSON.
- Method: a raw grep for the three user-task UIds (hyphenated, dash-less and case-insensitive spellings), run
  over the non-JSON files and the `.svn` pristine store as well, finds exactly **16 process schemas**. All 16 are
  on `branches/7.8.0`. The parse failures hide none.
- Product vs test, classified by package name:
  - 4 product processes, with 5 elements;
  - 12 test processes (ProcessTests ×8, CopilotAutoTest ×2, AutoTestUC ×1, DepTest_Level3 ×1), with 25 elements.
- Per variant and action (30 elements):

  | Variant | Total | Save | Use in process |
  |---|---|---|---|
  | Object attachments | 14 | 7 | 7 |
  | Process parameter | 2 | 2 | 0 |
  | Generated report | 14 | 12 | 2 |

- Report types: 12 elements use MS Word printables, 2 use FastReport.
- On the stand only the 4 product processes are installed. None of the test packages are (measured 2026-10-01).

### 2.2 Inventory

| # | Package | Process (caption) | Kind | Elements | Configuration | Pairing (consumer or surrounding elements) | Created in (`B8`: the creation version, not the last save; serialization-capture S1) | Evidence |
|---|---|---|---|---|---|---|---|---|
| 1 | AutoTestUC | UsrGeneratePdfFileForBasicFormPdf | test | Report ×1, save | Word printable `17ba5a7d`; filter Contact.Name = "Supervisor"; saved to ContactFile of the current user's contact | none (Start to End) | 8.2.0.3934 | `:306` |
| 2 | CopilotAutoTest | CallCreatioAIProcessElementFiles | test | Object ×1, use | ContactFile, **empty filter**, 50 rows | flows into a Creatio.ai element but binds nothing (dead) | 8.3.1.468 | `:376` |
| 3 | CopilotAutoTest | SkillFilesValidationProcess | test | Object ×2, use | CreatioAIIntentFile, Name starts with "heavy" / "more-60000-chars" | Creatio.ai `Files1` + `Files1FileLocator`, ×2 | 8.3.1.3829 | `:1426, 2282`; bindings `:1391, 2598` |
| 4 | CrtEmailMarketingApp | GenerateDNSRecordsSpecification ("Generate DNS Records Specification") | **product** | Report ×2, save | Word "DNS Requirements" / "DNS Specifications" on DNSGuideReport; SenderDomain = parameter; saved to DNSGuideFile with an **empty link column**; suffix formula | recipe B (2.3) | 8.3.2.217 | `:3711, 3946` |
| 5 | CrtInvoice | PrintInvoiceReport ("Print Invoice report") | **product** | Report ×1, save | Word "Invoice" `e1f1a474`; Invoice.Id = parameter; InvoiceFile linked by Invoice; suffix column Number | recipe A | 8.1.0.6428 | `:884`, action `:951` |
| 6 | CrtLeadOppMgmtApp | PrintQuotationReport ("Print quotation report") | **product** | Report ×1, save | Word "Quotation" `8d56963f`; Opportunity.Id = parameter; OpportunityFile; suffix formula Title + CurrentDateTime | recipe A, with Read data upstream | 8.0.10.3825 | `:1396` |
| 7 | CrtOrderContractMgmtApp | PrintContractsReport ("Print contract report") | **product** | Report ×1, save | Word "Contract" `ac39a15e`; Contract.Id = parameter; ContractFile; suffix column Number | recipe A | 8.1.0.6428 | `:861` |
| 8 | DepTest_Level3 | DepTestProcess | test (dependency fixture) | Object ×1 save; Report ×1 save | ContactFile to AccountFile; FastReport `1695c14a`, separate reports off | none | 0.0.0.0 | `:8073, 8273` |
| 9 | ProcessTests | CRM60006PP | test | Object ×3 (1 use, 2 save) | ContactFile of the contacts Read data found; one element has an empty filter | multi-instance sub-process **per file** (`InputRecordCollection` + item `SPFiles`), ×2 | 7.17.1.1179 | `:2129, 2297, 3126`; `:2502, 2619` |
| 10 | ProcessTests | FileCopyProcessPP | test | Object ×4, save | ContactFile to Contact / Account / Activity / Contact | multi-instance sub-process over **`CreatedObjectFileIds`** | 0.0.0.0 | `:2656-3218`; `:4290` |
| 11 | ProcessTests | FileParameterProcess | test | Process parameter ×2, save; Object ×1, use | Process2.Files from Object5.ObjectFiles; Process3.Files from the process parameter FileCollection | Object to Process chain; output to a process collection parameter to Process variant; **one output, two consumers** | 7.17.3.396 | `:1133, 1293, 1453`; `:25-52` |
| 12 | ProcessTests | FileReportNamesProcess | test | Report ×2, save | Word; constant suffix "Report"; suffix column JobTitle | file naming only | 7.17.3.384 | `:1605, 1813` |
| 13 | ProcessTests | MailConnectionsProcess | test | Object ×1, use | ContactFile, 1 record | Send email `Attachments1` | 7.18.2.172 | `:2535`; `:2489` |
| 14 | ProcessTests | PortalReportbySignal | test | Report ×1, save | Word; signal-started | none | 7.18.3.921 | `:315` |
| 15 | ProcessTests | ProcessFileReport | test | Report ×3 (2 save, 1 use) | Word | the "use" report feeds Send email `Attachments1` | 8.0.4.826 | `:3007, 3212, 3680`; `:2655` |
| 16 | ProcessTests | ProcessFileSendMail | test | Object ×1, use; Report ×1, use | ContactFile; FastReport with an empty filter | a gateway picks one of two Send email elements | 0.0.0.0 | `:2099, 3974`; `:2933, 3939` |

The full per-element table, with every parameter value and the designer captures, is in
[serialization-capture](eng-92719-file-processing-element-serialization-capture.md).

### 2.3 The product recipes (corpus; the stand holds the same four processes, measured 2026-10-01)

**Recipe A: print, attach, tag, show.** Used by PrintInvoiceReport, PrintQuotationReport and PrintContractsReport.

| Step | Element | What it does | Evidence |
|---|---|---|---|
| 1 | (Freedom UI page) | A print button on the form page sends `crt.RunBusinessProcessRequest` with `processRunType: ForTheSelectedPage` and `recordIdProcessParameterName`, so the open record's Id lands in an In parameter | `PS/CrtInvoice/branches/7.8.0/Schemas/Invoices_FormPage/Invoices_FormPage.js:131-147`; Opportunities_FormPage.js:123; Contracts_FormPage.js:167 |
| 2 | (Quotation only) Read data | Reads the opportunity, to build the file name | PrintQuotationReport `:1142` |
| 3 | Process file, Generated report | MS Word printable, filter `<Entity>.Id = parameter`, save to `<Entity>File` linked by the record column, separate reports on, file-name suffix from Number or from a formula | rows 5-7 above |
| 4 | Modify data "Set tag to file" | Re-finds the new file: `<Entity>File` where record = parameter AND Name contains "Invoice" / "Quotation" / "Contract" AND CreatedOn = macro CurrentHour; sets Tag | PrintInvoiceReport `:1612` (filter `:1666`), PrintQuotationReport `:1616`, PrintContractsReport `:1085` |
| 5 | Pre-configured page | Shows the record's file list (`InvoiceReportResult_MiniPage`, a `crt.FileList` on InvoiceFile) | PrintInvoiceReport `:1108`; `PS/CrtInvoice/branches/7.8.0/Schemas/InvoiceReportResult_MiniPage/InvoiceReportResult_MiniPage.js:46-50` |

**Recipe B: regenerate a guide document.** Used by GenerateDNSRecordsSpecification.

1. Delete data removes the domain's old DNSGuideFile rows.
2. Read data reads the sender domain.
3. Add data creates a DNSGuideReport row, which is the printable's data source.
4. An exclusive gateway picks one of two printables by the provider.
5. Process file (Generated report) saves the document to DNSGuideFile, with an empty link column.
6. Delete data removes the data row.
7. Modify data links the file to the domain: it filters by Name contains the domain and sets SenderDomain.
8. A pre-configured page shows the result.

Evidence: GenerateDNSRecordsSpecification `:5324, 4236, 3711/3946, 4164, 3602, 2990`.

What the product never does: map `ReportFiles` or `CreatedObjectFileIds`. Each output UId occurs only at its
definition and in its own element's mapping rows (corpus; the stand's describe shows the same, measured). The
re-find in step 4 has three hazards (inference from the filter):

- it also tags older files created in the same hour;
- it misses a file across an hour boundary;
- it depends on the printable's caption containing the word.

Recipe B works around the empty link column with a Modify data. Section 6.4 turns both points into guidance.

### 2.4 What the corpus never shows

| Absent shape | Count | Consequence for planning |
|---|---|---|
| SysFile storage (source or target) | 0 of 30 elements | No shipped capture exists. The SysFile path needs stand captures (D29 M7, M8, M21, M23) and moves to the SysFile Sub-task ([pr-split](eng-92719-file-processing-element-pr-split.md)) |
| Object or Process parameter variant in a product package | 0 | Their use cases come from Academy and the test packages only |
| A Process-variant output consumed | 0 | The designer never offers that variant's `ObjectFiles` as a mapping source (source) |
| Read data or Add data consuming a file output | 0 | Read data sits upstream in 9 of the 10 processes that have one. Add data appears only before a report (section 3) |
| A single File process parameter bound to the Process variant's `Files` | 0 | The single-file path is unproven and pending D29 M1 (D18) |
| An outer-only binding of a file collection | 0 of 11 file-collection bindings | All 11 bind both levels, and the builder must too (D5, D18) |
| PDF output | 0 (all 5 product printables have `ConvertInPDF = False`) | Guidance must not promise PDF |

---

## 3. The five patterns named in ENG-92719 File processing element

The issue says the element "appears in five recurring real-world use cases, all of which combine it with a second
element: Send email, Read data, Add data, Modify data, or a Creatio.ai call" (backlog). The measured evidence for each:

| Pattern | Academy (doc) | Product corpus | Test corpus |
|---|---|---|---|
| P1 Send email | **yes**: the primary documented pairing | 0 | 4 bindings (MailConnectionsProcess, ProcessFileReport, ProcessFileSendMail ×2) |
| P2 Read data | no | upstream in 2 of 4 (DNS: the sender domain; Quotation: the file name) | 8 test processes have one, 7 strictly upstream; no Read data anywhere consumes a file output |
| P3 Add data | no | 1: creates the report's data row before the report (DNS) | 0 |
| P4 Modify data | no | **4**: re-find the saved file by filter, set Tag or SenderDomain | 0 |
| P5 Creatio.ai | yes (8.3.1+, text files) | 0 | 2 bindings (SkillFilesValidationProcess); 1 dead element |

### 3.1 Capability per pattern

"Today" means CrtProcessBuilder 1.6.6.54 with clio `master` as read on 2026-10-01; the column still holds on
1.6.6.85, whose generic route is unchanged and which still accepts a collection as a filter value. Today the element exists only
through the unguarded generic `userTask` route, which writes no configuration (D13), so it is not counted as support.

| Pattern | Today | After ENG-95984 File process parameter type | After ENG-96505 Element readiness and object attachments mode | After ENG-96506 Generated report + process parameter modes | Still missing after all three | Home |
|---|---|---|---|---|---|---|
| **P1** Process file to Send email attachments | **No.** `sendEmail` has no attachments field. PB writes no `Attachments<N>` slots (grep of PB for `Attachments` / `FileLocator`: no hit, 2026-10-01) | File and FileCollection parameters exist, so single-file and collection sources become declarable | a source exists: `ObjectFiles` (item `File`) | more sources: `ReportFiles` (item `File`); the Process variant's `ObjectFiles` (item `ObjectFile`) | the `email.attachments` write and describe; the slot writer (`Attachments<N>` CompositeObjectList In + `Attachments<N>FileLocator` FileLocator In, created in the process); both-level binding | **ENG-95985 Send email attachments** (exists, To Do), after ENG-96505 Element readiness and object attachments mode and ENG-95984 File process parameter type |
| **P2a** Read data to Process file (which record; report filter; file name) | `readData` yes; the element cannot be configured | - | `attachments.recordId` and `saveTo.recordId` from `sourceElement` + `sourceColumn` (D11, D16) | `report.recordId`; a `fileNameSuffix` formula over a Read data column | nothing | ENG-96505 Element readiness and object attachments mode, ENG-96506 Generated report + process parameter modes |
| **P2b** Process file to Read data (re-read the files) | By a filter on the file object: yes. By `CreatedObjectFileIds`: **not a platform feature**. Each filter right-hand expression becomes one ESQ parameter (`CORE/Terrasoft.Nui.ServiceModel/Extensions/QueryExtension.cs:378-388`; source). PB accepts a collection as a filter value with no type check, so it saves green and is predicted to fail at run time (D29 M19) | - | - | - | a guard that refuses collection references (not file-specific) | guidance, plus the new Sub-task "Refuse collection parameters as filter values" (D22) |
| **P3** Add data with Process file | `addData` yes. A FileLocator into a column is **impossible**: 0 entity columns of that type exist (corpus), and PB refuses the mapping by exact type match | - | - | the "create the report's data row, generate, delete the row" recipe becomes buildable | nothing buildable is missing | guidance in ENG-96506 Generated report + process parameter modes |
| **P4** Process file to Modify data (re-find the saved file) | `changeData` + `contains` + macro `CurrentHour` exist (`PB/Filters/MacrosCatalog.cs:59`; `PB/Contracts/FilterContracts.cs:110-114`) | - | Object save produces files to re-find | Report save completes recipe A | nothing; the hazards go into guidance | guidance in ENG-96505 Element readiness and object attachments mode; per-Id route via P8 |
| **P5** Process file to Creatio.ai (Sub-agent) | **No.** PB has no ExecuteIntent support (grep, 2026-10-01). The generic route builds the element without the skill's parameters or `Files<N>` slots | - | a source exists | more sources | the element itself (skill selection and parameter sync; size L) and a `files` slot (S, reusing the ENG-95985 Send email attachments writer) | **outside ENG-92719 File processing element**. ENG-92725 Execute AI Intent element (BP generation) is Closed, Won't Do (backlog, 2026-10-01) |

The AC also says "an element's output collection can be mapped by more than one downstream element". The corpus has
it once: FileParameterProcess Object5.ObjectFiles feeds both ProcessFileProcessingUserTask2.Files (`:1264, 1280`) and
the process parameter FileCollection (`:25-52`). Builder mappings read an element output without consuming it, so
the case needs no extra code (inference). It is pinned by the ENG-96505 Element readiness and object attachments
mode e2e "two downstream consumers of `ObjectFiles`" (D28).

---

## 4. Patterns that exist only in the corpus

| # | Pattern | Corpus | Today | After ENG-95984 File process parameter type | After ENG-96505 Element readiness and object attachments mode | After ENG-96506 Generated report + process parameter modes | Home |
|---|---|---|---|---|---|---|---|
| P6 | Object to Process-parameter chain (read files, then save them elsewhere) | FileParameterProcess: `Files` from `ObjectFiles`, `Files.File` from `ObjectFiles.File` (`:1264, 1280`), test | dotted element-to-element mappings resolve, but neither element is configurable | the two-level binder (D5) | the Object source | the Process variant's `files` input binds both levels (D18) | e2e in ENG-96506 Generated report + process parameter modes |
| P7 | File output into a process-level file collection, then into a Process variant | FileParameterProcess.FileCollection (`:25-52`), test. Product: ProcessLibrary MarkProcessesToCancel `FilesCollection`, Out, filled by a script task | **No**: the FileLocator item type is refused; the mirror refuses items without a column Tag and binds the outer level only; process-parameter nested paths are flat-only | create, mirror and bind both levels; dotted process-parameter addressing (D2-D5) | the source | the consumer | ENG-95984 File process parameter type; consumed by ENG-96506 Generated report + process parameter modes |
| P8 | One multi-instance sub-process run per file, or per created file Id | CRM60006PP (per file; callee parameter `SPFiles`, FileLocator); FileCopyProcessPP (per Id, outer only), test | the machinery of ENG-99856 Sub-process element: support MULTI-INSTANCE (running the callee once per item of a collection) exists; a callee with a FileLocator parameter can be made only in the designer | the callee File parameter; an item mapping also binds its parent collection (D5 P1) | the source element | Report and Process sources too | e2e in ENG-96505 Element readiness and object attachments mode, run after ENG-95984 File process parameter type merges |
| P9 | Show the result on a pre-configured page with a file list | 4 product processes | the pre-configured page element is buildable (`PB/Elements/PreconfiguredPageElementHandler.cs`) if the page exists | - | - | recipe A complete | guidance only |
| P10 | Delete old files before regenerating | GenerateDNSRecordsSpecification `:5324`, product | `deleteData` on the file object, today | - | - | - | guidance (report section) |
| P11 | A gateway picks the printable | GenerateDNSRecordsSpecification, product | exclusive gateway, today | - | - | buildable | no work |
| P12 | Started from a record page button, with the record Id in an In parameter | 4 product processes (2.3, step 1) | an In parameter, today; the button is page configuration, outside the process | - | use the parameter as `attachments.recordId` / `saveTo.recordId` | as `report.recordId` / `saveTo.recordId` | guidance (the recipe) |

Traps that these patterns carry are in [traps](eng-92719-file-processing-element-traps.md). The one to state here,
because it changes which output an agent should map:

- After an Object element **saves**, its `ObjectFiles` holds the **source** files, not the copies
  (`PD/Schemas/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:77-86`: `SetReadObjectFiles(fileCopyResults.SourceFileLocators)`; basis=source; to be measured by M26, [traps T-19](eng-92719-file-processing-element-traps.md#t-19)).
- CRM60006PP SubProcess2 iterates exactly that collection.
- To iterate the copies, iterate `CreatedObjectFileIds`.

---

## 5. Recommended scope per pattern

From the downstream-consumer analysis and decisions D22 and D24. The owner still has to approve the moves marked "yes"; they are
collected with the other owner decisions in [open-questions](eng-92719-file-processing-element-open-questions.md).

| Pattern | Recommended scope | Ticket | Test that proves it | Owner decision |
|---|---|---|---|---|
| P1 Send email | Out of ENG-92719 File processing element. ENG-95985 Send email attachments owns it and reuses `BindCollection` (D5). Reword its AC "no upstream file source fails" to "a source that is neither a file collection nor a File parameter is refused", because the designer accepts a single File as an attachment | ENG-95985 Send email attachments | e2e there: all three variants as sources | **yes** (D22) |
| P2a Read data upstream | In scope | ENG-96505 Element readiness and object attachments mode; ENG-96506 Generated report + process parameter modes | e2e of ENG-96505 Element readiness and object attachments mode: `attachments.recordId` from Read data. e2e of ENG-96506 Generated report + process parameter modes: Word save with `report.recordId` | no |
| P2b Read data downstream | Guidance: filter the file object by record and name; never put `CreatedObjectFileIds` into a filter. Plus the new Sub-task "Refuse collection parameters as filter values" under ENG-92719 File processing element (size S, one package PR and one guidance line) | new Sub-task | package unit tests; M19 decides its urgency | **yes** (new Sub-task) |
| P3 Add data | Guidance in ENG-96506 Generated report + process parameter modes: the data-row recipe, and "a file cannot be written into a column; pass a created file's Id through P8" | ENG-96506 Generated report + process parameter modes | e2e TC-74 (CL-RP): `addData` row, then a report on it, then `deleteData`. The stand's "DNS Requirements" printable on DNSGuideReport fits (measured), with cleanup through `execute-dataservice-batch` | no |
| P4 Modify data | Guidance, with the hazards (2.3) and the per-Id alternative (P8), plus the build-and-describe e2e TC-71. In SysFile mode the file object is SysFile, linked by `RecordId` + `RecordSchemaName` | ENG-96505 Element readiness and object attachments mode (the consumers section of its knowledge PR) | e2e TC-71 (CL-OA): Object save, then `changeData` on the file object (record + Name contains + CreatedOn = CurrentHour), setting Tag | no |
| P5 Creatio.ai | Out of scope. Amend the AC. Guidance says "not through this tool yet" (never "Creatio cannot"). If ENG-92725 Execute AI Intent element (BP generation) is reopened, the `files` field becomes a Sub-task there and reuses the ENG-95985 Send email attachments slot writer | none | none | **yes** (drop, or reopen ENG-92725 Execute AI Intent element (BP generation)) |
| P6 Object to Process chain | In scope | ENG-96506 Generated report + process parameter modes | e2e: the Object-to-Process chain and Report "use in process" into the Process variant | no |
| P7 File collection parameter | In scope | ENG-95984 File process parameter type, consumed by ENG-96506 Generated report + process parameter modes | e2e of ENG-95984 File process parameter type: create with File and FileCollection, describe read-back. e2e of ENG-96506 Generated report + process parameter modes: Process variant from a FileCollection | no |
| P8 Per-file iteration | In scope, with no new production code beyond the two tickets | ENG-96505 Element readiness and object attachments mode, after ENG-95984 File process parameter type | e2e: multi-instance per file | no |
| P9-P12 | Guidance only (buildable with existing elements) | ENG-96505 Element readiness and object attachments mode / ENG-96506 Generated report + process parameter modes knowledge PRs | none beyond the above | no |
| U8 SysFile attachments of custom Freedom UI objects | Refused in ENG-96505 Element readiness and object attachments mode. Delivered by the new Sub-task "SysFile attachment storage in the Process file element", after measurements M7, M8, M21, M23 and M25 | new Sub-task (key on creation) | its own e2e fixture | **yes** (SysFile placement) |

Both patterns are tested, as [decisions D-2](eng-92719-file-processing-element-decisions.md#d-2-eng-92719-file-processing-element)
requires: TC-71 (Process file -> Modify data re-find, CL-OA) and TC-74 (Add data -> report, CL-RP) in the
[test-plan](eng-92719-file-processing-element-test-plan.md).

The replacement AC text is the last bullet of
[decisions D-2](eng-92719-file-processing-element-decisions.md#d-2-eng-92719-file-processing-element). It names the
five buildable patterns as tested, moves Process file -> Send email to ENG-95985 Send email attachments, and states
that Process file -> Creatio.ai is out of scope (ENG-92725 Execute AI Intent element (BP generation) is Won't Do).
This document is the use-case inventory it refers to.

---

## 6. Recommended guidance: which source variant for which intent

This is the content the ENG-92719 File processing element AC asks for ("Guidance covers which source variant to
pick for a given intent"). It goes into the new `process-files` guide (D25):

- ENG-96505 Element readiness and object attachments mode writes the Object rows and the decision questions;
- ENG-96506 Generated report + process parameter modes writes the report and process-parameter rows.

The key names are the D10/D11 proposal, which awaits the owner's naming decision. Each sentence ships only with the
CrtProcessBuilder cut that makes it true, behind the guide's version gate. Exact version numbers are chosen at cut
time.

### 6.1 Three questions, in order

1. **Where is the file now?**
   - On a record's attachments: Object attachments (`attachments`).
   - Not yet in existence, to be produced from an existing printable: Generated report (`report`).
   - In a process parameter or another element's output: Process parameter (`files`), but **only** to save it to a
     record. Otherwise map the parameter straight to the consumer; no Process file element is needed.
2. **Must the file outlive this process?**
   - Yes: `saveToAttachments` with `saveTo`.
   - No: `useInProcess` (Object and Report only).
   - On create, the action is inferred from whether `saveTo` is present (D14).
3. **Who consumes it?**
   - A sub-process, one run per file: P8.
   - A tag or link on the saved file: P4.
   - An email or a Creatio.ai skill: not through this tool yet (P1, P5).

### 6.2 Intent table

| # | The user wants to... | Variant (group) | Action | What to map next | Must state |
|---|---|---|---|---|---|
| I1 | email, forward or iterate files already attached to a record | Object attachments (`attachments`) | `useInProcess` | `ObjectFiles.File`: the existing attachment rows | scope by `attachments.recordId`; `numberOfRecords` defaults to 50 and does **not** read all; sort by a column of the file object |
| I2 | copy a record's attachments to another record | Object attachments | `saveToAttachments` + `saveTo` | `CreatedObjectFileIds.Id` for the copies; `ObjectFiles` still points at the **source** files (source; M26 measures it before the guide states it) | an empty filter is refused (D16); SysFile targets are refused until the SysFile Sub-task |
| I3 | produce a document from an existing printable and keep it on the record (recipe A) | Generated report (`report`) | `saveToAttachments` | `ReportFiles.File` = the saved rows; `CreatedObjectFileIds` the same Ids | MS Word only (FastReport refused, D17, owner decision); one file per record; every file goes to the one `saveTo.recordId`; `.docx` unless `ConvertInPDF` and a converter package |
| I4 | produce a document and pass it on without keeping it | Generated report | `useInProcess` | `ReportFiles.File` = temporary files | they are deleted when the process instance completes (pending D29 M16); consume them in the same process |
| I5 | store a file the process received (from a caller process, a page or a script) on a record | Process parameter (`files`) | save only (omit `action`) | `ObjectFiles.ObjectFile` and `CreatedObjectFileIds` = the copies | `useInProcess` is refused (the platform throws); `saveTo` is required; a single File source is pending D29 M1 |
| I6 | hand files that are already in a parameter to an email or a sub-process | **no Process file element** | - | map the parameter directly | Send email attachments: not through this tool yet (ENG-95985 Send email attachments) |
| I7 | run something once per file | any variant, then a multi-instance sub-process | - | `InputRecordCollection` from the file collection (`ObjectFiles` or `ReportFiles`), the callee's item from its `File` item | to iterate the copies after a save, iterate `CreatedObjectFileIds` |
| I8 | tag, relink or rename the file just saved | I2 or I3, then `changeData` on the file object | - | a filter: record + Name contains + CreatedOn = CurrentHour | the same-hour and hour-boundary hazards; or iterate `CreatedObjectFileIds` (I7) |
| I9 | send files to a Creatio.ai skill (Sub-agent) | not buildable | - | - | "not through this tool yet"; never "Creatio cannot" |
| I10 | put a file into a record field | impossible | - | - | attachments are rows of the file object; no column holds a file |
| I11 | create a new printable | out of scope | - | - | tell the user to create it in System Designer, Report setup, then retry |
| I12 | get a PDF | Generated report | - | - | only with `ConvertInPDF` and a converter package; otherwise `.docx` |

### 6.3 What each variant hands on (basis=source; D29 M4 and M16 verify the starred rows)

| Variant | Action | File output | `CreatedObjectFileIds` | Lifetime | Evidence |
|---|---|---|---|---|---|
| Object attachments | use in process | `ObjectFiles.File` = locators of the existing attachment rows | empty | the rows outlive the process | `PD/Schemas/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:71-75, 94-110` |
| Object attachments | save | `ObjectFiles.File` = the **source** rows | Ids of the copies | permanent | `:77-86` |
| Generated report | use in process (*) | `ReportFiles.File` = temporary process files | empty | until the process instance completes | `PD/Schemas/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:98-112, 140-153` |
| Generated report | save | `ReportFiles.File` = the saved attachment rows | the same Ids | permanent | `:77-96` |
| Process parameter | save (the only action) (*) | `ObjectFiles.ObjectFile` = the copies (item key `ObjectFile`, not `File`) | Ids of the copies | permanent | `PD/Schemas/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:36-45, 68-81` |
| Process parameter | use in process | throws `NotSupportedException` | - | - | `:89-95` |

### 6.4 Sentences the guide must carry

- Pick the variant by where the file is now (6.1). Use the shipped names: "Object attachments", "Generated report",
  "Process parameter".
- A source cannot be changed in place. Remove the element and add a new one (D12).
- Always scope Object attachments and Generated report by a record. With no filter, an Object element reads up to
  50 arbitrary files and a Word report produces one document for every record of the object. A saving Object
  element or any Word report with no record scope or filter is refused; an Object element used in process gets a
  notice (D16).
- (Ships only after M26 confirms it, as `measured <date>`; [traps T-19](eng-92719-file-processing-element-traps.md#t-19).)
  After saving attachments, the Object variant's `ObjectFiles` points at the original files. The copies' Ids are in
  `CreatedObjectFileIds`.
- Generated reports: pass the `templateId` from list-printables as `report.printable`. Never create a printable.
  The file name is the printable caption plus the suffix.
- Re-finding a saved file by name and `CurrentHour` can match older files from the same hour. Prefer iterating
  `CreatedObjectFileIds` when exactness matters.
- A save target needs its link column, otherwise the file is linked to no record (or, for a copy within one file
  object, duplicated on the source record). The link is derived from `saveTo.object`; name it in
  `saveTo.linkColumn` only when it differs, as GenerateDNSRecordsSpecification would (`DNSGuideFile` linked by
  `SenderDomain`), which makes its follow-up Modify data unnecessary (D15).
- Send email attachments and Creatio.ai file passing are "not through this tool yet" until ENG-95985 Send email
  attachments ships, or ENG-92725 Execute AI Intent element (BP generation) is reopened.
- A file never goes into a column. Pass its Id through a sub-process iteration.

Budget: the whole `process-files` guide targets at most 22,000 characters (D25). Sections 6.2 and 6.4 together are
the largest block it carries for this ticket, so they should be compressed to table rows, not prose.

---

## 7. Measurements that decide the use-case wording

Every runtime claim above that is only basis=source, and that a recommendation depends on. IDs are from D29 in
[decisions](eng-92719-file-processing-element-decisions.md). Each write on the stand needs the user's go-ahead.
Writes run one at a time, and cleanup goes through `execute-dataservice-batch`.

| ID | Claim | Decides |
|---|---|---|
| M1 | A single File bound to `Files.File` only (nested level) copies one file | I5 with a single File; whether the guide tells agents to wrap one file in a FileCollection |
| M4 | A FileCollection bound at both levels feeds the Process variant, and its `ObjectFiles` holds the copies | P6, P7, the starred Process-parameter row of 6.3 |
| M8 / M23 | SysFile read and write paths work | U8 and the SysFile Sub-task |
| M16 | Report "use in process" files live while the process is parked and are gone at completion | I4 wording; the starred Generated-report row of 6.3 |
| M19 | A collection used as a filter value fails at run time | the urgency of the "Refuse collection parameters as filter values" Sub-task |
| M26 | After an Object save, `ObjectFiles` holds the source locators ([traps T-19](eng-92719-file-processing-element-traps.md#t-19); D29) | I2, I7 and the 6.4 sentence. Probe: a designer-built Object save from a Contact to an Account, then a multi-instance callee that records each item's `EntitySchemaName` and `RecordId`, compared with the ContactFile and AccountFile Ids |

The Send email measurements (single-file attachment, outer-only binding, template and manual modes) belong to
ENG-95985 Send email attachments and are not repeated here.
