# How to Add a Menu Divider (`crt.MenuDivider`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MenuDivider` into a Creatio Freedom UI page schema.
>
> A `crt.MenuDivider` renders a horizontal separator line inside a dropdown menu.
> It has no properties of its own — its only purpose is visual grouping between menu item groups.

## Metadata

- **Category**: navigation
- **Container**: no
- **Parent types**: `crt.Menu` `items` array, `crt.Button` `menuItems` array
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | Include `{ "type": "crt.MenuDivider" }` inside a menu `items` array. |

`crt.MenuDivider` is not a standalone view element; it is an inline item config object inside a
`crt.Menu` or `crt.Button` `menuItems` array. It never needs a `name`, `parentName`, or
`propertyName`.

---

## 2. Step-by-step recipe

### 2.1 Insert as a menu item (inside `items` or `menuItems`)

```jsonc
// Inside crt.Button menuItems
"menuItems": [
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(Action1_Caption)#",
    "icon": "edit",
    "clicked": { "request": "crt.EditRequest" }
  },
  {
    "type": "crt.MenuDivider"
  },
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(Action2_Caption)#",
    "icon": "delete",
    "clicked": { "request": "crt.DeleteRequest" }
  }
]
```

---

## 3. Property reference

`crt.MenuDivider` has no `inputs` or `outputs`. Full registry entry is in
`ComponentRegistry.json` under `componentType: "crt.MenuDivider"`.

---

## 5. Copy-paste minimal example

```jsonc
// Inside crt.Menu items (real PackageStore usage pattern)
{
  "type": "crt.MenuDivider",
  "visible": "$DataGrid_abc.DataGrid_abcDS_Status | crt.SomeVisibilityConverter"
}
```

> `visible` is the only useful optional property on a divider — use it to conditionally
> show the separator line based on page state.

---

## 7. Common pitfalls

1. **Placing a divider at the start or end of a menu** — a leading or trailing divider looks
   odd and provides no visual grouping; always place dividers between non-empty item groups.
2. **Using `visible: false` unconditionally** — remove the divider entirely instead of binding
   it to a permanent false value.
3. **Assigning a `name` or `caption`** — neither property is read by the component; omit them.

---

## 8. Quick checklist

- [ ] `type: "crt.MenuDivider"` placed between meaningful item groups in the menu.
- [ ] No `caption`, `icon`, or `clicked` — those are read from `crt.MenuItem` only.
- [ ] `visible` is set if the divider should conditionally appear alongside optional items.
