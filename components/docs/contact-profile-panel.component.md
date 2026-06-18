# How to Add a Contact Profile Panel (`crt.ContactProfilePanel`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ContactProfilePanel` into a Creatio Freedom UI page schema.
> A `crt.ContactProfilePanel` is a collapsible sidebar panel that shows a contact's avatar, name, subtitle, and an expandable fields area with action buttons; it is used in omnichannel/chat pages to display the contact linked to the current conversation.

## Metadata

- **Category**: display
- **Container**: no (fields and actions are declared as inline `ContainerViewElements`, not as child insert ops)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, root page container
- **Typical children**: none (children defined inline via `fields` and `actions` inputs)

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ContactProfilePanel"` and starting values. **Always present.** |
| 2 | `handlers` (optional) | Request handlers for `opened`, `closed`, or `expandedChange` outputs. |

`crt.ContactProfilePanel` is **view-only** — no model or datasource of its own. It reads the contact via the `contact` input (a `LookupValue`) and renders the fields/actions passed as inline `ContainerViewElements`.

### 1.1 Naming convention

```
ContactProfilePanel_<id>       // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the panel (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ContactProfilePanel_abc123",
  "parentName": "SideContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ContactProfilePanel",
    "expanded": false,
    "contact": "$ContactLookup",
    "title": "$ContactName",
    "subtitle": "$ContactJobTitle",
    "icon": "messenger-icon",
    "fields": [],
    "actions": []
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ContactProfilePanel` are in `ComponentRegistry.json` under `componentType: "crt.ContactProfilePanel"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// contact — LookupValue shape
interface LookupValue {
  value: string;           // GUID of the contact record
  displayValue: string;    // contact display name
  primaryImageValue?: string;
}

// fields / actions — ContainerViewElements (inline child configs)
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;
```

---

## 7. Common pitfalls

1. **Binding `contact` to a plain string** — `contact` expects a `LookupValue` object (`{ value, displayValue }`); binding to a raw GUID string will break the component.
2. **Expecting `fields` to be separate `insert` ops** — `fields` is an inline `ContainerViewElements` array passed directly in `values`, not separate `viewConfigDiff` insert operations.
3. **Omitting `expanded`** — `expanded` defaults to `false`; always declare it explicitly to make the initial state clear.
4. **Setting `title` without a binding** — `title` falls back to `contact.displayValue` when empty; if you want a fixed label rather than the contact name, always pass an explicit `$-prefix` attribute.
5. **Using this outside a chat/sidebar context** — `crt.ContactProfilePanel` has no PackageStore usage outside omnichannel pages; prefer `crt.ContactCompactProfile` for general Customer 360 pages.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ContactProfilePanel"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `contact` bound to a `$-prefix` attribute that holds a `LookupValue`.
- [ ] `expanded` set to `false` or `true` explicitly.
- [ ] `fields` and `actions` provided as arrays (empty `[]` is valid).
- [ ] If `opened`/`closed`/`expandedChange` are needed, matching `handlers` entries are present.
