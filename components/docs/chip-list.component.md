# How to Add a Chip List (`crt.ChipList`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ChipList` into a Creatio Freedom UI page schema.
>
> `crt.ChipList` is a **horizontal collection of color-coded chip badges** with optional remove
> buttons. It is marked `@experimental`. Items without a `color` fall back to `#8B9FDA`. When
> `wrap: false`, overflow items are hidden and a `+N` counter appears; clicking it opens an overlay.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, inline in slots
- **Typical children**: none (items are rendered from the `items` array)

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.ChipList"` and an `items` binding. **Always present.** |
| 2 | `handlers` (optional) | Handlers for `itemClick` or `itemRemove` outputs. |

`crt.ChipList` has no datasource and no create command. Bind `items` to a page attribute to
populate the chips at runtime.

### 1.1 Naming convention

```
ChipList_<id>             // view element name
$ChipList_<id>_Items      // $-prefix attribute binding for the items array
```

---

## 2. Step-by-step recipe

### 2.1 Insert the chip list (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChipList_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChipList",
    "items": "$ChipList_abc123_Items",
    "readonly": false,
    "wrap": true,
    "itemRemove": {
      "request": "crt.RemoveChipRequest",
      "params": { "item": "@event" }
    }
  }
}
```

### 2.2 (Optional) Add a handler

```jsonc
{
  "request": "crt.RemoveChipRequest",
  "handler": async (request, next) => {
    // request.parameters.item = { caption, color?, value? }
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChipList` are in `ComponentRegistry.json` under `componentType: "crt.ChipList"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of `CrtChipListItemConfig`

```ts
interface CrtChipListItemConfig {
  caption: string;      // required — chip label text
  color?: string;       // optional — hex or keyword; defaults to '#8B9FDA'
  value?: string;       // optional — arbitrary identifier for the item
}
```

Items without `color` are coerced to `#8B9FDA` by the `_adjustItems` setter. Null/undefined
items in the array are filtered out.

---

## 5. Copy-paste minimal example

Real-world usage from `CopilotPanel.js` in PackageStore (inline in a `crt.MessageEditor` items
slot):

```jsonc
// Inline element inside MessageEditor items slot
{
  "type": "crt.ChipList",
  "name": "SelectedMentionsChipList",
  "visible": "$SelectedMentionsChipListVisible",
  "items": "$SelectedMentionsChipList",
  "itemRemove": {
    "request": "crt.CopilotChatRemoveBoundedSessionRequest",
    "params": {
      "attributesMapping": "$MessageEditorAttributesMapping"
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
  "path": [],
  "values": {
    "attributes": {
      "ChipList_Items": { "value": [] }
    }
  }
}

// viewConfigDiff.values
"items": "$ChipList_Items"
```

---

## 7. Common pitfalls

1. **`@experimental` status** — `crt.ChipList` is experimental; its API may change between
   releases. Avoid coupling critical flows to its specific behavior.
2. **`readonly: true` blocks `itemClick` and `itemRemove`** — both events are silently swallowed
   in readonly mode; do not bind them when you also set `readonly: true`.
3. **`wrap: false` hides overflow items** — items that don't fit are hidden and counted; the user
   can open the overflow via the `+N` counter. If all items must be visible, use `wrap: true`.
4. **`items` setter deduplicates based on equality** — if you pass the same array reference,
   the setter skips the update; always create a new array when mutating items.
5. **`itemClick` / `itemRemove` payloads are the full `CrtChipListItemConfig` object** — use
   `@event.value` or `@event.caption` in params to extract the identifier.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ChipList"`, unique `name`, valid
  `parentName`, `propertyName: "items"`.
- [ ] `items` bound to a page attribute (array of `{ caption, color?, value? }`).
- [ ] If `readonly: false`, `itemRemove` wired to a handler that removes the item from the
  page attribute.
- [ ] `wrap` chosen: `true` for multi-line layout, `false` for single-line with overflow counter.
