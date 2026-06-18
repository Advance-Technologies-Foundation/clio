# How to Add a Contact Compact Profile (`crt.ContactCompactProfile`) to a Mobile Page

> Audience: code agent inserting `crt.ContactCompactProfile` into a Creatio mobile Freedom UI page schema.
> A `crt.ContactCompactProfile` is an editable inline card for a contact record; it shows and allows
> editing of the contact's full name parts (first / middle / last), photo, birth date, country, city,
> and time zone. Requires the `CrtCustomer360App` package.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.Scaffold` items, `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ContactCompactProfile"` and `referenceColumn` + `readonly`. **Always present.** |

`crt.ContactCompactProfile` loads the contact record internally via `referenceColumn` (bound to the
primary record ID). No separate `modelConfigDiff` or `viewModelConfigDiff` is needed for the basic
recipe — the create command auto-wires `referenceColumn` to `$Id` on the primary datasource.

### 1.1 Naming convention

```
ContactCompactProfile_<id>      // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the compact profile (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ContactCompactProfile_abc123",
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ContactCompactProfile",
    "referenceColumn": "$Id",
    "readonly": false
  }
}
```

`referenceColumn` is bound to the `$-prefix` attribute that holds the Contact record ID (typically
`"$Id"` on a Contact mobile form page).

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ContactCompactProfile` are in `ComponentRegistry.json` under
`componentType: "crt.ContactCompactProfile"`. This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactCompactProfile",
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ContactCompactProfile",
    "referenceColumn": "$Id",
    "readonly": false
  }
}
```

---

## 6. Driving from page state

The `firstName`, `middleName`, `lastName`, `displayName`, `birthDate`, `country`, `city`, `timeZone`
inputs accept `$-prefix` attribute bindings when individual fields need to be driven from the viewModel:

```jsonc
"firstName": "$FirstName",
"lastName": "$LastName"
```

For name editing, the component emits `firstNameChange`, `lastNameChange`, `middleNameChange`, and
`fullNameChange` outputs — bind them to request handlers to persist the changes.

---

## 7. Common pitfalls

1. **Missing `CrtCustomer360App` package** — `crt.ContactCompactProfile` requires the `CrtCustomer360App` package to be installed; it will not render without it.
2. **Binding `referenceColumn` to a display value** — `referenceColumn` must point to a `$-prefix` attribute that contains the Contact record GUID, not the display name.
3. **Setting `readonly: true` to prevent all edits** — `readonly` gates all field edits; set it to `false` on edit forms and `true` on read-only views.
4. **Providing `firstNameValidationInfo` manually** — `firstNameValidationInfo` is a `CrtValidationInfo` object managed by the platform; do not construct it manually in `values`.
5. **Expecting `photo` changes without a handler** — changes to the photo emit `imageSelected` and `imageClear` outputs; wire them to request handlers to persist photo changes.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ContactCompactProfile"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `referenceColumn` bound to the Contact record GUID attribute (`"$Id"`).
- [ ] `readonly` explicitly set (`false` for editable, `true` for read-only).
- [ ] `CrtCustomer360App` package is installed in the target environment.
- [ ] If name-change events are needed, `firstNameChange`/`lastNameChange`/`middleNameChange`/`fullNameChange` are bound to request handlers.
