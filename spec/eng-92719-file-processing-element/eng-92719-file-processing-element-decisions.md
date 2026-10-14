# ENG-92719 File processing element: contract and design decisions

| | |
|---|---|
| Issues | ENG-92719 File processing element (Story), with its sub-tasks ENG-96505 Element readiness and object attachments mode and ENG-96506 Generated report + process parameter modes; ENG-95984 File process parameter type (Task), on which the element depends |
| Epic | ENG-92704 Create BP via AI Toolkit |
| Status | Proposed, 2026-10-01. 6 decisions wait for the owner (section 1, Q13-Q18): D2, D3 (Variable), D5, D22, D23, D24, D25 and D27 were agreed as recommended on 2026-10-07, D15's SysFile half (Q3) too, and D9 was answered by M3 on 2026-10-02. Q20 (where the description bytes for PT and OA come from, D25), opened 2026-10-13, was agreed on 2026-10-14 as A: trim existing text per tool, reviewer-approved |
| Baselines | Anchors point to CrtProcessBuilder `main` `a6bfcbf6` (package 1.6.6.96, 2026-10-12), clio `master` `19e6f9c53` and clio-knowledge `master` `0b56d4a` (guidance libraryVersion 1.15.142), re-pinned and re-checked for meaning on 2026-10-13 (Appendix B, round 5). The stand measurements of 2026-10-02 ran on CrtProcessBuilder 1.6.6.54 (`3f4cce50`); the stand runs 1.6.6.96 since (list-packages, read-only, 2026-10-13). Creatio core 10.1.37 (the stand's core) |
| How it was made | Read-only. Nothing was built, run, committed or written to a repository or to the stand. Jira was read for issue wording only; no Jira text is used as evidence for a platform fact |

## Summary

These are the decisions that let `create-business-process`, `modify-business-process` and
`describe-business-process` build the Process file element and the file-typed process parameters it consumes.
A single file is the platform type FileLocator, not Binary: Binary cannot hold a process value, and the platform
name "File" resolves to a different BLOB type that no designer page offers. A file collection is a Collection
with one FileLocator item; callers declare it as `type: FileCollection`, and describe reports it under that
name so that its output rebuilds the same shape. A mapping into a file-consuming collection binds both levels,
the way the designer does, because an outer-only binding saves green and crashes the consumer at run time. The
element gets one token, `fileProcessing`; its variant is whichever group the caller sends (`attachments`,
`report` or `files`). A variant cannot change in place, the record scope lives inside the group,
`ResultActionType` is always written, attachment storage is derived per object exactly as the designer
derives it, and the empty-filter cases that copy arbitrary files are refused. Each sub-task claims only the
variant it makes configurable, so every release is self-consistent. SysFile storage has no runtime evidence in
shipped content (0 of 30 elements), so it moves to a new Sub-task that follows five stand measurements.
Delivery is one package, one clio and one knowledge PR per Jira issue, merged in that order by a human. The
acceptance criteria of all four issues are corrected where they name a type that cannot work, a runtime
condition that does not exist, or a pattern another ticket owns; Part D gives the proposed replacement text.

---

## How to read this

**Basis labels.** *source* = read in code or metadata; for runtime behaviour this is a hypothesis.
*measured* = observed on the stand or counted over the shipped corpus, with the date. *inference* = a
conclusion drawn from several source facts. Every runtime-critical claim that is basis=source has a stand
measurement (D29). A decision that rests on an unmeasured runtime claim says **pending M-number** (for example "pending M1") and names its
fallback.

**No prototype arguments.** Every option is argued from clean-slate cost and constraints. Code that exists on
some branch is never an argument for or against an option.

**Path aliases.**

| Alias | Path |
|---|---|
| PB | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/` (main `a6bfcbf6`, 1.6.6.96) |
| PBA | crt-process-builder `packages/CrtProcessBuilder/Files/src/CrtProcessBuilderApp.cs` |
| PBT | crt-process-builder `tests/UnitTests/CrtProcessBuilder.Tests/` |
| CLIO | clio repository root (master `19e6f9c53`) |
| KB | clio-knowledge repository root (master `0b56d4a`, libraryVersion 1.15.142) |
| CORE | Creatio core `TSBpm/Src/Lib` at 10.1.37 |
| PD | `PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas` (byte-identical to what the stand serves) |
| PS | `PackageStore` (the shipped corpus) |

**Ticket aliases** (used in dense tables; prose always names the key with its title).

| Alias | Jira issue |
|---|---|
| PT | ENG-95984 File process parameter type (Task) |
| FE | ENG-92719 File processing element (Story) |
| OA | ENG-96505 Element readiness and object attachments mode (Sub-task of FE) |
| RP | ENG-96506 Generated report + process parameter modes (Sub-task of FE) |
| SF | NEW Sub-task of FE, "SysFile attachment storage in the Process file element" (key assigned on creation) |
| MH | not created: the conditional Sub-task of PT, "typeFromElement collection mirror leaves its items unbound", waited on M3, and M3 refuted the defect on 2026-10-02 |

**Sibling documents.**

| Document | What it holds |
|---|---|
| [README](README.md) | Entry point and reading order |
| [platform-reference](eng-92719-file-processing-element-platform-reference.md) | The three user-task schemas, their parameters, the runtime algorithms, storage and the report engine |
| [serialization-capture](eng-92719-file-processing-element-serialization-capture.md) | What the designer writes, key by key, and the shipped captures the AC compare against |
| [use-cases](eng-92719-file-processing-element-use-cases.md) | Why customers use the element (Academy) and the shipped processes that use it |
| [traps](eng-92719-file-processing-element-traps.md) | The silent hazards these decisions refuse or report |
| [reuse](eng-92719-file-processing-element-reuse.md) | What is reused from core and CrtProcessBuilder, and what must be mirrored |
| [plan](eng-92719-file-processing-element-plan.md) | Work packages and estimate |
| [test-plan](eng-92719-file-processing-element-test-plan.md) | Unit, e2e and stand test cases, and the C# mocking recipe |
| [pr-split](eng-92719-file-processing-element-pr-split.md) | The PR set, merge order and review protocol (supersedes the split first drafted here, D27) |
| [open-questions](eng-92719-file-processing-element-open-questions.md) | Owner decisions and measurements, with their status |
| [ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) and [ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md) | The parameter-type ticket (Part A below) |

---

## 0. Corrections to earlier research

Found while writing the decisions; each was re-checked in source. They change what earlier research reports
stated.

| # | Earlier claim | Correction | Basis |
|---|---|---|---|
| X1 | A nested-only `Files.File` binding is "not read at all": the element reader filters a collection by its ROOT value's provenance, so the Process variant copies zero files silently | The provenance filter is only in `CreateParameterValueReader(IProcessElementMetaInfo, ElementParametersReaderOptions)` (`CORE/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:718-753`), whose only callers are `InternalStartAsSubprocess` (`CORE/Terrasoft.Core/Process/ProcessComponentSet.cs:1361-1366`) and `GetElementIterationsCount` (`:1518-1521`). An ordinary flow element is initialised by `InitializeFlowElementProperties` (`:502-529`) with all parameters and no filter. So a nested-only `Files.File` is read as ONE row through `TryGetListValue` (`CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:444-453`). Still a hypothesis: M1 stays mandatory | source, re-read 2026-10-01 |
| X2 | Tool descriptions are not checked against `curated-knowledge-names.json` | They are: `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` (`CLIO/clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs:537-594`) fails on a `name=` that is neither available nor feature-gated. The clio PR that first writes `name=process-files` re-pins the fixture in the same commit (D25) | source |
| X3 | The description-budget swap may replace the floor sentence with one that names no version | `EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral` (`CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:1473-1530`) requires each of the create, modify and modify-as-new-version descriptions to carry `this clio requires <X>` or `CrtProcessBuilder <X> or newer`, equal to the `[RequiresPackage]` literal. The replacement keeps a short floor clause (D25) | source |
| X4 | `linkColumn: "none"` saves files "linked to no record" | Only for a cross-schema copy. A same-schema copy inherits the SOURCE's link through `CopyAttributes` (`CORE/Terrasoft.File/File.cs:174-180`: only attributes the target has not set are copied), so it lands on the source record as a duplicate. D15 does not offer `"none"` | source |
| X5 | "A missing or wrong `ConnectedObjectColumnUId` saves files linked to no record" | Cross-schema only, as in X4; same-schema gives a duplicate on the source record | source |
| X6 | "Add files" on the Process-parameter page creates a Collection IN parameter | Wrong. The page binds the nested `Files.File` of the schema's own `Files` input (`PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:44-57, 73-79`). The dynamic `ProcessFilesControlSchema` slots belong to Send email (`Attachments<N>`) and the Creatio.ai call (`Files<N>`) | source |
| X7 | The Binary-refusal e2e must be flipped in the rebundle PR | Binary stays refused (D1). The tests that pin the refusal stay green; one PBT probe changes its input (D1 consequences) | source |

---

## 1. Owner decisions at a glance

Every row below has a recommendation that this document already plans on. A different answer changes the
named decision and nothing else unless stated.

| # | Decision | Question | Recommended | Alternative and its cost |
|---|---|---|---|---|
| 1 | D2 | How does a caller declare a file collection? | `type: FileCollection`, read back by describe as `FileCollection` **Agreed 2026-10-07.** | Relax the `typeFromElement` mirror (cannot declare a callee input or a script-filled list); generic `itemProperties` (an older server silently drops it and saves a shapeless collection) |
| 2 | D3 | Default direction of a FileCollection | **Variable**: never refused, designer plain-Add parity, the shipped in-process file case. **Agreed 2026-10-07** (it reverses the first recommendation, Out) | Out (one rule per stored type; but shipped Out collections are results, 3 of 84 read inside their process, and a caller-filled one is refused until re-declared) |
| 3 | D5 | Plain source onto a collection item whose parent is bound to a collection (P3) | Reset the parent, with a notice **Agreed 2026-10-07.** | Designer parity: keep the stale parent, which yields N copies of one file |
| 4 | D5 | Scope of item pairing and the wrong-shape refusal (P2, R-M2) | File-consuming targets only **Agreed 2026-10-07.** | Every collection: also changes Read data -> multi-instance mappings, unmeasured and outside this work |
| 5 | D9 | Where the mirror defect H-1 is fixed | Answered by M3 (2026-10-02): H-1 refuted, so no MH; binding the items is one parity commit in PK-PT (X4) | - |
| 6 | D10 | Element token and how the variant is chosen | Token `fileProcessing` (alias `processFile`); the variant is the group present; `source` is an optional check | A required `source` enum (collides with `readData.source`); a `fileSource` key |
| 7 | D11 | Naming bundle of the block | `attachments`, `report`, `files`, `saveTo`, `action: useInProcess / saveToAttachments`, `report.printable`, `fileNameSuffix`, `recordId` inside the groups | Any renaming before code; afterwards renames cost a deprecation |
| 8 | D14 | `ResultActionType` when `action` is omitted on create | Inferred: `saveTo` present -> save, absent -> use in process; always written | Designer default (use in process: a caller who forgot `action` gets a silent no-save); mandatory `action` |
| 9 | D15 | Storage behaviour | Feature flag not read for behaviour (notice only); no `linkColumn: "none"`; no SysFile override; SysFile in Sub-task SF | Read the flag and reproduce the OFF list (string-coupled, copies `ModuleStructure`); keep SysFile in ENG-96505 Element readiness and object attachments mode with five pre-code measurements on its path |
| 10 | D16 | Record scope shape and the empty-filter policy | `attachments.recordId` / `report.recordId`; refuse saving or Word elements with no selecting filter | `filter.recordId` (two meanings of `recordId`, dropped by clio's filter DTO); notice only |
| 11 | D17 | FastReport printables | Refused in this work (no stand evidence that the process path works) | Accept with a notice |
| 12 | D22 | Downstream consumer patterns | Send email -> ENG-95985 Send email attachments; Creatio.ai call out of scope **Agreed 2026-10-07.** | Keep both in ENG-92719 File processing element (needs dynamic attachment slots the builder cannot create) |
| 13 | D23 | Jira links | L1-L5: ENG-95984 File process parameter type relates to ENG-96505 Element readiness and object attachments mode and blocks ENG-96506 Generated report + process parameter modes; OA blocks RP; RP blocks SF; PT and OA block ENG-95985 Send email attachments **Agreed 2026-10-07.** | Keep link 560203 "PT blocks OA" if the team uses "blocks" for sequencing (it then claims a functional dependency that does not exist) |
| 14 | D24 | Acceptance criteria | Replace as in Part D **Agreed 2026-10-07.** | Keep as written (the "Binary / File" and "ResultActionType = 1" criteria cannot pass) |
| 15 | D25, D27 | Guide name; PR split | One new guide `process-files`; one PR per Jira issue per repository (14 PRs, including the docs-only clio PRs CL-PT-DOC and CL-DOC). **The PR split (D27) was agreed on 2026-10-07 and the guide name (D25, Q19) on 2026-10-08.** | `process-file` or a guide per ticket; one PR per repository for everything |

Delivery-process decisions that do not change the contract are owner items O1-O8 in
[pr-split](eng-92719-file-processing-element-pr-split.md) section 13: the slot order of RP and SF (O3, RP first
recommended), the docs-only clio PR for this spec set (O4), and for ENG-95984 File process parameter type the
docs-only CL-PT-DOC (its plan's O-10), the variant registry as an ADR item (O5), a second
disposable stand (O6), merge-commit mode for stacked PRs (O7) and merge windows with in-flight branches (O8). The
same questions, with their options, are Q1-Q20 in
[open-questions](eng-92719-file-processing-element-open-questions.md); the row numbers above are the ones it cites.
Q20 (where the description bytes for PT and OA come from, opened 2026-10-13, agreed 2026-10-14: A, trim) has no row
above: it belongs to D25's tool-description budget and concerned the clio description edits only, not package code.

## 2. Decision index

| ID | Title | Ticket | Owner | Pending |
|---|---|---|---|---|
| D1 | One file is `FileLocator`; friendly names; Binary stays refused | PT | no | - |
| D2 | Declaring a file collection: `FileCollection` | PT | agreed 2026-10-07 | - |
| D3 | Direction and Tag defaults | PT | agreed 2026-10-07 (Variable) | - |
| D4 | Dotted process-parameter paths | PT | no | - |
| D5 | The two-level collection binder | PT | agreed 2026-10-07 | M6 (R-M1; done 2026-10-02: refusal stays) |
| D6 | The dotted-mirror bug | PT | no | - |
| D7 | Constants, `referenceSchema`, delete guard, `setParameter` shape | PT | no | - |
| D8 | Describe of file parameters, nested-only bindings, single-token element sources | PT | no | - |
| D9 | The shipped collection mirror binds the outer level only (H-1; refuted by M3) | PT | no (answered by M3) | - |
| D10 | Element token; the variant is the group present | OA | **yes** | - |
| D11 | The `fileProcessing` block contract | OA, RP | **yes** | - |
| D12 | Source immutability and `setElement` semantics | OA | no | - |
| D13 | The generic `userTask` route and raw configuration writes, per cut | OA, RP | no | - |
| D14 | `ResultActionType` | OA, RP | **yes** | M16 (notice wording) |
| D15 | The attachment-storage resolver | OA, SF | **yes** (Q17; SysFile in SF agreed 2026-10-07) | M8, M10, M11, M23, M25 |
| D16 | Filter and sort roots, record scope, empty-filter policy | OA, RP, SF | **yes** | M7 (SysFile), M24 |
| D17 | Generated report rules | RP | **yes** | M15, M16 |
| D18 | Process-parameter variant: the `files` input | RP | no | M1 |
| D19 | Describe block of the element | OA, RP | no | M11, M13 |
| D20 | Serialization parity, template defaults, nested UIds, diagram | OA | no | M14, M21 (SysFile) |
| D21 | Reuse map | all | no | - |
| D22 | Downstream consumers | FE | agreed 2026-10-07 | - |
| D23 | Jira dependencies | all | agreed 2026-10-07 | - |
| D24 | Acceptance-criteria corrections (text in Part D) | all | agreed 2026-10-07 | - |
| D25 | Guidance placement and tool-description budget | all | agreed 2026-10-08 (`process-files`); budget: Q20 agreed 2026-10-14: A, trim | - |
| D26 | `[RequiresPackage]` floors and version numbers | all | no | - |
| D27 | PR split per repository | all | agreed 2026-10-07 | - |
| D28 | Test strategy and C# mocking | all | no | - |
| D29 | Stand measurements that precede code | all | no (each write needs the user's go-ahead) | - |

---

# Part A. ENG-95984 File process parameter type

## D1. One file is `FileLocator`; friendly names; Binary stays refused

**Context.**
- The file-reference type is FileLocator `A33C9252-D401-453E-949D-169157067ED9` (name `FileLocator`, CLR
  `IFileLocator`, client `FILE_LOCATOR = 41`, caption "File"): `CORE/Terrasoft.Core/DataValueType.cs:297`,
  `CORE/Terrasoft.Core/DataValueTypeManager.cs:501-508`. basis=source, re-read 2026-10-01.
- The platform NAME `"File"` resolves to a different type: `FileDataValueType` `BA40CFC5-F554-4c26-8F57-1BB29CF43C4E`,
  caption "File (BLOB)" (`DataValueTypeManager.cs:341-349`). `GetInstanceByName` is exact and case-sensitive.
  basis=source, re-read.
- Binary `B7342B7A` is a `Stream` type. The flow-engine state store has no Stream store: it throws on one path
  and silently drops the value on the writer path (`CORE/Terrasoft.Core/Process/FlowEngineStateService.cs:139-148, 409-429`;
  `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataWriter.cs:255-286`). basis=source.
- Shipped corpus: 0 process or user-task parameters of type Binary or File (BLOB); every file parameter is
  FileLocator (50 occurrences in 21 metadata files). The designer's Add menu offers `FILE_LOCATOR` under "Other"
  and no BLOB (`PD/ProcessSchemaPropertiesPage/ProcessSchemaPropertiesPage.js:611-618`). basis=measured (corpus
  scan, 2026-10-01) + source.
- The package's only type entry point is `ResolveDataValueTypeUId` -> `NormalizeParameterTypeName` ->
  `GetInstanceByName` (`PB/Parameters/ProcessParameterService.cs:1012-1019, 1033-1078`); its default arm refuses
  every name it does not list, `File` included. basis=source, re-read.

**Options.**

| Option | Cost | Constraint violated |
|---|---|---|
| a. Pass `File` through to `GetInstanceByName` | 1 line | Produces the BLOB type, which no designer page offers and the runtime cannot hold |
| b. Admit Binary | small | No storage path; the designer never offers it |
| c. Map friendly names to `FileLocator`; keep Binary and BLOB refused with a targeted message | about 20 LOC + 4 tests | none |

**Decision: c.**
- `file`, `filelocator`, `file locator` (case-insensitive) normalise to
  `FileLocatorDataValueType.FileLocatorDataValueTypeName`. The canonical platform name is what reaches the
  manager, so `"File"` can never resolve to BA40CFC5.
- `binary` and `blob` get their own refusal: "Parameter type 'Binary' is not supported: a process parameter
  carries a file by reference, not its bytes. Use type File (one file) or FileCollection (several files)." The
  generic refusal lists `File, FileCollection`.
- The doc comment at `ProcessParameterService.cs:1021-1032` drops "binary / file" from the rejected list and says
  why `File` is special-cased.

**Consequences.**
- Contract text: the create `parameters[].type` and modify `addParameter` type lists name `File` and
  `FileCollection` (bytes in D25).
- Describe reads a File parameter back as `type: "FileLocator"` (`ProcessParameterService.cs:158, 1080-1085`),
  which the alias accepts, so the round trip holds.
- PBT: `File`, `file`, `FileLocator`, `file locator` give `A33C9252`, never `BA40CFC5`; `Binary` and `Blob` are
  refused with a message naming File.
- The Binary refusal keeps the substrings `not supported` and `Binary`. `PBT/ProcessParameterServiceTests.cs:144-150`
  stays unchanged. The probe `AddProcessParameter_ShouldNameCollection_InUnsupportedTypeMessage` (`:1672-1680`)
  moves its input from `Binary` to another unsupported registered name (`Image` or `Color`): Binary now gets the
  dedicated message, and the probe would otherwise pass only because "FileCollection" contains "Collection".
  The clio e2e that pins the refusal (`CLIO/clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs:2401-2427`) stays
  green, but it never runs in TeamCity: `baseFilter` excludes `McpE2E.ProcessDesigner`
  (`CLIO/clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:46`, measured 2026-10-01). So the PBT is the only
  automated guard. The e2e's `[Description]` is reworded to "refused for good", not "deferred".
- **Owner decision: no.**

## D2. Declaring a file collection: `type: FileCollection`, read back as `FileCollection`

**Context.**
- A file collection is the generic `CompositeObjectList` `651ec16f` (designer caption "Collection of records")
  with ONE nested FileLocator item. There is no file-collection data type. basis=source.
- Shipped process-level file collections: `ProcessTests/FileParameterProcess.FileCollection` (Variable, item
  `FileCollectionParameter`, no Tag) and `ProcessLibrary/MarkProcessesToCancel.FilesCollection` (Out, item
  `File`, no Tag, filled by a script). 0 of 133 shipped process-level collections carry a Tag. basis=measured
  (corpus, 2026-10-01).
- Today a declared `Collection` is shapeless, and the `typeFromElement` mirror (from ENG-96230 Collection process
  parameter type) refuses file outputs because their items carry no column Tag
  (`PB/Parameters/ProcessParameterService.cs:456-475`). basis=source.
- `compositeobjectlist` is accepted on write (`ProcessParameterService.cs:31-33`), and the write contract has no
  `itemProperties` or `tag` member (`PB/Contracts/ProcessDescriptorContracts.cs:1980-2055`). The shipped guide
  tells agents to feed a described collection back through `typeFromElement`, "never by re-typing the shape"
  (`KB/guidance/mcp/guides/processes/parameters.md:37-39`, pinned by
  `KB/automation/Clio.Knowledge.Bundle.Tests/CollectionParameterGuidanceTests.cs:24-25`). A file collection has no
  Tag and cannot be mirrored, so a describe output fed back as `CompositeObjectList` would create a SHAPELESS
  collection and save green. basis=source.
- Use cases: a collection no element can be mirrored from (a callee's input; a script-filled list, as in
  MarkProcessesToCancel); keeping an element's file output for later (FileParameterProcess).

**Options.**

| | A. `type: FileCollection` alias | B. Relax the mirror for file outputs | C. Generic `itemProperties` on a declared Collection |
|---|---|---|---|
| Covers a callee input or a script-filled list | yes | no | yes |
| Covers keeping an element output | yes, with one `addMapping` (D5 binds both levels) | yes, one step | yes, with one `addMapping` |
| On an older CrtProcessBuilder | refused loudly ("not supported") | old semantics silently, or refused | unknown member silently dropped: a shapeless collection saved green |
| Package cost | about 70 LOC + 10 tests | about 40 LOC + the item binder + guide exceptions | about 150 LOC + 12 tests + nested-type rules |
| Tool-description bytes | create +30, modify +28 (with D1) | about +100 per tool | about +100 per tool, plus a rewrite of the "opaque list" clause |

**Decision: A, with a describe read-back that names the alias.**
- Write: `filecollection`, `file collection` and `collection of files` normalise to `CompositeObjectList`, and
  `AddProcessParameter` (`ProcessParameterService.cs:84-101`) adds exactly one item: `Name = "File"`,
  `Caption = "File"`, type FileLocator, `Direction = Variable`, no Tag, `ContainerUId = schema.UId` (so the meta
  path is `[Parameter:{item}]` with no element segment, `CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:1097-1110`),
  a fresh UId, unbound.
- Read: describe reports `type: "FileCollection"` for a PROCESS-level `CompositeObjectList` with no Tag and
  exactly one item of type FileLocator (the "FileCollection predicate"); `itemProperties` stays in the output.
  Element parameters keep the platform name (nobody re-declares them). So describe output rebuilds as it reads.
- The fixed item name `File` equals the item names the consumer side already uses (`ObjectFiles.File`,
  `ReportFiles.File`, `Files.File`), which makes D5's item pairing trivial.
- A designer-made FileCollection with another item name (FileParameterProcess: `FileCollectionParameter`) is
  described as `FileCollection` with its real item name in `itemProperties`; a rebuild names the item `File`.
  Item bindings are by UId, so the rebuilt collection works. A replayed mapping that names the old item is
  refused loudly ("item 'FileCollectionParameter' not found on 'FileCollection'; its items: File"), and the guide
  says so. Rejected alternative: describe such a collection as `CompositeObjectList`, which rebuilds shapeless
  and silently.
- B stays refused, with a better message (server-side, 0 description bytes): "declare `type: FileCollection` and
  map it from `<el>.<param>`; both levels are bound by the mapping".
- C becomes a follow-up Sub-task under ENG-95984 File process parameter type, "Declared item shape for
  Collection process parameters". `FileCollection` is then sugar for a Collection with one `File: File` item.

**Consequences.**
- `referenceSchema` with `File` or `FileCollection` is refused (the Lookup branch at `:93-100` would silently turn
  `File + referenceSchema` into a Lookup).
- PBT, modelled on `CollectionParameter_ShouldRoundTrip_BuildDescribeBuild` (`PBT/ProcessParameterServiceTests.cs:2091`):
  build -> describe gives `FileCollection` -> rebuild gives the same item shape; a collection with a Tag, with two
  items, or with one non-file item still describes as `CompositeObjectList`.
- Knowledge (PT knowledge PR): `parameters.md:37-39` gains "... except a file collection, which describe reports
  as type FileCollection: re-declare it by that type"; the rationale comment of
  `CollectionParameterGuidanceTests.cs:24-25` is amended and the new sentence is pinned. Measured together with
  the D1 type-list edit, because both edit `parameters.md` (about 9,656 characters left on 2026-10-13, D25).
- `setParameter type: FileCollection` on a collection of another shape is refused (D7).
- **Owner decision: A, agreed on 2026-10-07.**

## D3. Direction and Tag defaults

**Context.**
- The designer's plain Add writes Direction Variable and no Tag
  (`PD/ProcessSchemaPropertiesPage/ProcessSchemaPropertiesPage.js:1204-1236`); "Create from element" writes Out plus
  Tags (`:665-715`). The package comment at `PB/Parameters/ProcessParameterService.cs:103-104` wrongly says the
  designer writes Out for collections. basis=source.
- The package rule today: every `CompositeObjectList` defaults to Out (`:105-107`), pinned in guidance
  (`KB/automation/Clio.Knowledge.Bundle.Tests/CollectionParameterGuidanceTests.cs:26-29`).
- Shipped process-level collections (top-level FJ1 of ProcessSchemaManager schemas): 85 Out, 30 Variable, 12 In, 6
  Internal of 133, none with a Tag. The user-task schemas hold 17 more (6 Out, 9 Variable, 2 In); they are element
  parameters, not process parameters. Shipped file parameters: both
  singles Variable; collections one Variable, one Out. basis=measured (corpus, 2026-10-01).
- A sub-process caller can feed only an In or Variable callee parameter; anything else is refused loudly by
  `EnsureSubProcessTargetCanHoldAValue` (`PB/Mappings/ProcessMappingService.cs:238-269`). basis=source.
- The core does not restrict a PROCESS-level parameter by direction: the direction check of
  `ParameterValuesValidationRule` (`CORE/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:252-286`) returns at
  once for a parameter with no element. The one platform rule is the sub-process element's: it silently clears a
  mapping onto a callee parameter that is not In or Variable (`ProcessSchemaSubProcess.cs:144-183`), which the
  builder turns into the refusal above. basis=source.
- Recount 2026-10-07 with in-process reads: 84 Out, 30 Variable, 12 In, 6 Internal; a mapping inside the same process reads **3 of the 84 Out** and **11 of the 30 Variable** collections (script reads by name are not counted). Out is the shape of a RESULT handed to the caller; a collection filled and then used in the same
  process is Variable, as in the one shipped in-process file case `FileParameterProcess.FileCollection`.
  basis=measured (corpus).

**Options.** File: Variable (`ParseDirection(null)`, `:357-369`); there is no alternative worth costing.
FileCollection: (i) Out, the existing rule for the stored type; (ii) Variable, designer plain-Add parity, which
costs a second default for one stored type, a second rule text and a guide line. D2 makes (ii) implementable;
its cost is a second rule agents must hold, not feasibility.

**Decision (owner, 2026-10-07).** File: Variable, no Tag, no reference schema. FileCollection: **Variable** (ii),
with its item Variable and no Tag. The first recommendation was Out; the in-process recount reversed it, because a
Variable FileCollection is never refused (a caller can fill it and the process can return it). The generic
`Collection` keeps its Out default, so the stored type now has two defaults. The comment at `:103-104` is corrected
(plain Add writes Variable).

**Consequences.**
- Guide sentence: "a FileCollection defaults to Variable: a caller can fill it and the process can return it;
  declare `direction: In` or `Out` only to narrow it. A generic Collection still defaults to Out." A caller's
  `addMapping` onto an explicitly Out callee parameter stays refused loudly, with a message naming the fix.
- Serialization pins: a Variable FileCollection (the default) equals the shipped
  `FileParameterProcess.FileCollection` (under row PT-b of the comparison rule); an explicit Out one equals
  `MarkProcessesToCancel.FilesCollection` (`PS/ProcessLibrary/branches/7.8.0/Schemas/MarkProcessesToCancel/metadata.json:76-91`).
- **Owner decision: Variable, agreed on 2026-10-07.**

## D4. Dotted process-parameter paths

**Context.** `ResolveProcessParameter` is flat-only for both `targetProcessParameter` and `processParameter`
(`PB/Mappings/ProcessMappingService.cs:594-604`, callers `:380, :440`; re-read). Shipped content binds a
process-level item both as a source (`$FileCollection.FileCollectionParameter`) and as a target
(`PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:47, 1440`). Dotted ELEMENT-parameter
paths already work through `ProcessSchemaElementLocator.DescendItemProperties` (`PB/ProcessSchemaElementLocator.cs:166-241`).
basis=source + measured.

**Options.** (a) a separate copy of the dotted grammar for process parameters; (b) generalise
`DescendItemProperties` over a root parameter list and an owner label, and reuse it for both; (c) stay flat-only
and let agents write raw `expression` tokens (no type check).

**Decision: b.** Add `ProcessSchemaElementLocator.ResolveProcessParameterPath(schema, path)`, returning the
parameter and its parent chain, with **no** `ContainerUId` backfill (a process-level item must keep
`[Parameter:{item}]`). Switch both `ResolveProcessParameter` callers.

**Consequences.** `processParameter: "Docs.File"` and `targetProcessParameter: "Docs.File"` become legal, and every
target stays type-checked. An older server answers "Process parameter 'Docs.File' was not found" (loud). Tests:
dotted source and target, a meta path with no element segment, the failing segment named in the error.
**Owner decision: no.**

## D5. The two-level collection binder

**Context.**
- The designer maps the NESTED item. When the source is itself an item of a collection,
  `_processMappingToCollectionItem` also maps the target's parent to the source's parent collection; the chain is
  type-agnostic (`PD/MappingEditMixin/MappingEditMixin.js:895-905, 947-995, 1132-1156`). A plain source goes to
  `_processMappingToItem` and leaves the parent untouched (`:1075-1096`). basis=source.
- Shipped: 11 of 11 file-collection bindings bind both levels; over all bound shipped top-level collections with
  items, 0 of 61 bind the outer level only. basis=measured (corpus, 2026-10-01).
- Runtime: rows of a target with item properties are rebuilt by the TARGET's item names; an unbound item gets
  the type default, null for `IFileLocator`, without a throw
  (`CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:441-488`, re-read). Only a consumer that
  DEREFERENCES the item fails: the Process variant (`PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:72-79`:
  `TryGetValue` returns true with null, then `GetFile(null)`), later the Send email and Creatio.ai slots. Outer-only
  onto the Process variant therefore gives `{File: null}` rows and a NullReferenceException when the source has
  rows; nested-only from a single file gives one row (X1). basis=source.
- A multi-instance sub-process's `InputRecordCollection` items are clones of the callee's In and Variable
  parameters (`CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:336-352`), so a callee with a File parameter
  gives the input collection a FileLocator item. Iterating such a sub-process over Read data rows or over
  `CreatedObjectFileIds` is legal and runs, with that item null on each iteration (shipped:
  `FileCopyProcessPP.SubProcess1` iterates over `CreatedObjectFileIds`, outer only). basis=source + corpus.
- `ProcessMappingService.ApplyMapping` (`PB/Mappings/ProcessMappingService.cs:48-72`) is the one funnel for
  `mappings[]`, `addMapping`, the build, the mirror, the entity-connection binder and the approval applier.
- Every mapping is stored as a Script meta-path formula. An agent can send an `expression` source, and describe
  returns undecoded sources as `value` formulas (D8).
- Today the builder needs two explicit `addMapping` calls, and an outer-only mapping saves green.

**Options.**
1. Explicit dotted mappings only: cheapest, but an outer-only mapping stays a saved-green crash, and agents must
   know the two-level rule.
2. Type-agnostic parent auto-bind (P1), plus item pairing (P2) and refusals limited to FILE-CONSUMING targets,
   all for structured sources only.
3. Type-agnostic P1 and type-agnostic P2 (name pairing for every collection): also changes what an outer mapping
   writes for Read data -> multi-instance flows, which is outside this ticket and unmeasured.

**Decision: option 2, inside `ApplyMapping`, with two scoping rules.**
- **Structured sources only.** The rules below run only when the source is `sourceElement` (+
  `sourceElementParameter`) or `processParameter` (without `sourceColumn`: a record column is not a
  parameter-to-parameter binding). An `expression` source goes to the named target as given: it
  never auto-binds or resets a parent, never pairs items, and is never refused by these rules (the platform
  validates the formula as before). The package does check it in one way: since 1.6.6.79 (ENG-102114) every
  meta-path token must be the path the platform writes for an item of THIS process, so a misspelled token, a token
  from another process and a column of a collection item are refused, and the token's GUIDs are stored lower-cased
  (`PB/Mappings/ProcessMappingService.cs:457-468`; 1.6.6.85 extended the lower-casing). Nested-item tokens such as
  `Files.File` and `ObjectFiles.File` are accepted. Replaying describe output that still carries formulas into the
  same process therefore writes what it names; a replay into another process is refused.
- **File-consuming targets** (for P2 and R-M2): the Process variant's `Files` (ENG-96506 Generated report +
  process parameter modes, also bound by D18); a process-level collection that matches the FileCollection
  predicate (D2), because what it holds is later handed to the former; later, the Send email and Creatio.ai
  attachment slots (ENG-95985 Send email attachments). Every other target, including a multi-instance
  `InputRecordCollection` and any other user task's collection, gets neither P2 nor R-M2.
- Every target is resolved and type-checked before the first value is written.

| Rule | Trigger | Writes | Refusal or notice |
|---|---|---|---|
| **P1** parent auto-bind (type-agnostic, designer parity) | structured source; the target is an item of a collection AND the source is an item of a collection | item <- source item, and the target's parents <- the source's parents, paired innermost first, as many levels as the SOURCE chain has: a deeper TARGET is fine, only the matching inner levels are bound (`Messages.Files.File <- OF1.ObjectFiles.File` binds `Messages.Files` and leaves `Messages`); a parent already bound to its source parent, or the same collection, is left as it is | **refuse** (F-M1) when a paired parent is bound to anything else ("'Files' is bound to 'X'; map 'Files' to 'Y' first: its items follow."; for a parent that does not take files the remedy is "then map '<item>' again"); **refuse** (F-M5) when the SOURCE is nested deeper than the target, because outside its row it reads empty; the same source is a no-op; no notice. Runs after the existing multi-instance guards, and each parent it binds passes the sub-process direction guard; applies to `InputRecordCollection.<F>` too |
| **P2** item pairing (file-consuming targets) | structured outer mapping, both sides collections, the target has a FileLocator item | each target FileLocator item not already bound into the new source <- the paired source item: (1) exact name, case-insensitive, type check passing; (2) else, when both sides have exactly one FileLocator item, pair those (`Files.File <- PF1.ObjectFiles.ObjectFile`); an item that cannot be paired and is bound outside the new source is **reset to None** | a notice (F-M4) for every item bound, rebound, reset or left unpaired; never overwrites an item already bound into the new source (and says nothing about it); a rebind and a reset name the old binding; an unpairable FileLocator item gets "at run time it will be empty". Non-file items are never auto-paired |
| **P2-MI** (notice only) | a structured mapping that binds a multi-instance `InputRecordCollection`, as the named target or as a parent P1 binds, while one of its FileLocator items stays unbound | nothing beyond the planned writes | notice (F-M4): "callee parameter '<F>' will be empty on every iteration unless you map 'InputRecordCollection.<F>'" |
| **P3** plain source on an item | structured source; the target is a FileLocator item; the source is not a collection item; single-instance owner (any element but a multi-instance sub-process, or the process itself: a process-level `Docs.File` counts) | item <- source; EVERY collection above it that is bound is **reset to None** | one notice (F-M4) per reset level ("'Files' was bound to 'X'; it was cleared because 'Files.File' now takes a single file.") |
| **R-M1** | flat FileLocator target (a process parameter or an element input, a single-instance sub-process's File parameter included) <- a collection item (`F <- OF1.ObjectFiles.File`) | - | **refuse** (F-M2: "a single File cannot take an item of 'OF1.ObjectFiles'; use a FileCollection, or a multi-instance sub-process."). **Measured by M6 (2026-10-02):** outside a row context `F` reads null while the source collection holds two files, so the refusal stays |
| **R-M2** | a file-consuming target collection has a FileLocator item and the source collection has none (`Files <- ReadData.ResultCompositeObjectList`) | - | **refuse** (F-M3: the source "has no File item"). `ParameterTypeCompatibility` accepts it today (`PB/Mappings/ParameterTypeCompatibility.cs:255-257`: item shapes are not compared); the runtime is predicted to throw in the consumer |

An explicit API for later consumers (ENG-95985 Send email attachments and the element variants):
`void BindCollection(ProcessSchema schema, ProcessMappingDescriptor outer, IReadOnlyList<(string TargetItem, string SourceItem)> itemPairs)`
on `IProcessMappingService` (`PB/Mappings/IProcessMappingService.cs:13-22`): type-agnostic mechanics; null pairs
mean the P2 policy. PK-PT adds a second entry point, `BindCollectionItems(schema, outer, itemPairs)`: the pairs only,
the outer target's value never written (the mirror's refresh needs it, D9), with the outer target's own guards.

**Consequences.**
- P3 rationale: the designer leaves a stale parent bound, which yields N copies of one file (one per source row);
  there is no `removeMapping` operation (`CLIO/clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs:187`),
  so the caller could not clear it.
- P1 also fixes a silent multi-instance hazard: an item-only `InputRecordCollection.X` binding gives an iteration
  count of 1 today. Two shipped guide passages state that behaviour as measured fact
  (`KB/guidance/mcp/guides/processes/sub-process.md:137-141`, `sub-process-when.md:50-52`); the PT knowledge PR
  rewrites both with a version gate (D25).
- P1 and P2 change what an existing operation writes, so the `[RequiresPackage]` floor moves (D26). The guide
  teaches P1, P2, P3 and the expression rule; tool descriptions do not (D25).
- PBT (`ProcessMappingServiceTests`): P1 on element and process targets; P1 refusal; P2 name pairing and the
  single-FileLocator fallback; P2 never overwrites an item bound into the new source; P2 notices; P3 reset and
  notice; R-M1; R-M2 on the Process variant and on a process-level FileCollection. Regressions: (1) a multi-instance
  sub-process whose callee has a Guid and a File parameter, iterated over `CreatedObjectFileIds` and over Read data
  rows, is ACCEPTED with the P2-MI notice and no item written; (2) the same iteration with explicit outer and
  per-item mappings writes exactly what it writes today; (3) an `expression` onto `Files` and then onto
  `Files.File` writes both as given (GUIDs lower-cased) and nothing else; (4) build -> describe -> replay the described parameters as
  mappings leaves both levels unchanged.
- **Owner decision: agreed on 2026-10-07 as recommended.** P3 resets the stale parent with a notice; P2 and R-M2
  apply to file-consuming targets only.
- **As built in PK-PT** (draft PR https://creatio.ghe.com/engineering/crt-process-builder/pull/96, 2026-10-14;
  source):
  - the Process file element is recognised by its task schema UId (`6c620dd2-...`, mirrored as
    `ProcessDesignConstants.FileProcessing`), so the shared funnel asks the user-task manager nothing on a mapping that
    has no file in it;
  - plan, then commit: every write is planned and every target resolved and type-checked first, so a refused mapping
    assigns no value (items it walked may be adopted: their `ContainerUId` and owning schema are backfilled, as a load
    of the schema would); notices are recorded after the writes;
  - one rule source: the rules are DECIDED in `PB/Mappings/CollectionBindingRules.cs` (static, pure), which the write
    side applies and describe asks (D8), so the two cannot drift apart;
  - `BindCollection` and `BindCollectionItems` as above; `ProcessMappingService` takes `IProcessDesignNotices` in its
    constructor;
  - the texts are those of Appendix A (F-M1..F-M5), quoted as the code writes them;
  - the create path is covered: T-68 was root-caused and fixed in the locator's for-mapping walk (traps T-68).
- **Open item (recorded 2026-10-14, not decided):** a single-instance sub-process's FileCollection-shaped `In`
  parameter is not a file-consuming target as written above, so an outer-only `SP.Docs <- OF1.ObjectFiles` gets
  neither P2 nor R-M2. M3 measured an outer-only binding onto a process parameter only; measurement M27
  ([open-questions](eng-92719-file-processing-element-open-questions.md) B.5) settles whether the callee reads the
  files, and the owner decides afterwards whether the set widens.

## D6. The dotted-mirror bug

**Context.** `AddProcessParameter`'s mirror branch resolves the source through `ResolveElementParameter`, which
accepts dotted paths, then copies the data type verbatim with no allow-list. So
`typeFromElementParameter: "ObjectFiles.File"` silently creates an UNBOUND FileLocator process parameter
(`PB/Parameters/ProcessParameterService.cs:69-83, 402-410`; `PB/ProcessSchemaElementLocator.cs:121-123, 166-241`).
basis=source.

**Options.** (a) flat-only resolution plus a refusal for item paths; (b) (a) plus a type allow-list on every
mirrored type; (c) leave it.

**Decision: a, in PT.** `ResolveTypeSourceParameter` resolves flat only (`FindParameter`, `:247-249`). When the
flat lookup misses and the dotted walk would succeed: "'ObjectFiles.File' is an item of collection
'ObjectFiles' on 'OF1'. A mirror copies a whole parameter: mirror 'ObjectFiles' for its shape, or declare type
File / FileCollection." The re-mirror path (`:282-286`) inherits it. (b) becomes a Sub-task under ENG-95984 File
process parameter type, "Allow-list the data types a typeFromElement mirror may copy", gated on a scan of the
output types shipped user tasks expose, so that no accepted mirror is refused unannounced.

**Consequences.** One PBT test (dotted mirror refused, schema unchanged). **Owner decision: no.**

## D7. Constants, `referenceSchema`, delete guard, `setParameter` shape

All basis=source.

| Case | Today | Change |
|---|---|---|
| `value` on a File (addParameter, setParameter) | stored as a text constant (`PB/Parameters/ProcessParameterValueValidator.cs:213-238`) | refuse: "a File has no constant form; map it from an element's file output or another File parameter". `EnsureNotCollectionConstant` (`ProcessParameterService.cs:425-431`) generalises to "no constant form" |
| `addMapping value` onto a FileLocator or collection target | accepted; the platform's pre-save rule skips constant sources | refuse in `ValidateConstantValue` (`ProcessParameterValueValidator.cs:147-244`), which every constant route passes |
| `referenceSchema` on File or FileCollection | File silently becomes a Lookup | refuse (D2) |
| `removeParameter` of a collection whose ITEM is referenced | missed: `FindParameterUsages` tests the root UId only (`ProcessParameterService.cs:576-647`) | test the root and every item UId, recursively |
| `setParameter type: FileCollection` on a differently shaped collection | silent no-op (same stored type) | refuse: "FileCollection would change this collection's shape; remove it and add it again" |
| `setParameter direction` on File or FileCollection | allowed | unchanged (the remedy in D3) |
| `expression` into a FileLocator or collection target | stored; its meta-path tokens checked by the package since 1.6.6.79, the formula validated by the platform | unchanged, and outside the D5 rules |

Type compatibility needs no change: FileLocator and collections fall through to an exact type match
(`PB/Mappings/ParameterTypeCompatibility.cs:255-257`). **Owner decision: no.**

## D8. Describe of file parameters, nested-only bindings and single-token element sources

**Context.**
- `ToDescribeParameter` already projects `itemProperties` recursively (`PB/Parameters/ProcessParameterService.cs:141-181`).
  clio's `DescribedParameter` declares `itemProperties`, `tag`, `sourceElement`, `sourceElementParameter` and
  `sourceColumn` (`CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:1990-2126`) but has no extension bag, so a NEW
  per-parameter field would be dropped (`CLIO/docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`).
  ENG-99970 (2026-10-07) added `valueOmitted` to it and `filterDecodedCompletely` to `DescribedElement`; both concern
  `DataSourceFilters` only, not file parameters.
- Describe lists a non-output element parameter only when its ROOT value was set in this schema
  (`PB/Describe/ProcessDescriber.cs:208-214`), so a nested-only `Files.File <- $File` binding is invisible.
- Describe decodes a source into `sourceElement` / `sourceElementParameter` / `sourceColumn` only for a
  THREE-segment record-column path (`PB/Contracts/DescribeContracts.cs:1502-1517`); every other source is a `value`
  formula. An agent therefore replays an element-to-element binding as an `expression`, which D5 passes through
  (only the package's meta-path check runs): safe within the same process, but it bypasses the D5 checks on the
  replay.
- A stale MCP clio client strips `itemProperties`, `isOutput` and `isRequired`; a current clio carries them.

**Decision.**
- No new per-parameter fields. File reads back as `FileLocator`; FileCollection as `FileCollection` plus
  `itemProperties` (D2).
- `ProcessDescriber` also lists a non-output element parameter when ANY nested item carries a value stamped with
  this schema (walked like `WithNestedParameters`, `ProcessParameterService.cs:937-971`).
- **Pulled into PT: single-token ELEMENT sources.** When the stored Script is exactly one element-parameter token,
  `[#[Element:{el}].[Parameter:{p}]#]` with `{p}` a top-level parameter or a nested item, describe also reports
  `sourceElement` and `sourceElementParameter` (dotted for a nested item, e.g. `ObjectFiles.File`, named through
  `ProcessSchemaElementLocator.TryNameNestedParameter`, `PB/ProcessSchemaElementLocator.cs:307-319`), with
  `sourceColumn` null. The existing rule holds: these are reported only when the write side, given them back,
  would store this very value. `value` stays, so nothing is lost. **As built in PK-PT** (2026-10-14): "this very
  value" became parity with the write side as a whole. The names are reported only when replaying them changes NO
  other level either: `CollectionBindingRules.ReappliesUnchanged` asks the plan D5's binder would make, and it must
  refuse nothing, bind no P1 parent, reset nothing (P3) and pair or clear no P2 item; any parent write counts. So an
  outer-only `Files` binding (the item unbound) stays a `value`, because replaying it would pair the item. A test
  pins the biconditional (`ElementSourceDecodeTests`: a source is named exactly when replaying it leaves every level
  byte-identical). The describer passes the parameter's owner element and builds one `ProcessElementLookup` per
  describe, so the decode scans no element list per value; it never throws. clio's DTO already declares the members, so
  clio needs no DTO change; the describe tool clause is reworded to cover "a value that IS another element's
  output" (about +40 B). This changes describe output for every element-to-element mapping of every process. It
  is additive (a member that was null now carries a name) and is listed as a visible change in the PR.
- **Stays a Sub-task:** decoding single-token PROCESS-parameter sources into `processParameter` needs a new member
  on clio's bagless `DescribedParameter`. Sub-task under ENG-95984 File process parameter type: "Describe: decode
  process-parameter sources into re-appliable names". Until then those bindings replay as expressions (accepted
  in the same process, because describe reports the canonical token).
- The element block (D19) decodes its own bindings, so OA and RP do not depend on that Sub-task.

**Consequences.** PBT `ProcessDescriberTests`: a `Files` input bound only on `Files.File` is listed; a single-token
element source reports the pair with `sourceColumn` null; a nested-item source reports the dotted name; a
multi-token formula reports none; the build -> describe -> replay round trip leaves both levels unchanged. clio
`ServerProcessDescriberTests`: FileLocator and FileCollection read-back and the decoded pair. e2e: create with File
and FileCollection, describe read-back (D28). **Owner decision: no.**

## D9. The shipped collection mirror binds the outer level only (hypothesis H-1)

**Context.** `BindMirroredCollection` maps root to root only (`PB/Parameters/ProcessParameterService.cs:500-512`).
If the runtime rebuilds rows by the target's own unbound items, every mirrored Read data row reads null
(`CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:441-488, 506-515, 967-971`). Shipped content
never has that shape (0 of 61). basis=source. M3 refuted it on 2026-10-02 (below). Had it held, it would have been
a defect in shipped ENG-96230 Collection process parameter type behaviour, independent of files.

**Options.** (a) fix inside the PT package PR through `BindCollection` with the clone pairs; (b) a separate
Sub-task, fixed first; (c) do nothing until it is reported.

**Decision: measure first (M3). M3 refuted H-1, so the "Refuted" branch applies and contingency X4 fires.**
M3 ran on CrtProcessBuilder 1.6.6.54 on 2026-10-02: 3 iterations, all `M3 name set`
([open-questions](eng-92719-file-processing-element-open-questions.md) C.7). The outer Script mapping copies the whole
collection value, items included, so the unbound item does not matter for reading.
- **Confirmed (not taken):** (b). A new Sub-task MH under ENG-95984 File process parameter type, "typeFromElement collection
  mirror leaves its items unbound", is delivered FIRST as its own package, clio and knowledge triple. It introduces
  `BindCollection` with explicit pairs only, and the mirror calls it with the clone pairs (names are identical by
  construction). PT then adds the P2 policy (null pairs) on top, so no code is thrown away. Reasons: a confirmed
  silent defect in shipped behaviour deserves its own record, revert path and release note; the fix is small
  enough for the fast review lane; and it takes nothing off PT. This revises the first draft of this decision,
  which recommended (a) (see the review log).
- **Refuted (applies):** no MH. Items are still bound (the shipped shape, 60 of 61) as parity, as one commit in PT
  (contingency X4), and the code comment at `ProcessParameterService.cs:440-447` is narrowed. **As built in PK-PT**
  (2026-10-14): the add path AND the re-mirror path (`setParameter` + `typeFromElement`) bind the items. A re-mirror
  writes the collection's own value again only when it is exactly the plain reference to the output (both levels
  through `BindCollection`); any other value its guard admits, such as a caller's formula that reads the output, is
  kept byte for byte, and only the items are bound (`BindCollectionItems`). A refused binding puts the previous items
  back.

**Consequences.** `AddProcessParameter_ShouldBindMirroredCollection_ToSourceOutput` (`PBT/ProcessParameterServiceTests.cs:1762`)
is extended and the test description at `:1703` corrected. The guide sentence that pins the mirror behaviour moves
in the same knowledge PR as the parity commit, KB-PT. **Owner decision: no longer needed.** M3 answered it (Q7).

---

# Part B. ENG-92719 File processing element (sub-tasks OA and RP)

## D10. Element API shape: one token; the variant is the group present

**Context.**
- "Process file" is one palette entry backed by three user-task schemas: ObjectFileProcessingUserTask
  `9387c794-8d84-5925-ab77-c47e7d876286` (Object attachments), ReportFileProcessingUserTask
  `c2bf0416-54c6-6c56-58e0-41162c7795f0` (Generated report), ProcessFileProcessingUserTask
  `6c620dd2-026e-560c-489f-030c5be5f2c3` (Process parameter). The palette offers only the Object schema
  (measured on the stand); "What is the source of the file?" is not a parameter, each option is a schema, and
  changing it replaces the element. basis=source + measured (2026-10-01).
- No element in the package chooses its schema from an option today; the closest templates are
  `UserTaskElementHandler.ResolveUserTaskName` and the `OpenEditPageElementHandler` structure.
- `ResolveBuildType` returns the FIRST matching handler's FIRST token (`PB/Elements/ProcessElementFactory.cs:132-133`);
  every token of a handler's `SupportedTypes` maps to that handler (`:29-37`), so a handler can carry an alias. An
  unknown TYPE token is refused loudly by an older server (`:57-66`); a block riding a known type is silently
  dropped (no `IExtensibleDataObject`, `PB/Contracts/VersionContracts.cs:22-26`).
- `source` already means an object NAME in `readData`, `changeData`, `addData` and `deleteData`
  (`PB/Contracts/ProcessDescriptorContracts.cs:1021, 1047, 1167, 1225`). Every variant needs its own group on
  create anyway (`attachments.object`, `report.printable`, `files`), and on an update the variant is known from
  the stored schema UId.

**Options.**

| Option | Clean-slate cost | Constraint |
|---|---|---|
| a. One token `fileProcessing`, one block, one handler over three schema identities | one handler, one binder, one describe block | matches the designer's single element; a source change is a schema change, refused (D12) |
| b. Three tokens, one per schema | three type-list entries (bytes x3) on a near-exhausted budget; a source change becomes a type change the agent must know | the designer presents one element |
| c. The generic `{type: userTask, userTaskName}` plus a block | no new token | an older server silently drops the block; the generic route can no longer be refused (D13) |

Discriminator, given (a):

| Form | Cost | Constraint |
|---|---|---|
| i. Required `source` enum (`objectAttachments` / `generatedReport` / `processParameter`) | one key | `source` means an object name in four data blocks; `processParameter` collides with `files.processParameter`; agents will write `{source: "Account"}`; redundant with the group |
| ii. No write discriminator: the variant is the single group present; describe emits `source` = the group name, accepted back as an identity check | none | none found |
| iii. A `fileSource` key whose values are the group names | one key | still redundant with the group |

**Decision: a with discriminator ii.**
- Token constant `ElementTypes.FileProcessing = "fileprocessing"` (wire spelling `fileProcessing`), plus a second
  `SupportedTypes` token `"processfile"` for agents who start from the palette caption. `fileprocessing` is the
  first token, so build and describe emit one spelling; tool descriptions name only `fileProcessing`.
- Variant: on create exactly one of `attachments` (Object schema), `report` (Report schema), `files` (Process
  schema) must be present. None: "name the file source: the object in `attachments`, the printable in `report`,
  or the input files in `files`". More than one: refused, naming both. On update, the stored schema UId fixes the
  variant; a group of another variant is a source change (D12).
- `source` on write is an optional identity check (`attachments`, `report` or `files`): equal is accepted, so
  describe output resubmits verbatim; different is refused with the derived value. An entity-like value gets a
  targeted refusal: "'Account' is an object: put it in `attachments.object` and use the `attachments` group".
- `userTaskName` on `type: fileProcessing` is ignored when it resolves to the schema the variant implies, and
  refused when it names another schema.
- The block is gated strictly, like `subProcess` and `scriptTask` (`EnsureBlockMatchesHandler`,
  `ProcessElementFactory.cs:100-113`).
- `FileProcessingElementHandler` is registered BEFORE `UserTaskElementHandler` (`PBA:126-152`) and claims elements
  by schema identity (`PB/Elements/UserTaskSchemaIdentity.cs:21-75`) through a **variant registry**: one collection
  of variants keyed by schema UId, which the handler, the generic-route refusal, the raw-mapping refusal, the
  filter target and describe all read. The registry grows per cut (D13): OA registers the Object variant; RP adds
  the Report and Process variants. This turns "claim, refusal, filter target and describe of a variant land
  together" from a checklist into a structure.
- Why `fileProcessing` over `processFile` as the primary: equal byte cost; it is the common stem of all three
  schema names and of the `SysProcessUserTask` caption "File Processing" (measured), and it cannot be mistaken for
  the Process-parameter variant, whose data-id starts with "processFile". Guide and description call it "the
  Process file element (type fileProcessing)".

**Consequences.**
- `fileProcessing` joins `WritesAConfigurationBlock` (`PB/Graph/ProcessGraphBuilder.cs:442-451`) and the
  `setElement` field list (`PB/Operations/ElementOperations.cs:284-296`).
- clio: `"fileprocessing"` and `"processfile"` join the explicit `EventType.UserTask` arm of `ManagerMap.ResolveDataId`
  (`CLIO/clio/Command/ProcessModel/Schema.cs:1144-1145`; re-read: the arm does not list them today, and the suffix
  arm matches only `...usertask`), with `[TestCase]`s in `CLIO/clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs`.
  This ships in the **PT** clio PR, one release before any package emits the token, so that an older clio does not
  turn a described element into a hard `validate-process-graph` error.
- **Visible change per cut:** `buildType` of existing Object elements changes from `usertask` to `fileprocessing`
  with OA; of Report and Process elements (including the shipped PrintInvoiceReport, PrintQuotationReport,
  PrintContractsReport and GenerateDNSRecordsSpecification) with RP. `userTaskName` stays in describe.
- Tests: the handler-order tripwire (`PBT/CrtProcessBuilderAppTests.cs:139-199`); `processFile` builds the same
  element as `fileProcessing`; a contradicting `userTaskName`, no group, two groups and `source: "Account"` are
  refused; a registered variant gets all of its claims or none.
- **Owner decision: yes.** The token name (`fileProcessing` with alias `processFile`) and the discriminator form
  (ii recommended; iii the alternative).

## D11. The `fileProcessing` block contract

**Context.**
- Vocabulary agents already know: `readData.numberOfRecords` (omitted = read all), `readData.sort {column, direction}`
  (ONE object), `approval.object` + `approval.recordId`, `openEditPage.recordId` with the value sources `value`,
  `processParameter`, `sourceElement` + `sourceElementParameter` (+ `sourceColumn`), `expression` (a formula such
  as `[#SysVariable.CurrentUserContact#]`) (`PB/Contracts/ProcessDescriptorContracts.cs:417-425, 928-975, 1277-1318`).
- Designer labels: "What is the source of the file?", "Which object to receive file from?", "How to filter
  records?", "Read first N records", "How to sort records?", "What to do with file?" (Save to object attachments /
  Use in process), "What object to save file to?" plus a record field, "What report to generate?", "Generate
  separate report for each record", "File name" (a suffix, not the full name), "Files". See
  [serialization-capture](eng-92719-file-processing-element-serialization-capture.md).
- `list-printables` returns `templateId`, `printableCaption` and `convertInPDF` (`CLIO/clio/Command/McpServer/Tools/ListPrintablesTool.cs`).
- Existing blocks never declare describe-only fields on the write contract; the narrow precedent for a write-side
  check is `openEditPage.recordType` ("an optional CHECK, not a selector", `ProcessDescriptorContracts.cs:659`).

**Options.** (a) flat keys at block level; (b) one group per variant (`attachments`, `report`, `files`) plus shared
`action` and `saveTo`. Report identity: an object `{id | name}` vs one string accepting an id, a lookup macro or a
caption (the `EmailTemplateResolver` precedent, `PB/Email/EmailTemplateResolver.cs:40-206`). Record scope: in the
shared `filter` vs in the variant group (D16).

**Decision: groups per variant (b); the variant is the group present (D10); the record scope lives in the group
(D16).**

```jsonc
"fileProcessing": {
  "source": "attachments" | "report" | "files",   // optional identity check (D10); describe always emits it
  "action": "useInProcess" | "saveToAttachments", // optional; create: inferred and always written; update: changes only when sent (D12, D14)

  "attachments": {                       // Object attachments variant
    "object": "Account",                 // the record object whose attachments are read; its file object (AccountFile) is also accepted. Storage derived (D15)
    "recordId": { /* one value source: value | processParameter | sourceElement + sourceElementParameter (+ sourceColumn) | expression */ },  // WHICH record (D16)
    "numberOfRecords": 50,               // RecordsToRead; default 50, max 5000; omitted does NOT read all (unlike readData)
    "sort": { "column": "CreatedOn", "direction": "desc" }   // readData shape; a column of the FILE object (D16)
  },

  "report": {                            // Generated report variant
    "printable": "<templateId>",         // ReportId: list-printables' templateId (preferred), a [#Lookup...#] macro, or a caption (D17)
    "recordId": { /* the same value-source shape */ },   // WHICH record of the report's object (D16)
    "separateReports": true,             // IsSeparateReports; MS Word: written true when omitted (D17)
    "fileNameSuffix": { /* the same value-source shape */ },  // ReportName
    "fileNameSuffixColumn": "Number"     // ReportNameDataSourceColumnUId; a TEXT column of the report object; exclusive with fileNameSuffix
  },

  "files": {                             // Process parameter variant; exactly ONE source (D18)
    "processParameter": "Docs"           // a FileCollection, a File, or a dotted item (Docs.File)
    // or "sourceElement": "OF1", "sourceElementParameter": "ObjectFiles" or "ObjectFiles.File"
    // or "expression": a formula, written as given onto Files.File (meta-path tokens checked; notice)
  },

  "saveTo": {                            // when action = saveToAttachments; required for the files variant
    "object": "Contact",                 // record object (or its file object); storage derived (D15)
    "recordId": { /* the same value-source shape */ },   // ConnectedObjectId; a constant must be an existing record of saveTo.object (D15)
    "linkColumn": "Contact"              // optional override of ConnectedObjectColumnUId (D15)
  }
},
"filter": { }                            // element-level, UNCHANGED FilterDescriptor; attachments and report variants only (D16)
```

Parameter mapping (designer parity; the parameter UIds are in [platform-reference](eng-92719-file-processing-element-platform-reference.md)):

| Key | Element parameter(s) written | How |
|---|---|---|
| (the group present) | the element's `SchemaUId` and `ManagerItemUId` | identity |
| `action` | `ResultActionType` 0 or 1 | constant |
| `attachments.object` | `SourceEntitySchemaUId` (the file object UId); `SourceDataEntitySchemaUId` (the record object in SysFile mode, cleared otherwise) | constant, written directly by the binder from the resolved schema (the generic mapping path skips the block's checks and still accepts a `SysSchema` row Id, T-54; since 1.6.6.69 the binder can check the UId through `ValidateConstantValue`) |
| `attachments.recordId`, `report.recordId` | the scope condition inside `DataSourceFilters` (D16) | compiled by `FileProcessingScopeFilter` |
| `attachments.numberOfRecords` | `RecordsToRead` | constant (the template copy when omitted, D20) |
| `attachments.sort` | `OrderByInfo` `<Col>:<1 asc, 2 desc>:1` | constant |
| `report.printable` | `ReportId` (Guid) | constant |
| `report.separateReports` | `IsSeparateReports` | constant |
| `report.fileNameSuffix` | `ReportName` (localizable text; a constant lands in the resource `BaseElements.<El>.Parameters.ReportName.Value`) | through `ApplyMapping` |
| `report.fileNameSuffixColumn` | `ReportNameDataSourceColumnUId` | constant |
| `files` | `Files` and `Files.File` (D18) | through `ApplyMapping` / `BindCollection` |
| `saveTo.object` | `TargetEntitySchemaUId`, `TargetDataEntitySchemaUId`; `ConnectedObjectId`'s reference = the record object | constant, written directly |
| `saveTo.recordId` | `ConnectedObjectId` | through `ApplyMapping`; a constant is validated as a record of the record object (D15) |
| `saveTo.linkColumn` or derived | `ConnectedObjectColumnUId` | constant |
| `filter` | the remainder of `DataSourceFilters` (D16) | `FileProcessingFilterTarget` |
| (internal) | `ConsiderTimeInFilter`: the template value copied on create (D20) | as the template (Script) |

Rules common to the block:
- A group that does not match the variant is refused ("`report` is only valid on a generated-report element; this
  element reads object attachments").
- `sort` uses the readData shape, resolved by the readData sort codec that OA extracts from `ReadDataConfigBinder`,
  with the element label injected (reuse RF2; D16, D21).
- All four value-source members (`attachments.recordId`, `report.recordId`, `report.fileNameSuffix`,
  `saveTo.recordId`) use ONE shared contract, `FileProcessingValueSourceDescriptor`, with the
  `OpenEditPageRecordDescriptor` member names and the house rule "exactly one source", so one key never means two
  shapes in one element. Its `expression` runs the same length bound and `MetaPathTokenReference` check that
  `openEditPage.recordId.expression` runs since 1.6.6.79 (`PB/Elements/OpenEditPageConfigBinder.cs:874-881`), so the
  new value surfaces do not become the only unchecked ones.
- **Read-only members come in two kinds.**
  - *Identity checks* are declared on the write contract and accepted only when equal to the derived value; a
    different value is refused, naming the derived one: `source`, `attachments.storage`, `attachments.fileObject`,
    `attachments.linkColumn` (refusal: "derived from `attachments.object` ('Account'); only `saveTo.linkColumn` can
    be set"), `saveTo.storage`, `saveTo.fileObject`, `report.object`, `report.type`. They identify what the element
    is, so a stale copy that disagrees must not retarget silently.
  - *Describe-only* members are NOT on the write contract: `actionStored`, `bindingLevels`, `outputs`, `issues`,
    `report.caption`, `report.convertToPdf`, every display value. They legitimately differ between a describe and
    a rebuild (captions in the reader's culture, environment settings, states the rebuild repairs), so "accept when
    equal" would refuse correct resubmissions. WCF drops them today; once ENG-95244 [Arch debt] Proven Solutions:
    give the process descriptor a real schema and wire the R1-R17 graph validator into the write path (ENG-88414)
    (ENG-88414 is AI-driven application development) lands, they are refused as read-back keys. The guide says "remove the describe-only members before
    resubmitting".

**Consequences.** New contracts `FileProcessingDescriptor` (with its group and value-source types) on
`ProcessElementDescriptor` (`PB/Contracts/ProcessDescriptorContracts.cs:13-249`) and `ProcessElementUpdateDescriptor`
(`PB/Contracts/ModifyContracts.cs:318-525`). `FilterDescriptor` does not change, so the other filter targets and
clio's bagless filter DTOs are untouched. If the strict-keys work above lands first, its allow-list learns the new
write members and its read-back list the describe-only members (D26). **Owner decision: yes.** The naming bundle:
`attachments`, `report`, `files`, `saveTo`, the `action` values, `report.printable`, `fileNameSuffix`, `recordId`
inside the groups, and the discriminator form of D10.

## D12. Source immutability and `setElement` semantics

**Context.** The designer replaces the element on a source change (same UId, position and caption; new name; all
values dropped), refuses it while another element's or a process parameter's value references the element, and
asks for confirmation only when the element is configured. The package already has "same value = no-op, different
= remove and add again" for parameter types (`PB/Parameters/ProcessParameterService.cs:232-237`) and a dependency
scanner (`PB/Graph/ProcessElementDependencyScanner.cs:71-147`). Since ENG-100041 Sub-process retarget guard scans the
live schema instead of the request dependency snapshot (merged 2026-10-12, package #95, CrtProcessBuilder 1.6.6.96)
no refusing guard runs that scanner over the live schema any more: every one asks the request's
`IProcessDependencySnapshot` (`PB/Graph/IProcessDependencySnapshot.cs:48-53`), the union of the pre-batch capture and
a live re-scan, so a dependent that an earlier operation of the same batch cleared still counts. The scanner's other
direct callers either report dangling references or, on build, parse one value for a forward reference
(`RefuseForwardReferenceIn`, `PB/Graph/ProcessGraphBuilder.cs:414`, a build-time refusal that is not a dependents
scan), so no refusing dependency guard scans the live schema for dependents. Partial-update precedents: readData
keeps omitted members (an explicit empty `columns` array resets, `ProcessDescriptorContracts.cs:1267-1300`); approval
says "OMITTING it is a partial update, not a default". The designer clears the filter on EVERY source-object change
(`PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:184-202, 228-248`) and on
every report change (`PD/ReportFileProcessingUserTaskPropertiesPage/ReportFileProcessingUserTaskPropertiesPage.js:204-211`).
The package helper `DataSourceFilterValue.ClearIfForeign` keeps a filter whose stored root NAME equals the new
root (`PB/Operations/DataSourceFilterValue.cs:62-85`); in SysFile mode the root is SysFile for every record object,
so it would keep a filter scoped to the OLD object's record while the runtime adds `RecordSchemaName = <new object>`
(`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:36-47`, re-read): zero or wrong files, with
success. basis=source.

**Options.** (a) refuse a source change and point to `removeElement` + `addElement`; (b) replace in place like the
designer (values dropped silently, dangling references the request did not mention); (c) mutate the schema UId in
place (the designer never does this).

**Decision: a.** The refusal names the dependents: "'File1' reads object attachments; its source cannot change.
Remove it and add a new element (these mappings use its outputs: ...)". The dependents come from the request's
dependency snapshot, as in every refusing guard since ENG-100041: `FileProcessingConfigApplier` injects
`IProcessDependencySnapshot` (like `PB/Operations/DeleteDataConfigApplier.cs:22, 33`) and asks
`FindDependents(element)`; the text takes the ENG-100041 wording ("reads from it, or did when this request
started"; "re-point them in a SEPARATE, earlier call"). The check runs in the applier only, never in the create-path
configuration, where the snapshot has not been captured (`FindDependents` throws before `Capture`). basis=source.

`setElement {fileProcessing}` semantics:
1. **Member-wise merge.** Inside `attachments`, `report` and `saveTo`, an omitted member keeps its value; an empty
   string (a name member) or an empty object (`sort`, a value source) clears it where clearing is legal
   (`attachments.recordId`, `attachments.sort`, `report.fileNameSuffix`, `report.fileNameSuffixColumn`,
   `saveTo.linkColumn` = back to the derived column). A value-source member (`attachments.recordId`,
   `report.recordId`, `report.fileNameSuffix`, `saveTo.recordId`, `files`) replaces as a whole when sent. So
   `{attachments: {numberOfRecords: 10}}` keeps the sort, and `{saveTo: {linkColumn: "X"}}` keeps the object and
   the record. WCF cannot tell an explicit null from an omitted member, so null never clears.
2. **Retarget.** *Object:* a change of `attachments.object` that resolves to a different storage pair clears the
   whole stored filter (scope and remainder), the sort, and the record scope unless the same request sends a new
   `attachments.recordId` (designer parity; `ClearIfForeign` is not used for this element). *Report:* a
   `report.printable` change whose report object differs clears the filter, the record scope and
   `fileNameSuffixColumn` (designer parity). When the new printable has the SAME report object, all three are kept
   and the record-scope type check is re-run: a deliberate divergence from the designer, written into the guide,
   because the stored values stay valid for that object. Either way a notice lists what was cleared or kept and
   says "send setFilter after this setElement".
3. A `saveTo.object` change requires `saveTo.recordId` in the same update (the record is typed to the object; the
   `OpenEditPageConfigApplier` precedent, `PB/Operations/OpenEditPageConfigApplier.cs:38-73`).
4. **Action on update.** `ResultActionType` changes only when `action` is sent, or when `saveTo` is sent to an
   element that runs `useInProcess` (it becomes `saveToAttachments`, with a notice). It is never inferred from an
   absent `saveTo`. Switching to `useInProcess` clears all four target fields, including
   `TargetDataEntitySchemaUId` (which the designer forgets, `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:380-389`),
   and is refused while another element maps from `CreatedObjectFileIds`, which would become empty (designer
   `getCanChangeElementConfig`, `ObjectFileProcessingUserTaskPropertiesPage.js:294-303`); the mapping is found with
   the snapshot's `FindDependents(element, parameter)`, not a live scan.
5. **Repair on touch.** Any `setElement fileProcessing` on an element whose stored storage pair is inconsistent
   (D15 `Decode` returns invalid or unpaired, including the H-G3-1 state) rewrites the whole pair from `Resolve`,
   with a notice. A modify never succeeds over a known-throwing pair it touched.

**Consequences.** `FileProcessingConfigApplier` in `PB/Operations/` shares the static binder with the handler's
create path (the OpenEditPage structure). PBT: same-group no-op; another variant's group refused with dependents
named; the same-batch case (clear the dependent mapping, then change the source in one batch: refused, "in a
SEPARATE, earlier call"); member-wise merge per group; legacy retarget; Report same-object printable change keeps
the filter and a different-object change clears it; a partial `setElement` on a saving element keeps `ResultActionType` 0 and the
target; `saveTo` on an in-process element switches the action; repair on touch. The SysFile-to-SysFile retarget
test belongs to SF. **Owner decision: no.**

## D13. The generic `userTask` route and raw configuration writes, per cut

**Context.** `{type: userTask, userTaskName: "ObjectFileProcessingUserTask"}` builds a correctly drawn but
unconfigured element today: no `ResultActionType` (runs as save), no source or target, saved green, throwing at run
time on `GetInstanceByUId(Guid.Empty)`. This still holds on 1.6.6.96 (hypothesis, source): the generic route builds
the same element for the three file schemas; it gained refusals for three other tasks, none of which fires for them,
and the end-of-request configuration ledger (D16) has no rule for them. `list-user-tasks` lists the three schemas
with "pass this as userTaskName on a userTask element" (`PB/Contracts/ListUserTasksContracts.cs:43`). For Send email,
Open edit page and approval the "do not build as a generic userTask" rule is guidance-only
(`KB/guidance/mcp/guides/processes/element-catalog.md:142-152`, which now reads "FOUR user tasks have their own
dedicated build type", Pre-configured page added). Since 2026-10-08 the server refuses three other generic routes.
ENG-102112 create-business-process silently accepts a generic userTask PreconfiguredPageUserTask without the
preconfiguredPage block (package #90, CrtProcessBuilder 1.6.6.88; clio #1777; knowledge #262) refuses the generic
Pre-configured page with or without its block (`RefusePreconfiguredPageOnGenericRoute`,
`PB/Elements/UserTaskElementHandler.cs:86`). ENG-102537 Process elements built without their configuration block
save green and fail or hang at run time (package #91, 1.6.6.91; clio #1778; knowledge #266) refuses Auto-generated
page and User dialog outright (`RefuseUnconfigurablePageTask`, `:87`). So a server refusal of a listed user task is
no longer unique to the file element.
A raw `addMapping value` onto `ResultActionType`, `SourceDataEntitySchemaUId` and the like bypasses every invariant
the binder enforces (for example `ResultActionType = 1` on the Process variant saves green and throws
`NotSupportedException`). Today `DataNodeFilterTarget` accepts `setFilter` on these elements
(`PB/Filters/DataNodeFilterTarget.cs:57-91`), and the shipped product processes contain Report elements only.
basis=source + measured (corpus).

**Options.** (a) refuse the generic route; (b) silently redirect it to `fileProcessing` (changes an existing
request's meaning); (c) leave it.

**Decision: a, scoped to the cut that makes the variant configurable,** so no release leaves a variant with no
route at all.
- OA refuses the generic route only for ObjectFileProcessingUserTask, in `UserTaskElementHandler.Create` after
  `FindInstanceByName` (`PB/Elements/UserTaskElementHandler.cs:82-85`) and the two existing refusals (`:86-87`),
  before the element is created (`:88`): a third `Refuse...` call, or one folded "refuse a route that cannot
  configure" step. It is keyed on the RESOLVED schema UId (it covers case, whitespace and the
  `performTask + userTaskName` alias): "'ObjectFileProcessingUserTask' is the Process file element; build it with
  type 'fileProcessing' and an `attachments` group. list-user-tasks lists it because it is a palette task." Report
  and Process schemas keep today's generic route, generic describe and `DataNodeFilterTarget` until RP. The
  justification is the precedent's: the typed route requires its block (D10 refuses a `fileProcessing` create with
  no variant group), so the generic route is the only blockless one.
- RP adds the same refusal for the other two schemas (naming `report` and `files`).
- `addMapping` onto the binder-owned configuration parameters of a claimed schema (`SourceEntitySchemaUId`,
  `SourceDataEntitySchemaUId`, `TargetEntitySchemaUId`, `TargetDataEntitySchemaUId`, `ConnectedObjectColumnUId`,
  `ResultActionType`, `RecordsToRead`, `OrderByInfo`, `ReportId`, `IsSeparateReports`,
  `ReportNameDataSourceColumnUId`, `DataSourceFilters`, `ConsiderTimeInFilter`) is refused, pointing to
  `setElement fileProcessing`, with the same per-cut scope. `addMapping` onto the value inputs (`Files`,
  `Files.File`, `ConnectedObjectId`, `ReportName`) stays legal and runs the same rules as the block (D5, D18).
  Since 1.6.6.69 raw `addMapping` writes a correct schema UId onto the four storage parameters (T-54), so this
  refusal takes away a route that works. It stays because that route bypasses the block's checks (storage pair,
  link column, D12's clearing) and still accepts a `SysSchema` row Id.
- Precedents in the package: ENG-102113 (1.6.6.69) already refuses block-owned targets inside `ApplyMapping`:
  `EnsureNotAnOpenEditPageObjectConstant` and, for the data elements' objects, `FindDataObjectSlot` +
  `EnsureTheDataObjectIsKept` (keep-only, the block route named in the refusal), keyed through
  `UserTaskSchemaIdentity` (`PB/Mappings/ProcessMappingService.cs:96-111, 136-183, 203-226`). F-E2 extends that slot
  table instead of adding a second mechanism. ENG-102112 (merged 2026-10-08, package #90, CrtProcessBuilder
  1.6.6.88) refuses the generic Pre-configured page at `UserTaskElementHandler.cs:86`, keyed on the resolved
  `taskSchema.UId`, create-only, and ENG-102537 added the second refusal at `:87`. F-E1 copies that shape: its
  message (quoted above) also names the element and the type used, and ends with the `setElement` repair; the tests
  cover the plain name, another name resolving to the same UId, and a "tolerated" case for the Report and
  Process schemas until RP (`PBT/UserTaskElementHandlerCreateTests.cs:356-489` is the pattern). OA.5 rebases onto
  merged code; there is no merge conflict. Design note (hypothesis about cost): the precedent holds
  `UserTaskSchemaIdentity` fields built from the connection, so its constructor did not change. Injecting the variant
  registry (D10) into `UserTaskElementHandler` changes the constructor (`PBA:152-153` plus 39 direct constructions in
  16 PBT files); either OA.5 prices that in, or the handler builds the registry from the connection as it builds
  the identities.
- `setFilter` on a claimed element goes through the new filter target (D16).

**Consequences.** The PT e2e uses no generic file-processing route (D28). The OA knowledge PR adds the element to the
element-catalog sentence "FOUR user tasks have their own dedicated build type" and says the server refuses the
generic route for it, as it does for the Pre-configured page. Making it a fifth dedicated type turns FOUR into FIVE
together with the three knowledge tests that pin FOUR (read at knowledge `0b56d4a`):
`ProcessDigestTests.TheCardsDedicatedTypes_ShouldEqualTheCatalogsFour` (`HaveCount(4)`, `ProcessDigestTests.cs:381`);
the `RulesHandedBackToTheirOwners` tuple at `ProcessDigestTests.cs:128`, which holds the exact sentence "FOUR user
tasks have their own dedicated build type and must NOT be built as a generic `userTask`"; and
`ProcessPreconfiguredPageCatalogGuidanceTests.cs:20`. The `process-digest` card's "(never as a userTask)" list must
stay equal to the catalog, so it must list `fileProcessing`; the card has about 3 bytes free (hypothesis, test
source), so the token is paid for by a trim on the card (inference). All of it lands in the same knowledge PR. The
clio PR of the same cut gives `ListUserTasksPrompt`, `CreateBusinessProcessPrompt` and the `list-user-tasks`
description the same per-cut exception (D25). **Owner decision: no.**

## D14. `ResultActionType`

**Context.**
- `SaveToFiles = 0`, `UseInProcess = 1` (`PD/FileProcessing/FileProcessing.cs:19-30`, re-read); the designer writes a
  constant "0" or "1".
- Object and Report produce output collections for BOTH values; 0 also saves. Object after 0: `ObjectFiles` holds
  the SOURCE locators, and the copies are only in `CreatedObjectFileIds`
  (`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:71-87`, re-read). Object after 1: `ObjectFiles` holds
  locators of EXISTING attachment rows, which outlive the process. Report after 1: temporary `SysProcessFile` rows
  deleted when the owning process completes, `CreatedObjectFileIds` empty
  (`PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:98-112`).
- Process variant: only 0; 1 throws `NotSupportedException`
  (`PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:89-97`, re-read); its page hides the control
  and forces "0".
- Designer default for Object and Report: 1 (`PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:11-12, 894-903`,
  re-read). No metadata default exists: an unset value runs as 0. basis=source.

**Options.** (1) the designer default: omitted `action` means use in process, so a caller who supplied `saveTo` but
forgot `action` gets a silent no-save; (2) require `action` on create: one more mandatory key and refusal round
trips; (3) infer on create from `saveTo`: present means save, absent means use in process (the designer default);
contradictions refused; always written.

**Decision: 3, on create only.**
- Object and Report: `action` optional and inferred as above; `useInProcess` with `saveTo` refused;
  `saveToAttachments` without `saveTo` refused.
- Process variant: `action` omitted or `saveToAttachments`; `useInProcess` refused ("the Process parameter source
  can only save files to a record; the platform throws for 'use in process'"); `saveTo` required.
- Update: D12 rule 4.
- `ResultActionType` is ALWAYS written explicitly on create, for every variant.

**Consequences.**
- Describe reports `action` and the describe-only `actionStored: false` when the stored value is unset ("runs as
  saveToAttachments").
- Notice for the **Report** variant only, when a `useInProcess` element's `ReportFiles` flow into an Out process
  parameter: "these generated files are temporary: they live as long as the process instance that runs this
  element (a sub-process's files are deleted when the sub-process completes); consume them in this process or save
  them". **Pending M16** for the lifetime wording. The Object variant gets no notice; the guide says its
  `useInProcess` output points at the source attachments.
- **Owner decision: yes.** Inference on create (recommended), the designer default, or a mandatory `action`.

## D15. The attachment-storage resolver

**Context.**
- With `UseSysFileInObjectFileProcessing` on (code default true; on the stand; no `Feature` row; measured
  2026-10-01), the designer chooses storage PER ENTITY by name: a record object X with an eligible BaseFile
  descendant named `<X>File` (Lead: `FileLead`) keeps the legacy encoding (`*EntitySchemaUId = <X>File`,
  `ConnectedObjectColumnUId` = the column named X that references X, `*DataEntitySchemaUId` cleared); every other
  eligible object uses SysFile `70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a` + `RecordId` + `*DataEntitySchemaUId` = the
  record object. basis=source + measured in the designer, in memory (UO-2 to UO-4, 2026-10-01).
- The server runtime never reads the flag; both encodings run. 22 of 22 product Freedom UI pages with an
  attachment list store in the dedicated `<X>File`. basis=source.
- **The SysFile path has no runtime evidence.** 0 of 30 shipped elements use it. READ adds `Type = File` and,
  when `SourceDataEntitySchemaUId` is set, `RecordSchemaName = <record object>`
  (`PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:36-47`, re-read), so a Freedom UI upload must stamp
  both for the read to find it (M8). WRITE writes the link column and writes `RecordSchemaName` only when
  `TargetDataEntitySchemaUId` resolves (`PD/FileProcessing/FileProcessing.cs:152-179`). Whether a written SysFile row
  carries `Type = File` and appears in the record's Freedom UI attachment list is unmeasured (M23).
- Hazards (basis=source): cross-schema copy with no link column -> linked to nothing; same-schema -> a duplicate
  on the source record (X4); an empty `ConnectedObjectId` -> a foreign-key failure on a legacy target, a silent
  orphan on SysFile; `SourceDataEntitySchemaUId` on a legacy source -> a `RecordSchemaName` filter on a file object
  that has no such column. Details in [traps](eng-92719-file-processing-element-traps.md).
- **H-G3-1, a designer-side hazard.** With the flag on, opening a legacy Object element fills the
  `SourceDataEntitySchemaUId` attribute from `SourceEntitySchemaUId` (`BaseFileProcessingUserTaskPropertiesPage.js:910-922`),
  and a later save writes it unless an attribute set only in the change handler is set
  (`ObjectFileProcessingUserTaskPropertiesPage.js:186, 319-328, 379-391`; Base `:853-861`). So an unchanged
  open-and-save of a legacy Object element would store `SourceDataEntitySchemaUId = AccountFile`, and the runtime
  would filter `AccountFile.RecordSchemaName`: a throw. basis=source; **pending M11**.
- Core's `FileSchemaProvider` follows a different rule and lives in `Terrasoft.Configuration`: not reusable.
- Reading the flag: `UserConnection.GetIsFeatureEnabled` is configuration code with a DB-only fallback that reads
  OFF on the stand; `Creatio.FeatureToggling.Features.GetIsEnabled(string)` is reachable and lets the code default
  win (predicted true on the stand; M25).
- `ConnectedObjectId` is Guid-typed in all three templates, so the shared constant validator checks the format only
  (`PB/Parameters/ProcessParameterValueValidator.cs:230-232`); the designer edits it as a LOOKUP of the record object
  and the runtime writes it straight into the link column (`FileProcessing.cs:193-201`).

**Options.**
- Flag: A. don't read it, always run the designer's ON algorithm; B. read it and reproduce the OFF list (a copy of
  `ModuleStructure` and a string-coupled feature read); C. read it only for a notice.
- Orphan file objects and link columns: (i) refuse a target without a designer link column unless `linkColumn`
  names a lookup column; (ii) also allow `linkColumn: "none"`; (iii) accept silently.
- `storage: "sysFile"` for an object that has a dedicated file object: allow or refuse in v1.
- Where SysFile ships: (1) inside OA, pending M8 and M23 with a fallback; (2) in a separate Sub-task SF after the
  measurements, with OA refusing SysFile sources and targets meanwhile.

**Decision.**
- `IAttachmentStorageResolver` with `Resolve(objectName, linkColumnName, role)` and `Decode(...)`, over a thin
  `IEntitySchemaHierarchyReader` adapter (`SchemaManager.GetAllParents` is non-virtual and cannot be substituted),
  following the designer's algorithm (see [platform-reference](eng-92719-file-processing-element-platform-reference.md)).
  Eligibility: a base (non-replacement) object, not virtual, not a database view, whose direct parent is not
  BaseEntityInTag, BaseFolder, BaseItemInFolder or BaseTag. Deterministic: independent of
  `GetAvailableEntitySchemas`, web.config and the caller's rights. `Resolve` and `Decode` are complete in OA (both
  modes), so describe can report designer-built SysFile elements.
- **SysFile goes to Sub-task SF (option 2).** OA refuses SysFile-mode sources and targets with an additive refusal:
  "reading or saving the attachments of 'UsrX' needs SysFile storage, which this version does not support yet;
  use an object with its own attachment object, or use the files in the process". Designer-built SysFile elements
  are described without loss, and `setElement` and `setFilter` on them are refused until SF. SF lifts the refusal
  per variant after M7, M8, M21, M23 and M25 pass. If M23 fails, SF ships SysFile sources only (if M8 passed) and
  keeps the target refusal. Reason: the five SysFile measurements include three that need the user to build
  designer probes; inside OA they would sit on its critical path, and the two high-severity review findings that
  are SysFile-only (A1, A2) would travel with the first element release.
- Flag: **A for behaviour, plus C's notice.** The runtime never reads the flag, the dedicated-storage output is
  identical in both modes, and the package already declines to refuse what the runtime accepts
  (`PB/Elements/OpenEditPageCandidateReader.cs:58-62`). In SF, when `Features.GetIsEnabled(<code name>)` reads OFF, a
  SysFile-mode element built or reconfigured in the request gets a notice ("this environment's designer cannot
  display SysFile attachment storage; the element runs, but cannot be re-saved in the designer without
  reconfiguring"), never a refusal, behind the one-method feature seam shared with D17. The notice ships only after
  M25 confirms that the read returns true on the stand.
- Link column: **(i)**. No `"none"` (X4: it does not mean "unlinked" for a same-schema copy). An explicit
  `linkColumn` that differs from the designer's column is accepted with warning W6 ("opening and saving this
  element in the designer will clear it", `BaseFileProcessingUserTaskPropertiesPage.js:668`). This also repairs the
  shipped GenerateDNSRecordsSpecification shape (DNSGuideFile has no column named after its record; the real link is
  `SenderDomain`): `saveTo {object: "DNSGuideFile", linkColumn: "SenderDomain"}`.
- `storage: "sysFile"` override: not in v1; `storage` is an identity check (D11). Revisit after M12.
- `saveTo.recordId` constant: validated as a LOOKUP of the record object: it must parse, must not be the empty
  Guid, and the record must exist (`EnsureReferencedRecordExists` through a new internal entry point of
  `ProcessParameterValueValidator`; the display value comes from it). The bare-Guid encoding is kept. A mapped
  `recordId` gets a notice: "an empty value at run time fails a legacy target (foreign key) and orphans a SysFile
  target".
- Refusals and warnings:

| Id | Case |
|---|---|
| R1 | object not found |
| R2 | `File` (the abstract base) named as the object |
| R3 | SysFile named directly ("name the object the files belong to") |
| R4 | an object that inherits SysFile |
| R5 | a virtual object, a database view, or a tag or folder object |
| R6 | a record object with more than one attachment object (pass one of them) |
| R7 | no SysFile object on the environment and no dedicated file object |
| R8 | a target file object with no column that references the record ("pass `linkColumn`: one of the lookup columns"; the "none" suggestion is dropped) |
| R9 | `linkColumn` is not a lookup column of the file object |
| W6 | `linkColumn` differs from the designer's column |
| W7 | the record object also has files in an unpaired file object |
| (OA only) | SysFile source or target refused until SF |

- Invariants: never write `SourceDataEntitySchemaUId` with a legacy source; always write `*DataEntitySchemaUId` in
  SysFile mode; rewrite the WHOLE storage pair from `Resolve` whenever a request writes the block (D12 rule 5).
- H-G3-1, if M11 confirms it: (a) a Sub-task under ENG-92719 File processing element to file and track a
  CrtProcessDesigner bug report; (b) D12 rule 5 repairs a touched element; (c) a guide warning: "on this platform
  version, opening and saving in the designer an Object element that reads a dedicated attachment object
  (AccountFile, ContactFile, ...) can make it fail at run time; re-apply `setElement fileProcessing` to repair it";
  (d) M22 covers it.

**Consequences.**
- Describe decodes stored pairs with `Decode` (never throws) and reports `storage` as dedicated, sysFile, unpaired
  or invalid, with `issues` for inconsistent pairs (the H-G3-1 source half "throws at run time", pending M11; the
  target half "cosmetic").
- Tests: a fake hierarchy reader covering AccountFile/Account, FileLead/Lead, a LeadFile + FileLead ambiguity, the
  DNSGuideFile orphan, a database-view record, an object in the SysFile family, a dedicated object without a
  conforming link column, a self-named file object, a tag-style object, an object with no claimant, SysFile
  missing; `recordId` constant unknown, empty, of another object, and existing; the OA SysFile refusal switch; in SF,
  the flag notice through a faked seam.
- **Owner decision: yes.** Flag not read for behaviour (A + notice); no `linkColumn: "none"`; no SysFile override in
  v1; SysFile in Sub-task SF, with one interim release that refuses SysFile-mode sources and targets.

## D16. Filter and sort roots, the record scope, the empty-filter policy

**Context.**
- The Object variant's runtime root is `SourceEntitySchemaUId` (the legacy `<X>File` or SysFile); the stored
  `rootSchemaName` is ignored. The Report runtime root is the report's entity (`SysModuleReport.SysEntitySchema`,
  else the section entity). basis=source.
- Today `DataNodeFilterTarget` claims these elements and roots the filter on whatever `filter.object` the caller
  sends, unchecked (`PB/Filters/DataNodeFilterTarget.cs:57-91`; `PB/Operations/DataSourceFilterValue.cs:16-18`). The
  natural phrasing `{object: "Account", Id = X}` evaluates as `AccountFile.Id = <account id>`: zero files, success.
- In SysFile mode a record-object column absent from SysFile throws; one SysFile shares (`Id`, `Name`, `CreatedOn`)
  silently applies to the FILE. The designer offers record-object sort columns after the source pick (a designer
  defect; UO-1 shows `GPS E` offered for Account address).
- In SysFile mode the designer edits `SysFile.RecordId` as a LOOKUP of the record object
  (`ObjectFileProcessingUserTaskPropertiesPage.js:228-248, 420-441`), so it most likely writes the same lookup
  InFilter shape as the legacy link-column condition. The runtime accepts either form. basis=source; M7 captures it.
- Empty filter: Object reads up to `RecordsToRead` arbitrary files (and, when saving, copies them onto the target);
  a Word report generates one document per record of the whole entity, uncapped. basis=source.
- Since ENG-102537 one element-configuration ledger evaluates every element the request touched once, at the END of
  the request, because `setFilter` can follow `addElement` in a batch (`IElementConfigurationLedger`,
  `PB/Elements/IElementConfigurationLedger.cs:31`; `Track` `ElementConfigurationLedger.cs:59`, `Reconcile` `:75`;
  registered at `PBA:341-342`). Its rules live in `PB/Elements/ElementConfigurationNotices.cs` (`Rules`, keyed by
  task NAME, `:40-50`, eight rules, none for the three file schemas); the Delete data "has a selecting filter"
  predicate is now `ElementConfigurationNotices.HasSelectingFilter` (`:248-259`), shared with Modify data, and
  "present" means `ProcessSchemaElementLocator.HasEffectiveValue`. Tracking is kind-agnostic at every touch site
  (addElement, setElement, setFilter / clearFilter, addMapping onto an element target, setElement.deleteData, build),
  so a file element is already tracked. Two properties matter here. It raises a NOTICE, never a refusal, by owner
  decision in ENG-102537 (`IElementConfigurationLedger.cs:22-27`: creating a bare element and configuring it later
  is a supported flow). And it reconciles AFTER the pre-save gate (modify `PB/Design/ProcessEditPipeline.cs:158`,
  build `PB/Design/ProcessBuildHandler.cs:178`; both orders pinned by tests). basis=source.
- clio's filter DTOs carry no `[JsonExtensionData]` bag (`CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:1724-1746`);
  `DescribedElement` does (`:822-829`), so an undeclared element block survives any clio.
- The filter right-hand vocabulary is `value`, `processParameter`, `elementParameter {elementName, parameter, column}`,
  `expression` (a bare meta-path token; since 1.6.6.75, ENG-102110, validated: only the canonical bare path of a ROOT
  process parameter, a top-level element's root parameter or one column of its record,
  `PB/Filters/FilterExpressionReference.cs:74-92`) and `macro` (`PB/Contracts/FilterContracts.cs:108-165`); every other
  `recordId` uses the value-source vocabulary, where `expression` is a formula. `CurrentUserContact` is a macro in
  the filter grammar (`PB/Filters/MacrosCatalog.cs:67`) and the formula `[#SysVariable.CurrentUserContact#]` in the
  value-source one.

**Options.**
- Record scope: R1 `filter.recordId` in the filter vocabulary (a second meaning of `recordId` and of `expression`
  in one element, and a member clio's bagless filter DTO drops on describe); R2 `attachments.recordId` /
  `report.recordId` in the value-source vocabulary (two writers of one `DataSourceFilters`, reconciled by one
  helper); R3 a pseudo-column `$record`; R4 no sugar (the agent must know the storage rule and the link column).
- Empty filter: refuse the high-blast-radius cases, or notice everywhere.
- `filter.object` = the record object: alias it to the file object, or refuse.

**Decision.**
- **R2.** `attachments.recordId` (Object) and `report.recordId` (Report), in the shared value-source contract
  (D11). One helper, `FileProcessingScopeFilter`, owns the scope condition: `Split(stored) -> (scope, remainder)` and
  `Join(scope, remainder)`. The block binder writes `Join(newScope, storedRemainder)`; `FileProcessingFilterTarget`
  writes `Join(storedScope, newRemainder)`; describe uses `Split`. `FilterDescriptor` stays unchanged.
- Scope compile by source: `value` -> a constant; `processParameter` -> the condition's `processParameter`;
  `sourceElement + sourceElementParameter (+ sourceColumn)` -> the condition's `elementParameter`; `expression` ->
  only `[#SysVariable.CurrentUserContact#]`, compiled to the `CurrentUserContact` macro; any other formula is
  refused ("a record scope compiles into a filter, which takes one value: a record id, a process parameter, an
  element output (optionally one column of it), or [#SysVariable.CurrentUserContact#]").
- Scope condition shape: legacy `<link column> = value` as a lookup InFilter (the corpus shape); Report `Id = value`
  (the product shape); SysFile (in SF) the shape M7 captures, defaulting to the same lookup InFilter on `RecordId`
  with the record object as its reference, and `Split` accepts both SysFile shapes. A caller's root `or` remainder
  is wrapped as `AND(scope, OR(...))`.
- Type check: a Lookup source must reference the record object (Object) or the report entity (Report); a Guid is
  accepted; a constant must be an existing record of that object; `CurrentUserContact` only when that object is
  Contact.
- The same scope set twice is refused ("keep `attachments.recordId` and remove the condition on 'Contact' from the
  filter").
- New `FileProcessingFilterTarget` (priority 50, between Signal 100 and DataNode 0, registered at `PBA:175-180`),
  claiming through the variant registry per cut: Object in OA, Report added in RP.
- Root rule: an omitted `filter.object` is the runtime root; a different value is refused, naming the root and
  (Object) the record object, pointing to `attachments.recordId`. No alias.
- A filter on an element whose source object or report is not configured is refused ("configure fileProcessing
  first; in a batch, setElement before setFilter").
- Sort: the readData shape `{column, direction}`, ONE direct column of the runtime root (paths refused), resolved by
  the readData sort codec extracted from `ReadDataConfigBinder`. Its `private static` `ResolveOrderInfoValue` /
  `ParseSortDirection` and `DescribeSort` / `DescribeSortDirection` (`PB/Elements/ReadDataConfigBinder.cs:793-825,
  917-961`) move into a shared internal codec that takes the element label, and the Read data refusal texts stay
  byte-identical (reuse RF2; plan OA.4). A stored multi-entry `OrderByInfo` is kept while `sort` is
  not sent, and describe reports its primary entry with an issue naming the hidden ones. The designer's SysFile
  record-object sort list is NOT mirrored. `sort` on Report is refused (no `OrderByInfo`). M24 measures what an
  ARRAY sent into this object member does at the WCF binder, and the guide pins the result.
- `numberOfRecords` 1..5000 (the designer's range); omitted = 50, the designer default, NOT "read all" as in
  readData. When it is omitted on create and the element saves or forwards files in the same request: notice
  "reads at most 50 files (numberOfRecords defaults to 50, max 5000)".
- **Empty-filter policy**, evaluated at the END of the request, for elements created or reconfigured in this
  request only (an untouched designer element with an empty filter, such as the shipped `CRM60006PP`, is never
  refused). "Selecting" = a record scope, or a remainder with at least one condition. **Refuse** Object +
  `saveToAttachments` with no selecting filter; **refuse** Report (Word) with no selecting filter; **notice** Object
  + `useInProcess` ("reads up to N arbitrary files"). The refusal carries `failedOperationIndex: null`.
  - The NOTICE is one `Rules` entry in `ElementConfigurationNotices` plus a `Describe...` method, reusing
    `HasSelectingFilter` (the record scope is stored inside `DataSourceFilters`, so it counts) and
    `HasEffectiveValue`: no new tracking, no new reconcile site. Per cut, OA adds the Object rule and RP the other
    two; a rule for an element left unconfigured rides the same entry.
  - The REFUSALS cannot ride that ledger, which never refuses by owner decision (ENG-102537), reconciles after the
    gate and exposes no touched-set query (reuse RF3). They need a new PRE-gate check: modify next to
    `EnsureFormulasStillBelongOnTouchedConnectors` (`PB/Design/ProcessEditPipeline.cs:130-133`), build at the end of
    `ApplyDeclarativeContent` (`PB/Design/ProcessBuildHandler.cs:583`), with their own record of touched elements
    (the planned `IFileProcessingFilterLedger`, or a read-only tracked-set accessor added to
    `IElementConfigurationLedger`, which changes its contract).
  - The refusal names the element and the operations that touched it, and says "add `attachments.recordId`
    (`report.recordId`) or setFilter in the same request".
  - Precedent to weigh (Q16): for the analogous Delete data / Modify data empty-filter hazard, ENG-102537's owner
    decision is "a NOTICE, never a refusal" (`PB/Elements/IElementConfigurationLedger.cs:22-27`). The refusal is
    still argued here: saving copies arbitrary files onto a record (a harmful action), and a Word report per record
    of the whole entity is a mass side effect, not a run-time failure.
- `setFilter` on the Process variant stays refused, and the message (`PB/Filters/ProcessFilterApplier.cs:66-72`)
  lists the Process file variants that do take a filter, per cut.
- Out of v1: "attachments of the records matching a condition" in SysFile mode (an unmeasured reverse join). In
  legacy mode it works through the link lookup (`Account.Type = ...`).

**Consequences.**
- Describe: `filter.object` = the runtime root; the block reports `recordId` when `Split` finds exactly one top-level
  equality on the scope column of an AND root whose right-hand side decodes to one value source, and the described
  filter then omits that condition; an `AND(scope, OR)` unwraps back. A scope condition that does not decode stays in
  `filter.conditions`, and no `recordId` is reported, so nothing is lost. The lift also applies to designer-made
  legacy filters, so describe output resubmits stably.
- Since 1.6.6.82 (ENG-99970) `FilterDescriptorReader` stamps `DecodedCompletely` and describe reports
  `filterDecodedCompletely` (`PB/Filters/FilterDescriptorReader.cs:57-97`), and `BuildFilterValue` writes `isNull` on
  every is-null leaf (`PB/Filters/ProcessFilterService.cs:265-273`). `Split` lifts no `recordId` from a lossy decode,
  and `Join` keeps the `isNull` flag of the remainder it re-writes.
- Describe issue (OA, for designer-built SysFile elements): a SysFile scope whose `RecordId` lookup reference
  differs from `SourceDataEntitySchemaUId` ("the record scope refers to 'Contact' records but the element reads
  attachments of 'UsrProject'; it reads no files").
- Tests: scope compile per source and the refused formula; legacy InFilter; Report `Id`; root rule, unconfigured
  element, type check; scope set twice; sort; OR wrap and unwrap; corpus filter lift; each writer keeps the other's
  half; a lossy decode is not lifted; `isNull` survives `Join`; empty-filter refusal and notice for touched elements
  only; the `numberOfRecords` notice.
- **Owner decision: yes.** The R2 shape (`recordId` inside the groups); refuse (recommended) or notice for the
  empty-filter cases (Q16, to be answered knowing the ENG-102537 "a NOTICE, never a refusal" precedent above).

## D17. Generated report rules

**Context.**
- `ReportId` = `SysModuleReport.Id`; the designer lists every printable the user can read, of every type, by
  caption. Product printables have the same id on every environment; duplicate captions ship ("Contract" in two
  packages). basis=source.
- Dispatch by `Type.Name`: "MS Word" and "FastReport" run; DevExpress throws `NotSupportedException`; an unknown or
  unreadable id throws `ArgumentException`. basis=source.
- MS Word: always one file per record (`IsSeparateReports` changes naming only); the designer forces the checkbox on
  and disabled. FastReport: always PDF; separate decides one or one per record; its process path resolves the
  generator without a user connection, and the stand has no FastReport assemblies or printables (unverified,
  possibly broken).
- PDF from Word only when `ConvertInPDF` is on AND a converter package is installed; otherwise silently `.docx`. On
  the stand always `.docx` (measured: `ConvertInPDF = false` on all 5 printables).
- File name: caption, then ". ReportName" if no name column, then " (i)" or ". column value" if separate, then
  the extension (pinned by the platform's own `ReportFileProcessingUserTask_Tests.cs:214-319`). A lookup name column
  is predicted to throw; the designer offers TEXT columns only, and only with separate on.
- Save: every generated file goes to the ONE `ConnectedObjectId` record.
- The stand: 5 MS Word printables (Contract, DNS Requirements, DNS Specifications, Invoice, Quotation); none for
  Account or Contact. basis=measured (2026-10-01).
- `EnableReportFileProcessingUserTask` only shows the source selector in the designer; the server never reads it;
  shipped ON. basis=measured + source.

**Options.** Discovery: a new report-lookup MCP tool, or server-side resolution inside the block with
`list-printables` as the discovery hint. FastReport: (A) accept with a notice; (B) refuse until M15 shows it works.
"The report must exist": refuse with candidates, or pick the closest. Word `separateReports: false`: refuse, or
accept with a notice.

**Decision.**
- `IReportTemplateReader` (ESQ on `SysModuleReport` with `UseAdminRights = false`, the runtime's own query and its
  entity fallback). `report.printable` accepts the id (preferred: "pass list-printables' `templateId` as
  `report.printable`"), the `[#Lookup...#]` macro, or the caption. An ambiguous caption is narrowed by
  `report.recordId`'s object when exactly one candidate matches; otherwise refused with every candidate's id, type
  and object.
- Refusals: a missing printable ("`report.printable` is required: pass a `templateId` from list-printables"); empty
  Guid; unknown or unreadable ("No printable 'X' is available. The Process file element cannot create one. Ask the
  user to create it in System Designer -> Report setup, then retry." plus up to N printables for the object, by
  `templateId`); DevExpress and any other type; **FastReport refused in this work (B)**, additive to lift later; a
  printable with no object; `fileNameSuffixColumn` with separate off, not a text column of the report object, or
  together with `fileNameSuffix`.
- `separateReports`: Word is written true when omitted; an explicit `false` on Word is ACCEPTED as given, with a
  notice ("the designer forces this on at the next save; the file names will change"), so a describe of a legacy
  element resubmits.
- A `fileNameSuffix` constant is written through the localizable resource path (designer parity); a formula inline.
- Notices: `ConvertInPDF` on ("PDF only when an MS Word -> PDF converter package is installed; otherwise .docx");
  save with a scope or filter that can match more than one record ("all N files are attached to that one record");
  `EnableReportFileProcessingUserTask` OFF ("this environment's designer hides the source selector; the element
  runs"), read through the shared feature seam (a DB feature, so the read is correct), never a refusal; temporary
  report files into an Out parameter (D14).
- Never create a printable; never pick an unrelated one.

**Consequences.** No new MCP tool (the compact tool index has about 25 bytes left, measured on clio
`origin/master` 2026-10-01). Guidance: `templateId` -> `report.printable`; record order (and so the " (i)"
numbering) is unspecified; captions are localized in the RUNNER's culture; a mid-run failure leaves partial
output; a sub-process's temporary files die with the sub-process (pending M16); the same-object printable swap keeps
the filter (D12). e2e: `Assert.Ignore` when `list-printables` returns no rows; `ListPrintablesEnvelope`
(`CLIO/clio.mcp.e2e/Support/Results/ListPrintablesEnvelope.cs:12-13`) gains `count` and `printables`.
**Owner decision: yes.** FastReport refused (B, recommended) or accepted with a notice (A).

## D18. Process-parameter variant: the `files` input

**Context.**
- `Files` is a required In collection with one FileLocator item `File`. The page binds the nested item; when the
  source is a collection item the designer also binds the outer level; a single file binds the nested item only
  (`PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:44-79, 117-136`;
  `PD/MappingEditMixin/MappingEditMixin.js:947-1096`). basis=source.
- Runtime (basis=source): nested-only from a single file -> one row (X1); outer-only -> a NullReferenceException
  per row; nothing bound or an empty source -> zero files, completed silently; the target schema is resolved first,
  so an empty `TargetEntitySchemaUId` throws even with zero rows; a non-FileLocator nested source -> an invalid cast.
- Its own output item is `ObjectFile` (the COPIES), while its input is read by the key `File`
  (`ProcessFileProcessingUserTask.cs:40, 73`). The designer never offers this variant's `ObjectFiles` as a mapping
  source; the runtime fills it.

**Decision.**
- `files` takes exactly one of `processParameter`, `sourceElement + sourceElementParameter`, or `expression`;
  `value` is refused (a file has no constant form).
- A structured COLLECTION source (a FileCollection, an element's `ObjectFiles` or `ReportFiles`, or a dotted item of
  one) binds BOTH levels through `BindCollection`: `Files <- the source collection`, `Files.File <- that
  collection's single FileLocator item`, resolved, never assumed (`File`, `ObjectFile`, or the declared name).
- A structured SINGLE FileLocator source (a File parameter or a single element output) binds `Files.File` only and
  leaves `Files` unbound, **pending M1**. If M1 fails, the single-file path is withdrawn (refused) and the guide tells
  agents to wrap one file in a FileCollection.
- `expression` is written onto `Files.File` as given, `Files` left unbound, with a notice ("the result is not
  checked: the formula must yield one file"). The package's meta-path check runs on it as on every expression since
  1.6.6.79, and nested-item tokens such as `ObjectFiles.File` pass it. It exists so that a describe of a nested
  binding that is not a single token resubmits; like every expression, it is outside D5's rules.
- Refused: a structured source with no FileLocator item (R-M2), an outer-only binding, no binding at all, any
  mapping FROM this element's `Files` input, `useInProcess` (D14). `saveTo` is required.
- The variant's `ObjectFiles` (item `ObjectFile`, the copies) and `CreatedObjectFileIds` are listed as outputs, with
  a guide note that the designer does not offer `ObjectFiles` (confirmed by M4).

**Consequences.** Describe decodes `files` to `processParameter` or to `sourceElement` + a dotted
`sourceElementParameter` when both levels are single tokens of one source (or the nested level alone, for a single
file); to `files.expression` when the nested level is a non-single-token formula and the outer is unbound; anything
else gets an `issues` entry and replays through the raw parameter list, because `addMapping` onto `Files` and
`Files.File` stays legal (D13). Guidance: a single-file input must be set before the element runs (an unset value
gives `{File: null}` and a crash, pending M2); an empty source copies nothing silently. Package tests pin only the
METADATA shape at both levels; the runtime reader is internal, so the runtime goes to the stand (M1, M2, M4).
**Owner decision: no** (M1 decides the single-file path, not preference).

## D19. Describe block of the element

**Context.** Today these elements describe as a generic `usertask` with a raw, provenance-filtered parameter list:
inherited defaults (`RecordsToRead` 50, an unset `ResultActionType`), an empty `ConnectedObjectColumnUId` and a
nested-only `Files` binding are invisible, and `ReportId` and `TargetEntitySchemaUId` are bare GUIDs (measured on
PrintQuotationReport and GenerateDNSRecordsSpecification, 2026-10-01). clio passes an undeclared element block
through `DescribedElement.AdditionalData` (`CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:822-829`). Describe
may return an unconverged snapshot for compiled processes.

**Decision.** A typed `fileProcessing` block in `DescribeProcessElement` (`PB/Contracts/DescribeContracts.cs:147-413`;
the class gained `filterDecodedCompletely` in 1.6.6.82, so expect a rebase),
reporting EFFECTIVE values (inherited template defaults count), in the write vocabulary so that it round-trips. Per
cut: OA describes Object elements with the block; Report and Process elements keep today's generic describe until RP.

```jsonc
"fileProcessing": {
  "source": "attachments",                                  // identity check on write
  "action": "saveToAttachments", "actionStored": true,      // actionStored: describe-only
  "attachments": { "object": "Account", "storage": "dedicated", "fileObject": "AccountFile", "linkColumn": "Account",  // storage, fileObject, linkColumn: identity checks
                   "recordId": { "sourceElement": "RD1", "sourceElementParameter": "ResultEntity", "sourceColumn": "Id" },
                   "numberOfRecords": 50, "sort": { "column": "CreatedOn", "direction": "desc" } },
  "report": { "printable": "<templateId>", "caption": "Invoice", "type": "MS Word", "object": "Invoice", "convertToPdf": false,  // caption, convertToPdf: describe-only; type, object: identity checks
              "recordId": { "processParameter": "Invoice" }, "separateReports": true, "fileNameSuffixColumn": "Number" },
  "files": { "processParameter": "Docs" }, "bindingLevels": "both",   // bindingLevels (both, itemOnly, outerOnly, none): describe-only
  "saveTo": { "object": "Contact", "storage": "dedicated", "fileObject": "ContactFile", "linkColumn": "Contact",
              "recordId": { "processParameter": "ContactId" } },
  "outputs": [ { "parameter": "ObjectFiles", "item": "File" }, { "parameter": "CreatedObjectFileIds", "item": "Id" } ],  // describe-only
  "issues": [ "sort column 'GPSE' is not a column of 'SysFile' - the element throws at run time" ]                       // describe-only
}
```

- Value sources decode to `processParameter`, or `sourceElement` + a dotted `sourceElementParameter` (+
  `sourceColumn`), when the stored Script is a single token (the D8 decoder plus a process-level twin); otherwise
  `expression` plus a describe-only display value; a constant as `value`.
- Read-only members follow D11's two kinds; describe-only members are never accepted back.
- `issues` covers the silent hazards: inconsistent storage pairs (H-G3-1 source half "throws at run time", target
  half "cosmetic", pending M11), a target with no link column, a sort or filter column the runtime root lacks, a
  SysFile scope whose reference is not the record object, hidden secondary sort entries ("sending sort replaces
  them"), an outer-only or unbound `Files`, a `Files` shape that does not decode, a Word element with
  `IsSeparateReports = false`, a DevExpress printable, an unset `ResultActionType`.
- The reader tolerates snapshots: absent parameters read as null, a Lookup-typed `ConnectedObjectId` reads like a
  Guid, no "out of sync" signal (the next design-time load converges it).
- The generic raw `parameters[]` and `userTaskName` stay.

**Consequences.** clio gets a typed `DescribedFileProcessing` DTO in which EVERY nested type carries its own
`[JsonExtensionData]` bag, so a member a later package adds survives this clio; a `ServerProcessDescriberTests` case
deserializes an unknown member at every level and re-serializes it. The same clio PR updates
`CLIO/docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`, which owns the list of
`Described*` types that carry a bag. PBT round trip: an element whose describe has issues, a uk-UA caption and
`bindingLevels: outerOnly`; feeding the write members back is accepted and repairs `outerOnly` to `both`; an equal
identity check is accepted, a changed `report.type` refused. Read-only describe of PrintContractsReport (M13) fixes
whether shipped elements come back converged or as a snapshot. **Owner decision: no.**

## D20. Serialization parity, template defaults, nested UIds, diagram

**Context.**
- The diagram needs `BL7 ManagerItemUId = SchemaUId`, size 69x55 and a lane container; creatio-ui has no
  file-processing logic and keys the user-task type on `managerItemUId`. The generic route already writes these
  (`PB/Elements/UserTaskElementHandler.cs:80-98, 401-411`; `PB/Elements/ProcessElementFactory.cs:114-125`).
  basis=source + measured (corpus 30 of 30).
- Every design-time load runs the activity parameter diff, so modify always sees the current parameter set; corpus
  drift (absent `*DataEntitySchemaUId`, absent `ConsiderTimeInFilter`, a Lookup-typed `ConnectedObjectId`) is what
  it repairs. The diff does not compare nested UIds (`CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:238-302`).
- Template values (`PD/ObjectFileProcessingUserTask/metadata.json`, `PD/ReportFileProcessingUserTask/metadata.json`):
  `ConsiderTimeInFilter` = Script "true" with `DefValueForExistingProcess` "false"; `RecordsToRead` (Object) = 50.
  A parameter the server creates from the template takes `DefValueForExistingProcess` when it has one
  (`ProcessSchemaActivity.cs:306-320`, re-read), so a server-built element gets "false" while the designer's keeps
  "true" (UO-3 measured "source 3, true"). Its only reader is the designer's filter editor; no runtime class reads it.
- Server-built nested items keep the TEMPLATE's UIds; the designer mints fresh UIds with `IL2` = the element UId.
  Mapping tokens embed the nested item UId (`[Element:{el}].[Parameter:{itemUId}]`). The runtime builds paths from
  the element UId, so `IL2` matters to the designer client only.
- The builder's display value for a nested source is the bare item caption; the designer form is
  `[#<el caption>.<collection caption>:<item caption>#]`.
- `BO2` SerializeToDB: the designer writes `true`; builder-made user tasks lack it today.

**Decision.**
- The handler writes `SchemaUId`, `ManagerItemUId = SchemaUId`, `SerializeToDB = taskSchema.SerializeToDB`, the
  factory's size and logging flag, and the lane. Nothing else is needed for the diagram.
- **Template defaults are written as an exact copy of the template value, provenance included:**
  `ConsiderTimeInFilter` always on create (Script "true", which keeps the "false" from `DefValueForExistingProcess`
  out); `RecordsToRead` on create when `numberOfRecords` is omitted. Every value the caller chose (including an
  explicit `numberOfRecords: 50`) and every derived configuration value is a NEW value with its source set before its
  value and `ModifiedInSchemaUId = schema.UId`. On an edit, `ConsiderTimeInFilter` is never re-pinned.
- **Re-mint nested item UIds on create only**, inside the handler's create path, before configuration and before
  any mapping of the same request resolves the element's items (fresh Guid, `IL2 = element UId`). Never on modify:
  an existing builder-made element that still carries template UIds keeps them, because consumer mapping tokens
  embed the item UId. Verified by M14.
- Nested-source display values are written in the designer form (`ProcessMappingService.BuildSourceValue`, element
  arm); this also fixes multi-instance per-item display.
- A missing template parameter after `GetDesignInstance` is a refusal ("the element's user-task parameters are not
  synchronized"), never a hand-created parameter.
- The generic-route `BO2` gap for other user tasks is a separate Sub-task under ENG-92719 File processing element:
  "Builder-made user tasks do not set SerializeToDB".

**Consequences.** PBT: `BL7`, `BO2`, size, lane; `ConsiderTimeInFilter` equals the template value on create and is
untouched on edit; `RecordsToRead` is the template copy when omitted and a new value when sent; derived values carry
this schema's provenance; fresh nested UIds with `IL2`; a create batch whose later mapping reads
`OF1.ObjectFiles.File` resolves to the re-minted UId; a modify keeps template nested UIds and their consumers; display
form. The AC capture comparison is the rule of
[serialization-capture](eng-92719-file-processing-element-serialization-capture.md) section 10, and Part D states it
in full:
- Element UId, name and lane are ignored (elements are paired by caption), and so are top-level and nested parameter
  UIds. A nested item's `IL2` (the element UId) is still compared.
- `ModifiedInSchemaUId` (`GS5`) is normalised for a value equal to the template default. The designer does not vary
  there: it keeps the template's GS5 on unchanged defaults and stamps the process UId only on a value it changed
  (`RecordsToRead`: 12 of 12 unchanged `50` keep the template's, 2 of 2 changed values carry the process UId, so 14
  of 14 follow the rule; `ConsiderTimeInFilter`: 18 of 18 keep the template's; serialization-capture S4). The
  normalisation is needed because D20 writes every caller-chosen value, an explicit `numberOfRecords: 50` included,
  as a new value stamped with the process UId.
- Four deviations are named, finite and tolerated:
  - **N2:** `BL8` is absent on every builder-made element. The gap is generic, not specific to files, and is recorded
    for every element the package builds (crt-process-builder `docs/script-task-element-capture.md:30-32`); it is not
    fixed in this work, and no Sub-task is proposed for it (D-5).
  - **N9:** a builder-made process parameter stores `IL2` = the process UId where the designer stores none
    (`PB/Parameters/ProcessParameterService.cs:65, 478`; `CORE/Terrasoft.Core/Process/BaseProcessSchemaElement.cs:217`).
    The meta path is the same either way, basis=source; M22 checks that the designer opens such a parameter.
  - **N10:** a nested item has no `BK15` mapping row, because rows come only from the sync of top-level parameters
    (`CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:291-305`). This is runtime-neutral by source, and 6 shipped
    nested items have no row either. If the owner wants strict parity, the alternative is to write the rows in the
    handler's create path.
  - **N13:** the DisplayValue rows of hidden constants (`ResultActionType`, `ConnectedObjectColumnUId`, `ReportId`,
    `IsSeparateReports`, `ConsiderTimeInFilter`) are reported but do not fail the comparison. Displayed values
    (`ConnectedObjectId`, `Files`, `Files.File`, `ReportName`) must still match.
- The same table's format normalisations also apply: N11 (`BK15.GT1` not compared), N14 (authoring culture only), N15
  (the three "no value" forms are equal) and N16 (filter keys normalised to positions).
- Any other difference fails.

The element documents state this rule as the test-plan's TC-41 rule (TC-41, TC-59, TC-69, DT-02, DT-04) and in plan
section 11. For process parameters, the ENG-95984 File process parameter type AC-8 rule applies (its test-plan
section 4.2; plan AC-8 and V4; pr-split section 14). **Owner decision: no.**

## D21. Reuse map

Summary of what is reused, mirrored or avoided; the full map with evidence is in
[reuse](eng-92719-file-processing-element-reuse.md). Production package code cannot reference
`Terrasoft.Configuration`, so configuration-level enums and constants are mirrored. Three rows below carry the reuse
RF1-RF3 corrections (plan C-6).

| Need | Reuse | Mirror | Do not reuse |
|---|---|---|---|
| Type UIds, directions | core `DataValueType.FileLocatorDataValueTypeUId`, `CompositeObjectListDataValueTypeUId`, `ProcessSchemaParameterDirection` | - | - |
| Element identity | `UserTaskSchemaIdentity` x3, through the variant registry | the three schema names (the UIds live in the test fixtures and the drift guard only; RF1) | - |
| Handler structure | `OpenEditPageElementHandler` + a shared static binder + an applier | - | - |
| Value sources | `IProcessMappingService.ApplyMapping`, `RecordColumnReference` (from ENG-91844 Implement full parameter mapping (sources)), the new `BindCollection`, the `OpenEditPageRecordDescriptor` member names | - | hand-built source values |
| Record-id constants | `ProcessParameterValueValidator.EnsureReferencedRecordExists` (new internal entry point) | - | the scalar Guid branch |
| Filters | `ProcessFilterService.BuildFilterValue`, `FilterDescriptorReader`, `ProcessFilterTargetBase`, the new `FileProcessingScopeFilter` | `FileConsts.FileTypeUId` (describe issues only) | `DataSourceFilterValue.ClearIfForeign`; the designer's SysFile sort list |
| Sort | `ReadDataSortDescriptor`; **reuse + change**: the sort codec extracted from `ReadDataConfigBinder` (`ResolveOrderInfoValue` / `DescribeSort` are private and worded for Read data; RF2) | the `OrderByInfo` format | - |
| Storage | `EntitySchemaManager`, `SchemaManager.GetAllParents` (behind an adapter), `EntitySchemaResolver` | SysFile and BaseFile UIds, the four tag/folder base UIds, the `<X>File` / `FileLead` rule | core `FileSchemaProvider`; `EntitySchema.MasterRecordColumn` |
| Reports | ESQ on `SysModuleReport` (`UseAdminRights = false`) | the type names, the entity fallback path | `ReportEngine` |
| Enums | - | `ResultActionType` 0/1; row keys `File`, `ObjectFile`, `Id` | `ResultActionTypeEnum` |
| End-of-request checks | notices: the element-configuration ledger (`IElementConfigurationLedger`), one rule in `ElementConfigurationNotices`; refusals: a new pre-gate check (RF3) beside the `EnsureFormulasStillBelongOnTouchedConnectors` end-check precedent (`PB/Design/ProcessEditPipeline.cs:130-133`), now the only end-of-request refusal before layout and the gate; `IProcessDesignNotices` | - | mid-batch notices; a refusing reconcile in the ledger (notice-only by contract, after the gate) |
| Dependents for refusals | `IProcessDependencySnapshot` (the request's dependency snapshot, as every refusing guard since ENG-100041) | - | a live `ProcessElementDependencyScanner` scan |
| Feature reads | `Creatio.FeatureToggling.Features.GetIsEnabled(string)` behind one seam | the two feature code names | `UserConnection.GetIsFeatureEnabled` |

**Owner decision: no.**

---

# Part C. Delivery

## D22. Downstream consumers: which ticket owns what

**Context.**
- Send email and the Creatio.ai call read files through DYNAMIC slots `<Prefix><N>` (a collection, In) plus
  `<Prefix><N>FileLocator` (FileLocator, In), created in the process; the runtime finds them by name pattern and
  "created in this process". The builder creates no attachment slots today. Corpus: 4 Send email and 2 Creatio.ai
  bindings, all in test packages, all both-level. basis=source + measured.
- ENG-95985 Send email attachments exists in the backlog with exactly this scope. ENG-92725 Execute AI Intent element
  (BP generation) is Closed with resolution Won't Do (read 2026-10-01).
- Read, Add and Modify data never consume a file output by mapping (0 corpus cases); a filter right-hand side is one
  value, so "Id in CreatedObjectFileIds" is not a platform feature; the builder accepts a collection parameter as a
  filter value with no type check (a latent, non-file defect). A FileLocator cannot be written into a column. The
  product pattern: Modify data re-finds the created file by filter (`Name contains`, `CreatedOn = CurrentHour`) and
  sets its Tag.
- A multi-instance sub-process per file works on the machinery of ENG-99856 Sub-process element: support
  MULTI-INSTANCE (running the callee once per item of a collection) once the callee can declare a File parameter
  (PT).

**Decision.**

| Pattern | Home |
|---|---|
| Process file -> Send email attachments | ENG-95985 Send email attachments, after OA (first source) and PT (process-parameter sources); it reuses `BindCollection` and adds its slots to the D5 file-consuming targets |
| Process file -> Creatio.ai call | out of ENG-92719 File processing element; guidance says "not through this tool yet" (never "Creatio cannot") |
| Read data -> Process file (record scope, report scope, file name) | OA and RP tests |
| Process file -> Modify data (re-find by filter) | guidance in OA's knowledge PR, with its hazards (same-hour matches, the hour boundary), plus the build-and-describe e2e TC-71 (CL-OA) |
| Add data creates the report's data row before a report | guidance in RP, plus the build-and-describe e2e TC-74 (CL-RP) |
| Process file -> multi-instance sub-process per file (`InputRecordCollection.F <- OF1.ObjectFiles.File`; P1 binds the parent), or per created Id | OA e2e, after PT merges |
| Object -> Process variant chain | RP e2e |
| NEW Sub-task under ENG-92719 File processing element: "Refuse collection parameters as filter values" (one guard in `ProcessFilterService.ResolveReference`, `PB/Filters/ProcessFilterService.cs:533-595`; since 1.6.6.75 the `expression` spelling resolves through the same two structured branches, so that guard covers all three spellings, and neither branch checks for a collection type yet: M19 still holds on 1.6.6.96, where `ProcessFilterService.cs` is unchanged) | its own PR, size S, not file-specific |

**Consequences.** The FE criterion "Tests cover the five patterns" is amended (Part D). The guide's "Consumers"
section states what is buildable, what is not yet (Send email attachments, Creatio.ai files) and the
Ids-via-iteration route. **Owner decision: yes.** Move the Send email pattern test to ENG-95985 Send email
attachments and drop the Creatio.ai pattern (or reopen ENG-92725 Execute AI Intent element (BP generation)).

## D23. Jira dependencies

**Context.** Jira today: ENG-95984 File process parameter type blocks ENG-96505 Element readiness and object
attachments mode (link 560203); ENG-96506 Generated report + process parameter modes has no link (read
2026-10-01). In code, the Object variant needs no File process parameter: its outputs are element parameters,
mappable today with dotted paths. What really needs PT is the Process-parameter variant (a File or FileCollection
source, dotted process-parameter addressing, the two-level binder) in RP, plus OA's multi-instance per-file test (a
callee File parameter). basis=source.

**Decision.** Links say what really depends on what; the PR order enforces the delivery order. The rows are
L1-L5 of [pr-split](eng-92719-file-processing-element-pr-split.md) section 12.2.

| # | Today (read 2026-10-01) | Proposed | Why |
|---|---|---|---|
| L1 | ENG-95984 File process parameter type **blocks** ENG-96505 Element readiness and object attachments mode (link 560203) | **relates to** | no functional dependency (above); keeping "blocks" is acceptable if the team uses it for sequencing |
| L2 | ENG-96506 Generated report + process parameter modes has no link | ENG-95984 File process parameter type **blocks** ENG-96506 Generated report + process parameter modes | the Process-parameter variant consumes File and FileCollection parameters |
| L3 | none | ENG-96505 Element readiness and object attachments mode **blocks** ENG-96506 Generated report + process parameter modes | RP registers its variants in the handler and the registry that OA introduces (D10) |
| L4 | none | ENG-96506 Generated report + process parameter modes **blocks** SF | default slot order (pr-split O3); reversed to "OA blocks SF" if the owner puts SF before RP |
| L5 | none | ENG-95984 File process parameter type and ENG-96505 Element readiness and object attachments mode **block** ENG-95985 Send email attachments | that ticket reuses `BindCollection` and needs a file source (D22) |

- New Sub-tasks: SF under ENG-92719 File processing element; the follow-up Sub-tasks listed in D27 and Part D-5. All
  are type Sub-task. MH is not created: M3 refuted H-1.
- The delivery ORDER stays PT -> OA -> RP -> SF anyway: the package PRs share `descriptor.json`, the contracts,
  `ProcessMappingService.cs` and `ProcessDescriber.cs`; OA's consumer and per-file tests benefit from P1; the PT clio
  PR carries the ManagerMap arm (D10). OA iterates as a stacked draft during PT's review
  ([pr-split](eng-92719-file-processing-element-pr-split.md)).

**Owner decision: yes** (Jira link changes L1-L5, the RP/SF slot order, and the new Sub-tasks).

## D24. Acceptance-criteria corrections

The proposed replacement text for all four issues, and the reason for every change, is in Part D. In short:
"Binary / File" becomes `File` + `FileCollection`; "mapped from its output when `ResultActionType = 1`" is wrong
(outputs exist for both values); serialization is compared per variant and per storage against named captures,
under a rule with four named exceptions (D20); "rarely an endpoint" becomes the shipped pattern (D-3);
Send email moves to ENG-95985 Send email attachments and the Creatio.ai pattern is out of scope; the record scope is
a value of the element, while the source object is a schema choice; Report and Process elements stay generic until
RP; SysFile moves to SF; a printable is chosen among MS Word printables of the reported object; "Use in process"
does not exist for the Process-parameter source. **Owner decision: yes** (Jira edits).

## D25. Guidance placement and tool-description budget

**Context** (measured on a replica of the size tests, 2026-10-01; the knowledge sizes recomputed with the same rules
on clio-knowledge master `0b56d4a`, 2026-10-13; the tool-contract sizes measured on clio master `19e6f9c53`,
2026-10-13).
- Knowledge budget: 27,793 characters per process article (JSON-escaped length plus a 1,400 envelope,
  `KB/automation/Clio.Knowledge.Bundle.Tests/ProcessGuideResponseSizeTests.cs:43, 131, 156`). Knowledge #239 split
  parameters, element-catalog, formulas, activity-connections, send-email and three other articles into `<article>` +
  `<article>-details` (2026-10-08), and nothing the plan edits was renamed. Room left: process-parameters about 9,656
  characters (about 345 on 2026-10-08), process-element-catalog about 9,491, sub-process about 7,738 (72.2% used),
  sub-process-when about 11,089, routing about 5,985 (78.5% used). process-modeling has 6,332 characters left but only
  8 bytes under the byte rule below; process-formulas has 6,932 characters and 561 bytes left; no PR of this work
  edits either. activity-connections and send-email were not re-measured.
- A second rule, `GuidanceInlineReplyBudgetTests` (`InlineReplyBudgetBytes = 19_000`, UTF-8 plus a 1,300 B
  envelope), covers process-modeling (since knowledge #271) and the eight process pairs split again by knowledge #280
  (ENG-103030 guidance: split the eight process articles the digest routes to that still exceed the 19 KB inline
  line). It does not cover parameters, element-catalog, sub-process, sub-process-when or routing, nor a new
  `process-files` unless someone adds it; its remarks say an article that outgrows it is split again, the budget is
  not raised. basis=source + calc.
- An article with the banner "Part of the process guide set." must be indexed in process-modeling, which has no
  room (8 bytes under the byte rule). Precedents without the banner: process-script-task, run-process-button,
  process-custom-elements, and `process-tracing`, registered banner-less by knowledge #268 for ENG-102111
  create-business-process / modify-business-process: allow switching on process tracing (IsTracing).
- `routing.md:36-43` sends a process build to the `process-digest` card "ALONE and FIRST", then only to the articles
  of the elements used. No test requires every process guide on the card (about 3,069 of 3,072 bytes), but an agent
  that follows the card may never open a guide that only has a routing row (inference, untested).
- Tool contracts: ceiling 35,072 B (`CLIO/clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs:205`). On
  2026-10-01: create 34,863 (209 left), modify 34,883 (189), describe 33,148 (1,924). Measured on clio master
  `19e6f9c53` (2026-10-13, a scratch export with the ceilings lowered to 1): create **35,068 (4 B left)**, modify
  **35,062 (10 B left)**, describe 34,019 (1,053 B left). ENG-102114 deleted C6 and dropped S2's member list on
  2026-10-07; what filled the room since: ENG-99970 CAADT runs for BPMS Tools cost 6-15x other teams - find and close
  the gap ("Take packageName from get-target-package.", +42 B on create, clio #1694, merged 2026-10-08), ENG-102112
  (clio #1777), ENG-102537 (clio #1778), ENG-102111 (clio #1786: `isTracing` / `setTracing` and the floor clause),
  ENG-102116 Process tools gated on the CrtProcessBuilder install: state the prerequisite in guidance and the restart
  in the refusal message ("after user approval", clio #1805), and ENG-103023 clio: spill-free reads and post-write
  summaries (describe-business-process modes, write tools return the result, get-page lean by default) (the
  `ProcessWriteDigest` sentence and describe's `mode` / `element`, clio #1801). The ceiling may not be re-pinned
  without review (test comment). **Moved on 2026-10-14:** on clio master `79fab4786`, ENG-103081 Warn when a
  process-parameter mapping is bound twice or to a branch some start skips (clio #1807) paid for its own sentences by
  a swap and re-measured create at **35,061 (11 B left)** and modify at **35,070 (2 B left)**
  (`CLIO/clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs:206-212`).
- Short form (ENG-100154, merged 2026-10-07): a default `get-tool-contract` lookup aims at 18,432 B
  (`ToolContractShortForm.InlineReplyBudgetBytes`, `CLIO/clio/Command/McpServer/Tools/ToolContractShortForm.cs:203`),
  which is now only Fit's own target. Since ENG-102797 O4c: make the short modify-business-process /
  create-business-process contract complete enough that agents stop asking for detail:full (clio #1795, 2026-10-12)
  the seven process-designer contracts requested together are held to `MaxInlineWireReplyBytes = 19_000` on the wire
  (`CLIO/clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs:217`): **18,986 B measured, 14 B left**; the
  seven come back short and Fit's own measure exceeds 18,432 B by design. The short form keeps the lead, each
  field's first sentence and every sentence with a safety word ("never", "must not", ...). The short create and
  modify contracts also always carry a call SHAPE built from hand lists (`ToolContractShortForm.CallShapes`,
  `:171-188`; `CreateElementBlockShape` and its siblings `:68-109`; `ModifyOperationShape` `:117-139`), held equal to
  the bundled archive by three drift tests (`ModifyCallShape_Should_NameExactlyTheOperationsTheBundledServerAccepts`
  `:524`, `ModifyCallShape_Should_NameExactlyTheOperationFieldsTheBundledServerDeclares` `:549`,
  `CreateCallShape_Should_NameExactlyTheMembersOfTheBundledServerContract` `:574`, in
  `CLIO/clio.tests/Command/McpServer/ToolContractShortFormTests.cs:521-589`; the args by
  `CallShapeArgs_Should_NameRealArgumentsAndEveryRequiredOne` `:595`); the shapes are never cut. A type-list
  addition does not count against the wire guard (hypothesis, source); a new refusal sentence worded with "never" or
  "must not", a new descriptor member or a new top-level op or operation field does, in full.
- `descriptor` and `operations` are JSON values since ENG-100153 O5 (2026-10-07); a string holding the same JSON is
  still accepted and passed through unchanged. Since ENG-103025 clio: forgiving argument shapes for process tools,
  mini-contract on empty calls, synchronous install-process-builder (clio #1800) a descriptor sent under a synonym or
  spread over the top level is accepted with a warning; the descriptor itself still passes through unchanged, so an
  element-level `fileProcessing` block needs no alias work (hypothesis, source).
- X2 (a `name=` in a description must be in the curated fixture) and X3 (the floor sentence must stay).
- Two shipped multi-instance passages state as measured fact that a per-item mapping without the explicit
  `InputRecordCollection` mapping runs ONE iteration (`KB/guidance/mcp/guides/processes/sub-process.md:137-141`,
  `sub-process-when.md:50-52`); P1 changes that from PT's cut.

**Options.** Guide name: `process-files` (the domain: the type, the element and its consumers), `process-file` (the
palette caption), or one guide per ticket.

**Decision.**
- ONE new guide, **`process-files`**, without the banner, born in the PT knowledge PR; OA adds the element, object
  attachments, legacy storage, action and consumers sections; RP adds the report and process-parameter sections; SF
  rewrites the storage section. Size target about 21,000 characters (inference: 22,000 characters is about 19,500
  bytes at the measured ratio, so 21,000 survives the article being added to the 19,000-byte rule); planned seam:
  the report section splits into `process-files-report` once the guide would exceed the about 21,000-character
  target (80% of the 27,793-character ceiling, about 22,200, would no longer fire first). Each knowledge PR registers
  the article fully
  (article, `bundle-source.json` entry and `requirements.itemIds`, the migration list, a routing row shipped in the
  same generation as the article because of the dangling-reference check of knowledge #259 (hypothesis), a pin test
  in the style of `CollectionParameterGuidanceTests`). It registers the article banner-less, the way knowledge #268
  registered `process-tracing`, and does NOT add it to `ProcessGuideSet.GoLiveFloor`: `GuidanceMigrationTests.cs:346`
  requires every `GoLiveFloor` id to be in the banner set split out of process-modeling, so a banner-less
  `process-files` there fails (an error in this plan's earlier text, not drift; hypothesis from test source). Each
  PR bumps libraryVersion above master at merge time and above every number another branch claims (master 1.15.142
  on 2026-10-13, when branch claims reached 1.15.142 and no 1.16.x existed; master 1.15.143 on 2026-10-14, released
  2026-10-13, so 1.15.144 or higher, re-checked at cut time).
- Content per PR:
  - PT: File and FileCollection; the read-back and the item-name caveat (D2); direction (D3); P1, P2, P3, R-M1,
    R-M2, file-consuming targets, "an expression goes through as given; only its meta-path tokens are checked" (D5);
    refusals; and two lines the PK-PT implementation made necessary: (a) order matters: on a file-consuming
    collection an item mapping followed by an outer mapping rebinds the item (P2), and the reverse order resets the
    parent (P3); (b) R-M1 also refuses a flat File input of a single-instance sub-process mapped from a collection
    item (the refusal's alternatives: a FileCollection, or a multi-instance sub-process).
  - OA: the element and "the group is the variant" (D10); `attachments` with `recordId`, "numberOfRecords defaults
    to 50 and does NOT read all", the sort shape (with the M24 result); member-wise merge (D12); identity checks vs
    describe-only members (D11); legacy storage and "SysFile storage is refused in this version"; the H-G3-1 warning
    if M11 confirms it; action per variant and the Object "locators point at the source" rule (D14); consumers; the
    endpoint sentence of D-3 ("most shipped uses end in a record's attachments; when a following element needs the
    files, map the element's output collection", plan C-2), pinned by TC-88 in place of "rarely an endpoint".
  - RP: report (`templateId` -> `report.printable`, Word `separateReports`, the same-object swap keeps the filter,
    temporary files) and process parameter (`files`, `expression` as given).
  - SF: the SysFile hazards (record-object sort columns, `RecordSchemaName`, checking the Freedom UI attachment
    list); the refusal sentences removed behind a version gate.
  - Every PR, in new and rewritten text alike (rules added to the knowledge suite after the 2026-10-07 pin):
    - every read-back instruction (the PT read-back of D2, the nested-only check, the rewritten multi-instance
      passages) writes `describe-business-process mode=full` or `element=<name>`, with the gate "on clio older than
      8.1.0.141, omit `mode`" that `process-modeling.md:68` owns: `ClioToolSyncGuidanceTests.cs:70-84` fails a plain
      read-back describe (knowledge #277);
    - every `install-process-builder` mention carries consent wording within 80 characters
      (`ProcessBuilderInstallConsentGuidanceTests.cs:63-96`, knowledge #279);
    - no Jira keys, issue or PR links (`InternalTaskReferenceTests`, KB `AGENTS.md:22`); a version boundary is
      the clio version that shipped the change, and a package version appears only as inline evidence ("measured
      on CrtProcessBuilder X on <date>").
- Existing articles:
  - PT: a routing row (about +197; routing has about 5,985 left); the process-parameters type list (about +122) AND
    the FileCollection exception to `parameters.md:37-39` (about +110), which fit the about 9,656 characters left
    since knowledge #239 split the article; the type list and the sentence stay in `parameters.md`, the sentence that
    `CollectionParameterGuidanceTests.cs:24-29` pins is kept, and the 2026-10-08 fallback (replace that sentence at
    the same length) is moot. `sub-process.md:137-141` and `sub-process-when.md:50-52` are rewritten with a version
    gate ("from clio [the version that ships CL-PT], a per-item value taken from an item of a collection binds that
    collection too; a per-item value from anything else still needs the explicit `InputRecordCollection` mapping; two
    different collections are refused", written with the real number at merge; the package version appears only as
    inline evidence, "measured on CrtProcessBuilder X on <date>"); the "send both" advice is kept (correct on older
    servers); the new sentence is pinned.
  - OA: the element-catalog buildable row (about +313 of about 9,491 left) and the generic-route sentence worded for
    this cut: only the Object schema is refused; Report and Process-parameter elements are "not buildable yet; do not
    build them as a generic userTask". The FOUR dedicated types become FIVE in the same PR, with the three knowledge
    tests that pin FOUR (`ProcessDigestTests.TheCardsDedicatedTypes_ShouldEqualTheCatalogsFour`, `HaveCount(4)` at
    `:381`; the `RulesHandedBackToTheirOwners` sentence at `ProcessDigestTests.cs:128`;
    `ProcessPreconfiguredPageCatalogGuidanceTests.cs:20`) and the `process-digest` card, which must list
    `fileProcessing` with about 3 bytes free (D13). RP extends the refusal to both schemas.
  - Optional one-line pointers in send-email and sub-process. process-modeling and activity-connections are not
    touched.
- Gate sentences in the guide: "use only when get-tool-contract for create-business-process mentions type File /
  fileProcessing", and concrete "from clio [the version that ships CL-PT / CL-OA]" lines written with the real number
  at merge (no placeholder can merge). A CrtProcessBuilder version appears only as inline evidence ("measured on
  CrtProcessBuilder X on <date>"), never as the boundary (KB `AGENTS.md:22`).
- Tool descriptions:
  - **Where the bytes come from: Q20 agreed 2026-10-14: A, trim** (asked 2026-10-13). Create had 4 B and modify 10 B
    left on 2026-10-13, and 11 B and 2 B on `79fab4786` (2026-10-14), so neither PT's nor OA's additions fit: a byte
    swap is needed again, freeing at least 19 B on create and 26 B on modify for PT. X1's condition (the description ceiling
    blocks the type-list edit) is met by measurement on 2026-10-13. Its action, a separate early description-budget
    PR, stays optional: by default the swap rides in CL-PT's CL-2; the reviewer settles which (Q20 = A: trim). The PR
    count stays 14 unless that early PR is opened (pr-split X1). Options were:
    A, trim existing text per tool, reviewer-approved, measured by lowering the ceiling (recommended, **chosen**); B, a
    reviewed ceiling raise (rejected); C, move the type lists out of the descriptions into guidance (rejected). Q20
    gated CL-PT's description edit (CL-2) and CL-OA, not package code; both now proceed with A, and the trim itself is
    work inside those clio PRs. Whoever lands first pays; the order PT then OA makes PT pay first. Cost:
    CL-2 grows by about 0.5 h (swap and re-measure), OA.10 by about 1 h (call shape and wire trim).
  - PT: type lists only (create +30, modify +28), no `name=` pointer (the routing row reaches agents); they need a
    byte swap (Q20). Candidate on create (inference, measured at cut): drop the `{enabled, executionMode,
    ignoreErrors}` member list from the floor clause "(for isTracing, ... subProcess.multiInstanceOptions {enabled,
    executionMode, ignoreErrors})" (about 39 B), as modify already did in ENG-102114; the modify candidate is chosen
    at cut with the reviewer. PT adds no descriptor member (types are values), so the call shapes and the 19,000 B
    wire guard are unaffected (hypothesis, source). The floor-sentence guards still bind any edit near the floor
    clause: `this clio requires <floor>` stays (X3), and the collapse clause with the exact words `stopped validating
    formulas` keeps no floor literal in the 60 characters before it
    (`FloorSentences_ShouldNotCreditTheEnforcedFloorWithTheCollapse`,
    `CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:1557-1593`; ENG-95984 File process parameter type
    plan CL-2); any swap leaves both intact. describe: the decoded-source clause covers "a value that IS another
    element's output" (about +40 B of 1,053 B left; it fits).
  - OA: the token in the type list, the block clause with `get-guidance name=process-files owns the fileProcessing
    block`, the `addElement` / `setElement` lists, the describe clause (estimated create +128, modify +74, describe
    +128; re-measured). Create and modify need a byte swap (Q20); describe fits. PK-OA adds a `fileProcessing` member
    to `ProcessElementDescriptor`, so `CreateElementBlockShape` gains the block in the same commit as the rebundle, or
    `CreateCallShape_Should_NameExactlyTheMembersOfTheBundledServerContract` fails. That entry (about 25-30 B) is never
    cut and lands inside the 14 B wire room, so OA also trims kept short-form text (a lead, a safety sentence or a
    field's first sentence of one of the seven contracts) to stay under 19,000 B on the wire; a new top-level op or
    operation field does the same to `ModifyOperationShape` (hypothesis, source). The curated fixture
    `CLIO/clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` (1.15.124 on 2026-10-13, without
    `process-files`) is re-pinned in the same commit to the PUBLISHED PT generation, with `process-files` and
    `creatio.process-files` added in sorted order. The end state is re-measured in the PR.
  - RP: the enforced floor clause moves to RP's final cut (byte-neutral below 1.6.6.100; at .100 or above the
    literal grows by one character in three descriptions, inference), plus the capability-map floor rows and the e2e
    floor. No other description change: the block pointer is variant-neutral.
  - SF: no description change and no floor raise (D26).
- `CLIO/docs/McpCapabilityMap.md` rows for create, modify and describe: type names, the block, the describe decode,
  the floor.
- MCP prompts (read on clio master `03ef3944f`; they have no byte ceiling). An earlier draft said "unchanged (they do
  not enumerate element guides)". That was wrong: the prompts enumerate the dedicated element types, their blocks
  and the generic `userTask` route, and AGENTS.md requires them to stay aligned with the tool contract.
  - `ListUserTasksPrompt` (`CLIO/clio/Command/McpServer/Prompts/ListUserTasksPrompt.cs:22-29`) tells agents to use a
    returned name as the `userTaskName` of a `userTask` element, except the carve-outs it already names since
    ENG-102112 / ENG-102537 (PreconfiguredPage and OpenEditPage go to their dedicated types; AutoGeneratedPage and
    UserQuestion are refused), while `list-user-tasks` keeps listing all three file schemas. OA excludes
    ObjectFileProcessingUserTask from that advice and points to `fileProcessing`; RP adds the other two schemas. The
    `list-user-tasks` tool description (`CLIO/clio/Command/McpServer/Tools/ListUserTasksTool.cs:46-72`) takes the same
    per-cut exception. Its count word is already gone (re-read on `19e6f9c53`, 2026-10-13): it says "Exceptions",
    routes PreconfiguredPage and OpenEditPage to their dedicated types, refuses AutoGeneratedPage and UserQuestion on
    every route, and limits the generic fallback to EmailTemplateUserTask and ApprovalUserTask (`:66-70`). Plan C-1
    is half done: OA still adds ObjectFileProcessingUserTask to the dedicated / refused list.
  - `CreateBusinessProcessPrompt` (`CLIO/clio/Command/McpServer/Prompts/ProcessDesigner/CreateBusinessProcessPrompt.cs:25-29, 44-55, 104-106`)
    names the dedicated types and their blocks: sendEmail, approval ("use that dedicated type rather than a generic
    `userTask` named ApprovalUserTask"), openEditPage, preconfiguredPage and changeAccessRights. OA adds one sentence
    for the `fileProcessing` block: the group present is the variant; use the dedicated type, not a generic
    `userTask` named ObjectFileProcessingUserTask; `get-guidance name=process-files` owns the block. RP adds the
    `report` and `files` groups to that sentence.
  - `ModifyBusinessProcessPrompt` (`CLIO/clio/Command/McpServer/Prompts/ProcessDesigner/ModifyBusinessProcessPrompt.cs:92-129`)
    states the `setElement` merge rule per element. OA adds the `fileProcessing` rule (D12): member-wise merge, an
    empty value clears, a value source replaces the whole value, and the variant cannot change in place.
  - `DescribeProcessPrompt` (`CLIO/clio/Command/McpServer/Prompts/ProcessDesigner/DescribeProcessPrompt.cs:33-35`)
    lists the configuration blocks that describe returns. OA adds `fileProcessing`. RP needs no change, because the
    block name does not depend on the variant.
  - PT: the prompts list no parameter types, so they need no update.
  - Cost: about 0.5-1 h each in OA.10 and RP.6 (plan section 5). Each change gets a clio unit test that pins the new
    sentence, next to the existing prompt pins (`CLIO/clio.tests/Command/McpServer/CreateBusinessProcessToolTests.cs:374`,
    `ModifyBusinessProcessToolTests.cs:300, 496, 601`, `DescribeProcessToolTests.cs:96, 109, 209`). For
    `ListUserTasksPrompt`, the test also pins that the generic-route advice excludes the schemas the cut claims.
- Resources: no process-specific MCP resource exists (`CLIO/clio/Command/McpServer/Resources/`), so none needs an
  update. `CLIO/clio/tpl`: it names none of these tools or schemas, so it needs no update.
- Every clio PR's MCP statement names the whole surface it reviewed, for example "MCP reviewed (tools
  create/modify/describe-business-process and list-user-tasks; prompts ListUserTasksPrompt,
  CreateBusinessProcessPrompt, ModifyBusinessProcessPrompt, DescribeProcessPrompt; resources; tpl)". It says
  "no update required" for each item the PR leaves unchanged. Plan OA.10, RP.6 and section 11, and pr-split sections
  5.3, 5.4 and 9.3, carry the prompt items and this wording; TC-90 of the test-plan pins the four prompt sentences.

**Consequences.** Merge order per ticket is package -> clio -> knowledge; the knowledge generation that births
`process-files` must be PUBLISHED (merged, released, and visible in `info-knowledge`) before the clio PR that first
names it merges, because CI checks only the fixture. A failed `update-knowledge` keeps serving the old guidance, so
check `info-knowledge`'s library version before any guidance-dependent verification. The knowledge half of
ENG-99970 merged on 2026-10-08 as knowledge #239 (at 1.15.104): it split parameters.md into `parameters.md` +
`parameters-details.md` and kept the type list and the `typeFromElement` sentence in `parameters.md` (`:19-22`,
`:37-39`), so no rebase is left. **Owner decision: yes** (the guide name); the description budget: Q20 agreed
2026-10-14: A, trim existing text per tool, each trim reviewer-approved, no ceiling raise.

## D26. `[RequiresPackage]` floors and version numbers

**Context.** create, modify and modify-as-new-version carry 1.6.6.92 since ENG-102111 (clio #1786, 2026-10-12, for
`isTracing` / `setTracing`; they carried 1.6.6.77 from ENG-102114,
https://github.com/Advance-Technologies-Foundation/clio/pull/1760, 2026-10-07, and 1.6.6.40 when this plan was
written) (`CLIO/clio/Command/CreateBusinessProcessCommand.cs:251`,
`ModifyBusinessProcessCommand.cs:207`, `ModifyProcessAsNewVersionCommand.cs:71`); the rule is that the floor moves
when clio starts advertising behaviour an older server may not have (`CreateBusinessProcessCommand.cs:211-217`).
Older-server behaviour: PT's `File` -> a misleading "not supported" and a silent unbound mirror; P1/P2 change what
`addMapping` writes; OA's `fileProcessing` on `setElement` -> silently dropped; RP's new members -> dropped. Package
main and clio's bundled archive are at 1.6.6.96 (2026-10-12; `ExpectedArchiveVersion`,
`CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:316`), and the stand runs 1.6.6.96 (read-only,
2026-10-13); the stand was at 1.6.6.54 when it was measured on 2026-10-02. Remote package tags reach
`crtprocessbuilder-1.6.6.96`; 1.6.6.87, 1.6.6.89 and 1.6.6.94 are burned, and no branch or tag of either repository
holds a number above 1.6.6.96. Two of the team's package PRs in review at the same time stamped the same number
twice (1.6.6.42 and 1.6.6.49, measured from git history).

**Decision.** Numbers are rules, not projections.
- Each cut claims, in its PR description and in one shared claim on ENG-92719 File processing element, the first
  free number ABOVE the highest number claimed anywhere at cut time (main, any branch or tag of either repository, a
  clio bundle), never reusing a number another branch holds. Today that means 1.6.6.97 or higher (re-check with
  `git ls-remote --tags` in both repositories at cut time), because main and clio's bundle are at 1.6.6.96 and
  nothing higher is claimed. A cut below a merged higher bundle would be a downgrade
  the convergence check refuses. Re-cuts burn numbers. Cut with
  `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <package at PR head> -Version <claimed>`. This manual cut is
  the only route: ENG-92113 Deliver clioprocessbuiilder package - CI for adding package into clio release added a
  "Deliver to clio" stage as package #83 (2026-10-08), which opened exactly one bot PR (clio #1780, bundle 1.6.6.91),
  and package #92 removed the stage on 2026-10-09. Since then every bundle moved by hand inside a feature clio PR
  (clio #1786 to 1.6.6.93, clio #1793 to 1.6.6.96).
- **Floor = the FINAL cut of that clio PR** (equal to `ExpectedArchiveVersion` when it merges), not the first cut: a
  foreign archive under our first number would satisfy a first-cut floor while lacking the feature.
  ENG-102111 (clio #1786) floored at 1.6.6.92 while bundling 1.6.6.93; this rule is deliberately stricter, for the
  reason above.
- The three literals move with PT, OA and RP. SF does not raise the floor: an older server refuses
  SysFile with a message. describe, list-user-tasks and validate-process-graph stay presence-only; set-active stays
  1.6.1.0. Because describe stays presence-only, an older clio can read a newer package's describe: the block
  survives through `DescribedElement`'s bag, and the ManagerMap arm is in clio from PT (D10).
- Files that move with each raise: the three literals and their comment blocks,
  `ProcessDesignerRequiresPackageAttributeTests.cs:72-73, 111`, the enforced floor clause in three descriptions
  (X3), `docs/McpCapabilityMap.md` floor rows, the new e2e fixtures' `MinimumPackageVersion`; the floor-history
  parenthetical of `ModifyProcessAsNewVersionTool.cs:87-92` (its enumeration ends "... to 1.6.6.40, to 1.6.6.77 and
  to 1.6.6.92" at `:88`, followed by a setTracing clause) is rewritten once without the enumeration.
- If ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph
  validator into the write path (ENG-88414) (ENG-88414 is AI-driven application development; still unmerged on
  2026-10-13, its branch at 1.6.6.36 and 237 commits behind main) lands first, our next
  package PR teaches its key allow-list the new
  write members and its read-back list the describe-only members; if ours lands first, that work adds them on
  rebase. On its own rebase that branch must also allow-list `isTracing` and `enabled` (ENG-102111), which widens the
  files our allow-list step meets.
- No new WCF operation, so `ExpectedOperationContractCount` and `ExpectedAuthorizationGateCallSites` do not move.

**Consequences.** Reinstall the stand in the same breath as a rebundle, or tell the verifier. Each reinstall is an
`install-process-builder` call: since ENG-102116 it needs the user's explicit approval EVERY time and restarts the
site for every connected user (`BundledPackages.ProcessBuilderInstallHint`, `CLIO/clio/Common/BundledPackages.cs:88-91`;
the tool is `Idempotent = false`), so the go-ahead is asked for that call by name, with its restart cost. If a
post-write read-back expectation for the block is ever added (not planned, D19; it would follow the
`{Email,AccessRights,Approval}BlockExpectation` pattern), its warning appends
`BundledPackages.ProcessBuilderInstallRemedy` (`:115-116`), not a bare "update the package". The
refusal and convergence logic itself is unchanged. A clio built from master (bundle 1.6.6.96, floors 1.6.6.92)
refuses the process tools against any stand below 1.6.6.96, such as one still on 1.6.6.54, so any
re-measurement needs the stand upgraded or an older clio, and every record names the package version. net472 package
worktrees live on `C:/Projects/workspace/<short>` (MAX_PATH). Every clio PR states "ClioRing compatibility
reviewed, no Ring-consumed contract changed" (Ring consumes only the catalog entry, its name, Purpose text and
Destructive flag, and counts the Resident flag for its summary line, `clio-ring/ClioRing.Ipc/ClioIpcModels.cs:80-95`;
it asks `get-tool-contract` for that catalog with `detail=index-entries` and reads a missing `destructive` as true,
`clio-ring/ClioRing.Ipc/ClioIpcClient.cs` (`CatalogRequest`); it dispatches any non-destructive tool through clio-run
with only `environment`, `clio-ring/ClioRing/ViewModels/ClioIpcViewModel.cs:144-157`; names, flags and resident
membership are unchanged, and the Purpose leads are confirmed unchanged in the object index, not in the 48-character
line of the default index, when re-measuring). **Owner decision: no.**

## D27. PR split per repository

**Context.** History: one package -> clio -> knowledge triple per ticket or sub-task, the tail merged within about
30-71 minutes of the package merge; one knowledge-first merge shipped a floor no clio bundled
(https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/198, fixed by
https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/201). clio's guard tests bind the archive,
the pins, the descriptions and the floor literal to one tree; there are six or more of them since the three call-shape
drift tests (D25) and `BundledArchive_ShouldDeclareTheTracingWireNamesClioAdvertises`
(`CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:1636`) also bind archive contents to clio text, so a
rebundle cannot leave its feature PR. Package `main` is a release candidate at every moment, because anyone's manual
clio rebundle cuts from it (D26). With per-cut scoping (D13) each cut is self-consistent.

**Options.** (1) one PR per repository for all issues (one cut, but a 12-16k-line package diff, one floor for four
blast radii, a long-lived branch); (2) one triple per Jira issue, stacked and merged in order; (3) a PT triple plus
one FE triple for both sub-tasks (the largest diff of the cycle).

**Decision: option 2, extended by the SysFile Sub-task SF** (the mirror Sub-task MH waited on M3, which refuted
H-1, so it is not created). Within each repository each issue is ONE PR;
no layer splits (each would leave a shippable `main` that saves green and fails at run time). The package PR carries
the descriptor restamp and its tag; the clio PR carries the rebundle, the pins, the descriptions, the floor, the DTOs,
the knowledge-record updates and the e2e; the knowledge PR carries one generation. Merge order per issue: package,
then clio, then knowledge. At most one of our package PRs is in human review at a time; the next ticket iterates as a
stacked draft. Only a human merges.

| Repository | PRs | Titles (exact) |
|---|---|---|
| crt-process-builder (https://creatio.ghe.com/engineering/crt-process-builder) | 4 | `ENG-95984 File process parameter type`; `ENG-96505 Element readiness and object attachments mode`; `ENG-96506 Generated report + process parameter modes`; `<SF-KEY> SysFile attachment storage in the Process file element` |
| clio (https://github.com/Advance-Technologies-Foundation/clio) | 6 | the same four titles, one rebundle PR per package PR, plus two docs-only PRs: `ENG-95984 File process parameter type` (CL-PT-DOC, its BMAD set) and `ENG-92719 File processing element` (CL-DOC, this spec set) |
| clio-knowledge (https://github.com/Advance-Technologies-Foundation/clio-knowledge) | 4 | the same four titles |

14 PRs. Sequencing, hard edges, review gates, contingency splits and the reasons against coarser or
finer splits are in [pr-split](eng-92719-file-processing-element-pr-split.md).

Out-of-scope fixes ship as their own Sub-tasks with their own PRs, off the critical path: "Declared item shape for
Collection process parameters" (D2), "Allow-list the data types a typeFromElement mirror may copy" (D6), "Describe:
decode process-parameter sources into re-appliable names" (D8), all under ENG-95984 File process parameter type;
"Builder-made user tasks do not set SerializeToDB" (D20), "Refuse collection parameters as filter values" (D22), and
"Report the designer storage re-save defect to CrtProcessDesigner" if M11 confirms H-G3-1 (D15), all under
ENG-92719 File processing element. Process file -> Send email attachments needs no Sub-task: ENG-95985 Send email
attachments already owns that scope, and link L5 is the only Jira action (plan section 5.4, C-5).

**Owner decision: yes.** Option 2 with SF (recommended; MH is not created, M3 refuted H-1); option 3 the alternative.

## D28. Test strategy and C# mocking

Full cases and the mocking recipe are in [test-plan](eng-92719-file-processing-element-test-plan.md) and the
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md). The decisions
that shape them:
- **Package unit tests** (NUnit, `Category("PreCommit")`, AAA, `because:`, `[Description]`): hand-built
  `ProcessSchemaUserTask` elements with the real schema UIds and literal parameter names and UIds, registered through
  `UserConnection.SetupProcessUserTaskSchemaManager()` (the `PBT/DeleteDataConfigBinderTests.cs:37-70` pattern); a
  real `new DataValueTypeManager()` registers FileLocator and CompositeObjectList without a database; the hierarchy
  reader, the report reader and the feature seam are faked; the filter target uses `MockEntitySchemaWithColumns` +
  `new ProcessFilterService(UserConnection)` (`PBT/DataNodeFilterTargetTests.cs:38-84`). Tests never name
  `Terrasoft.Configuration` types (the test build has no such assembly); constants are mirrored. Fixture facts
  (source, 2026-10-13):
  - each `SetupProcessUserTaskSchemaManager()` call installs a NEW manager, so all three file schemas and every task
    the code under test resolves are registered on ONE manager (`PBT/UserTaskElementHandlerCreateTests.cs:34-53`,
    `SetupUserTaskSchemas`); since ENG-102112 / ENG-102537 every `UserTaskElementHandler.Create` resolves
    `PreconfiguredPageUserTask`, `AutoGeneratedPageUserTask` and `UserQuestionUserTask` by name, and every ledger
    reconcile resolves the element's task NAME through `FindItemByUId`;
  - on a `TestProcessSchema` the `SchemaUId` setter throws a NullReferenceException, so a build-path test that
    places a file element there writes the field by reflection, as `ProcessBuildHandlerTests` does;
  - adding a collaborator to `SetElementOperation` edits every direct construction, `ReadDataConfigApplierTests`
    included (ENG-102537 had to), so those suites keep their assertions, not their text.
- **What a unit test cannot prove:** the runtime reader (internal), a `ProcessSchemaManager` save and the platform's
  pre-save validation. Those go to the stand (D29). The parameter sync on `SchemaUId` is NOT among them: PK-PT runs the
  platform's real sync in the harness (`PBT/FileCollectionMappingTests.cs` `WireTheSyncManagers` / `PlaceBySync`: a
  substituted `ProcessSchemaManager` whose `AppConnection` answers, a workspace whose provider returns the user-task
  manager, and the element NAMED before `SchemaUId` is assigned), and T-68's regression test uses it. The platform's own `ProcessDesigner.UnitTests` (`ReportFileProcessingUserTask_Tests`,
  `FileProcessing_Tests`, `ProcessFileProcessingUserTask_Tests`) are a reference for semantics only.
- **clio unit:** describe DTOs (an unknown member survives at every level), ManagerMap arm, "forwards the block
  verbatim", budget (and the 19,000 B wire guard with the call-shape drift tests, D25), pins, floor tests; run by
  `TestCategory` (not a Category alias over more than 2,000 tests), from bash with `< /dev/null`. Since ENG-103023
  create, modify, modify-as-new-version and set-active read the process back on every success for the digest, so a
  new command-level unit test stubs `IProcessDescriber.Describe`
  (`CLIO/clio.tests/Command/CreateBusinessProcessCommandTests.cs:27-40`). The digest reports `BuildType ?? Type` and
  `type(referenceSchema)`, so File, FileCollection and a `fileprocessing` element need no digest code (hypothesis,
  source).
- **clio e2e is mandatory for every changed MCP tool** (AGENTS.md MCP policy), in new fixtures per ticket with
  `MinimumPackageVersion` = the floor. Evidence rule: the new fixtures show Passed with **Ignored = 0** on a stand that
  carries the cut (an older package turns them into Ignored, not Failed), and the session's MCP clio is the PR's clio.
  Describe without `mode` returns the full graph only up to 18,000 B, otherwise a summary of at most 3,072 B, so
  every e2e or stand step that asserts on the graph (the `fileProcessing` block, the FileCollection read-back, the
  nested-only binding) passes `mode=full` or `element=<name>`; `ProcessDesignerE2EArrange.DescribeAsync` already
  passes `mode=full`.
- **knowledge:** a pin test per new rule family (including the version-gated multi-instance sentences and the
  FileCollection exception); the size test keeps every article under budget. KB-PT and KB-OA also pass the guards
  added since 2026-10-07: `GuidanceInlineReplyBudgetTests`, `ProcessDigestTests`,
  `ProcessBuilderInstallConsentGuidanceTests`, `ClioToolSyncGuidanceTests`, `ProcessSplitCoreRuleTests` and
  `InternalTaskReferenceTests` (D25).

**Owner decision: no.**

## D29. Stand measurements that precede code

Stand `Creatio` (core 10.1.37, .NET Framework). Every write (a saved process, a
run, a fixture record, an upload) needs the user's explicit go-ahead; schema writes and runs go one at a time
(parallel bursts crash the app pool); cleanup goes through `execute-dataservice-batch` (the stand rejects HTTP
DELETE). Evidence comes from `SysProcessElementLog`. Designer probes read an unsaved in-memory process or are done by
the user, who also checks UI results. Probes that need File parameters or the Process variant are built by a human in
the designer, because the builder cannot build them until these tickets land. Methods are in
[test-plan](eng-92719-file-processing-element-test-plan.md); status is tracked in
[open-questions](eng-92719-file-processing-element-open-questions.md).

| ID | Question | Write? | Decides | Gates |
|---|---|---|---|---|
| M1 | Does a nested-only `Files.File <- File parameter` copy one file? | yes | D18 single-file path | PK-RP code |
| M2 | Does an unset single File make the element fail with an NRE? | yes | guide wording | with M1 |
| M3 | Does the shipped collection mirror read null rows? (H-1) | yes | D9 | PK-PT (done 2026-10-02: no, H-1 refuted; no MH, X4) |
| M4 | Does a FileCollection bound at both levels feed the Process variant, and does its `ObjectFiles` hold the copies? | yes | D2, D5, D18 end to end | verification |
| M6 | What does a flat FileLocator <- collection item do outside a row context? | yes | R-M1 | PK-PT code (done 2026-10-02: reads null; refusal stays) |
| M7 | The exact filter JSON the designer writes for a SysFile `RecordId` scope | no | D16 SysFile shape | PK-SF code |
| M8 | Does an Object element find a Freedom UI upload to a SysFile-stored record? | yes | SysFile sources | PK-SF code |
| M9 | Does a SysFile sort on a record-object column throw? | yes | issue severity | no |
| M10 | Does the designer's object list equal the resolver's prediction? | no | D15 refusal set | PK-OA code |
| M11 | H-G3-1: (a) the Report target half; (b) the harmful Object source half, in memory; (c) optionally save and run | (a)(b) no, (c) yes | D15/D19 severity, bug report, guide warning | PK-OA code ((a), (b)) |
| M12 | Does SysFile + Account reload as plain "Account" and survive a designer re-save? | no | SysFile override (later) | no |
| M13 | Does describe of PrintContractsReport come back converged or as a snapshot? | no | D19 fixtures | PK-OA code (TC-45 Object fixtures) and PK-RP code (TC-45 Report fixtures) |
| M14 | The server-built element shape on 1.6.6.54 (template copies, nested UIds, `BO2`, display) | yes | D20 pins | PK-OA code |
| M15 | Are FastReport packages and printables present? | no | D17 | PK-RP code |
| M16 | Do temporary report files live while parked and disappear at completion, also from a sub-process? | yes | D14/D17 notice wording | no |
| M17 | Empty `ConnectedObjectId`: a foreign-key failure (legacy) and an orphan (SysFile); read-only FK pre-check first | pre-check no, runs yes | notice severity | pre-check before PK-OA code |
| M18 | A constant file-name suffix under a non-default culture | yes | culture note | no |
| M19 | A collection parameter as a filter value fails at run time | yes | urgency of the D22 Sub-task | no |
| M20 | `setElement` with an unknown block next to a known field is silently dropped on 1.6.6.54 | yes | D26 premise | no |
| M21 | A designer SAVE capture of a SysFile-mode Object element with a scope, a target and a sort | yes | SysFile capture for the AC | PK-SF code |
| M22 | The designer opens builder-made parameters and elements, and a no-op panel save changes nothing | yes (a no-op designer save is a schema write; go-ahead) | parity confidence | verification |
| M23 | The SysFile WRITE path: (a) legacy -> SysFile target, (b) SysFile -> dedicated target, (c) Word report -> SysFile target, (d) Process variant -> SysFile target; row fields and the Freedom UI list | yes | SysFile targets | PK-SF code |
| M24 | What an array sent into an object member does at the WCF binder | yes | the `attachments.sort` guide sentence | no |
| M25 | Does `Features.GetIsEnabled` read `UseSysFileInObjectFileProcessing` as true on the stand, and what is the code name? | yes | the SysFile designer notice | PK-SF (notice only) |
| M26 | After "Save to object attachments", does the Object variant's `ObjectFiles` hold the SOURCE locators? (probe in [traps](eng-92719-file-processing-element-traps.md), T-19) | yes | the OA.12 guide sentence and the CL-OA record `after-saving-object-files-points-at-the-source-files.md` | no code (the guide sentence and the record wait on it) |

Timing: M3, M3b, M6's builder mapping, M13, M14, M19, M20 and M24 run on day 0, on 1.6.6.54, before the first cut of
this work is installed; they ran on 2026-10-02 ([open-questions](eng-92719-file-processing-element-open-questions.md)
C.6, C.7). Main has moved to 1.6.6.96 since (2026-10-12), so those records describe 1.6.6.54, and a repeat needs a
1.6.6.54 stand and a clio that still bundles 1.6.6.54 (a master clio refuses the process tools there). The others are
version-independent and may run on any cut ([open-questions](eng-92719-file-processing-element-open-questions.md) B.0,
B.2).

M5 (outer-only `Files` gives an NRE) is skipped: the case is refused anyway. **Owner decision: no** for the plan;
every row with "Write? yes" needs the user's go-ahead before it runs.

---

# Part D. Acceptance criteria: proposed replacement text

Blocks D-2..D-4 replace the "Acceptance criteria" section of ENG-92719 File processing element, ENG-96505 Element
readiness and object attachments mode and ENG-96506 Generated report + process parameter modes. The table under
each block lists every change and its reason. D-1 holds no AC text of its own: it points to AC-1..AC-10 in §1.4 of
the ENG-95984 File process parameter type plan, which is the one replacement text for that issue. D-5 holds the
proposed Sub-tasks.
The issue wording was read on 2026-10-01.

### D-1. ENG-95984 File process parameter type

The replacement text is AC-1..AC-10 of the
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md)
section 1.4, with its description edit ("the Binary / File slice of ENG-92730 Complex / structured parameter types" ->
"the File (FileLocator) slice of ENG-92730 Complex / structured parameter types"). That is the text pasted into
Jira, and the ENG-95984 File process parameter type test-plan traces it (its section 9). The table of what the ticket
says against what is true is in that plan, section 1.3.

The shorter block that stood here was retired on 2026-10-01 for three reasons. It left out six rules (the
setParameter shape refusal, the item-aware delete guard, the meta-path rule, the two mirror refusals, the nested-only
listing and the floor move). It listed only FileLocator among the D1 aliases. It placed the ReportFiles mapping
verification in ENG-96505 Element readiness and object attachments mode, which configures only the Object schema
(D13).

### D-2. ENG-92719 File processing element

> **Acceptance criteria**
> - All three source variants (Object attachments, Generated report, Process parameter) and the save target can be
>   built through create and modify as one element, type `fileProcessing`, and read back by
>   `describe-business-process`. Delivered by ENG-96505 Element readiness and object attachments mode (Object
>   attachments), ENG-96506 Generated report + process parameter modes (Generated report, Process parameter) and the
>   Sub-task "SysFile attachment storage in the Process file element" (objects without their own attachment object).
> - An element's output collection can be mapped by more than one downstream element.
> - Guidance covers which source variant to pick for a given intent and the element pairings from the attached
>   use-case inventory.
>   - Creating a new report (printable) is out of scope. The report must already exist; when none fits, the refusal
>     tells the user to create it in System Designer, Report setup, and the element never picks an unrelated one.
> - Values are set through the ENG-91844 Implement full parameter mapping (sources) value sources, including File and
>   FileCollection parameters (ENG-95984 File process parameter type).
> - Server serialization matches a capture per source variant and per storage: shipped captures for Object attachments
>   in a dedicated attachment object (SkillFilesValidationProcess), Generated report (PrintInvoiceReport) and Process
>   parameter (FileParameterProcess). SysFile storage is compared in the SysFile Sub-task, against a designer capture
>   taken on the stand, because no shipped SysFile capture exists. The comparison follows serialization-capture
>   section 10:
>   - Element UId, name and lane are ignored (elements are paired by caption), and so are parameter and nested UIds.
>     The nested items' `IL2` is still compared.
>   - `ModifiedInSchemaUId` is normalised for values equal to the template default.
>   - Four deviations are named and tolerated:
>     - N2: `BL8` is absent on builder-made elements.
>     - N9: a builder-made process parameter stores `IL2` = the process UId.
>     - N10: a nested item has no `BK15` mapping row.
>     - N13: the DisplayValue rows of hidden constants are reported, not compared.
>   - The format normalisations N11 and N14-N16 apply.
>   - Any other difference fails.
> - Tests cover Read data -> Process file, Process file -> Modify data (re-finding the created file), Process file ->
>   multi-instance sub-process per file, Object attachments -> Process parameter chain, and Add data -> generated
>   report. Process file -> Send email is covered by ENG-95985 Send email attachments. Process file -> Creatio.ai call
>   is out of scope (ENG-92725 Execute AI Intent element (BP generation) is closed as Won't Do), and the guidance says
>   it is not buildable through this tool yet. Docs and MCP surface updated.

| AC as written (excerpt) | Change | Reason |
|---|---|---|
| "All three source variants ... can be built" | names the token and which sub-task delivers each part | one element, one token (D10); SysFile split out (D15) |
| "Values set via Task 6, including the Binary / File parameter type (Task 5)" | names the issues and the real types | wording; Binary cannot work (D1) |
| "Server serialization matches a designer-built capture per source variant" | per variant AND per storage, with named captures and the comparison rule, including its four named exceptions (N2, N9, N10, N13); the SysFile capture (M21) is the SysFile Sub-task's | no shipped SysFile capture exists (0 of 30); template defaults keep the template's provenance while builder-chosen values carry the process UId, so GS5 is normalised for template-default values (D20); builder output differs on four keys the plans already accept, so a bare "UIds ignored" rule fails the first parity run (D20; serialization-capture section 10); SysFile ships in its own Sub-task (D15) |
| "Tests cover the five patterns" | five buildable patterns; Send email moved; Creatio.ai out | consumers need dynamic slots the builder cannot create (D22) |

### D-3. ENG-96505 Element readiness and object attachments mode

> **Acceptance criteria**
>
> *Element readiness (shared across all modes)*
> - The element (type `fileProcessing`, alias `processFile`) can be added to a process layout through create and
>   modify and connected like any other supported element; it is drawn as the designer draws it (manager item = the
>   schema, 69x55, in a lane).
> - `describe-business-process` returns the element with its source variant and configuration in a `fileProcessing`
>   block written in the build vocabulary; feeding the write members back rebuilds the element.
>   - An Object attachments element in an existing process, built in the Process Designer or by an earlier
>     generation, is read back and modified without loss. Report and Process-parameter elements are described and
>     preserved as generic user tasks until ENG-96506 Generated report + process parameter modes; a modify of a
>     process that holds one leaves it intact. A designer-built element that uses SysFile storage is described
>     without loss; changing it is refused until the SysFile Sub-task.
> - The element's output file collection can be mapped by more than one downstream element.
> - Element identity resolves to the user-task schema per source variant; the variant is the group the request sends,
>   so adding a variant later does not change the layout contract.
> - Building ObjectFileProcessingUserTask as a generic `userTask` is refused with a message that points to
>   `fileProcessing`.
>
> *Object attachments mode*
> - The source object is chosen by name in `attachments.object`; its attachment storage is derived per object as the
>   designer derives it.
> - Attachment storage in SysFile is not part of this sub-task. Such sources and targets are refused with a message,
>   designer-built SysFile elements are described without loss, and the Sub-task "SysFile attachment storage in the
>   Process file element" delivers the support.
> - The record whose attachments are read is set in `attachments.recordId` through the value sources: a record id, a
>   process parameter, an element output or one column of it, or `[#SysVariable.CurrentUserContact#]`. The number of
>   files (default 50, at most 5000) and one sort column can be set.
> - Files can be saved to a target record's Attachments detail (`saveTo`), with the target record set through the same
>   value sources; a constant record id must be an existing record of the target object.
> - A saving element with no record scope and no filter condition is refused; a "use in process" element without
>   one gets a notice.
> - Server serialization matches the designer-built capture SkillFilesValidationProcess. The comparison rule:
>   - Element UId, name and lane are ignored (elements are paired by caption), and so are parameter and nested UIds.
>     The nested items' `IL2` is still compared.
>   - `ModifiedInSchemaUId` is normalised for values equal to the template default.
>   - Four deviations are named and tolerated:
>     - `BL8` is absent on builder-made elements (N2).
>     - A process parameter's `IL2` is the process UId (N9).
>     - A nested item has no `BK15` row (N10).
>     - The DisplayValue rows of hidden constants are reported, not compared (N13).
>   - The format normalisations N11 and N14-N16 apply.
>   - Any other difference fails (serialization-capture section 10).
>
> *General*
> - Guidance covers three points:
>   - "Most shipped uses end in a record's attachments; when a following element needs the files, map the element's
>     output collection." TC-88 pins this sentence.
>   - The Object attachments pairings from the use-case inventory.
>   - Which consumers are not buildable yet.
> - Unit tests, e2e, stand verification, clio MCP tool descriptions and clio-knowledge guidance updated in the same
>   change, merged package, then clio, then knowledge. **Public surface: coordinated change.**

| AC as written (excerpt) | Change | Reason |
|---|---|---|
| "read back correctly, including a source variant not configurable in this sub-task, without loss" | Report and Process elements stay generic until ENG-96506 Generated report + process parameter modes; SysFile elements described, changes refused | each cut claims only the variant it configures (D13) |
| "The source object and the record whose attachments are read are set through the established value sources" | the object is chosen by name; the record is a value source inside the group | the object is a schema choice, not a value; one value-source vocabulary per element (D11, D16) |
| "Server serialization matches a designer-built ObjectFileProcessingUserTask capture" | names the capture and the comparison rule with its four named exceptions (N2, N9, N10, N13); SysFile capture moves to the SysFile Sub-task | D20 (a bare "UIds ignored" rule fails the first parity run on keys the plans already accept), D15 |
| "Guidance states that Process file is rarely an endpoint" | replaced by "most shipped uses end in a record's attachments; when a following element needs the files, map the element's output collection" (plan C-2); TC-88 pins the corrected sentence | measured on the corpus, 2026-10-01, and re-counted for this edit: 16 processes (30 elements) hold the element. The 4 product processes (CrtInvoice PrintInvoiceReport, CrtLeadOppMgmtApp PrintQuotationReport, CrtOrderContractMgmtApp PrintContractsReport, CrtEmailMarketingApp GenerateDNSRecordsSpecification) hold 5 elements, all of them Generated report with `ResultActionType` 0, saving to the record's attachments. None maps an output of the element. The Object attachments and Process parameter variants occur only in test packages ([use-cases](eng-92719-file-processing-element-use-cases.md); plan N4). In shipped product content the element IS the endpoint, so the original sentence would have TC-88 pin a measured-false claim |
| (missing) | generic-route refusal; empty-filter refusal; SysFile refused in this sub-task | D13, D16, D15 |

### D-4. ENG-96506 Generated report + process parameter modes

> **Acceptance criteria**
>
> *Generated report mode*
> - The report is chosen in `report.printable` from the MS Word printables whose object is the record being reported
>   on, preferably by the `templateId` that list-printables returns (a caption or a lookup macro is also accepted; an
>   ambiguous caption is refused with the candidates). The record is set in `report.recordId` through the established
>   value sources. FastReport and DevExpress printables are refused in this sub-task.
> - The generated files can be saved to a record's Attachments detail, or handed to a following element as a file
>   collection (temporary files that live as long as the process instance).
> - Creating a new printable is out of scope. When no suitable printable exists, the refusal tells the person to
>   create it in System Designer, Report setup, and the element never picks an unrelated printable.
> - A Word report with no record scope and no filter condition is refused.
>
> *Process parameter mode*
> - The file source is set in `files`: a FileCollection or File process parameter, an element's file output, or a
>   formula. A collection source binds both levels of the element's `Files` input. A single File is accepted if stand
>   measurement M1 shows that the platform copies it; otherwise it is refused, and the guidance says to wrap it in a
>   FileCollection.
> - The files are saved to a record's Attachments detail (a target is required); the copies are then available to
>   following elements (`ObjectFiles.ObjectFile`, `CreatedObjectFileIds`). "Use in process" does not exist for this
>   source (the platform throws) and is refused.
>
> *General*
> - `describe-business-process` round-trips both variants: the source, the printable or the input files, the record,
>   and the save target. The shipped product processes with report elements describe as `buildType: fileprocessing`
>   (checked read-only).
> - Server serialization matches the shipped captures PrintInvoiceReport (Generated report) and FileParameterProcess
>   (Process parameter). The comparison rule:
>   - Element UId, name and lane are ignored (elements are paired by caption), and so are parameter and nested UIds.
>     The nested items' `IL2` is still compared.
>   - `ModifiedInSchemaUId` is normalised for values equal to the template default.
>   - Four deviations are named and tolerated:
>     - `BL8` is absent on builder-made elements (N2).
>     - A process parameter's `IL2` is the process UId (N9).
>     - A nested item, including `Files.File`, has no `BK15` row (N10).
>     - The DisplayValue rows of hidden constants such as `ReportId` and `IsSeparateReports` are reported, not
>       compared (N13).
>   - The format normalisations N11 and N14-N16 apply.
>   - Any other difference fails (serialization-capture section 10).
> - Guidance covers which source variant to pick for a given intent and the pairings for these two variants: Read
>   data, Add data (creating the report's data row), Modify data (re-finding the file); it states that Send email
>   attachments (ENG-95985 Send email attachments) and Creatio.ai file passing are not buildable through this tool yet.
> - Unit tests, e2e, clio MCP tool descriptions and clio-knowledge guidance updated in the same change.

| AC as written (excerpt) | Change | Reason |
|---|---|---|
| "selected from the reports that already exist on the target object" | the printables whose object is the reported record; MS Word only | "target object" is ambiguous: the save target is a different object; FastReport has no stand evidence, DevExpress throws (D17) |
| Process parameter mode: "The file source is a process parameter of the File type" | a FileCollection, a File (pending M1), an element output or a formula | D18 |
| Process parameter mode: "saved ... or handed to a following element as a file collection" | saved only; the copies are then available | the platform throws for "use in process" (D14) |
| "Server serialization matches a designer-built ProcessFileProcessingUserTask capture" | adds the Report capture; names both, and the comparison rule with its four named exceptions | the Report variant had no serialization criterion; a bare "UIds ignored" rule fails the first parity run on keys the plans already accept (D20) |
| "Guidance covers ... Send email ... and a Creatio.ai call" | states that both are not buildable yet | D22 |

### D-5. Proposed new Sub-tasks

| Parent | Title | Acceptance criteria (proposed) |
|---|---|---|
| ENG-92719 File processing element | SysFile attachment storage in the Process file element | Objects without their own attachment object can be read from and saved to (SysFile storage) by all three variants, after stand measurements M7, M8, M21, M23 and M25; the record scope uses the shape the designer writes; record-object sort columns are refused; a SysFile-mode element built where the designer's storage flag is off gets a notice; serialization matches a designer capture taken on the stand; guidance states the SysFile hazards |
| ENG-95984 File process parameter type: **not created** (it waited on M3, and M3 refuted H-1 on 2026-10-02) | typeFromElement collection mirror leaves its items unbound | none: binding the items is one parity commit in PK-PT (contingency X4) |
| ENG-95984 File process parameter type | Declared item shape for Collection process parameters | (to be refined; D2 option C) |
| ENG-95984 File process parameter type | Allow-list the data types a typeFromElement mirror may copy | (to be refined; D6) |
| ENG-95984 File process parameter type | Describe: decode process-parameter sources into re-appliable names | (to be refined; D8) |
| ENG-92719 File processing element | Builder-made user tasks do not set SerializeToDB | (to be refined; D20) |
| ENG-92719 File processing element | Refuse collection parameters as filter values | (to be refined; D22) |
| ENG-92719 File processing element (only if M11 confirms H-G3-1) | Report the designer storage re-save defect to CrtProcessDesigner | (to be refined; D15) |
| ENG-92719 File processing element (only on contingency X2: RP's Process-parameter half fails its stand proof) | Process parameter source of the Process file element | the Process-parameter mode rows of D-4, moved out of ENG-96506 Generated report + process parameter modes |

Process file -> Send email attachments gets no Sub-task: ENG-95985 Send email attachments owns it (link L5).

When each Sub-task is created (now, when its parent's PR opens, or only after its measurement) is in
[pr-split](eng-92719-file-processing-element-pr-split.md) section 12.3; a conditional Sub-task is not created before
its measurement, because that would record an unproven defect as fact.

**Done rule** (pr-split 12.4): ENG-95984 File process parameter type is done when its knowledge PR merges;
ENG-96505 Element readiness and object attachments mode, ENG-96506 Generated report + process
parameter modes and SF are each done when their own knowledge PR merges; ENG-92719 File processing element is done
when all three are. The follow-up Sub-tasks do not gate their parent: the three ENG-95984 File process parameter type
follow-ups (D2, D6, D8) and the two ENG-92719 File processing element side Sub-tasks (D20, D22) are separate scope
(D27). A parent closes with them open; the owner confirms that the team's Jira workflow allows this (Q9).

---

## Appendix A. Refusal and notice catalogue

New server-side texts; none costs tool-description bytes.

| ID | Kind | Trigger | Decision |
|---|---|---|---|
| F-T1 | refuse | `type: Binary` or `blob` | D1 |
| F-T2 | refuse | `referenceSchema` with File or FileCollection | D2, D7 |
| F-T3 | refuse | a constant on a File, or `addMapping value` onto a FileLocator or collection | D7 |
| F-T4 | refuse | `setParameter type: FileCollection` on another shape | D7 |
| F-T5 | refuse | dotted mirror source | D6 |
| F-T6 | refuse | mirror of a file output (the message points to FileCollection) | D2 |
| F-T7 | refuse | a replayed mapping naming a FileCollection item the rebuild named `File` | D2 |
| F-M1 | refuse | P1: a paired parent is bound to anything else (another collection or a formula) | D5 |
| F-M2 | refuse | R-M1: flat FileLocator <- a collection item | D5 |
| F-M3 | refuse | R-M2: a collection without a file item into a file-consuming collection (the source "has no File item") | D5 |
| F-M4 | notice | P2 item bound, rebound, reset (bound outside the new source and unpairable) or left unpaired; P2-MI empty callee file item, also when P1 binds the multi-instance input; P3 reset of EVERY bound collection above a file item, one notice per level | D5 |
| F-M5 | refuse | P1: the source is nested deeper than the target (outside its row it reads empty); a deeper TARGET is not refused | D5 |
| F-E1 | refuse | the generic `userTask` route, per cut | D13 |
| F-E2 | refuse | raw `addMapping` onto a binder-owned configuration parameter, per cut | D13 |
| F-E3 | refuse | no group or two groups on create; another variant's group on update (dependents named); a `source` check mismatch or an entity-like `source`; a contradicting `userTaskName` | D10, D12 |
| F-E4 | refuse | a group that does not match the variant | D11 |
| F-E6 | refuse | `action` contradicting `saveTo` on create; `useInProcess` on the Process variant; `saveTo` missing on the Process variant; `useInProcess` while `CreatedObjectFileIds` is mapped | D12, D14 |
| F-E7 | refuse or warn | storage R1-R9, W6, W7; a `recordId` constant empty, unknown or of another object; SysFile sources and targets until SF | D15 |
| F-E8 | refuse | an identity check mismatch (`storage`, `fileObject`, `attachments.linkColumn`, `report.object`, `report.type`) | D11 |
| F-E9 | notice | retarget cleared or kept; repair on touch; action switched by `saveTo`; a mapped `recordId` may be empty; a SysFile element where the designer flag reads off (SF, after M25) | D12, D15 |
| F-F1 | refuse | filter root mismatch, unconfigured element, record-scope type, scope set twice, a record-scope formula other than CurrentUserContact | D16 |
| F-F2 | refuse or notice | empty filter at the end of the request, touched elements only, `failedOperationIndex: null` | D16 |
| F-F3 | refuse | a sort column not on the runtime root; sort on Report; `numberOfRecords` outside 1..5000 | D16 |
| F-F4 | notice | `numberOfRecords` omitted on a saving or forwarding element | D16 |
| F-R1 | refuse | printable missing, empty, unknown, unreadable (with the Report setup hint and the `templateId` bridge), ambiguous, DevExpress, FastReport, no object | D17 |
| F-R2 | refuse | a bad `fileNameSuffixColumn`; suffix and column together | D17 |
| F-R3 | notice | ConvertInPDF; several records into one target; feature off; temporary report files into an Out parameter; Word `separateReports: false` accepted | D14, D17 |
| F-P1 | refuse | a `files` source with no FileLocator item; outer-only; none; a mapping FROM `Files`; a `value` | D18 |
| F-P2 | notice | `files.expression` written as given onto `Files.File` (only its meta-path tokens are checked) | D18 |

### F-T and F-M texts as PK-PT writes them (draft PR #96, 2026-10-14)

Quoted from `PB/Parameters/ProcessParameterService.cs`, `PB/Parameters/ProcessParameterValueValidator.cs`,
`PB/ProcessSchemaElementLocator.cs`, `PB/Mappings/CollectionBindingRules.cs` and `PB/Mappings/ProcessMappingService.cs`.
`<...>` marks a value the code fills in; `[Element '<E>': ]` is present only when the target is an element's
parameter. `<binding>` names a stored binding: `'<Element>.<path>'`, `'<path>'` for a process parameter, or
`the formula '<text>'`. `<items>` is `Its items are '<A>', '<B>'.` (at most 20, then a count), or `'<name>' has no
items.` In F-M texts, a target path is written without its element, a source with it; the exception is the opening
`Mapping for target '<target>'` of F-M2, F-M3 and F-M5, where `<target>` includes the element name (`PF1.Files`; a
process parameter has none).
- **F-T1** `Parameter type '<type>' is not supported: a process parameter carries a file by reference, not its bytes. Use type File (one file) or FileCollection (several files).` Any other unsupported name gets the generic text, which now lists `..., Guid, File, FileCollection, Collection (shape it via 'typeFromElement'), and Lookup (set a 'referenceSchema').`
- **F-T2** `Process parameter '<name>' is a File and cannot have a 'referenceSchema': a file is carried by reference to its stored content (FileLocator), not as a lookup to an object. Drop 'referenceSchema', or drop 'type' to declare a Lookup.` / `Process parameter '<name>' is a FileCollection and cannot have a 'referenceSchema': its shape is fixed (one File item), and a file is carried by reference to its stored content, not as a lookup to an object.`
- **F-T3** on `addParameter` / `setParameter`: `Process parameter '<name>' is a File and cannot carry a constant default 'value': a File has no constant form; map it from an element's file output or another File parameter.` On `addMapping value` (every constant route): `Value '<value>' is not valid for parameter '<name>' of type <type>: a file has no constant form. Map it from an element's file output or another File parameter ('sourceElement' + 'sourceElementParameter', or 'processParameter').` / `... of type <type>: a collection has no constant form. Map an element's collection output or another collection into it ('sourceElement' + 'sourceElementParameter', or 'processParameter').`
- **F-T4** `Process parameter '<name>' is a collection of another shape: type FileCollection would change this collection's shape (a FileCollection has exactly one File item and no tag). Remove it and add it again with type FileCollection.`
- **F-T5** `'<path>' is an item of collection '<collection>' on '<element>'. A mirror copies a whole parameter: mirror '<collection>' for its shape, or declare type File / FileCollection.`
- **F-T6** `Cannot mirror collection '<element>.<output>': its item '<item>' is a file (FileLocator) with no column identity, so a mirror cannot reproduce this file collection. To keep these files, declare type: FileCollection and map it from '<element>.<output>'; both levels are bound by the mapping.`
- **F-T7** `Process parameter '<path>' was not found: '<collection>' has no item '<segment>'. <items> A dotted name addresses an item of a collection process parameter, and the run time binds items by name and delivers nothing for a name it does not find.`
- **F-M1** `[Element '<E>': ]'<parent>' is bound to <binding>; map '<parent>' to '<source parent>' first: <then>. A mapping onto '<target>' from '<source>' binds '<parent>' to '<source parent>' as well, because each row of a collection comes from the collection it is bound to - one collection cannot take its rows from two.` `<then>` is `its items follow` when `<parent>` takes files (P2 pairs its items), and otherwise `then map '<target>' again`.
- **F-M2** `Mapping for target '<target>': a single File cannot take an item of '<source collection>'; use a FileCollection, or a multi-instance sub-process. Outside a row of that collection '<source>' reads empty at run time, so this mapping would save and deliver no file.`
- **F-M3** `Mapping for target '<target>': '<source>' has no File item, so it cannot fill a collection that takes files - every row would reach '<target>.<file item>' with no file, and the element that reads it fails at run time. <items> Map a collection whose items include a File (FileLocator): an element's file output, or a FileCollection process parameter.`
- **F-M4**, one per decision:
  - P2 bound: `[Element '<E>': ]'<target>' takes its rows from '<source>', so its file item was bound to match: '<target>.<item>' now reads '<source>.<source item>'.`
  - P2 rebound: `[Element '<E>': ]'<target>.<item>' was bound to <binding>, which is not an item of '<source>'; it was rebound: '<target>.<item>' now reads '<source>.<source item>', to match '<target>'.`
  - P2 reset (bound outside the new source, unpairable): `[Element '<E>': ]'<target>.<item>' was bound to <binding>, which is not an item of '<source>', and could not be paired with one - none is named '<item>' and there is not exactly one File item on each side; it was cleared, because left bound that one value would be copied into every row, so at run time it will be empty. Map it with addMapping onto '<target>.<item>' from the item of '<source>' it should read.`
  - P2 unpaired (unbound, left as it is): `[Element '<E>': ]'<target>.<item>' could not be paired with an item of '<source>' - none is named '<item>' and there is not exactly one File item on each side - so at run time it will be empty. Map it with addMapping onto '<target>.<item>' from the item of '<source>' it should read.`
  - P2-MI, for each FileLocator item of the multi-instance input that the plan leaves unbound, whether the input is the named target or a parent P1 binds: `Element '<SP>': callee parameter '<F>' will be empty on every iteration unless you map '<input>.<F>' - the iterated collection fills only the items mapped from it.`
  - P3, one per bound level above the file item: `[Element '<E>': ]'<level>' was bound to <binding>; it was cleared because '<target>' now takes a single file.`
- **F-M5** `Mapping for target '<target>': the source '<source collection>' is a collection nested inside another collection; outside its row it reads empty at run time, so '<target parent>' would take no rows from it. Map from a root collection, or use a multi-instance sub-process.` A defensive twin (`... is inside '<parent>' itself, so '<parent>' cannot take its rows from it - a collection bound to one of its own items would read itself.`) is unreachable with chains that start at a root and is not catalogued.

P3's scope as built: a FileLocator target item whose owner is single-instance, which is any element but a
multi-instance sub-process, and the process itself, so a process-level `Docs.File` counts. P3 is not limited to
file-consuming targets.

## Appendix B. Review log

### Round 1: two adversarial reviewers, 32 findings (8 high, 9 medium, 15 low)

"Accepted" means the finding's fix, or an equivalent, is in the decision named; "accepted, changed" means the
problem is fixed by a different change than the one proposed. Nothing was rejected outright.

| # | Sev. | Finding | Disposition | Where |
|---|---|---|---|---|
| A1 | high | A retarget keeps a foreign filter in SysFile mode: `ClearIfForeign` compares only the root name, which is SysFile for every record object | Accepted. Any change of `attachments.object` that resolves to a different storage pair clears filter, scope and sort; `ClearIfForeign` is not reused. Report: a different report object clears, the same keeps (documented divergence) | D12 rule 2, D16, D19, D21 |
| A2 | high | The SysFile WRITE path has no measurement, yet SysFile was the default target | Accepted. New M23 before code; SysFile pending M8/M23 with a fallback. Round 2 moved SysFile to SF | D15, D24, D29 |
| A3 | medium | M11 measured the designer re-save hazard on a Report element, which has no source side | Accepted. M11 extended to a legacy Object element in memory, plus an optional run; repair on touch; bug-report Sub-task and guide warning if confirmed | D12 rule 5, D15, D19, D29 |
| A4 | medium | P2 and R-M2 in the shared `ApplyMapping` would refuse or pair multi-instance `InputRecordCollection` mappings | Accepted. P2 and R-M2 run only for file-consuming targets; multi-instance gets the P2-MI notice; regressions added | D5, D22 |
| A5 | low | The SysFile scope shape is most likely the designer's lookup InFilter | Accepted. Default lookup InFilter; CompareFilter only if M7 shows it; `Split` accepts both | D16, D29 |
| A6 | low | Pinning `ConsiderTimeInFilter` with this schema's provenance diverges from the designer captures | Accepted, extended. Template defaults are an exact copy of the template value; the AC comparison normalises provenance for template defaults | D20, D24 |
| A7 | low | The "files are deleted" notice is wrong for Object `useInProcess` | Accepted. Report only; the Object guidance says the locators point at the source | D14 |
| A8 | low | The nested-UId re-minting order was implicit | Accepted. Create only, before configuration and any same-request mapping; never on modify | D20 |
| A9 | low | Not reading the flag gives no signal where the designer flag is off | Accepted. A notice through the shared feature seam, gated on M25 | D15, D29 |
| A10 | low | A `saveTo.recordId` constant takes the format-only Guid branch | Accepted. Validated as a lookup of the record object | D15, D21 |
| B1 | high | A `filter.recordId` lift is dropped by clio's bagless filter DTOs | Accepted, changed: the record scope moved into the block; filter DTOs untouched | D11, D16, D19 |
| B2 | high | "Read-only members accepted only when equal" refuses correct resubmissions | Accepted. Two kinds: identity checks vs describe-only members | D11, D19 |
| B3 | high | The binder rules did not say how an `expression` source is classified | Accepted. Rules run for structured sources only; expressions pass as given (since 1.6.6.79 the package checks their meta-path tokens); single-token element-source decode pulled into PT; `files.expression` accepted | D5, D8, D18 |
| B4 | high | "A supplied group REPLACES the member" loses configuration silently | Accepted. Member-wise merge; empty value clears; value sources replace whole | D12 rule 1, D25 |
| B5 | high | A FileCollection described as `CompositeObjectList` rebuilds shapeless and green | Accepted. Describe emits `FileCollection`; the guide exception is measured with the type-list edit | D2, D3, D25 |
| B6 | high | Two `recordId` vocabularies in one element | Accepted, by the B1 move: one value-source contract; only CurrentUserContact accepted as a formula | D11, D16, D24 |
| B7 | medium | `attachments.sort` as an array diverged from readData; the WCF mismatch is unmeasured | Accepted. readData shape; stored multi-entry sort kept while untouched; M24 | D11, D16, D19, D29 |
| B8 | medium | `numberOfRecords` omitted means 50 here but "all" in readData | Accepted. Stated in contract and guide; a create-time notice | D11, D16, D25 |
| B9 | medium | `source` collides with `readData.source` and `files.processParameter` | Accepted. The variant is the group present; `source` is an identity check | D10, D11 |
| B10 | medium | In OA no route builds a Report element, and `setFilter` on shipped Report elements would start being refused | Accepted. Every claim is scoped to its cut | D10, D12, D13, D16, D19, D24, D27 |
| B11 | medium | A SysFile-to-SysFile retarget keeps a foreign `RecordId` filter | Accepted with A1 | D12 rule 2 |
| B12 | medium | Action inference could flip a saving element on a partial update | Accepted. Changes only when sent, or when `saveTo` is sent to an in-process element | D12 rule 4, D14 |
| B13 | medium | P1 contradicts two shipped multi-instance guide passages | Accepted. Both rewritten with a version gate in the PT knowledge PR | D5, D25 |
| B14 | low | The ManagerMap arm shipping with the emitting archive leaves an older-clio window | Accepted. The arm ships in the PT clio PR | D10, D23, D26 |
| B15 | low | `userTaskName` on `fileProcessing` unspecified | Accepted. Ignored when it matches, refused when it contradicts | D10 |
| B16 | low | Agents will try `processFile` | Accepted. A second token; describe emits one spelling | D10 |
| B17 | low | The first server refusal of a listed user task needs explaining | Accepted. element-catalog sentence and the refusal text | D13, D25 |
| B18 | low | `list-printables` says `templateId`, the block says `printable` | Accepted. Guide bridge sentence and refusal text | D17, D25 |
| B19 | low | End-of-request refusal scope and `failedOperationIndex` unspecified | Accepted. Touched elements only; `failedOperationIndex: null` | D16 |
| B20 | low | RP does change the descriptions (floor clause) | Accepted | D25, D26 |
| B21 | low | Refusing Word `separateReports: false` breaks resubmission of a legacy element | Accepted. Accepted with a notice | D17 |
| B22 | low | `attachments.linkColumn` read-only vs writable `saveTo.linkColumn` | Accepted. Identity check whose refusal names `saveTo.linkColumn` | D11 |

### Round 2: the PR-split judgement (three competing split proposals, judged)

The judgement made five changes (C1-C5): it re-opened two decisions, replaced two delivery rules and moved one test
probe. All five are reflected above.

| # | Change | Reason | Where |
|---|---|---|---|
| C1 | SysFile storage leaves OA unconditionally, into the new Sub-task SF; OA refuses SysFile sources and targets and describes designer-built SysFile elements without loss | SysFile has no runtime evidence; its five pre-code measurements include three that need the user to build designer probes; carving it out takes them and the two SysFile-only high findings (A1, A2) off OA's path | D15, D16, D24, D27, D29 |
| C2 | If M3 confirms H-1, the mirror fix is its own Sub-task MH, delivered first, introducing `BindCollection` with explicit pairs; PT adds the P2 policy on top. Resolved later: M3 refuted H-1 on 2026-10-02, so MH was not created and X4 applies | a silent defect in shipped behaviour gets its own record and revert path, and the fix fits the fast review lane | D9, D5 |
| C3 | Version numbers are claimed by rule at cut time, and the floor equals the FINAL cut of each clio PR (not 1.6.6.55, .56, .57 fixed in advance, and not the first cut) | two of the team's package PRs in review at once stamped the same number twice; a foreign archive under a first-cut number would satisfy the floor without the feature | D26, D25 |
| C4 | The PR set is 13 PRs (16 with MH) (14 PRs, 17 with MH, once the ENG-95984 File process parameter type plan added CL-PT-DOC), with a docs-only clio PR for this spec set; at most one of our package PRs in human review at a time. Resolved later: M3 refuted H-1, MH was not created, so the set is 14 PRs | approvals are dismissed on push in the package repository; the docs PR satisfies "no PR before its story file" | D27 |
| C5 | The PBT probe that used Binary to exercise the generic unsupported-type message moves to another unsupported type | Binary now has a dedicated message, and the clio e2e that pins the refusal never runs in TeamCity, so the PBT is the only automated guard | D1 |

### Round 3: cross-document review of Part D and D25 (3 findings: 2 high, 1 medium)

Each finding was re-checked in source or in the corpus before it was applied. This round edited this document only.
The sibling documents each finding names were aligned in the reconciliation of 2026-10-01
([README, "Resolved during review"](README.md#resolved-during-review)).

| # | Sev. | Finding | Disposition | Where |
|---|---|---|---|---|
| R3-1 | high | Part D's comparison rule "UIds ignored, `GS5` normalised" fails on four keys the plans already accept (serialization-capture N2, N9, N10, N13) | Accepted. Verified in `CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:291-305` (the `BK15` sync covers top-level parameters only), `BaseProcessSchemaElement.cs:217` with `PB/Parameters/ProcessParameterService.cs:65, 478` (process-parameter `IL2`), and crt-process-builder `docs/script-task-element-capture.md:30-32` (`BL8`). D-1 carried N9 (N2, N10 and N13 are element keys) until the reconciliation of 2026-10-01 moved the ENG-95984 File process parameter type criteria to its plan's AC-8 rule. D-2, D-3 and D-4 carry all four exceptions, and D20 states the full rule and where each sibling document states it | D20, D24, D-1..D-4 |
| R3-2 | high | D25 left the MCP prompts out of scope on a false premise: they enumerate the dedicated element types, their blocks and the generic `userTask` route | Accepted, extended. Verified on clio master `03ef3944f`: `ListUserTasksPrompt.cs:22-29`, `CreateBusinessProcessPrompt.cs:25-29, 44-55, 104-106`, `ModifyBusinessProcessPrompt.cs:92-129`. `DescribeProcessPrompt.cs:33-35` (the block list) was added to the finding. Resources and `clio/tpl` were checked: neither refers to these tools or schemas. The PR MCP statement now names prompts, resources and tpl | D13, D25 |
| R3-3 | medium | D-3 kept "Process file is rarely an endpoint", which the corpus disproves | Accepted. Re-counted in `PS` for this edit: 16 processes; 4 product processes with 5 elements, all Report variant with `ResultActionType` 0. The sentence is replaced by the plan C-2 wording, with a change-table row, and TC-88 pins the corrected sentence | D-3, D25 |

### Round 4: residual cross-document consistency (4 findings: 3 applied, 1 not applied)

Each finding was re-checked against D10, D20, serialization-capture section 10 or the source before it was applied.
This round edited this document only.

| # | Finding | Disposition | Where |
|---|---|---|---|
| R4-1 | D-3 and D-4 stated a shorter comparison rule than D-2 and D20: no N1 pairing by caption (element name and lane) and no N11, N14-N16 format normalisations, so the OA and RP criteria would fail on the element name, a stale `BK15.GT1`, request-culture resource rows, the three "no value" forms and filter key ordinals | Accepted. D-3 and D-4 now carry D-2's N1 bullet and its format-normalisation bullet word for word. Their `GS5` bullet takes D-2's wording too, because "template-default values" could be read as values the template wrote, and D20 normalises any value equal to the template default (an explicit `numberOfRecords: 50` included). Plan section 1.3 row N2's summary, which omits N11 and N14-N16, is the plan's to correct | D-3, D-4 |
| R4-2 | D-4 said the shipped report processes "describe as `fileProcessing`", which is the block and wire spelling, while D10 has build and describe emit the lowercase token | Accepted. D-4 now reads `buildType: fileprocessing` (D10, "Visible change per cut"). Test-plan section 11 row RP-7 carries the same wording and is the test plan's to correct | D-4 |
| R4-3 | D-3's guidance bullet paraphrased the endpoint sentence that TC-88 pins ("... When a following element needs the files, it maps ...") | Accepted. The bullet quotes the pinned sentence verbatim, as D25, the D-3 change table, plan OA.12 and test-plan TC-88 do | D-3 |
| R4-4 | Line ranges differ across documents; normalisation to `ModifyBusinessProcessToolE2ETests.cs:2401-2428` was proposed | Not applied. Re-read at clio `03ef3944f`: the test runs from `[Test]` at line 2178 to its closing brace at 2204, and line 2205 is blank, so D1's `2178-2204` is exact; the `2178-2205` in traps, reuse, open-questions, the ENG-95984 File process parameter type plan (twice) and its test plan is the off-by-one form. This document's other three citations are exact as written: `ProcessParameterServiceTests.cs:1673-1680` (D1; `[Test]` is at 1670, the `[Description]` at 1671), `BundledProcessBuilderPackageTests.cs:1557-1593` (D25) and `ProcessMappingService.cs:594-604` (D4, method signature to closing brace). (Re-read 2026-10-13: `[Test]` at 1672, `[Description]` at 1673.) | D1, D4, D25 |

### Round 5: drift re-check, 2026-10-13

The plan was re-checked on 2026-10-13 against crt-process-builder `main` `a6bfcbf6` (CrtProcessBuilder 1.6.6.96),
clio `master` `19e6f9c53` and clio-knowledge `master` `0b56d4a` (libraryVersion 1.15.142). Anchors were re-pinned
first; this round changed meaning only, in this document. Runtime conclusions rest on source reading (hypotheses);
the tool-contract sizes were measured. Behaviour changes absorbed:
- ENG-102112 merged (package #90, 1.6.6.88): OA.5 rebases onto the generic Pre-configured page refusal and copies its
  shape after `UserTaskElementHandler.cs:87`; no merge conflict (D13).
- ENG-102537 merged (package #91, 1.6.6.91): one notice-only configuration ledger, reconciled after the gate, takes
  D16's notice; the refusals need their own pre-gate check (D16, D21).
- ENG-102537 also refuses Auto-generated page and User dialog outright, so a server refusal of a listed user task is
  no longer unique to the file element; element-catalog says FOUR (D13, D25).
- ENG-102111 merged (clio #1786): floors 1.6.6.92, the floor-history parenthetical (`:87-92`) ends "... to 1.6.6.40,
  to 1.6.6.77 and to 1.6.6.92", and ENG-95244 must also allow-list the tracing members (D26).
- ENG-100041 merged (package #95, 1.6.6.96): D12's refusals take dependents from the request's dependency snapshot,
  with the same-batch case tested (D12, D21).
- Bundle and next number: clio bundles 1.6.6.96; the next free package number is 1.6.6.97 or higher, and the next
  knowledge number 1.15.143 or higher (D25, D26).
- ENG-92113 delivery stage came (package #83) and went (package #92): the manual rebundle is again the only route
  (D26, D27).
- Description budget measured full (create 4 B, modify 10 B left) and a 19,000 B wire guard with call-shape drift
  tests added (ENG-102797): the swap is back, owner question Q20 opened (D25).
- ENG-102116: every `install-process-builder` needs the user's approval and restarts the site; read-back warnings
  append `ProcessBuilderInstallRemedy` (D26, D25 guidance rule).
- ENG-103023: describe defaults to a summary above 18,000 B, so graph assertions pass `mode=full` or `element=<name>`;
  new command tests stub the read-back (D25, D28).
- Knowledge: articles split (#239, #271, #280) but none the plan edits renamed; the room figures and the 345-character
  fallback are replaced; `process-files` targets about 21,000 characters (D25).
- Knowledge: the `GoLiveFloor` registration was an error in this plan (a banner-less article fails
  `GuidanceMigrationTests`), and six newer knowledge guards join the test list (D25, D28).
- ClioRing: Ring now requests the catalog with `detail=index-entries`; no Ring-consumed contract changed (D26).
- Test fixtures: one task manager per test, the `TestProcessSchema` `SchemaUId` NullReferenceException, and the
  `SetElementOperation` constructor churn (D28).
- Unchanged and re-confirmed (hypothesis, source): the generic route still builds the same unconfigured file element
  (T-46 / M14), T-68 is still open, describe of a FileCollection and M19 / M20 still hold.
- Q20 agreed 2026-10-14: A (trim existing text per tool, reviewer-approved; no ceiling raise, type lists stay in the
  descriptions) (D25).

### Round 6: sync with the PK-PT implementation, 2026-10-14

PK-PT (draft PR https://creatio.ghe.com/engineering/crt-process-builder/pull/96, commit `b6dcd2c1`, 3424 tests, not
stamped) settled or contradicted the following; this round changed this document accordingly (source, read in the
committed code and its tests):
- T-68, still open in Round 5, is root-caused and fixed in code; its stand proof is owed (D5; traps T-68).
- D5: P1 refuses a source chain deeper than the target's (new F-M5) and binds only the matching inner levels of a
  deeper target; F-M1 has a second remedy wording; P2 resets an unpairable item bound outside the new source; P2-MI
  also fires when P1 binds the multi-instance input; P3 resets EVERY bound ancestor; plan-then-commit, one rule
  source (`CollectionBindingRules`), `BindCollectionItems`, `IProcessDesignNotices` in the constructor.
- D8: the decode names a source only when replaying it changes no other level.
- D9: the re-mirror path binds items too and keeps a caller's formula.
- D25: two guidance lines for KB-PT (order of item and outer mappings; R-M1 on a single-instance sub-process input).
- D28: the parameter sync on `SchemaUId` runs in the harness.
- Appendix A: F-M5 added; the F-T and F-M texts quoted as written.
- New open item, not decided: a single-instance sub-process's FileCollection-shaped `In` parameter is not a
  file-consuming target (D5); measurement M27 (open-questions B.5).

### Round 7: gate-3 review fixes, 2026-10-14

The comprehensive gate-3 review of the two docs PRs found the following; this round changed this document
accordingly (source, read in PK-PT `b6dcd2c1` and on clio master `79fab4786`):
- D5: "structured sources only" excludes a mapping with `sourceColumn`, because a record column is not a
  parameter-to-parameter binding (`IsStructuredSource`, `PB/Mappings/ProcessMappingService.cs:671-672` in PK-PT).
- D5 and Appendix A: F-M5 refuses any source chain deeper than the target's, not only one level deeper.
- Appendix A: the opening `Mapping for target '<target>'` of F-M2, F-M3 and F-M5 names the element.
- D25: the description budget re-read on clio master `79fab4786` (ENG-103081: create 11 B, modify 2 B left); the next
  free knowledge number is 1.15.144 or higher (master 1.15.143).
- Outside this document: AC-5 of the ENG-95984 File process parameter type plan (section 1.4) was reworded to match
  D5 as built. An outer mapping onto a file-consuming collection is paired (P2), and a file item P2 cannot pair gets a
  notice, not a refusal; the shape refusals are R-M2 (a source with no File item) and R-M1 (an item into a single
  File).
