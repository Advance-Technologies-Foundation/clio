# How to Add a Link (`crt.Link`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Link` into a Creatio Freedom UI page schema.
>
> A `crt.Link` renders a styled hyperlink. In `mode: "native"` it behaves as a plain anchor;
> in `mode: "preventDefault"` it suppresses navigation and fires the `clicked` output instead,
> which is useful for opening records or triggering in-page actions.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, data-grid `_designOptions.columns.cellViews`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Link"`, `caption`, and optional `href`/`clicked`. **Always present.** |
| 2 | `handlers` (optional) | A request handler for the `clicked` event — only needed when `mode: "preventDefault"` is set. |

`crt.Link` is **view-only** — no datasource, no viewModel attribute. In data-grid column cell views
(`_designOptions.columns.cellViews`) it appears inline as a column-level object rather than a
top-level insert op.

### 1.1 Naming convention

```
Link_<id>          // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Link_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Link",
    "caption": "#ResourceString(Link_abc123_Caption)#",
    "href": "https://example.com",
    "target": "_blank",
    "underlining": "hover",
    "mode": "native",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Handler for `mode: "preventDefault"`

```jsonc
// viewConfigDiff — use preventDefault + clicked binding
"mode": "preventDefault",
"clicked": { "request": "crt.OpenRecordRequest", "params": { "recordId": "$Items.PDS_Id" } }

// handlers entry
{
  "request": "crt.OpenRecordRequest",
  "handler": async (request, next) => {
    // your logic here
    return next?.handle(request);
  }
}
```

### 2.3 As a data-grid cell view

`crt.Link` is frequently placed inside a data-grid column's `cellViews` map in `_designOptions`:

```jsonc
"_designOptions": {
  "columns": {
    "cellViews": {
      "PDS_Name": {
        "type": "crt.Link",
        "caption": "$Items.PDS_Name",
        "href": "$Items.PDS_Link",
        "mode": "$Items.PDS_LinkMode",
        "underlining": "hover",
        "target": "$Items.PDS_LinkTarget",
        "clicked": {
          "request": "crt.UpdateRecordRequest",
          "params": {
            "entityName": "CopilotAgent",
            "recordId": "$Items.PDS_Id"
          }
        }
      }
    }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Link` are in `ComponentRegistry.json` under `componentType: "crt.Link"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// clicked output — RequestBindingConfig
interface RequestBindingConfig {
  request: string;                    // e.g. 'crt.OpenRecordRequest'
  params?: RequestParamsBindingConfig;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — standalone link in a flex container
{
  "operation": "insert",
  "name": "ExternalLink_ref1",
  "parentName": "ToolsFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Link",
    "caption": "#ResourceString(ExternalLink_ref1_Caption)#",
    "href": "https://example.com",
    "target": "_blank",
    "underlining": "hover",
    "mode": "native"
  }
}
```

```jsonc
// viewConfigDiff — preventDefault link that opens a record
{
  "operation": "insert",
  "name": "RecordLink_abc",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Link",
    "caption": "$Items.PDS_Name",
    "mode": "preventDefault",
    "underlining": "hover",
    "clicked": { "request": "crt.UpdateRecordRequest", "params": { "recordId": "$Items.PDS_Id" } }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare attribute that holds the caption
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Link_caption": { "value": "" }
    }
  }
}

// viewConfigDiff.values
"caption": "$Link_caption"
```

---

## 7. Common pitfalls

1. **Setting `href` and `mode: "preventDefault"` together** — in `preventDefault` mode the browser
   does not navigate even if `href` is set; the link only fires `clicked`. Leave `href` empty or
   omit it when using `preventDefault`.
2. **`target: "_blank"` without `rel="noopener noreferrer"`** — the link component handles this
   internally via DomSanitizer but the schema value is just the target string; the renderer adds
   the security attribute.
3. **Forgetting `clicked.request`** when `mode: "preventDefault"` — the click is silenced and
   nothing happens.
4. **Using `linkType` for visual size on a standalone link** — `linkType` maps to typography
   presets (e.g. `"body"`, `"caption"`), not to a size enum; pick the preset that matches the
   surrounding text scale.
5. **Setting `underlining: "never"` on interactive links** — visually hides the affordance; pair
   with a distinctive `linkType` or `color` to compensate.
6. **Binding `caption` to a raw attribute without `| crt.StringValue`** — if the attribute value
   is a non-string (e.g. a number), the interpolation may render `[object Object]`; cast
   explicitly.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Link"`, unique `name`, valid `parentName`.
- [ ] `caption` set (static `#ResourceString(...)#` or `$Attribute` binding).
- [ ] `mode` chosen: `"native"` for plain anchor, `"preventDefault"` for in-page action.
- [ ] If `mode: "preventDefault"`, `clicked.request` is wired to a handler.
- [ ] If `mode: "native"`, `href` is set to a valid URL string or `$Attribute` binding.
- [ ] `target` set when opening external links (`"_blank"`).
- [ ] `underlining` explicitly set when the parent design requires a specific underline style.
