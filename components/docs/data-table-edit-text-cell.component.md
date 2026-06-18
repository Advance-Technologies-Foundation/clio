# How to Add a DataTable Edit Text Cell (`crt.DataTableEditTextCell`) to a DataTable Column

> Audience: code agent configuring an editable `crt.DataTable` column in a Creatio Freedom UI page schema.
>
> `crt.DataTableEditTextCell` is the inline text editor that appears when a user activates a cell in an editable
> `crt.DataTable` column. It is **not** inserted via `viewConfigDiff` — it is set as the `editingCellView`
> property on a column definition object inside the `crt.DataTable` values block.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: column `editingCellView` slot inside `crt.DataTable`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` — `crt.DataTable` column's `editingCellView` | An inline object `{ "type": "crt.DataTableEditTextCell", "control": "$AttributeName" }`. **Always present.** |

There is **no** separate `insert` op for this element. The cell view lives inline inside the column definition of a
`crt.DataTable` insert (or merge) op. No `modelConfigDiff` or `viewModelConfigDiff` changes are needed for the
cell itself — the bound attribute is owned by the DataTable's datasource.

### 1.1 Naming convention

```
// no standalone name — the cell view is an anonymous inline config
// the bound attribute follows DataTable column binding conventions, e.g.:
$DetailDS_ColumnName
```

---

## 2. Step-by-step recipe

### 2.1 Add or update the DataTable column to include `editingCellView`

Inside the `crt.DataTable` insert op in `viewConfigDiff`, find the target column in `columns` and set
`editingCellView`:

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
        "code": "Name",
        "caption": "#ResourceString(MyDataTable_column_Name_caption)#",
        "dataValueType": 1,
        "width": 200,
        "cellView": {
          "type": "crt.TableTextCell",
          "value": "$MyDetailDS_Name"
        },
        "editingCellView": {
          "type": "crt.DataTableEditTextCell",
          "control": "$MyDetailDS_Name"
        }
      }
    ]
  }
}
```

### 2.2 (Optional) Apply a mask

```jsonc
"editingCellView": {
  "type": "crt.DataTableEditTextCell",
  "control": "$MyDetailDS_PhoneRaw",
  "mask": { "mask": "+{1}(000)000-0000" }
}
```

The `mask` value is an `InputMaskConfig` object passed directly to the underlying masked input.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DataTableEditTextCell` are in `ComponentRegistry.json` under `componentType: "crt.DataTableEditTextCell"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// InputMaskConfig — see libs/.../input/input-mask-config.ts
interface InputMaskConfig {
  mask: string;               // imask pattern string, e.g. "+{7}(000)000-0000"
  lazy?: boolean;             // hide placeholder until typing starts (default true)
  overwrite?: boolean;
  autofix?: boolean;
  // …other imask options
}
```

---

## 5. Copy-paste minimal example

```jsonc
// DataTable column with editingCellView (real pattern from NestedCollectionBugTest.js)
{
  "id": "col-abc123",
  "code": "Caption",
  "caption": "Caption",
  "dataValueType": 1,
  "width": 500,
  "cellView": {
    "type": "crt.TableTextCell",
    "value": "$MyDetail_Caption"
  },
  "editingCellView": {
    "type": "crt.DataTableEditTextCell",
    "control": "$MyDetail_Caption"
  }
}
```

---

## 6. Driving from page state

The `readonly` input is `propertyBindable`:

```jsonc
"editingCellView": {
  "type": "crt.DataTableEditTextCell",
  "control": "$MyDetailDS_Name",
  "readonly": "$MyTable_readonly"
}
```

---

## 7. Common pitfalls

1. **Using an `insert` op instead of `editingCellView`** — `crt.DataTableEditTextCell` is not a standalone
   view element; placing it with a top-level `insert` op will not render correctly.
2. **Binding `control` to a raw value instead of an attribute** — `control` must receive a `FormControl`
   (bound via `$AttributeName`); binding to a plain string has no effect.
3. **Omitting `editingCellView` on read-only columns** — a column without `editingCellView` is implicitly
   read-only; adding `readonly: true` on the cell itself is redundant but harmless.
4. **Setting `mask` to a string** — `mask` accepts an `InputMaskConfig` object, not a raw string; wrap the
   pattern in `{ "mask": "<pattern>" }`.
5. **Forgetting `cellView`** — `cellView` and `editingCellView` are independent; if `cellView` is missing the
   display cell renders blank even when the editing cell works.
6. **Using `tooltip` for validation messages** — `tooltip` is a static hint text, not a dynamic error message;
   use form validation on the bound `FormControl` for errors.

---

## 8. Quick checklist

- [ ] `editingCellView` is set inside the column definition (not as a standalone `insert` op).
- [ ] `control` is bound to a `$AttributeName` referencing a `FormControl` owned by the DataTable datasource.
- [ ] `cellView` is also set on the same column so the read state renders correctly.
- [ ] If a `mask` is needed, value is `InputMaskConfig` object form `{ "mask": "..." }`, not a plain string.
- [ ] `readonly` is bound to a `$AttributeName` or a literal `false`/`true` as needed.
