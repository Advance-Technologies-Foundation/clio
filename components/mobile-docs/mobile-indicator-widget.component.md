# How to Add an Indicator Widget (`crt.IndicatorWidget`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.IndicatorWidget` into a mobile page schema.
> Displays a single aggregated KPI metric (count, sum, average, min, or max) with a title,
> color theme, and optional comparison badge.

## Metadata
- **Category**: interactive
- **Container**: false
- **Parent types**: any layout container that accepts chart-group items (e.g. `crt.DetailsGrid`)
- **Typical children**: none

---
## 1. Mental model
`crt.IndicatorWidget` renders one numeric metric in a colored tile. The entire configuration —
title, aggregation query, formatting, theme, and comparison — lives in a single `config` input
object. Tapping the tile opens a drilldown data grid (when drilldown is enabled). The component
supports `PlatformAPIs.Filtration`, so active page filters narrow the metric automatically.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "TotalIndicator",
  "values": {
    "type": "crt.IndicatorWidget",
    "config": {
      "title": "Total",
      "data": {
        "providing": {
          "type": "crt.ProvideAggregationDataRequest",
          "params": {
            "entitySchemaName": "Contact",
            "aggregationType": 0,
            "columnPath": "Id"
          }
        }
      }
    }
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.IndicatorWidget` are in
`ComponentRegistry.json` under `componentType: "crt.IndicatorWidget"`.

Key inputs (all `@CrtInput` on `CrtBaseIndicatorWidgetComponent`):

| Property | Type | Description |
|---|---|---|
| `config` | `IndicatorWidgetConfig` | Full widget configuration: `{ title, theme, layout: { color }, text: { template, metricMacros, labelPosition, fontSizeMode }, data: { providing, formatting } }`. |
| `isDesignTime` | `boolean` | When `true` the component skips live data requests. Set automatically by the designer. |
| `drilldownEnabled` | `boolean` | Enables tap-to-drilldown navigation (default `true`). |
| `data` | `unknown` | Raw aggregation result from the widget data source; drives the displayed metric when widget data source support is active. |
| `difference` | `number \| null` | Numeric difference used for the comparison badge. |
| `errorMessage` | `string` | Error text shown in place of the metric value when data loading fails. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "TotalIndicator",
  "values": {
    "type": "crt.IndicatorWidget",
    "config": {
      "title": "Total",
      "data": {
        "providing": {
          "type": "crt.ProvideAggregationDataRequest",
          "params": {
            "entitySchemaName": "Contact",
            "aggregationType": 0,
            "columnPath": "Id"
          }
        }
      }
    }
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **Empty `config.data`**: the component validates `config.data` on set — if it is absent the
  widget renders nothing and marks itself as not validated. Always include at least a `providing`
  block.
- **Missing `schemaName` in providing**: the legacy (non-widget-data-source) code path requires
  `providing.schemaName` to be set; omitting it silently skips the data request.
- **`aggregationType` values**: `0` = Count, `1` = Sum, `2` = Average, `3` = Min, `4` = Max.
  Use the numeric value, not the enum name, in JSON.
