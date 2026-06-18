# How to Add a Local Time (`crt.LocalTime`) to a Freedom UI Page

> Audience: code agent inserting a `crt.LocalTime` into a Creatio Freedom UI page schema.
>
> A `crt.LocalTime` displays the current local time adjusted to a specific timezone. It is a
> Lookup-type field that expects a `TimeZone` entity record bound through a datasource attribute.
> The component automatically converts the current UTC time to the given timezone offset and
> re-renders on each binding change.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `modelConfigDiff` | A datasource that loads the `TimeZone` entity record. |
| 2 | `viewModelConfigDiff` | A Lookup attribute bound to `TimeZone` datasource. |
| 3 | `viewConfigDiff` | An `insert` op with `type: "crt.LocalTime"` and `control: "$Attr"`. |

### 1.1 Naming convention

```
LocalTime_<id>           // view element name
LocalTime_<id>DS         // datasource key (TimeZone entity)
$LocalTime_<id>          // Lookup attribute
```

---

## 2. Step-by-step recipe

### 2.1 Add the datasource (`modelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "LocalTime_abcDS": {
        "type": "crt.EntityDataSource",
        "config": {
          "entitySchemaName": "TimeZone"
        }
      }
    }
  }
}
```

### 2.2 Declare the Lookup attribute (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "LocalTime_abc": {
        "type": "crt.LookupAttribute",
        "datasource": "LocalTime_abcDS"
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "LocalTime_abc",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.LocalTime",
    "label": "#ResourceString(LocalTime_abc_label)#",
    "caption": "#ResourceString(DataValueType.LocalTimeCaption)#",
    "control": "$LocalTime_abc",
    "labelType": "body",
    "labelThickness": "normal",
    "labelEllipsis": false,
    "labelColor": "#098401",
    "labelBackgroundColor": "transparent",
    "labelTextAlign": "start",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.LocalTime` are in `ComponentRegistry.json` under `componentType: "crt.LocalTime"`. This
guide covers only the assembly mechanics.

---

## 4. Shape of the `control` input

`control` expects a `LookupValue` (the resolved `TimeZone` record) which is provided by the
datasource attribute binding:

```ts
interface LookupValue {
  value: string;          // TimeZone record Id (GUID)
  displayValue: string;   // Timezone display name (e.g. "UTC+3")
}
```

The component reads `value` (GUID) to call the `AdjustmentRuleService`, retrieves the UTC
offset, and re-renders the formatted time string. In design-time it falls back to the current
local time.

---

## 5. Copy-paste minimal example

```jsonc
// modelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "ContactTimeZoneDS": {
        "type": "crt.EntityDataSource",
        "config": { "entitySchemaName": "TimeZone" }
      }
    }
  }
}
```

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ContactTimeZone": {
        "type": "crt.LookupAttribute",
        "datasource": "ContactTimeZoneDS"
      }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactLocalTime",
  "parentName": "InfoFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.LocalTime",
    "label": "#ResourceString(ContactLocalTime_label)#",
    "caption": "#ResourceString(DataValueType.LocalTimeCaption)#",
    "control": "$ContactTimeZone",
    "labelType": "body",
    "labelThickness": "normal",
    "labelColor": "#098401",
    "labelBackgroundColor": "transparent",
    "labelTextAlign": "start",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 7. Common pitfalls

1. **Binding `control` to a non-Lookup attribute** — the component casts `control` to `LookupValue`;
   if the attribute is not a `TimeZone` lookup the result is always empty.
2. **`referenceSchemaName` must be `"TimeZone"`** — the adjustment rule service only resolves
   records from the `TimeZone` entity; linking to another entity will silently yield an empty
   display.
3. **DST transitions** — the component applies the `AdjustmentRule` for Daylight Saving Time;
   if the server-side `TimeZone` record has `daylightDeltaInMinutes: 0` the DST period is
   ignored, which is correct behavior.
4. **Design-time preview** — in the interface designer the component shows the current local
   browser time (not the adjusted timezone time) as a placeholder.
5. **`labelColor` default `"#098401"`** — this green is the `@CrtInterfaceDesignerItem` default;
   override it when the color does not match the page design.

---

## 8. Quick checklist

- [ ] Datasource in `modelConfigDiff` with `entitySchemaName: "TimeZone"`.
- [ ] Lookup attribute in `viewModelConfigDiff` linked to the datasource.
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.LocalTime"` and `control: "$Attr"`.
- [ ] `label` set via `#ResourceString(...)#`.
- [ ] `labelType`, `labelColor`, `labelBackgroundColor` explicitly set (defaults are provided
  but visible in the designer).
- [ ] `visible` set to `true` or bound to a page attribute.
