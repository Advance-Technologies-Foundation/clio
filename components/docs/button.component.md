# How to Add a Button (`crt.Button`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Button` into a Creatio Freedom UI page schema.
>
> A Freedom UI page schema is a single JS file with these sections:
> `viewConfigDiff`, `viewModelConfigDiff`, `modelConfigDiff`, `handlers`. A plain Button
> only needs **`viewConfigDiff`** (plus a handler if you want the click to do something).

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: `crt.MenuItem` (in the `menuItems` slot)

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Button"` and a `clicked` request binding. |
| 2 | `handlers` (optional) | A request handler matching the `clicked` request you fired. |

`crt.Button` is **view-only** — it owns no attributes, has no datasource, and (unless you give it `menuItems`) has no children. All you do is drop it into the view tree and wire its `clicked` output to a request.

### 1.1 How diff operations work

`viewConfigDiff` is an **array of diff operations**. For inserting a new view element:

- `operation: "insert"` — appends a new view element into a parent's slot.
- `parentName` — the `name` of the container that owns the slot.
- `propertyName` — the slot name on the parent (almost always `"items"`).
- `index` — position in the slot (0-based).
- `name` — unique identifier for this element across the whole page.
- `values` — the runtime configuration of the new element.

(See `JsonDiffOperation` in `libs/studio-enterprise/util/low-code/src/lib/models/json-diff-operation.ts`.)

### 1.2 Naming convention

```
Button_<id>          // view element name; <id> is any short unique slug
$Button_<id>_visible // attribute references use $-prefix (when bound, see § 6)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Button_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "caption": "#ResourceString(Button_xkp4r_Caption)#",
    "color": "primary",
    "size": "large",
    "iconPosition": "left-icon",
    "icon": "save-button-icon",
    "clickMode": "default",
    "clicked": { "request": "crt.MyButtonRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Add a handler in `handlers`

```jsonc
{
  "request": "crt.MyButtonRequest",
  "handler": async (request, next) => {
    // your logic here
    return next?.handle(request);
  }
}
```

Without a handler, the click fires but nothing happens. The base platform may already ship some standard requests (e.g. `"crt.SaveRecordRequest"`, `"crt.CancelOpenedItemRequest"`) — prefer those for built-in actions.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Button` are in `ComponentRegistry.json` under `componentType: "crt.Button"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. `menuItems` shape — splitting a button into a menu

```jsonc
"menuItems": [
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(Button_xkp4r_Item1)#",
    "icon": "edit-button-icon",
    "clicked": { "request": "crt.EditRequest" }
  },
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(Button_xkp4r_Item2)#",
    "icon": "delete-button-icon",
    "clicked": { "request": "crt.DeleteRequest" }
  }
]
```

When `clickMode: "default"` the button is split: caption fires `clicked`, the chevron opens the menu. When `clickMode: "menu"` the whole button opens the menu.

See crt.MenuItem guide for the full `crt.MenuItem` reference.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "SaveButton",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "caption": "#ResourceString(SaveButton_Caption)#",
    "color": "primary",
    "icon": "save-button-icon",
    "iconPosition": "left-icon",
    "clicked": { "request": "crt.SaveRecordRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

```jsonc
// handlers entry (only if you need custom logic; the example above reuses the platform request)
{
  "request": "crt.SaveRecordRequest",
  "handler": async (request, next) => next?.handle(request)
}
```

---

## 6. Driving the button from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "SaveButton_disabled": { "value": false }
    }
  }
}

// viewConfigDiff.values
"disabled": "$SaveButton_disabled"
```

---

## Icon values — the `icon` property vocabulary

`icon` accepts one of two shapes:

1. **A platform icon-registry name** (a `string`) — rendered as an SVG via `mat-icon [svgIcon]`.
   The **recommended, known-good** set is the `ButtonIcon` type — see
   `references.typeDefinitions.ButtonIcon.values` on this component's registry entry
   (`ComponentRegistry.json`); that is the curated list the designer offers for buttons. It is a
   **subset**, not the full universe: `mat-icon` resolves against the platform `MatIconRegistry`, so
   any other registered SVG icon name also works (e.g. `calendar-icon`, used in `crt.QuickFilter`
   stories). Either way the name **must be an exact registered name** — there are **no short
   aliases** (`"save"` is invalid; the registered name is `"save-button-icon"`). If the name is not
   registered, the icon area is left empty (and `mat-icon` logs an `Error retrieving icon :<name>!`
   to the console) — there is no UI/platform validation. Prefer a value from `ButtonIcon` when in
   doubt. The same icon mechanism applies to the `icon` of a `crt.MenuItem` (the `menuItems` slot).
   It also covers `config.icon` of a `crt.QuickFilter` chip for the `date-range` variant (bound
   directly to `mat-icon [svgIcon]`) and the `lookup` variant (forwarded to the chip's `crt.Button`);
   the `custom` variant renders a checkbox and **ignores `config.icon`**.
2. **An animated (Lottie) icon config** — an object instead of a string (the `ButtonAnimatedIcon`
   type), for an animated glyph:

   ```jsonc
   "icon": {
     "type": "animation",   // required literal
     "name": "loader",      // registered Lottie animation name (see note below)
     "width": "32px",       // optional, defaults to 32px
     "height": "32px",      // optional, defaults to 32px
     "autoplay": true,      // optional, default true
     "loop": true           // optional, default true; boolean only — crt.Button ignores a numeric repeat count and falls back to the default
   }
   ```

   `name` must be a **registered Lottie animation name**: `getAnimationData(name)` dynamically
   imports `assets/animation/<name>.json` from the `cdk` animation assets, so the value must match a
   bundled animation file (e.g. `loader`, `search`, `dashboard`). An unregistered name (e.g.
   `rocket`) fails the import and the glyph renders nothing — the same empty-icon failure mode as an
   unregistered `svgIcon`.

---

## 7. Common pitfalls

1. **Forgetting `clicked.request`** — the click event fires silently if nothing is subscribed. Even when no logic is needed, wire it to a no-op request to make the click testable.
2. **Wrong `color` literal** — must be one of the seven allowed strings. Custom colors (e.g. `"red"`, `"#fff"`) are ignored and the button falls back to default.
3. **Setting `type` to something other than `"button"/"submit"/"reset"`** — `CrtBaseButtonComponent.set type` silently rejects other values, leaving the previous `type` in place.
4. **Using `size: "default"`** — coerced to `"large"`. If you want the small variant, write `"small"` explicitly.
5. **`clickMode: "menu"` without `menuItems`** — the button becomes a dead end (no click, no menu to open).
6. **`icon` value not registered in the platform icon registry** — the icon area renders empty and `mat-icon` logs `Error retrieving icon :<name>!` to the console; there is no UI/platform validation that blocks it. Use an exact registered name (prefer the _Icon values_ `ButtonIcon` set above); there are no short aliases (`"save"` is invalid — use `"save-button-icon"`).
7. **`disabled` bound to a non-existent attribute** — `"$Missing"` evaluates to `undefined`, which is falsy, so the button stays enabled. The platform does **not** warn about missing attributes.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Button"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `caption` set (or `iconPosition: "only-icon"` + `icon` for an icon-only button).
- [ ] `clicked.request` wired to either a platform request or a handler defined in `handlers`.
- [ ] `color` is one of the seven supported literals.
- [ ] `layoutConfig` provided (numbers; `-1` for stretch).
- [ ] If `menuItems` is set, each item has `type: "crt.MenuItem"` and its own `clicked.request`.
