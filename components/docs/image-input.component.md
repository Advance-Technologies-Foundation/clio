# How to Add an Image Input (`crt.ImageInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ImageInput` into a Creatio Freedom UI page schema.
>
> `crt.ImageInput` lets the user upload or pick an image and stores the resulting
> reference (URL / file id) into a column. Bind it to a column whose `dataValueType` is
> `IMAGELOOKUP` (image foreign-key reference).

For the underlying contract, see crt.Input guide. This
document highlights only the image-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ImageInput"`, `value: "$<attr>"`. (`crt.ImageInput` uses `valuePropertyName: 'value'`, so the binding goes through `value`, not `control`.) |
| 2 | `viewModelConfigDiff` | A page attribute holding the image reference. |
| 3 | `modelConfigDiff` | Register the IMAGELOOKUP column on the page data source if not already there. |

### 1.1 Naming convention

```
ImageInput_<id>           // view element name
ImageInput_<id>_value     // page attribute (or use the column name, e.g. Contact_Photo)
```

### 1.2 Required vs optional `values` properties

Only `type` and `value` are **required**. Everything else is optional — don't emit a property
just because the contract lists it.

| Property | Status | Notes |
|---|---|---|
| `type` | **Required** | Must be `"crt.ImageInput"`. |
| `value` | **Required** | `"$<attr>"` bound to an `IMAGELOOKUP (16)` column. Use `value`, never `control`. |
| `label`, `alt` | Recommended | Localized label; `alt` for accessibility. |
| `size`, `borderRadius`, `positioning`, `customWidth`, `customHeight`, `customBorderWidth` | Optional | Visual styling. |
| `readonly`, `labelPosition`, `tabIndex`, `disabled`, `visible` | Optional | Behavior / layout. |
| `maxFileSize` | Optional | In **bytes** (e.g. `5 * 1024 * 1024`). |
| `placeholder`, `placeholderMode` | Optional — omit unless a custom empty state is needed | `"abbreviation"` → `placeholder` is an initials seed (e.g. `"$Contact_Name"`). `"icon"` → `placeholder` must be a **registered** icon name (e.g. `"add-image-icon"`); a word like `"image"` is not an icon and throws `Error retrieving icon :<name>!`. |
| `tooltip` | Optional | Hover hint. Pass a **literal string** (e.g. `"Upload a photo of the task owner"`). Do **not** use `#ResourceString(...)#` — no tooltip resource is auto-registered for ImageInput, so a resource key resolves to empty and no tooltip shows. |

Minimal valid insert — only the required properties (add optional ones from the table above only when the design needs them):

```jsonc
{
  "operation": "insert",
  "name": "ImageInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "values": { "type": "crt.ImageInput", "value": "$Contact_Photo" }
}
```

---

## 2. Step-by-step recipe

### 2.1 Declare / bind the page attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Photo": {
        "modelConfig": { "path": "PDS.Photo" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

> The example below is intentionally illustrative — it includes optional styling props
> (`size`, `borderRadius`, `positioning`, …) to show where they go. Keep only what your
> design needs; the only required `values` keys are `type` and `value` (see §1.2).

```jsonc
{
  "operation": "insert",
  "name": "ImageInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ImageInput",
    "label": "#ResourceString(ImageInput_xkp4r_Label)#",
    "value": "$Contact_Photo",
    "readonly": false,
    "labelPosition": "auto",
    "size": "large",
    "borderRadius": "medium",
    "positioning": "cover",
    "tabIndex": 0,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Register the column in `modelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Photo": { "path": "Photo" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ImageInput` are in `ComponentRegistry.json` under `componentType: "crt.ImageInput"`. This guide
covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

**Recommended `dataValueType` of the bound column:** `16` (`IMAGELOOKUP`).

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — avatar image input
{
  "operation": "insert",
  "name": "ContactPhoto",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ImageInput",
    "label": "#ResourceString(ContactPhoto_Label)#",
    "value": "$Contact_Photo",
    "placeholderMode": "abbreviation",
    "size": "large",
    "borderRadius": "xxxl",
    "positioning": "cover",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 2 }
  }
}
```

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Photo": { "modelConfig": { "path": "PDS.Photo" } }
    }
  }
}
```

---

## 5. Common pitfalls

1. **Using `control` instead of `value`** — `crt.ImageInput` is configured with `valuePropertyName: 'value'`. The `control` field is unrecognized; binding goes through `value`.
2. **Setting `customWidth`/`customHeight` together with `size`** — custom dimensions override the preset. If both are set, the preset is silently ignored.
3. **`borderRadius: "xxxl"` with non-square dimensions** — produces an ellipse rather than a circle. For perfect circles use equal `customWidth`/`customHeight` (or a square `size`) plus `"xxxl"`.
4. **Binding to a `Text` column instead of `IMAGELOOKUP (16)`** — the image displays nothing and uploads fail silently because the storage layer expects an image reference, not a URL string.
5. **`maxFileSize` set in MB by mistake** — the property is bytes. `5` means 5 bytes, not 5 MB. Compute as `5 * 1024 * 1024`.
6. **Missing `alt` text** — fails accessibility audits. Provide a meaningful `alt` (often derived from a sibling name field).
7. **`positioning: "cover"` on a tall narrow placeholder** — the image is cropped to fit; if you need the whole image visible (e.g. logos), switch to `"contain"`.
8. **`tooltip` via `#ResourceString(...)#` renders empty** — unlike `label`/`ariaLabel`, no tooltip resource is auto-registered for ImageInput. Set `tooltip` to a literal string (e.g. `"tooltip": "Upload a photo of the task owner"`).

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.ImageInput"`, unique `name`, `propertyName: "items"`.
- [ ] `value: "$<attr>"` (not `control`!) references an attribute bound to an `IMAGELOOKUP (16)` column.
- [ ] `label` (and/or `alt`) provided.
- [ ] `size` / `borderRadius` / `positioning` chosen for the visual design.
- [ ] `placeholder` / `placeholderMode` omitted unless a custom empty state is needed.
- [ ] `maxFileSize` (if set) is in **bytes**.
- [ ] `layoutConfig` provided.
