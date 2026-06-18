# How to Add a Message Composer Selector (`crt.MessageComposerSelector`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MessageComposerSelector` into a Creatio Freedom UI page schema.
>
> A `crt.MessageComposerSelector` is a multi-channel message composer container. It renders a
> tabbed selector that switches between different composer channels (Feed, Email, Chat, etc.).
> Child composer components are placed in its `items` content slot.

## Metadata

- **Category**: interactive
- **Container**: yes (children go into the `items` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: `crt.FeedComposer`, `crt.ChatComposer`, `crt.EmailComposer` (via `items` slot)

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewModelConfigDiff` | Attributes for `composerEvent` and `selectedComposerChannelIndex`. |
| 2 | `viewConfigDiff` | An `insert` op with `type: "crt.MessageComposerSelector"` and child composers in `items`. |
| 3 | `handlers` (optional) | A handler for `composerEventChange` or `selectedComposerChannelIndexChange`. |

### 1.1 Naming convention

```
MessageComposer_<id>                          // view element name; use GENERATE_GUID_MACRO in schema
$MessageComposer_<id>_composerEvent           // composerEvent attribute
$MessageComposer_<id>_selectedChannel         // selectedComposerChannelIndex attribute
```

---

## 2. Step-by-step recipe

### 2.1 Declare viewModel attributes (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "MessageComposer_abc_composerEvent": { "value": [] },
      "MessageComposer_abc_selectedChannel": { "value": 0 }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MessageComposer_abc",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MessageComposerSelector",
    "defaultChannel": "crt.FeedComposer",
    "items": [],
    "composerEvent": "$MessageComposer_abc_composerEvent",
    "selectedComposerChannelIndex": "$MessageComposer_abc_selectedChannel",
    "preserveContent": true,
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 Handle composer events (optional)

```jsonc
// viewConfigDiff.values
"composerEventChange": { "request": "crt.ComposerEventChangeRequest" }

// handlers entry
{
  "request": "crt.ComposerEventChangeRequest",
  "handler": async (request, next) => {
    // request.parameters.changedValues holds the new ComposerEvent[]
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MessageComposerSelector` are in `ComponentRegistry.json` under
`componentType: "crt.MessageComposerSelector"`. This guide covers only the assembly mechanics.

---

## 4. Shape of `ComposerEvent`

```ts
interface ComposerEvent {
  name: string;   // e.g. "ComposerReady", "FocusInput", "ReplyEmail", "BlindZoneWidth"
  params?: {
    composerSchemaType?: string;
    isFocused?: boolean;
    emailId?: string;
    action?: ReplyAction;
    offset?: number;
  };
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FeedComposer_event": { "value": [] },
      "FeedComposer_channel": { "value": 0 }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "MessageComposer_ref1",
  "parentName": "ComposerFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MessageComposerSelector",
    "defaultChannel": "crt.FeedComposer",
    "items": [],
    "composerEvent": "$FeedComposer_event",
    "selectedComposerChannelIndex": "$FeedComposer_channel",
    "preserveContent": true,
    "visible": true
  }
}
```

---

## 6. Driving from page state

`composerEvent`, `selectedComposerChannelIndex`, and `preserveContent` are bound to viewModel
attributes. Changing `selectedComposerChannelIndex` programmatically switches the active
composer channel.

---

## 7. Common pitfalls

1. **`defaultChannel` must match an `items` entry** — if the channel type is not found in
   `items`, the component falls back to the first visible item; set `defaultChannel` to the
   canonical schema type string (e.g. `"crt.FeedComposer"`).
2. **`preserveContent: false`** destroys the inactive composer DOM on tab switch — this resets
   draft state; use `true` (default) to keep drafts across channel switches.
3. **`items: []` at insert time** — child composers are added as separate `insert` ops
   whose `parentName` points to this component's `name`; the `items` array in `values` starts
   empty.
4. **`composerEvent` must be initialized** — bind it to a viewModel attribute initialized to
   `[]`; an `undefined` value causes the component to silently ignore incoming events.
5. **The `GENERATE_GUID_MACRO` placeholder in `defaultPropertyValues`** — the designer
   auto-replaces it with a real GUID; when writing by hand use a real GUID suffix for `name`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.MessageComposerSelector"`, unique `name`.
- [ ] `composerEvent` and `selectedComposerChannelIndex` bound to viewModel attributes.
- [ ] `defaultChannel` set to the schema type of the default composer.
- [ ] Child composer inserts have `parentName` matching this component's `name`.
- [ ] `preserveContent` set to `true` to avoid losing draft content on tab switch.
