# How to Add a Gallery (`crt.Gallery`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Gallery` into a mobile page schema.
> Renders a card gallery with image thumbnails on a mobile page; tapping a card can navigate to a specific record page.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.Scaffold`, `crt.GridContainer`, any layout container
- **Typical children**: none (item configuration is done via `itemConfig`, not child elements)

---
## 1. Mental model
`crt.Gallery` shows a grid of cards, each with an image thumbnail drawn from a record collection.
Bind `items` to the PDS collection attribute. Use `itemConfig` to configure each card's appearance.
Set `useSpecificPage: true` and `specificPage` to open a named page schema when the user taps
a card instead of the default record edit page.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "ProductGallery",
  "values": {
    "type": "crt.Gallery",
    "items": "$PDS_Products"
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Gallery` are in
`ComponentRegistry.json` under `componentType: "crt.Gallery"`.

Key inputs:
| Property | Type | Description |
|---|---|---|
| `items` | string (binding) | Data attribute binding for the gallery collection (e.g. `$PDS_Products`). |
| `itemConfig` | object | Gallery item configuration (image column, title column, etc.). |
| `useSpecificPage` | boolean | When `true`, tapping a card opens `specificPage` instead of the default edit page. |
| `specificPage` | string | Page schema name to open when `useSpecificPage` is `true`. |
| `ariaLabel` | string | Accessible label for the gallery group element. |
| `itemStyles` | object (NgStyle) | CSS styles applied to each gallery item. |

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `selectionState` | string | Attribute binding for the gallery's selection state (e.g. `'$Gallery_SelectionState'`). |
| `_selectionOptions` | object | Internal selection options wired by the platform. Example: `{ attribute: 'Gallery_SelectionState' }`. Set only when using platform-managed selection. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "ProductGallery",
  "values": {
    "type": "crt.Gallery",
    "items": "$PDS_Products",
    "itemConfig": {}
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **`items` must be a binding expression** (`$PDS_*`). Without a valid binding the gallery shows
  design-time preview thumbnails and never loads real data at runtime.
- **`useSpecificPage: true` requires `specificPage`** to be set. Leaving `specificPage` empty
  causes navigation to fail silently on tap.
- **`itemConfig` shape is registry-defined.** Passing arbitrary keys inside `itemConfig` that
  are not in the registry will be ignored or may break the properties panel.
