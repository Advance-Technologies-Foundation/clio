# How to Add a Feed (`crt.Feed`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.Feed` into a mobile page schema.
> Renders an ESN (Enterprise Social Network) activity/message feed panel linked to a record on a mobile page.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.TabPanel` tab body, `crt.GridContainer`, any layout container
- **Typical children**: none

---
## 1. Mental model
`crt.Feed` displays the social feed (comments, likes, mentions) associated with a specific record.
Set `entitySchemaName` to the entity whose feed you want to show, and bind `primaryColumnValue`
to the record ID (typically `"$Id"`). The feed is loaded from the ESN subsystem using these two
values together.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "ActivityFeed",
  "values": {
    "type": "crt.Feed",
    "entitySchemaName": "Activity",
    "primaryColumnValue": "$Id"
  },
  "parentName": "FeedTab",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Feed` are in
`ComponentRegistry.json` under `componentType: "crt.Feed"`.

Key inputs:
| Property | Type | Description |
|---|---|---|
| `primaryColumnValue` | string (binding) | Record ID binding for ESN messages (e.g. `"$Id"`). |
| `entitySchemaName` | string | Entity schema name for the feed (e.g. `"Activity"`, `"Contact"`). |
| `dataSourceName` | string | Data source name used by the feed (optional; auto-resolved when omitted). |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "ActivityFeed",
  "values": {
    "type": "crt.Feed",
    "entitySchemaName": "Activity",
    "primaryColumnValue": "$Id"
  },
  "parentName": "FeedTab",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **`primaryColumnValue` defaults to `"$Id"`** via the toolbar config. Always confirm the
  binding resolves to the correct record ID in the page view-model.
- **`entitySchemaName` is case-sensitive.** Use the exact Pascal-case schema name (e.g.
  `"Contact"`, not `"contact"`).
- **Only one feed per page is recommended.** Placing multiple `crt.Feed` instances on the same
  page can cause subscription conflicts in the ESN subsystem.
