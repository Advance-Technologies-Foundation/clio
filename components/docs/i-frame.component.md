# How to Add an IFrame (`crt.IFrame`) to a Freedom UI Page

> Audience: code agent inserting a `crt.IFrame` into a Creatio Freedom UI page schema.
>
> `crt.IFrame` embeds external content (URL or inline HTML) inside a sandboxed iframe.
> It also exposes `items` and `placeholder` content slots for layout-time customizations
> (e.g. a custom "no content" placeholder).

## Metadata

- **Category**: interactive
- **Container**: yes
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: none on the visible iframe; an optional `placeholder` slot exists but is unused in 7.8.0 schemas

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.IFrame"` plus either `urlContent` or `htmlContent`. |
| 2 | `viewConfigDiff` (optional) | An `insert` into the iframe's `placeholder` slot for a custom empty-state. |

`crt.IFrame` is **not** data-bound — it doesn't need `viewModelConfigDiff` or
`modelConfigDiff` entries.

### 1.1 `contentSlots`

```
contentSlots: ['items', 'placeholder']
```

- `"items"` — additional overlay elements rendered next to the iframe (rarely used).
- `"placeholder"` — content shown when the iframe content fails to load (or before it loads).

---

## 2. Step-by-step recipe

### 2.1 Insert the iframe (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "IFrame_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IFrame",
    "contentType": "url",
    "urlContent": "https://example.com/embed/42",
    "sandbox": "allow-scripts allow-same-origin",
    "allow": "fullscreen; clipboard-read; clipboard-write",
    "referrerPolicy": "strict-origin-when-cross-origin",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 6 }
  }
}
```

Or with inline HTML:

```jsonc
{
  "operation": "insert",
  "name": "IFrame_xkp4r",
  ...
  "values": {
    "type": "crt.IFrame",
    "contentType": "html",
    "htmlContent": "<h1>Inline content</h1><p>Anything you like.</p>",
    "sandbox": "",
    "layoutConfig": { ... }
  }
}
```

### 2.1a Dynamic URL — bind `urlContent` to a view-model attribute

> `urlContent` is assigned to `iframe.src` verbatim. Inline `$Attr` placeholders **inside** a URL string are **not** interpolated — they will be sent to the embedded page as literal `$Attr` text. To compute the URL at runtime (e.g. inject the current record id), bind the whole `urlContent` value to a view-model attribute and set it from a handler.

The canonical PackageStore pattern (e.g. `IcoreAutoTestPackage / Page_IFrameTestPage`) needs three diff entries:

1. `viewConfigDiff` — bind `urlContent` to the attribute (the value is the full `$AttrName` reference, nothing else):

```jsonc
{
  "operation": "insert",
  "name": "IFrame_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IFrame",
    "contentType": "url",
    "urlContent": "$urlContent",
    "sandbox": "allow-scripts allow-same-origin"
  }
}
```

2. `viewModelConfigDiff` — declare the attribute:

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "urlContent": { "value": "" }
  }
}
```

3. `handlers` — compute the URL on init (or any other event) and write it into the attribute:

```js
{
  request: "crt.HandleViewModelInitRequest",
  handler: async (request, next) => {
    const id = await request.$context.Id;
    await request.$context.set("urlContent", `https://example.com/embed/${id}`);
    return next?.handle(request);
  },
}
```

### 2.2 (Optional) Custom placeholder

```jsonc
{
  "operation": "insert",
  "name": "IFrame_xkp4r_placeholder",
  "parentName": "IFrame_xkp4r",
  "propertyName": "placeholder",
  "index": 0,
  "values": {
    "type": "crt.Label",
    "caption": "#ResourceString(IFrame_xkp4r_NoContent)#",
    "labelType": "body",
    "labelTextAlign": "center"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.IFrame` are in `ComponentRegistry.json` under `componentType: "crt.IFrame"`. This guide
covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "EmbeddedDashboard",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IFrame",
    "contentType": "url",
    "urlContent": "https://internal.example.com/dashboards/customer/42",
    // `allow-scripts allow-same-origin` is appropriate ONLY because this
    // example embeds a first-party / fully-trusted internal dashboard.
    // For external/untrusted URLs, drop `allow-same-origin` (combining the
    // two effectively disables the sandbox).
    "sandbox": "allow-scripts allow-same-origin",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 8 }
  }
}
```

---

## 5. Common pitfalls

1. **Setting both `urlContent` and `htmlContent`** — `urlContent` wins. The HTML is ignored. Pick one.
2. **Inline `$Attr` inside a URL string is not interpolated** — `urlContent` is assigned to `iframe.src` verbatim, so `"urlContent": "https://x.com/?id=$Id"` loads the literal `$Id` (not the record id). To compute URLs at runtime, set `urlContent` to a full `$AttrName` reference (just `"$urlContent"`, nothing else) and write the URL into that attribute from a handler — see §2.1a.
3. **`sandbox: ""` (empty)** — applies the most restrictive sandbox (no script, no same-origin). Many embedded URLs need at least `"allow-scripts"`; add `allow-same-origin` **only** for fully-trusted first-party content (the two tokens together can be re-written by the embedded page and effectively disable the sandbox).
4. **`sandbox: null` (removed attribute)** — gives the embedded content full access (cookies, postMessage, etc.). Avoid for external URLs.
5. **`urlContent` pointing at an external site without CORS / X-Frame-Options support** — the iframe loads but the embedded page can refuse to render. Show a placeholder via the `placeholder` slot.
6. **Resizing surprises** — iframes default to their content's intrinsic height. Without a fixed `rowSpan` (and `layoutConfig`), they may collapse to zero height.
7. **`htmlContent` containing `<script>` plus `sandbox: ""`** — scripts won't execute. Add `"allow-scripts"` if execution is intended.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.IFrame"`, unique `name`, `propertyName: "items"`.
- [ ] Exactly one of `urlContent` or `htmlContent` set.
- [ ] `sandbox` set explicitly with the minimum required tokens for the embedded content.
- [ ] `allow` and `referrerPolicy` set when the embedded page needs specific permissions / origin info.
- [ ] `placeholder` slot populated for graceful fallback.
- [ ] `layoutConfig` provides generous `rowSpan`.
