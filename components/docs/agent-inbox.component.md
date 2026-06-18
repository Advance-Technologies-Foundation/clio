# How to Add an Agent Inbox (`crt.AgentInbox`) to a Freedom UI Page

> Audience: code agent inserting `crt.AgentInbox` into a Creatio Freedom UI page schema.
> Displays an agent's call inbox for the contact-center product; manages active call state, wrap-up
> timers, and call identity across content slots (call controls, center panel, footer, top actions).

## Metadata
- **Category**: display
- **Container**: yes (content slots: `callControlsPanel`, `centerPanel`, `footerPanel`, `topActions`)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: call-control components inserted into the named content slots

---

## 1. Mental model — the 2-3 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.AgentInbox"` and data bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for `callsCollection`, `selectedCallId`, and the `currentCall*` outputs. |
| 3 | `handlers` | Request handlers for each `*Change` output you need to react to. |

`crt.AgentInbox` is **not a designer-palette item** — it cannot be dragged from the components tray and
must be inserted programmatically.

### 1.1 Naming convention
```
AgentInbox_<id>            // view element name
$AgentInbox_<id>_attr      // $-prefix attribute names
```

---

## 2. Step-by-step recipe

### 2.1 Declare attributes in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "CallsCollection": {
      "value": null
    },
    "SelectedCallId": {
      "value": null
    },
    "CurrentCall": {
      "value": null
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "AgentInbox_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.AgentInbox",
    "callsCollection": "$CallsCollection",
    "selectedCallId": "$SelectedCallId",
    "editableAttributesList": [],
    "callsCollectionChange": { "request": "crt.HandleCallsCollectionChangeRequest" },
    "selectedCallIdChange": { "request": "crt.HandleSelectedCallIdChangeRequest" },
    "currentCallChange": { "request": "crt.HandleCurrentCallChangeRequest" },
    "identityClick": { "request": "crt.HandleIdentityClickRequest" }
  }
}
```

### 2.3 Add handlers

```jsonc
{
  "request": "crt.HandleCurrentCallChangeRequest",
  "handler": async (request, next) => {
    // request.parameter contains the new CallLookupValue or null
    return next?.handle(request);
  }
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.AgentInbox` are in `ComponentRegistry.json` under `componentType: "crt.AgentInbox"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`callsCollection` expects a `BaseViewModelCollection` — a live collection object managed by the platform,
not a plain array. Wire it from a datasource collection attribute rather than constructing one manually.

`CallLookupValue` is defined in the registry `references.typeDefinitions`:
```ts
interface CallLookupValue {
  value: string;          // guid
  displayValue: string;   // display name
  name: string;           // schema name
  callId?: string;        // telephony call identifier
  state?: 0|1|2|3|4|5|6; // call state enum
  index?: number;
  parentName?: string;
  propertyName?: string;
  primaryImageValue?: string;
  dataValueType?: string;
}
```

`LookupValue`:
```ts
interface LookupValue {
  value: string;          // guid
  displayValue: string;
  name: string;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — declare required attributes
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "CallsCollection": { "value": null },
    "SelectedCallId": { "value": null }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "AgentInbox",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.AgentInbox",
    "callsCollection": "$CallsCollection",
    "selectedCallId": "$SelectedCallId",
    "editableAttributesList": [],
    "currentCallChange": { "request": "crt.HandleCurrentCallChangeRequest" },
    "identityClick": { "request": "crt.HandleIdentityClickRequest" }
  }
}
```

---

## 7. Common pitfalls

1. **Not declaring attributes before binding** — every `$Attribute` used in `values` must appear in `viewModelConfigDiff.attributes`; a missing attribute silently evaluates to `undefined`.
2. **Empty setter pattern for `currentCall*` inputs** — these inputs (`currentCall`, `currentCallContact`, `currentCallAccount`, `currentCallIdentifiedContacts`, `currentCallIdentifiedAccounts`) have empty setters intentionally; the component manages those values internally and emits them back via the matching `*Change` outputs. Do not expect to drive them from page attributes.
3. **Not wiring `callsCollectionChange`** — the output fires when the call collection mutates; if not handled, the page attribute `callsCollection` goes stale.
4. **Using `AgentInbox` outside the contact-center product** — this component depends on `CtiStateService` and `WrapUpService` which are provided by the contact-center module. Pages outside that context will throw at runtime.
5. **Inserting children into wrong slot names** — valid content slot names are `callControlsPanel`, `centerPanel`, `footerPanel`, `topActions`. Any other `propertyName` is silently ignored.
6. **Forgetting `editableAttributesList`** — this array controls which call attributes are restored after a collection reload; leaving it empty means no unsaved call data is recovered after `ViewModelCollectionActionType.Reload`.
7. **Ignoring `identityClick`** — the component emits a `CallIdentity` when the user clicks a caller identity badge; without a handler the navigation never happens.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AgentInbox"`, unique `name`, valid `parentName`.
- [ ] `callsCollection` bound to a platform-managed collection attribute.
- [ ] `selectedCallId` bound to an attribute that holds the active call's database UID.
- [ ] `callsCollectionChange` and `selectedCallIdChange` outputs wired to request handlers.
- [ ] `currentCallChange` output wired if the page needs to react to call state changes.
- [ ] `identityClick` output wired if identity-navigation is required.
- [ ] Children (call controls, etc.) inserted into the correct named content slots.
- [ ] Contact-center module (providing `CtiStateService`) is available in the page context.
