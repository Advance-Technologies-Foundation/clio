# How to Add a Template Gallery (`crt.TemplateGallery`) to a Freedom UI Page

> Audience: code agent inserting `crt.TemplateGallery` into a Creatio Freedom UI page schema.
> A `crt.TemplateGallery` renders a card-grid gallery of template tiles backed by a `BaseViewModelCollection`. It supports single-item selection, bulk actions, and pagination, delegating each tile to a `crt.TemplateGalleryItem` child type.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: none (items rendered internally via `itemConfig.componentClassFactory`)

---

## 1. Mental model — the 1-2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TemplateGallery"`, `items` bound to a collection attribute, and optional `itemConfig`. |
| 2 | `viewModelConfigDiff` | A datasource attribute for `items` and an attribute for `selectedItemId` (only if you drive selection from state). |
| 3 | `handlers` | Handlers for `paginationChange`, `itemClick`, `itemDblClick`, etc. (only when needed). |

### 1.1 Naming convention
```
TemplateGallery_<id>              // view element name
$TemplateGallery_<id>_SelectedId  // attribute for selectedItemId binding
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "CampaignTemplateGallery",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TemplateGallery",
    "visible": true,
    "items": "$GalleryItems",
    "selectedItemId": "$Gallery_SelectedItemId",
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

### 2.2 (Optional) Add bulk action items

```jsonc
"bulkActions": [
  {
    "type": "crt.MenuItem",
    "caption": "#ResourceString(DeleteTemplateMenuItem)#",
    "icon": "delete-button-icon",
    "clicked": { "request": "crt.DeleteRecordRequest" }
  }
]
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TemplateGallery` are in `ComponentRegistry.json` under `componentType: "crt.TemplateGallery"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// GalleryItemConfig — passed to itemConfig
interface GalleryItemConfig {
  templateValuesMapping?: {
    templatePreview: string;    // datasource column for the preview image
    templateId: string;         // datasource column for the unique item id
    templateName: string;       // datasource column for the tile title
    templateDescription: string; // datasource column for the tile description
  };
  defaultSize?: SizeEnum;       // 'small' | 'medium' | 'large'
  actions?: CrtMenuItemViewElementConfig[];
}

// PlaceholderImage — passed to placeholderImage
type PlaceholderImage =
  | { type: 'icon'; icon: string; padding?: string }   // PlaceholderIcon
  | { type: 'animation'; animationData: unknown };       // PlaceholderAnimation
```

---

## 5. Copy-paste minimal example

Based on real `CampaignTemplates_MiniPage` schema from PackageStore:
```jsonc
{
  "operation": "insert",
  "name": "CampaignRecommendedTemplateGallery",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TemplateGallery",
    "visible": true,
    "items": "$RecommendedGalleryItems",
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

## 6. Driving from page state

`selectedItemId` accepts a `$Attribute` binding — bind it to track/restore the selected tile:
```jsonc
"selectedItemId": "$Gallery_SelectedItemId"
```

Wire `selectedItemIdChange` to update the attribute when the user picks a tile:
```jsonc
"selectedItemIdChange": { "request": "crt.GallerySelectionChangeRequest" }
```

---

## 7. Common pitfalls

- **`templateValuesMapping` columns not matching the datasource.** Column keys must exactly match the schema columns of the bound collection; mismatches cause tiles to render blank.
- **`selectable: false` without `itemClick` handler.** When `selectable` is `false` single-click fires `itemClick` instead of updating `selectedItemId`; ensure you have a handler for it.
- **`bulkActions` without the selection state loaded.** Bulk actions are only meaningful when `selectable: true` and a `SelectionState` is tracked via `selectionChange`.
- **`placeholderImage.type` mismatch.** Use `"icon"` for a system icon name, `"animation"` for Lottie animation data. Mixing types causes the placeholder to render empty.
- **Omitting `layoutConfig` inside a `crt.GridContainer`.** Without `layoutConfig` the gallery takes up no grid cells and becomes invisible.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.TemplateGallery"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `items` bound to a `BaseViewModelCollection` attribute.
- [ ] `itemConfig.templateValuesMapping` columns match the datasource schema.
- [ ] `selectedItemId` bound to a viewModel attribute if selection must persist.
- [ ] `layoutConfig` provided when inside a `crt.GridContainer`.
- [ ] `bulkActions` items each have `type: "crt.MenuItem"` and a `clicked.request`.
