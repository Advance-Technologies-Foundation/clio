# How to Add a Filter Builder Source (`crt.FilterBuilderSource`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FilterBuilderSource` into a Creatio Freedom UI page schema.
>
> A `crt.FilterBuilderSource` is an advanced filter panel that lets users build complex filter
> expressions. It exposes its current filter through `_filterOptions` so that other datasource
> bindings can consume the composed filter. Adding one only requires a single `viewConfigDiff`
> insert — no `modelConfigDiff` or `viewModelConfigDiff` are needed.

## Metadata

- **Category**: filter
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — what you edit depends on the wiring pattern

Two patterns exist. Choose one based on whether the filter state must **persist** into a page attribute.

### Pattern A — filter output only (`_filterOptions`)

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | `insert` op with `type: "crt.FilterBuilderSource"`, `schemaName`, and `_filterOptions`. |

No `viewModelConfigDiff` required. The `_filterOptions` block wires the filter output to datasource
attribute bindings without a separate attribute declaration.

### Pattern B — attribute-backed persistence (`filterStorage`)

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | `insert` op with `filterStorage: "$<attr>"` and `schemaName: "$<nameAttr>"` (or literal). |
| 2 | `viewModelConfigDiff` | Attribute for the stored filter JSON (`PDS.<FilterField>`) and optionally for the schema name. |

Use this pattern when the filter state must survive page navigation or be stored in the data model
(e.g. segment entry/exit conditions). `filterStorage` is a `@CrtInput()`-decorated property that
accepts the resolved attribute value as a JSON string and feeds it into the same parsing path as
`data`.

### 1.1 Naming convention

```
FilterBuilderSource_<id>              // view element name; <id> is any short unique slug
FilterBuilderSource_<id>_Value        // internal filter binding name (used in _filterOptions.from)
FilterBuilderSource_<id>_Filter       // exposed attribute name (used in _filterOptions.expose[].attribute)
```

---

## 2. Step-by-step recipe

### 2.1 Pattern A — filter output only (`_filterOptions`)

```jsonc
{
  "operation": "insert",
  "name": "FilterBuilderSource_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FilterBuilderSource",
    "schemaName": "Contact",
    "applyFiltersAutomatically": true,
    "stretch": true,
    "_filterOptions": {
      "expose": [
        {
          "attribute": "FilterBuilderSource_abc123_Filter",
          "converters": [{ "converter": "crt.FilterBuilderAttributeConverter" }]
        }
      ],
      "from": "FilterBuilderSource_abc123_Value"
    },
    "layoutConfig": { "alignSelf": "stretch", "width": 328 },
    "visible": true
  }
}
```

The `_filterOptions` block is auto-generated when using the create command. When adding manually:
- `from` must be `<name>_Value` (the component's internal binding key)
- `expose[].attribute` is the attribute name consumed by datasource bindings elsewhere on the page

### 2.2 Pattern B — attribute-backed persistence (`filterStorage`)

**`viewConfigDiff` entry:**

```jsonc
{
  "operation": "insert",
  "name": "EntryRulesFilter",
  "parentName": "GridContainer_abc",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FilterBuilderSource",
    "stretch": true,
    "applyFiltersAutomatically": true,
    "closeButtonVisible": false,
    "schemaName": "$PDS_EntitySchemaName",
    "filterStorage": "$PDS_EntryFilterData",
    "visible": true,
    "_filterOptions": { "expose": [], "from": "EntryRulesFilter_Value" },
    "layoutConfig": { "column": 1, "colSpan": 2, "row": 2, "rowSpan": 3 }
  }
}
```

**`viewModelConfigDiff` additions** (same `merge` op as other page attributes):

```jsonc
// Filter storage attribute — holds the serialized FilterGroup JSON
"PDS_EntryFilterData": {
  "modelConfig": { "path": "PDS.EntryFilterData" }
},
// Schema name attribute — resolved plain string bound to schemaName input
"PDS_EntitySchemaName": {
  "modelConfig": { "path": "PDS.EntitySchema.Name" }
}
```

Notes:
- `filterStorage` receives the **resolved attribute value** (the JSON string) — not the binding expression.
- The framework wires `filterStorage: "$attr"` to the component's `filterStorage` `@CrtInput()`,
  which internally delegates to the `data` setter and parses the JSON via `FilterBuilderDataHelper.fromJson()`.
- `schemaName` can also be an attribute binding (`"$PDS_EntitySchemaName"`) when the schema is stored
  in the data model rather than hardcoded.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FilterBuilderSource` are in `ComponentRegistry.json` under `componentType: "crt.FilterBuilderSource"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`data` accepts three forms, all normalized internally:

- a `FilterBuilderData` object (`{ elements, group, elementsData? }`),
- a flat `FilterBuilderElement[]` array (group reconstructed from elements),
- a `string` containing the JSON output of `FilterGroup.toJson()`. Malformed JSON leaves the panel empty rather than throwing.

`filterStorage` accepts a `string | null` — the resolved value of a page attribute that stores a
serialized `FilterGroup.toJson()` payload. It is a `@CrtInput()`-decorated input; the framework wires
`filterStorage: "$<attr>"` directly to the component. Internally it delegates to the `data` setter,
so the same JSON parsing applies. Use `filterStorage` (not `data`) when the filter state is persisted
in the data model.

`changed` emits `FilterBuilderData`:

```ts
interface FilterBuilderData {
  elements: FilterBuilderElement[];
  group: FilterGroup;
  elementsData?: Record<string, unknown>;
}
```

`_filterOptions` shape:

```ts
interface FilterOptions {
  expose: Array<{
    attribute: string;                      // attribute name for datasource binding
    converters: Array<{ converter: string }>;
  }>;
  from: string;                             // '<name>_Value'
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FilterBuilderSource_8i555cb",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "applyFiltersAutomatically": true,
    "type": "crt.FilterBuilderSource",
    "stretch": true,
    "_filterOptions": {
      "expose": [
        {
          "attribute": "FilterBuilderSource_8i555cb_Filter",
          "converters": [{ "converter": "crt.FilterBuilderAttributeConverter" }]
        }
      ],
      "from": "FilterBuilderSource_8i555cb_Value"
    },
    "layoutConfig": { "alignSelf": "stretch", "width": 328 },
    "visible": true,
    "schemaName": "Contact"
  }
}
```

---

## 6. Driving from page state

`visible` is `propertyBindable` — bind to a `$Attribute` to show/hide the filter panel at runtime.
The `crt.FilterBuilderToggler` is the canonical companion component that toggles this `visible`
state via `visibilityChanged`.

```jsonc
"visible": "$FilterBuilderSource_visible"
```

---

## 7. Common pitfalls

1. **`_filterOptions.from` not matching `<name>_Value`** — the filter binding will not propagate to datasources; always keep `from` as `<elementName>_Value`.
2. **`schemaName` wrong or empty** — the filter builder cannot load available columns and renders blank; set to the exact entity schema name.
3. **`applyFiltersAutomatically: false` without a toggler** — the Apply button appears but the filter change does not propagate until the user clicks it; use `true` for inline search experiences.
4. **`layoutConfig.width` omitted** — the component has a design default of `328px`; without a width constraint it may stretch to fill its parent unexpectedly.
5. **`stretch: true` without a flex parent that fills height** — `stretch` relies on `alignSelf: stretch` in the `layoutConfig`; the parent container must allow stretch alignment.
6. **Not pairing with a `crt.FilterBuilderToggler`** — the filter panel is visible by default unless controlled; add a toggler to give users a way to collapse it.
7. **Using `filterStorage` without a `viewModelConfigDiff` attribute** — `filterStorage: "$attr"` requires the attribute to be declared in `viewModelConfigDiff`; without it the binding resolves to `undefined` and the panel always starts empty.
8. **Using `data` instead of `filterStorage` for attribute-backed storage** — both accept a JSON string, but `filterStorage` is the correct input for attribute persistence. `data` is for programmatic or one-time data injection.

---

## 8. Quick checklist

**Always:**
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FilterBuilderSource"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `schemaName` set to the correct entity schema name (literal or `"$<attr>"`).
- [ ] `applyFiltersAutomatically` set based on whether filters should propagate on each change (`true`) or only on Apply (`false`).
- [ ] `layoutConfig.width: 328` set (the design default).
- [ ] Paired `crt.FilterBuilderToggler` has `viewElement` pointing to this element's `name`.

**Pattern A (`_filterOptions`) only:**
- [ ] `_filterOptions.from` is `<name>_Value`.
- [ ] `_filterOptions.expose[].attribute` matches the attribute name used by datasource bindings.

**Pattern B (`filterStorage`) only:**
- [ ] `filterStorage: "$<attr>"` in `viewConfigDiff`.
- [ ] Corresponding attribute declared in `viewModelConfigDiff` with the correct `modelConfig.path`.
- [ ] If `schemaName` is also attribute-bound, its attribute is declared in `viewModelConfigDiff` too.
