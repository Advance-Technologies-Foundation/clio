# How to Add an Expansion Panel (`crt.ExpansionPanel`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ExpansionPanel` into a Creatio Freedom UI page schema.
>
> `crt.ExpansionPanel` is a collapsible accordion section with a header, a body (`items`
> slot), and an optional toolbar (`tools` slot). Children are inserted with `parentName`
> pointing at the expansion panel.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: `crt.GridContainer`, `crt.FlexContainer`, `crt.Label` (body via `items` slot); `crt.Button`, `crt.SearchFilter`, `crt.MenuItem` (header via `tools` slot)

---

## 1. Mental model — the 1 + N places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ExpansionPanel"` and a `title`. |
| 2+ | `viewConfigDiff` (more entries) | Additional `insert` ops for each child element with `parentName: "<ExpansionPanel_id>"` and `propertyName: "items"` (or `"tools"`). |

If the panel hosts a data-bound element (e.g. `crt.DataGrid`), that child still needs its
own `modelConfigDiff` / `viewModelConfigDiff` entries (the panel itself is purely layout).

### 1.1 `contentSlots`

```
contentSlots: ['items', 'tools']
```

- `"items"` — the panel body. Most common slot.
- `"tools"` — the right-aligned toolbar in the panel header.

### 1.2 Naming convention

```
ExpansionPanel_<id>           // view element name
ExpansionPanel_<id>_body      // optional: a Container child holding the body
```

---

## 2. Step-by-step recipe

### 2.1 Insert the panel (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ExpansionPanel_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ExpansionPanel",
    "title": "#ResourceString(ExpansionPanel_xkp4r_Title)#",
    "description": "#ResourceString(ExpansionPanel_xkp4r_Description)#",
    "tooltip": "#ResourceString(ExpansionPanel_xkp4r_Tooltip)#",
    "expanded": false,
    "fullWidthHeader": true,
    "togglePosition": "before",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 6 }
  }
}
```

### 2.2 Insert children into `items` (`viewConfigDiff` entries)

```jsonc
{
  "operation": "insert",
  "name": "ExpansionPanel_xkp4r_Label",
  "parentName": "ExpansionPanel_xkp4r",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Label",
    "caption": "#ResourceString(Inside_Caption)#",
    "labelType": "body",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 1 }
  }
}
```

### 2.3 Insert children into `tools` (e.g. an action button)

```jsonc
{
  "operation": "insert",
  "name": "ExpansionPanel_xkp4r_AddBtn",
  "parentName": "ExpansionPanel_xkp4r",
  "propertyName": "tools",
  "index": 0,
  "values": {
    "type": "crt.Button",
    "icon": "add-button-icon",
    "iconPosition": "only-icon",
    "displayType": "text",
    "color": "default",
    "clicked": { "request": "crt.CreateRecordRequest" }
  }
}
```

---

## 3. Property reference

### 3.1 Header & state property names

- The panel **header text** is set via **`title`** — a localized string, e.g. `"#ResourceString(<Name>_Title)#"`.
- The panel **open/closed state** is controlled by **`expanded`** — a boolean (`true` = open, `false` = collapsed; default `false`).

These are the exact property names the runtime reads for the header and the open-state; emit them verbatim on every `crt.ExpansionPanel`. All OOTB pages follow this (e.g. `Cases_FormPage` → `TermsExpansionPanel`, `CaseLifecycleExpansionPanel`). See the §2 / §4 examples.

> **Edge case for the `expanded` default.** A panel with **no `title`, no `tooltip`, and no `description`** is treated as header-less and auto-expands (`expanded = true`) at init regardless of the value you pass. Because this recipe always pairs the panel with a `title`, the documented `false` default holds for the intended usage — just don't rely on `expanded: false` for a header-less panel.

### 3.2 Other properties

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ExpansionPanel` are in `ComponentRegistry.json` under `componentType: "crt.ExpansionPanel"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

The component does **not** declare `dataValueTypes`; it doesn't bind to a column directly.

---

## 4. Copy-paste minimal example — panel + DataGrid child

```jsonc
// viewConfigDiff entries (top-level inserts)
[
  {
    "operation": "insert",
    "name": "AttachmentsPanel",
    "parentName": "MainContainer",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.ExpansionPanel",
      "title": "#ResourceString(AttachmentsPanel_Title)#",
      "expanded": true,
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 }
    }
  },
  {
    "operation": "insert",
    "name": "AttachmentsPanel_FileList",
    "parentName": "AttachmentsPanel",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.FileList",
      "items": "$AttachmentsPanel_files",
      "primaryColumnName": "AttachmentsPanel_filesDS_Id",
      "masterRecordColumnValue": "$Id",
      "recordColumnName": "Contact",
      "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 6 }
    }
  }
]
```

Plus the corresponding `viewModelConfigDiff` and `modelConfigDiff` entries for `AttachmentsPanel_files` — see crt.FileList guide.

---

## 5. Common pitfalls

1. **Forgetting `parentName` on children** — without it, the child inserts at the page root and the panel appears empty.
2. **Wrong `propertyName`** — children go into `"items"` (body) or `"tools"` (header). Anything else is silently rejected.
3. **`rowSpan: 1` on a collapsed panel** — when the panel expands the body has no room and content gets clipped. Plan for the expanded height.
4. **Putting `tools` content with non-trivial layout** — the toolbar is a flex row; large/wide items break the header. Stick to icon buttons + small inputs in `tools`.
5. **`expanded: true` causing a heavy panel to render eagerly** — if the body has a `crt.DataGrid` or `crt.Feed`, those load on render. Set `expanded: false` (the default) for performance, or lazy-load via attribute binding.
6. **`fullWidthHeader: true` next to other elements in the same row** — the header pushes neighbouring cells. Use only when the panel occupies the full row.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ExpansionPanel"`, unique `name`, `propertyName: "items"`.
- [ ] `title` (localized) provided.
- [ ] Children inserted with `parentName: "<ExpansionPanel_id>"` and `propertyName: "items"` (or `"tools"`).
- [ ] `expanded` chosen intentionally (`false` for performance, `true` for primary content).
- [ ] `layoutConfig` reserves enough `rowSpan` for the expanded state.
