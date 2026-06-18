# How to Add a Label (`crt.Label`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Label` into a mobile page schema.
>
> A static text label for headings, captions, and inline explanatory copy.
> Controls typography preset, weight, and color independently.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: none

---

## 1. Mental model

`crt.Label` is a purely visual, non-interactive text element. The three most
important style properties are independent axes:

- `labelType` — typography preset (scale + weight family): `headline-1`, `body`, `caption`, etc.
- `labelThickness` — weight override on top of the preset: `light`, `normal`, `semibold`, `bold`.
- `labelColor` — CSS color or theme token (e.g. `"auto"`, `"primary-color-1"`).

For localizable text use `#ResourceString(<key>)#` in `caption`.

---

## 2. Clio operation

```jsonc
{
  "operation": "insert",
  "name": "TitleLabel",
  "values": {
    "type": "crt.Label",
    "caption": "Title"
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Label` are in
`ComponentRegistry.json` under `componentType: "crt.Label"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

---

## 4. Copy-paste minimal example

```jsonc
// Insert a styled heading label
{
  "operation": "insert",
  "name": "PageHeading",
  "values": {
    "type": "crt.Label",
    "caption": "$Resources.Strings.PageTitle",
    "labelType": "headline-1",
    "labelThickness": "bold"
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

**`labelType` alone does not control weight.** The preset has an intrinsic weight family;
use `labelThickness` to override it independently.

**`"default"` thickness keeps the preset weight.** Passing `labelThickness: "default"` is
equivalent to omitting the property — it does not apply any particular weight.

**`caption` must be a string.** Numeric values like `0` are coerced to `"0"`; all other
falsy values (`null`, `undefined`, `""`) render as empty.
