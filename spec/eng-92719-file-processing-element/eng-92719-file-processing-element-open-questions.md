# ENG-92719 File processing element and ENG-95984 File process parameter type: open questions and evidence status

This document lists what is still open before and during the implementation of ENG-95984 File process parameter
type (Task) and ENG-92719 File processing element (Story), with its sub-tasks ENG-96505 Element readiness and object
attachments mode and ENG-96506 Generated report + process parameter modes (epic ENG-92704 Create BP via AI Toolkit).
It has four parts. **Part A** holds 19 owner questions (Q1-Q19). Each has options, a recommendation the plan already
assumes, and what a different answer changes. Q1-Q9 are about Jira and delivery; **the owner agreed
Q1-Q9 as recommended on 2026-10-07** (Q7 had been answered by M3), Q10 (option A), Q11 (Variable) and Q12 (as recommended) the same day, and Q19 (`process-files`) on 2026-10-08. Q10-Q19 fix the contract. **Part B** holds 27 stand measurements with exact recipes and read-only evidence
queries. Seven are read-only. The rest need writes on disposable processes in the `Custom` package, and each needs
the user's go-ahead. Fifteen of them gate code; the rest verify or tune wording. **Part C** records what was already
measured on 2026-10-01: versions, feature states, the user's designer observations UO-1..UO-4, describe of the
shipped report processes, and corpus counts. **Part D** lists 37 contradictions found during the research and
while the sibling documents were cross-checked, and how each was resolved. Seven are still open, because their
resolution rests on a source trace or on documentation: five wait for a scheduled measurement (M1, M3b, M8, M13),
and two have none scheduled because a refusal covers the case. Nothing in this document was built, run or written to a repository or to the stand.

| | |
|---|---|
| Status | Open, 2026-10-01. Nothing decided yet. |
| Sibling documents | [README](README.md) · [platform-reference](eng-92719-file-processing-element-platform-reference.md) · [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) · [use-cases](eng-92719-file-processing-element-use-cases.md) · [traps](eng-92719-file-processing-element-traps.md) · [reuse](eng-92719-file-processing-element-reuse.md) · [decisions](eng-92719-file-processing-element-decisions.md) · [plan](eng-92719-file-processing-element-plan.md) · [test-plan](eng-92719-file-processing-element-test-plan.md) · [pr-split](eng-92719-file-processing-element-pr-split.md) · [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) · [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) |
| Numbering | `D1`-`D29` are the decisions in [decisions](eng-92719-file-processing-element-decisions.md). `M1`-`M26` are the measurements of its D29 (M26 is the T-19 probe of [traps](eng-92719-file-processing-element-traps.md)); M3b is added here and is the baseline the [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) calls MI-0. Refusal and notice ids (`F-T*`, `F-M*`, `F-E*`, `F-F*`, `F-R*`, `F-P*`) are those of the decisions' Appendix A. `O1`-`O8`, `L1`-`L7` and the contingencies `X1`-`X8` are in [pr-split](eng-92719-file-processing-element-pr-split.md) sections 11-13; "decisions X1-X7" are the corrections in section 0 of the decisions. |
| Methods | [test-plan](eng-92719-file-processing-element-test-plan.md) section 8 gives each measurement's method in one row, and its section 9 the evidence queries. Part B here is the full recipe and follows both; Part B and test-plan section 8 now give one recipe for every measurement. Rows 35-37 of Part D record the places where they differed before the reconciliation of 2026-10-01. |
| Basis labels | **measured** = observed on the stand, or counted over the corpus or a repository, with the date. **source** = read in code or metadata; for runtime behaviour this is a hypothesis. **inference** = derived from several source facts. **documentation** = Academy or Community text. The research passes of 2026-10-01 are cited by what they read (a query, a describe call, a designer read) and when; their raw outputs are not attached. |

Aliases, as in [pr-split](eng-92719-file-processing-element-pr-split.md):

| Alias | Meaning |
|---|---|
| **PT** | ENG-95984 File process parameter type |
| **FE** | ENG-92719 File processing element |
| **OA** | ENG-96505 Element readiness and object attachments mode |
| **RP** | ENG-96506 Generated report + process parameter modes |
| **SF** | NEW Sub-task of FE: "SysFile attachment storage in the Process file element" |
| **MH** | not created: the conditional Sub-task of PT "typeFromElement collection mirror leaves its items unbound" waited on M3, and M3 refuted H-1 on 2026-10-02 |
| `PK-` / `CL-` / `KB-` | the crt-process-builder / clio / clio-knowledge PR of a ticket, for example PK-OA |
| PB, PBT | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/` (main `3f4cce50`, 1.6.6.54); its tests `tests/UnitTests/CrtProcessBuilder.Tests/` |
| PD, PS | `PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas` (byte-identical to what the stand serves, measured 2026-10-01); `PackageStore` (the shipped corpus) |
| CORE, CFG | Creatio core `TSBpm/Src/Lib` at 10.1.37 (the stand's core); its compiled configuration `Terrasoft.WebApp.Loader/Terrasoft.WebApp/Terrasoft.Configuration/Autogenerated/Src` under the same root |
| CLIO, KB | clio repository root (master `03ef3944f`); clio-knowledge repository root (master `d0b5a2b`, libraryVersion 1.15.90) |

---

## A. Owner decisions

### A.0 At a glance

"Before" names the first PR whose code depends on the answer. "Ref" points at the decision and, where it exists, at
the owner row of the sibling documents.

| Q | Question | Recommended answer | Before | Ref |
|---|---|---|---|---|
| Q1 | Replace the acceptance criteria of PT, FE, OA and RP | yes: PT as AC-1..AC-10 of the ENG-95984 File process parameter type plan section 1.4; FE, OA and RP as decisions Part D (D-2..D-4) **Agreed 2026-10-07.** | any PR | D24; decisions row 14 |
| Q2 | Jira links | L1-L5: PT relates to OA, PT blocks RP, OA blocks RP, RP blocks SF, PT and OA block ENG-95985 Send email attachments **Agreed 2026-10-07.** | any PR | D23; L1-L7 |
| Q3 | Where SysFile attachment storage ships | in the NEW Sub-task SF; OA refuses SysFile sources and targets with a message **Agreed 2026-10-07.** | PK-OA | D15; O1 |
| Q4 | Slot order of RP and SF | RP first, SF last **Agreed 2026-10-07.** | PK-RP | O3; X3 |
| Q5 | Downstream consumer patterns | Send email to ENG-95985 Send email attachments; Creatio.ai call out of scope **Agreed 2026-10-07.** | FE AC | D22; decisions row 12 |
| Q6 | PR split | one PR per Jira issue per repository, plus SF and the docs-only CL-PT-DOC and CL-DOC (14 PRs) **Agreed 2026-10-07.** | any PR | D27; decisions row 15 |
| Q7 | Where the mirror defect H-1 is fixed | answered by M3 (2026-10-02): H-1 refuted, so no MH; binding the items is one parity commit in PK-PT (X4) | - | D9; O2; X4 |
| Q8 | Delivery protocol bundle | yes to CL-PT-DOC and CL-DOC first, the variant registry in the ADR, merge commits for stacked PRs, merge windows, a second stand **Agreed 2026-10-07.** | CL-PT-DOC and CL-DOC | O4-O8; ENG-95984 File process parameter type plan O-10 |
| Q9 | Which follow-up Sub-tasks to create, and when | the unconditional ones now (day 0); MH not created (M3 refuted H-1); the designer bug report only after M11; the X2 one only on that contingency **Agreed 2026-10-07.** | any PR | decisions D-5; pr-split 12.3 |
| Q10 | How a caller declares a file collection | `type: FileCollection`, read back as `FileCollection` **Agreed 2026-10-07.** | PK-PT | D2; decisions row 1 |
| Q11 | Default direction of a FileCollection | Variable (revised from Out after the 2026-10-07 recount) **Agreed 2026-10-07.** | PK-PT | D3; decisions row 2 |
| Q12 | Two-level binder policy (P3, P2 scope) | P3 resets a stale parent with a notice; P2 and R-M2 only on file-consuming targets **Agreed 2026-10-07.** | PK-PT | D5; decisions rows 3, 4 |
| Q13 | Element token and variant discriminator | `fileProcessing` (alias `processFile`); the group present is the variant; `source` is an optional check | PK-OA | D10; decisions row 6 |
| Q14 | Naming bundle of the `fileProcessing` block | as proposed, including `numberOfRecords` defaulting to 50 | PK-OA | D11; decisions row 7 |
| Q15 | `ResultActionType` when `action` is omitted on create | inferred from `saveTo`, always written | PK-OA | D14; decisions row 8 |
| Q16 | Record scope shape and empty-filter policy | `attachments.recordId` / `report.recordId`; refuse saving and Word elements with no selecting filter | PK-OA | D16; decisions row 10 |
| Q17 | Storage policy details | do not read the designer flag for behaviour; no `linkColumn: "none"`; no SysFile override | PK-OA | D15; decisions row 9 |
| Q18 | FastReport printables | refused in this work | PK-RP | D17; decisions row 11 |
| Q19 | Name of the new guide | `process-files` **Agreed 2026-10-08.** | KB-PT | D25; decisions row 15 |

### A.1 Jira and delivery (answer before any PR opens)

#### Q1. Replace the acceptance criteria? (agreed 2026-10-07: as recommended)

The AC of all four issues contain statements that cannot pass, such as "addParameter accepts the Binary / File data
type". The platform type name `File` is the BLOB type BA40CFC5. Binary B7342B7A cannot hold a process value
(`CORE/Terrasoft.Core/Process/FlowEngineStateService.cs:139-148, 409-429`, basis=source). The corpus has 0 Binary or
BLOB parameters (measured 2026-10-01). The AC also say "mapped from its output when ResultActionType = 1", but both
values produce the output collections (`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:60-87`,
basis=source).

| Option | Effect |
|---|---|
| a. Replace PT's AC with AC-1..AC-10 of the [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) section 1.4 (decisions D-1 points there). Replace FE's, OA's and RP's AC with decisions Part D (D-2..D-4), including the O1 edits in pr-split 12.4. | every AC row is testable; the SysFile and Creatio.ai rows move out |
| b. Correct only the platform facts (File/FileLocator, outputs per action, which captures to compare against); keep the scope rows | the "five patterns" row, the SysFile row and the "variant not configurable in this sub-task" row stay untestable as written |
| c. Keep as written | PT cannot be closed: "Binary / File" has no storage path, and "removed from the rejection allow-list" describes the list backwards |

**Recommendation: a.** **What changes:** the Jira text of PT, FE, OA and RP. The acceptance trace in
[test-plan](eng-92719-file-processing-element-test-plan.md) and
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) follows the new text.

#### Q2. Jira links (agreed 2026-10-07: as recommended)

Today PT "blocks" OA (link 560203), and RP has no links (measured 2026-10-01). In code, the Object variant needs no
File process parameter. What needs PT is RP's Process-parameter variant, plus OA's per-file multi-instance test
(basis=source, D23).

| Option | Effect |
|---|---|
| a. L1-L5 from pr-split 12.2: PT **relates to** OA; PT **blocks** RP; OA **blocks** RP; RP **blocks** SF; PT and OA **block** ENG-95985 Send email attachments | links say what really depends on what; the PR edges in pr-split section 7 keep the delivery order PT -> OA -> RP -> SF |
| b. Keep 560203 as "blocks" for sequencing and add L2-L5 | the order is the same; a reader may conclude that OA needs the File type |

**Recommendation: a** (b is acceptable if the team uses "blocks" for sequencing). **What changes:** Jira links only.
OA may start development as a stacked draft while PK-PT is in review.

#### Q3. Where does SysFile attachment storage ship? (agreed 2026-10-07: as recommended)

With `ProcessFeatures.UseSysFileInObjectFileProcessing` on (the stand's state, measured), the designer chooses
storage per object. Objects with a dedicated `<X>File` object keep it; every other object is a SysFile entry, with
SysFile `70ec5d9f` as its file object and `SourceDataEntitySchemaUId` = the record object (measured UO-2, UO-3,
2026-10-01); the `RecordId` link column and a `RecordId` scope are source
(`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:527-540`; the scope's
lookup column `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:420-441`),
pending M7/M21. The SysFile path has no runtime evidence: 0 of 30 shipped elements use it (corpus, measured), and on
the stand 0 SysFile rows have a non-null `RecordSchemaName` other than ConfActivityLog (measured; rows with a NULL
`RecordSchemaName` were not counted). Its read and write paths are source traces only (D15). A Community thread
reports an empty collection when attachments are in SysFile
(https://community.creatio.com/questions/process-file-business-process-element-not-working-sysfile; the asker
reported it, support gave a `SourceDataEntitySchemaUId` workaround, and an August 2026 comment asks whether it was
fixed; documentation, not evidence of today's behaviour). Five measurements stand before its code (M7, M8, M21, M23,
M25). Three of them need the user to build designer probes.

| Option | Cost | Risk |
|---|---|---|
| a. NEW Sub-task SF under FE. OA refuses SysFile-mode sources and targets with a message, and describes designer-built SysFile elements without loss. | one more PR triple at the end (about 1-2 days); one interim release in which custom objects without their own attachment object cannot be file sources or targets | lowest: the unmeasured path ships only after its measurements |
| b. Keep SysFile in OA, conditional on M8/M23, with the D15 fallback (refuse what fails) | OA's diff grows by about 20-25% (inference); five measurements sit on OA's path | OA waits for the user-built probes |
| c. Ship SysFile as the designer does, unmeasured | none up front | an unmeasured write path becomes the default for every custom object |

**Recommendation: a** (O1). **What changes:** create SF (Q9). OA's AC row reads as in pr-split 12.4. The SysFile
capture (M21) moves to SF. M7, M8, M21, M23 and M25 leave OA's path.

#### Q4. Slot order of RP and SF (agreed 2026-10-07: as recommended)

| Option | Consequence |
|---|---|
| a. RP before SF | one measurement set and one SysFile lift for all three variants (SF lifts Object, Report and Process together) |
| b. SF before RP (contingency X3) | custom-object attachments become available earlier. RP then ships Report and Process with SysFile targets refused, unless M23 (c)(d) passed before its code freeze |

**Recommendation: a**, unless the owner ranks custom-object attachments above generated reports. AI Toolkit apps
create custom objects, and a custom object is the typical SysFile case (inference). **What changes:** the slot order
in pr-split section 6, and link L4.

#### Q5. Downstream consumer patterns (agreed 2026-10-07: as recommended)

FE's AC asks for tests of five patterns. Two consumers, Send email and the Creatio.ai call, read files through
DYNAMIC element parameters `Attachments<N>` + `Attachments<N>FileLocator` and `Files<N>` + `Files<N>FileLocator`.
The runtime finds them by name prefix (`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:280-288`;
`PS/CrtCopilot/branches/7.8.0/Schemas/ExecuteIntentUserTask/ExecuteIntentUserTask.cs:91-108`),
and no PB file mentions `Attachments` (grep, 0 files, 2026-10-01), so the builder cannot create them today
(basis=source). ENG-95985 Send email attachments already has exactly that scope. ENG-92725 Execute AI Intent element
(BP generation) is Closed, Won't Do (measured in Jira, 2026-10-01).

| Option | Effect |
|---|---|
| a. Send email pattern -> ENG-95985 Send email attachments; Creatio.ai pattern dropped from FE; the guide says "not buildable through this tool yet" (never "Creatio cannot") | FE keeps four patterns plus Add data -> report |
| b. Keep both in FE | FE grows the dynamic-slot machinery of two foreign elements |
| c. Reopen ENG-92725 Execute AI Intent element (BP generation) for the Creatio.ai pattern | a second element ticket is reopened for one pattern |

**Recommendation: a.** **What changes:** FE's "Tests cover the five patterns" row (D24), ENG-95985 Send email
attachments' scope (link L5), and the "Consumers" section of the new guide.

#### Q6. PR split (agreed 2026-10-07: as recommended)

| Option | PRs | Comment |
|---|---|---|
| 1. One PR per repository for everything | 3 | a package diff of about 12-16k lines in one review; three issues close on one merge; every in-flight branch re-cuts against it (pr-split 10.1) |
| 2. One triple per Jira issue (PT, OA, RP), as in the first draft of decisions D27 (not attached) | 9 | SysFile stays inside OA, conditionally |
| 3. One triple per Jira issue plus SF and the docs-only CL-PT-DOC and CL-DOC (pr-split) | 14 | at most one package PR in human review at a time; each merged package PR is a self-consistent product state |
| 4. A PT triple plus one FE triple for both sub-tasks | 6 | the largest package diff of the cycle |

**Recommendation: 3.** The measured reasons are in pr-split section 3: approvals are dismissed on push, review
latency does not drop below 1.5k added lines, and two of our package PRs in review at once stamped the same number
twice. **What changes:** the PR table in pr-split section 4, four rebundles and four floor raises.

#### Q7. Where is the mirror defect H-1 fixed? (answered by M3: there is no defect)

**Answered by M3 on 2026-10-02 (CrtProcessBuilder 1.6.6.54): H-1 is refuted.** The mirror probe ran 3 iterations, all
`M3 name set` (C.7). The outer Script mapping copies the whole collection value, items included, so the unbound item
does not matter for reading. MH is not created, and contingency X4 applies. The question and its options are kept
below as the record of what was considered.

H-1: the shipped `typeFromElement` mirror of ENG-96230 Collection process parameter type binds the outer level only
(`PB/Parameters/ProcessParameterService.cs:500-512`). The runtime rebuilds rows by the TARGET item names
(`CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:441-488`), so every mirrored row would read null
(basis=source; M3 refuted it). Shipped content never has this shape: 0 of 61 bound collections (measured).

| Option | Effect |
|---|---|
| a. Its own Sub-task MH under PT, delivered first as its own triple (pr-split O2) | a shipped defect gets a ticket, its own evidence and its own revert; PT adds the P2 policy on top of MH's `BindCollection` |
| b. Inside PK-PT, with a Sub-task opened for the record only (decisions D9, first version) | one cut fewer; the fix is reviewed inside a larger diff |

M3 refuted H-1, so neither option applies: MH does not exist, and binding the items anyway is one parity commit in
PK-PT (contingency X4). **Recommendation before M3: a.** **What changes now:** nothing on the track; no MH triple, no
KB-MH.

#### Q8. Delivery protocol bundle (O4-O8, O-10) (agreed 2026-10-07: as recommended)

| # | Item | Recommendation |
|---|---|---|
| O4 | The FE spec (PRD, ADR, stories, test plan) goes in a docs-only clio PR "ENG-92719 File processing element" (CL-DOC), merged before any FE code PR opens | yes |
| O5 | The data-driven variant registry (one `IFileProcessingVariant` per schema UId, from which the handler, the refusals, the filter target and describe all read their claim) is OA's design, recorded in the ADR | yes |
| O6 | Ask for a second disposable .NET Framework stand for draft iteration | yes; it speeds up the track but is not a premise |
| O7 | Lower PRs with a stacked draft above them merge with "Create a merge commit" | yes |
| O8 | A merge window per ticket, agreed with the owners of the in-flight work that shares files with this split: ENG-99970 CAADT runs for BPMS Tools cost 6-15x other teams - find and close the gap; ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph validator into the write path (ENG-88414) (ENG-88414 is AI-driven application development); ENG-92113 Deliver clioprocessbuiilder package - CI for adding package into clio release | yes |
| O-10 (ENG-95984 File process parameter type plan) | ENG-95984 File process parameter type's BMAD set (spec, ADR, story, test plan, sprint-status row) goes in a docs-only clio PR, CL-PT-DOC, which merges before PK-PT opens (pr-split E9). The alternative is an owner-approved exception to the AGENTS.md pipeline | yes |

**What changes:** the review and merge protocol in pr-split section 14. A human merges every PR.

#### Q9. Which follow-up Sub-tasks to create, and when (agreed 2026-10-07: as recommended)

All are of type Sub-task (titles from decisions D-5 and pr-split 12.3):

| Title | Parent | Create | Slot (pr-split 12.3) |
|---|---|---|---|
| SysFile attachment storage in the Process file element (SF) | FE | day 0, if Q3 = a | main track, after RP |
| typeFromElement collection mirror leaves its items unbound (MH) | PT | not created: M3 refuted H-1 | - |
| Refuse collection parameters as filter values | FE | day 0 (M19 sets its urgency, not its existence) | side lane while PK-OA is a draft |
| Declared item shape for Collection process parameters | PT | day 0 | after PK-RP |
| Allow-list the data types a typeFromElement mirror may copy | PT | day 0 | after PK-RP |
| Describe: decode process-parameter sources into re-appliable names | PT | day 0 | after PK-RP |
| Builder-made user tasks do not set SerializeToDB | FE | day 0 | after PK-SF |
| Report the designer storage re-save defect to CrtProcessDesigner (H-G3-1) | FE | only after M11 confirms it | no PR in these repositories |
| Process parameter source of the Process file element | FE | only on contingency X2 (RP's Process half fails its stand proof) | replaces RP's Process half |

**Recommendation:** as in the "Create" column. The unconditional Sub-tasks describe known work and cost nothing to
open early. A conditional Sub-task created before its measurement would record an unproven defect as fact. Every
side-lane triple restamps, so none may overlap a final review window on the main track.
No Sub-task is created for Process file -> Send email attachments: ENG-95985 Send email attachments already owns it
(link L5; [plan](eng-92719-file-processing-element-plan.md) section 5.4).

### A.2 ENG-95984 File process parameter type contract

#### Q10. How does a caller declare a file collection? (agreed 2026-10-07: A)

A file collection is the generic `CompositeObjectList` 651ec16f with one FileLocator item; no file-collection data
type exists (basis=source). A file output cannot be mirrored because its items carry no Tag
(`PB/Parameters/ProcessParameterService.cs:456-475`). A described collection fed back as `CompositeObjectList` would
rebuild shapeless and save green (`PB/Parameters/ProcessParameterService.cs:31-33`).

| Option | Covers a callee input or a script-filled list | Older CrtProcessBuilder |
|---|---|---|
| A. `type: FileCollection` alias; describe reads it back as `FileCollection` | yes | refused loudly ("not supported") |
| B. Relax the mirror for file outputs | no | silently old semantics, or refused |
| C. Generic `itemProperties` on a declared Collection | yes | the unknown member is dropped silently, and a shapeless collection saves green |

**Recommendation: A**, with C as the later Sub-task "Declared item shape for Collection process parameters". This is
low stakes. **What changes:** the create/modify type lists (+30 B / +28 B), the describe predicate, one exception
sentence in `KB/guidance/mcp/guides/processes/parameters.md:35-37`, and a PBT round-trip test.

#### Q11. Default direction of a FileCollection (agreed 2026-10-07: Variable)

The designer's plain Add writes Variable; "Create from element" writes Out
(`PD/ProcessSchemaPropertiesPage/ProcessSchemaPropertiesPage.js:1204-1236, 665-715`, basis=source). Shipped
process-level collections: 85 Out, 30 Variable, 12 In, 6 Internal of 133 (measured, corpus recount 2026-10-01; the
user-task schemas hold 17 more, which are element parameters). A caller can feed only an In or Variable callee
parameter, and anything else is refused loudly
(`PB/Mappings/ProcessMappingService.cs:79-110`).

| Option | Effect |
|---|---|
| Out | one default per stored type (`CompositeObjectList`), the shipped majority (85 of 133, 64%), and the existing guidance pin stands; a caller-filled collection must be declared `In`, and the refusal names that fix |
| Variable | plain-Add parity; a second default rule for the same stored type, which agents must hold |

Both shapes have a shipped capture (`MarkProcessesToCancel.FilesCollection` Out, `FileParameterProcess.FileCollection`
Variable), so the AC "matches a designer-built capture" can pass either way. The first recommendation was Out.
**Revised 2026-10-07 to Variable, and agreed by the owner the same day.** Recount 2026-10-07 with in-process reads: 84 Out, 30 Variable, 12 In, 6 Internal; a mapping inside the same process reads **3 of the 84 Out** and **11 of the 30 Variable** collections (script reads by name are not counted). Out is the shape of a
result handed to the caller; a collection filled and used in the same process is Variable. The core does not
restrict a process-level parameter by direction (decisions D3). A Variable FileCollection is never refused: a caller
can fill it and the process can return it; it is also the designer's plain Add and the shipped in-process file case.
**What changes:** one guide sentence and the serialization pin used by the PT test.

#### Q12. Two-level binder policy (agreed 2026-10-07: P3 resets with a notice; P2 and R-M2 on file-consuming targets only)

The designer binds the nested item and, when the source is itself a collection item, also the parent
(`PD/MappingEditMixin/MappingEditMixin.js:947-995`). All 11 of 11 shipped file-collection bindings bind both levels
(measured). Two choices are open (D5):

| Choice | Option | Effect |
|---|---|---|
| P3: a plain single-file source onto an item whose parent is bound to a collection | reset the parent, with a notice (recommended) | the element copies the one file |
| | designer parity: leave the stale parent | N copies of one file (one per source row); there is no `removeMapping` operation to clear it |
| P2 / R-M2 scope | file-consuming targets only: the Process variant's `Files`, a process-level FileCollection, later the email and Creatio.ai slots (recommended) | Read data -> multi-instance mappings are untouched, apart from a notice |
| | every collection | also changes what an outer mapping writes for Read data -> multi-instance flows; unmeasured and outside this work |

**Recommendation:** reset with a notice; file-consuming targets only. **What changes:** the PBT regressions in
decisions D5 and the guide's binder section. P1 moves the `[RequiresPackage]` floor either way.

### A.3 ENG-92719 File processing element contract

#### Q13. Element token and variant discriminator

"Process file" is one palette entry backed by three schemas, and changing the source REPLACES the element (measured
live: `getExcludedMenuItems()` = `["ReportFileProcessingUserTask","ProcessFileProcessingUserTask"]`, 2026-10-01).
`source` already means an object NAME in four data blocks (`PB/Contracts/ProcessDescriptorContracts.cs:1012, 1038,
1155, 1213`).

| Choice | Options | Recommendation |
|---|---|---|
| Token | `fileProcessing` + alias `processFile`; `processFile` as primary; three tokens, one per schema; the generic `userTask` route with a block (an older server silently drops the block) | `fileProcessing`, alias `processFile` |
| Discriminator | (ii) none: the group present (`attachments` / `report` / `files`) is the variant, and `source` is an optional identity check; (iii) a `fileSource` key; (i) a required `source` enum (collides with `readData.source` and with `files.processParameter`) | (ii) |

**What changes:** the type lists and the block clause in the create/modify descriptions, the ManagerMap arm that ships
in CL-PT, and the refusal texts F-E3.

#### Q14. Naming bundle of the block

Proposed (D11): groups `attachments`, `report`, `files`, `saveTo`; `action: useInProcess | saveToAttachments`;
`report.printable` (accepts list-printables' `templateId`); `report.fileNameSuffix` / `fileNameSuffixColumn`;
`recordId` inside each group; `attachments.numberOfRecords`; `attachments.sort {column, direction}` (the readData
shape). One point needs an explicit acknowledgement: **`numberOfRecords` omitted means 50 here** (the designer
default, `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:10, 145-151`;
range 1-5000, `:353-357` and `PD/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:472-484`),
**but "read all" in `readData`**.
The contract, the guide and a create-time notice state this.

**Recommendation:** accept as proposed. Renaming before code is free; afterwards it costs a deprecation. **What
changes:** contract member names in package and clio DTOs, the guide, and the description bytes (to be re-measured).

#### Q15. `ResultActionType` when `action` is omitted on create

SaveToFiles = 0, UseInProcess = 1. The designer defaults the Object and Report pages to 1
(`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:11-12, 894-903`). An
unset value runs as 0 (basis=source).

| Option | Risk |
|---|---|
| Infer on create: `saveTo` present -> saveToAttachments, absent -> useInProcess; contradictions refused; always written (recommended) | none found; on update, the action changes only when sent, or when `saveTo` is sent to an in-process element |
| Designer default (useInProcess) | a caller who sent `saveTo` but forgot `action` gets a silent no-save |
| Mandatory `action` | one more required key, and more refusal round trips |

The Process variant accepts only saveToAttachments in every option (1 throws `NotSupportedException`,
`PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:89-97`). **What changes:** refusals F-E6, the
describe member `actionStored`, and the guide.

#### Q16. Record scope shape and empty-filter policy

The natural phrasing `filter {object: "Account", Id = X}` evaluates as `AccountFile.Id = <account id>` and finds zero
files, with success (`PB/Filters/DataNodeFilterTarget.cs:57-91`, basis=source). An empty filter reads up to N
arbitrary files (Object), or generates one Word document per record of the whole entity (Report,
`CFG/ReportEngine.Reports.cs:257-276`, basis=source).

| Choice | Options | Recommendation |
|---|---|---|
| Scope shape | R2: `attachments.recordId` / `report.recordId` in the shared value-source vocabulary; R1: `filter.recordId` (a second meaning of `recordId`, and clio's bagless filter DTO drops it on describe); R4: no sugar (the agent must know the storage rule and the link column) | R2 |
| Empty filter, evaluated at the end of the request for touched elements only | refuse Object + save and Report (Word), notice for Object + use in process; or notice everywhere (the designer's latitude) | refuse / notice |

**What changes:** `FileProcessingScopeFilter`, the end-of-request ledger, refusals F-F1/F-F2, and the e2e refusal
cases.

#### Q17. Storage policy details

These choices remain inside the storage resolver whichever answer Q3 gets (D15):

| Item | Recommended | Alternative |
|---|---|---|
| The designer flag `UseSysFileInObjectFileProcessing` | not read for behaviour (the server runtime never reads it); a notice when it reads OFF, once M25 confirms the read | read it and reproduce the OFF list (a `ModuleStructure` copy plus a string-coupled feature read) |
| `linkColumn: "none"` | not offered in v1: a same-schema copy with no link column inherits the SOURCE's link (`CORE/Terrasoft.File/File.cs:174-180`), so "none" would not mean unlinked | offer it with a warning |
| `storage: "sysFile"` for an object that has its own attachment object | not in v1; revisit after M12 | allow it |
| SysFile targets if M23 fails (moot under Q3 = a until SF) | refuse them; refuse SysFile sources too if M8 fails | ship anyway |

**What changes:** refusals and warnings F-E7, describe `storage`, and the guide's storage section.

#### Q18. FastReport printables

The stand has 0 FastReport printables (measured: `SysModuleReport` 5 rows, all MS Word, 2026-10-01). The stand
root (`C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib/Terrasoft.WebApp.Loader`, where the installed
CrtProcessBuilder 1.6.6.54 sources were found) has no file named `*fastreport*` (file search, 2026-10-01); M15 confirms
this through `list-packages`. The process path resolves the
generator with no constructor argument (`CFG/ReportEngine.Reports.cs:286-288`), while the generator has only a
`FastReportGenerator(UserConnection)` constructor
(`PS/FastReportEngine/branches/7.8.0/Schemas/FastReportGenerator/FastReportGenerator.cs:48-50`), so it would get a
connection that is not the process's, or fail (basis=source, a hypothesis). DevExpress throws eagerly
(`CFG/ReportEngine.Reports.cs:304-317`).

| Option | Effect |
|---|---|
| B. Refuse FastReport in this work; lifting it later is additive (recommended) | no runtime path ships without evidence; the refusal is unit-tested only |
| A. Accept FastReport with a notice | a configuration that may always throw is accepted silently on every environment |

**What changes:** refusal F-R1. M15 can reopen the question only if FastReport packages and printables turn up.

#### Q19. Name of the new guide (agreed 2026-10-08: `process-files`)

The existing articles are nearly full: process-modeling and activity-connections 99.9%, parameters 98.8%,
element-catalog 97.7% (measured on a replica of the budget check, 2026-10-01; the full table is in decisions D25).
New text therefore goes to a NEW guide, born in KB-PT.

| Option | Effect |
|---|---|
| `process-files` (recommended) | covers the type, the element and its consumers |
| `process-file` (the palette caption) | reads as the element only |
| one guide per ticket | two routing rows, two registrations, a split topic |

**What changes:** `curated-knowledge-names.json` re-pin in CL-OA, the routing row, and the
`get-guidance name=process-files` pointer in the create/modify descriptions.

---

## B. Measurements still needed on the stand

### B.0 Rules for every measurement

- **Stand:** clio environment `Creatio` (core 10.1.37, .NET Framework,
  CrtProcessBuilder 1.6.6.54).
- **Go-ahead:** every row marked "write" needs the user's explicit go-ahead before it runs. This covers a saved
  process, a run, a fixture record, an upload, an Activity completion, a cleanup and a culture change.
- **One at a time:** schema writes and runs go one at a time; a parallel burst crashes the app pool.
- **Package and names:** probe processes live in package `Custom` and are named `UsrFp<Id><Short>`, for example
  `UsrFpM3Callee`. Fixture records are named `FP <Id> ...`. The exception is M1: its probe and fixtures keep the
  names decisions D29 and the test-plan give them (`UsrG1FilesBindingProbe`, `G1 Probe Source`, `G1 Probe Target`),
  and M4, M6, M17, M23 and the ENG-95984 File process parameter type test-plan's V7/V8 reuse those fixtures.
- **Who builds:** a probe that needs a File parameter, a configured Process file element or the Process variant is
  built by a human in the classic designer, because the builder cannot build it until these tickets land.
- **Which package version:** the stand carries one cut at a time, and a PR's final cut and stand reinstall come
  BEFORE its review is requested ([pr-split](eng-92719-file-processing-element-pr-split.md) section 14, steps 4-5 and
  the stand rules); a draft may also be cut for the stand earlier (step 2). "During PK-PT's review" therefore means
  "on PT's cut". The probes that characterise shipped CrtProcessBuilder behaviour (M3, M3b, M6's builder mapping,
  M14, M19, M20, M24, and M13's describe as a precaution) run on **day 0, on 1.6.6.54, before the first cut of this
  work (PT's, a draft cut included) is installed**. M14 is the sharpest case: from PT's first cut its mappings
  `PF1.Files <- OF1.ObjectFiles` and `PF1.Files.File <- OF1.ObjectFiles.File` go through binder rules P2 (the outer
  mapping pairs the file item itself) and P1 (the item mapping then becomes a no-op)
  ([ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md),
  binder table rows P1-P2). An M14 taken on PT's cut would record PT's writer, not 1.6.6.54's, and set the D20 pins
  (TC-37..TC-39, `SyncShape.Server` in the [test-plan](eng-92719-file-processing-element-test-plan.md)) wrongly. Every
  other measurement is version-independent: designer-built (M1/M2, M4, M7, M8, M9, M16, the M17 runs, M21, M23, M25,
  M26), UO reads (M10, M11, M12) or SQL and list reads (M15, M17-Q7), so it may run on whichever cut the stand holds.
  M18 and M22 run after a cut by design. Every record names the CrtProcessBuilder version it ran on.
- **Repeating a version-dependent probe later:** reinstalling 1.6.6.54 over a higher cut is a downgrade, and
  `install-process-builder` refuses it unless a human passes its CLI-only override; the MCP path cannot
  (`CLIO/clio/Command/InstallBundledPackageCommand.cs:51-56, 277, 472`, basis=source). The cost: that human
  decision (it rolls the package back for everyone on the stand), a reinstall from a clio master build (which
  bundles 1.6.6.54, `CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:318`), telling every verifier, the
  probe itself, then a reinstall of the current cut and a re-run of its instrument checks (pr-split section 14,
  stand protocol step 2) before any verification continues on that cut. Day 0 avoids all of it.
- **Designer probes without a save (the UO method):** open
  `<stand URL>/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/` in an already logged-in
  browser. Nobody types credentials for an agent. Build the element in a NEW unsaved process and close its properties
  panel, which writes the panel's values to the in-memory element. Then read the element with read-only JavaScript.
  Close the tab WITHOUT saving. The sketch below uses the objects read in UO-3/UO-4. Adjust the collection calls if
  the API differs, and confirm that `items[0]` is the open process by its caption.

  ```js
  // read-only; the unsaved process open in this tab
  const inst = Terrasoft.ProcessSchemaManager.items[0].instance;
  const el = inst.flowElements.getItems().find(e => e.caption === "<element caption>");
  const p = n => el.parameters.getItems().find(x => x.name === n);
  ({ src: p("SourceEntitySchemaUId").sourceValue, data: p("SourceDataEntitySchemaUId").sourceValue,
     filters: p("DataSourceFilters").sourceValue, order: p("OrderByInfo").sourceValue });
  ```
- **Tool checks before a builder probe:** confirm that the session's MCP clio is current: `get-tool-contract`
  shows the expected arguments. An older client drops unknown arguments silently; on 2026-10-01 a stale session
  client stripped `itemProperties` from describe output (Part C.3). Before any guidance-dependent run, check that
  `info-knowledge` shows the expected library version, because a failed `update-knowledge` keeps serving the old one.
- **Cleanup (write, go-ahead):** delete probe processes with `delete-schema` and a CLI `--timeout`; a remote delete
  took about 6 minutes on 2026-09-27 and 7-50 seconds on 2026-10-02. Delete fixture and copied rows with one `execute-dataservice-batch` of
  DeleteQuery items, because the stand rejects HTTP DELETE. Then re-run the evidence queries to confirm.
- **UI checks** (an attachment list, a card's display text) are made by the user.
- **Cleanup tooling on 2026-10-02:** the session's clio client has no `execute-dataservice-batch`, and `odata-delete`
  sends HTTP DELETE, which this stand rejects; the fixture rows need another path, chosen with the user.

### B.1 Common read-only evidence queries

Evidence follows [test-plan](eng-92719-file-processing-element-test-plan.md) section 9. Reads are read-only
`execute-esq` calls (`CLIO/clio/Command/McpServer/Tools/ExecuteEsqTool.cs:37`, `ReadOnly = true`), one at a time.
Before each run that may write a file, take an **Id baseline** (EV-0) and compare against it afterwards. No read
filters by `CreatedOn`, because ESQ resolves date filters in the profile's time zone. The SQL column gives the same
read for `clio sql "<query>" -e Creatio` (`execute-sql-script`). That command runs through cliogate and also
accepts writes, so only SELECT text is ever sent. The only read that exists as SQL alone is the catalog query of
M17-Q7. On cliogate 2.0.0.53+, `execute-sql-script` logs every statement in `ClioSqlRequestLog`
([serialization-capture](eng-92719-file-processing-element-serialization-capture.md) section 12.2): an audit row with
no business data, so M17-Q7 stays in the read-only count.

| Id | When | Reads | SQL form |
|---|---|---|---|
| EV-0 | before each run that may write a file | the target file rows (`ContactFile` by `Contact`, `AccountFile` by `Account`, `InvoiceFile` by `Invoice`, `SysFile` by `RecordId` with NO `RecordSchemaName` filter, since a missing stamp is one of the hazards under test) and the source rows: `Id, Name, Size` | `SELECT Id, Name, Size, TypeId FROM ContactFile WHERE ContactId IN ('<source>', '<target>')` (likewise `AccountFile`/`AccountId`, `InvoiceFile`/`InvoiceId`, `SysFile`/`RecordId`) |
| EV-1, EV-2 | the run | the instance id and its root status. A root row without `CompleteDate` is parked or running, not hung; the root row is never the evidence | `SELECT TOP 5 l.Id, l.StatusId, l.StartDate, l.CompleteDate FROM SysProcessLog l JOIN SysSchema s ON s.Id = l.SysSchemaId WHERE s.Name = '<probe>' ORDER BY l.StartDate DESC` |
| EV-3 | after | the per-element outcome and the error text: **this is the evidence** | `SELECT e.Caption, e.SchemaElementUId, e.StatusId, e.StartDate, e.CompleteDate, e.ErrorDescription FROM SysProcessElementLog e WHERE e.SysProcessId = '<EV-2 Id>' ORDER BY e.StartDate` |
| EV-4 | multi-instance | the callee instances that are not in a baseline taken before the run (one per iteration), with their elements | before: `SELECT l.Id FROM SysProcessLog l JOIN SysSchema s ON s.Id = l.SysSchemaId WHERE s.Name = '<callee>'`; after: the same with `AND l.Id NOT IN (<baseline>)`, joined to `SysProcessElementLog` on `SysProcessId` |
| EV-5 | after | the file rows that are not in EV-0: `Id, Name, Size, Version, TypeId`; on SysFile also `RecordId, RecordSchemaName`; where attribute inheritance is checked also `Notes, Tag, CreatedById` | EV-0's query plus `AND Id NOT IN (<EV-0 ids>)` |
| EV-6 | after | the source rows again, compared with EV-5: same `Name` and `Size`, new `Id`s, sources unchanged | EV-0's source query |
| EV-7 | while parked, then after | the temporary process files of the instance (the instance column is confirmed against `SysProcessData` on the first run) | `SELECT Id, Name FROM SysProcessFile WHERE SysProcessId = '<instance Id>'` |
| EV-8 | after | what an executed element produced, per instance: its parameter values, collections included (each file locator with `_entitySchemaName` and `_recordId`). Measured on M6 | `SELECT d.SchemaElementUId, d.Status, CONVERT(varchar(max), d.PropertiesData) FROM SysProcessElementData d WHERE d.SysProcessId = '<EV-2 Id>'` |
| MD | after a save | the saved metadata: `clio pull-pkg Custom -e Creatio -d <scratch dir> -r`, then `Schemas/<probe>/metadata.json`, or the `SysSchema.MetaData` row through `execute-esq`. Describe is not used to check a nested-only binding, because it hides one (`PB/Describe/ProcessDescriber.cs:187-193`) | - |

Status values (`PS/CrtBase/branches/7.8.0/Data/SysProcessStatus`, basis=source): Running
`ed2ae277-b6e2-df11-971b-001d60e938c6`, Completed `815c9586-b6e2-df11-971b-001d60e938c6`, Error
`f942c08d-b6e2-df11-971b-001d60e938c6`, Canceled `1be78f3e-234d-4d6a-869a-dc07253fd2f3`. A file row of type File has
`TypeId = 529BC2F8-0EE0-DF11-971B-001D60E938C6` (`FileConsts.FileTypeUId`, basis=source). When an error text is cut
short, the full stack is in `Error.log` under `C:\Windows\Temp\Creatio\Creatio\0\Log\<date>\` on the stand host.

### B.2 Summary and gates

| ID | Question | Write | Built by | Gates | Settles | Status |
|---|---|---|---|---|---|---|
| M7 | the designer's filter JSON for a SysFile `RecordId` scope | no | UO method | PK-SF | D16 SysFile scope shape | open |
| M10 | the designer's object list against the resolver's prediction | no | UO method | PK-OA | D15 refusals R3-R5 | **done 2026-10-02**: matches the prediction (C.6) |
| M11 (a)(b) | H-G3-1: the Report target half; the Object source half | no | UO method | PK-OA | D15/D19 severity; bug report; guide warning | **done in memory 2026-10-02**: not reproduced on either half (C.6); (c) with a save remains optional |
| M12 | SysFile + Account reloads as plain "Account" | no | UO method | none (v2) | Q17 override | open |
| M13 | describe of PrintContractsReport: converged or snapshot | no | agent | PK-OA (TC-45 Object fixtures), PK-RP (TC-45 Report fixtures) | D19 fixtures | **done 2026-10-02**: `Guid` with `referenceSchema: Contract` (C.6) |
| M15 | FastReport packages and printables | no | agent | PK-RP | Q18 | **done 2026-10-02**: no FastReport package, 0 FastReport printables (C.6) |
| M17-Q7 | foreign key of `ContactFile` to Contact | no (audit row only: cliogate 2.0.0.53+ logs the statement in `ClioSqlRequestLog`) | agent | PK-OA | the F-E9 empty-record notice wording | **done 2026-10-02**: FK `ContactFile.ContactId` -> Contact, 0 empty-Id rows (C.6) |
| M3 | the shipped mirror reads null rows (H-1) | write | builder + one designer step | PK-PT / MH | Q7, D9 | **done 2026-10-02**: H-1 refuted, 3 iterations all "name set"; no MH, X4 (C.7) |
| M3b | baseline (MI-0 in the ENG-95984 File process parameter type test-plan): an item-only multi-instance mapping runs one iteration on 1.6.6.54 | write | builder | PK-PT verification | the version-gated guide sentence (D25) | **done 2026-10-02**: 1 iteration, Name empty (C.7) |
| M6 | flat File <- collection item outside a row context | write | designer + builder | PK-PT | R-M1 (D5) | **done 2026-10-02**: `F` empty while OF1 held two files; R-M1 stays a refusal (C.7) |
| M14 | the server-built element shape on 1.6.6.54 | write | builder | PK-OA | D20 pins | **done 2026-10-02**, card display included (C.7) |
| M1 | a nested-only `Files.File <- File` copies one file | write | designer (script task) | PK-RP | D18 single-file path; PT AC | open |
| M2 | an unset single File gives an element Error (NRE) | write (save + run) | reuses M1 | with M1 | the guide's single-File wording (D18) | open |
| M8 | an Object element finds a Freedom UI upload in SysFile | write | user + designer | PK-SF | SysFile sources | open |
| M23 (a)-(d) | the SysFile write path, four cases | write | designer | PK-SF | SysFile targets | open |
| M21 | a designer save capture of a SysFile-mode Object element | write | designer (SC-1 E3 of [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) section 12.4; M23b's saved metadata cross-checks it) | PK-SF | SF serialization AC | open |
| M25 | the server feature read and its code name | write | designer (script) | PK-SF (notice only) | D15 notice | partly (client read) |
| M9 | a SysFile sort on a record-object column throws | write | designer | none | issue severity | open |
| M4 | a FileCollection feeds the Process variant end to end | write | designer; repeated builder-written as V7 (PT cut) and ST-04 (RP cut) | verification | D2/D5/D18 end to end | open |
| M16 | the lifetime of temporary report files | write | designer | none | D14/D17 notice wording | open |
| M17 runs | an empty `ConnectedObjectId`: FK failure (legacy), orphan (SysFile) | write | designer | none | the F-E9 notice severity | open |
| M18 | a constant file-name suffix under another culture | write | builder after PK-RP | none | culture note | open |
| M19 | a collection parameter as a filter value | write | builder | none | urgency of "Refuse collection parameters as filter values" | **done 2026-10-02**: accepted at build, run fails with ArgumentException (C.7) |
| M20 | an unknown block next to a known field is silently dropped | write | builder | none | the D26 premise | **done 2026-10-02**: silently dropped (C.7) |
| M22 | the designer opens builder-made artifacts; a no-op save changes nothing | write (no-op save) | user, after each cut | verification | parity confidence | open |
| M24 | an array sent into an object member, and an object into an array member, at the WCF binder | write | builder | none | the `attachments.sort` guide sentence | **done 2026-10-02**: both nulled by the binder, refused by a later rule with a misleading text (C.7) |
| M26 | after "Save to object attachments", `ObjectFiles` holds the SOURCE locators ([traps](eng-92719-file-processing-element-traps.md#t-19) T-19) | write | user (designer-built probe) | none (it gates the OA.12 sentence and the CL-OA record) | the T-19 guide sentence and the knowledge record | open |
| M5 | outer-only `Files` gives an NRE | - | - | - | nothing (refused anyway) | skipped |

Order, read-only first (B.0 "Which package version"):
- **Day 0, on 1.6.6.54, before the first PT cut is installed:** [M13] (read-only), then the builder probes
  [M6][M3][M3b][M14][M19][M20][M24], one at a time. M6 and M3 gate PK-PT code (M3 is done), M14 gates PK-OA code, M13
  gates PK-OA and PK-RP (the TC-45 fixtures); M3b is PT's baseline; M19, M20 and M24 gate no code, but they measure 1.6.6.54 behaviour that a later
  cut changes or makes unmeasurable (from OA's cut `fileProcessing` is a known block, so M20 can no longer drop it).
  Moving M14, M19, M20 and M24 here adds four sequential builder probes to day 0 (M14 and M19 are creates; M20 and
  M24 modify M19's process). They hold back PT's first stand install, not PT's code.
- **Version-independent, on any cut, so also during PK-PT's review:** [M10][M11ab][M17-Q7] before PK-OA;
  [M1][M15] before PK-RP; [M26] before KB-OA writes the sentence; [M7][M8][M21][M23a-d][M25] before PK-SF (off
  the critical path under Q3 = a).

### B.3 Read-only measurements

**M7: the SysFile scope filter the designer writes.** UO method. Add Process file; source `Account address` (a plain
entry, so SysFile mode). Under "How to filter records?", add the record condition. In SysFile mode, the page offers
`SysFile.RecordId` as a LOOKUP of the record object
(`PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:420-441`,
basis=source). Variant 1: `= <one Account address record>` (a constant). Variant 2: first add an unsaved process
parameter `AddrParam` of type Lookup -> Account address, then set the condition `= [#AddrParam#]`. Close the panel
and read `DataSourceFilters.sourceValue.value` (JSON). Record the leaf's `filterType`, `comparisonType`, left
`columnPath`, `dataValueType`, `referenceSchemaName` and the right-hand shape (`rightExpressions` with
`parameterValue`, or a mapping expression). **Settles** D16: if the designer writes a lookup InFilter on `RecordId`
with `referenceSchemaName` = the record object, the builder writes that (the default). A Guid CompareFilter is
written only if that is what is captured. `Split` accepts both shapes either way.

**M10: the designer's object list.** UO method. Open "Which object to receive file from?" and clear the text first.
Text left in the field filters the list, which is the UO-1 artefact. Read the full list with `read_page`. Then choose
"Save to object attachments" and read "What object to save file to?" the same way. Compare each entry with the
resolver's prediction (decisions D15):

| Object | Predicted entry |
|---|---|
| Account | `Account (File and link of account)` (measured, UO-2) |
| Contact | `Contact (Contact attachment)` (measured, UO-2) |
| Lead | `Lead (<FileLead caption>)`: the `FileLead` special case |
| Account address | plain entry, SysFile (measured, UO-2) |
| DNS guide file (CrtEmailMarketingApp is installed) | listed by its own caption, as an orphan file object |
| Uploaded file, FeedFile | plain entries |
| VwSysProcessFile | orphan entry |

**Settles** refusals R3-R5 of D15. A mismatch changes the resolver algorithm before PK-OA code.

**M11 (a)(b): H-G3-1, the designer re-save trap.** The source trace: with the flag on, opening a legacy Object element
fills the `SourceDataEntitySchemaUId` attribute from `SourceEntitySchemaUId`, and the next panel save writes it as a
ConstValue (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:853-861,
910-922`; `ObjectFileProcessingUserTaskPropertiesPage.js:186, 319-328, 379-391`: `SourceEntitySchemaIsFileSuccessor`
is set only by the change handler at `:186`, while on load the select is restored with `{silent: true}` at `:388`,
so the save at `:323-327` takes the ConstValue branch). The runtime would then filter
`AccountFile.RecordSchemaName`, a column that `File` descendants do not have: a throw (basis=source).
- (a) Target half, Report element: open the shipped `PrintQuotationReport`,
  `…/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/7f98d916-6f57-4be7-96d4-7c718884b405`. Open the report
  element's panel, change nothing, and close it. Read `TargetDataEntitySchemaUId`: is it now `4135a9ba…`
  (OpportunityFile)? **Never save shipped content**: close the tab without saving.
- (b) Source half, Object element, the harmful one: in a NEW unsaved process, add Process file with source
  `Account (File and link of account)` and "Use in process", then close the panel. Read `SourceDataEntitySchemaUId`,
  which should be empty (UO-4). Reopen the panel, change nothing, and close it. Read again: is it now
  `149d2eaf-cbd2-49fa-b565-637748ff823c` (AccountFile)? If reopening does not re-run the load path (the value stays
  empty), the result is "not reproduced in memory", not "refuted". Then (c) applies.
- (c) Optional, write: save the process in `Custom`, reload the page, open and close the element, save again, and
  check MD for `SourceDataEntitySchemaUId`. Then run it once (EV-3: expect an Error naming `RecordSchemaName`).

**Settles** the describe issue's severity: "throws at run time" (the source half) or "cosmetic" (the target half).
It also settles the CrtProcessDesigner bug report and the guide warning (D15, D19).

**M12: SysFile + Account.** UO method, no save. On an unsaved Object element, set by JavaScript
`SourceEntitySchemaUId = 70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a` (SysFile) and `SourceDataEntitySchemaUId` = the
Account schema UId (read it with `get-entity-schema-properties Account`). Reopen the panel and read the select's
caption, then close the panel and read both parameters. **Settles** whether a later `storage: "sysFile"` override
survives the designer (Q17). Not before code.

**M13: describe of PrintContractsReport.** `describe-business-process` with process-name `PrintContractsReport`, from
a current clio (check `get-tool-contract` first), on day 0 with the 1.6.6.54 batch (B.0): describe is
CrtProcessBuilder code and PT's cut changes it (D8). The answer is most likely a property of the stored schema
(inference), but one read on 1.6.6.54 removes the doubt. Read `parameters[ConnectedObjectId].type`. The package metadata
types it Lookup `b295071f` (corpus, measured). "Lookup" in describe means an unconverged snapshot; "Guid" means it was
converged. **Settles** whether the D19 describe fixtures must carry the Lookup-typed form.

**M15: FastReport presence.** `list-packages -e Creatio` and look for `FastReport` / `FastReportEngine`. Already
measured: `SysModuleReportType` has 3 rows (MS Word, DevExpress, FastReport), and all 5 `SysModuleReport` rows are
MS Word (2026-10-01). **Settles** Q18. Recommendation B stands unless the packages AND a printable exist. Only then
would a second step run: a disposable process with a Report element on that FastReport printable, "Use in process",
run once, EV-3 (it predicts an error from the generator's connection, Q18; basis=source).

**M17-Q7: foreign-key pre-check.** `SELECT fk.name, OBJECT_NAME(fk.referenced_object_id) FROM sys.foreign_keys fk
WHERE fk.parent_object_id = OBJECT_ID('ContactFile')` and `SELECT COUNT(*) FROM Contact WHERE Id =
'00000000-0000-0000-0000-000000000000'`. One FK to Contact plus 0 rows predicts that an empty `ConnectedObjectId`
fails at the database for a legacy target. **Settles** the wording of the F-E9 notice "a mapped `recordId` may be
empty" ("fails" for legacy, "orphans" for SysFile, the latter pending the M17 runs).

### B.4 Writes that gate code

**M3: does the shipped mirror read null rows (H-1)?** Built on 1.6.6.54, in `Custom`. Evidence comes from branch
logs, so no business records are written.
1. Callee `UsrFpM3Callee` (builder, `create-business-process`): an In Text parameter `Name` and a Variable Text
   parameter `Mark`. Start -> an exclusive gateway with a conditional flow "Name is empty",
   `string.IsNullOrEmpty([#Name#])` (on create a flow condition takes the parameter NAME,
   `KB/guidance/mcp/guides/processes/formulas.md:41-43, 72-74`), to a Formula element captioned `M3 name empty`
   (`Mark` = `"empty"`). The default flow goes to a Formula element captioned `M3 name set` (`Mark` = `"set"`). Both
   continue to an end event.
2. Probe `UsrFpM3MirrorProbe` (builder): Read data `RD1` on Contact in collection mode, column `Name`, 3 records,
   sorted by `CreatedOn`. Then `addParameter P typeFromElement RD1.ResultCompositeObjectList`, which is the ENG-96230
   Collection process parameter type mirror (it binds `P <- RD1.ResultCompositeObjectList`, outer level only). Add a
   sub-process element `SP1` calling `UsrFpM3Callee`, multi-instance over `P`. One designer step follows, because a
   dotted process-parameter source does not exist on 1.6.6.54 (D4): map the callee's `Name` from `P > Name` in the
   designer and save.
3. Control `UsrFpM3Control` (builder only): the same callee, iterated with `InputRecordCollection <- RD1.ResultCompositeObjectList`
   and `InputRecordCollection.Name <- RD1.ResultCompositeObjectList.Name`.
4. Run each once, one at a time. Evidence: EV-4 for `UsrFpM3Callee`, counting the `M3 name empty` and `M3 name set`
   element rows per caller run. Before running, confirm in MD that `P`'s item `Name` has no source.

The ENG-95984 File process parameter type plan (W0), its test-plan (8.1) and test-plan section 8 now give this recipe.
An earlier version had the builder write `InputRecordCollection.Name <- P.Name`. On 1.6.6.54 that mapping is refused:
`ResolveProcessParameter` looks a process parameter up by its whole name
(`PB/Mappings/ProcessMappingService.cs:425-435`, callers `:221, :280`), so `P.Name` is "not found". Hence the designer
step in 2. Branch captions write no business records and need no cleanup beyond the processes (Part D, row 35).

**Settles** Q7 and D9. Three "name empty" with a control of three "name set" confirms H-1: open MH. Three "name set"
refutes it: no MH, and contingency X4 applies. Any other iteration count is recorded and escalated.

**M3b: baseline of an item-only multi-instance mapping.** Same callee. Probe `UsrFpM3ItemOnly` (builder, 1.6.6.54):
`RD1` as in M3, sub-process `SP1` multi-instance, mapping ONLY `InputRecordCollection.Name <- RD1.ResultCompositeObjectList.Name`
(no outer mapping). Run, then count the callee instances with EV-4. Predicted: 1 (the guide passage measured this on
1.6.6.22, `KB/guidance/mcp/guides/processes/sub-process.md:133-137`). After the PK-PT cut, the same request must
give 3 (P1 binds the parent). **Settles** the version-gated sentences in `sub-process.md` and `sub-process-when.md`
(D25), and gives PK-PT's stand proof its baseline (pr-split section 14).

**M6: a flat File <- a collection item, outside a row context.**
1. Fixtures: M1's `G1 Probe Source` (two attachments). No target is needed, because nothing is saved.
2. Designer-built `UsrFpM6FlatFile` in `Custom`: process parameter `F` (Add -> Other -> File, Direction Variable).
   Element `OF1` = Process file, source `Contact (Contact attachment)`, filter Contact = `G1 Probe Source`, read first
   50, "Use in process". Element `PROBE` = Script task:
   `throw new System.Exception("M6 F=" + System.Convert.ToString(Get<Terrasoft.File.Abstractions.IFileLocator>("F")));`
   (`EntityFileLocator.ToString()` prints `EntitySchemaName=..., RecordId=...`,
   `CORE/Terrasoft.File/EntityFileLocator.cs:91`). Graph: Start -> OF1 -> PROBE -> end. Save in the designer, which
   also compiles the script; a builder save does not mark the package for compilation on this core (measured
   2026-09-25 during ENG-92711 Script task element).
3. Builder on 1.6.6.54: `modify-business-process` `addMapping {targetProcessParameter: "F", sourceElement: "<OF1
   name>", sourceElementParameter: "ObjectFiles.File"}`. A dotted element source is accepted there (D5 R-M1,
   basis=source).
4. The user opens the process in the designer and saves it once more, with no change, so that the script task is
   compiled against the final schema (the builder's save in 3 is invisible to Build).
5. Run once. Evidence: EV-3 `ErrorDescription` of `PROBE`. It is expected to carry the message (basis=inference). If it
   is cut short, read `Error.log`.

**Settles** R-M1 (D5). `F=` empty or a NullReferenceException keeps the refusal. A locator equal to the first or the
last source row would allow a documented "first row" rule instead. The owner then chooses between that rule and the
refusal (the recommendation stays: refuse).

**M14: the server-built element shape on 1.6.6.54.** On day 0, before the first PT cut is installed
(B.0): from PT's first cut the two mappings below go through binder rules P1 and P2, so this probe cannot wait for
PK-PT's review.
Builder on 1.6.6.54: `create-business-process
UsrFpM14Generic` in `Custom` with `OF1 = {type: userTask, userTaskName: ObjectFileProcessingUserTask}` and
`PF1 = {type: userTask, userTaskName: ProcessFileProcessingUserTask}`. Then `addMapping` `PF1.Files <- OF1.ObjectFiles`
and `PF1.Files.File <- OF1.ObjectFiles.File`. Do not run. Evidence (MD) against the predictions behind decisions
D20 (basis=source):

| Key | Predicted on 1.6.6.54 |
|---|---|
| `ConsiderTimeInFilter` | `GS2 "false"` (from the template's `GS8`) |
| nested item UIds | the TEMPLATE's (`1ddb6de7…`, `e5903ef0…`, `2d5e0436…`); `IL2` absent |
| `BK15` | top-level rows only |
| `BO2` (SerializeToDB) | absent |
| `CL2` / `BL7` | `FFFFFFFF` / equal to `J6` |

The user then opens PF1's card: does "Files" display `File` (the builder's bare caption) or the designer form
`[#<element>.<collection>:<item>#]`? Optional run: expect a run-time ItemNotFound on `SourceEntitySchemaUId`.
**Settles** the D20 pins: copy the template defaults, re-mint nested UIds on create, the nested DisplayValue form, and
`BO2`.

**M1 + M2: a single File into the Process variant.** The probe is the one named in decisions D29 and in
[test-plan](eng-92719-file-processing-element-test-plan.md) section 8, `UsrG1FilesBindingProbe`.
1. Fixtures (user, write): Contact `G1 Probe Source` with two uploaded `.txt` attachments (ContactFile rows of type
   File; **S** is one of them, M4 and V7 need both); Contact `G1 Probe Target`. If possible, S is uploaded by a
   different user than the one who runs the process, and has `Notes` or `Tag` set (for the piggyback in 5). Read both
   Ids and S with EV-0.
2. Designer-built `UsrG1FilesBindingProbe` in `Custom`:
   - process parameter `FileParam`: Add -> Other -> File, Direction Variable;
   - script task `SetFile` (Start leads here), fully qualified names:
     `Set<Terrasoft.File.Abstractions.IFileLocator>("FileParam", new Terrasoft.File.EntityFileLocator("ContactFile", new Guid("<S.Id>"))); return true;`
     (constructor `CORE/Terrasoft.File/EntityFileLocator.cs:35`);
   - Process file `CopyFile`: palette "Process file", then source "Process parameter" (this REPLACES the element with
     ProcessFileProcessingUserTask); "Files" = `[#FileParam#]`; "What object to save file to?" =
     `Contact (Contact attachment)` (ContactFile, link `ContactFile.Contact` `f442867d-73ca-49b3-a8ba-8a2566b1fc59`);
     record = `G1 Probe Target`;
   - Terminate.

   Save in the designer, which also compiles the script; a builder save does not mark the package for compilation on
   this core (measured 2026-09-25 during ENG-92711 Script task element).
3. Before the first run, confirm the nested-only shape with the UO method's read (B.0): on `CopyFile`,
   `Files.sourceValue.source` is 0 and `Files.itemProperties.first().sourceValue.source` is set (3, a mapping). Do not
   use describe for this check; it hides nested-only bindings (`PB/Describe/ProcessDescriber.cs:187-193`).
4. Run once. Evidence: EV-3 (`CopyFile` Completed), EV-5 on `G1 Probe Target`, EV-6.
5. Piggyback: compare `Notes`, `Tag` and `CreatedById` of the copy with S (attribute inheritance through
   `CopyAttributes`, `CORE/Terrasoft.File/File.cs:174-180`).
6. M2 (negative): disable `SetFile` by connecting Start straight to `CopyFile` (a schema write), save, and run once.
   Expected: `CopyFile` Error with a NullReferenceException in EV-3, and no new row in EV-5.

Alternative without C#: a caller with an Object element "Use in process" on `G1 Probe Source` and a multi-instance
sub-process per row, mapping the callee's File parameter from `ObjectFiles > File`; the callee holds the Process file
element with "Files" = that parameter. It also exercises the multi-instance item mapping, so a zero-copy result
would not say which half failed. The
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md)
runs that route as V8 after the PT cut, on the same fixtures.

**Settles** D18 (the single-file path: a single FileLocator source binds `Files.File` only) and the PT AC row "a File
binds the nested item only". One copy on the target with `TypeId` = File, `Name` = S's name and `Version` 1: the
single-file path ships. Zero copies with Completed, or an Error: the path is withdrawn, and the guide tells agents to
wrap one file in a FileCollection. M2 sets the guide sentence "a single File must be set before the element runs".

**M8 + M23 + M21 + M9: the SysFile read and write paths (SF only under Q3 = a).**

Fixture (user, write): a disposable app whose object `UsrFpDoc` has a Freedom UI form page with an attachments list.
This is the App Designer default for a new object page; Academy says SysFile "is used automatically for custom apps"
(https://academy.creatio.com/guides/dev/development-on-creatio-platform/back-end-development/api-for-file-management/entity-file-api;
documentation only). Create records `FP Doc A` (upload one `.txt` through the Freedom UI page) and `FP Doc B` (no
attachments). Read-only checks first:
- `SELECT Id, Name, RecordId, RecordSchemaName, TypeId FROM SysFile WHERE RecordId = '<FP Doc A Id>'`. **M8, stamp
  half:** `RecordSchemaName = 'UsrFpDoc'` and `TypeId = 529BC2F8-…`? The Object read always adds `Type = File`, and in
  SysFile mode also adds `RecordSchemaName` (`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:36-47`),
  so a row missing either is never read.
- M10's list must show `UsrFpDoc` as a plain entry (no dedicated `UsrFpDocFile`).
- `SELECT f.Id, f.Name, f.AccountId, f.TypeId FROM AccountFile f` (2 rows, measured). At least one must have type
  File; otherwise upload one `.txt` to an Account (write).
- `SELECT TOP 3 Id, Number FROM Invoice` (an Invoice is needed for M23c and M16).

Designer-built probes in `Custom`, run one at a time:

| Probe | Graph | Pass condition (EV-3 Completed, plus) |
|---|---|---|
| M23a `UsrFpM23aLegacyToSysFile` | Process file, source `Account (File and link of account)`, filter Account = the account with a File-type row, "Save to object attachments", target `UsrFpDoc`, record `FP Doc B` | EV-5: a new row with `RecordId = FP Doc B`, `RecordSchemaName = 'UsrFpDoc'`, `TypeId` = File, the source's `Name` and `Size`; the user sees it in FP Doc B's attachment list |
| M23b + M8 run + M21 `UsrFpM23bSysFileToLegacy` | Process file, source `UsrFpDoc` (SysFile), filter record = `FP Doc A`, sort `CreatedOn` descending (a SysFile column), "Save to object attachments", target `Contact (Contact attachment)`, record `G1 Probe Target` | EV-5: one new ContactFile copy. Zero copies with Completed means the SysFile read does not find a Freedom UI upload (M8 fails). Its saved MD cross-checks M21, whose capture is SC-1 E3 ([serialization-capture](eng-92719-file-processing-element-serialization-capture.md) section 12.4) |
| M23c `UsrFpM23cReportToSysFile` | Process file, source "Generated report", report `Invoice` (`e1f1a474-1a77-82f8-4bc7-b1da23699e13`), filter Id = one Invoice, "Save to object attachments", target `UsrFpDoc`, record `FP Doc B` | EV-5: a new `Invoice….docx` row with `RecordSchemaName = 'UsrFpDoc'` and `TypeId` = File; visible in the list |
| M23d `UsrFpM23dProcessToSysFile` | Process file `OF1` (Contact attachments of `G1 Probe Source`, "Use in process") -> Process file source "Process parameter", Files <- `OF1 > ObjectFiles` (both levels, designer) -> "Save to object attachments" `UsrFpDoc` `FP Doc B` | EV-5: two new rows, as in M23a |
| M9 `UsrFpM9SysFileSort` | Process file, source `Account address` (SysFile), any record scope, sort on a column that SysFile lacks (the designer offers `GPS E`, UO-1), "Use in process" | EV-3 Error naming the column (an ItemNotFoundException is predicted, basis=source); no fixture needed |

**Settles:** M8 and M23 decide which SysFile sources and targets SF lifts (D15); a failing case keeps its refusal.
M21 is SF's serialization capture (pr-split 12.4). M9 sets the severity of the "sort column not on SysFile" issue
and the wording of its F-F3 refusal (D16). Cleanup: the app and its package (user), the copied rows
(`execute-dataservice-batch`), and the probes.

**M25: the server's feature read.** Designer-built `UsrFpM25Feature` in `Custom`: one script task:
`throw new System.Exception("M25 a=" + Creatio.FeatureToggling.Features.GetIsEnabled("ProcessFeatures.UseSysFileInObjectFileProcessing") + " b=" + Creatio.FeatureToggling.Features.GetIsEnabled("UseSysFileInObjectFileProcessing") + " control=" + Creatio.FeatureToggling.Features.GetIsEnabled("ProcessFeatures.NoSuchFeatureXyz"));`.
Save in the designer, which compiles, then run once; read EV-3. If the string overload does not compile, record the
compiler message: that is a result too. The test-plan's variant, which writes the value into a parameter and then
an Activity title, works as well; the throw needs no record and no cleanup (Part D, row 35). The client read is
already measured as true (Part C). **Settles** whether the D15 "designer cannot display SysFile" notice can ship, and
under which code name. Without M25 the notice does not ship; nothing else depends on it.

### B.5 Writes that verify or tune wording (no code waits on them)

| ID | Recipe | Evidence | Settles |
|---|---|---|---|
| M4 | Designer-built `UsrFpM4FilesFlow`, any time: `OF1` Object "Use in process" on `G1 Probe Source` (2 attachments) -> process parameter `Docs` (Add -> Other -> Collection of records, nested Add -> Other -> File; `Docs` and `Docs.File` mapped from `OF1 > ObjectFiles` and `ObjectFiles > File`) -> `PF1` Process variant, "Files" from `Docs` (both levels), saving to an Account `FP M4 Target`. After the cuts the same chain is repeated with builder writes: V7 of the ENG-95984 File process parameter type test-plan (PT cut) and ST-04 of the test-plan (RP cut) | EV-3: `OF1`, `PF1` Completed; EV-5: 2 AccountFile copies with the source names; after a cut, describe shows both levels bound | D2/D5/D18 end to end; RP's stand proof |
| M16 | Designer-built `UsrFpM16Park`: Process file, source "Generated report", Invoice printable, filter Id = one Invoice, "Use in process" -> a Perform task -> end. Run; read EV-7 while the process is parked. Complete the Activity (`odata-update Activity StatusId = 4bdbb88f-58e6-df11-971b-001d60e938c6`, write); read EV-7 again. Sub-process case: child returns `ReportFiles` through an Out FileCollection; parent passes it into a Process variant saving to `InvoiceFile` of that Invoice | parked: ≥ 1 row; after completion: 0 rows for that instance; sub-process: the parent's PF1 Error or 0 files (record which) | D14/D17 temporary-file notice wording |
| M17 runs | M1's `UsrG1FilesBindingProbe` with the target record left EMPTY: (E4a) target `Contact (Contact attachment)`; (E4b) target `Account address` (a SysFile entry) | E4a: EV-3 Error naming the foreign key, no row; E4b: EV-3 Completed and a SysFile row with `RecordId = 00000000-…` (`SELECT COUNT(*) FROM SysFile WHERE RecordId = '00000000-0000-0000-0000-000000000000'` before and after) | the F-E9 notice severity |
| M18 | After PK-RP: a Report element with a constant `fileNameSuffix`, built in en-US, run by a user whose profile culture differs (changing a user's culture is an account-settings write: the user does it, or names a second user) | the generated file's `Name` in `InvoiceFile` | the D17 culture note |
| M19 | Builder on 1.6.6.54, on day 0 (B.0), `UsrFpM19CollectionFilter`: `RD1` Read data Contact, collection, 3 records -> `RD2` Read data Contact, first record, filter `Id = RD1.ResultCompositeObjectList`. **Use Read data as the consumer, never Modify or Delete data**: a mis-evaluated filter there would touch every record | EV-3: RD2's outcome and error | urgency of "Refuse collection parameters as filter values" |
| M20 | Builder on 1.6.6.54, on day 0 (B.0; from OA's cut the block is known): `setElement {name: "RD1", caption: "M20", fileProcessing: {source: "attachments"}}` on M19's process | describe: the caption changed and no error. Check first that this clio forwards the unknown block to the package (`get-tool-contract`); if clio drops it, record that this measures the client | the D26 premise (silent drop, hence the floor raise) |
| M22 | After every cut: the user opens the builder-made File / FileCollection parameters and Process file elements in the designer (including a dedicated-storage Object element, the H-G3-1 case, and a builder-written scope), closes them, then saves with no change | MD before and after: identical, apart from known designer normalisations | parity confidence; the H-G3-1 guide warning |
| M24 | Builder on 1.6.6.54, on day 0 (B.0), on M19's process: (a) `setElement {name: "RD1", readData: {sort: [{column: "Name", direction: "asc"}]}}` (an array into the object member); (b) an object sent into the array member `readData.columns` (`string[]`, `PB/Contracts/ProcessDescriptorContracts.cs:1260-1261`; `sort` is an object, `:1285-1286`) | for each: a loud fault, a null sort, or something else; MD for what was stored | the `attachments.sort` guide sentence |
| M26 | Designer-built by the user (it needs a File parameter until PT lands); the full method is under the banner of [traps](eng-92719-file-processing-element-traps.md) (T-19). `OF1` Object attachments of a disposable Contact with 2 uploaded attachments, "Save to object attachments" onto a disposable Account; then a multi-instance sub-process with `InputRecordCollection.Doc <- OF1.ObjectFiles.File` (the parent bound too) whose callee script task writes `Doc`'s `EntitySchemaName` and `RecordId` into the title of an Activity it adds | read-only reads of ContactFile, AccountFile and Activity after the run: the 2 titles carry `ContactFile` and the Ids of the source ContactFile rows, and the 2 new AccountFile Ids are not among them; cleanup of the activities, copies and fixtures through `execute-dataservice-batch` | the OA.12 guide sentence and the CL-OA knowledge record (T-19) |

### B.6 Proposed during research, not scheduled

| Item | Why not |
|---|---|
| M5: an outer-only `Files` binding gives an NRE | refused by F-P1 anyway; needs hand-edited metadata |
| A record filter `Id = X` written on the wrong root | refused by F-F1 anyway |
| A lookup column as the report's file-name column | refused by F-R2 anyway |
| A Word printable with an empty template | needs a new printable in Report setup; only if the owner wants it |
| A bare generic-route Object element at run time | refused by F-E1 (D13) from OA; an optional run inside M14 |
| Send email attachment slots (shapes and run outcomes); a designer capture of the Creatio.ai call | belong to ENG-95985 Send email attachments; the Creatio.ai call is out of scope (Q5) |
| "Attachments of the records matching a condition" in SysFile mode | out of v1 (D16) |
| "Read first 0 records" | refused outside 1..5000 (F-F3) |

---

## C. What was measured already

All on 2026-10-01 unless noted, all read-only. "How" names the read that produced each value, so that it can be
repeated; the raw outputs of the research passes are not attached.

### C.1 Versions and identity

| Fact | Value | How |
|---|---|---|
| Core | 10.1.37.0, .NET Framework 4.8.9337.0, MSSql, primary culture en-US | `describe-environment` |
| CrtProcessDesigner | 7.8.0 | ESQ `SysPackage` |
| CrtProcessBuilder | 1.6.6.54, byte-identical to main `3f4cce50` | `list-packages`; `diff -rq` of the stand's package folder against the worktree |
| Designer client schemas (three property pages, their base page, `ProcessFilesControlSchema`, `EntitySchemaDesignerUtilities`, `ProcessSchemaPropertiesPage`) | byte-identical to PackageStore `CrtProcessDesigner/branches/7.8.0`, one layer each | `get-client-unit-schema` + `diff` |
| Palette rows `SysProcessUserTask` | all three schema UIds, caption "File Processing", Position 0 (Object), -1 (Process), -1 (Report) | ESQ |
| Palette and replace menu | `getExcludedMenuItems()` = `["ReportFileProcessingUserTask","ProcessFileProcessingUserTask"]`; the palette holds only `objectFileProcessingUserTask`; Process/Report have `showInReplaceMenu = false`; `UseProcessDiagramComponent` on | live designer JavaScript in a logged-in tab |
| Add-parameter types | `DataValueTypeConfig` has 33 keys; no BLOB or FILE key; FILE_LOCATOR (41) and COMPOSITE_OBJECT_LIST (39) present, captions "File" and "Collection of records" | live designer JavaScript |

### C.2 Features, settings and data

| Fact | Value | How |
|---|---|---|
| `EnableReportFileProcessingUserTask` | Feature row, DefaultState 1, All employees = 1, All external users = 1; client `getIsEnabled` = true | ESQ on `Feature` / `AdminUnitFeatureState`; `Terrasoft.Features.getIsEnabled` in a classic designer tab |
| `ProcessFeatures.UseSysFileInObjectFileProcessing` | no Feature row (571 rows read); client `getIsEnabled` = true; `cachedFeatures` state 1; a control code reads false | ESQ; Chrome JS on a classic page (on the Freedom `/0/Shell/` page `Terrasoft.Features` is undefined, so read it on a classic page) |
| `ProcessParameterCollections`, `UseProcessEmailAttachments` | on | ESQ / Chrome JS |
| `SaveWordReportAsRecordAttachment` | false | `list-sys-settings` |
| SysFile | 6,913 rows; 0 with `RecordSchemaName <> 'ConfActivityLog'` (that predicate also excludes NULLs); columns `RecordId`, `RecordSchemaName`, `SysFileStorage` present | ESQ count; `get-entity-schema-properties` |
| Account attachments | `SysFile` with `RecordSchemaName = 'Account'`: 0; `AccountFile`: 2 rows | ESQ |
| Printables | 5 `SysModuleReport` rows, all MS Word, none with `ConvertInPDF`: Contract `ac39a15e…`, DNS Requirements `67274fc0…`, DNS Specifications `56428857…`, Invoice `e1f1a474…`, Quotation `8d56963f…`; none for Account or Contact; `SysModuleReportType`: MS Word, DevExpress, FastReport | `list-printables`; `clio dataservice` select on `SysModuleReport` / `SysModuleReportType` |
| Element run history | 0 `SysProcessElementLog` rows of type user task whose caption contains "file" or "report". This does not prove that no file element ever ran, because captions vary | ESQ |
| Corpus test packages | ProcessTests, CopilotAutoTest, AutoTestUC, DepTest_Level3 are NOT installed, so no Object or Process-variant example can be described on the stand | ESQ `SysPackage` |

### C.3 Shipped processes with the element, and describe today

| Fact | Value | How |
|---|---|---|
| Installed | PrintInvoiceReport (`de5169a4…`), PrintQuotationReport (`7f98d916…`), PrintContractsReport (`d3470c16…`), GenerateDNSRecordsSpecification (`7c0f82c3…`): all Report variant, `ResultActionType = 0`, targets InvoiceFile / OpportunityFile / DNSGuideFile | ESQ `VwProcessLib` + `describe-business-process` |
| Consumers | no downstream element maps `ReportFiles` or `CreatedObjectFileIds`; a Modify data element re-finds the file by `Name contains` + `CreatedOn = CurrentHour` and sets `Tag` | describe |
| Describe today (PrintInvoiceReport, PrintQuotationReport, GenerateDNSRecordsSpecification x2) | `buildType: "usertask"`, `userTaskName: "ReportFileProcessingUserTask"`, every typed block null, `filter` decoded from `DataSourceFilters`, `ReportId` / `TargetEntitySchemaUId` raw GUIDs; `ConsiderTimeInFilter` and `TargetDataEntitySchemaUId` absent from these 2021-era elements | `describe-business-process` (re-measured the same day, correlation `16307feb8a4d`) |
| Correction to that reading | the missing `itemProperties` / `isOutput` / `isRequired` came from the session's stale MCP clio client, not from the package; a current clio shows `itemProperties` (File) under `ReportFiles` | the same describe through a current clio |
| PrintContractsReport (M13) | described 2026-10-02, see C.6 | `describe-business-process`, correlation `7534ca075c72` |

### C.4 Designer observations UO-1..UO-4

Measured 2026-10-01 in the classic designer on a NEW UNSAVED process. UO-1 is the user's screenshot. Its resolution
and UO-2..UO-4 were measured by the main research session in an already logged-in tab, read through
`Terrasoft.ProcessSchemaManager.items[0].instance.flowElements`; the tab was closed without saving.

| Id | Observation |
|---|---|
| UO-1 | "Process file", source "Object attachments", object `Account address`, read first 50, sort `GPS E` ascending. "What to do with file?" showed only "Use in process", but **that was the combobox filtering by its own text**: with the text selected, both actions are offered, for legacy and SysFile entries alike. Not a business rule. A record-object sort column (`GPS E`) is offered in SysFile mode (see M9). |
| UO-2 | Legacy file details are listed as `<Entity> (<file entity caption>)`: `Account (File and link of account)`, `Contact (Contact attachment)`, `Activity (File and link of activity)`. Every other object by its plain caption (a SysFile entry). |
| UO-3 | SysFile mode (`Account address`, defaults untouched): `SourceEntitySchemaUId` = SysFile `70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a` (ConstValue), `SourceDataEntitySchemaUId` = `8ab0fe8a-0340-41ac-8b09-b11f65dd83da` (ConstValue), `DataSourceFilters` = an empty FilterGroup, `RecordsToRead` 50, `ResultActionType` 1, `ConsiderTimeInFilter` Script `true`; the target fields, `OrderByInfo` and `ConnectedObject*` unset. |
| UO-4 | Legacy mode (source `Account (File…)`, "Save to object attachments", target `Contact (Contact attachment)`, record left empty): `SourceEntitySchemaUId` = AccountFile `149d2eaf-cbd2-49fa-b565-637748ff823c`, `TargetEntitySchemaUId` = ContactFile `e9eafee9-c4e4-4793-ad0a-003bd2c6a9b4`, `ConnectedObjectColumnUId` = `ContactFile.Contact` `f442867d-73ca-49b3-a8ba-8a2566b1fc59`, `ResultActionType` 0, both `*DataEntitySchemaUId` CLEARED (None); `ConnectedObjectId` unset. Changing the source object raised no confirmation, because the old value was empty: `onEntitySchemaChange` asks only when the old value is set (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:794-823`, the check at `:807-810`). |

### C.6 Read-only measurements of 2026-10-02

Run by the main session on CrtProcessBuilder 1.6.6.54, one call at a time. Designer reads used the UO method in
a NEW UNSAVED process and, for M11 (a), the shipped PrintQuotationReport opened without saving; both tabs were
closed without saving, and `SysSchema` shows 0 process schemas created since 2026-10-01. On an existing process the
open schema is not in `Terrasoft.ProcessSchemaManager.items`; it is
`Ext.ComponentMgr.all.map["schema-designer-mainCt"].model.changedValues.Schema` (the UO sketch in B.0 needs that path
there).

| Id | Result | How |
|---|---|---|
| M10 | Matches the prediction. 1,218 objects are offered. Legacy file details are listed as `<Entity> (<file object caption>)` (about 40 of them), including `Lead (Lead attachment)` (the `FileLead` case). `DNS guide file`, `Uploaded file` (SysFile itself), `Process files`, `Feed uploaded file`, `Attached file`, `Message file`, `File and link of mailbox synchronization settings` and `File and object link of OAuth 2.0 application` are plain entries: file objects that no entity claims by name are offered as ordinary objects | full list read from the opened combobox's DOM with the field text cleared |
| M11 (a) | Not reproduced. PrintQuotationReport's report element: before opening the panel `TargetDataEntitySchemaUId` had no value; after opening and closing the panel unchanged it holds source None, value null, not OpportunityFile `4135a9ba…`. The other target parameters (`TargetEntitySchemaUId` = OpportunityFile, `ConnectedObjectColumnUId`, `ConnectedObjectId`, `IsSeparateReports` true, `ResultActionType` 0) are unchanged. The panel shows "Generate separate report for each record" checked and disabled for the Word printable Quotation | designer JavaScript on the open schema |
| M11 (b) | Not reproduced in memory. Object element, source `Account (File and link of account)`, "Use in process": `SourceDataEntitySchemaUId` is None/null after the first close and still None/null after reopening and closing the panel unchanged. Per the recipe this is "not reproduced in memory", not "refuted"; step (c), a save and reload, would settle it | designer JavaScript |
| M13 | `ConnectedObjectId` reads back as type `Guid` with `referenceSchema: Contract`, source Script `[#ContractId#]`; the stored metadata types it Lookup. Describe therefore reports the CONVERGED type while keeping the snapshot's reference schema: D19 fixtures carry `Guid` plus `referenceSchema`. `TargetDataEntitySchemaUId` and `ConsiderTimeInFilter` are absent (not stored on this element) | `describe-business-process PrintContractsReport` through a current clio (`get-tool-contract` checked first), correlation `7534ca075c72` |
| M15 | No FastReport package (`list-packages` filter "Fast": 0; "Report": `Reports`, `WordReporting`); `SysModuleReport` 5 rows, 0 of type FastReport. Q18 recommendation B (refuse FastReport) stands | `list-packages`; `execute-sql-script` SELECT |
| M17-Q7 | `ContactFile.ContactId` -> Contact and `AccountFile.AccountId` -> Account are foreign keys; `SysFile` has none on `RecordId` (only `LockedById`, `FileGroupId`, `TypeId`); 0 rows with the empty Id in Contact and in Account. So an empty `ConnectedObjectId` is predicted to FAIL at the database for a legacy target and to ORPHAN the file for a SysFile target (the latter still needs the M17 runs) | `execute-sql-script` SELECT on `sys.foreign_keys` |

Still open among the read-only rows: **M7** (the SysFile scope filter JSON; it needs a lookup condition built in the
filter editor and gates only PK-SF) and **M12** (v2, gates nothing).

### C.7 Day-0 write probes of 2026-10-02 (CrtProcessBuilder 1.6.6.54)

Built by the main session in package `Custom`, one call at a time, with the user's go-ahead. **All of it was deleted on
2026-10-02 with the user's confirmation** (the eight processes through `clio delete-schema --remote`, 7-50 s each;
the contact and its two files through one SQL DELETE by Id, whose cascades removed its `ContactSubscription` and
`SysContactRight` rows; a recount found 0 rows left). The probe processes were:
`UsrFpM14Generic` (`37788ae3…`), `UsrFpM14CreateOuter` (`70da289a…`), `UsrFpM3Callee` (`c50c6050…`), `UsrFpM3Control`
(`d63a1617…`), `UsrFpM3ItemOnly` (`cee1f532…`), `UsrFpM3MirrorProbe` (`9cb856b5…`), `UsrFpM19CollectionFilter`
(`ccb453a0…`), `UsrFpM6FlatFile` (`661ae7d4…`). For M6 one fixture was written: the contact `G1 Probe Source`
(`ffe9dd78…`, created through `odata-create`) with two small `.txt` attachments uploaded on its Freedom UI page
(`ContactFile` `ccfdece7…`, `1cac5fed…`). Everything else left process log rows only.

| Id | Result |
|---|---|
| NEW: create-path NRE | `create-business-process` with the generic route `{type: userTask, userTaskName: ObjectFileProcessingUserTask / ProcessFileProcessingUserTask}` plus `mappings[]` failed with a bare **"Object reference not set to an instance of an object. Nothing was left behind."** (correlation `d43521be15cc`). Isolated: the same create with only the OUTER mapping `PF1.Files <- OF1.ObjectFiles` succeeds (`57c67716b563`); the NESTED mapping `PF1.Files.File <- OF1.ObjectFiles.File` is what throws on create, while the very same mapping through `modify-business-process addMapping` succeeds (`9ebc0ca62318`). So on 1.6.6.54 a dotted nested-item mapping in the create path NREs for these user tasks (multi-instance dotted mappings on create do work, see M3 control). The two-level binder of ENG-95984 File process parameter type must cover the create path, with a regression test |
| M14 | Measured on `UsrFpM14Generic` (created bare, both mappings added by modify). `ConsiderTimeInFilter`: Script, `GS2 "false"`, `GS8 "false"` (template copy, as predicted). `RecordsToRead`: ConstValue `50`. Nested item UIds are the TEMPLATE's: `ObjectFiles.File` `1ddb6de7…`, `Files.File` `e5903ef0…`, PF1 `ObjectFiles.ObjectFile` `2d5e0436…` (as predicted: the builder does not re-mint, so D20's re-mint is real work). **`IL2` is PRESENT** on every element parameter and nested item (= the element UId), contrary to the prediction "absent". `BK15`: 21 rows for 21 top-level element parameters, none for nested items (as predicted). `BO2` absent, `BO3` true, `CL2` `FFFFFFFF`, `BL7` = `J6`, size `69;55` (as predicted). `ResultActionType` unset (`L8 {}`) on both elements. The two-level mapping is stored as `Files` <- Script `[#…[Element:{OF1}].[Parameter:{ObjectFiles}]#]` and `Files.File` <- Script `[#…[Element:{OF1}].[Parameter:{1ddb6de7…}]#]` (the nested source addressed by its own UId). **Card display** (read in the designer without saving): PF1's "What is the source of the file?" shows `Process parameter` and its "Files" field shows `[#OF1 object files.Collection of files:File#]`, so the designer resolves the server-built two-level mapping and renders it like a designer-built one |
| M3 (premise) | `UsrFpM3MirrorProbe`'s mirrored parameter `P` (`typeFromElement RD1.ResultCompositeObjectList`): outer `L8` = Script <- `RD1.ResultCompositeObjectList`, Tag `RD1.ResultCompositeObjectList`; its item `Name` (Tag = column UId) has **no source** (`L8 {}`). The H-1 premise holds |
| M3 | **H-1 refuted.** The designer step was done in the designer by the main session (Claude in Chrome): `SP1`'s item `Name` <- `P > Full name` (`[#Full name#]`), then SAVE; the saved metadata was read back before the run. `UsrFpM3MirrorProbe` ran `completed`: **3** callee instances, all `M3 name set` (callee totals went from 1 empty / 3 set to 1 empty / 6 set, correlation `484118f1ce57`). The mirror's item having no source does not matter for reading: the outer Script mapping copies the whole collection value, items included. Per B.4 M3 this is the "3 set" outcome: **MH is not created**, contingency X4 applies (binding the items anyway is one parity commit in PK-PT), Q7 is moot, and the PR count is **14** |
| M3 control | `UsrFpM3Control` (both levels from element paths) ran `completed`: **3** callee instances, all `M3 name set` |
| M3b | `UsrFpM3ItemOnly` (only `InputRecordCollection.Name <- RD1.ResultCompositeObjectList.Name`) ran `completed`: **1** callee instance, `M3 name empty`. Same as the 1.6.6.22 measurement in the guidance; after the PT cut the same request must give 3 |
| M19 | A collection parameter as a filter value (`RD2` filter `Id = RD1.ResultCompositeObjectList`) is **accepted at build**; the run fails at `RD2` with `System.ArgumentException: No mapping exists from object type Terrasoft.Common.CompositeObjectList… to a known managed provider native type` (SQL parameter binding). Loud at run time, silent at build: the "Refuse collection parameters as filter values" Sub-task is justified |
| M20 | `setElement {useBackgroundMode: true, fileProcessing: {source: "attachments"}}` on 1.6.6.54 through the current clio: success, **no warning**, the unknown block is dropped silently end to end (`f63158412242`). The D26 premise (floor raise) holds |
| M24 | (a) an array into the object member `readData.sort` is refused with "'sort' requires a 'column'" (`efb48ac50196`); (b) an object into the array member `readData.columns` is refused with "mode 'collection' requires explicit 'columns'" (`8bc67379307b`). Both wrong shapes are NULLED by the binder and only a later rule refuses, with a text that names the wrong cause; nothing was saved. The `attachments.sort` guide sentence: a wrong JSON shape is not reported as such |
| NEW: schema UId as a Lookup constant | `modify addMapping {elementName: OF1, elementParameter: SourceEntitySchemaUId, value: "e9eafee9-…"}` (the ContactFile schema UId, exactly what the designer stores, UO-4) is **refused**: "no SysSchema record has this id" (`48118164cdf1`). The builder's Lookup-constant check looks the value up by `SysSchema.Id`, while these parameters hold a schema **UId**. So on 1.6.6.54 the generic route cannot write a correct `SourceEntitySchemaUId` / `TargetEntitySchemaUId` / `*DataEntitySchemaUId` (a `SysSchema.Id` would pass the check and reference the wrong thing at run time); for M6 the object was picked in the designer. The `fileProcessing` binder must write these through its own path (schema name -> UId), not through the generic Lookup-constant mapping ([traps](eng-92719-file-processing-element-traps.md) T-54, predicted from source and now measured) |
| NEW: dotted `typeFromElementParameter` | `parameters[{name: F, typeFromElement: OF1, typeFromElementParameter: "ObjectFiles.File"}]` creates `F` with the right type (`L1` = FileLocator `a33c9252…`) but **no source** (`L8 {}`), while the outer form (M3's `P`) gets the Script mapping. The dotted form types the parameter only; the value needs an explicit `addMapping` (`3cf363739721`) |
| NEW: Freedom UI upload lands in the legacy table | The two files uploaded on the Contact Freedom UI page landed in `ContactFile` (`TypeId` File), none in `SysFile`, on this stand (`UseSysFileInObjectFileProcessing` off). Consistent with the per-entity storage rule (Contact keeps its legacy `ContactFile`) |
| NEW: transient timeout | One `modify` carrying `setFilter` and `addMapping` together timed out and wrote nothing (`ModifiedOn` unchanged); the same two operations sent one per call both applied (`74bc25618f3b`, `3cf363739721`). Not reproduced; recorded only so a later timeout is not read as a builder defect without a retry |
| M6 | **R-M1 stays a refusal.** `UsrFpM6FlatFile` was saved and published in the designer by the main session (the publish compiled `PROBE`; its message window listed only warnings from other packages), then run once: instance `c3c86025…`, OF1 `Completed`, PROBE `Error` with **`System.Exception: M6 F=`**, so `F` was empty: no NullReferenceException, no first or last row. That the source was not empty is proven by the instance data: `SysProcessElementData.PropertiesData` of OF1 holds `ObjectFiles` with **two** `EntityFileLocator`s, `ContactFile` `ccfdece7…` and `1cac5fed…`, `CreatedObjectFileIds` empty, `ResultActionType` 1. F was mapped Script <- `[Element:{OF1}].[Parameter:{1ddb6de7…}]` (the item `ObjectFiles.File`). So a flat File taken from a collection item outside a row context reads **null, silently**: the builder must refuse it (F-M2) |
| NEW: element output evidence | `SysProcessElementData.PropertiesData` (varbinary holding JSON; `CONVERT(varchar(max), …)` reads it) keeps each executed element's parameter values per instance, collections included, with each locator's `_entitySchemaName` and `_recordId`. It is a runtime oracle the plan did not have: it shows what an element produced without a second probe element (B.1 EV-8) |

### C.5 Corpus and repository counts (not the stand)

| Fact | Value |
|---|---|
| Shipped elements | 16 processes / 30 elements: Object 14 (7 with action 0, 7 with 1), Process 2 (both 0), Report 14 (12 with 0, 2 with 1). Product use: 4 processes / 5 elements, all Report + SaveToFiles + MS Word; Object and Process variants occur only in test packages |
| Diagram | BL7 = J6 on 30/30; size 69;55; lane container |
| Storage | 0 of 30 elements use SysFile; Object sources: ContactFile x12, CreatioAIIntentFile x2 |
| Parameter types | 50 FileLocator `L1` occurrences in 21 files; 0 process or user-task parameters of Binary or File (BLOB); the binary UIds occur only as entity-column types (`S2`) |
| Collections | 11/11 file-collection bindings bind both levels; 0/61 bound top-level collections bind the outer level only; 0/133 process-level collections and 0/614 items carry a Tag; process-level directions 85 Out / 30 Variable / 12 In / 6 Internal of 133 (user-task schemas: 17 more) |
| Drift | 21/30 elements lack `TargetDataEntitySchemaUId`, 10/30 lack `ConsiderTimeInFilter`; 7 type `ConnectedObjectId` as Lookup where the schema says Guid |
| Delivery (repositories) | approvals dismissed on push on crt-process-builder `main` (ruleset 82837); no Copilot review on drafts; package -> clio -> knowledge tails of about 30-71 minutes; two of our package PRs in review at once stamped 1.6.6.42 and 1.6.6.49 twice ([pr-split](eng-92719-file-processing-element-pr-split.md) section 3) |

---

## D. Contradictions found during the research, and their resolution

"Where" names the place the earlier claim was made: the Jira text, the earlier refinement, a research pass of
2026-10-01, or a sibling document. "Open" means the resolution still rests on a source trace or on documentation;
the cell names the measurement that settles it, or says why none is scheduled.

### D.1 Platform facts

| # | Earlier claim (where) | Counter-evidence | Resolution | Basis | Open |
|---|---|---|---|---|---|
| 1 | "addParameter accepts the Binary / File data type" (Jira AC of PT) | the name `File` resolves to BLOB BA40CFC5 (`CORE/Terrasoft.Core/DataValueTypeManager.cs:341-349`); Binary B7342B7A has no flow-engine store (`CORE/Terrasoft.Core/Process/FlowEngineStateService.cs:139-148, 409-429`); 0 corpus parameters | one file = FileLocator A33C9252; friendly names map to it; Binary/BLOB stay refused (D1); AC rewritten (Q1) | source + corpus | no |
| 2 | Binary-refusal tests "must be flipped in the rebundle PR" (delivery research) | Binary stays refused | `CLIO/clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs:2178-2205` and `PBT/ProcessParameterServiceTests.cs:144-150` stay green; the `:1671-1678` probe moves to `Image` or `Color`, so it keeps testing the generic message (decisions X7, D1) | source | no |
| 3 | "CI would catch a broken Binary refusal" (a PR-split proposal) | the process-designer e2e category is excluded from TeamCity (`CLIO/clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:46`, `baseFilter`) | the PBT pin is the only automated guard ([pr-split](eng-92719-file-processing-element-pr-split.md) section 3, row 7) | measured | no |
| 4 | "An outer-only binding = a silent zero copy" (earlier refinement) | rows are rebuilt by target item names, and an unbound item is null; the Process variant dereferences it (`PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:72-79`) | a NullReferenceException is predicted; the shape is refused anyway (F-P1) | source | yes: none scheduled (M5 skipped, the refusal covers it) |
| 5 | "A nested-only `Files` binding is not read at all: the reader filters by the root's `GS5`" (builder-serialization research) | the provenance filter is only in the sub-process / iteration path (`CORE/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:718-753`, callers `ProcessComponentSet.cs:1361-1366, 1518-1521`); an ordinary element reads all its parameters (`ProcessComponentSet.cs:502-540`) | nested-only = one row through `TryGetListValue` (`CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:444-453`) (decisions X1) | source | **yes (M1)** |
| 6 | The one-row path is `HasCollectionWithOneElement` (runtime research) | that flag is set only for Script-sourced nested items and is read only by the multi-instance iteration count (`CORE/Terrasoft.Core/Process/ProcessInstanceCollectionParametersDataReader.cs:64-70`) | the path is `TryGetListValue`; the flag explains the "one iteration" behaviour of item-only multi-instance mappings (M3b) | source | **yes (M1, M3b)** |
| 7 | "Zero copies, no error" predicted for a nested-only binding (builder-serialization research) | row 5 | the prediction is "one row"; M1 decides | source | **yes (M1)** |
| 8 | "A missing ConnectedObjectColumnUId saves files linked to no record" (earlier refinement) | `CopyAttributes` copies only attributes the target has not set (`CORE/Terrasoft.File/File.cs:174-180`) | true for a cross-schema copy; a same-schema copy lands on the SOURCE record as a duplicate. Hence no `linkColumn: "none"` (decisions X4, X5; Q17) | source | yes: none scheduled (the D15 storage refusals cover a missing link column) |
| 9 | "ResultActionType 1 is used by the variants that PRODUCE the collection" (earlier refinement) | every variant fills its outputs for both values; 0 also saves (`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:60-87`) | AC rewritten (Q1, D14) | source + corpus | no |
| 10 | "Attachment storage changed in the current version, and writes follow the active mode" (earlier refinement) | the feature class exists at least since 8.1.5 (TSBpm history); the designer chooses storage per object, not globally (UO-2..UO-4) | per-object resolver (D15) | source + measured | no |
| 11 | The package comment "the designer creates a collection as OUT" (`PB/Parameters/ProcessParameterService.cs:103-104`) | plain Add writes Variable (`PD/ProcessSchemaPropertiesPage/ProcessSchemaPropertiesPage.js:1204-1236`); Out comes only from "Create from element" | the comment is corrected in PK-PT; Out stays as a package rule (Q11) | source | no |
| 12 | The mirror comment "the runtime keys values by the SOURCE items' names" (`PB/Parameters/ProcessParameterService.cs:440-447`, comment K3) | rows are rebuilt by TARGET item names (`CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:441-488`) | hypothesis H-1 (Q7); M3 refuted it on 2026-10-02: the outer Script mapping copies the whole collection value, items included, so no mirrored row reads null (C.7) | source + measured | no (M3 done) |
| 13 | "The source switch replaces the element after an 'all settings lost' warning" (earlier refinement) | the dialog appears only when the element is configured, and the switch is refused when the element cannot be removed (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:233-237, 278-310`) | as corrected | source | no |
| 14 | UO-4: changing the source object raised no confirmation, although the page has `ChangeReferenceSchemaWarningMessage` | `onEntitySchemaChange` asks only when the old value is set (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:794-823`) | consistent; not a bug | source + measured | no |
| 15 | "Position -1 hides the Report and Process entries from the palette" (stand research, a hypothesis) | the live designer excludes them through `getExcludedMenuItems()` | the exclusion list is the mechanism (C.1) | measured | no |
| 16 | "Academy says the Process variant has 'What to do with file?'" (earlier refinement) | Academy does not say so; the page forces SaveToFiles and drops the control from validation (`PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:174-176, 183-187`) | the page forces 0 | source + documentation | no |

### D.2 Stand and designer readings

| # | Earlier claim (where) | Counter-evidence | Resolution | Basis | Open |
|---|---|---|---|---|---|
| 17 | UO-1: only "Use in process" is offered for `Account address` (the user's screenshot) | with the field text selected, both options are listed | a combobox text-filter artefact (C.4) | measured | no |
| 18 | "Add files" on the Process-variant page creates a COMPOSITE_OBJECT_LIST IN parameter (stand research) | the page binds the nested `Files.File` of the schema's own `Files` input (`PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:44-57, 73-79`); the dynamic slots belong to Send email and the Creatio.ai call | decisions X6 | source | no |
| 19 | Describe "shows no item structure for out collections" and "lists only the parameters persisted in the element" (stand research) | the stale session clio stripped `itemProperties`; describe filters by provenance, a root value set in this schema (`PB/Describe/ProcessDescriber.cs:187-193`), and is converged only for metadata instances | describe is read with a current clio; PT lists nested-only bindings (D8) | measured + source | **yes (M13**, for the snapshot question) |
| 20 | "The designer is unreachable: login required" (stand research) | later passes read the designer in an already logged-in tab (UO-2..UO-4; the feature reads of C.2) | the block applied to a fresh agent tab only; nobody types credentials for an agent | measured | no |
| 21 | A describe capture cited with correlation `9e931c583c33` (builder research) | the file carries `1ba2aef74dce`; the re-measure gave `16307feb8a4d`, same content | citation corrected (C.3) | measured | no |
| 22 | "Support reported an empty collection with SysFile attachments" (Academy research) | the asker reported it; support acknowledged it and gave a `SourceDataEntitySchemaUId` workaround; an August 2026 comment asks whether it was fixed (https://community.creatio.com/questions/process-file-business-process-element-not-working-sysfile) | relevant to M8, not evidence of current behaviour | documentation | **yes (M8)** |

### D.3 Corpus counts

| # | Earlier claim (where) | Correct value | Basis |
|---|---|---|---|
| 23 | "Only FileParameterProcess has process-level file parameters" (corpus research) | three processes: also `MarkProcessesToCancel` (ProcessLibrary) and `CRM60006SP` (ProcessTests) | corpus |
| 24 | "Container-only file bindings ≈ 2" (corpus research) | 1, and it is an Id collection with an empty target item list; 11/11 file-collection bindings bind both levels | corpus |
| 25 | "30/30 elements reference a file object" (corpus research) | 28/30: two Report "Use in process" elements reference none | corpus |
| 26 | "Read data occurs in 9 of the 16 processes" (corpus research) | 10 | corpus |

### D.4 Design and delivery documents

| # | Earlier position (where) | What contradicted it | Resolution |
|---|---|---|---|
| 27 | Record scope as `filter.recordId` / `filter.record` (filter research) | clio's filter DTOs carry no extension bag, so a new filter member is dropped on describe (`CLIO/docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`) | the scope moved into the block, `attachments.recordId` / `report.recordId` (decisions review B1; Q16) |
| 28 | `linkColumn: "none"` with a warning (storage research) | row 8 | not offered in v1 (Q17) |
| 29 | A retarget keeps a filter whose root name matches (`DataSourceFilterValue.ClearIfForeign`) (decisions, first draft) | in SysFile mode the root is SysFile for every object, so a foreign `RecordId` scope survives | any storage-pair change clears filter, scope and sort (decisions review A1, D12) |
| 30 | "Read-only members are accepted only when equal" (decisions, first draft) | captions, issues and repaired states legitimately differ, so correct resubmissions would be refused | two kinds: identity CHECK vs describe-only (decisions review B2) |
| 31 | Two shipped guide passages: an item-only multi-instance mapping "runs ONE iteration" (`KB/guidance/mcp/guides/processes/sub-process.md:133-137`, measured on 1.6.6.22; `sub-process-when.md:49-51`) | P1 changes that behaviour from PT's cut | both rewritten with a version gate in KB-PT; M3b gives the baseline (decisions review B13) |
| 32 | H-1 fixed inside PT (decisions D9, first version) | a shipped defect, small, revertible on its own (pr-split O2) | Sub-task MH first, if M3 confirms (Q7). M3 refuted H-1 on 2026-10-02: MH not created; binding the items is one parity commit in PK-PT (X4) |
| 33 | SysFile conditional inside OA (decisions D15 and D27, first version) | five pre-code measurements, three of them user-built, and no runtime evidence (pr-split O1) | Sub-task SF (Q3) |
| 34 | Fixed versions 1.6.6.55 / .56 / .57 (decisions D26, first version), and "floor = the first cut" (a review proposal) | collisions under one number happened twice; a review round can add a write member after the first cut | numbers are claimed per cut, and the floor is the FINAL cut of each clio PR ([pr-split](eng-92719-file-processing-element-pr-split.md) section 3 row 3, section 9.1) |
| 35 | M3 in the [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) W0 and [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) 8.1: the builder writes `InputRecordCollection.Name <- P.Name`, and the callee writes `Name` into an Activity title. M25 in [test-plan](eng-92719-file-processing-element-test-plan.md) section 8 writes the feature value into an Activity title | on 1.6.6.54 `ResolveProcessParameter` resolves a process parameter by its whole name (`PB/Mappings/ProcessMappingService.cs:425-435`, callers `:221, :280`), so `P.Name` is refused as "not found" | M3 here maps the item in one designer step. M3 and M25 here record their result in branch captions and an error text, which writes no business record; the Activity-title form stays valid where a record is acceptable (B.4). Applied: the ENG-95984 File process parameter type plan W0, its test-plan 8.1 and test-plan section 8 carry this recipe. |
| 36 | M22 listed as "no write" (decisions D29; [test-plan](eng-92719-file-processing-element-test-plan.md) section 8) | a no-op designer save is a schema write; the [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) V5 marks the same save "yes" | M22 is a write and needs the go-ahead (B.2). Applied in D29, test-plan section 8 and the traps banner. |
| 37 | The first draft of this document read file rows through a `CreatedOn > DATEADD(minute, -15, GETUTCDATE())` window, and made a sub-process route M1's primary path | [test-plan](eng-92719-file-processing-element-test-plan.md) section 9 reads by Id baselines, without `CreatedOn` filters; decisions D29 and test-plan section 8 name the script-task probe `UsrG1FilesBindingProbe` | B.1 follows the Id baselines. M1's primary path is the script-task probe; the sub-process route is the alternative, which the ENG-95984 File process parameter type test-plan runs as V8 after the PT cut (B.4) |

Smaller corrections that changed no decision:
- A tool-description `name=` IS checked against the curated fixture
  (`CLIO/clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs:537-594`, decisions X2).
- The floor sentence must keep `this clio requires <X>`
  (`CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:1503-1560`, decisions X3).
- Merge tails are 30-71 minutes, not 10-45 (delivery research, re-measured over the merged PR triples).
- ClioRing dispatches any non-destructive catalog tool by name through clio-run with only an `environment` argument,
  and shows each tool's Purpose (`clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157`,
  `clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95`). No Ring-consumed contract changes while tool names, Destructive
  flags and Purpose leads stay unchanged.
- creatio-ui has 13 `File` matches, all in BPMN import, and no file-processing logic (diagram research).
- The M1 fixture needs TWO attachments on `G1 Probe Source`, because M4 and the ENG-95984 File process parameter
  type test-plan's V7 reuse it (B.4).
- M14 was scheduled "during PK-PT's review", and M13 "during PK-PT / PK-OA review"; M19, M20 and M24 had no slot
  ([pr-split](eng-92719-file-processing-element-pr-split.md) section 6, and an earlier B.2 here). The stand holds
  PT's final cut during that review (pr-split section 14, steps 4-5), so these probes would have measured PT's cut
  rather than 1.6.6.54. They now run on day 0, before the first cut (B.0, B.2). pr-split section 6 now shows the same
  schedule.
