# How to Add a Sidebar Container (`crt.SidebarContainer`) to a Freedom UI Page

> Audience: code agent inserting `crt.SidebarContainer` into a Creatio Freedom UI page schema.
>
> `crt.SidebarContainer` is the application-shell sidebar host. It reads its sidebar configuration from
> `SidebarsConfigProvider`, renders grouped and standalone sidebar panels, and handles user-resizable
> width. It is placed once in the application shell (e.g. `MainShell`) identified by a specific
> `uniqueName` value — not in regular record pages.

## Metadata

- **Category**: containers
- **Container**: yes (renders sidebar panels defined in `SidebarsConfig`)
- **Parent types**: root page container, `crt.GridContainer`
- **Typical children**: none declared in schema (sidebar panels are registered programmatically)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.SidebarContainer"` and a `uniqueName`. **Always present.** |

No `modelConfigDiff`, `viewModelConfigDiff`, or `handlers` are needed. The sidebar content is
determined by `SidebarsConfigProvider`, not by the schema diff.

### 1.1 Naming convention

```
SidebarContainer_<id>    // view element name; <id> = any short unique slug
```

**Reserved `uniqueName` values** (the component uses these to activate specific behaviors):

| `uniqueName` value | Position |
|---|---|
| `"SidebarContainer"` | Right sidebar (end of page) |
| `"PageStartSidebarContainer"` | Left sidebar (start of page) |

Any other value disables the sidebar-width tracking and `openedRootSidebar$` subscription.

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "SidebarContainer_right",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SidebarContainer",
    "uniqueName": "SidebarContainer",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.SidebarContainer` are in `ComponentRegistry.json` under `componentType: "crt.SidebarContainer"`.
This guide covers only the assembly mechanics.

**Key input:**

| Input | Type | Description |
|---|---|---|
| `uniqueName` | `string` | Position identifier for the sidebar. Use `"SidebarContainer"` (right) or `"PageStartSidebarContainer"` (left) to activate tracking. |

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — right sidebar
{
  "operation": "insert",
  "name": "SidebarContainer_right",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SidebarContainer",
    "uniqueName": "SidebarContainer",
    "visible": true
  }
}
```

```jsonc
// viewConfigDiff entry — left sidebar
{
  "operation": "insert",
  "name": "SidebarContainer_left",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SidebarContainer",
    "uniqueName": "PageStartSidebarContainer",
    "visible": true
  }
}
```

---

## 7. Common pitfalls

1. **Using inside a record page** — `crt.SidebarContainer` depends on `SidebarsConfigProvider` which is only provided in the application shell. Using it outside the shell renders an empty, unstyled container.
2. **Wrong `uniqueName`** — only the two reserved values activate sidebar-width tracking and resize behavior. A custom name is valid but the sidebar stays at its initial size and `openedRootSidebar$` never emits.
3. **Two containers with the same `uniqueName`** — placing two sidebars with the same name causes them to read and write the same width state, producing a visual conflict.
4. **Resizable sidebar without `isSizeAdjustmentAllowed`** — `useResizableMode` is toggled by `SidebarStateService.isSizeAdjustmentAllowed(code)`; if the service returns `false` for the sidebar code, the resize handle won't appear.
5. **ARIA attributes** — the component hardcodes `aria-label="Side panel"` and `role="complementary"` via `HostBinding`. Override them at the Angular template level if the panel serves a different semantic role.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.SidebarContainer"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `uniqueName` set to `"SidebarContainer"` or `"PageStartSidebarContainer"` for shell sidebars.
- [ ] Component placed in the application shell, not in a record page.
- [ ] Only one sidebar per `uniqueName` per shell.
- [ ] `layoutConfig` present when parent is a `crt.GridContainer`.
