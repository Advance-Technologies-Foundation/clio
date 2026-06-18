# How to Add a Web (URL) Input (`crt.WebInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.WebInput` into a Creatio Freedom UI page schema.
>
> `crt.WebInput` is a specialized text input for URLs. When the field has a value it renders
> as a clickable link with an optional icon. Bind it to a column whose `dataValueType` is
> `WEB_TEXT (44)`.

For the underlying contract, see crt.Input guide. This
document highlights only the URL-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

Identical to `crt.Input` (see § 1 of crt.Input guide).

### 1.1 Naming convention

```
WebInput_<id>           // view element name
WebInput_<id>_value     // page attribute (or use the column name, e.g. Account_Web)
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
      "Account_Web": {
        "modelConfig": { "path": "PDS.Web" }
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "WebInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WebInput",
    "label": "#ResourceString(WebInput_xkp4r_Label)#",
    "control": "$Account_Web",
    "placeholder": "https://example.com",
    "tooltip": "#ResourceString(WebInput_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "icon": "external-link",
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
    "Web": { "path": "Web" }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.WebInput` are in `ComponentRegistry.json` under `componentType: "crt.WebInput"`. This
guide covers only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "AccountWebsite",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WebInput",
    "label": "#ResourceString(AccountWebsite_Label)#",
    "control": "$Account_Web",
    "placeholder": "https://example.com",
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
      "Account_Web": { "modelConfig": { "path": "PDS.Web" } }
    }
  }
}
```

---

## 5. Common pitfalls

(In addition to the generic `crt.Input` pitfalls — see § 7 of crt.Input guide.)

1. **Storing a URL without a protocol** — `crt.WebInput` will render the link text but the `<a href>` may fail to open (browsers treat `example.com` as a relative path). Either prepend `https://` on save, or accept that users must type it explicitly.
2. **Using `crt.Input` for a `WEB_TEXT` column** — works, but loses the auto-link affordance and the launch icon. Prefer `crt.WebInput`.
3. **Overriding `icon` with a non-existent icon name** — the icon area renders empty (no fallback). Use a name from the platform icon registry.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.WebInput"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references an attribute bound to a `WEB_TEXT (44)` column.
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `placeholder` includes the expected protocol (e.g. `"https://example.com"`).
- [ ] `layoutConfig` provided.
