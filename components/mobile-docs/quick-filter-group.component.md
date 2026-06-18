# How to Add a QuickFilterGroup (`crt.QuickFilterGroup`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.QuickFilterGroup` into a mobile page schema.
> Group of quick filter chips displayed in the mobile page header area.

## Metadata
- **Category**: filtering
- **Container**: yes
- **Parent types**: header containers (e.g. `HeaderContainer`)
- **Typical children**: `crt.QuickFilter`

---
## 1. Mental model
A QuickFilterGroup is a horizontal strip of filter chips rendered in the mobile page header. It acts as the sole container for `crt.QuickFilter` entries. Each child QuickFilter represents one filter criterion. Adding the group first, then inserting individual QuickFilters inside it, is the standard workflow.

---
## 2. Clio operation
Insert the group into a header container, then add `crt.QuickFilter` children inside it:
```jsonc
{
  "operation": "insert",
  "name": "FiltersGroup",
  "values": {
    "type": "crt.QuickFilterGroup",
    "items": []
  },
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.QuickFilterGroup` are in
`ComponentRegistry.json` under `componentType: "crt.QuickFilterGroup"`.

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `_filterOptions` | object | Internal filter options propagated to child `crt.QuickFilter` items by the platform filter infrastructure. |

---
## 4. Copy-paste minimal example
```jsonc
// Step 1 – add the group
{
  "operation": "insert",
  "name": "FiltersGroup",
  "values": {
    "type": "crt.QuickFilterGroup",
    "items": []
  },
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0
}

// Step 2 – add a filter chip inside the group
{
  "operation": "insert",
  "name": "StatusFilter",
  "values": {
    "type": "crt.QuickFilter",
    "filterType": "Status",
    "config": { "caption": "Status" },
    "valueChange": { "request": "crt.UpdateDataRequest" }
  },
  "parentName": "FiltersGroup",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
**Only `crt.QuickFilter` entries should go in `items`.** Inserting other component types into the group's `items` array is not supported and will not render correctly.

**Create the group before adding individual filters.** QuickFilter chips require a QuickFilterGroup parent to render and function properly.
