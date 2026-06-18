# How to Add a Multi List (`crt.MultiList`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MultiList` into a Creatio Freedom UI page schema.
>
> `crt.MultiList` is a scrollable list of view-model items with built-in search, paging, and keyboard
> navigation. It uses a `contentSlots` mechanism (`items` slot) for custom item templates, so child
> view elements are injected via the slot rather than as `viewConfigDiff` children.

## Metadata

- **Category**: display
- **Container**: yes — custom item templates go into the `items` content slot
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.SidePanel`, `crt.TabContainer`
- **Typical children**: custom item template view elements via the `items` slot

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.MultiList"` and starting values. **Always present.** |
| 2 | `viewModelConfigDiff` | Attribute declarations for `itemsList`, `selectedItemId`, `currentPage`, `searchFilter` when bound. |

`crt.MultiList` does **not** own a datasource itself — data arrives through `itemsList` (a `BaseViewModelCollection`
bound to a `$attribute`). Wire `loadData` to populate the collection and `currentPageChange` to implement paging.

### 1.1 Naming convention

```
MultiList_<id>              // view element name
$MultiList_<id>_ItemsList   // attribute holding the collection
```

---

## 2. Step-by-step recipe

### 2.1 Declare attributes (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "MultiList_ItemsList": { "value": [] },
    "MultiList_SelectedItemId": { "value": null },
    "MultiList_CurrentPage": { "value": 0 },
    "MultiList_SearchFilter": { "value": "" }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MultiList_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MultiList",
    "itemsList": "$MultiList_ItemsList",
    "selectedItemId": "$MultiList_SelectedItemId",
    "currentPage": "$MultiList_CurrentPage",
    "searchFilter": "$MultiList_SearchFilter",
    "loadData": {
      "request": "crt.LoadMultiListDataRequest",
      "params": {
        "search": "@event.search",
        "rowsToLoadCount": "@event.count"
      }
    },
    "currentPageChange": {
      "request": "crt.MultiListPageChangeRequest",
      "params": { "page": "@event" }
    },
    "itemClicked": {
      "request": "crt.MultiListItemClickRequest",
      "params": { "id": "@event.id" }
    }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MultiList` are in `ComponentRegistry.json` under `componentType: "crt.MultiList"`. This guide covers
only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "WorkplaceMultiList",
  "parentName": "MainFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MultiList",
    "itemsList": "$WorkplaceItemsList",
    "selectedItemId": "$WorkplaceSelectedItemId",
    "currentPage": "$WorkplaceCurrentPage",
    "searchFilter": "$WorkplaceSearchFilter",
    "loadData": {
      "request": "crt.WorkplaceLoadDataRequest",
      "params": {
        "search": "@event.search",
        "rowsToLoadCount": "@event.count"
      }
    },
    "itemClicked": {
      "request": "crt.WorkplaceItemClickRequest",
      "params": { "id": "@event.id" }
    }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare the data attribute
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "WorkplaceItemsList": { "value": [] }
  }
}

// viewConfigDiff.values — bind via $-prefix
"itemsList": "$WorkplaceItemsList"
```

---

## 7. Common pitfalls

1. **`itemsList` not a `BaseViewModelCollection`.** Passing a plain array does not work; the collection must be built server-side or via a `BaseViewModelGenerator`; the attribute value should start as `[]` and be replaced by the load handler.
2. **`loadData` fired but `itemsList` not updated.** The `loadData` handler must push items into the bound attribute; if it only reads data the list stays empty.
3. **`isSearchDisabled` vs `searchDisableFeatureName`.** `isSearchDisabled` is a direct boolean flag; `searchDisableFeatureName` is a feature-flag key — use whichever is appropriate (not both simultaneously).
4. **`isCombinedMode` not matching the visual context.** Combined mode adds a CSS class for a specific side-panel layout; enable it only when the list is rendered inside a combined-mode host.
5. **Missing `currentPageChange` handler.** Without it the `currentPage` attribute is never updated and repeated scroll-to-end events load the same page.
6. **Expecting `items` slot children to appear as `viewConfigDiff` siblings.** The `items` slot uses `ng-content` projection — templates are injected at schema time, not at runtime through diff ops.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.MultiList"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `itemsList` bound to a `$attribute` that the `loadData` handler populates.
- [ ] `loadData.request` wired to a handler that builds and assigns the collection.
- [ ] `itemClicked.request` wired if item selection needs custom logic.
- [ ] `currentPage` and `currentPageChange` both wired for paging.
- [ ] `searchFilter` and `searchFilterChange` both wired if search is enabled.
