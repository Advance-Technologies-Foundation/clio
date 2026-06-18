# How to Add a Tag Select (`crt.TagSelect`) to a Freedom UI Page

> Audience: code agent inserting `crt.TagSelect` into a Creatio Freedom UI page schema.
> A `crt.TagSelect` is a chip-list component that displays and manages tag associations for a record. It uses a designer-side preprocessor (`crt.TagSelectPropertiesPanel`) — real Freedom UI pages only need `recordId` in the schema; the full `@CrtInput` surface is configured through the preprocessor.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TagSelect"` and `recordId: "$Id"`. |

`crt.TagSelect` relies on the `crt.TagSelectPropertiesPanel` designer preprocessor to wire up `items`, `listItems`, event handlers, and datasource bindings. In real PackageStore schemas only `recordId` appears directly in the schema diff — the rest is managed by the preprocessor.

### 1.1 Naming convention
```
TagSelect          // view element name (conventional; matches PackageStore usage)
$Id                // recordId binding — the primary key attribute of the current record
```

---

## 2. Step-by-step recipe

### 2.1 Insert the `crt.TagSelect` view element

```jsonc
{
  "operation": "insert",
  "name": "TagSelect",
  "parentName": "CardToolsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TagSelect",
    "recordId": "$Id"
  }
}
```

This minimal form is the canonical recipe from real Freedom UI page templates. Additional properties (`label`, `items`, `listItems`, `disabled`, event outputs) are populated by the preprocessor at design time; you can also set them explicitly when building pages programmatically.

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TagSelect` are in `ComponentRegistry.json` under `componentType: "crt.TagSelect"`. This guide covers
only the assembly mechanics.

---

## 5. Copy-paste minimal example

Real usage from `PageWithTabsFreedomTemplate` (PackageStore):
```jsonc
{
  "operation": "insert",
  "name": "TagSelect",
  "values": {
    "type": "crt.TagSelect",
    "recordId": "$Id"
  },
  "parentName": "CardToolsContainer",
  "propertyName": "items",
  "index": 0
}
```

Full programmatic example with explicit inputs:
```jsonc
{
  "operation": "insert",
  "name": "TagSelect",
  "parentName": "CardToolsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TagSelect",
    "recordId": "$Id",
    "label": "#ResourceString(TagSelect_Label)#",
    "disabled": false,
    "items": "$Tags",
    "listItems": "$AvailableTags",
    "tagInRecordSourceSchemaName": "TagInRecord"
  }
}
```

---

## 6. Driving from page state

`disabled` is `propertyBindable` and accepts a `$Attribute` binding:
```jsonc
"disabled": "$TagSelect_disabled"
```

---

## 7. Common pitfalls

- **Omitting `recordId`.** Without `recordId` the preprocessor cannot resolve the tag-to-record association; the component renders empty.
- **Manually wiring `items`/`listItems` without understanding the preprocessor.** If you bypass the preprocessor and wire these directly, you must also handle all CRUD event outputs (`createTag`, `editTag`, `deleteTag`, `addTagsInRecord`, `deleteTagInRecord`) yourself.
- **Not providing a `tagInRecordSourceSchemaName`.** Defaults to `"TagInRecord"`. Override only when your module uses a custom junction schema.
- **`label` as a raw string.** Use `#ResourceString(<key>)#` for any visible label text to support localization.
- **Placing outside `CardToolsContainer`.** Canonical placement is in the card tools area; placing in a detail container may cause layout issues.
- **`wrap: true` without enough horizontal space.** When `wrap` is enabled and many tags are selected, the chip list grows vertically and can push adjacent elements down.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.TagSelect"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `recordId: "$Id"` present in `values`.
- [ ] `tagInRecordSourceSchemaName` matches the actual junction entity (default: `"TagInRecord"`).
- [ ] If `label` is set, use `#ResourceString(...)#` for localization.
- [ ] Event outputs (`createTag`, `editTag`, etc.) have matching handlers when bypassing the preprocessor.
