# How to Add a Message Editor Input (`crt.MessageEditorInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MessageEditorInput` into a Creatio Freedom UI page schema.
>
> `crt.MessageEditorInput` is the text-entry field inside a `crt.MessageEditorBody`. It handles keyboard
> input, mention autocomplete, file attachment, and the Enter-key send hotkey. It always lives in the
> `inputs` slot of a `crt.MessageEditorBody`.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.MessageEditorBody` (slot `inputs`)
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.MessageEditorInput"`, `chatInput`, `chatInputChange`, and `sendMessage`. **Always present.** |
| 2 | `handlers` (optional) | Request handlers for `chatInputChange`, `sendMessage`, or `getMentionService` when custom logic is needed. |

`crt.MessageEditorInput` is **view-only** — it does not own a datasource. The message text lives in a
viewModel attribute that is two-way-bound via `chatInput` / `chatInputChange`.

### 1.1 Naming convention

```
MessageEditorInput_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MessageEditorCrtInput",
  "values": {
    "type": "crt.MessageEditorInput",
    "chatInput": "$ChatInput",
    "action": "$MessageEditorInputAction",
    "inputMode": "html",
    "getMentionService": {
      "request": "crt.MentionInitRequest",
      "params": {
        "initService": "@event"
      }
    },
    "sendMessage": {
      "request": "crt.MessageEditorSendRequest"
    },
    "chatInputChange": {
      "request": "crt.MessageEditorInputChangeRequest",
      "params": {
        "newValue": "@event"
      }
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
`crt.MessageEditorInput` are in `ComponentRegistry.json` under `componentType: "crt.MessageEditorInput"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// RequestBindingConfig — used by sendMessage, chatInputChange, getMentionService, etc.
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
// viewConfigDiff entry — plain text mode, no mentions
{
  "operation": "insert",
  "name": "MessageEditorCrtInput",
  "values": {
    "type": "crt.MessageEditorInput",
    "chatInput": "$ChatInput",
    "sendMessage": {
      "request": "crt.MessageEditorSendRequest"
    },
    "chatInputChange": {
      "request": "crt.MessageEditorInputChangeRequest",
      "params": {
        "newValue": "@event"
      }
    }
  },
  "parentName": "MessageEditorBody",
  "propertyName": "inputs",
  "index": 0
}
```

```jsonc
// viewConfigDiff entry — HTML mode with mention support
{
  "operation": "insert",
  "name": "MessageEditorCrtInput",
  "values": {
    "type": "crt.MessageEditorInput",
    "chatInput": "$ChatInput",
    "action": "$MessageEditorInputAction",
    "inputMode": "html",
    "getMentionService": {
      "request": "crt.MentionInitRequest",
      "params": { "initService": "@event" }
    },
    "sendMessage": { "request": "crt.MessageEditorSendRequest" },
    "chatInputChange": {
      "request": "crt.MessageEditorInputChangeRequest",
      "params": { "newValue": "@event" }
    }
  },
  "parentName": "MessageEditorBody",
  "propertyName": "inputs",
  "index": 0
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare the ChatInput attribute
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "ChatInput": { "value": "" },
    "MessageEditorInputAction": { "value": null }
  }
}

// To trigger a mention programmatically, set the attribute to "triggerMention":
// "action": "$MessageEditorInputAction"
```

---

## 7. Common pitfalls

1. **Placing outside `inputs` slot.** This component must be inserted into `propertyName: "inputs"` of a `crt.MessageEditorBody`; other parent types are unsupported.
2. **`inputMode: "html"` without `getMentionService`.** HTML mode expects a mention service provider; omitting it disables the `@mention` feature silently.
3. **`action` bound to an attribute that is never reset.** After the action fires (e.g. `"triggerMention"`), the attribute must be reset to `null`/empty so a second trigger works.
4. **`readonly: true` without `readonlyPlaceholderKey`.** The input shows the default placeholder `MessageEditor.InputPlaceholder`; set `readonlyPlaceholderKey` to a localization key for a context-specific message.
5. **Omitting `chatInputChange`.** Without it the parent body's `chatInput` attribute is never updated and the send button stays disabled.
6. **Both `sendMessage` on `crt.MessageEditorInput` and on `crt.MessageEditorBody`.** Both fire on Enter; wire only one to avoid duplicate sends.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.MessageEditorInput"`, `parentName` pointing to a `crt.MessageEditorBody`, `propertyName: "inputs"`.
- [ ] `chatInput` bound to the same attribute that `crt.MessageEditorBody.chatInput` reads.
- [ ] `chatInputChange` wired so the attribute is updated on every keystroke.
- [ ] `sendMessage.request` set (or `sendMessage` wired on the parent body, not both).
- [ ] If `inputMode: "html"`, `getMentionService` is wired to inject the mention provider.
- [ ] If `action` is used, the bound attribute is reset after each use.
