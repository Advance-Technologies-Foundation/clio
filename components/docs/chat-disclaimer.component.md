# How to Add a Chat Disclaimer (`crt.ChatDisclaimer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ChatDisclaimer` into a Creatio Freedom UI page schema.
>
> `crt.ChatDisclaimer` displays a legal consent notice with an **Accept** button. When the user
> clicks Accept, the `acceptClicked` output fires. It is typically nested inside the `placeholder`
> slot of a `crt.Conversation` and shown/hidden via the `visible` binding.

## Metadata

- **Category**: interactive
- **Container**: yes (extends `BaseContainerComponent`; rarely used as a layout container)
- **Parent types**: inline in `items` or `placeholder` slots of `crt.Conversation`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An inline element inside a parent container's slot (or a standalone `insert` op). **Always present.** |
| 2 | `handlers` (optional) | A request handler for the `acceptClicked` action. |

`crt.ChatDisclaimer` owns no datasource. The `caption` text is typically a localized HTML string
bound from a viewModel attribute.

### 1.1 Naming convention

```
ChatDisclaimer_<id>   // view element name when inserted as a standalone op
```

---

## 2. Step-by-step recipe

### 2.1 Insert the disclaimer (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ChatDisclaimer_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.ChatDisclaimer",
    "visible": "$IsChatDisclaimerVisible",
    "caption": "$ChatDisclaimerText",
    "consentCode": "MyConsentCode",
    "acceptClicked": {
      "request": "crt.AcceptDisclaimerRequest",
      "params": { "consentCode": "MyConsentCode" }
    }
  }
}
```

### 2.2 (Optional) Add a handler

```jsonc
{
  "request": "crt.AcceptDisclaimerRequest",
  "handler": async (request, next) => {
    // record consent acceptance
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ChatDisclaimer` are in `ComponentRegistry.json` under `componentType: "crt.ChatDisclaimer"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// RequestBindingConfig — shape for the acceptClicked output
interface RequestBindingConfig {
  request: string;                    // e.g. 'crt.AcceptDisclaimerRequest'
  params?: RequestParamsBindingConfig;
  useRelativeContext?: boolean;
  skipOnError?: boolean;
}
```

The `caption` setter passes the value through `DomSanitizer.bypassSecurityTrustHtml` — the
rendered text may include safe HTML markup (e.g. `<b>`, `<a>`).

---

## 5. Copy-paste minimal example

Real-world usage from `CopilotPanel.js` in PackageStore (inline in `placeholder` slot):

```jsonc
// Inline inside a crt.Conversation placeholder slot
{
  "type": "crt.ChatDisclaimer",
  "visible": "$IsCopilotDisclaimerVisible",
  "caption": "$CopilotDisclaimer",
  "consentCode": "CopilotLegalNotice",
  "acceptClicked": {
    "request": "crt.CopilotDisclaimerAcceptRequest",
    "params": {
      "consentCode": "CopilotLegalNotice"
    }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "IsChatDisclaimerVisible": { "value": true },
      "ChatDisclaimerText": { "value": "" }
    }
  }
}

// viewConfigDiff.values
"visible": "$IsChatDisclaimerVisible",
"caption": "$ChatDisclaimerText"
```

---

## 7. Common pitfalls

1. **`caption` renders as HTML** — the setter trusts the value as safe HTML. Passing user-supplied
   text without sanitization is a security risk; always use a controlled resource string or server-
   provided value.
2. **`consentCode` mismatch in `acceptClicked` params** — the handler typically uses the consent
   code to record acceptance; pass the same literal in both `consentCode` and `params.consentCode`.
3. **`acceptClicked` without hiding the disclaimer** — after acceptance, set
   `IsChatDisclaimerVisible` to `false` in the handler to remove the disclaimer from view.
4. **Standalone insert vs. inline** — when nesting inside a parent slot (e.g. `placeholder`), you
   do not need a separate `insert` op; provide the element object directly inside the `items`/
   `placeholder` array.

---

## 8. Quick checklist

- [ ] `consentCode` set to the correct consent code string.
- [ ] `caption` bound to a localized or server-provided HTML string.
- [ ] `visible` bound to a page attribute so the disclaimer can be hidden after acceptance.
- [ ] `acceptClicked` wired to a handler that records acceptance and hides the disclaimer.
- [ ] `consentCode` value is consistent between the `values` object and `acceptClicked.params`.
