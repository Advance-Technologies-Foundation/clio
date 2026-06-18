# How to Add a Base Message Composer (`crt.BaseMessageComposer`) to a Freedom UI Page

> Audience: code agent inserting `crt.BaseMessageComposer` into a Creatio Freedom UI page schema.
> A rich-text message input panel with attachment support, template selection, draft caching, and
> publish-on-hotkey (`Ctrl+Enter`); designed for omnichannel messaging pages.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.BaseMessageComposer"` and starting values. **Always present.** |

`crt.BaseMessageComposer` owns no datasource and no schema attributes — all input state is passed
as direct property values. Outputs fire events; wire them to `handlers` as needed.

### 1.1 Naming convention

```
BaseMessageComposer_<id>        // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "BaseMessageComposer_main",
  "parentName": "ComposerContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.BaseMessageComposer",
    "disabled": false,
    "inputPlaceHolder": "#ResourceString(BaseMessageComposer_main_placeholder)#",
    "useTemplates": false,
    "useDrafts": false,
    "loadAttachments": true,
    "maxAttachmentsSize": 0,
    "expandToContent": false,
    "messagePublish": { "request": "crt.PublishMessageRequest" }
  }
}
```

### 2.2 (Optional) Add publish handler

```jsonc
{
  "request": "crt.PublishMessageRequest",
  "handler": async (request, next) => {
    // read composer value via component ref or page attribute
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.BaseMessageComposer` are in `ComponentRegistry.json` under
`componentType: "crt.BaseMessageComposer"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// ComposerDataCachingService — injectable service shape
interface ComposerDataCachingService {
  get(code: string): string;
  set(code: string, value: string): void;
  remove(code: string): void;
}

// RichTextEditorMentionService — injected from the DI tree
// to enable @-mention autocomplete in the editor; optional.
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — minimal message composer
{
  "operation": "insert",
  "name": "MessageComposer",
  "parentName": "ConversationContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.BaseMessageComposer",
    "disabled": false,
    "loadAttachments": true,
    "messagePublish": { "request": "crt.SendMessageRequest" }
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.SendMessageRequest",
  "handler": async (request, next) => {
    return next?.handle(request);
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — toggle disabled state
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "MessageComposer_disabled": { "value": false }
  }
}

// viewConfigDiff.values
"disabled": "$MessageComposer_disabled"
```

---

## 7. Common pitfalls

1. **`messagePublish` must be wired** — the composer emits `messagePublish` when the user clicks Send or presses `Ctrl+Enter`; without a handler the message is silently discarded.
2. **`sendingButtonDisabled` ≠ `disabled`** — `disabled` disables the entire editor (no typing, no focus); `sendingButtonDisabled` only disables the Send button while typing remains possible.
3. **`expandToContent: true` can overflow the viewport** — when `false` (default) the composer auto-caps its height so Send stays visible; set `true` only in pages where the composer has its own scrollable container.
4. **`maxContentHeight` overrides auto-fit** — if set, it takes priority over the viewport-fit algorithm; only set it when you want a fixed editor height.
5. **`composerCacheCode` must be unique per composer** — the component uses this string as a key in `ComposerDataCachingService`; duplicate codes between two composers on the same page will cause draft data to collide.
6. **`loadAttachments: false`** — disables file-attachment UI completely; attachment-added events will not fire.
7. **`mentionsService`** — this is an injectable service reference, not a plain string; it cannot be set from a schema `values` block directly without a custom binding.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.BaseMessageComposer"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `messagePublish` wired to a request handler.
- [ ] `inputPlaceHolder` set for localization using `#ResourceString(<key>)#`.
- [ ] `composerCacheCode` provided and unique when `useDrafts: true`.
- [ ] `disabled` bound to a page attribute if you need runtime toggling.
- [ ] `loadAttachments` set appropriately for the messaging scenario.
