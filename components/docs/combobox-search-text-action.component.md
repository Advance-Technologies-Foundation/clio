# How to Add a Combobox Search Text Action (`crt.ComboboxSearchTextAction`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ComboboxSearchTextAction` into a Creatio Freedom UI page schema.
> A `crt.ComboboxSearchTextAction` is a dynamic action item rendered in the `crt.ComboBox` dropdown list — it displays the current search text alongside a fixed caption and fires `clicked` when the user selects it (e.g. "Add «typed text»").

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.ComboBox` (via `listActions` slot)
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.ComboboxSearchTextAction"` nested under a `crt.ComboBox` element. **Always present.** |
| 2 | `handlers` (optional) | A request handler for the `clicked` output request. |

`crt.ComboboxSearchTextAction` is **view-only** — no model or attribute. It lives as a child of a `crt.ComboBox` in the `listActions` slot (inside the dropdown list), not `controlActions` or `items`.

### 1.1 Naming convention

```
ComboboxSearchTextAction_<id>     // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the action inside an existing `crt.ComboBox` (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ComboboxSearchTextAction_abc123",
  "parentName": "MyComboBox",
  "propertyName": "listActions",
  "index": 0,
  "values": {
    "type": "crt.ComboboxSearchTextAction",
    "code": "addRecord",
    "icon": "combobox-add-new",
    "caption": "#ResourceString(ComboboxSearchTextAction_abc123_Caption)#",
    "iconPosition": "only-icon",
    "clicked": {
      "request": "crt.CreateRecordFromLookupRequest",
      "params": {}
    }
  }
}
```

### 2.2 (Optional) Handler for `clicked`

```jsonc
// handlers entry
{
  "request": "crt.CreateRecordFromLookupRequest",
  "handler": async (request, next) => {
    // custom logic, e.g. pre-fill a new record with the searched text
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ComboboxSearchTextAction` are in `ComponentRegistry.json` under
`componentType: "crt.ComboboxSearchTextAction"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// details input — runtime shape set by crt.ComboBox internals:
interface ComboboxSearchTextActionDetails {
  searchValue?: string;   // current text typed in the combo search field
}

// clicked output — RequestBindingConfig shape
interface RequestBindingConfig {
  request: string;     // e.g. 'crt.CreateRecordFromLookupRequest'
  params?: Record<string, unknown>;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — action inside a crt.ComboBox dropdown list
{
  "operation": "insert",
  "name": "ComboboxSearchTextAction_addNew",
  "parentName": "AccountComboBox",
  "propertyName": "listActions",
  "index": 0,
  "values": {
    "type": "crt.ComboboxSearchTextAction",
    "code": "addRecord",
    "icon": "combobox-add-new",
    "caption": "#ResourceString(ComboboxSearchTextAction_addNew_Caption)#",
    "iconPosition": "only-icon",
    "clicked": {
      "request": "crt.CreateRecordFromLookupRequest",
      "params": {}
    }
  }
}
```

---

## 7. Common pitfalls

1. **Using `propertyName: "controlActions"` instead of `"listActions"`** — `crt.ComboboxSearchTextAction` belongs in `listActions` (dropdown list), not `controlActions` (button area next to input); using the wrong slot causes the action to not appear.
2. **Using `propertyName: "items"`** — `items` is for child containers, not combo actions; always use `listActions`.
3. **Confusing with `crt.ComboboxAction`** — `crt.ComboboxAction` goes into `controlActions` (persistent button); `crt.ComboboxSearchTextAction` goes into `listActions` (appears in the dropdown with typed text interpolated into the label).
4. **Missing `code` field** — the `code` uniquely identifies the action within the combo; always provide it.
5. **Omitting `clicked`** — without a `clicked` binding the action is inert; always bind at least `{ request: '...' }`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.ComboboxSearchTextAction"`, unique `name`, valid `parentName` pointing to a `crt.ComboBox`, and `propertyName: "listActions"`.
- [ ] `code` field provided.
- [ ] `icon` matches a valid icon identifier.
- [ ] `clicked` output bound to a request.
- [ ] `caption` uses `#ResourceString(...)#` for localization.
- [ ] If `clicked` uses a custom request, a matching `handlers` entry exists.
