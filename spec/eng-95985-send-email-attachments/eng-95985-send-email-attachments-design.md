# ENG-95985 Send email attachments: design

Written 2026-10-28. The design follows the answers the owner agreed on 2026-10-28 in the decisions file
(Q1-Q20); a different answer changes the sections marked with its Q number. Facts and their evidence are in
`eng-95985-send-email-attachments-facts.md`; the target bytes are in `eng-95985-send-email-attachments-serialization.md`.

Basis: crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125), clio `origin/master` `358e9db52`,
clio-knowledge `origin/master` `534e913`, CrtProcessDesigner 7.8.0, core `TSBpm` trunk.
Paths: `PB/` = `packages/CrtProcessBuilder/Files/src/cs/`, `PBT/` = `tests/UnitTests/CrtProcessBuilder.Tests/`,
`PD/` = `CrtProcessDesigner/branches/7.8.0/Schemas/`, `TS/` = `TSBpm/Src/Lib/`, `CLIO/`, `KB/` = repository roots.

## 1. What the feature does, in one paragraph

An agent adds `attachments` to the `email` block of a Send email element. Each entry names ONE source of files: a
Process file element's output, another element's file output, a process FileCollection, or a single File. The
package turns every entry into one designer row: an element parameter `Attachments<N>` (a collection) with exactly one
nested item `Attachments<N>FileLocator` (a file), stamped and bound exactly as the Process Designer stores them, so
the platform's run time finds the files in all four mode combinations: it attaches them to the outgoing message in
automatic mode, and copies them to the email activity in manual mode (and in automatic mode with "Create activity").
A sent list replaces the element's attachments. `describe-business-process` returns the same list, ready to edit and
send back. What the build can see (a source that holds no files, a source declared later or not on a path before
the email, a single File that may be empty, a multi-instance Send email, an unscoped record source) is
refused or warned. What only the run can see (the feature off for the EXECUTING user, a source branch that was not
taken, a file deleted before the run) is stated in the guidance.

## 2. Contract

### 2.1 Write (`create-business-process` `elements[].email`, `addElement`, `setElement.elementUpdate.email`)

```json
"email": {
  "mode": "manual",
  "subject": "Your invoice",
  "body": "<p>Please find the invoice attached.</p>",
  "to": [ { "processParameter": "Contact" } ],
  "attachments": [
    { "sourceElement": "GenerateInvoice", "sourceElementParameter": "ReportFiles" },
    { "sourceElement": "ReadContractFiles", "sourceElementParameter": "ObjectFiles" },
    { "processParameter": "Documents" }
  ]
}
```

Entry members (new contract type, same five members as `fileProcessing.files`, `PB/Contracts/ProcessDescriptorContracts.cs:416-440`):

| Member | Meaning | Rule |
|---|---|---|
| `processParameter` | a process parameter holding files: a FileCollection, or a File (Q2, Q3) | a dotted path to an item of a process collection is accepted, as for `files` |
| `sourceElement` + `sourceElementParameter` | an element output holding files: `ObjectFiles`, `ReportFiles`, a sub-process FileCollection output, a File output; dotted for an item (`ObjectFiles.File`) | both halves together |
| `expression` | a raw formula, written onto the file item as given | escape hatch; no checks beyond the formula validator |
| `value` | declared only so the refusal can name it | always refused: a file has no constant form |

Exactly one source per entry. `attachments` absent: the element's attachments are untouched. `attachments: []`: every
attachment slot is removed (Q4).

Not added: a removal op, a per-entry slot name, a caption (the designer's captions are fixed), anything about
recipients.

### 2.2 Read back (`describe-business-process`, the element's `email` block)

```json
"attachments": [
  { "sourceElement": "GenerateInvoice", "sourceElementParameter": "ReportFiles",
    "slot": "Attachments1", "bindingLevels": "both" },
  { "processParameter": "Contract", "slot": "Attachments3", "bindingLevels": "itemOnly" }
]
```

- The write members are decoded from the stored formulas (the same decoder `fileProcessing.files` uses), so the entry
  can be sent back unchanged.
- Read-back members: `slot` (the stored parameter name), `bindingLevels` (`both` | `outerOnly` | `itemOnly` | `none`),
  `issues` (list of sentences, only when a stored shape would fail or surprise at run time). clio's key check tolerates
  them as read-back fields.
- Order: the element's stored parameter order - the designer's row order and the order the run time attaches the
  files - not the slot number: a re-added slot (lowest free number, appended) comes last, so numbers can have gaps and
  need not ascend (serialization SQ10, revised 2026-10-29: a stand run showed agents report the read-back order to
  users). An element with no slot omits `attachments` (null, not `[]`), like the other optional members.
- `configured` (the block's "is anything set" flag) counts attachments.

### 2.3 Modify semantics (Q4 = replace)

1. Plan every entry (section 4.2) before any write.
2. Read the element's existing slots (section 3.2).
3. Match each entry to an existing slot whose stored binding equals the entry's planned binding: same outer value
   and same item value, compared as the mapping service compares bindings (resolved meta paths, not raw strings).
   Matching is one to one: two entries with the same source match two slots. A matched slot is kept as it is: name,
   UIds, resources. A designer-made slot with an odd shape (outer-only, stale parent, different sources) never
   matches, so it is replaced by a slot in the package's shape.
4. Remove every slot that matched no entry. Before removing, ask the dependency snapshot
   (`IProcessDependencySnapshot.FindDependents(element, slot)`, `PB/Graph/IProcessDependencySnapshot.cs:55-58`) and
   refuse, naming the dependents, as the designer's `canRemoveParameter` does
   (`PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:392-405`). The removal notice carries each removed
   entry in the write shape, so one call restores it.
5. Create a slot for every entry that matched nothing, numbered per section 4.3 (Q5), and bind it.
6. Two entries with the same source are allowed (the designer allows it) and produce a warning: the files are
   attached twice.

Recipients keep their match-or-append rule. The guide states the difference in one line, and says to describe the
element immediately before sending a list: modify has no concurrency token, so a stale list deletes rows someone
added in the designer since.

## 3. Stored shape the package writes

Key-by-key target and the comparison rule: serialization file. The invariants the code must hold, each pinned by a
unit test:

| Invariant | Why |
|---|---|
| name matches `^Attachments\d+$` (case-sensitive) | the run time finds slots by this regex (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19, 78-98`) |
| outer `CreatedInSchemaUId` = `ModifiedInSchemaUId` = the PROCESS schema UId; the item stamped the same, explicitly | run-time filter; a nested item is not back-filled by `MetaItemCollection.InsertItem` |
| item name = slot name + `FileLocator`; exactly one item | the run time reads the key `<slot>FileLocator` (`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-119`); the designer renders `ItemProperties.first()` |
| `Direction = In` on both levels | designer and corpus; a recipient-style parameter would get the default `Variable` |
| types by `DataValueType.CompositeObjectListDataValueTypeUId` and `DataValueType.FileLocatorDataValueTypeUId` | resolving "File" by name returns the BLOB type |
| `ContainerUId` = the Send email element on both levels | corpus `IL2` |
| bindings stored as `Script` `[#...#]` with `GS5` = process UId | the pre-save gate registers only Script sources; `GS5` = process UId is designer/corpus parity, and the compiled-code generator skips an element parameter whose `GS5` is another schema (`TS/Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:296-298`) |
| captions "Attachments collection" / "Attachment"; no `BK15` mapping row | designer and corpus |
| display values in the designer form: outer `[#<element>.<collection>#]`, item `[#<element>.<collection>:<item>#]`, single File `[#<parameter>#]` | the package's `SourceDisplayValue` gives a top-level source its bare caption (`PB/Mappings/ProcessMappingService.cs:963-967`); the slot writer sets the designer form after binding, for slots only (serialization AD1/AD2), so other mappings keep today's display values |

### 3.1 Binding per source kind

| Entry | Outer slot | Item | Planner path |
|---|---|---|---|
| collection with one FileLocator item (`ObjectFiles`, `ReportFiles`, process FileCollection, sub-process output) | bound to the collection | bound to the collection's FileLocator item, found by TYPE (the Process-parameter variant's item is `ObjectFile`) | `BindCollection(outer, [(item, sourceItem)])` |
| item of a collection (`OF1.ObjectFiles.File`, `Docs.File`) | bound to the parent collection | bound to the item | `BindCollection` |
| single File (process File, element File output) | not bound (None) | bound to the File | item-only block mapping |
| `expression` | not bound | the formula | item-only block mapping |

### 3.2 Which existing parameters count as slots

A top-level parameter of the Send email element whose name matches the regex and whose `CreatedInSchemaUId` is this
process. Two predicates, each defined once on `EmailAttachmentSlot`: `IsSlot` (this definition, what the run time
reads) is used by the writer and the describer's `email.attachments` list (find, replace, describe); `IsReservedName`
(a root parameter name matching `^Attachments\d+$` case-insensitively on a Send email element, whatever its stamp) is
used by the raw-mapping guards (4.5) and by describe's generic `parameters[]` non-decoding (4.7). The guard keys on
the NAME only, so it also covers a slot that does not exist yet or was created elsewhere. A slot
with a different `CreatedInSchemaUId` is not touched and is reported by describe with an issue: the run time ignores
it.

### 3.3 Version clone

`modify-business-process-as-new-version` copies the schema through `ProcessVersionCloneFactory`, which replaces the
source schema UId in the serialized metadata as text (`PB/Design/ProcessVersionCloneFactory.cs:106-119`), so `A3`, `A4`
and `GS5` of both slot levels follow the clone. No code change is expected; TC-U-71 pins it and EM13 verifies the
run; a re-stamp task is added only if EM13 fails.

## 4. Package components

### 4.1 Overview

| # | Component | Kind | Files (expected) |
|---|---|---|---|
| C1 | Contract: `SendEmailAttachmentDescriptor`, `SendEmailDescriptor.Attachments`, `DescribeSendEmailAttachment`, `DescribeSendEmailInfo.Attachments` | contracts | `PB/Contracts/ProcessDescriptorContracts.cs`, `DescribeContracts.cs`, `ModifyContracts.cs` (doc of `setElement`) |
| C2 | Shared file-source planner: the `files` classification lifted out of `ProcessFileProcessingBinder` | refactor | `PB/FileProcessing/ProcessFileProcessingBinder.cs` -> new `IFileSourcePlanner` / `FileSourcePlanner` in `PB/FileProcessing/`, registered in `CrtProcessBuilderApp.cs` |
| C3 | Slot model and writer: `EmailAttachmentSlot` (predicate, names), `IEmailAttachmentSlotWriter` (find, create, remove, number) | new | `PB/Email/` |
| C4 | Applier: `SendEmailApplier.ApplyAttachments` (plan, reconcile, bind, notices) | change | `PB/Email/SendEmailApplier.cs`, `CrtProcessBuilderApp.cs` (DI) |
| C5 | Raw-route guards: no `addMapping` onto a slot, no mapping from a slot; a block form of `BindCollection` | change (shared mapping service: full review tier) | `PB/Mappings/ProcessMappingService.cs`, `PB/Mappings/IProcessMappingService.cs` |
| C6 | Order checks for attachment sources: build-path declaration order (refusal), flow reachability on both paths (warning) | change | `PB/Design/ProcessBuildHandler.cs`, `PB/Graph/ProcessGraphBuilder.cs` |
| C7 | Describe: `DescribeAttachments` + a binding reader shared with the `files` describer | change + refactor | `PB/Elements/SendEmailElementHandler.cs`, `PB/FileProcessing/ProcessFileProcessingFilesBinding.cs`, `ProcessFileProcessingDescriber.cs` |
| C9 | Comment fixes | docs | `PB/Mappings/CollectionBindingRules.cs:164-169` (Q15), `PB/Email/SendEmailApplier.cs:791, 811, 851-852` (numbering comments: the code takes max existing suffix + 1, `:854-867`, and does not refill gaps) |
| C10 | Package docs: the capture record for attachments | docs | `docs/send-email-element-capture.md` (attachments section) |

C8 (a feature reader for `UseProcessEmailAttachments`) was dropped with Q9 (section 4.8); C9 and C10 keep their
numbers because the other plan files cite them.

Every behaviour class gets an interface and a DI registration (repository rule). The refactors (C2, C7) land first, in
their own commits, with the existing Process file tests unchanged; that proves they are behaviour-neutral.

### 4.2 C2: the shared file-source planner

Lift `PlanFiles`, `FilesBinding` and `FileItemOf` (`PB/FileProcessing/ProcessFileProcessingBinder.cs:130-225, 253-318`)
into `FileSourcePlanner` behind `IFileSourcePlanner` (landed in slice A as `Plan(schema, element, IFileSourceDescriptor
source, FileSourceWording wording, UserConnection)` returning a data `FileSourcePlan`; the variant composes
`new ProcessFileProcessingBinder(new FileSourcePlanner())`, and the DI registration, the seam TC-U-70 resolves, lands
in P-B3 with its first container consumer), parametrized by:

- the label and the member path used in messages (`files` vs `email.attachments[2]`);
- the target collection name and item name (`Files`/`File` vs `Attachments<N>`/`Attachments<N>FileLocator`);
- the consequence sentence of the "names no source" refusal (the Process variant's "keeps the process from starting"
  is not true for an email slot);
- the write entry: `FilesBinding.Write` writes the single-file and expression cases through the raw `ApplyMapping`
  (`PB/FileProcessing/ProcessFileProcessingBinder.cs:299, 305`); for an email slot they go through the block entries
  (4.5), because the slot guard refuses the raw route.

It returns a plan (collection, collection item, single file, expression) with the resolved source names and the item
found by type. It writes nothing. `ProcessFileProcessingBinder` calls it with the `files` arguments and keeps every
message byte-identical (pinned by the existing `FileProcessingProcessTests`).

### 4.3 C3: slots

- `EmailAttachmentSlot.IsSlot(element, parameter, schema)`: top level, case-sensitive regex,
  `CreatedInSchemaUId == schema.UId` (what the run time reads; drives find, replace and the describe list).
- `EmailAttachmentSlot.IsReservedName(name)`: a root parameter name matching `^Attachments\d+$` case-insensitively,
  whatever its stamp (the guard predicate of 4.5 and 4.7, applied on a Send email element only).
- `ItemNameOf(slotName)` = slot name + `FileLocator`.
- `IEmailAttachmentSlotWriter.Find(schema, element)`: the slots in numeric order (write side only; describe keeps the
  stored order, 2.2).
- `Create(schema, element, number)`: builds the item first (name, type by UId, `In`, `ContainerUId` = element,
  explicit stamps, caption), adds it to the outer's `ItemProperties`, then builds the outer the same way and adds it to
  `element.Parameters`. Precedents: `MultiInstanceApplier.AParameter` (`PB/Elements/MultiInstanceApplier.cs:1004-1044`),
  `ProcessParameterService.StampAsCreatedIn` (`PB/Parameters/ProcessParameterService.cs:861-876`).
- `Remove(schema, element, slot)`: removes the parameter (both levels) with `Parameters.Remove`, after the dependency
  check of 2.3 step 4. The package has no mechanism that deletes `SysLocalizableValue` rows (every removal in the
  package is a bare `Parameters.Remove`: `PB/Parameters/ProcessParameterService.cs:559-568`,
  `PB/AccessRights/ChangeAccessRightsApplier.cs:531-545`), and resource keys are built from element and slot NAMES
  (`TS/Terrasoft.Core/Process/ProcessSchemaParameter.cs:900-930`). Whether a save drops the keys of a removed slot is
  measured (EM1, EM2), not assumed; if a stale outer DisplayValue survives under a reused name, the writer writes an
  explicit empty outer DisplayValue for every reused or unbound outer.
- `NextFreeNumber(element)`: the lowest k >= 1 such that no parameter of the element (every parameter of the
  element, nested items included, whatever its `CreatedInSchemaUId`) is named `Attachments<k>` (ordinal), as the
  designer's `generateItemUniqueName` scans the element's `parameters` collection (flat or roots-only by a client flag,
  the same numbers either way: serialization 5.1)
  (`PD/ProcessFilesControlSchema/ProcessFilesControlSchema.js:244-249, 259-260`,
  `PD/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:465-479`). A parameter that is not a slot (for
  example one created in another schema) still blocks its number.

### 4.4 C4: `SendEmailApplier.ApplyAttachments`

Order inside `SendEmailApplier.Apply` (`PB/Email/SendEmailApplier.cs:59-144`):

1. `EnsureConfiguresSomething` counts `attachments` (also `[]`).
2. Plan every attachment entry with C2, before the first write, together with the body and subject checks, so every
   refusal the planner can raise fires before any write (the "untouched on refusal" invariant the applier keeps for
   body, subject and template, `PB/Email/SendEmailApplier.cs:75-78, 98-100`). An `attachments` member (also `[]`) on a
   multi-instance Send email (`MultiInstanceOptions` set on the activity, Q16) is refused here too, before any write;
   `CollectionBindingRules.IsMultiInstance` (`PB/Mappings/CollectionBindingRules.cs:194-195`) answers for a Sub-process
   only, so the check reads the activity's `MultiInstanceOptions` itself. Refusals raised later inside
   `BindCollection` / `ApplyBlockMapping` (type, P1, R-M3) leave earlier in-memory writes behind, which is harmless
   because the request fails before save, as sender and recipient refusals do today (`:120-139`).
3. Existing writes (mode, body, subject, sender, importance, options, performer, recipients) as today.
4. Reconcile and bind attachments (section 2.3). Collection entries go through the BLOCK form of `BindCollection`
   (4.5), item-only entries through `ApplyBlockMapping` (`PB/Mappings/IProcessMappingService.cs:72`); both pass the
   C5 slot guard because they plan with `fromBlock = true`. `SendEmailApplier` gains the mapping service, the C2
   planner and the C3 slot writer in its constructor. (As built, 2026-10-29: the applier does not track Q20 sources;
   the end-of-request sweep of section 4.9 finds them on the final schema.)
5. Notices (section 5.2), handed to the request's element configuration ledger and judged on the final schema at the
   end of the request, together with the flow-reachability warning (as built).

### 4.5 C5: raw-route guards (Q8, Q10)

There is no separate raw route: `ApplyMapping`, `ApplyBlockMapping` and `BindCollection` all plan their writes in
`ProcessMappingService.Plan` (`PB/Mappings/ProcessMappingService.cs:61-86, 133-198`); `fromBlock` is the only switch
between the raw and the block form, and `BindCollection` passes `fromBlock = false` today (`:85`). `BindCollectionItems`
does not plan in `Plan` (`:89-117`); its only caller is the process-parameter mirror
(`PB/Parameters/ProcessParameterService.cs:1099`), whose target is a process parameter, so only the from-slot source
guard covers it. So:

- **onto a slot**: in `Plan`, in a NEW `if (!fromBlock)` block placed first, BEFORE `ResolveTargetParameter` (`:135`),
  a target on a Send email element (`SendEmailElementIdentity`, `PB/Email/SendEmailElementIdentity.cs`; the owner
  comes from the name-only lookup `ResolveTargetOwner`, `:580-586`) whose root parameter name satisfies
  `EmailAttachmentSlot.IsReservedName` (`^Attachments\d+$`, case-insensitive, so near-misses are caught too) is
  refused, naming `email.attachments`. It keys
  on the NAME, so it also refuses a build `mappings[]` entry onto a slot that does not exist yet (on build,
  `mappings[]` runs before the `email` pass: `PB/Design/ProcessBuildHandler.cs:553-561`), a foreign slot and the item;
- **a block form of `BindCollection`**: `IProcessMappingService.BindCollection` gains a `fromBlock` argument (or a new
  `BindBlockCollection`), documented on the interface; the applier's collection writes use it;
- **from a slot**: beside `EnsureNotReadFromAFilesInput`, which runs right after `ResolveStructuredSource` on both
  paths (`:110`, `:705`), a source that is a slot or a slot's item of a Send email element is refused (not inside
  `ResolveStructuredSource`: it is private static, and the Send email identity needs a `UserConnection`) (an `In` parameter only receives files; the platform's pre-save gate refuses it as
  `InvalidParameterMappingDirection`, with a less useful message, only while
  `Feature-UseVerificationOfProcessParameterDirection` is on, which is the default:
  `TS/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:252-277, 282`, `TS/Terrasoft.Core/GlobalAppSettings.cs:343`;
  the package guard holds either way). Placed there, it also covers `BindCollectionItems`.
- **a Formula element body reading a slot** (Q10 addendum): `ProcessFormulaTaskApplier` scans the expanded, bounded
  body with `EmailAttachmentSlot.TryFindSlotReadBy` before it writes the body and refuses with the same sentence; a
  scan that does not finish in time is refused (fail closed). Every route that writes a body (build, `addElement`,
  `setElement`) passes this applier.

This reaches every mapping that is not the block's own: build `mappings[]`, `addMapping`, connections, approval, the
collection mirror. `CollectionBindingRules.TakesFiles` does not change.

### 4.6 C6: order checks (Q6, Q7)

- **Declaration order (refusal, build path).** An attachment entry whose `sourceElement` is declared after the Send
  email element in `elements[]` is refused with the existing forward-reference sentence
  (`PB/Graph/ProcessGraphBuilder.cs:458-462`), the convention the data blocks already follow. The check runs on the
  planned names, before any write, so nested item paths are covered. It needs two changes: the build handler's
  `DeclarationIndex` moves before the `email` pass (today it is computed after it, `PB/Design/ProcessBuildHandler.cs:588-589`),
  and the applier receives the declaration order (an optional argument on `ISendEmailApplier.Apply`, which has none
  today and is shared with modify, or a planner result the build handler checks). `ForwardReferenceMessage` is private
  (`PB/Graph/ProcessGraphBuilder.cs:458`) and the existing walk reads top-level values only (`:377-380`), so the sentence
  is exposed as internal, or the guard calls the internal `RefuseForwardReferenceIn` (`:397`) over the written values
  of BOTH slot levels. On modify, an element that does not exist yet is refused by name resolution.
- **Flow reachability (warning, both paths).** When the source element cannot reach the Send email element along
  sequence flows, the build warns: at run time such a source has not run when the email reads it, and the email goes
  out without those files and without an error (EM10). The package already walks the flow graph breadth-first
  (`ProcessGraphBuilder.Traverse`, private, `PB/Graph/ProcessGraphBuilder.cs:2250-2270`, called by `ValidateStructure`
  on create at `:2228-2230`); the check reuses it over the schema's flows, so it also runs on modify. It is a warning, not a refusal: a source on
  ANOTHER branch of a gateway can reach the email yet still not run, and a loop can make an "after" source run first,
  so reachability is a hint, not a proof.
- `templateEntity` is not covered by either check (Q7 B, follow-up).

### 4.7 C7: describe

- `SendEmailElementHandler.Describe` (`PB/Elements/SendEmailElementHandler.cs:105-172`) adds `Attachments`.
- The generic element `parameters[]` projection (`PB/Describe/ProcessDescriber.cs:197-218`, decoded by
  `ProcessParameterService.DecodeElementSource`, `PB/Parameters/ProcessParameterService.cs:366-396`) does NOT decode a
  Send email slot or its item into `sourceElement` / `processParameter` names: it reports them raw, because those names
  would be re-sent as `addMapping`, which C5 refuses. The non-decoding uses C5's guard predicate
  `EmailAttachmentSlot.IsReservedName` (not `IsSlot`), so a foreign-stamped `Attachments2` or a near-miss
  `attachments1` is not decoded either, and describe never offers names that a write refuses.
- `DescribeAttachments` reads each slot with a binding reader generalized from `ProcessFileProcessingFilesBinding.Read`
  (`PB/FileProcessing/ProcessFileProcessingFilesBinding.cs:16-60`, parametrized by the two names) and decodes the
  source with `FileProcessingBlockDescriber.DecodeValueSource`.
- Issues (designer-built or raw-written shapes the package never writes):

| Stored shape | Issue sentence (draft) |
|---|---|
| outer bound, item unbound | "'Attachments2' is bound to 'OF1.ObjectFiles' but its file item is not, so the email fails when it reads the files; send this entry again through email.attachments to repair it." |
| item bound to a collection item, outer unbound | "'Attachments2' reads one item of 'OF1.ObjectFiles' outside its collection, so the email fails at run time; send the entry with the whole collection." |
| outer and item bound to different sources | "'Attachments2' takes its rows from 'X' but its file from 'Y'; the email attaches the wrong files or fails." |
| single File item under a collection-bound outer (stale parent, EM1d) | "'Attachments2' attaches 'Doc' once per row of 'OF1.ObjectFiles'." (wording after EM1d) |
| nothing bound | "'Attachments2' is empty and attaches nothing; the designer removes such a row when it saves." |
| slot created in another schema | "'Attachments2' was created in another process schema, so this process never sends it." |
| slot reads an unscoped Process file source (designer-built) | "'Attachments2' reads 'OF1', which has no record scope and no filter condition, so the email sends up to N arbitrary files; a write through this tool refuses it until OF1 is scoped." |
| multi-instance Send email (designer-built, `MultiInstanceOptions` set, Q16): the conversion moved every root parameter, the slots included, into the item properties of `InputRecordCollection` (`TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-activity-schema.js:317-338`), where the run time's top-level slot search (`TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:78-98`) does not look (traced, not measured). Each moved slot is reported as found, with this issue | "'SE1' is a multi-instance Send email, so 'Attachments2' is an item of its InputRecordCollection, where the run time does not look for attachments; attachments of a multi-instance Send email are not supported through this tool." |

### 4.8 Feature `UseProcessEmailAttachments`: no build-time check (Q9)

The run time honours the feature for the user the process runs as
(`PD/EmailTemplateUserTaskSender/EmailTemplateUserTaskSender.cs:227`, `PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:207-210`):
with it off, the email, or the email activity in manual mode, has no files, silently. The package does not check it.
It is a rollout toggle shipped with the feature in 7.17.1 (added 2020-11-19, switched on 2020-11-25), on for "All
employees" and "All external users" through `AdminUnitFeatureState` rows that every install force-updates
(`IsForceUpdate: true`, `PD/../Data/AdminUnitFeatureState_UseProcessEmailAttachments/descriptor.json`), and never
switched off (history: decisions Q9). No feature reader, no feature-off notice; the guide carries one troubleshooting
line (section 7).

### 4.9 Security

- Authority is the designer's: saving a process through the builder needs the same process-design rights as saving
  it in the designer. The feature adds no privilege.
- The run time reads attachment files with rights disabled (`PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:76-104`)
  and sends them out of the system. A Process file Object attachments element with no record scope and no selecting
  filter reads "up to N arbitrary files"; saving such files is already refused (ENG-92719 decision D16), but using them
  in the process only gets a notice from that element's own describe, and no notice fires when a later request wires
  the element into an email (`PB/FileProcessing/ObjectFileProcessingNoticeRule.cs:62-104`). The selection itself
  applies no record rights (`UseAdminRights = false`, `PD/FileProcessing/FileProcessing.cs`), and the Process Designer
  has no check at all (decisions Q20).
- **Q20 (agreed): an unscoped Process file source is refused**, for Object attachments AND Generated report, through the
  D16 machinery that already exists. As built (2026-10-29, after the slice B review; supersedes the per-write tracking
  first drafted here):
  - `FileProcessingFilterLedger.EnsureSelectingFilters` runs at the END of EVERY request (build, modify, version save)
    and sweeps every Send email slot of the FINAL schema (`EmailAttachmentSources`), following the path the files take:
    process parameters, `[Parameter:]` segments in formulas, element parameters' stored values (the Process parameter
    variant's `Files` -> `ObjectFiles` among them, a Formula element result onto that `Files` input included, fixed
    2026-10-30), Formula element results; an undecodable value fails closed. Every Process file element reached is
    judged; a sub-process OUTPUT is not followed and gets a notice (decisions Q20, "Indirect sources"), and so, on
    each side, do files passed INTO a Sub-process `In`/`Variable` parameter from an unscoped source and a slot that
    reads an `In`/`Variable` process parameter (owner's choice 2026-10-30): the other side is another schema;
  - `ObjectFileProcessingEmptyFilterRule.Judge` refuses an unscoped element that a Send email sends whatever its action
    (today it refuses only a saving one); `ReportFileProcessingEmptyFilterRule` refuses an unscoped one whatever its
    printable type, and fails closed on a printable it cannot read;
  - because the sweep reads the final schema, the order of operations in a modify batch does not matter, a later
    `setFilter` / `clearFilter` that leaves the source unscoped is refused too, and a designer-built violation is
    refused on the next builder save of any kind (decisions Q20, "Every request is judged").
  A filter that selects something but spans every record (file name only) passes: that is the user's intent.
- The guide says that attachments are read with system rights, not the process user's.


### 4.10 Reuse round (after the stand run)

A read-only comparison of the package code with core, the compiled configuration, CrtProcessDesigner and
PackageStore found one place to call a public core API and some duplication inside the package. Applied in the same
PR, behaviour-preserving except where stated:

- **Stored filter read as the run time reads it.** The Q20 sweep and the SysFile notice read a Process file
  element's stored filter with `Terrasoft.Common.Json.Json.Deserialize` into the platform's
  `Terrasoft.Nui.ServiceModel.DataContract.Filters` model (as `ProcessFilterFactory` does), not with a hand-written
  JSON walk. A disabled condition spelled with another letter case or as text no longer reads as selecting
  (it was the one fail-open). Fail closed on: text carrying `$id`/`$ref` metadata (the deserializer would build a
  cyclic graph), a tree with a shared or repeated item or over the item budget, and, for Object attachments only,
  text without the data-contract marker (the Object run time reads such text with the legacy reader:
  `ProcessConvertorUtilities.GetIsDataContractFilter`). Package-written and designer-written filters carry the
  marker and are unaffected.
- **Flags read as the run time reads them.** The filter descriptor reader reads `isNot`, `logicalOperation` and the
  other flags case-insensitively with the run time's conversions; a flag it cannot read is a loss. A negated
  record condition is never lifted as the record scope and an OR root is never split, in any spelling (before,
  `"IsNot":true` was lifted and rewritten as a positive scope). Measured on the PackageStore corpus: no stored
  filter reads differently (1,479 texts, 2026-10-30).
- **Formula element body** reading a slot is refused (4.5, Q10 addendum).
- **Formula into a `Files` input.** A Formula element result onto a Process parameter element's `Files` input is
  followed by the Q20 sweep (4.9; fixed 2026-10-30, TC-U-91); before, those files reached `ObjectFiles` unchecked.
- **Sub-process hand-off notices.** Files passed into a Sub-process from an unscoped source, and a slot reading an
  `In`/`Variable` parameter, get a notice on each side, not a refusal (4.9, decisions Q20; TC-U-92).
- **Wording.** A Q20 refusal for a printable the run time does not generate (neither MS Word nor FastReport) no
  longer says it reports on every record.
- **Consolidation.** One body for the slot-as-source refusal; parameter walks at any depth through the element
  locator (with its cycle guard); one helper that types a parameter; notice literals through `SafeText`; the
  attachment applier binds the file item the planner found by type; the planner takes the request's Send email
  identity.

## 5. Messages

### 5.1 Refusals (drafts; the review settles the final wording)

| Case | Draft |
|---|---|
| entry names no source | "Send email element 'SE1': email.attachments[2] names no source. Send exactly ONE of 'processParameter', 'sourceElement' + 'sourceElementParameter', or 'expression'." |
| more than one source | "... email.attachments[2] must set exactly ONE of ..." |
| `value` | "... email.attachments[2].value is not accepted - a file has no constant form. Pass the files from a process parameter or an element output." |
| unknown element, parameter, item | existing locator messages, prefixed with the entry path |
| holds no files | "... email.attachments[2] 'RD.ResultCompositeObjectList' holds no files (it is neither a File nor a collection with a File item), so the email would attach nothing. Pass a Process file element's output (ObjectFiles, ReportFiles), a FileCollection or a File." |
| collection with several File items | the shared planner sentence ("... is a collection with several File items (...), so which file each row hands on is not decided", `PB/FileProcessing/ProcessFileProcessingBinder.cs:218-224`), prefixed with the entry path |
| nested collection | the shared R-M3 sentence (`CollectionBindingRules.NestedSourceSentence`) |
| the element itself as source | "... email.attachments[2] names the element's own parameter." |
| source declared later (build) | the existing forward-reference sentence, naming the entry |
| not a Send email element | existing `EnsureSendEmailElement` refusal (the attachments count as configuration) |
| multi-instance Send email (Q16, `MultiInstanceOptions` set; also for `[]`) | "Send email element 'SE1' is multi-instance: attachments of a multi-instance Send email are not supported through this tool." (a remedy naming the multi-instance Sub-process pattern is added only if EM8 passes) |
| unscoped Object attachments source (Q20, judged at the end of the request) | "Process file element 'OF1' has no record scope and no filter condition, so it hands up to N ARBITRARY files of 'Contact' to Send email 'SE1', which sends them outside Creatio. Scope OF1 first: <the existing scope remedy>." |
| unscoped Generated report source (Q20) | the existing D16 report refusal ("generates one report for EVERY record of ..."), extended with "... and Send email 'SE1' sends them" |
| a slot still read by something (replace removal) | "... cannot remove 'Attachments2': it is read by <dependents>." |
| raw `addMapping` onto a slot | "'Attachments2' is an attachment of Send email element 'SE1' and is set through the element's email.attachments; addMapping is refused on it. Send the element's attachments list with setElement instead." |
| mapping from a slot | "'SE1.Attachments2' only receives files; it cannot be the source of a mapping." |

### 5.2 Notices (warnings)

| Case | Draft |
|---|---|
| single File source | "email.attachments[2] attaches the single file in 'Doc'. If 'Doc' is empty when the email runs, the element fails." (after EM8) |
| `expression` source | the shared planner's notice (`PB/FileProcessing/ProcessFileProcessingBinder.cs:300-301`), worded for the slot item: "email.attachments[2].expression is written onto 'Attachments2.Attachments2FileLocator' as given, and the result is not checked: the formula must yield one file. If it yields none when the email runs, the element fails." (an unbound outer with a bound item gives one row, `TS/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:444-452`, and a null locator is read, `PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-119`) |
| the same source twice | "email.attachments[1] and [3] read the same files, so each file is attached twice." |
| slots removed by the replace rule | "Removed attachment 'Attachments2' (it read 'OF1.ObjectFiles'): the sent list no longer names it. To restore it, add { \"sourceElement\": \"OF1\", \"sourceElementParameter\": \"ObjectFiles\" } to the list." |
| source not on a path before the email | "email.attachments[2] reads 'OF1', which does not lie on a path before 'SE1'. If OF1 has not run when the email runs, the email goes out without these files and without an error." |

When notices fire: the single-File, expression, duplicate and reachability notices fire for every entry of a SENT list, kept or
new; the removal notice fires per removed slot; an absent `attachments` member raises none.

## 6. clio

| # | Change | Where |
|---|---|---|
| K1 | Rebundle the attachments cut; pins | `CLIO/clio/CrtProcessBuilder/*.gz`, `CLIO/clio.tests/Common/BundledProcessBuilderPackageTests.cs` |
| K2 | Key check: `attachments` in the email key set; a child key set for the entry; `slot`, `bindingLevels`, `issues` as read-back fields | `CLIO/clio/Command/ProcessModel/ProcessBlockKeyCheck.cs:185-193` |
| K3 | Floors on create, modify, modify-as-new-version raised to the final cut; description literals and their pins | `CreateBusinessProcessCommand.cs:287`, `ModifyBusinessProcessCommand.cs:226`, `ModifyProcessAsNewVersionCommand.cs:90`, tool descriptions, `ProcessDesignerRequiresPackageAttributeTests.cs`; the e2e floor sentences and `IgnoreWhenProcessBuilderIsBehind` literals that name the create/modify floor (`CLIO/clio.mcp.e2e/*ToolE2ETests.cs`, grep the old literal) and `CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:2154` (precedent: commit `2a14c6b2c`) |
| K4 | Tool descriptions: create (`attachments?` in the email shape + guide pointer), modify ("attachments replaces the list: to add one, send the described list plus the new one"), describe (read-back line); swaps to stay under the ceiling (Q12) | `CreateBusinessProcessTool.cs`, `ModifyBusinessProcessTool.cs`, `DescribeProcessTool.cs`, `ModifyProcessAsNewVersionTool.cs` |
| K5 | Prompts: the Send email sentence and the Process file consumer sentence | `CLIO/clio/Command/McpServer/Prompts/ProcessDesigner/*` |
| K6 | Typed `Attachments` on `DescribedEmail` (keep the extension bag) | `CLIO/clio/Command/ProcessModel/IProcessDescriber.cs:1142-1250` |
| K7 | Read-back checks: "attachments did not land" (sent N, described fewer) and "N attachments were removed by the replace rule"; qualitative wording, no version in the message | `EmailBlockExpectation.cs`, `BlockExpectationIntent.cs`, `BlockExpectationReporter.cs` |
| K8 | Capability map: email block members, floor history | `CLIO/docs/McpCapabilityMap.md` |
| K9 | Curated knowledge names re-pinned to the generation that publishes the new article | `CLIO/clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` |
| K10 | MCP e2e `SendEmailAttachmentsToolE2ETests` | `CLIO/clio.mcp.e2e/` |
| K11 | Knowledge records for facts the code does not say (silent drops, manual-mode evidence path) | `CLIO/docs/knowledge/ProcessModel/` |
| K12 | Review only, stated in the PR body as reviewed with no update required: MCP resources (no process resource in `CLIO/clio/Command/McpServer/Resources/`), the shipped templates (`CLIO/clio/tpl/**` names no process-designer tool); no `McpToolCompatibilityCatalog` entry (no tool renamed) | - |

ClioRing: "ClioRing compatibility reviewed, no Ring-consumed contract changed". Ring calls none of these tools. It
reads every tool's `get-tool-contract` index entry and can fetch a named contract
(`CLIO/clio-ring/ClioRing.Ipc/ClioIpcClient.cs:188, 216`), but this change edits description text only: the
index-entry shape does not change, and the K4 swaps keep each description's first sentence, from which the Purpose is
distilled (`CLIO/clio/Command/McpServer/Tools/ToolContractGetTool.cs:392`). The PR states it with the inspected paths. Command docs: the process tools are MCP-only; "docs reviewed, no update required" is expected.

## 7. Guidance (clio-knowledge)

New article `process-send-email-attachments` (about 4-6 KB), owned topics:

1. The block: shape, one entry per attachment row, the source vocabulary shared with `fileProcessing.files`, and the
   word clash ("attachments" in `email` = files the email SENDS; `fileProcessing.attachments` = files a Process file
   element READS).
2. Picking a source: a Process file element for record attachments or a generated report; a process FileCollection or
   File for files passed in; several rows, each a collection of any size.
3. Order: configure the Process file element first; keep it on the flow BEFORE the Send email; a source that has not
   run gives an email without files and no error (EM10).
4. Modify: describe IMMEDIATELY before -> edit the list -> send it back (no concurrency token: a stale list deletes
   rows added since); `[]` removes all; the removal notice carries what was removed; recipients still append.
5. Run time: works in manual and automatic mode, custom and template message; manual mode puts the files on the draft
   email activity; a row whose file is empty (a single File, an `expression` that yields nothing, a collection row
   without a file) fails the element (traced: a null locator is read, `PD/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-119`); files are read with system rights, not the process
   user's; a generated report is a Word file.
6. Troubleshooting: an email without its files -> the source's filter, whether the source ran, and one line for the
   feature (Q9): "an email without its files: check feature UseProcessEmailAttachments for the user who runs the
   process".

Edits elsewhere: `files.md` (the sentence "Send email attachments ... are not buildable here yet" splits; the test
`ProcessFileGuidanceTests` that pins it changes in the same PR), `send-email.md` (owned-scope line and a pointer),
`element-catalog.md` (Send email row), `process-modeling.md` (route line, paid by a swap), `routing.md` (a
`name=process-send-email-attachments` row beside the Send email rows; `ProcessGuideCrossReferenceTests` requires it for
every declared process article), `bundle-source.json` (resource with NO `legacyUris`, `requirements.itemIds` and
`resourceUris`, the `process-send-email` AND `process-modeling` entry descriptions, `libraryVersion` above master and
every open branch claim; the `sequence` is derived from `libraryVersion` at build time and an authored `sequence` field
is refused, `KB/CONTRIBUTING.md:166-171`, `GenerationSequenceTests.RepositoryManifest_ShouldNotDeclare_AnAuthoredSequence`), `GuidanceMigrationTests` (`PostMigrationGuidance` gains the new name). Tests: `SendEmailAttachmentsGuidanceTests` (its `Guide_Should*`
methods: contract, order rules, the feature troubleshooting line, the design target;
`NoArticleOrDescription_ShouldClaimSendEmailAttachmentsAreNotBuildable`: the retired-claim sweep), size gates. Every claim is fact-checked by refute-first agents that may not cite
our own records (owner rule) before the PR leaves draft.

## 8. What is deliberately not built

- A flow-reachability REFUSAL (Q6 B): reachability is only a warning (4.6).
- `TakesFiles` membership for email slots (Q8 B).
- Creatio.ai `Files<N>` slots (Q15).
- Attachments on a multi-instance Send email (Q16): refused (4.4, 5.1), reported by describe with an issue (4.7). The
  per-recipient pattern is a multi-instance Sub-process, documented only if EM8 passes. A `removeElement` guard for a
  source is a follow-up only if EM11 shows a silent break.
- A build-time check of feature `UseProcessEmailAttachments` (Q9): no reader, no notice (4.8).
- Recipient replace semantics (Q4 note), `templateEntity` order guard (Q7 B).
- Any change to `ProcessParameterService`'s type allow-list: element-owned slots never pass through it.
