# How to Add a Router Outlet (`crt.RouterOutlet`) to a Freedom UI Page

> Audience: code agent inserting `crt.RouterOutlet` into a Creatio Freedom UI page schema.
>
> `crt.RouterOutlet` is the top-level navigation host for a Freedom UI shell. It listens to the platform
> router (`CrtRouter`), loads the appropriate schema into an embedded `crt.SchemaOutlet`, manages
> navigation history, and emits `contentBackgroundChanged` when the loaded schema changes its background
> requirement. It is typically placed once in the application shell (e.g. `MainShell`), not in regular
> record pages.

## Metadata

- **Category**: navigation
- **Container**: no (it manages its own embedded `CrtSchemaOutletComponent`)
- **Parent types**: root page container, `crt.GridContainer`
- **Typical children**: none (schema is loaded dynamically into the outlet)

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.RouterOutlet"` and optionally wired `contentBackgroundChanged`. **Always present.** |
| 2 | `handlers` (optional) | A handler for `contentBackgroundChanged` if the shell needs to react to background state changes. |

No `modelConfigDiff` or `viewModelConfigDiff` is needed for basic usage.

### 1.1 Naming convention

```
RouterOutlet_<id>    // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "RouterOutlet_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.RouterOutlet",
    "contentBackgroundChanged": {
      "request": "crt.ContentDisplayedStateChangedRequest"
    },
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Handle background state changes

```jsonc
{
  "request": "crt.ContentDisplayedStateChangedRequest",
  "handler": async (request, next) => {
    // request.parameters contains the boolean background state
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.RouterOutlet` are in `ComponentRegistry.json` under `componentType: "crt.RouterOutlet"`. This
guide covers only the assembly mechanics.

**Outputs:**

| Output | Type | Description |
|---|---|---|
| `contentBackgroundChanged` | `RequestBindingConfig` | Emits `true`/`false` when the loaded schema changes its background requirement. |

---

## 5. Copy-paste minimal example

Real PackageStore usage from `MainShell`:

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "RouterOutlet_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.RouterOutlet",
    "contentBackgroundChanged": {
      "request": "crt.ContentDisplayedStateChangedRequest"
    }
  }
}
```

---

## 7. Common pitfalls

1. **Using inside a record page** — `crt.RouterOutlet` is a shell-level component; it registers itself in the `CrtZoneService` under the `NAVIGATION_ZONE_NAME` key. Embedding multiple instances causes navigation conflicts.
2. **Skipping the `contentBackgroundChanged` binding** — if the shell needs to apply a dark/light background based on the loaded page, this output must be wired; otherwise the shell background stays static.
3. **Expecting inputs to drive navigation** — `crt.RouterOutlet` has no `@CrtInput` properties; navigation is driven entirely by `CrtRouter` service calls from other components or handlers.
4. **`layoutConfig` mismatch** — when placed inside a `crt.GridContainer`, the `layoutConfig` must fill the intended cell span; typically `colSpan` and `rowSpan` should cover the full main content area.
5. **No history support in non-`MainShell` contexts** — the outlet's `RouterOutletHistory` uses the router's navigation stack. Outside the main shell, back-navigation behavior may be undefined.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.RouterOutlet"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `contentBackgroundChanged` wired if the shell reacts to background changes.
- [ ] Only one `crt.RouterOutlet` per shell schema (avoid duplicate navigation zones).
- [ ] `layoutConfig` spans the full main content area when parent is a `crt.GridContainer`.
