# How to Add a Chart Widget (`crt.ChartWidget`) to a Freedom UI Page

> Audience: code agent inserting `crt.ChartWidget` into a Creatio Freedom UI page schema.
> A configurable analytics chart (doughnut, bar, line, area, scatter, funnel) with optional drill-down
> datagrid; data is driven entirely by the inline `config` object — no viewModel attributes are required
> for the chart series themselves.

## Metadata

- **Category**: chart
- **Container**: no
- **Parent types**: `crt.GridContainer` (most dashboard layouts), `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ChartWidget"` and an inline `config` object. **Always present.** |
| 2 | `modelConfigDiff` | Usually empty (`dataSources: {}`) — series data is fetched by the widget itself via `config.series[*].data.providing`. |

The `crt.CreateChartWidgetViewItemCommand` generates a `config` object with a single default series;
no `viewModelConfigDiff` entries are required for the chart data itself.

### 1.1 Naming convention

```
ChartWidget_<id>                  // view element name
ChartWidget_<id>_series_0         // resource key for the first series label
ChartWidget_<id>_SeriesData_<id2> // attribute referenced inside config.series[*].data.providing.attribute
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "AccountByIndustryWidget",
  "parentName": "DashboardGridContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 6, "rowSpan": 12 },
    "type": "crt.ChartWidget",
    "config": {
      "title": "#ResourceString(AccountByIndustryWidget_title)#",
      "color": "dark-blue",
      "theme": "without-fill",
      "series": [
        {
          "type": "doughnut",
          "label": "#ResourceString(AccountByIndustryWidget_series_0)#",
          "legend": { "enabled": false },
          "data": {
            "providing": {
              "attribute": "ChartWidget_abc123_SeriesData_xyz456",
              "schemaName": "Account",
              "filters": {
                "filter": { "filterType": 6, "isEnabled": true, "items": {}, "logicalOperation": 0 },
                "filterAttributes": []
              },
              "aggregation": {
                "column": {
                  "expression": { "expressionType": 1, "functionType": 2, "functionArgument": { "expressionType": 0, "columnPath": "Id" } }
                },
                "type": 2
              },
              "grouping": {
                "column": {
                  "expression": { "expressionType": 0, "columnPath": "Industry" }
                },
                "type": 0
              },
              "rowCount": 50
            }
          }
        }
      ]
    }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChartWidget` are in `ComponentRegistry.json` under `componentType: "crt.ChartWidget"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

The `config` input is typed `ChartWidgetConfig`. Key sub-shapes:

```ts
// ChartWidgetConfig
interface ChartWidgetConfig {
  title?: string;    // widget header label; use #ResourceString(<key>)# for localization
  color?: string;    // theme color token (e.g. "dark-blue", "green", "orange", "red")
  theme?: string;    // "without-fill" | "with-fill"
  series: SeriesConfig[];  // at least one required
}

// SeriesConfig (simplified)
interface SeriesConfig {
  type: "doughnut" | "bar" | "horizontal-bar" | "line" | "area" | "spline" | "scatter" | "funnel";
  label?: string;          // series legend label; use ResourceString macro
  color?: string;          // series color override (e.g. "BurntCoral")
  legend?: { enabled: boolean };
  data: {
    providing: {
      attribute: string;   // unique attribute name for series data (not in viewModelConfigDiff)
      schemaName: string;  // entity schema, e.g. "Account", "Contact"
      filters?: object;    // OData-style filter object
      aggregation?: object; // aggregation function config
      grouping?: object;    // grouping column config
      rowCount?: number;   // default 50
    };
  };
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — doughnut chart (from CrtCustomer360App AccountsAnalyticsDashboard)
{
  "operation": "insert",
  "name": "AccountByIndustryWidget",
  "parentName": "Main",
  "propertyName": "items",
  "index": 1,
  "values": {
    "layoutConfig": { "column": 7, "colSpan": 6, "row": 1, "rowSpan": 12 },
    "type": "crt.ChartWidget",
    "config": {
      "title": "#ResourceString(AccountByIndustryWidget_title)#",
      "color": "dark-blue",
      "theme": "without-fill",
      "series": [
        {
          "type": "doughnut",
          "label": "#ResourceString(AccountByIndustryWidget_series_0)#",
          "legend": { "enabled": false },
          "data": {
            "providing": {
              "attribute": "ChartWidget_jhrwqv6_SeriesData_ynfw9h4",
              "schemaName": "Account",
              "filters": {
                "filter": {
                  "items": {
                    "columnIsNotNullFilter": {
                      "comparisonType": 2,
                      "filterType": 2,
                      "isEnabled": true,
                      "isNull": false,
                      "trimDateTimeParameterToDate": false,
                      "leftExpression": { "expressionType": 0, "columnPath": "Industry" }
                    }
                  },
                  "logicalOperation": 0,
                  "isEnabled": true,
                  "filterType": 6,
                  "rootSchemaName": "Account"
                },
                "filterAttributes": []
              },
              "aggregation": {
                "column": {
                  "orderDirection": 0, "orderPosition": -1, "isVisible": true,
                  "expression": {
                    "expressionType": 1,
                    "functionArgument": { "expressionType": 0, "columnPath": "Id" },
                    "functionType": 2
                  }
                },
                "type": 2
              },
              "grouping": {
                "column": {
                  "expression": { "expressionType": 0, "columnPath": "Industry" }
                },
                "type": 0,
                "subFilters": {}
              },
              "rowCount": 50
            }
          }
        }
      ]
    }
  }
}
```

---

## 7. Common pitfalls

1. **`config.series[*].data.providing.attribute` is an arbitrary unique string** — it is NOT a viewModel attribute that must be declared in `viewModelConfigDiff`; the chart widget manages this data reference internally.
2. **`schemaName` in `providing` drives the data query** — get the entity schema name right; a wrong name returns no data without a visible error.
3. **`layoutConfig` is required when the parent is `crt.GridContainer`** — provide `column`, `row`, `colSpan`, `rowSpan` in integer form.
4. **`title` should use `#ResourceString(<key>)#`** — hardcoded strings are not translated; register the resource string under the widget element name + `_title`.
5. **Series `label` should also use `#ResourceString(<key>)#`** — the create command auto-generates a resource string named `<widgetName>_series_0` for the first series.
6. **`drillDown` output and `listData`/`listConfig`** — these are used together when drill-down datagrid mode is enabled; do not wire `drillDown` without also providing `listConfig` and `listData` attributes.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ChartWidget"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `config.series` has at least one entry with `type`, `label`, and `data.providing`.
- [ ] `data.providing.attribute` is a unique string (GUID-slug recommended).
- [ ] `data.providing.schemaName` matches a real entity schema name.
- [ ] `layoutConfig` provided with grid coordinates when inside `crt.GridContainer`.
- [ ] `title` and series `label` use `#ResourceString(<key>)#` macros.
- [ ] Resource strings registered in the page schema's `localizableStrings` section.
