# How to Add a Message Editor (`crt.MessageEditor`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MessageEditor` into a Creatio Freedom UI page schema.
>
> A `crt.MessageEditor` is a container component that hosts child editor elements (rich-text
> input, attachments, toolbar buttons) in its `items` slot. It coordinates the editing surface
> for a single message channel inside a `crt.MessageComposerSelector`.

## Metadata

- **Category**: interactive
- **Container**: yes (children go into the `items` slot)
- **Parent types**: `crt.MessageComposerSelector` items slot, `crt.FlexContainer`
- **Typical children**: rich-text editor, attachment picker, send button (via `items` slot)

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.MessageEditor"` and `items: []` for children. |
| 2 | `viewConfigDiff` (children) | Separate `insert` ops for each child with `parentName` pointing here. |

### 1.1 Naming convention

```
MessageEditor_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MessageEditor_abc",
  "parentName": "MessageComposerContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MessageEditor",
    "items": [],
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 Add child elements

```jsonc
// Example: add a text input inside the editor
{
  "operation": "insert",
  "name": "MessageInput_abc",
  "parentName": "MessageEditor_abc",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Input",
    "multiline": true,
    "value": "$MessageText",
    "placeholder": "#ResourceString(MessageInput_placeholder)#"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MessageEditor` are in `ComponentRegistry.json` under `componentType: "crt.MessageEditor"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FeedMessageEditor",
  "parentName": "FeedComposerContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MessageEditor",
    "items": [],
    "visible": true
  }
}
```

---

## 7. Common pitfalls

1. **`items: []` must be present** — without the `items` array the slot is not rendered and
   child inserts fail.
2. **Children insert into `items`, not a named slot** — use `propertyName: "items"` when
   inserting child elements.
3. **No `@CrtInterfaceDesignerItem` toolbar entry** — this component is not available via
   the interface designer drag-and-drop palette; add it programmatically.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.MessageEditor"`, unique `name`,
  valid `parentName`.
- [ ] `items: []` present in `values`.
- [ ] Child components inserted with `parentName` matching this editor's `name`.
- [ ] `visible` set or bound to a page attribute.
