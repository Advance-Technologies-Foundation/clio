# How to Add an Incoming Items (`crt.IncomingItems`) to a Freedom UI Page

> Audience: code agent inserting a `crt.IncomingItems` into a Creatio Freedom UI page schema.
>
> `crt.IncomingItems` is a tile list that displays contact-center chat/call notifications waiting for an
> agent to accept. Each tile shows caller info and an accept button. Adding it requires a single
> `viewConfigDiff` insert op; it consumes a live server-push `items` feed and fires `tileClick` /
> `itemAccept` outputs.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, sidebar containers (omnichannel inbox pages)
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.IncomingItems"` and output bindings. **Always present.** |
| 2 | `handlers` | Request handlers for `tileClick` and `itemAccept`. **Required to act on user interactions.** |

`crt.IncomingItems` has **no datasource** of its own — the `items` array is pushed by the platform's
contact-center service. Configure which data attributes trigger incoming-item refresh via the `dataAttributes`
field (not in the registry — see §4 below).

### 1.1 Naming convention

```
IncomingItems_<id>        // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "IncomingItems_abc123",
  "parentName": "SidebarContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IncomingItems",
    "outdateDelay": 1,
    "dataAttributes": ["NewChats"],
    "tileClick": {
      "request": "crt.UpdateRecordRequest",
      "params": {
        "entityName": "@event.schemaName",
        "recordId": "@event.id"
      }
    },
    "itemAccept": {
      "request": "crt.AcceptChatRequest",
      "params": {
        "incomingItem": "@event"
      }
    }
  }
}
```

### 2.2 Add handlers (`handlers` entries)

```jsonc
{
  "request": "crt.AcceptChatRequest",
  "handler": async (request, next) => {
    // accept the incoming item, e.g. open the chat page
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.IncomingItems` are in `ComponentRegistry.json` under `componentType: "crt.IncomingItems"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// IncomingItem — one incoming chat/call tile
interface IncomingItem {
  id: string;              // record GUID
  schemaName: string;      // entity schema name (e.g. "VwSysChannel")
  // additional fields populated by the contact-center service
}

// dataAttributes — NOT in ComponentRegistry; present in PackageStore real schemas
// Array of attribute names whose changes trigger incoming-item list refresh.
// Example values: ["NewChats"], ["NewCalls"]
type dataAttributes = string[];
```

> **Note:** `dataAttributes` is a runtime-only field not present in `ComponentRegistry.json`. It appears
> consistently in real PackageStore omnichannel page schemas and is required for the component to receive
> live push updates. Always include it with the appropriate channel attribute name(s).

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — omnichannel inbox with chat accept
{
  "operation": "insert",
  "name": "IncomingItems_chats",
  "parentName": "InboxSidebar",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IncomingItems",
    "outdateDelay": 1,
    "dataAttributes": ["NewChats"],
    "tileClick": {
      "request": "crt.UpdateRecordRequest",
      "params": {
        "entityName": "@event.schemaName",
        "recordId": "@event.id"
      }
    },
    "itemAccept": {
      "request": "crt.AcceptChatRequest",
      "params": {
        "incomingItem": "@event"
      }
    }
  }
}
```

```jsonc
// handlers entry — accept chat
{
  "request": "crt.AcceptChatRequest",
  "handler": async (request, next) => {
    const incomingItem = request.parameters.incomingItem;
    // open the chat record page for incomingItem.id
    return next?.handle(request);
  }
}
```

---

## 7. Common pitfalls

1. **Missing `dataAttributes`.** Without `dataAttributes` the component never receives incoming-item push updates; always include the correct channel attribute name(s) for the page's channel type.
2. **`outdateDelay: 0` (default) on slow networks.** A delay of `0` means items never expire visually; set a positive value (e.g. `1` second) so stale tiles are dismissed when the agent misses them.
3. **`tileClick` without `@event.id`.** The `@event` param contains the `IncomingItem` data; use `@event.id` and `@event.schemaName` to open the correct record.
4. **`itemAccept` without a real accept handler.** Clicking the accept button fires `itemAccept`; if no handler is bound the tile does nothing and the incoming item stays on screen.
5. **Placing inside a scrolling container with fixed height.** The tile list grows with the number of incoming items; ensure the parent container has `overflow: auto` or a capped height to avoid page overflow.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.IncomingItems"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `dataAttributes` set to the channel-specific attribute name(s) (e.g. `["NewChats"]`).
- [ ] `tileClick` wired to a request handler (at minimum a no-op to prevent silent failures).
- [ ] `itemAccept` wired to a handler that accepts/opens the incoming item.
- [ ] `outdateDelay` set to a positive number for automatic tile expiry.
