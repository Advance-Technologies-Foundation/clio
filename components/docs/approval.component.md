# How to Add an Approval Widget (`crt.Approval`) to a Freedom UI Page

> Audience: code agent inserting `crt.Approval` into a Creatio Freedom UI page schema.
> Renders an approval widget showing the current approver, approve/reject actions, and metric counters
> (pending, positive, negative); drives approval flow via request outputs and listens for WebSocket
> change notifications.

## Metadata
- **Category**: interactive
- **Container**: yes (content slot: `items`)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none in most schemas — `items` slot is reserved for custom content

---

## 1. Mental model — the 3 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Approval"` and entity bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for `recordId`, `approval`, `approvalsMetrics`, `dataLoaded`, `approvalChangedHandler`. |
| 3 | `handlers` | Request handlers for `loadData`, `approve`, `reject`, `createApproval`. |

No `modelConfigDiff` is required — the component manages approval data loading internally via the
`loadData` output.

### 1.1 Naming convention
```
Approval_<id>             // view element name
$Approval_<id>_approval   // attribute for the approval object
$Approval_<id>_metrics    // attribute for the metrics array
```

---

## 2. Step-by-step recipe

### 2.1 Declare attributes in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "OrderApproval": { "value": null },
    "OrderApprovalMetrics": { "value": null },
    "OrderApprovalDataLoaded": { "value": false },
    "OrderApprovalChangedHandler": { "value": null }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "OrderApprovalWidget",
  "values": {
    "type": "crt.Approval",
    "activeColor": "white",
    "inactiveColor": "white",
    "items": [],
    "entityName": "Order",
    "approvalEntityName": "OrderVisa",
    "visible": true,
    "hiddenWhenNoData": true,
    "layoutConfig": {}
  },
  "parentName": "SideContainer",
  "propertyName": "items",
  "index": 1
}
```

### 2.3 Wire request outputs

```jsonc
"loadData": { "request": "crt.LoadApprovalDataRequest" },
"approve": { "request": "crt.ApproveRequest" },
"reject": { "request": "crt.RejectRequest" }
```

### 2.4 Add handlers

```jsonc
{
  "request": "crt.LoadApprovalDataRequest",
  "handler": async (request, next) => {
    // load approval data and set attributes
    return next?.handle(request);
  }
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Approval` are in `ComponentRegistry.json` under `componentType: "crt.Approval"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

The registry defines the key types in `references.typeDefinitions`:

```ts
// Approval
interface Approval {
  id: string;          // guid of the approval record
  objective: string;   // description of the approval purpose
  visaOwner?: LookupValue;
}

// ApprovalMetric
interface ApprovalMetric {
  status: string;   // one of the ApprovalStatus guids
  count: number;
}

// ApprovalChangedEvent
interface ApprovalChangedEvent {
  status: string;   // one of the ApprovalStatus guids
}
```

`RequestBindingConfig` shape for outputs:
```ts
interface RequestBindingConfig {
  request: string;
  params?: Record<string, unknown>;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real PackageStore usage (Orders_FormPage)
{
  "operation": "insert",
  "name": "OrderApprovalWidget",
  "values": {
    "type": "crt.Approval",
    "activeColor": "white",
    "inactiveColor": "white",
    "items": [],
    "entityName": "Order",
    "approvalEntityName": "OrderVisa",
    "visible": true,
    "hiddenWhenNoData": true,
    "layoutConfig": {}
  },
  "parentName": "SideContainer",
  "propertyName": "items",
  "index": 1
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "ApprovalWidget_visible": { "value": true }
  }
}

// viewConfigDiff.values
"visible": "$ApprovalWidget_visible"
```

---

## 7. Common pitfalls

1. **Missing `entityName` and `approvalEntityName`** — without these the `loadData` output cannot identify which entity's approvals to fetch; the widget renders empty.
2. **`hiddenWhenNoData: false` when there are no approvals** — the widget shows a blank panel in the sidebar. Use `hiddenWhenNoData: true` in sidebars to auto-hide when the record has no approval records.
3. **Not wiring `loadData`** — the component emits `loadData` on init and when the record ID changes; without a handler the approval data is never fetched.
4. **Not handling `approve` / `reject`** — clicking the action buttons emits these outputs. Without handlers, the approval status is never updated.
5. **`items: []`** — the `items` slot is a content slot; always include `"items": []` in `values`; do not put children there unless explicitly needed.
6. **WebSocket subscription** — the component subscribes to WebSocket messages for the `recordId`; if `recordId` changes (e.g. after save), re-subscription is automatic, but ensure `recordId` is bound to the correct attribute.
7. **`approvalChangedHandler` timing** — this input triggers side-effects (moving to next approval, custom events). Wire it only when you receive the approval result from the server, not during page initialization.
8. **`activeColor`/`inactiveColor`** — these control the icon palette inside the widget. Use `"white"` for dark sidebar backgrounds and `"default"` for light backgrounds.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Approval"`, unique `name`, valid `parentName`.
- [ ] `entityName` and `approvalEntityName` set to the correct schema names.
- [ ] `loadData` output wired to a request handler that loads approval data.
- [ ] `approve` and `reject` outputs wired to request handlers.
- [ ] `items: []` included in `values`.
- [ ] `hiddenWhenNoData` set based on whether blank rendering is acceptable.
- [ ] `recordId` bound to the page's primary record ID attribute.
