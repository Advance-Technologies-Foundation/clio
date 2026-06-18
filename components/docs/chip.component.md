# How to Add a Chip (`crt.Chip`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Chip` into a Creatio Freedom UI page schema.
>
> `crt.Chip` is a **small inline label badge** with a configurable color. The `color` input accepts
> a hex code or a semantic color name, and the component auto-computes a contrasting text color and
> background. Typically used inside a `crt.ChipList` or as an inline badge inside a flex container.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.ChipList` (items slot)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Chip"`. **Always present.** |

`crt.Chip` is **view-only** — no datasource, no outputs, no create command.

### 1.1 Naming convention

```
Chip_<id>   // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the chip (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Chip_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Chip",
    "color": "#8B9FDA"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Chip` are in `ComponentRegistry.json` under `componentType: "crt.Chip"`. This guide covers
only the assembly mechanics.

---

## 4. Color behavior

The `color` setter computes both text and background colors automatically:

- If `color` is a **hex string** (e.g. `"#8B9FDA"`), `getChipTextColor()` picks a contrasting
  foreground and `getChipBackgroundColor()` lightens the hex for the background.
- If `color` is a **semantic keyword** (non-hex string), it is used as a CSS class/variable
  lookup.
- An unset or falsy `color` falls back to the default chip style.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — standalone chip
{
  "operation": "insert",
  "name": "Chip_abc123",
  "parentName": "ToolsFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Chip",
    "color": "#8B9FDA"
  }
}
```

---

## 6. Common pitfalls

1. **Color must be a hex or a recognized keyword** — arbitrary CSS color names (e.g. `"red"`) are
   not handled by `getChipTextColor`/`getChipBackgroundColor` and may produce unexpected results.
2. **Chip renders no text by default** — it displays projected content; use Angular template
   projection or `crt.ChipList` to supply the label.

---

## 7. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Chip"`, unique `name`, valid `parentName`.
- [ ] `color` is a hex string (e.g. `"#8B9FDA"`) or a recognized semantic keyword.
