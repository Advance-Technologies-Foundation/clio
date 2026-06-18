# How to Add a Gauge Widget (`crt.GaugeWidget`) to a Freedom UI Page

> Audience: code agent inserting a `crt.GaugeWidget` into a Creatio Freedom UI page schema.
>
> `crt.GaugeWidget` is a semicircular gauge chart that shows a single numeric metric against configurable
> thresholds (min/max/color bands). Adding it requires three diff sections: `viewConfigDiff` (the element),
> `modelConfigDiff` (the datasource), and `viewModelConfigDiff` (the data attribute).

## Metadata

- **Category**: chart
- **Container**: no
- **Parent types**: `crt.GridContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.GaugeWidget"` and the full `config` object. **Always present.** |
| 2 | `modelConfigDiff` | A datasource entry (`crt.EntityDataSource`) under `dataSources` with the aggregation column. |
| 3 | `viewModelConfigDiff` | An attribute entry under `attributes` linking to the datasource. |

The `config.data.providing.attribute` value in `viewConfigDiff` is the key that joins all three sections
together — it names the datasource (`<attribute>DS`) in `modelConfigDiff` and the attribute in
`viewModelConfigDiff`.

### 1.1 Naming convention

```
GaugeWidget_<id>             // view element name; <id> is any short unique slug
GaugeWidget_<id>_Data        // attribute name referenced in config.data.providing.attribute
GaugeWidget_<id>_DataDS      // datasource key in modelConfigDiff.dataSources
```

---

## 2. Step-by-step recipe

### 2.1 Add a datasource (`modelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "GaugeWidget_abc123_DataDS": {
        "type": "crt.EntityDataSource",
        "config": {
          "entitySchemaName": "Opportunity",
          "attributes": {
            "Amount_Aggregation": {
              "type": "Function",
              "path": "Amount",
              "functionConfig": {
                "type": "aggregation",
                "aggregation": "Sum",
                "aggregationEval": "none"
              }
            }
          }
        },
        "scope": "viewElement"
      }
    }
  }
}
```

### 2.2 Add a viewModel attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "GaugeWidget_abc123_Data": {}
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "GaugeWidget_abc123",
  "parentName": "Main",
  "propertyName": "items",
  "index": 0,
  "values": {
    "layoutConfig": {
      "column": 1,
      "row": 1,
      "colSpan": 3,
      "rowSpan": 3
    },
    "type": "crt.GaugeWidget",
    "config": {
      "title": "#ResourceString(GaugeWidget_abc123_title)#",
      "data": {
        "providing": {
          "attribute": "GaugeWidget_abc123_Data",
          "schemaName": "Opportunity",
          "filters": {
            "filter": {
              "items": {},
              "logicalOperation": 0,
              "isEnabled": true,
              "filterType": 6,
              "rootSchemaName": "Opportunity"
            },
            "filterAttributes": []
          },
          "aggregation": {
            "column": {
              "orderDirection": 0,
              "orderPosition": -1,
              "isVisible": true,
              "expression": {
                "expressionType": 1,
                "functionArgument": { "expressionType": 0, "columnPath": "Id" },
                "functionType": 2,
                "aggregationType": 1,
                "aggregationEvalType": 2
              }
            }
          }
        },
        "formatting": {
          "type": "number",
          "decimalSeparator": ".",
          "decimalPrecision": 0,
          "thousandSeparator": ","
        }
      },
      "text": {
        "template": "#ResourceString(GaugeWidget_abc123_template)#",
        "metricMacros": "{0}"
      },
      "layout": { "color": "green" },
      "theme": "full-fill",
      "thresholds": {
        "0":  { "color": "#00c853" },
        "10": { "color": "#0058ef" },
        "20": { "color": "#ff3e13" }
      },
      "min": 0,
      "max": 50
    },
    "visible": true
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.GaugeWidget` are in `ComponentRegistry.json` under `componentType: "crt.GaugeWidget"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// GaugeWidgetConfig extends IndicatorWidgetConfig
interface GaugeWidgetConfig {
  title: string;                     // use #ResourceString(...)# for localized text
  data: WidgetDataConfig;            // { providing, formatting }
  text: { template: string; metricMacros: string };
  layout: { color: WidgetColor };    // WidgetColor keyword e.g. "green" | "dark-blue" | "steel-blue"
  theme: WidgetTheme;                // "without-fill" | "full-fill" | "glassmorphism"
  thresholds: GaugeThresholds;       // Record<number|string, { color: string }>  (keys = start values)
  min: number;                       // gauge scale minimum
  max: number;                       // gauge scale maximum
  sectionBindingColumn?: { path: string | null };
}

// GaugeThresholds: key = threshold start value, value = { color: hex or named }
type GaugeThresholds = Record<string, { color: string }>;
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "GaugeWidget_lz4qryr",
  "values": {
    "layoutConfig": { "column": 2, "row": 1, "colSpan": 1, "rowSpan": 1 },
    "type": "crt.GaugeWidget",
    "config": {
      "title": "#ResourceString(GaugeWidget_lz4qryr_title)#",
      "data": {
        "providing": {
          "attribute": "GaugeWidget_lz4qryr_Data",
          "schemaName": "City",
          "filters": { "filter": { "items": {}, "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "City" }, "filterAttributes": [] },
          "aggregation": { "column": { "orderDirection": 0, "orderPosition": -1, "isVisible": true, "expression": { "expressionType": 1, "functionArgument": { "expressionType": 0, "columnPath": "Id" }, "functionType": 2, "aggregationType": 1, "aggregationEvalType": 2 } } }
        },
        "formatting": { "type": "number", "decimalSeparator": ".", "decimalPrecision": 0, "thousandSeparator": "," }
      },
      "text": { "template": "#ResourceString(GaugeWidget_lz4qryr_template)#", "metricMacros": "{0}" },
      "layout": { "color": "green" },
      "theme": "full-fill",
      "thresholds": { "0": { "color": "#00c853" }, "10": { "color": "#0058ef" }, "20": { "color": "#ff3e13" }, "50": { "color": "#ff3e13" } },
      "min": 0,
      "max": 50
    },
    "visible": true
  },
  "parentName": "GeneralInfoTabContainer",
  "propertyName": "items",
  "index": 1
}
```

```jsonc
// modelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "GaugeWidget_lz4qryr_DataDS": {
        "type": "crt.EntityDataSource",
        "config": {
          "entitySchemaName": "City",
          "attributes": {
            "Id_Aggregation": {
              "type": "Function",
              "path": "Id",
              "functionConfig": { "type": "aggregation", "aggregation": "Count", "aggregationEval": "none" }
            }
          }
        },
        "scope": "viewElement"
      }
    }
  }
}
```

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "GaugeWidget_lz4qryr_Data": {}
    }
  }
}
```

---

## 7. Common pitfalls

1. **`config.data.providing.attribute` does not match the datasource key.** The attribute name must equal the datasource key minus the trailing `DS` suffix: if the attribute is `GaugeWidget_abc_Data` the datasource key must be `GaugeWidget_abc_DataDS`.
2. **`thresholds` keys are strings, not numbers.** Keys are threshold start values as strings (e.g. `"0"`, `"10"`); the gauge renders each band from its key up to the next key.
3. **`min`/`max` out of sync with thresholds.** All threshold keys must fall within `[min, max]`; out-of-range keys produce invisible bands.
4. **`layout.color` vs top-level `color`.** Use `config.layout.color` for the gauge accent color. The component also falls back to `config.color` for backward compatibility.
5. **Forgetting `modelConfigDiff` and `viewModelConfigDiff`.** Unlike simple layout containers, this widget requires all three diff sections — omitting either model or viewModel entry prevents data from loading.
6. **`theme: "glassmorphism"` outside a glassmorphism page.** The glassmorphism theme relies on a parent container with a specific background; using it on a standard page produces incorrect contrast.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.GaugeWidget"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `config.data.providing.attribute` value matches the datasource key in `modelConfigDiff` (append `DS`).
- [ ] `modelConfigDiff` has a `crt.EntityDataSource` entry under the `<attribute>DS` key with the correct entity and aggregation.
- [ ] `viewModelConfigDiff` has an attribute entry with the same key as `config.data.providing.attribute`.
- [ ] `config.thresholds` keys are within `[min, max]`.
- [ ] `config.title` set as a `#ResourceString(...)#`.
- [ ] `layoutConfig` provided when inside a `crt.GridContainer`.
