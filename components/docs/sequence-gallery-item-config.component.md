# How to Add a Sequence Gallery Item Config (`crt.SequenceGalleryItemConfig`) to a Freedom UI Page

> Audience: code agent inserting `crt.SequenceGalleryItemConfig` into a Creatio Freedom UI page schema.
>
> A `crt.SequenceGalleryItemConfig` is a gallery item renderer for sequence-related records with title, time, contact, and type styling.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: gallery or dynamic collection item renderer host
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | item renderer registry/config | Register or reference this renderer for records that should render as `crt.SequenceGalleryItemConfig`. |

`crt.SequenceGalleryItemConfig` is normally used as an item renderer, not as a standalone page element. It inherits
gallery item inputs such as `record`, `isSelected`, and `tileSizeClasses`.

### 1.1 Naming convention

```text
SequenceGalleryItemConfig_<id> // renderer/view element name when a host requires one
```

---

## 2. Step-by-step recipe

### 2.1 Register the renderer with the collection host

Use this component where the host expects a view-element type for gallery item rendering.

```jsonc
{
  "type": "crt.SequenceGalleryItemConfig",
  "record": "$SequenceGalleryItem",
  "isSelected": "$SequenceGalleryItem_IsSelected"
}
```

### 2.2 Provide record fields expected by the renderer

The renderer reads these record keys:

```text
title
trailingText
contactName
typeCode
```

`trailingText` may be a lookup value, a date string, or plain text. `typeCode` controls the call/email/task icon
style and falls back to task styling when missing.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.SequenceGalleryItemConfig` are in `ComponentRegistry.json` under
`componentType: "crt.SequenceGalleryItemConfig"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

The inherited `record` input is a gallery item data object. The runtime component reads values by key with
`getRecordValue`.

```ts
interface SequenceGalleryRecord {
  title?: string;
  trailingText?: string | { displayValue?: string; value?: string; primaryColorValue?: string };
  contactName?: string;
  typeCode?: string;
}
```

---

## 5. Copy-paste minimal example

No PackageStore page schema currently contains `crt.SequenceGalleryItemConfig`, so this example uses the runtime
item-renderer contract only.

```jsonc
// renderer config consumed by a gallery/dynamic collection host
{
  "type": "crt.SequenceGalleryItemConfig",
  "record": "$SequenceItem",
  "isSelected": "$SequenceItem_IsSelected",
  "tileSizeClasses": "$SequenceItem_SizeClasses"
}
```

---

## 6. Driving from page state

Drive the renderer through the host collection record. Populate `title`, `trailingText`, `contactName`, and
`typeCode` in each record before it reaches the item renderer.

---

## 7. Common pitfalls

1. **Adding it directly to a normal container.** It is intended for item-renderer hosts that provide a `record`.
2. **Omitting `record`.** Without a record, title, trailing text, contact name, and type icon values render empty.
3. **Using unexpected record keys.** The component reads fixed keys and does not map aliases.
4. **Passing an invalid date in `trailingText`.** Invalid date strings render as-is instead of being time-formatted.
5. **Expecting custom type colors.** Only known type codes have custom gradients; unknown values use task styling.

---

## 8. Quick checklist

- [ ] Renderer host references `type: "crt.SequenceGalleryItemConfig"`.
- [ ] `record` is supplied by the collection host.
- [ ] Records include `title`, `trailingText`, `contactName`, and `typeCode` where available.
- [ ] `isSelected` and `tileSizeClasses` are passed only when the host supports them.
- [ ] Do not add child items; this component has no slots.
