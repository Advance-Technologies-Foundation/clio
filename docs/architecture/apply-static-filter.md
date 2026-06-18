# apply-static-filter — architecture

`apply-static-filter` is an **entity-level Freedom UI business-rule action** that restricts a lookup
field to the set of records matching a static ESQ filter. The filter is supplied through a friendly,
language-neutral contract; clio validates it, builds the platform ESQ envelope **locally** (no runtime
dependency on CrtCopilot / CrtComponentCopilot), and writes it as add-on metadata on the entity schema.

- **Tool:** `create-entity-business-rules` (MCP), action `type: "apply-static-filter"`.
- **Scope:** entity-level — applies everywhere the lookup is used, not page-scoped.
- **Not** a page `filterConfig` / `staticFilters` edit, **not** field visibility, **not** a data query.

---

## C4 — Level 1: System Context

```mermaid
C4Context
title apply-static-filter — System Context

Person(dev, "No-code dev / AI agent", "NL: 'limit Assignee to contacts where …'")
System(host, "Claude Code (AI host)", "Loads skills, calls MCP tools")
System(clio, "clio", "CLI + MCP server. Builds the ESQ envelope LOCALLY")
System_Ext(creatio, "Creatio platform", "DataService, EntitySchemaDesignerService, add-on schema store")
System_Ext(copilot, "CrtCopilot / CrtComponentCopilot", "Porting source (design-time). NO runtime dependency")

Rel(dev, host, "NL prompt")
Rel(host, clio, "MCP: create-entity-business-rules, get-guidance, get-entity-schema-properties, odata-read")
Rel(clio, creatio, "Schema/lookup reads, add-on metadata write", "HTTPS")
Rel(clio, copilot, "Ported conversion logic + prompt patterns", "design-time only")
```

---

## C4 — Level 2: Containers (inside clio)

```mermaid
C4Container
title apply-static-filter — Containers (clio)

Person(dev, "No-code dev / AI agent")
System_Boundary(clio, "clio") {
  Container(mcp, "MCP Server / Tools", ".NET", "create-entity-business-rules, get-tool-contract, get-guidance, get-entity-schema-properties, odata-read, update-page")
  Container(guide, "Guidance Resources", "text", "BusinessRulesGuidanceResource, PageModificationGuidanceResource — routing + disambiguation")
  Container(svc, "BusinessRule Domain/Service", ".NET", "EntityBusinessRuleService, BusinessRuleMetadataConverter")
  Container(filter, "Static-filter pipeline", ".NET", "Deserializer -> Structural -> SchemaAware -> LocalEsqFilterBuilder")
  Container(prov, "Schema/Lookup providers", ".NET", "FilterSchemaProvider, LookupValueResolver, CreatioDataValueType")
}
System_Ext(creatio, "Creatio web services", "EntitySchemaDesignerService.svc, DataService/SelectQuery, AddonSchemaDesigner")

Rel(dev, mcp, "MCP calls")
Rel(mcp, guide, "get-guidance / tool [Description] steer the choice")
Rel(mcp, svc, "create-entity-business-rules -> Create()")
Rel(svc, filter, "validate + build envelope")
Rel(svc, prov, "schema, lookup-Id resolve")
Rel(filter, prov, "columns/types, display -> GUID")
Rel(prov, creatio, "schema fetch / SelectQuery", "HTTPS")
Rel(svc, creatio, "AppendRule (add-on metadata)", "HTTPS")
```

---

## C4 — Level 3: Components (apply-static-filter pipeline)

```mermaid
C4Component
title apply-static-filter — Components

Container_Boundary(svc, "BusinessRule Service") {
  Component(tool, "CreateEntityBusinessRuleTool", "MCP tool", "ToBusinessRule(); filter stays a raw JsonElement")
  Component(esvc, "EntityBusinessRuleService", "service", "orchestrates Create()")
  Component(ctx, "StaticFilterContextFactory", "factory", "bundles SchemaProvider + LookupResolver")
  Component(val, "BusinessRuleValidator", "validator", "single-action; targetAttribute is a Lookup")
  Component(conv, "BusinessRuleMetadataConverter", "converter", "rootSchemaName = ref schema; BVE1 escape-once")
}
Container_Boundary(fp, "Static-filter pipeline") {
  Component(deser, "StaticFilterDeserializer", "Phase 0", "JSON shape + aggregationType/Column/Value")
  Component(struct, "StaticFilterStructuralValidator", "Phase 1", "tokens; backward shape; aggregation cross-field")
  Component(schema, "SchemaAwareFilterValidator", "Phase 2", "columnPath/types; aggregationColumnPath numeric")
  Component(build, "LocalEsqFilterBuilder", "Phase 3", "emits ESQ: compare/in/isnull/macros/EXISTS(.Id)/AGG")
}
Container_Boundary(pr, "Providers") {
  Component(fsp, "FilterSchemaProvider", "schema", "EntitySchemaDesignerService.svc, cached")
  Component(lkp, "LookupValueResolver", "resolver", "DataService/SelectQuery; no-match/ambiguous -> actionable error")
  Component(dvt, "CreatioDataValueType", "source-of-truth", "type codes/kinds")
}
Component(addon, "BusinessRuleAddonService", "persist", "AppendRule -> schema")

Rel(tool, esvc, "Create(request)")
Rel(esvc, ctx, "Create(pkgUId, rootSchema)")
Rel(ctx, fsp, "init")
Rel(ctx, lkp, "new")
Rel(esvc, val, "ValidateEntity()")
Rel(val, deser, "Deserialize")
Rel(val, struct, "Validate")
Rel(val, schema, "Validate")
Rel(esvc, conv, "ToEntityMetadata()")
Rel(conv, build, "Build(group, rootSchema)")
Rel(build, dvt, "type codes")
Rel(build, lkp, "display -> GUID")
Rel(schema, fsp, "columns/types")
Rel(esvc, addon, "AppendRule()")
```

---

## Dynamic view — runtime sequence

```mermaid
sequenceDiagram
  autonumber
  actor U as AI agent
  participant T as create-entity-business-rules (MCP)
  participant S as EntityBusinessRuleService
  participant P as FilterSchemaProvider
  participant V as Validator (Phase 0-2)
  participant B as LocalEsqFilterBuilder (Phase 3)
  participant R as LookupValueResolver
  participant C as Creatio

  Note over U: (1) routing (SKILL.md/guidance) -> apply-static-filter<br/>(2) discovery: get-app-info / get-entity-schema-properties / odata-read
  U->>T: rule{targetAttribute, filter}  (rootSchemaName NOT sent)
  T->>S: Create()
  S->>P: GetAttributes / init schema
  P->>C: EntitySchemaDesignerService.svc (cache)
  Note over S: rootSchemaName = targetAttribute.ReferenceSchemaName
  S->>V: Phase0 Deserialize -> Phase1 Structural -> Phase2 SchemaAware
  alt validation fails
    V-->>U: error (unknown column / bad token / aggregation cross-field)
  else ok
    S->>B: Build(group, rootSchema)
    B->>R: display-name -> GUID  (non-GUID / localized values)
    R->>C: DataService/SelectQuery
    Note over B: EXISTS -> columnPath+'.Id', dvt=Integer<br/>AGG(COUNT/SUM/...) -> SubQuery + AggregationQueryExpression<br/>macros -> FunctionExpression
    B-->>S: ESQ envelope (JSON)
    S->>C: AppendRule -> BVE1 (escape-once) into schema
    C-->>U: BusinessRule_xxxx created
  end
```

---

## End-to-end flow (current state)

```mermaid
flowchart TD
    P["USER PROMPT (любой язык)"] --> S1

    subgraph S1["① ВЫБОР ИНСТРУМЕНТА (routing)"]
        direction TB
        R["Идиомы «limit/restrict the «Field» to … /<br/>show only «records» that … / business entity rule»<br/>→ MCP create-entity-business-rules (apply-static-filter)"]
        R --> R1["«ограничить записи lookup» ≠ page filterConfig/staticFilters<br/>(не редактировать body.js)"]
        R --> R2["«show the «Field» only for «records» where …»<br/>= ОГРАНИЧЕНИЕ записей, не visibility (не hide/show)"]
        R --> R3["«restrict lookup» ≠ data-query (не odata/SQL-отчёт)"]
    end

    S1 --> S2

    subgraph S2["② DISCOVERY (обязателен, no-assumptions)"]
        direction TB
        D1["get-app-info / find-entity-schema<br/>→ entity, targetAttribute, его reference-схема"]
        D2["get-entity-schema-properties / dataforge-get-table-columns<br/>→ подтвердить КАЖДУЮ колонку и тип против ЭТОГО окружения<br/>(имена/наличие/типы не предполагать)"]
        D3["odata-read → резолв НЕ-GUID значений lookup<br/>в реальный Id (в т.ч. локализованных)"]
        D1 --> D2 --> D3
    end

    S2 --> S3["③ FRIENDLY-КОНТРАКТ<br/>{ type: apply-static-filter, targetAttribute: «Lookup»,<br/>filter: { logicalOperation: AND/OR, filters[], groups[], backwardReferenceFilters[] } }<br/><b>rootSchemaName НЕ передаётся</b> → выводится из targetAttribute.ReferenceSchemaName"]

    S3 --> MCP{{"── граница MCP → код clio ──"}}
    MCP --> S4["④ BusinessRuleTool.ToBusinessRule()<br/>(filter остаётся сырым JsonElement)"]
    S4 --> S5

    subgraph S5["⑤ EntityBusinessRuleService.Create() — оркестратор"]
        direction TB
        A["a. ValidateCreateRequest (package/entity/rule)"]
        B["b. packageResolver.ResolveUId"]
        C["c. attributeProvider.GetAttributes<br/>→ rootSchemaName = targetAttribute.ReferenceSchemaName"]
        Dd["d. StaticFilterContextFactory<br/>→ FilterSchemaProvider(init) + LookupValueResolver"]

        subgraph E["e. ВАЛИДАЦИЯ (дёшево, до сети-резолва)"]
            direction TB
            E0["Фаза 0 · Deserializer<br/>JSON-форма, типы, обязательные поля"]
            E1["Фаза 1 · StructuralValidator<br/>токены, unary-правило, backward-форма, агрегация cross-field"]
            E2["Фаза 2 · SchemaAwareValidator<br/>columnPath на ref-схеме, тип vs сравнение, 1:N, numeric для SUM/…"]
            E0 --> E1 --> E2
        end

        F["f. Фаза 3 · LocalEsqFilterBuilder.Build → ESQ-envelope<br/>• display-name → GUID (DataService) только если есть НЕ-GUID значение<br/>• → BVE1 (escape-once JSON-строка) в BusinessRuleValueExpression.value"]
        G["g. AppendRule → запись add-on метадаты в схему<br/>(persist, BuildConfiguration синхронно)"]

        A --> B --> C --> Dd --> E --> F --> G
    end

    S5 --> DONE(["BusinessRule_xxxx создан"])

    classDef done fill:#efe,stroke:#5a5;
    class DONE done;
```

<details>
<summary>Plain-text fallback (same flow)</summary>

```
USER PROMPT (any language)
   |
   v
(1) TOOL SELECTION (routing)
    Idioms "limit/restrict the <Field> to ... / show only <records> that ... / business entity rule"
    route to MCP create-entity-business-rules (apply-static-filter).
    Disambiguations baked into guidance / skill / tool descriptions:
      - "restrict the records a lookup offers" != page filterConfig/staticFilters (do not edit body.js)
      - "show the <Field> only for <records> where ..." = record RESTRICTION, not visibility (no hide/show)
      - "restrict lookup" != data query (no odata/SQL report)
   |
   v
(2) DISCOVERY (mandatory, no assumptions)
    - get-app-info / find-entity-schema -> entity, targetAttribute, its reference schema
    - get-entity-schema-properties / dataforge-get-table-columns -> confirm EVERY column + type
      against THIS environment (never assume names / existence / type)
    - odata-read -> resolve non-GUID lookup values to a real Id (incl. localized)
   |
   v
(3) FRIENDLY CONTRACT
    { "type":"apply-static-filter", "targetAttribute":"<Lookup>",
      "filter": { "logicalOperation":"AND|OR",
                  "filters":[...], "groups":[...], "backwardReferenceFilters":[...] } }
    rootSchemaName is NOT sent — inferred from targetAttribute.ReferenceSchemaName.
   |
   v  -- MCP boundary -> clio code --
(4) BusinessRuleTool.ToBusinessRule()         (filter stays a raw JsonElement)
   |
   v
(5) EntityBusinessRuleService.Create()  -- orchestrator
    a. ValidateCreateRequest (package/entity/rule)
    b. packageResolver.ResolveUId
    c. attributeProvider.GetAttributes -> rootSchemaName = targetAttribute.ReferenceSchemaName
    d. StaticFilterContextFactory -> FilterSchemaProvider(init) + LookupValueResolver
    e. VALIDATION (cheap, before network resolve):
         Phase 0  Deserializer        -- JSON shape, types, required fields
         Phase 1  StructuralValidator -- tokens, unary rule, backward shape, aggregation cross-field
         Phase 2  SchemaAwareValidator-- columnPath on ref schema, type vs comparison, 1:N, numeric for SUM/...
    f. Phase 3  LocalEsqFilterBuilder.Build -> ESQ envelope
         - resolve display-name -> GUID (DataService) only when a non-GUID value is present
         - -> BVE1 (escape-once JSON string) in BusinessRuleValueExpression.value
    g. AppendRule -> write add-on metadata to schema (persist; BuildConfiguration synchronous)
```

</details>

---

## Responsibilities (where things are checked)

| Phase | Component | Responsibility | Schema-aware? | Network? |
|-------|-----------|----------------|---------------|----------|
| 0 PARSE | `StaticFilterDeserializer` | JSON shape: `logicalOperation`, `columnPath`, `comparisonType`; numeric `aggregationType/Column/Value` | no | no |
| 1 STRUCTURAL | `StaticFilterStructuralValidator` | AND/OR; 14 leaf tokens; unary (IS_NULL without value); value XOR valueMacros; backward `[Schema:Column]`; aggregation: COUNT without column / SUM..MAX with column, relational comparison, value required; recursion into groups[] | no | no |
| 2 SCHEMA-AWARE | `SchemaAwareFilterValidator` | columnPath resolves on ref schema; type vs comparison; forward only through Lookup; backward 1:N; `aggregationColumnPath` numeric | yes | cached |
| 3 BUILD | `LocalEsqFilterBuilder` | emit ESQ; EXISTS -> `.Id` + Integer; aggregation -> SubQuery/AggregationQueryExpression; macros -> FunctionExpression; display -> GUID | yes | yes (non-GUID only) |
| persist | `BusinessRuleAddonService` | write add-on metadata | yes | yes |

---

## Friendly-contract capability surface

**Leaf comparisons:** `EQUAL, NOT_EQUAL, IS_NULL, IS_NOT_NULL, GREATER, GREATER_OR_EQUAL, LESS,
LESS_OR_EQUAL, CONTAIN, NOT_CONTAIN, START_WITH, NOT_START_WITH, END_WITH, NOT_END_WITH`.

| Capability | How |
|------------|-----|
| Constant | `{columnPath, comparisonType, value}` (value type per schema) |
| Lookup by value | GUID directly / display-name resolved; array of strings + EQUAL/NOT_EQUAL = multi-value IN |
| Forward path | `Country.Name`, `Account.CreatedOn` — through a Lookup chain |
| Nested groups | `groups[]` for (A AND B) OR (A AND C) |
| Backward EXISTS / NOT_EXISTS | `{referenceColumnPath:"[Child:Link]", comparisonType:"EXISTS"}` (+ optional `filter` on child) |
| Backward aggregation | `{referenceColumnPath, aggregationType:"COUNT/SUM/AVG/MIN/MAX", comparisonType:"GREATER...", aggregationValue:N}`; SUM..MAX require numeric `aggregationColumnPath` |
| Relative dates | `valueMacros`: Today/Yesterday/Tomorrow, Prev/Cur/Next Week/Month/Quarter/HalfYear/Year/Hour; N-style (`NextNDays` + `valueMacrosArgument`) |
| "birthday today/tomorrow" | `valueMacros:"DayOfYearTodayPlusDaysOffset"`, offset 0/1, on the birth-date column |
| "age = / < / between" | if an age column exists, filter it directly; otherwise a birth-date range (dates computed) |
| Current user | `valueMacros:"CurrentUser"/"CurrentUserContact"` on a Lookup + EQUAL/NOT_EQUAL |
| Multilingual | contract is language-neutral; resolve localized lookup **values** via odata-read |

### Example payloads

Constant + forward path:
```json
{ "type": "apply-static-filter", "targetAttribute": "UsrContact",
  "filter": { "logicalOperation": "AND",
    "filters": [ { "columnPath": "Account.Country.Name", "comparisonType": "EQUAL", "value": "United States" } ] } }
```

Backward EXISTS:
```json
{ "type": "apply-static-filter", "targetAttribute": "Account",
  "filter": { "logicalOperation": "AND",
    "backwardReferenceFilters": [ { "referenceColumnPath": "[Contact:Account]", "comparisonType": "EXISTS" } ] } }
```

Backward COUNT aggregation:
```json
{ "type": "apply-static-filter", "targetAttribute": "UsrAssignee",
  "filter": { "logicalOperation": "AND",
    "backwardReferenceFilters": [
      { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT",
        "comparisonType": "GREATER", "aggregationValue": 10 } ] } }
```

Relative-date macros + direct age:
```json
{ "type": "apply-static-filter", "targetAttribute": "UsrAssignee",
  "filter": { "logicalOperation": "AND",
    "filters": [
      { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "valueMacros": "CurrentYear" },
      { "columnPath": "Age", "comparisonType": "EQUAL", "value": 30 } ] } }
```

---

## Key invariants

- `rootSchemaName` is always `targetAttribute.ReferenceSchemaName`, never accepted from the caller.
- `columnPath` is rooted on the lookup's reference schema, not on the rule entity.
- Validation (phases 0-2) runs before the expensive DataService value resolve (phase 3).
- apply-static-filter is exactly one action per rule; the condition group may be empty (unconditional
  filter) or gated (`WHEN X=Y -> filter Z`).
- Backward `referenceColumnPath` is the bare `[Child:Link]` form (no `.Id`); the builder appends `.Id`.
- The ESQ envelope is stored as a BVE1 escape-once JSON string.
- No schema assumptions: column names/existence/types are confirmed against the real environment; the
  schema-aware validator rejects unknown columns and type-mismatched comparisons.

---

## Reuse from CrtCopilot / CrtComponentCopilot

There is **no runtime dependency** on either package — the logic was ported locally into
`clio/Command/BusinessRules/Filters/Esq/`. Reuse is at the level of shape/logic and numeric contracts,
plus prompt patterns.

| Source | What | C4 level | Landed in |
|--------|------|----------|-----------|
| **CrtCopilot** `LlmEsqFiltersConverter` | ESQ conversion, envelope shape, aggregation branch, class-names, macros | L3 Code | `LocalEsqFilterBuilder` + `EsqEnvelopeDtos` / `EsqFilterClassNames` / `MacrosCatalog` (ported, no runtime dep) |
| **Terrasoft.\*.dll** | numeric enums (FilterType / Comparison / Aggregation / Expression / Function / DataValueType) | L3 Code | `EsqFilterEnums` / `CreatioDataValueType` — values verified by reflection |
| **CrtComponentCopilot** widget prompts | backward/forward/lookup resolution patterns, discovery chain | L1->L2 (guidance) | `BusinessRulesGuidanceResource` + ToolContract examples/validators |

The L1 edge `clio -> CrtCopilot / CrtComponentCopilot` is a **design-time dashed link**: only the
conversion logic ("in spirit") and the prompt patterns were carried over; zero runtime coupling.
