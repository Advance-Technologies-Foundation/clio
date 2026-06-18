# How to Add an Article Preview (`crt.ArticlePreview`) to a Freedom UI Page

> Audience: code agent inserting `crt.ArticlePreview` into a Creatio Freedom UI page schema.
> Renders a single knowledge-base article tile inside an articles gallery; displays the article
> caption, author, modification date, tags, and collapsible full-content preview.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.ArticlesList` (via gallery item collection)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | Configuration of the parent `crt.ArticlesList` with `galleryItemConfig` specifying `"crt.ArticlePreview"` as the tile type. |

`crt.ArticlePreview` has no `@CrtInput`/`@CrtOutput` decorators in its leaf class — it inherits all
inputs from `CrtGalleryBaseItemComponent` (`record`, `isSelected`, `tileSizeClasses`). The tile is
rendered automatically by the parent `crt.ArticlesList` component and is not inserted as a standalone
view element.

---

## 2. Step-by-step recipe

### 2.1 Insert the parent ArticlesList

See the `crt.ArticlesList` guide. The `ArticlesList` component creates `crt.ArticlePreview` tiles
automatically for each item in its `dataItems` collection.

```jsonc
{
  "operation": "insert",
  "name": "ArticlesList_main",
  "values": {
    "type": "crt.ArticlesList",
    "dataItems": "$ArticlesCollection"
  },
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ArticlePreview` are in `ComponentRegistry.json` under `componentType: "crt.ArticlePreview"`. This
guide covers only the assembly mechanics.

---

## 7. Common pitfalls

1. **Inserting `crt.ArticlePreview` directly** — the tile is rendered by the parent `crt.ArticlesList` per collection item. Do not insert individual tile ops.
2. **`record` field mapping** — the tile reads `caption`, `author`, `modifiedDate`, `tags`, `text`, and `id` from the record object. These fields must be present in the article collection items passed to `crt.ArticlesList`.
3. **HTML content sanitization** — the `text` field is sanitized with DOMPurifier; `<style>` tags are stripped. External styles inside article bodies will be removed.
4. **`tags` as array or single value** — the component handles both an array of tag objects and a single tag object; each tag must have `displayValue`, `value`, and optionally `primaryColorValue`.

---

## 8. Quick checklist

- [ ] Parent `crt.ArticlesList` configured with `dataItems` bound to an article collection.
- [ ] Article collection items contain `caption`, `author`, `modifiedDate`, `tags`, `text`, and `id`.
- [ ] `ArticleCommunicationService` is available in the page context (provided by the articles module).
