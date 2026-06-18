# How to Add a Waterfall Widget (`crt.WaterfallWidget`) to a Freedom UI Page

> Audience: code agent inserting `crt.WaterfallWidget` into a Creatio Freedom UI page schema.
> `crt.WaterfallWidget` is a **chart widget** that renders a waterfall (pipeline) bar chart with
> drilldown, filtering, sorting, and full-screen capabilities.

## Metadata

- **Category**: chart
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.WaterfallWidget"` and a `config` object. **Always present.** |
| 2 | `modelConfigDiff` | A datasource registration for the widget's data feed. |
| 3 | `handlers` | *(if `drillDown`, `paginationChange`, `sortingChange`, `searchFilterChange` outputs need custom logic)* |

> **Note:** `@CrtInterfaceDesignerItem` is currently commented out in the component source; the
> create command `crt.CreateWaterfallWidgetViewItemCommand` does not exist as an active implementation.
> This component must be wired manually via schema diff.

### 1.1 Naming convention

```
WaterfallWidget_<id>      // view element name
WaterfallWidget_<id>DS    // datasource key in modelConfigDiff
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "WaterfallWidget_sales",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WaterfallWidget",
    "config": {
      "title": "#ResourceString(WaterfallWidget_sales_title)#",
      "stages": []
    },
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 3 }
  }
}
```

### 2.2 (Optional) Bind `drillDown` and sorting outputs to handlers

```jsonc
{
  "request": "crt.WaterfallDrillDownRequest",
  "handler": async (request, next) => {
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.WaterfallWidget` are in `ComponentRegistry.json` under `componentType: "crt.WaterfallWidget"`. This
guide covers only the assembly mechanics.

Key inputs: `config` (chart title, theme, color, stages), `seriesData`, `listData`, `pagingConfig`,
`sortingConfig`, `searchValue`, `sectionBindingColumnRecordId`, `toolbarMenuItems`, `userProfileData`.
Key outputs: `drillDown`, `paginationChange`, `sortingChange`, `searchFilterChange`, `columnsChange`,
`fullScreenStateChanged`, `getProfileColumns`, `resetToDefault`.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// WaterfallPipelineWidgetConfig (config input)
interface WaterfallPipelineWidgetConfig {
  title: string;               // widget heading; use #ResourceString(<key>)# for localization
  theme?: WidgetTheme;         // e.g. 'FullFill'
  color?: WidgetColor;         // e.g. 'Green'
  stages?: WidgetStageConfig[];
}

// SeriesData (seriesData input)
interface SeriesData {
  // see chart-widget/models
}
```

`crt.WaterfallWidget` declares `compatibleAPIs: { [PlatformAPIs.Filtration]: true }`, which means it
participates in the platform filtration API and responds to filter broadcasts from other components on
the page.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "WaterfallWidget_pipeline",
  "parentName": "DashboardContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WaterfallWidget",
    "config": {
      "title": "#ResourceString(WaterfallWidget_pipeline_title)#",
      "stages": []
    },
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 3 }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare filter/search attributes
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "WaterfallSearchValue": { "value": "" }
    }
  }
}

// viewConfigDiff.values
"searchValue": "$WaterfallSearchValue"
```

Most data-binding inputs (`seriesData`, `listData`, `pagingConfig`, `sortingConfig`) are fed by the
datasource registered in `modelConfigDiff`.

---

## 7. Common pitfalls

1. **`@CrtInterfaceDesignerItem` is commented out.** The create command `crt.CreateWaterfallWidgetViewItemCommand` is not registered; you must build the full `viewConfigDiff` manually.
2. **`config.stages` must be an array.** An absent or `null` stages array causes the chart to render empty.
3. **`config.title` is localizable.** Use `#ResourceString(<key>)#` to avoid hardcoded strings.
4. **`compatibleAPIs.Filtration: true`** — the widget integrates with the platform filtration bus; if a `crt.Filter` or `crt.QuickFilter` is on the same page, they will communicate automatically.
5. **`layoutConfig` in a `crt.GridContainer`** — the chart needs adequate `rowSpan` (at least 3) to render a readable chart height; a `rowSpan: 1` collapses the chart.
6. **`drillDown` and `fullScreenStateChanged`** — wire these to handlers if the page needs to respond to user drill-down events or full-screen toggling.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.WaterfallWidget"`, unique `name`.
- [ ] `config` object present with at least `title` and `stages: []`.
- [ ] `layoutConfig` provides adequate `colSpan` and `rowSpan` for a visible chart.
- [ ] If `searchValue` or `sortingConfig` are dynamic, corresponding attributes declared in `viewModelConfigDiff`.
- [ ] If `drillDown` / `paginationChange` / `sortingChange` need custom logic, handlers wired in `handlers`.
