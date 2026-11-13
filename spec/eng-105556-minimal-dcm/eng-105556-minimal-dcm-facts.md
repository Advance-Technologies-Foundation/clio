# ENG-105556 AI Toolkit: create a minimal DCM (stages from a lookup) and add a progress bar to the record page - platform facts

Written 2026-11-08. Nothing was built, saved or run; the only stand reads are the read-only ones
recorded in the research notes. Epic: ENG-92704 "Create BP via AI Toolkit".

**Summary.** What is true today, before any code, about the platform pieces the task touches: the DCM object model
in core, its stored keys, what the schema manager does on create and save, how a case runs (instance start, stage
change, records that existed before the case), the two settings tables, the 7.x designer that 8.x still uses, the
Freedom UI `crt.EntityStageProgressBar` and its data source, the feature flags and the rights. It is the evidence
base of the [design](eng-105556-minimal-dcm-design.md); choices are NOT made here, they are the open questions Q1..Q29
of the [decisions](eng-105556-minimal-dcm-decisions.md) file. Where a research note said something else, the
adversarial verdict or a later gap note wins, and the difference is listed under
[Corrections to the research](#13-corrections-to-the-research).

**Basis.**
- Core TSBpm `<core>/Src/Lib` at a95349820 (the stand `Creatio` runs from this checkout, v r10-C1).
- the package repository `origin/main` a3f58852 (CrtProcessBuilder 1.6.6.140) at `<package repository>`.
- clio `origin/master` 69788ccd5 at `<clio>`; clio-knowledge `origin/master` 622134b
  (libraryVersion 1.15.180) at `<clio-knowledge>`.
- creatio-ui master 7ca9210bd0e; PackageStore `*/branches/7.8.0` (1040 packages scanned).
- Stand `Creatio` (developer stand, .NET Framework 4.8.1, MSSql, read-only), inventory of
  2026-11-08 (r10, g1).
- Owner's DCM overview note (`[note "<section>"]`, section titles translated) and Academy pages (r9).
- Research notes r1..r10, gap notes g1..g6 and the verdict table
  [`_verdicts.md`](file:///<research-dir>/_verdicts.md) (78 verdicts: 18 confirmed, 58 partly,
  2 refuted) in `<research-dir>/`.

**Paths.** Every `PREFIX/path:line` is relative to the prefix.

| Prefix | Path |
|---|---|
| `CORE` | `<core>/Src/Lib/` |
| `PB` | `<package repository>/packages/CrtProcessBuilder/Files/src/cs/` |
| `CLIO` | `<clio>/` |
| `KB` | `<clio-knowledge>/` |
| `PS` | `<PackageStore>/<Package>/branches/7.8.0/` |
| `DD` / `DL` | `PS` DcmDesigner / DcmLibrary |
| `UI` | `<creatio-ui>/` |
| `BAR` | `UI/libs/studio-enterprise/ui/entity-stage-progress-bar/src/` |
| `AM` | `UI/libs/studio-enterprise/feature/application-management/src/lib/` |

**Tags.** `[traced path:line]` read in source or package data (for run-time behaviour a source trace is a
hypothesis until measured); `[corpus N/M]` count over PackageStore packages; `[measured]` read on stand `Creatio`
(the exact query is in the cited note); `[academy URL]`; `[note "..."]`; `[inferred]` reasoning from the cited
evidence.

**Fact-check column ("Chk").** `v rX-CY <verdict>` = the row of `_verdicts.md` that adversarially re-checked the
claim (its correction is what this file states); `judge` = re-read by the design judge on 2026-11-08 to settle a
disagreement between designs (design file section 1); `gN` = gap note gN, a second targeted trace, not adversarially
checked; `this file` = re-read while writing this file; `-` = research note only, not re-checked.

---

## 0. Short answer

- **A DCM is an interpreted process.** `DcmSchema : BaseProcessSchema`, always interpretable, no C# generated:
  saving one needs no compile and no restart on either runtime [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:48,210-217;
  DcmSchemaManager.cs:86-90,148-150] (v r1-C1 confirmed). Saving is NOT inert: it registers the start signal and
  writes the flow schema, outside the save transaction (v r1-C2 partly).
- **The stage-to-lookup link is only `DcmSchemaStage.StageRecordId` = lookup row Id.** Order = the `Stages` array
  order; nothing comes from the lookup (v r1-C3 partly, v r9-C4 confirmed).
- **Everything a minimal case needs is public**: `DcmSchemaManager.CreateSchema` / `SaveSchema` /
  `GetProcessValidationResult` / `RemoveItemByUId`, the stage and connection object model,
  `DcmEntityUtilities.FindEnabledDcmSchemas`, `GetIsActiveVersion(Guid)`, `IPackageSchemaDataCreator` (judge; v r1-C4).
- **A freshly saved root DCM is enabled and the active version at once** on the saving node; no `SetEnabled`, no
  user-property row (g1).
- **The settings tables are not core data.** Core never reads or writes `SysDcmSettings` /
  `SysDcmSchemaInSettings`; the 7.x designer needs the link row to open a case; the 8.x progress bar reads neither
  (v r5-C8, v r2-C4, `_verdicts.md` conflict 1).
- **There is no Freedom UI case designer.** 8.x opens the 7.x `DcmDesigner` by UId and the 7.x Section wizard
  "Cases" step (v r9-C8 partly).
- **The bar is one `viewConfigDiff` insert with `entityName`.** It takes stages, stage column, colours and allowed
  moves from the first enabled, active-version DCM of the entity; it hides itself without one; it needs a non-null
  stage `Color` and `StageConnections` to be clickable (v r3-C1 confirmed, v r3-C3, r3-C4, r3-C5 partly).
- **Rights**: a DCM save needs a General user with `CanManageDcm`; a package-data binding needs `CanManageSolution`;
  defaults grant `CanManageDcm` (and `CanManageProcessDesign`) only to System administrators (v r6-C3 confirmed; judge)
  [traced PS CrtBase/Data/SysAdminOperationGrantee_CanManageDcm/data.json:25]; CrtBase grants `CanManageSolution` to the
  Supervisor USER [traced PS CrtBase/Data/SysAdminOperationGrantee_CanManageSolution/data.json:12,17], so M4 must grant
  `CanManageSolution` to the non-admin explicitly.

---

## 1. DCM data model in core (`Terrasoft.Core/DcmProcess`)

### 1.1 Types and visibility

| Fact | Evidence | Chk |
|---|---|---|
| `DcmSchema : BaseProcessSchema`, public, public ctor `DcmSchema(ISchemaManager)`; ctor sets `Filters = ""`, `Enabled = true` | [traced CORE/Terrasoft.Core/DcmProcess/DcmSchema.cs:48,114-119] | v r1-C1 confirmed; g1 |
| `IsInterpretable` getter returns `true`, setter throws on `false`; `SerializeToDB` forced true | [traced DcmSchema.cs:205,210-217] | v r1-C1 confirmed |
| `EntitySchemaUId`, `StageColumnUId`, `Enabled` are `[MetaTypeProperty(IsExtraProperty = true)]`; `Enabled` is also a user property; `Filters` is a FilterGroup JSON string | [traced DcmSchema.cs:240-253,320-321] | g1 (a) |
| `Stages`, `Transitions`, `SystemElements`, `StageConnections` are public collections | [traced DcmSchema.cs:283-315,292-296] | - |
| Default parent = `DcmSchemaManager.DefSchema`, UId `51B280EB-D8A3-4932-81B9-EF61A0877AAB`; an instance that was not deserialized carries the def-schema UId/Name, so a new DCM must get its own UId or come from `CreateSchema` | [traced DcmSchema.cs:222-235,437-446] | g2 §1 |
| Internal, synthesized, never serialized: `StartSignal`, `IntermediateCatchSignal`, `DcmStageTransitionUserTask`, `DefaultStage`, `TerminateEvent`; internal `ReinitializeSystemElements`, `GetIsFinalStage`, `GetFilterColumnUIds`; `AssignParentSchema` throws | [traced DcmSchema.cs:148-196,568,582-596,602,710-712] | v r1-C6, v r8-C12 partly |
| `DcmSchemaManager : BaseProcessSchemaManager<DcmSchema>`, reached as `UserConnection.DcmSchemaManager`; `ItemTypeName => "Case"`, `SysSchemaUserPropertyRightsOperations => ["CanManageDcm"]` | [traced CORE/Terrasoft.Core/DcmProcess/DcmSchemaManager.cs:15,29-37] | v r6-C1 partly |
| `SetEnabled` / `GetIsEnabled(Guid)` are `internal virtual`; public wrapper `DcmSchemaDesigner.SetEnabled` | [traced DcmSchemaManager.cs:109-121; CORE/Terrasoft.Nui.ServiceModel/WebService/DcmSchemaDesigner.cs:64-69] | v r2-C13 partly; g1 (c) |
| `GetGenerator` throws `NotImplementedException` (no code generator) | [traced DcmSchemaManager.cs:148-150] | v r4-C4 confirmed |
| `GetIsActiveVersion(Guid schemaId)` is public; `GetIsActiveVersion(ISchemaManagerItem)` is `internal`; `GetProcessValidationResult`, `GetActiveVersionItem` public | [traced CORE/Terrasoft.Core/Process/BaseProcessSchemaManager.cs:1262,1373,1522,1612] | judge |
| `DcmEntityUtilities` is public sealed with a public ctor `(UserConnection)` and public `FindEnabledDcmSchemas(entityUId)` = root item `Enabled` + active version + same entity; its interface `IDcmEntityUtilities` is internal | [traced CORE/Terrasoft.Core/DcmProcess/DcmEntityUtilities.cs:19,57,434-438,467-476; IDcmEntityUtilities.cs:14] | judge |
| `DcmSchemaStage`, `DcmSchemaStageConnection(s)`, `DcmSchemaManagerItem`, `DcmSchemaChangeStageUserTask` are public with public ctors | [traced DcmSchemaStage.cs:31,72-74; DcmSchemaStageConnection.cs:12; DcmSchemaStageConnections.cs:11; DcmSchemaManagerItem.cs:14-26; DcmSchemaChangeStageUserTask.cs:13] | - |
| `DcmFlowSchemaGenerator`, `DcmProcessElementFactory`, `DcmStageUserTask`, `DcmStageTransitionUserTask`, `DcmInterpretationValidator` and other runtime helpers are internal | [traced DcmFlowSchemaGenerator.cs:16; DcmProcessElementFactory.cs:15; DcmStageUserTask.cs:15; DcmStageTransitionUserTask.cs:15; DcmInterpretationValidator.cs:9] | - |
| `DcmSchemaDesigner` (Terrasoft.Nui.ServiceModel) is public; `Save(ContractBaseProcessSchema<DcmSchema>, response)` is public; CrtProcessBuilder already references `Terrasoft.Nui.ServiceModel` and uses no designer class today | [traced CORE/Terrasoft.Nui.ServiceModel/WebService/DcmSchemaDesigner.cs:14,23; BaseProcessSchemaDesigner.cs:376; pb105556 CrtProcessBuilder.csproj:262-263] | g2 §1 |
| No conditional compilation in `Terrasoft.Core/DcmProcess` | [traced grep `#if` = 0 hits] | v r1-C1 confirmed |

### 1.2 Stage (`DcmSchemaStage : BaseProcessSchemaElement`)

| Property | Meaning | Evidence | Chk |
|---|---|---|---|
| `UId`, `Name` | identity; designer and products name stages `Stage1..N`; stage UIds are fresh GUIDs, never equal to `StageRecordId` in shipped DCMs | [corpus 238/238 stages have UId/A2; 0/238 UId = StageRecordId] | v r5-C10 partly |
| `Caption` | localizable, stored as resource `Stages.<Name>.Caption`, not in metadata; a design-time copy that can drift from the lookup (`LeadCase` "Distribution" vs "Nurturing") | [traced DcmSchema.cs:627-635] | v r5-C5 partly |
| `StageRecordId` | Id of the lookup row the stage stands for; nothing in core validates it | [traced DcmFlowSchemaGenerator.cs:96-100,162-174] | v r1-C3 partly |
| `Color` | hex string; server neither defaults nor validates it; every shipped stage has one | [traced DcmSchemaStage.cs:72-74,106-109,325] [corpus 238/238] | v r3-C5 partly |
| `ParentStageUId` | groups alternative stages into one bar slot; `IsAlternative => ParentStageUId != Empty` | [traced DcmSchemaStage.cs:96-100,177] | - |
| `IsSuccessful` | default `true`; unsuccessful final-stage bypass | [traced DcmSchemaStage.cs:72-74,138-142] | - |
| `TransitionType` | `None` / `AfterRequired` / `AfterAll` (automatic transition) | [traced DcmSchemaStage.cs:11-15,147-151] | v r9-C5 partly |
| `UsePermissions`, `Permissions` | stage role list; `UsePermissions` written only when true | [traced DcmSchemaStage.cs:114-124,332] | v r10-C6 partly |
| `Elements` | the steps (section 1.4) | [traced DcmSchemaStage.cs:156-160] | - |
| `IsDefault` | runtime-only flag of the synthetic `DefaultStage`; NOT persisted (GeneratedDcmSaver sets it on the first stage to no effect) | [traced DcmSchemaStage.cs:169-172,320-331] | v r1-C4, v r4-C3 partly |

### 1.3 Stage connections

| Fact | Evidence | Chk |
|---|---|---|
| `StageConnections.Connections` is a list of directed `{Source, Target}` pairs of stage UIds | [traced CORE/Terrasoft.Core/DcmProcess/DcmSchemaStageConnections.cs:130-138; DcmSchemaStageConnection.cs:41-49] | - |
| Core starts with an empty list and `SaveSchema` does not fill it; the full mesh is a designer default (client) | [traced DcmSchema.cs:292-296; DcmSchemaStageConnections.cs:33-35,76; DD Schemas/DcmSchema/DcmSchema.js:243-255,485-500] | v r5-C6 partly |
| In core, connections drive only automatic transitions (lowest-index forward non-alternative connected target); with empty stages no transition task is generated | [traced DcmStageTransitionUserTask.cs:32-58; DcmFlowSchemaGenerator.cs:308-321] | v r1-C3, v r5-C4 partly |
| Manual moves (field edit, record save) are not checked against connections; `Validating` checks required elements and stage permissions only | [traced DcmEntityUtilities.cs:282-323] | v r3-C4 partly |
| Corpus: 39/45 meaningful multi-stage DCMs are full mesh; some product DCMs restrict on purpose; `OmniChatCase` has 1 connection and alternative stages (not a template) | [corpus 39/45] | v r5-C6, v r10-C6 partly |

### 1.4 Steps (extension point for the later task, nothing used now)

| Fact | Evidence | Chk |
|---|---|---|
| A step is a `DcmSchemaElement` wrapper around an ordinary core process element (`ProcessSchemaUserTask` 56, `ProcessSchemaSubProcess` 25 of 81 corpus elements), with `RequiredType` and `Conditions` | [traced DcmSchemaElement.cs:25,94-110] [corpus 81/81] [note "A step is an ordinary process element inside a stage"] | v r1-C6, v r6-C6 partly |
| The wrapper's ctor taking a flow element and the `ProcessFlowElement` getter are internal, its setter private; the public routes are `new DcmSchemaElement()` + `ReadMetaData` (or `DcmSchema.ReadMetaData`), or the designer save | [traced DcmSchemaElement.cs:48-59,69-75,94,132-153] | v r6-C7 confirmed |
| The element must be in `Stage.Elements` AND have `ContainerUId` = stage UId; the inner type is instantiated from the Terrasoft.Core assembly only | [traced DcmSchemaElement.cs:116-122; CORE/Terrasoft.Core/Process/BaseProcessSchemaItem.cs:63-67] | v r1-C6 partly |
| The step palette is `SysDcmUserTask` (Activity, Approval, OpenEditPage, EmailTemplate, ChangeAdminRights; +AppValidation in FinAppLending) | [traced DD Data/SysDcmUserTask/data.json] | v r5-C3 partly |
| `SystemElements` (change-stage tasks) are rebuilt only by internal `ReinitializeSystemElements` from element `Conditions`; empty for empty stages; a public-API rebuild is possible (`DcmSchemaChangeStageUserTask` ctor, `Guid.Xor`, name `DcmChangeStageUserTask_{elementUId:N}_{nextStageId:N}`) | [traced DcmSchema.cs:582-596; DcmSchemaChangeStageUserTask.cs:14,36,46] [corpus 0 Conditions] | v r8-C12 partly |
| The element handlers in CrtProcessBuilder are typed on `ProcessSchema` (712 occurrences in 166 files); a stored DCM step differs from a process element (no A3/A4/A5/BL3/BL8/IL2 inside, wrapper-owned connection parameters, BK15 on the wrapper) | [traced PB IProcessElementHandler.cs:34-71] [corpus 81/81] | v r6-C5, v r6-C6 partly |
| A step whose element needs `ExecutionMode.Compile` makes the DCM save fail (no compile fallback) | [traced ParameterValuesValidationRule.cs:302-304; DcmSchema.cs:210-217] | v r1-C1 confirmed |

Decision pointer: Q27 (steps approach), design section 12.

---

## 2. Serialization

### 2.1 Storage dialect (`SysSchema` metadata, package `Schemas/<Name>/metadata.json`)

| Key | Meaning | Evidence | Chk |
|---|---|---|---|
| `ManagerName` = `DcmSchemaManager`, `UId` | identity | [corpus 61/61] | - |
| `A2` Name, `A3` CreatedInSchemaUId, `A4` ModifiedInSchemaUId, `A5` CreatedInPackageId | MetaItem | [traced CORE/Terrasoft.Core/MetaItem.cs:81-90] | - |
| `B1` Methods, `B2` LocalizableStrings, `B3` Usings, `B6` PackageUId, `B8` CreatedInVersion | Schema | [traced CORE/Terrasoft.Core/Schema.cs:77-90] | - |
| `FJ1` Parameters (always `[]` in the corpus) | | [corpus 61/61] | v r5-C10 partly |
| `IJ1` SerializeToDB, `IJ6` IsValid, `IJ7` IsActiveVersion, `IJ8` IsDelivered, `IJ9` Version, `IJ10` UseSystemSecurityContext, ... | BaseProcessSchema | [traced CORE/Terrasoft.Core/Process/BaseProcessSchema.cs:46-96] | - |
| `BK8` ParentSchemaUId (= DefSchema 51b280eb...), `BK15` Mappings, `BK37` NotificationCaption | | [traced BaseProcessSchema.cs:47-49] [corpus 61/61 BK8] | - |
| `EntitySchemaUId`, `StageColumnUId`, `Stages`, `Transitions`, `StageConnections` {`Connections`: [{`Source`,`Target`}]}, `SystemElements`, `Filters`, `Enabled` | literal keys of `DcmSchema` | [traced DcmSchema.cs:53-57,63-64,659-676; DcmSchemaStageConnections.cs:105,163-174] | - |
| Stage: `BL1` = `Terrasoft.Core.DcmProcess.DcmSchemaStage`, `UId`, `A2`, `A3`, `A4`, `A5`, then `ParentStageUId`, `Color`, `StageRecordId`, `Elements`, `IsSuccessful`, `TransitionType`, `UsePermissions`, `Permissions` | | [traced DcmSchemaStage.cs:36-43,322-334] | - |
| Element: `BL1` = `Terrasoft.Core.DcmProcess.DcmSchemaElement`, `IL2` = stage UId, `RequiredType`, `Conditions`, `ProcessFlowElement` {`BL1`, `BL7` (user-task or sub-process item), `BP2`, ...} | | [traced DcmSchemaElement.cs:30-32,212-221] [corpus 81/81 IL2 = stage UId] | v r1-C6 partly |

Omitted when default: `Enabled` (written only when `false`; key present in 5/61), `Filters` (""), stage
`IsSuccessful` (true), `ParentStageUId` (empty), `UsePermissions` (false), `TransitionType` (None) [traced
DcmSchema.cs:670-672; DcmSchemaStage.cs:324-332] [corpus 5/61] (v r5-C10 partly).

Writer-dependent keys (route A = object model + `SaveSchema`, route B = designer save, design section 1 / Q6) (g2 §3):
- `BK37` is written iff `GlobalAppSettings.UseProcessRemindings` (true in shipped Web.config / app.config), both
  routes [traced BaseProcessSchema.cs:1126-1130; Terrasoft.WebApp/Web.config:539; Terrasoft.WebHost/app.config:317] [corpus 60/61].
- `IJ10` is a 7.x client default, neither server route sets it; 21/61 shipped DCMs lack it [traced
  Terrasoft.Nui/.../base-process-schema-manager.js:308-313] [corpus 40/61].
- `B8` is stamped only when the item comes from `manager.CreateSchema` (not `new DcmSchema`) [traced
  SchemaManager.cs:3465; Schema.cs:109-111,386-392].
- `IJ6` IsValid: 0/61 shipped DCMs carry it [corpus 0/61].

### 2.2 Properties and resources

| Fact | Evidence | Chk |
|---|---|---|
| The save writes one `SysSchemaProperty` row per extra property (delete, then insert); `true` is stored as `"True"`; the same rows are read back into `ExtraProperties` on load | [traced CORE/Terrasoft.Core/SchemaManager.cs:787-796,1293-1299,3034,3777-3802; ExtraProperty.cs:69-90] | g1 (a) |
| Shipped DCM property set: `CreatedInVersion`, `Enabled`, `EntitySchemaUId`, `IsActiveVersion`, `IsTracing`, `StageColumnUId`, `Version` | [measured: `execute-esq SysSchemaProperty` where `SysSchema.Id = 616028ec-...` (OmniChatCase), 7 rows] [traced PS CrtKnowledgeManagementVersions/Schemas/KnowledgeArticlePublishing/properties.json] | g1 (a) |
| Captions live only in resources (`SysLocalizableValue` / `Resources/<Name>.Dcm/resource.<culture>.xml`): `Caption`, `NotificationCaption.Caption`, `NotificationCaption.DisplayValue`, `Stages.StageN.Caption` | [traced PS CrtKnowledgeManagementVersions/Resources/KnowledgeArticlePublishing.Dcm/resource.en-US.xml] | v r5-C10 partly |
| `CreateSchema` does not set `CultureName` for a DCM | [traced DcmSchemaManager.cs:15-150; SchemaManager.cs:5095-6179] | v r6-C1 partly |

### 2.3 Design dialect (designer wire)

| Fact | Evidence | Chk |
|---|---|---|
| camelCase names from `[DesignModeProperty]` (`entitySchemaUId`, `stages`, `stageConnections`, `stageColumnUId`, `filters`, `systemElements`, ...); the client stage adds `parentStageUId`, `color`, `stageRecordId`, `isSuccessful`, `transitionType`, and every item serializes `isValid` | [traced DcmSchema.cs:19-47; DD Schemas/DcmSchema/DcmSchema.js:316-319,343-350; DcmSchemaStage.js:152-168] | - |
| The READ output (`GetReadableSchemaMetaDataText`) is wrapped `{"metaData":{"schema":{...}}}` with no root `typeName`; it is not what the SAVE reader (`ProcessSchemaDesignDataReader`) accepts | [traced CORE/Terrasoft.Core/Process/ProcessSchemaDesignDataReader.cs:79-98,139-158,171-197; Terrasoft.Core.Tests/DcmProcess/DcmSchema.Tests.cs:146-170] | g2 §2 |
| Save needs a root `typeName` and a `typeName` on every untyped array item; `StageConnections.Connections[*]` are written without `BL1`, the 7.x client adds `typeName` to each connection | [traced DcmSchemaStageConnections.cs:73-84; DD Schemas/DcmSchemaStageConnection/DcmSchemaStageConnection.js:35-43] | g2 §2 (not executed) |
| Save-shape resources are `{"<elementUId>.caption": {culture: text}}`; the designer `ApplyChanges` never reads `contract.Caption`; read-shape keys (`Stages.Stage1.Caption`) fail at `Guid.Parse` | [traced ProcessSchemaDesignerUtilities.cs:31-56,82-97,105-127; BaseProcessSchemaDesigner.cs:187-211] | g2 §2 |
| No server-side writer of the client dialect exists; only the JS clients produce it | [traced grep: no `*DesignDataWriter` in TSBpm Lib] | g2 §2 |

### 2.4 Reference shapes

| Shape | What it is | Evidence | Chk |
|---|---|---|---|
| `CrtKnowledgeManagementVersions/KnowledgeArticlePublishing` | smallest flat, unfiltered, element-less DCM in a shipped package: 3 stages, 6 connections, `Color "#54AA4D"`, entity-bound settings, Freedom page with the bar; 16 test DCMs are smaller | [traced PS CrtKnowledgeManagementVersions/Schemas/KnowledgeArticlePublishing/metadata.json:14-96] | v r5-C10 partly |
| Product DCMs | 14 schemas = 13 DCMs (OmniChatCaseCustom1 is a version); 12/13 have no steps; 10/11 enabled DCMs are element-less; the one with steps is an "(example)" | [corpus 61 DCMs] | v r5-C2 partly |
| GeneratedDcmSaver output (CrtGenAI) | `{Entity}Case`, `#54AA4D`, stage UId = lookup Id, `IsDefault` on the first stage, directed n(n-1) connections | [traced PS CrtGenAI/Schemas/GeneratedDcmSaver/GeneratedDcmSaver.cs:29,106-158] | v r4-C3 partly |

---

## 3. Manager and save behaviour

### 3.1 Create

| Fact | Evidence | Chk |
|---|---|---|
| `CreateSchema(name, base, uc, uid, addToDesignItems)`: a null base becomes `DefSchema`; the item Id is a new Guid; the instance is a clone of `DefSchema` with `CreatedInVersion` stamped; `addToDesignItems = false` writes no session data | [traced CORE/Terrasoft.Core/SchemaManager.cs:3335-3355,3450-3471,5506-5515] | judge; g2 §1 |
| Built by hand instead (`new DcmSchema` + `DcmSchemaManagerItem`), the item needs `Instance`, `PackageUId`, `schema.ManagerItem`, `Assign`, `Id`, `Descriptor` set | [traced GeneratedDcmSaver.cs:123-182; MetaItem.cs:101-128] | v r1-C4 partly |

### 3.2 Save (`SaveSchema(item, uc, generateBundles, lock)`)

| Fact | Evidence | Chk |
|---|---|---|
| `SaveSchema` is non-virtual; the wrapper reads `designItem.Instance.PackageUId` and forwards to protected `InternalSaveSchema` (rights and foreign checks live only in the 9-parameter overload) | [traced SchemaManager.cs:830-836,992-998,3196-3219,5569-5616] | v r8-C2 partly |
| `InternalSaveSchema` runs, in order: `CheckOperationUserRights` (DCM override: General user AND `CanManageDcm`, replacing the base check, so no `IsSystemOperationsRestricted` / `GetCanModifyPackageElement`), `CheckIsPackageForeign`, `CheckIsValidSchemaName(validateNamePrefixes: true)`, the `ItemSaving` veto, `designItem.Assign(instance)` | [traced SchemaManager.cs:3214-3240; DcmSchemaManager.cs:132-139] | v r2-C7, v r4-C10 partly |
| The object-model save does NOT check name uniqueness in the manager or the package (the designer does, and throws) | [traced BaseProcessSchemaDesigner.cs:273-306; SchemaManager.cs:3126,3218-3228] | v r1-C5 confirmed; g2 §3 |
| A new item: `designItem.Clone()` is added to the manager collection BEFORE the DB write and removed only if the DB write throws | [traced SchemaManager.cs:3135-3141,3185-3191] | g1 (d) |
| `InternalSaveSchemaInDb`: `SaveSchemaItem` (SysSchema row + properties), `SaveSchemaContent` (metadata + resources), `GenerateSchemaSources` | [traced SchemaManager.cs:2174-2215] | v r1-C2 partly |
| For a DCM, `GetSchemaSources` is overridden: `ActualizeSchemaInfo(schema, schemaId, uc)`, then an EMPTY source collection | [traced DcmSchemaManager.cs:86-90] | v r1-C1 confirmed, v r4-C4 confirmed |
| `ActualizeSchemaInfo` = register the start signal (`SysEntityPrcStartEvent`, `RecordChangeType = Inserted`, `ConditionData = Filters`) through the SYSTEM connection, clear the start-event cache, generate the flow schema (`DcmFlowSchemaGenerator`) and save it to the flow-engine state service + `IFlowSchemaStorage` (+ `SysPrcFlowSchema` / `SysPrcParameterPath` through a new `DBExecutor` only with `UseParameterDataOptimization`), `InvalidateRemote` only with `ProcessFeatures.UseSchemaManagerRemoteInvalidation`. None of this is rolled back if the save fails. Registration is not gated by `Enabled` | [traced BaseProcessSchemaManager.cs:370-395,858-866,994-1022,1284-1297; FlowSchemaStorage.cs:44-48,212-228] | v r1-C2 partly |
| Package install and `SchemaImporter` reach the same code (so a DCM must be written through `SaveSchema` or a package install, never a raw `SysSchema` write) | [traced PackageInstallUtilities.cs:1705] | v r1-C1 confirmed, v r1-C2 partly |
| The designer passes `generateBundles: true, lockSchemaInSourceControlStorage: true`; CrtProcessBuilder saves processes with `(true, true)` and releases the UId-keyed resource cache after the save | [traced BaseProcessSchemaDesigner.cs:213-227; PB Schema/ProcessSchemaRepository.cs:184,189,336-337,352-376] | g2 §3 |
| Enabled / active right after save, on the saving node: the stored item carries `Enabled = true` (from the ctor), `EntitySchemaUId`, `StageColumnUId`, `IsActiveVersion` in `ExtraProperties`; `GetIsEnabled` reads user property (null for a new item) -> extra property; `IsActiveVersion` in the response is computed (`GetActiveVersionItem(item).Id == item.Id`, true for a one-member family). No `SetEnabled`, no `SysSchemaUserProperty` row is needed or written | [traced DcmSchemaManager.cs:118-121; SchemaManagerItem.cs:63-73,341-357,504-534,593-605,653-678; BaseProcessSchemaManager.cs:1262-1265,1612-1616] | g1 (b)-(c) |
| A `SysSchemaUserProperty` row is written only for a schema in a FOREIGN package (an editable package removes it); `fn_GetDcmSchemaIsActive` = `COALESCE(user property, schema property) = 'True'` on the root schema | [traced SchemaManagerItem.cs:711-734; DD SqlScripts/fn_GetDcmSchemaIsActiveMSSql/fn_GetDcmSchemaIsActiveMSSql.sql:10-28] | g1 (c) |

### 3.3 Validation and dependencies

| Fact | Evidence | Chk |
|---|---|---|
| `GetProcessValidationResult` for a DCM adds only `ParameterValuesValidationRule` (base rule list empty); the designer treats it as advisory (save proceeds with `IsValid = false`); the object-model path does not call it | [traced DcmInterpretationValidator.cs:27-31; BaseInterpretationValidator.cs:83-85; BaseProcessSchemaDesigner.cs:273-306] | v r1-C5 confirmed |
| Core never checks that `StageColumnUId` belongs to `EntitySchemaUId`, that it is a lookup, or that `StageRecordId`s exist; a wrong column or record surfaces at run time | [traced DcmSchema / DcmSchemaManager / DcmInterpretationValidator: no such check] | v r1-C5 confirmed |
| Package-dependency auto-add runs only in the designer save, only when `FeatureAutoAddPackageDependencies` AND `...InProcesses` are on (both true in shipped config); `IPackageElementDependencyApplier` and `BaseProcessSchemaPackageElementDependency` are internal | [traced BaseProcessSchemaPackageElementDependency.cs:10,36; IPackageElementDependencyApplier.cs:8; GlobalAppSettings.cs:2672-2679; Web.config:628] | v r1-C5 confirmed; g2 §3 |
| For an element-less DCM the stages report no entity/lookup dependency of their own; the change-stage tasks report column dependencies only when steps exist | [traced DcmSchema.cs:525-535,543-552; DcmSchemaStage.cs:241-245; DcmSchemaBaseChangeStageUserTask.cs:252-258] | g2 §3 |
| The required dependency set of a shipped DCM (column-defining package, object and lookup root packages, CrtCoreBase, CrtBase) is satisfied by every shipped DCM | [corpus 61/61] | g3 |

Decision pointers: Q16 (missing dependencies), M9 (`AutoAddPackageDependenciesInProcesses` on the stands).

### 3.4 Compile and restart

| Fact | Evidence | Chk |
|---|---|---|
| The DCM schema needs no compile and no restart: sources are empty, `IsInterpretable` is forced true, `Package.HasCompilableSchemas` / `PackageCompilabilityInspector` do not count a DCM; 0/61 shipped DCMs carry source | [traced DcmSchemaManager.cs:86-90,148-150; Package.cs:464-477; PackageCompilabilityInspector.cs:29-35; CompilationNecessityDetector.cs:144-167] [corpus 0/61] | v r1-C1, v r4-C4 confirmed; v r6-C2 partly |
| "No compile" covers the DCM only: entity and lookup changes, and custom user-task schemas, stay compilable; settings rows and lookup rows are data | [traced SchemaCodeGenerator.cs:620-622; PackageCompilabilityInspector.cs:29-35] | v r1-C1 confirmed, v r4-C4 confirmed |
| The designer's `TrackSchemaSaved` is a no-op for a DCM (`needCompile = null`) | [traced PackageCompilationOptimizationService.cs:287-293,607-626] | g2 §3 |
| clio-knowledge states the opposite: `process-modeling.md` lists "DCM" among schemas that "carry their own compile obligations" | [traced KB/guidance/mcp/guides/processes/process-modeling.md:157-158] | v r1-C1 confirmed; judge |
| Whether adding a package dependency marks anything for compile is NOT traced | - | open: M10 |

### 3.5 Caches and propagation

| Fact | Evidence | Chk |
|---|---|---|
| Same node: the new item is in `DcmSchemaManager.GetItems()` synchronously, which is what `DcmSchemaManagerRequest` reads | [traced SchemaManager.cs:3135-3141,5069-5078; CORE/Terrasoft.Nui.ServiceModel/WebService/BaseDcmSchemaManagerService.cs:28,49] | g1 (d) |
| `OnChanged` notifies configuration monitors (`UseSchemaManagerEventNotifier` default true; `UseClassFactorySchemaManagerMonitors` default true, run in `Task.Run`); CrtNUI `DcmSchemaManagerCacheResetMonitor` rotates `RuntimeSchemaCacheableHash` for new page loads, asynchronously | [traced SchemaManager.cs:598-602,935-947; GlobalAppSettings.cs:1039,3477; PS CrtNUI/Schemas/DcmSchemaManagerCacheResetMonitor/DcmSchemaManagerCacheResetMonitor.cs:10-48] | g1; r4 §6 (-) |
| Web farm: a NEW DCM reaches other nodes through the schema-manager PubSub sync (`WebFarmEnabled` + `UsePubSubNotificationOnSchemaManagerRefresh`, default true) -> full `RefreshItems()` on next access; `UseSchemaManagerRemoteInvalidation` only invalidates items a node already has, so it does not carry a new DCM | [traced SchemaManagerProvider.cs:172-196,241-244,321-334,366-443; BaseProcessSchemaManager.cs:1665-1672; ProcessFeatures.cs:593-610] | g1 (d), traced only |
| The Freedom UI client caches the DCM list for the tab's lifetime (section 7.5) | [traced UI .../dcm-schema-manager.service.ts:111-135] | v r3-C1 confirmed; g1 |

### 3.6 Removal

| Fact | Evidence | Chk |
|---|---|---|
| `DcmSchemaManager.RemoveItemByUId(uid, uc)` is how AutoTest deletes a DCM; clio `delete-schema` reaches it through `WorkspaceExplorerService.Delete` -> `PackageElementUtilities.Delete` | [traced PS AutoTest/Schemas/ProcessTestService/ProcessTestService.cs:141-150; CORE/Terrasoft.Core.ServiceModel/WorkspaceExplorerService.svc.cs:122,753-787; CORE/Terrasoft.Core/Packages/PackageElementUtilities.cs:1207-1267] | v r4-C1 partly; g5 §4 |
| Deleting the last `SysSchema` row of a UId fires a trigger: `DELETE SysDcmSchemaInSettings WHERE SysDcmSchemaUId = OLD.UId` and `SysDcmSettings.DefaultSchemaUId = NULL`; the `SysDcmSettings` row stays | [traced PS CrtBase/SqlScripts/trg_TRSysSchemaADMSSql/trg_TRSysSchemaADMSSql.sql:19,58-64; TRSysSchemaADPostgreSql.sql:4,14-15] | g5 §4 |
| What `RemoveItemByUId` leaves behind of the save side effects (`SysEntityPrcStartEvent` row, flow schema) is UNMEASURED | - | open: M5 (Q7) |
| Package-data bindings are removable through public `PackageElementUtilities.DeletePackageSchemaData(name, packageUId)`; deleting a binding never deletes the bound records | [traced PackageElementUtilities.cs:1268-1271,1685-1697] | judge; g5 §4 |

---

## 4. Runtime

### 4.1 Generated flow

| Fact | Evidence | Chk |
|---|---|---|
| Start signal `DcmSchemaStartSignal`: entity `Inserted` on `EntitySchemaUId`, filtered by `Filters` | [traced DcmSchema.cs:78,352-363] | v r1-C7 partly |
| Exclusive gateway `DcmStagesGateway`; intermediate catch signal: entity `Updated`, changed column = `StageColumnUId`, self-loop (waits again after every change) | [traced DcmSchema.cs:365-378; DcmFlowSchemaGenerator.cs:71,144-160] | - |
| One `DcmStageUserTask` per stage, reached by a conditional flow `[#ColumnValue.{EntitySchemaUId}.{StageColumnUId}#] == new Guid("<StageRecordId>")` - the record Id is baked into the flow | [traced DcmFlowSchemaGenerator.cs:96-100,162-174] | v r1-C3 partly |
| `DefaultStage` takes the gateway's default flow: ANY value that is not a stage (empty or unknown) lands there, with logging off | [traced DcmSchema.cs:390-411; DcmFlowSchemaGenerator.cs:273-278] | v r1-C3, v r1-C7 partly |
| Final stage = the last stage of `Stages`, its parent group, or a sibling alternative; an empty final stage flows straight to `DcmSchemaTerminateEvent` | [traced DcmSchema.cs:568-577; DcmFlowSchemaGenerator.cs:429-440] | v r1-C3 partly |
| Minimal case (N empty stages): start, gateway, catch (self-loop), N stage tasks, default stage, terminate from the last stage; no transition tasks, no change-stage gateways | [inferred from the rows above] | - |

### 4.2 Instance start and existing records

| Fact | Evidence | Chk |
|---|---|---|
| A NEW record starts a case through the Inserted start signal (`RunProcesses`), limited by `Filters` | [traced ProcessEngine.cs:1156-1163,1182-1208] [academy https://academy.creatio.com/guides/no-code-customization/bpm-tools/dynamic-case-setup/case-designer-workflows/execute-case] | v r1-C7 partly |
| Saving a DCM creates no instance for existing records; an existing record starts one on an UPDATE that changes the stage column (or a filter column, config flag `Feature-UseStartDcmWithoutChangingStage`, default true) when no running DCM listener exists (`BaseEntity` -> `RunDcmProcesses`) | [traced BaseEntitySchema.CrtCoreBase.cs:1014-1030; BaseEntity.cs:130-168; DcmEntityUtilities.cs:359-365,441-476; GlobalAppSettings.cs:491] | v r1-C7 partly |
| After a case completes, the next qualifying update starts a new one | [traced ProcessEngine.cs:1914-1926] | v r1-C7 partly; open: M7 |
| `DcmStageUserTask` returns `IsFinalStage && !HasStageElements`: an empty final stage ends the case at once | [traced DcmStageUserTask.cs:147-154,172-178] [note "What happens on a stage change"] | - |
| `RunDcmProcesses` starts EVERY enabled, active-version DCM of the object whose filters match; `RunDcmInstance`, `CancelNotEnabledDcmProcess` and the bar assume one; "one DCM per object" is not a platform rule (`CoreLead.LeadCase`, `Opportunity.OpportunityCase` are disabled next to enabled replacements) | [traced ProcessEngine.cs:498-502,1866-1874,1914-1926; DcmEntityUtilities.cs:434-476] [corpus Lead/Opportunity Enabled=False] | v r1-C8 partly |
| Explicit starts: `ChangeToAppropriateDcmInstance` (cancels running instances of the record, may overwrite the stage to the first stage; exposed through `ProcessEngineService`, used by the bar's change-case button) and `DcmService.RunDcmProcess` (no checks) | [traced ProcessEngine.cs:1182-1215,1981-1986; ProcessEngineService.svc.cs:1173-1180] | v r1-C7 partly |
| The running DCM of a record is found through `SysEntityCommonPrcEl` -> `SysProcessElementData` -> `SysProcessData` -> `SysSchema(ManagerName='DcmSchemaManager')` | [traced GetEntityRunningProcess/GetEntityRunningProcessAction.cs:13-70] | - |

Decision pointer: note N3 of the design (design section 3.4); M7 measures it.

### 4.3 Stage change

| Fact | Evidence | Chk |
|---|---|---|
| A stage change is a save of the record with a new lookup value; there is no change-stage service; a direct DB update does not wake the case | [note "What happens on a stage change"; note "One stage change, step by step"] | - |
| Entity layer: `Validating` checks required steps of the previous stage on forward moves and stage permissions (`DcmStagesPermissions`); `Saved` wakes the running case (`ContinueExecuting`) or starts one | [traced BaseEntitySchema.CrtCoreBase.cs:1021-1041; BaseEntity.CrtCoreBase.cs:114-173; DcmEntityUtilities.cs:282-323] | - |
| A step-driven stage change writes the column and saves with `UseAdminRights = false`, re-entering the entity layer | [traced DcmBaseChangeStageUserTask.cs:67-84] | - |
| User-visible rules: forward only when required steps are done; back always (default connections); a final stage without steps completes the case when selected; a deactivated case does not start for new records, running instances continue | [academy https://academy.creatio.com/guides/no-code-customization/bpm-tools/dynamic-case-setup/case-faq] [note "What happens on a stage change"] | - |
| For a step-less case: forward checks have nothing to block and backward moves nothing to cancel [inferred] | (r9 §4.4) | - |
| Moving a DCM to another environment needs the same lookup value Ids there (the designer binds new or changed values as `<Lookup>_Dcm_Data`) | [traced DcmFlowSchemaGenerator.cs:170-171] [note "A schema is not enough - data bindings are needed"] | v r2-C6 partly |

---

## 5. `SysDcmSettings` and `SysDcmSchemaInSettings`

### 5.1 Shape

| Fact | Evidence | Chk |
|---|---|---|
| `SysDcmSettings` (CrtCoreBase): `SysModuleEntity` (lookup), `StageColumnUId` (Guid), `Filters` (MaxSizeText), `DefaultSchemaUId` (Guid), `SysSchemaUId` (Guid, "Object UId"); `administrated-by-operations: true` | [traced PS CrtCoreBase/Schemas/SysDcmSettings/metadata.json] [measured `get-entity-schema-properties SysDcmSettings`] | - |
| `SysDcmSchemaInSettings` (CrtBase): `SysDcmSettings` (lookup, cascade delete `E9: true`), `SysDcmSchemaUId` - a plain Guid column, not a lookup to `SysSchema` | [traced PS CrtBase/Schemas/SysDcmSchemaInSettings/metadata.json:10-28] | judge; g5 §4 |
| Two forms: section form (`SysModuleEntity` set, 7.x sections, `DefaultSchemaUId` = the DCM) and object form (`SysSchemaUId` = entity, `SysModuleEntity` null, `DefaultSchemaUId` null, `Filters = "[{\"columnUId\":null}]"`; the `UseDcmForGeneralObject` route); 25/44 shipped rows use `SysModuleEntity`, 17/44 are entity-bound, pure 8.x product shape 9 rows / 7 packages | [traced PS CrtKnowledgeManagementVersions/Data/SysDcmSettings_KnowledgeArticle/data.json] [corpus 44 rows] | v r4-C6, v r5-C8 partly |
| `Filters = "[{\"columnUId\":null}]"` is designer output, not a requirement (35/44 rows) | [traced DL Schemas/SectionDcmSettings/SectionDcmSettings.js:668-682] [corpus 35/44] | v r5-C8 partly |
| One entity can have two settings rows (Case: one section-bound in `Case`, one entity-bound in `CrtCaseManagementApp`, both linking `CaseCase`) | [corpus] | - |
| Stand: one settings row, section form, for `OmniChat` (`SysModuleEntity ed9ae739-...`, `SysSchemaUId` empty, `Filters [{"columnUId":null}]`) and one link row to OmniChatCase | [measured `execute-esq SysDcmSettings`, `SysDcmSchemaInSettings`, `SysModuleEntity Id = ed9ae739-...`] | v r10-C5 confirmed |

### 5.2 Who reads and writes them

| Fact | Evidence | Chk |
|---|---|---|
| Core never reads or writes either table (`grep SysDcmSettings` over Terrasoft.Core = a test service reference only); starting a case uses `SysEntityPrcStartEvent` + metadata | [traced grep] | v r5-C8 partly |
| No server listener acts on either table; the only server writer in the corpus is CrtGenAI `GeneratedDcmSaver` | [traced grep PS */branches/7.8.0 *.cs and CORE] | judge |
| `DcmService.GetDcmStageColumnName(entityName)` (DcmDesigner) reads `TOP 1 StageColumnUId` where `SysModuleEntity.SysEntitySchemaUId = @uid OR SysSchemaUId = @uid ORDER BY ModifiedOn DESC`; in 8.x it feeds only `crt.ActionDashboard` and the playbook panel | [traced DD Schemas/DcmService/DcmService.cs:58-76; UI .../dcm-settings/dcm-settings.service.ts:12,23-27; action-dashboard-preprocessor.ts:46-52] | v r4-C6 partly |
| The 8.x progress bar does NOT read them (it takes the stage column from the DCM) | [traced BAR/runtime/entity-stage-progress-bar-preprocessor/entity-stage-progress-bar-preprocessor.ts:87-96,164-168,206; creatio-ui commits 80d8628b4c0, 5ee37abece0] | `_verdicts.md` conflict 1 |
| The 7.x designer needs the `SysDcmSchemaInSettings` row to open a case (section 6.2) | [traced DD Schemas/DcmSchemaDesignerViewModel/DcmSchemaDesignerViewModel.js:269-273,457-475,1093-1106] | v r2-C4 confirmed |
| Cases wizard: its case list is `VwDcmLibSection` over `VwSysDcmLib` by `EntitySchemaUId` (not through the link table); the link table drives the default-case list and the last-active check; `SysDcmSettings.StageColumnUId` drives the enable check (a case whose column differs from settings is refused); missing settings are created in memory | [traced DL Schemas/SectionWizardCasesSettings/SectionWizardCasesSettings.js:718-845; VwDcmLibSection.js:14,236-243,360-374,510-540; SectionDcmSettings.js:298-455] | v r9-C7, v r2-C3 partly |
| `VwSysDcmLib` is a DB view (no writes): `IsActive` from `fn_GetDcmSchemaIsActive`, `Filters` from `SysEntityPrcStartEvent.ConditionData`, entity and column from `SysSchemaProperty` | [traced DD SqlScripts/CreateVwSysDcmLibMSSql/CreateVwSysDcmLibMSSql.sql:23-31] | g1 (c) |
| Changing a section's stage column (or filter column) in the 7.x settings page deactivates cases whose `stageColumnUId` differs, after a confirmation | [traced DL Schemas/SectionDcmSettings/SectionDcmSettings.js:839-925] | v r2-C3 partly |
| GeneratedDcmSaver writes the object form (`SysSchemaUId`, `StageColumnUId` only), inserts a new row without an existence check, and binds under bare names `SysDcmSettings` / `SysDcmSchemaInSettings` | [traced GeneratedDcmSaver.cs:161-182] | v r4-C8 partly |

### 5.3 Package binding

| Fact | Evidence | Chk |
|---|---|---|
| The 7.x designer binds the rows after the schema save as `SysDcmSchemaInSettings_<rowId no dashes>`, `SysDcmSettings_<rowId no dashes>` and new or changed lookup rows as `<Lookup>_Dcm_Data`; existing lookup values are not bound | [traced DD .../DcmSchemaDesignerViewModel.js:430-557,1381-1392; SysDcmSettingsManager.js:87-90] | v r2-C6 partly |
| Shipped names: 97/97 bindings carry a suffix, 66 the designer id form, 31 hand-named (`SysDcmSettings_OmniChat`, `SysDcmSettings_LeadManagementCase`) | [corpus 97/97] [measured `execute-esq SysPackageSchemaData` Name contains `SysDcm` or `_Dcm_Data`] | v r4-C8, v r2-C6 partly |
| `IPackageSchemaDataCreator.CreatePackageSchemaData(name, packageUId, recordIds, recordSchemaName)`: interface in Terrasoft.Core, implementation internal, bound to `ClassFactory` on both hosts; uses the request's `UserConnection` | [traced CORE/Terrasoft.Core/Applications/Abstractions/Content/IPackageSchemaDataCreator.cs:10-22; Terrasoft.Core.Applications/Content/PackageSchemaDataCreator.cs:15; CoreModule.cs:577; Terrasoft.WebApp/Global.asax.cs:748; Terrasoft.WebHost/ApplicationBindings.cs:295] | v r4-C7 partly |
| Same name in the same package = the existing binding is reused and its data REPLACED by the passed Ids (empty list wipes it); a validation error (record already bound in the same package) returns silently; other failures throw (no `CanManageSolution` -> `SecurityException`, foreign package, unknown schema) | [traced PackageSchemaDataCreator.cs:43-65,114-139,155-187; PackageElementUtilities.cs:746-752,784-825,1820-1831] | v r4-C7 partly |
| Where a `ClassFactory`-resolved binding from a CrtProcessBuilder WCF call lands, and under whose rights, is UNMEASURED | - | open: M6 |

Decision pointers: Q13 (existing row), Q14 (form, `Filters`), Q15 (binding names), Q21 (lookup values).

---

## 6. Designer behaviour (7.x designer used from 8.x)

### 6.1 Entry points

| Fact | Evidence | Chk |
|---|---|---|
| There is no Freedom UI (Angular) case designer; grep of creatio-ui finds only the open template and mocks | [traced grep UI libs/apps `DcmDesigner\|#case/\|CaseDesigner\|openDcm`] [note] | v r9-C8 partly |
| 8.x opens the 7.x designer by UId: `<root>[/0]/Nui/ViewModule.aspx?vm=DcmDesigner#case/<schemaUId>` (Configuration, app hub); `UIRedirectBlacklist_DcmDesigner` keeps it from redirecting to 8.x | [traced UI libs/studio-enterprise/data-access/common-api/src/lib/app-info/app-info.service.ts:422-429; DD Data/UIRedirectBlacklist_DcmDesigner] | v r9-C8 partly |
| The bar's properties panel "Set up cases" opens `?vm=SectionCasesWizard#Cases/<sysModuleId or entityUId>/<packageUId>` (the 7.x Section wizard limited to its Cases step), using the entity UId under `UseDcmForGeneralObject` | [traced BAR/base-design-time/properties-panel/base-entity-stage-progress-bar-properties-panel.component.ts:134-139,202-217] | v r9-C8 partly |
| Creating a DCM from the 8.x Configuration explorer is not offered (`createTemplate: ''`) | [traced app-info.service.ts:423-428] | - |
| The designer's server save: `DcmSchemaManagerService.Post(ContractDcmSchema)` -> `BaseDcmSchemaManagerService.InternalPost` (process-local static lock) -> `new DcmSchemaDesigner(uc).Save`; the WCF `DcmSchemaManagerService.svc` cannot save | [traced BaseDcmSchemaManagerService.cs:22,158-171,234-241; DcmSchemaDesigner.cs:49-54] | v r2-C7 partly |

### 6.2 Opening a case and what makes it show errors

| Condition | Effect | Evidence | Chk |
|---|---|---|---|
| No `SysDcmSchemaInSettings` row whose `SysDcmSchemaUId` equals the opened UId and whose `SysDcmSettings` is non-empty | error "DcmSettingsId not found.", the case does not load; rows are read fresh at each designer start, so server-inserted rows are seen | [traced DD .../DcmSchemaDesignerViewModel.js:269-273,457-475,1093-1106; SysDcmSchemaInSettingsManager.js:24,54-60] | v r2-C4 confirmed |
| A stage whose `stageRecordId` is not a row of the stage-column lookup visible to the user (unfiltered ESQ under user rights) | stage reset (`stageRecordId = null`, `isValid = false`); a reset stage blocks the designer save | [traced DcmSchemaDesignerViewModel.js:568-613,1426-1454; base-process-schema.js:312-322] | v r2-C5 confirmed |
| A stage whose record is found | caption silently replaced by the lookup display value, current culture, in memory | [traced DcmSchemaDesignerViewModel.js:568-583] | v r2-C5 confirmed |
| Open by UId (including the 8.x link) | entity and stage column are NOT copied from settings (only a NEW case copies them); the designer shows the schema's own `EntitySchemaUId` / `StageColumnUId`, which can drift from settings | [traced DcmSchemaDesignerViewModel.js:289-321,1093-1105,1155-1173] | v r2-C3 partly |
| Entity not visible from the DCM's package (package-aware lookups when `AutoAddPackageDependenciesInProcesses` is off) | `initEntitySchema` cannot find the schema [inferred] | [traced DD Schemas/DcmSchema/DcmSchema.js:804; DcmSchemaDesignerViewModel.js:1426-1449] | - |
| Filters: a case without filters and settings without a filter column passes filter validation trivially | | [traced DD Schemas/DcmSchemaManager/DcmSchemaManager.js:446-450] | - |
| A route-A (object-model) DCM opens like a route-B one: the designer reads persisted metadata, properties and resources only, equal for a minimal case except `B8` / `IJ10` (both legitimately absent in shipped DCMs) | | [traced BaseDcmSchemaManagerService.cs:133-151] [inferred] | g2 §4; NOT measured: M1 |

### 6.3 What the designer itself does (the rules a server tool must port)

| Fact | Evidence | Chk |
|---|---|---|
| A new case starts with ONE placeholder stage "Stage 1" (`stageRecordId = null`, `isValid = false`); stages are added one by one; nothing fills stages from the lookup; lookup order and colour columns are never read | [traced DD Schemas/DcmSchemaManager/DcmSchemaManager.js:132-145,346-351; DcmSchemaDesignerViewModel.js:1245-1266] | v r2-C1 confirmed, v r9-C3 partly |
| The stage picker excludes used values and sorts by display value; quick-add creates a new lookup row; renaming a stage writes the lookup display value | [traced DcmSchemaStagePropertiesPage.js:29-34,365-401,660-699,728-760] | v r2-C2 confirmed |
| Entity and stage column come from the section-level settings row (chosen in the Section wizard Cases step) and are read-only in the case properties | [traced DcmSchemaDesignerViewModel.js:289-307,1155-1173; DcmSchemaPropertiesPage.js:658-690] | v r2-C3 partly |
| Stage-column candidates in the settings page = every lookup column of the entity minus the filter column, sorted by caption | [traced DL SectionDcmSettings.js:380-383,494-526] | - |
| Client defaults: all-to-all connections on every added stage, `TransitionType.None`, colour default `#8ecb60` saved as `#0C8701` | [traced DD DcmSchema.js:247-256,480-491; DcmSchemaStage.js:35,63,96,108; DcmConstants.js:78,116] | v r9-C5 partly |
| After the schema save the client writes: link row if missing -> pending settings changes -> deactivates other enabled same-settings cases with null filters ONLY when the saved case has filters -> new or changed lookup rows; each bound into the case's package | [traced DcmSchemaDesignerViewModel.js:430-557,1381-1392] | v r2-C6 partly |
| Versioning (`DcmCasesVersioning`) is a client switch only; the 8.x service returns only active versions | [traced DD DcmSchemaManager.js:319-328,389-407,523-525] | - |
| Truly client-only (must be ported): settings create-or-reuse, designer binding names, default colour and its 8.x mapping, stage-column-change deactivation. NOT client-only: link row, full mesh, colour, binding (GeneratedDcmSaver does them server-side) | [traced GeneratedDcmSaver.cs:29,143-177; DcmService.cs:58-77] | v r2-C13 partly |

Decision pointers: Q6 (save route), Q29 (stage shape), M1 (designer opens the case).

---

## 7. `crt.EntityStageProgressBar`

### 7.1 Component and page configuration

| Fact | Evidence | Chk |
|---|---|---|
| Registered as `crt.EntityStageProgressBar`; defaults `saveOnChange: false`, `askUserToChangeSchema: true` | [traced BAR/runtime/components/entity-stage-progress-bar.component.ts:30-33,39-41,121,131] | v r3-C1 confirmed |
| Author-set keys: `entityName` (+ optional `saveOnChange`, `askUserToChangeSchema`, `layoutConfig`, `visible`); no stage column appears in any page config | [corpus 25 inserts: entityName 25, saveOnChange 23, askUserToChangeSchema 22, layoutConfig 12, visible 8] | v r5-C9, v r10-C7 partly |
| The only page change is a `viewConfigDiff` insert; `viewModelConfigDiff`, `modelConfigDiff` and `handlers` stay empty in the shipped template | [traced PS CrtUIPlatform/Schemas/PageWithTabsAndProgressBarTemplate/PageWithTabsAndProgressBarTemplate.js:43-83] | v r3-C1 confirmed |
| `entityName` must be the page's primary data source entity | [traced preprocessor.ts:72-74,164-168] | v r3-C1 confirmed |
| Allowed parents per the shipped component recipe: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer` | [traced BAR/runtime/components/entity-stage-progress-bar.component.md:18-21] | - |
| Hand-wired `recordId`, `value`, `stages`, outputs have no effect: the preprocessor overwrites them on every open (8.3.0 to master) unless the kill switch is on; 0/24 corpus pages wire them | [traced preprocessor.ts:265-324] [corpus 0/24] | v r3-C2 partly |

### 7.2 Runtime preprocessor

| Fact | Evidence | Chk |
|---|---|---|
| An `OnModelInit` preprocessor finds every bar, groups by `entityName`, and creates attributes `ProgressBarComponent_<Entity>_*` and four request wirings (`loadData`, `stageChanged`, `setAllowedStages`, `changeToAppropriateEntityStageSchema`) | [traced preprocessor.ts:59-67,83-85,191,215-220,265-344] | v r3-C1 confirmed |
| It keeps DCMs of the entity with `isActive && isActiveVersion`; the stage column = `entityStageSchemas[0].stageColumnUId` (the FIRST one) | [traced preprocessor.ts:87-112,158-168,206] | v r3-C1 confirmed |
| The current-stage attribute reuses an existing attribute whose `modelConfig.path` is `<PDS>.<StageColumn>` (the page's own field), else creates one | [traced preprocessor.ts:207-214,228-241] | v r3-C7 partly |
| Without a DCM: `stageColumnUId` stays empty and `value` is bound to `'$'`; the bar cannot render from the lookup alone | [traced preprocessor.ts:87-97,114-117,145,164-168,206-214,275] | v r3-C3, v r9-C1 partly |
| Kill switch: feature `DisableEntityStageProgressBarPreprocessing` disables the preprocessor; off by default; its stand value was NOT measured | [traced preprocessor.ts:72-74,327-329] | v r3-C1 confirmed |

### 7.3 Data source

| Fact | Evidence | Chk |
|---|---|---|
| DCM list: `DataService/json/SyncReply/DcmSchemaManagerRequest` -> `BaseDcmSchemaManagerService.InternalPostDcmSchemaManagerRequest`: by default the in-memory `DcmSchemaManager.GetItems()` with `IsActive` = root `Enabled`, `IsActiveVersion` = `GetIsActiveVersion(item)`, `EntitySchemaUId`, `StageColumnUId`, `Filters` (from the start-event helper); with `DisableFastDcmSchemaManagerRequest` (default off) a `VwSysDcmLib` select | [traced CORE/Terrasoft.Nui.ServiceModel/WebService/BaseDcmSchemaManagerService.cs:34-81,83-128; DisableFastDcmSchemaManagerRequest.cs:11-14] | v r3-C1 confirmed; g1 (c) |
| Stages: `DcmSchemaInfoRequest` -> `GetDcmSchemaInfo(uId)` maps `Stages` (Caption, Color, UsePermissions, UId, StageRecordId, ParentStageUId, Permissions) and `StageConnections` (UId, Source, Target); on exception it returns an error with an empty `Stages` list | [traced CORE/Terrasoft.Nui.ServiceModel/WebService/DcmSchemaManagerService.cs:63-90,110-121; DataContract/DcmDataContract.cs:481,487] | v r3-C3 partly, v r3-C6 confirmed |
| Load order: if the record has a RUNNING instance (`DcmRunningProcessRequest`, or `...WithHistoryRequest` under `EnableHistoryDCM`) its DCM wins with no active/version/entity check; otherwise the DCM is chosen by the filter value (a DCM without filters always matches) | [traced BAR/runtime/handlers/entity-stage-progress-bar-load-data.handler.ts:241-269; util/entity-stage-progress-bar-utils/src/services/entity-stage-schema-filter.service.ts:14-35] | v r3-C3, v r5-C9 partly |
| A record without a running instance still renders the bar (DCM chosen by filter) | [traced load-data.handler.ts:257-270] | - |
| Captions: lookup primary display value by Id (ESQ Id + display column, under user rights, cached per lookup schema), falling back to the stage caption | [traced load-data.handler.ts:117-155,277-280,293-311] | v r3-C6 confirmed |
| One step per DCM stage in DCM array order; `id = stageRecordId`; last flattened stage (+ its parent) `isFinal`; the lookup is read unordered | [traced load-data.handler.ts:139-153,293-320] | v r3-C6 confirmed |
| Allowed stages: `rest/DcmService/GetAllowedStagesForCurrentUser` (or `DcmInfoRequest` under `ShowCustomHintMessageInProgressBar`); a stage without permissions is allowed for everyone; `IDcmInfoProcessor` plugins exist only in test packages | [traced load-data.handler.ts:170-189; CORE/Terrasoft.Core/DcmProcess/DcmPermissions.cs:41-56,85-93] | v r9-C2 confirmed |
| Connections and allowed stages come back by stage UId and are mapped to `stageRecordId` client-side, so stage UIds may differ from record Ids (designer) or equal them (GeneratedDcmSaver) | [traced load-data.handler.ts:93-115] | v r3-C4 partly |

### 7.4 Rendering conditions and clickability

| Fact | Evidence | Chk |
|---|---|---|
| The bar needs an enabled, active-version DCM with `EntitySchemaUId` = page entity (a running instance bypasses the filter); an empty non-null stage array hides the component host (`display:none`); `null` stays visible in loading state | [traced component.ts:87,142-146,368-381; preprocessor.ts:301-312] | v r9-C1, v r3-C3 partly |
| A null or missing stage `Color` throws in `getStageColor` (`toUpperCase` without a guard) and no stages render; an empty string does not throw; `#54AA4D` and `#8ECB60` render as `#0C8701` | [traced util/entity-stage-progress-bar-utils/src/services/entity-stage-progress-bar-color-mapper.service.ts:22,60,85-112; load-data.handler.ts:298,326-334] | v r3-C5 partly; `_verdicts.md` conflict 6 |
| Clickability: `disabled = !isAllowed || current`; when the current value IS a stage, a target without a connection from it is disabled; empty or unknown current value -> every allowed non-current stage is clickable; zero connections = frozen bar | [traced BAR/runtime/handlers/entity-stage-progress-bar-process-stage-permissions.handler.ts:45-56] | v r3-C4 partly; `_verdicts.md` conflict 7 |
| An unknown UId in a connection gives a `TypeError` | [traced process-stage-permissions.handler.ts:51-55] | v r3-C4 partly |

### 7.5 Sync with the lookup, saving, caching

| Fact | Evidence | Chk |
|---|---|---|
| Click -> lookup: only enabled steps reach the handler; it writes `{value: stageRecordId, displayValue}` into the `<PDS>.<StageColumn>` attribute only when changed, and marks the control dirty | [traced BAR/runtime/handlers/entity-stage-progress-bar-stage-changed.handler.ts:48-65,96-127; component.ts:436-451] | v r3-C7 partly |
| `saveOnChange: false` (default): page dirty, the user saves; `true`: silent set + `crt.SaveRecordRequest` (`preventCardClose: true`), revert on failure, reload of a new record after save | [traced stage-changed.handler.ts:47-77] | v r3-C7, v r9-C6 partly |
| Lookup -> bar: the `value` input is bound to the same attribute, so a field edit or a reload moves the bar; this direction never writes or saves | [traced component.ts:189-205] | v r3-C7 partly |
| A click therefore always writes the attribute; `saveOnChange` only decides whether the record is saved at once; both values satisfy "changing the stage changes the lookup value" | [traced component.ts:30-33,47,121,191-202] | v r9-C6 partly |
| `askUserToChangeSchema` matters only with several DCMs split by a filter column (change-case snackbar needs feature `EntityStageProgressbar-ShowSnackbar`); never triggers for a single unfiltered DCM [inferred] | [traced component.ts:251-274,392-415; entity-stage-process-get-appropriate-schema.converter.ts:38-47] | - |
| The client `DcmSchemaManagerService` is `providedIn: 'root'` and memoizes `DcmSchemaManagerRequest` for the SPA lifetime: a tab opened before the DCM existed needs F5 | [traced UI libs/studio-enterprise/data-access/common-api/src/lib/dcm/dcm-schema-manager-service/dcm-schema-manager.service.ts:111-135] | v r3-C1 confirmed; g1 |
| A list page whose grid shows the stage column renders it as a stage slider automatically once the DCM exists (feature `DisableDataGridProgressBarStageCell` turns it off) | [traced UI .../data-grid-stage-cell-view-preprocessor.ts:75-164; lookup-cell-view-element-config-creator.service.ts:78-130,150-160] | - |

### 7.6 Precedents for the page fragment

| Precedent | Shape | Evidence | Chk |
|---|---|---|---|
| AI app wizard (GenAI Create Application only, main section with `hasDcm`, only pages whose DIRECT parent is `PageWithTabsFreedomTemplate` `3b2e117f-...`; also adds a Next steps tab) | `ProgressBarContainer` (`crt.FlexContainer`, column, `gap: small`) at index 0 of `MainContainer`, then `ProgressBar` (`crt.EntityStageProgressBar`, literal `entityName`, `saveOnChange: true`, `askUserToChangeSchema: false`) | [traced AM/services/page-content-generator/page-content-generators/page-with-tabs/page-with-tabs-freedom-content-generator.ts:57-109,318-321; page-content-generator.service.ts:82-83,105-108] | v r3-C8 partly |
| Template `PageWithTabsAndProgressBarTemplate` (CrtUIPlatform, `7ed23be6-...`, parent `PageWithTabsFreedomTemplate`, 7 child pages) | same container (`gap: none`, `visible: true`), bar with `saveOnChange: false`, `askUserToChangeSchema: true`, `entityName: "#DataSourceEntityName()#"`, `layoutConfig: {}` | [traced PageWithTabsAndProgressBarTemplate.js:43-83] | v r3-C8 partly |
| Shipped pages | 24 files / 25 inserts; only 8/25 in `ProgressBarContainer` under `MainContainer`; product web pages 4/12 `saveOnChange: true` | [corpus 24 files, 25 inserts] | v r10-C7, v r9-C6 partly |
| AI app client contract | `AppGenDcm {dcmSchemaUId, entitySchemaUId, stageColumnName, stageColumnUId, stagesEntitySchemaUId, stages}`; used only to strip the stage column and lookup from demo data; page work driven by `section.hasDcm` | [traced AM/services/application-installer.service.ts:31-55; create-application.component.ts:232-264] | - |
| `MainContainer` is a standard element of `BasePageFreedomTemplate` (moved under `Main` at index 1) | | [traced PS CrtUIPlatform/Schemas/BasePageFreedomTemplate/BasePageFreedomTemplate.js:69-74] | - |
| Mini pages use a `crt.GridContainer` `MainContainer`; no product precedent for the bar on a mini page | | (g4 §5) | g4 |
| Future steps need no page change: the bar reads stages and connections from the DCM at run time [inferred] | | (r3 §10 item 8) | - |

Decision pointers: Q18 (page write path), Q19 (flags), Q20 (typed, mini, Classic pages); M3 (browser).

---

## 8. Feature flags

"Stand" = stand `Creatio`, [measured `execute-esq Feature` / `AdminUnitFeatureState` filter `Code contains 'Dcm'`]
(r10 §3). Every `FeatureState = 1` row is an "All employees" (or "All external users") override; 5 of the 10 DCM
features have `DefaultState = 0` and are OFF for portal users (v r10-C3 partly).

| Flag | Where / default | Effect on this task | Stand | Evidence | Chk |
|---|---|---|---|---|---|
| `UseDcmForGeneralObject` | CrtWizards7x data, All employees | settings keyed by `SysSchemaUId` when the object has no `SysModuleEntity`; "Set up cases" passes the entity UId | ON | [traced DL SysDcmSettingsManagerItem.js:130-138; SectionDcmSettings.js:296-301] | v r10-C3, v r10-C5 |
| `DcmCasesVersioning` | DcmDesigner data, All employees | client-side version UI and queries | ON | [traced DD DcmSchemaManager.js:523-525] | v r10-C3 |
| `DcmStagesPermissions` | DcmDesigner data, employees + external | stage permissions edited and checked in `Validating` | ON | [traced DcmEntityUtilities.cs:282-323; DD DcmSchemaStage.js:161] | v r10-C3 |
| `UseDcmServerEndPoint`, `UseDcmSchemaInfoInDcmActionsDashboard`, `ChangeToAppropriateDcmInstance`, `DcmNotImplementedElements` | DcmDesigner data | 7.x client loading, dashboard, change-case, step palette | ON | [traced DD Data/Feature_*] | v r10-C3 |
| `SynchronizeStagesByDcm` | CrtBase data | stage history read from the DCM (`SaveStageHistoryAsyncOperation`) | ON | [traced PS CrtBase Data/Feature_SynchronizeStagesByDcm; SaveStageHistoryAsyncOperation.cs:81-85] | v r10-C3 |
| `UseFastLoadDcmActionDashboard`, `OpenEditPageInDcm` | ActionsDashboard, CrtNUI data | 7.x dashboard and step behaviour | ON | [traced PS */Data/Feature_*] | v r10-C3 |
| Code-only flags: `UseDcmExpirationPeriod` off; `UseReentrantDcmStageSignal` on; stage toggles `DisableAsyncStageSave`, `EntityStageProgressbar-ShowSnackbar`, `EntityStageHistoryJournaling` on | core / UI | - | as stated | [traced ProcessFeatures.cs:68-78,312-324; DbFeatureProvider.cs:359-386,703-713] | v r10-C3 |
| `DisableEntityStageProgressBarPreprocessing` | UI, off by default | kills the bar preprocessor | NOT measured | [traced preprocessor.ts:72-74] | v r3-C1 |
| `DisableDataGridProgressBarStageCell`, `EnableHistoryDCM`, `ShowCustomHintMessageInProgressBar` | UI | list slider cell, history request, hint request | NOT measured | [traced data-grid-stage-cell-view-preprocessor.ts:75-164; load-data.handler.ts:170-189,243-248] | - |
| `DisableFastDcmSchemaManagerRequest` | core, default `IsEnabled = false` | `DcmSchemaManagerRequest` reads `VwSysDcmLib` instead of the manager | default | [traced DisableFastDcmSchemaManagerRequest.cs:11-14] | g1 (c) |
| `Feature-UseStartDcmWithoutChangingStage` | config, default true | a filter-column change also starts a case on an existing record | default | [traced GlobalAppSettings.cs:491] | v r1-C7 |
| `UseSchemaManagerRemoteInvalidation` (default on), `UsePubSubNotificationOnSchemaManagerRefresh` (default true), `UseSchemaManagerEventNotifier` (default true) | core | propagation and client cache hash (section 3.5) | single node | [traced ProcessFeatures.cs:593-610; GlobalAppSettings.cs:258-263,1039,1271,3477] | g1 (d) |
| `FeatureAutoAddPackageDependencies` + `AutoAddPackageDependenciesInProcesses` | config, both true | designer dependency auto-add; designer lookups package-aware when off | `AutoAddPackageDependenciesInProcesses` not found in `AdminUnitFeatureState` | [traced GlobalAppSettings.cs:2677-2679; DD DcmSchema.js:804] [measured r2 §10] | v r1-C5; open: M9 |
| `UseProcessRemindings` | Web.config / app.config true | `BK37` written | - | [traced Web.config:539; app.config:317] | g2 §3 |
| `EnableOptimizedPackageCompilation` | default on | designer `TrackSchemaSaved`, a no-op for a DCM | - | [traced EnableOptimizedPackageCompilation.cs:7-13] | g2 §3 |
| `GenAIFeatures.GenerateDcm` | CrtGenAI | gates only the AI app generator's DCM path | - | [traced GenAIFeatures.cs:7-13; GenAIApplicationContentGenerator.cs:415-428] | v r4-C10 |
| Sys setting `DcmLogExpirationPeriod` (Integer) | - | the only `Dcm` sys setting on the stand | present | [measured `execute-esq SysSettings Code contains 'Dcm'`] | - |

No DCM-specific license check exists; generic licensed-entity checks apply to `Entity.Save` of the settings rows
[traced Entity.cs:1124-1138,2966-2974,3316-3320] (v r4-C10 partly).

Decision pointer: Q28 (no clio feature toggle).

---

## 9. Rights

| Operation | Platform check | Evidence | Chk |
|---|---|---|---|
| DCM schema save (object model or designer) | General user (`ConnectionType`) AND operation `CanManageDcm`, else `SecurityException` (raw text has an unfilled `{0}`); replaces the base `CanManageSolution` check; foreign package refused | [traced CORE/Terrasoft.Core/DcmProcess/DcmSchemaManager.cs:37,132-139; SchemaManager.cs:992-998,3213-3219,5013-5018] | v r6-C3 confirmed; judge |
| User properties of a DCM (Enabled toggle) | `CanManageDcm` | [traced DcmSchemaManager.cs:37] | - |
| Package-data binding (`IPackageSchemaDataCreator`) | General user + `CanManageSolution` (`GetCanModifyPackageElement`) | [traced CORE/Terrasoft.Core/Packages/PackageElementUtilities.cs:1820-1825; CORE/Terrasoft.Core/DB/DBSecurityEngine.cs:5344-5347] | judge |
| 7.x client binding through `ManagerService` (`UpdatePackageSchemaDataRequest`) | `CanManageSolution` OR `CanManageDcm` when `FeatureEnableManagerServiceSecurity` | [traced CORE/Terrasoft.Nui.ServiceModel.WebService/ManagerService.cs:44,207-211,680-695] | - |
| `SysDcmSettings` insert | entity operation rights; CrtBase grants "All employees" read only (three `false`, one `true`); check API `GetEntitySchemaOperationsRightLevel` is public | [traced PS CrtBase/Data/SysEntitySchemaOperationRight_SysDcmSettings/data.json; DBSecurityEngine.cs:3707] | judge; this file |
| `SysDcmSchemaInSettings` insert | entity operation rights; CrtBase ships NO operation-right binding for this table, so its effective rights were not traced | [traced `ls PS CrtBase/Data` (Dcm entries: `SysAdminOperation_CanManageDcm`, `SysAdminOperationGrantee_CanManageDcm`, `SysEntitySchemaOperationRight_SysDcmSettings`, ...)] | this file; open: M4 |
| `CanManageDcm` itself | defined in CrtBase (`SysAdminOperation_CanManageDcm`, Id `9fccd5ea-52aa-45de-89ce-25c2806e7b2d`, "Access to Case setup section"); defaults grant `CanManageDcm` and `CanManageProcessDesign` only to System administrators | [traced <PackageStore>/DcmDesigner.DBTests SqlScripts/SysAdminOperation_DcmDesigner_DBTests_Tests/*.sql] [corpus grantee data 2/2] | v r6-C3 confirmed |
| `CanManageProcessDesign` | not required by the platform for cases | [traced DcmSchemaManager.cs:132-139] | v r6-C3 confirmed |
| 7.x client gates | Cases wizard step and "open case page" ask `CanManageDcm`; the app wizard asks `CanManageSolution` + `CanManageDcm` | [traced DD DcmUtilities.js:336-344; DcmWizardStep.js:14-19; CrtWizards7x ApplicationStructureItemWizard.js:548-550] | - |
| `VwSysDcmLib` | All employees read-only on the view; `SysEntityRightByAdminOperation_CanManageDcm` grants record rights on DCM library objects by the operation | [traced DD Data/SysEntitySchemaOperationRight_VwSysDcmLib_Fixed; Data/SysEntityRightByAdminOperation_CanManageDcm] | - |
| CrtProcessBuilder saves with the CALLER's `UserConnection`, so a missing right fails late inside the save unless gated up front | [traced PB Design/ProcessDesignGuard.cs:37-44; PB Schema/ProcessSchemaRepository.cs:337,345] | v r6-C3 confirmed |

Decision pointer: Q5 (up-front gate); M4 (non-admin user).

---

## 10. .NET Framework vs .NET 8

| Concern | Fact | Evidence | Chk |
|---|---|---|---|
| DCM code | no `#if` in `Terrasoft.Core/DcmProcess`; DCM services bound on both hosts | [traced grep; Terrasoft.WebApp/Global.asax.cs:774; Terrasoft.WebHost/ApplicationBindings.cs:324] | v r1-C1 confirmed |
| Save / property / provider path | two `#if NETFRAMEWORK` spots, neither affects a non-null `Enabled = true` (default for a null extra-property value; per-request manager cache) | [traced SchemaManager.cs:3788-3794; SchemaManagerProvider.cs:279-318] | g1 (d) |
| `IPackageSchemaDataCreator` | ClassFactory-bound on both | [traced Global.asax.cs:748; ApplicationBindings.cs:295] | v r4-C7 partly |
| Designer save route | `DcmDataContract.cs:6-8` `#if` touches only ServiceStack attributes | [traced] | g2 §1 |
| Route prefix | `0/` before `rest/...` and `Nui/...` on .NET Framework only | [traced CLIO/clio/Common/ServiceUrlBuilder.cs:59-60,165-268] | v r7-C1 partly |
| Measurement | parity is code-only: no reachable .NET 8 stand (all 8 `isNetCore` registrations refuse TCP on their Kestrel port); the ProcessDesigner TeamCity lane deploys .NET Framework Studio only | [measured `describe-environment` per registration, g5 §1-2] | g5 (supersedes r10 §7) |
| Package unit tests | CrtProcessBuilder.Tests builds and runs as net8.0 under `-c dev-n8` locally; CI is net472 by provisioning | [traced pb CrtProcessBuilder.Tests.csproj:10-15,56; env.dev-n8.props:11] | v r8-C6 refuted the "net472 only" claim |

Decision pointer: Q24 (.NET 8 gates "done"); M8.

---

## 11. Stand `Creatio` and corpus numbers (quick reference)

| Fact | Evidence | Chk |
|---|---|---|
| .NET Framework 4.8.1, MSSql, local core build at a95349820, prefix `Usr`, cliogate absent, culture en-US, user Supervisor | [measured `describe-environment`, `get-schema-name-prefix`] | v r10-C1 confirmed |
| CrtProcessBuilder 1.6.6.142 = an unmerged feature build (.NET 8 restart reporting), not main (main = 1.6.6.140); DcmDesigner, DcmLibrary, CrtGenAI 7.8.0 | [measured `list-packages`; git ls-remote] | v r10-C2 confirmed |
| Exactly one DCM: `OmniChatCase` (OmnichannelMessaging, entity `OmniChat`, column `Status` -> `OmnichannelChatStatus`, 6 values, no order column), `IsActive` and `IsActiveVersion` true | [measured `execute-esq SysSchema` / `VwSysDcmLib`, `get-entity-schema-properties`] | v r10-C5; g1 (a) |
| No clean sample object on the stand; candidates `Activity.Status`, `Webhook.Status` (4), `SysDataSegment.SysUserStatus` (3), `Call.Direction` (3), all product-owned | [measured `execute-esq`] | v r10-C9 partly |
| Stage lookups: 6/11 product stage lookups have no extra column; the rest use `StageNumber` / `StageOrder` / `StageInnerOrder`, `Number`, `Position`; `BaseLookup` has no order column | [traced PS BaseLookup/metadata.json:17-18] [corpus 6/11, 5/11] | v r9-C4 confirmed, v r10-C10 partly |
| Corpus DCMs: 61 (14 product schemas, 47 test); 42/61 have only empty stages; 29 have a `Filters` key; 0 use `Conditions`, non-empty `Transitions` or `SystemElements` (the test package `ProcessTests/TestDcmSectionCase` is the `SystemElements` outlier) | [corpus 61] | v r5-C2 partly; g2 §3 |
| Product stage count 3-7; largest test DCM 32 stages | [corpus] | - |

---

## 12. Unknown or unmeasured (where each is settled)

| Unknown | Settled by |
|---|---|
| A route-A DCM opens in `vm=DcmDesigner#case/<uid>` with the expected stages and no error; a designer re-save keeps one binding per row | M1 (manual, owner) |
| `DcmSchemaManagerRequest` right after the save returns `IsActive && IsActiveVersion` | M2 / E2E case 2 |
| Bar renders after F5; click persists; field change moves the bar | M3 (manual), CAADT |
| Non-admin user with `CanManageDcm` + `CanManageSolution` + settings-table rights; effective rights on `SysDcmSchemaInSettings` | M4 (Q5) |
| What `RemoveItemByUId` leaves (`SysEntityPrcStartEvent` row, flow schema) | M5 (Q7) |
| Where and under whose rights a `ClassFactory` binding from a CrtProcessBuilder WCF call lands | M6 |
| New record starts a case; existing record starts on stage change; after the final stage a qualifying update starts a new case | M7 |
| Every .NET 8 behaviour; web-farm propagation of a new DCM | M8 (Q24); no farm available |
| `AutoAddPackageDependenciesInProcesses` value on the stands | M9 (Q16) |
| Whether a package-dependency add marks anything for compile | M10 |
| `DisableEntityStageProgressBarPreprocessing` state on the stands; `SysSchemaUserProperty` contents (DataService ESQ cannot read the table: `ItemNotFoundException`) | not measured; guidance troubleshooting line |
| Route B in-process save and its exact exceptions for untyped connections | traced only (g2); decided by the steps task (Q6, Q27) |
| Culture-aware .NET sort versus database collation for the display-value order | Q9; the result states the order used |
| Whether `DesignerSchemaService.create` adds anything (for example `layoutConfig`) to the wizard's saved diff | not observed in a saved wizard page (r3 open points) |

---

## 13. Corrections to the research

| Note claim | Correct fact | Source |
|---|---|---|
| r1-C4 point 3: the Freedom UI bar finds the stage column through `DcmService.GetDcmStageColumnName` (SysDcmSettings) | The bar uses the first active, active-version DCM from `DcmSchemaManagerRequest`; `GetDcmStageColumnName` feeds only the action dashboard and the playbook panel (the bar's call was dropped in creatio-ui commits 80d8628b4c0, 5ee37abece0) | `_verdicts.md` conflict 1; v r3-C1, r4-C6, r9-C7 |
| r4 §2.5: "a DCM without `SysDcmSettings.SysSchemaUId` cannot be discovered by Freedom UI" | Not for the bar; `SysSchemaUId` is not required (25/44 rows use `SysModuleEntity`) | v r4-C6 partly |
| r4 §2.5 counts (19 / 27 / 2 of 44) | 17/44 entity-bound, 25/44 `SysModuleEntity`; pure product shape 9 rows / 7 packages | v r4-C6, v r5-C8 |
| r1 §2.2 / GeneratedDcmSaver: product colour `#54AA4D` | The designer saves `#0C8701`; `#54AA4D` / `#8ECB60` render as `#0C8701`; null crashes the bar | `_verdicts.md` conflict 6; v r9-C5 |
| r10 §4.2 / OmniChatCase as a template | Not a template: 1 connection, alternative stages, permissions | v r10-C6 partly; conflict 7 |
| r1-C2: `ActualizeSchemaInfo` runs inside the save transaction | Its writes go through the system connection and separate storages and are not rolled back | v r1-C2 partly |
| r3 §1.4 / component recipe: hand-wiring "breaks rendering" | Hand-wiring has no effect (overwritten) unless the kill switch is on; "breaks" is untraced for the bar | v r3-C2 partly |
| r1, r4: `UseSchemaManagerRemoteInvalidation` propagates a new DCM | Only the schema-manager PubSub sync does | g1 (d) |
| r9-C1 / r2-C13: the tool must make the DCM enabled (`SetEnabled` internal) | A new root DCM saved with the default `Enabled = true` is enabled and active at once; do not call `SetEnabled` | g1 |
| r1 §3.2: `DesignedDcmSchema.txt` is the save dialect | It is the READ output; core converts it before reading | g2 |
| r8 digest: unit tests run only on .NET Framework; NSubstitute cannot stub `InternalSaveSchema` | Both refuted: `-c dev-n8` runs net8.0; `NSubstituteExtension.Protected(...).Returns(...)` works | v r8-C6, v r8-C7 refuted |
| Design B: `GetIsActiveVersion(item)` is public | Only the `Guid` overload is public | judge |
| clio AGENTS.md: a guidance change needs `libraryVersion` + `sequence` bump | The sequence is derived; only `libraryVersion` moves | `_verdicts.md` conflict 5 |
| KnownRoute 122 is next | Claimed by two remote clio branches; take the next free value after rebase | `_verdicts.md` conflict 4 |

Wrong line references in the notes (do not cite): `DcmStageTransitionUserTask.cs:296-301` (file has 84 lines);
`DcmProcessElementFactory.cs:303-343`; `DefaultDcmSchemaElementTransition.cs:224-227`; `PackageSchemaDataCreator.cs:185-258`
(file has 196 lines, method :171-187); `BaseProcessSchemaManager.cs:936-940` as the DCM save path (the DCM path is
`DcmSchemaManager.cs:86-90`); `CommandExecutionResult` compile marker is at :50; `DcmSchemaElement` is declared at :25
(`_verdicts.md` conflict 10).
