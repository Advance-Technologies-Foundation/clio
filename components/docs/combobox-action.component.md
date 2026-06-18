# How to Add a Combobox Action (`crt.ComboboxAction`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ComboboxAction` into a Creatio Freedom UI page schema.
> A `crt.ComboboxAction` is an action item rendered inside a `crt.ComboBox` control-action area — it appears as a clickable button with an icon and optional caption next to the combo input field.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.ComboBox` (via `controlActions` slot)
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ComboboxAction"` nested under a `crt.ComboBox` element. **Always present.** |
| 2 | `handlers` (optional) | A request handler for the `clicked` output request if custom logic is needed. |

`crt.ComboboxAction` is **view-only** — no model or attribute. It lives as a child of a `crt.ComboBox` in the `controlActions` slot, not the standard `items` slot.

### 1.1 Naming convention

```
ComboboxAction_<id>       // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the action inside an existing `crt.ComboBox` (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ComboboxAction_abc123",
  "parentName": "MyComboBox",
  "propertyName": "controlActions",
  "index": 0,
  "values": {
    "type": "crt.ComboboxAction",
    "code": "goToRecord",
    "icon": "combobox-go-to-source",
    "caption": "#ResourceString(ComboboxAction_abc123_Caption)#",
    "iconPosition": "only-icon",
    "clicked": {
      "request": "crt.OpenLookupSourceRequest",
      "params": {}
    }
  }
}
```

### 2.2 (Optional) Handler for `clicked`

```jsonc
// handlers entry
{
  "request": "crt.OpenLookupSourceRequest",
  "handler": async (request, next) => {
    // custom logic here
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ComboboxAction` are in `ComponentRegistry.json` under `componentType: "crt.ComboboxAction"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// clicked output — RequestBindingConfig shape
interface RequestBindingConfig {
  request: string;     // e.g. 'crt.OpenLookupSourceRequest'
  params?: Record<string, unknown>;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — action inside a crt.ComboBox
{
  "operation": "insert",
  "name": "ComboboxAction_goTo",
  "parentName": "ContactComboBox",
  "propertyName": "controlActions",
  "index": 0,
  "values": {
    "type": "crt.ComboboxAction",
    "code": "goToRecordList",
    "icon": "combobox-go-to-source",
    "caption": "ComboBox.IsGoToSourceAllowedTooltip",
    "iconPosition": "only-icon",
    "clicked": {
      "request": "crt.OpenLookupSourceRequest",
      "params": {}
    }
  }
}
```

---

## 7. Common pitfalls

1. **Using `propertyName: "items"` instead of `"controlActions"`** — `crt.ComboboxAction` must be inserted into the `controlActions` slot of a `crt.ComboBox`, not the standard `items` slot; using `items` silently has no effect.
2. **Missing `code` field** — the `code` uniquely identifies the action within the combo; omitting it can cause ordering and tracking issues.
3. **Setting `iconPosition: "only-text"` without a `caption`** — if `caption` is empty and `iconPosition` is `"only-text"`, the action renders invisibly; always provide a caption or use `"only-icon"`.
4. **Nesting inside something other than `crt.ComboBox`** — `crt.ComboboxAction` is only meaningful as a child of `crt.ComboBox`; inserting it elsewhere has no rendering effect.
5. **Omitting `clicked`** — without a `clicked` binding the action is inert; always bind at least `{ request: '...' }`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ComboboxAction"`, unique `name`, valid `parentName` pointing to a `crt.ComboBox`, and `propertyName: "controlActions"`.
- [ ] `code` field provided to uniquely identify the action.
- [ ] `icon` matches a valid icon identifier.
- [ ] `clicked` output bound to a request (`{ request: '...' }`).
- [ ] `iconPosition` set appropriately (`"only-icon"` is most common for compact combo actions).
- [ ] If `clicked` uses a custom request, a matching `handlers` entry exists.
