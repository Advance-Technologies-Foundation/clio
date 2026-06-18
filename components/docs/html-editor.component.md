# How to Add an HTML Editor (`crt.HtmlEditor`) to a Freedom UI Page

> Audience: code agent inserting a `crt.HtmlEditor` into a Creatio Freedom UI page schema.
>
> `crt.HtmlEditor` is a CKEditor4-based rich-text / HTML editing field that supports macro insertion,
> email template selection, and optional preview. It is a **FormControl component** — the `control`
> input receives a `FormControl<string>` instance wired to the page attribute through the platform's
> FormControl binding mechanism (`formControlConfig: { relatesTo: 'control' }`).

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.HtmlEditor"` and a `control` binding. **Always present.** |
| 2 | `viewModelConfigDiff` | An attribute (`FormControl<string>`) to hold the HTML value. **Always present.** |
| 3 | `handlers` | Only if `showMacroHelper`, `useTemplates`, or `enablePreview` outputs need custom logic. |

`crt.HtmlEditor` uses the **FormControl pattern**: the view element's `control` property is bound to a
`$`-prefixed viewModel attribute. The platform resolves the `FormControl<string>` instance automatically
from the attribute definition.

### 1.1 Naming convention

```
HtmlEditor_<id>        // view element name
$HtmlEditor_<id>       // $-prefix attribute in viewModelConfigDiff
```

---

## 2. Step-by-step recipe

### 2.1 Add a viewModel attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "HtmlEditor_abc123": {
        "modelConfig": {
          "path": "EmailBody"
        }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "HtmlEditor_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.HtmlEditor",
    "control": "$HtmlEditor_abc123",
    "entitySchemaName": "EmailTemplate",
    "height": 300,
    "readonly": false,
    "placeholder": "#ResourceString(HtmlEditor_abc123_placeholder)#",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Wire macro / template / preview outputs

```jsonc
// viewConfigDiff.values additions
"showMacroHelper": true,
"macroSourceSchemaName": "Contact",
"enablePreview": true,
"previewRequested": { "request": "crt.PreviewHtmlRequest" }
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.HtmlEditor` are in `ComponentRegistry.json` under `componentType: "crt.HtmlEditor"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// FormControl — standard Angular reactive-forms control
// The platform resolves it from the $-prefixed attribute automatically.
type FormControl<T> = import('@angular/forms').FormControl<T>;

// HtmlEditorMacroCategory
interface HtmlEditorMacroCategory {
  caption: string;
  macros: HtmlEditorMacro[];
}

// HtmlEditorMacro
interface HtmlEditorMacro {
  code: string;         // macro template string inserted into editor
  displayName: string;
  description?: string;
  example?: string;
}

// HtmlEditorPreviewRequest payload
interface HtmlEditorPreviewRequest {
  htmlContent: string;
  entitySchemaName: string;
  recordId?: string;    // GUID
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff — declare the HTML-body attribute
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "HtmlEditor_body": {
        "modelConfig": { "path": "EmailBody" }
      }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "HtmlEditor_body",
  "parentName": "ContentContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.HtmlEditor",
    "control": "$HtmlEditor_body",
    "entitySchemaName": "EmailTemplate",
    "height": 400,
    "showMacroHelper": true,
    "macroSourceSchemaName": "Contact",
    "useTemplates": true,
    "showTemplatesButton": true,
    "readonly": false,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
"attributes": {
  "HtmlEditor_body": { "modelConfig": { "path": "EmailBody" } }
}

// viewConfigDiff.values
"control": "$HtmlEditor_body",
"readonly": "$IsReadOnly"
```

`readonly` accepts a `$`-prefixed attribute binding (boolean). `control` must always be a
`$`-prefixed attribute name — it is the FormControl binding that drives the value.

---

## 7. Common pitfalls

1. **`control` not prefixed with `$`.** The FormControl binding requires a `$`-prefixed attribute name (e.g. `"$HtmlEditor_body"`); without the prefix the editor renders with an empty, disconnected control.
2. **`entitySchemaName` missing.** The macro helper and template picker use `entitySchemaName` to resolve available macros and filter templates; leaving it empty disables both features silently.
3. **`macroSourceSchemaName` vs `entitySchemaName`.** Use `macroSourceSchemaName` when the macro data source differs from the page's entity (e.g. contact macros on an email template page); defaults to `entitySchemaName` when blank.
4. **`height` in non-pixel units.** The `height` input is a pixel number; CSS strings (e.g. `"300px"`) are not accepted.
5. **Enabling `enablePreview` without wiring `previewRequested`.** The preview button fires `previewRequested`; without a handler the button appears but does nothing.
6. **`autoResolveMacro: true` on a page without a `recordId`.** Auto-resolve silently no-ops when `recordId` is absent; bind `recordId` to the current record's ID attribute to enable it.
7. **`useTemplates: true` without `entitySchemaName`.** Template lookup requires an entity schema to filter matching templates; without it the picker shows all templates regardless of context.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.HtmlEditor"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `control` is set to a `$`-prefixed attribute name (e.g. `"$HtmlEditor_body"`).
- [ ] Matching attribute entry exists in `viewModelConfigDiff.attributes` with a `modelConfig.path`.
- [ ] `entitySchemaName` provided when macro helper or template selection is enabled.
- [ ] `height` is a pixel number.
- [ ] If `enablePreview` is `true`, `previewRequested` is wired to a handler.
- [ ] `layoutConfig` provided when inside a `crt.GridContainer`.
