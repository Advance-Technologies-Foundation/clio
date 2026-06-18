# How to Add a Module Loader (`crt.ModuleLoader`) to a Freedom UI Page

> Audience: code agent inserting a `crt.ModuleLoader` into a Creatio Freedom UI page schema.
>
> `crt.ModuleLoader` embeds a legacy 7x (RequireJS-based) module inside a Freedom UI page. It bootstraps
> the module into a dynamically created container div and delegates all rendering to the 7x sandbox — no
> Freedom UI children, no datasource, no attributes.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, any container slot
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.ModuleLoader"` and `module`. **Always present.** |

`crt.ModuleLoader` is **view-only** — it owns no attributes and no datasource. Provide the RequireJS module
name via `module` and the loader bootstraps it at `ngAfterViewInit`. Set `doNotRender: true` to load but
not render (useful for initializing background services).

### 1.1 Naming convention

```
ModuleLoader_<id>           // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "OBSWModule",
  "values": {
    "type": "crt.ModuleLoader",
    "module": "OBSWModule"
  },
  "parentName": "OBSWTabItems",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ModuleLoader` are in `ComponentRegistry.json` under `componentType: "crt.ModuleLoader"`. This guide
covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — basic module embed
{
  "operation": "insert",
  "name": "OBSWModule",
  "values": {
    "type": "crt.ModuleLoader",
    "module": "OBSWModule"
  },
  "parentName": "OBSWTabItems",
  "propertyName": "items",
  "index": 0
}
```

```jsonc
// viewConfigDiff entry — notifications module in a tab
{
  "operation": "insert",
  "name": "VisaNotificationsModule",
  "values": {
    "type": "crt.ModuleLoader",
    "module": "VisaNotifications"
  },
  "parentName": "VisaNotificationsTab",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **`module` value not matching a registered RequireJS module name.** The loader silently fails to render; double-check the sandbox registration.
2. **Using `crt.ModuleLoader` for native Freedom UI content.** This loader is exclusively for legacy 7x modules; use native containers for new Freedom UI content.
3. **Setting `doNotRender: true` and expecting visible output.** `doNotRender: true` loads and initializes the module but passes `null` as the render target, so nothing is displayed.
4. **Passing structured objects via `instanceConfig`.** `instanceConfig` is typed as `any` and is spread into the 7x sandbox's `instanceConfig`; only serializable values work reliably.
5. **Nesting inside a container that controls size.** The 7x module controls its own dimensions; wrapping it inside a `crt.FlexContainer` with `grow: 1` may produce unexpected sizing conflicts.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.ModuleLoader"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] `module` set to the exact RequireJS module name registered in the 7x system.
- [ ] If background-only initialization is needed, `doNotRender: true`.
- [ ] If the module needs initialization parameters, `instanceConfig` provided as a plain object.
