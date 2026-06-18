# How to Add a Menu Item (`crt.MenuItem`) to a Mobile Page

> Audience: code agent placing a `crt.MenuItem` inside a `menuItems` array of a button,
> button toggle group, or another menu host in a Creatio mobile Freedom UI page.
>
> A `crt.MenuItem` is **never a top-level element** of `viewConfigDiff`. It lives inside
> the `menuItems` array (or `items` of a `crt.Menu`) of a parent component.

## Metadata

- **Category**: interactive
- **Container**: yes
- **Parent types**: `crt.Button`, `crt.Menu`, `crt.MenuItem` (nested)
- **Typical children**: `crt.MenuItem` (nested submenu)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` (inside parent's `menuItems` or `items` array) | A `crt.MenuItem` entry; not a stand-alone `insert` op. |
| 2 | `handlers` (optional) | A request handler wired to the menu item's `clicked.request`. |

Because menu items are configuration objects on a parent — not standalone view elements —
they are added directly to the parent's `values.menuItems` array (or `crt.Menu.items`), not
via a separate `insert` op.

### 1.1 Naming convention

Menu items typically don't need a `name`; they're identified by position and `caption`. If you
need to address one programmatically, give it a unique `name` like: `MenuItem_<id>`.

---

## 2. Step-by-step recipe

### 2.1 Embed in a `crt.Menu.items` array (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "DeleteMenuItem",
  "parentName": "ActionsMenu",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(DeleteMenuItem_Caption)#",
    "icon": "crt-icon-delete",
    "clicked": { "request": "crt.DeleteRecordRequest" }
  }
}
```

### 2.2 (Optional) Add request handlers

```jsonc
{
  "request": "crt.DeleteRecordRequest",
  "handler": async (request, next) => next?.handle(request)
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MenuItem` are in `ComponentRegistry.json` under `componentType: "crt.MenuItem"`. This guide
covers only the assembly mechanics.

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `enabled` | boolean | Whether the menu item is enabled and tappable. Default: `true`. Set to `false` to disable without hiding. |

---

## 4. Copy-paste minimal example

```jsonc
// Inside a parent crt.Menu's `items` array (or crt.Button's `menuItems`):
{
  "type": "crt.MenuItem",
  "caption": "#ResourceString(Delete_Caption)#",
  "icon": "crt-icon-delete",
  "iconColor": "warn",
  "clicked": { "request": "crt.DeleteRecordRequest" }
}
```

Nested submenu:

```jsonc
{
  "type": "crt.MenuItem",
  "caption": "#ResourceString(Export_Caption)#",
  "icon": "crt-icon-download",
  "items": [
    { "type": "crt.MenuItem", "caption": "PDF", "clicked": { "request": "crt.ExportPdfRequest" } },
    { "type": "crt.MenuItem", "caption": "Excel", "clicked": { "request": "crt.ExportExcelRequest" } }
  ]
}
```

---

## 5. Common pitfalls

1. **Trying to use `crt.MenuItem` as a top-level `insert` op** — it is not a stand-alone view element. Place it inside a host (`crt.Button.menuItems`, `crt.Menu.items`, etc.).
2. **Passing a raw translation key as `caption`** — `CrtBaseMenuItemComponent` logs a warning and auto-translates. Always pass `#ResourceString(...)#`.
3. **Forgetting `clicked.request`** — clicking the item closes the menu but fires nothing. If no handler is needed, point at a platform no-op request.
4. **`iconColor` outside the named palette** — `"primary"/"accent"/"warn"/"default"` go through the theme; any other string is treated as a literal CSS color.
5. **`visible` as a function** — supported in code, but most JSON schemas store strings. Use `"$AttributeName"` bindings for mobile pages.

---

## 6. Quick checklist

- [ ] `type: "crt.MenuItem"` set on every entry.
- [ ] `caption` provided (resource string preferred).
- [ ] `clicked.request` wired (or `items` provided for a submenu node).
- [ ] If using `iconColor`, value matches `"primary"/"accent"/"warn"/"default"` for theme-aware behavior.
- [ ] Item is placed inside a host's `menuItems` (or `crt.Menu.items`) array — not a stand-alone `viewConfigDiff` entry.

---

## 6. Related: `crt.MenuSeparator`

`crt.MenuSeparator` is a visual divider between menu items. It has no Angular component (no `@CrtMobileViewElement`) — it is a schema-only element.

```jsonc
{
  "operation": "insert",
  "name": "MenuSeparator1",
  "values": { "type": "crt.MenuSeparator" },
  "parentName": "ActionButton",
  "propertyName": "menuItems",
  "index": 2
}
```
