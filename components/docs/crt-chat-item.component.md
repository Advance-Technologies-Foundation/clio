# How to Use a Chat Item (`crt.ChatItem`) in a Freedom UI Page

> Audience: code agent working with Creatio Freedom UI page schemas.
>
> `crt.ChatItem` is a **sub-component** rendered automatically by `crt.ChatList` for each row in
> the chat list. It displays the contact avatar, chat title, last message preview, date, and a
> context menu. It is not inserted directly into `viewConfigDiff` — the parent `crt.ChatList`
> manages its lifecycle by projecting each `ChatPreview` record into a tile.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: managed by `crt.ChatList` internally — not placed directly in `viewConfigDiff`
- **Typical children**: none

---

## 1. Mental model — 0 places you edit directly

`crt.ChatItem` has no standalone `viewConfigDiff` insert op. To control what items appear and how
they behave, configure the parent `crt.ChatList`:

```jsonc
// viewConfigDiff — ChatList (the entry point for chat item tiles)
{
  "operation": "insert",
  "name": "ChatList_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatList",
    "chats": "$ChatList_abc123_Chats",
    "menuItems": [
      { "type": "crt.MenuItem", "caption": "...", "clicked": { "request": "..." } }
    ],
    "chatClicked": {
      "request": "crt.OpenChatRequest",
      "params": { "chat": "@event" }
    }
  }
}
```

---

## 2. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.ChatItem` are in
`ComponentRegistry.json` under `componentType: "crt.ChatItem"`. Key inputs set by the parent:

| Input | Type | Notes |
|---|---|---|
| `chat` | `ChatPreview` | Full chat data object: `{ id, caption, author, date, messages, ... }`. |
| `searchFilter` | `string` | Highlighted search query string. Controlled by parent `crt.ChatList`. |
| `chatMenuItems` | `CrtMenuItemViewElementConfig[]` | Context menu items passed from parent `menuItems`. |
| `isCombinedMode` | `boolean` | Layout toggle: combined (side-by-side) vs split. Set by parent. |
| `isSelected` | `boolean` | Highlight state: `true` when this chat is the active selection. |

Note: `chatClicked`, `chatFocused`, and `menuItemsFocused` are internal `@Output()` events, not
part of the MMP contract (`@CrtOutput`) — they are handled entirely by the parent `crt.ChatList`.

---

## 3. Common pitfalls

1. **Do not insert `crt.ChatItem` in `viewConfigDiff` directly** — it is not a standalone
   insertable element; the parent `crt.ChatList` renders it per chat record.
2. **Driving selection** — set `selectedChatId` on `crt.ChatList`, not `isSelected` on individual
   items; the list propagates the selection down automatically.
3. **Menu items shape** — `chatMenuItems` accepts `CrtMenuItemViewElementConfig[]`; each item needs
   `type: "crt.MenuItem"` and a `clicked` binding.

---

## 4. Quick checklist

- [ ] To render chat items, add a `crt.ChatList` with `chats` and `chatClicked` wired.
- [ ] Do **not** add a `viewConfigDiff` insert op for `crt.ChatItem`.
