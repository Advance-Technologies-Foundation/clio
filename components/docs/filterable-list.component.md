# How to Add a Filterable List (`crt.FilterableList`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FilterableList` into a Creatio Freedom UI page schema.
>
> A `crt.FilterableList` is a display-only view element that renders a searchable, favoritable
> list of items. It emits events when the user selects an item or toggles a favorite. It owns
> no datasource — items are supplied entirely through the `items` input.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FilterableList"` and an `items` binding. **Always present.** |
| 2 | `handlers` (optional) | Request handlers for `itemSelected` and/or `favoriteStateChanged` outputs. |

`crt.FilterableList` is **view-only** — no model, no viewModel attribute required.

### 1.1 Naming convention

```
FilterableList_<id>          // view element name; <id> is any short unique slug
$FilterableList_<id>_items   // $-prefix attribute when items are driven from page state
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FilterableList_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FilterableList",
    "items": "$FilterableList_abc123_items",
    "itemSelected": { "request": "crt.FilterableListItemSelectedRequest" },
    "favoriteStateChanged": { "request": "crt.FilterableListFavoriteChangedRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Add handlers in `handlers`

```jsonc
{
  "request": "crt.FilterableListItemSelectedRequest",
  "handler": async (request, next) => {
    // request carries the selected IdentifiableItem
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FilterableList` are in `ComponentRegistry.json` under `componentType: "crt.FilterableList"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

The registry exposes `FilterableItem` as a typeDefinition:

```ts
interface FilterableItem {
  caption: string;
  id: string;
  isActive: boolean;
  isFavorite: boolean;
}
```

`items` accepts an array of `FilterableItem`. The list renders `caption` and uses `isActive` /
`isFavorite` for visual state. `itemSelected` emits `IdentifiableItem` (`{ id: string }`).
`favoriteStateChanged` emits `FavoritableItem` (`{ id: string; isFavorite: boolean }`).

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — declare the items attribute
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FilterableList_items": { "value": [] }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FilterableList_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FilterableList",
    "items": "$FilterableList_items",
    "itemSelected": { "request": "crt.FilterableListItemSelectedRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 6. Driving from page state

`items` is `propertyBindable` — bind to a `$AttributeName` to populate the list from page state.
Set the attribute value in `viewModelConfigDiff` and update it in a handler.

```jsonc
// In a handler, update the list:
this.$set("FilterableList_items", updatedItems);
```

---

## 7. Common pitfalls

1. **Supplying a static `items` array** — items must satisfy `FilterableItem` shape; missing `id` causes items to be unidentifiable when `itemSelected` fires.
2. **Not wiring `itemSelected`** — selection is silently ignored if no request is bound.
3. **Mutating the `items` array in place** — use `$set` with a new array reference; in-place mutations are not detected by the change detection.
4. **`isFavorite` not reflected back** — `favoriteStateChanged` only emits the change intent; the handler must update the item in the `items` attribute to persist the toggle visually.
5. **`isActive` not updated after selection** — set the selected item's `isActive: true` and others to `false` in the handler to keep visual selection in sync.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FilterableList"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `items` bound to a `$Attribute` (declared in `viewModelConfigDiff`) or a static array satisfying `FilterableItem[]`.
- [ ] Each item has `id`, `caption`, `isActive`, `isFavorite`.
- [ ] `itemSelected` wired to a request handler if selection needs to trigger logic.
- [ ] `favoriteStateChanged` handler updates the attribute to reflect new `isFavorite` state.
