# How to Add an Omnichannel Inbox (`crt.OmnichannelInbox`) to a Freedom UI Page

> Audience: code agent inserting `crt.OmnichannelInbox` into a Creatio Freedom UI page schema.
>
> `crt.OmnichannelInbox` is a drop-in alias of `crt.Chat` that exposes the same container and outputs under a
> more generic name. It is used in Agent Inbox panels where multiple communication channels (chat, email, tasks)
> appear in a unified list. All inputs, outputs, and child slots are inherited from `crt.Chat` — consult the
> `crt.Chat` recipe for the full property reference.

## Metadata

- **Category**: interactive
- **Container**: yes (`items` slot)
- **Parent types**: root page container, `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: `crt.ChatList`, `crt.ChatItem`, and other chat-panel sub-components placed into `items`

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.OmnichannelInbox"`, starting values, and an empty `items: []`. **Always present.** |
| 2 | `handlers` (optional) | Handlers for outputs such as `openChatSession`, `openChatSessionWithMessage`, or `changeCombinedMode`. |

`crt.OmnichannelInbox` carries no datasource and no viewModel attributes of its own — it is purely a view
element. Child elements are inserted with their own `insert` operations that reference this element's `name`.

### 1.1 Naming convention

```
OmnichannelInbox_<id>    // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "OmnichannelInbox_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.OmnichannelInbox",
    "items": [],
    "visible": true,
    "fitContent": false,
    "padding": { "top": "none", "right": "none", "bottom": "none", "left": "none" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Wire outputs in `handlers`

```jsonc
{
  "request": "crt.OpenChatSessionRequest",
  "handler": async (request, next) => {
    // handle chat session open
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.OmnichannelInbox` are in `ComponentRegistry.json` under `componentType: "crt.OmnichannelInbox"`.
This guide covers only the assembly mechanics.

**Key outputs (from registry):**

| Output | Description |
|---|---|
| `openChatSession` | Emitted when a chat session should be opened. |
| `openChatSessionWithMessage` | Emitted when a session should open with a pre-filled message (payload has session ID + message). |
| `changeCombinedMode` | Emitted when the layout should switch between combined and split mode. |
| `chatViewInit` | Emitted once when the chat component finishes initialization. |
| `combinedModeButtonVisibleChange` | Emitted when the combined-mode button visibility changes. |

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// ContainerViewElements — the items array
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;

// ContainerPadding — per-side object or size keyword
type ContainerPadding =
  | { top?: string | number; right?: string | number; bottom?: string | number; left?: string | number }
  | 'none' | 'xs' | 'small' | 'medium' | 'large' | 'xl' | 'xxl';

// RequestBindingConfig — for outputs
interface RequestBindingConfig {
  request: string;           // e.g. 'crt.OpenChatSessionRequest'
  params?: Record<string, unknown>;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "OmnichannelInbox_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.OmnichannelInbox",
    "items": [],
    "visible": true,
    "fitContent": false,
    "padding": { "top": "none", "right": "none", "bottom": "none", "left": "none" },
    "openChatSession": { "request": "crt.OpenChatSessionRequest" }
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.OpenChatSessionRequest",
  "handler": async (request, next) => next?.handle(request)
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "OmnichannelInbox_visible": { "value": true }
    }
  }
}

// viewConfigDiff.values
"visible": "$OmnichannelInbox_visible"
```

---

## 7. Common pitfalls

1. **Omitting `items: []`** — the container requires the slot even when starting empty; missing it breaks child inserts.
2. **Confusing `crt.OmnichannelInbox` with `crt.Chat`** — they share the same template and all inputs/outputs; use `OmnichannelInbox` in new Agent Inbox pages, `Chat` in legacy chat pages.
3. **Using `combinedModeEnabled` without `combinedModeButtonVisible`** — `combinedModeEnabled` controls whether the feature is available; `combinedModeButtonVisible` controls whether the toggle button renders; set both for predictable UI.
4. **Forgetting `isCombinedMode` default** — the input defaults to `false`; wire it from page state if your page supports toggling between split and combined views.
5. **Binding `stretch: true` and `fitContent: true` at the same time** — they are mutually exclusive; `stretch` wins.
6. **`openChatSessionWithMessage` payload shape** — the event carries `{ sessionId, message }`. Bind a handler that destructures this if you need to pre-fill a message.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.OmnichannelInbox"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `items: []` present in `values`.
- [ ] `fitContent` and `stretch` are not both `true`.
- [ ] `padding` provided as per-side object (or omitted to use defaults).
- [ ] If `combinedModeButtonVisible: true`, also set `combinedModeEnabled`.
- [ ] Outputs wired to platform requests or custom `handlers` entries.
- [ ] `layoutConfig` present when this component sits inside a `crt.GridContainer`.
