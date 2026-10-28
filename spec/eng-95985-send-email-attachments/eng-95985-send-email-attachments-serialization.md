# ENG-95985 Send email attachments: serialization

Written 2026-10-28.

Basis:
- crt-process-builder `origin/main` `389198a0` (CrtProcessBuilder 1.6.6.125)
- clio `origin/master` `358e9db52`
- clio-knowledge `origin/master` `534e913`
- CrtProcessDesigner 7.8.0 and the PackageStore corpus (branch `7.8.0` checkouts)
- core `TSBpm` trunk `8f6745caa` (2025-07-21)
- stand `Creatio` (a registered clio environment): read-only inventory of 2026-10-28 (EM0, partial)

| Prefix | Root (paths after the prefix are repository-relative) |
|---|---|
| `PB/` | `packages/CrtProcessBuilder/Files/src/cs/` (crt-process-builder repository) |
| `PBT/` | `tests/UnitTests/CrtProcessBuilder.Tests/` (crt-process-builder repository) |
| `PBREPO/` | the crt-process-builder repository root (repository root: `.gitattributes`, `docs/`) |
| `PD/` | `CrtProcessDesigner/branches/7.8.0/` (PackageStore repository) |
| `PS/` | the PackageStore repository root |
| `TS/` | `TSBpm/Src/Lib/` (core repository) (client files under `TS/Terrasoft.Nui/Resources/Terrasoft/`) |
| `CLIO/` | the clio repository root |
| `KB/` | the clio-knowledge repository root |
| `CLIOMAIN/` | a local clio checkout (only for an untracked local capture; not in the repository) |

Tags: [source] read in code; [corpus] real stored metadata or resources; [doc] documentation; [measured <date>] a
stand or file measurement (prior ones name where they are recorded); [traced, not measured] a source trace of run-time
behaviour; [inferred] my inference. Decision ids (Q1-Q20) are the decisions file's; EM ids are the measurements file's.

Siblings: [README](README.md) · [decisions](eng-95985-send-email-attachments-decisions.md) ·
[design](eng-95985-send-email-attachments-design.md) · [plan](eng-95985-send-email-attachments-plan.md) ·
facts, measurements, test plan, estimate (same folder).

## 0. Summary

- One attachment row = one outer parameter `Attachments<N>` (CompositeObjectList, In) owned by the Send email element,
  with exactly one nested item `Attachments<N>FileLocator` (FileLocator, In). Both levels carry the PROCESS schema
  UId as `A3`/`A4`/`GS5` and the element as `IL2`. Key sets are exactly `BL1, UId, A2, A3, A4, IL2, L1, L8, L12, L18`
  (outer) and the same without `L18` (item). Captions and display values live in resources only.
- The corpus has four such rows, all `Attachments1`, all bound on BOTH levels by Script, all from Object or Report
  sources, captured 2021-2022. Everything else (2+ rows, a gap, the Process-parameter variant, process File and
  FileCollection sources, the current designer) is [source] or [inferred] until EM1.
- The package can already write each level with its existing primitives, with one known resource difference: for
  the OUTER level (and for any top-level source, such as a single File) it stores the bare caption as the display
  value, where the designer stores `[#<element>.<collection>#]` (section 2.2, AD1/AD2). The slot writer therefore
  writes the designer form on slots only; other mappings keep today's display values (design 3, section 6.3).
- Parity is proven three ways (section 6): committed corpus captures with a SHA pin (pattern A), the EM1 designer
  capture restated as rows (pattern B), and the platform writer's JSON diffed against a quoted slot (pattern C),
  all under one comparison rule (section 6.2). Resources are compared on the stand only (EM2).

## 1. The slot pair, key by key

### 1.1 How the platform writes a parameter, and which defaults it omits

`ProcessSchemaParameter.WriteMetaData` runs its base chain first; every `WriteValue(key, value, default)` writes
nothing when the value equals the default, unless the writer's `WriteDefValues` is set
(`TS/Terrasoft.Common/DataWriter.cs:36-41`; off by default). The schema save (the corpus omits every default) and the
pattern-C harness (`PBT/FileProcessParameterTests.cs:553`, a default `JsonDataWriterSettings`) leave it off. That
rule, not the designer, decides which keys appear. [source]

| Writer (call order) | Keys, in order | Default omitted | Evidence |
|---|---|---|---|
| `BaseProcessSchemaItem` | `BL1` TypeName, then base, then `BL11` IsValid | `""`; `true` | `TS/Terrasoft.Core/Process/BaseProcessSchemaItem.cs:118-122` (keys `:25`, `:34`) |
| `MetaItem` | `UId`, `A2` Name, `A3` CreatedInSchemaUId, `A4` ModifiedInSchemaUId, `A5` CreatedInPackageId | `Guid.Empty` / `null` | `TS/Terrasoft.Core/MetaItem.cs:258-264` (keys `:81-90`) |
| `BaseProcessSchemaElement` | `IL2` ContainerUId; `IL3`/`IL4` only under `UseBackgroundProcessMode` | `Guid.Empty`; `false` | `TS/Terrasoft.Core/Process/BaseProcessSchemaElement.cs:215-225` |
| `ProcessSchemaBaseElement` | `BL3` Position, `BL5`, `BL6`, `BL7` ManagerItemUId, `BL8`, `BL9` | empty point, 0, null, `Guid.Empty`, null | `TS/Terrasoft.Core/Process/ProcessSchemaBaseElement.cs:409-417` (keys `:37-42`) |
| `ProcessSchemaParameter` | `L1` DataValueTypeUId, `L8` SourceValue (object, ALWAYS written), `L2`-`L5`, `L6` IsRequired, `L9` ReferenceSchemaUId, `L11` IsValueSerializable, `L13`-`L16`, `IsLazy`, `L17` Tag, `L12` Direction, `SourceParameterUId`, `L18` ItemProperties | `Guid.Empty`; -; `null`; `false`; `Guid.Empty`; `true`; `false`/`""`; `false`; `""`; **`Variable`**; `Guid.Empty`; **written only when not empty** | `TS/Terrasoft.Core/Process/ProcessSchemaParameter.cs:990-1016` (keys `:123-142`) |
| `ProcessSchemaParameterValue` (inside `L8`) | `GS1` Source, `GS2` Value (when a formula or a non-localizable type), `GS4` MetaPath, `GS5` ModifiedInSchemaUId, `GS8`, `GS9` | `None`; `null`; `null`; `Guid.Empty`; `null`; `null` | `TS/Terrasoft.Core/Process/ProcessSchemaParameterValue.cs:459-470` (keys `:31-38`) |

Two consequences matter. `L12: 0` (In) is written because only `Variable` is the default (`:1009`; enum
`ProcessSchemaParameter.cs:34-54`). `L8` is always present, so "unbound" is `{}` or `{"GS5": ...}`, never a missing
key. [source]

### 1.2 Outer slot `Attachments<N>`

`<P>` = the process schema UId, `<E>` = the Send email element UId, `<S>` = the source element UId, `<C>` = the
source collection's UId, `<I>` = the UId of that collection's FileLocator item, `M` = the literal
`[IsOwnerSchema:false].[IsSchema:false].`.

| Key | Value | Why | Evidence | Tag |
|---|---|---|---|---|
| `BL1` | `Terrasoft.Core.Process.ProcessSchemaParameter` | type name | `PS/ProcessTests/branches/7.8.0/Schemas/ProcessFileSendMail/metadata.json:2931` | corpus |
| `UId` | fresh GUID per slot | identity; not compared (6.2 R1) | `:2932` | corpus |
| `A2` | `Attachments<N>` | the run time finds slots by `^Attachments\d+$` | `:2933`; `TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19, 94` | corpus, source |
| `A3`, `A4` | `<P>` | run-time own-parameter filter (`A3` only); designer "dynamic" test | `:2934-2935`; section 4 | corpus |
| `IL2` | `<E>` | the slot belongs to the element | `:2936` | corpus |
| `L1` | `651ec16f-d140-46db-b9e2-825c985a8ac2` (CompositeObjectList) | the run time hard-casts the value to a composite list | `:2937`; `TS/Terrasoft.Core/DataValueType.cs:270-271`; `PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:284` | corpus, source |
| `L8` | collection source: `{"GS1": 3, "GS2": "[#M[Element:{<S>}].[Parameter:{<C>}]#]", "GS5": "<P>"}`; otherwise `{}` (section 3) | the run time iterates the OUTER value's rows | `:2938-2942`; `EmailTemplateUserTask.cs:283-285` | corpus, source |
| `L12` | `0` (In) | designer direction; written because the default is `Variable` | `:2943`; `PD/Schemas/ProcessFilesControlSchema/ProcessFilesControlSchema.js:288` | corpus, source |
| `L18` | exactly one item (1.3) | the run time reads one key per row; the designer renders the first item of its sorted view model (I6) | `:2944-2960`; `ProcessFilesControlSchema.js:223` | corpus, source |

Order of the keys on disk follows the writer chain (1.1). The corpus order is `BL1, UId, A2, A3, A4, IL2, L1, L8,
L12, L18` in all four bindings [measured 2026-10-28: JSON parse of the three files, section 6.4].

### 1.3 Item `Attachments<N>FileLocator`

| Key | Value | Why | Evidence | Tag |
|---|---|---|---|---|
| `BL1` | `Terrasoft.Core.Process.ProcessSchemaParameter` | | `ProcessFileSendMail/metadata.json:2946` | corpus |
| `UId` | fresh GUID | not compared | `:2947` | corpus |
| `A2` | slot name + `FileLocator` | the run time reads each row by the key `<slot name> + "FileLocator"` | `:2948`; `EmailTemplateUserTask.cs:110`; designer `ProcessFilesControlSchema.js:269` | corpus, source |
| `A3`, `A4` | `<P>` | designer parity; not read by the run-time filter, which checks top-level parameters only | `:2949-2950`; `ProcessFilesControlSchema.js:267-268` | corpus, source |
| `IL2` | `<E>`, the ELEMENT, not the outer parameter | designer `processFlowElementSchema: processElement` | `:2951`; `ProcessFilesControlSchema.js:277` | corpus, source |
| `L1` | `a33c9252-d401-453e-949d-169157067ed9` (FileLocator) | a non-`EntityFileLocator` value throws on the typed read | `:2952`; `TS/Terrasoft.Core/DataValueType.cs:276`; `TS/Terrasoft.Common/CompositeObject.cs:120-127` | corpus, source |
| `L8` | collection source: `{"GS1": 3, "GS2": "[#M[Element:{<S>}].[Parameter:{<I>}]#]", "GS5": "<P>"}` | the reference names the item's OWN UId with no collection segment (`GetMetaPath` of a parameter whose container is the element) | `:2953-2957`; `TS/Terrasoft.Core/Process/ProcessSchemaParameter.cs:1072-1085` | corpus, source |
| `L12` | `0` (In) | | `:2958`; `ProcessFilesControlSchema.js:271` | corpus, source |

The referenced `<C>` and `<I>` are the source ELEMENT INSTANCE's parameter UIds (`ObjectFiles` `19790e10-...`, `File`
`82f11a20-...` in `ProcessFileSendMail`), not the user-task template's (`b80cbf95-...`, `1ddb6de7-...` at
`PD/Schemas/ObjectFileProcessingUserTask/metadata.json:120, 130`). [corpus]

### 1.4 Keys that are absent, and why

| Absent key | Reason | Evidence | Tag |
|---|---|---|---|
| `A5` CreatedInPackageId | never stamped. The save stamps `A5` only on the items `GetMetaItems` returns: the schema, methods, lanes, artifacts and flow elements; an element contributes only itself, never its parameters. The client strips `packageUId` from serialized properties, and `InsertItem` does not back-fill it. Corpus: each Send email element carries `A5`, its slots do not; the template parameters that carry one (`A3` = the user-task UId) copy it from the template. A package build saved through the same path gets none either [inferred; EM2 shows it] | `TS/Terrasoft.Core/Process/ProcessSchemaManager.cs:158-166` -> `TS/Terrasoft.Core/SchemaManager.cs:2879-2887`; `TS/Terrasoft.Core/Process/ProcessSchema.cs:1528-1532`; `TS/Terrasoft.Core/Schema.cs:843-848`; `TS/Terrasoft.Core/Process/BaseProcessSchemaItem.cs:133-135`; `TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-schema-parameter.js:619-625`; `TS/Terrasoft.Core/MetaItemCollection.cs:92-117` | source, corpus |
| `BL11` IsValid | default `true` omitted; a `false` would mark an invalidated parameter (`InvalidateDependentElements`) | `BaseProcessSchemaItem.cs:121`; `TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:273-289` | source |
| `L6` IsRequired | not set by the designer config; default `false` (the Process file `Files` input, by contrast, carries `L6: true` from its template) | `ProcessFilesControlSchema.js:262-294`; `PS/ProcessTests/branches/7.8.0/Schemas/FileParameterProcess/metadata.json:1427` | source, corpus |
| `L9` ReferenceSchemaUId | no lookup type | `ProcessSchemaParameter.cs:1001` | source |
| `L11` IsValueSerializable | default `true` omitted | `ProcessSchemaParameter.cs:1002` | source |
| `L17` Tag | empty default | `ProcessSchemaParameter.cs:1008` | source |
| `IsLazy`, `L13`-`L16`, `SourceParameterUId`, `L2`-`L5`, `BL3`-`BL9`, `IL3`/`IL4` | defaults | 1.1 | source |
| `GS4`, `GS8`, `GS9` | null in every corpus slot, and omitted when null | `ProcessSchemaParameterValue.cs:466-469`; corpus parse of the four slots | source, corpus |
| captions, display values, `L19` Description | localizable: resources only (section 2); `L19` is declared but not written | `ProcessSchemaParameter.cs:900-930`; `L19` `:116, 142`, writer `:990-1016` | source |
| `skipValidation`, `forceRemove` (item, designer session flags) | removed from the serialized properties | `process-schema-parameter.js:621-622` | source |
| `L18` on the item | no nested items | `ProcessSchemaParameter.cs:1011` | source |
| any `BK15` (`ProcessSchemaMapping`) row for either level | rows come only from the activity sync of TEMPLATE parameters; each slot and item UId occurs exactly once in its file | `TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:291-305`; corpus count | source, [measured 2026-10-28] |

Contrast on the same elements: `Recipient1` is also own (`A3` = `<P>`) but has NO `L12` (Variable), so the
direction is not shared with recipients (`ProcessFileSendMail/metadata.json:2917-2928`). [corpus]

### 1.5 Verbatim corpus slot

`PS/ProcessTests/branches/7.8.0/Schemas/ProcessFileSendMail/metadata.json:2930-2961` (element
`EmailTemplateUserTask1` `07923c69-...`, manual + custom; source `ObjectFileProcessingUserTask1` `2e9d7e71-...`;
process `30fc7866-...`). Whitespace trimmed, every key and value as stored:

```json
{
  "BL1": "Terrasoft.Core.Process.ProcessSchemaParameter",
  "UId": "19622896-e15b-40aa-bdd2-cb9f99ca3325",
  "A2": "Attachments1",
  "A3": "30fc7866-0657-4f6b-8a56-79fa7a26b610",
  "A4": "30fc7866-0657-4f6b-8a56-79fa7a26b610",
  "IL2": "07923c69-b3cb-4470-8914-b34ca51998eb",
  "L1": "651ec16f-d140-46db-b9e2-825c985a8ac2",
  "L8": {
    "GS1": 3,
    "GS2": "[#[IsOwnerSchema:false].[IsSchema:false].[Element:{2e9d7e71-d5a0-48f3-87e4-a3eaab4c0645}].[Parameter:{19790e10-f6a8-4ad0-890c-f5eae4a6fe9d}]#]",
    "GS5": "30fc7866-0657-4f6b-8a56-79fa7a26b610"
  },
  "L12": 0,
  "L18": [
    {
      "BL1": "Terrasoft.Core.Process.ProcessSchemaParameter",
      "UId": "15173c5e-5661-4375-9259-2b9411f3e99a",
      "A2": "Attachments1FileLocator",
      "A3": "30fc7866-0657-4f6b-8a56-79fa7a26b610",
      "A4": "30fc7866-0657-4f6b-8a56-79fa7a26b610",
      "IL2": "07923c69-b3cb-4470-8914-b34ca51998eb",
      "L1": "a33c9252-d401-453e-949d-169157067ed9",
      "L8": {
        "GS1": 3,
        "GS2": "[#[IsOwnerSchema:false].[IsSchema:false].[Element:{2e9d7e71-d5a0-48f3-87e4-a3eaab4c0645}].[Parameter:{82f11a20-5b7c-4346-964f-5736c5a7a863}]#]",
        "GS5": "30fc7866-0657-4f6b-8a56-79fa7a26b610"
      },
      "L12": 0
    }
  ]
}
```

The other three corpus slots have the same key sets, roles and value forms: `ProcessFileSendMail/metadata.json:3936-3967`
(auto + custom, Report), `PS/ProcessTests/branches/7.8.0/Schemas/ProcessFileReport/metadata.json:2652-2683`
(manual + template, Report), `PS/ProcessTests/branches/7.8.0/Schemas/MailConnectionsProcess/metadata.json:2486-2517`
(auto + template, Object). The same generic files-control shape with prefix `Files` exists in a process created in
8.3.1.3829 (`B8`, the creation version: `TS/Terrasoft.Core/Schema.cs:82`) and last modified 2025-10-10
(`SkillFilesValidationProcess/descriptor.json` `ModifiedOnUtc`)
(`PS/CopilotAutoTest/branches/7.8.0/Schemas/SkillFilesValidationProcess/metadata.json:1388-1419`), the only corpus
evidence that the shape is unchanged on a recent platform; it is not a Send email capture. [corpus]

## 2. Resources

### 2.1 Keys

The resource group of a slot is `BaseElements.<element name>.Parameters.<slot name>`
(`TS/Terrasoft.Core/Process/ProcessSchema.cs:1401-1404`; `TS/Terrasoft.Core/Process/ProcessSchemaParametrizedFlowNode.cs:67-73`).
A parameter registers `Caption`, `Group`, `Sys_Description`, `Value` (only for a localizable or MetaDataText type,
neither of these two), `DisplayValue`, and recurses into each item under `<group>.<item name>`
(`ProcessSchemaParameter.cs:900-930`). Empty values produce no row: the corpus has exactly four rows per slot and no
`Group`, `Sys_Description` or `Value` row. [source, corpus]

| Key (per slot) | Designer value | Evidence | Tag |
|---|---|---|---|
| `BaseElements.<El>.Parameters.Attachments<N>.Caption` | `Attachments collection` | `PS/ProcessTests/branches/7.8.0/Resources/ProcessFileSendMail.Process/resource.en-US.xml:14`; `PD/Schemas/EmailTemplateUserTaskPropertiesPage/EmailTemplateUserTaskPropertiesPage.js:384` | corpus, source |
| `BaseElements.<El>.Parameters.Attachments<N>.DisplayValue` | `[#<source element caption>.<collection caption>#]`, e.g. `[#PF.Collection of files#]` | `resource.en-US.xml:15` | corpus |
| `BaseElements.<El>.Parameters.Attachments<N>.Attachments<N>FileLocator.Caption` | `Attachment` | `:12`; `EmailTemplateUserTaskPropertiesPage.js:385` | corpus, source |
| `BaseElements.<El>.Parameters.Attachments<N>.Attachments<N>FileLocator.DisplayValue` | `[#<source element caption>.<collection caption>:<item caption>#]`, e.g. `[#PF.Collection of files:File#]` | `:13` | corpus |

The same four keys exist for the other three bindings (`ProcessFileSendMail.Process/resource.en-US.xml:88-91`,
`ProcessFileReport.Process/resource.en-US.xml:11-14`, `MailConnectionsProcess.Process/resource.en-US.xml:57-60`).
B4's resource file is not from the same save as its metadata: it has no row for the bound source
`ReportFileProcessingUserTask5` (nor `ReportFileProcessingUserTask4`, `6`, `ReadDataUserTask3`), it keeps rows for
`ReportFileProcessingUserTask1-3` and `ReadDataUserTask2`, which the metadata lacks, and its DisplayValue
`[#ProcessReport3...#]` is `ReportFileProcessingUserTask2`'s caption (`resource.en-US.xml:141`). Only the B1-B3
resources are designer display-form evidence; `PROVENANCE.md` records this (6.4). [corpus]
Captions are not numbered (recipients are: "To 1") and are hard-coded English strings in the page, not page
resources (`EmailTemplateUserTaskPropertiesPage.js:381-387`). [corpus, source]

Keys embed the element NAME and the slot NAME, not UIds. A slot must therefore never be renamed once written (Q4
keeps names), and two slots with one name on one element would collide on every key; no platform collection refuses
a duplicate name: `MetaItemCollection.EnforceUniqueName` is `false` (`TS/Terrasoft.Core/MetaItemCollection.cs:55, 79`),
and no process parameter collection overrides it (`TS/Terrasoft.Core/Process/ProcessSchemaParameterCollection.cs:72-78`,
`TS/Terrasoft.Core/Process/ProcessSchemaNestedParameterCollection.cs:40-43`); only non-process collections do
(`TS/Creatio.Copilot/Metadata/CopilotActionMetaItemCollection.cs:61`, `TS/Terrasoft.Services/Metadata/ServiceParameterCollection.cs:42`). [source]

### 2.2 Display values: designer form and what the package writes today

| Level | Designer | Package today (existing `ProcessMappingService` primitives) | Evidence | Tag |
|---|---|---|---|---|
| item bound to a collection item | `[#<el>.<coll>:<item>#]`; for a process collection `[#<param>:<item>#]` | the same (ENG-92719 D20) | `PBT/MappingNestedDisplayValueTests.cs:74-75`; `PB/Mappings/ProcessMappingService.cs:963-972` | source |
| outer bound to a collection | `[#<el>.<coll>#]`; for a process collection `[#<param>#]` | **the bare caption** (`Collection of files`) | designer `PD/Schemas/MappingEditMixin/MappingEditMixin.js:534-540, 1101-1115`; package `ProcessMappingService.cs:965-967`, pinned by `PBT/MappingNestedDisplayValueTests.cs:76-77` | source, corpus |
| item bound to a single top-level File | `[#<File caption>#]` or `[#<el>.<File caption>#]` | the bare caption | designer [inferred] from the mask the page applies (`MappingEditMixin.js:539`); package `ProcessMappingService.cs:965-967` | inferred, source |

The process-collection forms are corpus on the Process file element: `[#FileCollection#]` and
`[#FileCollection:FileCollectionParameter#]` (`PS/ProcessTests/branches/7.8.0/Resources/FileParameterProcess.Process/resource.en-US.xml:40, 42`).
On the Send email they are [inferred] until EM1. The two package-side differences are AD1 and AD2 (section 6.3). The
designer's file row shows the ITEM's value (`ProcessFilesControlSchema.js:218-228`), so AD1 is likely invisible in the
designer and AD2 is visible [inferred; EM3 shows it].

### 2.3 Culture

- The designer creates both captions with `cultureValues: <plain string>`, i.e. in the current user's culture
  (`ProcessFilesControlSchema.js:264-266, 281-283`). Because the strings are hard-coded English, a ru-RU designer user
  would store English text under ru-RU [inferred]. The corpus has en-US rows only; the three ru-RU resource files carry
  no `Attachments` key [corpus, measured 2026-10-28 by grep].
- The package writes localizable values in the thread's current culture (`TS/Terrasoft.Common/LocalizableValue.cs:269-276`),
  which is the request's culture [inferred].
- Parity therefore compares the authoring culture only (6.2 R10); EM1 records the designer user's culture and EM2 the
  package's.

### 2.4 Resources when a slot is removed

- Designer: clearing a row and saving removes the outer parameter, and with it its item, on the client, after
  `canRemoveParameter` allows it (`ProcessFilesControlSchema.js:376-419`, check `:392-405`; client collection
  `TS/Terrasoft.Nui/Resources/Terrasoft/manager/service-schema-manager/process-schema-parameter-collection.js:171-178`). [source]
- Package (design 4.3 `Remove`): `Parameters.Remove` after `IProcessDependencySnapshot.FindDependents(element, slot)`,
  refused with the dependents named. The package has no mechanism that deletes `SysLocalizableValue` rows. [source]
- Server: a removed parameter is no longer walked by `GetLocalizableValues` (`ProcessSchemaParameter.cs:932-950`), so
  its four values are not part of the next save. Whether the save deletes the four `SysLocalizableValue` rows or
  leaves them orphaned is not traced [inferred]. EM1 (S2: clear row 2 and save) and EM2 record the rows before and
  after, and one save clears a collection-sourced row and adds a single-File row that reuses the freed number (7.2).
  If a stale outer DisplayValue survives under the reused name, the writer writes an explicit empty outer
  DisplayValue for every reused or unbound outer.
- Kept slots keep their keys (names unchanged), so their rows are untouched by a removal elsewhere [inferred].

## 3. Binding shapes per source kind

`M` = `[IsOwnerSchema:false].[IsSchema:false].`; `<S>` source element, `<C>` its collection, `<I>` the collection's
FileLocator item, `<D>` a process FileCollection, `<Di>` its item, `<F>` a single File parameter, `<Fo>` an element's
single FileLocator output. "Builder" is the agreed contract (Q1-Q3, agreed 2026-10-28; design 3.1).

| Source kind (entry) | Outer `L8` | Item `L8` | DisplayValue outer / item (designer) | Tag |
|---|---|---|---|---|
| Object attachments, `OF1.ObjectFiles` (or `OF1.ObjectFiles.File`) | `{GS1:3, GS2:"[#M[Element:{<S>}].[Parameter:{<C>}]#]", GS5:<P>}` | `{GS1:3, GS2:"[#M[Element:{<S>}].[Parameter:{<I>}]#]", GS5:<P>}`, item `File` | `[#OF1.Collection of files#]` / `[#OF1.Collection of files:File#]` | corpus (B1 `ProcessFileSendMail:2930-2961`, B3 `MailConnectionsProcess:2486-2517`) |
| Generated report, `RF1.ReportFiles` (or `.File`) | same form, `<C>` = `ReportFiles` | same form, item `File` | `[#RF1.Collection of reports#]` / `[#RF1.Collection of reports:File#]` | corpus (B2 `ProcessFileSendMail:3936-3967`, B4 `ProcessFileReport:2652-2683`) |
| Process-parameter variant, `PF1.ObjectFiles` (or `.ObjectFile`) | same form, `<C>` = `ObjectFiles` (Out) | same form, item **`ObjectFile`** (found by TYPE, not name) | `[#PF1.Collection of files#]` / `[#PF1.Collection of files:ObjectFile#]` | source (`PD/Schemas/ProcessFileProcessingUserTask/metadata.json:17-27`); inferred display from the variant's captions (`FileParameterProcess.Process/resource.en-US.xml:43-44`); EM1 |
| process FileCollection `Docs` (or `Docs.<item>`) | `{GS1:3, GS2:"[#M[Parameter:{<D>}]#]", GS5:<P>}`: no element segment | `{GS1:3, GS2:"[#M[Parameter:{<Di>}]#]", GS5:<P>}` | `[#Docs#]` / `[#Docs:<item caption>#]` | source (`ProcessSchemaParameter.cs:1078-1080`); corpus on the Process file element (`FileParameterProcess/metadata.json:1422-1441`); inferred for the email; EM1 |
| a collection ITEM (`OF1.ObjectFiles.File`, `Docs.File`) | bound to the item's parent collection | bound to the item | as its collection | source (designer binds the parent, `MappingEditMixin.js:947-958, 981-996`); builder `PB/FileProcessing/ProcessFileProcessingBinder.cs:188-194` |
| single File: process `Doc`, or an element output `<Fo>` | **not bound**: `{}` (or `{GS5:<P>}`, see 6.2 R6) | `{GS1:3, GS2:"[#M[Parameter:{<F>}]#]"` or `"[#M[Element:{<S>}].[Parameter:{<Fo>}]#]", GS5:<P>}` | no outer DisplayValue row / `[#Doc#]` | source (`MappingEditMixin.js:1075-1096`: no reset on a single-instance element); inferred bytes; EM1, run time EM8 |
| `expression` | not bound | `{GS1:3, GS2:"<formula as typed>", GS5:<P>}` | none / the formula's display text | inferred; not in EM1 (SQ5) |

Notes. [source unless tagged]
- A whole collection and one of its items store IDENTICAL bytes; describe cannot tell which form the caller sent,
  so a round trip compares the resolved binding, not the entry text (Q11).
- The item's `<I>` is found by TYPE among the collection's items, as the Process file binder does
  (`PB/FileProcessing/ProcessFileProcessingBinder.cs:209-225`); the Process-parameter variant's item is `ObjectFile`
  (`PD/Schemas/ProcessFileProcessingUserTask/metadata.json:27`).
- Not a source: `PF1.Files` / `PF1.Files.File` (the variant's INPUT, `L12: 0`). The designer's element tab drops In
  parameters, and the pre-save gate refuses a Script reference to an element parameter whose direction is In
  (`TS/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:252-262`). [source; traced, not measured]
- A process collection item stored by the designer has no `IL2` (rule N9) and a package-made one has `IL2` = `<P>`;
  `GetMetaPath` yields the same short token either way (`ProcessSchemaParameter.cs:1078-1080`).

Designer-made shapes the builder never writes (describe reports them with an issue, design 4.7; none matches a sent
entry, so a sent list replaces it, design 2.3):

| Shape | Outer `L8` | Item `L8` | Origin | Run time | Tag |
|---|---|---|---|---|---|
| item bound to the COLLECTION itself (not its item) | `{}` | `[#M[Parameter:{<D>}]#]` | the mapping window offers a collection node for a FileLocator item: measured on the Process file element, `Files` `{}` and `Files.File` = `[Parameter:{PFiles}]`, DisplayValue `[#PFiles#]` | one row whose value is a list, so the typed read throws `InvalidCastException` (`CompositeObject.cs:125`) | measured 2026-10-20 (`PBT/FileProcessingSysFileParityTests.cs:49-50, 524-526`; `CLIOMAIN/eng-103937-sc1sf/UsrFpSc1SfCapture/metadata.json`); email traced, not measured; EM1 |
| stale parent (EM1d) | the OLD collection binding | the single File | re-binding a row from a collection item to a File leaves the parent as it was | one attachment of the File per row of the old collection | source (`MappingEditMixin.js:1090-1094`); traced, not measured; EM1d |
| outer-only (raw `addMapping` today) | collection | `{}` | not produced by the designer: on every save of the element's card, `_saveFiles` removes the whole slot of a row whose ITEM is empty (`ProcessFilesControlSchema.js:376-387, 410-419`), whatever the outer holds; the outer itself is saved by `saveExtendedValues` (`PD/Schemas/ProcessFlowElementPropertiesPage/ProcessFlowElementPropertiesPage.js:1000-1009, 1019-1039`). An existing outer-only or unbound slot is therefore DELETED the next time a user opens the card and the designer saves it (when `canRemoveParameter` allows, `ProcessFilesControlSchema.js:392-405`) | rows carry `<slot>FileLocator` = null, then a NullReferenceException in `FileFactory.CreateFileInstance` | traced, not measured (`TS/Terrasoft.Core/Process/ProcessInstanceParametersDataReader.cs:481-487`); EM9 |
| item bound to a collection item, outer unbound | `{}` | `[#M[Element:{<S>}].[Parameter:{<I>}]#]` | not produced by the designer (it binds the parent) | one row evaluated outside a row: reads empty | traced, not measured (`ProcessInstanceParametersDataReader.cs:444-453`) |

## 4. Invariants the run time and the designer depend on

| # | Invariant | What breaks otherwise | Evidence | Tag |
|---|---|---|---|---|
| I1 | Slot name matches `^Attachments\d+$`, case-sensitive, anchored; the package writes ASCII digits with no leading zero | not found at run time (no error); not listed by the designer's control | `TS/Terrasoft.Core/Process/ProcessUserTaskExtensions.cs:19, 81, 94` (`new Regex(pattern)`, no options); client `TS/Terrasoft.Nui/Resources/Terrasoft/designers/process-schema-designer/process-constants.js:249`; `PD/Schemas/ProcessFilesControlSchema/ProcessFilesControlSchema.js:308-314` | source |
| I2 | The slot is a TOP-LEVEL element parameter | `ForceGetParameters()` returns top-level parameters only; a nested slot is never found | `ProcessUserTaskExtensions.cs:88-97` | source |
| I3 | Outer `CreatedInSchemaUId` (`A3`) = the process UId | ignored at run time with no error; not "dynamic" for the designer, so no row; a mapping-less parameter stamped with the user-task UId is DELETED by the activity sync | `ProcessUserTaskExtensions.cs:93-94`; `process-schema-parameter.js:1019-1025`; `TS/Terrasoft.Core/Process/ProcessSchemaActivity.cs:253-262`; server `IsDynamic` `ProcessSchemaParameter.cs:270-277` | source |
| I4 | The nested item stamped `A3` = `A4` = the process UId, explicitly | parity (the run time does not read it). `MetaItemCollection.InsertItem` back-fills a stamp only when the collection already has a `ParentMetaSchema`; `new ProcessSchemaParameter(ProcessSchema)` sets only the obsolete `ProcessSchema` property, and a later `ParentMetaSchema` propagation stamps nothing | `TS/Terrasoft.Core/MetaItemCollection.cs:62-72, 92-104`; `TS/Terrasoft.Core/Process/ProcessSchemaBaseElement.cs:60-63` vs `:69-72`; `ProcessSchemaParameter.cs:175-180, 326-331`; `TS/Terrasoft.Core/Process/ProcessSchemaNestedParameterCollection.cs:40-43`; precedent `PB/Elements/MultiInstanceApplier.cs:1027-1044` | source |
| I5 | Item name = current slot name + `FileLocator` | rows skipped silently (key absent) | `PD/Schemas/EmailTemplateUserTask/EmailTemplateUserTask.cs:107-113` | source |
| I6 | Exactly one item | an extra item is evaluated per row (a failing Script fails the element); the designer renders the first item of the view-model `ItemProperties`, sorted required-first, then by caption, not the first stored item (with exactly one item they are the same) | `ProcessInstanceParametersDataReader.cs:481-487`; `ProcessFilesControlSchema.js:223`; sort `PD/Schemas/ProcessFlowElementPropertiesPage/ProcessFlowElementPropertiesPage.js:753-758, 608-625` | source |
| I7 | `L1` by UId: outer CompositeObjectList, item FileLocator (`DataValueType.CompositeObjectListDataValueTypeUId`, `FileLocatorDataValueTypeUId`); never resolve `File` by name | outer of another type: `InvalidCastException` (hard cast); the name `File` is the binary file type `BA40CFC5-...`, not FileLocator | `TS/Terrasoft.Core/DataValueType.cs:270-276, 2135-2144`; `EmailTemplateUserTask.cs:284`; package use `PB/Mappings/CollectionBindingRules.cs:139-144` | source |
| I8 | `Direction` = In on both levels | parity; an In element parameter cannot be a mapping source (pre-save gate), which also makes a mapping FROM a slot fail the gate | `ProcessFilesControlSchema.js:271, 288`; `ParameterValuesValidationRule.cs:252-262` | source, corpus |
| I9 | Bindings are `Script` (`GS1` = 3) on both levels, never `Mapping` (2) | the pre-save flow generator registers Script values only, so a Mapping source escapes the parameter checks | `TS/Terrasoft.Core/Process/BaseFlowSchemaGenerator.cs:436-460` (skip at `:457-459`); designer: all four corpus bindings store `GS1` = 3 on both levels [corpus]; that the designer never writes 2 is [inferred], settled by EM1 record 2 | source, corpus |
| I10 | `GS5` = the process UId on every bound level | designer and corpus parity on the interpreted path: a Send email's parameters are read through `InitializeFlowElementProperties` with an unfiltered `ParametersReaderOptions`; `UseOnlyModifiedParameters` applies only to the reader built for a sub-process start; the multi-instance iteration-count reader is built with the same options type but returns before the filter. On a compiled process the code generator emits no initializer for an element parameter whose `GS5` is not the process [source; its run-time effect traced, not measured] | `TS/Terrasoft.Core/Process/ProcessComponentSet.cs:491-517`; `TS/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:773-774`; filter `:739-742`, reached only from the sub-process start `ProcessComponentSet.cs:1292-1297`; the iteration-count reader (`:1449-1452`) returns at `ProcessParameterValueProvider.cs:734-737`; `TS/Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:296-298` | source, corpus |
| I11 | `IL2` = the Send email element on BOTH levels (the item too) | parity and the designer client; the run time builds paths from the element UId | corpus `ProcessFileSendMail/metadata.json:2936, 2951`; `ProcessFilesControlSchema.js:277, 293` | corpus, source |
| I12 | No `BK15` row for either level | none known; parity (the designer writes none for dynamic parameters) | 1.4 | corpus |
| I13 | Slot names unique on the element | resource keys collide (2.1); both would be read | `MetaItemCollection.cs:55, 79` | source |
| I14 | Collection source: BOTH levels bound to the same source | outer-only: NRE; item-only on a collection item: empty; mixed sources: wrong files (section 3) | `EmailTemplateUserTask.cs:283-285`; `ProcessInstanceParametersDataReader.cs:441-489` | source; traced, not measured |
| I15 | Captions `Attachments collection` / `Attachment` in resources | parity only | 2.1 | corpus |

Every invariant except I12 and I15 gets a unit assertion on a schema with a real UId (`PBT/FileProcessingTestSupport.cs:222-230`
`CreateSchema`, `schema.UId = Guid.NewGuid()` at `:228`); `SendEmailApplierTests` builds its schema with `UId` = empty (`PBT/SendEmailApplierTests.cs:48`), where
an `A3` assertion passes vacuously. [source]

## 5. Numbering

### 5.1 Designer

`generateItemUniqueName(prefix, collection, filterFn, startIndex)` starts at 1 and returns the first `prefix + k`
that no item of the collection carries (`PD/Schemas/ProcessSchemaUserTaskUtilities/ProcessSchemaUserTaskUtilities.js:465-479`),
called with prefix `Attachments` and every parameter in `processElement.parameters`, exact name compare
(`ProcessFilesControlSchema.js:244-249, 259-260`). That client collection is flat, roots and nested items alike
(`TS/Terrasoft.Nui/Resources/Terrasoft/manager/service-schema-manager/process-schema-parameter-collection.js:101-112`;
`filterByFn` is the base `Terrasoft.Collection`'s, over the flat list), when the client feature `ProcessParameterCollections` is on or in
debug; otherwise it is a roots-only `Terrasoft.Collection`
(`TS/Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/parametrized-process-schema-element.js:37-39`),
in which the files control's item lookups (`findParameterByName(<slot>FileLocator)`, `ProcessFilesControlSchema.js:343, 380`)
find nothing (7.1 records the flag). Either way item names end in `FileLocator`, so only `Attachments<k>` roots compete. Gaps are refilled; nothing is ever renumbered; numbering is per element (both elements of
`ProcessFileSendMail` carry `Attachments1`). [source, corpus]

### 5.2 Package today (recipients)

`NextRecipientIndex` returns max + 1 (`PB/Email/SendEmailApplier.cs:854-867`), while its doc comment says "the first
free N (the designer numbers the same way)" (`:791`, `:851`). It matches names with `OrdinalIgnoreCase` and
`int.TryParse` (`:858-862`), which is looser than the run time's case-sensitive `\d+`. [source]

### 5.3 Agreed rule (Q5 = A, with Q4 = A, agreed 2026-10-28)

- A NEW slot takes the lowest `k >= 1` such that no parameter of the element (every parameter of the element,
  nested items included, whatever its `A3`) is named `Attachments<k>` (ordinal, case-sensitive). Designer parity,
  including a gap refilled after a removal: the designer scans `processElement.parameters`, flat or roots-only by the
  client flag (5.1), and both give the same numbers, because item names end in `FileLocator`.
- A KEPT slot (reconciled by resolved source, Q4) keeps its name, UIds and resource rows. Two entries with the same
  resolved source match two slots one to one.
- The package's slot predicate is the run time's: top level, `^Attachments\d+$` case-sensitive, `A3` = the process
  (design 3.2), not the recipients' `StartsWith` + `int.TryParse`.

### 5.4 Collection order versus numeric order

The run time attaches in PARAMETER COLLECTION order: `GetFilteredOwnParametersList` walks `ForceGetParameters()`
and `GetEmailAttachments` walks the result (`ProcessUserTaskExtensions.cs:88-97`; `EmailTemplateUserTask.cs:280-287`).
The designer appends a new slot (`ProcessFilesControlSchema.js:237`), and so does `element.Parameters.Add` in the
package. [source; order of the sent files traced, not measured]

| Step (EM1 sequence) | Designer | Package, agreed | Slots in `BP2` order | Recipients-style max + 1 |
|---|---|---|---|---|
| S1 add five rows | `Attachments1..5` | create with five entries | 1, 2, 3, 4, 5 | same |
| S2 clear row 2, save | `Attachments2` and its item removed | send the list without row 2 | 1, 3, 4, 5 | same |
| S3 add a row, save | new `Attachments2`, fresh UIds, appended | send the five entries again | 1, 3, 4, 5, **2** | `Attachments6` |

So after a gap, `BP2` order is not numeric order, and the absolute `BP2` index of a slot depends on how many template
parameters and later syncs sit around it (corpus: index 49-50 of 51-55) [corpus]. Parity compares the RELATIVE order
of the slots only (6.2 R9). Describe returns the slots by number (design 2.2, 4.3 `Find`); collection order, the
order the email attaches, is not part of the contract (SQ10).

## 6. Parity method (AC7)

### 6.1 Three patterns already in the package tests

| Pattern | Precedent | Mechanics | Use here |
|---|---|---|---|
| A. committed verbatim capture + `PROVENANCE.md` SHA-256 pin + oracle reader | `PBT/FileProcessingProcessParityTests.cs:17-38`, `PBT/FileProcessingReportParityTests.cs:29-33`; reader `PBT/FileProcessingCaptureOracle.cs:48-58` (element by `J6` + `A2`), `:79-88` (hash vs pin); `PBT/Fixtures/FileProcessing/PROVENANCE.md:27-40` | the capture is never edited; a hash test fails on a hand edit; the reader returns per level `GS1`, `GS2`, `GS5` and items | the four corpus bindings (B1-B4): Object and Report sources, all four mode combinations, one slot each |
| B. stand capture restated as rows, not committed | `PBT/FileProcessingSysFileParityTests.cs:19-50` (designer save of 2026-10-20 restated), `PBT/FileProcessingParityTests.cs:42-90` (`Row(name, Stored kind, value, provenance)`) | per parameter: source kind, value where the capture fixes it, stamp role; exclusions named | EM1: 2+ slots, the gap, the Process-parameter variant, the process FileCollection and File sources, the current designer |
| C. platform writer JSON diff | `PBT/FileProcessParameterTests.cs:481-716` (`WriteMetaData`, `ReadBack`, `ApplyComparisonRule`, `Diff`, `ShouldCarryTheHarnessStamps`) | the builder's parameter written by the platform's own writer, read back by the platform's reader, structurally diffed against a quoted stored entry | one slot of each binding shape against the verbatim slot of 1.5 and, after EM1, against EM1's slots |

What unit tests cannot show: resource rows (the orchestrator's `InitializeLocalizableValues` and save are not run
in units, `PBT/ProcessDesignerRoundTripTests.cs:25-30`), the pre-save gate (`GetProcessValidationResult`), and the
designer's rendering. Those are EM2 and EM3. [source]

### 6.2 The attachment comparison rule (ACR)

Applies to every Send email element of both sides; the sides are a designer save (corpus or EM1) and a package build
of the same configuration with the same element names. Each step says what may differ; anything else fails.

| # | Step | Detail |
|---|---|---|
| R1 | Pairing | slots paired by `A2` (names ARE compared: Q5 makes them deterministic); the one item of each slot paired by position. A slot on one side only fails |
| R2 | `UId` | removed at every level |
| R3 | `A3`, `A4`, `GS5` | role-replaced at every level: the side's own process UId -> `<process>`; the element's user-task UId (read from that element's `J6`, `184dbb27-...`) -> `<task>`; any other value kept as written, so it shows as a difference |
| R4 | `IL2` | role-replaced: the slot's own Send email element UId -> `<element>`; any other value kept |
| R5 | `GS2` | every `[Element:{g}]` -> `<element NAME>` resolved in that side's metadata (`A2` of the element with that UId); every `[Parameter:{g}]` -> its role resolved in that side's metadata: `<source collection: OF1.ObjectFiles>`, `<source item: FileLocator item of OF1.ObjectFiles>` (the item by ROLE, never by name: a designer-made process collection names its item `FileCollectionParameter`, a package-made one `File`), `<source file: Doc>`. An unresolvable GUID is kept as written. The rest of the text (`[#`, `M`, separators, a formula) is compared exactly |
| R6 | "no value" | an `L8` with no `GS1` is "no value": `{}` and `{"GS5": <process>}` are equal (prior N15, precedent `PBT/FileProcessParameterTests.cs:598-611`). This is the ONLY exception to R7, pending EM1: if the designer writes `{}` for an unbound outer, R6 is dropped |
| R7 | Key sets | compared EXACTLY at every level, after R2: outer `{BL1, A2, A3, A4, IL2, L1, L8, L12, L18}`, item `{BL1, A2, A3, A4, IL2, L1, L8, L12}`, and inside a bound `L8` `{GS1, GS2, GS5}`. An extra `A5`, `BL11`, `L6`, `L17`, `GS4` on either side fails |
| R8 | Exact values | `BL1`, `A2`, `L1`, `L12`, `GS1`; `L18` length = 1 |
| R9 | Order | the relative order of the slots inside the element's `BP2` is compared; the absolute index is not; key order inside a level is not compared (the platform writer fixes it) |
| R10 | Resources (stand only, EM2) | per slot the four keys of 2.1, paired by key, value compared exactly, authoring culture only; the GUID-free values need no role mapping. AD3 applies; AD1 and AD2 are not tolerated, because the slot writer writes the designer form (6.3) |
| R11 | `BK15` | asserted on both sides: no row references a slot or item UId (not diffed) |

The ACR differs from the ENG-92719 TC-41 / ENG-95984 AC-8 rules in two places, on purpose: `GS5` and `IL2` are
role-compared instead of removed (both sides carry them on every bound level here;
`PBT/FileProcessParameterTests.cs:601-614` removes `GS5` because its builder side is unbound, and `IL2` under N9
because the designer writes none on a process parameter, and pins the builder's `IL2` = the schema by value in
`:704-712`), and `GS2` GUIDs are role-mapped instead of compared through element names alone.

### 6.3 Deviation candidates (AD1 and AD2 removed by the design)

| # | Deviation | Builder option that removes it | Design |
|---|---|---|---|
| AD1 | outer `DisplayValue`: bare caption vs `[#<el>.<coll>#]` (2.2) | the slot writer sets the outer display value in the designer form after binding, without changing `SourceDisplayValue` for every mapping (ENG-92719 D20 scoped that to nested sources) | the slot writer writes the designer form, on slots only: outer `[#<el>.<coll>#]`, item `[#<el>.<coll>:<item>#]` (design 3) |
| AD2 | item `DisplayValue` of a single File: bare caption vs `[#Doc#]` | same, on the item | the slot writer writes `[#<param>#]` on the item; the row is visible (2.2) |
| AD3 | resource culture: request vs authoring | none needed | compare the authoring culture only (as N14) |

### 6.4 Corpus fixtures to commit (pattern A)

Copy byte for byte into a new folder `PBT/Fixtures/SendEmail/` with its own `PROVENANCE.md`. Whole files are needed,
not slot fragments: R5 resolves each `GS2` GUID through the same file (source element, collection, item), and the SHA
pin covers what the parity test reads. [measured 2026-10-28: sizes, SHA-256 and first bytes read from the PackageStore
checkout]

The table below is this plan's inventory, not the `PROVENANCE.md` layout. `PROVENANCE.md` rows END with the SHA-256
cell (`| File | Source (relative to the PackageStore root) | Process schema UId | SHA-256 |`, the layout of
`PBT/Fixtures/FileProcessing/PROVENANCE.md:35-38`); `B8`, the byte count and the slot lines go in a second table or
before the hash. Otherwise `FileProcessingCaptureOracle.Hashes` finds no pin: its pattern matches only a row whose
last cell is the 64-hex hash (`PBT/FileProcessingCaptureOracle.cs:81-82`).

| File | Source (`PS/`) | Process UId | `B8` | Bytes | SHA-256 | Slots (lines) |
|---|---|---|---|---|---|---|
| `ProcessFileSendMail.metadata.json` | `ProcessTests/branches/7.8.0/Schemas/ProcessFileSendMail/metadata.json` | `30fc7866-0657-4f6b-8a56-79fa7a26b610` | `0.0.0.0` | 252,943 | `8fb74e92123a7dc9da218d9fd9d36be28e9b48d06dfa61835790d065a8ce7df3` | B1 manual + custom, Object (`2930-2961`); B2 auto + custom, Report (`3936-3967`) |
| `ProcessFileReport.metadata.json` | `ProcessTests/branches/7.8.0/Schemas/ProcessFileReport/metadata.json` | `4892b8d8-a6c1-44ec-bfdc-549ce2a4d7ea` | `8.0.4.826` | 201,467 | `72bc4a991e5339769a059ca2f61f7e5406ce08b7fb88e499f5a6b0fdb14c8713` | B4 manual + template, Report (`2652-2683`) |
| `MailConnectionsProcess.metadata.json` | `ProcessTests/branches/7.8.0/Schemas/MailConnectionsProcess/metadata.json` | `01d2c911-8b8d-44c8-b174-2559ac7935e9` | `7.18.2.172` | 155,216 | `74e321d0bddd785604e4847176333562b525402a83e0daac73902b00743a6740` | B3 auto + template, Object (`2486-2517`) |

- Encoding: UTF-8 WITHOUT a BOM, CRLF (first bytes `7b 0d 0a`), like the two process captures already committed
  (`PrintInvoiceReport`, `FileParameterProcess`); only the three task-template captures carry a BOM. [measured 2026-10-28]
- `.gitattributes`: the existing rule covers only `tests/UnitTests/CrtProcessBuilder.Tests/Fixtures/FileProcessing/*.metadata.json -text`
  (`PBREPO/.gitattributes:18-21`). Add `tests/UnitTests/CrtProcessBuilder.Tests/Fixtures/SendEmail/*.metadata.json -text`
  in the same commit as the files, and correct the header, which says "Three files" for a rule that already covers
  five (`PBREPO/.gitattributes:6`), and the rule comment, which says "UTF-8 BOM, CRLF" (`PBREPO/.gitattributes:19`), to
  "byte-exact as copied (CRLF; the task-template captures also carry a UTF-8 BOM)", plus the same error at
  `PBT/Fixtures/FileProcessing/PROVENANCE.md:32` (correction 2), in the same commit. Without `-text`,
  `* text=auto` normalises CRLF to LF in the index and the pin breaks on another checkout.
- `FixturePath` is hard-wired to `Fixtures/FileProcessing` (`PBT/FileProcessingTestSupport.cs:197-199`), and
  `FileProcessingCaptureOracle.Hashes` reads `PROVENANCE.md` through it (`PBT/FileProcessingCaptureOracle.cs:79-82`):
  give both a folder argument. The oracle reader also needs `L1`, `L12`, `IL2`, `A3`/`A4` and the per-level key set;
  today it returns `A2`, `GS1`, `GS2`, `GS5` and items only (`:113-128`).
- Byte cost: about 610 KB in the package test tree (precedent: 116 KB and 97 KB process captures). The corpus resource
  files are not committed: resources are compared on the stand (R10).
- `PROVENANCE.md` also records that the corpus was last saved 2021-03-09, 2021-06-17 and 2022-08-08 (each schema's
  `descriptor.json` `ModifiedOnUtc`; `B8` above is the creation version, `TS/Terrasoft.Core/Schema.cs:82`), that only
  the B1-B3 resource files are display-form evidence (2.1), and that none of the three processes exists on stand
  `Creatio` [measured 2026-10-28 by the lead; re-run before `PROVENANCE.md` is written: a read-only `SysSchema` query
  for `UId` IN (`30fc7866-0657-4f6b-8a56-79fa7a26b610`, `4892b8d8-a6c1-44ec-bfdc-549ce2a4d7ea`,
  `01d2c911-8b8d-44c8-b174-2559ac7935e9`) returns 0 rows].

### 6.5 Tests per pattern (names carry no ticket key; names and ids are the test plan's, sections 2.3, 3.1-3.3)

| Fixture (PBT) | Test | Pattern | Oracle |
|---|---|---|---|
| `SendEmailAttachmentsParityTests` | TC-P-01 `Capture_ShouldHashToTheValuePinnedInProvenance` x3; TC-P-02 `Capture_ShouldHoldTheSlotShapeTheOracleReads` | A | `PROVENANCE.md`; the four captured elements |
| same | TC-P-03 `Slot_ShouldEqualTheShippedElement` x4 (B1-B4) | A | corpus slots under the ACR; the package builds the same element names and modes |
| `SendEmailAttachmentsStandCaptureParityTests` | TC-P-04 `Slot_ShouldEqualTheStandCapture`; TC-P-05 `Element_ShouldEqualTheStandCapture_WithFiveSlots`; TC-P-06 `Numbering_ShouldEqualTheStandCapture_AfterAGapAndAReAdd`; TC-P-07 `TemplateAutoElement_ShouldEqualTheStandCapture` | B | EM1 rows (7.3) |
| `SendEmailAttachmentsSerializationTests` | TC-P-08 `WriteMetaData_ShouldStoreTheDesignersShape_ForAnAttachmentSlot`; TC-P-09 `WriteMetaData_ShouldStoreTheDesignersShape_ForASingleFileSlot` | C | the 1.5 slot and EM1's `Doc` row, plus `ReadBack` through the platform reader |
| `EmailAttachmentSlotTests` (and the applier fixtures of test plan 2.4) | one assertion per invariant I1-I11, I13, I14 | - | section 4 |
| clio e2e `SendEmailAttachmentsToolE2ETests` | stored bytes + resource rows of a built slot, through `ReadElementNodesAsync` / `ReadElementResourceRowsAsync` (`CLIO/clio.mcp.e2e/ProcessFileElementToolE2ETests.cs:1530, 1562`) | stand | EM1's rows; floor = the cut claimed in the ENG-95985 claim comment |

## 7. What EM1 must record

### 7.1 Once, before the first save

- EM0 values: platform version, CrtProcessDesigner, CrtProcessBuilder (history only), the designer user's profile
  culture, `UseProcessEmailAttachments` state for that user (no build-time reader: Q9 agreed 2026-10-28),
  and, in the designer console, `Terrasoft.Features.getIsEnabled('ProcessParameterCollections')` = true (the files
  control needs the flat client collection, 5.1).
- After the process exists: the UIds and names of every source parameter, so R5 can resolve roles: `OF1.ObjectFiles`
  and its item, `RF1.ReportFiles` and its item, `PF1.ObjectFiles` and `ObjectFile`, process `Docs` and its item (and
  that item's NAME as the designer creates it), process `Doc`; the UIds of `SE1`, `SE2` and the process.

### 7.2 Per save

Saves: S1 (SE1 with Attachments1..5 from `OF1.ObjectFiles.File`, `RF1.ReportFiles.File`, `PF1.ObjectFiles.ObjectFile`,
`Docs` item, `Doc`; SE2 auto + template with one row), S2 (row 2 cleared, saved), S3 (a row added, saved), S4 = EM1d
(one row re-bound from a collection item to `Doc`, saved), and one reuse save (design 4.3 `Remove`, in EM1 or EM2 as
the measurements file places it): in ONE save, a collection-sourced row cleared and a single-File row added that takes
the freed number. After EVERY save read `SysSchema.MetaData` and the `SysLocalizableValue` rows of the process.

| # | Record | Checks |
|---|---|---|
| 1 | every top-level `SE1`/`SE2` parameter matching `^Attachments\d+$`, raw JSON of both levels | key set per level (any key outside R7 is a finding: `A5`, `BL11`, `L6`, `L17`, `IsLazy`, `GS4`) |
| 2 | per level `A2`, `A3`, `A4`, `IL2`, `L1`, `L12`, `L8` (`GS1`, `GS2`, `GS5`), `L18` count | roles of R3-R5; item `L1` still FileLocator for every source kind (SQ5) |
| 3 | the full `BP2` name list of `SE1` and `SE2`, with each slot's index | R9; after S3, `Attachments2` after `Attachments5` (5.4) |
| 4 | `BK15` rows whose `GT*` fields name any slot or item UId | R11: none expected |
| 5 | `SysLocalizableValue` rows with keys under `BaseElements.SE1.Parameters.Attachments` and `.SE2.` | key, culture, value; after S2 the four `Attachments2` keys gone or orphaned (2.4); after the reuse save the outer DisplayValue key of the reused name (a stale collection display value means the writer writes an explicit empty one, 2.4); the culture rows written |
| 6 | for the `Docs` row: which node the user picked in the mapping window (the collection's item, or the collection itself if offered), and what was stored | SQ3 (section 3, the E11 shape) |
| 7 | for the `Doc` row: outer `L8` exactly (`{}` or `{"GS5": ...}`), item `GS2`, outer DisplayValue row present or not | R6, AD2 |
| 8 | slot UIds across saves | kept slots unchanged at S2/S3; S3's new slot has fresh UIds |
| 9 | S4 (EM1d): outer `L8` after the re-bind, item `L8` and `L1`, both DisplayValue rows | the stale parent of section 3 |
| 10 | optional: reopen the saved process and save without change | the designer's own save is stable on the compared keys (baseline for EM3) |

Keep the raw reads locally and untracked (precedent: `CLIOMAIN/eng-103937-sc1sf/UsrFpSc1SfCapture/`); they are not
committed (pattern B).

### 7.3 Turning EM1 into pattern-B rows

- One constant block per save and element: per slot `(Name, OuterKind, OuterRole, ItemName, ItemKind, ItemRole,
  OuterL1, ItemL1, L12, Stamp)` where Kind is `Script`/`Nothing` (R6) and Role is the R5 role
  (`<source collection: OF1.ObjectFiles>`); plus the ordered slot-name list (R9) and the key set per level (R7).
- A header comment in the `FileProcessingSysFileParityTests.cs:19-50` style: process name and UId, the date, the
  versions (EM0), "saved once by the designer, never run", and every exclusion named.
- Resource rows go into the clio e2e expectation (stand side), not into the package units.
- The EM1 process is also the describe oracle for designer-made shapes: describe must read S1-S3 back in the write
  shape and report S4's stale parent with its issue (design 4.7). Those describe steps run in the EM2 re-run on the
  final cut, after P-C3.
- EM2 replays S1-S3 through the package on stand `Creatio` (create, then `setElement` without row 2, then with the
  five entries) and diffs each state against the matching EM1 save under the ACR, resources included. At the STOP
  gate EM2 runs only its save, pre-save-gate and stored-bytes steps (EM3 runs in the designer). S4 has no package
  twin: the package never writes a stale parent.

## 8. Open serialization questions

| # | Question | Why it matters | Settled by | Until then |
|---|---|---|---|---|
| SQ1 | Does the current designer write the same key sets as the 2021-2022 corpus (no `A5`, `BL11`, `L6`, `GS4`; no `BK15`)? | R7 is built on the corpus key sets | EM1 | corpus key sets |
| SQ2 | The Process-parameter variant source: both levels bound to `PF1.ObjectFiles` / `ObjectFile`? | AC2 third variant has no corpus row | EM1 | section 3 row, [source] |
| SQ3 | Process FileCollection: does the designer bind outer -> `Docs` and item -> `Docs` item, or item -> `Docs` itself (the 2026-10-20 Process file shape)? | decides whether the builder's both-level form has a designer twin, and the describe issue for the other shape | EM1 (record 6) | builder binds both levels |
| SQ4 | Single File: unbound outer stored as `{}` or `{"GS5": <P>}`; item display value form | R6, AD2 | EM1 (record 7); run time EM8 | R6 tolerant |
| SQ5 | Item `L1` after the files control's `change:Value` handler copies the picked value's `dataValueType` onto the item (`ProcessFilesControlSchema.js:340-346`); a Formula row could change it | an item typed other than FileLocator throws at run time | EM1 for the five rows; a Formula row only if added to EM1 | FileLocator |
| SQ6 | Resource rows of a removed slot: deleted or orphaned? (An exported resource file can carry rows of removed elements: B4, 2.1.) | the package removes the parameter only, with no resource deletion; a stale outer DisplayValue under a reused name would show | EM1 S2, the reuse save (7.2), EM2 | `Parameters.Remove` only; an explicit empty outer DisplayValue for reused or unbound outers if a stale value survives (2.4) |
| SQ7 | New slot after a gap: name `Attachments2`, appended last in `BP2`? | Q5 parity, R9 | EM1 S3 | 5.4 table |
| SQ8 | Captions and display values: which culture rows the designer writes for a non-en-US user, and which the package writes | R10, AD3 | EM1 (record 5), EM2 | authoring culture only |
| SQ9 | Outer display value (AD1) and single-File item display value (AD2): does the designer render a bare caption, and does a no-op designer save rewrite it? | whether the designer form the slot writer writes (6.3) is what the designer shows and keeps | EM2 (diff), EM3 (render + no-op save) | write the designer form |
| SQ10 | Describe order: numeric or collection order | round trip and the order the email attaches | EM1 S3 shows the stored order | design 2.2: by slot number; the order of files in an email is not part of the contract |
| SQ11 | Does the pre-save gate accept a package-built nested Script on the item (compiled against `IFileLocator`)? | a refusal blocks every build | EM2 | traced only: each Script value is compiled against the parameter's value type (`TS/Terrasoft.Core/Process/ParameterValuesValidationRule.cs:206-207, 325-350`) |
| SQ12 | Does a designer no-op save of a package-built element keep every compared key (adds `GS5`, rewrites display values, adds `BK15`)? | the residual risk of the refinement comment | EM3 | - |
| SQ13 | Version clone: does `A3`/`A4`/`GS5` of both levels follow the clone UId? (`PB/Design/ProcessVersionCloneFactory.cs:106-111` text-replaces the source UId) | an un-replaced `A3` makes the run time ignore the slot (I3) | EM13 | traced: yes |
| SQ14 | After `removeElement` of the source through the package: does the slot keep its dangling Script, gain `BL11: false`, or does the pre-save gate refuse it? | describe issue and the Q16 follow-up guard (proposed only if EM11 shows a silent break) | EM11 | - |
| SQ15 | Outer-only slot (raw): loud NRE or silent? | describe issue text; the raw-route refusal (Q8) | EM9 | traced: NRE |

EM4, EM5, EM7, EM8, EM10, EM14 and EM15 are run-time measurements; they settle no stored-bytes question beyond SQ4
(EM6 and EM12 are dropped: Q17 and Q9, agreed 2026-10-28).

## 9. Corrections to the research

| # | Report and claim | What the source says |
|---|---|---|
| 1 | r1 §2 table (`r1-package-send-email.md:99`): the package's mapping service "already writes exactly these display forms" for both levels | only the ITEM gets the designer form; a top-level source, including the outer slot's collection, keeps its bare caption (`PB/Mappings/ProcessMappingService.cs:963-967`; pinned by `PBT/MappingNestedDisplayValueTests.cs:76-77`). AD1/AD2 follow |
| 2 | r8 §3 pattern A (`r8-testing.md:119`): the committed captures are "UTF-8 BOM, CRLF" | only the three task-template captures have a BOM; `PrintInvoiceReport`, `FileParameterProcess` and the three Send email corpus files start `7b 0d 0a` (no BOM) [measured 2026-10-28]. `PBT/Fixtures/FileProcessing/PROVENANCE.md:32` states a BOM for the process captures and is wrong too |
| 3 | r3 §4.2 (`r3-designer.md:92`): a collection is offered "only as a node to drill into" | measured 2026-10-20 on the Process file element: the designer stored the FileLocator item bound to the process FileCollection ITSELF (`PBT/FileProcessingSysFileParityTests.cs:49-50, 524-526`; `CLIOMAIN/eng-103937-sc1sf/UsrFpSc1SfCapture/metadata.json`). EM1 must record which node is picked for the email |
| 4 | r5 identifier table (`r5-corpus.md:67`): `PB\ProcessDesignConstants.cs:634` holds the Send email UId | it holds the task NAME `EmailTemplateUserTask`; no package production file contains `184dbb27` (grep). The ACR's `<task>` role reads the UId from the element's `J6` |
| 5 | r5 B2 range (`r5-corpus.md:138`): `3936-3966` | the slot spans `3936-3967` (closing brace at 3967) |
| 6 | r3 §3 table (`r3-designer.md:65-67`): line pairs listed under parent/item columns | the config lists the ITEM first: `:262-278` item (FILE_LOCATOR `:270`, IN `:271`, None `:272-274`), `:279-294` parent (COMPOSITE_OBJECT_LIST `:287`, IN `:288`, None `:289-291`). Values right, line mapping swapped |
| 7 | r8 §2 (`r8-testing.md:108`): `ProcessSchemaNestedParameterCollection.InsertItem` "only sets IsNested" | it calls `base.InsertItem` first, which back-fills the stamp when the nested collection already has a `ParentMetaSchema` (`TS/Terrasoft.Core/Process/ProcessSchemaNestedParameterCollection.cs:40-43`; `TS/Terrasoft.Core/MetaItemCollection.cs:92-104`). The miss is order-dependent (critic C16); the conclusion, stamp explicitly, stands |
| 8 | r10 G8 and r3 P9: assert `GS5` on both levels because of the `UseOnlyModifiedParameters` filter | the filter is not on a Send email's path: it applies only to the reader built for a sub-process start (`TS/Terrasoft.Core/Process/ProcessComponentSet.cs:1292-1297`); the multi-instance iteration-count reader (`:1449-1452`) returns before the filter (`TS/Terrasoft.Core/Process/ProcessParameterValueProvider.cs:734-737`); a Send email's parameters are read unfiltered (`ProcessComponentSet.cs:491-517`). `GS5` = the process UId on both levels is designer and corpus parity; the compiled-code generator skips an element parameter with another `GS5` (`TS/Terrasoft.Core/Process/ProcessSchemaGeneratorNew.cs:296-298`). Keep the assertion, as parity (I10) |
