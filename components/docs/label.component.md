# How to Add a Label (`crt.Label`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Label` into a Creatio Freedom UI page schema.
>
> `crt.Label` renders static or attribute-bound text. It is the building block for
> headings, captions, hints, and any read-only text on the page.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`, `crt.ExpansionPanel`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Label"`. |
| 2 | `viewModelConfigDiff` (optional) | A page attribute if `caption` should be dynamic (`"$attr"`). |

For a fully static caption, only `viewConfigDiff` is required.

### 1.1 Naming convention

```
Label_<id>           // view element name; <id> is any short unique slug
Label_<id>_text      // optional attribute when caption is dynamic
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Label_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Label",
    "caption": "#ResourceString(Label_xkp4r_Caption)#",
    "labelType": "headline-1",
    "labelThickness": "default",
    "labelEllipsis": false,
    "labelColor": "auto",
    "labelBackgroundColor": "transparent",
    "labelTextAlign": "start",
    "headingLevel": "label",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Label` are in `ComponentRegistry.json` under `componentType: "crt.Label"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — section heading
{
  "operation": "insert",
  "name": "SectionHeading",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Label",
    "caption": "#ResourceString(SectionHeading_Caption)#",
    "labelType": "headline-2",
    "labelThickness": "bold",
    "labelTextAlign": "start",
    "headingLevel": "h2",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 1 }
  }
}
```

---

## 5. Common pitfalls

1. **Mixing `labelType` (visual) with `headingLevel` (semantic)** — they are independent. `labelType: "headline-1"` + `headingLevel: "label"` looks like a heading but is a `<label>` element, which screen readers announce differently.
2. **Empty caption** — when `caption` is `""` or `null`, the label collapses to zero height (no skeleton). The component does not warn about empty content. Bind to `"$attr"` only if the attribute is initialized.
3. **Raw CSS overrides fighting the preset** — setting both `labelType: "headline-1"` and `labelFontSize: "10px"` works (raw value wins) but breaks theme switching. Prefer `labelType` whenever possible.
4. **`required: true` without a sibling input** — the `*` glyph appears with no semantic meaning. Use `required` only when the label visually labels a real input.
5. **`labelEllipsis: true` without a fixed width** — the label has no edge to ellipsize against. Combine with a `colSpan` or `width` in `labelStyle`.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Label"`, unique `name`, valid `parentName`, `propertyName: "items"`, `index`.
- [ ] `caption` set (resource string or `$attr`).
- [ ] `labelType` and `headingLevel` agreed upon (e.g. heading text → `headline-X` + `h1`-`h4`; plain text → `body` + `label`).
- [ ] `layoutConfig` provided.
