# How to Add a Sort Control (`crt.Sort`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Sort` into a mobile page schema.
> Sort control button for record lists. Opens a sort order picker on tap.

## Metadata
- **Category**: filtering
- **Container**: no
- **Parent types**: header containers (e.g. `HeaderContainer`)
- **Typical children**: none

---
## 1. Mental model
A Sort control renders as a button in the mobile page header area. When tapped, it opens a picker listing available sort options. After the user selects an option, the control fires `valueChange` to update the list order. Provide `items` with the available sort fields and `defaultValue` to set the initial sort.

---
## 2. Clio operation
Insert into a header container:
```jsonc
{
  "operation": "insert",
  "name": "SortControl",
  "values": {
    "type": "crt.Sort",
    "items": [],
    "defaultValue": "CreatedOn",
    "valueChange": { "request": "crt.UpdateDataRequest" }
  },
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Sort` are in
`ComponentRegistry.json` under `componentType: "crt.Sort"`.

Key inputs:
| Input | Description |
|-------|-------------|
| `items` | Array of sort option items available in the picker. |
| `value` | Current sort value binding. |
| `valueChange` | Request descriptor fired when the sort selection changes. |
| `defaultValue` | Default sort column/field applied on page open. |
| `iconPosition` | Icon position relative to label (`left` or `right`). |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "SortControl",
  "values": {
    "type": "crt.Sort",
    "items": [],
    "defaultValue": "CreatedOn",
    "valueChange": { "request": "crt.UpdateDataRequest" }
  },
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
**`valueChange` requires a request binding.** Without it the sort selection updates locally but the list data is not re-sorted from the server.

**Provide `defaultValue` matching a column name.** If `defaultValue` is empty or points to a non-existent column, the list may open with an undefined sort order.
