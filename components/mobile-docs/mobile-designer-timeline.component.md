# How to Add a Timeline (`crt.Timeline`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Timeline` into a mobile page schema.
> Renders a chronological timeline view showing activities, calls, emails, feed messages, and files for a record.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.TabPanel` tab body, `crt.Scaffold`, any layout container
- **Typical children**: none (tile types are configured via `items` array, not as child elements)

---
## 1. Mental model
`crt.Timeline` aggregates multiple activity types (Call, Email, Feed, SysFile) from a master
record into a single chronological feed. Set `masterEntity` to the entity schema name of the
record being viewed, and bind `masterSchemaId` to the record ID binding (typically `"$Id"`).
Configure `items` as an array of tile type descriptors to control which activity types appear.

On mobile the supported tile types are: `Call`, `Email`, `Feed`, `SysFile` (plus `null` for
unconfigured entries).

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "RecordTimeline",
  "values": {
    "type": "crt.Timeline",
    "masterEntity": "Contact",
    "masterSchemaId": "$Id",
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Timeline` are in
`ComponentRegistry.json` under `componentType: "crt.Timeline"`.

Key inputs:
| Property | Type | Description |
|---|---|---|
| `items` | array | Timeline tile configurations; each entry describes one activity type to include. |
| `masterSchemaId` | string (binding) | Master record ID binding (e.g. `"$Id"`). Defaults to `"$Id"` via toolbar config. |
| `masterEntity` | string | Master entity name binding (e.g. `"Contact"`, `"Activity"`). |

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `caption` | string | Timeline caption resource string shown as a heading. |
| `label` | string | Timeline label resource string (subtitle or section label). |
| `filterValues` | string | Attribute binding for the active filter values on the timeline. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "RecordTimeline",
  "values": {
    "type": "crt.Timeline",
    "masterEntity": "Contact",
    "masterSchemaId": "$Id",
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **Mobile supports only 4 tile types**: `Call`, `Email`, `Feed`, `SysFile`. Adding other
  desktop timeline types (e.g. `Meeting`) will be filtered out silently by
  `filterAvailableItems` and will not appear.
- **`masterSchemaId` defaults to `"$Id"`** via toolbar config. Verify the binding resolves to
  the correct column in the page view-model when working on pages with non-standard primary keys.
- **`items: []` is valid** as the starting state; tile types are added through the designer
  properties panel after inserting the component. Do not pass random objects into `items`
  without matching the tile descriptor shape from the registry.
