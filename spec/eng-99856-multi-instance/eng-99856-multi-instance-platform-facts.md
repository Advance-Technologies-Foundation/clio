# ENG-99856 — platform facts

Source-grounded research for *Sub-process element: support MULTI-INSTANCE*. Written 2026-09-21.

Every claim below carries `path:line` for source that was read. Where a figure is a measurement, its
predicate, date and scope are quoted with it. Nothing here is cited from `docs/knowledge/`, `spec/`,
`.ai/specs/` or the Jira description — those were used only to decide which type to open.

Companion documents: `eng-99856-multi-instance-handover.md` (the entry brief),
`eng-99856-multi-instance-contract-answers.md` (the six open contract questions).

Reference points: Creatio core `C:\Projects\Creatio2` branch `trunk`; shipped packages
`C:\Projects\PackageStore`; CrtProcessBuilder `origin/main` = `a8d3575`, package version `1.6.3.31`;
clio `origin/master` = `efe45ba28`.

---

## 1. Seven corrections to the brief

The ticket description was written at the close of ENG-92707. Seven of its statements do not survive
a source check. They are listed first because each one changes a design decision.

**1.1 — the ticket is RIGHT that describe tells a caller nothing about the callee, and a source-only
reading that says otherwise is wrong.** This entry was originally written the other way round, on two code
paths and no observation, and a stand measurement refuted it. It is kept as a correction because the
reasoning is seductive and someone will repeat it.

The tempting derivation: `ProcessDescriber.ReadElementParameters` short-circuits its provenance filter for
a sub-process element and passes **every** parameter through `ToDescribeParameter`
(`packages/CrtProcessBuilder/Files/src/cs/Describe/ProcessDescriber.cs:160-173` @`origin/main`), and
`ToDescribeParameter` recurses into `ItemProperties` whenever the parameter has any
(`.../Parameters/ProcessParameterService.cs:152-154` @`origin/main`); clio deserializes the field
(`clio/Command/ProcessModel/IProcessDescriber.cs:1750`, `Schema.cs:379-380` and `:420-421`
@`origin/master`). Therefore, the argument goes, the callee's contract already comes back.

> **SUPERSEDED 2026-09-21 by story 1's gate — [eng-99856-multi-instance-describe-gate-outcome.md](eng-99856-multi-instance-describe-gate-outcome.md).**
> The refutation below **does not reproduce**, and the entry has now been wrong in both directions. On the
> same stand, the same package version (1.6.3.31), clio from this branch, a read taken on a worker process
> whose whole request history is known reports `itemProperties` on all five parameters that carry them:
> `SubProcess2` at **4** and **6**, `PushExpiredLicensesNotificationSubProcess` at 10 and 10, and the
> process-level `CheckedLicenses` at 4 — matching the environment's own stored `SysSchema.MetaData`, read
> directly, entry for entry. So the source-only derivation this entry calls "seductive" is in fact
> **right**, the gate outcome is **Outcome 1**, and **AC-15 is met as written**.
>
> The correction the entry was written to carry survives in a weaker and more useful form: a described
> `itemProperties` reflects the INSTANCE the server is holding, which is stable across repeated reads
> within one worker process and was observed to differ BETWEEN worker processes. An environment that has
> just had a package pushed is not a sound place to measure one.
>
> **Consequence for scope, restated:** reporting the callee's contract for a multi-instance element is
> NOT new work — it already ships. What ships with it is the sentence above.

**It does not.** MEASURED 2026-09-21 against the live stand `Creatio`
(`http://d_krestov_n.tscrm.com:40001`) carrying `CrtProcessBuilder 1.6.3.31` (installed for this
measurement and verified by `list-packages`), driven by a clio built from `origin/master` `01c8b677a`:
`describe-business-process` on `ExpireLicenseNotificationProcess` returns, for the multi-instance element
`SubProcess2`, exactly the five root parameters — three `Integer/Out` counters, `InputRecordCollection`
`CompositeObjectList/In` with its `Script` mapping, `OutputRecordCollection` `CompositeObjectList/Out` —
and `subProcess: { multiInstance: true, inSync: false, process: "ChecksLicensesForNotificationProcess" }`.
**No `itemProperties` on either collection.** A control sweep over every parameter of every element and of
the process reports `itemProperties` on **none** of them, including the process-level `CheckedLicenses`.

That absence is not the stored state: in the shipped metadata
(`CrtBase/branches/7.8.0/Schemas/ExpireLicenseNotificationProcess/metadata.json`) `InputRecordCollection`
carries `L18` with 4 entries, `OutputRecordCollection` 6, and `CheckedLicenses` 4. Nor is it a missing
projection: the deployed archive's own source carries it at
`Files/src/cs/Parameters/ProcessParameterService.cs:152-153` (read by unpacking the installed `.gz`).

So `ItemProperties` is **empty in the schema instance describe reads**. The likely mechanism —
candidate, not proven — is `ProcessSchemaRepository.LoadForDescribe`
(`Files/src/cs/Schema/ProcessSchemaRepository.cs:241-282`), which prefers the manager's runtime instance
(`FindInstanceByUId` / `FindInstanceByName`) and only falls back to a design instance when the manager has
none; design-time nested metadata plausibly is not materialised on the runtime instance. Confirming that,
and checking whether the design-instance fallback does report them, is the first thing story 1 must do.

**Consequence for scope:** reporting the callee's contract for a multi-instance element is real work, not
an assertion over something that already ships.

**1.2 — the metadata reader is not the only assignment site.** The ticket says "the only place the
platform assigns `MultiInstanceOptions` is the metadata reader". There are two: the copy constructor
`ProcessSchemaActivity.cs:63` and the reader arm `ProcessSchemaActivity.cs:498`. The conclusion the ticket
draws from it still holds — nothing server-side creates one from a design command — but the premise as
written is wrong.

**1.3 — the run-time path throws on all five, not only the collections.** The ticket frames the
self-heal/throw asymmetry as design-time. At run time `MultiInstanceParameters`' constructor resolves the
output collection and all three counters with `GetByUId`
(`Terrasoft.Core/Process/MultiInstanceParameters.cs:26-35`) and `CreateParameterValueReader` resolves the
input collection the same way (`Terrasoft.Core/Process/ProcessParameterValueProvider.cs:719-722`). A `BP6`
whose UIds do not resolve is a run-time throw as well as a design-time one.

**1.4 — an empty `"BP6": {}` round-trips as multi-instance ENABLED.** Three lines settle it, and the
consequence is the worst failure this feature can produce.

- The options writer suppresses **every** field at its default — `IgnoreErrors` against `false`, each of
  the five Guids against `Guid.Empty`, `ExecutionMode` against `Sequential`
  (`ProcessSchemaMultiInstanceOptions.cs:145-155`). An all-default options object therefore writes as `{}`.
- The element writer emits `BP6` whenever the property is non-null (`ProcessSchemaActivity.cs:555-559`).
- The reader constructs the instance **before** `ReadInto` and assigns it unconditionally
  (`ProcessSchemaActivity.cs:493-498`), so `"BP6": {}` reads back as a non-null options object with five
  empty UIds.

Since `IsMultiInstanceModeEnabled` is the bare null check `MultiInstanceOptions != null`
(`ProcessSchemaActivity.cs:84`), such an element **is** multi-instance, and the first thing the rebuild
does is `GetByUId(Guid.Empty)` on it (§3) — which throws. The element can no longer be loaded, in the
designer or on the server, and no platform validation rule exists to diagnose it. **No shipped element
carries an empty `BP6`** (§5), so this is a state only a tool could create.

**1.5 — `JE1` and `JE4` are absent at their defaults.** A Sequential, non-ignoring element serialises only
the five UIds. Measured: across the 61 shipped `BP6` nodes the key frequencies are `JE2` 61, `JE3` 61,
`JE5` 57, `JE6` 57, `JE7` 57, `JE1` 9, `JE4` 9. `JE4` is only ever written as `1` (Parallel) and `JE1` only
ever as `true`, which is exactly what default-suppression predicts.

**1.6 — Direction's omitted default is `Variable`, not `In`.**
`writer.WriteValue(DirectionPropertyName, Direction, ProcessSchemaParameterDirection.Variable)`
(`Terrasoft.Core/Process/ProcessSchemaParameter.cs:1009`); the enum is `In=0, Out=1, Variable=2,
Internal=3` (`:34-55`). An item property with no `L12` is a **Variable**. This is what makes the XOR'd
output twin legible in shipped data, and it means a guard phrased as "only In/Variable item properties may
be mapped" admits nearly everything.

**1.7 — de-conversion exists, and it is the designer's own operation.** The ticket treats de-conversion as
an open question. `convertToSingleInstance()` is implemented at
`Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-activity-schema.js:601-609`
and pinned by the platform's own Jasmine spec at `tests/process-activity-schema.unit.spec.js:406-437`.
Whether *the tooling* should offer it is a scope decision; whether the platform has one is settled.

---

## 2. The metadata a tool must produce

`MultiInstanceOptions` is meta name **`BP6`** on the abstract `ProcessSchemaActivity` — **not** on
`ProcessSchemaSubProcess` — `MetaTypeProperty {B6D92F79-A07C-434E-B205-6554C2239351}`,
`DesignModeUsageType.None`, a plain auto-property (`ProcessSchemaActivity.cs:22`, `:34`, `:122`).

| meta | property | type | MetaTypeProperty |
|---|---|---|---|
| `JE1` | `IgnoreErrors` | bool | `{F105FC11-26B7-467D-9B48-B6B2276DED87}` |
| `JE2` | `InputCollectionParameterUId` | Guid | `{8794C0A7-41A0-499F-BE83-E7337A43D25D}` |
| `JE3` | `OutputCollectionParameterUId` | Guid | `{63BBD230-8BD2-426F-91BE-32083A821E81}` |
| `JE4` | `ExecutionMode` | `MultiInstanceExecutionMode` | `{59364F37-A3A1-4C3C-A8E9-B0521BD65283}` |
| `JE5` | `CompletedIterationsCountParameterUId` | Guid | `{8D325BAA-5153-48C6-9C12-1B1610C253DA}` |
| `JE6` | `TerminatedIterationsCountParameterUId` | Guid | `{CFF0E7AC-1F0A-46A6-B9AE-D1D2D524B3AF}` |
| `JE7` | `TotalIterationsCountParameterUId` | Guid | `{8BEC1CF5-EF50-4304-9874-5150C905661F}` |

`ProcessSchemaMultiInstanceOptions.cs:30-36` (the consts), `:66-106` (the properties), `:56-60` (the empty
public constructor). `MultiInstanceExecutionMode` is `Sequential`(0) | `Parallel`(1) with no explicit
values (`ProcessEnum.cs:239-251`).

Parameter meta keys (`ProcessSchemaParameter.cs:123-142`): `L1` DataValueTypeUId, `L8` SourceValue,
`L9` ReferenceSchemaUId, `L12` Direction, `L17` Tag, `L18` ItemProperties (written only when non-empty,
`:1011-1015`). Inside `L8`: `GS1` Source, `GS2` value, `GS5` ModifiedInSchemaUId. On the parameter itself:
`A2` Name, `A3` CreatedInSchemaUId, `A4` ModifiedInSchemaUId, `IL2` ContainerUId.
`ProcessSchemaParameterValueSource` is `None=0, ConstValue=1, Mapping=2, Script=3, SystemValue=4,
SystemSetting=5, EntityMapping=6, SamplingEntityMapping=7` (`:15-25`).

Data value type UIds a tool needs: `CompositeObjectList` `{651EC16F-D140-46DB-B9E2-825C985A8AC2}`,
`Integer` `{6B6B74E2-820D-490E-A017-2B73D4CCF2B0}`, `CompositeObject` (distinct from the list)
`{632E4371-0A7F-46CD-A284-A623B3933027}`.

An element's parameters serialise as `BP2`, an array whose first key `BL1` names the CLR type the reader
instantiates. **`BP2` order is not canonical**: over the 61 shipped elements, 23 are in *client* order
(input, output, then counters — what `convertToMultiInstance` produces) and 38 in *server* order (counters,
input, output — what `SynchronizeParametersInternal` produces). A tool must not key on position.

There is **no server-side nesting depth limit** on `ItemProperties`: the reader is unconditionally
recursive and no depth counter exists in `Terrasoft.Common`'s reader/writer, in `Terrasoft.Core/Metadata`
or in `Terrasoft.Core/Process`. Adding an item to a nested collection only sets `IsNested = true`.

---

## 3. The rebuild, and the asymmetry that dictates construction order

`SynchronizeParametersInternal` (`ProcessSchemaActivity.cs:373-395`) is the whole mechanism:

1. clone the input collection, the output collection and the three counters;
2. `Parameters.Clear()`;
3. `LoadCollectionParameters` — flatten **all** input item properties, and only the `Out`-direction output
   item properties, into `Parameters` (`:356-368`);
4. clear both `ItemProperties`;
5. `SynchronizeParameters()` — the **ordinary** callee diff, running over the flattened list, which is the
   only state in which the five multi-instance parameters are invisible to it;
6. `FillCollectionParameters` — route by direction (`:336-355`);
7. `Parameters.Clear()` again;
8. re-add the three counters, then input, then output.

`Parameters.Clear()` runs twice. A third clear can occur transitively: the ordinary `SynchronizeParameters`
calls `ClearParameters()` when `SchemaUId.IsEmpty()`, and *that* one also removes the mappings of
everything it clears (`:508-514`, `:587-599`) — unlike the two bare `Clear()` calls.

**The direction routing** (`FillCollectionParameters`, `:336-355`): `Variable` → the original goes to the
input collection and a clone to the output collection with `UId = originalUId XOR outputCollectionUId` and
its source value cleared; `Out` → output only; `In` → input only. `Internal` is routed **nowhere** —
see §8.

`Guid.Xor` is a byte-wise XOR of `ToByteArray()`. Verified against shipped bytes: input item
`LicenseProductName` `2c9bd5f6-bc32-446a-b649-fd582ad5fb79` XOR `OutputRecordCollection`
`c187fc99-9c14-4f73-81ab-96711b2e77b0` = `ed1c296f-2026-0b19-37e2-6b2931fb8cc9`, which is the UId of the
mirrored output item of the same name in
`CrtBase/branches/7.8.0/Schemas/ExpireLicenseNotificationProcess/metadata.json`. A tool must reproduce the
derivation, not mint fresh Guids.

### The asymmetry

| | lookup | on a miss |
|---|---|---|
| `InputRecordCollection`, `OutputRecordCollection` | `Parameters.GetByUId` (`:328-334`) | **throws `ItemNotFoundException`** (`MetaItemCollection.cs:195-203`) |
| the three counters | `Parameters.FindByUId` via `TryCopyParameter` (`:458-461`) | returns null → `CreateIntegerParameter` → the new UId is written back into the options (`:397-428`) |

`CreateIntegerParameter` (`:430-446`) produces: fresh Guid, the literal name, a `Terrasoft.Nui` resource
caption, `Source = None`, `IntegerDataValueType`, `Direction = Out`, and
`CreatedInSchemaUId = ModifiedInSchemaUId = ProcessSchema.UId` — the **caller's** schema. It resolves
`DataValueTypeManager` through `ProcessSchema.SystemUserConnection`, so it needs a live system connection.

### The trap

`ProcessSchemaSubProcess.SchemaUId`'s **setter** calls `SynchronizeParameters()`, which routes to
`SynchronizeParametersInternal`. So assigning `MultiInstanceOptions` and then assigning `SchemaUId` throws
`ItemNotFoundException` **out of a property set** unless both collection parameters already exist in
`Parameters` under exactly the UIds named in `JE2`/`JE3`. Any assignment to `SchemaUId` — even the same
value — re-enters the rebuild.

The construction order is therefore forced, and it is exactly the order the designer's client uses:
**create all five parameters → add them to `Parameters` → write all five UIds into the options → assign
`MultiInstanceOptions` → only then touch `SchemaUId`.**

---

## 4. The client reference implementation

Conversion is a client behaviour; no server API converts an element. The reference is
`Terrasoft.Nui/Resources/Terrasoft/manager/process-flow-element-schema-manager/process-activity-schema.js`
(source tree, **not** the deployed copy under `Terrasoft.WebApp.Loader/.../Conf/content/`).

- `convertToMultiInstance()` `:526-547` — creates the five, calls `_fillCollectionParameters`, then
  `_fillIterationsCountParameters`, and assigns `multiInstanceOptions` **last**. It never sets
  `executionMode` or `ignoreErrors`, so both take the class defaults `SEQUENTIAL` / `false`
  (`process-schema-multi-instance-options.js:15`, `:31`).
- `_createInputRecordCollectionParameter` / `_createOutputRecordCollectionParameter` `:146-165` —
  `COMPOSITE_OBJECT_LIST`, Direction `IN` / `OUT`. The three counters `:169-196` — `INTEGER`, Direction
  `OUT`.
- `createParameter` `:441-455` stamps `createdInSchemaUId = this.parentSchema.uId` — the **caller**.
- `_fillCollectionParameters` `:317-338` — the client mirror of the server's routing, including the
  `Terrasoft.xorToGUID` twin.
- `convertToSingleInstance()` `:601-609` — `_convertToSingleInstanceElementParameters()` then
  `multiInstanceOptions = null`. Lossless for the callee's parameters: all input items return, only
  `OUT`-direction output items return, so the XOR'd Variable twin is discarded and the original comes back
  from the input side.
- `resetParameters(callback, scope)` `:560-595` — de-convert, run the callee re-sync, re-convert. This is
  how the designer re-syncs a multi-instance element.
- `getEditableParameters` `:634-648` — in non-extended mode **only the input collection parameter is
  editable**. The user maps `InputRecordCollection`; per-item mappings live inside its item properties.

Pinned by the platform's own Jasmine spec `tests/process-activity-schema.unit.spec.js:290-437`, which
asserts the exact field values, the count of five roots, the XOR twin and the caption resources.

### Two gates the ticket does not mention

`process-subprocess-schema.js:201`:

```js
getIsMultiInstanceSupported: function() {
    return Terrasoft.Features.getIsEnabled("UseMultiInstanceSubProcess") && !this.parentSchema.useForceCompile;
}
```

1. Feature `UseMultiInstanceSubProcess`, UId `868e51db-4582-40c1-9605-7b70d9b38e8b`, shipped **enabled**
   (state 1) for "All employees" and "All external users" —
   `PackageStore/CrtProcessDesigner/branches/7.8.0/Data/Feature_UseMultiInstanceSubprocess/data.json` and
   `.../AdminUnitFeatureState_UseMultiInstanceSubprocess/data.json`.
2. `!parentSchema.useForceCompile`. `UseForceCompile` is `BK31` on `ProcessSchema`
   (`ProcessSchema.cs:111`, `:389`), default false. An **embedded** process schema hard-codes
   `useForceCompile: true` (`embedded-process-schema.js:57`), so a sub-process element inside an embedded
   process can never be multi-instance.

### What triggers conversion in the UI

`CrtProcessDesigner/branches/7.8.0/Schemas/ProcessFlowElementPropertiesPage/ProcessFlowElementPropertiesPage.js:846`
subscribes `viewModel.on("collectionMappingSet", this._convertElementToMultiInstanceMode, this)`;
`_convertElementToMultiInstanceMode` `:875-888` calls `convertToMultiInstance()`. De-conversion at
`:859-863` when the mapping is cleared, gated by `MappingEditMixin.js:1018-1030`
(`_getCanConvertToSingleInstance`), which offers it only when the cleared parameter **is** the input
collection. So "mapping an incoming parameter to a data collection converts the element" is confirmed.

---

## 5. What the shipped corpus actually contains

Scanned `C:\Projects\PackageStore` on **2026-09-21**. Predicate: every JSON object anywhere inside a file
matching `*/Schemas/*/metadata.json` whose `"BL1"` is `"Terrasoft.Core.Process.ProcessSchemaSubProcess"`.
No de-duplication across `branches/` trees was applied.

- **416** sub-process elements in 262 candidate files; **61** carry a `BP6` key, across 40 files —
  14.7%. Reproduced independently by a second pass counting `"BP6"` occurrences
  (`grep -ro '"BP6"' --include=metadata.json .` → 61).
- Every `BP6`-bearing element in the corpus is a `ProcessSchemaSubProcess`. **No other activity kind ships
  as multi-instance.**
- Across all 61 nodes the only keys that ever appear are `JE1`..`JE7` — never `UId`, `A2`, `A3`, `A4`,
  `A5`. Node sizes: 5 keys ×43, 6 ×12, 7 ×2, 3 ×2, 2 ×2. **No element carries an empty `BP6`.**
- `JE1` and `JE4` are independent: `(JE1, JE4)` cross-tab = (absent, absent) 45, (true, absent) 7,
  (true, 1) 2, (absent, 1) 7. So **9 of 61 are Parallel** and **9 of 61 ignore errors**.
- Multi-instance elements appear **only** in the metadata blob, never in a generated `.cs` schema body
  (`grep -rl MultiInstanceOptions --include=*.cs` over PackageStore returns nothing).
- Shipped directions: `InputRecordCollection` `L12=0` (In), `OutputRecordCollection` and the three counters
  `L12=1` (Out).

A complete worked example is `CrtBase/branches/7.8.0/Schemas/ExpireLicenseNotificationProcess/metadata.json`,
element `SubProcess2`. Its `BP6` carries only `JE2`, `JE3`, `JE5`, `JE6`, `JE7`. Its
`InputRecordCollection` has `L1` = the `CompositeObjectList` UId, `L12: 0`, an `L8` with `GS1: 3` (Script)
whose `GS2` is `[#[IsOwnerSchema:false].[IsSchema:false].[Element:{…}].[Parameter:{…}]#]`, and an `L18`
holding the callee's parameters — each a full `ProcessSchemaParameter` with its own `L8` mapping. The item
properties are stamped with the **callee's** schema UId (`A3` `5905d1ab…`) while the element and its two
collections carry the **caller's** (`1893ce0c…`).

---

## 6. Run-time semantics

**The binding is by NAME, through the input collection's item properties.**
`ProcessParameterValueProvider.cs:719-723` substitutes the element's parameter set wholesale:

```csharp
if (element is ProcessSchemaActivity activity && activity.MultiInstanceOptions != null) {
    ProcessSchemaParameter inputCollection = activity.Parameters.GetByUId(
        activity.MultiInstanceOptions.InputCollectionParameterUId);
    parameters = inputCollection.ItemProperties;
    int iterationNumber = options.FlowContext?.MultiInstanceExecutionData.CurrentIterationNumber ?? 0;
```

The item properties are not a *description* of the callee's parameters — at run time they **are** the
element's parameters. The per-iteration value is then pulled out of the iteration's `CompositeObject` by
**name**. What is written back is one `CompositeObject` per writer instance, built by name and appended on
dispose; keys with no matching output item property are **silently skipped**. If
`outputCollectionParameter.ItemProperties` is empty, `GetMultiInstanceParameterOptions` returns `None` and
output writing is skipped entirely (`ProcessParameterValueProvider.cs:258-264`).

**Mapping authority.** The parameter's `SourceValue` is the live store; the `ProcessSchemaMapping` (`BK15`)
row is derived bookkeeping that nothing at run time reads. The `SourceValue` **setter** writes into
`mappingInfo.Source` (`ProcessSchemaParameter.cs:446-455`) — authority flows parameter → row, never back —
and `BaseFlowSchemaGenerator.GenerateProcessParameterMappings` builds the run-time parameter map from
`element.ForceGetParameters()` (`:1039-1062`), with the token `.Mappings` appearing in none of
`BaseFlowSchemaGenerator.cs`, `FlowSchemaGenerator.cs`, `ProcessSchemaGenerator.cs`,
`ProcessSchemaGeneratorNew.cs`. A corpus disagreement between a row's `GT1` and its parameter's `L8` is a
stale bookkeeping column, not an unsettled authority question.

**Execution mode.** Sequential and Parallel differ only in generated flow topology. Parallel is **not
gated** by any `GlobalAppSettings` flag or system setting — `FlowSchemaGenerator.cs:379` is the only place
in `Terrasoft.Core` that reads `MultiInstanceExecutionMode` and there is no condition around it. Nor does
"Parallel" mean concurrent threads by itself: `FlowVisitor` drives a single-threaded FIFO queue, so
parallel iterations interleave on one thread; genuine concurrency comes only from `FlowBackgroundToken`,
which the generator inserts when `activity.UseBackgroundMode` is true.

**IgnoreErrors** is transferred onto exactly one generated element, the Error iteration token, and changes
only what `Compensate` does: with it, the flow continues to the next element; without it, the element log
is set to Error and the process fails. The failed-iteration increment happens in both cases.

**The counters** are written exactly once, at the end of the run, by
`ProcessInstanceCollectionParametersDataWriter.ActualizeResultParameters`. `CompletedIterationsCount` is
not what it looks like mid-run: `FlowJoinIteratorGateway` uses it as the parallel barrier's arrival counter
and the End token then **overwrites** it with `TotalIterationsCount − FailedIterationsCount` before
persisting.

**A second, non-obvious input source.** If the input collection parameter itself has `Source == None` but
at least one of its item properties has `Source == Script`, the generator marks
`HasCollectionWithOneElement` and the run performs exactly **one** iteration whose values come from the
item properties' own formulas rather than from any stored collection.

**Generated names are load-bearing.** The generator rewires the element's sequence flows to a Start/End
iteration-token pair and inserts an `IteratorGateway`. The generated element names are derived from the
iterable element's `Name`, and the iteration-count round trip carries `IteratorTokenName` as a **string**
resolved with `FlowSchema.GetFlowElement(string)`, which throws if it does not resolve. Everything else in
the generated topology references elements by UId.

**Type checking.** `ParameterCollectionExpressionVariableBuilder` hard-checks the exact
`CompositeObjectList` UId and throws "is not table parameter" otherwise — and it looks the parameter up
**by name**.

---

## 7. `UseLastSchemaVersion` (`CK5`) — closed, negatively

Meta name `CK5` on `ProcessSchemaSubProcess`, `bool`, `MetaTypeProperty
{4FD6C6B7-790C-45D3-9029-D47865EC3E29}`, default false, `DesignModeUsageType.General` at Position 3 (and
explicitly `None` on `ProcessSchemaEventSubProcess`).

It has **no consumer anywhere in the platform source**, the `ProcessSchemaSubProcess` copy constructor does
not copy it (so every clone silently resets it to false), and **zero of the 61 shipped multi-instance
elements set it**. There is no behaviour to support and nothing for this contract to carry. It should stay
out of scope, and the ticket's question 6 can be closed on that basis.

---

## 8. Two defects found in passing

**8.1 — the multi-instance test harness types nothing correctly.**
`SubProcessTestSupport.AParameterOn` hardcodes
`DataValueType = schema.DataValueTypeManager.GetInstanceByName("Text")`
(`tests/UnitTests/CrtProcessBuilder.Tests/SubProcessTestSupport.cs:265` @`origin/main`), and
`MakeMultiInstance` (`:211-233`) builds both collections *and* all three counters through it. So every
existing multi-instance test in the package runs over an element whose collections are not collections and
whose counters are not integers. Any evidence resting on that helper is weaker than it looks, and the fix
is a precondition for offering new tests as evidence.

**8.2 — the refusal is typed one level too low.** `MultiInstanceOptions` is declared on the abstract
`ProcessSchemaActivity` (`ProcessSchemaActivity.cs:122`), but the guard is
`EnsureNotMultiInstance(ProcessSchemaSubProcess element, string elementName)`
(`packages/CrtProcessBuilder/Files/src/cs/Elements/SubProcessApplier.cs:393` @`origin/main`). A
multi-instance **user task** therefore reaches the other appliers unguarded and has its flat parameters
discarded by the rebuild. No shipped element is in that state (§5), so this is latent rather than live —
but it is a one-line re-typing.

---

## 9. T-27: the stale clause on `SubProcessApplier`, and why it does not hold

`SubProcessApplier`'s class summary on `origin/main` still says the platform removes a stranded mapping row
itself for every non-dynamic parameter, that "**this contract produces no dynamic ones**", and that the
state could not be constructed below a live stand. PR #72 corrected the neighbouring clause about the
dependency scanner and left this one.

It does not hold, and the chain is short:

1. `IsDynamic` is `CreatedInSchemaUId == BaseProcessSchema.UId` — the **caller's** schema
   (`ProcessSchemaParameter.cs:270-277`).
2. An activity's `Parameters` collection is constructed with `ParentMetaSchema = BaseProcessSchema`
   (`ProcessSchemaActivity.cs:115`).
3. `MetaItemCollection.InsertItem` backfills `CreatedInSchemaUId = ParentMetaSchema.UId` whenever it is
   empty (`MetaItemCollection.cs:99-100`), and `ClearItems` / `RemoveItem` **reset** it to `Guid.Empty`
   (`:123`, `:133`).

So any parameter added to an element's `Parameters` without an explicit stamp becomes dynamic — precisely
the case the prune arm skips. `GetRemovedSchemaParameters` (`ProcessSchemaActivity.cs:253-270`) has **two**
arms, not the one the clause addresses: no mapping **and** `CreatedInSchemaUId == SchemaUId` (the *callee's*
schema) → remove; or a mapping whose source is gone **and** `!target.IsDynamic` → remove.

Multi-instance makes this concrete rather than theoretical: `CreateIntegerParameter` explicitly stamps the
**caller's** schema (`:443-444`), so all three counters are dynamic by construction. The clause is true
today only because multi-instance is refused; delivering ENG-99856 makes it false. Correct it in the same
change.

---

## 10. What is genuinely stand-only

Everything above is source or corpus. These are not:

- whether the designer **opens and renders** an element the applier built. The client resolves all five
  multi-instance parameters with the throwing `Terrasoft.Collection#get` while loading localizable values,
  so a construction defect surfaces as a designer exception, not a validation message;
- whether a converted element actually **iterates** — the output collection filling one item per completed
  iteration and the three counters landing;
- whether a multi-instance element can be built **by hand on the target stand** to serve as an e2e fixture
  (the feature ships enabled, but per role, and neither caller nor callee may be force-compiled);
- concurrency behaviour of `Parallel` together with `UseBackgroundMode`.

Two operational notes for whoever runs those: schema-write operations against a stand must be run
**sequentially** — a parallel burst trips IIS rapid-fail and takes down a .NET Framework stand's app pool.
And CrtProcessBuilder must be built and tested in the **main checkout**, not a git worktree: the untracked
core-bin tree is absent there and the workaround yields ~1355 spurious assembly-resolution failures that
read as a code regression.
