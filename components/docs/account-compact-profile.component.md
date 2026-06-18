# How to Add an Account Compact Profile (`crt.AccountCompactProfile`) to a Freedom UI Page

> Audience: code agent inserting `crt.AccountCompactProfile` into a Creatio Freedom UI page schema.
> Renders a compact account card (photo, name, alternative name, country, city, time zone) bound to an
> account entity via a single reference-column attribute; designed for form side-panels.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.AccountCompactProfile"` and a `referenceColumn` binding. **Always present.** |
| 2 | `viewModelConfigDiff` | *(only if a new attribute for the reference column is not yet declared)* |

The create command (`crt.AddAccountCompactProfileCommand`) sets `referenceColumn` to `$<attributeName>`
where `<attributeName>` points to the page attribute that holds the Account primary key (e.g. `$Id` on an
account form page, `$Account` on a related-entity page). It does not touch `modelConfigDiff` or `handlers`.

### 1.1 Naming convention
```
AccountCompactProfile_<id>    // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "AccountCompactProfile_abc",
  "parentName": "SideAreaContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "layoutConfig": {},
    "type": "crt.AccountCompactProfile",
    "referenceColumn": "$Id",
    "readonly": false
  }
}
```

When embedding on a related-entity page (e.g. a Contact form page showing its Account), set
`referenceColumn` to the attribute holding the Account foreign-key lookup, e.g. `"referenceColumn": "$Account"`,
and set `readonly: true`.

### 2.2 (Optional) Declare attribute in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Id": {
      "modelConfig": { "path": "AccountDS.Id" }
    }
  }
}
```

Skip this step when the attribute already exists in the page's `viewModelConfigDiff`.

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.AccountCompactProfile` are in `ComponentRegistry.json` under
`componentType: "crt.AccountCompactProfile"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// CrtValidationInfo — passed to accountNameValidationInfo
interface CrtValidationInfo {
  valid: boolean;
  dirty?: boolean;
  touched?: boolean;
  message?: string;
}
```

The outputs `imageSelected` and `imageClear` are `RequestBindingConfig` shaped:
```ts
interface RequestBindingConfig {
  request: string;          // e.g. 'crt.UploadPhotoRequest'
  params?: Record<string, unknown>;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff — account form page (current record is the account)
{
  "operation": "insert",
  "name": "CompactProfile",
  "values": {
    "layoutConfig": {},
    "type": "crt.AccountCompactProfile",
    "referenceColumn": "$Id",
    "readonly": false
  },
  "parentName": "SideAreaProfileFieldFlexContainer",
  "propertyName": "items",
  "index": 0
}
```

```jsonc
// viewConfigDiff — contact form page (account is a foreign key)
{
  "operation": "insert",
  "name": "AccountCompactProfile",
  "values": {
    "layoutConfig": {},
    "type": "crt.AccountCompactProfile",
    "referenceColumn": "$Account",
    "readonly": true
  },
  "parentName": "AccountInfoFieldContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "AccountCompactProfile_readonly": { "value": false }
  }
}

// viewConfigDiff.values
"readonly": "$AccountCompactProfile_readonly"
```

`readonly` is a `propertyBindable` input — bind via `$Attribute` to control edit mode at runtime.

---

## 7. Common pitfalls

1. **Missing `referenceColumn`** — without it the card has no entity to fetch and renders empty. Always wire it to the attribute holding the Account primary-key guid.
2. **`referenceColumn` is a guid string, not a LookupValue** — the expected value is a plain string UUID pointing to the Account `Id`, not a `{ value, displayValue }` object.
3. **Setting `readonly: false` on a related-entity form** — if the viewing page is read-only, the profile widget may show an edit control that cannot save. Mirror the form's edit intent in `readonly`.
4. **Omitting `layoutConfig: {}`** — the empty object is required when the parent is a `crt.FlexContainer`; without it some layout engines log a warning.
5. **Not including required package** — `crt.AccountCompactProfile` requires package `CrtCustomer360App`. Pages in packages that do not depend on it will fail to resolve the component.
6. **Wiring `imageSelected`/`imageClear` without a handler** — if you add these outputs to `values`, they must point to real requests or the upload control does nothing on file selection/clear.
7. **Setting `accountName` directly** — these content inputs are populated by the component itself from the backend. Do not pre-set them in `viewConfigDiff`; they are read-only display properties in practice.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AccountCompactProfile"`, unique `name`, valid `parentName`, and `propertyName: "items"`.
- [ ] `referenceColumn` bound to a valid page attribute holding the Account primary key guid.
- [ ] `readonly` set to match the page's edit intent.
- [ ] Required package `CrtCustomer360App` declared in this page's package dependencies.
- [ ] `layoutConfig: {}` present (even empty) when parent is a `crt.FlexContainer`.
- [ ] If `imageSelected` or `imageClear` are wired, matching `handlers` entries exist.
