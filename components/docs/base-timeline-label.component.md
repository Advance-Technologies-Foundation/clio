# How to Add a Base Timeline Label (`crt.BaseTimelineLabel`) to a Freedom UI Page

> Audience: code agent inserting `crt.BaseTimelineLabel` into a Creatio Freedom UI page schema.
> A two-field label pair showing a static title (`caption`) and a dynamic value (`value`); used as a
> metadata label inside timeline tile content.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, timeline tile content slots
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.BaseTimelineLabel"` and `caption`/`value`. **Always present.** |

`crt.BaseTimelineLabel` is **view-only** — no model, no attribute required. Both `caption` and
`value` accept literal strings or `$Attribute` bindings.

### 1.1 Naming convention

```
BaseTimelineLabel_<id>        // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "BaseTimelineLabel_abc123",
  "parentName": "TimelineTileContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.BaseTimelineLabel",
    "caption": "#ResourceString(BaseTimelineLabel_abc123_caption)#",
    "value": "$SomeAttribute"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.BaseTimelineLabel` are in `ComponentRegistry.json` under
`componentType: "crt.BaseTimelineLabel"`. Only two inputs: `caption` and `value`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "DueDateLabel",
  "parentName": "TileMetadataContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.BaseTimelineLabel",
    "caption": "#ResourceString(DueDateLabel_caption)#",
    "value": "$Activity_DueDate"
  }
}
```

---

## 7. Common pitfalls

1. **Both `caption` and `value` are plain strings** — neither is auto-localized; use `#ResourceString(<key>)#` macros for the caption if it needs translation.
2. **No outputs** — this component fires no events; do not wire `handlers` for it.
3. **Primarily used inside timeline tiles** — it can technically appear anywhere, but its styling is designed for timeline content slots.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.BaseTimelineLabel"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `caption` set (use `#ResourceString(<key>)#` for localizable text).
- [ ] `value` set to a literal or `$Attribute` binding for the displayed data.
