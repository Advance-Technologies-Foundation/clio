# How to Add a Typed Value Cell (`crt.TypedValueCell`) to a Freedom UI Page

> Audience: code agent inserting `crt.TypedValueCell` into a Creatio Freedom UI page schema.
> `crt.TypedValueCell` is a **polymorphic table cell** that delegates rendering to a specific typed cell
> component based on `dataValueType`; it selects the appropriate sub-component (text, boolean, lookup,
> date-time) at runtime and passes `valueAttribute` through.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, table row containers
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.TypedValueCell"`, `dataValueType`, and `valueAttribute`. **Always present.** |

`crt.TypedValueCell` is **view-only** — it owns no datasource. It dynamically renders one of:
- `crt.TableTextCell` (default, for `DataValueType.Text` and unknown types)
- `crt.TableBooleanCell` (for `DataValueType.Boolean`)
- `crt.DataTableEditLookupCell` (for `DataValueType.Lookup`)
- `crt.DataTableEditDateTimeCell` (for `DataValueType.Date` or `DataValueType.DateTime`)

### 1.1 Naming convention

```
TypedValueCell_<id>          // view element name
$TypedValueCell_<id>_value   // bound value attribute (optional direct binding)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "TypedValueCell_status",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TypedValueCell",
    "dataValueType": 1,
    "valueAttribute": "StatusValue",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TypedValueCell` are in `ComponentRegistry.json` under `componentType: "crt.TypedValueCell"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// dataValueType — numeric enum from @creatio-devkit/common
// Key values for sub-component selection:
enum DataValueType {
  Text       = 1,
  Boolean    = 12,
  Lookup     = 10,
  Date       = 7,
  DateTime   = 8,
  // (other values fall through to crt.TableTextCell default)
}
```

`valueAttribute` is a string attribute name (without `$` prefix); the component appends `$` internally
when building the delegate config (e.g. `"StatusValue"` becomes `"$StatusValue"` in the delegated cell's
`control` or `value` binding).

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — boolean cell
{
  "operation": "insert",
  "name": "TypedValueCell_active",
  "parentName": "RowContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TypedValueCell",
    "dataValueType": 12,
    "valueAttribute": "IsActive",
    "visible": true
  }
}

// viewConfigDiff — lookup cell
{
  "operation": "insert",
  "name": "TypedValueCell_owner",
  "parentName": "RowContainer",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.TypedValueCell",
    "dataValueType": 10,
    "valueAttribute": "OwnerValue",
    "visible": true
  }
}
```

---

## 6. Driving from page state

`value` is a direct binding target if you want to pass a pre-resolved value instead of an attribute name:

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "StatusValue": { "value": null }
    }
  }
}

// viewConfigDiff.values
"valueAttribute": "StatusValue"
```

Both `value` and `valueAttribute` are `@CrtInput` — use `valueAttribute` (the attribute name string) for
the common datasource-backed pattern; `value` is for direct value injection.

---

## 7. Common pitfalls

1. **`dataValueType` must be set.** When `null` or absent, `_getTemplate()` returns `null` and the cell renders nothing.
2. **`valueAttribute` is a plain string without `$`.** The component prepends `$` when constructing the delegate; do not pass `"$StatusValue"` — it becomes `"$$StatusValue"`.
3. **Unknown `dataValueType` values fall through to `crt.TableTextCell`.** The switch has a `default` branch that always produces a text cell.
4. **`value` vs `valueAttribute`** — `value` is the raw value; `valueAttribute` is the attribute name used by the delegate cell for its `control` binding. Do not set both unless intentional.
5. **The delegate cell type changes when `dataValueType` changes.** If `dataValueType` is bound to an attribute, be aware the entire inner component is replaced on each change.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.TypedValueCell"`, unique `name`.
- [ ] `dataValueType` set to the correct `DataValueType` numeric value.
- [ ] `valueAttribute` is a plain string (no `$` prefix) matching an attribute name on the page.
- [ ] `layoutConfig` provided when parent is `crt.GridContainer`.
- [ ] Do not wire `crt.TypedValueCell` to `handlers` — it has no outputs.
