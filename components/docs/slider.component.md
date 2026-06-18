# How to Add a Slider (`crt.Slider`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Slider` into a Creatio Freedom UI page schema.
>
> A `crt.Slider` is a numeric range input that binds a `FormControl` to a horizontal drag handle; it
> supports a `minValue`/`maxValue`/`step` range, a palette color or custom hex color, and emits
> `valueChanged` when the user drags the handle.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Slider"` and `control`, `minValue`, `maxValue`, `step`, `label`. **Always present.** |
| 2 | `viewModelConfigDiff` | A bound attribute for the numeric value (`path: "PDS.<Column>"`). |
| 3 | `handlers` | *(optional)* Handler for `valueChanged` if custom logic is needed on change. |

`crt.Slider` is a **form control element**: it requires a `FormControl` binding to hold the current value.
The slider does not create a datasource — the attribute is wired through an existing page datasource.

### 1.1 Naming convention

```
Slider_<id>          // view element name; <id> = any short unique slug
$NumberAttribute_<id> // $-prefix attribute bound to a PDS numeric column
```

---

## 2. Step-by-step recipe

### 2.1 Declare the attribute in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "NumberAttribute_abc123": {
      "modelConfig": { "path": "PDS.NumberColumn" }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Slider_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Slider",
    "color": "primary",
    "value": 0,
    "minValue": 0,
    "maxValue": 100,
    "step": 1,
    "label": "#ResourceString(Slider_abc123_label)#",
    "disabled": false,
    "control": "$NumberAttribute_abc123",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Handler for `valueChanged`

```jsonc
{
  "request": "crt.SliderValueChangedRequest",
  "handler": async (request, next) => {
    // request.parameters.value contains the new numeric value
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Slider` are in `ComponentRegistry.json` under `componentType: "crt.Slider"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// SliderColor palette values (from ComponentRegistry references)
type SliderColor = 'default' | 'white' | 'accent' | 'primary' | 'warn';
// A hex string (e.g. '#3344aa') is also accepted; the component computes a 20%-opacity background.
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — bind to a PDS numeric column
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "NumberAttribute_310oswf": {
      "modelConfig": { "path": "PDS.Score" }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "Slider_310oswf",
  "parentName": "ControlGroupContainer",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.Slider",
    "color": "primary",
    "value": 70,
    "minValue": 0,
    "maxValue": 100,
    "step": 1,
    "label": "$Resources.Strings.NumberAttribute_310oswf",
    "disabled": false,
    "control": "$NumberAttribute_310oswf"
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — attribute for the disabled flag
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Slider_abc123_disabled": { "value": false }
  }
}

// viewConfigDiff.values
"disabled": "$Slider_abc123_disabled"
```

`readonly` is also `propertyBindable` and is an alias for `disabled` — setting either to `true`
prevents the user from dragging the handle.

---

## 7. Common pitfalls

1. **Missing `control` binding.** The slider creates an internal `FormControl` on construction, but without a `control` input the value is never persisted. Always bind `control` to a `$Attribute` that maps to a datasource path.
2. **`color` not a palette keyword or valid hex.** If `color` is not in `SliderColor` and not a hex string, the custom-color path is skipped and the slider renders with no fill color.
3. **`step: 0` or negative `step`.** A zero step causes divide-by-zero in thumb position calculations; use a positive value only.
4. **`minValue > maxValue`.** No runtime guard — the slider renders but the handle cannot move to a valid position.
5. **`value` outside `[minValue, maxValue]`.** The slider clamps the fill to 0% or 100% visually but does not reject the value.
6. **`hideThumb: true` without `paddingLineMode: true`.** When the thumb is hidden, the line becomes a plain fill bar. Set `paddingLineMode: true` to add end-padding so the bar does not bleed to the container edge.
7. **Forgetting `layoutConfig`** when the slider is inside a `crt.GridContainer` parent. Without `{ row, column, rowSpan, colSpan }` the grid cannot place the element.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Slider"`, unique `name`, valid `parentName`.
- [ ] `control` set to a `$AttributeName` that maps to a numeric datasource path.
- [ ] `minValue`, `maxValue`, and `step` are consistent positive numbers (`minValue < maxValue`, `step > 0`).
- [ ] `label` set (or `ariaLabel` for screen-reader-only label).
- [ ] `color` is a `SliderColor` keyword or a valid hex string.
- [ ] `layoutConfig` provided when the parent is a `crt.GridContainer`.
- [ ] `valueChanged` handler present in `handlers` if custom change logic is needed.
