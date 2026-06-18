# How to Add a Web Text Input (`crt.WebTextInput`) to a Freedom UI Page

> Audience: code agent inserting `crt.WebTextInput` into a Creatio Freedom UI page schema.
> `crt.WebTextInput` is a **shadow-DOM text input** web component that renders a standard `<input>` element
> with a label, placeholder, and validation state inside an isolated Shadow DOM boundary.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.WebTextInput"` and starting values. **Always present.** |
| 2 | `viewModelConfigDiff` | *(only if binding `value` to a page attribute)* |

> **Architecture note:** This component uses `ViewEncapsulation.ShadowDom` — CSS from the host page does
> not penetrate the shadow root. Native CustomEvents (`input`, `change`) bubble with `composed: true` so
> they cross the shadow boundary.

### 1.1 Naming convention

```
WebTextInput_<id>          // view element name
$WebTextInput_<id>_value   // $-prefix attribute for the bound text value
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "WebTextInput_name",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WebTextInput",
    "id": "web-text-input-name",
    "label": "Name",
    "placeholder": "Enter your name",
    "value": "$NameValue",
    "disabled": false,
    "required": false,
    "readonly": false,
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Declare the attribute in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "NameValue": { "value": "" }
    }
  }
}
```

### 2.3 (Optional) Handle value changes in `handlers`

```jsonc
{
  "request": "crt.WebTextInputValueChangedRequest",
  "handler": async (request, next) => {
    // request.event contains the new string value
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.WebTextInput` are in `ComponentRegistry.json` under `componentType: "crt.WebTextInput"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// valueValidationInfo — CrtValidationInfo
interface CrtValidationInfo {
  valid: boolean;
  dirty: boolean;
  touched: boolean;
}
```

`disabled`, `required`, and `readonly` all use Angular's `booleanAttribute` transform — any non-null
string value (including `""`) coerces to `true`; omitting the attribute coerces to `false`. These are
also reflected as host element attributes (`attr.disabled`, `attr.required`, `attr.readonly`).

`valueChange` emits a plain `string` from the native `input` event; it also dispatches native
CustomEvents (`input`, `change`) on the host element with `detail: { value }` that cross the Shadow DOM
boundary.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "WebTextInput_search",
  "parentName": "SearchContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.WebTextInput",
    "id": "web-text-input-search",
    "label": "Search",
    "placeholder": "Search...",
    "value": "$SearchValue",
    "visible": true,
    "valueChange": { "request": "crt.HandleSearchValueChangeRequest" }
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.HandleSearchValueChangeRequest",
  "handler": async (request, next) => {
    const newValue = request.event;
    return next?.handle(request);
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
      "SearchValue": { "value": "" }
    }
  }
}

// viewConfigDiff.values
"value": "$SearchValue",
"valueChange": { "request": "crt.HandleSearchValueChangeRequest" }
```

`value` is bindable; `valueChange` fires on every native `input` event. Use `valueValidationInfo` to show
required-field or custom validation errors.

---

## 7. Common pitfalls

1. **Shadow DOM isolation** — CSS variables and global stylesheets do not penetrate the shadow boundary; style the component via CSS custom properties passed as host attributes or `:host` rules inside the component.
2. **`booleanAttribute` coercion** — passing `disabled="false"` (a string) evaluates to `true`; omit the property or pass `false` as a boolean to keep the field enabled.
3. **`id` must be unique on the page.** The `id` attribute is reflected onto the host element's input and should be unique to avoid duplicate-ID accessibility warnings.
4. **`valueChange` vs native events** — `valueChange` emits the string value; native CustomEvents (`input`, `change`) also fire and bubble through the shadow boundary, which may cause double-handling in parent event listeners.
5. **`valueValidationInfo` requires `@CrtValidationInput`.** Wire it to the field's validation info attribute from a datasource for required-field marking to appear.
6. **`label` is a plain string, not a resource string key.** Use a pre-resolved translated string rather than `#ResourceString(<key>)#` unless the host renders resource strings natively.
7. **`readonly` and `disabled` are distinct.** `readonly` prevents editing but the field is still focusable and its value is submitted; `disabled` prevents both editing and form submission.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.WebTextInput"`, unique `name`.
- [ ] `id` is a unique string (defaults to `"web-text-input"` — override it).
- [ ] `label` and `placeholder` set to human-readable strings.
- [ ] `value` bound to an attribute (or set to a static string).
- [ ] `valueChange` wired to a handler if the page needs to react to user input.
- [ ] `disabled`, `required`, `readonly` set as booleans (not strings) to avoid `booleanAttribute` coercion surprises.
- [ ] `layoutConfig` provided when parent is `crt.GridContainer`.
