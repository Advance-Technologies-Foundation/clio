# How to Add a User Compact Profile (`crt.UserCompactProfile`) to a Freedom UI Page

> Audience: code agent inserting `crt.UserCompactProfile` into a Creatio Freedom UI page schema.
> `crt.UserCompactProfile` is a **display/edit widget** that shows and allows editing of the current
> user's name (first, middle, last) and profile photo within a compact profile card.

## Metadata

- **Category**: display
- **Container**: yes (`dialogItems` content slot for dialog overlay)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: custom dialog items in the `dialogItems` slot

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.UserCompactProfile"` and initial values. **Always present.** |
| 2 | `viewModelConfigDiff` | *(only if binding name fields to page attributes)* |

`crt.UserCompactProfile` is view-only from a page-schema perspective — it owns no datasource entry.
All name and photo fields are bound to page attributes. The widget resolves the logged-in user's full
name template automatically via `ContactInfoService`.

### 1.1 Naming convention

```
UserCompactProfile_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "UserCompactProfile_main",
  "parentName": "SideAreaProfileContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.UserCompactProfile",
    "readonly": false,
    "referenceColumn": "$Id",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.UserCompactProfile` are in `ComponentRegistry.json` under `componentType: "crt.UserCompactProfile"`.

Leaf-class inputs: `firstName`, `middleName`, `lastName`, `firstNameValidationInfo`.
Inherited inputs (from base): `dialogTitle`, `referenceColumn`, `readonly`, `photo`, `photoTitle`.
Inherited outputs: `imageSelected`, `imageClear`.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// firstNameValidationInfo — CrtValidationInfo
interface CrtValidationInfo {
  valid: boolean;
  dirty: boolean;
  touched: boolean;
}

// imageSelected event payload (UploadImageData)
interface UploadImageData {
  file: File;
  dataUrl: string;
}
```

The component resolves `fullName` using `ContactInfoService.getFullName(template, first, middle, last)`.
The template is fetched once via `getFullNameTemplateValue()` and cached internally.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry (from SysUserProfilePage real usage)
{
  "operation": "insert",
  "name": "UserCompactProfile_main",
  "parentName": "SideAreaProfileContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.UserCompactProfile",
    "readonly": false,
    "referenceColumn": "$Id",
    "visible": true,
    "layoutConfig": {
      "column": 1,
      "row": 1,
      "colSpan": 1,
      "rowSpan": 1
    }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff — declare name attributes
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FirstName": { "value": "" },
      "MiddleName": { "value": "" },
      "LastName": { "value": "" }
    }
  }
}

// viewConfigDiff.values
"firstName": "$FirstName",
"middleName": "$MiddleName",
"lastName": "$LastName"
```

`firstName`, `middleName`, `lastName` are all `@CrtInput` (bindable). Setting any of them triggers a
full-name recomputation via `ContactInfoService`.

---

## 7. Common pitfalls

1. **`readonly: true` disables all editing.** Name fields and photo upload are all locked; the widget becomes display-only.
2. **`referenceColumn` should be bound to the record ID attribute.** The base uses `getRefId()` which returns `userInfo.contactId`; bind `referenceColumn` to the page attribute holding the contact GUID.
3. **`firstName` validation** — `firstNameValidationInfo` drives `isShownRequiredMark`; wire it to the field's validation info attribute if required-field marking is needed.
4. **Full-name template is fetched asynchronously.** The `fullName` computed property will be empty until `ContactInfoService.getFullNameTemplateValue()` emits; expect an initial render with empty name.
5. **`imageSelected` carries the raw `File` object.** You must upload and persist the photo via a separate handler; the component only emits selection events.
6. **`dialogItems` content slot** — if you need to add extra actions to the profile dialog overlay, insert them into the `dialogItems` slot with a child `insert` op.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.UserCompactProfile"`, unique `name`.
- [ ] `referenceColumn` bound to the contact/user ID attribute.
- [ ] `readonly` set explicitly (`true` for display-only, `false` for editable).
- [ ] `firstName`, `middleName`, `lastName` bound to attributes if name editing is needed.
- [ ] `firstNameValidationInfo` bound if required-field validation is shown.
- [ ] If photo upload is needed, `imageSelected` and `imageClear` outputs wired to handlers.
- [ ] `layoutConfig` provided when parent is `crt.GridContainer`.
