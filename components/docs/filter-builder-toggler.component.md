# How to Add a Filter Builder Toggler (`crt.FilterBuilderToggler`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FilterBuilderToggler` into a Creatio Freedom UI page schema.
>
> A `crt.FilterBuilderToggler` is a toggle button that shows or hides a linked
> `crt.FilterBuilderSource` panel. It references the source by name via `viewElement` and emits
> `visibilityChanged` when the user clicks, allowing the filter panel to respond and toggle its
> own visibility.

## Metadata

- **Category**: filter
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FilterBuilderToggler"` and a `viewElement` reference. **Always present.** |
| 2 | `handlers` (optional) | A request handler for `visibilityChanged` if custom toggle logic is needed beyond what the linked `crt.FilterBuilderSource` handles natively. |

`crt.FilterBuilderToggler` owns no datasource and no page attributes. It is view-only.

### 1.1 Naming convention

```
FilterBuilderToggler_<id>    // view element name; <id> is any short unique slug
FilterBuilderSource_<id>     // the linked source element's name (referenced by viewElement)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FilterBuilderToggler_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FilterBuilderToggler",
    "viewElement": "FilterBuilderSource_abc123",
    "schemaName": "Contact",
    "_filterOptions": { "expose": [], "from": null },
    "visible": true
  }
}
```

`viewElement` must match the `name` of a `crt.FilterBuilderSource` on the same page.
`_filterOptions` is set automatically if both the toggler and source are added via the designer.
When adding manually, set `_filterOptions: { "expose": [], "from": null }` — the source manages
filter exposure.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FilterBuilderToggler` are in `ComponentRegistry.json` under `componentType: "crt.FilterBuilderToggler"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`visibilityChanged` emits `FilterBuilderVisibilityRequestData`:

```ts
interface FilterBuilderVisibilityRequestData {
  viewName: string;    // the name of the linked FilterBuilderSource view element
  schemaName: string;  // current schemaName value
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — filter source
{
  "operation": "insert",
  "name": "FilterBuilderSource_8i555cb",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "applyFiltersAutomatically": true,
    "type": "crt.FilterBuilderSource",
    "stretch": true,
    "_filterOptions": {
      "expose": [
        {
          "attribute": "FilterBuilderSource_8i555cb_Filter",
          "converters": [{ "converter": "crt.FilterBuilderAttributeConverter" }]
        }
      ],
      "from": "FilterBuilderSource_8i555cb_Value"
    },
    "layoutConfig": { "alignSelf": "stretch", "width": 328 },
    "visible": true,
    "schemaName": "Contact"
  }
}
```

```jsonc
// viewConfigDiff — toggler
{
  "operation": "insert",
  "name": "FilterBuilderToggler_8i555cb",
  "parentName": "ToolsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FilterBuilderToggler",
    "_filterOptions": { "expose": [], "from": null },
    "visible": true,
    "viewElement": "FilterBuilderSource_8i555cb"
  }
}
```

---

## 6. Driving from page state

`crt.FilterBuilderToggler` does not have `propertyBindable` inputs for `viewElement` or
`schemaName` — these are set statically at design time. Use `visible` to show/hide the toggler
from page state:

```jsonc
"visible": "$FilterBuilderToggler_visible"
```

---

## 7. Common pitfalls

1. **`viewElement` does not match any element name** — the toggler button becomes permanently disabled (sets `isDisabled: true` internally when `viewElement` is empty or unresolved).
2. **Adding a toggler without a source** — `visibilityChanged` fires but nothing responds; always pair with a `crt.FilterBuilderSource` on the same page.
3. **`_filterOptions` not set** — the platform expects `{ expose: [], from: null }` on the toggler even though it does not expose filters itself.
4. **`schemaName` mismatch with source** — both toggler and source should reference the same entity; a mismatch leads to the source not recognizing the toggle event.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FilterBuilderToggler"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `viewElement` set to the exact `name` of the paired `crt.FilterBuilderSource`.
- [ ] `_filterOptions: { "expose": [], "from": null }` present in `values`.
- [ ] `schemaName` matches the source's `schemaName`.
- [ ] Paired `crt.FilterBuilderSource` exists on the same page with a matching `name`.
