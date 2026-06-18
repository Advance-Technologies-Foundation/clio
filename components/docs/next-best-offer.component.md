# How to Add a Next Best Offer (`crt.NextBestOffer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.NextBestOffer` into a Creatio Freedom UI page schema.
>
> `crt.NextBestOffer` is a carousel or gallery that displays a horizontally scrollable collection of
> offer/recommendation tiles. Each tile renders data from a `BaseViewModelCollection` via the `itemConfig`
> template mapping. It is typically placed on a contact/account record page.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`
- **Typical children**: none — tiles are rendered internally from `items` via `itemConfig`

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.NextBestOffer"`, `items`, and `itemConfig`. **Always present.** |
| 2 | `viewModelConfigDiff` | Attribute for `items` (the collection) when bound to a `$attribute`. |

`crt.NextBestOffer` has `@CrtInterfaceDesignerItem` with a `copy` command but no `create` command — there
is no standard page-wizard create flow; insert it manually.

### 1.1 Naming convention

```
NextBestOffer_<id>            // view element name
$NextBestOffer_<id>           // attribute holding the BaseViewModelCollection
NextBestOffer_<id>DS          // datasource key (if using modelConfigDiff)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "NextBestOffer",
  "values": {
    "type": "crt.NextBestOffer",
    "visible": true,
    "layoutConfig": {
      "column": 1,
      "row": 1,
      "colSpan": 1,
      "rowSpan": 1
    },
    "items": "$NextBestOffer",
    "itemConfig": {
      "templateValuesMapping": {
        "caption": "NextBestOfferDS_Name",
        "description": "NextBestOfferDS_ShortDescription",
        "image": "NextBestOfferDS_ProductPicture",
        "id": "NextBestOfferDS_Id",
        "numberTag": "NextBestOfferDS_Score",
        "infoLabel": "NextBestOfferDS_Type"
      }
    }
  },
  "parentName": "NBOContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.NextBestOffer` are in `ComponentRegistry.json` under `componentType: "crt.NextBestOffer"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// BaseImageListItemConfig — the itemConfig shape
interface BaseImageListItemConfig {
  templateValuesMapping?: {
    caption?: string;        // datasource column for the tile title
    description?: string;    // datasource column for the tile subtitle
    image?: string;          // datasource column for the tile image
    id?: string;             // datasource column for the item identifier
    numberTag?: string;      // datasource column for the score/percentage badge
    infoLabel?: string;      // datasource column for an info label
    [key: string]: string | undefined;
  };
  componentClassFactory?: () => typeof Component;   // custom tile component (optional)
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real Contacts_FormPage usage
{
  "operation": "insert",
  "name": "NextBestOffer",
  "values": {
    "layoutConfig": {
      "column": 1,
      "row": 1,
      "colSpan": 1,
      "rowSpan": 1
    },
    "type": "crt.NextBestOffer",
    "visible": true,
    "itemConfig": {
      "templateValuesMapping": {
        "caption": "NextBestOfferDS_Name",
        "description": "NextBestOfferDS_ShortDescription",
        "image": "NextBestOfferDS_ProductPicture",
        "id": "NextBestOfferDS_Id",
        "numberTag": "NextBestOfferDS_Score",
        "infoLabel": "NextBestOfferDS_Type"
      }
    },
    "items": "$NextBestOffer"
  },
  "parentName": "NBOContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 6. Driving from page state

`items` is `propertyBindable`-style — bind to a `$attribute` that holds the `BaseViewModelCollection`.
`selectedItem` can be two-way-bound with `selectedItemChange` if selection state needs to be tracked:

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "NextBestOffer": { "value": [] },
    "NextBestOffer_Selected": { "value": null }
  }
}

// viewConfigDiff.values
"items": "$NextBestOffer",
"selectedItem": "$NextBestOffer_Selected",
"selectedItemChange": {
  "request": "crt.NextBestOfferSelectionRequest",
  "params": { "item": "@event" }
}
```

---

## 7. Common pitfalls

1. **`templateValuesMapping` keys not matching datasource column names.** Tile fields (caption, image, etc.) render empty when the column name in the mapping does not exactly match a column in the datasource.
2. **`viewMode` omitted.** Without `viewMode`, the component defaults to carousel layout; pass `"galleryView"` explicitly for a grid layout.
3. **`selectable: false` (default) with `selectedItemChange` wired.** Selection output fires only when `selectable: true`; otherwise clicks do nothing.
4. **`hideScrollButtons: true` (default) in a narrow container.** If the container is too narrow to show all items and scroll buttons are hidden, items are unreachable without keyboard navigation.
5. **`loop: true` with a single item.** The carousel wraps around immediately, which can be confusing; guard against this in the data handler.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.NextBestOffer"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `items` bound to a `$attribute` holding a `BaseViewModelCollection`.
- [ ] `itemConfig.templateValuesMapping` keys match actual datasource column names.
- [ ] `layoutConfig` provided when the parent is a `crt.GridContainer`.
- [ ] If selection is needed, `selectable: true` and `selectedItemChange.request` both set.
- [ ] `viewMode` set explicitly (`"carouselView"` or `"galleryView"`).
