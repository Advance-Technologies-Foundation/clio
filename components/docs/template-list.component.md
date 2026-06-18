# How to Add a Template List (`crt.TemplateList`) to a Freedom UI Page

> Audience: code agent inserting `crt.TemplateList` into a Creatio Freedom UI page schema.
> A `crt.TemplateList` renders a repeated list of items from a `BaseViewModelCollection` using a named `template` content slot. Each item in the collection is rendered once using the template defined in the `template` slot, enabling fully custom per-item layouts.

## Metadata
- **Category**: display
- **Container**: yes (child template goes into the `template` content slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: any view element type (defined inside the `template` slot)

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.TemplateList"`, `items` bound to a collection attribute, `direction`, `gap`, and a `template` array with child view elements. |
| 2 | `viewModelConfigDiff` | A datasource attribute for `items` (only if not already declared by another element). |

### 1.1 Naming convention
```
TemplateList_<id>           // view element name
// Children inside template use collection-scoped bindings, e.g. "$CopilotQuickActions.Name"
```

---

## 2. Step-by-step recipe

### 2.1 Insert the `crt.TemplateList` with a template

```jsonc
{
  "operation": "insert",
  "name": "ActionsList",
  "parentName": "ActionsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TemplateList",
    "items": "$CopilotQuickActions",
    "direction": "row",
    "gap": 8,
    "template": [
      {
        "type": "crt.Button",
        "name": "$CopilotQuickActions.Code",
        "caption": "$CopilotQuickActions.Name",
        "size": "large",
        "color": "outline",
        "clicked": {
          "request": "crt.CopilotActionRequest",
          "params": {
            "prompt": "$CopilotQuickActions.Name",
            "promptCode": "$CopilotQuickActions.Code"
          }
        }
      }
    ]
  }
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.TemplateList` are in `ComponentRegistry.json` under `componentType: "crt.TemplateList"`. This guide covers
only the assembly mechanics.

---

## 5. Copy-paste minimal example

Based on real `CopilotPanel` schema (PackageStore):
```jsonc
{
  "type": "crt.TemplateList",
  "items": "$CopilotQuickActions",
  "direction": "row",
  "gap": 8,
  "template": [
    {
      "type": "crt.Button",
      "name": "$CopilotQuickActions.Code",
      "caption": "$CopilotQuickActions.Name",
      "size": "large",
      "color": "outline",
      "clicked": {
        "request": "crt.CopilotActionRequest",
        "params": {
          "prompt": "$CopilotQuickActions.Name",
          "promptCode": "$CopilotQuickActions.Code",
          "useCurrentSession": true
        }
      },
      "disabled": "$NavigationStateIsLoading"
    }
  ],
  "classes": ["copilot-actions-list"]
}
```

---

## 7. Common pitfalls

- **Using `propertyName: "items"` for template children.** Children must be nested inside the `template` array in `values`, not inserted with separate `insert` ops using `propertyName: "items"`.
- **`items` is not a `BaseViewModelCollection`.** `crt.TemplateList` subscribes to `items.changed` for reactive updates; passing a plain array means the list does not refresh when the data changes.
- **`gap` as a string keyword on older schemas.** The registry type is `number | SizeEnum`; if you pass a raw pixel number (e.g. `8`) it is used as-is. Prefer SizeEnum keywords (`"small"`, `"medium"`) for theme consistency.
- **Bindings inside `template` use the wrong path.** Template item bindings must use the collection attribute path (`"$<collectionAttr>.<columnCode>"`), not a direct attribute reference.
- **Forgetting `name` on template items.** Template items that need unique keys for the runtime should set `name` to a per-item discriminator (e.g. `"$Collection.Code"`).

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.TemplateList"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `items` is a `BaseViewModelCollection` attribute.
- [ ] `direction` is `"row"`, `"column"`, `"row-reverse"`, or `"column-reverse"`.
- [ ] `gap` is a `SizeEnum` keyword or a pixel number.
- [ ] `template` array contains at least one child view element configuration.
- [ ] Bindings inside `template` use `"$<collectionAttr>.<columnCode>"` paths.
