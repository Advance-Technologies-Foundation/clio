# How to Add an Articles List (`crt.ArticlesList`) to a Freedom UI Page

> Audience: code agent inserting `crt.ArticlesList` into a Creatio Freedom UI page schema.
> Renders a scrollable list of knowledge-base article tiles (each rendered as `crt.ArticlePreview`);
> supports master-detail navigation to a full-article view and reacts to live collection changes.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ArticlesList"` and a `dataItems` binding. **Always present.** |
| 2 | `viewModelConfigDiff` | Attribute for the `dataItems` collection. |

`crt.ArticlesList` has no create command and is not a designer-palette item.

### 1.1 Naming convention
```
ArticlesList_<id>    // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Declare attribute in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "ArticlesCollection": {
      "isCollection": true,
      "viewModelConfig": {
        "attributes": {
          "ArticlesDS_Id": { "modelConfig": { "path": "ArticlesDS.Id" } },
          "ArticlesDS_Caption": { "modelConfig": { "path": "ArticlesDS.Name" } },
          "ArticlesDS_Text": { "modelConfig": { "path": "ArticlesDS.Body" } }
        }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ArticlesList_main",
  "values": {
    "type": "crt.ArticlesList",
    "dataItems": "$ArticlesCollection",
    "galleryItemConfig": {
      "templateValuesMapping": {
        "id": "ArticlesDS_Id",
        "caption": "ArticlesDS_Caption",
        "text": "ArticlesDS_Text",
        "author": "ArticlesDS_Author",
        "modifiedDate": "ArticlesDS_ModifiedOn",
        "tags": "ArticlesDS_Tags"
      }
    }
  },
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ArticlesList` are in `ComponentRegistry.json` under `componentType: "crt.ArticlesList"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`galleryItemConfig` accepts either:
```ts
// GalleryItemConfig — template-based mapping
interface GalleryItemConfig {
  templateValuesMapping: {
    id: string;           // attribute name for article ID
    caption: string;      // attribute name for article title
    author?: string;      // attribute name for author lookup
    modifiedDate?: string;// attribute name for last modified date
    tags?: string;        // attribute name for tags collection
    text?: string;        // attribute name for HTML body
  };
}

// GalleryViewItemConfig — factory-based (advanced)
interface GalleryViewItemConfig {
  viewElementConfigFactory: (record: unknown) => ViewElementConfig;
}
```

If `galleryItemConfig` is omitted, the component uses a default factory that creates
`crt.ArticlePreview` tiles without field mapping (all fields will be empty).

---

## 7. Common pitfalls

1. **Missing `galleryItemConfig.templateValuesMapping`** — without it the article tiles render with empty caption, author, and body. Always provide the mapping from datasource attribute names to the article model fields.
2. **`dataItems` bound to a plain array instead of a live collection** — the component subscribes to `changed` events on a `BaseViewModelCollection`; a plain `DataItem[]` does not fire change events, so the list won't update when data reloads.
3. **`ArticleCommunicationService` not provided** — the component subscribes to `articleShowed()` for master-detail navigation. This service must be provided at the module level that contains `crt.ArticlesList`.
4. **Article HTML body contains `<style>` tags** — the preview uses DOMPurifier with `FORBID_TAGS: ['style']`; inline styles in article bodies are stripped.
5. **Navigating back to the list** — the component provides a `backToList()` method that hides the detail view. If you add a back-button, bind its click to trigger `backToList` or reset the `showDetails` attribute.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ArticlesList"`, unique `name`, valid `parentName`.
- [ ] `dataItems` bound to a live `BaseViewModelCollection` attribute.
- [ ] `galleryItemConfig.templateValuesMapping` set with the correct datasource attribute names for all article fields.
- [ ] Datasource declared in `modelConfigDiff` with the corresponding columns for `Id`, `Name`, `Body`, `Author`, `ModifiedOn`, and the tags relation.
- [ ] `ArticleCommunicationService` available in the module context.
