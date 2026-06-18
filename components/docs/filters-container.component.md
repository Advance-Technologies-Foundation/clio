# How to Add a Filters Container (`crt.FiltersContainer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FiltersContainer` into a Creatio Freedom UI page schema.
>
> A `crt.FiltersContainer` renders an embedded filter editor (via an external resource frame)
> for the specified entity schema. It serializes filter state as a JSON string through
> `filterData` / `filterDataChange`, so both the initial filter and user edits live in a
> page attribute.

## Metadata

- **Category**: filter
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FiltersContainer"`, `entitySchemaName`, and a `filterData` binding. **Always present.** |
| 2 | `viewModelConfigDiff` | An attribute to hold the serialized filter JSON string. |

No `modelConfigDiff` or `handlers` are required for basic usage.

### 1.1 Naming convention

```
FiltersContainer_<id>         // view element name; <id> is any short unique slug
$FiltersContainer_<id>_filter // $-prefix attribute holding the JSON filter string
```

---

## 2. Step-by-step recipe

### 2.1 Declare the filter attribute (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FilterData": { "value": null }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FiltersContainer_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FiltersContainer",
    "entitySchemaName": "$EntityName",
    "filterData": "$FilterData",
    "filterDataChange": { "request": "crt.FiltersContainerFilterChangeRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Handle filter changes in `handlers`

```jsonc
{
  "request": "crt.FiltersContainerFilterChangeRequest",
  "handler": async (request, next) => {
    // request carries the new JSON filter string
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FiltersContainer` are in `ComponentRegistry.json` under `componentType: "crt.FiltersContainer"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`filterData` is a serialized JSON string representing a `FilterGroup` (Creatio extended filter
format). When `entitySchemaName` changes while a `filterData` value is set for a different
entity, the component automatically resets `filterData` to `null` and emits `filterDataChange(null)`.

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FilterData": { "value": null },
      "EntityName": { "value": "Contact" }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FiltersContainer_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FiltersContainer",
    "filterData": "$FilterData",
    "entitySchemaName": "$EntityName"
  }
}
```

---

## 6. Driving from page state

Both `entitySchemaName` and `filterData` are `propertyBindable` — use `$AttributeName` to drive
them from page state. When `entitySchemaName` is changed at runtime, the component debounces its
initialization by 50 ms to avoid double loads. `filterDataChange` emits the new serialized JSON
whenever the user edits filters; assign the result back to the attribute via a handler or an
inline binding.

---

## 7. Common pitfalls

1. **`filterData` not bound to an attribute** — without a `$Attribute` binding the filter state is lost on navigation; always persist in a page attribute.
2. **Passing a plain JS object to `filterData`** — the property expects a JSON string; pass `JSON.stringify(filterObj)` or the raw `$Attribute` that already holds the string.
3. **Changing `entitySchemaName` at runtime** — the component resets `filterData` to `null` when the entity changes; update both attributes together in the same handler to avoid a transient empty filter.
4. **`filterDataChange` not wired** — user edits are silently discarded; always bind `filterDataChange` to persist state changes.
5. **`entitySchemaName` is empty** — the embedded frame will not load; ensure the attribute is set before the component renders.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FiltersContainer"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `entitySchemaName` bound to a page attribute holding the target entity name.
- [ ] `filterData` bound to a `$Attribute` (declared in `viewModelConfigDiff`) initialized to `null`.
- [ ] `filterDataChange` wired so user edits are persisted back to the attribute.
