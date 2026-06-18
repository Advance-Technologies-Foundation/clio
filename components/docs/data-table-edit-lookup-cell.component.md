# How to Add a DataTable Edit Lookup Cell (`crt.DataTableEditLookupCell`) to a DataTable Column

> Audience: code agent configuring an editable lookup column in a `crt.DataTable` in a Creatio Freedom UI
> page schema.
>
> `crt.DataTableEditLookupCell` is the inline lookup editor (combo-box with a selection window) shown when a
> user activates a lookup-type cell in an editable `crt.DataTable` column. It is **not** inserted via
> `viewConfigDiff` — it is set as `editingCellView` on the column definition, or added to the `editingCellViews`
> map on the DataTable's `items` object.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: column `editingCellView` slot inside `crt.DataTable`, or `editingCellViews` map
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` — `crt.DataTable` column's `editingCellView` (or `editingCellViews` map) | An object `{ "type": "crt.DataTableEditLookupCell", "value": "$Attr", "items": "$ListAttr", "valueChange": {...} }`. **Always present.** |
| 2 | `handlers` (optional) | A request handler for `showList` to populate the dropdown on demand. |

There is **no** standalone `insert` op for this cell. The cell view lives inline inside the DataTable configuration.
No `modelConfigDiff` changes are needed for the cell itself.

### 1.1 Naming convention

```
// no standalone name — the cell view is inline config
// two PackageStore patterns exist:
//   1. column-level: "editingCellView": { ... }
//   2. table-level map: "editingCellViews": { "<attributeName>": { ... } }
```

---

## 2. Step-by-step recipe

### 2.1 Add `editingCellView` to the DataTable column

```jsonc
{
  "operation": "insert",
  "name": "MyDataTable",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataTable",
    "items": "$MyDetailDS",
    "columns": [
      {
        "id": "col-abc123",
        "code": "StatusId",
        "caption": "#ResourceString(MyDataTable_column_Status_caption)#",
        "dataValueType": 10,
        "referenceSchemaName": "Status",
        "cellView": {
          "type": "crt.TableTextCell",
          "value": "$MyDetailDS_StatusName"
        },
        "editingCellView": {
          "type": "crt.DataTableEditLookupCell",
          "value": "$MyDetailDS_StatusId",
          "items": "$StatusItems",
          "showList": {
            "request": "crt.LoadStatusListRequest"
          },
          "valueChange": {
            "request": "crt.StatusValueChangeRequest",
            "params": {
              "attributeName": "StatusId",
              "attributeValue": "@event.value"
            }
          }
        }
      }
    ]
  }
}
```

### 2.2 (Optional) Add a handler for `showList`

```jsonc
{
  "request": "crt.LoadStatusListRequest",
  "handler": async (request, next) => {
    // populate $StatusItems here
    return next?.handle(request);
  }
}
```

Without a `showList` handler, the dropdown uses `items` as a static list. The `showList` request fires with a
`filterValue` payload when the user types in the search box.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DataTableEditLookupCell` are in `ComponentRegistry.json` under `componentType: "crt.DataTableEditLookupCell"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// LookupValue — standard Creatio lookup pair
interface LookupValue {
  value: string;    // GUID
  displayValue: string;
}

// CrtMenuItemViewElementConfig — for listActions slot
interface CrtMenuItemViewElementConfig {
  type: 'crt.MenuItem';
  caption: string;
  icon?: string;
  clicked: RequestBindingConfig;
}

// showList output payload
interface ShowListPayload {
  filterValue: string;
  viewElement: 'crt.ComboBox';
}
```

---

## 5. Copy-paste minimal example

```jsonc
// Column editingCellView — from AISkills_FormPage.js
"editingCellViews": {
  "InputParametersDS_DataValueTypeUId": {
    "type": "crt.DataTableEditLookupCell",
    "value": "$InputParametersDetail.InputParametersDS_DataValueTypeUId | crt.ToDataValueTypeLookupValue",
    "valueChange": {
      "request": "crt.CopilotIntentParameterDataValueTypeChangeRequest",
      "params": {
        "attributeName": "InputParametersDS_DataValueTypeUId",
        "attributeValue": "@event.value",
        "collectionName": "InputParametersDetail",
        "primaryColumnValue": "$InputParametersDetail.InputParametersDS_Id"
      }
    },
    "items": "$AvailableInputDataValueTypes"
  }
}
```

---

## 6. Driving from page state

The `readonly` input is `propertyBindable`:

```jsonc
"editingCellView": {
  "type": "crt.DataTableEditLookupCell",
  "value": "$MyDetailDS_StatusId",
  "items": "$StatusItems",
  "readonly": "$MyTable_readonly"
}
```

---

## 7. Common pitfalls

1. **Using an `insert` op instead of `editingCellView`** — `crt.DataTableEditLookupCell` is not a standalone
   view element; placing it with a top-level `insert` op will not render correctly.
2. **Binding `value` directly to a GUID attribute** — for lookup columns the value must be a `LookupValue`
   object `{ value, displayValue }`; use `| crt.ToLookupValue` or similar converter when necessary.
3. **Empty `items` with no `showList`** — the dropdown renders empty; either populate `items` via attribute
   binding before the cell opens, or wire `showList` to a handler that loads on demand.
4. **Forgetting `valueChange`** — without `valueChange`, the selected value updates the `FormControl` but
   no action is dispatched; the change is not persisted to the datasource.
5. **Setting `useStaticFiltering: true` with a large list** — static filtering does a client-side substring
   match on the entire `items` array; for large lists prefer server-side filtering via `showList`.
6. **`listActions` without `clicked.request`** — each menu item must have a `clicked.request`; items without it
   render but do nothing.
7. **Mixing `editingCellView` and `editingCellViews`** — use `editingCellViews` (map) when the same DataTable
   has multiple lookup columns whose editing views differ; use `editingCellView` (inline) when the column
   definition itself carries the config.

---

## 8. Quick checklist

- [ ] `editingCellView` (or entry in `editingCellViews`) is set inside the DataTable column, not as a standalone `insert` op.
- [ ] `value` is bound to a `LookupValue`-compatible attribute (use a converter if needed).
- [ ] `items` is bound to an attribute holding an array of `LookupValue` objects.
- [ ] `valueChange` request is wired so the datasource gets updated on selection.
- [ ] `cellView` is also set on the same column for the read-only display state.
- [ ] If `showList` is wired, a matching `handlers` entry exists.
