# How to Add a Message Editor Body (`crt.MessageEditorBody`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MessageEditorBody` into a Creatio Freedom UI page schema.
>
> `crt.MessageEditorBody` is the layout shell for a chat message-input area: it owns a send-button and
> coordinates two content slots — `inputs` (the text-input area) and `toolbarItems` (action buttons/chips
> above the text field). It is always a child of a `crt.MessageEditor`/`crt.Conversation` container.

## Metadata

- **Category**: interactive
- **Container**: yes — injects child view elements into `inputs` and `toolbarItems` content slots
- **Parent types**: `crt.MessageEditor` (slot `items`), `crt.Conversation` (slot `items`)
- **Typical children**: `crt.MessageEditorInput` (into `inputs`); `crt.Button`, `crt.ChipList` (into `toolbarItems`)

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.MessageEditorBody"`, `toolbarItems`, `inputs`, `chatInput`, and `sendMessage`. **Always present.** |
| 2 | `handlers` (optional) | A request handler for the `sendMessage` request, if custom logic is needed. |

`crt.MessageEditorBody` uses **content slots** (`inputs` and `toolbarItems`) — children go into these slots as
inline arrays in the `values` object, **not** as separate `insert` ops with their own `parentName`. However,
children that are themselves view elements (e.g. `crt.MessageEditorInput`) can also be inserted via `insert`
ops with `parentName: "<MessageEditorBodyName>"` and `propertyName: "inputs"` or `propertyName: "toolbarItems"`.

### 1.1 Naming convention

```
MessageEditorBody_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MessageEditorBody",
  "values": {
    "type": "crt.MessageEditorBody",
    "toolbarItems": [
      {
        "type": "crt.Button",
        "displayType": "text",
        "name": "AttachFileButton",
        "icon": "clip-button-icon",
        "size": "small",
        "iconPosition": "only-icon",
        "title": "#ResourceString(AttachButton_caption)#",
        "color": "default",
        "clicked": {
          "request": "crt.UploadFileRequest",
          "params": {
            "fileEntitySchemaName": "SomeSessionFile",
            "recordColumnName": "SessionId",
            "recordId": "$CurrentSessionId"
          }
        }
      }
    ],
    "inputs": [],
    "chatInput": "$ChatInput",
    "sendMessage": {
      "request": "crt.MessageEditorSendRequest",
      "params": {
        "attributesMapping": "$MessageEditorAttributesMapping"
      }
    }
  },
  "parentName": "Conversation_MessageEditor",
  "propertyName": "items",
  "index": 0
}
```

### 2.2 Insert the input component into the `inputs` slot

```jsonc
{
  "operation": "insert",
  "name": "MessageEditorCrtInput",
  "values": {
    "type": "crt.MessageEditorInput",
    "chatInput": "$ChatInput",
    "chatInputChange": {
      "request": "crt.MessageEditorInputChangeRequest",
      "params": {
        "newValue": "@event"
      }
    },
    "sendMessage": {
      "request": "crt.MessageEditorSendRequest"
    }
  },
  "parentName": "MessageEditorBody",
  "propertyName": "inputs",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MessageEditorBody` are in `ComponentRegistry.json` under `componentType: "crt.MessageEditorBody"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// sendMessage / RequestBindingConfig
interface RequestBindingConfig {
  request: string;          // e.g. 'crt.MessageEditorSendRequest'
  params?: Record<string, unknown>;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — MessageEditorBody inside a Conversation MessageEditor slot
{
  "operation": "insert",
  "name": "MessageEditorBody",
  "values": {
    "type": "crt.MessageEditorBody",
    "toolbarItems": [],
    "inputs": [],
    "chatInput": "$ChatInput",
    "sendMessage": {
      "request": "crt.MessageEditorSendRequest"
    }
  },
  "parentName": "Conversation_MessageEditor",
  "propertyName": "items",
  "index": 0
}
```

```jsonc
// viewConfigDiff entry — MessageEditorInput inserted into the inputs slot
{
  "operation": "insert",
  "name": "MyMessageEditorInput",
  "values": {
    "type": "crt.MessageEditorInput",
    "chatInput": "$ChatInput",
    "sendMessage": { "request": "crt.MessageEditorSendRequest" }
  },
  "parentName": "MessageEditorBody",
  "propertyName": "inputs",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Using this as a direct page-level container.** `crt.MessageEditorBody` must be a child of `crt.MessageEditor` / `crt.Conversation`; dropping it into a bare `crt.FlexContainer` will break the chat context.
2. **Omitting `inputs: []`.** The runtime expects the `inputs` slot to be present even when empty; missing it prevents the slot projection from initializing.
3. **Binding `chatInput` without a matching `chatInputChange`.** If `chatInput` is bound to an attribute, the attribute must also be updated via `chatInputChange` (or via `crt.MessageEditorInput.chatInputChange`) or the send state will be stale.
4. **Not wiring `sendMessage`.** Without a `request`, clicking Send fires silently and the message is lost.
5. **Putting `toolbarItems` children as top-level `insert` ops.** They go inside the `toolbarItems` array in `values`, not as sibling ops (or as `propertyName: "toolbarItems"` child ops targeting the body by name — both patterns are valid; inline array is simpler when items are static).
6. **Setting `isFileUploadEnabled: true` without configuring `attachments`.** Attachment upload requires a bound `attachments` attribute and a handler for `crt.UploadFileRequest`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.MessageEditorBody"`, unique `name`, `parentName` pointing to a MessageEditor/Conversation `items` slot.
- [ ] `chatInput` bound to a `$attribute` that the `crt.MessageEditorInput` also reads.
- [ ] `sendMessage.request` wired to a request type (platform or custom).
- [ ] `inputs` array or separate `insert` ops targeting the `inputs` slot with at least one `crt.MessageEditorInput`.
- [ ] If file upload is needed, `isFileUploadEnabled: true` and `attachments` bound to an attribute.
- [ ] If `editorTooltip` is used, the attribute holds a `MessageEditorTooltipState` object.
