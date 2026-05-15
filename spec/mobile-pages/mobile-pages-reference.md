# Mobile Freedom UI Pages — Verified Source of Truth

> **Audience**: AI agents (clio MCP) creating or editing mobile Freedom UI page schemas.
>
> **Last verified**: 2026-05-14
>
> **Verification sources**: clio source code (C# — `SchemaValidationService`, `PageUpdateCommand`, `PageSchemaBodyParser`), creatio-ui source (TypeScript), `CrtUIPlatform` package schemas, Creatio Academy, Creatio 8.x live environment.

---

## 1. Schema Identity

A schema is a mobile Freedom UI page when:

| Property | Value |
|---|---|
| `schemaType` (numeric) | `10` (`ClientUnitSchemaType.MobileSchema`) |
| `schemaGroup` | `MobilePage` |
| Designer route | `MobilePageDesigner` |
| Runtime | Native mobile app (iOS / Android) |
| Available since | Creatio 8.3.2 |

Web Freedom UI pages use `schemaType = 9` (`AngularSchema`) and `schemaGroup = Page`.

---

## 2. Template Hierarchy

All templates live in the `CrtUIPlatform` package. Two independent root schemas; neither is the parent of the other.

```
BlankMobilePageTemplate  (478ab83b-527b-4830-b2b8-2206bb9bf283)
    Standalone root. Bare crt.Scaffold with empty leading/actions/items.
    Also the value of the BASE_MOBILE_TEMPLATE_ANGULAR constant (creatio-ui designer).

BaseMobileTemplate       (1d1942c9-6993-41c6-9eda-7b697acca221)
    Separate root. Adds $PageTitle and a CloseButton to the Scaffold,
    plus a MainContainer (crt.GridContainer).
    ├── BaseMobilePageTemplate  (a6a776d7-4415-4726-bf63-a1f19edef6e7)
    │       Record / form pages.
    │       Adds: CancelButton, SaveButton, FloatingActionButton (copy/delete menu),
    │             AreaProfileContainer (crt.GridContainer, color=primary).
    │       └── MobilePageWithTabsFreedomTemplate  (019c694a-1a3b-4812-b2e5-219b326061ae)
    │               Tabbed record pages.
    │               Adds: crt.TabPanel with GeneralInfoTab, FeedTab, AttachmentsTab.
    └── BaseMobileListTemplate  (011e7dda-a763-4535-9b9a-e09eddd047be)
            List / section pages.
            Adds: search button, FloatingActionButton (create record),
                  HeaderContainer (filters/sort/folder tree),
                  ListContainer with crt.List bound to $Items.
```

### Template selection guidance

| Use case | Recommended template |
|---|---|
| Custom / blank page | `BlankMobilePageTemplate` |
| Record (form) page | `BaseMobilePageTemplate` |
| Record page with tabs, feed, attachments | `MobilePageWithTabsFreedomTemplate` |
| List / section page | `BaseMobileListTemplate` |

---

## 3. Schema Body Format

Mobile page bodies are **plain JSON** — not AMD `define(...)` JavaScript modules.

```json
{
  "viewConfigDiff": [],
  "viewModelConfigDiff": [],
  "modelConfigDiff": []
}
```

**No other top-level sections exist in mobile page bodies.** `handlers`, `converters`, and `validators` are web-only (AMD) sections and cannot appear in mobile pages. Specifically, it is not possible to *define* a custom converter inside a mobile page body. However, existing OOTB converters can be *referenced* as inline binding expressions within `viewConfigDiff` values — for example, `"visible": "$HasUnsavedData | crt.InvertBooleanValue"` or `"visible": "$CardState | crt.IsEqual : 'edit'"`. These are expression strings, not entries in any `converters` section.

### Section semantics

| Section | Purpose |
|---|---|
| `viewConfigDiff` | Visual tree operations: add/merge/move/remove UI elements |
| `viewModelConfigDiff` | View-model patches: attributes, bindings, resource strings |
| `modelConfigDiff` | Data-source patches: `primaryDataSourceName`, `dataSources`, dependencies |

### Diff operations

Each array entry has an `"operation"` field:

| Operation | Meaning |
|---|---|
| `"insert"` | Add a new element. Requires `name`, `values`, `parentName`, `propertyName`, `index`. |
| `"merge"` | Patch an existing element by name (viewConfigDiff) or by path (viewModelConfigDiff / modelConfigDiff). |
| `"move"` | Move an existing element to a different position. |
| `"remove"` | Remove an element by name. |

---

## 4. `crt.Scaffold` — Mandatory Root Element

All four verified mobile templates use exactly one `crt.Scaffold` insert at the top level of `viewConfigDiff`. All other content goes inside it. Treat this as a verified current template convention rather than a guaranteed constraint for every future mobile runtime.

```json
{
  "operation": "insert",
  "name": "Scaffold",
  "values": {
    "type": "crt.Scaffold",
    "title": "$PageTitle",
    "leading": [],
    "actions": [],
    "floatAction": null,
    "items": []
  },
  "index": 0
}
```

| Scaffold property | Purpose |
|---|---|
| `title` | Page title (typically `"$PageTitle"` attribute reference) |
| `leading` | Left-side navigation items (back/cancel buttons) |
| `actions` | Right-side action items (save button, search button) |
| `floatAction` | Single floating action button object (`crt.FloatingActionButton`) |
| `items` | Page body — array of container/component references |

---

## 5. Differences from Web Pages

Verified from creatio-ui mobile page designer features service:

| Feature | Web | Mobile |
|---|---|---|
| Preview button | Shown | **Hidden** |
| Appearance settings panel | Shown | **Hidden** |
| Advanced panel | Shown | **Hidden** |
| Page parameters in data-source tools | Visible | **Hidden** |
| Multi-data-source | Enabled | **Disabled** — one data source per page |
| Masked fields | Supported | **Not supported** (`isMaskedPropertyVisible = false`) |
| Source code button | Available | Available (not hidden) |
| Unsupported data types | — | `SECURE_TEXT`, `Color`, `FILE` |
| Body format | AMD JS module | **Plain JSON** |
| Runtime | Browser | Native mobile app |
| Schema group | `Page` (and others) | `MobilePage` |
| Related pages addon | `AddonName.RelatedPage` | `AddonName.MobileRelatedPage` |
| Page properties panel | `crt.PagePropertiesPanel` | `crt.MobilePagePropertiesPanel` |
| Component registries | Web registries | **Separate mobile registries** |

**Do not mix web-only components into mobile pages.** A component type like `crt.Button` exists in both registries but may have different available properties.

---

## 6. Adaptive Breakpoints

Mobile pages support 3 viewport breakpoints, defined in `MobileBreakpointType`:

| Value | Viewport |
|---|---|
| `"small"` | Phone portrait — default canvas in designer |
| `"medium"` | Phone landscape / tablet portrait |
| `"large"` | Tablet landscape |

`crt.GridContainer` supports an `adaptive` property for per-breakpoint column overrides:

```json
{
  "type": "crt.GridContainer",
  "columns": "1fr",
  "adaptive": {
    "small": { "columns": "1fr" },
    "medium": { "columns": "1fr 1fr" },
    "large": { "columns": "1fr 1fr 1fr" }
  }
}
```

---

## 7. Available Components (Category Overview)

Mobile components are registered through mobile-specific decorators (`@CrtMobileViewElement`, `@CrtMobileInterfaceDesignerItem`) — separate from the web registry. Only components registered for mobile are valid in a mobile page.

**Input / form**
`crt.Input`, `crt.NumberInput`, `crt.EmailInput`, `crt.WebInput`, `crt.PhoneInput`,
`crt.Checkbox`, `crt.ComboBox`, `crt.DateTimePicker`, `crt.ImageInput`, `crt.RichTextEditor`,
`crt.Slider`, `crt.Toggle`, `crt.BarcodeScanner`

**Layout and structure**
`crt.Scaffold` (root — always required), `crt.GridContainer`, `crt.FlexContainer`,
`crt.TabPanel`, `crt.TabContainer`, `crt.ExpansionPanel`, `crt.Label`, `crt.Button`,
`crt.FloatingActionButton`, `crt.Menu`, `crt.ButtonToggleGroup`

**Data and collections**
`crt.List`, `crt.FileList`, `crt.Feed`, `crt.Gallery`, `crt.Timeline`

**Navigation / filtering**
`crt.FolderTree`, `crt.FolderTreeActions`, `crt.QuickFilter`, `crt.QuickFilterGroup`, `crt.Sort`

**Profile and special**
`crt.CompactProfile`, `crt.CommunicationOptions`, `crt.EntityStageProgressBar`

**Widgets**
`crt.IndicatorWidget`, `crt.ChartWidget`

---

## 8. clio MCP Workflow

### List available mobile templates

```
Tool: list-page-templates
Parameters:
  schema-type: "mobile"          ← also accepts "10" or "mobilepage"
  environment-name: <env>
```

Returns the 4 templates from Section 2 (names and UIds). Use a returned name as the `template` parameter for `create-page`.

### Create a mobile page

```
Tool: create-page
Parameters:
  schema-name:      "UsrMyApp_MobileFormPage"   (use env SchemaNamePrefix)
  template:         "BaseMobilePageTemplate"     (name from list-page-templates)
  package-name:     "UsrMyApp"
  environment-name: <env>
  caption:          "My Mobile Page"            (optional)
  entity-schema-name: "UsrMyEntity"             (optional)
```

Same tool as web pages — mobile vs web is determined by the chosen template. The response includes `schemaType: 10` confirming the page is mobile.

> **Note**: Standalone mobile page creation via `create-page` is not the primary intended workflow. Mobile pages are automatically generated when creating an app section with `--with-mobile-pages true` (see Section 9). Use `create-page` for standalone mobile pages only when adding a mobile page outside the standard app creation flow.

### Read a mobile page body

**Preferred** — writes `body.js` (plain JSON) and `bundle.json` to `.clio-pages/{schema-name}/`:

```
Tool: get-page
Parameters:
  schema-name:      "UsrMyApp_MobileFormPage"
  environment-name: <env>
```

`bundle.json` shows all elements inherited from parent templates — read it before inserting new components to avoid duplicating inherited nodes (see Rule #5). Both files are reliable for mobile pages.

**Alternative** — returns only the raw body without `bundle.json`:

```
Tool: get-client-unit-schema
Parameters:
  schema-name:      "UsrMyApp_MobileFormPage"
  environment-name: <env>
```

Use `get-client-unit-schema` only when you need the raw body quickly and do not need bundle context.

### Update a mobile page body

```
Tool: update-page
Parameters:
  schema-name:      "UsrMyApp_MobileFormPage"
  body:             "<plain JSON string>"
  environment-name: <env>
```

Use `update-page` (or `sync-pages` for batches) — both tools detect mobile JSON bodies automatically, skip AMD marker validation, and actively reject disallowed sections (`handlers`, `validators`, `converters`). The standard `get-page → update-page` workflow applies identically to web and mobile pages.

### Identify mobile pages in list-pages output

`list-pages` returns a `schema-type` field alongside `SchemaName`, `UId`, `PackageName`, and `ParentSchemaName`. The value is `"mobile"` for schemaType=10 pages, `"web"` for schemaType=9, and `"unknown"` otherwise. Use this field directly — no parent name inference is needed.

---

## 9. App Section Creation

`create-app-section` controls mobile page generation via `--with-mobile-pages`:

| `--with-mobile-pages` | Behavior |
|---|---|
| `true` (default since 8.3.2) | Backend creates web pages **and** mobile pages for the section |
| `false` | Web pages only (sends `clientTypeId = 195785B4-F55A-4E72-ACE3-6480B54C8FA5`) |

Since Creatio 8.3.2, creating a new app or section automatically generates a mobile form page and a mobile list page alongside the web pages.

> **Mobile page visibility requires app/section linkage.** A mobile page schema existing in the environment is not enough by itself to make it visible in the mobile application. The page must be created or linked through the supported app/section creation flow.

---

## 10. Feature Flags

| Flag | Effect |
|---|---|
| `UseMobilePageDesigner` | When enabled: mobile pages (schemaType=10) appear in the application page list; the Mobile Pages tab appears in the page template picker; mobile schema types are shown in the workspace dialog |
| `DisableCanvasSizeSwitcher` | Hides the phone/tablet size switcher in the mobile designer canvas |
| `EnableMobileCanvasToolbar` | Enables the device orientation rotation button in the canvas toolbar |

---

## 11. AI Generation Rules

1. **Always use a mobile template** from `list-page-templates --schema-type mobile`. Never create a mobile page from a web template.
2. **Body is plain JSON** — no `define(...)` wrapper, no `handlers`, `converters`, or `validators` sections.
3. **`crt.Scaffold` is always the root** — insert it first in `viewConfigDiff`.
4. **One data source per page** — multi-data-source is disabled in the mobile designer.
5. **Diff operations are additive** — many nodes come from parent templates via `bundle.json`. Read the bundle before adding elements; do not duplicate what's already inherited.
6. **Use `get-page` to read and `update-page` (or `sync-pages` for batches) to write** mobile page bodies. Both detect mobile JSON bodies automatically and skip AMD marker validation. `bundle.json` from `get-page` is reliable for mobile pages.
7. **Mobile component registry is separate** — verify a component exists on mobile before using it. Do not assume all `crt.*` web components are available on mobile.
8. **`Boolean` → `Toggle`** — the mobile designer maps Boolean data type to `crt.Toggle`, not `crt.Checkbox`.
9. **Resources for labels** — use `"$Resources.Strings.ElementName_label"` or `"#ResourceString(Key)#"` for user-visible text, consistent with what the templates use.

