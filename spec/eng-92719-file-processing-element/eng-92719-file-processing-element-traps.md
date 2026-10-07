# Traps: ENG-95984 File process parameter type and ENG-92719 File processing element

**Summary.** This is the catalogue of hazards that the implementation of ENG-95984 File process parameter type and
ENG-92719 File processing element (sub-tasks ENG-96505 Element readiness and object attachments mode and ENG-96506
Generated report + process parameter modes) has to neutralise, and that the new `process-files` guide has to teach.
There are 68 entries. 58 of them are **silent** (T-8 among them, which M3 refuted on 2026-10-02): nothing throws, nothing is logged and the save answers success at the
moment the mistake is made, and the damage shows up later (at run time, in the designer, or on the next round trip),
or never. Most of the dangerous ones sit in four places: the platform type NAME `File` (it is the BLOB type), the
two-level binding of file collections, the attachment storage that the designer picks per entity, and empty or
wrongly rooted filters. Each entry gives what happens, whether it is silent, the basis (a source trace is a
hypothesis; a measurement carries its date), the evidence as `path:line`, and the builder rule, refusal, notice or
test that neutralises it. The banner below lists the traps that are only source-traced and need a stand run before
code depends on them.

Sibling documents: [README](README.md) ·
[platform-reference](eng-92719-file-processing-element-platform-reference.md) ·
[serialization-capture](eng-92719-file-processing-element-serialization-capture.md) ·
[use-cases](eng-92719-file-processing-element-use-cases.md) · [reuse](eng-92719-file-processing-element-reuse.md) ·
[decisions](eng-92719-file-processing-element-decisions.md) · [plan](eng-92719-file-processing-element-plan.md) ·
[test-plan](eng-92719-file-processing-element-test-plan.md) · [pr-split](eng-92719-file-processing-element-pr-split.md) ·
[open-questions](eng-92719-file-processing-element-open-questions.md) ·
[ENG-95984 File process parameter type plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-plan.md) ·
[ENG-95984 File process parameter type test-plan](../eng-95984-file-parameter-type/eng-95984-file-parameter-type-test-plan.md).
D-numbers (D1-D29), measurement ids (M1-M26) and refusal/notice ids (F-T*, F-M*, F-E*, F-F*, F-R*, F-P*) are the ones
defined in [decisions](eng-92719-file-processing-element-decisions.md) (measurements: D29; refusals: Appendix A).
M26 is the T-19 probe described under the banner and listed in D29. The rule ids that decisions uses without a
number, or that only the research carried (FB-n, V-n), are listed under [How to read this](#how-to-read-this).

Written 2026-10-01, read-only: nothing was built, run, committed or written to a repository, to Jira or to the stand.

---

## Banner: traps that rest on a source trace only and need a stand run

A source trace is a hypothesis. The rows below are runtime-critical and have no measurement yet. Every run marked
"write" needs the user's go-ahead, runs one at a time on the .NET Framework stand `Creatio`
(core 10.1.37), and is cleaned up through `execute-dataservice-batch` (the stand
rejects HTTP DELETE).

| Trap | Claim that is only traced | Run | Before code? | If the run refutes the claim |
|---|---|---|---|---|
| [T-11](#t-11) | `Files.File` bound alone to a single File parameter copies ONE file; an unset one gives an NRE | M1, M2 (write) | **yes** (M1) | the single-file path of D18 is withdrawn and refused; the guide says "wrap one file in a FileCollection" |
| [T-8](#t-8) | the shipped ENG-96230 Collection process parameter type mirror binds the outer level only, so every mirrored row reads null | M3 (write), **done 2026-10-02: refuted** | **yes** | items are still bound, as parity (D9); this applies, so no MH Sub-task (X4) |
| [T-16](#t-16) | a single File mapped from a collection item outside a row context misbehaves | M6 (write), **done 2026-10-02: confirmed** (`F` reads null) | **yes** | the R-M1 refusal is dropped (did not happen: it stays) |
| [T-22](#t-22) | the server-side storage resolver predicts the designer's object list | M10 (read-only, designer) | **yes** | the D15 refusal set R3-R5 is adjusted to the designer's list |
| [T-23](#t-23) | an Object element finds a Freedom UI upload stored in SysFile | M8 (write) | **yes** (SF) | SysFile sources stay refused |
| [T-24](#t-24) | copies written into SysFile carry `RecordId`, `RecordSchemaName`, `Type = File` and show in the Freedom UI list | M23 (write) | **yes** (SF) | SysFile targets stay refused |
| [T-28](#t-28) | opening and closing a legacy Object element in the designer writes `SourceDataEntitySchemaUId = <X>File`, which throws at run time | M11 (a)(b) read-only, (c) write | **yes** ((a), (b)) | the describe issue is downgraded to "cosmetic", no designer bug is filed |
| [T-31](#t-31) | the exact filter JSON the designer writes for a SysFile `RecordId` scope | M7 (read-only, designer) | **yes** (SF) | the default lookup InFilter is kept |
| [T-27](#t-27) | an empty `ConnectedObjectId` fails a legacy target (FK) and orphans a SysFile target | M17 (its read-only foreign-key pre-check M17-Q7 first, then write) | M17-Q7 **yes** | the notice wording changes |
| [T-36](#t-36) | the FastReport process path works | M15 (read-only) | **yes** | FastReport stays refused (it is refused anyway unless present and working) |
| [T-50](#t-50) | describe of a compiled shipped process returns an unconverged snapshot | M13 (read-only) | **yes** | describe fixtures drop the Lookup-typed form |
| [T-51](#t-51), [T-52](#t-52) | a server-built element gets `ConsiderTimeInFilter = "false"`, template nested UIds, no nested BK15 rows | M14 (write) | **yes** | the D20 pins are rewritten to what is measured |
| [T-20](#t-20) | Report "use in process" files disappear when the owning process (or sub-process) completes | M16 (write) | no | the notice wording changes |
| [T-34](#t-34) | a SysFile sort on a record-object column throws; an array sent into the `sort` object member does X at the WCF binder | M9, M24 (write) | no | refusal severity and the guide sentence change |
| [T-40](#t-40) | a constant file-name suffix written under a non-default culture is dropped at run time | M18 (write) | no | the culture note in the guide changes |
| [T-29](#t-29) | the server read of the SysFile designer flag returns true on the stand | M25 (write) | no (gates a notice only) | the notice does not ship |
| [T-48](#t-48) | an unknown block next to a known field in `setElement` is silently dropped on 1.6.6.54 | M20 (write) | no | the floor rationale in D26 is rewritten |
| [T-67](#t-67) | a collection parameter used as a filter value errors at run time | M19 (write) | no | the urgency of the filter-value Sub-task changes |
| [T-21](#t-21) | a FileCollection bound at both levels feeds the Process variant, and its `ObjectFiles` holds the COPIES | M4 (write) | no (end-to-end verification) | the output list and the guide note change |
| [T-19](#t-19) | after "Save to object attachments" the Object variant's `ObjectFiles` holds the SOURCE locators, so a consumer iterating it touches the originals; the copies are only in `CreatedObjectFileIds` | **M26** (write; D29) | no code; **gates** the OA.12 guide sentence and the CL-OA knowledge record `after-saving-object-files-points-at-the-source-files.md` | neither the sentence nor the record is written; the guide states what M26 observed |
| [T-28](#t-28), [T-51](#t-51), [T-53](#t-53) | the designer opens builder-made elements and a no-op panel save changes nothing | M22 (write: a no-op designer save, go-ahead) | no (parity verification) | the D20 serialization rules are revisited |

**M26 (T-19; D29).** Method: a designer-built probe (it needs a File parameter, so a human builds it until
ENG-95984 File process parameter type is merged). OF1 = Object attachments of a disposable Contact `M26 Source` with
2 uploaded attachments, "Save to object attachments" onto a disposable Account `M26 Target`; then a multi-instance
sub-process with `InputRecordCollection.Doc <- OF1.ObjectFiles.File` (the parent bound too, as in ST-02) whose callee
has a script task that writes the `EntityFileLocator`'s `EntitySchemaName` and `RecordId` of `Doc` into the title of
an Activity it adds. Pass condition: the 2 titles carry `ContactFile` and the Ids of the source ContactFile rows
(read-only reads of ContactFile, AccountFile and Activity after the run), and the 2 new AccountFile Ids are not among
them. Cleanup of the activities, copies and fixtures through `execute-dataservice-batch`. The same callee can
instead be appended to ST-01 (Account -> Contact) in [test-plan](eng-92719-file-processing-element-test-plan.md); the
comparison is then AccountFile against ContactFile.

Source-traced but **no run planned**, because the builder refuses or notices the shape whatever the result:
[T-10](#t-10) outer-only binding (M5 skipped), [T-12](#t-12), [T-15](#t-15), [T-18](#t-18), [T-25](#t-25),
[T-32](#t-32), [T-33](#t-33), [T-36](#t-36) (DevExpress), [T-38](#t-38), [T-39](#t-39) (the lookup name column, optional
M-G6-4), [T-43](#t-43).

---

## How to read this

- **Silent** = no exception, no notice and no log line when the mistake is made. **Loud** = it throws or is refused
  at that moment. "Loud at run time" means it saves green and fails later.
- **Basis**: **source** = read in code or metadata (a runtime claim with this label is a hypothesis); **measured** =
  observed on the stand, in the designer, or counted over the corpus, with the date; **inference** = derived from both.
- Line numbers were taken from the checkouts below on 2026-10-01; the load-bearing ones were re-read for this document.
- Rule ids used below but not numbered in [decisions](eng-92719-file-processing-element-decisions.md):

  | Id | Meaning (owning decision) |
  |---|---|
  | FB-1 ... FB-15 | the binding rules of the Process variant's `files` input (D18): FB-1 a collection source binds both levels; FB-2 a single file binds `Files.File` only (pending M1); FB-3 outer-only refused; FB-4 no binding refused; FB-5 the nested source is FileLocator; FB-6 the nested source is an item of the SAME collection; FB-7 `TargetEntitySchemaUId` required; FB-8 the link column required and the designer's; FB-9 an empty `ConnectedObjectId` refused as a constant, noticed when mapped; FB-10 the `TargetDataEntitySchemaUId` invariant per storage; FB-11 `RecordsToRead` at least 1; FB-12 no mapping FROM `Files`; FB-13 `ResultActionType` 0 only; FB-14 describe reports a nested-only binding; FB-15 the guide says a single-file input must be set before the element runs |
  | V1, V2, V3, V9 | the filter-target validations of D16: V1 `filter.object` must be the runtime root, V2 no filter on an unconfigured element, V3 the record scope's type, V9 a sort column must be a direct column of the runtime root |
  | P1, P2, P2-MI, R-M1, R-M2 | the two-level collection binder rules of D5 (parent auto-bind, item pairing, the multi-instance notice, and the two refusals F-M2 / F-M3) |
  | R1-R9, W6, W7 | the attachment-storage refusals and warnings of D15 (all reported as F-E7) |
  | X1 | the correction in decisions section 0: an ordinary user task is initialised with all its parameters; only a sub-process start and the multi-instance count filter by provenance |
  | M17-Q7 | the read-only foreign-key pre-check that opens M17 (as in [open-questions](eng-92719-file-processing-element-open-questions.md)) |
  | G1 E5 | the attribute-inheritance comparison (Notes, Tag, CreatedById of the copies) piggybacked on M1 |
  | M-G6-4 | an optional run with a lookup column as the file-name column; not planned, F-R2 refuses the shape |

| Alias | Path |
|---|---|
| CORE | core `TSBpm/Src/Lib`, tag 10.1.37 (the stand's version) |
| GEN | `CORE/Terrasoft.WebApp.Loader/Terrasoft.WebApp/Terrasoft.Configuration/Autogenerated/Src` (compiled configuration code, 10.1.37) |
| PD | `PackageStore/CrtProcessDesigner/branches/7.8.0/Schemas` (byte-identical to what the stand serves) |
| PS | `PackageStore` (the shipped process corpus) |
| PB | crt-process-builder `packages/CrtProcessBuilder/Files/src/cs` (main `3f4cce50`, package 1.6.6.54) |
| PBA / PBT | crt-process-builder `.../Files/src/CrtProcessBuilderApp.cs` / `tests/UnitTests/CrtProcessBuilder.Tests` |
| clio | the clio repository (master `03ef3944f`) |
| KB | the clio-knowledge repository (master `d0b5a2b`, libraryVersion 1.15.90) |
| NUI | `CORE/Terrasoft.Nui/Resources/Terrasoft` (the classic designer's client code) |
| UT | platform unit tests `UnitTests` (a reference for platform semantics only) |
| UO-n | designer measurements of 2026-10-01 on the stand, in an unsaved process (see [serialization-capture](eng-92719-file-processing-element-serialization-capture.md)) |

| Alias | Jira issue / PR group (titles as in [pr-split](eng-92719-file-processing-element-pr-split.md)) |
|---|---|
| **PT** | ENG-95984 File process parameter type |
| **FE** | ENG-92719 File processing element |
| **OA** | ENG-96505 Element readiness and object attachments mode |
| **RP** | ENG-96506 Generated report + process parameter modes |
| **SF** | NEW Sub-task of FE: "SysFile attachment storage in the Process file element" |
| **MH** | not created: the conditional Sub-task of PT "typeFromElement collection mirror leaves its items unbound" waited on M3, and M3 refuted H-1 on 2026-10-02 |
| **SK** | ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17 graph validator into the write path (ENG-88414) (the key in that title is ENG-88414 AI-driven application development; in flight; makes descriptor keys strict) |

---

## Index

| # | Trap | Silent? | Basis | Neutralised in |
|---|---|---|---|---|
| T-1 | The platform type name `File` is the BLOB type | **yes** | source | PT (D1) |
| T-2 | Binary cannot hold a process value; its refusal has one automated guard | **yes** (writer path) | source + measured | PT (D1) |
| T-3 | A file collection read back as `CompositeObjectList` rebuilds shapeless | **yes** | source + measured | PT (D2) |
| T-4 | `referenceSchema` or a constant on a File parameter is accepted | **yes** | source | PT (D7) |
| T-5 | Shape change is a no-op; the delete guard does not see items | **yes** | source | PT (D7) |
| T-6 | An Out FileCollection cannot be filled by a caller | no | source + measured | PT (D3) |
| T-7 | The dotted mirror creates an unbound FileLocator parameter | **yes** | source | PT (D6) |
| T-8 | The ENG-96230 Collection process parameter type mirror binds the outer level only (H-1) | **yes** | source; refuted by M3 (2026-10-02) | PT, as parity (D9, X4) |
| T-9 | Process-parameter paths are flat-only | no | source | PT (D4) |
| T-10 | Outer-only binding of `Files` | at save **yes**, run loud | source | PT, RP (D5, D18) |
| T-11 | Nested-only binding of `Files` (disputed) | depends on M1 | source, disputed | M1, then RP (D18) |
| T-12 | Empty `Files`: zero files, Completed | **yes** | source | RP (D18) |
| T-13 | Multi-instance with only per-item mappings runs once | **yes** | source + measured | PT (D5, D25) |
| T-14 | Multi-instance over non-file rows: the callee File is empty | **yes** | source + corpus | PT (D5) |
| T-15 | Stale rows when something maps FROM the Process variant's `Files` | **yes** | source | RP (D18) |
| T-16 | A single File taken from a collection item | **yes** | measured (M6) | PT (D5) |
| T-17 | `ResultActionType` unset runs as "save"; the designer default is "use in process" | **yes** | source | OA, RP (D14) |
| T-18 | The Process variant with "use in process" throws | at save **yes**, run loud | source | RP (D14) |
| T-19 | After "save", `ObjectFiles` points at the SOURCE files | **yes** | source | M26, then OA (D12, D14), guide |
| T-20 | Report "use in process" files are temporary | **yes** | source | RP (D14, D17) |
| T-21 | The Process variant's output item is `ObjectFile`, its input item is `File` | **yes** | source | PT (D5), RP (D18) |
| T-22 | Attachment storage is chosen per entity | **yes** | measured + source | OA, SF (D15) |
| T-23 | The SysFile read path is unmeasured | **yes** | source + community | SF (D15) |
| T-24 | The SysFile write path is unmeasured | **yes** | source | SF (D15) |
| T-25 | Missing or wrong link column | **yes** | source | OA (D15) |
| T-26 | Orphan file objects; the designer clears an explicit link column | **yes** | source + corpus | OA (D15) |
| T-27 | Empty `ConnectedObjectId` | legacy loud, SysFile **yes** | source | OA (D15) |
| T-28 | H-G3-1: a designer open-and-close breaks a legacy Object element | **yes** until run | source | M11, OA (D12, D15, D19) |
| T-29 | The SysFile designer flag: OFF breaks re-save; the wrong read API reads OFF | **yes** | measured + source | OA, SF (D15) |
| T-30 | Copies inherit source attributes and are always Type = File | **yes** | source | guide |
| T-31 | The filter root is ignored; two `recordId` vocabularies | **yes** | source | OA, RP (D16) |
| T-32 | Empty filters: 50 arbitrary files, or one report per record | **yes** | source + corpus | OA, RP (D16) |
| T-33 | `numberOfRecords`: 50, not "all"; 0 reads nothing | **yes** | source | OA (D16) |
| T-34 | SysFile sort columns come from the record object | mixed | source + measured | OA, SF (D16) |
| T-35 | A SysFile retarget keeps a foreign record scope | **yes** | source | OA (D12) |
| T-36 | DevExpress printables are offered and throw; FastReport is unverified | at save **yes**, run loud | source + measured | RP (D17) |
| T-37 | No PDF converter: Word stays `.docx` silently | **yes** | source + measured | RP (D17) |
| T-38 | Word with `IsSeparateReports = false`: identical file names | **yes** | source | RP (D17) |
| T-39 | File-name column rules | mixed | source | RP (D17) |
| T-40 | The file-name suffix constant is localizable | **yes** | source | RP (D11, D17) |
| T-41 | "Save" attaches every report to ONE record | **yes** | source | RP (D17) |
| T-42 | Printable identity: duplicate captions, `templateId`, none for Account/Contact | no | source + measured | RP (D17) |
| T-43 | Partial output on a mid-run failure; an empty Word template | loud, partial | source | guide |
| T-44 | `EnableReportFileProcessingUserTask` is designer-only | **yes** | measured + source | RP (D17) |
| T-45 | Changing the source replaces the element | **yes** if mutated in place | source + measured | OA (D12) |
| T-46 | The generic `userTask` route builds an unconfigured element | at save **yes**, run loud | source | OA, RP (D13) |
| T-47 | Raw `addMapping` onto configuration parameters | **yes** | source | OA, RP (D13) |
| T-48 | An older package drops the `fileProcessing` block | **yes** | source + measured precedent | floor raise (D26) |
| T-49 | Missing ManagerMap arm; handler order | no | source + measured | PT clio, OA (D10) |
| T-50 | Old parameter snapshots | **yes** | source + corpus | OA (D19, D20) |
| T-51 | A server-built `ConsiderTimeInFilter` is "false" | **yes** | source + measured | OA (D20) |
| T-52 | Template nested-item UIds are shared | **yes** | source | OA (D20) |
| T-53 | An in-place value edit keeps the template's provenance | **yes** | source | OA, RP (D20) |
| T-54 | Schema UIds through the mapping path's Lookup validator | no | source + measured | OA (D11) |
| T-55 | Describe hides nested-only bindings | **yes** | source | PT (D8), RP (D19) |
| T-56 | Describe today: generic user task, raw GUIDs | **yes** | measured | OA, RP (D19) |
| T-57 | Describe decodes only three-segment column paths | **yes** | source | PT (D8) |
| T-58 | clio's filter DTOs have no overflow bag | **yes** | source | OA (D16, D19) |
| T-59 | Describe-only members fed back on write | **yes** today | source | OA (D11) |
| T-60 | The session's clio MCP client is stale | **yes** | measured | verification protocol |
| T-61 | Two research misreadings (UO-1 list, "Add files") | **yes** | measured + source | verification protocol |
| T-62 | Stale guidance, knowledge-first merges, exhausted budgets | **yes** | measured | merge order (D25, D27) |
| T-63 | Process-designer e2e never runs in TeamCity; old stands give Ignored | **yes** | source | PBT pins, Ignored = 0 rule |
| T-64 | Archive from the build output, number collisions, stand discipline | **yes** | measured | cut protocol (pr-split) |
| T-65 | Unit tests cannot prove runtime behaviour | **yes** | source | stand runs (D28, D29) |
| T-66 | Academy wording differs from the product | **yes** | source | guide (D25) |
| T-67 | Consumers the builder cannot wire; a collection as a filter value | **yes** | source | guide (D22), Sub-task |
| T-68 | A nested-item mapping on the create path throws a bare NRE | no (loud, uninformative) | measured | PT (two-level binder on create) |

---

## A. The file parameter type (PT)

### T-1
**The platform type name `File` is the BLOB type, not a file reference.**
- **What happens.** `DataValueTypeManager.GetInstanceByName("File")` returns `FileDataValueType : BinaryDataValueType`
  `BA40CFC5-F554-4c26-8F57-1BB29CF43C4E` ("File (BLOB)"). The file-reference type, the one every designer page and
  every shipped file parameter uses, is `FileLocator` `A33C9252-D401-453E-949D-169157067ED9` (CLR `IFileLocator`, client
  `FILE_LOCATOR = 41`, caption "File"). A builder that passes the user's word `File` through creates a parameter no
  designer page offers and the flow engine cannot store. The ENG-95984 File process parameter type AC wording
  "Binary / File" invites exactly this.
- **Silent or loud.** Silent: the name resolves and the save succeeds.
- **Basis / evidence.** source. `CORE/Terrasoft.Core/DataValueTypeManager.cs:341-349` (BLOB "File"), `:501-508`
  (FileLocator); `CORE/Terrasoft.Core/DataValueType.cs:297, 2160, 3003-3027`; the package's only entry point
  `PB/Parameters/ProcessParameterService.cs:1012-1019` (`GetInstanceByName(NormalizeParameterTypeName(type))`).
- **Neutraliser.** D1: the aliases `file`, `filelocator`, `file locator` return the canonical name `FileLocator`
  before the manager is called, so `File` can never reach BA40CFC5. PBT `[TestCase("File")]`, `"file"`, `"FileLocator"`,
  `"file locator"` assert A33C9252 and never BA40CFC5. AC corrected (D24). A clio knowledge record ("the platform type
  name File resolves to the BLOB type") ships with the PT clio PR.

### T-2
**Binary cannot hold a process value, and its refusal has one automated guard.**
- **What happens.** Binary `B7342B7A` is a `Stream` type. The flow-engine store has no Stream store: one path throws
  `NotSupportedException`, the writer path drops the value. Today the package refuses Binary by its allow-list; the
  refusal text is asserted by a clio e2e that TeamCity never runs (T-63).
- **Silent or loud.** Loud on one path, silent on the writer path.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/FlowEngineStateService.cs:139-148` (the typed stores: no
  Stream), `:409-429`; `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataWriter.cs:255-286`. measured (corpus):
  0 process or user-task parameters of type Binary or File (BLOB); 50 FileLocator `L1` occurrences in 21 files.
- **Neutraliser.** D1, refusal F-T1: "Parameter type 'Binary' is not supported: a process parameter carries a file by
  reference, not its bytes. Use type File (one file) or FileCollection (several files)." It keeps the two substrings
  "not supported" and "Binary" that `clio/clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs:2178-2205` asserts. The
  PBT pins `PBT/ProcessParameterServiceTests.cs:144-150, 1671-1678` are the only guard CI actually runs, so they stay
  and gain the "names File" assertion. Guidance never says "Binary".

### T-3
**A file collection read back as `CompositeObjectList` rebuilds shapeless, and saves green.**
- **What happens.** A file collection is the generic `CompositeObjectList` `651ec16f` with one nested FileLocator item;
  there is no file-collection type. The write contract has no `itemProperties`, the guide tells agents to re-declare a
  described collection through `typeFromElement` "never by re-typing the shape", and the mirror refuses file outputs
  because their items carry no Tag. An agent replaying describe output as `type: CompositeObjectList` gets a collection
  with no items: nothing can bind to it. A second trap: a designer-made FileCollection can name its item anything
  (`FileParameterProcess.FileCollection` item `FileCollectionParameter`), so a rebuild that names the item `File`
  breaks a replayed mapping that names the old item.
- **Silent or loud.** Silent for the shapeless rebuild; the replayed item name is loud ("not found").
- **Basis / evidence.** source: `PB/Parameters/ProcessParameterService.cs:31-33` (alias), `:456-475` (mirror refusal);
  `PB/Contracts/ProcessDescriptorContracts.cs:1965-2040`; `KB/guidance/mcp/guides/processes/parameters.md:35-37`.
  measured (corpus): 0 of 133 process-level collections carry a Tag; the two shipped file collections are
  `PS/ProcessTests/.../FileParameterProcess` and `PS/ProcessLibrary/.../MarkProcessesToCancel`.
- **Neutraliser.** D2: `type: FileCollection` adds exactly one item `File` (FileLocator, Variable, no Tag,
  `ContainerUId = schema.UId`); describe reports `type: "FileCollection"` for a process-level collection with no Tag
  and exactly one FileLocator item, with `itemProperties` kept for information. F-T6 (mirror of a file output points to
  FileCollection) and F-T7 (a replayed item name that the rebuild changed, items listed). `parameters.md:35-37` and
  `CollectionParameterGuidanceTests` amended in the PT knowledge PR. PBT round trip build -> describe -> build.

### T-4
**`referenceSchema` or a constant on a File parameter is accepted and changes its meaning.**
- **What happens.** `File + referenceSchema` falls into the lookup branch and becomes a Lookup. A `value` on a File, or
  an `addMapping value` onto any FileLocator or `CompositeObjectList` target, is stored as a text ConstValue; the
  platform's pre-save rule validates Script and Mapping sources only, so nothing catches it.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Parameters/ProcessParameterService.cs:93-100` (lookup branch);
  `PB/Parameters/ProcessParameterValueValidator.cs:143-147, 190-215` ("any other type is left to the runtime
  initializer"); `PB/Mappings/ProcessMappingService.cs:302-324`; `CORE/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:104-106`.
- **Neutraliser.** D7: F-T2 (`referenceSchema` with File / FileCollection refused) and F-T3 ("a File has no constant
  form; map it from an element's file output or another File parameter"), the latter in `ValidateConstantValue` so
  every constant route is covered. PBT per route.

### T-5
**Changing a collection's declared shape is a no-op, and the delete guard does not see items.**
- **What happens.** `setParameter type: FileCollection` on a collection of another shape keeps the same stored type UId,
  so nothing changes and the call succeeds. `removeParameter` of a collection checks usages of the ROOT UId only, so a
  collection whose ITEM another element maps from is deleted and leaves a dangling item reference.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Parameters/ProcessParameterService.cs:232-237` (same type = no-op), `:576-647`
  (`FindParameterUsages`, root UId only).
- **Neutraliser.** D7: F-T4 ("FileCollection would change this collection's shape; remove it and add it again"); the
  delete guard walks the root and every item UId recursively. PBT for both.

### T-6
**An Out FileCollection cannot be filled by a caller.**
- **What happens.** A FileCollection declared `direction: Out` cannot be filled by a caller: a sub-process caller
  can feed only an In or Variable callee parameter. The default is Variable (D3, agreed 2026-10-07), so only an
  explicit Out hits this; a generic `Collection` still defaults to Out.
- **Silent or loud.** Loud: `EnsureSubProcessTargetCanHoldAValue` refuses the caller's `addMapping`.
- **Basis / evidence.** source: `PB/Parameters/ProcessParameterService.cs:103-107` (default; the comment's "designer
  writes Out" is wrong), `PB/Mappings/ProcessMappingService.cs:79-110`; `PD/ProcessSchemaPropertiesPage/ProcessSchemaPropertiesPage.js:1204-1236`
  (plain Add = Variable, no Tag). measured (corpus, recount 2026-10-01): shipped process-level collections 85 Out,
  30 Variable, 12 In, 6 Internal of 133 (17 more in user-task schemas are element parameters).
- **Neutraliser.** D3: the Variable default; the existing refusal names the fix for an explicit Out; the misleading
  package comment is corrected.

### T-7
**The dotted mirror silently creates an unbound FileLocator parameter.**
- **What happens.** `typeFromElement: "OF1", typeFromElementParameter: "ObjectFiles.File"` resolves the nested item
  through the dotted locator, skips the collection branch (an item is not a collection) and copies `DataValueTypeUId`
  verbatim, with no allow-list. The result is a FileLocator process parameter that is bound to nothing, although the
  type allow-list refuses "File".
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Parameters/ProcessParameterService.cs:69-83, 402-410`;
  `PB/ProcessSchemaElementLocator.cs:121-123, 166-241`.
- **Neutraliser.** D6: `ResolveTypeSourceParameter` resolves flat only; when the dotted walk would succeed, F-T5: "'ObjectFiles.File'
  is an item of collection 'ObjectFiles' on 'OF1'. A mirror copies a whole parameter: mirror 'ObjectFiles' for its
  shape, or declare type File / FileCollection." One PBT test (refused, schema unchanged). The general type allow-list
  for mirrors is a separate Sub-task of PT.

### T-8
**The shipped ENG-96230 Collection process parameter type mirror binds the outer level only (hypothesis H-1).**
- **What happens.** `BindMirroredCollection` maps root to root. The runtime rebuilds the rows of a target that has item
  properties by the TARGET's item names and fills an unbound item with its default. If that holds, every mirrored Read
  data row reads null. No shipped process has that shape (0 of 61 bound top-level collections bind the outer level only).
- **Measured: refuted.** M3 on 2026-10-02 (CrtProcessBuilder 1.6.6.54): 3 iterations, all `M3 name set`. The outer
  Script mapping copies the whole collection value, items included, so the unbound item does not matter for reading.
- **Silent or loud.** It would have been silent; M3 found no null rows.
- **Basis / evidence.** source: `PB/Parameters/ProcessParameterService.cs:500-512`;
  `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:441-488`. measured (corpus): 0/61. measured
  (stand, 2026-10-02): M3, [open-questions](eng-92719-file-processing-element-open-questions.md) C.7.
- **Neutraliser.** M3 refuted H-1, so there is no MH Sub-task. Items are still bound, as parity (one commit in
  PK-PT, contingency X4 of [pr-split](eng-92719-file-processing-element-pr-split.md)), and the comment at
  `ProcessParameterService.cs:440-447` is narrowed (D9).

### T-9
**Process-parameter paths are flat-only.**
- **What happens.** `processParameter: "Docs.File"` or `targetProcessParameter: "Docs.File"` is not resolved, although
  shipped content binds process-level items both as source and as target.
- **Silent or loud.** Loud: "Process parameter 'Docs.File' was not found".
- **Basis / evidence.** source: `PB/Mappings/ProcessMappingService.cs:425-435` (callers `:221, :280`); measured (corpus):
  `PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:47, 1440`.
- **Neutraliser.** D4: `ProcessSchemaElementLocator.ResolveProcessParameterPath` reuses the element walk, with no
  `ContainerUId` backfill (a process-level item keeps `[Parameter:{item}]`). PBT: dotted source and target, no element
  segment in the meta path.

---

## B. File-collection bindings at run time

### T-10
**Outer-only binding of the Process variant's `Files` crashes per row.**
- **What happens.** `Files <- collection` with `Files.File` unbound: the reader builds each row from the TARGET item
  names, the unbound `File` gets `null`, `TryGetValue("File")` returns true with null, `GetFile(null)` throws a
  NullReferenceException in `FileFactory.CreateFileInstance`. No row reaches `CreatedObjectFileIds`. The designer cannot
  produce this shape; the builder can today, through two `addMapping` calls or one forgotten one. The only
  InvalidCastException path is a nested source that is not an `IFileLocator` (a Guid or Text item).
- **Silent or loud.** Silent at save (saved green today), loud at run time.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:454-458, 480-487`;
  `PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:68-82` (copy loop); `CORE/Terrasoft.File/FileFactory.cs:44-46`.
  measured (corpus): 11/11 shipped file-collection bindings bind both levels.
- **Neutraliser.** D5: P1 (an item mapped from an item of a collection also binds the parent) and P2 (an outer mapping
  onto a file-consuming target pairs its FileLocator item, with a notice naming what it bound); D18: `files` from a
  collection always binds both levels through `BindCollection`; F-P1 refuses outer-only (FB-3), R-M2 (F-M3) refuses a source
  collection with no FileLocator item. The type check already requires an exact FileLocator match
  (`PB/Mappings/ParameterTypeCompatibility.cs:240-242`).

### T-11
**Nested-only binding of `Files` (disputed until M1).**
- **What happens.** For a single-file source the designer writes the nested item only (`Files.File <- [#FileParam#]`,
  `Files` at Source None). The two research traces disagreed on whether an ordinary user task reads this at all. The
  current reading (gap G1 and decisions X1): an ordinary element is initialised with ALL its parameters and no
  provenance filter; with the outer source None and a mapped nested item, `TryGetListValue` adds exactly ONE row, so one
  file is copied. This is the source trace's favoured reading, and the claim stays disputed until M1. The restore path
  `InitializeFlowElementProperties(..., useOnlySchemaValues)` (`CORE/Terrasoft.Core/Process/ProcessComponentSet.cs:531-540`,
  reached from `TryCreateProcessElement`, `:1272`) keeps only None/ConstValue parameters that have a value, and the
  trace has not ruled it out. The provenance filter that would hide it applies only to a sub-process start and to the
  multi-instance iteration count. `HasCollectionWithOneElement` is not the reason the row exists; it only feeds the iteration count.
  If the parameter is unset at run time, the row is `{File: null}` and the element fails with an NRE.
- **Silent or loud.** If the reading is right: works; unset parameter loud (element Error). If wrong: a silent
  zero-copy is the likely failure.
- **Basis / evidence.** source, no shipped example: `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:444-453`;
  `CORE/Terrasoft.Core/Process/ProcessComponentSet.cs:502-540` (all parameters, no filter) against
  `CORE/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:718-753` (the filter, callers `ProcessComponentSet.cs:1361-1366, 1518-1521`);
  designer `PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:73-79`,
  `PD/MappingEditMixin/MappingEditMixin.js:1075-1096`.
- **Neutraliser.** D18 FB-2: a single File source binds `Files.File` only, **pending M1**; M2 confirms the unset case.
  If M1 fails, the single-file path is withdrawn and refused, and the guide says "wrap one file in a FileCollection".
  The guide says a single-file input must be set before the element runs (FB-15). The disagreement is tracked in
  [open-questions](eng-92719-file-processing-element-open-questions.md).

### T-12
**Empty `Files`: zero files and a Completed element.**
- **What happens.** No binding at either level, a source collection with zero rows, or a source that was never set:
  the element copies nothing, `ObjectFiles` and `CreatedObjectFileIds` are empty, the element completes. One exception:
  the target schema is resolved before the loop, so an empty or unknown `TargetEntitySchemaUId` throws even with zero rows.
- **Silent or loud.** Silent (the target exception is loud).
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:444-453, 459-467`;
  `PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:68-71`. The designer validator forbids an unbound
  `Files` (`PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:117-135`).
- **Neutraliser.** D18: F-P1 refuses `files` with no source (FB-4); `saveTo` is required on this variant (F-E6), so an
  empty `TargetEntitySchemaUId` cannot be built (FB-7). Guide: "an empty source copies nothing, silently".

### T-13
**A multi-instance sub-process with only per-item mappings runs once.**
- **What happens.** Mapping only `InputRecordCollection.F <- OF1.ObjectFiles.File` leaves `InputRecordCollection` at
  None; the iteration count then comes from `HasCollectionWithOneElement` and is 1, with every per-item value empty.
  Two shipped guide passages state this behaviour as measured fact and tell agents to send both mappings. P1 (D5)
  changes it: from the PT cut, a per-item value taken from an item of a collection also binds the collection.
- **Silent or loud.** Silent: the build succeeds and nothing warns.
- **Basis / evidence.** source (`CORE/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:466-490`, `FillHasCollectionWithOneElement`); the guides record a
  measurement on CrtProcessBuilder 1.6.6.22: `KB/guidance/mcp/guides/processes/sub-process.md:133-137`,
  `KB/guidance/mcp/guides/processes/sub-process-when.md:49-51`.
- **Neutraliser.** D5 P1, including `InputRecordCollection.<F>`; both guide passages rewritten in the PT knowledge PR
  with a version gate (the "send both" advice stays, it is right on older servers), and pinned. Stand proof on the PT
  cut: 3 iterations over 3 contacts, against 1 on the 1.6.6.54 baseline ([pr-split](eng-92719-file-processing-element-pr-split.md)).

### T-14
**A multi-instance sub-process over non-file rows leaves the callee's File empty.**
- **What happens.** A multi-instance element's `InputRecordCollection` items are clones of the callee's In/Variable
  parameters, so a callee with a File parameter gives the collection a FileLocator item. Iterating it over
  `ReadData.ResultCompositeObjectList` or `OF1.CreatedObjectFileIds` is legal and runs; the file item is null on every
  iteration. A shipped test process does exactly this (`FileCopyProcessPP.SubProcess1`, outer only).
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:336-352` (`FillCollectionParameters`);
  measured (corpus): `FileCopyProcessPP`.
- **Neutraliser.** D5 scoping: P2 and R-M2 apply only to file-consuming targets, so this mapping is ACCEPTED (refusing it
  would break legal shipped shapes) with notice P2-MI ("callee parameter `<F>` will be empty on every iteration unless
  you map `InputRecordCollection.<F>`"). PBT regressions: accepted with the notice and no item written; explicit
  mappings write exactly what they write today.

### T-15
**Stale rows when something maps FROM the Process variant's `Files`.**
- **What happens.** The reader prefers a STORED value at the element's own `Files` path. The writer stores `Files` only
  when another mapping references that path. If something maps from `Files` and the element runs twice in a loop, the
  second pass reuses the first pass's rows.
- **Silent or loud.** Silent; low likelihood.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:578-580`;
  `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataWriter.cs:185-191`.
- **Neutraliser.** D18 FB-12: any mapping FROM this element's `Files` input is refused (F-P1). The designer never offers
  the input as a source either.

### T-16
**A single File taken from an item of a collection, outside a row context.**
- **What happens.** `F <- OF1.ObjectFiles.File` with `F` a flat FileLocator process parameter: outside a collection
  context the nested value has no row, and the runtime gives `F` **null**: no error, no first row. CrtProcessBuilder
  1.6.6.54 accepts the mapping.
- **Silent or loud.** Silent: the process runs on with an empty `F`.
- **Basis / evidence.** measured 2026-10-02 (M6): `F` read empty (`M6 F=`) while OF1's `ObjectFiles` held two
  `EntityFileLocator`s in `SysProcessElementData`; [open-questions](eng-92719-file-processing-element-open-questions.md) C.7.
- **Neutraliser.** D5 R-M1 (F-M2): "a single File cannot take an item of `ObjectFiles`; use a FileCollection, or a
  multi-instance sub-process". M6 confirmed the refusal.

---

## C. "What to do with file?" and the outputs

### T-17
**`ResultActionType` unset runs as "Save to object attachments"; the designer default is "Use in process".**
- **What happens.** `SaveToFiles = 0`, `UseInProcess = 1`. The template has no value (`L8: {}`) and is flagged required
  (`L6: true`), but no server rule reads `IsRequired`, so an element without the parameter saves green and the runtime
  reads 0: it tries to save. The designer page, on the other hand, defaults Object and Report to 1. A builder that
  copies the designer default silently drops the save a caller asked for with `saveTo`; a builder that omits the
  parameter saves files nobody asked to save (or throws when no target is set).
- **Silent or loud.** Silent at save; at run time either a silent unwanted save or a loud missing-target error.
- **Basis / evidence.** source: `PD/ObjectFileProcessingUserTask/metadata.json:111-116` (`L8 {}`, `L6 true`);
  `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:98-112` (the switch);
  `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:11-12, 894-903` (designer
  default 1). measured: UO-3 (the designer writes ConstValue 1 by default).
- **Neutraliser.** D14: `action` is inferred on create from `saveTo` (present: save; absent: use in process),
  contradictions refused (F-E6), and `ResultActionType` is ALWAYS written as a ConstValue on create. On update it
  changes only when `action` is sent, or when `saveTo` is sent to an in-process element (D12 rule 4). Describe reports
  `actionStored: false` for an unset value ("runs as saveToAttachments").

### T-18
**The Process variant with "use in process" throws.**
- **What happens.** The Process variant supports only 0; 1 throws `NotSupportedException`. Its page hides the control
  and forces 0, so only a builder (or a raw `addMapping`) can produce it.
- **Silent or loud.** Silent at save, loud at run time.
- **Basis / evidence.** source: `PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:89-97`; page
  `PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:174-177, 333-334`.
- **Neutraliser.** D14: `action: useInProcess` on the `files` variant refused (F-E6, "the platform throws for 'use in
  process'"), `saveTo` required, 0 always written; D13: raw `addMapping` onto `ResultActionType` refused (F-E2).

### T-19
**After "Save to object attachments", `ObjectFiles` points at the SOURCE files.**
- **What happens.** The Object variant produces output collections for both action values. After a save, `ObjectFiles`
  holds the locators of the SOURCE attachments; the copies exist only as Ids in `CreatedObjectFileIds`. A downstream
  element that "works on the saved files" through `ObjectFiles` works on the originals. After "use in process",
  `ObjectFiles` holds locators of existing attachment rows (not temporary copies) and `CreatedObjectFileIds` is empty,
  so switching a saving element to "use in process" silently empties what a consumer of `CreatedObjectFileIds` reads.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:60-87` (`CopyFiles`
  calls `SetReadObjectFiles(fileCopyResults.SourceFileLocators)` at `:85`; the same line is
  `GEN/ObjectFileProcessingUserTask.CrtProcessDesigner.cs:85`, the stand's compiled copy);
  `PD/FileProcessing/FileProcessing.cs:159, 175-178` (`SourceFileLocators` = the locators read by the query, before the
  copy) and `:209-224`; the designer refuses the switch while `CreatedObjectFileIds` is mapped
  (`PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:294-303`). Not
  measured (**M26**, see the banner); ST-01 checks the copied rows only, not what `ObjectFiles` points at.
- **Neutraliser.** D12 rule 4: `saveToAttachments` -> `useInProcess` refused while another element maps from
  `CreatedObjectFileIds` (F-E6); the AC wording "mapped from its output when ResultActionType = 1" corrected (D24);
  describe lists `outputs`. The guide sentence "after saving, `ObjectFiles` points at the source files" (OA.12) and the
  CL-OA knowledge record `after-saving-object-files-points-at-the-source-files.md` are written **only after M26**
  confirms it, and say "measured <date>"; until then the guide states only which outputs each value fills.

### T-20
**Report "use in process" files are temporary.**
- **What happens.** With "use in process" the Report variant writes `SysProcessFile` rows keyed to the process instance
  that owns the element; `CreatedObjectFileIds` stays empty. `CompleteProcess` deletes them, for a root process and for
  a sub-process alike, and for a sub-process it does so BEFORE control and outputs return to the parent. A report
  generated in a sub-process and returned through an Out parameter reaches the parent as a dangling locator. Files the
  parent passes INTO a sub-process are keyed to the parent and survive.
- **Silent or loud.** Silent; what a consumer does with the dangling locator is not traced.
- **Basis / evidence.** source: `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:98-112` (`CreateTempFile`
  at `:105-106`); `CORE/Terrasoft.Core/Process/ProcessUserTask.cs:338`; `CORE/Terrasoft.Core/Process/Process.cs:1528-1537`
  (delete, then complete); `CORE/Terrasoft.Core.Process/ProcessFileStorage.cs:97-118`. Not measured (M16).
- **Neutraliser.** D14/D17: a notice (F-R3, Report only) when a `useInProcess` element's `ReportFiles` flows into an
  Out process parameter ("these generated files are temporary ... consume them in this process or save them");
  wording final after M16. No notice for the Object variant, whose locators point at existing attachments; the guide says so.

### T-21
**The Process variant's output item is `ObjectFile`, while its input item is `File`.**
- **What happens.** The Process variant reads its input by the key `File` and fills its own output `ObjectFiles` with
  items named `ObjectFile` (the copies). The designer never offers this variant's `ObjectFiles` as a mapping source. A
  pairing by item name alone finds no match when this output feeds another file consumer.
- **Silent or loud.** Silent (an unpaired item is null at run time, see T-10).
- **Basis / evidence.** source: `PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:40, 73`;
  `PD/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:201-231` (the designer's source list).
- **Neutraliser.** D5 P2 fallback: when both sides have exactly one FileLocator item they are paired whatever their
  names, with a notice; D18 resolves the source collection's FileLocator item and never assumes its name. M4 confirms
  that `ObjectFiles` holds the copies.

---

## D. Attachment storage

### T-22
**Attachment storage is chosen per entity, and there are two encodings.**
- **What happens.** With `ProcessFeatures.UseSysFileInObjectFileProcessing` on (code default, on the stand, no DB row),
  the designer keeps the LEGACY encoding for a record object that has a BaseFile descendant named `<X>File` (Account ->
  AccountFile; Lead -> FileLead): `Source/TargetEntitySchemaUId = <X>File`, `ConnectedObjectColumnUId` = its link
  column, `*DataEntitySchemaUId` cleared. Every other entity uses SysFile `70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a` +
  `RecordId` + `Source/TargetDataEntitySchemaUId` = the record object. The server simply honours whatever pair it is
  given; a builder that writes one fixed encoding reads no files, or saves them where no page shows them.
- **Silent or loud.** Silent.
- **Basis / evidence.** measured: UO-2 (legacy entries are listed as `<Entity> (<file entity caption>)`), UO-3 (SysFile
  state), UO-4 (legacy state), 2026-10-01. source: `PD/EntitySchemaDesignerUtilities/EntitySchemaDesignerUtilities.js:343-387, 424-446, 509-515`.
  measured (corpus): 0 of 30 shipped elements use SysFile.
- **Neutraliser.** D15: `IAttachmentStorageResolver` (+ `IEntitySchemaHierarchyReader`), deterministic, reproducing the
  designer's ON algorithm; the whole pair is rewritten on every block write; `storage` / `fileObject` /
  `attachments.linkColumn` are identity CHECK members (F-E8). OA ships legacy storage and refuses SysFile; SF lifts it.
  M10 compares the resolver with the designer's list before code.

### T-23
**The SysFile read path has no runtime evidence.**
- **What happens.** An Object read always adds `Type = File`, and when `SourceDataEntitySchemaUId` is set it adds
  `RecordSchemaName = <record object>`. A Freedom UI upload must stamp both for the element to find it. A community
  thread (April 2025, still open in August 2026) reports that the element "always returns an empty collection" with
  "Uploaded file"; the designer's flag path is the platform's fix for that, and nobody has measured it.
- **Silent or loud.** Silent (an empty collection).
- **Basis / evidence.** source: `PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:36-47`; community thread
  recorded in the Academy research (not evidence of behaviour). Not measured (M8).
- **Neutraliser.** D15: SysFile sources refused in OA ("this version does not read SysFile storage yet"); SF lifts the
  refusal only if M8 passes. The user checks the Freedom UI side.

### T-24
**The SysFile write path has no runtime evidence.**
- **What happens.** Every writer sets the link column (`RecordId` in SysFile) and writes `RecordSchemaName` only when
  `TargetDataEntitySchemaUId` resolves. A copy then takes every source attribute the target has not set. A SysFile
  target without `TargetDataEntitySchemaUId` therefore inherits the SOURCE's `RecordSchemaName` (SysFile source) or has
  none (legacy source): the file is attached to no visible record. Whether a written SysFile row carries `Type = File`
  (which a later Object element needs) and appears in the record's Freedom UI attachment list is unmeasured.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PD/FileProcessing/FileProcessing.cs:152-179`;
  `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:77-96`;
  `PD/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:47-66`; `CORE/Terrasoft.File/File.cs:174-180`;
  `CORE/Terrasoft.File/Metadata/EntityFileMetadataStorage.cs:155-174`. Not measured (M23).
- **Neutraliser.** D15 invariants: always write `*DataEntitySchemaUId` in SysFile mode; never write it on a legacy
  side. SysFile targets refused in OA; SF lifts the refusal only if M23 (four write cases) passes.

### T-25
**A missing or wrong link column puts the copy on no record, or back on the source record.**
- **What happens.** `ConnectedObjectColumnUId` missing or not a column of the target: a CROSS-schema copy (ContactFile ->
  AccountFile) loses the source's link (no matching column, skipped with a WARN in the log) and is linked to no record.
  A SAME-schema copy inherits the SOURCE's link through `CopyAttributes` and lands on the source record as a duplicate.
- **Silent or loud.** Silent (a WARN in the log at most).
- **Basis / evidence.** source: `PD/FileProcessing/FileProcessing.cs:193-201` (link set only when the column is found);
  `CORE/Terrasoft.File/File.cs:174-180` (copies only attributes the target has not set);
  `CORE/Terrasoft.File/Metadata/EntityFileMetadataStorage.cs:155-174`.
- **Neutraliser.** D15: the link column is derived by the resolver; R8 / R9 refusals (F-E7) when no conforming lookup
  exists; no `linkColumn: "none"` in v1 ("none" does not mean unlinked for a same-schema copy); describe issue "saved
  files are linked to no record".

### T-26
**Orphan file objects, and a designer save that clears an explicit link column.**
- **What happens.** A file object `<X>File` with no entity named `<X>` (DNSGuideFile, MailboxSettingsFile,
  CreatioAIIntentFile, CreatioAISessionFile, OAuth20AppFile, OmnichannelMessageFile, SysProcessFile, WebServiceV2File)
  is listed by its own caption; as a target the designer finds no link column and saves unlinked files. The shipped
  product process GenerateDNSRecordsSpecification has exactly this shape and compensates with a Modify data element
  that sets `DNSGuideFile.SenderDomain` by `Name contains <domain>`. Separately, the designer recomputes the link column
  on every load and saves the recomputed value, so an explicit link column the designer would not choose is cleared by
  the next designer save.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:668`
  (`initConnectedObjectInfo`), `:92-99`; `PD/EntitySchemaDesignerUtilities/EntitySchemaDesignerUtilities.js:440-445`. measured (corpus and stand describe,
  2026-10-01): GenerateDNSRecordsSpecification targets DNSGuideFile `337466a6...` with an empty `ConnectedObjectColumnUId`.
- **Neutraliser.** D15: an orphan target is refused (R8) unless `saveTo.linkColumn` names a lookup column (for example
  `{object: "DNSGuideFile", linkColumn: "SenderDomain"}`), which is accepted with warning W6 ("opening and saving this
  element in the designer will clear it"); describe issue for an unlinked target.

### T-27
**An empty `ConnectedObjectId` fails one storage and orphans the other.**
- **What happens.** An unset or `Guid.Empty` value is written as `00000000-...`; nothing converts it to NULL. On a legacy
  `<X>File` target the link is a non-weak lookup with a database FK: the insert fails (or `RequiredColumnsEmptyValuesException`
  when the link is required). On a SysFile target `RecordId` is a plain Guid: the copy saves as an orphan. The template
  types `ConnectedObjectId` as Guid, so the builder's constant validator checks the format only, although the designer
  edits it as a lookup of the record object.
- **Silent or loud.** Legacy: loud at run time. SysFile: silent.
- **Basis / evidence.** source: `GEN/FileProcessing.CrtProcessDesigner.cs:193-201`; `GEN/SysFileSchema.CrtCoreBase.cs:102-111`;
  `PB/Parameters/ProcessParameterValueValidator.cs:207-209` (scalar Guid branch) against `:154-181` (lookup branch).
  Not measured (M17; its read-only foreign-key pre-check M17-Q7 runs first).
- **Neutraliser.** D15: a constant `saveTo.recordId` is validated as a LOOKUP of the record object (parse, not empty,
  the record exists, through `EnsureReferencedRecordExists` with `effectiveReference` = the record object); a mapped one
  gets the notice "an empty value at run time fails a legacy target (foreign key) and orphans a SysFile target" (F-E9).

### T-28
**H-G3-1: opening and closing a legacy Object element in the designer can break it.**
- **What happens.** With the flag on, opening a legacy Object element fills the `SourceDataEntitySchemaUId` attribute
  from `SourceEntitySchemaUId` (the element's own `<X>File`) when the Data parameter is empty, which is the normal state.
  On close, `saveReferencedDataSchemas` writes it back as a ConstValue unless `SourceEntitySchemaIsFileSuccessor` is
  set, and that attribute is set only by a change handler, never on load. The runtime would then filter
  `RecordSchemaName` on `<X>File`, a column BaseFile descendants do not have: the element throws. The target half of
  the same mechanism is harmless (an unknown attribute is skipped).
- **Silent or loud.** Silent at the designer save, loud at run time. Unmeasured.
- **Basis / evidence.** source: `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:853-861, 910-922`;
  `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:186, 319-328, 379-391`;
  `PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:36-47`. Not measured (M11 (a) Report half, (b) Object
  half in memory, (c) an optional run).
- **Neutraliser.** M11 before code. D12 rule 5 "repair on touch": any `setElement fileProcessing` on an inconsistent
  pair rewrites the whole pair from `Resolve`, with a notice (F-E9). Describe reports `storage: invalid` with the issue "throws
  at run time" (source half) or "cosmetic" (target half). If M11 confirms it: a CrtProcessDesigner bug Sub-task under FE
  and a `process-files` guide warning. M22 checks builder-made elements after a designer no-op save.

### T-29
**The SysFile designer flag: OFF breaks re-saving, and the obvious read API reads OFF.**
- **What happens.** The server never reads `UseSysFileInObjectFileProcessing`; both encodings run either way. Where the
  flag is OFF, the designer builds its object list from `ModuleStructure`, so a SysFile-mode element opens with an empty
  object select and cannot be re-saved without reconfiguring. Reading the flag through `UserConnection.GetIsFeatureEnabled`
  (configuration code) falls back to the database only and would read OFF on the stand, where the flag has no DB row.
- **Silent or loud.** Silent.
- **Basis / evidence.** measured (2026-10-01): the flag is on, with no `Feature` row. source:
  `PD/EntitySchemaDesignerUtilities/EntitySchemaDesignerUtilities.js:250-292, 509-515`;
  `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:657-670, 866-877`; the
  reachable read `Creatio.FeatureToggling.Features.GetIsEnabled(string)` (already referenced, crt-process-builder `packages/CrtProcessBuilder/Files/CrtProcessBuilder.csproj:137-141`).
- **Neutraliser.** D15 option A: behaviour never depends on the flag. A notice (F-E9) for a SysFile-mode element on an
  environment where the read returns OFF, through the one-method feature seam shared with D17, never a refusal, and
  shipped only after M25 shows the read returns true on the stand (otherwise it would fire everywhere).

### T-30
**Copies inherit source attributes and are always of type File.**
- **What happens.** A copy takes every source attribute the target has not set: Notes, Tag, FileGroup, LockedBy /
  LockedOn, the source's link column, CreatedBy (predicted). `TypeId` is never copied, so every copy is `Type = File`; a
  copied LINK becomes an empty File. Object reads always filter `Type = File`, so links are never read.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `CORE/Terrasoft.File/File.cs:174-187`;
  `CORE/Terrasoft.File/Metadata/EntityFileMetadataStorage.cs:29-39, 192-201`;
  `PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:36-38` (`FileConsts.FileTypeUId`).
- **Neutraliser.** Guide only (`process-files`): "a copy keeps the source's notes, tag and lock; links are never read or
  copied". M23 and the comparison piggybacked on M1 (G1 E5) record the attribute inheritance on the stand.

---

## E. Filters, sort and record count

### T-31
**The filter root is ignored by the runtime, and the record scope has two vocabularies.**
- **What happens.** The Object variant's filter runs on `SourceEntitySchemaUId` (`AccountFile` or `SysFile`) whatever
  root the stored JSON names; the Report variant's on the report entity. Today `setFilter` on these elements goes to the
  generic `DataNodeFilterTarget`, which roots the filter on whatever `filter.object` the caller sends and never checks it.
  The natural phrasing `{object: "Account", Id = X}` is evaluated as `AccountFile.Id = <account id>`: zero files.
  Separately, the filter grammar's `expression` is a raw meta-path token and `CurrentUserContact` is a macro there,
  while every other `recordId` in the package takes `expression` = a formula such as `[#SysVariable.CurrentUserContact#]`.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Filters/DataNodeFilterTarget.cs:57-91`; `PB/Operations/DataSourceFilterValue.cs:16-18`;
  `PB/Contracts/FilterContracts.cs:100-150`, `PB/Filters/MacrosCatalog.cs:67`;
  `PB/Contracts/ProcessDescriptorContracts.cs:925-966`. The SysFile scope shape the designer writes is not captured (M7).
- **Neutraliser.** D16: the record scope moves into the block (`attachments.recordId`, `report.recordId`) with the one
  value-source contract; `FileProcessingScopeFilter` owns `Split` / `Join`; `FileProcessingFilterTarget` (priority 50)
  refuses a `filter.object` other than the runtime root (V1), a filter on an unconfigured element (V2), a scope of the
  wrong type (V3) and a scope set twice (F-F1); only `[#SysVariable.CurrentUserContact#]` is accepted as a scope formula.

### T-32
**An empty filter reads 50 arbitrary files, or prints the whole entity.**
- **What happens.** Object: with no filter the element reads up to `RecordsToRead` (default 50) arbitrary files of the
  file object, and with "save" copies all of them onto the target. Report (MS Word): one document per record of the
  whole report entity, with no cap. A shipped test process holds an empty-filter "save" element (`CRM60006PP`).
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `GEN/ObjectFileProcessingUserTask.CrtProcessDesigner.cs:98-100` (`RowCount = RecordsToRead`);
  `GEN/ReportEngine.Reports.cs:208-228, 257-276`. measured (corpus): `CRM60006PP`.
- **Neutraliser.** D16 empty-filter policy, evaluated at the END of the request for the elements created or reconfigured
  in it (the Delete data ledger pattern): Object + save and Word reports with no selecting filter are refused
  (`failedOperationIndex: null`, the element and operations named); Object + use in process gets a notice (F-F2).
  Untouched elements are never examined, so a modify of a designer process that already holds one is not refused.

### T-33
**`numberOfRecords`: omitted means 50, not "all"; 0 reads nothing.**
- **What happens.** `readData.numberOfRecords` omitted reads all records; on this element `RecordsToRead` omitted is the
  template's 50. `RecordsToRead` 0 or unset becomes `SELECT TOP 0`: zero files, Completed. A negative value means no limit.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PD/ObjectFileProcessingUserTask/metadata.json:48-56` (template `GS2 "50"`);
  `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:763-765` (unset reads 0); the designer range
  `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:353-357` (1..5000).
- **Neutraliser.** D16: 1..5000 validated (F-F3); omitted = the template copy (D20); on create, when omitted on a saving
  or forwarding element, the notice "reads at most 50 files (numberOfRecords defaults to 50, max 5000)" (F-F4). The
  guide states the difference from readData.

### T-34
**In SysFile mode the designer offers sort columns of the record object.**
- **What happens.** Right after a SysFile-mode source is picked, the designer's sort list holds the RECORD object's
  columns (UO-1's "GPS E" is `AccountAddress.GPSE`), while the runtime query is rooted on SysFile. A column SysFile lacks
  throws `ItemNotFoundException` before any file is read; a column both have (`Id`, `Name`, `CreatedOn`, `Notes`)
  silently sorts the FILES. When the panel is reopened, entries that are not SysFile columns are dropped silently and the
  next save rewrites `OrderByInfo` without them. The designer also stores multi-entry sorts
  (`SysFileStorage:1:1,Contact:1:0`), while the block takes one `{column, direction}`; what an ARRAY sent into that object
  member does at the WCF binder is unmeasured.
- **Silent or loud.** Loud (missing column) or silent (shared column, dropped entries, hidden secondary entries).
- **Basis / evidence.** source: `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:169-179, 188, 258-264`;
  `PD/SortingOrderControlsMixin/SortingOrderControlsMixin.js:321-362, 384-387`. measured: UO-1 (2026-10-01). Not
  measured: M9 (the throw), M24 (array/object at the binder).
- **Neutraliser.** D16 V9: `sort` is one direct column NAME of the runtime root (SysFile in SysFile mode), refused
  otherwise (F-F3); the designer's record-object list is not mirrored; a stored multi-entry `OrderByInfo` is kept
  verbatim while `sort` is not sent and describe reports its primary entry with an issue naming the hidden ones; the guide
  pins the M24 result. `sort` on a Report element is refused (no `OrderByInfo`).

### T-35
**A SysFile retarget keeps a filter scoped to the old object's record.**
- **What happens.** `DataSourceFilterValue.ClearIfForeign` keeps a stored filter whose root NAME equals the new root. In
  SysFile mode the root is SysFile for every record object, so moving an element from "Account address" to "Contact
  address" keeps a `RecordId` condition that points at an Account address record, while the runtime adds
  `RecordSchemaName = ContactAddress`: zero or wrong files. The designer clears the filter on every source-object change.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Operations/DataSourceFilterValue.cs:62-85`;
  `PD/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:184-202, 228-248`;
  `PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:36-47`.
- **Neutraliser.** D12 rule 2: any `attachments.object` change that resolves to a different storage pair clears the
  stored filter, the record scope and the sort (designer parity), with a notice (F-E9); `ClearIfForeign` is not used for this
  element. Report: a different report object clears; the same report object keeps them and re-runs V3 (a documented
  divergence). Describe issue for a SysFile scope whose lookup reference is not the record object. PBT for the SysFile
  retarget.

---

## F. Generated report

### T-36
**DevExpress printables are offered and always throw; FastReport is unverified.**
- **What happens.** The designer lists every printable the user can read, of every type, by caption. The runtime
  dispatches on `Type.Name`: "MS Word" and "FastReport" run, anything else (DevExpress) throws `NotSupportedException`
  eagerly. FastReport is accepted by the dispatch, but its process path resolves the generator without a
  UserConnection and the stand has no FastReport assemblies or printables.
- **Silent or loud.** Silent at design time, loud at run time (DevExpress); unknown (FastReport).
- **Basis / evidence.** source: `GEN/ReportEngine.Reports.cs:110-125` (type map), `:163-178, 320-334` (designer list,
  no type filter), `:296-317` (`Generate`, default arm throws); platform pin `UT/Reports.UnitTests/ReportEngine_Tests.cs:211-231`.
  measured (2026-10-01): the stand has 5 printables, all MS Word.
- **Neutraliser.** D17: DevExpress and any other type refused (F-R1); **FastReport refused in this ticket** (owner
  decision), additive to lift after M15; describe issue for a DevExpress printable on a designer-built element. Unit
  tests only (no stand fixture).

### T-37
**No PDF converter: a Word report silently stays `.docx`.**
- **What happens.** A Word report becomes PDF only when `SysModuleReport.ConvertInPDF` is true AND an `IPdfConverter`
  is bound under "PdfConverter"; only optional packages bind it (Apryse, Aspose, Aspose Cloud, WnvWordToPdf). Otherwise
  the file is `.docx`, with no warning and no log. The UI print button converts by another route, so UI and process
  output can differ.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `GEN/ReportEngine.Reports.cs:266-274` (`ClassFactory.TryGet("PdfConverter", ...)`).
  measured (2026-10-01): `ConvertInPDF = false` on all 5 stand printables.
- **Neutraliser.** D17 notice when `ConvertInPDF = true` ("PDF only when an MS Word -> PDF converter package is
  installed; otherwise .docx") (F-R3); `report.convertToPdf` is describe-only; the guide never promises PDF.

### T-38
**A Word report with `IsSeparateReports = false` gives every file the same name.**
- **What happens.** MS Word always generates one file per record; `IsSeparateReports` changes only the naming. With
  false, every file is named `prefix + ext`: N identical names on one target record. The designer forces the checkbox on
  and disabled, also when an existing element is opened, so a stored false silently becomes true at the next designer
  save.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:156-174`;
  `GEN/ReportEngine.Reports.cs:257-276` (the flag is never read for Word);
  `PD/ReportFileProcessingUserTaskPropertiesPage/ReportFileProcessingUserTaskPropertiesPage.js:153-163, 396-399`; the
  name formula is pinned by `UT/ProcessDesigner.UnitTests/ReportFileProcessingUserTask_Tests.cs:214-319`.
- **Neutraliser.** D17: on Word, `separateReports` is written true when omitted; an explicit false is ACCEPTED as given
  with a notice ("the designer forces this on at the next save; the file names will change"), so a described legacy
  element resubmits (F-R3); describe issue for a Word element with false.

### T-39
**The file-name column has rules the runtime does not enforce.**
- **What happens.** The name is `caption[. ReportName][ (i) | . <column value>] + ext`. `ReportName` is ignored when a
  name column is set. The designer offers only TEXT-family columns and only with separate reports on; a lookup column
  is predicted to throw `IndexOutOfRangeException`. The column is resolved whenever its UId is set, even with separate
  reports off, so an unknown column UId throws. The `(i)` numbering follows an unordered query; the caption is read in
  the RUNNER's culture.
- **Silent or loud.** Mixed: wrong-column cases throw at run time; ordering and culture are silent.
- **Basis / evidence.** source: `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:114-139, 156-174`
  (`schema.Columns.GetByUId` at `:125`, guard at `:160` does not check the flag); designer
  `PD/ReportFileProcessingUserTaskPropertiesPage/ReportFileProcessingUserTaskPropertiesPage.js:352-376, 453-466`; `PD/EntitySchemaDesignerUtilities/EntitySchemaDesignerUtilities.js:584-605`.
  Optional M-G6-4 (lookup column).
- **Neutraliser.** D17 F-R2: `fileNameSuffixColumn` refused with separate reports off, when it is not a TEXT-family
  column of the report object, or together with `fileNameSuffix`. Guide: record order is unspecified; captions are
  localized in the runner's culture.

### T-40
**A constant file-name suffix is a localizable value.**
- **What happens.** `ReportName` is localizable Text: a constant lands in the resource
  `BaseElements.<El>.Parameters.ReportName.Value`, not in `GS2`, and is written under the request thread's culture. A
  value written under a non-default culture may find no default-culture row at run time and the suffix may vanish. A
  resource row is not proof the value is set: shipped PrintContractsReport has `ReportName` at Source None while its
  resource still carries a `.Value`.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessSchemaParameterValue.cs:221-230, 459-469`;
  `CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:916-949`. measured (corpus): PrintContractsReport. Not measured:
  M18 (culture).
- **Neutraliser.** D11 / D17: the constant goes through the localizable resource path (designer parity), a formula
  inline; "set" is decided from the metadata `Source`, never from a resource row; the guide's culture note follows M18.

### T-41
**"Save" attaches every generated report to ONE record.**
- **What happens.** With "Save to object attachments" every generated file goes to the single `ConnectedObjectId`
  record of the target object, not to the record each report was generated for. N invoices give N files on one target.
  Product processes attach a report to its own record by filtering `Id = param` and targeting the same parameter.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:77-96`. measured
  (stand describe, 2026-10-01): PrintInvoiceReport and PrintQuotationReport filter `Id = param` and target the same param.
- **Neutraliser.** D17 notice when the scope or filter can match more than one record ("all N files are attached to that
  one record") (F-R3); the guide shows the product pattern; per-record attachment is a multi-instance sub-process.

### T-42
**Printable identity is easy to get wrong.**
- **What happens.** `ReportId` is `SysModuleReport.Id`. Captions are not unique (two shipped printables are captioned
  "Contract"); an unreadable or unknown id throws `ArgumentException` eagerly; `list-printables` calls the id
  `templateId`; the stand has no printable for Account or Contact, so an e2e written against those objects finds none.
- **Silent or loud.** Loud at run time (unknown id); a test that picks "the first printable" is silently wrong.
- **Basis / evidence.** source: `GEN/ReportEngine.Reports.cs:188-208`; `clio/clio/Command/McpServer/Tools/ListPrintablesTool.cs`.
  measured (2026-10-01): 5 MS Word printables on the stand (Contract, DNS Requirements, DNS Specifications, Invoice,
  Quotation).
- **Neutraliser.** D17: `IReportTemplateReader` resolves `report.printable` by id (preferred), `[#Lookup...#]` macro or
  caption; an ambiguous caption is refused with every candidate's id, type and object; an unknown one is refused with
  the Report setup hint and up to N candidates by `templateId` (F-R1). The guide bridges `templateId` ->
  `report.printable`. e2e uses Invoice / Opportunity / Contract and `Assert.Ignore` when `list-printables` returns 0 rows.

### T-43
**A mid-run failure leaves partial output; an empty Word template fails on the first record.**
- **What happens.** The per-type generators are lazy: files 1..k-1 are already written (and, with "save", already
  attached and listed in `CreatedObjectFileIds`) when record k fails; no transaction is visible. A Word printable whose
  template is empty is predicted to throw an NRE on the first matching record, and not at all when no record matches.
- **Silent or loud.** Loud, with partial side effects.
- **Basis / evidence.** source (hypothesis): `PD/ReportFileProcessingUserTask/ReportFileProcessingUserTask.cs:141-154`;
  `GEN/WordReportGenerator.CrtNUI.cs:74-82, 252-263`.
- **Neutraliser.** Guide only: "a failure mid-run leaves the files already generated". The builder does not read
  template BLOBs.

### T-44
**`EnableReportFileProcessingUserTask` only hides the designer's source selector.**
- **What happens.** The selector "What is the source of the file?" is visible only with this feature; the server never
  reads it. On an environment with the feature OFF, an element the builder makes runs, but a user cannot see or change
  its source in the designer.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:496`.
  measured (2026-10-01): ON on the stand, shipped ON.
- **Neutraliser.** D17: a notice when the feature reads OFF ("this environment's designer hides the source selector;
  the element runs"), read through `Features.GetIsEnabled` behind the shared seam (a DB feature, so the read is right),
  never a refusal (F-R3).

---

## G. Element identity and build routes

### T-45
**Changing the source replaces the element.**
- **What happens.** "What is the source of the file?" is not a parameter: each option is its own user-task schema. The
  designer replaces the element (same UId, position and caption; new name; no value carried over), refuses while
  another element's or a process parameter's VALUE references the element, and asks for confirmation only when the
  element is configured. The palette offers only "Process file" (the Object schema). A builder that mutates `SchemaUId`
  in place keeps values that belong to another schema and dangling references nobody asked about.
- **Silent or loud.** Silent if mutated in place.
- **Basis / evidence.** source: `PD/BaseFileProcessingUserTaskPropertiesPage/BaseFileProcessingUserTaskPropertiesPage.js:233-236, 241-312`;
  `NUI/designers/process-schema-designer/process-schema-designer-view-model-new.js:565-589, 1468-1487`;
  `NUI/manager/process-schema-manager/base-process-schema.js:929-939`. measured (2026-10-01): the stand's
  `getExcludedMenuItems()` returns the Report and Process schemas.
- **Neutraliser.** D12: a source change is refused; the text names the dependents found by
  `ProcessElementDependencyScanner` and says "remove it and add a new element" (F-E3). On update the stored `SchemaUId`
  fixes the variant; a group of another variant is refused (F-E4).

### T-46
**The generic `userTask` route builds a drawn but unconfigured element.**
- **What happens.** `{type: userTask, userTaskName: "ObjectFileProcessingUserTask"}` builds a correctly drawn element
  with no `ResultActionType`, no source and no target. It saves green (the platform pre-save gate has no
  required-parameter rule) and throws at run time on `GetInstanceByUId(Guid.Empty)`. `list-user-tasks` lists all three
  schemas with "pass this as userTaskName on a userTask element".
- **Silent or loud.** Silent at save, loud at run time.
- **Basis / evidence.** source: `PB/Elements/UserTaskElementHandler.cs:74-90`; `PD/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:94-112`;
  `PB/Catalog/UserTaskCatalog.cs:17-46`; `PB/Contracts/ListUserTasksContracts.cs:43`.
- **Neutraliser.** D13, F-E1, per cut: OA refuses the generic route for the Object schema, keyed on the RESOLVED schema
  UId right after `FindInstanceByName` (covers case, whitespace and the `performTask` alias); RP refuses it for the
  other two. The element-catalog sentence adds the Process file element to the "must NOT be built as a generic userTask"
  list "and, unlike the other three, the server refuses it".

### T-47
**Raw `addMapping` onto configuration parameters bypasses every invariant.**
- **What happens.** `addMapping` onto `ResultActionType`, `SourceDataEntitySchemaUId`, `ConnectedObjectColumnUId`,
  `DataSourceFilters` and the like writes whatever is sent; for example `ResultActionType = 1` on the Process variant
  saves green and throws (T-18), and a storage half written alone produces T-24 or T-28.
- **Silent or loud.** Silent at save.
- **Basis / evidence.** source: `PB/Mappings/ProcessMappingService.cs:48-63` (the one funnel), D13.
- **Neutraliser.** D13 F-E2, per cut: `addMapping` onto the 13 binder-owned parameters of a claimed schema is refused
  and points to `setElement fileProcessing`; the value inputs (`Files`, `Files.File`, `ConnectedObjectId`, `ReportName`)
  stay mappable and run the same rules as the block.

### T-48
**An older package drops the `fileProcessing` block and answers success.**
- **What happens.** No package contract implements `IExtensibleDataObject`, so an undeclared member is discarded by the
  WCF serializer. An unknown TYPE token is refused loudly, but a block that rides a known type, or an unknown key next to
  a known field in `setElement`, is dropped and the call succeeds.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Contracts/VersionContracts.cs:22-26`; `PB/Elements/ProcessElementFactory.cs:57-66`
  (unknown type refused), `:87-97` (tolerated `approval` route), `:100-113` (strict gates). measured precedent:
  `clio/clio.mcp.e2e/SubProcessElementToolE2ETests.cs:366` (the factory-gated case). M20 measures the `setElement` case.
- **Neutraliser.** D10: a new TYPE token (`fileProcessing`, alias `processFile`), so an older package refuses it; a
  strict `EnsureBlockMatchesHandler` gate; D26: the `[RequiresPackage]` floor is raised with each cut, so clio refuses an
  older server before sending.

### T-49
**The new token needs a ManagerMap arm in clio, and the handler must come first.**
- **What happens.** clio's `ManagerMap.ResolveDataId` maps a token to an element kind; `fileprocessing` does not end in
  "usertask", so it falls to `EventType.Unknown` and `validate-process-graph` reports a hard Error on a graph the server
  builds. In the package, `ResolveBuildType` takes the FIRST handler whose `CanBuild` matches, so a handler registered
  after `UserTaskElementHandler` never runs.
- **Silent or loud.** Loud (a validator Error; a composition tripwire test).
- **Basis / evidence.** source and measured: `clio/clio/Command/ProcessModel/Schema.cs:1140-1149`;
  `PB/Elements/ProcessElementFactory.cs:132-133`; `PBA:124-150`; tripwire `PBT/CrtProcessBuilderAppTests.cs:139-199`.
- **Neutraliser.** D10: `"fileprocessing"` and `"processfile"` join the explicit `EventType.UserTask` arm in the PT clio
  PR (one release before any package emits the token), with `[TestCase]`s in `ManagerMapResolveDataIdTests`; the
  handler is registered before `UserTaskElementHandler` and the tripwire is updated.

---

## H. Builder serialization

### T-50
**Old parameter snapshots: shipped elements do not carry today's parameter set.**
- **What happens.** Shipped elements drift from the templates: 21 of 30 have no `TargetDataEntitySchemaUId`, 11 of 14
  Object elements no `SourceDataEntitySchemaUId`, 10 of 30 no `ConsiderTimeInFilter`, and 7 carry `ConnectedObjectId`
  typed Lookup. Every design-time load runs the activity parameter diff and repairs exactly these cases, so the modify
  path always sees the current 13/8/13 set. Describe may not: a compiled process instance is created without the sync,
  so describe can return the raw stored snapshot. A binder that creates a missing parameter by hand makes an
  `IsDynamic` parameter the sync never maintains; a reader that trusts the stored type misreads the Lookup-typed form.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:238-322, 587-599`;
  `PB/Schema/ProcessSchemaRepository.cs:239-240, 265-310`. measured (corpus scan, 2026-10-01): the counts above. Not
  measured: M13 (is describe of PrintContractsReport converged?).
- **Neutraliser.** D20 / D19: the binder works only on the `GetDesignInstance` instance; a missing template parameter is
  refused ("the element's user-task parameters are not synchronized", the `ChangeDataConfigBinder.cs:959-968`
  precedent); values are encoded by the TEMPLATE's type; the describe reader tolerates absent parameters and reads a
  Lookup-typed `ConnectedObjectId` like a Guid, with no "out of sync" signal.

### T-51
**A server-built `ConsiderTimeInFilter` is "false", the designer's is "true".**
- **What happens.** A parameter the server adds from the template takes `DefValueForExistingProcess` (`GS8 "false"`),
  while a new designer element keeps the template's Script "true". Only the designer's filter editor reads it: left at
  "false", a later designer edit of the filter silently drops the time of day from date-time comparisons.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessSchemaActivity.cs:306-320`;
  `PD/ObjectFileProcessingUserTask/metadata.json:144-153`; `PD/FilterModuleMixin/FilterModuleMixin.js:509-521, 839-843`.
  measured: UO-3 (the designer writes source 3, "true"). Not measured: M14.
- **Neutraliser.** D20: on create the handler writes an exact copy of the template value, provenance included (Script,
  "true", GS8 "false", GS5 = the template); never repinned on an edit. PBT pins it.

### T-52
**Server-built nested items keep the template's UIds.**
- **What happens.** The platform copies nested items once with their parent, keeping the TEMPLATE's UIds, so two
  server-built Object elements share the same nested `File` UId and `IL2` is empty; the designer mints fresh UIds with
  `IL2` = the element UId. Mapping tokens embed the nested item UId (`[Element:{el}].[Parameter:{itemUId}]`), so
  re-minting on an existing element orphans every consumer mapping.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessSchemaParameter.cs:184-188`;
  `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:66-82`; measured (corpus):
  `PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:34, 47`. Not measured: M14.
- **Neutraliser.** D20: re-mint on CREATE only, inside `FileProcessingElementHandler.Create`, before `Configure` and
  before any mapping of the same request resolves the element's items; never on modify. PBT: a create batch whose later
  mapping reads `OF1.ObjectFiles.File` resolves to the re-minted UId; a modify leaves template UIds and their consumers intact.

### T-53
**An in-place edit of a template value keeps the template's provenance.**
- **What happens.** A value mutated in place keeps `ModifiedInSchemaUId` (`GS5`) = the template. Describe lists a
  non-output element parameter only when its value is stamped with THIS schema, so the value looks unset, and any
  "configured" check that uses provenance reads it as unconfigured. (Correction X1: an ordinary user task is initialised
  with all its parameters, so the runtime does read it; only a sub-process start and the multi-instance count filter by
  provenance.)
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Describe/ProcessDescriber.cs:187-193`; `CORE/Terrasoft.Core/Process/ProcessComponentSet.cs:502-540`;
  `CORE/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:718-753`.
- **Neutraliser.** D20: every value the caller chose or the binder derived is a NEW `ProcessSchemaParameterValue`, with
  `Source` set before `Value` and `ModifiedInSchemaUId = schema.UId`; template defaults are exact copies (T-51).
  "Configured" = `Source != None && GS5 == process`.

### T-54
**Schema UIds must not go through the mapping path's Lookup validator.**
- **What happens.** `Source/TargetEntitySchemaUId` and `*DataEntitySchemaUId` hold an ENTITY SCHEMA UId. The mapping
  path's Lookup validator checks that a `SysSchema` RECORD with that Id exists, and a `SysSchema.Id` is not its `UId`; a
  binder that writes these through `ApplyMapping` is refused or validates the wrong thing.
- **Silent or loud.** Loud (refused) or silently wrong, depending on the value.
- **Basis / evidence.** source: `PB/Parameters/ProcessParameterValueValidator.cs:166-189, 286-293`; the same trap in
  `PB/Elements/ChangeDataConfigBinder.cs:900-909`. **Measured 2026-10-02** on 1.6.6.54: `addMapping` of the ContactFile
  schema UId `e9eafee9…` onto `OF1.SourceEntitySchemaUId` is refused with "no SysSchema record has this id"
  (`48118164cdf1`), the loud half; so today an agent cannot set the element's object through the generic route at
  all and has to pick it in the designer ([open-questions](eng-92719-file-processing-element-open-questions.md) C.7).
- **Neutraliser.** D11: these parameters are written directly as ConstValue by the binder; raw `addMapping` onto them is
  refused (T-47).

---

## I. Describe and the round trip

### T-55
**Describe hides a binding that exists only on a nested item.**
- **What happens.** Describe lists a non-output element parameter only when its ROOT value is set in this schema, and
  projects item properties only under a listed root. `Files` is Direction In, so a correct nested-only binding
  (`Files.File <- $File`, T-11) is invisible and the element looks unbound. An agent then "fixes" it.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Describe/ProcessDescriber.cs:187-193`; `PB/Parameters/ProcessParameterService.cs:152-154`;
  `PD/ProcessFileProcessingUserTask/metadata.json` (`Files` L12 = 0).
- **Neutraliser.** D8: describe also lists a parameter when ANY nested item carries a value stamped with this schema;
  D19: the block reports `files` and `bindingLevels` (`both | itemOnly | outerOnly | none`); FB-14. PBT: a `Files` input
  bound only on `Files.File` is listed. Designer probes read the in-memory element, not describe (T-61).

### T-56
**Describe today returns these elements as a generic user task with raw GUIDs.**
- **What happens.** On 1.6.6.54 a Process file element describes as `buildType: "usertask"` with
  `userTaskName`, a raw parameter list, `ResultActionType` as a bare integer, `ReportId` and `TargetEntitySchemaUId` as
  bare GUIDs, and no semantic block; only the `filter` is decoded. Inherited defaults (`RecordsToRead` 50, an unset
  `ResultActionType`, an empty `ConnectedObjectColumnUId`) are invisible. An agent reasoning from that output cannot
  tell the variant, the action or the target.
- **Silent or loud.** Silent.
- **Basis / evidence.** measured (2026-10-01): describe of PrintQuotationReport, correlation-id 16307feb8a4d, and of
  PrintInvoiceReport / GenerateDNSRecordsSpecification.
- **Neutraliser.** D19: a typed `fileProcessing` block with EFFECTIVE values in the write vocabulary, per cut (OA: Object
  elements; RP: Report and Process elements); the raw `parameters[]` stays (lossless). The `buildType` change of the
  four shipped product processes is a visible change listed in the RP PR.

### T-57
**Describe decodes only three-segment record-column sources.**
- **What happens.** Describe turns a stored Script back into `sourceElement` / `sourceElementParameter` / `sourceColumn`
  only for a three-segment record-column path; every other source, including a plain element output such as
  `[#OF1.ObjectFiles#]`, comes back as a `value` formula. Replayed, it becomes an `expression`, which bypasses the
  structured checks (P1, R-M1, R-M2).
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `PB/Contracts/DescribeContracts.cs:1456-1471` ("Null for every other value").
- **Neutraliser.** D8 (in PT): a stored Script that is exactly one element-parameter token is also reported as
  `sourceElement` + `sourceElementParameter` (dotted for a nested item), `value` kept; D5: an `expression` source is
  written verbatim and never auto-binds, resets or pairs, so replaying old formulas changes nothing. Decoding single-token
  PROCESS-parameter sources is a Sub-task of PT (it needs a field on clio's bagless `DescribedParameter`).

### T-58
**clio's filter DTOs drop any member they do not declare.**
- **What happens.** `DescribedFilter`, `DescribedFilterGroup` and `DescribedFilterCondition` carry no
  `[JsonExtensionData]` bag, so a new filter member a package reports is dropped by every clio that does not declare it.
  `DescribedElement` does carry one, so an undeclared element block survives.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `clio/clio/Command/ProcessModel/IProcessDescriber.cs:720-726` (the element bag),
  `:1621-1643` (filter DTOs); `clio/docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md`.
- **Neutraliser.** D16: the record scope lives in the block, not in `FilterDescriptor`, so the filter DTOs are untouched;
  D19: every nested type of clio's `DescribedFileProcessing` gets its own bag, a `ServerProcessDescriberTests` case
  round-trips an unknown member at every level, and the knowledge record is updated in the same clio PR.

### T-59
**Describe-only members sent back on write.**
- **What happens.** The block reports members that legitimately differ between a describe and a rebuild: localized
  captions, an environment setting (`convertToPdf`), `issues`, `bindingLevels`, `actionStored`, display values. A rule
  "accept when equal" would refuse correct resubmissions (another culture, a repaired state). Today WCF drops them;
  once SK (strict descriptor keys) lands they are refused as unknown keys.
- **Silent or loud.** Silent today, loud later.
- **Basis / evidence.** source: `PB/Contracts/VersionContracts.cs:22-26`; the existing blocks never declare describe-only
  fields on the write contract (`PB/Contracts/DescribeContracts.cs:379-500, 937-1024`).
- **Neutraliser.** D11: two kinds of read-only member. Identity CHECK members (`source`, `storage`, `fileObject`,
  `attachments.linkColumn`, `report.object`, `report.type`) are on the write contract, accepted when equal and refused
  with the derived value otherwise (F-E8). Describe-only members are not on the write contract; the guide says "remove
  them before resubmitting"; SK's read-back list must learn them (D26).

---

## J. Verification instruments and delivery

### T-60
**The session's clio MCP client can be older than the code under test.**
- **What happens.** The `mcp__clio__*` tools run whatever clio the host started. On 2026-10-01 that client stripped
  `itemProperties`, `isOutput` and `isRequired` from describe output, although the server and clio master declare them.
  One research report concluded from such output that "Out collections show no item-property shape". An older client
  also drops arguments it does not know without a word.
- **Silent or loud.** Silent.
- **Basis / evidence.** measured (2026-10-01): describe of PrintQuotationReport, correlation-id 16307feb8a4d, against
  `PB/Contracts/DescribeContracts.cs:1419-1420, 1430-1431, 1489-1490` and `clio/clio/Command/ProcessModel/IProcessDescriber.cs:1930, 1940, 2010`.
- **Neutraliser.** Verification protocol ([pr-split](eng-92719-file-processing-element-pr-split.md)): before trusting a
  live call, check `get-tool-contract` shows the new vocabulary; drive the PR's own clio build over stdio for
  measurements; the e2e suite runs on the built clio.

### T-61
**Two research readings that were wrong, and the instruments that caught them.**
- **What happens.** (1) UO-1 showed only "Use in process" under "What to do with file?" for Account address; it was the
  combobox list filtered by the text already in the field, not a business rule: both actions are offered for legacy and
  SysFile entries. (2) A stand report said "Add files" on the Process-parameter page creates a `COMPOSITE_OBJECT_LIST`
  In parameter; that is `ProcessFilesControlSchema`, which belongs to the Send email (`Attachments<N>`) and Creatio.ai
  (`Files<N>`) slots. The Process-variant page binds the nested `Files.File` of the schema's own `Files` input.
- **Silent or loud.** Silent: both looked like platform behaviour.
- **Basis / evidence.** measured (UO-1 resolved, 2026-10-01); source:
  `PD/ProcessFileProcessingUserTaskPropertiesPage/ProcessFileProcessingUserTaskPropertiesPage.js:44-57, 73-79`.
- **Neutraliser.** Designer probes clear the combobox text before reading a list, read the element state in memory
  (`Terrasoft.ProcessSchemaManager.items[0].instance.flowElements`), and confirm a UI reading against the page source.
  UI results are checked by the user.

### T-62
**Stale guidance, a knowledge-first merge, and articles with no room left.**
- **What happens.** A failed `update-knowledge` keeps serving the OLD generation; only `info-knowledge`'s library version
  shows it. A knowledge PR merged before its clio PR once published a floor no clio bundled
  (https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/198, fixed by
  https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/201). The articles new text would naturally go
  into are full (process-modeling and activity-connections at 99.9%, parameters 98.8%, element-catalog 97.7%), and a
  `name=` pointer in a tool description must be in the curated-names fixture.
- **Silent or loud.** Silent (stale guidance, unpublished pointer); loud (size and fixture tests).
- **Basis / evidence.** measured (budget replica, 2026-10-01); source:
  `clio/clio.tests/Command/McpServer/WorkspaceTemplateGuidanceDriftTests.cs:537-594`.
- **Neutraliser.** D25 / D27: one new guide `process-files`; merge order per ticket package -> clio -> knowledge, by a
  human; the knowledge generation is PUBLISHED before the clio PR that names `process-files` merges; every guidance-dependent
  verification starts with `info-knowledge`.

### T-63
**The process-designer e2e never runs in TeamCity, and an old stand gives Ignored, not Failed.**
- **What happens.** The selection's `baseFilter` excludes `McpE2E.ProcessDesigner`, the category every process-builder
  e2e (including the Binary refusal) carries. A stand with an older package makes new fixtures Ignored. A green run can
  therefore prove nothing.
- **Silent or loud.** Silent.
- **Basis / evidence.** source (read 2026-10-01): `clio/clio.mcp.e2e/TestSelection/mcp-e2e-selection.json:46`;
  `clio/clio.mcp.e2e/Support/Mcp/ProcessDesignerE2EArrange.cs:73-93`.
- **Neutraliser.** The PBT pins are the automated guard for every refusal text; the stand protocol runs the e2e by hand
  and requires **Ignored = 0** on the new fixtures as evidence ([pr-split](eng-92719-file-processing-element-pr-split.md)).

### T-64
**What is installed is not always what was built.**
- **What happens.** An install resolves the bundled archive from clio's BUILD OUTPUT, so `clio compress -d <repo path>`
  changes nothing until clio is rebuilt. Two in-review package PRs stamped the same version twice (1.6.6.42 and
  1.6.6.49), so "newer" is not "contains". Rebundling mid-review leaves a reviewer's clio refusing the stand
  (`RequiredPackageChecker`). Parallel schema writes against this .NET Framework stand crash its app pool, and it
  rejects HTTP DELETE.
- **Silent or loud.** Silent (the wrong archive); loud (the refusal, the crash).
- **Basis / evidence.** measured (2026-10-01, `git show` of `descriptor.json` at `8c3acf07`, `9eae8795`, `4442d802`,
  `c7415f67`); source: `clio/docs/agent-instructions/bundled-packages.md:127-340`.
- **Neutraliser.** Cut protocol ([pr-split](eng-92719-file-processing-element-pr-split.md)): claim the number in the PR
  before cutting, rebuild clio, `list-packages` plus one behaviour probe only the new code can answer, reinstall the
  stand with each cut and tell every verifier; schema writes one at a time; cleanup through `execute-dataservice-batch`.

### T-65
**Unit tests cannot prove the runtime behaviour this feature depends on.**
- **What happens.** The runtime reader is `internal`, so no package test can run a binding through it; the platform's
  file classes live in `Terrasoft.Configuration`, which the package cannot reference and which the test build does not
  have; the parameter sync on `SchemaUId` needs the real platform. A green suite proves the metadata shape only.
- **Silent or loud.** Silent.
- **Basis / evidence.** source: `CORE/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:16`;
  `PBT/UserTaskElementHandlerCreateTests.cs:118-125`.
- **Neutraliser.** D28: tests pin the metadata (GS1/GS2 at both levels, nested UId references, the target block) and
  mirror the configuration constants (`ResultActionType` 0/1, row keys `File` / `ObjectFile` / `Id`,
  `FileConsts.FileTypeUId`); runtime claims go to the stand runs in the banner. Platform tests in `UT` are a reference only.

### T-68
**A nested-item mapping on the CREATE path throws a bare NullReferenceException.**
- **What happens.** On CrtProcessBuilder 1.6.6.54, `create-business-process` with a generic-route Process file
  element pair and `mappings[]` that include `PF1.Files.File <- OF1.ObjectFiles.File` fails with "Object reference not
  set to an instance of an object. Nothing was left behind." The same create with only the outer mapping succeeds, and
  the same nested mapping succeeds through `modify-business-process addMapping`. The agent gets no hint which mapping
  failed or why.
- **Silent or loud.** Loud, but uninformative (no parameter named, no operation index).
- **Basis / evidence.** measured 2026-10-02 on the stand, correlations `d43521be15cc` (create, both mappings: NRE),
  `57c67716b563` (create, outer only: success), `9ebc0ca62318` (modify, nested: success);
  [open-questions](eng-92719-file-processing-element-open-questions.md) C.7.
- **Neutraliser.** The ENG-95984 File process parameter type two-level binder must run on the create path as well
  as on modify, after the element's parameters are materialised; a package regression test builds the generic or the
  `fileProcessing` element and a nested mapping in ONE create request. Root-cause the NRE (likely the nested item
  parameters of a freshly added user task are not yet populated when `mappings[]` is applied) before PK-PT's code.

---

## K. Guidance

### T-66
**Academy wording differs from the product, and agents read Academy.**
- **What happens.** An agent that follows Academy text uses words the product and the builder do not use:

  | Academy | Product / builder | Consequence if copied |
  |---|---|---|
  | "Attachments and notes of the object", "Save to "Attachments and notes" of the object" | "Object attachments", "Save to object attachments" (`PS/CrtProcessDesigner/branches/7.8.0/Resources/BaseFileProcessingUserTaskPropertiesPage.ClientUnit/resource.en-US.xml:12, 15`) | the agent cannot match the user's screen |
  | "Files ... a single file or a collection" | two declared types, File and FileCollection, bound at different levels | one type is declared for both |
  | the Process parameters article lists no "File" type (7.17 to 10); "Binary" appears only in API pages | `FileLocator`, captioned "File" | an agent maps "file" to Binary (T-1, T-2) |
  | "Word always generates individual reports", while a figure shows the checkbox; 7.17 tied it to FastReport | the checkbox is forced on for Word | `separateReports: false` sent for Word (T-38) |
  | "File name" described once as a full pattern | a suffix after the caption | wrong expected file names (T-39) |
  | "Call Creatio.ai" | renamed "Sub-agent" in 8.3.4; its file slots are dynamic | guidance names the wrong element (T-67) |
  | 7.17.1: files "stored in the application database" only | File API, external storages supported | a false limitation repeated |
  | community: SysFile "always returns an empty collection"; a "best reply" uses the Process parameter source to read record attachments | the platform has a designer fix path; record attachments are the Object variant | wrong variant chosen (T-23) |
- **Silent or loud.** Silent.
- **Basis / evidence.** source (Academy pages read 2026-10-01, compared with the shipped resources and designer code;
  details in [platform-reference](eng-92719-file-processing-element-platform-reference.md) and
  [use-cases](eng-92719-file-processing-element-use-cases.md)).
- **Neutraliser.** D25: the `process-files` guide uses the product captions ("Process file", "Object attachments",
  "Generated report", "Process parameter", "Save to object attachments", "Use in process") and names the Academy
  wording once as a synonym; it never says "Binary"; it says which variant reads a record's attachments.

### T-67
**Consumers the builder cannot wire yet, and a collection used as a filter value.**
- **What happens.** Send email (`Attachments<N>` + `Attachments<N>FileLocator`) and the Creatio.ai call (`Files<N>` +
  `Files<N>FileLocator`) read files through DYNAMIC slots created in the process; the builder creates none today. Read,
  Add and Modify data never consume a file output by mapping (0 corpus cases), and "Id in CreatedObjectFileIds" is not
  a platform filter feature: a filter right-hand side is one value. The builder accepts a collection parameter as a
  filter value with no type check, which is predicted to fail at run time.
- **Silent or loud.** Silent at build.
- **Basis / evidence.** source: `PS/CrtCopilot/branches/7.8.0/Schemas/ExecuteIntentUserTask/ExecuteIntentUserTask.cs:91-123`;
  `PB/Filters/ProcessFilterService.cs:525-591`. measured (corpus): 4 Send email and 2 Creatio.ai file bindings, all in
  test packages, all both-level. Not measured: M19.
- **Neutraliser.** D22: the guide's "Consumers" section says what is buildable, that Send email attachments and
  Creatio.ai files are "not through this tool yet" (never "Creatio cannot"), and shows the product pattern (Modify data
  re-finds the created file by filter) and the per-file multi-instance route. Send email attachments belong to ENG-95985
  Send email attachments; a new Sub-task of FE refuses collection parameters as filter values.
