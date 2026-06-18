# How to Add a Dashboards (`crt.Dashboards`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Dashboards` into a Creatio Freedom UI page schema.
> A `crt.Dashboards` is a tabbed dashboard host that renders a list of available dashboards in a dropdown/tab switcher and loads the selected dashboard schema into an embedded schema outlet; it also handles cross-tab favorite synchronization and schema-change notifications.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Dashboards"` and starting values. **Always present.** |
| 2 | `handlers` (optional) | Handlers for `selectedDashboardChange`, `createDashboard`, `dashboardsOutdated`, `favoriteDashboardChanged`. |

`crt.Dashboards` is **view-only** — no model or datasource of its own. Dashboard list data comes from the `dashboards` input (a `BaseViewModelCollection` or `DashboardItem[]`). When inserted by the designer, it uses a `placeholder: true` flag until real data is bound.

### 1.1 Naming convention

```
Dashboards_<id>        // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the dashboards component (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Dashboards_abc123",
  "parentName": "DashboardsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Dashboards",
    "placeholder": true,
    "_designOptions": {
      "dependencies": [],
      "filters": []
    }
  }
}
```

When real dashboard data is available, replace `placeholder: true` with `dashboards: "$DashboardsCollection"` and remove the `placeholder` key.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Dashboards` are in `ComponentRegistry.json` under `componentType: "crt.Dashboards"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// DashboardItem — individual dashboard entry
interface DashboardItem {
  Id: string;     // GUID
  UId: string;
  Name: string;   // display name shown in the tab selector
  Code: string;   // schema code used to load the dashboard
  UserLevelSchema?: unknown;
}

// _designOptions — designer-side metadata, not processed at runtime
interface DesignOptions {
  dependencies: unknown[];
  filters: unknown[];
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — designer placeholder (initial state)
{
  "operation": "insert",
  "name": "Dashboards_main",
  "parentName": "DashboardsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Dashboards",
    "placeholder": true,
    "_designOptions": {
      "dependencies": [],
      "filters": []
    }
  }
}
```

```jsonc
// viewConfigDiff entry — runtime with real dashboards collection
{
  "operation": "insert",
  "name": "Dashboards_main",
  "parentName": "DashboardsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Dashboards",
    "dashboards": "$DashboardsCollection",
    "selectedDashboard": "$SelectedDashboard",
    "canManage": true
  }
}
```

---

## 6. Driving from page state

```jsonc
// Bind dashboards collection and selected tab from page state
"dashboards": "$DashboardsCollection",
"selectedDashboard": "$SelectedDashboard",
"filter": "$DashboardFilter",
"hierarchicalFilter": "$HierarchicalFilter"
```

`filter`, `hierarchicalFilter`, and `hierarchicalColumnValue` are forwarded as parameters to the currently loaded dashboard schema.

---

## 7. Common pitfalls

1. **Leaving `placeholder: true` in production** — `placeholder: true` renders an empty dashboards shell with no data; replace it with a `dashboards` binding before deploying.
2. **Not providing `_designOptions`** — `_designOptions` is required by the designer; always include it when inserting via the create command, even if the arrays are empty.
3. **Binding `dashboards` to a plain array instead of a `BaseViewModelCollection`** — `BaseViewModelCollection` supports reactive updates; a plain `DashboardItem[]` works for static lists but won't reflect server-side changes.
4. **Omitting `selectedDashboard`** — without `selectedDashboard`, the component always selects the first dashboard on load; bind it to persist the user's last selection.
5. **Setting `canManage: true` without checking rights** — the component auto-initializes `canManage` from the `CanManageAnalytics` operation right when unset; only override it explicitly when you want to force-enable/disable the management UI.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Dashboards"`, unique `name`, valid `parentName`, `propertyName: "items"`.
- [ ] Either `placeholder: true` (designer placeholder) or `dashboards` bound to a collection.
- [ ] `_designOptions` provided with `dependencies: []` and `filters: []`.
- [ ] If `selectedDashboard`, `filter`, or `hierarchicalFilter` need page-state binding, corresponding `$-prefix` attributes declared in `viewModelConfigDiff`.
- [ ] If `createDashboard`, `dashboardsOutdated`, or `favoriteDashboardChanged` events are needed, matching `handlers` entries exist.
