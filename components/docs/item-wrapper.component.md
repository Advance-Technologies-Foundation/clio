# How to Add an Item Wrapper (`crt.ItemWrapper`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ItemWrapper` into a Creatio Freedom UI page schema.
>
> `crt.ItemWrapper` is the leaf wrapper used by `crt.MultiList` to render one list item template and emit item
> focus/click actions. Prefer inserting `crt.MultiList` for page schemas; use this component only when you are
> composing a custom list item host.

## Metadata

- **Category**: containers
- **Container**: yes (renders one projected item template)
- **Parent types**: `crt.MultiList` internal item host, `crt.FlexContainer`
- **Typical children**: one item template supplied through `itemTemplate`

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.ItemWrapper"` and item/template bindings. **Only for custom list composition.** |
| 2 | `handlers` | Request handlers for `itemClicked` and optionally `itemFocused`. |

`crt.ItemWrapper` does **not** load list data. It receives one `ViewModel` and one `TemplateRef`, applies focus /
selection classes, and emits the same `ViewModel` when the user clicks or focuses the item.

### 1.1 Naming convention

```
ItemWrapper_<id>        // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ItemWrapper_row",
  "parentName": "CustomListContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ItemWrapper",
    "viewModel": "$CurrentListItem",
    "itemTemplate": "$CurrentListItemTemplate",
    "isSelected": "$CurrentListItemSelected",
    "isCombinedMode": false,
    "itemClicked": {
      "request": "crt.CustomListItemClickedRequest",
      "params": {
        "item": "@event"
      }
    }
  }
}
```

### 2.2 Add a handler (`handlers` entry)

```jsonc
{
  "request": "crt.CustomListItemClickedRequest",
  "handler": async (request, next) => {
    // request.params.item is the ViewModel emitted by crt.ItemWrapper
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ItemWrapper` are in `ComponentRegistry.json` under `componentType: "crt.ItemWrapper"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// viewModel — see @terrasoft/studio-enterprise/util/model
interface ViewModel {
  attributes: Record<string, unknown>;
  primaryAttributeName: string;
  dataSchemas?: Record<string, unknown>;
}

// itemTemplate is an Angular TemplateRef normally resolved by crt.MultiList from its `items` content slot.
type itemTemplate = TemplateRef<unknown>;
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — use only for custom list composition; crt.MultiList is the standard page API
{
  "operation": "insert",
  "name": "ItemWrapper_row",
  "parentName": "CustomListContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ItemWrapper",
    "viewModel": "$CurrentListItem",
    "itemTemplate": "$CurrentListItemTemplate",
    "isSelected": "$CurrentListItemSelected",
    "isCombinedMode": true,
    "itemClicked": {
      "request": "crt.CustomListItemClickedRequest",
      "params": {
        "item": "@event"
      }
    }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "CurrentListItemSelected": { "value": false }
  }
}

// viewConfigDiff.values
"isSelected": "$CurrentListItemSelected"
```

Selection styling is visible only when `isCombinedMode` is true.

---

## 7. Common pitfalls

1. **Using `crt.ItemWrapper` instead of `crt.MultiList`.** The wrapper handles only one item; it does not provide
   search, paging, item templates, or collection loading.
2. **Missing `itemTemplate`.** Without a template the wrapper has nothing useful to render.
3. **Missing `viewModel`.** The click and focus outputs emit the current `viewModel`; without it handlers receive
   an empty payload.
4. **Expecting selected styling without combined mode.** The selected class is applied only when both
   `isSelected` and `isCombinedMode` are true.
5. **Unwired `itemClicked`.** Clicking still changes local focus states, but page logic needs a request binding to
   react to selection.

---

## 8. Quick checklist

- [ ] Prefer `crt.MultiList` for normal page schemas.
- [ ] `insert` op uses `type: "crt.ItemWrapper"` only for custom list composition.
- [ ] `viewModel` and `itemTemplate` are both populated.
- [ ] `itemClicked.request` is wired when selection should trigger page logic.
- [ ] `isSelected` and `isCombinedMode` are set consistently.
