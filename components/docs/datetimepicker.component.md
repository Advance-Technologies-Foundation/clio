# How to Add a Date / Time Picker (`crt.DateTimePicker`) to a Freedom UI Page

> Audience: code agent inserting a `crt.DateTimePicker` into a Creatio Freedom UI page schema.
>
> `crt.DateTimePicker` is the single component for `Date (8)`, `Time (9)`, and
> `DateTime (7)` columns. The picker mode is selected with the `pickerType` property.

For the underlying contract, see crt.Input guide. This document highlights only the date/time-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.DateTimePicker"`, `label`, `control: "$<attr>"`, `pickerType`. |
| 2 | `viewModelConfigDiff` | A page attribute bound to a Date/Time/DateTime column. |
| 3 | `modelConfigDiff` | Register the column on the page data source if not already there. |

### 1.1 Naming convention

```
DateTimePicker_<id>          // view element name
DateTimePicker_<id>_value    // page attribute
```

---

## 2. Step-by-step recipe

### 2.1 Declare / bind the page attribute (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Activity_DueDate": {
        "modelConfig": { "path": "PDS.DueDate" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "DateTimePicker_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DateTimePicker",
    "label": "#ResourceString(DateTimePicker_xkp4r_Label)#",
    "control": "$Activity_DueDate",
    "placeholder": "#ResourceString(DateTimePicker_xkp4r_Placeholder)#",
    "tooltip": "#ResourceString(DateTimePicker_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "pickerType": "datetime",
    "multiYearSelector": true,
    "useTwelveHourFormat": false,
    "useSeconds": false,
    "startView": "month",
    "mode": "auto",
    "timeInterval": 15,
    "preventSameDateTimeSelection": false,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Register the column in `modelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "DueDate": { "path": "DueDate" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.DateTimePicker` are in `ComponentRegistry.json` under `componentType: "crt.DateTimePicker"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

**Allowed `dataValueType` of the bound column:**

| Value | Name | `pickerType` to set |
|---|---|---|
| `7` | DateTime | `"datetime"` |
| `8` | Date | `"date"` |
| `9` | Time | `"time"` |

> **Always set `pickerType` explicitly** — it defaults to `"datetime"` and is NOT derived from the column type.
> For a **date-only** field set `pickerType: "date"`, including on a `DateTime (7)` column (clio creates
> `DateTime` for a "Date" request). The picker then renders date-only; the time part is dropped on save, as intended.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — datetime picker bound to a due-date column
{
  "operation": "insert",
  "name": "DueDatePicker",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.DateTimePicker",
    "label": "#ResourceString(DueDatePicker_Label)#",
    "control": "$Activity_DueDate",
    "pickerType": "datetime",
    "timeInterval": 15,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
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
      "Activity_DueDate": { "modelConfig": { "path": "PDS.DueDate" } }
    }
  }
}
```

---

## 5. Common pitfalls

1. **`pickerType` mismatched with the column** — `"date"` on a `DateTime (7)` column drops the time part on save. That is correct **on purpose** for a date-only field (see §3); only a bug if you meant to keep the time.
2. **`useSeconds: true` without the `EnableSecondsForDateTime` feature flag** — the seconds field is hidden anyway because the platform feature gate is off. Don't rely on `useSeconds` in customer-facing UI without verifying the feature is enabled.
3. **`startView: "clock"` with `pickerType: "date"`** — there is no clock view in date-only mode; the picker falls back to `"month"`. Match the start view to the picker type.
4. **`timeInterval` of `0` or negative** — the picker still emits minute-by-minute values but the up/down arrows behave unexpectedly. Always pass a positive integer (commonly `1`, `5`, `10`, `15`, `30`).
5. **Locale formatting surprises** — the picker uses the user's locale for visible formatting (`MM/DD/YYYY` vs `DD.MM.YYYY` etc.). The stored value is always ISO. Tests that assert on the visible format must mock the locale.
6. **Reading `control.value` immediately after the user picks** — the value is a `Date`/`moment` object, not a string. Convert explicitly before sending to legacy APIs that expect strings.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.DateTimePicker"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references an attribute bound to a Date / Time / DateTime column.
- [ ] `pickerType` set explicitly (defaults to `"datetime"`; not auto-derived from the column).
- [ ] Date-only field → `pickerType: "date"`, even on a `DateTime (7)` column.
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `timeInterval` set when granularity > 1 minute is desired.
- [ ] `layoutConfig` provided.
