# How to Add a Menu (`crt.Menu`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Menu` into a Creatio Freedom UI page schema.
>
> A `crt.Menu` is a dropdown list of action items. It is almost always spawned
> programmatically by `crt.Button` (via `menuItems`) rather than inserted as a standalone
> view element. Insert it directly only when building a custom trigger component.

## Metadata

- **Category**: navigation
- **Container**: no (items are provided via the `items` input, not view-element slots)
- **Parent types**: typically embedded inside `crt.Button` — standalone insertion is rare
- **Typical children**: none (children are `CrtMenuItemViewElementConfig` objects in `items`)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Menu"` and an `items` array. |

> **Note:** In most Freedom UI pages, you do **not** insert `crt.Menu` directly. Instead,
> provide `menuItems` on a `crt.Button`, which embeds a `crt.Menu` internally. See the
> `crt.Button` recipe for the canonical approach.

### 1.1 Naming convention

```
Menu_<id>       // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Menu_abc",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Menu",
    "items": [
      {
        "type": "crt.MenuItem",
        "caption": "#ResourceString(Menu_abc_Item1_Caption)#",
        "icon": "edit",
        "clicked": { "request": "crt.EditItemRequest" }
      },
      {
        "type": "crt.MenuDivider"
      },
      {
        "type": "crt.MenuItem",
        "caption": "#ResourceString(Menu_abc_Item2_Caption)#",
        "icon": "delete",
        "clicked": { "request": "crt.DeleteItemRequest" }
      }
    ],
    "xPosition": "after",
    "yPosition": "below"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Menu` are in `ComponentRegistry.json` under `componentType: "crt.Menu"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of `CrtMenuItemViewElementConfig`

Each entry in `items` follows this shape:

```ts
interface CrtMenuItemViewElementConfig {
  type: 'crt.MenuItem' | 'crt.MenuLabel' | 'crt.MenuDivider';
  name?: string;         // auto-generated if omitted
  caption?: string;      // display text (required for crt.MenuItem)
  icon?: string;         // icon name from the platform icon registry
  visible?: boolean | string;   // static boolean or "$Attribute" binding
  disabled?: boolean | string;
  clicked?: RequestBindingConfig;  // action on click
  items?: CrtMenuItemViewElementConfig[];  // nested sub-menu
  nestedMenuYPosition?: 'above' | 'below';
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — standalone menu (rare; prefer crt.Button with menuItems)
{
  "operation": "insert",
  "name": "ActionsMenu",
  "parentName": "ActionsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Menu",
    "items": [
      {
        "type": "crt.MenuItem",
        "caption": "#ResourceString(ActionsMenu_Edit_Caption)#",
        "icon": "edit",
        "clicked": { "request": "crt.EditRecordRequest" }
      },
      { "type": "crt.MenuDivider" },
      {
        "type": "crt.MenuItem",
        "caption": "#ResourceString(ActionsMenu_Delete_Caption)#",
        "icon": "delete",
        "clicked": { "request": "crt.DeleteRecordRequest" }
      }
    ],
    "xPosition": "after",
    "yPosition": "below"
  }
}
```

---

## 7. Common pitfalls

1. **Use `crt.Button` with `menuItems` instead of inserting `crt.Menu` directly** — the button
   manages trigger, focus, and keyboard navigation automatically.
2. **`menuItems` input is deprecated** — use `items` (array of `CrtMenuItemViewElementConfig`)
   for all new schemas.
3. **Nested sub-menus**: set `items` on the parent `crt.MenuItem` config; the component
   auto-creates nested `crt.Menu` instances internally. Do not insert nested menus separately.
4. **`panelClass` must be a single class string** — multiple classes should be space-delimited
   in one string, not an array.
5. **`useGlassmorphism: true`** adds a blurred glass backdrop to the menu panel; only use
   it when the parent container is designed for glassmorphism style.
6. **`stopOverlayClickPropagation: true`** prevents overlay backdrop clicks from bubbling to
   the page — set this only when the menu is inside a modal or overlay panel that should not
   close on outside click.

---

## 8. Quick checklist

- [ ] Consider using `crt.Button` with `menuItems` instead of a standalone `crt.Menu`.
- [ ] If inserting directly: `items` array contains at least one `crt.MenuItem`.
- [ ] Each `crt.MenuItem` has `caption` and `clicked.request`.
- [ ] `xPosition` and `yPosition` are set for correct dropdown placement.
- [ ] `menuItems` (deprecated) is **not** used in new schemas — use `items` instead.
