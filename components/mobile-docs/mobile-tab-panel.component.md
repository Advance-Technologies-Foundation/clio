# How to Add a Tab Panel (`crt.TabPanel`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.TabPanel` into a mobile page schema.
>
> Top-level tab-strip container that holds one or more `crt.TabContainer` children and lets users switch between them by tapping the strip.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: Scaffold items, FlexContainer items
- **Typical children**: crt.TabContainer

---

## 1. Mental model

`crt.TabPanel` is the outer wrapper for a tabbed layout. It renders the tab-header strip and
manages which tab is currently visible. Each direct child must be a `crt.TabContainer`. The
`caption` of each `crt.TabContainer` becomes the tab label in the strip.

---

## 2. Clio operation

```jsonc
{
  "operation": "insert",
  "name": "Tabs",
  "values": {
    "type": "crt.TabPanel",
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.TabPanel` are in
`ComponentRegistry.json` under `componentType: "crt.TabPanel"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Key inputs (inherited from base class; set in `values`):

| Property | Type | Description |
|---|---|---|
| `items` | array | Child `crt.TabContainer` configs. |
| `isScrollable` | boolean | Whether the tab strip scrolls horizontally when tabs overflow. |
| `selectedTabName` | string | Data binding for the currently active tab name. |
| `mode` | string | `tab` (default) or `toggle` display style. |
| `allowToggleClose` | boolean | Toggle-mode only: allows de-selecting the active toggle. |
| `styleType` | string | Visual style token (e.g. `default`). |

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `scrollable` | boolean | Whether tab headers are horizontally scrollable when they overflow. |
| `allowToggleClose` | boolean | Allow toggling a tab closed by tapping the active tab again. |
| `styleType` | string | Visual style variant for the tab strip. |
| `bodyBackgroundColor` | string | Background color for the tab body area. |
| `selectedTabTitleColor` | string | Color of the selected tab title text. |
| `tabTitleColor` | string | Color of unselected tab title text. |
| `underlineSelectedTabColor` | string | Underline color for the active tab indicator. |
| `headerBackgroundColor` | string | Background color for the tab header row. |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "PageTabs",
  "values": {
    "type": "crt.TabPanel",
    "mode": "tab",
    "items": [
      {
        "name": "Tab1",
        "type": "crt.TabContainer",
        "caption": "$Resources.Strings.Tab1_caption",
        "items": []
      }
    ]
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

- **Direct children must be `crt.TabContainer`**: placing other element types directly in `items`
  causes rendering errors. Always wrap content in a `crt.TabContainer` first.
- **`caption` must be a resource string**: use `$Resources.Strings.<key>` form to support
  localization. A plain string literal will not be translated.
- **`selectedTabName` binding**: if you bind `selectedTabName` to an attribute, ensure the
  attribute is initialized to one of the existing tab names, otherwise no tab is selected on load.
