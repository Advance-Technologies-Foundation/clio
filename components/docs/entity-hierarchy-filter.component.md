# How to Add an Entity Hierarchy Filter (`crt.EntityHierarchyFilter`) to a Freedom UI Page

> Audience: code agent inserting a `crt.EntityHierarchyFilter` into a Creatio Freedom UI page schema.
>
> `crt.EntityHierarchyFilter` is a tree-based sidebar filter that shows a hierarchy of nodes and lets the user
> drill down to filter the page's main data. It also supports a secondary specification-filters tab. The
> create command inserts the element and wires `_filterOptions` inline on the view element config — no
> separate `modelConfigDiff` or `viewModelConfigDiff` inserts are needed.

## Metadata

- **Category**: filter
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, sidebar/catalog layout containers
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.EntityHierarchyFilter"`, `nodesConfig: []`, and an inline `_filterOptions` object. **Always present.** |
| 2 | `handlers` | Request handlers that load the node tree and respond to node selection. **Required to make the filter functional.** |

The `_filterOptions` object wires the filter output to the page's data-grid datasource. It is set as an
inline property on the view element `values` — not via a separate `viewModelConfigDiff` merge.

### 1.1 How `_filterOptions` works

```jsonc
"_filterOptions": {
  "expose": [
    {
      "attribute": "EntityHierarchyFilter_Filters",
      "converters": [{ "converter": "crt.ToHierarchyFiltersConverter" }]
    }
  ],
  "from": [
    "EntityHierarchyFilter_SelectedNode",
    "EntityHierarchyFilter_SpecificationFilters"
  ]
}
```

- `expose` — the attribute whose value is forwarded to the grid's `filter` input after conversion.
- `from` — the internal attributes the filter aggregates to compute `Filters`.

### 1.2 `buildHierarchyAttributeNames` — all 14 auto-generated attribute names

The create command calls `buildHierarchyAttributeNames(elementName)` to generate the following attributes
(using `ElementName` as the example):

```
ElementName_SpecificationFilters
ElementName_Filters
ElementName_SelectedNode
ElementName_NodesConfig
ElementName_Nodes
ElementName_ExpandedNodes
ElementName_SpecificationFiltersConfiguration
ElementName_SelectedTabInd
ElementName_PredefinedFilter
ElementName_SelectedNodeId
ElementName_SelectedNodeId_Profile
ElementName_SchemaName
ElementName_SearchValue
ElementName_SearchResultNodes
```

These are managed by the platform; you do not declare them manually in `viewModelConfigDiff`.

### 1.3 Naming convention

```
EntityHierarchyFilter_<id>         // view element name
EntityHierarchyFilter_<id>_Filters // exposed filter attribute (used in grid's filter input)
```

---

## 2. Step-by-step recipe

### 2.1 Insert the filter element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EntityHierarchyFilter",
  "parentName": "CatalogFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EntityHierarchyFilter",
    "nodesConfig": [],
    "_filterOptions": {
      "expose": [
        {
          "attribute": "EntityHierarchyFilter_Filters",
          "converters": [{ "converter": "crt.ToHierarchyFiltersConverter" }]
        }
      ],
      "from": [
        "EntityHierarchyFilter_SelectedNode",
        "EntityHierarchyFilter_SpecificationFilters"
      ]
    }
  }
}
```

### 2.2 Add required handlers

The filter fires `nodeClick` when the user selects a node. Wire it to a `crt.LoadDataRequest` (or a custom
request) that reloads the grid with the new filter applied.

```jsonc
{
  "request": "crt.HierarchyFilterNodeClickRequest",
  "handler": async (request, next) => {
    // update page attribute or reload grid data
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EntityHierarchyFilter` are in `ComponentRegistry.json` under
`componentType: "crt.EntityHierarchyFilter"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// HierarchyFilterNode — tree node
interface HierarchyFilterNode {
  id: string;
  caption: string;
  isLastLevel?: boolean;
  isLastItem?: boolean;
  children?: HierarchyFilterNode[];
}

// SpecificationFilter — secondary specification filter config
interface SpecificationFilter {
  name: string;
  caption: string;
  values?: unknown[];
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — from BaseEntityCatalogPage.js
{
  "operation": "insert",
  "name": "EntityHierarchyFilter",
  "parentName": "CatalogFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EntityHierarchyFilter",
    "nodesConfig": [],
    "_filterOptions": {
      "expose": [
        {
          "attribute": "EntityHierarchyFilter_Filters",
          "converters": [
            {
              "converter": "crt.ToHierarchyFiltersConverter"
            }
          ]
        }
      ],
      "from": [
        "EntityHierarchyFilter_SelectedNode",
        "EntityHierarchyFilter_SpecificationFilters"
      ]
    }
  }
}
```

---

## 6. Driving from page state

`nodes`, `selectedNodeId`, `expandedItems`, and `searchResultNodes` are all bound to platform-managed
attributes (generated by `buildHierarchyAttributeNames`). You do not need to declare these in
`viewModelConfigDiff` — the platform auto-creates them.

To load data on initial render, trigger `loadNext` in a `crt.HandlerChainInitialized` handler.

---

## 7. Common pitfalls

1. **Declaring attribute names manually in `viewModelConfigDiff`** — the 14 hierarchy attributes are
   platform-managed; duplicating them causes conflicts.
2. **Omitting `nodesConfig: []`** — required even when starting with no configuration; without it the
   runtime cannot bootstrap the filter.
3. **Forgetting `_filterOptions`** — without this the filter selection never reaches the grid; the tree
   renders but clicking a node has no effect on the data.
4. **Wrong `"from"` attribute names** — the attribute names in `_filterOptions.from` must exactly match
   those generated by `buildHierarchyAttributeNames`; a typo silently breaks filtering.
5. **Placing inside a `crt.GridContainer` without `layoutConfig`** — if the parent is a grid container,
   add `layoutConfig: { row, column, rowSpan, colSpan }` to the values.
6. **Using `displayMode` without a handler for the selected mode** — `EntityHierarchyDisplayMode.ByParentDisplayMode`
   requires the node data to be structured accordingly; mismatched mode and data causes render issues.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.EntityHierarchyFilter"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `nodesConfig: []` present in `values`.
- [ ] `_filterOptions` with correct `expose` and `from` arrays is present in `values`.
- [ ] `expose[0].attribute` matches `"${elementName}_Filters"` exactly.
- [ ] `from` array contains `"${elementName}_SelectedNode"` and `"${elementName}_SpecificationFilters"`.
- [ ] Handlers are set up for `loadNext` (initial and pagination) and `nodeClick` (filter application).
- [ ] If inside a `crt.GridContainer`, `layoutConfig` is present.
