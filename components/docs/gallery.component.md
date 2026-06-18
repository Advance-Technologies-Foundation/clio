# How to Add a Gallery (`crt.Gallery`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Gallery` into a Creatio Freedom UI page schema.
>
> `crt.Gallery` renders a tiled collection of items (typically with images or icons). It
> binds to a collection attribute fed by an `EntityDataSource` and supports single or
> multi selection, bulk actions, and infinite-scroll pagination.

For the underlying datasource/collection mechanics, see
crt.DataGrid guide.
This document covers the gallery-specific configuration.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: `crt.MenuItem`, `crt.MenuLabel`, `crt.MenuDivider` (in the `bulkActions` slot)

---

## 1. Mental model — the 3 places you must edit (same shape as DataGrid)

| # | Section | What you add |
|---|---|---|
| 1 | `modelConfigDiff` | A `crt.EntityDataSource` for the gallery items. |
| 2 | `viewModelConfigDiff` | A collection attribute bound to that datasource. |
| 3 | `viewConfigDiff` | An `insert` op with `type: "crt.Gallery"`, `items: "$<collection>"`. |

### 1.1 Naming convention

```
Gallery_<id>             // view element name
Gallery_<id>DS           // datasource for items
$Gallery_<id>            // items binding
Gallery_<id>DS_Id        // primary id attribute
```

---

## 2. Step-by-step recipe

### 2.1 Add the datasource (`modelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "Gallery_xkp4rDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "Product",
          "attributes": {
            "Id":           { "path": "Id" },
            "Name":         { "path": "Name" },
            "ImageReference": { "path": "ImageReference" },
            "Price":        { "path": "Price" }
          }
        }
      }
    }
  }
}
```

### 2.2 Declare the collection attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Gallery_xkp4r": {
        "isCollection": true,
        "modelConfig": { "path": "Gallery_xkp4rDS" },
        "viewModelConfig": {
          "attributes": {
            "Gallery_xkp4rDS_Id":             { "modelConfig": { "path": "Gallery_xkp4rDS.Id" } },
            "Gallery_xkp4rDS_Name":           { "modelConfig": { "path": "Gallery_xkp4rDS.Name" } },
            "Gallery_xkp4rDS_ImageReference": { "modelConfig": { "path": "Gallery_xkp4rDS.ImageReference" } },
            "Gallery_xkp4rDS_Price":          { "modelConfig": { "path": "Gallery_xkp4rDS.Price" } }
          }
        }
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Gallery_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Gallery",
    "items": "$Gallery_xkp4r",
    "itemConfig": {
      "defaultSize": "medium",
      "templateValuesMapping": {
        "caption":     "Gallery_xkp4rDS_Name",
        "description": "Gallery_xkp4rDS_Price",
        "image":       "Gallery_xkp4rDS_ImageReference",
        "id":          "Gallery_xkp4rDS_Id"
      }
    },
    "ariaLabel": "Products",
    "mode": "component-type",
    "selectable": true,
    "multiselect": false,
    "selectedItemId": "$Gallery_xkp4r_selectedId",
    "selectionState": "$Gallery_xkp4r_selectionState",
    "bulkActions": [
      { "type": "crt.MenuItem", "caption": "#ResourceString(Bulk_Archive)#", "icon": "archive", "clicked": { "request": "crt.ArchiveProductsRequest" } }
    ],
    "itemClick":   { "request": "crt.OpenProductRequest" },
    "selectedItemIdChange": { "request": "crt.UpdateProductSelectionRequest" },
    "paginationChange":     { "request": "crt.LoadMoreProductsRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 6 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Gallery` are in `ComponentRegistry.json` under `componentType: "crt.Gallery"`. This guide
covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. `itemConfig` shape — wiring tile content

Every PackageStore Gallery uses `itemConfig` to map column-attributes onto tile slots; without it the tiles render but contain no caption/image/description. Canonical shape:

```jsonc
"itemConfig": {
  "defaultSize": "medium",            // SizeEnum — controls tile size
  "templateValuesMapping": {
    "caption":     "<Gallery>DS_Name",         // attribute name for the title
    "description": "<Gallery>DS_Description",  // attribute name for the subtitle
    "image":       "<Gallery>DS_ImageReference", // attribute name for the image url
    "id":          "<Gallery>DS_Id"            // attribute name for the row id (selection)
  }
}
```

Notes:
- `defaultSize` accepts `SizeEnum` values (`"small"`, `"medium"`, `"large"`, `"extra-large"`, etc.). Galleries usually do **not** style via `itemStyles`; use `defaultSize` instead.
- `templateValuesMapping` field names (`caption`, `description`, `image`, `id`) are the tile-template slot names. The values are attribute names — they must match the projected attribute names you declared in `viewModelConfigDiff` (typically `<Gallery>DS_<EntityColumn>`).
- The `id` slot is the item key used for selection (`itemKeyProperty`). Always map it to the projected `<Gallery>DS_Id` attribute, otherwise selection and `selectedItemId`/`selectionState` cannot track rows.
- The mapping is also valid for image-only galleries: include only `image` and `id`.
- `itemConfig.defaultImage` sets a placeholder image (url or data) used when a row has no image.

## 5. `bulkActions` shape

```jsonc
"bulkActions": [
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(Bulk_Archive)#",
    "icon": "archive",
    "iconColor": "default",
    "clicked": { "request": "crt.ArchiveProductsRequest" }
  },
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(Bulk_Delete)#",
    "icon": "delete",
    "iconColor": "warn",
    "clicked": { "request": "crt.DeleteProductsRequest" }
  }
]
```

The handlers receive `request.$context.<Gallery>_selectionState` so they know which items
the user picked.

`bulkActions` are only rendered when `multiselect: true` **and** at least one item is selected.

Each entry is a `crt.MenuItem` (nested submenus allowed via its own `items`). To pass the current
selection to the handler, the canonical pattern builds filters from the selection state attribute:

```jsonc
"clicked": {
  "request": "crt.DeleteRecordsRequest",
  "params": {
    "dataSourceName": "Gallery_xkp4rDS",
    "filters": "$Gallery_xkp4r | crt.ToCollectionFilters : 'Gallery_xkp4r' : $Gallery_xkp4r_SelectionState | crt.SkipIfSelectionEmpty : $Gallery_xkp4r_SelectionState"
  }
}
```

Some requests take the selection state directly instead of filters (e.g. `crt.MergeRecordsRequest`
with `"selectionState": "$Gallery_xkp4r_SelectionState"`).

---

## 6. Copy-paste minimal example

See § 2 above — the example is already minimal.

---

## 7. Common pitfalls

(In addition to the generic collection-binding pitfalls — see § 7 of crt.DataGrid guide.)

1. **No image column in the datasource** — galleries are mostly visual; binding without an image column produces empty tiles. Either project an `ImageReference`-type column or rely on the default placeholder.
2. **`multiselect: true` without `selectionState` binding** — selections are kept internal and lost on each render. Always bind `selectionState: "$attr"`.
3. **`selectedItemId` and `selectionState` both set without coordination** — single-select id and multi-select state can drift. Pick one mode (`multiselect: true` → use `selectionState`; otherwise → use `selectedItemId`).
4. **`itemClick` wired to "open record" instead of `itemDblClick`** — single click also fires selection, so opening a record on click conflicts with multi-select gestures. Use `itemDblClick` for navigation when `selectable: true`.
5. **Tile size set via `itemStyles` but cells too narrow** — large tiles plus narrow `colSpan` produce horizontal scroll. Match `colSpan` to the tile grid you want.
6. **Forgetting `paginationChange.request`** — the gallery emits the event when the user scrolls past the last page, but nothing reloads. Always wire the handler for non-trivial datasets.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Gallery"`, unique `name`, `propertyName: "items"`.
- [ ] `items: "$<collectionAttr>"` references a collection bound to `crt.EntityDataSource` (see DataGrid for the diff shape).
- [ ] `itemConfig.templateValuesMapping` maps the projected attribute names onto `caption` / `description` / `image` / `id` slots; `itemConfig.defaultSize` set.
- [ ] `ariaLabel` provided (galleries are often image-heavy and need an accessible name).
- [ ] `selectable`/`multiselect` set intentionally; matching `selectedItemId` or `selectionState` bound to attributes.
- [ ] `paginationChange.request` wired for non-trivial datasets.
- [ ] `layoutConfig` provides generous `rowSpan`.
