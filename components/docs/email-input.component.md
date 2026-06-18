# How to Add an Email Input (`crt.EmailInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.EmailInput` into a Creatio Freedom UI page schema.
>
> `crt.EmailInput` is a specialized text input that validates the user's typing against an
> email pattern. Bind it to a column whose `dataValueType` is `EMAIL_TEXT (45)`.

For the underlying contract (slots, common props, attribute binding mechanics, pitfalls),
see crt.Input guide. This document highlights only the
email-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

Identical to `crt.Input` (see § 1 of crt.Input guide):
- `viewConfigDiff` — `insert` with `type: "crt.EmailInput"`.
- `viewModelConfigDiff` — attribute bound to the email column.
- `modelConfigDiff` — entity column registration (when not already declared).

### 1.1 Naming convention

```
EmailInput_<id>           // view element name
EmailInput_<id>_value     // page attribute (or use the column name directly, e.g. Contact_Email)
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
      "Contact_Email": {
        "modelConfig": { "path": "PDS.Email" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EmailInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EmailInput",
    "label": "#ResourceString(EmailInput_xkp4r_Label)#",
    "control": "$Contact_Email",
    "placeholder": "#ResourceString(EmailInput_xkp4r_Placeholder)#",
    "tooltip": "#ResourceString(EmailInput_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "isFormatValidated": true,
    "needHandleSave": false,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
  }
}
```

### 2.3 (Optional) Register the column in `modelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Email": { "path": "Email" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EmailInput` are in `ComponentRegistry.json` under `componentType: "crt.EmailInput"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

Component declares `contentSlots: ['tools']`, so you can insert action buttons into the `tools` slot exactly like `crt.Input`.

**Recommended `dataValueType` of the bound column:** `45 (EMAIL_TEXT)`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactEmail",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EmailInput",
    "label": "#ResourceString(ContactEmail_Label)#",
    "control": "$Contact_Email",
    "isFormatValidated": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
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
      "Contact_Email": { "modelConfig": { "path": "PDS.Email" } }
    }
  }
}
```

---

## 5. Common pitfalls

(In addition to the generic `crt.Input` pitfalls — see § 7 of crt.Input guide.)

1. **Binding to a non-email column** — `crt.EmailInput` adds a regex validator that rejects free text. If the column's `dataValueType` is `Text (1)` or `MEDIUM_TEXT (28)`, switch to `crt.Input` or change the column type to `EMAIL_TEXT (45)`.
2. **Disabling `isFormatValidated` to "make it work"** — usually a sign of the wrong column type. Fix the column instead of bypassing validation.
3. **`mask` configured on top of email validation** — masks and email validators can conflict. Don't apply a mask to `crt.EmailInput`.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.EmailInput"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references an attribute bound to an `EMAIL_TEXT` column (or marked transient).
- [ ] `isFormatValidated: true` unless validation is handled elsewhere.
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `layoutConfig` provided.
