# How to Add an Account Compact Profile (`crt.AccountCompactProfile`) to a Mobile Page

> Audience: code agent inserting `crt.AccountCompactProfile` into a Creatio mobile Freedom UI page schema.
> Renders a compact account card (photo, name, alternative name, country, city, time zone) bound to an
> account entity via a single reference-column attribute; designed for mobile account record pages.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.Scaffold` items, `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.AccountCompactProfile"` and a `referenceColumn` binding. **Always present.** |
| 2 | `viewModelConfigDiff` | *(only if the reference column attribute is not yet declared)* |

The create command (`crt.AddAccountCompactProfileCommand`) sets `referenceColumn` to `$<attributeName>`
where `<attributeName>` points to the page attribute holding the Account primary key (e.g. `$Id` on an
account form page). It does not touch `modelConfigDiff` or `handlers`.

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
  "name": "AccountProfile",
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.AccountCompactProfile",
    "referenceColumn": "$Id",
    "readonly": false
  }
}
```

When embedding on a related-entity mobile page (e.g. a Contact form showing its Account), set
`referenceColumn` to the attribute holding the Account foreign-key lookup and set `readonly: true`.

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
// viewConfigDiff — account mobile form page (current record is the account)
{
  "operation": "insert",
  "name": "AccountProfile",
  "values": {
    "type": "crt.AccountCompactProfile",
    "referenceColumn": "$Id",
    "readonly": false
  },
  "parentName": "Scaffold",
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
3. **Setting `readonly: false` on a related-entity page** — if the viewing page is read-only, the profile widget may show an edit control that cannot save. Mirror the page's edit intent in `readonly`.
4. **Not including required package** — `crt.AccountCompactProfile` requires package `CrtCustomer360App`. Pages in packages that do not depend on it will fail to resolve the component.
5. **Wiring `imageSelected`/`imageClear` without a handler** — if you add these outputs to `values`, they must point to real requests or the upload control does nothing on file selection/clear.
6. **Setting `accountName` directly** — content inputs are populated by the component itself from the backend. Do not pre-set them in `viewConfigDiff`; they are read-only display properties in practice.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AccountCompactProfile"`, unique `name`, valid `parentName`, and `propertyName: "items"`.
- [ ] `referenceColumn` bound to a valid page attribute holding the Account primary key guid.
- [ ] `readonly` set to match the page's edit intent.
- [ ] Required package `CrtCustomer360App` declared in this page's package dependencies.
- [ ] If `imageSelected` or `imageClear` are wired, matching `handlers` entries exist.
