# How to Add a Menu Label (`crt.MenuLabel`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MenuLabel` into a Creatio Freedom UI page schema.
>
> A `crt.MenuLabel` renders a non-clickable text heading inside a dropdown menu.
> It is used to group related menu items under a descriptive caption.

## Metadata

- **Category**: navigation
- **Container**: no
- **Parent types**: `crt.Menu` `items` array, `crt.Button` `menuItems` array
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | Include `{ "type": "crt.MenuLabel", "caption": "..." }` inside a menu `items` array. |

Like `crt.MenuDivider`, this component appears as an inline item object inside a menu's `items`
array, not as a standalone top-level insert op.

---

## 2. Step-by-step recipe

### 2.1 Insert as a menu item (inside `items` or `menuItems`)

```jsonc
// Inside crt.Menu items
"items": [
  {
    "type": "crt.MenuLabel",
    "caption": "#ResourceString(Menu_GroupLabel_Caption)#"
  },
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(Menu_Item1_Caption)#",
    "icon": "edit",
    "clicked": { "request": "crt.EditRequest" }
  }
]
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MenuLabel` are in `ComponentRegistry.json` under `componentType: "crt.MenuLabel"`. This
guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// Inside crt.Button menuItems — a labeled group
"menuItems": [
  {
    "type": "crt.MenuLabel",
    "caption": "#ResourceString(RecordActions_GroupLabel)#"
  },
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(EditRecord_Caption)#",
    "icon": "edit",
    "clicked": { "request": "crt.EditRecordRequest" }
  },
  {
    "type": "crt.MenuDivider"
  },
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(DeleteRecord_Caption)#",
    "icon": "delete",
    "clicked": { "request": "crt.DeleteRecordRequest" }
  }
]
```

---

## 7. Common pitfalls

1. **`caption` is required** — an empty or omitted `caption` renders an invisible element that
   still takes up vertical space in the dropdown.
2. **Using `#ResourceString(...)#` for caption** — always use resource strings for localized
   labels; plain strings are not translated when the UI language changes.
3. **`crt.MenuLabel` is not clickable** — if you need a clickable heading, use `crt.MenuItem`
   with a `clicked` binding instead.
4. **Do not place a `crt.MenuLabel` directly after a `crt.MenuDivider`** — the combination
   creates a visual double-separator; prefer label-only or divider-only grouping.

---

## 8. Quick checklist

- [ ] `type: "crt.MenuLabel"` placed at the top of a menu item group.
- [ ] `caption` set via `#ResourceString(...)#`.
- [ ] No `clicked`, `icon`, or `disabled` — those are only meaningful on `crt.MenuItem`.
