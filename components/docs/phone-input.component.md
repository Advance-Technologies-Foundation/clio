# How to Add a Phone Input (`crt.PhoneInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.PhoneInput` into a Creatio Freedom UI page schema.
>
> `crt.PhoneInput` is a phone-aware text input with country picker, format masking, and an
> optional click-to-dial affordance. Bind it to a column whose `dataValueType` is
> `PHONE_TEXT (42)`.

For the underlying contract (slots, common props, attribute binding mechanics, pitfalls),
see crt.Input guide. This document highlights only the phone-specific
differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

Identical to `crt.Input`.

### 1.1 Naming convention

```
PhoneInput_<id>           // view element name
PhoneInput_<id>_value     // page attribute (or use the column name, e.g. Contact_Phone)
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
      "Contact_Phone": {
        "modelConfig": { "path": "PDS.Phone" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "PhoneInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.PhoneInput",
    "label": "#ResourceString(PhoneInput_xkp4r_Label)#",
    "control": "$Contact_Phone",
    "placeholder": "#ResourceString(PhoneInput_xkp4r_Placeholder)#",
    "tooltip": "#ResourceString(PhoneInput_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "displayAsPhone": true,
    "alwaysShowFlags": false,
    "phoneAsLink": false,
    "needHandleSave": false,
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
    "Phone": { "path": "Phone" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.PhoneInput` are in `ComponentRegistry.json` under `componentType: "crt.PhoneInput"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactPhone",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.PhoneInput",
    "label": "#ResourceString(ContactPhone_Label)#",
    "control": "$Contact_Phone",
    "displayAsPhone": true,
    "phoneAsLink": true,
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
      "Contact_Phone": { "modelConfig": { "path": "PDS.Phone" } }
    }
  }
}
```

---

## 6. Common pitfalls

(In addition to the generic `crt.Input` pitfalls — see § 7 of crt.Input guide.)

1. **`displayAsPhone: false` with a `PHONE_TEXT` column** — strips the country code parsing; the stored value may lack the leading `+`, breaking later `tel:` links and analytics.
2. **`phoneAsLink: true` without a real phone in the column** — the link still renders (as `tel:`) but does nothing useful. Combine with format validation.
3. **Mismatched `defaultCountry` and column data** — if the column already stores numbers with explicit country codes, `defaultCountry` only affects new entries. Old numbers keep their stored country.
4. **Overriding `isGridMode` / `isViewCellMode`** — these flags are set automatically by the grid host. Manually overriding them on a free-standing form breaks behavior.
5. **Whitespace-stripped phone numbers** — the underlying `intl-tel-input` library normalizes whitespace; if downstream systems require the user's exact whitespace, store the raw input separately.

---

## 7. Quick checklist

- [ ] `insert` op with `type: "crt.PhoneInput"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references an attribute bound to a `PHONE_TEXT (42)` column.
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `displayAsPhone: true` (the default) unless intentionally disabled.
- [ ] `countrySelectionConfig` reflects your tenant's geography (set `defaultCountry` for accurate parsing).
- [ ] `layoutConfig` provided.
