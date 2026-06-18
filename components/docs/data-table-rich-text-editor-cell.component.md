# How to Add a Rich Text Table Cell (`crt.TableRichTextEditorCell`) to a Freedom UI Page

> Audience: code agent inserting `crt.TableRichTextEditorCell` into a Creatio Freedom UI page schema.
> A `crt.TableRichTextEditorCell` is a read-only display cell used inside a `crt.DataGrid` column to render HTML-formatted rich text. It shows an abbreviated preview and expands a tooltip with full formatted content on hover. It is wired as the `cellView` property of a column definition.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.DataGrid` column definition (as `cellView` value)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A column in a `crt.DataGrid` with `"cellView": { "type": "crt.TableRichTextEditorCell", "value": "..." }`. |

`crt.TableRichTextEditorCell` is **not** inserted as an independent element with `parentName`/`propertyName: "items"`. It lives inside the `cellView` slot of a column definition in `crt.DataGrid`.

### 1.1 Naming convention
```
// cellView object has no `name` field.
// Bind the rich text attribute path via "value".
```

---

## 2. Step-by-step recipe

### 2.1 Wire `crt.TableRichTextEditorCell` as `cellView` inside a `crt.DataGrid` column

```jsonc
{
  "id": "c3d4e5f6-0000-0000-0000-000000000001",
  "code": "MyDetail_Notes",
  "caption": "#ResourceString(MyDetail_Notes)#",
  "dataValueType": 28,
  "cellView": {
    "type": "crt.TableRichTextEditorCell",
    "value": "$MyDetail.MyDetail_Notes"
  }
}
```

Hovering over the cell shows a tooltip overlay with the full HTML content (SVGs are automatically converted to base64 `<img>` tags for tooltip display).

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TableRichTextEditorCell` are in `ComponentRegistry.json` under `componentType: "crt.TableRichTextEditorCell"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

Cell `inputs` available at runtime (from the registry):
```ts
interface TableRichTextEditorCellInputs {
  column: BaseColumnDefinition;  // injected by crt.DataGrid
  record: DataItem;              // injected by crt.DataGrid
  value: TValue;                 // bound via "value": "$..." in cellView
}
```
All three inputs are injected by `crt.DataGrid`; you do not set `column` or `record` manually in the schema.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — DataGrid with a rich text column
{
  "operation": "insert",
  "name": "ArticlesGrid",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DataGrid",
    "items": "$ArticlesDetail",
    "primaryColumnName": "ArticlesDetail_Id",
    "columns": [
      {
        "id": "c3d4e5f6-0000-0000-0000-000000000001",
        "code": "ArticlesDetail_Notes",
        "caption": "#ResourceString(ArticlesDetail_Notes)#",
        "dataValueType": 28,
        "cellView": {
          "type": "crt.TableRichTextEditorCell",
          "value": "$ArticlesDetail.ArticlesDetail_Notes"
        }
      }
    ],
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 12, "rowSpan": 6 }
  }
}
```

---

## 7. Common pitfalls

- **Trying to `insert` as a standalone view element.** `crt.TableRichTextEditorCell` is only valid inside a `crt.DataGrid` column's `cellView`.
- **Providing raw `&nbsp;` in value bindings.** The component auto-strips `&nbsp;` from the preview; the raw HTML is still used in the tooltip.
- **Expecting inline editing.** This cell type is read-only. For editable rich text columns, use a dedicated editable cell type (e.g. `crt.DataTableEditTextCell`) as `editingCellView`.
- **SVG images in content.** SVGs embedded in the rich text are converted to base64 `<img>` tags in the tooltip rendering. Ensure your content does not rely on SVG-specific features (e.g. scripts) that would break after the conversion.
- **Wrong `dataValueType`.** Rich text columns store their content as text (`dataValueType: 28` / `TEXT`). Mismatching with a non-text type causes serialization errors.

---

## 8. Quick checklist

- [ ] `crt.TableRichTextEditorCell` placed in `cellView` of a `crt.DataGrid` column, not as a top-level view element.
- [ ] `value` binding is `"$<collectionAttr>.<columnCode>"`.
- [ ] `id` on the column definition is a unique GUID.
- [ ] `dataValueType` is `28` (TEXT) or appropriate text variant.
- [ ] If inline editing is required, add a separate `editingCellView` entry with a compatible text edit cell.
