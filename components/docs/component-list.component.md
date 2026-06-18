# How to Add a Component List (`crt.ComponentList`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ComponentList` into a Creatio Freedom UI page schema.
> A `crt.ComponentList` renders a collection of view-model items, each projected through a shared `itemViewConfig` template; it is a low-level infrastructure component typically used to project a `BaseViewModelCollection` into a repeating set of view elements.

## Metadata

- **Category**: containers
- **Container**: no (children are generated dynamically from `viewModels`)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, `crt.HeaderContainer`, root page container
- **Typical children**: none (items are generated from `viewModels` via `itemViewConfig`)

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ComponentList"` and `viewModels` / `itemViewConfig` values. **Always present.** |
| 2 | `viewModelConfigDiff` | A `$-prefix` attribute bound to `viewModels` when driven from page state. |

`crt.ComponentList` is **view-only** — no model or datasource of its own. It projects an existing `BaseViewModelCollection` attribute into repeated view elements.

### 1.1 Naming convention

```
ComponentList_<id>        // view element name; <id> = any short unique slug
$ComponentList_<id>       // $-prefix attribute, when viewModelConfigDiff is touched
```

---

## 2. Step-by-step recipe

### 2.1 Insert the component list (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ComponentList_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ComponentList",
    "viewModels": "$MyViewModelCollection",
    "itemViewConfig": {
      "type": "crt.FlexContainer",
      "items": []
    }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ComponentList` are in `ComponentRegistry.json` under `componentType: "crt.ComponentList"`. This guide
covers only the assembly mechanics.

---

## 7. Common pitfalls

1. **Using `crt.ComponentList` for static layouts** — this component is for dynamically-generated repeated items; for static children, use `crt.FlexContainer` or `crt.GridContainer` instead.
2. **Passing a plain array to `viewModels`** — `viewModels` expects a `BaseViewModelCollection` (reactive); passing a static array prevents change detection from working.
3. **Omitting `itemViewConfig`** — without a view config template, items render as empty nodes.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ComponentList"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `viewModels` bound to a `BaseViewModelCollection` attribute via `$-prefix`.
- [ ] `itemViewConfig` provided as a valid view element config object.
