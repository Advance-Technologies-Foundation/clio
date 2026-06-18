# How to Add a Phone Table Cell (`crt.TablePhoneCell`) to a Freedom UI Page

> Audience: code agent inserting `crt.TablePhoneCell` into a Creatio Freedom UI page schema.
> A `crt.TablePhoneCell` is a read-only display cell used inside a `crt.DataGrid` column to render phone number values as clickable links (with built-in telephony integration). It is wired as the `cellView` property of a column definition, not inserted as a standalone view element.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.DataGrid` column definition (as `cellView` value)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A column in a `crt.DataGrid` with `"cellView": { "type": "crt.TablePhoneCell", "value": "..." }`. |

`crt.TablePhoneCell` is **not** inserted as an independent element with `parentName`/`propertyName: "items"`. It lives inside the `cellView` slot of a column in `crt.DataGrid`. The module is lazy-loaded on demand when the grid renders phone columns.

### 1.1 Naming convention
```
// The cellView object has no `name` field.
// Use the attribute path in `value` to bind to the phone column.
```

---

## 2. Step-by-step recipe

### 2.1 Wire `crt.TablePhoneCell` as `cellView` inside a `crt.DataGrid` column

```jsonc
{
  "id": "b2c3d4e5-0000-0000-0000-000000000001",
  "code": "MyDetail_Phone",
  "caption": "#ResourceString(MyDetail_Phone)#",
  "dataValueType": 28,
  "cellView": {
    "type": "crt.TablePhoneCell",
    "value": "$MyDetail.MyDetail_Phone",
    "disableLink": false
  },
  "editingCellView": {
    "type": "crt.DataTableEditPhoneCell",
    "control": "$MyDetail.MyDetail_Phone"
  }
}
```

When `disableLink` is `false` (the default), the phone number renders as a clickable link. If telephony is enabled in the platform, clicking initiates a call; otherwise `tel:` fallback is used.

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TablePhoneCell` are in `ComponentRegistry.json` under `componentType: "crt.TablePhoneCell"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

Cell `inputs` available at runtime (from the registry):
```ts
interface TablePhoneCellInputs {
  column: BaseColumnDefinition;  // injected by crt.DataGrid
  record: DataItem;              // injected by crt.DataGrid
  value: TValue;                 // bound via "value": "$..." in cellView
  disableLink: boolean;          // set in cellView values; default false
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — DataGrid with a phone column
{
  "operation": "insert",
  "name": "ContactsGrid",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "items": "$ContactsDetail",
    "primaryColumnName": "ContactsDetail_Id",
    "columns": [
      {
        "id": "b2c3d4e5-0000-0000-0000-000000000001",
        "code": "ContactsDetail_Phone",
        "caption": "#ResourceString(ContactsDetail_Phone)#",
        "dataValueType": 28,
        "cellView": {
          "type": "crt.TablePhoneCell",
          "value": "$ContactsDetail.ContactsDetail_Phone"
        }
      }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 6 }
  }
}
```

---

## 7. Common pitfalls

- **Trying to `insert` as a standalone view element.** `crt.TablePhoneCell` is only valid inside `cellView`/`editingCellView` of a `crt.DataGrid` column.
- **`disableLink: true` when telephony-dependent actions are required.** When the link is disabled the call action and `tel:` fallback are suppressed entirely.
- **Wrong `dataValueType`.** Phone columns are typically `Terrasoft.DataValueType.TEXT` (28); mismatching the type causes the filter/sort to behave unexpectedly.
- **Lazy-loading not resolved.** `crt.TablePhoneCell` is delivered via a lazy Angular module. If the module bundle is absent or misconfigured, the cell renders blank. Ensure the phone-input library is included in the app bundle.
- **Pairing with `crt.DataTableEditTextCell` instead of `crt.DataTableEditPhoneCell`.** When inline editing is enabled, use `crt.DataTableEditPhoneCell` as `editingCellView` for consistent phone formatting.

---

## 8. Quick checklist

- [ ] `crt.TablePhoneCell` placed in `cellView` of a `crt.DataGrid` column, not as a top-level view element.
- [ ] `value` binding is `"$<collectionAttr>.<columnCode>"`.
- [ ] `id` on the column definition is a unique GUID.
- [ ] `disableLink` set appropriately (`false` for clickable links, `true` for plain text).
- [ ] `editingCellView` is `crt.DataTableEditPhoneCell` if inline editing is enabled.
