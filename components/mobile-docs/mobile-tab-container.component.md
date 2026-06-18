# How to Add a Tab Container (`crt.TabContainer`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.TabContainer` into a mobile page schema.
>
> Individual tab body inside a `crt.TabPanel`; the `caption` property becomes the tab-strip label.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: crt.TabPanel
- **Typical children**: any view elements

---

## 1. Mental model

`crt.TabContainer` is always a direct child of `crt.TabPanel`. It provides the body content shown
when its tab is active. The `caption` property controls the visible label in the tab strip.
`items` holds the body content (fields, grids, expansion panels, etc.).

`tools` is an optional header-level slot for action controls (e.g. buttons or icons) that appear
next to the tab caption.

---

## 2. Clio operation

```jsonc
{
  "operation": "insert",
  "name": "InfoTab",
  "values": {
    "type": "crt.TabContainer",
    "caption": "$Resources.Strings.InfoTab_caption",
    "items": []
  },
  "parentName": "Tabs",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.TabContainer` are in
`ComponentRegistry.json` under `componentType: "crt.TabContainer"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Key inputs:

| Property | Type | Description |
|---|---|---|
| `caption` | string | Tab label in the strip. Use a `$Resources.Strings.<key>` reference. |
| `items` | array | Body content shown when the tab is active. |
| `tools` | array | Action controls rendered next to the tab caption in the header. |

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `iconPosition` | string | Icon position relative to the tab caption. Values: `left-icon`, `right-icon`, `only-icon`, `only-text`. |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "DetailsTab",
  "values": {
    "type": "crt.TabContainer",
    "caption": "$Resources.Strings.DetailsTab_caption",
    "items": []
  },
  "parentName": "PageTabs",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

- **`parentName` must point to a `crt.TabPanel`**: inserting a `crt.TabContainer` into any other
  container type breaks the tab rendering.
- **`caption` localization**: use `$Resources.Strings.<key>` with a matching `defaultLocalizableStrings`
  entry in the operation so the string is created automatically.
- **`tools` vs `items`**: action buttons that should appear in the tab header go in `tools`, not
  `items`. Putting them in `items` renders them inside the tab body instead.
