# How to Add a Template Select (`crt.TemplateSelect`) to a Freedom UI Page

> Audience: code agent inserting `crt.TemplateSelect` into a Creatio Freedom UI page schema.
> A `crt.TemplateSelect` is a chat-panel component that coordinates template selection between a text input, a floating template list, and a trigger detector. When the user types `//` in the bound input, a template dropdown opens. Selecting a template resolves macros (via the server when `chatId` is provided) and emits the final text via `chatInputChange`.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, chat-panel containers
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TemplateSelect"`, `chatInput` and `chatId` bound to viewModel attributes, and `attachToSelector` pointing to the input element. |
| 2 | `handlers` | A handler for `chatInputChange` to update the chat input attribute when the component replaces the trigger text with the resolved template. |

### 1.1 Naming convention
```
TemplateSelect_<id>               // view element name
$TemplateSelect_<id>_chatInput    // attribute bound to the live text of the chat input
$TemplateSelect_<id>_chatId       // attribute holding the current chat GUID
```

---

## 2. Step-by-step recipe

### 2.1 Insert the `crt.TemplateSelect`

```jsonc
{
  "operation": "insert",
  "name": "TemplateSelect_abc1",
  "parentName": "ChatActionsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TemplateSelect",
    "chatId": "$ActiveChatId",
    "chatInput": "$ChatInputText",
    "disabled": false,
    "attachToSelector": "#chat-input-element"
  }
}
```

### 2.2 Add a handler for `chatInputChange`

```jsonc
{
  "request": "crt.ChatInputChangeRequest",
  "handler": async (request, next) => {
    // request.parameters.text contains the updated input value
    return next?.handle(request);
  }
}
```

Wire in the view element:
```jsonc
"chatInputChange": { "request": "crt.ChatInputChangeRequest" }
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TemplateSelect` are in `ComponentRegistry.json` under `componentType: "crt.TemplateSelect"`. This guide covers
only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "TemplateSelect_chat",
  "parentName": "ChatActionsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TemplateSelect",
    "chatId": "$ActiveChatId",
    "chatInput": "$ChatInputText",
    "chatInputChange": { "request": "crt.UpdateChatInputRequest" },
    "templateSelected": { "request": "crt.TemplateSelectedRequest" }
  }
}
```

---

## 6. Driving from page state

`chatInput` and `chatId` are bindable and should be kept in sync with the page's chat state:
```jsonc
"chatInput": "$ChatInputText",   // mirrors the live text attribute
"chatId": "$ActiveChatId"        // current conversation GUID (null = no prior chat)
```

When `chatId` is `null`, macro resolution is skipped and the raw template text is inserted.

---

## 7. Common pitfalls

- **`chatInput` not updated in response to `chatInputChange`.** The component emits `chatInputChange` after replacing the `//` trigger with the resolved template text; if your handler does not update the bound attribute the component's internal state diverges from the displayed input.
- **`attachToSelector` not matching the actual input element.** The component uses the selector to read cursor position and restore focus after template insertion. An incorrect selector causes the cursor to be placed at the end of the input rather than after the inserted text.
- **`chatId` is `null` when macros are expected.** Without a `chatId` the server-side macro resolution is skipped; templates insert their raw text including unresolved macro placeholders.
- **`disabled: true` blocking the `//` trigger.** When `disabled` is `true` the component suppresses the trigger detector entirely; no dropdown opens even if the user types `//`.
- **Handling `templateSelected` instead of `chatInputChange`.** `templateSelected` fires with the template object and resolved text, but it is the `chatInputChange` output that carries the updated full input string needed to refresh the attribute.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.TemplateSelect"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `chatInput` bound to the live chat input attribute.
- [ ] `chatId` bound to the current conversation GUID attribute.
- [ ] `chatInputChange` wired to a request handler that updates the chat input attribute.
- [ ] `attachToSelector` set if cursor-position-aware insertion is required.
- [ ] Handler for `templateSelected` provided if post-selection logic is needed.
