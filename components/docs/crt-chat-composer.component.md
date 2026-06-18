# How to Add a Chat Composer (`crt.ChatComposer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ChatComposer` into a Creatio Freedom UI page schema.
>
> `crt.ChatComposer` is the **message-input panel** for creating new chats and sending messages
> via omnichannel providers (Telegram, SMS, WhatsApp, etc.). It manages channel selection,
> recipient selection, message text, file attachments, and send action. It renders attachment
> tiles for queued files automatically.

## Metadata

- **Category**: interactive
- **Container**: yes (`items` slot accepts projected templates)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: projected template items (via `items` slot)

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ChatComposer"` and output bindings. **Always present.** |
| 2 | `handlers` (optional) | Request handlers for `sendMessage`, `chatCreated`, or other outputs. |

`crt.ChatComposer` has no datasource and no create command. Inputs like `channels`, `providerId`,
`sendersChannels`, and `recipientContacts` are typically wired to viewModel attributes populated by
page logic.

### 1.1 Naming convention

```
ChatComposer_<id>                          // view element name
$ChatComposer_<id>_SelectedChannelId       // $-prefix attribute binding example
```

---

## 2. Step-by-step recipe

### 2.1 Insert the chat composer (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChatComposer_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatComposer",
    "channels": "$ChatComposer_abc123_Channels",
    "providerId": "$ChatComposer_abc123_ProviderId",
    "selectedChannelSchemaType": "$ChatComposer_abc123_ChannelSchemaType",
    "selectedChannelId": "$ChatComposer_abc123_SelectedChannelId",
    "selectedContactId": "$ChatComposer_abc123_SelectedContactId",
    "sendersChannels": "$ChatComposer_abc123_SendersChannels",
    "recipientContacts": "$ChatComposer_abc123_RecipientContacts",
    "chatInput": "$ChatComposer_abc123_ChatInput",
    "filesToUpload": "$ChatComposer_abc123_FilesToUpload",
    "sendMessage": { "request": "crt.SendChatMessageRequest" },
    "chatInputChange": { "request": "crt.UpdateChatInputRequest", "params": { "value": "@event" } },
    "selectedChannelIdChange": {
      "request": "crt.UpdateSelectedChannelRequest",
      "params": { "id": "@event" }
    },
    "selectedContactIdChange": {
      "request": "crt.UpdateSelectedContactRequest",
      "params": { "id": "@event" }
    },
    "filesToUploadChange": {
      "request": "crt.UpdateFilesToUploadRequest",
      "params": { "files": "@event" }
    }
  }
}
```

### 2.2 (Optional) Add handlers

```jsonc
{
  "request": "crt.SendChatMessageRequest",
  "handler": async (request, next) => {
    // send the composed message
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChatComposer` are in `ComponentRegistry.json` under `componentType: "crt.ChatComposer"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// ChatChannelConfig — shape for the `channels` input
interface ChatChannelConfig {
  schemaType: string;         // required — e.g. "OmnichannelChatSchema"
  caption: string;            // required — display label for the channel type selector
  icon?: string;              // optional — icon identifier
  providerId?: string;        // optional — provider Guid for pre-filtering
}

// RequestBindingConfig — shape for output bindings
interface RequestBindingConfig {
  request: string;
  params?: RequestParamsBindingConfig;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}
```

---

## 5. Copy-paste minimal example

No direct `viewConfigDiff` insert found in PackageStore — `crt.ChatComposer` is typically
embedded inside a chat page wired to omnichannel page logic. Use the recipe in §2 as the
starting point, wiring at minimum `sendMessage` and `chatInputChange`:

```jsonc
// viewConfigDiff entry — minimal chat composer
{
  "operation": "insert",
  "name": "ChatComposer",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatComposer",
    "chatInput": "$ChatComposer_ChatInput",
    "sendMessage": { "request": "crt.SendChatMessageRequest" },
    "chatInputChange": {
      "request": "crt.UpdateChatInputRequest",
      "params": { "value": "@event" }
    }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare text and attachments attributes
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ChatComposer_ChatInput": { "value": "" },
      "ChatComposer_FilesToUpload": { "value": [] }
    }
  }
}

// viewConfigDiff.values — bind via $-prefix
"chatInput": "$ChatComposer_ChatInput",
"filesToUpload": "$ChatComposer_FilesToUpload"
```

`chatInput` is `propertyBindable` — bind it to a viewModel attribute to pre-fill or read the
current message text. `filesToUpload` drives the attachment tile list.

---

## 6.1 Operator typing presence (automatic)

The composer notifies the backend that the operator is typing as a side effect of `chatInput`
changes — there is **no input or output to wire**. While a chat id is resolved (i.e. an existing
chat for the selected channel + recipient), keystrokes start an "operator is typing" signal that
is re-asserted while typing continues, and a "stopped" signal is sent once typing pauses or the
operator sends, discards, switches chat, or the panel is destroyed. The signal is routed through
the channel-agnostic `OmnichannelChatService.setAgentTyping` → `SetAgentTyping` endpoint;
channels whose backend has no agent-typing notifier resolve a server-side no-op, so nothing extra
is needed per channel. No typing signal is sent until a chat id is resolved.

---

## 7. Common pitfalls

1. **Omitting `selectedChannelIdChange`** — when the user picks a channel, the composer fires this
   output; without a handler writing it back to a page attribute, the selection resets on next
   render.
2. **`channels` empty without `providerId`** — the channel selector is hidden when `channels` is
   empty; populate it from page logic before rendering the composer.
3. **`sendMessage` without a handler** — the send button fires silently; always wire it to a
   request that actually sends the composed message.
4. **`filesToUpload` not cleared after send** — after a successful send, emit an empty array via
   `filesToUploadChange` to reset the attachment tiles.
5. **`providerId` change clears selections** — changing `providerId` resets `selectedChannelId`,
   `selectedContactId`, and `chatIdChange`; set it only once unless the user intentionally switches
   providers.
6. **`editorReadonlyChange` / `sendDisabledChange` are state outputs, not inputs** — do not try to
   force-disable the editor via these outputs; they reflect computed channel restriction state.
7. **`chatCreated` vs `chatIdChange`** — `chatCreated` fires once when a new chat is created;
   `chatIdChange` fires whenever the resolved chat id changes (including after creation).

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ChatComposer"`, unique `name`, valid
  `parentName`, `propertyName: "items"`.
- [ ] `sendMessage` wired to a handler or platform request.
- [ ] `chatInputChange` wired so the text attribute stays in sync.
- [ ] `selectedChannelIdChange` and `selectedContactIdChange` wired if channel/contact selection
  is needed.
- [ ] `filesToUploadChange` wired to clear/update the attachments attribute after send.
- [ ] `channels` populated from page logic (not hard-coded in schema).
