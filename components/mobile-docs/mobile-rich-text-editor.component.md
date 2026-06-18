# How to Add a Rich Text Editor (`crt.RichTextEditor`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.RichTextEditor` into a mobile page schema.
> Renders a multi-line rich text editor field bound to a model attribute on a mobile record page.

## Metadata
- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.TabPanel` tab body, any layout container
- **Typical children**: none

---
## 1. Mental model
Place `crt.RichTextEditor` wherever a user needs to view or edit richly formatted multi-line text
on a mobile page (e.g. a Notes or Description field). Bind `control` to the PDS attribute that
holds the rich text value. The `label` is shown above the field and can be localised via a
`$Resources.Strings.*` reference.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "NotesEditor",
  "values": {
    "type": "crt.RichTextEditor",
    "label": "$Resources.Strings.NotesEditor_label",
    "control": "$PDS_Notes"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.RichTextEditor` are in
`ComponentRegistry.json` under `componentType: "crt.RichTextEditor"`.

Key inputs:
| Property | Type | Description |
|---|---|---|
| `control` | string (binding) | Data attribute binding for the rich text value (e.g. `$PDS_Notes`). |
| `label` | string | Label text displayed above the editor. |
| `labelPosition` | string | Label placement: `"auto"`, `"top"`, `"left"`, or `"right"`. |
| `readonly` | boolean | When `true`, the field is read-only. |
| `visible` | boolean | Controls visibility of the component. |
| `placeholder` | string | Placeholder text shown when the field is empty. |
| `caption` | string | Alias for `label` used in some schema generators — resource string key for the field caption. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "NotesEditor",
  "values": {
    "type": "crt.RichTextEditor",
    "label": "$Resources.Strings.NotesEditor_label",
    "control": "$PDS_Notes",
    "labelPosition": "auto"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **`control` must be a binding expression** (`$PDS_*`). Passing a plain string will leave the
  field empty with no error.
- **`labelPosition` defaults to `"auto"`**; omitting it is safe, but setting an invalid string
  will silently fall back to the default.
- **Do not nest** `crt.RichTextEditor` inside another field component — place it directly in a
  grid or tab-body container.
