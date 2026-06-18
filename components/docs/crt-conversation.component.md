# How to Add a Conversation (`crt.Conversation`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Conversation` into a Creatio Freedom UI page schema.
> A `crt.Conversation` is a scrollable chat message list with pluggable `tools`, `actions`, `information`, `typing`, and `placeholder` content slots; it coordinates the message editor, scroll engine, pagination, and read-state tracking for a single omnichannel chat thread.

## Metadata

- **Category**: display
- **Container**: yes (`actions`, `information`, `tools`, `typing`, `placeholder` slots)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: `crt.MessageEditor` (in `tools`), action/info buttons (in `actions`/`information`)

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Conversation"` and starting values including nested slot children. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for message-editor bindings (`isFocused`, `chatInput`, `attributesMapping`). **Always present when create command seeds a `crt.MessageEditor`.** |
| 3 | `handlers` (optional) | Handlers for `loadPreviousMessages`, `messageEvent`, `markAsReadMessages`, `conversationEventChange`. |

The create command (`crt.AddConversationCommand`) auto-generates the nested `crt.MessageEditor` / `crt.MessageEditorBody` / `crt.MessageEditorInput` tree and seeds three `viewModelConfigDiff` attributes per message-editor instance.

### 1.1 Naming convention

```
Conversation_<id>                       // view element name
MessageEditor_<id>                      // nested message editor in `tools` slot
MessageEditorBody_<id>                  // nested inside MessageEditor items
MessageEditorInput_<id>                 // nested inside MessageEditorBody inputs
$MessageEditorBody_<id>_isFocused       // viewModel attribute
$MessageEditorBody_<id>_chatInput       // viewModel attribute
$MessageEditorBody_<id>_attributesMapping // viewModel attribute
```

---

## 2. Step-by-step recipe

### 2.1 Add viewModel attributes for the message editor (`viewModelConfigDiff` entries)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "MessageEditorBody_xyz_isFocused": { "value": false },
      "MessageEditorBody_xyz_chatInput": { "value": null },
      "MessageEditorBody_xyz_attributesMapping": { "value": null }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Conversation_abc123",
  "parentName": "ConversationContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Conversation",
    "actions": [],
    "information": [],
    "messages": "$Chat_abc123_ChatMessages",
    "hasPreviousMessages": "$Chat_abc123_PreviousChatId",
    "tools": [
      {
        "name": "MessageEditor_xyz",
        "type": "crt.MessageEditor",
        "items": [
          {
            "name": "MessageEditorBody_xyz",
            "type": "crt.MessageEditorBody",
            "isFocused": "$MessageEditorBody_xyz_isFocused",
            "attributesMapping": "$MessageEditorBody_xyz_attributesMapping",
            "inputs": [
              {
                "name": "MessageEditorInput_xyz",
                "type": "crt.MessageEditorInput",
                "chatInput": "$MessageEditorBody_xyz_chatInput"
              }
            ]
          }
        ]
      }
    ],
    "placeholder": [],
    "typing": [],
    "disableAutoScroll": true,
    "loadPreviousMessages": { "request": "crt.LoadConversationRequest", "params": { "chatId": "$Id" } },
    "conversationEvent": "$ConversationEvent"
  }
}
```

### 2.3 (Optional) Handler for `loadPreviousMessages`

```jsonc
{
  "request": "crt.LoadConversationRequest",
  "handler": async (request, next) => {
    // load previous page of messages
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Conversation` are in `ComponentRegistry.json` under `componentType: "crt.Conversation"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// ContainerViewElements — inline child config arrays for slots
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;

// loadPreviousMessages / conversationEvent — RequestBindingConfig
interface RequestBindingConfig {
  request: string;
  params?: Record<string, unknown>;
}

// ConversationEvent — internal event payload used for conversation reset
interface ConversationEvent {
  name: string;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff entry — 3 attributes for one MessageEditorBody
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "MessageEditorBody_abc_isFocused": { "value": false },
      "MessageEditorBody_abc_chatInput": { "value": null },
      "MessageEditorBody_abc_attributesMapping": { "value": null }
    }
  }
}
```

```jsonc
// viewConfigDiff entry — minimal conversation with message editor
{
  "operation": "insert",
  "name": "Conversation_main",
  "parentName": "ConversationContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Conversation",
    "actions": [],
    "information": [],
    "messages": "$Chat_main_ChatMessages",
    "hasPreviousMessages": "$Chat_main_PreviousChatId",
    "tools": [
      {
        "name": "MessageEditor_main",
        "type": "crt.MessageEditor",
        "items": [
          {
            "name": "MessageEditorBody_abc",
            "type": "crt.MessageEditorBody",
            "isFocused": "$MessageEditorBody_abc_isFocused",
            "attributesMapping": "$MessageEditorBody_abc_attributesMapping",
            "inputs": [
              {
                "name": "MessageEditorInput_abc",
                "type": "crt.MessageEditorInput",
                "chatInput": "$MessageEditorBody_abc_chatInput"
              }
            ]
          }
        ]
      }
    ],
    "placeholder": [],
    "typing": [],
    "disableAutoScroll": true,
    "conversationEvent": "$ConversationEvent"
  }
}
```

---

## 6. Driving from page state

```jsonc
// Bind messages to a datasource attribute
"messages": "$Chat_abc123_ChatMessages",
"hasPreviousMessages": "$Chat_abc123_PreviousChatId",
"conversationId": "$Id",
"conversationEvent": "$ConversationEvent"
```

`messages` and `hasPreviousMessages` are `propertyBindable` — bind them to `$-prefix` attributes driven
by the page datasource. Use `paginationChange` output to trigger additional message loading.

---

## 7. Common pitfalls

1. **Forgetting the 3 viewModel attributes per `MessageEditorBody`** — each `crt.MessageEditorBody` requires `isFocused`, `chatInput`, and `attributesMapping` attributes in `viewModelConfigDiff`; omitting them breaks the editor.
2. **Placing `tools` content in `items` slot** — `crt.Conversation` uses named content slots (`tools`, `actions`, `information`, `typing`, `placeholder`); do not put the message editor in `items` — use the `tools` slot.
3. **Setting `disableAutoScroll: false` on initial load** — with `disableAutoScroll: false`, the scroll engine tries to jump to bottom on every change; for most chat pages `true` is the correct default and the scroll engine handles it internally.
4. **Omitting `conversationEvent` binding** — `conversationEvent` is consumed and cleared by the component after each event; without it, conversation resets won't propagate.
5. **Not wiring `loadPreviousMessages`** — the scroll engine emits `loadPreviousMessages` when the user reaches the top of history; without a handler, pagination silently does nothing.
6. **Using `messages` as a static array** — `messages` must be a `$-prefix` attribute updated by a handler; a static array won't update when new chat messages arrive.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Conversation"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `messages` bound to a datasource-driven `$-prefix` attribute.
- [ ] `hasPreviousMessages` bound to a boolean attribute indicating whether there are earlier messages.
- [ ] `tools` array contains the nested `crt.MessageEditor → crt.MessageEditorBody → crt.MessageEditorInput` tree.
- [ ] `viewModelConfigDiff` declares `isFocused`, `chatInput`, `attributesMapping` attributes for each `MessageEditorBody`.
- [ ] Slot arrays (`actions`, `information`, `typing`, `placeholder`) provided (empty `[]` is valid).
- [ ] `conversationEvent` bound to a `$-prefix` event attribute.
- [ ] If `loadPreviousMessages` is wired, a matching `handlers` entry exists.
