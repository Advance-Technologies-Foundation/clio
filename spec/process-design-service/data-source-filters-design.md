# ENG-91842 — Data source filters in element parameters: research & design

> Task 4 of the BP-generation list (the **cost center**, ~6 days). Jira:
> [ENG-91842](https://creatio.atlassian.net/browse/ENG-91842). Sub-feature of ProcessDesignService /
> Approach 1. Read `process-design-service-state.md` first for the overall picture.
>
> **Branches (created for this task):**
> - clio: `feature/ENG-91842-data-source-filters` (off `feature/ENG-90883-approach1-backend-designer`)
> - package `clioprocessbuilder` (`C:\Projects\workspace\ProcessBuilder`): `feature/ENG-91842-data-source-filters` (off `main`)

## 1. Goal

Let the builder set **data source filters** declaratively on the elements that need them, so generated
processes target specific records instead of acting on "all" records / firing on any change:

- **Read data** (`ReadDataUserTask`) — which records to read.
- **Modify data** (`changeDataUserTask`) — which records to update.
- **Delete data** (`deleteDataUserTask`) — which records to delete.
- **Add data** (`addDataUserTask`) — selection filters in "add from selection" mode.
- **Signal start** (`startEventSignal`) — `EntityFilters`: only fire for records matching the filter.

The AI must express a **high-level filter** (column, comparison, value/parameter, AND/OR groups) and the
**server** must produce the deeply-nested, escaped `Terrasoft.FilterGroup` JSON. The AI must never
hand-write the escaped JSON.

## 2. Research findings (client + server)

(Full capture + enum table: `captures/readdata-filters-capture.md`.)

### Where filters live
- **Data-operation elements:** a string element-parameter **`DataSourceFilters`** (DataValueType
  `394e160f-…` = `MetaDataText` — a plain serialized-string store). Its `SourceValue` is a **ConstValue**
  whose `Value` is the wrapper JSON. Assigning `elementParameter.SourceValue` auto-syncs `schema.Mappings`
  (same mechanism the mapping service already uses — one write, no manual `Mappings` edit).
- **Signal start:** the same wrapper JSON, stored on the element's **`EntityFilters`** string property
  (metadata key `DZ13`). Not an element parameter — a direct property assignment + `SaveSchema`.

### The wrapper (both representations)
```
{ "className":"Terrasoft.FilterGroup",
  "serializedFilterEditData": "<full FilterGroup JSON, for designer re-edit>",
  "dataSourceFilters":        "<lean FilterGroup JSON, for runtime>" }
```
- **Runtime reads only `dataSourceFilters`** (`Terrasoft.Nui.ServiceModel.DataContract.ProcessFilterFactory.Deserialize`
  → `JsonConvert.DeserializeObject<Filters>(wrapper["dataSourceFilters"])`). `serializedFilterEditData` is
  purely for the designer to re-render the filter UI.
- We emit **both** (the process must be runnable AND re-editable in the designer — the whole point of
  Approach 1). The lean one is the full one minus `className`, per-item `key`, `leftExpressionCaption`,
  `referenceSchemaName`, `isAggregative`, and the parameter `displayValue` — exactly what the client's
  `FilterModuleMixin.saveDataSourceFilters()` does (`deleteParameterDisplayValues`).

### No server builder API
`Terrasoft.*` exposes no public "FilterGroup → JSON" helper (the converters are `internal`). **We emit the
JSON ourselves** from package-local serialization DTOs whose field names match the wire format
(`filterType`, `comparisonType`, `leftExpression`, `rightExpression`/`rightExpressions`, `items`,
`logicalOperation`, `rootSchemaName`, `isEnabled`, `expressionType`, `columnPath`, `parameter`). This is
also the right call for dependency isolation (the package deliberately keeps its version-coupling surface
minimal — see `ProcessDesignConstants` doc comment).

### Filter shape rules (from the capture)
- Each condition is a `CompareFilter` (`filterType:1`) with a single `rightExpression`, **except** a
  **lookup** column compared for equality, which the designer emits as an `InFilter` (`filterType:4`) with
  a `rightExpressions[]` array. To stay designer-faithful we mirror this: resolve the column's
  `DataValueType` from the `EntitySchema`; if Lookup → InFilter, else CompareFilter.
- Item-level `dataValueType` = the **column's** type. Right side:
  - **constant** → `parameter.dataValueType` = column type, `parameter.value` = the scalar (string vs
    number must match the type, e.g. `333` not `"333"` for Integer);
  - **parameter reference** → `parameter.dataValueType` = **26** (Mapping), `parameter.value` =
    `{ value:<token>, displayValue:<caption> (edit-data only), Id:<fresh GUID> }`.
- The reference **token** is `ProcessSchemaParameter.GetMetaPath()` of the referenced param (process-level:
  `[IsOwnerSchema:false].[IsSchema:false].[Parameter:{uid}]`; element-level — e.g. "Perform task.Account":
  `…[Element:{uid}].[Parameter:{uid}]`). We already use `GetMetaPath()` for mappings — reuse it.
- Group: `filterType:6`, `logicalOperation` 0=And / 1=Or, `rootSchemaName` = the queried object,
  `isEnabled:true`. Nested groups are an `items` entry that is itself a `filterType:6` group (recursive).

### Enum tokens (server-emitted numbers)
`FilterType` Compare=1/IsNull=2/Between=3/In=4/Group=6 · `ComparisonType` Equal=3, NotEqual=4, Less=5,
LessOrEqual=6, Greater=7, GreaterOrEqual=8, StartWith=9, Contain=11, EndWith=13, IsNull=1, IsNotNull=2 ·
`LogicalOperatorType` And=0/Or=1 · `ExpressionType` Column=0/Parameter=2 · `DataValueType` Text=1, Integer=4,
Float=5, Money=6, DateTime=7, Date=8, Time=9, Lookup=10, Boolean=12, Mapping=26.

## 3. The root-object dependency (important scoping point)

A filter needs a **root object** to resolve column types and to set `rootSchemaName`:
- **Signal start** — the object is already known (`signal.entity`). **Fully implementable now.**
- **Data-operation elements** — the object is the element's target (e.g. ReadData's `ResultEntity`
  reference). The current build contract does **not** yet set a data element's target object (that is Task
  12 / ENG-91850). A filter whose `rootSchemaName` doesn't match the element's actual queried object won't
  apply at runtime.

**Recommendation (keeps ENG-91842 self-contained and verifiable):** the filter descriptor carries its own
root **`object`**, and for data-operation elements we also set the **minimum** element target needed for the
filter to apply (e.g. ReadData `ResultEntity.ReferenceSchemaUId` + the result-entity wiring). Full Read-data
configuration (result columns, top-N, ordering, read mode) stays Task 12. This makes a filtered ReadData
end-to-end testable today without blocking on Task 12, and Task 12 later just enriches the same element.

## 4. Proposed contract (declarative filter descriptor) — CONFIRMED `conditions` + `groups`

A reusable `filter` block attachable to a data-operation element and to a signal start. High-level only —
no GUIDs, no escaping. **A group node = `{ logicalOperation, conditions[], groups[] }` (recursive).** The
top-level `filter` is the root group; `groups` nest to arbitrary depth (a subgroup has its own
`conditions` + `groups`). Chosen over a unified `items` array with a leaf/group discriminator because it is
unambiguous for an AI to author (leaves → `conditions`, subgroups → `groups`).

```jsonc
"filter": {
  "object": "Contact",          // root object (required for data elements; defaults to signal.entity for signalStart)
  "logicalOperation": "and",    // "and" | "or" (default "and")
  "conditions": [               // leaf conditions at THIS level
    { "column": "Account",      "comparison": "equal", "elementParameter": { "elementId": "task1", "parameter": "Account" } },
    { "column": "Address",      "comparison": "equal", "value": "2222" },
    { "column": "Age",          "comparison": "greater", "value": 333 },
    { "column": "Account.Code", "comparison": "equal", "value": "1" },   // lookup-traversal path (see below)
    { "column": "Email",        "comparison": "isNotNull" }
  ],
  "groups": [                   // nested subgroups, each with its own logicalOperation (recursive)
    { "logicalOperation": "or", "conditions": [
        { "column": "City.Name", "comparison": "equal", "value": "Boston" },
        { "column": "City.Name", "comparison": "equal", "value": "New York" }
    ] }
  ]
}
```
Per condition, exactly one right-hand source: **`value`** (constant), **`processParameter`** (by name),
**`elementParameter`** (`{elementId, parameter}` — e.g. a preceding element's output), or **`expression`**
(raw `[#…#]` token, advanced); `isNull`/`isNotNull` take no right side. `comparison` token →
`ComparisonType` (equal/notEqual/greater/greaterOrEqual/less/lessOrEqual/contains/startWith/endWith/
isNull/isNotNull).

### Column paths (filtering on a related table via a lookup) — REQUIRED
`column` is a **dot-path** that may traverse lookups into a related object, exactly as the designer's column
picker does: `"Account.Code"` = on `Contact`, follow the `Account` lookup → the `Code` column of `Account`;
`"City.Name"`, `"Account.Owner.Name"`, etc. (multi-hop). The server **walks the path** against the root
`EntitySchema` (`EntitySchemaColumn.ReferenceSchema` / `ReferenceSchemaUId` per hop) and uses the
**terminal** column's `DataValueType` to drive everything: the item-level `dataValueType`, the constant's
`dataValueType`, and Compare-vs-In (terminal lookup → `InFilter` + `referenceSchemaName`; terminal scalar →
`CompareFilter`). `columnPath` in the serialized filter is the path verbatim (`"Account.Code"`); a path
whose terminal column is scalar carries no `referenceSchemaName`. (Confirmed against the designer capture
with `Account.Code = "1"`, `dataValueType:1` text, `filterType:1` Compare.)

### Where it attaches
- **Build:** `ProcessElementDescriptor.Filter` (a data element or a `signalStart` element carries `filter`).
- **Modify:** a new op **`setFilter`** `{ op, elementId, filter }` (and `clearFilter` `{ op, elementId }`).
- clio forwards the descriptor as **opaque JSON** (it already does — `CreateBusinessProcessCommand`
  parses to `JsonObject` and forwards), so **no new clio DTOs for build/modify** — only guidance/prompt/docs
  + the `describe` read-back DTO.

## 5. Server design (package `clioprocessbuilder`)

New collaborator **`Filters/IProcessFilterService` + `ProcessFilterService`** (mirrors the existing
service-per-concern architecture; injected via the composition root). Plus small serialization DTOs in
`Contracts/` and a `FilterDescriptor` in `ProcessDescriptorContracts.cs`.

1. **`FilterDescriptor` / `FilterConditionDescriptor`** (contracts) — the shape in §4.
2. **`ProcessFilterService.BuildWrapperJson(schema, FilterDescriptor)`** →
   - resolve the root `EntitySchema` (via `EntitySchemaManager`) and, per condition, the column's
     `DataValueType` (drives Compare-vs-In and the numeric `dataValueType`s);
   - build the **edit-data** DTO tree (with className/key/captions/displayValue), serialize it
     (`DataContractJsonSerializer` or our own `JsonConvert`), then derive the **lean** tree (strip
     UI-only fields) and serialize it; wrap both → wrapper JSON string;
   - parameter references reuse `ProcessSchemaParameter.GetMetaPath()` (process param resolved on `schema`,
     element param resolved on the referenced element) for the token; `Id` = fresh GUID.
3. **Apply:**
   - data elements → set the `DataSourceFilters` element parameter `SourceValue` (ConstValue = wrapper
     JSON) — reuse the `ProcessMappingService` assignment idiom so `schema.Mappings` auto-syncs;
   - signal start → set `ProcessSchemaStartSignalEvent.EntityFilters = wrapper JSON` + `HasEntityFilters`
     semantics if needed.
   Wire into `ProcessGraphBuilder`/element handlers for build, and a `setFilter` case in
   `ProcessOperationExecutor` for modify.
4. **Describe read-back** — `ProcessDescriber` decodes the wrapper back to the high-level
   `filter` (object + conditions) for verification; new `DescribeFilterInfo` on `DescribeProcessElement`.

### clio side (forwarding + surface)
- **No build/modify DTOs** (opaque JSON forward). 
- `describe-process`: add **`filter`** to `DescribedElement` (`DescribedFilter`/`DescribedCondition`),
  else the command strips it on re-serialize (known gotcha — see param-types handoff §4.3).
- MCP: update `CreateBusinessProcessTool` / `ModifyBusinessProcessTool` `[Description]`, the prompts, and
  `ProcessModelingGuidanceResource` (teach the `filter` block + `setFilter` op). Docs:
  `help/en/{create,modify}-business-process.txt`, `docs/commands/*.md`, `Commands.md`. MCP e2e:
  filter build + describe read-back (env-gated).

## 6. Phasing (fits the ~6-day estimate)

1. **Filter engine + Signal start** (highest value, no Task-12 dependency): `ProcessFilterService`,
   wrapper builder (both reps), column-type resolution, constant + process-param + element-param sources,
   AND/OR, the common comparisons; apply to `signalStart.EntityFilters`; describe read-back; server tests.
2. **Read data filters** (+ the minimal target-object wiring from §3): apply to `DataSourceFilters`;
   end-to-end verify on krestov-test; tests.
3. **Modify/Delete/Add data**: same `DataSourceFilters` mechanism per element (mostly element plumbing).
4. **clio surface**: guidance/prompt/docs/describe-DTO/e2e.

Nested groups, `between`, `in (multiple)`, and date-relative filters are incremental on top of the engine.

## 7. Risks / open questions

- **Lookup right-hand value as a constant** — the capture's lookup case referenced a parameter. A
  constant lookup value (a record Id) likely needs `dataValueType:10` + the record Guid in an InFilter
  `rightExpressions[]`; confirm against a designer capture before shipping that variant.
- **`serializedFilterEditData` exactness** — the designer may include extra UI fields for some column
  types (dates, lookups). Verify each new column type against a capture; the runtime tolerates extra
  fields, but designer re-edit is stricter.
- **Root-object wiring for Read data** — confirm the minimal set of ReadData params to set so the filter
  applies (`ResultEntity` ref + result-mode) without pulling in all of Task 12.
- **`HasEntityFilters` / empty-filter** — DONE: `ApplyFilter` sets `HasEntityFilters = !IsEmptyFilter(filter)`
  on a signal start (a non-empty filter enables it; an empty one leaves it false), mirroring the designer.
  Without the flag the runtime ignores the filter (found + fixed in live verify).
- **Date / DateTime / Time constant format — UNVERIFIED.** `ConvertConstant` passes date constants through
  as strings; the runtime deserializes a filter date via `Json.DeserializeJsonDate` (JSON-deserialize →
  `DateTime`), so a bare `"2026-06-19"` likely won't parse. Capture a designer date-filter and confirm the
  accepted value format before relying on date/time constant filters (numeric/text/boolean are verified).

## 8. Implementation deltas (as shipped — supersedes §6–§7 where they differ)

This document is the original research/design; several decisions were made during implementation. The
shipped feature differs as follows (see `.codex/workspace-diary.md` for the blow-by-blow):

- **Vocabulary (beyond §6):** negative string comparisons (`notContains` / `notStartWith` / `notEndWith`);
  relative-date / system **macros** (`Today`, `CurrentUser`, `NextNDays`(+arg), …) as a right-hand
  `FunctionExpression` (`functionType=1`); left-hand **date-parts** (`Year`/`Month`/`Day`/`Week`/`Weekday`/`Hour`)
  as a `FunctionExpression` (`functionType=3`) compared to an Integer.
- **Date/Time constant — RESOLVED** (closes the §7 "UNVERIFIED" risk): stored as a quote-wrapped LOCAL ISO
  literal `"\"yyyy-MM-ddTHH:mm:ss.fff\""` (both reps) plus a UTC `dateValue` (edit rep only); TZ from the
  user connection with a UTC fallback. Offset detection uses `DateTime.Kind` (no manual scan). Live-verified.
- **Signal-start restriction:** a `signalStart` filter may **NOT** reference a process/element parameter or a
  raw expression (the signal is evaluated before any process instance exists) — enforced server-side in
  `ProcessFilterApplier`, mirroring the designer hiding the "select parameter" option. Constants / macros /
  date-parts / `isNull` only. Data-operation element filters keep parameter references.
- **Architecture:** `ProcessFilterService` is a PURE serializer (descriptor → JSON, no element knowledge); the
  element-aware apply (EntityFilters-vs-DataSourceFilters storage routing + the signal restriction) lives in
  `ProcessFilterApplier`. `ProcessDesigner` is a thin facade over per-use-case handlers
  (`ProcessBuildHandler` / `ProcessModifyHandler`) + a shared `ProcessDesignGuard`; package files are grouped
  by concern folders (`Design/`, `Filters/`, `Layout/`, …).
- **Core reuse (refines §2's "we emit it ourselves"):** the wire numeric codes now bind to the platform enums
  (`FilterComparisonType`, `Terrasoft.Core.Entities.Filters.*`, `EntitySchemaQueryExpressionType`,
  `LogicalOperationStrict`), and column-path resolution reuses `EntitySchema.FindSchemaColumnByPath`. A
  DataContract-based serialization was evaluated and **rejected** — the designer edit rep has client-only
  fields (`className` / `dateValue` / `displayValue`) absent from `Terrasoft.Nui.ServiceModel.DataContract.Filters`.
- **Scope today:** only the `signalStart` filter is end-to-end usable; data-task filters (Read / Add / Modify /
  Delete) serialize but their target-object config is not buildable yet (Tier 3 / ENG-91850).
- **Status:** implemented; unit + MCP e2e covered; live-verified on krestov-test (dates, macros, date-parts,
  signal guard).
