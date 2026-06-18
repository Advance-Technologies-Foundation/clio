# How to Add a Full Pipeline Widget (`crt.FullPipelineWidget`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FullPipelineWidget` into a Creatio Freedom UI page schema.
>
> `crt.FullPipelineWidget` is a funnel-style chart that visualizes a multi-stage sales pipeline across two
> connected entity schemas (e.g. Lead → Opportunity). It owns its data fetching inline via the `config` object
> and needs only a single `viewConfigDiff` insert op.

## Metadata

- **Category**: chart
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FullPipelineWidget"` and the full `config` object. **Always present.** |

`crt.FullPipelineWidget` is **self-contained** — it resolves entity data inline through `config.entities` and
has no separate datasource entry in `modelConfigDiff`. No `viewModelConfigDiff` entry is needed.

### 1.1 Naming convention

```
FullPipelineWidget_<id>        // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FullPipelineWidget_abc123",
  "parentName": "Main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FullPipelineWidget",
    "config": {
      "entities": [
        {
          "schemaName": "Lead",
          "calculatedOperations": [{ "operation": "Amount", "targetColumnName": "Budget" }],
          "connectedWith": null
        },
        {
          "schemaName": "Opportunity",
          "calculatedOperations": [{ "operation": "Amount", "targetColumnName": "Budget" }],
          "connectedWith": {
            "childSchemaColumnName": "Id",
            "connectionSchemaName": "Lead",
            "parentSchemaColumnName": "Opportunity",
            "schemaName": "Lead",
            "type": 0
          }
        }
      ],
      "title": "#ResourceString(FullPipelineWidget_abc123_title)#",
      "color": "dark-blue",
      "theme": "without-fill"
    },
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 6, "rowSpan": 8 }
  }
}
```

The first entity in `entities` is the pipeline entry-point (no `connectedWith`). Subsequent entries describe
how each stage connects back to the previous via a join (the `connectedWith` object).

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FullPipelineWidget` are in `ComponentRegistry.json` under `componentType: "crt.FullPipelineWidget"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// FunnelWidgetConfig (config root)
interface FunnelWidgetConfig {
  entities: FunnelEntityConfig[];
  title: string;                  // use #ResourceString(...)# for localized text
  color: WidgetColor;             // e.g. "dark-blue" | "steel-blue" | "green" | ...
  theme: WidgetTheme;             // "without-fill" | "full-fill"
  sectionBindingColumn?: WidgetDataSectionBindingColumnConfig;
  layout?: { border?: { hidden?: boolean } };
}

// FunnelEntityConfig (one pipeline stage)
interface FunnelEntityConfig {
  schemaName: string;
  calculatedOperations: Array<{ operation: string; targetColumnName: string }>;
  connectedWith: FunnelEntityConnectionConfig | null;  // null for the first stage
}

// FunnelEntityConnectionConfig
interface FunnelEntityConnectionConfig {
  childSchemaColumnName: string;   // column on this entity pointing to parent
  connectionSchemaName: string;    // intermediate schema (often same as parent)
  parentSchemaColumnName: string;  // column on parent entity
  schemaName: string;              // parent entity schema name
  type: number;                    // join type (0 = standard)
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — Lead → Opportunity two-stage pipeline
{
  "operation": "insert",
  "name": "FullPipelineWidget_be1wy0o",
  "parentName": "Main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FullPipelineWidget",
    "config": {
      "entities": [
        {
          "schemaName": "Lead",
          "calculatedOperations": [
            { "operation": "Amount", "targetColumnName": "Budget" }
          ],
          "connectedWith": null
        },
        {
          "schemaName": "Opportunity",
          "calculatedOperations": [
            { "operation": "Amount", "targetColumnName": "Budget" }
          ],
          "connectedWith": {
            "childSchemaColumnName": "Id",
            "connectionSchemaName": "Lead",
            "parentSchemaColumnName": "Opportunity",
            "schemaName": "Lead",
            "type": 0
          }
        }
      ],
      "title": "#ResourceString(FullPipelineWidget_be1wy0o_title)#",
      "color": "steel-blue",
      "theme": "without-fill",
      "layout": { "border": { "hidden": true } }
    },
    "visible": true
  },
  "parentName": "LeftFilterContainerInner",
  "propertyName": "items",
  "index": 2
}
```

---

## 7. Common pitfalls

1. **First entity has `connectedWith` set.** The first stage must have `connectedWith: null`; the pipeline starts there and each subsequent stage provides the `connectedWith` join definition.
2. **`calculatedOperations` is empty.** Omitting this array causes the widget to show no metric values; at minimum include one operation with `operation: "Amount"` (or the relevant aggregation key).
3. **Wrong `color` or `theme` value.** Use `WidgetColor` enum values (e.g. `"dark-blue"`, `"steel-blue"`, `"green"`); arbitrary CSS strings are silently ignored and the widget falls back to defaults.
4. **Missing `title` resource string.** The title renders as an empty string and the resource key is never created; always define the resource string in the page's localization map.
5. **`sectionBindingColumn` without a bound record ID.** If `sectionBindingColumn` is configured but `sectionBindingColumnRecordId` is not wired to a page attribute (`"$Id"` or similar), the widget renders empty while waiting for context.
6. **Forgetting `layoutConfig` inside a `crt.GridContainer`.** Without row/column/colSpan/rowSpan the widget overlaps other grid children at position (1,1).

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FullPipelineWidget"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `config.entities` contains at least one entry; the first entry has `connectedWith: null`.
- [ ] Each subsequent entity in `entities` has a valid `connectedWith` object.
- [ ] `config.title` set as a `#ResourceString(...)#`.
- [ ] `config.color` is a valid `WidgetColor` keyword.
- [ ] `layoutConfig` provided when inside a `crt.GridContainer`.
- [ ] `visible: true` set in `values`.
