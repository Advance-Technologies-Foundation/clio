# How to Add an Object Explorer (`crt.ObjectExplorer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ObjectExplorer` into a Creatio Freedom UI page schema.
>
> `crt.ObjectExplorer` is a dialog-style selector for object/channel rows. It is normally opened through the
> dialog service with `MAT_DIALOG_DATA`, not inserted directly into a page schema.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: Angular Material dialog overlay
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | Dialog caller code | Open `CrtObjectExplorerComponent` with `data` containing an item array or `{ items, caption }`. |

`crt.ObjectExplorer` has no page-schema inputs in `ComponentRegistry.json`. The runtime component reads all
content from `MAT_DIALOG_DATA` and closes the dialog with the selected `ObjectExplorerItem[]`.

### 1.1 Naming convention

```
ObjectExplorerDialog_<id>        // local caller variable or request name; not a viewConfigDiff element
```

---

## 2. Step-by-step recipe

### 2.1 Open the dialog from a handler or component

```ts
this._dialogService.open(CrtObjectExplorerComponent, {
	data: {
		items: selectableItems,
		caption: 'Components.MessageComposer.DesignerCustomEntities.SelectChannelsCaption',
	},
});
```

### 2.2 Handle the selected items

```ts
this._dialogService.open(CrtObjectExplorerComponent, { data: selectableItems }).subscribe((selectedItems) => {
	this._addSelectedItems(selectedItems);
});
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ObjectExplorer` are in `ComponentRegistry.json` under `componentType: "crt.ObjectExplorer"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
interface ObjectExplorerConfig {
  items: ObjectExplorerItem[];
  caption: string;
}

interface ObjectExplorerItem {
  uId: string;
  caption: string;
}
```

The dialog also accepts a bare `ObjectExplorerItem[]` as `data`; use `{ items, caption }` when a custom caption is
needed.

---

## 5. Copy-paste minimal example

```ts
// Real runtime pattern used by message-composer and timeline properties panels
this._dialogService.open(CrtObjectExplorerComponent, {
	data: {
		items: notSelectedChannels,
		caption: 'Components.MessageComposer.DesignerCustomEntities.SelectChannelsCaption',
	},
}).subscribe((selectedItems) => {
	this._addSelectedItems(selectedItems);
});
```

---

## 6. Driving from page state

`crt.ObjectExplorer` is not driven from `viewModelConfigDiff`. Prepare the `ObjectExplorerItem[]` in the caller
and pass it through dialog `data`.

---

## 7. Common pitfalls

1. **Inserting it directly into `viewConfigDiff`.** The component expects dialog providers such as
   `MAT_DIALOG_DATA` and `MatDialogRef`; direct page insertion lacks those dependencies.
2. **Passing items without `uId`.** Selection state is keyed by `uId`, so duplicate or missing ids break
   checked-item tracking.
3. **Passing items without `caption`.** Search and visible row text both depend on `caption`.
4. **Forgetting to subscribe to the dialog result.** The component closes with the selected items; caller logic
   must consume that result.
5. **Using raw schema objects as items.** Map them to `{ uId, caption }` before opening the dialog.

---

## 8. Quick checklist

- [ ] Open `CrtObjectExplorerComponent` through the dialog service, not `viewConfigDiff`.
- [ ] Pass either `ObjectExplorerItem[]` or `{ items, caption }` as dialog `data`.
- [ ] Every item has stable `uId` and user-facing `caption`.
- [ ] Subscribe to the dialog result and handle selected items.
- [ ] Keep filtering/search expectations tied to `caption`.
