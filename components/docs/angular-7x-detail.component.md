# How to Add an Angular 7.x Detail (`crt.Angular7XDetail`) to a Freedom UI Page

> Audience: code agent inserting `crt.Angular7XDetail` into a Creatio Freedom UI page schema.
> An adapter component that embeds a legacy AngularJS-based detail module inside a Freedom UI page,
> bridging old-style module configuration and default-values injection into the Freedom UI view tree.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Angular7XDetail"` and `config`/`defaultValues` bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for the `config` and `defaultValues` inputs. |

`crt.Angular7XDetail` has no inputs registered under `@CrtInput` and no create command — the component has
no `@CrtInterfaceDesignerItem` annotation and cannot be dragged from the designer palette.

### 1.1 Naming convention
```
Angular7XDetail_<id>    // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Declare attributes in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "MyDetailConfig": {
      "value": null
    },
    "MyDetailDefaultValues": {
      "value": null
    }
  }
}
```

In real schemas `defaultValues` is typically derived from other attributes via a `converter`:

```jsonc
"MyDetailDefaultValues": {
  "from": ["RelatedColumnA", "RelatedColumnB"],
  "converter": "crt.ToObjectCollectionWithMapping: $MyDetailDefaultValuesMapping"
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MyDetail",
  "values": {
    "type": "crt.Angular7XDetail",
    "layoutConfig": {
      "column": 1,
      "row": 1,
      "colSpan": 1,
      "rowSpan": 1
    },
    "defaultValues": "$MyDetailDefaultValues",
    "config": "$MyDetailConfig"
  },
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Angular7XDetail` are in `ComponentRegistry.json` under `componentType: "crt.Angular7XDetail"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real PackageStore usage (OpportunityConditions_FormPage)
{
  "operation": "insert",
  "name": "OpportunityConditionDetail",
  "values": {
    "type": "crt.Angular7XDetail",
    "layoutConfig": {
      "column": 1,
      "row": 1,
      "colSpan": 1,
      "rowSpan": 1
    },
    "defaultValues": "$OpportunityConditionDetailDefaultValues",
    "config": "$OpportunityConditionDetailConfig"
  },
  "parentName": "OpportunityConditionContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Using this component for new development** — `crt.Angular7XDetail` is an adapter for legacy AngularJS modules. Prefer native Freedom UI components for all new functionality.
2. **`config` not set** — without a valid config object the AngularJS module cannot initialize; the detail renders blank.
3. **`defaultValues` computed too early** — if converter attributes are resolved before the page datasource loads, `defaultValues` can be `null` on the first pass. Make sure the converter's `from` sources are loaded before the detail is visible.
4. **`layoutConfig` missing** — required when the parent is a `crt.GridContainer`; without it the detail is placed in column 1, row 1 by default which may overlap other elements.
5. **Module ID suffix** — some legacy schemas use an `attributeModuleIdSuffix` attribute (e.g. `"_module_MyModule"`) to namespace the AngularJS scope. Check the original module documentation for the required suffix value.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Angular7XDetail"`, unique `name`, valid `parentName`.
- [ ] `config` bound to an attribute that resolves to the legacy module configuration object.
- [ ] `defaultValues` bound to an attribute derived from the page datasource (often via a converter).
- [ ] `layoutConfig` provided when parent is a `crt.GridContainer`.
- [ ] Legacy AngularJS module is available in the page dependencies.
