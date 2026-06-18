# How to Add a DataTable Edit Phone Cell (`crt.DataTableEditPhoneCell`) to a DataTable Column

> Audience: code agent configuring an editable phone column in a `crt.DataTable` in a Creatio Freedom UI
> page schema.
>
> `crt.DataTableEditPhoneCell` is the inline phone-number editor shown when a user activates a phone-type
> cell in an editable `crt.DataTable` column. It embeds a country-code selector and formats the value
> according to the chosen locale. It is **not** inserted via `viewConfigDiff` — it is set as the
> `editingCellView` property on the column definition.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: column `editingCellView` slot inside `crt.DataTable`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` — `crt.DataTable` column's `editingCellView` | An object `{ "type": "crt.DataTableEditPhoneCell", "control": "$Attr" }`. **Always present.** |

There is **no** standalone `insert` op for this cell. No `modelConfigDiff` or `viewModelConfigDiff` changes are
needed for the cell itself.

### 1.1 Naming convention

```
// no standalone name — inline config
// bound attribute follows DataTable column binding convention, e.g.:
$DetailDS_Phone
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
        "code": "Phone",
        "caption": "#ResourceString(MyDataTable_column_Phone_caption)#",
        "dataValueType": 1,
        "cellView": {
          "type": "crt.TableTextCell",
          "value": "$MyDetailDS_Phone"
        },
        "editingCellView": {
          "type": "crt.DataTableEditPhoneCell",
          "control": "$MyDetailDS_Phone",
          "displayAsPhone": true
        }
      }
    ]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DataTableEditPhoneCell` are in `ComponentRegistry.json` under `componentType: "crt.DataTableEditPhoneCell"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

No additional custom types. All inputs are primitives or `FormControl`.

---

## 5. Copy-paste minimal example

No direct PackageStore schema match found. Based on the `editingCellView` column convention:

```jsonc
// Phone column with country-code selector
{
  "id": "col-phone",
  "code": "MobilePhone",
  "caption": "Mobile Phone",
  "dataValueType": 1,
  "cellView": {
    "type": "crt.TableTextCell",
    "value": "$ContactDS_MobilePhone"
  },
  "editingCellView": {
    "type": "crt.DataTableEditPhoneCell",
    "control": "$ContactDS_MobilePhone",
    "displayAsPhone": true
  }
}
```

---

## 6. Driving from page state

The `readonly` input is `propertyBindable`:

```jsonc
"editingCellView": {
  "type": "crt.DataTableEditPhoneCell",
  "control": "$DetailDS_Phone",
  "readonly": "$MyTable_readonly"
}
```

---

## 7. Common pitfalls

1. **Using an `insert` op instead of `editingCellView`** — this cell is not a standalone view element.
2. **Binding `control` to a raw string** — `control` must receive a `FormControl`; bind via `$AttributeName`.
3. **Setting `displayAsPhone: false` on a phone column** — the country-code selector and phone formatting are
   skipped; the cell renders as a plain text input.
4. **`isViewCellMode: true` in `editingCellView`** — this flag is for internal view-mode display; the
   `editingCellView` slot is always in edit mode, so setting it explicitly is redundant.
5. **Forgetting `cellView`** — `cellView` and `editingCellView` are independent; without `cellView` the
   display-mode cell renders blank.

---

## 8. Quick checklist

- [ ] `editingCellView` is set inside the DataTable column, not as a standalone `insert` op.
- [ ] `control` is bound to a `$AttributeName` referencing a `FormControl`.
- [ ] `displayAsPhone: true` is set when the column holds a phone number value.
- [ ] `cellView` is also set on the same column for the read-only display state.
- [ ] `readonly` is bound or set to a literal value as needed.
