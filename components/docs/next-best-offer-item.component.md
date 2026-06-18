# How to Add a Next Best Offer Item (`crt.NextBestOfferItem`) to a Freedom UI Page

> Audience: code agent inserting a `crt.NextBestOfferItem` into a Creatio Freedom UI page schema.
>
> `crt.NextBestOfferItem` is the individual tile rendered inside a `crt.NextBestOffer` carousel/gallery.
> It is created automatically from the `items` collection — you do not insert it directly via `viewConfigDiff`.
> It displays a product image, name, score badge, and info label based on `itemConfig.templateValuesMapping`.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.NextBestOffer` (internal, created automatically from `items`)
- **Typical children**: none

---

## 1. Mental model

`crt.NextBestOfferItem` is an internal tile component. You never insert it as a standalone `viewConfigDiff`
operation. Configure its appearance via `crt.NextBestOffer.itemConfig.templateValuesMapping`.

The registry exposes three inputs (`isSelected`, `record`, `tileSizeClasses`) that are all set by the parent
`crt.NextBestOffer` at runtime — do not set them manually.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.NextBestOfferItem` are in `ComponentRegistry.json` under `componentType: "crt.NextBestOfferItem"`.

---

## 5. Copy-paste minimal example

`crt.NextBestOfferItem` has no standalone `viewConfigDiff` entry. Configure the parent instead:

```jsonc
// viewConfigDiff — configure the parent; item tiles are created automatically
{
  "operation": "insert",
  "name": "NextBestOffer",
  "values": {
    "type": "crt.NextBestOffer",
    "items": "$NextBestOffer",
    "itemConfig": {
      "templateValuesMapping": {
        "caption": "NextBestOfferDS_Name",
        "image": "NextBestOfferDS_ProductPicture",
        "id": "NextBestOfferDS_Id",
        "numberTag": "NextBestOfferDS_Score"
      }
    }
  },
  "parentName": "NBOContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Trying to insert `crt.NextBestOfferItem` as a standalone view element.** It is an internal component; only the parent `crt.NextBestOffer` should reference it.
2. **Expecting to configure tile layout via `viewConfigDiff`.** All tile display settings are controlled through `crt.NextBestOffer.itemConfig.templateValuesMapping`.
