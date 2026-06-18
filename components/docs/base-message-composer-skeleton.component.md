# How to Add a Base Message Composer Skeleton (`crt.BaseMessageComposerSkeleton`) to a Freedom UI Page

> Audience: code agent inserting `crt.BaseMessageComposerSkeleton` into a Creatio Freedom UI page schema.
> A placeholder loading state that visually mimics the `crt.BaseMessageComposer` layout; shown while
> the real composer is initializing or data is loading.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.BaseMessageComposerSkeleton"`. **Always present.** |

`crt.BaseMessageComposerSkeleton` has no inputs, no outputs, and no properties beyond what the
platform base provides (`visible`). Drop it into the same parent container as
`crt.BaseMessageComposer` and toggle visibility with `visible` to swap between skeleton and the
real composer.

### 1.1 Naming convention

```
BaseMessageComposerSkeleton_<id>        // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "BaseMessageComposerSkeleton_main",
  "parentName": "ConversationContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.BaseMessageComposerSkeleton",
    "visible": "$IsComposerLoading"
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.BaseMessageComposerSkeleton` are in `ComponentRegistry.json` under
`componentType: "crt.BaseMessageComposerSkeleton"`. The component exposes no custom inputs.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — skeleton shown during loading
{
  "operation": "insert",
  "name": "ComposerSkeleton",
  "parentName": "ComposerWrapper",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.BaseMessageComposerSkeleton",
    "visible": "$IsComposerLoading"
  }
}
```

```jsonc
// viewModelConfigDiff — attribute to control skeleton visibility
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "IsComposerLoading": { "value": true }
  }
}
```

---

## 7. Common pitfalls

1. **Skeleton does not accept any content** — it has no `items` slot and no child elements; do not attempt to insert children.
2. **Pair with `visible` on the real composer** — the skeleton and the real `crt.BaseMessageComposer` should use complementary `visible` bindings so exactly one is shown at a time.
3. **No outputs to handle** — the skeleton is purely decorative; it fires no events.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.BaseMessageComposerSkeleton"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `visible` bound to a boolean attribute that is `true` while loading and `false` once the real composer is ready.
- [ ] Companion `crt.BaseMessageComposer` element uses the inverse of the same visibility attribute.
