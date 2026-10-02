# ENG-92719 File processing element and ENG-95984 File process parameter type: analysis index

[ENG-92719](https://creatio.atlassian.net/browse/ENG-92719) File processing element (Story) with its sub-tasks
[ENG-96505](https://creatio.atlassian.net/browse/ENG-96505) Element readiness and object attachments mode and
[ENG-96506](https://creatio.atlassian.net/browse/ENG-96506) Generated report + process parameter modes, and
[ENG-95984](https://creatio.atlassian.net/browse/ENG-95984) File process parameter type (Task); epic
[ENG-92704](https://creatio.atlassian.net/browse/ENG-92704) Create BP via AI Toolkit.

**Summary.** These documents analyse and plan the AI Toolkit support for the "Process file" element and for
file-typed process parameters in `create-business-process`, `modify-business-process` and
`describe-business-process`. Three facts shape everything. First, a file travels through a process by reference,
never as bytes. One file is the platform type FileLocator, and several files are a generic collection with one
FileLocator item. The ticket's "Binary / File" type cannot work, so the acceptance criteria of all four issues are
rewritten. Second, "Process file" is not one element. It is three user-task schemas behind one caption, and the
source choice swaps the schema. It is therefore built as one `fileProcessing` element whose variant is the group the
caller sends. Third, the platform accepts almost every configuration mistake silently. So most of the work is:
- refusals;
- a per-object attachment-storage resolver;
- a record scope compiled into the filter;
- a describe block that round-trips.

Delivery is one package → clio → knowledge PR triple per Jira issue, plus one docs-only clio PR per spec set: 14
PRs. The order is ENG-95984 File process parameter type, then ENG-96505 Element
readiness and object attachments mode, then ENG-96506 Generated report + process parameter modes, then a new SysFile
Sub-task. A human merges every PR. With an AI agent writing the code, ENG-95984 File process parameter type takes
about 7-15 h of agent time and 4.5-10 h of the owner's time (3-5 working days), and ENG-92719 File processing element
about 26-52 h of agent time and 15-33.5 h of the owner's time (about 7-10 working days after that); about 2-3 weeks
from day 0 for both. Eighteen owner questions are open (Q7 was answered by M3). Of the 27 stand measurements, 11 are
done, 2 partly, 1 skipped and 13 open. Every measurement that gates PK-PT or PK-OA code is done; the seven that
still gate code gate PK-RP (M1, M2) or PK-SF (M7, M8, M21, M23, M25). The day-0 decisions come before any code.

Written 2026-10-01; measured on the stand 2026-10-02. No product code was written. The probes were built in package
`Custom` and deleted with their fixtures the same day (open-questions C.7). The documents are on the local clio
branch `feature/ENG-92719-process-file-spec` and attached to the four Jira issues. **Status: open. No owner
decision has been taken yet.** The code was read at these points:

| Source | Version read |
|---|---|
| crt-process-builder | `main` `3f4cce50` (CrtProcessBuilder 1.6.6.54, the version the stand runs) |
| clio | `master` `03ef3944f` |
| clio-knowledge | `master` `d0b5a2b` (libraryVersion 1.15.90) |
| Creatio core | 10.1.37, the stand's core |

**Basis labels** used in every document:
- *source*: read in code or metadata. For runtime behaviour this is a hypothesis.
- *measured*: observed on the stand, or counted over the corpus, with its date.
- *inference*: derived from the two above.

No option in any document uses an existing prototype as an argument. Every option is argued from clean-slate cost and
constraints.

| Alias | Jira issue |
|---|---|
| **PT** | ENG-95984 File process parameter type (Task) |
| **FE** | ENG-92719 File processing element (Story) |
| **OA** | ENG-96505 Element readiness and object attachments mode (Sub-task of FE) |
| **RP** | ENG-96506 Generated report + process parameter modes (Sub-task of FE) |
| **SF** | NEW Sub-task of FE, "SysFile attachment storage in the Process file element" (key assigned on creation) |
| **MH** | not created: the conditional Sub-task of PT, "typeFromElement collection mirror leaves its items unbound", waited on measurement M3, and M3 refuted the defect on 2026-10-02 |

PR ids: `PK-` = crt-process-builder, `CL-` = clio, `KB-` = clio-knowledge (for example PK-OA). D1-D29, M1-M26 and
Q1-Q19 are defined in [decisions](eng-92719-file-processing-element-decisions.md) and
[open-questions](eng-92719-file-processing-element-open-questions.md).

---

## Reading order

| # | Document | What it settles |
|---|---|---|
| 1 | [platform-reference](eng-92719-file-processing-element-platform-reference.md) | **What the platform does, end to end.** The three schemas and their 13 / 8 / 13 parameters. The runtime per variant. `ResultActionType`: Object and Report produce their output collections for both values, and the Process variant supports SaveToFiles only. The file data model. Storage chosen per entity. The classic designer field by field. The diagram, the feature flags, and what the server validates (almost nothing). §13 lists every load-bearing claim that is still only source-traced. |
| 2 | [use-cases](eng-92719-file-processing-element-use-cases.md) | **Why customers use the element, and how the product uses it.** Academy and Community. All 16 shipped processes with the element (30 elements), of which 4 are product processes that follow one recipe. The five Jira patterns tested against the corpus. The recommended scope per pattern, and the "which variant for which intent" guidance. Proposed as the "attached use-case inventory" that the Story refers to; Jira has no such attachment today. |
| 3 | [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) | **The oracle for every "matches a designer-built capture" criterion.** Every metadata key decoded, and the provenance rules measured over 400 stored parameter entries. Which shipped capture each variant is compared with. What is missing: no saved SysFile-mode element exists anywhere. The named exceptions of the comparison rule, and the capture procedure SC-0..SC-4 on the stand. |
| 4 | [traps](eng-92719-file-processing-element-traps.md) | **T-1..T-68, 58 of them silent (T-8 among them, refuted by M3; T-68 added on 2026-10-02).** Each trap has its builder rule, refusal or test. The banner lists the traps that rest on a source trace only, and the run that settles each one. |
| 5 | [reuse](eng-92719-file-processing-element-reuse.md) | **What to reuse, mirror or write new.** What comes from core. About twenty constants and seven algorithms that must be mirrored from CrtProcessDesigner, because production code cannot reference `Terrasoft.Configuration`. The four CrtProcessBuilder pieces that must change before reuse. What is genuinely new. |
| 6 | [decisions](eng-92719-file-processing-element-decisions.md) | **The contract.** D1-D29 with options and consequences; 14 of them wait for the owner (D9 was answered by M3). Part D holds the replacement acceptance criteria for FE, OA and RP (D-2..D-4); D-1 points to AC-1..AC-10 of the ENG-95984 File process parameter type plan §1.4; D-5 holds the proposed Sub-tasks. Appendix A is the refusal and notice catalogue; Appendix B is the review log. |
| 7 | [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) | **The parameter type.** Replacement AC-1..AC-10, and the binder rules P1/P2/P2-MI/P3/R-M1/R-M2 in one table. Work packages PB/CL/KB with `path:line`. The stand rows W0, V0, SC-0 and V1-V6, the estimate, the Definition of Done, and owner items O-1..O-10. |
| 8 | [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) | **How the parameter type is tested.** Package (PU), clio (CU), e2e, knowledge (KU) and stand (V0-V8) cases, traced to AC-1..AC-10. The package mocking recipe. The cases that move with an open decision. |
| 9 | [plan](eng-92719-file-processing-element-plan.md) | **The element.** What the tickets say that is not true (N1-N16), and what exists versus what is missing. The delivery shape and the work packages OA / RP / SF, with files and hours. Stand gates and post-cut proofs, the knowledge records owed, the estimate, and the Definition of Done per issue. |
| 10 | [test-plan](eng-92719-file-processing-element-test-plan.md) | **How the element is tested.** The harness per repository, and the C# mocking recipes adapted from `C:/Projects/UnitTests`. TC-01..TC-91 traced to the AC, stand scenarios ST-01..ST-08, and designer checks DT-01..DT-04. The methods for M1-M26 and the read-only evidence queries. |
| 11 | [pr-split](eng-92719-file-processing-element-pr-split.md) | **How the work becomes PRs.** The verdict per repository and its measured basis, and the PRs with exact titles, branches and merge order. Hard edges E1-E10. What must land together and what must not. Version and floor rules, contingencies X1-X8, Jira link corrections, and the review / merge / stand protocol. |
| 12 | [open-questions](eng-92719-file-processing-element-open-questions.md) | **What is still open.** Q1-Q19 for the owner, each with a recommendation, and 27 stand measurements with recipes and status. What was measured on 2026-10-01 and 2026-10-02. The 37 contradictions found during the research and how each was resolved; 7 are still open. |

### Where each part of the request is answered

| Question | Answered in |
|---|---|
| How the existing designer does it | [platform-reference](eng-92719-file-processing-element-platform-reference.md) §8 (the fields per variant, source switching, the `Files` binding, the Add-parameter menu) and §7.3-7.4 (storage per entity, measured as UO-2..UO-4); [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §4 and §9 |
| Why customers use it (Academy) | [use-cases](eng-92719-file-processing-element-use-cases.md) §1 |
| How the element works | [platform-reference](eng-92719-file-processing-element-platform-reference.md) §5 (the runtime per variant), §6 (the file data model), §10-§11 (flags; what the server validates) |
| What to reuse from core and CrtProcessBuilder | [reuse](eng-92719-file-processing-element-reuse.md) (a verdict per piece; §10 lists what is new) |
| The diagram | [platform-reference](eng-92719-file-processing-element-platform-reference.md) §9; [decisions](eng-92719-file-processing-element-decisions.md) D20 |
| How to mock the C# tests | [test-plan](eng-92719-file-processing-element-test-plan.md) §3-§4; [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) §3 |
| Real shipped processes with the element: how and why | [use-cases](eng-92719-file-processing-element-use-cases.md) §2-§4; [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §1 and §4 |
| One PR or several per repository | [pr-split](eng-92719-file-processing-element-pr-split.md) §2 and §10; [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) §10 |

---

## What to attach to which Jira issue

The Story is where the shared reference set lives. Each Sub-task carries the documents its implementer works from
and points to the Story for the rest. ENG-95984 File process parameter type is a separate Task, not a child of the
Story, so it gets its own copy of the shared documents it depends on. If the owner accepts O4, the folder is also
committed in the docs-only clio PR (CL-DOC), as `spec/eng-92707-sub-process-element/` was before it.

| Issue | Attach | The parts that concern this issue |
|---|---|---|
| ENG-95984 File process parameter type | this README; [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md); [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md); [decisions](eng-92719-file-processing-element-decisions.md); [traps](eng-92719-file-processing-element-traps.md); [platform-reference](eng-92719-file-processing-element-platform-reference.md); [serialization-capture](eng-92719-file-processing-element-serialization-capture.md); [pr-split](eng-92719-file-processing-element-pr-split.md); [open-questions](eng-92719-file-processing-element-open-questions.md) | decisions D1-D9 (its acceptance-criteria text is AC-1..AC-10 of the ENG-95984 File process parameter type plan §1.4; decisions D-1 points there); traps §A-§B (T-1..T-16); platform-reference §6; serialization-capture §8; pr-split §5.1-§5.2 and §5.7-§5.8 (CL-PT-DOC, and the BMAD artifacts that must exist before PK-PT opens, edge E9); open-questions Q1, Q2, Q6, Q7, Q10-Q12, Q19 and M3, M3b, M6 |
| ENG-92719 File processing element | this README and all ten documents of this folder | use-cases is the use-case inventory that the AC names; decisions D-2; plan §1-§4 and §8; pr-split in full |
| ENG-96505 Element readiness and object attachments mode | this README; [plan](eng-92719-file-processing-element-plan.md); [test-plan](eng-92719-file-processing-element-test-plan.md); [decisions](eng-92719-file-processing-element-decisions.md); [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) | plan §5.1, §6, §11; test-plan §5.1-§5.8, ST-01, ST-02, DT-01, DT-02; decisions D10-D16, D19, D20 and D-3; serialization-capture §4.2; traps §C-§E and §G-§I (on the Story); open-questions Q3, Q13-Q17 and M10, M11, M14, M17-Q7, M26 (M26 gates the OA.12 guide sentence and the CL-OA knowledge record, not code) |
| ENG-96506 Generated report + process parameter modes | this README; [plan](eng-92719-file-processing-element-plan.md); [test-plan](eng-92719-file-processing-element-test-plan.md); [decisions](eng-92719-file-processing-element-decisions.md); [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) | plan §5.2, §11; test-plan §5.9-§5.11, ST-03..ST-07, DT-03; decisions D17, D18 and D-4; serialization-capture §4.3-§4.4; use-cases §2.3; traps §F; open-questions Q4, Q18 and M1, M2, M13, M15 |
| SF (new Sub-task, once created) | this README; [plan](eng-92719-file-processing-element-plan.md); [test-plan](eng-92719-file-processing-element-test-plan.md); [decisions](eng-92719-file-processing-element-decisions.md) | plan §5.3; test-plan §5.12, ST-08, DT-04; decisions D15 and D-5; serialization-capture §11-§12 (SC-1); open-questions Q3, Q17 and M7, M8, M21, M23, M25 |

---

## Verification status

| Layer | What was checked | Result |
|---|---|---|
| Ten research reports (platform runtime, file data model, designer client, diagram, corpus, Academy, CrtProcessBuilder, test mocking, delivery surface, the earlier refinement's claims) | each re-checked by an independent refute-first verifier. The verifier did not use Jira, the refinement or other agents' output as evidence, and its log wins over its report | 139 claims: **95 confirmed and 44 partial**, each partial corrected in its log. No claim was refuted as a whole. The corrections are carried into these documents |
| Eight gap reports, G1-G8 | the completeness critic's gaps: runtime binding, filter and sort roots, the storage resolver, builder serialization, consumers, report edge cases, the parameter contract, budgets and versions | each gap answered by a gap report. Their runtime claims are basis=source, and each one a decision depends on has an M-number |
| Decisions, review round 1 | two adversarial reviewers | 32 findings (8 high, 9 medium, 15 low), all accepted, none rejected ([decisions](eng-92719-file-processing-element-decisions.md) Appendix B) |
| Decisions, review round 2 | the PR split, judged from three competing proposals: reviewer-load-first 25 of 30 points, risk-first 24, throughput-first 19 | five changes C1-C5, among them SysFile moving out of OA, the MH Sub-task (later dropped: M3 refuted H-1 on 2026-10-02), and version numbers by rule ([decisions](eng-92719-file-processing-element-decisions.md) Appendix B, Round 2) |
| Cross-document review of the 13 documents | three reviewers: consistency, request and mandates, primary-source facts | 56 findings; every one applied in the documents it touched, in the reconciliation of 2026-10-01 (see [Resolved during review](#resolved-during-review)) |
| Cross-check of the documents against each other | [open-questions](eng-92719-file-processing-element-open-questions.md) Part D | 37 contradictions resolved. 7 are still open: five wait for M1, M3b, M8 or M13, and two need no run because a refusal covers the case |
| Read-only stand measurements, 2026-10-01 | versions; feature states; printables; the designer observations UO-1..UO-4; describe of the four shipped report processes | recorded in [open-questions](eng-92719-file-processing-element-open-questions.md) Part C. Three earlier readings were wrong and are corrected (Part D, rows 17-19). The UO-1 "only Use in process" list was a combobox text-filter artefact. The stand research's "Add files" claim was wrong. Its reading of describe output was wrong too: the missing `itemProperties` came from a stale MCP clio client |
| Runtime measurements, 2026-10-02 | 11 of 27 done (M3, M3b, M6, M10, M11 in memory, M13, M14, M15, M19, M20, M24); M17 and M25 partly; M5 skipped; 13 open | see [Stand measurements pending](#stand-measurements-pending) |

Corrections made while each document was written are listed in that document:
- decisions §0 (X1-X7);
- plan §10 (C-1..C-8);
- test-plan §2 (C1-C7);
- reuse §0 (RF1-RF8);
- serialization-capture §13 (S1-S7).

---

## The seven findings that change the shape of the work

**1. A file is a FileLocator reference. Binary stays refused, and the ticket wording cannot pass.**
- One file is FileLocator `A33C9252-D401-453E-949D-169157067ED9`.
- The platform type NAME "File" resolves to a different BLOB type, BA40CFC5 "File (BLOB)"
  (`CORE/Terrasoft.Core/DataValueTypeManager.cs:341-349`, source).
- Binary `B7342B7A` is a Stream type that the flow engine cannot hold
  (`CORE/Terrasoft.Core/Process/FlowEngineStateService.cs:139-148, 409-429`, source).
- The corpus has 0 Binary or BLOB process parameters, and 50 FileLocator occurrences in 21 files (measured).
- No file-collection type exists. A file collection is a generic collection with one FileLocator item, so callers
  declare `type: FileCollection`, and describe reports that name.
- If describe reported the platform type `CompositeObjectList` instead, feeding it back would rebuild a shapeless
  collection that saves green.
- Binary stays refused, so no Binary test flips. One package probe changes its input type.

Text: decisions D1-D3, Part D.

**2. Admitting the type is the small half. The two-level binder is the work, and it changes shipped behaviour.**
- The runtime rebuilds collection rows from the TARGET's own items, so a file collection bound only at its outer
  level saves green. Its rows are `{File: null}`, and the consumer is predicted to throw a NullReferenceException
  (source).
- Shipped content binds both levels in 11 of 11 file-collection bindings (measured).
- PT puts the rules P1, P2, P2-MI, P3, R-M1 and R-M2 into the shared `ProcessMappingService.ApplyMapping` funnel.
  That funnel serves `mappings[]`, `addMapping`, the mirror, `setConnections` and approvals. PT also adds dotted
  process-parameter paths and a describe decode.
- P1 changes what an item-only multi-instance mapping writes: one iteration becomes N. So the `[RequiresPackage]`
  floor moves, and two shipped guidance passages (`sub-process.md`, `sub-process-when.md`) are rewritten under a
  version gate.
- The shipped ENG-96230 Collection process parameter type mirror leaves its items unbound, but its rows still read
  their values: M3 refuted H-1 on 2026-10-02, because the outer mapping copies the whole collection, items included.
  So there is no Sub-task MH. PT binds the items anyway, as one parity commit (contingency X4).

Text: ENG-95984 File process parameter type plan §0, §3.1.

**3. "Process file" is three schemas, and changing the source replaces the element.**
- "What is the source of the file?" is not a parameter. The palette offers only the Object attachments schema.
- A source change replaces the element: a new name, and every value dropped. It is refused while other elements
  reference the element (source; the palette and replace-menu state measured 2026-10-01).
- So the element gets one token, `fileProcessing` (alias `processFile`). Its variant is the group the caller sends:
  `attachments`, `report` or `files`.
- A variant registry keyed by schema UId makes five things land together for each variant: identity, the
  generic-route refusal, the raw-mapping refusal, the filter-target claim and describe. A registry test checks "all
  five or none".
- Each cut claims only the variant it configures, so every release is self-consistent. One visible effect: from
  RP on, the four shipped product report processes describe as `fileprocessing` instead of `usertask`.

Text: decisions D10, D12, D13.

**4. The platform accepts almost every mistake silently, so most of the element work is refusals.** No variant has a
`Validate` override, and `IsRequired` is never enforced (source). The silent cases:
- an empty filter reads 50 arbitrary files (Object), or generates one Word document per record of the whole entity
  (Report);
- the natural filter `{object: "Account", Id = X}` runs as `AccountFile.Id = X`, finds zero files, and reports
  success;
- an unset `ResultActionType` runs as SaveToFiles, while the designer defaults to Use in process;
- a same-schema copy with no link column lands on the source record as a duplicate.

[traps](eng-92719-file-processing-element-traps.md) holds 67 traps, and 57 of them are silent. The answer is in
decisions D14-D16 and D19:
- a record scope inside the group, compiled into `DataSourceFilters`;
- a dedicated filter target rooted on the runtime root;
- an end-of-request refusal of empty filters on saving and Word elements;
- an action that is always written;
- a typed describe block that reports effective values and `issues`.

**5. Attachment storage is a second axis, and SysFile has no runtime evidence, so it becomes its own Sub-task.**
- The designer chooses storage per entity. An entity with a legacy `<X>File` object keeps it (measured UO-2, UO-4).
  Every other entity is a SysFile entry: SysFile is its file object and `SourceDataEntitySchemaUId` is the record
  object (measured UO-3). The `RecordId` link column and a `RecordId` scope are source
  (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:527-540`), pending
  M7/M21. The server never reads the designer's flag.
- 0 of 30 shipped elements use SysFile (corpus). On the stand, 0 SysFile rows have a non-null `RecordSchemaName`
  other than ConfActivityLog (measured; rows with a NULL `RecordSchemaName` were not counted). The read and write
  paths are source traces only.
- Five measurements stand before its code (M7, M8, M21, M23, M25), and three of them need the user to build designer
  probes.
- Recommended (O1): a new Sub-task SF. OA refuses SysFile sources and targets with a message, and describes
  designer-built SysFile elements without loss.
- The cost is one interim release in which a custom object without its own attachment object cannot be a source or
  a target, plus about 1-2 days at the end.

Text: decisions D15; pr-split §10.2, §13.

**6. The product uses one recipe, and two of the five Jira patterns are not buildable here.**
- In the corpus of 1,090 packages, 16 processes hold the element (30 elements). Only 4 are product processes, with 5
  elements. All 5 follow one recipe: a Generated report from an MS Word printable, saved to the record's
  attachments.
- No product process maps an element output. A following Modify data re-finds the new file with a filter on the
  file object.
- The Object attachments and Process parameter variants occur only in test packages. So AC-OA8's "rarely an
  endpoint" contradicts the product. Decisions D-3 replaces it with "most shipped uses end in a record's
  attachments; when a following element needs the files, map the element's output collection", and TC-88 pins the
  new sentence.
- Read data and Add data never consume a file output; they sit upstream.
- Send email and the Creatio.ai call read files through dynamic slots that the builder cannot create. Send email
  moves to ENG-95985 Send email attachments. The Creatio.ai call is out of scope, because ENG-92725 Execute AI
  Intent element (BP generation) is Won't Do.
- Only MS Word reports are accepted. FastReport is refused: the stand has none, and its process path is unverified.
  DevExpress throws at run time.
- The stand has 5 Word printables, none for Account or Contact (measured).

Text: use-cases §2-§5; decisions D17, D22, D24.

**7. The Jira dependency points at the wrong sub-task, and the clio and knowledge surface sets the merge order.**

ENG-95984 File process parameter type does not functionally block ENG-96505 Element readiness and object attachments
mode. The Object variant's outputs are element parameters, and dotted element paths can address them today
(source). What it really blocks is ENG-96506 Generated report + process parameter modes, which has no Jira link today.

PK-OA still merges after CL-PT, for two reasons (pr-split edges E4 and E6):
- CL-PT ships the `ManagerMap` arm `fileprocessing` / `processfile`. Without it, an older clio meeting the token
  raises a hard `validate-process-graph` Error.
- CL-PT also ships the description-budget swap. Against the 35,072-byte ceiling, create has 209 bytes left and
  modify 189.

Separately, CL-OA merges only after KB-PT is published (edge E5). KB-PT creates the new `process-files` guide,
because the articles the text would otherwise extend (process-modeling, activity-connections, parameters,
element-catalog) are 97.7-99.9% full, and CL-OA writes the first `name=process-files`.

TeamCity never runs the process-designer e2e. So every e2e run is a manual run on the stand, and it counts only with
Ignored = 0.

Text: pr-split §7, §12; ENG-95984 File process parameter type plan §4.3; decisions D25.

---

## PR split: verdict per repository

The answer to "one PR or several": **in every repository, one PR per Jira issue.** No PR combines two issues, and
nothing is split further by layer or by size. The issue order is PT, then OA, then RP, then SF. Within an issue,
the order is package, then clio, then knowledge, and a human merges each PR. That makes **14 PRs**: 4
package, 6 clio (two of them docs-only), 4 knowledge. For ENG-95984 File process parameter type alone, this
is one PR in each repository, plus its docs-only clio PR CL-PT-DOC.

| Repository | Verdict | PRs (exact titles) | Why |
|---|---|---|---|
| crt-process-builder (https://creatio.ghe.com/engineering/crt-process-builder) | **4 PRs** | `ENG-95984 File process parameter type`; `ENG-96505 Element readiness and object attachments mode`; `ENG-96506 Generated report + process parameter modes`; `<SF-KEY> SysFile attachment storage in the Process file element` | `main` is a release candidate at every moment, so every merged PR must leave a releasable state. A layer split fails that: it would ship a `main` that saves green and fails at run time. Pushes dismiss approvals (ruleset 82837), so only one of our package PRs is in review at a time. Splitting by size does not shorten review (48.7 h median for 1,500-4,500 added lines, 50.5 h above that). One PR for everything would be about 12-16k lines |
| clio (https://github.com/Advance-Technologies-Foundation/clio) | **6 PRs** | one rebundle PR per package PR, under the same titles, plus two docs-only PRs: `ENG-95984 File process parameter type` (CL-PT-DOC: PT's BMAD set, merged before PK-PT opens) and `ENG-92719 File processing element` (CL-DOC: FE's BMAD set, merged before PK-OA opens) | four guard tests bind the archive, the pins, the floor literals, the enforced-floor sentences, the capability map and the e2e floor to one tree. So a rebundle cannot leave its feature PR, and two cuts cannot share one clio PR |
| clio-knowledge (https://github.com/Advance-Technologies-Foundation/clio-knowledge) | **4 PRs** | the same titles as the package PRs | a guidance generation reaches every installed clio once it is published. So it must name a version that a merged clio bundles. The one knowledge-first merge in this epic needed a corrective PR (https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/198, fixed by https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/201) |

The hard edges that set the order are in [pr-split](eng-92719-file-processing-element-pr-split.md) §7:
- PK-OA merges only after CL-PT (edges E4 and E6);
- CL-OA merges only after KB-PT is **published** (E5);
- PK-PT opens only after CL-PT-DOC merged (E9);
- CL-DOC merges after CL-PT-DOC when both carry their analysis folders, so the FE folder's links into
  ../eng-95984-file-parameter-type/ resolve on master (E10);
- the floor is the FINAL cut of each clio PR, and version numbers are claimed at cut time, never projected (§9).

The BMAD artifacts each issue needs before its package PR opens are tabled in
[pr-split](eng-92719-file-processing-element-pr-split.md) §5.8.

Contingency splits fire only on a named trigger (§11):
- X1, an early clio PR for the budget swap;
- X2, cherry-pick RP's Report half if the Process-parameter half fails its stand proof;
- X3, swap the RP and SF slots.

X4 has fired: M3 refuted H-1 on 2026-10-02, so there is no MH triple, and binding the mirror's items is one parity
commit in PK-PT.

---

## Estimate

**Model.** An AI coding agent writes all code, tests, docs and guidance, and fixes review findings. The human
answers the owner decisions, builds the designer probes and runs the stand checks the agent cannot, steers the agent
and reads its output, and merges. Calendar time is set by review latency and the gate order (one of our package PRs
in review at a time, one cut on the stand at a time). Calibrated on 12 features delivered in these three repositories
between 2026-08-10 and 2026-09-30 (measured; [plan](eng-92719-file-processing-element-plan.md) §8.4).

| Issue | AI agent, h (wall clock) | Human, h | Calendar, working days |
|---|---|---|---|
| ENG-95984 File process parameter type (incl. its docs-only PR CL-PT-DOC) | 7-15 | 4.5-10 | 3-5 from day 0 |
| ENG-92719 File processing element: shared day 0 (decisions, Jira, CL-DOC) | 1-2 | 2-3.5 | day 0, shared with ENG-95984 File process parameter type |
| ENG-96505 Element readiness and object attachments mode | 15-27 | 5.5-13 | 4-6 (adds 2-3 to the critical path) |
| ENG-96506 Generated report + process parameter modes | 7-15 | 3.5-9 | 3-4 (adds 1.5-2) |
| SF, new Sub-task "SysFile attachment storage in the Process file element" | 3-8 | 4-8 | 2-4 (adds 1.5-2.5) |
| **ENG-92719 File processing element, total** | **26-52** | **15-33.5** | **about 7-10 after ENG-95984 File process parameter type merges** |
| **Both issues** | **33-67** | **19.5-43.5** | **about 2-3 weeks from day 0** |

The MH share is removed: M3 refuted H-1, so the conditional Sub-task MH (1-3 h agent, 1-2 h human, +0.5-1 day) is not
created. It was never inside the totals above.

The human column is the owner's time: decisions and Jira edits, designer-built probes and stand checks, steering and
reading the agent's output, merges and knowledge publication checks. Other reviewers' time is not in it; their
latency is in the calendar column. The breakdown per issue is in the
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md)
§8 and the [plan](eng-92719-file-processing-element-plan.md) §8.

What pushes it up:
- the owner's decision turnaround on day 0;
- the review-latency tail (a 1,500-4,500-line package PR took up to 136 h from review start to merge);
- a foreign restamp on `main` that forces a re-cut after approvals;
- M1 or M23 failing;
- M11 confirming H-G3-1;
- ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph
  validator into the write path (ENG-88414) (the key in that title is ENG-88414 AI-driven application development)
  landing first.

What pulls it down: a second disposable .NET Framework stand (O6), which removes the one-cut-at-a-time wait.

---

## Decide before implementation starts

Every row has a recommendation that the documents already plan on. Full options, and what a different answer
changes, are in [open-questions](eng-92719-file-processing-element-open-questions.md) Part A.

| Q | Decision | Recommended | Needed before |
|---|---|---|---|
| Q1 | Replace the acceptance criteria of PT, FE, OA and RP | yes: PT with AC-1..AC-10 of the [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) §1.4; FE, OA and RP with [decisions](eng-92719-file-processing-element-decisions.md) Part D (D-2..D-4) | any PR |
| Q2 | Jira links | PT **relates to** OA; PT **blocks** RP; OA blocks RP; RP blocks SF; PT and OA block ENG-95985 Send email attachments | any PR |
| Q3 | Where SysFile storage ships | the new Sub-task SF; OA refuses SysFile with a message | PK-OA |
| Q4 | Slot order of RP and SF | RP first, unless custom-object attachments rank above generated reports | PK-RP |
| Q5 | Downstream consumers | Send email → ENG-95985 Send email attachments; the Creatio.ai call is out of scope ("not buildable through this tool yet", never "Creatio cannot") | FE AC |
| Q6 | PR split | one PR per Jira issue per repository, plus SF, CL-PT-DOC and CL-DOC (14 PRs, the docs-only CL-PT-DOC and CL-DOC included) | any PR |
| Q7 | Where defect H-1 is fixed | answered by M3 (2026-10-02): H-1 refuted, so no MH; binding the items is one parity commit in PK-PT (X4) | - |
| Q8 | Delivery protocol (O4-O8, and the ENG-95984 File process parameter type plan's O-10) | CL-PT-DOC and CL-DOC first; the variant registry recorded in the ADR; merge commits for stacked PRs; merge windows with the owners of the in-flight branches; ask for a second stand | CL-PT-DOC and CL-DOC |
| Q9 | Follow-up Sub-tasks | the unconditional ones on day 0; MH not created (M3 refuted H-1); the designer bug report only after M11; the X2 one only on that contingency | any PR |
| Q10 | How a file collection is declared | `type: FileCollection`, read back as `FileCollection` | PK-PT |
| Q11 | Default direction of a FileCollection | Out (a caller-filled one is declared `In`) | PK-PT |
| Q12 | Binder policy | P3 resets a stale parent with a notice; P2 and R-M2 apply only to file-consuming targets | PK-PT |
| Q13 | Element token and variant discriminator | `fileProcessing` (alias `processFile`); the group present is the variant; `source` is an optional check | PK-OA |
| Q14 | Naming bundle of the block | as proposed, including `numberOfRecords` defaulting to 50 (not "all" as in `readData`) | PK-OA |
| Q15 | `ResultActionType` when `action` is omitted on create | inferred from `saveTo`, always written | PK-OA |
| Q16 | Record scope and empty-filter policy | `attachments.recordId` / `report.recordId`; refuse saving and Word elements that have no selecting filter | PK-OA |
| Q17 | Storage policy details | the designer flag is not read for behaviour; no `linkColumn: "none"`; no SysFile override | PK-OA |
| Q18 | FastReport printables | refused in this work | PK-RP |
| Q19 | Name of the new guide | `process-files` | KB-PT |

The owner items O-1..O-10 of the ENG-95984 File process parameter type plan are the same questions: Q10, Q11, Q12,
Q7, Q19, Q1, Q2 and Q8 (O-10 is part of Q8). Two smaller items sit outside that table:
- **O-TP1.** Where E2E-14 (the Process variant's `Files` over the wire) runs. Recommended: in RP's clio PR
  ([ENG-95984 File process parameter type
  test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) §6.2), tracked as TC-91
  of the [test-plan](eng-92719-file-processing-element-test-plan.md).
- **Nested `BK15` rows.** Builder-made elements have none. They are a tolerated deviation, or the handler writes
  them ([serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §10, N10; D20 names it
  a tolerated deviation, N10, with writing the rows as the alternative).

---

## Stand measurements pending

Stand `Creatio`: core 10.1.37, .NET Framework, CrtProcessBuilder 1.6.6.54.
On 2026-10-02 five of the seven read-only measurements (M10, M11 a/b in memory, M13, M15, M17-Q7;
[open-questions](eng-92719-file-processing-element-open-questions.md) C.6) and the day-0 builder probes M3b, M14,
M19, M20 and M24 (C.7) were run. M3 was run the same day and refuted H-1, so MH is not created. The day-0 probes also
found a new defect, a bare NullReferenceException for a nested-item mapping on the create path
([traps](eng-92719-file-processing-element-traps.md) T-68). Fifteen measurements gate code; eight are done, and the seven left gate only PK-RP (M1, M2) and PK-SF (M7,
M8, M21, M23, M25).
Recipes and status are in [open-questions](eng-92719-file-processing-element-open-questions.md) Part B, and methods
in [test-plan](eng-92719-file-processing-element-test-plan.md) §8.

Rules for every measurement:
- every write needs the user's explicit go-ahead;
- schema writes and runs go one at a time, because a parallel burst crashes the app pool;
- cleanup goes through `execute-dataservice-batch`, because the stand rejects HTTP DELETE, and a remote
  `delete-schema` needs the CLI `--timeout`;
- probes that need a File parameter or a configured Process file element are built by a human in the classic
  designer;
- probes that characterise shipped CrtProcessBuilder behaviour run on 1.6.6.54, before any cut is installed.

| Gate (first PR whose code waits) | Measurement | Write? | Built by |
|---|---|---|---|
| PK-PT | **M3**: does the shipped collection mirror read null rows (H-1)? **Done 2026-10-02: no, H-1 refuted** (3 iterations, all "name set"); no MH, X4 applies | yes | builder + one designer step |
| | **M6**: a flat File taken from a collection item outside a row context. **Done 2026-10-02: it reads null** (the source held two files), so R-M1 stays a refusal | yes | designer + builder |
| PK-PT verification baseline (no code waits) | **M3b** (MI-0 in the ENG-95984 File process parameter type test-plan): baseline, an item-only multi-instance mapping runs one iteration on 1.6.6.54. It is the baseline for the post-cut proof V3 and for the PT row of [pr-split](eng-92719-file-processing-element-pr-split.md) §14 | yes | builder |
| PK-OA | **M10**: the designer's object list against the resolver's prediction. **Done 2026-10-02: matches** | no | UO method |
| | **M11 (a)(b)**: H-G3-1, does a designer open-and-close write `SourceDataEntitySchemaUId = <X>File`? **Done 2026-10-02 in memory: not reproduced on either half**; the optional save variant (c) remains | no (in memory) | UO method |
| | **M14**: the server-built element shape on 1.6.6.54 (day 0, on 1.6.6.54, before the first cut) | yes | builder |
| | **M17-Q7**: the foreign-key pre-check for an empty `ConnectedObjectId`. **Done 2026-10-02: FK present on `ContactFile` / `AccountFile`, none on `SysFile.RecordId`** | no | agent |
| | **M13**: describe of PrintContractsReport, converged or snapshot. Decides the TC-45 fixtures: the Object ones in PK-OA, the Report ones in PK-RP. **Done 2026-10-02: `Guid` with `referenceSchema: Contract`** | no | agent |
| PK-RP | **M1 + M2**: does a nested-only `Files.File <- File` copy one file, and does an unset File fail with an NRE? | yes | designer (script-task probe `UsrG1FilesBindingProbe`) |
| | **M15**: FastReport packages and printables. **Done 2026-10-02: none**, so Q18 stands | no | agent |
| PK-SF | **M7**: the filter JSON the designer writes for a SysFile `RecordId` scope | no | UO method |
| | **M8**: an Object element finds a Freedom UI upload stored in SysFile | yes | user + designer |
| | **M21**: a designer save capture of a SysFile-mode Object element | yes | designer |
| | **M23 (a)-(d)**: the SysFile write path, four cases | yes | designer |
| | **M25**: the server feature read and its code name (partly answered: client read) | yes | designer (script) |
| OA.12 guide sentence and CL-OA record (no code) | **M26**: after "Save to object attachments", does `ObjectFiles` hold the SOURCE locators ([traps](eng-92719-file-processing-element-traps.md) T-19)? | yes | user (designer-built probe) |
| no code waits | M4, M9, M12, M16, M17 runs, M18, M19, M20, M22, M24 (M19, M20 and M24 on day 0); M5 is skipped because the shape is refused anyway | yes, except M12 | various |

The capture steps of [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §12 are
pending too:
- **SC-0**: calibrates the read path on PrintInvoiceReport (read-only).
- **SC-1**: one designer-built process `UsrFpSc1Capture`, never run, saved once by the user. It covers the missing
  captures, M7, and the capture half of M21.
- **SC-4**: the builder twin of each cut.

The ENG-95984 File process parameter type test-plan adds three rows:
- V0, a read-only designer read;
- V7, recommended: a builder-written FileCollection that is consumed;
- V8, a single builder-declared File, run only if M1 passed.

Order ([open-questions](eng-92719-file-processing-element-open-questions.md) B.2): on day 0, on 1.6.6.54, before
the first PT cut is installed: M13, then M6, M3, M3b, M14, M19, M20 and M24, one at a time. The
version-independent gates M10, M11(a)(b), M17-Q7 (PK-OA), M1 and M15 (PK-RP), the SF set, and M26 may run on any
cut, so also while PK-PT is in review.

---

## Resolved during review

The cross-document review of 2026-10-01 found places where one document had been corrected and a sibling still
carried the old reading. Each was aligned in every document it touches; none is still open.

| # | Point | The reading every document now carries | Stated in |
|---|---|---|---|
| 1 | Serialization parity rule | element side: the [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) §10 rule (UIds ignored; `GS5` normalised for template defaults; N2, N9, N10, N13 tolerated; N11, N14-N16 normalised; anything else fails), in D20, D-2..D-4, TC-41 / TC-59 / TC-69 / DT-02 / DT-04 and plan §11. Parameter side: the AC-8 rule of the [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) §4.2 (N5, PT-a, N9, N15, N14, PT-b), in its plan's AC-8 and V4 and in [pr-split](eng-92719-file-processing-element-pr-split.md) §14 | serialization-capture §10, S3 |
| 2 | ENG-95984 File process parameter type AC text | AC-1..AC-10 of its plan §1.4; decisions D-1 points there | [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) §1.4 |
| 3 | Describe as a capture read path | never; use the raw `SysSchema` row, `pull-pkg` or `export-schema` | serialization-capture §1, S6 |
| 4 | "The designer varies `GS5`" | it does not (14 of 14); the normalisation is needed only because the builder stamps explicit values | serialization-capture S4 |
| 5 | M22 | a write (a no-op designer save), and it needs the go-ahead | [open-questions](eng-92719-file-processing-element-open-questions.md) Part D, row 36 |
| 6 | M3 recipe | open-questions B.4: one designer step, the result recorded in branch captions | open-questions Part D, row 35 |
| 7 | M13, M14, M19, M20, M24 | day 0, on 1.6.6.54 | open-questions B.0, B.2 |
| 8 | Collection counts | 85 Out, 30 Variable, 12 In, 6 Internal of 133 process-level collections (17 more in user-task schemas) | [platform-reference](eng-92719-file-processing-element-platform-reference.md) §6.2 |
| 9 | Sort codec, identity, notice ledger | reuse + change (RF1-RF3) | [reuse](eng-92719-file-processing-element-reuse.md) §0; [plan](eng-92719-file-processing-element-plan.md) C-6 |
| 10 | Test helper and drift guard | owned by PK-PT (PB-9), inherited by PK-OA | ENG-95984 File process parameter type test-plan §3 |
| 11 | Process file → Send email attachments | no Sub-task; link L5 only | plan §5.4, C-5 |
| 12 | Modify data re-find, and Add data → report | tested (TC-71 in CL-OA, TC-74 in CL-RP), plus guidance | plan C-3; [decisions](eng-92719-file-processing-element-decisions.md) D22, D-2 |
| 13 | MCP prompts | `ListUserTasksPrompt`, `CreateBusinessProcessPrompt`, `ModifyBusinessProcessPrompt` and `DescribeProcessPrompt` change per cut, pinned by TC-90 | decisions D25 |
| 14 | "Rarely an endpoint" | replaced; TC-88 pins the new sentence | decisions D-3 |
| 15 | BMAD for PT | CL-PT-DOC before PK-PT (E9); 14 PRs (no MH: M3 refuted H-1) | ENG-95984 File process parameter type plan CL-0, O-10; pr-split §5.8 |
| 16 | M26 | in D29 | [traps](eng-92719-file-processing-element-traps.md) T-19 |
| 17 | Version numbers | rules, claimed at cut time; the floor is each clio PR's final cut | pr-split §9 |

---

## Scope excluded on purpose

Each item has its reason in [plan](eng-92719-file-processing-element-plan.md) §9 and
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) §12:
- creating a printable;
- FastReport and DevExpress reports;
- Process file → Send email attachments, which belongs to ENG-95985 Send email attachments;
- Process file → Creatio.ai;
- `linkColumn: "none"`, and an explicit SysFile storage override;
- "attachments of the records matching a condition" in SysFile mode;
- multi-column sort;
- a new report-lookup MCP tool, because `list-printables` is the discovery route;
- fixing H-G3-1 in CrtProcessDesigner;
- SerializeToDB on the generic route, and collection parameters as filter values (both side Sub-tasks);
- a declared item shape for collections, a mirror type allow-list, and decoding process-parameter sources (PT
  follow-up Sub-tasks);
- a file passed as a start value through `run-process`, and any constant, upload or byte form of a file;
- creatio-ui, which has no file-processing logic; the diagram needs only what the handler writes;
- ClioRing: it consumes only catalog names, Purpose text and Destructive flags, and dispatches any non-destructive
  tool generically through clio-run (`clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95`,
  `clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157`). Names and flags stay unchanged, and each clio PR
  confirms the Purpose leads are unchanged when it re-measures its descriptions.

---

## Evidence base

| Source | What and how much |
|---|---|
| Creatio core | `C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib` (cited as `CORE/`), 10.1.37, the stand's version, cited for runtime claims. The 8.3.x checkout `C:/Projects/Creatio2/TSBpm/Src/Lib` is cited only where it was read; the differences are in [platform-reference](eng-92719-file-processing-element-platform-reference.md) §12 |
| Classic designer | `C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0` (its `Schemas` folder is cited as `PD/`). The 13 client units involved are byte-identical to what the stand serves (measured 2026-10-01) |
| Shipped corpus | `C:/Projects/PackageStore`: 1,090 package directories and 1,665 process schemas. 16 of them hold the element (30 elements: Object 14, Process 2, Report 14), with 400 stored parameter entries and 394 mapping rows |
| Package under change | crt-process-builder `main` `3f4cce50` (1.6.6.54), byte-identical to the stand's installed package |
| MCP and guidance surface | clio `master` `03ef3944f`; clio-knowledge `master` `d0b5a2b` (libraryVersion 1.15.90) |
| Stand | read-only reads on 2026-10-01, sequential. Designer readings UO-1..UO-4 were taken on a new, unsaved process, and the tab was closed without saving |
| Product documentation | Academy: the Process file article (7.17 to 10), Send email, Creatio.ai text files, and 43 release-note pages. Community threads, cited as documentation, never as evidence of today's behaviour |
| New designer | `C:/Projects/creatio-ui`, **verified negative**: no file-processing logic; its 13 `File` matches are all BPMN import |
| Platform test patterns | `C:/Projects/UnitTests/ProcessDesigner.UnitTests`: a reference for runtime semantics only. Its recipes are copied as shapes, never referenced |
