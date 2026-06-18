# How to Add a QuickFilter (`crt.QuickFilter`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.QuickFilter` into a mobile page schema.
> Quick filter chip for applying a named data filter to a list or gallery.

## Metadata
- **Category**: filtering
- **Container**: no
- **Parent types**: `crt.QuickFilterGroup`
- **Typical children**: none

---
## 1. Mental model
A QuickFilter renders as an interactive chip in the mobile page header area. Each chip represents one filter criterion. Filters are grouped inside a `crt.QuickFilterGroup`. When tapped, the chip opens a picker and fires `valueChange` with the selected value.

---
## 2. Clio operation
Typically inserted inside a `crt.QuickFilterGroup`:
```jsonc
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
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.QuickFilter` are in
`ComponentRegistry.json` under `componentType: "crt.QuickFilter"`.

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `_filterOptions` | object | Internal filter options wired by the platform. Example: `{ expose: [], from: 'QuickFilter_Value' }`. Set only when integrating with platform-managed filter data sources. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "TypeFilter",
  "values": {
    "type": "crt.QuickFilter",
    "filterType": "Type",
    "config": { "caption": "Type" },
    "valueChange": { "request": "crt.UpdateDataRequest" }
  },
  "parentName": "FiltersGroup",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
**Always wrap QuickFilters in a `crt.QuickFilterGroup`.** Inserting a QuickFilter directly into Scaffold items or a grid will not render correctly.

**`valueChange` requires a request binding.** Without it the filter value is updated locally but the list data is not refreshed.
