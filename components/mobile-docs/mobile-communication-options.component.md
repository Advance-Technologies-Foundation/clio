# How to Add a Communication Options Widget (`crt.CommunicationOptions`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.CommunicationOptions` into a mobile page schema.
> Renders a list of clickable communication links (call, email, SMS) for the contact or account
> bound through a collection data attribute.

## Metadata
- **Category**: interactive
- **Container**: false
- **Parent types**: any layout container (e.g. `crt.GridContainer`, `crt.DetailsGrid`)
- **Typical children**: none

---
## 1. Mental model
`crt.CommunicationOptions` shows all communication entries (phone numbers, emails, etc.) for a
record as tappable rows. The component reads from a dedicated child data source that is
auto-created by the designer shell when you drop the element onto the canvas. You only need to
wire the right column names so the component can identify the type, value, and owning record for
each row.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "CommOptions",
  "values": {
    "type": "crt.CommunicationOptions",
    "items": "$PDS_CommunicationOptions",
    "typeColumnName": "CommunicationType",
    "numberColumnName": "Number",
    "masterRecordColumnName": "ContactId",
    "masterRecordColumnValue": "$Id"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.CommunicationOptions` are in
`ComponentRegistry.json` under `componentType: "crt.CommunicationOptions"`.

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `visible` | `string` | Visibility binding — show/hide the widget conditionally. |

Key inputs (all `@CrtInput` on `CrtBaseCommunicationOptionsComponent`):

| Property | Type | Description |
|---|---|---|
| `items` | `BaseViewModelCollection` | Data attribute bound to the communication-options collection data source. |
| `typeColumnName` | `string` | Column name that holds the communication type (e.g. `"CommunicationType"`). |
| `numberColumnName` | `string` | Column name that holds the number or value (e.g. `"Number"`). |
| `masterRecordColumnName` | `string` | Foreign-key column pointing to the owner record (e.g. `"ContactId"`). |
| `masterRecordColumnValue` | `string` | Binding to the owner record's primary key (e.g. `"$Id"`). |
| `displayFormatColumnName` | `string` | Column used to pick the display template per entry. |
| `primaryColumnName` | `string` | Primary key column of the communication row. |
| `columnsCount` | `number` | Number of grid columns to render entries in (default `2`). |
| `readonly` | `boolean` | Puts all entries into read-only mode. |
| `labelPosition` | `LabelPositionType` | Label alignment (`'auto'`, `'top'`, `'left'`, etc.). |
| `showNoDataPlaceholder` | `boolean` | Show placeholder image when collection is empty (default `true`). |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "CommOptions",
  "values": {
    "type": "crt.CommunicationOptions",
    "items": "$PDS_CommunicationOptions",
    "typeColumnName": "CommunicationType",
    "numberColumnName": "Number",
    "masterRecordColumnName": "ContactId",
    "masterRecordColumnValue": "$Id"
  },
  "parentName": "DetailsGrid",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **Missing data source**: the designer auto-creates the backing `crt.EntityDataSource` scoped to
  the view element. If you insert via Clio without going through the designer, create the data
  source manually and bind `items` to its view-model attribute.
- **Wrong column names**: `typeColumnName`, `numberColumnName`, and `masterRecordColumnName` must
  match the actual entity column names in the backing schema (e.g. `ContactCommunication`), not
  the display captions.
- **Mismatched master record value**: `masterRecordColumnValue` must resolve to the record GUID
  at runtime (e.g. `"$Id"`). Binding it to a display value or a nested path will break filtering.
