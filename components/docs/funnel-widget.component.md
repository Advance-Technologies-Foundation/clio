# How to Add a Funnel Widget (`crt.FunnelWidget`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FunnelWidget` into a Creatio Freedom UI page schema.
>
> `crt.FunnelWidget` is a single-entity funnel chart (typically Opportunity pipeline stages). It owns its data
> fetching inline via the `config` object and needs only a single `viewConfigDiff` insert op — no separate
> datasource or viewModel entries required.

## Metadata

- **Category**: chart
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FunnelWidget"` and the full `config` object. **Always present.** |

`crt.FunnelWidget` is **self-contained** — it resolves entity data inline through `config.entities` and has no
separate datasource entry in `modelConfigDiff`. No `viewModelConfigDiff` entry is needed.

### 1.1 Naming convention

```
FunnelWidget_<id>        // view element name; <id> is any short unique slug (or a guid-based name from designer)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FunnelWidget_abc123",
  "parentName": "Main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FunnelWidget",
    "config": {
      "entities": [
        {
          "schemaName": "Opportunity",
          "calculatedOperations": [{ "operation": "Amount", "targetColumnName": "Budget" }],
          "connectedWith": null
        }
      ],
      "title": "#ResourceString(FunnelWidget_abc123_title)#",
      "color": "dark-blue",
      "theme": "without-fill"
    },
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 6, "rowSpan": 18 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FunnelWidget` are in `ComponentRegistry.json` under `componentType: "crt.FunnelWidget"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// FunnelWidgetConfig (config root)
interface FunnelWidgetConfig {
  entities: FunnelEntityConfig[];
  title: string;                  // use #ResourceString(...)# for localized text
  color: WidgetColor;             // e.g. "dark-blue" | "blue" | "green" | "steel-blue" | ...
  theme: WidgetTheme;             // "without-fill" | "full-fill"
  sectionBindingColumn?: { path: string | null };
}

// FunnelEntityConfig
interface FunnelEntityConfig {
  schemaName: string;
  calculatedOperations: Array<{ operation: string; targetColumnName: string }>;
  connectedWith: null;   // FunnelWidget uses a single entity; set to null
  filters?: { items: Record<string, FilterItem>; logicalOperation: number; ... };
}

// FunnelTypeItem (funnelTypeList entries)
interface FunnelTypeItem {
  label: string;         // i18n key shown as toggle button label
  type: FunnelSliceType; // 'ByCount' | 'StageConversion' | 'ToFirstStage'
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — Opportunity single-entity funnel
{
  "operation": "insert",
  "name": "SalesFunnelYearlyWidget",
  "parentName": "Main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "layoutConfig": {
      "column": 1,
      "colSpan": 6,
      "row": 18,
      "rowSpan": 18
    },
    "type": "crt.FunnelWidget",
    "config": {
      "title": "#ResourceString(SalesFunnelYearlyWidget_title)#",
      "color": "dark-blue",
      "theme": "without-fill",
      "sectionBindingColumn": { "path": null },
      "entities": [
        {
          "schemaName": "Opportunity",
          "calculatedOperations": [
            { "operation": "Amount", "targetColumnName": "Budget" }
          ],
          "connectedWith": null
        }
      ]
    }
  }
}
```

---

## 7. Common pitfalls

1. **`crt.FunnelWidget` vs `crt.FullPipelineWidget`.** Use `crt.FunnelWidget` for a single-entity funnel (Opportunity stages). Use `crt.FullPipelineWidget` when you need a multi-stage cross-entity pipeline (Lead → Opportunity). The two components share `FunnelWidgetConfig` but `crt.FunnelWidget` only shows one entity.
2. **`funnelTypeList` not set.** The component defaults to a built-in list of slice-type toggles; override only when you need custom labels or want to restrict the available views.
3. **`sectionBindingColumn` without a bound record ID.** If `sectionBindingColumn.path` points to a column but `sectionBindingColumnRecordId` is not wired to a page attribute, the widget renders empty while waiting for context.
4. **Wrong `color` or `theme` value.** Use `WidgetColor` enum keywords (e.g. `"dark-blue"`, `"blue"`, `"green"`); arbitrary CSS strings are ignored and the widget falls back to defaults.
5. **Missing `title` resource string.** Always define the resource string in the page's localization map; the widget header renders empty otherwise.
6. **Forgetting `layoutConfig` inside a `crt.GridContainer`.** Without row/column/colSpan/rowSpan the widget overlaps other grid children at position (1,1).

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FunnelWidget"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `config.entities` contains at least one entry with `connectedWith: null`.
- [ ] `config.title` set as a `#ResourceString(...)#`.
- [ ] `config.color` is a valid `WidgetColor` keyword.
- [ ] `layoutConfig` provided when inside a `crt.GridContainer`.
- [ ] `visible: true` set in `values`.
