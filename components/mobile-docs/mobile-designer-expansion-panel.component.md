# How to Add an Expansion Panel (`crt.ExpansionPanel`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.ExpansionPanel` into a mobile page schema.
>
> Collapsible panel with a header title and optional tool controls; body content is shown or hidden when the user taps the toggle.

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: Scaffold items, GridContainer items, FlexContainer items, TabContainer items
- **Typical children**: any view elements (in `items`); action controls in `tools`

---

## 1. Mental model

`crt.ExpansionPanel` renders a header bar with a `title` and an expand/collapse toggle. When
expanded, the `items` slot is visible. The `tools` slot holds action controls (e.g. an upload
button) that are always visible in the header regardless of the expanded state.

On mobile, the toggle uses a material-style chevron and the panel header spans the full width.

---

## 2. Clio operation

```jsonc
{
  "operation": "insert",
  "name": "InfoPanel",
  "values": {
    "type": "crt.ExpansionPanel",
    "title": "Details",
    "expanded": true,
    "items": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.ExpansionPanel` are in
`ComponentRegistry.json` under `componentType: "crt.ExpansionPanel"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

Key inputs:

| Property | Type | Description |
|---|---|---|
| `title` | string | Panel header label (plain string or resource reference). |
| `expanded` | boolean | Whether the panel body is visible by default. |
| `items` | array | Body content shown when the panel is expanded. |
| `tools` | array | Action controls rendered in the header (always visible). |

---

## 4. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "AttachmentsPanel",
  "values": {
    "type": "crt.ExpansionPanel",
    "title": "Attachments",
    "expanded": false,
    "items": [],
    "tools": []
  },
  "parentName": "Scaffold",
  "propertyName": "items",
  "index": 0
}
```

---

## 5. Common pitfalls

- **`tools` vs `items`**: action buttons meant for the header (e.g. an upload trigger) must go in
  `tools`, not `items`. Placing them in `items` hides them when the panel is collapsed.
- **`expanded: false` hides body on load**: if you need data to be visible immediately, set
  `"expanded": true`. The default is `false`.
- **`title` localization**: for multilingual pages, use a `$Resources.Strings.<key>` reference
  instead of a plain string literal.
