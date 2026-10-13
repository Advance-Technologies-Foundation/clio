# ADR: File process parameter type (ENG-95984)

**Status**: Accepted. Owner questions Q1-Q12 and Q19 were agreed on 2026-10-07 and 2026-10-08 (Q7 answered by stand
measurement M3 on 2026-10-02), and **Q20** (where D25's description bytes come from) on 2026-10-14 as A: trim existing
text per tool, reviewer-approved. One item is open: the test-plan item **O-TP1** decides where the e2e cases on the
Process-parameter variant's `Files` run (recommended B: in ENG-96506 Generated report + process parameter modes). It
changes no decision below.
**Date**: 2026-10-13
**Jira**: ENG-95984 File process parameter type (Task; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-95984-file-parameter-type.md](../prd/spec-eng-95984-file-parameter-type.md)
**Stories**: [1](../stories/story-eng-95984-file-parameter-type-1.md) · follow-ups
[2](../stories/story-eng-95984-file-parameter-type-2.md) · [3](../stories/story-eng-95984-file-parameter-type-3.md) ·
[4](../stories/story-eng-95984-file-parameter-type-4.md)
**Test plan**: [tp-eng-95984-file-parameter-type.md](../test-plans/tp-eng-95984-file-parameter-type.md)
**Analysis**: the full analysis (`spec/eng-95984-file-parameter-type/`, `spec/eng-92719-file-processing-element/`)
lands with the docs PR titled `ENG-92719 File processing element`; its decisions document argues D1-D9, D23, D25 and
D26 below in full, with their source anchors. This ADR condenses them and is self-contained for implementation.

Path aliases: `PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBT/` =
`tests/UnitTests/CrtProcessBuilder.Tests/`; clio and clio-knowledge paths are relative to their repository roots.

---

## Context

`create-business-process` / `modify-business-process` refuse every file type, so an agent cannot declare a process
parameter that carries files, and the Process file element (ENG-92719 File processing element) cannot take files from
a parameter. A file is a reference (FileLocator), and a set of files is a collection whose rows the runtime rebuilds
from the target's own items, so binding is the larger half of the work. Every option below is argued from clean-slate
cost and constraints; code that exists on some branch is not an argument for or against an option.

## Facts this ADR depends on

Platform and corpus (source unless marked):
- **F1** FileLocator `A33C9252-D401-453E-949D-169157067ED9` (CLR `IFileLocator`, designer caption "File") is the
  file-reference type. The platform NAME `"File"` resolves to `FileDataValueType` `BA40CFC5-F554-4c26-8F57-1BB29CF43C4E`
  ("File (BLOB)"); `GetInstanceByName` is exact and case-sensitive. Binary `B7342B7A` is a Stream type the flow-engine
  state store cannot hold (throws on one path, silently drops the value on the writer path). The designer's Add menu
  offers FileLocator and no BLOB. Corpus: 0 Binary or BLOB parameters; 50 FileLocator occurrences in 21 metadata
  files (measured 2026-10-01).
- **F2** There is no file-collection data type: a file collection is `CompositeObjectList` `651ec16f` with one nested
  FileLocator item. A plain `new DataValueTypeManager()` registers both types with no database, so unit tests need no
  stand.
- **F3** Rows of a target with item properties are rebuilt by the TARGET's item names; an unbound item reads its type
  default (null for a FileLocator) without an error; only a consumer that dereferences it fails. A collection with
  Source None and a bound nested item is read as ONE row. The designer binds the nested item and, when the source is
  itself a collection item, also the parent; shipped content binds both levels in 11 of 11 file-collection bindings,
  and 0 of 61 bound shipped collections with items bind the outer level only (measured, corpus).
- **F4** `ProcessMappingService.ApplyMapping` (`PB/Mappings/ProcessMappingService.cs`) is the one funnel for
  `mappings[]`, `addMapping`, the build, the `typeFromElement` mirror, `setConnections` (`EntityConnectionBinder`) and
  the approval applier. Every mapping is stored as a Script meta-path formula. Since CrtProcessBuilder 1.6.6.79
  (ENG-102114 Process builder: raw meta-path tokens (filter expression, modify flow conditions) get no shape check - a
  missing dot before [EntityColumn:...] fails only at runtime) the package checks every meta-path token of an
  `expression` against this process: a misspelled token, a token from another process and a column of a collection
  item are refused; GUIDs are stored lower-cased; nested-item tokens such as `Files.File` and `ObjectFiles.File` are
  accepted.

Stand measurements of 2026-10-02 (CrtProcessBuilder 1.6.6.54; the stand runs 1.6.6.96 since):
- **M3** refuted H-1 (the ENG-96230 Collection process parameter type mirror bound root-only would read null rows):
  3 iterations, all values set; the outer Script mapping copies the whole collection value, items included.
- **MI-0** an item-only `InputRecordCollection.Name <- RD1.ResultCompositeObjectList.Name` mapping ran ONE iteration
  over 3 contacts (the baseline for the post-cut proof).
- **M6** a flat File mapped from a collection item outside a row context read null while the source held two files.
- **T-68** a nested-item mapping in `mappings[]` of one create request threw a bare NullReferenceException, while the
  same mapping through `addMapping` succeeded; the route is unchanged on 1.6.6.96 (hypothesis, source).

Re-checked on 2026-10-13 (clio master `19e6f9c53`, package main 1.6.6.96, knowledge 1.15.142):
- **B1 Description budget measured full.** Under the unchanged ceiling 137 x 256 = 35,072 B
  (`clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs`), create-business-process is 35,068 B (**4 B
  left**), modify-business-process 35,062 B (**10 B left**), describe-business-process 34,019 B (1,053 B left),
  measured by lowering the ceiling on a scratch copy. The test comment forbids re-pinning the ceiling without review.
- **B2 The 19,000 B wire guard.** Since ENG-102797 O4c: make the short modify-business-process /
  create-business-process contract complete enough that agents stop asking for detail:full, the seven
  process-designer contracts requested together are held to `MaxInlineWireReplyBytes = 19_000` on the wire: 18,986 B
  measured, 14 B left. The short create and modify contracts always carry call shapes built from hand lists
  (`ToolContractShortForm.CallShapes`), never cut and held equal to the bundled archive by three drift tests
  (`clio.tests/Command/McpServer/ToolContractShortFormTests.cs`). This ticket adds no descriptor member (the types are
  values), so neither the call shapes nor the wire guard move (hypothesis, source).
- **B3 The configuration ledger is notice-only and not used here.** ENG-102537 Process elements built without their
  configuration block save green and fail or hang at run time (CrtProcessBuilder 1.6.6.91) added one element
  configuration ledger (`PB/Elements/IElementConfigurationLedger.cs`), notice-only by contract and reconciled AFTER the
  pre-save gate. It has no rule for the three file user-task schemas, so a file element built by this ticket's tests
  gets no ledger notice and the notice-list assertions stay exact (hypothesis, source). This ticket adds no ledger
  rule and none of its refusals can ride the ledger: they are raised inside `ApplyMapping` and the parameter service,
  before the gate. (ENG-96505 Element readiness and object attachments mode is the first to add a ledger rule.)
- **B4 Versions.** Package `main` and clio's bundle are at 1.6.6.96; the three process-designer floors are 1.6.6.92
  (since ENG-102111 create-business-process / modify-business-process: allow switching on process tracing
  (IsTracing)); 1.6.6.87, 1.6.6.89 and 1.6.6.94 are burned and no branch or tag holds a number above 1.6.6.96. The CI
  "Deliver to clio" stage came and went (2026-10-08/09); every bundle now moves by hand inside a feature clio PR.

---

## Decisions

### D1 - One file is `FileLocator`; friendly names; Binary stays refused

**Decision.** `file`, `filelocator` and `file locator` (case-insensitive) normalise to the canonical FileLocator name
before `GetInstanceByName` (`NormalizeParameterTypeName`, `PB/Parameters/ProcessParameterService.cs`), so `"File"`
can never resolve to BA40CFC5. `binary` and `blob` get their own refusal (F-T1): "Parameter type 'Binary' is not
supported: a process parameter carries a file by reference, not its bytes. Use type File (one file) or FileCollection
(several files)." The generic refusal lists `File, FileCollection`. The doc comment that names "binary / file" as
rejected is corrected.

**Rejected.** (a) Pass `File` through: one line, but it creates the BLOB type no designer page offers and the runtime
cannot hold. (b) Admit Binary: no storage path in the flow engine, never offered by the designer.

**Consequences.** The create and modify type lists name `File` and `FileCollection` (bytes in D25). Describe reads a
File back as `FileLocator`, which the alias accepts, so the round trip holds. The Binary refusal keeps the substrings
`not supported` and `Binary`, so the existing package pin and the e2e `ModifyBusinessProcess_Should_RejectUnsupportedParameterType`
stay green (only its `[Description]` becomes "refused for good"); the package probe
`AddProcessParameter_ShouldNameCollection_InUnsupportedTypeMessage` moves from `Binary` to `Image` or `Color`, because
the new Binary message names `FileCollection` and the probe would otherwise pass for the wrong reason. The package test
is the only automated guard: TeamCity does not run the `McpE2E.ProcessDesigner` category.

### D2 - Declaring a file collection: `type: FileCollection`, read back as `FileCollection`

**Decision (option A, agreed 2026-10-07).** `filecollection`, `file collection` and `collection of files` normalise to
`CompositeObjectList`, and `AddProcessParameter` adds exactly one item: `Name = Caption = "File"`, FileLocator,
Variable, no Tag, `ContainerUId = schema.UId` (so its meta path is `[Parameter:{item}]` with no element segment), a
fresh UId, unbound. Describe reports `type: "FileCollection"` for the **FileCollection predicate**: a PROCESS-level
`CompositeObjectList` with no Tag and exactly one item, of type FileLocator; `itemProperties` stays in the output.
Element parameters keep the platform name (nobody re-declares them). A designer-made collection whose item has another
name (`FileCollectionParameter`) is described as `FileCollection` with its real item name; a rebuild names the item
`File` (item bindings are by UId, so it works), and a replayed mapping that names the old item is refused loudly with
the item list (F-T7). A mirror of a file output stays refused, with a better message (F-T6): "declare
`type: FileCollection` and map it from `<el>.<param>`; both levels are bound by the mapping".

**Rejected.** (B) Relax the `typeFromElement` mirror for file outputs: cannot declare a callee input or a
script-filled list, and an older server keeps the old semantics silently. (C) Generic `itemProperties` on a declared
Collection: an older server silently drops the unknown member and saves a SHAPELESS collection green; about twice the
package cost and +100 B per description; it becomes follow-up story 2 ("Declared item shape for Collection process
parameters"), after which `FileCollection` is sugar for a Collection with one `File: File` item. Also rejected:
describing such a collection as `CompositeObjectList`, which rebuilds shapeless, silently.

**Consequences.** `referenceSchema` with File or FileCollection is refused (D7). `setParameter type: FileCollection`
on another shape is refused (D7). Two shipped statements gain a documented exception: "the platform type it maps to is
what describe reports back" (rationale of the knowledge pin `CollectionParameterGuidanceTests`) and "feed a described
collection back through `typeFromElement`, never by re-typing the shape" (`parameters.md`); the knowledge PR amends
both. The fixed item name `File` equals the consumer side's item names (`ObjectFiles.File`, `ReportFiles.File`,
`Files.File`), which makes D5's pairing trivial.

### D3 - Direction and Tag defaults

**Decision (agreed 2026-10-07).** File: Variable, no Tag, no reference schema. FileCollection: **Variable**, with its
item Variable and no Tag. The generic `Collection` keeps its Out default, so the stored type has two defaults. The
package comment that says the designer writes Out for collections is corrected: the designer's plain Add writes
Variable ("Create from element" writes Out plus Tags).

**Rejected.** Out for FileCollection (one default per stored type). Shipped Out collections are results handed to
the caller: a mapping inside the same process reads 3 of the 84 Out collections and 11 of the 30 Variable ones
(measured, recount 2026-10-07; 84 Out, 30 Variable, 12 In, 6 Internal). A Variable FileCollection is never refused: a
caller can fill it and the process can return it, and it is the shipped in-process file case.

**Consequences.** Guide sentence: "a FileCollection defaults to Variable; declare `direction: In` or `Out` only to
narrow it; a generic Collection still defaults to Out." Serialization pins: a Variable FileCollection equals the
shipped `FileParameterProcess.FileCollection` (under comparison row PT-b), an explicit Out one equals
`MarkProcessesToCancel.FilesCollection`.

### D4 - Dotted process-parameter paths

**Decision (option b).** Generalise `ProcessSchemaElementLocator.DescendItemProperties` over a root parameter
collection and an owner label; add `ResolveProcessParameterPath(schema, path)`, returning the parameter and its parent
chain with **no** `ContainerUId` backfill (a process-level item keeps `[Parameter:{item}]`); switch both
`ResolveProcessParameter` callers in `PB/Mappings/ProcessMappingService.cs`. A flat name is tried first, so a parameter
literally named `A.B` still resolves.

**Rejected.** (a) A second copy of the dotted grammar for process parameters (two grammars that drift). (c) Stay
flat-only and let agents write raw `expression` tokens (no type check, and none of D5's rules).

**Consequences.** `processParameter: "Docs.File"` and `targetProcessParameter: "Docs.File"` become legal, every target
stays type-checked, and a failing segment is named in the error. An older server answers "Process parameter
'Docs.File' was not found" (loud). Shipped content already binds process-level items this way
(`$FileCollection.FileCollectionParameter` in `FileParameterProcess`).

### D5 - The two-level collection binder

**Decision (option 2; owner choices agreed 2026-10-07).** Inside `ApplyMapping`, after the existing guards, for
**structured sources only** (`sourceElement` + `sourceElementParameter`, or `processParameter`): rules P1, P2, P2-MI,
P3, R-M1 and R-M2 below. P2 and R-M2 apply to **file-consuming targets only**: the Process-parameter variant's `Files`
(schema `6c620dd2-026e-560c-489f-030c5be5f2c3`) and a process-level collection that matches the FileCollection
predicate; later ENG-95985 Send email attachments adds its slots. P3 **resets** a stale parent with a notice. An
`expression` source goes to the named target as given: no auto-bind, no reset, no pairing, no refusal by these rules.
Only the package's meta-path check applies to it (F4): a misspelled token, a token from another process and a column
of a collection item are refused; GUIDs are stored lower-cased; nested-item tokens such as `Files.File` are accepted.
Every target is resolved and type-checked before the first value is written.

| Rule | Trigger | Writes | Refusal or notice |
|---|---|---|---|
| P1 | the target is an item of a collection AND the source is an item of a collection | the item, and the parent to the source's parent (recursively) | refuse (F-M1) when the parent is bound to a DIFFERENT collection; the same source is a no-op; applies to `InputRecordCollection.<F>` too |
| P2 | an outer collection mapping onto a file-consuming target that has a FileLocator item | each target FileLocator item to the paired source item: exact name (case-insensitive, type-checked), else the single-FileLocator pair | always a notice (F-M4); never overwrites an item already bound into the new source; rebinds a stale item and says so; an unpairable file item gets "at run time it will be empty" |
| P2-MI | an outer mapping onto a multi-instance `InputRecordCollection` with an unbound FileLocator item | nothing extra | notice (F-M4): the callee parameter will be empty on every iteration unless mapped |
| P3 | a plain (non-item) source onto an item of a single-instance owner | the item; the parent reset to None if it was bound to a collection | notice (F-M4) |
| R-M1 | a flat FileLocator target from a collection item | - | refuse (F-M2): use a FileCollection, or a multi-instance sub-process (M6: such a mapping reads null) |
| R-M2 | a file-consuming target collection from a collection with no FileLocator item | - | refuse (F-M3) |

An explicit API for later consumers:
`void BindCollection(ProcessSchema schema, ProcessMappingDescriptor outer, IReadOnlyList<(string TargetItem, string SourceItem)> itemPairs)`
on `IProcessMappingService` (type-agnostic mechanics; null pairs mean the P2 policy).

**Rejected.** (1) Explicit dotted mappings only: cheapest, but an outer-only mapping stays a saved-green crash and
agents must know the two-level rule. (3) Type-agnostic P2 (name pairing for every collection): it also changes what an
outer mapping writes for Read data -> multi-instance flows, which is outside this ticket and unmeasured. For P3,
designer parity (keep the stale parent) yields N copies of one file, one per source row, and there is no
`removeMapping` operation to clear it.

**Consequences.** Every `ApplyMapping` caller inherits the rules; the scalar-target callers (`setConnections`,
approvals) must stay green unchanged. P1 changes what an item-only multi-instance mapping writes: the parent
`InputRecordCollection` is bound too, so one iteration becomes N (a visible behaviour change; two shipped guide
passages that state "one iteration" are rewritten under a version gate, D25). P1 and P2 change what an existing
operation writes, so the floor moves (D26). The create path is covered too: T-68 is root-caused before the binder code
and pinned by a test that builds an element and a nested mapping in ONE create request. Regressions that must hold:
(1) a multi-instance sub-process whose callee has a Guid and a File parameter, iterated over `CreatedObjectFileIds`
or over Read data rows, is ACCEPTED with the P2-MI notice and no item written; (2) explicit outer plus per-item
mappings write exactly what 1.6.6.54 writes; (3) an `expression` onto `Files` and then onto `Files.File` writes both
as given; (4) build -> describe -> replay leaves both levels unchanged.

### D6 - The dotted-mirror bug

**Decision (option a).** `ResolveTypeSourceParameter` resolves FLAT only. When the flat lookup misses and the dotted
walk would succeed, the call is refused (F-T5): "'ObjectFiles.File' is an item of collection 'ObjectFiles' on 'OF1'.
A mirror copies a whole parameter: mirror 'ObjectFiles' for its shape, or declare type File / FileCollection." The
`setParameter` re-mirror path inherits it.

**Rejected.** (b) A type allow-list on every mirrored type now: it could refuse an accepted mirror unannounced, so it
waits for a scan of the output types shipped user tasks expose and becomes follow-up story 3 ("Allow-list the data
types a typeFromElement mirror may copy"). (c) Leave it: the dotted form silently creates an UNBOUND parameter of the
item's type (measured 2026-10-02: right type, no source).

**Consequences.** One refusal and one package test; a visible change (a dotted mirror source accepted before is now
refused).

### D7 - Constants, `referenceSchema`, delete guard, `setParameter` shape

**Decision.**

| Case | Today | Change |
|---|---|---|
| `value` on a File (addParameter, setParameter) | stored as a text constant | refuse (F-T3): "a File has no constant form; map it from an element's file output or another File parameter"; `EnsureNotCollectionConstant` generalises to "no constant form" |
| `addMapping value` onto a FileLocator or collection target | accepted (the platform's pre-save rule skips constant sources) | refuse in `ValidateConstantValue` (`PB/Parameters/ProcessParameterValueValidator.cs`), the one place every constant route passes |
| `referenceSchema` on File or FileCollection | File silently becomes a Lookup | refuse (F-T2) |
| `removeParameter` of a collection whose ITEM is referenced | missed: only the root UId is tested | test the root and every item UId, recursively |
| `setParameter type: FileCollection` on another shape | silent no-op (same stored type) | refuse (F-T4): "FileCollection would change this collection's shape; remove it and add it again" |
| `setParameter direction` on File or FileCollection | allowed | unchanged (the remedy in D3) |
| `expression` into a FileLocator or collection target | stored after the meta-path check | unchanged, outside D5 |

**Rejected.** Keeping today's behaviour: each row is a silent wrong write (a text constant on a file, a Lookup instead
of a File, an item reference left dangling by a delete).

**Consequences.** Type compatibility needs no change: FileLocator and collections fall through to an exact type match
(`PB/Mappings/ParameterTypeCompatibility.cs`; item shapes are not compared there, which is why R-M2 exists).

### D8 - Describe of file parameters, nested-only bindings and single-token element sources

**Decision.** No new per-parameter field: File reads back as `FileLocator`, FileCollection as `FileCollection` plus
`itemProperties` (D2). `ProcessDescriber` also lists a non-output element parameter when ANY nested item carries a
value stamped with this schema (today only a ROOT value counts, so a nested-only `Files.File` binding is invisible).
When a stored Script is exactly one element-parameter token, describe also reports `sourceElement` and
`sourceElementParameter` (dotted for a nested item, such as `ObjectFiles.File`, named through
`ProcessSchemaElementLocator.TryNameNestedParameter`), with `sourceColumn` null, and only when the write side, given
them back, would store this very value; `value` stays.

**Rejected.** New per-parameter fields: clio's `DescribedParameter` has no overflow bag, so an undeclared field is
dropped (the members used here are already declared, so clio needs no DTO change). Formula-only output: a replayed
element binding would be an `expression` and bypass D5's checks. Decoding single-token PROCESS-parameter sources needs a
new member on the bagless clio DTO, so it becomes follow-up story 4 ("Describe: decode process-parameter sources into
re-appliable names"); until then those bindings replay as expressions, accepted within the same process.

**Consequences.** Describe output changes for every element-to-element mapping of every process (additive: a member
that was null now carries a name; a visible change). The describe tool's decoded-source clause covers "a value that IS
another element's output" (about +40 B of 1,053 B left). Three `DescribedParameter` doc comments in
`clio/Command/ProcessModel/IProcessDescriber.cs` become false and are rewritten (`Type`, `SourceElement`,
`ItemProperties`).

### D9 - The collection mirror binds the outer level only (H-1, refuted)

**Decision.** M3 refuted H-1 on 2026-10-02, so no Sub-task exists (contingency X4). The mirror
(`BindMirroredCollection`) still binds every item to the source item of the same name through `BindCollection` with
the clone pairs, as one parity commit in this ticket's package PR (the shipped shape: 60 of 61 bound collections with
items bind their items), and the code comment that predicted null rows is narrowed.

**Rejected.** A separate Sub-task fixed first (taken only had M3 confirmed H-1). Doing nothing (the builder keeps a
shape the designer never writes).

**Consequences.** `AddProcessParameter_ShouldBindMirroredCollection_ToSourceOutput` is extended and its description
corrected; the guide sentence that pins the mirror behaviour moves in this ticket's knowledge PR; visible behaviour
change (5).

### D23 - Jira dependencies

**Decision (agreed 2026-10-07).** ENG-95984 File process parameter type **blocks** ENG-96506 Generated report +
process parameter modes (its Process-parameter variant consumes File and FileCollection, dotted process-parameter
paths, `BindCollection` and the describe decode) and **relates to** ENG-96505 Element readiness and object
attachments mode, replacing today's "blocks" link to ENG-96505. ENG-95984 and ENG-96505 both block ENG-95985 Send
email attachments. The delivery ORDER stays ENG-95984, then ENG-96505, then ENG-96506.

**Rejected.** Keeping "ENG-95984 blocks ENG-96505": the Object attachments variant needs no File process parameter
(its outputs are element parameters that dotted element paths already address), so the link would claim a functional
dependency that does not exist.

**Consequences.** ENG-96505 Element readiness and object attachments mode can be developed in parallel as a stacked
draft, but merges after this ticket, for four reasons: this clio PR ships the `ManagerMap` arm
`fileprocessing` / `processfile` (an older clio meeting the token gives a hard `validate-process-graph` Error); its
clio PR is the first to write `name=process-files`, which must resolve to a PUBLISHED guide born here; its per-file
multi-instance e2e needs P1 and a callee File parameter; and both package PRs edit the same files under one cut order.
Since 2026-10-13 the PR that lands first also pays the description bytes (Q20).

### D25 - Guidance placement and tool-description budget

**Decision.** One new guide, **`process-files`** (name agreed 2026-10-08), born in this ticket's knowledge PR, without
the "Part of the process guide set" banner, registered banner-less the way `process-tracing` was, and NOT added to
`ProcessGuideSet.GoLiveFloor` (that set must be a subset of the banner set, so a banner-less entry there fails
`GuidanceMigrationTests`). Full registration: `bundle-source.json` entry, `requirements.itemIds`, `resourceUris`, the
migration list, a routing row shipped in the same generation as the article, a pin test. This ticket's content: the two
types and FileLocator; the FileCollection read-back and the item-name caveat; the direction rule; P1, P2, P2-MI, P3,
R-M1, R-M2 and the file-consuming targets; "an `expression` goes through as given; only its meta-path tokens are
checked" (a misspelled token, a token from another process and a column of a collection item are refused; nested-item
tokens such as `Files.File` are accepted); the refusals; the gate sentence "use only when get-tool-contract for
create-business-process mentions type File" with a concrete "from clio <version>" line written with the real number
at merge. Whole-guide size target about
21,000 characters (ENG-96505 and ENG-96506 add their sections later). Existing articles: the `parameters` type list
(+122 characters) and the FileCollection exception (+110), which fit the 9,656 characters left; `sub-process` and
`sub-process-when` multi-instance passages rewritten with the version gate ("from clio <version>, a per-item value taken
from an item of a collection binds that collection too; a per-item value from anything else still needs the explicit
`InputRecordCollection` mapping; two different collections are refused"), keeping the "send both" advice for older
servers. Every read-back the new text teaches says `describe-business-process mode=full` (or `element=<name>`), with
the gate "on clio older than 8.1.0.141, omit `mode`" that `process-modeling.md` owns (`ClioToolSyncGuidanceTests`
fails a plain read-back describe); every `install-process-builder` mention carries consent wording within 80
characters (`ProcessBuilderInstallConsentGuidanceTests`); no Jira key, issue or PR link (`InternalTaskReferenceTests`);
a CrtProcessBuilder version appears only as inline evidence.

Tool descriptions: the create and modify type lists only (+30 B / +28 B); NO `name=process-files` pointer yet (the
routing row reaches agents; the first pointer is ENG-96505's); the describe clause (+40 B, fits). Because of B1 a byte
swap is needed, and **where the bytes come from was owner question Q20, agreed 2026-10-14: A**: trim existing text per
tool, each trim reviewer-approved, measured by lowering the ceiling (candidate on create, still measured at cut: drop
the member list `{enabled, executionMode, ignoreErrors}` from the floor clause, about 39 B; on modify, a trim chosen at
cut with the reviewer). Rejected: B, a reviewed ceiling raise; C, move the type lists into guidance. Any swap leaves
`this clio requires <floor>` in all three descriptions and the `stopped validating formulas` clause with no floor
literal in the 60 characters before it (`EnforcedFloorSentences_ShouldEqualTheRequiresPackageLiteral`,
`FloorSentences_ShouldNotCreditTheEnforcedFloorWithTheCollapse`). MCP prompts need no update: they list no parameter
types.

**Rejected.** `process-file` (reads as the element only); one guide per ticket (two routing rows, two registrations, a
split topic); the banner (process-modeling, which indexes bannered guides, has 8 bytes left under the 19,000-byte
inline rule); registering in `GoLiveFloor` (fails the migration test); a `name=` pointer in this ticket's descriptions
(costs bytes the budget does not have and must resolve to a published guide).

**Consequences.** Merge order package -> clio -> knowledge; the generation that births `process-files` must be
published (merged, released, and visible in `info-knowledge`) before the clio PR of ENG-96505 merges; a failed
`update-knowledge` keeps serving the old guidance silently. Q20 concerned the clio description edit only; with A
agreed, the trim is work inside the clio PR.

### D26 - `[RequiresPackage]` floors and version numbers

**Decision.** Numbers are rules, not projections. The cut takes the first free number ABOVE the highest number claimed
anywhere at cut time (main, any branch or tag of either repository, a clio bundle): 1.6.6.97 or higher today (B4),
re-checked with `git ls-remote --tags` in both repositories. The number is claimed BEFORE cutting, in the PR
description AND in the one shared cut claim, a comment on ENG-92719 File processing element; the shared claim guards
against the double stamp measured in git history, when two package PRs in review at the same time stamped the same
number. Cut with
`pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <package at PR head> -Version <claimed>`; restamp last; tag
`crtprocessbuilder-<version>` verified on the remote. The three floors (create-business-process,
modify-business-process, modify-process-as-new-version; 1.6.6.92 today) move to the **final** cut of this clio PR,
equal to `ExpectedArchiveVersion` when it merges. describe-business-process, list-user-tasks and
validate-process-graph stay presence-only.

**Rejected.** No floor: an older server answers "not supported" for `File` (loud) but silently keeps the dotted-mirror
bug and the outer-only behaviour, so a newer clio would advertise rules the server does not apply. Floor = the first
cut: a foreign archive under our first number would satisfy it while lacking the feature.

**Consequences.** Files that move with the raise: the three literals and their comment blocks,
`clio.tests/Command/ProcessDesignerRequiresPackageAttributeTests.cs`, the enforced-floor sentence in three
descriptions, `docs/McpCapabilityMap.md` floor rows, the new e2e fixture's `MinimumPackageVersion`; the floor-history
parenthetical of `ModifyProcessAsNewVersionTool.cs` is rewritten once without its enumeration. A cut at 1.6.6.100 or
above grows the floor literal by one character in each of three descriptions. No new WCF operation, so
`ExpectedOperationContractCount` and `ExpectedAuthorizationGateCallSites` do not move. Every reinstall of the stand is
an `install-process-builder` call that needs the user's explicit approval each time and restarts the site for every
connected user. If ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema and wire the R1-R17
graph validator into the write path (ENG-88414) lands first, this package PR teaches its key allow-list the new
aliases; if this PR lands first, that branch adds them on rebase.

---

## Refusal and notice catalogue

Server-side texts; none costs tool-description bytes. Tests assert each entry's key words (the names it must mention),
not the full sentence, so wording can be tuned in review.

| ID | Kind | Trigger | Decision |
|---|---|---|---|
| F-T1 | refuse | `type: Binary` or `blob` | D1 |
| F-T2 | refuse | `referenceSchema` with File or FileCollection | D2, D7 |
| F-T3 | refuse | a constant on a File, or `addMapping value` onto a FileLocator or collection | D7 |
| F-T4 | refuse | `setParameter type: FileCollection` on another shape | D7 |
| F-T5 | refuse | dotted mirror source | D6 |
| F-T6 | refuse | mirror of a file output (the message points to FileCollection) | D2 |
| F-T7 | refuse | a replayed mapping naming a FileCollection item the rebuild named `File` | D2 |
| F-M1 | refuse | P1: the parent is bound to another collection | D5 |
| F-M2 | refuse | R-M1: flat FileLocator <- a collection item | D5 |
| F-M3 | refuse | R-M2: a collection without a file item into a file-consuming collection | D5 |
| F-M4 | notice | P2 items bound, unpaired or stale; P2-MI empty callee file item; P3 parent reset | D5 |

## Implementation outline

One story ([story 1](../stories/story-eng-95984-file-parameter-type-1.md)) carries the work packages: PB-1..PB-10 in
crt-process-builder, as ten commits, each mapped to its decision in the PR description: (1) D1 aliases and the
Binary / BLOB refusal; (2) D2 `FileCollection` write alias and describe predicate, with D3's defaults; (3) D4
`ResolveProcessParameterPath`, both callers switched, behaviour-neutral; (4) D5 binder rules and the whole
`BindCollection` API; (5) D6 dotted-mirror refusal; (6) D7 constants, `referenceSchema`, delete guard, `setParameter`
shape; (7) D8 nested-only listing and the element-source decode; (8) D9 parity, the mirror binds its items; (9)
`docs/file-parameter-capture.md`; (10) the restamp, last. Then CL-1..CL-7 in clio (rebundle and floor, descriptions, `ManagerMap` arm, capability map, unit tests and DTO doc
comments, the e2e fixture, knowledge records and status flips) and KB-1..KB-3 in clio-knowledge. One feature PR per
repository, titled `ENG-95984 File process parameter type`, merged by a human in the order package (tag verified on the
remote), clio, knowledge. This BMAD set is the docs-only clio PR that merges before the package PR opens.

## Owner decisions

| Item | State |
|---|---|
| Q1 / O-7 / D24 Replace A1-A4 with AC-1..AC-10 | agreed 2026-10-07 |
| Q2 / O-8 / D23 Jira links | agreed 2026-10-07 |
| Q6 PR split (one feature PR per repository, plus this docs-only PR) | agreed 2026-10-07 |
| Q7 / O-5 / D9 Where H-1 is fixed | answered by M3 (2026-10-02): no defect, parity commit only |
| Q8 / O-10 This docs-only BMAD set merged before the package PR | agreed 2026-10-07 |
| Q9 Follow-up Sub-tasks created on day 0 | agreed 2026-10-07 |
| Q10 / O-1 / D2 FileCollection as a type alias | agreed 2026-10-07 |
| Q11 / O-2 / D3 FileCollection defaults to Variable | agreed 2026-10-07 |
| Q12 / O-3, O-4 / D5 P3 resets with a notice; P2 and R-M2 on file-consuming targets only | agreed 2026-10-07 |
| Q19 / O-6 / D25 Guide name `process-files` | agreed 2026-10-08 |
| Q20 / D25 Where the description bytes come from | agreed 2026-10-14: A (trim existing text per tool, reviewer-approved; no ceiling raise, type lists stay in the descriptions); CL-2 proceeds with it |
| **O-TP1 (test plan) Where the e2e cases on the Process-parameter variant's `Files` run** | **open** (recommended B: in the clio PR of ENG-96506 Generated report + process parameter modes); moves only the cross-ticket cases XT-14a..d |
