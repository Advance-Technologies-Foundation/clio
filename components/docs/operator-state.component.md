# How to Add an Operator State (`crt.OperatorState`) to a Freedom UI Page

> Audience: code agent inserting `crt.OperatorState` into a Creatio Freedom UI page schema.
>
> `crt.OperatorState` is a status indicator and dropdown button that shows and changes the agent's current
> presence state for both CTI telephony and chat channels simultaneously. It fetches available states from
> the platform and renders a split menu: one section for telephony (CTI) states and one for chat states.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.OperatorState"` and starting values. **Always present.** |

`crt.OperatorState` owns no outputs registered in the schema and fetches its state data internally via
`OperatorStatusService`. No `modelConfigDiff`, `viewModelConfigDiff`, or `handlers` entries are needed
for basic usage.

### 1.1 Naming convention

```
OperatorState_<id>    // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "OperatorState_header",
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.OperatorState",
    "ctiStatusesVisible": true,
    "chatStatusesVisible": true,
    "useGlassmorphism": false,
    "visible": true
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.OperatorState` are in `ComponentRegistry.json` under `componentType: "crt.OperatorState"`. This guide
covers only the assembly mechanics.

**Key inputs:**

| Input | Type | Default | Description |
|---|---|---|---|
| `ctiStatusesVisible` | `boolean` | `true` | Show the telephony (CTI) state section in the dropdown. |
| `chatStatusesVisible` | `boolean` | `true` | Show the chat state section in the dropdown. |
| `useGlassmorphism` | `boolean` | `false` | Apply glassmorphism visual style to the dropdown panel. |

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — show both CTI and chat sections (typical shell header)
{
  "operation": "insert",
  "name": "OperatorState_header",
  "parentName": "HeaderFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.OperatorState",
    "ctiStatusesVisible": true,
    "chatStatusesVisible": true,
    "useGlassmorphism": false,
    "visible": true
  }
}
```

```jsonc
// viewConfigDiff entry — chat-only (no telephony)
{
  "operation": "insert",
  "name": "OperatorState_chatOnly",
  "parentName": "HeaderFlexContainer",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.OperatorState",
    "ctiStatusesVisible": false,
    "chatStatusesVisible": true,
    "visible": true
  }
}
```

---

## 7. Common pitfalls

1. **Hiding both sections** — setting both `ctiStatusesVisible: false` and `chatStatusesVisible: false` renders an empty button with no menu; at least one section must be visible for the component to be useful.
2. **Placing inside a detail or modal** — `crt.OperatorState` is a shell-level widget that reads telephony and chat services. Embedding it inside a record page or detail may work but produces unexpected behavior when multiple instances are present.
3. **`useGlassmorphism` on non-glassmorphic pages** — enabling glassmorphism when the parent container does not use it creates visual inconsistency; match the surrounding UI.
4. **Missing service providers** — the component depends on `OperatorStatusService`; if this service is not provided in the application module, state fetching will silently fail.
5. **`isActiveCall` is not a `@CrtInput`** — it is an Angular `@Input` only (not wired to the schema system); it cannot be bound via `$Attribute` in `viewConfigDiff`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.OperatorState"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] At least one of `ctiStatusesVisible` or `chatStatusesVisible` is `true`.
- [ ] `useGlassmorphism` matches the visual context of the surrounding container.
- [ ] Component is placed in a shell header or panel, not in a record-page body.
