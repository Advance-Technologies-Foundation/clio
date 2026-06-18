# How to Add a List Widget (`crt.ListWidget`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ListWidget` into a Creatio Freedom UI page schema.
>
> A `crt.ListWidget` is a data-grid displayed inside a dashboard-style widget card with a header,
> optional title, and fullscreen support. It combines the data-grid functionality
> (`items`, `columns`, `features`, sorting, selection, bulk actions) with a widget chrome
> (`widgetConfig.theme`, `widgetConfig.layout.color`, `title`).

## Metadata

- **Category**: chart (dashboard widget group)
- **Container**: no (renders its own content; `bulkActions`/`columns` are nested configs, not
  view-element slots)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none (columns are declared inline as `columns: [...]`)

---

## 1. Mental model — the 4 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `modelConfigDiff` | A datasource bound to an entity schema. |
| 2 | `viewModelConfigDiff` | Attributes for `items`, `activeRow`, and `selectedRows`. |
| 3 | `viewConfigDiff` | An `insert` op with `type: "crt.ListWidget"` and column definitions. |
| 4 | `handlers` (optional) | Handlers for `createItem`, `deleteItem`, `rowDoubleClick`, etc. |

The `crt.CreateListWidgetItemCommand` designer command sets up all four sections automatically.
When writing by hand, follow the pattern below.

### 1.1 Naming convention

```
ListWidget_<id>           // view element name
ListWidget_<id>DS         // datasource key in modelConfigDiff
$ListWidget_<id>          // items attribute in viewModelConfigDiff
$ListWidget_<id>_activeRow   // active row attribute
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
      "ListWidget_abcDS": {
        "type": "crt.EntityDataSource",
        "config": {
          "entitySchemaName": "Contact"
        }
      }
    }
  }
}
```

### 2.2 Declare viewModel attributes (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ListWidget_abc": {
        "isCollection": true,
        "datasource": "ListWidget_abcDS"
      },
      "ListWidget_abc_activeRow": {
        "datasource": "ListWidget_abcDS",
        "path": "activeRow"
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ListWidget_abc",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ListWidget",
    "title": "#ResourceString(ListWidget_abc_title)#",
    "widgetConfig": {
      "theme": "without-fill",
      "layout": {
        "color": "navy-blue"
      }
    },
    "features": {
      "rows": {
        "numeration": true,
        "selection": {
          "enable": true,
          "multiple": false
        }
      },
      "editable": false
    },
    "items": "$ListWidget_abc",
    "activeRow": "$ListWidget_abc_activeRow",
    "primaryColumnName": "ListWidget_abcDS_Id",
    "columns": [
      {
        "id": "col-guid-1",
        "code": "ListWidget_abcDS_Name",
        "caption": "#ResourceString(ListWidget_abcDS_Name)#",
        "dataValueType": 28
      }
    ],
    "placeholder": false,
    "visible": true,
    "fitContent": true,
    "layoutConfig": {
      "height": 350
    }
  }
}
```

### 2.4 (Optional) Row double-click handler

```jsonc
{
  "request": "crt.OpenEntityRecordRequest",
  "handler": async (request, next) => {
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ListWidget` are in `ComponentRegistry.json` under `componentType: "crt.ListWidget"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// ListWidgetConfig — widgetConfig shape
interface ListWidgetConfig {
  theme?: WidgetTheme;           // "with-fill" | "without-fill"
  layout?: {
    color?: WidgetColor;         // "navy-blue" | "dark-blue" | "green" | "orange" | ...
  };
  showTools?: boolean;           // show widget toolbar; defaults to true
}
```

---

## 5. Copy-paste minimal example

```jsonc
// modelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "ListWidget_w1DS": {
        "type": "crt.EntityDataSource",
        "config": { "entitySchemaName": "Contact" }
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
      "ListWidget_w1": { "isCollection": true, "datasource": "ListWidget_w1DS" },
      "ListWidget_w1_activeRow": { "datasource": "ListWidget_w1DS", "path": "activeRow" }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ListWidget_w1",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ListWidget",
    "title": "#ResourceString(ListWidget_w1_title)#",
    "widgetConfig": { "theme": "without-fill", "layout": { "color": "navy-blue" } },
    "features": {
      "rows": { "numeration": true, "selection": { "enable": true, "multiple": false } },
      "editable": false
    },
    "items": "$ListWidget_w1",
    "activeRow": "$ListWidget_w1_activeRow",
    "primaryColumnName": "ListWidget_w1DS_Id",
    "columns": [
      {
        "id": "73d605c8-46eb-bbf3-1333-6af7f9250699",
        "code": "ListWidget_w1DS_Name",
        "caption": "#ResourceString(ListWidget_w1DS_Name)#",
        "dataValueType": 28
      }
    ],
    "placeholder": false,
    "visible": true,
    "fitContent": true,
    "layoutConfig": { "height": 350 }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewConfigDiff.values — bind sorting via attribute
"sorting": "$ListWidget_w1_sorting"
```

`items`, `activeRow`, `selectedRows`, and `sorting` are `propertyBindable`; bind them to
viewModel attributes for runtime control.

---

## 7. Common pitfalls

1. **Omitting `primaryColumnName`** — the platform uses this column as the unique row key for
   selection and active-row tracking; the grid silently breaks without it.
2. **`features.rows.selection.multiple: true` vs. false** — the designer command defaults to
   single-selection (`multiple: false`) for list widgets; change explicitly only when multi-select
   bulk actions are present.
3. **`widgetConfig.showTools: false`** disables the toolbar including the fullscreen button;
   `widgetToolbarItems` will also be invisible even if populated.
4. **Forgetting `activeRow` attribute when using `rowDoubleClick`** — the event payload includes
   the active row; without the attribute wired to the datasource the handler receives `undefined`.
5. **`layoutConfig.height`** on the widget fixes the card height; without it the widget collapses
   to its minimum. Set a pixel number that fits the expected row count.
6. **`placeholder: false`** must be set when a real datasource is wired; leaving it `true`
   shows the empty-state placeholder permanently.
7. **Column `dataValueType`** must match the entity field type (`28` = Text, `7` = DateTime,
   `10` = Lookup, etc.) — a mismatch causes incorrect cell rendering.

---

## 8. Quick checklist

- [ ] Datasource entry in `modelConfigDiff` with unique key `ListWidget_<id>DS`.
- [ ] `items` and `activeRow` attributes in `viewModelConfigDiff` linked to the datasource.
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ListWidget"`, unique `name`,
  `items: "$ListWidget_<id>"`, `primaryColumnName`, and at least one entry in `columns`.
- [ ] `widgetConfig.theme` and `widgetConfig.layout.color` set.
- [ ] `title` set (static `#ResourceString(...)#`).
- [ ] `placeholder: false` when datasource is real.
- [ ] `layoutConfig.height` set to a sensible pixel value.
- [ ] Row action handlers registered in `handlers` if `rowDoubleClick` is wired.
