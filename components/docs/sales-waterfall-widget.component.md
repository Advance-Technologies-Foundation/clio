# How to Add a Sales Waterfall Widget (`crt.SalesWaterfallWidget`) to a Freedom UI Page

> Audience: code agent inserting `crt.SalesWaterfallWidget` into a Creatio Freedom UI page schema.
>
> `crt.SalesWaterfallWidget` is a chart widget that renders a waterfall pipeline visualization for sales
> opportunity analysis. It requires the `CrtWaterfallPipeline` package, reads pipeline stage data from the
> `WaterfallPipeline` virtual schema, and displays bookend metrics (`startValue`, `endValue`) alongside the
> stacked bar chart. The create command sets up all four diff sections automatically when a primary datasource
> is present.

## Metadata

- **Category**: chart
- **Container**: no
- **Parent types**: `crt.GridContainer` (typical for dashboard pages)
- **Typical children**: none

---

## 1. Mental model — the 4 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.SalesWaterfallWidget"`, `config`, and `seriesData`/`startValue`/`endValue` bindings. **Always present.** |
| 2 | `modelConfigDiff` | A datasource entry using the `WaterfallPipeline` virtual schema with aggregation and grouping. |
| 3 | `viewModelConfigDiff` | Attributes for `seriesData`, `startValue`, `endValue`, `toolbarMenuItems`, and optional filter/pagination state. |
| 4 | `handlers` (optional) | Handlers for `drillDown`, `paginationChange`, `sortingChange`, `searchFilterChange`, `columnsChange`. |

### 1.1 Naming convention

```
SalesWaterfallWidget_<id>             // view element name
SalesWaterfallWidget_<id>_SeriesData  // viewModel attribute + datasource providing.attribute
$SalesWaterfallWidget_<id>_SeriesData // $-prefix binding in viewConfigDiff
```

---

## 2. Step-by-step recipe

### 2.1 Add the datasource (`modelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "SalesWaterfallWidget_abc_SeriesData_Base": {
        "type": "crt.EntityDataSource",
        "config": {
          "entitySchemaName": "WaterfallPipeline",
          "rowCount": -1,
          "filters": {
            "filter": null,
            "filterAttributes": []
          },
          "aggregation": {
            "column": {
              "orderDirection": 0,
              "orderPosition": -1,
              "isVisible": true,
              "expression": {
                "expressionType": 1,
                "functionArgument": { "expressionType": 0, "columnPath": "Amount" },
                "functionType": 2,
                "aggregationType": 2,
                "aggregationEvalType": 2
              }
            }
          },
          "grouping": {
            "type": "by-value",
            "column": {
              "orderDirection": 1,
              "orderPosition": 0,
              "isVisible": true,
              "expression": { "expressionType": 0, "columnPath": "PipelineStage" }
            }
          }
        }
      }
    }
  }
}
```

### 2.2 Declare attributes (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "SalesWaterfallWidget_abc_SeriesData": {
        "type": "crt.BaseViewModelCollection",
        "isCollection": true,
        "dataSources": ["SalesWaterfallWidget_abc_SeriesData_Base"]
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "SalesWaterfallWidget_abc",
  "parentName": "MainGridContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SalesWaterfallWidget",
    "layoutConfig": {
      "column": 1,
      "colSpan": 1,
      "row": 2,
      "rowSpan": 3
    },
    "config": {
      "title": "#ResourceString(SalesWaterfallWidget_abc_title)#",
      "theme": "full-fill",
      "layout": { "color": "dark-blue" },
      "data": {
        "designOptions": {
          "filterOptions": {
            "HistoricalDataEntityName": "OpportunityHistory",
            "EntityName": "Opportunity",
            "DateColumnName": "DueDate",
            "StageColumnName": "Stage",
            "AmountColumnName": "Amount"
          }
        },
        "providing": {
          "attribute": "SalesWaterfallWidget_abc_SeriesData_Base",
          "schemaName": "WaterfallPipeline",
          "rowCount": -1,
          "filters": { "filter": null, "filterAttributes": [] },
          "aggregation": {
            "column": {
              "orderDirection": 0,
              "orderPosition": -1,
              "isVisible": true,
              "expression": {
                "expressionType": 1,
                "functionArgument": { "expressionType": 0, "columnPath": "Amount" },
                "functionType": 2,
                "aggregationType": 2,
                "aggregationEvalType": 2
              }
            }
          },
          "grouping": {
            "type": "by-value",
            "column": {
              "orderDirection": 1,
              "orderPosition": 0,
              "isVisible": true,
              "expression": { "expressionType": 0, "columnPath": "PipelineStage" }
            }
          },
          "hierarchyConfig": {}
        }
      },
      "stages": []
    },
    "seriesData": "$SalesWaterfallWidget_abc_SeriesData",
    "visible": true
  }
}
```

### 2.4 (Optional) Handler for drilldown

```jsonc
{
  "request": "crt.SalesWaterfallWidgetDrillDownRequest",
  "handler": async (request, next) => next?.handle(request)
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.SalesWaterfallWidget` are in `ComponentRegistry.json` under `componentType: "crt.SalesWaterfallWidget"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

Real PackageStore usage from `UsrSalesWaterfallWidget`:

```jsonc
// viewConfigDiff entry (condensed)
{
  "operation": "insert",
  "name": "SalesWaterfallWidget_m04h4yo",
  "parentName": "MainGridContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "layoutConfig": { "column": 1, "colSpan": 1, "row": 2, "rowSpan": 3 },
    "type": "crt.SalesWaterfallWidget",
    "config": {
      "title": "#ResourceString(SalesWaterfallWidget_m04h4yo_title)#",
      "theme": "full-fill",
      "layout": { "color": "dark-blue" },
      "data": {
        "designOptions": {
          "filterOptions": {
            "HistoricalDataEntityName": "OpportunityHistory",
            "EntityName": "Opportunity",
            "DateColumnName": "DueDate",
            "StageColumnName": "Stage",
            "AmountColumnName": "Amount"
          }
        },
        "providing": {
          "attribute": "SalesWaterfallWidget_m04h4yo_SeriesData_Base",
          "schemaName": "WaterfallPipeline",
          "rowCount": -1,
          "filters": { "filterAttributes": [] },
          "aggregation": {
            "column": {
              "orderDirection": 0,
              "orderPosition": -1,
              "isVisible": true,
              "expression": {
                "expressionType": 1,
                "functionArgument": { "expressionType": 0, "columnPath": "Amount" },
                "functionType": 2,
                "aggregationType": 2,
                "aggregationEvalType": 2
              }
            }
          },
          "grouping": {
            "type": "by-value",
            "column": {
              "orderDirection": 1,
              "orderPosition": 0,
              "isVisible": true,
              "expression": { "expressionType": 0, "columnPath": "PipelineStage" }
            }
          },
          "hierarchyConfig": {}
        }
      },
      "stages": []
    }
  }
}
```

---

## 7. Common pitfalls

1. **Missing `CrtWaterfallPipeline` package** — the `WaterfallPipeline` virtual schema is defined in this package; the widget renders empty without it.
2. **`providing.attribute` must match the viewModel attribute name** — the datasource key and the `providing.attribute` string inside `config.data.providing` both refer to the same attribute name; they must be identical.
3. **`stages` left empty** — the create command seeds stages from `WaterfallPipelineStages` enum values; an empty `stages` array hides all category bars. Copy the full stages array from a real PackageStore schema.
4. **`startValue`/`endValue` not bound** — without both inputs the chart will not render even when `seriesData` is present; the component only calls `updateChartConfig()` when all three are non-null.
5. **`drillDown` output not handled** — clicking a chart bar fires `drillDown`; without a handler the click is silently ignored. Wire it to a request handler if drilldown is desired.
6. **`config.data.providing.filters.filterAttributes` references** — each filter attribute entry must match a `QuickFilter` or similar filter attribute declared in `viewModelConfigDiff`; mismatches cause silent empty results.
7. **`RequiredFeatures: ShowDesignerDemoItems`** — the toolbar item requires this feature flag; the widget itself works at runtime without the flag.

---

## 8. Quick checklist

- [ ] `CrtWaterfallPipeline` package is installed.
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.SalesWaterfallWidget"`, unique `name`, and `layoutConfig`.
- [ ] `config.data.providing.attribute` matches the viewModel attribute name in `viewModelConfigDiff`.
- [ ] `config.data.providing.schemaName` is `"WaterfallPipeline"`.
- [ ] `stages` array populated with at least the standard waterfall stage IDs and colors.
- [ ] `seriesData` bound to a `$Attribute` declared as a `BaseViewModelCollection`.
- [ ] If `startValue` and `endValue` are used, both must be bound; one alone suppresses the chart.
- [ ] `modelConfigDiff` datasource uses `WaterfallPipeline` entity schema with aggregation + grouping.
