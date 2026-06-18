# How to Add a Button (`crt.Button`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Button` into a Creatio mobile page schema.
>
> A tappable button for triggering actions, navigation, and menus on a mobile page.
> On mobile, a Button only needs **`viewConfigDiff`** (plus a handler if you want the tap to do something).

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.MobileScaffold` (`items`, `leading`, `actions`), `crt.GridContainer` items, `crt.FlexContainer` items
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Button"` and a `clicked` request binding. |
| 2 | `handlers` (optional) | A request handler matching the `clicked` request you fired. |

`crt.Button` on mobile is **view-only** — it owns no data attributes and requires no `viewModelConfigDiff` unless you want to bind `disabled` or `visible` to page state.

### 1.1 Mobile scaffold slots

For toolbar placement use `parentName: "Scaffold"` with:
- `propertyName: "actions"` — right-hand action buttons (e.g. Save)
- `propertyName: "leading"` — left-hand navigation buttons (e.g. Cancel)
- `propertyName: "items"` — body area

### 1.2 Naming convention

```
Button_<id>          // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "SaveButton",
  "parentName": "Scaffold",
  "propertyName": "actions",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "caption": "#ResourceString(SaveButton_caption)#",
    "color": "primary",
    "icon": "save",
    "iconPosition": "only-text",
    "clicked": { "request": "crt.SaveRecordRequest" }
  }
}
```

### 2.2 (Optional) Add a handler in `handlers`

```jsonc
{
  "request": "crt.MyMobileActionRequest",
  "handler": async (request, next) => {
    // your logic here
    return next?.handle(request);
  }
}
```

Without a handler, the tap fires but nothing happens. Prefer built-in requests (`"crt.SaveRecordRequest"`, `"crt.CancelOpenedItemRequest"`) for standard actions.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Button` are in
`ComponentRegistry.json` under `componentType: "crt.Button"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding — show/hide the button conditionally. |
| `menuTitle` | `string` | Title shown in the native mobile menu picker dialog when `menuItems` are shown. |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "SaveButton",
  "values": {
    "type": "crt.Button",
    "caption": "#ResourceString(SaveButton_caption)#",
    "color": "primary",
    "clicked": { "request": "crt.SaveRecordRequest" }
  },
  "parentName": "Scaffold",
  "propertyName": "actions",
  "index": 0
}
```

---

## 5. Common pitfalls

1. **Forgetting `clicked.request`** — the tap fires silently if nothing is subscribed. Wire it to at least a no-op request to keep behavior testable.
2. **Wrong `color` literal** — must be one of the supported strings (`default`, `primary`, `danger`, etc.). Custom values fall back to `default`.
3. **Placing in wrong scaffold slot** — Save/Submit go in `actions`; Cancel/Back go in `leading`; body content goes in `items`. Mixing them breaks the mobile toolbar layout.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Button"`, unique `name`, valid `parentName` and `propertyName`.
- [ ] `caption` set (or `iconPosition: "only-icon"` + `icon` for an icon-only button).
- [ ] `clicked.request` wired to either a platform request or a custom handler in `handlers`.
- [ ] `color` is one of the supported literals.
