# Facts: ENG-95985 Send email attachments

Written 2026-10-28. Read-only: nothing was built, saved or run for this file, and nothing was
written to a stand, to Jira or to a repository.

**Summary.** What is true today, before any code: in the package, in the classic designer, at run time, in the
shipped corpus, on stand `Creatio` (a registered clio environment), in the product documentation, in clio and in the
guidance library. This is the evidence base of the plan. Every claim below was re-read in source on 2026-10-28; where
a research note said something else, the source wins and the difference is listed under
[Corrections to the research](#corrections-to-the-research).

Sibling documents: [README](README.md) · [decisions](eng-95985-send-email-attachments-decisions.md) ·
[serialization](eng-95985-send-email-attachments-serialization.md) ·
[design](eng-95985-send-email-attachments-design.md) ·
[measurements](eng-95985-send-email-attachments-measurements.md) ·
[plan](eng-95985-send-email-attachments-plan.md) · [test plan](eng-95985-send-email-attachments-test-plan.md) ·
[estimate](eng-95985-send-email-attachments-estimate.md).

**Basis.**
- crt-process-builder `origin/main` `389198a0` (merge of PR #110, 2026-10-28), CrtProcessBuilder 1.6.6.125
  (`PBR/packages/CrtProcessBuilder/descriptor.json:4`).
- clio `origin/master` `358e9db52` (2026-10-28).
- clio-knowledge `origin/master` `534e913`, `libraryVersion` 1.15.173 (`KB/bundle-source.json:6`).
- CrtProcessDesigner 7.8.0 and the PackageStore corpus; core `TSBpm` trunk `8f6745caa` (2025-07-21).
- Academy "Send email process element" (version 10), read 2026-10-28.
- Stand `Creatio`: read-only inventory taken by the lead on 2026-10-28 (EM0, partly done).
- Research notes r0-r10 (local notes, not in the repository; see README), re-verified here.

**Paths.** Every `prefix/path:line` is repository-relative after the prefix.

| Prefix | Path | Revision |
|---|---|---|
| `PB` | `packages/CrtProcessBuilder/Files/src/cs/` (crt-process-builder repository) | crt-process-builder `389198a0`, 1.6.6.125 |
| `PBR` | the crt-process-builder repository root (the package repository root: `docs/`, `CrtProcessBuilderApp.cs` under `packages/CrtProcessBuilder/Files/src/`) | same |
| `PBT` | `tests/UnitTests/CrtProcessBuilder.Tests/` (crt-process-builder repository) | same |
| `PD` | `CrtProcessDesigner/branches/7.8.0/` (PackageStore repository) | CrtProcessDesigner 7.8.0 |
| `PS` | the PackageStore repository root | shipped package corpus |
| `TS` | `TSBpm/Src/Lib/` (core repository) | core trunk `8f6745caa` |
| `CLIO` | the clio repository root | clio `358e9db52` |
| `KB` | the clio-knowledge repository root | clio-knowledge `534e913` |

**Tags.** [source] read in code or metadata; for run-time behaviour a source trace is a hypothesis.
[corpus] read in shipped process metadata. [doc] product or repository documentation. [measured <date>] observed
on stand `Creatio` or computed over files on that date. [traced, not measured] a trace through platform internals;
section 10 names the measurement (EM id) that settles it. [inferred] my inference from the cited lines.

---

## 1. Short answer

- **Not buildable today.** The package has no writer for an attachment slot: `SendEmailDescriptor` declares no
  attachments member (`PB/Contracts/ProcessDescriptorContracts.cs:1634`, DataMembers `:1650-1809`), and nothing in
  `PB` creates `Attachments<N>` [source]. clio refuses `email.attachments` before sending, because its `EmailBlock`
  key set mirrors the bundled contract (`CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:185-193`); a clio
  built before 2026-10-13 has no such check and forwards it (section 9.1) [source].
- **One designer row is one slot of two levels.** An element parameter `Attachments<N>` (CompositeObjectList, In)
  with exactly one nested item `Attachments<N>FileLocator` (FileLocator, In); both levels have `A3 = A4 =` the
  process schema UId (`PD/Schemas/ProcessFilesControlSchema/ProcessFilesControlSchema.js:262-295`), `GS5 =` the
  process UId on every level that gets a value (stamped by `setMappingValue`,
  `TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-schema-parameter.js:840-858, 880-885`),
  and `IL2 =` the Send email element UId; captions and display values live only in resources; there is no `BK15`
  mapping row [source] [corpus].
- **The run time finds a slot by name AND by stamp, and reads rows from the OUTER value.** Anchored, case-sensitive
  `^Attachments\d+$` plus `CreatedInSchemaUId == ` the running process schema UId on the top-level parameter
  (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19, 78-98`); each row is read by the literal key
  `<slot name> + "FileLocator"` (`PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-119, 280-288`)
  [source]. A collection source therefore needs both levels bound, which is what all four corpus bindings store.
- **Several failures are silent** [traced, not measured]: a wrong stamp (slot ignored), a misnamed item (row
  skipped), the feature `UseProcessEmailAttachments` off for the running user (attachments dropped), a source that
  did not run (0 rows), a missing file in automatic mode with a sender, `CreateActivity` off and `IgnoreErrors` true
  (the whole email is not sent and the element completes; with `CreateActivity` on, or in manual mode, the copy to
  `ActivityFile` throws outside `IgnoreErrors` and the element fails, `AutoEmailUserTaskSender.cs:112-116, 176`).
  Section 4.4.
- **Several failures are loud and outside `IgnoreErrors`** [traced, not measured]: a row whose item reads null (an
  item-only binding to a collection item, an unset single File, and by trace an outer-only binding) fails the
  element with a NullReferenceException while the message is built. Section 4.4.
- **All four mode combinations share one attachment path.** Manual and automatic, custom and template message all
  build the message in one non-virtual method; manual mode and automatic mode with `CreateActivity` copy the files
  to `ActivityFile` rows of the email activity, so they are observable without a real delivery (section 4.3)
  [traced, not measured].
- **Most binding machinery exists.** `PlanFiles` and `FilesBinding` classify a file source and find the file item by
  TYPE; `BindCollection` writes both levels in one plan; P1 binds a parent from an item-of-item mapping; R5 and R6 are
  refusal precedents; `ProcessFileProcessingDescriber` reports `bindingLevels` and issues (section 2.5-2.7)
  [source]. The `TakesFiles` doc comment names this ticket as its next consumer, and also the Creatio.ai slots,
  which the agreed scope excludes (section 2.5) [source].
- **Recipients are not a template to copy.** `Recipient<N>` is created with Direction Variable, no explicit stamp,
  max+1 numbering and a case-insensitive prefix test; attachment slots need Direction In, explicit stamps on the
  nested item, and the designer numbers by lowest free index (section 2.4) [source].
- **Ordering is not enforced anywhere for `email`.** The build-path declaration-order guard does not cover the
  `email` pass; `mappings[]` runs before the `email` pass; `removeElement` checks no reference; nothing in `PB` checks
  that a source lies upstream of its consumer, though `ValidateStructure` already walks the flow graph breadth-first
  on the create path (section 2.8) [source].
- **The only oracle is old and narrow.** The corpus has four one-row `Attachments1` bindings from 2021-2022, all in
  the ProcessTests test package, two sources (Object attachments, Generated report) (section 5) [corpus]. The stand
  has no specimen and no mailbox (section 6) [measured 2026-10-28]. A new designer capture (EM1) is the AC7 oracle.
- **The builder's outer display value already differs from the designer's.** `PB` writes a top-level source's bare
  caption (`Collection of files`); the designer stores `[#<element>.<collection>#]` (section 2.5) [source] [corpus].
  AC7 must name it or fix it.
- **Budgets are at the ceiling.** clio create and modify descriptions have about 2-3 bytes free; KB
  `process-modeling.md` about 1 byte; the KB sentence "Send email attachments ... are not buildable here yet" is
  pinned by a test (section 9) [inferred] [source].

---

## 2. Package today (PB)

### 2.1 Routes of the `email` block

| Route | Where the block lives | Dispatch | When it runs |
|---|---|---|---|
| build (`create-business-process`) | `elements[].email` (`PB/Contracts/ProcessDescriptorContracts.cs:110-111`) | `ProcessBuildHandler.ApplyDeclarativeContent` (`PB/Design/ProcessBuildHandler.cs:557-561`) | after the graph is built and every element configured (`PB/Graph/ProcessGraphBuilder.cs:238-243`); after `mappings[]` (`ProcessBuildHandler.cs:553-556`); before the filter pass (`:576-580`) and the formula pass (`:582-596`) [source] |
| modify `addElement` | `element.email` | `AddElementOperation.Apply` (`PB/Operations/ElementOperations.cs:68-69`) | right after the element is placed, before `ConfigureElement` (`:126`) [source] |
| modify `setElement` | `elementUpdate.email` | `SetElementOperation.Apply` (`ElementOperations.cs:330-331`) | after readData / changeData / addData / deleteData / openEditPage / fileProcessing of the same operation (`:311-329`) [source] |
| userTask fallback | `{"type":"userTask","userTaskName":"EmailTemplateUserTask","email":{...}}` | the same applier; the guard keys on the element's task schema, not on the descriptor token (`PB/Contracts/ProcessDescriptorContracts.cs:102-108`) | as above [source] |

- `SendEmailApplier` holds `UserConnection`, `SendEmailElementIdentity`, `IBodyMacroResolver`,
  `IUserTaskPerformerApplier` (`PB/Email/SendEmailApplier.cs:32-35, 46-52`); DI at
  `PBR/packages/CrtProcessBuilder/Files/src/CrtProcessBuilderApp.cs:219`. It has no `IProcessMappingService`, no
  notice sink and no feature reader [source].
- The contract doc of `email` still says "The email-template path is out of scope"
  (`PB/Contracts/ProcessDescriptorContracts.cs:108`), false since template mode shipped [source].
- Request bounds: nested lists `email.to/cc/bcc` are deliberately NOT bounded (`PB/Design/ProcessBuildHandler.cs:716-723`);
  an attachments list would join them [source] [inferred].

### 2.2 `SendEmailDescriptor` today

Fields (DataMembers `PB/Contracts/ProcessDescriptorContracts.cs:1650-1809`) [source]:
`messageSource`, `template`, `templateEntity`, `body`, `bodyFormat`, `mode`, `sender`, `subject`, `to`, `cc`, `bcc`,
`importance`, `ignoreErrors`, `performer`. No attachments member.

| Value-source vocabulary already in the contract | Members | Evidence |
|---|---|---|
| `templateEntity` | `processParameter` · `sourceElement` + `sourceElementParameter` · `expression` | `ProcessDescriptorContracts.cs:1878-1895` |
| recipients `to/cc/bcc` | `value` · `processParameter` · `expression` (+ `referenceSchema`); no element source | `:1921` (members `:1925-1941`); exactly-one rule `PB/Email/SendEmailApplier.cs:945-962` |
| `fileProcessing.files` (Process parameter variant) | `processParameter` · `sourceElement` + `sourceElementParameter` · `expression` · `value` (declared only to be refused) | `FileProcessingFilesDescriptor`, `ProcessDescriptorContracts.cs:416-442` |

The word `attachments` already names a SOURCE group: `fileProcessing.attachments` is the Object attachments variant
(`ProcessDescriptorContracts.cs:301-303`; clio key set `CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs`,
`fileProcessing` block) [source].

Apply order (`PB/Email/SendEmailApplier.cs:59-144`): trim name, bodyFormat, no-field check, resolve element by name
or UId, Send email identity, body and subject validation, `PlanMessageMode` (all template refusals), then the first
writes: mode content, subject, mode, sender, importance, ignoreErrors, performer, To, Cc, Bcc (`:140-143`). Sender,
performer and recipient refusals fire after earlier writes; harmless because a modify aborts before save and a
build rolls back [source] [inferred].

### 2.3 `EnsureConfiguresSomething`

`PB/Email/SendEmailApplier.cs:203-216` refuses a block that sets none of the listed fields, and its message lists
them. An attachments-only block would be refused as "specifies no field to apply" until the list counts it [source].

### 2.4 `Recipient<N>` today versus what an attachment slot needs

| Aspect | `Recipient<N>` in `PB` | Attachment slot (designer, run time, corpus) |
|---|---|---|
| Creation | `new ProcessSchemaParameter(schema) { UId, Name, Caption, ContainerUId = element.UId }` (`PB/Email/SendEmailApplier.cs:881-886`) [source] | two levels; item added to `ItemProperties` (`PD/Schemas/ProcessFilesControlSchema/ProcessFilesControlSchema.js:254-295`) [source] |
| Direction | not set, so `Variable` (`TS/Terrasoft.Core/Process/ProcessSchemaParameter.cs:412`); corpus `Recipient1` has no `L12` [source] [corpus] | `In` on both levels: designer `direction: IN` (`ProcessFilesControlSchema.js:271, 288`); corpus `L12: 0` on both levels [source] [corpus] |
| `CreatedInSchemaUId` / `ModifiedInSchemaUId` | not set; back-filled by `MetaItemCollection.InsertItem` when the parameter joins `element.Parameters` (`TS/Terrasoft.Core/MetaItemCollection.cs:91-104`) [traced, not measured] | explicit on both levels in the designer (`ProcessFilesControlSchema.js:267-268, 284-285`). The nested item is NOT back-filled when it joins `ItemProperties` before the parent joins: `new ProcessSchemaParameter(ProcessSchema)` uses the obsolete `ProcessSchema` constructor, not `ParentMetaSchema` (`TS/Terrasoft.Core/Process/ProcessSchemaBaseElement.cs:60-63` vs `:69-72`), and the parent's `ParentMetaSchema` setter only propagates the schema to the item list (`ProcessSchemaParameter.cs:326-331`), it does not stamp items already in it [source]. Precedents that stamp explicitly: `ProcessParameterService.StampAsCreatedIn` (`PB/Parameters/ProcessParameterService.cs:872-876`, measured need 2026-10-14 per `:865`), `MultiInstanceApplier.AParameter` (`PB/Elements/MultiInstanceApplier.cs:1027-1043`, stamp `:1037-1038`) |
| `SourceValue.ModifiedInSchemaUId` (`GS5`) | `schema.UId` (`SendEmailApplier.cs:888-891`) [source] | `schema.UId` on both levels [corpus]; every mapping write stamps it (`PB/Mappings/ProcessMappingService.cs:1008`) [source] |
| Type | `SetDataValueType` by UId, resolved object (`SendEmailApplier.cs:970-977`) | `DataValueType.CompositeObjectListDataValueTypeUId` / `FileLocatorDataValueTypeUId` (`TS/Terrasoft.Core/DataValueType.cs:270-271, 276`); `PB` production code already uses both (`PB/Mappings/CollectionBindingRules.cs:140, 144`) [source] |
| Caption | numbered `"To N"` | constant: "Attachments collection" / "Attachment" (`PD/Schemas/EmailTemplateUserTaskPropertiesPage/EmailTemplateUserTaskPropertiesPage.js:381-387`) [source] |
| Numbering | max existing suffix + 1, gaps never refilled (`SendEmailApplier.cs:854-867`); its doc comment claims "the first free N (the designer numbers the same way)" (`:791`), which is false [source] | designer: lowest free index from 1, gaps refilled (`PD/Schemas/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:465-479`, called at `ProcessFilesControlSchema.js:244-249, 260`); run time: any number, gaps irrelevant (regex only) [source] |
| Slot test | `StartsWith(prefix, OrdinalIgnoreCase)` + `int.TryParse(suffix)` (`SendEmailApplier.cs:834-838, 858-862`) [source] | anchored, case-sensitive `^Attachments\d+$` (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19, 80-81`; designer `TS/Terrasoft.Nui/Resources/Terrasoft/designers/process-schema-designer/process-constants.js:249`) [source]. `int.TryParse` also accepts a sign or surrounding spaces the regex refuses [inferred] |
| Idempotency | skip an entry whose resolved `Source` and `Value` equal an existing slot's, ordinal compare (`SendEmailApplier.cs:830-848`) | replace: a slot whose resolved source equals a sent entry is kept as it is, the rest are removed (decisions Q4, agreed 2026-10-28) |
| Removal | none: match-or-append (`SendEmailApplier.cs:789-818`); guidance routes removal to the designer (`KB/guidance/mcp/guides/processes/send-email.md:153-157`) [source]. The package deletes no `SysLocalizableValue` row anywhere: every parameter removal is a bare `Parameters.Remove` (`PB/Parameters/ProcessParameterService.cs:559-568`, `PB/AccessRights/ChangeAccessRightsApplier.cs:531-545`), and resource keys are built from element and parameter NAMES (`TS/Terrasoft.Core/Process/ProcessSchemaParameter.cs:900-930`); whether a save drops a removed slot's keys is open (OF-23) [source]. A parameter-level dependency lookup exists: `IProcessDependencySnapshot.FindDependents(element, parameter)` (`PB/Graph/IProcessDependencySnapshot.cs:55-58`) [source] | designer: clearing a row's value and saving deletes both levels when `canRemoveParameter` allows it (section 3) [source] |

### 2.5 File-source machinery the slot can reuse

| Piece | What it does | Evidence |
|---|---|---|
| `ProcessFileProcessingBinder.PlanFiles` | Classifies the `files` source before any write: refuses `value` (`:136-141`), "names no source" (`:144-149`), more than one source, the element's own parameter (`:169-173`), a nested collection (`:176-179`), something that is neither a file nor a collection of files (`:183-187`), a file item more than one level deep (`:191-193`). A collection, or a dotted item of one, becomes `FromCollection` (both levels); a top-level File becomes `FromSingleFile` (item only); an expression `FromExpression` (item only, outer cleared, notice) | `PB/FileProcessing/ProcessFileProcessingBinder.cs:130-195` [source] |
| `FileItemOf` | Finds the collection's ONE FileLocator item by TYPE, never by name; refuses none or several | `:210-226` [source] |
| `FilesBinding` | `FromCollection` writes `BindCollection(outer, [(File, <source item>)])` (`:292`); `FromSingleFile` writes `ApplyMapping` on `Files.File` (`:299`; P3 clears a bound parent, with a notice); `FromExpression` clears the outer and writes the item (`:305`). All three are the raw (`fromBlock = false`) forms (section 2.6) | `:253-318` [source] |
| Measured precedent M1 | the item-only binding from a single File copied one file on the Process file element (2026-10-19) | doc comment `:27-29` [measured 2026-10-19, Process file element only] |
| `IProcessMappingService.BindCollection` | Both levels in one plan; pairs written as given, type-checked, no notice; R-M2 still applies to a file-consuming outer target; the doc names "later, element variants and attachment slots" | `PB/Mappings/IProcessMappingService.cs:74-96` [source] |
| Rules P1, P2, P2-MI, P3, R-M1, R-M2 | P1 binds a parent from an item-of-item mapping; P2 pairs file items for a file-consuming target; P3 resets a bound parent when an item takes a non-item source; R-M1 refuses a single File target from a collection item; R-M2 refuses a file-consuming target from a collection with no FileLocator item | `IProcessMappingService.cs:29-53`; `CollectionBindingRules.Evaluate` `PB/Mappings/CollectionBindingRules.cs:70-101` [source] |
| R-M3 | a source collection nested in another one reads empty outside a row; refused | `CollectionBindingRules.cs:87-88, 393-403` [source] |
| `TakesFiles` | true only for the Process parameter variant's `Files` input and a process-level FileCollection shape; so P2 and R-M2 do not run for an attachment slot today | `CollectionBindingRules.cs:173-181` [source] |
| `TakesFiles` doc comment | "the email and Creatio.ai attachment slots join this set with ENG-95985 Send email attachments" | `CollectionBindingRules.cs:164-168` [source]. The agreed scope puts the Creatio.ai call OUT of scope: D22 (`CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-decisions.md:1573-1610`) and Q5 "Agreed 2026-10-07" (`CLIO/spec/eng-92719-file-processing-element/README.md:412`) [doc] |
| PU-42 | an arbitrary user task's collection with a FileLocator item is not file-consuming; the test uses a random task UId, so a Send-email-only predicate keeps it green | `PBT/FileCollectionMappingTests.cs:1682` [source] [inferred] |
| Write primitive | `FillParameterSourceValue`: Source Script before Value, `[#<meta path>#]`, display value, `GS5 = schema.UId` | `PB/Mappings/ProcessMappingService.cs:947-953, 1008`; `MetaPathFormat` `PB/ProcessDesignConstants.cs:23` [source] |
| Display value | a NESTED source gets the designer form `[#<element>.<collection>:<item>#]`; a TOP-LEVEL source keeps its bare caption | `ProcessMappingService.cs:963-972`; pinned by `PBT/MappingNestedDisplayValueTests.cs:74-77, 111-114` [source] |

**The outer display value differs from the designer.** For a slot bound to `OF1.ObjectFiles`, `PB` would store the
outer display value `Collection of files` (bare caption, top-level source), while the designer stores
`[#PF.Collection of files#]` (`PS/ProcessTests/branches/7.8.0/Resources/ProcessFileSendMail.Process/resource.en-US.xml:15`;
`[#Report.Collection of reports#]` at `:91`). The item's form matches (`:13`). The same divergence exists today on the
Process file element's `Files` level, where the ENG-92719 comparison rule says displayed values must match
(`CLIO/spec/eng-92719-file-processing-element/eng-92719-file-processing-element-serialization-capture.md`, row N13)
while the test pins the bare caption [source] [corpus] [inferred].

### 2.6 Refusal precedents for raw routes

| Id | Refusal | Evidence |
|---|---|---|
| R6 | a raw mapping onto one of the 13 block-owned parameters of a Process file element is refused, naming the block member that sets it | `EnsureNotABlockOwnedFileParameter`, `PB/Mappings/ProcessMappingService.cs:416-435`, called at `:142` [source] |
| R5 | a mapping FROM the Process parameter variant's `Files` input is refused | `EnsureNotReadFromAFilesInput`, `ProcessMappingService.cs:871-889`; `CollectionBindingRules.IsFilesInputOf` `:189-191` [source] |
| Resolver | "Unknown or non-parametrized process element 'X'." / "Process parameter 'X' was not found." / "Element 'X' has no parameter 'Y'..." | `PB/ProcessSchemaElementLocator.cs:64, 251, 267` [source] |

The raw and the block routes share one planner: `ApplyMapping`, `ApplyBlockMapping` and `BindCollection` all plan their writes in
`ProcessMappingService.Plan` (`PB/Mappings/ProcessMappingService.cs:61-86, 133-198`); `fromBlock` is the only switch
(`ApplyBlockMapping` passes `true`, `:73`), and `BindCollection` plans with `fromBlock = false` (`:85`).
`BindCollectionItems` does not go through `Plan`; it resolves its source through `ResolveStructuredSource`
(`:89-109`, `:896`) [source].

A raw `addMapping` onto an existing slot, today: `Attachments1 <- OF1.ObjectFiles` writes the outer level only, with
no pairing and no notice (TakesFiles false); `Attachments1.Attachments1FileLocator <- OF1.ObjectFiles.File` binds
both levels through P1 [source, by `CollectionBindingRules.cs:78-101`] [traced, not measured].

### 2.7 Describe paths

| Path | Today | Evidence |
|---|---|---|
| Generic element `parameters[]` | lists a parameter whose own value, or any item's value, was set in this schema (`Source != None` and `GS5 == schema.UId`); a designer-made bound slot is listed, an untouched one is not | `PB/Describe/ProcessDescriber.cs:197-250` [source] |
| Name decoding | `DecodeElementSource` reports `sourceElement` + `sourceElementParameter` (dotted for an item) only when `ReappliesUnchanged` says re-sending writes nothing else; for a both-level slot both levels decode today [traced, not measured] | `ProcessDescriber.cs:314-315`; `CollectionBindingRules.cs:123-133` [source] |
| `email` block read-back | `SendEmailElementHandler.Describe` fills mode, message, sender, recipients, performer; `configured` does not count attachments; `DescribeSendEmailInfo` has no attachments member | `PB/Elements/SendEmailElementHandler.cs:105-172` (`configured` `:165-168`); `PB/Contracts/DescribeContracts.cs:639` [source] |
| `DescribeRecipients` | `<prefix><digits>` by `StartsWith` + `TryParse`, skips a parameter whose OWN source is None, projects with `ToDescribeParameter` and no name decoding, orders by suffix | `SendEmailElementHandler.cs:234-255` (None skip `:246-248`) [source]. A copy would skip an item-only slot and report formulas, not names [inferred] |
| Template for a faithful read-back | `ProcessFileProcessingDescriber.Describe` over `ProcessFileProcessingFilesBinding.Read`: `bindingLevels` (both / outerOnly / itemOnly / none), one decoded source, issues for outer-only, mismatched levels, an outer formula, item-only onto a collection item (measured NRE, 2026-10-26), nested sources | `PB/FileProcessing/ProcessFileProcessingDescriber.cs:32-72, 181-188`; `PB/FileProcessing/ProcessFileProcessingFilesBinding.cs:44-48`; tokens `PB/ProcessDesignConstants.cs:1956-1965`; `DescribeFileProcessingInfo.bindingLevels` `PB/Contracts/DescribeContracts.cs:2333` [source]. Names and issue texts hard-code `Files` / `File` [source] |

### 2.8 Ordering and dependency guards

- **Declaration-order guard.** `RefuseForwardReference` refuses a configuration block that binds a value to an element
  declared LATER in `elements[]`; its message says such a value "would arrive empty"
  (`PB/Graph/ProcessGraphBuilder.cs:370-381`, message `:458-462`). It runs only for blocks in
  `WritesAConfigurationBlock`: readData, changeData, openEditPage, addData, deleteData, fileProcessing (`:452-455`).
  The shared core `RefuseForwardReferenceIn` (`:397-430`) is reused by the formula pass
  (`PB/Design/ProcessBuildHandler.cs:594` -> `RefuseForwardReferenceFromFormula` `:644`, which calls it at `:659-660`)
  [source].
- **Scope.** The guard scans `node.Parameters[*].SourceValue.Value`, top level only (`ProcessGraphBuilder.cs:377-380`).
  A both-level slot carries the element reference on the outer level and would be seen; a single File taken from an
  element output is bound on the item only and would not [source] [inferred]. Reading `SourceValue.Value` resolves
  the type and can throw for an unresolvable one, so any walk must stay scoped (`:432-447`) [source].
- **The `email` pass is not guarded.** `ProcessBuildHandler.cs:557-561` applies `email` with no forward-reference
  check, so a `templateEntity.sourceElement` naming a later element builds green today [source] [inferred].
- **Phase order.** `mappings[]` runs before the `email` pass (`ProcessBuildHandler.cs:553-561`), so a build-path raw
  mapping cannot target a slot the same build creates. The formula pass was moved after `email` for exactly this
  reason with `Recipient<N>` (`:582-587`) [source].
- **Modify path.** Operations apply in order; a source not yet added fails as unknown; a source that exists but sits
  later on the flow is accepted [source] [inferred].
- **No check in `PB` that an attachment's SOURCE lies upstream of its consumer.** The only flow traversal is
  `ValidateStructure`'s reachability from a start and to an end (`PB/Graph/ProcessGraphBuilder.cs:2228-2238`), a
  breadth-first `Traverse` over the adjacency maps (`:2250`); it runs on the create path only (`:268-270`) [source].
- **`removeElement` checks no reference.** `ProcessGraphBuilder.RemoveElement` removes the flows, the schema
  `Mappings` rows whose TARGET meta path names the element (`:1910-1917`), and the element
  (`PB/Graph/ProcessGraphBuilder.cs:1882-1918`); rows and values that READ from it are kept; `RemoveElementOperation`
  adds nothing (`PB/Operations/ElementOperations.cs:188-203`). A slot bound to the removed source keeps a dangling
  Script [source]. The dependency scanner does walk nested items (`PB/Graph/ProcessElementDependencyScanner.cs:285`),
  and a Process file variant switch is refused while readers exist (`PB/Operations/FileProcessingConfigApplier.cs:72-73`)
  [source].

### 2.9 Mode and message switches

- One element: `EmailTemplateUserTask` (`PB/ProcessDesignConstants.cs:634`). "How is the message sent?" is
  `SendEmailType` "0" auto / "1" manual (`:795, 816, 819`); "What is the message?" is `BodyTemplateType` "1" custom /
  "0" template (`:741, 747, 758`) [source].
- Neither switch touches any dynamic slot in the applier [source]. A sender given as an email address must match
  `MailboxSyncSettings.SenderEmailAddress` (`PB/Email/SendEmailApplier.cs:680-690`); a GUID or lookup macro is stored
  as the record id unchecked (`:653-657`) [source].
- Multi-instance in `PB` is a Sub-process feature only: `IsMultiInstance` is `owner is ProcessSchemaSubProcess &&
  MultiInstanceOptions != null` (`PB/Mappings/CollectionBindingRules.cs:194-195`), and `multiInstanceOptions` is
  refused on any other element (`PB/Elements/MultiInstanceApplier.cs:1051-1058`) [source]. A multi-instance Send
  email cannot be built. The designer CAN make one (section 3); `IsMultiInstance` answers false for it, so mappings
  onto such an element get the single-instance rules [source]. Decisions Q16 (agreed 2026-10-28): `email.attachments`
  on it is refused.

### 2.10 An older package drops an unknown member silently

"No contract in this package implements `IExtensibleDataObject`, so an environment running an older package would
silently DROP an unknown ... member" (`PB/Contracts/VersionContracts.cs:23-26`); clio's `EmailBlockExpectation`
exists for the same failure (`CLIO/clio/Command/ProcessModel/EmailBlockExpectation.cs:8-14`) [source]. A package
without the member drops `email.attachments`: a block that also sets another field answers success and creates no
slot; an attachments-only block is refused by `EnsureConfiguresSomething` as "specifies no field to apply"
(`PB/Email/SendEmailApplier.cs:203-216`, section 2.3), in a message whose field list does not mention attachments
[source].

### 2.11 Notices and the feature seam

- The applier emits no notice. End-of-request Send email notices come from `ElementConfigurationNotices.DescribeSendEmail`
  ("no message", "automatic with no recipient"; `PB/Elements/ElementConfigurationNotices.cs:33, 223, 227`) [source].
- A mapping that reads a Process file element hands it to the ledger (`ProcessMappingService.cs:235-239`); the
  consumer rules return nothing for Object and Process parameter sources
  (`PB/FileProcessing/ObjectFileProcessingNoticeRule.cs:62-64`, `ProcessFileProcessingNoticeRule.cs:24-26`) and the
  Report rule warns only when `ReportFiles` feeds a process output (`ReportFileProcessingNoticeRule.cs:53-56, 79-88`).
  No notice fires for a Send email consumer [source].
- `PB` never reads `UseProcessEmailAttachments` (search) [source]. A feature-reader seam exists:
  `FileProcessingFeatureReader` reads another feature (`EnableReportFileProcessingUserTask`) through the
  context-free `Features.GetIsEnabled(code)` and answers "on" when the read fails
  (`PB/FileProcessing/FileProcessingFeatureReader.cs:14-36`, call `:29-31`). A request without a user gets the
  feature's `DefaultState` (`TS/Terrasoft.Core/FeatureToggling/DbFeatureProvider.cs:810-827`), read from the
  `Feature.DefaultState` column (`:545`), which the CrtProcessDesigner data does not seed for
  `UseProcessEmailAttachments`; the package enables it through `AdminUnitFeatureState` rows (section 4.5), which a
  context-free read does not see [source]. On stand `Creatio` the `Feature` row nevertheless has `DefaultState` = 1,
  set by the platform, so a context-free read answers on there (section 6) [measured 2026-10-28]. The
  per-user overload is `Features.GetIsEnabled(code, userConnection.CurrentUser.Id)`
  (`TS/Terrasoft.Core/Configuration/ConfigurationFeature.cs:53-56`); in a build it reads the BUILDING user, not the
  user who will run the process. The run time asks with the executing user's connection
  (`PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:208`) [source] [inferred]. No reader is added:
  decisions Q9 (agreed 2026-10-28) drops the build-time check, on the toggle's history (section 4.5).

### 2.12 Test harness traps

| Trap | Evidence |
|---|---|
| `SendEmailApplierTests.CreateSchema()` builds `new ProcessSchema(...)` with `UId == Guid.Empty`, so a `CreatedInSchemaUId == schema.UId` assertion there passes vacuously; `FileProcessingTestSupport.CreateSchema` sets a real UId | `PBT/SendEmailApplierTests.cs:47`; `PBT/FileProcessingTestSupport.cs:222-228` [source] |
| No Send email test asserts `CreatedInSchemaUId` today | search over `PBT/SendEmail*.cs` [source] |
| `SetupEmailTaskSchema()` must run per test in `[SetUp]` | `PBT/SendEmailApplierTests.cs:44-45` [source] |
| CI compiles and tests against core 8.3.4 (`DEFAULT_VERSION`); the local main checkouts carry core-bin 10.1.37 (`Terrasoft.Core.dll` FileVersion); a fresh package worktree has no `.application` | `PBR/docs/ci-cd.md:175-177` [doc]; [measured 2026-10-28] |
| `DataValueType.FileLocatorDataValueTypeUId` and `CompositeObjectListDataValueTypeUId` exist on 8.3.4: production `PB` code references both and CI compiles it there | `PB/Mappings/CollectionBindingRules.cs:140, 144` [source] [inferred] |
| Existing suites to mirror: `SendEmailApplierTests` (29 tests), `SendEmailElementHandlerDescribeTests` (8), `FileCollectionMappingTests` (79) | `[Test]`/`[TestCase` count [measured 2026-10-28] |

---

## 3. Designer (PD)

| Fact | Evidence | Tag |
|---|---|---|
| The control is the generic `ProcessFilesControlSchema`, hosted by `RootUserTaskPropertiesPage` in `FilesControlContainer`, visible through `getIsFileControlEnabled` (base `false`) | `PD/Schemas/RootUserTaskPropertiesPage/RootUserTaskPropertiesPage.js:779, 805-813` | source |
| Send email enables it by feature `UseProcessEmailAttachments`; filter regex `EMAIL_ATTACHMENT_PARAMETER_TEMPLATE`; config prefix `Attachments`, captions "Attachments collection" / "Attachment" | `PD/Schemas/EmailTemplateUserTaskPropertiesPage/EmailTemplateUserTaskPropertiesPage.js:365-367, 373-375, 381-387` | source |
| Shown in all four mode combinations: the diff moves `FilesControlContainer` into `UserTaskContainer`, outside `ManualSendContainer` and `AutoSendContainer` | `EmailTemplateUserTaskPropertiesPage.js:2348, 2392, 2515-2520` | source |
| Creation config: outer `Attachments<N>` COMPOSITE_OBJECT_LIST, IN, source None, `createdInSchemaUId = modifiedInSchemaUId =` process UId, one item `<name>FileLocator` FILE_LOCATOR, IN, source None, same stamps, `skipValidation` and `forceRemove` true | `PD/Schemas/ProcessFilesControlSchema/ProcessFilesControlSchema.js:254-295` | source |
| `skipValidation` and `forceRemove` are not serialized, so they hold only in the creating session | `TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-schema-parameter.js:619-625` | source |
| Name = lowest free `Attachments<k>` from k = 1 over the element's parameters (every parameter in `processElement.parameters`, whatever its `createdInSchemaUId`, exact name compare); gaps are refilled; numbering is per element | `ProcessFilesControlSchema.js:244-249, 259-260`; `PD/Schemas/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:465-479`; corpus: two Send email elements of one process each carry `Attachments1` | source, corpus |
| Rows on open: parameters with `getIsDynamic() && !hasParent() && regex`; the row is the FIRST nested item (`$ItemProperties.first()`) | `ProcessFilesControlSchema.js:218-228, 308-324` | source |
| `getIsDynamic()` = `createdInSchemaUId === parentSchema.uId` (false inside a user-task schema) | `process-schema-parameter.js:1019-1025` | source |
| A new row is added with `processElement.parameters.add`, so it lands after existing parameters | `ProcessFilesControlSchema.js:233-239` | source; position after a refill is [inferred], EM1 |
| On value change `_onParameterValueChanged` copies the picked value's `dataValueType` and an ad-hoc `source` field onto the item. The picked value carries the item's OWN type (`ProcessParameterSelectionPage.js:169`; `MappingEditMixin.js:492, 624`), `source` is not a serialized property (`process-schema-parameter.js:619-630`; the stored source comes from `setMappingValue`, `:846`), and `saveExtendedValue` re-sets the type from the view model (`ProcessFlowElementPropertiesPage.js:1029, 1034`). Source predicts `L1` = FileLocator for every source kind | `ProcessFilesControlSchema.js:340-346` | source; [traced, not measured], EM1 |
| Save: every row (child view model) gets `setMappingValue`; an EMPTY row removes the whole outer parameter (both levels) when lazy parameters are loaded and `canRemoveParameter` allows it; no per-row delete button | `ProcessFilesControlSchema.js:376-387, 392-405, 410-419`; save skipped when the feature is off `RootUserTaskPropertiesPage.js:387-388` | source |
| Parent binding: when the item is bound to an item of a collection, `setParentMappingEditValue` -> `_processParentFormulaParameter` -> `_processMappingToCollectionItem` sets the PARENT to a mapping of the source collection | `PD/Schemas/MappingEditMixin/MappingEditMixin.js:895-905, 947-963, 981-996, 1101-1115, 1132-1156` | source |
| Single File (not a collection item): `_processMappingToItem` fires `collectionMappingReset` only; the handler converts multi-instance elements only, so the parent stays as it was: None on a fresh row, a STALE collection binding after a re-bind | `MappingEditMixin.js:1075-1096`; `PD/Schemas/ProcessFlowElementPropertiesPage/ProcessFlowElementPropertiesPage.js:847, 856-867` | source; run-time effect EM1d, EM8 |
| The item picker offers: on the ELEMENT tab, items of element output collections (`ObjectFiles.File`, `ReportFiles.File`, `ObjectFiles.ObjectFile`) and single FileLocator outputs; a whole element collection is listed but cannot be selected (`getParameterSelectedValue` returns null, `ProcessElementParametersMappingPage.js:532-538, 880-883`), and `In` and `Internal` element parameters (such as the Process parameter variant's `Files` input) are dropped (`:506-509, 654`). On the PROCESS tab: a FileLocator process parameter, an item of a process FileCollection, AND the process FileCollection itself, with no type check on selection (`PD/Schemas/ProcessParametersMappingPage/ProcessParametersMappingPage.js:85-96`; the collection row is listed when its item is valid, `BaseParametersMappingPage.js:438-470`); that last pick binds the item to the collection and leaves the outer unbound (a top-level parameter has no parent, so `_processMappingToItem` runs, `MappingEditMixin.js:947-963, 1075-1096`). Also formulas | `PD/Schemas/ProcessElementParametersMappingPage/ProcessElementParametersMappingPage.js:506-509, 532-538, 880-883`; `BaseParametersMappingPage.js:438-470, 545-554, 677-701` | source; the stored shape of the process-tab collection pick [traced, not measured], EM1 |
| With 2+ rows bound to collection items in one session, a re-bound row whose outer is already bound is offered on the element tab only the items of that same collection (`_nestedMappingValidator`, `ProcessElementParametersMappingPage.js:514-527`; `MappingEditMixin.js:857-884`); top-level sources and the process tab are unaffected. The files control binds no `cleariconclick` handler (`ProcessFilesControlSchema.js:530-553`, unlike `ProcessFileProcessingUserTaskPropertiesPage.js:329`), so the clear icon resets the item only (`TS/Terrasoft.Nui/Resources/Terrasoft/controls/mappingedit/mappingedit.js:338-350`) and a stale parent survives clear-then-rebind as well | as cited | source; [traced, not measured], EM1 |
| Elements AFTER the Send email in the flow are offered; no order filter | `ProcessElementParametersMappingPage.js:258-262` | source |
| No attachment validation on save beyond a red "value required" marker; save always runs; "no upstream source" cannot be expressed (an empty row is dropped) | `ProcessFilesControlSchema.js:360-371, 376-387` (r3 §6) | source |
| The designer can make a Send email MULTI-INSTANCE (checked 2026-10-28): mapping a TOP-LEVEL parameter of any activity (a recipient, say) to an ITEM of a collection fires `collectionMappingSet`, and the base properties page converts the element; `convertToMultiInstance` lives on the client activity class that the client user task extends, and the server keeps `MultiInstanceOptions` on every activity (a user task is a `ProcessSchemaTask : ProcessSchemaActivity`). The conversion MOVES every root parameter into the item properties of `InputRecordCollection` / `OutputRecordCollection` and leaves only those two at the root, so an `Attachments<N>` slot becomes a nested item, which the run time's top-level slot search does not see (section 4.1). Nobody uses it: the corpus holds 61 multi-instance elements (`BP6`), all Sub-processes, no user task | `PD/Schemas/MappingEditMixin/MappingEditMixin.js:981-995, 1029-1035`; `PD/Schemas/ProcessFlowElementPropertiesPage/ProcessFlowElementPropertiesPage.js:846, 875-888`; `TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-activity-schema.js:317-338, 526-535`, `process-schema-usertask.js:3-5`; `TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:22, 122`, `ProcessSchemaTask.cs:11`, `ProcessSchemaUserTask.cs:27`; slot search `TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:78-98` | source; run-time effect [traced, not measured]; corpus [re-counted 2026-10-28 over every `PS/*/branches/7.8.0/Schemas/*/metadata.json`: 61 elements in 40 files, none with a user-task `J6`] |
| No check anywhere for a Process file source with no record scope and no filter (checked 2026-10-28): the Object attachments page validates only that the source object is chosen and that "Read first" is 1-5000 (default 50); its filter panel only OFFERS a `RecordId` condition and never requires one, in either action; the Report page requires a report and asks for confirmation only when the report changes over a non-empty filter; the Send email files control checks only that a row's mapping is not empty; the run time runs an empty filter as "the first N files". The builder's D16 refusal (section 4.5) is therefore a builder-only rule, stricter than the designer | `PD/Schemas/ObjectFileProcessingUserTaskPropertiesPage/ObjectFileProcessingUserTaskPropertiesPage.js:10, 33-37, 353-357, 423-438`; `PD/Schemas/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:489-498, 506-518`; `PD/Schemas/ReportFileProcessingUserTaskPropertiesPage/ReportFileProcessingUserTaskPropertiesPage.js:51-55, 217-230`; `PD/Schemas/ObjectFileProcessingUserTask/ObjectFileProcessingUserTask.cs:94-103` (`RowCount = RecordsToRead`, `:99`) | source |

---

## 4. Run time (PD + TS)

### 4.1 Discovery and reading

| Step | Code | Evidence |
|---|---|---|
| Sender by `SendEmailType`, provider by `BodyTemplateType` | Auto -> `AutoEmailUserTaskSender`, Manual -> `ManualEmailUserTaskSender`; EmailTemplate -> `EmailTemplateUserTaskMessageProvider`, ProcessTemplate -> `EmailProcessTemplateUserTaskMessageProvider` | `PD/Schemas/EmailUserTaskSenderFactory/EmailUserTaskSenderFactory.cs:14-20`; `PD/Schemas/EmailUserTaskMessageProviderFactory/EmailUserTaskMessageProviderFactory.cs:14-18` [source] |
| Message build, shared by both providers | non-virtual `CreateEmailMessage`; `message.Attachments = EmailUserTask.GetEmailAttachments()` when `UseProcessEmailAttachments` is on | `PD/Schemas/EmailTemplateUserTaskSender/EmailTemplateUserTaskSender.cs:206-236` (gate `:227-229`); the providers override only `GetEmailContent` / `GetEmailBody` / `GetSenderEsq` (`EmailTemplateUserTaskMessageProvider.cs:137, 206`; `EmailProcessTemplateUserTaskMessageProvider.cs:21, 25`) [source] |
| Slot discovery | `GetFilteredOwnParametersList("Attachments")`: `ForceGetParameters()` (top level), regex `^Attachments\d+$`, `CreatedInSchemaUId == source.Owner.Schema.UId` | `PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:280-288`; `TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19, 78-98` (check `:94`) [source] |
| Slot value | hard cast `(ICompositeObjectList<ICompositeObject>)GetParameterValue(parameter)` | `EmailTemplateUserTask.cs:284` [source] |
| Row read | `TryGetValue(parentParameterName + "FileLocator", out EntityFileLocator)`; a row without the key is skipped | `EmailTemplateUserTask.cs:107-119` [source] |
| File read, `Feature-UseFileStorageInProcessUserTasks` on (default true) | `GetFileFactory().WithRightsDisabled().Get(locator)` | `EmailTemplateUserTask.cs:77-89`; `TS/Terrasoft.Core/GlobalAppSettings.cs:2031, 2706-2707` [source] |
| File read, flag off (legacy) | entity fetch of `Name`, `Data` with `UseAdminRights = false` (no rights check on `Entity`, `TS/Terrasoft.Core/Entities/Entity.cs:976, 2189`); not fetched -> skipped | `EmailTemplateUserTask.cs:87-104` [source] |
| Order | slots in parameter-collection order, then rows in collection order | `ProcessUserTaskExtensions.cs:89-97`; `EmailTemplateUserTask.cs:283-286` [source] |
| Duplicates | the same file in two slots gives two attachments, each with a fresh `Guid` Id | `EmailTemplateUserTask.cs:79-83` [source] |

### 4.2 What a slot reads, per stored shape [traced, not measured]

`GetCompositeObjectListValue` -> `TryGetListValue` -> `CreateListItem`
(`TS/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:421-429, 441-469, 471-489`).

| Shape | Rows the email gets | Evidence |
|---|---|---|
| A. Outer bound to a collection, item bound to its item (every corpus binding) | one row per source row, each item evaluated in that row | `:454-458, 480-487` |
| B. Outer None, item bound to a single File | one row built outside any row context | `:443-452` |
| C. Outer None, item None | 0 rows, empty list (not null) | `:443-453, 468` |
| D. Outer bound, item None (outer-only) | rows with `{<slot>FileLocator: null}` (default value of the item type) | `:483-486` |
| E. Outer None, item bound to an item of a collection (item-only) | one row with a null locator (an item read outside its row); measured on the Process file element 2026-10-26 | `:450-451`; `PB/FileProcessing/ProcessFileProcessingDescriber.cs:181-187` [measured 2026-10-26, Process file element] |
| F. Outer bound, value not enumerable and not null | `TryGetListValue` returns false, the value is `null`, then `foreach` over null | `:459-466, 428`; `EmailTemplateUserTask.cs:109, 284-285` |
| G. Outer bound, source value null (source not run) or empty | 0 rows, no error | `:454-468` |

### 4.3 Manual, automatic, template, custom

| | Automatic, `CreateActivity` false (default) | Automatic, `CreateActivity` true | Manual |
|---|---|---|---|
| Code | `HtmlEmailMessageSender.Send` inside `TrySendEmailWithRetry` (`PD/Schemas/AutoEmailUserTaskSender/AutoEmailUserTaskSender.cs:183-187`) | `CreateActivityEntity(message, false)` then send inside `TrySendEmailWithRetry` (`:165-181`, create `:176`), only when the element holds no fetchable `ActivityId`; a re-executed element re-sends its stored Activity with message null and copies nothing (`:120-133, 168-174`), and that email takes its attachments from the stored activity (`PS/CrtEmailSender/branches/7.8.0/Schemas/EmailSender/EmailSender.cs:975, 1042-1050`) | `CreateActivity`: `CreateActivityEntity(message)`, or with `UseProcessPerformerAssignment` off `ObsoleteCreateActivity` + `AddEmailAttachments` (`PD/Schemas/ManualEmailUserTaskSender/ManualEmailUserTaskSender.cs:56-70`) |
| Files go to | the outgoing message only | `ActivityFile` rows (`AddEmailAttachments`, `EmailTemplateUserTask.cs:207-222`, called at `:327`), then the send | `ActivityFile` rows on the draft email activity; the user sends later from the email page |
| Observable without real delivery | no | yes: `ActivityFile` rows exist before the send fails | yes |
| Empty sender (`message.From`) | `SendEmailWithDefaultSender`: no activity; in the current integration no try/catch, so `IgnoreErrors` is not applied (`AutoEmailUserTaskSender.cs:68-75, 105-108`) | the same path, taken before `CreateActivity` is read (`:105-113`) | n/a |

- Defaults: `IgnoreErrors` True, `CreateActivity` false (`PD/Schemas/EmailTemplateUserTask/metadata.json:594-600, 665-671`) [source].
- `AddEmailAttachments` saves the activity before copying (`EmailTemplateUserTask.cs:212`); the file-storage copy sets
  `ActivityId` and `Name` explicitly (`:121-129`), and the file API then stores `TypeId` = File
  (`529bc2f8-0ee0-df11-971b-001d60e938c6`) on every new file (`TS/Terrasoft.File/Metadata/EntityFileMetadataStorage.cs:27, 230-237`)
  and copies every other loaded source column the target also has (`TS/Terrasoft.File/File.cs:172-185, 249-251`;
  unknown columns skipped, `EntityFileMetadataStorage.cs:136-142`); the legacy copy sets `TypeId` = File itself
  (`:138`) [source, traced].
- Re-execution: `activityExists` is computed before either branch (`ManualEmailUserTaskSender.cs:165-168`). With
  `Feature-ResetParameterValuesBeforeReExecuteProcessElement` on (default off, `TS/Terrasoft.Core/GlobalAppSettings.cs:2126, 2908-2910`)
  and the element in Error, an empty `ActivityId` with no activity yet calls `CreateActivity` at `:173` and again at
  `:177`; with an activity already created but no `ActivityId`, `:177` still creates another. Each call copies the
  attachments again. Out of scope; the guidance must not promise a single activity on retry [traced, not measured].
- Template versus custom: one routine, no attachment difference [source, traced, not measured].

### 4.4 Errors and `IgnoreErrors` [traced, not measured]

| Failure | Where it throws | `IgnoreErrors` true | false |
|---|---|---|---|
| null locator (shapes D, E; an unset or empty single File in shape B) | `ReadEmailAttachment(null)` -> `FileFactory.CreateFileInstance` -> `fileLocator.GetType()`, while the message is built (`AutoEmailUserTaskSender.cs:98`; `ManualEmailUserTaskSender.cs:58`); `CompositeObject.TryGetValue` returns true with null when the key exists | element fails (outside every guard) | element fails |
| locator that is not an `EntityFileLocator` | hard cast in `CompositeObject.TryGetValue` | element fails | element fails |
| outer value not enumerable (shape F) | `foreach` over null in `FillEmailAttachments` | element fails | element fails |
| missing file record, automatic, sender set, `CreateActivity` false | first read of `Name` / `Data` during the send, inside `TrySendEmailWithRetry` (`AutoEmailUserTaskSender.cs:135-150`) | swallowed: the email is NOT sent and the element completes | rethrown, element fails |
| missing file record, automatic with `CreateActivity`, or manual | `SaveEmailAttachment` copy, outside `TrySendEmailWithRetry` | element fails, a saved Activity is left behind | element fails |
| no recipients, automatic | `AutoEmailUserTaskSender.cs:99-104` | returns, nothing sent | throws |
| `UseProcessEmailAttachments` off for the running user | gates at `EmailTemplateUserTaskSender.cs:227` and `EmailTemplateUserTask.cs:208` | attachments silently absent | same |
| slot `CreatedInSchemaUId` is not the running process | `ProcessUserTaskExtensions.cs:94` | slot silently ignored | same |
| item misnamed | `EmailTemplateUserTask.cs:110-113` | row silently skipped | same |
| legacy read cannot fetch the record | `EmailTemplateUserTask.cs:98-101` | file silently skipped | same |

Evidence for the null path: `TS/Terrasoft.Common/CompositeObject.cs:120-127`; `TS/Terrasoft.File/FileFactory.cs:44-45, 70-75`
[source]. Missing-record path: `TS/Terrasoft.File/File.cs:66-69, 90-93` (metadata is lazy, `:124-133`; the first
`Name` read throws `FileNotFoundByLocatorException`); `PS/CrtBase/branches/7.8.0/IntegrationApi/MailSenderDTO/EmailAttachment.cs:28-34, 49-57`
(`Name` / `Data` delegate to the file); send `PS/IntegrationV2/branches/7.8.0/Files/cs/EmailClient.cs:163-175`; copy
`EmailTemplateUserTask.cs:126` [source].

### 4.5 Feature gate, rights, limits

- `UseProcessEmailAttachments` gates the designer control, the message build and the `ActivityFile` copy (section 3;
  `EmailTemplateUserTaskSender.cs:227`; `EmailTemplateUserTask.cs:208`) [source]. The package seeds it through
  `AdminUnitFeatureState` = 1 for "All employees" and "All external users"
  (`PD/Data/AdminUnitFeatureState_UseProcessEmailAttachments/data.json:12, 21, 34, 43`); the `Feature` row carries Code
  and Id only and its descriptor has no DefaultState column (`PD/Data/Feature_UseProcessEmailAttachments/data.json`,
  `descriptor.json`) [source]. The group rows force-update `SysAdminUnit` and `FeatureState` (`IsForceUpdate: true`,
  `PD/Data/AdminUnitFeatureState_UseProcessEmailAttachments/descriptor.json:14-16, 28-30`), so every install of the
  package resets both to on; and a user's state is the HIGHEST over the user's roles (`MAX`,
  `TS/Terrasoft.Core/FeatureToggling/DbFeatureProvider.cs:302-307`; the older path takes the top row by state
  descending, `:355-367`), so a user-level 0 does not switch it off for a member of either group [source]. It can be
  off only for a user in neither group, or after someone edits the group rows, until the next install [inferred].
- **History of the toggle** (researched 2026-10-28 at the owner's request; SVN log of `PS/CrtProcessDesigner`, Jira).
  Added 2020-11-19 (r194794) with the feature itself, task CRM-58692 "Implement attaching files to emails sent by the
  Send email element" (title in Russian; epic ENG-64351, a process element for files: copying, reports, email
  attachments), and switched ON 2020-11-25 (r195148) through the two `AdminUnitFeatureState` rows, "All employees" and
  the portal users (renamed "All external users" in r258827, 2023-04-03, ENG-38709 "Rename portal users to external
  users"; the commits name the old keys, RND-38709, RND-10941 and RND-72316, which Jira now resolves to the keys
  given here). Shipped in 7.17.1 (released 2021-01-14) [doc]. A classic rollout toggle [measured 2026-10-28, svn].
  Never switched off or scheduled for removal: no package in `PS` references it outside CrtProcessDesigner (search
  2026-10-28), no data row sets it to 0, and Jira has no issue about it besides ENG-95985 "Send email attachments"
  [measured 2026-10-28]. The checks survive by later refactors, not by a decision: the run-time sender check dates
  from r195004 (2020-11-24, CRM-58692), the activity-copy check was carried into the code of ENG-10941 "Implement the
  run time of activity creation for automatic email sending" (r204435, 2021-06-11), and the designer check was
  rewritten as a `getIsFileControlEnabled` override when the files control became generic for the Creatio.ai element
  (APD-3313 "[2] Ability to add attachments to the BP ExecuteIntentUserTask element (Creation.ai call)", r336217,
  2025-06-03) [measured 2026-10-28, svn log and blame]. Consequence, decisions Q9 (agreed 2026-10-28): no build-time
  check; the guide carries one troubleshooting line (an email without its files: check the feature for the user who
  runs the process).
- Rights: the source file is read and the `ActivityFile` copy created with rights disabled
  (`EmailTemplateUserTask.cs:78, 122`); the legacy path uses `UseAdminRights = false` (`:96, 133`) [source]. A
  Process file Object attachments element with no record scope and no selecting filter reads "up to N ARBITRARY
  files"; saving them is refused (ENG-92719 decision D16, `PB/FileProcessing/FileProcessingFilterLedger.cs:75-95`),
  but using them in the process only gets that element's own describe notice, and no notice fires when a later
  request wires it into an email (`PB/FileProcessing/ObjectFileProcessingNoticeRule.cs:62-104`) [source]. The file
  selection itself applies no record rights (`esq.UseAdminRights = false`,
  `PD/Schemas/FileProcessing/FileProcessing.cs:144-145`, used at `:210`) [source]. The designer has no unscoped-source
  check of any kind (section 3). In the package, `ObjectFileProcessingEmptyFilterRule` refuses an unscoped Object
  element only when it SAVES (`PB/FileProcessing/ObjectFileProcessingEmptyFilterRule.cs:38-43`), and
  `ReportFileProcessingEmptyFilterRule` refuses an unscoped MS Word element whatever its action
  (`PB/FileProcessing/ReportFileProcessingEmptyFilterRule.cs:24-38`); both run at the end of the request through
  `FileProcessingFilterLedger.EnsureSelectingFilters` (`:75-95`) [source]. Decisions Q20 (agreed 2026-10-28): an
  attachment entry that reads an unscoped Object attachments or Generated report element is refused through these
  rules.
- Limits: no size or count check in the Creatio code on this path (`EmailTemplateUserTask.cs:68-142`;
  `PS/CrtEmailSender/branches/7.8.0/Schemas/EmailSender/EmailSender.cs:316-320, 690-693`;
  `PS/IntegrationV2/branches/7.8.0/Files/cs/EmailClient.cs:163-175`) [source]; a limit of the email service or the mail
  server is outside the tree [unverifiable, OF-20]; every attachment is loaded fully into memory
  (`PS/CrtBase/branches/7.8.0/IntegrationApi/MailSenderDTO/EmailAttachment.cs:49-57`); the documented 10 MB
  `MaxFileSize` is for UI uploads [source] [doc].
- `LoadAttachmentsInSameProcess` is read only by inbound Exchange sync
  (`PS/Exchange/branches/7.8.0/Schemas/ExchangeEmailSyncProvider/ExchangeEmailSyncProvider.cs:407`); nothing on the
  process side reads it [source].

### 4.6 Save and sync

- **Pre-save gate** (`ProcessSchemaManager.GetProcessValidationResult`): registers nested parameters
  (`TS/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:402-416`, nested at `:410`, called per element at
  `:1049-1050`); a Script token whose parameter is not found raises
  `ParameterNotFound` (`:609`, thrown via `HandleErrorInfoWhenExists` `:491`) when
  `FeatureUseVerificationOfProcessParameterDirection` is on (default true, `TS/Terrasoft.Core/GlobalAppSettings.cs:343`);
  a source that is an element `In` parameter is refused (`TS/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:259`);
  only Script sources are registered, so write Script, as the designer does (r4 §5.6). No rule compares an
  element's dynamic parameters with its task schema [source]. Whether a server-built nested Script compiles against
  `IFileLocator` is EM2 [traced, not measured].
- **Element sync** removes a parameter that has no schema mapping and whose `CreatedInSchemaUId` is the TASK schema
  UId (`TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:253-262`); a slot stamped with the process UId and no
  mapping row is kept [source].
- **`GS5` at run time.** `UseOnlyModifiedParameters` (`TS/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:739-742`)
  applies only to the element reader `ProcessComponentSet` builds for a sub-process start
  (`TS/Terrasoft.Core/Process/ProcessComponentSet.cs:1292-1297`); the multi-instance count reader (`:1449-1452`) uses
  the same options but returns before the filter (`ProcessParameterValueProvider.cs:734-737`). A Send email's
  parameters are read through `InitializeFlowElementProperties` with an unfiltered `ParametersReaderOptions`
  (`ProcessComponentSet.cs:491-517`; `ProcessParameterValueProvider.cs:773-774`). `GS5` = process UId is therefore
  designer and corpus parity on the interpreted path; the compiled-code generator skips an element parameter whose
  `GS5` is not the process (`TS/Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:296-298`). The designer and every
  `PB` mapping write that value [source].
- **Version clone.** `ProcessVersionCloneFactory.Materialise` replaces the source UId with the clone UId across the
  serialized metadata (`PB/Design/ProcessVersionCloneFactory.cs:106-110`), so `A3`, `A4` and `GS5` follow and the
  slot stays "own" in the new version [source] [inferred]; EM13.

---

## 5. Corpus (PS)

**Four bindings, all one row `Attachments1`, all in the ProcessTests test package** [corpus; parsed
2026-10-28 from `PS/ProcessTests/branches/7.8.0/Schemas/<Name>/metadata.json`].

| # | Process (created in) / element | Mode / message | Source element (variant) | Outer `A2` / item `A2` lines |
|---|---|---|---|---|
| B1 | `ProcessFileSendMail` (`0.0.0.0`) / `EmailTemplateUserTask1` | manual / custom; IgnoreErrors true | `ObjectFileProcessingUserTask1` (Object attachments) | `:2933` / `:2948` |
| B2 | `ProcessFileSendMail` / `EmailTemplateUserTask2` | auto / custom; IgnoreErrors false | `ReportFileProcessingUserTask1` (Generated report) | `:3939` / `:3954` |
| B3 | `MailConnectionsProcess` (`7.18.2.172`) / `EmailTemplateUserTask1` | auto / template; CreateActivity true | `ObjectFileProcessingUserTask1` (Object attachments) | `:2489` / `:2504` |
| B4 | `ProcessFileReport` (`8.0.4.826`) / `EmailTemplateUserTask1` | manual / template; CreateActivity false | `ReportFileProcessingUserTask5` (Generated report) | `:2655` / `:2670` |

- Every binding: outer keys exactly `BL1, UId, A2, A3, A4, IL2, L1, L8, L12, L18`; item keys
  `BL1, UId, A2, A3, A4, IL2, L1, L8, L12`; `L12 = 0` on both levels; `A3`, `A4`, `GS5` = the process UId and `IL2` =
  the email element UId on both levels; exactly one item; both levels Script (`GS1 = 3`) [corpus, measured
  2026-10-28].
- Values: outer `[#[IsOwnerSchema:false].[IsSchema:false].[Element:{<source>}].[Parameter:{<collection>}]#]`, item the
  same with `[Parameter:{<item>}]` and no collection segment; the parameter UIds are the source ELEMENT INSTANCE's
  (r3, r5) [corpus].
- Resources: four keys per row in the authoring culture only, e.g.
  `PS/ProcessTests/branches/7.8.0/Resources/ProcessFileSendMail.Process/resource.en-US.xml:12-15, 88-91`; the item
  key nests under the outer (`...Parameters.Attachments1.Attachments1FileLocator.DisplayValue`) [corpus]. B4's
  resource file is not from the same save as its metadata: it has no row for the bound source
  `ReportFileProcessingUserTask5` (nor `ReportFileProcessingUserTask4`, `6`, `ReadDataUserTask3`), it keeps rows for
  `ReportFileProcessingUserTask1-3` and `ReadDataUserTask2`, which the metadata lacks, and its DisplayValue
  `[#ProcessReport3...#]` (`PS/ProcessTests/branches/7.8.0/Resources/ProcessFileReport.Process/resource.en-US.xml:12, 14`)
  is `ReportFileProcessingUserTask2`'s caption (`:141`). Only the B1-B3 resources are designer display-form evidence
  [corpus, parsed 2026-10-28].
- Sources seen: Object attachments x2, Generated report x2, all "Use in process". Never seen: the Process parameter
  variant (item `ObjectFile`), a process File or FileCollection, a single File, `Attachments2+` (search over all of
  `PS`), one collection feeding two Send email elements [corpus]. In every binding the source is the direct flow
  predecessor of the email (r5 §3.4) [corpus].
- Source item names per variant: Object `ObjectFiles` -> `File` (`PD/Schemas/ObjectFileProcessingUserTask/metadata.json:121, 131`);
  Report `ReportFiles` -> `File` (`PD/Schemas/ReportFileProcessingUserTask/metadata.json:126`); Process parameter
  `ObjectFiles` -> `ObjectFile`, its `Files` -> `File` is an input (`PD/Schemas/ProcessFileProcessingUserTask/metadata.json:17, 27, 102, 113`;
  `PB/FileProcessing/ProcessFileProcessingVariant.cs:20`); that variant only saves, "use in process" throws
  (`PD/Schemas/ProcessFileProcessingUserTask/ProcessFileProcessingUserTask.cs:94`) [source].
- Size of Send email usage: 27 `EmailTemplateUserTask` elements (`J6` `184dbb27-ce13-4d37-8dff-c2ff1df9cf19`) in 23
  schemas (16 manual / 11 auto, 24 custom / 3 template) [corpus, re-counted 2026-10-28]; 11 in product packages
  excluding CrtCopilotDemo (13 including it), none with attachments. The wider "41 Send email elements in 37 schemas"
  (r5 §2) is not re-counted.
- Dates: the four bindings' processes were last saved 2021-03-09 (`ProcessFileSendMail`), 2021-06-17
  (`MailConnectionsProcess`) and 2022-08-08 (`ProcessFileReport`) (each schema's `descriptor.json` `ModifiedOnUtc`);
  the "created in" versions above are `B8`, the creation version (`TS/Terrasoft.Core/Schema.cs:82`) [corpus].
- Newest 8.3.x evidence of the same generic shape: the Creatio.ai element's `Files1` + `Files1FileLocator` in
  `PS/CopilotAutoTest/branches/7.8.0/Schemas/SkillFilesValidationProcess/metadata.json:1391, 1406, 2598, 2613`, created
  in 8.3.1.3829 (`:12`) and last modified 2025-10-10 (`descriptor.json` `ModifiedOnUtc`) [corpus]. It is not a Send
  email capture.

---

## 6. Stand `Creatio`: read-only inventory, 2026-10-28

Source: the lead's read-only SQL (r0) and ESQ reads (r8) [measured 2026-10-28].

| Item | Value |
|---|---|
| CrtProcessBuilder | 1.6.6.125 (ModifiedOn 2026-10-27) |
| CrtProcessDesigner / CrtBase | 7.8.0 / 7.8.0 |
| Core version | not read on 2026-10-28; 10.1.37 (.NET Framework) when measured for ENG-92719 on 2026-10-01 (EM0 records it) |
| `UseProcessEmailAttachments` | state 1 for "All employees" and "All external users"; no row with state 0; `Feature.DefaultState` = 1, set by the platform on 2022-12-03, not by the package data (which carries Code and Id only), so a context-free read answers on here too |
| `LoadAttachmentsInSameProcess` | state 1 for "All employees" (a 2017 row) |
| `MailboxSyncSettings` | 0 rows: automatic sending with a named sender cannot be built or proven here yet; the owner configures real mail sending on this stand, then EM7 runs here (decisions Q17, agreed 2026-10-28; EM6 dropped) |
| Processes with `Recipient1` in metadata | 11 |
| Processes with an `Attachments` slot or a FileLocator-typed email parameter | 0 |
| The three corpus processes | not installed (r8, read-only SysSchema read by UId; measured 2026-10-28 by the lead, re-run before PROVENANCE is written) |

---

## 7. Academy (doc)

- The Send email article (version 10, read 2026-10-28) has a section on attachments: add and set up the Process file
  element first ("Add and set up the Process file element before setting up attachments"), click the add button in
  "Add attachments", choose "Process parameter" in the lookup, map the file collection, Select. At run time the
  mapped collection is sent as attachments. No restriction by sending mode or message type, no size or count limit
  [doc, read 2026-10-28].
- The ordering rule is a CONFIGURATION order; the flow order is implied, not stated (r6 §2.4) [doc].
- Process file has three sources (Object attachments, Process parameter, Generated report) and two actions (use in
  process, save to attachments); the current docs list Word reports only, one file per record (r6 §2.2) [doc].
- The 10 MB `MaxFileSize` and the composer restrictions of 8.0.10 apply to the UI, not to process sends (r6 §2.5) [doc].
- No toggle is documented; the 7.17.1 "database-stored files only" note is out of date (r6 §2.5-2.6) [doc].
- Field reports: the most common complaint is an email without its file (wrong file object, filtering by `Id`
  instead of the parent link, empty collection) (r6 §2.7) [doc, community, not authoritative].

Business use cases, condensed from r6 §5 [doc] [inferred]:

| UC | Scenario | Source variant | Mode |
|---|---|---|---|
| UC1 | Email a generated invoice, quote or order document to the customer | Generated report | auto, custom or template |
| UC2 | Scheduled report distribution | Generated report | auto |
| UC3 | Forward the latest N files of a record | Object attachments | auto or manual |
| UC4 | Email a file received as a parameter (page upload, parent process, skill output) | Process parameter / File | auto |
| UC5 | Manual draft with prefilled attachments for review | any | manual |
| UC6 | Template notification with a fixed document pack | Object attachments | auto + template |
| UC7 | One collection, several emails (customer and manager) | any | mixed |
| UC8 | Several sources in one email (invoice + signed contract) | two variants | any |
| UC9 | Generate, save to attachments, and send the same output | Generated report + save | auto |
| UC10 | Negative: no files match; today the email goes out without files | any | any |
| UC11 | Negative: no upstream file source; the builder refuses (AC5) | none | any |
| UC12 | Negative or limit: an oversized file or a blocked extension; process behaviour undocumented | any | any |

A generated report is a Word file; guidance must not promise a PDF from a report (r6 §2.7 K4) [doc].

---

## 8. Refinement comment (2026-09-08): verdicts

The claims are as the research quotes them (r1-r8); the comment itself was not re-read for this file. The ids are
RC1-RC12 so that C1-C10 keep meaning the design's package components.

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| RC1 | One row = `Attachments<N>` CompositeObjectList In "Attachments collection" + `Attachments<N>FileLocator` FileLocator In "Attachment" | confirmed | `PD/Schemas/ProcessFilesControlSchema/ProcessFilesControlSchema.js:254-295`; corpus section 5 [source] [corpus] |
| RC2 | The outer level has source None and carries no value; the item carries the mapping | wrong for a collection source: None is only the creation default; the designer binds the parent (`MappingEditMixin.js:947-996`); 4 of 4 corpus slots are bound on both levels; the run time reads the PARENT's value (`EmailTemplateUserTask.cs:284`). Right only for a single File (shape B) | sections 3, 4, 5 [source] [corpus] |
| RC3 | Both levels stamped with the process UId, the discriminator `Recipient<N>` uses | half right: the stamp is shared; Direction is not (Recipient Variable, slot In); `PB` never stamps recipients explicitly, and a nested item gets no back-fill | section 2.4 [source] [corpus] |
| RC4 | `ItemProperties` is a public settable `ProcessSchemaNestedParameterCollection` | confirmed | `TS/Terrasoft.Core/Process/ProcessSchemaParameter.cs:586-593` [source] |
| RC5 | Both types resolve by NAME; no `DataValueType.*UId` constant | wrong: `TS/Terrasoft.Core/DataValueType.cs:270-271, 276`; used by `PB` production code. Resolving `File` by name gives the BLOB type | section 2.4 [source] |
| RC6 | `ProcessParameterService` refuses composite / entity-collection / binary-file types; blast radius `ProcessParameterService.cs` | stale and mis-scoped: its allow-list applies to process-level parameters only; element-owned slots never pass it; the shared write surface is `CollectionBindingRules` + `ProcessMappingService` | `PB/Parameters/ProcessParameterService.cs:1797`; section 2.5 [source] |
| RC7 | `UseProcessEmailAttachments` gates the designer control; `Feature.DefaultState = 1` | gate confirmed and it ALSO gates the run time; the package enables it through `AdminUnitFeatureState` rows and seeds no DefaultState; on stand `Creatio` the `Feature` row does have `DefaultState` = 1, set by the platform, so there a context-free read answers on as well; the run time asks with the executing user (section 2.11) | sections 2.11, 4.5 [source]; stand states [measured 2026-10-28] (section 6) |
| RC8 | `LoadAttachmentsInSameProcess` (DefaultState 0), interaction uncaptured | unrelated: inbound Exchange sync only; on the stand it is state 1 | section 4.5 [source]; section 6 [measured 2026-10-28] |
| RC9 | Recipients are match-or-append with no removal path | confirmed | `PB/Email/SendEmailApplier.cs:789-818` [source] |
| RC10 | Delivery proof needs a configured mailbox | only for a real delivery: manual mode and automatic mode with `CreateActivity` and a non-empty sender leave `ActivityFile` rows | section 4.3 [traced, not measured] |
| RC11 | AC7 needs a capture of 2+ attachments x the three sources | partly: the corpus covers one-row Object and Report bindings in all four mode combinations (2021-2022); a current capture is still needed for 2+ slots, a gap, the Process parameter variant, File and FileCollection sources and the current version | section 5 [corpus] |
| RC12 | Residual risk: the pre-save gate and the designer control on a server-built nested parameter | narrowed, not closed: no structural gate exists; the nested Script type check (EM2) and designer rendering (EM3) remain | section 4.6 [source] |

---

## 9. clio and KB today

### 9.1 clio (CLIO)

| Fact | Evidence | Tag |
|---|---|---|
| The process tools forward the descriptor or operations verbatim; they are long-tail tools (not in the resident profile), so their descriptions reach an agent through `get-tool-contract` | `CLIO/clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs`; no process tool in `CLIO/clio/Command/McpServer/McpCoreToolProfile.cs` | source |
| `ProcessBlockKeyCheck` refuses any key the bundled contract does not declare ("Accepted email keys ..."); `EmailBlock` = `messageSource template templateEntity body bodyFormat mode sender subject to cc bcc importance ignoreErrors performer`; so `email.attachments` is refused today on create, modify and modify-as-new-version. The check exists from 2026-10-13 (`0944088ba`, the file's first commit); a clio built before it forwards `email.attachments`, and the published library still reaches that clio (`KB/bundle-source.json` compatibility clio 8.1.0-8.1.999) | `CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:20-40, 185-193`; calls `CreateBusinessProcessCommand.cs:342`, `ModifyBusinessProcessCommand.cs:302`, `ModifyProcessAsNewVersionCommand.cs:170`; test `CLIO/clio.tests/Command/ProcessModel/ProcessBlockKeyCheckTests.cs:101` | source |
| Drift guards that bite on the rebundle: each key set must equal the bundled contract's members; read-back fields must be describe-only | `ProcessBlockKeyCheckTests.cs:449, 469` | source |
| Floors: create, modify and modify-as-new-version require CrtProcessBuilder 1.6.6.119; describe is unversioned | `CLIO/clio/Command/CreateBusinessProcessCommand.cs:287`, `ModifyBusinessProcessCommand.cs:226`, `ModifyProcessAsNewVersionCommand.cs:90`, `DescribeProcessCommand.cs:21`; description literals `CreateBusinessProcessTool.cs:310`, `ModifyBusinessProcessTool.cs:158`, `ModifyProcessAsNewVersionTool.cs:90` | source |
| Floor rule: it moves when clio starts ADVERTISING behaviour an older server may not have; a new member on a known block is discarded silently by an older server | `CreateBusinessProcessCommand.cs:205-222` | source |
| Bundled archive pinned at 1.6.6.125; `ExpectedAuthorizationGateCallSites = 6`, `ExpectedOperationContractCount = 8` | `CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs:208, 338, 350` | source |
| Describe model `DescribedEmail` has a `[JsonExtensionData]` bag whose doc names "an attachment list", so a package that emits `email.attachments` reaches the output verbatim without a clio change | `CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:1142, 1245` | source |
| `EmailBlockExpectation` is the behavioural read-back precedent for a silently dropped block | `CLIO/clio/Command/ProcessModel/EmailBlockExpectation.cs:8-14` | source |
| Description budgets: ceiling 35,072 B (`137 * 256`); last recorded create 35,054, modify 35,021, describe 34,974 | `CLIO/clio.tests/Command/McpServer/ToolContractPayloadBudgetTests.cs:286-292` | source |
| Later commits changed the descriptions without re-recording; estimate create ~35,070, modify ~35,069, describe ~34,917 (about 2, 3 and 155 B free) | r7 §2, computed outside the repository; the guard was not run | inferred |
| Seven-contract wire bundle (`GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether`, `:436`): 18,815 B against 19,000, measured 2026-10-19; stale since the create call shape grew (`itemProperties?`, clio `03f6933c8`, 2026-10-23) and later commits. A nested `email` member is not in the call shapes (block names only), but any new sentence matching `SafetyDuty` (`CLIO/clio/Command/McpServer/Tools/ToolContractShortForm.cs:248-253`, e.g. "must not", "tell the user") is kept by the short form and lands in this bundle | `ToolContractPayloadBudgetTests.cs:310-314` | source; the figure is stale |
| ClioRing does not consume `create/modify/describe-business-process`, `sendEmail` or `fileProcessing` | search over `CLIO/clio-ring/ClioRing.Ipc`, `CLIO/clio-ring/ClioRing`, `CLIO/clio-ring/ClioRing.Desktop/actions.json` | measured 2026-10-28 |
| No BMAD story for this ticket: "ENG-95985 Send email attachments has no story id in this tracker yet" | `CLIO/spec/sprint-status.yaml:4702` | source |

### 9.2 KB

| Fact | Evidence | Tag |
|---|---|---|
| "Send email attachments and Creatio.ai file inputs are not buildable here yet." | `KB/guidance/mcp/guides/processes/files.md:188-189` | source |
| The sentence is pinned (whitespace-collapsed) at `:181` by `Section_ShouldStateTheEndpointConsumersAndTheReFind` (`:169`); the same test asserts that files.md's `fileProcessing` section does not say "Creatio cannot" (`:195-196`; the section runs from its heading to the end of the article, `:28, 280-285`); only "rarely an endpoint" is checked across every process article (`:193`) | `KB/automation/Clio.Knowledge.Bundle.Tests/ProcessFileGuidanceTests.cs:181` | source |
| `send-email.md` owns mode, sender, recipients, custom message; recipients have no removal path through the tool, removal is routed to the designer | `KB/guidance/mcp/guides/processes/send-email.md:153-157` | source |
| `element-catalog.md` lists the Send email block contents, without attachments | `KB/guidance/mcp/guides/processes/element-catalog.md:40-44` | source |
| `process-modeling.md` routes `process-send-email` and `process-send-email-template` | `KB/guidance/mcp/guides/processes/process-modeling.md:40-42` | source |
| `routing.md` routes the Send email family: `process-send-email`, `process-send-email-details`, `process-send-email-template` | `KB/guidance/mcp/guides/routing.md:107-112` | source |
| Manifest: an article written after the v0 migration declares NO `legacyUris` and is listed in `GuidanceMigrationTests.PostMigrationGuidance` (`process-send-email-details`, `process-files-parameter` do this; `process-send-email-template` declares one); every other guidance resource must declare exactly one | `KB/bundle-source.json` (resources); `KB/automation/Clio.Knowledge.Bundle.Tests/GuidanceMigrationTests.cs:25-58, 235-245` | source |
| Budgets: `process-modeling.md` inline reply 18,999 of 19,000 B (gated); the process-digest card about 3,070 of `MaxCardBytes = 3,072`; `send-email.md` 20,246 of the 27,793 gate and about 17,461 B on the wire | r7 §4.2 (computed with the tests' formulas, the tests not run); `MaxCardBytes` at `KB/automation/Clio.Knowledge.Bundle.Tests/ProcessDigestTests.cs:41` | inferred |
| `libraryVersion` 1.15.173; every content change needs a new one, and the consumer's `sequence` is derived from it at build time (`bundle-source.json` has no `sequence` field to bump) | `KB/bundle-source.json:6` (value); `KB/CONTRIBUTING.md:166-171` (rule) | source |

---

## 10. Open facts (traced, not measured) and the measurement that settles each

| # | Open fact | Basis today | Settled by |
|---|---|---|---|
| OF-1 | The current designer's key set, stamps and resources for 2+ slots, the Process parameter variant, a FileCollection item and a single File; whether a removed row's four resource keys go; the position of a refilled slot in `BP2`; the item `L1` after `_onParameterValueChanged` per source kind (source predicts FileLocator unchanged); what the process-tab pick of a whole FileCollection stores | corpus 2021-2022 only (section 5); source sections 3 | EM1 |
| OF-2 | Re-binding a row from a collection item to a single File leaves the parent STALE, and at run time gives N copies of one file | `MappingEditMixin.js:1075-1096` (section 3) | EM1 (EM1d), then EM8 |
| OF-3 | A server-built twin passes the pre-save gate; a nested Script compiles against `IFileLocator` outside a row | section 4.6 | EM2 |
| OF-4 | The outer display value divergence (section 2.5) is the only resource difference, or there are others | `ProcessMappingService.cs:963-966`; corpus resources | EM1 + EM2 under the comparison rule |
| OF-5 | The designer renders builder-made rows with display values, and a no-op save keeps the compared keys | `ProcessFilesControlSchema.js:218-228, 308-324` | EM3 |
| OF-6 | Manual + custom with 2+ slots: `ActivityFile` rows = the sum of rows over slots (`TypeId` = File on both copy paths is traced, section 4.3) | section 4.3 | EM4 |
| OF-7 | Manual + template attaches as custom does | shared `CreateEmailMessage` | EM5 |
| OF-8 | Automatic + `CreateActivity` + a named sender + `IgnoreErrors` leaves `ActivityFile` rows although the send fails; an empty sender creates no activity | `AutoEmailUserTaskSender.cs:105-113, 165-181` | the activity and its files: EM7c, with real mail on stand `Creatio`; the failed-send and empty-sender parts stay traced (EM6 dropped, decisions Q17 agreed 2026-10-28) |
| OF-9 | Real delivery carries every file (auto + custom, auto + template) | section 4.3 | EM7 |
| OF-10 | A single File item-only binding attaches one file; an unset File fails the element with an NRE outside `IgnoreErrors` | shape B, section 4.4; M1 analogue on the Process file element | EM8 |
| OF-11 | An outer-only slot is loud (NRE) or silent (0 files) | shape D; the measured M3 copy behaviour was on a process-parameter target | EM9 |
| OF-12 | A source that did not run (untaken branch, or after the email on the flow) gives 0 attachments and no error | shape G | EM10 |
| OF-13 | One collection feeds two Send email elements; `removeElement` of the source through the builder: does the pre-save gate refuse the dangling Script (`ParameterNotFound`) | sections 2.8, 4.6 | EM11 |
| OF-14 | The feature off for the EXECUTING user drops attachments silently | sections 2.11, 4.5 | not measured: decisions Q9 (agreed 2026-10-28) adds no build-time check and drops EM12; the guide's troubleshooting line rests on the trace |
| OF-15 | Modify-as-new-version keeps the slots "own" (`A3` = clone UId) and files still attach | `ProcessVersionCloneFactory.cs:106-110` | EM13 |
| OF-16 | A SysFile-root Object source feeds Send email through `IFileFactory` | `EmailTemplateUserTask.cs:77-89` | EM14 |
| OF-17 | A Generated report "use in process" temp file stays readable from `ActivityFile` after the process ends (manual mode) | the copy runs inside `Execute` (section 4.3) | EM15 |
| OF-18 | The stand's core version and the remaining baseline items | section 6 | EM0 |
| OF-19 | What `describe-business-process` on 1.6.6.125 reports for a designer-built slot (generic `parameters[]`, decoded names) | section 2.7 | EM1 (read-only describe of the capture) |
| OF-20 | Behaviour for an oversized file or a blocked extension on a process send | section 4.5 | no EM assigned; out of the AC unless the owner adds it |
| OF-21 | A multi-instance Send email (the designer converts an activity when a top-level parameter takes a collection item) loses its slots: the conversion moves them into `InputRecordCollection` item properties, and the run time's top-level slot search does not see them | section 3 (conversion, `process-activity-schema.js:317-338`); `ProcessUserTaskExtensions.cs:88-97`; `PB` cannot build one (section 2.9) | no EM: `email.attachments` on a multi-instance Send email is refused (decisions Q16, agreed 2026-10-28) |
| OF-22 | A slot bound to a process FileCollection that a parent process filled (a sub-process input) attaches every file it holds | shape A with a process-parameter source (section 4.2); the corpus has no process-parameter source (section 5) | EM4c |
| OF-23 | A builder removal (`Parameters.Remove`) drops the removed slot's resource keys on save, or a slot that reuses the freed name inherits a stale outer `DisplayValue` | the package deletes no `SysLocalizableValue` row and keys are built from names (section 2.4) | EM1, EM2 |

---

## Corrections to the research

1. **Outer display value** (r1 §2 "PB's mapping service already writes exactly these display forms"; r2 §3.1). Only the
   item's form matches. `SourceDisplayValue` gives a top-level source its bare caption
   (`PB/Mappings/ProcessMappingService.cs:963-966`, pinned by `PBT/MappingNestedDisplayValueTests.cs:74-77, 111-114`),
   while the designer stores `[#<element>.<collection>#]` on the outer level
   (`PS/ProcessTests/branches/7.8.0/Resources/ProcessFileSendMail.Process/resource.en-US.xml:15, 91`). No report
   raised it.
2. **FileLocator constant on 8.3.4** (r8 §0.3, §10.4 "presence on 8.3.4 not verified"). Production `PB` code already
   references `DataValueType.FileLocatorDataValueTypeUId` (`PB/Mappings/CollectionBindingRules.cs:140`) and CI compiles
   it against 8.3.4, so it exists there [inferred]. Same resolution as r10 C2.
3. **Silent skip of a missing locator** (r6 TL;DR 9, §2.7 K1, §4, §6 AC5). A row whose `<slot>FileLocator` key exists
   with a null value throws an NRE (`TS/Terrasoft.Common/CompositeObject.cs:120-127`; `TS/Terrasoft.File/FileFactory.cs:44-45`);
   a silent skip happens only for an absent key or a failed legacy fetch. A missing file record on the file-storage
   path throws on first read. The run time matches an anchored, case-sensitive regex, not "starts with"
   (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19`). Same as r10 C9, C10.
4. **Shared code and old-server behaviour** (r9 §5 "a change in the shared `Parameters/ProcessParameterService.cs`";
   r9 §4.4 and §6 "silent drop vs refusal not verified"). No `ProcessParameterService` change is needed (section 8,
   RC6); an older package drops the member silently (`PB/Contracts/VersionContracts.cs:23-26`), and refuses an
   attachments-only block as specifying no field (section 2.10). Same as r10 C3, C14.
5. **Parent binding "not located"** (r5 §4). It is `MappingEditMixin` `_processMappingToCollectionItem`
   (`PD/Schemas/MappingEditMixin/MappingEditMixin.js:947-996`).
6. **MappingEditMixin path** (r4 §7 cites `TS/.../Conf/content/MappingEditMixin.js`). The cited lines match
   `PD/Schemas/MappingEditMixin/MappingEditMixin.js:895-905, 1132-1156`.
7. **Line numbers** (r7 §4.1, §4.3; r1 §8.1): the pinned clause is at `ProcessFileGuidanceTests.cs:181` (not `:180`);
   the KB sentence at `files.md:188-189` (not `:187-189`); `SendEmailRecipientDescriptor` is declared at
   `ProcessDescriptorContracts.cs:1921` (not `:1902`); the recipient removal text in `send-email.md` is at `:153-157`
   (not `:150-158`).
8. **Numbering** (r0 "[inferred: first free number]"). Confirmed in source: the designer takes the lowest free index
   from 1 (`PD/Schemas/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:465-479`); `PB` recipients use
   max+1 and their doc comment saying otherwise is false (`PB/Email/SendEmailApplier.cs:791, 854-867`). Same as r10 C15.
9. **Stand address.** r5 §6 and r8 §0.1, §0.4 write the stand's address; this file names the stand only as `Creatio`.
