# How to Add an HTML Code Editor (`crt.HtmlCodeEditor`) to a Freedom UI Page

> Audience: code agent inserting `crt.HtmlCodeEditor` into a Creatio Freedom UI page schema.
>
> A `crt.HtmlCodeEditor` wraps the code editor control for editing or viewing an HTML markup string.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.HtmlCodeEditor"` and an `htmlCode` binding. **Always present.** |
| 2 | `viewModelConfigDiff` | A string attribute for the edited HTML value. |
| 3 | `handlers` (optional) | A request handler when `htmlCodeChange` should update or save page state. |

`crt.HtmlCodeEditor` is value-oriented: it displays `htmlCode`, respects `readOnly`, and emits `htmlCodeChange`.

### 1.1 Naming convention

```text
HtmlCodeEditor_<id>       // view element name; <id> = any short unique slug
HtmlCodeEditor_<id>_Code  // HTML string attribute
```

---

## 2. Step-by-step recipe

### 2.1 Add an HTML string attribute

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "HtmlCodeEditor_Main_Code": {
      "value": ""
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "HtmlCodeEditor_Main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.HtmlCodeEditor",
    "htmlCode": "$HtmlCodeEditor_Main_Code",
    "readOnly": false,
    "htmlCodeChange": {
      "request": "crt.HtmlCodeChangedRequest",
      "params": {
        "htmlCode": "@event"
      }
    }
  }
}
```

### 2.3 (Optional) Update page state from changes

```jsonc
{
  "request": "crt.HtmlCodeChangedRequest",
  "handler": async (request, next) => {
    request.$context.HtmlCodeEditor_Main_Code = request.htmlCode;
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.HtmlCodeEditor` are in `ComponentRegistry.json` under `componentType: "crt.HtmlCodeEditor"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

No additional shared shapes are required. `htmlCode` is a string, `readOnly` is a boolean, and `htmlCodeChange`
emits the changed string value.

---

## 5. Copy-paste minimal example

No PackageStore page schema currently contains `crt.HtmlCodeEditor`, so this example uses the runtime contract only.

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "HtmlCodeEditor_Main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.HtmlCodeEditor",
    "htmlCode": "$HtmlCode",
    "readOnly": false,
    "htmlCodeChange": { "request": "crt.HtmlCodeChangedRequest", "params": { "htmlCode": "@event" } }
  }
}
```

---

## 6. Driving from page state

Bind `htmlCode` to a string attribute and set `readOnly` from a boolean attribute when editability depends on page
state.

---

## 7. Common pitfalls

1. **Treating `htmlCode` as sanitized output.** This component edits markup text; rendering or sanitization happens elsewhere.
2. **Leaving change output unwired.** The editor updates its own value, but page state changes only through a handler.
3. **Using `readOnly` for visibility.** `readOnly` blocks editing; use standard `visible` binding to hide the editor.
4. **Binding without `$`.** The editor receives a literal attribute name instead of the HTML value.
5. **Expecting formatting.** The component passes text through; it does not normalize or prettify markup.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.HtmlCodeEditor"`.
- [ ] `htmlCode` is bound to a string attribute or supplied as a literal string.
- [ ] `readOnly` is set only when editing should be blocked.
- [ ] `htmlCodeChange.request` is wired when the page must store changes.
- [ ] Parent container and slot coordinates are valid.
