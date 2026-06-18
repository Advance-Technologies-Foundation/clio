# How to Add a File Drop (`crt.FileDrop`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FileDrop` into a Creatio Freedom UI page schema.
>
> A `crt.FileDrop` is a container view element that accepts drag-and-dropped files and emits
> them through its `fileDropped` output. It wraps child view elements in the `items` slot and
> overlays a visual drop indicator when a drag enters the area.

## Metadata

- **Category**: interactive
- **Container**: yes (children go into the `items` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: any view elements that should appear inside the drop zone

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FileDrop"`, an empty `items: []`, and a `fileDropped` request binding. **Always present.** |
| 2 | `handlers` | A request handler for the `fileDropped` request to process the dropped files. |

`crt.FileDrop` owns no datasource and no attributes. It is view-only.

### 1.1 Naming convention

```
FileDrop_<id>          // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FileDrop_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FileDrop",
    "multiple": true,
    "disabled": false,
    "showOverlay": true,
    "visible": true,
    "items": [],
    "fileDropped": { "request": "crt.UploadFileRequest", "params": { "Files": { "attributeValue": "crt.HandlerInputParameters.Files" } } },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 Add a handler in `handlers`

```jsonc
{
  "request": "crt.UploadFileRequest",
  "handler": async (request, next) => {
    const files = request.Files;
    // process dropped files here
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FileDrop` are in `ComponentRegistry.json` under `componentType: "crt.FileDrop"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// fileDropped payload
interface FileDroppedPayload {
  files: File[];
}

// fileDropped binding — RequestBindingConfig shape
interface RequestBindingConfig {
  request: string;                    // e.g. 'crt.UploadFileRequest'
  params?: RequestParamsBindingConfig;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FileDrop_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FileDrop",
    "visible": "$IsConversationVisible",
    "multiple": false,
    "showOverlay": true,
    "disabled": "$EnableChatFileHandling | crt.InvertBooleanValue",
    "items": [],
    "fileDropped": {
      "request": "crt.UploadFileRequest",
      "params": {
        "Files": { "attributeValue": "crt.HandlerInputParameters.Files" }
      }
    }
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.UploadFileRequest",
  "handler": async (request, next) => next?.handle(request)
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FileDrop_disabled": { "value": false }
    }
  }
}

// viewConfigDiff.values
"disabled": "$FileDrop_disabled"
```

`disabled` and `visible` are `propertyBindable` — use `$AttributeName` bindings to toggle the
drop zone at runtime.

---

## 7. Common pitfalls

1. **Forgetting `items: []`** — `crt.FileDrop` is a container with a `contentSlots: ['items']` slot; without the array, child inserts have no target.
2. **Not wiring `fileDropped.request`** — the drop event fires silently if no request is bound; dropped files are lost.
3. **`multiple: false` with multi-file drags** — when `multiple` is `false`, the drag validation rejects transfers with more than one item before `fileDropped` fires.
4. **`disabled` bound to a non-existent attribute** — evaluates to `undefined` (falsy), leaving the drop zone enabled; the platform does not warn about missing attributes.
5. **Directories dropped** — the component rejects directory entries; only flat file drops are accepted.
6. **`showOverlay: false`** — the drag-over visual indicator is suppressed; users receive no feedback that the area is a drop target.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FileDrop"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `items: []` present in `values` (container slot required even when starting empty).
- [ ] `fileDropped.request` bound to a handler or a platform request.
- [ ] `multiple` set explicitly (`true` for multi-file, `false` to restrict to one file).
- [ ] `disabled` wired to a page attribute if the drop zone must be togglable at runtime.
- [ ] Matching `handlers` entry exists for the `fileDropped` request.
