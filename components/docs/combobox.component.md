# How to Add a ComboBox (`crt.ComboBox`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ComboBox` into a Creatio Freedom UI page schema.
>
> `crt.ComboBox` is a single-select lookup picker. The user types to filter, picks one
> value, and the result is stored as a `LookupValue` (`{ value, displayValue }`). Bind it
> to a `Lookup (10)` column.

For the underlying contract (slots, common props, attribute binding mechanics), see
crt.Input guide. This document
highlights the lookup-specific differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none (lookup items live on the bound datasource, not as child elements)

---

## 1. Mental model — what you edit

For a ComboBox bound to a **Lookup column** you normally edit just **two** places. The
`ComboboxPreprocessor` builds the items datasource for you at page-load time (an embedded
`crt.EntityDataSource` derived from the bound column), so you do **not** declare one by hand:

| # | Section | What you add |
|---|---|---|
| 1 | `viewModelConfigDiff` | A page attribute bound to the lookup column (`modelConfig.path: "PDS.<Column>"`). The attribute holds a `LookupValue`. |
| 2 | `viewConfigDiff` | An `insert` op with `type: "crt.ComboBox"` and `control: "$<attr>"`. |

Only when the dropdown must show a **custom / filtered / non-column list** do you add a third edit:

| # | Section | What you add |
|---|---|---|
| 3 | `modelConfigDiff` | A `crt.EntityDataSource` (scope `viewElement`) under `dataSources`, plus a matching `isCollection` attribute, and `items: "$<collectionAttr>"` on the view element. |

> There is **no** `crt.LookupDataSource` type. A custom lookup list uses
> `crt.EntityDataSource`; emitting `crt.LookupDataSource` fails at runtime with
> `Data source type 'crt.LookupDataSource' not supported`.

The lookup options can come from:
- The bound Lookup column itself — the preprocessor loads them automatically (the common case; no datasource or `items` needed).
- An inline static array of `LookupValue` items (`items: [...]`).
- A `BaseViewModelCollection` exposed through a page attribute that is itself fed by a `crt.EntityDataSource` (custom / filtered list).
- A `showList` event handler that resolves items on demand from the server.

### 1.1 Naming convention

```
ComboBox_<id>            // view element name
ComboBox_<id>_value      // page attribute holding the selected LookupValue
ComboBox_<id>_list       // page attribute holding the dropdown items (when bound)
ComboBox_<id>DS          // optional crt.EntityDataSource key (custom items list only)
```

---

## 2. Step-by-step recipe

### 2.0 The default case — bind to a Lookup column (no datasource)

For a ComboBox on a standard Lookup column you declare **no** datasource and **no** `items`:
the `ComboboxPreprocessor` creates the embedded `crt.EntityDataSource` and the list attribute
for you at page-load time. You only add the binding attribute and the view element.

```jsonc
// viewModelConfigDiff — bind an attribute to the lookup column
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Country": { "modelConfig": { "path": "PDS.Country" } }
    }
  }
}
```

```jsonc
// viewConfigDiff — insert the ComboBox bound via control (no items)
{
  "operation": "insert",
  "name": "ComboBox_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ComboBox",
    "label": "#ResourceString(ComboBox_xkp4r_Label)#",
    "control": "$Contact_Country",
    "labelPosition": "auto",
    "mode": "List",
    "showValueAsLink": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.1 (Optional) Add a custom items datasource (`modelConfigDiff` entry) — *only for a filtered / non-column list*

Skip this for a plain Lookup column (see § 2.0). Declare a datasource only when the dropdown must
show a custom or filtered set of records. The type is `crt.EntityDataSource`; pair it with the
collection attribute in § 2.2 and the `items` binding in § 2.3.

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "ComboBox_xkp4rDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "Country"
        }
      }
    }
  }
}
```

Also register the lookup column on the page's primary datasource:

```jsonc
{
  "operation": "merge",
  "path": ["dataSources", "PDS", "config", "attributes"],
  "values": {
    "Country": { "path": "Country" }
  }
}
```

### 2.2 Declare attributes (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Country": {
        "modelConfig": { "path": "PDS.Country" }
      },
      "ComboBox_xkp4r_list": {
        "isCollection": true,
        "modelConfig": { "path": "ComboBox_xkp4rDS" }
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ComboBox_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ComboBox",
    "label": "#ResourceString(ComboBox_xkp4r_Label)#",
    "control": "$Contact_Country",
    "items": "$ComboBox_xkp4r_list",
    "placeholder": "#ResourceString(ComboBox_xkp4r_Placeholder)#",
    "tooltip": "#ResourceString(ComboBox_xkp4r_Tooltip)#",
    "labelPosition": "auto",
    "mode": "List",
    "showValueAsLink": true,
    "useStaticFiltering": false,
    "debounceTime": 500,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

**Recommended `dataValueType` of the bound column:** `10 (Lookup)`.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ComboBox` are in `ComponentRegistry.json` under `componentType: "crt.ComboBox"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. `LookupValue` shape

```ts
interface LookupValue {
  value: string;            // GUID / id of the row
  displayValue?: string;    // visible caption
  primaryColorValue?: string;
  primaryImageValue?: string;
  secondaryDisplayValue?: string;
}
```

Lookup paths in `modelConfig` use dotted notation: `"PDS.Country"`, `"PDS.Country.Name"` (for the display path), etc.

---

## 5. Copy-paste minimal example

### Default — Lookup column (no datasource, no `items`)

The preprocessor auto-wires the items, so `modelConfigDiff` stays `[]`.

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Country": { "modelConfig": { "path": "PDS.Country" } }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactCountryCombo",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ComboBox",
    "label": "#ResourceString(ContactCountryCombo_Label)#",
    "control": "$Contact_Country",
    "mode": "List",
    "showValueAsLink": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### Explicit / filtered list — custom items via `crt.EntityDataSource`

```jsonc
// modelConfigDiff entries
[
  {
    "operation": "merge",
    "path": [],
    "values": {
      "dataSources": {
        "CountryDS": {
          "type": "crt.EntityDataSource",
          "scope": "viewElement",
          "config": { "entitySchemaName": "Country" }
        }
      }
    }
  },
  {
    "operation": "merge",
    "path": ["dataSources", "PDS", "config", "attributes"],
    "values": { "Country": { "path": "Country" } }
  }
]
```

```jsonc
// viewModelConfigDiff entry
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Contact_Country":   { "modelConfig": { "path": "PDS.Country" } },
      "Contact_CountryList": {
        "isCollection": true,
        "modelConfig": { "path": "CountryDS" }
      }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactCountryCombo",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ComboBox",
    "label": "#ResourceString(ContactCountryCombo_Label)#",
    "control": "$Contact_Country",
    "items": "$Contact_CountryList",
    "mode": "List",
    "showValueAsLink": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 6. Common pitfalls

(In addition to the generic `crt.Input` pitfalls — see § 7 of crt.Input guide.)

1. **Declaring a datasource for a plain Lookup column, or naming a `crt.LookupDataSource`** — for a standard lookup column you declare **no** datasource: the `ComboboxPreprocessor` builds the embedded `crt.EntityDataSource` and loads the items automatically. There is **no** `crt.LookupDataSource` type — emitting it fails at runtime with `Data source type 'crt.LookupDataSource' not supported` and the page does not render. Declare a `crt.EntityDataSource` (scope `viewElement`) only for a custom / filtered list, with a matching `isCollection` attribute and `items` binding. Inline static `items` need no datasource.
2. **Storing a string instead of a `LookupValue`** — saving `"USA"` instead of `{ value: "<guid>", displayValue: "USA" }` makes the picker show an empty value next render. The platform's `LookupValue` shape is non-negotiable.
3. **`mode: "SelectionWindow"` without a selection-window service registered** — the magnifier icon opens nothing. Use `"List"` for plain dropdowns.
4. **`useStaticFiltering: true` with a server-paginated dataset** — the filter operates only on the already-loaded slice, silently hiding the rest. Either load all rows up front or stick to server-side filtering.
5. **Static `items` + `showList` handler** — the handler still fires on every keystroke; if it overwrites `items`, the static list flickers. Pick one source.
6. **Lookup column with two-level paths** — `"path": "PDS.Owner.Name"` works for the display value but `control` itself binds to the foreign key, not the display column. Match `control` to the lookup column (`PDS.Owner`).
7. **`useMultiChoice: true` on a single-value column** — the column accepts only one foreign key; the second selection silently replaces the first. Use `crt.MultiSelect` (with a back-reference table) for true multi-pick.

---

## 7. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ComboBox"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references an attribute bound to a `Lookup (10)` column.
- [ ] `items` omitted for a plain Lookup column (auto-loaded), inline (static), or `"$<collectionAttr>"` for a custom/filtered list.
- [ ] Custom / filtered list only: `modelConfigDiff` declares a `crt.EntityDataSource` (scope `viewElement`) + a matching `isCollection` attribute. A plain Lookup column needs no datasource.
- [ ] `mode` set intentionally (`"List"` for inline dropdown, `"SelectionWindow"` for modal picker — case-sensitive).
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `layoutConfig` provided.
