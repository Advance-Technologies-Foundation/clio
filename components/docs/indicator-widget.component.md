# How to Add an Indicator Widget (`crt.IndicatorWidget`) to a Freedom UI Page

> Audience: code agent inserting a `crt.IndicatorWidget` into a Creatio Freedom UI page schema.
>
> `crt.IndicatorWidget` is a KPI tile that displays a single aggregated numeric metric with an optional
> color/icon accent and a trend comparison badge.
>
> **Scope.** This document is the single source of truth for `crt.IndicatorWidget`. It covers:
> - the **generation contract** — the runtime config shapes valid when emitting the widget into a page schema; and
> - the **authoring guidance** — how to translate a metric intent into that contract (the recipe and workflow).
>
> **Out of scope** (defined elsewhere):
> - Type-level schemas (`inputs`, `outputs`, `default`, `values`) → `ComponentRegistry.json`.
> - The ESQ filter/query contract and the `execute-esq` test tool → §7 (References).

## Authoring workflow

1. Inspect the target page (`list-pages` / `get-page`) and choose the container the widget belongs in.
2. Resolve the data-source entity (`schemaName`) and the aggregate column from the requirement (see §2.2).
3. Build the filter with the `esq-filters` guidance.
4. Before saving, run the widget's own aggregation over its filter group as a SelectQuery with `execute-esq` against the target environment, and confirm the returned value matches the intended metric. This catches wrong column paths, lookup values and date macros before they reach the page.
5. Read the page body back (`verify`) to confirm the saved payload.

Do **not** replace a static aggregate/filter requirement with business rules or JavaScript handlers that load
records manually — the widget queries and aggregates declaratively at render time.

## Metadata

- **Category**: chart
- **Container**: no
- **Parent types**: `crt.GridContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section          | What you add                                                                                       |
| --- | ---------------- | -------------------------------------------------------------------------------------------------- |
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.IndicatorWidget"` and the full `config` object. **Always present.** |

Naming: `IndicatorWidget_<id>` view element name, where `<id>` is any short unique slug.

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

Add an `insert` op to `viewConfigDiff` with a unique `name` (`IndicatorWidget_<id>`), the target `parentName`,
`propertyName: "items"`, an `index`, and `values` carrying `layoutConfig` (when inside a `crt.GridContainer`),
`type: "crt.IndicatorWidget"`, and the full `config` object (`title`, `data.providing`, `data.formatting`, `text`,
`layout`, `theme`). See §2.4 for a complete payload.

### 2.2 Aggregation

The aggregate lives in `config.data.providing.aggregation.column.expression`; the full expression shape and enum
values are owned by the `esq` guidance (§7), and §2.4 shows a complete `COUNT(Id)` example. Widget rule: `COUNT`
aggregates `Id`; `SUM`/`AVG`/`MIN`/`MAX` use the explicit business column, never `Id`.

### 2.3 Filters

`config.data.providing.filters.filter` is a runtime ESQ filter group; the filter/leaf contract itself is owned by
the `esq-filters` guidance (§7). Widget-specific binding:

- The group lives at `config.data.providing.filters.filter`; the surrounding `filters` object also carries
  `filterAttributes: []`.
- `filter.rootSchemaName` MUST equal `providing.schemaName`.
- Keep the envelope even with no conditions — an empty filter is `"items": {}` with `filterType: 6`,
  `logicalOperation: 0`, `isEnabled: true`, and `rootSchemaName` intact (never removed or `null`; see §2.4).
- A lookup condition is wrong as a bare GUID: its value must be an object (`{ Id, value, displayValue, Name }`) inside an In filter (`filterType: 4`), not a scalar `filterType: 1` value.

### 2.4 Example

A complete Account-count tile (`COUNT(Id)`, no filter):

```jsonc
{
  "operation": "insert",
  "name": "IndicatorWidget_abc123",
  "parentName": "Main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 3, "rowSpan": 3 },
    "type": "crt.IndicatorWidget",
    "config": {
      "title": "#ResourceString(IndicatorWidget_abc123_title)#",
      "data": {
        "providing": {
          "attribute": "IndicatorWidget_abc123_Data",
          "schemaName": "Account",
          "filters": { "filter": { "items": {}, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Account" }, "filterAttributes": [] },
          "aggregation": { "column": { "orderDirection": 0, "orderPosition": -1, "isVisible": true, "expression": { "expressionType": 1, "functionArgument": { "expressionType": 0, "columnPath": "Id" }, "functionType": 2, "aggregationType": 1, "aggregationEvalType": 2 } } }
        },
        "formatting": { "type": "number", "decimalSeparator": ".", "decimalPrecision": 0, "thousandSeparator": "," }
      },
      "text": { "template": "#ResourceString(IndicatorWidget_abc123_template)#", "metricMacros": "{0}", "fontSizeMode": "medium", "labelPosition": "above-under" },
      "layout": { "color": "steel-blue", "icon": { "iconName": "work-icon" } },
      "theme": "without-fill"
    },
    "visible": true
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for `crt.IndicatorWidget` are
in `ComponentRegistry.json` under `componentType: "crt.IndicatorWidget"`. This guide covers only assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// IndicatorWidgetConfig (config root)
interface IndicatorWidgetConfig {
  title: string;                  // use #ResourceString(...)# for localized text
  data: WidgetDataConfig;         // { providing, formatting, comparisonProviding? }
  text: {
    template: string;             // e.g. "#ResourceString(...)#"; {0} is the metric placeholder
    metricMacros: string;         // usually "{0}"
    fontSizeMode: 'small' | 'medium' | 'large';
    labelPosition: 'above-under' | 'inline';
  };
  layout: {
    color: WidgetColor;           // e.g. "steel-blue" | "green" | "cadmium-red" | "dark-blue"
    icon?: { iconName: string };
  };
  theme: WidgetTheme;             // "without-fill" | "full-fill"
  comparison?: IndicatorComparisonConfig | null;
}

// WidgetDataConfig.providing — datasource binding
interface WidgetProvidingConfig {
  attribute: string;              // e.g. "IndicatorWidget_abc123_Data"
  schemaName: string;
  filters: { filter: FilterConfig; filterAttributes: FilterAttributeConfig[] };
  aggregation: { column: AggregationColumn };
  dependencies?: Array<{ attributePath: string; relationPath: string }>;
}
```

---

## 5. Common pitfalls

1. **`attribute` ≠ datasource key.** It must equal the datasource key minus the trailing `DS`: `IndicatorWidget_abc_Data` → `IndicatorWidget_abc_DataDS`.
2. **Wrong aggregated column.** `COUNT` → `Id`; `SUM`/`AVG`/`MIN`/`MAX` → the business column, never `Id`.
3. **Missing `filters.filter.rootSchemaName`.** It must match `schemaName`; omitting it makes the server-side filter parser fail silently.
4. **Unknown `layout.color`.** Use `WidgetColor` keywords (`"steel-blue"`, `"green"`, `"cadmium-red"`); arbitrary CSS strings are ignored.
5. **`text.template` without `{0}`.** It must include `{0}` (the `metricMacros` placeholder) or only static text renders.
6. **`layoutConfig` missing in a `crt.GridContainer`.** Without row/column/colSpan/rowSpan the widget overlaps other children at (1,1).
7. **Dropping the filter-group envelope.** Keep `filterType: 6`, `rootSchemaName`, `logicalOperation`, `isEnabled` even for one condition or none — removing it breaks the server-side parser.

---

## 6. Quick checklist

- [ ]  `insert` op in `viewConfigDiff` with `type: "crt.IndicatorWidget"`, unique `name`, valid `parentName`, `propertyName: "items"`, and an `index`.
- [ ]  Aggregated column matches intent: COUNT → `Id`, SUM/AVG/MIN/MAX → business column.
- [ ]  Filter group keeps the envelope (`filterType: 6`, `rootSchemaName` = `schemaName`); empty filter is `items: {}`.
- [ ]  Filter + aggregation validated by running them through `execute-esq` before saving.
- [ ]  `config.text.template` contains `{0}`; `config.title` is a `#ResourceString(...)#`.
- [ ]  `layoutConfig` provided inside a `crt.GridContainer`; `visible: true` set in `values`.

---

## 7. References

- **`esq-filters`** — the complete ESQ filter contract for the filter group in §2.3.
- **`esq`** — the SelectQuery envelope, columns/select, and the aggregation-expression shape and enum values.
- **`execute-esq`** (tool) — runs a raw ESQ SelectQuery against an environment; use it to test a widget by running
  its own aggregation over its filter group before saving the page.
- **`page-modification`** — replacing-schema and minimal-body write rules for the `viewConfigDiff`.
- **`page-schema-resources`** — adding the user-visible titles, labels, and hints referenced via `#ResourceString(...)#`.
