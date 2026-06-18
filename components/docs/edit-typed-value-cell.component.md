# How to Add a DataTable Edit Typed Value Cell (`crt.EditTypedValueCell`) to a DataTable Column

> Audience: code agent configuring an editable column in a `crt.DataTable` where the cell type is determined
> at runtime by the column's `dataValueType` in a Creatio Freedom UI page schema.
>
> `crt.EditTypedValueCell` is a dispatcher cell that automatically selects the correct underlying editor
> (`crt.DataTableEditTextCell`, `crt.DataTableEditNumericCell`, `crt.DataTableEditLookupCell`, etc.) based on
> the `dataValueType` input. Use it when the editing cell type must be chosen dynamically rather than being
> hardcoded in the column definition. It is **not** inserted via `viewConfigDiff` — it is set as the
> `editingCellView` property on the DataTable column definition.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: column `editingCellView` slot inside `crt.DataTable`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` — `crt.DataTable` column's `editingCellView` | An object `{ "type": "crt.EditTypedValueCell", "valueAttribute": "Attr", "dataValueType": $TypeAttr, "config": {...} }`. **Always present.** |

There is **no** standalone `insert` op. Internally the component builds a config and forwards it to the
appropriate typed cell; the `control` binding is constructed automatically as `$${valueAttribute}`.

### 1.1 Dispatching table

| `dataValueType` | Delegate cell |
|---|---|
| Integer, Float, FLOAT0-FLOAT4, FLOAT8, MONEY0-MONEY3, Money | `crt.DataTableEditNumericCell` |
| Boolean | `crt.TableBooleanCell` (with `readonly: false`) |
| Lookup | `crt.DataTableEditLookupCell` |
| Date | `crt.DataTableEditDateTimeCell` (with `dateType: 'Date'`) |
| DateTime | `crt.DataTableEditDateTimeCell` (with `dateType: 'DateTime'`) |
| Text (default) | `crt.DataTableEditTextCell` |

Any extra properties in `config` are merged on top of the template and override type defaults.

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
        "code": "Value",
        "caption": "#ResourceString(MyDataTable_column_Value_caption)#",
        "dataValueType": "$MyDetailDS_DataValueType",
        "cellView": {
          "type": "crt.TableTextCell",
          "value": "$MyDetailDS_Value"
        },
        "editingCellView": {
          "type": "crt.EditTypedValueCell",
          "valueAttribute": "MyDetailDS_Value",
          "dataValueType": "$MyDetailDS_DataValueType",
          "config": {}
        }
      }
    ]
  }
}
```

### 2.2 (Optional) Override delegate cell properties via `config`

```jsonc
// Force decimal precision for numeric types
"editingCellView": {
  "type": "crt.EditTypedValueCell",
  "valueAttribute": "MyDetailDS_Amount",
  "dataValueType": "$MyDetailDS_DataValueType",
  "config": {
    "format": { "decimalPrecision": 2 }
  }
}
```

Any property in `config` is spread on top of the auto-selected template, so you can pass extra props to the
underlying typed cell without knowing its type at schema-authoring time.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EditTypedValueCell` are in `ComponentRegistry.json` under `componentType: "crt.EditTypedValueCell"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`DataValueType` is a `Terrasoft.DataValueType` enum integer (`1` = Text, `5` = Integer, `6` = Float, `7` = Money,
`10` = Lookup, `34` = DateTime, `33` = Date, `12` = Boolean). The `dataValueType` input accepts the raw integer or
an attribute binding that holds the integer.

---

## 5. Copy-paste minimal example

No direct PackageStore match found for `crt.EditTypedValueCell` directly (it is typically generated
programmatically). Pattern based on the component implementation:

```jsonc
// Column with dynamic cell type driven by a datasource attribute
{
  "id": "col-value",
  "code": "Value",
  "caption": "Value",
  "dataValueType": "$ParametersDS_DataValueType",
  "cellView": {
    "type": "crt.TableTextCell",
    "value": "$ParametersDS_Value"
  },
  "editingCellView": {
    "type": "crt.EditTypedValueCell",
    "valueAttribute": "ParametersDS_Value",
    "dataValueType": "$ParametersDS_DataValueType",
    "config": {}
  }
}
```

---

## 6. Driving from page state

`dataValueType` accepts a `$AttributeName` binding that changes at runtime. When the bound attribute value
changes, the cell re-dispatches and selects the appropriate delegate automatically.

---

## 7. Common pitfalls

1. **Using an `insert` op instead of `editingCellView`** — `crt.EditTypedValueCell` is not a standalone view element.
2. **Setting `valueAttribute` to the `$`-prefixed form** — pass the bare attribute name string (`"MyDS_Value"`),
   not the binding expression (`"$MyDS_Value"`); the component prepends `$` internally.
3. **Passing `control` in `config`** — the component always sets `control: "$${valueAttribute}"` and will
   overwrite any `control` value you pass in `config`.
4. **Using this cell when the type is static** — if the column's `dataValueType` is fixed, prefer the specific
   typed cell directly (`crt.DataTableEditTextCell`, etc.) for clarity.
5. **Expecting `readonly` propagation** — `crt.EditTypedValueCell` itself has no `readonly` input; set
   `readonly` inside `config` to forward it to the delegate cell.

---

## 8. Quick checklist

- [ ] `editingCellView` is set inside the DataTable column, not as a standalone `insert` op.
- [ ] `valueAttribute` is the bare attribute name string (no `$` prefix).
- [ ] `dataValueType` is bound to an attribute or set to a `DataValueType` integer literal.
- [ ] `cellView` is also set on the same column for the read-only display state.
- [ ] If type-specific props are needed, they are placed inside `config` (not at the top level of `editingCellView`).
