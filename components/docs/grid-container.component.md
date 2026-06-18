# How to Add a Grid Container (`crt.GridContainer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.GridContainer` into a Creatio Freedom UI page schema.
>
> A `crt.GridContainer` is a **layout-only** view element: it holds a child collection in `items` and places
> them on a CSS Grid (columns/rows track-list + per-child row/column placement). It owns no datasource and no
> attributes — adding one only requires a single `viewConfigDiff` insert op.

## Metadata

- **Category**: containers
- **Container**: yes (children go into the `items` slot)
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.HeaderContainer`, `crt.TabContainer`, `crt.ExpansionPanel`, root page container
- **Typical children**: any view element — fields, buttons, labels, other `crt.GridContainer` / `crt.FlexContainer` (nesting is the most common layout pattern)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.GridContainer"`, a `columns` track-list, an optional `rows` track-list, a `gap`, and an empty `items: []`. **Always present.** |

`crt.GridContainer` is **view-only** — no model, no attribute. Each child is a separate `viewConfigDiff`
`insert` op whose `parentName` points back to this container. The grid itself only describes the **track**
geometry; the children carry their own grid-cell coordinates via `layoutConfig`.

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
GridContainer_<id>          // view element name; <id> is any short unique slug
```

Children reference this name via their own `parentName` field.

---

## 2. Step-by-step recipe

### 2.1 Insert the grid container (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "GridContainer_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.GridContainer",
    "columns": [
      "minmax(64px, 1fr)",
      "minmax(64px, 1fr)"
    ],
    "rows": "minmax(max-content, 32px)",
    "gap": { "columnGap": "large", "rowGap": "none" },
    "alignItems": "stretch",
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

`columns` is the **column track-list**. The array length defines how many columns the grid has; each entry
is a CSS grid track sizing function (`"1fr"`, `"100px"`, `"minmax(64px, 1fr)"`, `"max-content"`). The
canonical PackageStore value is `"minmax(64px, 1fr)"` — it gives each column a 64-px floor and equal flexible
width above that.

`rows` may be a single CSS sizing function (applied as `grid-auto-rows`, so rows are generated on demand —
this is the common case) or an array of explicit track sizes (applied as `grid-template-rows`).

> **Default to `"rows": "minmax(max-content, 32px)"`** when adding a grid container. `rows` is technically
> optional, but an empty grid needs a row sizing that reserves height — otherwise it collapses and no child can
> be dropped into it afterwards. `minmax(max-content, 32px)` is the canonical default and keeps an empty grid
> usable as a drop target; do not reverse it to `"minmax(32px, max-content)"`. Override it only when the user
> explicitly specifies a row height.

### 2.2 Position each child via its own `layoutConfig`

A `crt.GridContainer` requires every child to declare **where** it lives on the grid. The child shape is:

```jsonc
// child's layoutConfig (in viewConfigDiff.values.layoutConfig)
{
  "row": 1,        // 1-based row index
  "column": 1,     // 1-based column index
  "rowSpan": 1,    // how many rows the child covers (>= 1; use -1 to span to the end of the row track)
  "colSpan": 1     // how many columns the child covers (>= 1; use -1 to span to the end of the column track)
}
```

`row`/`column` are 1-based (CSS Grid line indices). `rowSpan`/`colSpan` are 1-based span counts. A child
without a `layoutConfig` falls into the first free cell, which is rarely deterministic — set `layoutConfig`
explicitly for every grid child.

### 2.3 (Optional) Allow overlapping children

Set `allowOverlap: true` on the container if two children must occupy the same cell (rare — usually one of
them is an absolutely-positioned overlay). With `allowOverlap: false` (default), the runtime nudges
overlapping children apart automatically, which silently mutates `layoutConfig`.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.GridContainer` are in `ComponentRegistry.json` under `componentType: "crt.GridContainer"`. This guide
covers only the assembly mechanics — what to put where in `viewConfigDiff` / `viewModelConfigDiff` /
`modelConfigDiff`.

The `ContainerGapConfig` shape is already expanded in the registry's `references.typeDefinitions` and is not
repeated here.

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

// items collection — the only legal element shape inside `items`
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;

// child's layoutConfig — required for every grid child
interface GridLayoutConfig {
  row: number;        // 1-based
  column: number;     // 1-based
  rowSpan: number;    // span count; -1 means "span to end"
  colSpan: number;    // span count; -1 means "span to end"
}
```

`gap` accepts a `SizeEnum` keyword, a raw pixel `number`, a CSS string, OR the per-axis object
`{ columnGap, rowGap }` — the per-axis form is the canonical choice in real PackageStore schemas because grid
gutters usually differ between rows and columns.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — the grid itself
{
  "operation": "insert",
  "name": "GeneralInfoGrid",
  "parentName": "GeneralInfoTab",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.GridContainer",
    "rows": "minmax(max-content, 32px)",
    "columns": [
      "minmax(64px, 1fr)",
      "minmax(64px, 1fr)"
    ],
    "gap": { "columnGap": "large", "rowGap": "none" },
    "items": [],
    "visible": true,
    "color": "transparent",
    "borderRadius": "none",
    "padding": { "top": "none", "right": "none", "bottom": "none", "left": "none" }
  }
}
```

```jsonc
// viewConfigDiff entry — a child placed in row 1, column 1
{
  "operation": "insert",
  "name": "TypeField",
  "parentName": "GeneralInfoGrid",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ComboBox",
    "label": "#ResourceString(TypeField_Label)#",
    "control": "$Type",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}

// viewConfigDiff entry — a child that spans both columns
{
  "operation": "insert",
  "name": "NotesField",
  "parentName": "GeneralInfoGrid",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.Input",
    "label": "#ResourceString(NotesField_Label)#",
    "control": "$Notes",
    "layoutConfig": { "column": 1, "row": 2, "colSpan": 2, "rowSpan": 1 }
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
      "GeneralInfoGrid_visible": { "value": true }
    }
  }
}

// viewConfigDiff.values — bind via $-prefix
"visible": "$GeneralInfoGrid_visible"
```

`visible` is a platform-level wrapper property and accepts `$Attribute` bindings on any container. The
container-specific inputs (`columns`, `rows`, `gap`, ...) are not flagged as `propertyBindable` in the
registry — bind via `visible` for toggle-style runtime control, or compute the layout statically in the
schema diff.

---

## 7. Common pitfalls

1. **Child without `layoutConfig`** — the runtime drops it into the first free cell, which is non-deterministic and tends to clobber layout. Always set `{ row, column, rowSpan, colSpan }` on every child.
2. **Off-by-one on `row`/`column`** — these are **1-based** CSS Grid line indices, not 0-based array indices. The first cell is `{ row: 1, column: 1 }`.
3. **`colSpan` exceeds the column track count** — the child stretches past the rightmost column, leaving a phantom column. Match `colSpan` to the actual `columns` array length, or use `colSpan: -1` to mean "span to the end".
4. **Forgetting `items: []`** — `crt.GridContainer` requires the `items` array even when starting empty; without it the runtime falls back to `<ng-content>` projection and child inserts have nowhere to land.
5. **`rows` as an array vs. string** — array form (`["100px", "1fr"]`) defines a fixed `grid-template-rows`; string form (`"minmax(max-content, 32px)"`) applies as `grid-auto-rows` and generates rows on demand. The string form is almost always what you want.
6. **Mixing layout systems** — using `{ basis, alignSelf, grow, shrink }` on a grid child has no effect (those fields are read only by a `crt.FlexContainer` parent). For grid children, only `{ row, column, rowSpan, colSpan }` is honored.
7. **`gap` as a flat `SizeEnum` keyword** — accepted but applies the same value to both axes; for differing column/row gutters use the `{ columnGap, rowGap }` object form.
8. **`allowOverlap: true` without explicit `layoutConfig`s** — overlap is permitted only if the cells you specify actually overlap; otherwise behavior is identical to the default and the flag is noise.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.GridContainer"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `items: []` present in `values` (even when starting empty).
- [ ] `columns` declared as an array of CSS track sizing functions; the array length matches the highest `column + colSpan - 1` used by any child.
- [ ] `rows` set to `"minmax(max-content, 32px)"` (keeps an empty grid droppable; override only when the user specifies a row height).
- [ ] `gap` set as `{ columnGap, rowGap }` (each is a `SizeEnum` keyword) to stay theme-consistent.
- [ ] `padding` provided as a per-side object (or omitted to inherit defaults).
- [ ] If this grid container sits inside another `crt.GridContainer`, it has its own outer `layoutConfig: { row, column, rowSpan, colSpan }`.
- [ ] Every child carries `layoutConfig: { row, column, rowSpan, colSpan }` with 1-based indices.
