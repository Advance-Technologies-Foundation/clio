# How to Add a Flex Container (`crt.FlexContainer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FlexContainer` into a Creatio Freedom UI page schema.
>
> A `crt.FlexContainer` is a **layout-only** view element: it holds a child collection in `items` and arranges
> them with CSS flexbox (`direction`, `gap`, `wrap`, `justifyContent`, `alignItems`). It owns no datasource and
> no attributes — adding one only requires a single `viewConfigDiff` insert op (plus optionally an `innerScroll`
> request binding).

## Metadata

- **Category**: containers
- **Container**: yes (children go into the `items` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`, `crt.TabContainer`, `crt.ExpansionPanel`, root page container
- **Typical children**: any view element — fields, buttons, labels, other `crt.FlexContainer` / `crt.GridContainer` (nesting is the most common layout pattern)

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FlexContainer"`, starting flex values, and an empty `items: []`. **Always present.** |
| 2 | `handlers` (optional) | A request handler for `innerScroll` — only if you wired `innerScroll: { request: '...' }` and need custom logic. |

`crt.FlexContainer` is **view-only** — no model, no attribute. Every child you nest inside it is a separate
`insert` op whose `parentName` points back to this container. The flex container itself only describes
**how** those children flow.

### 1.1 How diff operations work

`viewConfigDiff` is an **array of diff operations**. For inserting a new view element:

- `operation: "insert"` — appends a new view element into a parent's slot.
- `parentName` — the `name` of the container that owns the slot.
- `propertyName` — the slot name on the parent (almost always `"items"`).
- `index` — position in the slot (0-based).
- `name` — unique identifier across the whole page (used by children's `parentName`).
- `values` — the runtime configuration of the new element.

(See `JsonDiffOperation` in `libs/studio-enterprise/util/low-code/src/lib/models/json-diff-operation.ts`.)

### 1.2 Naming convention

```
FlexContainer_<id>          // view element name; <id> is any short unique slug
```

Children reference this name via their own `parentName` field.

---

## 2. Step-by-step recipe

### 2.1 Insert the flex container (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FlexContainer_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FlexContainer",
    "direction": "row",
    "justifyContent": "start",
    "alignItems": "stretch",
    "gap": "small",
    "wrap": "wrap",
    "color": "transparent",
    "borderRadius": "none",
    "padding": { "top": "none", "right": "none", "bottom": "none", "left": "none" },
    "items": [],
    "fitContent": true,
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

The outer `layoutConfig` only matters when this flex container itself sits **inside a `crt.GridContainer`** —
it tells the parent grid which cell to place the flex container in. Drop `layoutConfig` if the parent is a
`crt.FlexContainer` (flex parents do not consume grid coordinates).

### 2.2 Position each child via its own `layoutConfig`

A `crt.FlexContainer` does **not** rely on row/column coordinates for its children. Instead each child may
carry a flex-shaped `layoutConfig`:

```jsonc
// child's layoutConfig (any of these fields are optional)
{
  "basis": "fit-content",         // or a CSS length string ("120px", "20%"); maps to flex-basis
  "alignSelf": "center",          // 'flex-start' | 'flex-end' | 'center' | 'stretch' | 'baseline'
  "grow": 1,                      // flex-grow integer
  "shrink": 0,                    // flex-shrink integer
  "width": 200,                   // px — when set, also fixes min-width
  "height": 40,                   // px — when set, also fixes min-height
  "minWidth": 64, "maxWidth": 320,
  "minHeight": 24, "maxHeight": 160
}
```

If a child should simply fill the row in `direction: "row"`, set `grow: 1`. If you want the child to keep its
intrinsic width, omit `grow`/`shrink`/`basis` altogether — flex defaults take over.

### 2.3 (Optional) Wire `innerScroll`

```jsonc
"innerScroll": { "request": "crt.MyScrollRequest" }
```

The container fires the request whenever the user scrolls inside it (when content overflows). Useful for
infinite scrolling. Without a `request`, scrolling is silent.

```jsonc
// matching handlers entry
{
  "request": "crt.MyScrollRequest",
  "handler": async (request, next) => {
    // your logic here
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FlexContainer` are in `ComponentRegistry.json` under `componentType: "crt.FlexContainer"`. This guide
covers only the assembly mechanics — what to put where in `viewConfigDiff` / `viewModelConfigDiff` /
`modelConfigDiff`.

---

## 4. Shape of types not expanded in the registry

The registry generator does not expand the following shared types. Use these literal shapes when filling
property values:

```ts
// padding — see base-container.component.ts
type ContainerPaddingType =
  | { top?: SizeEnum | string | number;
      right?: SizeEnum | string | number;
      bottom?: SizeEnum | string | number;
      left?: SizeEnum | string | number; }
  | SizeEnum     // 'none' | 'xs' | 'small' | 'medium' | 'large' | 'xl' | 'xxl'
  | string       // CSS length
  | number;      // px

// innerScroll request payload — see request-binding-config.type.ts
interface RequestBindingConfig {
  request: string;                    // e.g. 'crt.MyScrollRequest'
  params?: RequestParamsBindingConfig;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}

// items collection — the only legal element shape inside `items`
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;
```

`gap` accepts a `SizeEnum` keyword (`'none' | 'xs' | 'small' | 'medium' | 'large' | 'xl' | 'xxl'`) or a raw
pixel `number`. Keywords are the canonical choice in real PackageStore schemas.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ToolsFlexContainer",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FlexContainer",
    "direction": "row",
    "gap": "none",
    "items": [],
    "visible": true,
    "color": "transparent",
    "borderRadius": "none",
    "padding": { "top": "none", "right": "none", "bottom": "none", "left": "none" },
    "justifyContent": "start",
    "alignItems": "center",
    "wrap": "wrap"
  }
}
```

```jsonc
// viewConfigDiff entry — a child of the flex container (note layoutConfig uses flex fields)
{
  "operation": "insert",
  "name": "PrimaryButton",
  "parentName": "ToolsFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "caption": "#ResourceString(PrimaryButton_Caption)#",
    "color": "primary",
    "clicked": { "request": "crt.SaveRecordRequest" },
    "layoutConfig": { "alignSelf": "center", "grow": 0 }
  }
}
```

---

## 6. Driving the container from page state

```jsonc
// viewModelConfigDiff — declare an attribute that holds the visibility flag
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ToolsFlexContainer_visible": { "value": true }
    }
  }
}

// viewConfigDiff.values — bind via $-prefix
"visible": "$ToolsFlexContainer_visible"
```

`visible` is a platform-level wrapper property and accepts `$Attribute` bindings on any container. The
container-specific inputs (`direction`, `gap`, `wrap`, ...) are not flagged as `propertyBindable` in the
registry — bind via `visible` for toggle-style runtime control, or compute the layout statically in the
schema diff.

---

## 7. Common pitfalls

1. **Forgetting `items: []`** — `crt.FlexContainer` requires the `items` array even when starting empty; without it the runtime falls back to `<ng-content>` slot projection and your subsequent child inserts have nowhere to go.
2. **Mixing layout systems** — using `{ row, column, rowSpan, colSpan }` on a flex container's child has no effect (those fields are read only by a `crt.GridContainer` parent). For flex children, set `grow`/`shrink`/`basis`/`alignSelf` instead.
3. **`direction: "column"` + `wrap: "wrap"`** — children wrap into new columns, which is rarely what you want. With vertical flex, prefer `wrap: "nowrap"` (the runtime auto-selects `nowrap` when `wrap` is unset and `direction` is `column`/`column-reverse`).
4. **Padding as a bare number/string** — both work (`5` → `5px`, `"small"` → `var(--gap-small)`) but mixing styles inside a page makes diffs noisy; pick the per-side object form (`{ top, right, bottom, left }`) for consistency with the rest of the schema.
5. **`gap` as a number** — accepted but converted to a pixel value; prefer the `SizeEnum` keyword so theme spacing tokens still apply.
6. **`innerScroll` without `request`** — the output fires silently; either remove the binding entirely or attach a real request.
7. **Setting `role`** — only set the ARIA `role` when this container plays a semantic role for screen readers (e.g. `"toolbar"`, `"navigation"`); a wrong role degrades accessibility more than no role.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FlexContainer"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `items: []` present in `values` (even when starting empty).
- [ ] `direction` chosen (`"row"` or `"column"` are the realistic options; reverse variants are exotic).
- [ ] `gap` set as a `SizeEnum` keyword (`'small'`/`'medium'`/`'large'`) to stay theme-consistent.
- [ ] `padding` provided as a per-side object (or omitted to inherit defaults).
- [ ] If this flex container sits inside a `crt.GridContainer`, it has its own `layoutConfig: { row, column, rowSpan, colSpan }`.
- [ ] Each child's `layoutConfig` uses **flex fields** (`grow`, `shrink`, `basis`, `alignSelf`, `width`/`height`), **not** `row`/`column`.
- [ ] If `innerScroll` is wired, the matching `handlers` entry exists or it points to a platform request.
