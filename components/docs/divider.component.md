# How to Add a Divider (`crt.Divider`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Divider` into a Creatio Freedom UI page schema.
>
> `crt.Divider` is a view-only separator line used to visually split content. It owns no datasource and no
> attributes, so adding one only requires a `viewConfigDiff` insert op.
>
> At runtime the divider **auto-orients perpendicular to the parent flex flow**: it becomes a full-width
> horizontal line inside a column container and a full-height vertical line inside a row container, never
> pushing its siblings. The `orientation` input is therefore an initial/seed value inside flex layouts and
> is honoured directly only when there is no flex ancestor (e.g. a grid cell or plain block parent).

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Divider"` and starting visual values. **Always present.** |

`crt.Divider` is **view-only**. The designtime create command seeds `orientation` from the parent flex
direction (horizontal for column-like parents, vertical for row-like parents); at runtime the component
re-derives and keeps the effective orientation perpendicular to the parent flow, so the line stays visible
even if the parent direction changes later.

### 1.1 Naming convention

```
Divider_<id>        // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Divider_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Divider",
    "orientation": "horizontal",
    "thickness": 1,
    "padding": { "top": "small", "right": "none", "bottom": "small", "left": "none" },
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

Drop `layoutConfig` when the parent is a flex container; keep it when the divider is placed directly in a
`crt.GridContainer`.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Divider` are in `ComponentRegistry.json` under `componentType: "crt.Divider"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// padding — see containerPaddingConverter usage in divider.component.ts
type ContainerPadding =
  | { top?: SizeEnum | string | number;
      right?: SizeEnum | string | number;
      bottom?: SizeEnum | string | number;
      left?: SizeEnum | string | number; }
  | SizeEnum
  | string
  | number;
```

`thickness` accepts the runtime-supported values `1`, `2`, or `4`. A string such as `"2px"` is normalized to
the matching number; unsupported values fall back to `1`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — mirrors AddDividerCommand defaults when PackageStore has no direct page example
{
  "operation": "insert",
  "name": "Divider_ContentSplit",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Divider",
    "orientation": "horizontal",
    "thickness": 1,
    "padding": { "top": "small", "right": "none", "bottom": "small", "left": "none" }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Divider_ContentSplit_visible": { "value": true }
  }
}

// viewConfigDiff.values
"visible": "$Divider_ContentSplit_visible"
```

`visible` is a platform-level wrapper property and accepts `$Attribute` bindings on any view element.

---

## 7. Common pitfalls

1. **Fighting the auto-orientation inside a flex parent.** In a flex container the rendered direction follows
   the parent flow regardless of the `orientation` value, so setting `orientation: "horizontal"` in a row (or
   `"vertical"` in a column) has no visible effect. Set the parent's `direction`, not the divider, to control
   the line. `orientation` only takes hold outside flex (grid cell / block parent).
2. **Passing unsupported `thickness`.** Only `1`, `2`, and `4` are preserved; other values fall back to `1`.
3. **Mixing padding shapes.** A bare token works, but the per-side object is easier to review in page diffs.
4. **Keeping grid `layoutConfig` under a flex parent.** Flex parents ignore row/column coordinates; remove the
   grid layout block there.
5. **Expecting click behavior.** The divider has no outputs; wire actions to adjacent buttons or containers.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Divider"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `orientation` seeds the initial direction; inside a flex parent the runtime auto-orients it
      perpendicular to the parent flow (it only matters directly in a grid cell / block parent).
- [ ] `thickness` is `1`, `2`, or `4`.
- [ ] `padding` is set only when visual spacing is needed.
- [ ] `layoutConfig` is included only for grid placement.
