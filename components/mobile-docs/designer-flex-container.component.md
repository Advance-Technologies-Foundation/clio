# How to Add a Flex Container (`crt.FlexContainer`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.FlexContainer` into a mobile page schema.
>
> Flexbox layout container for linear horizontal or vertical arrangement of child items.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: Scaffold items, GridContainer items, TabContainer items
- **Typical children**: any view elements

---

## 1. Mental model

`crt.FlexContainer` wraps its children in a CSS flexbox row or column. Use it when you need
a single-axis linear layout — for example, a row of action buttons or a vertical stack of fields.
It is shared between the Freedom UI desktop and mobile page designers.

---

## 2. Clio operation

```jsonc
{
  "operation": "insert",
  "name": "ActionsRow",
  "values": {
    "type": "crt.FlexContainer",
    "direction": "row",
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.FlexContainer` are in
`ComponentRegistry.json` under `componentType: "crt.FlexContainer"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Key inputs:

| Property | Type | Values | Default |
|---|---|---|---|
| `direction` | string | `row`, `column` | `column` |
| `justifyContent` | string | `start`, `end`, `center`, `spaceBetween`, `spaceAround`, `spaceEvenly` | `start` |
| `alignItems` | string | `start`, `end`, `center`, `stretch` | `stretch` |
| `wrap` | string | `noWrap`, `wrap` | `noWrap` |
| `gap` | number | pixels | `0` |
| `items` | array | child view element configs | `[]` |
| `padding` | object | `{ top, right, bottom, left }` | — |
| `scrollable` | boolean | enable scroll overflow | `false` |
| `fitContent` | boolean | Fit the container height/width to its content instead of stretching to fill the parent. | — |
| `color` | string | Background color token or hex value for the container. | — |
| `borderRadius` | object | Border radius config for the container. | — |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "FieldsColumn",
  "values": {
    "type": "crt.FlexContainer",
    "direction": "column",
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

- **`direction` default is `column`**: omitting `direction` gives a vertical stack. Set
  `"direction": "row"` explicitly when you want a horizontal arrangement.
- **`gap` is a pixel number, not a string**: use `8`, not `"8px"`.
- **Child `layoutConfig`**: unlike `crt.GridContainer`, FlexContainer children do not use
  `row`/`column` layout configs — their order is determined by array position in `items`.
