# How to Add an Allowed Results (`crt.AllowedResults`) to a Freedom UI Page

> Audience: code agent inserting `crt.AllowedResults` into a Creatio Freedom UI page schema.
> Renders a list of selectable activity-result options (positive/negative/neutral) filtered by the
> current activity category; writes the chosen result back through a `FormControl` binding.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.AllowedResults"` and three control bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for `activityResultControl`, `allowedResults`, `activityCategory`. |

`crt.AllowedResults` has no create command that touches `modelConfigDiff` or `handlers` — only
`viewConfigDiff` is required. The toolbar item is gated by the feature flag `ShowDesignerDemoItems`.

### 1.1 Naming convention
```
AllowedResults_<id>    // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Declare attributes in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Result": {
      "modelConfig": { "path": "ActivityDS.Result" }
    },
    "AllowedResult": {
      "modelConfig": { "path": "ActivityDS.AllowedResult" }
    },
    "ActivityCategory": {
      "modelConfig": { "path": "ActivityDS.ActivityCategory" }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "AllowedResults_abc",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "layoutConfig": { "column": 1, "row": 2, "colSpan": 1, "rowSpan": 1 },
    "type": "crt.AllowedResults",
    "activityResultControl": "$Result",
    "allowedResults": "$AllowedResult",
    "activityCategory": "$ActivityCategory",
    "label": "$Resources.Strings.Result"
  }
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.AllowedResults` are in `ComponentRegistry.json` under `componentType: "crt.AllowedResults"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`activityResultControl` accepts an Angular `FormControl` instance. The platform creates the `FormControl`
from the attribute bound to `activityResultControl`; the component reads its validator to determine
whether the field is required.

`allowedResults` is a **JSON-serialized string** — the attribute value must be a string like
`'["guid1","guid2"]'`, not a JS array. The component parses it internally with `JSON.parse`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real PackageStore usage (CrtActivityMiniPage)
{
  "operation": "insert",
  "name": "AllowedResults",
  "values": {
    "layoutConfig": {
      "column": 1,
      "row": 2,
      "colSpan": 1,
      "rowSpan": 1
    },
    "type": "crt.AllowedResults",
    "activityResultControl": "$Result",
    "allowedResults": "$AllowedResult",
    "activityCategory": "$ActivityCategory",
    "label": "$Resources.Strings.Result"
  },
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 1
}
```

---

## 7. Common pitfalls

1. **Passing a JS array to `allowedResults`** — the input expects a JSON-stringified string (`'["guid1","guid2"]'`). Passing a raw array causes `JSON.parse` to fail silently and all results are shown.
2. **Setting `activityCategory` before the entity is loaded** — the setter triggers a live ESQ query against `ActivityResult`; if called with an empty/null value it is silently ignored, but stale results may remain if the attribute value changes late.
3. **Not binding `activityResultControl`** — without it the component cannot write the selected result back and the required-field validation never fires.
4. **Feature flag `ShowDesignerDemoItems`** — the component's designer toolbar item is hidden unless this flag is enabled. The runtime component works without the flag; it is safe to use programmatically.
5. **Using `AllowedResults` without an `ActivityCategory` binding** — the result list is empty until a category is provided; show a placeholder or handle the empty state in the page.
6. **Not providing `label`** — the heading above the results list stays blank. Use a resource string for localisation.
7. **Result selection replaces the whole `FormControl` value** — the component calls `setValue(item)` on the control, where `item` is an `AllowedResult` object; downstream handlers should expect an object, not a plain guid string.
8. **Keyboard navigation depends on rendered items** — the `FocusKeyManager` is initialized after `resultsList` is populated; if the DOM updates asynchronously, the first item may not receive focus automatically.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AllowedResults"`, unique `name`, valid `parentName`.
- [ ] `activityResultControl` bound to the `FormControl` attribute for the result column.
- [ ] `allowedResults` bound to a JSON-string attribute (not a JS array).
- [ ] `activityCategory` bound to the category attribute so the result list is populated.
- [ ] `label` set (use `#ResourceString(...)#` for localised text).
- [ ] `layoutConfig` provided when parent is a `crt.GridContainer`.
