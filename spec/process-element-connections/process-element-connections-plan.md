# "Connected to" (activity connections) for CrtProcessBuilder — analysis and implementation plan

Status: analysis complete; every research fact closed. **Every decision that gates the first delivery is
taken** — D1, D1a, D2, D3, D6, D7, D8, D9, D10, D11. D4, D5, D12 remain and do **not** gate it (Phase 3, or
separate surfaces). D8 was taken during story 4 — see the ADR; §10 below is struck through accordingly.

**This document is the analysis of record. The decisions and their rejected alternatives now live in
[adr-process-element-connections.md](../adr/adr-process-element-connections.md), and the work is sliced into
`spec/stories/story-process-element-connections-{1..5}.md`, tracked in `spec/sprint-status.yaml`.** Read the
ADR before implementing; read this document when you need the mechanism, the traps (§6), or the evidence
behind a decision. §10 below is retained as the decision index — the ADR is authoritative for rationale.

One decision was taken after the rewrite and is recorded only in the ADR: **D1a** — `setConnections` is an
**upsert keyed on `column`**, not a collection replace, so changing one connection never silently clears its
siblings. Changing an existing binding is the same call with a new source.
Date: 2026-08-10
Repos: `CrtProcessBuilder` (server logic) + `clio` (MCP surface)
Ground truth: five designer-authored revisions of `UsrProcess_5ae7551` on `krestov-test`
(schema UId `277886f6-4c52-45af-8171-70d88829917d`), plus a census of all 1098 packages in
`C:/Projects/PackageStore`.

---

## 1. What the feature is

`Connected to` (resource `ActivityLinksCaption`; en *Connected to*, ru *Связи активности*; relabelled
*Email connections* in `EmailTemplateUserTaskPropertiesPage`) is the process-designer editor for the
**lookup connection columns of the Activity record that the element creates**.

> "Connect the task to other Creatio entities in the **Connected to** parameter. […] Creatio will
> display the task on the **Activities** detail of the connected record. By default, the element setup
> area displays account and contact connections."
> — [Perform task process element](https://academy.creatio.com/docs/8.x/no-code-customization/bpm-tools/process-elements-reference/user-actions/task-process-element)

Why it is functional, not decorative:

| Consequence | Set | Not set |
|---|---|---|
| Task/email on the counterparty's **Activities** detail | yes | no — lives only in Activities and the performer's list |
| Activity page fields | pre-filled | "will remain empty. You will be able to fill it out manually" |
| Connected record's **Timeline** | appears | absent |
| Email counts as **processed** | needs Account **or** Contact **and** ≥1 further connection | stays unprocessed |
| Cascade fill (Opportunity → Account+Contact; Lead → Account/Contact; Project → Contact/Account/Opportunity) | yes | no derived context |

Not documented — do not claim: any effect on access rights or record visibility.

Three mechanisms that must not be conflated: **`Connected to`** links the created *Activity* to business
records (this feature); **`Connected object` + `Record of connected object`** links the *process log*
entry; the **`Link process to object`** element links the *process instance*. The **Approval** element
has no `Connected to` at all.

---

## 2. How it works

### 2.1 The principle, as set algebra

Three source sets, four stages. Naming them separately is the whole point.

- **R** — registry rows for Activity. `ProcessSchemaUserTaskUtilities.js:274-286`: root schema
  `EntityConnection`, columns `ColumnUId` + `Position`, single filter
  `SysEntitySchemaUId = Activity.uId`. **No workspace filter, no `ColumnUId IN`** — unlike the
  record-page path.
- **C** — columns of the **compiled** client `Activity` schema, regenerated from the *installed* object.
- **P** — the element's parameter contract (`P_static ∪ P_dynamic`).

```
Stage 0  server, on contract-cache MISS only (BaseProcessUserTaskSchemaManagerService.cs:96-104 → :50)
   P := P_static ∪ { dyn(c) : c ∈ resolve(R), name(c) ∉ names(P), uId(c) ∉ uIds(P) }
Stage 1  client, loadEntityConnectionColumns:324-349
   E := dedup_by_name{ c ∈ C : ∃ r ∈ R . c.uId = r.ColumnUId }      // resolve-or-skip, SILENT
   E := sort(E, position ASC, then caption localeCompare)
   E := E ++ [C.Project]  iff C.Project exists ∧ ∉ E                 // appended → lands LAST
Stage 2  onInitActivityConnection:379-397 — iterates E, never P
   V := { (e, π(e)) : π(e) ≠ null },  π(e) = P.byName(e.name) ?? P.byUId(e.id) ?? newDynamic(e)
```

> **Connected to = (registry rows for Activity) ⋈ (columns that actually exist on the compiled Activity
> object), plus `Project` iff that column exists. The user task's parameter list is never a source —
> only a lookup target.**

The registry is the *authorisation* list; the Activity object is the *existence* list; you see the
intersection; the parameter is derived from it.

**Verified against the stand.** `|R| = 5`; `6b279be6-…` resolves to no column and is dropped (leaving one
`_log.InfoFormat` at `ProcessUserTaskUtilities.cs:563` as the only trace anywhere); the other four tie on
`Position = 0` and fall through to caption order → **Account, Chat, Contact, Test Approval Element**;
`C.Project` is undefined, the guard at `:308-309` fails, `Project` is absent. Exactly what the designer
shows.

`Position` is an `Integer` with no `RequirementType` (`EntityConnectionSchema.CrtUIv2.cs:145-153`), i.e.
materialised `NOT NULL DEFAULT 0` — 15 of 44 shipped Activity rows omit it from the descriptor and still
land as 0. That matters: `_sortEntityConnections:291-302` returns `position1 - position2`, which with
NULL would be `NaN` and an undefined sort. Only four client call sites read `Position`; every server
consumer and the Freedom UI service select `ColumnUId` only. For Activity it is effectively inert; it is
meaningful only for `Call`.

Captions come from the **host column**, not the referenced entity
(`EntityConnectionLinksUtilities.js:79`, `ProcessSchemaUserTaskUtilities.js:340`,
`BaseProcessUserTaskUtilities.cs:86`). The wizard merely copies the section caption at creation time, so
renaming the section later does not move it. Icons are keyed on **column name**
(`<ColumnName>ExistIcon`), so custom columns always get `DefaultIcon`.

### 2.2 The registry is product-wide, and shipped as package data

`EntityConnection` is `(SysEntitySchemaUId, ColumnUId, Position)` and nothing else — no caption, no name,
no reference schema. Census of all 1098 packages: **56 bound-data folders, 68 rows, 65 distinct entries**,
targeting **six** root schemas — Activity (`c449d832-…`, 44 rows / 41 distinct), Call (`2f81fa05-…`),
Document, Invoice, Order, Bonus. Three `ColumnUId`s resolve to no column anywhere in the store, and two of
the stand's five rows are outside the census entirely (one wizard-created, one dangling). **Bound data is a
lower bound on the registry, not a model of it.**

It has ~10 consumers across classic UI, Freedom UI and server; the process designer is one, and the only
one that hard-codes `X = Activity`. The record page's `EntityConnectionsDetailV2` reads the same table
with the same key, so the designer's list and the record page's list are the same set by construction.

### 2.3 What gets persisted — two populations

| | (ii) static / inherited | (i) dynamic |
|---|---|---|
| example | `Account`, `Contact`, `Lead`, … | `UsrUsrTestApprovalElement`, `OmniChat` |
| `TypeName` | base `ProcessSchemaParameter` | base `ProcessSchemaParameter` (**never** the Dynamic subclass — 3208/3208 in the census) |
| `CreatedInSchemaUId` | **user-task** schema | **process** schema ⇒ `IsDynamic` |
| `Tag` | `EntityColumnValue` | `<HostEntity>Connection`, e.g. `ActivityConnection` |
| `UId` | fresh GUID | fresh GUID |
| the Activity **column UId** lives in | — | the element's `ProcessSchemaMapping.SourceParameterUId` |
| created by | the **platform**, automatically (§2.4) | the designer, on save |

`IsDynamic` is a **predicate**, not a type test:
`ProcessSchemaParameter.cs:271-278` — `CreatedInSchemaUId == BaseProcessSchema.UId`. For element
parameters it is the exact complement of `IsInherited`.

The element parameter's `UId` is **disposable**. Proven by an accidental A/B: the reporter swapped the
element's user task from `ActivityUserTask` to `EmailTemplateUserTask` keeping the same element UId; the
two dynamic connections survived (`GetRemovedSchemaParameters:253-271` exempts `IsDynamic`) but were
re-stamped — `592bbcd1→c0604e18`, `fc36f968→10e2e0bc` — while the mapping's `SourceParameterUId` stayed
`21da1fdf…` / `e4eae837…`, exactly the two `EntityConnection.ColumnUId` rows on the stand. **Name is the
identity; the column UId lives in the mapping.**

### 2.4 The static set is created by the platform, for free

`ProcessSchemaUserTask.SchemaUId` setter (`:106-115`) → `ProcessSchemaActivity.SynchronizeParameters`
(`:586-597`) → `FillNewSchemaParameters` (`:291-306`): one element parameter per user-task-schema
parameter, reused **by name**, each with `Guid.NewGuid()` (`:311`), `ContainerUId = element.UId`, plus a
`ProcessSchemaMapping`.

`UserTaskElementHandler.Create:71-73` assigns `SchemaUId`, so a CrtProcessBuilder-built Perform task
**already carries** `Account`, `Contact`, `Lead`, … as element parameters. **Confirmed by reading, not
assumed:** `ProcessSchemaRepository.CreateSchema` passes a real `Guid.NewGuid()` and the handler sets the
element `UId` *before* `SchemaUId`, so `GetCanSynchronizeParameters` (`ProcessSchemaActivity.cs:324-326`)
is satisfied inside the build transaction. The planned unit test is a **regression pin**, not a
measurement — and it should pin the ordering, because the 2-arg constructor path would break it.

Side effect to watch: `PlaceNewElement` assigns `element.Name` *after* `Create` returns, so those
auto-created mappings carry `Name = null` (§9 item 8).

### 2.5 The value contract — one rule, five samples

**A connection value is always `Source = 3` (Script) plus a macro.** Only the dialect differs. The
platform *has* first-class `SystemValue = 4` / `SystemSetting = 5` sources
(`ProcessSchemaGeneratorNew.cs:364-375`, `ProcessPropertyGenerator.cs:163-164`) and the designer **does
not use them for connections** — a sample binding both connections to system variables still wrote
`Source = 3`.

The macro vocabulary is a closed set of regexes, `Terrasoft.Core/GeneratorUtilities.cs:50-69`:

| Macro | Binds to | Observed on a connection | Requested via |
|---|---|---|---|
| `[#Lookup.<schemaUId>.<recordId>#]` (`:57`) | a fixed record | ✅ | `expression` |
| `[#…[Element:{…}].[Parameter:{…}]#]` (`:53`) | an element's output | ✅ | `sourceElement` + `sourceElementParameter` |
| `[#SysVariable.<Name>#]` (`:58`) | a system variable | ✅ | `expression` |
| `[#…[Parameter:{…}]#]` | a process parameter | — | `processParameter` |
| `[#SysSettings.<Code>#]`, `<Code><Type>` (`:59`) | a system setting | — | `expression` |
| `[#ColumnValue.<schemaUId>.<path>#]` (`:69`) | a column of an entity | — | `expression` |
| `[#DateValue/DateTimeValue/TimeValue/BooleanValue.…#]` (`:60-64`) | typed constants | — | `expression` |

Alongside the value, `SourceValue.ModifiedInSchemaUId` is stamped with the **process** schema UId — this
is load-bearing, see T-2.

### 2.6 Runtime — two channels, plus reverse sync

`UserTaskActivityHandler.SetColumnValuesFromParameters:104-117`:

- **Channel A (static).** `GetActivitySchemaColumns:79-87` takes user-task-schema parameters tagged
  `EntityColumnValue`, intersects with Activity columns **by name**, then
  `userTask.GetPropertyValue(column.Name)` — i.e. it needs the *generated property*. No null guard
  (`ReflectionUtilities.cs:302-305`) ⇒ `InvalidObjectStateException` on a mismatch.
- **Channel B (dynamic).** `BaseProcessUserTaskUtilities.SetEntityColumnValues:124-138` iterates
  `GetDynamicParameters()` and resolves the column as
  `columns.FindByUId(parameter.UId) ?? columns.FindByName(parameter.Name)` (`:34-42`). In shipped data the
  **name** fallback fires ~95 % of the time. Empty lookup/date values are skipped (`:132-134`).

**Neither channel reads `EntityConnection`.** For the process engine the registry is design-time only,
corroborated by `ManualEmailUserTaskSender.cs:107-146`, which fills connections from a hard-coded name
array.

**Reverse sync is on by default** — `UserTaskActivitySyncOptions.SynchronizeParameterValues = true` — so
on completion `SetDynamicParameterValues:145-158` pushes Activity values back into the element's
parameters. A connection is therefore also an *output*. That path dereferences
`parameter.DataValueType.ValueType` unguarded (`:153`), so an unresolvable `DataValueTypeUId` throws **at
task completion**, not at build (§9 item 12).

---

## 3. Universality map

### 3.1 Two client hosts, one mixin

| Properties page | `getIsActivityModuleEnabled` | Live block |
|---|---|---|
| `ActivityUserTaskPropertiesPage` (Perform task) | inherited `true` | the **module** `ProcessUserTaskActivityEditSchema:562-616` |
| `CallUserTaskPropertiesPage:179-181` | `false` | in-page (`BaseActivityUserTaskPropertiesPage:171-224`) |
| `EmailUserTaskPropertiesPage:104-106` | `false` | in-page |
| `EmailTemplateUserTaskPropertiesPage:606-608` | `false` | in-page, relabelled `EmailLinksCaption` |

For Perform task the in-page block is **dead** — `getIsActivityTaskVisible` is false and
`initParameters` passes `shouldInitEntityConnections = false`. Non-`BaseActivity*` tasks get the module
only under `UseOptionalProcessUserTaskActivities` **&&** `CreateActivity`
(`BaseUserTaskPropertiesPage.js:118-128`).

### 3.2 Static connection parameters per user task

| User task | `Group = "Connected to"` | `Tag = EntityColumnValue` | notes |
|---|---|---|---|
| `ActivityUserTask` | 19 | **22** | the 3 extras — `ActivityCategory`, `OwnerId`, `ShowInScheduler` — are **not** connections |
| `EmailTemplateUserTask` | 17 | 19 | live; the 17 verified against a real designed element |
| `UserQuestionUserTask` | 17 | 19 | |
| `OpenEditPageUserTask` | 17 | 19 | |
| ~~`EmailUserTask`~~ ("Write email") | 17 | **0** | platform-obsolete (§3.3) |
| ~~`CallUserTask`~~ | 16 | **0** | deprecated (§3.3); also never written at runtime |
| `AutoGeneratedPageUserTask` | **0** | 1 (`ShowInScheduler`) | parameter must be **created** |
| `PreconfiguredPageUserTask` | **0** | 1 | parameter must be **created** |

`Group` is a `LocalizableString` that is **never written to metadata** — it exists only as a generated
`[DesignModeProperty(… Group = "Connected to" …)]`. A metadata-driven backend cannot see the 19-vs-22
split and must intersect against `Activity.Columns` instead.

### 3.3 Deprecation is data-driven — with one exception

The palette is built **client-side** from `SysSchema ∩ SysProcessUserTask` (`process-usertask-schema-manager.js:25-30`
adds an exists-filter on `SysProcessUserTask`; the base query adds workspace + manager name), then
filtered by `process-schema-designer-left-toolbar.js:77`:

```js
return !item.isDeprecated && item.group && item.usageType !== Terrasoft.ProcessSchemaUsageType.NONE;
```

`getIsElementObsolete:100-110` has three signals and the feature flag **inverts** them
(`ProcessObsoletedElements` enabled ⇒ nothing is obsolete, i.e. the flag *reveals* retired elements):

```js
if (Terrasoft.Features.getIsEnabled(this.obsoleteElementsFeatureCode)) { return false; }
return this.obsoleteElementNames.indexOf(element.name || element) > -1 ||
    element.usageType === Terrasoft.ProcessSchemaUsageType.NONE ||
    element.isDeprecated;
```

**`UsageType` is readable server-side** — `ProcessUserTaskSchema.cs:83` `UsageTypePropertyName = "FK2"`,
enum `None=0, General=1, Advanced=2` (`BaseProcessUserTaskSchema.cs:9-13`). Measured across metadata:

| Schema | `FK2` | Meaning |
|---|---|---|
| `CallUserTask` | `0` | **`None` — deprecated, and detectable** |
| `SendEmailUserTask` | *absent* | defaults to `Advanced` — **not** caught by `UsageType` |
| `EmailUserTask`, `EmailTemplateUserTask`, `ActivityUserTask` | `1` | `General` |

So the single correct predicate — **adopted as D9** — is
**`usageType == None || name ∈ {SendEmailUserTask, EmailUserTask}`**, and the hard-coded name list is
required, not lazy, because `SendEmailUserTask` carries no marker. Two facts make the form complete and
the implementation safe: the client's third signal `element.isDeprecated` has **no server counterpart**
(no `IsDeprecated` exists anywhere in `Terrasoft.Core/Process/`) and is anyway the *output* of
`getIsElementObsolete`; and `UsageType` must be read from an instance that actually carries it, so resolve
with a metadata fallback — `FindInstanceByUId(uid) ?? FindInstanceFromMetaData(uid)`, the shipped pattern
at `ProcessSchemaElementManager.cs:562-563` — because a compiled instance sheds metadata (that is how
`Tag` was lost) and the predicate would otherwise return `Advanced` for everything. That literal is repeated in ≥4 places
(`ProcessSchemaElementManager.cs:544`, `process-flow-element-schema-manager.js:220-222`,
`process-constants.js:419,448`) and is always the same two names.

`ProcessSchemaElementManager.AddConfigurationUserTasks:542-579` unconditionally skips
`EmailTemplateUserTask` at `:567` — **inert for the modern designer**: that manager feeds only two legacy
WebForms pop-ups and `PackageStorageDiagramUtilities`. `EmailTemplateUserTask` is registered by its own
`CrtProcessDesigner/Data/SysProcessUserTask_Email/data.json` row (`Caption "Email"`, `Position 8`).

### 3.4 "Send email" is ambiguous — the trap the brief walks into

| Name | UId | Connections | Creates Activity |
|---|---|---|---|
| `EmailTemplateUserTask` | `184dbb27-ce13-4d37-8dff-c2ff1df9cf19` | 17 | yes, gated by `CreateActivity` |
| `SendEmailUserTask` | `b749e6e7-cde4-4a2e-ade0-0b8cf36b0926` | **0** | **no**, and no `.cs` at all |

Both ship with `Caption = "Send email"`. `UserTaskCatalog.GetUserTasks()` returns `Name`, so the package
can disambiguate — but every tool `[Description]`, every prose line and every test must name the
**schema**. "Universal" here means *reject `SendEmailUserTask` with a clear reason*.

### 3.5 Other exclusions and preconditions

- **`CallUserTask`** — `CallUserTask.cs:239-240` does `new Activity(UserConnection)` itself, never routing
  through `CreateUserTaskActivity`, and `SetEntityColumnValues` appears nowhere in the file. A connection
  on a Call element is persisted, displayed, and **silently dropped at runtime**. Deprecated *and*
  mechanically unsupportable.
- **`ReadDataUserTask`** — a `ProcessSchemaUserTask` and a first-class package element type
  (`UserTaskElementHandler.SupportedTypes`), with no Activity. A capability probe of
  `element is ProcessSchemaUserTask` accepts it and produces a compiling, inert process.
- **`CreateActivity`** — every connection-capable task **except `ActivityUserTask`** has it;
  `AutoEmailUserTaskSender.cs:112` gates activity creation on it. **Observed live:** the reporter's Send
  email artifact had all 19 connections present with `CreateActivity = false` (the schema *default*,
  `ModifiedInSchemaUId` = user-task schema) and `SendEmailType = 0` (automatic) — a perfectly valid,
  perfectly inert process, produced by the designer's own defaults.
- **`OpenEditPageUserTask`** suppresses connections when the edited page's entity is `Activity`
  (`OpenEditPageUserTaskPropertiesPage.js:358-360`).
- **`EmailTemplateUserTaskPropertiesPage.clearActivityConnection():866-875`** wipes every connection on a
  send-mode switch, so a later human edit can erase what the backend wrote.

### 3.6 Obsolete elements: readable, not designable

Obsolete elements still live in old processes, so we must read and understand them; we must not design
new processes with them. The gate is therefore **asymmetric**:

| Path | Obsolete element | Behaviour |
|---|---|---|
| `describe` | any | **never gated** — report it, its user-task name and its connections, and label it |
| `build` / `addElement` | obsolete user task | **refuse**, reason = *policy* |
| other `modify` ops on an already-obsolete element | — | **allow** — maintaining legacy is not designing legacy |
| `setConnections` on `CallUserTask` | ever | **refuse**, reason = *mechanism* (its runtime writes nothing) |

Keep the two reasons distinct in the message: "deprecated" is policy a maintainer may legitimately want
to override on an old process; "the runtime never writes this" is a fact no context makes acceptable.

**The hole this closes — describe→build laundering.** `UserTaskElementHandler.Describe:86-90` emits
`UserTaskName`, `Create:66-77` resolves the user task by that same name, and `CanBuild:83` accepts any
`ProcessSchemaUserTask`. So describing a legacy process containing `EmailUserTask` yields a
round-trippable descriptor that `build` recreates faithfully, with no warning anywhere. The refusal must
live on the **build** side; making `describe` lossy would break the legacy comprehension the requirement
protects.

For legacy comprehension `describe` should expose `deprecated` (computable from §3.3's predicate —
the same read, so it is nearly free) and `writesConnectionsAtRuntime: false` where it applies
(`CallUserTask`; the inert `CreateActivity = false` case). Connections on `CallUserTask`/`EmailUserTask`
are findable **only** by name-intersection with `Activity.Columns` — they carry 16/17 group parameters and
**zero** `EntityColumnValue` tags, so neither the tag nor `IsDynamic` locates them.

### 3.7 Modelling a call

The retired Call element is replaced by **Perform task with the corresponding `ActivityCategory`** —
consistent with the dead element's own default (`CallUserTask.ProcessUserTask` resources declare
`Parameters.ActivityCategory.DisplayValue = "[#Lookup.Activity category.Call#]"`). Good news: that puts
calls on the most connection-capable element (19 static connections, both channels, and uniquely **no
`CreateActivity` gate**).

Three things to get right:

1. **`ActivityCategory` is name-ambiguous.** `krestov-test` has two rows named `Call`:
   `e52bd583-…` (ActivityType = Call) and `03df85bf-…` (ActivityType = **Task**). The Perform task page
   offers only `ActivityType == Task` (`ActivityUserTaskPropertiesPage.js:70-72`), and
   `ActivityUserTask.CreateActivity` always writes `TypeId = ActivityConsts.TaskTypeUId`. The correct one
   is **`03df85bf-…`**; resolving by name is a coin flip whose wrong branch a human designer cannot
   produce. Default when unset: `f51c4643-…` (`To do`).
2. **On `ActivityUserTask` write it as `ConstValue`.** The `Source == ConstValue` gate exists **only** at
   `ActivityUserTask.cs:192-195`; otherwise the allowed-results list silently falls back to `To do`. This
   is *not* platform-wide: `EmailTemplateUserTask.GetResultParameterAllValues:349-361` derives results by
   joining `ActivityCategoryResultEntry → ActivityCategory → ActivityType` and never reads `SourceValue` —
   it ships its category as `Source = 3` + `[#Lookup.961e2086-….8038a396-…#]`. The correct encoding is
   **per user task**.
3. **`ActivityCategory` is tagged `EntityColumnValue` but is not a connection** (with `OwnerId`,
   `ShowInScheduler`). Now that callers will set it actively, a tag-based filter would both misreport it
   in `describe` and collide with a legitimate write.

---

## 4. Making a section connectable

Two independent preconditions, failing differently:

| Precondition | If missing |
|---|---|
| the `Activity` lookup column **exists** | nothing works at any layer — hard requirement |
| an `EntityConnection` row registers it | the value is still **written at runtime**, but the connection is invisible and inert everywhere else |

The registry is design-time for the process engine (§2.6) but **is** read at runtime by other features:
email-chain relation actualisation (`EmailMessageHelper.cs:676-688`), the Next Steps widget
(`ActivityNextStepQueryExecutor:39-53`), email auto-relation rules (`RuleRelationModel.cs:98-130`,
`AutoEmailRelation.cs:159-201`), Freedom UI email creation, quick-add defaults
(`QuickAddMixin.js:109-137`). So an unregistered-but-written column is a **half-citizen**: filled at
runtime, invisible in the designer's *Connected to* (Stage 2 iterates `E`, so the parameter exists but no
human can see or edit it), absent from the record page's detail, ignored by all of the above.

**Rule for the package: check both preconditions before writing. Never silently write an invisible
connection** — refuse, or write and return an explicit warning naming the missing registration.

### 4.1 The reference procedure

The only real admin write path is the Section Wizard → *Cases* (DCM) step
(`DcmLibrary/SectionWizardCasesSettings.js`):

```
:647-680  prepareActivity                 initialise EntityConnectionManager, load both schemas
:461-475  findActivityConnectionColumn    lookup with referenceSchemaUId == section.uId?
:424-452  createActivityConnectionColumn  name = getSchemaNamePrefix() + entitySchema.getName(),
                                          caption = entitySchema.caption.clone(), LOOKUP, isIndexed: true
:533-542  findActivityConnection          already registered?              (idempotency)
:551-562  addActivityConnection           EntityConnectionManager.createItem/addItem
                                          → Terrasoft.ProcessUserTaskSchemaManager.reset(...)
SectionWizard.js:840-843 → object-manager.js:578-590 saveAndUpdateSchemaData
                                          DataService insert + bound data EntityConnection_<id-no-dashes>
then compile
```

Corroboration from the stand: step 3 produces `Usr` + `UsrTestApprovalElement` =
**`UsrUsrTestApprovalElement`**, matching the measured physical column exactly.

**The name prefix is mandatory, not convention** — `EntitySchema.GetIsPrefixRequired()` returns `true`
unconditionally (`EntitySchema.cs:2281-2283`), enforced at save. `isIndexed = true` is the product
convention; GenAI's unindexed variant is the outlier.

### 4.2 Binding, caches, and what 8.x does

Binding is not optional for delivery: an unbound row does not travel with the package and is never
pruned. `EntityConnectionManager.getPackageSchemaDataName` generates the bound-data name itself
(`EntityConnection_<Id-no-dashes>`) — binding is built into the mechanism, which is why PackageStore is
full of `Data/EntityConnection_<32-hex>/` folders. The descriptor shape is fixed:

```
Schema  EntityConnection      187a8e9a-6f0e-465d-aeb0-9556dfa93b7d
  Id                 (IsKey)  ae0e45ca-c495-4fe7-a39d-3ab7278e1617
  SysEntitySchemaUId          2d2a1d06-fa97-4bb5-b37c-6af8782f7a07
  ColumnUId                   a79438c7-070f-4f50-b9c4-509c94770c82
  Position           (opt.)   c1ab9d0a-ff01-456b-bc0f-d11cd879b870
```

`SysEntitySchemaUId` must be the **root** Activity schema — `c449d832-a4cc-4b01-b9d5-8a12c42a9f89`
(`ConfigurationConstants.Activity.ActivitySchemaUId`, and `GeneratedEntitySaver.cs:35` independently).
The stand has **nine** `SysSchema` rows named `Activity`, one per package that extends it; using your own
layer's UId writes a row the registry query will never return.

**Two caches must be cleared or the change is invisible even after compile**: the server contract cache
(`GetProcessUserTaskSchema` runs only on a `GetContractMetaDataFromCache` miss,
`BaseProcessUserTaskSchemaManagerService.cs:96-104`) and the client ESQ cache keyed
`activityEntitySchema.uId + "_" + this.name` (`:277-279`). `ProcessUserTaskSchemaManager.reset` clears
both. **A hand SQL `INSERT` clears neither** — the practical failure mode of the unofficial recipe.

**8.x App Hub writes neither the column nor the row.** Section creation is app-template-package expansion
(`ApplicationSectionEventListener` → `AppSectionManager.Create` → `SectionCreator:419-435`); the four
shipped templates contain zero `EntityConn`/`Activity` entries, and
`rg EntityConnection Terrasoft.Core.Applications` returns nothing. The **only** 8.x writer is
`GeneratedEntitySaver.cs:255-266`, behind `GenAIFeatures.GenerateNextSteps`, which creates the column
(`:239-253`) then the row — but does **not** bind it, and clears
`SessionCache["EntityConnectionColumns"]`, the *runtime* key, not the designer's. The 7.x `SectionWizard`
is still reachable in 8.x as the editor of legacy classic sections, and still writes rows.

**This inverts an earlier assumption**: "column exists but unregistered" is the *rare* case. For a section
created in 8.x, **neither** exists — so "connect my new app's section" needs the full two-step schema
write (§9 item 2, decision D6).

### 4.3 Reachability from configuration code — possible, but deliberately not used (D6)

Both steps *are* reachable from a configuration package; every needed API is `public` in `Terrasoft.Core`:

- **row** — plain Entity API (`GeneratedEntitySaver.cs:255-266` is the shipped reference):
  `EntitySchemaManager.GetInstanceByName("EntityConnection")` → `CreateEntity` → `SetDefColumnValues` →
  `SetColumnValue(Id | SysEntitySchemaUId | ColumnUId)` → `Save(false)`.
- **column** — `EntitySchemaManager.CreateDesignSchema(uc, activityUId, packageUId, true)` →
  `designSchema.Columns.Add(col)` → save → `PackageInstallUtilities.SaveSchemaDBStructure(uids, false)`.
  The `true` creates a **replacing** schema of Activity in your package — the source of the nine layers.
- **binding** — `PackageElementUtilities.SavePackageSchemaData(...)` is `public virtual` (`:1747`, `:1780`).

Two constraints: a **declared dependency** on Activity's package is required, enforced only at
export/install (the auto-dependency applier is `internal` and unreachable from configuration); and a
**same-name column collision between two packages breaks `Activity` for the whole environment** via a
codegen `ValidateException` — not a scoped failure.

**Decision D6 = (i): CrtProcessBuilder does not do this.** The facts above are recorded because they
constrain whoever does (§4.4), not because the package will use them.

### 4.4 Who registers a section — a composition, not a component (D6, D7, D10)

Two changes are conflated in "connect my section to activities", and they differ on every axis that
matters: scope (one `ProcessSchema` vs. the whole environment), reversibility (delete the process vs.
you do not drop columns from `Activity`), and privilege. The process builder's stated purpose is to turn
a declarative description into a saved `ProcessSchema`; mutating the `Activity` data model is not that.
So registration lives **outside** the package — and it turns out almost nothing has to be built,
because clio already ships the pieces:

| Step | Existing capability | Notes |
|---|---|---|
| 1. add the `Activity` lookup column | **`update-entity-schema`** — *"Applies a batch of **add**, modify, and remove column operations to a remote Creatio entity schema"*; args `environment-name`, `package-name`, `schema-name`, `operations`; the operation model carries `ReferenceSchemaName`/`ReferenceSchemaAlias`, so a lookup to the section's entity is expressible. Publishes and rebuilds OData — no compile. | Name **must** carry the package prefix (`EntitySchema.GetIsPrefixRequired()` → `true`, unconditional, enforced at save). `isIndexed: true` is the product convention (`SectionWizardCasesSettings.js:424-437`). |
| 2. declare the dependency | **`add-package-dependency`** on `Activity`'s owning package (`CrtCoreBase`) | Required: the platform enforces it at export/install and cannot auto-apply it from configuration. |
| 3. registry row + binding | **`create-data-binding`** + **`add-data-binding-row`** (local package sources — the delivery-correct artifact, identical to what the 7.x wizard emits), or the DB-first pair **`create-data-binding-db`** + **`upsert-data-binding-row-db`** | Row: `Id` = a **fixed** guid (it is the binding key), `SysEntitySchemaUId` = `c449d832-a4cc-4b01-b9d5-8a12c42a9f89` (root `Activity`), `ColumnUId` = the UId of the column from step 1 (read it with `get-entity-schema-properties` (its structured MCP output carries `u-id` per column; `get-entity-schema-column-properties` returns every OTHER column property and NOT the UId, on any surface)). `Position` may be omitted. |

**The only genuinely missing artifact is cache invalidation.** `ProcessUserTaskSchemaManager.reset`
clears both the server contract cache and the client ESQ cache; without it the designer keeps showing
the old list even after a compile — the practical failure mode of the unofficial "add column → INSERT →
compile" recipe. Nothing in clio does this today. It belongs either in **cliogate** (a privileged
endpoint with `CheckCanManageSolution` as its first line, following the four-step recipe in `AGENTS.md`)
or as a thin clio tool.

Where the *knowledge* lives: in **guidance, as a recipe**, so an agent composes the three tools rather
than guessing. A thin convenience tool that sequences them is acceptable — but it must sit in the
**schema / app-modeling** surface, never in the process-designer surface, and it must be a composition,
not a reimplementation.

Consequences recorded as decisions:

- **D6 = (i)** — the package refuses column creation.
- **D7 = one gate** — the package keeps `CanManageProcessDesign` only. It never mutates the data model,
  so there is no second privilege level and no conditional gate whose contract depends on request content.
- **D10 = resolved** — the package writes no bound data at all; binding is step 3's job, in package
  sources, via the existing tools.

**What the package must do instead — diagnose, precisely.** A refusal is only a defensible architectural
choice if it names the real state. Three states must be distinguishable in the response:

1. no `Activity` column for that entity → "change the data model first", naming step 1;
2. column exists, no registry row → the value *would* be written at runtime but the connection is
   invisible in the designer, absent from the record page's detail, and ignored by Next Steps, email
   auto-relations and quick-add (§4) → name step 3;
3. both present → bind.

One point to verify at implementation time (not now): `update-entity-schema` states that an **inherited**
column can have only its caption/description overridden. We are *adding* a column, which is a different
operation — adding to a base schema from another package means creating a replacing schema. That it does
so cleanly for `Activity` specifically is worth one check.

---

## 5. Scope, reframed

For every task with a static set (Perform task, Send email/template, User dialog, Open edit page), the
element parameter named `Account` already exists (§2.4) and
`ProcessSchemaElementLocator.ResolveElementParameter` matches by name, case-insensitively.

**The canonical request is already expressible with the shipped `addMapping`** — checked link by link
against the reporter's designer artifact:

| Link | Shipped behaviour | Verdict |
|---|---|---|
| target by name | `ResolveElementParameter`; `Contact` present as a static auto-created parameter | ✔ |
| type check | `ParameterTypeCompatibility.cs:106-116` — Lookup target + **Guid** source skips the reference-object constraint (*"a Guid source is exempt"*) | ✔ |
| value shape | `GetMetaPath():1097-1109` + `MetaPathFormat = "[#{0}#]"` (`ProcessDesignConstants.cs:20`) | **byte-identical** to the designer |
| codegen guard | `ProcessMappingService.cs:103` stamps `ModifiedInSchemaUId = schema.UId` — exactly `isOverride` (`ProcessSchemaGeneratorNew.cs:611-617`) | ✔ (T-2) |
| source availability | the platform auto-adds `RecordId` to a signal start (`SignalStartElementHandler.cs:17`; core `ProcessSchemaEvent.RecordIdParameterName`, Guid at `ProcessSchemaStartSignalEvent.cs:218`) | ✔ |

```json
{"op":"addMapping","elementName":"ActivityUserTask1","elementParameter":"Contact",
 "sourceElement":"StartSignal1","sourceElementParameter":"RecordId"}
```

and the "process parameter set from outside" variant is the same op with `processParameter`.

### 5.1 Measured end to end on `krestov-test` (2026-08-10) — the shipped ops already do it

Not inferred. Four steps, each observed:

1. **`BuildProcess`** — `UsrConnProbe1` (startEvent → performtask → endEvent) with one `Guid` process
   parameter `AccountRef` defaulted to a real Account id → `success: true`,
   `schemaUId e38d5af4-b3ca-434a-9251-af332dc48e92`.
2. **`ModifyProcess`** — `{"op":"addMapping","mapping":{"elementName":"Task1","elementParameter":"Account","processParameter":"AccountRef"}}`
   → `appliedOperations: 1`, `success: true`. **This alone proves §2.4 on a package-built element**: the
   target resolved by name, so the auto-created static `Account` connection parameter really is there,
   and the `Guid → Lookup` type check passed.
3. **`DescribeProcess`** read-back of the persisted shape:

   ```
   Account (element parameter of Task1)
     type "Lookup"   referenceSchema "Account"
     source "Script"                                    ← Source = 3, the designer's encoding
     value  "[#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{818185d1-…}]#]"
     uid    1c9dc721-…                                  ← fresh GUID, not the column UId (T-3 honoured)
   ```
   `818185d1-…` is exactly `AccountRef`'s uid — the process-parameter branch of `GetMetaPath`, the same
   envelope as the designer's element-output form minus the `[Element:…]` segment.
4. **Run** — `ProcessEngineService.svc/RunProcess` → `success: true`; the created Activity `CONNPROBE`
   carries **`AccountId = e308b781-…` ("Our company")** and **`ContactId = NULL`**. The null is the
   control: only `Account` was mapped, so the value demonstrably came from the mapping rather than a
   default or a cascade.

Independently, on a designer-authored process (`UsrProcess_5ae7551`, both connections bound to system
variables), a signal-triggered run produced an Activity with `AccountId` = "Our company" and
`ContactId` = "Supervisor", against a prior Activity with both NULL.

**So §9 item 1 is closed: no new write path is needed for a connection whose column is in the user
task's static set.** The remaining scope is the catalog, the guards, describe and guidance.

That reframes what a dedicated contract is *for* — not capability, but enforcement and ergonomics. Three
things, and **D1 accepts all three** (§10):

1. **Ergonomics for the fixed-record dialect** — the caller must hand-craft `[#Lookup.<uid>.<uid>#]` and
   therefore know the *reference schema UId*, which the package already holds as `ReferenceSchemaUId` on
   the target parameter. A `recordId` field synthesising it removes the one piece of platform trivia the
   caller has no business knowing.
2. **Closing the `expression` escape hatch** — `ProcessMappingService.BuildSourceValue:122-126` stores an
   expression **verbatim with no type check**, unlike the parameter-source branches. Today an agent can
   map `[#SysSettings.SomeTextSetting#]` into a Lookup connection; it persists, compiles, and does
   nothing. A typed op can validate the macro *family* against the target column's type.
3. **Per-element validation, the capability gate, and the describe projection** (§6, §7).

Two different "creations" must not be confused, and D6 separates them:

- **Element-parameter creation — IN scope.** Needed for `AutoGeneratedPageUserTask` /
  `PreconfiguredPageUserTask` (0 static parameters) and for registry columns outside a task's static set
  (`OmniChat`, `UsrUsrTestApprovalElement`). This is a `ProcessSchema` mutation — the package's own job —
  and it is bounded: the column already exists, only the parameter is missing.
- **`Activity` column creation — OUT of scope (D6).** The case that dominates new 8.x sections (§4.2)
  is handed to the composition in §4.4; the package refuses it and says which state it is in.

---

## 6. Traps to encode

| # | Trap | Rule |
|---|---|---|
| T-1 | **Duplicate parameter name breaks codegen.** `ProcessSchemaGeneratorNew.cs:3686-3688` emits one property per element parameter; an inherited `Account` (once bound) emits `public override Guid Account`, a new one `public virtual Guid Account`. Duplicate names occur 3× in all of PackageStore and never for a connection — the platform never produces this state. | **Find-or-reuse by name (OrdinalIgnoreCase), then UId; create only on a double miss** — mirroring `ActivityConnectionsEditMixin.js:113-127` and `BaseProcessUserTaskUtilities.cs:183`. |
| T-2 | **Silent no-op on an inherited parameter.** `isOverride = isInherited && ModifiedInSchemaUId == processSchema.UId && Source != None`; otherwise no property, value never compiles in, process runs green, column stays empty. `IsInherited` is cached on first read (`ProcessSchemaBaseElement.cs:117`). | Always stamp `SourceValue.ModifiedInSchemaUId = processSchema.UId`. Never mutate `CreatedInSchemaUId` after the fact. |
| T-3 | **`UId = column.UId` at element level corrupts other elements.** The `SourceValue` setter does a schema-wide, non-element-qualified `Mappings.FindByTargetUId(UId)` (`ProcessSchemaParameter.cs:450-458`). | Use `Guid.NewGuid()`. Identity is **Name**. |
| T-4 | **`Name` is the codegen sink** — property, `"_" + lowerCamel` field, `Create{Name}Parameter` (`ProcessGenerator.cs:317-319`). | Resolve the caller's column against `Activity.Columns.FindByName` and copy **`column.Name`**, never the request string. Reuse `EntitySchemaResolver`. |
| T-5 | **`param is DynamicProcessSchemaParameter` never matches** — 3208/3208 persist as the base type. | Use `GetDynamicParameters()` or the predicate `CreatedInSchemaUId == schema.UId`. |
| T-6 | **`Tag` identifies nothing on the write path.** Derived (`$"{Entity}Connection"`); `Activity` has a column literally named `ActivityConnection`; `EntityColumnValue` covers 22 params of which 3 are not connections. | Identify by *element parameter name ∈ Activity connection columns*, UId secondary. Write the derived tag for designer/dependency fidelity only. |
| T-7 | **Unbound connections persist forever** — 3164 of 3208 shipped ones have `Source = None`; nothing prunes them. | `describe` emits **only** `Source != None` — a pinned test, not a guideline. Do **not** replicate the client's `Account`+`Contact` default; that default is what produced the 3164 empty rows. |
| T-8 | **`clearConnections` cannot mean "delete the parameter."** The designer's delete menu only hides and clears (`ActivityConnectionsEditMixin.js:142-145`); removing a parameter strands `[Parameter:{uid}]` references and trips `InvalidateDependentElements`. | Clear the value only. Note a touched-then-cleared connection persists as `SourceValue: { ModifiedInSchemaUId }` while never-touched ones persist as `{}` — an observation, not a contract. |
| T-9 | **Visibility is unreproducible.** `ActivityConnectionsStore` is a `MemoryStore` (`isCache:false`), dead on reload; after any reload the designer shows the hard-coded `Account`+`Contact`. | Never promise designer UI parity for *unbound* connections; no QA case may assert it. |
| T-10 | **Unknown JSON members are silently dropped — MEASURED, not inferred** (§9 item 4). A `DescribeProcess` request carrying a bogus object *and* a future-shaped `connections` array was answered normally with both ignored; no contract implements `IExtensibleDataObject`, so this holds at every nesting level. An old package + a new `connections` field is therefore a green log and a wrong process. **The plan's former mitigation does not exist:** `[RequiresPackage]` on the four process-designer gates is presence-only, and `ProcessDesignerRequiresPackageAttributeTests.cs` **asserts the absence** of a version literal. | A detector is mandatory — decision D8. |
| T-11 | **Any element parameter whose name collides with an Activity column becomes a connection** — for element parameters `IsDynamic ≡ !IsInherited`. The package **cannot** do this today (one parameter-creation site, process-level only), so this becomes reachable exactly when Phase 2 adds an element-parameter path — with no existing guard to extend and no regression baseline. | Guard and test are both new work. State it in guidance too. |
| T-12 | **Build-output and cache traps.** `clio compress -d <repo>` has no effect until clio is rebuilt; `BundledPackageCatalog._versionCache` is process-lifetime; the `EntityConnection` ESQ uses a shared `CacheItemName`. | Rebuild clio; restart long-lived `clio mcp`; decide and document the catalog's cache policy (§9 item 11). |
| T-13 | **An orphan registry row is not always benign.** The designer drops it silently, but a row whose `ColumnUId` does not resolve **throws** on the email path. | Never leave a row without its column; validate before writing. |

---

## 7. Implementation

**D1 = dedicated contract, minimal.** Phases 1 and 2 below are **one delivery** — the ops and the guards
are the reason the contract exists, so shipping the read side alone would leave every silent-inert mode
open (§10, D1). Phase 3 (clio surface) follows.

### Phase 1 — catalog + read side

Files under `packages/CrtProcessBuilder/Files/src/cs/`:

Names follow **D2 = B1**: `EntityConnection*`, not `ActivityConnection*`; the host entity is a constructor
input defaulting to `Activity`; the tag is derived, never a literal; and nothing about the host reaches
the wire format.

- `Connections/IEntityConnectionCatalog.cs` / `EntityConnectionCatalog.cs` — reads `EntityConnection` for
  the **host entity** (default `Activity`, resolved through `EntitySchemaResolver.ResolveByName`, kept as
  a *named* default rather than an inline literal). Resolves each `ColumnUId` via `EntitySchemaResolver`;
  returns `{ Name, Caption, DataValueTypeUId, ReferenceSchemaUId }`. Reads with admin rights — do **not**
  copy the designer's `UseAdminRights = false` (§9 item 5). Document the cache policy.
- `Connections/EntityConnectionBinder.cs` — the **shared** read/write kernel used by both build and
  modify, mirroring `SignalTriggerBinder`'s role so the two cannot drift. Owns find-or-reuse (T-1), the
  write shape (§7 Phase 2) and value synthesis.
- `Connections/ConnectionCapability.cs` — an **explicit allow-list keyed on user-task schema Name/UId**,
  applied **only on write paths**. There is no clean introspectable predicate: `IUserTaskActivityInfo` is
  a CLR marker on the *runtime* class while a design-time element exposes only `SchemaUId`; "overrides
  `SynchronizeDynamicParameters`" is not introspectable; "has `EntityColumnValue` params"
  under-approximates (page tasks = 0); "has registry rows" is schema-global and over-approximates to
  `ReadData`. Refuse anything not listed, naming the schema.

  Per **D3** this class also owns the **effectiveness rule** — "what makes connections actually take effect
  on this user task" — so that element-specific knowledge stays in one place instead of leaking into the
  guards:

  | User task | Connections take effect when |
  |---|---|
  | `ActivityUserTask` | **always** — it has no `CreateActivity` parameter |
  | `EmailTemplateUserTask` | `CreateActivity == true` **or** the send mode is *manual* — `ManualEmailUserTaskSender.cs:56-69` has **no** gate, unlike `AutoEmailUserTaskSender.cs:112`, so refusing on the manual path would be a **false positive** on a legitimate configuration |
  | `AutoGeneratedPageUserTask`, `PreconfiguredPageUserTask`, `UserQuestionUserTask`, `OpenEditPageUserTask` | `CreateActivity == true` |

  A simpler variant — "the parameter exists and is not `true` → refuse" — was considered and rejected: it
  is no cheaper to build (this is a three-row table in a class that must exist anyway) and it blocks the
  manual-send configuration, which is precisely the mode where a human reviews the email and the
  connections populate its *Connected to* block.

  Note the boundary with D9: `ConnectionCapability` answers *"can connections work here"*; it does **not**
  answer *"is this element retired"*. The two coincide for the three retired schemas but for independent
  reasons, so deprecation lives elsewhere (below).
- `Elements/UserTaskDeprecationPolicy.cs` — the **D9** predicate, deliberately *outside*
  `ConnectionCapability` because it is not connections-specific. Resolves the user-task schema with the
  metadata fallback (`FindInstanceByUId(uid) ?? FindInstanceFromMetaData(uid)`) so `UsageType` is actually
  populated, then applies `usageType == None ∥ name ∈ {SendEmailUserTask, EmailUserTask}`. Consumed by
  `ProcessDescriber` (the `deprecated` flag), by the build / `addElement` policy refusal (§3.6), and by
  `UserTaskCatalog` if D5 is taken. Decide the `ProcessObsoletedElements` flag treatment here (D9 residual).
- `Describe/ProcessDescriber.cs` + `Contracts/DescribeContracts.cs` — per-element `connections[]`,
  filtered to `Source != None` (T-7), plus `deprecated` and `writesConnectionsAtRuntime` (§3.6).
- `Connections/EntityConnectionReader.cs` — the **D11 hybrid** read side, mirroring
  `FilterDescriptorReader`'s role. Each emitted connection carries the **raw** persisted value under a
  stable field **and** a decoded source in exactly the shape `setConnections` accepts:

  | persisted macro | decoded source |
  |---|---|
  | `[#Lookup.{schemaUId}.{recordId}#]` | `{ recordId, referenceSchema }` — schema UId → name via `EntitySchemaResolver.FindNameByUId` (tolerant) |
  | `[#…[Element:{e}].[Parameter:{p}]#]` | `{ sourceElement, sourceElementParameter }` — both resolved to names from the schema being walked |
  | `[#…[Parameter:{p}]#]` | `{ processParameter }` |
  | anything else, **or** any of the above whose identifiers do not resolve | `{ expression: "<raw>" }` |

  Two invariants: the decoder **never fails and never loses information** — an unrecognised or
  unresolvable macro degrades to `expression` rather than producing a half-decoded source; and a new
  platform macro (the vocabulary is the fixed regex set at `GeneratorUtilities.cs:50-69`) degrades
  instead of breaking `describe`.

Tests in `tests/CrtProcessBuilder/Connections/*`; the matrix must include one element from **each side of
the 19/0 split**. First verify the fixture can even express the input (§9 item 6).

### Phase 2 — write side (same delivery as Phase 1)

The shape to write, empirically pinned (all 3208 census samples agree):

```
TypeName            Terrasoft.Core.Process.ProcessSchemaParameter   (base — NOT the Dynamic subclass)
UId                 Guid.NewGuid()                                  (NOT the column UId — T-3)
Name                column.Name                                     (identity; from FindByName — T-4)
CreatedInSchemaUId  <process schema UId>                            (⇒ IsDynamic ⇒ channel B writes it)
ModifiedInSchemaUId <process schema UId>
ContainerUId        <element UId>                                   (else GetMetaPath emits a process path)
DataValueTypeUId    column.DataValueTypeUId                         (verbatim, never normalised)
ReferenceSchemaUId  column.ReferenceSchemaUId                       (verbatim)
Tag                 $"{column.ParentSchema.Name}Connection"         (derived — T-6)
SourceValue         Source before Value; ModifiedInSchemaUId = process schema UId  (T-2)
—                   no Direction (Variable is the write default and is never persisted)
```

…and only when both `Parameters.FirstOrDefault(name, OrdinalIgnoreCase)` and `FindByUId(column.UId)` miss.
Otherwise **reuse** and assign only `SourceValue`.

Operations — add to `ProcessDesignConstants.Operations` + an `IProcessOperation` strategy each in
`Operations/ConnectionOperations.cs` (`#region Class:` per strategy, per the established idiom) + one DI
line each; `CrtProcessBuilderAppTests.CompositionRoot_RegistersEveryDocumentedOperation` fails until both
exist:

- **`setConnections`** — idempotent, per element, find-or-reuse-then-create. One op rather than splitting
  bind/create: the caller should not have to know whether a column happens to be statically declared on
  that user task. The safety comes from making find-or-reuse **mandatory and tested**, not from two verbs.
- **`clearConnections`** — clears values only (T-8).

Value ergonomics: `recordId` (synthesised to `[#Lookup.{column.ReferenceSchemaUId}.{recordId}#]`),
`processParameter`, `sourceElement` + `sourceElementParameter`, or `expression`. Exactly one, and
validate the macro family against the target type.

Guards that **refuse**, not ignore: user task not allow-listed; column not in *registry ∪ element
parameters*, validated **per element** (the static sets differ, so a global catalog would accept
`Application` on a Send email); the **effectiveness rule** fails — `ConnectionCapability` says the
connections would not take effect on this element (D3; the table in Phase 1), whose commonest case is
`CreateActivity` left at its `false` schema default (§2.6c); plus the two deprecation guards of §3.6 with
distinct messages. None of these may touch `describe`.

Refusal messages must name the fix in the **caller's** vocabulary, and for the D3 case that means making
clear it costs one array element rather than another call — e.g. *"connections on `Task1` would not take
effect: `CreateActivity` is false. Prepend `{"op":"setParameter","parameterName":"CreateActivity","parameterUpdate":{"value":"true"}}`
to this operations array."*

**D6 = (i) keeps column creation out of this path entirely.** `setConnections` therefore has exactly one
gate (`CanManageProcessDesign`, D7), no conditional privilege check, and no partial-application question.
When the column is missing it refuses with state (1) of §4.4; when the column exists but is unregistered
it refuses — or writes with an explicit warning, per D3's sibling choice — with state (2). Neither
refusal may reach `describe`.

### Phase 3 — clio surface

- `clio/Command/McpServer/Tools/ProcessDesigner/*.cs` — thread the field, update `[Description]` trigger
  lines, and **name `EmailTemplateUserTask` explicitly** as the connection-capable "Send email".
- `clio/Command/McpServer/Prompts/ProcessDesigner/*.cs` — align with the new contract.
- **`ProcessGraphValidator` needs no change.** A connection is a parameter binding, not a sequence flow;
  it does not participate in adjacency, arity or reachability.
- **Guidance** — `guidance/mcp/guides/processes/process-modeling.md` in the external `clio-knowledge` repo
  (**fetched: 316 lines / 27 950 chars, 10 `== … ==` sections**). A body edit needs **no** local re-pin;
  separately, `curated-knowledge-names.json` is pinned 9 sequences behind — pre-existing, not this feature.

  **The article currently says nothing about connections** — zero occurrences of "Connected to",
  "ActivityConnection" or "EntityConnection"; "connect" appears only for sequence flows and the R1–R17
  graph rules. Seven passages, now pinned to lines:

  | Line | Change |
  |---|---|
  | 9–15 (tool list) | `list-user-tasks` gains the deprecation caveat (D5/D9) and the note that two schemas share the caption "Send email" |
  | 17+ "What you can build today" | mention connections |
  | 178 (modify-op vocabulary) | add `setConnections` / `clearConnections` |
  | 231+ "Parameters / mapping / formulas" | the connections subsection |
  | **289** (Lookup-macro paragraph) | for connections, `recordId` replaces hand-crafting the token |
  | describe section | the `connections[]` projection, `deprecated`, `writesConnectionsAtRuntime` |
  | 293+ R1–R17 | state explicitly that connections are **not** edges and the validator is unaffected |

  Line 289 is worth quoting, because it is D1's ergonomics argument in the article's own words: *"A LOOKUP
  default … set via `expression` as `[#Lookup.{referenceObjectSchemaUId}.{recordId}#]` … **You cannot guess
  these ids** — copy the token from an existing process … a bare record id as `value` will NOT work."*
- **Docs** — the four process-designer options classes carry **no `[Verb]`**, so there is no
  `docs/commands` / `help/en` / `WikiAnchors` target unless decision D12 adds a CLI verb. Note separately
  that `add-user-task` / `modify-user-task-parameters` *are* shipped verbs and that nothing clio generates
  overrides `SynchronizeDynamicParameters` (decision D4).
- **Tests** — `clio.tests` (`Category=Unit&(Module=McpServer|Module=ProcessModel)`) and mandatory
  `clio.mcp.e2e`; `describe-business-process` has **2** E2E tests today and connections extend exactly
  that surface (§9 item 14).
- **Rebundle** — `pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <checkout> -Version X.Y.Z.W` with
  the version going **up**, then re-pin SHA-256 / `ModifiedOnUtc` in
  `clio.tests/Common/BundledProcessBuilderPackageTests.cs`. Rebuild clio (T-12).
- **ClioRing** — verdict: *ClioRing compatibility reviewed, no Ring-consumed contract changed*
  (inspected `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, `clio-ring/ClioRing.Desktop/actions.json`;
  none of the six process tools is consumed). Conditional on not touching the tool-contract envelope,
  `clio-run` dispatch, or stage events.

---

## 8. Verification matrix

| Case | Must prove |
|---|---|
| Perform task, static column (`Account`) | ✅ **measured** (§5.1): value compiles in (T-2) and the **Activity row** carries `AccountId` after a real run, with the unmapped `ContactId` NULL as control |
| Perform task, canonical signal binding (`Contact` ← `StartSignal1.RecordId`) | byte-identical persistence to the designer artifact, and the column written at runtime |
| Auto-generated page, created column | parameter materialised with correct `DataValueTypeUId`/`ReferenceSchemaUId`, written at runtime, and **completion does not throw** (§9 item 12) |
| Perform task, registry-only column (`OmniChat`) | created, not duplicated, written |
| same column twice / re-run | idempotent; exactly one parameter; still compiles |
| `SendEmailUserTask` | refused, reason names the schema |
| `EmailTemplateUserTask`, **automatic** send, `CreateActivity` at its `false` default | **refused** (D3), message naming the one-array-element fix — never silently inert |
| `EmailTemplateUserTask`, **manual** send, `CreateActivity` false | **allowed** — `ManualEmailUserTaskSender` has no gate, so this is legitimate and refusing it would be a false positive |
| `ActivityUserTask` | the effectiveness rule never fires — no `CreateActivity` parameter exists |
| `CallUserTask` (deprecated, still palette-visible) | refused on *mechanism* grounds |
| `ReadDataUserTask` | refused |
| legacy process with `EmailUserTask` / `SendEmailUserTask` / `CallUserTask` | `describe` succeeds, reports name-matched connections, flags `deprecated` |
| that describe output fed into `build` | refused on *policy* grounds — laundering closed |
| `modify` a non-connection parameter on that legacy element | allowed |
| describe round-trip, **per dialect** (D11) | `describe` → `setConnections` → `describe` is stable, once for each of: fixed record, element output, process parameter, system variable, system setting. Only bound connections appear (T-7) |
| describe of an **unrecognised** macro | degrades to `{expression: "<raw>"}` and still round-trips without loss — the pinned forward-compatibility case |
| designer round-trip | rows render with values; re-save; re-describe unchanged. Do **not** assert visibility of unbound rows (T-9). Tolerate `IsValid: false` elements |

---

## 9. Open questions of fact

### Blocks sizing

1. ~~**Does `addMapping { elementParameter: "Account" }` actually land `Activity.AccountId`?**~~
   **CLOSED by measurement on `krestov-test`, 2026-08-10 — see §5.1.** Build → `addMapping` → describe
   read-back → run → `Activity.AccountId` carries the mapped value while the unmapped `ContactId` stays
   NULL. The failure mode this question guarded against (a source evaluating to `Guid.Empty`, skipped
   silently) did not occur because the source resolved; it remains a runtime hazard worth a guidance note,
   not a scoping risk.
2. ~~**How large is the creation tail?**~~ **RESOLVED by D6.** The `Activity`-column half moved out of the
   package (§4.4), so what remains in scope is only element-parameter creation, which is bounded and needs
   no privilege escalation (§5). The sizing question dissolved with the decision.
3. ~~**The target stand's inventory**~~ — **converted into a design invariant, not a measurement.** The
   correct conclusion from the 5 rows on `krestov-test` is that the catalog may **not** assume any
   inventory: compute *registry ∪ element parameters ∩ `Activity.Columns`* and validate **per element**.
   Nothing further to measure.

### Blocks a decision

4. ~~**Is T-10's premise even true?**~~ **CONFIRMED by measurement on `krestov-test`, 2026-08-10.** A
   read-only `DescribeProcess` request carrying two unknown members — an arbitrary object **and a
   `connections` array shaped exactly like the future field** — was answered normally, with both members
   **silently ignored**. So the exact future failure mode is demonstrated, not merely analogous: an old
   package plus a new `connections` field is a green log and a silently wrong process. The result
   generalises to nested descriptors because no contract opts out: `grep IExtensibleDataObject` over all
   `[DataContract]` types in `Contracts/` (25 when measured, 27 after this feature) returns **nothing**, so the serializer drops unknown members
   uniformly at every level. **D8 is therefore required** — a detector must exist.
5. **Can the calling identity read `EntityConnection`?** Failure mode is an empty candidate list, which
   presents as a validation bug rather than a permission bug. *Remove the dependency instead of measuring
   it:* the package's catalog must not copy the designer's `UseAdminRights = false` ESQ — read the registry
   with admin rights (or via a plain `Select`), since the caller has already passed
   `CanManageProcessDesign` and the registry carries no user data.
6. ~~**Do the fixture's mocked lookup columns get a non-empty `UId`, `ReferenceSchemaUId`, `Caption`?**~~
   **CLOSED — the catalog unit-test layer is writable.** `UnitTestUtilities.CreateLookupColumn`
   (`C:/Projects/UnitTests/UnitTest/UnitTestUtilities.cs:223-238`) sets `UId = Guid.NewGuid()`,
   `ReferenceSchemaUId = referenceSchema.UId`, `ReferenceSchema`, and `DataValueType = Lookup` — all
   non-empty, so the `ColumnUId`-keyed registry join is testable. **But `Caption = new LocalizableString()`
   is empty**, so no test may assert caption content. Two setup facts: `CreateEntitySchemaMock` resolves
   the reference via `GetInstanceByName(...)`, so the referenced schema must be mocked **first** (else an
   NRE on `referenceSchema.UId`); and the extension `manager.CreateLookupColumn(target, column,
   lookupSchema)` (`:249-262`) **auto-creates the referenced schema** when missing — use that in
   connections tests and the ordering requirement disappears.
   *Residual, now a budgeted fixture task rather than an unknown:* exercising §2.4's parameter
   materialisation needs a mocked `ProcessUserTaskSchemaManager` returning a hand-built
   `ProcessUserTaskSchema` with `EntityColumnValue`-tagged parameters — `CreateUserConnection` substitutes
   only `ProcessSchemaManager`. `TestProcessSchema.UseDataValueTypeManager` already shows the pattern for
   working around the null `AppConnection`.
7. **Which diagnostic does a duplicate element-parameter name produce** (CS0102 vs CS0114)? T-1's
   regression pin; there is nothing to regress against today, so guard and test are both new.
8. ~~**Package-built processes get mappings with `Name = null`**~~ — **CLOSED, harmless.**
   `ProcessSchemaMapping` declares `[DesignModeProperty]` only for `Source`, `TargetMetaPath`, `TargetUId`,
   `SourceSchemaUId`, `SourceParameterUId` (`GT1`–`GT5`); `Name` comes from the base
   `ProcessSchemaBaseElement` and is not part of the mapping's own meta set. **No reader exists** — no
   `mapping.Name`, and no `Mappings.FindByName` / `GetByName` / `ExistsByName` anywhere in
   `Terrasoft.Core`, `Terrasoft.Core.Process` or PackageStore. So a null name is a **cosmetic metadata
   diff** against a designer-authored schema, not a functional risk. Residual, same cosmetic class and not
   falsifiable by grep: the designer's own client-side save/validation path.
9. **Same-name `Activity` column collision** — a collision breaks `Activity` for the **whole environment**
   via a codegen `ValidateException`. **Moved out of this plan by D6**: it is a requirement on the §4.4
   registration recipe (pre-check before `update-entity-schema`), not on the process builder.

### Blocks implementation detail

10. ~~**Which schema is "Send email" on the target stand**~~ — **measured on `krestov-test`:
    `ListUserTasks` returns 23 tasks** (matching the `SysProcessUserTask` row count) and includes **both**
    `EmailTemplateUserTask` and `SendEmailUserTask`, plus `EmailUserTask`, `CallUserTask` and
    `ReadDataUserTask` — every one of them as an equal peer. Two consequences, worth separating: the tool
    returns `Name`, not caption, so `list-user-tasks` is **safe on the naming axis** (an agent that keys on
    Name cannot confuse the two "Send email" schemas — only prose and captions can); but it is **unsafe on
    the deprecation axis**, since nothing marks the three retired schemas. That is D5, now confirmed on
    live data rather than inferred.
11. **What must be invalidated after a connection write**, and does the shared `CacheItemName` serve stale
    rows? Decide the catalog's own cache policy.
12. **Does reverse sync at completion survive a *created* (dynamic) parameter?** The §2.6 NRE was ruled
    out for static `Account` on the grounds that it is not dynamic — exactly the property the tail path
    does not have. *Action:* complete an Activity from an AutoGeneratedPage task carrying a created
    connection; check the error log.
13. **Read-back of lookup-record constants** — settled as fact (`describe` emits the raw metapath
    verbatim, there is no `displayValue`, and `DisplayValue` is never persisted, so parameter sources are
    reverse-resolvable by `uid` but lookup-record constants are not). What remains is build work: a
    decoder or a new field, following the `FilterDescriptorReader` precedent.
14. ~~**E2E budget**~~ — **COUNTED.** The process-designer E2E surface is **43 tests across 9 files**, not
    the "2" an earlier estimate implied: `ModifyBusinessProcessToolE2ETests` **16**,
    `CreateBusinessProcessToolE2ETests` **14**, `ValidateProcessGraphToolE2ETests` 4,
    `InstallProcessBuilderContractToolE2ETests` 3, `DescribeProcessToolE2ETests` **2**, plus
    GenerateProcessModel/Contract 1+1 and GetProcessSignature/Contract 1+1. Connections extend **three** of
    them: **Modify** (the two new ops), **Describe** (the projection plus D11's round-trip-per-dialect — six
    new cases on a file that has two today, so it more than triples), and **Create** if `connections` is
    accepted in the build descriptor. `ValidateProcessGraph` needs nothing (connections are not edges).

### Nice to know

15. Does a missing package dependency break anything at *runtime*, as opposed to being refused at
    export/install?
16. `curated-knowledge-names.json` is 9 sequences behind the live guidance library — pre-existing.
17. The 7.x `SectionWizard` is a live second writer of `EntityConnection`; concurrency/idempotency against
    the package's writer is unexamined. Matters only if partners still use it.

### Coverage gaps — no research touched these

Every stand measurement except §2.4; the created/dynamic tail at runtime; the T-10 premise; cache
staleness beyond listing the two clears; DCM/case elements; portal/SSP sections and subprocess elements;
performance of the catalog read and the unindexed-column scan; localisation of created captions; the
upgrade path for processes already built by the package; and whether **clio itself** forwards or rejects
unknown descriptor members.

---

## 10. Decisions for the reporter

| # | Decision |
|---|---|
| ~~**D1**~~ | **TAKEN = dedicated contract, minimal.** Two ops (`setConnections`, `clearConnections`) + the catalog + the describe projection, shipped as **one** delivery (§7). Rationale, in order of weight: (1) the Activity-specific knowledge — which columns are connections *per element*, the allow-list, the `CreateActivity` precondition, the deprecation rules, the three-state diagnosis, the value-dialect type rules — has to live somewhere, and the only alternatives were "unenforced, in the guidance article" or "smeared into the general-purpose `addMapping`"; (2) every gap left by the guidance-only option produces the worst failure class in this domain — a process that persists, compiles, runs green and writes nothing (the same class as T-2, T-7 and the inert `CreateActivity` case), which a validating op converts into a refusal with a reason. **Explicit non-goals:** no bind/create op split (rejected with D6); no `clearConnections` that removes parameters (T-8); no attempt to reproduce visibility of unbound rows (T-9); and **`addMapping` is not deprecated** — it remains the general primitive, with guidance naming the preferred path. Rejected staging option C (read side first, writes left on `addMapping`) because the guards would have nothing to hang on, leaving every silent-inert mode open for the whole interim. |
| ~~**D2**~~ | **TAKEN = B1, internal seam only.** The code takes a host entity (default `Activity`); the **wire format gains nothing** — the field stays `connections`, with no host member. Rationale: three of the four layers are already generic (the registry is keyed by `SysEntitySchemaUId` and ships rows for six root schemas; the tag is derived at `BaseProcessUserTaskUtilities.cs:89`; the runtime resolves columns against `entity.Schema`), and only the *consumer* layer hard-codes Activity — a narrowing the process designer invented, which the classic-UI mixin, the CTI panel (host = `Call`) and the Freedom UI service all avoid. There is, however, **no known second host** for this feature: every connection-capable user task creates an Activity. So the generality is not paid for; what B1 buys is that adding a host later is **additive** (a new optional member defaulting to Activity) rather than a breaking rename. Concretely: field named **`connections`**, never `activityConnections` — the former stays correct once a host exists, the latter becomes self-contradictory; tag always **derived**, never the literal `"ActivityConnection"` (which is doubly bad because `Activity` has a column of that exact name, T-6); classes named `EntityConnection*`, not `ActivityConnection*`; the Activity root UId kept as a **named default**. Recorded disagreement: an earlier review advised naming the field `activityConnections` *or* carrying the target entity up front, precisely to avoid a future rename — B1 reaches the same end by making the host a late addition instead of part of the name, so the rename never arises. |
| ~~**D3**~~ | **TAKEN = refuse; the "what makes connections effective" rule is owned by `ConnectionCapability`.** The decisive point against setting it implicitly: `modify-business-process` takes an **ordered array**, so a refusal costs the caller **one array element**, not a round trip — `[{setParameter CreateActivity=true}, {setConnections …}]` — which removes implicit-set's only real advantage. And turning `CreateActivity` on is a **visible product change**, not a technical detail: an extra Activity per send, appearing in the Activities section, in connected records' timelines, and in the email "processed" criterion. An op named `setConnections` must not opt a user into that, nor mutate a parameter outside its own name. The trigger includes the **schema default `false`** — that default is exactly what produced the live inert artifact (§2.6c). Never fires on `ActivityUserTask`, which has no such parameter. Refusal messages name the fix in the caller's own vocabulary. |
| **D4** | clio-generated user tasks are connection-incapable (`add-user-task` emits no `SynchronizeDynamicParameters` override): document, or extend. |
| **D5** | `list-user-tasks` does not reproduce the designer's list (no workspace filter, no `Position`, no visibility signals) and advertises `Call`, `Write email` and the obsolete `Send email` as live. Fix here or spin out — and decide hide-vs-flag. §3.3 makes the fix cheap: `deprecated` is computable from the same read. |
| ~~**D6**~~ | **TAKEN = (i).** The package refuses `Activity`-column creation; registration is the composition in §4.4. Rationale: process-schema edits and data-model edits differ in scope (one schema vs. the whole environment), reversibility, and privilege; the package's purpose is the former. A decisive practical signal: bound-data writing requires a **non-foreign** target package, so the capability would be available only on some environments — a boundary drawn in the wrong place. Rejected: (ii) removes the common case from process designers who need no schema change; (iii) makes an operation's contract depend on its content, adds a second gate inside a component that deliberately has one, and creates a partial-application question. |
| ~~**D7**~~ | **TAKEN = one gate.** `CanManageProcessDesign` only, unconditional — a direct consequence of D6. |
| ~~**D8**~~ | **TAKEN = (i).** Rely on `IBundledPackageConvergence`'s archive-version comparison: it already refuses when the environment's recorded version is older than the archive this clio ships, and `RequiredPackageChecker` already runs it on every triggered `[RequiresPackage]` — which is every gated process-designer call. So no new detector, and the rebundle's mandatory version bump is what arms it. Rejected: (ii) restates a delivery policy where it cannot track the archive, which is what the pin test forbids and `adr-bundled-package-version-source-of-truth` exists to prevent; (iii) puts the detector in the component that is BY DEFINITION the old one on a stale environment. §9 item 4 closed it. One blind spot, accepted and since OBSERVED on a live stand: an environment recording a version at or ahead of the archive passes, so a hand-built newer-but-older-code package is not caught — it degrades to the package's own loud `Operation 'setConnections' is not supported`, because an unknown OP TOKEN is refused by name while an unknown MEMBER is dropped in silence. See the ADR's D8 section. |
| ~~**D9**~~ | **TAKEN = adopt the predicate `usageType == None ∥ name ∈ {SendEmailUserTask, EmailUserTask}` as the single deprecation source, read with a METADATA FALLBACK.** Both disjuncts are mandatory, measured: `CallUserTask` carries `FK2 = 0` (`None`) and is caught by data; `SendEmailUserTask` has **no `FK2` at all** (so it defaults to `Advanced`) and `EmailUserTask` is `1` (`General`) — neither is detectable from `UsageType`, so the name literal is a necessity, not laziness, and it mirrors the platform's own list rather than inventing one. The two-disjunct form is **complete, not a simplification**: the client's third signal `element.isDeprecated` has **no server counterpart** (verified: no `IsDeprecated` anywhere in `Terrasoft.Core/Process/`) and is in any case the *output* of `getIsElementObsolete`, not an independent input. **Metadata fallback is load-bearing:** the data half only works if `UsageType` is populated on the instance the package holds, and a compiled instance is known to shed metadata (that is how `Tag` was lost) — so resolve as `FindInstanceByUId(uid) ?? FindInstanceFromMetaData(uid)`, the shipped pattern at `ProcessSchemaElementManager.cs:562-563`. **Correction from implementation:** `FindInstanceFromMetaData(Guid)` is `internal virtual` and unreachable from a configuration package — the shipped code uses the public `FindRuntimeSchemaFromMetaData` instead, to the same effect. Do not re-derive the unreachable one from this paragraph. Without it the predicate would look correct and silently return `Advanced` for everything, letting `CallUserTask` through. **Scope, as a consequence rather than a separate choice:** the predicate does **not** drive connections refusals — those come from `ConnectionCapability`'s allow-list, which already excludes all three retired schemas, and for independent *mechanical* reasons (`CallUserTask` writes nothing at runtime; `EmailUserTask` has 0 `EntityColumnValue` tags; `SendEmailUserTask` has no connections at all). Deprecation and connection-capability coincide today but are different questions, so they stay separate. The predicate was intended to power three things: the `deprecated` flag in `describe`, the policy refusal on `build`/`addElement` (§3.6), and — if D5 is taken — the flag in `list-user-tasks`. **Only the first shipped.** Stories 1-3 delivered the flag and nothing else: no operation refuses a retired user task, so `addElement` with `SendEmailUserTask` succeeds and §8's 'describe output fed into build → refused on policy grounds' row is NOT closed. Recorded here rather than left implied, because from the outside the reporting half looks like the whole feature. Whoever picks up the refusal owns §3.6's row and that §8 row together. **Residual sub-choice, two lines of code, not blocking:** whether to honour the `ProcessObsoletedElements` feature flag, which *inverts* the predicate (flag on ⇒ nothing is deprecated). Recommended: honour it, so an environment that deliberately re-enabled retired elements is not blocked by us while the designer allows them; the alternative is to be deliberately stricter than the platform and say so. |
| ~~**D10**~~ | **RESOLVED by D6.** The package writes no bound data. Binding is step 3 of §4.4, in package sources, via `create-data-binding` / `add-data-binding-row` — so column and row land in the same package by construction, and the "registry-only row" variant is simply not produced by this feature. |
| ~~**D11**~~ | **TAKEN = hybrid.** `describe` emits, per bound connection, **both** a decoded structured source and the raw value verbatim. Decode the four known dialects into exactly the shape `setConnections` accepts (`{recordId, referenceSchema}` / `{processParameter}` / `{sourceElement, sourceElementParameter}` / `{expression}`); anything else, or any dialect whose identifiers do not resolve, degrades to `{expression: "<raw>"}`. Rationale: cross-reference by `uid` works for the element-output and process-parameter dialects (both uids are in the payload — verified in §5.1) but is **inapplicable** to `[#Lookup.{schemaUId}.{recordId}#]`, where neither GUID appears anywhere in the payload; and reading a metapath while writing a `recordId` would force the caller to know two representations of one thing, reintroducing exactly the platform trivia D1 removed. The hybrid additionally makes the field forward-compatible: a new platform macro degrades instead of breaking describe, and the decoder cannot lose information. Precedent for the reader: `FilterDescriptorReader`. |
| **D12** | Ship connections MCP-only (no CLI doc surface — the options classes carry no `[Verb]`), or add a CLI verb and take on the full doc surface. |
