# How to Add Chat Quick Actions (`crt.ChatQuickActions`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ChatQuickActions` into a Creatio Freedom UI page schema.
>
> `crt.ChatQuickActions` is a **button with a dropdown** that loads and displays business process
> quick actions for the current chat session. When the user selects an action, the component
> executes the linked business process and fires `actionExecuted`. It owns no datasource — actions
> are loaded automatically when `chatId` changes.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.ChatQuickActions"`. **Always present.** |
| 2 | `handlers` (optional) | A request handler for `actionExecuted` to respond after a process executes. |

`crt.ChatQuickActions` does not require `viewModelConfigDiff` or `modelConfigDiff`. The component
fetches available actions from the chat service when `chatId` is bound.

### 1.1 Naming convention

```
ChatQuickActions_<id>   // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the quick actions button (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChatQuickActions_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatQuickActions",
    "chatId": "$CurrentChatId",
    "contactId": "$CurrentContactId",
    "disabled": "$IsQuickActionsDisabled",
    "actionExecuted": { "request": "crt.QuickActionExecutedRequest", "params": { "process": "@event" } }
  }
}
```

### 2.2 (Optional) Add a handler

```jsonc
{
  "request": "crt.QuickActionExecutedRequest",
  "handler": async (request, next) => {
    // respond to process execution completion; request.parameters.process = process name
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChatQuickActions` are in `ComponentRegistry.json` under
`componentType: "crt.ChatQuickActions"`. This guide covers only the assembly mechanics.

---

## 4. Common pitfalls

1. **`chatId` must be a valid non-empty Guid** — the component does not load actions when
   `chatId` is falsy; only set this after a chat session is selected.
2. **`contactId` is required for process execution** — the business process receives both
   `ChatId` and `ContactId` as input parameters; omitting `contactId` may cause process errors.
3. **`actionExecuted` payload is the process name string** — use `@event` to get the process name
   in params, not a chat/contact id.
4. **Actions are fetched on `chatId` change** — changing `chatId` triggers an automatic reload of
   the available actions list from the server.

---

## 5. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ChatQuickActions"`, unique `name`, valid
  `parentName`, `propertyName: "items"`.
- [ ] `chatId` and `contactId` bound to page attributes populated after session selection.
- [ ] `disabled` bound to a page attribute if you need to disable actions in certain states.
- [ ] `actionExecuted` wired to a handler or left unwired (process notification is shown by default).
