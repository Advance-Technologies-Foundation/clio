# How to Add a Conversation Template List (`crt.ConversationTemplateList`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ConversationTemplateList` into a Creatio Freedom UI page schema.
> A `crt.ConversationTemplateList` is an auto-loading dropdown list of message templates for omnichannel chat; it fetches grouped templates internally via `MessageTemplateService` based on the supplied `chatId`, and emits `templateSelected` when the user picks one.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, chat tool containers
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ConversationTemplateList"` and starting values. **Always present.** |
| 2 | `handlers` (optional) | Request handlers wired to `templateSelected`, `closed`, or `listOpenChange` outputs. |

`crt.ConversationTemplateList` is **view-only** — no model or datasource of its own. Templates are loaded internally; only `chatId` needs to be bound from the page.

### 1.1 Naming convention

```
ConversationTemplateList_<id>     // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the template list (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ConversationTemplateList_abc123",
  "parentName": "ToolsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ConversationTemplateList",
    "chatId": "$Id",
    "templateSelected": { "request": "crt.TemplateSelectedRequest" },
    "closed": { "request": "crt.TemplateListClosedRequest" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ConversationTemplateList` are in `ComponentRegistry.json` under
`componentType: "crt.ConversationTemplateList"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// templateSelected output payload — ChatTemplate
interface ChatTemplate {
  id: string;
  // + other template fields
}

// RequestBindingConfig shape
interface RequestBindingConfig {
  request: string;    // e.g. 'crt.TemplateSelectedRequest'
  params?: Record<string, unknown>;
}
```

---

## 7. Common pitfalls

1. **Not binding `chatId`** — without `chatId` the component loads templates without chat context; bind it to `"$Id"` (the current chat record GUID) for context-aware filtering.
2. **Confusing `closed` with `listOpenChange`** — `closed` fires only when the list dismisses; `listOpenChange` fires on every open/close toggle with a boolean value.
3. **Expecting manual data loading** — templates are fetched automatically by `MessageTemplateService`; do not bind a datasource or viewModel collection to this component.
4. **Using outside an omnichannel context** — `crt.ConversationTemplateList` depends on `MessageTemplateService` which is only available in the omnichannel module; it will fail in non-chat pages.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ConversationTemplateList"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `chatId` bound to the current chat record GUID (e.g. `"$Id"`).
- [ ] `templateSelected` output bound to a request that handles the selected template.
- [ ] If `closed` or `listOpenChange` events need handling, matching `handlers` entries exist.
