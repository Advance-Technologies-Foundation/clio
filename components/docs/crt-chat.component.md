# How to Add a Chat (`crt.Chat`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Chat` into a Creatio Freedom UI page schema.
>
> `crt.Chat` is a **container** that renders the chat interface panel. It coordinates the chat
> session display, combined/split mode switching, and child view elements (most importantly a
> `crt.Conversation` child that the create command inserts automatically).

## Metadata

- **Category**: interactive
- **Container**: yes (children go into the `items` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: `crt.Conversation` (auto-added by create command)

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Chat"`, output bindings, and `items: []`. **Always present.** |
| 2 | `viewModelConfigDiff` | A `merge` op that declares the `${name}_ChatMessages` attribute with `value: []` — the messages array passed to the `crt.Conversation` child. |
| 3 | `handlers` | *(optional)* Request handlers for the outputs you wired (e.g. `openChatSession`, `changeCombinedMode`). |

The create command (`crt.CreateChatCommand`) automatically adds the `${name}_ChatMessages` viewModel attribute
**and** inserts a `crt.Conversation` child inside `items` with `messages: "${name}_ChatMessages"`.

### 1.1 Naming convention

```
Chat_<id>                   // view element name
Chat_<id>_ChatMessages      // viewModel attribute for the messages array
$Chat_<id>_ChatMessages     // $-prefix attribute binding in the Conversation child
```

---

## 2. Step-by-step recipe

### 2.1 Declare the messages attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Chat_abc123_ChatMessages": { "value": [] }
    }
  }
}
```

### 2.2 Insert the chat container (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Chat_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Chat",
    "isCombinedMode": "$IsCombinedMode",
    "combinedModeEnabled": "$CombinedModeEnabled",
    "combinedModeButtonVisible": "$CombinedModeButtonVisible",
    "openChatSession": { "request": "crt.ChatSessionOpenRequest", "params": { "session": { "id": "@event" } } },
    "changeCombinedMode": { "request": "crt.ChatModeRequest", "params": { "isCombined": "@event" } },
    "items": []
  }
}
```

### 2.3 Insert the Conversation child (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Conversation_abc123",
  "parentName": "Chat_abc123",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Conversation",
    "messages": "$Chat_abc123_ChatMessages"
  }
}
```

### 2.4 (Optional) Add handlers

```jsonc
{
  "request": "crt.ChatSessionOpenRequest",
  "handler": async (request, next) => {
    // handle opening a chat session by session.id
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Chat` are in `ComponentRegistry.json` under `componentType: "crt.Chat"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// RequestBindingConfig — shape for output bindings
interface RequestBindingConfig {
  request: string;                    // e.g. 'crt.ChatSessionOpenRequest'
  params?: RequestParamsBindingConfig;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}

// ContainerViewElements — the items slot
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;
```

---

## 5. Copy-paste minimal example

Real-world usage from `CopilotPanel.js` in PackageStore:

```jsonc
// viewModelConfigDiff — messages attribute
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Chat_ChatMessages": { "value": [] },
      "IsCombinedMode": { "value": false },
      "CombinedModeEnabled": { "value": true },
      "CombinedModeButtonVisible": { "value": false }
    }
  }
}
```

```jsonc
// viewConfigDiff — Chat container
{
  "operation": "insert",
  "name": "Chat",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Chat",
    "isCombinedMode": "$IsCombinedMode",
    "combinedModeEnabled": "$CombinedModeEnabled",
    "combinedModeButtonVisible": "$CombinedModeButtonVisible",
    "openChatSession": {
      "request": "crt.ChatSessionOpenRequest",
      "params": { "session": { "id": "@event" } }
    },
    "openChatSessionWithMessage": {
      "request": "crt.CopilotNewChatWithMessageRequest",
      "params": { "sessionId": "@event.sessionId", "message": "@event.message" }
    },
    "changeCombinedMode": {
      "request": "crt.CopilotChatModeRequest",
      "params": { "isCombined": "@event" }
    },
    "items": []
  }
}
```

```jsonc
// viewConfigDiff — Conversation child inside Chat
{
  "operation": "insert",
  "name": "Conversation",
  "parentName": "Chat",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Conversation",
    "messages": "$Chat_ChatMessages"
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare attributes
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "IsCombinedMode": { "value": false },
      "CombinedModeEnabled": { "value": true }
    }
  }
}

// viewConfigDiff.values — bind via $-prefix
"isCombinedMode": "$IsCombinedMode",
"combinedModeEnabled": "$CombinedModeEnabled"
```

Both `isCombinedMode` and `combinedModeEnabled` are `propertyBindable` — bind them to viewModel
attributes to drive combined/split layout switching from page logic.

---

## 7. Common pitfalls

1. **Forgetting `items: []`** — the container requires the slot array even when starting empty; child inserts fail without it.
2. **Missing `_ChatMessages` attribute** — the `crt.Conversation` child's `messages` binding resolves to `undefined` if you skip the `viewModelConfigDiff` declaration.
3. **Skipping the Conversation child** — `crt.Chat` is a layout container; without a `crt.Conversation` child the panel renders empty.
4. **`combinedModeEnabled: false` without user affordance** — when disabled, the mode-switch button is hidden permanently. Only set `false` when you intentionally want split-only mode.
5. **`openChatSession` without a handler** — the output fires on every session click but nothing happens; wire it to a platform request or a custom handler.
6. **`openChatSessionWithMessage` payload shape** — the emitted value is `{ sessionId: Guid, message: string }`; use `@event.sessionId` and `@event.message` in `params`, not `@event` directly.
7. **Using `@event` for `changeCombinedMode`** — the payload is a plain boolean (`true` = combined, `false` = split); map to `params: { isCombined: "@event" }`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Chat"`, unique `name`, valid `parentName`, `propertyName: "items"`, `items: []`.
- [ ] `viewModelConfigDiff` has a `${name}_ChatMessages` attribute with `value: []`.
- [ ] A `crt.Conversation` child is inserted with `messages: "$${name}_ChatMessages"`.
- [ ] `isCombinedMode`, `combinedModeEnabled`, `combinedModeButtonVisible` bound to viewModel attributes if you need runtime control.
- [ ] `openChatSession` wired to a request handler (or platform request).
- [ ] `changeCombinedMode` wired so the component can toggle combined/split mode.
- [ ] If `openChatSessionWithMessage` is used, handler reads `request.parameters.sessionId` and `request.parameters.message`.
