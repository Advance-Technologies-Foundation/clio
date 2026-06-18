# How to Use a Chat Attachment Tile (`crt.ChatAttachmentTile`) in a Freedom UI Page

> Audience: code agent working with Creatio Freedom UI page schemas.
>
> `crt.ChatAttachmentTile` is a **sub-component** rendered automatically by `crt.ChatComposer`
> for each file queued for upload. It is not inserted directly into `viewConfigDiff` — the parent
> composer manages its lifecycle. You interact with it indirectly through the `filesToUpload`
> input of `crt.ChatComposer`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: managed by `crt.ChatComposer` internally — not placed directly in `viewConfigDiff`
- **Typical children**: none

---

## 1. Mental model — 0 places you edit directly

`crt.ChatAttachmentTile` has no `viewConfigDiff` insert op. The tile is rendered for each item in
the `crt.ChatComposer` attachment queue. To control what tiles appear, drive the `filesToUpload`
input of the parent `crt.ChatComposer`:

```jsonc
// viewConfigDiff — ChatComposer (the entry point for attachment tiles)
{
  "operation": "insert",
  "name": "ChatComposer_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatComposer",
    "filesToUpload": "$ChatComposer_abc123_FilesToUpload"
  }
}
```

---

## 2. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for
`crt.ChatAttachmentTile` are in `ComponentRegistry.json` under
`componentType: "crt.ChatAttachmentTile"`. The registry exposes:

| Input | Type | Notes |
|---|---|---|
| `record` | `TRecord` | File data object passed from the parent gallery. |
| `isSelected` | `boolean` | Selection state managed by parent gallery. |
| `tileSizeClasses` | `string[]` | Style classes controlling tile dimensions; provided by parent. |

All three inputs are owned by the base class (`CrtGalleryBaseItemComponent`) and set by the
parent gallery/composer — they are not configurable in `viewConfigDiff`.

---

## 3. Common pitfalls

1. **Do not insert `crt.ChatAttachmentTile` in `viewConfigDiff` directly** — it is not a
   standalone insertable element; the parent composer renders it for each upload slot.
2. **`record` is a `File` object** — the tile casts `record` to `File` internally; passing a
   plain data object will result in empty file name and size.

---

## 4. Quick checklist

- [ ] To show attachment tiles, add a `crt.ChatComposer` with `filesToUpload` wired.
- [ ] Do **not** add a `viewConfigDiff` insert op for `crt.ChatAttachmentTile`.
