# How to Add a Folder Tree Actions (`crt.FolderTreeActions`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FolderTreeActions` into a Creatio Freedom UI page schema.
>
> `crt.FolderTreeActions` is a compact action bar that sits next to a `crt.FolderTree`: it shows the currently
> active folder label, lets the user clear the selection, and opens a dropdown with favorite-folder shortcuts.
> Adding it only requires a single `viewConfigDiff` insert op pointing at an existing `crt.FolderTree` element.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer` (placed in the toolbar row of a list page, typically alongside a search filter)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FolderTreeActions"` and `folderTree` pointing to the sibling `crt.FolderTree` element. **Always present.** |

`crt.FolderTreeActions` is **view-only** — no datasource, no viewModel attribute. All active-folder state flows
through the paired `crt.FolderTree` element referenced by name in the `folderTree` input.

### 1.1 Naming convention

```
FolderTreeActions_<id>        // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FolderTreeActions_p4bcvr1",
  "parentName": "FlexContainer_toolbar",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FolderTreeActions",
    "caption": "#ResourceString(FolderTreeActions_p4bcvr1_caption)#",
    "folderTree": "FolderTree_gm5sany"
  }
}
```

The `folderTree` value must match the `name` of the `crt.FolderTree` element already present in the same page
schema. The component uses that name to subscribe to folder-selection changes and emit
`activeFolderChanged`/`folderTreeVisibleChanged` events back to the page.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FolderTreeActions` are in `ComponentRegistry.json` under `componentType: "crt.FolderTreeActions"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// activeFolderChanged payload
interface ActiveFolderChangedEvent {
  id: string;           // Guid string, empty string to clear selection
  name: string;
  folderTreeId: string;
  filterData: string;   // JSON-stringified filter; '{}' when clearing
  folderTypeId?: string;
  isFavorite?: boolean;
}

// folderTreeVisibleChanged payload
interface FolderTreeVisibleChangedEvent {
  togglePanel: boolean;
}

// favoriteItems element shape (DataItem)
interface DataItem {
  Id: Promise<string>;
  Name: Promise<string>;
  FolderTreeId: Promise<string>;
  FilterData: Promise<string>;
  FolderTypeId: Promise<string>;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — FolderTreeActions in a list-page toolbar
{
  "operation": "insert",
  "name": "FolderTreeActions_p4bcvr1",
  "parentName": "FlexContainer_f65ub2d",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FolderTreeActions",
    "caption": "#ResourceString(FolderTreeActions_p4bcvr1_caption)#",
    "folderTree": "FolderTree_gm5sany"
  }
}
```

```jsonc
// handlers entry — react to folder selection change
{
  "request": "crt.HandlerType",
  "handler": async (request, next) => {
    // request.folderId / request.folderName available depending on binding
    return next?.handle(request);
  }
}
```

---

## 6. Driving from page state (OPTIONAL)

`activeFolderChanged` fires whenever the user selects or clears a folder. Bind its `request` to a handler
that updates an attribute used to filter a data grid:

```jsonc
// viewConfigDiff.values
"activeFolderChanged": { "request": "crt.HandleActiveFilterChanged" }
```

`favoriteItems` accepts a `$`-prefixed attribute bound to a datasource collection, giving the dropdown
its list of starred folders. The component also accepts a live `BaseViewModelCollection` at runtime.

---

## 7. Common pitfalls

1. **`folderTree` points to a non-existent element name.** The component silently does nothing if the referenced `crt.FolderTree` element is absent or mistyped; always double-check the `name` of the paired tree element.
2. **Placing it outside a flex row.** `crt.FolderTreeActions` renders as an inline-flex bar; put it inside a `crt.FlexContainer` with `direction: "row"` — not directly in a grid cell.
3. **Forgetting `caption`.** The resource string is shown as the placeholder text when no folder is active; omitting it leaves an empty button.
4. **Not wiring `activeFolderChanged`.** The component emits this output to signal folder changes; without a handler or binding, selecting a folder has no effect on the page data.
5. **Not wiring `folderTreeVisibleChanged`.** The toggle-panel button does nothing unless this output fires a request that shows/hides the `crt.FolderTree` element.
6. **Setting `favoriteItems` to a plain array from a non-reactive source.** The component only reacts to live changes when passed a `BaseViewModelCollection`; a static array is read once at bind time and never updated.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FolderTreeActions"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `folderTree` is set to the exact `name` of the paired `crt.FolderTree` element in the same page.
- [ ] `caption` resource string created (used as placeholder when no folder is active).
- [ ] `activeFolderChanged` wired to a request handler that applies the folder filter.
- [ ] `folderTreeVisibleChanged` wired to a request handler that toggles the folder-tree panel.
- [ ] Element is placed inside a `crt.FlexContainer` (not directly in a grid cell).
