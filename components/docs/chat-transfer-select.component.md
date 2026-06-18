# How to Add a Chat Transfer Select (`crt.ChatTransferSelect`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ChatTransferSelect` into a Creatio Freedom UI page schema.
>
> `crt.ChatTransferSelect` is a **dropdown button** that lets the agent transfer the current chat
> to another operator or a queue. It renders a `crt.ChatTransferSelectList` internally, populates
> it with live operator/queue data, and emits `chatTransferred` when the transfer completes.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.ChatTransferSelect"`. **Always present.** |
| 2 | `handlers` (optional) | A handler for `chatTransferred` to update page state after transfer. |

`crt.ChatTransferSelect` has no datasource and no create command. The `systemMessageTemplate`
input is typically fetched from the chat service and bound via a page attribute.

### 1.1 Naming convention

```
ChatTransferSelect_<id>   // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the transfer select (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChatTransferSelect_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatTransferSelect",
    "chatId": "$CurrentChatId",
    "systemMessageTemplate": "$ChatMessageTemplate",
    "disabled": "$IsTransferDisabled",
    "chatTransferred": {
      "request": "crt.ChatTransferredRequest",
      "params": { "event": "@event" }
    }
  }
}
```

### 2.2 (Optional) Add a handler

```jsonc
{
  "request": "crt.ChatTransferredRequest",
  "handler": async (request, next) => {
    // request.parameters.event = { operatorId, operatorName } or { queueId, queueName }
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChatTransferSelect` are in `ComponentRegistry.json` under
`componentType: "crt.ChatTransferSelect"`. This guide covers only the assembly mechanics.

---

## 4. Shape of `ChatMessageTemplate`

```ts
interface ChatMessageTemplate {
  message?: string;           // usually empty for templates
  messageDirection?: number;  // 0 = Incoming, 1 = Outgoing
  chatId?: Guid;              // chat identifier
  channelId?: Guid;           // channel identifier
  queueId?: Guid;             // current queue id — excluded from transfer target list
}
```

`chatTransferred` payload shape:

```ts
// operator transfer
{ operatorId: Guid; operatorName: string; }
// queue transfer
{ queueId: Guid; queueName: string; }
```

---

## 5. Common pitfalls

1. **`systemMessageTemplate` must be populated before the dropdown opens** — it is passed to the
   transfer API; if `null`, the backend cannot identify the source chat/channel/queue.
2. **`chatId` change does not reload the operator list** — the list loads when the user opens the
   dropdown; `chatId` is used only as a parameter for the transfer API.
3. **`chatTransferred` payload differs by tab** — operator transfers emit `{ operatorId,
   operatorName }`; queue transfers emit `{ queueId, queueName }`. Check both shapes in your handler.
4. **`disabled`** — bind to a page attribute when the transfer action should be blocked (e.g.
   chat not yet assigned or already being transferred).

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ChatTransferSelect"`, unique `name`, valid
  `parentName`, `propertyName: "items"`.
- [ ] `chatId` bound to the current chat session id.
- [ ] `systemMessageTemplate` bound to a page attribute populated from `OmnichannelChatService`.
- [ ] `chatTransferred` wired to update page state after transfer.
- [ ] `disabled` bound if transfer should be conditionally blocked.
