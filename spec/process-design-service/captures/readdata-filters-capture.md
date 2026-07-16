# Capture — "Read data" element with Data source filters (designer-built)

> Source: designer-built process `UsrClioBpInitParams1` (schema UId `1a0fe978-c3c1-42c2-9879-962c56e61c9e`)
> on a live test stand, pasted into the ENG-91842 session. This is the authoritative reference for the
> `DataSourceFilters` serialization. Screenshot showed: Read data → Contact, filtered by
> `Account = Perform task.Account AND Address = 2222 AND Age = 333`, sorted by Full name Ascending.

## The element parameter that carries filters

The "Read data" element (`ReadDataUserTask1`, UId `45f0dc74-…`) has an element parameter
**`DataSourceFilters`** (UId `b025cdff-…`, DataValueType `394e160f-c8e0-46fa-9c0d-75d97e9e9169` =
`MetaDataTextDataValueTypeUId` — a plain serialized-string store). Its `SourceValue` is
`Source=1` (ConstValue) and `Value` is this wrapper JSON (string):

```jsonc
{
  "className": "Terrasoft.FilterGroup",
  "serializedFilterEditData": "<escaped FilterGroup JSON — UI re-edit representation>",
  "dataSourceFilters": "<escaped FilterGroup JSON — runtime representation>"
}
```

The same element-param value is mirrored in `schema.Mappings` (the platform auto-syncs it when the
`SourceValue` is assigned — see state doc), so we only assign `elementParameter.SourceValue` (one write).

## `serializedFilterEditData` (UI re-edit representation — decoded)

Full fidelity: includes `className`, per-item `key`, `leftExpressionCaption`, `referenceSchemaName`,
`isAggregative`, and parameter `displayValue`.

```jsonc
{
  "className": "Terrasoft.FilterGroup",
  "items": {
    "4f3a4115-7721-49d2-9102-008e2448231d": {            // Account (lookup) = Perform task.Account
      "className": "Terrasoft.InFilter",                  // lookup equality → InFilter (filterType 4)
      "filterType": 4, "comparisonType": 3,               // 4=In, 3=Equal
      "isEnabled": true, "trimDateTimeParameterToDate": false,
      "leftExpression": { "className": "Terrasoft.ColumnExpression", "expressionType": 0, "columnPath": "Account" },
      "isAggregative": false, "key": "4f3a4115-…", "dataValueType": 10,  // 10 = Lookup
      "leftExpressionCaption": "Account", "referenceSchemaName": "Account",
      "rightExpressions": [                               // InFilter uses an ARRAY
        { "className": "Terrasoft.ParameterExpression", "expressionType": 2,
          "parameter": { "className": "Terrasoft.Parameter", "dataValueType": 26,  // 26 = Mapping (param ref)
            "value": {
              "value": "[IsOwnerSchema:false].[IsSchema:false].[Element:{02f3221a-…}].[Parameter:{4d2571e8-…}]",
              "displayValue": "Perform task.Account",
              "Id": "43bf5cb3-33d9-46ad-bfeb-0abe64928413" } } } ]
    },
    "5184056a-dbe5-4769-b63a-0916fc0450a8": {            // Address = "2222" (text constant)
      "className": "Terrasoft.CompareFilter",
      "filterType": 1, "comparisonType": 3,               // 1=Compare, 3=Equal
      "isEnabled": true, "trimDateTimeParameterToDate": false,
      "leftExpression": { "className": "Terrasoft.ColumnExpression", "expressionType": 0, "columnPath": "Address" },
      "isAggregative": false, "key": "5184056a-…", "dataValueType": 1,  // 1 = Text
      "leftExpressionCaption": "Address",
      "rightExpression": { "className": "Terrasoft.ParameterExpression", "expressionType": 2,
        "parameter": { "className": "Terrasoft.Parameter", "dataValueType": 1, "value": "2222" } }
    },
    "9aac0ace-c29e-4453-92c1-4bca1c0c8a84": {            // Age = 333 (integer constant)
      "className": "Terrasoft.CompareFilter",
      "filterType": 1, "comparisonType": 3,
      "isEnabled": true, "trimDateTimeParameterToDate": false,
      "leftExpression": { "className": "Terrasoft.ColumnExpression", "expressionType": 0, "columnPath": "Age" },
      "isAggregative": false, "key": "9aac0ace-…", "dataValueType": 4,  // 4 = Integer
      "leftExpressionCaption": "Age",
      "rightExpression": { "className": "Terrasoft.ParameterExpression", "expressionType": 2,
        "parameter": { "className": "Terrasoft.Parameter", "dataValueType": 4, "value": 333 } }  // numeric, not string
    }
  },
  "logicalOperation": 0,                                  // 0 = And
  "isEnabled": true, "filterType": 6,                     // 6 = FilterGroup
  "rootSchemaName": "Contact", "key": ""
}
```

## `dataSourceFilters` (runtime representation — decoded)

Same tree, **stripped** of `className`, `key`, `leftExpressionCaption`, `referenceSchemaName`,
`isAggregative`, and the parameter `displayValue`. This is the only representation the **runtime reads**
(`ProcessFilterFactory.Deserialize` → `JsonConvert.DeserializeObject<Filters>(jsonObject["dataSourceFilters"])`).

```jsonc
{
  "items": {
    "4f3a4115-…": {
      "filterType": 4, "comparisonType": 3, "isEnabled": true, "trimDateTimeParameterToDate": false,
      "leftExpression": { "expressionType": 0, "columnPath": "Account" },
      "rightExpressions": [ { "expressionType": 2, "parameter": { "dataValueType": 26,
        "value": { "value": "[IsOwnerSchema:false].[IsSchema:false].[Element:{02f3221a-…}].[Parameter:{4d2571e8-…}]",
                   "Id": "43bf5cb3-…" } } } ]
    },
    "5184056a-…": { "filterType": 1, "comparisonType": 3, "isEnabled": true, "trimDateTimeParameterToDate": false,
      "leftExpression": { "expressionType": 0, "columnPath": "Address" },
      "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 1, "value": "2222" } } },
    "9aac0ace-…": { "filterType": 1, "comparisonType": 3, "isEnabled": true, "trimDateTimeParameterToDate": false,
      "leftExpression": { "expressionType": 0, "columnPath": "Age" },
      "rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 333 } } }
  },
  "logicalOperation": 0, "isEnabled": true, "filterType": 6, "rootSchemaName": "Contact"
}
```

## Enum values (confirmed from `Terrasoft.Nui/.../sysenums.js` + `Terrasoft.Core`)

| Enum | Values |
|---|---|
| `FilterType` | None=0, **Compare=1**, IsNull=2, Between=3, **In=4**, Exists=5, **FilterGroup=6** |
| `ComparisonType` | Between=0, IsNull=1, IsNotNull=2, **Equal=3**, NotEqual=4, Less=5, LessOrEqual=6, Greater=7, GreaterOrEqual=8, StartWith=9, NotStartWith=10, Contain=11, NotContain=12, EndWith=13, NotEndWith=14, Exists=15, NotExists=16 |
| `LogicalOperatorType` | **And=0**, Or=1 |
| `ExpressionType` | **SchemaColumn=0**, Function=1, **Parameter=2**, Subquery=3, Arithmetic=4 |
| `DataValueType` (filter-relevant) | Guid=0, **Text=1**, **Integer=4**, Float=5, Money=6, DateTime=7, Date=8, Time=9, **Lookup=10**, Boolean=12, **Mapping(param ref)=26** |

## Key facts

- **Wrapper builder (client):** `FilterModuleMixin.CrtProcessDesigner.js` → `saveDataSourceFilters()`. It
  builds `serializedFilterEditData` (with `serializeFilterManagerInfo:true`), then deep-copies and calls
  `deleteParameterDisplayValues()` to derive `dataSourceFilters`, wraps both, and `Terrasoft.encode()`s.
- **Signal start** uses the **same wrapper**, stored on the element's `EntityFilters` string property
  (metadata key `DZ13`). `BaseSignalEventPropertiesPage` overrides `get/setDataSourceFiltersValue` to use
  `EntityFilters` and reuses the same `FilterModuleMixin`.
- **⚠️ `HasEntityFilters` (DZ8, `[MetaTypeProperty]`, defaults `false`) MUST be set `true`** when a signal
  start carries an `EntityFilters`. The runtime evaluates `EntityFilters` **only** when `HasEntityFilters`
  is true; otherwise the filter is stored (and renders in the designer) but ignored — the signal fires on
  every matching change. Confirmed by comparing a clio-built process (no `HasEntityFilters` → fired on any
  change) against the designer's working version (`HasEntityFilters: true`). `WriteMetaData` omits the key
  when false, so its absence in serialized metadata = disabled.
- **Runtime reads only `dataSourceFilters`** — `serializedFilterEditData` is purely for designer re-edit.
  We emit **both** so the generated process is runnable AND re-editable in the designer.
- **No server builder API** — `Terrasoft.*` exposes no "FilterGroup → JSON" helper we can call (the
  converters are `internal`). We emit the JSON ourselves from package-local DTOs that match these exact
  field names. This also keeps the version-coupling surface minimal (per the package's design principle).
- **Parameter reference token** = `ProcessSchemaParameter.GetMetaPath()` of the referenced param, wrapped
  in `[ ... ]` form. Process-level param → `[IsOwnerSchema:false].[IsSchema:false].[Parameter:{uid}]`;
  element-level param → `…[Element:{elementUId}].[Parameter:{paramUId}]` (the "Perform task.Account" case).
  `Id` is a fresh GUID; `displayValue` (UI only) is the human caption.
- **Lookup column equality → InFilter** (`rightExpressions[]`); scalar column → CompareFilter
  (`rightExpression`). The item-level `dataValueType` = the column's type; the right parameter's
  `dataValueType` = the column type for a constant, or `26` (Mapping) for a parameter reference.

## Variant — column path traversal into a related table (2nd capture)

The same process, with a 4th condition added in the designer: **`Account.Code = "1"`** (filter a `Contact`
by a column of its related `Account` — i.e. a lookup-traversal path). New item in both representations:

```jsonc
// serializedFilterEditData (UI rep)
"1e68663c-ce41-40c0-922c-0738084c0454": {
  "className": "Terrasoft.CompareFilter",          // terminal column 'Code' is scalar text → CompareFilter
  "filterType": 1, "comparisonType": 3, "isEnabled": true, "trimDateTimeParameterToDate": false,
  "leftExpression": { "className": "Terrasoft.ColumnExpression", "expressionType": 0, "columnPath": "Account.Code" },
  "isAggregative": false, "key": "1e68663c-…", "dataValueType": 1,   // type of the TERMINAL column (Code = Text)
  "leftExpressionCaption": "Account.Code",          // no referenceSchemaName (terminal is scalar, not a lookup)
  "rightExpression": { "className": "Terrasoft.ParameterExpression", "expressionType": 2,
    "parameter": { "className": "Terrasoft.Parameter", "dataValueType": 1, "value": "1" } }
}
// dataSourceFilters (runtime rep)
"1e68663c-…": { "filterType":1, "comparisonType":3, "isEnabled":true, "trimDateTimeParameterToDate":false,
  "leftExpression": { "expressionType":0, "columnPath":"Account.Code" },
  "rightExpression": { "expressionType":2, "parameter": { "dataValueType":1, "value":"1" } } }
```

**Rule confirmed:** `columnPath` is a dot-path that traverses lookups (`Account.Code`, `City.Name`,
`Account.Owner.Name`, …). The **terminal** column's `DataValueType` drives the item `dataValueType`, the
constant's `dataValueType`, and Compare-vs-In (terminal scalar → CompareFilter, no `referenceSchemaName`;
terminal lookup → InFilter + `referenceSchemaName`). The server resolves it by walking the root
`EntitySchema` hop-by-hop via `EntitySchemaColumn.ReferenceSchema` / `ReferenceSchemaUId`.
