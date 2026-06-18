# How to Add an Encrypted Input (`crt.EncryptedInput`) to a Freedom UI Page

> Audience: code agent inserting a `crt.EncryptedInput` into a Creatio Freedom UI page schema.
>
> `crt.EncryptedInput` is a text input whose value is encrypted at rest (column type
> `SECURE_TEXT (24)`). The runtime masks the value by default (`••••••`) and lets the user
> reveal it through an eye button (when allowed). Bind it to a `SECURE_TEXT` column.

> **Feature flag.** The component is gated by the `EnableSecureTextMasking` platform
> feature. Make sure it's enabled in the target environment before relying on this element.

For the underlying contract, see crt.Input guide. This document highlights only the encrypted-input differences.

## Metadata

- **Category**: fields
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.EncryptedInput"`, `control: "$<attr>"`, optional `state` binding. |
| 2 | `viewModelConfigDiff` | A page attribute bound to a `SECURE_TEXT` column. |
| 3 | `modelConfigDiff` | Register the `SECURE_TEXT` column on the page data source if not already there. |

### 1.1 Naming convention

```
EncryptedInput_<id>          // view element name
EncryptedInput_<id>_value    // page attribute (or use the column name, e.g. User_ApiKey)
EncryptedInput_<id>_state    // optional attribute holding the mask state ("masked"/"unmasked")
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
      "User_ApiKey": {
        "modelConfig": { "path": "PDS.ApiKey" }
      },
      "User_ApiKey_state": {
        "value": "masked"
      }
    }
  }
}
```

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EncryptedInput_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EncryptedInput",
    "label": "#ResourceString(EncryptedInput_xkp4r_Label)#",
    "control": "$User_ApiKey",
    "state": "$User_ApiKey_state",
    "unmaskingDisabled": false,
    "labelPosition": "auto",
    "toggleMaskValue": { "request": "crt.ToggleEncryptedValueRequest" },
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
    "ApiKey": { "path": "ApiKey" }
  }
}
```

### 2.4 Add a `toggleMaskValue` handler

```jsonc
{
  "request": "crt.ToggleEncryptedValueRequest",
  "handler": async (request, next) => {
    const current = request.$context.User_ApiKey_state;
    request.$context.User_ApiKey_state = current === "masked" ? "unmasked" : "masked";
    return next?.handle(request);
  }
}
```

The platform also exposes view-model methods `setMaskedState` / `getMaskedState` for this
attribute family; in custom code the symbol-based helpers in
`@terrasoft/studio-enterprise/util/model` can replace the inline handler.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EncryptedInput` are in `ComponentRegistry.json` under `componentType: "crt.EncryptedInput"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

The component is **not** a container — there are no `contentSlots`.

**Recommended `dataValueType` of the bound column:** `24 (SECURE_TEXT)`.

---

## 4. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "UserApiKey",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EncryptedInput",
    "label": "#ResourceString(UserApiKey_Label)#",
    "control": "$User_ApiKey",
    "state": "$User_ApiKey_state",
    "toggleMaskValue": { "request": "crt.ToggleApiKeyMaskRequest" },
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

```jsonc
// viewModelConfigDiff entries
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "User_ApiKey":       { "modelConfig": { "path": "PDS.ApiKey" } },
      "User_ApiKey_state": { "value": "masked" }
    }
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.ToggleApiKeyMaskRequest",
  "handler": async (request, next) => {
    const state = request.$context.User_ApiKey_state;
    request.$context.User_ApiKey_state = state === "masked" ? "unmasked" : "masked";
    return next?.handle(request);
  }
}
```

---

## 5. Common pitfalls

1. **Confusing `crt.EncryptedInput` with `crt.PasswordInput`** — `crt.PasswordInput` is a separate, unrelated input that uses a native `type="password"` field; `crt.EncryptedInput` is the one bound to `SECURE_TEXT` columns. The Jira ENG-89871 list refers to `crt.EncryptedInput`.
2. **Forgetting to wire `toggleMaskValue`** — the eye button still appears, but clicking it does nothing because the `state` attribute is never flipped.
3. **Binding to a non-`SECURE_TEXT` column** — encryption never happens; the value travels through the network as plain text. Always use `SECURE_TEXT (24)`.
4. **Setting `unmaskingDisabled: true` without disabling the input itself** — the user can still type and overwrite the value blindly, only they can't read it back. Combine with `readonly: true` when the field should be view-only.
5. **`EnableSecureTextMasking` feature flag is off in the target environment** — the designer hides the toolbar item, but a page that already contains the element renders with the eye button disabled and the value permanently masked.
6. **Forgetting to declare the `_state` attribute** — `state: "$..."` resolves to `undefined`, which the runtime treats as `"masked"`. The mask never toggles.

---

## 6. Quick checklist

- [ ] `insert` op with `type: "crt.EncryptedInput"`, unique `name`, `propertyName: "items"`.
- [ ] `control: "$<attr>"` references a page attribute bound to a `SECURE_TEXT (24)` column.
- [ ] `state: "$<attr>_state"` references a page attribute with initial value `"masked"`.
- [ ] `toggleMaskValue.request` wired to a handler that flips `state`.
- [ ] `label` (or `ariaLabel`) provided.
- [ ] `EnableSecureTextMasking` feature flag confirmed in the target environment.
- [ ] `layoutConfig` provided.
