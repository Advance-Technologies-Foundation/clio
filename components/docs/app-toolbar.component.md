# How to Add an App Toolbar (`crt.AppToolbar`) to a Freedom UI Page

> Audience: code agent inserting `crt.AppToolbar` into a Creatio Freedom UI page schema.
> Renders the application header toolbar with global-search, quick-add, navigation-panel toggle,
> help, and communication-indicator buttons; used once at the shell level.

## Metadata
- **Category**: navigation
- **Container**: no
- **Parent types**: root shell `crt.FlexContainer` (banner role)
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.AppToolbar"` and output bindings. **Always present.** |
| 2 | `viewModelConfigDiff` | Attributes for `navigationPanelVisible`, `communicationItemsInfo`, etc. |

`crt.AppToolbar` has no create command and is not a designer-palette item. It is configured once in the
shell schema and relies on `RightsService`, `LicenseService`, and feature values to auto-show/hide
individual buttons.

### 1.1 Naming convention
```
AppToolbar_<id>    // view element name; "ShellHeaderToolbar" in real schemas
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ShellHeaderToolbar",
  "values": {
    "type": "crt.AppToolbar",
    "createRecord": {
      "request": "crt.CreateRecordRequest",
      "params": {
        "entityName": "@event.entityName",
        "entityPageName": "@event.entityPageName",
        "defaultValues": "@event.defaultValues"
      }
    },
    "navigationPanelVisibleChange": {
      "request": "crt.NavigationPanelChangeVisibleRequest",
      "params": {
        "isVisible": "@event"
      }
    },
    "navigationPanelVisible": "$WorkplaceNavigationPanelVisibleAttribute",
    "communicationItemsInfo": "$ActiveCommunicationsInfo",
    "communicationIndicatorClicked": {
      "request": "crt.CommunicationIndicatorClickedRequest",
      "params": {
        "communicationItemInfo": "@event"
      }
    }
  },
  "parentName": "ShellHeader",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.AppToolbar` are in `ComponentRegistry.json` under `componentType: "crt.AppToolbar"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`CommunicationItemInfo` is defined in the registry `references.typeDefinitions`:
```ts
interface CommunicationItemInfo {
  displayValue: string;           // name of the call/chat contact
  type: 'call' | 'consultation';  // communication type
  communicationStartedOn: Date;   // timestamp
  primaryImageValue?: string;     // avatar image ID
}
```

`ToggleValue` (used in `currentRightPanelTab`, `currentLeftPanelTab`, `rightPanelButtonClicked`,
`leftPanelButtonClicked`) is a string tab identifier matching the sidebar panel's toggle group values.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real PackageStore usage (MainShell)
{
  "operation": "insert",
  "name": "ShellHeaderToolbar",
  "values": {
    "type": "crt.AppToolbar",
    "createRecord": {
      "request": "crt.CreateRecordRequest",
      "params": {
        "entityName": "@event.entityName",
        "entityPageName": "@event.entityPageName",
        "defaultValues": "@event.defaultValues"
      }
    },
    "navigationPanelVisibleChange": {
      "request": "crt.NavigationPanelChangeVisibleRequest",
      "params": { "isVisible": "@event" }
    },
    "navigationPanelVisible": "$WorkplaceNavigationPanelVisibleAttribute",
    "communicationItemsInfo": "$ActiveCommunicationsInfo",
    "communicationIndicatorClicked": {
      "request": "crt.CommunicationIndicatorClickedRequest",
      "params": { "communicationItemInfo": "@event" }
    }
  },
  "parentName": "ShellHeader",
  "propertyName": "items",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Using outside a shell context** — `crt.AppToolbar` injects `RightsService`, `LicenseService`, and `UserInfo`; these are provided at the shell/app level. Embedding on a regular page will fail.
2. **Not wiring `createRecord`** — the quick-add menu button fires `createRecord` on item selection; without a handler the user can open the menu but nothing happens on click.
3. **`navigationPanelVisibleChange` not handled** — clicking the navigation-panel toggle emits this output; without a handler the sidebar never opens/closes.
4. **Setting deprecated outputs (`rightPanelButtonClicked`, `leftPanelButtonClicked`)** — these work only in legacy sidebar mode. For the current sidebar, use the Freedom UI sidebar component's own bindings.
5. **`communicationItemsInfo` left as empty array** — the communication indicators are always hidden when the array is empty, even if telephony/chat licences are active.
6. **`runProcessButtonVisible: false` is permanent** — once set to `false` in the view config, the Run Process button is always hidden regardless of rights or feature flags.
7. **Only one per shell** — placing multiple `crt.AppToolbar` instances in the same shell causes duplicate headers.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AppToolbar"`, unique `name`, valid `parentName`.
- [ ] `createRecord` output wired to `crt.CreateRecordRequest` with entity-name params.
- [ ] `navigationPanelVisibleChange` output wired to toggle the navigation panel attribute.
- [ ] `navigationPanelVisible` bound to the attribute controlling navigation panel visibility.
- [ ] `communicationItemsInfo` bound if telephony/chat communication indicators are needed.
- [ ] `communicationIndicatorClicked` output wired if communication indicator navigation is required.
- [ ] Used only at the shell level — not on individual page schemas.
