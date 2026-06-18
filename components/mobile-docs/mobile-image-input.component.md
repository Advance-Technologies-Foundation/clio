# How to Add an ImageInput (`crt.ImageInput`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.ImageInput` into a mobile page schema.
> Renders an image upload and preview field for image/file data on a mobile page.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---

## 1. Mental model
`crt.ImageInput` lets the user pick an image from the device gallery or capture one with
the camera. It shows the current image inside a configurable frame controlled by `size`
and `borderRadius`. When the user selects a file the `imageSelected` action fires;
when the user clears the field the `imageClear` action fires. The `positioning` input
controls how the image fits inside the frame (`'cover'` crops to fill, `'scale-down'`
shrinks to fit without cropping).

---

## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "PhotoInput",
  "values": {
    "type": "crt.ImageInput",
    "label": "$Resources.Strings.PhotoInput_label",
    "control": "$PDS_Photo",
    "size": "large",
    "borderRadius": "medium",
    "positioning": "fill"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.ImageInput` are in
`ComponentRegistry.json` under `componentType: "crt.ImageInput"`.

Additional runtime properties (not Angular `@Input` — applied via schema binding):

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding. Bind to a boolean expression, e.g. `'$CardState \| crt.IsEqual : \'edit\''`, to show/hide the component conditionally. |

---

## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "PhotoInput",
  "values": {
    "type": "crt.ImageInput",
    "label": "$Resources.Strings.PhotoInput_label",
    "control": "$PDS_Photo",
    "size": "large",
    "borderRadius": "medium",
    "positioning": "fill"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls
- **`size` vs. `customWidth`/`customHeight`** — when both `customWidth` and `customHeight` are set, the `size` preset is ignored; use one approach or the other, not both.
- **`positioning` value** — valid values are `'cover'` (crop to fill) and `'scale-down'` (shrink to fit); the designer accepts `'fill'` as an alias but the canonical runtime value is `'cover'`.
- **`maxFileSize`** — if omitted the platform falls back to the `MaxImageSize` system setting; set it explicitly when the column has a smaller limit to avoid upload failures that are hard to diagnose at runtime.
