# How to Add an Emoji Select (`crt.EmojiSelect`) to a Freedom UI Page

> Audience: code agent inserting a `crt.EmojiSelect` into a Creatio Freedom UI page schema.
>
> `crt.EmojiSelect` is a button that opens a floating emoji picker overlay. When an emoji is selected it is
> inserted at the current cursor position in a target input element (identified by `attachToSelector` or
> the chat input bound via `chatInput`). Commonly used inside a chat toolbar.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.EmojiSelect"` and the chat input binding. **Always present.** |
| 2 | `handlers` (optional) | A handler for `emojiSelected` if custom emoji insertion logic is needed beyond the built-in cursor-position insertion. |

`crt.EmojiSelect` manages the picker overlay internally. The page only needs to bind `chatId`, `chatInput`,
and `chatInputChange` to sync text back to the page attribute.

### 1.1 Naming convention

```
EmojiSelect_<id>         // view element name; <id> is any short unique slug
$EmojiSelect_<id>_input  // attribute holding the current chat input text (two-way)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EmojiSelect_abc123",
  "parentName": "ChatToolbarContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EmojiSelect",
    "chatId": "$ActiveChatId",
    "chatInput": "$ChatInput_text",
    "chatInputChange": { "request": "crt.ChatInputChangeRequest", "params": { "value": "@event" } },
    "attachToSelector": ".chat-input-textarea"
  }
}
```

### 2.2 (Optional) Handle `emojiSelected`

```jsonc
{
  "request": "crt.EmojiSelectedRequest",
  "handler": async (request, next) => {
    // request.params.emoji contains the selected emoji character
    return next?.handle(request);
  }
}
```

The built-in behavior already inserts the emoji at the cursor position in the `attachToSelector` element;
add a handler only if you need side-effects (e.g. analytics).

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EmojiSelect` are in `ComponentRegistry.json` under `componentType: "crt.EmojiSelect"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

No custom types. All inputs are primitives (`string`, `boolean`, `Guid`). Outputs emit primitive values
(`string` for `chatInputChange`, `{ emoji: string }` for `emojiSelected`).

---

## 5. Copy-paste minimal example

No direct PackageStore schema match found. Based on the component implementation and chat panel context:

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "EmojiSelect_xkp4r",
  "parentName": "ChatToolbarFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EmojiSelect",
    "chatId": "$ActiveChatId",
    "chatInput": "$ChatMessageText",
    "chatInputChange": {
      "request": "crt.ChatMessageTextChangeRequest",
      "params": { "value": "@event" }
    },
    "attachToSelector": ".chat-message-input",
    "disabled": false
  }
}
```

---

## 6. Driving from page state

`disabled` can be bound to a page attribute:

```jsonc
"disabled": "$ChatInput_disabled"
```

`chatInput` is read by the component to know the current cursor position. When an emoji is inserted the
component emits `chatInputChange` with the updated string — the page handler must write this back to the
bound attribute to keep state in sync.

---

## 7. Common pitfalls

1. **Omitting `chatInputChange` handler** — the component inserts the emoji into the DOM element directly,
   but the page attribute drifts unless `chatInputChange` is handled and synced back.
2. **`attachToSelector` matching multiple elements** — the selector is passed to `document.querySelector`;
   if it matches more than one element, only the first is used; make the selector specific enough.
3. **Using without `chatId`** — `chatId` is optional but required if the picker needs to save emoji usage
   history per chat; omit it for generic (non-chat) text inputs.
4. **Setting `disabled: true` globally** — the emoji button becomes unclickable; bind to a page attribute
   so it can be enabled when the chat is active.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.EmojiSelect"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `chatInput` is bound to the page attribute holding the current text value.
- [ ] `chatInputChange` is wired to a handler that writes the updated text back to the page attribute.
- [ ] `attachToSelector` points to the input/textarea DOM element where emoji should be inserted.
- [ ] If `disabled` binding is needed, a corresponding page attribute is declared in `viewModelConfigDiff`.
