# How to Add a Chart Widget (`crt.ChartWidget`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.ChartWidget` into a mobile page schema.
> Renders grouped/aggregated data as an interactive chart with drilldown support.
> Supported types: bar, horizontal bar, line, spline, area, scatter, doughnut, funnel.

## Metadata
- **Category**: interactive
- **Container**: false
- **Parent types**: any layout container that accepts chart-group items (e.g. `crt.DetailsGrid`)
- **Typical children**: none

---
## 1. Mental model
`crt.ChartWidget` renders one or more data series as a chart. The entire configuration lives in
a single `config` input: title, theme, color, and a `layout.series` array where each entry
describes an aggregation query with grouping and axis mappings. The component supports
`PlatformAPIs.Filtration`, so active page filters are automatically applied. Tapping a chart
point opens a drilldown data grid.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "LeadsByStatus",
  "values": {
    "type": "crt.ChartWidget",
    "config": {
      "title": "Leads by Status",
      "layout": {
        "series": [
          {
            "type": "bar",
            "yAxis": { "column": "Id", "aggregationType": 0 },
            "xAxis": { "column": "Status" }
          }
        ]
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
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.ChartWidget` are in
`ComponentRegistry.json` under `componentType: "crt.ChartWidget"`.

Key inputs (all `@CrtInput` on `CrtBaseChartWidgetComponent`):

| Property | Type | Description |
|---|---|---|
| `config` | `ChartWidgetConfig` | Full chart configuration: `{ title, theme, color, series: [{ type, label, data: { providing: { schemaName, aggregation, grouping, filters } } }] }`. |
| `isDesignTime` | `boolean` | When `true` the component skips live data requests. Set automatically by the designer. |
| `seriesData` | `SeriesData[][]` | Pre-fetched series data array; used when widget data source support is active. |

Key outputs:

| Event | Description |
|---|---|
| `seriesConfigsChanged` | Emitted when the user switches chart type via the toolbar; payload is the updated `SeriesConfig[]`. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "LeadsByStatus",
  "values": {
    "type": "crt.ChartWidget",
    "config": {
      "title": "Leads by Status",
      "layout": {
        "series": [
          {
            "type": "bar",
            "yAxis": { "column": "Id", "aggregationType": 0 },
            "xAxis": { "column": "Status" }
          }
        ]
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
- **Invalid `config` structure**: `_isValidConfig` rejects configs whose `series` entries lack a
  `data` property or whose structure matches the empty default template. Always include at least
  one series with a `data.providing` block that has a `schemaName`.
- **`aggregationType` values**: `0` = Count, `1` = Sum, `2` = Average, `3` = Min, `4` = Max.
  Use the numeric value, not the enum name, in JSON.
- **Section binding with no record ID**: if any series uses `sectionBindingColumn` the chart
  will not request data until `sectionBindingRecordId` is provided; in mobile the binding is
  resolved from the page context automatically, but a missing context silently suppresses data.
