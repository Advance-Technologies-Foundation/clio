# How to Add a File Gallery Item (`crt.FileGalleryItem`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FileGalleryItem` into a Creatio Freedom UI page schema.
>
> A `crt.FileGalleryItem` renders a single file card (thumbnail, name, size, date) inside a
> gallery layout. It is not inserted directly into a page schema — it is used as the item
> component inside a `crt.FileList` gallery configured via `GalleryItemConfig.componentClassFactory`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: rendered by a parent `crt.FileList` gallery; not inserted directly into a page schema
- **Typical children**: none

---

## 1. Mental model

`crt.FileGalleryItem` is an **internal component** managed by `crt.FileList`. It is not added
via `viewConfigDiff` — the parent gallery instantiates it programmatically using a
`componentClassFactory`.

All configurable inputs (`isSelected`, `record`, `tileSizeClasses`) are inherited from
`CrtGalleryBaseItemComponent` and are supplied by the parent gallery at runtime.

There are **no leaf-defined `@CrtInput` or `@CrtOutput`** properties on this class.

---

## 2. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FileGalleryItem` are in `ComponentRegistry.json` under `componentType: "crt.FileGalleryItem"`.

---

## 3. Copy-paste minimal example

`crt.FileGalleryItem` is configured indirectly via the parent `crt.FileList`:

```jsonc
// Inside the crt.FileList values
"galleryItemConfig": {
  "componentClassFactory": "CrtFileListGalleryItemComponent",
  "itemKeyProperty": "fileId",
  "defaultSize": "small",
  "actions": [
    {
      "type": "crt.MenuItem",
      "caption": "#ResourceString(DownloadFile)#",
      "icon": "feed-download-file"
    }
  ]
}
```

---

## 4. Common pitfalls

1. **Trying to insert `crt.FileGalleryItem` directly into `viewConfigDiff`** — this component is not a standalone page element; it must be used as the gallery item factory for a `crt.FileList`.
2. **Expecting `@CrtInput` JSDoc on this class** — all bindable inputs are defined on the base class `CrtGalleryBaseItemComponent`; this leaf adds no new inputs.
