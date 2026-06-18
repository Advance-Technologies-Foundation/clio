# How to Add a File Input (`crt.FileInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FileInput` into a Creatio Freedom UI page schema.
>
> A `crt.FileInput` is a form field bound to a datasource attribute of type Blob, File, Image,
> or ImageLookup. It lets the user upload, preview, download, and delete a single file. Like
> all input components it requires a `control` binding that links it to a datasource column.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FileInput"` and a `control` binding. **Always present.** |
| 2 | `modelConfigDiff` | The datasource that exposes the Blob/File column (only if not already present). |
| 3 | `handlers` (optional) | Request handlers for `upload`, `download`, `delete`, `cancelUpload`, `preview` outputs. |

### 1.1 Naming convention

```
FileInput_<id>        // view element name; <id> is any short unique slug
FileInput_<id>DS      // datasource key when modelConfigDiff is touched
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FileInput_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FileInput",
    "label": "#ResourceString(FileInput_abc123_label)#",
    "control": "$ds_PDS.FileColumn",
    "accept": ".pdf,.docx",
    "maxFileSize": 10,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Handle file operations in `handlers`

```jsonc
{
  "request": "crt.FileUploadRequest",
  "handler": async (request, next) => {
    // request.detail.file contains the FileUploadDto
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FileInput` are in `ComponentRegistry.json` under `componentType: "crt.FileInput"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`accept` is a standard HTML `accept` attribute string (comma-separated MIME types or extensions,
e.g. `".pdf,.docx"` or `"image/*"`).

`maxFileSize` is a number in **megabytes**. The component converts it to bytes internally
(`maxFileSize × 1024 × 1024`) and shows a localized error notification when the limit is exceeded.

`upload` emits a `CustomEvent` with `detail.file` of type `FileUploadDto` (`{ id, progress$, ...}`).
`download` emits a `CustomEvent` with `detail.file` of type `FileValue`.
`delete` and `cancelUpload` emit void.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FileInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FileInput",
    "label": "#ResourceString(FileInput_xkp4r_label)#",
    "control": "$ds_PDS.Attachment",
    "readonly": false,
    "placeholder": "#ResourceString(FileInput_xkp4r_placeholder)#",
    "labelPosition": "auto",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 6. Driving from page state

`disabled` and `readonly` are `propertyBindable` — bind to page attributes for runtime control.
`control` accepts a `$ds_<name>.<column>` binding to a datasource attribute.

```jsonc
"disabled": "$FileInput_disabled",
"control": "$ds_PDS.FileColumn"
```

---

## 7. Common pitfalls

1. **`control` pointing to a non-file column** — the component is designed for `Blob`, `File`, `Image`, and `ImageLookup` data value types; binding to a text column will produce unexpected behavior.
2. **`maxFileSize` as bytes instead of MB** — the value is in megabytes; passing `10485760` instead of `10` sets the limit to 10 TB.
3. **Not wiring `upload`** — the component emits the file DTO on upload but does not persist it; a handler must call the file service to store the file.
4. **`accept` as MIME type only** — some browsers enforce MIME; use both extension and MIME (e.g. `".pdf,application/pdf"`) for reliable filtering across browsers.
5. **`delete` not handled** — the component clears the control value locally but does not call the server; wire `delete` to remove the file from storage.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FileInput"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `control` bound to a datasource attribute of Blob/File/Image/ImageLookup type.
- [ ] `label` set (or empty string `""` if intentionally labelless).
- [ ] `upload` wired to a handler that persists the file.
- [ ] `delete` wired to a handler that removes the file from storage.
- [ ] `maxFileSize` is in megabytes (not bytes).
- [ ] `accept` uses file extensions or MIME types that match the expected file formats.
