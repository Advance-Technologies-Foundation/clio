# How to Add a Call Conversation (`crt.CallConversation`) to a Freedom UI Page

> Audience: code agent inserting `crt.CallConversation` into a Creatio Freedom UI page schema.
> A CTI call panel that shows active call information, caller identity, hold/transfer controls, and
> DTMF dial pad; it binds to call-state attributes and emits change events when identity or call data
> is updated.

## Metadata

- **Category**: interactive
- **Container**: yes (call-controls panel goes into the `callControlsPanel` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: view elements in the `callControlsPanel` content slot

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.CallConversation"` and attribute bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for `currentCall`, `currentCallContact`, `currentCallAccount`, `currentCallIdentifiedContacts`, `currentCallIdentifiedAccounts`, `outboundNumber`. |

Handlers are optional — wire `identityClick` or `currentCallChange` only if you need custom logic
on identity selection or call-state changes.

### 1.1 Naming convention

```
CallConversation_<id>                       // view element name
$CallConversation_<id>_currentCall          // $-attribute for the current call data
$CallConversation_<id>_currentCallContact   // $-attribute for contact lookup
$CallConversation_<id>_currentCallAccount   // $-attribute for account lookup
```

---

## 2. Step-by-step recipe

### 2.1 Declare viewModel attributes (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "CallConversation_main_currentCall":                  {},
    "CallConversation_main_currentCallContact":           {},
    "CallConversation_main_currentCallAccount":           {},
    "CallConversation_main_currentCallIdentifiedContacts": {},
    "CallConversation_main_currentCallIdentifiedAccounts": {},
    "CallConversation_main_outboundNumber":               {}
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff`)

```jsonc
{
  "operation": "insert",
  "name": "CallConversation_main",
  "parentName": "SidebarContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.CallConversation",
    "currentCall":                    "$CallConversation_main_currentCall",
    "currentCallContact":             "$CallConversation_main_currentCallContact",
    "currentCallAccount":             "$CallConversation_main_currentCallAccount",
    "currentCallIdentifiedContacts":  "$CallConversation_main_currentCallIdentifiedContacts",
    "currentCallIdentifiedAccounts":  "$CallConversation_main_currentCallIdentifiedAccounts",
    "outboundNumber":                 "$CallConversation_main_outboundNumber",
    "currentCallChange":              { "request": "crt.CallConversationChangeRequest" },
    "identityClick":                  { "request": "crt.CallIdentityClickRequest" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.CallConversation` are in `ComponentRegistry.json` under `componentType: "crt.CallConversation"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// CallConversation — the active call state object
interface CallConversation {
  callerId?: string;
  callId?: string;
  databaseUId?: string;
  dtmfInput?: string;
  identity?: {
    id: string;            // required
    identityType?: "Account" | "Contact" | "Employee";
    name?: string;
    number?: string;
    photo?: string;
    accountId?: string;
    accountName?: string;
    city?: string;
    department?: string;
    job?: string;
    fields?: Array<{ caption: string; icon: string; value: string }>;
    // ... additional fields
  };
  consultCall?: CallConversation;  // nested consult call
}

// LookupValue — lookup binding
interface LookupValue {
  value: string;
  displayValue: string;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — minimal attribute set
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "ActiveCall":                    {},
    "ActiveCallContact":             {},
    "ActiveCallAccount":             {},
    "ActiveCallIdentifiedContacts":  {},
    "ActiveCallIdentifiedAccounts":  {}
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "CallConversation",
  "parentName": "PhonePanelContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.CallConversation",
    "currentCall":                   "$ActiveCall",
    "currentCallContact":            "$ActiveCallContact",
    "currentCallAccount":            "$ActiveCallAccount",
    "currentCallIdentifiedContacts": "$ActiveCallIdentifiedContacts",
    "currentCallIdentifiedAccounts": "$ActiveCallIdentifiedAccounts",
    "identityClick":                 { "request": "crt.NavigateToIdentityRequest" }
  }
}
```

---

## 7. Common pitfalls

1. **Stand-alone mode vs data-bound mode** — when `isStandAlone` is `true` (the default), the component subscribes to `CtiStateService` directly and ignores the `currentCall` input; to drive the call state from attributes, `isStandAlone` must be `false`.
2. **`currentCallContact` / `currentCallAccount` setters are no-ops** — these inputs exist for two-way attribute binding; the component computes the actual values internally from the call's identity options and emits them via `*Change` outputs.
3. **`identityClick` payload is a `CallIdentity` object** — not a `LookupValue`; the handler receives the full identity including `entityName` and `id` for navigation.
4. **`outboundNumber` is auto-synced** — when a non-incoming call is active, the component overrides `outboundNumber` from the call's `callerId`; do not expect the bound attribute to persist a manually set value during an active call.
5. **`callControlsPanel` content slot** — additional call control elements can be projected into this slot via child `insert` ops with `propertyName: "callControlsPanel"`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.CallConversation"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] viewModel attributes declared for all bound inputs.
- [ ] `currentCall` bound to a `CallConversation`-typed attribute.
- [ ] `identityClick` wired if navigation to contact/account page is needed.
- [ ] Aware that `isStandAlone: true` (default) makes the component self-subscribe to CTI state.
