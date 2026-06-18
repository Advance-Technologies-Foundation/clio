# How to Use Chat Transfer Select List (`crt.ChatTransferSelectList`) in a Freedom UI Page

> Audience: code agent working with Creatio Freedom UI page schemas.
>
> `crt.ChatTransferSelectList` is a **sub-component** rendered automatically by
> `crt.ChatTransferSelect`. It shows the operator/queue dropdown with search and tabs. It is
> not inserted directly into `viewConfigDiff` — the parent `crt.ChatTransferSelect` manages it
> internally. To show the transfer dropdown, insert `crt.ChatTransferSelect`.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: managed by `crt.ChatTransferSelect` internally — not placed directly in `viewConfigDiff`
- **Typical children**: none

---

## 1. Mental model — 0 places you edit directly

`crt.ChatTransferSelectList` has no standalone `viewConfigDiff` insert op. All configuration
(search, operator/queue loading, transfer API calls) is managed by the parent
`crt.ChatTransferSelect`. To show the list, insert the parent:

```jsonc
// viewConfigDiff — parent component (the entry point)
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
    "chatTransferred": { "request": "crt.ChatTransferredRequest" }
  }
}
```

---

## 2. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for
`crt.ChatTransferSelectList` are in `ComponentRegistry.json` under
`componentType: "crt.ChatTransferSelectList"`. Key inputs set by the parent:

| Input | Type | Notes |
|---|---|---|
| `chatId` | `Guid` | Passed from `crt.ChatTransferSelect` for the transfer API. |
| `systemMessageTemplate` | `ChatMessageTemplate` | Source chat/channel/queue metadata for transfer routing. |
| `listOpenChange` | output | Fires when the dropdown opens/closes. |
| `chatTransferred` | output | Fires when a transfer completes; payload `{ operatorId/Name }` or `{ queueId/Name }`. |

---

## 3. Common pitfalls

1. **Do not insert `crt.ChatTransferSelectList` in `viewConfigDiff` directly** — it is not a
   standalone insertable element; the parent `crt.ChatTransferSelect` renders it.
2. **`systemMessageTemplate.queueId`** — used to exclude the current queue from the transfer
   target list; ensure it is populated before the dropdown opens.

---

## 4. Quick checklist

- [ ] To show the transfer list, insert `crt.ChatTransferSelect` — do not use
  `crt.ChatTransferSelectList` directly.
