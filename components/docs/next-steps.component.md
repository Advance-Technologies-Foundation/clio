# How to Add Next Steps (`crt.NextSteps`) to a Freedom UI Page

> Audience: code agent inserting a `crt.NextSteps` into a Creatio Freedom UI page schema.
>
> `crt.NextSteps` is a widget that displays a collection of upcoming activities and approval tasks related
> to the current record. It fetches data automatically when `masterSchemaId` and `masterSchemaName` are
> wired to the record ID and entity name. It is typically placed in a dedicated "Next Steps" tab.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.NextSteps"`, `masterSchemaId`, `masterSchemaName`. **Always present.** |
| 2 | `viewModelConfigDiff` | Attribute declarations only if you bind optional inputs like `cardState`. |

`crt.NextSteps` loads its own data internally when `masterSchemaId` and `masterSchemaName` are provided.
No `modelConfigDiff` or `handlers` are required for basic usage.

### 1.1 Naming convention

```
NextSteps_<id>          // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "NextSteps",
  "values": {
    "type": "crt.NextSteps",
    "masterSchemaId": "$Id",
    "masterSchemaName": "Lead",
    "layoutConfig": {
      "colSpan": 2,
      "column": 1,
      "row": 1,
      "rowSpan": 1
    }
  },
  "parentName": "NextStepsTabContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.NextSteps` are in `ComponentRegistry.json` under `componentType: "crt.NextSteps"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// NextStepTileConfig — entry in the tilesMap
interface NextStepTileConfig {
  tileClassName: string;    // e.g. 'crt.NextStepTile' or 'crt.ApprovalTile'
  editPage?: string;        // page schema name for "open specific page" action
}

// LookupValue — typeFilters entry
interface LookupValue {
  value: string | number;
  displayValue: string;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real Leads_FormPage usage
{
  "operation": "insert",
  "name": "NextSteps",
  "values": {
    "type": "crt.NextSteps",
    "masterSchemaId": "$Id",
    "masterSchemaName": "Lead",
    "layoutConfig": {
      "colSpan": 2,
      "column": 1,
      "row": 1,
      "rowSpan": 1
    }
  },
  "parentName": "NextStepsTabContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — bind cardState to the page action attribute
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "CardState": { "modelConfig": { "path": "PDS.CardState" } }
  }
}

// viewConfigDiff.values
"cardState": "$CardState"
```

`cardState` controls whether the "Add" button is visible: the button shows only when
`cardState === "edit"` and `canAdd: true` (default).

---

## 7. Common pitfalls

1. **`masterSchemaName` is `"#DataSourceEntityName()#"` at runtime.** This placeholder is valid in schema templates; ensure the actual entity name resolves before the component tries to subscribe to WebSocket notifications.
2. **Omitting `masterSchemaId`.** Without the record ID the component cannot subscribe to approval notifications or reload on state changes.
3. **`canAdd: false` and expecting the Add button.** The button is hidden when `canAdd` is `false` or when `cardState` is not `"edit"`.
4. **`tilesMap` not including a `"default"` key.** The component merges custom entries with the defaults, but if you replace `tilesMap` entirely without a `"default"` key, unknown entity types fall back to `undefined`.
5. **`typeFilters` non-empty without the feature flag.** The filter logic is gated behind `NEXT_STEP_TYPE_FILTER_FEATURE_NAME`; filters are silently ignored when the feature is off.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.NextSteps"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `masterSchemaId` bound to the record ID attribute (e.g. `"$Id"`).
- [ ] `masterSchemaName` set to the entity schema name (e.g. `"Lead"`, `"Contact"`).
- [ ] `layoutConfig` provided when the parent is a `crt.GridContainer`.
- [ ] If `cardState` is needed, the attribute is declared and bound.
- [ ] Custom `tilesMap` entries include a `"default"` key for unknown entity types.
