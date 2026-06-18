# How to Add a Badge (`crt.Badge`) to a Freedom UI Page

> Audience: code agent inserting `crt.Badge` into a Creatio Freedom UI page schema.
> A container that overlays a small visual indicator (dot) on top of its child element; used to
> signal unread counts, alerts, or status without showing a numeric label.

## Metadata

- **Category**: display
- **Container**: yes (wraps a single child in the `items` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: any single view element (e.g. `crt.Button`, `crt.ButtonToggleGroupItem`)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Badge"` and the child element nested in `items`. **Always present.** |

`crt.Badge` is **view-only** — no model, no attribute, no handlers. Drop it around any element to
give it a badge indicator. The badge is shown/hidden by the `enabled` input.

### 1.1 Naming convention

```
Badge_<id>          // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Badge_abc123",
  "parentName": "ToolbarFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Badge",
    "color": "primary",
    "enabled": true,
    "items": []
  }
}
```

### 2.2 Insert the child element inside the badge

```jsonc
{
  "operation": "insert",
  "name": "NotifyButton",
  "parentName": "Badge_abc123",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "caption": "#ResourceString(NotifyButton_Caption)#",
    "icon": "notification",
    "iconPosition": "only-icon",
    "clicked": { "request": "crt.OpenNotificationsRequest" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Badge` are in `ComponentRegistry.json` under `componentType: "crt.Badge"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// BadgeOffset — a numeric pixel offset value
type BadgeOffset = number;  // negative = move inside, positive = move outside
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — badge wrapping a notification button
{
  "operation": "insert",
  "name": "NotificationBadge",
  "parentName": "HeaderFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Badge",
    "color": "primary",
    "enabled": "$HasUnreadNotifications",
    "items": []
  }
}
```

```jsonc
// viewConfigDiff — child inside the badge
{
  "operation": "insert",
  "name": "NotificationButton",
  "parentName": "NotificationBadge",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "icon": "notification",
    "iconPosition": "only-icon",
    "clicked": { "request": "crt.OpenNotificationsRequest" }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare a boolean attribute to control badge visibility
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "HasUnreadNotifications": { "value": false }
  }
}

// viewConfigDiff.values
"enabled": "$HasUnreadNotifications"
```

---

## 7. Common pitfalls

1. **`items` must be present and non-empty** — `crt.Badge` wraps its child through the `items` content slot; without a child, the badge renders as an invisible element.
2. **`enabled: false` hides the dot, not the child** — the wrapped element remains visible; only the badge indicator is toggled.
3. **`offset` vs `offsetX`/`offsetY`** — setting `offset` applies the same value to both axes; use `offsetX`/`offsetY` for asymmetric adjustments. Setting all three is additive on the axis where both are set.
4. **Badge color literals** — only `"primary"`, `"accent"`, and `"warn"` are accepted; custom color strings are ignored.
5. **Child slot is `items`, not a named content projection** — the registry declares `contentSlots: ["items"]`; insert the child with `propertyName: "items"`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Badge"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `items: []` present in values so the child insert has a slot to target.
- [ ] A child view element inserted with `parentName` pointing to the badge name and `propertyName: "items"`.
- [ ] `color` set to one of `"primary"`, `"accent"`, `"warn"`.
- [ ] `enabled` bound to a boolean attribute if the badge visibility should change at runtime.
