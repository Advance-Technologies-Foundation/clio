# How to Add a Template Gallery Item (`crt.TemplateGalleryItem`) to a Freedom UI Page

> Audience: code agent inserting `crt.TemplateGalleryItem` into a Creatio Freedom UI page schema.
> A `crt.TemplateGalleryItem` is the individual tile component rendered by `crt.TemplateGallery` for each collection record. It displays a preview image, title, and description, and exposes a context-menu for per-item actions. It is wired indirectly through the parent gallery's `itemConfig` — you do not insert it with an `insert` op.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: rendered internally by `crt.TemplateGallery` (not directly insertable)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | Configure `crt.TemplateGallery` with an `itemConfig` that maps datasource columns to the tile display slots. `crt.TemplateGalleryItem` is automatically used as the tile renderer. |

`crt.TemplateGalleryItem` is resolved via `CrtTemplateGalleryItemComponent` inside the parent gallery's `_defaultGalleryItemConfig.componentClassFactory`. You do not write an `insert` op for it — only configure the parent `crt.TemplateGallery`.

### 1.1 Configuration convention
```
// In crt.TemplateGallery values:
"itemConfig": {
  "templateValuesMapping": {
    "templateId": "<datasource_column_for_id>",
    "templateName": "<datasource_column_for_name>",
    "templatePreview": "<datasource_column_for_preview_image>",
    "templateDescription": "<datasource_column_for_description>"
  },
  "defaultSize": "large"
}
```

---

## 2. Step-by-step recipe

### 2.1 Configure the parent `crt.TemplateGallery` to use this tile

```jsonc
{
  "operation": "insert",
  "name": "CampaignGallery",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TemplateGallery",
    "items": "$GalleryItems",
    "itemConfig": {
      "templateValuesMapping": {
        "templateId": "CampaignTemplateDS_Id",
        "templateName": "CampaignTemplateDS_Caption",
        "templatePreview": "CampaignTemplateDS_PreviewImage",
        "templateDescription": "CampaignTemplateDS_Description"
      },
      "defaultSize": "large",
      "actions": [
        {
          "type": "crt.MenuItem",
          "caption": "#ResourceString(DeleteTemplateMenuItem)#",
          "icon": "delete-button-icon",
          "clicked": { "request": "crt.DeleteRecordRequest" }
        }
      ]
    },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 8 }
  }
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TemplateGalleryItem` are in `ComponentRegistry.json` under `componentType: "crt.TemplateGalleryItem"`. This guide covers
only the assembly mechanics.

---

## 5. Copy-paste minimal example

Based on `CampaignTemplates_MiniPage` (PackageStore):
```jsonc
// The tile is not inserted directly; configure the parent gallery instead
{
  "operation": "insert",
  "name": "CampaignTemplateGallery",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TemplateGallery",
    "items": "$GalleryItems",
    "selectedItemId": "$SavedTemplates_Gallery_SelectedItemId",
    "itemConfig": {
      "templateValuesMapping": {
        "templatePreview": "CampaignTemplateDS_PreviewImage",
        "templateId": "CampaignTemplateDS_Id",
        "templateName": "CampaignTemplateDS_Caption",
        "templateDescription": "CampaignTemplateDS_Description"
      },
      "defaultSize": "large"
    },
    "selectable": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 8 }
  }
}
```

---

## 7. Common pitfalls

- **Trying to `insert` `crt.TemplateGalleryItem` directly.** It has no `parentName`/`propertyName` slot of its own — always configure it via the parent gallery's `itemConfig`.
- **Wrong column names in `templateValuesMapping`.** Each mapping value must exactly match a column code in the datasource; mismatches cause blank tile fields.
- **`templatePreview` mapping to a non-image column.** The preview slot fetches the image by ID via `getImageFullUrlById`; the mapped column must hold a `LookupValue` with `value` equal to the image file GUID.
- **`actions` items missing `type: "crt.MenuItem"`.** The tile renders context-menu items only when each action has `type: "crt.MenuItem"` and a valid `clicked.request`.

---

## 8. Quick checklist

- [ ] Tile is configured via the parent `crt.TemplateGallery`'s `itemConfig`, not via a standalone `insert` op.
- [ ] `templateValuesMapping` covers all four slots: `templateId`, `templateName`, `templatePreview`, `templateDescription`.
- [ ] Each mapping key matches an actual datasource column code.
- [ ] `actions` items each have `type: "crt.MenuItem"` and `clicked.request`.
- [ ] Parent gallery has `items` bound to a populated `BaseViewModelCollection`.
