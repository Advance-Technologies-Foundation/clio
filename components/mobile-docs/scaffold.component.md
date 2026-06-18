# How to Add / Patch a Scaffold (`crt.Scaffold`) on a Mobile Page

> Audience: Clio AI agent building or modifying a mobile page schema.
>
> The Scaffold is the mandatory root element of every mobile page. It wraps the navigation bar
> (title, leading, actions) and the body (`items`). Every template ships with exactly one Scaffold.
> **Never insert a second Scaffold — merge into the existing one instead.**

## Metadata

- **Category**: containers
- **Container**: yes
- **Parent types**: (root — has no parent container)
- **Typical children**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabPanel`

---

## 1. Mental model

The Scaffold has four named content slots you patch via `merge`:

| Slot | Role |
|---|---|
| `items` | Main page body — place all layout containers here |
| `leading` | Left side of the navigation bar (back / cancel buttons) |
| `actions` | Right side of the navigation bar (save / action buttons) |
| `header` | Optional header area rendered above `items` |

Additional runtime-only properties (not Angular `@Input` bindings):

| Property | Type | Description |
|---|---|---|
| `floatAction` | object | Single floating action button (`crt.FloatingActionButton`). Set to `null` to hide. |
| `fullScreen` | boolean | Full-screen mode — hides the native navigation chrome. |
| `useSurface` | boolean | Use the surface background color for the page body. |
| `leadingWidth` | number | Override the width of the leading (left) navigation area. |

---

## 2. Clio operation

Always use `operation: "merge"` with the Scaffold's preset name:

```jsonc
{
  "operation": "merge",
  "name": "Scaffold",
  "values": {
    "items": [{ "name": "MainContainer" }]
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.Scaffold` are in
`ComponentRegistry.json` under `componentType: "crt.Scaffold"`. This guide covers the assembly
mechanics — what to put where in Clio mobile page operations.

---

## 4. Copy-paste minimal example

```jsonc
// Patch the Scaffold to set the page title and add a body container
{
  "operation": "merge",
  "name": "Scaffold",
  "values": {
    "title": "$PageTitle",
    "items": [{ "name": "MainContainer" }]
  }
}
```

---

## 5. Common pitfalls

**Use `merge`, never `insert`.** The Scaffold is pre-created by the page template.
Inserting a new `crt.Scaffold` element results in two root elements and breaks rendering.

**Preserve existing `items` references.** When merging, include names of any
containers that already exist in `items` so they are not discarded.

**`floatAction` takes an object, not an array.** Use `"floatAction": null` to explicitly remove
a floating action button rather than omitting the key.

---

## 6. Schema-only child: `crt.FloatingActionButton`

`crt.FloatingActionButton` has no Angular component (no `@CrtMobileViewElement`). It is configured via the `floatAction` property of the Scaffold using a direct object value:

```jsonc
{
  "operation": "merge",
  "name": "Scaffold",
  "values": {
    "floatAction": {
      "type": "crt.FloatingActionButton",
      "icon": "add-button-icon",
      "clicked": { "request": "crt.CreateRecordRequest" },
      "menuItems": []
    }
  }
}
```

| Property | Type | Description |
|---|---|---|
| `type` | string | Must be `crt.FloatingActionButton`. |
| `icon` | string | Icon identifier for the FAB button. |
| `clicked` | object | Request descriptor fired on tap. |
| `menuItems` | array | Child `crt.MenuItem` entries for FAB dropdown menu. |
| `visible` | string | Visibility binding. |
| `color` | string | Color scheme. |

To remove the FAB, set `"floatAction": null`.
