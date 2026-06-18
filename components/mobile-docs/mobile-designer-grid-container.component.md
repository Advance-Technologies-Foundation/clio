# How to Add a Grid Container (`crt.GridContainer`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.GridContainer` into a mobile page schema.
>
> CSS-grid layout container with optional per-breakpoint column overrides for responsive mobile layouts.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: Scaffold items, FlexContainer items, TabContainer items
- **Typical children**: any view elements

---

## 1. Mental model

`crt.GridContainer` places its children on a CSS grid. On mobile pages it also supports the
`adaptive` property, which lets each screen breakpoint (small / medium / large) use a different
column layout. Without `adaptive`, the static `columns` value is used at all breakpoints.

---

## 2. Clio operation

```jsonc
{
  "operation": "insert",
  "name": "DetailsGrid",
  "values": {
    "type": "crt.GridContainer",
    "columns": "1fr",
    "adaptive": {
      "small":  { "columns": "1fr" },
      "medium": { "columns": "1fr 1fr" }
    },
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.GridContainer` are in
`ComponentRegistry.json` under `componentType: "crt.GridContainer"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Key inputs:

| Property | Type | Description |
|---|---|---|
| `columns` | `string \| string[]` | CSS grid-template-columns. Single string `'1fr 1fr'` or array `['1fr','1fr']`. |
| `rows` | `string \| string[]` | CSS grid-template-rows. |
| `gap` | `number \| string \| ContainerGapConfig` | Row and column gap. Object form: `{ row: 8, column: 8 }`. |
| `adaptive` | object | Per-breakpoint column overrides. Keys: `small`, `medium`, `large`. |
| `items` | array | Child view element configs. |
| `padding` | object | Padding config: `{ top, right, bottom, left }`. |
| `color` | string | Background color token or hex. |
| `borderRadius` | object | Border radius config for the container. |
| `justifyItems` | string | CSS `justify-items` for child alignment within grid cells. |
| `alignItems` | string | CSS `align-items` for child alignment within grid cells. |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "InfoGrid",
  "values": {
    "type": "crt.GridContainer",
    "columns": "1fr",
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

- **Missing `adaptive` keys**: if you set `adaptive` but omit one breakpoint (e.g. `large`), the
  designer auto-generates it from the nearest defined breakpoint — but it is safer to declare all
  three explicitly.
- **`columns` vs `adaptive.small.columns`**: on a phone, the `small` breakpoint takes precedence
  over the top-level `columns` value when `adaptive` is present.
- **Child `layoutConfig`**: each item inside `items` must include a `layoutConfig` with `row`,
  `column`, `rowSpan`, and `colSpan` fields that fit within the declared column count.
