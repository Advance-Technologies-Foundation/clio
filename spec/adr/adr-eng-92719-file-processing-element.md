# ADR: Process file element (ENG-92719)

**Status**: Accepted in part, 2026-10-14. **Agreed**: Q1-Q12 on 2026-10-07 (Q7 answered by stand measurement M3 on
2026-10-02), Q19 on 2026-10-08, Q13-Q18 on 2026-10-14 (each as recommended) and Q20 on 2026-10-14 (A: trim existing text per
tool, reviewer-approved). For this ADR they settle D15's SysFile half (Q3: a separate Sub-task SF), the slot order RP then SF (Q4), D22 (Q5), D23 (Q2), D24
(Q1), D25's guide name (Q19) and description bytes (Q20), D27 (Q6), and the delivery items O1 (Q3), O3 (Q4) and
O4-O8 (Q8), among them **O5: the variant registry is OA's design**, and D10 (Q13, token and discriminator), D11
(Q14, naming bundle), D14 (Q15, action inference), D16 (Q16, record scope and empty-filter policy, answered knowing
the precedent of ENG-102537 Process elements built without their configuration block save green and fail or hang at
run time), D15's storage details (Q17) and D17 (Q18, FastReport). The acceptance rows the PRD marks with those
questions (FE-1, OA-1, OA-4, OA-6, OA-7, OA-9, RP-1, RP-4) stand as written. Test-plan item O-TP1 (where TC-91 runs) is open too, recommended B: in RP's clio PR.
**Date**: 2026-10-14
**Jira**: ENG-92719 File processing element (Story; epic ENG-92704 Create BP via AI Toolkit); ENG-96505 Element
readiness and object attachments mode (OA; a Task that relates to ENG-92719, converted from a Sub-task on 2026-10-14);
Sub-tasks ENG-96506 Generated report + process parameter modes (RP) and SF,
"SysFile attachment storage in the Process file element" (not created in Jira as of 2026-10-14)
**PRD**: [prd-eng-92719-file-processing-element.md](../prd/prd-eng-92719-file-processing-element.md)
**Stories**: [1](../stories/story-eng-92719-file-processing-element-1.md) (OA) ·
[2](../stories/story-eng-92719-file-processing-element-2.md) (RP) ·
[3](../stories/story-eng-92719-file-processing-element-3.md) (SF) ·
[4](../stories/story-eng-92719-file-processing-element-4.md) ·
[5](../stories/story-eng-92719-file-processing-element-5.md) (side Sub-tasks)
**Test plan**: [tp-eng-92719-file-processing-element.md](../test-plans/tp-eng-92719-file-processing-element.md)
**Analysis**: [decisions](../eng-92719-file-processing-element/eng-92719-file-processing-element-decisions.md) argues
D10-D29 in full, with their options and source anchors; this ADR condenses them. D30 is a documentation rule (the
documents name no current or future package cut number) and has no entry here. D1-D9 (the File and FileCollection
parameter types and the two-level binder) belong to ENG-95984 File process parameter type and its own ADR; this ADR
relies on them where it says so.

Path aliases: `PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PBA` =
`packages/CrtProcessBuilder/Files/src/CrtProcessBuilderApp.cs`; `PBT/` = `tests/UnitTests/CrtProcessBuilder.Tests/`;
clio and clio-knowledge paths are relative to their repository roots. Line anchors are those of the analysis (package
`main` `a6bfcbf6`, clio `19e6f9c53`), re-read where marked.

---

## Context

The Process file element cannot be configured through the process builder. The generic `userTask` route draws it and
configures nothing; the element saves green and throws at run time. The designer presents one element over three
user-task schemas and replaces the element on a source change, and the platform validates almost nothing, so the
element needs its own contract, its own refusals and a describe block that round-trips. Every option below is argued
from clean-slate cost and constraints; code that exists on some branch is not an argument for or against an option.

## Facts this ADR depends on

Platform and corpus (source unless marked; Creatio core 10.1.37 and the CrtProcessDesigner 7.8.0 package):
- **F1 Three schemas.** ObjectFileProcessingUserTask `9387c794-8d84-5925-ab77-c47e7d876286` (13 parameters),
  ProcessFileProcessingUserTask `6c620dd2-026e-560c-489f-030c5be5f2c3` (8), ReportFileProcessingUserTask
  `c2bf0416-54c6-6c56-58e0-41162c7795f0` (13). "What is the source of the file?" is not a parameter; the palette offers
  only the Object schema, and a source change replaces the element (new name, every value dropped), refused by the
  designer while another element references it (measured in the designer, 2026-10-01).
- **F2 `ResultActionType`.** SaveToFiles = 0, UseInProcess = 1, no metadata default: an unset value runs as 0. The
  designer defaults the Object and Report pages to 1. The Process variant supports only 0 (1 throws
  `NotSupportedException`). Object after 0: `ObjectFiles` holds the SOURCE locators and the copies are only in
  `CreatedObjectFileIds` (pending M26). Report after 1: temporary `SysProcessFile` rows deleted when the owning process
  completes, `CreatedObjectFileIds` empty (pending M16).
- **F3 Runtime roots.** The Object variant's filter root is `SourceEntitySchemaUId` (the legacy `<X>File` or SysFile);
  the stored `rootSchemaName` is ignored and every read adds `Type = File`. The Report root is the printable's entity
  (`SysModuleReport.SysEntitySchema`, else the section entity).
- **F4 Storage per object.** The designer keeps the legacy encoding for an object with an eligible `<X>File`
  (`FileLead` for Lead) and chooses SysFile `70ec5d9f-a55e-4f5c-8f59-30d2c5149c4a` for every other eligible object.
  Measured in the designer, in memory (UO-2..UO-4, 2026-10-01): the per-object choice, and for a SysFile source the
  file object SysFile with `SourceDataEntitySchemaUId` = the record object (UO-3). Source, not measured: the
  `RecordId` link column and scope, and a SysFile target's `TargetDataEntitySchemaUId` (pending M7, M21, M23). The
  server never reads the designer flag `UseSysFileInObjectFileProcessing`. 0 of 30 shipped elements use SysFile
  (corpus). `CopyAttributes` copies only what the target has not set, so a same-schema copy with no link column lands
  on the source record.
- **F5 Reports** (source, not measured, unless marked). Dispatch by `Type.Name`: MS Word runs; FastReport is
  dispatched, but its process path is unverified (it resolves the generator without the process's user connection);
  DevExpress throws `NotSupportedException`. Word always writes one file per record (`IsSeparateReports` changes naming
  only). Measured (2026-10-01 and M15): the stand has 5 MS Word printables, none for Account or Contact, `ConvertInPDF`
  false on all, and no FastReport package or printable.
- **F6 The Process variant's input.** `Files` is a required In collection with one FileLocator item `File`; its output
  item is `ObjectFile` (the copies). A nested-only `Files.File` from a single file is read as one row (pending M1); an
  outer-only binding gives rows `{File: null}`.
- **F7 Nothing is validated.** No variant overrides `Validate`; `IsRequired` is never enforced.

Package (source; CrtProcessBuilder 1.6.6.96, re-read 2026-10-13):
- **P1** The generic route builds the same unconfigured element for the three file schemas.
  `UserTaskElementHandler.Create` already refuses the generic Pre-configured page (ENG-102112 create-business-process
  silently accepts a generic userTask PreconfiguredPageUserTask without the preconfiguredPage block,
  `PB/Elements/UserTaskElementHandler.cs:86`) and Auto-generated page / User dialog (ENG-102537, `:87`), keyed on the
  resolved schema UId.
- **P2** `DataNodeFilterTarget` claims any node with `DataSourceFilters` and roots the filter on whatever object the
  caller names, unchecked (`PB/Filters/DataNodeFilterTarget.cs:57-91`).
- **P3** The element-configuration ledger of ENG-102537 (`IElementConfigurationLedger`) evaluates every touched
  element at the end of the request, raises NOTICES and never refusals (owner decision,
  `PB/Elements/IElementConfigurationLedger.cs:22-27`), reconciles AFTER the pre-save gate, and has no rule for the
  three file schemas. The one end-of-request refusal before the gate is `EnsureFormulasStillBelongOnTouchedConnectors`
  (`PB/Design/ProcessEditPipeline.cs:130-133`).
- **P4** Since ENG-100041 Sub-process retarget guard scans the live schema instead of the request dependency snapshot
  (1.6.6.96) every refusing dependency guard asks the request's `IProcessDependencySnapshot`, which throws before
  `Capture` runs.
- **P5** The readData sort helpers (`ResolveOrderInfoValue`, `ParseSortDirection`, `DescribeSort`,
  `DescribeSortDirection`) are `private static` and worded "Read data '<label>': ..."
  (`PB/Elements/ReadDataConfigBinder.cs:793-825, 917-962`).
- **P6** `UserTaskSchemaIdentity` resolves a task schema by NAME (`FindInstanceByName`); production code cannot
  reference `Terrasoft.Configuration`.
- **P7** Raw `addMapping` writes a correct schema UId onto the storage parameters since 1.6.6.69 (ENG-102113
  modify-business-process addMapping: Add data object (EntitySchemaId) is validated against the record Id instead of
  the schema UId), but it bypasses every block check and still accepts a `SysSchema` row Id (trap T-54).
- **P8** A block riding a known element type is dropped silently by a server that does not know it (no
  `IExtensibleDataObject`; measured as M20 on 1.6.6.54, 2026-10-02; still so on 1.6.6.96, source).

clio (source; master `79fab4786`, 2026-10-14):
- **K1** `ManagerMap.ResolveDataId` has no `fileprocessing` arm; an older clio meeting the token raises a hard
  `validate-process-graph` Error. ENG-95984's clio PR (https://github.com/Advance-Technologies-Foundation/clio/pull/1814)
  adds `"fileprocessing" or "processfile"`.
- **K2** Since ENG-103085 Refuse element and block keys the process builder would drop silently, clio refuses an
  element or block key it does not know before sending (`clio/Command/ProcessModel/ProcessBlockKeyCheck.cs`); its key
  sets mirror the bundled archive's request contracts, and `KeySets_Should_EqualTheMembersOfTheBundledServerContracts`
  holds them equal.
- **K3** Description budget under the 35,072 B per-contract ceiling: create 35,061 B (11 B left) and modify 35,070 B
  (2 B left), as re-measured on `79fab4786` by ENG-103081 Warn when a process-parameter mapping is bound twice or to a
  branch some start skips; describe 1,053 B left (measured on `19e6f9c53`). The seven process-designer contracts
  together: 18,986 B of the 19,000 B wire guard (14 B left, `19e6f9c53`). The short create and modify contracts carry
  call shapes that are never cut and are held equal to the bundled archive by three drift tests (ENG-102797 O4c: make
  the short modify-business-process / create-business-process contract complete enough that agents stop asking for
  detail:full).
- **K4** `DescribedElement` keeps an undeclared block in its `[JsonExtensionData]` bag; the filter DTOs have no bag.
- **K5** Since ENG-103023 clio: spill-free reads and post-write summaries (describe-business-process modes, write tools
  return the result, get-page lean by default), describe returns a summary above 18,000 B unless `mode=full` or
  `element=<name>` is passed.

Stand measurements of 2026-10-02 (CrtProcessBuilder 1.6.6.54): **M10** the designer's object list matches the resolver's
prediction; **M11 (a)(b)** H-G3-1 not reproduced in memory; **M13** describe of PrintContractsReport gives `Guid` with
`referenceSchema: Contract`; **M14** the server-built element keeps the template's nested UIds, stores `IL2` on every
element parameter, has no `BO2`, and `ConsiderTimeInFilter` "false"; **M15** no FastReport; **M17-Q7** foreign keys
`ContactFile.ContactId` and `AccountFile.AccountId`, none on `SysFile.RecordId`; **M19** a collection parameter as a
filter value is accepted at build and fails at run with `ArgumentException`; **M20** an unknown block is dropped
silently; **M24** a wrong JSON shape is nulled by the WCF binder and refused later with a misleading text. Open: M1, M2,
M4, M7, M8, M9, M12, M16, M17 runs, M18, M21, M22, M23, M25, M26.

---

## Decisions

### D10 - One element type `fileProcessing`; the variant is the group present (agreed 2026-10-14, Q13)

**Decision.** One token `ElementTypes.FileProcessing = "fileprocessing"` (wire spelling
`fileProcessing`) with a second `SupportedTypes` token `"processfile"`; build and describe emit only the first.
Discriminator form (ii): on create exactly one of `attachments` (Object schema), `report` (Report schema) or `files`
(Process schema) must be present; none or two are refused, naming them. On update the stored schema UId fixes the
variant, and a group of another variant is a source change (D12). `source` on write is an optional identity check
(`attachments` / `report` / `files`): equal is accepted so that describe output resubmits verbatim, different is
refused, and an entity-like value ("Account") gets a targeted refusal pointing to `attachments.object`. A `userTaskName`
that resolves to the implied schema is ignored; one that names another schema is refused. The block is gated strictly
(`EnsureBlockMatchesHandler`). `FileProcessingElementHandler` is registered BEFORE `UserTaskElementHandler` and claims
elements through the variant registry (O5).

**Rejected.** (b) Three tokens, one per schema: three type-list entries on a full description budget, and a source
change becomes a type change the agent must know. (c) The generic route plus a block: an older server drops the block
silently (P8), and the generic route could no longer be refused (D13). Discriminator (i), a required `source` enum:
`source` already means an object name in four data blocks and collides with `files.processParameter`. (iii) A
`fileSource` key: still redundant with the group. `processFile` as the primary token: equal cost, but it can be
mistaken for the Process-parameter variant.

**Consequences.** `fileProcessing` joins `WritesAConfigurationBlock` and the `setElement` field list. The clio
`ManagerMap` arm ships in ENG-95984's clio PR, one release before any package emits the token (K1). Visible change per
cut: existing Object elements describe as `buildType: fileprocessing` with OA, Report and Process-parameter elements
(the four shipped product report processes among them) with RP; `userTaskName` stays in describe. Tests: the
handler-order tripwire; `processFile` builds the same element; no group, two groups, a contradicting `userTaskName` and
`source: "Account"` refused.

### O5 - The variant registry (agreed 2026-10-07)

**Decision.** One collection of variants keyed by schema UId, `IFileProcessingVariant` in a
`FileProcessingVariantRegistry`. Five consumers read it: the handler's `CanBuild` / `CanDescribe`, the generic-route
refusal (D13), the raw-`addMapping` refusal (D13), the `FileProcessingFilterTarget` claim (D16) and the describe block
(D19); the `ProcessFilterApplier` message and the per-schema notice rule (D16) change in the same cut. Each cut ADDS a
registration: OA registers the Object variant, RP the Report and Process variants, SF none (it lifts a SysFile switch
per variant).
Production code resolves each variant's schema UId by NAME through `UserTaskSchemaIdentity` (`FindInstanceByName`), with
the three schema names mirrored next to `ProcessDesignConstants.UserTasks`; the literal UIds live only in the test
fixtures and the drift guard (reuse correction RF1). A registry test asserts "a registered variant gets all five claims,
an unregistered one none".

**Rejected.** A checklist of five shared files edited per cut: one missed edit leaves a claimed variant with an
unconfigured generic route, or a refused route with no replacement. An "element readiness" scaffold PR that registers
the family with no consumer: an L-sized API reviewed without a use, two PRs under one title (D27).

**Consequences.** "Claim, refusal, filter target and describe of a variant land together" becomes a structure (pr-split
T3). Injecting the registry into `UserTaskElementHandler` changes its constructor (its registration plus 39 direct
constructions in 16 PBT files); OA either prices that in or builds the registry from the connection, as the schema
identities are built (hypothesis about cost).

### D11 - The `fileProcessing` block contract (agreed 2026-10-14, Q14)

**Decision.** One group per variant plus shared `action` and `saveTo`; the element-level `filter` stays
the unchanged `FilterDescriptor`; the record scope lives in the group (D16).

```jsonc
"fileProcessing": {
  "source": "attachments" | "report" | "files",   // optional identity check (D10)
  "action": "useInProcess" | "saveToAttachments", // optional; inferred and always written on create (D14)
  "attachments": { "object": "Account",           // the record object (its file object also accepted); storage derived (D15)
                   "recordId": { /* one value source */ },          // WHICH record (D16)
                   "numberOfRecords": 50,                           // default 50, 1..5000; omitted does NOT read all
                   "sort": { "column": "CreatedOn", "direction": "desc" } },   // readData shape, a FILE-object column
  "report": { "printable": "<templateId>",        // list-printables' templateId, a [#Lookup...#] macro or a caption (D17)
              "recordId": { /* one value source */ }, "separateReports": true,
              "fileNameSuffix": { /* one value source */ }, "fileNameSuffixColumn": "Number" },
  "files": { "processParameter": "Docs" },        // exactly one source (D18)
  "saveTo": { "object": "Contact", "recordId": { /* one value source */ }, "linkColumn": "Contact" }
},
"filter": { }                                     // attachments and report variants only (D16)
```

- The four value-source members (`attachments.recordId`, `report.recordId`, `report.fileNameSuffix`,
  `saveTo.recordId`) use ONE shared contract, `FileProcessingValueSourceDescriptor`, with the
  `OpenEditPageRecordDescriptor` member names (`value`, `processParameter`, `sourceElement` + `sourceElementParameter`
  (+ `sourceColumn`), `expression`) and "exactly one source". Its `expression` runs the same length bound and
  `MetaPathTokenReference` check that `openEditPage.recordId.expression` runs.
- A group that does not match the variant is refused. `sort` is resolved by the readData sort codec extracted from
  `ReadDataConfigBinder` with the element label injected (D16, D21).
- Read-only members come in two kinds. **Identity checks** are on the write contract and accepted only when equal to
  the derived value: `source`, `attachments.storage`, `attachments.fileObject`, `attachments.linkColumn` (refusal names
  `saveTo.linkColumn` as the only writable one), `saveTo.storage`, `saveTo.fileObject`, `report.object`,
  `report.type`. **Describe-only** members are NOT on the write contract: `actionStored`, `bindingLevels`, `outputs`,
  `issues`, `report.caption`, `report.convertToPdf`, every display value; the guide says "remove the describe-only
  members before resubmitting".
- Storage and identity parameters (`SourceEntitySchemaUId`, `SourceDataEntitySchemaUId`, `TargetEntitySchemaUId`,
  `TargetDataEntitySchemaUId`, `ConnectedObjectColumnUId`, `ReportId`) are written directly by the binder from the
  resolved schema; value inputs (`ConnectedObjectId`, `ReportName`, `Files`, `Files.File`) go through `ApplyMapping`.

**Rejected.** Flat keys at block level: one key would mean different things per variant. A report identity object
`{id | name}`: one string accepting an id, a macro or a caption follows the `EmailTemplateResolver` precedent. A
record scope inside `filter` (D16). Accepting describe-only members "when equal": captions in the reader's culture and
environment settings differ legitimately, so correct resubmissions would be refused.

**Consequences.** New contracts `FileProcessingDescriptor` (its groups and the value-source type) on
`ProcessElementDescriptor` and `ProcessElementUpdateDescriptor`; `FilterDescriptor` does not change, so the other
filter targets and clio's bagless filter DTOs are untouched. In clio the block needs a `fileProcessing` key set in the
client-side key check (K2) and a `fileProcessing` entry in the short create contract's call shape (K3), both in the
commit that carries the rebundle. If ENG-95244 [Arch debt] Proven Solutions: give the process descriptor a real schema
and wire the R1-R17 graph validator into the write path (ENG-88414) lands first (ENG-88414 is AI-driven application
development), its allow-list learns the write members and its read-back list the describe-only members.

### D12 - Source immutability and `setElement` semantics (no owner decision)

**Decision.** A source change is refused (a): "'File1' reads object attachments; its source cannot change. Remove it
and add a new element (these mappings use its outputs: ...)". The dependents come from the request's
`IProcessDependencySnapshot` (`FindDependents(element)`), injected into `FileProcessingConfigApplier` as
`DeleteDataConfigApplier` does, with the ENG-100041 wording ("or did when this request started"; "re-point them in a
SEPARATE, earlier call"); asked from the applier only, never from the create path (P4). `setElement {fileProcessing}`:
1. **Member-wise merge** inside `attachments`, `report` and `saveTo`: an omitted member keeps its value; an empty
   string or object clears where clearing is legal; a value-source member replaces as a whole when sent; null never
   clears (WCF cannot tell null from omitted).
2. **Retarget.** An `attachments.object` change to a different storage pair clears the stored filter (scope and
   remainder), the sort and the record scope unless a new `attachments.recordId` is sent (designer parity;
   `DataSourceFilterValue.ClearIfForeign` is not used, because in SysFile mode the root is SysFile for every object). A
   `report.printable` change to another report object clears the filter, the record scope and `fileNameSuffixColumn`;
   to the SAME report object it keeps them and re-runs the record-scope type check (a deliberate, documented divergence
   from the designer). A notice lists what was cleared or kept.
3. A `saveTo.object` change requires `saveTo.recordId` in the same update.
4. **Action on update** changes only when `action` is sent, or when `saveTo` is sent to a "use in process" element
   (it becomes `saveToAttachments`, with a notice). Switching to `useInProcess` clears all four target fields
   (`TargetDataEntitySchemaUId` included) and is refused while another element maps from `CreatedObjectFileIds`
   (`FindDependents(element, parameter)`).
5. **Repair on touch.** Any `setElement fileProcessing` on an element whose stored storage pair is inconsistent (D15
   `Decode` invalid or unpaired, the H-G3-1 state included) rewrites the whole pair from `Resolve`, with a notice.

**Rejected.** (b) Replace in place like the designer: values dropped silently and dangling references the request did
not mention. (c) Mutate the schema UId in place: the designer never does it.

**Consequences.** `FileProcessingConfigApplier` shares a static binder with the handler's create path (the
OpenEditPage structure). Tests cover the same-batch case (clear the dependent mapping, then change the source in one
batch: refused). The SysFile-to-SysFile retarget test belongs to SF.

### D13 - The generic `userTask` route and raw configuration writes, refused per cut (no owner decision)

**Decision (a), scoped to the cut that makes the variant configurable.** OA refuses `{type: userTask, userTaskName}`
for ObjectFileProcessingUserTask in `UserTaskElementHandler.Create`, after the two merged refusals (`:86-87`) and
before the element is created (`:88`), keyed on the RESOLVED schema UId (case, padding and the `performTask` alias
included), create-only, in the ENG-102112 shape: the message names the element, the task, the type used,
`fileProcessing` with its group, and the `setElement` repair. RP adds the Report and Process schemas. `addMapping` onto
the 13 binder-owned configuration parameters of a claimed schema (`SourceEntitySchemaUId`, `SourceDataEntitySchemaUId`,
`TargetEntitySchemaUId`, `TargetDataEntitySchemaUId`, `ConnectedObjectColumnUId`, `ResultActionType`, `RecordsToRead`,
`OrderByInfo`, `ReportId`, `IsSeparateReports`, `ReportNameDataSourceColumnUId`, `DataSourceFilters`,
`ConsiderTimeInFilter`) is refused, pointing to `setElement fileProcessing`, by extending ENG-102113's
`FindDataObjectSlot` / `EnsureTheDataObjectIsKept` slot table rather than adding a second mechanism. `addMapping` onto
the value inputs (`ConnectedObjectId`, `Files`, `Files.File`, `ReportName`) stays legal and runs the block's rules.

**Rejected.** (b) Silently redirect the generic route to `fileProcessing`: it changes an existing request's meaning.
(c) Leave it: the route builds an element that saves green and throws at run time.

**Consequences.** The raw-mapping refusal takes away a route that has written correct schema UIds since 1.6.6.69 (P7);
it stays because that route bypasses the storage pair, the link column and D12's clearing, and still accepts a row Id.
In OA the Report and Process generic routes still build (a "tolerated" regression test) until RP. In clio, the
`list-user-tasks` description and `ListUserTasksPrompt` add each claimed schema to their dedicated / refused exceptions
per cut; in clio-knowledge, `element-catalog`'s "FOUR user tasks have their own dedicated build type" becomes FIVE in
OA's knowledge PR, together with the three knowledge tests that pin FOUR and the `process-digest` card (about 3 bytes
free), and RP extends the generic-route sentence to both other schemas.

### D14 - `ResultActionType` (agreed 2026-10-14, Q15)

**Decision, on create only.** Object and Report: `action` is optional and inferred (`saveTo` present:
`saveToAttachments` = 0; absent: `useInProcess` = 1); `useInProcess` with `saveTo`, and `saveToAttachments` without
it, are refused. Process variant: `action` omitted or `saveToAttachments`; `useInProcess` refused ("the platform throws
for 'use in process'"); `saveTo` required. `ResultActionType` is ALWAYS written explicitly as a ConstValue on create.
Update follows D12 rule 4.

**Rejected.** The designer default (omitted = use in process): a caller who sent `saveTo` and forgot `action` gets a
silent no-save. A mandatory `action`: one more required key and more refusal round trips.

**Consequences.** Describe reports `action` and the describe-only `actionStored: false` when the stored value is unset
("runs as saveToAttachments"). The Report variant gets a notice when its "use in process" `ReportFiles` flow into an
Out process parameter (temporary files; wording pending M16). The Object variant gets no notice; the guide says its
"use in process" output points at the source attachments.

### D15 - The attachment-storage resolver (agreed 2026-10-14, Q17; SysFile in SF agreed 2026-10-07, Q3)

**Decision.** `IAttachmentStorageResolver` with `Resolve(objectName, linkColumnName, role)` and `Decode(...)` (never
throws), over a thin `IEntitySchemaHierarchyReader` adapter (`SchemaManager.GetAllParents` is non-virtual), following
the designer's algorithm: eligible objects are base (non-replacement), not virtual, not a database view, and their
direct parent is not BaseEntityInTag, BaseFolder, BaseItemInFolder or BaseTag; a record object with an eligible
`<X>File` (Lead: `FileLead`) keeps the legacy pair, any other one is SysFile mode. `Resolve` and `Decode` are complete
in OA for both modes, so describe reports designer-built SysFile elements.
- **SysFile ships in SF.** OA refuses SysFile sources and targets through a switch SF flips: "reading or saving the
  attachments of 'UsrX' needs SysFile storage, which this version does not support yet; use an object with its own
  attachment object, or use the files in the process". Designer-built SysFile elements are described without loss, and
  `setElement` / `setFilter` on them are refused until SF. SF lifts the refusal per variant after M7, M8, M21, M23 and
  M25; a case whose measurement fails keeps its refusal.
- **The designer flag** is not read for behaviour (A, agreed 2026-10-14); in SF a notice, never a refusal, when
  `Features.GetIsEnabled(<code name>)` reads off, shipped only after M25 confirms the read (through the one-method
  feature seam shared with D17).
- **Link column**: a target file object needs a column that references the record object, or an explicit `linkColumn`
  that is a lookup column of the file object; no `"none"` (a same-schema copy without a link column lands on the source
  record, F4). An explicit column that differs from the designer's is accepted with warning W6. This also expresses the
  shipped GenerateDNSRecordsSpecification shape (`saveTo {object: "DNSGuideFile", linkColumn: "SenderDomain"}`).
- **No `storage: "sysFile"` override** in v1; `storage` is an identity check. Revisit after M12.
- **`saveTo.recordId` constant**: validated as a lookup of the record object (parses, not the empty Guid, the record
  exists) through a new internal entry point of `ProcessParameterValueValidator.EnsureReferencedRecordExists`; a mapped
  `recordId` gets the notice "an empty value at run time fails a legacy target (foreign key) and orphans a SysFile
  target" (M17-Q7 measured the foreign keys).
- Refusals R1-R9 (object not found; the abstract `File`; SysFile named directly; an object inheriting SysFile; a
  virtual, view, tag or folder object; several attachment objects; no SysFile and no dedicated object; no link column;
  a `linkColumn` that is not a lookup) and warnings W6 (an explicit `linkColumn` that differs from the designer's
  column) and W7 (the record object also has files in an unpaired file object).
- Invariants: never write `SourceDataEntitySchemaUId` for a legacy source; always write `*DataEntitySchemaUId` in
  SysFile mode; rewrite the whole pair from `Resolve` whenever a request writes the block.

**Rejected.** Reading the flag and reproducing the designer's OFF list: a copy of `ModuleStructure` and a
string-coupled feature read for a behaviour the runtime never consults. `linkColumn: "none"` (above). SysFile inside OA
pending M8 and M23: five measurements, three of them designer probes built by the user, and two high-severity
SysFile-only review findings would sit on OA's critical path.

**Consequences.** One interim release in which an object without its own attachment object cannot be a source or a
target. If M11 (c) ever confirms H-G3-1 (a designer open-and-save writing `SourceDataEntitySchemaUId = <X>File`): a
Sub-task reporting it to CrtProcessDesigner, D12 rule 5 repairs a touched element, and a guide warning ships.

### D16 - Filter and sort roots, the record scope, the empty-filter policy (agreed 2026-10-14, Q16)

**Decision.**
- **Record scope R2**: `attachments.recordId` (Object) and `report.recordId` (Report), in the shared value-source
  contract. One helper, `FileProcessingScopeFilter`, owns the scope condition: `Split(stored) -> (scope, remainder)`
  and `Join(scope, remainder)`. The block binder writes `Join(newScope, storedRemainder)`, the filter target
  `Join(storedScope, newRemainder)`, and describe uses `Split`. Compile by source: `value` -> a constant;
  `processParameter` -> the condition's `processParameter`; `sourceElement` (+ `sourceColumn`) -> `elementParameter`;
  `expression` -> only `[#SysVariable.CurrentUserContact#]`, compiled to the `CurrentUserContact` macro; any other
  formula is refused. Shape: legacy `<link column> = value` as a lookup InFilter (the corpus shape); Report `Id = value`;
  SysFile (in SF) the shape M7 captures, by default the same lookup InFilter on `RecordId`. A root `or` remainder is
  wrapped as `AND(scope, OR(...))`. `Split` lifts no `recordId` from a decode that is not `DecodedCompletely`, and
  `Join` keeps the `isNull` flag (ENG-99970 CAADT runs for BPMS Tools cost 6-15x other teams - find and close the gap,
  1.6.6.82).
- Type check: a Lookup source must reference the record object (Object) or the report entity (Report); a Guid is
  accepted; a constant must exist; `CurrentUserContact` only when that object is Contact. The same scope set twice is
  refused.
- **`FileProcessingFilterTarget`** (priority 50, between Signal 100 and DataNode 0), claiming through the registry per
  cut. An omitted `filter.object` is the runtime root; a different one is refused, naming the root and (Object) the
  record object, pointing to `attachments.recordId` (no alias). A filter on an element whose source or printable is not
  configured is refused ("in a batch, setElement before setFilter"). `setFilter` on the Process variant stays refused
  and the message lists the variants that take a filter, per cut.
- **Sort**: the readData shape `{column, direction}`, ONE direct column of the runtime root, resolved by the sort codec
  EXTRACTED from `ReadDataConfigBinder` into a shared internal codec that takes the element label and kind, so the Read
  data refusal texts stay byte-identical (reuse RF2). A stored multi-entry `OrderByInfo` is kept while `sort` is not
  sent; `sort` on Report is refused.
- **`numberOfRecords`** 1..5000; omitted = 50 (the designer default), NOT "read all" as in readData; a notice when it
  is omitted on a saving or forwarding element.
- **Empty-filter policy**, at the END of the request, touched elements only ("selecting" = a record scope or at least
  one remainder condition): **refuse** Object + `saveToAttachments` and Report (Word) with no selecting filter;
  **notice** Object + `useInProcess`. The NOTICE is one `Rules` entry per schema name in `ElementConfigurationNotices`
  plus a `Describe...` method, reusing `HasSelectingFilter` and `HasEffectiveValue` (P3). The REFUSAL cannot ride that
  ledger (notice-only, after the gate, no touched-set query), so it is a new PRE-gate check with its own record of
  touched elements (`IFileProcessingFilterLedger`, or a read-only tracked-set accessor added to
  `IElementConfigurationLedger`): on modify next to `EnsureFormulasStillBelongOnTouchedConnectors`, on build at the end
  of `ApplyDeclarativeContent`; thrown outside the operation loop, so `failedOperationIndex: null`.

**Rejected.** R1 `filter.recordId`: a second meaning of `recordId` and of `expression` in one element, and a member
clio's bagless filter DTO drops on describe (K4). R4 no sugar: the agent would have to know the storage rule and the
link column. Aliasing `filter.object` = the record object to the file object: hides the trap rather than teaching it.
Notice everywhere: the precedent of ENG-102537 for Delete data / Modify data, weighed and not followed for the two
harmful cases (copying arbitrary files onto a record; one Word document per record of the whole entity); the owner
rejected it here knowing it (Q16, 2026-10-14): an unconfigured element visibly fails at run time, while an empty filter
on this element runs "successfully" with a silent mass effect.

**Consequences.** Describe reports `filter.object` = the runtime root and lifts the scope into `recordId` when `Split`
finds exactly one top-level equality on the scope column that decodes to one value source; otherwise the condition
stays in `filter.conditions` and nothing is lost. The lift also applies to designer-made legacy filters. In SysFile
mode, describe reports an issue when the scope's lookup reference differs from `SourceDataEntitySchemaUId`.
"Attachments of the records matching a condition" in SysFile mode is out of v1.

### D17 - Generated report rules (agreed 2026-10-14, Q18)

**Decision.** `IReportTemplateReader`: an ESQ on `SysModuleReport` with `UseAdminRights = false` (the runtime's own
query and entity fallback; columns `Id`, `Caption`, `Type.Name`, `SysEntitySchema.Name`, `ConvertInPDF`).
`report.printable` accepts the id (preferred: "pass list-printables' `templateId` as `report.printable`"), a
`[#Lookup...#]` macro or a caption; an ambiguous caption is narrowed by `report.recordId`'s object when exactly one
candidate matches, otherwise refused with every candidate's id, type and object. Refusals: missing, empty Guid,
unknown or unreadable ("No printable 'X' is available. The Process file element cannot create one. Ask the user to
create it in System Designer -> Report setup, then retry." plus up to N candidates), DevExpress and any other type,
**FastReport (B, agreed 2026-10-14)**, a printable with no object, a bad `fileNameSuffixColumn` (separate off, not a text
column of the report object, or together with `fileNameSuffix`). Word `separateReports` is written true when omitted;
an explicit `false` is accepted with a notice. A constant `fileNameSuffix` is written through the localizable resource
path. Notices: `ConvertInPDF` on; several records into one target; `EnableReportFileProcessingUserTask` off (through the
feature seam, never a refusal); temporary report files into an Out parameter (D14).

**Rejected.** A new report-lookup MCP tool (the compact tool index had about 25 bytes left; `list-printables` is the
route). FastReport accepted with a notice (A): a configuration that may always throw would be accepted silently. Picking
the closest printable: AC-RP3 forbids it. Refusing Word `separateReports: false`: a describe of a legacy element would
not resubmit.

**Consequences.** Never create or pick an unrelated printable. The e2e skips with `Assert.Ignore` when
`list-printables` returns no rows; `ListPrintablesEnvelope` gains `count` and `printables`. Guidance: the
`templateId` -> `report.printable` bridge; record order unspecified; captions in the runner's culture; a mid-run failure
leaves partial output.

### D18 - Process-parameter variant: the `files` input (no owner decision; M1 decides)

**Decision.** `files` takes exactly one of `processParameter`, `sourceElement` + `sourceElementParameter`, or
`expression`; `value` is refused (a file has no constant form). A structured COLLECTION source (a FileCollection, an
element's `ObjectFiles` or `ReportFiles`, or a dotted item of one) binds BOTH levels through ENG-95984's
`BindCollection`: `Files` <- the collection, `Files.File` <- its single FileLocator item, resolved, never assumed (`File`,
`ObjectFile` or the declared name). A single FileLocator source binds `Files.File` only, **pending M1**; if M1 fails the
single-file path is refused and the guide says to wrap one file in a FileCollection. `expression` is written onto
`Files.File` as given (only its meta-path tokens are checked) with a notice. Refused: a source with no FileLocator item
(R-M2), an outer-only binding, no binding, a mapping FROM `Files`, `useInProcess` (D14). `saveTo` is required.

**Rejected.** Outer-only from a single file: the runtime reads `{File: null}` rows. Accepting `value`: no constant form
exists.

**Consequences.** Describe decodes `files` to `processParameter` or `sourceElement` + a dotted
`sourceElementParameter` (or `files.expression`) and reports `bindingLevels` (both, itemOnly, outerOnly, none); anything
else is an `issues` entry and replays through the raw parameter list. The variant's outputs are `ObjectFiles` (item
`ObjectFile`, the copies) and `CreatedObjectFileIds`. Package tests pin the metadata at both levels; the runtime goes to
the stand (M1, M2, M4).

### D19 - Describe block of the element (no owner decision)

**Decision.** A typed `fileProcessing` block in `DescribeProcessElement`, per cut (OA: Object; RP: Report and Process),
reporting EFFECTIVE values (inherited template defaults count) in the write vocabulary. Value sources decode to
`processParameter`, or `sourceElement` + a dotted `sourceElementParameter` (+ `sourceColumn`) when the stored Script is
one token, otherwise `expression`; a constant as `value`. `issues` covers the silent hazards: an inconsistent storage
pair, a target with no link column, a sort or filter column the runtime root lacks, a SysFile scope whose reference is
not the record object, hidden secondary sort entries, an outer-only or unbound `Files`, a `Files` shape that does not
decode, Word with `IsSeparateReports = false`, a DevExpress printable, an unset `ResultActionType`. The reader tolerates
unconverged snapshots: absent parameters read as null, a Lookup-typed `ConnectedObjectId` reads like a Guid (M13: shipped
elements come back with `Guid` and `referenceSchema`). The raw `parameters[]` and `userTaskName` stay.

**Rejected.** Keeping the generic raw parameter list only: lossless at the parameter level but not round-trippable,
and it hides the inherited defaults and the empty link column.

**Consequences.** clio gets a typed `DescribedFileProcessing` DTO in which EVERY nested type carries its own
`[JsonExtensionData]` bag, so a member a later package adds survives this clio; `///` summaries on every type and public
member; the knowledge record `docs/knowledge/ProcessModel/described-filter-types-have-no-json-overflow-bag.md` updated.
An older clio keeps the block through `DescribedElement`'s bag (K4).

### D20 - Serialization parity, template defaults, nested UIds, diagram (no owner decision)

**Decision.** The handler writes `SchemaUId`, `ManagerItemUId = SchemaUId`, `SerializeToDB = taskSchema.SerializeToDB`,
the factory's size (69x55) and logging flag, and the lane; nothing else is needed for the diagram. Template defaults are
written as an exact copy of the template value, provenance included: `ConsiderTimeInFilter` always on create (Script
"true", which keeps the server's `DefValueForExistingProcess` "false" out; M14 measured the "false"), `RecordsToRead` on
create when `numberOfRecords` is omitted. Every caller-chosen value (an explicit `numberOfRecords: 50` included) and
every derived value is a new value, source before value, `ModifiedInSchemaUId` = the process. Nested item UIds are
re-minted on create only (fresh Guid, `IL2` = the element UId), before configuration and before any mapping of the same
request resolves the element's items; never on modify. Nested-source display values use the designer form
`[#<element caption>.<collection caption>:<item caption>#]`. A template parameter missing after `GetDesignInstance` is
refused ("not synchronized"), never hand-created.

**Rejected.** Pinning `ConsiderTimeInFilter` with the process's provenance: diverges from every designer capture. Re-
minting on modify: consumer mapping tokens embed the nested item UId. Writing nested `BK15` rows: the alternative to
tolerating N10, the owner's call if strict parity is wanted.

**Consequences.** The comparison rule for the AC (PRD): UIds ignored, `IL2` of nested items compared, `GS5` normalised
for template-default values, tolerated N2 (`BL8` absent; generic, recorded for every builder element, no Sub-task), N9,
N10, N13; normalised N11, N14-N16; parameters a shipped oracle predates excluded and NAMED in the test until the SC-1
designer capture replaces that oracle (FileCopyProcessPP: `ConsiderTimeInFilter`, `SourceDataEntitySchemaUId`,
`TargetDataEntitySchemaUId`; FileParameterProcess: `TargetDataEntitySchemaUId`, with its item `FileCollectionParameter`
role-normalised to `File`; PrintInvoiceReport: whatever SC-0 records, none expected); anything else fails. The
exclusion class is the analysis test plan's (TC-41, TC-69) and clarifies decisions Part D's "any other difference
fails". The generic-route `SerializeToDB` gap for every other user task is the side Sub-task of story 5.

### D21 - Reuse map (no owner decision)

**Decision.** Reuse what core and CrtProcessBuilder already provide, mirror the configuration-level constants that
production package code cannot reference (`Terrasoft.Configuration`), and write new only what the table leaves. The
"Do not reuse" column lists the rejected candidates, each for the reason given (the full map with evidence is the
analysis [reuse](../eng-92719-file-processing-element/eng-92719-file-processing-element-reuse.md) document).

| Need | Reuse | Mirror | Do not reuse |
|---|---|---|---|
| Element identity | `UserTaskSchemaIdentity` x3, through the variant registry | the three schema names (UIds in fixtures and the drift guard only; RF1) | - |
| Handler structure | `OpenEditPageElementHandler` + a shared static binder + an applier | - | - |
| Value sources | `IProcessMappingService.ApplyMapping`, `RecordColumnReference`, ENG-95984's `BindCollection` | - | hand-built source values |
| Record-id constants | `ProcessParameterValueValidator.EnsureReferencedRecordExists` (new internal entry point) | - | the scalar Guid branch |
| Filters | `ProcessFilterService.BuildFilterValue`, `FilterDescriptorReader`, `ProcessFilterTargetBase`, the new `FileProcessingScopeFilter` | `FileConsts.FileTypeUId` (describe issues only) | `DataSourceFilterValue.ClearIfForeign`; the designer's SysFile sort list |
| Sort | **reuse + change**: the codec extracted from `ReadDataConfigBinder` (RF2) | the `OrderByInfo` format | - |
| Storage | `EntitySchemaManager`, `SchemaManager.GetAllParents` behind an adapter | SysFile and BaseFile UIds, the tag/folder bases, the `<X>File` / `FileLead` rule | core `FileSchemaProvider` (configuration code, a different rule) |
| Reports | ESQ on `SysModuleReport` (`UseAdminRights = false`) | the type names, the entity fallback | `ReportEngine` |
| End-of-request checks | notices: one rule in `ElementConfigurationNotices`; refusals: a new pre-gate check (RF3) | - | a refusing reconcile in the ledger |
| Dependents for refusals | `IProcessDependencySnapshot` | - | a live `ProcessElementDependencyScanner` scan |
| Feature reads | `Creatio.FeatureToggling.Features.GetIsEnabled(string)` behind one seam | the two feature code names | `UserConnection.GetIsFeatureEnabled` (reads off on the stand) |

**Consequences.** Three rows change work packages (reuse corrections RF1-RF3, plan C-6): identity mirrors names, not
UIds (OA.1); the sort helpers are extracted into a shared codec rather than reused as they are (OA.4); the empty-filter
refusal is a new pre-gate check, because the ledger only raises notices (OA.4).

### D22 - Downstream consumers: which ticket owns what (agreed 2026-10-07, Q5)

**Decision.** Process file -> Send email attachments belongs to ENG-95985 Send email attachments (it reuses
`BindCollection` and adds its slots to the file-consuming targets; no new Sub-task, link L5 only). Process file ->
Creatio.ai call is out of scope (guidance: "not through this tool yet"). Read data -> Process file is tested in OA and
RP; Process file -> Modify data (re-find by filter) gets guidance in OA plus the build-and-describe e2e TC-71; Add data
-> report gets guidance in RP plus the e2e TC-74; Process file -> multi-instance sub-process per file is OA's e2e after
ENG-95984 merges; the Object -> Process chain is RP's e2e. A NEW Sub-task, "Refuse collection parameters as filter
values", fixes the latent non-file defect that a filter takes a collection parameter as its value (M19: accepted at
build, fails at run).

**Rejected.** Keeping Send email and the Creatio.ai call in this Story: both need dynamic attachment slots the builder
cannot create, and ENG-92725 Execute AI Intent element (BP generation) is closed as Won't Do.

**Consequences.** FE's "five patterns" criterion is amended (FE-6). The guide's consumers section states what is
buildable, what is not yet, and the per-item iteration route for Ids.

### D23 - Jira dependencies (agreed 2026-10-07, Q2)

**Decision.** L1 ENG-95984 File process parameter type **relates to** ENG-96505 (replaces link 560203 "blocks"); L2
ENG-95984 **blocks** ENG-96506; L3 ENG-96505 **blocks** ENG-96506; L4 ENG-96506 **blocks** SF (reversed to "ENG-96505
blocks SF" under contingency X3); L5 ENG-95984 and ENG-96505 **block** ENG-95985 Send email attachments. New Sub-tasks
SF and the two side Sub-tasks, all of type Sub-task, under ENG-92719.

**Rejected.** Keeping "ENG-95984 blocks ENG-96505": the Object variant needs no File process parameter (its outputs are
element parameters that dotted element paths already address), so the link would claim a functional dependency that
does not exist.

**Consequences.** The delivery ORDER stays ENG-95984 -> OA -> RP -> SF: the package PRs share `descriptor.json`, the
contracts, `ProcessMappingService.cs` and `ProcessDescriber.cs`; ENG-95984's clio PR carries the `ManagerMap` arm; OA's
per-file test needs a callee File parameter.

### D24 - Acceptance-criteria corrections (agreed 2026-10-07, Q1)

**Decision.** FE, OA and RP take the replacement text of the analysis decisions Part D (D-2, D-3, D-4), copied into
the PRD. The rows that encode a recommended answer to one of Q13-Q18 (FE-1, OA-1, OA-4, OA-6, OA-7, OA-9, RP-1,
RP-4) were conditional on that answer; the owner agreed those answers on 2026-10-14, so the rows are pasted as written. SF takes the SysFile row of D-5 as a
proposal, confirmed when the SF Sub-task is created (Q1 covered ENG-95984, FE, OA and RP only). "Binary / File"
becomes File + FileCollection; serialization is compared per variant and per storage under a written rule; "rarely an
endpoint" becomes the measured shipped pattern; Send email moves and the Creatio.ai pattern leaves; the source object
is a schema choice and the record a value of the element; Report and Process stay generic until RP; SysFile moves to
SF; a printable is an MS Word printable of the reported object; "use in process" does not exist for the
Process-parameter source.

**Consequences.** The Story is done only when OA, RP and SF are done. OA, RP and SF each move to done when their own
knowledge PR merges. The two side Sub-tasks do not gate the Story; the owner confirms the Jira workflow allows closing
the parent with them open (Q9).

### D25 - Guidance placement and tool-description budget (agreed: guide name Q19, bytes Q20)

**Decision.** One guide, **`process-files`**, born banner-less in ENG-95984's knowledge PR
(https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/285), not in `ProcessGuideSet.GoLiveFloor`.
OA adds the element, "the group is the variant", `attachments` (`recordId`; "numberOfRecords defaults to 50 and does
NOT read all"; the sort shape with M24's result), the member-wise merge, identity checks vs describe-only members,
dedicated storage and "SysFile storage is refused in this version", the action per variant, the consumers, and the
endpoint sentence "most shipped uses end in a record's attachments; when a following element needs the files, map the
element's output collection" (never "rarely an endpoint"); the sentence "after saving, `ObjectFiles` points at the
source files" only after M26, as "measured <date>". RP adds the report and process-parameter sections and "Add data
creates the report's data row first". SF rewrites the storage section with the SysFile hazards and removes the refusal
sentences behind a version gate. `element-catalog`: the buildable row and the generic-route sentence, per cut. Every
read-back says `describe-business-process mode=full` (or `element=<name>`) under the "on clio older than 8.1.0.141, omit
`mode`" gate; every `install-process-builder` mention carries consent wording; no Jira key or PR link; target about
21,000 characters, with the report section split into `process-files-report` past that.

Tool descriptions: OA adds the token, the block clause with `get-guidance name=process-files`, the `addElement` /
`setElement` lists and the describe clause (about +128 B create, +74 B modify, +128 B describe, estimated). Create and
modify have no room (K3), so **the bytes come from a reviewer-approved trim of existing text per tool (Q20 = A)**,
measured by lowering the ceiling; the `fileProcessing` call-shape entry (about 25-30 B, never cut) is paid by trimming
kept short-form text of one of the seven contracts under the 19,000 B wire guard. The curated fixture
`curated-knowledge-names.json` is re-pinned to the PUBLISHED generation that carries `process-files` in the same
commit as the first `name=process-files`. RP changes only the enforced floor clause, in the create, modify and
modify-as-new-version descriptions (describe stays presence-only). Raising the floor can lengthen the version literal
(a new digit, e.g. at 1.6.6.100), which costs one byte in each of the create, modify and modify-as-new-version
descriptions. `ToolContractPayloadBudgetTests` catches it at the cut that does it; that PR pays it under Q20=A (a
reviewer-approved trim, no ceiling raise). SF changes no description. MCP prompts change per
cut: `ListUserTasksPrompt` (the claimed schemas leave the generic-`userTask` advice), `CreateBusinessProcessPrompt`
(the `fileProcessing` sentence), `ModifyBusinessProcessPrompt` (the D12 merge rule, OA), `DescribeProcessPrompt` (the
block name, OA). Resources and `clio/tpl` need no update.

**Rejected.** `process-file` (reads as the element only) or one guide per ticket. B, a reviewed ceiling raise, and C,
moving the type lists into guidance (Q20). Registering in `GoLiveFloor` (fails `GuidanceMigrationTests`).

**Consequences.** The generation that births `process-files` must be published (merged, released, visible in
`info-knowledge`) before OA's clio PR merges; a failed `update-knowledge` keeps serving the old library silently. Each
knowledge PR bumps libraryVersion above master and every branch claim at merge (1.15.144 or higher on 2026-10-14).

### D26 - `[RequiresPackage]` floors and version numbers (no owner decision)

**Decision.** Numbers are rules, not projections: each cut claims, in its PR description AND in the one shared cut
claim on ENG-92719 File processing element, the first free number above the highest number claimed anywhere at cut time
(re-check with `git ls-remote --tags` in both repositories). The claim list (comment 550565), with the tag and
`descriptor.json`, is the one place a number lives; this set names no current or future cut (owner decision,
2026-10-14). The create, modify and modify-as-new-version floors move to the **final** cut of OA's and RP's clio PRs,
equal to `ExpectedArchiveVersion` when each merges; SF raises none (an older server refuses SysFile loudly, the
ENG-96230 Collection process parameter type precedent). describe-business-process, list-user-tasks and
validate-process-graph stay presence-only.

**Rejected.** No floor: an older server drops `fileProcessing` on `setElement` silently (M20). Floor = the first cut: a
foreign archive under our first number would satisfy it without the feature.

**Consequences.** Files that move with each raise, in the archive's commit: the three literals with their comment
blocks, `ProcessDesignerRequiresPackageAttributeTests`, the enforced-floor clause in three descriptions,
`docs/McpCapabilityMap.md` floor rows, the new e2e fixture's `MinimumPackageVersion`. No new WCF operation, so
`ExpectedOperationContractCount` and `ExpectedAuthorizationGateCallSites` do not move. A permanent capability probe on
`ProcessElementUpdateDescriptor.FileProcessing` guards against a foreign cut between ours. Every stand reinstall is an
`install-process-builder` call that needs the user's approval each time and restarts the site.

### D27 - PR split per repository (agreed 2026-10-07: Q6; delivery items O1 via Q3, O3 via Q4, O4-O8 via Q8)

**Decision.** One PR per Jira issue per repository, merged package -> clio -> knowledge by a human; SysFile carved out
into SF (O1). For this Story: this docs-only clio PR (CL-DOC, titled `ENG-92719 File processing element`, O4) plus
three triples titled exactly `ENG-96505 Element readiness and object attachments mode`, `ENG-96506 Generated report +
process parameter modes` and `<SF-KEY> SysFile attachment storage in the Process file element`. With ENG-95984's
triple and its docs PR, 14 PRs. At most one of our package PRs in human review at a time; the next ticket iterates as a
stacked draft. Delivery items: O1 SF as its own Sub-task; O3 RP before SF; O4 the docs-only PRs; O5 the variant registry
(above); O6 ask for a second disposable .NET Framework stand; O7 merge commits for lower PRs with a stacked draft above;
O8 merge windows with the owner of ENG-95244 (the only in-flight work left on 2026-10-13).

**Rejected.** One PR per repository for everything (a 12-16k-line package diff, one floor for four blast radii, a
long-lived branch). A PT triple plus one FE triple (the largest diff of the cycle). Layer splits (each leaves a `main`
that saves green and fails at run time). An inert scaffold PR (O5's rejected option).

**Consequences.** The side Sub-tasks ship as their own triples, off the critical path and never inside a final review
window of the main track (each restamps). Contingencies: X2 cherry-picks RP's Report half if the Process half fails its
stand proof (a Sub-task "Process parameter source of the Process file element"); X3 swaps RP and SF if the owner ranks
custom-object attachments first.

### D28 - Test strategy and C# mocking (no owner decision)

**Decision.** Package unit tests (NUnit, `[TestFixture(Category = "UnitTests"), Category("PreCommit")]`, AAA,
`because:`, `[Description]`) build every element by hand with the real schema UIds and literal parameter names through
`FileProcessingTestSupport`, guarded by the drift guard against the shipped CrtProcessDesigner metadata (both owned by
ENG-95984's package PR, inherited here); every task schema a test resolves is registered on ONE user-task schema
manager; a real `new DataValueTypeManager()` registers FileLocator and `CompositeObjectList`; the hierarchy reader, the
report reader and the feature seam are faked. The platform's user-task sync on `SchemaUId` runs in the harness (the
ENG-95984 recipe); the runtime reader, a `ProcessSchemaManager` save and the pre-save validation do not, so they go to
the stand. clio unit tests run by `TestCategory` from bash with `< /dev/null`; every changed MCP tool gets e2e coverage
in a new fixture with `MinimumPackageVersion` = the floor, evidence Passed with Ignored = 0, every graph read-back with
`mode=full` or `element=<name>`. Knowledge: a pin test per rule family, plus the size and the newer guards.

**Rejected.** Porting the platform's own `ProcessDesigner.UnitTests` (configuration types the test build cannot load);
they are a reference for runtime semantics only.

**Consequences.** TeamCity does not run the `McpE2E.ProcessDesigner` category, so every e2e run is a manual run on the
stand that counts only with Ignored = 0.

### D29 - Stand measurements that precede code (no owner decision; every write needs the user's go-ahead)

**Decision.** Every runtime-critical claim that rests on a source trace only is measured on the .NET Framework stand
before the code that depends on it (pr-split edge E8); a decision that rests on an unmeasured claim says "pending
M-number" and names its fallback. **Rejected**: coding on the source trace alone (a source trace is a hypothesis: M3
refuted the source-traced mirror defect H-1 on 2026-10-02) and running the measurements after the code (a failing one
would then mean rework under review). **Consequences**: the gates below; SF's five measurements are why SysFile is its own
Sub-task (D15).

| M | Question | Write | Gates | State |
|---|---|---|---|---|
| M10 | the designer's object list equals the resolver's prediction | no | OA code (R3-R5) | done 2026-10-02: matches |
| M11 (a)(b) | H-G3-1 in memory; (c) optional save and run | (c) yes | OA code (issue severity) | done 2026-10-02: not reproduced; (c) optional |
| M13 | describe of PrintContractsReport: converged or snapshot | no | OA and RP code (TC-45 fixtures) | done 2026-10-02: `Guid` + `referenceSchema` |
| M14 | the server-built element shape on 1.6.6.54 | yes | OA code (D20 pins) | done 2026-10-02 |
| M17-Q7 | foreign keys for an empty `ConnectedObjectId` | no | OA (notice wording) | done 2026-10-02 |
| M1 (+M2) | a nested-only `Files.File <- File` copies one file; an unset one fails with an NRE | yes | RP code (single-file path) | open |
| M15 | FastReport packages and printables | no | RP (Q18) | done 2026-10-02: none |
| M7 | the filter JSON the designer writes for a SysFile `RecordId` scope | no | SF code | open |
| M8 | an Object element finds a Freedom UI upload stored in SysFile | yes | SF code | open |
| M21 | a designer save capture of a SysFile-mode Object element | yes | SF code (the SF capture) | open |
| M23 (a)-(d) | the SysFile write path, four cases | yes | SF code | open |
| M25 | the server feature read and its code name | yes | SF (notice only) | partly (client read) |
| M26 | after a save, `ObjectFiles` holds the SOURCE locators | yes | OA guide sentence and knowledge record, no code | open |
| M4, M9, M12, M16, M17 runs, M18, M22 | end-to-end chain, SysFile sort, override, temporary files, empty record, culture, designer no-op save | mostly yes | no code | open |
| M19, M20, M24 | collection filter value, silent block drop, WCF shape | yes | no code | done 2026-10-02 |

Rules: probes that need a File parameter or a configured Process file element are built by a human in the designer;
designer reads use a new unsaved process, closed without saving; schema writes and runs go one at a time; a callee's
inputs are read through a gateway oracle, never from the process trace; every record names the package version.

---

## Refusal and notice catalogue

Server-side texts; none costs tool-description bytes. Tests assert the key words each entry must mention, not the full
sentence. F-T and F-M (the parameter type and the binder) are ENG-95984's.

| ID | Kind | Trigger | Decision | Cut |
|---|---|---|---|---|
| F-E1 | refuse | the generic `userTask` route for a claimed schema | D13 | OA (Object), RP (Report, Process) |
| F-E2 | refuse | raw `addMapping` onto a binder-owned configuration parameter of a claimed schema | D13 | OA, RP |
| F-E3 | refuse | no group or two groups on create; another variant's group on update (dependents named); a `source` mismatch or an entity-like `source`; a contradicting `userTaskName` | D10, D12 | OA |
| F-E4 | refuse | a group that does not match the variant | D11 | OA |
| F-E6 | refuse | `action` contradicting `saveTo` on create; `useInProcess` on the Process variant; `saveTo` missing on the Process variant; `useInProcess` while `CreatedObjectFileIds` is mapped | D12, D14 | OA, RP |
| F-E7 | refuse or warn | storage R1-R9, W6, W7; a `recordId` constant empty, unknown or of another object; SysFile sources and targets until SF | D15 | OA (SF lifts) |
| F-E8 | refuse | an identity-check mismatch (`storage`, `fileObject`, `attachments.linkColumn`, `report.object`, `report.type`) | D11 | OA, RP |
| F-E9 | notice | retarget cleared or kept; repair on touch; action switched by `saveTo`; a mapped `recordId` may be empty; a SysFile element where the designer flag reads off (after M25) | D12, D15 | OA, SF |
| F-F1 | refuse | filter root mismatch, unconfigured element, record-scope type, scope set twice, a record-scope formula other than CurrentUserContact | D16 | OA, RP |
| F-F2 | refuse or notice | empty filter at the end of the request, touched elements only, `failedOperationIndex: null` | D16 | OA, RP |
| F-F3 | refuse | a sort column not on the runtime root; sort on Report; `numberOfRecords` outside 1..5000 | D16 | OA, RP, SF |
| F-F4 | notice | `numberOfRecords` omitted on a saving or forwarding element | D16 | OA |
| F-R1 | refuse | printable missing, empty, unknown, unreadable (with the Report setup hint and the `templateId` bridge), ambiguous, DevExpress, FastReport, no object | D17 | RP |
| F-R2 | refuse | a bad `fileNameSuffixColumn`; suffix and column together | D17 | RP |
| F-R3 | notice | `ConvertInPDF`; several records into one target; feature off; temporary report files into an Out parameter; Word `separateReports: false` accepted | D14, D17 | RP |
| F-P1 | refuse | a `files` source with no FileLocator item; outer-only; none; a mapping FROM `Files`; a `value` | D18 | RP |
| F-P2 | notice | `files.expression` written as given onto `Files.File` | D18 | RP |

## Implementation outline

- [Story 1](../stories/story-eng-92719-file-processing-element-1.md) (OA, ENG-96505): package OA.1-OA.8 (family core
  and registry, binder and applier, storage resolver, record scope and filter target and sort codec and empty filter,
  route refusals, describe, tests, docs and restamp); clio OA.9-OA.11 (rebundle and floor, descriptions, key set, call
  shape, DTO, prompts, e2e); knowledge OA.12.
- [Story 2](../stories/story-eng-92719-file-processing-element-2.md) (RP, ENG-96506): two variant registrations, one
  commit group each (X2), RP.1-RP.5; clio RP.6-RP.7; knowledge RP.8.
- [Story 3](../stories/story-eng-92719-file-processing-element-3.md) (SF): SF.1-SF.2 package; SF.3 clio (no floor);
  SF.4 knowledge.
- [Story 4](../stories/story-eng-92719-file-processing-element-4.md) and
  [story 5](../stories/story-eng-92719-file-processing-element-5.md): one guard each, own triples, side lane.

Every package PR carries its descriptor restamp (last commit) and the tag `crtprocessbuilder-<version>` verified on the
remote before its clio PR merges.

## Owner decisions

| Item | State |
|---|---|
| Q1 / D24 Replace the FE, OA and RP acceptance criteria | agreed 2026-10-07 |
| Q2 / D23 Jira links L1-L5 | agreed 2026-10-07; not applied in Jira as of 2026-10-14 |
| Q3 / D15 / O1 SysFile in the new Sub-task SF | agreed 2026-10-07 |
| Q4 / O3 RP before SF | agreed 2026-10-07 |
| Q5 / D22 Consumers: Send email to ENG-95985, Creatio.ai out of scope | agreed 2026-10-07 |
| Q6 / D27 One PR per Jira issue per repository (14 PRs) | agreed 2026-10-07 |
| Q7 Where the mirror defect H-1 is fixed (ENG-95984) | answered by M3 on 2026-10-02: H-1 refuted, no Sub-task |
| Q8 / O4-O8 Docs-only PRs first, the variant registry in this ADR (O5), merge commits, merge windows, a second stand | agreed 2026-10-07 |
| Q9 Follow-up Sub-tasks: the unconditional ones on day 0 | agreed 2026-10-07; SF and the two side Sub-tasks not created as of 2026-10-14 |
| Q10-Q12 FileCollection declared as `type: FileCollection`; its Variable default; the binder policy (ENG-95984's contract, which D18 relies on) | agreed 2026-10-07 |
| Q19 / D25 Guide name `process-files` | agreed 2026-10-08 |
| Q20 / D25 Description bytes: a reviewer-approved trim per tool | agreed 2026-10-14 (A) |
| Q13 / D10 Token `fileProcessing` (alias `processFile`); the group is the variant | agreed 2026-10-14; AC rows FE-1, OA-1, OA-4 |
| Q14 / D11 Naming bundle; `numberOfRecords` defaults to 50 | agreed 2026-10-14; AC rows OA-7, RP-1 (and member names everywhere) |
| Q15 / D14 Action inferred from `saveTo` on create, always written | agreed 2026-10-14; no AC row encodes it, the stories' and tests' action inference does |
| Q16 / D16 Record scope in the group; refuse saving and Word elements with no selecting filter | agreed 2026-10-14 (the ENG-102537 "notice, never a refusal" precedent weighed and not followed, D16); AC rows OA-7, OA-9, RP-1, RP-4 |
| Q17 / D15 Flag not read for behaviour; no `linkColumn: "none"`; no SysFile override | agreed 2026-10-14; AC row OA-6 |
| Q18 / D17 FastReport refused | agreed 2026-10-14; AC row RP-1 |
| **O-TP1 Where TC-91 runs** | **open** (recommended B: in RP's clio PR) |
| **Nested `BK15` rows (D20)** | **open** (recommended: tolerated as N10; the alternative is writing the rows in the handler's create path) |
| **What FE-1 means if SF narrows** | **open** (PRD "Open owner items") |
