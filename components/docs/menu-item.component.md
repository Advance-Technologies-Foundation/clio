# How to Add a Menu Item (`crt.MenuItem`) to a Freedom UI Page

> Audience: code agent placing a `crt.MenuItem` inside a `menuItems` array of a button,
> button toggle group, or another menu host.
>
> A `crt.MenuItem` is **never a top-level element** of `viewConfigDiff`. It lives inside
> the `menuItems` array of a parent component (typically `crt.Button` with `clickMode: "menu"`).

## Metadata

- **Category**: interactive
- **Container**: yes
- **Parent types**: `crt.Button`, `crt.MenuItem`, `crt.DataGrid`, `crt.Feed`, `crt.Gallery`
- **Typical children**: `crt.MenuItem` (nested menu)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (inside parent's `menuItems` or `items` array) | A `crt.MenuItem` entry; not a stand-alone `insert` op. |
| 2 | `handlers` (optional) | A request handler wired to the menu item's `clicked.request`. |

Because menu items are configuration objects on a parent — not standalone view elements —
they are added directly to the parent's `values.menuItems` array, not via a separate `insert` op.

### 1.1 Naming convention

Menu items typically don't need a `name`; they're identified by position and `caption`. If you need to address one programmatically, give it a unique `name` like the host (`MenuItem_<id>`).

---

## 2. Step-by-step recipe

### 2.1 Embed in a button's `menuItems` (`viewConfigDiff` entry of the host)

```jsonc
{
  "operation": "insert",
  "name": "ActionsButton",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "caption": "#ResourceString(ActionsButton_Caption)#",
    "clickMode": "menu",
    "menuItems": [
      {
        "type": "crt.MenuItem",
        "caption": "#ResourceString(ActionsButton_Item_Edit)#",
        "icon": "edit",
        "iconColor": "primary",
        "clicked": { "request": "crt.EditRecordRequest" }
      },
      {
        "type": "crt.MenuItem",
        "caption": "#ResourceString(ActionsButton_Item_Delete)#",
        "icon": "delete",
        "iconColor": "warn",
        "clicked": { "request": "crt.DeleteRecordRequest" }
      }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Add request handlers

```jsonc
{
  "request": "crt.EditRecordRequest",
  "handler": async (request, next) => next?.handle(request)
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MenuItem` are in `ComponentRegistry.json` under `componentType: "crt.MenuItem"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// Inside a parent crt.Button or another menu host's `menuItems` array:
{
  "type": "crt.MenuItem",
  "caption": "#ResourceString(MenuItem_Save_Caption)#",
  "icon": "save",
  "iconColor": "primary",
  "clicked": { "request": "crt.SaveRecordRequest" }
}
```

Nested submenu:

```jsonc
{
  "type": "crt.MenuItem",
  "caption": "#ResourceString(Export_Caption)#",
  "icon": "download",
  "items": [
    { "type": "crt.MenuItem", "caption": "PDF", "clicked": { "request": "crt.ExportPdfRequest" } },
    { "type": "crt.MenuItem", "caption": "Excel", "clicked": { "request": "crt.ExportExcelRequest" } }
  ]
}
```

---

## 5. Common pitfalls

1. **Trying to use `crt.MenuItem` as a top-level `insert` op** — it is not a stand-alone view element. Place it inside a host (`crt.Button.menuItems`, etc.).
2. **Passing a raw translation key as `caption`** — `CrtBaseMenuItemComponent` logs a warning and auto-translates, but designer round-trip breaks. Always pass `#ResourceString(...)#`.
3. **Forgetting `clicked.request`** — clicking the item closes the menu but fires nothing. If no handler is needed, point at a no-op or platform request.
4. **`iconColor` outside the named palette** — `"primary"/"accent"/"warn"/"default"` go through the theme; any other string is treated as a literal CSS color and won't auto-adapt to theme changes.
5. **Deeply nested submenus (>2 levels)** — visually supported but a UX smell; consider flattening or splitting into multiple buttons.
6. **`visible` as a function** — supported, but only callable in environments that hydrate functions from JSON (most JSON schemas store strings; reserve the function form for in-code page definitions).

---

## 6. Quick checklist

- [ ] `type: "crt.MenuItem"` set on every entry.
- [ ] `caption` provided (resource string preferred).
- [ ] `clicked.request` wired (or `items` provided for a submenu node).
- [ ] If using `iconColor`, value matches `"primary"/"accent"/"warn"/"default"` for theme-aware behavior.
- [ ] Item is placed inside a host's `menuItems` (or compatible) array — not a stand-alone `viewConfigDiff` entry.
