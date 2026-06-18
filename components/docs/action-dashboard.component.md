# How to Add an Action Dashboard (`crt.ActionDashboard`) to a Freedom UI Page

> Audience: code agent inserting `crt.ActionDashboard` into a Creatio Freedom UI page schema.
> Renders a panel of quick-action buttons (Call, Email, Feed, Task) for the entity currently open on the
> page; delegates actual action handling to the separately loaded `ActionDashboardComponent` AMD module.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.ActionDashboard"`. **Always present.** |

`crt.ActionDashboard` is **view-only** — no datasource, no attributes, no handlers required for basic
usage. The component auto-resolves the current entity from the page context. There is no create command
that touches additional sections.

> **Important**: schemas that use this component must declare `"ActionDashboardComponent"` in the
> AMD `define([...])` dependency array (the `clientSchemaDeps` array in the designer item config).
> Without it the runtime cannot load the action panel logic.

### 1.1 Naming convention
```
ActionDashboard_<id>    // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ActionDashboard",
  "values": {
    "type": "crt.ActionDashboard"
  },
  "parentName": "CardContentContainer",
  "propertyName": "items",
  "index": 0
}
```

For a customized variant with a title and a restricted action set:

```jsonc
{
  "operation": "insert",
  "name": "ActionDashboard",
  "values": {
    "type": "crt.ActionDashboard",
    "title": "#ResourceString(ActionDashboard_title)#",
    "allowedActions": ["Call", "Email"],
    "fitContent": true
  },
  "parentName": "CardContentContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ActionDashboard` are in `ComponentRegistry.json` under `componentType: "crt.ActionDashboard"`. This
guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real PackageStore usage
{
  "operation": "insert",
  "name": "ActionDashboard",
  "values": {
    "type": "crt.ActionDashboard"
  },
  "parentName": "CardContentContainer",
  "propertyName": "items",
  "index": 0
}
```

The schema's AMD wrapper must include the dependency:
```js
define("MyPage", ["ActionDashboardComponent"], function() { ... });
```

---

## 7. Common pitfalls

1. **Missing `ActionDashboardComponent` AMD dependency** — the schema will load without errors but the action buttons will not render because the module is resolved lazily. Always list it in `clientSchemaDeps` / the AMD deps array.
2. **Using this component inside a base template that does not inherit `clientSchemaDeps`** — child schemas that extend the template must re-declare the dependency or the module resolver may not inject it.
3. **Setting `allowedActions` to an empty array** — the dashboard renders but all action buttons are hidden, which looks broken. Omit the property to use the platform default set.
4. **Putting `entitySchemaName` / `primaryColumnValue` manually** — these are auto-populated by the platform from the page's primary datasource. Override only when the dashboard must target a different entity than the page itself.
5. **Expecting `fitContent: false` to fill the row** — the component only shrinks to fit content when `fitContent: true` (the default). Set it to `false` and pair with a flex parent's `grow: 1` if you need the panel to stretch.
6. **Using `crt.DesignTimeActionDashboard` in runtime schemas** — that type is the designtime overlay only; always use `"crt.ActionDashboard"` in the `values.type` field.
7. **Feature flag `EnableActionDashboardDesignerItem`** — the toolbar item is hidden unless this feature is enabled. The runtime component works regardless of the feature flag; only the designer palette entry is gated.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ActionDashboard"`, unique `name`, valid `parentName`, `propertyName: "items"`, and a valid `index`.
- [ ] `"ActionDashboardComponent"` listed in the schema AMD deps array.
- [ ] `allowedActions` is a non-empty string array if provided; omit to use the platform default.
- [ ] `entitySchemaName` / `primaryColumnValue` set only when targeting an entity other than the page primary datasource.
- [ ] `fitContent` left at default (`true`) unless explicit full-width stretching is needed.
