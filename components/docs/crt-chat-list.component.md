# How to Add a Chat List (`crt.ChatList`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ChatList` into a Creatio Freedom UI page schema.
>
> `crt.ChatList` is a **scrollable list of chat sessions** with built-in search, keyboard
> navigation, infinite scroll pagination, and a create-chat button. Each row is rendered as a
> `crt.ChatItem` automatically. It owns no datasource — the `chats` array and search/pagination
> state are driven from page attributes.

## Metadata

- **Category**: display
- **Container**: yes (`chats` content slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: none (items are auto-rendered from the `chats` array)

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ChatList"` and output bindings. **Always present.** |
| 2 | `handlers` (optional) | Request handlers for `chatClicked`, `loadData`, `createChatClicked`, etc. |

`crt.ChatList` requires `viewModelConfigDiff` attributes for `chats`, `selectedChatId`,
`searchFilter`, and `currentPage` when you need runtime state control.

### 1.1 Naming convention

```
ChatList_<id>                        // view element name
$ChatList_<id>_Chats                 // attribute binding for the chats array
$ChatList_<id>_SelectedChatId        // attribute binding for the selected chat id
```

---

## 2. Step-by-step recipe

### 2.1 Insert the chat list (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChatList_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatList",
    "chats": "$ChatList_abc123_Chats",
    "selectedChatId": "$ChatList_abc123_SelectedChatId",
    "isCombinedMode": "$IsCombinedMode",
    "currentPage": "$ChatList_abc123_CurrentPage",
    "searchFilter": "$ChatList_abc123_SearchFilter",
    "chatsListChange": {
      "request": "crt.ChatListChangeRequest",
      "params": { "chats": "@event" }
    },
    "loadData": {
      "request": "crt.ChatListLoadRequest",
      "params": { "searchFilter": "@event.search", "rowsToLoadCount": "@event.count" }
    },
    "chatClicked": {
      "request": "crt.OpenChatRequest",
      "params": { "session": "@event" }
    },
    "createChatClicked": {
      "request": "crt.CreateChatRequest"
    }
  }
}
```

### 2.2 (Optional) Add handlers

```jsonc
{
  "request": "crt.ChatListLoadRequest",
  "handler": async (request, next) => {
    // load next page of chats using request.parameters.searchFilter and rowsToLoadCount
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChatList` are in `ComponentRegistry.json` under `componentType: "crt.ChatList"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// loadData payload
interface ChatListLoadRequest {
  count: number;       // rows to load (controlled by internal rowsToLoadCount)
  search: string;      // current search filter text
}

// ContainerViewElements — shape for the menuItems slot
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;
```

---

## 5. Copy-paste minimal example

Real-world usage from `CopilotPanel.js` in PackageStore:

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ChatList",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatList",
    "visible": "$IsChatListVisible",
    "chats": "$ActiveChatsList",
    "isCombinedMode": "$IsCombinedMode",
    "selectedChatId": "$CurrentCopilotSessionId",
    "currentPage": "$ChatsListPage",
    "searchFilter": "$ChatsSearchFilter",
    "searchDisableFeatureName": "DisableSearchForCreatioAI",
    "chatsListChange": {
      "request": "crt.ChatListChangeRequest",
      "params": { "chats": "@event" }
    },
    "loadData": {
      "request": "crt.CopilotChatListLoadRequest",
      "params": {
        "searchFilter": "@event.search",
        "rowsToLoadCount": "@event.count"
      }
    },
    "focusedChatId": "$FocusedChatId",
    "chatClicked": {
      "request": "crt.ChatSessionOpenRequest",
      "params": { "session": "@event" }
    },
    "createChatClicked": {
      "request": "crt.CopilotNewChatRequest",
      "params": { "chatMessagesAttributeName": "ChatMessages" }
    },
    "menuItems": [
      {
        "type": "crt.MenuItem",
        "caption": "#ResourceString(RenameButton_caption)#",
        "color": "default",
        "icon": "pencil-button-icon",
        "clicked": { "request": "crt.CopilotRenameSessionRequest" }
      }
    ]
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
      "ActiveChatsList": { "value": [] },
      "CurrentChatId": { "value": "" },
      "ChatsListPage": { "value": 0 },
      "ChatsSearchFilter": { "value": "" }
    }
  }
}
```

Both `searchFilter` and `currentPage` are `propertyBindable`. After a search or page change, the
component fires `searchFilterChange`/`currentPageChange` — sync them back to page attributes to
reset pagination correctly.

---

## 7. Common pitfalls

1. **`chats` not cleared on search change** — when `searchFilter` changes, clear the `chats`
   array and reset `currentPage` to `0` before loading new results; the component does not reset
   the list automatically.
2. **`loadData` payload shape** — the event is `{ count: number, search: string }`, not a bare
   number; use `@event.count` and `@event.search` in params.
3. **`chatClicked` payload is the full `ChatPreview` object** — use `@event.id` or `@event`
   depending on what the handler expects.
4. **`selectedChatId` vs `focusedChatId`** — `selectedChatId` drives the highlighted selection
   style; `focusedChatId` tracks keyboard focus for accessibility. Bind both when you need full
   focus control.
5. **`menuItems` requires `type: "crt.MenuItem"`** — pass the full config object including `type`;
   omitting it causes silent rendering failures.
6. **`searchDisableFeatureName` is a feature flag name string, not a boolean** — the component
   resolves the flag value internally; pass the flag name, not `true`/`false`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ChatList"`, unique `name`, valid
  `parentName`, `propertyName: "items"`.
- [ ] `chats` bound to a page attribute (initially `[]`).
- [ ] `loadData` wired to a handler that fetches and appends items to the `chats` attribute.
- [ ] `chatClicked` wired to a handler that opens the selected session.
- [ ] `chatsListChange` wired to keep the `chats` attribute in sync.
- [ ] `currentPage` and `searchFilter` bound and synced via their `Change` outputs.
- [ ] `menuItems` each has `type: "crt.MenuItem"` and a `clicked` binding.
