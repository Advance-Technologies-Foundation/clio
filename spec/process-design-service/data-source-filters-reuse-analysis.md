# ENG-91842 — Data source filters: reuse analysis & layering decision

> Research record for ENG-91842 ("Support Data source filters in element parameters"). Purpose: capture
> the investigation into reusing the **existing clio filter subsystem** so nobody has to re-research it.
> Audience: anyone continuing the data-source-filter work (clio + the `clioprocessbuilder` package).
>
> TL;DR — **A filter serializer for the process designer must live in the `clioprocessbuilder` package
> (Option B), NOT in clio (Option A).** clio cannot build the process/element-parameter reference tokens,
> and moving serialization to clio would break the package as a standalone "non-visual designer" for any
> other REST consumer. We reuse the mature clio filter code as a *reference* (contract shape, comparison
> vocabulary, proven feature logic), not as a runtime dependency.

## 1. Context

ENG-91842 needs declarative **data source filters** on data-operation elements (Read / Add / Modify /
Delete data, via the element's `DataSourceFilters` parameter) and on the **Signal start** trigger (via
`EntityFilters`). The platform stores them as a deeply-nested, escaped `Terrasoft.FilterGroup` JSON; the
caller must express high-level intent (column, comparison, value/parameter, AND/OR groups) and the
backend must serialize it. Approach-1 principle: **the package owns all process-metadata serialization;
the caller never hand-writes filter JSON.**

While implementing, we discovered clio **already** contains a comprehensive filter subsystem. This
document records whether/how to reuse it.

## 2. The existing clio filter subsystem (found)

Location: `clio/Command/BusinessRules/Filters/` (used by business rules + `execute-esq`). It is markedly
more complete than the new process-filter engine. Inventory:

| Concern | File |
|---|---|
| High-level recursive DTO (`StaticFilterGroup` / `StaticFilterLeaf` / `StaticFilterBackwardReference`) | `BusinessRules/Filters/StaticFilterDtos.cs` |
| ESQ envelope builder (DTO → platform filter JSON) | `BusinessRules/Filters/Esq/LocalEsqFilterBuilder.cs` |
| Numeric enums (FilterType, ComparisonType, LogicalOperation, ExpressionType, Aggregation, FunctionType, QueryMacros, DatePart, DataValueType) | `BusinessRules/Filters/Esq/EsqFilterEnums.cs` |
| Envelope DTOs (Compare/In/IsNull/Exists/Aggregation/DatePart/Macros expressions) | `BusinessRules/Filters/Esq/EsqEnvelopeDtos.cs` |
| Schema-aware column-path validation/resolution | `BusinessRules/Filters/Schema/{FilterSchemaProvider,SchemaAwareFilterValidator}.cs` |
| Lookup display-name ↔ Id resolution | `BusinessRules/Filters/Schema/LookupValueResolver.cs` |
| Tokens/constants, structural validation, deserializer | `BusinessRules/Filters/{StaticFilterConstants,StaticFilterStructuralValidator,StaticFilterDeserializer}.cs` |
| AI guidance | `McpServer/Resources/{EsqFiltersGuidanceResource,BusinessRuleFiltersGuidanceResource}.cs` |

**Capabilities already implemented there** (a superset of what the new engine has):
- Recursive nested groups, per-group AND/OR.
- Full comparison set incl. negatives: Equal/NotEqual, Less/LessOrEqual/Greater/GreaterOrEqual,
  StartWith/NotStartWith, Contain/NotContain, EndWith/NotEndWith, IsNull/IsNotNull, Exists/NotExists.
- Lookup `InFilter` with **multiple** values, GUID-or-display-name input, mandatory Name/displayValue
  enrichment (the Freedom UI lookup control needs it).
- **Macros** (`Today`, `Yesterday`, `CurrentUser`, `NextNDays`, `CurrentYear`, … full `EsqQueryMacrosType`).
- **DatePart** filters (Year/Month/Day/Weekday/Hour/HourMinute) with correct `trimDateTimeParameterToDate`.
- **Aggregation / backward-reference** filters (EXISTS/NOT_EXISTS, COUNT/SUM/AVG/MIN/MAX vs threshold).
- Column-type-driven constant typing; lookup-path resolution; date/time carrier handling.

## 3. The question

Should the process data-source-filter feature **reuse** this subsystem? Two options:

- **Option A — build the filter in clio.** Reuse/extend `LocalEsqFilterBuilder` clio-side; clio sends a
  finished filter string to the package; the package only assigns it.
- **Option B — build the filter in the package.** Keep serialization in `clioprocessbuilder`; reuse the
  clio implementation as a *reference* (contract shape + vocabulary + proven feature logic).

## 4. Option A feasibility — two decisive problems

### 4.1 clio does NOT have all the information

clio *can* resolve, via **environment round-trips**, what `LocalEsqFilterBuilder` needs for ESQ:
- column metadata/types — `FilterSchemaProvider.GetColumns()` → `IEntityBusinessRuleSchemaProvider.GetSchema(name, packageUId)` (remote fetch);
- lookup display-name ↔ Id — `LookupValueResolver` via `IApplicationClient` + ESQ.

But a **process** filter's right-hand side can reference a **process parameter** or **another element's
output parameter** (e.g. the captured case `Account = "Perform task".Account`). That reference is a
meta-path token `[IsOwnerSchema:false].[IsSchema:false].[Element:{elementUId}].[Parameter:{paramUId}]`
produced by `ProcessSchemaParameter.GetMetaPath()`. **The `elementUId`/`paramUId` are generated inside the
package during `BuildProcess`** (`Guid.NewGuid()` per element/param + `SynchronizeParameters`). clio holds
only the high-level descriptor (names), not these UIds — they do not exist until the package builds the
schema. **So clio cannot construct process/element-parameter filter references** (the distinctive
process-filter case). A two-phase workaround (build → describe to read UIds → build filter → setFilter)
is fragile and round-trip-heavy.

### 4.2 The package must own serialization to stay a standalone designer

The package exposes a REST API (`BuildProcess` / `ModifyProcess`) intended as a **non-visual designer for
any caller** — clio today, but also other tools, a future UI, or integrations in another language. If
filter serialization moves to clio, the package contract must either silently not serialize a high-level
`filter` (broken for non-clio callers) or accept only a pre-serialized escaped `Terrasoft.FilterGroup`
string — forcing **every other consumer to hand-build the filter JSON**, which is exactly what Approach 1
forbids. That makes clio a mandatory middleware for filters and degrades the package's core value.

**Conclusion: Option A is wrong for the process designer.**

## 5. Decision — Option B (package owns serialization; reuse clio as reference)

Keep filter serialization in `clioprocessbuilder`. Reuse the clio implementation as the **reference
design**, not as a runtime:
1. **Align the contract & vocabulary** of the process `filter` descriptor with `StaticFilterGroup` /
   `StaticFilterLeaf` + `EsqComparisonType` (one filter dialect across clio — for AI and humans).
   Process-specific additions: right-hand `processParameter` / `elementParameter` references (which the
   ESQ/business-rule filter does not have).
2. **Port the proven feature logic** into the package: negatives, multi-value `In`, macros, date-parts,
   lookup Name/displayValue enrichment, constant typing — mirroring `LocalEsqFilterBuilder` and its
   hard-won correctness lessons (see §7), without copying the code (separate assemblies/runtimes).

### Why not share code directly
`LocalEsqFilterBuilder` is clio-side (.NET 8/10, `internal`, env-backed resolution); the package is net472
and resolves via `EntitySchemaManager` in-process. They cannot share a DLL trivially. The **only** real
code-reuse path is extracting the *pure* build logic (resolved-column-types + DTO → JSON, resolution
injected per side) into a shared **netstandard2.0** library referenced by both repos. That is a
cross-repo coupling decision; **deferred** — mirror for now.

### Output-format note (why `LocalEsqFilterBuilder` is not a drop-in even on the clio side)
It emits the inner ESQ envelope with `Filter_0`/`Group_0` item keys and **no** wrapper. A process element
needs the dual-representation wrapper `{className, serializedFilterEditData, dataSourceFilters}` with
**GUID** item keys and the designer edit-data rep (className/key/leftExpressionCaption/displayValue). So
reuse means porting the leaf/expression logic, then wrapping + re-keying for the process element.

## 6. Current implementation state (package branch `feature/ENG-91842-data-source-filters`)

**Done & unit-tested** (`Filters/ProcessFilterService.cs`, `Contracts/FilterContracts.cs`):
- High-level `FilterDescriptor` (object + logicalOperation + recursive conditions/groups).
- Dual-rep wrapper builder (serializedFilterEditData + dataSourceFilters).
- Column-path lookup traversal (`Account.Code`); Compare-vs-In; constant typing (int/float/money/bool);
  process/element-parameter & raw-expression references.
- Apply to `signalStart.EntityFilters` (+ `HasEntityFilters`) and data-element `DataSourceFilters` param.
- `setFilter`/`clearFilter` modify ops; exactly-one-source validation; empty-filter → `HasEntityFilters=false`.
- clio MCP surface (guidance/prompts/tool descriptions/docs).

**Pending (Phase 1 follow-ups):**
- **Align contract to `StaticFilter*` + `EsqComparisonType`; port negatives / multi-value In / macros /
  date-parts / lookup Name-displayValue enrichment** (this analysis's main action).
- `describe-process` filter read-back (server decode + clio `DescribedElement.filter`) + its e2e.
- Read-data (and Add/Modify/Delete) target-object config so data-task filters are end-to-end usable
  (currently only the signalStart filter is); tracked under ENG-91850.
- MCP e2e building a filtered process.
- Verify Date/DateTime/Time constant value format live (runtime parses via `Json.DeserializeJsonDate`).

## 7. Key technical facts (do not re-derive)

- **Runtime reads only `dataSourceFilters`**; `serializedFilterEditData` is for designer re-edit. Emit both.
- **Signal start requires `HasEntityFilters=true`** (meta key DZ8, defaults false; `WriteMetaData` omits it
  when false). Without it the runtime ignores `EntityFilters` and the signal fires on every change. Set it
  from filter non-emptiness (mirrors `BaseSignalEventPropertiesPage.saveEntityFilters`). *(Found in live
  verify: a clio build fired on every change; the designer's working version differed only by this flag.)*
- **Lookup filter values must carry Name/displayValue**, not an Id-only value — the Freedom UI lookup
  control fails to render otherwise (see `LocalEsqFilterBuilder.BuildLookupParameter`).
- **Numeric codes** (mirror `EsqFilterEnums.cs` / `Terrasoft.Core.Entities.Filters`): FilterType
  Compare=1/IsNull=2/Between=3/In=4/Exists=5/Group=6; ComparisonType Equal=3, NotEqual=4, Less=5,
  LessOrEqual=6, Greater=7, GreaterOrEqual=8, StartWith=9, NotStartWith=10, Contain=11, NotContain=12,
  EndWith=13, NotEndWith=14, IsNull=1, IsNotNull=2, Exists=15, NotExists=16; Logical And=0/Or=1;
  ExpressionType Column=0/Function=1/Parameter=2/SubQuery=3; DataValueType Guid=0/Text=1/Integer=4/Float=5/
  Money=6/DateTime=7/Date=8/Time=9/Lookup=10/Boolean=12/**Mapping(param ref)=26**.
- **Param/element reference token** = `ProcessSchemaParameter.GetMetaPath()` (process-level:
  `[IsOwnerSchema:false].[IsSchema:false].[Parameter:{uid}]`; element-level adds `.[Element:{uid}]`),
  with `dataValueType:26` (Mapping); the right-hand value object carries `{value, displayValue, Id}`.

## 8. Pointers

- Designer captures + decoded wrapper: `spec/process-design-service/captures/readdata-filters-capture.md`.
- Filter feature design / contract / risks: `spec/process-design-service/data-source-filters-design.md`.
- Overall ProcessDesignService state: `spec/process-design-service/process-design-service-state.md`.
- clio filter subsystem: `clio/Command/BusinessRules/Filters/**`.
- Package serializer: `clioprocessbuilder/Files/src/cs/Filters/ProcessFilterService.cs` (repo
  `Advance-Technologies-Foundation/ProcessBuilder`, branch `feature/ENG-91842-data-source-filters`).
