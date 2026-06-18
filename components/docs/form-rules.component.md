# How to Add Form Rules (`crt.FormRules`) to a Freedom UI Page

> Audience: code agent inserting `crt.FormRules` into a Creatio Freedom UI page schema.
>
> A `crt.FormRules` renders a form-rule editor that consumes available form fields and emits serialized rule metadata.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 2-4 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FormRules"`, `fields`, `rulesMetadata`, and `rulesMetadataChange`. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for the available fields collection and serialized rules metadata. |
| 3 | `handlers` | A request handler that receives `rulesMetadataChange` and stores the new metadata. |
| 4 | `modelConfigDiff` | Only when the metadata or fields attributes are backed by a datasource. |

`crt.FormRules` edits serialized metadata; it does not create field metadata or save rules by itself.

### 1.1 Naming convention

```text
FormRules_<id>                 // view element name; <id> = any short unique slug
FormRules_<id>_Fields          // available fields collection attribute
FormRules_<id>_RulesMetadata   // serialized rules metadata attribute
```

---

## 2. Step-by-step recipe

### 2.1 Add attributes for fields and rule metadata

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "FormRules_Main_Fields": {},
    "FormRules_Main_RulesMetadata": {
      "value": ""
    }
  }
}
```

Use a real model path instead of `value` when metadata is stored in a record column.

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FormRules_Main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FormRules",
    "fields": "$FormRules_Main_Fields",
    "rulesMetadata": "$FormRules_Main_RulesMetadata",
    "rulesMetadataChange": {
      "request": "crt.FormRulesMetadataChangedRequest",
      "params": {
        "rulesMetadata": "@event"
      }
    }
  }
}
```

### 2.3 Add a handler for metadata changes

```jsonc
{
  "request": "crt.FormRulesMetadataChangedRequest",
  "handler": async (request, next) => {
    request.$context.FormRules_Main_RulesMetadata = request.rulesMetadata;
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FormRules` are in `ComponentRegistry.json` under `componentType: "crt.FormRules"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`fields` is a `BaseViewModelCollection` of form field view-model records. `rulesMetadata` is a serialized JSON
string consumed by the form-rule converter.

---

## 5. Copy-paste minimal example

No PackageStore page schema currently contains `crt.FormRules`, so this example uses the runtime contract only.

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FormRules_Main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FormRules",
    "fields": "$FormFields",
    "rulesMetadata": "$RulesMetadata",
    "rulesMetadataChange": { "request": "crt.FormRulesMetadataChangedRequest", "params": { "rulesMetadata": "@event" } }
  }
}
```

---

## 6. Driving from page state

Bind `fields` to the same field collection used by the surrounding form editor. Bind `rulesMetadata` to a string
attribute so the editor can parse existing rules and emit the updated serialized value.

---

## 7. Common pitfalls

1. **Passing raw field configs instead of a collection.** `fields` is consumed as a `BaseViewModelCollection`.
2. **Leaving `rulesMetadataChange` unwired.** Edits remain local unless a request stores the emitted metadata.
3. **Using invalid serialized metadata.** Empty or invalid values reset the rule list.
4. **Binding metadata without `$`.** The component receives the attribute name as text instead of the serialized value.
5. **Expecting automatic persistence.** The component emits changes; the page handler decides how to save them.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FormRules"`.
- [ ] `fields` points to a valid form field collection attribute.
- [ ] `rulesMetadata` points to a string attribute.
- [ ] `rulesMetadataChange.request` is wired to a handler when edits must be retained.
- [ ] Any datasource-backed metadata attribute has the required `modelConfigDiff` entry.
