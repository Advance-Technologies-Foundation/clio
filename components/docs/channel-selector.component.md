# How to Add a Channel Selector (`crt.ChannelSelector`) to a Freedom UI Page

> Audience: code agent inserting `crt.ChannelSelector` into a Creatio Freedom UI page schema.
> A tab/button strip for switching between communication channels (email, chat, feed) in a message
> composer panel; filters channels by license and emits selection-change events when the user picks
> a different channel.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ChannelSelector"` and channel/selection bindings. **Always present.** |

`crt.ChannelSelector` has no datasource. The `channels` input receives a
`ComposerViewConfig[]` array; the component enforces chat-license restrictions automatically.

### 1.1 Naming convention

```
ChannelSelector_<id>            // view element name
$ChannelSelector_selectedIndex  // $-attribute for the currently selected channel index
$ChannelSelector_selectedSchema // $-attribute for the selected chat schema type
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChannelSelector_main",
  "parentName": "ComposerHeaderContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChannelSelector",
    "channels": "$AvailableChannels",
    "selectedChannelIndex": "$SelectedChannelIndex",
    "selectedSchemaType": "$SelectedSchemaType",
    "selectedChannelIndexChange": { "request": "crt.ChannelSelectedRequest" },
    "selectedChatSchemaTypeChange": { "request": "crt.ChatSchemaTypeChangedRequest" }
  }
}
```

### 2.2 (Optional) Handle selection change

```jsonc
{
  "request": "crt.ChannelSelectedRequest",
  "handler": async (request, next) => {
    // request contains the new index
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChannelSelector` are in `ComponentRegistry.json` under `componentType: "crt.ChannelSelector"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// ComposerViewConfig — a single channel tab configuration
interface ComposerViewConfig {
  type: string;       // e.g. "crt.ChatComposer", "crt.EmailComposer"
  visible?: boolean;  // if false, channel is hidden from the selector
  data?: {
    caption?: string;
    icon?: string;
    schemaType?: string;
    isChatChannel?: boolean;
  };
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — channel selection attributes
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "AvailableChannels":   {},
    "SelectedChannelIndex": { "value": 0 },
    "SelectedSchemaType":  { "value": "" }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ChannelSelector",
  "parentName": "ComposerToolbar",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChannelSelector",
    "channels": "$AvailableChannels",
    "selectedChannelIndex": "$SelectedChannelIndex",
    "selectedChannelIndexChange": { "request": "crt.ChannelSelectionChangedRequest" }
  }
}
```

---

## 7. Common pitfalls

1. **Chat channels require a license** — `crt.ChatComposer`-type channels are automatically hidden if the `CAN_USE_CHATS_INTEGRATION` license operation is not active; do not guard this in your handler.
2. **`channels` is not reactive until license check completes** — the component returns an empty visible list until the license query resolves; the list then populates on the next change-detection cycle.
3. **`selectedChannelIndexChange` emits the parent-composer index, not the visible-channel index** — channels may be re-indexed after filtering; use the emitted value directly rather than re-computing from the visible list.
4. **`selectedSchemaType` is for chat schema disambiguation** — only relevant when multiple chat channels with different schema types are present; for email/feed-only setups it can be omitted.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ChannelSelector"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `channels` bound to an attribute containing the `ComposerViewConfig[]` array.
- [ ] `selectedChannelIndex` bound to a numeric attribute.
- [ ] `selectedChannelIndexChange` wired to a handler if the parent composer needs to respond to channel switches.
