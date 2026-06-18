# How to Add an Icon (`crt.Icon`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Icon` into a Creatio Freedom UI page schema.
>
> `crt.Icon` is a view-only component that renders one of the platform's named icons with
> optional background, padding, and tooltip.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Icon"`. |

No attributes, no datasource, no children. Pure decoration.

### 1.1 Naming convention

```
Icon_<id>           // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Icon_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Icon",
    "iconName": "info-icon",
    "size": "16",
    "color": "#0D2E4E",
    "backgroundType": "circle",
    "backgroundColor": "#E7ECFA",
    "padding": "xs",
    "tooltip": "#ResourceString(Icon_xkp4r_Tooltip)#",
    "ariaLabel": "Information",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Icon` are in `ComponentRegistry.json` under `componentType: "crt.Icon"`. This guide
covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — decorative icon next to a heading
{
  "operation": "insert",
  "name": "StatusIcon",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Icon",
    "iconName": "check",
    "size": "24",
    "color": "#2BBC4F",
    "backgroundType": "circle",
    "backgroundColor": "#E8F8EC",
    "padding": "xs",
    "tooltip": "#ResourceString(StatusIcon_Done)#",
    "ariaLabel": "Done",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 5. Common pitfalls

1. **`iconName` not in the platform icon registry** — the icon area renders empty without an error. Cross-check against the available icon set in your project's icon library before shipping.
2. **`size` passed as a number instead of a string** — internal designer state expects `"16"`, not `16`. Numeric inputs are accepted by the runtime but break designer round-trip.
3. **Missing `ariaLabel` on meaningful icons** — fails accessibility audits. Skip it only for purely decorative icons whose meaning is conveyed elsewhere.
4. **`backgroundColor` set while `backgroundType: "none"`** — the colour is silently ignored.
5. **Using `color` for both the glyph and the frame** — `color` styles the glyph; the frame uses `backgroundColor`. Confusing these gives an unreadable icon.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Icon"`, unique `name`, valid `parentName`, `propertyName: "items"`, `index`.
- [ ] `iconName` matches an entry in the platform icon registry.
- [ ] `size` is a string (e.g. `"16"`).
- [ ] `ariaLabel` is set for any icon that conveys meaning.
- [ ] `layoutConfig` provided.
