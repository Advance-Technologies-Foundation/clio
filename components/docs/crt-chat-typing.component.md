# How to Add a Chat Typing Indicator (`crt.ChatTyping`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ChatTyping` into a Creatio Freedom UI page schema.
>
> `crt.ChatTyping` displays an animated typing indicator with the author's avatar and an optional
> label. It is typically nested inline inside the `placeholder` or `items` slot of a parent
> container and toggled via `visible`. No datasource or create command — all inputs are driven
> from page state.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: inline in slots of `crt.Conversation`, `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An inline element inside a parent container's slot (or a standalone `insert` op). **Always present.** |

`crt.ChatTyping` owns no datasource and no outputs. It renders passively based on its inputs.

### 1.1 Naming convention

```
ChatTyping_<id>   // view element name when inserted as a standalone op
```

---

## 2. Step-by-step recipe

### 2.1 Insert the typing indicator (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChatTyping_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatTyping",
    "author": "$TypingAuthorContact",
    "message": "#ResourceString(ChatTyping_message)#",
    "visible": "$IsTypingVisible"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChatTyping` are in `ComponentRegistry.json` under `componentType: "crt.ChatTyping"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of the `author` input

```ts
// Contact — from @terrasoft/studio-enterprise/ui/cdk
interface Contact {
  id?: string;
  name?: string;
  photo?: string;   // URL for the avatar image
  // (additional fields may exist — use only these three for viewConfigDiff)
}
```

Note: When `author.name` is `"Creatio.ai"`, the avatar is hidden regardless of the `photo` value.
The `loading` input controls a skeleton/loading state but is not listed in the registry — use
`visible` to show/hide the whole indicator instead.

---

## 5. Copy-paste minimal example

Real-world inline usage from `CopilotPanel.js` in PackageStore:

```jsonc
// Inline inside a parent container slot (no separate insert op needed)
{
  "type": "crt.ChatTyping",
  "author": "$CopilotContact",
  "message": "#ResourceString(CopilotTyping_text)#"
}
```

---

## 6. Common pitfalls

1. **`author` must be a `Contact` object** — pass the full contact object, not just an id string.
   Binding `"author": "$SomeGuid"` will result in no avatar and the name falling back to empty.
2. **`message` supports `#ResourceString(...)#`** — use localized resource strings for the typing
   label text (e.g. `"John is typing..."`) rather than hard-coded strings.
3. **Show/hide via `visible`, not by removing the element** — toggling `visible` on a bound
   attribute is the canonical way to show the indicator; removing/re-adding via diff is expensive.
4. **`author.name === "Creatio.ai"` suppresses the avatar** — by design, the Creatio AI bot hides
   its photo and uses a default icon style.

---

## 7. Quick checklist

- [ ] `author` bound to a `Contact` page attribute with at least `name` populated.
- [ ] `message` set to a localized resource string.
- [ ] `visible` bound to a page attribute toggled by your typing-state logic.
