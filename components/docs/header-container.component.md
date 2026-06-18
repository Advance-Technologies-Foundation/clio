# How to Add a Header Container (`crt.HeaderContainer`) to a Freedom UI Page

> **Deprecated.** `crt.HeaderContainer` is marked `@deprecated`. New pages should use `crt.FlexContainer`
> or `crt.GridContainer` for the top-row area instead.

> Audience: code agent inserting a `crt.HeaderContainer` into a Creatio Freedom UI page schema.
>
> `crt.HeaderContainer` is a top-level container that renders a page header bar. It inherits the full
> `crt.FlexContainer`/`crt.GridContainer` base-container inputs and adds a `title` text field.

## Metadata

- **Category**: containers (deprecated)
- **Container**: yes (children go into the `items` slot)
- **Parent types**: root page container (positioned as the first item at the top of the page)
- **Typical children**: `crt.GridContainer`, `crt.FlexContainer`, buttons

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.HeaderContainer"` and starting values. **Always present.** |

`crt.HeaderContainer` is **view-only** — no model, no attribute. Adding one only requires a single
`viewConfigDiff` insert op.

### 1.1 Naming convention

```
MainHeader                 // canonical name in platform templates
HeaderContainer_<id>       // alternative for custom pages; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MainHeader",
  "values": {
    "type": "crt.HeaderContainer",
    "color": "primary",
    "padding": {
      "right": "large",
      "left": "large"
    },
    "fitContent": true,
    "items": []
  },
  "index": 0
}
```

Children are placed as separate `insert` ops that reference this container's `name` in their `parentName` field.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.HeaderContainer` are in `ComponentRegistry.json` under `componentType: "crt.HeaderContainer"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// padding — inherited from base-container.component.ts
type ContainerPaddingType =
  | { top?: SizeEnum | string | number;
      right?: SizeEnum | string | number;
      bottom?: SizeEnum | string | number;
      left?: SizeEnum | string | number; }
  | SizeEnum     // 'none' | 'xs' | 'small' | 'medium' | 'large' | 'xl' | 'xxl'
  | string
  | number;

// items collection
type ContainerViewElements = Array<{ type: string; name?: string; /* + element-specific props */ }>;
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — standard page header
{
  "operation": "insert",
  "name": "MainHeader",
  "values": {
    "type": "crt.HeaderContainer",
    "color": "primary",
    "padding": { "right": "large", "left": "large" },
    "fitContent": true,
    "items": []
  },
  "index": 0
}
```

```jsonc
// child: action grid inside the header
{
  "operation": "insert",
  "name": "ActionContainer",
  "values": {
    "type": "crt.GridContainer",
    "rows": "minmax(max-content, 32px)",
    "columns": ["minmax(64px, 1fr)"],
    "color": "primary",
    "gap": "small",
    "items": []
  },
  "parentName": "MainHeader",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Using `crt.HeaderContainer` in new pages.** The component is deprecated — use `crt.FlexContainer` or `crt.GridContainer` for the header area in new schemas. Only use `crt.HeaderContainer` when modifying existing pages that already include it.
2. **Forgetting `items: []`.** Like all containers, `crt.HeaderContainer` requires the `items` array even when starting empty.
3. **Setting `title` on a header expected to contain rich content.** The `title` input renders a simple text string inside the header; for complex header layouts, place content elements directly in `items` instead.
4. **Missing `color: "primary"`.** In existing platform schemas the header uses `color: "primary"` to apply the themed header background; omitting it leaves a transparent background that may not match the page design.
5. **Using `parentName` + `propertyName` incorrectly.** If this container sits at the root page level (no explicit parent), omit `parentName`/`propertyName` from the insert op; the `index` field alone controls its position.

---

## 8. Quick checklist

- [ ] **Prefer `crt.FlexContainer` or `crt.GridContainer`** for new pages — `crt.HeaderContainer` is deprecated.
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.HeaderContainer"`, unique `name`, and a valid `index`.
- [ ] `items: []` present in `values` (even when starting empty).
- [ ] `color` set (typically `"primary"` to match platform header theme).
- [ ] `padding` provided as a per-side object or size keyword.
- [ ] Each child has its `parentName` pointing to this container's `name`.
