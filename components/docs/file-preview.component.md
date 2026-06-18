# How to Add a File Preview (`crt.FilePreview`) to a Freedom UI Page

> Audience: code agent inserting `crt.FilePreview` into a Creatio Freedom UI page schema.
>
> A `crt.FilePreview` renders a document viewer for a bound file value and can synchronize viewer options such as zoom.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FilePreview"` and file/options bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes that provide the bound `file` and optional `options` values. |
| 3 | `handlers` (optional) | A request handler only if `optionsChange` needs custom logic. |

`crt.FilePreview` is view-only: it owns no datasource and does not create attributes by itself.

### 1.1 Naming convention

```text
FilePreview_<id>                 // view element name; <id> = any short unique slug
FilePreview_<id>_File            // file attribute
FilePreview_<id>_Options         // viewer options attribute, only when options are state-driven
```

---

## 2. Step-by-step recipe

### 2.1 Add page attributes for the file and options

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "FilePreview_Main_File": {
      "modelConfig": {
        "path": "PageParameters.FilePreviewComponent_File"
      }
    },
    "FilePreview_Main_Options": {
      "modelConfig": {
        "path": "PageParameters.FilePreviewComponent_Options"
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FilePreview_Main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FilePreview",
    "file": "$FilePreview_Main_File",
    "options": "$FilePreview_Main_Options"
  }
}
```

### 2.3 (Optional) Handle option changes

```jsonc
"optionsChange": { "request": "crt.FilePreviewOptionsChangedRequest", "params": { "options": "@event" } }
```

Add a matching handler only when the page needs to persist or react to viewer option changes.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FilePreview` are in `ComponentRegistry.json` under `componentType: "crt.FilePreview"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`FileValue` and `FileViewerOptions` are expanded in the registry. The fields most commonly used by the viewer are:

```ts
interface FileValue {
  name: string;
  displayValue: string;
  value: string;
  url?: string;
  extension?: string;
  size?: number;
}

interface FileViewerOptions {
  zoom?: number;
}
```

---

## 5. Copy-paste minimal example

This mirrors the real `FilePreviewPage` schema from PackageStore.

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FilePreviewComponent",
  "values": {
    "type": "crt.FilePreview",
    "file": "$FilePreviewComponent_File_Parameter",
    "options": "$FilePreviewComponent_Options_Parameter"
  },
  "parentName": "FilePreviewFrameContainer",
  "propertyName": "items",
  "index": 0
}
```

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "FilePreviewComponent_File_Parameter": {
      "modelConfig": {
        "path": "PageParameters.FilePreviewComponent_File"
      }
    },
    "FilePreviewComponent_Options_Parameter": {
      "modelConfig": {
        "path": "PageParameters.FilePreviewComponent_Options"
      }
    }
  }
}
```

---

## 6. Driving from page state

Bind `file` to a page parameter, record attribute, or handler-populated attribute that resolves to a `FileValue`.
Bind `options` when the page must remember viewer state such as zoom.

---

## 7. Common pitfalls

1. **Passing an incomplete file value.** The viewer needs a usable file identity and URL/name data to load the document.
2. **Forgetting the `$` prefix on bindings.** Without `$`, `file` receives a literal string instead of the attribute value.
3. **Expecting a datasource to be created.** `crt.FilePreview` consumes a file value; it does not load records by itself.
4. **Treating `optionsChange` as automatic persistence.** Wire a request handler if option changes must be saved.
5. **Initializing before the viewer container exists.** Runtime retries internally, but schema code should still provide stable bindings.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FilePreview"`.
- [ ] `file` is bound to an attribute that provides a valid file value.
- [ ] `options` is bound only when viewer state must be controlled by page state.
- [ ] Optional `optionsChange.request` has a matching handler when used.
- [ ] Parent container name, `propertyName`, and `index` point to an existing slot.
